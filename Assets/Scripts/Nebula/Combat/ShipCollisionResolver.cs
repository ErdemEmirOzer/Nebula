using UnityEngine;

namespace Nebula.Combat
{
    /// <summary>
    /// Pure ship-ship (and ship-enemy) collision damage math (VS-013 §6). The
    /// design brief gives the safe-speed threshold (80 u/s) and the
    /// per-event cap (15% of the victim's own max Hull) exactly; the actual
    /// below-cap scaling curve is not specified anywhere, so this uses a
    /// simple, explicitly-labeled provisional linear model (2 damage per
    /// u/s of relative speed above the safe threshold) that is ALWAYS
    /// subordinate to the cap -- the cap, not this curve, is the approved
    /// number.
    /// </summary>
    public static class ShipCollisionResolver
    {
        /// <summary>Documented provisional scaling constant -- see class doc. The 80 u/s threshold and 15%-max-Hull cap are the actual approved numbers.</summary>
        public const float ProvisionalDamagePerExcessSpeedUnit = 2f;

        public static bool ShouldDamage(float relativeSpeed) => relativeSpeed > CombatCatalog.CollisionSafeRelativeSpeed;

        /// <summary>Damage dealt to ONE side of a collision, already capped at 15% of that side's own max Hull.</summary>
        public static float DamageForEvent(float relativeSpeed, float victimMaxHull)
        {
            if (!ShouldDamage(relativeSpeed)) return 0f;
            float cap = victimMaxHull * CombatCatalog.CollisionMaxHullFractionPerEvent;
            float excess = relativeSpeed - CombatCatalog.CollisionSafeRelativeSpeed;
            float raw = excess * ProvisionalDamagePerExcessSpeedUnit;
            return Mathf.Min(raw, cap);
        }

        /// <summary>Two colliders' combined radii + this margin is the minimum separation the AI's obstacle avoidance (VS-013 §6) must keep.</summary>
        public const float MinimumSeparationMargin = 50f;
        public static float MinimumSeparation(float radiusA, float radiusB) => radiusA + radiusB + MinimumSeparationMargin;
    }
}
