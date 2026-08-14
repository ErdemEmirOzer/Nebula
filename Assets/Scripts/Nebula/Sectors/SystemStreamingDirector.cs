using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Remote;
using Nebula.Ship;
using Nebula.Simulation;
using Nebula.World;

namespace Nebula.Sectors
{
    public enum SystemStreamingState { None, Warm, Full }

    /// <summary>
    /// Owns which system is Full, which (at most one more) is Warm, and
    /// drives promotion/demotion as the ship flies — continuously, never a
    /// teleport/loading screen/time-skip/speed reset (VS-008 hard rule).
    /// Also runs the distant tick for every OTHER known system that still has
    /// something in its delta. A system is either being ticked Full (every
    /// frame) or distantly — never both in the same frame, since a system
    /// leaves the sparse-tick set the instant it promotes to Full and only
    /// rejoins it after being fully demoted.
    /// <para>
    /// The distant tick runs at <b>1 Hz for a system with anything in flight
    /// and 0.1 Hz for a calm one</b> (P2-TIME-002). Only the evaluation rate
    /// differs: a skipped cycle's seconds are carried and handed to the
    /// evaluation that runs, so simulated time advances identically at either
    /// rate. See <c>TickSparseSystems</c> for the schedule and why the seconds
    /// are carried rather than multiplied.
    /// </para>
    /// </summary>
    public class SystemStreamingDirector : MonoBehaviour,
        Nebula.Visuals.IFullSystemStarSource, Nebula.Visuals.IFullSystemEncounterSource
    {
        private const float WarmPrepareDistance = 300000f;
        private const float DemoteDistance = 400000f;
        private const float SparseTickInterval = 1f;

        private ShipController ship;
        private GravitySystem gravity;
        private List<Material> planetMaterialPool;
        private bool showOrbitRings, showPlanetTrails;
        private ShipHangar hangar;
        private bool orbitGuidesVisible;

        public NavigationDirector Navigation { get; set; }
        /// <summary>VS-015.1 §1: late-injected the same way Navigation is — set once by GameplayInstaller after both this director and the intervention director exist.</summary>
        public Hangar.InterventionGroupDirector InterventionGroupDirector { get; set; }
        /// <summary>Read-only access to the ship's Hangar (VS-010 §4 attention query / vehicle-recall checks) — never mutated from outside.</summary>
        public ShipHangar HangarRef => hangar;

        /// <summary>VS-013: the CURRENT Full system's combat encounter director, or null when none is materialized (Warm/sparse systems never have one). The single access point every other combat-adjacent class (ModuleMilestones, CombatCampaignDirector) uses instead of a new locator/Find.</summary>
        public Combat.CombatEncounterDirector CombatEncounter { get; private set; }

        private string fullSystemId;
        private string warmSystemId;
        private MaterializedSystem fullSystem;
        private MaterializedSystem warmSystem;

        private readonly HashSet<string> sparseSystemIds = new HashSet<string>();
        private float sparseTickTimer;
        private int sparseCycleCount;

        public string FullSystemId => fullSystemId;

        /// <summary>VS-017A.1 §1: fired only from TickPromotionDemotion's own real, physical arrival check -- never from BootFull/ResetAndBootFull (load, restart, or test-driven reboot), so a subscriber can tell a genuine "the ship flew here" transition apart from a save/load or a test's own scripted system switch.</summary>
        public event System.Action<string> FullSystemChangedByRealMovement;
        public string WarmSystemId => warmSystemId;
        public float LastPromotionMs { get; private set; }
        public float LastDemotionMs { get; private set; }

        public SystemStreamingState GetState(string systemId) =>
            systemId == fullSystemId ? SystemStreamingState.Full : systemId == warmSystemId ? SystemStreamingState.Warm : SystemStreamingState.None;

        public Transform TryGetStarTransform(string systemId) =>
            systemId == fullSystemId ? fullSystem?.StarTransform : systemId == warmSystemId ? warmSystem?.StarTransform : null;

        public void Initialize(ShipController shipController, GravitySystem gravitySystem, ShipHangar shipHangar,
            List<Material> pool, bool orbitRings, bool trails)
        {
            ship = shipController; gravity = gravitySystem; hangar = shipHangar;
            planetMaterialPool = pool; showOrbitRings = orbitRings; showPlanetTrails = trails;
            orbitGuidesVisible = orbitRings;
        }

        /// <summary>New-campaign / restored-save boot: the start (or restored) system is created Full directly, with no Warm neighbor yet.</summary>
        /// <summary>
        /// Materializes <paramref name="startSystem"/> Full and publishes this director as
        /// the session's live one.
        /// <para>
        /// The registration lives here, at the end, rather than at the call site, and that
        /// placement is the fix for a real defect. <c>StarSystemRegistry.Initialize</c>
        /// nulls <see cref="StarSystemRegistry.Streaming"/> as part of re-seeding the
        /// world, which is correct — between that call and a fresh boot the previous
        /// campaign's materialized systems are still hanging off this object, and handing
        /// those out would be worse than handing out nothing. But the only code that ever
        /// put the pointer back was <c>WorldInstaller</c>, which runs at composition time
        /// only. A manual load calls <c>Initialize</c> and then <see cref="ResetAndBootFull"/>,
        /// so from the first load onward the registry said "nothing is streamed" for the
        /// rest of the session.
        /// </para>
        /// <para>
        /// Nothing failed loudly, which is why it survived: <c>ExtractionDirector</c> holds
        /// its own reference and loads fine. Everything reading the <i>static</i> degraded
        /// quietly instead — no system ever reported Full, so status and attention queries,
        /// module milestones, the hangar's in-combat guards, the raid scheduler's tick
        /// target, surface structures and two tutorial chapters all silently took their
        /// "nothing streamed" branch.
        /// </para>
        /// <para>
        /// Published last, after the system is actually materialized, so the registry never
        /// hands out a half-booted director. An invariant that has to be remembered at each
        /// call site is one that gets forgotten at the second one.
        /// </para>
        /// </summary>
        public void BootFull(SystemManifest startSystem)
        {
            fullSystemId = startSystem.SystemId;
            fullSystem = SystemMaterializer.CreateWarm(startSystem, gravity, orbitGuidesVisible);
            SystemMaterializer.UpgradeToFull(fullSystem, startSystem, gravity, planetMaterialPool, showOrbitRings, showPlanetTrails, SystemMaterializer.AsteroidsPerSystem, orbitGuidesVisible);
            using (fullSystem.FullResources.Activate())
            {
                RestoreDeltaInto(startSystem, fullSystem);
                MaterializePois(startSystem, fullSystem);
                MaterializeCombat(startSystem, fullSystem);
            }

            StarSystemRegistry.Streaming = this;
        }

        public Transform BootStarTransform => fullSystem?.StarTransform;

        /// <summary>
        /// Load-time (manual load / boot-from-save) reset: discards whatever is
        /// currently materialized WITHOUT capturing it (the caller has
        /// already destroyed/reloaded every live drill/structure/vehicle
        /// itself, so there is nothing left here worth saving), then
        /// re-materializes Full at the given system and restores its delta.
        /// </summary>
        public void ResetAndBootFull(SystemManifest manifest)
        {
            Shutdown();
            if (manifest == null) return;
            BootFull(manifest);
            // VS-016A §2: resume distant ticking for every already-demoted system this save/load
            // carries -- must run AFTER BootFull sets fullSystemId, so the just-booted system itself
            // is correctly excluded from its own remote-tick set.
            RepopulateSparseSystemsFromDeltas();
        }

        /// <summary>
        /// TF-002: full session teardown — destroys whatever is currently
        /// materialized WITHOUT capturing or rebooting anything (the composition
        /// root's own dispose path, distinct from the per-frame demote/promote
        /// that always preserves a delta). Never used for the normal Full/Warm
        /// transition; only for tearing down the whole streaming director.
        /// </summary>
        public void Shutdown()
        {
            if (warmSystem != null) SystemMaterializer.Destroy(warmSystem);
            warmSystem = null; warmSystemId = null;
            if (fullSystem != null) SystemMaterializer.Destroy(fullSystem);
            fullSystem = null; fullSystemId = null;
            CombatEncounter = null; // its GameObject is already destroyed above as a child of fullSystem.Root
            sparseSystemIds.Clear();
        }

        /// <summary>
        /// Takes the materialized systems down with the director.
        /// <para>
        /// The warm and full systems are created as <b>scene roots</b>
        /// (<see cref="SystemMaterializer"/>), never parented to this component, so
        /// destroying the director left them behind: every Restart Campaign and every
        /// return to the menu stranded a whole materialized system, measured at
        /// roughly 3,180 objects per rebuild. <see cref="Shutdown"/> already knew how
        /// to free them and its only caller was <see cref="ResetAndBootFull"/>, so
        /// nothing called it on the path that actually ends a session.
        /// </para>
        /// <para>
        /// Save/load was clean throughout, which is why this hid: it reuses the same
        /// director rather than disposing one.
        /// </para>
        /// </summary>
        private void OnDestroy() => Shutdown();

        private void Update()
        {
            if (!InputGate.Suppressed && Input.GetKeyDown(KeyCode.O)) ToggleOrbitGuides();

            float dt = Time.deltaTime;
            if (dt <= 0f) return; // paused — no offline/paused progress anywhere in streaming either

            Vector3 shipAbs = ship.transform.position + FloatingOrigin.TotalOffset;
            TickPromotionDemotion(shipAbs);
            TickSparseSystems(dt);
        }

        /// <summary>Current global orbit-ring visibility state (VS-008.1: Input-independent, so this is directly testable).</summary>
        public bool OrbitGuidesVisible => orbitGuidesVisible;

        private void ToggleOrbitGuides() => SetOrbitGuidesVisible(!orbitGuidesVisible);

        /// <summary>Sets the global orbit-ring visibility state (VS-008.1 test-facing API): flips the currently materialized Full/Warm guides immediately, and every future Warm/Full/downgrade materialization reads this state at creation time.</summary>
        public void SetOrbitGuidesVisible(bool visible)
        {
            orbitGuidesVisible = visible;
            if (fullSystem?.OrbitGuides != null) fullSystem.OrbitGuides.gameObject.SetActive(orbitGuidesVisible);
            if (warmSystem?.OrbitGuides != null) warmSystem.OrbitGuides.gameObject.SetActive(orbitGuidesVisible);
        }

        // --- Promotion / demotion ---------------------------------------------------
        private void TickPromotionDemotion(Vector3 shipAbs)
        {
            // The registry is campaign-scoped: StarSystemRegistry.Clear() nulls Manifests at
            // session teardown, while this component goes on ticking until its own GameObject
            // is destroyed -- at minimum one more frame, and indefinitely whenever anything
            // holds the director past the session. Every other reader already treats a cleared
            // registry as "nothing materialized": SystemsNear yield-breaks on it, TryGet reads
            // through Manifests?., and NebulaPlanetName and ChapterCSteps check it outright.
            // This is the one path that runs every frame, and it dereferenced Manifests
            // directly below -- so a cleared registry became one NullReferenceException per
            // frame for the rest of the process. In a build that is a permanent exception
            // storm from the moment the player returns to the menu.
            if (StarSystemRegistry.Manifests == null) return;

            SystemManifest nearest = null;
            float nearestDist = float.MaxValue;
            SystemManifest secondNearest = null;
            float secondDist = float.MaxValue;

            foreach (StarSystemRecord record in StarSystemRegistry.SystemsNear(shipAbs))
            {
                float d = Vector3.Distance(shipAbs, record.AbsoluteCenter);
                SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(record.SystemId);
                if (manifest == null) continue;
                if (d < nearestDist) { secondNearest = nearest; secondDist = nearestDist; nearest = manifest; nearestDist = d; }
                else if (d < secondDist) { secondNearest = manifest; secondDist = d; }
            }

            // Physical entry into a system's navigation radius -> it becomes Full.
            if (nearest != null && nearestDist <= nearest.NavigationRadius && nearest.SystemId != fullSystemId)
            {
                float t0 = Time.realtimeSinceStartup;
                PromoteToFull(nearest);
                LastPromotionMs = (Time.realtimeSinceStartup - t0) * 1000f;
                FullSystemChangedByRealMovement?.Invoke(nearest.SystemId);
            }

            // Warm candidate: nearest system that ISN'T the current Full, within prep distance.
            SystemManifest warmCandidate = nearest != null && nearest.SystemId != fullSystemId ? nearest : secondNearest;
            float warmCandidateDist = ReferenceEquals(warmCandidate, nearest) ? nearestDist : secondDist;

            if (warmCandidate == null || warmCandidateDist > WarmPrepareDistance)
            {
                if (warmSystemId != null) DemoteWarmToNone();
            }
            else if (warmCandidate.SystemId != warmSystemId)
            {
                if (warmSystemId != null) DemoteWarmToNone();
                PromoteToWarm(warmCandidate);
            }

            // The old Full falls behind -> demote once clearly beyond both the demote distance and its own radius (hysteresis).
            if (fullSystemId != null)
            {
                SystemManifest fullManifest = StarSystemRegistry.Manifests.ResolveSystem(fullSystemId);
                if (fullManifest != null)
                {
                    float distToFull = Vector3.Distance(shipAbs, fullManifest.AbsoluteCenter);
                    if (distToFull > DemoteDistance && distToFull > fullManifest.NavigationRadius)
                    {
                        float t0 = Time.realtimeSinceStartup;
                        DemoteFullToNoneOrWarm(fullManifest);
                        LastDemotionMs = (Time.realtimeSinceStartup - t0) * 1000f;
                    }
                }
            }
        }

        private void PromoteToWarm(SystemManifest manifest)
        {
            warmSystemId = manifest.SystemId;
            warmSystem = SystemMaterializer.CreateWarm(manifest, gravity, orbitGuidesVisible);
        }

        private void DemoteWarmToNone()
        {
            if (warmSystem != null) SystemMaterializer.Destroy(warmSystem);
            warmSystem = null;
            warmSystemId = null;
        }

        private void PromoteToFull(SystemManifest manifest)
        {
            // Old Full steps down first (captured + torn down, or handed off to become the new Warm).
            if (fullSystemId != null && fullSystemId != manifest.SystemId)
            {
                SystemManifest oldFullManifest = StarSystemRegistry.Manifests.ResolveSystem(fullSystemId);
                if (oldFullManifest != null) DemoteFullToNoneOrWarm(oldFullManifest);
            }

            if (manifest.SystemId == warmSystemId && warmSystem != null)
            {
                fullSystem = warmSystem;
                fullSystemId = warmSystemId;
                warmSystem = null; warmSystemId = null;
                SystemMaterializer.UpgradeToFull(fullSystem, manifest, gravity, planetMaterialPool, showOrbitRings, showPlanetTrails, SystemMaterializer.AsteroidsPerSystem, orbitGuidesVisible);
            }
            else
            {
                fullSystemId = manifest.SystemId;
                fullSystem = SystemMaterializer.CreateWarm(manifest, gravity, orbitGuidesVisible);
                SystemMaterializer.UpgradeToFull(fullSystem, manifest, gravity, planetMaterialPool, showOrbitRings, showPlanetTrails, SystemMaterializer.AsteroidsPerSystem, orbitGuidesVisible);
            }

            using (fullSystem.FullResources.Activate())
            {
                RestoreDeltaInto(manifest, fullSystem);
                MaterializePois(manifest, fullSystem);
                MaterializeCombat(manifest, fullSystem);
            }
            sparseSystemIds.Remove(manifest.SystemId);
        }

        /// <summary>Demotion order (VS-008 §5): validate manifest -> capture delta -> only then tear down GameObjects/registry entries.</summary>
        private void DemoteFullToNoneOrWarm(SystemManifest manifest)
        {
            if (manifest == null || fullSystem == null) return;

            SystemDelta delta = CaptureDelta(manifest, fullSystem, destroyVehicles: true);
            if (delta != null && !delta.IsEmpty) { StarSystemRegistry.Deltas.Set(delta); sparseSystemIds.Add(manifest.SystemId); }
            else StarSystemRegistry.Deltas.PruneIfEmpty(manifest.SystemId);

            SystemMaterializer.Destroy(fullSystem);
            fullSystem = null; fullSystemId = null;
            CombatEncounter = null; // its GameObject is already destroyed above as a child of fullSystem.Root
        }

        /// <summary>Read-only snapshot of the CURRENT Full system's live state for a save (VS-008 §8) — never destroys anything, unlike demotion's capture.</summary>
        public SystemDelta CaptureCurrentFullDelta()
        {
            if (fullSystemId == null || fullSystem == null) return null;
            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(fullSystemId);
            return manifest == null ? null : CaptureDelta(manifest, fullSystem, destroyVehicles: false);
        }

        // --- Distant simulation: 1 Hz busy, 0.1 Hz calm (VS-008 §6, VS-016A §2) -----
        //
        // P2-TIME-002. The honest statement of the schedule, because "1 Hz remote tick"
        // was written in a dozen comments and was true for only some systems:
        //
        //   * A system with anything in flight is evaluated once per second.
        //   * A system that has been calm for CalmSkipThreshold consecutive evaluations
        //     is evaluated once per CalmSkipEvery cycles -- 0.1 Hz -- because evaluating
        //     an idle system is a guaranteed no-op and the cost of doing it scales with
        //     how much of the galaxy the player has discovered.
        //
        // The *rate of evaluation* is what changes. Simulated time does not: a skipped
        // cycle's seconds are held and handed to the evaluation that does run, so a calm
        // system's drills, raids and vehicle tasks advance by exactly the elapsed wall
        // time either way. That is what makes the two rates a scheduler decision rather
        // than a difficulty setting, and it is why remote and Full results agree.

        /// <summary>The one place the two rates and the carried seconds are decided. This class asks it; it never re-derives the rule.</summary>
        private readonly SparseTickScheduler sparseSchedule = new SparseTickScheduler();

        /// <summary>VS-017A.2 §B4: that drill's storage capacity at its own real tier, through the single ExtractionCatalog authority. Falls back to its current stored amount when the profile is unknown, exactly as before.</summary>
        private static float DrillCapacityFor(string worldId, DrillDto dto)
        {
            PlanetProfile profile = ExtractionState.GetProfile(worldId);
            return profile != null ? ExtractionCatalog.StorageCapacity(profile.Rarity, dto.tier) : dto.stored;
        }

        private void TickSparseSystems(float dt)
        {
            sparseTickTimer += dt;
            if (sparseTickTimer < SparseTickInterval) return;
            float elapsed = sparseTickTimer;
            sparseTickTimer = 0f;
            sparseCycleCount++;

            if (sparseSystemIds.Count == 0) return;

            // Canonical system-ID order (VS-016A §2: "Sistemler canonical system ID sırasıyla işlenir").
            var ordered = new List<string>(sparseSystemIds);
            ordered.Sort(string.CompareOrdinal);

            var toRemove = new List<string>();
            foreach (string systemId in ordered)
            {
                SystemDelta delta = StarSystemRegistry.Deltas.TryGet(systemId);
                if (delta == null || delta.IsEmpty) { toRemove.Add(systemId); sparseSchedule.Forget(systemId); continue; }

                // This cycle's seconds belong to this system whether or not it is
                // evaluated now; skipping defers them, it never discards them.
                if (!sparseSchedule.ShouldRun(systemId, sparseCycleCount, elapsed, out float secondsToApply)) continue;

                TickDeltaDataOnly(delta, secondsToApply);
                if (delta.IsEmpty) { toRemove.Add(systemId); sparseSchedule.Forget(systemId); continue; }

                sparseSchedule.NoteEvaluated(systemId, IsSystemCalm(delta));
            }
            foreach (string id in toRemove) sparseSystemIds.Remove(id);
        }

        /// <summary>A system has nothing meaningfully in flight -- no producing/building drill, no in-progress structure build/upgrade, no active Collector route leg, no active vehicle task, and no raid/Probe encounter. Purely a scheduler-cost heuristic; ticking a calm system is always still SAFE (a no-op), just wasted, which is exactly what this skips.</summary>
        private static bool IsSystemCalm(SystemDelta delta)
        {
            foreach (DrillDto d in delta.drills)
            {
                var state = (DrillLifecycle)d.state;
                if (state == DrillLifecycle.CapsuleInTransit || state == DrillLifecycle.Constructing) return false;
                if (state == DrillLifecycle.Active) return false; // still has capacity to fill or is at least eligible to keep producing
            }
            foreach (StructureDto s in delta.structures)
            {
                var state = (DrillLifecycle)s.state;
                if (state == DrillLifecycle.CapsuleInTransit || state == DrillLifecycle.Constructing || s.upgrading) return false;
            }
            foreach (CollectorRouteDto r in delta.collectorRoutes) if ((RoutePhase)r.phase != RoutePhase.Paused) return false;
            foreach (VehicleDto v in delta.vehicles)
            {
                if (v.engineerTask != null && v.engineerTask.active) return false;
                if (v.escortState != null && v.escortState.active) return false;
                if (v.resupplyTask != null && v.resupplyTask.active) return false;
            }
            if (delta.threatPressure != null && (RaidPhase)delta.threatPressure.phase != RaidPhase.None) return false;
            if (delta.remoteProbe != null && (Remote.RemoteProbePhase)delta.remoteProbe.phase != Remote.RemoteProbePhase.None) return false;
            return true;
        }

        /// <summary>VS-016A §2: after a fresh boot/load, every system that still carries mutable remote state resumes distant ticking -- before this fix, only a system that demoted again in the CURRENT session was tracked, so a save/load silently stopped remote progress for every already-demoted system until it happened to be revisited. Both scheduler tables start empty, so every system is re-evaluated at 1 Hz until it has proved itself calm again.</summary>
        private void RepopulateSparseSystemsFromDeltas()
        {
            sparseSystemIds.Clear();
            sparseSchedule.Clear();
            foreach (SystemDelta d in StarSystemRegistry.Deltas.AllNonEmpty)
                if (d.systemId != fullSystemId) sparseSystemIds.Add(d.systemId);
        }

        /// <summary>Advances drill production/storage and dominance for a demoted system's delta, purely as data (no GameObjects touched at all).</summary>
        private void TickDeltaDataOnly(SystemDelta delta, float dt)
        {
            delta.simElapsedSeconds += dt; // VS-011: the "now" reference for a remotely-ticked route's dock-wait tie-break.

            for (int i = 0; i < delta.drills.Count; i++)
            {
                DrillDto d = delta.drills[i];
                var state = (DrillLifecycle)d.state;
                if (state == DrillLifecycle.CapsuleInTransit) { d.capsuleRemaining -= dt; if (d.capsuleRemaining <= 0f) d.state = (int)DrillLifecycle.Constructing; }
                else if (state == DrillLifecycle.Constructing) { d.buildRemaining -= dt; if (d.buildRemaining <= 0f) { d.buildRemaining = 0f; d.state = (int)DrillLifecycle.Active; } }
                else if (state == DrillLifecycle.Active)
                {
                    // VS-017A.2 §B3: an in-flight T1->T2 upgrade keeps running while the system
                    // is remote (the ship may freely leave mid-upgrade), and production stays
                    // fully halted for its whole duration -- the exact same rule DrillRuntime.Tick
                    // applies on the Full path.
                    if (d.upgradeActive)
                    {
                        d.upgradeRemainingSeconds -= dt;
                        if (d.upgradeRemainingSeconds <= 0f)
                        {
                            d.upgradeRemainingSeconds = 0f;
                            d.upgradeActive = false;
                            d.tier = DrillUpgradeCatalog.MaxTier;
                            // Consume the ship-cargo reservation exactly once, through the same
                            // service the Full path uses -- otherwise a remotely-completed upgrade
                            // would leave its cost reserved-but-never-consumed forever.
                            LogisticsCargo remoteUpgradeCargo = ship != null ? ship.GetComponent<Extraction.ShipLogistics>()?.Cargo : null;
                            remoteUpgradeCargo?.ConsumeReservation(DrillUpgradeService.OwnerId(d.worldId, d.nodeId));
                            d.upgradeRemainingCost.Clear();
                        }
                    }
                    else if (!IsDominanceSuspended(delta, d.worldId))
                    {
                        PlanetProfile profile = ExtractionState.GetProfile(d.worldId);
                        // Same single (Rarity, Tier) authority the Full path reads -- never a
                        // second formula, so Full and the distant tick can never diverge or round differently.
                        float perMinute = profile != null ? ExtractionCatalog.ProductionPerMinute(profile.Rarity, d.tier) : 0f;
                        float capacity = profile != null ? ExtractionCatalog.StorageCapacity(profile.Rarity, d.tier) : 0f;
                        d.accumulator += perMinute / 60f * dt;
                        float whole = Mathf.Floor(d.accumulator);
                        if (whole > 0f)
                        {
                            float space = Mathf.Max(0f, capacity - d.stored);
                            float deposited = Mathf.Min(whole, space);
                            d.stored += deposited;
                            d.accumulator -= deposited;
                        }
                    }
                }
                delta.drills[i] = d;
            }

            LogisticsCargo remoteBuildCargo = ship != null ? ship.GetComponent<Extraction.ShipLogistics>()?.Cargo : null;
            for (int i = 0; i < delta.structures.Count; i++)
            {
                StructureDto s = delta.structures[i];
                var state = (DrillLifecycle)s.state;
                var buildCost = new List<(string itemId, float amount)>();
                foreach (ItemAmountDto item in s.buildCost) buildCost.Add((item.itemId, item.amount));
                string ownerId = $"structure-build:{s.worldId}/{s.nodeId}";
                if (buildCost.Count > 0
                    && (remoteBuildCargo == null || !remoteBuildCargo.TrySetReservation(ownerId, buildCost)))
                {
                    // A saved economic job without its funding remains frozen;
                    // remote simulation must not turn it into a free structure.
                    delta.structures[i] = s;
                    continue;
                }
                if (state == DrillLifecycle.Active && buildCost.Count > 0)
                {
                    remoteBuildCargo.ConsumeReservation(ownerId);
                    s.buildCost.Clear();
                }
                if (state == DrillLifecycle.CapsuleInTransit) { s.capsuleRemaining -= dt; if (s.capsuleRemaining <= 0f) s.state = (int)DrillLifecycle.Constructing; }
                else if (state == DrillLifecycle.Constructing)
                {
                    s.buildRemaining -= dt;
                    if (s.buildRemaining <= 0f)
                    {
                        s.buildRemaining = 0f;
                        s.state = (int)DrillLifecycle.Active;
                        if (buildCost.Count > 0)
                        {
                            remoteBuildCargo.ConsumeReservation(ownerId);
                            s.buildCost.Clear();
                        }
                    }
                }
                delta.structures[i] = s;
            }

            // Vehicle data-only movement (Collector returning cargo to its own drill; Scout simply waits).
            for (int i = 0; i < delta.vehicles.Count; i++)
            {
                VehicleDto v = delta.vehicles[i];
                if ((VehiclePhase)v.phase == VehiclePhase.Waiting && (MissionKind)v.missionKind == MissionKind.Collector && v.heldAmount > 0.0001f)
                {
                    DrillDto ownDrill = FindDrillByWorldId(delta, v.targetId);
                    if (ownDrill.worldId != null)
                    {
                        int idx = delta.drills.IndexOf(ownDrill);
                        PlanetProfile profile = ExtractionState.GetProfile(ownDrill.worldId);
                        float capacity = profile != null ? ExtractionCatalog.StorageCapacity(profile.Rarity, ownDrill.tier) : ownDrill.stored;
                        float space = Mathf.Max(0f, capacity - ownDrill.stored);
                        float want = Mathf.Min(ResourceTransferService.VolumePerSecond * dt, space, v.heldAmount);
                        if (want > 0f)
                        {
                            ownDrill.stored += want;
                            v.heldAmount -= want;
                            if (v.heldAmount <= 0.0001f) { v.heldAmount = 0f; v.heldItemId = null; }
                            delta.drills[idx] = ownDrill;
                        }
                    }
                }
                delta.vehicles[i] = v;
            }

            TickLogisticsDataOnly(delta, dt);
            TickRemoteSlice(delta, dt);
        }

        /// <summary>
        /// VS-016A: the pieces of remote simulation VS-008/VS-011 above did NOT
        /// already cover -- raid/combat (Remote.RemoteRaidTick/RemoteProbeTick,
        /// mirroring Combat.RaidScheduler's own phase machine against this same
        /// SystemDelta instead of live ExtractionState) and the remaining
        /// economy/support pieces (Remote.RemoteEconomyTick: dominance
        /// stabilization, structure tier upgrades, Engineer/Escort/Maintenance
        /// Resupply/Maintenance Reserve policies/InterventionGroup). Reconstruct-
        /// tick-writeback, same convention as every method above it in this file --
        /// never a persistent cross-tick runtime cache of its own.
        /// </summary>
        private void TickRemoteSlice(SystemDelta delta, float dt)
        {
            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(delta.systemId);
            if (manifest == null) return;

            Remote.RemoteRaidContext raidCtx = Remote.RemoteRaidContext.LoadFromDelta(delta, manifest);
            Remote.RemoteRaidTick.Tick(raidCtx, dt);
            Remote.RemotePendingAttackTick.Tick(raidCtx, dt); // VS-016A.1 §1/§2D: advance/resolve/intercept every in-flight carried-over attack, structure/Escort/enemy-side alike, in the same reconstruct-tick-writeback pass.
            bool remoteRaidActive = raidCtx.Pressure.Phase != RaidPhase.None;
            raidCtx.SaveToDelta();

            if (delta.remoteProbe == null) delta.remoteProbe = new RemoteProbeDto();
            Remote.RemoteProbeState probe = Remote.RemoteProbeState.Restore(
                (Remote.RemoteProbePhase)delta.remoteProbe.phase, delta.remoteProbe.enemyId, delta.remoteProbe.scanCompleted);
            Remote.RemoteProbeTick.Tick(raidCtx, probe, dt);
            delta.remoteProbe.phase = (int)probe.Phase; delta.remoteProbe.enemyId = probe.EnemyId; delta.remoteProbe.scanCompleted = probe.ScanCompleted;
            raidCtx.SaveToDelta(); // Probe tick may also have spawned/moved a RemoteEnemyRuntime -- persist that too.

            Remote.RemoteEconomyTick.Tick(delta, manifest, dt, remoteRaidActive);
        }

        // --- VS-011: Logistics Center + Collector route remote simulation -------------
        /// <summary>Same real distance/speed/cargo-capacity/transfer-rate/dock-and-holding rules as the live path (CollectorRouteDirector + VehicleAgent.TickRouteMovement), duplicated here as DTO-only math — the same convention the drill/structure/vehicle blocks above already use, never a shared GameObject-touching call.</summary>
        private void TickLogisticsDataOnly(SystemDelta delta, float dt)
        {
            LogisticsCenterDto center = delta.logisticsCenters.Count > 0 ? delta.logisticsCenters[0] : null;
            if (center != null) TickCenterLifecycleRemote(center, dt);

            SystemManifest manifest = StarSystemRegistry.Manifests?.ResolveSystem(delta.systemId);
            for (int i = 0; i < delta.collectorRoutes.Count; i++)
            {
                CollectorRouteDto r = delta.collectorRoutes[i];
                var phase = (RoutePhase)r.phase;
                if (phase == RoutePhase.Paused || phase == RoutePhase.Queued || phase == RoutePhase.Unloading || phase == RoutePhase.Holding) continue;

                VehicleDto vdto = FindVehicleDtoByIdMutable(delta, r.vehicleId, out int vIdx);
                if (vdto == null) continue;
                Hangar.VehicleDef def = Hangar.VehicleCatalog.Get((Hangar.VehicleClass)vdto.vehicleClass, vdto.mk);
                if (def == null) continue;

                switch (phase)
                {
                    case RoutePhase.ToSource: TickRouteToSourceRemote(delta, r, def, dt); break;
                    case RoutePhase.Loading: TickRouteLoadingRemote(delta, r, vdto, def, manifest, dt); break;
                    case RoutePhase.ToCenter: TickRouteToCenterRemote(r, def, dt, delta.simElapsedSeconds); break;
                    case RoutePhase.Returning: break; // ship isn't in this (demoted) system by definition — mirrors the live "wait in place" behavior.
                }

                delta.collectorRoutes[i] = r;
                delta.vehicles[vIdx] = vdto;
            }

            if (center != null) TickCenterDocksRemote(delta, center, dt);
        }

        /// <summary>VS-014 §12: mirrors the live VehicleAgent route gate — a Contested/Lost source planet stops remote-tick pickup exactly like an evacuated one.</summary>
        private static bool IsSourceContestedOrLostRemote(SystemDelta delta, string worldId)
        {
            foreach (DominanceDto d in delta.dominance)
                if (d.worldId == worldId) return (DominanceState)d.state == DominanceState.Contested || (DominanceState)d.state == DominanceState.Lost;
            return false;
        }

        private static SurfaceNode FindNodeById(PlanetProfile profile, string nodeId)
        {
            foreach (SurfaceNode n in profile.Nodes) if (n.Id == nodeId) return n;
            return profile.FirstDefenseNode; // pre-VS-014 save with a nodeId format this profile no longer has -- safe fallback, never a hard failure
        }

        private static void TickRouteToSourceRemote(SystemDelta delta, CollectorRouteDto route, Hangar.VehicleDef def, float dt)
        {
            if (!TryFindDrillIndex(delta, route.drillWorldId, out int idx))
            { route.waitReason = (int)RouteWaitReason.SourceDrillMissing; return; }
            DrillDto drill = delta.drills[idx];
            if (delta.abandonedPlanets.Contains(route.drillWorldId)) { route.waitReason = (int)RouteWaitReason.SourceDrillEvacuated; return; }
            if (IsSourceContestedOrLostRemote(delta, route.drillWorldId)) { route.waitReason = (int)RouteWaitReason.SourceDrillContested; return; }
            if ((DrillLifecycle)drill.state != DrillLifecycle.Active) { route.waitReason = (int)RouteWaitReason.SourceDrillInactive; return; }

            route.waitReason = (int)RouteWaitReason.None;
            route.remainingDistance = Mathf.Max(0f, route.remainingDistance - def.Speed * dt);
            if (route.remainingDistance <= 0f) route.phase = (int)RoutePhase.Loading;
        }

        private static void TickCenterLifecycleRemote(LogisticsCenterDto center, float dt)
        {
            var state = (LogisticsCenterState)center.state;
            if (state == LogisticsCenterState.Constructing)
            {
                center.buildRemaining -= dt;
                if (center.buildRemaining <= 0f) { center.buildRemaining = 0f; center.state = (int)LogisticsCenterState.Active; }
                return;
            }
            if (state != LogisticsCenterState.Active) return;

            // Shield regen only (no damage/combat source exists yet this slice — matches Durability's own constants for parity with the live path).
            LogisticsCenterTierDef def = LogisticsCenterCatalog.Get((LogisticsCenterTier)center.tier);
            if (center.regenTimer > 0f) center.regenTimer = Mathf.Max(0f, center.regenTimer - dt);
            else center.shield = Mathf.Min(def.ShieldMax, center.shield + def.ShieldMax * 0.05f * dt);

            if (!center.upgrading) return;
            center.upgradeRemaining -= dt;
            if (center.upgradeRemaining > 0f) return;

            LogisticsCenterTierDef t2 = LogisticsCenterCatalog.T2;
            float shieldFrac = def.ShieldMax > 0f ? center.shield / def.ShieldMax : 0f;
            float armorFrac = def.ArmorMax > 0f ? center.armor / def.ArmorMax : 0f;
            float hullFrac = def.HullMax > 0f ? center.hull / def.HullMax : 0f;
            center.tier = (int)LogisticsCenterTier.T2;
            center.shield = t2.ShieldMax * shieldFrac; center.armor = t2.ArmorMax * armorFrac; center.hull = t2.HullMax * hullFrac;
            center.upgrading = false; center.upgradeRemaining = 0f;
        }

        private static void TickRouteLoadingRemote(SystemDelta delta, CollectorRouteDto route, VehicleDto vdto, Hangar.VehicleDef def, SystemManifest manifest, float dt)
        {
            if (!TryFindDrillIndex(delta, route.drillWorldId, out int idx)) { route.phase = (int)RoutePhase.ToSource; return; }
            DrillDto drill = delta.drills[idx];
            if (delta.abandonedPlanets.Contains(route.drillWorldId)) { route.waitReason = (int)RouteWaitReason.SourceDrillEvacuated; return; }
            if (IsSourceContestedOrLostRemote(delta, route.drillWorldId)) { route.waitReason = (int)RouteWaitReason.SourceDrillContested; return; }
            if ((DrillLifecycle)drill.state != DrillLifecycle.Active) { route.waitReason = (int)RouteWaitReason.SourceDrillInactive; return; }
            route.waitReason = (int)RouteWaitReason.None;

            PlanetProfile profile = ExtractionState.GetProfile(route.drillWorldId);
            string itemId = ExtractionCatalog.ResourceIds[(int)profile.Family];
            float perUnitVol = Mathf.Max(0.0001f, CargoItemVolumes.PerUnit(itemId));
            float usedVol = vdto.heldAmount * perUnitVol;
            float freeVol = Mathf.Max(0f, def.CargoCapacity - usedVol);
            float maxUnitsByVol = freeVol / perUnitVol;
            float want = Mathf.Min(def.TransferVolumePerSecond * dt, Mathf.Min(maxUnitsByVol, drill.stored));
            if (want > 0f)
            {
                drill.stored -= want;
                vdto.heldItemId = itemId; vdto.heldAmount += want;
                delta.drills[idx] = drill;
            }

            if (vdto.heldAmount >= def.CargoCapacity - 0.0001f)
            {
                route.phase = (int)RoutePhase.ToCenter;
                route.remainingDistance = ComputeDrillToCenterDistance(manifest, delta, route.drillWorldId);
            }
        }

        private static void TickRouteToCenterRemote(CollectorRouteDto route, Hangar.VehicleDef def, float dt, float nowSeconds)
        {
            route.remainingDistance = Mathf.Max(0f, route.remainingDistance - def.Speed * dt);
            if (route.remainingDistance <= 0f) { route.phase = (int)RoutePhase.Queued; route.dockWaitStartedAt = nowSeconds; }
        }

        /// <summary>Mirrors CollectorRouteDirector.TickCenterDocks exactly (dock tie-break, aggregate transfer budget, holding/backpressure) but reads/writes SystemDelta DTOs instead of live GameObjects.</summary>
        private static void TickCenterDocksRemote(SystemDelta delta, LogisticsCenterDto center, float dt)
        {
            LogisticsCenterTierDef def = LogisticsCenterCatalog.Get((LogisticsCenterTier)center.tier);

            foreach (CollectorRouteDto r in delta.collectorRoutes)
            {
                if ((RoutePhase)r.phase != RoutePhase.Holding) continue;
                VehicleDto vdto = FindVehicleDtoByIdMutable(delta, r.vehicleId, out _);
                if (vdto == null || vdto.heldAmount > 0.0001f) continue;
                if (def.Capacity - DtoInventoryTotal(center) > 0.0001f) r.phase = (int)RoutePhase.ToSource;
            }

            var candidates = new List<CollectorRouteService.DockCandidate>();
            foreach (CollectorRouteDto r in delta.collectorRoutes)
            {
                var phase = (RoutePhase)r.phase;
                VehicleDto vdto = FindVehicleDtoByIdMutable(delta, r.vehicleId, out _);
                bool isHolding = phase == RoutePhase.Holding && vdto != null && vdto.heldAmount > 0.0001f;
                if (phase != RoutePhase.Queued && !isHolding) continue;
                bool sourceDrillFull = false;
                foreach (DrillDto d in delta.drills)
                    if (d.worldId == r.drillWorldId) { sourceDrillFull = (DrillLifecycle)d.state == DrillLifecycle.Active && d.stored >= DrillCapacityFor(r.drillWorldId, d) - 0.0001f; break; }
                candidates.Add(new CollectorRouteService.DockCandidate(r.routeId, r.vehicleId, isHolding, sourceDrillFull, (RoutePriority)r.priority, r.dockWaitStartedAt));
            }
            foreach (CollectorRouteService.DockCandidate candidate in CollectorRouteService.OrderForDockAssignment(candidates))
            {
                if (CountRoutePhase(delta, RoutePhase.Unloading) >= def.DockCount) break;
                CollectorRouteDto route = FindRouteDto(delta, candidate.RouteId);
                if (route != null) route.phase = (int)RoutePhase.Unloading;
            }

            var unloading = new List<CollectorRouteDto>();
            foreach (CollectorRouteDto r in delta.collectorRoutes) if ((RoutePhase)r.phase == RoutePhase.Unloading) unloading.Add(r);
            unloading.Sort((a, b) => string.CompareOrdinal(a.vehicleId, b.vehicleId));

            float remainingBudget = def.TransferVolumePerSecond * dt;
            foreach (CollectorRouteDto r in unloading)
            {
                if (remainingBudget <= 0.0001f) break;
                VehicleDto vdto = FindVehicleDtoByIdMutable(delta, r.vehicleId, out int vIdx);
                if (vdto == null || vdto.heldAmount <= 0.0001f || vdto.heldItemId == null) continue;
                Hangar.VehicleDef vdef = Hangar.VehicleCatalog.Get((Hangar.VehicleClass)vdto.vehicleClass, vdto.mk);
                float rate = vdef != null ? vdef.TransferVolumePerSecond : ResourceTransferService.VolumePerSecond;
                float want = Mathf.Min(rate * dt, Mathf.Min(vdto.heldAmount, remainingBudget));
                float added = DtoInventoryAdd(center, def, vdto.heldItemId, want);
                vdto.heldAmount -= added;
                if (vdto.heldAmount <= 0.0001f) { vdto.heldAmount = 0f; vdto.heldItemId = null; }
                remainingBudget -= added;
                delta.vehicles[vIdx] = vdto;

                if (vdto.heldAmount <= 0.0001f)
                {
                    r.phase = (int)(def.Capacity - DtoInventoryTotal(center) > 0.0001f ? RoutePhase.ToSource : RoutePhase.Holding);
                    if ((RoutePhase)r.phase == RoutePhase.Holding) r.dockWaitStartedAt = delta.simElapsedSeconds;
                }
                else if (added <= 0.0001f && def.Capacity - DtoInventoryTotal(center) <= 0.0001f)
                {
                    r.phase = (int)RoutePhase.Holding;
                    r.dockWaitStartedAt = delta.simElapsedSeconds;
                }
            }
        }

        private static float DtoInventoryTotal(LogisticsCenterDto center)
        {
            float total = 0f;
            foreach (CargoItemDto item in center.inventory) total += item.amount * CargoItemVolumes.PerUnit(item.itemId);
            return total;
        }

        private static float DtoInventoryAdd(LogisticsCenterDto center, LogisticsCenterTierDef def, string itemId, float amount)
        {
            if (amount <= 0f || !LogisticsCenterCatalog.IsAcceptedItem(itemId)) return 0f;
            float perUnit = Mathf.Max(0.0001f, CargoItemVolumes.PerUnit(itemId));
            float freeVol = Mathf.Max(0f, def.Capacity - DtoInventoryTotal(center));
            float toAdd = Mathf.Min(amount, freeVol / perUnit);
            if (toAdd <= 0f) return 0f;
            for (int i = 0; i < center.inventory.Count; i++)
                if (center.inventory[i].itemId == itemId) { center.inventory[i].amount += toAdd; return toAdd; }
            center.inventory.Add(new CargoItemDto { itemId = itemId, amount = toAdd });
            return toAdd;
        }

        private static float ComputeDrillToCenterDistance(SystemManifest manifest, SystemDelta delta, string drillWorldId)
        {
            if (manifest == null) return 0f;
            Vector3 planetPos = Vector3.zero; bool found = false;
            foreach (PlanetSnapDto snap in delta.planetSnapshots)
                if (snap.worldId == drillWorldId) { planetPos = new Vector3(snap.px, snap.py, snap.pz); found = true; break; }
            if (!found) return 0f;
            if (!LogisticsCenterAnchor.TryComputeAbsolutePosition(manifest, out Vector3 centerAbs, out _)) return 0f;
            return Vector3.Distance(planetPos, centerAbs);
        }

        private static bool TryFindDrillIndex(SystemDelta delta, string worldId, out int index)
        {
            for (int i = 0; i < delta.drills.Count; i++) if (delta.drills[i].worldId == worldId) { index = i; return true; }
            index = -1; return false;
        }

        private static int CountRoutePhase(SystemDelta delta, RoutePhase phase)
        {
            int count = 0;
            foreach (CollectorRouteDto r in delta.collectorRoutes) if ((RoutePhase)r.phase == phase) count++;
            return count;
        }

        private static CollectorRouteDto FindRouteDto(SystemDelta delta, string routeId)
        {
            foreach (CollectorRouteDto r in delta.collectorRoutes) if (r.routeId == routeId) return r;
            return null;
        }

        private static VehicleDto FindVehicleDtoByIdMutable(SystemDelta delta, string vehicleId, out int index)
        {
            for (int i = 0; i < delta.vehicles.Count; i++)
                if (delta.vehicles[i].vehicleId == vehicleId) { index = i; return delta.vehicles[i]; }
            index = -1; return null;
        }

        private static DrillDto FindDrillByWorldId(SystemDelta delta, string worldId)
        {
            foreach (DrillDto d in delta.drills) if (d.worldId == worldId) return d;
            return default;
        }

        private static bool IsDominanceSuspended(SystemDelta delta, string worldId)
        {
            if (delta.abandonedPlanets.Contains(worldId)) return true;
            foreach (DominanceDto d in delta.dominance)
                if (d.worldId == worldId) return (DominanceState)d.state == DominanceState.Contested || (DominanceState)d.state == DominanceState.Lost;
            return false;
        }

        // --- Delta capture / restore -------------------------------------------------
        private SystemDelta CaptureDelta(SystemManifest manifest, MaterializedSystem materialized, bool destroyVehicles)
        {
            Vector3 offset = FloatingOrigin.TotalOffset;
            var delta = new SystemDelta { systemId = manifest.SystemId };

            foreach (var kv in materialized.PlanetsByIndex)
            {
                PlanetIdentity identity = kv.Value;
                if (identity == null || identity.Body == null) continue;
                PlanetProfile profile = ExtractionState.GetProfile(identity);
                if (profile != null && profile.Scanned) delta.scanned.Add(identity.WorldId);

                Vector3 abs = identity.Body.transform.position + offset;
                delta.planetSnapshots.Add(new PlanetSnapDto
                {
                    worldId = identity.WorldId,
                    px = abs.x, py = abs.y, pz = abs.z,
                    rx = identity.Body.transform.rotation.x, ry = identity.Body.transform.rotation.y,
                    rz = identity.Body.transform.rotation.z, rw = identity.Body.transform.rotation.w,
                    vx = identity.Body.Velocity.x, vy = identity.Body.Velocity.y, vz = identity.Body.Velocity.z
                });
            }

            foreach (DrillStructure drill in ExtractionState.Drills)
            {
                if (drill == null || WorldIdCodec.SystemIdFromPlanetWorldId(drill.WorldId) != manifest.SystemId) continue;
                DrillRuntime r = drill.Runtime;
                var drillDto = new DrillDto
                {
                    worldId = drill.WorldId, nodeId = drill.NodeId, state = (int)r.State,
                    capsuleRemaining = r.CapsuleRemaining, buildRemaining = r.BuildRemaining,
                    stored = r.StoredAmount, accumulator = r.Accumulator, source = (int)drill.Source, refundParts = drill.RefundParts,
                    tier = r.Tier
                };
                // VS-017A.2 §B5: the in-flight T1->T2 upgrade, with the real remaining
                // reserved cost so a load re-reserves exactly what is still owed.
                DrillUpgradeJob upgradeJob = r.ActiveUpgradeJob;
                if (upgradeJob != null)
                {
                    drillDto.upgradeActive = true;
                    drillDto.upgradeTotalSeconds = upgradeJob.TotalSeconds;
                    drillDto.upgradeRemainingSeconds = upgradeJob.RemainingSeconds;
                    LogisticsCargo upgradeCargo = ship != null ? ship.GetComponent<Extraction.ShipLogistics>()?.Cargo : null;
                    if (upgradeCargo != null)
                        foreach (var reserved in upgradeCargo.GetOwnerReservations(DrillUpgradeService.OwnerId(drill.WorldId, drill.NodeId)))
                            drillDto.upgradeRemainingCost.Add(new ItemAmountDto { itemId = reserved.Key, amount = reserved.Value });
                }
                delta.drills.Add(drillDto);
            }

            foreach (SurfaceStructure s in ExtractionState.Structures)
            {
                if (s == null || WorldIdCodec.SystemIdFromPlanetWorldId(s.WorldId) != manifest.SystemId) continue;
                StructureRuntime r = s.Runtime; Durability d = r.Durability;
                var structureDto = new StructureDto
                {
                    worldId = s.WorldId, nodeId = s.NodeId, type = (int)s.Type, state = (int)r.State,
                    capsuleRemaining = r.CapsuleRemaining, buildRemaining = r.BuildRemaining,
                    shield = d.Shield, armor = d.Armor, hull = d.Hull, regenTimer = d.SavedRegenTimer,
                    tier = r.Tier
                };
                foreach (var (itemId, amount) in s.BuildFullCost) structureDto.buildCost.Add(new ItemAmountDto { itemId = itemId, amount = amount });
                if (s.FlatRefund != null) foreach (var (itemId, amount) in s.FlatRefund) structureDto.flatRefund.Add(new ItemAmountDto { itemId = itemId, amount = amount });
                // VS-014 §2/§17: in-flight tier upgrade job, if any -- only target tier and
                // progress are saved; RestoreUpgradeJob re-derives the remaining reservation
                // from those two numbers against the tier's own catalog cost, exactly like a
                // fresh RestoreUpgradeJob call already does for a live in-session cancel.
                if (s.ActiveUpgradeJob != null)
                {
                    structureDto.upgrading = true;
                    structureDto.upgradeTargetTier = s.ActiveUpgradeJob.TargetTier;
                    structureDto.upgradeProgress01 = s.ActiveUpgradeJob.Progress01;
                }
                if (s.MaintenanceReserve != null)
                {
                    var reserveDto = new MaintenanceReserveDto
                    {
                        policy = (int)s.MaintenanceReserve.Policy,
                        activeEngineerTargetId = s.MaintenanceReserve.ActiveEngineerTargetId
                    };
                    reserveDto.queuedTargetIds.AddRange(s.MaintenanceReserve.QueuedTargetIds);
                    foreach (var kv in s.MaintenanceReserve.Cargo.Items) reserveDto.items.Add(new ItemAmountDto { itemId = kv.Key, amount = kv.Value });
                    structureDto.maintenanceReserve = reserveDto;
                }
                delta.structures.Add(structureDto);
            }

            foreach (var kv in ExtractionState.AllDominance)
            {
                if (WorldIdCodec.SystemIdFromPlanetWorldId(kv.Key) != manifest.SystemId) continue;
                delta.dominance.Add(new DominanceDto { worldId = kv.Key, state = (int)kv.Value.State, stabilizeTimer = kv.Value.StabilizeTimer, recoveryTimer = kv.Value.RecoveryTimer });
                if (kv.Value.Abandoned) delta.abandonedPlanets.Add(kv.Key);
            }

            if (hangar != null)
            {
                var vehiclesInSystem = new List<VehicleAgent>();
                foreach (VehicleAgent v in hangar.Vehicles)
                {
                    // A route/Engineer/Escort-controlled vehicle's Mission.Phase stays InHangar (those
                    // bypass the Mission machinery entirely, VS-011 §4/VS-015 §3-4) — it must still be
                    // captured/destroyed here (mirrors the exact VS-011 Route fix, extended to VS-015's two new task types).
                    bool routeControlled = v != null && (v.Route != null || v.EngineerTask != null || v.EscortState != null || v.ResupplyTask != null);
                    if (v == null || (!routeControlled && v.Mission.Phase == VehiclePhase.InHangar)) continue;
                    if (v.HomeSystemId != manifest.SystemId) continue;
                    vehiclesInSystem.Add(v);

                    Vector3 abs = v.transform.position + offset;
                    var vehicleDto = new VehicleDto
                    {
                        vehicleId = v.VehicleId, vehicleClass = (int)v.Def.Class, mk = v.Def.Mk, homeSystemId = v.HomeSystemId,
                        phase = (int)v.Mission.Phase, missionKind = (int)v.Mission.Kind, targetId = v.Mission.TargetId,
                        phaseTimer = v.Mission.PhaseTimer, recallRequested = v.Mission.RecallRequested,
                        px = abs.x, py = abs.y, pz = abs.z, rx = 0f, ry = 0f, rz = 0f, rw = 1f,
                        heldItemId = v.HeldItemId, heldAmount = v.HeldAmount,
                        shield = v.Shield, armor = v.Armor, hull = v.Hull, stranded = v.Stranded
                    };
                    CaptureEngineerAndEscort(v, vehicleDto);
                    delta.vehicles.Add(vehicleDto);
                }
                if (destroyVehicles)
                {
                    foreach (VehicleAgent v in vehiclesInSystem) { Object.Destroy(v.gameObject); hangar.RemoveVehicleReference(v); }
                }
            }

            CaptureLogistics(manifest, delta, destroyVehicles);
            CapturePois(delta);
            CaptureCombat(delta);
            CaptureInterventionGroup(manifest, delta);

            return delta;
        }

        /// <summary>VS-015 §10: restores an in-flight Engineer/Escort task exactly as saved and re-derives the resource reservation from the real REMAINING cost (reserved - consumed), never re-reserving the full original amount (no free/duplicated resources on load).</summary>
        private void RestoreEngineerAndEscort(Hangar.VehicleAgent agent, VehicleDto dto)
        {
            if (dto.engineerTask != null && dto.engineerTask.active)
            {
                var e = dto.engineerTask;
                var task = Hangar.EngineerMissionState.Restore(e.missionId, dto.vehicleId, e.systemId, (Hangar.EngineerTargetKind)e.targetKind,
                    e.targetId, (Hangar.EngineerWorkKind)e.workKind, (Hangar.EngineerSourceKind)e.sourceKind, e.chosenMaterialId,
                    e.playerManuallyStarted, e.reservedParts, e.reservedMaterialAmount, e.consumedParts, e.consumedMaterialAmount,
                    (Hangar.EngineerPhase)e.phase, e.safeSecondsAccumulated, e.xpAwarded, e.plannedRestoredHp, e.actualRestoredHpSoFar);

                LogisticsCargo source = e.sourceKind == (int)Hangar.EngineerSourceKind.ShipCargo
                    ? hangar?.GetComponent<ShipLogistics>()?.Cargo
                    : FindMaintenanceReserveCargoForSystem(e.systemId);
                bool funded = true;
                string ownerId = $"engineer-mission:{e.missionId}";
                var remaining = new List<(string, float)>();
                int remainingParts = Mathf.Max(0, e.reservedParts - e.consumedParts);
                float remainingMaterial = Mathf.Max(0f, e.reservedMaterialAmount - e.consumedMaterialAmount);
                if (remainingParts > 0) remaining.Add((ProcessingCatalog.BasicPartId, remainingParts));
                if (remainingMaterial > 0f && !string.IsNullOrEmpty(e.chosenMaterialId)) remaining.Add((e.chosenMaterialId, remainingMaterial));
                if (remaining.Count > 0)
                {
                    funded = source != null && source.TrySetReservation(ownerId, remaining);
                }
                if (funded) agent.RestoreEngineerTask(task);
            }
            if (dto.escortState != null && dto.escortState.active)
            {
                var e = dto.escortState;
                var state = Hangar.EscortState.Restore(dto.vehicleId, e.missionId, e.protectedTargetId, (Combat.TargetCategory)e.protectedTargetCategory,
                    e.playerManuallyStarted, e.xpAwarded, (Hangar.EscortCommand)e.command, (Hangar.EscortStance)e.stance,
                    (Hangar.EscortSubState)e.subState, e.engagedThreatId);
                agent.RestoreEscortState(state);
            }
            if (dto.resupplyTask != null && dto.resupplyTask.active)
            {
                var r = dto.resupplyTask;
                var mission = Hangar.MaintenanceResupplyMission.Restore(r.missionId, dto.vehicleId, r.systemId, r.targetCommandStructureId,
                    r.itemId, r.plannedAmount, (Hangar.MaintenanceResupplyPhase)r.phase);
                agent.RestoreResupplyTask(mission);
            }
        }

        private LogisticsCargo FindMaintenanceReserveCargoForSystem(string systemId)
        {
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.Type == SurfaceStructureType.Command && s.MaintenanceReserve != null && WorldIdCodec.SystemIdFromPlanetWorldId(s.WorldId) == systemId)
                    return s.MaintenanceReserve.Cargo;
            return null;
        }

        /// <summary>VS-015 §3/§4/§10: nests the vehicle's Engineer mission / Escort assignment into its own VehicleDto — active=false (the correct default) when neither is running. A demoted system's mission simply stops being ticked (frozen, never advanced) until promoted back to Full and this same DTO is restored.</summary>
        private static void CaptureEngineerAndEscort(Hangar.VehicleAgent v, VehicleDto dto)
        {
            if (v.EngineerTask != null)
            {
                Hangar.EngineerMissionState t = v.EngineerTask;
                dto.engineerTask = new EngineerMissionDto
                {
                    active = true, missionId = t.MissionId, systemId = t.SystemId,
                    targetKind = (int)t.TargetKind, targetId = t.TargetId, workKind = (int)t.WorkKind, sourceKind = (int)t.SourceKind,
                    chosenMaterialId = t.ChosenMaterialId, playerManuallyStarted = t.PlayerManuallyStarted,
                    reservedParts = t.ReservedParts, reservedMaterialAmount = t.ReservedMaterialAmount,
                    consumedParts = t.ConsumedParts, consumedMaterialAmount = t.ConsumedMaterialAmount,
                    phase = (int)t.Phase, safeSecondsAccumulated = t.SafeSecondsAccumulated, xpAwarded = t.XpAwarded,
                    plannedRestoredHp = t.PlannedRestoredHp, actualRestoredHpSoFar = t.ActualRestoredHpSoFar
                };
            }
            if (v.EscortState != null)
            {
                Hangar.EscortState e = v.EscortState;
                dto.escortState = new EscortStateDto
                {
                    active = true, missionId = e.MissionId, protectedTargetId = e.ProtectedTargetId,
                    protectedTargetCategory = (int)e.ProtectedTargetCategory, playerManuallyStarted = e.PlayerManuallyStarted, xpAwarded = e.XpAwarded,
                    command = (int)e.Command, stance = (int)e.Stance, subState = (int)e.SubState, engagedThreatId = e.EngagedThreatId
                };
            }
            if (v.ResupplyTask != null)
            {
                Hangar.MaintenanceResupplyMission r = v.ResupplyTask;
                dto.resupplyTask = new MaintenanceResupplyDto
                {
                    active = true, missionId = r.MissionId, systemId = r.SystemId,
                    targetCommandStructureId = r.TargetCommandStructureId, itemId = r.ItemId, plannedAmount = r.PlannedAmount,
                    phase = (int)r.Phase
                };
            }
        }

        /// <summary>
        /// VS-013: this system's live combat state — the encounter open/close
        /// timer, every EnemyAgent (the static registry only ever holds the
        /// current Full system's entries, same invariant as PoiLiveState),
        /// every in-flight projectile, and any un-collected combat wreckage.
        /// Nothing here is captured for a system that isn't the current Full
        /// one (CombatEncounter is null in that case).
        /// </summary>
        private void CaptureCombat(SystemDelta delta)
        {
            if (CombatEncounter == null) return;
            Vector3 offset = FloatingOrigin.TotalOffset;

            delta.combatEncounterOpen = CombatEncounter.Encounter.IsOpen;
            delta.combatEncounterSinceLastThreat = CombatEncounter.Encounter.SinceLastThreatSeconds;

            foreach (Combat.EnemyAgent agent in Combat.EnemyAgent.All.Values)
            {
                if (agent == null || agent.Destroyed) continue;
                Vector3 abs = agent.transform.position + offset;
                var enemyDto = new EnemyDto
                {
                    enemyId = agent.Id, defId = agent.Def.Id,
                    px = abs.x, py = abs.y, pz = abs.z,
                    vx = 0f, vy = 0f, vz = 0f,
                    rx = agent.transform.rotation.x, ry = agent.transform.rotation.y, rz = agent.transform.rotation.z, rw = agent.transform.rotation.w,
                    aiPhase = (int)agent.Ai.Phase, targetId = agent.Ai.TargetId,
                    scanElapsed = agent.Ai.ScanElapsed, continuousEscapeSeconds = 0f,
                    shield = agent.Combat.Shield, armor = agent.Combat.Armor, hull = agent.Combat.Hull, regenTimer = agent.Combat.SavedRegenTimer,
                    structureTargetId = agent.StructureTarget?.TargetId,
                    disruptRemaining = agent.DisruptRemaining, disruptImmunityRemaining = agent.DisruptImmunityRemaining,
                    telegraphRemaining = agent.TelegraphRemaining
                };
                if (agent.LooterCargo != null)
                    foreach (var kv in agent.LooterCargo.Items) enemyDto.looterCargo.Add(new CargoItemDto { itemId = kv.Key, amount = kv.Value });
                delta.enemies.Add(enemyDto);
            }

            // VS-016A.1 §1: every live in-flight shot becomes a PendingAttackDto -- the
            // single carryover authority (never both ProjectileDto and PendingAttackDto
            // for the same attack, per spec). delta.projectiles is intentionally left
            // untouched here (any prior entries are stale/cleared elsewhere on promote).
            foreach (Combat.ProjectileObject proj in CombatEncounter.Projectiles)
            {
                if (proj == null || proj.State == null) continue;
                delta.pendingAttacks.Add(CaptureAttackDto(proj.State));
            }

            // VS-014 §8/§17: this system's pressure/grace/raid-flow + per-planet defense coordination.
            delta.threatPressure = new ThreatPressureDto
            {
                pressure = CombatEncounter.Pressure.Pressure, graceRemainingActiveMinutes = CombatEncounter.Pressure.GraceRemainingActiveMinutes,
                foreignPresenceActive = CombatEncounter.Pressure.ForeignPresenceActive, foreignPresenceActiveMinutesElapsed = CombatEncounter.Pressure.ForeignPresenceActiveMinutesElapsed,
                firstControlledGraceGranted = CombatEncounter.Pressure.FirstControlledGraceGranted,
                phase = (int)CombatEncounter.Pressure.Phase, stage = CombatEncounter.Pressure.Stage.HasValue ? (int)CombatEncounter.Pressure.Stage.Value : -1,
                doctrine = CombatEncounter.Pressure.Doctrine.HasValue ? (int)CombatEncounter.Pressure.Doctrine.Value : -1,
                targetPlanetWorldId = CombatEncounter.Pressure.TargetPlanetWorldId, encounterSeed = CombatEncounter.Pressure.EncounterSeed.ToString(),
                warningRemainingSeconds = CombatEncounter.Pressure.WarningRemainingSeconds, elapsedActiveSeconds = CombatEncounter.Pressure.ElapsedActiveSeconds,
                playerDealtMeaningfulDamage = CombatEncounter.Pressure.PlayerDealtMeaningfulDamage, playerIssuedValidFocusCommand = CombatEncounter.Pressure.PlayerIssuedValidFocusCommand,
                playerActiveDefenseParticipation = CombatEncounter.Pressure.PlayerActiveDefenseParticipation, playerWasShutdownDuringRaid = CombatEncounter.Pressure.PlayerWasShutdownDuringRaid,
                anyResourceStolen = CombatEncounter.Pressure.AnyResourceStolen, resultApplied = CombatEncounter.Pressure.ResultApplied,
                salvageApplied = CombatEncounter.Pressure.SalvageApplied, pendingProbeEffectConsumedThisRaid = CombatEncounter.Pressure.PendingProbeEffectConsumedThisRaid,
                wasTargetContestedDuringRaid = CombatEncounter.Pressure.WasTargetContestedDuringRaid
            };
            delta.threatPressure.compositionDefIds.AddRange(CombatEncounter.Pressure.CompositionDefIds);
            delta.threatPressure.spawnedEnemyIds.AddRange(CombatEncounter.Pressure.SpawnedEnemyIds);

            foreach (var (worldId, mode, focusEnemyId) in CombatEncounter.DefenseCoordinator.Snapshot())
                delta.defenseCoordination.Add(new DefenseCoordinationDto { worldId = worldId, mode = (int)mode, focusEnemyId = focusEnemyId });

            foreach (CombatWreckageState wreck in CombatEncounter.Wreckage)
            {
                var dto = new CombatWreckageDto { wreckageId = wreck.WreckageId, remainingActiveSeconds = wreck.RemainingActiveSeconds };
                foreach (CombatWreckageNode node in wreck.Nodes)
                    dto.nodes.Add(new CombatWreckageNodeDto { itemId = node.ItemId, total = node.Total, remaining = node.Remaining });
                delta.combatWreckage.Add(dto);
            }
        }

        /// <summary>
        /// VS-016A.1 §1 / VS-017A §1: converts one live in-flight shot into its carryover DTO.
        /// Live positions here are all in the same pre-<see cref="FloatingOrigin"/>-offset local
        /// frame, so no offset is needed for the distance term. Every live spawn site now sets a
        /// real IntendedTargetId (ship/enemy/structure) even though Plasma/Siege never LOCK a
        /// target in the live game (proximity hit detection instead) -- see ProjectileState's own
        /// doc comment. When that target can be resolved to a live position, remaining flight time
        /// is the real distance/speed, capped by the shot's own remaining lifetime so it can never
        /// overshoot; when it can't (target already gone), the shot only has its remaining lifetime
        /// to carry, targetId stays empty, and it will deterministically expire with no result once
        /// ticked out -- it never picks a new remote target.
        /// </summary>
        private PendingAttackDto CaptureAttackDto(Combat.ProjectileState state)
        {
            string targetId = state.IntendedTargetId;
            float remaining = state.RemainingLifetimeSeconds;
            if (!string.IsNullOrEmpty(targetId))
            {
                Vector3? targetPos = ResolveLiveAttackPosition(targetId);
                float speed = state.Velocity.magnitude;
                if (targetPos.HasValue && speed > 0.01f)
                {
                    float distance = Vector3.Distance(state.Position, targetPos.Value);
                    remaining = Mathf.Min(remaining, distance / speed);
                }
            }

            // The live ship's EM shot always spawns with DamageAmount 0 -- ProjectileObject.ApplyHit's
            // EM branch ignores DamageAmount entirely and always derives it from State.Tier at hit
            // time (WeaponCatalog.EmDamagePerShot). A PendingAttackDto has no tier field, so that
            // derivation has to happen now, at capture, or a promoted/remote-resolved EM hit would
            // silently deal zero damage. Plasma already carries a real non-zero DamageAmount from
            // both live spawn sites (planetary defense DPS, ship's own catalog lookup), so no
            // fallback is needed there.
            float damageAmount = state.DamageAmount;
            if (state.Kind == Combat.ProjectileKind.Em && damageAmount <= 0f)
                damageAmount = Combat.WeaponCatalog.EmDamagePerShot(state.Tier);

            PendingAttack attack = PendingAttack.CaptureFromLive(state.ProjectileId, state.OwnerId, targetId ?? string.Empty,
                state.Kind, state.Faction, damageAmount, remaining, state.Interceptable, state.InterceptionDurabilityRemaining);

            return new PendingAttackDto
            {
                attackId = attack.AttackId, attackerId = attack.AttackerId, targetId = attack.TargetId,
                kind = (int)attack.Kind, faction = (int)attack.Faction, damageAmount = attack.DamageAmount,
                remainingFlightSeconds = attack.RemainingFlightSeconds, splashRadius = attack.SplashRadius,
                splashMaxFractionOfDirect = attack.SplashMaxFractionOfDirect, isEm = attack.IsEm,
                emDisruptSeconds = attack.EmDisruptSeconds, emImmunityAfterDisruptSeconds = attack.EmImmunityAfterDisruptSeconds,
                interceptable = attack.Interceptable, interceptionDurabilityRemaining = attack.InterceptionDurabilityRemaining,
                applied = false
            };
        }

        /// <summary>VS-015.1 §1: this system's single intervention group, if the InterventionGroupDirector's live group belongs to it — capture only, never destroys/re-picks a target, so a demoted group's last-known state is exactly what gets restored later (no remote tick advances it).</summary>
        private void CaptureInterventionGroup(SystemManifest manifest, SystemDelta delta)
        {
            Combat.InterventionGroup group = InterventionGroupDirector != null ? InterventionGroupDirector.Group : null;
            if (group == null || group.SystemId != manifest.SystemId) return;

            var dto = new InterventionGroupDto
            {
                policy = (int)group.Policy,
                activeTargetPlanetId = group.ActiveTargetPlanetId,
                holdingOrRetreating = group.HoldingOrRetreating,
                knownThreatPowerSnapshot = group.KnownThreatPowerSnapshot
            };
            dto.memberVehicleIds.AddRange(group.MemberVehicleIds);
            delta.interventionGroup = dto;
        }

        /// <summary>VS-012: this system's live POI mutable state. PoiLiveState only ever holds the current Full system's entries (same convention as ExtractionState.Drills/LogisticsState), so no per-system filtering is needed here. The POI GameObjects themselves need no explicit destroy call — they are children of the system root and are torn down for free by SystemMaterializer.Destroy(fullSystem) right after this returns (their own OnDestroy unregisters them from Poi.PoiObject/Poi.PoiLiveState).</summary>
        private static void CapturePois(SystemDelta delta)
        {
            foreach (Poi.PoiState state in Poi.PoiLiveState.States.Values)
            {
                var dto = new PoiDto
                {
                    poiId = state.PoiId, level = (int)state.Level,
                    locateElapsed = state.LocateElapsed, identifyElapsed = state.IdentifyElapsed, operationElapsed = state.OperationElapsed,
                    pauseReason = (int)state.PauseReason, assignedVehicleId = state.AssignedVehicleId,
                    anomalyXpAwarded = state.AnomalyXpAwarded, wreckXpAwarded = state.WreckXpAwarded,
                    facilityChoice = (int)state.Choice,
                    facilityOutputBasicRemaining = state.FacilityOutputBasicRemaining, facilityOutputSpecialRemaining = state.FacilityOutputSpecialRemaining,
                    facilityOutputSpecialItemId = state.FacilityOutputSpecialItemId,
                    facilityXpAwarded = state.FacilityXpAwarded, facilitySignalApplied = state.FacilitySignalApplied
                };
                foreach (Poi.SalvageNode n in state.SalvageNodes)
                    dto.salvageNodes.Add(new PoiSalvageNodeDto { itemId = n.ItemId, total = n.Total, remaining = n.Remaining });
                delta.pois.Add(dto);
            }
        }

        /// <summary>VS-011: the system's single Logistics Center (if any) and its persistent Collector routes. A demotion (destroyRoutes) also drops them from the live LogisticsState registry — the center's GameObject itself is destroyed for free as a child of the system root; a route is pure data and has no other owner to clean it up.</summary>
        private static void CaptureLogistics(SystemManifest manifest, SystemDelta delta, bool destroyRoutes)
        {
            LogisticsCenter center = LogisticsState.FindCenter(manifest.SystemId);
            if (center != null)
            {
                var inventory = new List<CargoItemDto>();
                foreach (var kv in center.Inventory.Items) inventory.Add(new CargoItemDto { itemId = kv.Key, amount = kv.Value });
                Durability d = center.Runtime.Durability;
                delta.logisticsCenters.Add(new LogisticsCenterDto
                {
                    systemId = manifest.SystemId, tier = (int)center.Runtime.Tier, state = (int)center.Runtime.State,
                    buildRemaining = center.Runtime.BuildRemaining, upgrading = center.Runtime.Upgrading, upgradeRemaining = center.Runtime.UpgradeRemaining,
                    shield = d.Shield, armor = d.Armor, hull = d.Hull, regenTimer = d.SavedRegenTimer,
                    inventory = inventory,
                    refundParts = center.RefundParts, refundMetalAlloy = center.RefundMetalAlloy, refundBiocomposite = center.RefundBiocomposite,
                    upgradeRefundParts = center.UpgradeRefundParts, upgradeRefundMetalAlloy = center.UpgradeRefundMetalAlloy, upgradeRefundBiocomposite = center.UpgradeRefundBiocomposite
                });
            }

            var routesToDrop = destroyRoutes ? new List<CollectorRoute>() : null;
            foreach (CollectorRoute route in LogisticsState.Routes)
            {
                if (route == null || route.SystemId != manifest.SystemId) continue;
                delta.collectorRoutes.Add(new CollectorRouteDto
                {
                    routeId = route.RouteId, systemId = route.SystemId, drillWorldId = route.DrillWorldId, vehicleId = route.VehicleId,
                    priority = (int)route.Priority, phase = (int)route.Phase, resumePhase = (int)route.ResumePhase, waitReason = (int)route.WaitReason,
                    remainingDistance = route.RemainingDistance,
                    activeTransferTransactionId = route.ActiveTransferTransactionId, lastTransferTransactionId = route.LastTransferTransactionId,
                    dockWaitStartedAt = route.DockWaitStartedAt
                });
                if (destroyRoutes) routesToDrop.Add(route);
            }
            if (destroyRoutes) foreach (CollectorRoute r in routesToDrop) LogisticsState.RemoveRoute(r);
        }

        private void RestoreDeltaInto(SystemManifest manifest, MaterializedSystem materialized)
        {
            SystemDelta delta = StarSystemRegistry.Deltas.TryGet(manifest.SystemId);
            if (delta == null) return;

            Vector3 offset = FloatingOrigin.TotalOffset;
            foreach (var kv in materialized.PlanetsByIndex)
            {
                if (kv.Value == null || kv.Value.Body == null) continue;
                foreach (PlanetSnapDto snap in delta.planetSnapshots)
                {
                    if (snap.worldId != kv.Value.WorldId) continue;
                    kv.Value.Body.transform.position = new Vector3(snap.px, snap.py, snap.pz) - offset;
                    kv.Value.Body.transform.rotation = new Quaternion(snap.rx, snap.ry, snap.rz, snap.rw);
                    kv.Value.Body.Velocity = new Vector3(snap.vx, snap.vy, snap.vz);
                    break;
                }
            }

            foreach (string worldId in delta.scanned) ExtractionState.MarkScanned(worldId);

            foreach (DrillDto dto in delta.drills)
            {
                PlanetIdentity planet = ExtractionState.FindPlanet(dto.worldId);
                if (planet == null) continue;
                PlanetProfile profile = ExtractionState.GetProfile(planet);
                var runtime = new DrillRuntime(profile, (DrillLifecycle)dto.state, dto.capsuleRemaining, dto.buildRemaining, dto.stored, dto.accumulator, dto.tier);
                DrillStructure drill = DrillStructure.Restore(planet, profile, runtime, (DrillStructure.BuildSource)dto.source, dto.refundParts);

                // VS-017A.2 §B5: an in-flight T1->T2 upgrade resumes at its exact saved
                // progress, re-reserving only what is genuinely still owed. If the cargo can
                // no longer cover that remainder, RestoreUpgrade stops the job outright
                // rather than letting it finish for free.
                if (dto.upgradeActive && dto.upgradeRemainingCost != null && dto.upgradeRemainingCost.Count > 0)
                {
                    var remainingCost = new List<(string itemId, float amount)>();
                    foreach (ItemAmountDto item in dto.upgradeRemainingCost) remainingCost.Add((item.itemId, item.amount));
                    var job = new DrillUpgradeJob(DrillUpgradeCatalog.MaxTier, dto.upgradeTotalSeconds, dto.upgradeRemainingSeconds);
                    drill.RestoreUpgrade(job, ship != null ? ship.GetComponent<Extraction.ShipLogistics>()?.Cargo : null, remainingCost);
                }
            }

            foreach (StructureDto dto in delta.structures)
            {
                PlanetIdentity planet = ExtractionState.FindPlanet(dto.worldId);
                if (planet == null) continue;
                PlanetProfile profile = ExtractionState.GetProfile(planet);
                var type = (SurfaceStructureType)dto.type;
                SurfaceStructureDef def = SurfaceStructureCatalog.Get(type);
                // VS-014: looked up by the structure's own saved node id, never re-derived from
                // type alone -- Command vs FirstDefenseNode was correct pre-VS-014 (only Laser
                // could ever occupy the sole "defense" node), but a Plasma structure on defense.1
                // would have silently been restored onto Laser's own defense.0 node otherwise.
                SurfaceNode node = FindNodeById(profile, dto.nodeId);
                var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 0.5f);
                durability.RestoreFromSave(dto.shield, dto.armor, dto.hull, dto.regenTimer);
                var runtime = new StructureRuntime(def, (DrillLifecycle)dto.state, dto.capsuleRemaining, dto.buildRemaining, durability, dto.tier);
                Extraction.ShipLogistics shipLogistics = ship != null ? ship.GetComponent<Extraction.ShipLogistics>() : null;
                SurfaceStructure structure;
                if (dto.flatRefund.Count > 0)
                {
                    var flat = new List<(string, float)>();
                    foreach (ItemAmountDto item in dto.flatRefund) flat.Add((item.itemId, item.amount));
                    structure = SurfaceStructure.RestoreWithFlatRefund(planet, profile, def, node, runtime, flat);
                }
                else
                {
                    var buildCost = new List<(string, float)>();
                    foreach (ItemAmountDto item in dto.buildCost) buildCost.Add((item.itemId, item.amount));
                    structure = SurfaceStructure.Restore(planet, profile, def, node, runtime, shipLogistics?.Cargo, buildCost);
                }
                if (structure == null) continue;
                if (dto.upgrading)
                {
                    structure.RestoreUpgradeJob(dto.upgradeTargetTier, dto.upgradeProgress01, shipLogistics?.Cargo);
                }
                if (dto.maintenanceReserve != null)
                {
                    var items = new List<(string, float)>();
                    foreach (ItemAmountDto item in dto.maintenanceReserve.items) items.Add((item.itemId, item.amount));
                    structure.RestoreMaintenanceReserve((MaintenancePolicy)dto.maintenanceReserve.policy, dto.maintenanceReserve.activeEngineerTargetId,
                        dto.maintenanceReserve.queuedTargetIds, items);
                }
            }

            foreach (DominanceDto dto in delta.dominance)
                ExtractionState.GetDominance(dto.worldId).Restore((DominanceState)dto.state, dto.stabilizeTimer, dto.recoveryTimer,
                    delta.abandonedPlanets.Contains(dto.worldId));

            // VS-011: Logistics Center + routes restored BEFORE vehicles, so a
            // route-controlled vehicle below can find its already-live route
            // by vehicle id (single source of truth — VehicleDto never
            // duplicates route linkage, see CollectorRouteDto).
            foreach (LogisticsCenterDto dto in delta.logisticsCenters)
            {
                var tier = (LogisticsCenterTier)dto.tier;
                LogisticsCenterTierDef def = LogisticsCenterCatalog.Get(tier);
                var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 0f);
                durability.RestoreFromSave(dto.shield, dto.armor, dto.hull, dto.regenTimer);
                var runtime = new LogisticsCenterRuntime(tier, (LogisticsCenterState)dto.state, dto.buildRemaining, durability, dto.upgrading, dto.upgradeRemaining);

                LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 anchorOffset, out _);
                var inventoryItems = new List<(string itemId, float amount)>();
                foreach (CargoItemDto item in dto.inventory) inventoryItems.Add((item.itemId, item.amount));
                LogisticsCenter center = LogisticsCenter.Restore(manifest, materialized.StarTransform, anchorOffset, runtime, inventoryItems);
                center.RestoreRefunds(dto.refundParts, dto.refundMetalAlloy, dto.refundBiocomposite,
                    dto.upgradeRefundParts, dto.upgradeRefundMetalAlloy, dto.upgradeRefundBiocomposite);
            }

            foreach (CollectorRouteDto dto in delta.collectorRoutes)
            {
                var route = new CollectorRoute(dto.routeId, dto.systemId, dto.drillWorldId, dto.vehicleId,
                    (RoutePriority)dto.priority, (RoutePhase)dto.phase, (RoutePhase)dto.resumePhase, (RouteWaitReason)dto.waitReason,
                    null, 0f, dto.remainingDistance, dto.activeTransferTransactionId, dto.lastTransferTransactionId, dto.dockWaitStartedAt);
                LogisticsState.AddRoute(route);
            }

            if (hangar != null)
            {
                foreach (VehicleDto dto in delta.vehicles)
                {
                    VehicleDef def = VehicleCatalog.Get((VehicleClass)dto.vehicleClass, dto.mk);
                    if (def == null) continue;
                    VehicleAgent agent = hangar.RestoreVehicle(dto.vehicleId, def);

                    CollectorRoute route = LogisticsState.FindRouteByVehicle(dto.vehicleId);
                    if (route != null)
                    {
                        agent.RestorePhysicalForRoute(route, new Vector3(dto.px, dto.py, dto.pz), offset, dto.heldItemId, dto.heldAmount);
                        route.SetHeld(dto.heldItemId, dto.heldAmount);
                        continue;
                    }

                    agent.RestorePhysical(dto.homeSystemId, new Vector3(dto.px, dto.py, dto.pz), new Quaternion(dto.rx, dto.ry, dto.rz, dto.rw),
                        offset, (VehiclePhase)dto.phase, (MissionKind)dto.missionKind, dto.targetId, dto.phaseTimer, dto.recallRequested,
                        dto.heldItemId, dto.heldAmount);
                    agent.RestoreDurability(dto.shield, dto.armor, dto.hull, dto.stranded);
                    RestoreEngineerAndEscort(agent, dto);
                }
            }

            RestoreInterventionGroup(manifest, delta);

            delta.drills.Clear(); delta.structures.Clear(); delta.dominance.Clear(); delta.vehicles.Clear();
            delta.planetSnapshots.Clear(); delta.logisticsCenters.Clear(); delta.collectorRoutes.Clear();
            // "scanned" stays (it mirrors PlanetProfile.Scanned, harmless/idempotent to re-mark and useful for save capture parity).
        }

        /// <summary>VS-015.1 §1: restores this system's intervention group exactly as saved — vehicle ids are reused as-is (never re-minted), no new group/vehicle spawned. Absent DTO (pre-v13.1 save or a system that never had one) -&gt; the director's EnsureGroupForSystem later creates a fresh Manual/empty group on first tick, the correct safe default.</summary>
        private void RestoreInterventionGroup(SystemManifest manifest, SystemDelta delta)
        {
            if (InterventionGroupDirector == null) return;
            if (delta.interventionGroup == null || (delta.interventionGroup.policy == 0 && delta.interventionGroup.memberVehicleIds.Count == 0))
            {
                InterventionGroupDirector.EnsureGroupForSystem(manifest.SystemId);
                return;
            }
            InterventionGroupDto dto = delta.interventionGroup;
            Combat.InterventionGroup group = Combat.InterventionGroup.Restore(manifest.SystemId, (Combat.InterventionPolicy)dto.policy,
                dto.memberVehicleIds, dto.activeTargetPlanetId, dto.holdingOrRetreating, dto.knownThreatPowerSnapshot);
            InterventionGroupDirector.RestoreGroup(group);
        }

        /// <summary>
        /// VS-012: records this system's first physical visit (foreign
        /// systems only — never the start system, which never carries POIs
        /// at all), resolves its POI manifest (deterministic from seed +
        /// the now-updated visit ledger — see Poi.PoiManifestGenerator for
        /// why this is provably stable, not a reroll), and materializes up
        /// to 2 PoiObjects, restoring saved mutable state from this
        /// system's delta if any exists, or a fresh PoiState otherwise.
        /// Independent of RestoreDeltaInto: a first-ever-visited system has
        /// no delta at all yet, but must still materialize its POIs.
        /// </summary>
        private void MaterializePois(SystemManifest manifest, MaterializedSystem materialized)
        {
            if (!manifest.IsStartSystem) Poi.PoiManifestRepository.Ledger.RecordVisit(manifest.SystemId);
            Poi.PoiManifestRepository.Invalidate(manifest.SystemId);
            List<Poi.PoiManifest> poiManifests = Poi.PoiManifestRepository.Resolve(ExtractionState.UniverseSeed, manifest);
            if (poiManifests.Count == 0) return;

            SystemDelta delta = StarSystemRegistry.Deltas.TryGet(manifest.SystemId);

            foreach (Poi.PoiManifest pm in poiManifests)
            {
                Poi.PoiState state = FindSavedPoiState(delta, pm.PoiId);
                if (state == null)
                {
                    state = new Poi.PoiState(pm.PoiId);
                    if (pm.Type == Poi.PoiType.Wreck) state.SetSalvageNodes(Poi.WreckSalvageGenerator.Generate(pm));
                }
                Poi.PoiObject.Create(pm, materialized.StarTransform, state, ship, hangar, Navigation?.Knowledge, Navigation?.FrontierSignal);
            }

            delta?.pois.Clear();
        }

        /// <summary>
        /// VS-013: creates this system's CombatEncounterDirector and restores
        /// any saved enemies/projectiles/wreckage/encounter-open state from
        /// its delta, or leaves it fresh (closed, no enemies) for a
        /// never-visited system — independent of RestoreDeltaInto, exactly
        /// like MaterializePois. Never called for Warm.
        /// </summary>
        private void MaterializeCombat(SystemManifest manifest, MaterializedSystem materialized)
        {
            CombatEncounter = CombatEncounterDirector.Create(manifest.SystemId, materialized.StarTransform, ship,
                ExtractionState.UniverseSeed, materialized.FullResources);

            SystemDelta delta = StarSystemRegistry.Deltas.TryGet(manifest.SystemId);
            if (delta == null) return;

            CombatEncounter.Encounter.RestoreFromSave(delta.combatEncounterOpen, delta.combatEncounterSinceLastThreat);

            Vector3 offset = FloatingOrigin.TotalOffset;
            foreach (EnemyDto dto in delta.enemies)
            {
                EnemyDef def = EnemyCatalog.Get(dto.defId);
                if (def == null) continue;
                Vector3 local = new Vector3(dto.px, dto.py, dto.pz) - offset;
                EnemyAgent agent = CombatEncounter.RestoreEnemy(dto.enemyId, dto.defId, local);
                if (agent == null) continue;
                agent.transform.rotation = new Quaternion(dto.rx, dto.ry, dto.rz, dto.rw);
                agent.RestoreFromSave((EnemyAiPhase)dto.aiPhase, dto.targetId, dto.scanElapsed, dto.continuousEscapeSeconds,
                    dto.shield, dto.armor, dto.hull, dto.regenTimer);
                // VS-014 §17: Looter cargo / disrupt-immunity / telegraph -- StructureTarget itself
                // is never restored from dto.structureTargetId (deliberately): Heavy/Siege's own
                // RefreshStructureTarget already re-derives the correct live target within one
                // Update() tick, exactly as if it had just lost and reacquired it mid-session.
                if (dto.looterCargo.Count > 0)
                {
                    var cargo = new List<(string, float)>();
                    foreach (CargoItemDto c in dto.looterCargo) cargo.Add((c.itemId, c.amount));
                    agent.RestoreLooterCargo(cargo);
                }
                agent.RestoreDisruptState(dto.disruptRemaining, dto.disruptImmunityRemaining);
                agent.RestoreTelegraph(dto.telegraphRemaining);
            }

            foreach (ProjectileDto dto in delta.projectiles)
            {
                Vector3 localPos = new Vector3(dto.px, dto.py, dto.pz) - offset;
                Vector3 velocity = new Vector3(dto.vx, dto.vy, dto.vz);
                ProjectileObject.Spawn((ProjectileKind)dto.kind, dto.tier, dto.ownerId, localPos, velocity,
                    dto.remainingLifetimeSeconds, dto.lockedTargetId, CombatEncounter,
                    (ProjectileFaction)dto.faction, dto.interceptable, dto.interceptionDurabilityRemaining, dto.damageAmount);
            }

            // VS-016A.1 §1: any pending attack still unresolved (time not yet expired, never
            // applied/intercepted remotely) is reconstructed as a real, live in-flight
            // ProjectileObject with the same attack ID, remaining time, damage and
            // interception durability -- restored AFTER enemies above so a Player-faction
            // attack's real EnemyAgent target is already live to aim at. An already-applied
            // entry is a stale leftover (should never actually be present -- both the tick
            // and the save writeback drop it) and is skipped defensively rather than ever
            // re-materialized.
            foreach (PendingAttackDto dto in delta.pendingAttacks)
            {
                if (dto == null || dto.applied) continue;
                RematerializePendingAttack(dto);
            }

            if (delta.threatPressure != null)
            {
                ThreatPressureDto p = delta.threatPressure;
                ulong.TryParse(p.encounterSeed, out ulong seed);
                CombatEncounter.Pressure.RestoreFromSave(p.pressure, p.graceRemainingActiveMinutes, p.foreignPresenceActive, p.foreignPresenceActiveMinutesElapsed,
                    (RaidPhase)p.phase, p.stage >= 0 ? (RaidStage)p.stage : (RaidStage?)null, p.doctrine >= 0 ? (RaidDoctrine)p.doctrine : (RaidDoctrine?)null,
                    p.targetPlanetWorldId, p.compositionDefIds, seed, p.warningRemainingSeconds, p.elapsedActiveSeconds,
                    p.playerDealtMeaningfulDamage, p.playerIssuedValidFocusCommand, p.playerActiveDefenseParticipation, p.playerWasShutdownDuringRaid,
                    p.anyResourceStolen, p.resultApplied, p.salvageApplied, p.pendingProbeEffectConsumedThisRaid,
                    p.firstControlledGraceGranted, p.spawnedEnemyIds, p.wasTargetContestedDuringRaid);
            }

            foreach (DefenseCoordinationDto dto in delta.defenseCoordination)
                CombatEncounter.DefenseCoordinator.Restore(dto.worldId, (DefenseMode)dto.mode, dto.focusEnemyId);

            foreach (CombatWreckageDto dto in delta.combatWreckage)
            {
                var wreck = new CombatWreckageState(dto.wreckageId);
                var nodes = new List<(string, float, float)>();
                foreach (CombatWreckageNodeDto n in dto.nodes) nodes.Add((n.itemId, n.total, n.remaining));
                wreck.RestoreFromSave(dto.remainingActiveSeconds, nodes);
                CombatEncounter.Wreckage.Add(wreck);
            }

            delta.enemies.Clear(); delta.projectiles.Clear(); delta.combatWreckage.Clear(); delta.pendingAttacks.Clear();
        }

        /// <summary>
        /// VS-016A.1 §1: rebuilds one carried-over attack into a live ProjectileObject.
        /// A PendingAttackDto deliberately carries no position (see PendingAttack's own
        /// doc comment), so it's reconstructed here: whichever of attacker/target can be
        /// resolved to a real live position (the ship is always live; an enemy was just
        /// restored above in this same call) anchors the spawn, with the shared catalog's
        /// own speed constant for that Kind driving velocity -- exactly the constants the
        /// live spawn sites themselves use, never a re-derived number. When neither side
        /// is resolvable the shot still spawns (at the ship, heading forward) rather than
        /// silently vanishing -- its own remaining lifetime is what then governs it.
        /// </summary>
        private void RematerializePendingAttack(PendingAttackDto dto)
        {
            var kind = (ProjectileKind)dto.kind;
            var faction = (ProjectileFaction)dto.faction;
            float speed = kind switch
            {
                ProjectileKind.Em => WeaponCatalog.EmProjectileSpeed,
                ProjectileKind.EnemySiege => ProjectileCatalog.SiegeProjectileSpeed,
                _ => WeaponCatalog.PlasmaProjectileSpeed // Plasma / EnemyPlasma
            };
            float remaining = Mathf.Max(0.05f, dto.remainingFlightSeconds);

            Vector3? attackerPos = ResolveLiveAttackPosition(dto.attackerId);
            Vector3? targetPos = ResolveLiveAttackPosition(dto.targetId);

            Vector3 spawnPos;
            Vector3 direction;
            if (attackerPos.HasValue)
            {
                spawnPos = attackerPos.Value;
                direction = targetPos.HasValue && (targetPos.Value - spawnPos).sqrMagnitude > 0.01f
                    ? (targetPos.Value - spawnPos).normalized : Vector3.forward;
            }
            else if (targetPos.HasValue)
            {
                direction = Vector3.forward;
                spawnPos = targetPos.Value - direction * speed * remaining;
            }
            else
            {
                spawnPos = ship != null ? ship.transform.position : Vector3.zero;
                direction = Vector3.forward;
            }

            ProjectileObject.Spawn(kind, 1, dto.attackerId, spawnPos, direction * speed, remaining,
                string.IsNullOrEmpty(dto.targetId) ? null : dto.targetId, CombatEncounter,
                faction, dto.interceptable, dto.interceptionDurabilityRemaining, dto.damageAmount);
        }

        /// <summary>
        /// VS-017A §1: the live-resolvable IDs a PendingAttackDto's/ProjectileState's attacker/target
        /// can carry: the ship (special id "ship"), a live EnemyAgent, or a live SurfaceStructure (by
        /// its own TargetId = worldId/nodeId, e.g. what a Siege shot's intended structure target uses).
        /// Escorts still have no queryable-by-id live position (only a DTO-side one, meaningless while
        /// this system is Full) -- those resolve to null, same as before this slice.
        /// </summary>
        private Vector3? ResolveLiveAttackPosition(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id == "ship") return ship != null ? ship.transform.position : (Vector3?)null;
            EnemyAgent agent = EnemyAgent.Find(id);
            if (agent != null) return agent.transform.position;
            foreach (SurfaceStructure s in FindObjectsOfType<SurfaceStructure>()) if (s.TargetId == id) return s.transform.position;
            return null;
        }

        private static Poi.PoiState FindSavedPoiState(SystemDelta delta, string poiId)
        {
            if (delta == null) return null;
            foreach (PoiDto dto in delta.pois)
            {
                if (dto.poiId != poiId) continue;
                var state = new Poi.PoiState(poiId);
                state.RestoreCore((Poi.PoiKnowledgeLevel)dto.level, dto.locateElapsed, dto.identifyElapsed, dto.operationElapsed,
                    (Poi.PoiPauseReason)dto.pauseReason, dto.assignedVehicleId);
                state.RestoreAnomaly(dto.anomalyXpAwarded);
                state.RestoreWreck(dto.wreckXpAwarded);
                state.RestoreFacility((Poi.FacilityChoice)dto.facilityChoice, dto.facilityOutputBasicRemaining, dto.facilityOutputSpecialRemaining,
                    dto.facilityOutputSpecialItemId, dto.facilityXpAwarded, dto.facilitySignalApplied);
                var nodes = new List<Poi.SalvageNode>();
                foreach (PoiSalvageNodeDto n in dto.salvageNodes) { var node = new Poi.SalvageNode(n.itemId, n.total); node.Remaining = n.remaining; nodes.Add(node); }
                state.SetSalvageNodes(nodes);
                return state;
            }
            return null;
        }
    }
}
