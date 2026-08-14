using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Extraction;
using Nebula.Tutorial;
using Nebula.Visuals;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The tutorial's lifecycle promises: it starts where it says it starts, it does
    /// not advance until the player has actually done the thing, it leaves nothing
    /// behind, and it never writes.
    /// <para>
    /// The world is composed once for the whole fixture. Composing a tutorial world
    /// per test is what turns a two-minute run into an hour here, and the leak tests
    /// that genuinely need repeated composition say so and use a small, stated
    /// number rather than pretending a hundred full worlds is free.
    /// </para>
    /// </summary>
    // ReleaseRegression: the curriculum invariants here are release-gating. The one
    // endurance leaf carries [Category("Soak")] of its own and must be excluded by that
    // name -- NUnit propagates this class category to it, so Soak is the narrower and
    // authoritative one.
    [Category("ReleaseRegression")]
    public class TutorialLifecycleTests
    {
        private TutorialSession session;
        private TutorialDirector director;
        private TutorialHudRecorder hud;

        [SetUp]
        public void SetUp()
        {
            session = TutorialSession.Compose();
            hud = new TutorialHudRecorder();
            director = TutorialDirector.Create(session, hud);
        }

        [TearDown]
        public void TearDown()
        {
            if (director != null) UnityEngine.Object.DestroyImmediate(director.gameObject);
            director = null;
            session?.Dispose();
            session = null;
            TutorialRunRequest.Clear();
        }

        // ---- where a run starts ----------------------------------------------

        [Test]
        public void SequentialRunStartsAtStepOne()
        {
            director.RunSequential();

            Assert.AreEqual(1, director.CurrentGlobalNumber, "BAŞTAN BAŞLA must start at step 1");
            Assert.AreEqual(1, hud.Shown.Count, "exactly one step should have been shown");
            Assert.AreEqual(TutorialStepCatalog.Count, hud.Shown[0].Total,
                "the card's denominator is the whole curriculum");
        }

        [Test]
        public void StandaloneRunKeepsTheGlobalNumberOnTheCard()
        {
            // The rule from the brief: entering step 27 on its own shows "27 / N",
            // not "1 / 1". The player's place in the tutorial is a property of the
            // curriculum, not of this sitting.
            TutorialStepDefinition target = TutorialStepCatalog.All
                .FirstOrDefault(s => s.GlobalNumber == Math.Min(27, TutorialStepCatalog.Count));
            Assert.IsNotNull(target, "curriculum is too small to test standalone entry");

            director.RunSingleStep(target.StepId);

            Assert.AreEqual(target.GlobalNumber, director.CurrentGlobalNumber);
            Assert.AreEqual(target.StepId, hud.Shown.Single().StepId);
            Assert.AreEqual(TutorialStepCatalog.Count, hud.Shown.Single().Total);
        }

        [Test]
        public void EveryStepIsReachableStandalone()
        {
            var unreachable = new List<string>();
            foreach (TutorialStepDefinition step in TutorialStepCatalog.All)
            {
                if (TutorialStepCatalog.Find(step.StepId) == null) unreachable.Add(step.StepId);
            }
            CollectionAssert.IsEmpty(unreachable,
                "BÖLÜMLER offers every step with no lock and no progress gate, so every " +
                "step id must resolve: " + string.Join(", ", unreachable));
        }

        // ---- a run does not advance by itself ---------------------------------

        [UnityTest]
        public IEnumerator SequentialRunDoesNotAdvanceWhileTheStepIsIncomplete()
        {
            director.RunSequential();
            int startedOn = director.CurrentGlobalNumber;

            // Twenty ticks of a full second each. If anything completed a step on
            // elapsed time rather than on real state, this is where it shows.
            for (int i = 0; i < 20; i++)
            {
                director.Tick(1f);
                yield return null;
            }

            Assert.AreEqual(startedOn, director.CurrentGlobalNumber,
                "the run advanced without the player completing the step — a step is " +
                "passing on a timer, which the design forbids");
            Assert.AreEqual(0, director.CompletedCount);
        }

        /// <summary>
        /// How many real frames each step is left alone for in the sweep below.
        /// <para>
        /// Frames, deliberately, and not a large injected delta: the director's
        /// delta only drives the success beat, while the world advances by whatever
        /// <c>Time.deltaTime</c> a real frame took. Ten frames is a sixth of a
        /// second — long enough for anything that arrives "on the next frame or
        /// two" (a HUD driver's refresh cadence, a widget's first paint, a
        /// subscription that fires late) and far too short to trip a genuine
        /// gameplay timer measured in seconds. Making it longer would not find more
        /// self-completing steps; it would start failing steps whose lesson really
        /// is to wait for a drill or a scanner.
        /// </para>
        /// </summary>
        private const int IdleFramesPerStep = 10;

        /// <summary>
        /// The whole-curriculum invariant, and the one this fixture exists for.
        /// <para>
        /// Prepare may install every prerequisite but never the thing the player is
        /// asked to do — and it is not enough to check that once, immediately.
        /// Evaluating a single time misses exactly the failure that was actually
        /// shipping: a step whose condition becomes true a frame or two later
        /// because a HUD driver refreshed, a widget painted, or a subscription
        /// caught up. So every step in the catalog is entered, left completely
        /// alone for <see cref="IdleFramesPerStep"/> real frames with the director
        /// polling every one of them, and must still be waiting.
        /// </para>
        /// <para>
        /// Reading steps are not exempt here and must not be: the recording HUD
        /// never acknowledges, so an <c>InformationAcknowledgement</c> that
        /// completed in this sweep would mean the director had found some other way
        /// to finish it.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator NoStepCompletesWhileThePlayerDoesNothing()
        {
            var selfCompleting = new List<string>();

            foreach (TutorialStepDefinition step in TutorialStepCatalog.All)
            {
                var probe = new TutorialHudRecorder();
                TutorialDirector runner = TutorialDirector.Create(session, probe);
                bool entered = false;

                try
                {
                    runner.RunSingleStep(step.StepId);
                    entered = true;
                }
                catch (Exception error)
                {
                    selfCompleting.Add($"{step.StepId} threw entering: {error.GetType().Name}: {error.Message}");
                }

                if (entered)
                {
                    for (int frame = 0; frame < IdleFramesPerStep; frame++)
                    {
                        runner.Tick(Time.unscaledDeltaTime);
                        yield return null;
                    }

                    string verdict = IdleVerdict(step, runner, probe);
                    if (verdict != null) selfCompleting.Add($"{step.GlobalNumber} {step.StepId}: {verdict}");
                }

                UnityEngine.Object.DestroyImmediate(runner.gameObject);
                yield return null;
            }

            CollectionAssert.IsEmpty(selfCompleting,
                $"these steps finished themselves within {IdleFramesPerStep} frames of opening, with the " +
                "player doing nothing at all:\n" + string.Join("\n", selfCompleting));
        }

        /// <summary>
        /// What went wrong for one idled step, or <c>null</c> when it behaved.
        /// Named separately so the failure says which of the three symptoms it was
        /// rather than leaving the reader to guess from a boolean.
        /// </summary>
        private static string IdleVerdict(TutorialStepDefinition step,
            TutorialDirector runner, TutorialHudRecorder probe)
        {
            if (probe.Completed.Count > 0)
                return "the director marked it complete and moved on";
            if (runner.CompletedCount > 0)
                return "it was counted as completed";

            // A reading step's validator is legitimately true the moment its card
            // opens -- that is what makes it a reading step -- so the report is not
            // the thing being asserted for it. The director gate above is.
            if (step.Kind != TutorialStepKind.InformationAcknowledgement
                && runner.Report != null && runner.Report.IsComplete)
                return "its own validator was satisfied without the player acting";

            if (runner.Phase != TutorialPhase.AwaitingAction)
                return $"it left AwaitingAction on its own (now {runner.Phase})";

            return null;
        }

        // ---- reading steps and the acknowledgement gate ------------------------

        /// <summary>
        /// A reading step's validator is true almost immediately, on purpose: it is
        /// evidence that the thing the player was told to look at really is on
        /// screen. What must not happen is the director treating that as the step
        /// being finished.
        /// </summary>
        [UnityTest]
        public IEnumerator AReadingStepDoesNotAdvanceWithoutTheAcknowledgement()
        {
            foreach (TutorialStepDefinition step in ReadingSteps())
            {
                var probe = new TutorialHudRecorder();
                TutorialDirector runner = TutorialDirector.Create(session, probe);
                try
                {
                    runner.RunSingleStep(step.StepId);
                    for (int i = 0; i < 30; i++) { runner.Tick(1f); yield return null; }

                    Assert.AreEqual(0, runner.CompletedCount,
                        $"{step.StepId} completed without the player confirming they had read it");
                    CollectionAssert.IsEmpty(probe.Completed, step.StepId);
                    Assert.AreEqual(TutorialPhase.AwaitingAction, runner.Phase, step.StepId);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(runner.gameObject);
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator AReadingStepCompletesOnTheAcknowledgementAndCountsItOnce()
        {
            foreach (TutorialStepDefinition step in ReadingSteps())
            {
                var probe = new TutorialHudRecorder();
                TutorialDirector runner = TutorialDirector.Create(session, probe);
                try
                {
                    runner.RunSingleStep(step.StepId);
                    runner.Tick(0f);
                    Assert.AreEqual(0, runner.CompletedCount, $"{step.StepId} was complete before it was confirmed");

                    probe.Acknowledge();
                    runner.Tick(0f);

                    Assert.AreEqual(1, runner.CompletedCount,
                        $"{step.StepId} did not complete on the player's confirmation");
                    CollectionAssert.AreEqual(new[] { step.StepId }, probe.Completed,
                        $"{step.StepId} was marked complete more than once");

                    // Held through the success beat: a second count here would mean
                    // the same confirmation was being read again every frame.
                    for (int i = 0; i < 10; i++) { runner.Tick(1f); yield return null; }
                    Assert.AreEqual(1, runner.CompletedCount, $"{step.StepId} was counted twice");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(runner.gameObject);
                }
                yield return null;
            }
        }

        /// <summary>
        /// The rule the whole gate stands on: a confirmation is not a way to finish
        /// a step that has a real action.
        /// <para>
        /// Each step is run twice — once acknowledged and once not — and only a step
        /// that completes <i>with</i> the confirmation and not <i>without</i> it is
        /// reported. Without that control run this test cannot tell a leaking gate
        /// from a step that was finishing itself anyway, and it accused the gate of
        /// exactly that when I-20's Escort engaged on its own. A wrong diagnosis is
        /// worse than no test: it sends the fix to the wrong place.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AnAcknowledgementNeverCompletesAnInteractiveStep()
        {
            TutorialStepDefinition[] sample = TutorialStepCatalog.All
                .Where(s => s.Kind == TutorialStepKind.InteractiveAction)
                .Where(s => s.GlobalNumber % 17 == 1)   // a spread across the curriculum, not the first few
                .ToArray();
            Assert.That(sample.Length, Is.GreaterThan(0), "no interactive step to sample");

            var skipped = new List<string>();
            var selfCompleting = new List<string>();

            foreach (TutorialStepDefinition step in sample)
            {
                bool acknowledged = false, control = false;

                yield return RunOnce(step, acknowledge: true, r => acknowledged = r);
                yield return RunOnce(step, acknowledge: false, r => control = r);

                // Only the difference between the two runs is evidence about the gate.
                if (acknowledged && !control) skipped.Add(step.StepId);
                else if (acknowledged && control) selfCompleting.Add(step.StepId);
            }

            CollectionAssert.IsEmpty(skipped,
                "an acknowledgement completed a gameplay step that does not complete without one: "
                + string.Join(", ", skipped));

            // Reported separately and named for what it is: the gate held, and these
            // steps finish themselves. NoStepCompletesWhileThePlayerDoesNothing is
            // where that is fixed.
            CollectionAssert.IsEmpty(selfCompleting,
                "these gameplay steps completed with and without an acknowledgement, so they are " +
                "self-completing rather than gate leaks: " + string.Join(", ", selfCompleting));
        }

        /// <summary>One standalone run of a step, optionally acknowledged, reporting only whether it completed.</summary>
        private IEnumerator RunOnce(TutorialStepDefinition step, bool acknowledge, Action<bool> completed)
        {
            var probe = new TutorialHudRecorder();
            TutorialDirector runner = TutorialDirector.Create(session, probe);
            try
            {
                runner.RunSingleStep(step.StepId);
                if (acknowledge) probe.Acknowledge();
                for (int i = 0; i < 10; i++) { runner.Tick(1f); yield return null; }
                completed(runner.CompletedCount > 0 || probe.Completed.Count > 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runner.gameObject);
            }
            yield return null;
        }

        /// <summary>
        /// One press, one step. The reset lives on <c>ShowStep</c> rather than on
        /// completion, so it holds however the previous step ended — including when
        /// the player abandoned it — and a second run entered with the same HUD
        /// starts unconfirmed.
        /// </summary>
        [UnityTest]
        public IEnumerator OneAcknowledgementNeverCompletesTwoSteps()
        {
            TutorialStepDefinition[] readings = ReadingSteps();
            Assert.That(readings.Length, Is.GreaterThan(1),
                "this needs two reading steps to prove the confirmation does not carry");

            var probe = new TutorialHudRecorder();

            TutorialDirector first = TutorialDirector.Create(session, probe);
            try
            {
                first.RunSingleStep(readings[0].StepId);
                probe.Acknowledge();
                for (int i = 0; i < 10; i++) { first.Tick(1f); yield return null; }
                Assert.AreEqual(1, first.CompletedCount, "the first reading did not complete");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first.gameObject);
            }
            yield return null;

            // The same HUD, never told to forget anything by the test: showing the
            // next step is what clears the confirmation.
            TutorialDirector second = TutorialDirector.Create(session, probe);
            try
            {
                second.RunSingleStep(readings[1].StepId);
                for (int i = 0; i < 20; i++) { second.Tick(1f); yield return null; }

                Assert.AreEqual(0, second.CompletedCount,
                    $"{readings[1].StepId} completed on the confirmation the player gave {readings[0].StepId}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(second.gameObject);
            }
        }

        /// <summary>
        /// BÖLÜMLER, all the way through: a reading step entered on its own,
        /// confirmed, and then the summary and the end of the run — never the next
        /// step, and never a run left hanging because the gate had no way to open.
        /// </summary>
        [UnityTest]
        public IEnumerator AStandaloneReadingStepStillEndsItsRun()
        {
            TutorialStepDefinition step = ReadingSteps()[0];

            var probe = new TutorialHudRecorder();
            TutorialDirector runner = TutorialDirector.Create(session, probe);
            var endings = new List<(TutorialRunMode mode, bool completed)>();
            runner.RunEnded += (mode, completed) => endings.Add((mode, completed));

            try
            {
                runner.RunSingleStep(step.StepId);
                probe.Acknowledge();
                for (int i = 0; i < 10; i++) { runner.Tick(1f); yield return null; }

                Assert.AreEqual(TutorialPhase.Summary, runner.Phase,
                    "a standalone reading step must go to the summary, not to the next step");
                Assert.AreEqual(1, probe.Summaries.Count);
                Assert.AreEqual(TutorialRunMode.Standalone, probe.Summaries[0].Mode);
                Assert.AreEqual(TutorialStepCatalog.Count, probe.Summaries[0].Total,
                    "the denominator is the whole curriculum in both run modes");

                runner.AcknowledgeSummary();

                Assert.AreEqual(TutorialPhase.Ended, runner.Phase);
                Assert.AreEqual(1, endings.Count, "the run must end exactly once");
                Assert.IsTrue(endings[0].completed,
                    "the run did everything it set out to do, so it ended completed — which is what sends " +
                    "the player back to the main menu rather than to a failure");
                Assert.AreEqual(1, probe.ClearCount, "the card was not taken off screen when the run ended");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runner.gameObject);
            }
        }

        private static TutorialStepDefinition[] ReadingSteps()
        {
            TutorialStepDefinition[] steps = TutorialStepCatalog.All
                .Where(s => s.Kind == TutorialStepKind.InformationAcknowledgement).ToArray();
            Assert.That(steps.Length, Is.GreaterThan(0), "the curriculum declares no reading step");
            return steps;
        }

        // ---- leaving ----------------------------------------------------------

        [Test]
        public void AbandoningARunDoesNotCountTheStepAsCompleted()
        {
            director.RunSequential();
            director.Abandon();

            Assert.AreEqual(0, director.CompletedCount,
                "leaving mid-step must never count it — the brief is explicit");
            CollectionAssert.IsEmpty(hud.Completed);
        }

        [Test]
        public void AbandoningARunLeavesNoStepObjectsBehind()
        {
            int before = CountSceneObjects();

            director.RunSequential();
            director.Abandon();

            Assert.LessOrEqual(CountSceneObjects(), before + AllowedResidue,
                "abandoning a run left step objects in the scene");
        }

        // ---- repeated entry ---------------------------------------------------

        [Test]
        public void AHundredEntriesAndExitsLeaveTheCountsUnchanged()
        {
            // At the launcher level with the scene load stubbed out. A hundred real
            // world compositions would take longer than the whole rest of the suite,
            // and what this guards is the request/lifecycle bookkeeping — that
            // entering and leaving a hundred times cannot accumulate state.
            var launcher = new TutorialLauncher(null)
            {
                LoadTutorialScene = () => true,
                UnloadTutorialScene = () => { },
                Hud = new TutorialHudRecorder(),
            };

            int endings = 0;
            launcher.RunEnded += (mode, completed) => endings++;

            for (int i = 0; i < 100; i++)
            {
                launcher.StartFromBeginning();
                launcher.ExitToMainMenu();
            }

            Assert.AreEqual(100, launcher.RunsStarted);
            Assert.AreEqual(100, endings);
            Assert.IsFalse(launcher.IsRunning, "the launcher is still running after its last exit");
            Assert.IsFalse(TutorialRunRequest.IsArmed,
                "a run request survived the run — the next entry would inherit it");
        }

        /// <summary>
        /// Fifty real session start/stop cycles, each one checked while it is live
        /// and again once it is gone: exactly one streaming director, exactly one
        /// ship, the registry pointing at that director and nothing else, and no
        /// campaign-scoped static event still holding a listener afterwards.
        /// <para>
        /// This is the regression guard for the failure that cost this suite twenty
        /// tutorial tests. A fixture that booted a session and never ended one left
        /// a <see cref="Sectors.SystemStreamingDirector"/> running <c>Update</c>
        /// for the rest of the process. It then did two things, both invisible in
        /// isolation: it drove a second world alongside whichever world the next
        /// fixture composed, and — once that fixture's teardown called
        /// <c>StarSystemRegistry.Clear()</c> — it threw a
        /// <c>NullReferenceException</c> out of <c>TickPromotionDemotion</c> every
        /// single frame. Neither showed up as "the streaming director leaked",
        /// which is precisely why the count is asserted directly here.
        /// </para>
        /// <para>
        /// Frames are stepped inside each cycle deliberately. A composed-and-disposed
        /// session that never renders a frame never runs <c>Update</c> at all, so the
        /// exception this test exists to catch would not be raised; the test framework
        /// fails on an unexpected error log, so those frames are the "zero NREs" half
        /// of the proof and the assertions below are the "one director" half.
        /// </para>
        /// <para>
        /// Fifty full compositions are not free — roughly four minutes, which is
        /// more than the rest of this fixture put together and is why the default
        /// 180-second per-test timeout had to be raised explicitly rather than
        /// quietly worked around by doing fewer cycles. It is priced in knowingly:
        /// a leak that only shows on the Nth cycle is exactly the shape of the bug
        /// that got through, and three cycles
        /// (<see cref="RepeatedCompositionsDoNotAccumulateObjects"/>) were not
        /// enough to make anyone look.
        /// </para>
        /// </summary>
        [UnityTest]
        [Timeout(600000)]
        // Soak: fifty full session compositions, ~4 minutes, and its own summary prices that
        // in deliberately -- "a leak that only shows on the Nth cycle". That is endurance,
        // not a release regression. The invariant itself is NOT left unguarded in the release
        // lane: RepeatedCompositionsDoNotAccumulateObjects runs the same census over three
        // cycles ("Three, not a hundred") and stays in it.
        [Category("Soak")]
        public IEnumerator FiftySessionStartStopCyclesLeaveOneDirectorAndNoOrphans()
        {
            const int Cycles = 50;

            TearDown();          // the fixture's own session first — this test owns every world it counts
            yield return null;

            Assert.AreEqual(0, UnityEngine.Object.FindObjectsOfType<Sectors.SystemStreamingDirector>().Length,
                "a streaming director was alive before the first cycle — something earlier in this " +
                "run never ended its session");

            for (int cycle = 1; cycle <= Cycles; cycle++)
            {
                TutorialSession probe = TutorialSession.Compose();

                // Two real frames with the world up: Update runs, promotion/demotion
                // ticks, and anything that throws per frame throws here.
                yield return null;
                yield return null;

                Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<Sectors.SystemStreamingDirector>().Length,
                    $"cycle {cycle}: more than one streaming director is alive — a previous session's " +
                    "director outlived its dispose");
                Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<Ship.ShipController>().Length,
                    $"cycle {cycle}: more than one ship is alive — two worlds are running at once");
                Assert.AreSame(probe.Gameplay.Streaming, World.StarSystemRegistry.Streaming,
                    $"cycle {cycle}: the registry is pointing at a director this session does not own");

                probe.Dispose();
                yield return null;
                yield return null;

                int strayListeners = LiveSubscribers();
                Assert.AreEqual(0, UnityEngine.Object.FindObjectsOfType<Sectors.SystemStreamingDirector>().Length,
                    $"cycle {cycle}: disposing the session left its streaming director behind");
                Assert.AreEqual(0, UnityEngine.Object.FindObjectsOfType<Ship.ShipController>().Length,
                    $"cycle {cycle}: disposing the session left its ship behind");
                Assert.IsNull(World.StarSystemRegistry.Streaming,
                    $"cycle {cycle}: the registry still names a destroyed director");
                Assert.AreEqual(0, strayListeners, $"cycle {cycle}: {strayListeners} campaign-scoped " +
                    "static event listeners survived the session that registered them");
            }

            SetUp();
        }

        /// <summary>
        /// How many listeners the three campaign-scoped static events are still
        /// holding.
        /// <para>
        /// Reflection, because there is no other way to ask: they are declared
        /// <c>public static event</c>, so a test from another assembly may add and
        /// remove but never read. The compiler's backing field for a field-like
        /// event carries the event's own name, and the lookup is asserted rather
        /// than defaulted — a renamed event must fail this loudly instead of
        /// quietly reporting zero listeners forever.
        /// </para>
        /// </summary>
        private static int LiveSubscribers()
        {
            return Count(typeof(Progression.ProgressionService), "Awarded")
                 + Count(typeof(CampaignIdentity), "IdentityChanged")
                 + Count(typeof(Hangar.ResourceTransferService), "Collected");

            static int Count(Type owner, string eventName)
            {
                System.Reflection.FieldInfo field = owner.GetField(eventName,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert.IsNotNull(field,
                    $"{owner.Name}.{eventName} no longer has a field-like backing store — this " +
                    "counter is silently blind until it is updated");
                return field.GetValue(null) is Delegate handler ? handler.GetInvocationList().Length : 0;
            }
        }

        [UnityTest]
        public IEnumerator RepeatedCompositionsDoNotAccumulateObjects()
        {
            // Three, not a hundred: this one composes a real world each time, and
            // the leak it is looking for shows on the second repeat if it exists.
            TearDown();
            yield return null;

            int baseline = -1;
            for (int i = 0; i < 3; i++)
            {
                TutorialSession probe = TutorialSession.Compose();
                var recorder = new TutorialHudRecorder();
                TutorialDirector runner = TutorialDirector.Create(probe, recorder);
                runner.RunSequential();
                runner.Abandon();
                UnityEngine.Object.DestroyImmediate(runner.gameObject);
                probe.Dispose();
                yield return null;

                int now = CountSceneObjects();
                if (i == 0) baseline = now;
                else Assert.LessOrEqual(now, baseline + AllowedResidue,
                    $"composition {i + 1} left {now - baseline} objects behind — the tutorial world is leaking");
            }

            SetUp();
        }

        // ---- zero persistence -------------------------------------------------

        [Test]
        public void ComposingAndRunningATutorialWorldSeversEverySaveTrigger()
        {
            Assert.IsTrue(session.SaveTriggersSevered,
                "the tutorial composed a world whose save path is still live — a run " +
                "longer than the 300s autosave interval would write a real save");
        }

        [UnityTest]
        public IEnumerator ARunLeavesEverySaveFileByteIdentical()
        {
            Dictionary<string, (long length, DateTime written)> before = SnapshotSaves();

            director.RunSequential();
            for (int i = 0; i < 30; i++) { director.Tick(1f); yield return null; }
            director.Abandon();
            yield return null;

            Dictionary<string, (long length, DateTime written)> after = SnapshotSaves();

            CollectionAssert.AreEquivalent(before.Keys, after.Keys,
                "the tutorial created or removed a save file");
            foreach (KeyValuePair<string, (long length, DateTime written)> entry in before)
            {
                Assert.AreEqual(entry.Value.length, after[entry.Key].length,
                    $"{entry.Key} changed size during a tutorial run");
                Assert.AreEqual(entry.Value.written, after[entry.Key].written,
                    $"{entry.Key} was rewritten during a tutorial run");
            }
        }

        [Test]
        public void ARunNeverAdoptsACampaignIdentity()
        {
            string before = CampaignIdentity.Current;

            director.RunSequential();
            director.Tick(1f);
            director.Abandon();

            Assert.AreEqual(before, CampaignIdentity.Current,
                "a tutorial run minted or adopted a campaign id");
        }

        // ---- accessibility ----------------------------------------------------

        [Test]
        public void MotionOffStillRunsTheWholeStepLifecycle()
        {
            bool enteredMotionOff = AnimationAccessibility.MotionOff;
            bool enteredReduced = AnimationAccessibility.ReducedMotion;
            try
            {
                AnimationAccessibility.MotionOff = true;
                AnimationAccessibility.ReducedMotion = true;

                director.RunSequential();
                director.Tick(0f);

                Assert.AreEqual(1, director.CurrentGlobalNumber);
                Assert.AreEqual(1, hud.Shown.Count,
                    "with motion off the card must still be shown — snapped to its end pose, not skipped");
            }
            finally
            {
                AnimationAccessibility.MotionOff = enteredMotionOff;
                AnimationAccessibility.ReducedMotion = enteredReduced;
            }
        }

        // ---- helpers ----------------------------------------------------------

        /// <summary>
        /// Unity keeps a few of its own objects alive between frames, so an exact
        /// match would be flaky for reasons that have nothing to do with us.
        /// </summary>
        private const int AllowedResidue = 4;

        private static int CountSceneObjects() =>
            UnityEngine.Object.FindObjectsOfType<GameObject>(true).Length;

        private static Dictionary<string, (long, DateTime)> SnapshotSaves()
        {
            var snapshot = new Dictionary<string, (long, DateTime)>();
            string root = Application.persistentDataPath;
            if (!Directory.Exists(root)) return snapshot;

            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                snapshot[file] = (info.Length, info.LastWriteTimeUtc);
            }
            return snapshot;
        }
    }
}
