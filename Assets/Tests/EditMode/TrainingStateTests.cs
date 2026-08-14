using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for training gate/skip/replay/no-reward/3s-reset/2-failure-help (VS-013 §7/§11).</summary>
    public class TrainingStateTests
    {
        [Test]
        public void TrainingA_IsUnlockedFromTheVeryStart()
        {
            var progress = new TrainingProgress();
            Assert.IsTrue(progress.IsUnlocked(TrainingId.A, weaponCoordinationTier: 0, plasmaEquippedAnywhere: false, emEquippedAnywhere: false));
        }

        [Test]
        public void TrainingB_RequiresWeaponCoordinationT1AndAnEquippedPlasma()
        {
            var progress = new TrainingProgress();
            Assert.IsFalse(progress.IsUnlocked(TrainingId.B, 0, plasmaEquippedAnywhere: true, false), "tier alone insufficient without production tier");
            Assert.IsFalse(progress.IsUnlocked(TrainingId.B, 1, plasmaEquippedAnywhere: false, false), "tier without an actually-equipped Plasma must not unlock");
            Assert.IsTrue(progress.IsUnlocked(TrainingId.B, 1, plasmaEquippedAnywhere: true, false));
        }

        [Test]
        public void TrainingC_RequiresWeaponCoordinationT2AndAnEquippedEm()
        {
            var progress = new TrainingProgress();
            Assert.IsFalse(progress.IsUnlocked(TrainingId.C, 1, false, emEquippedAnywhere: true), "T1 is not enough for EM");
            Assert.IsTrue(progress.IsUnlocked(TrainingId.C, 2, false, emEquippedAnywhere: true));
        }

        [Test]
        public void CompleteStep_AdvancesAndClearsFailureState()
        {
            var run = new TrainingRun(TrainingId.A, 3);
            run.FailStep();
            run.CompleteStep();
            Assert.AreEqual(1, run.CurrentStep);
            Assert.IsTrue(run.IsStepCompleted(0));
            Assert.AreEqual(0, run.FailuresThisStep);
            Assert.IsFalse(run.HelpUnlockedThisStep);
        }

        [Test]
        public void FailStep_BlocksActionForExactly3Seconds_ThenRetriesSameStep()
        {
            var run = new TrainingRun(TrainingId.A, 3);
            run.FailStep();
            Assert.IsFalse(run.CanAct);
            run.Tick(2.9f);
            Assert.IsFalse(run.CanAct);
            run.Tick(0.2f);
            Assert.IsTrue(run.CanAct);
            Assert.AreEqual(0, run.CurrentStep, "a failed step must retry from the SAME step, never skip forward");
        }

        [Test]
        public void SecondFailure_UnlocksHelp()
        {
            var run = new TrainingRun(TrainingId.A, 3);
            run.FailStep();
            Assert.IsFalse(run.HelpUnlockedThisStep);
            run.Tick(3.1f);
            run.FailStep();
            Assert.IsTrue(run.HelpUnlockedThisStep);
        }

        [Test]
        public void Skip_CompletesAllStepsImmediately_NoIntermediateReward()
        {
            var run = new TrainingRun(TrainingId.B, 4);
            run.Skip();
            Assert.IsTrue(run.Completed);
            for (int i = 0; i < 4; i++) Assert.IsTrue(run.IsStepCompleted(i));
        }

        [Test]
        public void Reset_AllowsFullReplay()
        {
            var run = new TrainingRun(TrainingId.A, 3);
            run.CompleteStep();
            run.CompleteStep();
            run.Reset();
            Assert.AreEqual(0, run.CurrentStep);
            Assert.IsFalse(run.Completed);
            Assert.IsFalse(run.IsStepCompleted(0));
        }

        [Test]
        public void RestoreFromSave_ReappliesStepAndCompletionFlags()
        {
            var run = new TrainingRun(TrainingId.C, 4);
            run.RestoreFromSave(2, new[] { true, true, false, false });
            Assert.AreEqual(2, run.CurrentStep);
            Assert.IsTrue(run.IsStepCompleted(0));
            Assert.IsTrue(run.IsStepCompleted(1));
            Assert.IsFalse(run.IsStepCompleted(2));
        }

        [Test]
        public void SnapshotCompletedFlags_RoundTripsThroughRestoreFromSave()
        {
            var run = new TrainingRun(TrainingId.A, 6);
            run.CompleteStep(); run.CompleteStep(); run.CompleteStep();
            bool[] snapshot = run.SnapshotCompletedFlags();

            var restored = new TrainingRun(TrainingId.A, 6);
            restored.RestoreFromSave(3, snapshot);
            CollectionAssert.AreEqual(snapshot, restored.SnapshotCompletedFlags());
        }
    }
}
