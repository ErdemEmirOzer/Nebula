using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Tutorial;
using Nebula.UI.Accessibility;
using Nebula.UI.Design;
using Nebula.UI.Session;
using Nebula.UI.Shell;

namespace Nebula.UI.Tutorial
{
    /// <summary>
    /// What the player is told when a run ends, whichever way it ended.
    /// <para>
    /// Two different sentences rather than one with a flag: finishing the whole
    /// curriculum says <b>EĞİTİM TAMAMLANDI</b>, and anything short of that says
    /// what was actually covered. A summary that congratulates a player for a run
    /// they left after two steps is the reason nobody reads summaries.
    /// </para>
    /// <para>
    /// A single-step run reports the same totals as a sequential one, because the
    /// numbers are the curriculum's and not this sitting's -- the same rule the
    /// instruction card's counter follows.
    /// </para>
    /// </summary>
    public sealed class TutorialSummaryScreen : MonoBehaviour, INebulaUiScreen
    {
        public string ScreenId => "tutorial.summary";

        private ITutorialLauncher launcher;
        private NebulaUiShell shell;
        private RectTransform root;

        private NebulaUiFactory.LabelHandle headline;
        private NebulaUiFactory.LabelHandle tally;
        private NebulaUiFactory.LabelHandle modeLine;
        private UiStatusCueView.Handle mark;
        private NebulaUiFactory.ButtonHandle mainMenu;
        private NebulaUiFactory.ButtonHandle restart;

        /// <summary>Test-facing: whether the whole curriculum was covered.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>Test-facing: how many steps the director counted as done.</summary>
        public int CompletedCount { get; private set; }

        /// <summary>Test-facing: the curriculum's total.</summary>
        public int Total { get; private set; }

        /// <summary>Test-facing: which door the run came through.</summary>
        public TutorialRunMode Mode { get; private set; }

        /// <summary>Test-facing: the headline exactly as drawn.</summary>
        public string HeadlineText => headline == null ? string.Empty : headline.Value;

        /// <summary>Test-facing: how many times the player asked for the main menu.</summary>
        public int MainMenuRequests { get; private set; }

        /// <summary>Test-facing: how many times the player asked to run it again.</summary>
        public int RestartRequests { get; private set; }

        public static TutorialSummaryScreen Create(NebulaUiShell shell, ITutorialLauncher launcher)
        {
            var go = new GameObject("Tutorial Summary");
            var screen = go.AddComponent<TutorialSummaryScreen>();
            screen.launcher = launcher;
            screen.Build(shell);
            return screen;
        }

        private void Build(NebulaUiShell owningShell)
        {
            shell = owningShell;

            root = NebulaUiFactory.Group(shell.Root(UiLayer.Menu), "Tutorial Summary Root");
            NebulaUiFactory.Fill(root);
            transform.SetParent(shell.Root(UiLayer.Menu), false);

            Image scrim = NebulaUiFactory.Rect(root, "Scrim", NebulaUiDesignTokens.OverlayDim);
            NebulaUiFactory.Fill(scrim.rectTransform);
            scrim.raycastTarget = true;

            Image panel = NebulaUiFactory.Panel(root, "Panel", NebulaUiAssetCatalog.Modal);
            RectTransform card = panel.rectTransform;
            NebulaUiFactory.Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 320f));

            mark = UiStatusCueView.Build(card, "Mark", UiStatusCue.None, 22f, showMarker: false);
            NebulaUiFactory.Place(mark.Root, new Vector2(0f, 1f), new Vector2(32f, -38f),
                new Vector2(22f, 22f));

            headline = NebulaUiFactory.Label(card, "Headline", TutorialUiCopy.SummaryTitle,
                NebulaUiDesignTokens.SizeScreenTitle, NebulaUiDesignTokens.InkPrimary,
                TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingScreenTitle);
            NebulaUiFactory.Place(headline.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(66f, -36f), new Vector2(520f, 28f));

            tally = NebulaUiFactory.Label(card, "Tally", string.Empty,
                NebulaUiDesignTokens.SizeValue, NebulaUiDesignTokens.InkPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(tally.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(32f, -92f), new Vector2(556f, 24f));

            modeLine = NebulaUiFactory.Label(card, "Mode", string.Empty,
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.UpperLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption, wrap: true);
            NebulaUiFactory.Place(modeLine.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(32f, -128f), new Vector2(556f, 60f));

            restart = NebulaUiFactory.ButtonControl(card, TutorialUiCopy.RestartName,
                TutorialUiCopy.SummaryRestart, NebulaUiAssetCatalog.ButtonKind.Secondary,
                () => ActivateRestart());
            NebulaUiFactory.Place(restart.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(32f, 32f), new Vector2(260f, 46f));

            mainMenu = NebulaUiFactory.ButtonControl(card, TutorialUiCopy.MainMenuName,
                TutorialUiCopy.SummaryToMainMenu, NebulaUiAssetCatalog.ButtonKind.Primary,
                () => ActivateMainMenu());
            NebulaUiFactory.Place(mainMenu.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(-32f, 32f), new Vector2(260f, 46f));

            root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Fills the card in and pushes it. <paramref name="total"/> is the whole
        /// curriculum in both modes.
        /// </summary>
        public bool Show(TutorialRunMode mode, int completedCount, int total)
        {
            Mode = mode;
            CompletedCount = Mathf.Max(0, completedCount);
            Total = Mathf.Max(0, total);
            IsComplete = Total > 0 && CompletedCount >= Total;

            headline.Value = TutorialUiCopy.SummaryHeadline(IsComplete);
            headline.Text.color = IsComplete
                ? NebulaUiDesignTokens.Celebration
                : NebulaUiDesignTokens.InkPrimary;

            // Shape and word alongside the colour, as everywhere else: the
            // completion state must be readable by a player who cannot tell the
            // gold headline from the grey one.
            mark.SetCue(UiStatusCue.Of(IsComplete ? UiCueKind.Nominal : UiCueKind.Working));

            tally.Value = TutorialUiCopy.SummaryTally(CompletedCount, Total);
            modeLine.Value = TutorialUiCopy.SummaryMode(mode);

            restart.SetAvailable(launcher != null, TutorialUiCopy.NoLauncher);
            mainMenu.SetAvailable(launcher != null, TutorialUiCopy.NoLauncher);

            root.gameObject.SetActive(true);
            if (shell == null) return false;
            if (shell.Top == (INebulaUiScreen)this) return true;
            return shell.Push(this);
        }

        /// <summary>Test-facing: what ANA MENÜ does.</summary>
        internal bool ActivateMainMenu()
        {
            MainMenuRequests++;
            if (launcher == null) return false;

            shell?.PopAll();
            launcher.ExitToMainMenu();
            return true;
        }

        /// <summary>Test-facing: what BAŞTAN BAŞLA does from the summary.</summary>
        internal bool ActivateRestart()
        {
            RestartRequests++;
            if (launcher == null) return false;

            shell?.PopAll();
            launcher.StartFromBeginning();
            return true;
        }

        // ---- INebulaUiScreen -------------------------------------------------

        public void OnOpened(NebulaUiShell owningShell)
        {
            shell = owningShell;
            root.gameObject.SetActive(true);

            EventSystem events = EventSystem.current;
            if (events != null && mainMenu != null)
                events.SetSelectedGameObject(mainMenu.Button.gameObject);
        }

        public void OnClosed() => root.gameObject.SetActive(false);

        public void OnCovered() { }

        public void OnRevealed() { }

        /// <summary>
        /// The summary is the end of the run and Back has nowhere to go from it.
        /// Consumed rather than passed through, so Escape cannot pop the last screen
        /// and leave the player on an empty tutorial scene with no way out.
        /// </summary>
        public bool HandleBack() => true;
    }
}
