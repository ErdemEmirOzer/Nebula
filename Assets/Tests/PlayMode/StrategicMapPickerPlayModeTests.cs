using System.Collections;
using Nebula.UI.StrategicMap;
using Nebula.UI.StrategicMap.Preview;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Picking has to agree with drawing: the player can only ever click something the map
    /// is currently showing. These aim a real camera at real objects and check both that the
    /// right one comes back and that the wrong level returns nothing.
    ///
    /// A real camera is needed because the picking is done in screen space --
    /// WorldToScreenPoint has no meaning without one.
    /// </summary>
    public sealed class StrategicMapPickerPlayModeTests
    {
        private Camera camera;
        private StrategicMapUniverseSnapshot universe;

        /// <summary>
        /// The map's orbits run on real time, so "where a planet is" is a question with a
        /// clock in it. Pinning it makes the answer exact instead of depending on how many
        /// seconds of PlayMode had elapsed when the fixture happened to run.
        ///
        /// Deliberately NOT zero: at zero the live phase equals the authored start phase, and
        /// a picker that had gone back to reading the start phase would still pass.
        /// </summary>
        private const double PinnedOrbitSeconds = 60d;

        [SetUp]
        public void SetUp()
        {
            StrategicMapOrbitClock.OverrideSeconds(PinnedOrbitSeconds);
            universe = StrategicMapStaticPreviewSource.Load();
            var go = new GameObject("PickerTestCamera");
            camera = go.AddComponent<Camera>();
            camera.fieldOfView = StrategicMapUniverseRenderer.FieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 4000f;
        }

        [TearDown]
        public void TearDown()
        {
            StrategicMapOrbitClock.ClearOverride();
            if (camera != null) Object.DestroyImmediate(camera.gameObject);
        }

        /// <summary>Points the camera at a position from a given distance and returns its screen point.</summary>
        private Vector2 Aim(Vector3 target, float distance)
        {
            camera.transform.position = target + new Vector3(0f, distance * 0.35f, -distance);
            camera.transform.LookAt(target);
            Vector3 projected = camera.WorldToScreenPoint(target);
            return new Vector2(projected.x, projected.y);
        }

        [UnityTest]
        public IEnumerator Universe_PicksTheGalaxyUnderTheCursor()
        {
            yield return null;
            StrategicMapUniverseSnapshot.GalaxyRow galaxy = universe.galaxies[0];
            Vector2 screenPoint = Aim(galaxy.Center, 55f);

            StrategicMapPick pick = StrategicMapPicker.Pick(camera, universe, screenPoint,
                StrategicMapLevel.Universe, null, null, 1f);

            Assert.AreEqual(StrategicMapPickKind.Galaxy, pick.Kind);
            Assert.AreEqual(galaxy.id, pick.Id);
        }

        [UnityTest]
        public IEnumerator Universe_PicksNothingWhereThereIsNothing()
        {
            yield return null;
            Aim(universe.galaxies[0].Center, 55f);

            // Far off in the corner, where no galaxy is.
            StrategicMapPick pick = StrategicMapPicker.Pick(camera, universe, new Vector2(4f, 4f),
                StrategicMapLevel.Universe, null, null, 1f);

            Assert.AreEqual(StrategicMapPickKind.None, pick.Kind,
                "empty space must pick nothing rather than the nearest thing anywhere on screen");
        }

        [UnityTest]
        public IEnumerator Galaxy_PicksASystemOfTheActiveGalaxyOnly()
        {
            yield return null;
            StrategicMapUniverseSnapshot.SystemRow system = universe.systems[0];
            Vector2 screenPoint = Aim(system.Position, 12f);

            StrategicMapPick pick = StrategicMapPicker.Pick(camera, universe, screenPoint,
                StrategicMapLevel.Galaxy, system.galaxyId, null, 1f);
            Assert.AreEqual(StrategicMapPickKind.System, pick.Kind);
            Assert.AreEqual(system.id, pick.Id);

            // The same click with a different galaxy active must find nothing: that system
            // is not being drawn, so it cannot be clicked.
            string otherGalaxy = null;
            for (int i = 0; i < universe.galaxies.Count; i++)
                if (universe.galaxies[i].id != system.galaxyId) { otherGalaxy = universe.galaxies[i].id; break; }

            StrategicMapPick blocked = StrategicMapPicker.Pick(camera, universe, screenPoint,
                StrategicMapLevel.Galaxy, otherGalaxy, null, 1f);
            Assert.AreEqual(StrategicMapPickKind.None, blocked.Kind);
        }

        [UnityTest]
        public IEnumerator Galaxy_SystemTargetIncludesTheVisibleGlow_NotOnlyTheCoreDot()
        {
            yield return null;
            var focusedUniverse = new StrategicMapUniverseSnapshot();
            focusedUniverse.systems.Add(new StrategicMapUniverseSnapshot.SystemRow
            {
                id = "sys-target",
                name = "Target",
                galaxyId = "galaxy-target",
                x = 0f,
                y = 0f,
                z = 0f
            });

            Vector2 centre = Aim(Vector3.zero, 12f);
            // Twenty-six pixels is visibly within the enlarged marker/hover affordance but
            // outside the retired 18px pick disc that made edge clicks fail.
            StrategicMapPick pick = StrategicMapPicker.Pick(camera, focusedUniverse,
                centre + Vector2.right * 26f, StrategicMapLevel.Galaxy,
                "galaxy-target", null, 1f);

            Assert.AreEqual(StrategicMapPickKind.System, pick.Kind,
                "the luminous body of a system marker must be clickable, not only its centre pixel");
            Assert.AreEqual("sys-target", pick.Id);
        }

        [UnityTest]
        public IEnumerator Galaxy_TargetMarkerAndHoverAffordance_AreBrightAndLegible()
        {
            yield return null;
            var focusedUniverse = new StrategicMapUniverseSnapshot();
            focusedUniverse.systems.Add(new StrategicMapUniverseSnapshot.SystemRow
            {
                id = "sys-target",
                name = "Target",
                galaxyId = "galaxy-target",
                knowledge = (int)Nebula.Navigation.SystemKnowledgeLevel.Visited
            });

            var root = new GameObject("TargetAffordanceTest");
            try
            {
                StrategicMapSystemMarkers markers = StrategicMapSystemMarkers.Create(root.transform, focusedUniverse);
                markers.SetActiveGalaxy("galaxy-target");
                Mesh markerMesh = markers.GetComponent<MeshFilter>().sharedMesh;
                Assert.GreaterOrEqual(markerMesh.uv2[0].x, 0.009f,
                    "a system target must no longer collapse into the old dim pinprick");
                Assert.GreaterOrEqual(markers.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Intensity"),
                    1.5f, "a system target needs enough additive energy to separate from decor stars");

                StrategicMapSelectionOverlay overlay = StrategicMapSelectionOverlay.Create(root.transform, focusedUniverse);
                Mesh overlayMesh = overlay.GetComponent<MeshFilter>().sharedMesh;
                Assert.GreaterOrEqual(overlayMesh.uv2[4].x, 22f,
                    "the hover acquisition ring must surround the enlarged target cleanly");
                Color hover = overlayMesh.colors[4];
                Assert.Greater(hover.g, hover.r,
                    "the hover indicator must use the map's cyan action language, not a faint white ring");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator Galaxy_PicksNothingWhenNoGalaxyIsActive()
        {
            yield return null;
            Vector2 screenPoint = Aim(universe.systems[0].Position, 12f);

            StrategicMapPick pick = StrategicMapPicker.Pick(camera, universe, screenPoint,
                StrategicMapLevel.Galaxy, null, null, 1f);

            Assert.AreEqual(StrategicMapPickKind.None, pick.Kind);
        }

        /// <summary>
        /// OLD CONTRACT: aim at <c>OrbitPoint(..., planet.phase, ...)</c> — the phase the
        /// planet was authored at — because the map drew its worlds parked there.
        ///
        /// CURRENT PRODUCTION AUTHORITY: the worlds move.
        /// <see cref="StrategicMapPlanetBodies"/> places every body at
        /// <c>StrategicMapOrbitClock.PhaseOf(planet)</c> each frame, and
        /// <see cref="StrategicMapPicker"/> reads the same clock, so a click resolves against
        /// where the world is now. Aiming at the start phase aims at a spot the planet left
        /// long ago — which is exactly what this test was doing when it came back Star: the
        /// abandoned spot fell inside the star's own 30px reach.
        ///
        /// INVARIANT STILL PROTECTED, and strengthened: picking agrees with drawing. The
        /// clock is pinned (see <see cref="PinnedOrbitSeconds"/>) so "now" is an exact
        /// instant, and the second half proves the agreement is real rather than accidental —
        /// the abandoned start-phase spot must NOT resolve to that planet. A picker that
        /// went back to reading <c>planet.phase</c> fails on that half.
        /// </summary>
        [UnityTest]
        public IEnumerator System_PicksAPlanetWhereItActuallyIs()
        {
            yield return null;
            StrategicMapUniverseSnapshot.SystemRow system = universe.systems[0];
            float visualScale = StrategicMapOrbits.VisualScale(system);
            Assert.Greater(system.planetTo - system.planetFrom, 2,
                "this fixture aims at the system's third world, so it must have one");
            StrategicMapUniverseSnapshot.PlanetRow planet = universe.planets[system.planetFrom + 2];
            Assert.Greater(planet.AngularSpeed, 0f,
                "a parked world would make the moved-along-its-orbit half of this test vacuous");

            // The picker has to use the same orbit maths AND the same clock the world is
            // drawn with, so aim at where the planet really is rather than at its system.
            Vector3 planetPosition = StrategicMapOrbits.OrbitPoint(system.Position, planet,
                StrategicMapOrbitClock.PhaseOf(planet), visualScale);
            Vector2 screenPoint = Aim(planetPosition, 1.72f);

            StrategicMapPick pick = StrategicMapPicker.Pick(camera, universe, screenPoint,
                StrategicMapLevel.System, system.galaxyId, system.id, visualScale);

            Assert.AreEqual(StrategicMapPickKind.Planet, pick.Kind);
            Assert.AreEqual(planet.id, pick.Id);

            // And where it no longer is must not answer for it. At the pinned instant this
            // world is a third of a turn past its start phase, far outside the picker's reach.
            Vector3 abandoned = StrategicMapOrbits.OrbitPoint(system.Position, planet,
                planet.phase, visualScale);
            Assert.Greater(Vector3.Distance(abandoned, planetPosition), 0.01f,
                "the pinned clock must actually have moved this world off its start phase");
            Vector2 abandonedPoint = Aim(abandoned, 1.72f);

            StrategicMapPick stale = StrategicMapPicker.Pick(camera, universe, abandonedPoint,
                StrategicMapLevel.System, system.galaxyId, system.id, visualScale);

            Assert.AreNotEqual(planet.id, stale.Id,
                "a click where the world used to be resolved to it, so picking is reading a stale phase");
        }

        [UnityTest]
        public IEnumerator System_PicksAcrossTheVisiblePlanetDiscAtCloseZoom()
        {
            yield return null;
            StrategicMapUniverseSnapshot.SystemRow system = universe.systems[0];
            float visualScale = StrategicMapOrbits.VisualScale(system);
            StrategicMapUniverseSnapshot.PlanetRow planet = universe.planets[system.planetFrom + 2];
            Vector3 position = StrategicMapOrbits.OrbitPoint(system.Position, planet,
                StrategicMapOrbitClock.PhaseOf(planet), visualScale);

            // Close zoom is where the old fixed 16px hit disc was most visibly wrong: the
            // rendered sphere grows with perspective while its click target did not.
            Vector2 centre = Aim(position, 0.35f);
            float worldRadius = StrategicMapPlanetBodies.VisualDiameterMapUnits(planet) * 0.5f;
            Vector3 edge3 = camera.WorldToScreenPoint(position + camera.transform.right * worldRadius);
            float visibleRadius = Vector2.Distance(centre, new Vector2(edge3.x, edge3.y));
            Assert.Greater(visibleRadius, 20f,
                "fixture must draw the planet wider than the retired fixed 16px hit radius");

            float insideDisc = Mathf.Max(18f, visibleRadius * 0.75f);
            StrategicMapPick pick = StrategicMapPicker.Pick(camera, universe,
                centre + Vector2.right * insideDisc, StrategicMapLevel.System,
                system.galaxyId, system.id, visualScale);

            Assert.AreEqual(StrategicMapPickKind.Planet, pick.Kind,
                "a point visibly inside the rendered sphere must remain clickable at close zoom");
            Assert.AreEqual(planet.id, pick.Id);
        }

        [UnityTest]
        public IEnumerator NothingBehindTheCameraIsEverPickable()
        {
            yield return null;
            StrategicMapUniverseSnapshot.GalaxyRow galaxy = universe.galaxies[0];
            Aim(galaxy.Center, 55f);

            // Turn right around. WorldToScreenPoint still returns a point for something
            // behind the camera, and without the depth check it would stay clickable.
            camera.transform.Rotate(0f, 180f, 0f);
            Vector3 behind = camera.WorldToScreenPoint(galaxy.Center);

            StrategicMapPick pick = StrategicMapPicker.Pick(camera, universe,
                new Vector2(behind.x, behind.y), StrategicMapLevel.Universe, null, null, 1f);

            Assert.AreEqual(StrategicMapPickKind.None, pick.Kind);
        }
    }
}
