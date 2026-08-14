using Nebula.Extraction;

namespace Nebula.Sectors
{
    /// <summary>
    /// Deterministic, immutable description of one planet. Carries EXACTLY
    /// one <see cref="ResourceFamily"/> — never a list, never a secondary
    /// field (VS-008 hard rule). Orbit fields are enough for a materializer
    /// to reproduce the exact same physical orbit every time, without
    /// depending on UnityEngine.Random call order. <see cref="Environment"/>
    /// (VS-009 §3) is generated on its own independent hash channel, never
    /// coupled to Family/Rarity. <see cref="SurfaceLayoutSeed"/> is the sole
    /// input (together with a canonical logical slot id) for every node
    /// direction on this planet (VS-009 §5) — never free placement.
    /// </summary>
    public sealed class PlanetManifest
    {
        public int PlanetIndex { get; }
        public string PlanetId { get; }
        public ResourceFamily Family { get; }
        public Rarity Rarity { get; }

        public float OrbitRadius { get; }
        public float OrbitInclinationDegrees { get; }
        public float AscendingNodeDegrees { get; }
        public float StartAngleDegrees { get; }
        public float PlanetRadius { get; }

        public bool RecommendedStart { get; }

        public EnvironmentClass Environment { get; }
        public ulong SurfaceLayoutSeed { get; }

        public PlanetManifest(int planetIndex, string planetId, ResourceFamily family, Rarity rarity,
            float orbitRadius, float orbitInclinationDegrees, float ascendingNodeDegrees, float startAngleDegrees,
            float planetRadius, bool recommendedStart, EnvironmentClass environment, ulong surfaceLayoutSeed)
        {
            PlanetIndex = planetIndex; PlanetId = planetId; Family = family; Rarity = rarity;
            OrbitRadius = orbitRadius; OrbitInclinationDegrees = orbitInclinationDegrees;
            AscendingNodeDegrees = ascendingNodeDegrees; StartAngleDegrees = startAngleDegrees;
            PlanetRadius = planetRadius; RecommendedStart = recommendedStart;
            Environment = environment; SurfaceLayoutSeed = surfaceLayoutSeed;
        }

        /// <summary>Returns a copy with Family/Rarity overridden — used by SectorManifestGenerator's start-planet clamp and FrontierGuaranteePass's family-coverage fixups.</summary>
        public PlanetManifest WithFrozenResource(ResourceFamily family, Rarity rarity) => new PlanetManifest(
            PlanetIndex, PlanetId, family, rarity, OrbitRadius, OrbitInclinationDegrees, AscendingNodeDegrees,
            StartAngleDegrees, PlanetRadius, RecommendedStart, Environment, SurfaceLayoutSeed);

        /// <summary>Returns a copy with Environment overridden — used by SectorManifestGenerator's start-planet Calm clamp and FrontierGuaranteePass's environment-coverage fixup. Never touches Family/Rarity/position.</summary>
        public PlanetManifest WithEnvironment(EnvironmentClass environment) => new PlanetManifest(
            PlanetIndex, PlanetId, Family, Rarity, OrbitRadius, OrbitInclinationDegrees, AscendingNodeDegrees,
            StartAngleDegrees, PlanetRadius, RecommendedStart, environment, SurfaceLayoutSeed);
    }
}
