using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Sectors
{
    /// <summary>
    /// Internal-only generation profile (VS-008 §2). Never surfaced to the
    /// player directly — system labels shown in UI must come from explored
    /// real content, not this tag (see NavigationScreen).
    /// </summary>
    public enum SystemProfileKind { Balanced, CompactFrontier, ResourceCluster, RichFrontier }

    /// <summary>
    /// Deterministic, immutable description of one real navigable system —
    /// the manifest-era replacement for a live GameObject as the source of
    /// truth. AbsoluteCenter is authoritative for every distance/route/ETA
    /// calculation; a live Transform (once materialized) is only a rendered
    /// view of it. Always reproducible from (seed, GenerationVersion,
    /// SectorCoordinate) alone — there is no frozen/persisted variant.
    /// </summary>
    public sealed class SystemManifest
    {
        public string SystemId { get; }
        public SectorCoordinate Sector { get; }
        public int SystemIndex { get; }
        public string DisplayName { get; }
        public StarClass StarClass { get; }
        public Vector3 AbsoluteCenter { get; }
        public float NavigationRadius { get; }
        public float StarRadius { get; }
        public bool IsStartSystem { get; }
        public SystemProfileKind ProfileKind { get; }
        public int GenerationVersion { get; }

        private readonly List<PlanetManifest> planets;
        public IReadOnlyList<PlanetManifest> Planets => planets;

        public SystemManifest(string systemId, SectorCoordinate sector, int systemIndex, string displayName,
            StarClass starClass, Vector3 absoluteCenter, float navigationRadius, float starRadius, bool isStartSystem,
            SystemProfileKind profileKind, int generationVersion, IReadOnlyList<PlanetManifest> planetList)
        {
            SystemId = systemId; Sector = sector; SystemIndex = systemIndex; DisplayName = displayName;
            StarClass = starClass; AbsoluteCenter = absoluteCenter; NavigationRadius = navigationRadius;
            StarRadius = starRadius; IsStartSystem = isStartSystem; ProfileKind = profileKind;
            GenerationVersion = generationVersion;
            planets = new List<PlanetManifest>(planetList);
        }

        public PlanetManifest FindPlanet(int planetIndex)
        {
            for (int i = 0; i < planets.Count; i++) if (planets[i].PlanetIndex == planetIndex) return planets[i];
            return null;
        }

        /// <summary>Returns a copy with one planet replaced — used by FrontierGuaranteePass's family/environment-coverage fixups.</summary>
        public SystemManifest WithReplacedPlanet(PlanetManifest replacement)
        {
            var copy = new List<PlanetManifest>(planets);
            for (int i = 0; i < copy.Count; i++)
                if (copy[i].PlanetIndex == replacement.PlanetIndex) { copy[i] = replacement; break; }
            return new SystemManifest(SystemId, Sector, SystemIndex, DisplayName, StarClass, AbsoluteCenter,
                NavigationRadius, StarRadius, IsStartSystem, ProfileKind, GenerationVersion, copy);
        }

        /// <summary>Returns a copy repositioned within the same sector — used only by FrontierGuaranteePass's nearest-neighbor-distance fix.</summary>
        public SystemManifest WithCenter(Vector3 newCenter) => new SystemManifest(SystemId, Sector, SystemIndex,
            DisplayName, StarClass, newCenter, NavigationRadius, StarRadius, IsStartSystem, ProfileKind, GenerationVersion, planets);

        /// <summary>Returns a copy with a forced profile/planet list (a fresh deterministic BuildPlanets result) and recomputed navigation radius — used only by FrontierGuaranteePass.</summary>
        public SystemManifest WithForcedComposition(SystemProfileKind profile, IReadOnlyList<PlanetManifest> newPlanets, float navigationRadius) =>
            new SystemManifest(SystemId, Sector, SystemIndex, DisplayName, StarClass, AbsoluteCenter, navigationRadius,
                StarRadius, IsStartSystem, profile, GenerationVersion, newPlanets);

        /// <summary>Returns a copy with only StarClass overridden — used by FrontierGuaranteePass's star-class guarantee fixup. Never touches position/composition/family/rarity.</summary>
        public SystemManifest WithStarClass(StarClass newClass) => new SystemManifest(SystemId, Sector, SystemIndex,
            DisplayName, newClass, AbsoluteCenter, NavigationRadius, StarRadius, IsStartSystem, ProfileKind, GenerationVersion, planets);
    }
}
