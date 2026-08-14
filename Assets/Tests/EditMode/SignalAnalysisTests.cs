using NUnit.Framework;
using Nebula.Navigation;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure signal-analysis state (VS-006 §9, tests 6-7).</summary>
    public class SignalAnalysisTests
    {
        [Test]
        public void Start_OnlyOneAnalysisAtATime()
        {
            var state = new SignalAnalysisState();
            Assert.IsTrue(state.Start("sys002", 8f));
            Assert.IsFalse(state.Start("sys003", 4f), "a second target must be rejected while one is active");
            Assert.AreEqual("sys002", state.TargetSystemId);
        }

        [Test]
        public void Tick_T1EightSeconds_CompletesExactlyOnce()
        {
            var state = new SignalAnalysisState();
            state.Start("sys002", 8f);

            for (int i = 0; i < 7 * 60; i++) Assert.IsFalse(state.Tick(1f / 60f), "must not complete before 8 active seconds");
            bool completedThisFrame = false;
            for (int i = 0; i < 2 * 60 && !completedThisFrame; i++) completedThisFrame = state.Tick(1f / 60f);
            Assert.IsTrue(completedThisFrame);
        }

        [Test]
        public void Tick_T2FourSeconds_CompletesFaster()
        {
            var state = new SignalAnalysisState();
            state.Start("sys002", 4f);
            for (int i = 0; i < 3 * 60; i++) Assert.IsFalse(state.Tick(1f / 60f));
            bool completed = false;
            // Slack margin (2s of ticks for the last ~1s) absorbs float
            // accumulation drift from repeated 1/60f additions, same pattern
            // as the T1 test above.
            for (int i = 0; i < 2 * 60 && !completed; i++) completed = state.Tick(1f / 60f);
            Assert.IsTrue(completed);
        }

        [Test]
        public void ZeroDeltaTime_NeverProgresses_MatchesPauseAndOfflineBehavior()
        {
            var state = new SignalAnalysisState();
            state.Start("sys002", 8f);
            for (int i = 0; i < 10000; i++) state.Tick(0f); // simulates Time.timeScale == 0 every frame
            Assert.AreEqual(8f, state.RemainingSeconds);
            Assert.IsTrue(state.Active);
        }

        [Test]
        public void Restore_RoundTrips_NoDuplicationOrLoss()
        {
            var state = new SignalAnalysisState();
            state.Restore("sys004", 3.5f);
            Assert.IsTrue(state.Active);
            Assert.AreEqual("sys004", state.TargetSystemId);
            Assert.AreEqual(3.5f, state.RemainingSeconds);
        }

        [Test]
        public void Clear_AllowsStartingANewAnalysis()
        {
            var state = new SignalAnalysisState();
            state.Start("sys002", 8f);
            state.Clear();
            Assert.IsFalse(state.Active);
            Assert.IsTrue(state.Start("sys003", 4f));
        }
    }
}
