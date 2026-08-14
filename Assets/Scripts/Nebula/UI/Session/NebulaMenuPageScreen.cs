using UnityEngine;
using UnityEngine.UI;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;

namespace Nebula.UI.Session
{
    /// <summary>
    /// A standalone screen wrapped around one <see cref="INebulaMenuPage"/>.
    /// <para>
    /// Inside a campaign a page lives in <see cref="NebulaParentMenuScreen"/>'s rail.
    /// Before one exists there is no rail — and the settings the player wants at the
    /// front door are exactly the settings that must be changeable without starting
    /// a campaign to reach them. Rather than build a second settings authority for
    /// the menu, this hosts the real page: same class, same rows, same writes, one
    /// chrome around it.
    /// </para>
    /// <para>
    /// Back is given to the page first, so a page with internal state (an open
    /// picker, a key waiting to be captured) unwinds that before the screen closes.
    /// </para>
    /// </summary>
    public sealed class NebulaMenuPageScreen : MonoBehaviour, INebulaUiScreen
    {
        public string ScreenId => "session.page." + (page != null ? page.Page.ToString() : "none")
            + (string.IsNullOrEmpty(idSuffix) ? string.Empty : "." + idSuffix);

        private INebulaMenuPage page;
        private string idSuffix;
        private string breadcrumbRoot = "ANA MENÜ";
        private NebulaUiShell shell;
        private RectTransform root;

        /// <summary>Test-facing: the page this screen is showing.</summary>
        public INebulaMenuPage Page => page;

        /// <summary>
        /// <paramref name="idSuffix"/> distinguishes two screens built from the same
        /// page kind. The accessibility front door and the full settings page are
        /// both <c>Page.Settings</c>, and a shared <see cref="ScreenId"/> would make
        /// them one screen to anything that identifies a screen by its id.
        /// </summary>
        /// <param name="breadcrumbRoot">
        /// Where the player came from, for the breadcrumb. The default is the front
        /// door; a screen pushed from inside a campaign says so instead, because a
        /// breadcrumb that reads "ANA MENÜ" over a running session is telling the
        /// player they are somewhere they are not.
        /// </param>
        public static NebulaMenuPageScreen Create(NebulaUiShell shell, INebulaMenuPage page,
            string idSuffix = null, string breadcrumbRoot = "ANA MENÜ")
        {
            var go = new GameObject("Menu Page Screen — " + (page != null ? page.Page.ToString() : "none")
                + (string.IsNullOrEmpty(idSuffix) ? string.Empty : " (" + idSuffix + ")"));
            var screen = go.AddComponent<NebulaMenuPageScreen>();
            screen.page = page;
            screen.idSuffix = idSuffix;
            screen.breadcrumbRoot = string.IsNullOrEmpty(breadcrumbRoot) ? "ANA MENÜ" : breadcrumbRoot;
            screen.Build(shell);
            return screen;
        }

        private void Build(NebulaUiShell owningShell)
        {
            shell = owningShell;

            root = NebulaUiFactory.Group(shell.Root(UiLayer.Menu), "Page Screen Root");
            NebulaUiFactory.Fill(root);
            transform.SetParent(shell.Root(UiLayer.Menu), false);

            // Opaque, not a scrim. There is no gameplay behind the front door, and a
            // translucent panel over the wordmark reads as an unfinished overlay.
            Image backdrop = NebulaUiFactory.Rect(root, "Backdrop", NebulaUiDesignTokens.SpaceVoid);
            NebulaUiFactory.Fill(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            NebulaSessionChrome.Header(root, page != null ? NebulaText.Upper(page.Title) : "—", breadcrumbRoot);
            NebulaSessionChrome.BackButton(root, () => shell.Back());

            RectTransform host = NebulaUiFactory.Group(root, "Page Host");
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.one;
            host.offsetMin = new Vector2(0f, 62f);
            host.offsetMax = new Vector2(0f, -78f);

            page?.Build(host);
            page?.Refresh();
        }

        // ---- INebulaUiScreen -------------------------------------------------

        public void OnOpened(NebulaUiShell owningShell)
        {
            shell = owningShell;
            root.gameObject.SetActive(true);
            page?.Refresh();
        }

        public void OnClosed() => root.gameObject.SetActive(false);

        public void OnCovered() { }

        public void OnRevealed() => page?.Refresh();

        /// <summary>The page gets Back first; only then does the shell pop this screen.</summary>
        public bool HandleBack() => page != null && page.HandleBack();
    }

    /// <summary>
    /// The header, rule and Back control every front-door screen shares.
    /// <para>
    /// Three screens hang off the main menu and each of them needs the same three
    /// pieces of chrome. One helper rather than three copies is what keeps the Back
    /// control in the same place on all of them — a Back that moves between screens
    /// is the small thing that makes a menu feel assembled rather than designed.
    /// </para>
    /// </summary>
    internal static class NebulaSessionChrome
    {
        /// <summary>The name every front-door Back control carries, so a test can find it.</summary>
        public const string BackControlName = "Back";

        public static NebulaUiFactory.LabelHandle Header(RectTransform root, string title,
            string breadcrumbRoot = "ANA MENÜ")
        {
            var header = NebulaUiFactory.Group(root, "Header");
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(0f, -78f);
            header.offsetMax = Vector2.zero;

            NebulaUiFactory.LabelHandle breadcrumb = NebulaUiFactory.Label(header, "Breadcrumb",
                breadcrumbRoot + "  /  " + title, NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.UpperLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(breadcrumb.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(88f, -18f), new Vector2(700f, 16f));

            NebulaUiFactory.LabelHandle heading = NebulaUiFactory.Label(header, "Title", title,
                NebulaUiDesignTokens.SizeScreenTitle, NebulaUiDesignTokens.InkPrimary,
                TextAnchor.UpperLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingScreenTitle);
            NebulaUiFactory.Place(heading.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(88f, -38f), new Vector2(900f, 30f));

            Image divider = NebulaUiFactory.Rect(header, "Divider", NebulaUiDesignTokens.Divider);
            divider.rectTransform.anchorMin = new Vector2(0f, 0f);
            divider.rectTransform.anchorMax = new Vector2(1f, 0f);
            divider.rectTransform.pivot = new Vector2(0.5f, 0f);
            divider.rectTransform.offsetMin = Vector2.zero;
            divider.rectTransform.offsetMax = new Vector2(0f, 1f);

            return heading;
        }

        public static NebulaUiFactory.ButtonHandle BackButton(RectTransform root,
            UnityEngine.Events.UnityAction onClick)
        {
            NebulaUiFactory.ButtonHandle handle = NebulaUiFactory.ButtonControl(root,
                BackControlName, "GERİ", NebulaUiAssetCatalog.ButtonKind.Secondary, onClick);
            NebulaUiFactory.Place(handle.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(88f, 14f), new Vector2(200f, 44f));

            NebulaUiFactory.LabelHandle prompt = NebulaUiFactory.Label(root, "Prompt",
                "ESC  GERİ", NebulaUiDesignTokens.SizeKeyPrompt, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleRight, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(prompt.Text.rectTransform, new Vector2(1f, 0f),
                new Vector2(-88f, 36f), new Vector2(300f, 16f));

            return handle;
        }
    }
}
