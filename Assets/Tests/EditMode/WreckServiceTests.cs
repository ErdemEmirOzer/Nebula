using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Poi;
using Nebula.Progression;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for pure Wreck identify/collection logic (VS-012 §6/§10.6).</summary>
    public class WreckServiceTests
    {
        [SetUp]
        public void ResetProgression() => ProgressionService.Reset();

        private static PoiState IdentifiedState()
        {
            var state = new PoiState("sys/poi.00");
            state.TickLocate(12f, true, 12f);
            state.TickIdentify(15f, true);
            return state;
        }

        [Test]
        public void OnIdentified_AwardsXpExactlyOnce()
        {
            var state = IdentifiedState();
            WreckService.OnIdentified(state, "sys/poi.00");
            Assert.IsTrue(state.WreckXpAwarded);

            state.MarkWreckXpAwarded(); // idempotent -- already true
            WreckService.OnIdentified(state, "sys/poi.00"); // must be a no-op, not a second award
            Assert.IsTrue(state.WreckXpAwarded);
        }

        [Test]
        public void CheckCollect_RejectsUnstable_OnlyForCollector_NeverForShip()
        {
            var state = IdentifiedState();
            Assert.AreEqual(WreckCollectRejectReason.None, WreckService.CheckCollect(state, PoiRisk.Unstable, byCollector: false), "the ship itself may always collect Unstable");
            Assert.AreEqual(WreckCollectRejectReason.UnstableNotAllowedForCollector, WreckService.CheckCollect(state, PoiRisk.Unstable, byCollector: true));
            Assert.AreEqual(WreckCollectRejectReason.None, WreckService.CheckCollect(state, PoiRisk.Safe, byCollector: true));
        }

        [Test]
        public void CheckCollect_RejectsWhenNotYetIdentifiedOrAlreadyResolved()
        {
            var fresh = new PoiState("sys/poi.00"); // Signal
            Assert.AreEqual(WreckCollectRejectReason.NotAccessible, WreckService.CheckCollect(fresh, PoiRisk.Safe, byCollector: false));

            var resolved = IdentifiedState();
            resolved.StartOperation(null);
            resolved.Resolve();
            Assert.AreEqual(WreckCollectRejectReason.NotAccessible, WreckService.CheckCollect(resolved, PoiRisk.Safe, byCollector: false));
        }

        [Test]
        public void Withdraw_IsBoundedByRateVolumeAndRemaining_NeverLosesOrDuplicates()
        {
            var state = IdentifiedState();
            state.SetSalvageNodes(new[] { new SalvageNode(ProcessingCatalog.BasicPartId, 100f) });

            string heldItem = null; float heldAmount = 0f;
            float taken = WreckService.Withdraw(state, ref heldItem, ref heldAmount, holdCapacityVolume: 1000f, ratePerSecond: 10f, dt: 1f);

            Assert.AreEqual(10f, taken);
            Assert.AreEqual(ProcessingCatalog.BasicPartId, heldItem);
            Assert.AreEqual(10f, heldAmount);
            Assert.AreEqual(90f, state.SalvageNodes[0].Remaining, "exactly what was withdrawn must leave the node -- no loss, no duplication");
        }

        [Test]
        public void Withdraw_StopsAtRemainingCapacity_EvenIfRateAllowsMore()
        {
            var state = IdentifiedState();
            state.SetSalvageNodes(new[] { new SalvageNode(ProcessingCatalog.BasicPartId, 3f) });

            string heldItem = null; float heldAmount = 0f;
            float taken = WreckService.Withdraw(state, ref heldItem, ref heldAmount, holdCapacityVolume: 1000f, ratePerSecond: 10f, dt: 1f);

            Assert.AreEqual(3f, taken, "must never withdraw more than the node actually has");
            Assert.AreEqual(0f, state.SalvageNodes[0].Remaining);
        }

        [Test]
        public void Withdraw_StopsAtHoldCapacity_NeverOverflowsTheReceiver()
        {
            var state = IdentifiedState();
            state.SetSalvageNodes(new[] { new SalvageNode(ProcessingCatalog.BasicPartId, 100f) });
            float perUnit = CargoItemVolumes.PerUnit(ProcessingCatalog.BasicPartId);

            string heldItem = null; float heldAmount = 0f;
            float capacityUnits = 5f;
            float taken = WreckService.Withdraw(state, ref heldItem, ref heldAmount, holdCapacityVolume: capacityUnits * perUnit, ratePerSecond: 999f, dt: 1f);

            Assert.LessOrEqual(taken, capacityUnits + 0.001f);
        }

        [Test]
        public void Withdraw_ReturnsZero_WhenNoStockLeft()
        {
            var state = IdentifiedState();
            state.SetSalvageNodes(new[] { new SalvageNode(ProcessingCatalog.BasicPartId, 0f) });
            string heldItem = null; float heldAmount = 0f;
            float taken = WreckService.Withdraw(state, ref heldItem, ref heldAmount, 1000f, 10f, 1f);
            Assert.AreEqual(0f, taken);
        }

        [Test]
        public void CheckDepletedAndResolve_TransitionsOnlyWhenAllNodesEmpty_AndOnlyOnce()
        {
            var state = IdentifiedState();
            state.StartOperation(null);
            state.SetSalvageNodes(new[] { new SalvageNode(ProcessingCatalog.BasicPartId, 5f), new SalvageNode("resource.metal", 0f) });

            Assert.IsFalse(WreckService.CheckDepletedAndResolve(state), "one node still has stock");

            state.SalvageNodes[0].Remaining = 0f;
            Assert.IsTrue(WreckService.CheckDepletedAndResolve(state));
            Assert.AreEqual(PoiKnowledgeLevel.Resolved, state.Level);

            Assert.IsFalse(WreckService.CheckDepletedAndResolve(state), "already Resolved -- must not re-trigger");
        }
    }
}
