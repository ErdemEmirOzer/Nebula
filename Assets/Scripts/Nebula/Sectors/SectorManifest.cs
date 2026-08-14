using System.Collections.Generic;

namespace Nebula.Sectors
{
    /// <summary>
    /// Deterministic, immutable set of the real systems belonging to one sector — however
    /// many the galaxy actually places there, including none.
    /// <para>
    /// The count is not a property of the sector. Systems come from
    /// <see cref="Nebula.World.GalaxyStarField"/>, which strings about 128 stars along the
    /// spiral arms of four galaxies across millions of units on every axis. Most sectors
    /// therefore hold <b>zero</b> systems; the campaign start sector holds one to three.
    /// </para>
    /// <para>
    /// This previously read "4-7 real systems belonging to one sector cell (VS-008 §1)",
    /// from the sub-cell model that <c>SectorManifestGenerator.ChooseCells</c> implemented
    /// before the star field replaced it. Code written against that sentence assumed a
    /// sector always yields systems and that system index 1 exists — neither is true, and
    /// both assumptions cost real time before the sentence was corrected.
    /// </para>
    /// </summary>
    public sealed class SectorManifest
    {
        public SectorCoordinate Coordinate { get; }
        public int GenerationVersion { get; }

        private readonly List<SystemManifest> systems;
        public IReadOnlyList<SystemManifest> Systems => systems;

        public SectorManifest(SectorCoordinate coordinate, int generationVersion, IReadOnlyList<SystemManifest> systemList)
        {
            Coordinate = coordinate;
            GenerationVersion = generationVersion;
            systems = new List<SystemManifest>(systemList);
        }

        public SystemManifest FindSystem(string systemId)
        {
            for (int i = 0; i < systems.Count; i++) if (systems[i].SystemId == systemId) return systems[i];
            return null;
        }
    }
}
