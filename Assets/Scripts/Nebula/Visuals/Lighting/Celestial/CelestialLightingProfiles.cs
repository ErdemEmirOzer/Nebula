using UnityEngine;
using Nebula.Sectors;

namespace Nebula.Visuals
{
    /// <summary>
    /// How one star class lights the system it sits in. Presentation only: the
    /// gameplay meaning of a star class — weights, energy multiplier, hazard
    /// radii — stays in <see cref="StarClassCatalog"/> and is never copied
    /// here.
    /// </summary>
    public readonly struct StarLightProfile
    {
        public readonly StarClass StarClass;

        /// <summary>Colour the one global key light takes. This is the whole difference between a red dwarf's system and a blue giant's.</summary>
        public readonly Color KeyLightColor;

        /// <summary>Multiplies <see cref="LightingRig.StarIntensity"/>. Kept inside a band that cannot blow surfaces to white.</summary>
        public readonly float IntensityScale;

        /// <summary>Multiplies the reach the star manifest asked for — a blue giant lights its system much further out than a dwarf.</summary>
        public readonly float RangeMultiplier;

        /// <summary>How much the corona/halo adds on top. Feeds the emissive read, never a second light.</summary>
        public readonly float CoronaBoost;

        /// <summary>
        /// Pushes lit surfaces toward a tighter, harder highlight. A blue giant
        /// throws a sharp specular; a red dwarf's is soft and diffuse.
        /// </summary>
        public readonly float SpecularSharpness;

        /// <summary>How strongly the hazard rim reads in this star's light, so a danger zone stays legible under a dim red sun.</summary>
        public readonly float HazardReadability;

        public StarLightProfile(StarClass starClass, Color keyLightColor, float intensityScale, float rangeMultiplier,
            float coronaBoost, float specularSharpness, float hazardReadability)
        {
            StarClass = starClass;
            KeyLightColor = keyLightColor;
            IntensityScale = intensityScale;
            RangeMultiplier = rangeMultiplier;
            CoronaBoost = coronaBoost;
            SpecularSharpness = specularSharpness;
            HazardReadability = hazardReadability;
        }
    }

    /// <summary>
    /// How one planet family answers light. Only lighting response — never
    /// albedo or texture, which the authored visuals own and which this part is
    /// explicitly not allowed to restyle.
    /// </summary>
    public readonly struct PlanetSurfaceProfile
    {
        public readonly PlanetArchetype Archetype;
        public readonly float Smoothness;
        public readonly float Metallic;

        /// <summary>Chromatic character emission. Deliberately dim — see <see cref="CelestialLightingProfiles.MaxSafeEmissionLuminance"/>.</summary>
        public readonly Color Emission;

        /// <summary>
        /// How much of the ambient floor this family keeps on its unlit side.
        /// Above zero so the dark side is a readable silhouette instead of a
        /// void — the alternative would be a second fill light, which the one
        /// key light contract forbids.
        /// </summary>
        public readonly float DarkSideFloor;

        public PlanetSurfaceProfile(PlanetArchetype archetype, float smoothness, float metallic, Color emission, float darkSideFloor)
        {
            Archetype = archetype;
            Smoothness = smoothness;
            Metallic = metallic;
            Emission = emission;
            DarkSideFloor = darkSideFloor;
        }
    }

    /// <summary>
    /// What an environment class does to a surface's look. Visual only: the
    /// duration/risk meaning of Calm/Harsh/Extreme lives in
    /// <see cref="EnvironmentClassCatalog"/> and is untouched.
    /// </summary>
    public readonly struct EnvironmentLightProfile
    {
        public readonly EnvironmentClass Environment;

        /// <summary>Scales specular response, widening the gap between lit and unlit as conditions worsen.</summary>
        public readonly float ContrastScale;

        /// <summary>A faint tint the surface picks up. Never strong enough to hide the family's own colour.</summary>
        public readonly Color Tint;

        /// <summary>Static hazard rim strength. Never animated, never flashing.</summary>
        public readonly float HazardRim;

        public EnvironmentLightProfile(EnvironmentClass environment, float contrastScale, Color tint, float hazardRim)
        {
            Environment = environment;
            ContrastScale = contrastScale;
            Tint = tint;
            HazardRim = hazardRim;
        }
    }

    /// <summary>
    /// The presentation tables for every celestial class the game actually has:
    /// four star classes, five planet archetypes, three environment classes,
    /// resolved from the typed enums rather than from names or indices.
    /// </summary>
    public static class CelestialLightingProfiles
    {
        /// <summary>
        /// Ceiling on any emission colour's brightest channel. Nothing in this
        /// file may cross it — that is the line between "a crystal world has
        /// character" and a white sheet where the planet used to be.
        /// </summary>
        public const float MaxSafeEmissionLuminance = 0.35f;

        /// <summary>Smoothness ceiling. Above this a planet reads as wet plastic or bare glass rather than as a world.</summary>
        public const float MaxSurfaceSmoothness = 0.75f;

        // ---- stars ------------------------------------------------------

        private static readonly StarLightProfile[] Stars =
        {
            // Warm and dark: dim, close-in light with a soft, diffuse highlight.
            new StarLightProfile(StarClass.RedDwarf,
                keyLightColor: new Color(1f, 0.46f, 0.26f), intensityScale: 0.80f, rangeMultiplier: 0.80f,
                coronaBoost: 0.85f, specularSharpness: 0.55f, hazardReadability: 1.25f),

            // The natural reference the other three are read against.
            new StarLightProfile(StarClass.Yellow,
                keyLightColor: new Color(1f, 0.93f, 0.78f), intensityScale: 1f, rangeMultiplier: 1f,
                coronaBoost: 1f, specularSharpness: 0.8f, hazardReadability: 1f),

            // Bright and neutral: the cleanest read of a surface's own colour.
            new StarLightProfile(StarClass.White,
                keyLightColor: new Color(0.97f, 0.97f, 1f), intensityScale: 1.12f, rangeMultiplier: 1.12f,
                coronaBoost: 1.1f, specularSharpness: 1f, hazardReadability: 0.95f),

            // Hard and cold: the furthest reach and the tightest highlight.
            new StarLightProfile(StarClass.BlueGiant,
                keyLightColor: new Color(0.68f, 0.79f, 1f), intensityScale: 1.25f, rangeMultiplier: 1.35f,
                coronaBoost: 1.25f, specularSharpness: 1.25f, hazardReadability: 0.9f)
        };

        // ---- planets ----------------------------------------------------

        private static readonly PlanetSurfaceProfile[] Planets =
        {
            // Metal — controlled cold specular over smoke-grey rock. Half
            // metallic on purpose: full metal would read as chrome.
            new PlanetSurfaceProfile(PlanetArchetype.MetallicRocky,
                smoothness: 0.34f, metallic: 0.55f, emission: Color.black, darkSideFloor: 0.16f),

            // Wood — warm, organic, low specular so relief and silhouette survive.
            new PlanetSurfaceProfile(PlanetArchetype.Biomass,
                smoothness: 0.16f, metallic: 0f, emission: new Color(0.05f, 0.03f, 0.01f), darkSideFloor: 0.20f),

            // Gas — a broad, soft highlight reads as atmosphere. Low smoothness
            // widens the highlight; the surface stays fully opaque.
            new PlanetSurfaceProfile(PlanetArchetype.UpperAtmospherePlatform,
                smoothness: 0.26f, metallic: 0f, emission: new Color(0.04f, 0.05f, 0.07f), darkSideFloor: 0.24f),

            // Water — blue/cyan reflection, held below the plastic threshold.
            new PlanetSurfaceProfile(PlanetArchetype.FloatingOceanic,
                smoothness: 0.60f, metallic: 0.05f, emission: new Color(0.02f, 0.05f, 0.07f), darkSideFloor: 0.18f),

            // Diamond — sharp but bounded. The emission is cyan-lilac and dim:
            // this family's whole failure mode is becoming a white sheet, so it
            // is the one profile with a hard ceiling on both axes.
            new PlanetSurfaceProfile(PlanetArchetype.CrystalShell,
                smoothness: 0.70f, metallic: 0.10f, emission: new Color(0.10f, 0.20f, 0.26f), darkSideFloor: 0.22f)
        };

        // ---- environments -----------------------------------------------

        private static readonly EnvironmentLightProfile[] Environments =
        {
            new EnvironmentLightProfile(EnvironmentClass.Calm,
                contrastScale: 1f, tint: Color.white, hazardRim: 0f),

            // Slightly warmer and harder, without hiding the family's colour.
            new EnvironmentLightProfile(EnvironmentClass.Harsh,
                contrastScale: 1.14f, tint: new Color(1f, 0.96f, 0.90f), hazardRim: 0.14f),

            // The hardest contrast in the game, and a static rim. Never a pulse:
            // a hazard that blinks is a hazard some players cannot look at.
            new EnvironmentLightProfile(EnvironmentClass.Extreme,
                contrastScale: 1.30f, tint: new Color(1f, 0.92f, 0.84f), hazardRim: 0.28f)
        };

        // ---- lookups ----------------------------------------------------

        public static StarLightProfile ForStar(StarClass starClass)
        {
            for (int i = 0; i < Stars.Length; i++)
                if (Stars[i].StarClass == starClass) return Stars[i];
            return Stars[1]; // Yellow — the natural reference, and a safe fallback
        }

        public static PlanetSurfaceProfile ForPlanet(PlanetArchetype archetype)
        {
            for (int i = 0; i < Planets.Length; i++)
                if (Planets[i].Archetype == archetype) return Planets[i];
            return Planets[0];
        }

        public static EnvironmentLightProfile ForEnvironment(EnvironmentClass environment)
        {
            for (int i = 0; i < Environments.Length; i++)
                if (Environments[i].Environment == environment) return Environments[i];
            return Environments[0]; // Calm
        }

        /// <summary>Every star profile, for tests and tooling. Never mutated.</summary>
        public static System.Collections.Generic.IReadOnlyList<StarLightProfile> AllStars => Stars;
        public static System.Collections.Generic.IReadOnlyList<PlanetSurfaceProfile> AllPlanets => Planets;
        public static System.Collections.Generic.IReadOnlyList<EnvironmentLightProfile> AllEnvironments => Environments;
    }
}
