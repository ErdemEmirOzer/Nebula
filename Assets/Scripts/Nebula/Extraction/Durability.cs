using UnityEngine;

namespace Nebula.Extraction
{
    /// <summary>
    /// Shared Shield → Armor → Hull durability. Damage cascades: it drains the
    /// shield first, then overflow spills to armor, then hull. Hull reaching 0
    /// does NOT destroy the structure — it Disables it. Shield regenerates 5%
    /// of max per second starting 8s after the last hit; armor and hull only
    /// come back via <see cref="Repair"/>. Pure and engine-independent (only
    /// Mathf), so combat-free tests can drive damage/repair directly.
    /// </summary>
    public class Durability
    {
        private const float ShieldRegenPerSecond = 0.05f;
        private const float DefaultRegenDelaySeconds = 8f;

        public float ShieldMax { get; }
        public float ArmorMax { get; }
        public float HullMax { get; }
        public float Shield { get; private set; }
        public float Armor { get; private set; }
        public float Hull { get; private set; }

        private readonly float regenDelaySeconds;
        private float regenDelay;

        public bool Disabled => Hull <= 0.0001f;

        /// <summary>
        /// regenDelaySeconds defaults to the standard 8s for every existing
        /// consumer (planetary structures, logistics centers, enemies) —
        /// only VS-013.1's Shield Systems T2 ship consumer ever passes a
        /// non-default value (7s), and only for the player ship's own
        /// Durability instance.
        /// </summary>
        public Durability(float shieldMax, float armorMax, float hullMax, float startFraction, float regenDelaySeconds = DefaultRegenDelaySeconds)
        {
            ShieldMax = shieldMax; ArmorMax = armorMax; HullMax = hullMax;
            this.regenDelaySeconds = regenDelaySeconds;
            // Under-construction structures start with no shield and a fraction
            // of the final armor/hull (completion never auto-heals them).
            Shield = 0f;
            Armor = armorMax * startFraction;
            Hull = hullMax * startFraction;
        }

        public void RestoreFromSave(float shield, float armor, float hull, float regenTimer)
        {
            Shield = Mathf.Clamp(shield, 0f, ShieldMax);
            Armor = Mathf.Clamp(armor, 0f, ArmorMax);
            Hull = Mathf.Clamp(hull, 0f, HullMax);
            regenDelay = regenTimer;
        }

        public float SavedRegenTimer => regenDelay;

        /// <summary>Applies cascading damage; returns any amount left after the hull hit 0.</summary>
        public float ApplyDamage(float amount)
        {
            if (amount <= 0f) return 0f;
            regenDelay = regenDelaySeconds;

            float remaining = amount;
            float shieldHit = Mathf.Min(Shield, remaining); Shield -= shieldHit; remaining -= shieldHit;
            if (remaining <= 0f) return 0f;
            float armorHit = Mathf.Min(Armor, remaining); Armor -= armorHit; remaining -= armorHit;
            if (remaining <= 0f) return 0f;
            float hullHit = Mathf.Min(Hull, remaining); Hull -= hullHit; remaining -= hullHit;
            return remaining; // overflow past hull (structure now Disabled)
        }

        /// <summary>
        /// VS-014 §3: the same Shield→Armor→Hull cascade as <see cref="ApplyDamage(float)"/>,
        /// but each layer absorbs raw damage at its OWN multiplier (Laser/Plasma/EM
        /// hit Shield/Armor/Hull differently) — never three independent flat calls.
        /// At each layer, the raw damage still "remaining" is converted into that
        /// layer's own HP units via its multiplier, clamped to what the layer
        /// actually has, then converted BACK into the raw amount actually consumed
        /// before moving to the next layer — so a multiplier's effect is applied
        /// exactly once per layer it actually touches, and overflow always carries
        /// forward as raw damage, never pre-scaled HP. A multiplier of 1 exactly
        /// reproduces <see cref="ApplyDamage(float)"/>'s own result.
        /// </summary>
        public float ApplyDamageWithProfile(float rawAmount, float shieldMultiplier, float armorMultiplier, float hullMultiplier)
        {
            if (rawAmount <= 0f) return 0f;
            regenDelay = regenDelaySeconds;

            float remainingRaw = rawAmount;
            if (Shield > 0f && remainingRaw > 0f)
            {
                float hpConsumed = Mathf.Min(Shield, remainingRaw * shieldMultiplier);
                Shield -= hpConsumed;
                remainingRaw -= shieldMultiplier > 0f ? hpConsumed / shieldMultiplier : remainingRaw;
            }
            if (remainingRaw <= 0f) return 0f;

            if (Armor > 0f && remainingRaw > 0f)
            {
                float hpConsumed = Mathf.Min(Armor, remainingRaw * armorMultiplier);
                Armor -= hpConsumed;
                remainingRaw -= armorMultiplier > 0f ? hpConsumed / armorMultiplier : remainingRaw;
            }
            if (remainingRaw <= 0f) return 0f;

            if (Hull > 0f && remainingRaw > 0f)
            {
                float hpConsumed = Mathf.Min(Hull, remainingRaw * hullMultiplier);
                Hull -= hpConsumed;
                remainingRaw -= hullMultiplier > 0f ? hpConsumed / hullMultiplier : remainingRaw;
            }
            return remainingRaw; // overflow past hull, in RAW units (structure now Disabled)
        }

        /// <summary>Repairs armor then hull by the given amount (free-form, for tests / future repair tools).</summary>
        public void Repair(float amount)
        {
            if (amount <= 0f) return;
            float toHull = Mathf.Min(HullMax - Hull, amount); Hull += toHull; amount -= toHull;
            Armor = Mathf.Min(ArmorMax, Armor + amount);
        }

        /// <summary>
        /// Applies exact, pre-allocated amounts to Armor and Hull independently
        /// (VS-013.1 §5's physical repair transaction) — unlike <see cref="Repair"/>,
        /// the caller (RepairAllocator) has already decided the split (lowest-
        /// fraction-layer-first, respecting a field/safe-zone cap), so this never
        /// re-derives priority itself. Each amount is clamped to its own max.
        /// </summary>
        public void RepairExact(float toArmor, float toHull)
        {
            if (toArmor > 0f) Armor = Mathf.Min(ArmorMax, Armor + toArmor);
            if (toHull > 0f) Hull = Mathf.Min(HullMax, Hull + toHull);
        }

        public void Tick(float dt)
        {
            if (Disabled) return; // no passive shield regen while hull is gone
            if (regenDelay > 0f) { regenDelay -= dt; return; }
            Shield = Mathf.Min(ShieldMax, Shield + ShieldMax * ShieldRegenPerSecond * dt);
        }
    }
}
