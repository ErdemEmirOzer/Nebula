using NUnit.Framework;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-014 §2/§18: exact Command T2/Laser T2/Plasma T1/T2 KZG/cost/gate/combat numbers, and percentage-preserving tier-upgrade math.</summary>
    public class Vs014StructureTierTests
    {
        [Test]
        public void CommandT2_ExactGateCostKzg()
        {
            StructureTierDef d = PlanetaryDefenseCatalog.CommandT2;
            Assert.AreEqual(21, d.RequiredCharacterLevel);
            Assert.AreEqual(1, d.RequiredCommandTier);
            Assert.AreEqual(65, d.PartsCost);
            Assert.AreEqual(120f, d.BuildSeconds, 0.001f);
            Assert.AreEqual(300f, d.ShieldMax, 0.001f);
            Assert.AreEqual(900f, d.ArmorMax, 0.001f);
            Assert.AreEqual(1500f, d.HullMax, 0.001f);
        }

        [Test]
        public void LaserT2_ExactGateCostKzgCombat()
        {
            StructureTierDef d = PlanetaryDefenseCatalog.LaserT2;
            Assert.AreEqual(30, d.RequiredCharacterLevel);
            Assert.AreEqual(2, d.RequiredCommandTier);
            Assert.AreEqual(2, d.RequiredWeaponCoordinationTier);
            Assert.AreEqual(30, d.PartsCost);
            Assert.AreEqual(45f, d.BuildSeconds, 0.001f);
            Assert.AreEqual(150f, d.ShieldMax, 0.001f);
            Assert.AreEqual(380f, d.ArmorMax, 0.001f);
            Assert.AreEqual(600f, d.HullMax, 0.001f);
            Assert.AreEqual(135f, d.Dps, 0.001f);
            Assert.AreEqual(1100f, d.Range, 0.001f);
            Assert.AreEqual(90f, d.TurnDegreesPerSecond, 0.001f);
            Assert.AreEqual(8f, d.ContinuousFireSecondsBeforeOverheat, 0.001f);
            Assert.AreEqual(3f, d.OverheatCooldownSeconds, 0.001f);
        }

        [Test]
        public void PlasmaT1_ExactGateCostKzgCombat()
        {
            StructureTierDef d = PlanetaryDefenseCatalog.PlasmaT1;
            Assert.AreEqual(30, d.RequiredCharacterLevel);
            Assert.AreEqual(2, d.RequiredCommandTier);
            Assert.AreEqual(1, d.RequiredWeaponCoordinationTier);
            Assert.AreEqual(25, d.PartsCost);
            Assert.AreEqual(30f, d.BuildSeconds, 0.001f);
            Assert.AreEqual(100f, d.ShieldMax, 0.001f);
            Assert.AreEqual(450f, d.ArmorMax, 0.001f);
            Assert.AreEqual(600f, d.HullMax, 0.001f);
            Assert.AreEqual(140f, d.Dps, 0.001f);
            Assert.AreEqual(2500f, d.Range, 0.001f);
            Assert.AreEqual(45f, d.TurnDegreesPerSecond, 0.001f);
            Assert.AreEqual(120f, d.SplashRadius, 0.001f);
            Assert.AreEqual(0.25f, d.SplashMaxFractionOfDirect, 0.001f);
        }

        [Test]
        public void PlasmaT2_ExactGateCostKzgCombat()
        {
            StructureTierDef d = PlanetaryDefenseCatalog.PlasmaT2;
            Assert.AreEqual(2, d.RequiredCommandTier);
            Assert.AreEqual(2, d.RequiredWeaponCoordinationTier);
            Assert.AreEqual(45, d.PartsCost);
            Assert.AreEqual(60f, d.BuildSeconds, 0.001f);
            Assert.AreEqual(150f, d.ShieldMax, 0.001f);
            Assert.AreEqual(680f, d.ArmorMax, 0.001f);
            Assert.AreEqual(900f, d.HullMax, 0.001f);
            Assert.AreEqual(189f, d.Dps, 0.001f);
            Assert.AreEqual(2750f, d.Range, 0.001f);
        }

        [Test]
        public void Get_ReturnsNullForOutOfRangeTier_MaxTierGuard()
        {
            Assert.IsNull(PlanetaryDefenseCatalog.Get(SurfaceStructureType.Command, 3));
            Assert.IsNull(PlanetaryDefenseCatalog.Get(SurfaceStructureType.Laser, 3));
            Assert.IsNull(PlanetaryDefenseCatalog.Get(SurfaceStructureType.Plasma, 3));
        }

        [Test]
        public void RefundFraction_Is75Percent()
        {
            Assert.AreEqual(0.75f, PlanetaryDefenseCatalog.RefundFraction, 0.001f);
        }

        [Test]
        public void ApplyTierUpgrade_PreservesExactPercentage_NeverFreeFullHeal()
        {
            var runtime = new StructureRuntime(SurfaceStructureCatalog.Laser, DrillLifecycle.Active, 0f, 0f,
                new Durability(SurfaceStructureCatalog.Laser.ShieldMax, SurfaceStructureCatalog.Laser.ArmorMax, SurfaceStructureCatalog.Laser.HullMax, 1f));
            // Damage it to 50% hull before upgrading.
            runtime.Durability.ApplyDamage(runtime.Durability.HullMax * 0.5f + runtime.Durability.ShieldMax + runtime.Durability.ArmorMax);
            float hullFracBefore = runtime.Durability.Hull / runtime.Durability.HullMax;

            runtime.ApplyTierUpgrade(PlanetaryDefenseCatalog.LaserT2);

            Assert.AreEqual(2, runtime.Tier);
            float hullFracAfter = runtime.Durability.Hull / runtime.Durability.HullMax;
            Assert.AreEqual(hullFracBefore, hullFracAfter, 0.01f);
            Assert.Less(hullFracAfter, 1f, "an upgrade must never be a free full heal");
        }

        [Test]
        public void ApplyTierUpgrade_FreshT1_UpgradesToFullT2Kzg()
        {
            var runtime = new StructureRuntime(SurfaceStructureCatalog.Laser, DrillLifecycle.Active, 0f, 0f,
                new Durability(SurfaceStructureCatalog.Laser.ShieldMax, SurfaceStructureCatalog.Laser.ArmorMax, SurfaceStructureCatalog.Laser.HullMax, 1f));
            runtime.ApplyTierUpgrade(PlanetaryDefenseCatalog.LaserT2);
            Assert.AreEqual(PlanetaryDefenseCatalog.LaserT2.HullMax, runtime.Durability.Hull, 0.01f);
        }

        [Test]
        public void DefaultTier_IsOne_ForAV11MigratedStructure()
        {
            var runtime = new StructureRuntime(SurfaceStructureCatalog.Command, DrillLifecycle.Active, 0f, 0f,
                new Durability(1f, 1f, 1f, 1f)); // tier omitted -> defaults to 1
            Assert.AreEqual(1, runtime.Tier);
        }
    }
}
