using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Extraction
{
    /// <summary>
    /// The ship's logistics hold — a keyed store (stable item IDs → amount)
    /// with owner/transaction-based reservations, so processing, module
    /// upgrades, hangar jobs and structure builds can share one physical cargo
    /// safely. Reserved items stay physically present (still occupy capacity;
    /// reserving never manufactures artificial free space) until the owner
    /// either consumes them (permanent) or releases them (returned to
    /// available). All amounts are non-negative; every mutating path is
    /// designed so resources can never be duplicated or destroyed outside an
    /// explicit, deliberate refund/consume call.
    /// <para>
    /// P2-DATA-301: this class also carried a second ledger — "pending output
    /// claims", volume promised to a producer that could not yet deliver, folded
    /// into <see cref="UsedVolume"/>. It had no producer and no consumer in the
    /// game, and no field in the save: the only code that ever set a claim was a
    /// test. A quantity that counts against the player's capacity, that nothing
    /// writes and that a save cannot carry, is not a feature waiting for its
    /// consumer — it is a hold whose used volume would silently change meaning
    /// across a save/load the first time anything did write one. It is removed
    /// rather than persisted, because the bench that used to need it now settles
    /// its output inside the same call that consumes the input
    /// (<c>ProcessingBenchCommands.TryConvert</c>), so nothing is owed at any
    /// point a save could be taken. Used volume is physical stock, exactly.
    /// </para>
    /// </summary>
    public class LogisticsCargo
    {
        private readonly Dictionary<string, float> stored = new Dictionary<string, float>();
        private readonly Dictionary<string, Dictionary<string, float>> reservations =
            new Dictionary<string, Dictionary<string, float>>();
        public float Capacity { get; private set; }

        public LogisticsCargo(float capacity) { Capacity = Mathf.Max(0f, capacity); }

        /// <summary>Changes capacity (module tier effect). Never destroys existing stock, even if it now exceeds capacity.</summary>
        public void SetCapacity(float newCapacity) => Capacity = Mathf.Max(0f, newCapacity);

        public static float VolumePerUnit(string itemId) => CargoItemVolumes.PerUnit(itemId);

        // --- Physical totals ---------------------------------------------------
        private float PhysicalVolume()
        {
            float total = 0f;
            foreach (var kv in stored) total += kv.Value * VolumePerUnit(kv.Key);
            return total;
        }

        /// <summary>Physical stock volume. Nothing else counts against capacity — see the type comment on P2-DATA-301.</summary>
        public float UsedVolume => PhysicalVolume();

        public float AvailableVolume => Mathf.Max(0f, Capacity - UsedVolume);

        /// <summary>True when physically-stored volume alone exceeds capacity (e.g. after a save migration shrank capacity math).</summary>
        public bool OverCapacity => PhysicalVolume() > Capacity + 0.0001f;

        public float Total => PhysicalVolume(); // kept for HUD/back-compat: physical volume, matching the old "Total" meaning
        public bool IsEmpty => stored.Count == 0;
        public float FreeSpace => AvailableVolume; // back-compat alias

        /// <summary>Physically stored amount of an item, ignoring reservations.</summary>
        public float GetStored(string itemId) => stored.TryGetValue(itemId, out float v) ? v : 0f;

        /// <summary>Back-compat alias for GetStored (existing call sites use Get()).</summary>
        public float Get(string itemId) => GetStored(itemId);

        /// <summary>Sum of this item reserved by any owner.</summary>
        public float GetReserved(string itemId)
        {
            float total = 0f;
            foreach (var ownerMap in reservations.Values)
                if (ownerMap.TryGetValue(itemId, out float v)) total += v;
            return total;
        }

        /// <summary>What's left of this item for a NEW reservation or direct removal.</summary>
        public float GetAvailable(string itemId) => Mathf.Max(0f, GetStored(itemId) - GetReserved(itemId));

        /// <summary>Enumerates physically stored items (save/HUD).</summary>
        public IEnumerable<KeyValuePair<string, float>> Items => stored;

        // --- Adding physical stock ----------------------------------------------
        /// <summary>Stores up to the available volume; returns what actually fit.</summary>
        public float Add(string itemId, float amount)
        {
            if (amount <= 0f) return 0f;
            float volPerUnit = Mathf.Max(0.0001f, VolumePerUnit(itemId));
            float maxUnits = AvailableVolume / volPerUnit;
            float toAdd = Mathf.Min(amount, maxUnits);
            if (toAdd <= 0f) return 0f;
            stored[itemId] = GetStored(itemId) + toAdd;
            return toAdd;
        }

        /// <summary>Restore-only: writes the exact saved amount, bypassing capacity (never silently drops saved stock).</summary>
        public void LoadItem(string itemId, float amount)
        {
            if (amount > 0.0001f) stored[itemId] = amount;
        }

        public void Clear()
        {
            stored.Clear();
            reservations.Clear();
        }

        /// <summary>Immediate all-or-nothing consumption with no reservation bookkeeping (kept for simple one-shot costs like drill/Command/Laser builds).</summary>
        public bool TryConsumeImmediate(string itemId, float amount)
        {
            if (amount < 0f) return false;
            if (amount == 0f) return true;
            if (GetAvailable(itemId) < amount - 0.0001f) return false;
            float left = GetStored(itemId) - amount;
            if (left <= 0.0001f) stored.Remove(itemId); else stored[itemId] = left;
            return true;
        }

        /// <summary>Back-compat alias.</summary>
        public bool TryRemove(string itemId, float amount) => TryConsumeImmediate(itemId, amount);

        // --- Owner/transaction reservations -------------------------------------
        private Dictionary<string, float> ReservationMap(string owner)
        {
            if (!reservations.TryGetValue(owner, out var map)) { map = new Dictionary<string, float>(); reservations[owner] = map; }
            return map;
        }

        /// <summary>Atomically reserves every listed item under one owner id (all-or-nothing). Reserved items stay physically stored.</summary>
        public bool TryReserveAll(string owner, IReadOnlyList<(string itemId, float amount)> costs)
        {
            for (int i = 0; i < costs.Count; i++)
                if (GetAvailable(costs[i].itemId) < costs[i].amount - 0.0001f) return false;

            Dictionary<string, float> map = ReservationMap(owner);
            for (int i = 0; i < costs.Count; i++)
            {
                if (costs[i].amount <= 0f) continue;
                map[costs[i].itemId] = (map.TryGetValue(costs[i].itemId, out float v) ? v : 0f) + costs[i].amount;
            }
            return true;
        }

        /// <summary>
        /// Atomically replaces one owner's reservation with the requested total.
        /// The owner's current lock is treated as available to that same owner,
        /// so save/load and streaming restore are idempotent instead of stacking.
        /// On failure the previous reservation is left untouched.
        /// </summary>
        public bool TrySetReservation(string owner, IReadOnlyList<(string itemId, float amount)> costs)
        {
            var desired = new Dictionary<string, float>();
            for (int i = 0; i < costs.Count; i++)
            {
                string itemId = costs[i].itemId;
                float amount = costs[i].amount;
                if (string.IsNullOrEmpty(itemId) || amount <= 0f) continue;
                desired[itemId] = (desired.TryGetValue(itemId, out float current) ? current : 0f) + amount;
            }

            foreach (var kv in desired)
            {
                float availableToOwner = GetAvailable(kv.Key) + GetOwnerReservation(owner, kv.Key);
                if (availableToOwner < kv.Value - 0.0001f) return false;
            }

            if (desired.Count == 0) reservations.Remove(owner);
            else reservations[owner] = desired;
            return true;
        }

        public bool TryReserve(string owner, string itemId, float amount) =>
            TryReserveAll(owner, new[] { (itemId, amount) });

        public float GetOwnerReservation(string owner, string itemId) =>
            reservations.TryGetValue(owner, out var map) && map.TryGetValue(itemId, out float v) ? v : 0f;

        public IEnumerable<KeyValuePair<string, float>> GetOwnerReservations(string owner) =>
            reservations.TryGetValue(owner, out var map) ? map : (IEnumerable<KeyValuePair<string, float>>)System.Array.Empty<KeyValuePair<string, float>>();

        /// <summary>Permanently removes a reservation's items from stock (job completed successfully).</summary>
        public void ConsumeReservation(string owner)
        {
            if (!reservations.TryGetValue(owner, out var map)) return;
            foreach (var kv in map)
            {
                float left = GetStored(kv.Key) - kv.Value;
                if (left <= 0.0001f) stored.Remove(kv.Key); else stored[kv.Key] = left;
            }
            reservations.Remove(owner);
        }

        /// <summary>Consumes part of a reservation permanently (used by processing batches). Caller guarantees amount <= remaining reservation for that item.</summary>
        public void ConsumePartialReservation(string owner, string itemId, float amount)
        {
            if (amount <= 0f) return;
            if (!reservations.TryGetValue(owner, out var map) || !map.TryGetValue(itemId, out float reserved)) return;
            float taken = Mathf.Min(amount, reserved);
            float leftReserved = reserved - taken;
            if (leftReserved <= 0.0001f) map.Remove(itemId); else map[itemId] = leftReserved;
            if (map.Count == 0) reservations.Remove(owner);

            float leftStored = GetStored(itemId) - taken;
            if (leftStored <= 0.0001f) stored.Remove(itemId); else stored[itemId] = leftStored;
        }

        /// <summary>Releases a reservation without consuming — the items simply become available again (100% return).</summary>
        public void ReleaseReservation(string owner) => reservations.Remove(owner);

        /// <summary>Every owner id currently holding a reservation. Read-only; pair with <see cref="GetOwnerReservation"/>.</summary>
        public IEnumerable<string> ReservationOwners => reservations.Keys;

        /// <summary>
        /// Cancel path: permanently removes the full reservation from stock, then
        /// re-adds floor(amount * refundFraction) of each item back as fresh
        /// stock (guaranteed to fit — that exact volume was just freed). Returns
        /// what was refunded per item. Calling this twice for the same owner the
        /// second time is a safe no-op (nothing left to refund).
        /// </summary>
        public Dictionary<string, float> ConsumeReservationForRefund(string owner, float refundFraction)
        {
            var refunded = new Dictionary<string, float>();
            if (!reservations.TryGetValue(owner, out var map)) return refunded;

            foreach (var kv in new List<KeyValuePair<string, float>>(map))
            {
                string itemId = kv.Key;
                float reservedAmount = kv.Value;
                float left = GetStored(itemId) - reservedAmount;
                if (left <= 0.0001f) stored.Remove(itemId); else stored[itemId] = left;

                float refundAmount = Mathf.Floor(reservedAmount * refundFraction);
                if (refundAmount > 0f)
                {
                    float added = Add(itemId, refundAmount);
                    if (added > 0f) refunded[itemId] = added;
                }
            }
            reservations.Remove(owner);
            return refunded;
        }

        /// <summary>
        /// Consumes every listed item, or nothing at all (P1-ECO-016).
        /// <para>
        /// The multi-item equivalent of <see cref="TryConsumeImmediate"/>, for the
        /// several places that charged a cost list by looping over
        /// <c>TryRemove</c> and ignoring what each call answered. That loop can spend
        /// the first two lines of a three-line price and then fail the third, leaving
        /// the player charged for a build that did not happen; whether it did depended
        /// on the caller having done its own availability check first with a different
        /// method. The whole list is checked here, against the same availability the
        /// removal will use, before a single unit leaves the hold.
        /// </para>
        /// <para>
        /// Duplicate item ids in the list are summed rather than checked separately,
        /// for the same reason <see cref="TrySetReservation"/> sums them: two lines of
        /// 5 against a stock of 6 must fail, not pass twice.
        /// </para>
        /// </summary>
        public bool TryConsumeAllImmediate(IReadOnlyList<(string itemId, float amount)> costs)
        {
            if (costs == null || costs.Count == 0) return true;

            var required = new Dictionary<string, float>();
            for (int i = 0; i < costs.Count; i++)
            {
                string itemId = costs[i].itemId;
                float amount = costs[i].amount;
                if (string.IsNullOrEmpty(itemId) || amount <= 0f) continue;
                required[itemId] = (required.TryGetValue(itemId, out float current) ? current : 0f) + amount;
            }

            foreach (var kv in required)
                if (GetAvailable(kv.Key) < kv.Value - 0.0001f) return false;

            foreach (var kv in required)
            {
                float left = GetStored(kv.Key) - kv.Value;
                if (left <= 0.0001f) stored.Remove(kv.Key); else stored[kv.Key] = left;
            }
            return true;
        }
    }

    /// <summary>Thin MonoBehaviour holder for the ship's logistics cargo.</summary>
    public class ShipLogistics : MonoBehaviour
    {
        /// <summary>
        /// A brand-new campaign's cargo volume. MUST stay equal to
        /// <c>ModuleCatalog.CargoCapacityByTier[0]</c>: logistics_cargo is a free
        /// T1 module, so <c>ShipModules.ApplyFunctionalEffect</c> pushes the T1
        /// table entry into the hold on every new campaign AND every save load.
        /// If the two ever disagree, whatever is set here is silently overwritten
        /// by the table the first time that effect applies.
        /// (LogisticsCargoTests.BaseCapacity_MatchesTier1TableEntry guards this.)
        /// </summary>
        public const float BaseCapacity = 150f;

        /// <summary>Basic Building Parts a brand-new campaign starts with, so the first drill/structure is buildable without a processing run first.</summary>
        public const float StartingBasicParts = 50f;

        public LogisticsCargo Cargo { get; private set; }
        private void Awake() => Cargo = new LogisticsCargo(BaseCapacity);

        /// <summary>
        /// Seeds a fresh campaign's starting cargo. Called once by composition, mirroring
        /// ShipHangar/ShipModules/ShipCombat's own InitializeNewCampaign — never on load, which
        /// clears the cargo and restores the exact saved contents instead.
        /// </summary>
        public void InitializeNewCampaign()
        {
            if (Cargo == null) Cargo = new LogisticsCargo(BaseCapacity);
            Cargo.Clear();
            Cargo.Add(ProcessingCatalog.BasicPartId, StartingBasicParts);
        }
    }
}
