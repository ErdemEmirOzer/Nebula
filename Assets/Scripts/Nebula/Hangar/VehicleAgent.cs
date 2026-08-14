using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Simulation;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Hangar
{
    /// <summary>
    /// MonoBehaviour adapter for one produced vehicle (mirrors the
    /// DrillRuntime/DrillStructure split): <see cref="VehicleMissionState"/>
    /// owns the pure phase logic, this class owns everything physical —
    /// position, movement, collision avoidance, on-station work timers and
    /// the collector's cargo hold. Never teleports: every transform update is
    /// a bounded step of Speed*dt (or a same-frame continuous lerp during the
    /// short Launching/Docking placeholder animations), and every phase
    /// transition fires only once the physical distance is actually covered.
    /// VS-014 §6: also a real <see cref="ICombatTarget"/> — a loaded Collector
    /// is a legitimate hostile target. IsTargetable requires the vehicle to be
    /// physically out in the field (never the parked-in-hangar/inactive state).
    /// </summary>
    public class VehicleAgent : MonoBehaviour, ICombatTarget
    {
        private const float AvoidanceMargin = 40f;
        private const float CollectorApproachRange = 300f;
        private const float EngineerApproachRange = 250f;

        /// <summary>VS-015: id-keyed registry of every physically-active vehicle in the currently materialized system, so an Engineer/Escort can find another vehicle (e.g. a Disabled one to recover, or its protected target) without FindObjectOfType or a second locator.</summary>
        public static readonly Dictionary<string, VehicleAgent> Registry = new Dictionary<string, VehicleAgent>();

        public string VehicleId { get; private set; }
        public VehicleDef Def { get; private set; }
        public VehicleMissionState Mission { get; } = new VehicleMissionState();
        public string HomeSystemId { get; private set; }

        /// <summary>VS-015 §3: true once a combat hit disables this vehicle in the field — it freezes in place (never an auto-Recall teleport-equivalent) until a friendly Engineer physically recovers it.</summary>
        public bool Stranded { get; private set; }
        public bool IsRecoverable => gameObject.activeSelf && durability != null && durability.Disabled && Stranded;

        /// <summary>VS-015 §3: non-null while this vehicle (an Engineer) is running a maintenance mission — mutually exclusive with Mission/Route/PoiTask/EscortState.</summary>
        public EngineerMissionState EngineerTask { get; private set; }
        /// <summary>VS-015 §4: non-null while this vehicle (an Escort) is assigned to protect a target — mutually exclusive with Mission/Route/PoiTask/EngineerTask.</summary>
        public EscortState EscortState { get; private set; }
        /// <summary>VS-015.1 §4: non-null while this vehicle (a Collector Mk II) is running a physical Maintenance Resupply run — mutually exclusive with every other task type.</summary>
        public MaintenanceResupplyMission ResupplyTask { get; private set; }

        /// <summary>VS-014 §6: the real single K/Z/G authority backing Shield/Armor/Hull below — never a second Durability copy. Starts at 100% of Def's max, exactly like the old raw-float assignment did.</summary>
        private Durability durability;
        public float Shield => durability?.Shield ?? 0f;
        public float Armor => durability?.Armor ?? 0f;
        public float Hull => durability?.Hull ?? 0f;
        /// <summary>VS-015 §3: read access to the real Durability instance for EngineerFieldWork's repair/recovery ticking — never a second copy.</summary>
        public Durability DurabilityRef => durability;

        // --- ICombatTarget (VS-014 §6) ---------------------------------------
        public string TargetId => VehicleId;
        public TargetCategory Category => TargetCategory.Vehicle;
        public Vector3 AbsolutePosition => transform.position + FloatingOrigin.TotalOffset;
        /// <summary>Only a physically-active, not-yet-destroyed vehicle is a legitimate target — never a parked-in-hangar or already-disabled one.</summary>
        public bool IsTargetable => gameObject.activeSelf && durability != null && !durability.Disabled;

        /// <summary>
        /// VS-014 §6: a destructive hit forces an immediate, safe Recall —
        /// deliberately NOT a full destruction/wreck pipeline (no established
        /// convention exists yet for "vehicle destroyed by combat" state/save
        /// handling); this keeps a damaged vehicle from lingering as a
        /// perpetually-defeated live target while never inventing new loss
        /// mechanics beyond what the spec actually describes (only Looter
        /// cargo has a defined destruction consequence this slice).
        /// </summary>
        public void ApplyDamage(float rawAmount, DamageProfile profile)
        {
            if (durability == null) return;
            DamageResolver.Apply(durability, rawAmount, profile);
            if (durability.Disabled && !Stranded) Stranded = true; // VS-015 §3: freeze in place, awaiting physical Engineer recovery instead of an auto-Recall.
            if (EngineerTask != null) EngineerTask.RegisterThreatEvent();
        }

        /// <summary>VS-015 §3: applied by an Engineer's Recover work — restores the disabled vehicle to the recovery-complete hull fraction and lets it resume (heads home like a normal Recall).</summary>
        public void CompleteRecovery(float completeHullFraction)
        {
            if (!Stranded || durability == null) return;
            durability.RestoreFromSave(0f, durability.Armor, durability.HullMax * Mathf.Clamp01(completeHullFraction), regenTimer: 0f);
            Stranded = false;
            Recall();
        }

        // Collector-only cargo hold (a Scout never carries anything). Shared
        // between the Hangar single-fetch Mission path and a VS-011
        // persistent route — only one of the two ever drives a given vehicle
        // at a time (Route != null takes over Update() entirely).
        /// <summary>ANI-03.1 presentation read: how far the real 60-second Scout scan has run, 0..1. Never advances it.</summary>
        public float ScanProgress01 => Mathf.Clamp01(scoutScan.Elapsed / ScoutScanWork.RequiredSeconds);

        public string HeldItemId { get; internal set; }
        public float HeldAmount { get; internal set; }

        /// <summary>VS-011 §4: non-null while this vehicle is assigned to a persistent Collector route — Update() then drives route movement instead of the Hangar Mission state machine, which stays untouched (VS-007 fallback missions keep working exactly as before for every OTHER vehicle).</summary>
        public CollectorRoute Route { get; private set; }
        private const float CenterApproachRange = 400f;

        /// <summary>Fired the instant Docking completes (back InHangar) — ShipHangar releases the command slot.</summary>
        public event Action<VehicleAgent> DockedInHangar;

        private ShipController ship;
        private readonly ScoutScanWork scoutScan = new ScoutScanWork();
        private Vector3 launchAnimStart;
        private Vector3 dockAnimStart;
        private GameObject visualRoot;
        private GameObject cargoPodVisual;

        public static VehicleAgent Create(ShipController shipController, string vehicleId, VehicleDef def)
        {
            var go = new GameObject($"Vehicle ({vehicleId})");
            var agent = go.AddComponent<VehicleAgent>();
            agent.ship = shipController;
            agent.VehicleId = vehicleId;
            agent.Def = def;
            agent.durability = new Durability(def.Shield, def.Armor, def.Hull, startFraction: 1f);
            agent.durability.RestoreFromSave(def.Shield, def.Armor, def.Hull, regenTimer: 0f); // force full shield -- Durability's ctor always starts Shield at 0 (VS-013's EnemyAgent fix)
            agent.BuildVisuals();
            // LGT-03: presentation only — emission-driven class/state lighting.
            // Adds no light, and reads the agent without changing it.
            go.AddComponent<Nebula.Visuals.VehicleLightingPresenter>().Initialize(agent);
            // ANI-03: presentation only — poses the visual child from the phase
            // this agent is really in. Never moves the agent or its collider.
            go.AddComponent<Nebula.Visuals.VehicleAnimationPresenter>().Initialize(agent, null);
            // ANI-03.1: presentation only — pooled operation VFX and one
            // representative item per real transfer. Reads, never writes.
            go.AddComponent<Nebula.Visuals.SupportOperationPresenter>().Initialize(agent, null);
            // ANI-05.2: the mechanisms this class's model really carries —
            // sensor wings, cargo clamps, repair arms, the fixed lance.
            agent.AttachMechanicalPresenter();
            go.SetActive(false); // parked in the hangar until launched
            Registry[vehicleId] = agent;
            return agent;
        }

        private void OnDestroy()
        {
            if (VehicleId != null && Registry.TryGetValue(VehicleId, out VehicleAgent current) && current == this) Registry.Remove(VehicleId);
        }

        // --- Launch/dock entry points (called by ShipHangar) -----------------
        /// <summary>homeSystemId is fixed at launch time — whichever real system the ship (and the target) are physically in right now.</summary>
        public void Launch(MissionKind kind, string targetId, string homeSystemId)
        {
            if (!Mission.CanLaunch) return;
            HomeSystemId = homeSystemId;
            gameObject.SetActive(true);
            transform.position = ship.transform.position;
            launchAnimStart = transform.position;
            scoutScan.Reset();
            HeldItemId = null; HeldAmount = 0f;
            Mission.Launch(kind, targetId);
        }

        public void Recall() => Mission.RequestRecall();

        /// <summary>VS-011 §5 / VS-015 §1: Mk I-&gt;Mk II in-place upgrade — swaps the definition (stats/speed/cargo/transfer rate); K/Z/G PERCENTAGES are preserved across the swap (never a full-heal), vehicle id/GameObject/roster slot are untouched.</summary>
        public void UpgradeDefInPlace(VehicleDef newDef)
        {
            if (newDef == null) return;
            float shieldFraction = durability != null && durability.ShieldMax > 0f ? durability.Shield / durability.ShieldMax : 1f;
            float armorFraction = durability != null && durability.ArmorMax > 0f ? durability.Armor / durability.ArmorMax : 1f;
            float hullFraction = durability != null && durability.HullMax > 0f ? durability.Hull / durability.HullMax : 1f;
            Def = newDef;
            durability = new Durability(newDef.Shield, newDef.Armor, newDef.Hull, startFraction: 1f);
            durability.RestoreFromSave(newDef.Shield * shieldFraction, newDef.Armor * armorFraction, newDef.Hull * hullFraction, regenTimer: 0f);
            BuildVisuals();
            AttachMechanicalPresenter(); // the new Mk starts from its own authored baseline
        }

        /// <summary>
        /// Outside play mode <c>Destroy</c> is not deferred, it simply refuses:
        /// it logs an error and the object survives. That is not cosmetic here.
        /// The presenter sweep above exists so a Mk change replaces its mechanical
        /// presenter instead of stacking a second one on the same id, and the
        /// collider strips exist so an authored visual never carries physics --
        /// under a refused Destroy both silently do the opposite of what they say.
        /// An EditMode fixture that builds a vehicle and upgrades it in place hit
        /// exactly that, and the resulting error storm through the runner's log
        /// check took the whole unfiltered suite down with it.
        /// </summary>
        private static void DestroyNow(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        // --- VS-011 persistent route -------------------------------------------
        public void AssignRoute(CollectorRoute route)
        {
            Route = route;
            HomeSystemId = route.SystemId;
            gameObject.SetActive(true);
            HeldItemId = null; HeldAmount = 0f;
        }

        public void ClearRoute() => Route = null;

        /// <summary>VS-011 §4: route deletion — the vehicle keeps its Route reference (so it stays capturable/restorable mid-transit) and heads home physically; TickRouteReturning drops it from LogisticsState and clears Route only once actually docked.</summary>
        public void BeginReturningFromRoute() => Route?.SetPhase(RoutePhase.Returning);

        // --- VS-012 single-assignment POI task -----------------------------------
        /// <summary>Non-null while this vehicle is working a POI — mutually exclusive with Route and the Hangar Mission machinery (only one of the three ever drives Update()).</summary>
        public Poi.PoiTask PoiTask { get; private set; }

        public void AssignPoiTask(Poi.PoiTask task)
        {
            PoiTask = task;
            HomeSystemId = task.SystemId;
            gameObject.SetActive(true);
            HeldItemId = null; HeldAmount = 0f;
        }

        public void ClearPoiTask() => PoiTask = null;
        public void RequestPoiTaskRecall() => PoiTask?.RequestRecall();

        // --- VS-015 §3: Engineer maintenance task -------------------------------
        public void AssignEngineerTask(EngineerMissionState task)
        {
            EngineerTask = task;
            HomeSystemId = task.SystemId;
            gameObject.SetActive(true);
            task.BeginLoad();
            task.BeginLaunch();
            task.BeginTravel();
        }

        public void ClearEngineerTask() => EngineerTask = null;
        public void RequestEngineerCancel() { EngineerTask?.Cancel(); }

        // --- VS-015 §4: Escort protection assignment ----------------------------
        public void AssignEscort(EscortState state)
        {
            EscortState = state;
            gameObject.SetActive(true);
        }

        public void ClearEscort() => EscortState = null;

        // --- VS-015.1 §4: Collector Mk II Maintenance Resupply ------------------
        public void AssignResupplyTask(MaintenanceResupplyMission task, string heldItemId, float heldAmount)
        {
            ResupplyTask = task;
            HomeSystemId = task.SystemId;
            gameObject.SetActive(true);
            HeldItemId = heldItemId; HeldAmount = heldAmount;
            task.BeginTravel();
        }

        public void ClearResupplyTask() => ResupplyTask = null;
        public void RequestResupplyCancel() => ResupplyTask?.Cancel();

        /// <summary>Restore-only: HeldItemId/HeldAmount are already restored generically by RestorePhysical — this only reattaches the mission reference itself.</summary>
        public void RestoreResupplyTask(MaintenanceResupplyMission task)
        {
            ResupplyTask = task;
            gameObject.SetActive(true);
        }

        /// <summary>True while the owning <see cref="ShipController"/> reference is gone (destroyed at teardown, or being rebuilt). The agent is frozen, not finished.</summary>
        public bool ShipMissing => ship == null;

        /// <summary>
        /// Re-attaches this agent to a live ship without creating a second agent —
        /// the roster entry, vehicle id, mission phase, cargo and durability are all
        /// the ones it already had, so a frozen vehicle simply resumes.
        /// Called by <see cref="ShipHangar"/>, the only object that knows the live ship.
        /// </summary>
        public void RebindShip(ShipController shipController)
        {
            if (shipController != null) ship = shipController;
        }

        private void Update()
        {
            // Same lifecycle guard EnemyAgent.Update already has (playerShip == null).
            // Every phase below eventually resolves ship.transform -- AnimateLaunch
            // twice, AnimateDock, TickTravelReturn, TryResolveTargetPosition -- so a
            // vehicle that outlived its ShipController threw MissingReferenceException
            // every frame. It is placed here rather than in AnimateLaunch so that one
            // check covers every animate/tick path at once, and it FREEZES: no phase
            // timer advances, so a vehicle can never complete a mission (or dock, or
            // deliver) merely because the ship vanished. The agent is neither destroyed
            // nor deactivated, so a re-materialised ship (see RebindShip) finds the
            // same vehicle exactly where it left it.
            if (ship == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            UpdateCargoPodVisual();
            if (Stranded) return; // VS-015 §3: frozen in place awaiting Engineer recovery -- no movement, no phase progression.

            if (Route != null) { TickRouteMovement(dt); return; }
            if (PoiTask != null) { TickPoiTaskMovement(dt); return; }
            if (EngineerTask != null) { TickEngineerMovement(dt); return; }
            if (EscortState != null) { TickEscortMovement(dt); return; }
            if (ResupplyTask != null) { TickResupplyMovement(dt); return; }

            switch (Mission.Phase)
            {
                case VehiclePhase.Launching:
                    AnimateLaunch();
                    if (Mission.TickTimedPhase(dt) && Mission.RecallRequested) Mission.BeginReturn();
                    break;
                case VehiclePhase.TravelOut:
                    TickTravelOut(dt);
                    break;
                case VehiclePhase.OnStation:
                    TickOnStation(dt);
                    break;
                case VehiclePhase.TravelReturn:
                    TickTravelReturn(dt);
                    break;
                case VehiclePhase.Waiting:
                    TickWaiting(dt);
                    break;
                case VehiclePhase.Docking:
                    TickDocking(dt);
                    break;
            }
        }

        // --- Travel out --------------------------------------------------------
        private void TickTravelOut(float dt)
        {
            if (!TryResolveTargetPosition(out Vector3 targetPos, out float stopDistance))
            {
                Mission.RequestRecall(); // target vanished (e.g. drill destroyed) — safe fallback, never strand the vehicle
                return;
            }

            MoveToward(targetPos, dt, stopDistance);
            if ((transform.position - targetPos).magnitude <= stopDistance) Mission.ArriveAtTarget();
        }

        // --- On station ----------------------------------------------------------
        private void TickOnStation(float dt)
        {
            if (Def.Class == VehicleClass.Scout)
            {
                if (!TryResolveTargetPosition(out Vector3 targetPos, out _)) { Mission.BeginReturn(); return; }
                bool inRange = (transform.position - targetPos).magnitude <= ScoutScanWork.Range;
                if (scoutScan.Tick(dt, inRange))
                {
                    PlanetIdentity planet = ExtractionState.FindPlanet(Mission.TargetId);
                    if (planet != null) ExtractionState.CompletePlanetScan(planet);
                    Mission.BeginReturn();
                }
                else if (!inRange)
                {
                    // Simple re-approach: keep closing distance while scanning is paused.
                    MoveToward(targetPos, dt, ScoutScanWork.Range * 0.6f);
                }
            }
            else // Collector
            {
                DrillStructure drill = FindMissionDrill();
                if (drill == null) { Mission.BeginReturn(); return; }

                string heldItem = HeldItemId; float heldAmount = HeldAmount;
                ResourceTransferService.CollectFromDrill(drill.Runtime, ref heldItem, ref heldAmount, Def.CargoCapacity, dt);
                HeldItemId = heldItem; HeldAmount = heldAmount;

                bool full = HeldAmount >= Def.CargoCapacity - 0.0001f;
                if (full) Mission.BeginReturn();
                // Drill empty and nothing held yet: stays OnStation (shown as
                // "waiting on the drill" by the HUD/panel) — no auto-return,
                // no auto-repeat; the player recalls it if they want it back.
            }
        }

        // --- Travel return -------------------------------------------------------
        private void TickTravelReturn(float dt)
        {
            bool shipHome = ShipInHomeSystem();

            if (Def.Class == VehicleClass.Collector && !shipHome)
            {
                // Ship left the system: physically return the cargo to its drill instead of chasing the ship.
                if (TryResolveTargetPosition(out Vector3 drillPos, out float stopDistance))
                {
                    MoveToward(drillPos, dt, stopDistance);
                    if ((transform.position - drillPos).magnitude <= stopDistance) Mission.ArriveHome(false); // -> Waiting
                }
                else Mission.ArriveHome(false);
                return;
            }

            if (!shipHome) { Mission.ArriveHome(false); return; } // Scout: hold position where it is, wait for the ship

            Vector3 shipPos = ship.transform.position;
            MoveToward(shipPos, dt, 30f);
            if ((transform.position - shipPos).magnitude <= 30f) Mission.ArriveHome(true);
        }

        // --- Waiting ---------------------------------------------------------------
        private void TickWaiting(float dt)
        {
            // A Collector sitting at its drill (ship absent) keeps returning
            // whatever cargo it still holds, capacity-permitting, the whole time.
            if (Def.Class == VehicleClass.Collector && HeldAmount > 0.0001f)
            {
                DrillStructure drill = FindMissionDrill();
                if (drill != null)
                {
                    string heldItem = HeldItemId; float heldAmount = HeldAmount;
                    ResourceTransferService.ReturnToDrill(drill.Runtime, ref heldItem, ref heldAmount, dt);
                    HeldItemId = heldItem; HeldAmount = heldAmount;
                }
            }

            if (ShipInHomeSystem()) Mission.ResumeFromWaiting();
        }

        // --- Docking / delivery -------------------------------------------------
        /// <summary>
        /// Docking's fixed 2s placeholder animation and its state transition
        /// are decoupled (VS-007 §5): a loaded Collector keeps delivering at
        /// the standard rate for as long as it takes — "KARGO DOLU — TESLİM
        /// BEKLİYOR" is simply this phase persisting past the animation while
        /// the ship's cargo has no room — and only frees its command slot
        /// once the hold is completely empty.
        /// </summary>
        private void TickDocking(float dt)
        {
            AnimateDock();
            Mission.AdvancePhaseTimer(dt);

            if (Def.Class == VehicleClass.Collector && HeldAmount > 0.0001f)
            {
                var logistics = ship != null ? ship.GetComponent<ShipLogistics>() : null;
                if (logistics != null) TickDeliverToShip(logistics.Cargo, dt);
            }

            bool cargoClear = Def.Class != VehicleClass.Collector || HeldAmount <= 0.0001f;
            if (!Mission.DockAnimationDone || !cargoClear) return;

            Mission.CompleteDocking();
            gameObject.SetActive(false);
            DockedInHangar?.Invoke(this);
        }

        /// <summary>Drains the collector's hold into the ship's cargo at the standard rate.</summary>
        public void TickDeliverToShip(LogisticsCargo shipCargo, float dt)
        {
            if (Def.Class != VehicleClass.Collector || HeldAmount <= 0.0001f) return;
            string heldItem = HeldItemId; float heldAmount = HeldAmount;
            ResourceTransferService.DeliverToShip(shipCargo, ref heldItem, ref heldAmount, dt);
            HeldItemId = heldItem; HeldAmount = heldAmount;
        }

        // --- VS-011 §4: persistent route physical movement --------------------------
        /// <summary>
        /// Drives ToSource/Loading/ToCenter/Returning physically (real
        /// distance, real speed, real collection rate). Queued/Unloading/
        /// Holding/Paused are stationary from this vehicle's point of view —
        /// <see cref="CollectorRouteDirector"/> centrally owns dock
        /// assignment and the center's aggregate transfer budget across every
        /// vehicle docked there, so this method never touches those phases.
        /// </summary>
        private void TickRouteMovement(float dt)
        {
            switch (Route.Phase)
            {
                case RoutePhase.ToSource: TickRouteToSource(dt); break;
                case RoutePhase.Loading: TickRouteLoading(dt); break;
                case RoutePhase.ToCenter: TickRouteToCenter(dt); break;
                case RoutePhase.Returning: TickRouteReturning(dt); break;
                // Paused / Queued / Unloading / Holding: stationary, no physical work here.
            }
        }

        private DrillStructure FindRouteDrill() => FindDrillByWorldId(Route.DrillWorldId);

        private static DrillStructure FindDrillByWorldId(string worldId)
        {
            var drills = ExtractionState.Drills;
            for (int i = 0; i < drills.Count; i++)
                if (drills[i] != null && drills[i].WorldId == worldId) return drills[i];
            return null;
        }

        /// <summary>VS-014 §12: a Contested or Lost source planet stops route pickup exactly like an evacuated one — drill production is already suspended via DominanceSystem, this is the separate route-side gate the brief calls out on top of that.</summary>
        private static bool IsSourceContestedOrLost(string worldId)
        {
            DominanceState state = ExtractionState.GetDominance(worldId).State;
            return state == DominanceState.Contested || state == DominanceState.Lost;
        }

        private void TickRouteToSource(float dt)
        {
            DrillStructure drill = FindRouteDrill();
            if (drill == null) { Route.SetWaitReason(RouteWaitReason.SourceDrillMissing); return; }
            if (ExtractionState.IsPlanetAbandoned(drill.WorldId)) { Route.SetWaitReason(RouteWaitReason.SourceDrillEvacuated); return; }
            if (IsSourceContestedOrLost(drill.WorldId)) { Route.SetWaitReason(RouteWaitReason.SourceDrillContested); return; }
            if (drill.Runtime.State != DrillLifecycle.Active) { Route.SetWaitReason(RouteWaitReason.SourceDrillInactive); return; }

            Route.SetWaitReason(RouteWaitReason.None);
            Vector3 targetPos = drill.transform.position;
            MoveToward(targetPos, dt, CollectorApproachRange);
            if ((transform.position - targetPos).magnitude <= CollectorApproachRange) Route.SetPhase(RoutePhase.Loading);
        }

        private void TickRouteLoading(float dt)
        {
            DrillStructure drill = FindRouteDrill();
            if (drill == null) { Route.SetPhase(RoutePhase.ToSource); return; }
            if (ExtractionState.IsPlanetAbandoned(drill.WorldId) || IsSourceContestedOrLost(drill.WorldId) || drill.Runtime.State != DrillLifecycle.Active)
            {
                // Source went bad mid-load: hold position with whatever is already collected and report why.
                RouteWaitReason reason = IsSourceContestedOrLost(drill.WorldId) ? RouteWaitReason.SourceDrillContested
                    : drill.Runtime.State != DrillLifecycle.Active ? RouteWaitReason.SourceDrillInactive : RouteWaitReason.SourceDrillEvacuated;
                Route.SetWaitReason(reason);
                return;
            }
            Route.SetWaitReason(RouteWaitReason.None);

            string heldItem = HeldItemId; float heldAmount = HeldAmount;
            ResourceTransferService.CollectFromDrill(drill.Runtime, ref heldItem, ref heldAmount, Def.CargoCapacity, dt, Def.TransferVolumePerSecond);
            HeldItemId = heldItem; HeldAmount = heldAmount;
            Route.SetHeld(HeldItemId, HeldAmount);

            if (HeldAmount >= Def.CargoCapacity - 0.0001f) Route.SetPhase(RoutePhase.ToCenter);
        }

        private void TickRouteToCenter(float dt)
        {
            LogisticsCenter center = LogisticsState.FindCenter(Route.SystemId);
            if (center == null) return; // frozen in place — a route can't outlive its center (deleted on dismantle)

            Vector3 targetPos = center.transform.position;
            MoveToward(targetPos, dt, CenterApproachRange);
            if ((transform.position - targetPos).magnitude <= CenterApproachRange)
                Route.EnterQueued(Time.time); // CollectorRouteDirector promotes to Unloading (or keeps Holding priority) every frame.
        }

        private void TickRouteReturning(float dt)
        {
            if (ship == null) { gameObject.SetActive(false); return; }
            if (!ShipInHomeSystem())
            {
                // Ship left the system: wait right here (matches the Mission-based Waiting behavior) — never chases across systems.
                return;
            }
            Vector3 shipPos = ship.transform.position;
            MoveToward(shipPos, dt, 30f);
            if ((transform.position - shipPos).magnitude > 30f) return;

            if (HeldAmount > 0.0001f)
            {
                string heldItem = HeldItemId; float heldAmount = HeldAmount;
                ResourceTransferService.DeliverToShip(ship.GetComponent<ShipLogistics>()?.Cargo, ref heldItem, ref heldAmount, dt, Def.TransferVolumePerSecond);
                HeldItemId = heldItem; HeldAmount = heldAmount;
                if (HeldAmount > 0.0001f) return; // keep delivering before docking, same discipline as the Mission Docking phase
            }

            LogisticsState.RemoveRoute(Route); // only dropped from the registry once truly home — mid-transit it stays capturable/restorable
            ClearRoute();
            Mission.Restore(VehiclePhase.InHangar, MissionKind.None, null, 0f, false);
            gameObject.SetActive(false);
            DockedInHangar?.Invoke(this); // releases the Hangar command slot, same as a normal dock
        }

        // --- VS-012 POI task physical movement --------------------------------------
        // Purely physical: fly to the POI, hold position while "Working", fly home when
        // done. ALL actual progression (locate/identify/operation timers, XP, signals,
        // Collector salvage withdrawal) is owned centrally by Poi.PoiObject.Update() —
        // this class only ever reads PoiState.Level to know when to head home, never
        // calls into any POI service directly (mirrors the CollectorRouteDirector split).
        private const float PoiApproachRange = 500f;

        private void TickPoiTaskMovement(float dt)
        {
            switch (PoiTask.Phase)
            {
                case Poi.PoiTaskPhase.ToTarget: TickPoiToTarget(dt); break;
                case Poi.PoiTaskPhase.Working: TickPoiWorking(dt); break;
                case Poi.PoiTaskPhase.Returning: TickPoiReturning(dt); break;
            }
        }

        private void TickPoiToTarget(float dt)
        {
            Poi.PoiObject target = Poi.PoiObject.Find(PoiTask.PoiId);
            if (target == null) { PoiTask.RequestRecall(); return; }
            Vector3 targetPos = target.transform.position;
            MoveToward(targetPos, dt, PoiApproachRange);
            if ((transform.position - targetPos).magnitude <= PoiApproachRange) PoiTask.ArriveAtTarget();
        }

        private void TickPoiWorking(float dt)
        {
            Poi.PoiObject target = Poi.PoiObject.Find(PoiTask.PoiId);
            Poi.PoiState state = Poi.PoiLiveState.Find(PoiTask.PoiId);
            if (target == null || state == null) { PoiTask.BeginReturn(); return; }

            // Keep station: re-approach if pushed out of range, otherwise hold.
            float distance = (transform.position - target.transform.position).magnitude;
            if (distance > PoiApproachRange) MoveToward(target.transform.position, dt, PoiApproachRange * 0.6f);

            bool doneOrGivenUp = state.Level == Poi.PoiKnowledgeLevel.Resolved
                || (Def.Class == VehicleClass.Collector && HeldAmount >= Def.CargoCapacity - 0.0001f);
            if (doneOrGivenUp) PoiTask.BeginReturn();
        }

        private void TickPoiReturning(float dt)
        {
            if (ship == null) { gameObject.SetActive(false); return; }
            if (!ShipInHomeSystem()) return; // wait in place — never chases across systems, same discipline as Route/Mission Returning.

            Vector3 shipPos = ship.transform.position;
            MoveToward(shipPos, dt, 30f);
            if ((transform.position - shipPos).magnitude > 30f) return;

            if (HeldAmount > 0.0001f)
            {
                string heldItem = HeldItemId; float heldAmount = HeldAmount;
                ResourceTransferService.DeliverToShip(ship.GetComponent<ShipLogistics>()?.Cargo, ref heldItem, ref heldAmount, dt, Def.TransferVolumePerSecond);
                HeldItemId = heldItem; HeldAmount = heldAmount;
                if (HeldAmount > 0.0001f) return;
            }

            ClearPoiTask();
            Mission.Restore(VehiclePhase.InHangar, MissionKind.None, null, 0f, false);
            gameObject.SetActive(false);
            DockedInHangar?.Invoke(this);
        }

        // --- VS-015 §3: Engineer maintenance mission physical movement --------------
        private bool CurrentSystemInEncounter()
        {
            var director = StarSystemRegistry.Streaming;
            return director != null && director.CombatEncounter != null && director.CombatEncounter.Encounter != null && director.CombatEncounter.Encounter.IsOpen;
        }

        private void TickEngineerMovement(float dt)
        {
            switch (EngineerTask.Phase)
            {
                case EngineerPhase.Travel: TickEngineerTravel(dt); break;
                case EngineerPhase.WaitForSafety: TickEngineerSafety(dt); break;
                case EngineerPhase.Working: TickEngineerWorking(dt); break;
                case EngineerPhase.ReturnUnusedCargo: EngineerTask.BeginTravelHome(); break;
                case EngineerPhase.TravelHome: TickEngineerTravelHome(dt); break;
                case EngineerPhase.Dock: TickEngineerDock(dt); break;
            }
        }

        private void TickEngineerTravel(float dt)
        {
            if (!TryResolveEngineerTargetPosition(out Vector3 targetPos, out float stop)) { EngineerTask.Cancel(); ReleaseUnusedEngineerReservation(); ClearMaintenanceReserveActiveTarget(); gameObject.SetActive(false); return; }
            float speedMultiplier = CurrentSystemInEncounter() ? 0.5f : 1f;
            if ((targetPos - transform.position).magnitude <= stop) { EngineerTask.ArriveAtTarget(); return; }
            MoveTowardOnPlanet(EngineerTargetPlanetFrame(), targetPos, dt, stop, speedMultiplier);
            if ((targetPos - transform.position).magnitude <= stop) EngineerTask.ArriveAtTarget();
        }

        private void TickEngineerSafety(float dt)
        {
            HoldStationOnEngineerTarget(dt);
            EngineerTask.TickSafety(dt, !CurrentSystemInEncounter());
        }

        /// <summary>
        /// Eight seconds of safety and the field work itself both take real time,
        /// and the planet does not wait through them — without this the Engineer
        /// would be left kilometres behind the structure it is repairing.
        /// </summary>
        private void HoldStationOnEngineerTarget(float dt)
        {
            Transform frame = EngineerTargetPlanetFrame();
            if (frame == null) return;
            if (!TryResolveEngineerTargetPosition(out Vector3 targetPos, out float stop)) return;
            MoveTowardOnPlanet(frame, targetPos, dt, stop);
        }

        private void TickEngineerWorking(float dt)
        {
            HoldStationOnEngineerTarget(dt);
            if (CurrentSystemInEncounter()) { EngineerTask.RegisterThreatEvent(); return; }

            LogisticsCargo source = ResolveEngineerSourceCargo();
            string ownerId = EngineerReservationOwnerId();
            bool done;
            switch (EngineerTask.WorkKind)
            {
                case EngineerWorkKind.Recover:
                    var target = VehicleAgent.Registry.TryGetValue(EngineerTask.TargetId, out VehicleAgent v) ? v : null;
                    done = Combat.EngineerFieldWork.TickVehicleRecovery(target, EngineerTask, dt, source, ownerId);
                    break;
                case EngineerWorkKind.Repair when EngineerTask.TargetKind == EngineerTargetKind.Ship:
                    var shipCombat = ship != null ? ship.GetComponent<ShipCombat>() : null;
                    if (shipCombat == null)
                    {
                        EngineerTask.Cancel();
                        ReleaseUnusedEngineerReservation();
                        ClearMaintenanceReserveActiveTarget();
                        gameObject.SetActive(false);
                        return;
                    }
                    if (shipCombat.Repair.Active)
                    {
                        EngineerTask.MarkWorkAccepted();
                        done = false;
                        break;
                    }
                    if (EngineerTask.WorkAccepted)
                    {
                        done = true;
                        break;
                    }
                    if (!shipCombat.TryStartRepair())
                    {
                        // A rejected first attempt is a failed mission, never a
                        // zero-HP “completion” that travels home and awards XP.
                        EngineerTask.Cancel();
                        ReleaseUnusedEngineerReservation();
                        ClearMaintenanceReserveActiveTarget();
                        gameObject.SetActive(false);
                        return;
                    }
                    EngineerTask.MarkWorkAccepted();
                    done = false;
                    break;
                case EngineerWorkKind.Repair when EngineerTask.TargetKind == EngineerTargetKind.Vehicle:
                    var repairVehicle = VehicleAgent.Registry.TryGetValue(EngineerTask.TargetId, out VehicleAgent rv) ? rv : null;
                    float vehicleCap = InCombatNow() ? Combat.EngineerRepairCatalog.VehicleCombatCapFraction : Combat.EngineerRepairCatalog.VehicleNonCombatCapFraction;
                    done = Combat.EngineerFieldWork.TickVehicleRepair(repairVehicle, EngineerTask, Def.RepairRatePerSecond, vehicleCap, source, ownerId, dt);
                    break;
                default:
                    Durability targetDurability = ResolveStructureOrDrillDurability();
                    float multiplier = ResolveMaintenancePlatformRepairMultiplier();
                    done = Combat.EngineerFieldWork.TickStructureRepair(targetDurability, EngineerTask, Def.RepairRatePerSecond * multiplier,
                        Combat.EngineerRepairCatalog.StructureFieldCapFraction, source, ownerId, dt);
                    break;
            }
            if (done) EngineerTask.CompleteWork();
        }

        private bool InCombatNow() => CurrentSystemInEncounter();

        private void TickEngineerTravelHome(float dt)
        {
            if (ship == null) { gameObject.SetActive(false); return; }
            if (!ShipInHomeSystem()) return;
            Vector3 shipPos = ship.transform.position;
            MoveToward(shipPos, dt, 30f);
            if ((transform.position - shipPos).magnitude <= 30f) EngineerTask.BeginDock();
        }

        private void TickEngineerDock(float dt)
        {
            ReleaseUnusedEngineerReservation();
            // VS-015 §8: manual-only, once per mission id -- the ledger inside Award already de-dupes across save/load/re-dock.
            if (EngineerTask.PlayerManuallyStarted && !EngineerTask.XpAwarded)
            {
                Progression.ProgressionService.Award(Progression.ProgressionEvents.EngineerMission(EngineerTask.MissionId), Progression.ProgressionEvents.EngineerMissionXp);
                EngineerTask.MarkXpAwarded();
            }
            ClearMaintenanceReserveActiveTarget();
            EngineerTask.CompleteMission();
            ClearEngineerTask();
            gameObject.SetActive(false);
            DockedInHangar?.Invoke(this);
        }

        private void ClearMaintenanceReserveActiveTarget()
        {
            if (EngineerTask == null || EngineerTask.SourceKind != EngineerSourceKind.PlanetMaintenanceReserve) return;
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.Type == SurfaceStructureType.Command && s.MaintenanceReserve != null) s.MaintenanceReserve.ClearActiveTarget(EngineerTask.TargetId);
        }

        private void ReleaseUnusedEngineerReservation()
        {
            LogisticsCargo source = ResolveEngineerSourceCargo();
            source?.ConsumeReservationForRefund(EngineerReservationOwnerId(), 1f); // full return of whatever's unused — not a cancel penalty
        }

        private string EngineerReservationOwnerId() => $"engineer-mission:{EngineerTask.MissionId}";

        private LogisticsCargo ResolveEngineerSourceCargo()
        {
            if (EngineerTask.SourceKind == EngineerSourceKind.ShipCargo)
                return ship != null ? ship.GetComponent<ShipLogistics>()?.Cargo : null;
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.Type == SurfaceStructureType.Command && s.MaintenanceReserve != null
                    && WorldIdCodec.SystemIdFromPlanetWorldId(s.WorldId) == EngineerTask.SystemId)
                    return s.MaintenanceReserve.Cargo;
            return null;
        }

        /// <summary>Drills have no Durability/K-Z-G model in this project (only a Damaged/8s-pause lifecycle) — a Drill-target Engineer mission safely no-ops (nothing planned, nothing reserved, nothing consumed).</summary>
        private Durability ResolveStructureOrDrillDurability()
        {
            if (EngineerTask.TargetKind == EngineerTargetKind.Drill) return null;
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.TargetId == EngineerTask.TargetId) return s.Runtime.Durability;
            return null;
        }

        private float ResolveMaintenancePlatformRepairMultiplier()
        {
            string worldId = null;
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.TargetId == EngineerTask.TargetId) { worldId = s.WorldId; break; }
            if (worldId == null && EngineerTask.TargetKind == EngineerTargetKind.Drill)
            {
                var drills = ExtractionState.Drills;
                for (int i = 0; i < drills.Count; i++)
                    if (drills[i] != null && drills[i].WorldId == EngineerTask.TargetId) { worldId = drills[i].WorldId; break; }
            }
            if (worldId == null) return 1f;
            foreach (SurfaceStructure s in ExtractionState.Structures)
            {
                if (s == null || s.WorldId != worldId || s.Type != SurfaceStructureType.MaintenancePlatform || !s.Runtime.IsActive) continue;
                StructureTierDef def = PlanetaryDefenseCatalog.Get(SurfaceStructureType.MaintenancePlatform, s.Runtime.Tier);
                if (def != null) return def.EngineerRepairMultiplier;
            }
            return 1f;
        }

        private bool TryResolveEngineerTargetPosition(out Vector3 position, out float stopDistance)
        {
            position = default; stopDistance = EngineerApproachRange;
            switch (EngineerTask.TargetKind)
            {
                case EngineerTargetKind.Ship:
                    if (ship == null) return false;
                    position = ship.transform.position; stopDistance = 80f; return true;
                case EngineerTargetKind.Drill:
                    var drills = ExtractionState.Drills;
                    for (int i = 0; i < drills.Count; i++)
                        if (drills[i] != null && drills[i].WorldId == EngineerTask.TargetId) { position = drills[i].transform.position; return true; }
                    return false;
                case EngineerTargetKind.Structure:
                case EngineerTargetKind.SupportStructure:
                    foreach (SurfaceStructure s in ExtractionState.Structures)
                        if (s != null && s.TargetId == EngineerTask.TargetId) { position = s.transform.position; return true; }
                    return false;
                case EngineerTargetKind.DisabledVehicle:
                case EngineerTargetKind.Vehicle:
                    if (Registry.TryGetValue(EngineerTask.TargetId, out VehicleAgent target) && target != null) { position = target.transform.position; return true; }
                    return false;
                default:
                    return false;
            }
        }

        // --- VS-015 §4: Escort protection physical movement -------------------------
        private void TickEscortMovement(float dt)
        {
            if (!TryResolveEscortAnchorPosition(out Vector3 anchorPos)) return;

            float totalFraction = TotalDurabilityFraction();
            bool mustRetreat = EscortState.Command == EscortCommand.Retreat
                || EscortCombatResolver.ShouldRetreat(totalFraction, EscortState.Stance, knownEnemyPowerInsufficient: false);
            if (mustRetreat && EscortState.SubState != EscortSubState.Retreating) EscortState.ForceRetreat();
            else if (!mustRetreat && EscortState.SubState == EscortSubState.Retreating) EscortState.EndRetreat();

            if (EscortState.SubState == EscortSubState.Retreating)
            {
                MoveToward(ship != null ? ship.transform.position : anchorPos, dt, 60f);
                return;
            }

            if (EscortState.SubState == EscortSubState.Following && EscortState.Command != EscortCommand.Retreat)
            {
                string acquired = AcquireEscortTarget(anchorPos);
                if (acquired != null) EscortState.EngageThreat(acquired);
            }

            Vector3 followTarget = anchorPos;
            bool wasEngaging = EscortState.SubState == EscortSubState.Engaging || EscortState.SubState == EscortSubState.Chasing;
            if (wasEngaging)
            {
                if (Combat.EnemyAgent.All.TryGetValue(EscortState.EngagedThreatId ?? "", out Combat.EnemyAgent threat) && threat != null && !threat.Destroyed)
                {
                    followTarget = threat.transform.position;
                    FireEscortLaser(threat, dt);
                }
                else
                {
                    // VS-015 §8: the engaged threat is gone -- a successful manual protection cleared it while the protected target survived.
                    if (EscortState.PlayerManuallyStarted && !EscortState.XpAwarded)
                    {
                        Progression.ProgressionService.Award(Progression.ProgressionEvents.EscortMission(EscortState.MissionId), Progression.ProgressionEvents.EscortMissionXp);
                        EscortState.MarkXpAwarded();
                    }
                    EscortState.ClearThreat();
                }
            }

            MoveToward(followTarget, dt, EscortState.SubState == EscortSubState.Following ? 150f : Def.FixedLaserRange * 0.8f);
        }

        private float TotalDurabilityFraction()
        {
            if (durability == null) return 1f;
            float max = durability.ShieldMax + durability.ArmorMax + durability.HullMax;
            if (max <= 0f) return 1f;
            return (durability.Shield + durability.Armor + durability.Hull) / max;
        }

        /// <summary>VS-015 §4: real target-priority scan within the Escort's own weapon range of the protected target — raider (Looter role) beats fighter beats the player's own current focus target.</summary>
        private string AcquireEscortTarget(Vector3 anchorPos)
        {
            string raiderId = null, fighterId = null;
            float raiderSqr = float.MaxValue, fighterSqr = float.MaxValue;
            float rangeSqr = Def.FixedLaserRange * Def.FixedLaserRange;
            foreach (Combat.EnemyAgent e in Combat.EnemyAgent.All.Values)
            {
                if (e == null || e.Destroyed) continue;
                float sqr = (e.transform.position - anchorPos).sqrMagnitude;
                if (sqr > rangeSqr) continue;
                if (e.Def != null && e.Def.Role == Combat.EnemyRole.Looter) { if (sqr < raiderSqr) { raiderSqr = sqr; raiderId = e.Id; } }
                else { if (sqr < fighterSqr) { fighterSqr = sqr; fighterId = e.Id; } }
            }
            string playerFocusId = ship != null ? ship.GetComponent<ShipCombat>()?.Target?.Id : null;
            return EscortCombatResolver.ResolveTarget(null, null, raiderId, fighterId, playerFocusId);
        }

        private void FireEscortLaser(Combat.EnemyAgent target, float dt)
        {
            if (Def.FixedLaserDps <= 0f || target == null) return;
            float distance = (target.transform.position - transform.position).magnitude;
            if (distance > Def.FixedLaserRange) return;
            target.ApplyDamage(Def.FixedLaserDps * dt, DamageProfile.Laser);

            // ANI-05.2.2: presentation only, and only here — a shot refused for
            // range above never reaches this line, so a rejected shot can never
            // produce a recoil or a muzzle flash.
            Nebula.Visuals.EscortFireSignal.Raise(VehicleId, transform.position, transform.forward);
        }

        private bool TryResolveEscortAnchorPosition(out Vector3 position)
        {
            position = default;
            if (EscortState.ProtectedTargetCategory == TargetCategory.Ship)
            {
                if (ship == null) return false;
                position = ship.transform.position; return true;
            }
            if (EscortState.ProtectedTargetCategory == TargetCategory.Vehicle)
            {
                if (Registry.TryGetValue(EscortState.ProtectedTargetId, out VehicleAgent v) && v != null) { position = v.transform.position; return true; }
                return false;
            }
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.TargetId == EscortState.ProtectedTargetId) { position = s.transform.position; return true; }
            return false;
        }

        // --- VS-015.1 §4: Collector Mk II Maintenance Resupply physical movement ----
        private const float ResupplyApproachRange = 250f;

        private void TickResupplyMovement(float dt)
        {
            switch (ResupplyTask.Phase)
            {
                case MaintenanceResupplyPhase.ToReserve: TickResupplyToReserve(dt); break;
                case MaintenanceResupplyPhase.Unloading: TickResupplyUnloading(dt); break;
                case MaintenanceResupplyPhase.Returning: TickResupplyReturning(dt); break;
            }
        }

        private SurfaceStructure FindResupplyTargetCommand()
        {
            foreach (SurfaceStructure s in ExtractionState.Structures)
                if (s != null && s.TargetId == ResupplyTask.TargetCommandStructureId && s.Type == SurfaceStructureType.Command) return s;
            return null;
        }

        private void TickResupplyToReserve(float dt)
        {
            SurfaceStructure target = FindResupplyTargetCommand();
            if (target == null || target.MaintenanceReserve == null) { ResupplyTask.Cancel(); return; }
            MoveTowardOnPlanet(PlanetFrameOf(target.WorldId), target.transform.position, dt, ResupplyApproachRange);
            if ((transform.position - target.transform.position).magnitude <= ResupplyApproachRange) ResupplyTask.BeginUnloading();
        }

        private void TickResupplyUnloading(float dt)
        {
            SurfaceStructure target = FindResupplyTargetCommand();
            if (target == null || target.MaintenanceReserve == null || HeldAmount <= 0.0001f) { ResupplyTask.BeginReturn(); return; }
            // Unloading is not instant, so the Collector rides the planet through it
            // rather than transferring cargo from a kilometre behind and climbing.
            MoveTowardOnPlanet(PlanetFrameOf(target.WorldId), target.transform.position, dt, ResupplyApproachRange);
            float rate = Def.TransferVolumePerSecond * dt;
            float moved = Mathf.Min(rate, HeldAmount);
            float accepted = target.MaintenanceReserve.Add(HeldItemId, moved);
            HeldAmount -= accepted;
            if (accepted <= 0.0001f || HeldAmount <= 0.0001f) ResupplyTask.BeginReturn();
        }

        private void TickResupplyReturning(float dt)
        {
            if (ship == null) { gameObject.SetActive(false); return; }
            if (!ShipInHomeSystem()) return;
            Vector3 shipPos = ship.transform.position;
            MoveToward(shipPos, dt, 30f);
            if ((transform.position - shipPos).magnitude > 30f) return;

            if (HeldAmount > 0.0001f)
            {
                string heldItem = HeldItemId; float heldAmount = HeldAmount;
                ResourceTransferService.DeliverToShip(ship.GetComponent<ShipLogistics>()?.Cargo, ref heldItem, ref heldAmount, dt, Def.TransferVolumePerSecond);
                HeldItemId = heldItem; HeldAmount = heldAmount;
                if (HeldAmount > 0.0001f) return;
            }

            ResupplyTask.Complete();
            ClearResupplyTask();
            Mission.Restore(VehiclePhase.InHangar, MissionKind.None, null, 0f, false);
            gameObject.SetActive(false);
            DockedInHangar?.Invoke(this);
        }

        // --- Helpers ---------------------------------------------------------------
        private bool ShipInHomeSystem()
        {
            if (ship == null) return false;
            Vector3 shipAbs = ship.transform.position + FloatingOrigin.TotalOffset;
            return StarSystemRegistry.TryFindContaining(shipAbs, out StarSystemRecord record)
                   && record.SystemId == HomeSystemId;
        }

        private bool TryResolveTargetPosition(out Vector3 position, out float stopDistance)
        {
            position = default; stopDistance = 0f;
            if (Def.Class == VehicleClass.Scout)
            {
                PlanetIdentity planet = ExtractionState.FindPlanet(Mission.TargetId);
                if (planet == null || planet.Body == null) return false;
                position = planet.Body.transform.position;
                stopDistance = ScoutScanWork.Range * 0.8f;
                return true;
            }
            DrillStructure drill = FindMissionDrill();
            if (drill == null) return false;
            position = drill.transform.position;
            stopDistance = CollectorApproachRange;
            return true;
        }

        private DrillStructure FindMissionDrill()
        {
            var drills = ExtractionState.Drills;
            for (int i = 0; i < drills.Count; i++)
                if (drills[i] != null && drills[i].WorldId == Mission.TargetId) return drills[i];
            return null;
        }

        private void MoveToward(Vector3 targetPosition, float dt, float stopDistance)
        {
            Vector3 toTarget = targetPosition - transform.position;
            float distance = toTarget.magnitude;
            if (distance <= stopDistance) return;

            Vector3 dir = toTarget / Mathf.Max(distance, 0.0001f);
            float step = Mathf.Min(Def.Speed * dt, distance - stopDistance);
            Vector3 end = AvoidCelestialBodies(transform.position, transform.position + dir * step);
            transform.position = end;
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
        }

        /// <summary>
        /// Approaches — and then keeps station on — something that is carried by a
        /// planet, working inside that planet's own frame instead of chasing its
        /// absolute position.
        /// <para>
        /// A surface structure does not sit still, and a planet's motion dwarfs any
        /// vehicle's. Measured on the start system's outer planet: a Command Center
        /// pulls away at roughly 625 u/s from a Collector Mk II whose
        /// <see cref="VehicleDef.Speed"/> is 154, so pursuing where it *is* is a
        /// chase that provably never converges. The gap grew every single frame —
        /// 452 units to 1,129 over thirty — the mission sat in ToReserve forever,
        /// and that is why a Maintenance Resupply delivered nothing and a field
        /// Engineer never reached a structure to repair it.
        /// </para>
        /// <para>
        /// Nothing tuned changes here. <c>Def.Speed</c> and the approach ranges keep
        /// meaning exactly what they were written to mean — closing speed and
        /// working distance — they are simply measured against the planet the target
        /// rides rather than against the star. Targets that fly under their own power
        /// (the ship, another vehicle, an enemy) still use <see cref="MoveToward"/>:
        /// they move at speeds a vehicle can match, so that pursuit does converge,
        /// and putting an Escort into someone else's frame would change how escorting
        /// reads.
        /// </para>
        /// <para>
        /// Position and rotation only, never <c>lossyScale</c>: a planet body bakes
        /// its radius into scale, so <c>InverseTransformPoint</c> would divide every
        /// distance by it and quietly rescale both the step and the approach range.
        /// </para>
        /// </summary>
        private Transform frameAnchor;
        private Vector3 frameLocalPosition;

        private void MoveTowardOnPlanet(Transform frame, Vector3 targetPosition, float dt,
            float stopDistance, float speedMultiplier = 1f)
        {
            if (frame == null) { frameAnchor = null; MoveToward(targetPosition, dt, stopDistance); return; }

            Quaternion inverse = Quaternion.Inverse(frame.rotation);

            // Carried from the previous tick while it is the same planet. Deriving it
            // from the current world position instead would be an identity — convert
            // out of the frame and straight back into it and nothing has happened —
            // and the entire point is that the planet turned in between.
            Vector3 localSelf = frameAnchor == frame
                ? frameLocalPosition
                : inverse * (transform.position - frame.position);
            Vector3 localTarget = inverse * (targetPosition - frame.position);

            Vector3 toTarget = localTarget - localSelf;
            float distance = toTarget.magnitude;
            if (distance > stopDistance)
            {
                Vector3 dir = toTarget / Mathf.Max(distance, 0.0001f);
                localSelf += dir * Mathf.Min(Def.Speed * speedMultiplier * dt, distance - stopDistance);
            }

            // Rebuilt from the frame even when already in range, so an arrived vehicle
            // rides along with the planet instead of being left behind by it.
            Vector3 end = AvoidCelestialBodies(transform.position, frame.position + frame.rotation * localSelf);
            Vector3 travelled = end - transform.position;
            transform.position = end;
            if (travelled.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(travelled);

            frameAnchor = frame;
            frameLocalPosition = inverse * (end - frame.position);
        }

        /// <summary>
        /// Docking, being recalled and being destroyed all deactivate the vehicle, and
        /// a carried planet-local position must not outlive the mission that earned it
        /// — the same planet on a later mission would otherwise teleport the vehicle
        /// back to where the previous one left off.
        /// </summary>
        private void OnDisable() => frameAnchor = null;

        /// <summary>The planet a surface target rides, or <c>null</c> if it is not on one.</summary>
        private static Transform PlanetFrameOf(string planetWorldId)
        {
            PlanetIdentity planet = ExtractionState.FindPlanet(planetWorldId);
            return planet != null && planet.Body != null ? planet.Body.transform : null;
        }

        /// <summary>Same for an Engineer mission's target, which is only planet-carried for the three surface kinds.</summary>
        private Transform EngineerTargetPlanetFrame()
        {
            switch (EngineerTask.TargetKind)
            {
                case EngineerTargetKind.Structure:
                case EngineerTargetKind.SupportStructure:
                    foreach (SurfaceStructure s in ExtractionState.Structures)
                        if (s != null && s.TargetId == EngineerTask.TargetId) return PlanetFrameOf(s.WorldId);
                    return null;
                case EngineerTargetKind.Drill:
                    var drills = ExtractionState.Drills;
                    for (int i = 0; i < drills.Count; i++)
                        if (drills[i] != null && drills[i].WorldId == EngineerTask.TargetId)
                            return PlanetFrameOf(drills[i].WorldId);
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>Same swept-segment sphere test as ShipController.MoveWithCollision — never lets a vehicle tunnel into a planet, even at a high dt.</summary>
        private static Vector3 AvoidCelestialBodies(Vector3 start, Vector3 end)
        {
            var bodies = GravitySystem.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] == null) continue;
                end = ResolveSphereCollision(start, end, bodies[i].transform.position, bodies[i].Radius + AvoidanceMargin);
            }
            return end;
        }

        /// <summary>
        /// Keeps a step out of a planet without also forbidding a step <em>along</em>
        /// one.
        /// <para>
        /// The old resolution snapped to the segment's own closest point, and every
        /// slide across a surface dips its chord a fraction of a unit below the
        /// keep-out radius — so a vehicle parked at grazing altitude had roughly half
        /// of every step thrown away, every frame. Measured: a Collector holding
        /// station on a Command Center stayed pinned at exactly
        /// <c>Radius + AvoidanceMargin</c> (2,616 of 2,576 + 40) while the planet
        /// turned underneath it, and the gap to the structure grew from 452 to 2,902
        /// units over a hundred frames without the mission ever leaving ToReserve.
        /// </para>
        /// <para>
        /// Now the destination is lifted back out along its own radius, which keeps
        /// whatever tangential progress the step made, and only a step that crosses
        /// to the far hemisphere — the tunnel this guard exists for — is stopped on
        /// the near side. <see cref="ShipController"/> keeps its own copy untouched:
        /// the player's collision feel is not this change's business.
        /// </para>
        /// </summary>
        private static Vector3 ResolveSphereCollision(Vector3 start, Vector3 end, Vector3 center, float minDistance)
        {
            Vector3 segment = end - start;
            float segLenSq = segment.sqrMagnitude;
            float t = segLenSq > 1e-8f ? Mathf.Clamp01(Vector3.Dot(center - start, segment) / segLenSq) : 0f;
            Vector3 closest = start + segment * t;
            Vector3 fromCenter = closest - center;
            if (fromCenter.sqrMagnitude >= minDistance * minDistance) return end;

            Vector3 fromStart = start - center;
            Vector3 fromEnd = end - center;

            // Opposite hemispheres: the step really would pass through the body, so
            // it stops where it entered rather than reappearing on the other side.
            if (Vector3.Dot(fromStart, fromEnd) < 0f)
            {
                Vector3 nearNormal = fromStart.sqrMagnitude > 1e-6f ? fromStart.normalized
                    : (fromCenter.sqrMagnitude > 1e-6f ? fromCenter.normalized : Vector3.up);
                return center + nearNormal * minDistance;
            }

            Vector3 outward = fromEnd.sqrMagnitude > 1e-6f ? fromEnd.normalized
                : (fromStart.sqrMagnitude > 1e-6f ? fromStart.normalized : Vector3.up);
            return center + outward * minDistance;
        }

        private void AnimateLaunch()
        {
            float t = Mathf.Clamp01(Mission.PhaseTimer / VehicleMissionState.LaunchSeconds);
            Vector3 outward = ship.transform.position + ship.transform.forward * 6f + ship.transform.right * (Def.Class == VehicleClass.Scout ? -3f : 3f);
            transform.position = Vector3.Lerp(launchAnimStart, outward, t);
        }

        private void AnimateDock()
        {
            if (Mission.PhaseTimer <= 0f) dockAnimStart = transform.position;
            float t = Mathf.Clamp01(Mission.PhaseTimer / VehicleMissionState.DockSeconds);
            transform.position = Vector3.Lerp(dockAnimStart, ship.transform.position, t);
        }

        // --- Save/load ---------------------------------------------------------
        /// <summary>VS-015 §10: restores real K/Z/G (falls back to full health if the save predates v13, i.e. all three are 0) and the Stranded flag.</summary>
        public void RestoreDurability(float shield, float armor, float hull, bool stranded)
        {
            if (durability == null) return;
            if (shield <= 0f && armor <= 0f && hull <= 0f) durability.RestoreFromSave(Def.Shield, Def.Armor, Def.Hull, 0f);
            else durability.RestoreFromSave(shield, armor, hull, 0f);
            Stranded = stranded;
        }

        /// <summary>VS-015 §10: restores an in-flight Engineer mission exactly as saved, without re-validating or re-reserving — the caller (SystemStreamingDirector) re-derives the reservation separately against the same source cargo.</summary>
        public void RestoreEngineerTask(EngineerMissionState task)
        {
            EngineerTask = task;
            gameObject.SetActive(true);
        }

        public void RestoreEscortState(EscortState state)
        {
            EscortState = state;
            gameObject.SetActive(true);
        }

        public void RestorePhysical(string homeSystemId, Vector3 absolutePosition, Quaternion rotation, Vector3 floatingOriginOffset,
            VehiclePhase phase, MissionKind kind, string targetId, float phaseTimer, bool recallRequested,
            string heldItemId, float heldAmount)
        {
            HomeSystemId = homeSystemId;
            transform.position = absolutePosition - floatingOriginOffset;
            transform.rotation = rotation;
            Mission.Restore(phase, kind, targetId, phaseTimer, recallRequested);
            HeldItemId = heldItemId; HeldAmount = heldAmount;
            gameObject.SetActive(phase != VehiclePhase.InHangar);
        }

        /// <summary>VS-011 §9: restore-only for a route-controlled vehicle — physically present and active regardless of phase (even Paused/Holding sit visibly parked), never re-validates the route.</summary>
        public void RestorePhysicalForRoute(CollectorRoute route, Vector3 absolutePosition, Vector3 floatingOriginOffset, string heldItemId, float heldAmount)
        {
            Route = route;
            HomeSystemId = route.SystemId;
            transform.position = absolutePosition - floatingOriginOffset;
            HeldItemId = heldItemId; HeldAmount = heldAmount;
            gameObject.SetActive(true);
        }

        /// <summary>VS-012 §10: restore-only for a POI-task-controlled vehicle — same discipline as RestorePhysicalForRoute.</summary>
        public void RestorePhysicalForPoiTask(Poi.PoiTask task, Vector3 absolutePosition, Vector3 floatingOriginOffset, string heldItemId, float heldAmount)
        {
            PoiTask = task;
            HomeSystemId = task.SystemId;
            transform.position = absolutePosition - floatingOriginOffset;
            HeldItemId = heldItemId; HeldAmount = heldAmount;
            gameObject.SetActive(true);
        }

        // --- Visuals ---------------------------------------------------------------

        /// <summary>
        /// ANI-05.2: binds this vehicle's authored class mechanisms. Called
        /// again after a Mk change rebuilds the model, replacing the old
        /// presenter rather than stacking a second one on the same id.
        /// </summary>
        internal void AttachMechanicalPresenter()
        {
            foreach (Nebula.Visuals.EconomyMechanicalPresenter existing
                     in GetComponents<Nebula.Visuals.EconomyMechanicalPresenter>())
                DestroyNow(existing);

            if (visualRoot == null || Def == null) return;
            if (!NebulaVisualCatalog.TryGetVehicle(Def.Id, out NebulaVisualDefinition definition)) return;
            if (!Nebula.Visuals.ShipVehicleMechanicalProfiles.HasRealMechanisms(definition.VisualId)) return;

            gameObject.AddComponent<Nebula.Visuals.EconomyMechanicalPresenter>()
                .Initialize(definition.VisualId, visualRoot.transform,
                    Nebula.Visuals.ShipVehicleMechanicalProfiles.ForVehicle(definition.VisualId),
                    ResolveMechanicalDrive, () => 1f);
        }

        /// <summary>
        /// ANI-05.2 presentation read: a vehicle deploys its tools while it is
        /// really working, and stows them otherwise. Mission phases, transfer
        /// amounts and repair progress are read and never written.
        /// </summary>
        private Nebula.Visuals.MechanicalDrive ResolveMechanicalDrive()
        {
            if (Def == null) return Nebula.Visuals.MechanicalDrive.Stowed;
            if (durability != null && durability.Disabled) return Nebula.Visuals.MechanicalDrive.Disabled;
            if (Mission != null && Mission.Phase == VehiclePhase.InHangar)
                return Nebula.Visuals.MechanicalDrive.Stowed;

            switch (Def.Class)
            {
                case VehicleClass.Scout: return ScoutDrive();
                case VehicleClass.Collector: return CollectorDrive();
                case VehicleClass.Engineer: return EngineerDrive();
                case VehicleClass.Escort: return EscortDrive();
                default: return Nebula.Visuals.MechanicalDrive.Ready;
            }
        }

        /// <summary>
        /// Sensor wings open only while the real 60-second counter is actually
        /// advancing. Approaching, returning, a finished scan and a cancelled
        /// one all stow them; the counter itself is ScoutScanWork's.
        /// </summary>
        private Nebula.Visuals.MechanicalDrive ScoutDrive()
        {
            float progress = ScanProgress01;
            bool advancing = progress > lastScanProgressSample + 1e-5f && progress < 1f;
            lastScanProgressSample = progress;
            return advancing
                ? Nebula.Visuals.MechanicalDrive.Working
                : Nebula.Visuals.MechanicalDrive.Ready;
        }

        /// <summary>
        /// Cradle, clamps and carousel follow the hold itself: they work while
        /// cargo is really moving, hold while the vehicle is carrying a load,
        /// and stow when it is empty. The amount and the transfer rate are
        /// ResourceTransferService's.
        /// </summary>
        private Nebula.Visuals.MechanicalDrive CollectorDrive()
        {
            float held = HeldAmount;
            bool moving = Mathf.Abs(held - lastHeldAmountSample) > 1e-3f;
            lastHeldAmountSample = held;

            if (moving) return Nebula.Visuals.MechanicalDrive.Working;
            return held > 0f
                ? Nebula.Visuals.MechanicalDrive.Holding
                : Nebula.Visuals.MechanicalDrive.Ready;
        }

        /// <summary>
        /// Tools stay shut through the real eight-second safety gate, and only
        /// work while the target is genuinely gaining hull. A stalled repair
        /// does not look like a working one.
        /// </summary>
        private Nebula.Visuals.MechanicalDrive EngineerDrive()
        {
            if (EngineerTask == null) return Nebula.Visuals.MechanicalDrive.Ready;
            if (EngineerTask.Phase != EngineerPhase.Working)
                return Nebula.Visuals.MechanicalDrive.Ready; // WaitForSafety included: tools shut

            float restored = EngineerTask.ActualRestoredHpSoFar;
            bool healing = restored > lastRestoredHpSample + 1e-4f;
            lastRestoredHpSample = restored;
            return healing
                ? Nebula.Visuals.MechanicalDrive.Working
                : Nebula.Visuals.MechanicalDrive.Holding;
        }

        /// <summary>
        /// The fixed lance is only made ready while the escort is really on a
        /// threat. A retreat, a disrupt and an idle follow all stow it.
        /// </summary>
        private Nebula.Visuals.MechanicalDrive EscortDrive()
        {
            if (EscortState == null) return Nebula.Visuals.MechanicalDrive.Ready;
            switch (EscortState.SubState)
            {
                case EscortSubState.Engaging:
                case EscortSubState.Chasing: return Nebula.Visuals.MechanicalDrive.Working;
                case EscortSubState.Retreating: return Nebula.Visuals.MechanicalDrive.Ready;
                default: return Nebula.Visuals.MechanicalDrive.Ready;
            }
        }

        private float lastScanProgressSample;
        private float lastHeldAmountSample;
        private float lastRestoredHpSample;

        private void BuildVisuals()
        {
            NebulaVisualResolver.DestroyInstance(visualRoot);
            visualRoot = null;
            cargoPodVisual = null;

            if (NebulaVisualCatalog.TryGetVehicle(Def.Id, out NebulaVisualDefinition definition))
                visualRoot = NebulaVisualResolver.Instantiate(
                    definition, transform, instanceName: "Authored Vehicle Visual");

            if (visualRoot == null)
            {
                visualRoot = new GameObject("Vehicle Visual (Fallback)");
                visualRoot.transform.SetParent(transform, false);
                if (Def.Class == VehicleClass.Scout) BuildScoutVisual(visualRoot.transform);
                else BuildCollectorVisual(visualRoot.transform);
                return;
            }

            if (Def.Class != VehicleClass.Collector) return;
            Transform cargoAnchor = NebulaVisualResolver.FindAnchor(visualRoot.transform, "anchor.cargo")
                                    ?? visualRoot.transform;
            cargoPodVisual = NebulaVisualResolver.Instantiate(
                NebulaVisualCatalog.CargoPod, cargoAnchor, instanceName: "Authored Cargo Pod");
            UpdateCargoPodVisual();
        }

        private void UpdateCargoPodVisual()
        {
            if (cargoPodVisual != null) cargoPodVisual.SetActive(HeldAmount > 0.0001f);
        }

        private void BuildScoutVisual(Transform parent)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Scout Hull";
            DestroyNow(body.GetComponent<Collider>());
            body.transform.SetParent(parent, false);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            body.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            body.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateLit(new Color(0.55f, 0.8f, 0.95f), 0.6f, 0.7f);

            GameObject antenna = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            antenna.name = "Sensor Antenna";
            DestroyNow(antenna.GetComponent<Collider>());
            antenna.transform.SetParent(parent, false);
            antenna.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            antenna.transform.localScale = new Vector3(0.06f, 0.7f, 0.06f);
            antenna.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateUnlit(new Color(0.8f, 0.95f, 1f));

            GameObject dish = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dish.name = "Sensor Dish";
            DestroyNow(dish.GetComponent<Collider>());
            dish.transform.SetParent(parent, false);
            dish.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            dish.transform.localScale = new Vector3(0.28f, 0.1f, 0.28f);
            dish.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateAdditive(new Color(0.4f, 0.9f, 1f, 0.6f));
        }

        private void BuildCollectorVisual(Transform parent)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Collector Hull";
            DestroyNow(body.GetComponent<Collider>());
            body.transform.SetParent(parent, false);
            body.transform.localScale = new Vector3(1.1f, 0.8f, 1.6f);
            body.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateLit(new Color(0.6f, 0.58f, 0.5f), 0.5f, 0.6f);

            GameObject pod = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pod.name = "Cargo Pod";
            DestroyNow(pod.GetComponent<Collider>());
            pod.transform.SetParent(parent, false);
            pod.transform.localPosition = new Vector3(0f, 0.65f, -0.1f);
            pod.transform.localScale = new Vector3(1.4f, 0.7f, 1.9f);
            pod.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateLit(new Color(0.85f, 0.65f, 0.3f), 0.35f, 0.4f);
        }
    }
}
