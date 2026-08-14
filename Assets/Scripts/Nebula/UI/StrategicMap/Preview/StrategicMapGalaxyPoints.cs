using System.Collections.Generic;
using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>One rendered galaxy particle: where it is, how bright, how big, and which palette slot it uses.</summary>
    public struct StrategicGalaxyPoint
    {
        public Vector3 Position;
        public float Size;
        public float Alpha;
        /// <summary>Index into the galaxy's own three-colour palette.</summary>
        public int ColorIndex;
        public int GalaxyIndex;
    }

    /// <summary>
    /// A line-for-line port of the reference map's <c>buildGalaxyPoints</c>.
    ///
    /// The distribution is not decorative — it is what makes the four galaxies read as
    /// the same objects: an 18% central bulge, a 72% logarithmic four-arm spiral, and a
    /// 10% flat halo, all drawn from the snapshot's captured RNG state so every point
    /// lands where the reference put it.
    ///
    /// Built once and cached. Nothing here allocates per frame.
    /// </summary>
    public static class StrategicMapGalaxyPoints
    {
        /// <summary>The reference's captured Unity RNG state (snapshot <c>mapState</c>).</summary>
        private static readonly uint[] MapState = { 3380438929u, 1108024197u, 2475611664u, 4207517736u };

        private const float MaxTheta = Mathf.PI * 4.6f;
        private const float Tau = Mathf.PI * 2f;

        private static StrategicGalaxyPoint[] cached;
        private static StrategicMapUniverseSnapshot cachedFor;

        public static StrategicGalaxyPoint[] Build(StrategicMapUniverseSnapshot snapshot)
        {
            if (snapshot == null) return System.Array.Empty<StrategicGalaxyPoint>();
            if (cached != null && ReferenceEquals(cachedFor, snapshot)) return cached;

            var random = new StrategicMapPreviewRandom(MapState[0], MapState[1], MapState[2], MapState[3]);
            var points = new List<StrategicGalaxyPoint>(
                snapshot.galaxies.Count * StrategicMapUniverseSnapshot.GalaxyRow.PointCount);

            for (int g = 0; g < snapshot.galaxies.Count; g++)
            {
                StrategicMapUniverseSnapshot.GalaxyRow galaxy = snapshot.galaxies[g];
                float bulgeRadius = galaxy.radius * 0.13f;
                float r0 = bulgeRadius * 0.6f;
                float spiralB = Mathf.Log(galaxy.radius / r0) / MaxTheta;

                for (int i = 0; i < StrategicMapUniverseSnapshot.GalaxyRow.PointCount; i++)
                {
                    float size = galaxy.radius * random.RangeFloat(0.012f, 0.034f);
                    float category = random.Value();
                    Vector3 local;
                    float alpha;
                    int colorIndex;

                    if (category < 0.18f)
                    {
                        // Central bulge: a fat, bright, slightly flattened core.
                        local = new Vector3(
                            random.Gauss() * bulgeRadius * 0.8f,
                            random.Gauss() * bulgeRadius * 0.4f,
                            random.Gauss() * bulgeRadius * 0.8f);
                        alpha = random.RangeFloat(0.28f, 0.5f);
                        colorIndex = 0;
                        size *= 1.25f;
                    }
                    else if (category < 0.9f)
                    {
                        // Logarithmic spiral arm, scattered with a radius-dependent spread.
                        float t = Mathf.Sqrt(random.Value());
                        int arm = random.RangeInt(0, StrategicMapUniverseSnapshot.GalaxyRow.Arms);
                        float theta = t * MaxTheta;
                        float radius = r0 * Mathf.Exp(spiralB * theta);
                        float angle = theta + galaxy.phase + arm * Tau / StrategicMapUniverseSnapshot.GalaxyRow.Arms;
                        float armX = Mathf.Cos(angle) * radius;
                        float armZ = Mathf.Sin(angle) * radius;
                        float spread = galaxy.radius * 0.01f + radius * 0.035f;

                        local = new Vector3(
                            armX + random.Gauss() * spread,
                            random.Gauss() * (galaxy.radius * 0.006f + radius * 0.03f),
                            armZ + random.Gauss() * spread);

                        float mix = random.Value();
                        colorIndex = random.Value() < 0.08f ? 2 : (mix * mix < 0.5f ? 1 : 0);
                        alpha = random.RangeFloat(0.16f, 0.36f);
                    }
                    else
                    {
                        // Thin outer halo.
                        float angle = random.RangeFloat(0f, Tau);
                        float radius = galaxy.radius * Mathf.Sqrt(random.Value());
                        local = new Vector3(
                            Mathf.Cos(angle) * radius,
                            random.Gauss() * galaxy.radius * 0.012f,
                            Mathf.Sin(angle) * radius);
                        alpha = random.RangeFloat(0.08f, 0.16f);
                        colorIndex = 1;
                    }

                    // The reference consumes a unit-sphere sample per point purely to stay in
                    // step with Unity's own draw order; skipping it desynchronises everything after.
                    random.ConsumeUnitSphere();

                    points.Add(new StrategicGalaxyPoint
                    {
                        Position = galaxy.Center + local,
                        Size = size,
                        Alpha = alpha,
                        ColorIndex = colorIndex,
                        GalaxyIndex = g
                    });
                }
            }

            cachedFor = snapshot;
            return cached = points.ToArray();
        }

        /// <summary>Test hook: forces the next <see cref="Build"/> to regenerate.</summary>
        public static void ClearCache() { cached = null; cachedFor = null; }

        /// <summary>The reference's galaxy palette order: gold, blue, red, violet.</summary>
        public static Color GalaxyPaletteColor(int galaxyIndex, int colorIndex)
        {
            Color[] order = { StrategicMapTheme.Gold, StrategicMapTheme.Blue, StrategicMapTheme.Red, StrategicMapTheme.Violet };
            // Each galaxy mixes its own three palette slots; slot 0 is its primary hue.
            int primary = Mathf.Clamp(galaxyIndex, 0, order.Length - 1);
            switch (colorIndex)
            {
                case 0: return order[primary];
                case 1: return order[(primary + 1) % order.Length];
                default: return StrategicMapTheme.Cyan;
            }
        }
    }
}
