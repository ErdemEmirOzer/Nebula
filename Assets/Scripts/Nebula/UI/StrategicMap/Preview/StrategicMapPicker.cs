using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// What a click landed on, if anything.
    ///
    /// <see cref="Star"/> is the system's own star clicked from INSIDE that system, which is
    /// a different act from clicking the system from its galaxy: the player is already there
    /// and is asking about the star itself. Its id is the system's, because the star is the
    /// system's centre — nothing else in the game has an id for it.
    /// </summary>
    public enum StrategicMapPickKind { None, Galaxy, System, Planet, Star, Poi }

    /// <summary>A resolved click: what kind of thing, and which one.</summary>
    public readonly struct StrategicMapPick
    {
        public readonly StrategicMapPickKind Kind;
        public readonly string Id;
        public readonly string GalaxyId;

        public StrategicMapPick(StrategicMapPickKind kind, string id, string galaxyId)
        {
            Kind = kind;
            Id = id;
            GalaxyId = galaxyId;
        }

        public static readonly StrategicMapPick None = new StrategicMapPick(StrategicMapPickKind.None, null, null);
    }

    /// <summary>
    /// Turns a point on the map surface into the object under it.
    ///
    /// Picking is done in screen space against projected positions, not with physics rays.
    /// Nothing on this map has a collider -- the whole thing is procedural meshes and
    /// billboards -- and most markers are drawn at a fixed pixel size. Planets are real
    /// perspective meshes, so their projected visual radius is folded into the same
    /// screen-space test. A world-space ray would still miss a marker that is drawn eight
    /// pixels wide but is mathematically a point.
    ///
    /// What is pickable depends on the level, exactly as what is drawn does: galaxies at the
    /// universe overview, the active galaxy's systems inside it, that system's planets inside
    /// that. You can never click something the map is not currently showing.
    /// </summary>
    public static class StrategicMapPicker
    {
        /// <summary>How close to a target a click has to land, in pixels.</summary>
        private const float GalaxyRadiusPixels = 60f;
        // Larger than the bright core and its glow, so the full visible star is a target and
        // ordinary hand jitter does not turn a deliberate click into empty space.
        private const float SystemRadiusPixels = 32f;
        private const float PlanetRadiusPixels = 16f;
        private const float PlanetPaddingPixels = 6f;
        private const float PoiRadiusPixels = 22f;

        /// <summary>
        /// The star at the centre of the system view. Wider than a planet's reach because it
        /// is drawn wider — the disk alone is up to 16.5px and the corona spreads well past
        /// it, so a click that visibly lands on the star has to resolve to the star.
        /// </summary>
        private const float StarRadiusPixels = 30f;

        public static StrategicMapPick Pick(
            Camera camera,
            StrategicMapUniverseSnapshot universe,
            Vector2 screenPoint,
            StrategicMapLevel level,
            string activeGalaxyId,
            string activeSystemId,
            float visualScale)
        {
            if (camera == null || universe == null) return StrategicMapPick.None;

            switch (level)
            {
                case StrategicMapLevel.Universe:
                    return PickGalaxy(camera, universe, screenPoint);

                case StrategicMapLevel.Galaxy:
                    return PickSystem(camera, universe, screenPoint, activeGalaxyId);

                case StrategicMapLevel.System:
                case StrategicMapLevel.Planet:
                    return PickPlanet(camera, universe, screenPoint, activeSystemId, visualScale);

                default:
                    return StrategicMapPick.None;
            }
        }

        private static StrategicMapPick PickGalaxy(Camera camera, StrategicMapUniverseSnapshot universe,
            Vector2 screenPoint)
        {
            string best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < universe.galaxies.Count; i++)
            {
                StrategicMapUniverseSnapshot.GalaxyRow galaxy = universe.galaxies[i];
                if (!TryScreenDistance(camera, galaxy.Center, screenPoint, out float distance)) continue;

                // A galaxy reads as a disc filling much of the frame, not as a point. Testing
                // only a fixed pixel radius around the core meant a click anywhere on the arms
                // — which is most of what the player sees and aims at — resolved to nothing.
                // The fixed radius stays as a floor so a galaxy that is currently only a few
                // pixels across is still clickable.
                float reach = Mathf.Max(ScaledPixels(camera, GalaxyRadiusPixels),
                    ScreenRadius(camera, galaxy.Center, galaxy.radius));
                if (distance > reach) continue;
                // Overlapping discs resolve to whichever core the click is nearer.
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = galaxy.id;
            }

            return best == null
                ? StrategicMapPick.None
                : new StrategicMapPick(StrategicMapPickKind.Galaxy, best, best);
        }

        /// <summary>A world radius measured in screen pixels at that point's depth.</summary>
        private static float ScreenRadius(Camera camera, Vector3 center, float worldRadius)
        {
            if (worldRadius <= 0f) return 0f;
            Vector3 projectedCenter = camera.WorldToScreenPoint(center);
            Vector3 projectedEdge = camera.WorldToScreenPoint(center + camera.transform.right * worldRadius);
            if (projectedCenter.z <= 0f || projectedEdge.z <= 0f) return 0f;
            return Vector2.Distance(
                new Vector2(projectedCenter.x, projectedCenter.y),
                new Vector2(projectedEdge.x, projectedEdge.y));
        }

        private static StrategicMapPick PickSystem(Camera camera, StrategicMapUniverseSnapshot universe,
            Vector2 screenPoint, string activeGalaxyId)
        {
            if (string.IsNullOrEmpty(activeGalaxyId)) return StrategicMapPick.None;

            string best = null;
            float bestDistance = ScaledPixels(camera, SystemRadiusPixels);

            for (int i = 0; i < universe.systems.Count; i++)
            {
                StrategicMapUniverseSnapshot.SystemRow system = universe.systems[i];
                if (system.galaxyId != activeGalaxyId) continue;
                if (!TryScreenDistance(camera, system.Position, screenPoint, out float distance)) continue;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = system.id;
            }

            return best == null
                ? StrategicMapPick.None
                : new StrategicMapPick(StrategicMapPickKind.System, best, activeGalaxyId);
        }

        private static StrategicMapPick PickPlanet(Camera camera, StrategicMapUniverseSnapshot universe,
            Vector2 screenPoint, string activeSystemId, float visualScale)
        {
            if (string.IsNullOrEmpty(activeSystemId)) return StrategicMapPick.None;

            StrategicMapUniverseSnapshot.SystemRow system = null;
            for (int i = 0; i < universe.systems.Count; i++)
                if (universe.systems[i].id == activeSystemId) { system = universe.systems[i]; break; }
            if (system == null) return StrategicMapPick.None;

            string best = null;
            StrategicMapPickKind bestKind = StrategicMapPickKind.None;
            float bestDistance = float.MaxValue;

            // The star first, so its own card is reachable. A world crossing in front of it
            // still wins, because the planet test below runs against its own tighter reach
            // and only replaces the star when it is genuinely nearer the cursor.
            if (TryScreenDistance(camera, system.Position, screenPoint, out float starDistance)
                && starDistance <= ScaledPixels(camera, StarRadiusPixels))
            {
                best = system.id;
                bestKind = StrategicMapPickKind.Star;
                bestDistance = starDistance;
            }

            int planetFrom = Mathf.Clamp(system.planetFrom, 0, universe.planets.Count);
            int planetTo = Mathf.Clamp(system.planetTo, planetFrom, universe.planets.Count);
            for (int p = planetFrom; p < planetTo; p++)
            {
                StrategicMapUniverseSnapshot.PlanetRow planet = universe.planets[p];
                // The same orbit maths AND the same clock the bodies are placed with, so the
                // click target and the world the player sees can never be in different places.
                // Reading the start phase here instead would aim every click at where the
                // world was when the system was opened.
                Vector3 position = StrategicMapOrbits.OrbitPoint(
                    system.Position, planet, StrategicMapOrbitClock.PhaseOf(planet), visualScale);
                if (!TryScreenDistance(camera, position, screenPoint, out float distance)) continue;

                // Planets are real perspective meshes, not fixed-size icons. Their clickable
                // area follows the same canonical diameter used to draw them, with a small
                // accessibility margin. The fixed value remains only as a minimum target.
                float renderedRadius = ScreenRadius(camera, position,
                    StrategicMapPlanetBodies.VisualDiameterMapUnits(planet) * 0.5f);
                float reach = Mathf.Max(ScaledPixels(camera, PlanetRadiusPixels),
                    renderedRadius + ScaledPixels(camera, PlanetPaddingPixels));
                if (distance > reach) continue;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = planet.id;
                bestKind = StrategicMapPickKind.Planet;
            }

            ConsiderPoiRows(camera, universe.discoveries, screenPoint, activeSystemId,
                ref best, ref bestKind, ref bestDistance);
            ConsiderPoiRows(camera, universe.derelicts, screenPoint, activeSystemId,
                ref best, ref bestKind, ref bestDistance);
            ConsiderPoiRows(camera, universe.anomalies, screenPoint, activeSystemId,
                ref best, ref bestKind, ref bestDistance);

            return bestKind == StrategicMapPickKind.None
                ? StrategicMapPick.None
                : new StrategicMapPick(bestKind, best, system.galaxyId);
        }

        private static void ConsiderPoiRows(Camera camera,
            System.Collections.Generic.IReadOnlyList<StrategicMapUniverseSnapshot.MarkerRow> rows,
            Vector2 screenPoint, string activeSystemId, ref string best,
            ref StrategicMapPickKind bestKind, ref float bestDistance)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                StrategicMapUniverseSnapshot.MarkerRow poi = rows[i];
                if (poi.systemId != activeSystemId) continue;
                if (!TryScreenDistance(camera, poi.Position, screenPoint, out float distance)) continue;
                if (distance > ScaledPixels(camera, PoiRadiusPixels) || distance >= bestDistance) continue;
                bestDistance = distance;
                best = poi.id;
                bestKind = StrategicMapPickKind.Poi;
            }
        }

        private static bool TryScreenDistance(Camera camera, Vector3 worldPosition, Vector2 screenPoint,
            out float distance)
        {
            Vector3 projected = camera.WorldToScreenPoint(worldPosition);
            distance = 0f;
            // Behind the camera: WorldToScreenPoint still returns a point, and without this
            // check an object behind the player would be pickable through the back of the head.
            if (projected.z <= 0f) return false;
            distance = Vector2.Distance(new Vector2(projected.x, projected.y), screenPoint);
            return true;
        }

        /// <summary>
        /// Keeps interaction targets physically useful when the render texture is larger
        /// than 1080p. Targets never shrink below their authored 1080p minimum.
        /// </summary>
        private static float ScaledPixels(Camera camera, float pixelsAt1080)
        {
            float height = camera != null ? camera.pixelHeight : 0f;
            return pixelsAt1080 * Mathf.Max(1f, height / 1080f);
        }
    }
}
