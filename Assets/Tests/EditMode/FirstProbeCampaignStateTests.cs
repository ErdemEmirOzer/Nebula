using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the one-shot First Probe warning + campaign-wide registry reset (VS-013 §7/§11).</summary>
    public class FirstProbeStateTests
    {
        [Test]
        public void FreshState_IsNotScheduled_ThreatUnknown()
        {
            var probe = new FirstProbeState();
            Assert.AreEqual(FirstProbeStatus.NotScheduled, probe.Status);
            Assert.IsFalse(probe.ThreatKnown);
        }

        [Test]
        public void ScheduleWarningIfNeeded_StartsThe150SecondWindow_SetsThreatKnown()
        {
            var probe = new FirstProbeState();
            probe.ScheduleWarningIfNeeded();
            Assert.AreEqual(FirstProbeStatus.WarningActive, probe.Status);
            Assert.AreEqual(150f, probe.WarningRemainingSeconds);
            Assert.IsTrue(probe.ThreatKnown, "Shield Systems' First AI Threat milestone reads this directly");
        }

        [Test]
        public void ScheduleWarningIfNeeded_IsIdempotent_NeverReschedulesOrResets()
        {
            var probe = new FirstProbeState();
            probe.ScheduleWarningIfNeeded();
            probe.TickWarning(100f);
            probe.ScheduleWarningIfNeeded(); // second call must be a no-op
            Assert.AreEqual(50f, probe.WarningRemainingSeconds, 0.01f);
        }

        [Test]
        public void TickWarning_ReturnsTrueExactlyOnceOnExpiry()
        {
            var probe = new FirstProbeState();
            probe.ScheduleWarningIfNeeded();
            int fires = 0;
            for (int i = 0; i < 151; i++) if (probe.TickWarning(1f)) fires++;
            Assert.AreEqual(1, fires);
        }

        [Test]
        public void TickWarning_NoOp_WhenNotActive()
        {
            var probe = new FirstProbeState();
            Assert.IsFalse(probe.TickWarning(1000f));
        }

        [Test]
        public void MarkResolved_DestroyedWithoutScan_LeavesNoPendingReconEffect()
        {
            var probe = new FirstProbeState();
            probe.ScheduleWarningIfNeeded();
            probe.MarkResolved(probeScanned: false);
            Assert.AreEqual(FirstProbeStatus.Resolved, probe.Status);
            Assert.IsNull(probe.PendingReconEffect);
        }

        [Test]
        public void MarkResolved_AfterScan_SetsASingleNonAccumulatingPendingEffect()
        {
            var probe = new FirstProbeState();
            probe.ScheduleWarningIfNeeded();
            probe.MarkResolved(probeScanned: true);
            Assert.AreEqual("vs014.recon.pending", probe.PendingReconEffect);
        }

        [Test]
        public void MarkResolved_IsIdempotent_NeverReResolvesOrDuplicatesEffect()
        {
            var probe = new FirstProbeState();
            probe.ScheduleWarningIfNeeded();
            probe.MarkResolved(true);
            probe.MarkResolved(true); // second resolution attempt must be ignored entirely
            Assert.AreEqual("vs014.recon.pending", probe.PendingReconEffect);
        }

        [Test]
        public void ConsumePendingReconEffect_ReadsAndClearsInOneStep()
        {
            var probe = new FirstProbeState();
            probe.ScheduleWarningIfNeeded();
            probe.MarkResolved(true);
            string first = probe.ConsumePendingReconEffect();
            string second = probe.ConsumePendingReconEffect();
            Assert.AreEqual("vs014.recon.pending", first);
            Assert.IsNull(second, "VS-014's own consumer must never see the effect twice");
        }

        [Test]
        public void RestoreFromSave_RoundTripsAllFields()
        {
            var probe = new FirstProbeState();
            probe.RestoreFromSave(FirstProbeStatus.WarningActive, 42.5f, true, null);
            Assert.AreEqual(FirstProbeStatus.WarningActive, probe.Status);
            Assert.AreEqual(42.5f, probe.WarningRemainingSeconds);
            Assert.IsTrue(probe.ThreatKnown);
        }
    }

    /// <summary>EditMode tests for the static campaign-scoped combat registry (VS-013 §11 — must never leak across a campaign restart/load).</summary>
    public class CombatCampaignStateTests
    {
        [TearDown]
        public void TearDown() => CombatCampaignState.Reset();

        [Test]
        public void Reset_ReplacesAllThreeSubStatesWithFreshInstances()
        {
            CombatCampaignState.FirstProbe.ScheduleWarningIfNeeded();
            CombatCampaignState.Training.A.CompleteStep();
            CombatCampaignState.Recovery.RecordDefeat();

            CombatCampaignState.Reset();

            Assert.AreEqual(FirstProbeStatus.NotScheduled, CombatCampaignState.FirstProbe.Status, "a reset campaign must never inherit a prior campaign's Probe warning state");
            Assert.AreEqual(0, CombatCampaignState.Training.A.CurrentStep, "a reset campaign must never inherit prior training progress");
            Assert.IsFalse(CombatCampaignState.Recovery.ShouldApplyMercyForRepeatedDefeats(), "a reset campaign must never inherit prior defeat history");
        }

        [Test]
        public void EnemyAgentRegistry_ClearRegistry_EmptiesTheStaticMap()
        {
            // EnemyAgent itself needs a live GameObject to construct, so this only
            // exercises the static side of the contract directly relevant to the
            // "no duplicate/static leak after restart" requirement: after
            // ClearRegistry, Find must return null for any previously-known id.
            EnemyAgent.ClearRegistry();
            Assert.IsNull(EnemyAgent.Find("any-id"));
            Assert.AreEqual(0, EnemyAgent.All.Count);
        }
    }
}
