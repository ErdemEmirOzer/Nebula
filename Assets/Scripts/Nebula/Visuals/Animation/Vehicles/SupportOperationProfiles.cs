using Nebula.Hangar;

namespace Nebula.Visuals
{
    /// <summary>
    /// The operation a support vehicle is visibly performing. Every value here
    /// is reached only from a real mission state — none of them is a mode the
    /// animation layer can put itself into.
    /// </summary>
    public enum SupportOperationVisual
    {
        None,
        Scan,
        Repair,
        Recovery,
        Protect,
        RetreatWarning
    }

    /// <summary>Which way a real transfer moved, derived from the vehicle's own held amount.</summary>
    public enum TransferDirection
    {
        None,
        Inbound,
        Outbound
    }

    /// <summary>
    /// Maps real vehicle state onto the seven shared VFX sprites that already
    /// exist. No new asset is authored here, and no sprite is used for
    /// something it does not depict.
    /// </summary>
    public static class SupportOperationProfiles
    {
        /// <summary>Below this, a held-amount change is float noise rather than a transfer.</summary>
        public const float TransferEpsilon = 1e-3f;

        /// <summary>
        /// A Scout shows its scan only while the real 60-second counter is
        /// actually advancing — which it does only in range. Approaching shows
        /// nothing, and neither does a finished or cancelled scan.
        /// </summary>
        public static SupportOperationVisual ForScout(bool scanAdvancing)
            => scanAdvancing ? SupportOperationVisual.Scan : SupportOperationVisual.None;

        /// <summary>
        /// An Engineer shows work only while the target is really gaining hull.
        /// The eight-second WaitForSafety gate deliberately shows nothing: the
        /// vehicle is waiting, not working.
        /// </summary>
        public static SupportOperationVisual ForEngineer(EngineerPhase phase, EngineerWorkKind work, bool healingNow)
        {
            if (phase != EngineerPhase.Working || !healingNow) return SupportOperationVisual.None;
            return work == EngineerWorkKind.Recover ? SupportOperationVisual.Recovery : SupportOperationVisual.Repair;
        }

        /// <summary>
        /// An Escort marks only the two states that have an honest sprite: a
        /// standing protection assignment, and a retreat. Engaging and chasing
        /// are weapon behaviour and belong to ANI-04.
        /// </summary>
        public static SupportOperationVisual ForEscort(EscortSubState subState)
        {
            switch (subState)
            {
                case EscortSubState.Retreating: return SupportOperationVisual.RetreatWarning;
                case EscortSubState.Following:
                case EscortSubState.Idle: return SupportOperationVisual.Protect;
                default: return SupportOperationVisual.None;
            }
        }

        /// <summary>
        /// A POI job reuses the very same visuals: an anomaly scan is a scan, a
        /// wreck salvage is a transfer and therefore carries no VFX of its own —
        /// the real item proxy is the whole story.
        /// </summary>
        public static SupportOperationVisual ForPoi(VehicleClass vehicleClass, PoiTaskPhaseKind phase)
        {
            if (phase != PoiTaskPhaseKind.Working) return SupportOperationVisual.None;
            return vehicleClass == VehicleClass.Scout ? SupportOperationVisual.Scan : SupportOperationVisual.None;
        }

        /// <summary>Which of the seven existing sprites depicts this operation.</summary>
        public static NebulaCommonVfx Vfx(SupportOperationVisual visual)
        {
            switch (visual)
            {
                case SupportOperationVisual.Scan: return NebulaCommonVfx.ScanPulse;
                case SupportOperationVisual.Repair: return NebulaCommonVfx.RepairPulse;
                case SupportOperationVisual.Recovery: return NebulaCommonVfx.BuildHologram;
                case SupportOperationVisual.Protect: return NebulaCommonVfx.ShieldImpact;
                case SupportOperationVisual.RetreatWarning: return NebulaCommonVfx.WarningPulse;
                default: return NebulaCommonVfx.HitSpark;
            }
        }

        /// <summary>True when this operation draws anything at all.</summary>
        public static bool HasVfx(SupportOperationVisual visual) => visual != SupportOperationVisual.None;

        /// <summary>
        /// Whether the operation is drawn on the vehicle itself or over what it
        /// is working on. A repair belongs on the wound, not on the mechanic.
        /// </summary>
        public static bool DrawsOnTarget(SupportOperationVisual visual)
            => visual == SupportOperationVisual.Repair
               || visual == SupportOperationVisual.Recovery
               || visual == SupportOperationVisual.Protect;

        /// <summary>
        /// Reads a real change in the vehicle's own cargo. Zero and negative-zero
        /// changes are not transfers, so they produce no proxy.
        /// </summary>
        public static TransferDirection Direction(float heldDelta)
        {
            if (heldDelta > TransferEpsilon) return TransferDirection.Inbound;
            if (heldDelta < -TransferEpsilon) return TransferDirection.Outbound;
            return TransferDirection.None;
        }
    }

    /// <summary>
    /// A copy of the POI task's phases kept at the presentation boundary, so the
    /// mapping above stays testable without constructing a real POI task.
    /// </summary>
    public enum PoiTaskPhaseKind
    {
        ToTarget,
        Working,
        Returning
    }
}
