using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Shell;

namespace Nebula.UI.Menu
{
    /// <summary>
    /// One page of the parent command menu. A page owns its content and its
    /// refresh; it never pauses, never touches the cursor, and never opens a
    /// second modal -- the shell does all three for the whole stack.
    /// </summary>
    public interface INebulaMenuPage
    {
        NebulaUiAssetCatalog.Page Page { get; }

        /// <summary>Rail label and header title. Player-facing, Turkish.</summary>
        string Title { get; }

        /// <summary>Builds content into the supplied host. Called once.</summary>
        void Build(RectTransform host);

        /// <summary>Re-reads gameplay state. Called on every reveal and on a timer while visible.</summary>
        void Refresh();

        /// <summary>Consume Back for internal state (an open confirmation, an expanded row).</summary>
        bool HandleBack();

        /// <summary>Count for the rail badge, or 0 for none. Attention, not decoration.</summary>
        int AttentionCount { get; }
    }

    /// <summary>
    /// A page that has controls a keyboard or a gamepad needs to stand on.
    /// <para>
    /// Unity's <c>StandaloneInputModule</c> walks Selectables outward from whatever
    /// is currently selected, and with nothing selected the arrow keys and the stick
    /// reach nothing at all. The shell selects nothing on its own — that is right for
    /// a page that is only a readout, and wrong for one whose whole purpose is to be
    /// operated. Implementing this is a page saying which of the two it is.
    /// </para>
    /// </summary>
    public interface INebulaFocusablePage
    {
        /// <summary>Puts keyboard/gamepad focus on this page's primary control.</summary>
        void FocusFirstControl();
    }

    /// <summary>
    /// The shell every management page lives in: navigation rail, header,
    /// content, action rail.
    /// <para>
    /// This replaces four independent full-screen modals that each opened on
    /// their own key, each paused the game their own way, and each drew their
    /// own chrome. It introduces <b>no new keybinding</b>: the three keys that
    /// already opened a management screen -- U, Y, M -- now open this menu on
    /// the matching page. Escape keeps meaning back/cancel, because it already
    /// means five different cancels elsewhere and cannot be claimed.
    /// </para>
    /// </summary>
    public sealed class NebulaParentMenuScreen : MonoBehaviour, INebulaUiScreen
    {
        public string ScreenId => "menu.parent";

        // No new global binding is introduced. These two already opened a
        // management screen before the redesign; they now open this one.
        //
        // M is deliberately absent. StrategicMapScreen owns it, and the older
        // NavigationScreen had already yielded it so that the key would have a
        // single owner -- binding it here would have quietly taken it back and
        // made one press do two things. The Map page is reached from the rail
        // and the arrow keys until UI-03 embeds the map into the page itself.
        // Each key still lands where it always landed: U opened the progression
        // screen and Y opened the hangar. Pointing U at Overview instead would
        // have cost every existing player a second click for no reason.
        //
        // Read from the action catalog rather than declared here. U is claimed by
        // two owners — this menu, globally, and the Quick Asset Package's cancel,
        // only while a package is actually in flight — and a key with two claimants
        // needs a place that can see both. The catalog is that place; this stays the
        // global intent, which is what it has always been.
        public static readonly KeyCode ProgressionKey = NebulaInputActions.Key(NebulaInputActions.MenuProgression);
        public static readonly KeyCode FleetKey = NebulaInputActions.Key(NebulaInputActions.MenuFleet);

        /// <summary>
        /// Where an opening key lands. One table, so the binding and the
        /// destination cannot drift apart and a test can assert both.
        /// </summary>
        public static NebulaUiAssetCatalog.Page PageForKey(KeyCode key)
        {
            if (key == ProgressionKey) return NebulaUiAssetCatalog.Page.Progression;
            if (key == FleetKey) return NebulaUiAssetCatalog.Page.Fleet;
            return NebulaUiAssetCatalog.Page.Overview;
        }

        private NebulaUiShell shell;
        private RectTransform root;
        private RectTransform contentHost;
        private RectTransform railItems;
        private NebulaUiFactory.LabelHandle titleLabel;
        private NebulaUiFactory.LabelHandle breadcrumbLabel;

        private readonly List<INebulaMenuPage> pages = new List<INebulaMenuPage>();
        private readonly Dictionary<NebulaUiAssetCatalog.Page, RectTransform> pageHosts =
            new Dictionary<NebulaUiAssetCatalog.Page, RectTransform>();
        private readonly Dictionary<NebulaUiAssetCatalog.Page, Image> railHighlights =
            new Dictionary<NebulaUiAssetCatalog.Page, Image>();
        private readonly Dictionary<NebulaUiAssetCatalog.Page, Button> railButtons =
            new Dictionary<NebulaUiAssetCatalog.Page, Button>();
        private readonly Dictionary<NebulaUiAssetCatalog.Page, NebulaUiFactory.LabelHandle> railBadges =
            new Dictionary<NebulaUiAssetCatalog.Page, NebulaUiFactory.LabelHandle>();

        private INebulaMenuPage current;
        private float refreshTimer;

        /// <summary>Test-facing: the page currently shown, or <c>null</c> when the menu is closed.</summary>
        public NebulaUiAssetCatalog.Page? CurrentPage => current?.Page;

        /// <summary>True while the menu is on screen.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Test-facing: how many pages are registered.</summary>
        public int PageCount => pages.Count;

        /// <summary>
        /// The registered page of a given type, or <c>null</c>. The menu already owns
        /// every page, so reaching one through it beats threading each new page
        /// through the presentation handle's constructor one parameter at a time.
        /// </summary>
        public T PageOf<T>() where T : class, INebulaMenuPage
        {
            for (int i = 0; i < pages.Count; i++)
                if (pages[i] is T match) return match;
            return null;
        }

        public static NebulaParentMenuScreen Create(NebulaUiShell shell)
        {
            var go = new GameObject("Parent Command Menu");
            var screen = go.AddComponent<NebulaParentMenuScreen>();
            screen.Build(shell);
            return screen;
        }

        private void Build(NebulaUiShell owningShell)
        {
            shell = owningShell;

            root = NebulaUiFactory.Group(shell.Root(UiLayer.Menu), "Parent Menu Root");
            NebulaUiFactory.Fill(root);
            // On the layer root, not inside the panel. A closed screen hides its
            // root, and a controller parented into that root stops updating with
            // it -- which for this screen means the key that opens it is never
            // read, and the screen can never be opened again.
            transform.SetParent(shell.Root(UiLayer.Menu), false);

            Image scrim = NebulaUiFactory.Rect(root, "Scrim", NebulaUiDesignTokens.OverlayDim);
            NebulaUiFactory.Fill(scrim.rectTransform);
            scrim.raycastTarget = true;

            BuildRail();
            BuildHeader();
            BuildContentHost();
            BuildActionRail();

            root.gameObject.SetActive(false);
        }

        private void BuildRail()
        {
            Image rail = NebulaUiFactory.Panel(root, "Rail", NebulaUiAssetCatalog.PanelPrimary);
            RectTransform rect = rail.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(236f, 0f);

            railItems = NebulaUiFactory.Group(rect, "Items");
            railItems.anchorMin = new Vector2(0f, 1f);
            railItems.anchorMax = new Vector2(1f, 1f);
            railItems.pivot = new Vector2(0.5f, 1f);
            railItems.anchoredPosition = new Vector2(0f, -92f);
            railItems.sizeDelta = new Vector2(0f, 480f);
        }

        private void BuildHeader()
        {
            var header = NebulaUiFactory.Group(root, "Header");
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(236f, -78f);
            header.offsetMax = new Vector2(0f, 0f);

            breadcrumbLabel = NebulaUiFactory.Label(header, "Breadcrumb", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.UpperLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(breadcrumbLabel.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(32f, -18f), new Vector2(600f, 16f));

            titleLabel = NebulaUiFactory.Label(header, "Title", "", NebulaUiDesignTokens.SizeScreenTitle,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.UpperLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingScreenTitle);
            NebulaUiFactory.Place(titleLabel.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(32f, -38f), new Vector2(700f, 30f));

            Image divider = NebulaUiFactory.Rect(header, "Divider", NebulaUiDesignTokens.Divider);
            divider.rectTransform.anchorMin = new Vector2(0f, 0f);
            divider.rectTransform.anchorMax = new Vector2(1f, 0f);
            divider.rectTransform.pivot = new Vector2(0.5f, 0f);
            divider.rectTransform.offsetMin = Vector2.zero;
            divider.rectTransform.offsetMax = new Vector2(0f, 1f);
        }

        private void BuildContentHost()
        {
            contentHost = NebulaUiFactory.Group(root, "Content");
            contentHost.anchorMin = Vector2.zero;
            contentHost.anchorMax = Vector2.one;
            contentHost.offsetMin = new Vector2(236f, 62f);
            contentHost.offsetMax = new Vector2(0f, -78f);
        }

        private void BuildActionRail()
        {
            Image bar = NebulaUiFactory.Panel(root, "Action Rail", NebulaUiAssetCatalog.PanelSecondary);
            RectTransform rect = bar.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(236f, 0f);
            rect.offsetMax = new Vector2(0f, 62f);

            // Stretched across the rail, not left at its default centred rect --
            // MiddleRight alignment does nothing if the rect is a small box in the
            // middle, which is exactly how this first rendered.
            // Read off the catalog, not typed here. This line said "←→  SAYFA" long
            // after page stepping had moved to PageUp/PageDown: the arrows belong to
            // EventSystem navigation inside the page now, so the prompt was telling the
            // player to press a key that does nothing, which is worse than saying
            // nothing at all. A prompt naming a key it does not own drifts the moment
            // the row moves; this one cannot.
            NebulaUiFactory.LabelHandle prompts = NebulaUiFactory.Label(rect, "Prompts",
                "ESC  GERİ / KAPAT      ↑↓  GEZ      " + PageStepPrompt(),
                NebulaUiDesignTokens.SizeKeyPrompt, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleRight, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            RectTransform promptRect = prompts.Text.rectTransform;
            promptRect.anchorMin = Vector2.zero;
            promptRect.anchorMax = Vector2.one;
            promptRect.offsetMin = new Vector2(32f, 0f);
            promptRect.offsetMax = new Vector2(-32f, 0f);
        }

        // =====================================================================
        // PAGES
        // =====================================================================

        /// <summary>
        /// Registers a page and builds its rail entry. Order of registration is the
        /// rail's order, which is also the order the arrow keys walk.
        /// </summary>
        public void AddPage(INebulaMenuPage page)
        {
            if (page == null || pageHosts.ContainsKey(page.Page)) return;

            var host = NebulaUiFactory.Group(contentHost, page.Page + " Page");
            NebulaUiFactory.Fill(host);
            host.gameObject.SetActive(false);
            pageHosts[page.Page] = host;

            pages.Add(page);
            page.Build(host);
            BuildRailEntry(page, pages.Count - 1);
        }

        private void BuildRailEntry(INebulaMenuPage page, int index)
        {
            var entry = NebulaUiFactory.Group(railItems, page.Page + " Rail");
            NebulaUiFactory.Place(entry, new Vector2(0f, 1f), new Vector2(0f, -index * 44f),
                new Vector2(236f, 44f));

            Image highlight = NebulaUiFactory.Rect(entry, "Highlight",
                NebulaUiDesignTokens.SelectedFill);
            NebulaUiFactory.Fill(highlight.rectTransform);
            highlight.enabled = false;
            railHighlights[page.Page] = highlight;

            Image glyph = NebulaUiFactory.Icon(entry, "Glyph",
                NebulaUiAssetCatalog.PageGlyph(page.Page), 20f);
            NebulaUiFactory.Place(glyph.rectTransform, new Vector2(0f, 0.5f), new Vector2(22f, 0f),
                new Vector2(20f, 20f));
            glyph.color = NebulaUiDesignTokens.InkSecondary;

            NebulaUiFactory.LabelHandle label = NebulaUiFactory.Label(entry, "Label", page.Title,
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(label.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(55f, 0f), new Vector2(140f, 20f));

            NebulaUiFactory.LabelHandle badge = NebulaUiFactory.Label(entry, "Badge", "",
                NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(Visuals.UiStatusLevel.Warning),
                TextAnchor.MiddleRight, FontStyle.Bold);
            NebulaUiFactory.Place(badge.Text.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-18f, 0f), new Vector2(40f, 20f));
            badge.Text.gameObject.SetActive(false);
            railBadges[page.Page] = badge;

            // The whole row is the hit target, not just the label -- a 20px glyph
            // is below the minimum and a label-only target reads as broken.
            var button = entry.gameObject.AddComponent<Button>();
            railButtons[page.Page] = button;

            // This used to be an alpha-zero Image on Button's default ColorTint
            // transition, which is the same as having no transition at all:
            // ColorTint multiplies the target graphic's colour, and any multiple
            // of zero alpha is still invisible. Ten navigation tabs -- the primary
            // way around the whole interface -- answered a hover, a press and a
            // keyboard focus with nothing whatsoever.
            //
            // The art for this was already drawn. menu.item.hover and
            // menu.item.selected have been in the catalog since the generator ran
            // and were reachable from nowhere in the codebase; menu.item.default
            // is fully transparent, so the resting tab looks exactly as it did.
            var stateBackground = NebulaUiFactory.Panel(entry, "State",
                NebulaUiAssetCatalog.MenuItem(NebulaUiAssetCatalog.MenuItemState.Default));
            NebulaUiFactory.Fill(stateBackground.rectTransform);
            stateBackground.raycastTarget = true;
            // First sibling, or the state wash would be painted over the glyph and
            // the label it is supposed to sit behind.
            stateBackground.rectTransform.SetAsFirstSibling();

            button.targetGraphic = stateBackground;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = NebulaUiAssetCatalog.MenuItem(NebulaUiAssetCatalog.MenuItemState.Hover),
                pressedSprite = NebulaUiAssetCatalog.MenuItem(NebulaUiAssetCatalog.MenuItemState.Selected),
                // Keyboard focus gets the focus ring rather than a fill, because a
                // focused tab and the open tab are different facts and the rail
                // already spends a fill on the second one.
                selectedSprite = NebulaUiAssetCatalog.FocusRing,
                disabledSprite = NebulaUiAssetCatalog.MenuItem(NebulaUiAssetCatalog.MenuItemState.Disabled),
            };

            NebulaUiAssetCatalog.Page captured = page.Page;
            button.onClick.AddListener(() => ShowPage(captured));
        }

        /// <summary>Switches the visible page. Idempotent.</summary>
        public bool ShowPage(NebulaUiAssetCatalog.Page page)
        {
            INebulaMenuPage target = Find(page);
            if (target == null) return false;
            if (current != null && current.Page == page) return true;

            if (current != null && pageHosts.TryGetValue(current.Page, out RectTransform previous))
                previous.gameObject.SetActive(false);

            current = target;
            pageHosts[page].gameObject.SetActive(true);
            SetRailSelection(page);

            titleLabel.Value = target.Title;
            breadcrumbLabel.Value = "KOMUTA MENÜSÜ  /  " + Design.NebulaText.Upper(target.Title);

            target.Refresh();
            // After the refresh, so focus lands on a control the page has already
            // painted its current state onto.
            if (target is INebulaFocusablePage focusable) focusable.FocusFirstControl();
            EstablishFocus(page);
            return true;
        }

        /// <summary>
        /// Guarantees the keyboard and the gamepad have somewhere to stand, and that
        /// wherever that is can actually be seen.
        /// <para>
        /// Two failures, not one. A focusable page can decline outright — every control
        /// it would focus may be disabled, which is exactly what the Combat page looks
        /// like while a production job runs. And the selection can be <i>stale</i>: the
        /// previous page's control is still what the EventSystem holds, but its object
        /// was deactivated one line above when that page was hidden, and Unity's
        /// navigation walks outwards from a selected object that no longer exists in
        /// the hierarchy — which lands nowhere just as surely as no selection at all.
        /// Both end at the rail row for the page now open, which is always present and
        /// always interactable.
        /// </para>
        /// <para>
        /// The scroll step is not cosmetic: a control focused below the fold is a
        /// control the player cannot see they have, and the first thing they do is
        /// press a direction to look for it — which moves the focus off the thing they
        /// were never shown.
        /// </para>
        /// </summary>
        private void EstablishFocus(NebulaUiAssetCatalog.Page page)
        {
            UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return;

            GameObject selected = events.currentSelectedGameObject;
            bool usable = selected != null && selected.activeInHierarchy
                          && selected.GetComponent<Selectable>() is Selectable s && s.IsInteractable();

            if (!usable && railButtons.TryGetValue(page, out Button rail))
            {
                events.SetSelectedGameObject(rail.gameObject);
                selected = rail.gameObject;
            }

            ScrollIntoView(selected);
        }

        /// <summary>
        /// Brings a focused control inside its own scroll view, if it has one. Walks up
        /// from the control rather than down from a remembered view, so a page that
        /// nests scroll views gets the innermost one — the only one that can actually
        /// move the control.
        /// </summary>
        private static void ScrollIntoView(GameObject selected)
        {
            if (selected == null) return;
            var target = selected.GetComponent<RectTransform>();
            if (target == null) return;

            ScrollRect view = selected.GetComponentInParent<ScrollRect>();
            if (view == null || view.content == null || view.viewport == null) return;

            Canvas.ForceUpdateCanvases();

            RectTransform content = view.content, viewport = view.viewport;
            float contentHeight = content.rect.height, viewportHeight = viewport.rect.height;
            if (contentHeight <= viewportHeight + 0.01f) return; // nothing to scroll

            // The control's position measured down from the top of the content, then
            // expressed as the normalised position Unity's own scrollbar uses.
            float offset = Mathf.Abs(content.InverseTransformPoint(target.position).y
                                     - content.InverseTransformPoint(content.position).y);
            float travel = contentHeight - viewportHeight;
            view.verticalNormalizedPosition = Mathf.Clamp01(1f - (offset - viewportHeight * 0.5f) / travel);
        }

        private void SetRailSelection(NebulaUiAssetCatalog.Page page)
        {
            foreach (KeyValuePair<NebulaUiAssetCatalog.Page, Image> entry in railHighlights)
                entry.Value.enabled = entry.Key == page;
        }

        private INebulaMenuPage Find(NebulaUiAssetCatalog.Page page)
        {
            for (int i = 0; i < pages.Count; i++)
                if (pages[i].Page == page) return pages[i];
            return null;
        }

        /// <summary>
        /// The key-prompt fragment for page stepping, named from the catalog rows that
        /// actually do it. Test-facing so the prompt and the binding can be checked
        /// against each other rather than against a string somebody remembered.
        /// </summary>
        public static string PageStepPrompt()
        {
            string previous = Controls.NebulaControlBindings.KeyLabel(
                NebulaInputActions.Key(NebulaInputActions.MenuPrevPage));
            string next = Controls.NebulaControlBindings.KeyLabel(
                NebulaInputActions.Key(NebulaInputActions.MenuNextPage));
            return $"{previous}/{next}  SAYFA";
        }

        /// <summary>Moves one page along the rail. Wraps, so the rail is a loop rather than a dead end.</summary>
        public void StepPage(int delta)
        {
            if (pages.Count == 0 || current == null) return;
            int index = pages.IndexOf(current);
            int next = ((index + delta) % pages.Count + pages.Count) % pages.Count;
            ShowPage(pages[next].Page);
        }

        // =====================================================================
        // OPEN / CLOSE
        // =====================================================================

        /// <summary>Opens the menu on a specific page. Returns false when a modal already owns input.</summary>
        public bool Open(NebulaUiAssetCatalog.Page page)
        {
            if (IsOpen)
            {
                ShowPage(page);
                return true;
            }

            if (!shell.Push(this)) return false;
            ShowPage(page);
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            shell.PopAll();
        }

        private void Update()
        {
            if (!IsOpen)
            {
                // Only listen while the HUD owns input. InputGate is raised by the
                // shell whenever any screen is open, so this cannot fire underneath
                // another modal.
                if (InputGate.Suppressed) return;

                // Through the router, not Input, because U has a second claimant. The
                // router hands the press to the highest-ranked context that is
                // actually live: while a Quick Asset Package is in flight that is the
                // package's cancel, and every other moment — which is nearly all of
                // them — it is this. Y has one owner and needs no arbitration.
                if (NebulaInputRouter.Pressed(NebulaInputActions.MenuProgression))
                    Open(PageForKey(ProgressionKey));
                else if (NebulaInputRouter.Pressed(NebulaInputActions.MenuFleet)) Open(PageForKey(FleetKey));
                return;
            }

            // All three through the router, so a rebind reaches them and a test can
            // drive them. Escape uses the shared-intent read: it stays unarbitrated
            // because every surface answers it for its own thing.
            if (NebulaInputRouter.SharedPressed(NebulaInputActions.MenuBack)) shell.Back();
            // Arrow keys belong to EventSystem navigation inside the active page.
            // PageUp/PageDown change pages without also moving a focused control.
            else if (NebulaInputRouter.Pressed(NebulaInputActions.MenuNextPage)) StepPage(1);
            else if (NebulaInputRouter.Pressed(NebulaInputActions.MenuPrevPage)) StepPage(-1);

            // Unscaled: the menu pauses the game, so a scaled timer would freeze
            // with it and every reading on screen would go stale.
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = 0.25f;
                current?.Refresh();
                RefreshRailBadges();
            }
        }

        private void RefreshRailBadges()
        {
            for (int i = 0; i < pages.Count; i++)
            {
                INebulaMenuPage page = pages[i];
                if (!railBadges.TryGetValue(page.Page, out NebulaUiFactory.LabelHandle badge)) continue;

                int count = page.AttentionCount;
                bool show = count > 0;
                if (badge.Text.gameObject.activeSelf != show) badge.Text.gameObject.SetActive(show);
                if (show) badge.Value = count.ToString();
            }
        }

        // ---- INebulaUiScreen -------------------------------------------------

        /// <summary>
        /// What was focused before this menu took the screen, so closing can give it
        /// back. Null whenever the menu was opened from gameplay, which is the usual
        /// case and the reason closing normally clears the selection outright.
        /// </summary>
        private GameObject focusBeforeOpen;

        public void OnOpened(NebulaUiShell owningShell)
        {
            shell = owningShell;
            IsOpen = true;
            UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
            focusBeforeOpen = events != null ? events.currentSelectedGameObject : null;
            root.gameObject.SetActive(true);
            refreshTimer = 0f;
        }

        public void OnClosed()
        {
            IsOpen = false;
            root.gameObject.SetActive(false);

            // Hand focus back, or clear it. Leaving it on one of this menu's controls
            // would leave the EventSystem holding an object that was just deactivated:
            // the next screen to open would find a "selection" that cannot be navigated
            // from and would have to guess whether to replace it.
            UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return;
            bool restorable = focusBeforeOpen != null && focusBeforeOpen.activeInHierarchy;
            events.SetSelectedGameObject(restorable ? focusBeforeOpen : null);
            focusBeforeOpen = null;
        }

        public void OnCovered() { }

        public void OnRevealed() => current?.Refresh();

        /// <summary>
        /// Gives the open page first refusal, so a confirmation inside a page
        /// closes itself rather than the whole menu.
        /// </summary>
        public bool HandleBack() => current != null && current.HandleBack();
    }
}
