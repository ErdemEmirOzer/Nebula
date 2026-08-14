using System.Collections.Generic;
using UnityEngine;
using Nebula.UI.Design;

namespace Nebula.UI.Shell
{
    /// <summary>
    /// Every authored interface prefab, addressed by name rather than by string.
    /// <para>
    /// The enum member <b>is</b> the file name and the folder is derived from the
    /// member, so a prefab cannot be renamed on disk without the compiler noticing
    /// at every call site. That is the whole point: the previous shape of this
    /// system was <c>new GameObject(...)</c> in eighty-three places, where the only
    /// way to change a panel's edge was to find all eighty-three.
    /// </para>
    /// </summary>
    public enum NebulaUiPrefab
    {
        // ---- primitives ------------------------------------------------------
        // What NebulaUiFactory instantiates on every call. These carry the shell
        // -- component set, slice mode, raycast policy, font -- and the factory
        // applies only what the caller actually passed in.

        PrimitiveGroup,
        PrimitivePanel,
        PrimitiveRect,
        PrimitiveIcon,
        PrimitiveLabel,
        PrimitiveButton,
        PrimitiveBar,

        /// <summary>Panel authored for a dynamic list row. Group 17/18/20/23's mount.</summary>
        PrimitiveRowSurface,

        /// <summary>Panel authored for a card. Group 19/21/22's mount.</summary>
        PrimitiveCardSurface,

        /// <summary>Panel authored for one of a planet's twelve structure slots.</summary>
        PrimitiveSlotSurface,

        // ---- the twenty-four required groups ---------------------------------

        /// <summary>1 - the single interface root every layer hangs from.</summary>
        UiRoot,

        /// <summary>2 - the inset a notch or a TV overscan leaves usable.</summary>
        SafeAreaShell,

        /// <summary>3 - the session entry screen.</summary>
        MainMenu,

        /// <summary>4 - the paused command menu that hosts the ten pages.</summary>
        ParentMenuShell,

        /// <summary>5 - title, breadcrumb and page navigation across the top.</summary>
        HeaderBar,

        /// <summary>6 - the resource and status readout strip.</summary>
        ResourceStatusStrip,

        /// <summary>7 - left rail: page list and selection context.</summary>
        LeftContextRail,

        /// <summary>8 - right rail: actions for whatever is selected.</summary>
        RightContextRail,

        /// <summary>9 - the panel every page section is made of.</summary>
        StandardPanel,

        /// <summary>10 - in-page tab navigation.</summary>
        TabBar,

        /// <summary>11 - the button, with all five catalog states authored.</summary>
        StandardButton,

        /// <summary>12 - scrim plus elevated surface.</summary>
        Modal,

        /// <summary>13 - the yes/no dialog built on the modal.</summary>
        ConfirmationDialog,

        /// <summary>14 - the pointer-following reason panel.</summary>
        Tooltip,

        /// <summary>15 - transient notification.</summary>
        Toast,

        /// <summary>16 - continuous progress bar.</summary>
        ProgressBar,

        /// <summary>16 - the segmented variant, for counts rather than fractions.</summary>
        SegmentedBar,

        /// <summary>17 - one line of the resource list.</summary>
        ResourceRow,

        /// <summary>18 - one vehicle in the fleet list.</summary>
        FleetRow,

        /// <summary>19 - a planet or system card.</summary>
        PlanetCard,

        /// <summary>20 - one module on the progression page.</summary>
        ModuleRow,

        /// <summary>21 - an attention or warning card.</summary>
        AttentionCard,

        /// <summary>22 - one save slot.</summary>
        SaveSlot,

        /// <summary>23 - a settings row carrying an on/off control.</summary>
        SettingsToggleRow,

        /// <summary>23 - a settings row carrying a slider.</summary>
        SettingsSliderRow,

        /// <summary>23 - a settings row carrying a key binding.</summary>
        SettingsBindingRow,

        /// <summary>24 - hull, armour, shield and energy.</summary>
        HudVitals,

        /// <summary>24 - the combat and target cluster.</summary>
        HudTargetCluster,

        // ---- shared state variants -------------------------------------------
        // A page with nothing in it must say which nothing it is. A blank box
        // reads as a screen that failed to load, which is the defect the
        // resources page actually shipped with.

        PanelLoadingState,
        PanelEmptyState,
        PanelErrorState,

        // ---- handoff ---------------------------------------------------------

        /// <summary>
        /// The mount the strategic map's own renderer attaches into. Deliberately
        /// empty: the map owns its interior and nothing here may author it.
        /// </summary>
        StrategicMapHandoffShell,
    }

    /// <summary>
    /// Resolves an authored interface prefab once and keeps it.
    /// <para>
    /// Fixed-size storage indexed by the enum, so the cache has exactly as many
    /// slots as there are prefabs and cannot grow no matter how many sessions
    /// open and close. There is no dictionary, no reflection, no hierarchy scan
    /// and no per-call <c>Resources.Load</c> past the first.
    /// </para>
    /// <para>
    /// A missing prefab is not fatal. <see cref="NebulaUiFactory"/> falls back to
    /// building that one widget from code, and the miss is recorded here, so a
    /// half-imported project still runs while <see cref="FallbacksUsed"/> keeps
    /// the fact visible instead of letting it pass as normal.
    /// </para>
    /// </summary>
    public static class NebulaUiPrefabCatalog
    {
        /// <summary>Root under <c>Resources/</c>. Matches the builder's output.</summary>
        public const string Root = "UI/Prefabs";

        private static readonly NebulaUiPrefab[] Ids =
            (NebulaUiPrefab[])System.Enum.GetValues(typeof(NebulaUiPrefab));

        private static readonly string[] Paths = BuildPaths();

        private static readonly GameObject[] Cache = new GameObject[Ids.Length];
        private static readonly bool[] Attempted = new bool[Ids.Length];

        private static readonly List<NebulaUiPrefab> Fallbacks = new List<NebulaUiPrefab>();

        /// <summary>
        /// How many times a widget had to be built from code because its prefab
        /// was not there. Zero is the only acceptable value on a shipped boot;
        /// anything else means the asset side of the build is incomplete.
        /// </summary>
        public static int FallbacksUsed { get; private set; }

        /// <summary>Which prefabs fell back, without duplicates. Test-facing.</summary>
        public static IReadOnlyList<NebulaUiPrefab> FallbackPrefabs => Fallbacks;

        /// <summary>Every prefab this catalog can hand out, in enum order.</summary>
        public static IReadOnlyList<NebulaUiPrefab> All => Ids;

        /// <summary>The <c>Resources</c> path for one entry, without the extension.</summary>
        public static string PathOf(NebulaUiPrefab id) => Paths[(int)id];

        // ---- resolution ------------------------------------------------------

        /// <summary>
        /// The prefab asset, loaded at most once. Returns <c>null</c> when the
        /// asset is absent; the caller decides what to do about it.
        /// <para>
        /// The cached reference is re-checked rather than trusted blindly because
        /// <c>Resources.UnloadUnusedAssets</c> can take an asset back out from
        /// under a live cache, and a Unity reference that has gone away compares
        /// equal to null instead of throwing.
        /// </para>
        /// </summary>
        public static GameObject Resolve(NebulaUiPrefab id)
        {
            int index = (int)id;
            if (Attempted[index] && Cache[index] != null) return Cache[index];

            var prefab = Resources.Load<GameObject>(Paths[index]);
            Cache[index] = prefab;
            Attempted[index] = true;
            return prefab;
        }

        /// <summary>True when the asset for this entry exists and loads.</summary>
        public static bool Has(NebulaUiPrefab id) => Resolve(id) != null;

        /// <summary>
        /// Whether this entry has been asked for since the last <see cref="Clear"/>,
        /// without asking for it now.
        /// <para>
        /// Test-facing, and the only honest way to prove which prefab a widget came
        /// out of: a row and a panel end up structurally identical once the factory
        /// has configured them, so the evidence has to be caught at load time.
        /// </para>
        /// </summary>
        public static bool Loaded(NebulaUiPrefab id) => Attempted[(int)id] && Cache[(int)id] != null;

        // ---- instantiation ---------------------------------------------------

        /// <summary>
        /// A configured copy of the authored prefab, parented and renamed, or
        /// <c>null</c> when the prefab is missing -- in which case the miss is
        /// recorded and the caller is expected to build from code.
        /// </summary>
        public static GameObject Instantiate(NebulaUiPrefab id, RectTransform parent, string name)
        {
            GameObject prefab = Resolve(id);
            if (prefab == null)
            {
                NoteFallback(id);
                return null;
            }

            GameObject clone = Object.Instantiate(prefab, parent, false);
            clone.name = name;
            return clone;
        }

        /// <inheritdoc cref="Instantiate"/>
        public static RectTransform InstantiateRect(NebulaUiPrefab id, RectTransform parent, string name)
        {
            GameObject clone = Instantiate(id, parent, name);
            return clone == null ? null : (RectTransform)clone.transform;
        }

        /// <summary>
        /// Records that a widget was built from code instead of from its prefab.
        /// Public because <see cref="NebulaUiFactory"/> also falls back for reasons
        /// the catalog cannot see -- a prefab that loaded but is missing the child
        /// the factory needs is as much a fallback as one that never loaded.
        /// </summary>
        public static void NoteFallback(NebulaUiPrefab id)
        {
            FallbacksUsed++;
            if (!Fallbacks.Contains(id)) Fallbacks.Add(id);
        }

        // ---- surface routing -------------------------------------------------

        /// <summary>
        /// Which authored surface a panel request belongs to.
        /// <para>
        /// Screens ask for a panel and hand in the sprite they want; that sprite is
        /// what says whether the thing being built is a list row, a card, a
        /// structure slot or a section panel. Routing on it here is what lets every
        /// dynamic row in the game come out of <c>PrimitiveRowSurface</c> without a
        /// single page changing a line -- and it means a designer editing that one
        /// prefab changes every row at once.
        /// </para>
        /// </summary>
        public static NebulaUiPrefab SurfaceFor(Sprite sprite)
        {
            if (sprite == null) return NebulaUiPrefab.PrimitivePanel;

            string id = sprite.name;
            if (id.StartsWith("row.", System.StringComparison.Ordinal))
                return NebulaUiPrefab.PrimitiveRowSurface;
            if (id.StartsWith("card.", System.StringComparison.Ordinal))
                return NebulaUiPrefab.PrimitiveCardSurface;
            if (id.StartsWith("slot.", System.StringComparison.Ordinal))
                return NebulaUiPrefab.PrimitiveSlotSurface;
            return NebulaUiPrefab.PrimitivePanel;
        }

        // ---- lifecycle -------------------------------------------------------

        /// <summary>
        /// Drops every cached asset reference and the fallback record.
        /// <para>
        /// Called on session teardown so a restart cannot hold the previous
        /// session's assets alive, and so a test can measure a genuinely cold
        /// boot. The arrays themselves are fixed length and are reused, which is
        /// why a thousand open/close cycles cannot grow this.
        /// </para>
        /// </summary>
        public static void Clear()
        {
            for (int i = 0; i < Cache.Length; i++)
            {
                Cache[i] = null;
                Attempted[i] = false;
            }
            Fallbacks.Clear();
            FallbacksUsed = 0;
        }

        /// <inheritdoc cref="Clear"/>
        public static void ResetForSession() => Clear();

        // ---- reporting -------------------------------------------------------

        /// <summary>One entry's identity, location and whether the asset is really there.</summary>
        public readonly struct Entry
        {
            public readonly NebulaUiPrefab Id;
            public readonly string ResourcePath;
            public readonly bool Resolved;

            public Entry(NebulaUiPrefab id, string resourcePath, bool resolved)
            {
                Id = id;
                ResourcePath = resourcePath;
                Resolved = resolved;
            }

            public override string ToString() =>
                (Resolved ? "resolved " : "MISSING  ") + ResourcePath;
        }

        /// <summary>
        /// Walks every entry and says which resolved. This is the coverage test's
        /// input: a prefab that exists on disk but is unreachable from here is as
        /// much a defect as one that was never built.
        /// </summary>
        public static void Report(List<Entry> into)
        {
            if (into == null) return;
            into.Clear();
            for (int i = 0; i < Ids.Length; i++)
                into.Add(new Entry(Ids[i], Paths[i], Resolve(Ids[i]) != null));
        }

        /// <inheritdoc cref="Report(List{Entry})"/>
        public static List<Entry> Report()
        {
            var list = new List<Entry>(Ids.Length);
            Report(list);
            return list;
        }

        /// <summary>
        /// The twenty-four groups the interface is required to author, and which
        /// prefabs stand for each. Shared by the builder and the coverage test so
        /// the requirement and the assets cannot drift apart.
        /// </summary>
        public readonly struct GroupDefinition
        {
            public readonly int Number;
            public readonly string Title;
            public readonly NebulaUiPrefab[] Members;

            public GroupDefinition(int number, string title, params NebulaUiPrefab[] members)
            {
                Number = number;
                Title = title;
                Members = members;
            }
        }

        private static readonly GroupDefinition[] Groups =
        {
            new GroupDefinition(1, "UI root", NebulaUiPrefab.UiRoot),
            new GroupDefinition(2, "Safe-area shell", NebulaUiPrefab.SafeAreaShell),
            new GroupDefinition(3, "Main menu", NebulaUiPrefab.MainMenu),
            new GroupDefinition(4, "Parent menu shell", NebulaUiPrefab.ParentMenuShell),
            new GroupDefinition(5, "Header and page navigation", NebulaUiPrefab.HeaderBar),
            new GroupDefinition(6, "Resource and status strip", NebulaUiPrefab.ResourceStatusStrip),
            new GroupDefinition(7, "Left context rail", NebulaUiPrefab.LeftContextRail),
            new GroupDefinition(8, "Right context rail", NebulaUiPrefab.RightContextRail),
            new GroupDefinition(9, "Standard panel", NebulaUiPrefab.StandardPanel),
            new GroupDefinition(10, "Tab navigation", NebulaUiPrefab.TabBar),
            new GroupDefinition(11, "Standard button", NebulaUiPrefab.StandardButton),
            new GroupDefinition(12, "Modal", NebulaUiPrefab.Modal),
            new GroupDefinition(13, "Confirmation dialog", NebulaUiPrefab.ConfirmationDialog),
            new GroupDefinition(14, "Tooltip", NebulaUiPrefab.Tooltip),
            new GroupDefinition(15, "Toast", NebulaUiPrefab.Toast),
            new GroupDefinition(16, "Progress and segmented bar",
                NebulaUiPrefab.ProgressBar, NebulaUiPrefab.SegmentedBar),
            new GroupDefinition(17, "Resource row", NebulaUiPrefab.ResourceRow),
            new GroupDefinition(18, "Fleet row", NebulaUiPrefab.FleetRow),
            new GroupDefinition(19, "Planet card", NebulaUiPrefab.PlanetCard),
            new GroupDefinition(20, "Module row", NebulaUiPrefab.ModuleRow),
            new GroupDefinition(21, "Attention card", NebulaUiPrefab.AttentionCard),
            new GroupDefinition(22, "Save slot", NebulaUiPrefab.SaveSlot),
            new GroupDefinition(23, "Settings rows",
                NebulaUiPrefab.SettingsToggleRow, NebulaUiPrefab.SettingsSliderRow,
                NebulaUiPrefab.SettingsBindingRow),
            new GroupDefinition(24, "HUD vitals and target cluster",
                NebulaUiPrefab.HudVitals, NebulaUiPrefab.HudTargetCluster),
        };

        /// <inheritdoc cref="Groups"/>
        public static IReadOnlyList<GroupDefinition> RequiredGroups => Groups;

        /// <summary>
        /// The loading, empty and error faces of a panel. Kept beside the groups
        /// rather than inside one, because every one of the twenty-four can be in
        /// any of the three.
        /// </summary>
        public static readonly NebulaUiPrefab[] StateVariants =
        {
            NebulaUiPrefab.PanelLoadingState,
            NebulaUiPrefab.PanelEmptyState,
            NebulaUiPrefab.PanelErrorState,
        };

        /// <summary>The primitives the factory instantiates for every widget it makes.</summary>
        public static readonly NebulaUiPrefab[] Primitives =
        {
            NebulaUiPrefab.PrimitiveGroup,
            NebulaUiPrefab.PrimitivePanel,
            NebulaUiPrefab.PrimitiveRect,
            NebulaUiPrefab.PrimitiveIcon,
            NebulaUiPrefab.PrimitiveLabel,
            NebulaUiPrefab.PrimitiveButton,
            NebulaUiPrefab.PrimitiveBar,
            NebulaUiPrefab.PrimitiveRowSurface,
            NebulaUiPrefab.PrimitiveCardSurface,
            NebulaUiPrefab.PrimitiveSlotSurface,
        };

        // ---- paths -----------------------------------------------------------

        private static string[] BuildPaths()
        {
            var paths = new string[Ids.Length];
            for (int i = 0; i < Ids.Length; i++)
                paths[i] = Root + "/" + Folder(Ids[i]) + "/" + Ids[i];
            return paths;
        }

        /// <summary>
        /// The folder an entry lives in. Derived rather than stored so a new enum
        /// member cannot be added without deciding where it belongs; anything left
        /// undecided lands in a folder the coverage test refuses.
        /// </summary>
        public static string Folder(NebulaUiPrefab id)
        {
            switch (id)
            {
                case NebulaUiPrefab.PrimitiveGroup:
                case NebulaUiPrefab.PrimitivePanel:
                case NebulaUiPrefab.PrimitiveRect:
                case NebulaUiPrefab.PrimitiveIcon:
                case NebulaUiPrefab.PrimitiveLabel:
                case NebulaUiPrefab.PrimitiveButton:
                case NebulaUiPrefab.PrimitiveBar:
                case NebulaUiPrefab.PrimitiveRowSurface:
                case NebulaUiPrefab.PrimitiveCardSurface:
                case NebulaUiPrefab.PrimitiveSlotSurface:
                    return "Primitives";

                case NebulaUiPrefab.UiRoot:
                case NebulaUiPrefab.SafeAreaShell:
                    return "Root";

                case NebulaUiPrefab.MainMenu:
                case NebulaUiPrefab.ParentMenuShell:
                case NebulaUiPrefab.StrategicMapHandoffShell:
                    return "Screens";

                case NebulaUiPrefab.HeaderBar:
                case NebulaUiPrefab.ResourceStatusStrip:
                case NebulaUiPrefab.LeftContextRail:
                case NebulaUiPrefab.RightContextRail:
                case NebulaUiPrefab.TabBar:
                    return "Chrome";

                case NebulaUiPrefab.StandardPanel:
                case NebulaUiPrefab.PanelLoadingState:
                case NebulaUiPrefab.PanelEmptyState:
                case NebulaUiPrefab.PanelErrorState:
                    return "Panels";

                case NebulaUiPrefab.StandardButton:
                case NebulaUiPrefab.ProgressBar:
                case NebulaUiPrefab.SegmentedBar:
                    return "Controls";

                case NebulaUiPrefab.Modal:
                case NebulaUiPrefab.ConfirmationDialog:
                case NebulaUiPrefab.Tooltip:
                case NebulaUiPrefab.Toast:
                    return "Overlays";

                case NebulaUiPrefab.ResourceRow:
                case NebulaUiPrefab.FleetRow:
                case NebulaUiPrefab.ModuleRow:
                case NebulaUiPrefab.SettingsToggleRow:
                case NebulaUiPrefab.SettingsSliderRow:
                case NebulaUiPrefab.SettingsBindingRow:
                    return "Rows";

                case NebulaUiPrefab.PlanetCard:
                case NebulaUiPrefab.AttentionCard:
                case NebulaUiPrefab.SaveSlot:
                    return "Cards";

                case NebulaUiPrefab.HudVitals:
                case NebulaUiPrefab.HudTargetCluster:
                    return "Hud";

                default:
                    return Undecided;
            }
        }

        /// <summary>
        /// Where an entry with no home lands. Never a real folder -- the coverage
        /// test fails on it, which is the point.
        /// </summary>
        public const string Undecided = "Unfiled";

        /// <summary>
        /// The named child slots an authored composite is required to carry, so
        /// the builder and the test agree on the contract without either one
        /// hard-coding the other's strings.
        /// </summary>
        public static IReadOnlyList<string> SlotsOf(NebulaUiPrefab id)
        {
            switch (id)
            {
                case NebulaUiPrefab.UiRoot: return RootSlots;
                case NebulaUiPrefab.SafeAreaShell: return SafeAreaSlots;
                case NebulaUiPrefab.MainMenu: return MainMenuSlots;
                case NebulaUiPrefab.ParentMenuShell: return ParentMenuSlots;
                case NebulaUiPrefab.HeaderBar: return HeaderSlots;
                case NebulaUiPrefab.ResourceStatusStrip: return StripSlots;
                case NebulaUiPrefab.LeftContextRail: return LeftRailSlots;
                case NebulaUiPrefab.RightContextRail: return RightRailSlots;
                case NebulaUiPrefab.StandardPanel: return PanelSlots;
                case NebulaUiPrefab.TabBar: return TabSlots;
                case NebulaUiPrefab.StandardButton: return ButtonSlots;
                case NebulaUiPrefab.Modal: return ModalSlots;
                case NebulaUiPrefab.ConfirmationDialog: return DialogSlots;
                case NebulaUiPrefab.Tooltip: return TooltipSlots;
                case NebulaUiPrefab.Toast: return ToastSlots;
                case NebulaUiPrefab.ProgressBar: return ProgressSlots;
                case NebulaUiPrefab.SegmentedBar: return SegmentedSlots;
                case NebulaUiPrefab.ResourceRow: return ResourceRowSlots;
                case NebulaUiPrefab.FleetRow: return FleetRowSlots;
                case NebulaUiPrefab.PlanetCard: return PlanetCardSlots;
                case NebulaUiPrefab.ModuleRow: return ModuleRowSlots;
                case NebulaUiPrefab.AttentionCard: return AttentionCardSlots;
                case NebulaUiPrefab.SaveSlot: return SaveSlotSlots;
                case NebulaUiPrefab.SettingsToggleRow: return ToggleRowSlots;
                case NebulaUiPrefab.SettingsSliderRow: return SliderRowSlots;
                case NebulaUiPrefab.SettingsBindingRow: return BindingRowSlots;
                case NebulaUiPrefab.HudVitals: return VitalsSlots;
                case NebulaUiPrefab.HudTargetCluster: return TargetSlots;
                case NebulaUiPrefab.PanelLoadingState: return LoadingSlots;
                case NebulaUiPrefab.PanelEmptyState: return EmptySlots;
                case NebulaUiPrefab.PanelErrorState: return ErrorSlots;
                case NebulaUiPrefab.PrimitiveButton: return ButtonSlots;
                case NebulaUiPrefab.PrimitiveBar: return BarSlots;
                default: return NoSlots;
            }
        }

        private static readonly string[] NoSlots = new string[0];
        // Named exactly as NebulaUiShell names its own layers, so adopting this
        // prefab is a swap rather than a rename.
        private static readonly string[] RootSlots =
            { "HUD Static", "HUD Dynamic", "Menu", "Overlay" };
        private static readonly string[] SafeAreaSlots = { "SafeArea" };
        private static readonly string[] MainMenuSlots = { "Backdrop", "Title", "Actions", "Footer" };
        private static readonly string[] ParentMenuSlots =
            { "Scrim", "Header", "LeftRail", "Content", "RightRail" };
        private static readonly string[] HeaderSlots = { "Title", "Breadcrumb", "Tabs", "Close" };
        private static readonly string[] StripSlots = { "Resources", "Status", "Clock" };
        private static readonly string[] LeftRailSlots = { "Items", "Indicator" };
        private static readonly string[] RightRailSlots = { "Title", "Actions", "Hints" };
        private static readonly string[] PanelSlots = { "Title", "Body", "States" };
        private static readonly string[] TabSlots = { "Tabs", "Underline" };
        private static readonly string[] ButtonSlots = { "Label" };
        private static readonly string[] ModalSlots = { "Scrim", "Surface" };

        // Paths, not names. The dialog's parts live inside its surface, and a
        // by-name search deep in a composite is how you end up wiring the wrong
        // "Title" the first time two composites nest.
        private static readonly string[] DialogSlots =
        {
            "Scrim", "Surface",
            "Surface/Title", "Surface/Body", "Surface/Confirm", "Surface/Cancel",
        };
        private static readonly string[] TooltipSlots = { "Text" };
        private static readonly string[] ToastSlots = { "Icon", "Text", "Progress" };
        private static readonly string[] ProgressSlots = { "Fill" };
        private static readonly string[] SegmentedSlots = { "Fill", "Ticks" };
        private static readonly string[] BarSlots = { "Fill" };
        private static readonly string[] ResourceRowSlots = { "Icon", "Name", "Amount" };
        private static readonly string[] FleetRowSlots = { "Icon", "Name", "State", "Condition" };
        private static readonly string[] PlanetCardSlots = { "Name", "Kind", "Slots", "Status" };
        private static readonly string[] ModuleRowSlots = { "Name", "Tier", "Progress", "State" };
        private static readonly string[] AttentionCardSlots = { "Glyph", "Title", "Detail" };
        private static readonly string[] SaveSlotSlots = { "Name", "Stamp", "Detail" };
        private static readonly string[] ToggleRowSlots = { "Name", "Control", "Detail" };
        private static readonly string[] SliderRowSlots = { "Name", "Control", "Value" };
        private static readonly string[] BindingRowSlots = { "Name", "Control", "Value" };
        private static readonly string[] VitalsSlots = { "Hull", "Armor", "Shield", "Energy" };
        private static readonly string[] TargetSlots = { "Reticle", "Name", "Condition", "Distance" };
        private static readonly string[] LoadingSlots = { "Message" };
        private static readonly string[] EmptySlots = { "Message" };
        private static readonly string[] ErrorSlots = { "Message" };

        /// <summary>
        /// A named child of an authored composite. Ordinal name match on the
        /// immediate children only -- a deep scan would find the wrong "Title"
        /// the moment a composite nests another composite.
        /// </summary>
        public static RectTransform Slot(RectTransform root, string slot)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (string.Equals(child.name, slot, System.StringComparison.Ordinal))
                    return child as RectTransform;
            }
            return null;
        }

        /// <summary>
        /// A slot addressed by an explicit path, one named step at a time.
        /// Deliberately not a search: every segment has to exist where the
        /// composite says it does.
        /// </summary>
        public static RectTransform Descendant(RectTransform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;

            RectTransform current = root;
            int start = 0;
            while (start <= path.Length)
            {
                int separator = path.IndexOf('/', start);
                int end = separator < 0 ? path.Length : separator;
                current = Slot(current, path.Substring(start, end - start));
                if (current == null || separator < 0) return current;
                start = separator + 1;
            }
            return current;
        }

        /// <summary>
        /// The sprite an authored surface is expected to carry when nothing has
        /// overridden it. Used by the builder so the prefab on disk is already
        /// correct rather than blank until a screen touches it.
        /// </summary>
        public static Sprite DefaultSpriteOf(NebulaUiPrefab id)
        {
            switch (id)
            {
                case NebulaUiPrefab.PrimitivePanel:
                case NebulaUiPrefab.StandardPanel:
                case NebulaUiPrefab.PanelLoadingState:
                case NebulaUiPrefab.PanelEmptyState:
                case NebulaUiPrefab.PanelErrorState:
                case NebulaUiPrefab.LeftContextRail:
                case NebulaUiPrefab.RightContextRail:
                case NebulaUiPrefab.HeaderBar:
                case NebulaUiPrefab.ResourceStatusStrip:
                case NebulaUiPrefab.TabBar:
                case NebulaUiPrefab.HudVitals:
                case NebulaUiPrefab.HudTargetCluster:
                    return NebulaUiAssetCatalog.PanelPrimary;

                case NebulaUiPrefab.PrimitiveRowSurface:
                case NebulaUiPrefab.ResourceRow:
                case NebulaUiPrefab.FleetRow:
                case NebulaUiPrefab.ModuleRow:
                case NebulaUiPrefab.SettingsToggleRow:
                case NebulaUiPrefab.SettingsSliderRow:
                case NebulaUiPrefab.SettingsBindingRow:
                    return NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default);

                case NebulaUiPrefab.PrimitiveCardSurface:
                case NebulaUiPrefab.PlanetCard:
                case NebulaUiPrefab.SaveSlot:
                    return NebulaUiAssetCatalog.Card(NebulaUiAssetCatalog.CardState.Default);

                case NebulaUiPrefab.AttentionCard:
                    return NebulaUiAssetCatalog.Card(NebulaUiAssetCatalog.CardState.Warning);

                case NebulaUiPrefab.PrimitiveSlotSurface:
                    return NebulaUiAssetCatalog.Slot(NebulaUiAssetCatalog.SlotState.Empty);

                case NebulaUiPrefab.Modal:
                case NebulaUiPrefab.ConfirmationDialog:
                    return NebulaUiAssetCatalog.Modal;

                case NebulaUiPrefab.Tooltip:
                    return NebulaUiAssetCatalog.Tooltip;

                case NebulaUiPrefab.Toast:
                    return NebulaUiAssetCatalog.Toast;

                default:
                    return null;
            }
        }
    }
}
