using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Accessibility;
using Nebula.UI.Bench;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.UI.Menu
{
    /// <summary>
    /// The hold, the processing bench and the conversions the bench can run.
    /// <para>
    /// The page this replaces drew a timed bay: slots, a progress bar, a remaining
    /// count and a cancel control. None of those describe what processing is any
    /// more. A conversion is a single transaction that finishes the moment it is
    /// confirmed, so a bar that fills would be an animation pretending to be a
    /// mechanic, and a "cancel" would offer to stop something that has already
    /// happened.
    /// </para>
    /// <para>
    /// What replaced it is a bench the player loads by hand. Input is <i>moved</i>
    /// out of the hold and onto the bench rather than reserved inside it, which is
    /// the difference that makes this page truthful: the hold's number and the
    /// bench's number are disjoint, so there is no state where two places disagree
    /// about what the player has.
    /// </para>
    /// <para>
    /// Every command goes through <see cref="ProcessingBenchCommands"/>. The page
    /// owns no inventory, no bench amount, no recipe table and no timer: it reads
    /// <see cref="LogisticsCargo"/>, <see cref="ProcessingBenchRuntime"/> and
    /// <see cref="ProcessingCatalog"/>, and asks the validators what they would say.
    /// A refusal shown here is the refusal the backend gives, word for word from
    /// <see cref="NebulaBenchMessages"/>.
    /// </para>
    /// <para>
    /// The drag path and the keyboard path are the same path. Dropping a stack on
    /// the bench and pressing <c>TEZGÂHA EKLE</c> on a row both call
    /// <see cref="RequestDeposit"/>, which opens one modal and hands one number to
    /// one command. Two entry points writing their own transaction is exactly how
    /// a hold and a bench end up disagreeing.
    /// </para>
    /// <para>
    /// Rows and cards are built once from fixed catalogs and only ever repainted,
    /// so a refresh allocates nothing and nothing reshuffles under the cursor.
    /// </para>
    /// </summary>
    public sealed class NebulaResourcesPage : INebulaMenuPage, INebulaFocusablePage, INebulaBenchDragHost
    {
        public NebulaUiAssetCatalog.Page Page => NebulaUiAssetCatalog.Page.Resources;
        public string Title => "Kaynaklar";

        /// <summary>How long a result stays on screen. Unscaled — the menu holds the game at zero.</summary>
        private const float FeedbackSeconds = 5f;

        /// <summary>How long a changed amount keeps its delta marker.</summary>
        private const float DeltaSeconds = 4.5f;

        /// <summary>
        /// How long the bench keeps its "that just happened" emphasis.
        /// <para>
        /// Presentation only, and deliberately after the fact: the transaction has
        /// already been applied and the hold already repainted by the time this is
        /// set, so nothing on screen is waiting for it to finish. Under Motion Off
        /// the shared pulse holds at full and the emphasis is a steady colour rather
        /// than no emphasis at all.
        /// </para>
        /// </summary>
        private const float FlashSeconds = 0.9f;

        /// <summary>
        /// The three markers that carry state without colour.
        /// <para>
        /// ASCII, deliberately. An earlier cut used ▸, ✔ and ✕ and the shipped SDF
        /// atlas has none of them, so every one rendered as a tofu box and the whole
        /// point of not relying on colour was defeated by the glyphs meant to
        /// replace it. Anything the interface leans on for meaning has to exist in
        /// the face it is actually drawn with.
        /// </para>
        /// </summary>
        private const string ChosenMark = "> ";

        /// <inheritdoc cref="ChosenMark"/>
        private const string ReadyMark = "[+] ";

        /// <inheritdoc cref="ChosenMark"/>
        private const string ShortMark = "[!] ";

        /// <summary>How wide a row's own action control is, and how far it sits from the edge.</summary>
        private const float AddWidth = 122f;

        /// <inheritdoc cref="AddWidth"/>
        private const float AddGutter = 10f;

        /// <inheritdoc cref="AddWidth"/>
        private const float ConvertWidth = 152f;

        private const float CargoRowHeight = 52f;
        private const float RecipeCardHeight = 108f;

        private readonly NebulaContext context;

        // ---- cargo column ----
        private NebulaUiFactory.BarHandle cargoBar;
        private NebulaUiFactory.LabelHandle cargoValue, cargoWarning, itemsEmpty;
        private readonly Dictionary<string, ItemRow> itemRows = new Dictionary<string, ItemRow>();
        private readonly List<ItemRow> itemOrder = new List<ItemRow>();

        // ---- bench column ----
        private Image benchZone;
        private Image benchIcon;
        private NebulaUiFactory.LabelHandle benchHint, benchName, benchAmountLabel, benchDrop, benchLegacy;
        private NebulaUiFactory.ButtonHandle benchAdd, benchWithdraw, benchWithdrawAll;
        private NebulaUiFactory.LabelHandle feedback;

        // ---- recipe column ----
        private readonly List<RecipeCard> recipeCards = new List<RecipeCard>();
        private NebulaUiFactory.LabelHandle recipesEmpty;

        // ---- shared ----
        private NebulaQuantityModal modal;
        private NebulaDragGhost ghost;
        private RectTransform host;

        private readonly UiEmphasisPulse deltaPulse = new UiEmphasisPulse();
        private float lastRefreshUnscaled;
        private float feedbackUntil;
        private float benchFlashUntil;

        /// <summary>
        /// The stack currently being carried, or <c>null</c>.
        /// <para>
        /// This is the gesture, not the inventory: it is cleared the instant a drop
        /// is taken, which is what makes a second <c>OnDrop</c> for the same drag a
        /// no-op rather than a second question over the same bench.
        /// </para>
        /// </summary>
        private string dragItemId;

        /// <summary>Where the pointer is relative to the bench, for the highlight only.</summary>
        public enum DropState
        {
            /// <summary>No drag, or the pointer is not over the bench.</summary>
            Idle,

            /// <summary>The bench would take this stack.</summary>
            Valid,

            /// <summary>The bench would refuse it, and <see cref="DropMessage"/> says why.</summary>
            Invalid,
        }

        // =====================================================================
        // TEST-FACING STATE
        // =====================================================================

        /// <summary>What is on the bench, read from the backend. The page keeps no copy.</summary>
        public string BenchItemId => Bench()?.InputItemId;

        /// <inheritdoc cref="BenchItemId"/>
        public int BenchAmount => Bench()?.InputAmount ?? 0;

        /// <summary>The recipe the convert control acts on, as a stable catalog index, or -1.</summary>
        public int SelectedRecipe { get; private set; } = -1;

        public BenchDepositReason LastDepositResult { get; private set; } = BenchDepositReason.None;
        public BenchWithdrawReason LastWithdrawResult { get; private set; } = BenchWithdrawReason.None;
        public BenchConvertReason LastConvertResult { get; private set; } = BenchConvertReason.None;

        /// <summary>How many deposits this page has actually applied.</summary>
        public int DepositsIssued { get; private set; }

        /// <inheritdoc cref="DepositsIssued"/>
        public int WithdrawalsIssued { get; private set; }

        /// <inheritdoc cref="DepositsIssued"/>
        public int ConversionsIssued { get; private set; }

        /// <summary>
        /// How many success cues this page has asked the mixer for. Counted here as
        /// well as played, so a fixture can prove a refusal is silent without a
        /// listener.
        /// </summary>
        public int SuccessCuesPlayed { get; private set; }

        /// <summary>The message under the bench, or empty.</summary>
        public string FeedbackText => feedback == null ? string.Empty : feedback.Value;

        /// <summary>The last applied change, in the delta form the bench shows.</summary>
        public string LastDeltaText { get; private set; } = string.Empty;

        /// <summary>The reusable quantity box. Never opens itself; the page opens it.</summary>
        public NebulaQuantityModal Modal => modal;

        /// <summary>The single pooled drag ghost.</summary>
        public NebulaDragGhost DragGhost => ghost;

        /// <summary>True while a stack is being carried.</summary>
        public bool IsDragging => dragItemId != null;

        /// <summary>What the bench is saying about the stack over it.</summary>
        public DropState DropHighlight { get; private set; } = DropState.Idle;

        /// <summary>Why a drop would be refused, or empty. Never colour alone.</summary>
        public string DropMessage => benchDrop == null ? string.Empty : benchDrop.Value;

        /// <summary>The empty-bench instruction, or empty when the bench is loaded.</summary>
        public string BenchHintText =>
            benchHint != null && benchHint.Text.gameObject.activeSelf ? benchHint.Value : string.Empty;

        /// <summary>What the bench says it is holding.</summary>
        public string BenchAmountText => benchAmountLabel == null ? string.Empty : benchAmountLabel.Value;

        /// <summary>The v14 migration notice, or empty when nothing is owed.</summary>
        public string LegacyClaimText =>
            benchLegacy != null && benchLegacy.Text.gameObject.activeSelf ? benchLegacy.Value : string.Empty;

        /// <summary>True while the bench is drawing its post-transaction emphasis.</summary>
        public bool ConversionFlashActive => Time.unscaledTime < benchFlashUntil;

        /// <summary>Item rows currently holding something.</summary>
        public int VisibleItemRows { get; private set; }

        /// <summary>Recipe cards on screen. Derived from the bench's item, never stored.</summary>
        public int VisibleRecipeCards { get; private set; }

        /// <summary>Cards built. The catalog is fixed, so this never changes after Build.</summary>
        public int RecipeCardCount => recipeCards.Count;

        /// <summary>Over-capacity cargo, a migration claim still owed, and a bench ready to convert.</summary>
        public int AttentionCount { get; private set; }

        public Button CargoRowButton(string itemId) =>
            itemRows.TryGetValue(itemId, out ItemRow row) ? row.Select : null;

        public Button CargoAddButton(string itemId) =>
            itemRows.TryGetValue(itemId, out ItemRow row) ? row.Add?.Button : null;

        /// <summary>Whether a cargo row is offering its keyboard path to the bench.</summary>
        public bool CargoAddVisible(string itemId) =>
            itemRows.TryGetValue(itemId, out ItemRow row)
            && row.Add != null && row.Add.Button.gameObject.activeSelf;

        public string CargoAmountText(string itemId) =>
            itemRows.TryGetValue(itemId, out ItemRow row) ? row.Amounts.Value : null;

        public string CargoDeltaText(string itemId) =>
            itemRows.TryGetValue(itemId, out ItemRow row)
            && row.Delta.Text.gameObject.activeSelf ? row.Delta.Value : string.Empty;

        public Button BenchAddButton => benchAdd?.Button;
        public Button BenchWithdrawButton => benchWithdraw?.Button;
        public Button BenchWithdrawAllButton => benchWithdrawAll?.Button;
        public string BenchAddBlockedReason => benchAdd?.DisabledReason;
        public string BenchWithdrawBlockedReason => benchWithdraw?.DisabledReason;

        /// <summary>The bench's drop target, so a fixture can drive the real handler.</summary>
        public NebulaBenchDropZone DropZone { get; private set; }

        /// <summary>One cargo row's drag handler, so a fixture drags what a player drags.</summary>
        public NebulaBenchDragSource CargoDragSource(string itemId) =>
            itemRows.TryGetValue(itemId, out ItemRow row) ? row.Drag : null;

        public Button RecipeCardButton(int recipeIndex) =>
            recipeIndex >= 0 && recipeIndex < recipeCards.Count ? recipeCards[recipeIndex].Select : null;

        public Button RecipeConvertButton(int recipeIndex) =>
            recipeIndex >= 0 && recipeIndex < recipeCards.Count
                ? recipeCards[recipeIndex].Convert?.Button : null;

        public bool RecipeCardVisible(int recipeIndex) =>
            recipeIndex >= 0 && recipeIndex < recipeCards.Count
            && recipeCards[recipeIndex].Root.gameObject.activeSelf;

        public string RecipeCardStateText(int recipeIndex) =>
            recipeIndex >= 0 && recipeIndex < recipeCards.Count ? recipeCards[recipeIndex].State.Value : null;

        public string RecipeCardNameText(int recipeIndex) =>
            recipeIndex >= 0 && recipeIndex < recipeCards.Count ? recipeCards[recipeIndex].Name.Value : null;

        public string RecipeCardRatioText(int recipeIndex) =>
            recipeIndex >= 0 && recipeIndex < recipeCards.Count ? recipeCards[recipeIndex].Ratio.Value : null;

        public string RecipeCardBenchText(int recipeIndex) =>
            recipeIndex >= 0 && recipeIndex < recipeCards.Count ? recipeCards[recipeIndex].OnBench.Value : null;

        public string RecipeCardMaxText(int recipeIndex) =>
            recipeIndex >= 0 && recipeIndex < recipeCards.Count ? recipeCards[recipeIndex].Ceiling.Value : null;

        public string RecipeConvertBlockedReason(int recipeIndex) =>
            recipeIndex >= 0 && recipeIndex < recipeCards.Count
                ? recipeCards[recipeIndex].Convert?.DisabledReason : null;

        /// <summary>The hint shown in place of the cards while the bench is empty.</summary>
        public string RecipesEmptyText =>
            recipesEmpty != null && recipesEmpty.Text.gameObject.activeSelf ? recipesEmpty.Value : string.Empty;

        private sealed class ItemRow
        {
            public string ItemId;
            public RectTransform Root;
            public Image Background;
            public Button Select;
            public NebulaUiFactory.LabelHandle Name;
            public NebulaUiFactory.LabelHandle Amounts;
            public NebulaUiFactory.LabelHandle Delta;
            public NebulaUiFactory.ButtonHandle Add;
            public NebulaBenchDragSource Drag;
            public bool Processable;
            public float LastAmount;
            public float DeltaUntil;
            public float DeltaValue;
        }

        private sealed class RecipeCard
        {
            public int Index;
            public RectTransform Root;
            public Image Background;
            public Button Select;
            public NebulaUiFactory.LabelHandle Name;
            public NebulaUiFactory.LabelHandle Ratio;
            public NebulaUiFactory.LabelHandle OnBench;
            public NebulaUiFactory.LabelHandle Ceiling;
            public NebulaUiFactory.LabelHandle State;
            public NebulaUiFactory.ButtonHandle Convert;
        }

        public NebulaResourcesPage(NebulaContext sessionContext)
        {
            context = sessionContext;
        }

        // =====================================================================
        // BUILD
        // =====================================================================

        public void Build(RectTransform root)
        {
            host = root;

            RectTransform cargo = Column(root, 0f, 0.34f, 24f, 8f);
            RectTransform bench = Column(root, 0.34f, 0.65f, 8f, 8f);
            RectTransform recipes = Column(root, 0.65f, 1f, 8f, 24f);

            BuildCargoCard(cargo);
            BuildCargoList(cargo);
            BuildBench(bench);
            BuildRecipeList(recipes);

            // Last, so both live above every card: the ghost has to be drawn over
            // whatever it is carried across, and the modal over everything.
            ghost = new NebulaDragGhost(root);
            modal = NebulaQuantityModal.Create(root);
        }

        /// <summary>
        /// One vertical band of the page, as a fraction of the content host.
        /// <para>
        /// Fractions rather than fixed widths, which is what keeps the three
        /// sections readable from 1280x720 to 3440x1440 without a breakpoint: the
        /// canvas scaler already maps the viewport onto the 1920x1080 reference, so
        /// the columns only have to divide whatever that leaves.
        /// </para>
        /// </summary>
        private static RectTransform Column(RectTransform parent, float min, float max,
            float leftPad, float rightPad)
        {
            RectTransform column = NebulaUiFactory.Group(parent, $"Column {min:0.00}");
            column.anchorMin = new Vector2(min, 0f);
            column.anchorMax = new Vector2(max, 1f);
            column.offsetMin = new Vector2(leftPad, 16f);
            column.offsetMax = new Vector2(-rightPad, -16f);
            return column;
        }

        private static RectTransform Card(RectTransform parent, string name, float y,
            float height, string title)
        {
            Image panel = NebulaUiFactory.Panel(parent, name, NebulaUiAssetCatalog.PanelPrimary);
            RectTransform card = panel.rectTransform;
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, y);
            card.sizeDelta = new Vector2(0f, height);

            NebulaUiFactory.LabelHandle heading = NebulaUiFactory.SectionTitle(card, "Title", title);
            Band(heading.Text.rectTransform, -12f, 18f, left: 20f, rightReserve: 200f);
            return card;
        }

        /// <summary>
        /// A text band: from a left inset to a reserved right band, stretching with
        /// whatever it is inside.
        /// <para>
        /// Stretched rather than given a fixed width on purpose. The columns are a
        /// fraction of the canvas, and the canvas gets logically narrower as the
        /// interface scale goes up — the shell expresses 140% by shrinking the
        /// scaler's reference resolution — so a fixed label that clears a button at
        /// 100% runs straight through it at 140% on a 1280x720 screen. Reserving the
        /// band makes the clearance structural at every scale and every resolution.
        /// </para>
        /// </summary>
        private static void Band(RectTransform rect, float top, float height,
            float left, float rightReserve)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(left, top - height);
            rect.offsetMax = new Vector2(-rightReserve, top);
        }

        /// <summary>
        /// An item's own icon, or nothing at all.
        /// <para>
        /// A uGUI <see cref="Image"/> with no sprite draws a filled white rectangle,
        /// so the one item the catalog has no authored icon for rendered as a blank
        /// tile beside every real icon. An absent icon must be absent, not a
        /// placeholder nobody drew.
        /// </para>
        /// </summary>
        private static Image Glyph(RectTransform parent, string itemId, Vector2 anchor,
            Vector2 offset, float size = 20f)
        {
            Sprite sprite = NebulaIconResolver.Item(itemId);
            Image icon = NebulaUiFactory.Icon(parent, "Icon", sprite, size);
            NebulaUiFactory.Place(icon.rectTransform, anchor, offset, new Vector2(size, size));
            icon.enabled = sprite != null;
            return icon;
        }

        private void BuildCargoCard(RectTransform parent)
        {
            RectTransform card = Card(parent, "Cargo", 0f, 104f, "GEMİ KARGOSU");

            cargoValue = NebulaUiFactory.Value(card, "Value", "—", NebulaUiDesignTokens.SizeValue);
            NebulaUiFactory.Place(cargoValue.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -12f), new Vector2(190f, 22f));

            cargoBar = NebulaUiFactory.Bar(card, "Bar", NebulaUiDesignTokens.ActionPrimary);
            RectTransform bar = cargoBar.Track.rectTransform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(20f, -50f - NebulaUiDesignTokens.BarHeight);
            bar.offsetMax = new Vector2(-20f, -50f);

            cargoWarning = NebulaUiFactory.Label(card, "Warning", "",
                NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Critical),
                TextAnchor.UpperLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption, wrap: true);
            Band(cargoWarning.Text.rectTransform, -68f, 30f, left: 20f, rightReserve: 20f);
            cargoWarning.Text.gameObject.SetActive(false);
        }

        /// <summary>
        /// The hold, grouped the way the player thinks about it: what was mined,
        /// what was refined, and the shared building part.
        /// <para>
        /// Every catalog item gets a row up front, including the ones held at zero.
        /// A crafting screen that hides what you do not have makes "why can I not
        /// make this" unanswerable without leaving the page, and building the set
        /// once keeps a refresh free of allocation and free of reordering.
        /// </para>
        /// </summary>
        private void BuildCargoList(RectTransform parent)
        {
            RectTransform card = Card(parent, "Items", -112f, 792f, "TAŞINAN");

            RectTransform list = NebulaUiFactory.Group(card, "List");
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            list.offsetMin = new Vector2(12f, -784f);
            list.offsetMax = new Vector2(-12f, -40f);

            // The rows are stacked to a fixed height, so a catalog that grows past it
            // used to draw the overflow across whatever was beside the card rather than
            // stopping at its edge. Clipped to the card it belongs to: content that does
            // not fit is out of sight instead of on top of another panel.
            list.gameObject.AddComponent<RectMask2D>();

            float y = 0f;
            y = GroupHeading(list, "HAM KAYNAKLAR", y);
            for (int i = 0; i < ExtractionCatalog.ResourceIds.Length; i++)
                y = CreateItemRow(list, ExtractionCatalog.ResourceIds[i], y);

            y = GroupHeading(list, "İŞLENMİŞ MALZEMELER", y);
            for (int i = 0; i < ProcessingCatalog.MaterialIds.Length; i++)
                y = CreateItemRow(list, ProcessingCatalog.MaterialIds[i], y);

            y = GroupHeading(list, "YAPI PARÇALARI", y);
            y = CreateItemRow(list, ProcessingCatalog.BasicPartId, y);

            itemsEmpty = NebulaUiFactory.Label(list, "Empty",
                "KARGO BOŞ — BİR GEZEGENDE SONDAJ KURARAK TOPLAMAYA BAŞLAYIN",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.UpperLeft, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption,
                wrap: true);
            Band(itemsEmpty.Text.rectTransform, y - 6f, 34f, left: 4f, rightReserve: 4f);
        }

        private static float GroupHeading(RectTransform list, string text, float y)
        {
            NebulaUiFactory.LabelHandle heading = NebulaUiFactory.Label(list, "Group " + text, text,
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.LowerLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            Band(heading.Text.rectTransform, y - 4f, 18f, left: 4f, rightReserve: 4f);
            return y - 26f;
        }

        private float CreateItemRow(RectTransform list, string itemId, float y)
        {
            var row = new ItemRow { ItemId = itemId };
            Image panel = NebulaUiFactory.Panel(list, itemId,
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            row.Root = panel.rectTransform;
            row.Background = panel;
            row.Root.anchorMin = new Vector2(0f, 1f);
            row.Root.anchorMax = new Vector2(1f, 1f);
            row.Root.pivot = new Vector2(0.5f, 1f);
            row.Root.anchoredPosition = new Vector2(0f, y);
            row.Root.sizeDelta = new Vector2(0f, CargoRowHeight);

            Glyph(row.Root, itemId, new Vector2(0f, 1f), new Vector2(12f, -8f));

            row.Name = NebulaUiFactory.Label(row.Root, "Name",
                NebulaBenchMessages.ItemName(itemId), NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            Band(row.Name.Text.rectTransform, -8f, 18f,
                left: 40f, rightReserve: AddWidth + AddGutter * 2f + 118f);

            row.Delta = NebulaUiFactory.Label(row.Root, "Delta", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Nominal), TextAnchor.MiddleRight,
                FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(row.Delta.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-(AddWidth + AddGutter * 2f), -8f), new Vector2(112f, 18f));
            row.Delta.Text.gameObject.SetActive(false);

            // Three numbers, not one. A reservation is not a loss and must not read
            // as one, and a hold that shows only what is spendable cannot explain
            // where the rest went.
            row.Amounts = NebulaUiFactory.Label(row.Root, "Amounts", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            Band(row.Amounts.Text.rectTransform, -28f, 16f,
                left: 40f, rightReserve: AddWidth + AddGutter * 2f);

            panel.raycastTarget = true;
            row.Select = row.Root.gameObject.AddComponent<Button>();
            row.Select.targetGraphic = panel;
            row.Select.transition = Selectable.Transition.SpriteSwap;
            row.Select.spriteState = NebulaUiAssetCatalog.RowInteraction();
            string captured = itemId;
            row.Select.onClick.AddListener(() => SelectCargoRow(captured));

            row.Processable = ProcessingBenchCommands.IsProcessable(itemId);
            if (row.Processable)
            {
                // The keyboard's and the pad's way onto the bench. Same command, same
                // modal, same validation as the drag — the control exists because a
                // drag is a pointer gesture and a pointer is not the only way in.
                row.Add = NebulaUiFactory.ButtonControl(row.Root, "Add", "TEZGÂHA EKLE",
                    NebulaUiAssetCatalog.ButtonKind.Secondary, () => RequestDeposit(captured));
                NebulaUiFactory.Place(row.Add.Button.GetComponent<RectTransform>(),
                    new Vector2(1f, 0.5f), new Vector2(-AddGutter, 0f),
                    new Vector2(AddWidth, NebulaUiDesignTokens.MinimumHitTarget));

                row.Drag = NebulaBenchDragSource.Attach(row.Root.gameObject, this, itemId);
            }

            row.LastAmount = float.NaN;   // the first refresh adopts, never celebrates
            itemRows[itemId] = row;
            itemOrder.Add(row);
            return y - (CargoRowHeight + 4f);
        }

        private void BuildBench(RectTransform parent)
        {
            RectTransform card = Card(parent, "Bench", 0f, 486f, "İŞLEME TEZGÂHI");

            benchZone = NebulaUiFactory.Panel(card, "Drop Zone",
                NebulaUiAssetCatalog.Slot(NebulaUiAssetCatalog.SlotState.Empty));
            RectTransform zone = benchZone.rectTransform;
            zone.anchorMin = new Vector2(0f, 1f);
            zone.anchorMax = new Vector2(1f, 1f);
            zone.pivot = new Vector2(0.5f, 1f);
            zone.offsetMin = new Vector2(16f, -292f);
            zone.offsetMax = new Vector2(-16f, -40f);
            // The zone answers the pointer so it can be dropped onto and so it can
            // say, while the stack is still in the air, whether it would take it.
            benchZone.raycastTarget = true;
            DropZone = NebulaBenchDropZone.Attach(benchZone.gameObject, this);

            benchHint = NebulaUiFactory.Label(zone, "Hint", NebulaBenchMessages.DropHint,
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleCenter, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption,
                wrap: true);
            NebulaUiFactory.Place(benchHint.Text.rectTransform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(300f, 44f));

            benchIcon = NebulaUiFactory.Icon(zone, "Item Icon", null, 48f);
            NebulaUiFactory.Place(benchIcon.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, -34f), new Vector2(48f, 48f));
            benchIcon.enabled = false;

            benchName = NebulaUiFactory.Label(zone, "Item", "", NebulaUiDesignTokens.SizeBody,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.UpperCenter, FontStyle.Bold);
            Band(benchName.Text.rectTransform, -92f, 20f, left: 12f, rightReserve: 12f);

            benchAmountLabel = NebulaUiFactory.Label(zone, "Amount", "",
                NebulaUiDesignTokens.SizeValueLarge, NebulaUiDesignTokens.InkPrimary,
                TextAnchor.UpperCenter, FontStyle.Bold);
            Band(benchAmountLabel.Text.rectTransform, -120f, 34f, left: 12f, rightReserve: 12f);

            // The refusal a drop earns, in words. The border tint says the same
            // thing a second time; High Contrast flattens the palette and a
            // colour-only refusal goes with it.
            benchDrop = NebulaUiFactory.Label(zone, "Drop", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Critical), TextAnchor.LowerCenter,
                FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption, wrap: true);
            Band(benchDrop.Text.rectTransform, -204f, 36f, left: 12f, rightReserve: 12f);

            benchLegacy = NebulaUiFactory.Label(card, "Legacy", NebulaBenchMessages.LegacyClaimPending,
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.Status(UiStatusLevel.Caution),
                TextAnchor.UpperLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption, wrap: true);
            Band(benchLegacy.Text.rectTransform, -300f, 30f, left: 18f, rightReserve: 18f);
            benchLegacy.Text.gameObject.SetActive(false);

            benchAdd = NebulaUiFactory.ButtonControl(card, "Add", "EKLE",
                NebulaUiAssetCatalog.ButtonKind.Primary, () => RequestDeposit(BenchItemId));
            Stretch(benchAdd, card, -336f, 44f, 0f, 1f, 16f, -16f);

            benchWithdraw = NebulaUiFactory.ButtonControl(card, "Withdraw", "KARGOYA GERİ AL",
                NebulaUiAssetCatalog.ButtonKind.Secondary, RequestWithdraw);
            Stretch(benchWithdraw, card, -392f, 44f, 0f, 0.5f, 16f, -6f);

            benchWithdrawAll = NebulaUiFactory.ButtonControl(card, "Withdraw All", "TÜMÜNÜ GERİ AL",
                NebulaUiAssetCatalog.ButtonKind.Destructive, () => WithdrawAll());
            Stretch(benchWithdrawAll, card, -392f, 44f, 0.5f, 1f, 6f, -16f);

            feedback = NebulaUiFactory.Label(parent, "Feedback", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkMuted, TextAnchor.UpperLeft, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingCaption, wrap: true);
            RectTransform line = feedback.Text.rectTransform;
            line.anchorMin = new Vector2(0f, 1f);
            line.anchorMax = new Vector2(1f, 1f);
            line.pivot = new Vector2(0.5f, 1f);
            line.offsetMin = new Vector2(4f, -572f);
            line.offsetMax = new Vector2(-4f, -498f);
        }

        /// <summary>Anchors a control across a horizontal fraction of its card.</summary>
        private static void Stretch(NebulaUiFactory.ButtonHandle handle, RectTransform card,
            float top, float height, float minX, float maxX, float leftPad, float rightPad)
        {
            var rect = (RectTransform)handle.Button.transform;
            rect.anchorMin = new Vector2(minX, 1f);
            rect.anchorMax = new Vector2(maxX, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(leftPad, top - height);
            rect.offsetMax = new Vector2(rightPad, top);
        }

        /// <summary>
        /// A card per catalog recipe, built once and shown only when the bench's
        /// item is its input.
        /// <para>
        /// Built from <see cref="ProcessingCatalog.Recipes"/> rather than from a
        /// hand-written list, so a recipe added later gets a card with no change
        /// here — the same rule the backend's <c>IsProcessable</c> follows.
        /// </para>
        /// </summary>
        private void BuildRecipeList(RectTransform parent)
        {
            RectTransform card = Card(parent, "Recipes", 0f, 880f, "DÖNÜŞÜM SEÇENEKLERİ");

            recipesEmpty = NebulaUiFactory.Label(card, "Empty", NebulaBenchMessages.BenchEmptyHint,
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkMuted,
                TextAnchor.UpperLeft, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption, wrap: true);
            Band(recipesEmpty.Text.rectTransform, -44f, 34f, left: 20f, rightReserve: 20f);

            RectTransform list = NebulaUiFactory.Group(card, "List");
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            list.offsetMin = new Vector2(12f, -868f);
            list.offsetMax = new Vector2(-12f, -84f);

            ProcessingRecipe[] all = ProcessingCatalog.Recipes;
            for (int i = 0; i < all.Length; i++) CreateRecipeCard(list, all[i], i);
        }

        private void CreateRecipeCard(RectTransform list, ProcessingRecipe recipe, int index)
        {
            var card = new RecipeCard { Index = index };
            Image panel = NebulaUiFactory.Panel(list, recipe.Id,
                NebulaUiAssetCatalog.Card(NebulaUiAssetCatalog.CardState.Default));
            card.Root = panel.rectTransform;
            card.Background = panel;
            card.Root.anchorMin = new Vector2(0f, 1f);
            card.Root.anchorMax = new Vector2(1f, 1f);
            card.Root.pivot = new Vector2(0.5f, 1f);
            card.Root.sizeDelta = new Vector2(0f, RecipeCardHeight);

            Glyph(card.Root, recipe.OutputItemId, new Vector2(0f, 1f), new Vector2(14f, -12f), 24f);

            card.Name = NebulaUiFactory.Label(card.Root, "Name", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.UpperLeft, FontStyle.Bold);
            Band(card.Name.Text.rectTransform, -12f, 18f, left: 46f, rightReserve: ConvertWidth + 28f);

            card.Ratio = NebulaUiFactory.Label(card.Root, "Ratio", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            Band(card.Ratio.Text.rectTransform, -34f, 16f, left: 46f, rightReserve: ConvertWidth + 28f);

            card.OnBench = NebulaUiFactory.Label(card.Root, "OnBench", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(card.OnBench.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(46f, -56f), new Vector2(150f, 16f));

            card.Ceiling = NebulaUiFactory.Label(card.Root, "Ceiling", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(card.Ceiling.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(202f, -56f), new Vector2(150f, 16f));

            card.State = NebulaUiFactory.Label(card.Root, "State", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.UpperLeft, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingCaption);
            Band(card.State.Text.rectTransform, -78f, 16f, left: 46f, rightReserve: ConvertWidth + 28f);

            panel.raycastTarget = true;
            card.Select = card.Root.gameObject.AddComponent<Button>();
            card.Select.targetGraphic = panel;
            card.Select.transition = Selectable.Transition.SpriteSwap;
            card.Select.spriteState = NebulaUiAssetCatalog.RowInteraction();
            int captured = index;
            card.Select.onClick.AddListener(() => SelectRecipe(captured));

            card.Convert = NebulaUiFactory.ButtonControl(card.Root, "Convert", "DÖNÜŞTÜR",
                NebulaUiAssetCatalog.ButtonKind.Primary, () => RequestConvert(captured));
            NebulaUiFactory.Place(card.Convert.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(ConvertWidth, 40f));

            card.Root.gameObject.SetActive(false);
            recipeCards.Add(card);
        }

        // =====================================================================
        // DRAG AND DROP
        // =====================================================================

        /// <inheritdoc />
        public bool BeginBenchDrag(string itemId)
        {
            LogisticsCargo cargo = Cargo();
            if (!ProcessingBenchCommands.IsProcessable(itemId))
            {
                ShowFeedback(NebulaBenchMessages.Deposit(BenchDepositReason.ItemNotProcessable),
                    UiStatusLevel.Warning);
                return false;
            }

            int available = ProcessingBenchCommands.WholeUnitsAvailable(cargo, itemId);
            if (available <= 0)
            {
                ShowFeedback(NebulaBenchMessages.Deposit(BenchDepositReason.InsufficientCargo),
                    UiStatusLevel.Warning);
                return false;
            }

            dragItemId = itemId;
            ghost.Show(itemId, available);
            return true;
        }

        /// <inheritdoc />
        public void MoveBenchDrag(Vector2 screenPosition, Camera eventCamera) =>
            ghost?.MoveTo(screenPosition, eventCamera);

        /// <inheritdoc />
        public void EndBenchDrag()
        {
            dragItemId = null;
            DropHighlight = DropState.Idle;
            ghost?.Hide();
            if (benchDrop != null) benchDrop.Value = string.Empty;
            PaintBench(Bench(), Cargo());
        }

        /// <inheritdoc />
        public void HoverBench(bool entering)
        {
            if (!entering || dragItemId == null)
            {
                DropHighlight = DropState.Idle;
                if (benchDrop != null) benchDrop.Value = string.Empty;
                PaintBench(Bench(), Cargo());
                return;
            }

            BenchDepositReason reason = ProcessingBenchCommands.ValidateDeposit(
                Bench(), Cargo(), dragItemId, 1);
            DropHighlight = reason == BenchDepositReason.None ? DropState.Valid : DropState.Invalid;
            if (benchDrop != null)
                benchDrop.Value = reason == BenchDepositReason.None
                    ? string.Empty
                    : NebulaBenchMessages.Deposit(reason);
            PaintBench(Bench(), Cargo());
        }

        /// <inheritdoc />
        public void DropOnBench()
        {
            // Taken exactly once. The gesture is consumed here rather than in
            // EndBenchDrag, so a second OnDrop delivered for the same drag finds
            // nothing to act on and asks no second question.
            string itemId = dragItemId;
            if (itemId == null) return;
            dragItemId = null;

            RequestDeposit(itemId);
        }

        /// <summary>
        /// Test-facing: the whole gesture, through the same handlers the EventSystem
        /// drives. A fixture that reached past them would prove nothing about drag.
        /// </summary>
        public bool DragToBench(string itemId)
        {
            NebulaBenchDragSource source = CargoDragSource(itemId);
            if (source == null) return false;

            var pointer = new PointerEventData(EventSystem.current) { pointerDrag = source.gameObject };
            source.OnBeginDrag(pointer);
            if (!source.Dragging) return false;

            DropZone.OnPointerEnter(pointer);
            DropZone.OnDrop(pointer);
            source.OnEndDrag(pointer);
            return true;
        }

        // =====================================================================
        // COMMANDS — one path, whichever control started it
        // =====================================================================

        /// <summary>Picks the cargo row the keyboard is standing on. Selection only; nothing moves.</summary>
        public void SelectCargoRow(string itemId)
        {
            if (!itemRows.ContainsKey(itemId)) return;
            SelectedCargoItem = itemId;
            Refresh();
        }

        /// <summary>The cargo row currently chosen, or <c>null</c>.</summary>
        public string SelectedCargoItem { get; private set; }

        /// <summary>
        /// Asks how much of an item to move onto the bench, and moves nothing until
        /// the answer is confirmed.
        /// <para>
        /// The single deposit entry point. The drop handler calls it, the row's
        /// <c>TEZGÂHA EKLE</c> calls it, and the bench's own <c>EKLE</c> calls it
        /// with whatever is already loaded.
        /// </para>
        /// </summary>
        public void RequestDeposit(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return;

            ProcessingBenchRuntime bench = Bench();
            LogisticsCargo cargo = Cargo();

            if (BlockedByLegacyClaims(bench, cargo))
            {
                ShowFeedback(NebulaBenchMessages.LegacyClaimPending, UiStatusLevel.Caution);
                Refresh();
                return;
            }

            // The validator, not the command. They share every rule, but the command
            // moves stock -- asking it "would this work" would make merely hovering
            // manufacture things. Re-deriving the rules here instead would be a
            // second authority, and two authorities drift.
            BenchDepositReason gate = ProcessingBenchCommands.ValidateDeposit(bench, cargo, itemId, 1);
            if (gate != BenchDepositReason.None)
            {
                LastDepositResult = gate;
                ShowFeedback(NebulaBenchMessages.Deposit(gate), UiStatusLevel.Warning);
                Refresh();
                return;
            }

            int available = ProcessingBenchCommands.WholeUnitsAvailable(cargo, itemId);
            int onBench = bench != null && bench.InputItemId == itemId ? bench.InputAmount : 0;
            string name = NebulaBenchMessages.ItemName(itemId);

            modal.Open(QuantityMode.Deposit, "TEZGÂHA EKLE", itemId,
                $"KULLANILABİLİR {available} · TEZGÂHTA {onBench}",
                min: 1, max: available, step: 1,
                previewBuilder: amount =>
                    $"TEZGÂHA GİDECEK: {amount} {name}\nKARGODA KALACAK: {available - amount} {name}",
                confirmed: amount => Deposit(itemId, amount));
            Refresh();
        }

        /// <summary>Asks how much to take back off the bench. Nothing moves until confirmed.</summary>
        public void RequestWithdraw()
        {
            ProcessingBenchRuntime bench = Bench();
            LogisticsCargo cargo = Cargo();

            BenchWithdrawReason gate = ProcessingBenchCommands.ValidateWithdraw(bench, cargo, 1);
            if (gate != BenchWithdrawReason.None)
            {
                LastWithdrawResult = gate;
                ShowFeedback(NebulaBenchMessages.Withdraw(gate), UiStatusLevel.Warning);
                Refresh();
                return;
            }

            int onBench = bench.InputAmount;
            string itemId = bench.InputItemId;
            string name = NebulaBenchMessages.ItemName(itemId);

            modal.Open(QuantityMode.Withdraw, "KARGOYA GERİ AL", itemId,
                $"TEZGÂHTA {onBench}",
                min: 1, max: onBench, step: 1,
                previewBuilder: amount =>
                    $"KARGOYA DÖNECEK: {amount} {name}\nTEZGÂHTA KALACAK: {onBench - amount} {name}",
                confirmed: amount => Withdraw(amount));
            Refresh();
        }

        /// <summary>
        /// Empties the bench in one press. No box, because there is no number to
        /// choose: "all of it" is the number.
        /// </summary>
        public BenchWithdrawReason WithdrawAll()
        {
            ProcessingBenchRuntime bench = Bench();
            LogisticsCargo cargo = Cargo();
            string itemId = bench?.InputItemId;

            BenchWithdrawReason reason = ProcessingBenchCommands.TryWithdrawAll(bench, cargo, out int moved);
            LastWithdrawResult = reason;

            if (reason == BenchWithdrawReason.None)
            {
                WithdrawalsIssued++;
                NoteDelta(itemId, moved);
                Announce($"+{moved} {NebulaBenchMessages.ItemName(itemId)}", UiStatusLevel.Nominal);
                PlaySuccessCue("audio.ui.confirm");
            }
            else ShowFeedback(NebulaBenchMessages.Withdraw(reason), UiStatusLevel.Warning);

            Refresh();
            return reason;
        }

        /// <summary>Picks the recipe the convert control acts on. Selection only; nothing moves.</summary>
        public void SelectRecipe(int recipeIndex)
        {
            if (ProcessingBenchCommands.RecipeAt(recipeIndex) == null) return;
            SelectedRecipe = recipeIndex;
            Refresh();
        }

        /// <summary>
        /// Asks how much of the bench to spend on a recipe, in whole multiples of
        /// its input count. Nothing moves until confirmed.
        /// </summary>
        public void RequestConvert(int recipeIndex)
        {
            ProcessingRecipe recipe = ProcessingBenchCommands.RecipeAt(recipeIndex);
            if (recipe == null) return;

            SelectedRecipe = recipeIndex;
            ProcessingBenchRuntime bench = Bench();
            LogisticsCargo cargo = Cargo();

            if (BlockedByLegacyClaims(bench, cargo))
            {
                ShowFeedback(NebulaBenchMessages.LegacyClaimPending, UiStatusLevel.Caution);
                Refresh();
                return;
            }

            // Validated at the smallest batch the recipe allows: if one batch cannot
            // run, no number the player types can.
            BenchConvertReason gate = ProcessingBenchCommands.ValidateConvert(
                bench, cargo, recipeIndex, recipe.InputCount);
            if (gate != BenchConvertReason.None)
            {
                LastConvertResult = gate;
                ShowFeedback(NebulaBenchMessages.Convert(gate, recipe), UiStatusLevel.Warning);
                Refresh();
                return;
            }

            int ceiling = ProcessingBenchCommands.MaxConvertibleInput(bench, recipeIndex);
            int onBench = bench.InputAmount;
            string inName = NebulaBenchMessages.ItemName(recipe.InputItemId);
            string outName = NebulaBenchMessages.ItemName(recipe.OutputItemId);

            modal.Open(QuantityMode.Convert, "DÖNÜŞTÜR", recipe.InputItemId,
                $"TEZGÂHTA {onBench} · EN FAZLA {ceiling}",
                min: recipe.InputCount, max: ceiling, step: recipe.InputCount,
                previewBuilder: amount =>
                    $"HARCANACAK: {amount} {inName}\n" +
                    $"ÇIKTI: {ProcessingBenchCommands.OutputFor(recipe, amount)} {outName}\n" +
                    $"TEZGÂHTA KALACAK: {onBench - amount} {inName}",
                confirmed: amount => Convert(recipeIndex, amount));
            Refresh();
        }

        // ---- the transactions themselves ------------------------------------

        /// <summary>
        /// Moves whole units out of the hold and onto the bench. Returns the seam's
        /// own answer rather than a string to parse.
        /// </summary>
        public BenchDepositReason Deposit(string itemId, int amount)
        {
            BenchDepositReason reason = ProcessingBenchCommands.TryDeposit(
                Bench(), Cargo(), itemId, amount);
            LastDepositResult = reason;

            if (reason == BenchDepositReason.None)
            {
                DepositsIssued++;
                NoteDelta(itemId, -amount);
                Announce($"-{amount} {NebulaBenchMessages.ItemName(itemId)}", UiStatusLevel.Working);
                PlaySuccessCue("audio.ui.confirm");
            }
            else ShowFeedback(NebulaBenchMessages.Deposit(reason), UiStatusLevel.Warning);

            // In the same pass as the transaction, always. The result is what the
            // player asked for; making them wait for an animation to see it would be
            // the timed model creeping back in under a different name.
            Refresh();
            return reason;
        }

        /// <summary>Moves whole units back off the bench into the hold.</summary>
        public BenchWithdrawReason Withdraw(int amount)
        {
            string itemId = BenchItemId;
            BenchWithdrawReason reason = ProcessingBenchCommands.TryWithdraw(Bench(), Cargo(), amount);
            LastWithdrawResult = reason;

            if (reason == BenchWithdrawReason.None)
            {
                WithdrawalsIssued++;
                NoteDelta(itemId, amount);
                Announce($"+{amount} {NebulaBenchMessages.ItemName(itemId)}", UiStatusLevel.Nominal);
                PlaySuccessCue("audio.ui.confirm");
            }
            else ShowFeedback(NebulaBenchMessages.Withdraw(reason), UiStatusLevel.Warning);

            Refresh();
            return reason;
        }

        /// <summary>
        /// Runs one conversion, atomically and immediately. There is no timer, no
        /// tick and no progress: the output is in the hold before this returns, or
        /// nothing moved at all.
        /// </summary>
        public BenchConvertReason Convert(int recipeIndex, int inputAmount)
        {
            ProcessingRecipe recipe = ProcessingBenchCommands.RecipeAt(recipeIndex);
            BenchConvertReason reason = ProcessingBenchCommands.TryConvert(
                Bench(), Cargo(), recipeIndex, inputAmount, out int produced);
            LastConvertResult = reason;

            if (reason == BenchConvertReason.None)
            {
                ConversionsIssued++;
                NoteDelta(recipe.OutputItemId, produced);
                Announce(
                    $"-{inputAmount} {NebulaBenchMessages.ItemName(recipe.InputItemId)}  ·  " +
                    $"+{produced} {NebulaBenchMessages.ItemName(recipe.OutputItemId)}",
                    UiStatusLevel.Nominal);
                // No cue here on purpose. NebulaAudioGameplayBridge watches the bench's
                // own conversion count and plays audio.processing.complete when it
                // rises, which covers this page AND the in-world panel. Playing it here
                // as well is not "one sound per transaction" — it is the same cue twice
                // for the same conversion, and the bridge's Update is unscaled, so it
                // fires on the very next frame even with the menu holding the pause.
                // Deposit and withdrawal keep their audio.ui.confirm below: that is a
                // UI acknowledgement of a control the player just used, not the bench's
                // gameplay sound, and the bridge emits nothing for it.
                benchFlashUntil = Time.unscaledTime + FlashSeconds;
            }
            else ShowFeedback(NebulaBenchMessages.Convert(reason, recipe), UiStatusLevel.Warning);

            Refresh();
            return reason;
        }

        /// <summary>
        /// Whether the v14 migration still owes the player something the hold could
        /// not take. Nothing new may be started until it has been collected, or the
        /// rescued output would sit behind an ever-growing bench.
        /// </summary>
        private static bool BlockedByLegacyClaims(ProcessingBenchRuntime bench, LogisticsCargo cargo)
        {
            if (bench == null || !bench.HasLegacyClaims) return false;
            // Try to hand it back first: the hold may have gained room since the
            // last look, and the honest answer is then "nothing is owed".
            return ProcessingBenchCommands.DrainLegacyClaims(bench, cargo);
        }

        private void PlaySuccessCue(string cueId)
        {
            SuccessCuesPlayed++;
            context?.AudioServiceOrNull?.PlayOneShot(cueId, volume: 0.85f);
        }

        /// <summary>The delta line, kept for a fixture and shown as the feedback message.</summary>
        private void Announce(string delta, UiStatusLevel level)
        {
            LastDeltaText = delta;
            ShowFeedback(delta, level);
        }

        private void ShowFeedback(string text, UiStatusLevel level)
        {
            if (feedback == null) return;
            feedback.Value = text;
            feedback.Text.color = NebulaUiDesignTokens.StatusInk(level);
            feedbackUntil = Time.unscaledTime + FeedbackSeconds;
        }

        /// <summary>
        /// Marks a row as just-changed so the emphasis fires on the transaction
        /// rather than on the next poll noticing a different number. Polling alone
        /// would miss a change that lands and reverses inside one refresh interval.
        /// </summary>
        private void NoteDelta(string itemId, int delta)
        {
            if (delta == 0 || itemId == null || !itemRows.TryGetValue(itemId, out ItemRow row)) return;
            row.DeltaValue = delta;
            row.DeltaUntil = Time.unscaledTime + DeltaSeconds;
        }

        // =====================================================================
        // REFRESH
        // =====================================================================

        public void Refresh()
        {
            float now = Time.unscaledTime;
            deltaPulse.Tick(Mathf.Clamp(now - lastRefreshUnscaled, 0f, 0.5f));
            lastRefreshUnscaled = now;

            AttentionCount = 0;
            LogisticsCargo cargo = Cargo();
            ProcessingBenchRuntime bench = Bench();

            // Only ever drains, never grows, and is empty for the life of any
            // campaign that never loaded a v14 save. Safe on a quarter-second timer
            // because it is idempotent: a second call finds nothing left to hand
            // back.
            bool owed = bench != null && bench.HasLegacyClaims
                        && ProcessingBenchCommands.DrainLegacyClaims(bench, cargo);
            if (owed) AttentionCount++;

            // A selection the bench can no longer honour is dropped rather than
            // carried: a stale recipe index is exactly what produces
            // RecipeInputMismatch, and offering it would be offering a refusal.
            ProcessingRecipe selected = ProcessingBenchCommands.RecipeAt(SelectedRecipe);
            if (selected != null && (bench == null || bench.IsEmpty
                                     || selected.InputItemId != bench.InputItemId))
                SelectedRecipe = -1;

            RefreshCargo(cargo, bench);
            SetVisible(itemsEmpty, VisibleItemRows == 0);
            PaintBench(bench, cargo);
            SetVisible(benchLegacy, owed);
            RefreshRecipes(bench, cargo);
            RefreshModalLimits(bench, cargo);
            WireNavigation();

            if (feedback != null && now > feedbackUntil && feedback.Value.Length > 0)
                feedback.Value = string.Empty;
        }

        private static void SetVisible(NebulaUiFactory.LabelHandle label, bool visible)
        {
            if (label != null && label.Text.gameObject.activeSelf != visible)
                label.Text.gameObject.SetActive(visible);
        }

        private LogisticsCargo Cargo()
        {
            ShipController ship = ShipOrNull();
            return ship == null ? null : ship.GetComponent<ShipExtraction>()?.Cargo;
        }

        private ProcessingBenchRuntime Bench()
        {
            ShipController ship = ShipOrNull();
            return ship == null ? null : ship.GetComponent<ShipProcessing>()?.Bench;
        }

        /// <summary>
        /// The session's ship, or <c>null</c> before composition has set one.
        /// <para>
        /// <see cref="NebulaContext.Ship"/> throws when read early — deliberately, so
        /// a wiring-order bug fails loudly — and a page refreshes on a timer that can
        /// out-run composition. Asking the set-once holder first is how the page
        /// survives that without swallowing a real wiring error.
        /// </para>
        /// </summary>
        private ShipController ShipOrNull() => context?.ShipOrNull;

        private void RefreshCargo(LogisticsCargo cargo, ProcessingBenchRuntime bench)
        {
            if (cargo == null)
            {
                cargoValue.Value = "—";
                cargoBar.SetFraction(0f);
                SetVisible(cargoWarning, false);
                for (int i = 0; i < itemOrder.Count; i++)
                    PaintItem(itemOrder[i], null, 0f, 0f, false, bench);
                VisibleItemRows = 0;
                return;
            }

            cargoValue.Value = $"{cargo.UsedVolume:0} / {cargo.Capacity:0}";
            cargoBar.SetFraction(cargo.Capacity > 0f ? cargo.UsedVolume / cargo.Capacity : 0f);
            cargoBar.SetColor(NebulaUiDesignTokens.Status(
                cargo.OverCapacity ? UiStatusLevel.Critical
                : cargo.UsedVolume > cargo.Capacity * 0.85f ? UiStatusLevel.Caution
                : UiStatusLevel.Nominal));

            bool over = cargo.OverCapacity;
            SetVisible(cargoWarning, over);
            if (over)
            {
                cargoWarning.Value = "KAPASİTE AŞILDI — TOPLAMA DURDU";
                AttentionCount++;
            }

            int held = 0;
            for (int i = 0; i < itemOrder.Count; i++)
            {
                ItemRow row = itemOrder[i];
                float stored = cargo.GetStored(row.ItemId);
                float reserved = cargo.GetReserved(row.ItemId);
                PaintItem(row, cargo, stored, reserved, true, bench);
                if (stored >= 0.5f) held++;
            }
            VisibleItemRows = held;
        }

        /// <summary>
        /// One hold line: what is physically there, what another owner has locked,
        /// and how many whole units the bench could actually take.
        /// <para>
        /// Whole units for the third figure because that is the number the deposit
        /// is checked against — extraction can leave 7.4 metal in the hold and the
        /// bench deals in units. The fraction is not hidden: it is still in the
        /// first figure, which is why all three are stated rather than one.
        /// </para>
        /// </summary>
        private void PaintItem(ItemRow row, LogisticsCargo cargo, float stored, float reserved,
            bool present, ProcessingBenchRuntime bench)
        {
            // The hold is passed in rather than looked up per row: a refresh paints
            // eleven rows four times a second, and eleven GetComponent walks per
            // repaint is a cost with nothing to show for it.
            int whole = present ? ProcessingBenchCommands.WholeUnitsAvailable(cargo, row.ItemId) : 0;

            row.Amounts.Value = present
                ? $"MEVCUT {stored:0.#} · REZERVE {reserved:0.#} · KULLANILABİLİR {whole}"
                : "OTURUM YOK";

            bool dim = !present || stored < 0.5f;
            row.Name.Text.color = dim ? NebulaUiDesignTokens.DisabledInk : NebulaUiDesignTokens.InkPrimary;
            row.Amounts.Text.color = dim
                ? NebulaUiDesignTokens.DisabledInk : NebulaUiDesignTokens.InkSecondary;

            bool chosen = row.ItemId == SelectedCargoItem;
            row.Background.sprite = NebulaUiAssetCatalog.Row(chosen
                ? NebulaUiAssetCatalog.RowState.Selected
                : NebulaUiAssetCatalog.RowState.Default);
            row.Name.Value = (chosen ? ChosenMark : string.Empty) + NebulaBenchMessages.ItemName(row.ItemId);

            // Only what the bench would actually take is offered. The bench is
            // single-type, so while it holds copper the tin row's control would be a
            // button whose only possible outcome is a refusal.
            if (row.Add != null)
            {
                bool offers = present && whole > 0
                              && (bench == null || bench.IsEmpty || bench.InputItemId == row.ItemId);
                GameObject control = row.Add.Button.gameObject;
                if (control.activeSelf != offers) control.SetActive(offers);
                if (offers) row.Add.SetAvailable(true);
            }

            float now = Time.unscaledTime;
            if (present) row.LastAmount = stored;

            bool showDelta = now < row.DeltaUntil && Mathf.Abs(row.DeltaValue) >= 0.5f;
            SetVisible(row.Delta, showDelta);
            if (!showDelta) return;

            row.Delta.Value = row.DeltaValue > 0f ? $"+{row.DeltaValue:0}" : $"{row.DeltaValue:0}";
            Color tone = NebulaUiDesignTokens.StatusInk(
                row.DeltaValue > 0f ? UiStatusLevel.Nominal : UiStatusLevel.Working);
            // Through the shared pulse, so Motion Off, Flash Reduction and Safe Mode
            // reach this emphasis without the page knowing any of them exist.
            row.Delta.Text.color = deltaPulse.Apply(tone);
        }

        /// <summary>
        /// The bench, drawn from the backend's own two fields and nothing else.
        /// <para>
        /// There is no second amount here to fall out of step: <see cref="BenchAmount"/>
        /// is a read through to <see cref="ProcessingBenchRuntime.InputAmount"/>, and
        /// this method only turns it into words.
        /// </para>
        /// </summary>
        private void PaintBench(ProcessingBenchRuntime bench, LogisticsCargo cargo)
        {
            if (benchZone == null) return;

            bool loaded = bench != null && !bench.IsEmpty;
            // The instruction stays up while a stack is being carried over an empty
            // bench; it only gives way to the refusal, which needs the same space.
            SetVisible(benchHint, !loaded && DropHighlight != DropState.Invalid);

            if (loaded)
            {
                Sprite sprite = NebulaIconResolver.Item(bench.InputItemId);
                benchIcon.sprite = sprite;
                benchIcon.enabled = sprite != null;
                benchName.Value = NebulaBenchMessages.ItemName(bench.InputItemId);
                benchAmountLabel.Value = bench.InputAmount.ToString();
            }
            else
            {
                benchIcon.enabled = false;
                benchName.Value = string.Empty;
                benchAmountLabel.Value = string.Empty;
            }

            benchZone.sprite = NebulaUiAssetCatalog.Slot(
                DropHighlight == DropState.Invalid ? NebulaUiAssetCatalog.SlotState.Damaged
                : DropHighlight == DropState.Valid ? NebulaUiAssetCatalog.SlotState.Building
                : loaded ? NebulaUiAssetCatalog.SlotState.Occupied
                : NebulaUiAssetCatalog.SlotState.Empty);

            // A refusal is never carried by the border alone; the words under the
            // zone say the same thing, and they survive High Contrast.
            benchZone.color = DropHighlight == DropState.Invalid
                ? NebulaUiDesignTokens.Status(UiStatusLevel.Critical)
                : DropHighlight == DropState.Valid
                    ? NebulaUiDesignTokens.SelectedEdge
                    : ConversionFlashActive
                        ? deltaPulse.Apply(NebulaUiDesignTokens.SelectedEdge)
                        : Color.white;

            PaintBenchControls(bench, cargo, loaded);
        }

        private void PaintBenchControls(ProcessingBenchRuntime bench, LogisticsCargo cargo, bool loaded)
        {
            if (benchAdd == null) return;

            if (!loaded)
            {
                benchAdd.SetCaption("EKLE");
                benchAdd.SetAvailable(false, NebulaBenchMessages.DropHint);
                benchWithdraw.SetAvailable(false, NebulaBenchMessages.Withdraw(BenchWithdrawReason.BenchEmpty));
                benchWithdrawAll.SetAvailable(false, NebulaBenchMessages.Withdraw(BenchWithdrawReason.BenchEmpty));
                return;
            }

            benchAdd.SetCaption($"EKLE: {NebulaBenchMessages.ItemName(bench.InputItemId)}");
            BenchDepositReason deposit = ProcessingBenchCommands.ValidateDeposit(
                bench, cargo, bench.InputItemId, 1);
            benchAdd.SetAvailable(deposit == BenchDepositReason.None,
                NebulaBenchMessages.Deposit(deposit));

            BenchWithdrawReason withdraw = ProcessingBenchCommands.ValidateWithdraw(bench, cargo, 1);
            benchWithdraw.SetAvailable(withdraw == BenchWithdrawReason.None,
                NebulaBenchMessages.Withdraw(withdraw));

            BenchWithdrawReason all = ProcessingBenchCommands.ValidateWithdraw(
                bench, cargo, bench.InputAmount);
            benchWithdrawAll.SetAvailable(all == BenchWithdrawReason.None,
                NebulaBenchMessages.Withdraw(all));
        }

        /// <summary>
        /// Every recipe the bench's item can feed, in catalog order — including the
        /// ones the bench cannot currently afford.
        /// <para>
        /// Short recipes stay visible and disabled with the exact shortfall rather
        /// than being hidden. Hiding them makes "why can I not make this" a question
        /// the page cannot answer, and the number needed is the whole answer.
        /// </para>
        /// </summary>
        private void RefreshRecipes(ProcessingBenchRuntime bench, LogisticsCargo cargo)
        {
            bool loaded = bench != null && !bench.IsEmpty;
            SetVisible(recipesEmpty, !loaded);

            int shown = 0;
            for (int i = 0; i < recipeCards.Count; i++)
            {
                RecipeCard card = recipeCards[i];
                ProcessingRecipe recipe = ProcessingCatalog.Recipes[card.Index];
                bool matches = loaded && recipe.InputItemId == bench.InputItemId;

                if (card.Root.gameObject.activeSelf != matches) card.Root.gameObject.SetActive(matches);
                if (!matches) continue;

                card.Root.anchoredPosition = new Vector2(0f, -shown * (RecipeCardHeight + 6f));
                shown++;
                PaintRecipe(card, recipe, bench, cargo);
            }

            VisibleRecipeCards = shown;
        }

        private void PaintRecipe(RecipeCard card, ProcessingRecipe recipe,
            ProcessingBenchRuntime bench, LogisticsCargo cargo)
        {
            bool chosen = card.Index == SelectedRecipe;
            string inName = NebulaBenchMessages.ItemName(recipe.InputItemId);
            string outName = NebulaBenchMessages.ItemName(recipe.OutputItemId);

            card.Name.Value = (chosen ? ChosenMark : string.Empty) +
                              $"{recipe.OutputCount} {outName}";
            card.Ratio.Value = $"{recipe.InputCount} × {inName}  →  {recipe.OutputCount} × {outName}";

            int onBench = bench?.InputAmount ?? 0;
            int ceiling = ProcessingBenchCommands.MaxConvertibleInput(bench, card.Index);
            card.OnBench.Value = $"TEZGÂHTA: {onBench}";
            card.Ceiling.Value = $"EN FAZLA: {ceiling}";

            BenchConvertReason reason = ProcessingBenchCommands.ValidateConvert(
                bench, cargo, card.Index, recipe.InputCount);
            bool ready = reason == BenchConvertReason.None;

            if (ready)
            {
                card.State.Value = ReadyMark + $"{ceiling / recipe.InputCount} PARTİ HAZIR";
                card.State.Text.color = NebulaUiDesignTokens.StatusInk(UiStatusLevel.Nominal);
                AttentionCount++;
            }
            else if (reason == BenchConvertReason.InsufficientBenchInput
                     || reason == BenchConvertReason.BenchEmpty)
            {
                int missing = Mathf.Max(1, recipe.InputCount - onBench);
                card.State.Value = ShortMark + $"{missing} {inName} EKSİK";
                card.State.Text.color = NebulaUiDesignTokens.StatusInk(UiStatusLevel.Caution);
            }
            else
            {
                card.State.Value = ShortMark + NebulaBenchMessages.Convert(reason, recipe);
                card.State.Text.color = NebulaUiDesignTokens.StatusInk(UiStatusLevel.Warning);
            }

            card.Background.sprite = NebulaUiAssetCatalog.Card(
                chosen ? NebulaUiAssetCatalog.CardState.Selected
                : ready ? NebulaUiAssetCatalog.CardState.Default
                : NebulaUiAssetCatalog.CardState.Warning);
            card.Name.Text.color = chosen
                ? NebulaUiDesignTokens.SelectedEdge : NebulaUiDesignTokens.InkPrimary;

            card.Convert.SetAvailable(ready, NebulaBenchMessages.Convert(reason, recipe));
        }

        /// <summary>
        /// Re-states an open box's ceiling from live state.
        /// <para>
        /// The hold and the bench can move underneath an open box — the page is on a
        /// quarter-second timer and the world does not stop for a dialog — and a
        /// confirm against a ceiling that no longer exists is a refusal the
        /// interface invited.
        /// </para>
        /// </summary>
        private void RefreshModalLimits(ProcessingBenchRuntime bench, LogisticsCargo cargo)
        {
            if (modal == null || !modal.IsOpen) return;

            switch (modal.Mode)
            {
                case QuantityMode.Deposit:
                    modal.SetLimits(1, ProcessingBenchCommands.WholeUnitsAvailable(cargo, modal.ItemId));
                    break;
                case QuantityMode.Withdraw:
                    modal.SetLimits(1, bench?.InputAmount ?? 0);
                    break;
                case QuantityMode.Convert:
                    modal.SetLimits(modal.Step,
                        ProcessingBenchCommands.MaxConvertibleInput(bench, SelectedRecipe));
                    break;
            }
        }

        // =====================================================================
        // FOCUS AND NAVIGATION
        // =====================================================================

        /// <summary>
        /// Hands the keyboard and the stick an explicit path: cargo rows → the row's
        /// own bench control → the bench → the recipe cards → convert.
        /// <para>
        /// Explicit rather than automatic, and the reason is structural rather than a
        /// tuning problem. Unity searches outward from the current rect's edge and
        /// discards any candidate whose facing edge lies behind it, and a row's own
        /// control sits <i>inside</i> that row's rect — the vector from the row's
        /// right edge to the button's left edge points backwards, the candidate is
        /// thrown away, and the control is reachable only with a mouse. For an
        /// interface that has to be operable without one, that is unreachable.
        /// </para>
        /// <para>
        /// Rewired every refresh because which controls exist changes with the bench:
        /// a row offers its control only while the bench would take that item, and a
        /// recipe card only exists while its input is loaded.
        /// </para>
        /// </summary>
        private void WireNavigation()
        {
            Selectable benchAnchor = benchAdd?.Button;
            Selectable firstCard = null;
            for (int i = 0; i < recipeCards.Count && firstCard == null; i++)
                if (recipeCards[i].Root.gameObject.activeSelf) firstCard = recipeCards[i].Select;

            for (int i = 0; i < itemOrder.Count; i++)
            {
                ItemRow row = itemOrder[i];
                Selectable up = i > 0 ? itemOrder[i - 1].Select : null;
                Selectable down = i + 1 < itemOrder.Count ? itemOrder[i + 1].Select : null;
                Selectable add = AddOf(row);

                Link(row.Select, up, down, null, add ?? benchAnchor);
                if (add == null) continue;

                Link(add,
                    AddOf(i > 0 ? itemOrder[i - 1] : null) ?? up,
                    AddOf(i + 1 < itemOrder.Count ? itemOrder[i + 1] : null) ?? down,
                    row.Select, benchAnchor);
            }

            Selectable lastAdd = null;
            for (int i = 0; i < itemOrder.Count; i++)
                if (AddOf(itemOrder[i]) != null) lastAdd = AddOf(itemOrder[i]);

            Link(benchAdd?.Button, benchWithdrawAll?.Button, benchWithdraw?.Button, lastAdd, firstCard);
            Link(benchWithdraw?.Button, benchAdd?.Button, benchWithdrawAll?.Button, lastAdd, firstCard);
            Link(benchWithdrawAll?.Button, benchWithdraw?.Button, benchAdd?.Button, lastAdd, firstCard);

            RecipeCard previous = null;
            for (int i = 0; i < recipeCards.Count; i++)
            {
                RecipeCard card = recipeCards[i];
                if (!card.Root.gameObject.activeSelf) continue;

                RecipeCard next = null;
                for (int j = i + 1; j < recipeCards.Count && next == null; j++)
                    if (recipeCards[j].Root.gameObject.activeSelf) next = recipeCards[j];

                Link(card.Select, previous?.Select, next?.Select, benchAnchor, card.Convert?.Button);
                Link(card.Convert?.Button, previous?.Convert?.Button ?? previous?.Select,
                    next?.Convert?.Button ?? next?.Select, card.Select, null);
                previous = card;
            }
        }

        private static Selectable AddOf(ItemRow row) =>
            row?.Add != null && row.Add.Button.gameObject.activeSelf ? row.Add.Button : null;

        private static void Link(Selectable target, Selectable up, Selectable down,
            Selectable left, Selectable right)
        {
            if (target == null) return;
            target.navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down,
                selectOnLeft = left,
                selectOnRight = right,
            };
        }

        /// <summary>
        /// Gives the keyboard and the gamepad something to stand on.
        /// <para>
        /// Unity's <c>StandaloneInputModule</c> walks Selectables from whatever is
        /// currently selected, and with nothing selected the arrow keys and the stick
        /// do nothing at all. The shell selects nothing on its own, so the page hands
        /// focus to the first row the hold actually has something in — the row a
        /// player would reach for — and to the first row otherwise.
        /// </para>
        /// </summary>
        public void FocusFirstControl()
        {
            EventSystem events = EventSystem.current;
            if (events == null || itemOrder.Count == 0) return;

            // An open box owns the keyboard; handing focus to the page underneath it
            // is exactly the "underlying UI is still live" bug this page must not have.
            if (modal != null && modal.IsOpen)
            {
                modal.FocusField();
                return;
            }

            LogisticsCargo cargo = Cargo();
            ItemRow target = itemOrder[0];
            if (cargo != null)
                for (int i = 0; i < itemOrder.Count; i++)
                    if (itemOrder[i].Processable && cargo.GetStored(itemOrder[i].ItemId) >= 0.5f)
                    {
                        target = itemOrder[i];
                        break;
                    }

            if (target.Root.gameObject.activeInHierarchy)
                events.SetSelectedGameObject(target.Root.gameObject);
        }

        /// <summary>
        /// Back closes the box first and the page second.
        /// <para>
        /// Cancelling is zero mutation, so Escape over an open box is always safe —
        /// which is what lets the page consume Back rather than asking a second
        /// question about a question.
        /// </para>
        /// </summary>
        public bool HandleBack()
        {
            if (modal == null || !modal.IsOpen) return false;
            modal.Cancel();
            Refresh();
            return true;
        }
    }
}
