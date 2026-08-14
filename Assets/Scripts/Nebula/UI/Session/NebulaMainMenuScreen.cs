using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Nebula.UI.Design;
using Nebula.UI.Shell;

namespace Nebula.UI.Session
{
    /// <summary>
    /// What the main menu is allowed to do to the game. Kept as an interface so the
    /// menu can be exercised without booting a universe -- and so the menu itself
    /// never holds a <c>NebulaBootstrap</c>, which is what would let it create
    /// gameplay state just by being drawn.
    /// </summary>
    public interface INebulaSessionLauncher
    {
        /// <summary>Starts a fresh campaign. The slot is chosen at first save.</summary>
        void StartNewCampaign();

        /// <summary>Starts a session and restores the given slot.</summary>
        void ContinueFromSlot(int slot);

        /// <summary>Leaves the application.</summary>
        void QuitGame();

        /// <summary>
        /// Tears the running campaign down and returns to the main menu.
        /// <para>
        /// On the interface because the session screen is the only surface that can
        /// offer it, and it had no way to ask: the concrete launcher has had this
        /// method all along, while the abstraction the screen holds did not expose
        /// it, so leaving a campaign was unreachable from inside one.
        /// </para>
        /// </summary>
        void ReturnToMainMenu();

        /// <summary>
        /// Ends the running campaign and starts a fresh one.
        /// <para>
        /// Distinct from <see cref="StartNewCampaign"/>, and it has to be:
        /// that one routes to <c>BeginSession</c>, which is idempotent by design so a
        /// double-pressed menu button cannot cost a player their campaign. In-session
        /// "SEFERİ YENİDEN BAŞLAT" therefore did nothing at all while reporting
        /// success -- and still stamped a new campaign id, leaving the old universe
        /// running under a new identity with a broken save and telemetry lineage.
        /// </para>
        /// </summary>
        void RestartCampaign();
    }

    /// <summary>
    /// The first screen the player sees.
    /// <para>
    /// Continue is the strongest action and carries a live summary of the save it
    /// would resume, because "Continue" with nothing behind it is the single most
    /// common way a main menu lies. With no playable save it is disabled <b>and
    /// says why</b>.
    /// </para>
    /// <para>
    /// The menu reads saves and nothing else. It never starts a session to find out
    /// what is in one, never adopts a campaign identity, and never writes -- see
    /// <see cref="NebulaSaveSlotQuery"/>.
    /// </para>
    /// </summary>
    public sealed class NebulaMainMenuScreen : MonoBehaviour, INebulaUiScreen
    {
        public string ScreenId => "session.main-menu";

        private INebulaSessionLauncher launcher;
        private NebulaUiShell shell;
        private RectTransform root;

        private NebulaUiFactory.ButtonHandle continueButton;
        private NebulaUiFactory.ButtonHandle newCampaignButton;
        private NebulaUiFactory.LabelHandle continueDetail;
        private NebulaUiFactory.LabelHandle continueBlocked;
        private NebulaUiFactory.LabelHandle migrationNotice;
        private RectTransform footerFrame;

        /// <summary>Footer height with only the build line on it.</summary>
        private const float FooterHeightPlain = 44f;

        /// <summary>Footer height while the one-off migration notice is shown above the build line.</summary>
        private const float FooterHeightWithNotice = 76f;
        private RectTransform summaryCard;

        private Brand.NebulaBrandKeyArtView keyArt;
        private RectTransform wordmark;

        private RectTransform menuFrame;
        private RectTransform menuColumn;
        private ScrollRect menuScroller;

        /// <summary>Test-facing: the window the entries scroll inside.</summary>
        public RectTransform MenuViewport => menuFrame;

        /// <summary>Test-facing: the sheet the entries are laid out on.</summary>
        public RectTransform MenuColumn => menuColumn;

        /// <summary>
        /// Test-facing: whether the entries currently need scrolling to be all seen.
        /// False on any viewport tall enough to show the whole column, which is every
        /// supported resolution at 100% interface scale.
        /// </summary>
        public bool MenuScrolls => menuFrame != null && menuFrame.rect.height < ColumnContentHeight - 0.5f;

        /// <summary>Test-facing: the key-art background, once the screen has been built.</summary>
        public Brand.NebulaBrandKeyArtView KeyArt => keyArt;

        /// <summary>
        /// Test-facing: whether the drawn-text title is on screen.
        /// <para>
        /// It must never be true at the same time as <see cref="KeyArt"/> has art.
        /// The plate carries the real wordmark at the top left — exactly where this
        /// block sits — so showing both would print "NEBULA" twice, once in the
        /// authored logo and once in a 76pt label straddling it.
        /// </para>
        /// </summary>
        public bool TextWordmarkVisible => wordmark != null && wordmark.gameObject.activeSelf;

        private SaveSlotSummary resumeTarget;

        /// <summary>Test-facing: the slot Continue would resume, or -1 when it is unavailable.</summary>
        public int ResumeSlot => resumeTarget != null && resumeTarget.CanContinue ? resumeTarget.Slot : -1;

        /// <summary>Test-facing: why Continue is unavailable, or <c>null</c> when it is available.</summary>
        public string ContinueBlockedReason { get; private set; }

        /// <summary>Raised when the player asks for a screen this menu does not own.</summary>
        public event Action SettingsRequested;
        public event Action AccessibilityRequested;
        public event Action LoadRequested;
        public event Action CreditsRequested;

        /// <summary>
        /// Raised when the player picks EĞİTİM.
        /// <para>
        /// An event and not a launcher call, and that distinction is the whole point:
        /// the tutorial is not a campaign. It mints no campaign id, writes no save
        /// and starts no session, so this path must never reach
        /// <see cref="INebulaSessionLauncher"/> — the menu does not know how a
        /// tutorial run begins and is not allowed to find out.
        /// </para>
        /// </summary>
        public event Action TutorialRequested;

        private NebulaUiFactory.ButtonHandle tutorialButton;

        public static NebulaMainMenuScreen Create(NebulaUiShell shell, INebulaSessionLauncher launcher)
        {
            var go = new GameObject("Main Menu");
            var screen = go.AddComponent<NebulaMainMenuScreen>();
            screen.launcher = launcher;
            screen.Build(shell);
            return screen;
        }

        private void Build(NebulaUiShell owningShell)
        {
            shell = owningShell;

            root = NebulaUiFactory.Group(shell.Root(UiLayer.Menu), "Main Menu Root");
            NebulaUiFactory.Fill(root);
            // On the layer root, not inside the panel. A closed screen hides its
            // root, and a controller parented into that root stops updating with
            // it -- which for this screen means the key that opens it is never
            // read, and the screen can never be opened again.
            transform.SetParent(shell.Root(UiLayer.Menu), false);

            // Backdrop. Opaque, because the main menu is not layered over gameplay --
            // there is no session running behind it. It stays under the key art
            // rather than being replaced by it: it is what the player sees if the
            // brand assets are missing, and what fills the frame for the instant
            // before the plate is bound.
            Image backdrop = NebulaUiFactory.Rect(root, "Backdrop", NebulaUiDesignTokens.SpaceVoid);
            NebulaUiFactory.Fill(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            keyArt = Brand.NebulaBrandKeyArtView.Create(root);

            // The backdrop is pushed under the plate, rather than the plate being
            // lifted over the backdrop. They look like the same instruction and are
            // not: the view builds a plate *and* a wordmark above it, and moving the
            // plate up past the backdrop slides the wordmark down under both of them.
            // That is how this menu ran with an authored logo that was bound, enabled,
            // correctly placed, and painted over before it reached the screen.
            backdrop.rectTransform.SetAsFirstSibling();

            BuildWordmark();
            BuildMenu();
            BuildFooter();

            ApplyBrandPresentation();
            SyncViewport();
            Refresh();
        }

        /// <summary>
        /// Sizes the scrolling window to the space between the column's top and the
        /// footer, capped at the column's own height.
        /// <para>
        /// Recomputed rather than fixed because the canvas height in layout units is
        /// a function of both the resolution and the interface scale, and the two
        /// configurations that do not fit — 3440x1440 and 1080p at 140% — differ from
        /// each other by 161 units. A single conservative height would make the
        /// common case scroll for no reason: at 1920x1080 there are 664 units
        /// available for 590 units of content.
        /// </para>
        /// <para>
        /// The cap is what keeps this invisible when there is room. Above a 1006 unit
        /// canvas the window is exactly the content and the ScrollRect has nothing to
        /// move, so the menu looks and behaves as it always did.
        /// </para>
        /// </summary>
        private void SyncViewport()
        {
            if (menuFrame == null || root == null) return;

            float canvasHeight = root.rect.height;
            if (canvasHeight <= 0f) return;

            float available = canvasHeight - ColumnTop - FooterReserve;
            float height = Mathf.Clamp(available, MinViewportHeight, ColumnContentHeight);
            if (Mathf.Abs(menuFrame.sizeDelta.y - height) < 0.5f) return;

            menuFrame.sizeDelta = new Vector2(ColumnWidth, height);

            // Back to the top whenever the window resizes. A scroll offset left over
            // from a shorter viewport would show the menu already scrolled down on a
            // screen with room for all of it.
            if (menuScroller != null) menuScroller.verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// Follows the canvas. The reference resolution moves when the player changes
        /// the interface scale and when the window is resized, neither of which raises
        /// an event this screen could subscribe to — and <see cref="SyncViewport"/>
        /// returns immediately unless the height actually changed.
        /// </summary>
        private void Update()
        {
            if (root == null || !root.gameObject.activeInHierarchy) return;
            SyncViewport();
        }

        /// <summary>
        /// Reconciles the drawn title with the plate.
        /// <para>
        /// One title, whichever is available. With the key art bound, the authored
        /// logo is the title and the whole text block goes — the 76pt mark, its rule
        /// and the "MECHANICAL ALPHA" line, which at 110-128px from the top would sit
        /// across the logo's halo. Nothing is lost by hiding it: the same build string
        /// is in the footer, which stays.
        /// </para>
        /// <para>
        /// Without the key art the text block is exactly what it always was, so a
        /// checkout missing the brand assets still gets a titled menu rather than an
        /// anonymous dark rectangle.
        /// </para>
        /// <para>
        /// The condition is "the background is showing a title", not "the background
        /// bound a plate". Those came apart: the plate bound and its separate wordmark
        /// was ordered underneath the opaque backdrop, so the drawn block stood down for
        /// a title nobody could see and the menu ran with no name on it at all.
        /// </para>
        /// </summary>
        private void ApplyBrandPresentation()
        {
            if (wordmark == null) return;
            bool plateCarriesTitle = keyArt != null && keyArt.CarriesTitle;
            wordmark.gameObject.SetActive(!plateCarriesTitle);
        }

        private void BuildWordmark()
        {
            var block = NebulaUiFactory.Group(root, "Wordmark");
            NebulaUiFactory.Place(block, new Vector2(0f, 1f), new Vector2(88f, -150f), new Vector2(700f, 110f));
            wordmark = block;

            // Placed explicitly: the factory's default rect is 200x18, and a 76pt
            // wordmark clipped to nothing inside it. The menu had never been
            // rendered, so the title was simply missing and no test could see it.
            NebulaUiFactory.Place(
                NebulaUiFactory.Label(block, "Mark", "NEBULA", 76,
                    NebulaUiDesignTokens.InkPrimary, TextAnchor.UpperLeft, FontStyle.Normal,
                    NebulaUiDesignTokens.TrackingDisplay).Text.rectTransform,
                new Vector2(0f, 1f), Vector2.zero, new Vector2(700f, 92f));

            var ruleRect = NebulaUiFactory.Rect(block, "Rule", NebulaUiDesignTokens.ActionPrimary).rectTransform;
            NebulaUiFactory.Place(ruleRect, new Vector2(0f, 0f), new Vector2(0f, -14f), new Vector2(64f, 1f));

            // Sized as well as positioned. At 0.42 tracking this sits close enough to
            // the 200px default to be one label edit away from the footer's fate.
            NebulaUiFactory.Place(
                NebulaUiFactory.Label(block, "Subtitle", "MECHANICAL ALPHA",
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.UpperLeft, FontStyle.Normal, 0.42f).Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(80f, -110f), new Vector2(360f, 18f));
        }

        // ---- column geometry -------------------------------------------------
        //
        // Named rather than inlined because the viewport's height is derived from
        // them every layout, and three of the four used to appear as bare numbers in
        // two places each.

        /// <summary>Left inset, shared with the footer so the two align.</summary>
        private const float ColumnLeft = 88f;

        /// <summary>Distance from the top of the canvas to the first entry.</summary>
        private const float ColumnTop = 300f;

        private const float ColumnWidth = 560f;

        /// <summary>
        /// The sheet's natural height: ÇIKIŞ sits at -542 and is 48 tall.
        /// <para>
        /// This is what the column wants, not what it gets. The viewport it scrolls
        /// inside is sized to what is actually available — see <see cref="SyncViewport"/>.
        /// </para>
        /// </summary>
        private const float ColumnContentHeight = 590f;

        /// <summary>
        /// Space kept clear at the bottom for the build line: its 56 unit margin, its
        /// 44 unit height, and 16 units so the two do not touch.
        /// </summary>
        private const float FooterReserve = 116f;

        /// <summary>A floor, so a pathologically short viewport still shows entries to scroll.</summary>
        private const float MinViewportHeight = 200f;

        private void BuildMenu()
        {
            // A scrolling viewport with the column inside it.
            //
            // The column is 590 units of content starting 300 down, so it needs a
            // 990 unit canvas to clear a bottom-anchored footer. Two supported
            // configurations do not have one, and both failed silently:
            //
            //   3440x1440  ->  932 units. ÇIKIŞ printed over the build line.
            //   1080p @140% ->  771 units. The build line printed over AYARLAR, and
            //                   KÜNYE and ÇIKIŞ were off the canvas entirely -- the
            //                   accessibility setting that exists to make the
            //                   interface usable was removing the quit button.
            //
            // Scrolling rather than a tighter row pitch: at 140% the player has asked
            // for larger targets, and shrinking rows to fit is the opposite of what
            // that setting means. Scrolling rather than letting the footer yield: the
            // footer is not what is missing at 140%, two entries are.
            //
            // When the canvas is tall enough the viewport is exactly the content
            // height and nothing scrolls at all, so the common case is byte-identical
            // to the layout this replaces.
            menuFrame = NebulaUiFactory.Group(root, "Menu Viewport");
            NebulaUiFactory.Place(menuFrame, new Vector2(0f, 1f),
                new Vector2(ColumnLeft, -ColumnTop), new Vector2(ColumnWidth, ColumnContentHeight));

            // Invisible, and a raycast target on purpose: a ScrollRect only sees a
            // wheel event that landed on a graphic. Without this, the wheel would work
            // over a row and do nothing in the gaps between them.
            Image wheelTarget = NebulaUiFactory.Rect(menuFrame, "Wheel Target", new Color(0f, 0f, 0f, 0f));
            NebulaUiFactory.Fill(wheelTarget.rectTransform);
            wheelTarget.raycastTarget = true;

            RectTransform viewport = NebulaUiFactory.Group(menuFrame, "Viewport");
            NebulaUiFactory.Fill(viewport);
            if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();

            var column = NebulaUiFactory.Group(viewport, "Menu Column");
            NebulaUiFactory.Place(column, new Vector2(0f, 1f), Vector2.zero,
                new Vector2(ColumnWidth, ColumnContentHeight));
            menuColumn = column;

            menuScroller = menuFrame.gameObject.AddComponent<ScrollRect>();
            menuScroller.viewport = viewport;
            menuScroller.content = column;
            menuScroller.horizontal = false;
            menuScroller.vertical = true;
            menuScroller.movementType = ScrollRect.MovementType.Clamped;
            // No inertia and no elasticity, for the reason the tutorial's chapter list
            // gives: a list that keeps sliding after the player stopped is decorative
            // motion, and decorative motion is what Reduced Motion and Motion Off
            // exist to remove. Never introduced rather than switched off twice.
            menuScroller.inertia = false;
            menuScroller.scrollSensitivity = 60f;      // one row per notch

            // The wheel is not the only way in. A ScrollRect does not follow keyboard
            // or controller focus on its own, so tabbing past the last visible entry
            // moves the selection to a control that is scrolled out of sight -- the
            // player is then operating a menu they cannot see. This keeps the window
            // on whatever is selected.
            NebulaMenuScrollFollow.Attach(menuFrame, menuScroller, column);

            continueButton = NebulaUiFactory.ButtonControl(column, "Continue", "DEVAM ET",
                NebulaUiAssetCatalog.ButtonKind.Primary, OnContinue);
            NebulaUiFactory.Place(continueButton.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), Vector2.zero, new Vector2(520f, 56f));

            summaryCard = BuildSummaryCard(column);

            newCampaignButton = MenuEntry(column, "New", "YENİ SEFER", -182f, NebulaUiAssetCatalog.ButtonKind.Secondary, OnNewCampaign);
            // Directly under YENİ SEFER, because that is where a player who has just
            // decided not to start a campaign yet is already looking. Everything
            // below it moved down one 60px slot.
            tutorialButton = MenuEntry(column, Tutorial.TutorialUiCopy.TutorialEntryName,
                Tutorial.TutorialUiCopy.TutorialEntry, -242f,
                NebulaUiAssetCatalog.ButtonKind.Secondary, OnTutorial);
            MenuEntry(column, "Load", "KAYIT YÜKLE", -302f, NebulaUiAssetCatalog.ButtonKind.Tertiary,
                () => LoadRequested?.Invoke());
            MenuEntry(column, "Settings", "AYARLAR", -362f, NebulaUiAssetCatalog.ButtonKind.Tertiary,
                () => SettingsRequested?.Invoke());
            // Its own entry, as the approved menu has it. The rows were reachable
            // before -- one section down inside AYARLAR, past interface and motion --
            // which is the wrong place for the switches a player needs before they
            // can comfortably use the rest of the menu.
            MenuEntry(column, "Accessibility", "ERİŞİLEBİLİRLİK", -422f,
                NebulaUiAssetCatalog.ButtonKind.Tertiary,
                () => AccessibilityRequested?.Invoke());
            MenuEntry(column, "Credits", "KÜNYE", -482f, NebulaUiAssetCatalog.ButtonKind.Tertiary,
                () => CreditsRequested?.Invoke());
            MenuEntry(column, "Quit", "ÇIKIŞ", -542f, NebulaUiAssetCatalog.ButtonKind.Tertiary, OnQuit);
        }

        /// <summary>
        /// Enables or disables the EĞİTİM entry, with the reason it is unavailable.
        /// Only the host knows whether a tutorial launcher exists, and a disabled
        /// control here still has to say why — that rule is the factory's, not this
        /// screen's option.
        /// </summary>
        public void SetTutorialAvailable(bool available, string reasonWhenBlocked = null)
        {
            TutorialBlockedReason = available ? null : reasonWhenBlocked;
            tutorialButton?.SetAvailable(available, reasonWhenBlocked);
        }

        /// <summary>Test-facing: why EĞİTİM is unavailable, or <c>null</c> when it is.</summary>
        public string TutorialBlockedReason { get; private set; }

        private NebulaUiFactory.ButtonHandle MenuEntry(RectTransform parent, string name, string caption,
            float y, NebulaUiAssetCatalog.ButtonKind kind, UnityEngine.Events.UnityAction onClick)
        {
            NebulaUiFactory.ButtonHandle handle =
                NebulaUiFactory.ButtonControl(parent, name, caption, kind, onClick);
            NebulaUiFactory.Place(handle.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(520f, 48f));
            handle.Label.Text.alignment = TMPro.TextAlignmentOptions.Left;
            handle.Label.Text.rectTransform.offsetMin = new Vector2(20f, 0f);
            return handle;
        }

        private RectTransform BuildSummaryCard(RectTransform parent)
        {
            Image panel = NebulaUiFactory.Panel(parent, "Continue Summary", NebulaUiAssetCatalog.PanelSecondary);
            RectTransform rect = panel.rectTransform;
            // 102, not the 148 this started at.
            //
            // It was cut to 102 to buy back a row: adding the tutorial entry pushed
            // ERİŞİLEBİLİRLİK off the bottom at 140% interface scale, and 102 was the
            // largest card height that let the accessibility entry clear a 771 unit
            // canvas. That arithmetic is no longer what holds the layout together --
            // the column scrolls now, so no entry depends on this number to be
            // reachable, and KÜNYE and ÇIKIŞ are no longer below any fold.
            //
            // Left at 102 anyway. Its two labels are a wrapped save summary or a
            // blocked reason at SizeCompact, which fit in 102 - 2*S4 = 70; growing it
            // back would buy nothing and would cost a row of scrolling on exactly the
            // short viewports this menu just stopped failing on.
            NebulaUiFactory.Place(rect, new Vector2(0f, 1f), new Vector2(0f, -68f), new Vector2(520f, 102f));

            continueDetail = NebulaUiFactory.Label(rect, "Detail", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Fill(continueDetail.Text.rectTransform, NebulaUiDesignTokens.S4);

            continueBlocked = NebulaUiFactory.Label(rect, "Blocked", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.Status(Visuals.UiStatusLevel.Caution), TextAnchor.UpperLeft,
                FontStyle.Bold, 0f, wrap: true);
            NebulaUiFactory.Fill(continueBlocked.Text.rectTransform, NebulaUiDesignTokens.S4);

            return rect;
        }

        /// <summary>
        /// What to tell a player whose data was carried across from the build before
        /// the app was renamed — or <c>null</c>, which is the usual answer.
        /// <para>
        /// The migrator's own report is a file list with words like "archived" and
        /// "conflict" in it; that is the right vocabulary for the report and the wrong
        /// one for a menu. This says the two things a player actually needs: their old
        /// data is here, and — in the case that would otherwise look like loss — the
        /// copies that could not be placed were kept rather than discarded.
        /// </para>
        /// <para>
        /// A failed migration deliberately says nothing. Nothing was deleted and the
        /// next boot retries, so the only effect of announcing it would be to worry
        /// somebody about a folder that is still sitting intact on their disk.
        /// </para>
        /// </summary>
        public static string MigrationNoticeFor(Platform.NebulaLegacyMigrationReport report)
        {
            if (report == null) return null;
            if (report.Outcome != Platform.LegacyMigrationOutcome.Migrated) return null;

            bool keptBack = report.archived.Count > 0 || report.conflicts.Count > 0;
            if (keptBack)
                return "Önceki sürümdeki bilgileriniz buraya taşındı. Bu bilgisayarda zaten " +
                       "kayıt olduğu için eskileri değiştirmedik — hiçbiri silinmedi, ayrı bir " +
                       "yedekte duruyor.";

            return report.imported.Count > 0
                ? "Önceki sürümdeki kayıtlarınız ve tuş ayarlarınız buraya taşındı."
                : null;
        }

        private void BuildFooter()
        {
            footerFrame = NebulaUiFactory.Group(root, "Footer");
            // 44 by default, grown to 76 only while a migration notice is on screen.
            //
            // It was a flat 76 when the notice was added, which cost a row: at 3440x1440
            // and 140% interface scale the taller footer reached up into the menu entries
            // by 0.849 world units. The notice appears on at most one boot in a player's
            // life, so making every other boot pay for it was the wrong trade -- and an
            // overlap the brand matrix only catches at one extreme configuration is
            // exactly the kind that ships.
            NebulaUiFactory.Place(footerFrame, new Vector2(0f, 0f), new Vector2(88f, 56f),
                new Vector2(760f, FooterHeightPlain));

            // Above the build line rather than in the summary card: this is a fact about
            // the install, not about the save the card describes, and it must still be
            // readable on the boot where there is no save to continue at all.
            migrationNotice = NebulaUiFactory.Label(footerFrame, "Migration", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.LowerLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(migrationNotice.Text.rectTransform,
                new Vector2(0f, 0f), new Vector2(0f, 22f), new Vector2(760f, 32f));
            migrationNotice.Text.gameObject.SetActive(false);

            // Placed, for the same reason the wordmark above is: an unplaced label
            // keeps the factory's 200x18 default, and this string is four times that
            // wide. It shipped reading "NEBULA · MECHANICAL ALPH…" -- the build and
            // schema the line exists to state were the part that got ellipsized.
            NebulaUiFactory.Place(
                NebulaUiFactory.Label(footerFrame, "Build",
                    $"NEBULA · MECHANICAL ALPHA · SAVE SCHEMA {Extraction.SaveService.CurrentSchema}",
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.LowerLeft, FontStyle.Normal,
                    NebulaUiDesignTokens.TrackingCaption).Text.rectTransform,
                new Vector2(0f, 0f), Vector2.zero, new Vector2(760f, 18f));
        }

        /// <summary>
        /// Re-reads the save slots and rebuilds the Continue affordance. Called on
        /// open and whenever the menu is revealed again, so returning from Load or
        /// from a finished session never shows a stale summary.
        /// </summary>
        public void Refresh()
        {
            resumeTarget = NebulaSaveSlotQuery.MostRecent();

            bool canContinue = resumeTarget != null && resumeTarget.CanContinue;
            ContinueBlockedReason = canContinue ? null : DescribeWhyNoContinue();

            continueButton.SetAvailable(canContinue, ContinueBlockedReason);
            summaryCard.gameObject.SetActive(true);
            continueDetail.Text.gameObject.SetActive(canContinue);
            continueBlocked.Text.gameObject.SetActive(!canContinue);

            if (canContinue) continueDetail.Value = Describe(resumeTarget);
            else continueBlocked.Value = ContinueBlockedReason;

            string notice = MigrationNoticeFor(Platform.NebulaLegacyDataMigrator.LastReport);
            migrationNotice.Text.gameObject.SetActive(notice != null);
            if (notice != null) migrationNotice.Value = notice;
            if (footerFrame != null)
                footerFrame.sizeDelta = new Vector2(footerFrame.sizeDelta.x,
                    notice != null ? FooterHeightWithNotice : FooterHeightPlain);
        }

        /// <summary>
        /// Why there is nothing to continue. A present-but-unusable slot reports its
        /// own reason instead of the generic "no save" line, so a player whose save
        /// was written by a newer build is told that rather than being told it is gone.
        /// </summary>
        private static string DescribeWhyNoContinue()
        {
            List<SaveSlotSummary> all = NebulaSaveSlotQuery.All();
            SaveSlotSummary blocked = null;
            foreach (SaveSlotSummary slot in all)
            {
                if (!slot.Exists) continue;
                if (blocked == null || slot.BlockReason == SaveSlotBlockReason.SchemaTooNew) blocked = slot;
            }

            return blocked == null
                ? "HENÜZ KAYIT YOK — YENİ BİR SEFER BAŞLATIN"
                : NebulaSaveSlotQuery.BlockReasonText(blocked);
        }

        private static string Describe(SaveSlotSummary slot)
        {
            var text = new System.Text.StringBuilder(160);
            text.Append($"YUVA {slot.Slot}   ·   SEVİYE {slot.Level}\n");
            text.Append($"{slot.KnownSystems} SİSTEM   ·   {slot.HeldPlanets} GEZEGEN   ·   {slot.FleetVehicles} ARAÇ\n");
            if (!string.IsNullOrEmpty(slot.LocationName)) text.Append($"KONUM: {slot.LocationName}\n");
            if (slot.SavedAtUtc.HasValue) text.Append(RelativeTime(slot.SavedAtUtc.Value));
            return text.ToString();
        }

        /// <summary>"4 dakika önce" reads better than a UTC stamp on a menu card.</summary>
        internal static string RelativeTime(DateTime savedUtc)
        {
            TimeSpan age = DateTime.UtcNow - savedUtc;
            if (age.TotalSeconds < 0) return "AZ ÖNCE KAYDEDİLDİ";
            if (age.TotalMinutes < 1) return "AZ ÖNCE KAYDEDİLDİ";
            if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} DAKİKA ÖNCE KAYDEDİLDİ";
            if (age.TotalHours < 24) return $"{(int)age.TotalHours} SAAT ÖNCE KAYDEDİLDİ";
            return $"{(int)age.TotalDays} GÜN ÖNCE KAYDEDİLDİ";
        }

        // ---- actions ---------------------------------------------------------

        private void OnContinue()
        {
            if (resumeTarget == null || !resumeTarget.CanContinue) return;
            launcher?.ContinueFromSlot(resumeTarget.Slot);
        }

        private void OnNewCampaign() => launcher?.StartNewCampaign();

        /// <summary>
        /// Deliberately does not touch <see cref="launcher"/>. The tutorial has no
        /// campaign behind it, and the one way to guarantee that is for this method
        /// to have nothing it could call.
        /// </summary>
        private void OnTutorial() => TutorialRequested?.Invoke();

        private void OnQuit() => launcher?.QuitGame();

        // ---- INebulaUiScreen -------------------------------------------------

        public void OnOpened(NebulaUiShell owningShell)
        {
            shell = owningShell;
            root.gameObject.SetActive(true);

            // Re-bound on open, not only at build: the plate depends on the viewport,
            // and a screen can be opened again after the window was resized past the
            // point where the smaller master stops being the right one.
            if (keyArt != null) keyArt.Bind();
            ApplyBrandPresentation();

            Refresh();
            FocusInitialControl();
        }

        public void OnClosed() => root.gameObject.SetActive(false);

        public void OnCovered() { }

        /// <summary>Re-reads on reveal so a save made elsewhere is reflected immediately.</summary>
        public void OnRevealed() { Refresh(); FocusInitialControl(); }

        /// <summary>
        /// Puts keyboard and gamepad focus somewhere real, every time this screen comes
        /// to the front.
        /// <para>
        /// Continue first when there is a campaign to continue, New Campaign otherwise
        /// — that is the ordering, and it is the interesting part. What used to be
        /// missing is the guarantee: both named buttons can be non-interactable at once
        /// (no save, and a launcher that has disabled New Campaign while it is starting
        /// one), and the method then returned having selected nothing. Unity's
        /// navigation walks outwards from the current selection, so with none the
        /// arrow keys and the stick do nothing at all and the screen is keyboard-dead
        /// with no visible sign of it. The sweep is the guarantee: if the column holds
        /// anything the player can use, one of them ends up focused.
        /// </para>
        /// </summary>
        private void FocusInitialControl()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;

            Button target = continueButton?.Button != null && continueButton.Button.interactable
                ? continueButton.Button : newCampaignButton?.Button;
            if (target != null && target.interactable && target.gameObject.activeInHierarchy)
            {
                events.SetSelectedGameObject(target.gameObject);
                return;
            }

            if (MenuColumn == null) return;
            foreach (Selectable candidate in MenuColumn.GetComponentsInChildren<Selectable>(false))
            {
                if (candidate == null || !candidate.IsInteractable()) continue;
                if (!candidate.gameObject.activeInHierarchy) continue;
                if (candidate.navigation.mode == UnityEngine.UI.Navigation.Mode.None) continue;
                events.SetSelectedGameObject(candidate.gameObject);
                return;
            }
        }

        /// <summary>The main menu is the root; Back has nowhere to go from here.</summary>
        public bool HandleBack() => true;
    }
}
