using UnityEngine;
using Nebula.Sectors;

namespace Nebula.Visuals
{
    /// <summary>
    /// The finished lighting response for one celestial surface: what actually
    /// goes into the shader.
    /// </summary>
    public readonly struct CelestialSurfaceResponse
    {
        public readonly float Smoothness;
        public readonly float Metallic;
        public readonly Color Emission;

        public CelestialSurfaceResponse(float smoothness, float metallic, Color emission)
        {
            Smoothness = smoothness;
            Metallic = metallic;
            Emission = emission;
        }
    }

    /// <summary>
    /// Combines the planet family, its environment and the active quality tier
    /// into one surface response. A pure function of its inputs — same
    /// arguments, same answer, no <see cref="Random"/>, no time, no scene
    /// state — so a system re-materialized after a Full → Warm → Full round
    /// trip comes back looking exactly as it did.
    /// </summary>
    public static class CelestialLightingResolver
    {
        /// <summary>How much of the authored specular each tier pays for.</summary>
        private static float SmoothnessScale(LightingQuality quality) => quality switch
        {
            LightingQuality.Low => 0.70f,     // simplest response
            LightingQuality.High => 1.10f,    // richer, still bounded
            _ => 1f                            // Medium is the target look
        };

        /// <summary>Low deliberately dims hazard rim and character emission along with everything else.</summary>
        private static float EmissionScale(LightingQuality quality) => quality switch
        {
            LightingQuality.Low => 0.55f,
            LightingQuality.High => 1.15f,
            _ => 1f
        };

        public static CelestialSurfaceResponse Resolve(PlanetArchetype archetype, EnvironmentClass environment, LightingQuality quality)
        {
            PlanetSurfaceProfile planet = CelestialLightingProfiles.ForPlanet(archetype);
            EnvironmentLightProfile env = CelestialLightingProfiles.ForEnvironment(environment);

            float smoothness = Mathf.Clamp(
                planet.Smoothness * env.ContrastScale * SmoothnessScale(quality),
                0f, CelestialLightingProfiles.MaxSurfaceSmoothness);

            // Metallic is a material fact about the family, not a dial: an
            // environment does not make a world more or less metallic.
            float metallic = Mathf.Clamp01(planet.Metallic);

            Color emission = planet.Emission * EmissionScale(quality);

            // The hazard rim is static, but Flash Reduction and Photosensitivity
            // Safe Mode still pull its intensity down — a permanently bright
            // danger edge is exactly what those settings exist to soften.
            if (env.HazardRim > 0f)
            {
                float rim = LightingAccessibility.ScaleFlashIntensity(env.HazardRim) * EmissionScale(quality);
                emission += env.Tint * rim * 0.25f;
            }

            emission = ClampEmission(emission);
            return new CelestialSurfaceResponse(smoothness, metallic, emission);
        }

        /// <summary>
        /// The white-clipping guard. Scales a colour down until its brightest
        /// channel sits under the safe ceiling, preserving hue — clamping each
        /// channel independently would drag every bright colour toward white,
        /// which is the exact failure this is here to prevent.
        /// </summary>
        public static Color ClampEmission(Color emission)
        {
            float peak = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
            if (peak > CelestialLightingProfiles.MaxSafeEmissionLuminance && peak > 0f)
            {
                float scale = CelestialLightingProfiles.MaxSafeEmissionLuminance / peak;
                emission = new Color(emission.r * scale, emission.g * scale, emission.b * scale);
            }
            emission.a = 1f;
            return emission;
        }

        /// <summary>
        /// The key light values for a star, folded together: the class profile
        /// scales the intensity and the reach the system manifest asked for.
        /// </summary>
        public static (Color color, float intensityScale, float range) ResolveStarKeyLight(StarClass starClass, float manifestRange)
        {
            StarLightProfile profile = CelestialLightingProfiles.ForStar(starClass);
            return (profile.KeyLightColor, profile.IntensityScale, manifestRange * profile.RangeMultiplier);
        }
    }
}
