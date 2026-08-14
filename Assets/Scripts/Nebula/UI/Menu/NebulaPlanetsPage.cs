using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.UI.Menu
{
    /// <summary>
    /// Held planets, their dominance, and the canonical structure slots on each.
    /// <para>
    /// This is where the extraction text wall's remaining half went. That panel
    /// summarised the surface as
    /// <c>"NODE: DRILL 1/1 · COMMAND 0/1 · DEFENSE 2/3 · SUPPORT 1/2"</c> and the
    /// four dominance conditions as <c>"SCAN [OK] DRILL [OK] COMMAND [--]"</c> —
    /// a layout described in counts, and a checklist rendered in ASCII. Both are
    /// spatial facts, so both are drawn.
    /// </para>
    /// <para>
    /// Strictly read-only. Building and upgrading still happen at the planet,
    /// through the extraction systems that own those rules; this page is where the
    /// player finds out what they have and what is wrong with it.
    /// </para>
    /// </summary>
    public sealed class NebulaPlanetsPage : INebulaMenuPage
    {
        public NebulaUiAssetCatalog.Page Page => NebulaUiAssetCatalog.Page.Planets;
        public string Title => "Gezegenler";

        /// <summary>Planet rows drawn at once. Beyond this the list scrolls.</summary>
        public const int MaxPlanetRows = 8;

        private readonly NebulaContext context;

        private NebulaUiFactory.LabelHandle summaryValue;
        private NebulaUiFactory.LabelHandle emptyState;
        private RectTransform planetList;

        private readonly List<PlanetRow> rows = new List<PlanetRow>(MaxPlanetRows);
        private readonly List<string> orderedWorldIds = new List<string>(MaxPlanetRows);

        /// <summary>Contested and lost planets. Both need the player now.</summary>
        public int AttentionCount { get; private set; }

        /// <summary>Test-facing: planet rows currently shown.</summary>
        public int VisibleRows { get; private set; }

        /// <summary>
        /// Evacuation lives here, not on the HUD. The context bar shows one primary
        /// and two secondary, and an idle "you could abandon this planet" offer is
        /// not urgent enough to spend a slot on -- only an evacuation that is
        /// actually happening takes one. This is where the player finds the action
        /// in the first place, and where it says why it is refused.
        /// </summary>
        private NebulaUiFactory.ButtonHandle evacuateButton;
        private NebulaUiFactory.LabelHandle statusLine;
        private readonly NebulaConfirmationDialog confirmation;

        /// <summary>The planet the action bar acts on, or <c>null</c>.</summary>
        public string SelectedWorldId { get; private set; }

        /// <summary>Test-facing: why evacuate/reclaim is unavailable, or <c>null</c>.</summary>
        public string EvacuateBlockedReason { get; private set; }

        /// <summary>Test-facing: the last thing the page told the player.</summary>
        public string StatusText => statusLine?.Text != null ? statusLine.Text.text : null;

        private sealed class PlanetRow
        {
            public RectTransform Root;
            public Image Background;
            public Image DominanceIcon;
            public NebulaUiFactory.LabelHandle Name;
            public NebulaUiFactory.LabelHandle State;
            public NebulaUiFactory.LabelHandle Detail;
            public Image[] Slots;
        }

        public NebulaPlanetsPage(NebulaContext sessionContext,
            NebulaConfirmationDialog confirmationDialog = null)
        {
            context = sessionContext;
            confirmation = confirmationDialog;
        }

        public void Build(RectTransform host)
        {
            var column = NebulaUiFactory.Group(host, "Planets Column");
            column.anchorMin = Vector2.zero;
            column.anchorMax = Vector2.one;
            column.offsetMin = new Vector2(32f, 24f);
            column.offsetMax = new Vector2(-32f, -24f);

            BuildSummary(column);
            BuildList(column);
            BuildActions(column);
        }

        private void BuildActions(RectTransform parent)
        {
            statusLine = NebulaUiFactory.Label(parent, "Status", "",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(statusLine.Text.rectTransform, new Vector2(0f, 0f),
                new Vector2(0f, 12f), new Vector2(560f, 32f));

            evacuateButton = NebulaUiFactory.ButtonControl(parent, "Evacuate",
                "GEZEGENİ TAHLİYE ET", NebulaUiAssetCatalog.ButtonKind.Destructive,
                Evacuate);
            NebulaUiFactory.Place(evacuateButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(280f, 44f));
        }

        /// <summary>Picks the planet the action bar acts on.</summary>
        public void Select(string worldId)
        {
            SelectedWorldId = worldId;
            Refresh();
        }

        /// <summary>
        /// Abandoning a planet cannot be undone, so it asks -- and it asks here
        /// rather than relying on the ship's two-press window, because a dialog can
        /// say what is being given up and a second keypress cannot.
        /// </summary>
        private void Evacuate()
        {
            ShipPlanetEvacuation evacuation = Evacuation();
            if (evacuation == null || SelectedWorldId == null) return;

            bool abandoned = ExtractionState.IsPlanetAbandoned(SelectedWorldId);

            void Commit()
            {
                bool ok = abandoned ? evacuation.TryReclaim() : evacuation.TryBeginEvacuation();
                Report(ok
                    ? (abandoned ? "GEZEGEN GERİ ALINDI" : "TAHLİYE BAŞLADI")
                    : EvacuateBlockedReason ?? "İŞLEM REDDEDİLDİ");
                Refresh();
            }

            if (abandoned) { Commit(); return; }

            if (confirmation == null) { Commit(); return; }
            confirmation.Ask("GEZEGENİ TAHLİYE ET",
                "Bu gezegendeki her şey terk edilecek ve sondajlar kaybolacak. Bu işlem geri alınamaz.",
                null, "TAHLİYE ET", Commit, destructive: true);
        }

        private ShipPlanetEvacuation Evacuation()
        {
            Ship.ShipController ship = context?.Ship;
            return ship != null ? ship.GetComponent<ShipPlanetEvacuation>() : null;
        }

        private void Report(string message)
        {
            if (statusLine == null) return;
            statusLine.Value = message ?? string.Empty;
        }

        private void RefreshActions()
        {
            if (evacuateButton == null) return;

            ShipPlanetEvacuation evacuation = Evacuation();
            bool abandoned = SelectedWorldId != null
                && ExtractionState.IsPlanetAbandoned(SelectedWorldId);

            evacuateButton.SetCaption(abandoned ? "GEZEGENİ GERİ AL" : "GEZEGENİ TAHLİYE ET");

            if (evacuation == null) EvacuateBlockedReason = "OTURUM YOK";
            else if (SelectedWorldId == null) EvacuateBlockedReason = "BİR GEZEGEN SEÇİN";
            else if (abandoned)
                EvacuateBlockedReason = evacuation.CanReclaim(out ReclaimRejectReason reclaim)
                    ? null : ShipPlanetEvacuation.RejectMessage(reclaim);
            else
                EvacuateBlockedReason = evacuation.CanEvacuate(out EvacuationRejectReason evacuate)
                    ? null : ShipPlanetEvacuation.RejectMessage(evacuate);

            evacuateButton.SetAvailable(EvacuateBlockedReason == null, EvacuateBlockedReason);
        }

        private void BuildSummary(RectTransform parent)
        {
            RectTransform card = Card(parent, "Summary", 0f, 80f, "HÂKİMİYET");
            summaryValue = NebulaUiFactory.Value(card, "Value", "—", NebulaUiDesignTokens.SizeValue);
            NebulaUiFactory.Place(summaryValue.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -42f), new Vector2(560f, 22f));
        }

        private void BuildList(RectTransform parent)
        {
            RectTransform card = Card(parent, "Planets", -92f, 724f, "GEZEGENLER");

            emptyState = NebulaUiFactory.Label(card, "Empty",
                "HENÜZ HÂKİMİYET KURULMUŞ GEZEGEN YOK",
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleCenter, FontStyle.Normal, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(emptyState.Text.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, -80f), new Vector2(700f, 20f));

            planetList = NebulaUiFactory.Group(card, "List");
            planetList.anchorMin = new Vector2(0f, 1f);
            planetList.anchorMax = new Vector2(1f, 1f);
            planetList.pivot = new Vector2(0.5f, 1f);
            planetList.offsetMin = new Vector2(14f, -712f);
            planetList.offsetMax = new Vector2(-14f, -44f);

            for (int i = 0; i < MaxPlanetRows; i++) rows.Add(CreateRow(i));
        }

        private static RectTransform Card(RectTransform parent, string name, float y, float height, string title)
        {
            Image panel = NebulaUiFactory.Panel(parent, name, NebulaUiAssetCatalog.PanelPrimary);
            RectTransform card = panel.rectTransform;
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, y);
            card.sizeDelta = new Vector2(0f, height);

            NebulaUiFactory.Place(NebulaUiFactory.SectionTitle(card, "Title", title).Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(20f, -18f), new Vector2(340f, 18f));
            return card;
        }

        private PlanetRow CreateRow(int index)
        {
            var row = new PlanetRow();
            Image panel = NebulaUiFactory.Panel(planetList, "Planet " + index,
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            row.Root = panel.rectTransform;
            row.Background = panel;

            row.Root.anchorMin = new Vector2(0f, 1f);
            row.Root.anchorMax = new Vector2(1f, 1f);
            row.Root.pivot = new Vector2(0.5f, 1f);
            row.Root.anchoredPosition = new Vector2(0f, -index * 84f);
            row.Root.sizeDelta = new Vector2(0f, 78f);

            row.DominanceIcon = NebulaUiFactory.Icon(row.Root, "Dominance", null, 22f);
            NebulaUiFactory.Place(row.DominanceIcon.rectTransform, new Vector2(0f, 1f),
                new Vector2(16f, -14f), new Vector2(22f, 22f));

            row.Name = NebulaUiFactory.Label(row.Root, "Name", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(row.Name.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(46f, -14f), new Vector2(300f, 18f));

            row.State = NebulaUiFactory.Label(row.Root, "State", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleRight, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(row.State.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-16f, -14f), new Vector2(300f, 16f));

            row.Detail = NebulaUiFactory.Label(row.Root, "Detail", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(row.Detail.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(16f, -38f), new Vector2(420f, 14f));

            // The canonical slots, drawn rather than counted. Occupied, building,
            // damaged, empty and locked each have their own authored state, so the
            // player reads the surface at a glance instead of parsing "2/3".
            row.Slots = new Image[SurfaceSlotView.CanonicalSlotCount];
            for (int i = 0; i < row.Slots.Length; i++)
            {
                Image slot = NebulaUiFactory.Panel(row.Root, "Slot " + i,
                    NebulaUiAssetCatalog.Slot(NebulaUiAssetCatalog.SlotState.Empty));
                NebulaUiFactory.Place(slot.rectTransform, new Vector2(1f, 1f),
                    new Vector2(-16f - (row.Slots.Length - 1 - i) * 26f, -38f), new Vector2(22f, 22f));
                row.Slots[i] = slot;
            }

            // The row is the selector. Reusing the row panel as the button's target
            // graphic is required: a second Image on one GameObject returns null.
            var button = row.Root.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = row.Background;
            int captured = index;
            button.onClick.AddListener(() => SelectRow(captured));

            row.Root.gameObject.SetActive(false);
            return row;
        }

        // ---- refresh ---------------------------------------------------------

        public void Refresh()
        {
            AttentionCount = 0;

            orderedWorldIds.Clear();
            CollectPlanets(orderedWorldIds);

            bool any = orderedWorldIds.Count > 0;
            if (emptyState.Text.gameObject.activeSelf == any)
                emptyState.Text.gameObject.SetActive(!any);

            int controlled = 0, contested = 0, lost = 0, developing = 0;
            int shown = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                bool visible = i < orderedWorldIds.Count;
                if (rows[i].Root.gameObject.activeSelf != visible)
                    rows[i].Root.gameObject.SetActive(visible);
                if (!visible) continue;

                string worldId = orderedWorldIds[i];
                PlanetDominance dominance = ExtractionState.GetDominance(worldId);
                ApplyRow(rows[i], worldId, dominance);
                shown++;

                switch (dominance.State)
                {
                    case DominanceState.Controlled: controlled++; break;
                    case DominanceState.Contested: contested++; AttentionCount++; break;
                    case DominanceState.Lost: lost++; AttentionCount++; break;
                    case DominanceState.Developing: developing++; break;
                }
            }

            VisibleRows = shown;
            summaryValue.Value = $"{controlled} KONTROL · {developing} GELİŞİYOR · " +
                                 $"{contested} ÇEKİŞMELİ · {lost} KAYIP";

            // A selection that has dropped off the list cannot be acted on.
            if (SelectedWorldId != null && !orderedWorldIds.Contains(SelectedWorldId))
                SelectedWorldId = null;

            RefreshActions();
        }

        private void SelectRow(int index)
        {
            if (index < 0 || index >= orderedWorldIds.Count) return;
            Select(orderedWorldIds[index]);
        }

        /// <summary>
        /// Planets worth listing, most urgent first. A contested planet the player
        /// has ninety seconds to save must never sit below a stable one.
        /// </summary>
        private static void CollectPlanets(List<string> into)
        {
            foreach (KeyValuePair<string, PlanetDominance> entry in ExtractionState.AllDominance)
            {
                if (entry.Value == null) continue;
                if (entry.Value.State == DominanceState.Unowned) continue;
                into.Add(entry.Key);
            }

            into.Sort((a, b) => Urgency(ExtractionState.GetDominance(b).State)
                .CompareTo(Urgency(ExtractionState.GetDominance(a).State)));

            if (into.Count > MaxPlanetRows) into.RemoveRange(MaxPlanetRows, into.Count - MaxPlanetRows);
        }

        private static int Urgency(DominanceState state)
        {
            switch (state)
            {
                case DominanceState.Contested: return 4;
                case DominanceState.Lost: return 3;
                case DominanceState.Developing: return 2;
                case DominanceState.Controlled: return 1;
                default: return 0;
            }
        }

        private void ApplyRow(PlanetRow row, string worldId, PlanetDominance dominance)
        {
            // L-05: the id was going straight to the player. It is the system's
            // name plus an orbit numeral now, composed from data that existed.
            row.Name.Value = NebulaPlanetName.Of(worldId);
            row.State.Value = StateName(dominance.State);

            UiStatusLevel level = LevelFor(dominance.State);
            row.State.Text.color = NebulaUiDesignTokens.Status(level);
            row.Background.sprite = NebulaUiAssetCatalog.Row(RowStateFor(dominance.State));

            Sprite icon = NebulaProducedVisualResolver.Dominance(dominance.State);
            row.DominanceIcon.sprite = icon;
            row.DominanceIcon.enabled = icon != null;

            row.Detail.Value = DetailFor(dominance);
            row.Detail.Text.color = dominance.State == DominanceState.Contested
                ? NebulaUiDesignTokens.Status(UiStatusLevel.Critical)
                : NebulaUiDesignTokens.InkMuted;

            SurfaceSlotView.Paint(worldId, row.Slots);
        }

        /// <summary>
        /// The timer that matters for this state, in words. A contested planet's
        /// recovery countdown is the single most actionable number on the page.
        /// </summary>
        private static string DetailFor(PlanetDominance dominance)
        {
            switch (dominance.State)
            {
                case DominanceState.Contested:
                    return $"KAYBA {Mathf.Max(0f, PlanetDominance.RecoverySeconds - dominance.RecoveryTimer):0}sn";
                case DominanceState.Developing:
                    return $"DENGELENİYOR {dominance.StabilizeTimer:0} / {PlanetDominance.StabilizeSeconds:0}sn";
                case DominanceState.Lost:
                    return "HÂKİMİYET KAYBEDİLDİ";
                default:
                    return string.Empty;
            }
        }

        internal static string StateName(DominanceState state)
        {
            switch (state)
            {
                case DominanceState.Unowned: return "SAHİPSİZ";
                case DominanceState.Developing: return "GELİŞİYOR";
                case DominanceState.Controlled: return "KONTROL EDİLİYOR";
                case DominanceState.Contested: return "ÇEKİŞMELİ";
                case DominanceState.Lost: return "KAYBEDİLDİ";
                default: return "—";
            }
        }

        private static UiStatusLevel LevelFor(DominanceState state)
        {
            switch (state)
            {
                case DominanceState.Controlled: return UiStatusLevel.Nominal;
                case DominanceState.Developing: return UiStatusLevel.Working;
                case DominanceState.Contested: return UiStatusLevel.Critical;
                case DominanceState.Lost: return UiStatusLevel.Warning;
                default: return UiStatusLevel.Inactive;
            }
        }

        private static NebulaUiAssetCatalog.RowState RowStateFor(DominanceState state)
        {
            switch (state)
            {
                case DominanceState.Contested: return NebulaUiAssetCatalog.RowState.Critical;
                case DominanceState.Lost: return NebulaUiAssetCatalog.RowState.Warning;
                default: return NebulaUiAssetCatalog.RowState.Default;
            }
        }

        /// <summary>No nested state, so Back falls through to the shell.</summary>
        public bool HandleBack() => false;
    }

    /// <summary>
    /// Paints a planet's canonical structure slots.
    /// <para>
    /// Separated from the page so the same reading can be reused by the planet
    /// context UI later, and so the slot rule has one implementation rather than
    /// one per screen.
    /// </para>
    /// </summary>
    public static class SurfaceSlotView
    {
        /// <summary>Drill, Command, and the defence/support slots the surface declares.</summary>
        public const int CanonicalSlotCount = 12;

        /// <summary>
        /// Fills <paramref name="slots"/> from the planet's real node layout and
        /// what is built on it. Slots beyond the planet's own node count are drawn
        /// locked rather than empty — an unbuildable position is not a free one.
        /// </summary>
        public static void Paint(string worldId, Image[] slots)
        {
            if (slots == null) return;

            IReadOnlyList<SurfaceNode> nodes = ResolveNodes(worldId);

            for (int i = 0; i < slots.Length; i++)
            {
                Image slot = slots[i];
                if (slot == null) continue;

                if (nodes == null || i >= nodes.Count)
                {
                    // Unscanned planets reveal no layout at all: the information
                    // gate applies here exactly as it does on the HUD.
                    slot.sprite = NebulaUiAssetCatalog.Slot(NebulaUiAssetCatalog.SlotState.Locked);
                    continue;
                }

                slot.sprite = NebulaUiAssetCatalog.Slot(Resolve(worldId, nodes[i]));
            }
        }

        /// <summary>
        /// The planet's node layout, or <c>null</c> when it has none to show.
        /// <para>
        /// <see cref="ExtractionState.GetProfile(string)"/> throws for a world id
        /// that has no manifest, which is correct for gameplay — a profile really
        /// must come from one — but a page refresh cannot be allowed to take the
        /// interface down over a stale id. Drawing every slot locked is the honest
        /// answer: we do not know this surface.
        /// </para>
        /// </summary>
        private static IReadOnlyList<SurfaceNode> ResolveNodes(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return null;

            PlanetProfile profile;
            try
            {
                profile = ExtractionState.GetProfile(worldId);
            }
            catch (System.InvalidOperationException)
            {
                return null;
            }

            return profile != null && profile.Scanned ? profile.Nodes : null;
        }

        private static NebulaUiAssetCatalog.SlotState Resolve(string worldId, SurfaceNode node)
        {
            if (!ExtractionState.IsNodeOccupied(worldId, node.Id))
                return NebulaUiAssetCatalog.SlotState.Empty;

            // Occupied is not the whole story: a structure part-way through
            // construction, or one that has been shot, both read as "there" from a
            // count and as very different things from the surface.
            foreach (SurfaceStructure structure in ExtractionState.Structures)
            {
                if (structure == null || structure.WorldId != worldId || structure.NodeId != node.Id) continue;
                StructureRuntime runtime = structure.Runtime;
                if (runtime == null) break;

                if (runtime.State == DrillLifecycle.Damaged) return NebulaUiAssetCatalog.SlotState.Damaged;
                if (runtime.State == DrillLifecycle.Constructing
                    || runtime.State == DrillLifecycle.CapsuleInTransit)
                    return NebulaUiAssetCatalog.SlotState.Building;
                break;
            }

            foreach (DrillStructure drill in ExtractionState.Drills)
            {
                if (drill == null || drill.WorldId != worldId || drill.NodeId != node.Id) continue;
                DrillRuntime runtime = drill.Runtime;
                if (runtime == null) break;

                if (runtime.State == DrillLifecycle.Damaged) return NebulaUiAssetCatalog.SlotState.Damaged;
                if (runtime.State == DrillLifecycle.Constructing
                    || runtime.State == DrillLifecycle.CapsuleInTransit)
                    return NebulaUiAssetCatalog.SlotState.Building;
                break;
            }

            return NebulaUiAssetCatalog.SlotState.Occupied;
        }
    }
}
