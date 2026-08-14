using NUnit.Framework;
using Nebula.Poi;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure POI knowledge/timer state machine (VS-012 §3/§4/§10.3/§10.4).</summary>
    public class PoiStateTests
    {
        [Test]
        public void FreshState_StartsAtSignal()
        {
            var state = new PoiState("sys/poi.00");
            Assert.AreEqual(PoiKnowledgeLevel.Signal, state.Level);
        }

        [Test]
        public void TickLocate_CompletesOnceElapsedReachesRequiredSeconds()
        {
            var state = new PoiState("sys/poi.00");
            bool crossed = state.TickLocate(12f, true, 12f);
            Assert.IsTrue(crossed);
            Assert.AreEqual(PoiKnowledgeLevel.Located, state.Level);
        }

        [Test]
        public void TickLocate_PausesWithoutResetting_WhenConditionLost()
        {
            var state = new PoiState("sys/poi.00");
            state.TickLocate(5f, true, 12f);
            Assert.AreEqual(5f, state.LocateElapsed);

            bool crossed = state.TickLocate(100f, false, 12f); // out of range -- must not advance at all
            Assert.IsFalse(crossed);
            Assert.AreEqual(5f, state.LocateElapsed, "elapsed must never reset, only pause");
            Assert.AreEqual(PoiPauseReason.OutOfRange, state.PauseReason);

            state.TickLocate(7f, true, 12f);
            Assert.AreEqual(PoiKnowledgeLevel.Located, state.Level, "resuming must continue from where it paused, not restart");
        }

        [Test]
        public void LocateAndIdentify_AreTwoSeparateGates_NeverCombinedInOneCall()
        {
            var state = new PoiState("sys/poi.00");
            state.TickLocate(12f, true, 12f);
            Assert.AreEqual(PoiKnowledgeLevel.Located, state.Level);
            Assert.AreNotEqual(PoiKnowledgeLevel.Identified, state.Level, "reaching Located must never also silently reach Identified");

            state.TickIdentify(15f, true);
            Assert.AreEqual(PoiKnowledgeLevel.Identified, state.Level);
        }

        [Test]
        public void TickIdentify_OnlyProgressesFromLocated()
        {
            var state = new PoiState("sys/poi.00"); // still Signal
            Assert.IsFalse(state.TickIdentify(100f, true));
            Assert.AreEqual(0f, state.IdentifyElapsed);
        }

        [Test]
        public void StartOperation_OnlyFromIdentified()
        {
            var state = new PoiState("sys/poi.00");
            Assert.IsFalse(state.StartOperation(null), "cannot start from Signal");

            state.TickLocate(12f, true, 12f);
            state.TickIdentify(15f, true);
            Assert.IsTrue(state.StartOperation("vehicle-0000"));
            Assert.AreEqual(PoiKnowledgeLevel.Active, state.Level);
            Assert.AreEqual("vehicle-0000", state.AssignedVehicleId);
        }

        [Test]
        public void TickOperation_PausesNotResets_NoDamageNoDeathNoRandomLoss()
        {
            var state = new PoiState("sys/poi.00");
            state.TickLocate(12f, true, 12f);
            state.TickIdentify(15f, true);
            state.StartOperation(null);

            state.TickOperation(20f, true, 60f, PoiPauseReason.OutOfRange);
            Assert.AreEqual(20f, state.OperationElapsed);

            bool crossed = state.TickOperation(1000f, false, 60f, PoiPauseReason.OutOfRange);
            Assert.IsFalse(crossed);
            Assert.AreEqual(20f, state.OperationElapsed, "no reset, no forward drift while paused either");
            Assert.AreEqual(PoiPauseReason.OutOfRange, state.PauseReason);

            Assert.IsTrue(state.TickOperation(40f, true, 60f, PoiPauseReason.OutOfRange));
        }

        [Test]
        public void CancelOperation_ReturnsToIdentified_ClearsAssignment()
        {
            var state = new PoiState("sys/poi.00");
            state.TickLocate(12f, true, 12f);
            state.TickIdentify(15f, true);
            state.StartOperation("vehicle-0000");
            state.TickOperation(10f, true, 60f, PoiPauseReason.None);

            state.CancelOperationBackToIdentified();
            Assert.AreEqual(PoiKnowledgeLevel.Identified, state.Level);
            Assert.AreEqual(0f, state.OperationElapsed);
            Assert.IsNull(state.AssignedVehicleId);
        }

        [Test]
        public void Resolve_IsTerminal_RewardNeverRepeats()
        {
            var state = new PoiState("sys/poi.00");
            state.TickLocate(12f, true, 12f);
            state.TickIdentify(15f, true);
            state.StartOperation(null);
            state.MarkAnomalyXpAwarded();
            state.Resolve();

            Assert.AreEqual(PoiKnowledgeLevel.Resolved, state.Level);
            Assert.IsTrue(state.AnomalyXpAwarded, "the one-shot flag itself persists permanently as the anti-repeat guard");
            Assert.IsFalse(state.StartOperation(null), "a Resolved POI can never restart the operation");
        }

        [Test]
        public void WreckDepleted_TrueOnlyWhenAllNodesAreEmpty()
        {
            var state = new PoiState("sys/poi.00");
            state.SetSalvageNodes(new[] { new SalvageNode("part.basic", 10f), new SalvageNode("resource.metal", 5f) });
            Assert.IsFalse(state.Depleted);

            state.SalvageNodes[0].Remaining = 0f;
            Assert.IsFalse(state.Depleted, "one empty node is not the whole Wreck depleted");
            state.SalvageNodes[1].Remaining = 0f;
            Assert.IsTrue(state.Depleted);
        }

        [Test]
        public void FacilityChoice_LocksOnce_NeverOverwritten()
        {
            var state = new PoiState("sys/poi.00");
            state.LockFacilityChoice(FacilityChoice.Dismantle);
            state.LockFacilityChoice(FacilityChoice.SensorRelay); // must be ignored
            Assert.AreEqual(FacilityChoice.Dismantle, state.Choice);
        }

        [Test]
        public void FacilityOutput_WithdrawIsBoundedAndConserved()
        {
            var state = new PoiState("sys/poi.00");
            state.SetFacilityDismantleOutput(50f, 20f, "material.metal_alloy");

            var (basic, special) = state.WithdrawFacilityOutput(30f, 100f);
            Assert.AreEqual(30f, basic);
            Assert.AreEqual(20f, special, "bounded by what's actually remaining, never manufactured beyond it");
            Assert.AreEqual(20f, state.FacilityOutputBasicRemaining);
            Assert.AreEqual(0f, state.FacilityOutputSpecialRemaining);
        }
    }
}
