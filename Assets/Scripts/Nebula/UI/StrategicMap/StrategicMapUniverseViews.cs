using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Sectors;
using Nebula.UI.StrategicMap.Preview;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// Builds detail-card views out of the LIVE universe the 3D map is drawing, for the
    /// systems the gameplay snapshot has no row for.
    ///
    /// The two sources cover different sets, and that mismatch was a real bug: the map draws
    /// every system in the player's 27-sector neighbourhood plus every galaxy star — some
    /// hundreds of them — while <see cref="StrategicMapDataSource"/> only builds rows for
    /// systems the knowledge service has heard of, and only fills in PLANETS for systems the
    /// player has actually visited. So clicking a world in any system but the one the player
    /// had been to selected something the card layer could not describe, and the card simply
    /// never opened.
    ///
    /// This closes that gap without weakening knowledge gates: Located can describe the
    /// position, while star and planet detail remains Visited-only. What this CANNOT invent
    /// is surface state — drills, structures, dominance, damage — because those exist only
    /// where the world has really been materialized. Those come back empty, which is the
    /// truth: there is nothing built on a planet nobody has ever been to.
    /// </summary>
    public static class StrategicMapUniverseViews
    {
        /// <summary>
        /// Builds a system view from the map's own universe. False when the id is not a
        /// system the map is carrying.
        /// </summary>
        public static bool TrySystem(StrategicMapUniverseSnapshot universe, string systemId,
            out StrategicSystemView view)
        {
            view = default;
            StrategicMapUniverseSnapshot.SystemRow row = FindSystem(universe, systemId);
            if (row == null) return false;
            SystemKnowledgeLevel knowledge = KnowledgeOf(row);
            view = SystemView(universe, row, includePlanets: knowledge >= SystemKnowledgeLevel.Visited);
            return true;
        }

        /// <summary>
        /// Builds a planet view and its parent system from the map's own universe. False when
        /// the id is not a planet the map is carrying.
        /// </summary>
        public static bool TryPlanet(StrategicMapUniverseSnapshot universe, string planetId,
            out StrategicPlanetView planet, out StrategicSystemView system)
        {
            planet = default;
            system = default;
            if (universe == null || string.IsNullOrEmpty(planetId)) return false;

            for (int i = 0; i < universe.systems.Count; i++)
            {
                StrategicMapUniverseSnapshot.SystemRow row = universe.systems[i];
                if (KnowledgeOf(row) < SystemKnowledgeLevel.Visited) continue;
                for (int p = row.planetFrom; p < row.planetTo && p < universe.planets.Count; p++)
                {
                    if (universe.planets[p].id != planetId) continue;
                    planet = PlanetView(universe.planets[p]);
                    system = SystemView(universe, row, includePlanets: true);
                    return true;
                }
            }
            return false;
        }

        private static StrategicMapUniverseSnapshot.SystemRow FindSystem(
            StrategicMapUniverseSnapshot universe, string systemId)
        {
            if (universe == null || string.IsNullOrEmpty(systemId)) return null;
            for (int i = 0; i < universe.systems.Count; i++)
                if (universe.systems[i].id == systemId) return universe.systems[i];
            return null;
        }

        private static StrategicSystemView SystemView(StrategicMapUniverseSnapshot universe,
            StrategicMapUniverseSnapshot.SystemRow row, bool includePlanets)
        {
            var planets = new List<StrategicPlanetView>();
            if (includePlanets)
                for (int p = row.planetFrom; p < row.planetTo && p < universe.planets.Count; p++)
                    planets.Add(PlanetView(universe.planets[p]));

            // Positions on the map are in map units; the view models speak absolute world units.
            Vector3 absoluteCenter = row.Position * StrategicMapOrbits.MapUnit;

            SystemKnowledgeLevel knowledge = KnowledgeOf(row);
            bool positionKnown = knowledge >= SystemKnowledgeLevel.Located;
            bool detailsKnown = knowledge >= SystemKnowledgeLevel.Visited;

            return new StrategicSystemView(
                systemId: row.id,
                displayName: row.name,
                sector: SectorCoordinate.FromAbsolutePosition(absoluteCenter),
                knowledge: knowledge,
                // Nothing of the player's is out there reporting in, so there is no link at
                // all. "Son bilinen" would imply there had once been one.
                link: SystemLinkState.Disconnected,
                positionKnown: positionKnown,
                absoluteCenter: positionKnown ? absoluteCenter : Vector3.zero,
                // Signal rows are already placed on a quantized surrogate bearing by the
                // live source. Recover that direction relative to the ship, never from the
                // universe origin (which would point somewhere else and leak false data).
                approximateDirection: knowledge == SystemKnowledgeLevel.Signal
                    ? (row.Position - universe.ShipPosition).normalized : Vector3.zero,
                detailsKnown: detailsKnown,
                starClass: (StarClass)Mathf.Clamp(row.starClass, 0, 3),
                starRadius: row.starRadius,
                navigationRadius: row.navigationRadius,
                planetCount: detailsKnown ? Mathf.Max(0, row.planetTo - row.planetFrom) : 0,
                ownership: detailsKnown ? SystemOwnershipTag.Discovered : SystemOwnershipTag.Undiscovered,
                isCurrentSystem: false,
                isStartSystem: false,
                hasAttention: false,
                hasRaidWarning: false,
                // Not stale in the "your colony went dark" sense; there is simply nothing
                // there yet reporting in. Zero keeps the card from crying about an age.
                lastKnownAgeSeconds: 0f,
                planets: planets,
                pois: Array.Empty<StrategicPoiView>());
        }

        private static SystemKnowledgeLevel KnowledgeOf(StrategicMapUniverseSnapshot.SystemRow row) =>
            (SystemKnowledgeLevel)Mathf.Clamp(row?.knowledge ?? (int)SystemKnowledgeLevel.Unknown,
                (int)SystemKnowledgeLevel.Unknown, (int)SystemKnowledgeLevel.Visited);

        private static StrategicPlanetView PlanetView(StrategicMapUniverseSnapshot.PlanetRow row)
        {
            ResourceFamily family = ParseFamily(row.resourceFamily);
            var environment = (EnvironmentClass)Mathf.Clamp(row.environment, 0, 2);

            return new StrategicPlanetView(
                planetId: row.id,
                displayName: row.name,
                planetIndex: 0,
                // Back to world units: the row carries map units, the view models do not.
                orbitRadius: row.orbit * StrategicMapOrbits.MapUnit,
                planetRadius: row.radius,
                orbitAngleDegrees: row.phase * Mathf.Rad2Deg,
                scanned: row.scanned,
                family: family,
                rarity: ParseRarity(row.rarity),
                environment: environment,
                archetype: PlanetArchetypeCatalog.ForFamily(family),
                dominance: DominanceState.Unowned,
                abandoned: false,
                hasDrill: false,
                drillTier: 1,
                drillFill01: 0f,
                // Deliberately empty. Surface slots exist only where a real profile does, and
                // inventing twelve of them for a world nobody has visited would be the card
                // stating something the game does not know.
                slots: Array.Empty<StrategicSlotView>());
        }

        /// <summary>
        /// The row carries the family as a string so the map's data format never depends on
        /// the enum's ordering, which is save-facing. Parsed back here rather than cast.
        /// </summary>
        private static ResourceFamily ParseFamily(string family) =>
            Enum.TryParse(family, out ResourceFamily parsed) ? parsed : ResourceFamily.Metal;

        private static Rarity ParseRarity(string rarity) =>
            Enum.TryParse(rarity, out Rarity parsed) ? parsed : Rarity.Common;
    }
}
