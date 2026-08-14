using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Poi
{
    /// <summary>
    /// Pure, deterministic Wreck reward generation (VS-012 §6): total
    /// part.basic + one seed-stable special material (from
    /// <see cref="ProcessingCatalog.MaterialIds"/>) inside the size's exact
    /// range, split conservation-exactly across 2-4 salvage nodes.
    /// </summary>
    public static class WreckSalvageGenerator
    {
        public static List<SalvageNode> Generate(PoiManifest manifest)
        {
            ulong seed = manifest.RewardSeed;
            (int minBasic, int maxBasic, int minSpecial, int maxSpecial) = PoiCatalog.WreckRewardRange(manifest.Size);

            int totalBasic = RangeRoll(seed, "basic", minBasic, maxBasic);
            int totalSpecial = RangeRoll(seed, "special", minSpecial, maxSpecial);
            string materialId = ProcessingCatalog.MaterialIds[Mathf.FloorToInt(Unit(seed, "material") * ProcessingCatalog.MaterialIds.Length) % ProcessingCatalog.MaterialIds.Length];

            int nodeCount = Mathf.Clamp(PoiCatalog.WreckMinNodes + Mathf.FloorToInt(Unit(seed, "nodecount") * (PoiCatalog.WreckMaxNodes - PoiCatalog.WreckMinNodes + 1)),
                PoiCatalog.WreckMinNodes, PoiCatalog.WreckMaxNodes);

            // nodeCount is the TOTAL physical node budget for this Wreck -- it must be
            // split BETWEEN the basic and special item streams (never applied to each
            // stream independently, which would double it up to 2x nodeCount).
            int basicNodeCount, specialNodeCount;
            if (totalBasic <= 0) { basicNodeCount = 0; specialNodeCount = nodeCount; }
            else if (totalSpecial <= 0) { basicNodeCount = nodeCount; specialNodeCount = 0; }
            else
            {
                basicNodeCount = Mathf.Clamp(Mathf.RoundToInt(nodeCount * (0.35f + Unit(seed, "basicshare") * 0.3f)), 1, nodeCount - 1);
                specialNodeCount = nodeCount - basicNodeCount;
            }

            var nodes = new List<SalvageNode>(nodeCount);
            List<int> basicSplit = SplitConserved(totalBasic, basicNodeCount, seed, "basicsplit");
            List<int> specialSplit = SplitConserved(totalSpecial, specialNodeCount, seed, "specialsplit");

            foreach (int share in basicSplit) if (share > 0) nodes.Add(new SalvageNode(ProcessingCatalog.BasicPartId, share));
            foreach (int share in specialSplit) if (share > 0) nodes.Add(new SalvageNode(materialId, share));
            // Every node must carry something; a size/split combination that left a slot with
            // 0 of both (rare, only possible with small totals and node counts) collapses to
            // fewer physical nodes rather than ever creating an empty one -- total node COUNT
            // in the 2-4 range is a target, not a hard multiply-resources guarantee.
            if (nodes.Count == 0) nodes.Add(new SalvageNode(ProcessingCatalog.BasicPartId, Mathf.Max(1, totalBasic)));
            return nodes;
        }

        /// <summary>Deterministically splits an integer total into `count` non-negative parts that sum EXACTLY to the total.</summary>
        private static List<int> SplitConserved(int total, int count, ulong seed, string tag)
        {
            var weights = new List<float>(count);
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                float w = 0.4f + Unit(seed, tag + i) * 0.6f; // never a zero-weight slot
                weights.Add(w);
                sum += w;
            }

            var parts = new List<int>(count);
            int assigned = 0;
            for (int i = 0; i < count; i++)
            {
                int share = Mathf.FloorToInt(total * weights[i] / sum);
                parts.Add(share);
                assigned += share;
            }
            // Deterministic remainder distribution (never random): hand any leftover units,
            // one at a time, to the earliest slots until the split sums exactly to total.
            int remainder = total - assigned;
            for (int i = 0; remainder > 0 && i < count; i++, remainder--) parts[i]++;
            return parts;
        }

        private static int RangeRoll(ulong seed, string tag, int min, int max) =>
            min + Mathf.FloorToInt(Unit(seed, tag) * (max - min + 1) * 0.999999f);

        private static float Unit(ulong seed, string tag) => ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, "wreck/" + tag));
    }
}
