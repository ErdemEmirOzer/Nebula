using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Tutorial;
using Nebula.UI.Design;
using Nebula.UI.Session;
using Nebula.UI.Shell;

namespace Nebula.UI.Tutorial
{
    /// <summary>
    /// BÖLÜMLER: every step in the curriculum, grouped under its chapter, and all
    /// of it reachable.
    /// <para>
    /// There is no lock, no progress gate and nothing persisted. That is a design
    /// decision and not an omission: a player who wants to practise docking is not
    /// helped by being told to finish forty other steps first, and a tutorial that
    /// remembers where you got to is a tutorial with a save file, a schema and a
    /// migration -- for a mode that is supposed to be free to walk away from. Every
    /// row on this screen is live on a cold install.
    /// </para>
    /// <para>
    /// The list is built from <c>TutorialStepCatalog</c> and holds no copy of it. An
    /// empty catalog renders as an empty state that says so, rather than as a blank
    /// panel that reads as a screen which failed to load.
    /// </para>
    /// </summary>
    public sealed class TutorialChapterListScreen : MonoBehaviour, INebulaUiScreen
    {
        public string ScreenId => "tutorial.chapters";

        /// <summary>Height of one step row.</summary>
        public const float RowHeight = 58f;

        /// <summary>Row-to-row distance.</summary>
        public const float RowPitch = 62f;

        /// <summary>Height of a chapter heading between two groups of rows.</summary>
        public const float HeadingPitch = 40f;

        private const float ListLeft = 88f;
        private const float ListTop = -132f;
        private const float ListWidth = 1180f;

        /// <summary>
        /// Viewport height. 132 + 580 = 712, which still clears the bottom at 140%
        /// interface scale, where the reference canvas is only 771 units tall.
        /// </summary>
        private const float ListHeight = 580f;

        private ITutorialLauncher launcher;
        private NebulaUiShell shell;
        private RectTransform root;
        private RectTransform content;
        private ScrollRect scroller;
        private NebulaUiFactory.LabelHandle emptyState;
        private NebulaUiFactory.LabelHandle blocked;

        private sealed class StepRow
        {
            public string StepId;
            public int GlobalNumber;
            public RectTransform Root;
            public Button Button;
        }

        private readonly List<StepRow> rows = new List<StepRow>(64);

        /// <summary>Raised once a run has been asked for, so the menu shell can stand down.</summary>
        public event Action RunStarted;

        /// <summary>Test-facing: how many step rows the screen drew.</summary>
        public int RowCount => rows.Count;

        /// <summary>Test-facing: how many chapter headings the screen drew.</summary>
        public int HeadingCount { get; private set; }

        /// <summary>Test-facing: the last step the player asked to enter, or <c>null</c>.</summary>
        public string LastRequestedStepId { get; private set; }

        /// <summary>Test-facing: how many single-step runs reached the launcher.</summary>
        public int StandaloneStarts { get; private set; }

        /// <summary>Test-facing: why nothing can be started, or <c>null</c> when it can.</summary>
        public string BlockedReason { get; private set; }

        /// <summary>
        /// Test-facing, and deliberately a constant: this screen gates nothing on
        /// progress, so the answer can never become true by accident.
        /// </summary>
        public bool GatesRowsOnProgress => false;

        /// <summary>Test-facing: the whole scrollable height of the list.</summary>
        public float ContentHeight => content == null ? 0f : content.sizeDelta.y;

        /// <summary>Test-facing: how tall the window onto the list is.</summary>
        public static float ViewportHeight => ListHeight;

        public static TutorialChapterListScreen Create(NebulaUiShell shell, ITutorialLauncher launcher)
        {
            var go = new GameObject("Tutorial Chapter List");
            var screen = go.AddComponent<TutorialChapterListScreen>();
            screen.launcher = launcher;
            screen.Build(shell);
            return screen;
        }

        private void Build(NebulaUiShell owningShell)
        {
            shell = owningShell;

            root = NebulaUiFactory.Group(shell.Root(UiLayer.Menu), "Tutorial Chapters Root");
            NebulaUiFactory.Fill(root);
            transform.SetParent(shell.Root(UiLayer.Menu), false);

            Image backdrop = NebulaUiFactory.Rect(root, "Backdrop", NebulaUiDesignTokens.SpaceVoid);
            NebulaUiFactory.Fill(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            NebulaSessionChrome.Header(root, TutorialUiCopy.ChapterListTitle);
            NebulaSessionChrome.BackButton(root, () => GoBack());

            NebulaUiFactory.Place(
                NebulaUiFactory.Label(root, "Note", TutorialUiCopy.ChapterListNote,
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                    TextAnchor.UpperLeft, FontStyle.Normal,
                    NebulaUiDesignTokens.TrackingCaption).Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(ListLeft, -104f), new Vector2(ListWidth, 18f));

            BuildViewport();

            emptyState = NebulaUiFactory.Label(root, "Empty", string.Empty,
                NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.InkMuted,
                TextAnchor.UpperLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption, wrap: true);
            NebulaUiFactory.Place(emptyState.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(ListLeft + 24f, ListTop - 24f), new Vector2(ListWidth - 48f, 44f));

            blocked = NebulaUiFactory.Label(root, "Blocked", string.Empty,
                NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.Status(Visuals.UiStatusLevel.Caution),
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(blocked.Text.rectTransform, new Vector2(0f, 0f),
                new Vector2(ListLeft + 232f, 24f), new Vector2(700f, 20f));

            Rebuild();
        }

        /// <summary>
        /// A masked window with a taller sheet inside it. A real
        /// <see cref="ScrollRect"/> rather than a hand-rolled offset so the wheel
        /// and a drag work without this screen reading input at all -- which matters
        /// here, because input cannot be faked in this project's tests and anything
        /// driven by a key read would be untestable. The seams below move the same
        /// sheet without a pointer.
        /// </summary>
        private void BuildViewport()
        {
            Image frame = NebulaUiFactory.Panel(root, "List", NebulaUiAssetCatalog.PanelSecondary);
            RectTransform frameRect = frame.rectTransform;
            NebulaUiFactory.Place(frameRect, new Vector2(0f, 1f),
                new Vector2(ListLeft, ListTop), new Vector2(ListWidth, ListHeight));

            RectTransform viewport = NebulaUiFactory.Group(frameRect, "Viewport");
            NebulaUiFactory.Fill(viewport, NebulaUiDesignTokens.S2);
            if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();

            content = NebulaUiFactory.Group(viewport, "Content");
            NebulaUiFactory.Place(content, new Vector2(0f, 1f), Vector2.zero,
                new Vector2(ListWidth - NebulaUiDesignTokens.S4, ListHeight));

            scroller = frameRect.gameObject.GetComponent<ScrollRect>();
            if (scroller == null) scroller = frameRect.gameObject.AddComponent<ScrollRect>();
            scroller.viewport = viewport;
            scroller.content = content;
            scroller.horizontal = false;
            scroller.vertical = true;
            scroller.movementType = ScrollRect.MovementType.Clamped;
            // No inertia and no elasticity. A list that keeps sliding after the
            // player stopped is decorative motion, and decorative motion is the
            // thing Reduced Motion and Motion Off exist to remove -- so it is simply
            // never introduced rather than switched off twice.
            scroller.inertia = false;
            scroller.scrollSensitivity = RowPitch;
        }

        // ---- content ---------------------------------------------------------

        /// <summary>
        /// Rebuilds the list from the catalog. Public because the catalog is a
        /// static the tutorial's own tests populate, and a screen that could only
        /// read it once at construction would be untestable against more than one
        /// curriculum.
        /// </summary>
        public void Rebuild()
        {
            ClearRows();

            IReadOnlyList<TutorialStepDefinition> all = SafeSteps();
            IReadOnlyList<TutorialChapter> chapters = SafeChapters();

            float y = 0f;
            int headings = 0;

            // Chapter order comes from the catalog, and any step whose chapter the
            // catalog does not describe still gets a row -- under a heading built
            // from its own chapter id. A step that exists and cannot be reached is
            // the one failure this screen must not have.
            var placed = new HashSet<string>(StringComparer.Ordinal);

            for (int c = 0; c < chapters.Count; c++)
            {
                TutorialChapter chapter = chapters[c];
                if (chapter == null) continue;

                List<TutorialStepDefinition> members = StepsIn(all, chapter.Id, placed);
                if (members.Count == 0) continue;

                BuildHeading(chapter.Id.ToString(), TutorialUiCopy.ChapterTag(chapter), y);
                headings++;
                y += HeadingPitch;

                for (int i = 0; i < members.Count; i++)
                {
                    BuildRow(members[i], chapter, y);
                    y += RowPitch;
                }

                y += NebulaUiDesignTokens.S2;
            }

            // Anything the chapter table did not account for. Grouped under its own
            // heading rather than appended loose, so the list never loses its shape.
            for (int i = 0; i < all.Count; i++)
            {
                TutorialStepDefinition step = all[i];
                if (step == null || placed.Contains(step.StepId)) continue;

                BuildHeading(step.Chapter.ToString(), NebulaText.Upper(step.Chapter.ToString()), y);
                headings++;
                y += HeadingPitch;

                for (int j = i; j < all.Count; j++)
                {
                    TutorialStepDefinition other = all[j];
                    if (other == null || other.Chapter != step.Chapter) continue;
                    if (!placed.Add(other.StepId)) continue;
                    BuildRow(other, null, y);
                    y += RowPitch;
                }

                y += NebulaUiDesignTokens.S2;
            }

            HeadingCount = headings;
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(ListHeight, y));
            ScrollTo(1f);

            bool empty = rows.Count == 0;
            emptyState.Value = empty ? TutorialUiCopy.EmptyCatalog : string.Empty;
            emptyState.Text.gameObject.SetActive(empty);

            BlockedReason = launcher == null ? TutorialUiCopy.NoLauncher : null;
            blocked.Value = BlockedReason ?? string.Empty;
            blocked.Text.gameObject.SetActive(BlockedReason != null);
        }

        /// <summary>
        /// The catalog, defended against being absent or half-built. It is shipped
        /// by another workstream and may legitimately be empty while this screen is
        /// alive, and a list that throws on an empty curriculum is a list that
        /// cannot be developed against.
        /// </summary>
        private static IReadOnlyList<TutorialStepDefinition> SafeSteps()
        {
            IReadOnlyList<TutorialStepDefinition> all = TutorialStepCatalog.All;
            return all ?? Array.Empty<TutorialStepDefinition>();
        }

        private static IReadOnlyList<TutorialChapter> SafeChapters()
        {
            IReadOnlyList<TutorialChapter> chapters = TutorialStepCatalog.Chapters;
            return chapters ?? Array.Empty<TutorialChapter>();
        }

        private static List<TutorialStepDefinition> StepsIn(IReadOnlyList<TutorialStepDefinition> all,
            TutorialChapterId chapter, HashSet<string> placed)
        {
            var members = new List<TutorialStepDefinition>();
            for (int i = 0; i < all.Count; i++)
            {
                TutorialStepDefinition step = all[i];
                if (step == null || step.Chapter != chapter) continue;
                if (!placed.Add(step.StepId)) continue;
                members.Add(step);
            }
            return members;
        }

        private void BuildHeading(string id, string caption, float y)
        {
            NebulaUiFactory.LabelHandle heading = NebulaUiFactory.SectionTitle(
                content, TutorialUiCopy.ChapterHeadingNamePrefix + id, caption);
            NebulaUiFactory.Place(heading.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(NebulaUiDesignTokens.S4, -y - 10f),
                new Vector2(ListWidth - NebulaUiDesignTokens.S8, 20f));

            Image rule = NebulaUiFactory.Rect(content, "Rule " + id, NebulaUiDesignTokens.Divider);
            NebulaUiFactory.Place(rule.rectTransform, new Vector2(0f, 1f),
                new Vector2(NebulaUiDesignTokens.S4, -y - 32f),
                new Vector2(ListWidth - NebulaUiDesignTokens.S8, 1f));
        }

        /// <summary>
        /// One step. Built on the catalog's row surface with the shared interaction
        /// states, not on a shape invented here -- the same mount every other
        /// dynamic list in the interface sits on.
        /// </summary>
        private void BuildRow(TutorialStepDefinition step, TutorialChapter chapter, float y)
        {
            var row = new StepRow { StepId = step.StepId, GlobalNumber = step.GlobalNumber };

            Image surface = NebulaUiFactory.Panel(content,
                TutorialUiCopy.StepRowNamePrefix + step.StepId,
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            row.Root = surface.rectTransform;
            NebulaUiFactory.Place(row.Root, new Vector2(0f, 1f),
                new Vector2(NebulaUiDesignTokens.S4, -y),
                new Vector2(ListWidth - NebulaUiDesignTokens.S8, RowHeight));

            // The global number, which is what the card will also be showing while
            // the step runs. Same number, both surfaces, one source.
            NebulaUiFactory.Place(
                NebulaUiFactory.Label(row.Root, "Number",
                    step.GlobalNumber > 0 ? step.GlobalNumber.ToString() : TutorialUiCopy.NoStep,
                    NebulaUiDesignTokens.SizeValue, NebulaUiDesignTokens.ActionPrimary,
                    TextAnchor.MiddleRight, FontStyle.Bold,
                    NebulaUiDesignTokens.TrackingCaption).Text.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(56f, 22f));

            NebulaUiFactory.Place(
                NebulaUiFactory.Label(row.Root, "Title", step.TitleTr,
                    NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.InkPrimary,
                    TextAnchor.MiddleLeft, FontStyle.Bold,
                    NebulaUiDesignTokens.TrackingBody).Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(88f, -10f), new Vector2(700f, 20f));

            NebulaUiFactory.Place(
                NebulaUiFactory.Label(row.Root, "Summary", step.SummaryTr ?? string.Empty,
                    NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkSecondary,
                    TextAnchor.MiddleLeft, FontStyle.Normal,
                    NebulaUiDesignTokens.TrackingBody).Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(88f, -32f), new Vector2(700f, 18f));

            // Which chapter and system this belongs to, on the row itself. A player
            // scrolling for "the one about drills" is looking for the system name,
            // not the step title.
            string tag = chapter != null
                ? TutorialUiCopy.ChapterTag(chapter)
                : NebulaText.Upper(step.Chapter.ToString());
            NebulaUiFactory.Place(
                NebulaUiFactory.Label(row.Root, "Chapter", tag,
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.MiddleRight, FontStyle.Normal,
                    NebulaUiDesignTokens.TrackingCaption).Text.rectTransform,
                new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(300f, 18f));

            surface.raycastTarget = true;
            row.Button = row.Root.gameObject.GetComponent<Button>();
            if (row.Button == null) row.Button = row.Root.gameObject.AddComponent<Button>();
            row.Button.targetGraphic = surface;
            TutorialUiStates.ApplyRowInteraction(row.Button);

            int index = rows.Count;
            row.Button.onClick.AddListener(() => Activate(index));

            rows.Add(row);
        }

        private void ClearRows()
        {
            rows.Clear();
            HeadingCount = 0;
            if (content == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        // ---- actions ---------------------------------------------------------

        /// <summary>
        /// Test-facing: what clicking row <paramref name="index"/> does. Returns
        /// false when the index is not on screen, so a fixture cannot pass by
        /// activating nothing.
        /// </summary>
        internal bool Activate(int index)
        {
            if (index < 0 || index >= rows.Count) return false;

            StepRow row = rows[index];
            LastRequestedStepId = row.StepId;

            if (launcher == null) { Rebuild(); return false; }

            StandaloneStarts++;
            shell?.PopAll();
            launcher.StartSingleStep(row.StepId);
            RunStarted?.Invoke();
            return true;
        }

        /// <summary>Test-facing: activate by step id rather than by position.</summary>
        internal bool ActivateStep(string stepId)
        {
            for (int i = 0; i < rows.Count; i++)
                if (string.Equals(rows[i].StepId, stepId, StringComparison.Ordinal)) return Activate(i);
            return false;
        }

        /// <summary>Test-facing: the step id drawn at <paramref name="index"/>, or <c>null</c>.</summary>
        public string StepIdAt(int index) =>
            index >= 0 && index < rows.Count ? rows[index].StepId : null;

        /// <summary>Test-facing: the global number drawn at <paramref name="index"/>, or 0.</summary>
        public int GlobalNumberAt(int index) =>
            index >= 0 && index < rows.Count ? rows[index].GlobalNumber : 0;

        /// <summary>
        /// Test-facing: where the list is scrolled to. 1 is the top.
        /// <para>
        /// Read off the sheet's own position rather than off
        /// <see cref="ScrollRect.verticalNormalizedPosition"/>. The ScrollRect keeps
        /// that number in bounds it recomputes during the canvas update, and an
        /// EditMode fixture never runs one -- so asking it would report a scroll
        /// that has not happened, or NaN.
        /// </para>
        /// </summary>
        public float ScrollPosition01
        {
            get
            {
                if (content == null) return 1f;
                float span = Mathf.Max(1f, ContentHeight - ListHeight);
                return Mathf.Clamp01(1f - content.anchoredPosition.y / span);
            }
        }

        /// <summary>
        /// Test-facing: scrolls the sheet without a pointer, which is the only way
        /// to reach it in a fixture here. 1 is the top, 0 the bottom.
        /// <para>
        /// The sheet is moved directly <i>and</i> the ScrollRect is told, so the
        /// wheel and a drag pick up from where this left off instead of snapping
        /// back to their own idea of the position.
        /// </para>
        /// </summary>
        internal void ScrollTo(float position01)
        {
            if (content == null) return;

            float clamped = Mathf.Clamp01(position01);
            float span = Mathf.Max(0f, ContentHeight - ListHeight);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, (1f - clamped) * span);
            if (scroller != null) scroller.verticalNormalizedPosition = clamped;
        }

        /// <summary>
        /// Test-facing: brings one row into the window. This is what a keyboard
        /// player's focus move has to do, and what the fixture calls to prove a row
        /// far down the list is genuinely reachable.
        /// </summary>
        internal bool ScrollToRow(int index)
        {
            if (content == null || index < 0 || index >= rows.Count) return false;

            float span = Mathf.Max(1f, ContentHeight - ListHeight);
            float rowTop = -rows[index].Root.anchoredPosition.y;
            ScrollTo(1f - (rowTop - ListHeight * 0.5f + RowHeight * 0.5f) / span);
            return true;
        }

        /// <summary>
        /// Test-facing: whether row <paramref name="index"/> is inside the window
        /// right now. The claim "every step is reachable" is only worth anything if
        /// a fixture can check a row it had to scroll to.
        /// </summary>
        public bool RowVisible(int index)
        {
            if (content == null || index < 0 || index >= rows.Count) return false;

            float top = -rows[index].Root.anchoredPosition.y - content.anchoredPosition.y;
            return top + RowHeight > 0f && top < ListHeight;
        }

        /// <summary>Test-facing: puts keyboard focus on one row, scrolling it into view first.</summary>
        internal bool FocusRow(int index)
        {
            if (index < 0 || index >= rows.Count) return false;
            ScrollToRow(index);

            EventSystem events = EventSystem.current;
            if (events != null) events.SetSelectedGameObject(rows[index].Root.gameObject);
            return true;
        }

        internal bool GoBack()
        {
            if (shell == null) return false;
            return shell.Pop();
        }

        // ---- INebulaUiScreen -------------------------------------------------

        public void OnOpened(NebulaUiShell owningShell)
        {
            shell = owningShell;
            root.gameObject.SetActive(true);
            // Re-read on open: the catalog is a static, and the list must never be
            // an older photograph of it.
            Rebuild();
            FocusRow(0);
        }

        public void OnClosed() => root.gameObject.SetActive(false);

        public void OnCovered() { }

        public void OnRevealed() => Rebuild();

        /// <summary>No nested state, so Back falls through and the shell pops this screen.</summary>
        public bool HandleBack() => false;
    }
}
