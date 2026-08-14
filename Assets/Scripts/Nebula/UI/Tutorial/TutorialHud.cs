using UnityEngine;
using Nebula.Tutorial;
using Nebula.UI.Shell;

namespace Nebula.UI.Tutorial
{
    /// <summary>
    /// The whole of what the tutorial shows while it is running: the instruction
    /// card, the exit confirmation Escape opens, and the summary a finished run
    /// ends on.
    /// <para>
    /// One object, because <see cref="ITutorialHud"/> is one seam. The director
    /// holds this and can reach nothing else in the interface; everything below is
    /// this class's own business, which is what lets the card, the prompt and the
    /// summary change shape without the director knowing.
    /// </para>
    /// <para>
    /// Escape is handled here rather than by the director for the same reason the
    /// confirmation is a real screen on the shell: leaving a step is an interface
    /// decision with an interface consequence -- the clock stops, the modal gate is
    /// taken, and the step underneath keeps running untouched until the player says
    /// otherwise. Cancelling does nothing at all, which is exactly the promise.
    /// </para>
    /// </summary>
    public sealed class TutorialHud : MonoBehaviour, ITutorialHud
    {
        private NebulaUiShell shell;
        private ITutorialLauncher launcher;

        private TutorialInstructionCard card;
        private TutorialSummaryScreen summary;
        private NebulaConfirmationDialog exitPrompt;

        /// <summary>The card itself. Public so a fixture can read what is drawn on it.</summary>
        public TutorialInstructionCard Card => card;

        /// <summary>The summary screen, built on first use.</summary>
        public TutorialSummaryScreen Summary => summary;

        /// <summary>Test-facing: whether the exit confirmation is on screen.</summary>
        public bool ExitPromptOpen => exitPrompt != null && exitPrompt.IsOpen;

        /// <summary>Test-facing: how many times the player confirmed leaving.</summary>
        public int ExitConfirmations { get; private set; }

        /// <summary>
        /// Test-facing: how many times the player backed out of leaving. Read off
        /// the dialog itself, so a click on VAZGEÇ, Escape and the test seam are all
        /// counted by the same thing rather than by three that could drift.
        /// </summary>
        public int ExitCancellations => exitPrompt == null ? 0 : exitPrompt.CancelledCount;

        /// <summary>
        /// Test-facing: how many steps this HUD has shown a success beat for.
        /// <para>
        /// The number exists so a fixture can state the rule that matters most about
        /// the exit path -- that leaving mid-step never counts it -- as an assertion
        /// rather than as a comment.
        /// </para>
        /// </summary>
        public int StepsMarkedComplete { get; private set; }

        /// <summary>Test-facing: the step currently on the card, or <c>null</c>.</summary>
        public string CurrentStepId => card == null ? null : card.CurrentStepId;

        public static TutorialHud Create(NebulaUiShell shell, ITutorialLauncher launcher)
        {
            var go = new GameObject("Tutorial HUD");
            var hud = go.AddComponent<TutorialHud>();
            hud.shell = shell;
            hud.launcher = launcher;
            hud.card = TutorialInstructionCard.Create(shell);
            // The controller sits on the layer root, never inside the card: a
            // component parented into the visual it toggles stops updating when that
            // visual is hidden, and can then never bring itself back -- which here
            // would mean Escape stops being read the first time the card goes away.
            hud.transform.SetParent(shell.Root(UiLayer.HudStatic), false);
            return hud;
        }

        // ---- ITutorialHud ----------------------------------------------------

        /// <summary>
        /// Shows a step. <paramref name="total"/> is the whole curriculum in both
        /// run modes, so the number on the card is where the player is in the
        /// tutorial and not where they are in this sitting.
        /// </summary>
        public void ShowStep(TutorialStepDefinition step, int total)
        {
            if (card == null) return;
            card.Show(step, total, ChapterOf(step));
        }

        public void UpdateReport(TutorialStepReport report)
        {
            if (card == null) return;
            card.Apply(report);
        }

        public void ShowStepComplete(TutorialStepDefinition step)
        {
            if (card == null) return;
            StepsMarkedComplete++;
            card.ShowComplete();
        }

        public void ShowSummary(TutorialRunMode mode, int completedCount, int total)
        {
            if (card != null) card.Hide();

            if (shell == null) return;
            if (summary == null) summary = TutorialSummaryScreen.Create(shell, launcher);
            summary.Show(mode, completedCount, total);
        }

        /// <summary>
        /// Read straight off the card, which owns the control and only builds it for
        /// an <see cref="TutorialStepKind.InformationAcknowledgement"/> step. On any
        /// other step the control is absent and
        /// <see cref="TutorialInstructionCard.Acknowledge"/> refuses, so this stays
        /// false forever — which is what keeps the confirmation from becoming a way
        /// to skip a gameplay step. Nothing here sets it; opening the card, waiting
        /// and letting frames pass all leave it false.
        /// </summary>
        public bool AcknowledgedCurrentStep => card != null && card.Acknowledged;

        /// <summary>Test-facing: whether the confirmation control is on screen at all.</summary>
        public bool AcknowledgementOffered => card != null && card.AcknowledgementOffered;

        /// <summary>
        /// Test-facing: the player's confirmation, as the button and the Enter key
        /// both deliver it. Returns false when the current step is not a reading, or
        /// when it has already been confirmed.
        /// </summary>
        internal bool Acknowledge() => card != null && card.Acknowledge();

        public void Clear()
        {
            if (card != null) card.Hide();
            CloseExitPrompt();
        }

        // ---- the chapter a step belongs to -----------------------------------

        /// <summary>
        /// The chapter heading for a step, or <c>null</c> when the catalog does not
        /// describe it. Looked up rather than stored, so a card built against a
        /// half-populated catalog draws the step and simply leaves the tag empty.
        /// </summary>
        private static TutorialChapter ChapterOf(TutorialStepDefinition step)
        {
            if (step == null) return null;

            System.Collections.Generic.IReadOnlyList<TutorialChapter> chapters = TutorialStepCatalog.Chapters;
            if (chapters == null) return null;

            for (int i = 0; i < chapters.Count; i++)
                if (chapters[i] != null && chapters[i].Id == step.Chapter) return chapters[i];

            return null;
        }

        // ---- leaving ---------------------------------------------------------

        /// <summary>
        /// Escape. Read here rather than by the director because this is the object
        /// that owns the confirmation the key opens.
        /// </summary>
        private void Update()
        {
            // Enter confirms a reading step, for a player who is not using the
            // mouse. It is read before Escape and refused while the exit prompt is
            // up, because a confirmation dialog owns Enter for as long as it is on
            // screen. The card itself refuses on any step that is not a reading, so
            // this path cannot pass a gameplay step either.
            if (!ExitPromptOpen &&
                (Input.GetKeyDown(TutorialAcknowledgeInput.PrimaryKey) ||
                 Input.GetKeyDown(TutorialAcknowledgeInput.SecondaryKey)))
            {
                Acknowledge();
                return;
            }

            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            // While the prompt is up, Escape belongs to the prompt: the shell's own
            // Back closes it and counts the cancellation, which is what a player
            // pressing Escape twice expects and is the opposite of leaving.
            if (ExitPromptOpen) { CancelExit(); return; }

            RequestExit();
        }

        /// <summary>
        /// Test-facing: opens the exit confirmation. Returns false when there is
        /// nothing to leave or the prompt is already up, so a fixture can tell a
        /// refused press from one that did nothing.
        /// </summary>
        internal bool RequestExit()
        {
            if (shell == null) return false;
            if (ExitPromptOpen) return false;

            if (exitPrompt == null) exitPrompt = NebulaConfirmationDialog.Create(shell);

            return exitPrompt.Ask(TutorialUiCopy.ExitHeading, TutorialUiCopy.ExitQuestion,
                TutorialUiCopy.ExitNote, TutorialUiCopy.ExitConfirm, ConfirmedExit, destructive: true);
        }

        /// <summary>
        /// Test-facing: the confirming half of the prompt.
        /// <para>
        /// The dialog leaves the stack <b>before</b> the teardown runs, which is the
        /// same order <see cref="NebulaConfirmationDialog"/> uses for a real click --
        /// a confirmation that ran its action while still open would be holding the
        /// pause the teardown is about to hand back. Popping also clears the
        /// dialog's own callback, so the action cannot fire twice.
        /// </para>
        /// </summary>
        internal bool ConfirmExit()
        {
            if (!ExitPromptOpen) return false;

            shell?.Pop();
            ConfirmedExit();
            return true;
        }

        /// <summary>
        /// Test-facing: the cancelling half. The step underneath is untouched.
        /// <para>
        /// Routed through <see cref="NebulaUiShell.Back"/> rather than straight to
        /// <c>Pop</c>, because Back is what gives the dialog its own
        /// <see cref="NebulaConfirmationDialog.HandleBack"/> -- the same path the
        /// VAZGEÇ button takes, and the one that counts the cancellation. Popping
        /// around it would leave this seam and a real click disagreeing about
        /// whether the player backed out.
        /// </para>
        /// </summary>
        internal bool CancelExit()
        {
            if (!ExitPromptOpen || shell == null) return false;
            return shell.Back();
        }

        /// <summary>
        /// What confirming actually does: the card goes, then the launcher tears the
        /// run down and returns to the main menu.
        /// <para>
        /// It never touches <see cref="StepsMarkedComplete"/> and never calls
        /// <see cref="ShowStepComplete"/>. Leaving a step is not finishing it, and
        /// the only way to be sure of that is for this path to have no way to say so.
        /// </para>
        /// </summary>
        private void ConfirmedExit()
        {
            ExitConfirmations++;

            if (card != null) card.Hide();
            shell?.PopAll();
            launcher?.ExitToMainMenu();
        }

        private void CloseExitPrompt()
        {
            if (!ExitPromptOpen) return;
            shell?.Pop();
        }

        // ---- animation seam --------------------------------------------------

        /// <summary>
        /// Test-facing: one frame of the card's motion, in unscaled seconds.
        /// <see cref="UnityEngine.Input"/> and the frame loop cannot be driven from
        /// a fixture in this project, so the animation is exercised through here.
        /// </summary>
        internal void Tick(float unscaledSeconds) => card?.Tick(unscaledSeconds);
    }
}
