using UnityEngine;
using UnityEngine.EventSystems;

namespace Nebula.UI.Shell
{
    /// <summary>
    /// Shows a control's own refusal while the pointer is over it.
    /// <para>
    /// Attached by the factory to every control that can be disabled, so the
    /// reason a service gave is reachable rather than merely stored.
    /// </para>
    /// <para>
    /// In its own file, and it has to be. Unity creates a <c>MonoScript</c> asset
    /// only for the type whose name matches the file, so while this lived at the
    /// bottom of <c>NebulaTooltip.cs</c> it had no script asset to point at.
    /// <c>AddComponent</c> still worked at runtime, which is why nothing noticed —
    /// but saving it into a prefab wrote <c>m_Script: {fileID: 0}</c>, and every
    /// instantiate of that prefab logged a missing-script error. Six authored
    /// prefabs carried it, one per button, and the log flood alone was enough to
    /// fail an otherwise green test run.
    /// </para>
    /// </summary>
    public sealed class NebulaTooltipSource : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        /// <summary>Resolved on hover, so a reason that changes is never stale.</summary>
        public System.Func<string> Reason;

        public void OnPointerEnter(PointerEventData eventData) => Publish(eventData.position);

        public void OnPointerExit(PointerEventData eventData) => NebulaTooltip.Active?.Hide();

        /// <summary>
        /// Focus is the keyboard's and the pad's pointer.
        /// <para>
        /// Without this the reason was mouse-only: a player on a stick or on the
        /// arrow keys could put a control under the cursor the interface draws and
        /// still be told nothing, because the only door into the tooltip was
        /// <see cref="OnPointerEnter"/>. A refusal that one input device can read
        /// and another cannot is the same defect as a refusal with no reason at
        /// all, one class of player later.
        /// </para>
        /// <para>
        /// Note the event type: select carries no screen position, so the anchor is
        /// derived from this control's own rect rather than from the event —
        /// <see cref="BaseEventData"/> genuinely has none to give.
        /// </para>
        /// </summary>
        public void OnSelect(BaseEventData eventData) => Publish(FocusAnchor());

        public void OnDeselect(BaseEventData eventData) => NebulaTooltip.Active?.Hide();

        /// <summary>
        /// Shows the live reason, or leaves the screen alone. Silence is the whole
        /// point of the empty case: a control that is not refusing has nothing to
        /// explain, and a tooltip that appeared anyway would make every assertion
        /// about "does this control explain itself" meaningless.
        /// </summary>
        private void Publish(Vector2 screenPoint)
        {
            string text = Reason?.Invoke();
            if (string.IsNullOrEmpty(text)) return;
            NebulaTooltip.Active?.Show(text, screenPoint);
        }

        /// <summary>
        /// This control's bottom-left corner in screen space — where the panel would
        /// have been put had a pointer entered here, so a focused control and a
        /// hovered one place the tooltip the same way.
        /// </summary>
        private Vector2 FocusAnchor()
        {
            if (!(transform is RectTransform rect)) return Vector2.zero;

            rect.GetWorldCorners(corners);
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        }

        /// <summary>Reused so focusing a control allocates nothing.</summary>
        private static readonly Vector3[] corners = new Vector3[4];
    }
}
