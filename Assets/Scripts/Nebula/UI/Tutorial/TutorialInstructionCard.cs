using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Tutorial;
using Nebula.UI.Accessibility;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.UI.Tutorial
{
    /// <summary>
    /// The card the player reads while a step is running: where they are in the
    /// curriculum, what to do, which key does it, what is still outstanding, and
    /// what the game just said back.
    /// <para>
    /// It is a HUD widget rather than a screen on <see cref="NebulaUiShell"/>'s
    /// stack, and that is the whole design. A pushed screen pauses the clock,
    /// raises the input gate and takes the modal lock -- which for a card the
    /// player is supposed to read <i>while flying</i> would mean the step could
    /// never be performed. Nothing on the card is a raycast target and the group
    /// blocks nothing, so a click passes straight through it to the game.
    /// </para>
    /// <para>
    /// Top centre, below the notification band, and bounded so it can never reach
    /// the middle of the screen: the reticle, the target bracket under it and the
    /// vitals block bottom-right are all things a player is being asked to use
    /// during the very step the card is describing. A tutorial card that covers
    /// the reticle is a tutorial that cannot be followed.
    /// </para>
    /// </summary>
    public sealed class TutorialInstructionCard : MonoBehaviour
    {
        // ---- geometry --------------------------------------------------------

        /// <summary>Card width in canvas units. Fits inside 1280x720 at 140% interface scale.</summary>
        public const float Width = 860f;

        /// <summary>Distance from the top edge. Clears the notification band, which owns 24..56.</summary>
        public const float Top = 60f;

        private const float Pad = 24f;
        private const float Inner = Width - Pad * 2f;

        private const float MetaY = -14f;
        private const float TitleY = -36f;
        private const float InstructionY = -64f;
        private const float InstructionHeight = 36f;

        /// <summary>Where the checklist starts, measured from the card's top edge.</summary>
        private const float HeaderHeight = 106f;

        private const float RowPitch = 22f;
        private const float MinRowPitch = 18f;
        private const float KeyRowHeight = 24f;
        private const float FeedbackRowHeight = 24f;
        private const float ExitHintHeight = 18f;
        private const float BottomPad = 14f;

        /// <summary>The confirmation row, which exists only on a reading step.</summary>
        private const float AcknowledgeRowHeight = 34f;

        private const float AcknowledgeButtonWidth = 176f;
        private const float AcknowledgeButtonHeight = 28f;

        private const float MarkSize = 14f;
        private const float StateWordWidth = 128f;

        /// <summary>
        /// How much daylight is kept between the card's bottom edge and the middle
        /// of the screen, where the reticle sits.
        /// </summary>
        public const float ReticleClearanceFloor = 24f;

        // ---- widgets ---------------------------------------------------------

        private RectTransform root;
        private CanvasGroup group;

        private NebulaUiFactory.LabelHandle counter;
        private NebulaUiFactory.LabelHandle chapterTag;
        private NebulaUiFactory.LabelHandle title;
        private NebulaUiFactory.LabelHandle instruction;
        private NebulaUiFactory.LabelHandle keyPrompt;
        private NebulaUiFactory.LabelHandle exitHint;
        private NebulaUiFactory.LabelHandle overflow;

        private RectTransform checklistRoot;
        private RectTransform feedbackRoot;
        private UiStatusCueView.Handle feedbackMark;
        private NebulaUiFactory.LabelHandle feedbackText;

        private RectTransform acknowledgeRoot;
        private NebulaUiFactory.ButtonHandle acknowledgeButton;
        private NebulaUiFactory.LabelHandle acknowledgeNote;

        private sealed class ObjectiveRow
        {
            public RectTransform Root;
            public UiStatusCueView.Handle Mark;
            public NebulaUiFactory.LabelHandle Label;
            public NebulaUiFactory.LabelHandle StateWord;
        }

        private readonly List<ObjectiveRow> objectiveRows = new List<ObjectiveRow>(8);
        private readonly List<string> objectiveText = new List<string>(8);
        private readonly List<bool> objectiveDone = new List<bool>(8);

        // ---- animation -------------------------------------------------------

        private UiPanelPhase phase = UiPanelPhase.Hidden;
        private float elapsed;
        private readonly UiEmphasisPulse pulse = new UiEmphasisPulse();

        // ---- state -----------------------------------------------------------

        private TutorialStepDefinition step;

        /// <summary>Test-facing: the step's number across the whole curriculum, in both run modes.</summary>
        public int CurrentGlobalNumber { get; private set; }

        /// <summary>Test-facing: the curriculum's total, as handed in by the director.</summary>
        public int CurrentTotal { get; private set; }

        /// <summary>Test-facing: the step id on the card, or <c>null</c> when there is none.</summary>
        public string CurrentStepId => step?.StepId;

        /// <summary>Test-facing: "12 / 47" exactly as drawn.</summary>
        public string CounterText => counter == null ? string.Empty : counter.Value;

        public string TitleText => title == null ? string.Empty : title.Value;

        public string InstructionText => instruction == null ? string.Empty : instruction.Value;

        /// <summary>Test-facing: the resolved key prompt, or <c>null</c> when the step has no key.</summary>
        public string KeyPromptText { get; private set; }

        public string FeedbackText { get; private set; }

        public TutorialFeedbackKind FeedbackKind { get; private set; }

        /// <summary>Test-facing: how many checklist rows are drawn.</summary>
        public int ObjectiveCount => objectiveText.Count;

        /// <summary>
        /// Test-facing: objectives the card could not draw without crossing the
        /// screen's middle. Never silently dropped -- the card says how many.
        /// </summary>
        public int HiddenObjectiveCount { get; private set; }

        public bool IsVisible => root != null && root.gameObject.activeSelf;

        /// <summary>Test-facing: 0 at the start of the entry, 1 once the card is at rest.</summary>
        public float AnimationProgress01
        {
            get
            {
                float duration = NebulaUiDesignTokens.MotionDuration(UiPanelPhase.Entering);
                return duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            }
        }

        /// <summary>Test-facing: true when the card is required to arrive without travelling.</summary>
        public bool MotionSnapped => NebulaUiDesignTokens.MotionDuration(UiPanelPhase.Entering) <= 0f;

        /// <summary>Test-facing: where the card actually is.</summary>
        public Vector2 CardOffset => root == null ? Vector2.zero : root.anchoredPosition;

        /// <summary>Test-facing: the card's rest pose, which Motion Off must land on immediately.</summary>
        public static Vector2 RestOffset => new Vector2(0f, -Top);

        public float CardHeight => root == null ? 0f : root.sizeDelta.y;

        /// <summary>
        /// Test-facing: canvas units between the card's bottom edge and the middle
        /// of the screen. Must stay positive at every supported interface scale, or
        /// the card is sitting on the reticle.
        /// </summary>
        public float ReticleClearance => CanvasHalfHeight() - (Top + CardHeight);

        /// <summary>Test-facing: whether a repeating emphasis is allowed to run at all.</summary>
        public bool EmphasisRepeats => UiEmphasisPulse.Repeats;

        /// <summary>Test-facing: the current emphasis level. 1 means steady.</summary>
        public float EmphasisLevel => pulse.Level;

        // ---- the reading confirmation ----------------------------------------

        /// <summary>
        /// Whether this card is currently offering the confirmation control.
        /// <para>
        /// True only while a step whose <see cref="TutorialStepDefinition.Kind"/> is
        /// <see cref="TutorialStepKind.InformationAcknowledgement"/> is on screen.
        /// On every other step the control is not merely disabled but absent, and
        /// <see cref="Acknowledge"/> refuses — which is what stops a confirmation
        /// that exists for reading from becoming a way to pass a gameplay step.
        /// </para>
        /// </summary>
        public bool AcknowledgementOffered { get; private set; }

        /// <summary>
        /// True once the player has confirmed the reading on the current step.
        /// Cleared by every <see cref="Show"/> and every <see cref="Hide"/>, so one
        /// confirmation cannot carry over and finish the following step as well.
        /// </summary>
        public bool Acknowledged { get; private set; }

        /// <summary>
        /// The one funnel the confirmation goes through: the button's click, the
        /// Enter key the HUD reads, and the fixture seam all end up here, so there
        /// is a single place where "is this step even allowed to be confirmed" is
        /// asked. Returns false when the answer is no, or when the step has already
        /// been confirmed.
        /// </summary>
        public bool Acknowledge()
        {
            if (!AcknowledgementOffered) return false;
            if (Acknowledged) return false;
            Acknowledged = true;
            return true;
        }

        // ---- construction ----------------------------------------------------

        public static TutorialInstructionCard Create(NebulaUiShell shell)
        {
            var go = new GameObject("Tutorial Instruction Card");
            var card = go.AddComponent<TutorialInstructionCard>();
            card.Build(shell);
            return card;
        }

        private void Build(NebulaUiShell shell)
        {
            RectTransform layer = shell.Root(UiLayer.HudStatic);

            Image surface = NebulaUiFactory.Panel(layer, "Tutorial Card", NebulaUiAssetCatalog.PanelPrimary);
            root = surface.rectTransform;
            NebulaUiFactory.Place(root, new Vector2(0.5f, 1f), RestOffset, new Vector2(Width, HeaderHeight));

            // The controller lives on the layer root and never inside the visual it
            // toggles: a widget parented into its own panel stops updating the
            // moment that panel is hidden, and can then never bring itself back.
            transform.SetParent(layer, false);

            group = root.gameObject.GetComponent<CanvasGroup>();
            if (group == null) group = root.gameObject.AddComponent<CanvasGroup>();
            // The card informs; it never takes a click. Leaving these true is how a
            // tutorial ends up teaching a manoeuvre the player cannot perform
            // because the instructions are in the way.
            group.interactable = false;
            group.blocksRaycasts = false;

            counter = NebulaUiFactory.Label(root, "Counter", TutorialUiCopy.Counter(0, 0),
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.ActionPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(counter.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, MetaY), new Vector2(200f, 18f));

            chapterTag = NebulaUiFactory.Label(root, "Chapter", string.Empty,
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleRight, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(chapterTag.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-Pad, MetaY), new Vector2(Inner - 210f, 18f));

            title = NebulaUiFactory.Label(root, "Title", TutorialUiCopy.NoStep,
                NebulaUiDesignTokens.SizeScreenTitle, NebulaUiDesignTokens.InkPrimary,
                TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingScreenTitle);
            NebulaUiFactory.Place(title.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, TitleY), new Vector2(Inner, 26f));

            instruction = NebulaUiFactory.Label(root, "Instruction", string.Empty,
                NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.UpperLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingBody, wrap: true);
            NebulaUiFactory.Place(instruction.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, InstructionY), new Vector2(Inner, InstructionHeight));

            checklistRoot = NebulaUiFactory.Group(root, "Checklist");
            NebulaUiFactory.Place(checklistRoot, new Vector2(0f, 1f),
                new Vector2(0f, -HeaderHeight), new Vector2(Width, RowPitch));

            overflow = NebulaUiFactory.Label(root, "Overflow", string.Empty,
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(overflow.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad + MarkSize + NebulaUiDesignTokens.S2, -HeaderHeight),
                new Vector2(Inner, 18f));
            overflow.Text.gameObject.SetActive(false);

            keyPrompt = NebulaUiFactory.Label(root, "KeyPrompt", string.Empty,
                NebulaUiDesignTokens.SizeKeyPrompt, NebulaUiDesignTokens.ActionPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingButton);
            NebulaUiFactory.Place(keyPrompt.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(Pad, -HeaderHeight), new Vector2(Inner, 18f));
            keyPrompt.Text.gameObject.SetActive(false);

            BuildFeedback();
            BuildAcknowledge();

            exitHint = NebulaUiFactory.Label(root, "ExitHint", TutorialKeyPrompts.Exit(),
                NebulaUiDesignTokens.SizeKeyPrompt, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleRight, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(exitHint.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-Pad, -HeaderHeight), new Vector2(Inner, 16f));

            root.gameObject.SetActive(false);
        }

        private void BuildFeedback()
        {
            feedbackRoot = NebulaUiFactory.Group(root, "Feedback");
            NebulaUiFactory.Place(feedbackRoot, new Vector2(0f, 1f),
                new Vector2(Pad, -HeaderHeight), new Vector2(Inner, 18f));

            feedbackMark = UiStatusCueView.Build(feedbackRoot, "Mark", UiStatusCue.None,
                MarkSize, showMarker: false);

            feedbackText = NebulaUiFactory.Label(feedbackRoot, "Text", string.Empty,
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(feedbackText.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(MarkSize + NebulaUiDesignTokens.S2, 0f),
                new Vector2(Inner - MarkSize - NebulaUiDesignTokens.S2, 18f));

            feedbackRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// The confirmation row. Built once and left inactive; only a reading step
        /// switches it on.
        /// <para>
        /// It carries its own <see cref="CanvasGroup"/> with
        /// <see cref="CanvasGroup.ignoreParentGroups"/> set, because the card's own
        /// group is deliberately non-interactive and raycast-transparent so a click
        /// passes through it to the game. This one row is the single exception, and
        /// making it an exception explicitly — rather than by opening the whole card
        /// up — is what keeps every other pixel of the card click-through.
        /// </para>
        /// </summary>
        private void BuildAcknowledge()
        {
            acknowledgeRoot = NebulaUiFactory.Group(root, TutorialUiCopy.AcknowledgeName);
            NebulaUiFactory.Place(acknowledgeRoot, new Vector2(0f, 1f),
                new Vector2(Pad, -HeaderHeight), new Vector2(Inner, AcknowledgeRowHeight));

            CanvasGroup rowGroup = acknowledgeRoot.gameObject.GetComponent<CanvasGroup>();
            if (rowGroup == null) rowGroup = acknowledgeRoot.gameObject.AddComponent<CanvasGroup>();
            rowGroup.ignoreParentGroups = true;
            rowGroup.interactable = true;
            rowGroup.blocksRaycasts = true;

            acknowledgeButton = NebulaUiFactory.ButtonControl(acknowledgeRoot, "Confirm",
                TutorialUiCopy.Acknowledge, NebulaUiAssetCatalog.ButtonKind.Primary,
                // Deliberately routed through Acknowledge() rather than setting the
                // flag here: the button is one of three ways in, and all three have
                // to be refused on a step that is not a reading.
                () => Acknowledge());
            NebulaUiFactory.Place((RectTransform)acknowledgeButton.Button.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0f),
                new Vector2(AcknowledgeButtonWidth, AcknowledgeButtonHeight));

            acknowledgeNote = NebulaUiFactory.Label(acknowledgeRoot, "Note",
                TutorialKeyPrompts.Acknowledge(),
                NebulaUiDesignTokens.SizeKeyPrompt, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(acknowledgeNote.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(AcknowledgeButtonWidth + NebulaUiDesignTokens.S3, 0f),
                new Vector2(Inner - AcknowledgeButtonWidth - NebulaUiDesignTokens.S3, 18f));

            acknowledgeRoot.gameObject.SetActive(false);
        }

        // ---- content ---------------------------------------------------------

        /// <summary>
        /// Puts a step on the card and starts its entry. <paramref name="total"/> is
        /// the whole curriculum in both run modes, so a standalone run of step 27
        /// reads "27 / 47" and not "1 / 1".
        /// </summary>
        public void Show(TutorialStepDefinition definition, int total, TutorialChapter chapter)
        {
            step = definition;
            CurrentTotal = Mathf.Max(0, total);
            CurrentGlobalNumber = definition == null ? 0 : definition.GlobalNumber;

            counter.Value = TutorialUiCopy.Counter(CurrentGlobalNumber, CurrentTotal);
            chapterTag.Value = TutorialUiCopy.ChapterTag(chapter);
            title.Value = definition == null ? TutorialUiCopy.NoStep : definition.TitleTr;
            instruction.Value = definition == null ? string.Empty : definition.InstructionTr;

            KeyPromptText = TutorialKeyPrompts.For(definition);
            keyPrompt.Value = KeyPromptText ?? string.Empty;
            keyPrompt.Text.gameObject.SetActive(KeyPromptText != null);

            exitHint.Value = TutorialKeyPrompts.Exit();

            // Offered by kind and by nothing else, and cleared before it is offered:
            // a step is only confirmable because the curriculum said so, and one
            // confirmation can never reach a second step.
            Acknowledged = false;
            AcknowledgementOffered = definition != null
                && definition.Kind == TutorialStepKind.InformationAcknowledgement;
            if (acknowledgeRoot != null)
            {
                acknowledgeNote.Value = TutorialKeyPrompts.Acknowledge();
                acknowledgeButton.SetAvailable(true);
                if (acknowledgeRoot.gameObject.activeSelf != AcknowledgementOffered)
                    acknowledgeRoot.gameObject.SetActive(AcknowledgementOffered);
            }

            LoadObjectives(definition);
            ClearFeedback();

            root.gameObject.SetActive(true);
            phase = UiPanelPhase.Entering;
            elapsed = 0f;
            pulse.Reset();

            Layout();
            // Applied here and not left to the first Tick: under Motion Off the card
            // has to be at its final pose the instant it appears, and a test that
            // reads the pose before any frame has run would otherwise photograph the
            // start of a tween that is not supposed to exist.
            ApplyPose();
        }

        private void LoadObjectives(TutorialStepDefinition definition)
        {
            objectiveText.Clear();
            objectiveDone.Clear();

            IReadOnlyList<string> checklist = definition?.ChecklistTr;
            if (checklist != null)
            {
                for (int i = 0; i < checklist.Count; i++)
                {
                    objectiveText.Add(checklist[i] ?? string.Empty);
                    objectiveDone.Add(false);
                }
            }
        }

        /// <summary>
        /// Pushes one evaluation onto the card. Called every frame while a step
        /// runs, so it writes only what changed.
        /// </summary>
        public void Apply(TutorialStepReport report)
        {
            if (report == null) return;

            IReadOnlyList<bool> objectives = report.Objectives;
            bool checklistMoved = false;
            for (int i = 0; i < objectiveDone.Count; i++)
            {
                bool done = objectives != null && i < objectives.Count && objectives[i];
                if (objectiveDone[i] == done) continue;
                objectiveDone[i] = done;
                checklistMoved = true;
            }
            if (checklistMoved) PaintObjectives();

            SetFeedback(report.Feedback, report.FeedbackKind);
        }

        /// <summary>The success beat, before the director moves on.</summary>
        public void ShowComplete()
        {
            for (int i = 0; i < objectiveDone.Count; i++) objectiveDone[i] = true;
            PaintObjectives();
            SetFeedback(TutorialUiCopy.StepCompleteFeedback, TutorialFeedbackKind.Success);
        }

        /// <summary>Takes the card off screen and forgets the step it was holding.</summary>
        public void Hide()
        {
            step = null;
            CurrentGlobalNumber = 0;
            CurrentTotal = 0;
            KeyPromptText = null;
            objectiveText.Clear();
            objectiveDone.Clear();
            HiddenObjectiveCount = 0;
            Acknowledged = false;
            AcknowledgementOffered = false;
            if (acknowledgeRoot != null && acknowledgeRoot.gameObject.activeSelf)
                acknowledgeRoot.gameObject.SetActive(false);
            ClearFeedback();
            phase = UiPanelPhase.Hidden;
            elapsed = 0f;
            pulse.Reset();
            if (root != null) root.gameObject.SetActive(false);
        }

        private void ClearFeedback() => SetFeedback(null, TutorialFeedbackKind.None);

        private void SetFeedback(string text, TutorialFeedbackKind kind)
        {
            bool changed = FeedbackKind != kind
                || !string.Equals(FeedbackText, text, System.StringComparison.Ordinal);

            FeedbackText = text;
            FeedbackKind = kind;

            bool visible = kind != TutorialFeedbackKind.None && !string.IsNullOrEmpty(text);
            if (feedbackRoot.gameObject.activeSelf != visible) feedbackRoot.gameObject.SetActive(visible);

            if (visible)
            {
                UiStatusCue cue = TutorialUiStates.FeedbackCue(kind);
                feedbackMark.SetCue(cue);
                feedbackText.Value = text;
                // StatusInk, not Status: this is a sentence the player has to read,
                // and Inactive-family colour clears the 3:1 mark bar without
                // clearing the 4.5:1 text bar.
                feedbackText.Text.color = NebulaUiDesignTokens.StatusInk(cue.Level);
            }

            if (kind != TutorialFeedbackKind.Success) pulse.Reset();
            if (changed) Layout();
        }

        // ---- checklist -------------------------------------------------------

        private ObjectiveRow EnsureRow(int index)
        {
            while (objectiveRows.Count <= index)
            {
                var row = new ObjectiveRow();
                row.Root = NebulaUiFactory.Group(checklistRoot, "Objective " + objectiveRows.Count);
                NebulaUiFactory.Place(row.Root, new Vector2(0f, 1f), Vector2.zero, new Vector2(Width, RowPitch));

                row.Mark = UiStatusCueView.Build(row.Root, "Mark",
                    TutorialUiStates.ObjectiveCue(false), MarkSize, showMarker: false);
                NebulaUiFactory.Place(row.Mark.Root, new Vector2(0f, 0.5f),
                    new Vector2(Pad, 0f), new Vector2(MarkSize, MarkSize));

                row.Label = NebulaUiFactory.Label(row.Root, "Label", string.Empty,
                    NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkSecondary,
                    TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingBody);
                NebulaUiFactory.Place(row.Label.Text.rectTransform, new Vector2(0f, 0.5f),
                    new Vector2(Pad + MarkSize + NebulaUiDesignTokens.S2, 0f),
                    new Vector2(Inner - MarkSize - NebulaUiDesignTokens.S2 - StateWordWidth, 18f));

                // The word channel. A tick and a green line say the same thing twice
                // for a player who can see both; this is the third saying, for the
                // one who can see neither.
                row.StateWord = NebulaUiFactory.Label(row.Root, "State", string.Empty,
                    NebulaUiDesignTokens.SizeKeyPrompt, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.MiddleRight, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
                NebulaUiFactory.Place(row.StateWord.Text.rectTransform, new Vector2(1f, 0.5f),
                    new Vector2(-Pad, 0f), new Vector2(StateWordWidth, 18f));

                objectiveRows.Add(row);
            }

            return objectiveRows[index];
        }

        private void PaintObjectives()
        {
            int drawn = objectiveText.Count - HiddenObjectiveCount;
            for (int i = 0; i < drawn; i++)
            {
                ObjectiveRow row = EnsureRow(i);
                bool done = objectiveDone[i];
                TutorialStateVisual visual = TutorialUiStates.Visual(
                    done ? TutorialUiState.Complete : TutorialUiState.Incomplete);

                row.Label.Value = objectiveText[i];
                row.Label.Text.color = visual.Ink;
                row.Mark.SetCue(TutorialUiStates.ObjectiveCue(done));
                row.StateWord.Value = visual.Marker;
                row.StateWord.Text.color = visual.Ink;
            }
        }

        /// <summary>Test-facing: whether objective <paramref name="index"/> is drawn as met.</summary>
        public bool ObjectiveDone(int index) =>
            index >= 0 && index < objectiveDone.Count && objectiveDone[index];

        // ---- layout ----------------------------------------------------------

        /// <summary>
        /// Sizes the card to what it is actually carrying, and refuses to grow past
        /// the middle of the screen.
        /// <para>
        /// The ceiling is computed from the live interface scale rather than fixed,
        /// because the canvas shrinks as the scale grows -- at 140% the reference
        /// canvas is 771 units tall, so a card that was comfortably clear of the
        /// reticle at 100% sits right on it. What gives first is the checklist's
        /// row pitch, then the number of rows drawn, and the rows that did not fit
        /// are counted out loud rather than dropped.
        /// </para>
        /// </summary>
        private void Layout()
        {
            bool hasKey = KeyPromptText != null;
            bool hasFeedback = FeedbackKind != TutorialFeedbackKind.None
                && !string.IsNullOrEmpty(FeedbackText);

            bool hasAcknowledge = AcknowledgementOffered;

            float fixedHeight = HeaderHeight
                + (hasKey ? KeyRowHeight : 0f)
                + (hasFeedback ? FeedbackRowHeight : 0f)
                + (hasAcknowledge ? AcknowledgeRowHeight : 0f)
                + ExitHintHeight + BottomPad;

            float ceiling = Mathf.Max(HeaderHeight + BottomPad,
                CanvasHalfHeight() - Top - ReticleClearanceFloor);
            float forChecklist = Mathf.Max(0f, ceiling - fixedHeight);

            int wanted = objectiveText.Count;
            float pitch = RowPitch;
            int drawn = wanted;

            if (wanted > 0 && wanted * pitch > forChecklist)
            {
                pitch = MinRowPitch;
                drawn = Mathf.Clamp(Mathf.FloorToInt(forChecklist / pitch), 0, wanted);
                // One row is given up so the "+N daha" line has somewhere to go: a
                // count the player cannot see is the same as no count at all.
                if (drawn < wanted && drawn > 0) drawn--;
            }

            HiddenObjectiveCount = wanted - drawn;

            float y = HeaderHeight;
            for (int i = 0; i < drawn; i++)
            {
                ObjectiveRow row = EnsureRow(i);
                if (!row.Root.gameObject.activeSelf) row.Root.gameObject.SetActive(true);
                row.Root.sizeDelta = new Vector2(Width, pitch);
                row.Root.anchoredPosition = new Vector2(0f, -(i * pitch));
            }
            for (int i = drawn; i < objectiveRows.Count; i++)
            {
                if (objectiveRows[i].Root.gameObject.activeSelf)
                    objectiveRows[i].Root.gameObject.SetActive(false);
            }

            checklistRoot.anchoredPosition = new Vector2(0f, -y);
            checklistRoot.sizeDelta = new Vector2(Width, Mathf.Max(pitch, drawn * pitch));
            y += drawn * pitch;

            bool hasOverflow = HiddenObjectiveCount > 0;
            if (overflow.Text.gameObject.activeSelf != hasOverflow)
                overflow.Text.gameObject.SetActive(hasOverflow);
            if (hasOverflow)
            {
                overflow.Value = $"+{HiddenObjectiveCount} HEDEF DAHA";
                overflow.Text.rectTransform.anchoredPosition =
                    new Vector2(Pad + MarkSize + NebulaUiDesignTokens.S2, -y);
                y += pitch;
            }

            if (hasKey)
            {
                keyPrompt.Text.rectTransform.anchoredPosition = new Vector2(Pad, -y - 3f);
                y += KeyRowHeight;
            }

            if (hasFeedback)
            {
                feedbackRoot.anchoredPosition = new Vector2(Pad, -y - 3f);
                y += FeedbackRowHeight;
            }

            if (acknowledgeRoot != null)
            {
                if (acknowledgeRoot.gameObject.activeSelf != hasAcknowledge)
                    acknowledgeRoot.gameObject.SetActive(hasAcknowledge);
                if (hasAcknowledge)
                {
                    acknowledgeRoot.anchoredPosition = new Vector2(Pad, -y - 3f);
                    y += AcknowledgeRowHeight;
                }
            }

            exitHint.Text.rectTransform.anchoredPosition = new Vector2(-Pad, -y - 1f);
            y += ExitHintHeight + BottomPad;

            root.sizeDelta = new Vector2(Width, y);

            PaintObjectives();
        }

        /// <summary>
        /// Half the reference canvas, in the units the card is laid out in. The
        /// canvas scaler expresses the interface scale by dividing the reference
        /// resolution, so the middle of the screen moves up as the scale goes up.
        /// </summary>
        private static float CanvasHalfHeight() =>
            NebulaUiShell.ReferenceResolution.y * 0.5f
            / Mathf.Clamp(NebulaUiShell.UiScale, NebulaUiShell.MinUiScale, NebulaUiShell.MaxUiScale);

        // ---- animation -------------------------------------------------------

        /// <summary>
        /// Unscaled, always. The card is on screen while the game runs and while a
        /// confirmation has the clock at zero, and an entry that stops mid-slide
        /// because something paused is an entry that never finishes.
        /// </summary>
        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>
        /// One frame of the card's own motion. Split out because
        /// <see cref="UnityEngine.Input"/> and the frame loop cannot be driven from
        /// an EditMode fixture here, so the animation is exercised by calling this.
        /// </summary>
        internal void Tick(float unscaledSeconds)
        {
            if (root == null || !root.gameObject.activeSelf) return;

            float duration = NebulaUiDesignTokens.MotionDuration(UiPanelPhase.Entering);
            if (phase == UiPanelPhase.Entering)
            {
                if (duration <= 0f)
                {
                    // Motion Off: the card still has to end up open, it simply must
                    // not be watched arriving.
                    elapsed = 0f;
                    phase = UiPanelPhase.Shown;
                }
                else
                {
                    elapsed += Mathf.Max(0f, unscaledSeconds);
                    if (elapsed >= duration) { elapsed = duration; phase = UiPanelPhase.Shown; }
                }
                ApplyPose();
            }

            // The success beat is the only repeating thing on the card, and it only
            // repeats when the player's settings allow one at all -- Safe Mode and
            // Motion Off both hold it steady rather than switching it off, so the
            // state is still shown, it just stops moving.
            if (FeedbackKind == TutorialFeedbackKind.Success && NebulaUiDesignTokens.AllowsRepeatingEmphasis)
            {
                pulse.Tick(unscaledSeconds);
                feedbackText.Text.color = pulse.Apply(
                    NebulaUiDesignTokens.StatusInk(UiStatusLevel.Nominal));
            }
            else if (!Mathf.Approximately(pulse.Level, 1f))
            {
                pulse.Reset();
            }
        }

        private void ApplyPose()
        {
            float progress = AnimationProgress01;
            float eased = NebulaUiDesignTokens.Ease(progress);
            float travel = NebulaUiDesignTokens.SlideDistance() * (1f - eased);

            root.anchoredPosition = new Vector2(0f, -Top + travel);
            // Never from zero: a card that fades in from invisible is a card that is
            // absent for the first frames of the step it is describing.
            group.alpha = Mathf.Lerp(0.35f, 1f, eased);
        }
    }
}
