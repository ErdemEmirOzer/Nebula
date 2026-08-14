namespace Nebula.Celestial
{
    /// <summary>
    /// Every kind of celestial object the world can contain.
    /// Only Star and Planet are spawned in the current stage; the remaining
    /// entries are reserved so future systems (asteroid fields, black holes,
    /// background galaxies, moons) can extend the world without touching the
    /// core simulation.
    /// </summary>
    public enum CelestialBodyType
    {
        Star,
        Planet,
        Moon,
        Asteroid,
        BlackHole,
        Galaxy
    }
}
