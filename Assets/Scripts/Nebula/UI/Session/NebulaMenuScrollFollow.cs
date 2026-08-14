using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nebula.UI.Session
{
    /// <summary>
    /// Keeps a scrolling column's window on whatever is currently selected.
    /// <para>
    /// A <see cref="ScrollRect"/> follows the pointer and the wheel and nothing else.
    /// Move the selection with the keyboard or a controller and Unity will happily
    /// focus a control that is scrolled out of sight, leaving the player operating a
    /// menu they cannot see — pressing down past the last visible entry appears to do
    /// nothing, then Enter starts something they never read.
    /// </para>
    /// <para>
    /// Polling <see cref="EventSystem.currentSelectedGameObject"/> rather than
    /// implementing <c>ISelectHandler</c> on every entry: the entries are built by the
    /// UI factory and adding an interface to them would put navigation plumbing into
    /// every button in the game. This watches one value and does nothing at all until
    /// it changes.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class NebulaMenuScrollFollow : MonoBehaviour
    {
        private ScrollRect scroller;
        private RectTransform viewport;
        private RectTransform content;
        private GameObject lastSelected;

        /// <summary>Padding kept between a focused entry and the window edge, in canvas units.</summary>
        private const float Margin = 8f;

        /// <summary>Test-facing: how many times the window has been moved to reveal a selection.</summary>
        public int Follows { get; private set; }

        public static NebulaMenuScrollFollow Attach(RectTransform frame, ScrollRect scroller,
            RectTransform content)
        {
            if (frame == null || scroller == null || content == null) return null;

            NebulaMenuScrollFollow follow = frame.gameObject.GetComponent<NebulaMenuScrollFollow>();
            if (follow == null) follow = frame.gameObject.AddComponent<NebulaMenuScrollFollow>();

            follow.scroller = scroller;
            follow.viewport = frame;
            follow.content = content;
            return follow;
        }

        private void Update()
        {
            if (scroller == null || content == null || viewport == null) return;

            EventSystem events = EventSystem.current;
            GameObject selected = events == null ? null : events.currentSelectedGameObject;
            if (selected == null || selected == lastSelected) return;

            lastSelected = selected;
            if (!selected.transform.IsChildOf(content)) return;

            Reveal((RectTransform)selected.transform);
        }

        /// <summary>
        /// Slides the window the minimum distance that brings a rect fully inside it.
        /// Public so a fixture can drive it without an EventSystem, and so a future
        /// "jump to entry" affordance has something to call.
        /// </summary>
        public void Reveal(RectTransform target)
        {
            if (target == null || scroller == null || content == null || viewport == null) return;

            float windowHeight = viewport.rect.height;
            float contentHeight = content.rect.height;
            float hidden = contentHeight - windowHeight;
            if (hidden <= 0f) return;                 // everything fits; nothing to reveal

            // Distance from the content's top edge down to the target's edges.
            float top = -target.anchoredPosition.y;
            float bottom = top + target.rect.height;

            float offset = content.anchoredPosition.y;      // how far the sheet has been pulled up
            float visibleTop = offset;
            float visibleBottom = offset + windowHeight;

            float move = 0f;
            if (top - Margin < visibleTop) move = top - Margin - visibleTop;
            else if (bottom + Margin > visibleBottom) move = bottom + Margin - visibleBottom;
            if (Mathf.Approximately(move, 0f)) return;

            float wanted = Mathf.Clamp(offset + move, 0f, hidden);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, wanted);
            Follows++;
        }
    }
}
