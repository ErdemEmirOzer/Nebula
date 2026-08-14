using System.Collections.Generic;

namespace Nebula.Poi
{
    /// <summary>
    /// Pure, save-friendly ordered record of every FOREIGN (non-start)
    /// system's first physical visit (VS-012 §2/§10) — "physical visit"
    /// means the ship actually entered the system's navigation radius and it
    /// materialized Full, independent of whether the player ever bothered to
    /// Locate it first (matching the literal "fiziksel ziyaret" wording, not
    /// <see cref="Navigation.SystemKnowledgeService"/>'s Visited level, which
    /// has a pre-existing gap: a system can go Full without ever reaching
    /// Visited if it was never Located). The ordinal position (0-based) is
    /// the single source of truth both POI guarantees key off:
    /// ordinal 0 = the very first foreign system ever visited (guaranteed a
    /// Safe Wreck); ordinal 2, 5, 8... = the system that COMPLETES a
    /// consecutive group of three first-visits (guaranteed >=1 POI across
    /// the group). Recording is idempotent (a system already in the ledger
    /// is never re-appended or reordered), so replaying
    /// <see cref="PoiManifestGenerator"/> against a stable, already-complete
    /// ledger always reproduces the exact same guarantee decisions — no
    /// separate persisted "did this system get a guarantee" flag is needed.
    /// </summary>
    public class PoiVisitLedger
    {
        private readonly List<string> order = new List<string>();
        private readonly HashSet<string> seen = new HashSet<string>();

        public IReadOnlyList<string> VisitOrder => order;
        public int Count => order.Count;

        /// <summary>Records a first visit; returns false (no-op) if this system was already recorded.</summary>
        public bool RecordVisit(string systemId)
        {
            if (string.IsNullOrEmpty(systemId) || !seen.Add(systemId)) return false;
            order.Add(systemId);
            return true;
        }

        /// <summary>0-based first-visit ordinal, or -1 if this system has never been recorded.</summary>
        public int OrdinalOf(string systemId)
        {
            if (string.IsNullOrEmpty(systemId)) return -1;
            for (int i = 0; i < order.Count; i++) if (order[i] == systemId) return i;
            return -1;
        }

        /// <summary>The (up to) three systemIds sharing this ordinal's consecutive visit group — empty entries omitted only if the ledger hasn't grown that far yet.</summary>
        public List<string> GroupMembers(int ordinal)
        {
            var result = new List<string>(3);
            if (ordinal < 0) return result;
            int groupStart = (ordinal / 3) * 3;
            for (int i = groupStart; i < groupStart + 3 && i < order.Count; i++) result.Add(order[i]);
            return result;
        }

        public void Clear() { order.Clear(); seen.Clear(); }

        /// <summary>Restore-only: writes the exact saved order, no re-validation, no guarantee replay (that happens lazily on next PoiManifestGenerator.Resolve call).</summary>
        public void RestoreOrder(IEnumerable<string> savedOrder)
        {
            Clear();
            if (savedOrder == null) return;
            foreach (string id in savedOrder) RecordVisit(id);
        }
    }
}
