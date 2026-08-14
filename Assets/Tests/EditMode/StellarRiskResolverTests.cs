using NUnit.Framework;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure distance-to-star risk classification (VS-009 §2/§10.4).</summary>
    public class StellarRiskResolverTests
    {
        [Test]
        public void Safe_AtRestingDistance_AllMultipliersAreOne_HeatZero_CruiseAllowed()
        {
            StellarRiskState s = StellarRiskResolver.Resolve(distance: 100000f, starRadius: 8000f, StarClass.Yellow);
            Assert.AreEqual(StellarRiskLevel.Safe, s.Level);
            Assert.AreEqual(1f, s.ShieldRegenMultiplier);
            Assert.AreEqual(1f, s.CoolingMultiplier);
            Assert.AreEqual(1f, s.CombatEnergyMultiplier);
            Assert.AreEqual(0f, s.EnvironmentalHeatPerSecondFractionOfMax);
            Assert.IsTrue(s.CruiseAllowed);
        }

        [Test]
        public void Warning_ShieldRegenZero_CoolingAndCombatEnergyPoint75_HeatZero_CruiseBlocked()
        {
            float starRadius = 8000f;
            // Yellow: critical <=1.50R, warning <=2.75R — pick a distance strictly between.
            float distance = starRadius * 2.0f;
            StellarRiskState s = StellarRiskResolver.Resolve(distance, starRadius, StarClass.Yellow);
            Assert.AreEqual(StellarRiskLevel.Warning, s.Level);
            Assert.AreEqual(0f, s.ShieldRegenMultiplier);
            Assert.AreEqual(0.75f, s.CoolingMultiplier);
            Assert.AreEqual(0.75f, s.CombatEnergyMultiplier);
            Assert.AreEqual(0f, s.EnvironmentalHeatPerSecondFractionOfMax);
            Assert.IsFalse(s.CruiseAllowed);
        }

        [Test]
        public void Critical_ShieldRegenZero_CoolingAndEnergyPoint40_HeatTwoPercent_CruiseBlocked()
        {
            float starRadius = 8000f;
            float distance = starRadius * 1.0f; // inside every class's Critical radius
            StellarRiskState s = StellarRiskResolver.Resolve(distance, starRadius, StarClass.Yellow);
            Assert.AreEqual(StellarRiskLevel.Critical, s.Level);
            Assert.AreEqual(0f, s.ShieldRegenMultiplier);
            Assert.AreEqual(0.40f, s.CoolingMultiplier);
            Assert.AreEqual(0.40f, s.CombatEnergyMultiplier);
            Assert.AreEqual(0.02f, s.EnvironmentalHeatPerSecondFractionOfMax);
            Assert.IsFalse(s.CruiseAllowed);
        }

        [TestCase(StarClass.RedDwarf, 1.35f, 2.25f)]
        [TestCase(StarClass.Yellow, 1.50f, 2.75f)]
        [TestCase(StarClass.White, 1.75f, 3.25f)]
        [TestCase(StarClass.BlueGiant, 2.00f, 4.00f)]
        public void RadiusBoundaries_ExactlyMatchPerClassMultipliers(StarClass starClass, float criticalMul, float warningMul)
        {
            const float starRadius = 8000f;
            float criticalRadius = starRadius * criticalMul;
            float warningRadius = starRadius * warningMul;

            Assert.AreEqual(StellarRiskLevel.Critical, StellarRiskResolver.Resolve(criticalRadius - 1f, starRadius, starClass).Level);
            Assert.AreEqual(StellarRiskLevel.Critical, StellarRiskResolver.Resolve(criticalRadius, starRadius, starClass).Level);
            Assert.AreEqual(StellarRiskLevel.Warning, StellarRiskResolver.Resolve(criticalRadius + 1f, starRadius, starClass).Level);
            Assert.AreEqual(StellarRiskLevel.Warning, StellarRiskResolver.Resolve(warningRadius, starRadius, starClass).Level);
            Assert.AreEqual(StellarRiskLevel.Safe, StellarRiskResolver.Resolve(warningRadius + 1f, starRadius, starClass).Level);
        }

        [Test]
        public void FirstPlanetOrbit_AtFourPointFiveR_IsAlwaysSafe_ForEveryStarClass()
        {
            // VS-008/VS-009: SystemCompositionGenerator's first-orbit floor is
            // 4.5R, and the widest Warning radius (BlueGiant) is only 4R —
            // so the guarantee holds structurally for every star class.
            const float starRadius = 8000f;
            float firstOrbit = starRadius * 4.5f;
            foreach (StarClass c in new[] { StarClass.RedDwarf, StarClass.Yellow, StarClass.White, StarClass.BlueGiant })
                Assert.AreEqual(StellarRiskLevel.Safe, StellarRiskResolver.Resolve(firstOrbit, starRadius, c).Level, c.ToString());
        }
    }
}
