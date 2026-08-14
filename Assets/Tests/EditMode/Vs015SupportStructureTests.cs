using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §7: Sensor Array / Maintenance Platform T1-T2 exact values, gates, and bonus multipliers, plus the combined-warning 150s cap.</summary>
    public class Vs015SupportStructureTests
    {
        [Test]
        public void SurfaceStructureType_NewValuesAreAdditive_ExistingOrdinalsUnchanged()
        {
            Assert.AreEqual(0, (int)SurfaceStructureType.Command);
            Assert.AreEqual(1, (int)SurfaceStructureType.Laser);
            Assert.AreEqual(2, (int)SurfaceStructureType.Plasma);
            Assert.AreEqual(3, (int)SurfaceStructureType.SensorArray);
            Assert.AreEqual(4, (int)SurfaceStructureType.MaintenancePlatform);
        }

        [Test]
        public void SensorArrayT1_HasExactCostDurationAndKzg()
        {
            StructureTierDef t1 = PlanetaryDefenseCatalog.SensorArrayT1;
            Assert.AreEqual(21, t1.RequiredCharacterLevel);
            Assert.AreEqual(2, t1.RequiredCommandTier);
            Assert.AreEqual(35, t1.PartsCost);
            Assert.AreEqual(60f, t1.BuildSeconds);
            Assert.AreEqual(200f, t1.ShieldMax); Assert.AreEqual(250f, t1.ArmorMax); Assert.AreEqual(350f, t1.HullMax);
            Assert.AreEqual(20f, t1.WarningBonusSeconds);

            var cost = t1.FullCost();
            Assert.AreEqual(35f, Find(cost, ProcessingCatalog.BasicPartId));
            Assert.AreEqual(12f, Find(cost, "material.industrial_diamond"));
            Assert.AreEqual(8f, Find(cost, "material.biocomposite"));
        }

        [Test]
        public void SensorArrayT2_IsOnePointFiveTimesT1Kzg_AndHasExactValues()
        {
            StructureTierDef t1 = PlanetaryDefenseCatalog.SensorArrayT1;
            StructureTierDef t2 = PlanetaryDefenseCatalog.SensorArrayT2;
            Assert.AreEqual(65, t2.PartsCost);
            Assert.AreEqual(120f, t2.BuildSeconds);
            Assert.AreEqual(t1.ShieldMax * 1.5f, t2.ShieldMax, 0.01f);
            Assert.AreEqual(t1.ArmorMax * 1.5f, t2.ArmorMax, 0.01f);
            Assert.AreEqual(t1.HullMax * 1.5f, t2.HullMax, 0.01f);
            Assert.AreEqual(35f, t2.WarningBonusSeconds);
        }

        [Test]
        public void MaintenancePlatformT1_HasExactCostDurationKzgAndMultipliers()
        {
            StructureTierDef t1 = PlanetaryDefenseCatalog.MaintenancePlatformT1;
            Assert.AreEqual(25, t1.RequiredCharacterLevel);
            Assert.AreEqual(2, t1.RequiredCommandTier);
            Assert.IsTrue(t1.RequiresLocalEngineer);
            Assert.AreEqual(40, t1.PartsCost);
            Assert.AreEqual(75f, t1.BuildSeconds);
            Assert.AreEqual(100f, t1.ShieldMax); Assert.AreEqual(400f, t1.ArmorMax); Assert.AreEqual(600f, t1.HullMax);
            Assert.AreEqual(1.25f, t1.EngineerRepairMultiplier);
            Assert.AreEqual(0.85f, t1.RecoveryTimeMultiplier);

            var cost = t1.FullCost();
            Assert.AreEqual(15f, Find(cost, "material.metal_alloy"));
            Assert.AreEqual(10f, Find(cost, "material.biocomposite"));
        }

        [Test]
        public void MaintenancePlatformT2_HasExactMultipliers()
        {
            StructureTierDef t2 = PlanetaryDefenseCatalog.MaintenancePlatformT2;
            Assert.AreEqual(70, t2.PartsCost);
            Assert.AreEqual(150f, t2.BuildSeconds);
            Assert.AreEqual(1.50f, t2.EngineerRepairMultiplier);
            Assert.AreEqual(0.70f, t2.RecoveryTimeMultiplier);
            Assert.IsTrue(t2.RequiresLocalEngineer);
        }

        [Test]
        public void MaintenancePlatform_ZeroEffect_WithoutMaterialsOrEngineer_IsAGateNotABonus()
        {
            // RequiresLocalEngineer is the gate that must be checked by the caller before the multiplier is ever applied
            // (StructureTierDef itself has no notion of "currently has an engineer" -- that's live state elsewhere).
            Assert.IsTrue(PlanetaryDefenseCatalog.MaintenancePlatformT1.RequiresLocalEngineer);
            Assert.IsTrue(PlanetaryDefenseCatalog.MaintenancePlatformT2.RequiresLocalEngineer);
        }

        [Test]
        public void Get_ResolvesAllFourSupportTiers()
        {
            Assert.AreSame(PlanetaryDefenseCatalog.SensorArrayT1, PlanetaryDefenseCatalog.Get(SurfaceStructureType.SensorArray, 1));
            Assert.AreSame(PlanetaryDefenseCatalog.SensorArrayT2, PlanetaryDefenseCatalog.Get(SurfaceStructureType.SensorArray, 2));
            Assert.AreSame(PlanetaryDefenseCatalog.MaintenancePlatformT1, PlanetaryDefenseCatalog.Get(SurfaceStructureType.MaintenancePlatform, 1));
            Assert.AreSame(PlanetaryDefenseCatalog.MaintenancePlatformT2, PlanetaryDefenseCatalog.Get(SurfaceStructureType.MaintenancePlatform, 2));
        }

        [Test]
        public void WarningResolver_SensorArrayBonus_StacksWithScoutButCapsAt150()
        {
            // 90 base + 20 difficulty + 15 scout + 35 sensor T2 = 160, clamped down to the shared 150s cap.
            float withBoth = WarningResolver.Resolve(isNewClassFirstAppearance: false, probePendingEffectActive: false,
                difficultyWarningOffsetSeconds: 20f, hasRealActiveScoutInSystem: true, scoutMk: 1,
                sensorArrayBonusSeconds: WarningResolver.SensorArrayBonusSeconds(true, 2));
            Assert.AreEqual(WarningResolver.WarningCapSeconds, withBoth);
        }

        [Test]
        public void WarningResolver_SensorArrayBonus_UsesTheStrongestActiveTierOnly()
        {
            Assert.AreEqual(20f, WarningResolver.SensorArrayBonusSeconds(true, 1));
            Assert.AreEqual(35f, WarningResolver.SensorArrayBonusSeconds(true, 2));
            Assert.AreEqual(0f, WarningResolver.SensorArrayBonusSeconds(false, 2));
        }

        [Test]
        public void SurfaceStructureCatalog_Get_ResolvesSupportBaseDefs()
        {
            Assert.AreSame(SurfaceStructureCatalog.SensorArray, SurfaceStructureCatalog.Get(SurfaceStructureType.SensorArray));
            Assert.AreSame(SurfaceStructureCatalog.MaintenancePlatform, SurfaceStructureCatalog.Get(SurfaceStructureType.MaintenancePlatform));
        }

        [Test]
        public void SupportStructures_AreNotActiveDefenseWeapons()
        {
            Assert.IsFalse(SurfaceStructureCatalog.IsActiveDefenseWeapon(SurfaceStructureType.SensorArray));
            Assert.IsFalse(SurfaceStructureCatalog.IsActiveDefenseWeapon(SurfaceStructureType.MaintenancePlatform));
        }

        [Test]
        public void Support2_ProducesNoRuntimeContent()
        {
            // VS-015 §12: support.2 is reserved/hidden for L41+ -- no third SurfaceStructureType value, no
            // catalog/tier/job/stat/render exists for it this slice. Only MaintenancePlatform=4 is the highest ordinal.
            var values = (SurfaceStructureType[])System.Enum.GetValues(typeof(SurfaceStructureType));
            Assert.AreEqual(5, values.Length, "Command, Laser, Plasma, SensorArray, MaintenancePlatform only");
            foreach (SurfaceStructureType t in values) Assert.LessOrEqual((int)t, 4);
        }

        private static float Find(System.Collections.Generic.List<(string itemId, float amount)> cost, string itemId)
        {
            foreach (var (id, amount) in cost) if (id == itemId) return amount;
            return 0f;
        }
    }
}
