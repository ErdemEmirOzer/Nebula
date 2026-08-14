using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The interface is authored, not assembled.
    /// <para>
    /// Every assertion here reads a real asset or drives the real factory. None of
    /// them greps for a method name and calls that coverage — this project has
    /// already shipped a screenshot suite whose seventeen captures were blank grey
    /// and whose three "passing" tests asserted nothing at all.
    /// </para>
    /// <para>
    /// <b>Read-only, deliberately.</b> This fixture verifies the forty-two prefabs
    /// that are committed to the repository. It never runs the builder, never
    /// refreshes the asset database, never writes into <c>Assets/</c> and never
    /// generates a script. An earlier version authored the prefabs from
    /// <c>OneTimeSetUp</c>; standalone it passed in seventy-five seconds, and in a
    /// full EditMode run it livelocked -- the import pass landed inside the test
    /// runner's own update loop, eight file-hasher threads saturated, resident set
    /// climbing past four gigabytes, no results file after forty-nine minutes.
    /// Whether the builder would rewrite anything is a separate question, asked
    /// without writing anything, in <c>UiPrefabIdempotencyTests</c>.
    /// </para>
    /// </summary>
    public class UiFinalPrefabCoverageTests
    {
        private NebulaUiShell shell;
        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void Reset()
        {
            NebulaUiPrefabCatalog.Clear();
            NebulaUiAssetCatalog.ResetForSession();
        }

        [TearDown]
        public void Cleanup()
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null) UnityEngine.Object.DestroyImmediate(spawned[i]);
            spawned.Clear();

            if (shell != null)
            {
                shell.Shutdown();
                UnityEngine.Object.DestroyImmediate(shell.gameObject);
                shell = null;
            }
            ExclusiveModal.ResetForSession();
            NebulaUiPrefabCatalog.Clear();
            // Also reset on the way out, not just on the way in: a fixture that
            // leaves shared static state behind makes the next one order-dependent.
            NebulaUiAssetCatalog.ResetForSession();
        }

        /// <summary>A parent for factory calls that is not itself made by the factory.</summary>
        private RectTransform Host()
        {
            var go = new GameObject("Host", typeof(RectTransform));
            spawned.Add(go);
            return (RectTransform)go.transform;
        }

        private static string AssetPath(NebulaUiPrefab id) =>
            "Assets/Resources/" + NebulaUiPrefabCatalog.PathOf(id) + ".prefab";

        // =====================================================================
        // 1 -- the twenty-four groups exist and resolve
        // =====================================================================

        [Test]
        public void EveryRequiredGroup_HasALoadablePrefabAsset()
        {
            var failures = new List<string>();
            var covered = new HashSet<int>();

            foreach (NebulaUiPrefabCatalog.GroupDefinition group in NebulaUiPrefabCatalog.RequiredGroups)
            {
                covered.Add(group.Number);
                Assert.IsNotEmpty(group.Members, $"group {group.Number} ({group.Title}) names no prefab");

                foreach (NebulaUiPrefab member in group.Members)
                {
                    string path = AssetPath(member);
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                        failures.Add($"{group.Number} {group.Title}: no asset at {path}");
                    else if (!NebulaUiPrefabCatalog.Has(member))
                        failures.Add($"{group.Number} {group.Title}: asset exists but " +
                                     $"Resources cannot reach {NebulaUiPrefabCatalog.PathOf(member)}");
                }
            }

            for (int number = 1; number <= 24; number++)
                if (!covered.Contains(number)) failures.Add($"group {number} is not declared at all");

            Assert.IsEmpty(failures,
                "The authored interface is incomplete; run Nebula/UI/Rebuild Interface Prefabs:\n  " +
                string.Join("\n  ", failures));
        }

        [Test]
        public void EveryCatalogEntry_ResolvesAndIsFiled()
        {
            List<NebulaUiPrefabCatalog.Entry> report = NebulaUiPrefabCatalog.Report();
            Assert.AreEqual(NebulaUiPrefabCatalog.All.Count, report.Count,
                "the report must cover every entry, not a subset");

            var unresolved = new List<string>();
            var unfiled = new List<string>();

            foreach (NebulaUiPrefabCatalog.Entry entry in report)
            {
                if (!entry.Resolved) unresolved.Add(entry.ResourcePath);
                if (NebulaUiPrefabCatalog.Folder(entry.Id) == NebulaUiPrefabCatalog.Undecided)
                    unfiled.Add(entry.Id.ToString());
            }

            Assert.IsEmpty(unfiled,
                "A prefab was added to the enum without deciding which folder it belongs in: " +
                string.Join(", ", unfiled));
            Assert.IsEmpty(unresolved,
                "Catalog entries with no asset behind them:\n  " + string.Join("\n  ", unresolved));
        }

        [Test]
        public void TheStateVariantsAndTheMapHandoff_AreAuthoredToo()
        {
            foreach (NebulaUiPrefab variant in NebulaUiPrefabCatalog.StateVariants)
                Assert.IsTrue(NebulaUiPrefabCatalog.Has(variant),
                    $"{variant} is missing; a page with nothing in it then shows a blank box, " +
                    "which reads as a screen that failed to load rather than an empty one");

            GameObject handoff = NebulaUiPrefabCatalog.Resolve(NebulaUiPrefab.StrategicMapHandoffShell);
            Assert.IsNotNull(handoff, "the strategic map needs a shell to attach into");
            Assert.AreEqual(0, handoff.transform.childCount,
                "the handoff shell is a mount, not a map. Its interior belongs to the map's own renderer.");
        }

        /// <summary>
        /// The forty-two prefabs mapped onto the twenty-four groups, checked in
        /// both directions: no group without a prefab, no prefab claimed by two
        /// groups, and no enum member that belongs to nothing at all.
        /// <para>
        /// A prefab standing for two groups is the failure worth catching. It reads
        /// as full coverage in a per-group count and it is not: editing that one
        /// asset moves both groups, and there is no longer any file that can be the
        /// header without also being the strip.
        /// </para>
        /// </summary>
        [Test]
        public void TheGroupMap_HasNoGapAndNoPrefabStandingForTwoGroups()
        {
            var claimedBy = new Dictionary<NebulaUiPrefab, int>();
            var duplicates = new List<string>();
            var gaps = new List<string>();

            foreach (NebulaUiPrefabCatalog.GroupDefinition group in NebulaUiPrefabCatalog.RequiredGroups)
            {
                if (group.Members == null || group.Members.Length == 0)
                {
                    gaps.Add($"group {group.Number} ({group.Title}) names no prefab");
                    continue;
                }

                foreach (NebulaUiPrefab member in group.Members)
                {
                    if (claimedBy.TryGetValue(member, out int owner))
                        duplicates.Add($"{member} stands for group {owner} and group {group.Number}");
                    else
                        claimedBy[member] = group.Number;
                }
            }

            for (int number = 1; number <= 24; number++)
            {
                bool declared = false;
                foreach (NebulaUiPrefabCatalog.GroupDefinition group in NebulaUiPrefabCatalog.RequiredGroups)
                    if (group.Number == number) { declared = true; break; }
                if (!declared) gaps.Add($"group {number} is not declared at all");
            }

            // The other eighteen entries are not groups and must not be counted as
            // if they were: ten primitives, three state faces and the map's mount.
            var accountedFor = new HashSet<NebulaUiPrefab>(claimedBy.Keys);
            foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.Primitives) accountedFor.Add(id);
            foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.StateVariants) accountedFor.Add(id);
            accountedFor.Add(NebulaUiPrefab.StrategicMapHandoffShell);

            var orphans = new List<string>();
            foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.All)
                if (!accountedFor.Contains(id))
                    orphans.Add($"{id} belongs to no group, is not a primitive, is not a state face " +
                                "and is not the map mount -- say what it is for or drop it");

            Assert.IsEmpty(gaps, string.Join("\n  ", gaps));
            Assert.IsEmpty(duplicates, string.Join("\n  ", duplicates));
            Assert.IsEmpty(orphans, string.Join("\n  ", orphans));

            Assert.AreEqual(NebulaUiPrefabCatalog.All.Count, accountedFor.Count,
                "every entry must be accounted for exactly once");
            Assert.AreEqual(24, NebulaUiPrefabCatalog.RequiredGroups.Count,
                "the requirement is twenty-four groups; changing that number is a design decision, " +
                "not a test edit");
        }

        /// <summary>
        /// A prefab's GUID is the only thing that survives a rename, and it is what
        /// every scene and nested prefab actually stores. Two assets sharing one --
        /// which is what copying a <c>.meta</c> alongside a duplicated prefab
        /// produces -- makes one of them unreachable in a way nothing else reports.
        /// </summary>
        [Test]
        public void EveryPrefab_HasItsOwnStableGuid()
        {
            var owners = new Dictionary<string, NebulaUiPrefab>(StringComparer.Ordinal);
            var failures = new List<string>();

            foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.All)
            {
                string path = AssetPath(id);
                string guid = AssetDatabase.AssetPathToGUID(path);

                if (string.IsNullOrEmpty(guid))
                {
                    failures.Add($"{id}: no GUID for {path}; the asset is not imported");
                    continue;
                }

                // The round trip is the real check. A GUID that does not resolve
                // back to the same path is a stale entry in the database rather
                // than a reference anything can follow.
                string resolved = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(resolved, path, StringComparison.Ordinal))
                    failures.Add($"{id}: GUID {guid} resolves to {resolved}, not {path}");

                if (owners.TryGetValue(guid, out NebulaUiPrefab other))
                    failures.Add($"{id} and {other} share GUID {guid}; one of them is unreachable");
                else
                    owners[guid] = id;
            }

            Assert.IsEmpty(failures, string.Join("\n  ", failures));
            Assert.AreEqual(NebulaUiPrefabCatalog.All.Count, owners.Count,
                "forty-two prefabs, forty-two distinct identities");
        }

        [Test]
        public void EveryDeclaredSlot_ExistsOnTheAuthoredPrefab()
        {
            var failures = new List<string>();

            foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.All)
            {
                GameObject prefab = NebulaUiPrefabCatalog.Resolve(id);
                if (prefab == null) continue;

                var root = (RectTransform)prefab.transform;
                foreach (string slot in NebulaUiPrefabCatalog.SlotsOf(id))
                    if (NebulaUiPrefabCatalog.Descendant(root, slot) == null)
                        failures.Add($"{id} promises a slot '{slot}' it does not carry");
            }

            Assert.IsEmpty(failures,
                "A screen wiring itself to one of these would get a null:\n  " +
                string.Join("\n  ", failures));
        }

        // =====================================================================
        // 2 -- nothing on any prefab is missing or drawn by the error shader
        // =====================================================================

        /// <summary>
        /// Ceiling on one component's serialized-property walk. See the comment at
        /// the loop for why the walk is capped rather than open.
        /// </summary>
        private const int MaxSerializedProperties = 4000;

        [Test]
        public void NoPrefabCarriesAMissingReference()
        {
            var failures = new List<string>();

            foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.All)
            {
                GameObject prefab = NebulaUiPrefabCatalog.Resolve(id);
                if (prefab == null) continue;

                foreach (Transform node in prefab.GetComponentsInChildren<Transform>(true))
                {
                    foreach (Component component in node.GetComponents<Component>())
                    {
                        if (component == null)
                        {
                            failures.Add($"{id} at {node.name} has a component whose script is gone");
                            continue;
                        }

                        // A reference to a deleted asset reads as null but keeps its
                        // instance id. That is the difference between "deliberately
                        // empty" and "broken", and it is the only way to tell them
                        // apart from outside the inspector.
                        var serialized = new SerializedObject(component);
                        SerializedProperty property = serialized.GetIterator();

                        // Bounded rather than `while (NextVisible(true))`. A managed
                        // reference that points back into its own graph makes that
                        // walk run forever, and a test that hangs reports nothing at
                        // all -- the whole reason this fixture was rewritten. The cap
                        // is an order of magnitude above the widest component here
                        // (TextMeshProUGUI, a few hundred visible properties) and
                        // failing it is a reported failure, not a silent truncation.
                        int visited = 0;
                        while (property.NextVisible(true))
                        {
                            if (++visited > MaxSerializedProperties)
                            {
                                failures.Add($"{id} at {node.name}: {component.GetType().Name} still had " +
                                             $"properties after {MaxSerializedProperties}; the walk was " +
                                             "cut short rather than left to spin");
                                break;
                            }
                            if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                            if (property.objectReferenceValue != null) continue;
                            if (property.objectReferenceInstanceIDValue == 0) continue;
                            failures.Add($"{id} at {node.name}: {component.GetType().Name}." +
                                         $"{property.propertyPath} points at a deleted asset");
                        }
                        serialized.Dispose();
                    }
                }
            }

            Assert.IsEmpty(failures,
                "Authored prefabs reference assets that are not there:\n  " +
                string.Join("\n  ", failures));
        }

        [Test]
        public void NoPrefabDrawsWithTheErrorShader_AndEveryLabelHasItsFace()
        {
            var failures = new List<string>();

            foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.All)
            {
                GameObject prefab = NebulaUiPrefabCatalog.Resolve(id);
                if (prefab == null) continue;

                foreach (Graphic graphic in prefab.GetComponentsInChildren<Graphic>(true))
                {
                    Material material = graphic.material;
                    if (material == null || material.shader == null)
                    {
                        failures.Add($"{id} at {graphic.name} has no material or no shader");
                        continue;
                    }
                    if (material.shader.name == "Hidden/InternalErrorShader")
                        failures.Add($"{id} at {graphic.name} is drawn by Unity's error shader");
                }

                foreach (TextMeshProUGUI text in prefab.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (text.font == null)
                    {
                        // A TMP label with no font asset renders nothing at all --
                        // not a fallback face, not a box, nothing.
                        failures.Add($"{id} at {text.name} has no font asset");
                        continue;
                    }
                    if (text.fontSharedMaterial == null)
                        failures.Add($"{id} at {text.name} has a font with no material");
                }
            }

            Assert.IsEmpty(failures,
                "Authored prefabs would not draw correctly:\n  " + string.Join("\n  ", failures));
        }

        [Test]
        public void EverySurfacePrefab_CarriesTheSpriteItsCatalogEntryPromises()
        {
            var failures = new List<string>();

            foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.All)
            {
                Sprite expected = NebulaUiPrefabCatalog.DefaultSpriteOf(id);
                if (expected == null) continue;

                GameObject prefab = NebulaUiPrefabCatalog.Resolve(id);
                if (prefab == null) continue;

                // A composite carries its art on the surface inside it -- a modal
                // is a scrim plus a surface, and the scrim is deliberately a plain
                // tint. Simple entries carry it on the root.
                Image image = prefab.GetComponent<Image>();
                if (image == null)
                {
                    RectTransform surface = NebulaUiPrefabCatalog.Slot(
                        (RectTransform)prefab.transform, "Surface");
                    image = surface == null ? null : surface.GetComponent<Image>();
                }

                if (image == null) { failures.Add($"{id} declares a sprite but has no Image"); continue; }
                if (image.sprite != expected)
                    failures.Add($"{id} carries {(image.sprite == null ? "nothing" : image.sprite.name)}, " +
                                 $"the catalog says {expected.name}");
            }

            Assert.IsEmpty(failures, string.Join("\n  ", failures));
        }

        // =====================================================================
        // 3 -- a production boot uses the prefabs and never the fallback
        // =====================================================================

        [Test]
        public void AProductionBoot_BuildsEveryPrimitiveFromItsPrefab()
        {
            NebulaUiPrefabCatalog.Clear();
            shell = NebulaUiShell.Create();
            NebulaTooltip.Create(shell);
            NebulaHudTargeting.Create(shell, null, null);

            RectTransform host = Host();
            NebulaUiFactory.Group(host, "Group");
            NebulaUiFactory.Panel(host, "Panel", NebulaUiAssetCatalog.PanelPrimary);
            NebulaUiFactory.Panel(host, "Row",
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            NebulaUiFactory.Panel(host, "Card",
                NebulaUiAssetCatalog.Card(NebulaUiAssetCatalog.CardState.Default));
            NebulaUiFactory.Panel(host, "Slot",
                NebulaUiAssetCatalog.Slot(NebulaUiAssetCatalog.SlotState.Empty));
            NebulaUiFactory.Rect(host, "Divider", NebulaUiDesignTokens.Divider);
            NebulaUiFactory.Icon(host, "Icon", NebulaUiAssetCatalog.BadgePill, 20f);
            NebulaUiFactory.Label(host, "Label", "ÖLÇÜM", NebulaUiDesignTokens.SizeBody,
                NebulaUiDesignTokens.InkPrimary);
            NebulaUiFactory.SectionTitle(host, "Section", "BAŞLIK");
            NebulaUiFactory.Value(host, "Value", "0", NebulaUiDesignTokens.SizeValue);
            NebulaUiFactory.ButtonControl(host, "Button", "DENE",
                NebulaUiAssetCatalog.ButtonKind.Primary, null);
            NebulaUiFactory.Bar(host, "Bar", NebulaUiDesignTokens.ActionPrimary);

            Assert.AreEqual(0, NebulaUiPrefabCatalog.FallbacksUsed,
                "The factory had to build from code because a prefab was missing or incomplete: " +
                string.Join(", ", NebulaUiPrefabCatalog.FallbackPrefabs) +
                ". The code path is a safety net for a broken import, never the normal one.");
        }

        [Test]
        public void AButtonBuiltFromItsPrefab_KeepsTheWholeContract()
        {
            NebulaUiFactory.ButtonHandle button = NebulaUiFactory.ButtonControl(
                Host(), "Blocked", "DENE", NebulaUiAssetCatalog.ButtonKind.Secondary, null);

            Assert.AreEqual(0, NebulaUiPrefabCatalog.FallbacksUsed, "it came from the prefab");
            Assert.IsNotNull(button.Button.GetComponent<NebulaTooltipSource>(),
                "a control that can refuse has to be able to say why, and a disabled " +
                "Button stops receiving pointer events");
            Assert.AreEqual(Selectable.Transition.SpriteSwap, button.Button.transition);
            Assert.AreSame(button.Background, button.Button.targetGraphic);
            Assert.AreEqual("DENE", button.Label.Value);
            Assert.IsTrue(button.Background.raycastTarget, "a button that takes no click is not a button");
            Assert.AreEqual(
                NebulaUiAssetCatalog.Button(NebulaUiAssetCatalog.ButtonKind.Secondary,
                    NebulaUiAssetCatalog.ControlState.Hover),
                button.Button.spriteState.highlightedSprite,
                "state art follows the kind that was asked for, not the prefab's authored default");

            button.SetAvailable(false, "KOMUTA KAPASİTESİ YETERSİZ");
            Assert.AreEqual("KOMUTA KAPASİTESİ YETERSİZ",
                button.Button.GetComponent<NebulaTooltipSource>().Reason());
            Assert.IsFalse(button.Button.interactable);
        }

        [Test]
        public void ALabelBuiltFromItsPrefab_StartsTopLeftAndEllipsises()
        {
            NebulaUiFactory.LabelHandle label = NebulaUiFactory.Label(
                Host(), "Text", "ÇOK UZUN BİR DEĞER", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleRight);

            Assert.AreEqual(0, NebulaUiPrefabCatalog.FallbacksUsed);
            RectTransform rect = label.Text.rectTransform;
            Assert.AreEqual(NebulaUiFactory.LabelAnchor, rect.anchorMin,
                "a caller that forgets Place() must land in the panel's corner, not float in its middle");
            Assert.AreEqual(NebulaUiFactory.LabelAnchor, rect.pivot);
            Assert.AreEqual(NebulaUiFactory.LabelSize, rect.sizeDelta);
            Assert.AreEqual(TextOverflowModes.Ellipsis, label.Text.overflowMode,
                "spilling outside its own rect is invisible; not fitting is findable");
            Assert.IsFalse(label.Text.raycastTarget, "text never eats a click");
            Assert.AreSame(NebulaUiFactory.SharedFontAsset, label.Text.font);
            Assert.AreEqual(TextAlignmentOptions.Right, label.Text.alignment);
        }

        [Test]
        public void ABarBuiltFromItsPrefab_HasAFillThatOnlyMovesOneAnchor()
        {
            NebulaUiFactory.BarHandle bar = NebulaUiFactory.Bar(
                Host(), "Bar", NebulaUiDesignTokens.ActionPrimary, striped: true);

            Assert.AreEqual(0, NebulaUiPrefabCatalog.FallbacksUsed);
            Assert.AreEqual(NebulaUiAssetCatalog.BarTrack, bar.Track.sprite);
            Assert.AreEqual(NebulaUiAssetCatalog.BarFillStriped, bar.Fill.sprite);

            RectTransform fill = bar.Fill.rectTransform;
            Assert.AreEqual(Vector2.zero, fill.anchorMin);
            Assert.AreEqual(new Vector2(0f, 0.5f), fill.pivot);

            bar.SetFraction(0.25f);
            Assert.AreEqual(new Vector2(0.25f, 1f), fill.anchorMax);
            bar.SetFraction(4f);
            Assert.AreEqual(new Vector2(1f, 1f), fill.anchorMax, "raw ratios are clamped, not trusted");
        }

        /// <summary>
        /// A row and a panel are structurally identical once the factory has
        /// configured them, so the only honest evidence of which prefab a row came
        /// from is which asset got loaded to make it.
        /// </summary>
        [Test]
        public void ADynamicRow_IsInstantiatedFromTheAuthoredRowPrefab()
        {
            NebulaUiPrefabCatalog.Clear();
            NebulaUiFactory.Panel(Host(), "Item",
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));

            Assert.IsTrue(NebulaUiPrefabCatalog.Loaded(NebulaUiPrefab.PrimitiveRowSurface),
                "a list row must come out of the authored row prefab");
            Assert.IsFalse(NebulaUiPrefabCatalog.Loaded(NebulaUiPrefab.PrimitivePanel),
                "and not out of the generic panel, or editing the row prefab changes nothing");

            NebulaUiPrefabCatalog.Clear();
            NebulaUiFactory.Panel(Host(), "Threat",
                NebulaUiAssetCatalog.Card(NebulaUiAssetCatalog.CardState.Critical));
            Assert.IsTrue(NebulaUiPrefabCatalog.Loaded(NebulaUiPrefab.PrimitiveCardSurface));

            NebulaUiPrefabCatalog.Clear();
            NebulaUiFactory.Panel(Host(), "Section", NebulaUiAssetCatalog.PanelPrimary);
            Assert.IsTrue(NebulaUiPrefabCatalog.Loaded(NebulaUiPrefab.PrimitivePanel));
            Assert.IsFalse(NebulaUiPrefabCatalog.Loaded(NebulaUiPrefab.PrimitiveRowSurface));
        }

        [Test]
        public void TheCache_IsFixedSizeAndDropsEverythingOnTeardown()
        {
            // A thousand open/close cycles is the shape the lifecycle suite drives;
            // what matters here is that resolution is bounded by the enum rather
            // than by how many times anyone asked.
            for (int cycle = 0; cycle < 1000; cycle++)
            {
                NebulaUiPrefabCatalog.Resolve(NebulaUiPrefab.PrimitivePanel);
                NebulaUiPrefabCatalog.Resolve(NebulaUiPrefab.PrimitiveLabel);
            }

            Assert.IsTrue(NebulaUiPrefabCatalog.Loaded(NebulaUiPrefab.PrimitivePanel));
            Assert.AreEqual(0, NebulaUiPrefabCatalog.FallbacksUsed,
                "resolving an existing asset is not a fallback");

            NebulaUiPrefabCatalog.Clear();
            Assert.IsFalse(NebulaUiPrefabCatalog.Loaded(NebulaUiPrefab.PrimitivePanel),
                "teardown must actually drop the references, not just say it did");
            Assert.AreEqual(0, NebulaUiPrefabCatalog.FallbacksUsed);
            Assert.IsEmpty(NebulaUiPrefabCatalog.FallbackPrefabs);
        }

        [Test]
        public void TheFallbackIsRealAndItIsRecorded()
        {
            // The safety net has to be proven to work, or the claim that a missing
            // asset is survivable is just a comment. NoteFallback is the same entry
            // point the factory uses when an asset does not load.
            int before = NebulaUiPrefabCatalog.FallbacksUsed;
            NebulaUiPrefabCatalog.NoteFallback(NebulaUiPrefab.PrimitiveBar);

            Assert.AreEqual(before + 1, NebulaUiPrefabCatalog.FallbacksUsed);
            CollectionAssert.Contains(NebulaUiPrefabCatalog.FallbackPrefabs, NebulaUiPrefab.PrimitiveBar);

            NebulaUiPrefabCatalog.NoteFallback(NebulaUiPrefab.PrimitiveBar);
            Assert.AreEqual(1, NebulaUiPrefabCatalog.FallbackPrefabs.Count,
                "the same prefab failing twice is one broken asset, not two");
        }

        [Test]
        public void TheAuthoredRoot_AgreesWithTheShellItStandsFor()
        {
            GameObject prefab = NebulaUiPrefabCatalog.Resolve(NebulaUiPrefab.UiRoot);
            Assert.IsNotNull(prefab);

            shell = NebulaUiShell.Create();

            var live = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (UiLayer layer in Enum.GetValues(typeof(UiLayer)))
            {
                Canvas canvas = shell.CanvasFor(layer);
                Assert.IsNotNull(canvas, $"the shell has no canvas for {layer}");
                // Keyed by the shell's own object name, so adopting the prefab is a
                // swap rather than a rename of four layers.
                live[canvas.name] = canvas.sortingOrder;
            }

            var authored = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Canvas canvas in prefab.GetComponentsInChildren<Canvas>(true))
                authored[canvas.name] = canvas.sortingOrder;

            // The shell's sorting constants are private, so the prefab necessarily
            // repeats them. Comparing against a live shell is what stops the copy
            // from drifting into a layer that quietly renders in the wrong order.
            CollectionAssert.AreEquivalent(live.Keys, authored.Keys,
                "the authored root must carry exactly the shell's four layers");
            foreach (KeyValuePair<string, int> entry in live)
                Assert.AreEqual(entry.Value, authored[entry.Key],
                    $"{entry.Key} renders at a different depth in the prefab than in the shell");
        }

        // =====================================================================
        // 4 -- the builder writes where the catalog looks
        //
        // Whether the builder is *idempotent* is asked in UiPrefabIdempotencyTests.
        // Nothing in this fixture invokes it; the only thing borrowed here is its
        // pure path function, which computes a string and touches no asset.
        // =====================================================================

        internal static Type BuilderType()
        {
            // The builder lives in Assembly-CSharp-Editor, which an assembly
            // definition is not allowed to reference. Reflection is the only way in,
            // and it still reaches the real builder rather than a stand-in.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType("Nebula.Editor.NebulaUiPrefabBuilder", false);
                if (type != null) return type;
            }
            return null;
        }

        [Test]
        public void TheBuilderWritesWhereTheCatalogLooks()
        {
            Type builder = BuilderType();
            Assert.IsNotNull(builder, "Nebula.Editor.NebulaUiPrefabBuilder is not compiled into the editor");

            MethodInfo assetPathOf = builder.GetMethod("AssetPathOf",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(assetPathOf, "the test needs the builder's own idea of where a prefab goes");

            var mismatched = new List<string>();
            foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.All)
            {
                var fromBuilder = (string)assetPathOf.Invoke(null, new object[] { id });
                if (fromBuilder != AssetPath(id)) mismatched.Add($"{id}: {fromBuilder} vs {AssetPath(id)}");
            }

            Assert.IsEmpty(mismatched,
                "The builder and the runtime catalog disagree about where a prefab lives:\n  " +
                string.Join("\n  ", mismatched));
        }

        // =====================================================================
        // 5 -- the remaining hand-built objects, enumerated and justified
        // =====================================================================

        /// <summary>
        /// The production interface, excluding the strategic map (its own
        /// workstream) and the two development HUDs at the root of
        /// <c>Scripts/Nebula/UI</c>, which are gated out of a release player.
        /// </summary>
        private static readonly string[] ProductionUiDirectories = { "Shell", "Menu", "Hud", "Session" };

        /// <summary>
        /// Every remaining <c>new GameObject(</c> in the production interface, with
        /// the reason it is not a prefab. The number cannot grow quietly: an
        /// unlisted file, or a listed file that grew a call, fails this test.
        /// </summary>
        private static readonly (string File, int Count, string Why)[] HandBuiltObjects =
        {
            ("NebulaUiFactory.cs", 1,
                "the documented fallback for a prefab that failed to load; the boot test asserts it is never taken"),
            ("NebulaUiShell.cs", 3,
                "the shell's own host object, its four canvas layers, and Unity's EventSystem singleton -- " +
                "the layers are what UiRoot.prefab authors and the shell has not adopted it yet"),
            ("NebulaTooltip.cs", 1, "the controller host; its panel and label already come from the factory"),
            ("NebulaConfirmationDialog.cs", 1, "the controller host; its surface already comes from the factory"),
            ("NebulaParentMenuScreen.cs", 1, "the controller host for the paused command menu"),
            ("NebulaMainMenuScreen.cs", 1, "the controller host for the session entry screen"),
            ("NebulaMainMenuHost.cs", 1, "the controller host that owns the main menu across scene loads"),
            // The three secondary entries behind the front door. They existed before
            // but nothing reached them: the main menu screen raised SettingsRequested,
            // LoadRequested and CreditsRequested and no production code subscribed, so
            // AYARLAR, KAYIT YÜKLE and KÜNYE answered a click with nothing. Wiring them
            // to the host is what put them in this scan's path for the first time --
            // they are not new hand-building, they are newly reachable.
            ("NebulaMenuPageScreen.cs", 1,
                "the controller host that hangs a real settings page off the front door; its rows come from the factory"),
            ("NebulaSaveSlotScreen.cs", 1,
                "the controller host for the save-slot list; its cards already come from the factory"),
            ("NebulaCreditsScreen.cs", 1,
                "the controller host for the credits screen; its text already comes from the factory"),
            ("NebulaHudVitals.cs", 1, "the controller host; its bars already come from the factory"),
            ("NebulaHudTargeting.cs", 1, "the controller host; its markers already come from the factory"),
            ("NebulaHudAlerts.cs", 1, "the controller host; its cards already come from the factory"),
            ("NebulaHudNavigation.cs", 1, "the controller host; its chrome already comes from the factory"),
            ("NebulaHudNotifications.cs", 1, "the controller host; its toasts already come from the factory"),
            ("NebulaHudContextBar.cs", 1, "the controller host; its strip already comes from the factory"),
            ("NebulaHudPlanetContext.cs", 1, "the controller host; its panel already comes from the factory"),
            ("NebulaExtractionContextDriver.cs", 1, "the controller host; it draws nothing itself"),
            ("NebulaShipLevelTag.cs", 1,
                "the controller host for the world-space tag; it is a TextMeshPro on the "
                + "shared SDF face, and a world-space tag has no prefab to load from"),
        };

        /// <summary>
        /// Counts direct GameObject construction per file, ignoring comments --
        /// every one of these sites is described in a comment above it, and a
        /// comment is not a call.
        /// </summary>
        private static Dictionary<string, int> ScanConstruction(IEnumerable<string> files)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                source = Regex.Replace(source, @"^[ \t]*///.*$", string.Empty, RegexOptions.Multiline);
                source = Regex.Replace(source, @"^[ \t]*//.*$", string.Empty, RegexOptions.Multiline);

                int found = Regex.Matches(source, @"new\s+GameObject\s*\(").Count;
                if (found > 0) counts[Path.GetFileName(file)] = found;
            }
            return counts;
        }

        private static IEnumerable<string> ProductionUiSources()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "Nebula", "UI");
            foreach (string directory in ProductionUiDirectories)
            {
                string path = Path.Combine(root, directory);
                if (!Directory.Exists(path)) continue;
                foreach (string file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
                    yield return file;
            }
        }

        [Test]
        public void EveryRemainingHandBuiltObject_IsListedAndJustified()
        {
            Dictionary<string, int> actual = ScanConstruction(ProductionUiSources());

            var expected = new Dictionary<string, int>(StringComparer.Ordinal);
            var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string file, int count, string why) in HandBuiltObjects)
            {
                expected[file] = count;
                reasons[file] = why;
                Assert.IsNotEmpty(why, $"{file} is exempt without saying why, which is not an exemption");
            }

            var failures = new List<string>();
            foreach (KeyValuePair<string, int> entry in actual)
            {
                if (!expected.TryGetValue(entry.Key, out int allowed))
                {
                    failures.Add($"{entry.Key} builds {entry.Value} GameObject(s) by hand and is not listed. " +
                                 "Either instantiate the authored prefab or add it here with a reason.");
                    continue;
                }
                if (entry.Value != allowed)
                    failures.Add($"{entry.Key} builds {entry.Value} by hand; {allowed} were justified " +
                                 $"({reasons[entry.Key]})");
            }

            foreach (KeyValuePair<string, int> entry in expected)
                if (!actual.ContainsKey(entry.Key))
                    failures.Add($"{entry.Key} no longer builds anything by hand; drop its exemption " +
                                 "so the list keeps meaning something");

            Assert.IsEmpty(failures,
                "The hand-built interface has changed shape:\n  " + string.Join("\n  ", failures));
        }

        /// <summary>
        /// The scan itself, proven able to fail. A suite that has already shipped
        /// three tests asserting nothing does not get to take this on trust.
        /// </summary>
        [Test]
        public void TheConstructionScan_ActuallyReports()
        {
            string directory = Path.Combine(Path.GetTempPath(), "nebula-scan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string offender = Path.Combine(directory, "Offender.cs");
            string innocent = Path.Combine(directory, "Innocent.cs");

            try
            {
                File.WriteAllText(offender, string.Join("\n", new[]
                {
                    "public static class Offender {",
                    "    static void Build() {",
                    "        var a = new GameObject(\"one\");",
                    "        var b = new  GameObject (\"two\");",
                    "        // var c = new GameObject(\"commented out\");",
                    "        /* var d = new GameObject(\"blocked out\"); */",
                    "    }",
                    "}",
                }));
                File.WriteAllText(innocent, "public static class Innocent { static void Build() { } }");

                Dictionary<string, int> counts = ScanConstruction(new[] { offender, innocent });

                Assert.IsTrue(counts.ContainsKey("Offender.cs"),
                    "the scan missed a file that plainly constructs GameObjects");
                Assert.AreEqual(2, counts["Offender.cs"],
                    "the scan must count both live calls and neither commented one -- " +
                    "if it counted 4 it is matching comments, if it counted 1 it is missing whitespace");
                Assert.IsFalse(counts.ContainsKey("Innocent.cs"),
                    "the scan invented a call in a file that has none");

                // And the assertion the real test makes, driven off this input, has
                // to come out red rather than quietly passing on an unknown file.
                var expected = new Dictionary<string, int>(StringComparer.Ordinal);
                var unlisted = new List<string>();
                foreach (KeyValuePair<string, int> entry in counts)
                    if (!expected.ContainsKey(entry.Key)) unlisted.Add(entry.Key);

                CollectionAssert.AreEqual(new[] { "Offender.cs" }, unlisted,
                    "an unlisted offender must reach the failure list");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void TheStrategicMapIsNotInScope_AndTheDevHudsAreNot()
        {
            // Scope is asserted rather than assumed: if the map ever moved under
            // Shell/ or Hud/ this suite would start policing another workstream's
            // files and fail for reasons that have nothing to do with it.
            foreach (string file in ProductionUiSources())
                Assert.IsFalse(file.Replace('\\', '/').Contains("/StrategicMap/"),
                    $"{file} belongs to the strategic map workstream");

            string root = Path.Combine(Application.dataPath, "Scripts", "Nebula", "UI");
            foreach (string dev in new[] { "FlightTestSpeedHud.cs", "SectorDebugHud.cs" })
                Assert.IsTrue(File.Exists(Path.Combine(root, dev)),
                    $"{dev} moved; it is excluded from the prefab requirement because it is " +
                    "gated behind Debug.isDebugBuild and never reaches a player");
        }
    }
}
