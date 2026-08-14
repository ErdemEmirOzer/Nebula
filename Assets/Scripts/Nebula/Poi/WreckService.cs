using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Poi
{
    public enum WreckCollectRejectReason { None, NotAccessible, UnstableNotAllowedForCollector }

    /// <summary>Pure Wreck identify/collection logic (VS-012 §6).</summary>
    public static class WreckService
    {
        /// <summary>150 XP exactly once, on the Identified transition (never re-fired by later salvage).</summary>
        public static void OnIdentified(PoiState state, string poiId)
        {
            if (state.WreckXpAwarded) return;
            Progression.ProgressionService.Award("poi.wreck.identify." + poiId, PoiCatalog.WreckXp);
            state.MarkWreckXpAwarded();
        }

        public static WreckCollectRejectReason CheckCollect(PoiState state, PoiRisk risk, bool byCollector)
        {
            if (state.Level != PoiKnowledgeLevel.Identified && state.Level != PoiKnowledgeLevel.Active) return WreckCollectRejectReason.NotAccessible;
            if (byCollector && risk == PoiRisk.Unstable) return WreckCollectRejectReason.UnstableNotAllowedForCollector;
            return WreckCollectRejectReason.None;
        }

        /// <summary>
        /// Withdraws from whichever salvage node still has stock into a
        /// single-item hold (vehicle collector or the ship's own transient
        /// hold), bounded by rate*dt and the receiver's free volume — the
        /// same discipline as <see cref="Hangar.ResourceTransferService.CollectFromDrill"/>.
        /// Returns the amount actually taken this call (0 if nothing fits/available).
        /// </summary>
        public static float Withdraw(PoiState state, ref string heldItemId, ref float heldAmount, float holdCapacityVolume,
            float ratePerSecond, float dt)
        {
            if (state == null || dt <= 0f) return 0f;
            SalvageNode node = FindNodeWithStock(state, heldAmount > 0.0001f ? heldItemId : null);
            if (node == null) return 0f;

            float perUnitVolume = Mathf.Max(0.0001f, CargoItemVolumes.PerUnit(node.ItemId));
            float usedVolume = heldAmount * perUnitVolume;
            float freeVolume = Mathf.Max(0f, holdCapacityVolume - usedVolume);
            float maxUnitsByVolume = freeVolume / perUnitVolume;

            float want = Mathf.Min(ratePerSecond * dt, maxUnitsByVolume, node.Remaining);
            if (want <= 0f) return 0f;

            node.Remaining -= want;
            heldItemId = node.ItemId;
            heldAmount += want;
            return want;
        }

        private static SalvageNode FindNodeWithStock(PoiState state, string preferItemId)
        {
            if (preferItemId != null)
                foreach (SalvageNode n in state.SalvageNodes) if (n.ItemId == preferItemId && n.Remaining > 0.0001f) return n;
            foreach (SalvageNode n in state.SalvageNodes) if (n.Remaining > 0.0001f) return n;
            return null;
        }

        /// <summary>Call after any withdrawal — transitions to Resolved (Depleted) exactly once, all nodes empty.</summary>
        public static bool CheckDepletedAndResolve(PoiState state)
        {
            if (state.Level == PoiKnowledgeLevel.Resolved) return false;
            if (!state.Depleted) return false;
            state.Resolve();
            return true;
        }
    }
}
