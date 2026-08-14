using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Whether <c>NebulaUiPrefabBuilder</c> would rewrite a prefab that has not
    /// changed — asked without writing one.
    /// <para>
    /// <b>Why this fixture cannot hang.</b> The obvious way to test idempotency is
    /// to run the builder twice and diff the files, and that is what the coverage
    /// fixture used to do from <c>OneTimeSetUp</c>. It passed standalone and
    /// livelocked the full EditMode suite: <c>BuildAll</c> brackets its writes in
    /// <c>StartAssetEditing</c>/<c>StopAssetEditing</c>, <c>StopAssetEditing</c>
    /// ends in an import pass over the project, and run from a fixture that pass
    /// happens inside the test runner's own update loop — eight file-hasher threads
    /// pinned, resident set past four gigabytes, no results file after forty-nine
    /// minutes.
    /// </para>
    /// <para>
    /// So this fixture never calls <c>BuildAll</c>. It calls <c>PlanAll</c>, which
    /// runs the same construction and the same fingerprint comparison against the
    /// same assets and returns what a build <em>would</em> write. Every operation in
    /// it is bounded: forty-two hierarchies built in memory and destroyed again,
    /// forty-two already-imported assets loaded, and string comparisons. There is
    /// no write, no <c>AssetDatabase.Refresh</c>, no editing bracket, no script
    /// generation and therefore no domain reload, and no <c>while</c> that is not
    /// bounded by the enum. Nothing it does can be slower on a full suite than it
    /// is standalone, because nothing it does depends on the state of the asset
    /// database beyond reading forty-two entries out of it.
    /// </para>
    /// <para>
    /// <b>Why a plan is real evidence.</b> The prefabs on disk were produced by
    /// <c>SaveAsPrefabAsset</c> from this same recipe. If the recipe rebuilt in
    /// memory fingerprints identically to the asset loaded back off disk, then the
    /// round trip through serialization preserved everything the fingerprint
    /// measures — which is exactly the condition under which <c>BuildOne</c> takes
    /// its skip branch. An empty plan and a real second run are the same claim; only
    /// one of them can freeze a suite.
    /// </para>
    /// <para>
    /// It also leaves the working tree clean, because it has no code path that
    /// writes a file.
    /// </para>
    /// </summary>
    [Category("PrefabIdempotency")]
    public class UiPrefabIdempotencyTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void Reset() => NebulaUiPrefabCatalog.Clear();

        [TearDown]
        public void Cleanup()
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null) UnityEngine.Object.DestroyImmediate(spawned[i]);
            spawned.Clear();
            NebulaUiPrefabCatalog.Clear();
        }

        private static string AssetPath(NebulaUiPrefab id) =>
            "Assets/Resources/" + NebulaUiPrefabCatalog.PathOf(id) + ".prefab";

        // =====================================================================
        // REACHING THE BUILDER
        // =====================================================================

        private static Type Builder()
        {
            Type builder = UiFinalPrefabCoverageTests.BuilderType();
            Assert.IsNotNull(builder,
                "Nebula.Editor.NebulaUiPrefabBuilder is not compiled into the editor domain");
            return builder;
        }

        private static MethodInfo Method(Type builder, string name)
        {
            MethodInfo method = builder.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method,
                $"{name} is part of the builder's test-facing surface; it must stay public static");
            return method;
        }

        private static object Invoke(MethodInfo method, params object[] arguments)
        {
            try { return method.Invoke(null, arguments); }
            catch (TargetInvocationException wrapped) { throw wrapped.InnerException ?? wrapped; }
        }

        /// <summary>
        /// The builder's own read-only answer to "what would a rebuild change?".
        /// This is the only builder entry point this fixture is allowed to call:
        /// <c>BuildAll</c> writes, and writing is what froze the suite.
        /// </summary>
        private static IReadOnlyList<string> Plan()
        {
            Type builder = Builder();

            // BuildAll is looked up and deliberately not invoked. It is still the
            // -executeMethod entry point and the menu item, so it has to stay
            // reachable; this fixture just is not the thing that may call it.
            Assert.IsNotNull(builder.GetMethod("BuildAll",
                    BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null),
                "BuildAll is the -executeMethod entry point; it must stay public static");

            return (IReadOnlyList<string>)Invoke(Method(builder, "PlanAll"));
        }

        // =====================================================================
        // FINGERPRINTS OF THE COMMITTED ASSETS
        // =====================================================================

        /// <summary>Identity, dependency hash and bytes, for one prefab.</summary>
        private readonly struct AssetStamp : IEquatable<AssetStamp>
        {
            public readonly string Guid;
            public readonly string DependencyHash;
            public readonly string FileHash;

            public AssetStamp(string guid, string dependencyHash, string fileHash)
            {
                Guid = guid;
                DependencyHash = dependencyHash;
                FileHash = fileHash;
            }

            public bool Equals(AssetStamp other) =>
                string.Equals(Guid, other.Guid, StringComparison.Ordinal) &&
                string.Equals(DependencyHash, other.DependencyHash, StringComparison.Ordinal) &&
                string.Equals(FileHash, other.FileHash, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is AssetStamp other && Equals(other);

            public override int GetHashCode() => Guid == null ? 0 : Guid.GetHashCode();

            public string Describe(AssetStamp other)
            {
                var parts = new List<string>();
                if (!string.Equals(Guid, other.Guid, StringComparison.Ordinal))
                    parts.Add($"GUID {Guid} -> {other.Guid}");
                if (!string.Equals(DependencyHash, other.DependencyHash, StringComparison.Ordinal))
                    parts.Add($"dependency hash {DependencyHash} -> {other.DependencyHash}");
                if (!string.Equals(FileHash, other.FileHash, StringComparison.Ordinal))
                    parts.Add($"file hash {FileHash} -> {other.FileHash}");
                return string.Join(", ", parts);
            }
        }

        private static Dictionary<NebulaUiPrefab, AssetStamp> Stamp()
        {
            var stamps = new Dictionary<NebulaUiPrefab, AssetStamp>();
            using (MD5 md5 = MD5.Create())
            {
                foreach (NebulaUiPrefab id in NebulaUiPrefabCatalog.All)
                {
                    string path = AssetPath(id);
                    string bytes = File.Exists(path)
                        ? Convert.ToBase64String(md5.ComputeHash(File.ReadAllBytes(path)))
                        : "<absent>";

                    stamps[id] = new AssetStamp(
                        AssetDatabase.AssetPathToGUID(path),
                        AssetDatabase.GetAssetDependencyHash(path).ToString(),
                        bytes);
                }
            }
            return stamps;
        }

        // =====================================================================
        // THE CLAIM
        // =====================================================================

        [Test]
        public void ARebuildWouldWriteNothing_AndAskingDoesNotTouchAnAsset()
        {
            Dictionary<NebulaUiPrefab, AssetStamp> before = Stamp();

            var absent = new List<string>();
            foreach (KeyValuePair<NebulaUiPrefab, AssetStamp> entry in before)
            {
                if (entry.Value.FileHash == "<absent>") absent.Add(AssetPath(entry.Key));
                else if (string.IsNullOrEmpty(entry.Value.Guid)) absent.Add(AssetPath(entry.Key) + " (not imported)");
            }
            Assert.IsEmpty(absent,
                "These prefabs are committed assets, not build output. Run " +
                "Nebula/UI/Rebuild Interface Prefabs from the editor menu and commit the result:\n  " +
                string.Join("\n  ", absent));

            IReadOnlyList<string> plan = Plan();

            Assert.IsEmpty(plan,
                "A rebuild would rewrite prefabs whose content has not changed. Every rewrite is a " +
                "re-import and a chance for a reference to move, so this is a defect in the recipe or " +
                "in the committed asset, not a formality:\n  " + string.Join("\n  ", plan));

            Dictionary<NebulaUiPrefab, AssetStamp> after = Stamp();

            var moved = new List<string>();
            foreach (KeyValuePair<NebulaUiPrefab, AssetStamp> entry in before)
            {
                AssetStamp now = after[entry.Key];
                if (!entry.Value.Equals(now))
                    moved.Add($"{entry.Key}: {entry.Value.Describe(now)}");
            }

            Assert.IsEmpty(moved,
                "Asking what a rebuild would do changed an asset. The question is supposed to be " +
                "read-only:\n  " + string.Join("\n  ", moved));
        }

        /// <summary>
        /// The plan is only worth reading if it can come back non-empty. A fixture
        /// in this project has already shipped three tests that asserted nothing, so
        /// the mechanism gets proven rather than trusted: the fingerprint the plan
        /// is built on has to notice a single changed value.
        /// </summary>
        [Test]
        public void TheFingerprintNoticesAChange_SoAnEmptyPlanMeansSomething()
        {
            MethodInfo fingerprint = Method(Builder(), "Fingerprint");

            GameObject asset = NebulaUiPrefabCatalog.Resolve(NebulaUiPrefab.ResourceRow);
            Assert.IsNotNull(asset, "the committed ResourceRow prefab is the input to this test");

            // A plain copy, renamed back: the fingerprint starts from the root's
            // name, and Instantiate appends "(Clone)".
            GameObject copy = UnityEngine.Object.Instantiate(asset);
            spawned.Add(copy);
            copy.name = asset.name;

            var unchanged = (string)Invoke(fingerprint, copy);
            Assert.AreEqual((string)Invoke(fingerprint, asset), unchanged,
                "an untouched copy of the asset must fingerprint identically, or the comparison the " +
                "builder skips on is measuring the instance rather than the content");

            RectTransform name = NebulaUiPrefabCatalog.Slot((RectTransform)copy.transform, "Name");
            Assert.IsNotNull(name, "ResourceRow declares a 'Name' slot");
            var label = name.GetComponent<TextMeshProUGUI>();
            Assert.IsNotNull(label, "the 'Name' slot carries the label this test mutates");

            label.color = label.color == Color.red ? Color.green : Color.red;

            var mutated = (string)Invoke(fingerprint, copy);
            Assert.AreNotEqual(unchanged, mutated,
                "the fingerprint did not notice a changed colour, so an empty plan would prove nothing");
        }

        /// <summary>
        /// The plan and the build have to agree about which prefabs exist at all.
        /// If the enum grows a member with no recipe the builder throws, and this is
        /// where that surfaces — during a read-only plan, rather than the first time
        /// someone opens the menu item.
        /// </summary>
        [Test]
        public void EveryCatalogEntry_HasABuildRecipe()
        {
            // PlanAll builds every entry in NebulaUiPrefabCatalog.All and throws
            // InvalidOperationException on one with no recipe, so reaching this
            // assertion at all is the evidence.
            Assert.DoesNotThrow(() => Plan(),
                "an entry was added to NebulaUiPrefab without a case in the builder's Build switch");
        }
    }
}
