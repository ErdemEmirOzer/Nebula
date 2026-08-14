using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the typed star-class catalog and its weighted pick (VS-009 §2/§10.1).</summary>
    public class StarClassCatalogTests
    {
        [Test]
        public void CatalogValues_MatchTheSpec()
        {
            Assert.AreEqual(40f, StarClassCatalog.Weight(StarClass.RedDwarf));
            Assert.AreEqual(35f, StarClassCatalog.Weight(StarClass.Yellow));
            Assert.AreEqual(20f, StarClassCatalog.Weight(StarClass.White));
            Assert.AreEqual(5f, StarClassCatalog.Weight(StarClass.BlueGiant));

            Assert.AreEqual(0.75f, StarClassCatalog.EnergyMultiplier(StarClass.RedDwarf));
            Assert.AreEqual(1f, StarClassCatalog.EnergyMultiplier(StarClass.Yellow));
            Assert.AreEqual(1.25f, StarClassCatalog.EnergyMultiplier(StarClass.White));
            Assert.AreEqual(1.5f, StarClassCatalog.EnergyMultiplier(StarClass.BlueGiant));

            Assert.AreEqual(1.35f, StarClassCatalog.CriticalRadiusMultiplier(StarClass.RedDwarf));
            Assert.AreEqual(1.50f, StarClassCatalog.CriticalRadiusMultiplier(StarClass.Yellow));
            Assert.AreEqual(1.75f, StarClassCatalog.CriticalRadiusMultiplier(StarClass.White));
            Assert.AreEqual(2f, StarClassCatalog.CriticalRadiusMultiplier(StarClass.BlueGiant));

            Assert.AreEqual(2.25f, StarClassCatalog.WarningRadiusMultiplier(StarClass.RedDwarf));
            Assert.AreEqual(2.75f, StarClassCatalog.WarningRadiusMultiplier(StarClass.Yellow));
            Assert.AreEqual(3.25f, StarClassCatalog.WarningRadiusMultiplier(StarClass.White));
            Assert.AreEqual(4f, StarClassCatalog.WarningRadiusMultiplier(StarClass.BlueGiant));
        }

        [Test]
        public void Pick_LargeSample_MatchesFortyThirtyFiveTwentyFiveWeights()
        {
            var counts = new Dictionary<StarClass, int>();
            const int total = 20000;
            for (int i = 0; i < total; i++)
            {
                StarClass c = StarClassCatalog.Pick((ulong)i * 104729UL, "sector.0.0.0/system." + i.ToString("000"));
                counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;
            }

            float redFrac = counts.GetValueOrDefault(StarClass.RedDwarf) / (float)total;
            float yellowFrac = counts.GetValueOrDefault(StarClass.Yellow) / (float)total;
            float whiteFrac = counts.GetValueOrDefault(StarClass.White) / (float)total;
            float blueFrac = counts.GetValueOrDefault(StarClass.BlueGiant) / (float)total;

            Assert.AreEqual(0.40f, redFrac, 0.02f, "RedDwarf");
            Assert.AreEqual(0.35f, yellowFrac, 0.02f, "Yellow");
            Assert.AreEqual(0.20f, whiteFrac, 0.02f, "White");
            Assert.AreEqual(0.05f, blueFrac, 0.015f, "BlueGiant");
        }

        [Test]
        public void Pick_IsDeterministic_SameSeedSameSystemId_AlwaysSameClass()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                StarClass a = StarClassCatalog.Pick(seed, "sector.0.0.0/system.000");
                StarClass b = StarClassCatalog.Pick(seed, "sector.0.0.0/system.000");
                Assert.AreEqual(a, b, $"seed {seed}");
            }
        }

        [Test]
        public void PickNonBlueGiant_NeverReturnsBlueGiant()
        {
            for (ulong seed = 0; seed < 500; seed++)
            {
                StarClass c = StarClassCatalog.PickNonBlueGiant(seed, "sector.0.0.0/system." + seed);
                Assert.AreNotEqual(StarClass.BlueGiant, c, $"seed {seed}");
            }
        }
    }
}
