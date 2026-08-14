using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;

namespace Nebula.UI
{
    /// <summary>
    /// Development/debug observability panel (VS-008 §9), 0 toggle: manifest
    /// cache/persisted counts, current Full/Warm system id, live real
    /// GameObject counts, and the last promotion/demotion duration. Purely
    /// diagnostic — never gameplay-relevant.
    /// </summary>
    public class SectorDebugHud : MonoBehaviour
    {
        private static readonly Color TextColor = new Color(0.6f, 1f, 0.7f, 0.95f);
        private static readonly Color PanelColor = new Color(0.0f, 0.06f, 0.03f, 0.65f);

        private SystemStreamingDirector streaming;
        private Canvas canvas;
        private Font font;
        private Text bodyText;
        private float refreshTimer;

        public static SectorDebugHud Create(SystemStreamingDirector streamingDirector)
        {
            var go = new GameObject("Sector Debug HUD");
            var hud = go.AddComponent<SectorDebugHud>();
            hud.streaming = streamingDirector;
            hud.Build();
            return hud;
        }

        private void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var panelRect = new GameObject("Panel").AddComponent<RectTransform>();
            panelRect.SetParent(canvas.transform, false);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(12f, -12f);
            panelRect.sizeDelta = new Vector2(520f, 190f);
            panelRect.gameObject.AddComponent<Image>().color = PanelColor;

            var textRect = new GameObject("Body").AddComponent<RectTransform>();
            textRect.SetParent(panelRect, false);
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 10f); textRect.offsetMax = new Vector2(-10f, -10f);
            bodyText = textRect.gameObject.AddComponent<Text>();
            bodyText.font = font; bodyText.fontSize = 13; bodyText.color = TextColor;
            bodyText.alignment = TextAnchor.UpperLeft;
            bodyText.horizontalOverflow = HorizontalWrapMode.Overflow;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;

            canvas.enabled = false;
        }

        private void Update()
        {
            if (!InputGate.Suppressed && Input.GetKeyDown(NebulaInputBindings.SectorDebugToggleKey)) canvas.enabled = !canvas.enabled;
            if (!canvas.enabled) return;

            refreshTimer -= Time.deltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = 0.5f;

            int cached = StarSystemRegistry.Manifests?.CachedSectorCount ?? 0;
            int persistedDeltas = StarSystemRegistry.Deltas?.PersistedCount ?? 0;

            int planetCount = 0; foreach (var _ in ExtractionState.AllPlanets) planetCount++;

            var sb = new System.Text.StringBuilder(300);
            sb.Append("[0] SEKTÖR DEBUG\n");
            sb.Append($"Manifest cache: {cached}   ·   Persisted delta: {persistedDeltas}\n");
            sb.Append($"FULL: {streaming?.FullSystemId ?? "-"}\n");
            sb.Append($"WARM: {streaming?.WarmSystemId ?? "-"}\n");
            sb.Append($"Canlı gezegen: {planetCount}   ·   Drill: {ExtractionState.Drills.Count}   ·   Structure: {ExtractionState.Structures.Count}\n");
            sb.Append($"Son promotion: {streaming?.LastPromotionMs ?? 0f:0.00}ms   ·   Son demotion: {streaming?.LastDemotionMs ?? 0f:0.00}ms\n");
            bodyText.text = sb.ToString();
        }
    }
}
