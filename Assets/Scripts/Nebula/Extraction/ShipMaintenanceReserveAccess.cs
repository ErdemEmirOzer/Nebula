using System;
using UnityEngine;
using Nebula.Sectors;
using Nebula.Ship;

namespace Nebula.Extraction
{
    public enum ReserveTransferRejectReason { None, NotInRange, ItemNotAccepted, InsufficientAmount, ReserveFull }

    /// <summary>
    /// VS-015.1 §4: ship-side physical proximity access to a nearby active
    /// Command Center's Maintenance Reserve — mirrors ShipLogisticsCenter's
    /// own proximity-detection shape (nearest active anchor within range,
    /// re-checked every frame), but exposes a discrete quantity Deposit/
    /// Withdraw API instead of a continuous per-second key-held drain, since
    /// this is a deliberate player-initiated transfer, not automatic
    /// vehicle cargo movement. No teleportation — both ship and reserve are
    /// real physical entities, and a transfer is refused outside ApproachRange.
    /// </summary>
    public class ShipMaintenanceReserveAccess : MonoBehaviour
    {
        public event Action<float> Deposited;
        public event Action<float> Withdrawn;
        public const float ApproachRange = 500f;
        public const float DepositWithdrawIncrement = 10f;

        public SurfaceStructure NearbyCommand { get; private set; }
        public bool InRange { get; private set; }

        private ShipLogistics logistics;

        private void Awake() => logistics = GetComponent<ShipLogistics>();

        private void Update() => ResolveNearbyCommand();

        /// <summary>
        /// Re-answers "is there an active Command Center within
        /// <see cref="ApproachRange"/> of the ship right now", which is the gate every
        /// transfer below gets refused by. Called every frame by <see cref="Update"/>;
        /// public so it can be driven at a known instant instead of only by the player
        /// loop.
        /// <para>
        /// Split from the frame callback for the same reason
        /// <c>ShipExtraction.TickScan</c> was split from its input read: the answer
        /// depends on where two objects are <em>at the moment it is asked</em>, and a
        /// Command Center is not stationary — it re-derives its own world position from
        /// its planet every frame (<c>SurfaceStructure.cs:215</c>). Anything that parks
        /// the ship on the structure and then waits a frame is comparing a position it
        /// sampled before the structure moved with the structure's position after, and
        /// the two Update methods run in undefined order, so no number of extra frames
        /// converges. That is exactly how both G-16 tutorial tests reported a ship
        /// placed on the Command Center as 5321.5 units away from it. Nothing here is
        /// relaxed or bypassed — this is the same resolution, asked at a defined time.
        /// </para>
        /// </summary>
        public void ResolveNearbyCommand()
        {
            NearbyCommand = null;
            InRange = false;
            foreach (SurfaceStructure s in ExtractionState.Structures)
            {
                if (s == null || s.Type != SurfaceStructureType.Command || s.MaintenanceReserve == null || !s.Runtime.IsActive) continue;
                float distance = Vector3.Distance(transform.position, s.transform.position);
                if (distance > ApproachRange) continue;
                NearbyCommand = s;
                InRange = true;
                break;
            }
        }

        /// <summary>Physically moves `amount` of `itemId` from the ship's cargo into the nearby reserve. Whitelist/capacity are enforced by MaintenanceReserve.Add itself (bounded, all-or-nothing per call).</summary>
        public ReserveTransferRejectReason TryDeposit(string itemId, float amount)
        {
            if (!InRange || NearbyCommand?.MaintenanceReserve == null) return ReserveTransferRejectReason.NotInRange;
            if (!MaintenanceReserve.IsAccepted(itemId)) return ReserveTransferRejectReason.ItemNotAccepted;
            LogisticsCargo cargo = logistics?.Cargo;
            if (cargo == null || cargo.GetAvailable(itemId) < amount - 0.0001f) return ReserveTransferRejectReason.InsufficientAmount;

            // P1-ECO-016: take from the ship FIRST, then hand over.
            //
            // This used to add to the reserve and only then call TryRemove on the ship,
            // throwing that call's answer away — the one ordering in which a failure
            // duplicates the item rather than losing it. Whatever the reserve turns out
            // not to accept is given straight back to the hold, and that always fits:
            // it is volume the same call freed a moment ago.
            if (!cargo.TryConsumeImmediate(itemId, amount)) return ReserveTransferRejectReason.InsufficientAmount;

            float accepted = NearbyCommand.MaintenanceReserve.Add(itemId, amount);
            float rejected = amount - accepted;
            if (rejected > 0.0001f) cargo.Add(itemId, rejected);
            if (accepted <= 0.0001f) return ReserveTransferRejectReason.ReserveFull;

            Deposited?.Invoke(accepted);
            return ReserveTransferRejectReason.None;
        }

        /// <summary>Physically moves `amount` of `itemId` from the nearby reserve back into the ship's cargo.</summary>
        public ReserveTransferRejectReason TryWithdraw(string itemId, float amount)
        {
            if (!InRange || NearbyCommand?.MaintenanceReserve == null) return ReserveTransferRejectReason.NotInRange;
            LogisticsCargo reserveCargo = NearbyCommand.MaintenanceReserve.Cargo;
            if (reserveCargo.GetAvailable(itemId) < amount - 0.0001f) return ReserveTransferRejectReason.InsufficientAmount;
            LogisticsCargo shipCargo = logistics?.Cargo;
            if (shipCargo == null) return ReserveTransferRejectReason.NotInRange;

            float shipFreeSpace = shipCargo.AvailableVolume / Mathf.Max(0.0001f, LogisticsCargo.VolumePerUnit(itemId));
            float moveAmount = Mathf.Min(amount, shipFreeSpace);
            if (moveAmount <= 0.0001f) return ReserveTransferRejectReason.InsufficientAmount;

            // Same rule in the other direction: nothing reaches the hold that the
            // reserve did not actually give up.
            if (!reserveCargo.TryConsumeImmediate(itemId, moveAmount)) return ReserveTransferRejectReason.InsufficientAmount;
            float landed = shipCargo.Add(itemId, moveAmount);
            if (moveAmount - landed > 0.0001f) reserveCargo.Add(itemId, moveAmount - landed);
            if (landed <= 0.0001f) return ReserveTransferRejectReason.InsufficientAmount;

            Withdrawn?.Invoke(landed);
            return ReserveTransferRejectReason.None;
        }
    }
}
