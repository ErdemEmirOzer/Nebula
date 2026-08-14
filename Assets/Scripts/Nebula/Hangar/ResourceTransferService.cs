using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;

namespace Nebula.Hangar
{
    /// <summary>
    /// Single transaction authority for every drill&lt;-&gt;vehicle-&lt;-&gt;ship
    /// resource move (VS-007 §5) — the same 10 volume/s rate in every
    /// direction, so drill + vehicle-hold + ship cargo always sum to the same
    /// total outside of the drill's own production tick. Never duplicates,
    /// deletes or exceeds capacity: every method is bounded by the smaller of
    /// (rate * dt) and the receiving side's actual free space.
    /// </summary>
    public static class ResourceTransferService
    {
        public const float VolumePerSecond = 10f;

        /// <summary>VS-017A.1 §1: fired the moment a real, positive amount is actually withdrawn from a drill -- the single authoritative "resource collected" commit point every collection path (VehicleAgent, remote tick math) funnels through. Narrow, notification-only -- never changes the transfer itself.</summary>
        public static event System.Action<float> Collected;

        /// <summary>Mirrors every other static event's own reset-on-campaign-restart convention (ProgressionService.Reset, DominanceSystem.ResetForSession) so a disposed session's subscribers can never fire into the next one.</summary>
        public static void ResetForSession() => Collected = null;

        /// <summary>Withdraws from a drill into the vehicle's single-item hold, capped by remaining hold volume, at the standard 10 vol/s rate. Returns the amount collected this call.</summary>
        public static float CollectFromDrill(DrillRuntime drill, ref string heldItemId, ref float heldAmount, float holdCapacityVolume, float dt) =>
            CollectFromDrill(drill, ref heldItemId, ref heldAmount, holdCapacityVolume, dt, VolumePerSecond);

        /// <summary>VS-011 §5: rate-parameterized overload — Collector Mk I/Mk II transfer at 10/20 vol/s respectively.</summary>
        public static float CollectFromDrill(DrillRuntime drill, ref string heldItemId, ref float heldAmount, float holdCapacityVolume, float dt, float ratePerSecond)
        {
            if (drill == null || dt <= 0f) return 0f;
            if (drill.StoredAmount <= 0.0001f) return 0f;

            string itemId = ExtractionCatalog.ResourceIds[(int)drill.Family];
            if (heldAmount > 0.0001f && heldItemId != itemId) return 0f; // a hold only ever carries one item at a time

            float perUnitVolume = Mathf.Max(0.0001f, CargoItemVolumes.PerUnit(itemId));
            float usedVolume = heldAmount * perUnitVolume;
            float freeVolume = Mathf.Max(0f, holdCapacityVolume - usedVolume);
            float maxUnitsByVolume = freeVolume / perUnitVolume;

            float want = Mathf.Min(ratePerSecond * dt, maxUnitsByVolume);
            if (want <= 0f) return 0f;

            float taken = drill.Withdraw(want);
            if (taken <= 0f) return 0f;
            heldItemId = itemId;
            heldAmount += taken;
            Collected?.Invoke(taken);
            return taken;
        }

        /// <summary>Delivers the vehicle's held cargo into ship cargo, capped by the ship's actual free capacity, at the standard 10 vol/s rate. Returns the amount delivered this call.</summary>
        public static float DeliverToShip(LogisticsCargo shipCargo, ref string heldItemId, ref float heldAmount, float dt) =>
            DeliverToShip(shipCargo, ref heldItemId, ref heldAmount, dt, VolumePerSecond);

        public static float DeliverToShip(LogisticsCargo shipCargo, ref string heldItemId, ref float heldAmount, float dt, float ratePerSecond)
        {
            if (shipCargo == null || dt <= 0f || heldAmount <= 0.0001f || heldItemId == null) return 0f;

            float want = Mathf.Min(ratePerSecond * dt, heldAmount);
            float added = shipCargo.Add(heldItemId, want); // Add() itself is capacity-bounded
            heldAmount -= added;
            if (heldAmount <= 0.0001f) { heldAmount = 0f; heldItemId = null; }
            return added;
        }

        /// <summary>Returns held cargo back into the drill it came from (ship left the system), capped by the drill's remaining storage, at the standard 10 vol/s rate. Returns the amount returned this call.</summary>
        public static float ReturnToDrill(DrillRuntime drill, ref string heldItemId, ref float heldAmount, float dt) =>
            ReturnToDrill(drill, ref heldItemId, ref heldAmount, dt, VolumePerSecond);

        public static float ReturnToDrill(DrillRuntime drill, ref string heldItemId, ref float heldAmount, float dt, float ratePerSecond)
        {
            if (drill == null || dt <= 0f || heldAmount <= 0.0001f || heldItemId == null) return 0f;

            float want = Mathf.Min(ratePerSecond * dt, heldAmount);
            float deposited = drill.Deposit(want); // Deposit() itself is capacity-bounded
            heldAmount -= deposited;
            if (heldAmount <= 0.0001f) { heldAmount = 0f; heldItemId = null; }
            return deposited;
        }

        /// <summary>VS-011 §3/§5: delivers the vehicle's held cargo into a Logistics Center's inventory, capped by BOTH the vehicle's own rate and the center's remaining aggregate per-tick budget (the caller rations that budget across every vehicle unloading at the same center this tick). Returns the amount delivered this call.</summary>
        public static float DeliverToCenter(LogisticsCargo centerInventory, ref string heldItemId, ref float heldAmount,
            float ratePerSecond, float dt, float remainingAggregateBudget)
        {
            if (centerInventory == null || dt <= 0f || heldAmount <= 0.0001f || heldItemId == null) return 0f;
            if (!LogisticsCenterCatalog.IsAcceptedItem(heldItemId)) return 0f;

            float want = Mathf.Min(ratePerSecond * dt, heldAmount, Mathf.Max(0f, remainingAggregateBudget));
            float added = centerInventory.Add(heldItemId, want);
            heldAmount -= added;
            if (heldAmount <= 0.0001f) { heldAmount = 0f; heldItemId = null; }
            return added;
        }
    }
}
