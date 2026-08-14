using System.Collections.Generic;
using System.Linq;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    public class VisualAssetsPart24Tests
    {
        /// <summary>Both resolver caches are static; cleared at each end so fixture
        /// order cannot reach any assertion below.</summary>
        [SetUp]
        public void SetUp() => ClearStaticState();

        [TearDown]
        public void TearDown() => ClearStaticState();

        private static void ClearStaticState()
        {
            NebulaVisualResolver.ClearCache();
            NebulaIconResolver.ClearCache();
        }

        [Test]
        public void PlayerShipCatalog_ContainsExactlyTwentySixRuntimeVariants()
        {
            NebulaVisualDefinition[] ships = NebulaVisualCatalog.All
                .Where(x => x.ResourcePath.StartsWith("VisualPrefabs/PlayerShip/"))
                .ToArray();
            Assert.AreEqual(26, ships.Length);
            Assert.AreEqual(26, ships.Select(x => x.VisualId).Distinct().Count());
            Assert.AreEqual(26, ships.Select(x => x.ResourcePath).Distinct().Count());
            Assert.IsFalse(ships.Any(x => x.ResourcePath.Contains("Master")));
        }

        [Test]
        public void EveryPlayerShipVariant_LoadsRenderOnly_WithUniqueRuntimeAnchors()
        {
            for (int hull = 1; hull <= 2; hull++)
            for (int stage = 0; stage <= 12; stage++)
            {
                Assert.IsTrue(NebulaVisualCatalog.TryGetPlayerShip(hull, stage, out NebulaVisualDefinition definition));
                var owner = new GameObject("ship owner");
                try
                {
                    GameObject instance = NebulaVisualResolver.Instantiate(
                        definition, owner.transform, hull == 1
                            ? PlayerShipVisualController.MkIEnvelope
                            : PlayerShipVisualController.MkIIEnvelope);
                    Assert.IsNotNull(instance, definition.VisualId);
                    Assert.Greater(instance.GetComponentsInChildren<Renderer>(true).Length, 0, definition.VisualId);
                    Assert.IsFalse(instance.GetComponentsInChildren<Collider>(true).Any(x => x.enabled), definition.VisualId);
                    Assert.IsFalse(instance.GetComponentsInChildren<Camera>(true).Any(x => x.enabled), definition.VisualId);
                    Assert.IsFalse(instance.GetComponentsInChildren<Light>(true).Any(x => x.enabled), definition.VisualId);

                    foreach (string anchor in new[]
                             {
                                 "anchor.module.01", "anchor.module.12",
                                 "anchor.hardpoint.01", "anchor.hardpoint.05",
                                 "anchor.thruster.L", "anchor.thruster.R",
                                 "anchor.dock", "anchor.hangar", "anchor.cargo",
                                 "anchor.camera", "anchor.target", "anchor.vfx.damage"
                             })
                    {
                        Assert.AreEqual(1,
                            instance.GetComponentsInChildren<Transform>(true)
                                .Count(x => x.name == anchor),
                            $"{definition.VisualId}/{anchor}");
                    }
                }
                finally { Object.DestroyImmediate(owner); }
            }
        }

        [Test]
        public void PlayerShipLods_UseOnlyTheFourSuppliedChains()
        {
            for (int hull = 1; hull <= 2; hull++)
            for (int stage = 0; stage <= 12; stage++)
            {
                NebulaVisualCatalog.TryGetPlayerShip(hull, stage, out NebulaVisualDefinition definition);
                GameObject prefab = NebulaVisualResolver.Load(definition.ResourcePath);
                Assert.IsNotNull(prefab, definition.VisualId);
                LODGroup group = prefab.GetComponent<LODGroup>();
                if (stage == 0 || stage == 12)
                {
                    Assert.IsNotNull(group, definition.VisualId);
                    Assert.AreEqual(3, group.GetLODs().Length, definition.VisualId);
                    Assert.AreEqual(LODFadeMode.None, group.fadeMode, definition.VisualId);
                }
                else
                {
                    Assert.IsNull(group, $"{definition.VisualId} received an invented LOD.");
                }
            }
        }

        [Test]
        public void CumulativeStage_StopsAtFirstMissingModule_AndNeverInventsOwnership()
        {
            var state = new ModuleProgressionState();
            state.GrantFreeStartingModules();
            Assert.AreEqual(3, PlayerShipVisualController.ResolveCumulativeStage(state));

            state.SetTier("exploration.hangar_bot", 1);
            Assert.AreEqual(3, PlayerShipVisualController.ResolveCumulativeStage(state),
                "stage 6 must not imply Scanner/Engine ownership");

            state.SetTier("exploration.scanner", 1);
            state.SetTier("exploration.engine_navigation", 1);
            Assert.AreEqual(6, PlayerShipVisualController.ResolveCumulativeStage(state));
        }

        [Test]
        public void ResourceAndRarityIcons_LoadAtGameResolution()
        {
            foreach (ResourceFamily family in System.Enum.GetValues(typeof(ResourceFamily)))
                AssertIcon(NebulaIconResolver.Resource(family), family.ToString());
            foreach (Rarity rarity in System.Enum.GetValues(typeof(Rarity)))
                AssertIcon(NebulaIconResolver.Rarity(rarity), rarity.ToString());
        }

        [Test]
        public void DownloadedNuclearIcon_MapsOnlyToRealNuclearEnergyId()
        {
            Sprite energy = NebulaIconResolver.Item(ModuleCatalog.EnergyNuclearId);
            AssertIcon(energy, ModuleCatalog.EnergyNuclearId);
            Assert.IsNull(NebulaIconResolver.Item("resource.nuclear"),
                "the export label must not create a new gameplay resource id");
            Assert.IsNull(NebulaIconResolver.Item(ModuleCatalog.CoreNuclearId),
                "a nuclear-energy glyph must not be misrepresented as a nuclear core");
        }

        [Test]
        public void GameplayCompatibilityVersions_AreUnchangedByPart24()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }

        private static void AssertIcon(Sprite sprite, string label)
        {
            Assert.IsNotNull(sprite, label);
            Assert.IsNotNull(sprite.texture, label);
            Assert.AreEqual(128, sprite.texture.width, label);
            Assert.AreEqual(128, sprite.texture.height, label);
        }
    }
}
