using System.Collections.Generic;
using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>One soft volumetric nebula cloud: a large, very dim, squashed additive blob.</summary>
    public struct StrategicNebulaCloud
    {
        public Vector3 Position;
        public float Radius;
        /// <summary>Vertical squash — the reference draws these as flattened ellipses in the galactic plane.</summary>
        public float Squash;
        public float Rotation;
        public float Alpha;
        public int GalaxyIndex;
        public int ColorIndex;
        /// <summary>Drift phase — only the foreground motes use it.</summary>
        public float Phase;
    }

    /// <summary>One point in a cinematic space layer — halo star, intergalactic bridge star, or dust-lane mote.</summary>
    public struct StrategicSpacePoint
    {
        public Vector3 Position;
        public float Alpha;
        public float Size;
        public int GalaxyIndex;
        public int ColorIndex;
        /// <summary>Drift phase — only the foreground motes use it.</summary>
        public float Phase;
    }

    /// <summary>
    /// A port of the reference map's <c>buildCinematicSpaceLayers</c>: the galactic haloes,
    /// the intergalactic bridge, and the dust lanes that thread each spiral arm.
    ///
    /// The draw ORDER matters as much as the maths. Every layer shares one
    /// <c>mulberry32(60817)</c> stream, so generating them in a different order — or
    /// skipping one — shifts every point after it. They are produced here in exactly the
    /// reference's sequence, including the two snapshot arrays that happen to be empty
    /// (halo and bridge), which consume no draws but fix the starting point.
    ///
    /// The reference's own quality report counts 6930 halo and 1200 bridge stars as the
    /// generator's totals; what it actually draws is the procedural sampling reproduced
    /// here, which is the visible result the brief asks to match.
    /// </summary>
    public static class StrategicMapSpaceLayers
    {
        private const uint Seed = 60817u;
        private const float Tau = Mathf.PI * 2f;

        /// <summary>Per-galaxy nebula cloud counts, straight from the reference's galaxy specs.</summary>
        private static readonly int[] NebulaCounts = { 7, 6, 6, 7 };

        private static StrategicSpacePoint[] cachedHalo;
        private static StrategicSpacePoint[] cachedDust;
        private static StrategicSpacePoint[] cachedForegroundDust;
        private static StrategicNebulaCloud[] cachedNebulae;
        private static StrategicMapUniverseSnapshot cachedFor;

        /// <summary>Halo stars plus the intergalactic bridge — one sparse, wide shell layer.</summary>
        public static StrategicSpacePoint[] Halo(StrategicMapUniverseSnapshot snapshot)
        {
            EnsureBuilt(snapshot);
            return cachedHalo;
        }

        /// <summary>Dust lanes: dense, dim motes trailing each spiral arm slightly behind the stars.</summary>
        public static StrategicSpacePoint[] DustLanes(StrategicMapUniverseSnapshot snapshot)
        {
            EnsureBuilt(snapshot);
            return cachedDust;
        }

        /// <summary>
        /// The 128 near-camera motes the reference drifts in front of everything. They are the
        /// last thing its builder draws from the shared stream, so they only come out right if
        /// every layer before them consumed exactly the draws it should.
        /// </summary>
        public static StrategicSpacePoint[] ForegroundDust(StrategicMapUniverseSnapshot snapshot)
        {
            EnsureBuilt(snapshot);
            return cachedForegroundDust;
        }

        /// <summary>The nebula volumes, including the globular clusters the snapshot carries.</summary>
        public static StrategicNebulaCloud[] Nebulae(StrategicMapUniverseSnapshot snapshot)
        {
            EnsureBuilt(snapshot);
            return cachedNebulae;
        }

        private static void EnsureBuilt(StrategicMapUniverseSnapshot snapshot)
        {
            if (cachedHalo != null && ReferenceEquals(cachedFor, snapshot)) return;

            var random = new Mulberry32(Seed);
            var halo = new List<StrategicSpacePoint>(8192);
            var dust = new List<StrategicSpacePoint>(8192);
            var nebulae = new List<StrategicNebulaCloud>(64);
            Vector3 universeCenter = snapshot.UniverseCenter;

            // 1-2. The snapshot's own halo/bridge arrays. Both are empty in the reference
            //      capture, so they consume no draws — but they come first in its order.

            // 3. Per-galaxy halo shell.
            for (int g = 0; g < snapshot.galaxies.Count; g++)
            {
                StrategicMapUniverseSnapshot.GalaxyRow galaxy = snapshot.galaxies[g];
                int sampleCount = Mathf.RoundToInt(360f * galaxy.radius / 10f);
                for (int i = 0; i < sampleCount; i++)
                {
                    float radius = galaxy.radius * (1.15f + 1.35f * Mathf.Pow(random.Next(), 1.6f));
                    float theta = random.Next() * Tau;
                    float phi = Mathf.Acos(random.Next() * 2f - 1f);
                    halo.Add(new StrategicSpacePoint
                    {
                        Position = galaxy.Center + new Vector3(
                            radius * Mathf.Sin(phi) * Mathf.Cos(theta),
                            radius * Mathf.Cos(phi),
                            radius * Mathf.Sin(phi) * Mathf.Sin(theta)),
                        Alpha = 0.12f + random.Next() * 0.24f,
                        Size = 0.012f + random.Next() * 0.018f,
                        GalaxyIndex = g,
                        ColorIndex = 1
                    });
                }
            }

            // 4. The intergalactic bridge: a loose cloud filling the space between galaxies.
            for (int i = 0; i < 420; i++)
            {
                float radius = 6f + Cbrt(random.Next()) * 30f;
                float theta = random.Next() * Tau;
                float phi = Mathf.Acos(random.Next() * 2f - 1f);
                halo.Add(new StrategicSpacePoint
                {
                    Position = universeCenter + new Vector3(
                        radius * Mathf.Sin(phi) * Mathf.Cos(theta),
                        radius * Mathf.Cos(phi),
                        radius * Mathf.Sin(phi) * Mathf.Sin(theta)),
                    Alpha = 0.07f + random.Next() * 0.16f,
                    Size = 0.012f + random.Next() * 0.016f,
                    GalaxyIndex = 1,
                    ColorIndex = 1
                });
            }

            // 5. Globular clusters from the snapshot become nebula clouds — no draws consumed.
            for (int i = 0; i < snapshot.clusters.Count; i++)
            {
                StrategicMapUniverseSnapshot.ClusterRow cluster = snapshot.clusters[i];
                nebulae.Add(new StrategicNebulaCloud
                {
                    Position = cluster.Position,
                    Radius = cluster.radius * 4.5f,
                    Squash = 1f,
                    Rotation = 0f,
                    Alpha = 0.028f,
                    GalaxyIndex = cluster.galaxyIndex,
                    ColorIndex = 0
                });
            }

            // 6. Nebula clouds per galaxy, seated on the spiral arms. The eight draws per
            //    cloud must happen here in this order or every dust-lane point below shifts.
            const float NebulaMaxTheta = Mathf.PI * 4.6f;
            for (int g = 0; g < snapshot.galaxies.Count; g++)
            {
                StrategicMapUniverseSnapshot.GalaxyRow galaxy = snapshot.galaxies[g];
                float nebulaBulge = galaxy.radius * 0.13f;
                float nebulaR0 = nebulaBulge * 0.6f;
                float nebulaSpiralB = Mathf.Log(galaxy.radius / nebulaR0) / NebulaMaxTheta;
                int nebulaCount = g < NebulaCounts.Length ? NebulaCounts[g] : 6;

                for (int i = 0; i < nebulaCount; i++)
                {
                    int arm = i % StrategicMapUniverseSnapshot.GalaxyRow.Arms;
                    float t = 0.18f + Mathf.Sqrt(random.Next()) * 0.78f;
                    float theta = t * NebulaMaxTheta;
                    float radius = nebulaR0 * Mathf.Exp(nebulaSpiralB * theta);
                    float angle = theta + galaxy.phase + arm * Tau / StrategicMapUniverseSnapshot.GalaxyRow.Arms;

                    nebulae.Add(new StrategicNebulaCloud
                    {
                        Position = galaxy.Center + new Vector3(
                            Mathf.Cos(angle) * radius,
                            (random.Next() - 0.5f) * galaxy.radius * 0.16f,
                            Mathf.Sin(angle) * radius),
                        Radius = galaxy.radius * (0.16f + random.Next() * 0.3f),
                        Squash = 0.34f + random.Next() * 0.5f,
                        Rotation = random.Next() * Tau,
                        Alpha = 0.012f + random.Next() * 0.022f,
                        GalaxyIndex = g,
                        ColorIndex = 0
                    });
                    random.Next(); // phase
                    random.Next(); // drift
                }
            }

            // 7. Dust lanes, offset 0.055 rad ahead of their arm so they read as a dark edge.
            const float MaxTheta = Mathf.PI * 4.6f;
            for (int g = 0; g < snapshot.galaxies.Count; g++)
            {
                StrategicMapUniverseSnapshot.GalaxyRow galaxy = snapshot.galaxies[g];
                float bulgeRadius = galaxy.radius * 0.13f;
                float r0 = bulgeRadius * 0.6f;
                float spiralB = Mathf.Log(galaxy.radius / r0) / MaxTheta;
                int dustCount = Mathf.RoundToInt(920f * Mathf.Max(0.34f, galaxy.radius / 10f));

                for (int i = 0; i < dustCount; i++)
                {
                    int arm = Mathf.FloorToInt(random.Next() * StrategicMapUniverseSnapshot.GalaxyRow.Arms);
                    float t = Mathf.Sqrt(random.Next());
                    float theta = t * MaxTheta;
                    float radius = r0 * Mathf.Exp(spiralB * theta);
                    float angle = theta + galaxy.phase + arm * Tau / StrategicMapUniverseSnapshot.GalaxyRow.Arms + 0.055f;
                    float spread = galaxy.radius * 0.0025f + radius * 0.022f;

                    dust.Add(new StrategicSpacePoint
                    {
                        Position = galaxy.Center + new Vector3(
                            Mathf.Cos(angle) * radius + Gaussian(random) * spread,
                            Gaussian(random) * (galaxy.radius * 0.0014f + radius * 0.011f),
                            Mathf.Sin(angle) * radius + Gaussian(random) * spread),
                        Alpha = 0.08f + random.Next() * 0.19f,
                        Size = 0.8f + random.Next() * 2.2f,
                        GalaxyIndex = g,
                        ColorIndex = 0
                    });
                }
            }

            // 8. Foreground dust: 128 motes on a wide ring around the universe centre, close
            //    enough to the camera to drift across the frame. Six draws each.
            var foreground = new List<StrategicSpacePoint>(128);
            for (int i = 0; i < 128; i++)
            {
                float angle = random.Next() * Tau;
                float radius = 18f + random.Next() * 46f;
                float height = (random.Next() - 0.5f) * 28f;
                float alpha = 0.035f + random.Next() * 0.12f;
                float size = 0.8f + random.Next() * 2.8f;
                float phase = random.Next() * Tau;

                foreground.Add(new StrategicSpacePoint
                {
                    Position = snapshot.UniverseCenter + new Vector3(
                        Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius),
                    Alpha = alpha,
                    Size = size,
                    // The reference tints every third mote cyan and the rest near-white, by
                    // index rather than by any property of the mote itself.
                    ColorIndex = i % 3 == 0 ? 1 : 0,
                    Phase = phase
                });
            }

            cachedFor = snapshot;
            cachedForegroundDust = foreground.ToArray();
            cachedHalo = halo.ToArray();
            cachedDust = dust.ToArray();
            cachedNebulae = nebulae.ToArray();
        }

        /// <summary>Box-Muller, exactly as the reference's <c>gaussian(random)</c> — two draws per sample.</summary>
        private static float Gaussian(Mulberry32 random)
        {
            float a = Mathf.Max(0.000001f, random.Next());
            float b = Mathf.Max(0.000001f, random.Next());
            return Mathf.Sqrt(-2f * Mathf.Log(a)) * Mathf.Cos(Tau * b);
        }

        private static float Cbrt(float value) => Mathf.Pow(Mathf.Max(0f, value), 1f / 3f);

        public static void ClearCache() { cachedHalo = null; cachedDust = null; cachedNebulae = null; cachedFor = null; }
    }
}
