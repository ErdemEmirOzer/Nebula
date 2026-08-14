using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.Poi;
using Nebula.Ship;
using Nebula.Visuals;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// LGT-04 in the real booted scene: structures and POIs light themselves
    /// through emission, and no number of them changes the scene's light count.
    /// </summary>
    public class Lgt04StructurePoiLightingPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        private IEnumerator BootScene()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it -- explicitly, through the same idempotent
            // entry the menu's New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            UnityEngine.Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        [TearDown]
        public void TearDown() => LightingAccessibility.ResetToDefaults();

        private static Light[] ActiveWorldLights()
            => UnityEngine.Object.FindObjectsOfType<Light>()
                .Where(l => l != null && l.gameObject.activeInHierarchy && l.cullingMask == -1)
                .ToArray();

        [UnityTest]
        public IEnumerator TheBootedSceneStillRunsOnOneKeyLightPlusOneHullFill()
        {
            yield return BootScene();

            Light[] worldLights = ActiveWorldLights();
            Assert.AreEqual(2, worldLights.Length,
                "unexpected real-time lights: " + string.Join(", ", worldLights.Select(l => l.gameObject.name)));
        }

        [UnityTest]
        public IEnumerator AHundredStructuresAndSalvageObjectsAddNoRealtimeLight()
        {
            yield return BootScene();

            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            int variantsBefore = MaterialVariantCache.VariantCount;

            var made = new System.Collections.Generic.List<GameObject>();
            try
            {
                Shader standard = Shader.Find("Standard");
                var shared = new Material(standard) { name = "LGT04 Probe Shared" };
                made.Add(new GameObject("LGT04 Probe Root"));

                for (int i = 0; i < 200; i++) // 100 structures + 100 salvage stand-ins
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.SetParent(made[0].transform, false);
                    go.GetComponent<MeshRenderer>().sharedMaterial = shared;
                    go.AddComponent<StructureLightingPresenter>().RebuildRendererCache();
                }

                yield return null;

                Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                    "200 built objects must not add a single real-time light");
                Assert.AreEqual(variantsBefore + 1, MaterialVariantCache.VariantCount,
                    "they all share one source material, so they must share one variant");

                UnityEngine.Object.DestroyImmediate(shared);
            }
            finally
            {
                foreach (GameObject go in made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// The three POI types a real system can materialize. Protected is a
        /// declared-but-unused hook (see <see cref="PoiRisk"/>'s own note), so
        /// the generator never emits one and neither does this fixture.
        /// </summary>
        private static readonly PoiType[] MaterializableTypes =
        {
            PoiType.Anomaly, PoiType.Wreck, PoiType.AbandonedFacility
        };

        /// <summary>
        /// Builds one POI through the real production factory — the same
        /// <see cref="PoiObject.Create"/> the streaming director calls, with a
        /// manifest of the same shape the generator produces and a state
        /// restored exactly as a loaded save restores it. Nothing here adds a
        /// presenter: it arrives because production code attaches it.
        /// <para>
        /// This is what removes the seed dependency. The old test waited for
        /// the start system to happen to contain a POI; this one materializes
        /// known ones, so the result is the same on every seed.
        /// </para>
        /// </summary>
        private static PoiObject CreateProductionPoi(PoiType type, PoiKnowledgeLevel level, int index,
            Transform starTransform, ShipController ship, Nebula.Hangar.ShipHangar hangar)
        {
            var manifest = new PoiManifest(
                poiId: $"lgt041-{type}-{index}",
                systemId: "lgt041-fixture",
                index: index,
                type: type,
                risk: PoiRisk.Safe,
                // Far enough out that the ship is never in range, so the POI's
                // own Update cannot advance the knowledge level mid-assertion.
                systemLocalPosition: new Vector3(250000f + index * 5000f, 0f, 0f),
                size: WreckSize.Medium,
                rewardSeed: 91117UL + (ulong)index,
                visualSeed: 42424UL + (ulong)index);

            var state = new PoiState(manifest.PoiId);
            state.RestoreCore(level, 0f, 0f, 0f, PoiPauseReason.None, null);
            if (type == PoiType.Wreck) state.SetSalvageNodes(WreckSalvageGenerator.Generate(manifest));

            // knowledge/frontierSignal are null in production too whenever the
            // navigation director is absent (Navigation?.Knowledge).
            return PoiObject.Create(manifest, starTransform, state, ship, hangar, null, null);
        }

        private static Transform StartSystemStar()
        {
            var streaming = UnityEngine.Object.FindObjectOfType<Nebula.Sectors.SystemStreamingDirector>();
            Assert.IsNotNull(streaming, "the streaming director must exist after boot");
            Transform star = streaming.TryGetStarTransform(streaming.FullSystemId);
            Assert.IsNotNull(star, "the start system must be Full after boot");
            return star;
        }

        [UnityTest]
        public IEnumerator EveryMaterializablePoiTypeGetsItsPresenterFromTheProductionFactory()
        {
            yield return BootScene();

            Transform star = StartSystemStar();
            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            var hangar = UnityEngine.Object.FindObjectOfType<Nebula.Hangar.ShipHangar>();

            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            var created = new System.Collections.Generic.List<PoiObject>();
            try
            {
                for (int i = 0; i < MaterializableTypes.Length; i++)
                    created.Add(CreateProductionPoi(MaterializableTypes[i], PoiKnowledgeLevel.Identified, i, star, ship, hangar));

                foreach (PoiObject poi in created)
                {
                    var presenter = poi.GetComponent<PoiLightingPresenter>();
                    Assert.IsNotNull(presenter,
                        $"{poi.Manifest.Type} came out of the production factory without a lighting presenter");
                    Assert.Greater(presenter.CachedRendererCount, 0,
                        $"{poi.Manifest.Type} has no renderer for its presenter to light");
                    Assert.AreEqual(0, poi.GetComponentsInChildren<Light>(true).Length,
                        $"{poi.Manifest.Type} must add no real-time light");
                    Assert.AreEqual(poi.Manifest.Type, presenter.Type);
                }

                // Identified opens each type's own profile, so the three must
                // now be visibly different from one another.
                var emissions = created.Select(p => p.GetComponent<PoiLightingPresenter>().AppliedEmission).ToArray();
                Assert.AreEqual(MaterializableTypes.Length, emissions.Distinct().Count(),
                    "an identified POI must read as its own type: " + string.Join(", ", emissions));

                Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                    "materializing three POIs must not add a real-time light");
            }
            finally
            {
                foreach (PoiObject poi in created)
                    if (poi != null) UnityEngine.Object.DestroyImmediate(poi.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator AtSignalAndLocatedRealPoiRenderersLeakNothingAboutTheirType()
        {
            yield return BootScene();

            Transform star = StartSystemStar();
            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            var hangar = UnityEngine.Object.FindObjectOfType<Nebula.Hangar.ShipHangar>();

            foreach (PoiKnowledgeLevel level in new[] { PoiKnowledgeLevel.Signal, PoiKnowledgeLevel.Located })
            {
                var created = new System.Collections.Generic.List<PoiObject>();
                try
                {
                    for (int i = 0; i < MaterializableTypes.Length; i++)
                        created.Add(CreateProductionPoi(MaterializableTypes[i], level, i, star, ship, hangar));

                    var emissions = created
                        .Select(p => p.GetComponent<PoiLightingPresenter>().AppliedEmission)
                        .ToArray();

                    Assert.AreEqual(1, emissions.Distinct().Count(),
                        $"at {level} every POI must be the same abstract contact on the actual renderer, " +
                        "otherwise its type is readable before the game reveals it: " + string.Join(", ", emissions));
                    Assert.Greater(emissions[0].maxColorComponent, 0f, $"at {level} a contact must still be visible");

                    foreach (PoiObject poi in created)
                        Assert.AreEqual(level, poi.GetComponent<PoiLightingPresenter>().CurrentLevel);
                }
                finally
                {
                    foreach (PoiObject poi in created)
                        if (poi != null) UnityEngine.Object.DestroyImmediate(poi.gameObject);
                }

                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator IdentifyingARealPoiOpensItsOwnProfileOnItsOwnRenderer()
        {
            yield return BootScene();

            Transform star = StartSystemStar();
            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            var hangar = UnityEngine.Object.FindObjectOfType<Nebula.Hangar.ShipHangar>();

            PoiObject poi = CreateProductionPoi(PoiType.Anomaly, PoiKnowledgeLevel.Located, 0, star, ship, hangar);
            try
            {
                var presenter = poi.GetComponent<PoiLightingPresenter>();
                Color asContact = presenter.AppliedEmission;

                // The same transition the real identify tick performs.
                poi.State.RestoreCore(PoiKnowledgeLevel.Identified, 0f, 0f, 0f, PoiPauseReason.None, null);
                presenter.Apply();

                Assert.AreNotEqual(asContact, presenter.AppliedEmission,
                    "identification must actually change what the renderer shows");
                Assert.AreEqual(PoiKnowledgeLevel.Identified, presenter.CurrentLevel);

                (Color expectedColor, float expectedStrength) =
                    StructureLightingProfiles.Poi(PoiType.Anomaly, PoiKnowledgeLevel.Identified);
                Color expected = StructureLightingProfiles.Resolve(
                    expectedColor, expectedStrength, presenter.Quality, false);
                Assert.AreEqual(expected, presenter.AppliedEmission,
                    "an identified anomaly must show the anomaly profile, not the contact colour");
            }
            finally
            {
                if (poi != null) UnityEngine.Object.DestroyImmediate(poi.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ReMaterializingAPoiNeitherDuplicatesNorOrphansItsPresenter()
        {
            yield return BootScene();

            Transform star = StartSystemStar();
            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            var hangar = UnityEngine.Object.FindObjectOfType<Nebula.Hangar.ShipHangar>();

            int presentersBefore = PoiLightingPresenter.LiveCount;

            PoiObject first = CreateProductionPoi(PoiType.Wreck, PoiKnowledgeLevel.Identified, 0, star, ship, hangar);
            Assert.AreEqual(presentersBefore + 1, PoiLightingPresenter.LiveCount);
            Assert.AreEqual(1, first.GetComponents<PoiLightingPresenter>().Length,
                "one POI, one presenter");

            // Demote: the system is torn down and the POI with it.
            UnityEngine.Object.DestroyImmediate(first.gameObject);
            yield return null;

            Assert.AreEqual(presentersBefore, PoiLightingPresenter.LiveCount,
                "a destroyed POI must leave no orphan presenter behind");

            // Promote again: the same POI id comes back.
            PoiObject second = CreateProductionPoi(PoiType.Wreck, PoiKnowledgeLevel.Identified, 0, star, ship, hangar);
            try
            {
                Assert.AreEqual(presentersBefore + 1, PoiLightingPresenter.LiveCount,
                    "re-materializing must not accumulate presenters");
                Assert.AreEqual(1, second.GetComponents<PoiLightingPresenter>().Length);
                Assert.AreEqual(0, second.GetComponentsInChildren<Light>(true).Length);
            }
            finally
            {
                if (second != null) UnityEngine.Object.DestroyImmediate(second.gameObject);
            }

            yield return null;
            Assert.AreEqual(presentersBefore, PoiLightingPresenter.LiveCount);
        }

        [UnityTest]
        public IEnumerator PoiEmissionFollowsTheKnowledgeGateFromSignalToResolved()
        {
            yield return BootScene();

            // Drive the profile directly across the whole gate: this is the
            // information contract, independent of which POIs this seed spawned.
            foreach (PoiType type in (PoiType[])Enum.GetValues(typeof(PoiType)))
            {
                Color signal = StructureLightingProfiles.Resolve(
                    StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Signal).color,
                    StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Signal).strength,
                    LightingQuality.Medium, false);
                Color identified = StructureLightingProfiles.Resolve(
                    StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Identified).color,
                    StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Identified).strength,
                    LightingQuality.Medium, false);
                Color resolved = StructureLightingProfiles.Resolve(
                    StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Resolved).color,
                    StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Resolved).strength,
                    LightingQuality.Medium, false);

                Assert.Less(signal.maxColorComponent, identified.maxColorComponent, $"{type}");
                Assert.Less(resolved.maxColorComponent, identified.maxColorComponent, $"{type}");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator StructurePresentersLightThroughPropertyBlocksNotClonedMaterials()
        {
            yield return BootScene();

            Shader standard = Shader.Find("Standard");
            var shared = new Material(standard) { name = "LGT04 Structure Probe" };
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = shared;

                go.AddComponent<StructureLightingPresenter>().RebuildRendererCache();
                yield return null;

                Assert.IsTrue(renderer.sharedMaterial.IsKeywordEnabled("_EMISSION"),
                    "the renderer must draw with an emission-capable material");
                Assert.IsFalse(shared.IsKeywordEnabled("_EMISSION"),
                    "the shared source material must not be mutated");
                Assert.AreEqual(0, go.GetComponentsInChildren<Light>(true).Length);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(shared);
            }
        }

        [UnityTest]
        public IEnumerator QualityChangesReachStructuresAndPoisWithoutGrowingTheScene()
        {
            yield return BootScene();

            var director = UnityEngine.Object.FindObjectOfType<NebulaLightingDirector>();
            Assert.IsNotNull(director);

            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            int renderersBefore = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
            int variantsBefore = MaterialVariantCache.VariantCount;
            int structurePresenters = StructureLightingPresenter.LiveCount;
            int poiPresenters = PoiLightingPresenter.LiveCount;

            foreach (LightingQuality quality in new[]
                     { LightingQuality.Low, LightingQuality.High, LightingQuality.Medium })
            {
                director.SetQuality(quality);
                yield return null;
            }

            Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length);
            Assert.AreEqual(renderersBefore, UnityEngine.Object.FindObjectsOfType<Renderer>().Length);
            Assert.AreEqual(variantsBefore, MaterialVariantCache.VariantCount,
                "a quality switch must not create a material variant");
            Assert.AreEqual(structurePresenters, StructureLightingPresenter.LiveCount,
                "a quality switch must not duplicate a structure presenter");
            Assert.AreEqual(poiPresenters, PoiLightingPresenter.LiveCount,
                "a quality switch must not duplicate a POI presenter");
        }

        [UnityTest]
        public IEnumerator RestartLeavesNoPresenterOrVariantPileUp()
        {
            yield return BootScene();

            var bootstrap = UnityEngine.Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);

            int poisBefore = PoiLightingPresenter.LiveCount;

            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            Assert.AreEqual(2, ActiveWorldLights().Length,
                "still one key light plus one hull fill after a restart");
            Assert.LessOrEqual(PoiLightingPresenter.LiveCount, Mathf.Max(1, poisBefore) * 2,
                "the previous session's POI presenters must be disposed with it, not accumulated");
        }
    }
}
