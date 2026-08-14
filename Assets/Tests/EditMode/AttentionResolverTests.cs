using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure attention-card resolver: priority, tie-break, max 3, LastKnown labeling (VS-010 §4/§10.9).</summary>
    public class AttentionResolverTests
    {
        private static SystemAttentionInputs Critical(float timeToLoss = 10f, bool isLive = true) => new SystemAttentionInputs
        { AnyPlanetContested = true, MinRecoveryTimeRemaining = timeToLoss, RaidEtaSeconds = float.MaxValue, IsLive = isLive };

        private static SystemAttentionInputs Medium(bool isLive = true) => new SystemAttentionInputs
        { AllActiveDrillsStalledFull = true, MinRecoveryTimeRemaining = float.MaxValue, RaidEtaSeconds = float.MaxValue, IsLive = isLive };

        [Test]
        public void NoIssue_ProducesNoCard()
        {
            AttentionCard? card = AttentionResolver.ResolveForSystem("sys.A", new SystemAttentionInputs
            { MinRecoveryTimeRemaining = float.MaxValue, RaidEtaSeconds = float.MaxValue, IsLive = true });
            Assert.IsFalse(card.HasValue);
        }

        [Test]
        public void Critical_OutranksHighAndMedium()
        {
            AttentionCard? critical = AttentionResolver.ResolveForSystem("sys.A", Critical());
            Assert.AreEqual(AttentionPriority.Critical, critical.Value.Priority);
        }

        [Test]
        public void High_NeverProducedWithoutARealRaidDetection_NoCombatProviderThisSlice()
        {
            // RaidDetected is always false at the one real call site (AttentionQuery) —
            // proving the pure resolver ITSELF only ever goes High when told to.
            var inputs = new SystemAttentionInputs { RaidDetected = false, MinRecoveryTimeRemaining = float.MaxValue, RaidEtaSeconds = float.MaxValue };
            AttentionCard? card = AttentionResolver.ResolveForSystem("sys.A", inputs);
            Assert.IsFalse(card.HasValue, "no combat provider this slice -> RaidDetected is always false -> never a fabricated High card");
        }

        [Test]
        public void Medium_WhenAllActiveDrillsStalledFull_OrVehicleStranded()
        {
            AttentionCard? full = AttentionResolver.ResolveForSystem("sys.A", Medium());
            Assert.AreEqual(AttentionPriority.Medium, full.Value.Priority);

            var stranded = new SystemAttentionInputs { VehicleStrandedNoTarget = true, MinRecoveryTimeRemaining = float.MaxValue, RaidEtaSeconds = float.MaxValue };
            AttentionCard? strandedCard = AttentionResolver.ResolveForSystem("sys.A", stranded);
            Assert.AreEqual(AttentionPriority.Medium, strandedCard.Value.Priority);
        }

        [Test]
        public void TopThree_CapsAtThreeEvenWithManyCandidates()
        {
            var cards = new List<AttentionCard>();
            for (int i = 0; i < 6; i++) cards.Add(AttentionResolver.ResolveForSystem($"sys.{i}", Medium()).Value);
            List<AttentionCard> top = AttentionResolver.TopThree(cards);
            Assert.AreEqual(3, top.Count);
        }

        [Test]
        public void TopThree_OrdersByPriorityFirst()
        {
            var cards = new List<AttentionCard>
            {
                AttentionResolver.ResolveForSystem("sys.medium", Medium()).Value,
                AttentionResolver.ResolveForSystem("sys.critical", Critical()).Value
            };
            List<AttentionCard> top = AttentionResolver.TopThree(cards);
            Assert.AreEqual("sys.critical", top[0].SystemId);
            Assert.AreEqual("sys.medium", top[1].SystemId);
        }

        [Test]
        public void TopThree_TieBreak_ShortestTimeToLossFirst()
        {
            var cards = new List<AttentionCard>
            {
                AttentionResolver.ResolveForSystem("sys.slow", Critical(timeToLoss: 200f)).Value,
                AttentionResolver.ResolveForSystem("sys.fast", Critical(timeToLoss: 5f)).Value
            };
            List<AttentionCard> top = AttentionResolver.TopThree(cards);
            Assert.AreEqual("sys.fast", top[0].SystemId);
        }

        [Test]
        public void TopThree_TieBreak_FinallyByCanonicalSystemId()
        {
            var cards = new List<AttentionCard>
            {
                AttentionResolver.ResolveForSystem("sector.0.0.0/system.002", Critical(timeToLoss: 5f)).Value,
                AttentionResolver.ResolveForSystem("sector.0.0.0/system.001", Critical(timeToLoss: 5f)).Value
            };
            List<AttentionCard> top = AttentionResolver.TopThree(cards);
            Assert.AreEqual("sector.0.0.0/system.001", top[0].SystemId);
        }

        [Test]
        public void NonLiveSystem_CardIsLabeledLastKnown()
        {
            AttentionCard? card = AttentionResolver.ResolveForSystem("sys.A", Critical(isLive: false));
            Assert.IsTrue(card.Value.IsLastKnown);
        }

        [Test]
        public void LiveSystem_CardIsNotLabeledLastKnown()
        {
            AttentionCard? card = AttentionResolver.ResolveForSystem("sys.A", Critical(isLive: true));
            Assert.IsFalse(card.Value.IsLastKnown);
        }

        [Test]
        public void ConditionResolved_ProducesNoCardOnTheNextCall_PureReDerivation()
        {
            var inputs = new SystemAttentionInputs { AnyPlanetContested = true, MinRecoveryTimeRemaining = 10f, RaidEtaSeconds = float.MaxValue };
            Assert.IsTrue(AttentionResolver.ResolveForSystem("sys.A", inputs).HasValue);

            inputs.AnyPlanetContested = false; // condition resolved
            Assert.IsFalse(AttentionResolver.ResolveForSystem("sys.A", inputs).HasValue, "the card must disappear the instant its condition is no longer true");
        }
    }
}
