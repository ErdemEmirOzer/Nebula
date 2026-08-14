namespace Nebula.Visuals
{
    /// <summary>
    /// Who gets a pooled light when there are not enough to go round.
    /// <para>
    /// Ordered by what a player must not miss. A hit landing on their own hull
    /// outranks their own gun, which outranks a planetary turret, which
    /// outranks a firefight happening far away, which outranks decoration.
    /// When the pool is full the lowest live claim is displaced and the
    /// highest request is served — but only ever cosmetically: a shot that
    /// cannot get a light still fires, still hits, and still does its damage.
    /// </para>
    /// <para>
    /// Values are spaced so a caller can nudge within a band (e.g. a
    /// particularly close impact) without crossing into the next one.
    /// </para>
    /// </summary>
    public enum TransientLightPriority
    {
        /// <summary>Ambience. First thing dropped, always.</summary>
        Decorative = 10,

        /// <summary>A firefight the player is not part of, somewhere across the system.</summary>
        DistantCombat = 20,

        /// <summary>Planetary defence fire, and enemies close enough to matter.</summary>
        PlanetaryDefense = 30,

        /// <summary>The player's own weapons, and impacts near them.</summary>
        PlayerWeapon = 40,

        /// <summary>Something is hitting the player. This must never be the light that gets dropped.</summary>
        CriticalPlayerImpact = 50
    }
}
