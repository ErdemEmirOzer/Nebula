namespace Nebula.Combat
{
    /// <summary>
    /// VS-014 §15: catalog constants for hostile (enemy-fired) physical
    /// projectiles and planetary Laser interception. The design brief left
    /// the Siege projectile's exact speed unspecified beyond "slow and
    /// Laser-interceptable" — <see cref="SiegeProjectileSpeed"/> is the one
    /// centralized, telemetry-tunable value that satisfies both traits
    /// (slower than every existing player projectile: Plasma 1100, EM's
    /// carrier speed is implicit in its turn-rate-guided flight — see
    /// WeaponCatalog.PlasmaProjectileSpeed/EmProjectileSpeed for comparison)
    /// while still crossing its own 5000-range in a readable, real amount of
    /// time (~14s at this speed) so a planetary Laser has a genuine
    /// intercept window. Never re-declared anywhere else.
    /// </summary>
    public static class ProjectileCatalog
    {
        /// <summary>Slower than Plasma (1100) and EM's implied carrier speed — deliberately the slowest player-visible weapon projectile in the game, so "yavaş ve kesilebilir" reads as genuinely true at every tier.</summary>
        public const float SiegeProjectileSpeed = 500f;
        public const float EnemyPlasmaProjectileSpeed = 900f;

        /// <summary>Hit points a hostile interceptable projectile can absorb from planetary Laser fire before being destroyed — one shared constant, not re-derived per projectile kind.</summary>
        public const float InterceptionDurability = 60f;

        public const float EnemyPlasmaLifetimeSeconds = 8f;
        public const float SiegeLifetimeSeconds = 20f;

        /// <summary>
        /// VS-014 §2/§4: the design brief gives planetary Plasma its DPS/range/splash
        /// but never a shot cadence -- mirrors WeaponCatalog.PlasmaFireIntervalSeconds'
        /// own role (DPS x interval = damage per shot) as the one centralized,
        /// telemetry-tunable value, kept distinct from the ship's own 1.5s interval
        /// since a planetary Plasma node's much higher DPS (140-189 vs the ship's 120)
        /// would otherwise land disproportionately large single hits.
        /// </summary>
        public const float PlanetaryPlasmaFireIntervalSeconds = 2.5f;
        public const float PlanetaryPlasmaLifetimeSeconds = 10f;
    }
}
