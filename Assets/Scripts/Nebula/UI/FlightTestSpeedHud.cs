using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Ship;

namespace Nebula.UI
{
    /// <summary>
    /// DT-001: a small, separate, non-modal developer panel that lets a
    /// tester dial the ship's session-local flight test speed multiplier
    /// (1×–100×, logarithmic) to fly through manual TF-002/VS-012
    /// streaming/collision smoke tests in seconds instead of minutes. `J`
    /// toggles it. It never pauses the game, never touches
    /// <see cref="Time.timeScale"/>, never acquires <see cref="ExclusiveModal"/>,
    /// and is only ever created in the Editor or a Development Build —
    /// <see cref="Composition.PresentationInstaller"/> is the sole gate for
    /// that; this class assumes it was only asked to exist where allowed.
    /// </summary>
    public class FlightTestSpeedHud : MonoBehaviour
    {
        private static readonly Color TagColor = new Color(1f, 0.75f, 0.25f, 0.95f); // amber "TEST ONLY"
        private static readonly Color TextColor = new Color(0.7f, 1f, 0.75f, 0.95f); // green test-tool text
        private static readonly Color DimColor = new Color(0.7f, 1f, 0.75f, 0.6f);
        private static readonly Color PanelColor = new Color(0.05f, 0.1f, 0.05f, 0.75f);
        private static readonly Color TrackColor = new Color(0.1f, 0.18f, 0.1f, 0.9f);
        private static readonly Color FillColor = new Color(0.55f, 0.95f, 0.55f, 0.9f);
        private static readonly Color ButtonColor = new Color(0.12f, 0.22f, 0.12f, 0.9f);

        /// <summary>The sole owner of KeyCode.J -- ShipPoi's Sensor Relay shortcut moved to Z (DT-001) specifically to keep this the only J consumer.</summary>
        public const KeyCode ToggleKey = KeyCode.J;

        private ShipController ship;
        private Canvas canvas;
        private Font font;
        private GameObject panel;
        private Text multiplierText;
        private Text actualText;

        public Slider SpeedSlider { get; private set; }
        public Button ResetButton { get; private set; }

        public bool IsVisible => panel != null && panel.activeSelf;

        /// <summary>
        /// The whole-unit speed the ACTUAL line currently reads, or
        /// <see cref="int.MinValue"/> when nothing has been shown yet (P2-PERF-004).
        /// A measurement tool that allocates twice per frame while it is up is adding
        /// noise to the very thing it is measuring.
        /// </summary>
        private int shownActual = int.MinValue;

        /// <summary>Test-facing: how many times the ACTUAL line was actually rebuilt.</summary>
        public int ActualLabelBuilds { get; private set; }

        public static FlightTestSpeedHud Create(ShipController shipController)
        {
            var go = new GameObject("Flight Test Speed HUD");
            var hud = go.AddComponent<FlightTestSpeedHud>();
            hud.ship = shipController;
            hud.Build();
            return hud;
        }

        private void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 70; // above every other HUD (highest existing is SectorDebugHud at 60), still below modal Screens (50-52 use their own separate canvases so this never blocks them)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            gameObject.AddComponent<GraphicRaycaster>();

            if (EventSystem.current == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            var panelRect = new GameObject("Test Speed Panel").AddComponent<RectTransform>();
            panelRect.SetParent(canvas.transform, false);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -100f); // below ProgressionHud's top-center bar, never overlapping it
            panelRect.sizeDelta = new Vector2(440f, 150f);
            panelRect.gameObject.AddComponent<Image>().color = PanelColor;
            panel = panelRect.gameObject;

            CreateText(panelRect, "Tag", "TEST ONLY", 12, FontStyle.Bold, TagColor,
                new Vector2(14f, -8f), new Vector2(120f, 20f));
            CreateText(panelRect, "Hint", "[J] TEST FLIGHT SPEED", 13, FontStyle.Bold, TextColor,
                new Vector2(140f, -8f), new Vector2(290f, 20f));

            SpeedSlider = BuildSlider(panelRect, new Vector2(14f, -38f), new Vector2(412f, 20f));
            SpeedSlider.onValueChanged.AddListener(OnSliderChanged);

            multiplierText = CreateText(panelRect, "Multiplier", "TEST SPEED  ×1.0", 16, FontStyle.Bold, TextColor,
                new Vector2(14f, -68f), new Vector2(412f, 24f));
            actualText = CreateText(panelRect, "Actual", "ACTUAL  0 u/s", 13, FontStyle.Normal, DimColor,
                new Vector2(14f, -94f), new Vector2(300f, 22f));

            ResetButton = BuildResetButton(panelRect, new Vector2(-14f, -94f));

            panel.SetActive(false);
        }

        private Slider BuildSlider(RectTransform parent, Vector2 position, Vector2 size)
        {
            var sliderGO = new GameObject("Speed Slider");
            var sliderRect = sliderGO.AddComponent<RectTransform>();
            sliderRect.SetParent(parent, false);
            sliderRect.anchorMin = sliderRect.anchorMax = new Vector2(0f, 1f);
            sliderRect.pivot = new Vector2(0f, 1f);
            sliderRect.anchoredPosition = position;
            sliderRect.sizeDelta = size;
            var slider = sliderGO.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;

            var background = new GameObject("Background").AddComponent<RectTransform>();
            background.SetParent(sliderRect, false);
            background.anchorMin = Vector2.zero; background.anchorMax = Vector2.one;
            background.offsetMin = Vector2.zero; background.offsetMax = Vector2.zero;
            background.gameObject.AddComponent<Image>().color = TrackColor;

            var fillArea = new GameObject("Fill Area").AddComponent<RectTransform>();
            fillArea.SetParent(sliderRect, false);
            fillArea.anchorMin = new Vector2(0f, 0.25f); fillArea.anchorMax = new Vector2(1f, 0.75f);
            fillArea.offsetMin = new Vector2(4f, 0f); fillArea.offsetMax = new Vector2(-16f, 0f);

            var fill = new GameObject("Fill").AddComponent<RectTransform>();
            fill.SetParent(fillArea, false);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f);
            fill.sizeDelta = new Vector2(10f, 0f);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = FillColor;
            slider.fillRect = fill;

            var handleArea = new GameObject("Handle Slide Area").AddComponent<RectTransform>();
            handleArea.SetParent(sliderRect, false);
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(8f, 0f); handleArea.offsetMax = new Vector2(-8f, 0f);

            var handle = new GameObject("Handle").AddComponent<RectTransform>();
            handle.SetParent(handleArea, false);
            handle.sizeDelta = new Vector2(16f, size.y + 6f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Color.white;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;

            return slider;
        }

        private Button BuildResetButton(RectTransform parent, Vector2 position)
        {
            var buttonRect = new GameObject("Reset Button").AddComponent<RectTransform>();
            buttonRect.SetParent(parent, false);
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(1f, 1f);
            buttonRect.anchoredPosition = position;
            buttonRect.sizeDelta = new Vector2(110f, 24f);
            buttonRect.gameObject.AddComponent<Image>().color = ButtonColor;
            var button = buttonRect.gameObject.AddComponent<Button>();

            CreateText(buttonRect, "Label", "RESET 1×", 13, FontStyle.Bold, TextColor,
                Vector2.zero, buttonRect.sizeDelta, TextAnchor.MiddleCenter);

            button.onClick.AddListener(OnResetClicked);
            return button;
        }

        private Text CreateText(RectTransform parent, string name, string content, int size,
            FontStyle style, Color color, Vector2 position, Vector2 area, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = area;
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.text = content;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Opens/closes the panel — the same path `J` uses, exposed so tests never need to simulate a keypress.</summary>
        public void SetVisible(bool visible)
        {
            if (panel == null) return;
            panel.SetActive(visible);
            if (visible)
            {
                // Release through the ship's ownership state, not only Unity's global
                // cursor. Changing Cursor directly left ShipController.cursorCaptured
                // true, so a click outside this panel could never take the pointer back.
                // Never force-relock on close (TF spec): the player recaptures by
                // clicking the game view, exactly as they do after Escape.
                if (ship != null) ship.ReleasePointerAim();
                else
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
        }

        private void OnSliderChanged(float value01)
        {
            if (ship == null) return;
            float multiplier = FlightTestSpeedMath.MultiplierFromSlider(value01);
            ship.SetTestSpeedMultiplier(multiplier);
            RefreshMultiplierLabel(multiplier);
        }

        private void OnResetClicked()
        {
            if (ship == null) return;
            ship.ResetTestSpeedMultiplier();
            // SetValueWithoutNotify -- must never re-enter OnSliderChanged (which
            // would re-apply the multiplier a second time via a different path).
            SpeedSlider.SetValueWithoutNotify(0f);
            RefreshMultiplierLabel(FlightTestSpeedMath.MinMultiplier);
        }

        private void RefreshMultiplierLabel(float multiplier)
        {
            multiplierText.text = $"TEST SPEED  ×{multiplier:0.0}";
        }

        private void Update()
        {
            // The visibility gate stays exactly where it was, and so does the release
            // gate above it: PresentationInstaller only ever constructs this HUD inside
            // `if (Debug.isDebugBuild)`, so on a player build the component does not
            // exist and this Update never runs at all. NebulaReleaseVisibilityProbe
            // checks that from inside a shipped player.
            if (!InputGate.Suppressed && Input.GetKeyDown(ToggleKey)) SetVisible(!IsVisible);
            if (ship == null || !IsVisible) return;

            // Rounded first, compared second. The label has always shown whole units, so
            // a speed drifting between 412.1 and 412.4 produced a new string every frame
            // and an identical picture. FloorToInt(v + 0.5f) is round-half-up on a
            // non-negative magnitude — the same value "N0" would have printed — so the
            // text is unchanged, only the number of times it is built.
            int actual = Mathf.FloorToInt(ship.Velocity.magnitude + 0.5f);
            if (actual == shownActual) return;

            shownActual = actual;
            actualText.text = $"ACTUAL  {actual.ToString("N0", CultureInfo.InvariantCulture)} u/s";
            ActualLabelBuilds++;
        }
    }
}
