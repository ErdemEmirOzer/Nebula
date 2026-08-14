using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for Laser/Plasma/EM weapon math + hardpoint heat + production cost (VS-013 §3/§11).</summary>
    public class WeaponCatalogTests
    {
        [Test]
        public void LaserT1_MatchesSpecExactly()
        {
            Assert.AreEqual(80f, WeaponCatalog.LaserDps(1));
            Assert.AreEqual(1000f, WeaponCatalog.LaserRange(1));
            Assert.AreEqual(25f, WeaponCatalog.LaserGimbalDegrees);
            Assert.AreEqual(3f, WeaponCatalog.LaserStandardAimAssistDegrees);
            Assert.AreEqual(12f, WeaponCatalog.LaserEnergyPerSecond(1));
        }

        [Test]
        public void Laser_DamageAndEnergy_AreFrameRateIndependent()
        {
            // 10 ticks of 0.1s must equal one tick of 1.0s -- proves the tick is a pure dt multiply, no fixed-step assumption.
            float accumulated = 0f;
            for (int i = 0; i < 10; i++) accumulated += WeaponCatalog.LaserDamagePerTick(1, 0.1f);
            Assert.AreEqual(WeaponCatalog.LaserDamagePerTick(1, 1f), accumulated, 0.001f);
        }

        [Test]
        public void Laser_8SecondsContinuousFire_ReachesExactlyMaxHeat()
        {
            float heat = WeaponCatalog.LaserHeatPerSecond * WeaponCatalog.LaserContinuousFireSecondsBeforeOverheat;
            Assert.AreEqual(WeaponCatalog.HardpointMaxHeat, heat, 0.001f);
        }

        [Test]
        public void PlasmaT1_MatchesSpecExactly()
        {
            Assert.AreEqual(2500f, WeaponCatalog.PlasmaRange(1));
            Assert.AreEqual(1100f, WeaponCatalog.PlasmaProjectileSpeed);
            Assert.AreEqual(1.5f, WeaponCatalog.PlasmaFireIntervalSeconds);
            Assert.AreEqual(30f, WeaponCatalog.PlasmaEnergyPerShot);
            Assert.AreEqual(35f, WeaponCatalog.PlasmaHeatPerShot);
            Assert.AreEqual(120f, WeaponCatalog.PlasmaDirectDamage(1) / WeaponCatalog.PlasmaFireIntervalSeconds, 0.001f); // 120 DPS
        }

        [Test]
        public void Plasma_SplashDamage_FallsOffLinearlyToZeroAtRadius_CappedAt25PercentOfDirect()
        {
            float direct = WeaponCatalog.PlasmaDirectDamage(1);
            float atCenter = WeaponCatalog.PlasmaSplashDamage(1, 0f);
            float atRadius = WeaponCatalog.PlasmaSplashDamage(1, WeaponCatalog.PlasmaSplashRadius);
            float beyondRadius = WeaponCatalog.PlasmaSplashDamage(1, WeaponCatalog.PlasmaSplashRadius + 1f);

            Assert.AreEqual(direct * 0.25f, atCenter, 0.01f);
            Assert.AreEqual(0f, atRadius, 0.01f);
            Assert.AreEqual(0f, beyondRadius);
        }

        [Test]
        public void EmT1_MatchesSpecExactly()
        {
            Assert.AreEqual(5000f, WeaponCatalog.EmRange(1));
            Assert.AreEqual(15f, WeaponCatalog.EmLockConeDegrees);
            Assert.AreEqual(1.5f, WeaponCatalog.EmLockSeconds);
            Assert.AreEqual(1600f, WeaponCatalog.EmProjectileSpeed);
            Assert.AreEqual(45f, WeaponCatalog.EmMaxTurnRateDegreesPerSecond);
            Assert.AreEqual(50f, WeaponCatalog.EmEnergyPerShot);
            Assert.AreEqual(60f, WeaponCatalog.EmHeatPerShot);
            Assert.AreEqual(3f, WeaponCatalog.EmDisruptSeconds);
            Assert.AreEqual(8f, WeaponCatalog.EmImmunityAfterDisruptSeconds);
            Assert.AreEqual(2f, WeaponCatalog.EmLockBreakGraceSeconds);
        }

        [Test]
        public void TierMultiplierTables_MatchSpecT1ToT5()
        {
            CollectionAssert.AreEqual(new[] { 1f, 1.35f, 1.8f, 2.4f, 3.2f }, WeaponCatalog.DpsMultiplierByTier);
            CollectionAssert.AreEqual(new[] { 1f, 1.1f, 1.2f, 1.3f, 1.4f }, WeaponCatalog.RangeMultiplierByTier);
            CollectionAssert.AreEqual(new[] { 1f, 1.1f, 1.2f, 1.3f, 1.4f }, WeaponCatalog.EnergyMultiplierByTier);
        }

        [Test]
        public void RuntimeProgression_StopsAtT2ThisSlice()
        {
            Assert.AreEqual(2, WeaponCatalog.MaxTierThisSlice);
        }

        [Test]
        public void ProductionCost_IsHalfOfMatchingPlanetaryStructure_RoundedUp()
        {
            // Planetary Laser: 15 parts / 5 material / 20s -> ship Laser T1: 8/3/10s.
            WeaponTierCost laser = WeaponCatalog.CostForTier(WeaponFamily.Laser, 1);
            Assert.AreEqual(8, laser.Parts);
            Assert.AreEqual(3, laser.Material);
            Assert.AreEqual(10f, laser.Seconds);

            // Planetary Command: 30 parts / 10 material / 60s -> ship Plasma/EM T1: 15/5/30s.
            WeaponTierCost plasma = WeaponCatalog.CostForTier(WeaponFamily.Plasma, 1);
            Assert.AreEqual(15, plasma.Parts);
            Assert.AreEqual(5, plasma.Material);
            Assert.AreEqual(30f, plasma.Seconds);
        }

        [Test]
        public void ProductionRefundFraction_ReusesTheProjectStandard75Percent()
        {
            Assert.AreEqual(Extraction.SurfaceStructureCatalog.RefundFraction, WeaponCatalog.ProductionRefundFraction);
            Assert.AreEqual(0.75f, WeaponCatalog.ProductionRefundFraction);
        }
    }

    /// <summary>EditMode tests for per-hardpoint 0-100 heat (VS-013 §3).</summary>
    public class HardpointStateTests
    {
        [Test]
        public void FreshHardpoint_IsEmpty()
        {
            var hp = new HardpointState(0);
            Assert.IsTrue(hp.IsEmpty);
            Assert.IsFalse(hp.CanFire);
        }

        [Test]
        public void Equip_MakesItFireable_AtZeroHeat()
        {
            var hp = new HardpointState(0);
            hp.Equip(WeaponFamily.Laser, 1);
            Assert.IsFalse(hp.IsEmpty);
            Assert.IsTrue(hp.CanFire);
            Assert.AreEqual(0f, hp.Heat);
        }

        [Test]
        public void AddHeat_AtCap_TriggersOverheat_DisablingOnlyThisHardpoint()
        {
            var hp = new HardpointState(0);
            hp.Equip(WeaponFamily.Plasma, 1);
            hp.AddHeat(100f);
            Assert.IsTrue(hp.Overheated);
            Assert.IsFalse(hp.CanFire);
        }

        [Test]
        public void Overheat_Clears_AfterLockoutDuration()
        {
            var hp = new HardpointState(0);
            hp.Equip(WeaponFamily.Plasma, 1);
            hp.AddHeat(100f);
            for (int i = 0; i < 4; i++) hp.Tick(1f, 0f); // 4s > 3s lockout
            Assert.IsFalse(hp.Overheated);
            Assert.IsTrue(hp.CanFire);
        }

        [Test]
        public void Heat_CoolsAfterIdleDelay_NeverWhileRecentlyFired()
        {
            var hp = new HardpointState(0);
            hp.Equip(WeaponFamily.Laser, 1);
            hp.AddHeat(50f);
            hp.Tick(0.5f, 0f); // still within the 0.75s idle delay
            Assert.AreEqual(50f, hp.Heat, 0.01f, "must not cool while still inside the idle delay");

            hp.Tick(1f, 0f); // now past idle delay
            Assert.Less(hp.Heat, 50f);
        }

        [Test]
        public void EnvironmentalHeat_AppliesEvenWhileNotFiring()
        {
            var hp = new HardpointState(0);
            hp.Equip(WeaponFamily.Em, 1);
            hp.Tick(1f, 2f); // Critical zone: 2 heat/s
            Assert.AreEqual(2f, hp.Heat, 0.01f);
        }
    }
}
