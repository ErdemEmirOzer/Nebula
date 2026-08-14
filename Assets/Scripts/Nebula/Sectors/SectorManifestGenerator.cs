using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.World;

namespace Nebula.Sectors
{
    /// <summary>
    /// Deterministic placement of the real star systems inside one sector.
    ///
    /// Systems are the stars <see cref="GalaxyStarField"/> strings along the galaxies'
    /// spiral arms, so the navigable universe and the galaxies the player can see out of
    /// the window are the same thing. A sector that no arm passes through holds no systems
    /// at all — the space between the arms is empty, as it looks.
    ///
    /// This replaced a uniform 2x2x2 sub-cell grid, which spread systems evenly through
    /// every sector in the universe regardless of where the galaxies were. The separation
    /// the grid used to guarantee by construction is now enforced by the field itself, which
    /// pushes a crowded star further out along its own arm until it clears
    /// <see cref="RequiredSeparation"/>.
    /// </summary>
    public static class SectorManifestGenerator
    {
        public const int GenerationVersion = 3;
        public const float RequiredSeparation = 350000f;

        internal const int CellsPerAxis = 2; // 8 sub-cells per sector
        internal const float CellSize = SectorCoordinate.SectorSize / CellsPerAxis; // 1,000,000
        internal const float Margin = RequiredSeparation / 2f; // 175,000 — see class remarks

        private static readonly string[] NamePool =
        {
            "Rubeus", "Aurelia", "Albion", "Boreas", "Vespera", "Caldera", "Meridian", "Solace", "Thessaly", "Ondine",
            "Kestrel", "Halcyon", "Peregrine", "Marrow", "Ashwood", "Corvid", "Lumen", "Obsidian", "Petrichor", "Zephyra",
            "Amaranth", "Vantage", "Wyrmwood", "Silvane", "Duskmere", "Ferrous", "Glacia", "Hearthstone", "Ignis", "Juniper"
        };

        public static SectorManifest Generate(ulong seed, SectorCoordinate sector, bool isStartSector)
        {
            var stars = new List<GalaxyStarField.Placement>();
            GalaxyStarField.InSector(seed, sector, stars);
            var systems = new List<SystemManifest>(stars.Count);

            for (int i = 0; i < stars.Count; i++)
            {
                string systemId = WorldIdCodec.SystemId(sector, i);
                bool isStart = isStartSector && i == 0;

                Vector3 center = stars[i].AbsolutePosition;
                // VS-009 §2: the start system is always Yellow — every other
                // system rolls the typed weighted (40/35/20/5) distribution.
                StarClass starClass = isStart ? StarClass.Yellow : StarClassCatalog.Pick(seed, systemId);
                float starRadius = LerpUnit(Unit(seed, systemId, "starradius"), 5000f, 9500f);
                string displayName = DisplayNameOf(seed, systemId);

                SystemCompositionGenerator.Composition composition = isStart
                    ? SystemCompositionGenerator.ForcedBalanced(5)
                    : SystemCompositionGenerator.PickComposition(seed, systemId);

                List<PlanetManifest> planets = SystemCompositionGenerator.BuildPlanets(seed, systemId, composition, starRadius);
                if (isStart && planets.Count > 0)
                {
                    // VS-009 §3: the recommended start planet is always Calm.
                    PlanetManifest recommended = planets[0].WithFrozenResource(planets[0].Family, ClampToStartFriendly(planets[0].Rarity));
                    recommended = new PlanetManifest(recommended.PlanetIndex, recommended.PlanetId, recommended.Family, recommended.Rarity,
                        recommended.OrbitRadius, recommended.OrbitInclinationDegrees, recommended.AscendingNodeDegrees,
                        recommended.StartAngleDegrees, recommended.PlanetRadius, recommendedStart: true,
                        EnvironmentClass.Calm, recommended.SurfaceLayoutSeed);
                    planets[0] = recommended;
                }

                float outermost = planets.Count > 0 ? planets[planets.Count - 1].OrbitRadius : 40000f;
                float navigationRadius = outermost * 2.5f;

                systems.Add(new SystemManifest(systemId, sector, i, displayName, starClass, center, navigationRadius,
                    starRadius, isStart, composition.Profile, GenerationVersion, planets));
            }

            var manifest = new SectorManifest(sector, GenerationVersion, systems);
            return isStartSector ? FrontierGuaranteePass.Apply(seed, manifest) : manifest;
        }

        /// <summary>Where one real star is, and what kind it is — everything needed to DRAW it, and nothing more.</summary>
        public readonly struct StarPlacement
        {
            public readonly string SystemId;
            public readonly string DisplayName;
            public readonly Vector3 AbsoluteCenter;
            public readonly StarClass StarClass;
            public readonly float StarRadius;

            public StarPlacement(string systemId, string displayName, Vector3 absoluteCenter,
                StarClass starClass, float starRadius)
            {
                SystemId = systemId; DisplayName = displayName; AbsoluteCenter = absoluteCenter;
                StarClass = starClass; StarRadius = starRadius;
            }
        }

        /// <summary>
        /// The stars of one sector, positions and classes only.
        ///
        /// The strategic map draws every real star across whole galaxies — tens of thousands
        /// of them — and needs nothing from a star but where it is and what colour it burns.
        /// Running the full <see cref="Generate"/> for each would build planet lists, orbits,
        /// resource families, environments and surface-layout seeds that no viewer will ever
        /// read, which is the difference between a map that opens instantly and one that
        /// stalls. Identical maths to <see cref="Generate"/>, so a star enumerated here and
        /// the same star resolved fully always agree.
        ///
        /// NOT valid for the start sector, whose content passes through
        /// <see cref="FrontierGuaranteePass"/> and may be moved or reclassified afterwards —
        /// see <see cref="SectorManifestRepository.RequiresFullResolve"/>.
        /// </summary>
        public static void EnumerateStars(ulong seed, SectorCoordinate sector, List<StarPlacement> into)
        {
            if (into == null) return;
            // Read from the very field Generate places systems from, so the cheap path and
            // the full resolve can never describe different stars. It used to run its own
            // sub-cell maths, which stopped agreeing the moment placement moved to the arms.
            var stars = new List<GalaxyStarField.Placement>();
            GalaxyStarField.InSector(seed, sector, stars);

            for (int i = 0; i < stars.Count; i++)
            {
                string systemId = WorldIdCodec.SystemId(sector, i);
                into.Add(new StarPlacement(systemId, DisplayNameOf(seed, systemId),
                    stars[i].AbsolutePosition,
                    StarClassCatalog.Pick(seed, systemId),
                    LerpUnit(Unit(seed, systemId, "starradius"), 5000f, 9500f)));
            }
        }

        /// <summary>A system's name, from its own hash — shared by the full generator and the map's cheap star enumeration so the two can never disagree.</summary>
        public static string DisplayNameOf(ulong seed, string systemId) =>
            NamePool[Mathf.Abs((int)(ExtractionCatalog.Hash(seed, systemId + "/name") % (ulong)NamePool.Length))];

        // ChooseCells and CellPosition lived here until the star field replaced them.
        //
        // They implemented the old model: every sector picked 4-7 of its 8 sub-cells and
        // placed one system in each, so a sector always held systems and system index 1
        // always existed. Generate now fills a sector from GalaxyStarField instead, and
        // both methods had zero call sites -- but their doc comments still described the
        // dead model, and several test fixtures had been written against it. That cost a
        // full EditMode suite: two tests walked sectors "until 32 systems", which under
        // the real field never terminates.
        //
        // CellsPerAxis, CellSize and Margin are NOT dead and stay: FrontierGuaranteePass.BoxFor
        // still measures a cell box from them.

        private static Rarity ClampToStartFriendly(Rarity rarity) => rarity > Rarity.Uncommon ? Rarity.Uncommon : rarity;

        private static float Unit(ulong seed, string id, string tag) => ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, id + "/" + tag));
        private static float LerpUnit(float unit01, float min, float max) => min + Mathf.Clamp01(unit01) * (max - min);
    }
}
