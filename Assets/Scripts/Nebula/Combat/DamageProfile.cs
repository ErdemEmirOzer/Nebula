using Nebula.Extraction;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-014 §3: per-weapon-family layer multipliers — Laser/Plasma/EM hit
    /// Shield/Armor/Hull with different effectiveness. This is the ONE shared
    /// table every consumer (ship weapons, planetary defenses, and — via
    /// <see cref="Neutral"/> — enemy weapons/collision, which have no assigned
    /// family this slice) reads from; nobody re-declares these multipliers.
    /// </summary>
    public readonly struct DamageProfile
    {
        public readonly float ShieldMultiplier;
        public readonly float ArmorMultiplier;
        public readonly float HullMultiplier;

        public DamageProfile(float shieldMultiplier, float armorMultiplier, float hullMultiplier)
        {
            ShieldMultiplier = shieldMultiplier;
            ArmorMultiplier = armorMultiplier;
            HullMultiplier = hullMultiplier;
        }

        /// <summary>1/1/1 — identical to the un-profiled cascade. Used by enemy weapons and collision damage, neither of which has an assigned Laser/Plasma/EM identity this slice.</summary>
        public static readonly DamageProfile Neutral = new DamageProfile(1f, 1f, 1f);
        public static readonly DamageProfile Laser = new DamageProfile(1f, 0.65f, 1.25f);
        public static readonly DamageProfile Plasma = new DamageProfile(0.70f, 1.50f, 1.15f);
        public static readonly DamageProfile Em = new DamageProfile(2f, 0.35f, 0.50f);

        public static DamageProfile ForFamily(WeaponFamily family) => family switch
        {
            WeaponFamily.Laser => Laser,
            WeaponFamily.Plasma => Plasma,
            WeaponFamily.Em => Em,
            _ => Neutral
        };
    }

    /// <summary>
    /// VS-014 §3: the single shared damage-application path for ship weapons,
    /// planetary defenses, and (via <see cref="DamageProfile.Neutral"/>) enemy
    /// weapons/collision — never three separate flat ApplyDamage calls per
    /// layer. Durability itself stays the one K/Z/G authority
    /// (<see cref="Durability.ApplyDamageWithProfile"/>); this is only the
    /// thin, named entry point every combat call site should route through.
    /// </summary>
    public static class DamageResolver
    {
        public static float Apply(Durability durability, float rawAmount, DamageProfile profile)
        {
            if (durability == null || rawAmount <= 0f) return 0f;
            return durability.ApplyDamageWithProfile(rawAmount, profile.ShieldMultiplier, profile.ArmorMultiplier, profile.HullMultiplier);
        }
    }
}
