using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for Emergency Shutdown timer, recovery priority/fractions, cargo-loss math, soft-lock mercy, and wreckage lifetime (VS-013 §8/§11).</summary>
    public class EmergencyShutdownRecoveryTests
    {
        [Test]
        public void Trigger_StartsAn8SecondCountdown_NeverStacks()
        {
            var s = new EmergencyShutdownState();
            s.Trigger();
            Assert.IsTrue(s.Active);
            Assert.AreEqual(8f, s.RemainingSeconds);
            s.Tick(2f);
            s.Trigger(); // re-trigger while already active must not restart the clock
            Assert.AreEqual(6f, s.RemainingSeconds, 0.001f);
        }

        [Test]
        public void Tick_ReturnsTrueExactlyOnce_WhenCountdownReachesZero()
        {
            var s = new EmergencyShutdownState();
            s.Trigger();
            int firedCount = 0;
            for (int i = 0; i < 9; i++) if (s.Tick(1f)) firedCount++;
            Assert.AreEqual(1, firedCount);
            Assert.IsFalse(s.Active);
        }

        [Test]
        public void RecoveryResolver_PrioritizesActiveCommandCenter_ThenLastVisited_ThenSafetyMarker()
        {
            Assert.AreEqual(RecoveryPointKind.ActiveCommandCenterPlanet, RecoveryResolver.Resolve(true, true));
            Assert.AreEqual(RecoveryPointKind.LastVisitedControlledSystem, RecoveryResolver.Resolve(false, true));
            Assert.AreEqual(RecoveryPointKind.SafetyMarker, RecoveryResolver.Resolve(false, false));
        }

        [Test]
        public void RecoveryResolver_ExactFractions_MatchSpec()
        {
            Assert.AreEqual(0.25f, RecoveryResolver.RecoveryHullFraction);
            Assert.AreEqual(0.10f, RecoveryResolver.RecoveryArmorFraction);
            Assert.AreEqual(0f, RecoveryResolver.RecoveryShieldFraction);
            Assert.AreEqual(0.60f, RecoveryResolver.SoftLockHullFraction);
            Assert.AreEqual(0.30f, RecoveryResolver.SoftLockArmorFraction);
        }

        [Test]
        public void DefeatCargoLossResolver_Takes20PercentPerItem_SkipsNegligibleAmounts()
        {
            var cargo = new Dictionary<string, float> { { "part.basic", 100f }, { "material.refined_gas", 0f } };
            var loss = DefeatCargoLossResolver.ComputeLoss(cargo);
            Assert.AreEqual(20f, loss["part.basic"], 0.001f);
            Assert.IsFalse(loss.ContainsKey("material.refined_gas"));
        }

        [Test]
        public void SoftLock_MercyTriggers_OnThirdDefeatWithinWindow()
        {
            var soft = new RecoverySoftLockState();
            soft.TickActiveMinutes(60f); // 1 active minute
            Assert.IsFalse(soft.ShouldApplyMercyForRepeatedDefeats(), "no prior defeats yet");
            soft.RecordDefeat();
            soft.TickActiveMinutes(60f);
            soft.RecordDefeat();
            Assert.IsTrue(soft.ShouldApplyMercyForRepeatedDefeats(), "2 defeats already recorded inside the 20-minute window -- the 3rd recovery gets mercy");
        }

        [Test]
        public void SoftLock_PrunesDefeatsOutsideTheWindow()
        {
            var soft = new RecoverySoftLockState();
            soft.RecordDefeat();
            soft.TickActiveMinutes(21f * 60f); // 21 minutes later -- outside the 20-minute window
            soft.RecordDefeat();
            Assert.IsFalse(soft.ShouldApplyMercyForRepeatedDefeats(), "only 1 defeat remains inside the sliding window");
        }

        [Test]
        public void SoftLock_NoteVisitedSystem_OnlyRecordsControlledSystems()
        {
            var soft = new RecoverySoftLockState();
            soft.NoteVisitedSystem("sys-uncontrolled", hasControlledPlanet: false);
            Assert.IsNull(soft.LastVisitedControlledSystemId);
            soft.NoteVisitedSystem("sys-controlled", hasControlledPlanet: true);
            Assert.AreEqual("sys-controlled", soft.LastVisitedControlledSystemId);
        }

        [Test]
        public void SoftLock_RestoreFromSave_RoundTripsDefeatTimestampsAndLastVisited()
        {
            var soft = new RecoverySoftLockState();
            soft.TickActiveMinutes(300f);
            soft.RecordDefeat();
            soft.NoteVisitedSystem("sys-1", true);
            var snapshot = soft.SnapshotDefeatTimestamps().ToList();

            var restored = new RecoverySoftLockState();
            restored.RestoreFromSave(soft.TotalActiveMinutesElapsed, snapshot, soft.LastVisitedControlledSystemId);
            Assert.AreEqual(soft.TotalActiveMinutesElapsed, restored.TotalActiveMinutesElapsed);
            CollectionAssert.AreEqual(snapshot, restored.SnapshotDefeatTimestamps());
            Assert.AreEqual("sys-1", restored.LastVisitedControlledSystemId);
        }

        [Test]
        public void Wreckage_ExpiresAfter30ActiveMinutes()
        {
            var wreck = new CombatWreckageState("wreck-1");
            wreck.Nodes.Add(new CombatWreckageNode("part.basic", 10f));
            Assert.IsFalse(wreck.Expired);
            wreck.Tick(30f * 60f);
            Assert.IsTrue(wreck.Expired);
        }

        [Test]
        public void Wreckage_ExpiresOnceFullyWithdrawn_EvenBeforeTimeout()
        {
            var wreck = new CombatWreckageState("wreck-2");
            wreck.Nodes.Add(new CombatWreckageNode("part.basic", 10f));
            float taken = wreck.Withdraw("part.basic", 10f);
            Assert.AreEqual(10f, taken);
            Assert.IsTrue(wreck.Expired);
        }

        [Test]
        public void Wreckage_Withdraw_NeverExceedsRemaining_ConservesTotal()
        {
            var wreck = new CombatWreckageState("wreck-3");
            wreck.Nodes.Add(new CombatWreckageNode("material.refined_gas", 5f));
            float first = wreck.Withdraw("material.refined_gas", 3f);
            float second = wreck.Withdraw("material.refined_gas", 10f); // asks for more than remains
            Assert.AreEqual(3f, first);
            Assert.AreEqual(2f, second, 0.001f, "must never hand out more than what remains");
        }

        [Test]
        public void Wreckage_RestoreFromSave_RoundTripsNodesAndRemainingTime()
        {
            var wreck = new CombatWreckageState("wreck-4");
            var nodes = new (string, float, float)[] { ("part.basic", 10f, 6f), ("material.industrial_diamond", 3f, 3f) };
            wreck.RestoreFromSave(1200f, nodes);
            Assert.AreEqual(1200f, wreck.RemainingActiveSeconds);
            Assert.AreEqual(2, wreck.Nodes.Count);
            Assert.AreEqual(6f, wreck.Nodes[0].Remaining);
        }
    }
}
