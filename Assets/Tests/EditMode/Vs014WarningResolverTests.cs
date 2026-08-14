using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-014 §10/§18: warning duration formula, Probe pending-effect discount, new-class priority, Scout bonus, cap.</summary>
    public class Vs014WarningResolverTests
    {
        [Test]
        public void Resolve_Base_Is90Seconds()
        {
            float w = WarningResolver.Resolve(false, false, 0f, false, 0);
            Assert.AreEqual(90f, w, 0.001f);
        }

        [Test]
        public void Resolve_ProbePendingEffect_ShortensTo60Seconds()
        {
            float w = WarningResolver.Resolve(false, true, 0f, false, 0);
            Assert.AreEqual(60f, w, 0.001f);
        }

        [Test]
        public void Resolve_NewClassFirstAppearance_Always150_EvenWithProbeEffectActive()
        {
            float w = WarningResolver.Resolve(true, true, 0f, false, 0);
            Assert.AreEqual(150f, w, 0.001f);
        }

        [Test]
        public void Resolve_DifficultyOffsetApplied()
        {
            Assert.AreEqual(120f, WarningResolver.Resolve(false, false, 30f, false, 0), 0.001f); // Explorer +30
            Assert.AreEqual(75f, WarningResolver.Resolve(false, false, -15f, false, 0), 0.001f); // Conqueror -15
        }

        [Test]
        public void Resolve_ScoutMk1Bonus_Adds15Seconds()
        {
            float withoutScout = WarningResolver.Resolve(false, false, 0f, false, 0);
            float withScout = WarningResolver.Resolve(false, false, 0f, true, 1);
            Assert.AreEqual(15f, withScout - withoutScout, 0.001f);
        }

        [Test]
        public void Resolve_NeverExceedsTheCap()
        {
            // NewClass(150) + Explorer(+30) + Scout(+15) = 195, must clamp to 150.
            float w = WarningResolver.Resolve(true, false, 30f, true, 1);
            Assert.AreEqual(WarningResolver.WarningCapSeconds, w, 0.001f);
        }

        [Test]
        public void Resolve_NeverNegative()
        {
            float w = WarningResolver.Resolve(false, true, -100f, false, 0);
            Assert.GreaterOrEqual(w, 0f);
        }

        [Test]
        public void WeakestDefense_ReturnsLowestHullFraction()
        {
            var defenses = new System.Collections.Generic.List<(SurfaceStructureType, float)>
            {
                (SurfaceStructureType.Laser, 0.8f), (SurfaceStructureType.Plasma, 0.3f)
            };
            Assert.AreEqual(SurfaceStructureType.Plasma, WarningResolver.WeakestDefense(defenses));
        }

        [Test]
        public void WeakestDefense_EmptyList_ReturnsNull()
        {
            Assert.IsNull(WarningResolver.WeakestDefense(new System.Collections.Generic.List<(SurfaceStructureType, float)>()));
        }
    }

    /// <summary>VS-014 §8/§18: economic value multipliers and caps.</summary>
    public class Vs014EconomicValueResolverTests
    {
        [Test]
        public void RarityMultiplier_ExactValues()
        {
            Assert.AreEqual(1f, EconomicValueResolver.RarityMultiplier(Rarity.Common), 0.001f);
            Assert.AreEqual(1.10f, EconomicValueResolver.RarityMultiplier(Rarity.Uncommon), 0.001f);
            Assert.AreEqual(1.25f, EconomicValueResolver.RarityMultiplier(Rarity.Rare), 0.001f);
            Assert.AreEqual(1.45f, EconomicValueResolver.RarityMultiplier(Rarity.VeryRare), 0.001f);
            Assert.AreEqual(1.70f, EconomicValueResolver.RarityMultiplier(Rarity.Legendary), 0.001f);
        }

        [Test]
        public void Resolve_NeverExceedsTheCap()
        {
            float value = EconomicValueResolver.Resolve(Rarity.Legendary, activeDrillCountOnPlanet: 10, averageStorageFillFraction01: 1f, starConnected: true, pendingProbeReconActive: true);
            Assert.LessOrEqual(value, EconomicValueResolver.EconomicValueCap);
        }

        [Test]
        public void Resolve_MoreDrillsAndFullerStorage_HigherValue()
        {
            float baseline = EconomicValueResolver.Resolve(Rarity.Common, 1, 0f, false, false);
            float busier = EconomicValueResolver.Resolve(Rarity.Common, 4, 1f, false, false);
            Assert.Greater(busier, baseline);
        }

        [Test]
        public void Resolve_PendingProbeRecon_Adds15PercentOnTopOfBaseline()
        {
            float baseline = EconomicValueResolver.Resolve(Rarity.Common, 1, 0f, false, false);
            float withProbe = EconomicValueResolver.Resolve(Rarity.Common, 1, 0f, false, true);
            Assert.AreEqual(baseline * 1.15f, withProbe, 0.001f);
        }

        [Test]
        public void HasStarConnection_AlwaysFalse_NoRealSystemExistsYet()
        {
            Assert.IsFalse(EconomicValueResolver.HasStarConnection("any-system"));
        }
    }
}
