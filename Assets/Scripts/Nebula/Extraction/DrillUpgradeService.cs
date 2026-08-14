using System.Collections.Generic;

namespace Nebula.Extraction
{
    public enum DrillUpgradeRejectReason { None, AlreadyUpgrading, NotActive, MaxTier, ModuleTierNotMet, InsufficientResources }

    /// <summary>
    /// VS-017A.2 §B3: pure, stateless orchestration for the Drill T1→T2
    /// upgrade — atomic reserve, in-place tier swap on completion, and the
    /// project-wide floor(75%) refund of whatever is still reserved on cancel.
    /// Mirrors <see cref="StructureUpgradeService"/>'s exact shape and reuses
    /// the same <see cref="LogisticsCargo"/> reservation primitives, so no
    /// second cost/refund authority is introduced.
    /// </summary>
    public static class DrillUpgradeService
    {
        /// <summary>Reservation owner key — the drill's own worldId+nodeId, so two drills can never share or clobber each other's reservation.</summary>
        public static string OwnerId(string worldId, string nodeId) => DrillUpgradeJob.CargoOwnerPrefix + worldId + "/" + nodeId;

        public static DrillUpgradeRejectReason CheckStart(DrillRuntime runtime, int stationConstructionTier, LogisticsCargo cargo)
        {
            if (runtime == null) return DrillUpgradeRejectReason.NotActive;
            if (runtime.ActiveUpgradeJob != null) return DrillUpgradeRejectReason.AlreadyUpgrading;
            if (runtime.State != DrillLifecycle.Active) return DrillUpgradeRejectReason.NotActive;
            if (runtime.Tier >= DrillUpgradeCatalog.MaxTier) return DrillUpgradeRejectReason.MaxTier;
            if (stationConstructionTier < DrillUpgradeCatalog.RequiredStationConstructionTier) return DrillUpgradeRejectReason.ModuleTierNotMet;
            if (!HasCost(cargo, runtime.Family)) return DrillUpgradeRejectReason.InsufficientResources;
            return DrillUpgradeRejectReason.None;
        }

        private static bool HasCost(LogisticsCargo cargo, ResourceFamily family)
        {
            if (cargo == null) return false;
            foreach (var (itemId, amount) in DrillUpgradeCatalog.Cost(family))
                if (cargo.GetAvailable(itemId) < amount - 0.0001f) return false;
            return true;
        }

        /// <summary>Atomically reserves the full cost and returns the started job, or null if anything about the gate/cost fails. Never partially reserves.</summary>
        public static DrillUpgradeJob TryStart(DrillRuntime runtime, string worldId, string nodeId, int stationConstructionTier, LogisticsCargo cargo)
        {
            if (CheckStart(runtime, stationConstructionTier, cargo) != DrillUpgradeRejectReason.None) return null;
            if (!cargo.TryReserveAll(OwnerId(worldId, nodeId), DrillUpgradeCatalog.Cost(runtime.Family))) return null;
            return new DrillUpgradeJob(DrillUpgradeCatalog.MaxTier, DrillUpgradeCatalog.UpgradeSeconds, DrillUpgradeCatalog.UpgradeSeconds);
        }

        /// <summary>Consumes the reservation exactly once and swaps the drill to its new tier — stored resource is carried over untouched (never wiped, never duplicated).</summary>
        public static void Complete(DrillRuntime runtime, string worldId, string nodeId, DrillUpgradeJob job, LogisticsCargo cargo)
        {
            cargo?.ConsumeReservation(OwnerId(worldId, nodeId));
            runtime?.ApplyTierUpgrade(job != null ? job.TargetTier : DrillUpgradeCatalog.MaxTier);
        }

        /// <summary>floor(75%) refund of whatever is still reserved-but-unconsumed for the in-flight upgrade only — the drill's CURRENT tier, storage and stored amount are untouched.</summary>
        public static Dictionary<string, float> Cancel(string worldId, string nodeId, LogisticsCargo cargo) =>
            cargo?.ConsumeReservationForRefund(OwnerId(worldId, nodeId), DrillUpgradeCatalog.RefundFraction);

        /// <summary>
        /// Re-establishes the reservation a save could not carry directly, using ONLY the
        /// real remaining cost the save recorded — never a fresh full cost (double charge)
        /// and never nothing (a free upgrade). Returns false when the resources genuinely
        /// are not there any more; the caller must then stop the upgrade explicitly rather
        /// than letting it continue unfunded.
        /// </summary>
        public static bool TryRestoreReservation(string worldId, string nodeId, LogisticsCargo cargo, IReadOnlyList<(string itemId, float amount)> remainingCost)
        {
            if (cargo == null) return false;
            if (remainingCost == null || remainingCost.Count == 0) return false;
            cargo.ReleaseReservation(OwnerId(worldId, nodeId));
            return cargo.TryReserveAll(OwnerId(worldId, nodeId), remainingCost);
        }
    }
}
