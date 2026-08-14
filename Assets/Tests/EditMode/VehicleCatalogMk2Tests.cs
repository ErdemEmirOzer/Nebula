using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for Collector Mk II's stable catalog data (VS-011 §5/§10.7).</summary>
    public class VehicleCatalogMk2Tests
    {
        [Test]
        public void CollectorMk2_HasSpecifiedStatsAndDirectBuildCost()
        {
            VehicleDef d = VehicleCatalog.CollectorMk2;
            Assert.AreEqual("vehicle.collector.mk2", VehicleCatalog.CollectorMk2Id);
            Assert.AreEqual(VehicleClass.Collector, d.Class);
            Assert.AreEqual(2, d.Mk);
            Assert.AreEqual(21, d.UnlockLevel);
            Assert.AreEqual(2, d.RequiredHangarTier);
            Assert.AreEqual(50, d.BasicPartCost);
            Assert.AreEqual(12, d.SpecialMaterialCost);
            Assert.AreEqual(70f, d.BuildSeconds);
            Assert.AreEqual(154f, d.Speed);
            Assert.AreEqual(150f, d.Shield);
            Assert.AreEqual(270f, d.Armor);
            Assert.AreEqual(380f, d.Hull);
            Assert.AreEqual(80f, d.CargoCapacity);
            Assert.AreEqual(20f, d.TransferVolumePerSecond);
        }

        [Test]
        public void CollectorMk1_UnaffectedByMk2Addition()
        {
            VehicleDef d = VehicleCatalog.Collector;
            Assert.AreEqual(1, d.Mk);
            Assert.AreEqual(40f, d.CargoCapacity);
            Assert.AreEqual(10f, d.TransferVolumePerSecond);
        }

        [Test]
        public void Get_ById_ResolvesMk2()
        {
            Assert.AreSame(VehicleCatalog.CollectorMk2, VehicleCatalog.Get(VehicleCatalog.CollectorMk2Id));
        }

        [Test]
        public void Get_ByClassAndMk_ResolvesCorrectDef()
        {
            Assert.AreSame(VehicleCatalog.Collector, VehicleCatalog.Get(VehicleClass.Collector, 1));
            Assert.AreSame(VehicleCatalog.CollectorMk2, VehicleCatalog.Get(VehicleClass.Collector, 2));
            Assert.AreSame(VehicleCatalog.Scout, VehicleCatalog.Get(VehicleClass.Scout, 2), "Scout only ever has Mk I, even if asked for Mk 2");
        }

        [Test]
        public void Mk2UpgradeCost_MatchesSpec_20Parts12MetalAlloy8Biocomposite()
        {
            Assert.AreEqual(20, VehicleCatalog.Mk2UpgradePartsCost);
            Assert.AreEqual(12, VehicleCatalog.Mk2UpgradeMetalAlloyCost);
            Assert.AreEqual(8, VehicleCatalog.Mk2UpgradeBiocompositeCost);
            Assert.AreEqual(42f, VehicleCatalog.Mk2UpgradeSeconds);
            Assert.AreEqual(0.75f, VehicleCatalog.Mk2UpgradeRefundFraction);

            var cost = VehicleCatalog.Mk2UpgradeCost();
            Assert.AreEqual(3, cost.Count);
            Assert.Contains((ProcessingCatalog.BasicPartId, 20f), cost);
            Assert.Contains((VehicleCatalog.MetalAlloyId, 12f), cost);
            Assert.Contains((VehicleCatalog.BiocompositeId, 8f), cost);
        }

        [Test]
        public void All_IncludesAllSevenDefs()
        {
            // VS-015 §1: Scout, Collector Mk I/II, Engineer Mk I/II, Escort Mk I/II.
            Assert.AreEqual(7, VehicleCatalog.All.Length);
            CollectionAssert.Contains(VehicleCatalog.All, VehicleCatalog.CollectorMk2);
            CollectionAssert.Contains(VehicleCatalog.All, VehicleCatalog.EngineerMk1);
            CollectionAssert.Contains(VehicleCatalog.All, VehicleCatalog.EngineerMk2);
            CollectionAssert.Contains(VehicleCatalog.All, VehicleCatalog.EscortMk1);
            CollectionAssert.Contains(VehicleCatalog.All, VehicleCatalog.EscortMk2);
        }
    }
}
