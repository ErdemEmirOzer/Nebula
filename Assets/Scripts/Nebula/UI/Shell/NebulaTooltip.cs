using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.UI.Design;

namespace Nebula.UI.Shell
{
    /// <summary>
    /// The one tooltip, on the overlay layer.
    /// <para>
    /// Every disabled control in this interface already knows why it is disabled
    /// — <see cref="NebulaUiFactory.ButtonHandle.DisabledReason"/> is set from the
    /// service's own refusal, never from the UI's guess — and until now there was
    /// nowhere for that answer to appear. A control that refuses without saying
    /// why is the same defect as a refusal with no reason at all.
    /// </para>
    /// <para>
    /// One instance for the whole shell rather than one per control: a tooltip is
    /// a single thing that moves, and forty of them would be forty rebuild groups
    /// on the layer that is meant to stay cheap.
    /// </para>
    /// </summary>
    public sealed class NebulaTooltip : MonoBehaviour
    {
        /// <summary>Gap between the pointer and the panel's corner.</summary>
        public const float PointerOffset = 16f;

        private static NebulaTooltip active;

        private RectTransform root;
        private NebulaUiFactory.LabelHandle label;
        private Canvas canvas;

        /// <summary>Test-facing: what the tooltip currently reads, or <c>null</c>.</summary>
        public string Text => root != null && root.gameObject.activeSelf ? label.Text.text : null;

        /// <summary>Test-facing: whether anything is showing.</summary>
        public bool Visible => root != null && root.gameObject.activeSelf;

        public static NebulaTooltip Create(NebulaUiShell shell)
        {
            var go = new GameObject("Tooltip");
            var tooltip = go.AddComponent<NebulaTooltip>();
            tooltip.Build(shell);
            active = tooltip;
            return tooltip;
        }

        /// <summary>The shell's tooltip, or <c>null</c> before one is built.</summary>
        public static NebulaTooltip Active => active;

        private void Build(NebulaUiShell shell)
        {
            RectTransform layer = shell.Root(UiLayer.Overlay);
            canvas = shell.CanvasFor(UiLayer.Overlay);

            // The controller sits on the layer, never inside the panel it hides.
            transform.SetParent(layer, false);

            Image panel = NebulaUiFactory.Panel(layer, "Tooltip Panel", NebulaUiAssetCatalog.Tooltip);
            root = panel.rectTransform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = new Vector2(320f, 44f);

            label = NebulaUiFactory.Label(root, "Text", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleLeft, FontStyle.Normal,
                0f, wrap: true);
            NebulaUiFactory.Place(label.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(12f, -8f), new Vector2(296f, 28f));

            // Never a raycast target: a tooltip that swallows the click meant for
            // the control it is describing is worse than no tooltip.
            panel.raycastTarget = false;
            label.Text.raycastTarget = false;

            root.gameObject.SetActive(false);
        }

        public void Show(string text, Vector2 screenPosition)
        {
            if (root == null || string.IsNullOrEmpty(text)) { Hide(); return; }

            label.Value = text;
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            Place(screenPosition);
        }

        public void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Follows the pointer, and flips rather than leaving the screen: a tooltip
        /// half off the right edge has answered nobody.
        /// <para>
        /// Two coordinate systems meet here and used to be mixed.
        /// <see cref="RectTransformUtility.ScreenPointToLocalPointInRectangle"/>
        /// answers in the layer's own local space, whose origin is the layer's
        /// pivot — the centre of the screen. <see cref="RectTransform.anchoredPosition"/>
        /// is measured from this panel's anchor, which is the layer's <i>top-left</i>
        /// corner. Assigning the first straight into the second put the panel half a
        /// screen left and half a screen up from the pointer, so every control in the
        /// left-hand column explained itself off the edge of the display. The
        /// conversion below is the whole fix: the top-left corner is
        /// <c>(bounds.xMin, bounds.yMax)</c> in the same local space the flip logic
        /// is written in.
        /// </para>
        /// </summary>
        private void Place(Vector2 screenPosition)
        {
            RectTransform parent = (RectTransform)root.parent;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, screenPosition, camera, out Vector2 local))
                return;

            Vector2 size = root.sizeDelta;
            Rect bounds = parent.rect;

            float x = local.x + PointerOffset;
            if (x + size.x > bounds.xMax) x = local.x - PointerOffset - size.x;

            float y = local.y - PointerOffset;
            if (y - size.y < bounds.yMin) y = local.y + PointerOffset + size.y;

            // The flip handles the ordinary case; the clamp is what makes "never off
            // the screen" unconditional, for a pointer in a corner or a layer smaller
            // than the panel itself.
            x = Mathf.Clamp(x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - size.x));
            y = Mathf.Clamp(y, Mathf.Min(bounds.yMax, bounds.yMin + size.y), bounds.yMax);

            root.anchoredPosition = new Vector2(x - bounds.xMin, y - bounds.yMax);
        }
    }

}
