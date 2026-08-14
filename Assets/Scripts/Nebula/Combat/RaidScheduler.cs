using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-014 §8/§9/§11: the live raid scheduler, physical spawner, and
    /// RaidPhase state-machine driver — ticked once per frame by
    /// <see cref="CombatCampaignDirector"/> (the existing single campaign-
    /// session ticker; this is not a second global director, just an
    /// ordinary static helper it calls into). Every actual DECISION (stage
    /// escalation, budget, composition, target planet, warning duration) is
    /// delegated to the pure resolvers (RaidComposer/WarningResolver/
    /// EconomicValueResolver) built earlier this slice; this class only
    /// wires them to live ExtractionState/EnemyAgent/CombatEncounterDirector
    /// state and drives phase transitions. Exact win/loss result, XP, and
    /// salvage are intentionally NOT decided here (VS-014 §13, a separate
    /// task) — Resolved-phase bookkeeping here is limited to history/taught-
    /// class recording, releasing the one global major-raid slot, and a
    /// safe default grace so a system is never left permanently exposed.
    /// </summary>
    public static class RaidScheduler
    {
        public static void Tick(CombatEncounterDirector director, float dt)
        {
            if (director == null) return;
            ThreatPressureState pressure = director.Pressure;
            RaidCampaignState campaign = CombatCampaignState.RaidCampaign;

            switch (pressure.Phase)
            {
                case RaidPhase.None: TickIdle(director, pressure, campaign, dt); break;
                case RaidPhase.Scheduled: pressure.SetPhase(RaidPhase.Warning); break;
                case RaidPhase.Warning: if (pressure.TickWarning(dt)) pressure.SetPhase(RaidPhase.Spawning); break;
                case RaidPhase.Spawning:
                    SpawnComposition(director, pressure);
                    // VS-014.1 §4: SpawnComposition itself may already have redirected Phase to
                    // RetreatCleanup (target planet vanished before spawn) -- only advance to
                    // Active if it's still genuinely Spawning, never stomp an abort back to Active.
                    if (pressure.Phase == RaidPhase.Spawning) pressure.SetPhase(RaidPhase.Active);
                    break;
                case RaidPhase.Active: TickActive(director, pressure); break;
                case RaidPhase.RetreatCleanup: pressure.SetPhase(RaidPhase.Resolved); break;
                case RaidPhase.Resolved: FinalizeRaid(director, pressure, campaign); break;
            }
        }

        private static void TickIdle(CombatEncounterDirector director, ThreatPressureState pressure, RaidCampaignState campaign, float dt)
        {
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(director.SystemId);
            if (manifest == null) return;

            int controlledCount = CountControlledPlanets(manifest);
            bool isStartSystem = director.SystemId == StarSystemRegistry.StartSystemId;

            // VS-014 §8: the first-Controlled-outside-the-start-system 15min grace, granted at most once per system.
            if (!pressure.FirstControlledGraceGranted && controlledCount > 0 && !isStartSystem)
            {
                pressure.StartGrace(15f);
                pressure.MarkFirstControlledGraceGranted();
            }

            if (controlledCount == 0) return;
            if (campaign.HasGlobalMajorRaidActive && campaign.ActiveMajorRaidSystemId != director.SystemId) return; // one major raid anywhere, globally
            if (pressure.InGrace) return;

            bool systemDominant = controlledCount == manifest.Planets.Count;
            bool disruptorTaught = campaign.IsTaught(EnemyIds.EmDisruptorMk1);
            RaidStage nextStage = RaidComposer.NextEligibleStage(systemDominant, controlledCount, disruptorTaught);

            List<(string worldId, float economicValue)> candidates = BuildEconomicCandidates(manifest, director.SystemId);
            float bestEconomicValue = 1f;
            foreach (var c in candidates) if (c.economicValue > bestEconomicValue) bestEconomicValue = c.economicValue;

            float ratePerMinute = nextStage switch { RaidStage.Pirate => 3f, RaidStage.Assault => 2.5f, _ => 2f };
            pressure.TickPressure(dt, ratePerMinute * RaidComposer.DifficultyPressureMultiplier(campaign.Difficulty), bestEconomicValue);

            if (pressure.Pressure < ThreatPressureState.MaxPressure) return;
            if (candidates.Count == 0) return; // nothing real to target -- never schedule an empty raid
            if (!campaign.TryStartGlobalMajorRaid(director.SystemId)) return;

            ScheduleRaid(director, pressure, campaign, nextStage, candidates);
        }

        private static void ScheduleRaid(CombatEncounterDirector director, ThreatPressureState pressure, RaidCampaignState campaign,
            RaidStage stage, List<(string worldId, float economicValue)> candidates)
        {
            string targetWorldId = RaidComposer.SelectTargetPlanet(candidates, campaign);
            string newClass = RaidComposer.WhichNewClassToTeach(stage, campaign);
            ulong seed = ExtractionCatalog.Hash(ExtractionState.UniverseSeed, director.SystemId + "/raid/" + campaign.History.Count);
            int budget = RaidComposer.ResolveBudget(stage, campaign.Difficulty, seed);
            List<string> composition = RaidComposer.Compose(stage, budget, seed, newClass, campaign);

            bool isNewClassFirstAppearance = newClass != null;
            bool probeEffectActive = !isNewClassFirstAppearance && CombatCampaignState.FirstProbe.PendingReconEffect != null;

            bool hasScout = WarningResolver.HasRealActiveScoutInSystem(PlayerVehicles(director), director.SystemId, out int scoutMk);
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

        private static void TickActive(CombatEncounterDirector director, ThreatPressureState pressure)
        {
            // An empty spawn is an aborted raid, not “all spawned enemies were
            // defeated”. Close the global slot without XP, salvage, history or
            // taught-class progression.
            if (pressure.SpawnedEnemyIds.Count == 0)
            {
                CombatCampaignState.RaidCampaign.EndGlobalMajorRaid(director.SystemId);
                pressure.ClearToIdle();
                return;
            }

            bool anyAlive = false;
            bool anyTargetingPlayer = false;
            foreach (string id in pressure.SpawnedEnemyIds)
            {
                EnemyAgent agent = EnemyAgent.Find(id);
                if (agent == null || agent.Destroyed) continue;
                anyAlive = true;
                if (agent.Ai.TargetId == "player" && agent.Ai.Phase == EnemyAiPhase.AttackOrScan) anyTargetingPlayer = true;
            }

            DominanceState targetState = !string.IsNullOrEmpty(pressure.TargetPlanetWorldId)
                ? ExtractionState.GetDominance(pressure.TargetPlanetWorldId).State : DominanceState.Unowned;
            if (targetState == DominanceState.Contested || targetState == DominanceState.Lost) pressure.NoteTargetContested();

            // VS-014 §13: "active-defense participation while targeted" -- a real active defense weapon on the target planet, while at least one raid enemy is actively engaging the player.
            if (anyTargetingPlayer && HasActiveDefenseOn(pressure.TargetPlanetWorldId)) pressure.NoteActiveDefenseParticipation();

            if (!anyAlive || targetState == DominanceState.Lost) pressure.SetPhase(RaidPhase.RetreatCleanup);
        }

        private static bool HasActiveDefenseOn(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return false;
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.WorldId == worldId && SurfaceStructureCatalog.IsActiveDefenseWeapon(s.Type) && s.Runtime.IsActive) return true;
            return false;
        }

        /// <summary>
        /// VS-014 §13: exactly-once result/XP/salvage, guarded by ResultApplied/
        /// SalvageApplied so a save/load replay of an already-Resolved raid
        /// never re-grants either — plus the same history/taught-class/global-
        /// slot/grace bookkeeping VS-014 §11's state machine always needed.
        /// </summary>
        private static void FinalizeRaid(CombatEncounterDirector director, ThreatPressureState pressure, RaidCampaignState campaign)
        {
            if (pressure.Stage.HasValue && !pressure.ResultApplied)
            {
                DominanceState targetState = !string.IsNullOrEmpty(pressure.TargetPlanetWorldId)
                    ? ExtractionState.GetDominance(pressure.TargetPlanetWorldId).State : DominanceState.Unowned;
                bool targetLost = targetState == DominanceState.Lost;

                RaidOutcome outcome = RaidOutcomeResolver.Resolve(targetLost, pressure.PlayerWasShutdownDuringRaid,
                    pressure.WasTargetContestedDuringRaid, pressure.AnyResourceStolen);
                int xp = RaidOutcomeResolver.ResolveXp(pressure.Stage.Value, outcome, pressure.HasMeaningfulParticipation);
                if (xp > 0)
                {
                    string ledgerKey = "vs014.raid.result/" + director.SystemId + "/" + pressure.EncounterSeed;
                    Progression.ProgressionService.Award(ledgerKey, xp);
                }
                pressure.MarkResultApplied();

                if (!pressure.SalvageApplied)
                {
                    CombatWreckageState wreck = RaidSalvageResolver.Resolve(
                        director.SystemId + "/combat-wreck.raid." + pressure.EncounterSeed, pressure.EncounterSeed, outcome, pressure.CompositionDefIds);
                    if (wreck.Nodes.Count > 0) director.Wreckage.Add(wreck);
                    pressure.MarkSalvageApplied();
                }

                campaign.RecordHistory(pressure.Stage.Value, pressure.Doctrine ?? RaidComposer.DoctrineFor(pressure.Stage.Value),
                    LeaderDefId(pressure.Stage.Value), pressure.TargetPlanetWorldId, pressure.CompositionDefIds);
                foreach (string defId in pressure.CompositionDefIds) campaign.MarkTaught(defId);

                // VS-014 §14: exactly one Protected Pirate Base, created after the FIRST Pirate raid ever resolves, never in the start system.
                if (pressure.Stage.Value == RaidStage.Pirate && !campaign.PirateBase.Spawned && director.SystemId != StarSystemRegistry.StartSystemId)
                {
                    string poiId = "poi.pirate_base";
                    campaign.MarkPirateBaseSpawned(poiId, director.SystemId);
                    Vector3 basePosition = director.transform.position + PirateBaseCatalog.ComputePosition(ExtractionState.UniverseSeed, director.SystemId);
                    director.SetPirateBase(PirateBaseRuntime.Create(poiId, director.SystemId, basePosition, director, alreadyResolved: false));
                }
            }
            campaign.EndGlobalMajorRaid(director.SystemId);

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

        // --- Physical spawn (VS-014 §11) ---------------------------------------------
        private static void SpawnComposition(CombatEncounterDirector director, ThreatPressureState pressure)
        {
            PlanetIdentity planet = ExtractionState.FindPlanet(pressure.TargetPlanetWorldId);
            if (planet == null || planet.Body == null) { pressure.SetPhase(RaidPhase.RetreatCleanup); return; } // target vanished -- abort cleanly, never stall forever

            float spawnDistance = Mathf.Max(planet.Body.Radius * 1.5f, OuterDefenseRange(pressure.TargetPlanetWorldId) * 1.1f);
            for (int i = 0; i < pressure.CompositionDefIds.Count; i++)
            {
                Vector3 direction = DeterministicDirection(pressure.EncounterSeed, i);
                Vector3 position = planet.Body.transform.position + direction * spawnDistance;
                EnemyAgent agent = director.SpawnEnemy(pressure.CompositionDefIds[i], position);
                if (agent != null) pressure.NoteSpawnedEnemy(agent.Id);
            }
        }

        private static float OuterDefenseRange(string worldId)
        {
            float best = 1500f; // sane fallback for a target with no active defense yet
            foreach (SurfaceStructure s in ExtractionState.Structures)
            {
                if (s == null || s.WorldId != worldId || !SurfaceStructureCatalog.IsActiveDefenseWeapon(s.Type) || !s.Runtime.IsActive) continue;
                StructureTierDef def = PlanetaryDefenseCatalog.Get(s.Type, s.Runtime.Tier);
                if (def != null && def.Range > best) best = def.Range;
            }
            return best;
        }

        /// <summary>Deterministic, seed+index-derived spawn direction (never UnityEngine.Random) — kept off the poles so it always reads as a real approach vector.</summary>
        private static Vector3 DeterministicDirection(ulong seed, int index)
        {
            float azimuth = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, "raid/spawn/azimuth/" + index)) * Mathf.PI * 2f;
            float elevation = (ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, "raid/spawn/elevation/" + index)) - 0.5f) * Mathf.PI * 0.6f;
            float cosE = Mathf.Cos(elevation);
            return new Vector3(cosE * Mathf.Cos(azimuth), Mathf.Sin(elevation), cosE * Mathf.Sin(azimuth));
        }

        // --- Economic value / target candidates (VS-014 §8) --------------------------
        private static List<(string worldId, float economicValue)> BuildEconomicCandidates(SystemManifest manifest, string systemId)
        {
            var list = new List<(string, float)>();
            bool starConnected = EconomicValueResolver.HasStarConnection(systemId);
            bool pendingProbe = CombatCampaignState.FirstProbe.PendingReconEffect != null;
            foreach (PlanetManifest p in manifest.Planets)
            {
                if (ExtractionState.GetDominance(p.PlanetId).State != DominanceState.Controlled) continue;
                int activeDrills = CountActiveDrills(p.PlanetId);
                float avgFill = AverageStorageFill(p.PlanetId);
                float value = EconomicValueResolver.Resolve(p.Rarity, activeDrills, avgFill, starConnected, pendingProbe);
                list.Add((p.PlanetId, value));
            }
            return list;
        }

        private static int CountControlledPlanets(SystemManifest manifest)
        {
            int count = 0;
            foreach (PlanetManifest p in manifest.Planets) if (ExtractionState.GetDominance(p.PlanetId).State == DominanceState.Controlled) count++;
            return count;
        }

        private static int CountActiveDrills(string worldId)
        {
            int count = 0;
            foreach (DrillStructure d in ExtractionState.Drills) if (d != null && d.WorldId == worldId && d.Runtime.State == DrillLifecycle.Active) count++;
            return count;
        }

        private static float AverageStorageFill(string worldId)
        {
            float total = 0f; int count = 0;
            foreach (DrillStructure d in ExtractionState.Drills)
            {
                if (d == null || d.WorldId != worldId || d.Runtime.State != DrillLifecycle.Active) continue;
                total += d.Runtime.StoredAmount / Mathf.Max(1f, d.Runtime.StorageCapacity);
                count++;
            }
            return count > 0 ? total / count : 0f;
        }

        private static IReadOnlyList<VehicleAgent> PlayerVehicles(CombatEncounterDirector director)
        {
            ShipHangar hangar = director.PlayerShip != null ? director.PlayerShip.GetComponent<ShipHangar>() : null;
            return hangar != null ? hangar.Vehicles : System.Array.Empty<VehicleAgent>();
        }
    }
}
