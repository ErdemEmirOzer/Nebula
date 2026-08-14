using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-02 in the real booted scene: a drill built through the real
    /// production path animates, a dense planet surface stays bounded, and
    /// none of it moves a gameplay root.
    /// </summary>
    public class Ani02ConstructionEconomyAnimationPlayModeTests
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
        public void TearDown() => AnimationAccessibility.ResetToDefaults();

        private static NebulaAnimationDirector Director()
        {
            var director = UnityEngine.Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(director, "the animation director must exist after boot");
            return director;
        }

        /// <summary>Builds a drill through the real production factory, on a real planet of the booted system.</summary>
        private static DrillStructure BuildRealDrill()
        {
            var planet = UnityEngine.Object.FindObjectOfType<PlanetIdentity>();
            Assert.IsNotNull(planet, "the Full system must have materialized planets");

            PlanetProfile profile = ExtractionState.GetProfile(planet);
            Assert.IsNotNull(profile, "the planet must have an extraction profile");

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship);

            return DrillStructure.Create(planet, profile, ship.transform.position,
                DrillStructure.BuildSource.Parts, refundParts: 0);
        }

        // ---- the real production path -------------------------------------------

        [UnityTest]
        public IEnumerator ADrillBuiltThroughTheRealFactoryGetsItsAnimationPresenter()
        {
            yield return BootScene();

            DrillStructure drill = BuildRealDrill();
            try
            {
                var presenter = drill.GetComponent<DrillAnimationPresenter>();
                Assert.IsNotNull(presenter,
                    "the production Setup seam must attach the presenter — Create and Restore both go through it");
                Assert.AreEqual(drill.Runtime.Family, presenter.Family);
                Assert.AreEqual(0, drill.GetComponentsInChildren<Light>(true).Length,
                    "animation must add no light");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(drill.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator ANewDrillIsAdoptedByTheDirectorOnItsNextTick()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            int before = director.ParticipantCount;

            DrillStructure drill = BuildRealDrill();
            try
            {
                director.Tick(1f);
                // A drill brings its pose presenter and, since ANI-05.1, the
                // presenter for the mechanisms its model really carries. The
                // claim here is adoption without a global lookup — not a
                // particular number of presenters, which every later part moves.
                Assert.Greater(director.ParticipantCount, before,
                    "a drill built mid-flight must join the director without any global lookup");
                Assert.AreEqual(0, StructurePosePresenter.PendingCount, "and the adoption queue must drain");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(drill.gameObject);
            }

            director.Tick(1f);
            Assert.AreEqual(before, director.ParticipantCount,
                "destroying it must leave no orphan participant");
        }

        [UnityTest]
        public IEnumerator ADrillsLifecycleDrivesItsPoseWithoutMovingItsGameplayRoot()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            DrillStructure drill = BuildRealDrill();
            try
            {
                Vector3 rootBefore = drill.transform.position;
                var presenter = drill.GetComponent<DrillAnimationPresenter>();

                director.Tick(1f);

                Assert.AreEqual(drill.Runtime.State, presenter.CurrentState,
                    "the presenter must read the real runtime state, not invent one");
                Assert.That(presenter.CurrentProgress, Is.InRange(0f, 1f));
                Assert.AreEqual(rootBefore, drill.transform.position,
                    "the drill's own transform is its surface slot and must never be animated");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(drill.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator AnimationChangesNoGameplayValueOnARealDrill()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            DrillStructure drill = BuildRealDrill();
            try
            {
                DrillRuntime runtime = drill.Runtime;
                float productionBefore = runtime.ProductionPerMinute;
                float capacityBefore = runtime.StorageCapacity;
                int tierBefore = runtime.Tier;
                float capsuleBefore = runtime.CapsuleRemaining;

                for (int i = 0; i < 300; i++) director.Tick(1f / 60f);

                Assert.AreEqual(productionBefore, runtime.ProductionPerMinute, 1e-4f,
                    "animation must not touch production rate");
                Assert.AreEqual(capacityBefore, runtime.StorageCapacity, 1e-4f);
                Assert.AreEqual(tierBefore, runtime.Tier);
                Assert.AreEqual(capsuleBefore, runtime.CapsuleRemaining, 1e-4f,
                    "and must not advance the build countdown — the runtime owns that");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(drill.gameObject);
            }
        }

        // ---- composition and teardown ----------------------------------------------

        [UnityTest]
        public IEnumerator TheSessionHasOneAnimationDirector()
        {
            yield return BootScene();

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaAnimationDirector>().Length);

            var bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap?.Context);
            Assert.AreSame(Director(), bootstrap.Context.AnimationDirector);
        }

        [UnityTest]
        public IEnumerator RestartLeavesOneDirectorAndAnEmptyAdoptionQueue()
        {
            yield return BootScene();

            var bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);

            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaAnimationDirector>().Length,
                "the previous session's director must be disposed with it");
            Assert.AreEqual(0, StructurePosePresenter.PendingCount,
                "a new campaign must not inherit the previous one's pending presenters");
        }

        // ---- the dense surface ---------------------------------------------------------

        [UnityTest]
        public IEnumerator AHundredStructuresAndTwentyDrillsStayBoundedAndDriftFree()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            int participantsBefore = director.ParticipantCount;
            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            int variantsBefore = MaterialVariantCache.VariantCount;

            var built = new List<GameObject>();
            var visuals = new List<Transform>();
            var basePositions = new List<Vector3>();
            try
            {
                for (int i = 0; i < 120; i++) // 100 structures + 20 drills
                {
                    var root = new GameObject($"ANI02 Probe {i}");
                    root.AddComponent<BoxCollider>();
                    built.Add(root);

                    var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    visual.transform.SetParent(root.transform, false);
                    visuals.Add(visual.transform);
                    basePositions.Add(visual.transform.localPosition);

                    root.AddComponent<DensityProbePresenter>().BindProbe(null);
                }

                director.Tick(1f);
                Assert.AreEqual(participantsBefore + 120, director.ParticipantCount,
                    "every probe must be adopted exactly once");

                for (int i = 0; i < 10000; i++) director.Tick(1f / 60f);

                Assert.AreEqual(participantsBefore + 120, director.ParticipantCount,
                    "ten thousand ticks must not grow the participant list");
                Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                    "nor create a Light");
                Assert.AreEqual(variantsBefore, MaterialVariantCache.VariantCount,
                    "nor a material variant");

                for (int i = 0; i < built.Count; i++)
                    built[i].GetComponent<DensityProbePresenter>().ResetToBasePose();

                for (int i = 0; i < visuals.Count; i++)
                    Assert.Less((visuals[i].localPosition - basePositions[i]).magnitude, 1e-3f,
                        $"probe {i} drifted off its authored pose");
            }
            finally
            {
                foreach (GameObject go in built) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            director.Tick(1f);
            Assert.AreEqual(participantsBefore, director.ParticipantCount,
                "destroying them all must leave no orphan participants");
        }

        // ---- quality and accessibility ------------------------------------------------------

        [UnityTest]
        public IEnumerator QualityAndMotionSwitchesNeverDuplicateAPresenter()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            DrillStructure drill = BuildRealDrill();
            try
            {
                director.Tick(1f);
                int participants = director.ParticipantCount;

                foreach (AnimationQuality quality in new[]
                         { AnimationQuality.Low, AnimationQuality.High, AnimationQuality.Medium })
                {
                    director.SetQuality(quality);
                    director.Tick(1f);
                }

                AnimationAccessibility.MotionOff = true;
                director.Tick(1f);
                AnimationAccessibility.MotionOff = false;
                AnimationAccessibility.ReducedMotion = true;
                director.Tick(1f);

                Assert.AreEqual(participants, director.ParticipantCount,
                    "quality and accessibility switches must not duplicate a presenter");
                Assert.AreEqual(1, drill.GetComponents<DrillAnimationPresenter>().Length);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(drill.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator TheWorldStillRunsOnOneKeyLightPlusOneHullFill()
        {
            yield return BootScene();

            Light[] worldLights = UnityEngine.Object.FindObjectsOfType<Light>()
                .Where(l => l != null && l.gameObject.activeInHierarchy && l.cullingMask == -1)
                .ToArray();

            Assert.AreEqual(2, worldLights.Length,
                "ANI-02 must not have disturbed the lighting budget: "
                + string.Join(", ", worldLights.Select(l => l.gameObject.name)));
        }
    }

    /// <summary>
    /// Stands in for a built structure in the density test: the real
    /// <see cref="StructurePosePresenter"/> machinery, driven by fixed values
    /// so the test measures the animation system rather than a planet's economy.
    /// </summary>
    public class DensityProbePresenter : StructurePosePresenter
    {
        public void BindProbe(NebulaAnimationDirector director) => BindAndRegister(director);
        protected override DrillLifecycle ResolveState() => DrillLifecycle.Active;
        protected override float ResolveProgress() => 1f;
        protected override (float rateHz, float amplitude) ResolveWork() => (2f, 0.02f);
    }
}
