using NUnit.Framework;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure vehicle catalog data (VS-007 §2).</summary>
    public class VehicleCatalogTests
    {
        [Test]
        public void Scout_HasSpecifiedStatsAndCost()
        {
            VehicleDef d = VehicleCatalog.Scout;
            Assert.AreEqual(10, d.UnlockLevel);
            Assert.AreEqual(1, d.RequiredHangarTier);
            Assert.AreEqual(20, d.BasicPartCost);
            Assert.AreEqual(15, d.SpecialMaterialCost);
            Assert.AreEqual(30f, d.BuildSeconds);
            Assert.AreEqual(220f, d.Speed);
            Assert.AreEqual(100f, d.Shield);
            Assert.AreEqual(80f, d.Armor);
            Assert.AreEqual(120f, d.Hull);
            Assert.AreEqual(1500f, d.ScanRange);
            Assert.AreEqual(60f, d.ScanSeconds);
            Assert.AreEqual(0f, d.CargoCapacity);
        }

        [Test]
        public void Collector_HasSpecifiedStatsAndCost()
        {
            VehicleDef d = VehicleCatalog.Collector;
            Assert.AreEqual(15, d.UnlockLevel);
            Assert.AreEqual(1, d.RequiredHangarTier);
            Assert.AreEqual(30, d.BasicPartCost);
            Assert.AreEqual(20, d.SpecialMaterialCost);
            Assert.AreEqual(45f, d.BuildSeconds);
            Assert.AreEqual(140f, d.Speed);
            Assert.AreEqual(100f, d.Shield);
            Assert.AreEqual(180f, d.Armor);
            Assert.AreEqual(250f, d.Hull);
            Assert.AreEqual(40f, d.CargoCapacity);
            Assert.AreEqual(10f, d.TransferVolumePerSecond);
        }

        [Test]
        public void Get_ResolvesByIdAndByClass()
        {
            Assert.AreSame(VehicleCatalog.Scout, VehicleCatalog.Get(VehicleCatalog.ScoutId));
            Assert.AreSame(VehicleCatalog.Collector, VehicleCatalog.Get(VehicleCatalog.CollectorId));
            Assert.AreSame(VehicleCatalog.Scout, VehicleCatalog.Get(VehicleClass.Scout));
            Assert.AreSame(VehicleCatalog.Collector, VehicleCatalog.Get(VehicleClass.Collector));
            Assert.IsNull(VehicleCatalog.Get("vehicle.unknown"));
        }

        [Test]
        public void SpecialMaterialIds_ExposesAllFiveProcessedMaterials()
        {
            Assert.AreEqual(5, VehicleCatalog.SpecialMaterialIds.Length);
        }
    }
}
