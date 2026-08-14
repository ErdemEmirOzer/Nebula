using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Progression;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.UI.Hud
{
    /// <summary>
    /// The two panels that only exist while the ship is physically somewhere.
    /// <para>
    /// Maintenance Reserve transfer and support-structure building are the last
    /// two legacy HUDs, and they are the two that could not simply become pages.
    /// The reserve is deliberately a physical transfer — the design forbids a
    /// teleporting menu-anywhere move — and support building needs a scanned
    /// planet under the ship. Putting either on a parent-menu page would quietly
    /// break that, so both live here, in context UI, appearing under exactly the
    /// conditions the old panels appeared under.
    /// </para>
    /// <para>
    /// Every rule still belongs to <see cref="ShipMaintenanceReserveAccess"/> and
    /// <see cref="ShipExtraction"/>; this asks and reports.
    /// </para>
    /// </summary>
    public sealed class NebulaHudPlanetContext : MonoBehaviour
    {
        private ShipMaintenanceReserveAccess reserve;
        private ShipExtraction extraction;

        private RectTransform reserveRoot;
        private NebulaUiFactory.LabelHandle reserveSummary, reserveMessage;
        private NebulaUiFactory.LabelHandle policyLabel;
        private NebulaUiFactory.ButtonHandle policyCycle;
        private readonly List<ReserveRow> reserveRows = new List<ReserveRow>();
        private string[] items;

        private RectTransform supportRoot;
        private NebulaUiFactory.LabelHandle supportTitle, supportCurrent;
        private NebulaUiFactory.ButtonHandle supportToggle, supportBuild;
        private readonly List<NebulaUiFactory.LabelHandle> supportGates = new List<NebulaUiFactory.LabelHandle>(3);

        private RectTransform defenseRoot;
        private NebulaUiFactory.LabelHandle defenseCurrent, defenseCost;
        private NebulaUiFactory.ButtonHandle defenseToggle, defenseBuild;

        private ShipQuickAssetPackage quickPackage;
        private RectTransform quickRoot;
        private NebulaUiFactory.LabelHandle quickStatus, quickMaterial;
        private NebulaUiFactory.ButtonHandle quickCycleMaterial, quickConfirm, quickCancel;

        private RectTransform commandRoot;
        private NebulaUiFactory.LabelHandle commandStatus, commandNode, commandTiming, commandMessage;
        private readonly List<CommandCostRow> commandCostRows = new List<CommandCostRow>(MaxCommandCostRows);
        private NebulaUiFactory.ButtonHandle commandAction;

        /// <summary>
        /// One cost item as three cells on one line: what it is, whether it is
        /// covered, and the four numbers under a shared header.
        /// <para>
        /// A column layout rather than a sentence per row because the card has to
        /// fit inside 190 logical pixels — the entire height left above the
        /// support card on an ultrawide at 140%, which is the smallest canvas the
        /// settings can produce. Four numbers spelled out per row needs 280px of
        /// a 190px budget and would ellipsise away exactly the part that answers
        /// "why is the button off".
        /// </para>
        /// </summary>
        private sealed class CommandCostRow
        {
            public NebulaUiFactory.LabelHandle Item;
            public NebulaUiFactory.LabelHandle Verdict;
            public NebulaUiFactory.LabelHandle Numbers;

            public void SetActive(bool active)
            {
                if (Item.Text.gameObject.activeSelf == active) return;
                Item.Text.gameObject.SetActive(active);
                Verdict.Text.gameObject.SetActive(active);
                Numbers.Text.gameObject.SetActive(active);
            }

            public bool Visible => Item.Text.gameObject.activeSelf;
        }

        /// <summary>
        /// The header the number column is read against. One line, once, instead
        /// of four labels per row.
        /// <para>
        /// Tight separators, unlike the rows below it: spaced out, the four words
        /// need 219px of a 196px column and the last one rendered as "AYRI…". The
        /// data keeps its spaces — a muted header can afford to be compact, a
        /// column of numbers cannot.
        /// </para>
        /// </summary>
        internal const string CommandCostHeader = "GEREK·VAR·DEPO·AYRILAN";

        /// <summary>Command T2 is the widest recipe in the game at three lines; nothing needs a fourth.</summary>
        private const int MaxCommandCostRows = 3;

        /// <summary>The control's object name. Tests click it by name, exactly as a player clicks it by sight.</summary>
        public const string CommandActionButtonName = "Command Action";

        private float refreshTimer;

        /// <summary>Test-facing: whether the reserve panel is up.</summary>
        public bool ReserveVisible => reserveRoot != null && reserveRoot.gameObject.activeSelf;

        /// <summary>Test-facing: whether the support-build panel is up.</summary>
        public bool SupportVisible => supportRoot != null && supportRoot.gameObject.activeSelf;

        /// <summary>Test-facing: the last refusal shown, or <c>null</c>.</summary>
        public string LastRejectShown { get; private set; }

        // ANI-05.4.3: acceptance is read from the access layer's own answer, never
        // from the button being pressed. A refusal — range, whitelist, stock or a
        // zero amount — returns a reason and flashes nothing.
        private readonly UiValueAnimator reserveDelta = new UiValueAnimator();

        /// <summary>Test-facing: accepted transfers actually shown.</summary>
        public int ReserveDeltasShown => reserveDelta.HighlightsShown;

        /// <summary>
        /// Acceptance read from the structure's own state changing, not from the
        /// click. A refused build leaves the tier where it was and flashes nothing.
        /// </summary>
        private readonly UiValueAnimator buildDelta = new UiValueAnimator();

        /// <summary>Test-facing: accepted builds actually shown.</summary>
        public int BuildAcceptancesShown => buildDelta.HighlightsShown;

        /// <summary>
        /// Acceptance for the Command control, read the same way: from the
        /// structure on the ground changing, never from the click landing. A
        /// refused build flashes nothing, and a second click on a capsule already
        /// in flight is a refusal.
        /// </summary>
        private readonly UiValueAnimator commandDelta = new UiValueAnimator();

        /// <summary>Test-facing: accepted Command builds/upgrades actually shown.</summary>
        public int CommandAcceptancesShown => commandDelta.HighlightsShown;

        private sealed class ReserveRow
        {
            public string ItemId;
            public NebulaUiFactory.LabelHandle Label;
        }

        public static NebulaHudPlanetContext Create(NebulaUiShell shell,
            ShipMaintenanceReserveAccess reserveAccess, ShipExtraction shipExtraction)
        {
            var go = new GameObject("HUD Planet Context");
            var panel = go.AddComponent<NebulaHudPlanetContext>();
            panel.reserve = reserveAccess;
            panel.extraction = shipExtraction;
            // Resolved off the ship this panel already holds, so the composition
            // installer needs no new argument. A sibling lookup on an object we were
            // handed -- never a scene-wide search.
            panel.quickPackage = shipExtraction != null
                ? shipExtraction.GetComponent<ShipQuickAssetPackage>() : null;
            panel.Build(shell);
            return panel;
        }

        private void Build(NebulaUiShell shell)
        {
            BuildReserve(shell);
            BuildSupport(shell);
            BuildCommand(shell);
            BuildDefense(shell);
            BuildQuickPackage(shell);
        }

        /// <summary>Test-facing: whether the surface-construction card is up.</summary>
        public bool CommandVisible => commandRoot != null && commandRoot.gameObject.activeSelf;

        /// <summary>Test-facing: the verb on the primary control.</summary>
        public string CommandCaption => commandAction?.Label?.Text != null ? commandAction.Label.Text.text : null;

        /// <summary>Test-facing: whether the primary control can be used right now.</summary>
        public bool CommandAvailable => commandAction != null && commandAction.Button.interactable;

        /// <summary>Test-facing: why it cannot, or <c>null</c>.</summary>
        public string CommandBlockedReason => commandAction?.DisabledReason;

        /// <summary>Test-facing: the state line the player reads.</summary>
        public string CommandStatusText => commandStatus?.Text != null ? commandStatus.Text.text : null;

        /// <summary>Test-facing: the Command node's own occupied/empty line.</summary>
        public string CommandNodeText => commandNode?.Text != null ? commandNode.Text.text : null;

        /// <summary>Test-facing: the feedback line, or <c>null</c> when nothing is being said.</summary>
        public string CommandMessageShown =>
            commandMessage?.Text != null && commandMessage.Text.gameObject.activeSelf
                ? commandMessage.Text.text : null;

        /// <summary>Test-facing: every cost line currently on the card, recipe and stock together.</summary>
        public IReadOnlyList<string> CommandCostLinesShown
        {
            get
            {
                var lines = new List<string>(MaxCommandCostRows);
                foreach (CommandCostRow row in commandCostRows)
                    if (row.Visible)
                        lines.Add($"{row.Item.Text.text} · {row.Verdict.Text.text} · {row.Numbers.Text.text}");
                return lines;
            }
        }

        // ---- reserve ---------------------------------------------------------

        private void BuildReserve(NebulaUiShell shell)
        {
            Image panel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudStatic), "Reserve",
                NebulaUiAssetCatalog.PanelElevated);
            reserveRoot = panel.rectTransform;
            // 280 -> 330: the reserve's own automatic-spending policy now has a row
            // under the six item rows. Nothing above it moves.
            NebulaUiFactory.Place(reserveRoot, new Vector2(0f, 0f), new Vector2(32f, 200f),
                new Vector2(420f, 330f));
            // The controller lives on the layer root, never inside the visual it
            // toggles: a widget parented into its own panel is switched off
            // together with it, its Update stops, and it can never decide to
            // come back. Hiding the panel must not disable the thing that shows it.
            transform.SetParent(shell.Root(UiLayer.HudStatic), false);

            NebulaUiFactory.Place(NebulaUiFactory.SectionTitle(reserveRoot, "Title", "BAKIM REZERVİ")
                .Text.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -16f), new Vector2(280f, 18f));

            reserveSummary = NebulaUiFactory.Label(reserveRoot, "Summary", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(reserveSummary.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -38f), new Vector2(384f, 16f));

            var itemList = new List<string> { ProcessingCatalog.BasicPartId };
            itemList.AddRange(ProcessingCatalog.MaterialIds);
            items = itemList.ToArray();

            for (int i = 0; i < items.Length; i++)
            {
                string itemId = items[i];
                float y = -62f - i * 32f;

                var row = new ReserveRow { ItemId = itemId };
                row.Label = NebulaUiFactory.Label(reserveRoot, itemId,
                    ProcessingCatalog.ItemDisplayName(itemId), NebulaUiDesignTokens.SizeCompact,
                    NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft);
                NebulaUiFactory.Place(row.Label.Text.rectTransform, new Vector2(0f, 1f),
                    new Vector2(18f, y), new Vector2(230f, 18f));
                reserveRows.Add(row);

                NebulaUiFactory.ButtonHandle withdraw = NebulaUiFactory.ButtonControl(reserveRoot,
                    itemId + " -", "−10", NebulaUiAssetCatalog.ButtonKind.Secondary,
                    () => Transfer(reserve?.TryWithdraw(itemId, ShipMaintenanceReserveAccess.DepositWithdrawIncrement)));
                NebulaUiFactory.Place(withdraw.Button.GetComponent<RectTransform>(),
                    new Vector2(1f, 1f), new Vector2(-96f, y + 4f), new Vector2(64f, 26f));

                NebulaUiFactory.ButtonHandle deposit = NebulaUiFactory.ButtonControl(reserveRoot,
                    itemId + " +", "+10", NebulaUiAssetCatalog.ButtonKind.Secondary,
                    () => Transfer(reserve?.TryDeposit(itemId, ShipMaintenanceReserveAccess.DepositWithdrawIncrement)));
                NebulaUiFactory.Place(deposit.Button.GetComponent<RectTransform>(),
                    new Vector2(1f, 1f), new Vector2(-22f, y + 4f), new Vector2(64f, 26f));
            }

            // VS-015 §5's automatic-spending policy. MaintenanceReserve.SetPolicy had
            // no caller anywhere under the interface -- only the save restore path --
            // so every reserve in the game stayed Manual, and the whole automatic
            // Engineer dispatch it drives (InterventionGroupDirector.TickMaintenancePolicies
            // and RemoteEconomyTick's remote equivalent) never ran once. This is the
            // panel that already owns the reserve, and the ship has to be physically
            // at the Command Center to see it, which is exactly the right gate.
            policyLabel = NebulaUiFactory.Label(reserveRoot, "Policy", "",
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(policyLabel.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -258f), new Vector2(230f, 18f));

            policyCycle = NebulaUiFactory.ButtonControl(reserveRoot, "Policy Cycle", "DEĞİŞTİR",
                NebulaUiAssetCatalog.ButtonKind.Secondary, CyclePolicy);
            NebulaUiFactory.Place(policyCycle.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 1f), new Vector2(-22f, -254f), new Vector2(138f, 26f));

            reserveMessage = NebulaUiFactory.Label(reserveRoot, "Message", "",
                NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Warning),
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(reserveMessage.Text.rectTransform, new Vector2(0f, 0f),
                new Vector2(18f, 14f), new Vector2(384f, 16f));
            reserveMessage.Text.gameObject.SetActive(false);

            reserveRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows the refusal when there is one. A rejected transfer that silently
        /// does nothing is the exact failure this redesign set out to remove.
        /// </summary>
        private void Transfer(ReserveTransferRejectReason? result)
        {
            if (result == null) return;

            if (result.Value == ReserveTransferRejectReason.None)
            {
                LastRejectShown = null;
                reserveDelta.RaiseDelta(ShipMaintenanceReserveAccess.DepositWithdrawIncrement);
                reserveMessage.Text.gameObject.SetActive(false);
                return;
            }

            LastRejectShown = ReserveRejectText(result.Value);
            reserveMessage.Value = LastRejectShown;
            reserveMessage.Text.gameObject.SetActive(true);
        }

        /// <summary>
        /// Advances the nearby Command Center's reserve policy through the three the
        /// enum really has. The policy is the reserve's own state — this asks it to
        /// change and then reads back what it became, never assuming the write took.
        /// </summary>
        private void CyclePolicy()
        {
            MaintenanceReserve store = reserve != null && reserve.InRange
                ? reserve.NearbyCommand?.MaintenanceReserve : null;
            if (store == null)
            {
                LastRejectShown = ReserveRejectText(ReserveTransferRejectReason.NotInRange);
                reserveMessage.Value = LastRejectShown;
                reserveMessage.Text.gameObject.SetActive(true);
                return;
            }

            store.SetPolicy(store.Policy switch
            {
                MaintenancePolicy.Manual => MaintenancePolicy.Emergency,
                MaintenancePolicy.Emergency => MaintenancePolicy.FullField,
                _ => MaintenancePolicy.Manual
            });
            LastRejectShown = null;
            reserveMessage.Text.gameObject.SetActive(false);
            RefreshReserve();
        }

        /// <summary>Test-facing: the reserve policy control, driven exactly as its own button drives it.</summary>
        public void CycleMaintenancePolicyForTest() => CyclePolicy();

        /// <summary>Test-facing: the policy line the player reads.</summary>
        public string MaintenancePolicyText => policyLabel?.Text != null ? policyLabel.Text.text : null;

        internal static string MaintenancePolicyName(MaintenancePolicy policy)
        {
            switch (policy)
            {
                case MaintenancePolicy.Emergency: return "ACİL";
                case MaintenancePolicy.FullField: return "TAM SAHA";
                default: return "ELLE";
            }
        }

        internal static string ReserveRejectText(ReserveTransferRejectReason reason)
        {
            switch (reason)
            {
                case ReserveTransferRejectReason.None: return null;
                case ReserveTransferRejectReason.NotInRange: return "KOMUTA MERKEZİNE YAKLAŞIN";
                case ReserveTransferRejectReason.ItemNotAccepted: return "BU MALZEME KABUL EDİLMİYOR";
                case ReserveTransferRejectReason.InsufficientAmount: return "YETERLİ MİKTAR YOK";
                case ReserveTransferRejectReason.ReserveFull: return "REZERV DOLU";
                default: return "AKTARIM REDDEDİLDİ";
            }
        }

        // ---- support build ---------------------------------------------------

        private void BuildSupport(NebulaUiShell shell)
        {
            Image panel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudStatic), "Support",
                NebulaUiAssetCatalog.PanelElevated);
            supportRoot = panel.rectTransform;
            NebulaUiFactory.Place(supportRoot, new Vector2(1f, 0f), new Vector2(-32f, 276f),
                new Vector2(420f, 190f));

            supportTitle = NebulaUiFactory.SectionTitle(supportRoot, "Title", "DESTEK YAPILARI");
            NebulaUiFactory.Place(supportTitle.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -16f), new Vector2(280f, 18f));

            supportToggle = NebulaUiFactory.ButtonControl(supportRoot, "Toggle", "",
                NebulaUiAssetCatalog.ButtonKind.Secondary, OnToggleSupport);
            RectTransform toggleRect = supportToggle.Button.GetComponent<RectTransform>();
            NebulaUiFactory.Place(toggleRect, new Vector2(0f, 1f),
                new Vector2(18f, -40f), new Vector2(384f, 30f));

            // The authored arrow, twice, instead of the "◀ ▶" characters that used
            // to sit in the caption. Those were font-dependent glyphs standing in
            // for art this project already owns -- exactly what L-11 is about.
            BuildCycleArrow(toggleRect, "CyclePrev", new Vector2(0f, 0.5f), new Vector2(12f, 0f), 180f);
            BuildCycleArrow(toggleRect, "CycleNext", new Vector2(1f, 0.5f), new Vector2(-12f, 0f), 0f);

            supportCurrent = NebulaUiFactory.Label(supportRoot, "Current", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(supportCurrent.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -78f), new Vector2(384f, 16f));

            for (int i = 0; i < 3; i++)
            {
                NebulaUiFactory.LabelHandle gate = NebulaUiFactory.Label(supportRoot, "Gate " + i, "",
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
                NebulaUiFactory.Place(gate.Text.rectTransform, new Vector2(0f, 1f),
                    new Vector2(18f + i * 130f, -100f), new Vector2(126f, 14f));
                supportGates.Add(gate);
            }

            supportBuild = NebulaUiFactory.ButtonControl(supportRoot, "Build", "İNŞA ET",
                NebulaUiAssetCatalog.ButtonKind.Primary, OnBuildSupport);
            NebulaUiFactory.Place(supportBuild.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(18f, 16f), new Vector2(384f, 36f));

            supportRoot.gameObject.SetActive(false);
        }

        private void OnToggleSupport()
        {
            extraction?.ToggleSupportNodeSelection();
            RefreshSupport();
        }

        private void OnBuildSupport()
        {
            if (extraction == null) return;

            int before = HighestSupportTier();
            extraction.BuildOrUpgradeSupport();
            if (HighestSupportTier() > before) buildDelta.RaiseDelta(1f);

            RefreshSupport();
        }

        /// <summary>The real tier on the ground; -1 when nothing is built.</summary>
        private static int HighestSupportTier()
        {
            int highest = -1;
            foreach (SurfaceStructure structure in ExtractionState.Structures)
                if (structure?.Runtime != null && structure.Runtime.Tier > highest)
                    highest = structure.Runtime.Tier;
            return highest;
        }

        // ---- surface construction (Command Center) ---------------------------

        /// <summary>
        /// The card that finally gives the Command Center a control.
        /// <para>
        /// The backend has worked since VS-003 and the only way to reach it was the
        /// raw K key, which nothing on screen mentioned and which the input gate
        /// switches off the moment a menu opens. Everything here — the cost, the
        /// three stock numbers, the material name, the timings, the refusal — comes
        /// from <see cref="ShipExtraction.AssessCommandAction"/>, and the button
        /// calls <see cref="ShipExtraction.TryBuildOrUpgradeCommand"/>. This class
        /// owns no rule of its own, and never calls <c>SurfaceStructure.Create</c>.
        /// </para>
        /// <para>
        /// It sits in context UI rather than on the Planets page deliberately: a
        /// Command Center is delivered by a capsule from the ship, so the ship has
        /// to be there. A page that let it be placed from across the system would
        /// be a different game.
        /// </para>
        /// </summary>
        private void BuildCommand(NebulaUiShell shell)
        {
            Image panel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudStatic), "Surface Construction",
                NebulaUiAssetCatalog.PanelElevated);
            commandRoot = panel.rectTransform;
            // Bottom LEFT, above the flight panel — not the right column beside the
            // support card, which is where this started.
            //
            // At 140% the logical canvas is only 771 tall (665 on an ultrawide), and
            // the right column already carries vitals to 258 and the support card to
            // 466. A card above that lands under the KARGO readout, which is on the
            // dynamic layer, sorts above this one, and is on screen permanently — so
            // the status line and the node line were covered whenever a player
            // enlarged the interface. Here the only thing it can meet is the
            // maintenance-reserve panel, and that appears solely when the ship is
            // parked at an already-active Command Center: the one moment this card
            // has nothing left to ask for.
            NebulaUiFactory.Place(commandRoot, new Vector2(0f, 0f), new Vector2(32f, CardBottom),
                new Vector2(420f, CardHeight));

            NebulaUiFactory.Place(NebulaUiFactory.SectionTitle(commandRoot, "Title", "YÜZEY İNŞASI")
                .Text.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -11f),
                new Vector2(160f, CaptionRowHeight));

            commandNode = NebulaUiFactory.Label(commandRoot, "Node", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleRight, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(commandNode.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-18f, -11f), new Vector2(210f, CaptionRowHeight));

            NebulaUiFactory.Place(NebulaUiFactory.Label(commandRoot, "Structure", CommandBuildText.StructureName,
                    NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                    TextAnchor.MiddleLeft, FontStyle.Bold).Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(18f, -27f), new Vector2(170f, CompactRowHeight));

            commandStatus = NebulaUiFactory.Label(commandRoot, "Status", "",
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                TextAnchor.MiddleRight, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(commandStatus.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-18f, -27f), new Vector2(200f, CompactRowHeight));

            NebulaUiFactory.Place(NebulaUiFactory.Label(commandRoot, "Cost Header", "MALZEME",
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption)
                .Text.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -45f),
                new Vector2(CostItemWidth, CaptionRowHeight));

            NebulaUiFactory.Place(NebulaUiFactory.Label(commandRoot, "Cost Header Numbers", CommandCostHeader,
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.MiddleRight, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption)
                .Text.rectTransform, new Vector2(1f, 1f), new Vector2(-18f, -45f),
                new Vector2(CostNumbersWidth, CaptionRowHeight));

            for (int i = 0; i < MaxCommandCostRows; i++) commandCostRows.Add(BuildCostRow(i));

            commandTiming = NebulaUiFactory.Label(commandRoot, "Timing", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(commandTiming.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -108f), new Vector2(384f, CaptionRowHeight));

            commandMessage = NebulaUiFactory.Label(commandRoot, "Message", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.Status(UiStatusLevel.Caution),
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(commandMessage.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -124f), new Vector2(384f, CaptionRowHeight));
            commandMessage.Text.gameObject.SetActive(false);

            commandAction = NebulaUiFactory.ButtonControl(commandRoot, CommandActionButtonName,
                "KUR · T1", NebulaUiAssetCatalog.ButtonKind.Primary, OnCommandAction);
            NebulaUiFactory.Place(commandAction.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(18f, 6f), new Vector2(384f, CommandButtonHeight));

            commandRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// The card's own geometry. Named because the layout test asserts against
        /// these exact values rather than against numbers retyped beside it.
        /// <para>
        /// The card sits above the flight panel, which ends at 162. Its top must
        /// still clear the smallest logical canvas the settings can produce — an
        /// ultrawide at 140%, which is 665 tall.
        /// </para>
        /// </summary>
        internal const float CardBottom = 172f;

        /// <inheritdoc cref="CardBottom"/>
        internal const float CardHeight = 180f;

        /// <summary>Comfortably over the 32px minimum hit target, at every supported scale.</summary>
        internal const float CommandButtonHeight = 32f;

        /// <summary>The number column's rect, shared by the header and every row so they line up.</summary>
        internal const float CostNumbersWidth = 196f;

        /// <summary>
        /// The item-name cell. Wide enough for "ENDÜSTRİYEL ELMAS", which is the
        /// longest of the six names this card can print — at the previous 126px a
        /// Diamond planet's own material was the one that got ellipsised.
        /// </summary>
        internal const float CostItemWidth = 136f;

        /// <summary>The verdict cell. "TAMAM"/"EKSİK" and nothing longer.</summary>
        internal const float CostVerdictWidth = 48f;

        /// <summary>
        /// The shortest rect a caption-size line may be given.
        /// <para>
        /// Not cosmetic. Every label in this interface is built with
        /// <c>TextOverflowModes.Ellipsis</c>, and TMP drops a line <b>entirely</b>
        /// when its line height does not fit the rect — it does not clip it, and
        /// it does not warn. At 12px the whole cost table, the timings and the
        /// refusal line rendered as nothing at all while every string assertion
        /// about them passed, because a test that reads <c>Text.text</c> cannot
        /// see what the screen did not draw.
        /// </para>
        /// </summary>
        internal const float CaptionRowHeight = 14f;

        /// <inheritdoc cref="CaptionRowHeight"/>
        internal const float CompactRowHeight = 16f;

        /// <summary>Top-to-top spacing between cost rows.</summary>
        internal const float CostRowPitch = 15f;

        /// <summary>Item name, verdict word, and the four numbers under the shared header.</summary>
        private CommandCostRow BuildCostRow(int index)
        {
            float top = -61f - index * CostRowPitch;
            var row = new CommandCostRow();

            row.Item = NebulaUiFactory.Label(commandRoot, "Cost " + index, "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkPrimary,
                TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(row.Item.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, top), new Vector2(CostItemWidth, CaptionRowHeight));

            row.Verdict = NebulaUiFactory.Label(commandRoot, "Cost Verdict " + index, "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.MiddleLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(row.Verdict.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(158f, top), new Vector2(CostVerdictWidth, CaptionRowHeight));

            row.Numbers = NebulaUiFactory.Label(commandRoot, "Cost Numbers " + index, "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.MiddleRight, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(row.Numbers.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-18f, top), new Vector2(CostNumbersWidth, CaptionRowHeight));

            return row;
        }

        /// <summary>
        /// The one thing the button does. Acceptance is decided by comparing the
        /// structure on the ground before and after, so a refusal — and a second
        /// click on a capsule that is already flying — flashes nothing and builds
        /// nothing.
        /// </summary>
        private void OnCommandAction()
        {
            if (extraction == null) return;

            (bool built, int tier, bool upgrading) before = CommandGroundState();
            extraction.TryBuildOrUpgradeCommand();
            (bool built, int tier, bool upgrading) after = CommandGroundState();

            if (after != before) commandDelta.RaiseDelta(1f);
            RefreshCommand();
        }

        /// <summary>What is really standing on this planet's Command node right now.</summary>
        private (bool built, int tier, bool upgrading) CommandGroundState()
        {
            if (extraction?.TargetPlanet == null) return (false, 0, false);
            SurfaceStructure command = ExtractionState.FindStructure(
                extraction.TargetPlanet.WorldId, SurfaceStructureType.Command);
            return command == null || command.Runtime == null
                ? (false, 0, false)
                : (true, command.Runtime.Tier, command.ActiveUpgradeJob != null);
        }

        private void RefreshCommand()
        {
            bool relevant = extraction != null && extraction.TargetPlanet != null;
            if (commandRoot.gameObject.activeSelf != relevant) commandRoot.gameObject.SetActive(relevant);
            if (!relevant) return;

            CommandBuildAssessment assessment = extraction.AssessCommandAction();

            commandStatus.Value = CommandBuildText.Status(assessment);
            commandStatus.Text.color = StatusInk(assessment);
            commandNode.Value = $"KOMUTA YUVASI · {CommandBuildText.NodeStatus(assessment)}";

            RefreshCommandCost(assessment);
            RefreshCommandTiming(assessment);

            commandAction.SetCaption(CommandBuildText.Caption(assessment));
            string blocked = CommandBlockedText(assessment);
            commandAction.SetAvailable(assessment.CanAct, blocked);

            RefreshCommandMessage(blocked);
        }

        /// <summary>
        /// Four numbers per line, because they are four different answers: what
        /// the recipe needs, what may actually be spent, what is physically in the
        /// hold, and how much of that is already promised elsewhere. "Insufficient"
        /// over a full hold is what reserved stock looks like when only the total
        /// is shown, and that reading cost this card its whole reason to exist.
        /// </summary>
        private void RefreshCommandCost(CommandBuildAssessment assessment)
        {
            IReadOnlyList<CommandCostLine> cost = assessment.Cost;
            for (int i = 0; i < commandCostRows.Count; i++)
            {
                CommandCostRow row = commandCostRows[i];
                bool used = i < cost.Count;
                row.SetActive(used);
                if (!used) continue;

                CommandCostLine line = cost[i];
                row.Item.Value = CommandBuildText.ItemName(line.ItemId);
                // The word, not just the colour: colour alone is not a channel a
                // colour-blind player can read, and this is the answer to "why is
                // the button off".
                row.Verdict.Value = line.Met ? "TAMAM" : "EKSİK";
                row.Verdict.Text.color = line.Met
                    ? NebulaUiDesignTokens.Status(UiStatusLevel.Working)
                    : NebulaUiDesignTokens.Status(UiStatusLevel.Caution);
                row.Numbers.Value = $"{line.Required:0} · {line.Available:0} · {line.Stored:0} · {line.Reserved:0}";
                row.Numbers.Text.color = line.Met
                    ? NebulaUiDesignTokens.InkSecondary
                    : NebulaUiDesignTokens.Status(UiStatusLevel.Caution);
            }

            // The withheld family material is stated as withheld rather than left
            // as a gap the player has to notice: the scan is what reveals it.
            if (assessment.MaterialHidden && cost.Count < commandCostRows.Count)
            {
                CommandCostRow row = commandCostRows[cost.Count];
                row.SetActive(true);
                row.Item.Value = "GEZEGEN MALZEMESİ";
                row.Verdict.Value = "TARA";
                row.Verdict.Text.color = NebulaUiDesignTokens.InkMuted;
                row.Numbers.Value = "? · ? · ? · ?";
                row.Numbers.Text.color = NebulaUiDesignTokens.InkMuted;
            }
        }

        private void RefreshCommandTiming(CommandBuildAssessment assessment)
        {
            if (assessment.Action == CommandActionKind.BuildT1)
            {
                commandTiming.Value = $"TESLİMAT {assessment.DeliverySeconds:0} sn  ·  YAPIM {assessment.WorkSeconds:0} sn";
                return;
            }

            commandTiming.Value = assessment.WorkSeconds > 0f
                ? $"YÜKSELTME {assessment.WorkSeconds:0} sn"
                : string.Empty;
        }

        /// <summary>
        /// Why the control is off, with the numbers that make a distance refusal
        /// actionable. "Move closer" without saying how much closer is a refusal
        /// the player cannot act on.
        /// </summary>
        private static string CommandBlockedText(CommandBuildAssessment assessment)
        {
            string reason = CommandBuildText.BlockedReason(assessment);
            if (reason == null) return null;
            return assessment.BuildReject == CommandBuildRejectReason.OutOfRange
                ? $"{reason} (YÜKSEKLİK {assessment.Altitude:0} · AZAMİ {assessment.RangeLimit:0})"
                : reason;
        }

        /// <summary>
        /// The feedback line. What just happened wins over what is still missing
        /// for as long as the ship's own feedback window lasts, so a successful
        /// launch is not immediately overwritten by the next thing that is short.
        /// </summary>
        private void RefreshCommandMessage(string blocked)
        {
            CommandActionOutcome outcome = extraction.CommandFeedback;
            string text = outcome.HasValue ? CommandBuildText.OutcomeText(outcome) : blocked;
            bool success = outcome.HasValue && outcome.Succeeded;

            bool show = !string.IsNullOrEmpty(text);
            if (commandMessage.Text.gameObject.activeSelf != show)
                commandMessage.Text.gameObject.SetActive(show);
            if (!show) return;

            commandMessage.Value = text;
            commandMessage.Text.color = success
                ? NebulaUiDesignTokens.Status(UiStatusLevel.Working)
                : NebulaUiDesignTokens.Status(UiStatusLevel.Caution);
        }

        private static Color StatusInk(CommandBuildAssessment assessment)
        {
            if (!assessment.HasStructure) return NebulaUiDesignTokens.InkMuted;
            if (assessment.State == DrillLifecycle.Disabled || assessment.State == DrillLifecycle.Damaged)
                return NebulaUiDesignTokens.Status(UiStatusLevel.Critical);
            return assessment.State == DrillLifecycle.Active
                ? NebulaUiDesignTokens.Status(UiStatusLevel.Nominal)
                : NebulaUiDesignTokens.Status(UiStatusLevel.Working);
        }

        // ---- refresh ---------------------------------------------------------

        /// <summary>
        /// Unscaled, deliberately. On scaled time this panel stopped refreshing
        /// entirely whenever the world was paused: the first frame set the timer
        /// to 0.2 and no subsequent frame ever subtracted anything from it, so a
        /// refusal raised just before a pause stayed on screen unchanged and a
        /// state that moved during the pause was never drawn. A readout is a
        /// readout whether or not the simulation is running.
        /// </summary>
        private void Update()
        {
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = 0.2f;

            RefreshReserve();
            RefreshSupport();
            RefreshCommand();
            RefreshDefense();
            RefreshQuickPackage();
        }

        private void RefreshReserve()
        {
            bool relevant = reserve != null && reserve.InRange
                            && reserve.NearbyCommand?.MaintenanceReserve != null;
            if (reserveRoot.gameObject.activeSelf != relevant) reserveRoot.gameObject.SetActive(relevant);
            if (!relevant) return;

            MaintenanceReserve store = reserve.NearbyCommand.MaintenanceReserve;
            reserveSummary.Value = $"{store.Cargo.UsedVolume:0} / {store.Cargo.Capacity:0}  ·  " +
                                   $"BOŞ {store.Cargo.AvailableVolume:0}";

            for (int i = 0; i < reserveRows.Count; i++)
            {
                ReserveRow row = reserveRows[i];
                float stored = store.Cargo.GetStored(row.ItemId);
                float reserved = store.Cargo.GetReserved(row.ItemId);

                // Reserved is not the same as stored: it is spoken for by a job, and
                // withdrawing it will be refused. Saying so beats a silent refusal.
                // Resolved into a local first so the item id never appears inside a
                // player-facing literal. The hygiene check cannot tell a safe
                // wrapper from a raw one, and a rule needing special cases is not
                // a rule.
                string itemName = ProcessingCatalog.ItemDisplayName(row.ItemId);
                row.Label.Value = reserved > 0.01f
                    ? $"{itemName}  {stored:0}  (AYRILAN {reserved:0})"
                    : $"{itemName}  {stored:0}";
            }

            policyLabel.Value = $"POLİTİKA: {MaintenancePolicyName(store.Policy)}";
        }

        private void RefreshSupport()
        {
            bool relevant = extraction != null && extraction.TargetPlanet != null
                            && extraction.TargetProfile != null && extraction.TargetProfile.Scanned;
            if (supportRoot.gameObject.activeSelf != relevant) supportRoot.gameObject.SetActive(relevant);
            if (!relevant) return;

            int index = extraction.SelectedSupportNodeIndex;
            SurfaceStructureType type = index == 0
                ? SurfaceStructureType.SensorArray : SurfaceStructureType.MaintenancePlatform;
            supportToggle.SetCaption(index == 0 ? "SENSÖR DİZİSİ" : "BAKIM PLATFORMU");

            SurfaceStructure existing = extraction.FindSupportStructure(index);
            StructureTierDef nextTier = existing == null
                ? PlanetaryDefenseCatalog.Get(type, 1)
                : (existing.Runtime.Tier < 2 ? PlanetaryDefenseCatalog.Get(type, 2) : null);

            supportCurrent.Value = existing == null
                ? "MEVCUT: İNŞA EDİLMEDİ"
                : $"MEVCUT: T{existing.Runtime.Tier}";

            if (nextTier == null)
            {
                for (int i = 0; i < supportGates.Count; i++) supportGates[i].Value = string.Empty;
                supportBuild.SetCaption("AZAMİ SEVİYEDE");
                supportBuild.SetAvailable(false, "AZAMİ SEVİYEDE");
                return;
            }

            bool levelOk = ProgressionService.State.Level >= nextTier.RequiredCharacterLevel;
            bool commandOk = extraction.CommandTierOnTarget() >= nextTier.RequiredCommandTier;
            bool specialOk = type == SurfaceStructureType.SensorArray
                ? extraction.HasForeignEntityNearby()
                : extraction.HasEngineerInSystem();
            bool resourcesOk = extraction.CanAfford(nextTier, out string resourceReason);
            string specialLabel = type == SurfaceStructureType.SensorArray ? "YABANCI VARLIK" : "MÜHENDİS";

            var costText = new System.Text.StringBuilder("MALİYET: ");
            List<(string itemId, float amount)> cost = nextTier.FullCost();
            for (int i = 0; i < cost.Count; i++)
            {
                if (i > 0) costText.Append(" + ");
                costText.Append(cost[i].amount.ToString("0")).Append(' ')
                    .Append(ProcessingCatalog.ItemDisplayName(cost[i].itemId));
            }
            supportCurrent.Value += " · " + costText;

            // Each gate states itself and shows whether it is met. The old panel
            // printed "[OK]" and "[--]" as literal text for exactly this.
            SetGate(supportGates[0], $"SEVİYE {nextTier.RequiredCharacterLevel}", levelOk);
            SetGate(supportGates[1], $"KOMUTA T{nextTier.RequiredCommandTier}", commandOk);
            SetGate(supportGates[2], specialLabel, specialOk);

            supportBuild.SetCaption(existing == null ? $"İNŞA ET · T{nextTier.Tier}" : $"YÜKSELT · T{nextTier.Tier}");

            string blocked = !levelOk ? $"SEVİYE {nextTier.RequiredCharacterLevel} GEREKLİ"
                : !commandOk ? $"KOMUTA T{nextTier.RequiredCommandTier} GEREKLİ"
                : !specialOk ? $"{specialLabel} GEREKLİ"
                : !resourcesOk ? resourceReason
                : null;
            supportBuild.SetAvailable(blocked == null, blocked);
        }

        /// <summary>
        /// Met or not, in a word and a colour. It used to be "✓"/"✕", which are
        /// font-dependent characters rather than the project's own iconography, and
        /// a font without them prints a box where the answer should be.
        /// </summary>
        private static void BuildCycleArrow(RectTransform parent, string name,
            Vector2 anchor, Vector2 offset, float rotation)
        {
            Image arrow = NebulaUiFactory.Icon(parent, name, NebulaUiAssetCatalog.RouteArrow, 12f);
            NebulaUiFactory.Place(arrow.rectTransform, anchor, offset, new Vector2(12f, 12f));
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            arrow.color = NebulaUiDesignTokens.InkMuted;
        }

        private static void SetGate(NebulaUiFactory.LabelHandle gate, string label, bool met)
        {
            gate.Value = met ? $"{label} · HAZIR" : $"{label} · EKSİK";
            gate.Text.color = met
                ? NebulaUiDesignTokens.Status(UiStatusLevel.Working)
                : NebulaUiDesignTokens.Status(UiStatusLevel.Caution);
        }

        // ---- surface defense node -------------------------------------------

        /// <summary>The node stepper's object name. Tests click it by name, exactly as a player clicks it by sight.</summary>
        public const string DefenseToggleButtonName = "Defense Node";

        /// <summary>The build/upgrade control's object name.</summary>
        public const string DefenseBuildButtonName = "Defense Build";

        /// <summary>
        /// Which surface-defense node the build command means, and the command itself.
        /// <para>
        /// The selection was reachable only by pressing <c>[</c> or <c>]</c> inside
        /// build mode, and <c>SelectedDefenseNodeIndex</c> had a private setter, so no
        /// control could exist for it at any price: a player who never discovered the
        /// bracket keys could build a Laser and could never build a Plasma, because
        /// there was no way to say which node the verb applied to. The stepper is the
        /// same authored control the support card uses, with the same authored arrows,
        /// and it drives <see cref="ShipExtraction.StepDefenseNode"/> — the identical
        /// seam the two keys now reach through the router.
        /// </para>
        /// </summary>
        private void BuildDefense(NebulaUiShell shell)
        {
            Image panel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudStatic), "Defense",
                NebulaUiAssetCatalog.PanelElevated);
            defenseRoot = panel.rectTransform;
            // Directly above the support card, which ends at 466. 120 tall keeps the
            // top at 596, inside the 665 an ultrawide at 140% produces.
            NebulaUiFactory.Place(defenseRoot, new Vector2(1f, 0f), new Vector2(-32f, 476f),
                new Vector2(420f, 120f));

            NebulaUiFactory.Place(NebulaUiFactory.SectionTitle(defenseRoot, "Title", "SAVUNMA DÜĞÜMÜ")
                .Text.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(280f, 18f));

            defenseToggle = NebulaUiFactory.ButtonControl(defenseRoot, DefenseToggleButtonName, "",
                NebulaUiAssetCatalog.ButtonKind.Secondary, OnStepDefenseNode);
            RectTransform toggleRect = defenseToggle.Button.GetComponent<RectTransform>();
            NebulaUiFactory.Place(toggleRect, new Vector2(0f, 1f),
                new Vector2(18f, -36f), new Vector2(384f, 28f));
            BuildCycleArrow(toggleRect, "CyclePrev", new Vector2(0f, 0.5f), new Vector2(12f, 0f), 180f);
            BuildCycleArrow(toggleRect, "CycleNext", new Vector2(1f, 0.5f), new Vector2(-12f, 0f), 0f);

            defenseCurrent = NebulaUiFactory.Label(defenseRoot, "Current", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(defenseCurrent.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -68f), new Vector2(190f, 14f));

            defenseCost = NebulaUiFactory.Label(defenseRoot, "Cost", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleRight, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(defenseCost.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-18f, -68f), new Vector2(190f, 14f));

            defenseBuild = NebulaUiFactory.ButtonControl(defenseRoot, DefenseBuildButtonName, "KUR",
                NebulaUiAssetCatalog.ButtonKind.Primary, OnBuildDefense);
            NebulaUiFactory.Place(defenseBuild.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(18f, 8f), new Vector2(384f, CommandButtonHeight));

            defenseRoot.gameObject.SetActive(false);
        }

        private void OnStepDefenseNode()
        {
            extraction?.StepDefenseNode(1);
            RefreshDefense();
        }

        private void OnBuildDefense()
        {
            extraction?.TryBuildOrUpgradeDefense();
            RefreshDefense();
        }

        /// <summary>Test-facing: whether the defense-node card is up.</summary>
        public bool DefenseVisible => defenseRoot != null && defenseRoot.gameObject.activeSelf;

        /// <summary>Test-facing: which node the card currently names.</summary>
        public string DefenseNodeCaption =>
            defenseToggle?.Label.Text != null ? defenseToggle.Label.Text.text : null;

        /// <summary>Test-facing: why the build control cannot be used, or <c>null</c>.</summary>
        public string DefenseBlockedReason => defenseBuild?.DisabledReason;

        private void RefreshDefense()
        {
            bool relevant = extraction != null && extraction.TargetPlanet != null
                            && extraction.TargetProfile != null && extraction.TargetProfile.Scanned;
            if (defenseRoot.gameObject.activeSelf != relevant) defenseRoot.gameObject.SetActive(relevant);
            if (!relevant) return;

            int index = extraction.SelectedDefenseNodeIndex;
            SurfaceStructureType type = index == 0
                ? SurfaceStructureType.Laser : SurfaceStructureType.Plasma;
            defenseToggle.SetCaption(index == 0 ? "LAZER DÜĞÜMÜ" : "PLAZMA DÜĞÜMÜ");

            SurfaceStructure existing =
                ExtractionState.FindStructure(extraction.TargetPlanet.WorldId, type);
            StructureTierDef next = existing == null
                ? PlanetaryDefenseCatalog.Get(type, 1)
                : (existing.Runtime.Tier < 2 ? PlanetaryDefenseCatalog.Get(type, 2) : null);

            defenseCurrent.Value = existing == null
                ? "MEVCUT: İNŞA EDİLMEDİ"
                : $"MEVCUT: T{existing.Runtime.Tier}";

            if (next == null)
            {
                defenseCost.Value = string.Empty;
                defenseBuild.SetCaption("AZAMİ SEVİYEDE");
                defenseBuild.SetAvailable(false, "AZAMİ SEVİYEDE");
                return;
            }

            defenseBuild.SetCaption(existing == null ? "KUR · T1" : "T2'YE YÜKSELT");
            defenseCost.Value = CostSummary(next);

            // Only the shared "near a scanned, unlocked planet" gate is answered here,
            // because it is the only one this card can answer without forming a second
            // opinion. Everything past it — the Command tier, Weapon Coordination, the
            // material — is the seam's to refuse, and it says so on the alert strip in
            // the same words the key produces.
            string blocked = extraction.BuildEligibleNow(out string reason) ? null : reason;
            if (blocked == null && !extraction.CanAfford(next, out string missing)) blocked = missing;
            defenseBuild.SetAvailable(blocked == null, blocked);
        }

        private static string CostSummary(StructureTierDef tier)
        {
            var text = new System.Text.StringBuilder(48);
            List<(string itemId, float amount)> cost = tier.FullCost();
            for (int i = 0; i < cost.Count; i++)
            {
                if (i > 0) text.Append(" + ");
                text.Append(cost[i].amount.ToString("0")).Append(' ')
                    .Append(NebulaText.Upper(ProcessingCatalog.ItemDisplayName(cost[i].itemId)));
            }
            return text.ToString();
        }

        // ---- Quick Asset Package --------------------------------------------

        /// <summary>The confirm/retry control's object name.</summary>
        public const string QuickConfirmButtonName = "Quick Package Confirm";

        /// <summary>The cancel control's object name.</summary>
        public const string QuickCancelButtonName = "Quick Package Cancel";

        /// <summary>The Laser-material stepper's object name.</summary>
        public const string QuickMaterialButtonName = "Quick Package Material";

        /// <summary>
        /// The Quick Asset Package's first interface.
        /// <para>
        /// A grep for the type under <c>Assets/Scripts/Nebula/UI</c> returned nothing
        /// at all: the whole feature — an atomic three-capsule confirm, a staggered
        /// send, a retry for a held-back capsule, a cancel with a 75%/100% split refund
        /// and a player-chosen Laser material — was reachable only by pressing P, U and
        /// I, none of which appeared anywhere on screen. Every control here calls the
        /// component's own public seam, the same one the three keys reach through the
        /// router, and the confirm control's disabled reason is
        /// <c>CanConfirm</c>'s own typed answer rather than a second guess at it.
        /// </para>
        /// </summary>
        private void BuildQuickPackage(NebulaUiShell shell)
        {
            Image panel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudStatic), "Quick Package",
                NebulaUiAssetCatalog.PanelElevated);
            quickRoot = panel.rectTransform;
            // Left column, above the maintenance reserve (which tops out at 530).
            NebulaUiFactory.Place(quickRoot, new Vector2(0f, 0f), new Vector2(32f, 540f),
                new Vector2(420f, 120f));

            NebulaUiFactory.Place(NebulaUiFactory.SectionTitle(quickRoot, "Title", "HIZLI KURULUM PAKETİ")
                .Text.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(280f, 18f));

            quickStatus = NebulaUiFactory.Label(quickRoot, "Status", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.MiddleRight, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(quickStatus.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-18f, -14f), new Vector2(190f, 18f));

            quickMaterial = NebulaUiFactory.Label(quickRoot, "Material", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(quickMaterial.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, -36f), new Vector2(190f, 14f));

            quickCycleMaterial = NebulaUiFactory.ButtonControl(quickRoot, QuickMaterialButtonName,
                "MALZEMEYİ DEĞİŞTİR", NebulaUiAssetCatalog.ButtonKind.Secondary, OnCycleQuickMaterial);
            NebulaUiFactory.Place(quickCycleMaterial.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 1f), new Vector2(-18f, -34f), new Vector2(190f, 26f));

            quickConfirm = NebulaUiFactory.ButtonControl(quickRoot, QuickConfirmButtonName,
                "ONAYLA", NebulaUiAssetCatalog.ButtonKind.Primary, OnQuickConfirm);
            NebulaUiFactory.Place(quickConfirm.Button.GetComponent<RectTransform>(),
                new Vector2(0f, 0f), new Vector2(18f, 8f), new Vector2(190f, CommandButtonHeight));

            quickCancel = NebulaUiFactory.ButtonControl(quickRoot, QuickCancelButtonName,
                "İPTAL ET", NebulaUiAssetCatalog.ButtonKind.Secondary, OnQuickCancel);
            NebulaUiFactory.Place(quickCancel.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(-18f, 8f), new Vector2(190f, CommandButtonHeight));

            quickRoot.gameObject.SetActive(false);
        }

        private void OnQuickConfirm()
        {
            quickPackage?.ConfirmOrRetry();
            RefreshQuickPackage();
        }

        private void OnQuickCancel()
        {
            quickPackage?.Cancel();
            RefreshQuickPackage();
        }

        private void OnCycleQuickMaterial()
        {
            quickPackage?.CycleLaserMaterial();
            RefreshQuickPackage();
        }

        /// <summary>Test-facing: whether the package card is up.</summary>
        public bool QuickPackageVisible => quickRoot != null && quickRoot.gameObject.activeSelf;

        /// <summary>Test-facing: why confirm cannot be used, or <c>null</c>.</summary>
        public string QuickConfirmBlockedReason => quickConfirm?.DisabledReason;

        /// <summary>Test-facing: the Laser material the next confirm would use, as the player reads it.</summary>
        public string QuickMaterialText => quickMaterial?.Text != null ? quickMaterial.Text.text : null;

        private void RefreshQuickPackage()
        {
            bool relevant = quickPackage != null && extraction != null
                            && extraction.TargetPlanet != null
                            && extraction.TargetProfile != null && extraction.TargetProfile.Scanned;
            if (quickRoot.gameObject.activeSelf != relevant) quickRoot.gameObject.SetActive(relevant);
            if (!relevant) return;

            bool live = quickPackage.CancelAvailable;
            quickStatus.Value = live ? "PAKET SÜRÜYOR" : "PAKET YOK";
            quickStatus.Text.color = live
                ? NebulaUiDesignTokens.Status(UiStatusLevel.Working)
                : NebulaUiDesignTokens.InkSecondary;

            string materialId = quickPackage.PendingLaserMaterialId;
            quickMaterial.Value = "LAZER MALZEMESİ: " +
                                  NebulaText.Upper(ProcessingCatalog.ItemDisplayName(materialId));

            // A live package has already reserved its material, so changing the choice
            // now would change nothing about what is in flight and everything about
            // what the label claims. Off, with the reason.
            quickCycleMaterial.SetAvailable(!live, "PAKET SÜRERKEN DEĞİŞTİRİLEMEZ");

            quickConfirm.SetCaption(live ? "YENİDEN DENE" : "ONAYLA");
            if (live)
            {
                quickConfirm.SetAvailable(true, null);
            }
            else
            {
                bool can = quickPackage.CanConfirm(out QuickPackageRejectReason reason, materialId);
                quickConfirm.SetAvailable(can, can ? null : ShipQuickAssetPackage.RejectMessage(reason));
            }

            quickCancel.SetAvailable(live, "ETKİN PAKET YOK");
        }
    }
}
