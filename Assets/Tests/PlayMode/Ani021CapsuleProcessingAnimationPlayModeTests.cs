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
    /// ANI-02.1 in the real booted scene: the capsule proxy pool behaves under
    /// real builds, and the processing presenter reads the ship's real slots
    /// without touching a single cargo value.
    /// </summary>
    public class Ani021CapsuleProcessingAnimationPlayModeTests
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

        private static DrillStructure BuildRealDrill()
        {
            var planet = UnityEngine.Object.FindObjectOfType<PlanetIdentity>();
            Assert.IsNotNull(planet, "the Full system must have materialized planets");
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            Assert.IsNotNull(profile);
            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship);

            return DrillStructure.Create(planet, profile, ship.transform.position,
                DrillStructure.BuildSource.Parts, refundParts: 0);
        }

        // ---- capsule route against a real build ---------------------------------

        [UnityTest]
        public IEnumerator ARealDrillsCapsuleCountdownDrivesARouteThatStartsAtTheShipAndEndsOnTheSlot()
        {
            yield return BootScene();

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            DrillStructure drill = BuildRealDrill();
            try
            {
                Vector3 shipPosition = ship.transform.position;
                Vector3 slot = drill.transform.position;
                Vector3 normal = (slot - GetPlanetCentre(drill)).normalized;

                float total = Mathf.Max(0.01f, drill.Runtime.CapsuleRemaining);
                float progress = CapsuleFlightProfile.ProgressFromRemaining(drill.Runtime.CapsuleRemaining, total);

                Assert.AreEqual(0f, progress, 1e-3f, "a freshly ordered capsule has not left yet");
                Assert.AreEqual(CapsuleFlightPhase.Prepared, CapsuleFlightProfile.Phase(progress));

                Vector3 landed = CapsuleFlightProfile.Position(shipPosition, slot, normal, 1f);
                Assert.Less((landed - slot).magnitude, 1f,
                    "the route must end on the drill's real surface slot");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(drill.gameObject);
            }
        }

        private static Vector3 GetPlanetCentre(DrillStructure drill)
        {
            var planet = UnityEngine.Object.FindObjectOfType<PlanetIdentity>();
            return planet != null ? planet.transform.position : Vector3.zero;
        }

        [UnityTest]
        public IEnumerator ARouteBuiltFromRealWorldPositionsNeverJumps()
        {
            yield return BootScene();

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            DrillStructure drill = BuildRealDrill();
            try
            {
                Vector3 start = ship.transform.position;
                Vector3 end = drill.transform.position;
                Vector3 normal = (end - GetPlanetCentre(drill)).normalized;

                Vector3 previous = CapsuleFlightProfile.Position(start, end, normal, 0f);
                float longestStep = 0f;
                for (int i = 1; i <= 240; i++)
                {
                    Vector3 here = CapsuleFlightProfile.Position(start, end, normal, i / 240f);
                    longestStep = Mathf.Max(longestStep, (here - previous).magnitude);
                    previous = here;
                }

                Assert.Less(longestStep, (end - start).magnitude * 0.05f,
                    "no step across the real route may look like a teleport");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(drill.gameObject);
            }
        }

        // ---- the real build → capsule → hand-off flow ---------------------------------

        [UnityTest]
        public IEnumerator ARealDrillBuildProducesExactlyOneCapsuleProxyThatHandsOffOnLanding()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            director.CapsuleProxies.ReleaseAll();
            int leasedBefore = director.CapsuleProxies.LeasedCount;

            DrillStructure drill = BuildRealDrill();
            try
            {
                var presenters = drill.GetComponents<BuildCapsulePresenter>();
                Assert.AreEqual(1, presenters.Length, "one build, one capsule presenter — never a duplicate");

                director.Tick(1f);

                Assert.AreEqual(DrillLifecycle.CapsuleInTransit, drill.Runtime.State,
                    "a fresh build really is in transit");
                Assert.IsTrue(presenters[0].HasProxy, "and it must have leased exactly one proxy");
                Assert.AreEqual(leasedBefore + 1, director.CapsuleProxies.LeasedCount);
                Assert.IsFalse(presenters[0].HandedOff);

                // Run the real countdown out; the runtime — not the animation —
                // decides when the capsule has landed.
                for (int i = 0; i < 200; i++)
                {
                    drill.Runtime.Tick(0.1f);
                    director.Tick(1f / 30f);
                    if (presenters[0].HandedOff) break;
                }

                Assert.IsTrue(presenters[0].HandedOff,
                    "once the real countdown ends, construction owns the site");
                Assert.IsFalse(presenters[0].HasProxy, "and the proxy goes back to the pool");
                Assert.AreEqual(leasedBefore, director.CapsuleProxies.LeasedCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(drill.gameObject);
            }

            director.Tick(1f);
            Assert.AreEqual(leasedBefore, director.CapsuleProxies.LeasedCount,
                "and nothing is left flying");
        }

        [UnityTest]
        public IEnumerator ALandedCapsuleNeverFliesAgainNoMatterHowLongTheSessionRuns()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            director.CapsuleProxies.ReleaseAll();

            DrillStructure drill = BuildRealDrill();
            try
            {
                var presenter = drill.GetComponent<BuildCapsulePresenter>();
                for (int i = 0; i < 200 && !presenter.HandedOff; i++)
                {
                    drill.Runtime.Tick(0.1f);
                    director.Tick(1f / 30f);
                }
                Assert.IsTrue(presenter.HandedOff);

                int leasedAfterHandoff = director.CapsuleProxies.LeasedCount;
                for (int i = 0; i < 300; i++) director.Tick(1f / 60f);

                Assert.IsFalse(presenter.HasProxy,
                    "a completed capsule must not reappear after a load or a promote");
                Assert.AreEqual(leasedAfterHandoff, director.CapsuleProxies.LeasedCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(drill.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator CancellingABuildReturnsItsProxyImmediately()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            director.CapsuleProxies.ReleaseAll();
            int leasedBefore = director.CapsuleProxies.LeasedCount;

            DrillStructure drill = BuildRealDrill();
            director.Tick(1f);
            Assert.AreEqual(leasedBefore + 1, director.CapsuleProxies.LeasedCount);

            // Demote/cancel: the structure goes away mid-flight.
            UnityEngine.Object.DestroyImmediate(drill.gameObject);
            director.Tick(1f);

            Assert.AreEqual(leasedBefore, director.CapsuleProxies.LeasedCount,
                "a cancelled build must not strand its proxy in the sky");

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheCapsuleTracksItsRealCountdownDownTheRoute()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            director.CapsuleProxies.ReleaseAll();

            DrillStructure drill = BuildRealDrill();
            try
            {
                var presenter = drill.GetComponent<BuildCapsulePresenter>();
                director.Tick(1f);
                float startProgress = presenter.Progress;

                for (int i = 0; i < 20; i++)
                {
                    drill.Runtime.Tick(0.1f);
                    director.Tick(1f / 30f);
                }

                Assert.Greater(presenter.Progress, startProgress,
                    "progress must follow the real countdown, not a timer of its own");
                Assert.That(presenter.Progress, Is.InRange(0f, 1f));
                Assert.AreNotEqual(CapsuleFlightPhase.Prepared, presenter.Phase);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(drill.gameObject);
            }
        }

        // ---- proxy pool in a live session ------------------------------------------

        [UnityTest]
        public IEnumerator AHundredConsecutiveBuildsReuseTheSameProxiesAndAddNoLight()
        {
            yield return BootScene();

            var host = new GameObject("ANI021 Pool Host");
            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            try
            {
                CapsuleProxyPool pool = CapsuleProxyPool.Create(host.transform, 3, null);
                int childrenBefore = pool.Root.childCount;

                for (int i = 0; i < 100; i++)
                {
                    Assert.IsTrue(pool.TryLease(out CapsuleProxyLease lease, out _));
                    pool.Release(lease);
                }

                Assert.AreEqual(childrenBefore, pool.Root.childCount,
                    "a hundred builds must not create a hundred capsules");
                Assert.AreEqual(0, pool.LeasedCount);
                Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                    "a capsule proxy must never add a Light");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ThreeQuickAssetCapsulesFitTheDefaultPoolAndAFourthIsRefusedSafely()
        {
            yield return BootScene();

            var host = new GameObject("ANI021 Pool Host");
            try
            {
                // The Quick Asset Package sends exactly three.
                CapsuleProxyPool pool = CapsuleProxyPool.Create(host.transform, 3, null);

                var leases = new List<CapsuleProxyLease>();
                for (int i = 0; i < 3; i++)
                {
                    Assert.IsTrue(pool.TryLease(out CapsuleProxyLease lease, out _),
                        $"quick-asset capsule {i} must get a proxy");
                    leases.Add(lease);
                }

                Assert.IsFalse(pool.TryLease(out _, out _), "a fourth must be refused, not queued");
                Assert.AreEqual(3, pool.LeasedCount);

                foreach (CapsuleProxyLease lease in leases) pool.Release(lease);
                Assert.AreEqual(0, pool.LeasedCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            yield return null;
        }

        // ---- processing against the ship's real slots ---------------------------------

        [UnityTest]
        public IEnumerator TheShipHasExactlyOneProcessingPresenterBoundToItsRealSlots()
        {
            yield return BootScene();

            var presenters = UnityEngine.Object.FindObjectsOfType<ProcessingAnimationPresenter>();
            Assert.AreEqual(1, presenters.Length, "one processing presenter per session");

            var processing = UnityEngine.Object.FindObjectOfType<ShipProcessing>();
            Assert.IsNotNull(processing, "the ship must have its processing component");
            Assert.IsNotNull(processing.Bench, "and the real bench the presenter reads");
        }

        [UnityTest]
        public IEnumerator AnIdleShipReportsIdleAndTouchesNoCargoValue()
        {
            yield return BootScene();

            var presenter = UnityEngine.Object.FindObjectOfType<ProcessingAnimationPresenter>();
            var processing = UnityEngine.Object.FindObjectOfType<ShipProcessing>();
            NebulaAnimationDirector director = Director();

            // VS-017A.2: the slot bank is gone; what the presenter reads now is the
            // bench's own monotonic conversion count. Animation must never move it.
            long conversionsBefore = processing.Bench.ConversionsTotal;
            int benchBefore = processing.Bench.InputAmount;

            for (int i = 0; i < 300; i++) director.Tick(1f / 60f);

            Assert.AreEqual(ProcessingAnimationState.Idle, presenter.CurrentState,
                "nothing is loaded, so nothing should be animating");
            Assert.AreEqual(conversionsBefore, processing.Bench.ConversionsTotal,
                "animation must never produce a conversion");
            Assert.AreEqual(benchBefore, processing.Bench.InputAmount,
                "nor change what is on the bench");
        }

        [UnityTest]
        public IEnumerator AdoptingProgressSilentlyNeverReplaysAFinishedBatch()
        {
            yield return BootScene();

            var presenter = UnityEngine.Object.FindObjectOfType<ProcessingAnimationPresenter>();
            NebulaAnimationDirector director = Director();

            // This is what a load does: take the real totals as the baseline
            // rather than treating every past batch as new.
            presenter.AdoptCurrentProgressSilently();
            for (int i = 0; i < 60; i++) director.Tick(1f / 60f);

            Assert.AreNotEqual(ProcessingAnimationState.OutputReady, presenter.CurrentState,
                "a restored session must not celebrate batches it already finished");
        }

        // ---- teardown and budget ------------------------------------------------------

        [UnityTest]
        public IEnumerator RestartLeavesOneProcessingPresenterAndOneDirector()
        {
            yield return BootScene();

            var bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);

            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<ProcessingAnimationPresenter>().Length,
                "a restart must not accumulate processing presenters");
        }

        [UnityTest]
        public IEnumerator TheWorldStillRunsOnOneKeyLightPlusOneHullFill()
        {
            yield return BootScene();

            Light[] worldLights = UnityEngine.Object.FindObjectsOfType<Light>()
                .Where(l => l != null && l.gameObject.activeInHierarchy && l.cullingMask == -1)
                .ToArray();

            Assert.AreEqual(2, worldLights.Length,
                "ANI-02.1 must not have disturbed the lighting budget: "
                + string.Join(", ", worldLights.Select(l => l.gameObject.name)));
        }
    }
}
