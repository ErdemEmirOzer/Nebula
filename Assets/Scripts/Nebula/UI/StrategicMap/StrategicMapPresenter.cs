using System.Collections.Generic;

namespace Nebula.UI.StrategicMap
{
    /// <summary>Where the player currently is in the map hierarchy.</summary>
    public enum StrategicMapLevel { Universe, Galaxy, System, Planet }

    /// <summary>
    /// Pure, engine-free presentation state for the strategic map: which level the
    /// player is looking at, what is selected, and what the breadcrumb reads. It
    /// holds no world data of its own — it only ever names ids that a
    /// <see cref="StrategicMapSnapshot"/> already contains, and it can never change
    /// gameplay.
    ///
    /// Selection is deliberately deterministic across refreshes: a selected system
    /// or planet that still exists in the newest snapshot stays selected, and one
    /// that no longer exists falls back up the hierarchy instead of dangling.
    /// </summary>
    public sealed class StrategicMapPresenter
    {
        public StrategicMapLevel Level { get; private set; } = StrategicMapLevel.Universe;
        public string SelectedGalaxyId { get; private set; }
        public string SelectedSystemId { get; private set; }
        public string SelectedPlanetId { get; private set; }

        /// <summary>True when a detail card should be showing for the current selection.</summary>
        public bool CardOpen => Level == StrategicMapLevel.Planet;

        public void Reset()
        {
            Level = StrategicMapLevel.Universe;
            SelectedGalaxyId = null;
            SelectedSystemId = null;
            SelectedPlanetId = null;
        }

        /// <summary>
        /// Dives into a galaxy. This is a real level, not a zoom step: the reference only
        /// draws system markers for the active galaxy, so which galaxy is open decides what
        /// the map shows rather than only where the camera sits.
        /// </summary>
        public void EnterGalaxy(string galaxyId)
        {
            if (string.IsNullOrEmpty(galaxyId)) return;
            SelectedGalaxyId = galaxyId;
            SelectedSystemId = null;
            SelectedPlanetId = null;
            Level = StrategicMapLevel.Galaxy;
        }

        /// <summary>Single click in the universe view: selects without diving in.</summary>
        public void SelectSystem(string systemId)
        {
            if (string.IsNullOrEmpty(systemId)) return;
            SelectedSystemId = systemId;
            SelectedPlanetId = null;
        }

        /// <summary>Double click / explicit focus: dives into the system view.</summary>
        public void EnterSystem(string systemId, string galaxyId = null)
        {
            if (string.IsNullOrEmpty(systemId)) return;
            // Entering a system from the universe view skips the galaxy level, so the galaxy
            // is recorded here rather than assumed to have been set on the way in -- otherwise
            // stepping back out of the system would land on a galaxy that was never opened.
            if (!string.IsNullOrEmpty(galaxyId)) SelectedGalaxyId = galaxyId;
            SelectedSystemId = systemId;
            SelectedPlanetId = null;
            Level = StrategicMapLevel.System;
        }

        /// <summary>Opens a planet's detail card. Only meaningful inside a system.</summary>
        public void SelectPlanet(string planetId)
        {
            if (string.IsNullOrEmpty(planetId) || string.IsNullOrEmpty(SelectedSystemId)) return;
            SelectedPlanetId = planetId;
            Level = StrategicMapLevel.Planet;
        }

        /// <summary>
        /// Clicking the star at the centre of the system the player is already standing in.
        /// It is not a level change — the star IS the system level — so this only drops any
        /// planet selection, which is what puts the star's own card back on screen.
        /// </summary>
        public void SelectStar()
        {
            if (string.IsNullOrEmpty(SelectedSystemId)) return;
            SelectedPlanetId = null;
            Level = StrategicMapLevel.System;
        }

        /// <summary>
        /// Escape / back: exactly one level per press — planet card → system →
        /// universe → (caller closes the map). Returns true while the map should
        /// stay open.
        /// </summary>
        public bool StepBack()
        {
            switch (Level)
            {
                case StrategicMapLevel.Planet:
                    SelectedPlanetId = null;
                    Level = StrategicMapLevel.System;
                    return true;
                case StrategicMapLevel.System:
                    SelectedSystemId = null;
                    // Back out to the galaxy the system belongs to when one is known, and
                    // straight to the universe when the system was entered without one.
                    Level = string.IsNullOrEmpty(SelectedGalaxyId)
                        ? StrategicMapLevel.Universe
                        : StrategicMapLevel.Galaxy;
                    return true;
                case StrategicMapLevel.Galaxy:
                    SelectedGalaxyId = null;
                    Level = StrategicMapLevel.Universe;
                    return true;
                default:
                    return false; // already at the top: the caller closes the map
            }
        }

        /// <summary>
        /// Re-validates the current selection against a fresh snapshot. A system that
        /// stopped being known (or a planet that is no longer visible) never leaves a
        /// stale selection behind — the presenter falls back to the nearest still-valid
        /// level instead.
        /// </summary>
        /// <param name="drawnByMap">
        /// Optional second opinion: does the 3D map itself carry this system id?
        ///
        /// The map draws every system in the player's neighbourhood and every galaxy star,
        /// which is far more than the gameplay snapshot builds rows for. Without this, a
        /// selection made on one of those was thrown away by the next revalidation half a
        /// second later — the player clicked a world, got a card, and watched it vanish.
        /// A system the map is genuinely drawing is a real place and stays selected.
        /// </param>
        public void Revalidate(StrategicMapSnapshot snapshot, System.Func<string, bool> drawnByMap = null)
        {
            if (snapshot == null) { Reset(); return; }
            if (string.IsNullOrEmpty(SelectedSystemId))
            {
                // A galaxy on its own is still a valid place to be standing.
                if (Level != StrategicMapLevel.Galaxy) Level = StrategicMapLevel.Universe;
                return;
            }

            if (!snapshot.TryGetSystem(SelectedSystemId, out StrategicSystemView system))
            {
                // Not in the gameplay snapshot. If the map is drawing it, the selection stands
                // and the card reads it from the map's own universe instead; the planet check
                // below is skipped because that snapshot is the wrong place to look for it.
                if (drawnByMap != null && drawnByMap(SelectedSystemId)) return;
                Reset();
                return;
            }

            if (string.IsNullOrEmpty(SelectedPlanetId)) return;

            bool planetStillVisible = false;
            for (int i = 0; i < system.Planets.Count; i++)
                if (system.Planets[i].PlanetId == SelectedPlanetId) { planetStillVisible = true; break; }

            if (!planetStillVisible)
            {
                SelectedPlanetId = null;
                if (Level == StrategicMapLevel.Planet) Level = StrategicMapLevel.System;
            }
        }

        /// <summary>Breadcrumb segments: Evren → Sektör → Sistem → Gezegen, in Turkish, using only names the player may see.</summary>
        public IReadOnlyList<string> Breadcrumb(StrategicMapSnapshot snapshot)
        {
            var crumbs = new List<string> { "Evren" };
            if (snapshot == null || string.IsNullOrEmpty(SelectedSystemId)) return crumbs;
            if (!snapshot.TryGetSystem(SelectedSystemId, out StrategicSystemView system)) return crumbs;

            crumbs.Add($"Sektör {system.Sector.X}.{system.Sector.Y}.{system.Sector.Z}");
            crumbs.Add(system.DisplayName);

            if (!string.IsNullOrEmpty(SelectedPlanetId))
                for (int i = 0; i < system.Planets.Count; i++)
                    if (system.Planets[i].PlanetId == SelectedPlanetId)
                    { crumbs.Add(system.Planets[i].DisplayName); break; }

            return crumbs;
        }
    }
}
