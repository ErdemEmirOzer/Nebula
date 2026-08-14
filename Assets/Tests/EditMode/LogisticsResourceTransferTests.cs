using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the full drill-&gt;Collector-&gt;center-&gt;ship chain
    /// (VS-011 §3/§5/§10.6/§10.7): Mk I/II transfer rates and end-to-end
    /// resource conservation through every step of the single transaction
    /// authority.
    /// </summary>
    public class LogisticsResourceTransferTests
    {
        private static PlanetProfile Profile(string planetId) => new PlanetProfile(
            new PlanetManifest(0, planetId, ResourceFamily.Metal, Rarity.Legendary, 40000f, 0f, 0f, 0f, 1200f,
                recommendedStart: false, EnvironmentClass.Calm, surfaceLayoutSeed: 1UL));

        private static DrillRuntime FullDrill(string worldId = "sys000/pl01") =>
            new DrillRuntime(Profile(worldId), DrillLifecycle.Active, 0f, 0f, stored: 1_000_000f, accumulator: 0f);

        [Test]
        public void CollectFromDrill_MkII_MovesAtDoubleTheMkIRate()
        {
            DrillRuntime drill = FullDrill();
            string heldItem = null; float heldAmount = 0f;

            float taken = ResourceTransferService.CollectFromDrill(drill, ref heldItem, ref heldAmount,
                holdCapacityVolume: VehicleCatalog.CollectorMk2.CargoCapacity, dt: 1f, ratePerSecond: VehicleCatalog.CollectorMk2.TransferVolumePerSecond);

            Assert.AreEqual(20f, taken, "Mk II moves 20 vol/s, twice Mk I's 10 vol/s");
        }

        [Test]
        public void DeliverToCenter_BoundedByBothVehicleRateAndCenterAggregateBudget()
        {
            var center = new LogisticsCargo(LogisticsCenterCatalog.T1.Capacity);
            string heldItem = "resource.metal"; float heldAmount = 100f;

            // Vehicle could move 20/s (Mk II) but only 5 remains of the center's shared per-tick budget.
            float delivered = ResourceTransferService.DeliverToCenter(center, ref heldItem, ref heldAmount,
                ratePerSecond: 20f, dt: 1f, remainingAggregateBudget: 5f);

            Assert.AreEqual(5f, delivered, "the smaller of vehicle rate and remaining aggregate budget wins");
            Assert.AreEqual(95f, heldAmount);
            Assert.AreEqual(5f, center.GetStored("resource.metal"));
        }

        [Test]
        public void DeliverToCenter_RejectsItemsOutsideTheAcceptedCatalog()
        {
            var center = new LogisticsCargo(1000f);
            string heldItem = Modules.ModuleCatalog.EnergyNuclearId; float heldAmount = 50f;

            float delivered = ResourceTransferService.DeliverToCenter(center, ref heldItem, ref heldAmount, 20f, 1f, 100f);

            Assert.AreEqual(0f, delivered, "nuclear energy must never enter the center's inventory");
            Assert.AreEqual(50f, heldAmount, "rejected cargo stays exactly where it was -- never silently destroyed");
        }

        [Test]
        public void DeliverToCenter_NeverOverfillsCenterCapacity()
        {
            var center = new LogisticsCargo(10f);
            center.Add("resource.metal", 8f); // 2 volume of free space left
            string heldItem = "resource.metal"; float heldAmount = 50f;

            float delivered = ResourceTransferService.DeliverToCenter(center, ref heldItem, ref heldAmount, 20f, 1f, 100f);

            Assert.AreEqual(2f, delivered);
            Assert.AreEqual(10f, center.GetStored("resource.metal"));
            Assert.AreEqual(48f, heldAmount, "the undelivered remainder is never lost, it just stays held");
        }

        [Test]
        public void FullChain_DrillToVehicleToCenterToShip_ConservesTotalQuantity()
        {
            DrillRuntime drill = FullDrill();
            var center = new LogisticsCargo(LogisticsCenterCatalog.T1.Capacity);
            var shipCargo = new LogisticsCargo(1000f);

            float drillStart = drill.StoredAmount;
            string heldItem = null; float heldAmount = 0f;

            // Collector Mk I fills its 40-volume hold from the drill.
            ResourceTransferService.CollectFromDrill(drill, ref heldItem, ref heldAmount, VehicleCatalog.Collector.CargoCapacity, dt: 10f);
            Assert.AreEqual(40f, heldAmount);
            float takenFromDrill = drillStart - drill.StoredAmount;
            Assert.AreEqual(40f, takenFromDrill);

            // Unloads fully into the center.
            float deliveredToCenter = ResourceTransferService.DeliverToCenter(center, ref heldItem, ref heldAmount,
                VehicleCatalog.Collector.TransferVolumePerSecond, dt: 10f, remainingAggregateBudget: 1000f);
            Assert.AreEqual(40f, deliveredToCenter);
            Assert.AreEqual(0f, heldAmount);
            Assert.AreEqual(40f, center.GetStored("resource.metal"));

            // Player manually withdraws it all into the ship (VS-011 §8 manual transfer).
            bool removed = center.TryConsumeImmediate("resource.metal", 40f);
            Assert.IsTrue(removed);
            float addedToShip = shipCargo.Add("resource.metal", 40f);

            Assert.AreEqual(40f, addedToShip);
            Assert.AreEqual(0f, center.GetStored("resource.metal"));
            Assert.AreEqual(40f, shipCargo.GetStored("resource.metal"), "the full 40 units survive drill -> vehicle -> center -> ship intact");
        }
    }
}
