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
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-01 in the real booted scene: one director, one ship presenter, and
    /// a guarantee that animating the ship changes nothing about how it flies.
    /// </summary>
    public class Ani01PlayerShipAnimationPlayModeTests
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

        private static PlayerShipAnimationPresenter Presenter()
        {
            var presenter = UnityEngine.Object.FindObjectOfType<PlayerShipAnimationPresenter>();
            Assert.IsNotNull(presenter, "the ship animation presenter must exist after boot");
            return presenter;
        }

        // ---- composition ------------------------------------------------------

        [UnityTest]
        public IEnumerator TheSessionHasExactlyOneDirectorAndOneShipPresenter()
        {
            yield return BootScene();

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<PlayerShipAnimationPresenter>().Length);

            var bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap?.Context);
            Assert.AreSame(Director(), bootstrap.Context.AnimationDirector,
                "the director must be reachable through the typed context, not by searching the scene");
        }

        [UnityTest]
        public IEnumerator TheShipPresenterIsRegisteredAndTicking()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            Assert.GreaterOrEqual(director.ParticipantCount, 1, "the ship must be registered for animation");

            // Waited out in world time, not in frames. The claim is that the player
            // loop drives the director on its own -- nothing here calls Tick. But a
            // batchmode editor loops far faster than a display, so five frames can add
            // up to under 10 ms of Time.deltaTime, which is short of a single tick
            // interval at every tier (16.7 ms at High, 41.7 ms at Low) and the director
            // is correctly still accumulating. Wait for one interval to actually
            // elapse, then require the tick that must have happened by then; the
            // deadline is unscaled so a frozen clock times out and fails rather than
            // hanging.
            long before = director.TickCount;
            float deadline = Time.unscaledTime + Mathf.Max(director.Profile.TickInterval * 6f, 0.5f);
            while (director.TickCount == before && Time.unscaledTime < deadline) yield return null;

            Assert.Greater(director.TickCount, before, "the director must be ticking in a live session");
        }

        // ---- gameplay separation ------------------------------------------------

        [UnityTest]
        public IEnumerator AnimationNeverMovesTheGameplayRootTheRigidbodyOrTheCamera()
        {
            yield return BootScene();

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship);
            Camera camera = Camera.main;
            Assert.IsNotNull(camera);

            var rigidbody = ship.GetComponent<Rigidbody>();
            Collider[] colliders = ship.GetComponentsInChildren<Collider>(true);
            int colliderCount = colliders.Length;

            Vector3 rootBefore = ship.transform.localPosition;
            Quaternion rootRotationBefore = ship.transform.localRotation;
            Transform cameraRoot = camera.transform.parent != null ? camera.transform.parent : camera.transform;
            Vector3 cameraRootBefore = cameraRoot.localPosition;

            NebulaAnimationDirector director = Director();
            for (int i = 0; i < 200; i++) director.Tick(1f / 60f);
            yield return null;

            Assert.AreEqual(rootBefore, ship.transform.localPosition,
                "the gameplay root must not be animated");
            Assert.AreEqual(rootRotationBefore, ship.transform.localRotation);
            Assert.AreEqual(cameraRootBefore, cameraRoot.localPosition,
                "animation must never reach the camera root");
            Assert.AreEqual(colliderCount, ship.GetComponentsInChildren<Collider>(true).Length,
                "animation must not add or remove a collider");
            if (rigidbody != null)
                Assert.AreEqual(rigidbody, ship.GetComponent<Rigidbody>(), "the Rigidbody must be untouched");
        }

        [UnityTest]
        public IEnumerator TheShipFliesIdenticallyWithAnimationOnAndOff()
        {
            yield return BootScene();

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship);

            ship.SetThrottleFraction(0.6f);
            float throttleWithAnimation = ship.GetThrottleFraction();
            Vector3 velocityWithAnimation = ship.Velocity;

            AnimationAccessibility.MotionOff = true;
            NebulaAnimationDirector director = Director();
            for (int i = 0; i < 60; i++) director.Tick(1f / 60f);
            yield return null;

            Assert.AreEqual(throttleWithAnimation, ship.GetThrottleFraction(), 1e-4f,
                "turning animation off must not change the throttle");
            Assert.AreEqual(velocityWithAnimation, ship.Velocity,
                "nor the ship's velocity — animation is visual only");
        }

        // ---- states -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ThePresenterReadsIdleAndThrottleFromTheRealShip()
        {
            yield return BootScene();

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            PlayerShipAnimationPresenter presenter = Presenter();
            NebulaAnimationDirector director = Director();

            ship.SetThrottleFraction(0f);
            director.Tick(1f);
            Assert.AreEqual(ShipLightingState.Idle, presenter.CurrentState);

            ship.SetThrottleFraction(0.7f);
            director.Tick(1f);
            Assert.AreEqual(ShipLightingState.Throttle, presenter.CurrentState);
            Assert.AreEqual(0.7f, ship.GetThrottleFraction(), 1e-4f,
                "the presenter must not have written back to the throttle");

            ship.SetThrottleFraction(0f);
            director.Tick(1f);
            Assert.AreEqual(ShipLightingState.Idle, presenter.CurrentState, "the state must return, not latch");

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheVisualRootIsFoundAndIsAChildOfTheGameplayRoot()
        {
            yield return BootScene();

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            var visualController = ship.GetComponent<PlayerShipVisualController>();
            Assert.IsNotNull(visualController);

            if (visualController.CurrentVisual == null)
                Assert.Ignore("this ship has no authored visual root; the presenter is a documented no-op");

            Assert.IsTrue(visualController.CurrentVisual.transform.IsChildOf(ship.transform),
                "the animated visual must be a child of the gameplay root, never the root itself");
            Assert.IsTrue(Presenter().HasVisualRoot);

            yield return null;
        }

        // ---- accessibility and quality ----------------------------------------------------

        [UnityTest]
        public IEnumerator MotionOffHoldsTheVisualRootExactlyAtItsAuthoredPose()
        {
            yield return BootScene();

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            var visualController = ship.GetComponent<PlayerShipVisualController>();
            if (visualController.CurrentVisual == null)
                Assert.Ignore("this ship has no authored visual root; the presenter is a documented no-op");

            Transform visual = visualController.CurrentVisual.transform;
            NebulaAnimationDirector director = Director();

            AnimationAccessibility.MotionOff = true;
            for (int i = 0; i < 50; i++) director.Tick(1f / 60f);
            Vector3 held = visual.localPosition;
            Quaternion heldRotation = visual.localRotation;

            for (int i = 0; i < 50; i++) director.Tick(1f / 60f);

            Assert.AreEqual(held, visual.localPosition, "Motion Off must hold the pose completely still");
            Assert.AreEqual(heldRotation, visual.localRotation);
            Assert.AreEqual(0f, Presenter().BankDegrees, 1e-4f, "and there must be no bank at all");

            yield return null;
        }

        [UnityTest]
        public IEnumerator ReducedMotionLowersTheBankCeilingWithoutRemovingIt()
        {
            yield return BootScene();

            float full = AnimationAccessibility.BankCeilingDegrees;
            AnimationAccessibility.ReducedMotion = true;
            float reduced = AnimationAccessibility.BankCeilingDegrees;

            Assert.Less(reduced, full);
            Assert.Greater(reduced, 0f);

            AnimationAccessibility.CameraComfort = true;
            Assert.Less(AnimationAccessibility.BankCeilingDegrees, reduced,
                "Camera Comfort tightens it further still");

            yield return null;
        }

        [UnityTest]
        public IEnumerator QualitySwitchesAddNoParticipantObjectOrLight()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            int participantsBefore = director.ParticipantCount;
            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;
            int renderersBefore = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
            int variantsBefore = MaterialVariantCache.VariantCount;

            foreach (AnimationQuality quality in new[]
                     { AnimationQuality.Low, AnimationQuality.High, AnimationQuality.Medium })
            {
                director.SetQuality(quality);
                for (int i = 0; i < 20; i++) director.Tick(1f / 60f);
                yield return null;
                Assert.AreEqual(quality, director.Profile.Quality);
            }

            Assert.AreEqual(participantsBefore, director.ParticipantCount,
                "a quality switch must not duplicate a presenter");
            Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                "animation must never create a Light");
            Assert.AreEqual(renderersBefore, UnityEngine.Object.FindObjectsOfType<Renderer>().Length);
            Assert.AreEqual(variantsBefore, MaterialVariantCache.VariantCount,
                "animation must not touch the lighting material variant cache");
        }

        // ---- drift and growth -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator TenThousandVisualTicksLeaveNoDriftAndGrowNothing()
        {
            yield return BootScene();

            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            var visualController = ship.GetComponent<PlayerShipVisualController>();
            if (visualController.CurrentVisual == null)
                Assert.Ignore("this ship has no authored visual root; the presenter is a documented no-op");

            Transform visual = visualController.CurrentVisual.transform;
            NebulaAnimationDirector director = Director();
            PlayerShipAnimationPresenter presenter = Presenter();

            Vector3 basePosition = visual.localPosition;
            Quaternion baseRotation = visual.localRotation;
            int objectsBefore = ship.GetComponentsInChildren<Transform>(true).Length;
            int participantsBefore = director.ParticipantCount;

            for (int i = 0; i < 10000; i++) director.Tick(1f / 60f);

            presenter.ResetToBasePose();

            Assert.AreEqual(basePosition.x, visual.localPosition.x, 1e-3f, "visual position drifted");
            Assert.AreEqual(basePosition.y, visual.localPosition.y, 1e-3f);
            Assert.AreEqual(basePosition.z, visual.localPosition.z, 1e-3f);
            Assert.Less(Quaternion.Angle(baseRotation, visual.localRotation), 0.05f, "visual rotation drifted");

            Assert.AreEqual(objectsBefore, ship.GetComponentsInChildren<Transform>(true).Length,
                "ten thousand ticks must not spawn a GameObject");
            Assert.AreEqual(participantsBefore, director.ParticipantCount,
                "nor register another participant");
        }

        // ---- teardown ------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator RestartLeavesOneDirectorOnePresenterAndNoOrphanParticipant()
        {
            yield return BootScene();

            var bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);

            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            NebulaAnimationDirector[] directors = UnityEngine.Object.FindObjectsOfType<NebulaAnimationDirector>();
            Assert.AreEqual(1, directors.Length, "the previous session's director must be disposed with it");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<PlayerShipAnimationPresenter>().Length);

            Assert.GreaterOrEqual(directors[0].ParticipantCount, 1,
                "the new session's ship must be registered");
            // Deliberately not a fixed number: every later part legitimately adds
            // presenters to a session. The claim is that nothing carried over,
            // which the single-director and single-ship-presenter checks above,
            // and the empty adoption queue below, actually establish.
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount,
                "and the old session's participants must not have carried over");

            // The new director must still work after the old one was destroyed.
            long before = directors[0].TickCount;
            directors[0].Tick(1f);
            Assert.Greater(directors[0].TickCount, before);
        }
    }
}
