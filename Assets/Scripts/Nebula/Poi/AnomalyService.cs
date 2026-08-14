using System.Collections.Generic;
using Nebula.Navigation;

namespace Nebula.Poi
{
    /// <summary>Pure Anomaly completion logic (VS-012 §5).</summary>
    public static class AnomalyService
    {
        public static float RequiredSeconds(bool byScout, PoiRisk risk)
        {
            if (byScout) return risk == PoiRisk.Unstable ? PoiCatalog.AnomalyScoutUnstableSeconds : PoiCatalog.AnomalyScoutSafeSeconds;
            return risk == PoiRisk.Unstable ? PoiCatalog.AnomalyShipUnstableSeconds : PoiCatalog.AnomalyShipSafeSeconds;
        }

        /// <summary>
        /// Completes an Active Anomaly: awards 250 XP exactly once (ledger-guarded — if ship
        /// and Scout somehow both call this the same tick, the second call's Award is a
        /// no-op duplicate) and opens one Approximate Signal from the pre-filtered 4th-8th
        /// nearest still-Unknown candidate list the caller supplies (no candidate -&gt; XP only,
        /// never a fake system/signal). Returns false if the POI wasn't Active.
        /// </summary>
        public static bool TryComplete(PoiState state, string poiId, List<(string systemId, float distance)> sortedUnknownCandidates,
            SystemKnowledgeService knowledge, FrontierSignalService frontierSignal)
        {
            if (state.Level != PoiKnowledgeLevel.Active) return false;

            Progression.ProgressionService.Award("poi.anomaly.xp." + poiId, PoiCatalog.AnomalyXp);
            state.MarkAnomalyXpAwarded();

            var slice = new List<(string systemId, float distance)>();
            if (sortedUnknownCandidates != null)
                for (int i = PoiCatalog.AnomalySignalNearestIndexStart; i <= PoiCatalog.AnomalySignalNearestIndexEnd && i < sortedUnknownCandidates.Count; i++)
                    slice.Add(sortedUnknownCandidates[i]);

            frontierSignal?.TryOpenOneShot(poiId, slice, knowledge, PoiCatalog.AnomalySignalCount);
            state.Resolve();
            return true;
        }
    }
}
