using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the single-slot Hangar production queue (VS-007 §1-2).</summary>
    public class HangarProductionServiceTests
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
        public void TryStart_RejectsWithoutMaterialSelection()
        {
            var service = new HangarProductionService(FullCargo());
            HangarProductionRejectReason reason = service.TryStartProduction(VehicleClass.Scout, null, characterLevel: 20, hangarTier: 1);
            Assert.AreEqual(HangarProductionRejectReason.MaterialNotSelected, reason);
            Assert.IsNull(service.ActiveJob);
        }

        [Test]
        public void TryStart_RejectsLevelTooLow_NoStateChanged()
        {
            LogisticsCargo cargo = FullCargo();
            float partsBefore = cargo.GetStored(ProcessingCatalog.BasicPartId);
            var service = new HangarProductionService(cargo);

            HangarProductionRejectReason reason = service.TryStartProduction(VehicleClass.Scout, Metal, characterLevel: 5, hangarTier: 1);
            Assert.AreEqual(HangarProductionRejectReason.LevelTooLow, reason);
            Assert.IsNull(service.ActiveJob);
            Assert.AreEqual(partsBefore, cargo.GetStored(ProcessingCatalog.BasicPartId), "a rejected start must not touch cargo");
        }

        [Test]
        public void TryStart_RejectsHangarTierTooLow()
        {
            var service = new HangarProductionService(FullCargo());
            HangarProductionRejectReason reason = service.TryStartProduction(VehicleClass.Scout, Metal, characterLevel: 20, hangarTier: 0);
            Assert.AreEqual(HangarProductionRejectReason.HangarTierTooLow, reason);
        }

        [Test]
        public void TryStart_RejectsInsufficientResources_NoPartialReservation()
        {
            var cargo = new LogisticsCargo(500f);
            cargo.Add(ProcessingCatalog.BasicPartId, 5f); // Scout needs 20
            cargo.Add(Metal, 100f);
            var service = new HangarProductionService(cargo);

            HangarProductionRejectReason reason = service.TryStartProduction(VehicleClass.Scout, Metal, characterLevel: 20, hangarTier: 1);
            Assert.AreEqual(HangarProductionRejectReason.InsufficientResources, reason);
            Assert.AreEqual(0f, cargo.GetReserved(Metal), "a failed reservation must leave nothing reserved");
        }

        [Test]
        public void TryStart_ReservesFullCostAtomically_QueueBusyBlocksSecondStart()
        {
            LogisticsCargo cargo = FullCargo();
            var service = new HangarProductionService(cargo);

            HangarProductionRejectReason reason = service.TryStartProduction(VehicleClass.Scout, Metal, characterLevel: 20, hangarTier: 1);
            Assert.AreEqual(HangarProductionRejectReason.None, reason);
            Assert.AreEqual(20f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(15f, cargo.GetReserved(Metal));
            Assert.AreEqual(80f, cargo.GetAvailable(ProcessingCatalog.BasicPartId));

            HangarProductionRejectReason second = service.TryStartProduction(VehicleClass.Collector, Biocomposite, characterLevel: 20, hangarTier: 1);
            Assert.AreEqual(HangarProductionRejectReason.QueueBusy, second);
        }

        [Test]
        public void Tick_PauseAndOffline_NeverAdvancesTheJob()
        {
            var service = new HangarProductionService(FullCargo());
            service.TryStartProduction(VehicleClass.Scout, Metal, characterLevel: 20, hangarTier: 1);
            float remainingBefore = service.ActiveJob.RemainingSeconds;

            Assert.IsNull(service.Tick(0f)); // paused / offline dt=0
            Assert.AreEqual(remainingBefore, service.ActiveJob.RemainingSeconds);
        }

        [Test]
        public void Tick_CompletesJob_ConsumesReservationAndMintsOneVehicle()
        {
            LogisticsCargo cargo = FullCargo();
            var service = new HangarProductionService(cargo);
            service.TryStartProduction(VehicleClass.Scout, Metal, characterLevel: 20, hangarTier: 1);

            string vehicleId = null;
            service.JobCompleted += (job, id) => vehicleId = id;

            string result = service.Tick(1000f); // force-complete regardless of BuildSeconds
            Assert.IsNotNull(result);
            Assert.AreEqual(result, vehicleId);
            Assert.IsNull(service.ActiveJob);
            Assert.AreEqual(0f, cargo.GetReserved(ProcessingCatalog.BasicPartId), "completion must consume the reservation permanently");
            Assert.AreEqual(80f, cargo.GetStored(ProcessingCatalog.BasicPartId));
        }

        [Test]
        public void Tick_AfterCompletion_NeverMintsASecondVehicle()
        {
            var service = new HangarProductionService(FullCargo());
            service.TryStartProduction(VehicleClass.Scout, Metal, characterLevel: 20, hangarTier: 1);
            Assert.IsNotNull(service.Tick(1000f));
            Assert.IsNull(service.Tick(1f), "ticking after ActiveJob is already null must be a safe no-op");
        }

        [Test]
        public void CancelActiveJob_RefundsFloor75Percent()
        {
            var cargo = new LogisticsCargo(500f);
            cargo.Add(ProcessingCatalog.BasicPartId, 20f);
            cargo.Add(Metal, 15f);
            var service = new HangarProductionService(cargo);
            service.TryStartProduction(VehicleClass.Scout, Metal, characterLevel: 20, hangarTier: 1);

            bool cancelled = service.CancelActiveJob();
            Assert.IsTrue(cancelled);
            Assert.IsNull(service.ActiveJob);
            Assert.AreEqual(15f, cargo.GetStored(ProcessingCatalog.BasicPartId)); // floor(20*0.75)
            Assert.AreEqual(11f, cargo.GetStored(Metal)); // floor(15*0.75)
        }

        [Test]
        public void CancelActiveJob_SecondCallIsSafeNoOp()
        {
            var service = new HangarProductionService(FullCargo());
            service.TryStartProduction(VehicleClass.Scout, Metal, characterLevel: 20, hangarTier: 1);
            Assert.IsTrue(service.CancelActiveJob());
            Assert.IsFalse(service.CancelActiveJob());
        }

        [Test]
        public void RestoreActiveJob_RebuildsReservationFromRestoredCargoWithoutSpending()
        {
            LogisticsCargo cargo = FullCargo(); // cargo already holds the physically-restored amounts, including the reserved slice
            var service = new HangarProductionService(cargo);

            service.RestoreActiveJob(VehicleClass.Collector, Biocomposite, reservedParts: 30, reservedMaterial: 20,
                totalSeconds: 45f, remainingSeconds: 12f);

            Assert.IsNotNull(service.ActiveJob);
            Assert.AreEqual(12f, service.ActiveJob.RemainingSeconds);
            Assert.AreEqual(30f, cargo.GetReserved(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(20f, cargo.GetReserved(Biocomposite));
        }
    }
}
