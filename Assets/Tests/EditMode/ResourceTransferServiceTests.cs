using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the shared drill&lt;-&gt;vehicle-&lt;-&gt;ship transfer
    /// authority (VS-007 §5): rate, capacity bounds, and total conservation.
    /// </summary>
    public class ResourceTransferServiceTests
    {
        private static PlanetProfile Profile(string planetId, Rarity rarity) => new PlanetProfile(
            new PlanetManifest(0, planetId, ResourceFamily.Metal, rarity, 40000f, 0f, 0f, 0f, 1200f,
                recommendedStart: false, EnvironmentClass.Calm, surfaceLayoutSeed: 1UL));

        private static DrillRuntime FullDrill(string worldId = "sys000/pl01")
        {
            var profile = Profile(worldId, Rarity.Legendary);
            // Restore-constructor clamps to StorageCapacity, so a huge "stored" always yields a full drill regardless of rarity.
            return new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, stored: 1_000_000f, accumulator: 0f);
        }

        [Test]
        public void CollectFromDrill_MovesAtStandardRate_BoundedByHoldCapacity()
        {
            DrillRuntime drill = FullDrill();
            float capacityBefore = drill.StoredAmount;
            string heldItem = null; float heldAmount = 0f;

            float taken = ResourceTransferService.CollectFromDrill(drill, ref heldItem, ref heldAmount, holdCapacityVolume: 40f, dt: 1f);

            Assert.AreEqual(10f, taken); // 10 volume/s * 1s, raw resources are 1 volume/unit
            Assert.AreEqual(10f, heldAmount);
            Assert.AreEqual(capacityBefore - 10f, drill.StoredAmount);
            Assert.IsNotNull(heldItem);
        }

        [Test]
        public void CollectFromDrill_StopsExactlyAtHoldCapacity_NeverOverfills()
        {
            DrillRuntime drill = FullDrill();
            string heldItem = null; float heldAmount = 0f;

            // 5 seconds at 10/s would want 50, but the hold only fits 40.
            ResourceTransferService.CollectFromDrill(drill, ref heldItem, ref heldAmount, holdCapacityVolume: 40f, dt: 5f);
            Assert.AreEqual(40f, heldAmount);

            float takenMore = ResourceTransferService.CollectFromDrill(drill, ref heldItem, ref heldAmount, holdCapacityVolume: 40f, dt: 1f);
            Assert.AreEqual(0f, takenMore, "a full hold must accept nothing more");
            Assert.AreEqual(40f, heldAmount);
        }

        [Test]
        public void CollectFromDrill_NeverTakesMoreThanTheDrillHas()
        {
            var profile = Profile("sys000/pl02", Rarity.Common);
            var drill = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, stored: 3f, accumulator: 0f);
            string heldItem = null; float heldAmount = 0f;

            ResourceTransferService.CollectFromDrill(drill, ref heldItem, ref heldAmount, holdCapacityVolume: 40f, dt: 10f);
            Assert.AreEqual(3f, heldAmount);
            Assert.AreEqual(0f, drill.StoredAmount);
        }

        [Test]
        public void DeliverToShip_MovesAtStandardRate_BoundedByShipCapacity()
        {
            var cargo = new LogisticsCargo(5f); // tiny ship capacity
            string heldItem = "resource.metal"; float heldAmount = 20f;

            float delivered = ResourceTransferService.DeliverToShip(cargo, ref heldItem, ref heldAmount, dt: 10f); // wants 100
            Assert.AreEqual(5f, delivered, "delivery must stop at the ship's actual free capacity");
            Assert.AreEqual(15f, heldAmount, "undelivered cargo must stay on the vehicle, never vanish");
            Assert.AreEqual(5f, cargo.GetStored("resource.metal"));
        }

        [Test]
        public void DeliverToShip_FullShip_DeliversNothing_HeldCargoUnchanged()
        {
            var cargo = new LogisticsCargo(10f);
            cargo.Add("resource.metal", 10f); // already full
            string heldItem = "resource.metal"; float heldAmount = 8f;

            float delivered = ResourceTransferService.DeliverToShip(cargo, ref heldItem, ref heldAmount, dt: 1f);
            Assert.AreEqual(0f, delivered);
            Assert.AreEqual(8f, heldAmount, "\"KARGO DOLU\" must leave the vehicle's cargo exactly as it was");
        }

        [Test]
        public void ReturnToDrill_BoundedByRemainingDrillCapacity_NeverExceedsIt()
        {
            var profile = Profile("sys000/pl03", Rarity.Common);
            var drill = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, stored: 0f, accumulator: 0f);
            float capacity = drill.StorageCapacity;
            string heldItem = "resource.metal"; float heldAmount = capacity + 50f; // more than the drill can ever hold

            float returned = ResourceTransferService.ReturnToDrill(drill, ref heldItem, ref heldAmount, dt: 1000f);
            Assert.LessOrEqual(drill.StoredAmount, capacity + 0.001f);
            Assert.AreEqual(capacity, returned, 0.01f);
            Assert.Greater(heldAmount, 0f, "cargo that doesn't fit must stay on the vehicle, never be destroyed");
        }

        [Test]
        public void FullCycle_DrillPlusHeldPlusShip_TotalIsConservedThroughoutTransfer()
        {
            DrillRuntime drill = FullDrill();
            var cargo = new LogisticsCargo(1000f);
            string itemId = ExtractionCatalog.ResourceIds[(int)drill.Family];
            string heldItem = null; float heldAmount = 0f;

            float total0 = drill.StoredAmount + heldAmount + cargo.GetStored(itemId);

            // Collect for a while.
            for (int i = 0; i < 4; i++)
                ResourceTransferService.CollectFromDrill(drill, ref heldItem, ref heldAmount, holdCapacityVolume: 40f, dt: 1f);
            float total1 = drill.StoredAmount + heldAmount + cargo.GetStored(itemId);
            Assert.AreEqual(total0, total1, 0.001f, "collecting must move, never create or destroy, resource units");

            // Deliver it all to the ship.
            for (int i = 0; i < 5; i++)
                ResourceTransferService.DeliverToShip(cargo, ref heldItem, ref heldAmount, dt: 1f);
            float total2 = drill.StoredAmount + heldAmount + cargo.GetStored(itemId);
            Assert.AreEqual(total0, total2, 0.001f, "delivering must also conserve the total");
            Assert.AreEqual(0f, heldAmount);
        }
    }
}
