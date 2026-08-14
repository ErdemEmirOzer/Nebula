using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// VS-016A.1 §4: the pending-attack carryover chain, two-sided remote combat
    /// (planetary defense + Escort vs. enemy), interception, EM disrupt/immunity,
    /// and the invalid-raid-target abort -- on top of the VS-016A baseline these
    /// tests never touch (Vs016aRemoteRaidTests/Vs016aSchedulerAndParticipationTests
    /// already cover the shared raid-phase machine and global-slot caps).
    /// </summary>
    public class Vs016a1HotfixCombatTests
    {
        [TearDown]
        public void Cleanup()
        {
            StarSystemRegistry.Clear();
            CombatCampaignState.Reset();
            Progression.ProgressionService.Reset();
        }

        private static RemoteRaidContext BuildActiveCtx(ulong seed, string targetWorldId, out SystemManifest manifest)
        {
            StarSystemRegistry.Initialize(seed);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));
            ExtractionState.Reset((int)seed);

            manifest = TestGalaxyNeighbours.OneRemote(sector, WorldIdCodec.SystemId(sector, 0));
            Assert.IsNotNull(manifest, "the galaxy must supply a real remote system to raid");
            string remoteSystemId = manifest.SystemId;
            foreach (PlanetManifest p in manifest.Planets) ExtractionState.GetDominance(p.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);

            var delta = new SystemDelta { systemId = remoteSystemId };
            var pressure = new ThreatPressureState();
            pressure.ScheduleRaid(RaidStage.Assault, RaidDoctrine.Suppression, targetWorldId, new List<string>(), seed, 0f);
            pressure.SetPhase(RaidPhase.Active);
            return new RemoteRaidContext(delta, manifest, pressure);
        }

        /// <summary>Same setup as <see cref="BuildActiveCtx"/>, but the raid target is that remote system's own first planet -- needed by every test that exercises planetary defense fire, since TickPlanetaryDefenseFire is a no-op without a real TargetPlanetWorldId.</summary>
        private static RemoteRaidContext BuildActiveCtxTargetingFirstPlanet(ulong seed, out string worldId)
        {
            StarSystemRegistry.Initialize(seed);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));
            ExtractionState.Reset((int)seed);

            SystemManifest manifest = TestGalaxyNeighbours.OneRemote(sector, WorldIdCodec.SystemId(sector, 0));
            Assert.IsNotNull(manifest, "the galaxy must supply a real remote system to raid");
            string remoteSystemId = manifest.SystemId;
            foreach (PlanetManifest p in manifest.Planets) ExtractionState.GetDominance(p.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);
            worldId = manifest.Planets[0].PlanetId;

            var delta = new SystemDelta { systemId = remoteSystemId };
            var pressure = new ThreatPressureState();
            pressure.ScheduleRaid(RaidStage.Assault, RaidDoctrine.Suppression, worldId, new List<string>(), seed, 0f);
            pressure.SetPhase(RaidPhase.Active);
            return new RemoteRaidContext(delta, manifest, pressure);
        }

        private static RemoteEnemyRuntime AddTankyEnemy(RemoteRaidContext ctx, string id, string defId, bool inAttackOrScan = true)
        {
            EnemyDef def = EnemyCatalog.Get(defId);
            // Armor deliberately 0 so every hit flows straight to Hull (DamageResolver drains
            // Shield -> Armor -> Hull in order, and a nonzero Armor pool would silently absorb
            // every test hit before Hull ever moved) -- Hull itself is a huge pool so the enemy
            // never dies mid-test regardless of how much counter-fire lands.
            var durability = new Durability(0f, 0f, 100000f, 1f);
            var ai = new EnemyAiState(def);
            if (inAttackOrScan) ai.RestoreFromSave(EnemyAiPhase.AttackOrScan, null, 0f, 0f);
            var enemy = new RemoteEnemyRuntime(id, defId, def, ai, durability, Vector3.zero, Vector3.zero);
            ctx.Enemies.Add(enemy);
            ctx.Pressure.NoteSpawnedEnemy(id);
            return enemy;
        }

        private static StructureDto AddActiveStructure(RemoteRaidContext ctx, string worldId, string nodeId, SurfaceStructureType type, int tier = 1)
        {
            var dto = new StructureDto
            {
                worldId = worldId, nodeId = nodeId, type = (int)type, state = (int)DrillLifecycle.Active, tier = tier,
                shield = 0f, armor = 999999f, hull = 999999f
            };
            ctx.Delta.structures.Add(dto);
            return dto;
        }

        // --- §1: pending-attack carryover chain ------------------------------------------------

        [Test]
        public void PendingAttack_DealsNoDamageBeforeTimeExpires_ThenAppliesExactlyOnceAtExpiry()
        {
            RemoteRaidContext ctx = BuildActiveCtx(101UL, "w-none", out _);
            RemoteEnemyRuntime target = AddTankyEnemy(ctx, "e1", EnemyIds.FighterMk1);
            float hullBefore = target.Durability.Hull;

            ctx.PendingAttacks.Add(new PendingAttack("atk-1", "ship", target.Id, ProjectileKind.Em, ProjectileFaction.Player,
                damageAmount: 50f, remainingFlightSeconds: 3f, splashRadius: 0f, splashMaxFractionOfDirect: 0f,
                isEm: false, emDisruptSeconds: 0f, emImmunityAfterDisruptSeconds: 0f, interceptable: false, interceptionDurabilityRemaining: 0f));

            RemotePendingAttackTick.Tick(ctx, 1f);
            RemotePendingAttackTick.Tick(ctx, 1f);
            Assert.AreEqual(hullBefore, target.Durability.Hull, 0.001f, "an unexpired pending attack must never deal damage early");
            Assert.AreEqual(1, ctx.PendingAttacks.Count, "still ticking down -- not yet resolved");

            RemotePendingAttackTick.Tick(ctx, 1.5f); // pushes remaining below zero -> resolves this tick
            Assert.AreEqual(hullBefore - 50f, target.Durability.Hull, 0.001f, "must apply its full damage exactly once at expiry");
            Assert.AreEqual(0, ctx.PendingAttacks.Count, "resolved attack must be removed, never lingering as Applied");

            RemotePendingAttackTick.Tick(ctx, 1f);
            Assert.AreEqual(hullBefore - 50f, target.Durability.Hull, 0.001f, "a further tick after resolution must never apply the same attack again");
        }

        [Test]
        public void SaveThenLoad_NeverReAppliesAResolvedAttack()
        {
            RemoteRaidContext ctx = BuildActiveCtx(102UL, "w-none", out SystemManifest manifest);
            RemoteEnemyRuntime target = AddTankyEnemy(ctx, "e1", EnemyIds.FighterMk1);
            ctx.PendingAttacks.Add(new PendingAttack("atk-2", "ship", target.Id, ProjectileKind.Em, ProjectileFaction.Player,
                20f, 0.5f, 0f, 0f, false, 0f, 0f, false, 0f));

            RemotePendingAttackTick.Tick(ctx, 1f); // resolves immediately
            float hullAfterFirstResolve = target.Durability.Hull;
            ctx.SaveToDelta();
            Assert.AreEqual(0, ctx.Delta.pendingAttacks.Count, "an Applied attack must never be written back to the delta");

            RemoteRaidContext reloaded = RemoteRaidContext.LoadFromDelta(ctx.Delta, manifest);
            Assert.AreEqual(0, reloaded.PendingAttacks.Count, "a fresh load from that same delta must never resurrect a resolved attack");
        }

        [Test]
        public void LoadFromDelta_AppliedAttackDto_IsNeverRestored()
        {
            var delta = new SystemDelta { systemId = "sys-x" };
            delta.pendingAttacks.Add(new PendingAttackDto { attackId = "stale", attackerId = "e1", targetId = "t1", applied = true, damageAmount = 10f, remainingFlightSeconds = 0f });
            SystemManifest manifest = BuildEmptyManifest("sys-x");

            RemoteRaidContext ctx = RemoteRaidContext.LoadFromDelta(delta, manifest);
            Assert.AreEqual(0, ctx.PendingAttacks.Count, "an already-applied/consumed attack must never be restored from save -- it simply no longer exists");
        }

        [Test]
        public void Interception_DestroysAHostileSiegeAttack_BeforeItEverResolves()
        {
            RemoteRaidContext ctx = BuildActiveCtx(103UL, "w-none", out _);
            AddActiveStructure(ctx, "p0", "defense.0", SurfaceStructureType.Laser);
            RemoteEnemyRuntime escortTargetStandIn = AddTankyEnemy(ctx, "e1", EnemyIds.SiegeMk1); // never hit -- the shot is destroyed first

            ctx.PendingAttacks.Add(new PendingAttack("atk-siege", "e1", "p0/defense.0", ProjectileKind.EnemySiege, ProjectileFaction.Hostile,
                damageAmount: 360f, remainingFlightSeconds: ProjectileCatalog.SiegeLifetimeSeconds,
                splashRadius: 0f, splashMaxFractionOfDirect: 0f, isEm: false, emDisruptSeconds: 0f, emImmunityAfterDisruptSeconds: 0f,
                interceptable: true, interceptionDurabilityRemaining: ProjectileCatalog.InterceptionDurability));

            float ticksNeeded = ProjectileCatalog.InterceptionDurability / PlanetaryDefenseCatalog.LaserT1.Dps + 1f;
            for (int i = 0; i < Mathf.CeilToInt(ticksNeeded) + 2; i++) RemotePendingAttackTick.Tick(ctx, 1f);

            Assert.AreEqual(0, ctx.PendingAttacks.Count, "a fully-intercepted attack must be consumed (Applied), never left pending");
            Assert.IsFalse(escortTargetStandIn.Destroyed);
        }

        [Test]
        public void Interception_NeverAppliesAndConsumesTwice()
        {
            RemoteRaidContext ctx = BuildActiveCtx(104UL, "w-none", out _);
            AddActiveStructure(ctx, "p0", "defense.0", SurfaceStructureType.Laser);
            PendingAttack shot = new PendingAttack("atk-siege2", "e1", "p0/defense.0", ProjectileKind.EnemySiege, ProjectileFaction.Hostile,
                360f, ProjectileCatalog.SiegeLifetimeSeconds, 0f, 0f, false, 0f, 0f, true, 1f); // one hit from a T1 Laser destroys it
            ctx.PendingAttacks.Add(shot);

            RemotePendingAttackTick.Tick(ctx, 1f);
            Assert.IsTrue(shot.InterceptedDown || shot.Applied);
            RemotePendingAttackTick.Tick(ctx, 1f); // must be a no-op -- already gone
            Assert.AreEqual(0, ctx.PendingAttacks.Count);
        }

        // --- §2A: planetary defense counter-fire ------------------------------------------------

        [Test]
        public void PlanetaryLaser_GenuinelyReducesEnemyKzg_AndOverheatsAfterContinuousFire()
        {
            // T1 Laser has no overheat mechanic at all (ContinuousFireSecondsBeforeOverheat=0,
            // exactly like the live FireLaserAtTarget's own "> 0f" guard) -- only T2 does, so the
            // overheat half of this test needs a real T2 structure to mean anything.
            RemoteRaidContext ctx = BuildActiveCtxTargetingFirstPlanet(105UL, out string worldId);
            StructureDto laser = AddActiveStructure(ctx, worldId, "defense.0", SurfaceStructureType.Laser, tier: 2);
            RemoteEnemyRuntime enemy = AddTankyEnemy(ctx, "e1", EnemyIds.FighterMk1);
            float hullBefore = enemy.Durability.Hull;

            for (int i = 0; i < 8; i++) RemoteRaidTick.Tick(ctx, 1f);

            // Armor is 0 (see AddTankyEnemy) so every hit lands on Hull, which DamageProfile.Laser
            // scales by its own HullMultiplier (1.25x) -- the same shared profile ApplyHit/DamageResolver
            // apply everywhere else, so the expected figure must include it too.
            float expectedDamage = PlanetaryDefenseCatalog.LaserT2.Dps * 8f * DamageProfile.Laser.HullMultiplier;
            Assert.AreEqual(hullBefore - expectedDamage, enemy.Durability.Hull, 0.5f, "8 seconds of continuous T2 Laser fire must genuinely reduce enemy hull by DPS*8s*HullMultiplier");
            Assert.Greater(laser.defenseOverheatRemaining, 0f, "continuous fire at/over the 8s threshold must trigger overheat, exactly like the live structure");

            float hullAtOverheat = enemy.Durability.Hull;
            RemoteRaidTick.Tick(ctx, 1f);
            Assert.AreEqual(hullAtOverheat, enemy.Durability.Hull, 0.001f, "an overheated Laser must not fire");
        }

        [Test]
        public void PlanetaryPlasma_NeverDealsDirectDamage_ProducesPendingAttackAndAppliesSplashAtExpiry()
        {
            RemoteRaidContext ctx = BuildActiveCtxTargetingFirstPlanet(106UL, out string worldId);
            StructureDto plasma = AddActiveStructure(ctx, worldId, "defense.0", SurfaceStructureType.Plasma);
            RemoteEnemyRuntime enemy = AddTankyEnemy(ctx, "e1", EnemyIds.FighterMk1);
            float hullBeforeFire = enemy.Durability.Hull;

            RemoteRaidTick.Tick(ctx, 1f);
            Assert.AreEqual(hullBeforeFire, enemy.Durability.Hull, 0.001f, "Plasma must never deal direct damage the instant it fires");
            Assert.AreEqual(1, ctx.PendingAttacks.Count, "firing Plasma must produce exactly one PendingAttack");
            Assert.AreEqual(ProjectileFaction.Player, ctx.PendingAttacks[0].Faction);

            // Disable the weapon so it can't refire (its own 2.5s cooldown would otherwise queue a
            // second shot inside the 10s flight-time window this test waits out) -- the single shot
            // already in flight is unaffected by the source structure's own later state, exactly like
            // a real live projectile keeps flying after its launcher is destroyed.
            plasma.state = (int)DrillLifecycle.Disabled;

            // RemoteRaidTick.Tick alone only ever produces/collects pending attacks -- resolving them
            // is RemotePendingAttackTick's own job, exactly like SystemStreamingDirector.TickRemoteSlice
            // calls both in sequence every real tick. A test driving RemoteRaidTick.Tick alone would
            // never see this Plasma shot resolve at all.
            for (int i = 0; i < Mathf.CeilToInt(ProjectileCatalog.PlanetaryPlasmaLifetimeSeconds) + 1; i++)
            {
                RemoteRaidTick.Tick(ctx, 1f);
                RemotePendingAttackTick.Tick(ctx, 1f);
            }
            Assert.Less(enemy.Durability.Hull, hullBeforeFire, "the Plasma pending attack must apply its damage once its real flight time expires");
            Assert.AreEqual(0, ctx.PendingAttacks.Count);
        }

        // --- §2B: Escort counter-fire ------------------------------------------------------------

        [Test]
        public void Escort_FightsWithRealFixedLaserDps_MkIAndMkII()
        {
            AssertEscortDeals(EnemyIds.FighterMk1, mk: 1, expectedDps: VehicleCatalog.EscortMk1.FixedLaserDps);
            AssertEscortDeals(EnemyIds.FighterMk1, mk: 2, expectedDps: VehicleCatalog.EscortMk2.FixedLaserDps);
            Assert.AreEqual(60f, VehicleCatalog.EscortMk1.FixedLaserDps, 0.01f);
            Assert.AreEqual(100f, VehicleCatalog.EscortMk2.FixedLaserDps, 0.01f);
        }

        private static void AssertEscortDeals(string enemyDefId, int mk, float expectedDps)
        {
            RemoteRaidContext ctx = BuildActiveCtx(200UL + (ulong)mk, "w-none", out _);
            RemoteEnemyRuntime enemy = AddTankyEnemy(ctx, "e1", enemyDefId);
            float hullBefore = enemy.Durability.Hull;

            var vehicle = new VehicleDto
            {
                vehicleId = "esc-1", vehicleClass = (int)VehicleClass.Escort, mk = mk, armor = 999999f, hull = 999999f,
                escortState = new EscortStateDto { active = true, command = (int)EscortCommand.Protect, subState = (int)EscortSubState.Engaging, protectedTargetId = "ship" }
            };
            ctx.Delta.vehicles.Add(vehicle);

            RemoteRaidTick.Tick(ctx, 1f);
            // Armor is 0 (see AddTankyEnemy) so the hit lands on Hull, scaled by DamageProfile.Laser's
            // own HullMultiplier (1.25x) -- same shared profile every Laser hit uses, fixed DPS or not.
            float expectedDamage = expectedDps * DamageProfile.Laser.HullMultiplier;
            Assert.AreEqual(hullBefore - expectedDamage, enemy.Durability.Hull, 0.5f, $"Escort Mk{mk} must deal its real {expectedDps} DPS (x Laser HullMultiplier) in one second of remote fire");
        }

        [Test]
        public void Escort_RetiredOrRetreating_NeverFires()
        {
            RemoteRaidContext ctx = BuildActiveCtx(201UL, "w-none", out _);
            RemoteEnemyRuntime enemy = AddTankyEnemy(ctx, "e1", EnemyIds.FighterMk1);
            float hullBefore = enemy.Durability.Hull;
            ctx.Delta.vehicles.Add(new VehicleDto
            {
                vehicleId = "esc-retreat", vehicleClass = (int)VehicleClass.Escort, mk = 1, armor = 999999f, hull = 999999f,
                escortState = new EscortStateDto { active = true, command = (int)EscortCommand.Retreat, subState = (int)EscortSubState.Retreating }
            });

            RemoteRaidTick.Tick(ctx, 1f);
            Assert.AreEqual(hullBefore, enemy.Durability.Hull, 0.001f, "a retreating Escort must never counter-fire");
        }

        // --- §2C/§2D: EM disrupt/immunity -----------------------------------------------------

        [Test]
        public void EmPendingAttack_DisruptsEscort_ThenGrantsImmunity_PreventingImmediateReDisrupt()
        {
            RemoteRaidContext ctx = BuildActiveCtx(300UL, "w-none", out _);
            var vehicle = new VehicleDto
            {
                vehicleId = "esc-em", vehicleClass = (int)VehicleClass.Escort, mk = 1, armor = 999999f, hull = 999999f,
                escortState = new EscortStateDto { active = true, command = (int)EscortCommand.Protect, subState = (int)EscortSubState.Engaging }
            };
            ctx.Delta.vehicles.Add(vehicle);

            ctx.PendingAttacks.Add(new PendingAttack("atk-em", "e1", "esc-em", ProjectileKind.Em, ProjectileFaction.Hostile,
                damageAmount: 10f, remainingFlightSeconds: 0.5f, splashRadius: 0f, splashMaxFractionOfDirect: 0f,
                isEm: true, emDisruptSeconds: 2f, emImmunityAfterDisruptSeconds: 10f, interceptable: false, interceptionDurabilityRemaining: 0f));
            RemotePendingAttackTick.Tick(ctx, 1f);

            Assert.AreEqual(2f, vehicle.escortState.disruptRemaining, 0.01f, "the EM hit must apply the attack's own disrupt duration");

            // Immediately re-hit while still disrupted -- must not stack indefinitely beyond a single fresh application.
            ctx.PendingAttacks.Add(new PendingAttack("atk-em2", "e1", "esc-em", ProjectileKind.Em, ProjectileFaction.Hostile,
                10f, 0.5f, 0f, 0f, true, 2f, 10f, false, 0f));
            RemotePendingAttackTick.Tick(ctx, 1f);
            Assert.LessOrEqual(vehicle.escortState.disruptRemaining, 2f, "a disrupted Escort re-hit mid-disrupt must not accumulate beyond one fresh disrupt duration");
        }

        [Test]
        public void DisruptedEscort_CannotFire_UntilDisruptExpires()
        {
            RemoteRaidContext ctx = BuildActiveCtx(301UL, "w-none", out _);
            RemoteEnemyRuntime enemy = AddTankyEnemy(ctx, "e1", EnemyIds.FighterMk1);
            float hullBefore = enemy.Durability.Hull;
            var vehicle = new VehicleDto
            {
                vehicleId = "esc-disrupted", vehicleClass = (int)VehicleClass.Escort, mk = 1, armor = 999999f, hull = 999999f,
                escortState = new EscortStateDto { active = true, command = (int)EscortCommand.Protect, subState = (int)EscortSubState.Engaging, disruptRemaining = 5f }
            };
            ctx.Delta.vehicles.Add(vehicle);

            RemoteRaidTick.Tick(ctx, 1f);
            Assert.AreEqual(hullBefore, enemy.Durability.Hull, 0.001f, "a currently-disrupted Escort must deal zero damage");
            Assert.AreEqual(4f, vehicle.escortState.disruptRemaining, 0.01f, "disrupt must still count down even while it prevents firing");
        }

        // --- §2/§8: no player-absent participation/XP from automatic defense --------------------

        [Test]
        public void AutomaticDefenseAndEscortFire_NeverSetsPlayerParticipationOrAwardsXp()
        {
            RemoteRaidContext ctx = BuildActiveCtxTargetingFirstPlanet(400UL, out string worldId);
            AddActiveStructure(ctx, worldId, "defense.0", SurfaceStructureType.Laser);
            ctx.Delta.vehicles.Add(new VehicleDto
            {
                vehicleId = "esc-passive", vehicleClass = (int)VehicleClass.Escort, mk = 1, armor = 999999f, hull = 999999f,
                escortState = new EscortStateDto { active = true, command = (int)EscortCommand.Protect, subState = (int)EscortSubState.Engaging }
            });
            AddTankyEnemy(ctx, "e1", EnemyIds.FighterMk1);
            long xpBefore = Progression.ProgressionService.State.TotalXp;

            for (int i = 0; i < 20; i++) RemoteRaidTick.Tick(ctx, 1f);

            Assert.IsFalse(ctx.Pressure.PlayerActiveDefenseParticipation, "automatic remote defense fire must never flip a player-participation flag");
            Assert.IsFalse(ctx.Pressure.HasMeaningfulParticipation);
            Assert.AreEqual(xpBefore, Progression.ProgressionService.State.TotalXp, "automatic defense/Escort fire alone must never award XP");
        }

        // --- §3: invalid raid target aborts cleanly ---------------------------------------------

        [Test]
        public void InvalidRaidTarget_SpawnsNothing_AwardsNothing_AndFreesTheGlobalSlot()
        {
            StarSystemRegistry.Initialize(500UL);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));
            ExtractionState.Reset(500);
            SystemManifest manifest = TestGalaxyNeighbours.OneRemote(sector, WorldIdCodec.SystemId(sector, 0));
            Assert.IsNotNull(manifest, "the galaxy must supply a real remote system to raid");
            string remoteSystemId = manifest.SystemId;

            var delta = new SystemDelta { systemId = remoteSystemId };
            var pressure = new ThreatPressureState();
            var ctx = new RemoteRaidContext(delta, manifest, pressure);

            Assert.IsTrue(CombatCampaignState.RaidCampaign.TryStartGlobalMajorRaid(remoteSystemId));
            pressure.ScheduleRaid(RaidStage.Pirate, RaidDoctrine.Loot, "sector.0.0.0/system.999/planet.99", new List<string> { EnemyIds.FighterMk1, EnemyIds.FighterMk1 }, 500UL, 0f);
            pressure.SetPhase(RaidPhase.Spawning);
            long xpBefore = Progression.ProgressionService.State.TotalXp;

            for (int i = 0; i < 5; i++) RemoteRaidTick.Tick(ctx, 1f);

            Assert.AreEqual(0, ctx.Enemies.Count, "an invalid target must spawn zero enemies");
            Assert.AreEqual(0, ctx.Pressure.SpawnedEnemyIds.Count);
            Assert.AreEqual(xpBefore, Progression.ProgressionService.State.TotalXp, "an aborted invalid-target raid must never award XP");
            Assert.IsFalse(CombatCampaignState.RaidCampaign.HasGlobalMajorRaidActive, "the global major-raid slot must be freed, never stuck held by an aborted raid");
            Assert.AreEqual(RaidPhase.None, ctx.Pressure.Phase, "must return to idle exactly like a normal resolved raid");
        }

        [Test]
        public void InvalidRaidTarget_NeverBlocksANewValidRaidAfterwards()
        {
            StarSystemRegistry.Initialize(501UL);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));
            ExtractionState.Reset(501);
            SystemManifest manifest = TestGalaxyNeighbours.OneRemote(sector, WorldIdCodec.SystemId(sector, 0));
            Assert.IsNotNull(manifest, "the galaxy must supply a real remote system to raid");
            string remoteSystemId = manifest.SystemId;
            var pressure = new ThreatPressureState();
            var ctx = new RemoteRaidContext(new SystemDelta { systemId = remoteSystemId }, manifest, pressure);

            Assert.IsTrue(CombatCampaignState.RaidCampaign.TryStartGlobalMajorRaid(remoteSystemId));
            pressure.ScheduleRaid(RaidStage.Pirate, RaidDoctrine.Loot, "sector.0.0.0/system.999/planet.99", new List<string>(), 501UL, 0f);
            pressure.SetPhase(RaidPhase.Spawning);
            for (int i = 0; i < 5; i++) RemoteRaidTick.Tick(ctx, 1f);
            Assert.IsFalse(CombatCampaignState.RaidCampaign.HasGlobalMajorRaidActive);

            // A second, legitimate raid must still be startable right after.
            Assert.IsTrue(CombatCampaignState.RaidCampaign.TryStartGlobalMajorRaid(remoteSystemId), "the slot freed by the aborted raid must be immediately usable by a real one");
        }

        // --- §4/§5: schema and content invariants this hotfix must never bump -------------------

        [Test]
        public void SaveSchemaGenerationAndContentRevision_AreUnchangedByThisHotfix()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
            // 3, not the 2 this fixture froze when it was written: the generator
            // moved on afterwards and nobody came back here, so this assertion had
            // been failing on the project's own pinned value rather than guarding
            // it. The number this guards is the one UiFinalVersionPinTests pins.
            Assert.AreEqual(3, Sectors.SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, Content.ContentFingerprint.ContentRevision);
        }

        // --- §2: order-independence of the two-sided intent-first resolution --------------------

        [Test]
        public void TwoSidedActiveTick_100Of100IdenticalRegardlessOfEnemyIterationOrder()
        {
            string a = RunTwoSidedScenario(new[] { "e-aaa", "e-bbb", "e-ccc" });
            string b = RunTwoSidedScenario(new[] { "e-ccc", "e-aaa", "e-bbb" });
            Assert.AreEqual(a, b, "adding the same three enemies in a different order must produce an identical outcome -- canonical-ID tie-break, not insertion order, decides targeting");
        }

        private static string RunTwoSidedScenario(string[] enemyIds)
        {
            StarSystemRegistry.Clear(); CombatCampaignState.Reset(); Progression.ProgressionService.Reset();
            RemoteRaidContext ctx = BuildActiveCtxTargetingFirstPlanet(600UL, out string worldId);
            AddActiveStructure(ctx, worldId, "defense.0", SurfaceStructureType.Laser);
            foreach (string id in enemyIds) AddTankyEnemy(ctx, id, EnemyIds.FighterMk1);

            for (int i = 0; i < 5; i++) RemoteRaidTick.Tick(ctx, 1f);

            var sb = new System.Text.StringBuilder();
            var sorted = new List<RemoteEnemyRuntime>(ctx.Enemies);
            sorted.Sort((x, y) => string.CompareOrdinal(x.Id, y.Id));
            foreach (RemoteEnemyRuntime e in sorted) sb.Append(e.Id).Append(':').Append(e.Durability.Hull.ToString("0.00")).Append('|');
            return sb.ToString();
        }

        private static SystemManifest BuildEmptyManifest(string systemId)
        {
            var sector = new SectorCoordinate(0, 0, 0);
            return new SystemManifest(systemId, sector, 0, "Test", StarClass.Yellow, Vector3.zero,
                500000f, 50000f, false, SystemProfileKind.Balanced, 2, new List<PlanetManifest>());
        }
    }
}
