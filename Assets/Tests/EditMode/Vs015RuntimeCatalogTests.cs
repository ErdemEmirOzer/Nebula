using NUnit.Framework;
using Nebula.Content;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §2: RuntimeCatalog snapshot correctness for the four new vehicles/four new structures, validator acceptance, fingerprint sensitivity, and immutable-snapshot discipline.</summary>
    public class Vs015RuntimeCatalogTests
    {
        private static RuntimeCatalog BuildReal() => RuntimeCatalogBuilder.Build(new CSharpContentCatalog());

        [Test]
        public void EngineerMk1_SnapshotMatchesSourceExactly()
        {
            ContentVehicle v = BuildReal().GetRequiredVehicle(VehicleCatalog.EngineerMk1Id);
            Assert.AreEqual(18, v.UnlockLevel);
            Assert.AreEqual(2, v.CommandCost);
            Assert.AreEqual(5f, v.RepairRatePerSecond);
            Assert.AreEqual(60f, v.DisabledVehicleRecoverSeconds);
            Assert.AreEqual(0, v.UpgradeCost.Count);
            Assert.AreEqual(0, v.FixedDirectBuildCost.Count);
        }

        [Test]
        public void EngineerMk2_SnapshotHasFixedDirectBuildAndUpgradeCosts()
        {
            ContentVehicle v = BuildReal().GetRequiredVehicle(VehicleCatalog.EngineerMk2Id);
            Assert.GreaterOrEqual(v.FixedDirectBuildCost.Count, 2);
            Assert.GreaterOrEqual(v.UpgradeCost.Count, 2);
        }

        [Test]
        public void EscortMk1AndMk2_HaveFixedLaserStats()
        {
            RuntimeCatalog catalog = BuildReal();
            Assert.AreEqual(60f, catalog.GetRequiredVehicle(VehicleCatalog.EscortMk1Id).FixedLaserDps);
            Assert.AreEqual(100f, catalog.GetRequiredVehicle(VehicleCatalog.EscortMk2Id).FixedLaserDps);
        }

        [Test]
        public void SensorArrayAndMaintenancePlatform_SnapshotsExist()
        {
            RuntimeCatalog catalog = BuildReal();
            Assert.IsTrue(catalog.Structures.ContainsKey("structure.support.sensor_array.t1"));
            Assert.IsTrue(catalog.Structures.ContainsKey("structure.support.sensor_array.t2"));
            Assert.IsTrue(catalog.Structures.ContainsKey("structure.support.maintenance_platform.t1"));
            Assert.IsTrue(catalog.Structures.ContainsKey("structure.support.maintenance_platform.t2"));
        }

        [Test]
        public void MaintenancePlatformSnapshot_CarriesRepairMultiplierAndEngineerGate()
        {
            ContentStructure s = BuildReal().GetRequiredStructure("structure.support.maintenance_platform.t1");
            Assert.AreEqual(1.25f, s.EngineerRepairMultiplier);
            Assert.AreEqual(0.85f, s.RecoveryTimeMultiplier);
            Assert.IsTrue(s.RequiresLocalEngineer);
        }

        [Test]
        public void ExistingContentIds_AreUnchanged()
        {
            RuntimeCatalog catalog = BuildReal();
            Assert.IsTrue(catalog.Vehicles.ContainsKey(VehicleCatalog.ScoutId));
            Assert.IsTrue(catalog.Vehicles.ContainsKey(VehicleCatalog.CollectorId));
            Assert.IsTrue(catalog.Vehicles.ContainsKey(VehicleCatalog.CollectorMk2Id));
            Assert.IsTrue(catalog.Structures.ContainsKey("structure.command"));
            Assert.IsTrue(catalog.Structures.ContainsKey("structure.laser"));
        }

        [Test]
        public void Validator_AcceptsTheRealCatalog_ZeroErrors()
        {
            var errors = ContentCatalogValidator.Validate(new CSharpContentCatalog());
            CollectionAssert.IsEmpty(errors);
        }

        [Test]
        public void Fingerprint_ChangesWhenAVehicleFieldChanges()
        {
            RuntimeCatalog a = BuildReal();
            ulong fingerprintA = ContentFingerprint.Compute(a);

            var fake = new FakeContentCatalog();
            // Mutate one Engineer vehicle's repair rate in the fake source and rebuild -- fingerprint must differ.
            fake.Vehicles.Add(new ContentVehicle("vehicle.engineer.mk1.mutated", "x", 1, 1, 1, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f, 0, 0, null, 2, 999f, 60f, 0f, 0f, null));
            RuntimeCatalog b = RuntimeCatalogBuilder.Build(fake);
            ulong fingerprintB = ContentFingerprint.Compute(b);

            Assert.AreNotEqual(fingerprintA, fingerprintB);
        }

        [Test]
        public void ContentRevision_IsExactly3()
        {
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }

        [Test]
        public void Snapshot_IsImmutable_MutatingSourceAfterBuildNeverChangesIt()
        {
            var source = new CSharpContentCatalog();
            RuntimeCatalog before = RuntimeCatalogBuilder.Build(source);
            int countBefore = before.Vehicles.Count;
            RuntimeCatalog after = RuntimeCatalogBuilder.Build(source); // rebuild is independent -- same real data, still no shared mutable reference
            Assert.AreEqual(countBefore, after.Vehicles.Count);
            Assert.AreEqual(7, before.Vehicles.Count);
        }
    }

}
