using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests proving the center's aggregate transfer-rate budget
    /// (VS-011 §5: "Çoklu araç aynı anda boşaltılırken toplam merkezin
    /// T1/T2 20/40 hacim/saniye limitini aşmamalı") holds even when several
    /// vehicles are individually fast enough to exceed it alone — the exact
    /// rationing mechanism <see cref="Sectors.SystemStreamingDirector"/> and
    /// <see cref="CollectorRouteDirector"/> both use via
    /// <see cref="ResourceTransferService.DeliverToCenter"/>'s
    /// remainingAggregateBudget parameter, exercised here in canonical
    /// vehicle-id order exactly like both real call sites.
    /// </summary>
    public class AggregateTransferBudgetTests
    {
        [Test]
        public void TwoMkIICollectors_UnloadingSameTick_T1Center_NeverExceeds20VolPerSecond()
        {
            var center = new LogisticsCargo(LogisticsCenterCatalog.T1.Capacity);
            float dt = 1f;
            float budget = LogisticsCenterCatalog.T1.TransferVolumePerSecond * dt; // 20

            // Two Mk II Collectors (20 vol/s each) both fully loaded and Unloading the same tick,
            // processed in canonical vehicle-id order exactly like CollectorRouteDirector/TickCenterDocksRemote.
            string heldA = "resource.metal"; float amountA = 80f;
            string heldB = "resource.metal"; float amountB = 80f;

            float deliveredA = ResourceTransferService.DeliverToCenter(center, ref heldA, ref amountA,
                VehicleCatalog.CollectorMk2.TransferVolumePerSecond, dt, budget);
            budget -= deliveredA;

            float deliveredB = ResourceTransferService.DeliverToCenter(center, ref heldB, ref amountB,
                VehicleCatalog.CollectorMk2.TransferVolumePerSecond, dt, budget);
            budget -= deliveredB;

            Assert.AreEqual(20f, deliveredA, "the first vehicle in canonical order gets its full 20/s share");
            Assert.AreEqual(0f, deliveredB, "the second gets nothing left -- the T1 aggregate cap is 20/s total, not per-vehicle");
            Assert.AreEqual(20f, center.GetStored("resource.metal"), "total delivered this tick never exceeds the T1 center's 20 vol/s cap");
            Assert.AreEqual(60f, amountA);
            Assert.AreEqual(80f, amountB, "untouched -- nothing lost, it just waits for the next tick's budget");
        }

        [Test]
        public void TwoMkICollectors_UnloadingSameTick_T2Center_SplitsUpTo40VolPerSecond()
        {
            var center = new LogisticsCargo(LogisticsCenterCatalog.T2.Capacity);
            float dt = 1f;
            float budget = LogisticsCenterCatalog.T2.TransferVolumePerSecond * dt; // 40

            string heldA = "resource.metal"; float amountA = 40f;
            string heldB = "resource.metal"; float amountB = 40f;

            float deliveredA = ResourceTransferService.DeliverToCenter(center, ref heldA, ref amountA, VehicleCatalog.Collector.TransferVolumePerSecond, dt, budget);
            budget -= deliveredA;
            float deliveredB = ResourceTransferService.DeliverToCenter(center, ref heldB, ref amountB, VehicleCatalog.Collector.TransferVolumePerSecond, dt, budget);
            budget -= deliveredB;

            Assert.AreEqual(10f, deliveredA, "each Mk I only wants 10/s -- well under the T2 40/s cap, so both are fully served");
            Assert.AreEqual(10f, deliveredB);
            Assert.AreEqual(20f, center.GetStored("resource.metal"));
            Assert.AreEqual(20f, budget, "T2's spare budget (40 - 10 - 10 = 20) simply goes unused this tick, never overspent elsewhere");
        }
    }
}
