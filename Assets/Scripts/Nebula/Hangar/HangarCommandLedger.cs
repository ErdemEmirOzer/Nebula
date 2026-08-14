using UnityEngine;

namespace Nebula.Hangar
{
    /// <summary>
    /// Pure command-capacity counter (VS-007 §1): only launched/on-mission
    /// vehicles reserve a slot; vehicles sitting in the hangar never do.
    /// Capacity comes from the Hangar-Bot module tier (2 at T1, 4 at T2) and
    /// only ever grows (module tiers never downgrade in this slice).
    /// VS-015 §1: reservation cost is per-vehicle (Scout/Collector = 1,
    /// Engineer/Escort = 2) — <see cref="TryReserve"/>/<see cref="Release"/>
    /// take an explicit cost so only LAUNCHED vehicles' real command cost is
    /// ever counted, never a flat 1 regardless of class.
    /// </summary>
    public class HangarCommandLedger
    {
        public int Capacity { get; private set; }
        public int Used { get; private set; }

        public void SetCapacity(int capacity) => Capacity = Mathf.Max(0, capacity);

        public bool TryReserve(int cost = 1)
        {
            if (cost <= 0) return true;
            if (Used + cost > Capacity) return false;
            Used += cost;
            return true;
        }

        public void Release(int cost = 1)
        {
            if (cost <= 0) return;
            Used = Mathf.Max(0, Used - cost);
        }

        /// <summary>Restore-only: sets the exact used count from a save (never validated against a live launch).</summary>
        public void Restore(int used) => Used = Mathf.Max(0, used);
    }
}
