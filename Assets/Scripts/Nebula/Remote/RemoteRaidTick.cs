using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Remote
{
    /// <summary>
    /// VS-016A §8: the non-Full-system equivalent of <see cref="RaidScheduler"/> —
    /// same phase machine (None→Scheduled→Warning→Spawning→Active→RetreatCleanup→Resolved),
    /// same pure decision resolvers (RaidComposer/EconomicValueResolver/WarningResolver/
    /// RaidOutcomeResolver/RaidSalvageResolver), same global Major-Raid slot
    /// (<see cref="RaidCampaignState.HasGlobalMajorRaidActive"/>) — only the data
    /// source differs (SystemDelta via <see cref="RemoteRaidContext"/> instead of
    /// live ExtractionState/EnemyAgent/CombatEncounterDirector), and enemies are
    /// <see cref="RemoteEnemyRuntime"/> instead of real GameObjects. Never runs for
    /// the currently-Full system (that stays on RaidScheduler) -- the caller is
    /// responsible for only ticking a system through exactly one of the two paths.
    /// </summary>
    public static class RemoteRaidTick
    {
        public static void Tick(RemoteRaidContext ctx, float dt)
        {
            if (ctx == null) return;
            ThreatPressureState pressure = ctx.Pressure;
            RaidCampaignState campaign = CombatCampaignState.RaidCampaign;

            // Mirrors CombatEncounterDirector.Update's unconditional per-frame timers -- a remote
            // system has no separate director, so RemoteRaidTick itself owns grace/foreign-presence/
            // active-raid-clock/wreckage-aging exactly like the live director already does every frame.
            pressure.TickGrace(dt);
            pressure.TickForeignPresence(dt);
            pressure.TickActive(dt);
            TickWreckageAging(ctx, dt);

            switch (pressure.Phase)
            {
                case RaidPhase.None: TickIdle(ctx, pressure, campaign, dt); break;
                case RaidPhase.Scheduled: pressure.SetPhase(RaidPhase.Warning); break;
                case RaidPhase.Warning: if (pressure.TickWarning(dt)) pressure.SetPhase(RaidPhase.Spawning); break;
                case RaidPhase.Spawning:
                    SpawnComposition(ctx, pressure);
                    if (pressure.Phase == RaidPhase.Spawning) pressure.SetPhase(RaidPhase.Active);
                    break;
                case RaidPhase.Active: TickActive(ctx, pressure, dt); break;
                case RaidPhase.RetreatCleanup: pressure.SetPhase(RaidPhase.Resolved); break;
                case RaidPhase.Resolved: FinalizeRaid(ctx, pressure, campaign); break;
            }
        }

        /// <summary>Mirrors CombatEncounterDirector.Update's own wreckage-aging loop -- same field, same expiry rule, just decremented directly on the DTO instead of round-tripping through a live CombatWreckageState.</summary>
        private static void TickWreckageAging(RemoteRaidContext ctx, float dt)
        {
            List<CombatWreckageDto> wreckage = ctx.Delta.combatWreckage;
            for (int i = wreckage.Count - 1; i >= 0; i--)
            {
                wreckage[i].remainingActiveSeconds -= dt;
                if (wreckage[i].remainingActiveSeconds <= 0f) wreckage.RemoveAt(i);
            }
        }

        private static void TickIdle(RemoteRaidContext ctx, ThreatPressureState pressure, RaidCampaignState campaign, float dt)
        {
            int controlledCount = ctx.CountControlledPlanets();
            bool isStartSystem = ctx.Delta.systemId == World.StarSystemRegistry.StartSystemId;

            if (!pressure.FirstControlledGraceGranted && controlledCount > 0 && !isStartSystem)
            {
                pressure.StartGrace(15f);
                pressure.MarkFirstControlledGraceGranted();
            }

            if (controlledCount == 0) return;
            if (campaign.HasGlobalMajorRaidActive && campaign.ActiveMajorRaidSystemId != ctx.Delta.systemId) return;
            if (pressure.InGrace) return;

            bool systemDominant = controlledCount == ctx.Manifest.Planets.Count;
            bool disruptorTaught = campaign.IsTaught(EnemyIds.EmDisruptorMk1);
            RaidStage nextStage = RaidComposer.NextEligibleStage(systemDominant, controlledCount, disruptorTaught);

            List<(string worldId, float economicValue)> candidates = ctx.BuildEconomicCandidates();
            float bestEconomicValue = 1f;
            foreach (var c in candidates) if (c.economicValue > bestEconomicValue) bestEconomicValue = c.economicValue;

            float ratePerMinute = nextStage switch { RaidStage.Pirate => 3f, RaidStage.Assault => 2.5f, _ => 2f };
            pressure.TickPressure(dt, ratePerMinute * RaidComposer.DifficultyPressureMultiplier(campaign.Difficulty), bestEconomicValue);

            if (pressure.Pressure < ThreatPressureState.MaxPressure) return;
            if (candidates.Count == 0) return;
            if (!campaign.TryStartGlobalMajorRaid(ctx.Delta.systemId)) return;

            ScheduleRaid(ctx, pressure, campaign, nextStage, candidates);
        }

        private static void ScheduleRaid(RemoteRaidContext ctx, ThreatPressureState pressure, RaidCampaignState campaign,
            RaidStage stage, List<(string worldId, float economicValue)> candidates)
        {
            string targetWorldId = RaidComposer.SelectTargetPlanet(candidates, campaign);
            string newClass = RaidComposer.WhichNewClassToTeach(stage, campaign);
            ulong seed = ExtractionCatalog.Hash(ExtractionState.UniverseSeed, ctx.Delta.systemId + "/remote-raid/" + campaign.History.Count);
            int budget = RaidComposer.ResolveBudget(stage, campaign.Difficulty, seed);
            List<string> composition = RaidComposer.Compose(stage, budget, seed, newClass, campaign);

            bool isNewClassFirstAppearance = newClass != null;
            bool probeEffectActive = !isNewClassFirstAppearance && CombatCampaignState.FirstProbe.PendingReconEffect != null;

            bool hasScout = ctx.HasRealActiveScout(out int scoutMk);
            float warningSeconds = WarningResolver.Resolve(isNewClassFirstAppearance, probeEffectActive,
                RaidComposer.DifficultyWarningOffsetSeconds(campaign.Difficulty), hasScout, scoutMk);

            pressure.ScheduleRaid(stage, RaidComposer.DoctrineFor(stage), targetWorldId, composition, seed, warningSeconds);
            pressure.ResetPressure();
            if (probeEffectActive)
            {
                CombatCampaignState.FirstProbe.ConsumePendingReconEffect();
                pressure.MarkPendingProbeEffectConsumed();
            }
        }

        /// <summary>VS-016A.1 §3: a target that vanished (never existed / no longer Controlled / dropped from this system's own manifest) aborts cleanly -- zero enemies spawn, and the phase machine is pushed straight into the same RetreatCleanup -> Resolved path a real raid would take, so FinalizeRaid's own idempotent slot-release/grace/ClearToIdle runs exactly once and the next legitimate raid is never blocked.</summary>
        private static void SpawnComposition(RemoteRaidContext ctx, ThreatPressureState pressure)
        {
            if (!IsRaidTargetValid(ctx, pressure.TargetPlanetWorldId))
            {
                AbortInvalidRaid(pressure);
                return;
            }
            for (int i = 0; i < pressure.CompositionDefIds.Count; i++)
            {
                EnemyDef def = EnemyCatalog.Get(pressure.CompositionDefIds[i]);
                if (def == null) continue;
                Vector3 position = ctx.SpawnPosition(pressure.EncounterSeed, i, pressure.TargetPlanetWorldId);
                string id = $"remote-enemy-{ctx.Delta.systemId}-{pressure.EncounterSeed}-{i:00}";
                RemoteEnemyRuntime enemy = RemoteEnemyRuntime.Create(id, def, position);
                ctx.Enemies.Add(enemy);
                pressure.NoteSpawnedEnemy(id);
            }
        }

        private static bool IsRaidTargetValid(RemoteRaidContext ctx, string targetWorldId)
        {
            if (string.IsNullOrEmpty(targetWorldId)) return false;
            if (!WorldIdCodec.TryParsePlanetIndex(targetWorldId, out int planetIndex)) return false;
            if (ctx.Manifest.FindPlanet(planetIndex) == null) return false; // dropped from this system's own manifest
            return ExtractionState.GetDominance(targetWorldId).State == DominanceState.Controlled; // only a Controlled planet was ever a legitimate raid target (BuildEconomicCandidates' own filter) -- anything else means it changed hands since scheduling
        }

        /// <summary>No enemies, no result/salvage/reward -- MarkResultApplied/MarkSalvageApplied make FinalizeRaid's reward block a guaranteed no-op (its own idempotency guard), while campaign.EndGlobalMajorRaid/StartGrace/ClearToIdle there still run unconditionally, so the global slot is freed and this system returns to a normal idle raid-eligible state exactly once.</summary>
        private static void AbortInvalidRaid(ThreatPressureState pressure)
        {
            pressure.MarkResultApplied();
            pressure.MarkSalvageApplied();
            pressure.SetPhase(RaidPhase.RetreatCleanup);
        }

        /// <summary>
        /// VS-016A §3 / VS-016A.1 §2 intent-first: every enemy's decision/fire/loot
        /// intent is gathered first, THEN all damage/theft is applied in one
        /// simultaneous pass -- so the outcome never depends on which enemy is
        /// iterated first (spec §3: "Çağrı sırası sonucu değiştirmemeli"). As of
        /// VS-016A.1, combat is two-sided within the same tick: enemies fire on
        /// structures (hitscan direct, or a PendingAttack for Siege/EM), then
        /// active planetary defenses and assigned Escorts fire back on enemies
        /// (hitscan Laser direct, PendingAttack for Plasma) -- each side its own
        /// collect-then-apply pass, always in the same fixed code order, so the
        /// combined per-tick outcome is 100% order-independent (§2's own test 11).
        /// </summary>
        private static void TickActive(RemoteRaidContext ctx, ThreatPressureState pressure, float dt)
        {
            var fireIntents = new List<(RemoteEnemyRuntime enemy, StructureDto target, float amount)>();
            var lootIntents = new List<(RemoteEnemyRuntime enemy, DrillDto target, float amount)>();
            var siegeIntents = new List<(RemoteEnemyRuntime enemy, StructureDto target)>();
            var emIntents = new List<(RemoteEnemyRuntime enemy, StructureDto target)>();

            bool anyAlive = false;
            foreach (string id in pressure.SpawnedEnemyIds)
            {
                RemoteEnemyRuntime enemy = ctx.FindEnemy(id);
                if (enemy == null || enemy.Destroyed) continue;
                anyAlive = true;
                enemy.TickTimers(dt);

                bool decisionDue = enemy.Ai.DecisionDue(dt);
                bool exitToRetreat = enemy.Def.RetreatHullFraction > 0f
                    && (enemy.Durability.Armor + enemy.Durability.Hull) <= (enemy.Def.ArmorMax + enemy.Def.HullMax) * enemy.Def.RetreatHullFraction;
                if (decisionDue) enemy.Ai.MakeDecision(distanceToTarget: 0f, targetLost: false, exitAttackOrScanToRetreat: exitToRetreat);
                enemy.Ai.TickRetreatEscape(dt, distanceToTarget: 999999f, stillInSensorRange: false);

                if (enemy.Ai.Phase != EnemyAiPhase.AttackOrScan || enemy.IsDisrupted) continue;

                if (enemy.Def.Role == EnemyRole.Looter)
                {
                    DrillDto drillTarget = ctx.ChooseDrillTarget(pressure.TargetPlanetWorldId);
                    if (drillTarget != null)
                    {
                        float amount = Mathf.Min(enemy.Def.CargoTransferVolumePerSecond * dt, drillTarget.stored);
                        if (amount > 0f) lootIntents.Add((enemy, drillTarget, amount));
                    }
                    continue;
                }

                if (!enemy.Def.IsCombatCapable) continue;
                StructureDto structureTarget = ctx.ChooseStructureTarget(pressure.TargetPlanetWorldId);
                if (structureTarget == null) continue;

                // VS-016A.1 §2C: Fighter/Heavy stay same-tick hitscan (unchanged); Siege and
                // Disruptor's EM instead produce a PendingAttack -- a single TelegraphRemaining
                // gate stands in for the live per-role telegraph+separate-weapon-cooldown pair
                // (that private cooldown field was never captured into EnemyDto even before this
                // hotfix), so a shot fires the instant the telegraph reaches zero and immediately
                // rearms for the next one -- deterministic, just a coarser cadence than live.
                switch (enemy.Def.Role)
                {
                    case EnemyRole.Siege:
                        if (enemy.TelegraphRemaining <= 0f) { siegeIntents.Add((enemy, structureTarget)); enemy.TelegraphRemaining = enemy.Def.TelegraphSeconds; }
                        break;
                    case EnemyRole.Disruptor:
                        if (enemy.TelegraphRemaining <= 0f) { emIntents.Add((enemy, structureTarget)); enemy.TelegraphRemaining = enemy.Def.TelegraphSeconds; }
                        break;
                    default:
                        fireIntents.Add((enemy, structureTarget, enemy.Def.Dps * dt));
                        break;
                }
            }

            // Simultaneous damage application (§3 step 6). Never notes player participation --
            // HasMeaningfulParticipation (and therefore raid XP) must only ever reflect flags the
            // player set while physically present (§8: zero player-absent ship XP).
            foreach (var (enemy, target, amount) in fireIntents)
            {
                Durability structureDurability = RemoteStructureAccess.ToDurability(target);
                DamageResolver.Apply(structureDurability, amount, DamageProfile.Neutral);
                RemoteStructureAccess.WriteBack(target, structureDurability);
            }

            // Simultaneous resource theft (§3 step 7).
            foreach (var (enemy, target, amount) in lootIntents)
            {
                target.stored = Mathf.Max(0f, target.stored - amount);
                float roomLeft = enemy.Def.CargoCapacityVolume;
                foreach (var (itemId, heldAmount) in enemy.LooterCargo) if (itemId == ItemIdForDrill(target)) roomLeft -= heldAmount;
                float taken = Mathf.Min(amount, Mathf.Max(0f, roomLeft));
                if (taken <= 0f) continue;
                AddLooterCargo(enemy, ItemIdForDrill(target), taken);
                pressure.NoteResourceStolen();
            }

            // VS-016A.1 §2C: Siege -> a real interceptable physical PendingAttack, same shared
            // ProjectileCatalog constants FireSiegeProjectile itself uses (never re-derived).
            foreach (var (enemy, target) in siegeIntents)
            {
                ctx.PendingAttacks.Add(new PendingAttack(ctx.NextAttackId(), enemy.Id, RemoteRaidContext.StructureTargetId(target),
                    ProjectileKind.EnemySiege, ProjectileFaction.Hostile, enemy.Def.Dps * 2f, ProjectileCatalog.SiegeLifetimeSeconds,
                    splashRadius: 0f, splashMaxFractionOfDirect: 0f, isEm: false, emDisruptSeconds: 0f, emImmunityAfterDisruptSeconds: 0f,
                    interceptable: true, interceptionDurabilityRemaining: ProjectileCatalog.InterceptionDurability));
            }

            // VS-016A.1 §2C: Disruptor's EM -- same 1.5s-telegraph-equivalent damage formula as
            // live TickDisruptor (Dps * TelegraphSeconds), but the enemy's OWN DisruptSeconds/
            // DisruptImmunitySeconds (never WeaponCatalog's ship-EM constants) drive the disrupt,
            // exactly matching the live non-projectile Disruptor's own catalog source.
            foreach (var (enemy, target) in emIntents)
            {
                ctx.PendingAttacks.Add(new PendingAttack(ctx.NextAttackId(), enemy.Id, RemoteRaidContext.StructureTargetId(target),
                    ProjectileKind.Em, ProjectileFaction.Hostile, enemy.Def.Dps * enemy.Def.TelegraphSeconds, enemy.Def.TelegraphSeconds,
                    splashRadius: 0f, splashMaxFractionOfDirect: 0f, isEm: true,
                    emDisruptSeconds: enemy.Def.DisruptSeconds, emImmunityAfterDisruptSeconds: enemy.Def.DisruptImmunitySeconds,
                    interceptable: false, interceptionDurabilityRemaining: 0f));
            }

            // VS-016A.1 §2A/§2B: the counter-fire side -- active planetary defenses, then any
            // assigned Escort, each firing on their own chosen enemy target this same tick.
            TickPlanetaryDefenseFire(ctx, pressure, dt);
            TickEscortFire(ctx, dt);

            DominanceState targetState = !string.IsNullOrEmpty(pressure.TargetPlanetWorldId)
                ? ExtractionState.GetDominance(pressure.TargetPlanetWorldId).State : DominanceState.Unowned;
            if (targetState == DominanceState.Contested || targetState == DominanceState.Lost) pressure.NoteTargetContested();

            if (!anyAlive || targetState == DominanceState.Lost) pressure.SetPhase(RaidPhase.RetreatCleanup);
        }

        /// <summary>VS-016A.1 §2A: active planetary Laser/Plasma counter-fire on this raid's enemies. Laser is hitscan (shared T1/T2 DPS, 8s-continuous/3s-overheat exactly like SurfaceStructure.FireLaserAtTarget); Plasma never deals direct damage -- it produces a PendingAttack at the real fire interval, resolved later by RemotePendingAttackTick with splash, exactly like SurfaceStructure.FirePlasmaAtTarget. defenseOverheatRemaining doubles as the generic "cannot fire" gate for an EM-disrupted structure (RemotePendingAttackTick's EM-vs-structure resolution) as well as an overheated Laser -- both mean the same thing here: this weapon is offline for N seconds.</summary>
        private static void TickPlanetaryDefenseFire(RemoteRaidContext ctx, ThreatPressureState pressure, float dt)
        {
            if (string.IsNullOrEmpty(pressure.TargetPlanetWorldId)) return;
            List<StructureDto> weapons = ctx.ActiveDefenseWeaponsOrderedById(pressure.TargetPlanetWorldId);
            if (weapons.Count == 0) return;

            var laserHitIntents = new List<(RemoteEnemyRuntime target, float amount)>();
            foreach (StructureDto weapon in weapons)
            {
                if (weapon.defenseOverheatRemaining > 0f) { weapon.defenseOverheatRemaining = Mathf.Max(0f, weapon.defenseOverheatRemaining - dt); continue; }
                var type = (SurfaceStructureType)weapon.type;

                if (type == SurfaceStructureType.Laser)
                {
                    StructureTierDef def = PlanetaryDefenseCatalog.Get(type, weapon.tier) ?? PlanetaryDefenseCatalog.LaserT1;
                    RemoteEnemyRuntime target = ChooseFocusOrNearestEnemy(ctx, FocusEnemyIdFor(ctx, weapon.worldId));
                    if (target == null) continue;
                    laserHitIntents.Add((target, def.Dps * dt));
                    weapon.defenseContinuousFireSeconds += dt;
                    if (def.ContinuousFireSecondsBeforeOverheat > 0f && weapon.defenseContinuousFireSeconds >= def.ContinuousFireSecondsBeforeOverheat)
                    {
                        weapon.defenseOverheatRemaining = def.OverheatCooldownSeconds;
                        weapon.defenseContinuousFireSeconds = 0f;
                    }
                }
                else // Plasma
                {
                    if (weapon.defensePlasmaCooldownRemaining > 0f) { weapon.defensePlasmaCooldownRemaining = Mathf.Max(0f, weapon.defensePlasmaCooldownRemaining - dt); continue; }
                    StructureTierDef def = PlanetaryDefenseCatalog.Get(type, weapon.tier) ?? PlanetaryDefenseCatalog.PlasmaT1;
                    RemoteEnemyRuntime target = ChooseFocusOrNearestEnemy(ctx, FocusEnemyIdFor(ctx, weapon.worldId));
                    if (target == null) continue;
                    ctx.PendingAttacks.Add(new PendingAttack(ctx.NextAttackId(), RemoteRaidContext.StructureTargetId(weapon), target.Id,
                        ProjectileKind.Plasma, ProjectileFaction.Player, def.Dps * ProjectileCatalog.PlanetaryPlasmaFireIntervalSeconds,
                        ProjectileCatalog.PlanetaryPlasmaLifetimeSeconds, def.SplashRadius, def.SplashMaxFractionOfDirect,
                        isEm: false, emDisruptSeconds: 0f, emImmunityAfterDisruptSeconds: 0f, interceptable: false, interceptionDurabilityRemaining: 0f));
                    weapon.defensePlasmaCooldownRemaining = ProjectileCatalog.PlanetaryPlasmaFireIntervalSeconds;
                }
            }

            foreach (var (target, amount) in laserHitIntents) target.ApplyDamage(amount, DamageProfile.Laser);
        }

        /// <summary>The saved FocusTarget enemy id for this planet's defense coordination, if any weapon there is currently in Focus mode -- state read-only, never mutated here (VS-016A.1 §2A: "saved defense-coordination state must be preserved").</summary>
        private static string FocusEnemyIdFor(RemoteRaidContext ctx, string worldId)
        {
            foreach (DefenseCoordinationDto dc in ctx.Delta.defenseCoordination)
                if (dc != null && dc.worldId == worldId && (DefenseMode)dc.mode == DefenseMode.FocusTarget) return dc.focusEnemyId;
            return null;
        }

        /// <summary>No structure has a persisted world position in the remote model (SurfaceStructure's real position is only ever procedurally derived from a materialized planet+node) -- the existing enemy-vs-structure fire intent above already reflects that same "no spatial range gate" abstraction. Target choice is therefore: the saved Focus enemy if it's still alive, else the alive enemy first in canonical ID order -- deterministic regardless of list/call order (VS-016A.1 §2A's own explicit tie-break requirement), never re-picking a new target arbitrarily.</summary>
        private static RemoteEnemyRuntime ChooseFocusOrNearestEnemy(RemoteRaidContext ctx, string focusEnemyId)
        {
            if (!string.IsNullOrEmpty(focusEnemyId))
            {
                RemoteEnemyRuntime focus = ctx.FindEnemy(focusEnemyId);
                if (focus != null && !focus.Destroyed) return focus;
            }
            RemoteEnemyRuntime best = null;
            foreach (RemoteEnemyRuntime e in ctx.Enemies)
            {
                if (e.Destroyed) continue;
                if (best == null || string.CompareOrdinal(e.Id, best.Id) < 0) best = e;
            }
            return best;
        }

        /// <summary>VS-016A.1 §2B: every real, assigned, non-retreating Escort fires its Fixed Laser (shared VehicleDef DPS/DamageProfile.Laser, hitscan same-tick, exactly like VehicleAgent.FireEscortLaser) on its own chosen target -- raider (Looter) priority over any other combat enemy, same as the live AcquireEscortTarget's own priority order, canonical-ID tie-break. A destroyed/retreated Escort is already excluded by ActiveEscortsOrderedById; an EM-disrupted one is gated here (disruptRemaining counts down every tick regardless of whether it can otherwise fire, mirroring EnemyAgent's own unconditional per-frame disrupt/immunity countdown) and deals zero damage while disrupted, matching "automatic defense fire must never produce ship participation/XP" by construction -- this path never touches EscortState.XpAwarded or any ship-participation flag.</summary>
        private static void TickEscortFire(RemoteRaidContext ctx, float dt)
        {
            List<VehicleDto> escorts = ctx.ActiveEscortsOrderedById();
            if (escorts.Count == 0) return;

            var hitIntents = new List<(RemoteEnemyRuntime target, float amount)>();
            foreach (VehicleDto v in escorts)
            {
                EscortStateDto es = v.escortState;
                if (es.disruptRemaining > 0f)
                {
                    es.disruptRemaining = Mathf.Max(0f, es.disruptRemaining - dt);
                    if (es.disruptRemaining <= 0f) es.disruptImmunityRemaining = WeaponCatalog.EmImmunityAfterDisruptSeconds;
                    continue;
                }
                if (es.disruptImmunityRemaining > 0f) es.disruptImmunityRemaining = Mathf.Max(0f, es.disruptImmunityRemaining - dt);

                Hangar.VehicleDef def = Hangar.VehicleCatalog.Get((Hangar.VehicleClass)v.vehicleClass, v.mk);
                if (def == null || def.FixedLaserDps <= 0f) continue;
                RemoteEnemyRuntime target = ChooseEscortTarget(ctx);
                if (target == null) continue;
                hitIntents.Add((target, def.FixedLaserDps * dt));
            }
            foreach (var (target, amount) in hitIntents) target.ApplyDamage(amount, DamageProfile.Laser);
        }

        private static RemoteEnemyRuntime ChooseEscortTarget(RemoteRaidContext ctx)
        {
            RemoteEnemyRuntime bestRaider = null, bestOther = null;
            foreach (RemoteEnemyRuntime e in ctx.Enemies)
            {
                if (e.Destroyed) continue;
                if (e.Def.Role == EnemyRole.Looter) { if (bestRaider == null || string.CompareOrdinal(e.Id, bestRaider.Id) < 0) bestRaider = e; }
                else { if (bestOther == null || string.CompareOrdinal(e.Id, bestOther.Id) < 0) bestOther = e; }
            }
            return bestRaider ?? bestOther;
        }

        private static string ItemIdForDrill(DrillDto d)
        {
            PlanetProfile profile = ExtractionState.GetProfile(d.worldId);
            return profile != null ? ExtractionCatalog.ResourceIds[(int)profile.Family] : ExtractionCatalog.ResourceIds[0];
        }

        private static void AddLooterCargo(RemoteEnemyRuntime enemy, string itemId, float amount)
        {
            for (int i = 0; i < enemy.LooterCargo.Count; i++)
            {
                if (enemy.LooterCargo[i].itemId != itemId) continue;
                enemy.LooterCargo[i] = (itemId, enemy.LooterCargo[i].amount + amount);
                return;
            }
            enemy.LooterCargo.Add((itemId, amount));
        }

        private static void FinalizeRaid(RemoteRaidContext ctx, ThreatPressureState pressure, RaidCampaignState campaign)
        {
            if (pressure.Stage.HasValue && !pressure.ResultApplied)
            {
                DominanceState targetState = !string.IsNullOrEmpty(pressure.TargetPlanetWorldId)
                    ? ExtractionState.GetDominance(pressure.TargetPlanetWorldId).State : DominanceState.Unowned;
                bool targetLost = targetState == DominanceState.Lost;

                // §8: zero player-absent ship XP -- HasMeaningfulParticipation only reflects flags the
                // player actually set while present (PlayerDealtMeaningfulDamage / PlayerIssuedValidFocusCommand
                // / PlayerActiveDefenseParticipation), never anything RemoteRaidTick itself notes.
                RaidOutcome outcome = RaidOutcomeResolver.Resolve(targetLost, pressure.PlayerWasShutdownDuringRaid,
                    pressure.WasTargetContestedDuringRaid, pressure.AnyResourceStolen);
                int xp = RaidOutcomeResolver.ResolveXp(pressure.Stage.Value, outcome, pressure.HasMeaningfulParticipation);
                if (xp > 0)
                {
                    string ledgerKey = "vs016a.remote-raid.result/" + ctx.Delta.systemId + "/" + pressure.EncounterSeed;
                    Progression.ProgressionService.Award(ledgerKey, xp);
                }
                pressure.MarkResultApplied();

                if (!pressure.SalvageApplied)
                {
                    CombatWreckageState wreck = RaidSalvageResolver.Resolve(
                        ctx.Delta.systemId + "/combat-wreck.remote-raid." + pressure.EncounterSeed, pressure.EncounterSeed, outcome, pressure.CompositionDefIds);
                    if (wreck.Nodes.Count > 0) ctx.Delta.combatWreckage.Add(ToDto(wreck));
                    pressure.MarkSalvageApplied();
                }

                campaign.RecordHistory(pressure.Stage.Value, pressure.Doctrine ?? RaidComposer.DoctrineFor(pressure.Stage.Value),
                    LeaderDefId(pressure.Stage.Value), pressure.TargetPlanetWorldId, pressure.CompositionDefIds);
                foreach (string defId in pressure.CompositionDefIds) campaign.MarkTaught(defId);
            }
            campaign.EndGlobalMajorRaid(ctx.Delta.systemId);

            bool defeatGrace = !string.IsNullOrEmpty(pressure.TargetPlanetWorldId)
                && ExtractionState.GetDominance(pressure.TargetPlanetWorldId).State == DominanceState.Lost;
            pressure.StartGrace(defeatGrace ? 25f : 15f);
            pressure.ClearToIdle();
        }

        private static string LeaderDefId(RaidStage stage) => stage switch
        {
            RaidStage.Pirate => EnemyIds.LooterTransportMk1,
            RaidStage.Assault => EnemyIds.HeavyAssaultMk1,
            RaidStage.Siege => EnemyIds.SiegeMk1,
            _ => null
        };

        private static CombatWreckageDto ToDto(CombatWreckageState wreck)
        {
            var dto = new CombatWreckageDto { wreckageId = wreck.WreckageId, remainingActiveSeconds = wreck.RemainingActiveSeconds };
            foreach (CombatWreckageNode n in wreck.Nodes) dto.nodes.Add(new CombatWreckageNodeDto { itemId = n.ItemId, total = n.Total, remaining = n.Remaining });
            return dto;
        }
    }
}
