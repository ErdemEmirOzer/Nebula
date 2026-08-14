using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for Collector Mk II direct production and the Mk I-&gt;Mk II in-place upgrade job (VS-011 §5/§10.8).</summary>
    public class HangarProductionMk2Tests
    {
        private const string Metal = "material.metal_alloy";
        private const string Biocomposite = "material.biocomposite";

        private static LogisticsCargo FullCargo()
        {
            var cargo = new LogisticsCargo(500f);
            cargo.Add(ProcessingCatalog.BasicPartId, 100f);
            cargo.Add(Metal, 100f);
            cargo.Add(Biocomposite, 100f);
            return cargo;
        }

        [Test]
        public void DirectBuild_Mk2_ReservesExactCost_CompletesWithMk2Def()
        {
            LogisticsCargo cargo = FullCargo();
            var service = new HangarProductionService(cargo);

            HangarProductionRejectReason reason = service.TryStartProduction(VehicleClass.Collector, Metal, characterLevel: 25, hangarTier: 2, mk: 2);
            Assert.AreEqual(HangarProductionRejectReason.None, reason);
            Assert.AreEqual(2, service.ActiveJob.Mk);
            Assert.AreEqual(50f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(12f, cargo.GetReserved(Metal));

            string vehicleId = null;
            service.JobCompleted += (job, id) => vehicleId = id;
            service.Tick(70f);

            Assert.IsNotNull(vehicleId);
            Assert.AreEqual(50f, 100f - cargo.GetStored(ProcessingCatalog.BasicPartId), "Mk2's 50-part cost was consumed, not Mk1's 30");
        }

        [Test]
        public void DirectBuild_Mk2_RejectsBelowLevel21OrHangarTier2()
        {
            var service1 = new HangarProductionService(FullCargo());
            Assert.AreEqual(HangarProductionRejectReason.LevelTooLow, service1.TryStartProduction(VehicleClass.Collector, Metal, characterLevel: 20, hangarTier: 2, mk: 2));

            var service2 = new HangarProductionService(FullCargo());
            Assert.AreEqual(HangarProductionRejectReason.HangarTierTooLow, service2.TryStartProduction(VehicleClass.Collector, Metal, characterLevel: 25, hangarTier: 1, mk: 2));
        }

        [Test]
        public void Upgrade_ReservesFixedRecipe_NoMaterialChoice()
        {
            LogisticsCargo cargo = FullCargo();
            var service = new HangarProductionService(cargo);

            HangarUpgradeRejectReason reason = service.TryStartUpgrade("vehicle-0000", vehicleExistsAndIsMkI: true, characterLevel: 25, hangarTier: 2);
            Assert.AreEqual(HangarUpgradeRejectReason.None, reason);
            Assert.IsTrue(service.ActiveJob.IsUpgrade);
            Assert.AreEqual("vehicle-0000", service.ActiveJob.UpgradeVehicleId);
            Assert.AreEqual(20f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(12f, cargo.GetReserved(Metal));
            Assert.AreEqual(8f, cargo.GetReserved(Biocomposite));
        }

        [Test]
        public void Upgrade_RejectsWhenNotMkI()
        {
            var service = new HangarProductionService(FullCargo());
            HangarUpgradeRejectReason reason = service.TryStartUpgrade("vehicle-0000", vehicleExistsAndIsMkI: false, characterLevel: 25, hangarTier: 2);
            Assert.AreEqual(HangarUpgradeRejectReason.NotMkI, reason);
        }

        [Test]
        public void Upgrade_SharesSingleSlot_WithDirectBuild_QueueBusyEitherWay()
        {
            LogisticsCargo cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            service.TryStartUpgrade("vehicle-0000", true, 25, 2);

            HangarProductionRejectReason directReason = service.TryStartProduction(VehicleClass.Scout, Metal, 20, 1);
            Assert.AreEqual(HangarProductionRejectReason.QueueBusy, directReason);

            HangarUpgradeRejectReason upgradeReason = service.TryStartUpgrade("vehicle-0001", true, 25, 2);
            Assert.AreEqual(HangarUpgradeRejectReason.QueueBusy, upgradeReason);
        }

        [Test]
        public void Upgrade_Completion_FiresJobUpgradeCompleted_NeverJobCompleted_VehicleIdPreserved()
        {
            LogisticsCargo cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            service.TryStartUpgrade("vehicle-0000", true, 25, 2);

            bool upgradeCompletedFired = false;
            string upgradedVehicleId = null;
            bool directCompletedFired = false;
            service.JobUpgradeCompleted += job => { upgradeCompletedFired = true; upgradedVehicleId = job.UpgradeVehicleId; };
            service.JobCompleted += (job, id) => directCompletedFired = true;

            string returned = service.Tick(42f);

            Assert.IsTrue(upgradeCompletedFired);
            Assert.AreEqual("vehicle-0000", upgradedVehicleId, "the SAME vehicle id is preserved -- no second vehicle is spawned");
            Assert.IsFalse(directCompletedFired, "an upgrade must never fire the direct-build completion event");
            Assert.IsNull(returned, "Tick() never returns a fresh vehicle id for an upgrade job");
            Assert.IsNull(service.ActiveJob);
        }

        [Test]
        public void Upgrade_Cancel_Refunds75Percent_OfEachReservedResource()
        {
            LogisticsCargo cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            service.TryStartUpgrade("vehicle-0000", true, 25, 2);

            bool cancelled = service.CancelActiveJob();
            Assert.IsTrue(cancelled);
            Assert.IsNull(service.ActiveJob);
            // 100 - 20 (spent) + floor(20*0.75)=15 back = 95; likewise materials.
            Assert.AreEqual(95f, cargo.GetStored(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(97f, cargo.GetStored(Metal), "100 - 12 + floor(12*0.75)=9 = 97");
            Assert.AreEqual(98f, cargo.GetStored(Biocomposite), "100 - 8 + floor(8*0.75)=6 = 98");
        }

        [Test]
        public void RestoreActiveUpgradeJob_RederivesReservationFromRestoredCargo()
        {
            LogisticsCargo cargo = FullCargo();
            var service = new HangarProductionService(cargo);

            service.RestoreActiveUpgradeJob("vehicle-0000", 20, 12, 8, 42f, 10f);

            Assert.IsNotNull(service.ActiveJob);
            Assert.IsTrue(service.ActiveJob.IsUpgrade);
            Assert.AreEqual("vehicle-0000", service.ActiveJob.UpgradeVehicleId);
            Assert.AreEqual(10f, service.ActiveJob.RemainingSeconds);
            Assert.AreEqual(20f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
        }
    }
}
