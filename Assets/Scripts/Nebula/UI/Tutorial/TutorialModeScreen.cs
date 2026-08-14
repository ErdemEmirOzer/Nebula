using System;
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
    /// The tutorial's front door: start at the beginning, pick a step, or go back.
    /// <para>
    /// It holds an <see cref="ITutorialLauncher"/> and nothing else. That is the
    /// same rule <c>INebulaSessionLauncher</c> exists for on the main menu -- a
    /// screen that could reach the bootstrap is a screen that can create a world by
    /// being drawn -- and it is load-bearing twice over here, because the tutorial
    /// must never mint a campaign id or put a save lineage behind itself. There is
    /// no path from this screen to <c>BeginSession</c>.
    /// </para>
    /// </summary>
    public sealed class TutorialModeScreen : MonoBehaviour, INebulaUiScreen
    {
        public string ScreenId => "tutorial.mode";

        private ITutorialLauncher launcher;
        private NebulaUiShell shell;
        private RectTransform root;

        private NebulaUiFactory.ButtonHandle fromBeginning;
        private NebulaUiFactory.ButtonHandle chapters;
        private NebulaUiFactory.ButtonHandle back;
        private NebulaUiFactory.LabelHandle blocked;

        /// <summary>Raised when the player asks for the step list, which this screen does not own.</summary>
        public event Action ChaptersRequested;

        /// <summary>
        /// Raised once a run has actually been asked for, so whoever owns the menu
        /// shell can stand it down. Raised last, after the launcher has been called:
        /// the handler destroys the shell this screen lives on.
        /// </summary>
        public event Action RunStarted;

        /// <summary>Test-facing: how many times BAŞTAN BAŞLA reached the launcher.</summary>
        public int SequentialStarts { get; private set; }

        /// <summary>Test-facing: how many times BÖLÜMLER was asked for.</summary>
        public int ChapterOpens { get; private set; }

        /// <summary>Test-facing: why nothing can be started, or <c>null</c> when it can.</summary>
        public string BlockedReason { get; private set; }

        public static TutorialModeScreen Create(NebulaUiShell shell, ITutorialLauncher launcher)
        {
            var go = new GameObject("Tutorial Mode Screen");
            var screen = go.AddComponent<TutorialModeScreen>();
            screen.launcher = launcher;
            screen.Build(shell);
            return screen;
        }

        private void Build(NebulaUiShell owningShell)
        {
            shell = owningShell;

            root = NebulaUiFactory.Group(shell.Root(UiLayer.Menu), "Tutorial Mode Root");
            NebulaUiFactory.Fill(root);
            transform.SetParent(shell.Root(UiLayer.Menu), false);

            Image backdrop = NebulaUiFactory.Rect(root, "Backdrop", NebulaUiDesignTokens.SpaceVoid);
            NebulaUiFactory.Fill(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            NebulaSessionChrome.Header(root, TutorialUiCopy.ModeTitle);

            // Placed with a real size, like everything else on this screen. An
            // unplaced label keeps the factory's 200x18 default, and this project
            // has shipped a clipped caption twice from exactly that.
            NebulaUiFactory.Place(
                NebulaUiFactory.Label(root, "Subtitle", TutorialUiCopy.ModeSubtitle,
                    NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.InkSecondary,
                    TextAnchor.UpperLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingBody).Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(88f, -104f), new Vector2(900f, 22f));

            var column = NebulaUiFactory.Group(root, "Mode Column");
            NebulaUiFactory.Place(column, new Vector2(0f, 1f), new Vector2(88f, -156f),
                new Vector2(560f, 260f));

            fromBeginning = Entry(column, TutorialUiCopy.FromBeginningName, TutorialUiCopy.FromBeginning,
                0f, NebulaUiAssetCatalog.ButtonKind.Primary, () => StartFromBeginning());
            chapters = Entry(column, TutorialUiCopy.ChaptersName, TutorialUiCopy.Chapters,
                -68f, NebulaUiAssetCatalog.ButtonKind.Secondary, () => OpenChapters());
            back = Entry(column, TutorialUiCopy.BackName, TutorialUiCopy.Back,
                -136f, NebulaUiAssetCatalog.ButtonKind.Tertiary, () => GoBack());

            blocked = NebulaUiFactory.Label(root, "Blocked", string.Empty,
                NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.Status(Visuals.UiStatusLevel.Caution),
                TextAnchor.UpperLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption, wrap: true);
            NebulaUiFactory.Place(blocked.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(88f, -348f), new Vector2(900f, 40f));

            NebulaUiFactory.Place(
                NebulaUiFactory.Label(root, "Prompt", TutorialKeyPrompts.Key(KeyCode.Escape, TutorialUiCopy.Back),
                    NebulaUiDesignTokens.SizeKeyPrompt, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.MiddleRight, FontStyle.Normal,
                    NebulaUiDesignTokens.TrackingCaption).Text.rectTransform,
                new Vector2(1f, 0f), new Vector2(-88f, 36f), new Vector2(320f, 16f));

            Refresh();
        }

        private NebulaUiFactory.ButtonHandle Entry(RectTransform parent, string name, string caption,
            float y, NebulaUiAssetCatalog.ButtonKind kind, UnityEngine.Events.UnityAction onClick)
        {
            NebulaUiFactory.ButtonHandle handle =
                NebulaUiFactory.ButtonControl(parent, name, caption, kind, onClick);
            NebulaUiFactory.Place(handle.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(520f, 56f));
            handle.Label.Text.alignment = TMPro.TextAlignmentOptions.Left;
            handle.Label.Text.rectTransform.offsetMin = new Vector2(20f, 0f);
            return handle;
        }

        /// <summary>
        /// Re-reads whether a run can be asked for at all. A missing launcher is a
        /// wiring failure rather than a gate, and it is said out loud on the two
        /// controls it actually stops -- GERİ keeps working either way, so the
        /// player is never trapped on this screen.
        /// </summary>
        public void Refresh()
        {
            bool ready = launcher != null;
            BlockedReason = ready ? null : TutorialUiCopy.NoLauncher;

            fromBeginning.SetAvailable(ready, BlockedReason);
            chapters.SetAvailable(true);
            back.SetAvailable(true);

            blocked.Value = BlockedReason ?? string.Empty;
            blocked.Text.gameObject.SetActive(!ready);
        }

        // ---- actions ---------------------------------------------------------

        /// <summary>
        /// Test-facing: what BAŞTAN BAŞLA does. Returns false when there was nothing
        /// to start, so a fixture can tell a refused press from a silent one.
        /// </summary>
        internal bool StartFromBeginning()
        {
            if (launcher == null) { Refresh(); return false; }

            SequentialStarts++;

            // The screen leaves the stack before the run begins, exactly as the save
            // slot screen does: a still-open screen holds the pause the run is about
            // to need, and this shell is about to be torn down anyway.
            shell?.PopAll();
            launcher.StartFromBeginning();
            RunStarted?.Invoke();
            return true;
        }

        /// <summary>Test-facing: what BÖLÜMLER does.</summary>
        internal bool OpenChapters()
        {
            ChapterOpens++;
            ChaptersRequested?.Invoke();
            return true;
        }

        /// <summary>Test-facing: what GERİ does.</summary>
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
            Refresh();
            FocusFirst();
        }

        public void OnClosed() => root.gameObject.SetActive(false);

        public void OnCovered() { }

        public void OnRevealed()
        {
            Refresh();
            FocusFirst();
        }

        /// <summary>
        /// Puts keyboard focus somewhere, so a player driving from the keyboard has
        /// a place to start rather than having to click once first.
        /// </summary>
        internal void FocusFirst()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;

            Button target = fromBeginning != null && fromBeginning.Button.interactable
                ? fromBeginning.Button
                : chapters?.Button;
            if (target != null) events.SetSelectedGameObject(target.gameObject);
        }

        /// <summary>No nested state, so Back falls through and the shell pops this screen.</summary>
        public bool HandleBack() => false;
    }
}
