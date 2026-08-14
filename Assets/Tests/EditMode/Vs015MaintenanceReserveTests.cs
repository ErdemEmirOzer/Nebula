using NUnit.Framework;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §5: Command Maintenance Reserve capacity by tier, accepted-item whitelist/rejection, and the Manual/Emergency/Full-Field automatic policy priority.</summary>
    public class Vs015MaintenanceReserveTests
    {
        [Test]
        public void Tier1Capacity_IsExactly40()
        {
            var reserve = new MaintenanceReserve("w1", 1);
            Assert.AreEqual(40f, reserve.Cargo.Capacity);
        }

        [Test]
        public void Tier2Capacity_IsExactly120()
        {
            var reserve = new MaintenanceReserve("w1", 2);
            Assert.AreEqual(120f, reserve.Cargo.Capacity);
        }

        [Test]
        public void ApplyTier_RescalesCapacity()
        {
            var reserve = new MaintenanceReserve("w1", 1);
            reserve.ApplyTier(2);
            Assert.AreEqual(120f, reserve.Cargo.Capacity);
        }

        [Test]
        public void AcceptsBasicPartAndAllFiveProcessedMaterials()
        {
            var reserve = new MaintenanceReserve("w1", 2);
            Assert.AreEqual(10f, reserve.Add(ProcessingCatalog.BasicPartId, 10f));
            foreach (string m in ProcessingCatalog.MaterialIds) Assert.AreEqual(5f, reserve.Add(m, 5f), m);
        }

        [Test]
        public void RejectsRawResources()
        {
            var reserve = new MaintenanceReserve("w1", 2);
            foreach (string raw in ExtractionCatalog.ResourceIds)
            {
                Assert.AreEqual(0f, reserve.Add(raw, 10f), raw);
                Assert.AreEqual(0f, reserve.Cargo.GetStored(raw));
            }
        }

        [Test]
        public void RejectsNuclearEnergyAndCore()
        {
            var reserve = new MaintenanceReserve("w1", 2);
            Assert.AreEqual(0f, reserve.Add("energy.nuclear", 5f));
            Assert.AreEqual(0f, reserve.Add("core.nuclear", 5f));
        }

        [Test]
        public void RejectsUnknownItem()
        {
            var reserve = new MaintenanceReserve("w1", 2);
            Assert.AreEqual(0f, reserve.Add("item.unknown_thing", 1f));
        }

        [Test]
        public void SingleActiveEngineerTarget_PerPlanet()
        {
            var reserve = new MaintenanceReserve("w1", 1);
            Assert.IsTrue(reserve.TrySetActiveTarget("target-a"));
            Assert.IsFalse(reserve.TrySetActiveTarget("target-b"));
            reserve.ClearActiveTarget("target-a");
            Assert.IsTrue(reserve.TrySetActiveTarget("target-b"));
        }

        [Test]
        public void ManualPolicy_IsTheDefault()
        {
            var reserve = new MaintenanceReserve("w1", 1);
            Assert.AreEqual(MaintenancePolicy.Manual, reserve.Policy);
        }

        [Test]
        public void EmergencyPolicy_PrefersCommandOverActiveDefense()
        {
            var defenses = new System.Collections.Generic.List<string> { "defense-1", "defense-2" };
            Assert.AreEqual("command-id", MaintenancePolicyResolver.ResolveEmergencyTarget("command-id", true, defenses));
        }

        [Test]
        public void EmergencyPolicy_FallsBackToFirstActiveDefense_WhenCommandDoesNotNeedRepair()
        {
            var defenses = new System.Collections.Generic.List<string> { "defense-1", "defense-2" };
            Assert.AreEqual("defense-1", MaintenancePolicyResolver.ResolveEmergencyTarget("command-id", false, defenses));
        }

        [Test]
        public void FullFieldPolicy_OrdersCommandThenDefenseThenDisabledVehicleThenDrillThenOthers()
        {
            var order = MaintenancePolicyResolver.ResolveFullFieldOrder(
                "command-id",
                new System.Collections.Generic.List<string> { "defense-1" },
                new System.Collections.Generic.List<string> { "vehicle-1" },
                new System.Collections.Generic.List<string> { "drill-1" },
                new System.Collections.Generic.List<string> { "support-1" });

            CollectionAssert.AreEqual(new[] { "command-id", "defense-1", "vehicle-1", "drill-1", "support-1" }, order);
        }

        [Test]
        public void FullFieldPolicy_SkipsCommand_WhenItDoesNotNeedRepair()
        {
            var order = MaintenancePolicyResolver.ResolveFullFieldOrder(null,
                new System.Collections.Generic.List<string> { "defense-1" }, null, null, null);
            CollectionAssert.AreEqual(new[] { "defense-1" }, order);
        }

        [Test]
        public void NotBuildOrUpgradeOrProductionOrGlobalStock_IsJustAPhysicalReservePool()
        {
            // VS-015 §5: the reserve is a bounded LogisticsCargo-backed pool with no build/upgrade/production API surface of its own.
            var reserve = new MaintenanceReserve("w1", 1);
            Assert.IsInstanceOf<LogisticsCargo>(reserve.Cargo);
            Assert.AreEqual(0f, reserve.Cargo.UsedVolume);
        }
    }
}
