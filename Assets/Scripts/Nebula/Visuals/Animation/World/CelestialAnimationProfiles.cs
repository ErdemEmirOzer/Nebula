using UnityEngine;
using Nebula.Celestial;
using Nebula.Sectors;

namespace Nebula.Visuals
{
    /// <summary>How a celestial body moves. Amplitudes and rates only — never a position.</summary>
    public readonly struct CelestialMotion
    {
        /// <summary>Degrees per second about the body's own axis.</summary>
        public readonly float SpinDegreesPerSecond;

        /// <summary>Texture offset per second, for surfaces whose shader supports it.</summary>
        public readonly float SurfaceFlowPerSecond;

        /// <summary>Degrees per second for a secondary layer — a corona, a disk, an atmosphere.</summary>
        public readonly float SecondaryDegreesPerSecond;

        public CelestialMotion(float spin, float flow, float secondary)
        {
            SpinDegreesPerSecond = spin;
            SurfaceFlowPerSecond = flow;
            SecondaryDegreesPerSecond = secondary;
        }

        public bool IsStill => Mathf.Approximately(SpinDegreesPerSecond, 0f)
                               && Mathf.Approximately(SurfaceFlowPerSecond, 0f)
                               && Mathf.Approximately(SecondaryDegreesPerSecond, 0f);
    }

    /// <summary>
    /// The character of every celestial family, as rates rather than poses.
    /// <para>
    /// Everything here is slow on purpose. A star is a star because it barely
    /// appears to move; a blue giant that spins like a toy reads as a prop. The
    /// gameplay body — its orbit, its position, its radius, its hazard band and
    /// its energy — is owned by <see cref="CelestialBody"/> and the sector
    /// system, and nothing in this file can reach any of it.
    /// </para>
    /// </summary>
    public static class CelestialAnimationProfiles
    {
        /// <summary>
        /// A star's surface and corona. Mass reads as slowness: the blue giant
        /// is the largest and therefore the most ponderous of the four.
        /// </summary>
        public static CelestialMotion Star(StarClass starClass)
        {
            switch (starClass)
            {
                case StarClass.RedDwarf: return new CelestialMotion(0.6f, 0.004f, 0.9f);
                case StarClass.Yellow: return new CelestialMotion(0.45f, 0.0035f, 0.7f);
                case StarClass.White: return new CelestialMotion(0.35f, 0.003f, 0.55f);
                case StarClass.BlueGiant: return new CelestialMotion(0.18f, 0.002f, 0.3f);
                default: return new CelestialMotion(0.4f, 0.003f, 0.6f);
            }
        }

        /// <summary>
        /// A planet's own turn. Metal is dead weight; a gas envelope flows
        /// across itself; a crystal shell turns slowly enough to catch light.
        /// </summary>
        public static CelestialMotion Planet(PlanetArchetype archetype)
        {
            switch (archetype)
            {
                case PlanetArchetype.MetallicRocky: return new CelestialMotion(0.8f, 0f, 0f);
                case PlanetArchetype.Biomass: return new CelestialMotion(1.2f, 0.002f, 0.4f);
                case PlanetArchetype.UpperAtmospherePlatform: return new CelestialMotion(0.9f, 0.012f, 0.6f);
                case PlanetArchetype.FloatingOceanic: return new CelestialMotion(1.0f, 0.006f, 0.25f);
                case PlanetArchetype.CrystalShell: return new CelestialMotion(0.7f, 0f, 0.2f);
                default: return new CelestialMotion(0.9f, 0f, 0f);
            }
        }

        /// <summary>
        /// A black hole. The core and the event horizon are perfectly still and
        /// perfectly black; only the disk and the photon ring turn, and heavily.
        /// </summary>
        public static CelestialMotion BlackHoleDisk => new CelestialMotion(0f, 0f, 3.5f);

        /// <summary>The core never turns. Reading this as a rule rather than a magic zero.</summary>
        public static CelestialMotion BlackHoleCore => new CelestialMotion(0f, 0f, 0f);

        /// <summary>
        /// An asteroid's tumble, derived from its own stable id so the same rock
        /// always turns the same way after a reload.
        /// </summary>
        public static CelestialMotion Asteroid(string bodyId)
        {
            float unit = DeterministicUnit01(bodyId);
            // Slow, and never fast enough to read as spin rather than drift.
            return new CelestialMotion(0.5f + unit * 1.5f, 0f, 0f);
        }

        /// <summary>An asteroid's tumble axis, stable across a reload for the same id.</summary>
        public static Vector3 AsteroidAxis(string bodyId)
        {
            if (string.IsNullOrEmpty(bodyId)) return Vector3.up;

            unchecked
            {
                int hash = 23;
                for (int i = 0; i < bodyId.Length; i++) hash = hash * 31 + bodyId[i];

                var axis = new Vector3(
                    ((hash & 0xFF) / 255f) * 2f - 1f,
                    (((hash >> 8) & 0xFF) / 255f) * 2f - 1f,
                    (((hash >> 16) & 0xFF) / 255f) * 2f - 1f);
                return axis.sqrMagnitude < 1e-4f ? Vector3.up : axis.normalized;
            }
        }

        /// <summary>A stable 0..1 from an id. The same id always gives the same number.</summary>
        public static float DeterministicUnit01(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0f;
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < id.Length; i++) hash = hash * 31 + id[i];
                return ((hash >> 4) & 0xFFFF) / 65535f;
            }
        }

        /// <summary>
        /// The environment only changes how much, never how long. A hostile
        /// world looks more agitated; its extraction rates and hazard timers are
        /// somebody else's and are not read here.
        /// </summary>
        public static float EnvironmentAmplitude(EnvironmentClass environment)
        {
            switch (environment)
            {
                case EnvironmentClass.Calm: return 1f;
                case EnvironmentClass.Harsh: return 1.15f;
                case EnvironmentClass.Extreme: return 1.3f;
                default: return 1f;
            }
        }

        /// <summary>
        /// The motion for whatever kind of body this is. A body type with no
        /// authored character stands perfectly still rather than guessing.
        /// </summary>
        public static CelestialMotion ForBodyType(CelestialBodyType type, StarClass starClass,
            PlanetArchetype archetype, string bodyId)
        {
            switch (type)
            {
                case CelestialBodyType.Star: return Star(starClass);
                case CelestialBodyType.Planet: return Planet(archetype);
                case CelestialBodyType.BlackHole: return BlackHoleDisk;
                case CelestialBodyType.Asteroid: return Asteroid(bodyId);
                default: return new CelestialMotion(0f, 0f, 0f);
            }
        }

        /// <summary>
        /// The spin angle at a given age. A pure function, so a demote, a
        /// promote and a reload all land on the same angle without the animation
        /// ever having to catch up — and without a byte in the save.
        /// </summary>
        public static float SpinDegrees(float degreesPerSecond, float ageSeconds)
        {
            if (AnimationAccessibility.MotionOff) return 0f;
            float degrees = degreesPerSecond * ageSeconds * AnimationAccessibility.MotionScale;
            return Mathf.Repeat(degrees, 360f);
        }

        /// <summary>
        /// Photosensitivity damping for the busiest bodies. A black hole's disk
        /// and a hazard rim are the two places fast contrast could appear.
        /// </summary>
        public static float PhotosensitivityScale
            => LightingAccessibility.PhotosensitivitySafeMode
                ? LightingAccessibility.PhotosensitivityFlashFactor
                : LightingAccessibility.FlashReduction
                    ? LightingAccessibility.FlashReductionFactor
                    : 1f;
    }
}
