namespace Nebula.Logistics
{
    /// <summary>A persistent Collector route's current leg (VS-011 §4).</summary>
    public enum RoutePhase { Paused, ToSource, Loading, ToCenter, Queued, Unloading, Holding, Returning }

    public enum RoutePriority { Low = 0, Normal = 1, High = 2 }

    /// <summary>Why a route isn't currently progressing (HUD-facing, never auto-resolved by picking a different drill/vehicle).</summary>
    public enum RouteWaitReason { None, SourceDrillMissing, SourceDrillInactive, SourceDrillEvacuated, SourceDrillContested, CenterFull, CenterDisabled }

    /// <summary>
    /// One persistent drill-&gt;Collector-&gt;Logistics-Center route (VS-011
    /// §4). Pure data + explicit transitions, mirroring
    /// <see cref="Hangar.VehicleMissionState"/>'s "never assume distance/time
    /// equivalence" discipline — the physical mover (VehicleAgent, live) or
    /// the distant delta tick (remote) drives every transition once the
    /// underlying condition is actually true. Route/vehicle/drill identity
    /// never changes after creation; only Phase/WaitReason/cargo/priority/
    /// transaction bookkeeping mutate.
    /// </summary>
    public class CollectorRoute
    {
        public string RouteId { get; }
        public string SystemId { get; }
        public string DrillWorldId { get; }
        public string VehicleId { get; }

        public RoutePriority Priority { get; private set; }
        public RoutePhase Phase { get; private set; }
        /// <summary>The phase to resume into when the player un-pauses (never itself one of the live 7 working phases).</summary>
        public RoutePhase ResumePhase { get; private set; } = RoutePhase.ToSource;
        public RouteWaitReason WaitReason { get; private set; }

        public string HeldItemId { get; private set; }
        public float HeldAmount { get; private set; }

        /// <summary>Remote-tick equivalent of a live physical distance-to-go; unused by the live path, which reads real Transform distance instead.</summary>
        public float RemainingDistance { get; private set; }

        public string ActiveTransferTransactionId { get; private set; }
        public string LastTransferTransactionId { get; private set; }
        /// <summary>Timestamp (accumulated active-sim seconds) this route started waiting for a dock — the deterministic dock tie-break's 3rd key.</summary>
        public float DockWaitStartedAt { get; private set; }

        public CollectorRoute(string routeId, string systemId, string drillWorldId, string vehicleId, RoutePriority priority)
        {
            RouteId = routeId; SystemId = systemId; DrillWorldId = drillWorldId; VehicleId = vehicleId;
            Priority = priority;
            Phase = RoutePhase.ToSource;
        }

        /// <summary>Save-restore constructor — exact field values, no re-validation.</summary>
        public CollectorRoute(string routeId, string systemId, string drillWorldId, string vehicleId, RoutePriority priority,
            RoutePhase phase, RoutePhase resumePhase, RouteWaitReason waitReason, string heldItemId, float heldAmount,
            float remainingDistance, string activeTransferTransactionId, string lastTransferTransactionId, float dockWaitStartedAt)
        {
            RouteId = routeId; SystemId = systemId; DrillWorldId = drillWorldId; VehicleId = vehicleId;
            Priority = priority; Phase = phase; ResumePhase = resumePhase; WaitReason = waitReason;
            HeldItemId = heldItemId; HeldAmount = heldAmount; RemainingDistance = remainingDistance;
            ActiveTransferTransactionId = activeTransferTransactionId; LastTransferTransactionId = lastTransferTransactionId;
            DockWaitStartedAt = dockWaitStartedAt;
        }

        public void SetPriority(RoutePriority priority) => Priority = priority;

        public void SetWaitReason(RouteWaitReason reason) => WaitReason = reason;

        public void SetHeld(string itemId, float amount) { HeldItemId = amount > 0.0001f ? itemId : null; HeldAmount = amount > 0.0001f ? amount : 0f; }

        public void SetRemainingDistance(float distance) => RemainingDistance = UnityEngine.Mathf.Max(0f, distance);

        public bool Paused => Phase == RoutePhase.Paused;

        public void Pause()
        {
            if (Phase == RoutePhase.Paused) return;
            ResumePhase = Phase;
            Phase = RoutePhase.Paused;
        }

        /// <summary>Player must explicitly resume — never automatic (VS-011 §6).</summary>
        public void Resume()
        {
            if (Phase != RoutePhase.Paused) return;
            Phase = ResumePhase;
            WaitReason = RouteWaitReason.None;
        }

        public void SetPhase(RoutePhase phase) => Phase = phase;

        public void EnterQueued(float nowSeconds) { Phase = RoutePhase.Queued; DockWaitStartedAt = nowSeconds; }
        public void EnterHolding(float nowSeconds) { Phase = RoutePhase.Holding; DockWaitStartedAt = nowSeconds; }
        public void EnterUnloading() { Phase = RoutePhase.Unloading; }

        public void BeginTransferTransaction(string transactionId) => ActiveTransferTransactionId = transactionId;

        public void EndTransferTransaction()
        {
            if (ActiveTransferTransactionId != null) LastTransferTransactionId = ActiveTransferTransactionId;
            ActiveTransferTransactionId = null;
        }
    }
}
