using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Sectors;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.UI.Logistics
{
    /// <summary>
    /// The System Logistics Centre, on screen.
    /// <para>
    /// The whole VS-011 backend has been shipped and unreachable: a centre is built,
    /// upgraded, filled, docked at and dismantled entirely through four contextual
    /// keys the player is told about in one line of a settings reading. Nothing
    /// anywhere showed how full it was, what was in it, which routes fed it, what a
    /// T2 upgrade would cost, or why an operation was refused —
    /// <c>ShipHangar.TryCreateRoute</c> in particular had no caller in the entire
    /// interface, so persistent collector routes could not be created at all.
    /// </para>
    /// <para>
    /// This panel is a reading and a set of commands, and it is <b>only</b> that. It
    /// writes no logistics state: every operation goes through
    /// <see cref="ShipLogisticsCenter"/>'s public command seam — the same calls the
    /// keys make — so the checks, the charges and the refusal text have exactly one
    /// implementation and the two entry points cannot drift.
    /// </para>
    /// <para>
    /// <b>Remote centres.</b> The list is every centre the campaign knows about, not
    /// only the one in the system the ship is standing in, because the backend
    /// genuinely carries both: a materialized centre is a live
    /// <see cref="LogisticsCenter"/> and every other one is the record inside its
    /// system's persisted <c>SystemDelta</c>. Reading a remote one is therefore real
    /// and is offered; <b>commanding</b> one is not, and no part of this panel
    /// pretends otherwise — every command still goes through
    /// <see cref="ShipLogisticsCenter"/>, which acts on the centre the ship is
    /// physically at and nothing else. That is why the gate below distinguishes
    /// three different refusals rather than one: out of communication, in
    /// communication but not here, and here but not close enough. Only the last two
    /// are fixed by flying, and telling a player to fly to a system they have no
    /// link to would be telling them the wrong thing.
    /// </para>
    /// </summary>
    public sealed class NebulaLogisticsCenterPanel
    {
        /// <summary>
        /// Centres listed at once: one live one plus the last-known records of three
        /// other systems. A campaign with more than four is listing the four it can
        /// reach first, which is the order <see cref="LogisticsCentreDirectory"/>
        /// collects them in.
        /// </summary>
        public const int MaxCenterRows = 4;

        /// <summary>Drill sources offered for a new route at once.</summary>
        public const int MaxSourceRows = 5;

        public const string PanelName = "Logistics";
        public const string OpenControlName = "OpenLogistics";
        public const string CloseControlName = "CloseLogistics";
        public const string UpgradeControlName = "LogisticsUpgrade";
        public const string CancelUpgradeControlName = "LogisticsCancelUpgrade";
        public const string DepositControlName = "LogisticsDeposit";
        public const string WithdrawControlName = "LogisticsWithdraw";
        public const string DismantleControlName = "LogisticsDismantle";
        public const string AssignRouteControlName = "AssignRoute";
        public const string BuildControlName = "LogisticsBuild";

        private readonly NebulaContext context;
        private readonly System.Func<string> selectedVehicleId;

        private RectTransform panel;
        private RectTransform centerList;
        private RectTransform sourceList;
        private NebulaUiFactory.LabelHandle emptyState;
        private NebulaUiFactory.LabelHandle headline, identity, capacityValue, contents, routes, docks, upgradeInfo, gate, status;
        private NebulaUiFactory.BarHandle fillBar;
        private Image fillWarningIcon;
        private NebulaUiFactory.ButtonHandle build, upgrade, cancelUpgrade, deposit, withdraw, dismantle;

        private readonly List<CenterRow> centerRows = new List<CenterRow>(MaxCenterRows);
        private readonly List<SourceRow> sourceRows = new List<SourceRow>(MaxSourceRows);
        private readonly List<string> centerIds = new List<string>(MaxCenterRows);
        private readonly List<string> sourceDrillIds = new List<string>(MaxSourceRows);

        /// <summary>The system whose centre the panel is acting on, or <c>null</c>.</summary>
        public string SelectedSystemId { get; private set; }

        /// <summary>True while the panel is on screen.</summary>
        public bool IsOpen => panel != null && panel.gameObject.activeSelf;

        /// <summary>Test-facing: centre rows currently drawn.</summary>
        public int VisibleCenters { get; private set; }

        /// <summary>Test-facing: drill sources currently offered for a new route.</summary>
        public int VisibleSources { get; private set; }

        /// <summary>Test-facing: the last thing the panel told the player.</summary>
        public string StatusText => status?.Text != null ? status.Text.text : null;

        /// <summary>Test-facing: why upgrading is unavailable, or <c>null</c>.</summary>
        public string UpgradeBlockedReason { get; private set; }

        /// <summary>
        /// Test-facing: why no operation can be issued at all — the ship is not near
        /// the anchor, or the system is out of communication. <c>null</c> when the
        /// centre can be commanded.
        /// </summary>
        public string ReachBlockedReason { get; private set; }

        /// <summary>
        /// Test-facing: the selected system's real communication state, exactly as
        /// <see cref="SystemStatusQuery.Link"/> resolved it. <c>null</c> when nothing
        /// is selected.
        /// </summary>
        public SystemLinkState? SelectedLink { get; private set; }

        /// <summary>
        /// Test-facing: true when the selected centre is a live object in the
        /// streamed-in system, false when it is the last-known record of another
        /// system. <c>null</c> when nothing is selected.
        /// </summary>
        public bool? SelectedIsMaterialized { get; private set; }

        private sealed class CenterRow
        {
            public RectTransform Root;
            public Image Background;
            public NebulaUiFactory.LabelHandle Name;
            public NebulaUiFactory.LabelHandle Detail;
        }

        private sealed class SourceRow
        {
            public RectTransform Root;
            public NebulaUiFactory.LabelHandle Label;
            public NebulaUiFactory.ButtonHandle Assign;
        }

        public NebulaLogisticsCenterPanel(NebulaContext sessionContext,
            System.Func<string> selectedVehicle = null)
        {
            context = sessionContext;
            selectedVehicleId = selectedVehicle;
        }

        // =====================================================================
        // BUILD
        // =====================================================================

        /// <summary>Builds the panel, closed, into the page that hosts it.</summary>
        public void Build(RectTransform host)
        {
            Image background = NebulaUiFactory.Panel(host, PanelName, NebulaUiAssetCatalog.PanelElevated);
            panel = background.rectTransform;
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(1080f, 700f);

            NebulaUiFactory.Place(
                NebulaUiFactory.SectionTitle(panel, "Title", "LOJİSTİK MERKEZİ").Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(24f, -22f), new Vector2(520f, 18f));

            NebulaUiFactory.ButtonHandle close = NebulaUiFactory.ButtonControl(panel, CloseControlName,
                "KAPAT", NebulaUiAssetCatalog.ButtonKind.Secondary, Close);
            NebulaUiFactory.Place(close.Button.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                new Vector2(-24f, -14f), new Vector2(140f, NebulaUiDesignTokens.MinimumHitTarget));

            BuildCenterList();
            BuildDetail();
            BuildSourceList();
            BuildActions();

            panel.gameObject.SetActive(false);
        }

        private void BuildCenterList()
        {
            centerList = NebulaUiFactory.Group(panel, "Centers");
            NebulaUiFactory.Place(centerList, new Vector2(0f, 1f), new Vector2(24f, -52f),
                new Vector2(360f, 300f));

            emptyState = NebulaUiFactory.Label(centerList, "Empty",
                "BU SİSTEMDE LOJİSTİK MERKEZİ YOK — ÇAPAYA YAKLAŞIP F İLE İNŞA EDİN",
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkMuted,
                TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(emptyState.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(350f, 48f));

            for (int i = 0; i < MaxCenterRows; i++) centerRows.Add(BuildCenterRow(i));
        }

        private CenterRow BuildCenterRow(int index)
        {
            var row = new CenterRow();
            Image panelImage = NebulaUiFactory.Panel(centerList, "Center " + index,
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            row.Root = panelImage.rectTransform;
            row.Background = panelImage;
            NebulaUiFactory.Place(row.Root, new Vector2(0f, 1f), new Vector2(0f, -index * 62f),
                new Vector2(356f, 56f));

            row.Name = NebulaUiFactory.Label(row.Root, "Name", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(row.Name.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(16f, -12f), new Vector2(320f, 18f));

            row.Detail = NebulaUiFactory.Label(row.Root, "Detail", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(row.Detail.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(16f, -34f), new Vector2(320f, 14f));

            // The row is the selector, reusing its own panel as the target graphic:
            // a second Image on one GameObject comes back null.
            var button = row.Root.gameObject.AddComponent<Button>();
            button.targetGraphic = row.Background;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = NebulaUiAssetCatalog.RowInteraction();
            int captured = index;
            button.onClick.AddListener(() => SelectRow(captured));

            row.Root.gameObject.SetActive(false);
            return row;
        }

        private void BuildDetail()
        {
            headline = NebulaUiFactory.Label(panel, "Headline", "", NebulaUiDesignTokens.SizeBody,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(headline.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(408f, -56f), new Vector2(640f, 20f));

            identity = NebulaUiFactory.Label(panel, "Identity", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(identity.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(408f, -82f), new Vector2(640f, 36f));

            capacityValue = NebulaUiFactory.Label(panel, "Capacity", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(capacityValue.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(408f, -124f), new Vector2(600f, 18f));

            // The 80% mark is the game's own attention icon, not a "⚠" typed into the
            // string. NebulaUiRemovalGuardTests.NoUnicodeIconReachesThePlayer exists
            // for exactly this: the project produces its icons, and a glyph borrowed
            // from the font renders at whatever weight and baseline that font happens
            // to give it, in a colour the status palette never chose. It is the same
            // mark NebulaHudAlerts mounts on an attention card, tinted by the same
            // status token.
            fillWarningIcon = NebulaUiFactory.Icon(panel, "FillWarning",
                NebulaProducedVisualResolver.AttentionIcon(), 16f);
            NebulaUiFactory.Place(fillWarningIcon.rectTransform, new Vector2(0f, 1f),
                new Vector2(1016f, -124f), new Vector2(16f, 16f));
            fillWarningIcon.color = NebulaUiDesignTokens.Status(UiStatusLevel.Warning);
            fillWarningIcon.gameObject.SetActive(false);

            fillBar = NebulaUiFactory.Bar(panel, "Fill", NebulaUiDesignTokens.Status(UiStatusLevel.Working));
            RectTransform bar = fillBar.Track.rectTransform;
            NebulaUiFactory.Place(bar, new Vector2(0f, 1f), new Vector2(408f, -148f),
                new Vector2(640f, NebulaUiDesignTokens.BarHeight));

            contents = NebulaUiFactory.Label(panel, "Contents", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(contents.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(408f, -168f), new Vector2(640f, 60f));

            routes = NebulaUiFactory.Label(panel, "Routes", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(routes.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(408f, -232f), new Vector2(640f, 76f));

            docks = NebulaUiFactory.Label(panel, "Docks", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft);
            NebulaUiFactory.Place(docks.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(408f, -312f), new Vector2(640f, 16f));

            upgradeInfo = NebulaUiFactory.Label(panel, "Upgrade", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(upgradeInfo.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(408f, -334f), new Vector2(640f, 76f));

            // The reach gate is a caution, not a failure: nothing is wrong with the
            // centre, the ship is simply not in a position to command it.
            gate = NebulaUiFactory.Label(panel, "Gate", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Caution), TextAnchor.UpperLeft,
                FontStyle.Bold, 0f, wrap: true);
            NebulaUiFactory.Place(gate.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(408f, -414f), new Vector2(640f, 36f));

            status = NebulaUiFactory.Label(panel, "Status", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(status.Text.rectTransform, new Vector2(0f, 0f),
                new Vector2(24f, 14f), new Vector2(560f, 32f));
        }

        private void BuildSourceList()
        {
            sourceList = NebulaUiFactory.Group(panel, "Sources");
            NebulaUiFactory.Place(sourceList, new Vector2(0f, 1f), new Vector2(24f, -366f),
                new Vector2(360f, 260f));

            NebulaUiFactory.Place(
                NebulaUiFactory.SectionTitle(sourceList, "SourcesTitle", "ROTA KAYNAĞI").Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(340f, 16f));

            for (int i = 0; i < MaxSourceRows; i++)
            {
                var row = new SourceRow();
                Image rowPanel = NebulaUiFactory.Panel(sourceList, "Source " + i,
                    NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
                row.Root = rowPanel.rectTransform;
                NebulaUiFactory.Place(row.Root, new Vector2(0f, 1f), new Vector2(0f, -28f - i * 44f),
                    new Vector2(356f, 40f));

                row.Label = NebulaUiFactory.Label(row.Root, "Label", "", NebulaUiDesignTokens.SizeCaption,
                    NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft);
                NebulaUiFactory.Place(row.Label.Text.rectTransform, new Vector2(0f, 0.5f),
                    new Vector2(14f, 0f), new Vector2(200f, 16f));

                // The assign control belongs to the source list's coordinate space,
                // not to the row's, and is switched off with it.
                //
                // Both halves of that are the same defect, caught by
                // NebulaFleetPageTests.EveryVisibleContextAction_GetsItsOwnSlot: a
                // control nested in the row inherited the row's local origin, so all
                // five sat at one anchored position, and switching the row off left
                // each of them still reading as active in its own right. An action
                // that is not being offered must not be switched on anywhere in the
                // graph, and every action on this page must own a distinct slot —
                // otherwise "two actions overlap" is a question nothing can answer.
                // Anchored to the list's top-right with the row's own vertical offset
                // folded in, so it draws exactly where it did before.
                int captured = i;
                row.Assign = NebulaUiFactory.ButtonControl(sourceList, AssignRouteControlName, "ROTA KUR",
                    NebulaUiAssetCatalog.ButtonKind.Secondary, () => AssignRoute(captured));
                NebulaUiFactory.Place(row.Assign.Button.GetComponent<RectTransform>(),
                    new Vector2(1f, 1f),
                    new Vector2(-14f, -48f - i * 44f + NebulaUiDesignTokens.MinimumHitTarget * 0.5f),
                    new Vector2(120f, NebulaUiDesignTokens.MinimumHitTarget));

                row.Root.gameObject.SetActive(false);
                row.Assign.Button.gameObject.SetActive(false);
                sourceRows.Add(row);
            }
        }

        private void BuildActions()
        {
            build = NebulaUiFactory.ButtonControl(panel, BuildControlName, "T1 MERKEZ İNŞA ET",
                NebulaUiAssetCatalog.ButtonKind.Primary, BuildCenter);
            NebulaUiFactory.Place(build.Button.GetComponent<RectTransform>(), new Vector2(0f, 0f),
                new Vector2(24f, 14f), new Vector2(230f, 44f));

            upgrade = NebulaUiFactory.ButtonControl(panel, UpgradeControlName, "T2'YE YÜKSELT",
                NebulaUiAssetCatalog.ButtonKind.Primary, StartUpgrade);
            NebulaUiFactory.Place(upgrade.Button.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                new Vector2(-24f, 14f), new Vector2(210f, 44f));

            cancelUpgrade = NebulaUiFactory.ButtonControl(panel, CancelUpgradeControlName,
                "YÜKSELTMEYİ İPTAL", NebulaUiAssetCatalog.ButtonKind.Secondary, CancelUpgrade);
            NebulaUiFactory.Place(cancelUpgrade.Button.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                new Vector2(-246f, 14f), new Vector2(220f, 44f));

            deposit = NebulaUiFactory.ButtonControl(panel, DepositControlName, "YÜKLE",
                NebulaUiAssetCatalog.ButtonKind.Secondary, Deposit);
            NebulaUiFactory.Place(deposit.Button.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                new Vector2(-24f, 66f), new Vector2(140f, 40f));

            withdraw = NebulaUiFactory.ButtonControl(panel, WithdrawControlName, "BOŞALT",
                NebulaUiAssetCatalog.ButtonKind.Secondary, Withdraw);
            NebulaUiFactory.Place(withdraw.Button.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                new Vector2(-176f, 66f), new Vector2(140f, 40f));

            dismantle = NebulaUiFactory.ButtonControl(panel, DismantleControlName, "SÖK",
                NebulaUiAssetCatalog.ButtonKind.Destructive, Dismantle);
            NebulaUiFactory.Place(dismantle.Button.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                new Vector2(-328f, 66f), new Vector2(140f, 40f));
        }

        private void BuildCenter()
        {
            ShipLogisticsCenter shipSide = ShipSide();
            if (shipSide == null) { Report("OTURUM YOK"); return; }
            CenterBuildRejectReason reason = shipSide.TryBuild();
            Report(reason == CenterBuildRejectReason.None
                ? "T1 LOJİSTİK MERKEZİ İNŞASI BAŞLADI"
                : ShipLogisticsCenter.BuildRejectText(reason));
            Refresh();
        }

        // =====================================================================
        // OPEN / CLOSE
        // =====================================================================

        public void Open()
        {
            if (panel == null) return;
            panel.gameObject.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (panel == null) return;
            panel.gameObject.SetActive(false);
        }

        /// <summary>Picks the centre the commands act on.</summary>
        public void Select(string systemId)
        {
            SelectedSystemId = systemId;
            Refresh();
        }

        private void SelectRow(int index)
        {
            if (index < 0 || index >= centerIds.Count) return;
            Select(centerIds[index]);
        }

        // =====================================================================
        // COMMANDS — every one through ShipLogisticsCenter's public seam
        // =====================================================================

        private ShipLogisticsCenter ShipSide()
        {
            Ship.ShipController ship = context?.Ship;
            return ship != null ? ship.GetComponent<ShipLogisticsCenter>() : null;
        }

        private LogisticsCentreReading Selected() =>
            SelectedSystemId == null ? null : LogisticsCentreDirectory.Read(SelectedSystemId);

        /// <summary>
        /// The live object behind the selection, or <c>null</c> when the selection is
        /// a last-known record. Every command path goes through this: a reading is a
        /// reading, and there is nothing on the other end of a remote one to command.
        /// </summary>
        private LogisticsCenter SelectedLiveCentre()
        {
            LogisticsCentreReading reading = Selected();
            return reading != null ? reading.Live : null;
        }

        private void StartUpgrade()
        {
            ShipLogisticsCenter shipSide = ShipSide();
            if (shipSide == null) { Report("OTURUM YOK"); return; }

            CenterUpgradeRejectReason reason = shipSide.TryStartUpgrade();
            Report(reason == CenterUpgradeRejectReason.None
                ? "T2 YÜKSELTME BAŞLADI"
                : UpgradeRejectText(reason));
            Refresh();
        }

        private void CancelUpgrade()
        {
            ShipLogisticsCenter shipSide = ShipSide();
            if (shipSide == null) { Report("OTURUM YOK"); return; }

            Report(shipSide.TryCancelUpgrade()
                ? "YÜKSELTME İPTAL — %75 İADE"
                : "İPTAL EDİLECEK YÜKSELTME YOK");
            Refresh();
        }

        private void Deposit()
        {
            ShipLogisticsCenter shipSide = ShipSide();
            if (shipSide == null) { Report("OTURUM YOK"); return; }

            float moved = shipSide.DepositToCenter(shipSide.OneSecondTransferBudget);
            Report(moved > 0.0001f ? $"{moved:0} BİRİM YÜKLENDİ" : "AKTARILACAK KABUL EDİLEN KAYNAK YOK");
            Refresh();
        }

        private void Withdraw()
        {
            ShipLogisticsCenter shipSide = ShipSide();
            if (shipSide == null) { Report("OTURUM YOK"); return; }

            float moved = shipSide.WithdrawFromCenter(shipSide.OneSecondTransferBudget);
            Report(moved > 0.0001f ? $"{moved:0} BİRİM ALINDI" : "ALINACAK KAYNAK YOK");
            Refresh();
        }

        private void Dismantle()
        {
            ShipLogisticsCenter shipSide = ShipSide();
            if (shipSide == null) { Report("OTURUM YOK"); return; }

            bool ok = shipSide.TryDismantle();
            // The seam already wrote the refusal; quoting it back is what puts the
            // reason on the screen rather than only in the HUD's transient line.
            Report(ok ? "MERKEZ SÖKÜLDÜ — %50 İADE" : shipSide.StatusMessage);
            SelectedSystemId = ok ? null : SelectedSystemId;
            Refresh();
        }

        /// <summary>
        /// Creates a persistent drill → collector → centre route from the vehicle the
        /// fleet page has selected. <c>ShipHangar.TryCreateRoute</c> has existed since
        /// VS-011 and had no caller anywhere in the interface; this is it.
        /// </summary>
        private void AssignRoute(int index)
        {
            ShipHangar hangar = context?.Hangar;
            LogisticsCenter center = SelectedLiveCentre();
            if (hangar == null || center == null) { Report("OTURUM YOK"); return; }
            if (index < 0 || index >= sourceDrillIds.Count) return;

            VehicleAgent vehicle = SelectedVehicle(hangar);
            if (vehicle == null) { Report("ÖNCE FİLO SAYFASINDAN BİR TOPLAYICI SEÇİN"); return; }

            DrillStructure drill = FindDrill(sourceDrillIds[index]);
            if (drill == null) { Report("SONDAJ BULUNAMADI"); Refresh(); return; }

            RouteCreateRejectReason reason =
                hangar.TryCreateRoute(vehicle, drill, center, RoutePriority.Normal);
            Report(reason == RouteCreateRejectReason.None
                ? "ROTA KURULDU"
                : RouteRejectText(reason));
            Refresh();
        }

        private VehicleAgent SelectedVehicle(ShipHangar hangar)
        {
            string id = selectedVehicleId != null ? selectedVehicleId() : null;
            if (id == null) return null;
            IReadOnlyList<VehicleAgent> vehicles = hangar.Vehicles;
            for (int i = 0; i < vehicles.Count; i++)
                if (vehicles[i] != null && vehicles[i].VehicleId == id) return vehicles[i];
            return null;
        }

        private static DrillStructure FindDrill(string worldId)
        {
            foreach (DrillStructure drill in ExtractionState.Drills)
                if (drill != null && drill.WorldId == worldId) return drill;
            return null;
        }

        private void Report(string message)
        {
            if (status == null) return;
            status.Value = message ?? string.Empty;
        }

        // =====================================================================
        // REFRESH
        // =====================================================================

        public void Refresh()
        {
            if (panel == null) return;

            CollectCenters();
            LogisticsCentreReading selected = Selected();
            RefreshDetail(selected);
            RefreshSources(selected);
            RefreshActions(selected);
        }

        /// <summary>
        /// Every centre the campaign knows about, once — the live one and every
        /// system whose delta still carries a record. Keyed by system id rather than
        /// by list index so a centre that is registered twice (a restore over a live
        /// registration, the exact shape of a duplicate-row defect) is still one row,
        /// and so a live centre is never listed a second time from its own delta.
        /// </summary>
        private void CollectCenters()
        {
            LogisticsCentreDirectory.CollectSystems(centerIds, MaxCenterRows);

            // A selection that has gone away cannot be commanded.
            if (SelectedSystemId != null && !centerIds.Contains(SelectedSystemId)) SelectedSystemId = null;
            if (SelectedSystemId == null && centerIds.Count > 0) SelectedSystemId = centerIds[0];

            bool any = centerIds.Count > 0;
            if (emptyState.Text.gameObject.activeSelf == any)
                emptyState.Text.gameObject.SetActive(!any);

            for (int i = 0; i < centerRows.Count; i++)
            {
                bool visible = i < centerIds.Count;
                if (centerRows[i].Root.gameObject.activeSelf != visible)
                    centerRows[i].Root.gameObject.SetActive(visible);
                if (!visible) continue;

                LogisticsCentreReading reading = LogisticsCentreDirectory.Read(centerIds[i]);
                centerRows[i].Name.Value = SystemName(centerIds[i]);
                centerRows[i].Detail.Value = reading == null
                    ? "—"
                    : $"{TierName(reading.Tier)} · {StateName(reading.State)} · " +
                      $"%{reading.Fill01 * 100f:0}" +
                      (reading.IsMaterialized ? string.Empty : "  ·  SON BİLİNEN");
                centerRows[i].Background.sprite = NebulaUiAssetCatalog.Row(
                    centerIds[i] == SelectedSystemId
                        ? NebulaUiAssetCatalog.RowState.Selected
                        : NebulaUiAssetCatalog.RowState.Default);
            }

            VisibleCenters = centerIds.Count;
        }

        private void RefreshDetail(LogisticsCentreReading centre)
        {
            if (centre == null)
            {
                headline.Value = "—";
                identity.Value = string.Empty;
                capacityValue.Value = string.Empty;
                if (fillWarningIcon.gameObject.activeSelf) fillWarningIcon.gameObject.SetActive(false);
                fillBar.SetFraction(0f);
                contents.Value = string.Empty;
                routes.Value = string.Empty;
                docks.Value = string.Empty;
                upgradeInfo.Value = string.Empty;
                gate.Value = string.Empty;
                ReachBlockedReason = null;
                SelectedLink = null;
                SelectedIsMaterialized = null;
                return;
            }

            SelectedIsMaterialized = centre.IsMaterialized;

            headline.Value = $"{SystemName(centre.SystemId)} LOJİSTİK MERKEZİ";

            // Orbital, never on a surface: the anchor sits between the star's danger
            // boundary and the innermost planet, which is why no planet is named here.
            string state = StateName(centre.State);
            if (centre.State == LogisticsCenterState.Constructing)
                state += $" · {centre.BuildRemaining:0}sn";
            if (centre.Upgrading)
                state += $" · T2 {centre.UpgradeRemaining:0}sn";

            identity.Value = $"SİSTEM: {SystemName(centre.SystemId)}  ·  YÖRÜNGESEL ÇAPA (GEZEGEN ÜSTÜ DEĞİL)\n" +
                             $"KADEME: {TierName(centre.Tier)}  ·  DURUM: {state}  ·  " +
                             (centre.IsMaterialized ? "CANLI VERİ" : "SON BİLİNEN VERİ");

            capacityValue.Value =
                $"DOLULUK {centre.StoredVolume:0} / {centre.Capacity:0}  ·  %{centre.Fill01 * 100f:0}" +
                (centre.FillWarningActive ? "   %80 ÜZERİ" : string.Empty);
            if (fillWarningIcon.gameObject.activeSelf != centre.FillWarningActive)
                fillWarningIcon.gameObject.SetActive(centre.FillWarningActive);
            fillBar.SetFraction(centre.Fill01);
            fillBar.SetColor(NebulaUiDesignTokens.Status(
                centre.FillWarningActive ? UiStatusLevel.Warning : UiStatusLevel.Working));

            contents.Value = DescribeContents(centre);
            routes.Value = DescribeRoutes(centre);
            docks.Value = $"DOCK {centre.DocksInUse} / {centre.DockCount}" +
                          (centre.DocksInUse < centre.DockCount ? "  ·  BOŞ DOCK VAR" : "  ·  DOCK'LAR DOLU") +
                          $"   ·   ROTA {centre.Routes.Count} / {centre.MaxRoutes}";
            upgradeInfo.Value = DescribeUpgrade(centre);

            ReachBlockedReason = ReachReason(centre);
            gate.Value = ReachBlockedReason ?? string.Empty;
        }

        /// <summary>
        /// What is in the centre, and what the centre does with reservations.
        /// <para>
        /// It does not use them, and this line says so as a fact about the design
        /// rather than drawing a figure. Every <c>TryReserveAll</c> call site in the
        /// game targets the ship's own hold or a planet's Maintenance Reserve; a
        /// centre's inventory is never reserved into, and its save record has no
        /// reservation field to carry one even if it were. A "RESERVED: 0" row would
        /// therefore be a number that can never move, which reads to a player as
        /// "nothing is spoken for <i>right now</i>" — a promise this centre is not
        /// making.
        /// </para>
        /// <para>
        /// The real API is still read rather than assumed away: if a reservation ever
        /// does appear in a centre's inventory this line reports it instead, and the
        /// PlayMode fixture holds the invariant to the same reading.
        /// </para>
        /// </summary>
        private static string DescribeContents(LogisticsCentreReading centre)
        {
            if (centre == null || centre.Contents.Count == 0) return "İÇERİK: BOŞ";

            var text = new System.Text.StringBuilder(240);
            text.Append("İÇERİK: ");

            for (int i = 0; i < centre.Contents.Count && i < 4; i++)
            {
                if (i > 0) text.Append("  ·  ");
                text.Append($"{NebulaText.Upper(ItemName(centre.Contents[i].itemId))} {centre.Contents[i].amount:0}");
            }

            float free = Mathf.Max(0f, centre.Capacity - centre.StoredVolume);
            if (centre.ReservedVolume > 0.0001f)
                text.Append($"\nREZERVE: {centre.ReservedVolume:0} BİRİM  ·  BOŞ ALAN: {free:0} BİRİM");
            else
                text.Append("\nBU MERKEZ REZERVASYON KULLANMAZ — DEPOSUNUN TAMAMI ÇEKİLEBİLİR" +
                            $"  ·  BOŞ ALAN: {free:0} BİRİM");
            return text.ToString();
        }

        private static string DescribeRoutes(LogisticsCentreReading centre)
        {
            var text = new System.Text.StringBuilder(240);
            int shown = 0;

            for (int i = 0; i < centre.Routes.Count && shown < 3; i++)
            {
                CentreRouteReading route = centre.Routes[i];
                text.Append($"ROTA {NebulaPlanetName.Of(route.DrillWorldId)} → MERKEZ  ·  ");
                text.Append(PhaseName(route.Phase));
                if (route.HeldAmount > 0.0001f)
                    text.Append($"  ·  {NebulaText.Upper(ItemName(route.HeldItemId))} {route.HeldAmount:0}");
                if (route.WaitReason != RouteWaitReason.None)
                    text.Append($"  ·  {WaitName(route.WaitReason)}");
                text.Append('\n');
                shown++;
            }

            return shown == 0 ? "AKTİF ROTA YOK" : text.ToString().TrimEnd('\n');
        }

        /// <summary>What a T2 upgrade needs, costs and takes — before it is started, not after.</summary>
        private string DescribeUpgrade(LogisticsCentreReading centre)
        {
            if (centre.Tier == LogisticsCenterTier.T2) return "KADEME AZAMİ SEVİYEDE (T2)";

            LogisticsCenterTierDef t2 = LogisticsCenterCatalog.T2;
            if (centre.Upgrading)
            {
                // The exact refund is bookkeeping the live object carries and the
                // delta does not, so a last-known reading quotes the catalog rule it
                // was computed from rather than a number it does not have.
                LogisticsCenter live = centre.Live;
                string refund = live != null
                    ? $"{live.UpgradeRefundParts} PARÇA · {live.UpgradeRefundMetalAlloy} METAL ALAŞIM · " +
                      $"{live.UpgradeRefundBiocomposite} BİYOKOMPOZİT"
                    : $"T2 MALİYETİNİN %{LogisticsCenterCatalog.RefundFraction * 100f:0}'İ";
                return $"T2 YÜKSELTMESİ SÜRÜYOR · KALAN {centre.UpgradeRemaining:0}sn\nİPTALDE İADE: {refund}";
            }

            return $"T2 GEREKSİNİM: SEVİYE {t2.RequiredLevel} · İSTASYON İNŞA T{t2.RequiredModuleTier} · " +
                   $"2 KONTROL EDİLEN GEZEGEN\n" +
                   $"T2 MALİYET: {t2.PartsCost} PARÇA · {t2.MetalAlloyCost} METAL ALAŞIM · " +
                   $"{t2.BiocompositeCost} BİYOKOMPOZİT  ·  SÜRE {t2.BuildSeconds:0}sn\n" +
                   $"T2 KAZANIM: KAPASİTE {t2.Capacity:0} · ROTA {t2.MaxRoutes} · DOCK {t2.DockCount}";
        }

        /// <summary>
        /// Why nothing can be commanded, or <c>null</c>.
        /// <para>
        /// Two different gates, and they are not interchangeable: <b>proximity</b> is
        /// the ship not being at the anchor, which the player fixes by flying there,
        /// and <b>communication</b> is the system being unreachable from where the
        /// ship is, which they fix by taking a Command relay. Collapsing them into
        /// one "unavailable" would tell the player to do the wrong thing half the time.
        /// </para>
        /// </summary>
        private string ReachReason(LogisticsCentreReading centre)
        {
            ShipLogisticsCenter shipSide = ShipSide();
            if (shipSide == null) { SelectedLink = null; return "OTURUM YOK"; }

            SystemLinkState link = LinkTo(centre.SystemId, shipSide.CurrentSystemId);
            SelectedLink = link;

            if (link == SystemLinkState.Disconnected)
                return "BU SİSTEMLE BAĞLANTI YOK — KOMUTA AĞI DIŞINDA";
            if (link == SystemLinkState.LastKnown)
                return "SON BİLİNEN VERİ — CANLI BAĞLANTI YOK, KOMUT VERİLEMEZ";

            // Live, and still not commandable from here: the relay carries a reading,
            // not a hand. Every command below acts through ShipLogisticsCenter on the
            // centre the ship is physically at, so a live link to somewhere else is
            // still a trip.
            if (shipSide.CurrentSystemId != centre.SystemId || !centre.IsMaterialized)
                return "GEMİ BAŞKA BİR SİSTEMDE — KOMUT İÇİN MERKEZE GİDİN";
            if (!shipSide.InRange)
                return "ÇAPAYA YAKLAŞIN — KOMUT MENZİLİ DIŞINDA";

            return null;
        }

        private SystemLinkState LinkTo(string systemId, string shipSystemId)
        {
            // A ship that is not inside any system has no live link to anything: the
            // resolver's "Live" case is same-system or a Command relay at both ends,
            // and neither can hold with no system at this end.
            if (string.IsNullOrEmpty(shipSystemId)) return SystemLinkState.Disconnected;

            Navigation.SystemKnowledgeService knowledge = context?.NavigationDirector?.Knowledge;

            // With no knowledge service there is nothing to resolve a relay against.
            // The ship's own system is Live by definition and needs no resolver;
            // claiming Live for any other system would be inventing a link.
            if (knowledge == null)
                return systemId == shipSystemId ? SystemLinkState.Live : SystemLinkState.LastKnown;

            return SystemStatusQuery.Link(systemId, shipSystemId, knowledge);
        }

        private void RefreshSources(LogisticsCentreReading centre)
        {
            sourceDrillIds.Clear();

            // Only a materialized centre has drills to route from: an unstreamed
            // system has no DrillStructure instances at all, and TryCreateRoute
            // refuses anything outside the ship's own system anyway.
            if (centre != null && centre.IsMaterialized)
            {
                foreach (DrillStructure drill in ExtractionState.Drills)
                {
                    if (drill == null || drill.Runtime == null) continue;
                    if (WorldIdCodec.SystemIdFromPlanetWorldId(drill.WorldId) != centre.SystemId) continue;
                    if (LogisticsState.FindRouteByDrill(drill.WorldId) != null) continue;
                    sourceDrillIds.Add(drill.WorldId);
                    if (sourceDrillIds.Count >= MaxSourceRows) break;
                }
            }

            bool canRoute = centre != null && centre.IsMaterialized && ReachBlockedReason == null;
            for (int i = 0; i < sourceRows.Count; i++)
            {
                bool visible = i < sourceDrillIds.Count;
                if (sourceRows[i].Root.gameObject.activeSelf != visible)
                    sourceRows[i].Root.gameObject.SetActive(visible);
                // The control no longer lives inside the row, so it is switched with
                // it rather than by it. A row that is not offered leaves nothing on.
                if (sourceRows[i].Assign.Button.gameObject.activeSelf != visible)
                    sourceRows[i].Assign.Button.gameObject.SetActive(visible);
                if (!visible) continue;

                sourceRows[i].Label.Value = NebulaPlanetName.Of(sourceDrillIds[i]);
                sourceRows[i].Assign.SetAvailable(canRoute, ReachBlockedReason ?? "MERKEZ SEÇİLMEDİ");
            }

            VisibleSources = sourceDrillIds.Count;
        }

        private void RefreshActions(LogisticsCentreReading centre)
        {
            if (upgrade == null) return;

            ShipLogisticsCenter shipSide = ShipSide();
            LogisticsCenter live = centre != null ? centre.Live : null;

            CenterBuildRejectReason buildReason = shipSide != null
                ? shipSide.CheckBuild() : CenterBuildRejectReason.NoSafeAnchor;
            bool canBuild = centre == null && shipSide != null && buildReason == CenterBuildRejectReason.None;
            build.SetAvailable(canBuild, centre != null
                ? "BU SİSTEMDE MERKEZ ZATEN VAR"
                : shipSide == null ? "OTURUM YOK" : ShipLogisticsCenter.BuildRejectText(buildReason));

            if (centre == null) UpgradeBlockedReason = "MERKEZ SEÇİLMEDİ";
            else if (ReachBlockedReason != null) UpgradeBlockedReason = ReachBlockedReason;
            else if (shipSide == null) UpgradeBlockedReason = "OTURUM YOK";
            else
            {
                CenterUpgradeRejectReason reason = shipSide.CheckUpgrade();
                UpgradeBlockedReason = reason == CenterUpgradeRejectReason.None
                    ? null : UpgradeRejectText(reason);
            }

            upgrade.SetAvailable(UpgradeBlockedReason == null, UpgradeBlockedReason);

            bool upgrading = live != null && live.Runtime.Upgrading;
            cancelUpgrade.SetAvailable(upgrading && ReachBlockedReason == null,
                ReachBlockedReason ?? "SÜREN BİR YÜKSELTME YOK");

            bool transferable = live != null && live.Runtime.IsActive && ReachBlockedReason == null;
            deposit.SetAvailable(transferable, ReachBlockedReason ?? "MERKEZ AKTİF DEĞİL");
            withdraw.SetAvailable(transferable, ReachBlockedReason ?? "MERKEZ AKTİF DEĞİL");

            bool dismantleable = live != null && ReachBlockedReason == null && live.CanDismantle();
            dismantle.SetAvailable(dismantleable,
                ReachBlockedReason ?? "SÖKÜM İÇİN ENVANTER BOŞ VE DOCK'LAR BOŞ OLMALI");
        }

        // =====================================================================
        // Naming
        // =====================================================================

        internal static string SystemName(string systemId)
        {
            if (string.IsNullOrEmpty(systemId)) return "SİSTEM";
            return StarSystemRegistry.TryGet(systemId, out StarSystemRecord record)
                   && !string.IsNullOrEmpty(record.DisplayName)
                ? NebulaText.Upper(record.DisplayName)
                : NebulaText.Upper(systemId);
        }

        private static string ItemName(string itemId) =>
            string.IsNullOrEmpty(itemId) ? "—" : itemId;

        internal static string TierName(LogisticsCenterTier tier) =>
            tier == LogisticsCenterTier.T1 ? "T1" : "T2";

        internal static string StateName(LogisticsCenterState state)
        {
            switch (state)
            {
                case LogisticsCenterState.Constructing: return "İNŞA EDİLİYOR";
                case LogisticsCenterState.Active: return "ETKİN";
                case LogisticsCenterState.Disabled: return "DEVRE DIŞI";
                case LogisticsCenterState.Dismantled: return "SÖKÜLDÜ";
                default: return "—";
            }
        }

        internal static string PhaseName(RoutePhase phase)
        {
            switch (phase)
            {
                case RoutePhase.Paused: return "DURAKLATILDI";
                case RoutePhase.ToSource: return "KAYNAĞA GİDİYOR";
                case RoutePhase.Loading: return "YÜKLENİYOR";
                case RoutePhase.ToCenter: return "MERKEZE GİDİYOR";
                case RoutePhase.Queued: return "SIRADA";
                case RoutePhase.Unloading: return "BOŞALTIYOR";
                case RoutePhase.Holding: return "BEKLİYOR";
                case RoutePhase.Returning: return "DÖNÜYOR";
                default: return "—";
            }
        }

        internal static string WaitName(RouteWaitReason reason)
        {
            switch (reason)
            {
                case RouteWaitReason.SourceDrillMissing: return "SONDAJ YOK";
                case RouteWaitReason.SourceDrillInactive: return "SONDAJ ETKİN DEĞİL";
                case RouteWaitReason.SourceDrillEvacuated: return "SONDAJ TAHLİYE EDİLDİ";
                case RouteWaitReason.SourceDrillContested: return "SONDAJ ÇEKİŞMELİ";
                case RouteWaitReason.CenterFull: return "MERKEZ DOLU";
                case RouteWaitReason.CenterDisabled: return "MERKEZ DEVRE DIŞI";
                default: return "—";
            }
        }

        /// <summary>Why an upgrade is refused, in the player's language.</summary>
        internal static string UpgradeRejectText(CenterUpgradeRejectReason reason)
        {
            switch (reason)
            {
                case CenterUpgradeRejectReason.None: return null;
                case CenterUpgradeRejectReason.NotActive: return "MERKEZ AKTİF DEĞİL";
                case CenterUpgradeRejectReason.AlreadyUpgrading: return "ZATEN YÜKSELTİLİYOR";
                case CenterUpgradeRejectReason.LevelTooLow:
                    return $"SEVİYE YETERSİZ ({LogisticsCenterCatalog.T2.RequiredLevel})";
                case CenterUpgradeRejectReason.ModuleTierTooLow: return "İSTASYON İNŞA T2 GEREKLİ";
                case CenterUpgradeRejectReason.NotEnoughControlledPlanets:
                    return "EN AZ 2 KONTROL EDİLEN GEZEGEN GEREKLİ";
                case CenterUpgradeRejectReason.NotInRange: return "ÇAPAYA YAKLAŞIN";
                case CenterUpgradeRejectReason.TooFast: return "BAĞIL HIZ ÇOK YÜKSEK";
                case CenterUpgradeRejectReason.InsufficientCargo: return "YETERSİZ KAYNAK";
                default: return "YÜKSELTİLEMEDİ";
            }
        }

        /// <summary>Why a route could not be created, in the player's language.</summary>
        internal static string RouteRejectText(RouteCreateRejectReason reason)
        {
            switch (reason)
            {
                case RouteCreateRejectReason.None: return null;
                case RouteCreateRejectReason.CenterNotActive: return "MERKEZ AKTİF DEĞİL";
                case RouteCreateRejectReason.RouteCapReached: return "ROTA SINIRINA ULAŞILDI";
                case RouteCreateRejectReason.VehicleNotCollector: return "SEÇİLİ ARAÇ TOPLAYICI DEĞİL";
                case RouteCreateRejectReason.VehicleNotEligible: return "ARAÇ UYGUN DURUMDA DEĞİL";
                case RouteCreateRejectReason.VehicleAlreadyRouted: return "ARACIN ZATEN BİR ROTASI VAR";
                case RouteCreateRejectReason.DrillAlreadyRouted: return "SONDAJIN ZATEN BİR ROTASI VAR";
                case RouteCreateRejectReason.DrillNotActive: return "SONDAJ ETKİN DEĞİL";
                case RouteCreateRejectReason.NotSameSystem: return "AYNI SİSTEMDE DEĞİL";
                case RouteCreateRejectReason.NoCommandCapacity: return "KOMUTA KAPASİTESİ DOLU";
                default: return "ROTA KURULAMADI";
            }
        }
    }
}
