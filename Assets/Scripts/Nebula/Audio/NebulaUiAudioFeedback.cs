using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.Ship;

namespace Nebula.Audio
{
    /// <summary>
    /// One global UI feedback adapter. It observes the active EventSystem
    /// rather than adding an AudioSource/listener to every generated button,
    /// so dynamic panels and restarted sessions cannot accumulate handlers.
    /// </summary>
    public sealed class NebulaUiAudioFeedback : MonoBehaviour
    {
        private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
        private new NebulaAudioService audio;
        private GameObject lastHovered;
        private GameObject lastSelected;
        private object lastModalOwner;
        private float lastHoverAt;
        private bool alternateHover;

        public void Initialize(NebulaAudioService service) => audio = service;

        private void Update()
        {
            if (audio == null || EventSystem.current == null || Cursor.lockState == CursorLockMode.Locked)
            {
                lastHovered = null;
                return;
            }

            GameObject hovered = RaycastTop();
            if (hovered != lastHovered)
            {
                lastHovered = hovered;
                if (hovered != null && Time.unscaledTime - lastHoverAt >= 0.045f)
                {
                    alternateHover = !alternateHover;
                    audio.PlayOneShot(alternateHover
                        ? "audio.ui.hover.01" : "audio.ui.hover.02", volume: 0.70f);
                    lastHoverAt = Time.unscaledTime;
                }
            }

            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != lastSelected)
            {
                if (selected != null && lastSelected != null)
                    audio.PlayOneShot("audio.ui.selection_move", volume: 0.72f);
                lastSelected = selected;
            }

            if (hovered != null && Input.GetMouseButtonDown(0))
                audio.PlayOneShot(IsDeniedControl(hovered)
                    ? "audio.ui.denied" : "audio.ui.confirm");

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
                audio.PlayOneShot("audio.ui.cancel", volume: 0.78f);

            if (hovered != null && Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f)
                audio.PlayOneShot("audio.ui.slider_tick", volume: 0.65f);

            object owner = ExclusiveModal.Owner;
            if (!ReferenceEquals(owner, lastModalOwner))
            {
                audio.PlayOneShot(owner != null
                    ? "audio.ui.panel_open" : "audio.ui.panel_close", volume: 0.76f);
                lastModalOwner = owner;
            }
        }

        private GameObject RaycastTop()
        {
            var eventData = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };
            raycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, raycastResults);
            return raycastResults.Count > 0 ? raycastResults[0].gameObject : null;
        }

        private static bool IsDeniedControl(GameObject target)
        {
            Selectable selectable = target.GetComponentInParent<Selectable>();
            return selectable != null && !selectable.IsInteractable();
        }
    }
}
