using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §1: generalized fixed-recipe direct build + Mk I-&gt;Mk II upgrade for Engineer/Escort — single slot, atomic reservation, 75% cancel refund, generic vehicle-id/recipe path.</summary>
    public class Vs015HangarProductionTests
    {
        private static LogisticsCargo FullCargo() => new LogisticsCargo(10000f).With(ProcessingCatalog.BasicPartId, 500f)
            .With(VehicleCatalog.MetalAlloyId, 200f).With(VehicleCatalog.BiocompositeId, 200f).With(VehicleCatalog.IndustrialDiamondId, 200f);

        [Test]
        public void TryStartFixedProduction_EngineerMk2_ReservesExactRecipe()
        {
            var cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            HangarProductionRejectReason reason = service.TryStartFixedProduction(VehicleCatalog.EngineerMk2Id, characterLevel: 21, hangarTier: 2);
            Assert.AreEqual(HangarProductionRejectReason.None, reason);
            Assert.AreEqual(75f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(15f, cargo.GetReserved(VehicleCatalog.MetalAlloyId));
            Assert.AreEqual(15f, cargo.GetReserved(VehicleCatalog.BiocompositeId));
        }

        [Test]
        public void TryStartFixedProduction_EscortMk2_ReservesExactRecipe()
        {
            var cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            HangarProductionRejectReason reason = service.TryStartFixedProduction(VehicleCatalog.EscortMk2Id, characterLevel: 21, hangarTier: 2);
            Assert.AreEqual(HangarProductionRejectReason.None, reason);
            Assert.AreEqual(65f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(18f, cargo.GetReserved(VehicleCatalog.MetalAlloyId));
            Assert.AreEqual(10f, cargo.GetReserved(VehicleCatalog.IndustrialDiamondId));
        }

        [Test]
        public void TryStartFixedProduction_LevelTooLow_Rejects()
        {
            var service = new HangarProductionService(FullCargo());
            Assert.AreEqual(HangarProductionRejectReason.LevelTooLow, service.TryStartFixedProduction(VehicleCatalog.EngineerMk2Id, 5, 2));
        }

        [Test]
        public void TryStartFixedProduction_QueueBusy_WhenAJobAlreadyActive()
        {
            var service = new HangarProductionService(FullCargo());
            service.TryStartFixedProduction(VehicleCatalog.EngineerMk2Id, 21, 2);
            Assert.AreEqual(HangarProductionRejectReason.QueueBusy, service.TryStartFixedProduction(VehicleCatalog.EscortMk2Id, 21, 2));
        }

        [Test]
        public void TryStartUpgrade_Generic_EngineerMk1ToMk2_ReservesExactRecipe()
        {
            var cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            HangarUpgradeRejectReason reason = service.TryStartUpgrade("vehicle-0001", VehicleCatalog.EngineerMk1Id, true, 21, 2);
            Assert.AreEqual(HangarUpgradeRejectReason.None, reason);
            Assert.AreEqual(30f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(15f, cargo.GetReserved(VehicleCatalog.MetalAlloyId));
            Assert.AreEqual(15f, cargo.GetReserved(VehicleCatalog.BiocompositeId));
            Assert.IsTrue(service.ActiveJob.IsUpgrade);
            Assert.AreEqual(VehicleCatalog.EngineerMk2Id, service.ActiveJob.TargetVehicleId);
            Assert.AreEqual("vehicle-0001", service.ActiveJob.UpgradeVehicleId);
        }

        [Test]
        public void TryStartUpgrade_Generic_EscortMk1ToMk2_ReservesExactRecipe()
        {
            var cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            HangarUpgradeRejectReason reason = service.TryStartUpgrade("vehicle-0002", VehicleCatalog.EscortMk1Id, true, 21, 2);
            Assert.AreEqual(HangarUpgradeRejectReason.None, reason);
            Assert.AreEqual(25f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(18f, cargo.GetReserved(VehicleCatalog.MetalAlloyId));
            Assert.AreEqual(10f, cargo.GetReserved(VehicleCatalog.IndustrialDiamondId));
        }

        [Test]
        public void TryStartUpgrade_CollectorFlow_StillWorksIdentically()
        {
            var cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            HangarUpgradeRejectReason reason = service.TryStartUpgrade("vehicle-0003", true, 21, 2); // original Collector-only overload
            Assert.AreEqual(HangarUpgradeRejectReason.None, reason);
            Assert.AreEqual(20f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(12f, cargo.GetReserved(VehicleCatalog.MetalAlloyId));
            Assert.AreEqual(8f, cargo.GetReserved(VehicleCatalog.BiocompositeId));
            Assert.AreEqual(VehicleCatalog.CollectorMk2Id, service.ActiveJob.TargetVehicleId);
        }

        [Test]
        public void DirectBuildAndUpgrade_ShareTheSameSingleSlot()
        {
            var cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            service.TryStartFixedProduction(VehicleCatalog.EngineerMk2Id, 21, 2);
            Assert.AreEqual(HangarUpgradeRejectReason.QueueBusy, service.TryStartUpgrade("vehicle-9", VehicleCatalog.EscortMk1Id, true, 21, 2));
        }

        [Test]
        public void CancelActiveJob_RefundsExactly75Percent()
        {
            var cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            service.TryStartUpgrade("vehicle-0001", VehicleCatalog.EngineerMk1Id, true, 21, 2);
            float partsBefore = cargo.GetAvailable(ProcessingCatalog.BasicPartId);
            bool cancelled = service.CancelActiveJob();
            Assert.IsTrue(cancelled);
            Assert.AreEqual(0f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
            // 30 parts reserved -> floor(75%) = 22 refunded back into available stock.
            Assert.AreEqual(partsBefore + 22f, cargo.GetAvailable(ProcessingCatalog.BasicPartId));
        }

        [Test]
        public void Tick_CompletesUpgrade_FiresJobUpgradeCompletedNotJobCompleted()
        {
            var cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            service.TryStartUpgrade("vehicle-0001", VehicleCatalog.EngineerMk1Id, true, 21, 2);

            bool upgradeCompletedFired = false; bool directCompletedFired = false;
            service.JobUpgradeCompleted += _ => upgradeCompletedFired = true;
            service.JobCompleted += (_, __) => directCompletedFired = true;

            service.Tick(VehicleCatalog.EngineerUpgradeRecipe.Seconds + 1f);
            Assert.IsTrue(upgradeCompletedFired);
            Assert.IsFalse(directCompletedFired);
            Assert.IsNull(service.ActiveJob);
        }

        [Test]
        public void RestoreGenericFixedJob_RebuildsReservationFromSavedAmounts()
        {
            var cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            var materials = new System.Collections.Generic.List<(string, float)> { (VehicleCatalog.MetalAlloyId, 15f), (VehicleCatalog.BiocompositeId, 15f) };
            service.RestoreGenericFixedJob(true, VehicleClass.Engineer, 2, VehicleCatalog.EngineerMk2Id, "vehicle-0001", 30, materials, 69f, 40f);
            Assert.AreEqual(30f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(15f, cargo.GetReserved(VehicleCatalog.MetalAlloyId));
            Assert.IsNotNull(service.ActiveJob);
            Assert.AreEqual(40f, service.ActiveJob.RemainingSeconds);
        }
    }

    internal static class LogisticsCargoTestExtensions
    {
        public static LogisticsCargo With(this LogisticsCargo cargo, string itemId, float amount)
        {
            cargo.Add(itemId, amount);
            return cargo;
        }
    }
}
