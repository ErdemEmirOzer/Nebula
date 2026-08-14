using System;
using UnityEngine;

namespace Nebula.World.Composition
{
    /// <summary>
    /// Immutable, validated snapshot of every value the scene adapter
    /// (<see cref="SolarSystemBootstrap"/>) exposes in the Inspector today.
    /// Built once per session start from the adapter's serialized fields —
    /// never itself a Unity asset/MonoBehaviour, so it is fully constructible
    /// and testable from pure EditMode code with no scene involved.
    /// </summary>
    public sealed class NebulaBootstrapConfig
    {
        /// <summary>0 = pick a fresh random seed this session; any other value reproduces the same universe. Resolution happens exactly once, in <see cref="NebulaBootstrap.StartSession"/> — never here.</summary>
        public int Seed;
        public float GravitationalConstant;

        public int StarsPerArm;
        public Vector2 ArmStarRadiusRange;

        public string GalaxyName;
        public Vector3 GalaxyCenter;
        public float GalaxyRadius;
        public int GalaxyParticleCount;
        public int GalaxyArmCount;
        public float GalaxyRotationSpeedDegPerSec;
        public float BlackHoleRadius;

        public struct CompanionGalaxy
        {
            public string Name;
            public float Scale;
            public Vector3 Center;
            public float CloudWhiteness;
        }

        /// <summary>Copied by value from the adapter's own serialized struct array — never a shared reference, so nothing here can mutate Inspector data.</summary>
        public CompanionGalaxy[] CompanionGalaxies = Array.Empty<CompanionGalaxy>();

        public int HaloStarsPerGalaxy;
        public int ClustersPerGalaxy;
        public int BridgeStarCount;

        public int AnomalyCount;

        public bool ShowOrbitRings;
        public bool ShowPlanetTrails;
        public int StarfieldStarCount;
        public float StarfieldRadius;
        public int NebulaCount;

        /// <summary>
        /// Fail-fast field validation — must run before any world mutation
        /// starts (no static reset, no Random.InitState, nothing). Throws a
        /// descriptive <see cref="InvalidOperationException"/> naming the
        /// offending field on the first violation found; never silently
        /// clamps or substitutes a default. Seed == 0 is a valid sentinel
        /// ("pick a random one this session") and is intentionally NOT
        /// rejected here.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(GalaxyName))
                throw new InvalidOperationException("NebulaBootstrapConfig.GalaxyName must not be empty.");
            if (GravitationalConstant <= 0f)
                throw new InvalidOperationException($"NebulaBootstrapConfig.GravitationalConstant must be positive, was {GravitationalConstant}.");
            if (GalaxyRadius <= 0f)
                throw new InvalidOperationException($"NebulaBootstrapConfig.GalaxyRadius must be positive, was {GalaxyRadius}.");
            if (BlackHoleRadius <= 0f)
                throw new InvalidOperationException($"NebulaBootstrapConfig.BlackHoleRadius must be positive, was {BlackHoleRadius}.");
            if (ArmStarRadiusRange.x <= 0f || ArmStarRadiusRange.y <= 0f)
                throw new InvalidOperationException($"NebulaBootstrapConfig.ArmStarRadiusRange must be positive, was {ArmStarRadiusRange}.");
            if (ArmStarRadiusRange.x > ArmStarRadiusRange.y)
                throw new InvalidOperationException($"NebulaBootstrapConfig.ArmStarRadiusRange.x must not exceed .y, was {ArmStarRadiusRange}.");
            if (StarfieldRadius <= 0f)
                throw new InvalidOperationException($"NebulaBootstrapConfig.StarfieldRadius must be positive, was {StarfieldRadius}.");

            RequireCountNotNegative(StarsPerArm, nameof(StarsPerArm));
            RequireCountNotNegative(GalaxyParticleCount, nameof(GalaxyParticleCount));
            RequireCountNotNegative(GalaxyArmCount, nameof(GalaxyArmCount));
            RequireCountNotNegative(HaloStarsPerGalaxy, nameof(HaloStarsPerGalaxy));
            RequireCountNotNegative(ClustersPerGalaxy, nameof(ClustersPerGalaxy));
            RequireCountNotNegative(BridgeStarCount, nameof(BridgeStarCount));
            RequireCountNotNegative(AnomalyCount, nameof(AnomalyCount));
            RequireCountNotNegative(StarfieldStarCount, nameof(StarfieldStarCount));
            RequireCountNotNegative(NebulaCount, nameof(NebulaCount));

            if (CompanionGalaxies != null)
            {
                for (int i = 0; i < CompanionGalaxies.Length; i++)
                {
                    CompanionGalaxy c = CompanionGalaxies[i];
                    if (c.Scale < 0f)
                        throw new InvalidOperationException($"NebulaBootstrapConfig.CompanionGalaxies[{i}].Scale must not be negative, was {c.Scale}.");
                    // scale <= 0 means "disabled" (the original bootstrap's own `continue` skip) —
                    // an inert entry's name is never used, so it is exempt from the name check.
                    if (c.Scale > 0f && string.IsNullOrWhiteSpace(c.Name))
                        throw new InvalidOperationException($"NebulaBootstrapConfig.CompanionGalaxies[{i}].Name must not be empty for an active (Scale > 0) companion.");
                }
            }
        }

        private static void RequireCountNotNegative(int value, string fieldName)
        {
            if (value < 0) throw new InvalidOperationException($"NebulaBootstrapConfig.{fieldName} must not be negative, was {value}.");
        }
    }
}
