using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Navigation;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;

namespace Nebula.Extraction
{
    /// <summary>
    /// Owns the extraction slice's save/load and autosave. Snapshots live
    /// state into plain DTOs on the main thread, then hands them to the
    /// file-only <see cref="SaveService"/>. Restore rebuilds drills, kit,
    /// scans, cargo, ship transform and — crucially — the moving-planet
    /// snapshots, so structures and the ship line up with their planets after
    /// a load even though the planets keep orbiting. Ctrl/Cmd+, saves,
    /// Ctrl/Cmd+. loads, autosave runs every 5 active minutes.
    /// </summary>
    public class ExtractionDirector : MonoBehaviour
    {
        /// <summary>
        /// The slot this director reads and writes. Defaults to 1, which is what
        /// autosave and the keyboard shortcuts use unless the player picks another.
        /// <para>
        /// This was <c>private const int Slot = 1</c>, so the save screen offered
        /// three slots that were all the same file: picking slot 2 wrote slot 1 and
        /// then reported "YUVA 2 KAYDEDİLDİ". The slot the player chose reached the
        /// status line and nothing else, which is the worst shape for this bug —
        /// the interface confirmed an outcome the save system never performed.
        /// </para>
        /// </summary>
        public int ActiveSlot
        {
            get => activeSlot;
            set => activeSlot = SaveService.ClampSlot(value);
        }

        private int activeSlot = SaveService.FirstSlot;
        private const float AutosaveSeconds = 300f;

        /// <summary>VS-017A.1 §2: real stopwatch timing around this director's own SaveNow/LoadNow calls -- external measurement only, never changes what SaveNow/LoadNow actually do.</summary>
        public static readonly SavePerformanceRecorder PerformanceRecorder = new SavePerformanceRecorder();

        /// <summary>
        /// Presentation-only success seams. They fire after the authoritative
        /// save/load transaction has completed; failures and autosave-on-quit
        /// never masquerade as a successful player action.
        /// </summary>
        public event Action<string> Saved;
        public event Action Loaded;

        private ShipController ship;
        private ShipExtraction extraction;
        private ShipLogistics logistics;
        private ShipProcessing processing;
        private ShipModules modules;
        private int seed;
        private int revision;
        private float autosaveTimer;

        /// <summary>Set by the bootstrap once the navigation director exists (VS-006, schema v4). Null-safe everywhere below.</summary>
        public NavigationDirector Navigation { get; set; }

        /// <summary>Set by the bootstrap once the ship's Hangar exists (VS-007, schema v5). Null-safe everywhere below.</summary>
        public ShipHangar Hangar { get; set; }

        /// <summary>Set by the bootstrap once the streaming director exists (VS-008, schema v6). Null-safe everywhere below.</summary>
        public SystemStreamingDirector Streaming { get; set; }

        public string LastSaveMessage { get; private set; }
        private float messageUntil;
        public string TransientMessage => Time.unscaledTime < messageUntil ? LastSaveMessage : null;

        private ShipCruise cruise;
        private ShipQuickAssetPackage quickPackage;
        private ShipPlanetEvacuation evacuation;
        private Ship.ShipCombat combat;

        public void Initialize(ShipController shipController, int universeSeed)
        {
            ship = shipController;
            extraction = shipController.GetComponent<ShipExtraction>();
            logistics = shipController.GetComponent<ShipLogistics>();
            processing = shipController.GetComponent<ShipProcessing>();
            modules = shipController.GetComponent<ShipModules>();
            cruise = shipController.GetComponent<ShipCruise>();
            quickPackage = shipController.GetComponent<ShipQuickAssetPackage>();
            evacuation = shipController.GetComponent<ShipPlanetEvacuation>();
            combat = shipController.GetComponent<Ship.ShipCombat>();
            seed = universeSeed;
        }

        private void Update()
        {
            if (!InputGate.Suppressed)
            {
                if (NebulaInputBindings.ManualSavePressed()) SaveNow("MANUAL SAVE");
                if (NebulaInputBindings.ManualLoadPressed()) LoadNow();
            }

            autosaveTimer += Time.deltaTime;
            if (autosaveTimer >= AutosaveSeconds) { autosaveTimer = 0f; SaveNow("AUTOSAVE"); }
        }

        private void OnApplicationQuit()
        {
            SaveNow(null);
            Progression.SessionLog.EndSession();
        }

        // --- Save ------------------------------------------------------------
        public void SaveNow(string label)
        {
            if (ship == null) return;
            SavePayload payload = null;
            PerformanceRecorder.RecordMainThreadSnapshot(() => payload = Capture());
            bool ok = false;
            PerformanceRecorder.RecordBackgroundWrite(() => ok = SaveService.Save(ActiveSlot, payload, ++revision));
            Progression.SessionLog.Log(ok ? "save_success" : "save_fail", label, null);
            if (label != null) SetMessage(ok ? label + " OK" : label + " FAILED");
            if (ok) Saved?.Invoke(label);
        }

        private SavePayload Capture()
        {
            var payload = new SavePayload { seed = seed, kit = (int)ExtractionState.Kit };

            Vector3 offset = FloatingOrigin.TotalOffset;
            Vector3 shipAbs = ship.transform.position + offset;
            Quaternion shipRot = ship.transform.rotation;
            LocationRegistry.GetLocation(ship.transform.position, out string star, out _);
            payload.ship = new ShipDto
            {
                px = shipAbs.x, py = shipAbs.y, pz = shipAbs.z,
                rx = shipRot.x, ry = shipRot.y, rz = shipRot.z, rw = shipRot.w,
                vx = ship.Velocity.x, vy = ship.Velocity.y, vz = ship.Velocity.z,
                throttle = ship.Throttle, systemStar = star
            };

            foreach (PlanetProfile profile in ExtractionState.CachedProfiles)
                if (profile.Scanned) payload.scanned.Add(profile.WorldId);

            if (logistics != null)
                foreach (var kv in logistics.Cargo.Items)
                    payload.cargo.Add(new CargoItemDto { itemId = kv.Key, amount = kv.Value });

            // The bench, and only the bench. No slot line is ever written again: a v15
            // save that still emitted processorSlots would hand the next load a timed
            // bay to migrate that this session never had.
            if (processing != null)
            {
                ProcessingBenchRuntime bench = processing.Bench;
                var benchDto = new ProcessingBenchDto
                {
                    inputItemId = bench.IsEmpty ? null : bench.InputItemId,
                    inputAmount = bench.IsEmpty ? 0 : bench.InputAmount
                };
                foreach (LegacyProcessingClaim claim in bench.LegacyClaims)
                {
                    if (claim == null || claim.Amount <= 0) continue;
                    benchDto.legacyClaims.Add(new LegacyProcessingClaimDto
                    {
                        itemId = claim.ItemId, amount = claim.Amount
                    });
                }
                payload.processingBench = benchDto;
            }

            if (modules != null)
            {
                foreach (var kv in modules.Modules.OwnedTiers)
                    payload.moduleTiers.Add(new ModuleTierDto { moduleId = kv.Key, tier = kv.Value });

                ModuleUpgradeJob job = modules.Upgrades?.ActiveJob;
                if (job != null)
                {
                    var jobDto = new ModuleJobDto
                    {
                        moduleId = job.ModuleId, fromTier = job.FromTier, toTier = job.ToTier,
                        reservedPoints = job.ReservedPoints, totalSeconds = job.TotalSeconds,
                        remainingSeconds = job.RemainingSeconds, paused = job.Paused,
                        safeStreakSeconds = job.SafeStreakSeconds
                    };
                    foreach (var r in job.ReservedResources)
                        jobDto.reservedResources.Add(new ModuleJobResourceDto { itemId = r.itemId, amount = r.amount });
                    payload.activeModuleJob = jobDto;
                }
            }

            payload.totalXp = Progression.ProgressionService.State.TotalXp;
            payload.developmentPoints = Progression.ProgressionService.State.DevelopmentPoints;
            foreach (string id in Progression.ProgressionService.Ledger.All) payload.ledger.Add(id);

            if (Navigation != null) payload.navigation = CaptureNavigation();
            if (Hangar != null) CaptureHangarShipState(payload);
            if (Streaming != null) CaptureWorldStreaming(payload);
            CaptureQuickPackageAndEvacuation(payload);
            CaptureCombat(payload);

            return payload;
        }

        /// <summary>VS-013 §10: ship/campaign-owned combat state — never per-system (enemies/projectiles/wreckage already went out with CaptureWorldStreaming's system deltas above).</summary>
        private void CaptureCombat(SavePayload payload)
        {
            if (combat != null && combat.State != null)
            {
                var dto = new ShipCombatDto
                {
                    shield = combat.State.Durability.Shield, armor = combat.State.Durability.Armor, hull = combat.State.Durability.Hull,
                    regenTimer = combat.State.Durability.SavedRegenTimer, energy = combat.State.Energy,
                    weaponsLocked = combat.State.WeaponsLocked, boostRemaining = combat.State.BoostRemaining,
                    boostCooldownRemaining = combat.State.BoostCooldownRemaining, damaged = combat.State.Damaged,
                    targetEnemyId = combat.Target != null ? combat.Target.Id : null,
                    repairActive = combat.Repair.Active, repairPaused = combat.Repair.Paused,
                    repairHullForm = combat.Repair.HullFormForCurrentPackage, repairPackageProgress01 = combat.Repair.PackageProgress01,
                    sinceLastDamageSeconds = combat.State.SinceLastDamageSeconds
                };
                for (int i = 0; i < combat.Hardpoints.Length; i++)
                {
                    Combat.HardpointState hp = combat.Hardpoints[i];
                    dto.hardpoints.Add(new HardpointDto
                    {
                        index = i, equipped = !hp.IsEmpty, weaponFamily = (int)(hp.Weapon ?? Combat.WeaponFamily.Laser),
                        tier = hp.Tier, heat = hp.Heat, overheated = hp.Overheated
                    });
                }
                if (combat.ActiveProductionJob != null)
                {
                    Combat.WeaponProductionJob job = combat.ActiveProductionJob;
                    dto.productionJob = new WeaponProductionJobDto
                    {
                        active = true, weaponFamily = (int)job.Family, targetTier = job.TargetTier,
                        reservedParts = job.ReservedParts, materialId = job.MaterialId, reservedMaterial = job.ReservedMaterial,
                        totalSeconds = job.TotalSeconds, remainingSeconds = job.RemainingSeconds
                    };
                }
                payload.shipCombat = dto;
            }

            Combat.TrainingProgress training = Combat.CombatCampaignState.Training;
            payload.training = new TrainingProgressDto
            {
                a = SnapshotTrainingRun(training.A), b = SnapshotTrainingRun(training.B), c = SnapshotTrainingRun(training.C)
            };

            Combat.FirstProbeState probe = Combat.CombatCampaignState.FirstProbe;
            payload.firstProbe = new FirstProbeDto
            {
                status = (int)probe.Status, warningRemainingSeconds = probe.WarningRemainingSeconds,
                threatKnown = probe.ThreatKnown, pendingReconEffect = probe.PendingReconEffect
            };

            if (combat != null)
            {
                payload.emergencyShutdown = new EmergencyShutdownDto { active = combat.Shutdown.Active, remainingSeconds = combat.Shutdown.RemainingSeconds };
            }

            Combat.RecoverySoftLockState recovery = Combat.CombatCampaignState.Recovery;
            var recoveryDto = new RecoverySoftLockDto
            {
                totalActiveMinutesElapsed = recovery.TotalActiveMinutesElapsed,
                lastVisitedControlledSystemId = recovery.LastVisitedControlledSystemId
            };
            recoveryDto.recentDefeatTimestamps.AddRange(recovery.SnapshotDefeatTimestamps());
            payload.recoverySoftLock = recoveryDto;

            // VS-014 §17: campaign-lifetime raid registry — ship/campaign-owned, same placement reasoning as everything else in this method.
            Combat.RaidCampaignState raidCampaign = Combat.CombatCampaignState.RaidCampaign;
            var raidDto = new RaidCampaignDto
            {
                difficulty = (int)raidCampaign.Difficulty,
                activeMajorRaidSystemId = raidCampaign.ActiveMajorRaidSystemId,
                pirateBaseSpawned = raidCampaign.PirateBase.Spawned, pirateBasePoiId = raidCampaign.PirateBase.PoiId,
                pirateBaseSystemId = raidCampaign.PirateBase.SystemId, pirateBaseResolved = raidCampaign.PirateBase.Resolved,
                pirateBaseRewardAwarded = raidCampaign.PirateBase.RewardAwarded
            };
            raidDto.taughtEnemyDefIds.AddRange(raidCampaign.TaughtEnemyDefIds);
            foreach (Combat.RaidHistoryEntry entry in raidCampaign.History)
            {
                var entryDto = new RaidHistoryEntryDto
                {
                    stage = (int)entry.Stage, doctrine = (int)entry.Doctrine, leaderDefId = entry.LeaderDefId, planetWorldId = entry.PlanetWorldId
                };
                entryDto.compositionDefIds.AddRange(entry.CompositionDefIds);
                raidDto.history.Add(entryDto);
            }
            payload.raidCampaign = raidDto;
        }

        private static TrainingRunDto SnapshotTrainingRun(Combat.TrainingRun run)
        {
            var dto = new TrainingRunDto { currentStep = run.CurrentStep };
            dto.stepCompleted.AddRange(run.SnapshotCompletedFlags());
            return dto;
        }

        /// <summary>VS-010 §7: the ship's own single in-flight Quick Asset Package / planet evacuation transaction (never system-delta state — a ship-owned process, same reasoning as Hangar production).</summary>
        private void CaptureQuickPackageAndEvacuation(SavePayload payload)
        {
            QuickAssetPackageTransaction pkg = quickPackage != null ? quickPackage.ActiveTransaction : null;
            if (pkg != null && !pkg.Cancelled && !pkg.Completed)
            {
                payload.quickPackage = new QuickPackageDto
                {
                    active = true, transactionId = pkg.TransactionId, planetWorldId = pkg.PlanetWorldId,
                    commandMaterialId = pkg.CommandMaterialId, laserMaterialId = pkg.LaserMaterialId,
                    drillSent = pkg.DrillSent, commandSent = pkg.CommandSent,
                    laserSent = pkg.LaserSent, elapsedSeconds = pkg.ElapsedSeconds
                };
            }

            PlanetEvacuationTransaction evac = evacuation != null ? evacuation.ActiveTransaction : null;
            if (evac != null && !evac.Finalized && !evac.Cancelled)
            {
                var dto = new EvacuationDto { active = true, planetWorldId = evac.PlanetWorldId };
                dto.pendingVehicleIds.AddRange(evac.PendingVehicleIds);
                payload.evacuation = dto;
            }
        }

        /// <summary>
        /// VS-008 §8: per-system mutable state (drills, structures,
        /// dominance, planet phase, and any vehicle away from the ship) is no
        /// longer a flat global list — only the CURRENT (Full) system's live
        /// state needs capturing fresh; every other known system's state
        /// already lives in <see cref="StarSystemRegistry.Deltas"/> from the
        /// moment it demoted. Every system's manifest itself is reproducible
        /// from (seed, coordinate) alone and is never persisted.
        /// </summary>
        private void CaptureWorldStreaming(SavePayload payload)
        {
            SystemDelta current = Streaming.CaptureCurrentFullDelta();
            if (current != null && !current.IsEmpty) payload.systemDeltas.Add(current);
            foreach (SystemDelta delta in StarSystemRegistry.Deltas.AllNonEmpty)
                if (current == null || delta.systemId != current.systemId) payload.systemDeltas.Add(delta);
        }

        /// <summary>
        /// Ship-owned Hangar state only (VS-008 §5: "Ship-owned progression,
        /// kargo, modül job'ı, Hangar üretim job'ı ve docked araçlar sistem
        /// delta'sına taşınmamalı"). A non-docked vehicle is captured once,
        /// as part of whichever system's delta it's physically in
        /// (<see cref="CaptureWorldStreaming"/>) — never duplicated here.
        /// </summary>
        private void CaptureHangarShipState(SavePayload payload)
        {
            payload.hangarCommandUsed = Hangar.Command.Used;
            payload.hangarNextVehicleSequence = Hangar.Production.NextVehicleSequenceForSave;

            HangarProductionJob job = Hangar.Production.ActiveJob;
            if (job != null)
            {
                payload.hangarJob = new HangarJobDto
                {
                    active = true, mk = job.Mk,
                    vehicleClass = (int)job.VehicleClass, chosenMaterialId = job.ChosenMaterialId,
                    reservedParts = job.ReservedParts, reservedMaterial = job.ReservedMaterial,
                    totalSeconds = job.TotalSeconds, remainingSeconds = job.RemainingSeconds,
                    isUpgrade = job.IsUpgrade, upgradeVehicleId = job.UpgradeVehicleId, reservedSecondMaterial = job.ReservedSecondMaterial
                };
            }

            foreach (VehicleAgent v in Hangar.Vehicles)
            {
                // A route-controlled vehicle's Mission.Phase reads InHangar too (Route bypasses
                // Mission entirely, VS-011 §4) but it is NOT physically docked — it is captured
                // once, inside its system's delta (CaptureLogistics), never duplicated here.
                if (v == null || v.Route != null || v.Mission.Phase != VehiclePhase.InHangar) continue;
                payload.vehicles.Add(new VehicleDto
                {
                    vehicleId = v.VehicleId, vehicleClass = (int)v.Def.Class, mk = v.Def.Mk, homeSystemId = null,
                    phase = (int)VehiclePhase.InHangar, missionKind = (int)MissionKind.None, targetId = null,
                    phaseTimer = 0f, recallRequested = false,
                    px = 0f, py = 0f, pz = 0f, rx = 0f, ry = 0f, rz = 0f, rw = 1f,
                    heldItemId = v.HeldItemId, heldAmount = v.HeldAmount
                });
            }
        }

        private NavigationDto CaptureNavigation()
        {
            var dto = new NavigationDto();
            foreach (var kv in Navigation.Knowledge.All)
                dto.systemKnowledge.Add(new SystemKnowledgeDto { systemId = kv.Key, level = (int)kv.Value });
            foreach (var (origin, target) in Navigation.FrontierSignal.SnapshotPairs())
                dto.frontierProvenance.Add(new FrontierProvenanceDto { origin = origin, target = target });
            foreach (string id in Navigation.FrontierSignal.SnapshotOneShotOrigins) dto.oneShotSignalOrigins.Add(id);
            foreach (string systemId in Poi.PoiManifestRepository.Ledger.VisitOrder) dto.poiVisitOrder.Add(systemId);

            dto.selectedRouteSystemId = Navigation.SelectedRouteSystemId;
            dto.signalAnalysisTargetSystemId = Navigation.Analysis.TargetSystemId;
            dto.signalAnalysisRemainingSeconds = Navigation.Analysis.RemainingSeconds;

            if (cruise != null)
            {
                dto.flightMode = (int)cruise.Mode;
                dto.flightModeElapsedSeconds = cruise.ElapsedForSave;
                dto.cruiseSpeedMultiplier = cruise.SpeedMultiplier;
                dto.headingLockEngaged = cruise.HeadingLockEngaged;
                dto.headingX = cruise.HeadingLockVector.x;
                dto.headingY = cruise.HeadingLockVector.y;
                dto.headingZ = cruise.HeadingLockVector.z;
            }
            return dto;
        }

        // --- Load ------------------------------------------------------------
        public void LoadNow() => PerformanceRecorder.RecordLoad(LoadNowInternal);

        private void LoadNowInternal()
        {
            SavePayload payload = SaveService.Load(ActiveSlot, out int loadedSchemaVersion, out string loadedCampaignId);
            if (payload == null) { Progression.SessionLog.Log("load_fail", "not_found", null); SetMessage("KAYIT BULUNAMADI"); return; }
            if (payload.seed != seed) { Progression.SessionLog.Log("load_fail", "seed_mismatch", null); SetMessage("KAYIT TOHUMU UYUŞMUYOR"); return; }

            // VS-008 §8: schema v6 introduced the sector/manifest world — there
            // is no migration path from an older save. Reject outright,
            // before touching the save file (SaveService.Load never writes)
            // or any live state, with an explicit incompatibility message.
            if (loadedSchemaVersion < 6)
            {
                Progression.SessionLog.Log("load_fail", "incompatible_schema_v" + loadedSchemaVersion, null);
                SetMessage($"SAVE INCOMPATIBLE — SCHEMA v{loadedSchemaVersion} SAVES ARE NOT SUPPORTED (NEED v6+)");
                return;
            }

            // VS-009 §7: a v6 payload's DrillDto/StructureDto nodeId values use
            // the pre-canonical shape ("{worldId}/node/drill", ".../defense/01"
            // ...) — rewrite them losslessly to the canonical VS-009 shape
            // before any delta reaches live state. v7 payloads are already canonical.
            if (loadedSchemaVersion == 6)
            {
                foreach (SystemDelta d in payload.systemDeltas)
                {
                    foreach (DrillDto dto in d.drills) dto.nodeId = Sectors.NodeIdMigration.Migrate(dto.nodeId);
                    foreach (StructureDto dto in d.structures) dto.nodeId = Sectors.NodeIdMigration.Migrate(dto.nodeId);
                }
            }

            // VS-015 §10: v12->v13 is purely additive -- no rewrite needed. A pre-v13 VehicleDto has
            // shield=armor=hull=0 (RestoreDurability falls back to the vehicle's own full-health
            // default in that case) and engineerTask/escortState both default to active=false (an old
            // vehicle simply never had one). A pre-v13 StructureDto's maintenanceReserve is null on a
            // non-Command structure and, for an old Command structure, a fresh empty Manual-policy
            // reserve at its restored tier's capacity -- exactly the spec's required safe default. An
            // old system's delta has no InterventionGroupDto -> Manual policy, no members, no target.
            // Gated on loadedSchemaVersion, never on any of these fields being null (JsonUtility never
            // leaves a nested [Serializable] field null after deserializing, VS-013's documented gotcha).

            // VS-017A.2 §A2: a load ADOPTS the campaign identity its own envelope carried --
            // it is never a new campaign. A legacy save with no real id (or the old hardcoded
            // "campaign-1" placeholder) is upgraded to one real unique id exactly here, which
            // the next save then writes, so it stays stable instead of churning every load.
            CampaignIdentity.AdoptFromSave(loadedCampaignId);

            Vector3 offset = FloatingOrigin.TotalOffset;

            StarSystemRegistry.Initialize(unchecked((ulong)seed));
            foreach (SystemDelta d in payload.systemDeltas) if (!d.IsEmpty) StarSystemRegistry.Deltas.Set(d);

            var startSector = new SectorCoordinate(0, 0, 0);
            string startSystemId = WorldIdCodec.SystemId(startSector, 0);
            StarSystemRegistry.SetStartSystem(startSector, startSystemId);

            ExtractionState.DestroyAllDrills();
            ExtractionState.DestroyAllStructures();
            ExtractionState.ForceKitState((ExtractionState.KitState)payload.kit);
            // Must happen before Streaming.ResetAndBootFull below, which will
            // repopulate Hangar.Vehicles with whatever the boot system's own
            // delta says belongs there — destroying afterward would undo it.
            Hangar?.DestroyAllVehicles();
            // VS-011: routes are pure data with no GameObject to auto-clean on
            // demotion/reload — an explicit reset here is what makes a repeated
            // manual load never duplicate them (the live Logistics Center, if any, is a
            // real child GameObject and is already destroyed by ResetAndBootFull below).
            LogisticsState.Reset();
            // VS-012: same reasoning as LogisticsState — POI state/registry/manifest cache
            // are pure data with no GameObject to auto-clean; the live PoiObjects (if any)
            // are real child GameObjects, already destroyed by ResetAndBootFull below.
            Poi.PoiLiveState.Reset();
            Poi.PoiObject.ClearRegistry();
            Poi.PoiManifestRepository.Reset();
            // VS-013: same reasoning as LogisticsState/PoiLiveState above — a fresh
            // load must never carry over the previous session's Training/FirstProbe/
            // Recovery state (restored fully below) or a stale enemy registry entry
            // (the live EnemyAgents, if any, are real child GameObjects already
            // destroyed by ResetAndBootFull below).
            Combat.CombatCampaignState.Reset();
            Combat.EnemyAgent.ClearRegistry();
            Ship.CombatInputContext.CombatActive = false;
            Ship.CombatInputContext.TrainingActive = false;

            // Cargo first: reservations for slots/modules are re-derived from
            // its restored physical stock, so it must exist before them.
            // Over-capacity migration: LoadItem writes the exact saved amount
            // regardless of the (possibly smaller, tier-driven) current
            // capacity — nothing is ever trimmed or deleted; Cargo.OverCapacity
            // simply reads true until the player uses/transfers stock down.
            if (logistics != null)
            {
                logistics.Cargo.Clear();
                for (int i = 0; i < payload.cargo.Count; i++)
                    logistics.Cargo.LoadItem(payload.cargo[i].itemId, payload.cargo[i].amount);
            }

            // --- Modules: v1/v2 saves have no moduleTiers at all → migrate to
            // the same "3 free Industry T1, rest T0" a new campaign gets.
            if (modules != null)
            {
                bool isLegacySave = payload.moduleTiers == null || payload.moduleTiers.Count == 0;
                if (isLegacySave)
                {
                    modules.Modules.GrantFreeStartingModules();
                }
                else
                {
                    modules.Modules.Reset();
                    foreach (ModuleTierDto dto in payload.moduleTiers) modules.Modules.SetTier(dto.moduleId, dto.tier);
                }
                modules.ApplyAllFunctionalEffects(); // capacity/slots must match the restored tiers before slots restore below

                // JsonUtility cannot represent a null nested-class field: a
                // payload captured with no active job still round-trips
                // activeModuleJob as a non-null, all-default instance (moduleId
                // stays the C# string default, ""/null) — a bare null-check
                // here would try to RestoreActiveJob(null, ...), and the next
                // Tick() would crash on ModuleProgressionState.SetTier(null, 0)
                // (Dictionary<string,int> rejects a null key). A real captured
                // job always has a real moduleId, so that is the actual signal.
                if (payload.activeModuleJob != null && !string.IsNullOrEmpty(payload.activeModuleJob.moduleId))
                {
                    ModuleJobDto j = payload.activeModuleJob;
                    var resources = new (string, float)[j.reservedResources.Count];
                    for (int i = 0; i < resources.Length; i++)
                        resources[i] = (j.reservedResources[i].itemId, j.reservedResources[i].amount);
                    if (!modules.Upgrades.RestoreActiveJob(j.moduleId, j.fromTier, j.toTier, j.reservedPoints,
                        resources, j.totalSeconds, j.remainingSeconds, j.paused, j.safeStreakSeconds))
                    {
                        // Progression.Restore runs later in this load and would
                        // overwrite the service's rollback. Carry the refund into
                        // that authoritative saved total when an unfunded job is
                        // deliberately stopped.
                        payload.developmentPoints += j.reservedPoints;
                    }
                }
            }

            // --- Processing: restore the v15 bench, or migrate a v3-v14 timed bay
            // into it. Either way the bench is written exactly once per load, so a
            // restart cannot end up with two benches or a doubled amount.
            if (processing != null && logistics != null)
                RestoreProcessing(payload, loadedSchemaVersion, logistics.Cargo, processing.Bench);

            ship.ReleaseStabilize();
            Vector3 shipAbs = new Vector3(payload.ship.px, payload.ship.py, payload.ship.pz);
            ship.transform.position = shipAbs - offset;
            ship.transform.rotation = new Quaternion(payload.ship.rx, payload.ship.ry, payload.ship.rz, payload.ship.rw);
            ship.SetVelocity(new Vector3(payload.ship.vx, payload.ship.vy, payload.ship.vz));
            ship.Throttle = payload.ship.throttle;

            // VS-014.1 §2: must run BEFORE Streaming.ResetAndBootFull below --
            // see RestoreRaidCampaign's own doc comment for why.
            RestoreRaidCampaign(payload, loadedSchemaVersion);

            // VS-008 §8: re-materialize Full wherever the restored ship
            // actually is (falls back to the start system if that position
            // isn't inside any known system's radius) — never a loading
            // screen, never a teleport (the ship's transform above already IS
            // its saved position; this only builds/tears down GameObjects).
            if (Streaming != null)
            {
                SystemManifest bootManifest = StarSystemRegistry.TryFindContaining(shipAbs, out StarSystemRecord found)
                    ? StarSystemRegistry.Manifests.ResolveSystem(found.SystemId)
                    : StarSystemRegistry.Manifests.ResolveSystem(startSystemId);
                Streaming.ResetAndBootFull(bootManifest);
            }

            // Progression: restore WITHOUT awarding (v1 saves migrate to L1/0/empty).
            Progression.ProgressionService.Restore(payload.totalXp, payload.developmentPoints, payload.ledger);

            // --- Navigation: every real (schema v6+, the only kind reachable
            // here now) save always has at least the start system in its
            // knowledge, since InitializeNewCampaign seeds it. This emptiness
            // check is a defensive fallback only — kept for the same reason
            // the moduleTiers/processor ones above are (JsonUtility always
            // default-constructs "navigation" even when absent, so emptiness
            // — not nullness — is the only reliable signal something is missing).
            if (Navigation != null)
            {
                bool isLegacyNavigation = payload.navigation == null || payload.navigation.systemKnowledge == null
                    || payload.navigation.systemKnowledge.Count == 0;
                if (isLegacyNavigation)
                {
                    var controlledWorldIds = new List<string>();
                    foreach (SystemDelta delta in payload.systemDeltas)
                        foreach (DominanceDto dom in delta.dominance)
                            if ((DominanceState)dom.state == DominanceState.Controlled) controlledWorldIds.Add(dom.worldId);
                    Navigation.MigrateLegacySave(controlledWorldIds);
                }
                else
                {
                    NavigationDto nav = payload.navigation;
                    var knowledge = new List<(string, int)>();
                    foreach (SystemKnowledgeDto k in nav.systemKnowledge) knowledge.Add((k.systemId, k.level));
                    var provenance = new List<(string, string)>();
                    foreach (FrontierProvenanceDto p in nav.frontierProvenance) provenance.Add((p.origin, p.target));
                    Navigation.RestoreFromSave(knowledge, nav.selectedRouteSystemId,
                        nav.signalAnalysisTargetSystemId, nav.signalAnalysisRemainingSeconds, provenance);
                    // VS-012: must run AFTER RestoreFromSave (which Clears FrontierSignal first).
                    Navigation.FrontierSignal.RestoreOneShotOrigins(nav.oneShotSignalOrigins);
                    Poi.PoiManifestRepository.Ledger.RestoreOrder(nav.poiVisitOrder);

                    // VS-010 §7: a v7 save has no frontier provenance at all —
                    // knowledge levels above are already restored exactly as
                    // saved; this only backfills the provenance bookkeeping
                    // from currently-Controlled planets, never touching levels.
                    if (loadedSchemaVersion < 8)
                    {
                        var controlledWorldIds = new List<string>();
                        foreach (SystemDelta delta in payload.systemDeltas)
                            foreach (DominanceDto dom in delta.dominance)
                                if ((DominanceState)dom.state == DominanceState.Controlled) controlledWorldIds.Add(dom.worldId);
                        Navigation.RebuildFrontierProvenanceFromControlled(controlledWorldIds);
                    }

                    if (cruise != null)
                    {
                        cruise.RestoreFlightState((FlightMode)nav.flightMode, nav.flightModeElapsedSeconds,
                            Mathf.Max(1f, nav.cruiseSpeedMultiplier), nav.headingLockEngaged,
                            new Vector3(nav.headingX, nav.headingY, nav.headingZ));
                    }
                }
            }

            // --- Hangar (VS-007 v5 / VS-008 v6): an empty "vehicles" list is a
            // legitimate real state (never built one), so — unlike every other
            // migration check above — this one branches on the envelope's
            // actual saved schema version instead of DTO emptiness. Only
            // DOCKED vehicles are restored here; any vehicle away from the
            // ship already came back the instant its system's delta was
            // applied above (Streaming.ResetAndBootFull for the current
            // system; every other system restores its own lazily on promotion).
            if (Hangar != null)
            {
                if (loadedSchemaVersion >= 5)
                {
                    Hangar.RestoreCommandUsed(payload.hangarCommandUsed);
                    Hangar.Production.EnsureSequenceAtLeast(payload.hangarNextVehicleSequence);

                    if (payload.hangarJob != null && payload.hangarJob.active)
                    {
                        HangarJobDto j = payload.hangarJob;
                        if (j.isUpgrade)
                            Hangar.Production.RestoreActiveUpgradeJob(j.upgradeVehicleId, j.reservedParts,
                                j.reservedMaterial, j.reservedSecondMaterial, j.totalSeconds, j.remainingSeconds);
                        else
                            Hangar.Production.RestoreActiveJob((VehicleClass)j.vehicleClass, j.chosenMaterialId,
                                j.reservedParts, j.reservedMaterial, j.totalSeconds, j.remainingSeconds, j.mk);
                    }

                    for (int i = 0; i < payload.vehicles.Count; i++)
                    {
                        VehicleDto v = payload.vehicles[i];
                        VehicleDef def = VehicleCatalog.Get((VehicleClass)v.vehicleClass, v.mk);
                        if (def == null) continue;
                        VehicleAgent agent = Hangar.RestoreVehicle(v.vehicleId, def);
                        agent.RestorePhysical(null, Vector3.zero, Quaternion.identity, Vector3.zero,
                            VehiclePhase.InHangar, MissionKind.None, null, 0f, false, v.heldItemId, v.heldAmount);
                    }
                }
            }

            // VS-010 §7: must run AFTER Streaming.ResetAndBootFull and the
            // Hangar vehicle restore above, since re-linking an already-sent
            // package structure and resuming an evacuation's pending-vehicle
            // wait both need the real (already-restored) objects to exist first.
            if (quickPackage != null) quickPackage.RestoreTransaction(payload.quickPackage);
            if (evacuation != null) evacuation.RestoreTransaction(payload.evacuation);

            // DT-001: a loaded save reuses this same ship GameObject -- the
            // test speed multiplier is session-local and never saved, so it
            // must be forced back to 1x here rather than relying on a fresh
            // ShipController field default that a load never re-triggers.
            ship.ResetTestSpeedMultiplier();

            RestoreCombat(payload, loadedSchemaVersion);

            Progression.SessionLog.Log("load_success", null, null);
            SetMessage("YÜKLENDİ");
            Loaded?.Invoke();
        }

        /// <summary>
        /// VS-017A.2 §5: restores the v15 processing bench, or migrates a v3-v14
        /// timed bay into it.
        /// <para>
        /// Gated on <paramref name="loadedSchemaVersion"/> rather than on
        /// <c>payload.processingBench</c> being non-null, for the same reason every
        /// earlier schema check in this file is: JsonUtility never leaves a nested
        /// <c>[Serializable]</c> field null after deserialization, so a genuine v14
        /// save still yields a non-null, all-default <see cref="ProcessingBenchDto"/>.
        /// Treating that default as real data would restore an empty bench over a
        /// legacy bay and silently destroy whatever the slots were holding.
        /// </para>
        /// </summary>
        private static void RestoreProcessing(SavePayload payload, int loadedSchemaVersion,
            LogisticsCargo cargo, ProcessingBenchRuntime bench)
        {
            // One write per load. Clear first so a restart can never stack a restored
            // bench on top of the one the previous session left in the component.
            bench.Clear();

            if (loadedSchemaVersion >= 15)
            {
                ProcessingBenchDto dto = payload.processingBench;
                if (dto == null) return;

                bench.Restore(dto.inputItemId, dto.inputAmount);
                if (dto.legacyClaims != null)
                    foreach (LegacyProcessingClaimDto claim in dto.legacyClaims)
                        if (claim != null) bench.AddLegacyClaim(claim.itemId, claim.amount);

                ProcessingBenchCommands.DrainLegacyClaims(bench, cargo);
                return;
            }

            // Pre-v15: a timed bay. The arithmetic lives in LegacyProcessingMigration
            // so it can be tested on its own DTOs without a scene; this only chooses
            // which legacy shape to hand it and records what it did.
            List<ProcessorDto> slotDtos = payload.processorSlots;
            if ((slotDtos == null || slotDtos.Count == 0) && payload.processor != null)
                slotDtos = new List<ProcessorDto> { payload.processor };

            ProcessingMigrationReport report = LegacyProcessingMigration.Migrate(slotDtos, cargo, bench);
            Progression.SessionLog.Log("processing_migrated_v15", report.Summary, report.BenchItemId);
        }

        /// <summary>
        /// VS-013 §10 restore + v10-&gt;v11 migration. Per-system enemies/
        /// projectiles/combat wreckage were already restored above by
        /// Streaming.ResetAndBootFull -&gt; MaterializeCombat; only the ship/
        /// campaign-owned top-level state is handled here. Load never
        /// rerolls or duplicates the Probe encounter/salvage/XP.
        ///
        /// Gated on loadedSchemaVersion, NOT on payload.shipCombat/etc being
        /// non-null: JsonUtility never leaves a nested [Serializable] class
        /// field null after deserialization (a genuine pre-v11 save still
        /// yields a non-null, all-default ShipCombatDto/FirstProbeDto/etc) --
        /// treating that default instance as real data would zero out the
        /// ship's Shield/Armor/Hull on every legacy load and would make the
        /// migration branch below unreachable. loadedSchemaVersion is the
        /// only reliable "was this actually written by v11+" signal, exactly
        /// like every earlier schema check in this method (v6, v7).
        /// </summary>
        private void RestoreCombat(SavePayload payload, int loadedSchemaVersion)
        {
            bool hasV11CombatData = loadedSchemaVersion >= 11;

            if (combat != null)
            {
                if (hasV11CombatData && payload.shipCombat != null)
                {
                    var hardpoints = new List<(int, bool, Combat.WeaponFamily, int, float, bool)>();
                    foreach (HardpointDto h in payload.shipCombat.hardpoints)
                        hardpoints.Add((h.index, h.equipped, (Combat.WeaponFamily)h.weaponFamily, h.tier, h.heat, h.overheated));

                    // VS-013.1: Level (Hull Form) and module tiers were already restored above
                    // (moduleTiers ~L448, ProgressionService.Restore ~L525) -- re-derive max
                    // Shield/Armor/Hull/Energy from that NOW-correct state before applying the
                    // saved absolute amounts on top, so they clamp/land against the real loaded
                    // maximums rather than whatever InitializeNewCampaign's fresh-campaign
                    // (Level 1, T0) defaults happened to compute at composition time.
                    combat.ReapplyDurabilityTiers();

                    combat.RestoreFromSave(payload.shipCombat.shield, payload.shipCombat.armor, payload.shipCombat.hull,
                        payload.shipCombat.regenTimer, payload.shipCombat.energy, payload.shipCombat.weaponsLocked,
                        payload.shipCombat.boostRemaining, payload.shipCombat.boostCooldownRemaining, payload.shipCombat.damaged,
                        hardpoints, payload.shipCombat.targetEnemyId);

                    // VS-013.1 §6: an old vs-013-auto-pass-shaped v11 save never wrote these
                    // keys -- JsonUtility leaves repairActive/repairPaused/repairPackageProgress01
                    // at false/false/0 (correctly "no job in flight") and sinceLastDamageSeconds
                    // at its 999f field-initializer default (correctly "not recently damaged"),
                    // never at a misleading 0. No active job -> no reservation to re-make, so
                    // restoring never re-reserves or double-consumes cargo.
                    combat.RestoreRepair(payload.shipCombat.repairActive, payload.shipCombat.repairPaused,
                        payload.shipCombat.repairHullForm, payload.shipCombat.repairPackageProgress01, payload.shipCombat.sinceLastDamageSeconds);

                    if (payload.shipCombat.productionJob != null && payload.shipCombat.productionJob.active)
                    {
                        WeaponProductionJobDto j = payload.shipCombat.productionJob;
                        combat.RestoreProductionJob((Combat.WeaponFamily)j.weaponFamily, j.targetTier, j.reservedParts, j.materialId, j.reservedMaterial, j.totalSeconds, j.remainingSeconds);
                    }
                }

                if (hasV11CombatData && payload.emergencyShutdown != null) combat.Shutdown.RestoreFromSave(payload.emergencyShutdown.active, payload.emergencyShutdown.remainingSeconds);
            }

            if (hasV11CombatData && payload.training != null)
            {
                Combat.TrainingProgress training = Combat.CombatCampaignState.Training;
                RestoreTrainingRun(training.A, payload.training.a);
                RestoreTrainingRun(training.B, payload.training.b);
                RestoreTrainingRun(training.C, payload.training.c);
            }

            if (hasV11CombatData && payload.firstProbe != null)
            {
                Combat.CombatCampaignState.FirstProbe.RestoreFromSave((Combat.FirstProbeStatus)payload.firstProbe.status,
                    payload.firstProbe.warningRemainingSeconds, payload.firstProbe.threatKnown, payload.firstProbe.pendingReconEffect);
            }
            else if (!hasV11CombatData)
            {
                // VS-013 §10 migration (VS-013.1 §4 fix: real defense WEAPON only, never a
                // lone Command Center): a pre-v11 save with an already-active planetary
                // defense weapon schedules exactly one Probe warning now, on this single
                // load -- never re-scheduled again afterward (ScheduleWarningIfNeeded is
                // itself idempotent).
                bool hasAnyActiveDefense = false;
                foreach (SurfaceStructure s in ExtractionState.Structures)
                    if (s != null && SurfaceStructureCatalog.IsActiveDefenseWeapon(s.Type) && s.Runtime.State == DrillLifecycle.Active) { hasAnyActiveDefense = true; break; }
                if (hasAnyActiveDefense) Combat.CombatCampaignState.FirstProbe.ScheduleWarningIfNeeded();
            }

            if (hasV11CombatData && payload.recoverySoftLock != null)
            {
                Combat.CombatCampaignState.Recovery.RestoreFromSave(payload.recoverySoftLock.totalActiveMinutesElapsed,
                    payload.recoverySoftLock.recentDefeatTimestamps, payload.recoverySoftLock.lastVisitedControlledSystemId);
            }

        }

        /// <summary>
        /// VS-014.1 §2: split out of RestoreCombat and called BEFORE
        /// Streaming.ResetAndBootFull (unlike everything else RestoreCombat
        /// restores) — CombatEncounterDirector.Create's own Pirate Base
        /// materialization check reads CombatCampaignState.RaidCampaign
        /// during that boot, so the campaign ledger must already reflect the
        /// loaded save by the time it runs, not still be sitting at
        /// WorldInstaller's fresh-campaign Reset() default. A pre-v12 save
        /// has no raidCampaign at all — that Reset() default (Commander
        /// difficulty, no active raid, nothing taught, empty history, Pirate
        /// Base unspawned) is already the correct state, so only a real v12
        /// payload restores anything here.
        /// </summary>
        private static void RestoreRaidCampaign(SavePayload payload, int loadedSchemaVersion)
        {
            if (loadedSchemaVersion < 12 || payload.raidCampaign == null) return;

            RaidCampaignDto r = payload.raidCampaign;
            var history = new List<Combat.RaidHistoryEntry>();
            foreach (RaidHistoryEntryDto h in r.history)
            {
                var entry = new Combat.RaidHistoryEntry { Stage = (Combat.RaidStage)h.stage, Doctrine = (Combat.RaidDoctrine)h.doctrine, LeaderDefId = h.leaderDefId, PlanetWorldId = h.planetWorldId };
                entry.CompositionDefIds.AddRange(h.compositionDefIds);
                history.Add(entry);
            }
            Combat.CombatCampaignState.RaidCampaign.RestoreFromSave((Combat.RaidDifficulty)r.difficulty, r.activeMajorRaidSystemId,
                r.taughtEnemyDefIds, history, r.pirateBaseSpawned, r.pirateBasePoiId, r.pirateBaseSystemId, r.pirateBaseResolved, r.pirateBaseRewardAwarded);
        }

        private static void RestoreTrainingRun(Combat.TrainingRun run, TrainingRunDto dto)
        {
            if (dto == null) return;
            run.RestoreFromSave(dto.currentStep, dto.stepCompleted?.ToArray());
        }

        private void SetMessage(string text) { LastSaveMessage = text; messageUntil = Time.unscaledTime + 3.5f; NebulaGameplayFeedback.Publish(GameplayFeedbackSource.Save, text); }
    }
}
