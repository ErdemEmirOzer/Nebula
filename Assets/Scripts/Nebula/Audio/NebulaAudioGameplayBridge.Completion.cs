using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Poi;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;

namespace Nebula.Audio
{
    /// <summary>
    /// Completion slice for the produced SFX library. This remains a
    /// presentation observer: it only compares public gameplay state and
    /// starts/stops sound emitters. It owns no gameplay timers or save data.
    /// </summary>
    public sealed partial class NebulaAudioGameplayBridge
    {
        private ShipProcessing processing;
        private ShipMaintenanceReserveAccess reserveAccess;
        private bool headingLocked;
        private bool stabilized;
        private bool buildMode;
        private bool analysisActive;
        private bool orbitGuides;
        private string previousFullSystem;
        private string cruiseReject;
        private bool arrivalAssistPlayed;
        private float worldPulseTimer;
        private float asteroidFlybyCooldown;
        private float sensorPulseTimer;
        private float remoteAudioTimer;
        private BlackHole cachedBlackHole;
        private PirateBaseRuntime observedPirateBase;
        private bool pirateAlerted;

        private ProcessorSnapshot processorAudio;
        private bool processorAudioSeen;
        private readonly Dictionary<string, VehicleSnapshot> vehicleAudio =
            new Dictionary<string, VehicleSnapshot>();
        private readonly Dictionary<string, RouteSnapshot> routeAudio =
            new Dictionary<string, RouteSnapshot>();
        private readonly Dictionary<int, CenterSnapshot> centerAudio =
            new Dictionary<int, CenterSnapshot>();
        private readonly Dictionary<int, StructureSnapshot> structureAudio =
            new Dictionary<int, StructureSnapshot>();
        private readonly Dictionary<string, DefenseSnapshot> defenseAudio =
            new Dictionary<string, DefenseSnapshot>();
        private readonly Dictionary<string, LinkSnapshot> linkAudio =
            new Dictionary<string, LinkSnapshot>();
        private readonly Dictionary<string, bool> abandonedAudio =
            new Dictionary<string, bool>();
        private readonly Dictionary<string, float> wreckageAudio =
            new Dictionary<string, float>();
        private readonly Dictionary<TrainingId, TrainingSnapshot> trainingAudio =
            new Dictionary<TrainingId, TrainingSnapshot>();
        private readonly HashSet<int> stoppedDrillAudio = new HashSet<int>();

        /// <summary>
        /// What the bench looked like last poll. <see cref="Conversions"/> is the
        /// bench's own monotonic transaction count — the one thing that can tell a
        /// committed conversion from a refused one, since a refusal changes neither
        /// the bench nor the hold.
        /// </summary>
        private struct ProcessorSnapshot
        {
            public bool Loaded;
            public string InputItemId;
            public int InputAmount;
            public long Conversions;
            public int LastRecipe;
        }

        private struct VehicleSnapshot
        {
            public VehiclePhase Phase;
            public EngineerPhase? Engineer;
            public EscortStance? Stance;
            public EscortSubState? EscortSubState;
            public MaintenanceResupplyPhase? Resupply;
            public PoiTaskPhase? Poi;
        }

        private struct RouteSnapshot
        {
            public RoutePhase Phase;
            public RouteWaitReason Wait;
        }

        private struct CenterSnapshot
        {
            public bool FillWarning;
            public int Docks;
            public LogisticsCenterTier Tier;
        }

        private struct StructureSnapshot
        {
            public DrillLifecycle State;
            public bool Upgrading;
            public bool Firing;
            public bool Overheated;
            public float PlasmaCooldown;
        }

        private struct DefenseSnapshot
        {
            public DefenseMode Mode;
            public string Focus;
        }

        private struct LinkSnapshot
        {
            public SystemLinkState Link;
            public AttentionPriority Attention;
        }

        private struct TrainingSnapshot
        {
            public int Step;
            public int Failures;
            public bool CanAct;
            public bool Completed;
        }

        private void InitializeCompletionAudio()
        {
            processing = ship.GetComponent<ShipProcessing>();
            reserveAccess = ship.GetComponent<ShipMaintenanceReserveAccess>();
            headingLocked = cruise != null && cruise.HeadingLockEngaged;
            stabilized = ship.IsStabilized;
            buildMode = extraction != null && extraction.BuildMode;
            analysisActive = context.NavigationDirector.Analysis.Active;
            orbitGuides = context.Streaming.OrbitGuidesVisible;
            previousFullSystem = context.Streaming.FullSystemId;
            ship.CollisionResolved += OnShipCollisionResolved;
            RemoteCommandService.CommandCompleted += OnRemoteCommandCompleted;
            context.NavigationDirector.AnalysisStarted += OnAnalysisStarted;
            context.NavigationDirector.AnalysisCompleted += OnAnalysisCompleted;
            context.NavigationDirector.AnalysisCancelled += OnAnalysisCancelled;
            if (reserveAccess != null)
            {
                reserveAccess.Deposited += OnReserveDeposited;
                reserveAccess.Withdrawn += OnReserveWithdrawn;
            }
        }

        private void DisposeCompletionAudio()
        {
            if (ship != null) ship.CollisionResolved -= OnShipCollisionResolved;
            RemoteCommandService.CommandCompleted -= OnRemoteCommandCompleted;
            context.NavigationDirector.AnalysisStarted -= OnAnalysisStarted;
            context.NavigationDirector.AnalysisCompleted -= OnAnalysisCompleted;
            context.NavigationDirector.AnalysisCancelled -= OnAnalysisCancelled;
            if (reserveAccess != null)
            {
                reserveAccess.Deposited -= OnReserveDeposited;
                reserveAccess.Withdrawn -= OnReserveWithdrawn;
            }
            if (observedPirateBase != null)
            {
                observedPirateBase.Hit -= OnPirateBaseHit;
                observedPirateBase.TurretFired -= OnPirateBaseTurret;
                observedPirateBase.Destroyed -= OnPirateBaseDestroyed;
                observedPirateBase.RewardGranted -= OnPirateBaseReward;
            }
        }

        private void SyncCompletionAudio()
        {
            SyncFlightCompletion();
            SyncAnalysisCompletion();
            SyncBuildModeCompletion();
            SyncProcessingCompletion();
        }

        private void SyncCompletionSlowAudio()
        {
            SyncVehicleCompletion();
            SyncLogisticsCompletion();
            SyncStructureCompletion();
            SyncWorldCompletion();
            SyncRemoteCompletion();
            SyncTrainingCompletion();
            SyncPirateBaseCompletion();
            SyncDominanceCompletion();
            SyncWreckageCompletion();
        }

        private void SyncFlightCompletion()
        {
            bool rcs = Cursor.lockState == CursorLockMode.Locked && !ship.RotationLocked
                       && (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.E)
                           || Mathf.Abs(Input.GetAxis("Mouse X")) > 0.02f
                           || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.02f);
            if (rcs) audio.StartLoop("ship/rcs", "audio.ship.rcs.loop", ship.transform, 0.64f);
            else audio.StopLoop("ship/rcs");

            bool locked = cruise != null && cruise.HeadingLockEngaged;
            if (locked != headingLocked)
            {
                audio.PlayOneShot(locked
                    ? "audio.navigation.heading_lock"
                    : "audio.navigation.heading_unlock", ship.transform);
                headingLocked = locked;
            }

            bool nowStabilized = ship.IsStabilized;
            if (nowStabilized != stabilized)
            {
                audio.PlayOneShot(nowStabilized ? "audio.ship.dock" : "audio.ship.launch",
                    ship.transform);
                stabilized = nowStabilized;
            }

            string reject = cruise != null ? cruise.CruiseBlockedReason : null;
            if (!string.IsNullOrEmpty(reject) && reject != cruiseReject)
                audio.PlayOneShot("audio.ship.cruise.rejected", ship.transform);
            cruiseReject = reject;

            string route = context.NavigationDirector.SelectedRouteSystemId;
            if (string.IsNullOrEmpty(route))
            {
                arrivalAssistPlayed = false;
            }
            else if (!arrivalAssistPlayed
                     && context.NavigationWorldProvider.TryGetSystem(route, out StarSystemRecord record))
            {
                float distance = Vector3.Distance(
                    ship.transform.position + FloatingOrigin.TotalOffset,
                    context.NavigationWorldProvider.AbsolutePosition(record));
                if (distance <= Mathf.Max(12000f, record.NavigationRadius * 1.2f))
                {
                    arrivalAssistPlayed = true;
                    audio.PlayOneShot("audio.navigation.arrival_assist");
                }
            }

            bool anyOverheat = false;
            if (shipCombat.Hardpoints != null)
                foreach (HardpointState hp in shipCombat.Hardpoints)
                    if (hp != null && hp.Overheated) { anyOverheat = true; break; }
            if (anyOverheat)
                audio.StartLoop("ship/overheat-warning", "audio.ship.overheat_warning",
                    ship.transform, 0.72f);
            else audio.StopLoop("ship/overheat-warning");
        }

        private void SyncAnalysisCompletion()
        {
            bool active = context.NavigationDirector.Analysis.Active;
            if (active)
                audio.StartLoop("signal/analysis", "audio.signal.analysis.loop");
            else audio.StopLoop("signal/analysis");
            analysisActive = active;
        }

        private void SyncBuildModeCompletion()
        {
            bool active = extraction != null && extraction.BuildMode;
            if (active != buildMode)
            {
                if (active)
                    audio.StartLoop("build/hologram", "audio.build.hologram.loop",
                        ship.transform, 0.58f);
                else audio.StopLoop("build/hologram");
                buildMode = active;
            }
        }

        /// <summary>
        /// One bench, one sound per transaction.
        /// <para>
        /// The bay this observed used to be N slots each on a timer, so the loops
        /// covered a batch's duration and completion was a slot's output counter
        /// rising. A conversion has no duration now: the loops cover the bench being
        /// loaded, and the only completion signal is the bench's own conversion count
        /// moving. Comparing counts rather than reacting to a call is what keeps a
        /// refused conversion silent — a refusal leaves the count where it was — and
        /// what makes two conversions between two polls one completion sound rather
        /// than two.
        /// </para>
        /// </summary>
        private void SyncProcessingCompletion()
        {
            ProcessingBenchRuntime bench = processing != null ? processing.Bench : null;
            if (bench == null) return;

            ProcessorSnapshot now = new ProcessorSnapshot
            {
                Loaded = !bench.IsEmpty,
                InputItemId = bench.InputItemId,
                InputAmount = bench.InputAmount,
                Conversions = bench.ConversionsTotal,
                LastRecipe = bench.LastRecipeIndex
            };

            if (!processorAudioSeen)
            {
                processorAudioSeen = true;
                processorAudio = now;
                if (now.Loaded) StartProcessorAudio(now.InputItemId);
                return;
            }

            ProcessorSnapshot before = processorAudio;
            processorAudio = now;

            // Loading the bench, or adding to what is already on it.
            if (now.Loaded && (!before.Loaded || before.InputItemId != now.InputItemId
                               || now.InputAmount > before.InputAmount))
            {
                audio.PlayOneShot("audio.processing.start", ship.transform);
                StartProcessorAudio(now.InputItemId);
            }

            if (before.Conversions < now.Conversions)
            {
                audio.PlayOneShot("audio.processing.complete", ship.transform);
                if (!now.Loaded) StopProcessorAudio();
                return;
            }

            // The bench emptied with no conversion behind it: the player took the
            // input back. That is the withdrawal the timed model called a cancel.
            if (before.Loaded && !now.Loaded)
            {
                StopProcessorAudio();
                audio.PlayOneShot("audio.processing.cancel", ship.transform);
            }
        }

        private void StartProcessorAudio(string inputItemId)
        {
            audio.StartLoop("processing/bench/core",
                "audio.processing.core.loop", ship.transform, 0.58f);
            string material = ProcessingLoopForInput(inputItemId);
            if (material != null)
                audio.StartLoop("processing/bench/material", material,
                    ship.transform, 0.62f);
        }

        private void StopProcessorAudio()
        {
            audio.StopLoop("processing/bench/core");
            audio.StopLoop("processing/bench/material");
        }

        /// <summary>
        /// The material loop for whatever raw family is on the bench, derived from the
        /// catalog's own id order rather than a recipe index the bench no longer has
        /// while it is merely loaded.
        /// </summary>
        private static string ProcessingLoopForInput(string inputItemId)
        {
            if (string.IsNullOrEmpty(inputItemId)) return null;
            for (int f = 0; f < ExtractionCatalog.ResourceIds.Length; f++)
            {
                if (ExtractionCatalog.ResourceIds[f] != inputItemId) continue;
                return f switch
                {
                    0 => "audio.processing.metal.loop",
                    1 => "audio.processing.wood.loop",
                    2 => "audio.processing.gas.loop",
                    3 => "audio.processing.water.loop",
                    _ => "audio.processing.diamond.loop"
                };
            }
            return null;
        }

        private void SyncVehicleCompletion()
        {
            var live = new HashSet<string>();
            foreach (VehicleAgent vehicle in context.Hangar.Vehicles)
            {
                if (vehicle == null || string.IsNullOrEmpty(vehicle.VehicleId)) continue;
                string id = vehicle.VehicleId;
                live.Add(id);
                VehicleSnapshot now = VehicleSnapshotFor(vehicle);
                if (!vehicleAudio.TryGetValue(id, out VehicleSnapshot before))
                {
                    vehicleAudio[id] = now;
                    continue;
                }

                if (before.Phase == VehiclePhase.InHangar && now.Phase == VehiclePhase.Launching)
                {
                    audio.PlayOneShot("audio.hangar.bay.open", vehicle.transform);
                    audio.PlayOneShot("audio.vehicle.launch", vehicle.transform);
                }
                if (before.Phase == VehiclePhase.Docking && now.Phase == VehiclePhase.InHangar)
                {
                    audio.PlayOneShot("audio.vehicle.dock", vehicle.transform);
                    audio.PlayOneShot("audio.hangar.bay.close", vehicle.transform);
                }
                if (before.Phase != VehiclePhase.OnStation && now.Phase == VehiclePhase.OnStation)
                {
                    if (vehicle.Def.Class == VehicleClass.Scout)
                        audio.PlayOneShot("audio.vehicle.scout.scan", vehicle.transform);
                    if (vehicle.Def.Class == VehicleClass.Collector)
                        audio.PlayOneShot("audio.vehicle.collector.transfer", vehicle.transform);
                }
                if (before.Engineer != now.Engineer)
                {
                    if (now.Engineer == EngineerPhase.Working)
                        audio.PlayOneShot("audio.vehicle.engineer.repair", vehicle.transform);
                    if (now.Engineer == EngineerPhase.WaitForSafety)
                        audio.PlayOneShot("audio.vehicle.engineer.rescue", vehicle.transform);
                }
                if (before.Stance != now.Stance && now.Stance.HasValue)
                    audio.PlayOneShot("audio.vehicle.escort.stance", vehicle.transform);
                if (before.EscortSubState != EscortSubState.Retreating
                    && now.EscortSubState == EscortSubState.Retreating)
                    audio.PlayOneShot("audio.vehicle.escort.retreat", vehicle.transform);
                if (!before.Resupply.HasValue && now.Resupply.HasValue)
                    audio.PlayOneShot("audio.maintenance.resupply.assigned", vehicle.transform);
                if (before.Resupply.HasValue
                    && now.Resupply == MaintenanceResupplyPhase.Completed)
                    audio.PlayOneShot("audio.maintenance.resupply.complete", vehicle.transform);
                if (before.Poi != PoiTaskPhase.Working && now.Poi == PoiTaskPhase.Working)
                    audio.PlayOneShot("audio.poi.anomaly.signal", vehicle.transform);
                vehicleAudio[id] = now;
            }
            foreach (string id in new List<string>(vehicleAudio.Keys))
                if (!live.Contains(id)) vehicleAudio.Remove(id);
        }

        private static VehicleSnapshot VehicleSnapshotFor(VehicleAgent vehicle) =>
            new VehicleSnapshot
            {
                Phase = vehicle.Mission.Phase,
                Engineer = vehicle.EngineerTask != null ? vehicle.EngineerTask.Phase : (EngineerPhase?)null,
                Stance = vehicle.EscortState != null ? vehicle.EscortState.Stance : (EscortStance?)null,
                EscortSubState = vehicle.EscortState != null ? vehicle.EscortState.SubState : (EscortSubState?)null,
                Resupply = vehicle.ResupplyTask != null ? vehicle.ResupplyTask.Phase : (MaintenanceResupplyPhase?)null,
                Poi = vehicle.PoiTask != null ? vehicle.PoiTask.Phase : (PoiTaskPhase?)null
            };

        private void SyncLogisticsCompletion()
        {
            var liveRoutes = new HashSet<string>();
            foreach (CollectorRoute route in LogisticsState.Routes)
            {
                if (route == null) continue;
                liveRoutes.Add(route.RouteId);
                RouteSnapshot now = new RouteSnapshot { Phase = route.Phase, Wait = route.WaitReason };
                if (!routeAudio.TryGetValue(route.RouteId, out RouteSnapshot before))
                {
                    routeAudio[route.RouteId] = now;
                    audio.PlayOneShot("audio.logistics.route.assigned");
                    continue;
                }
                if (before.Phase != now.Phase)
                {
                    if (now.Phase == RoutePhase.Paused)
                        audio.PlayOneShot("audio.logistics.route.paused");
                    else if (before.Phase == RoutePhase.Paused)
                        audio.PlayOneShot("audio.logistics.route.resumed");
                }
                routeAudio[route.RouteId] = now;
                string key = "route/" + route.RouteId;
                if (now.Phase == RoutePhase.Loading)
                    audio.StartLoop(key, "audio.logistics.load.loop");
                else if (now.Phase == RoutePhase.Unloading)
                    audio.StartLoop(key, "audio.logistics.unload.loop");
                else audio.StopLoop(key);
            }
            foreach (string id in new List<string>(routeAudio.Keys))
            {
                if (liveRoutes.Contains(id)) continue;
                audio.StopLoop("route/" + id);
                routeAudio.Remove(id);
            }

            var liveCenters = new HashSet<int>();
            foreach (LogisticsCenter center in LogisticsState.Centers)
            {
                if (center?.Runtime == null) continue;
                int id = center.GetInstanceID();
                liveCenters.Add(id);
                CenterSnapshot now = new CenterSnapshot
                {
                    FillWarning = center.FillWarningActive,
                    Docks = center.DocksInUse,
                    Tier = center.Runtime.Tier
                };
                if (centerAudio.TryGetValue(id, out CenterSnapshot before))
                {
                    if (!before.FillWarning && now.FillWarning)
                        audio.PlayOneShot("audio.logistics.fill_warning", center.transform);
                    if (before.Docks < now.Docks && now.Docks >= center.Runtime.Def.DockCount)
                        audio.PlayOneShot("audio.logistics.dock_queue", center.transform);
                }
                centerAudio[id] = now;
                if (center.Runtime.IsActive)
                    audio.StartLoop("logistics/" + id,
                        now.Tier == LogisticsCenterTier.T2
                            ? "audio.structure.logistics_t2.loop"
                            : "audio.structure.logistics_t1.loop", center.transform);
                else audio.StopLoop("logistics/" + id);
            }
            foreach (int id in new List<int>(centerAudio.Keys))
                if (!liveCenters.Contains(id))
                {
                    audio.StopLoop("logistics/" + id);
                    centerAudio.Remove(id);
                }
        }

        private void SyncStructureCompletion()
        {
            var live = new HashSet<int>();
            foreach (SurfaceStructure structure in ExtractionState.Structures)
            {
                if (structure?.Runtime == null) continue;
                int id = structure.GetInstanceID();
                live.Add(id);
                StructureSnapshot now = new StructureSnapshot
                {
                    State = structure.Runtime.State,
                    Upgrading = structure.ActiveUpgradeJob != null,
                    Firing = structure.DefenseContinuousFireSeconds > 0.0001f,
                    Overheated = structure.DefenseOverheatRemaining > 0.0001f,
                    PlasmaCooldown = structure.DefensePlasmaCooldownRemaining
                };
                if (structureAudio.TryGetValue(id, out StructureSnapshot before))
                {
                    if (before.State == DrillLifecycle.CapsuleInTransit
                        && now.State == DrillLifecycle.Constructing)
                        audio.PlayOneShot("audio.build.capsule.impact", structure.transform);
                    if (!before.Upgrading && now.Upgrading)
                        audio.PlayOneShot("audio.build.upgrade.start", structure.transform);
                    if (before.Upgrading && !now.Upgrading && now.State == DrillLifecycle.Active)
                        audio.PlayOneShot("audio.build.upgrade.complete", structure.transform);
                    if (before.State != DrillLifecycle.Dismantled
                        && now.State == DrillLifecycle.Dismantled)
                        audio.PlayOneShot("audio.build.structure.dismantle", structure.transform);
                    if (!before.Firing && now.Firing
                        && structure.Type == SurfaceStructureType.Laser)
                    {
                        audio.PlayOneShot("audio.defense.laser.fire", structure.transform);
                        audio.StartLoop("defense/laser/" + id,
                            "audio.defense.laser.loop", structure.transform);
                    }
                    if (before.Firing && !now.Firing
                        && structure.Type == SurfaceStructureType.Laser)
                    {
                        audio.StopLoop("defense/laser/" + id);
                        audio.PlayOneShot("audio.defense.laser.end", structure.transform);
                    }
                    if (before.PlasmaCooldown <= 0.0001f
                        && now.PlasmaCooldown > 0.0001f
                        && structure.Type == SurfaceStructureType.Plasma)
                        audio.PlayOneShot("audio.defense.plasma.fire", structure.transform);
                    if (before.State != DrillLifecycle.Disabled
                        && now.State == DrillLifecycle.Disabled
                        && SurfaceStructureCatalog.IsActiveDefenseWeapon(structure.Type))
                        audio.PlayOneShot("audio.defense.disabled", structure.transform);
                }
                else if (now.State == DrillLifecycle.CapsuleInTransit)
                    audio.PlayOneShot("audio.build.capsule.launch", structure.transform);
                structureAudio[id] = now;

            }
            sensorPulseTimer -= 0.2f;
            if (sensorPulseTimer <= 0f)
            {
                sensorPulseTimer = 8f;
                foreach (SurfaceStructure structure in ExtractionState.Structures)
                    if (structure != null && structure.Type == SurfaceStructureType.SensorArray
                        && structure.Runtime.IsActive)
                    {
                        audio.PlayOneShot("audio.structure.sensor_array.pulse", structure.transform);
                        break;
                    }
            }
            foreach (int id in new List<int>(structureAudio.Keys))
                if (!live.Contains(id))
                {
                    audio.StopLoop("defense/laser/" + id);
                    structureAudio.Remove(id);
                }

            CombatEncounterDirector encounter = context.Streaming.CombatEncounter;
            if (encounter != null)
            {
                SystemManifest defenseManifest = StarSystemRegistry.Manifests?
                    .ResolveSystem(context.Streaming.FullSystemId);
                if (defenseManifest == null) return;
                foreach (PlanetManifest planet in defenseManifest.Planets)
                {
                    DefenseSnapshot now = new DefenseSnapshot
                    {
                        Mode = encounter.DefenseCoordinator.GetMode(planet.PlanetId),
                        Focus = encounter.DefenseCoordinator.GetFocus(planet.PlanetId)
                    };
                    if (defenseAudio.TryGetValue(planet.PlanetId, out DefenseSnapshot before))
                    {
                        if (before.Mode != now.Mode)
                            audio.PlayOneShot("audio.defense.coordinator.mode");
                        if (before.Focus != now.Focus && !string.IsNullOrEmpty(now.Focus))
                            audio.PlayOneShot("audio.defense.focus_set");
                    }
                    defenseAudio[planet.PlanetId] = now;
                }
            }

            foreach (DrillStructure drill in ExtractionState.Drills)
            {
                if (drill?.Runtime == null || drill.Runtime.State != DrillLifecycle.Active) continue;
                if (!drill.Runtime.IsProducing)
                {
                    if (stoppedDrillAudio.Add(drill.GetInstanceID()))
                    {
                        audio.PlayOneShot("audio.drill.production_stopped", drill.transform);
                    }
                }
                else if (stoppedDrillAudio.Remove(drill.GetInstanceID()))
                {
                    audio.PlayOneShot("audio.drill.production_resumed", drill.transform);
                }
            }
        }

        private void SyncWorldCompletion()
        {
            worldPulseTimer += 0.2f;
            asteroidFlybyCooldown = Mathf.Max(0f, asteroidFlybyCooldown - 0.2f);

            EnvironmentClass environment = extraction?.TargetProfile != null
                ? extraction.TargetProfile.Environment : EnvironmentClass.Calm;
            string environmentId = environment switch
            {
                EnvironmentClass.Harsh => "audio.world.environment.harsh.loop",
                EnvironmentClass.Extreme => "audio.world.environment.extreme.loop",
                _ => "audio.world.environment.calm.loop"
            };
            audio.StartLoop("world/environment", environmentId,
                context.Streaming.BootStarTransform, 0.46f);

            Asteroid nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (Asteroid asteroid in Asteroid.All)
            {
                if (asteroid == null) continue;
                float d = Vector3.Distance(ship.transform.position, asteroid.transform.position);
                if (d < nearestDistance) { nearestDistance = d; nearest = asteroid; }
            }
            if (nearest != null && nearestDistance < 12000f)
                audio.StartLoop("world/asteroids", "audio.world.asteroid_field.loop",
                    nearest.transform, 0.52f);
            else audio.StopLoop("world/asteroids");
            if (nearest != null && nearestDistance < 650f && asteroidFlybyCooldown <= 0f
                && ship.Velocity.magnitude > 80f)
            {
                asteroidFlybyCooldown = 4f;
                audio.PlayOneShot(((Mathf.FloorToInt(worldPulseTimer) & 1) == 0)
                    ? "audio.world.asteroid.flyby.01"
                    : "audio.world.asteroid.flyby.02", nearest.transform);
            }

            if (cachedBlackHole == null)
                cachedBlackHole = Object.FindObjectOfType<BlackHole>();
            if (cachedBlackHole != null)
            {
                float d = Vector3.Distance(ship.transform.position, cachedBlackHole.transform.position);
                if (d < 100000f)
                {
                    audio.StopLoop("world/black-hole-far");
                    audio.StartLoop("world/black-hole-near",
                        "audio.world.black_hole.near.loop", cachedBlackHole.transform, 0.76f);
                }
                else
                {
                    audio.StopLoop("world/black-hole-near");
                    audio.StartLoop("world/black-hole-far",
                        "audio.world.black_hole.far.loop", cachedBlackHole.transform, 0.48f);
                }
            }

            SafetyMarkerAnchor marker = context.SafetyMarkerAnchor;
            if (marker != null)
            {
                float distance = Vector3.Distance(ship.transform.position, marker.transform.position);
                if (distance < 12000f)
                    audio.StartLoop("world/safety-marker", "audio.world.safety_marker.loop",
                        marker.transform, 0.58f);
                else audio.StopLoop("world/safety-marker");
                if (distance < 2500f && worldPulseTimer >= 5f)
                {
                    worldPulseTimer = 0f;
                    audio.PlayOneShot("audio.world.safety_marker.pulse", marker.transform);
                }
            }

            bool guides = context.Streaming.OrbitGuidesVisible;
            if (guides != orbitGuides)
            {
                audio.PlayOneShot(guides
                    ? "audio.world.orbit_guides.on"
                    : "audio.world.orbit_guides.off");
                orbitGuides = guides;
            }
        }

        private void SyncRemoteCompletion()
        {
            remoteAudioTimer -= 0.2f;
            if (remoteAudioTimer > 0f) return;
            remoteAudioTimer = 1f;
            var nearby = new HashSet<string>();
            foreach (StarSystemRecord record in context.NavigationDirector.SystemsNearShip())
            {
                if (record.SystemId == context.Streaming.FullSystemId) continue;
                nearby.Add(record.SystemId);
                SystemLinkState link = SystemStatusQuery.Link(record.SystemId,
                    context.Streaming.FullSystemId, context.NavigationDirector.Knowledge);
                AttentionCard? card = AttentionResolver.ResolveForSystem(record.SystemId,
                    AttentionQuery.BuildInputs(record.SystemId, context.Streaming.FullSystemId,
                        context.NavigationDirector.Knowledge));
                AttentionPriority priority = card?.Priority ?? AttentionPriority.None;
                LinkSnapshot now = new LinkSnapshot { Link = link, Attention = priority };
                if (linkAudio.TryGetValue(record.SystemId, out LinkSnapshot before))
                {
                    if (before.Link != now.Link)
                    {
                        audio.PlayOneShot(now.Link switch
                        {
                            SystemLinkState.Live => "audio.remote.link_live",
                            SystemLinkState.LastKnown => "audio.remote.last_known",
                            _ => "audio.remote.disconnected"
                        });
                        if (now.Link == SystemLinkState.Live
                            && before.Link != SystemLinkState.Live)
                        {
                            audio.PlayOneShot("audio.remote.reconnect_summary");
                            audio.PlayOneShot("audio.remote.summary");
                        }
                    }
                    if (before.Attention != now.Attention && now.Attention != AttentionPriority.None)
                    {
                        audio.PlayOneShot(now.Attention switch
                        {
                            AttentionPriority.Critical => "audio.remote.attention_high",
                            AttentionPriority.High => "audio.remote.attention_high",
                            AttentionPriority.Medium => "audio.remote.attention_medium",
                            _ => "audio.remote.attention_low"
                        });
                        if (now.Attention == AttentionPriority.Critical)
                            audio.PlayOneShot("audio.ui.notification_critical");
                    }
                    else if (before.Attention != AttentionPriority.None
                             && now.Attention == AttentionPriority.None)
                        audio.PlayOneShot("audio.remote.attention_low");
                }
                linkAudio[record.SystemId] = now;
            }
            foreach (string id in new List<string>(linkAudio.Keys))
                if (!nearby.Contains(id)) linkAudio.Remove(id);
        }

        private void SyncTrainingCompletion()
        {
            foreach (TrainingId id in new[] { TrainingId.A, TrainingId.B, TrainingId.C })
            {
                TrainingRun run = CombatCampaignState.Training.Get(id);
                if (run == null) continue;
                TrainingSnapshot now = new TrainingSnapshot
                {
                    Step = run.CurrentStep,
                    Failures = run.FailuresThisStep,
                    CanAct = run.CanAct,
                    Completed = run.Completed
                };
                if (trainingAudio.TryGetValue(id, out TrainingSnapshot before))
                {
                    if (now.Step > before.Step)
                    {
                        audio.PlayOneShot("audio.training.valid");
                        audio.PlayOneShot("audio.training.step");
                    }
                    if (now.Failures > before.Failures)
                        audio.PlayOneShot("audio.training.invalid");
                    if (!before.CanAct && now.CanAct)
                        audio.PlayOneShot("audio.training.retry");
                    if (!before.Completed && now.Completed)
                        audio.PlayOneShot("audio.training.complete");
                }
                trainingAudio[id] = now;
            }
        }

        private void SyncDominanceCompletion()
        {
            SystemManifest manifest = StarSystemRegistry.Manifests?
                .ResolveSystem(context.Streaming.FullSystemId);
            if (manifest == null) return;
            bool allControlled = manifest.Planets.Count > 0;
            foreach (PlanetManifest planet in manifest.Planets)
            {
                PlanetDominance dominance = ExtractionState.GetDominance(planet.PlanetId);
                bool abandoned = dominance != null && dominance.Abandoned;
                if (abandonedAudio.TryGetValue(planet.PlanetId, out bool before)
                    && before != abandoned)
                    audio.PlayOneShot(abandoned
                        ? "audio.dominance.abandoned"
                        : "audio.dominance.reclaim");
                abandonedAudio[planet.PlanetId] = abandoned;
                if (dominance == null || dominance.State != DominanceState.Controlled || abandoned)
                    allControlled = false;
            }
            string key = manifest.SystemId + "/full-dominance";
            if (allControlled)
            {
                if (!abandonedAudio.ContainsKey(key))
                {
                    abandonedAudio[key] = true;
                    audio.PlayOneShot("audio.dominance.full_system");
                }
            }
            else abandonedAudio.Remove(key);
        }

        private void SyncWreckageCompletion()
        {
            CombatEncounterDirector encounter = context.Streaming.CombatEncounter;
            if (encounter == null) return;
            var live = new HashSet<string>();
            foreach (CombatWreckageState wreckage in encounter.Wreckage)
            {
                if (wreckage == null) continue;
                live.Add(wreckage.WreckageId);
                float remaining = 0f;
                foreach (CombatWreckageNode node in wreckage.Nodes)
                    remaining += node.Remaining;
                if (!wreckageAudio.TryGetValue(wreckage.WreckageId, out float before))
                {
                    wreckageAudio[wreckage.WreckageId] = remaining;
                    audio.PlayOneShot("audio.ship.wreckage");
                    continue;
                }
                if (remaining < before - 0.0001f)
                    audio.PlayOneShot("audio.poi.wreck.salvage_node");
                wreckageAudio[wreckage.WreckageId] = remaining;
            }
            foreach (string id in new List<string>(wreckageAudio.Keys))
                if (!live.Contains(id)) wreckageAudio.Remove(id);
        }

        private void SyncPirateBaseCompletion()
        {
            PirateBaseRuntime current = context.Streaming.CombatEncounter?.PirateBase;
            if (current != observedPirateBase)
            {
                if (observedPirateBase != null)
                {
                    observedPirateBase.Hit -= OnPirateBaseHit;
                    observedPirateBase.TurretFired -= OnPirateBaseTurret;
                    observedPirateBase.Destroyed -= OnPirateBaseDestroyed;
                    observedPirateBase.RewardGranted -= OnPirateBaseReward;
                }
                audio.StopLoop("pirate-base/local");
                observedPirateBase = current;
                pirateAlerted = false;
                if (current != null)
                {
                    current.Hit += OnPirateBaseHit;
                    current.TurretFired += OnPirateBaseTurret;
                    current.Destroyed += OnPirateBaseDestroyed;
                    current.RewardGranted += OnPirateBaseReward;
                    audio.PlayOneShot("audio.pirate_base.signature", current.transform);
                    if (!current.Resolved)
                        audio.StartLoop("pirate-base/local",
                            "audio.pirate_base.local_loop", current.transform, 0.64f);
                }
            }
            if (current != null && !current.Resolved && !pirateAlerted
                && Vector3.Distance(ship.transform.position, current.transform.position) < 9000f)
            {
                pirateAlerted = true;
                audio.PlayOneShot("audio.pirate_base.alert", current.transform);
            }
        }

        private void OnShipCollisionResolved(float speed)
        {
            bool heavy = speed >= 120f;
            damageVariation = 1 + (damageVariation % 2);
            audio.PlayOneShot(heavy
                ? $"audio.ship.collision_heavy.{damageVariation:00}"
                : $"audio.ship.collision_light.{damageVariation:00}", ship.transform);
            audio.PlayOneShot(heavy
                ? "audio.world.debris.heavy"
                : $"audio.world.debris.light.{damageVariation:00}", ship.transform, 0.62f);
        }

        private void OnReserveDeposited(float _) =>
            audio.PlayOneShot("audio.maintenance.reserve.deposit", ship.transform);

        private void OnReserveWithdrawn(float _) =>
            audio.PlayOneShot("audio.maintenance.reserve.withdraw", ship.transform);

        private void OnPirateBaseHit() =>
            audio.PlayOneShot("audio.pirate_base.hit", observedPirateBase?.transform);

        private void OnPirateBaseTurret() =>
            audio.PlayOneShot("audio.pirate_base.turret", observedPirateBase?.transform);

        private void OnPirateBaseDestroyed()
        {
            audio.StopLoop("pirate-base/local");
            audio.PlayOneShot("audio.pirate_base.destroyed", observedPirateBase?.transform);
        }

        private void OnPirateBaseReward() =>
            audio.PlayOneShot("audio.pirate_base.reward");

        private void OnRemoteCommandCompleted(RemoteCommandRejectReason result) =>
            audio.PlayOneShot(result == RemoteCommandRejectReason.None
                ? "audio.remote.command_accept"
                : "audio.remote.command_reject");

        private void OnAnalysisStarted()
        {
            audio.PlayOneShot("audio.signal.analysis.start");
            audio.StartLoop("signal/analysis", "audio.signal.analysis.loop");
        }

        private void OnAnalysisCompleted()
        {
            audio.StopLoop("signal/analysis");
            audio.PlayOneShot("audio.signal.analysis.complete");
        }

        private void OnAnalysisCancelled()
        {
            audio.StopLoop("signal/analysis");
            audio.PlayOneShot("audio.ui.cancel");
        }
    }
}
