using System;
using UnityEngine;
using UnityEngine.UI;

namespace Nebula.UI.StrategicMap
{
    /// <summary>Presentation-only layers. Toggling one never changes gameplay or knowledge.</summary>
    public enum StrategicMapDisplayLayer
    {
        Grid,
        Route,
        Discoveries,
        Fleet,
        Logistics,
        Threat,
        Ambient
    }

    /// <summary>Everything the command deck needs for one render; no gameplay authority lives here.</summary>
    public readonly struct StrategicMapDeckState
    {
        public readonly string TargetTitle;
        public readonly string TargetMeta;
        public readonly string RouteSummary;
        public readonly string AnalysisSummary;
        public readonly bool CanRoute;
        public readonly bool HasRoute;
        public readonly bool CanAnalyze;
        public readonly bool AnalysisActive;
        public readonly string RouteActionLabel;

        public StrategicMapDeckState(string targetTitle, string targetMeta, string routeSummary,
            string analysisSummary, bool canRoute, bool hasRoute, bool canAnalyze, bool analysisActive,
            string routeActionLabel = null)
        {
            TargetTitle = targetTitle;
            TargetMeta = targetMeta;
            RouteSummary = routeSummary;
            AnalysisSummary = analysisSummary;
            CanRoute = canRoute;
            HasRoute = hasRoute;
            CanAnalyze = canAnalyze;
            AnalysisActive = analysisActive;
            RouteActionLabel = routeActionLabel;
        }
    }

    /// <summary>
    /// Responsive action and layer chrome for the live map. It turns formerly hidden keyboard
    /// commands into visible, focusable uGUI controls while keeping the cinematic centre clear.
    /// </summary>
    public sealed class StrategicMapCommandDeck : MonoBehaviour
    {
        private sealed class Control
        {
            public Button Button;
            public Image Background;
            public Text Label;
        }

        private RectTransform bottomRect;
        private RectTransform layerRect;
        private Text targetTitle;
        private Text targetMeta;
        private Text routeSummary;
        private Text analysisSummary;
        private Text toast;
        private Control focusButton;
        private Control overviewButton;
        private Control routeButton;
        private Control clearButton;
        private Control analysisButton;
        private Control gridButton;
        private Control routeLayerButton;
        private Control discoveryButton;
        private Control fleetButton;
        private Control logisticsButton;
        private Control threatButton;
        private Control ambientButton;

        private bool gridVisible = true;
        private bool routeVisible = true;
        private bool discoveriesVisible = true;
        private bool fleetVisible;
        private bool logisticsVisible;
        private bool threatVisible = true;
        private bool ambientVisible = true;

        public event Action FocusCurrentRequested;
        public event Action OverviewRequested;
        public event Action RouteRequested;
        public event Action ClearRouteRequested;
        public event Action AnalysisRequested;
        public event Action<StrategicMapDisplayLayer, bool> LayerChanged;

        public static StrategicMapCommandDeck Create(RectTransform parent)
        {
            var go = new GameObject("Strategic Map Command Deck");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            StrategicMapScreen.Stretch(rect);
            var deck = go.AddComponent<StrategicMapCommandDeck>();
            deck.Build(rect);
            return deck;
        }

        private void Build(RectTransform root)
        {
            bottomRect = Panel(root, "Command Rail", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(-260f, 28f), new Vector2(920f, 142f),
                new Vector2(0.5f, 0f));

            targetTitle = Label(bottomRect, "Target", "HEDEF SEÇİLMEDİ", 22,
                StrategicMapTheme.PanelText, TextAnchor.UpperLeft,
                new Vector2(24f, -14f), new Vector2(410f, 28f));
            targetMeta = Label(bottomRect, "Target Meta", "Bir sistem veya gezegen seç", 15,
                StrategicMapTheme.MutedText, TextAnchor.UpperLeft,
                new Vector2(24f, -43f), new Vector2(410f, 23f));
            routeSummary = Label(bottomRect, "Route Summary", "ROTA · YOK", 15,
                StrategicMapTheme.Cyan, TextAnchor.UpperRight,
                new Vector2(-24f, -14f), new Vector2(420f, 24f), right: true);
            analysisSummary = Label(bottomRect, "Analysis Summary", "ANALİZ · BEKLEMEDE", 14,
                StrategicMapTheme.Violet, TextAnchor.UpperRight,
                new Vector2(-24f, -40f), new Vector2(420f, 22f), right: true);

            focusButton = Action(bottomRect, "Focus", "GEMİYE ODAK  [F]", 18,
                new Vector2(16f, 14f), new Vector2(164f, 48f), () => FocusCurrentRequested?.Invoke());
            overviewButton = Action(bottomRect, "Overview", "GENEL BAKIŞ  [HOME]", 18,
                new Vector2(188f, 14f), new Vector2(184f, 48f), () => OverviewRequested?.Invoke());
            routeButton = Action(bottomRect, "Set Route", "ROTA BELİRLE  [ENTER]", 18,
                new Vector2(380f, 14f), new Vector2(188f, 48f), () => RouteRequested?.Invoke());
            clearButton = Action(bottomRect, "Clear Route", "ROTAYI SİL  [DEL]", 18,
                new Vector2(576f, 14f), new Vector2(150f, 48f), () => ClearRouteRequested?.Invoke());
            analysisButton = Action(bottomRect, "Analysis", "ANALİZ  [A]", 18,
                new Vector2(734f, 14f), new Vector2(170f, 48f), () => AnalysisRequested?.Invoke());

            layerRect = Panel(root, "Layer Rail", new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(28f, 34f), new Vector2(250f, 402f),
                new Vector2(0f, 0.5f));
            Label(layerRect, "Layer Heading", "HARİTA KATMANLARI", 18,
                StrategicMapTheme.PanelText, TextAnchor.UpperLeft,
                new Vector2(18f, -16f), new Vector2(214f, 26f));
            Label(layerRect, "Layer Hint", "Görünümü sadeleştir · veri değişmez", 13,
                StrategicMapTheme.MutedText, TextAnchor.UpperLeft,
                new Vector2(18f, -44f), new Vector2(214f, 38f));

            gridButton = Layer(layerRect, "Grid Layer", "IZGARA", 82f,
                () => ToggleLayer(StrategicMapDisplayLayer.Grid));
            routeLayerButton = Layer(layerRect, "Route Layer", "ROTALAR", 126f,
                () => ToggleLayer(StrategicMapDisplayLayer.Route));
            discoveryButton = Layer(layerRect, "Discovery Layer", "KEŞİF NOKTALARI", 170f,
                () => ToggleLayer(StrategicMapDisplayLayer.Discoveries));
            fleetButton = Layer(layerRect, "Fleet Layer", "FİLO", 214f,
                () => ToggleLayer(StrategicMapDisplayLayer.Fleet));
            logisticsButton = Layer(layerRect, "Logistics Layer", "LOJİSTİK", 258f,
                () => ToggleLayer(StrategicMapDisplayLayer.Logistics));
            threatButton = Layer(layerRect, "Threat Layer", "TEHDİT", 302f,
                () => ToggleLayer(StrategicMapDisplayLayer.Threat));
            ambientButton = Layer(layerRect, "Ambient Layer", "ORTAM DETAYI", 346f,
                () => ToggleLayer(StrategicMapDisplayLayer.Ambient));

            toast = Label(root, "Command Toast", "", 18, StrategicMapTheme.Green,
                TextAnchor.MiddleCenter, new Vector2(0f, 188f), new Vector2(680f, 40f),
                centeredBottom: true);
            toast.gameObject.SetActive(false);
            RefreshLayerLabels();
        }

        public void Render(in StrategicMapDeckState state)
        {
            targetTitle.text = string.IsNullOrEmpty(state.TargetTitle)
                ? "HEDEF SEÇİLMEDİ" : state.TargetTitle.ToUpperInvariant();
            targetMeta.text = string.IsNullOrEmpty(state.TargetMeta)
                ? "Bir sistem veya gezegen seç" : state.TargetMeta;
            routeSummary.text = string.IsNullOrEmpty(state.RouteSummary) ? "ROTA · YOK" : state.RouteSummary;
            analysisSummary.text = string.IsNullOrEmpty(state.AnalysisSummary)
                ? "ANALİZ · BEKLEMEDE" : state.AnalysisSummary;

            Style(focusButton, true, false);
            Style(overviewButton, true, false);
            routeButton.Label.text = string.IsNullOrEmpty(state.RouteActionLabel)
                ? "ROTA BELİRLE  [ENTER]" : state.RouteActionLabel;
            Style(routeButton, state.CanRoute, false);
            Style(clearButton, state.HasRoute, state.HasRoute);
            analysisButton.Label.text = state.AnalysisActive ? "ANALİZİ İPTAL  [A]" : "ANALİZ  [A]";
            Style(analysisButton, state.CanAnalyze || state.AnalysisActive, state.AnalysisActive,
                StrategicMapTheme.Violet);
        }

        public void ShowStatus(string message, bool success)
        {
            if (string.IsNullOrEmpty(message)) return;
            toast.text = message;
            toast.color = success ? StrategicMapTheme.Green : StrategicMapTheme.Gold;
            toast.gameObject.SetActive(true);
            CancelInvoke(nameof(HideToast));
            Invoke(nameof(HideToast), 3.2f);
        }

        private void HideToast()
        {
            if (toast != null) toast.gameObject.SetActive(false);
        }

        public bool ContainsScreenPoint(Vector2 screenPoint) =>
            (bottomRect != null && RectTransformUtility.RectangleContainsScreenPoint(bottomRect, screenPoint, null))
            || (layerRect != null && RectTransformUtility.RectangleContainsScreenPoint(layerRect, screenPoint, null));

        private void ToggleLayer(StrategicMapDisplayLayer layer)
        {
            bool value;
            switch (layer)
            {
                case StrategicMapDisplayLayer.Grid: value = gridVisible = !gridVisible; break;
                case StrategicMapDisplayLayer.Route: value = routeVisible = !routeVisible; break;
                case StrategicMapDisplayLayer.Discoveries: value = discoveriesVisible = !discoveriesVisible; break;
                case StrategicMapDisplayLayer.Fleet: value = fleetVisible = !fleetVisible; break;
                case StrategicMapDisplayLayer.Logistics: value = logisticsVisible = !logisticsVisible; break;
                case StrategicMapDisplayLayer.Threat: value = threatVisible = !threatVisible; break;
                default: value = ambientVisible = !ambientVisible; break;
            }
            RefreshLayerLabels();
            LayerChanged?.Invoke(layer, value);
        }

        private void RefreshLayerLabels()
        {
            LayerLabel(gridButton, "IZGARA", gridVisible);
            LayerLabel(routeLayerButton, "ROTALAR", routeVisible);
            LayerLabel(discoveryButton, "KEŞİF NOKTALARI", discoveriesVisible);
            LayerLabel(fleetButton, "FİLO", fleetVisible, StrategicMapTheme.Cyan);
            LayerLabel(logisticsButton, "LOJİSTİK", logisticsVisible, StrategicMapTheme.Green);
            LayerLabel(threatButton, "TEHDİT", threatVisible, StrategicMapTheme.Red);
            LayerLabel(ambientButton, "ORTAM DETAYI", ambientVisible);
        }

        private static void LayerLabel(Control control, string label, bool active, Color? accent = null)
        {
            // ♦/◊ rather than ◆/◇: LiberationSans.ttf, the only face in the
            // project, has no glyph for U+25C6 or U+25C7, so both states drew the
            // same empty box and the on/off marker carried nothing. These two are
            // in the face and keep the filled/hollow reading, and they are the
            // same pair the strategic map legend uses for system/signal, so a
            // filled diamond means the same thing in both places. Do not restore
            // the originals; they cannot be drawn.
            control.Label.text = (active ? "♦  " : "◊  ") + label + (active ? "    AÇIK" : "    KAPALI");
            Style(control, true, active, accent);
        }

        private static void Style(Control control, bool available, bool active, Color? accent = null)
        {
            if (control == null) return;
            control.Button.interactable = available;
            Color color = accent ?? StrategicMapTheme.Cyan;
            control.Background.color = !available
                ? StrategicMapTheme.WithAlpha(StrategicMapTheme.Panel, 0.72f)
                : active
                    ? StrategicMapTheme.WithAlpha(color, 0.30f)
                    : StrategicMapTheme.WithAlpha(StrategicMapTheme.PanelText, 0.075f);
            control.Label.color = !available ? StrategicMapTheme.WithAlpha(StrategicMapTheme.MutedText, 0.55f)
                : active ? color : StrategicMapTheme.PanelText;
        }

        private static RectTransform Panel(RectTransform parent, string name, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 position, Vector2 size, Vector2 pivot)
        {
            Image image = StrategicMapScreen.CreateImage(parent, name, StrategicMapTheme.PanelFill);
            image.sprite = StrategicMapShapes.Panel(20);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Control Action(RectTransform parent, string name, string text, int fontSize,
            Vector2 position, Vector2 size, Action callback)
        {
            Image background = StrategicMapScreen.CreateImage(parent, name,
                StrategicMapTheme.WithAlpha(StrategicMapTheme.PanelText, 0.075f));
            background.sprite = StrategicMapShapes.Panel(12);
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;
            RectTransform rect = background.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.78f, 0.90f, 0.95f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.72f);
            colors.fadeDuration = StrategicMapTheme.FastTransitionSeconds;
            button.colors = colors;
            button.navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Automatic
            };
            button.onClick.AddListener(() => callback?.Invoke());

            Text label = StrategicMapScreen.CreateText(rect, "Label", text, fontSize,
                StrategicMapTheme.PanelText, TextAnchor.MiddleCenter);
            StrategicMapScreen.Stretch(label.rectTransform);
            return new Control { Button = button, Background = background, Label = label };
        }

        private static Control Layer(RectTransform parent, string name, string text, float top, Action callback)
        {
            Control control = Action(parent, name, text, 16, new Vector2(14f, -top),
                new Vector2(222f, 40f), callback);
            RectTransform rect = control.Background.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            return control;
        }

        private static Text Label(RectTransform parent, string name, string text, int size, Color color,
            TextAnchor alignment, Vector2 position, Vector2 dimensions, bool right = false,
            bool centeredBottom = false)
        {
            Text label = StrategicMapScreen.CreateText(parent, name, text, size, color, alignment);
            RectTransform rect = label.rectTransform;
            if (centeredBottom)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
                rect.pivot = right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            }
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            return label;
        }
    }
}
