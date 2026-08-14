using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Navigation;
using Nebula.Sectors;
using Nebula.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    public class NebulaProducedAssetIntegrationTests
    {
        private static string ProjectRoot =>
            Directory.GetParent(Application.dataPath).FullName;

        [Test]
        public void EveryFinalArtProductionOutput_HasAnExplicitRuntimeOrSourceRole()
        {
            string artRoot = Path.Combine(ProjectRoot, "ArtProduction");

            // ArtProduction/ is the 43MB authoring tree and is not in git. Absent, the
            // walk below throws DirectoryNotFoundException from inside a Where clause
            // and reads like a broken test rather than a missing input. Named skip
            // instead: the three other tests in this fixture still check the runtime
            // copies, so nothing about the shipped assets goes unmeasured.
            if (!Directory.Exists(artRoot))
                Assert.Ignore(
                    "SKIPPED — ArtProduction/ is not present in this checkout (it is untracked, "
                    + "~43MB of authoring sources). This test compares the 41 runtime copies "
                    + "against their approved exports and needs the export tree to do it.");

            string[] finals = Directory.GetFiles(artRoot, "*", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Final{Path.DirectorySeparatorChar}"))
                .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.AreEqual(63, finals.Length, "new final output was added without an integration decision");

            int runtime = 0;
            int sourceOnly = 0;
            foreach (string source in finals)
            {
                string relative = source.Substring(artRoot.Length + 1).Replace('\\', '/');
                string runtimePath = RuntimeDerivative(relative);
                if (runtimePath == null)
                {
                    sourceOnly++;
                    Assert.IsTrue(relative.Contains("/512/"),
                        $"{relative} is neither integrated nor an approved high-resolution source");
                    string lowerRuntime = relative
                        .Replace("/512/", relative.StartsWith("VFX/", StringComparison.Ordinal)
                            ? "/256/"
                            : "/128/");
                    Assert.IsTrue(File.Exists(Path.Combine(artRoot, lowerRuntime)),
                        $"{relative} has no optimized runtime counterpart");
                    continue;
                }

                runtime++;
                string destination = Path.Combine(ProjectRoot, runtimePath);
                Assert.IsTrue(File.Exists(destination), $"missing runtime derivative for {relative}");
                AssertSameBytes(source, destination, relative);
            }

            Assert.AreEqual(41, runtime);
            Assert.AreEqual(22, sourceOnly);
        }

        [Test]
        public void FourStarFamilies_LoadAtApprovedDimensions_AndHaveContinuousUvEdges()
        {
            foreach (StarClass starClass in Enum.GetValues(typeof(StarClass)))
            {
                Texture2D surface = NebulaProducedVisualResolver.StarSurface(starClass);
                Texture2D corona = NebulaProducedVisualResolver.StarCorona(starClass);
                Assert.IsNotNull(surface, starClass + " surface");
                Assert.IsNotNull(corona, starClass + " corona");
                Assert.AreEqual(2048, surface.width);
                Assert.AreEqual(1024, surface.height);
                Assert.AreEqual(1024, corona.width);
                Assert.AreEqual(1024, corona.height);

                Texture2D readable = LoadReadablePng(
                    Path.Combine(ProjectRoot, "Assets/Resources",
                        NebulaProducedVisualCatalog.StarSurface(starClass) + ".png"));
                try
                {
                    double seamMae = 0d;
                    for (int y = 0; y < readable.height; y++)
                    {
                        Color32 left = readable.GetPixel(0, y);
                        Color32 right = readable.GetPixel(readable.width - 1, y);
                        seamMae += Math.Abs(left.r - right.r);
                        seamMae += Math.Abs(left.g - right.g);
                        seamMae += Math.Abs(left.b - right.b);
                    }
                    seamMae /= readable.height * 3d;
                    Assert.Less(seamMae, 55d, $"{starClass} has a visible wrapped UV seam ({seamMae:0.00})");
                }
                finally { UnityEngine.Object.DestroyImmediate(readable); }
            }
        }

        [Test]
        public void EnvironmentStatusAndVfxRuntimeSprites_HaveDimensionsAndRealAlpha()
        {
            var paths = new List<(string path, int size)>();
            foreach (EnvironmentClass environment in Enum.GetValues(typeof(EnvironmentClass)))
                paths.Add((NebulaProducedVisualCatalog.Environment(environment), 128));
            foreach (SystemKnowledgeLevel level in new[]
                     { SystemKnowledgeLevel.Signal, SystemKnowledgeLevel.Located, SystemKnowledgeLevel.Visited })
                paths.Add((NebulaProducedVisualCatalog.Knowledge(level), 128));
            foreach (DominanceState state in new[]
                     { DominanceState.Developing, DominanceState.Controlled, DominanceState.Contested, DominanceState.Lost })
                paths.Add((NebulaProducedVisualCatalog.Dominance(state), 128));
            foreach (SystemLinkState link in Enum.GetValues(typeof(SystemLinkState)))
                paths.Add((NebulaProducedVisualCatalog.Link(link), 128));
            paths.Add((NebulaProducedVisualCatalog.Attention, 128));
            paths.Add((NebulaProducedVisualCatalog.RaidWarning, 128));
            foreach (NebulaCommonVfx vfx in Enum.GetValues(typeof(NebulaCommonVfx)))
                paths.Add((NebulaProducedVisualCatalog.Vfx(vfx), 256));

            Assert.AreEqual(22, paths.Count);
            foreach ((string resourcePath, int size) in paths)
            {
                Sprite sprite = NebulaIconResolver.LoadSprite(resourcePath);
                Assert.IsNotNull(sprite, resourcePath);
                Assert.AreEqual(size, sprite.texture.width, resourcePath);
                Assert.AreEqual(size, sprite.texture.height, resourcePath);

                Texture2D readable = LoadReadablePng(
                    Path.Combine(ProjectRoot, "Assets/Resources", resourcePath + ".png"));
                try
                {
                    Color32[] pixels = readable.GetPixels32();
                    Assert.IsTrue(pixels.Any(pixel => pixel.a == 0),
                        resourcePath + " has no transparent background");
                    Assert.IsTrue(pixels.Any(pixel => pixel.a > 0),
                        resourcePath + " is fully transparent");
                }
                finally { UnityEngine.Object.DestroyImmediate(readable); }
            }
        }

        [Test]
        public void ElevenProducedGlbs_AreMappedToLoadableSanitizedPrefabs()
        {
            var definitions = new List<NebulaVisualDefinition>
            {
                NebulaVisualCatalog.DrillT2,
                NebulaVisualCatalog.SensorRelayResolved
            };
            foreach (int count in new[] { 2, 3, 4 })
            {
                definitions.Add(NebulaVisualCatalog.WreckVariant(count, false));
                definitions.Add(NebulaVisualCatalog.WreckVariant(count, true));
            }
            foreach (string itemId in new[]
                     { ExtractionCatalog.BuildKitId, ModuleCatalog.EnergyNuclearId, ModuleCatalog.CoreNuclearId })
            {
                Assert.IsTrue(NebulaVisualCatalog.TryGetItem(itemId, out NebulaVisualDefinition item));
                definitions.Add(item);
            }

            Assert.AreEqual(11, definitions.Count);
            Assert.AreEqual(11, definitions.Select(x => x.VisualId).Distinct().Count());
            foreach (NebulaVisualDefinition definition in definitions)
            {
                GameObject prefab = NebulaVisualResolver.Load(definition.ResourcePath);
                Assert.IsNotNull(prefab, definition.ResourcePath);
                Assert.Greater(prefab.GetComponentsInChildren<Renderer>(true).Length, 0, definition.VisualId);
                Assert.IsFalse(prefab.GetComponentsInChildren<Collider>(true).Any(x => x.enabled),
                    definition.VisualId + " contains an enabled visual collider");
                Assert.IsFalse(prefab.GetComponentsInChildren<Camera>(true).Any(x => x.enabled));
                Assert.IsFalse(prefab.GetComponentsInChildren<Light>(true).Any(x => x.enabled));
            }
        }

        /// <summary>
        /// Byte equality for two multi-megabyte files, in a loop rather than through
        /// <c>CollectionAssert.AreEqual</c>.
        /// <para>
        /// The constraint engine walks a byte[] through IEnumerator and boxes every
        /// element. The eight star textures alone are 16MB, so the same assertion
        /// that reads as one line costs tens of millions of boxed comparisons and
        /// turns a file check into the slowest test in the EditMode suite. This is
        /// the identical guarantee at a flat memcmp cost, and it says where the two
        /// files first diverge instead of printing an expected/actual byte dump.
        /// </para>
        /// </summary>
        private static void AssertSameBytes(string source, string destination, string label)
        {
            byte[] approved = File.ReadAllBytes(source);
            byte[] shipped = File.ReadAllBytes(destination);
            Assert.AreEqual(approved.Length, shipped.Length,
                $"runtime copy diverged from approved export (length): {label}");
            for (int index = 0; index < approved.Length; index++)
                if (approved[index] != shipped[index])
                    Assert.Fail($"runtime copy diverged from approved export: {label} "
                                + $"(first difference at byte {index})");
        }

        /// <summary>
        /// Where an approved export was integrated into the project, or null if it
        /// was deliberately left as authoring-only source.
        /// <para>
        /// Textures land under <c>Resources/</c> because code path-loads them by
        /// name. GLBs land under <c>Assets/VisualSource/</c> instead: nothing
        /// path-loads a model, they are reached only as the model prefabs the
        /// wrappers in <c>Resources/VisualPrefabs/</c> nest, and a source tree
        /// under <c>Resources/</c> would ship unconditionally forever. The byte
        /// equality this feeds is unaffected by which tree the copy sits in.
        /// </para>
        /// </summary>
        private static string RuntimeDerivative(string relative)
        {
            if (relative.StartsWith("Stars/Final/", StringComparison.Ordinal))
                return "Assets/Resources/VisualTextures/Stars/" + Path.GetFileName(relative);
            if (relative.StartsWith("UI/Environment/Final/128/", StringComparison.Ordinal))
                return "Assets/Resources/VisualIcons/Environment/" + Path.GetFileName(relative);
            if (relative.StartsWith("UI/Status/Final/128/", StringComparison.Ordinal))
                return "Assets/Resources/VisualIcons/Status/" + Path.GetFileName(relative);
            if (relative.StartsWith("VFX/Final/256/", StringComparison.Ordinal))
                return "Assets/Resources/VisualVfx/Common/" + Path.GetFileName(relative);
            if (!relative.StartsWith("3D/Final/", StringComparison.Ordinal)) return null;

            string name = Path.GetFileName(relative);
            if (name == "DrillT2.glb") return "Assets/VisualSource/Structures/Drill/DrillT2.glb";
            if (name == "UniversalPioneerBuildKit.glb") return "Assets/VisualSource/Items/Special/UniversalPioneerBuildKit.glb";
            if (name == "NuclearEnergyCell.glb") return "Assets/VisualSource/Items/Nuclear/NuclearEnergyCell.glb";
            if (name == "NuclearCore.glb") return "Assets/VisualSource/Items/Nuclear/NuclearCore.glb";
            if (name == "SensorRelayResolved.glb") return "Assets/VisualSource/POI/SensorRelay/SensorRelayResolved.glb";
            if (name.StartsWith("Wreck_", StringComparison.Ordinal))
                return "Assets/VisualSource/POI/Wreck/Variants/" + name;
            return null;
        }

        private static Texture2D LoadReadablePng(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false), path);
            return texture;
        }
    }
}
