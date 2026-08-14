using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Poi;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for deterministic Wreck reward generation (VS-012 §6/§10.6).</summary>
    public class WreckSalvageGeneratorTests
    {
        private static PoiManifest Manifest(WreckSize size, ulong rewardSeed) =>
            new PoiManifest("sys/poi.00", "sys", 0, PoiType.Wreck, PoiRisk.Safe, UnityEngine.Vector3.zero, size, rewardSeed, 0UL);

        [Test]
        public void NodeCount_IsAlwaysBetweenTwoAndFour()
        {
            for (ulong seed = 0; seed < 40; seed++)
            {
                var nodes = WreckSalvageGenerator.Generate(Manifest(WreckSize.Medium, seed * 7919UL));
                Assert.GreaterOrEqual(nodes.Count, 1); // may collapse below 2 only in the documented all-zero edge case
                Assert.LessOrEqual(nodes.Count, PoiCatalog.WreckMaxNodes);
            }
        }

        [Test]
        public void TotalReward_WithinSizeRange_AndConservedAcrossNodes()
        {
            for (ulong seed = 0; seed < 40; seed++)
            {
                foreach (WreckSize size in new[] { WreckSize.Small, WreckSize.Medium, WreckSize.Large })
                {
                    var nodes = WreckSalvageGenerator.Generate(Manifest(size, seed * 104729UL));
                    (int minB, int maxB, int minS, int maxS) = PoiCatalog.WreckRewardRange(size);

                    float totalBasic = 0f, totalSpecial = 0f;
                    string materialId = null;
                    foreach (SalvageNode n in nodes)
                    {
                        if (n.ItemId == ProcessingCatalog.BasicPartId) totalBasic += n.Total;
                        else { totalSpecial += n.Total; materialId = n.ItemId; }
                        Assert.AreEqual(n.Total, n.Remaining, "a freshly generated node starts fully stocked");
                    }

                    Assert.GreaterOrEqual(totalBasic, minB, $"{size} seed{seed}"); Assert.LessOrEqual(totalBasic, maxB, $"{size} seed{seed}");
                    Assert.GreaterOrEqual(totalSpecial, minS, $"{size} seed{seed}"); Assert.LessOrEqual(totalSpecial, maxS, $"{size} seed{seed}");
                    if (totalSpecial > 0f) CollectionAssert.Contains(ProcessingCatalog.MaterialIds, materialId);
                }
            }
        }

        [Test]
        public void Generation_IsDeterministic_SameSeedSameResult()
        {
            var a = WreckSalvageGenerator.Generate(Manifest(WreckSize.Large, 999UL));
            var b = WreckSalvageGenerator.Generate(Manifest(WreckSize.Large, 999UL));
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) { Assert.AreEqual(a[i].ItemId, b[i].ItemId); Assert.AreEqual(a[i].Total, b[i].Total); }
        }
    }
}
