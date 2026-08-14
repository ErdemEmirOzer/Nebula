#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Nebula.UI.Design;
using Nebula.UI.Shell;

namespace Nebula.Editor
{
    /// <summary>
    /// Authors every interface prefab <see cref="NebulaUiPrefabCatalog"/> can hand
    /// out, from code, in the editor.
    /// <para>
    /// Prefab YAML is never written by hand here. The hierarchy is assembled with
    /// the same calls the interface used to make at runtime and then saved through
    /// <see cref="PrefabUtility.SaveAsPrefabAsset"/>, so the local file ids Unity
    /// assigns are Unity's own and a nested reference cannot end up pointing at a
    /// object id that no importer will ever produce.
    /// </para>
    /// <para>
    /// <b>Idempotent by comparison, not by hope.</b> Each entry is built in memory,
    /// fingerprinted, and compared against the fingerprint of the prefab already on
    /// disk. Identical means the asset is not touched at all: no write, no
    /// re-import, no meta churn, and therefore no chance of the GUID moving under a
    /// scene or a prefab that references it. A second run writes nothing, which is
    /// exactly what <c>UiFinalPrefabCoverageTests</c> asserts.
    /// </para>
    /// <para>
    /// The builder never calls <see cref="NebulaUiFactory"/>'s creation methods.
    /// Those now instantiate prefabs, and a builder that instantiated the prefabs
    /// it is about to author would bootstrap itself off whatever was already on
    /// disk. Only the factory's pure layout helpers are shared, so the authored
    /// asset and the code fallback cannot drift.
    /// </para>
    /// </summary>
    public static class NebulaUiPrefabBuilder
    {
        /// <summary>Where the prefabs live. Under Resources so the catalog can reach them.</summary>
        public const string AssetRoot = "Assets/Resources";

        /// <summary>How many prefabs the last run actually wrote.</summary>
        public static int WrittenLastRun { get; private set; }

        /// <summary>How many the last run found already correct and left alone.</summary>
        public static int UnchangedLastRun { get; private set; }

        private static readonly List<string> Written = new List<string>();

        /// <summary>
        /// Which entries the last run rewrote. Without this a failing idempotency
        /// check says only that something moved, and the next person has to bisect
        /// forty-two prefabs to find out what.
        /// </summary>
        public static IReadOnlyList<string> WrittenPathsLastRun => Written;

        /// <summary>
        /// Rebuilds every interface prefab.
        /// <para>
        /// <b>Never call this from a test.</b> It brackets its writes in
        /// <see cref="AssetDatabase.StartAssetEditing"/>/<see cref="AssetDatabase.StopAssetEditing"/>,
        /// and <c>StopAssetEditing</c> ends in an import pass over the project. Run
        /// from a fixture that pass happens inside the EditMode runner's own update
        /// loop, which is how a full suite livelocked here for forty-nine minutes
        /// with eight file-hasher threads pinned and no results file. A test that
        /// wants to know whether this builder is idempotent asks <see cref="PlanAll"/>,
        /// which answers the same question and writes nothing.
        /// </para>
        /// </summary>
        [MenuItem("Nebula/UI/Rebuild Interface Prefabs")]
        public static void BuildAll()
        {
            WrittenLastRun = 0;
            UnchangedLastRun = 0;
            Written.Clear();

            // No refresh before the loop. Nothing on disk has changed at this point --
            // the hierarchies are about to be built from code -- so a forced synchronous
            // refresh only re-hashes the entire project. That is survivable from the
            // menu with a warm cache, and it livelocked a full EditMode run: the
            // coverage fixture calls this from OneTimeSetUp, so the refresh ran inside
            // the test runner's update loop with every sprite, prefab and audio asset in
            // the project going through the file hasher. Fifteen minutes, no output, the
            // main thread spinning in the runner's own loop.
            //
            // The writes are batched instead, so 42 saves cost one import pass rather
            // than 42 cascading ones. StopAssetEditing is in a finally because leaving
            // the database in the editing state would freeze every later import.
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.All) BuildOne(id);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (WrittenLastRun > 0) AssetDatabase.SaveAssets();

            // Anything the catalog cached before the rebuild is now stale.
            NebulaUiPrefabCatalog.Clear();

            Debug.Log($"Interface prefabs: {WrittenLastRun} written, {UnchangedLastRun} already correct.");
        }

        /// <summary>The asset path for one entry, derived from the catalog's resource path.</summary>
        public static string AssetPathOf(NebulaUiPrefab id) =>
            $"{AssetRoot}/{NebulaUiPrefabCatalog.PathOf(id)}.prefab";

        private static void BuildOne(NebulaUiPrefab id)
        {
            string path = AssetPathOf(id);
            GameObject built = Build(id);
            try
            {
                Verify(id, built);

                if (Compare(built, path, out string _))
                {
                    UnchangedLastRun++;
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(built, path);
                if (saved == null) throw new InvalidOperationException($"Failed to save prefab: {path}");
                WrittenLastRun++;
                Written.Add(path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(built);
            }
        }

        // =====================================================================
        // DRY RUN
        // =====================================================================

        /// <summary>
        /// What <see cref="BuildAll"/> would write, without writing any of it.
        /// An empty result is the idempotency claim, stated as a measurement.
        /// <para>
        /// Every entry is assembled in memory, checked against its declared slot
        /// contract and fingerprinted, and the fingerprint is compared against the
        /// prefab already on disk -- the same comparison <see cref="BuildOne"/>
        /// makes, through the same <see cref="Compare"/> call, so the plan and the
        /// build cannot drift into disagreeing.
        /// </para>
        /// <para>
        /// Nothing here writes a file, saves an asset, refreshes the database,
        /// brackets an editing scope, or generates a script. It loads assets that
        /// are already imported and compares strings. That is what makes it safe to
        /// run inside the EditMode runner's update loop, where <see cref="BuildAll"/>
        /// is not.
        /// </para>
        /// </summary>
        /// <returns>
        /// One line per entry that would be rewritten, naming the entry, its path
        /// and the first fingerprint line that differs. Empty when the assets on
        /// disk already match the recipe.
        /// </returns>
        public static IReadOnlyList<string> PlanAll()
        {
            var plan = new List<string>();
            foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.All)
            {
                GameObject built = Build(id);
                try
                {
                    Verify(id, built);

                    string path = AssetPathOf(id);
                    if (!Compare(built, path, out string detail))
                        plan.Add($"{id} -> {path}: {detail}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(built);
                }
            }
            return plan;
        }

        /// <summary>
        /// Whether the prefab on disk already is what was just built. Read-only:
        /// <see cref="AssetDatabase.LoadAssetAtPath{T}"/> on an imported asset, then
        /// a string comparison.
        /// </summary>
        private static bool Compare(GameObject built, string path, out string detail)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing == null)
            {
                detail = "no prefab asset on disk";
                return false;
            }

            string onDisk = Fingerprint(existing);
            string inMemory = Fingerprint(built);
            if (onDisk == inMemory)
            {
                detail = null;
                return true;
            }

            detail = FirstDifference(onDisk, inMemory);
            return false;
        }

        /// <summary>
        /// The first fingerprint line the two disagree on. Without this a failing
        /// idempotency check says only that something moved, and the next person
        /// bisects forty-two prefabs to find out what.
        /// </summary>
        private static string FirstDifference(string onDisk, string inMemory)
        {
            string[] a = onDisk.Split('\n');
            string[] b = inMemory.Split('\n');
            int shared = Math.Min(a.Length, b.Length);

            for (int line = 0; line < shared; line++)
            {
                if (a[line] == b[line]) continue;
                return $"line {line + 1}\n      disk:  {Clip(a[line])}\n      built: {Clip(b[line])}";
            }

            if (a.Length == b.Length) return "identical lines but unequal text";
            return a.Length < b.Length
                ? $"the recipe adds {b.Length - a.Length} node(s) the asset does not have, from line {shared + 1}"
                : $"the asset has {a.Length - b.Length} node(s) the recipe no longer builds, from line {shared + 1}";
        }

        private const int ClipWidth = 220;

        private static string Clip(string line) =>
            line.Length <= ClipWidth ? line : line.Substring(0, ClipWidth) + " ...";

        /// <summary>
        /// The contract the catalog declares, checked at build time rather than
        /// discovered by a screen at runtime: every named slot exists where it was
        /// promised, and no two children share a name.
        /// </summary>
        private static void Verify(NebulaUiPrefab id, GameObject built)
        {
            var root = (RectTransform)built.transform;

            foreach (string slot in NebulaUiPrefabCatalog.SlotsOf(id))
            {
                if (NebulaUiPrefabCatalog.Descendant(root, slot) == null)
                    throw new InvalidOperationException($"{id} is missing its declared slot '{slot}'.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Transform node in built.GetComponentsInChildren<Transform>(true))
            {
                if (node == root) continue;
                string key = NodePath(node, root);
                if (!seen.Add(key))
                    throw new InvalidOperationException($"{id} has two children at '{key}'.");
            }
        }

        // =====================================================================
        // CONSTRUCTION
        // =====================================================================

        private static GameObject Build(NebulaUiPrefab id)
        {
            RectTransform root = NewRoot(id);
            switch (id)
            {
                // ---- primitives ---------------------------------------------
                case NebulaUiPrefab.PrimitiveGroup:
                    break;

                case NebulaUiPrefab.PrimitivePanel:
                case NebulaUiPrefab.PrimitiveRowSurface:
                case NebulaUiPrefab.PrimitiveCardSurface:
                case NebulaUiPrefab.PrimitiveSlotSurface:
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    break;

                case NebulaUiPrefab.PrimitiveRect:
                    Tint(root, Color.white);
                    break;

                case NebulaUiPrefab.PrimitiveIcon:
                    IconImage(root, null);
                    break;

                case NebulaUiPrefab.PrimitiveLabel:
                    LabelText(root, string.Empty, NebulaUiDesignTokens.SizeBody,
                        NebulaUiDesignTokens.InkPrimary, TextAlignmentOptions.TopLeft);
                    break;

                case NebulaUiPrefab.PrimitiveButton:
                    ButtonBody(root);
                    break;

                case NebulaUiPrefab.PrimitiveBar:
                    BarBody(root, NebulaUiAssetCatalog.BarFill);
                    break;

                // ---- 1: UI root ---------------------------------------------
                case NebulaUiPrefab.UiRoot:
                    Stretch(root);
                    // Sorting orders mirror NebulaUiShell's own constants, which are
                    // private. The coverage test builds a live shell and compares,
                    // so a change on either side is a failing test rather than a
                    // layer that quietly renders in the wrong order.
                    Layer(root, "HUD Static", 20, raycasts: true);
                    Layer(root, "HUD Dynamic", 22, raycasts: false);
                    Layer(root, "Menu", 40, raycasts: true);
                    Layer(root, "Overlay", 80, raycasts: false);
                    break;

                // ---- 2: safe-area shell -------------------------------------
                case NebulaUiPrefab.SafeAreaShell:
                    Stretch(root);
                    NebulaUiFactory.Fill(Child(root, "SafeArea"), NebulaUiDesignTokens.S6);
                    break;

                // ---- 3: main menu -------------------------------------------
                case NebulaUiPrefab.MainMenu:
                {
                    Stretch(root);
                    // The backdrop is the fullscreen fill; a nine-slice under it
                    // would be a whole screen of overdraw nobody ever sees.
                    NebulaUiFactory.Fill(Tint(Child(root, "Backdrop"), NebulaUiDesignTokens.SpaceVoid).rectTransform);
                    TextChild(root, "Title", NebulaUiDesignTokens.SizeDisplay, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingDisplay,
                        TopLeft, new Vector2(NebulaUiDesignTokens.S16, -NebulaUiDesignTokens.S16),
                        new Vector2(720f, 44f));
                    Slot(root, "Actions", TopLeft,
                        new Vector2(NebulaUiDesignTokens.S16, -160f), new Vector2(420f, 420f));
                    TextChild(root, "Footer", NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingCaption,
                        BottomLeft, new Vector2(NebulaUiDesignTokens.S16, NebulaUiDesignTokens.S6),
                        new Vector2(720f, 18f));
                    break;
                }

                // ---- 4: parent menu shell -----------------------------------
                case NebulaUiPrefab.ParentMenuShell:
                {
                    Stretch(root);
                    // Same reason as the main menu: the scrim covers the screen, so
                    // painting a nine-slice underneath it is pure overdraw.
                    NebulaUiFactory.Fill(Tint(Child(root, "Scrim"), NebulaUiDesignTokens.OverlayDim).rectTransform);
                    Panel(root, "Header", NebulaUiAssetCatalog.PanelPrimary,
                        TopLeft, Vector2.zero, new Vector2(1920f, HeaderHeight));
                    Panel(root, "LeftRail", NebulaUiAssetCatalog.PanelSecondary,
                        TopLeft, new Vector2(0f, -HeaderHeight), new Vector2(RailWidth, 1016f));
                    Slot(root, "Content", TopLeft,
                        new Vector2(RailWidth, -HeaderHeight), new Vector2(1380f, 1016f));
                    Panel(root, "RightRail", NebulaUiAssetCatalog.PanelSecondary,
                        TopRight, new Vector2(0f, -HeaderHeight), new Vector2(RightRailWidth, 1016f));
                    break;
                }

                // ---- 5: header and page navigation --------------------------
                case NebulaUiPrefab.HeaderBar:
                {
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    NebulaUiFactory.Place(root, TopLeft, Vector2.zero, new Vector2(1920f, HeaderHeight));
                    TextChild(root, "Title", NebulaUiDesignTokens.SizeScreenTitle, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingScreenTitle,
                        MiddleLeft, new Vector2(NebulaUiDesignTokens.S6, 0f), new Vector2(420f, 26f));
                    TextChild(root, "Breadcrumb", NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingCaption,
                        MiddleLeft, new Vector2(460f, 0f), new Vector2(420f, 16f));
                    Slot(root, "Tabs", MiddleLeft, new Vector2(900f, 0f), new Vector2(760f, 40f));
                    ButtonChild(root, "Close", MiddleRight,
                        new Vector2(-NebulaUiDesignTokens.S4, 0f), new Vector2(120f, 36f));
                    break;
                }

                // ---- 6: resource and status strip ---------------------------
                case NebulaUiPrefab.ResourceStatusStrip:
                {
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    NebulaUiFactory.Place(root, TopLeft, Vector2.zero, new Vector2(1920f, StripHeight));
                    Slot(root, "Resources", MiddleLeft,
                        new Vector2(NebulaUiDesignTokens.S6, 0f), new Vector2(960f, 28f));
                    Slot(root, "Status", MiddleLeft, new Vector2(1020f, 0f), new Vector2(640f, 28f));
                    TextChild(root, "Clock", NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkSecondary,
                        TextAlignmentOptions.Right, NebulaUiDesignTokens.TrackingBody,
                        MiddleRight, new Vector2(-NebulaUiDesignTokens.S6, 0f), new Vector2(160f, 18f));
                    break;
                }

                // ---- 7: left context rail -----------------------------------
                case NebulaUiPrefab.LeftContextRail:
                {
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    NebulaUiFactory.Place(root, TopLeft, Vector2.zero, new Vector2(RailWidth, 1016f));
                    NebulaUiFactory.Fill(Child(root, "Items"), NebulaUiDesignTokens.S2);
                    Icon(root, "Indicator", NebulaUiAssetCatalog.RailIndicator,
                        TopLeft, Vector2.zero, new Vector2(3f, NebulaUiDesignTokens.RowHeight));
                    break;
                }

                // ---- 8: right context rail ----------------------------------
                case NebulaUiPrefab.RightContextRail:
                {
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    NebulaUiFactory.Place(root, TopRight, Vector2.zero, new Vector2(RightRailWidth, 1016f));
                    TextChild(root, "Title", NebulaUiDesignTokens.SizeSectionTitle, NebulaUiDesignTokens.InkMuted,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingSectionTitle,
                        TopLeft, new Vector2(NebulaUiDesignTokens.S4, -NebulaUiDesignTokens.S4),
                        new Vector2(260f, 16f));
                    Slot(root, "Actions", TopLeft,
                        new Vector2(NebulaUiDesignTokens.S4, -NebulaUiDesignTokens.S8),
                        new Vector2(268f, 680f));
                    TextChild(root, "Hints", NebulaUiDesignTokens.SizeKeyPrompt, NebulaUiDesignTokens.InkMuted,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingCaption,
                        BottomLeft, new Vector2(NebulaUiDesignTokens.S4, NebulaUiDesignTokens.S4),
                        new Vector2(268f, 16f));
                    break;
                }

                // ---- 9: standard panel --------------------------------------
                case NebulaUiPrefab.StandardPanel:
                {
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    NebulaUiFactory.Place(root, TopLeft, Vector2.zero, new Vector2(560f, 320f));
                    TextChild(root, "Title", NebulaUiDesignTokens.SizeSectionTitle, NebulaUiDesignTokens.InkMuted,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingSectionTitle,
                        TopLeft, new Vector2(NebulaUiDesignTokens.S4, -NebulaUiDesignTokens.S3),
                        new Vector2(400f, 16f));
                    RectTransform body = NebulaUiFactory.Fill(Child(root, "Body"));
                    body.offsetMin = new Vector2(NebulaUiDesignTokens.S4, NebulaUiDesignTokens.S4);
                    body.offsetMax = new Vector2(-NebulaUiDesignTokens.S4, -NebulaUiDesignTokens.S8);
                    NebulaUiFactory.Fill(Child(root, "States"));
                    break;
                }

                // ---- 10: tab navigation -------------------------------------
                case NebulaUiPrefab.TabBar:
                {
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    NebulaUiFactory.Place(root, TopLeft, Vector2.zero, new Vector2(760f, 40f));
                    NebulaUiFactory.Fill(Child(root, "Tabs"));
                    Tint(Slot(root, "Underline", BottomLeft, Vector2.zero, new Vector2(120f, 2f)),
                        NebulaUiDesignTokens.SelectedEdge);
                    break;
                }

                // ---- 11: standard button ------------------------------------
                case NebulaUiPrefab.StandardButton:
                    ButtonBody(root);
                    NebulaUiFactory.Place(root, TopLeft, Vector2.zero, new Vector2(180f, 36f));
                    break;

                // ---- 12: modal ----------------------------------------------
                case NebulaUiPrefab.Modal:
                    Stretch(root);
                    NebulaUiFactory.Fill(Tint(Child(root, "Scrim"), NebulaUiDesignTokens.OverlayDim).rectTransform);
                    Panel(root, "Surface", NebulaUiPrefabCatalog.DefaultSpriteOf(id),
                        Centre, Vector2.zero, new Vector2(560f, 320f));
                    break;

                // ---- 13: confirmation dialog --------------------------------
                case NebulaUiPrefab.ConfirmationDialog:
                {
                    Stretch(root);
                    NebulaUiFactory.Fill(Tint(Child(root, "Scrim"), NebulaUiDesignTokens.OverlayDim).rectTransform);
                    RectTransform surface = Panel(root, "Surface",
                        NebulaUiPrefabCatalog.DefaultSpriteOf(id),
                        Centre, Vector2.zero, new Vector2(520f, 220f));
                    TextChild(surface, "Title", NebulaUiDesignTokens.SizeScreenTitle,
                        NebulaUiDesignTokens.InkPrimary, TextAlignmentOptions.Left,
                        NebulaUiDesignTokens.TrackingScreenTitle,
                        TopLeft, new Vector2(NebulaUiDesignTokens.S6, -NebulaUiDesignTokens.S6),
                        new Vector2(440f, 26f));
                    TextChild(surface, "Body", NebulaUiDesignTokens.SizeBody,
                        NebulaUiDesignTokens.InkSecondary, TextAlignmentOptions.TopLeft,
                        NebulaUiDesignTokens.TrackingBody,
                        TopLeft, new Vector2(NebulaUiDesignTokens.S6, -68f),
                        new Vector2(440f, 60f), wrap: true);
                    ButtonChild(surface, "Cancel", BottomRight,
                        new Vector2(-NebulaUiDesignTokens.S6, NebulaUiDesignTokens.S6),
                        new Vector2(150f, 36f));
                    ButtonChild(surface, "Confirm", BottomRight,
                        new Vector2(-(NebulaUiDesignTokens.S6 + 158f), NebulaUiDesignTokens.S6),
                        new Vector2(150f, 36f));
                    break;
                }

                // ---- 14: tooltip --------------------------------------------
                case NebulaUiPrefab.Tooltip:
                {
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    NebulaUiFactory.Place(root, TopLeft, Vector2.zero, new Vector2(320f, 44f));
                    TextChild(root, "Text", NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingBody,
                        TopLeft, new Vector2(12f, -8f), new Vector2(296f, 28f), wrap: true);
                    break;
                }

                // ---- 15: toast ----------------------------------------------
                case NebulaUiPrefab.Toast:
                {
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    NebulaUiFactory.Place(root, TopRight, Vector2.zero, new Vector2(360f, 56f));
                    Icon(root, "Icon", NebulaUiAssetCatalog.BadgeSquare,
                        MiddleLeft, new Vector2(NebulaUiDesignTokens.S3, 0f), new Vector2(20f, 20f));
                    TextChild(root, "Text", NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingBody,
                        MiddleLeft, new Vector2(44f, 0f), new Vector2(300f, 32f), wrap: true);
                    Bar(root, "Progress", BottomLeft, Vector2.zero,
                        new Vector2(360f, NebulaUiDesignTokens.BarHeight));
                    break;
                }

                // ---- 16: bars ------------------------------------------------
                case NebulaUiPrefab.ProgressBar:
                    BarBody(root, NebulaUiAssetCatalog.BarFill);
                    NebulaUiFactory.Place(root, TopLeft, Vector2.zero,
                        new Vector2(240f, NebulaUiDesignTokens.BarHeight));
                    break;

                case NebulaUiPrefab.SegmentedBar:
                    BarBody(root, NebulaUiAssetCatalog.BarFillStriped);
                    NebulaUiFactory.Place(root, TopLeft, Vector2.zero,
                        new Vector2(240f, NebulaUiDesignTokens.BarHeightLarge));
                    RectTransform ticks = Child(root, "Ticks");
                    IconImage(ticks, NebulaUiAssetCatalog.BarTicks);
                    NebulaUiFactory.Fill(ticks);
                    break;

                // ---- 17: resource row ---------------------------------------
                case NebulaUiPrefab.ResourceRow:
                {
                    Row(root, id);
                    Icon(root, "Icon", null, MiddleLeft, new Vector2(12f, 0f), new Vector2(20f, 20f));
                    TextChild(root, "Name", NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingBody,
                        MiddleLeft, new Vector2(42f, 0f), new Vector2(240f, 18f));
                    TextChild(root, "Amount", NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Right, NebulaUiDesignTokens.TrackingBody,
                        MiddleRight, new Vector2(-14f, 0f), new Vector2(120f, 18f));
                    break;
                }

                // ---- 18: fleet row ------------------------------------------
                case NebulaUiPrefab.FleetRow:
                {
                    Row(root, id, NebulaUiDesignTokens.RowHeightLarge);
                    Icon(root, "Icon", null, MiddleLeft, new Vector2(12f, 0f), new Vector2(24f, 24f));
                    TextChild(root, "Name", NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingBody,
                        TopLeft, new Vector2(48f, -10f), new Vector2(300f, 18f));
                    TextChild(root, "State", NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingCaption,
                        TopLeft, new Vector2(48f, -30f), new Vector2(300f, 16f));
                    Bar(root, "Condition", MiddleRight, new Vector2(-14f, 0f),
                        new Vector2(120f, NebulaUiDesignTokens.BarHeight));
                    break;
                }

                // ---- 19: planet card ----------------------------------------
                case NebulaUiPrefab.PlanetCard:
                {
                    Card(root, id, new Vector2(300f, 168f));
                    TextChild(root, "Name", NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingBody,
                        TopLeft, new Vector2(NebulaUiDesignTokens.S3, -NebulaUiDesignTokens.S3),
                        new Vector2(240f, 18f));
                    TextChild(root, "Kind", NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingCaption,
                        TopLeft, new Vector2(NebulaUiDesignTokens.S3, -34f), new Vector2(240f, 16f));
                    Slot(root, "Slots", TopLeft,
                        new Vector2(NebulaUiDesignTokens.S3, -60f), new Vector2(276f, 60f));
                    TextChild(root, "Status", NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingCaption,
                        BottomLeft, new Vector2(NebulaUiDesignTokens.S3, NebulaUiDesignTokens.S3),
                        new Vector2(276f, 16f));
                    break;
                }

                // ---- 20: module row -----------------------------------------
                case NebulaUiPrefab.ModuleRow:
                {
                    Row(root, id, NebulaUiDesignTokens.RowHeightLarge);
                    TextChild(root, "Name", NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingBody,
                        TopLeft, new Vector2(NebulaUiDesignTokens.S4, -10f), new Vector2(320f, 18f));
                    TextChild(root, "Tier", NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingCaption,
                        TopLeft, new Vector2(NebulaUiDesignTokens.S4, -30f), new Vector2(120f, 16f));
                    Bar(root, "Progress", BottomLeft,
                        new Vector2(NebulaUiDesignTokens.S4, NebulaUiDesignTokens.S1),
                        new Vector2(320f, NebulaUiDesignTokens.BarHeight));
                    TextChild(root, "State", NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                        TextAlignmentOptions.Right, NebulaUiDesignTokens.TrackingCaption,
                        MiddleRight, new Vector2(-NebulaUiDesignTokens.S4, 0f), new Vector2(180f, 16f));
                    break;
                }

                // ---- 21: attention card -------------------------------------
                case NebulaUiPrefab.AttentionCard:
                {
                    Card(root, id, new Vector2(360f, 88f));
                    Icon(root, "Glyph", NebulaUiAssetCatalog.Glyph(NebulaUiAssetCatalog.ShapeGlyph.Hull),
                        TopLeft, new Vector2(NebulaUiDesignTokens.S3, -NebulaUiDesignTokens.S3),
                        new Vector2(20f, 20f));
                    TextChild(root, "Title", NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingBody,
                        TopLeft, new Vector2(40f, -NebulaUiDesignTokens.S3), new Vector2(300f, 18f));
                    TextChild(root, "Detail", NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                        TextAlignmentOptions.TopLeft, NebulaUiDesignTokens.TrackingCaption,
                        TopLeft, new Vector2(40f, -36f), new Vector2(300f, 34f), wrap: true);
                    break;
                }

                // ---- 22: save slot ------------------------------------------
                case NebulaUiPrefab.SaveSlot:
                {
                    Card(root, id, new Vector2(420f, 72f));
                    TextChild(root, "Name", NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingBody,
                        TopLeft, new Vector2(NebulaUiDesignTokens.S4, -NebulaUiDesignTokens.S3),
                        new Vector2(260f, 18f));
                    TextChild(root, "Stamp", NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                        TextAlignmentOptions.Right, NebulaUiDesignTokens.TrackingCaption,
                        TopRight, new Vector2(-NebulaUiDesignTokens.S4, -NebulaUiDesignTokens.S3),
                        new Vector2(140f, 16f));
                    TextChild(root, "Detail", NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingCaption,
                        BottomLeft, new Vector2(NebulaUiDesignTokens.S4, NebulaUiDesignTokens.S3),
                        new Vector2(388f, 16f));
                    break;
                }

                // ---- 23: settings rows --------------------------------------
                case NebulaUiPrefab.SettingsToggleRow:
                case NebulaUiPrefab.SettingsBindingRow:
                {
                    Row(root, id);
                    TextChild(root, "Name", NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingBody,
                        MiddleLeft, new Vector2(NebulaUiDesignTokens.S4, 0f), new Vector2(280f, 18f));
                    ButtonChild(root, "Control", MiddleRight,
                        new Vector2(-NebulaUiDesignTokens.S4, 0f),
                        new Vector2(140f, NebulaUiDesignTokens.MinimumHitTarget));
                    TextChild(root, id == NebulaUiPrefab.SettingsToggleRow ? "Detail" : "Value",
                        NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                        TextAlignmentOptions.Right, NebulaUiDesignTokens.TrackingCaption,
                        MiddleRight, new Vector2(-170f, 0f), new Vector2(220f, 16f));
                    break;
                }

                case NebulaUiPrefab.SettingsSliderRow:
                {
                    Row(root, id);
                    TextChild(root, "Name", NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingBody,
                        MiddleLeft, new Vector2(NebulaUiDesignTokens.S4, 0f), new Vector2(280f, 18f));
                    Bar(root, "Control", MiddleRight, new Vector2(-80f, 0f),
                        new Vector2(180f, NebulaUiDesignTokens.BarHeightLarge));
                    TextChild(root, "Value", NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkSecondary,
                        TextAlignmentOptions.Right, NebulaUiDesignTokens.TrackingBody,
                        MiddleRight, new Vector2(-NebulaUiDesignTokens.S4, 0f), new Vector2(64f, 18f));
                    break;
                }

                // ---- 24: HUD ------------------------------------------------
                case NebulaUiPrefab.HudVitals:
                {
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    NebulaUiFactory.Place(root, BottomLeft,
                        new Vector2(NebulaUiDesignTokens.S6, NebulaUiDesignTokens.S6),
                        new Vector2(300f, 132f));
                    string[] vitals = { "Hull", "Armor", "Shield", "Energy" };
                    for (int i = 0; i < vitals.Length; i++)
                        Bar(root, vitals[i], TopLeft,
                            new Vector2(NebulaUiDesignTokens.S3, -(NebulaUiDesignTokens.S3 + i * 30f)),
                            new Vector2(276f, NebulaUiDesignTokens.BarHeightLarge));
                    break;
                }

                case NebulaUiPrefab.HudTargetCluster:
                {
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    NebulaUiFactory.Place(root, TopRight,
                        new Vector2(-NebulaUiDesignTokens.S6, -NebulaUiDesignTokens.S6),
                        new Vector2(320f, 108f));
                    Icon(root, "Reticle",
                        NebulaUiAssetCatalog.Reticle(NebulaUiAssetCatalog.ReticleState.Idle),
                        TopLeft, new Vector2(NebulaUiDesignTokens.S3, -NebulaUiDesignTokens.S3),
                        new Vector2(28f, 28f));
                    TextChild(root, "Name", NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkPrimary,
                        TextAlignmentOptions.Left, NebulaUiDesignTokens.TrackingBody,
                        TopLeft, new Vector2(48f, -NebulaUiDesignTokens.S3), new Vector2(260f, 18f));
                    Bar(root, "Condition", TopLeft, new Vector2(48f, -36f),
                        new Vector2(260f, NebulaUiDesignTokens.BarHeight));
                    TextChild(root, "Distance", NebulaUiDesignTokens.SizeCaption,
                        NebulaUiDesignTokens.InkSecondary, TextAlignmentOptions.Right,
                        NebulaUiDesignTokens.TrackingCaption,
                        BottomRight, new Vector2(-NebulaUiDesignTokens.S3, NebulaUiDesignTokens.S3),
                        new Vector2(200f, 16f));
                    break;
                }

                // ---- state variants -----------------------------------------
                case NebulaUiPrefab.PanelLoadingState:
                case NebulaUiPrefab.PanelEmptyState:
                case NebulaUiPrefab.PanelErrorState:
                {
                    Surface(root, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
                    NebulaUiFactory.Place(root, TopLeft, Vector2.zero, new Vector2(560f, 320f));
                    // A blank box reads as a screen that failed to load; that is the
                    // defect the resources page actually shipped with. Every state
                    // face carries a message rect so there is somewhere to say which
                    // nothing this is.
                    TextChild(root, "Message", NebulaUiDesignTokens.SizeCompact,
                        id == NebulaUiPrefab.PanelErrorState
                            ? NebulaUiDesignTokens.InkSecondary
                            : NebulaUiDesignTokens.InkMuted,
                        TextAlignmentOptions.Center, NebulaUiDesignTokens.TrackingCaption,
                        Centre, Vector2.zero, new Vector2(420f, 40f), wrap: true);
                    break;
                }

                // ---- handoff -------------------------------------------------
                case NebulaUiPrefab.StrategicMapHandoffShell:
                    // Deliberately empty. The map owns its own renderer hierarchy
                    // and nothing here may author its interior.
                    Stretch(root);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"{id} has no build recipe; add one before adding the enum member.");
            }
            return root.gameObject;
        }

        // =====================================================================
        // BUILDING BLOCKS
        // =====================================================================

        private const float HeaderHeight = 64f;
        private const float StripHeight = 44f;
        private const float RailWidth = 240f;
        private const float RightRailWidth = 300f;

        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 TopRight = new Vector2(1f, 1f);
        private static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        private static readonly Vector2 BottomRight = new Vector2(1f, 0f);
        private static readonly Vector2 MiddleLeft = new Vector2(0f, 0.5f);
        private static readonly Vector2 MiddleRight = new Vector2(1f, 0.5f);
        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// The prefab root. Built exactly the way <see cref="NebulaUiFactory"/>
        /// used to build one at runtime, so an instantiated primitive lands with
        /// the same rect defaults the screens have always positioned against.
        /// </summary>
        private static RectTransform NewRoot(NebulaUiPrefab id)
        {
            var go = new GameObject(id.ToString());
            return go.AddComponent<RectTransform>();
        }

        private static RectTransform Child(RectTransform parent, string name)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform Stretch(RectTransform rect) => NebulaUiFactory.Fill(rect);

        private static Image Surface(RectTransform rect, Sprite sprite)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static Image Tint(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Image IconImage(RectTransform rect, Sprite sprite)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI LabelText(RectTransform rect, string content, int size,
            Color color, TextAlignmentOptions alignment, float tracking = 0f, bool wrap = false)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = NebulaUiFactory.LabelAnchor;
            rect.sizeDelta = NebulaUiFactory.LabelSize;

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = NebulaUiFactory.SharedFontAsset;
            text.fontSize = size;
            text.fontStyle = FontStyles.Normal;
            text.color = color;
            text.text = content;
            text.alignment = alignment;
            text.enableWordWrapping = wrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.characterSpacing = tracking * 100f;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>The root half of a button: art, control and the reason carrier.</summary>
        private static void ButtonBody(RectTransform rect)
        {
            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = NebulaUiAssetCatalog.Button(
                NebulaUiAssetCatalog.ButtonKind.Primary, NebulaUiAssetCatalog.ControlState.Default);
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = NebulaUiAssetCatalog.Button(
                    NebulaUiAssetCatalog.ButtonKind.Primary, NebulaUiAssetCatalog.ControlState.Hover),
                pressedSprite = NebulaUiAssetCatalog.Button(
                    NebulaUiAssetCatalog.ButtonKind.Primary, NebulaUiAssetCatalog.ControlState.Pressed),
                selectedSprite = NebulaUiAssetCatalog.Button(
                    NebulaUiAssetCatalog.ButtonKind.Primary, NebulaUiAssetCatalog.ControlState.Focused),
                disabledSprite = NebulaUiAssetCatalog.Button(
                    NebulaUiAssetCatalog.ButtonKind.Primary, NebulaUiAssetCatalog.ControlState.Disabled),
            };

            // A disabled Button stops receiving pointer events, so the refusal has
            // to ride on something that still does.
            rect.gameObject.AddComponent<NebulaTooltipSource>();

            RectTransform label = Child(rect, NebulaUiFactory.ButtonLabelSlot);
            LabelText(label, string.Empty, NebulaUiDesignTokens.SizeButton,
                NebulaUiDesignTokens.InkPrimary, TextAlignmentOptions.Center,
                NebulaUiDesignTokens.TrackingButton).fontStyle = FontStyles.Bold;
            NebulaUiFactory.Fill(label);
        }

        /// <summary>The root half of a bar: the track plus the one fill it moves.</summary>
        private static void BarBody(RectTransform rect, Sprite fillSprite)
        {
            var track = rect.gameObject.AddComponent<Image>();
            track.sprite = NebulaUiAssetCatalog.BarTrack;
            track.type = Image.Type.Sliced;
            track.color = Color.white;
            track.raycastTarget = false;

            RectTransform fillRect = Child(rect, NebulaUiFactory.BarFillSlot);
            NebulaUiFactory.ApplyBarFillRect(fillRect);

            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.sprite = fillSprite;
            fill.type = Image.Type.Sliced;
            fill.color = NebulaUiDesignTokens.ActionPrimary;
            fill.raycastTarget = false;
        }

        private static void Row(RectTransform rect, NebulaUiPrefab id,
            float height = NebulaUiDesignTokens.RowHeight)
        {
            Surface(rect, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
            NebulaUiFactory.Place(rect, TopLeft, Vector2.zero, new Vector2(480f, height));
        }

        private static void Card(RectTransform rect, NebulaUiPrefab id, Vector2 size)
        {
            Surface(rect, NebulaUiPrefabCatalog.DefaultSpriteOf(id));
            NebulaUiFactory.Place(rect, TopLeft, Vector2.zero, size);
        }

        private static RectTransform Slot(RectTransform parent, string name,
            Vector2 anchor, Vector2 offset, Vector2 size) =>
            NebulaUiFactory.Place(Child(parent, name), anchor, offset, size);

        private static RectTransform Panel(RectTransform parent, string name, Sprite sprite,
            Vector2 anchor, Vector2 offset, Vector2 size)
        {
            RectTransform rect = Child(parent, name);
            Surface(rect, sprite);
            return NebulaUiFactory.Place(rect, anchor, offset, size);
        }

        private static RectTransform Icon(RectTransform parent, string name, Sprite sprite,
            Vector2 anchor, Vector2 offset, Vector2 size)
        {
            RectTransform rect = Child(parent, name);
            IconImage(rect, sprite);
            return NebulaUiFactory.Place(rect, anchor, offset, size);
        }

        private static RectTransform TextChild(RectTransform parent, string name, int size, Color color,
            TextAlignmentOptions alignment, float tracking, Vector2 anchor, Vector2 offset,
            Vector2 rectSize, bool wrap = false)
        {
            RectTransform rect = Child(parent, name);
            LabelText(rect, string.Empty, size, color, alignment, tracking, wrap);
            return NebulaUiFactory.Place(rect, anchor, offset, rectSize);
        }

        private static RectTransform ButtonChild(RectTransform parent, string name,
            Vector2 anchor, Vector2 offset, Vector2 size)
        {
            RectTransform rect = Child(parent, name);
            ButtonBody(rect);
            return NebulaUiFactory.Place(rect, anchor, offset, size);
        }

        private static RectTransform Bar(RectTransform parent, string name,
            Vector2 anchor, Vector2 offset, Vector2 size)
        {
            RectTransform rect = Child(parent, name);
            BarBody(rect, NebulaUiAssetCatalog.BarFill);
            return NebulaUiFactory.Place(rect, anchor, offset, size);
        }

        private static void Layer(RectTransform parent, string name, int sorting, bool raycasts)
        {
            RectTransform rect = Child(parent, name);
            NebulaUiFactory.Fill(rect);

            var canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sorting;

            var scaler = rect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = NebulaUiShell.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (raycasts) rect.gameObject.AddComponent<GraphicRaycaster>();
        }

        // =====================================================================
        // FINGERPRINT
        // =====================================================================

        /// <summary>
        /// A deterministic description of everything this builder decides about a
        /// prefab. Two hierarchies with the same fingerprint would serialize to the
        /// same asset, which is what lets an unchanged entry be skipped outright
        /// rather than rewritten and re-imported every run.
        /// </summary>
        public static string Fingerprint(GameObject root)
        {
            var sb = new StringBuilder(2048);
            Describe(root.transform, root.name, sb);
            return sb.ToString();
        }

        private static void Describe(Transform node, string path, StringBuilder sb)
        {
            sb.Append(path).Append(node.gameObject.activeSelf ? " on" : " off");

            // A ScreenSpaceOverlay Canvas drives its own RectTransform to the screen,
            // so on such a node these five values are output rather than authored
            // input: freshly built they read the current game view, reloaded from the
            // asset they read whatever was serialized last time. Comparing them made
            // UiRoot -- the only prefab with root-level canvases -- report a change on
            // every single run and rewrite itself forever, which is a re-import and a
            // chance for a reference to move each time. What is actually authored on a
            // canvas node is the Canvas and CanvasScaler settings, and those are still
            // compared below.
            if (node is RectTransform rect && node.GetComponent<Canvas>() == null)
                sb.Append(" rect").Append(V(rect.anchorMin)).Append(V(rect.anchorMax))
                    .Append(V(rect.pivot)).Append(V(rect.anchoredPosition)).Append(V(rect.sizeDelta));

            foreach (Component component in node.GetComponents<Component>())
            {
                if (component == null) { sb.Append(" |MISSING SCRIPT"); continue; }
                if (component is Transform) continue;
                sb.Append(" |").Append(component.GetType().Name);
                Describe(component, sb);
            }

            sb.Append('\n');
            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                Describe(child, path + "/" + child.name, sb);
            }
        }

        private static void Describe(Component component, StringBuilder sb)
        {
            switch (component)
            {
                case Image image:
                    sb.Append(' ').Append(A(image.sprite)).Append(' ').Append(image.type)
                        .Append(' ').Append(C(image.color))
                        .Append(image.preserveAspect ? " aspect" : string.Empty)
                        .Append(image.raycastTarget ? " hit" : " nohit");
                    break;

                case TextMeshProUGUI text:
                    sb.Append(' ').Append(A(text.font)).Append(' ').Append(F(text.fontSize))
                        .Append(' ').Append(text.fontStyle).Append(' ').Append(C(text.color))
                        .Append(' ').Append(text.alignment).Append(' ').Append(text.overflowMode)
                        .Append(' ').Append(F(text.characterSpacing))
                        .Append(text.enableWordWrapping ? " wrap" : " nowrap")
                        .Append(text.raycastTarget ? " hit" : " nohit")
                        .Append(" \"").Append(text.text).Append('"');
                    break;

                case Button button:
                    SpriteState state = button.spriteState;
                    sb.Append(' ').Append(button.transition)
                        .Append(button.interactable ? " on" : " off")
                        .Append(' ').Append(button.targetGraphic == null ? "-" : button.targetGraphic.name)
                        .Append(' ').Append(A(state.highlightedSprite))
                        .Append(' ').Append(A(state.pressedSprite))
                        .Append(' ').Append(A(state.selectedSprite))
                        .Append(' ').Append(A(state.disabledSprite))
                        .Append(' ').Append(button.onClick.GetPersistentEventCount());
                    break;

                case Canvas canvas:
                    sb.Append(' ').Append(canvas.renderMode).Append(' ').Append(canvas.sortingOrder);
                    break;

                case CanvasScaler scaler:
                    sb.Append(' ').Append(scaler.uiScaleMode).Append(V(scaler.referenceResolution))
                        .Append(' ').Append(scaler.screenMatchMode)
                        .Append(' ').Append(F(scaler.matchWidthOrHeight));
                    break;

                case CanvasGroup group:
                    sb.Append(' ').Append(F(group.alpha))
                        .Append(group.blocksRaycasts ? " block" : " pass")
                        .Append(group.interactable ? " on" : " off");
                    break;
            }
        }

        /// <summary>
        /// An asset's path, so the fingerprint compares which asset is referenced
        /// rather than which instance happens to be loaded this run.
        /// </summary>
        private static string A(UnityEngine.Object asset)
        {
            if (asset == null) return "-";
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? asset.name : path + "#" + asset.name;
        }

        private static string F(float value) => value.ToString("F4", CultureInfo.InvariantCulture);

        private static string V(Vector2 value) => "(" + F(value.x) + "," + F(value.y) + ")";

        private static string C(Color value) => "#" + ColorUtility.ToHtmlStringRGBA(value);

        private static string NodePath(Transform node, Transform root)
        {
            var sb = new StringBuilder(node.name);
            Transform current = node.parent;
            while (current != null && current != root)
            {
                sb.Insert(0, current.name + "/");
                current = current.parent;
            }
            return sb.ToString();
        }
    }
}
#endif
