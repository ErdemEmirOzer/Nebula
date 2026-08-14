using System;
using UnityEngine;

namespace Nebula.Tutorial
{
    /// <summary>Where a run currently is. Exposed so a test can assert the lifecycle instead of inferring it from side effects.</summary>
    public enum TutorialPhase
    {
        /// <summary>No run.</summary>
        Idle,

        /// <summary>The step's objects and prerequisite state are being built. The validator is not attached yet.</summary>
        Preparing,

        /// <summary>The card is up, the validator is attached, and the director is polling for the player's real action.</summary>
        AwaitingAction,

        /// <summary>The success condition was observed. The beat is being held before teardown.</summary>
        SuccessBeat,

        /// <summary>Every step this run was going to do is done, and the summary is up.</summary>
        Summary,

        /// <summary>The run is over and <see cref="TutorialDirector.RunEnded"/> has fired.</summary>
        Ended,
    }

    /// <summary>
    /// Runs the curriculum.
    /// <para>
    /// One step's lifecycle is exactly: prepare → spawn real objects → install
    /// prerequisite state → show the instruction card → attach the validator →
    /// poll for the player's real action → corrective feedback on a refusal →
    /// verify the real success state → success beat → detach the validator →
    /// destroy or return every asset the step took → the next step, or the
    /// summary. Nothing is skipped and nothing is reordered, in either run mode.
    /// </para>
    /// <para>
    /// Every method here is directly callable from a test with no input
    /// simulation, for the same reason <c>TrainingArea</c>'s are: this project
    /// uses the legacy Input Manager, and <c>UnityEngine.Input</c> cannot be
    /// faked in automated tests here at all. So the frame loop is one line --
    /// <c>Update</c> forwards to <see cref="Tick"/> -- and a test drives
    /// <see cref="Tick"/> itself, with whatever delta it wants, as many times as
    /// it wants.
    /// </para>
    /// <para>
    /// The counter the player sees is always the global one, in both modes: a
    /// standalone run of step 27 shows "27 / total" and, when it completes, goes
    /// to the summary rather than to step 28.
    /// </para>
    /// </summary>
    public sealed class TutorialDirector : MonoBehaviour
    {
        private TutorialSession session;
        private ITutorialHud hud;
        private TutorialWorldFactory world;

        private readonly TutorialStepReport report = new TutorialStepReport();

        private TutorialStepScope scope;
        private ITutorialStepRuntime runtime;
        private TutorialStepDefinition step;
        private Transform runRoot;

        private float beatRemaining;
        private bool validatorAttached;

        public TutorialPhase Phase { get; private set; } = TutorialPhase.Idle;
        public TutorialRunMode Mode { get; private set; }

        /// <summary>The step on screen, or <c>null</c> outside a step.</summary>
        public TutorialStepDefinition CurrentStep => step;

        /// <summary>The number the card shows. Global in both run modes; 0 when no step is up.</summary>
        public int CurrentGlobalNumber => step != null ? step.GlobalNumber : 0;

        /// <summary>The whole curriculum's size -- the denominator, in both run modes.</summary>
        public int Total => TutorialStepCatalog.Count;

        /// <summary>How many steps this run genuinely completed.</summary>
        public int CompletedCount { get; private set; }

        public bool IsRunning => Phase != TutorialPhase.Idle && Phase != TutorialPhase.Ended;

        /// <summary>The exception a step threw, or <c>null</c>. Set before teardown, so it survives the teardown that follows.</summary>
        public Exception LastError { get; private set; }

        /// <summary>Test-facing: the live report the current step is writing into.</summary>
        public TutorialStepReport Report => report;

        /// <summary>Test-facing: the asset blockers this run has accumulated.</summary>
        public TutorialWorldFactory World => world;

        /// <summary>Raised once per run. The bool is true only when every step the run set out to do actually completed.</summary>
        public event Action<TutorialRunMode, bool> RunEnded;

        // ---- construction -----------------------------------------------------

        /// <summary>
        /// <paramref name="tutorialSession"/> is the tutorial's own composition --
        /// never a campaign's. <paramref name="tutorialHud"/> is an interface so
        /// this can be exercised in EditMode against
        /// <see cref="TutorialHudRecorder"/>, with no canvas, no fonts and no
        /// frame loop.
        /// </summary>
        public static TutorialDirector Create(TutorialSession tutorialSession, ITutorialHud tutorialHud)
        {
            var go = new GameObject("Tutorial Director");
            var director = go.AddComponent<TutorialDirector>();
            director.session = tutorialSession;
            director.hud = tutorialHud;

            var rootObject = new GameObject("Tutorial Step Root");
            rootObject.transform.SetParent(go.transform, false);
            director.runRoot = rootObject.transform;

            director.world = new TutorialWorldFactory(() => director.scope,
                tutorialSession != null ? tutorialSession.Gameplay : null);
            return director;
        }

        // ---- entry points -----------------------------------------------------

        /// <summary>BAŞTAN BAŞLA. Step 1 onward; each step gated on the previous genuinely completing.</summary>
        public void RunSequential()
        {
            Mode = TutorialRunMode.Sequential;
            CompletedCount = 0;
            LastError = null;

            TutorialStepDefinition first = TutorialStepCatalog.ByGlobalNumber(1);
            if (first == null) { ShowSummaryAndHold(); return; }
            EnterStep(first);
        }

        /// <summary>
        /// BÖLÜMLER. Exactly one step, whose prerequisites its own
        /// <c>Prepare</c> installs because the preceding steps did not run, then
        /// the summary and the main menu -- never the next step.
        /// </summary>
        public void RunSingleStep(string stepId)
        {
            Mode = TutorialRunMode.Standalone;
            CompletedCount = 0;
            LastError = null;

            TutorialStepDefinition chosen = TutorialStepCatalog.Find(stepId);
            if (chosen == null)
            {
                Debug.LogError($"[Tutorial] No step with id '{stepId}'. The run ends before anything is built.");
                EndRun(false);
                return;
            }
            EnterStep(chosen);
        }

        /// <summary>
        /// The player left mid-step. Everything the step took comes back the same
        /// way it would on success -- that is the whole point of the scope owning
        /// it rather than the step.
        /// </summary>
        public void Abandon()
        {
            if (!IsRunning) return;

            // Leaving from the summary is not abandonment: every step the run set
            // out to do already finished, and the player is only closing the card.
            bool completed = Phase == TutorialPhase.Summary && LastError == null;

            TeardownStep();
            EndRun(completed);
        }

        /// <summary>The player dismissed the summary. Separate from the beat so no timer decides when a run is over.</summary>
        public void AcknowledgeSummary()
        {
            if (Phase != TutorialPhase.Summary) return;
            EndRun(LastError == null);
        }

        // ---- the frame loop ---------------------------------------------------

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>
        /// One evaluation. Public and delta-injected on purpose: this is the only
        /// thing a test has to call to advance a run, and it needs no input, no
        /// canvas and no real elapsed time.
        /// </summary>
        public void Tick(float unscaledDeltaTime)
        {
            switch (Phase)
            {
                case TutorialPhase.AwaitingAction:
                    PollValidator();
                    break;

                case TutorialPhase.SuccessBeat:
                    beatRemaining -= unscaledDeltaTime;
                    if (beatRemaining <= 0f) FinishStep();
                    break;
            }
        }

        // ---- one step ---------------------------------------------------------

        private void EnterStep(TutorialStepDefinition definition)
        {
            step = definition;
            Phase = TutorialPhase.Preparing;

            // 1. The step's own lifetime, before anything can be spawned into it.
            scope = TutorialStepScope.Create(definition.StepId, runRoot);
            report.Reset(definition.ChecklistTr.Count);

            runtime = Guard(() => definition.CreateRuntime(), out bool built);
            if (!built || runtime == null) return;

            // 2+3. Spawn real objects and install prerequisite state. Prepare may
            //      build everything except the thing the player is being asked to
            //      do -- a step that prepares its way into its own success
            //      condition completes itself, which is what this whole design
            //      exists to prevent.
            var context = new TutorialStepContext(
                session != null ? session.Gameplay : null, scope, Mode, world);

            Guard(() => runtime.Prepare(context), out bool prepared);
            if (!prepared) return;

            // 4. The instruction card. The denominator is the whole curriculum in
            //    both run modes, and the numerator is the step's global number.
            hud?.ShowStep(definition, Total);

            // 5. Attach the validator. Only now can Evaluate observe anything, so
            //    a step cannot be marked complete by its own preparation.
            validatorAttached = true;
            Phase = TutorialPhase.AwaitingAction;

            // First push, so the card starts with the step's real opening state
            // rather than an empty checklist that fills in a frame later.
            hud?.UpdateReport(report);
        }

        /// <summary>
        /// 6+7. Poll for the player's real action, and verify the real success
        /// state. Corrective feedback needs no branch here: a step writes its
        /// refusal into the report with the game's own rejection reason, and the
        /// card renders whatever kind the report carries.
        /// </summary>
        private void PollValidator()
        {
            if (!validatorAttached || runtime == null) return;

            Guard(() => runtime.Evaluate(report), out bool evaluated);
            if (!evaluated) return;

            hud?.UpdateReport(report);
            if (!IsStepFinished()) return;

            // 8. The success beat, on unscaled time so a tutorial-compressed or
            //    paused world cannot make it flash past or hang.
            CompletedCount++;
            beatRemaining = TutorialMotion.Duration(TutorialMotion.SuccessBeatSeconds);
            Phase = TutorialPhase.SuccessBeat;
            hud?.ShowStepComplete(step);

            // Motion Off snaps: the beat still happens, it is just not travelled to.
            if (beatRemaining <= 0f) FinishStep();
        }

        /// <summary>
        /// What "done" means for the step on screen, and the only place the two
        /// kinds differ.
        /// <para>
        /// An <see cref="TutorialStepKind.InteractiveAction"/> is done when its own
        /// validator says the player really changed the world, and nothing else can
        /// finish it — the acknowledgement is not consulted at all, so a card that
        /// somehow offered one could still not pass a gameplay step.
        /// </para>
        /// <para>
        /// An <see cref="TutorialStepKind.InformationAcknowledgement"/> is done on
        /// the player's explicit confirmation and on nothing else. Its validator
        /// still runs and still fills the checklist — that is how the player is
        /// shown that the thing they were told to look at is genuinely on screen —
        /// but it cannot end the step, because a reading step's validator is true
        /// the moment the card opens, which is exactly how those steps used to slip
        /// past on the following frame.
        /// </para>
        /// </summary>
        private bool IsStepFinished()
        {
            if (step == null) return false;

            if (step.Kind == TutorialStepKind.InformationAcknowledgement)
                return hud != null && hud.AcknowledgedCurrentStep;

            return report.IsComplete;
        }

        private void FinishStep()
        {
            TutorialStepDefinition finished = step;

            // 9+10. Detach the validator, then give back everything the step took.
            TeardownStep();

            // 11. The next step, or the summary. A standalone run of step 27 goes
            //     to the summary here -- never to step 28.
            if (Mode == TutorialRunMode.Standalone) { ShowSummaryAndHold(); return; }

            TutorialStepDefinition next = TutorialStepCatalog.ByGlobalNumber(finished.GlobalNumber + 1);
            if (next == null) { ShowSummaryAndHold(); return; }
            EnterStep(next);
        }

        /// <summary>
        /// Detaches the validator and disposes the scope. Reached from success,
        /// from <see cref="Abandon"/>, and from an exception thrown out of a step
        /// -- the three cases the contract requires cleanup in, running the same
        /// code in all three so none of them can be the one that was forgotten.
        /// Idempotent.
        /// </summary>
        private void TeardownStep()
        {
            validatorAttached = false;

            if (runtime != null)
            {
                ITutorialStepRuntime doomed = runtime;
                runtime = null;
                // A step's own Cleanup may throw; the scope must still be disposed,
                // so this failure is recorded and swallowed rather than propagated.
                try { doomed.Cleanup(); }
                catch (Exception error) { LastError = error; Debug.LogException(error); }
            }

            if (scope != null)
            {
                TutorialStepScope doomedScope = scope;
                scope = null;
                doomedScope.Dispose();
            }

            step = null;
        }

        private void ShowSummaryAndHold()
        {
            Phase = TutorialPhase.Summary;
            hud?.ShowSummary(Mode, CompletedCount, Total);
        }

        private void EndRun(bool completed)
        {
            if (Phase == TutorialPhase.Ended) return;

            TeardownStep();
            hud?.Clear();
            Phase = TutorialPhase.Ended;

            // The scene, the session and this director are torn down by whoever
            // owns them (the launcher); the director's job ends with the event.
            RunEnded?.Invoke(Mode, completed);
        }

        // ---- failure ----------------------------------------------------------

        /// <summary>
        /// Runs one piece of step code. An exception out of <c>Prepare</c>,
        /// <c>Evaluate</c> or the runtime factory ends the run, but only after the
        /// scope has been disposed -- cleanup that does not run when a step blows
        /// up is cleanup that leaks a world, and a leaked step root would still be
        /// in the scene for the next run to find.
        /// </summary>
        private void Guard(Action work, out bool ok)
        {
            try { work(); ok = true; }
            catch (Exception error)
            {
                ok = false;
                LastError = error;
                Debug.LogException(error);
                Debug.LogError($"[Tutorial] Step '{(step != null ? step.StepId : "?")}' threw; the run is torn down.");
                TeardownStep();
                EndRun(false);
            }
        }

        private T Guard<T>(Func<T> work, out bool ok)
        {
            try { T result = work(); ok = true; return result; }
            catch (Exception error)
            {
                ok = false;
                LastError = error;
                Debug.LogException(error);
                TeardownStep();
                EndRun(false);
                return default;
            }
        }

        /// <summary>
        /// The last line of defence. If this director is destroyed while a step is
        /// live -- a scene unload, a domain reload, a test that forgot -- the step
        /// still gives everything back.
        /// </summary>
        private void OnDestroy() => TeardownStep();
    }
}
