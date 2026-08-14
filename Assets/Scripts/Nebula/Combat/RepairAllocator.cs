using UnityEngine;

namespace Nebula.Combat
{
    /// <summary>
    /// Pure lowest-fraction-layer-first split of one tick's healable HP
    /// between Armor and Hull (VS-013.1 §5) — re-evaluated every tick, so
    /// repair naturally balances the two toward parity instead of maxing one
    /// out while the other stays low. Caps (field 60% / safe-zone 100%) are
    /// the caller's own precomputed absolute ceilings, never re-derived here.
    /// </summary>
    public static class RepairAllocator
    {
        public static (float toArmor, float toHull) Allocate(float healAmount,
            float armor, float armorMax, float armorCap,
            float hull, float hullMax, float hullCap)
        {
            if (healAmount <= 0f) return (0f, 0f);

            float armorRoom = Mathf.Max(0f, armorCap - armor);
            float hullRoom = Mathf.Max(0f, hullCap - hull);
            float armorFraction = armorMax > 0f ? armor / armorMax : 1f;
            float hullFraction = hullMax > 0f ? hull / hullMax : 1f;

            if (armorFraction <= hullFraction)
            {
                float toArmor = Mathf.Min(healAmount, armorRoom);
                float toHull = Mathf.Min(healAmount - toArmor, hullRoom);
                return (toArmor, toHull);
            }
            else
            {
                float toHull = Mathf.Min(healAmount, hullRoom);
                float toArmor = Mathf.Min(healAmount - toHull, armorRoom);
                return (toArmor, toHull);
            }
        }
    }
}
