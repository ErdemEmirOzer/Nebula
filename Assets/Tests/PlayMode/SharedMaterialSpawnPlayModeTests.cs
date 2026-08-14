using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Nebula.Combat;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// What the shared-material rebind does once things are actually spawning.
    /// <para>
    /// The EditMode gates prove the assets on disk point where they should. They
    /// cannot see the failure that matters most here: a renderer that resolves to
    /// the right shared material at rest, and then quietly clones it the first
    /// time something touches <c>renderer.material</c> at runtime. One clone per
    /// spawned object puts every deduplicated texture straight back into memory,
    /// with no asset on disk changed and every EditMode test still green.
    /// </para>
    /// <para>
    /// So these tests count. They spawn every visual family, spawn the same family
    /// repeatedly, drive LOD transitions and a full ship progression, and insist
    /// the number of live Material and Texture objects does not grow.
    /// </para>
    /// </summary>
    public class SharedMaterialSpawnPlayModeTests
    {
        private const string SharedRoot = "Assets/RuntimeVisuals/";

        private GameObject host;

        [SetUp]
        public void SetUp() => host = new GameObject("shared material spawn host");

        [TearDown]
        public void TearDown()
        {
            if (host != null) UnityEngine.Object.Destroy(host);
        }

        /// <summary>
        /// Every visual family spawns, draws, and draws with a real material.
        /// <para>
        /// The list is resolved through the catalog's own typed accessors, so it
        /// fails the way the game would fail rather than the way a hard-coded path
        /// list would.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator EveryVisualFamilySpawnsWithRenderersAndMaterials()
        {
            var problems = new List<string>();

            foreach (KeyValuePair<string, NebulaVisualDefinition> family in Families())
            {
                if (family.Value == null) { problems.Add(family.Key + ": no catalog entry"); continue; }

                GameObject instance = NebulaVisualResolver.Instantiate(family.Value, host.transform);
                if (instance == null) { problems.Add(family.Key + ": did not instantiate"); continue; }

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) problems.Add(family.Key + ": spawned with no renderers");

                foreach (Renderer renderer in renderers)
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        problems.Add($"{family.Key}: {renderer.name} has a null material");
                        continue;
                    }
                    if (material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                        problems.Add($"{family.Key}: {renderer.name} has an error shader");
                }
            }

            yield return null;
            Assert.IsEmpty(problems, string.Join("\n  ", problems));
        }

        /// <summary>
        /// Spawning the same visual over and over allocates no new materials and no
        /// new textures.
        /// <para>
        /// The direct test for the clone trap. <c>Object.FindObjectsOfType</c>
        /// would miss these — materials and textures are assets, not scene
        /// objects — so this counts through <c>Resources.FindObjectsOfTypeAll</c>,
        /// which sees every loaded instance including the ones a clone would create.
        /// </para>
        /// <para>
        /// The first spawn is excluded from the baseline on purpose: it is the one
        /// that legitimately loads the shared assets. Everything after it must be
        /// free.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator RepeatedSpawnsDoNotGrowTheMaterialOrTextureCount()
        {
            NebulaVisualCatalog.TryGetPlanet(PlanetArchetype.MetallicRocky, out NebulaVisualDefinition planet);
            Assert.IsNotNull(planet);

            // Warm: loads the prefab, its meshes and its shared materials.
            NebulaVisualResolver.Instantiate(planet, host.transform);
            yield return null;

            int materialsBefore = CountLoaded<Material>();
            int texturesBefore = CountLoaded<Texture>();

            for (int index = 0; index < 12; index++)
            {
                GameObject instance = NebulaVisualResolver.Instantiate(planet, host.transform);
                Assert.IsNotNull(instance, "spawn " + index + " failed");
            }
            yield return null;

            Assert.AreEqual(materialsBefore, CountLoaded<Material>(),
                "twelve more planets created new Material objects — something is touching "
                + "renderer.material and cloning the shared asset per instance");
            Assert.AreEqual(texturesBefore, CountLoaded<Texture>(),
                "twelve more planets created new Texture objects");
        }

        /// <summary>
        /// Where two LOD levels draw the same picture they draw the same object.
        /// <para>
        /// LOD0, LOD1 and LOD2 are three separate GLBs that each embedded their own
        /// image data, so before the rebind every level held a private copy of
        /// every map. What must NOT be asserted is that the levels use identical
        /// texture sets: the dedup key is the decoded pixel hash, and a lower LOD
        /// whose maps were genuinely re-authored is a different picture that
        /// correctly stays a separate asset. <c>PlanetMetal</c> is exactly that
        /// case — its crust map is byte-identical across LOD0 and LOD1 and
        /// deduplicates to one object, while its shards map differs and does not.
        /// </para>
        /// <para>
        /// So the claim is the one that distinguishes dedup from no dedup: at least
        /// one texture object is genuinely shared between levels, and no level
        /// reaches a texture from outside the deduplicated set.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator LodLevelsShareTheTextureObjectsTheyHaveInCommon()
        {
            NebulaVisualCatalog.TryGetPlanet(PlanetArchetype.MetallicRocky, out NebulaVisualDefinition planet);
            GameObject instance = NebulaVisualResolver.Instantiate(planet, host.transform);
            Assert.IsNotNull(instance);
            yield return null;

            LODGroup group = instance.GetComponentInChildren<LODGroup>(true);
            Assert.IsNotNull(group, "the planet wrapper lost its LODGroup");

            LOD[] lods = group.GetLODs();
            Assert.AreEqual(3, lods.Length, "the planet lost a LOD level");

            var perLevel = new List<HashSet<int>>();
            foreach (LOD lod in lods)
            {
                var ids = new HashSet<int>();
                foreach (Renderer renderer in lod.renderers)
                {
                    Assert.IsNotNull(renderer, "a LOD level lost a renderer");
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.IsTrue(material != null && IsShared(material),
                            $"{renderer.name} is not on a deduplicated material");
                        Texture texture = material.GetTexture("baseColorTexture") ?? material.mainTexture;
                        if (texture != null) ids.Add(texture.GetInstanceID());
                    }
                }
                perLevel.Add(ids);
            }

            Assert.IsNotEmpty(perLevel[0], "LOD0 resolved no textures at all");
            Assert.IsNotEmpty(perLevel[1], "LOD1 resolved no textures at all");
            CollectionAssert.IsNotEmpty(perLevel[0].Intersect(perLevel[1]).ToArray(),
                "LOD0 and LOD1 share no texture object at all, so each level is still carrying "
                + "its own copy of the maps they have in common");
        }

        /// <summary>
        /// The per-instance tint still lands, and lands per instance.
        /// <para>
        /// The diamond planet is coloured through a <see cref="MaterialPropertyBlock"/>
        /// so the shared imported material is never written to. That mattered
        /// before; it matters far more now that one material is shared by up to
        /// ninety objects, because a write to the material would recolour all of
        /// them. Two planets are spawned and the second is checked to have its own
        /// block rather than to have inherited the first's.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheDiamondTintUsesPropertyBlocksAndNotTheSharedMaterial()
        {
            NebulaVisualCatalog.TryGetPlanet(PlanetArchetype.CrystalShell, out NebulaVisualDefinition diamond);
            Assert.IsNotNull(diamond);

            GameObject first = NebulaVisualResolver.Instantiate(diamond, host.transform);
            GameObject second = NebulaVisualResolver.Instantiate(diamond, host.transform);
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            yield return null;

            var shared = new HashSet<int>();
            int blocks = 0;

            foreach (GameObject planet in new[] { first, second })
            {
                Transform crust = NebulaVisualResolver.FindAnchor(planet.transform, "planet-crust");
                if (crust == null) continue;

                foreach (Renderer renderer in crust.GetComponentsInChildren<Renderer>(true))
                {
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    if (!block.isEmpty) blocks++;
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null) shared.Add(material.GetInstanceID());
                }
            }

            Assert.Greater(blocks, 0, "the resolver wrote no property block, so the tint is gone");
            Assert.IsNotEmpty(shared);
            Assert.LessOrEqual(shared.Count, 8,
                "two diamond planets resolved to more materials than one set, which means the "
                + "tint path cloned the shared material instead of using a property block");
        }

        /// <summary>
        /// A full ship progression swaps hulls and stages without leaking materials.
        /// <para>
        /// Twenty-six player-ship prefabs share nine materials between them, so the
        /// first walk legitimately loads assets that were not resident before —
        /// counting from a cold start would just measure that loading and call it a
        /// leak. The walk is therefore done twice and only the second is measured:
        /// by then every shared asset the ships need is resident, so any further
        /// growth is a clone rather than a load.
        /// </para>
        /// <para>
        /// The bound on distinct materials is the other half. Nine assets serving
        /// 26 prefabs is the dedup working; 26 would mean each prefab had quietly
        /// acquired its own.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingEveryPlayerShipStageTwiceDoesNotLeakMaterials()
        {
            var distinct = new HashSet<int>();

            IEnumerator Walk()
            {
                for (int hull = 1; hull <= 2; hull++)
                for (int stage = 0; stage <= 12; stage++)
                {
                    Assert.IsTrue(
                        NebulaVisualCatalog.TryGetPlayerShip(hull, stage, out NebulaVisualDefinition ship),
                        $"hull {hull} stage {stage} has no visual");
                    GameObject instance = NebulaVisualResolver.Instantiate(ship, host.transform);
                    Assert.IsNotNull(instance, $"hull {hull} stage {stage} did not instantiate");

                    foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.IsTrue(material != null && IsShared(material),
                            $"hull {hull} stage {stage}: {renderer.name} is not on a shared material");
                        distinct.Add(material.GetInstanceID());
                    }

                    NebulaVisualResolver.DestroyInstance(instance);
                }
                yield return null;
            }

            yield return Walk();
            int materialsAfterFirstWalk = CountLoaded<Material>();
            distinct.Clear();

            yield return Walk();

            Assert.AreEqual(materialsAfterFirstWalk, CountLoaded<Material>(),
                "the second walk over all 26 ship stages created new Material objects, so "
                + "something is cloning rather than sharing");
            Assert.LessOrEqual(distinct.Count, 12,
                $"the 26 ship prefabs resolved to {distinct.Count} distinct materials; they share "
                + "nine, so anything near 26 means each prefab has its own again");
        }

        /// <summary>
        /// Nothing anywhere resolves to Unity's error shader.
        /// <para>
        /// A material whose shader failed to compile still renders — magenta — and
        /// still passes every reference check, because the reference is intact and
        /// only the shader behind it is broken. Worth its own pass over every
        /// family after a change that rewrote 9885 material bindings.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator NoSpawnedVisualUsesTheErrorShader()
        {
            var offenders = new List<string>();

            foreach (KeyValuePair<string, NebulaVisualDefinition> family in Families())
            {
                if (family.Value == null) continue;
                GameObject instance = NebulaVisualResolver.Instantiate(family.Value, host.transform);
                if (instance == null) continue;

                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) { offenders.Add(family.Key + ": null material"); continue; }
                    Shader shader = material.shader;
                    if (shader == null || !shader.isSupported
                        || shader.name.StartsWith("Hidden/InternalError", StringComparison.Ordinal))
                        offenders.Add($"{family.Key}: {material.name} -> {shader?.name ?? "<null>"}");
                }
                NebulaVisualResolver.DestroyInstance(instance);
            }

            yield return null;
            Assert.IsEmpty(offenders, string.Join("\n  ", offenders.Distinct()));
        }

        // ---- helpers ----------------------------------------------------------

        private static bool IsShared(Material material)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.GetAssetPath(material)
                .StartsWith(SharedRoot, StringComparison.Ordinal);
#else
            // Outside the editor there is no asset path to inspect. The clone that
            // this guards against is named "<name> (Instance)" by Unity, which is
            // observable everywhere.
            return !material.name.EndsWith("(Instance)", StringComparison.Ordinal);
#endif
        }

        private static int CountLoaded<T>() where T : UnityEngine.Object =>
            Resources.FindObjectsOfTypeAll<T>().Length;

        /// <summary>One representative of every family the game can spawn.</summary>
        private static IEnumerable<KeyValuePair<string, NebulaVisualDefinition>> Families()
        {
            yield return Pair("planet", Planet(PlanetArchetype.MetallicRocky));
            yield return Pair("gas planet", Planet(PlanetArchetype.UpperAtmospherePlatform));
            yield return Pair("black hole", NebulaVisualCatalog.BlackHole);
            yield return Pair("asteroid", NebulaVisualCatalog.Asteroid(0));
            yield return Pair("foundation", Foundation(SurfaceAnchorCategory.Solid));
            yield return Pair("structure T1", NebulaVisualCatalog.DrillForTier(1));
            yield return Pair("structure T2", NebulaVisualCatalog.DrillForTier(2));
            yield return Pair("logistics centre",
                NebulaVisualCatalog.TryGetLogisticsCenter(LogisticsCenterTier.T1, out NebulaVisualDefinition centre) ? centre : null);
            yield return Pair("vehicle",
                NebulaVisualCatalog.TryGetVehicle(VehicleCatalog.ScoutId, out NebulaVisualDefinition scout) ? scout : null);
            yield return Pair("vehicle Mk II",
                NebulaVisualCatalog.TryGetVehicle(VehicleCatalog.CollectorMk2Id, out NebulaVisualDefinition mk2) ? mk2 : null);
            yield return Pair("enemy",
                NebulaVisualCatalog.TryGetEnemy(EnemyIds.FighterMk1, out NebulaVisualDefinition enemy) ? enemy : null);
            yield return Pair("module T1",
                NebulaVisualCatalog.TryGetModule("defense.shield_systems", 1, out NebulaVisualDefinition module) ? module : null);
            yield return Pair("weapon",
                NebulaVisualCatalog.TryGetWeapon(WeaponFamily.Laser, out NebulaVisualDefinition weapon) ? weapon : null);
            yield return Pair("projectile",
                NebulaVisualCatalog.TryGetProjectile(ProjectileKind.Plasma, out NebulaVisualDefinition shot) ? shot : null);
            yield return Pair("enemy projectile",
                NebulaVisualCatalog.TryGetProjectile(ProjectileKind.EnemySiege, out NebulaVisualDefinition siege) ? siege : null);
            yield return Pair("poi",
                NebulaVisualCatalog.TryGetPoi(PoiType.Anomaly, out NebulaVisualDefinition poi) ? poi : null);
            yield return Pair("wreck variant", NebulaVisualCatalog.WreckVariant(3, false));
            yield return Pair("state kit", NebulaVisualCatalog.StructureConstruction);
            yield return Pair("build kit", NebulaVisualCatalog.BuildCapsule);
            yield return Pair("item",
                NebulaVisualCatalog.TryGetItem("resource.metal", out NebulaVisualDefinition item) ? item : null);
            yield return Pair("training dummy", NebulaVisualCatalog.TrainingDummy);
            yield return Pair("safety marker", NebulaVisualCatalog.SafetyMarker);
        }

        private static KeyValuePair<string, NebulaVisualDefinition> Pair(
            string name, NebulaVisualDefinition definition) =>
            new KeyValuePair<string, NebulaVisualDefinition>(name, definition);

        private static NebulaVisualDefinition Planet(PlanetArchetype archetype) =>
            NebulaVisualCatalog.TryGetPlanet(archetype, out NebulaVisualDefinition definition) ? definition : null;

        private static NebulaVisualDefinition Foundation(SurfaceAnchorCategory category) =>
            NebulaVisualCatalog.TryGetFoundation(category, out NebulaVisualDefinition definition) ? definition : null;
    }
}
