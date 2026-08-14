using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// The canonical 12 surface slots, drawn as illustrated marks over the planet's own
    /// portrait using each slot's real local surface normal — so the layout on the card
    /// matches where the structure actually sits on the world.
    ///
    /// Each state reads differently without relying on colour alone, and each has its own
    /// motion, because a state that only differs by hue is a state the player has to be
    /// told about rather than one they can see:
    /// <list type="bullet">
    /// <item>Active — the real structure prefab preview, sitting still on a solid ring.</item>
    /// <item>Constructing / Upgrading — a radial arc sweeping round the ring to the real
    /// progress figure, so how far along it is takes no reading.</item>
    /// <item>Damaged / Disabled — its own mark plus a warning pulse.</item>
    /// <item>Buildable — an open dashed-feeling ring that breathes, plainly an invitation.</item>
    /// <item>Locked — dim, still, and explicitly not offered.</item>
    /// </list>
    ///
    /// Every mark is drawn on a small glass disc so it stays legible over a bright limb or a
    /// dark night side, and the whole set staggers in when the card opens.
    /// </summary>
    public sealed class StrategicPlanetSlotsView : MonoBehaviour
    {
        private const int MaxSlots = 12;

        /// <summary>Where the ring of marks sits, as a fraction of the portrait's width.</summary>
        private const float RingRadiusFraction = 0.34f;

        private const float MarkSize = 40f;

        private RectTransform viewRect;
        private readonly List<SlotWidget> pool = new List<SlotWidget>();

        /// <summary>When the current set of marks started arriving, for the stagger.</summary>
        private float shownSince;
        private int shownCount;

        private sealed class SlotWidget
        {
            public GameObject Root;
            public RectTransform Rect;
            public CanvasGroup Group;
            public Image Backing;
            public Image Ring;
            public Image Progress;
            public Image Content;
            public Text Tag;
            public StrategicSlotState State;
        }

        public static StrategicPlanetSlotsView Create(RectTransform parent, Vector2 anchoredPosition, float size)
        {
            var go = new GameObject("Planet Slots");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(size, size);

            var view = go.AddComponent<StrategicPlanetSlotsView>();
            view.viewRect = rect;
            view.BuildUi();
            return view;
        }

        private void BuildUi()
        {
            for (int i = 0; i < MaxSlots; i++) pool.Add(BuildSlot(i));
        }

        private SlotWidget BuildSlot(int index)
        {
            var go = new GameObject($"Slot {index}");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(viewRect, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(MarkSize, MarkSize);
            var group = go.AddComponent<CanvasGroup>();

            // A glass disc under every mark: the portrait behind it is a lit world, and a
            // thin ring on its own vanishes over a bright limb.
            Image backing = StrategicMapScreen.CreateImage(rect, "Backing", new Color(0.02f, 0.03f, 0.06f, 0.62f));
            backing.sprite = StrategicMapShapes.Disc;
            StrategicMapScreen.Stretch(backing.rectTransform);

            Image ring = StrategicMapScreen.CreateImage(rect, "Ring", Color.white);
            ring.sprite = StrategicMapShapes.Ring(3); // a slot mark is a ring, never a filled square
            StrategicMapScreen.Stretch(ring.rectTransform);

            // A real radial arc rather than a bar: it reads as "this much of the way round"
            // at a glance, and it sits on the mark instead of beside it.
            Image progress = StrategicMapScreen.CreateImage(rect, "Progress", Color.clear);
            progress.sprite = StrategicMapShapes.Ring(5);
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Radial360;
            progress.fillOrigin = (int)Image.Origin360.Top;
            progress.fillClockwise = true;
            progress.fillAmount = 0f;
            StrategicMapScreen.Stretch(progress.rectTransform);

            Image content = StrategicMapScreen.CreateImage(rect, "Content", Color.white);
            content.rectTransform.anchorMin = content.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            content.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            content.rectTransform.anchoredPosition = Vector2.zero;
            content.rectTransform.sizeDelta = new Vector2(26f, 26f);
            content.preserveAspect = true;

            Text tag = StrategicMapScreen.CreateText(rect, "Tag", "", StrategicMapTheme.CaptionSize - 1,
                StrategicMapTheme.PanelText, TextAnchor.MiddleCenter);
            tag.rectTransform.anchorMin = tag.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            tag.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tag.rectTransform.anchoredPosition = Vector2.zero;
            tag.rectTransform.sizeDelta = new Vector2(MarkSize, 20f);

            go.SetActive(false);
            return new SlotWidget
            {
                Root = go, Rect = rect, Group = group, Backing = backing,
                Ring = ring, Content = content, Progress = progress, Tag = tag
            };
        }

        /// <summary>Renders one planet's slots. An unscanned planet has none, and that is the correct, gated result.</summary>
        public void Render(StrategicPlanetView planet)
        {
            int used = 0;
            float radius = viewRect.rect.width * RingRadiusFraction;

            for (int i = 0; i < planet.Slots.Count && used < pool.Count; i++)
            {
                StrategicSlotView slot = planet.Slots[i];
                SlotWidget widget = pool[used++];
                widget.Root.SetActive(true);

                // The slot's real local surface normal projected onto the card plane, so
                // the marks sit where the structures genuinely are on the planet. A normal
                // pointing straight at or away from the camera would otherwise pile several
                // marks in the middle, so the projected direction is renormalised and the
                // depth pushed into how far out the mark sits.
                Vector3 n = slot.LocalNormal.sqrMagnitude > 0.0001f ? slot.LocalNormal.normalized : Vector3.up;
                var flat = new Vector2(n.x, n.y);
                Vector2 direction = flat.sqrMagnitude > 0.0001f
                    ? flat.normalized
                    : new Vector2(Mathf.Cos(i * 1.0472f), Mathf.Sin(i * 1.0472f));
                float outward = Mathf.Lerp(0.62f, 1f, Mathf.Clamp01(flat.magnitude));
                widget.Rect.anchoredPosition = direction * (radius * outward);

                ApplyState(widget, slot);
            }

            for (int i = used; i < pool.Count; i++)
                if (pool[i].Root.activeSelf) pool[i].Root.SetActive(false);

            // A new planet restarts the arrival; the same one being re-rendered does not.
            if (used != shownCount) { shownCount = used; shownSince = Time.unscaledTime; }
        }

        /// <summary>
        /// Carries the motion: the staggered arrival when a card opens, then each state's own
        /// idle — a buildable slot breathing, a damaged one pulsing, a build sweeping round.
        /// Driven from the elapsed time every frame rather than accumulated, so nothing can
        /// end up stranded mid-animation.
        /// </summary>
        private void Update()
        {
            float now = Time.unscaledTime;
            float elapsed = now - shownSince;

            for (int i = 0; i < pool.Count; i++)
            {
                SlotWidget widget = pool[i];
                if (!widget.Root.activeSelf) continue;

                // Arrival: each mark a beat after the one before it, scaling up as it fades in.
                float start = i * 0.035f;
                float t = StrategicMapTheme.ReducedMotion
                    ? 1f
                    : Mathf.Clamp01((elapsed - start) / StrategicMapTheme.FastTransitionSeconds);
                float eased = StrategicMapTheme.EaseArrive(t);
                widget.Group.alpha = StrategicMapTheme.EaseOut(t);

                float idle = 1f;
                switch (widget.State)
                {
                    case StrategicSlotState.Buildable:
                        // An open slot breathes: plainly an invitation rather than a fault.
                        idle = Mathf.Lerp(0.92f, 1.08f, StrategicMapTheme.Breathe(now, 0.42f));
                        widget.Ring.color = StrategicMapTheme.WithAlpha(StrategicMapTheme.Cyan,
                            Mathf.Lerp(0.24f, 0.62f, StrategicMapTheme.Breathe(now, 0.42f)));
                        break;

                    case StrategicSlotState.Damaged:
                    case StrategicSlotState.Disabled:
                        // Faster and sharper than a breath: this one wants attention.
                        idle = 1f + StrategicMapTheme.Pulse(now, 0.07f);
                        break;

                    case StrategicSlotState.Constructing:
                    case StrategicSlotState.Upgrading:
                        // The arc itself is the animation; the mark only glows a little under it.
                        idle = 1f + StrategicMapTheme.Pulse(now, 0.03f);
                        break;
                }

                widget.Rect.localScale = Vector3.one * (eased * idle);
            }
        }

        private static void ApplyState(SlotWidget widget, StrategicSlotView slot)
        {
            widget.State = slot.State;
            widget.Progress.color = Color.clear;
            widget.Progress.fillAmount = 0f;
            widget.Tag.text = "";
            widget.Content.enabled = false;
            widget.Backing.color = new Color(0.02f, 0.03f, 0.06f, 0.62f);

            switch (slot.State)
            {
                case StrategicSlotState.Active:
                    widget.Ring.color = StrategicMapTheme.WithAlpha(StrategicMapTheme.Green, 0.72f);
                    ShowStructure(widget, slot);
                    if (slot.Tier >= 2) widget.Tag.text = "II";
                    break;

                case StrategicSlotState.Constructing:
                    widget.Ring.color = StrategicMapTheme.WithAlpha(StrategicMapTheme.Cyan, 0.34f);
                    widget.Progress.color = StrategicMapTheme.Cyan;
                    widget.Progress.fillAmount = Mathf.Clamp01(slot.Progress01);
                    widget.Tag.text = "İNŞA";
                    break;

                case StrategicSlotState.Upgrading:
                    widget.Ring.color = StrategicMapTheme.WithAlpha(StrategicMapTheme.Violet, 0.34f);
                    widget.Progress.color = StrategicMapTheme.Violet;
                    widget.Progress.fillAmount = Mathf.Clamp01(slot.Progress01);
                    ShowStructure(widget, slot);
                    widget.Tag.text = "↑";
                    break;

                case StrategicSlotState.Damaged:
                    widget.Ring.color = StrategicMapTheme.WithAlpha(StrategicMapTheme.Gold, 0.78f);
                    widget.Backing.color = new Color(0.10f, 0.06f, 0.02f, 0.66f);
                    ShowStructure(widget, slot);
                    widget.Tag.text = "!";
                    break;

                case StrategicSlotState.Disabled:
                    widget.Ring.color = StrategicMapTheme.WithAlpha(StrategicMapTheme.Red, 0.78f);
                    widget.Backing.color = new Color(0.12f, 0.03f, 0.06f, 0.66f);
                    ShowStructure(widget, slot);
                    widget.Tag.text = "×";
                    break;

                case StrategicSlotState.Buildable:
                    // Distinctly "open and available", never confusable with an occupied slot.
                    widget.Ring.color = StrategicMapTheme.WithAlpha(StrategicMapTheme.Cyan, 0.4f);
                    widget.Backing.color = new Color(0.02f, 0.03f, 0.06f, 0.34f);
                    widget.Tag.text = "+";
                    break;

                default: // Locked
                    widget.Ring.color = new Color(1f, 1f, 1f, 0.10f);
                    widget.Backing.color = new Color(0.02f, 0.03f, 0.06f, 0.28f);
                    widget.Tag.text = "";
                    break;
            }
        }

        private static void ShowStructure(SlotWidget widget, StrategicSlotView slot)
        {
            Sprite preview = StrategicMapVisualResolver.StructureThumbnail(slot.VisualContentId);
            widget.Content.sprite = preview;
            widget.Content.enabled = preview != null; // controlled fallback: ring + tag still convey the state
        }
    }
}
