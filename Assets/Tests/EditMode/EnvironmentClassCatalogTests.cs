using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the environment-class catalog and its weighted pick (VS-009 §3/§10.1).</summary>
    public class EnvironmentClassCatalogTests
    {
        [Test]
        public void CatalogValues_MatchTheSpec()
        {
            Assert.AreEqual(60f, EnvironmentClassCatalog.Weight(EnvironmentClass.Calm));
            Assert.AreEqual(30f, EnvironmentClassCatalog.Weight(EnvironmentClass.Harsh));
            Assert.AreEqual(10f, EnvironmentClassCatalog.Weight(EnvironmentClass.Extreme));

            Assert.AreEqual(1f, EnvironmentClassCatalog.DurationMultiplier(EnvironmentClass.Calm));
            Assert.AreEqual(1.15f, EnvironmentClassCatalog.DurationMultiplier(EnvironmentClass.Harsh));
            Assert.AreEqual(1.30f, EnvironmentClassCatalog.DurationMultiplier(EnvironmentClass.Extreme));
        }

        [Test]
        public void Pick_LargeSample_MatchesSixtyThirtyTenWeights()
        {
            var counts = new Dictionary<EnvironmentClass, int>();
            const int total = 20000;
            for (int i = 0; i < total; i++)
            {
                EnvironmentClass e = EnvironmentClassCatalog.Pick((ulong)i * 65537UL, "sector.0.0.0/system.000/planet." + (i % 97).ToString("00"));
                counts[e] = counts.TryGetValue(e, out int n) ? n + 1 : 1;
            }

            float calmFrac = counts.GetValueOrDefault(EnvironmentClass.Calm) / (float)total;
            float harshFrac = counts.GetValueOrDefault(EnvironmentClass.Harsh) / (float)total;
            float extremeFrac = counts.GetValueOrDefault(EnvironmentClass.Extreme) / (float)total;

            Assert.AreEqual(0.60f, calmFrac, 0.02f, "Calm");
            Assert.AreEqual(0.30f, harshFrac, 0.02f, "Harsh");
            Assert.AreEqual(0.10f, extremeFrac, 0.015f, "Extreme");
        }
    }
}
