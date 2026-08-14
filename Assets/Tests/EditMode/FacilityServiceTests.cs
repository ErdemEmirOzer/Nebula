using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Poi;
using Nebula.Progression;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure Abandoned Facility Dismantle/Relay logic (VS-012 §7/§10.7).</summary>
    public class FacilityServiceTests
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

        private static PoiManifest Manifest(PoiRisk risk, ulong rewardSeed) =>
            new PoiManifest("sys/poi.00", "sys", 0, PoiType.AbandonedFacility, risk, Vector3.zero, WreckSize.Small, rewardSeed, 0UL);

        [Test]
        public void DismantleAndRelaySeconds_MatchSafeAndUnstableSpec()
        {
            Assert.AreEqual(180f, FacilityService.DismantleSeconds(PoiRisk.Safe));
            Assert.AreEqual(270f, FacilityService.DismantleSeconds(PoiRisk.Unstable));
            Assert.AreEqual(120f, FacilityService.RelaySeconds(PoiRisk.Safe));
            Assert.AreEqual(180f, FacilityService.RelaySeconds(PoiRisk.Unstable));
        }

        [Test]
        public void StartDismantle_LocksChoice_EntersActive_CostsNoCargo()
        {
            var state = IdentifiedState();
            Assert.IsTrue(FacilityService.StartDismantle(state, null));
            Assert.AreEqual(FacilityChoice.Dismantle, state.Choice);
            Assert.AreEqual(PoiKnowledgeLevel.Active, state.Level);
        }

        [Test]
        public void StartRelay_FailsWithoutEnoughCargo_ReservesNothing()
        {
            var state = IdentifiedState();
            var cargo = new LogisticsCargo(1000f);
            // no cargo added at all
            Assert.IsFalse(FacilityService.StartRelay(state, "sys/poi.00", cargo, null));
            Assert.AreEqual(FacilityChoice.None, state.Choice);
            Assert.AreEqual(PoiKnowledgeLevel.Identified, state.Level);
        }

        [Test]
        public void StartRelay_AtomicallyReservesExactCost_LocksChoice_EntersActive()
        {
            var state = IdentifiedState();
            var cargo = new LogisticsCargo(1000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 25f);
            cargo.Add(PoiCatalog.FacilityRelayDiamondCompositeId, 8f);
            cargo.Add(PoiCatalog.FacilityRelayBiocompositeId, 5f);

            Assert.IsTrue(FacilityService.StartRelay(state, "sys/poi.00", cargo, null));
            Assert.AreEqual(FacilityChoice.SensorRelay, state.Choice);
            Assert.AreEqual(PoiKnowledgeLevel.Active, state.Level);
            Assert.AreEqual(0f, cargo.GetAvailable(ProcessingCatalog.BasicPartId), "reserved items are no longer available for anything else");
            Assert.AreEqual(25f, cargo.GetStored(ProcessingCatalog.BasicPartId), "reserved items stay physically present until consumed");
        }

        [Test]
        public void Choice_IsExclusiveAndIrreversible_SecondStartAttemptAlwaysFails()
        {
            var state = IdentifiedState();
            Assert.IsTrue(FacilityService.StartDismantle(state, null));

            var cargo = new LogisticsCargo(1000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 25f);
            cargo.Add(PoiCatalog.FacilityRelayDiamondCompositeId, 8f);
            cargo.Add(PoiCatalog.FacilityRelayBiocompositeId, 5f);
            Assert.IsFalse(FacilityService.StartRelay(state, "sys/poi.00", cargo, null), "a POI can never flip to the other outcome once one is chosen");
            Assert.AreEqual(0f, cargo.GetReserved(ProcessingCatalog.BasicPartId), "the rejected path must never reserve anything");
        }

        [Test]
        public void CompleteDismantle_ProducesRewardWithinRange_AwardsXpOnce_Resolves()
        {
            for (ulong seed = 0; seed < 20; seed++)
            {
                var state = IdentifiedState();
                FacilityService.StartDismantle(state, null);
                PoiManifest manifest = Manifest(PoiRisk.Safe, seed * 7919UL);

                FacilityService.CompleteDismantle(state, manifest);

                Assert.GreaterOrEqual(state.FacilityOutputBasicRemaining, PoiCatalog.FacilityDismantleMinBasic - 0.001f);
                Assert.LessOrEqual(state.FacilityOutputBasicRemaining, PoiCatalog.FacilityDismantleMaxBasic + 0.001f);
                Assert.GreaterOrEqual(state.FacilityOutputSpecialRemaining, PoiCatalog.FacilityDismantleMinSpecial - 0.001f);
                Assert.LessOrEqual(state.FacilityOutputSpecialRemaining, PoiCatalog.FacilityDismantleMaxSpecial + 0.001f);
                CollectionAssert.Contains(ProcessingCatalog.MaterialIds, state.FacilityOutputSpecialItemId);
                Assert.IsTrue(state.FacilityXpAwarded);
                Assert.AreEqual(PoiKnowledgeLevel.Resolved, state.Level);
            }
        }

        [Test]
        public void CompleteDismantle_IsIdempotent_NeverRerollsOrDoubleAwards()
        {
            var state = IdentifiedState();
            FacilityService.StartDismantle(state, null);
            PoiManifest manifest = Manifest(PoiRisk.Safe, 42UL);

            FacilityService.CompleteDismantle(state, manifest);
            float basicAfterFirst = state.FacilityOutputBasicRemaining;

            // state.Choice stays Dismantle after Resolve -- a second call must be a no-op (guarded via state.Choice check, not Level)
            FacilityService.CompleteDismantle(state, manifest);
            Assert.AreEqual(basicAfterFirst, state.FacilityOutputBasicRemaining, "must never top up / reroll a second time");
        }

        [Test]
        public void CompleteRelay_ConsumesReservationPermanently_OpensUpToTwoSignals_AwardsXpOnce()
        {
            var state = IdentifiedState();
            var cargo = new LogisticsCargo(1000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 25f);
            cargo.Add(PoiCatalog.FacilityRelayDiamondCompositeId, 8f);
            cargo.Add(PoiCatalog.FacilityRelayBiocompositeId, 5f);
            FacilityService.StartRelay(state, "sys/poi.00", cargo, null);
            PoiManifest manifest = Manifest(PoiRisk.Safe, 11UL);

            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            var candidates = new List<(string systemId, float distance)> { ("sysA", 100f), ("sysB", 200f), ("sysC", 300f) };

            FacilityService.CompleteRelay(state, manifest, cargo, candidates, knowledge, frontier);

            Assert.AreEqual(0f, cargo.GetStored(ProcessingCatalog.BasicPartId), "reservation must be permanently consumed, not merely released");
            int signalCount = 0;
            foreach (var (systemId, _) in candidates) if (knowledge.GetLevel(systemId) == SystemKnowledgeLevel.Signal) signalCount++;
            Assert.LessOrEqual(signalCount, PoiCatalog.FacilityRelaySignalCount);
            Assert.GreaterOrEqual(signalCount, 1);
            Assert.IsTrue(state.FacilityXpAwarded);
            Assert.AreEqual(PoiKnowledgeLevel.Resolved, state.Level);
        }

        [Test]
        public void CompleteRelay_NeverOpensSignalTwice_WhenCalledAgain()
        {
            var state = IdentifiedState();
            var cargo = new LogisticsCargo(1000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 25f);
            cargo.Add(PoiCatalog.FacilityRelayDiamondCompositeId, 8f);
            cargo.Add(PoiCatalog.FacilityRelayBiocompositeId, 5f);
            FacilityService.StartRelay(state, "sys/poi.00", cargo, null);
            PoiManifest manifest = Manifest(PoiRisk.Safe, 11UL);
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            var candidates = new List<(string systemId, float distance)> { ("sysA", 100f) };

            FacilityService.CompleteRelay(state, manifest, cargo, candidates, knowledge, frontier);
            Assert.IsTrue(state.FacilitySignalApplied);

            // second call: Choice is still SensorRelay, but FacilitySignalApplied guards against a second signal grant
            FacilityService.CompleteRelay(state, manifest, cargo, candidates, knowledge, frontier);
            Assert.AreEqual(SystemKnowledgeLevel.Signal, knowledge.GetLevel("sysA"), "must not somehow regress or duplicate");
        }

        [Test]
        public void TransferDismantleOutput_IsRateAndCapacityBounded_ConservesTotal()
        {
            var state = IdentifiedState();
            FacilityService.StartDismantle(state, null);
            state.SetFacilityDismantleOutput(50f, 20f, ProcessingCatalog.MaterialIds[0]);
            var shipCargo = new LogisticsCargo(1000f);

            float delivered = FacilityService.TransferDismantleOutput(state, shipCargo, ratePerSecond: 10f, dt: 1f);

            Assert.AreEqual(10f, delivered);
            Assert.AreEqual(40f, state.FacilityOutputBasicRemaining);
            Assert.AreEqual(10f, shipCargo.GetStored(ProcessingCatalog.BasicPartId));
        }

        [Test]
        public void TransferDismantleOutput_NeverExceedsShipFreeCapacity_RemainderStaysRecoverable()
        {
            var state = IdentifiedState();
            FacilityService.StartDismantle(state, null);
            state.SetFacilityDismantleOutput(50f, 0f, null);
            float perUnit = CargoItemVolumes.PerUnit(ProcessingCatalog.BasicPartId);
            var shipCargo = new LogisticsCargo(3f * perUnit); // room for only 3 units

            float delivered = FacilityService.TransferDismantleOutput(state, shipCargo, ratePerSecond: 999f, dt: 1f);

            Assert.LessOrEqual(delivered, 3f + 0.001f);
            Assert.AreEqual(50f - delivered, state.FacilityOutputBasicRemaining, 0.001f, "whatever didn't fit must remain fully recoverable, never lost");
        }
    }
}
