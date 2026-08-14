using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nebula.UI.Design
{
    /// <summary>
    /// Type-safe lookup for every generated UI sprite, resolved once and cached.
    /// <para>
    /// The old HUDs called <c>Resources.GetBuiltinResource</c> and built their
    /// graphics inline, twelve times over. This is the one place a UI sprite is
    /// ever loaded, so a missing asset fails loudly in a test instead of quietly
    /// drawing a white box in front of the player, and a per-frame refresh can
    /// ask for a sprite without touching the disk.
    /// </para>
    /// <para>
    /// The catalog owns <b>generated chrome only</b>. The game's existing icons
    /// (<c>VisualIcons/*</c>, <c>VisualVfx/*</c>) keep their own resolvers --
    /// <see cref="Nebula.Visuals.NebulaIconResolver"/> and
    /// <see cref="Nebula.Visuals.NebulaProducedVisualResolver"/> -- and are never
    /// copied, re-baked or re-registered here.
    /// </para>
    /// </summary>
    public static class NebulaUiAssetCatalog
    {
        /// <summary>Root under <c>Resources/</c>. Matches the generator's output.</summary>
        public const string Root = "UI";

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(128);

        /// <summary>Sprites asked for that did not exist. Test-facing; empty is the only passing state.</summary>
        private static readonly List<string> Missing = new List<string>();

        /// <inheritdoc cref="Missing"/>
        public static IReadOnlyList<string> MissingAssets => Missing;

        // ---- folders ---------------------------------------------------------
        private const string Frames = "Frames";
        private const string Buttons = "Buttons";
        private const string Bars = "Bars";
        private const string CardsFolder = "Cards";
        private const string Status = "Status";
        private const string Hud = "HUD";
        private const string Nav = "Nav";
        private const string Menus = "Menus";

        // ---- frames ----------------------------------------------------------
        public static Sprite PanelPrimary => Get(Frames, "frame.panel.primary");
        public static Sprite PanelSecondary => Get(Frames, "frame.panel.secondary");
        public static Sprite PanelElevated => Get(Frames, "frame.panel.elevated");
        public static Sprite Modal => Get(Frames, "frame.modal");
        public static Sprite Tooltip => Get(Frames, "frame.tooltip");
        public static Sprite Toast => Get(Frames, "frame.toast");
        public static Sprite FocusRing => Get(Frames, "frame.focus");
        public static Sprite SelectionRing => Get(Frames, "frame.selection");

        // ---- buttons ---------------------------------------------------------

        /// <summary>Visual kind of a control -- how loud it is, not what it does.</summary>
        public enum ButtonKind { Primary, Secondary, Tertiary, Destructive, Icon }

        /// <summary>Interaction state. <see cref="Locked"/> reuses Disabled art plus a dashed edge.</summary>
        public enum ControlState { Default, Hover, Pressed, Disabled, Focused }

        public static Sprite Button(ButtonKind kind, ControlState state) =>
            Get(Buttons, $"button.{Lower(kind)}.{Lower(state)}");

        // ---- bars ------------------------------------------------------------
        public static Sprite BarTrack => Get(Bars, "bar.track");
        public static Sprite BarFill => Get(Bars, "bar.fill");
        public static Sprite BarFillStriped => Get(Bars, "bar.fill.striped");
        public static Sprite BarTicks => Get(Bars, "bar.ticks");
        public static Sprite RingScan => Get(Bars, "bar.ring.scan");
        public static Sprite RingLock => Get(Bars, "bar.ring.lock");

        // ---- rows, slots, cards ---------------------------------------------

        /// <summary>Row background state. Semantic edge colour is baked per state.</summary>
        public enum RowState { Default, Selected, Warning, Critical, Disabled, Empty }

        /// <summary>A structure slot on a planet's canonical 12.</summary>
        public enum SlotState { Occupied, Building, Damaged, Empty, Locked }

        /// <summary>Card background state.</summary>
        public enum CardState { Default, Selected, Warning, Critical, Locked }

        public static Sprite Row(RowState state) => Get(CardsFolder, $"row.{Lower(state)}");
        public static Sprite Slot(SlotState state) => Get(CardsFolder, $"slot.{Lower(state)}");
        public static Sprite Card(CardState state) => Get(CardsFolder, $"card.{Lower(state)}");

        /// <summary>
        /// The interaction states for a clickable row.
        /// <para>
        /// Every list row in the interface -- the fleet's vehicles, its mission
        /// targets, the module tree -- was built with
        /// <c>Selectable.Transition.None</c>, so hovering one, pressing one or
        /// tabbing onto one produced no change of any kind. The rows were the only
        /// way to select anything on those pages.
        /// </para>
        /// <para>
        /// Sprite swap rather than a colour tint, for the reason
        /// <c>NebulaUiFactory.ButtonControl</c> gives: a tint is a brightness ramp
        /// over one image and loses the authored edge. It also has to be a swap
        /// here for a second reason -- the page paints the row's <i>data</i> state
        /// into <c>Image.sprite</c> on every refresh, and Unity's swap writes
        /// <c>Image.overrideSprite</c> instead, so the two never fight and the data
        /// sprite comes straight back when the pointer leaves.
        /// </para>
        /// <para>
        /// Keyboard focus deliberately gets <see cref="FocusRing"/> -- an edge with
        /// no fill -- rather than another wash, because a focused row and a
        /// selected row are different facts and a player driving the interface from
        /// the keyboard has to be able to tell them apart.
        /// </para>
        /// </summary>
        public static SpriteState RowInteraction() => new SpriteState
        {
            highlightedSprite = Row(RowState.Selected),
            pressedSprite = SelectionRing,
            selectedSprite = FocusRing,
            disabledSprite = Row(RowState.Disabled),
        };

        // ---- status ----------------------------------------------------------
        public static Sprite BadgeSquare => Get(Status, "badge.square.default");
        public static Sprite BadgePill => Get(Status, "badge.pill.default");
        public static Sprite RarityHexFrame => Get(Status, "rarity.hex.frame");

        /// <summary>
        /// The shape channel that keeps colour from being the only carrier.
        /// Shield reads as a hexagon, Armor as a plate, Hull as a block, whether
        /// or not the player can tell the three colours apart.
        /// </summary>
        public enum ShapeGlyph { Shield, Armor, Hull, Energy, Heat }

        public static Sprite Glyph(ShapeGlyph glyph) => Get(Status, $"glyph.{Lower(glyph)}");

        // ---- HUD -------------------------------------------------------------

        /// <summary>Reticle appearance. Honours the existing reticle accessibility settings.</summary>
        public enum ReticleState { Idle, Lead, Locked }

        public static Sprite Reticle(ReticleState state) => Get(Hud, $"hud.reticle.{Lower(state)}");
        public static Sprite BracketCorner => Get(Hud, "hud.bracket.corner");
        public static Sprite EdgeMarker => Get(Hud, "hud.marker.edge");
        public static Sprite RouteArrow => Get(Hud, "hud.arrow.route");

        // ---- navigation ------------------------------------------------------

        /// <summary>The ten parent-menu pages. Order is the rail's order.</summary>
        public enum Page
        {
            Overview, Map, Progression, Resources, Fleet,
            Planets, Combat, Discovery, Settings, Session
        }

        public static Sprite PageGlyph(Page page) => Get(Nav, $"nav.page.{Lower(page)}");
        public static Sprite RailIndicator => Get(Nav, "nav.rail.indicator");

        // ---- menus -----------------------------------------------------------

        /// <summary>Main-menu / rail item background.</summary>
        public enum MenuItemState { Default, Hover, Selected, Disabled }

        public static Sprite MenuItem(MenuItemState state) => Get(Menus, $"menu.item.{Lower(state)}");
        public static Sprite MenuDivider => Get(Menus, "menu.divider");

        // ---- machinery -------------------------------------------------------

        private static string Lower<T>(T value) => value.ToString().ToLowerInvariant();

        /// <summary>
        /// Resolves once and caches. A miss is recorded rather than thrown so a
        /// single absent sprite can never take the whole interface down mid-session;
        /// the asset test is what makes a miss fail the build.
        /// </summary>
        private static Sprite Get(string folder, string id)
        {
            string path = $"{Root}/{folder}/{id}";
            if (Cache.TryGetValue(path, out Sprite cached)) return cached;

            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null && !Missing.Contains(path)) Missing.Add(path);
            Cache[path] = sprite;
            return sprite;
        }

        /// <summary>
        /// Drops every cached reference. Called on session teardown so a restart
        /// cannot hold sprites from the previous session alive, and so the asset
        /// test can measure a cold load.
        /// </summary>
        public static void ResetForSession()
        {
            Cache.Clear();
            Missing.Clear();
        }

        /// <summary>
        /// Every id the catalog can hand out. The asset test walks this to prove
        /// the generator and the catalog have not drifted apart -- a sprite that
        /// exists on disk but is unreachable here is as much a defect as a missing one.
        /// </summary>
        public static IEnumerable<string> AllPaths()
        {
            yield return $"{Root}/{Frames}/frame.panel.primary";
            yield return $"{Root}/{Frames}/frame.panel.secondary";
            yield return $"{Root}/{Frames}/frame.panel.elevated";
            yield return $"{Root}/{Frames}/frame.modal";
            yield return $"{Root}/{Frames}/frame.tooltip";
            yield return $"{Root}/{Frames}/frame.toast";
            yield return $"{Root}/{Frames}/frame.focus";
            yield return $"{Root}/{Frames}/frame.selection";

            foreach (ButtonKind k in System.Enum.GetValues(typeof(ButtonKind)))
                foreach (ControlState s in System.Enum.GetValues(typeof(ControlState)))
                    yield return $"{Root}/{Buttons}/button.{Lower(k)}.{Lower(s)}";

            yield return $"{Root}/{Bars}/bar.track";
            yield return $"{Root}/{Bars}/bar.fill";
            yield return $"{Root}/{Bars}/bar.fill.striped";
            yield return $"{Root}/{Bars}/bar.ticks";
            yield return $"{Root}/{Bars}/bar.ring.scan";
            yield return $"{Root}/{Bars}/bar.ring.lock";

            foreach (RowState s in System.Enum.GetValues(typeof(RowState)))
                yield return $"{Root}/{CardsFolder}/row.{Lower(s)}";
            foreach (SlotState s in System.Enum.GetValues(typeof(SlotState)))
                yield return $"{Root}/{CardsFolder}/slot.{Lower(s)}";
            foreach (CardState s in System.Enum.GetValues(typeof(CardState)))
                yield return $"{Root}/{CardsFolder}/card.{Lower(s)}";

            yield return $"{Root}/{Status}/badge.square.default";
            yield return $"{Root}/{Status}/badge.pill.default";
            yield return $"{Root}/{Status}/rarity.hex.frame";
            foreach (ShapeGlyph g in System.Enum.GetValues(typeof(ShapeGlyph)))
                yield return $"{Root}/{Status}/glyph.{Lower(g)}";

            foreach (ReticleState s in System.Enum.GetValues(typeof(ReticleState)))
                yield return $"{Root}/{Hud}/hud.reticle.{Lower(s)}";
            yield return $"{Root}/{Hud}/hud.bracket.corner";
            yield return $"{Root}/{Hud}/hud.marker.edge";
            yield return $"{Root}/{Hud}/hud.arrow.route";

            foreach (Page p in System.Enum.GetValues(typeof(Page)))
                yield return $"{Root}/{Nav}/nav.page.{Lower(p)}";
            yield return $"{Root}/{Nav}/nav.rail.indicator";

            foreach (MenuItemState s in System.Enum.GetValues(typeof(MenuItemState)))
                yield return $"{Root}/{Menus}/menu.item.{Lower(s)}";
            yield return $"{Root}/{Menus}/menu.divider";
        }
    }
}
