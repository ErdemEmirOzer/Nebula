using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The processing bench in the real booted scene (VS-017A.2).
    /// <para>
    /// The EditMode suite proves the bench's arithmetic on plain objects. What only a
    /// booted session can prove is everything around it: that the ship's real hold is
    /// the one being debited, that the save the director actually writes carries the
    /// bench, that a restart does not leave two of it, that the progression funnel is
    /// still a singularity through the live ledger, that one transaction produces one
    /// piece of feedback, and — decisively — that no amount of elapsed time changes any
    /// of it, because there is no longer a tick to elapse.
    /// </para>
    /// </summary>
    public class ProcessingBenchPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int TestSlot = 3;

        private const int Family = 3;
        private static string RawId => ExtractionCatalog.ResourceIds[Family];
        private static int PartRecipe => Family * 2;

        private IEnumerator BootScene()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it through the same idempotent entry the menu's
            // New Campaign button calls.
            Object.FindObjectOfType<SolarSystemBootstrap>()?.BeginSession();
            yield return null;

            // The presenter-state tests below step the director one frame at a time with
            // Tick(1f/60f) and expect one pose per call. That holds at the tier this
            // project ships (ProjectSettings m_CurrentQuality: 5 -> Ultra ->
            // LightingQuality.High -> AnimationQualityProfile.High, 60 Hz), but
            // AnimationInstaller derives the profile from the editor's *live*
            // QualitySettings level -- which any fixture that switches quality and dies
            // before its teardown leaves behind. At Very Low the profile is 24 Hz, a
            // 1/60 tick never crosses the 41.7 ms interval, and the presenter is asserted
            // against a pose pass it was never given ("loaded material did not read as
            // loaded"). A fixture that depends on a tick interval must state the tier it
            // needs rather than inherit per-project state another fixture demonstrably
            // drifts — QualitySettings is serialized back into ProjectSettings by the
            // Editor, so the drift outlives the process that caused it. The assertions
            // themselves are unchanged.
            Object.FindObjectOfType<NebulaAnimationDirector>()?.SetQuality(AnimationQuality.High);
        }

        [TearDown]
        public void TearDown() => AnimationAccessibility.ResetToDefaults();

        private static ShipProcessing Bay()
        {
            var processing = Object.FindObjectOfType<ShipProcessing>();
            Assert.IsNotNull(processing, "the ship must carry its processing bay");
            return processing;
        }

        private static LogisticsCargo Hold()
        {
            var logistics = Object.FindObjectOfType<ShipLogistics>();
            Assert.IsNotNull(logistics, "the ship must carry its hold");
            return logistics.Cargo;
        }

        private static ExtractionDirector Director()
        {
            var director = Object.FindObjectOfType<ExtractionDirector>();
            Assert.IsNotNull(director, "the session must have a save director");
            return director;
        }

        /// <summary>Everything the ship owns of one item, wherever it is sitting.</summary>
        private static float Owned(LogisticsCargo cargo, ProcessingBenchRuntime bench, string itemId)
        {
            float total = cargo.GetStored(itemId);
            if (!bench.IsEmpty && bench.InputItemId == itemId) total += bench.InputAmount;
            return total;
        }

        // ---- the real hold ------------------------------------------------------

        [UnityTest]
        public IEnumerator DepositingTakesTheUnitsOutOfTheShipsRealHold()
        {
            yield return BootScene();

            ShipProcessing processing = Bay();
            LogisticsCargo cargo = Hold();
            cargo.Add(RawId, 30f);

            float ownedBefore = Owned(cargo, processing.Bench, RawId);

            Assert.AreEqual(BenchDepositReason.None,
                ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 12));

            Assert.AreEqual(12, processing.Bench.InputAmount);
            Assert.AreEqual(18f, cargo.GetStored(RawId), 0.0001f,
                "the real hold did not pay for the deposit");
            Assert.AreEqual(ownedBefore, Owned(cargo, processing.Bench, RawId), 0.0001f,
                "the ship's total changed");
            Assert.AreEqual(0f, cargo.GetReserved(RawId), 0.0001f,
                "the bench reserved instead of moving — two authorities over one stack");
        }

        /// <summary>
        /// The property the timed model could not have: time passing changes nothing.
        /// Three hundred frames with input on the bench must leave the hold, the bench
        /// and the transaction counter exactly where they were — there is no tick, no
        /// timer and no completion for a frame to advance.
        /// </summary>
        [UnityTest]
        public IEnumerator NoAmountOfElapsedTimeConvertsAnythingOnItsOwn()
        {
            yield return BootScene();

            ShipProcessing processing = Bay();
            LogisticsCargo cargo = Hold();
            cargo.Add(RawId, 30f);
            ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 12);

            float rawAfterDeposit = cargo.GetStored(RawId);
            float partsAfterDeposit = cargo.GetStored(ProcessingCatalog.BasicPartId);
            int benchAfterDeposit = processing.Bench.InputAmount;

            for (int i = 0; i < 300; i++) yield return null;

            Assert.AreEqual(benchAfterDeposit, processing.Bench.InputAmount,
                "the bench converted itself while the game simply ran");
            Assert.AreEqual(rawAfterDeposit, cargo.GetStored(RawId), 0.0001f, "the hold's raw drifted");
            Assert.AreEqual(partsAfterDeposit, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "an output appeared with no transaction behind it");
            Assert.AreEqual(0L, processing.Bench.ConversionsTotal,
                "elapsed time counted as a conversion");
        }

        [UnityTest]
        public IEnumerator ConvertingLandsTheOutputInTheRealHoldInsideOneCall()
        {
            yield return BootScene();

            ShipProcessing processing = Bay();
            LogisticsCargo cargo = Hold();
            cargo.Add(RawId, 30f);
            ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 12);

            float partsBefore = cargo.GetStored(ProcessingCatalog.BasicPartId);

            Assert.AreEqual(BenchConvertReason.None,
                ProcessingBenchCommands.TryConvert(processing.Bench, cargo, PartRecipe, 12, out int produced));

            // Asserted before yielding a single frame: the result may not wait on a
            // presentation beat.
            Assert.AreEqual(6, produced);
            Assert.AreEqual(partsBefore + 6f, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "the output had not reached the hold by the time the call returned");
            Assert.IsTrue(processing.Bench.IsEmpty);
        }

        // ---- save, load, restart ------------------------------------------------

        [UnityTest]
        public IEnumerator TheBenchSurvivesARealSaveAndLoad()
        {
            yield return BootScene();

            ShipProcessing processing = Bay();
            LogisticsCargo cargo = Hold();
            ExtractionDirector director = Director();
            director.ActiveSlot = TestSlot;

            cargo.Add(RawId, 30f);
            ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 9);
            float rawInHold = cargo.GetStored(RawId);

            director.SaveNow("TEST");
            yield return null;

            // Move both sides away from the saved truth, so a load that did nothing
            // would be indistinguishable from a load that worked.
            ProcessingBenchCommands.TryWithdrawAll(processing.Bench, cargo, out _);
            Assert.IsTrue(processing.Bench.IsEmpty, "precondition: the bench was cleared before loading");

            director.LoadNow();
            yield return null;

            Assert.AreEqual(RawId, processing.Bench.InputItemId, "the bench's type did not survive the load");
            Assert.AreEqual(9, processing.Bench.InputAmount, "the bench's amount did not survive the load");
            Assert.AreEqual(rawInHold, Hold().GetStored(RawId), 0.0001f,
                "the hold and the bench disagree after a load");
        }

        /// <summary>
        /// Loading twice — and a restart is a load onto a live session — must land on
        /// one bench holding nine, never two benches or eighteen.
        /// </summary>
        [UnityTest]
        public IEnumerator LoadingTwiceDoesNotDuplicateTheBench()
        {
            yield return BootScene();

            ShipProcessing processing = Bay();
            LogisticsCargo cargo = Hold();
            ExtractionDirector director = Director();
            director.ActiveSlot = TestSlot;

            cargo.Add(RawId, 30f);
            ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 9);
            director.SaveNow("TEST");
            yield return null;

            director.LoadNow();
            yield return null;
            director.LoadNow();
            yield return null;

            Assert.AreEqual(9, processing.Bench.InputAmount, "a second load stacked onto the first");
            Assert.AreEqual(0, processing.Bench.LegacyClaims.Count,
                "a second load duplicated the legacy claim queue");
        }

        [UnityTest]
        public IEnumerator ARestartLeavesExactlyOneBayWithAnEmptyBench()
        {
            yield return BootScene();

            ShipProcessing processing = Bay();
            LogisticsCargo cargo = Hold();
            cargo.Add(RawId, 30f);
            ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 9);

            var bootstrap = Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);
            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<ShipProcessing>().Length,
                "a restart accumulated processing bays");
            Assert.IsTrue(Bay().Bench.IsEmpty,
                "a fresh campaign started with the previous campaign's bench loaded");
        }

        // ---- progression, audio, animation --------------------------------------

        /// <summary>
        /// The <c>recipe:first</c> funnel stays a singularity through the live ledger:
        /// the first conversion of a recipe pays, and no later conversion of the same
        /// recipe pays again, however many batches' worth it is.
        /// </summary>
        [UnityTest]
        public IEnumerator ARecipesFirstConversionPaysOnceAndNeverAgain()
        {
            yield return BootScene();

            ShipProcessing processing = Bay();
            LogisticsCargo cargo = Hold();
            cargo.Add(RawId, 60f);

            string eventId = Progression.ProgressionEvents.RecipeFirst(
                ProcessingCatalog.Recipes[PartRecipe].Id);
            Assert.IsFalse(Progression.ProgressionService.Ledger.Contains(eventId),
                "precondition: this campaign has not processed this recipe yet");

            long xpBefore = Progression.ProgressionService.State.TotalXp;

            ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 12);
            ProcessingBenchCommands.TryConvert(processing.Bench, cargo, PartRecipe, 12, out _);

            long xpAfterFirst = Progression.ProgressionService.State.TotalXp;
            Assert.AreEqual(xpBefore + Progression.ProgressionEvents.RecipeXp, xpAfterFirst,
                "a six-batch conversion paid something other than the recipe's one award");
            Assert.IsTrue(Progression.ProgressionService.Ledger.Contains(eventId));

            ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 12);
            ProcessingBenchCommands.TryConvert(processing.Bench, cargo, PartRecipe, 12, out _);

            Assert.AreEqual(xpAfterFirst, Progression.ProgressionService.State.TotalXp,
                "converting the same recipe again paid a second time — an XP faucet");
        }

        [UnityTest]
        public IEnumerator ARefusedConversionPaysNothingAndReportsNothing()
        {
            yield return BootScene();

            ShipProcessing processing = Bay();
            LogisticsCargo cargo = Hold();
            cargo.Add(RawId, 30f);
            ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 5);

            long xpBefore = Progression.ProgressionService.State.TotalXp;

            Assert.AreEqual(BenchConvertReason.AmountNotMultipleOfRecipeInput,
                ProcessingBenchCommands.TryConvert(processing.Bench, cargo, PartRecipe, 5, out int produced));

            yield return null;

            Assert.AreEqual(0, produced);
            Assert.AreEqual(xpBefore, Progression.ProgressionService.State.TotalXp,
                "a refused conversion paid XP");
            Assert.AreEqual(0L, processing.Bench.ConversionsTotal,
                "a refused conversion moved the counter every sound and VFX reads");
        }

        /// <summary>
        /// One transaction, one report. The animation presenter and the audio bridge
        /// both key off the bench's monotonic conversion count, so this is the property
        /// that stops a burst of conversions producing a burst of duplicate feedback —
        /// and stops a refusal producing any.
        /// </summary>
        [UnityTest]
        public IEnumerator OneConversionMovesTheFeedbackCounterExactlyOnce()
        {
            yield return BootScene();

            ShipProcessing processing = Bay();
            LogisticsCargo cargo = Hold();
            cargo.Add(RawId, 60f);

            long before = processing.Bench.ConversionsTotal;

            ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 12);
            ProcessingBenchCommands.TryConvert(processing.Bench, cargo, PartRecipe, 12, out _);

            Assert.AreEqual(before + 1, processing.Bench.ConversionsTotal,
                "one transaction moved the feedback counter by something other than one");
            Assert.AreEqual(6L, processing.Bench.BatchEquivalentsTotal,
                "the batch-equivalent count does not match the work done");

            // Frames passing must not move it again.
            for (int i = 0; i < 60; i++) yield return null;
            Assert.AreEqual(before + 1, processing.Bench.ConversionsTotal,
                "the counter kept rising after the transaction ended");
        }

        /// <summary>
        /// The presenter reports a conversion once and then settles, and it never
        /// touches a cargo value. With Motion Off it goes straight to its end state —
        /// the result never waits on a visual beat, because the transaction already
        /// finished before the presenter saw anything.
        /// </summary>
        [UnityTest]
        public IEnumerator TheAnimationPresenterReportsTheConversionWithoutOwningIt()
        {
            yield return BootScene();

            var presenter = Object.FindObjectOfType<ProcessingAnimationPresenter>();
            Assert.IsNotNull(presenter, "the ship must carry exactly one processing presenter");
            Assert.AreEqual(1, Object.FindObjectsOfType<ProcessingAnimationPresenter>().Length);

            ShipProcessing processing = Bay();
            LogisticsCargo cargo = Hold();
            cargo.Add(RawId, 30f);

            var director = Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(director, "the animation director must exist after boot");

            ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 12);
            director.Tick(1f / 60f);
            Assert.AreEqual(ProcessingAnimationState.InputAccepted, presenter.CurrentState,
                "loaded material did not read as loaded");

            ProcessingBenchCommands.TryConvert(processing.Bench, cargo, PartRecipe, 12, out int produced);
            float partsAfterTransaction = cargo.GetStored(ProcessingCatalog.BasicPartId);

            director.Tick(1f / 60f);
            Assert.AreEqual(ProcessingAnimationState.Complete, presenter.CurrentState,
                "the emptied bench did not report a completed conversion");

            for (int i = 0; i < 120; i++) director.Tick(1f / 60f);

            Assert.AreEqual(ProcessingAnimationState.Idle, presenter.CurrentState,
                "the presenter kept celebrating a conversion that ended");
            Assert.AreEqual(6, produced);
            Assert.AreEqual(partsAfterTransaction, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.0001f,
                "animation changed a cargo value");
            Assert.AreEqual(1L, processing.Bench.ConversionsTotal,
                "animation produced a second conversion");
        }

        [UnityTest]
        public IEnumerator MotionOffJumpsStraightToTheFinalVisualState()
        {
            yield return BootScene();

            var presenter = Object.FindObjectOfType<ProcessingAnimationPresenter>();
            var director = Object.FindObjectOfType<NebulaAnimationDirector>();
            ShipProcessing processing = Bay();
            LogisticsCargo cargo = Hold();
            cargo.Add(RawId, 30f);

            AnimationAccessibility.MotionOff = true;
            Assert.AreEqual(0f, AnimationAccessibility.MotionScale, "precondition: motion is off");

            ProcessingBenchCommands.TryDeposit(processing.Bench, cargo, RawId, 12);
            ProcessingBenchCommands.TryConvert(processing.Bench, cargo, PartRecipe, 12, out int produced);

            director.Tick(1f / 60f);

            Assert.AreEqual(6, produced, "Motion Off changed the result");
            Assert.AreEqual(ProcessingAnimationState.Complete, presenter.CurrentState);
            Assert.IsFalse(presenter.HasOutputProxy,
                "Motion Off leased a proxy for a journey it will not show");

            director.Tick(1f / 60f);
            Assert.AreEqual(ProcessingAnimationState.Idle, presenter.CurrentState,
                "Motion Off still played out a post-conversion beat");
        }
    }
}
