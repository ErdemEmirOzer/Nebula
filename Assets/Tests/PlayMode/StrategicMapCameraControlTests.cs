using System.Collections;
using Nebula.UI.StrategicMap.Preview;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Dragging has to actually turn the camera.
    ///
    /// It did not, for a while: the screen handed drags to the flat node graph, and once that
    /// was switched off in favour of the 3D map the input went nowhere. The map looked
    /// three-dimensional and answered nothing. These pin the camera's own response, so the
    /// wiring cannot quietly come loose again.
    /// </summary>
    public sealed class StrategicMapCameraControlTests
    {
        private StrategicMapUniverseRenderer renderer;

        [SetUp]
        public void SetUp()
        {
            renderer = StrategicMapUniverseRenderer.Create(new StrategicMapStaticPreviewSource());
            // The renderer is created dormant; the screen wakes it when the map opens. Without
            // this its Update never runs, so nothing that needs a frame -- the coast, the zoom
            // smoothing -- moves at all.
            renderer.SetActiveRendering(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (renderer != null) Object.DestroyImmediate(renderer.gameObject);
        }

        /// <summary>Waits real seconds, which a fixed frame count does not in batchmode.</summary>
        private static IEnumerator WaitUnscaled(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }

        [UnityTest]
        public IEnumerator DraggingSidewaysTurnsTheCamera()
        {
            yield return null;
            float before = renderer.YawDegrees;

            renderer.Drag(new Vector2(120f, 0f));

            Assert.AreNotEqual(before, renderer.YawDegrees, "a horizontal drag must change the yaw");
        }

        [UnityTest]
        public IEnumerator DraggingUpAndDownTiltsTheCamera()
        {
            yield return null;
            float before = renderer.PitchDegrees;

            renderer.Drag(new Vector2(0f, 90f));

            Assert.Greater(renderer.PitchDegrees, before, "dragging up must raise the camera");
        }

        [UnityTest]
        public IEnumerator TheCameraNeverTipsOverTheTop()
        {
            yield return null;

            // Far past any sane limit, in both directions.
            for (int i = 0; i < 200; i++) renderer.Drag(new Vector2(0f, 100f));
            float high = renderer.PitchDegrees;
            for (int i = 0; i < 400; i++) renderer.Drag(new Vector2(0f, -100f));
            float low = renderer.PitchDegrees;

            // A camera that rolls past vertical turns the map upside down, which is
            // disorienting and has no way back except more dragging.
            Assert.Less(high, 90f, "the camera must stop short of straight down");
            Assert.Greater(low, -90f, "the camera must stop short of straight up");
        }

        [UnityTest]
        public IEnumerator ReleasingLetsTheCameraCoastAndThenStop()
        {
            yield return null;
            renderer.Drag(new Vector2(10f, 0f));
            float atRelease = renderer.YawDegrees;

            renderer.ReleaseDrag(new Vector2(600f, 0f));
            yield return null;
            float coasting = renderer.YawDegrees;
            Assert.AreNotEqual(atRelease, coasting, "the camera must keep turning after the drag is released");

            // And it must actually settle rather than spinning on. Waited in real seconds,
            // not frames: the decay is per second, and a batchmode frame is short enough that
            // a few hundred of them are a fraction of one.
            yield return WaitUnscaled(4f);
            float settled = renderer.YawDegrees;
            yield return WaitUnscaled(0.5f);
            Assert.AreEqual(settled, renderer.YawDegrees, 0.01f, "the coast must decay to a stop");
        }

        [UnityTest]
        public IEnumerator GrabbingAgainStopsTheCoastDead()
        {
            yield return null;
            renderer.ReleaseDrag(new Vector2(900f, 0f));
            yield return null;

            renderer.Drag(new Vector2(1f, 0f));
            float grabbed = renderer.YawDegrees;
            yield return null;

            // Without this the camera would keep drifting under a stationary cursor, which
            // feels like the map fighting the player.
            Assert.AreEqual(grabbed, renderer.YawDegrees, 0.0001f);
        }

        [UnityTest]
        public IEnumerator TheWheelZoomsInAndOutWithinItsLimits()
        {
            yield return null;
            float start = renderer.Distance;

            // Unity reports +1 per notch scrolling up, which the screen negates and scales.
            renderer.Zoom(-500f, false);
            yield return WaitUnscaled(1f);
            Assert.Less(renderer.Distance, start, "scrolling up must move the camera in");

            renderer.Zoom(20000f, false);
            yield return WaitUnscaled(2f);
            Assert.LessOrEqual(renderer.Distance, StrategicMapUniverseRenderer.MaxDistance + 0.01f);

            renderer.Zoom(-20000f, false);
            yield return WaitUnscaled(2f);
            Assert.GreaterOrEqual(renderer.Distance, StrategicMapUniverseRenderer.MinDistanceUniverse - 0.01f);
        }
    }
}
