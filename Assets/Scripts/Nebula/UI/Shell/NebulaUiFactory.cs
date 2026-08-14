using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Nebula.UI.Design;

namespace Nebula.UI.Shell
{
    /// <summary>
    /// Builds the interface's primitives from the authored prefabs, the token set
    /// and the sprite catalog.
    /// <para>
    /// Every panel, label, button and bar in the new UI comes from here. The old
    /// HUDs each carried their own <c>CreatePanel</c>/<c>CreateText</c>/<c>MakeButton</c>
    /// — twelve near-identical copies, which is exactly why twelve slightly
    /// different greys ended up on screen.
    /// </para>
    /// <para>
    /// <b>Where the widget comes from:</b> the shell is instantiated from an
    /// authored prefab in <see cref="NebulaUiPrefabCatalog"/> and then configured,
    /// rather than assembled component by component. The split is deliberate:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Structure</b> — which objects exist, which components they carry,
    /// how they nest — belongs to the prefab alone. That is the part that used to
    /// be unreachable in code and is now editable without a compile.</item>
    /// <item><b>Contract</b> — the handful of properties this class's own
    /// documentation promises (a panel is nine-sliced, a label ellipsises rather
    /// than spills, chrome never eats a raycast) is authored into the prefab
    /// <i>and</i> re-asserted here. In a healthy project the re-assert changes
    /// nothing; the prefab coverage test proves the two agree. It exists so a
    /// half-imported asset cannot silently change how the interface behaves.</item>
    /// <item><b>Parameters</b> — everything the caller passed in — is applied
    /// here, as it always was.</item>
    /// </list>
    /// <para>
    /// A missing prefab is survivable: the widget is built from code exactly as it
    /// was before, and the fact is recorded in
    /// <see cref="NebulaUiPrefabCatalog.FallbacksUsed"/>. That path is a safety
    /// net for a broken import, never the normal one — a shipped boot resolves
    /// every prefab and uses it zero times.
    /// </para>
    /// <para>
    /// <b>Type pipeline:</b> every text object in the interface is created by
    /// <see cref="Label"/> alone, which is what made the move to TextMeshPro a
    /// change to one method rather than a sweep across every screen.
    /// </para>
    /// </summary>
    public static class NebulaUiFactory
    {
        private static TMP_FontAsset sharedFontAsset;

        /// <summary>
        /// The SDF face every label uses. One place, which is what made L-18 an
        /// edit here rather than a change in twenty-five files.
        /// </summary>
        public static TMP_FontAsset SharedFontAsset =>
            sharedFontAsset != null ? sharedFontAsset
                : sharedFontAsset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        /// <summary>uGUI's four styles map onto TMP's flags one for one.</summary>
        private static FontStyles TmpStyle(FontStyle style) => style switch
        {
            FontStyle.Bold => FontStyles.Bold,
            FontStyle.Italic => FontStyles.Italic,
            FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
            _ => FontStyles.Normal,
        };

        /// <summary>
        /// TextAnchor to TMP's alignment. Kept as a translation rather than
        /// changing every call site, so the migration is one file.
        /// </summary>
        private static TextAlignmentOptions TmpAlignment(TextAnchor anchor) => anchor switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            _ => TextAlignmentOptions.BottomRight,
        };

        /// <summary>
        /// The default anchor, pivot and size every label starts from.
        /// <para>
        /// Top-left rather than Unity's centred 100x100. A caller that forgets
        /// <see cref="Place"/> then lands in the panel's corner instead of floating
        /// in its middle — which is exactly how a "GAZ" label ended up rendering
        /// outside its own card, and the action rail's key prompts ended up centred
        /// despite being right-aligned. Shared with the prefab builder so the
        /// authored asset and this fallback cannot drift.
        /// </para>
        /// </summary>
        public static readonly Vector2 LabelAnchor = new Vector2(0f, 1f);

        /// <inheritdoc cref="LabelAnchor"/>
        public static readonly Vector2 LabelSize = new Vector2(200f, 18f);

        /// <summary>
        /// A label plus the type intent uGUI cannot yet express.
        /// <para>
        /// A class, not a struct, and deliberately so. As a readonly struct this
        /// was returned by value from properties and indexers, so
        /// <c>handle.Value = "x"</c> mutated a copy and compiled without a word of
        /// complaint — silently doing nothing. That cost three separate bugs
        /// before the pattern was obvious. One small allocation per label at build
        /// time is a fair price for a whole class of invisible failure.
        /// </para>
        /// </summary>
        public sealed class LabelHandle
        {
            public readonly TextMeshProUGUI Text;

            /// <summary>
            /// Letterspacing, in em. It is what makes the aerospace voice work and
            /// uGUI could not do it at all, so every caller has been passing a value
            /// that went nowhere. TMP applies it.
            /// </summary>
            public readonly float Tracking;

            public LabelHandle(TextMeshProUGUI text, float tracking)
            {
                Text = text;
                Tracking = tracking;
            }

            public string Value
            {
                get => Text.text;
                set { if (!string.Equals(Text.text, value, System.StringComparison.Ordinal)) Text.text = value; }
            }
        }

        // ---- prefab plumbing -------------------------------------------------

        /// <summary>
        /// Instantiates one authored primitive, or an empty rect when the prefab
        /// is absent. <paramref name="authored"/> says which happened, so the
        /// caller knows whether the components it needs should already be there.
        /// </summary>
        private static RectTransform Instance(NebulaUiPrefab id, RectTransform parent,
            string name, out bool authored)
        {
            RectTransform rect = NebulaUiPrefabCatalog.InstantiateRect(id, parent, name);
            authored = rect != null;
            return authored ? rect : BuildGroup(parent, name);
        }

        /// <summary>The pre-prefab construction path. Reached only on a missing asset.</summary>
        private static RectTransform BuildGroup(RectTransform parent, string name)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>
        /// The component the authored prefab was supposed to carry, added if it
        /// did not. An authored prefab that arrives incomplete counts as a
        /// fallback: the widget still works, and the gap stays visible.
        /// </summary>
        private static T Require<T>(RectTransform rect, NebulaUiPrefab id, bool authored,
            out bool repaired) where T : Component
        {
            var component = rect.GetComponent<T>();
            repaired = component == null;
            if (!repaired) return component;

            if (authored) NebulaUiPrefabCatalog.NoteFallback(id);
            return rect.gameObject.AddComponent<T>();
        }

        // ---- structure -------------------------------------------------------

        /// <summary>An empty, non-drawing rect. The building block for every group.</summary>
        public static RectTransform Group(RectTransform parent, string name) =>
            Instance(NebulaUiPrefab.PrimitiveGroup, parent, name, out _);

        /// <summary>Anchors a rect to a corner or edge with an explicit offset and size.</summary>
        public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>Stretches a rect to fill its parent with uniform padding.</summary>
        public static RectTransform Fill(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            return rect;
        }

        /// <summary>
        /// One of the authored composites — a header bar, a modal, a save slot.
        /// <para>
        /// The primitives above are what the screens have always called; this is
        /// the door to the assembled groups, so a screen can adopt an authored
        /// layout in one line instead of re-deriving it. Named children are
        /// reached through <see cref="NebulaUiPrefabCatalog.Slot"/>.
        /// </para>
        /// </summary>
        public static RectTransform Composite(NebulaUiPrefab id, RectTransform parent, string name)
        {
            RectTransform rect = NebulaUiPrefabCatalog.InstantiateRect(id, parent, name);
            if (rect == null) rect = BuildGroup(parent, name);
            return rect;
        }

        // ---- surfaces --------------------------------------------------------

        /// <summary>
        /// A 9-sliced panel from the catalog. Never a flat coloured rectangle.
        /// <para>
        /// The sprite decides which authored surface backs it: a row sprite comes
        /// out of the row prefab, a card sprite out of the card prefab, a slot
        /// sprite out of the slot prefab. That is what makes every dynamic row and
        /// card in the game an instance of one authored asset without a single
        /// page changing a line.
        /// </para>
        /// </summary>
        public static Image Panel(RectTransform parent, string name, Sprite sprite, Color? tint = null)
        {
            NebulaUiPrefab id = NebulaUiPrefabCatalog.SurfaceFor(sprite);
            RectTransform rect = Instance(id, parent, name, out bool authored);

            Image image = Require<Image>(rect, id, authored, out _);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;

            image.sprite = sprite;
            image.color = tint ?? Color.white;
            return image;
        }

        /// <summary>A plain tinted rect. For dividers and scrims only, never for a panel.</summary>
        public static Image Rect(RectTransform parent, string name, Color color)
        {
            RectTransform rect = Instance(NebulaUiPrefab.PrimitiveRect, parent, name, out bool authored);

            Image image = Require<Image>(rect, NebulaUiPrefab.PrimitiveRect, authored, out _);
            image.raycastTarget = false;
            image.color = color;
            return image;
        }

        /// <summary>An icon. Existing game icons are passed straight in; nothing is re-tinted by default.</summary>
        public static Image Icon(RectTransform parent, string name, Sprite sprite, float size)
        {
            RectTransform rect = Instance(NebulaUiPrefab.PrimitiveIcon, parent, name, out bool authored);
            rect.sizeDelta = new Vector2(size, size);

            Image image = Require<Image>(rect, NebulaUiPrefab.PrimitiveIcon, authored, out _);
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.sprite = sprite;
            return image;
        }

        // ---- text ------------------------------------------------------------

        /// <summary>
        /// The single place a text object is created. Everything else in the
        /// interface routes through here, which is what makes the eventual TMP
        /// migration a one-method change.
        /// </summary>
        public static LabelHandle Label(RectTransform parent, string name, string content,
            int size, Color color, TextAnchor alignment = TextAnchor.UpperLeft,
            FontStyle style = FontStyle.Normal, float tracking = 0f, bool wrap = false)
        {
            RectTransform rect = Instance(NebulaUiPrefab.PrimitiveLabel, parent, name, out bool authored);
            TextMeshProUGUI text = Require<TextMeshProUGUI>(
                rect, NebulaUiPrefab.PrimitiveLabel, authored, out bool repaired);

            // A repaired label has never been through the authored layout, so the
            // rect it landed on is Unity's centred default rather than the top-left
            // start every caller writes their Place() offsets against.
            if (repaired) ApplyLabelRect(rect);

            ConfigureLabel(text, content, size, color, alignment, style, tracking, wrap);
            return new LabelHandle(text, tracking);
        }

        /// <inheritdoc cref="LabelAnchor"/>
        private static void ApplyLabelRect(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = LabelAnchor;
            rect.sizeDelta = LabelSize;
        }

        /// <summary>
        /// Everything a label carries, in one place, so the authored prefab and the
        /// code fallback cannot say different things.
        /// </summary>
        private static void ConfigureLabel(TextMeshProUGUI text, string content, int size,
            Color color, TextAnchor alignment, FontStyle style, float tracking, bool wrap)
        {
            // Reference compare, so a prefab that already carries the shared face
            // is left alone -- reassigning a TMP font rebuilds its material.
            if (text.font != SharedFontAsset) text.font = SharedFontAsset;

            text.fontSize = size;
            text.fontStyle = TmpStyle(style);
            text.color = color;
            text.text = content;
            text.alignment = TmpAlignment(alignment);
            text.enableWordWrapping = wrap;
            // Ellipsis, not Overflow. uGUI let a too-narrow label spill silently
            // outside its own rect, and tracked text is wider than untracked, so the
            // migration turned that into visible bleed. A label that does not fit
            // now says so, which is findable; spilling is not.
            text.overflowMode = TextOverflowModes.Ellipsis;
            // Tracking finally goes somewhere: uGUI had no letterspacing, so every
            // caller's value was being discarded.
            text.characterSpacing = tracking * 100f;
            text.raycastTarget = false;
        }

        /// <summary>A section heading, in the one style section headings use.</summary>
        public static LabelHandle SectionTitle(RectTransform parent, string name, string content) =>
            Label(parent, name, content, NebulaUiDesignTokens.SizeSectionTitle,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleLeft, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingSectionTitle);

        /// <summary>A numeric readout. Right-aligned so digits line up as they change.</summary>
        public static LabelHandle Value(RectTransform parent, string name, string content, int size) =>
            Label(parent, name, content, size, NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleRight);

        // ---- controls --------------------------------------------------------

        /// <summary>
        /// A button wired to the catalog's state art. The reason string is what a
        /// disabled button shows instead of sitting there dead.
        /// </summary>
        public sealed class ButtonHandle
        {
            public Button Button { get; }
            public Image Background { get; }
            public LabelHandle Label { get; }
            public NebulaUiAssetCatalog.ButtonKind Kind { get; }

            /// <summary>
            /// The focus stop that stands in for <see cref="Button"/> while it is
            /// refusing, or <c>null</c> on a control that has not asked for one. See
            /// <see cref="KeepRefusalReachableByFocus"/>.
            /// </summary>
            public Selectable ReasonFocus { get; private set; }

            /// <inheritdoc cref="ReasonFocus"/>
            public Image ReasonFocusRing { get; private set; }

            /// <summary>Why this control cannot be used, or <c>null</c> when it can.</summary>
            public string DisabledReason { get; private set; }

            internal ButtonHandle(Button button, Image background, LabelHandle label,
                NebulaUiAssetCatalog.ButtonKind kind)
            {
                Button = button;
                Background = background;
                Label = label;
                Kind = kind;
            }

            /// <summary>
            /// Keeps this control's refusal reachable from the keyboard and the pad,
            /// by giving it a focus stop that survives being disabled.
            /// <para>
            /// Unity's directional search skips a Selectable that is not interactable
            /// (<c>Selectable.FindSelectable</c>), so turning a button off also takes
            /// it off every input device that has no pointer — and with it the only
            /// surface carrying the reason. The stand-in occupies the same rect, so
            /// the focus order the player learned does not change; it carries no
            /// <c>onClick</c> and is a plain <see cref="Selectable"/> rather than a
            /// <see cref="Button"/>, so Submit lands on nothing. The button itself
            /// stays refused throughout.
            /// </para>
            /// <para>
            /// <b>Asked for, not automatic</b>, and the reason is measurable: a
            /// stand-in is one more interactable Selectable in its column, and a
            /// surface whose focus order is asserted against its <i>buttons</i>
            /// (the main menu is, in <c>NebulaMenuNavigationPlayModeTests</c>) counts
            /// it. Adopting this on such a surface is a change to that surface's
            /// traversal and belongs with the fixture that pins it, not smuggled in
            /// underneath one. Pages that wire an explicit navigation ring must also
            /// wire it <i>before</i> handing availability out — see
            /// <c>NebulaQuantityModal.Repaint</c>.
            /// </para>
            /// </summary>
            public ButtonHandle KeepRefusalReachableByFocus()
            {
                if (ReasonFocus != null) return this;

                ReasonFocus = ReasonFocusSlot((RectTransform)Button.transform, Kind,
                    out Image ring, out NebulaTooltipSource tooltip);
                ReasonFocusRing = ring;
                // The same reason, resolved the same way, on both doors into the one
                // tooltip. Two sources, never two tooltips.
                tooltip.Reason = () => DisabledReason;
                HandFocusTo(Button.interactable);
                return this;
            }

            /// <summary>
            /// Changes the caption. A method rather than a settable property
            /// because <see cref="LabelHandle"/> is a readonly struct returned by
            /// value — assigning through it would compile away silently.
            /// </summary>
            public void SetCaption(string caption) => Label.Value = caption;

            /// <summary>
            /// Enables or disables with a reason. A control may only be disabled
            /// <b>with</b> an explanation — that rule is enforced here rather than
            /// left to each caller to remember.
            /// </summary>
            public void SetAvailable(bool available, string reasonWhenBlocked = null)
            {
                if (!available && string.IsNullOrEmpty(reasonWhenBlocked))
                    reasonWhenBlocked = "ŞU AN KULLANILAMIYOR";

                DisabledReason = available ? null : reasonWhenBlocked;
                // Stored is not the same as reachable: until the tooltip existed
                // this string was written and never shown to anyone.
                Button.interactable = available;
                Background.sprite = NebulaUiAssetCatalog.Button(Kind, available
                    ? NebulaUiAssetCatalog.ControlState.Default
                    : NebulaUiAssetCatalog.ControlState.Disabled);
                Label.Text.color = available
                    ? NebulaUiDesignTokens.InkPrimary
                    : NebulaUiDesignTokens.DisabledInk;
                HandFocusTo(available);
            }

            /// <summary>
            /// Keeps exactly one focus stop alive for this control: the
            /// <see cref="Button"/> while it can be pressed, and
            /// <see cref="ReasonFocus"/> while it refuses. Does nothing at all on a
            /// control that never asked for a stand-in.
            /// </summary>
            private void HandFocusTo(bool available)
            {
                if (ReasonFocus == null) return;

                if (!available)
                {
                    // Mirrored rather than authored: a page that wires an explicit
                    // ring (the quantity box, the resources page) confines its focus
                    // on purpose, and a stand-in with Unity's automatic search would
                    // be the one door out of it.
                    ReasonFocus.navigation = Button.navigation;
                }

                if (ReasonFocus.gameObject.activeSelf == !available) return;

                // A control that becomes usable while its stand-in holds focus must
                // hand the focus back rather than drop it: navigation walks outward
                // from the current selection, and destroying that selection leaves
                // the pad with nothing to stand on.
                UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
                if (available && events != null
                    && events.currentSelectedGameObject == ReasonFocus.gameObject)
                    events.SetSelectedGameObject(Button.gameObject);

                ReasonFocus.gameObject.SetActive(!available);
            }
        }

        /// <summary>The one child a button prefab is required to carry.</summary>
        public const string ButtonLabelSlot = "Label";

        public static ButtonHandle ButtonControl(RectTransform parent, string name, string caption,
            NebulaUiAssetCatalog.ButtonKind kind, UnityEngine.Events.UnityAction onClick)
        {
            const NebulaUiPrefab id = NebulaUiPrefab.PrimitiveButton;
            RectTransform rect = Instance(id, parent, name, out bool authored);

            Image background = Require<Image>(rect, id, authored, out _);
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;
            background.sprite = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Default);

            Button button = Require<Button>(rect, id, authored, out _);
            button.targetGraphic = background;

            // Swap the catalog art per state rather than tinting one sprite, so
            // hover and pressed keep their authored edges instead of being a
            // brightness ramp over the same image.
            button.spriteState = new SpriteState
            {
                highlightedSprite = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Hover),
                pressedSprite = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Pressed),
                selectedSprite = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Focused),
                disabledSprite = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Disabled),
            };
            button.transition = Selectable.Transition.SpriteSwap;
            if (onClick != null) button.onClick.AddListener(onClick);

            // A disabled Button stops receiving pointer events, so the reason has
            // to be carried by something that still does -- hence the source on the
            // same object rather than on the Button itself.
            NebulaTooltipSource tooltip = Require<NebulaTooltipSource>(rect, id, authored, out _);

            LabelHandle label = LabelSlot(rect, id, authored, caption);

            var handle = new ButtonHandle(button, background, label, kind);
            tooltip.Reason = () => handle.DisabledReason;
            return handle;
        }

        /// <summary>The child that keeps a refusing control reachable. See <see cref="ButtonHandle.ReasonFocus"/>.</summary>
        public const string ButtonReasonFocusSlot = "Reason";

        /// <summary>
        /// Whether this is a stand-in rather than a control.
        /// <para>
        /// A stand-in is interactable on purpose — that is the only way Unity's
        /// directional search will visit it — but it does nothing when pressed, so
        /// anything choosing where to <i>put</i> focus first should choose a real
        /// control over one of these and fall back to it, not the other way round.
        /// </para>
        /// </summary>
        public static bool IsReasonFocus(Selectable candidate) =>
            candidate != null
            && candidate.gameObject.name == ButtonReasonFocusSlot
            && candidate.GetComponent<Button>() == null
            && candidate.transform.parent != null
            && candidate.transform.parent.GetComponent<Button>() != null;

        /// <summary>
        /// Builds the stand-in focus stop, inactive, in front of the button it
        /// belongs to.
        /// <para>
        /// Built here in code rather than authored into the button prefab on
        /// purpose: it must never be a second clickable thing. It takes no
        /// raycast at all — the button's own background is still the hit target,
        /// so the mouse path is exactly the one that already worked — and it draws
        /// only the focus art the catalog already publishes for this kind, through
        /// Unity's own <see cref="Selectable.Transition.ColorTint"/> with a zero
        /// fade. Zero rather than the default tenth of a second so there is no
        /// transition for Motion Off to remove and nothing that can read as a
        /// flash under Photosensitivity Safe Mode: the ring is either there or it
        /// is not.
        /// </para>
        /// </summary>
        private static Selectable ReasonFocusSlot(RectTransform buttonRect,
            NebulaUiAssetCatalog.ButtonKind kind, out Image ring, out NebulaTooltipSource tooltip)
        {
            RectTransform rect = BuildGroup(buttonRect, ButtonReasonFocusSlot);
            Fill(rect);

            ring = rect.gameObject.AddComponent<Image>();
            ring.sprite = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Focused);
            ring.type = Image.Type.Sliced;
            // White, and the tint alone carries the alpha. The vertex colour is the
            // graphic's colour times the renderer's, so a transparent Image would
            // stay transparent no matter what the selected state asked for.
            ring.color = Color.white;
            ring.raycastTarget = false;

            var focus = rect.gameObject.AddComponent<Selectable>();
            focus.transition = Selectable.Transition.ColorTint;
            focus.targetGraphic = ring;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = colors.highlightedColor = colors.disabledColor = Transparent;
            colors.selectedColor = colors.pressedColor = Color.white;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0f;
            focus.colors = colors;

            tooltip = rect.gameObject.AddComponent<NebulaTooltipSource>();

            // Off until something refuses. While the button can be pressed it is the
            // button that owns the focus order, and two stops on one control would
            // make the player press the stick twice to leave it.
            rect.gameObject.SetActive(false);
            return focus;
        }

        private static readonly Color Transparent = new Color(1f, 1f, 1f, 0f);

        /// <summary>
        /// The caption inside a button. Taken from the authored child when the
        /// prefab has one, built beside it when the prefab is missing entirely.
        /// </summary>
        private static LabelHandle LabelSlot(RectTransform rect, NebulaUiPrefab id,
            bool authored, string caption)
        {
            RectTransform slot = NebulaUiPrefabCatalog.Slot(rect, ButtonLabelSlot);
            var text = slot == null ? null : slot.GetComponent<TextMeshProUGUI>();

            if (text == null)
            {
                if (authored) NebulaUiPrefabCatalog.NoteFallback(id);
                LabelHandle built = Label(rect, ButtonLabelSlot, caption, NebulaUiDesignTokens.SizeButton,
                    NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleCenter, FontStyle.Bold,
                    NebulaUiDesignTokens.TrackingButton);
                Fill(built.Text.rectTransform);
                return built;
            }

            ConfigureLabel(text, caption, NebulaUiDesignTokens.SizeButton,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleCenter, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingButton, wrap: false);
            return new LabelHandle(text, NebulaUiDesignTokens.TrackingButton);
        }

        // ---- bars ------------------------------------------------------------

        /// <summary>A track plus a fill. The fill is tinted; the track never is.</summary>
        public sealed class BarHandle
        {
            public Image Track { get; }
            public Image Fill { get; }
            private readonly RectTransform fillRect;

            internal BarHandle(Image track, Image fill)
            {
                Track = track;
                Fill = fill;
                fillRect = fill.rectTransform;
            }

            /// <summary>Sets the filled fraction. Clamped; callers pass raw ratios freely.</summary>
            public void SetFraction(float fraction01)
            {
                float clamped = Mathf.Clamp01(fraction01);
                fillRect.anchorMax = new Vector2(clamped, 1f);
            }

            public void SetColor(Color color) => Fill.color = color;
        }

        /// <summary>The one child a bar prefab is required to carry.</summary>
        public const string BarFillSlot = "Fill";

        public static BarHandle Bar(RectTransform parent, string name, Color fillColor, bool striped = false)
        {
            const NebulaUiPrefab id = NebulaUiPrefab.PrimitiveBar;
            RectTransform trackRect = Instance(id, parent, name, out bool authored);

            Image track = Require<Image>(trackRect, id, authored, out _);
            track.sprite = NebulaUiAssetCatalog.BarTrack;
            track.type = Image.Type.Sliced;
            // Set explicitly rather than left at the prefab's white: an untinted
            // track composited brighter than its own panel, so an empty bar and a
            // full one were the same full-width band.
            track.color = NebulaUiDesignTokens.BarTrackTint;
            track.raycastTarget = false;

            RectTransform fillRect = NebulaUiPrefabCatalog.Slot(trackRect, BarFillSlot);
            if (fillRect == null)
            {
                if (authored) NebulaUiPrefabCatalog.NoteFallback(id);
                fillRect = BuildGroup(trackRect, BarFillSlot);
                ApplyBarFillRect(fillRect);
            }

            Image fill = fillRect.GetComponent<Image>();
            if (fill == null) fill = fillRect.gameObject.AddComponent<Image>();

            fill.sprite = striped ? NebulaUiAssetCatalog.BarFillStriped : NebulaUiAssetCatalog.BarFill;
            fill.type = Image.Type.Sliced;
            fill.color = fillColor;
            fill.raycastTarget = false;

            return new BarHandle(track, fill);
        }

        /// <summary>
        /// A fill anchored to the track's left edge and stretched vertically, so
        /// <see cref="BarHandle.SetFraction"/> only ever moves one anchor. Shared
        /// with the prefab builder.
        /// </summary>
        public static void ApplyBarFillRect(RectTransform fillRect)
        {
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
        }
    }
}
