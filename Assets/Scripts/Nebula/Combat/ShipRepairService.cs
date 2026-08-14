using System.Collections.Generic;
using Nebula.Extraction;

namespace Nebula.Combat
{
    /// <summary>
    /// Pure, stateless orchestration for the physical ship repair transaction
    /// (VS-013.1 §5) — mirrors WeaponLoadoutService's shape. A single
    /// reservation owner id means only one repair job can ever be in flight;
    /// each 10% package is atomically reserved before it starts, consumed
    /// strictly in proportion to REAL HP actually applied (never to elapsed
    /// time alone — see RepairJobState.AdvanceByAppliedHp), and a completed
    /// package auto-chains into reserving the next one at whatever Hull Form
    /// is current by then. Never applied twice, never a free heal: Cancel
    /// only ever refunds the still-UNCONSUMED remainder of the CURRENT
    /// package at the project's standard 75% fraction.
    /// </summary>
    public static class ShipRepairService
    {
        public static RepairRejectReason CheckStart(RepairJobState job, bool inCombat, bool emergencyShutdown,
            float safeSecondsSinceLastDamage, float shipSpeed, bool armorAtCap, bool hullAtCap, int hullForm, LogisticsCargo cargo)
        {
            if (job.Active) return RepairRejectReason.AlreadyActive;
            if (inCombat) return RepairRejectReason.InCombat;
            if (emergencyShutdown) return RepairRejectReason.EmergencyShutdown;
            if (safeSecondsSinceLastDamage < RepairCatalog.RequiredSafeSecondsSinceLastDamage) return RepairRejectReason.TooSoonAfterDamage;
            if (shipSpeed > RepairCatalog.RequiredMaxShipSpeed) return RepairRejectReason.TooFast;
            if (armorAtCap && hullAtCap) return RepairRejectReason.AlreadyAtCap;
            if (!HasCargo(cargo, hullForm)) return RepairRejectReason.InsufficientResources;
            return RepairRejectReason.None;
        }

        private static bool HasCargo(LogisticsCargo cargo, int hullForm)
        {
            if (cargo == null) return false;
            foreach (var (itemId, amount) in RepairCatalog.PackageCost(hullForm))
                if (cargo.GetAvailable(itemId) < amount - 0.0001f) return false;
            return true;
        }

        public static bool TryStart(RepairJobState job, int hullForm, LogisticsCargo cargo)
        {
            if (cargo == null) return false;
            List<(string itemId, float amount)> cost = RepairCatalog.PackageCost(hullForm);
            if (!cargo.TryReserveAll(RepairCatalog.RepairCargoOwnerId, cost)) return false;
            job.StartPackage(hullForm);
            return true;
        }

        /// <summary>Permanently consumes exactly `progressDeltaThisTick` (0..1) of the current package's reserved cost — called every tick right after Durability.RepairExact applies the matching real HP.</summary>
        public static void ConsumeProportional(RepairJobState job, LogisticsCargo cargo, float progressDeltaThisTick)
        {
            if (progressDeltaThisTick <= 0f || cargo == null) return;
            foreach (var (itemId, amount) in RepairCatalog.PackageCost(job.HullFormForCurrentPackage))
                cargo.ConsumePartialReservation(RepairCatalog.RepairCargoOwnerId, itemId, amount * progressDeltaThisTick);
        }

        /// <summary>
        /// The current package just reached 100% (fully paid+applied). Tries to
        /// reserve+start the next one at the CURRENT Hull Form. Stops the job
        /// outright once both layers are already at their cap (nothing left to
        /// accomplish); if resources are insufficient the job simply stalls
        /// (Active stays true, no new reservation) so the caller can show an
        /// explicit missing-resource list without losing any already-applied
        /// K/Z/G or already-consumed resources from completed packages.
        /// </summary>
        public static bool CompletePackageAndTryContinue(RepairJobState job, int currentHullForm, LogisticsCargo cargo, bool armorAtCap, bool hullAtCap)
        {
            if (armorAtCap && hullAtCap) { job.Stop(); return false; }
            if (cargo == null) return false;
            List<(string itemId, float amount)> cost = RepairCatalog.PackageCost(currentHullForm);
            if (!cargo.TryReserveAll(RepairCatalog.RepairCargoOwnerId, cost)) return false;
            job.ResetForNextPackage(currentHullForm);
            return true;
        }

        /// <summary>75% resource refund (RepairCatalog.RefundFraction) of whatever remains reserved-but-unconsumed for the current package only.</summary>
        public static Dictionary<string, float> Cancel(RepairJobState job, LogisticsCargo cargo)
        {
            job.Stop();
            return cargo?.ConsumeReservationForRefund(RepairCatalog.RepairCargoOwnerId, RepairCatalog.RefundFraction);
        }

        /// <summary>
        /// VS-013.2: restores an active repair job's reservation at load time to
        /// ONLY the remaining (unconsumed) fraction of the current package's cost —
        /// the already-progressed portion was already physically consumed from
        /// stock before save, so re-reserving the FULL package cost here would be a
        /// phantom double reservation. Any stale reservation left under the repair
        /// owner id is cleared first so this is always an exact reset, never an
        /// addition on top of leftover state. A package restored at (or within
        /// epsilon of) 100% progress has nothing left to reserve at all.
        /// </summary>
        public static bool RestorePartialReservation(LogisticsCargo cargo, int hullForm, float packageProgress01)
        {
            if (cargo == null) return false;
            cargo.ReleaseReservation(RepairCatalog.RepairCargoOwnerId);
            if (packageProgress01 >= 1f - 0.0001f) return true;
            List<(string itemId, float amount)> remaining = RepairCatalog.RemainingCost(hullForm, packageProgress01);
            return cargo.TryReserveAll(RepairCatalog.RepairCargoOwnerId, remaining);
        }
    }
}
