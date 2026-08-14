using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using System;

namespace Nebula.UI.Hud
{
    /// <summary>
    /// One thing the player can do right now.
    /// <para>
    /// The cost is carried as an icon plus an amount, never as prose. That is the
    /// distinction that kept the old HUD honest-looking and unreadable at the same
    /// time: it printed the full sentence
    /// <c>"[F] DRILL T2 UPGRADE (30 Parts + 40 Metal Alloy) · 60s · PRODUCTION
    /// HALTED · LOCKED — NEEDS STATION CONSTRUCTION T2"</c>
    /// into a text wall, when what the player needs on the HUD is the verb, the
    /// key, and the price.
    /// </para>
    /// </summary>
    public readonly struct HudAction
    {
        /// <summary>The verb. Short, imperative, Turkish.</summary>
        public readonly string Caption;

        /// <summary>The key that performs it. Shown as a prompt, never spelled out in the caption.</summary>
        public readonly KeyCode Key;

        /// <summary>Compact price, e.g. "30 PARÇA + 40 ALAŞIM". Empty when free.</summary>
        public readonly string Cost;

        /// <summary>False when the action exists but cannot be taken right now.</summary>
        public readonly bool Available;

        /// <summary>
        /// Why it cannot be taken. Required whenever <see cref="Available"/> is
        /// false -- an action shown without a reason is the dead control this
        /// redesign exists to remove.
        /// </summary>
        public readonly string BlockedReason;
        public readonly string ActionId;

        public HudAction(string caption, KeyCode key, string cost = null,
            bool available = true, string blockedReason = null, string actionId = null)
        {
            Caption = caption;
            Key = key;
            Cost = cost;
            Available = available;
            BlockedReason = available ? null
                : (string.IsNullOrEmpty(blockedReason) ? "ŞU AN YAPILAMAZ" : blockedReason);
            ActionId = actionId;
        }

        public bool IsValid => !string.IsNullOrEmpty(Caption);
    }

    /// <summary>
    /// The bottom-centre action bar: one primary action, at most two secondary
    /// ones, and a single context line.
    /// <para>
    /// This is what replaces the extraction HUD's text wall. That panel stacked
    /// scan, dominance, node layout, drill lifecycle, cargo, package, evacuation
    /// and processing lines into one 940x148 <c>Text</c> with vertical overflow
    /// switched on -- it had already been pushed 220px up the screen to stop it
    /// running off the bottom, which is a symptom rather than a fix.
    /// </para>
    /// <para>
    /// The cap is enforced here rather than trusted to callers. A caller that
    /// offers five actions gets the three that matter and no layout break, because
    /// the failure mode of "just this once" is exactly how the wall grew.
    /// </para>
    /// </summary>
    public sealed class NebulaHudContextBar : MonoBehaviour
    {
        /// <summary>Hard cap. One primary plus two secondary; anything more is a page, not a HUD.</summary>
        public const int MaxSecondaryActions = 2;

        private RectTransform root;
        private NebulaUiFactory.LabelHandle contextLine;

        private ActionWidget primary;
        private readonly List<ActionWidget> secondaries = new List<ActionWidget>(MaxSecondaryActions);

        /// <summary>Test-facing: actions currently on screen, primary included.</summary>
        public int VisibleActionCount { get; private set; }

        /// <summary>Test-facing: the context line as shown.</summary>
        public string ContextText => contextLine.Text != null ? contextLine.Text.text : null;

        /// <summary>Test-facing: how many offered actions were dropped by the cap.</summary>
        public int DroppedActionCount { get; private set; }

        private sealed class ActionWidget
        {
            public RectTransform Root;
            public Image Background;

            /// <summary>
            /// Kept so a blocked action can swap to its own disabled art. Without
            /// it the widget held the Default sprite forever and only the ink
            /// changed -- which put <c>DisabledInk</c> on the full-strength
            /// primary fill at a measured 1.06:1, i.e. an invisible caption.
            /// </summary>
            public NebulaUiAssetCatalog.ButtonKind Kind;

            public NebulaUiFactory.LabelHandle Key;
            public NebulaUiFactory.LabelHandle Caption;
            public NebulaUiFactory.LabelHandle Cost;
            public Button Button;
            public string ActionId;
        }

        /// <summary>Raised only for an available widget carrying a semantic action id.</summary>
        public event Action<string> ActionRequested;

        public static NebulaHudContextBar Create(NebulaUiShell shell)
        {
            var go = new GameObject("HUD Context Bar");
            var bar = go.AddComponent<NebulaHudContextBar>();
            bar.Build(shell);
            return bar;
        }

        private void Build(NebulaUiShell shell)
        {
            root = NebulaUiFactory.Group(shell.Root(UiLayer.HudStatic), "Context Bar");
            NebulaUiFactory.Place(root, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(1100f, 92f));
            root.pivot = new Vector2(0.5f, 0f);
            // The controller lives on the layer root, never inside the visual it
            // toggles: a widget parented into its own panel is switched off
            // together with it, its Update stops, and it can never decide to
            // come back. Hiding the panel must not disable the thing that shows it.
            transform.SetParent(shell.Root(UiLayer.HudStatic), false);

            // The one descriptive line the HUD gets -- "VEGA-7 / II · METAL ·
            // RARE · SAKİN" is where the player reads what they are standing on,
            // so it is a value and not decoration. InkMuted measured 3.5:1 on the
            // void behind it; InkSecondary is 8.3:1 and looks the same.
            contextLine = NebulaUiFactory.Label(root, "Context", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperCenter, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(contextLine.Text.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0f, 0f), new Vector2(1100f, 16f));

            primary = BuildWidget("Primary", NebulaUiAssetCatalog.ButtonKind.Primary, 320f, 46f);
            // 200 was not enough for Turkish. "YÖRÜNGE SABİTLE" is fifteen
            // tracked characters at 13px bold and it rendered as "YÖRÜNGE SA…";
            // a key prompt the player cannot finish reading is worse than no
            // prompt. 264 leaves 204 for the caption, which clears the longest
            // secondary this bar is handed ("GEZEGENİ TAHLİYE ET"), and a full
            // row still comes to 872 of the 1100 the bar owns.
            // LongTurkishText_FitsEveryControlItIsPutIn measures this rather than
            // trusting the arithmetic.
            for (int i = 0; i < MaxSecondaryActions; i++)
                secondaries.Add(BuildWidget("Secondary " + i, NebulaUiAssetCatalog.ButtonKind.Secondary, 264f, 38f));

            Clear();
        }

        private ActionWidget BuildWidget(string name, NebulaUiAssetCatalog.ButtonKind kind,
            float width, float height)
        {
            var widget = new ActionWidget();
            widget.Kind = kind;
            widget.Root = NebulaUiFactory.Group(root, name);
            widget.Root.sizeDelta = new Vector2(width, height);

            widget.Background = NebulaUiFactory.Panel(widget.Root, "Background",
                NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Default));
            NebulaUiFactory.Fill(widget.Background.rectTransform);
            widget.Background.raycastTarget = true;
            widget.Button = widget.Root.gameObject.AddComponent<Button>();
            widget.Button.targetGraphic = widget.Background;
            widget.Button.transition = Selectable.Transition.SpriteSwap;
            widget.Button.spriteState = new SpriteState
            {
                highlightedSprite = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Hover),
                pressedSprite = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Pressed),
                selectedSprite = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Focused),
                disabledSprite = NebulaUiAssetCatalog.Button(kind, NebulaUiAssetCatalog.ControlState.Disabled),
            };
            widget.Button.onClick.AddListener(() =>
            {
                if (widget.Button.interactable && !string.IsNullOrEmpty(widget.ActionId))
                    ActionRequested?.Invoke(widget.ActionId);
            });

            widget.Key = NebulaUiFactory.Label(widget.Root, "Key", "", NebulaUiDesignTokens.SizeKeyPrompt,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleCenter, FontStyle.Bold);
            NebulaUiFactory.Place(widget.Key.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(28f, 22f));

            widget.Caption = NebulaUiFactory.Label(widget.Root, "Caption", "", NebulaUiDesignTokens.SizeButton,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingButton);
            NebulaUiFactory.Place(widget.Caption.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(48f, 6f), new Vector2(width - 60f, 18f));

            // InkSecondary, not InkMuted: a price is a value, and InkMuted
            // measured 3.5:1 here against the 4.5:1 an eleven-pixel line needs.
            widget.Cost = NebulaUiFactory.Label(widget.Root, "Cost", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(widget.Cost.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(48f, -10f), new Vector2(width - 60f, 14f));

            widget.Root.gameObject.SetActive(false);
            return widget;
        }

        /// <summary>Hides every action. The HUD shows nothing rather than something stale.</summary>
        public void Clear()
        {
            primary.Root.gameObject.SetActive(false);
            for (int i = 0; i < secondaries.Count; i++) secondaries[i].Root.gameObject.SetActive(false);
            contextLine.Value = string.Empty;
            VisibleActionCount = 0;
            DroppedActionCount = 0;
        }

        /// <summary>
        /// Sets what the player can do here. <paramref name="context"/> is the one
        /// descriptive line allowed -- everything else is an action or belongs in a
        /// tooltip or a parent page.
        /// </summary>
        /// <summary>
        /// Nothing applies right now, so the bar says nothing. Clearing through the
        /// same path Show uses keeps the counts honest rather than leaving the last
        /// frame's actions on screen with no context under them.
        /// </summary>
        public void Hide() => Show(null, default, null);

        public void Show(string context, HudAction primaryAction, IReadOnlyList<HudAction> secondaryActions = null)
        {
            contextLine.Value = context ?? string.Empty;

            VisibleActionCount = 0;
            DroppedActionCount = 0;

            bool hasPrimary = primaryAction.IsValid;
            Apply(primary, primaryAction, hasPrimary);
            if (hasPrimary) VisibleActionCount++;

            int shown = 0;
            if (secondaryActions != null)
            {
                for (int i = 0; i < secondaryActions.Count; i++)
                {
                    if (!secondaryActions[i].IsValid) continue;
                    if (shown >= MaxSecondaryActions) { DroppedActionCount++; continue; }
                    Apply(secondaries[shown], secondaryActions[i], true);
                    shown++;
                    VisibleActionCount++;
                }
            }

            for (int i = shown; i < secondaries.Count; i++)
                secondaries[i].Root.gameObject.SetActive(false);

            Layout(hasPrimary, shown);
        }

        private static void Apply(ActionWidget widget, HudAction action, bool visible)
        {
            if (widget.Root.gameObject.activeSelf != visible) widget.Root.gameObject.SetActive(visible);
            if (!visible) return;

            widget.Key.Value = KeyPrompt(action.Key);
            widget.Caption.Value = action.Caption;

            // A blocked action shows its reason where the price would be. The
            // player reads one line either way, and never a control with no answer.
            bool blocked = !action.Available;
            widget.ActionId = action.ActionId;
            widget.Button.interactable = action.Available && !string.IsNullOrEmpty(action.ActionId);
            widget.Cost.Value = blocked ? action.BlockedReason : (action.Cost ?? string.Empty);
            widget.Cost.Text.color = blocked
                ? NebulaUiDesignTokens.Status(Visuals.UiStatusLevel.Warning)
                : NebulaUiDesignTokens.InkSecondary;

            // The art has to change too, and it did not. Every button in the
            // interface swaps to its disabled sprite through ButtonHandle
            // .SetAvailable, but this bar builds raw panels, so a blocked primary
            // kept the full-strength primary fill and simply had its caption
            // dimmed on top of it -- measured 1.06:1, which is not "dim", it is
            // gone. Swapping the sprite is what makes the state visible at all.
            widget.Background.sprite = NebulaUiAssetCatalog.Button(widget.Kind, blocked
                ? NebulaUiAssetCatalog.ControlState.Disabled
                : NebulaUiAssetCatalog.ControlState.Default);

            // With the fill corrected the caption stays legible rather than
            // greyed to the floor: the player still has to read *what* is blocked
            // for the reason under it to mean anything. Unavailability is carried
            // by the disabled fill, the amber reason line and the words -- three
            // channels, none of them the caption's own colour.
            widget.Caption.Text.color = blocked
                ? NebulaUiDesignTokens.InkSecondary
                : NebulaUiDesignTokens.InkPrimary;

            // The key does not change when the action is blocked, so neither does
            // its prompt. It used to be dimmed to DisabledInk on the undimmed
            // primary fill, which is where the 1.05:1 came from.
            widget.Key.Text.color = NebulaUiDesignTokens.InkSecondary;
        }

        /// <summary>Centres the row: primary in the middle, secondaries flanking it.</summary>
        private void Layout(bool hasPrimary, int secondaryCount)
        {
            const float gap = 12f;
            float primaryWidth = hasPrimary ? primary.Root.sizeDelta.x : 0f;
            float secondaryWidth = secondaryCount > 0 ? secondaries[0].Root.sizeDelta.x : 0f;

            float total = primaryWidth
                          + secondaryCount * secondaryWidth
                          + gap * ((hasPrimary ? 1 : 0) + secondaryCount - 1);
            if (total < 0f) total = 0f;

            float x = -total * 0.5f;

            if (secondaryCount > 0)
            {
                Position(secondaries[0], x, secondaryWidth);
                x += secondaryWidth + gap;
            }

            if (hasPrimary)
            {
                Position(primary, x, primaryWidth);
                x += primaryWidth + gap;
            }

            if (secondaryCount > 1) Position(secondaries[1], x, secondaryWidth);
        }

        private static void Position(ActionWidget widget, float left, float width)
        {
            widget.Root.anchorMin = widget.Root.anchorMax = new Vector2(0.5f, 0f);
            widget.Root.pivot = new Vector2(0f, 0f);
            widget.Root.anchoredPosition = new Vector2(left, 8f);
            widget.Root.sizeDelta = new Vector2(width, widget.Root.sizeDelta.y);
        }

        /// <summary>
        /// A readable prompt for a key. Unity's enum names are not player-facing --
        /// "Alpha1" and "LeftBracket" would be shown verbatim otherwise.
        /// </summary>
        internal static string KeyPrompt(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.None: return string.Empty;
                case KeyCode.Escape: return "ESC";
                case KeyCode.Return:
                case KeyCode.KeypadEnter: return "ENTER";
                case KeyCode.Space: return "BOŞLUK";
                case KeyCode.LeftShift:
                case KeyCode.RightShift: return "SHIFT";
                case KeyCode.Tab: return "TAB";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                default:
                    string name = key.ToString();
                    if (name.StartsWith("Alpha") && name.Length == 6) return name.Substring(5);
                    // Deliberately ASCII: a KeyCode name is not a Turkish word, and
                    // Turkish casing would render "Insert" as "İNSERT".
                    return Design.NebulaText.UpperAscii(name);
            }
        }
    }
}
