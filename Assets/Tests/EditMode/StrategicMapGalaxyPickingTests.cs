using NUnit.Framework;
using UnityEngine;
using Nebula.UI.StrategicMap;
using Nebula.UI.StrategicMap.Preview;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Picking a galaxy at the universe overview. A galaxy is drawn as a disc that fills
    /// much of the frame, so the arms — which is most of what the player aims at — have to
    /// be part of its click target.
    /// </summary>
    public class StrategicMapGalaxyPickingTests
    {
        private Camera camera;
        private RenderTexture target;

        private const float GalaxyRadius = 10f;
        private const float CameraDistance = 55f;

        [SetUp]
        public void SetUp()
        {
            // A render texture pins the camera's pixel dimensions, so the projected sizes
            // these assertions are written against never depend on the game view's size.
            target = new RenderTexture(1920, 1080, 0);
            var go = new GameObject("PickTestCamera");
            camera = go.AddComponent<Camera>();
            camera.targetTexture = target;
            camera.fieldOfView = StrategicMapUniverseRenderer.FieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 4000f;
            // Looking down +Z at the origin, so world +X is screen right.
            go.transform.SetPositionAndRotation(new Vector3(0f, 0f, -CameraDistance), Quaternion.identity);
        }

        [TearDown]
        public void TearDown()
        {
            if (camera != null) Object.DestroyImmediate(camera.gameObject);
            if (target != null) Object.DestroyImmediate(target);
        }

        private static StrategicMapUniverseSnapshot OneGalaxyAtOrigin()
        {
            var snapshot = new StrategicMapUniverseSnapshot();
            snapshot.galaxies.Add(new StrategicMapUniverseSnapshot.GalaxyRow
            {
                id = "aetheria", name = "Aetheria",
                centerX = 0f, centerY = 0f, centerZ = 0f,
                radius = GalaxyRadius
            });
            return snapshot;
        }

        private StrategicMapPick PickAtOffset(StrategicMapUniverseSnapshot universe, float pixelsRight)
        {
            Vector3 centre = camera.WorldToScreenPoint(Vector3.zero);
            var point = new Vector2(centre.x + pixelsRight, centre.y);
            return StrategicMapPicker.Pick(camera, universe, point, StrategicMapLevel.Universe, null, null, 1f);
        }

        /// <summary>Half-height at distance d is d*tan(fov/2), so the disc's own radius in pixels is known exactly.</summary>
        private static float ExpectedScreenRadiusPixels =>
            GalaxyRadius / (CameraDistance * Mathf.Tan(StrategicMapUniverseRenderer.FieldOfView * 0.5f * Mathf.Deg2Rad)) * 540f;

        [Test]
        public void AClickOnTheGalaxysArms_EntersThatGalaxy_NotNothing()
        {
            StrategicMapUniverseSnapshot universe = OneGalaxyAtOrigin();
            float armsOffset = ExpectedScreenRadiusPixels * 0.7f;
            Assert.Greater(armsOffset, 100f,
                "the test is only meaningful if the arms sit well outside the old fixed 60px core radius");

            StrategicMapPick pick = PickAtOffset(universe, armsOffset);

            Assert.AreEqual(StrategicMapPickKind.Galaxy, pick.Kind,
                "clicking the visible disc must enter the galaxy; a core-only target made most of the map dead");
            Assert.AreEqual("aetheria", pick.Id);
        }

        [Test]
        public void AClickOnTheCore_StillEntersTheGalaxy()
        {
            StrategicMapPick pick = PickAtOffset(OneGalaxyAtOrigin(), 0f);
            Assert.AreEqual(StrategicMapPickKind.Galaxy, pick.Kind);
            Assert.AreEqual("aetheria", pick.Id);
        }

        [Test]
        public void AClickWellOutsideTheDisc_PicksNothing_TheTargetIsNotUnbounded()
        {
            StrategicMapPick pick = PickAtOffset(OneGalaxyAtOrigin(), ExpectedScreenRadiusPixels * 2.5f);
            Assert.AreEqual(StrategicMapPickKind.None, pick.Kind,
                "empty space between galaxies must stay empty, or a missed click would teleport the player somewhere");
        }

        [Test]
        public void ATinyDistantGalaxy_KeepsTheFixedPixelFloor_SoItStaysClickable()
        {
            var universe = new StrategicMapUniverseSnapshot();
            universe.galaxies.Add(new StrategicMapUniverseSnapshot.GalaxyRow
            {
                id = "speck", name = "Speck", centerX = 0f, centerY = 0f, centerZ = 0f,
                radius = 0.02f // a couple of pixels across at this distance
            });

            StrategicMapPick pick = PickAtOffset(universe, 30f);
            Assert.AreEqual(StrategicMapPickKind.Galaxy, pick.Kind,
                "a galaxy smaller than the click floor must still be reachable");
        }
    }
}
