using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-014 §7: what one active Looter has stolen so far — a simple
    /// multi-item accumulator (unlike the single-item VehicleAgent Collector
    /// hold), capped by volume via the project's existing
    /// <see cref="CargoItemVolumes"/> table. Never a reservation system (a
    /// Looter's hold is never shared/contested the way LogisticsCargo is) —
    /// just add/withdraw/measure.
    /// </summary>
    public class LooterCargoState
    {
        private readonly Dictionary<string, float> items = new Dictionary<string, float>();
        public float Capacity { get; }

        public LooterCargoState(float capacity) { Capacity = capacity; }

        public IReadOnlyDictionary<string, float> Items => items;

        public float UsedVolume
        {
            get
            {
                float total = 0f;
                foreach (var kv in items) total += kv.Value * CargoItemVolumes.PerUnit(kv.Key);
                return total;
            }
        }

        public float AvailableVolume => Mathf.Max(0f, Capacity - UsedVolume);
        public bool IsFull => AvailableVolume <= 0.0001f;
        public float FillFraction01 => Capacity > 0f ? Mathf.Clamp01(UsedVolume / Capacity) : 1f;

        /// <summary>Adds up to however much volume remains, clamped by both the requested amount and the Looter's own remaining capacity; returns the amount actually added.</summary>
        public float Add(string itemId, float requestedAmount)
        {
            if (string.IsNullOrEmpty(itemId) || requestedAmount <= 0f) return 0f;
            float perUnit = CargoItemVolumes.PerUnit(itemId);
            float maxByVolume = perUnit > 0f ? AvailableVolume / perUnit : requestedAmount;
            float added = Mathf.Min(requestedAmount, maxByVolume);
            if (added <= 0f) return 0f;
            items[itemId] = items.TryGetValue(itemId, out float existing) ? existing + added : added;
            return added;
        }

        public void Clear() => items.Clear();

        public void RestoreFromSave(IEnumerable<(string itemId, float amount)> saved)
        {
            items.Clear();
            if (saved == null) return;
            foreach (var (itemId, amount) in saved) if (!string.IsNullOrEmpty(itemId) && amount > 0f) items[itemId] = amount;
        }
    }
}
