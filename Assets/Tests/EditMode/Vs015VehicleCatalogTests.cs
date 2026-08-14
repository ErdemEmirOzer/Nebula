using NUnit.Framework;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §1: exact values for the four new vehicle defs, plus unlock/hangar/controlled-planet gates and command cost.</summary>
    public class Vs015VehicleCatalogTests
    {
        [Test]
        public void EngineerMk1_HasExactValues()
        {
            VehicleDef d = VehicleCatalog.EngineerMk1;
            Assert.AreEqual("vehicle.engineer.mk1", d.Id);
            Assert.AreEqual(VehicleClass.Engineer, d.Class);
            Assert.AreEqual(18, d.UnlockLevel);
            Assert.AreEqual(1, d.RequiredHangarTier);
            Assert.IsTrue(d.RequiresControlledPlanet);
            Assert.AreEqual(2, d.CommandCost);
            Assert.AreEqual(45, d.BasicPartCost);
            Assert.AreEqual(40, d.SpecialMaterialCost);
            Assert.AreEqual(75f, d.BuildSeconds);
            Assert.AreEqual(150f, d.Speed);
            Assert.AreEqual(150f, d.Shield); Assert.AreEqual(300f, d.Armor); Assert.AreEqual(400f, d.Hull);
            Assert.AreEqual(5f, d.RepairRatePerSecond);
            Assert.AreEqual(60f, d.DisabledVehicleRecoverSeconds);
            Assert.AreEqual(20f, d.CargoCapacity);
            Assert.AreEqual(1, d.Mk);
        }

        [Test]
        public void EngineerMk2_HasExactValues()
        {
            VehicleDef d = VehicleCatalog.EngineerMk2;
            Assert.AreEqual("vehicle.engineer.mk2", d.Id);
            Assert.AreEqual(21, d.UnlockLevel);
            Assert.AreEqual(2, d.RequiredHangarTier);
            Assert.AreEqual(2, d.CommandCost);
            Assert.AreEqual(75, d.BasicPartCost);
            Assert.AreEqual(115f, d.BuildSeconds);
            Assert.AreEqual(165f, d.Speed);
            Assert.AreEqual(230f, d.Shield); Assert.AreEqual(450f, d.Armor); Assert.AreEqual(600f, d.Hull);
            Assert.AreEqual(9f, d.RepairRatePerSecond);
            Assert.AreEqual(45f, d.DisabledVehicleRecoverSeconds);
            Assert.AreEqual(40f, d.CargoCapacity);
            Assert.AreEqual(2, d.Mk);

            Assert.AreEqual(30, VehicleCatalog.EngineerMk2UpgradePartsCost);
            Assert.AreEqual(15, VehicleCatalog.EngineerMk2UpgradeMetalAlloyCost);
            Assert.AreEqual(15, VehicleCatalog.EngineerMk2UpgradeBiocompositeCost);
            Assert.AreEqual(69f, VehicleCatalog.EngineerMk2UpgradeSeconds);
            Assert.AreEqual(15, VehicleCatalog.EngineerMk2DirectMetalAlloyCost);
            Assert.AreEqual(15, VehicleCatalog.EngineerMk2DirectBiocompositeCost);
        }

        [Test]
        public void EscortMk1_HasExactValues()
        {
            VehicleDef d = VehicleCatalog.EscortMk1;
            Assert.AreEqual("vehicle.escort.mk1", d.Id);
            Assert.AreEqual(21, d.UnlockLevel);
            Assert.AreEqual(2, d.RequiredHangarTier);
            Assert.AreEqual(2, d.CommandCost);
            Assert.AreEqual(40, d.BasicPartCost);
            Assert.AreEqual(30, d.SpecialMaterialCost);
            Assert.AreEqual(60f, d.BuildSeconds);
            Assert.AreEqual(190f, d.Speed);
            Assert.AreEqual(200f, d.Shield); Assert.AreEqual(250f, d.Armor); Assert.AreEqual(350f, d.Hull);
            Assert.AreEqual(60f, d.FixedLaserDps);
            Assert.AreEqual(800f, d.FixedLaserRange);
        }

        [Test]
        public void EscortMk2_HasExactValues()
        {
            VehicleDef d = VehicleCatalog.EscortMk2;
            Assert.AreEqual("vehicle.escort.mk2", d.Id);
            Assert.AreEqual(2, d.CommandCost);
            Assert.AreEqual(65, d.BasicPartCost);
            Assert.AreEqual(90f, d.BuildSeconds);
            Assert.AreEqual(209f, d.Speed);
            Assert.AreEqual(300f, d.Shield); Assert.AreEqual(380f, d.Armor); Assert.AreEqual(530f, d.Hull);
            Assert.AreEqual(100f, d.FixedLaserDps);
            Assert.AreEqual(1000f, d.FixedLaserRange);

            Assert.AreEqual(25, VehicleCatalog.EscortMk2UpgradePartsCost);
            Assert.AreEqual(18, VehicleCatalog.EscortMk2UpgradeMetalAlloyCost);
            Assert.AreEqual(10, VehicleCatalog.EscortMk2UpgradeIndustrialDiamondCost);
            Assert.AreEqual(54f, VehicleCatalog.EscortMk2UpgradeSeconds);
            Assert.AreEqual(18, VehicleCatalog.EscortMk2DirectMetalAlloyCost);
            Assert.AreEqual(10, VehicleCatalog.EscortMk2DirectIndustrialDiamondCost);
        }

        [Test]
        public void ScoutAndCollector_CommandCostStaysOne()
        {
            Assert.AreEqual(1, VehicleCatalog.Scout.CommandCost);
            Assert.AreEqual(1, VehicleCatalog.Collector.CommandCost);
            Assert.AreEqual(1, VehicleCatalog.CollectorMk2.CommandCost);
        }

        [Test]
        public void VehicleClass_ScoutAndCollectorOrdinalsUnchanged()
        {
            Assert.AreEqual(0, (int)VehicleClass.Scout);
            Assert.AreEqual(1, (int)VehicleClass.Collector);
            Assert.AreEqual(2, (int)VehicleClass.Engineer);
            Assert.AreEqual(3, (int)VehicleClass.Escort);
        }

        [Test]
        public void All_IncludesAllSevenDefs()
        {
            Assert.AreEqual(7, VehicleCatalog.All.Length);
        }

        [Test]
        public void Get_ByClassAndMk_ResolvesEngineerAndEscort()
        {
            Assert.AreSame(VehicleCatalog.EngineerMk1, VehicleCatalog.Get(VehicleClass.Engineer, 1));
            Assert.AreSame(VehicleCatalog.EngineerMk2, VehicleCatalog.Get(VehicleClass.Engineer, 2));
            Assert.AreSame(VehicleCatalog.EscortMk1, VehicleCatalog.Get(VehicleClass.Escort, 1));
            Assert.AreSame(VehicleCatalog.EscortMk2, VehicleCatalog.Get(VehicleClass.Escort, 2));
        }

        [Test]
        public void UpgradeRecipes_AreCorrectlyKeyed()
        {
            Assert.IsTrue(VehicleCatalog.TryGetUpgradeRecipe(VehicleCatalog.EngineerMk1Id, out var er));
            Assert.AreEqual(VehicleCatalog.EngineerMk2Id, er.ToVehicleId);
            Assert.IsTrue(VehicleCatalog.TryGetUpgradeRecipe(VehicleCatalog.EscortMk1Id, out var es));
            Assert.AreEqual(VehicleCatalog.EscortMk2Id, es.ToVehicleId);
            Assert.IsTrue(VehicleCatalog.TryGetUpgradeRecipe(VehicleCatalog.CollectorId, out var cr));
            Assert.AreEqual(VehicleCatalog.CollectorMk2Id, cr.ToVehicleId);
        }

        [Test]
        public void FixedDirectBuildRecipes_ExistOnlyForMkIIEngineerAndEscort()
        {
            Assert.IsTrue(VehicleCatalog.TryGetFixedDirectBuildRecipe(VehicleCatalog.EngineerMk2Id, out _));
            Assert.IsTrue(VehicleCatalog.TryGetFixedDirectBuildRecipe(VehicleCatalog.EscortMk2Id, out _));
            Assert.IsFalse(VehicleCatalog.TryGetFixedDirectBuildRecipe(VehicleCatalog.CollectorMk2Id, out _));
        }
    }
}
