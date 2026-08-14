using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;

namespace Nebula.Combat
{
    public enum LootSourceKind { Drill, Vehicle, LogisticsCenter }

    /// <summary>One resolved loot target — a stable id (for canonical tie-break/persistence) plus a physical position to travel toward.</summary>
    public readonly struct LootTarget
    {
        public readonly LootSourceKind Kind;
        public readonly string TargetId;
        public readonly Vector3 Position;
        public LootTarget(LootSourceKind kind, string targetId, Vector3 position) { Kind = kind; TargetId = targetId; Position = position; }
    }

    /// <summary>
    /// VS-014 §7: the single transaction authority for Looter theft —
    /// target priority (fullest active drill → heaviest-cargo Collector →
    /// System Logistics Center, canonical id tie-break), the stealable-item
    /// whitelist (never nuclear energy/core, never a reserved/pending
    /// amount, never the ship's own cargo), and the exact same-tick
    /// source-decreases/Looter-cargo-increases conservation every theft tick
    /// guarantees. Reuses the existing Drill/Vehicle/LogisticsCenter cargo
    /// APIs directly — no parallel inventory system, no EnemyAgent-side
    /// inventory bookkeeping.
    /// </summary>
    public static class LooterCargoService
    {
        private static readonly HashSet<string> stealableItems = BuildStealableItems();

        private static HashSet<string> BuildStealableItems()
        {
            var set = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (string id in ExtractionCatalog.ResourceIds) set.Add(id);
            set.Add(ProcessingCatalog.BasicPartId);
            foreach (string id in ProcessingCatalog.MaterialIds) set.Add(id);
            return set;
        }

        public static bool IsStealable(string itemId) => itemId != null && stealableItems.Contains(itemId);

        public static bool ShouldOptionalRetreat(LooterCargoState cargo) => cargo != null && cargo.FillFraction01 >= 0.5f;
        public static bool MustRetreat(LooterCargoState cargo) => cargo == null || cargo.IsFull;

        /// <summary>
        /// The stable identity of one drill (P1-WORLD-007).
        /// <para>
        /// A drill target used to be identified by its planet's <c>worldId</c> alone,
        /// and the Looter then stole from the first drill it found with that
        /// <c>worldId</c> — a different question from the one selection answered.
        /// The two coincide while a planet carries one drill on one node, which is what
        /// the live build path produces; they stop coinciding the moment anything puts
        /// a second drill record on a planet, and then the Looter selects the fullest
        /// drill and robs whichever one the list happens to hold first.
        /// </para>
        /// <para>
        /// The pairing is <c>worldId/nodeId</c> — the same identity
        /// <c>SurfaceStructure.TargetId</c>, <c>DrillUpgradeService.OwnerId</c> and the
        /// remote structure lookup already use, so a drill is addressed one way
        /// everywhere rather than one way per caller.
        /// </para>
        /// </summary>
        public static string DrillTargetId(DrillStructure drill) =>
            drill == null ? null : drill.WorldId + "/" + drill.NodeId;

        /// <summary>Resolves a drill target id back to the exact drill it named, or null if that drill is gone.</summary>
        public static DrillStructure FindDrillByTargetId(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return null;
            foreach (DrillStructure d in ExtractionState.Drills)
                if (d != null && DrillTargetId(d) == targetId) return d;
            return null;
        }

        /// <summary>
        /// Fullest active drill (by stored-fraction-of-capacity) → heaviest-
        /// currently-held active Collector → the system's Logistics Center
        /// (if it has any available stealable inventory) → null (nothing to
        /// steal). Ties broken by the lower canonical stable id, ascending.
        /// </summary>
        public static LootTarget? SelectTarget(string worldSystemId, IReadOnlyList<VehicleAgent> vehicles)
        {
            DrillStructure bestDrill = null; float bestDrillFraction = -1f; string bestDrillId = null;
            foreach (DrillStructure d in ExtractionState.Drills)
            {
                if (d == null || d.Runtime.State != DrillLifecycle.Active || d.Runtime.StoredAmount <= 0.0001f) continue;
                float frac = d.Runtime.StorageCapacity > 0f ? d.Runtime.StoredAmount / d.Runtime.StorageCapacity : 0f;
                string id = DrillTargetId(d);
                // Tie-break on the same identity the target carries, so "the lower
                // canonical id wins" and "the drill we then steal from" cannot disagree.
                if (frac > bestDrillFraction || (Mathf.Approximately(frac, bestDrillFraction) && string.CompareOrdinal(id, bestDrillId ?? "￿") < 0))
                { bestDrillFraction = frac; bestDrill = d; bestDrillId = id; }
            }
            if (bestDrill != null) return new LootTarget(LootSourceKind.Drill, bestDrillId, bestDrill.transform.position);

            VehicleAgent bestVehicle = null; float bestVehicleAmount = -1f;
            if (vehicles != null)
            {
                foreach (VehicleAgent v in vehicles)
                {
                    if (v == null || !v.gameObject.activeSelf || v.Def.Class != VehicleClass.Collector) continue;
                    if (string.IsNullOrEmpty(v.HeldItemId) || v.HeldAmount <= 0.0001f || !IsStealable(v.HeldItemId)) continue;
                    if (v.HeldAmount > bestVehicleAmount || (Mathf.Approximately(v.HeldAmount, bestVehicleAmount) && string.CompareOrdinal(v.VehicleId, bestVehicle?.VehicleId ?? "￿") < 0))
                    { bestVehicleAmount = v.HeldAmount; bestVehicle = v; }
                }
            }
            if (bestVehicle != null) return new LootTarget(LootSourceKind.Vehicle, bestVehicle.VehicleId, bestVehicle.transform.position);

            LogisticsCenter center = LogisticsState.FindCenter(worldSystemId);
            if (center != null && center.Runtime.IsActive && HasAnyStealable(center.Inventory))
                return new LootTarget(LootSourceKind.LogisticsCenter, center.TargetId, center.transform.position);

            return null;
        }

        private static bool HasAnyStealable(LogisticsCargo cargo)
        {
            if (cargo == null) return false;
            foreach (var kv in cargo.Items) if (IsStealable(kv.Key) && cargo.GetAvailable(kv.Key) > 0.0001f) return true;
            return false;
        }

        /// <summary>Withdraws up to ratePerSecond*dt from the drill's single product item (its planet's own resource family), respecting the Looter's remaining capacity. Returns the amount actually stolen.</summary>
        public static float StealFromDrill(LooterCargoState cargo, DrillStructure drill, float ratePerSecond, float dt)
        {
            if (cargo == null || drill == null || drill.Runtime.State != DrillLifecycle.Active) return 0f;
            PlanetProfile profile = ExtractionState.GetProfile(drill.WorldId);
            if (profile == null) return 0f;
            string itemId = ExtractionCatalog.ResourceIds[(int)profile.Family];
            float budget = ratePerSecond * dt;
            float requested = Mathf.Min(budget, drill.Runtime.StoredAmount);
            if (requested <= 0f) return 0f;
            float actuallyAddable = cargo.AvailableVolume / Mathf.Max(0.0001f, CargoItemVolumes.PerUnit(itemId));
            float take = Mathf.Min(requested, actuallyAddable);
            if (take <= 0f) return 0f;
            float withdrawn = drill.Runtime.Withdraw(take);
            return cargo.Add(itemId, withdrawn);
        }

        /// <summary>Withdraws from a Collector's single held-item slot — same-tick conservation (the vehicle's HeldAmount decreases by exactly what the Looter's cargo increases by).</summary>
        public static float StealFromVehicle(LooterCargoState cargo, VehicleAgent collector, float ratePerSecond, float dt)
        {
            if (cargo == null || collector == null || string.IsNullOrEmpty(collector.HeldItemId) || collector.HeldAmount <= 0f) return 0f;
            float budget = ratePerSecond * dt;
            float requested = Mathf.Min(budget, collector.HeldAmount);
            if (requested <= 0f) return 0f;
            float actuallyAddable = cargo.AvailableVolume / Mathf.Max(0.0001f, CargoItemVolumes.PerUnit(collector.HeldItemId));
            float take = Mathf.Min(requested, actuallyAddable);
            if (take <= 0f) return 0f;
            collector.HeldAmount -= take;
            float added = cargo.Add(collector.HeldItemId, take);
            if (collector.HeldAmount <= 0.0001f) { collector.HeldAmount = 0f; collector.HeldItemId = null; }
            return added;
        }

        /// <summary>Withdraws stealable, non-reserved inventory from the Logistics Center, one item id at a time each tick (never nuclear energy/core, never a reserved amount).</summary>
        public static float StealFromLogisticsCenter(LooterCargoState cargo, LogisticsCenter center, float ratePerSecond, float dt)
        {
            if (cargo == null || center == null || !center.Runtime.IsActive) return 0f;
            float budget = ratePerSecond * dt;
            float stolenThisTick = 0f;
            foreach (var kv in new List<KeyValuePair<string, float>>(center.Inventory.Items))
            {
                if (budget <= 0f) break;
                if (!IsStealable(kv.Key)) continue;
                float available = center.Inventory.GetAvailable(kv.Key);
                if (available <= 0.0001f) continue;
                float requested = Mathf.Min(budget, available);
                float actuallyAddable = cargo.AvailableVolume / Mathf.Max(0.0001f, CargoItemVolumes.PerUnit(kv.Key));
                float take = Mathf.Min(requested, actuallyAddable);
                if (take <= 0f) continue;
                bool removed = center.Inventory.TryConsumeImmediate(kv.Key, take);
                if (!removed) continue;
                float added = cargo.Add(kv.Key, take);
                stolenThisTick += added;
                budget -= take;
            }
            return stolenThisTick;
        }

        /// <summary>VS-014 §7: a destroyed Looter converts 100% of what it was carrying into physical wreckage nodes — never lost, never duplicated.</summary>
        public static List<CombatWreckageNode> ToWreckageNodes(LooterCargoState cargo)
        {
            var nodes = new List<CombatWreckageNode>();
            if (cargo == null) return nodes;
            foreach (var kv in cargo.Items) if (kv.Value > 0f) nodes.Add(new CombatWreckageNode(kv.Key, kv.Value));
            return nodes;
        }
    }
}
