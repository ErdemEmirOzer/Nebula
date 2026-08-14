using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.World.Composition;

namespace Nebula.Tutorial
{
    /// <summary>
    /// How the player entered the tutorial.
    /// <para>
    /// The distinction is not cosmetic. A step reached from BÖLÜMLER has none of
    /// the state the preceding forty steps would have built, so it must install
    /// its own prerequisites; the same step reached from BAŞTAN BAŞLA inherits
    /// them and must not install them twice. Both paths run the *same*
    /// <see cref="ITutorialStepRuntime"/> and the same validator -- the mode only
    /// changes what <see cref="ITutorialStepRuntime.Prepare"/> has to build.
    /// </para>
    /// </summary>
    public enum TutorialRunMode
    {
        /// <summary>BAŞTAN BAŞLA: step 1 onward, each step gated on the previous.</summary>
        Sequential,

        /// <summary>BÖLÜMLER: one chosen step, then the summary and the main menu.</summary>
        Standalone,
    }

    /// <summary>
    /// The chapter a step belongs to. The letters are the curriculum's own and are
    /// shown to the player, so they are part of the contract rather than an
    /// implementation detail of the catalog.
    /// </summary>
    public enum TutorialChapterId
    {
        Flight,          // A
        Exploration,     // B
        Extraction,      // C
        Economy,         // D
        Progression,     // E
        Hangar,          // F
        Structures,      // G
        Combat,          // H
        Raids,           // I
        Dominance,       // J
        Session,         // K
    }

    /// <summary>A chapter heading, as shown in the BÖLÜMLER list.</summary>
    public sealed class TutorialChapter
    {
        public TutorialChapterId Id { get; }

        /// <summary>The letter the player sees ("A", "B", ...).</summary>
        public string Letter { get; }

        /// <summary>Turkish chapter title.</summary>
        public string TitleTr { get; }

        public TutorialChapter(TutorialChapterId id, string letter, string titleTr)
        {
            Id = id;
            Letter = letter ?? throw new ArgumentNullException(nameof(letter));
            TitleTr = titleTr ?? throw new ArgumentNullException(nameof(titleTr));
        }
    }

    /// <summary>
    /// Whether a step asks the player to <i>do</i> something or to <i>read</i>
    /// something.
    /// <para>
    /// The distinction is load-bearing rather than descriptive. Almost every step
    /// completes on a real state change the player caused, and must never complete
    /// any other way. A handful genuinely teach by observation — the attention
    /// cards carry no interaction at all, so there is nothing real the player can
    /// do to signal "I have read this" — and those steps were completing on the
    /// frame after they opened, whether the player read anything or not.
    /// </para>
    /// <para>
    /// Naming the two kinds is what lets the acknowledgement gate exist for the
    /// second without becoming a way to skip the first: the card only offers
    /// confirmation for an <see cref="InformationAcknowledgement"/>, and an
    /// <see cref="InteractiveAction"/> has no path to completion except its own
    /// real validator.
    /// </para>
    /// </summary>
    public enum TutorialStepKind
    {
        /// <summary>Completes only on a real gameplay state change the player caused.</summary>
        InteractiveAction,

        /// <summary>Teaches by reading. Completes on the player's explicit acknowledgement.</summary>
        InformationAcknowledgement,
    }

    /// <summary>What kind of thing a step needs to exist before it can run.</summary>
    public enum TutorialAssetKind
    {
        /// <summary>An id resolved through the project's own visual catalog/resolver.</summary>
        VisualCatalogId,

        /// <summary>An authored prefab, by asset path.</summary>
        Prefab,

        /// <summary>A VFX/effect taken on lease from a pool and returned at cleanup.</summary>
        Vfx,
    }

    /// <summary>
    /// A real asset a step depends on, declared up front so it can be verified
    /// without running the step.
    /// <para>
    /// Declared rather than merely used because "the tutorial shows the wrong
    /// model" is a silent failure: a missing prefab that falls back to a primitive
    /// still renders, still passes a screenshot test that only checks the image is
    /// not blank, and teaches the player something untrue about the game. A
    /// declared requirement can be resolved in an EditMode test with no scene at
    /// all, which is the only cheap way to keep every step honest.
    /// </para>
    /// </summary>
    public readonly struct TutorialAssetRequirement
    {
        public TutorialAssetKind Kind { get; }

        /// <summary>Catalog id or asset path, depending on <see cref="Kind"/>.</summary>
        public string Id { get; }

        /// <summary>Why this step needs it -- surfaced in the failure message when it does not resolve.</summary>
        public string Purpose { get; }

        public TutorialAssetRequirement(TutorialAssetKind kind, string id, string purpose)
        {
            Kind = kind;
            Id = id;
            Purpose = purpose;
        }

        public override string ToString() => $"{Kind}:{Id} ({Purpose})";
    }

    /// <summary>How the instruction card should present what it is saying.</summary>
    public enum TutorialFeedbackKind
    {
        /// <summary>Nothing to add; the instruction stands alone.</summary>
        None,

        /// <summary>Neutral progress toward the goal.</summary>
        Progress,

        /// <summary>The player did something the game refused, or did the wrong thing.</summary>
        Reject,

        /// <summary>The step's real success condition has been met.</summary>
        Success,
    }

    /// <summary>
    /// The channel a running step uses to tell the director what is true, refreshed
    /// every frame.
    /// <para>
    /// A mutable object the director owns and hands back on each evaluation, rather
    /// than a value the step returns: a step is polled at frame rate for as long as
    /// the player takes to do the thing, and returning a freshly built result --
    /// with a fresh objective list inside it -- would allocate on every one of those
    /// frames.
    /// </para>
    /// </summary>
    public sealed class TutorialStepReport
    {
        private readonly List<bool> objectives = new List<bool>();

        /// <summary>True once the step's real success condition has been observed.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>Player-facing feedback line, or <c>null</c> when there is nothing to say.</summary>
        public string Feedback { get; private set; }

        public TutorialFeedbackKind FeedbackKind { get; private set; }

        /// <summary>Per-objective completion, positionally matched to <see cref="TutorialStepDefinition.ChecklistTr"/>.</summary>
        public IReadOnlyList<bool> Objectives => objectives;

        /// <summary>Sizes the checklist. Called by the director when the step is prepared.</summary>
        public void Reset(int objectiveCount)
        {
            objectives.Clear();
            for (int i = 0; i < objectiveCount; i++) objectives.Add(false);
            IsComplete = false;
            Feedback = null;
            FeedbackKind = TutorialFeedbackKind.None;
        }

        /// <summary>
        /// Marks the step done. Only ever called from a real state or event
        /// observation -- never from an input read, a timer, or a string compare.
        /// </summary>
        public void Complete()
        {
            IsComplete = true;
            FeedbackKind = TutorialFeedbackKind.Success;
        }

        /// <summary>
        /// The game refused the action, or the player did the wrong thing. Carries
        /// the real rejection reason so the card never invents its own vocabulary
        /// for a refusal the game already has words for.
        /// </summary>
        public void Reject(string reason)
        {
            if (IsComplete) return;
            Feedback = reason;
            FeedbackKind = TutorialFeedbackKind.Reject;
        }

        /// <summary>Neutral progress note.</summary>
        public void Note(string text)
        {
            if (IsComplete) return;
            Feedback = text;
            FeedbackKind = TutorialFeedbackKind.Progress;
        }

        /// <summary>Flips one checklist row. Out-of-range indices are ignored rather than thrown.</summary>
        public void SetObjective(int index, bool done)
        {
            if (index < 0 || index >= objectives.Count) return;
            objectives[index] = done;
        }
    }

    /// <summary>
    /// Everything one step is allowed to create, and the guarantee that all of it
    /// goes away again.
    /// <para>
    /// A step never destroys its own objects and never unsubscribes its own events
    /// by hand. It hands both to the scope, and the director disposes the scope on
    /// the way out -- on success, on the player quitting mid-step, and on an
    /// exception. Cleanup written inside a step is cleanup that does not run when
    /// the step is abandoned, which is exactly when it matters.
    /// </para>
    /// </summary>
    public interface ITutorialStepScope
    {
        /// <summary>The step's root. Everything the step spawns lives under it.</summary>
        Transform Root { get; }

        /// <summary>Parents an object under the step root and destroys it at cleanup.</summary>
        T Track<T>(T tracked) where T : UnityEngine.Object;

        /// <summary>
        /// Registers work to run at cleanup: unsubscribing an event, returning a
        /// VFX lease, restoring a value the step changed.
        /// </summary>
        void OnCleanup(Action release);

        /// <summary>A named empty child of the step root, already tracked.</summary>
        GameObject NewGroup(string name);
    }

    /// <summary>
    /// What a step is handed when it runs: the real gameplay services, its own
    /// lifetime, and which way the player got here.
    /// </summary>
    public sealed class TutorialStepContext
    {
        /// <summary>
        /// The live gameplay composition. These are the campaign's own services --
        /// the tutorial reads and commands them, and never reimplements them.
        /// <para>
        /// It is a tutorial-owned <see cref="NebulaContext"/>, not the campaign's:
        /// composed directly by the tutorial's installer path rather than through
        /// <c>SolarSystemBootstrap.BeginSession()</c>, which is what mints a campaign
        /// id and puts a save lineage behind it.
        /// </para>
        /// </summary>
        public NebulaContext Gameplay { get; }

        /// <summary>This step's lifetime. Everything spawned goes through it.</summary>
        public ITutorialStepScope Scope { get; }

        /// <summary>Sequential or standalone -- changes what must be installed, never what counts as success.</summary>
        public TutorialRunMode Mode { get; }

        /// <summary>Spawns real, catalog-resolved world content for a step.</summary>
        public ITutorialWorld World { get; }

        public TutorialStepContext(NebulaContext gameplay, ITutorialStepScope scope,
            TutorialRunMode mode, ITutorialWorld world)
        {
            Gameplay = gameplay;
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Mode = mode;
            World = world;
        }
    }

    /// <summary>
    /// Spawns the real things a step needs to point at.
    /// <para>
    /// Exists so no step ever reaches for <c>GameObject.CreatePrimitive</c>. Every
    /// method here resolves through the project's authored catalogs, and a step
    /// that cannot get its real asset fails loudly rather than teaching the player
    /// with a grey capsule.
    /// </para>
    /// </summary>
    public interface ITutorialWorld
    {
        /// <summary>
        /// Resolves and instantiates a declared requirement under the step scope.
        /// Returns <c>null</c> and reports a blocker if the asset does not resolve.
        /// </summary>
        GameObject Spawn(TutorialAssetRequirement requirement, Vector3 position, Quaternion rotation);

        /// <summary>True when the requirement resolves today. Used by asset tests with no scene.</summary>
        bool CanResolve(TutorialAssetRequirement requirement, out string reason);
    }

    /// <summary>
    /// The behaviour of one tutorial step.
    /// <para>
    /// The three methods are the whole contract, and the hard rule lives between
    /// the first two: <see cref="Prepare"/> may install everything the step needs
    /// <b>except</b> the thing the player is being asked to do. A step that
    /// prepares its way into its own success condition is a step that completes
    /// itself, which is the failure mode this whole design exists to prevent --
    /// so every step is covered by a test that prepares it, evaluates once, and
    /// asserts it is <i>not</i> complete.
    /// </para>
    /// </summary>
    public interface ITutorialStepRuntime
    {
        /// <summary>
        /// Spawns real objects and installs prerequisite gameplay state. Must not
        /// satisfy the step's own success condition.
        /// </summary>
        void Prepare(TutorialStepContext context);

        /// <summary>
        /// Polled every frame. Reads real gameplay state and real events and writes
        /// what it finds into <paramref name="report"/>. Must never consult input,
        /// elapsed time, or displayed text to decide success.
        /// </summary>
        void Evaluate(TutorialStepReport report);

        /// <summary>
        /// Releases anything the step owns that the scope cannot know about.
        /// Scope-tracked objects and registered cleanups run regardless.
        /// </summary>
        void Cleanup();
    }

    /// <summary>
    /// One row of the curriculum: what the player is told, what proves they did it,
    /// and what it needs to exist.
    /// <para>
    /// <see cref="GlobalNumber"/> is assigned by the catalog when it is built, not
    /// written here. Contiguous, gap-free numbering is then a property of how the
    /// list is constructed rather than something forty-odd hand-written constants
    /// have to agree about -- and inserting a step in chapter C cannot silently
    /// renumber chapter J out from under a test.
    /// </para>
    /// </summary>
    public sealed class TutorialStepDefinition
    {
        /// <summary>1-based position across the whole curriculum. Assigned by the catalog.</summary>
        public int GlobalNumber { get; internal set; }

        public TutorialChapterId Chapter { get; }

        /// <summary>
        /// Whether this step is performed or read. Defaults to
        /// <see cref="TutorialStepKind.InteractiveAction"/>, so a step is only ever
        /// acknowledgeable because someone said so — never by omission.
        /// </summary>
        public TutorialStepKind Kind { get; }

        /// <summary>Stable id for tests and deep entry, e.g. "C-04". Never shown to the player.</summary>
        public string StepId { get; }

        /// <summary>Turkish step title on the card and in the BÖLÜMLER list.</summary>
        public string TitleTr { get; }

        /// <summary>One short, single-meaning Turkish instruction.</summary>
        public string InstructionTr { get; }

        /// <summary>Short Turkish description for the BÖLÜMLER row.</summary>
        public string SummaryTr { get; }

        /// <summary>Sub-objectives for complex steps; empty when the instruction stands alone.</summary>
        public IReadOnlyList<string> ChecklistTr { get; }

        /// <summary>Assets that must resolve before this step can run.</summary>
        public IReadOnlyList<TutorialAssetRequirement> Assets { get; }

        /// <summary>
        /// The binding this step's key prompt is read from, or <c>null</c> when the
        /// step is driven entirely through UI. Never a hardcoded key name: the card
        /// resolves the label from the live binding so a rebound key is shown as
        /// the player actually bound it.
        /// </summary>
        public Func<string> KeyHint { get; }

        private readonly Func<ITutorialStepRuntime> factory;

        public TutorialStepDefinition(
            TutorialChapterId chapter,
            string stepId,
            string titleTr,
            string instructionTr,
            string summaryTr,
            Func<ITutorialStepRuntime> runtimeFactory,
            IReadOnlyList<string> checklistTr = null,
            IReadOnlyList<TutorialAssetRequirement> assets = null,
            Func<string> keyHint = null,
            TutorialStepKind kind = TutorialStepKind.InteractiveAction)
        {
            Chapter = chapter;
            Kind = kind;
            StepId = stepId ?? throw new ArgumentNullException(nameof(stepId));
            TitleTr = titleTr ?? throw new ArgumentNullException(nameof(titleTr));
            InstructionTr = instructionTr ?? throw new ArgumentNullException(nameof(instructionTr));
            SummaryTr = summaryTr ?? titleTr;
            factory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
            ChecklistTr = checklistTr ?? Array.Empty<string>();
            Assets = assets ?? Array.Empty<TutorialAssetRequirement>();
            KeyHint = keyHint;
        }

        /// <summary>Builds a fresh runtime. Called once per entry, so re-entering a step is genuinely fresh.</summary>
        public ITutorialStepRuntime CreateRuntime() => factory();
    }

    /// <summary>
    /// How the menus ask for a tutorial run, and how they are told it ended.
    /// <para>
    /// The one seam between the tutorial's UI and its lifecycle. It is deliberately
    /// this narrow: the menu can start a run and can be told a run finished, and it
    /// cannot reach the director, the scene, or any gameplay service. That is the
    /// same reason <c>INebulaSessionLauncher</c> exists for the campaign -- a menu
    /// holding the bootstrap is a menu that can create a world by being drawn.
    /// </para>
    /// </summary>
    public interface ITutorialLauncher
    {
        /// <summary>
        /// BAŞTAN BAŞLA: loads the tutorial scene and runs from step 1.
        /// Starts no campaign session, mints no campaign id, writes nothing.
        /// </summary>
        void StartFromBeginning();

        /// <summary>
        /// BÖLÜMLER: loads the tutorial scene and runs exactly one step, whose
        /// prerequisites are installed for it, then returns to the main menu.
        /// </summary>
        void StartSingleStep(string stepId);

        /// <summary>Abandons the run, tears the scope down, and returns to the main menu.</summary>
        void ExitToMainMenu();

        /// <summary>Raised when a run ends, by completion or by the player leaving.</summary>
        event Action<TutorialRunMode, bool> RunEnded;
    }

    /// <summary>
    /// What the director needs from the instruction card. An interface so the
    /// director can be exercised in EditMode against a recording double, with no
    /// canvas, no fonts and no frame loop.
    /// </summary>
    public interface ITutorialHud
    {
        /// <summary>Shows a step. <paramref name="total"/> is the whole curriculum's count, in both run modes.</summary>
        void ShowStep(TutorialStepDefinition step, int total);

        /// <summary>Pushes the current evaluation to the card.</summary>
        void UpdateReport(TutorialStepReport report);

        /// <summary>The step's success beat, before the director moves on.</summary>
        void ShowStepComplete(TutorialStepDefinition step);

        /// <summary>The end-of-run summary.</summary>
        void ShowSummary(TutorialRunMode mode, int completedCount, int total);

        /// <summary>Takes the card off screen and releases anything it holds.</summary>
        void Clear();

        /// <summary>
        /// True once the player has explicitly acknowledged the current
        /// <see cref="TutorialStepKind.InformationAcknowledgement"/> step.
        /// <para>
        /// Read by the director, never set by it. The card owns the control and
        /// only builds it for an information step, so this is false forever on an
        /// interactive one — which is what stops the confirmation from becoming a
        /// way to skip gameplay. Opening the card, waiting, or letting frames pass
        /// must not set it.
        /// </para>
        /// </summary>
        bool AcknowledgedCurrentStep { get; }
    }
}
