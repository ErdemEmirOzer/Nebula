using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Modules;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for LogisticsCargo's capacity/reservation/claim model (VS-005 §8, tests 20-23).</summary>
    public class LogisticsCargoTests
    {
        [Test]
        public void CargoTierCapacities_Match150_300_600_1000_1500()
        {
            var cargo = new LogisticsCargo(150f);
            float[] expected = ModuleCatalog.CargoCapacityByTier;
            CollectionAssert.AreEqual(new[] { 150f, 300f, 600f, 1000f, 1500f }, expected);
            foreach (float c in expected) { cargo.SetCapacity(c); Assert.AreEqual(c, cargo.Capacity); }
        }

        /// <summary>
        /// The starting capacity lives in two places and they must agree.
        /// logistics_cargo is a free T1 module, so ShipModules.ApplyFunctionalEffect
        /// writes CargoCapacityByTier[0] into the hold on every new campaign and on
        /// every save load. If BaseCapacity drifts away from the table entry, the
        /// value set at Awake is silently overwritten the first time that effect
        /// runs — the bug this test exists to make loud.
        /// </summary>
        [Test]
        public void BaseCapacity_MatchesTier1TableEntry()
        {
            Assert.AreEqual(ModuleCatalog.CargoCapacityByTier[0], ShipLogistics.BaseCapacity,
                "ShipLogistics.BaseCapacity must equal CargoCapacityByTier[0]; the free T1 module effect overwrites the hold with the table entry on new campaign and on load.");
            Assert.AreEqual(150f, ShipLogistics.BaseCapacity, "a new campaign starts with a 150-volume hold");
        }

        /// <summary>
        /// A save taken at the old 120 capacity with a full hold must open sanely at
        /// 150: load writes the exact saved stock (LoadItem), then the restored T1
        /// module effect widens the hold via SetCapacity. Widening never touches
        /// stock, and the hold is no longer over capacity.
        /// </summary>
        [Test]
        public void LegacySaveAtOldCapacity_OpensWiderWithStockIntact()
        {
            var cargo = new LogisticsCargo(120f); // pre-change hold
            string water = ExtractionCatalog.ResourceIds[3];
            cargo.LoadItem(water, 120f); // saved completely full at the old capacity
            Assert.IsFalse(cargo.OverCapacity);

            cargo.SetCapacity(ModuleCatalog.CargoCapacityByTier[0]); // what the restored T1 effect does

            Assert.AreEqual(150f, cargo.Capacity);
            Assert.AreEqual(120f, cargo.GetStored(water), "widening capacity must not touch saved stock");
            Assert.IsFalse(cargo.OverCapacity);
            Assert.AreEqual(30f, cargo.AvailableVolume, "the extra 30 volume is immediately usable");
        }

        [Test]
        public void ItemVolumes_MatchSpec()
        {
            foreach (string raw in ExtractionCatalog.ResourceIds) Assert.AreEqual(1f, LogisticsCargo.VolumePerUnit(raw), raw);
            Assert.AreEqual(1f, LogisticsCargo.VolumePerUnit(ProcessingCatalog.BasicPartId));
            foreach (string mat in ProcessingCatalog.MaterialIds) Assert.AreEqual(2f, LogisticsCargo.VolumePerUnit(mat), mat);
            Assert.AreEqual(1f, LogisticsCargo.VolumePerUnit(ModuleCatalog.EnergyNuclearId));
            Assert.AreEqual(10f, LogisticsCargo.VolumePerUnit(ModuleCatalog.CoreNuclearId));
        }

        /// <summary>
        /// P2-DATA-301. Used volume is physical stock and nothing else.
        /// <para>
        /// This test used to assert the opposite half as well: that a "pending output
        /// claim" of 3 pushed a full 10-volume hold to 13. That second ledger counted
        /// against the player's capacity, had no producer or consumer anywhere in the
        /// game, and had no field in the save — so the number this test pinned could
        /// not survive a save/load, and only this test ever created one. The ledger is
        /// gone; what remains is the invariant that replaced it, which is stronger:
        /// there is no quantity outside physical stock that can consume capacity, so
        /// no such quantity can be lost by saving.
        /// </para>
        /// </summary>
        [Test]
        public void UsedVolumeIsPhysicalStockAlone_AndReservingCreatesNoFreeSpace()
        {
            var cargo = new LogisticsCargo(10f);
            string water = ExtractionCatalog.ResourceIds[3];
            cargo.LoadItem(water, 10f); // fills capacity exactly (volume 1 each)

            cargo.TryReserve("owner-a", water, 5f);
            Assert.AreEqual(10f, cargo.UsedVolume, "reserving must not create artificial free space");
            Assert.AreEqual(cargo.Total, cargo.UsedVolume,
                "used volume drifted from physical stock — a second, unsaveable ledger is back");

            Assert.AreEqual(0f, cargo.Add(water, 5f), "a full hold accepted more stock");
        }

        [Test]
        public void OverCapacityMigration_NeverDeletesStock_AndBlocksNewAdds()
        {
            var cargo = new LogisticsCargo(50f); // simulates a smaller tier-1 capacity than a legacy save held
            string water = ExtractionCatalog.ResourceIds[3];
            cargo.LoadItem(water, 200f); // legacy save contents, loaded verbatim regardless of capacity

            Assert.IsTrue(cargo.OverCapacity);
            Assert.AreEqual(200f, cargo.GetStored(water), "nothing may be trimmed or deleted");
            Assert.AreEqual(0f, cargo.Add(ExtractionCatalog.ResourceIds[0], 10f), "new stock is blocked while over capacity");
        }

        [Test]
        public void ConsumeReservationForRefund_IsIdempotent()
        {
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(ProcessingCatalog.BasicPartId, 40f);
            cargo.TryReserve("job", ProcessingCatalog.BasicPartId, 40f);

            var refunded1 = cargo.ConsumeReservationForRefund("job", 0.75f);
            Assert.AreEqual(30f, refunded1[ProcessingCatalog.BasicPartId]);
            Assert.AreEqual(30f, cargo.GetStored(ProcessingCatalog.BasicPartId));

            var refunded2 = cargo.ConsumeReservationForRefund("job", 0.75f); // nothing left to refund
            Assert.AreEqual(0, refunded2.Count);
            Assert.AreEqual(30f, cargo.GetStored(ProcessingCatalog.BasicPartId), "a second refund call must not duplicate or destroy stock");
        }
    }
}
