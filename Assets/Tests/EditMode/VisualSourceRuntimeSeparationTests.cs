using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nebula.Combat;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.EditorTools.VisualTexture;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The source/runtime split, stated as gates rather than as a convention.
    /// <para>
    /// Unity ships every file under a <c>Resources/</c> folder whether or not
    /// anything references it. That rule is what made 245 authored GLBs
    /// permanently un-droppable: no dedup, no rebinding and no import setting
    /// could take one out of the player while the folder kept putting it back.
    /// Part 3 moved them to <c>Assets/VisualSource/</c>, where they reach the
    /// build only as dependencies of the wrapper prefabs — a claim the build is
    /// allowed to reason about, and one that can therefore be reduced later.
    /// </para>
    /// <para>
    /// Nothing here asserts that the move made the build smaller, because it did
    /// not: every one of those GLBs is still nested inside a wrapper prefab under
    /// <c>Resources/VisualPrefabs/</c> and still ships. What these tests defend is
    /// the property that makes a future reduction possible at all, plus the much
    /// more fragile thing the move depends on — that not one GUID changed.
    /// </para>
    /// </summary>
    public class VisualSourceRuntimeSeparationTests
    {
        private const string SourceRoot = "Assets/VisualSource";
        private const string PrefabRoot = "Assets/Resources/VisualPrefabs";

        /// <summary>
        /// The retired legacy hull, at the source-only path Part 4 moved it to.
        /// <para>
        /// It used to be the one GLB genuinely loaded by path rather than reached
        /// through a prefab — the fallback-of-a-fallback in
        /// <c>ShipFactory</c> — and being under <c>Resources/</c> it shipped
        /// unconditionally: 25.7 MB on disk, 70.0 MB expanded in the player, 10.3%
        /// of the whole build and its single largest asset, for a picture nobody
        /// sees unless all 26 Part 24 prefabs fail to load. The fail-safe now
        /// mounts one of those authored prefabs instead, so the file is kept as
        /// source and ships nothing — see
        /// <see cref="TheRetiredLegacyHullIsOutOfTheBuildAndOffEveryLoadPath"/>.
        /// </para>
        /// </summary>
        private const string LegacyShipAsset =
            "Assets/VisualSource/PlayerShip/Legacy/PlayerShip.glb";

        /// <summary>
        /// Anchor and part nodes across all 127 wrappers, counted on this branch.
        /// A census rather than a rule — the number has no meaning beyond "the
        /// same as before", which is exactly what a preservation gate needs.
        /// </summary>
        private const int ExpectedAnchorNodes = 1961;

        /// <summary>Where the deduplicated materials and textures live — produced by
        /// <c>NebulaVisualTextureOptimizer</c>, never authored by hand.</summary>
        private const string SharedMaterialRoot = "Assets/RuntimeVisuals/Materials/";

        private const string SharedTextureRoot = "Assets/RuntimeVisuals/Textures/";

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        private static string Abs(string relative) => Path.Combine(ProjectRoot, relative);

        // ---- source / runtime classification ---------------------------------

        /// <summary>
        /// No authored model sits under a <c>Resources/</c> folder. No exceptions
        /// any more — Part 4 removed the last one.
        /// <para>
        /// This is the gate that has to stay red if anyone drops a GLB back into
        /// <c>Resources/</c>, because that single act silently re-arms the
        /// unconditional-inclusion rule for that file and nothing else in the
        /// project would notice.
        /// </para>
        /// </summary>
        [Test]
        public void NoRawModelSourceIsLeftUnderAnyResourcesFolder()
        {
            var offenders = new List<string>();
            foreach (string absolute in Directory.GetFiles(
                         Application.dataPath, "*.glb", SearchOption.AllDirectories))
            {
                string relative = Relative(absolute);
                if (IsUnderResources(relative)) offenders.Add(relative);
            }

            Assert.IsEmpty(offenders,
                "these authored models are under a Resources folder and will ship whether or "
                + "not anything references them:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// The source tree is where the models actually are, and it is not empty.
        /// Guards every other assertion in this fixture: a walk pointed at a
        /// missing folder makes emptiness checks pass for the wrong reason.
        /// </summary>
        [Test]
        public void TheSourceTreeHoldsEveryAuthoredModel()
        {
            string[] inSource = Directory.GetFiles(Abs(SourceRoot), "*.glb", SearchOption.AllDirectories);
            Assert.AreEqual(246, inSource.Length,
                "the authored model count changed without an integration decision");

            string[] everywhere = Directory.GetFiles(
                Application.dataPath, "*.glb", SearchOption.AllDirectories);
            var outside = everywhere
                .Select(Relative)
                .Where(path => !path.StartsWith(SourceRoot + "/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            // One by design: the Part 24 master, which never enters the runtime
            // table because its Mk I and Mk II sets repeat anchor names. The legacy
            // hull used to be the second entry here; Part 4 moved it into the
            // source tree, which is why the count above went from 245 to 246.
            CollectionAssert.AreEqual(
                new[] { "Assets/ArtSource/NebulaPart24/Ship/PlayerShip_Master.glb" },
                outside,
                "a model appeared outside the source tree without a recorded role");
        }

        /// <summary>
        /// Every GLB in the source tree is reached by at least one wrapper prefab.
        /// <para>
        /// The counterpart to the test above. Moving models out of
        /// <c>Resources/</c> only works because something still references them;
        /// an unreferenced file out here would simply stop shipping, silently, and
        /// the first symptom would be a missing mesh in the game rather than a
        /// failing test.
        /// </para>
        /// </summary>
        [Test]
        public void EverySourceModelIsReachedByAWrapperPrefab()
        {
            HashSet<string> referenced = PrefabDependencyPaths();
            var orphans = Directory
                .GetFiles(Abs(SourceRoot), "*.glb", SearchOption.AllDirectories)
                .Select(Relative)
                .Where(path => !referenced.Contains(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            // The retired legacy hull is the one intended orphan, and it is listed
            // rather than skipped: being unreferenced is exactly what took it out
            // of the build, so this assertion is the positive statement of the
            // saving as well as the negative one about everything else.
            CollectionAssert.AreEqual(
                new[] { LegacyShipAsset },
                orphans,
                "these models are outside Resources/ and nothing references them, so they no "
                + "longer reach the player at all:\n  " + string.Join("\n  ", orphans));
        }

        /// <summary>
        /// The move preserved identity. A GLB whose GUID changed would leave every
        /// wrapper prefab pointing at nothing, and Unity reports that as an empty
        /// prefab rather than as an error — which is exactly the kind of breakage
        /// that reaches a build.
        /// </summary>
        [Test]
        public void EverySourceModelKeptAResolvableGuid()
        {
            var problems = new List<string>();
            foreach (string absolute in Directory.GetFiles(
                         Abs(SourceRoot), "*.glb", SearchOption.AllDirectories))
            {
                string relative = Relative(absolute);
                string guid = AssetDatabase.AssetPathToGUID(relative);
                if (string.IsNullOrEmpty(guid)) { problems.Add(relative + ": no GUID"); continue; }
                if (AssetDatabase.GUIDToAssetPath(guid) != relative)
                    problems.Add($"{relative}: GUID {guid} resolves to "
                                 + AssetDatabase.GUIDToAssetPath(guid));
                if (AssetDatabase.LoadAssetAtPath<GameObject>(relative) == null)
                    problems.Add(relative + ": imports to no GameObject");
            }

            Assert.IsEmpty(problems, string.Join("\n  ", problems));
        }

        // ---- runtime call-site audit -----------------------------------------

        /// <summary>
        /// No production code loads a model by path.
        /// <para>
        /// Path loading is the failure mode this whole split is exposed to: a
        /// <c>Resources.Load</c> string is invisible to the reference graph, so a
        /// file it names can be moved, renamed or dropped with nothing failing
        /// until the moment it is asked for at runtime. The single audited
        /// exception is asserted by name below rather than waved through.
        /// </para>
        /// </summary>
        [Test]
        public void NoProductionCodeLoadsAModelByResourcesPath()
        {
            var offenders = new List<string>();
            var loads = new Regex(@"Resources\s*\.\s*Load(All)?\s*<[^>]*>\s*\(\s*""([^""]+)""",
                RegexOptions.Compiled);

            foreach (string file in Directory.GetFiles(
                         Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (Match match in loads.Matches(text))
                {
                    string path = match.Groups[2].Value;
                    // The wrapper prefabs are the intended runtime surface, and the
                    // legacy hull is the one audited direct model load.
                    if (path.StartsWith("VisualPrefabs/", StringComparison.Ordinal)) continue;
                    if (!LooksLikeAModelPath(path)) continue;
                    offenders.Add($"{Relative(file)}: Resources.Load(\"{path}\")");
                }

                if (text.Contains("Resources/Visuals") || text.Contains("\"Visuals/"))
                    offenders.Add(Relative(file) + ": names the retired Resources/Visuals tree");
            }

            Assert.IsEmpty(offenders, string.Join("\n  ", offenders));
        }

        /// <summary>
        /// The legacy hull is off every load path and out of the build, and the
        /// file itself still exists.
        /// <para>
        /// Three separate claims, because three separate things could undo the
        /// saving. The <c>Resources</c> path must not resolve — that is what makes
        /// the asset droppable at all. No production source may name it — a
        /// <c>Resources.Load</c> string is invisible to the reference graph, so a
        /// forgotten one would be a null load at runtime rather than a build
        /// error. And nothing may reference the GLB, because a single prefab or
        /// scene pointing at it drags all 70.0 MB straight back in.
        /// </para>
        /// <para>
        /// The file is asserted present rather than deleted on purpose: it is the
        /// only copy of that mesh in the repository and its provenance is already
        /// unresolved. Keeping it as source costs the build nothing and keeps the
        /// decision reversible.
        /// </para>
        /// </summary>
        [Test]
        public void TheRetiredLegacyHullIsOutOfTheBuildAndOffEveryLoadPath()
        {
            Assert.IsTrue(File.Exists(Abs(LegacyShipAsset)),
                "the legacy hull was deleted rather than moved to the source tree");
            Assert.IsNull(Resources.Load<GameObject>("Ship/PlayerShip"),
                "the legacy hull still resolves through Resources, so it still ships");

            // String literals only, not prose: the constant that replaced this
            // load documents what it replaced, and a comment naming a retired path
            // is the record of the decision rather than a call-site.
            var literals = new Regex(@"""(Ship/PlayerShip|Assets/Resources/Ship[^""]*)""", RegexOptions.Compiled);
            var namers = new List<string>();
            foreach (string file in Directory.GetFiles(
                         Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories))
                if (literals.IsMatch(File.ReadAllText(file)))
                    namers.Add(Relative(file));

            Assert.IsEmpty(namers,
                "production code still names the retired legacy hull:\n  "
                + string.Join("\n  ", namers));

            // The whole build surface, not just the wrapper prefabs: a player
            // contains everything under a Resources folder plus the transitive
            // closure of the scenes in build settings, so those two root sets are
            // the complete answer to "does this asset ship".
            var roots = new List<string>();
            foreach (string file in Directory.GetFiles(
                         Path.Combine(Application.dataPath, "Resources"), "*", SearchOption.AllDirectories))
                if (!file.EndsWith(".meta", StringComparison.Ordinal)) roots.Add(Relative(file));
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled) roots.Add(scene.path);

            Assert.Greater(roots.Count, 200, "the build-surface walk found almost nothing");
            string[] dependents = AssetDatabase.GetDependencies(roots.ToArray(), true)
                .Where(path => path == LegacyShipAsset)
                .ToArray();
            Assert.IsEmpty(dependents,
                "something in the build surface — a Resources asset or a scene in build settings — "
                + "still depends on the legacy hull, so all 70.0 MB of it still ships");
        }

        /// <summary>
        /// The fail-safe hull is bound to a prefab that already ships, and to the
        /// same one the catalog would hand back for a fresh Mk I.
        /// <para>
        /// <c>PlayerShipVisualController.FallbackHullResourcePath</c> is a literal
        /// string deliberately not resolved through
        /// <see cref="NebulaVisualCatalog"/> — see the constant's own remarks for
        /// why a fail-safe must not ask the failing subsystem for the answer. The
        /// cost of that independence is a string that can drift, and this is the
        /// test that stops it: if the catalog ever re-paths its player ships, the
        /// literal has to move with them or the fail-safe silently loads nothing.
        /// </para>
        /// <para>
        /// It also asserts the prefab is one the build already contains for other
        /// reasons, which is the whole basis of the claim that the fail-safe is now
        /// free. A path to some private fallback-only asset would satisfy every
        /// other test here and quietly cost its own megabytes.
        /// </para>
        /// </summary>
        [Test]
        public void Vs018FallbackBindingIsThePrefabTheCatalogWouldPick()
        {
            Assert.IsTrue(NebulaVisualCatalog.TryGetPlayerShip(
                    1, 0, out NebulaVisualDefinition freshMkI));
            Assert.AreEqual(freshMkI.ResourcePath, PlayerShipVisualController.FallbackHullResourcePath,
                "the fail-safe hull no longer names the prefab the catalog uses for a fresh Mk I");

            GameObject prefab = Resources.Load<GameObject>(
                PlayerShipVisualController.FallbackHullResourcePath);
            Assert.IsNotNull(prefab, "the fail-safe hull does not load");
            Assert.Greater(prefab.GetComponentsInChildren<Renderer>(true).Length, 0,
                "the fail-safe hull has no renderers, so the fail-safe draws nothing");
            Assert.IsFalse(prefab.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Any(material => material == null),
                "the fail-safe hull has a missing material");
        }

        /// <summary>
        /// The fail-safe path does not load a model by <c>Resources</c> path in the
        /// sense that matters: what it names is a wrapper prefab under
        /// <c>VisualPrefabs/</c>, not a raw GLB.
        /// <para>
        /// Separate from the audit above because that one walks source text and
        /// this one reads the constant the code actually uses. A string that passes
        /// a grep and points at a GLB would be the exact regression this whole pass
        /// exists to undo.
        /// </para>
        /// </summary>
        [Test]
        public void Vs018TheFallbackNamesAWrapperPrefabAndNotARawModel()
        {
            string path = PlayerShipVisualController.FallbackHullResourcePath;
            Assert.IsTrue(path.StartsWith("VisualPrefabs/", StringComparison.Ordinal),
                "the fail-safe hull is loaded from outside the wrapper prefab tree: " + path);
            Assert.IsFalse(LooksLikeAModelPath(path), "the fail-safe hull names a raw model");

            string resolved = AssetDatabase.GetAssetPath(Resources.Load<GameObject>(path));
            Assert.IsTrue(resolved.EndsWith(".prefab", StringComparison.Ordinal),
                "the fail-safe hull resolves to " + resolved + " rather than to a prefab");
        }

        /// <summary>
        /// The editor rebuilder is the only place that names source paths, and it
        /// names the current ones. A stale path here fails at rebuild time with
        /// "Cannot load corrected model", which is recoverable — but only if
        /// somebody runs the rebuild, and nobody runs it until a model changes.
        /// </summary>
        [Test]
        public void TheRebuilderNamesTheCurrentSourceTree()
        {
            string rebuilder = File.ReadAllText(Path.Combine(
                Application.dataPath, "Editor", "Nebula", "NebulaVisualPrefabRebuilder.cs"));

            Assert.IsFalse(rebuilder.Contains("Assets/Resources/Visuals"),
                "the rebuilder still points at the retired Resources/Visuals tree");
            Assert.IsTrue(rebuilder.Contains("\"Assets/VisualSource\""),
                "the rebuilder no longer declares the source root");
            Assert.IsTrue(rebuilder.Contains("Assets/Resources/VisualPrefabs/"),
                "the rebuilder must keep writing wrapper prefabs to their runtime paths");
        }

        /// <summary>
        /// One player-ship hull at a time, always.
        /// <para>
        /// Two hulls on the same transform is not a degraded picture, it is
        /// z-fighting across every surface plus a doubled silhouette — worse than
        /// either hull alone and far harder to diagnose, because both are correct
        /// models correctly placed. The guard lives in
        /// <c>PlayerShipVisualController.CreateFallbackHullVisual</c> rather than
        /// in the factory's control flow on purpose: control flow is what changes
        /// when somebody adds a retry or reorders startup, and an invariant that
        /// depends on call order is not an invariant.
        /// </para>
        /// <para>
        /// Both directions are exercised. The fail-safe must refuse while the
        /// catalog path owns the picture, and it must be idempotent — a second
        /// call has to hand back the instance that exists rather than mount a
        /// second copy of a twenty-six-renderer prefab.
        /// </para>
        /// </summary>
        [Test]
        public void Vs018OnlyOnePlayerShipHullIsEverMounted()
        {
            var host = new GameObject("player ship duplicate probe");
            try
            {
                var controller = host.AddComponent<PlayerShipVisualController>();
                Assert.IsTrue(controller.CreateInitialVisual(),
                    "the catalog path did not produce a hull, so this test proves nothing");
                Assert.IsNotNull(controller.CurrentVisual);

                Assert.IsNull(controller.CreateFallbackHullVisual(),
                    "the fail-safe mounted a second hull on top of the catalog visual");
                Assert.IsNull(controller.CurrentFallback,
                    "the fail-safe recorded an instance it should never have created");
                Assert.AreEqual(1, MountedHulls(host),
                    "more than one player-ship hull is mounted under the ship root");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        /// <summary>The fail-safe, on its own, mounts exactly once however often it is asked.</summary>
        [Test]
        public void Vs018TheFallbackHullIsIdempotent()
        {
            var host = new GameObject("fail-safe idempotence probe");
            try
            {
                var controller = host.AddComponent<PlayerShipVisualController>();
                GameObject first = controller.CreateFallbackHullVisual();
                Assert.IsNotNull(first, "the fail-safe hull did not mount");
                Assert.AreSame(first, controller.CreateFallbackHullVisual(),
                    "a second call mounted a second hull instead of returning the first");
                Assert.AreSame(first, controller.CurrentFallback);
                Assert.AreEqual(1, MountedHulls(host));

                Assert.Greater(first.GetComponentsInChildren<Renderer>(true).Length, 0,
                    "the fail-safe hull drew nothing");
                Assert.IsFalse(first.GetComponentsInChildren<Collider>(true).Any(c => c.enabled),
                    "the fail-safe hull left a collider enabled, so a visual-only body joined physics");
                Assert.IsFalse(first.GetComponentsInChildren<LODGroup>(true)
                        .Any(group => group.fadeMode != LODFadeMode.None),
                    "the fail-safe hull kept cross-fading, which draws two LOD meshes at once");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        /// <summary>
        /// The fail-safe is presentation only: it adds no collider, no
        /// <c>Rigidbody</c> and no gameplay component to the ship root.
        /// <para>
        /// This is the claim that lets the fail-safe be reached at any point in
        /// startup without a second thought. If mounting it could change the
        /// gameplay envelope, then which hull loaded would change how the ship
        /// collides — and the fail-safe fires precisely in the situations nobody
        /// tested.
        /// </para>
        /// </summary>
        [Test]
        public void Vs018TheFallbackHullChangesNothingOnTheGameplayRoot()
        {
            var host = new GameObject("fail-safe envelope probe");
            try
            {
                var controller = host.AddComponent<PlayerShipVisualController>();
                Component[] before = host.GetComponents<Component>();

                Assert.IsNotNull(controller.CreateFallbackHullVisual());

                CollectionAssert.AreEqual(before, host.GetComponents<Component>(),
                    "the fail-safe added or removed a component on the ship root");
                Assert.IsNull(host.GetComponent<Rigidbody>(), "the fail-safe added a Rigidbody");
                Assert.IsNull(host.GetComponent<Collider>(), "the fail-safe added a collider");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        /// <summary>
        /// Hull instances mounted directly under a ship root — the catalog visual
        /// and the fail-safe both land there, so counting children with renderers
        /// counts hulls without needing to know which path produced them.
        /// </summary>
        private static int MountedHulls(GameObject shipRoot)
        {
            int count = 0;
            foreach (Transform child in shipRoot.transform)
                if (child.GetComponentsInChildren<Renderer>(true).Length > 0) count++;
            return count;
        }

        // ---- generated-texture manifest --------------------------------------

        /// <summary>
        /// The optimizer's source roots are folders that exist, and none of them is
        /// under <c>Resources/</c>.
        /// <para>
        /// <c>NebulaVisualTextureOptimizer.FindSourceGlbs</c> skips a root that is
        /// not on disk, which is exactly why a dead root survives a rename in
        /// silence: the scan simply covers less and reports success. Two of these
        /// were dead for a whole part — <c>Assets/Resources/Visuals</c> after
        /// Part 3 moved the library out, and <c>Assets/Resources/Ship</c> after
        /// Part 4 moved the legacy hull — and neither failure was visible from any
        /// output the pipeline produced.
        /// </para>
        /// <para>
        /// The <c>Resources/</c> clause is a separate claim: a source root under
        /// <c>Resources/</c> would mean the pipeline's inputs ship as well as its
        /// outputs, which is the condition this entire split exists to remove.
        /// </para>
        /// </summary>
        [Test]
        public void EveryTextureSourceRootExistsAndIsOutsideResources()
        {
            var problems = new List<string>();
            foreach (string root in NebulaVisualTextureManifest.SourceRoots
                         .Concat(NebulaVisualTextureManifest.LodRoots))
            {
                if (!Directory.Exists(Abs(root))) problems.Add(root + ": not on disk");
                if (IsUnderResources("/" + root)) problems.Add(root + ": is a Resources path");
            }

            Assert.IsEmpty(problems, string.Join("\n  ", problems));
            CollectionAssert.AreEqual(
                new[] { SourceRoot }, NebulaVisualTextureManifest.SourceRoots,
                "the source root set changed; a second root has to be an integration decision");
        }

        /// <summary>
        /// The generated manifest describes the disk exactly: every GLB it names is
        /// there, every GLB that is there is named, every asset it claims to have
        /// produced exists, and no record appears twice.
        /// <para>
        /// This is the assertion that makes a dry run and an apply provably the
        /// same walk. Both derive their file set from
        /// <c>FindSourceGlbs</c>, so the only way they could disagree is a root
        /// that resolves differently between them — and with the set pinned above
        /// and the manifest pinned to disk here, there is nowhere left for a ghost
        /// record or a missed file to hide.
        /// </para>
        /// </summary>
        [Test]
        public void TheTextureManifestMatchesDiskExactly()
        {
            string json = File.ReadAllText(Abs(NebulaVisualTextureManifest.ManifestPath));

            var named = new HashSet<string>(
                Regex.Matches(json, @"""glb""\s*:\s*""([^""]+)""")
                    .Cast<Match>().Select(match => match.Groups[1].Value),
                StringComparer.Ordinal);
            var onDisk = new HashSet<string>(
                Directory.GetFiles(Abs(SourceRoot), "*.glb", SearchOption.AllDirectories)
                    .Select(Relative),
                StringComparer.Ordinal);

            CollectionAssert.IsEmpty(named.Except(onDisk).OrderBy(path => path).ToArray(),
                "the manifest names source models that are not on disk — ghost records");
            CollectionAssert.IsEmpty(onDisk.Except(named).OrderBy(path => path).ToArray(),
                "these models are on disk but absent from the manifest, so the scan missed them");

            var produced = Regex.Matches(json, @"""assetPath""\s*:\s*""([^""]+)""")
                .Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
            Assert.IsNotEmpty(produced, "the manifest claims to have produced nothing");

            var missing = produced.Where(path => !File.Exists(Abs(path))).Distinct().ToArray();
            Assert.IsEmpty(missing,
                "the manifest names generated assets that are not on disk:\n  "
                + string.Join("\n  ", missing));

            var duplicated = produced.GroupBy(path => path, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => $"{group.Key} x{group.Count()}")
                .ToArray();
            Assert.IsEmpty(duplicated, "duplicate manifest records:\n  " + string.Join("\n  ", duplicated));

            // And the other direction: an asset under the generated root that the
            // manifest does not account for is a leftover from an earlier run, and
            // it would be rebound to by nothing and audited by nobody.
            var accounted = new HashSet<string>(produced, StringComparer.Ordinal);
            var strays = Directory
                .GetFiles(Abs(NebulaVisualTextureManifest.GeneratedRoot), "*", SearchOption.AllDirectories)
                .Select(Relative)
                .Where(path => !path.EndsWith(".meta", StringComparison.Ordinal))
                .Where(path => !path.EndsWith(".json", StringComparison.Ordinal))
                .Where(path => !accounted.Contains(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.IsEmpty(strays,
                "generated assets nothing in the manifest accounts for:\n  " + string.Join("\n  ", strays));
        }

        // ---- runtime catalog integrity ---------------------------------------

        /// <summary>Two definitions sharing a visualId would make anchor and
        /// presentation lookups ambiguous in a way that only shows up as the wrong
        /// model on screen.</summary>
        [Test]
        public void EveryVisualIdIsUnique()
        {
            var duplicates = NebulaVisualCatalog.All
                .GroupBy(definition => definition.VisualId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => $"{group.Key} x{group.Count()}")
                .ToArray();

            Assert.IsEmpty(duplicates, string.Join(", ", duplicates));
            Assert.AreEqual(127, NebulaVisualCatalog.All.Count,
                "the catalog size changed without an integration decision");
        }

        /// <summary>
        /// Every catalog entry resolves to a prefab that is actually there, still
        /// under <c>Resources/</c>, and still renders something.
        /// </summary>
        [Test]
        public void EveryCatalogEntryLoadsARenderingPrefab()
        {
            var problems = new List<string>();
            foreach (NebulaVisualDefinition definition in NebulaVisualCatalog.All)
            {
                Assert.IsTrue(definition.ResourcePath.StartsWith("VisualPrefabs/", StringComparison.Ordinal),
                    definition.VisualId + " is not a wrapper prefab path");

                GameObject prefab = NebulaVisualResolver.Load(definition.ResourcePath);
                if (prefab == null) { problems.Add(definition.VisualId + ": does not load"); continue; }
                if (prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
                    problems.Add(definition.VisualId + ": has no renderers");
                if (prefab.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Any(material => material == null))
                    problems.Add(definition.VisualId + ": has a missing material");
            }

            Assert.IsEmpty(problems, string.Join("\n  ", problems));
        }

        /// <summary>
        /// Every group the game can spawn has at least one real entry. A catalog
        /// that loses a whole family still passes a uniqueness check and still
        /// passes a load check — it just stops being able to draw asteroids.
        /// </summary>
        [Test]
        public void EveryBasicVisualGroupIsRepresented()
        {
            // Resolved through the typed accessors the game itself calls, not by
            // matching id prefixes: a prefix search would still pass if an accessor
            // stopped finding its own entries, which is the failure that actually
            // stops something spawning.
            var groups = new Dictionary<string, VisualLookup>(StringComparer.Ordinal)
            {
                { "planet", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetPlanet(PlanetArchetype.MetallicRocky, out d) },
                { "black hole", (out NebulaVisualDefinition d) => Fixed(NebulaVisualCatalog.BlackHole, out d) },
                { "asteroid", (out NebulaVisualDefinition d) => Fixed(NebulaVisualCatalog.Asteroid(0), out d) },
                { "foundation", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetFoundation(SurfaceAnchorCategory.Solid, out d) },
                { "structure T1", (out NebulaVisualDefinition d) => Fixed(NebulaVisualCatalog.DrillForTier(1), out d) },
                { "structure T2", (out NebulaVisualDefinition d) => Fixed(NebulaVisualCatalog.DrillForTier(2), out d) },
                { "logistics centre", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetLogisticsCenter(LogisticsCenterTier.T1, out d) },
                { "vehicle", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetVehicle(VehicleCatalog.ScoutId, out d) },
                { "vehicle Mk II", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetVehicle(VehicleCatalog.CollectorMk2Id, out d) },
                { "enemy", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetEnemy(EnemyIds.FighterMk1, out d) },
                { "module T1", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetModule("defense.shield_systems", 1, out d) },
                { "module T2", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetModule("defense.shield_systems", 2, out d) },
                { "weapon", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetWeapon(WeaponFamily.Laser, out d) },
                { "projectile", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetProjectile(ProjectileKind.Plasma, out d) },
                { "enemy projectile", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetProjectile(ProjectileKind.EnemySiege, out d) },
                { "poi", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetPoi(PoiType.Anomaly, out d) },
                { "wreck variant", (out NebulaVisualDefinition d) => Fixed(NebulaVisualCatalog.WreckVariant(3, false), out d) },
                { "item", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetItem("resource.metal", out d) },
                { "state kit", (out NebulaVisualDefinition d) => Fixed(NebulaVisualCatalog.StructureConstruction, out d) },
                { "build kit", (out NebulaVisualDefinition d) => Fixed(NebulaVisualCatalog.BuildCapsule, out d) },
                { "player ship", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetPlayerShip(1, 0, out d) },
                { "player ship Mk II stage 12", (out NebulaVisualDefinition d) => NebulaVisualCatalog.TryGetPlayerShip(2, 12, out d) }
            };

            var missing = new List<string>();
            foreach (KeyValuePair<string, VisualLookup> group in groups)
            {
                if (!group.Value(out NebulaVisualDefinition definition) || definition == null)
                {
                    missing.Add(group.Key + ": no catalog entry");
                    continue;
                }
                if (NebulaVisualResolver.Load(definition.ResourcePath) == null)
                    missing.Add($"{group.Key}: {definition.VisualId} does not load");
            }

            Assert.IsEmpty(missing, string.Join("\n  ", missing));
        }

        // ---- authored structure preservation ---------------------------------

        /// <summary>
        /// Every anchor a catalog entry names is still findable on the prefab it
        /// names it on.
        /// <para>
        /// Anchors are how modules, placement and scaling attach: the resolver
        /// looks them up by exact name in
        /// <c>NebulaVisualResolver.FindAnchor</c>, and a missing one is not an
        /// error — it silently skips the alignment and leaves the visual in the
        /// wrong place. This is the assertion that a rebuild or a material
        /// rebinding did not quietly drop one.
        /// </para>
        /// <para>
        /// Uniqueness is deliberately not asserted. Anchors do repeat across LODs
        /// in the shipped wrappers — <c>CombatWreckageCache</c> carries each of
        /// its seven three times, once per LOD — because
        /// <c>RemoveDuplicateRuntimeAnchors</c> calls <c>DestroyImmediate</c> on
        /// children of a nested prefab instance, which Unity records as a removal
        /// that <c>SaveAsPrefabAsset</c> does not keep. That predates this pass and
        /// is harmless while <c>FindAnchor</c> returns the first match in
        /// hierarchy order, which is LOD0's. It is recorded here rather than
        /// asserted so that fixing it is a decision somebody takes, not a test
        /// somebody has to make green in a hurry.
        /// </para>
        /// </summary>
        [Test]
        public void EveryAnchorNamedByTheCatalogExistsOnItsPrefab()
        {
            var problems = new List<string>();
            int checkedAnchors = 0;

            foreach (NebulaVisualDefinition definition in NebulaVisualCatalog.All)
            {
                GameObject prefab = NebulaVisualResolver.Load(definition.ResourcePath);
                if (prefab == null) continue;

                foreach (string anchor in new[] { definition.ScaleAnchorName, definition.PlacementAnchorName })
                {
                    if (string.IsNullOrEmpty(anchor)) continue;
                    checkedAnchors++;
                    if (NebulaVisualResolver.FindAnchor(prefab.transform, anchor) == null)
                        problems.Add($"{definition.VisualId}: anchor \"{anchor}\" is gone");
                }
            }

            Assert.Greater(checkedAnchors, 20, "the anchor walk found almost nothing to check");
            Assert.IsEmpty(problems, string.Join("\n  ", problems));
        }

        /// <summary>
        /// The total population of anchor and part nodes across every wrapper is
        /// unchanged.
        /// <para>
        /// The catalog only names the anchors it aligns to; most nodes exist for
        /// module mounting, VFX and tethering and are found by systems that look
        /// them up later. A flat census catches the case the test above cannot:
        /// a rebuild that keeps the named anchors and loses everything else.
        /// </para>
        /// </summary>
        [Test]
        public void TheAnchorNodeCensusIsUnchanged()
        {
            int anchors = 0;
            foreach (string prefabPath in Directory.GetFiles(
                         Abs(PrefabRoot), "*.prefab", SearchOption.AllDirectories))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Relative(prefabPath));
                if (prefab == null) continue;
                anchors += prefab.GetComponentsInChildren<Transform>(true)
                    .Count(node => node.name.StartsWith("anchor.", StringComparison.Ordinal));
            }

            Assert.AreEqual(ExpectedAnchorNodes, anchors,
                "the anchor node population changed; if that was intended, update the constant "
                + "and say which wrappers gained or lost nodes");
        }

        /// <summary>
        /// Collider hints stay disabled in the asset.
        /// <para>
        /// The resolver also disables them at instantiate time, so an enabled hint
        /// would not be visible in play — until something instantiates a wrapper
        /// without going through the resolver, at which point a visual-only sphere
        /// starts taking part in physics.
        /// </para>
        /// </summary>
        [Test]
        public void ColliderHintsAreDisabledInTheAssetItself()
        {
            var problems = new List<string>();
            foreach (string prefabPath in Directory.GetFiles(
                         Abs(PrefabRoot), "*.prefab", SearchOption.AllDirectories))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Relative(prefabPath));
                if (prefab == null) continue;
                foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
                    if (collider.enabled)
                        problems.Add($"{prefab.name}: {collider.GetType().Name} is enabled");
            }

            Assert.IsEmpty(problems, string.Join("\n  ", problems));
        }

        /// <summary>
        /// LOD groups keep their authored transition thresholds and every LOD keeps
        /// a live renderer.
        /// <para>
        /// The thresholds are the part that must survive a material rebinding: a
        /// renderer that drops out of its LOD array does not error, it just stops
        /// being drawn at that distance, and the model appears to vanish as the
        /// camera pulls back. Two shapes exist in the project and both are
        /// authored, not accidental — the 59 three-LOD wrappers the rebuilder
        /// creates at 0.55/0.18/0.035, and 11 single-LOD groups that come from
        /// inside the GLBs themselves at 0.02.
        /// </para>
        /// <para>
        /// Fade mode is deliberately not asserted here. Twenty of these groups
        /// carry CrossFade in the asset, and normalizing it is the resolver's job
        /// at instantiate time — see <c>NebulaVisualResolver.Sanitize</c>, which
        /// exists because glTFast's imported shaders do not compile Unity's
        /// cross-fade variant. Asserting it on the asset would fail on twenty
        /// perfectly working wrappers; the runtime behaviour is pinned by
        /// <see cref="TheResolverNormalizesFadeModeOnEveryInstance"/> instead.
        /// </para>
        /// </summary>
        [Test]
        public void LodGroupsKeepTheirAuthoredTransitions()
        {
            var problems = new List<string>();
            int threeLod = 0;
            int singleLod = 0;

            foreach (string prefabPath in Directory.GetFiles(
                         Abs(PrefabRoot), "*.prefab", SearchOption.AllDirectories))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Relative(prefabPath));
                if (prefab == null) continue;

                foreach (LODGroup group in prefab.GetComponentsInChildren<LODGroup>(true))
                {
                    LOD[] lods = group.GetLODs();
                    float[] expected = lods.Length == 3
                        ? new[] { 0.55f, 0.18f, 0.035f }
                        : new[] { 0.02f };

                    if (lods.Length != expected.Length)
                    {
                        problems.Add($"{prefab.name}: {lods.Length} LODs is neither shape");
                        continue;
                    }
                    if (lods.Length == 3) threeLod++; else singleLod++;

                    for (int index = 0; index < lods.Length; index++)
                    {
                        if (Mathf.Abs(lods[index].screenRelativeTransitionHeight - expected[index]) > 0.0001f)
                            problems.Add($"{prefab.name}: LOD{index} threshold "
                                         + $"{lods[index].screenRelativeTransitionHeight} != {expected[index]}");
                        if (lods[index].renderers.Length == 0)
                            problems.Add($"{prefab.name}: LOD{index} has no renderers");
                        if (lods[index].renderers.Any(renderer => renderer == null))
                            problems.Add($"{prefab.name}: LOD{index} has a missing renderer");
                    }
                }
            }

            Assert.IsEmpty(problems, string.Join("\n  ", problems));
            Assert.AreEqual(59, threeLod, "the three-LOD wrapper count changed");
            Assert.AreEqual(11, singleLod, "the single-LOD group count changed");
        }

        /// <summary>
        /// Whatever fade mode an asset carries, what reaches the scene hard-switches.
        /// <para>
        /// This is the assertion that actually protects the picture. Cross-fading
        /// against glTFast's shaders draws both LOD meshes at once and reads as a
        /// bright, jagged silhouette; the resolver strips it on every instance, so
        /// the guarantee belongs on an instance rather than on the asset.
        /// </para>
        /// </summary>
        [Test]
        public void TheResolverNormalizesFadeModeOnEveryInstance()
        {
            var host = new GameObject("fade mode probe");
            try
            {
                var problems = new List<string>();
                foreach (NebulaVisualDefinition definition in NebulaVisualCatalog.All)
                {
                    GameObject instance = NebulaVisualResolver.Instantiate(definition, host.transform);
                    if (instance == null) { problems.Add(definition.VisualId + ": did not instantiate"); continue; }

                    foreach (LODGroup group in instance.GetComponentsInChildren<LODGroup>(true))
                    {
                        if (group.fadeMode != LODFadeMode.None)
                            problems.Add($"{definition.VisualId}: fadeMode is {group.fadeMode}");
                        if (group.animateCrossFading)
                            problems.Add(definition.VisualId + ": animateCrossFading is on");
                    }
                    NebulaVisualResolver.DestroyInstance(instance);
                }

                Assert.IsEmpty(problems, string.Join("\n  ", problems));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        // ---- shared-material rebinding ---------------------------------------

        /// <summary>
        /// No renderer draws with a material that lives inside a GLB.
        /// <para>
        /// This is the gate the whole dedup rests on. While a renderer points at
        /// glTFast's imported material, that material holds the GLB's own RGBA32
        /// image, the image is reachable from a prefab under <c>Resources/</c>, and
        /// it ships — 929 occurrences of 62 distinct pictures, up to 90 copies of
        /// one texture. Rebinding is what breaks the chain, and a single renderer
        /// left behind quietly drags its whole texture set back into the player.
        /// </para>
        /// </summary>
        [Test]
        public void NoRendererStillDrawsWithAGlbEmbeddedMaterial()
        {
            var offenders = new List<string>();
            int slots = 0;

            foreach (string prefabPath in WrapperPrefabs())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    slots++;
                    if (material == null)
                    {
                        offenders.Add($"{Path.GetFileName(prefabPath)} / {renderer.name}: null material");
                        continue;
                    }

                    string owner = AssetDatabase.GetAssetPath(material);
                    if (!owner.StartsWith(SharedMaterialRoot, StringComparison.Ordinal))
                        offenders.Add($"{Path.GetFileName(prefabPath)} / {renderer.name}: "
                                      + $"\"{material.name}\" still comes from {owner}");
                }
            }

            Assert.AreEqual(9885, slots, "the material slot population changed");
            Assert.IsEmpty(offenders, string.Join("\n  ", offenders.Take(25)));
        }

        /// <summary>
        /// Every texture reached through a bound material is a shared asset, not a
        /// GLB sub-asset.
        /// <para>
        /// Separate from the test above because they fail for different reasons. A
        /// material can sit in <c>RuntimeVisuals/</c> and still sample an embedded
        /// texture — that is exactly what a half-finished retarget looks like, and
        /// it would leave the duplicate pictures in the build while every
        /// material-level check passed.
        /// </para>
        /// </summary>
        [Test]
        public void EveryTextureReachedFromAPrefabIsADeduplicatedAsset()
        {
            var offenders = new List<string>();
            var distinct = new HashSet<string>(StringComparer.Ordinal);

            foreach (string prefabPath in WrapperPrefabs())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null) continue;

                    foreach (string property in TexturePropertiesOf(material.shader))
                    {
                        Texture texture = material.GetTexture(property);
                        if (texture == null) continue;

                        string owner = AssetDatabase.GetAssetPath(texture);
                        distinct.Add(owner);
                        if (!owner.StartsWith(SharedTextureRoot, StringComparison.Ordinal))
                            offenders.Add($"{Path.GetFileName(prefabPath)} / {material.name}.{property} "
                                          + $"-> {owner}");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "these draw from a texture that is still embedded in a model, so its duplicates "
                + "are still in the build:\n  " + string.Join("\n  ", offenders.Distinct().Take(25)));
            Assert.LessOrEqual(distinct.Count, 62,
                "more distinct textures are reachable than the manifest deduplicated to");
            Assert.Greater(distinct.Count, 40, "the texture walk found almost nothing");
        }

        /// <summary>
        /// Each shared material still says what glTFast said, apart from where it
        /// samples from.
        /// <para>
        /// The generated materials are clones with the five texture slots
        /// retargeted, so every other property must survive: shader, render queue,
        /// keywords, blend state, and the factors that decide how a surface reads.
        /// Sixteen of them carry glTF extensions the optimizer does not interpret —
        /// <c>KHR_materials_transmission</c> and <c>_ior</c> and <c>_volume</c> on
        /// the diamond planet, <c>_unlit</c> on the black hole's rings and event
        /// horizon, <c>_emissive_strength</c> on the plasma emissives. Nothing
        /// parses those extensions on this side either; what this asserts is that
        /// whatever glTFast made of them came across unchanged, which is the only
        /// claim either pass is entitled to make.
        /// </para>
        /// <para>
        /// Textures are excluded from the comparison by construction — they are the
        /// thing that is supposed to differ.
        /// </para>
        /// </summary>
        [Test]
        public void EverySharedMaterialPreservesTheAuthoredPropertiesOfTheOneItReplaced()
        {
            var problems = new List<string>();
            int compared = 0;

            foreach (string prefabPath in WrapperPrefabs())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(renderer) as Renderer;
                    if (source == null) continue;

                    Material[] bound = renderer.sharedMaterials;
                    Material[] authored = source.sharedMaterials;
                    if (bound.Length != authored.Length)
                    {
                        problems.Add($"{Path.GetFileName(prefabPath)} / {renderer.name}: "
                                     + $"{bound.Length} slots but the model has {authored.Length}");
                        continue;
                    }

                    for (int slot = 0; slot < bound.Length; slot++)
                    {
                        if (bound[slot] == null || authored[slot] == null) continue;
                        compared++;
                        problems.AddRange(Differences(
                            $"{Path.GetFileName(prefabPath)} / {renderer.name}[{slot}]",
                            authored[slot], bound[slot]));
                    }
                }
            }

            Assert.Greater(compared, 9000, "the material comparison walk found almost nothing");
            Assert.IsEmpty(problems, string.Join("\n  ", problems.Distinct().Take(30)));
        }

        /// <summary>
        /// The per-instance tint path still finds the properties it writes.
        /// <para>
        /// <c>NebulaVisualResolver.ApplyDiamondPresentation</c> colours the diamond
        /// planet through a <see cref="MaterialPropertyBlock"/> precisely so the
        /// shared imported materials stay untouched — and now they are shared by 90
        /// other things, so writing to the material itself would recolour half the
        /// galaxy. The block is keyed by property name against
        /// <c>material.HasProperty</c>, so a rebind onto a material that dropped
        /// <c>baseColorFactor</c> would silently stop tinting rather than fail.
        /// </para>
        /// </summary>
        [Test]
        public void ThePerInstanceTintPathStillResolvesOnTheSharedMaterials()
        {
            NebulaVisualCatalog.TryGetPlanet(PlanetArchetype.CrystalShell, out NebulaVisualDefinition diamond);
            Assert.IsNotNull(diamond);

            var host = new GameObject("tint probe");
            try
            {
                GameObject instance = NebulaVisualResolver.Instantiate(diamond, host.transform);
                Assert.IsNotNull(instance, "the diamond planet did not instantiate");

                var tinted = new List<string>();
                foreach (string layer in new[]
                         { "diamond-core", "planet-crust", "crystal-field-clear",
                           "crystal-field-ice", "crystal-monoliths" })
                {
                    Transform node = NebulaVisualResolver.FindAnchor(instance.transform, layer);
                    if (node == null) continue;

                    foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true))
                    {
                        var block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block);
                        Assert.IsFalse(block.isEmpty,
                            layer + ": the resolver wrote no property block, so the tint was lost");

                        foreach (Material material in renderer.sharedMaterials)
                        {
                            if (material == null) continue;
                            if (material.HasProperty("baseColorFactor")
                                || material.HasProperty("_BaseColor")
                                || material.HasProperty("_Color"))
                                tinted.Add(layer);
                        }
                    }
                }

                Assert.IsNotEmpty(tinted,
                    "no shared material on the diamond planet exposes a colour property, so the "
                    + "per-instance tint writes into nothing");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        // ---- emoji ------------------------------------------------------------

        /// <summary>
        /// Nothing in the project asks for a TMP sprite.
        /// <para>
        /// This is the gate that has to go red before the emoji removal can be
        /// undone by accident. The removal is safe precisely because no text
        /// anywhere uses a <c>&lt;sprite&gt;</c> tag or a sprite asset API; if a
        /// single one appears, the default sprite asset has to come back and the
        /// licence question with it.
        /// </para>
        /// </summary>
        [Test]
        public void NoTextInTheProjectUsesTmpSprites()
        {
            var offenders = new List<string>();
            string[] roots =
            {
                Path.Combine(Application.dataPath, "Scripts"),
                Path.Combine(Application.dataPath, "Resources")
            };

            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension != ".cs" && extension != ".json" && extension != ".txt"
                        && extension != ".asset" && extension != ".prefab") continue;

                    string text = File.ReadAllText(file);
                    if (text.Contains("<sprite")) offenders.Add(Relative(file) + ": <sprite> tag");
                    if (extension == ".cs" && Regex.IsMatch(text, @"\bspriteAsset\b|\bTMP_SpriteAsset\b"))
                        offenders.Add(Relative(file) + ": sprite asset API");
                }
            }

            Assert.IsEmpty(offenders,
                "TMP sprites are in use again, so clearing the default sprite asset is no "
                + "longer safe:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// The emoji set is out of the build and stays out — checked at both of
        /// the mechanisms that put it there, because either one alone is enough.
        /// </summary>
        [Test]
        public void TheEmojiSetIsNotShipped()
        {
            Assert.IsFalse(Directory.Exists(Abs("Assets/TextMesh Pro/Resources/Sprite Assets")),
                "the emoji sprite asset is back under a Resources folder");
            Assert.IsTrue(File.Exists(Abs("Assets/TextMesh Pro/Sprite Assets/EmojiOne.asset")),
                "the emoji sprite asset was deleted rather than moved");

            string settings = File.ReadAllText(Abs("Assets/TextMesh Pro/Resources/TMP Settings.asset"));
            Assert.IsTrue(settings.Contains("m_defaultSpriteAsset: {fileID: 0}"),
                "TMP Settings names a default sprite asset again, which ships it by reference");
            Assert.IsFalse(settings.Contains("c41005c129ba4d66911b75229fd70b45"),
                "TMP Settings still references the EmojiOne sprite asset GUID");
        }

        // ---- helpers ----------------------------------------------------------

        private static IEnumerable<string> WrapperPrefabs() =>
            Directory.GetFiles(Abs(PrefabRoot), "*.prefab", SearchOption.AllDirectories)
                .Select(Relative)
                .OrderBy(path => path, StringComparer.Ordinal);

        private static IEnumerable<string> TexturePropertiesOf(Shader shader)
        {
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int index = 0; index < count; index++)
                if (ShaderUtil.GetPropertyType(shader, index) == ShaderUtil.ShaderPropertyType.TexEnv)
                    yield return ShaderUtil.GetPropertyName(shader, index);
        }

        /// <summary>
        /// Everything about two materials that must agree, texture bindings aside.
        /// Compared property by property off the shader's own declaration rather
        /// than a hand-written list, so a property nobody thought of is still
        /// covered.
        /// </summary>
        private static IEnumerable<string> Differences(string where, Material authored, Material bound)
        {
            if (authored.shader != bound.shader)
            {
                yield return $"{where}: shader {authored.shader?.name} -> {bound.shader?.name}";
                yield break;
            }
            if (authored.renderQueue != bound.renderQueue)
                yield return $"{where}: renderQueue {authored.renderQueue} -> {bound.renderQueue}";

            foreach (string keyword in authored.shaderKeywords.Except(bound.shaderKeywords, StringComparer.Ordinal))
                yield return $"{where}: lost keyword {keyword}";
            foreach (string keyword in bound.shaderKeywords.Except(authored.shaderKeywords, StringComparer.Ordinal))
                yield return $"{where}: gained keyword {keyword}";

            int count = ShaderUtil.GetPropertyCount(authored.shader);
            for (int index = 0; index < count; index++)
            {
                string name = ShaderUtil.GetPropertyName(authored.shader, index);
                switch (ShaderUtil.GetPropertyType(authored.shader, index))
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        if (authored.GetColor(name) != bound.GetColor(name))
                            yield return $"{where}: {name} {authored.GetColor(name)} -> {bound.GetColor(name)}";
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        if (authored.GetVector(name) != bound.GetVector(name))
                            yield return $"{where}: {name} {authored.GetVector(name)} -> {bound.GetVector(name)}";
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        if (!Mathf.Approximately(authored.GetFloat(name), bound.GetFloat(name)))
                            yield return $"{where}: {name} {authored.GetFloat(name)} -> {bound.GetFloat(name)}";
                        break;
                    // TexEnv is the one thing this pass is allowed to change.
                }
            }
        }

        /// <summary>The shape every catalog accessor already has, so the group
        /// table below can list them uniformly instead of unpacking each one.</summary>
        private delegate bool VisualLookup(out NebulaVisualDefinition definition);

        /// <summary>Adapts a directly-exposed definition to <see cref="VisualLookup"/>.</summary>
        private static bool Fixed(NebulaVisualDefinition value, out NebulaVisualDefinition definition)
        {
            definition = value;
            return value != null;
        }

        private static string Relative(string absolute) =>
            "Assets" + absolute.Substring(Application.dataPath.Length).Replace('\\', '/');

        private static bool IsUnderResources(string relative) =>
            relative.IndexOf("/Resources/", StringComparison.Ordinal) >= 0;

        private static bool LooksLikeAModelPath(string resourcePath) =>
            resourcePath.StartsWith("Visuals/", StringComparison.Ordinal)
            || resourcePath.StartsWith("VisualSource/", StringComparison.Ordinal);

        /// <summary>
        /// Every asset the wrapper prefabs depend on, transitively — the real
        /// answer to "does this still reach the player", as opposed to a text
        /// search for a path that a GUID reference never contains.
        /// </summary>
        private static HashSet<string> PrefabDependencyPaths()
        {
            string[] prefabs = Directory
                .GetFiles(Abs(PrefabRoot), "*.prefab", SearchOption.AllDirectories)
                .Select(Relative)
                .ToArray();

            Assert.Greater(prefabs.Length, 100, "the wrapper prefab walk found almost nothing");
            return new HashSet<string>(
                AssetDatabase.GetDependencies(prefabs, true), StringComparer.Ordinal);
        }
    }
}
