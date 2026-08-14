using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    public class VisualAssetsPart1Tests
    {
        /// <summary>
        /// The resolver cache is static. Cleared at both ends so this fixture neither
        /// inherits a neighbour's cache nor leaves it one — fixture order is not an
        /// input to any assertion below.
        /// </summary>
        [SetUp]
        public void SetUp() => NebulaVisualResolver.ClearCache();

        [TearDown]
        public void TearDown() => NebulaVisualResolver.ClearCache();

        [Test]
        public void Catalog_ContainsAllApprovedUniqueRuntimeVisuals()
        {
            Assert.AreEqual(127, NebulaVisualCatalog.All.Count);
            Assert.AreEqual(127, NebulaVisualCatalog.All.Select(x => x.VisualId).Distinct().Count());
            Assert.AreEqual(127, NebulaVisualCatalog.All.Select(x => x.ResourcePath).Distinct().Count());
            Assert.IsFalse(NebulaVisualCatalog.All.Any(x => x.VisualId.StartsWith("source.celestial")),
                "source-only donor stars must never enter the runtime catalog");
            Assert.IsFalse(NebulaVisualCatalog.All.Any(x => x.VisualId.Contains(".star.")),
                "unapproved star assets must not be reachable through the runtime visual catalog");
        }

        [Test]
        public void EveryPart1Prefab_Loads_AndIsRenderOnly()
        {
            foreach (NebulaVisualDefinition definition in NebulaVisualCatalog.All)
            {
                GameObject prefab = NebulaVisualResolver.Load(definition.ResourcePath);
                Assert.IsNotNull(prefab, $"{definition.VisualId} failed to load from {definition.ResourcePath}");
                Assert.Greater(prefab.GetComponentsInChildren<Renderer>(true).Length, 0,
                    $"{definition.VisualId} has no renderer");
                Assert.AreEqual(0, prefab.GetComponentsInChildren<Camera>(true).Length,
                    $"{definition.VisualId} contains a camera");
                Assert.AreEqual(0, prefab.GetComponentsInChildren<Light>(true).Length,
                    $"{definition.VisualId} contains a light");
                Assert.IsFalse(prefab.GetComponentsInChildren<Collider>(true).Any(x => x.enabled),
                    $"{definition.VisualId} contains an enabled collider");

                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.IsNotNull(material, $"{definition.VisualId} has a missing material");
                    Assert.IsNotNull(material.shader, $"{definition.VisualId} has a missing shader");
                    Assert.AreNotEqual("Hidden/InternalErrorShader", material.shader.name,
                        $"{definition.VisualId} uses Unity's error shader");
                }
            }
        }

        [Test]
        public void TypedMappings_CoverEveryPart1GameplaySelector()
        {
            foreach (PlanetArchetype archetype in System.Enum.GetValues(typeof(PlanetArchetype)))
                Assert.IsTrue(NebulaVisualCatalog.TryGetPlanet(archetype, out _), $"missing {archetype}");
            foreach (SurfaceAnchorCategory category in System.Enum.GetValues(typeof(SurfaceAnchorCategory)))
                Assert.IsTrue(NebulaVisualCatalog.TryGetFoundation(category, out _), $"missing {category}");

            var expectedItems = new HashSet<string>(ExtractionCatalog.ResourceIds);
            expectedItems.UnionWith(ProcessingCatalog.MaterialIds);
            expectedItems.Add(ProcessingCatalog.BasicPartId);
            Assert.AreEqual(11, expectedItems.Count);
            foreach (string itemId in expectedItems)
                Assert.IsTrue(NebulaVisualCatalog.TryGetItem(itemId, out _), $"missing {itemId}");
            foreach (string producedItemId in new[]
                     { ExtractionCatalog.BuildKitId, ModuleCatalog.EnergyNuclearId, ModuleCatalog.CoreNuclearId })
                Assert.IsTrue(NebulaVisualCatalog.TryGetItem(producedItemId, out _),
                    $"missing produced item {producedItemId}");

            foreach (SurfaceStructureType type in System.Enum.GetValues(typeof(SurfaceStructureType)))
            for (int tier = 1; tier <= 2; tier++)
                Assert.IsTrue(NebulaVisualCatalog.TryGetStructure(type, tier, out _),
                    $"missing {type} T{tier}");
            foreach (SurfaceAnchorCategory category in System.Enum.GetValues(typeof(SurfaceAnchorCategory)))
                Assert.IsTrue(NebulaVisualCatalog.TryGetDrillAdapter(category, out _),
                    $"missing drill adapter {category}");
            foreach (VehicleDef vehicle in VehicleCatalog.All)
                Assert.IsTrue(NebulaVisualCatalog.TryGetVehicle(vehicle.Id, out _),
                    $"missing {vehicle.Id}");
            foreach (EnemyDef enemy in EnemyCatalog.All)
                Assert.IsTrue(NebulaVisualCatalog.TryGetEnemy(enemy.Id, out _),
                    $"missing {enemy.Id}");
            foreach (LogisticsCenterTier tier in System.Enum.GetValues(typeof(LogisticsCenterTier)))
                Assert.IsTrue(NebulaVisualCatalog.TryGetLogisticsCenter(tier, out _),
                    $"missing logistics center {tier}");
        }

        [Test]
        public void AsteroidVisual_UsesOneRendererWhileRetainingThreeAuthoredLods()
        {
            var rock = new GameObject("test asteroid");
            try
            {
                Assert.IsTrue(AsteroidVisualLibrary.Apply(rock, 0, 20f, out Renderer renderer));
                Assert.IsNotNull(renderer);
                Assert.AreEqual(1, rock.GetComponentsInChildren<Renderer>(true).Length,
                    "each asteroid must stay one renderer, not a nested prefab hierarchy");
                var authored = rock.GetComponent<AsteroidAuthoredVisual>();
                Assert.IsNotNull(authored);
                Mesh lod0 = rock.GetComponent<MeshFilter>().sharedMesh;
                authored.SetLod(1);
                Mesh lod1 = rock.GetComponent<MeshFilter>().sharedMesh;
                authored.SetLod(2);
                Mesh lod2 = rock.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreNotSame(lod0, lod1);
                Assert.AreNotSame(lod1, lod2);
            }
            finally { Object.DestroyImmediate(rock); }
        }

        [Test]
        public void TransparentPlanetLayers_UsePortableStandardAlpha()
        {
            AssertLayerUsesPortableAlpha(PlanetArchetype.Biomass, "cloud");
            AssertLayerUsesPortableAlpha(PlanetArchetype.FloatingOceanic, "cloud");
            AssertLayerUsesPortableAlpha(PlanetArchetype.UpperAtmospherePlatform, "ring");
        }

        [Test]
        public void RuntimeVisuals_DisableUnsupportedLodCrossFade()
        {
            Assert.IsTrue(NebulaVisualCatalog.TryGetPlanet(
                PlanetArchetype.Biomass, out NebulaVisualDefinition definition));
            var owner = new GameObject("visual owner");
            try
            {
                GameObject visual = NebulaVisualResolver.Instantiate(definition, owner.transform, 10f);
                Assert.IsNotNull(visual);
                LODGroup[] groups = visual.GetComponentsInChildren<LODGroup>(true);
                Assert.Greater(groups.Length, 0);
                Assert.IsTrue(groups.All(x => x.fadeMode == LODFadeMode.None),
                    "glTF shaders do not support Unity LOD cross-fade; overlapping LODs corrupt silhouettes");
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void DecorativeEnvelopes_DoNotShrinkGameplaySizedCelestialBodies()
        {
            Assert.IsTrue(NebulaVisualCatalog.TryGetPlanet(
                PlanetArchetype.UpperAtmospherePlatform, out NebulaVisualDefinition gas));
            AssertScaleAnchorDiameter(gas, "gas-body", 10f);
            AssertScaleAnchorDiameter(NebulaVisualCatalog.BlackHole, "event-horizon", 10f);
        }

        [Test]
        public void PlanetPresentation_RemovesOnlyPaleVeils_AndKeepsAuthoredDetail()
        {
            AssertPlanetLayerProfile(
                PlanetArchetype.MetallicRocky,
                hidden: new[] { "vapour-halo", "vapour-outer" },
                visible: new[] { "metal-crust", "metal-shards" });
            AssertPlanetLayerProfile(
                PlanetArchetype.Biomass,
                hidden: new[] { "atmosphere" },
                visible: new[] { "terrain", "forest-canopy", "cloud-layer" });
            AssertPlanetLayerProfile(
                PlanetArchetype.UpperAtmospherePlatform,
                hidden: new[] { "atmosphere-haze" },
                visible: new[] { "gas-body", "rings" });
            AssertPlanetLayerProfile(
                PlanetArchetype.FloatingOceanic,
                hidden: new[] { "atmosphere" },
                visible: new[] { "ocean-surface", "cloud-layer" });
            AssertPlanetLayerProfile(
                PlanetArchetype.CrystalShell,
                hidden: new string[0],
                visible: new[] { "diamond-core", "planet-crust", "crystal-field-clear", "crystal-field-ice" });
        }

        [Test]
        public void DiamondPresentation_FillsInterior_AndRemovesWhiteWash()
        {
            Assert.IsTrue(NebulaVisualCatalog.TryGetPlanet(
                PlanetArchetype.CrystalShell, out NebulaVisualDefinition definition));
            var owner = new GameObject(definition.VisualId);
            try
            {
                GameObject visual = NebulaVisualResolver.Instantiate(
                    definition, owner.transform, 10f);
                Assert.IsNotNull(visual);

                Transform core = NebulaVisualResolver.FindAnchor(visual.transform, "diamond-core");
                Assert.IsNotNull(core);
                Assert.IsTrue(core.gameObject.activeSelf, "diamond interior fill must stay active");

                AssertDiamondLayerTint(visual, "diamond-core", 0.60f);
                AssertDiamondLayerTint(visual, "planet-crust", 0.96f);
                AssertDiamondLayerTint(visual, "crystal-field-clear", 0.99f);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        /// <summary>
        /// The legacy hull's bytes, at the path it lives at now.
        /// <para>
        /// Part 4 moved it out of <c>Resources/Ship/</c> — under a
        /// <c>Resources</c> folder it shipped unconditionally, 70.0 MB expanded,
        /// for a picture only reachable once the whole Part 24 prefab family had
        /// failed. The expected hash is deliberately the same string it has always
        /// been: a move that changed a byte would fail here, which is the point.
        /// </para>
        /// </summary>
        [Test]
        public void PlayerShipAsset_IsByteForByteUnchanged()
        {
            string path = Path.Combine(
                Application.dataPath, "VisualSource/PlayerShip/Legacy/PlayerShip.glb");
            using var sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            string actual = string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
            Assert.AreEqual("be56c48db3f1c99fdfde089f6faaa20ef3e5eeaee5e07df9bf239247251e0e9c", actual);
        }

        [Test]
        public void GameplayCompatibilityVersions_AreUnchanged()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }

        private static void AssertLayerUsesPortableAlpha(
            PlanetArchetype archetype, string materialNameFragment)
        {
            Assert.IsTrue(NebulaVisualCatalog.TryGetPlanet(archetype, out NebulaVisualDefinition definition));
            GameObject prefab = NebulaVisualResolver.Load(definition.ResourcePath);
            Assert.IsNotNull(prefab);

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter =>
                {
                    MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                    return renderer != null && renderer.sharedMaterials.Any(material =>
                        material != null && material.name.ToLowerInvariant().Contains(materialNameFragment));
                }).ToArray();

            Assert.Greater(filters.Length, 0, $"{definition.VisualId} has no {materialNameFragment} layer");
            foreach (MeshFilter filter in filters)
            {
                Color[] colors = filter.sharedMesh.colors;
                bool hasVertexAlpha =
                    colors.Length == filter.sharedMesh.vertexCount
                    && colors.Any(color => color.a < 0.99f)
                    && colors.Any(color => color.a > 0.001f);

                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                bool hasBakedTextureAlpha = renderer.sharedMaterials.Any(material =>
                    material != null
                    && material.HasProperty("baseColorTexture")
                    && material.GetTexture("baseColorTexture") != null
                    && material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent);

                Assert.IsTrue(hasVertexAlpha || hasBakedTextureAlpha,
                    $"{filter.name} has neither standard COLOR_0 alpha nor baked transparent BaseColor");
            }
        }

        private static void AssertScaleAnchorDiameter(
            NebulaVisualDefinition definition, string anchorName, float expectedDiameter)
        {
            var owner = new GameObject(definition.VisualId);
            try
            {
                GameObject visual = NebulaVisualResolver.Instantiate(
                    definition, owner.transform, expectedDiameter);
                Assert.IsNotNull(visual);
                Transform anchor = NebulaVisualResolver.FindAnchor(visual.transform, anchorName);
                Assert.IsNotNull(anchor);
                Renderer[] renderers = anchor.GetComponentsInChildren<Renderer>(true);
                Assert.Greater(renderers.Length, 0);
                Bounds bounds = renderers[0].bounds;
                for (int index = 1; index < renderers.Length; index++)
                    bounds.Encapsulate(renderers[index].bounds);
                float diameter = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                Assert.AreEqual(expectedDiameter, diameter, expectedDiameter * 0.01f,
                    $"{definition.VisualId} scaled its decorative envelope instead of {anchorName}");
            }
            finally { Object.DestroyImmediate(owner); }
        }

        private static void AssertPlanetLayerProfile(
            PlanetArchetype archetype, string[] hidden, string[] visible)
        {
            Assert.IsTrue(NebulaVisualCatalog.TryGetPlanet(
                archetype, out NebulaVisualDefinition definition));
            var owner = new GameObject(definition.VisualId);
            try
            {
                GameObject visual = NebulaVisualResolver.Instantiate(
                    definition, owner.transform, 10f);
                Assert.IsNotNull(visual);
                foreach (string layerName in hidden)
                {
                    Transform[] matches = visual.GetComponentsInChildren<Transform>(true)
                        .Where(x => x.name == layerName).ToArray();
                    Assert.Greater(matches.Length, 0, $"missing veil layer {layerName}");
                    Assert.IsTrue(matches.All(x => !x.gameObject.activeSelf),
                        $"{definition.VisualId}/{layerName} veil is still visible");
                }
                foreach (string layerName in visible)
                {
                    Transform[] matches = visual.GetComponentsInChildren<Transform>(true)
                        .Where(x => x.name == layerName).ToArray();
                    Assert.Greater(matches.Length, 0, $"missing authored detail {layerName}");
                    Assert.IsTrue(matches.All(x => x.gameObject.activeSelf),
                        $"{definition.VisualId}/{layerName} authored detail was hidden");
                }
            }
            finally { Object.DestroyImmediate(owner); }
        }

        private static void AssertDiamondLayerTint(
            GameObject visual, string layerName, float maximumLightness)
        {
            Transform layer = NebulaVisualResolver.FindAnchor(visual.transform, layerName);
            Assert.IsNotNull(layer, $"missing diamond layer {layerName}");
            Renderer renderer = layer.GetComponentInChildren<Renderer>(true);
            Assert.IsNotNull(renderer, $"{layerName} has no renderer");

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Material material = renderer.sharedMaterials.First(x => x != null);
            string property = material.HasProperty("baseColorFactor")
                ? "baseColorFactor"
                : material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            Color tint = block.GetColor(property);
            Assert.AreEqual(1f, tint.a, 0.001f, $"{layerName} must remain opaque");
            Assert.Less((tint.r + tint.g + tint.b) / 3f, maximumLightness,
                $"{layerName} still reads as a white veil");
            Assert.Greater(tint.b, tint.r, $"{layerName} is not diamond-blue");
        }
    }
}
