using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Visuals;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// The universe/sector graph: known systems as nodes, known relationships as thin
    /// edges, drawn Obsidian-style with fluid pan/zoom driven by
    /// <see cref="StrategicMapCamera"/>.
    ///
    /// Everything is pooled and culled: node and edge widgets are allocated once and
    /// reused, off-screen nodes are skipped, and there is a hard visible-node cap so a
    /// very large discovered galaxy can never blow the frame budget. Nothing here is
    /// loaded per frame — icons come from the cached
    /// <see cref="NebulaProducedVisualResolver"/>.
    ///
    /// It renders only what a <see cref="StrategicMapSnapshot"/> already permits: a
    /// Signal-level system has no real position, so it is drawn as a heading marker on
    /// the rim rather than placed as if it were surveyed.
    /// </summary>
    public sealed class StrategicMapGraphView : MonoBehaviour
    {
        private const int MaxNodes = 256;
        private const int MaxEdges = 384;
        /// <summary>Screen pixels per map unit at zoom 1.</summary>
        private const float PixelsPerMapUnit = 3.2f;
        private const float NodeCullMargin = 120f;

        public StrategicMapCamera Camera { get; } = new StrategicMapCamera();

        /// <summary>Raised when the player single-clicks a node.</summary>
        public event System.Action<string> SystemClicked;
        /// <summary>Raised when the player double-clicks a node (dive into the system).</summary>
        public event System.Action<string> SystemActivated;

        private RectTransform viewRect;
        private readonly List<NodeWidget> nodePool = new List<NodeWidget>();
        private readonly List<EdgeWidget> edgePool = new List<EdgeWidget>();

        private string selectedSystemId;
        private float lastClickTime;
        private string lastClickedId;

        private sealed class NodeWidget
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Dot;
            public Image KnowledgeIcon;
            public Image StateIcon;
            public Text Label;
            public Button Button;
            public string SystemId;
        }

        private sealed class EdgeWidget
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Line;
        }

        public static StrategicMapGraphView Create(RectTransform parent)
        {
            var go = new GameObject("Graph View");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(400f, 40f);   // clear of the left player panel
            rect.offsetMax = new Vector2(-40f, -110f); // clear of the breadcrumb

            var view = go.AddComponent<StrategicMapGraphView>();
            view.viewRect = rect;
            view.BuildPools();
            return view;
        }

        private void BuildPools()
        {
            // Edges first so they always draw behind the nodes.
            for (int i = 0; i < MaxEdges; i++) edgePool.Add(BuildEdge(i));
            for (int i = 0; i < MaxNodes; i++) nodePool.Add(BuildNode(i));
        }

        private EdgeWidget BuildEdge(int index)
        {
            var go = new GameObject($"Edge {index}");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(viewRect, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            var line = go.AddComponent<Image>();
            line.raycastTarget = false;
            go.SetActive(false);
            return new EdgeWidget { Root = go, Rect = rect, Line = line };
        }

        private NodeWidget BuildNode(int index)
        {
            var go = new GameObject($"Node {index}");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(viewRect, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(52f, 52f);

            var dot = go.AddComponent<Image>();
            dot.sprite = StrategicMapShapes.Disc; // a system marker is a disc, never a square
            dot.raycastTarget = true;
            var button = go.AddComponent<Button>();
            button.targetGraphic = dot;

            var widget = new NodeWidget { Root = go, Rect = rect, Dot = dot, Button = button };
            button.onClick.AddListener(() => OnNodeClicked(widget));

            widget.KnowledgeIcon = StrategicMapScreen.CreateImage(rect, "Knowledge", Color.white);
            widget.KnowledgeIcon.rectTransform.anchorMin = widget.KnowledgeIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            widget.KnowledgeIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            widget.KnowledgeIcon.rectTransform.anchoredPosition = new Vector2(-20f, 20f);
            widget.KnowledgeIcon.rectTransform.sizeDelta = new Vector2(22f, 22f);
            widget.KnowledgeIcon.preserveAspect = true;

            widget.StateIcon = StrategicMapScreen.CreateImage(rect, "State", Color.white);
            widget.StateIcon.rectTransform.anchorMin = widget.StateIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            widget.StateIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            widget.StateIcon.rectTransform.anchoredPosition = new Vector2(20f, 20f);
            widget.StateIcon.rectTransform.sizeDelta = new Vector2(22f, 22f);
            widget.StateIcon.preserveAspect = true;

            widget.Label = StrategicMapScreen.CreateText(rect, "Label", "", StrategicMapTheme.CaptionSize,
                StrategicMapTheme.SpaceText, TextAnchor.UpperCenter);
            widget.Label.rectTransform.anchorMin = widget.Label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            widget.Label.rectTransform.pivot = new Vector2(0.5f, 1f);
            widget.Label.rectTransform.anchoredPosition = new Vector2(0f, -30f);
            widget.Label.rectTransform.sizeDelta = new Vector2(200f, 22f);

            go.SetActive(false);
            return widget;
        }

        private void OnNodeClicked(NodeWidget widget)
        {
            if (string.IsNullOrEmpty(widget.SystemId)) return;

            bool isDoubleClick = widget.SystemId == lastClickedId && Time.unscaledTime - lastClickTime < 0.35f;
            lastClickedId = widget.SystemId;
            lastClickTime = Time.unscaledTime;

            if (isDoubleClick) SystemActivated?.Invoke(widget.SystemId);
            else SystemClicked?.Invoke(widget.SystemId);
        }

        public void SetSelected(string systemId) => selectedSystemId = systemId;

        /// <summary>Smoothly glides the view to a system's own map position.</summary>
        public void FocusSystem(StrategicSystemView system)
        {
            if (!system.PositionKnown) return;
            Camera.FocusOn(StrategicMapCamera.WorldToMap(system.AbsoluteCenter), StrategicMapCamera.DetailZoomThreshold);
        }

        private void Update() => Camera.Tick(Time.unscaledDeltaTime);

        /// <summary>Redraws the graph from an immutable snapshot. Allocates nothing once the pools are warm.</summary>
        public void Render(StrategicMapSnapshot snapshot)
        {
            if (snapshot == null) return;

            Vector2 half = viewRect.rect.size * 0.5f;
            int nodeCount = 0;
            int edgeCount = 0;

            // --- edges (drawn first, behind the nodes) --------------------------------
            for (int i = 0; i < snapshot.Links.Count && edgeCount < edgePool.Count; i++)
            {
                StrategicLinkView link = snapshot.Links[i];
                if (!snapshot.TryGetSystem(link.FromSystemId, out StrategicSystemView from) || !from.PositionKnown) continue;
                if (!snapshot.TryGetSystem(link.ToSystemId, out StrategicSystemView to) || !to.PositionKnown) continue;

                Vector2 a = Camera.MapToScreen(StrategicMapCamera.WorldToMap(from.AbsoluteCenter), PixelsPerMapUnit);
                Vector2 b = Camera.MapToScreen(StrategicMapCamera.WorldToMap(to.AbsoluteCenter), PixelsPerMapUnit);
                if (!OnScreen(a, half, NodeCullMargin) && !OnScreen(b, half, NodeCullMargin)) continue;

                EdgeWidget edge = edgePool[edgeCount++];
                edge.Root.SetActive(true);
                Vector2 delta = b - a;
                edge.Rect.anchoredPosition = a;
                edge.Rect.sizeDelta = new Vector2(delta.magnitude, link.IsActiveRoute ? 3f : 1.5f);
                edge.Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                edge.Line.color = link.IsActiveRoute
                    ? new Color(StrategicMapTheme.Gold.r, StrategicMapTheme.Gold.g, StrategicMapTheme.Gold.b, 0.85f)
                    : new Color(StrategicMapTheme.Blue.r, StrategicMapTheme.Blue.g, StrategicMapTheme.Blue.b, 0.22f);
            }

            // --- nodes ----------------------------------------------------------------
            // A node without its name is an anonymous box, so labels appear as soon as the
            // map is showing individual systems at all -- only the far-out cluster tier hides them.
            bool showLabels = Camera.Tier != StrategicZoomTier.Cluster;
            bool showBadges = Camera.Tier != StrategicZoomTier.Cluster;

            for (int i = 0; i < snapshot.Systems.Count && nodeCount < nodePool.Count; i++)
            {
                StrategicSystemView system = snapshot.Systems[i];

                Vector2 screen;
                if (system.PositionKnown)
                {
                    screen = Camera.MapToScreen(StrategicMapCamera.WorldToMap(system.AbsoluteCenter), PixelsPerMapUnit);
                    if (!OnScreen(screen, half, NodeCullMargin)) continue; // culled
                }
                else
                {
                    // Signal only: park the marker on the rim along its coarse heading.
                    Vector2 dir = new Vector2(system.ApproximateDirection.x, system.ApproximateDirection.z);
                    if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
                    screen = dir.normalized * Mathf.Min(half.x, half.y) * 0.86f;
                }

                NodeWidget node = nodePool[nodeCount++];
                node.SystemId = system.SystemId;
                node.Root.SetActive(true);
                node.Rect.anchoredPosition = screen;

                bool selected = system.SystemId == selectedSystemId;
                float scale = system.IsCurrentSystem ? 1.25f : selected ? 1.15f : 1f;
                if (system.HasAttention || system.HasRaidWarning) scale += StrategicMapTheme.Pulse(Time.unscaledTime, 0.12f);
                node.Rect.localScale = Vector3.one * scale;

                // A visited system shows its OWN real star texture; anything less-known keeps the
                // neutral marker, which is itself the information gate made visible.
                // The CORONA is the radial art (the surface map is equirectangular and would render
                // as a rectangle at this size), so a visited system reads as a real glowing star.
                Sprite starSprite = system.DetailsKnown ? StrategicMapVisualResolver.StarCorona(system.StarClass) : null;
                node.Dot.sprite = starSprite != null ? starSprite : StrategicMapShapes.Disc;
                node.Dot.color = starSprite != null
                    ? (selected || system.IsCurrentSystem ? Color.white : new Color(0.92f, 0.95f, 1f, 0.95f))
                    : NodeColor(system, selected);
                node.Label.gameObject.SetActive(showLabels);
                if (showLabels) node.Label.text = system.PositionKnown ? system.DisplayName : "SİNYAL";

                node.KnowledgeIcon.gameObject.SetActive(showBadges);
                if (showBadges)
                {
                    Sprite k = NebulaProducedVisualResolver.Knowledge(system.Knowledge);
                    node.KnowledgeIcon.sprite = k;
                    node.KnowledgeIcon.enabled = k != null; // controlled fallback, never a broken icon
                }

                // The state badge prefers a raid/attention warning, then the link state --
                // always a real produced icon, so colour is never the only signal.
                node.StateIcon.gameObject.SetActive(showBadges);
                if (showBadges)
                {
                    Sprite s = system.HasRaidWarning ? NebulaProducedVisualResolver.RaidWarningIcon()
                             : system.HasAttention ? NebulaProducedVisualResolver.AttentionIcon()
                             : NebulaProducedVisualResolver.Link(system.Link);
                    node.StateIcon.sprite = s;
                    node.StateIcon.enabled = s != null;
                }
            }

            for (int i = nodeCount; i < nodePool.Count; i++)
                if (nodePool[i].Root.activeSelf) { nodePool[i].Root.SetActive(false); nodePool[i].SystemId = null; }
            for (int i = edgeCount; i < edgePool.Count; i++)
                if (edgePool[i].Root.activeSelf) edgePool[i].Root.SetActive(false);
        }

        private static Color NodeColor(StrategicSystemView system, bool selected)
        {
            Color baseColor = system.DetailsKnown
                ? StrategicMapTheme.Ownership(system.Ownership)
                : StrategicMapTheme.Knowledge(system.Knowledge);
            if (system.IsCurrentSystem) baseColor = StrategicMapTheme.Cyan;
            if (selected) baseColor = Color.Lerp(baseColor, Color.white, 0.35f);
            // A stale (non-Live) system reads dimmer, and still carries its own link icon.
            if (system.IsStale) baseColor *= 0.72f;
            baseColor.a = 1f;
            return baseColor;
        }

        private static bool OnScreen(Vector2 screen, Vector2 half, float margin) =>
            Mathf.Abs(screen.x) <= half.x + margin && Mathf.Abs(screen.y) <= half.y + margin;

        // ------------------------------------------------------------------ input

        /// <summary>Drag-to-pan and wheel-to-zoom, fed by the owning screen so this view never polls input on its own.</summary>
        public void HandlePan(Vector2 screenDelta) => Camera.Pan(screenDelta, PixelsPerMapUnit);
        public void HandleRelease(Vector2 screenVelocity) => Camera.ReleaseWithMomentum(screenVelocity, PixelsPerMapUnit);
        public void HandleZoom(float wheelDelta, Vector2 cursorOffset) => Camera.ZoomBy(wheelDelta, cursorOffset, PixelsPerMapUnit);
    }
}
