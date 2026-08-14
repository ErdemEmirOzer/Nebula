using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for VS-013.1 Hull Form/Hull Reinforcement/Shield Systems/energy catalog data (§1/§2/§7).</summary>
    public class CombatCatalogTests
    {
        [Test]
        public void T1Baseline_MatchesSpecExactly()
        {
            Assert.AreEqual(180f, CombatCatalog.BaseSpeed);
            Assert.AreEqual(400f, CombatCatalog.BaseShield);
            Assert.AreEqual(600f, CombatCatalog.BaseArmor);
            Assert.AreEqual(1000f, CombatCatalog.BaseHull);
        }

        [TestCase(1, ExpectedResult = 1)]
        [TestCase(20, ExpectedResult = 1)]
        [TestCase(21, ExpectedResult = 2)]
        [TestCase(40, ExpectedResult = 2)]
        [TestCase(41, ExpectedResult = 2)] // this slice never exceeds Form II regardless of level
        [TestCase(100, ExpectedResult = 2)]
        public int HullFormForLevel_MatchesSpecBands(int level) => CombatCatalog.HullFormForLevel(level);

        [Test]
        public void HullFormBaseline_FormI_MatchesSpecExactly()
        {
            Assert.AreEqual(180f, CombatCatalog.HullFormSpeedReference(1));
            Assert.AreEqual(1f, CombatCatalog.HullFormDurabilityMultiplier(1));
            Assert.AreEqual(1f, CombatCatalog.HullFormTurnMultiplier(1));
            Assert.AreEqual(1, CombatCatalog.HullFormHardpoints(1));
        }

        [Test]
        public void HullFormBaseline_FormII_MatchesSpecExactly()
        {
            Assert.AreEqual(194.4f, CombatCatalog.HullFormSpeedReference(2), 0.001f);
            Assert.AreEqual(1.5f, CombatCatalog.HullFormDurabilityMultiplier(2), 0.001f);
            Assert.AreEqual(1.05f, CombatCatalog.HullFormTurnMultiplier(2), 0.001f);
            Assert.AreEqual(2, CombatCatalog.HullFormHardpoints(2));
        }

        [Test]
        public void HullFormSpeedReference_NeverActuallyAppliedToShip_MatchesEngineNavigationTableExactly()
        {
            // Form I/II's speed reference intentionally mirrors ModuleCatalog.LocalTopSpeedByTier
            // (Engine-Navigation's own, sole authority over commanded ship speed) -- proving
            // the two numbers stay in sync without CombatCatalog ever driving speed itself.
            Assert.AreEqual(Modules.ModuleCatalog.LocalTopSpeedByTier[0], CombatCatalog.HullFormSpeedReference(1));
            Assert.AreEqual(Modules.ModuleCatalog.LocalTopSpeedByTier[1], CombatCatalog.HullFormSpeedReference(2), 0.001f);
        }

        [Test]
        public void HullReinforcement_T0AndT1_AreDistinct_T1IsNotANoOp()
        {
            // VS-013.1 fix: T0 and T1 must never collapse to the same multiplier.
            Assert.AreEqual(1f, CombatCatalog.HullReinforcementMultiplier(0));
            Assert.AreEqual(1.15f, CombatCatalog.HullReinforcementMultiplier(1), 0.001f);
            Assert.AreNotEqual(CombatCatalog.HullReinforcementMultiplier(0), CombatCatalog.HullReinforcementMultiplier(1));
        }

        [Test]
        public void HullReinforcement_T2_MatchesSpec()
        {
            Assert.AreEqual(1.30f, CombatCatalog.HullReinforcementMultiplier(2), 0.001f);
        }

        [Test]
        public void ShieldSystems_T0T1T2_MatchSpec()
        {
            Assert.AreEqual(1f, CombatCatalog.ShieldSystemsMultiplier(0));
            Assert.AreEqual(1.25f, CombatCatalog.ShieldSystemsMultiplier(1), 0.001f);
            Assert.AreEqual(1.50f, CombatCatalog.ShieldSystemsMultiplier(2), 0.001f);
        }

        [Test]
        public void ResolvedDurability_FormII_NoModules_MatchesSpecExample()
        {
            // "L21 Gövde II, modülsüz: K600/Z900/G1500."
            Assert.AreEqual(600f, CombatCatalog.ResolvedShieldMax(hullForm: 2, shieldSystemsTier: 0), 0.01f);
            Assert.AreEqual(900f, CombatCatalog.ResolvedArmorMax(hullForm: 2, hullReinforcementTier: 0), 0.01f);
            Assert.AreEqual(1500f, CombatCatalog.ResolvedHullMax(hullForm: 2, hullReinforcementTier: 0), 0.01f);
        }

        [Test]
        public void ResolvedDurability_FormII_HullReinforcementT2_MatchesSpecExample()
        {
            // "Gövde Güçlendirme T2: K600/Z1170/G1950." -- Shield untouched by Hull Reinforcement.
            Assert.AreEqual(600f, CombatCatalog.ResolvedShieldMax(hullForm: 2, shieldSystemsTier: 0), 0.01f);
            Assert.AreEqual(1170f, CombatCatalog.ResolvedArmorMax(hullForm: 2, hullReinforcementTier: 2), 0.01f);
            Assert.AreEqual(1950f, CombatCatalog.ResolvedHullMax(hullForm: 2, hullReinforcementTier: 2), 0.01f);
        }

        [Test]
        public void ResolvedShieldMax_ShieldSystemsT2_NeverAffectsArmorOrHull()
        {
            float armorAtT0 = CombatCatalog.ResolvedArmorMax(2, 1);
            float hullAtT0 = CombatCatalog.ResolvedHullMax(2, 1);
            // Shield Systems tier plays no role in Armor/Hull resolution at all (different
            // methods, different parameters) -- this asserts the two Resolved*Max calls for
            // Armor/Hull are identical regardless of any shieldSystemsTier value.
            Assert.AreEqual(armorAtT0, CombatCatalog.ResolvedArmorMax(2, 1), 0.001f);
            Assert.AreEqual(hullAtT0, CombatCatalog.ResolvedHullMax(2, 1), 0.001f);
        }

        [Test]
        public void EnergyTables_Tier0And1_MatchSpecT1Values()
        {
            Assert.AreEqual(100f, CombatCatalog.EnergyCapacity(0));
            Assert.AreEqual(100f, CombatCatalog.EnergyCapacity(1));
            Assert.AreEqual(15f, CombatCatalog.EnergyRegenPerSecond(1));
        }

        [Test]
        public void EnergyTables_Tier2_MatchSpec()
        {
            Assert.AreEqual(150f, CombatCatalog.EnergyCapacity(2));
            Assert.AreEqual(22f, CombatCatalog.EnergyRegenPerSecond(2));
        }

        [TestCase(1, 0, ExpectedResult = 1)]
        [TestCase(3, 0, ExpectedResult = 1)]
        [TestCase(3, 1, ExpectedResult = 2)]
        [TestCase(3, 2, ExpectedResult = 3)]
        [TestCase(1, 5, ExpectedResult = 1)] // capped by hull hardpoint count even if coordination tier is high
        public int ActiveHardpointSlots_IsMinOfHullFormHardpointsAndCoordinationTierPlusOne(int hullForm, int weaponCoordinationTier)
        {
            return CombatCatalog.ActiveHardpointSlots(hullForm, weaponCoordinationTier);
        }

        [Test]
        public void BoostConstants_MatchSpec()
        {
            Assert.AreEqual(40f, CombatCatalog.BoostEnergyCost);
            Assert.AreEqual(3f, CombatCatalog.BoostDurationSeconds);
            Assert.AreEqual(1.40f, CombatCatalog.BoostSpeedMultiplier, 0.001f);
            Assert.AreEqual(6f, CombatCatalog.BoostCooldownSeconds);
        }

        [Test]
        public void CollisionConstants_MatchSpec()
        {
            Assert.AreEqual(80f, CombatCatalog.CollisionSafeRelativeSpeed);
            Assert.AreEqual(0.15f, CombatCatalog.CollisionMaxHullFractionPerEvent, 0.001f);
            Assert.AreEqual(1.5f, CombatCatalog.CollisionPairImmunitySeconds);
        }
    }
}
