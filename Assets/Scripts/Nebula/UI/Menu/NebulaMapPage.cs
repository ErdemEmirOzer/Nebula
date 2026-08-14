using UnityEngine;
using UnityEngine.UI;
using Nebula.Navigation;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.UI.StrategicMap;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.UI.Menu
{
    /// <summary>
    /// The menu's entry to the strategic map.
    /// <para>
    /// The map is not redrawn or re-hosted here, and nothing about how it looks or
    /// behaves changes. It is a full-screen surface with its own canvas, its own
    /// 3D render target sized to the backbuffer, its own camera controls and its
    /// own modal ownership; hosting it inside a menu page's content rect would
    /// mean changing all four. So this page is the handoff, not a container.
    /// </para>
    /// <para>
    /// The only real work is ownership. Both the menu and the map take
    /// <see cref="ExclusiveModal"/>, and the shell freezes the game while a screen
    /// is open, while the map deliberately keeps the simulation running and only
    /// gates ship input. Opening the map from inside the menu therefore has to
    /// close the menu completely first — releasing the pause and the modal — and
    /// only then hand over. Doing it the other way round would either be refused
    /// outright or leave the map running over a frozen world, which is precisely
    /// the behaviour change this page exists to avoid.
    /// </para>
    /// </summary>
    public sealed class NebulaMapPage : INebulaMenuPage
    {
        public NebulaUiAssetCatalog.Page Page => NebulaUiAssetCatalog.Page.Map;
        public string Title => "Harita";

        /// <summary>The map raises no attention of its own; the rail badge stays empty.</summary>
        public int AttentionCount => 0;

        private readonly NebulaContext context;
        private readonly NebulaUiShell shell;

        private NebulaUiFactory.LabelHandle knownValue, routeValue, analysisValue, hintValue;
        private NebulaUiFactory.ButtonHandle openButton;

        /// <summary>Test-facing: why the map cannot be opened, or <c>null</c>.</summary>
        public string OpenBlockedReason { get; private set; }

        /// <summary>Test-facing: times this page handed off to the map.</summary>
        public int HandoffCount { get; private set; }

        public NebulaMapPage(NebulaContext sessionContext, NebulaUiShell owningShell = null)
        {
            context = sessionContext;
            shell = owningShell;
        }

        public void Build(RectTransform host)
        {
            var column = NebulaUiFactory.Group(host, "Map Column");
            column.anchorMin = Vector2.zero;
            column.anchorMax = Vector2.one;
            column.offsetMin = new Vector2(32f, 24f);
            column.offsetMax = new Vector2(-32f, -24f);

            BuildSummary(column);
            BuildAction(column);
        }

        private void BuildSummary(RectTransform parent)
        {
            RectTransform card = Card(parent, "Navigation", 0f, 232f, "SEYİR DURUMU");

            knownValue = Reading(card, "Known", "BİLİNEN SİSTEMLER", -52f);
            routeValue = Reading(card, "Route", "SEÇİLİ ROTA", -104f);
            analysisValue = Reading(card, "Analysis", "SİNYAL ANALİZİ", -156f);
        }

        private NebulaUiFactory.LabelHandle Reading(RectTransform card, string name,
            string caption, float y)
        {
            NebulaUiFactory.Place(
                NebulaUiFactory.Label(card, name + "Caption", caption,
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                    TextAnchor.MiddleLeft, FontStyle.Normal,
                    NebulaUiDesignTokens.TrackingCaption).Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(20f, y), new Vector2(320f, 16f));

            NebulaUiFactory.LabelHandle value = NebulaUiFactory.Value(card, name, "—",
                NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(value.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, y), new Vector2(420f, 16f));
            return value;
        }

        private void BuildAction(RectTransform parent)
        {
            hintValue = NebulaUiFactory.Label(parent, "Hint",
                $"Harita tam ekran açılır. {NebulaText.UpperAscii(StrategicMapScreen.ToggleKey.ToString())} " +
                "tuşu da aynı haritayı açar.",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.MiddleLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(hintValue.Text.rectTransform, new Vector2(0f, 0f),
                new Vector2(0f, 12f), new Vector2(560f, 32f));

            openButton = NebulaUiFactory.ButtonControl(parent, "OpenMap", "HARİTAYI AÇ",
                NebulaUiAssetCatalog.ButtonKind.Primary, () => OpenMap());
            NebulaUiFactory.Place(openButton.Button.GetComponent<RectTransform>(),
                new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(240f, 44f));
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

            NebulaUiFactory.Place(
                NebulaUiFactory.SectionTitle(card, "Title", title).Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(20f, -18f), new Vector2(340f, 18f));
            return card;
        }

        // ---- the handoff -----------------------------------------------------

        /// <summary>
        /// Closes the menu, then opens the map. In that order, and never both open
        /// at once: the shell holds the modal and the input gate while the menu is
        /// up, and the map refuses to open over another modal owner.
        /// <para>
        /// Public and returning a bool for the same reason
        /// <see cref="StrategicMapScreen.ToggleFromMapKey"/> is: the whole decision
        /// is then testable without synthesising a click. Returns true when the map
        /// really opened.
        /// </para>
        /// </summary>
        public bool OpenMap()
        {
            StrategicMapScreen map = context?.Presentation?.StrategicMapScreen;
            if (map == null) return false;

            // PopAll releases the input gate and the modal. Until it has run, the
            // map's own TryAcquire would be refused by the shell's hold. Nothing is
            // closed before we know there is a map to hand over to, so a refused
            // handoff leaves the player where they were rather than on a bare HUD.
            //
            // The gate briefly drops between the two calls and the map raises it
            // again on the same frame. That is why the order cannot be reversed:
            // opening the map first would have the shell's PopAll restore the
            // pre-menu value straight over the map's fresh hold.
            shell?.PopAll();

            if (!map.ToggleFromMapKey())
            {
                // The menu has already closed by this point, so the player is looking
                // at the HUD with no map and no explanation. ToggleFromMapKey publishes
                // the typed refusal on the one feedback channel, which the HUD strip is
                // showing again now that the screen is down; this records the same
                // reason on the page so the control that was clicked also carries it.
                OpenBlockedReason = StrategicMapScreen.ModalBlockedText;
                openButton?.SetAvailable(false, OpenBlockedReason);
                return false;
            }
            HandoffCount++;
            return true;
        }

        // ---- refresh ---------------------------------------------------------

        public void Refresh()
        {
            NavigationDirector director = context?.NavigationDirector;
            StrategicMapScreen map = context?.Presentation?.StrategicMapScreen;

            OpenBlockedReason =
                map == null ? "HARİTA HAZIR DEĞİL"
                : map.IsOpen ? "HARİTA ZATEN AÇIK"
                : null;
            openButton?.SetAvailable(OpenBlockedReason == null, OpenBlockedReason);

            if (director?.Knowledge == null)
            {
                knownValue.Value = "—";
                routeValue.Value = "—";
                analysisValue.Value = "—";
                return;
            }

            int known = 0;
            foreach (var entry in director.Knowledge.All)
                if (entry.Value != SystemKnowledgeLevel.Unknown) known++;
            knownValue.Value = $"{known}";

            // The route is named, never shown as its stable id -- L-05.
            string routeId = director.SelectedRouteSystemId;
            routeValue.Value = routeId == null ? "YOK"
                : director.TryGetSystem(routeId, out StarSystemRecord record)
                    ? NebulaText.Upper(record.DisplayName)
                    : "BİLİNMEYEN HEDEF";

            analysisValue.Value = director.Analysis != null && director.Analysis.Active
                ? $"SÜRÜYOR · {director.Analysis.RemainingSeconds:0}sn"
                : "YOK";
        }

        /// <summary>The map owns its own back handling once it is open.</summary>
        public bool HandleBack() => false;
    }
}
