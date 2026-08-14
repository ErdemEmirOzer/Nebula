using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Modules;
using Nebula.Navigation;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;
using Nebula.World.Composition;
using Nebula.Combat;
using Nebula.Poi;

namespace Nebula.Audio
{
    /// <summary>
    /// Thin presentation observer: converts already-authoritative gameplay
    /// state/events into audio. It never advances timers, mutates gameplay or
    /// writes saves. Polling is limited to presentation state that has no
    /// event seam (flight/nearby materialized emitters).
    /// </summary>
    [DefaultExecutionOrder(250)]
    public sealed partial class NebulaAudioGameplayBridge : MonoBehaviour
    {
        private NebulaContext context;
        private new NebulaAudioService audio;
        private ShipController ship;
        private ShipCruise cruise;
        private ShipExtraction extraction;
        private ShipCombat shipCombat;
        private Combat.ShipCombatState combatState;
        private CombatEncounterDirector observedEncounter;
        private bool initialized;

        private bool thrusting;
        private bool localBoost;
        private bool combatBoost;
        private bool energyLow;
        private bool scanning;
        private bool transferring;
        private bool mapOpen;
        private string selectedMapSystem;
        private string selectedMapPlanet;
        private string selectedWaypoint;
        private FlightMode cruiseMode;
        private float emitterPollTimer;
        private float laserHoldUntil;
        private float previousEmLockProgress;
        private bool raidWarningHigh;
        private RaidPhase raidPhase;
        private int damageVariation;
        private float fireDeniedCooldown;

        private readonly Dictionary<int, DrillLifecycle> drillStates =
            new Dictionary<int, DrillLifecycle>();
        private readonly Dictionary<int, DrillLifecycle> structureStates =
            new Dictionary<int, DrillLifecycle>();
        private readonly Dictionary<string, DominanceState> dominanceStates =
            new Dictionary<string, DominanceState>();
        private readonly HashSet<string> knownVehicles = new HashSet<string>();
        private readonly HashSet<string> knownEnemies = new HashSet<string>();
        private readonly Dictionary<string, EnemyAudioSnapshot> enemyStates =
            new Dictionary<string, EnemyAudioSnapshot>();
        private readonly Dictionary<string, PoiAudioSnapshot> poiStates =
            new Dictionary<string, PoiAudioSnapshot>();
        private readonly Dictionary<string, ProjectileKind> knownProjectiles =
            new Dictionary<string, ProjectileKind>();
        private readonly Dictionary<int, bool> hardpointOverheated =
            new Dictionary<int, bool>();
        private readonly HashSet<int> fullDrills = new HashSet<int>();
        private readonly Dictionary<int, bool> drillUpgradeStates =
            new Dictionary<int, bool>();

        private struct EnemyAudioSnapshot
        {
            public bool Telegraphing;
            public EnemyAiPhase Phase;
            public float LootVolume;
            public bool LootFull;
            public float Shield;
            public float Armor;
            public float Hull;
            public bool Disrupted;
            public bool ScanComplete;
            public bool HasLootTarget;
            public EnemyRole Role;
        }

        private struct PoiAudioSnapshot
        {
            public PoiKnowledgeLevel Level;
            public FacilityChoice Choice;
            public bool Depleted;
            public int ActiveSalvageNodes;
        }

        public void Initialize(NebulaContext runtimeContext, NebulaAudioService service)
        {
            context = runtimeContext;
            audio = service;
            ship = context.Ship;
            cruise = ship.GetComponent<ShipCruise>();
            extraction = ship.GetComponent<ShipExtraction>();
            shipCombat = context.ShipCombat;
            combatState = shipCombat.State;

            SubscribeToStaticEvents();
            context.NavigationDirector.FrontierSignal.OriginOpened += OnFrontierSignal;
            context.Streaming.FullSystemChangedByRealMovement += OnFullSystemChanged;
            context.ExtractionDirector.Saved += OnSaved;
            context.ExtractionDirector.Loaded += OnLoaded;
            shipCombat.WeaponFired += OnWeaponFired;
            shipCombat.FireDenied += OnFireDenied;
            shipCombat.TargetChanged += OnTargetChanged;
            shipCombat.DamageApplied += OnDamageApplied;
            shipCombat.EmergencyShutdownStarted += OnEmergencyShutdown;
            shipCombat.RepairStarted += OnRepairStarted;
            shipCombat.RepairCompleted += OnRepairCompleted;
            shipCombat.RepairCancelled += OnRepairCancelled;

            if (context.Modules.Upgrades != null)
            {
                context.Modules.Upgrades.JobStarted += OnModuleStarted;
                context.Modules.Upgrades.JobCompleted += OnModuleCompleted;
                context.Modules.Upgrades.JobCancelled += OnModuleCancelled;
            }
            if (context.Hangar.Production != null)
            {
                context.Hangar.Production.JobStarted += OnHangarStarted;
                context.Hangar.Production.JobCompleted += OnHangarCompleted;
                context.Hangar.Production.JobUpgradeCompleted += OnHangarUpgradeCompleted;
                context.Hangar.Production.JobCancelled += OnHangarCancelled;
            }

            audio.StartLoop("listener/deep-space", "audio.world.deep_space.loop");
            audio.StartLoop("ship/cockpit", "audio.ship.cockpit_ambience.loop");
            audio.StartLoop("ship/engine-idle", "audio.ship.engine_idle.loop", ship.transform);
            SetSystemAmbience(context.Streaming.FullSystemId);
            cruiseMode = cruise != null ? cruise.Mode : FlightMode.LocalFlight;
            mapOpen = context.Presentation.StrategicMapScreen != null
                      && context.Presentation.StrategicMapScreen.IsOpen;
            selectedWaypoint = context.NavigationDirector.SelectedRouteSystemId;
            ObserveEncounter(context.Streaming.CombatEncounter);
            InitializeCompletionAudio();
            initialized = true;
        }

        /// <summary>
        /// The two events on this bridge that belong to nobody: static ones. Every
        /// other subscription below targets an object the same session owns, so it
        /// dies with its publisher; these two outlive it, and until now they were
        /// only released in <c>OnDestroy</c>.
        /// <para>
        /// <c>Destroy</c> is deferred to the end of the frame, and
        /// <c>RestartCampaign</c> disposes one session and boots the next inside a
        /// single frame — so the previous campaign's bridge was still answering
        /// <c>ProgressionService.Awarded</c> and
        /// <c>DominanceSystem.PlanetFirstControlled</c> throughout the new
        /// campaign's boot, playing the dead session's audio for the live one's
        /// awards. Releasing them on deactivation closes that window, and
        /// <c>OnEnable</c> puts them back so the pair stays symmetric.
        /// </para>
        /// </summary>
        private bool staticSubscribed;

        private void SubscribeToStaticEvents()
        {
            if (staticSubscribed) return;
            staticSubscribed = true;
            ProgressionService.Awarded += OnAwarded;
            DominanceSystem.PlanetFirstControlled += OnPlanetFirstControlled;
        }

        private void ReleaseStaticEvents()
        {
            if (!staticSubscribed) return;
            staticSubscribed = false;
            ProgressionService.Awarded -= OnAwarded;
            DominanceSystem.PlanetFirstControlled -= OnPlanetFirstControlled;
        }

        private void OnEnable()
        {
            if (initialized) SubscribeToStaticEvents();
        }

        private void OnDisable() => ReleaseStaticEvents();

        private void OnDestroy()
        {
            ReleaseStaticEvents();
            if (!initialized || context == null) return;
            context.NavigationDirector.FrontierSignal.OriginOpened -= OnFrontierSignal;
            context.Streaming.FullSystemChangedByRealMovement -= OnFullSystemChanged;
            context.ExtractionDirector.Saved -= OnSaved;
            context.ExtractionDirector.Loaded -= OnLoaded;
            shipCombat.WeaponFired -= OnWeaponFired;
            shipCombat.FireDenied -= OnFireDenied;
            shipCombat.TargetChanged -= OnTargetChanged;
            shipCombat.DamageApplied -= OnDamageApplied;
            shipCombat.EmergencyShutdownStarted -= OnEmergencyShutdown;
            shipCombat.RepairStarted -= OnRepairStarted;
            shipCombat.RepairCompleted -= OnRepairCompleted;
            shipCombat.RepairCancelled -= OnRepairCancelled;
            DisposeCompletionAudio();
            ObserveEncounter(null);
            if (context.Modules.Upgrades != null)
            {
                context.Modules.Upgrades.JobStarted -= OnModuleStarted;
                context.Modules.Upgrades.JobCompleted -= OnModuleCompleted;
                context.Modules.Upgrades.JobCancelled -= OnModuleCancelled;
            }
            if (context.Hangar.Production != null)
            {
                context.Hangar.Production.JobStarted -= OnHangarStarted;
                context.Hangar.Production.JobCompleted -= OnHangarCompleted;
                context.Hangar.Production.JobUpgradeCompleted -= OnHangarUpgradeCompleted;
                context.Hangar.Production.JobCancelled -= OnHangarCancelled;
            }
        }

        private void Update()
        {
            if (!initialized || audio == null || ship == null) return;
            SyncFlight();
            SyncInteractionAudio();
            SyncMapAudio();
            SyncCombatAudio();
            SyncRaidAudio();
            SyncCompletionAudio();
            emitterPollTimer -= Time.unscaledDeltaTime;
            if (emitterPollTimer > 0f) return;
            emitterPollTimer = 0.20f;
            SyncCurrentStarAmbience();
            SyncMaterializedEmitters();
            SyncDominance();
            SyncEnemies();
            SyncPois();
            SyncProjectiles();
            SyncCompletionSlowAudio();
        }

        private void SyncFlight()
        {
            bool nowThrusting = ship.ThrustInput > 0.05f;
            if (nowThrusting != thrusting)
            {
                thrusting = nowThrusting;
                if (thrusting)
                    audio.StartLoop("ship/engine-thrust", "audio.ship.engine_thrust.loop", ship.transform);
                else
                    audio.StopLoop("ship/engine-thrust");
            }

            bool nowLocalBoost = thrusting && Input.GetKey(KeyCode.LeftShift)
                                 && ship.CruiseSpeedMultiplier <= 1.0001f;
            if (nowLocalBoost != localBoost)
            {
                localBoost = nowLocalBoost;
                if (localBoost)
                {
                    audio.PlayOneShot("audio.ship.boost.start", ship.transform);
                    audio.StartLoop("ship/boost", "audio.ship.boost.loop", ship.transform);
                }
                else
                {
                    audio.StopLoop("ship/boost");
                    audio.PlayOneShot("audio.ship.boost.end", ship.transform);
                }
            }

            bool nowCombatBoost = combatState != null && combatState.IsBoosting;
            if (nowCombatBoost != combatBoost)
            {
                combatBoost = nowCombatBoost;
                audio.PlayOneShot(combatBoost ? "audio.ship.boost.start" : "audio.ship.boost.end",
                    ship.transform);
            }

            FlightMode nowMode = cruise != null
                ? cruise.Mode : FlightMode.LocalFlight;
            if (nowMode != cruiseMode)
            {
                FlightMode before = cruiseMode;
                cruiseMode = nowMode;
                if (nowMode == FlightMode.CruiseRamp)
                    audio.PlayOneShot("audio.ship.cruise.charge", ship.transform);
                if (nowMode == FlightMode.DeepSpaceCruise)
                {
                    audio.PlayOneShot("audio.ship.cruise.engage", ship.transform);
                    audio.StartLoop("ship/cruise", "audio.ship.cruise.loop", ship.transform);
                }
                if (before == FlightMode.DeepSpaceCruise
                    && nowMode != FlightMode.DeepSpaceCruise)
                {
                    audio.StopLoop("ship/cruise");
                    audio.PlayOneShot("audio.ship.cruise.disengage", ship.transform);
                }
            }

            bool nowEnergyLow = combatState != null && combatState.EnergyCapacity > 0f
                                && combatState.Energy / combatState.EnergyCapacity <= 0.20f;
            if (nowEnergyLow && !energyLow)
                audio.PlayOneShot("audio.ship.energy_low");
            energyLow = nowEnergyLow;

            if (combatState != null && combatState.Damaged)
                audio.StartLoop("ship/damaged", "audio.ship.damaged.loop", ship.transform, 0.72f);
            else
                audio.StopLoop("ship/damaged");
        }

        private void SyncInteractionAudio()
        {
            bool nowScanning = extraction != null && extraction.ScanProgress > 0.0001f;
            if (nowScanning != scanning)
            {
                scanning = nowScanning;
                if (scanning)
                {
                    audio.PlayOneShot("audio.scan.ship.start", ship.transform);
                    audio.StartLoop("ship/scan", "audio.scan.ship.loop", ship.transform);
                }
                else
                {
                    audio.StopLoop("ship/scan");
                    audio.PlayOneShot(extraction != null && extraction.TargetProfile != null
                                      && extraction.TargetProfile.Scanned
                        ? "audio.scan.ship.complete" : "audio.scan.ship.failed", ship.transform);
                }
            }

            bool nowTransferring = extraction != null && extraction.Transferring;
            if (nowTransferring != transferring)
            {
                transferring = nowTransferring;
                if (transferring)
                {
                    audio.PlayOneShot("audio.drill.collect.start", ship.transform);
                    audio.StartLoop("ship/drill-transfer", "audio.drill.collect.loop", ship.transform);
                }
                else
                {
                    audio.StopLoop("ship/drill-transfer");
                    audio.PlayOneShot("audio.drill.collect.end", ship.transform);
                }
            }
        }

        private void SyncMapAudio()
        {
            UI.StrategicMap.StrategicMapScreen map =
                context.Presentation.StrategicMapScreen;
            if (map != null && map.IsOpen != mapOpen)
            {
                mapOpen = map.IsOpen;
                audio.PlayOneShot(mapOpen ? "audio.ui.map_open" : "audio.ui.map_close");
            }
            if (map != null && map.IsOpen)
            {
                string system = map.Presenter.SelectedSystemId;
                string planet = map.Presenter.SelectedPlanetId;
                if (system != selectedMapSystem)
                {
                    selectedMapSystem = system;
                    if (!string.IsNullOrEmpty(system))
                        audio.PlayOneShot("audio.ui.map_focus_system");
                }
                if (planet != selectedMapPlanet)
                {
                    selectedMapPlanet = planet;
                    if (!string.IsNullOrEmpty(planet))
                        audio.PlayOneShot("audio.ui.map_focus_planet");
                }
            }

            string waypoint = context.NavigationDirector.SelectedRouteSystemId;
            if (waypoint != selectedWaypoint)
            {
                audio.PlayOneShot(string.IsNullOrEmpty(waypoint)
                    ? "audio.ui.waypoint_clear" : "audio.ui.waypoint_set");
                selectedWaypoint = waypoint;
            }
        }

        private void SyncCombatAudio()
        {
            ObserveEncounter(context.Streaming.CombatEncounter);

            if (audio.IsLoopPlaying("weapon/laser")
                && Time.unscaledTime >= laserHoldUntil)
            {
                audio.StopLoop("weapon/laser");
                audio.PlayOneShot("audio.weapon.laser.end", ship.transform);
            }
            fireDeniedCooldown = Mathf.Max(0f, fireDeniedCooldown - Time.unscaledDeltaTime);

            float emProgress = shipCombat.EmLockProgress01;
            if (emProgress > 0.0001f && previousEmLockProgress <= 0.0001f)
            {
                audio.PlayOneShot("audio.weapon.em.charge", ship.transform);
                audio.StartLoop("weapon/em-lock", "audio.weapon.em.lock_loop", ship.transform);
            }
            if (emProgress <= 0.0001f && previousEmLockProgress > 0.0001f)
                audio.StopLoop("weapon/em-lock");
            if (emProgress >= 0.999f && previousEmLockProgress < 0.999f)
                audio.PlayOneShot("audio.weapon.em.lock_acquired");
            previousEmLockProgress = emProgress;

            if (shipCombat.Hardpoints != null)
            {
                for (int i = 0; i < shipCombat.Hardpoints.Length; i++)
                {
                    HardpointState hp = shipCombat.Hardpoints[i];
                    bool now = hp != null && hp.Overheated;
                    if (hardpointOverheated.TryGetValue(i, out bool before) && before != now)
                        audio.PlayOneShot(now
                            ? "audio.combat.hardpoint_overheat"
                            : "audio.combat.hardpoint_ready");
                    hardpointOverheated[i] = now;
                }
            }

            bool shutdownNow = shipCombat.Shutdown.Active;
            if (shutdownNow)
                audio.StartLoop("ship/emergency-countdown",
                    "audio.ship.emergency.countdown.loop", ship.transform);
            else if (audio.IsLoopPlaying("ship/emergency-countdown"))
            {
                audio.StopLoop("ship/emergency-countdown");
                audio.PlayOneShot("audio.ship.emergency.recovery", ship.transform);
            }

            if (shipCombat.Repair.Active)
                audio.StartLoop("ship/repair", "audio.build.repair.loop", ship.transform);
            else
                audio.StopLoop("ship/repair");
        }

        private void ObserveEncounter(CombatEncounterDirector encounter)
        {
            if (observedEncounter == encounter) return;
            if (observedEncounter != null)
                observedEncounter.ProjectileResolved -= OnProjectileResolved;

            foreach (string id in new List<string>(knownProjectiles.Keys))
                audio?.StopLoop("projectile/" + id);
            foreach (string id in new List<string>(knownEnemies))
            {
                audio?.StopLoop("enemy/" + id);
                audio?.StopLoop("enemy/" + id + "/siphon");
            }
            knownProjectiles.Clear();
            enemyStates.Clear();
            knownEnemies.Clear();

            observedEncounter = encounter;
            if (observedEncounter == null)
            {
                raidPhase = RaidPhase.None;
                return;
            }

            observedEncounter.ProjectileResolved += OnProjectileResolved;
            raidPhase = observedEncounter.Pressure.Phase;
            raidWarningHigh = raidPhase == RaidPhase.Warning
                              && observedEncounter.Pressure.WarningRemainingSeconds <= 30f;

            foreach (ProjectileObject projectile in observedEncounter.Projectiles)
            {
                if (projectile?.State == null) continue;
                knownProjectiles[projectile.State.ProjectileId] = projectile.State.Kind;
                StartProjectileLoop(projectile);
            }
            foreach (var pair in EnemyAgent.All)
            {
                EnemyAgent enemy = pair.Value;
                if (enemy == null || enemy.Destroyed) continue;
                knownEnemies.Add(enemy.Id);
                enemyStates[enemy.Id] = SnapshotEnemy(enemy);
            }
        }

        private void SyncRaidAudio()
        {
            CombatEncounterDirector encounter = context.Streaming.CombatEncounter;
            if (encounter != observedEncounter)
            {
                ObserveEncounter(encounter);
                return;
            }
            if (encounter == null) return;

            ThreatPressureState pressure = encounter.Pressure;
            RaidPhase now = pressure.Phase;
            if (now != raidPhase)
            {
                raidPhase = now;
                switch (now)
                {
                    case RaidPhase.Scheduled:
                        raidWarningHigh = false;
                        audio.PlayOneShot("audio.raid.scheduled");
                        break;
                    case RaidPhase.Warning:
                        audio.PlayOneShot("audio.raid.warning_low");
                        break;
                    case RaidPhase.Spawning:
                        audio.PlayOneShot("audio.raid.spawn");
                        break;
                    case RaidPhase.Active:
                        audio.PlayOneShot("audio.raid.active");
                        break;
                    case RaidPhase.RetreatCleanup:
                        audio.PlayOneShot("audio.raid.retreat");
                        break;
                    case RaidPhase.Resolved:
                        PlayRaidOutcome(pressure);
                        break;
                }
            }

            if (now == RaidPhase.Warning && !raidWarningHigh
                && pressure.WarningRemainingSeconds <= 30f)
            {
                raidWarningHigh = true;
                audio.PlayOneShot("audio.raid.warning_high");
            }
        }

        private void PlayRaidOutcome(ThreatPressureState pressure)
        {
            bool targetLost = !string.IsNullOrEmpty(pressure.TargetPlanetWorldId)
                              && ExtractionState.GetDominance(
                                  pressure.TargetPlanetWorldId).State == DominanceState.Lost;
            RaidOutcome outcome = RaidOutcomeResolver.Resolve(targetLost,
                pressure.PlayerWasShutdownDuringRaid,
                pressure.WasTargetContestedDuringRaid,
                pressure.AnyResourceStolen);
            audio.PlayOneShot(outcome switch
            {
                RaidOutcome.DecisiveVictory => "audio.raid.decisive",
                RaidOutcome.CostlyVictory => "audio.raid.costly",
                RaidOutcome.PlayerDefeatedDefenseWon => "audio.raid.costly",
                _ => "audio.raid.defeat"
            });
            if (pressure.SalvageApplied) audio.PlayOneShot("audio.raid.salvage");
        }

        private void SyncMaterializedEmitters()
        {
            var liveDrills = new HashSet<int>();
            foreach (DrillStructure drill in ExtractionState.Drills)
            {
                if (drill == null || drill.Runtime == null) continue;
                int id = drill.GetInstanceID();
                liveDrills.Add(id);
                string prefix = "drill/" + id;
                DrillLifecycle state = drill.Runtime.State;
                if (!drillStates.TryGetValue(id, out DrillLifecycle old))
                    old = state;
                if (old != state)
                {
                    if (state == DrillLifecycle.Active)
                        audio.PlayOneShot("audio.build.construction.complete", drill.transform);
                    else if (state == DrillLifecycle.CapsuleInTransit)
                        audio.PlayOneShot("audio.build.capsule.launch", drill.transform);
                    else if (old == DrillLifecycle.CapsuleInTransit
                             && state == DrillLifecycle.Constructing)
                        audio.PlayOneShot("audio.build.capsule.impact", drill.transform);
                    else if (state == DrillLifecycle.Disabled)
                        audio.PlayOneShot("audio.build.structure.disabled", drill.transform);
                    drillStates[id] = state;
                }
                else if (!drillStates.ContainsKey(id)) drillStates.Add(id, state);

                bool upgrading = drill.ActiveUpgradeJob != null;
                if (drillUpgradeStates.TryGetValue(id, out bool wasUpgrading))
                {
                    if (!wasUpgrading && upgrading)
                        audio.PlayOneShot("audio.build.upgrade.start", drill.transform);
                    if (wasUpgrading && !upgrading && drill.Runtime.Tier >= 2)
                        audio.PlayOneShot("audio.build.upgrade.complete", drill.transform);
                }
                drillUpgradeStates[id] = upgrading;

                if (state == DrillLifecycle.Active)
                {
                    string tierId = drill.Runtime.Tier >= 2
                        ? "audio.drill.t2.loop" : "audio.drill.t1.loop";
                    audio.StartLoop(prefix + "/machine", tierId, drill.transform);
                    audio.StartLoop(prefix + "/material", ResourceLoop(drill.Runtime.Family),
                        drill.transform, 0.62f);
                    audio.StopLoop(prefix + "/construction");
                    if (drill.Runtime.IsFull && fullDrills.Add(id))
                        audio.PlayOneShot("audio.drill.storage_full", drill.transform, 0.55f);
                    if (!drill.Runtime.IsFull) fullDrills.Remove(id);
                }
                else if (state == DrillLifecycle.CapsuleInTransit
                         || state == DrillLifecycle.Constructing)
                {
                    audio.StartLoop(prefix + "/construction",
                        state == DrillLifecycle.CapsuleInTransit
                            ? "audio.build.capsule.flight.loop"
                            : "audio.build.construction.loop", drill.transform);
                    audio.StopLoop(prefix + "/machine");
                    audio.StopLoop(prefix + "/material");
                }
                else
                {
                    audio.StopLoop(prefix + "/machine");
                    audio.StopLoop(prefix + "/material");
                    audio.StopLoop(prefix + "/construction");
                }
            }
            RemoveMissing(drillStates, liveDrills, "drill/");

            var liveStructures = new HashSet<int>();
            foreach (SurfaceStructure structure in ExtractionState.Structures)
            {
                if (structure == null || structure.Runtime == null) continue;
                int id = structure.GetInstanceID();
                liveStructures.Add(id);
                string prefix = "structure/" + id;
                DrillLifecycle state = structure.Runtime.State;
                if (!structureStates.TryGetValue(id, out DrillLifecycle old)) old = state;
                if (old != state)
                {
                    if (state == DrillLifecycle.Active)
                        audio.PlayOneShot("audio.build.structure.activate", structure.transform);
                    else if (state == DrillLifecycle.Disabled)
                        audio.PlayOneShot("audio.build.structure.disabled", structure.transform);
                    structureStates[id] = state;
                }
                else if (!structureStates.ContainsKey(id)) structureStates.Add(id, state);

                string ambient = StructureLoop(structure);
                if (state == DrillLifecycle.Active && ambient != null)
                    audio.StartLoop(prefix + "/ambient", ambient, structure.transform);
                else
                    audio.StopLoop(prefix + "/ambient");

                if (state == DrillLifecycle.CapsuleInTransit || state == DrillLifecycle.Constructing)
                    audio.StartLoop(prefix + "/construction", "audio.build.construction.loop",
                        structure.transform);
                else
                    audio.StopLoop(prefix + "/construction");

                if (structure.ActiveUpgradeJob != null)
                    audio.StartLoop(prefix + "/upgrade", "audio.build.upgrade.loop",
                        structure.transform);
                else
                    audio.StopLoop(prefix + "/upgrade");
            }
            RemoveMissing(structureStates, liveStructures, "structure/");

            var liveVehicles = new HashSet<string>();
            foreach (VehicleAgent vehicle in context.Hangar.Vehicles)
            {
                if (vehicle == null || vehicle.Def == null || !vehicle.gameObject.activeInHierarchy)
                    continue;
                liveVehicles.Add(vehicle.VehicleId);
                audio.StartLoop("vehicle/" + vehicle.VehicleId,
                    VehicleLoop(vehicle.Def.Class), vehicle.transform);
                knownVehicles.Add(vehicle.VehicleId);
            }
            foreach (string vehicleId in new List<string>(knownVehicles))
            {
                if (liveVehicles.Contains(vehicleId)) continue;
                audio.StopLoop("vehicle/" + vehicleId);
                knownVehicles.Remove(vehicleId);
            }

            foreach (PoiObject poi in PoiObject.All)
            {
                if (poi == null || poi.Manifest == null || poi.State == null) continue;
                string key = "poi/" + poi.Manifest.PoiId + "/local";
                if (poi.State.Level >= PoiKnowledgeLevel.Located
                    && poi.State.Level < PoiKnowledgeLevel.Resolved)
                    audio.StartLoop(key, PoiLoop(poi.Manifest.Type), poi.transform);
                else
                    audio.StopLoop(key);
            }
        }

        private void SyncDominance()
        {
            foreach (var pair in ExtractionState.AllDominance)
            {
                DominanceState state = pair.Value.State;
                if (!dominanceStates.TryGetValue(pair.Key, out DominanceState old))
                {
                    dominanceStates[pair.Key] = state;
                    continue;
                }
                if (old == state) continue;
                dominanceStates[pair.Key] = state;
                string eventId = state switch
                {
                    DominanceState.Developing => "audio.dominance.developing",
                    DominanceState.Controlled => "audio.dominance.controlled",
                    DominanceState.Contested => "audio.dominance.contested",
                    DominanceState.Lost => "audio.dominance.lost",
                    _ => null
                };
                if (eventId != null) audio.PlayOneShot(eventId);
            }
        }

        private void SyncEnemies()
        {
            var live = new HashSet<string>();
            foreach (var pair in EnemyAgent.All)
            {
                EnemyAgent enemy = pair.Value;
                if (enemy == null || enemy.Def == null || enemy.Destroyed) continue;
                live.Add(enemy.Id);
                string role = EnemyRoleName(enemy.Def.Id);
                audio.StartLoop("enemy/" + enemy.Id, $"audio.enemy.{role}.engine.loop",
                    enemy.transform);
                if (knownEnemies.Add(enemy.Id))
                {
                    audio.PlayOneShot("audio.enemy.acquired", enemy.transform);
                    enemyStates[enemy.Id] = SnapshotEnemy(enemy);
                    continue;
                }

                EnemyAudioSnapshot now = SnapshotEnemy(enemy);
                if (enemyStates.TryGetValue(enemy.Id, out EnemyAudioSnapshot before))
                {
                    if (!before.Telegraphing && now.Telegraphing)
                    {
                        string telegraph = enemy.Def.Role switch
                        {
                            EnemyRole.Probe => "audio.enemy.probe.scan_telegraph",
                            EnemyRole.Disruptor => "audio.enemy.disruptor.telegraph",
                            EnemyRole.Siege => "audio.enemy.siege.telegraph",
                            _ => null
                        };
                        if (telegraph != null) audio.PlayOneShot(telegraph, enemy.transform);
                    }
                    if (before.Telegraphing && !now.Telegraphing)
                    {
                        switch (enemy.Def.Role)
                        {
                            case EnemyRole.Disruptor:
                                audio.PlayOneShot("audio.enemy.disruptor.fire", enemy.transform);
                                audio.PlayOneShot("audio.enemy.disruptor.impact", enemy.transform);
                                break;
                            case EnemyRole.Heavy:
                                audio.PlayOneShot("audio.enemy.heavy.weapon_fire", enemy.transform);
                                audio.PlayOneShot("audio.enemy.heavy.weapon_impact", enemy.transform);
                                break;
                        }
                    }
                    if (before.Phase != EnemyAiPhase.AttackOrScan
                        && now.Phase == EnemyAiPhase.AttackOrScan
                        && enemy.Def.Role == EnemyRole.Fighter)
                    {
                        audio.PlayOneShot("audio.enemy.fighter.attack_pass", enemy.transform);
                        audio.PlayOneShot("audio.enemy.fighter.laser_fire", enemy.transform);
                    }
                    if (!before.HasLootTarget && now.HasLootTarget)
                        audio.PlayOneShot("audio.enemy.looter.approach", enemy.transform);
                    if (!before.ScanComplete && now.ScanComplete)
                        audio.PlayOneShot("audio.enemy.probe.transmission", enemy.transform);
                    if (before.Disrupted && !now.Disrupted)
                        audio.PlayOneShot("audio.enemy.disruptor.ready", enemy.transform);
                    if (now.Shield + now.Armor + now.Hull
                        < before.Shield + before.Armor + before.Hull - 0.0001f)
                        audio.PlayOneShot("audio.enemy.hit", enemy.transform);
                    if (before.Shield > 0.0001f && now.Shield <= 0.0001f)
                        audio.PlayOneShot("audio.enemy.shield_break", enemy.transform);
                    if (before.Phase != EnemyAiPhase.Retreat
                        && now.Phase == EnemyAiPhase.Retreat)
                        audio.PlayOneShot("audio.enemy.retreat", enemy.transform);

                    bool stealing = now.LootVolume > before.LootVolume + 0.0001f;
                    string siphonKey = "enemy/" + enemy.Id + "/siphon";
                    if (stealing)
                    {
                        if (!audio.IsLoopPlaying(siphonKey))
                        {
                            audio.PlayOneShot("audio.enemy.looter.siphon_start", enemy.transform);
                            audio.PlayOneShot("audio.enemy.looter.resource_stolen", enemy.transform);
                        }
                        audio.StartLoop(siphonKey, "audio.enemy.looter.siphon_loop",
                            enemy.transform);
                    }
                    else if (audio.IsLoopPlaying(siphonKey))
                    {
                        audio.StopLoop(siphonKey);
                        audio.PlayOneShot("audio.enemy.looter.siphon_end", enemy.transform);
                    }
                    if (!before.LootFull && now.LootFull)
                        audio.PlayOneShot("audio.enemy.looter.cargo_full", enemy.transform);
                }
                enemyStates[enemy.Id] = now;
            }
            foreach (string id in new List<string>(knownEnemies))
            {
                if (live.Contains(id)) continue;
                if (enemyStates.TryGetValue(id, out EnemyAudioSnapshot ended))
                {
                    if (ended.Phase == EnemyAiPhase.Retreat
                        || ended.Phase == EnemyAiPhase.Escaped)
                    {
                        audio.PlayOneShot(ended.Role == EnemyRole.Probe
                            ? "audio.enemy.probe.escape_burst"
                            : "audio.enemy.escape");
                    }
                    else
                    {
                        string destroyed = ended.Role switch
                        {
                            EnemyRole.Heavy => "audio.enemy.destroyed_heavy",
                            EnemyRole.Siege => "audio.enemy.destroyed_heavy",
                            EnemyRole.Disruptor => "audio.enemy.destroyed_medium",
                            EnemyRole.Looter => "audio.enemy.destroyed_medium",
                            _ => ((id.GetHashCode() & 1) == 0)
                                ? "audio.enemy.destroyed_small.01"
                                : "audio.enemy.destroyed_small.02"
                        };
                        audio.PlayOneShot(destroyed);
                    }
                }
                audio.StopLoop("enemy/" + id);
                audio.StopLoop("enemy/" + id + "/siphon");
                knownEnemies.Remove(id);
                enemyStates.Remove(id);
            }
        }

        private static EnemyAudioSnapshot SnapshotEnemy(EnemyAgent enemy) =>
            new EnemyAudioSnapshot
            {
                Telegraphing = enemy.IsTelegraphing,
                Phase = enemy.Ai != null ? enemy.Ai.Phase : EnemyAiPhase.Acquire,
                LootVolume = enemy.LooterCargo != null ? enemy.LooterCargo.UsedVolume : 0f,
                LootFull = enemy.LooterCargo != null && enemy.LooterCargo.IsFull,
                Shield = enemy.Combat != null ? enemy.Combat.Shield : 0f,
                Armor = enemy.Combat != null ? enemy.Combat.Armor : 0f,
                Hull = enemy.Combat != null ? enemy.Combat.Hull : 0f,
                Disrupted = enemy.IsDisrupted,
                ScanComplete = enemy.Ai != null && enemy.Ai.ScanComplete,
                HasLootTarget = enemy.CurrentLootTarget.HasValue,
                Role = enemy.Def != null ? enemy.Def.Role : EnemyRole.Fighter
            };

        private static int CountActiveSalvageNodes(PoiState state)
        {
            int count = 0;
            if (state == null) return count;
            foreach (SalvageNode node in state.SalvageNodes)
                if (node.Remaining > 0.0001f) count++;
            return count;
        }

        private void SyncPois()
        {
            var live = new HashSet<string>();
            foreach (PoiObject poi in PoiObject.All)
            {
                if (poi?.Manifest == null || poi.State == null) continue;
                string id = poi.Manifest.PoiId;
                live.Add(id);
                PoiAudioSnapshot now = new PoiAudioSnapshot
                {
                    Level = poi.State.Level,
                    Choice = poi.State.Choice,
                    Depleted = poi.State.Depleted,
                    ActiveSalvageNodes = CountActiveSalvageNodes(poi.State)
                };
                if (!poiStates.TryGetValue(id, out PoiAudioSnapshot before))
                {
                    poiStates[id] = now;
                    continue;
                }

                string operationKey = "poi/" + id + "/operation";
                if (before.Level != now.Level)
                {
                    if (now.Level == PoiKnowledgeLevel.Identified)
                    {
                        audio.PlayOneShot("audio.poi.identified", poi.transform);
                        if (poi.Manifest.Risk == PoiRisk.Unstable)
                            audio.PlayOneShot("audio.poi.unstable.warning", poi.transform);
                    }
                    if (now.Level == PoiKnowledgeLevel.Active)
                    {
                        switch (poi.Manifest.Type)
                        {
                            case PoiType.Anomaly:
                                audio.PlayOneShot("audio.poi.anomaly.analysis.start", poi.transform);
                                audio.StartLoop(operationKey,
                                    "audio.poi.anomaly.analysis.loop", poi.transform);
                                break;
                            case PoiType.Wreck:
                                audio.PlayOneShot("audio.poi.wreck.transfer.start", poi.transform);
                                audio.StartLoop(operationKey,
                                    "audio.poi.wreck.transfer.loop", poi.transform);
                                break;
                            case PoiType.AbandonedFacility:
                                audio.PlayOneShot("audio.poi.facility.terminal.open", poi.transform);
                                audio.StartLoop(operationKey,
                                    "audio.poi.facility.terminal.loop", poi.transform);
                                break;
                        }
                    }
                    if (before.Level == PoiKnowledgeLevel.Active
                        && now.Level != PoiKnowledgeLevel.Active)
                    {
                        audio.StopLoop(operationKey);
                        if (poi.Manifest.Type == PoiType.Wreck)
                            audio.PlayOneShot("audio.poi.wreck.transfer.end", poi.transform);
                    }
                    if (now.Level == PoiKnowledgeLevel.Resolved)
                    {
                        audio.StopLoop(operationKey);
                        audio.StopLoop("poi/" + id + "/local");
                        if (poi.Manifest.Type == PoiType.AbandonedFacility)
                            audio.PlayOneShot(now.Choice == FacilityChoice.SensorRelay
                                ? "audio.poi.facility.relay.activate"
                                : "audio.poi.facility.dismantle", poi.transform);
                        audio.PlayOneShot(poi.Manifest.Type switch
                        {
                            PoiType.Anomaly => "audio.poi.anomaly.resolve",
                            PoiType.AbandonedFacility => "audio.poi.facility.resolved",
                            _ => "audio.poi.resolved"
                        }, poi.transform);
                    }
                }

                if (before.Choice == FacilityChoice.None
                    && now.Choice != FacilityChoice.None)
                {
                    audio.PlayOneShot("audio.poi.facility.choice.confirm", poi.transform);
                }
                if (!before.Depleted && now.Depleted)
                    audio.PlayOneShot("audio.poi.wreck.depleted", poi.transform);
                if (now.ActiveSalvageNodes < before.ActiveSalvageNodes)
                    audio.PlayOneShot("audio.poi.wreck.salvage_node", poi.transform);
                poiStates[id] = now;
            }

            foreach (string id in new List<string>(poiStates.Keys))
            {
                if (live.Contains(id)) continue;
                audio.StopLoop("poi/" + id + "/local");
                audio.StopLoop("poi/" + id + "/operation");
                poiStates.Remove(id);
            }
        }

        private void SyncProjectiles()
        {
            if (observedEncounter == null) return;
            var live = new HashSet<string>();
            foreach (ProjectileObject projectile in observedEncounter.Projectiles)
            {
                if (projectile?.State == null) continue;
                ProjectileState state = projectile.State;
                live.Add(state.ProjectileId);
                if (knownProjectiles.ContainsKey(state.ProjectileId)) continue;
                knownProjectiles[state.ProjectileId] = state.Kind;
                if (state.Kind == ProjectileKind.EnemySiege)
                    audio.PlayOneShot("audio.enemy.siege.launch", projectile.transform);
                else if (state.Kind == ProjectileKind.Plasma && state.OwnerId != "ship")
                    audio.PlayOneShot("audio.defense.plasma.fire", projectile.transform);
                StartProjectileLoop(projectile);
            }

            foreach (string id in new List<string>(knownProjectiles.Keys))
            {
                if (live.Contains(id)) continue;
                audio.StopLoop("projectile/" + id);
                knownProjectiles.Remove(id);
            }
        }

        private void StartProjectileLoop(ProjectileObject projectile)
        {
            ProjectileState state = projectile.State;
            string eventId = state.Kind switch
            {
                ProjectileKind.Plasma => state.OwnerId == "ship"
                    ? "audio.weapon.plasma.projectile_loop"
                    : "audio.defense.plasma.projectile_loop",
                ProjectileKind.Em => "audio.weapon.em.projectile_loop",
                ProjectileKind.EnemySiege => "audio.enemy.siege.projectile_loop",
                ProjectileKind.EnemyPlasma => "audio.defense.plasma.projectile_loop",
                _ => null
            };
            if (eventId != null)
                audio.StartLoop("projectile/" + state.ProjectileId, eventId,
                    projectile.transform);
        }

        private void OnProjectileResolved(ProjectileState state, Vector3 position,
            bool intercepted)
        {
            audio.StopLoop("projectile/" + state.ProjectileId);
            knownProjectiles.Remove(state.ProjectileId);
            if (intercepted)
            {
                audio.PlayOneShot("audio.defense.projectile_intercept");
                return;
            }
            switch (state.Kind)
            {
                case ProjectileKind.Plasma:
                    audio.PlayOneShot(state.OwnerId == "ship"
                        ? "audio.weapon.plasma.impact"
                        : "audio.defense.plasma.impact");
                    if (state.OwnerId == "ship")
                        audio.PlayOneShot("audio.weapon.plasma.splash");
                    break;
                case ProjectileKind.Em:
                    audio.PlayOneShot("audio.weapon.em.impact");
                    audio.PlayOneShot("audio.weapon.em.disrupt");
                    break;
                case ProjectileKind.EnemySiege:
                    audio.PlayOneShot("audio.enemy.siege.impact");
                    break;
                case ProjectileKind.EnemyPlasma:
                    audio.PlayOneShot("audio.defense.plasma.impact");
                    break;
            }
        }

        private void OnSaved(string label)
        {
            if (!string.IsNullOrEmpty(label))
                audio.PlayOneShot("audio.ui.save_complete");
        }

        private void OnLoaded()
        {
            audio.StopLoop("ship/emergency-countdown");
            audio.StopLoop("ship/repair");
            audio.PlayOneShot("audio.ui.load_complete");
        }

        private void OnWeaponFired(WeaponFamily family)
        {
            switch (family)
            {
                case WeaponFamily.Laser:
                    if (!audio.IsLoopPlaying("weapon/laser"))
                    {
                        audio.PlayOneShot("audio.weapon.laser.start", ship.transform);
                        audio.StartLoop("weapon/laser", "audio.weapon.laser.loop",
                            ship.transform);
                    }
                    laserHoldUntil = Time.unscaledTime + 0.12f;
                    audio.PlayOneShot("audio.weapon.laser.hit");
                    break;
                case WeaponFamily.Plasma:
                    audio.PlayOneShot("audio.weapon.plasma.charge", ship.transform);
                    audio.PlayOneShot("audio.weapon.plasma.fire", ship.transform);
                    break;
                case WeaponFamily.Em:
                    audio.StopLoop("weapon/em-lock");
                    audio.PlayOneShot("audio.weapon.em.fire", ship.transform);
                    break;
            }
        }

        private void OnTargetChanged(EnemyAgent target)
        {
            if (target != null) audio.PlayOneShot("audio.combat.target_cycle");
            audio.PlayOneShot(target != null
                ? "audio.combat.target_acquire"
                : "audio.combat.target_lost");
        }

        private void OnFireDenied()
        {
            if (fireDeniedCooldown > 0f) return;
            fireDeniedCooldown = 0.45f;
            audio.PlayOneShot("audio.combat.fire_denied");
        }

        private void OnDamageApplied(ShipDamageLayer layer, bool heavy, bool broke)
        {
            string name = layer.ToString().ToLowerInvariant();
            if (broke)
            {
                audio.PlayOneShot("audio.damage." + name + ".break", ship.transform);
                return;
            }
            if (heavy)
            {
                audio.PlayOneShot("audio.damage." + name + ".heavy", ship.transform);
                return;
            }
            damageVariation = 1 + (damageVariation % 2);
            audio.PlayOneShot($"audio.damage.{name}.light.{damageVariation:00}",
                ship.transform);
        }

        private void OnEmergencyShutdown()
        {
            audio.PlayOneShot("audio.ship.emergency.shutdown", ship.transform);
            audio.StartLoop("ship/emergency-countdown",
                "audio.ship.emergency.countdown.loop", ship.transform);
        }

        private void OnRepairStarted()
        {
            audio.StartLoop("ship/repair", "audio.build.repair.loop", ship.transform);
        }

        private void OnRepairCompleted()
        {
            audio.StopLoop("ship/repair");
            audio.PlayOneShot("audio.build.repair.complete", ship.transform);
        }

        private void OnRepairCancelled() => audio.StopLoop("ship/repair");

        private void OnAwarded(AwardResult result)
        {
            audio.PlayOneShot(result.PointsGained > 0
                ? "audio.ui.level_up" : "audio.ui.notification_info");
        }

        private void OnPlanetFirstControlled(string worldId) =>
            audio.PlayOneShot("audio.dominance.controlled");

        private void OnFrontierSignal(string origin) =>
            audio.PlayOneShot("audio.signal.frontier_discovered");

        private void OnFullSystemChanged(string systemId)
        {
            if (!string.IsNullOrEmpty(previousFullSystem)
                && previousFullSystem != systemId)
                audio.PlayOneShot("audio.navigation.system_exit");
            audio.PlayOneShot("audio.navigation.system_enter");
            SetSystemAmbience(systemId);
            previousFullSystem = systemId;
        }

        private void OnModuleStarted(ModuleUpgradeJob job) =>
            audio.PlayOneShot("audio.build.upgrade.start", ship.transform);

        private void OnModuleCompleted(ModuleUpgradeJob job) =>
            audio.PlayOneShot("audio.ship.module_install", ship.transform);

        private void OnModuleCancelled(ModuleUpgradeJob job) =>
            audio.PlayOneShot("audio.build.construction.cancel_refund", ship.transform);

        private void OnHangarStarted(HangarProductionJob job)
        {
            audio.PlayOneShot("audio.hangar.production.start", ship.transform);
            audio.StartLoop("hangar/production", "audio.hangar.production.loop", ship.transform);
        }

        private void OnHangarCompleted(HangarProductionJob job, string vehicleId)
        {
            audio.StopLoop("hangar/production");
            audio.PlayOneShot("audio.hangar.production.complete", ship.transform);
        }

        private void OnHangarUpgradeCompleted(HangarProductionJob job) =>
            OnHangarCompleted(job, null);

        private void OnHangarCancelled(HangarProductionJob job)
        {
            audio.StopLoop("hangar/production");
            audio.PlayOneShot("audio.hangar.production.cancel", ship.transform);
        }

        /// <summary>
        /// Keeps the current star's ambience alive across every way its star can be
        /// replaced.
        /// <para>
        /// The emitter is a child of the Full system's star transform, so it dies
        /// with the star — and a star is destroyed and rebuilt by a load, by a
        /// restart and by any re-materialization, none of which raise
        /// <c>FullSystemChangedByRealMovement</c> (that one is deliberately reserved
        /// for a genuine physical arrival). The ambience was therefore started once
        /// at composition and lost the first time the system was rebuilt under it,
        /// leaving the player flying a lit star in silence for the rest of the
        /// session. A lifecycle census over a thousand cycles caught it as one
        /// object destroyed and never rebuilt.
        /// </para>
        /// <para>
        /// Reconciled on the emitter poll rather than hooked to each of those
        /// events, because the emitter's lifetime is the <b>star's</b>, not any one
        /// event's — a rule stated once here cannot be forgotten by the next path
        /// that replaces a system.
        /// </para>
        /// </summary>
        private void SyncCurrentStarAmbience()
        {
            if (context?.Streaming == null) return;
            if (audio.IsLoopPlaying("world/current-star")) return;
            SetSystemAmbience(context.Streaming.FullSystemId);
        }

        private void SetSystemAmbience(string systemId)
        {
            SystemManifest manifest = !string.IsNullOrEmpty(systemId)
                ? StarSystemRegistry.Manifests?.ResolveSystem(systemId) : null;
            if (manifest == null) return;
            string eventId = manifest.StarClass switch
            {
                StarClass.RedDwarf => "audio.world.star.red.loop",
                StarClass.Yellow => "audio.world.star.yellow.loop",
                StarClass.White => "audio.world.star.white.loop",
                StarClass.BlueGiant => "audio.world.star.blue.loop",
                _ => "audio.world.star.yellow.loop"
            };
            audio.StartLoop("world/current-star", eventId, context.Streaming.BootStarTransform, 0.72f);
        }

        private static string ResourceLoop(ResourceFamily family) => family switch
        {
            ResourceFamily.Metal => "audio.drill.material.metal.loop",
            ResourceFamily.Wood => "audio.drill.material.wood.loop",
            ResourceFamily.Gas => "audio.drill.material.gas.loop",
            ResourceFamily.Water => "audio.drill.material.water.loop",
            ResourceFamily.Diamond => "audio.drill.material.diamond.loop",
            _ => "audio.drill.material.metal.loop"
        };

        private static string StructureLoop(SurfaceStructure structure) => structure.Type switch
        {
            SurfaceStructureType.Command => "audio.structure.command_uplink.loop",
            SurfaceStructureType.SensorArray => null,
            SurfaceStructureType.MaintenancePlatform => "audio.structure.maintenance.loop",
            _ => null
        };

        private static string VehicleLoop(VehicleClass vehicleClass) => vehicleClass switch
        {
            VehicleClass.Scout => "audio.vehicle.scout.engine.loop",
            VehicleClass.Collector => "audio.vehicle.collector.engine.loop",
            VehicleClass.Engineer => "audio.vehicle.engineer.engine.loop",
            VehicleClass.Escort => "audio.vehicle.escort.engine.loop",
            _ => "audio.vehicle.scout.engine.loop"
        };

        private static string PoiLoop(Poi.PoiType type) => type switch
        {
            Poi.PoiType.Anomaly => "audio.poi.anomaly.local.loop",
            Poi.PoiType.Wreck => "audio.poi.wreck.local.loop",
            Poi.PoiType.AbandonedFacility => "audio.poi.facility.local.loop",
            _ => "audio.remote.communication.ping.loop"
        };

        private static string EnemyRoleName(string enemyDefId)
        {
            string id = enemyDefId?.ToLowerInvariant() ?? string.Empty;
            if (id.Contains("probe")) return "probe";
            if (id.Contains("looter")) return "looter";
            if (id.Contains("disrupt")) return "disruptor";
            if (id.Contains("heavy")) return "heavy";
            if (id.Contains("siege")) return "siege";
            return "fighter";
        }

        private void RemoveMissing(Dictionary<int, DrillLifecycle> states,
            HashSet<int> live, string prefix)
        {
            foreach (int id in new List<int>(states.Keys))
            {
                if (live.Contains(id)) continue;
                audio.StopLoop(prefix + id + "/machine");
                audio.StopLoop(prefix + id + "/material");
                audio.StopLoop(prefix + id + "/construction");
                audio.StopLoop(prefix + id + "/ambient");
                audio.StopLoop(prefix + id + "/upgrade");
                fullDrills.Remove(id);
                drillUpgradeStates.Remove(id);
                states.Remove(id);
            }
        }
    }
}
