using System.Collections.Generic;
using UnityEngine;

namespace Nebula.World
{
    /// <summary>
    /// Lightweight "where am I" lookup for the HUD. The bootstrap registers
    /// each galaxy plus a region per star system and per planet; the HUD asks
    /// for the nearest matches at every level.
    /// </summary>
    public static class LocationRegistry
    {
        private struct Region
        {
            public string name;
            public Transform transform;
            public float radius;
        }

        private struct GalaxyEntry
        {
            public string name;
            public GalaxyMap map;
        }

        private static readonly List<Region> stars = new List<Region>();
        private static readonly List<Region> planets = new List<Region>();
        private static readonly List<GalaxyEntry> galaxies = new List<GalaxyEntry>();

        public static void Clear()
        {
            stars.Clear();
            planets.Clear();
            galaxies.Clear();
        }

        /// <summary>Read-only snapshot row for consumers like the universe map.</summary>
        public struct RegionInfo
        {
            public string name;
            public Vector3 position;
            public float radius;
        }

        /// <summary>Snapshots every registered star-level region (systems, black holes, fields, wrecks, anomalies).</summary>
        public static void CopyStars(List<RegionInfo> results)
        {
            results.Clear();
            for (int i = 0; i < stars.Count; i++)
            {
                Region region = stars[i];
                if (region.transform == null) continue;
                results.Add(new RegionInfo
                {
                    name = region.name,
                    position = region.transform.position,
                    radius = region.radius
                });
            }
        }

        public static void RegisterGalaxy(string name, GalaxyMap map)
        {
            galaxies.Add(new GalaxyEntry { name = name, map = map });
        }

        /// <summary>
        /// Galaxy the position most plausibly belongs to: smallest distance in
        /// units of each galaxy's own radius, so a small nearby galaxy wins
        /// over a giant distant one. Null when no galaxy is registered.
        /// </summary>
        public static string NearestGalaxy(Vector3 position, out GalaxyMap map)
        {
            map = null;
            string result = null;
            float best = float.MaxValue;
            for (int i = 0; i < galaxies.Count; i++)
            {
                GalaxyEntry entry = galaxies[i];
                if (entry.map == null) continue;
                float normalized = Vector3.Distance(entry.map.transform.position, position)
                                   / Mathf.Max(1f, entry.map.Radius);
                if (normalized < best)
                {
                    best = normalized;
                    result = entry.name;
                    map = entry.map;
                }
            }
            return result;
        }

        public static void RegisterStar(string name, Transform transform, float radius)
        {
            stars.Add(new Region { name = name, transform = transform, radius = radius });
        }

        public static void RegisterPlanet(string name, Transform transform, float radius)
        {
            planets.Add(new Region { name = name, transform = transform, radius = radius });
        }

        /// <summary>
        /// Star system and planet the position is currently in; either can be
        /// null ("interstellar space" / not near any planet).
        /// </summary>
        public static void GetLocation(Vector3 position, out string starName, out string planetName)
        {
            starName = FindNearest(stars, position);
            planetName = FindNearest(planets, position);
        }

        private static string FindNearest(List<Region> regions, Vector3 position)
        {
            string result = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < regions.Count; i++)
            {
                Region region = regions[i];
                if (region.transform == null) continue;
                float distance = Vector3.Distance(region.transform.position, position);
                if (distance < region.radius && distance < bestDistance)
                {
                    bestDistance = distance;
                    result = region.name;
                }
            }
            return result;
        }
    }
}
