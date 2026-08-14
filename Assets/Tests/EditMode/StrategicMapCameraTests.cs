using NUnit.Framework;
using UnityEngine;
using Nebula.UI.StrategicMap;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Strategic map M3: the pan/zoom/momentum core. These tests exist mostly to
    /// guarantee the quality bar the design sets — no NaN coordinates, no lost
    /// input, and a view transform that stays sane under abusive input.
    /// </summary>
    public class StrategicMapCameraTests
    {
        private const float Ppu = 4f; // pixels per map unit

        [Test]
        public void FreshCamera_StartsCentredAtUnitZoomWithNoMomentum()
        {
            var cam = new StrategicMapCamera();
            Assert.AreEqual(Vector2.zero, cam.Center);
            Assert.AreEqual(1f, cam.Zoom);
            Assert.AreEqual(Vector2.zero, cam.Velocity);
            Assert.IsFalse(cam.IsFocusing);
        }

        [Test]
        public void MapSpaceIsScaledFromAbsoluteWorldPositions_NeverFloatingOriginRelative()
        {
            // One full sector across becomes a manageable map distance.
            Vector2 origin = StrategicMapCamera.WorldToMap(Vector3.zero);
            Vector2 oneSector = StrategicMapCamera.WorldToMap(new Vector3(2_000_000f, 0f, 0f));

            Assert.AreEqual(Vector2.zero, origin);
            Assert.AreEqual(200f, oneSector.x, 0.001f, "2,000,000 absolute units must scale to 200 map units");
            Assert.AreEqual(0f, oneSector.y, "the map is a top-down projection: world Y is dropped, world Z becomes map Y");

            Vector2 depth = StrategicMapCamera.WorldToMap(new Vector3(0f, 999f, 10000f));
            Assert.AreEqual(1f, depth.y, 0.001f, "world Z drives map Y");
            Assert.AreEqual(0f, depth.x, 0.001f, "world Y must not leak into the map plane");
        }

        [Test]
        public void ScreenAndMapConversions_RoundTripExactly()
        {
            var cam = new StrategicMapCamera();
            cam.Pan(new Vector2(37f, -19f), Ppu);

            var mapPoint = new Vector2(12.5f, -4.25f);
            Vector2 screen = cam.MapToScreen(mapPoint, Ppu);
            Vector2 back = cam.ScreenToMap(screen, Ppu);

            Assert.AreEqual(mapPoint.x, back.x, 0.0001f);
            Assert.AreEqual(mapPoint.y, back.y, 0.0001f);
        }

        [Test]
        public void DraggingRight_MovesTheViewLeftOverTheMap_LikeGrabbingTheCanvas()
        {
            var cam = new StrategicMapCamera();
            cam.Pan(new Vector2(100f, 0f), Ppu);
            Assert.Less(cam.Center.x, 0f, "dragging the canvas right must move the viewport left");
        }

        [Test]
        public void WheelZoom_KeepsThePointUnderTheCursorAnchored()
        {
            var cam = new StrategicMapCamera();
            var cursor = new Vector2(160f, -90f);

            Vector2 anchorBefore = cam.ScreenToMap(cursor, Ppu);
            cam.ZoomBy(1.4f, cursor, Ppu);
            Vector2 anchorAfter = cam.ScreenToMap(cursor, Ppu);

            Assert.Greater(cam.Zoom, 1f, "the wheel must actually zoom in");
            Assert.AreEqual(anchorBefore.x, anchorAfter.x, 0.001f, "the map point under the cursor must not drift");
            Assert.AreEqual(anchorBefore.y, anchorAfter.y, 0.001f);
        }

        [Test]
        public void ZoomIsAlwaysClampedToItsRange_HoweverHardItIsPushed()
        {
            var cam = new StrategicMapCamera();
            for (int i = 0; i < 200; i++) cam.ZoomBy(5f, Vector2.zero, Ppu);
            Assert.AreEqual(StrategicMapCamera.MaxZoom, cam.Zoom, 0.0001f);

            for (int i = 0; i < 400; i++) cam.ZoomBy(-5f, Vector2.zero, Ppu);
            Assert.AreEqual(StrategicMapCamera.MinZoom, cam.Zoom, 0.0001f);
        }

        [Test]
        public void SemanticZoom_MovesThroughClusterSystemsAndDetailTiers()
        {
            var cam = new StrategicMapCamera();
            for (int i = 0; i < 200; i++) cam.ZoomBy(-5f, Vector2.zero, Ppu);
            Assert.AreEqual(StrategicZoomTier.Cluster, cam.Tier, "far out the map shows clusters only");

            cam.Reset();
            Assert.AreEqual(StrategicZoomTier.Systems, cam.Tier, "the default zoom shows systems");

            for (int i = 0; i < 200; i++) cam.ZoomBy(5f, Vector2.zero, Ppu);
            Assert.AreEqual(StrategicZoomTier.Detail, cam.Tier, "zoomed in the map shows full detail");
        }

        [Test]
        public void Momentum_GlidesThenSettlesToARest_NeverDriftsForever()
        {
            var cam = new StrategicMapCamera();
            cam.ReleaseWithMomentum(new Vector2(600f, 0f), Ppu);
            Assert.AreNotEqual(Vector2.zero, cam.Velocity);

            for (int i = 0; i < 600; i++) cam.Tick(0.016f);

            Assert.AreEqual(Vector2.zero, cam.Velocity, "momentum must damp to a full stop");
            Assert.IsTrue(IsFinite(cam.Center), "a glide must never produce a non-finite centre");
        }

        [Test]
        public void PlayerInputAlwaysWinsOverARunningFocusAnimation()
        {
            var cam = new StrategicMapCamera();
            cam.FocusOn(new Vector2(500f, 500f), 3f);
            Assert.IsTrue(cam.IsFocusing);

            cam.Pan(new Vector2(10f, 0f), Ppu);
            Assert.IsFalse(cam.IsFocusing, "a drag must cancel the focus glide immediately -- input is never swallowed");

            cam.FocusOn(new Vector2(500f, 500f), 3f);
            cam.ZoomBy(0.5f, Vector2.zero, Ppu);
            Assert.IsFalse(cam.IsFocusing, "a wheel zoom must cancel the focus glide too");
        }

        [Test]
        public void FocusAnimation_LandsExactlyOnItsTargetAndThenStops()
        {
            var cam = new StrategicMapCamera();
            var target = new Vector2(-321.5f, 88.25f);
            cam.FocusOn(target, 2.5f, durationSeconds: 0.3f);

            for (int i = 0; i < 60; i++) cam.Tick(0.016f);

            Assert.IsFalse(cam.IsFocusing);
            Assert.AreEqual(target.x, cam.Center.x, 0.001f);
            Assert.AreEqual(target.y, cam.Center.y, 0.001f);
            Assert.AreEqual(2.5f, cam.Zoom, 0.001f);
        }

        [Test]
        public void AbusiveInput_NeverProducesNaNOrInfiniteState()
        {
            var cam = new StrategicMapCamera();

            cam.Pan(new Vector2(float.NaN, 5f), Ppu);
            cam.Pan(new Vector2(float.PositiveInfinity, 0f), Ppu);
            cam.ZoomBy(float.NaN, Vector2.zero, Ppu);
            cam.ReleaseWithMomentum(new Vector2(float.NaN, float.NaN), Ppu);
            cam.FocusOn(new Vector2(float.NaN, 0f), float.NaN);
            cam.Tick(float.NaN);
            cam.Tick(-1f);
            // A zero pixel scale must not divide by zero either.
            cam.Pan(new Vector2(10f, 10f), 0f);
            cam.ZoomBy(1f, new Vector2(10f, 10f), 0f);

            Assert.IsTrue(IsFinite(cam.Center), $"centre became {cam.Center}");
            Assert.IsTrue(!float.IsNaN(cam.Zoom) && !float.IsInfinity(cam.Zoom), $"zoom became {cam.Zoom}");
            Assert.IsTrue(IsFinite(cam.Velocity));
            Assert.GreaterOrEqual(cam.Zoom, StrategicMapCamera.MinZoom);
            Assert.LessOrEqual(cam.Zoom, StrategicMapCamera.MaxZoom);
        }

        [Test]
        public void AVeryLongFrame_NeverTeleportsTheView()
        {
            var cam = new StrategicMapCamera();
            cam.ReleaseWithMomentum(new Vector2(800f, 0f), Ppu);
            Vector2 before = cam.Center;

            cam.Tick(5f); // a 5-second hitch

            float moved = Vector2.Distance(before, cam.Center);
            Assert.Less(moved, 50f, $"a long frame must be clamped, but the view jumped {moved} map units");
        }

        private static bool IsFinite(Vector2 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y);
    }
}
