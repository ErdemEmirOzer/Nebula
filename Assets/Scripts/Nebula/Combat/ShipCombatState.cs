using UnityEngine;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Combat
{
    /// <summary>
    /// Pure ship-side combat state (VS-013 §1): Shield→Armor→Hull via the
    /// existing <see cref="Durability"/> authority (never a second K/Z/G
    /// implementation), combat energy pool with the 0→20%-reopen lockout,
    /// and Combat Boost's cost/duration/cooldown. No UnityEngine.Random, no
    /// MonoBehaviour dependency — <see cref="Tick"/> takes plain values so
    /// EditMode tests can drive it directly. Star-risk effects are applied
    /// exactly as <see cref="StellarRiskResolver"/> already resolves them
    /// (never a second copy of those multipliers).
    /// </summary>
    public class ShipCombatState
    {
        public Durability Durability { get; private set; }
        public float EnergyCapacity { get; private set; }
        public float Energy { get; private set; }
        private float energyRegenPerSecond;

        /// <summary>True from the instant Energy hits 0 until it refills back up to <see cref="CombatCatalog.EnergyReopenFraction"/> of capacity — weapons and Boost are both unusable the whole time.</summary>
        public bool WeaponsLocked { get; private set; }

        public float BoostRemaining { get; private set; }
        public float BoostCooldownRemaining { get; private set; }
        public bool IsBoosting => BoostRemaining > 0f;

        /// <summary>Hull reached 0 — Emergency Shutdown, never ship destruction (see EmergencyShutdownState).</summary>
        public bool EmergencyShutdown => Durability.Disabled;

        /// <summary>True from a defeat-recovery until the physical repair transaction (VS-013.1 §5) clears it at Armor+Hull >= 60% each.</summary>
        public bool Damaged { get; private set; }

        /// <summary>Seconds of real, active ticking since the last damage event — the repair gate requires at least RepairCatalog.RequiredSafeSecondsSinceLastDamage. Starts effectively "long ago" so a never-damaged fresh ship is never blocked by this gate.</summary>
        public float SinceLastDamageSeconds { get; private set; } = 999f;

        /// <summary>VS-014 §5/§10: EM Disruptor disable — a distinct effect from WeaponsLocked (energy-depletion) or EmergencyShutdown (hull-0). Mirrors EnemyAgent's own TryDisrupt shape exactly (impaired-then-immune), just with the Disruptor's own catalog durations instead of WeaponCatalog's EM-lock ones.</summary>
        public float DisruptRemaining { get; private set; }
        private float disruptImmunityRemaining;
        public bool IsDisrupted => DisruptRemaining > 0f;

        /// <summary>No-op while still immune from a previous disrupt. Returns whether it actually landed.</summary>
        public bool TryDisrupt(float disruptSeconds, float immunitySeconds)
        {
            if (disruptImmunityRemaining > 0f) return false;
            DisruptRemaining = disruptSeconds;
            disruptImmunityRemaining = immunitySeconds + disruptSeconds;
            return true;
        }

        public void RestoreDisrupt(float disruptRemaining, float disruptImmunityRemainingSaved)
        {
            DisruptRemaining = Mathf.Max(0f, disruptRemaining);
            disruptImmunityRemaining = Mathf.Max(0f, disruptImmunityRemainingSaved);
        }

        public ShipCombatState(int hullForm, int hullReinforcementTier, int shieldSystemsTier, int weaponCoordinationTier)
        {
            ApplyModuleTiers(hullForm, hullReinforcementTier, shieldSystemsTier, weaponCoordinationTier, resetCurrentToFull: true);
        }

        /// <summary>
        /// Re-applies a Hull Form/module tier change (level-up or purchase,
        /// VS-013.1 §1/§2) to the max values. When resetCurrentToFull is
        /// false (the normal live path), current Shield/Armor/Hull are
        /// rescaled preserving their PERCENTAGE of the old max (never a free
        /// full heal, never an unintended percentage drop/gain from a max
        /// change) — Energy is clamped down only, never auto-topped-up.
        /// </summary>
        public void ApplyModuleTiers(int hullForm, int hullReinforcementTier, int shieldSystemsTier, int weaponCoordinationTier, bool resetCurrentToFull)
        {
            float newShieldMax = CombatCatalog.ResolvedShieldMax(hullForm, shieldSystemsTier);
            float newArmorMax = CombatCatalog.ResolvedArmorMax(hullForm, hullReinforcementTier);
            float newHullMax = CombatCatalog.ResolvedHullMax(hullForm, hullReinforcementTier);
            float regenDelaySeconds = shieldSystemsTier >= CombatCatalog.ShieldSystemsT2Tier
                ? CombatCatalog.ShieldSystemsT2RegenDelaySeconds : 8f;

            if (Durability == null || resetCurrentToFull)
            {
                // Durability's own constructor always starts Shield at 0 regardless of
                // startFraction -- correct for an under-construction planetary structure
                // (its original use case), but a freshly commissioned/reset ship must
                // start at FULL shield (VS-013 §1), so force it up via RestoreFromSave.
                Durability = new Durability(newShieldMax, newArmorMax, newHullMax, startFraction: 1f, regenDelaySeconds);
                Durability.RestoreFromSave(newShieldMax, newArmorMax, newHullMax, regenTimer: 0f);
            }
            else
            {
                float shieldFraction = Durability.ShieldMax > 0f ? Durability.Shield / Durability.ShieldMax : 1f;
                float armorFraction = Durability.ArmorMax > 0f ? Durability.Armor / Durability.ArmorMax : 1f;
                float hullFraction = Durability.HullMax > 0f ? Durability.Hull / Durability.HullMax : 1f;
                float regenTimer = Durability.SavedRegenTimer;
                Durability = new Durability(newShieldMax, newArmorMax, newHullMax, startFraction: 1f, regenDelaySeconds);
                Durability.RestoreFromSave(newShieldMax * shieldFraction, newArmorMax * armorFraction, newHullMax * hullFraction, regenTimer);
            }

            EnergyCapacity = CombatCatalog.EnergyCapacity(weaponCoordinationTier);
            energyRegenPerSecond = CombatCatalog.EnergyRegenPerSecond(weaponCoordinationTier);
            Energy = resetCurrentToFull ? EnergyCapacity : Mathf.Min(Energy, EnergyCapacity);
        }

        public void RestoreFromSave(float shield, float armor, float hull, float regenTimer,
            float energy, bool weaponsLocked, float boostRemaining, float boostCooldownRemaining, bool damaged)
        {
            Durability.RestoreFromSave(shield, armor, hull, regenTimer);
            Energy = Mathf.Clamp(energy, 0f, EnergyCapacity);
            WeaponsLocked = weaponsLocked;
            BoostRemaining = Mathf.Max(0f, boostRemaining);
            BoostCooldownRemaining = Mathf.Max(0f, boostCooldownRemaining);
            Damaged = damaged;
        }

        /// <summary>VS-013.1 §10: restores the exact saved since-last-damage counter (never re-derived, never reset to 999 on load — a load must not artificially reopen the repair gate early or keep it closed longer than it really was).</summary>
        public void RestoreSinceLastDamage(float seconds) => SinceLastDamageSeconds = Mathf.Max(0f, seconds);

        /// <summary>Cascading Shield→Armor→Hull damage (never applies star-risk damage directly — see class doc). Routes through the Neutral (1/1/1) profile, exactly reproducing the pre-VS-014 cascade.</summary>
        public float ApplyDamage(float amount) => ApplyDamage(amount, DamageProfile.Neutral);

        /// <summary>VS-014 §3: the shared profile-aware entry point — no enemy this slice has an assigned Laser/Plasma/EM identity, so real enemy fire always passes Neutral today, but this is the seam a future enemy weapon-family assignment (or a planetary-defense-vs-ship friendly-fire-never-happens case) would use without re-deriving the cascade.</summary>
        public float ApplyDamage(float amount, DamageProfile profile)
        {
            float overflow = DamageResolver.Apply(Durability, amount, profile);
            if (amount > 0f) SinceLastDamageSeconds = 0f;
            return overflow;
        }

        /// <summary>VS-013.1 §5: clears Damaged once both Armor and Hull reach at least the field-cap fraction (60%) of their own max — called by the repair driver after each real HP application.</summary>
        public void ClearDamagedIfHealedEnough()
        {
            if (!Damaged) return;
            float armorFraction = Durability.ArmorMax > 0f ? Durability.Armor / Durability.ArmorMax : 1f;
            float hullFraction = Durability.HullMax > 0f ? Durability.Hull / Durability.HullMax : 1f;
            // Epsilon guards the exact-60% boundary against float rounding (e.g. 600*0.6-60
            // applied via RepairExact can land a hair under 0.6 due to floating-point error) --
            // same convention as HardpointState.AddHeat's overheat-cap comparison.
            if (armorFraction >= RepairCatalog.FieldCapFraction - 0.0001f && hullFraction >= RepairCatalog.FieldCapFraction - 0.0001f) Damaged = false;
        }

        /// <summary>VS-013 §8 defeat-recovery transition: sets Shield/Armor/Hull to exact fractions of their current max (never above), marks Damaged, and always leaves the ship non-Disabled (hullFraction is never 0 in any recovery/soft-lock case this slice defines).</summary>
        public void RecoverToFractions(float hullFraction, float armorFraction, float shieldFraction)
        {
            float shield = Durability.ShieldMax * Mathf.Clamp01(shieldFraction);
            float armor = Durability.ArmorMax * Mathf.Clamp01(armorFraction);
            float hull = Durability.HullMax * Mathf.Clamp01(hullFraction);
            Durability.RestoreFromSave(shield, armor, hull, regenTimer: 0f);
            Damaged = true;
            WeaponsLocked = false;
            BoostRemaining = 0f;
            BoostCooldownRemaining = 0f;
        }

        /// <summary>False (no-op) while locked out, on cooldown, Damaged (VS-013.1 §5), or under 40 energy. Never grants invincibility/teleport/lock-break — those effects simply don't exist anywhere in this class.</summary>
        public bool TryStartBoost()
        {
            if (WeaponsLocked || IsBoosting || BoostCooldownRemaining > 0f || Damaged) return false;
            if (Energy < CombatCatalog.BoostEnergyCost) return false;
            Energy -= CombatCatalog.BoostEnergyCost;
            BoostRemaining = CombatCatalog.BoostDurationSeconds;
            return true;
        }

        /// <summary>Weapon fire/lock energy draw. False (no-op, no partial spend) when locked out or insufficient.</summary>
        public bool TryConsumeEnergy(float amount)
        {
            if (amount <= 0f) return true;
            if (WeaponsLocked) return false;
            if (Energy < amount) return false;
            Energy -= amount;
            if (Energy <= 0.0001f) { Energy = 0f; WeaponsLocked = true; }
            return true;
        }

        public void Tick(float dt, StellarRiskState starRisk)
        {
            SinceLastDamageSeconds += dt;

            if (DisruptRemaining > 0f) DisruptRemaining = Mathf.Max(0f, DisruptRemaining - dt);
            if (disruptImmunityRemaining > 0f) disruptImmunityRemaining = Mathf.Max(0f, disruptImmunityRemaining - dt);

            // Shield regen: skip Durability.Tick entirely while ShieldRegenMultiplier is 0
            // (Warning/Critical) -- Durability itself has no multiplier parameter, and
            // skipping the call also pauses (never resets) the post-hit regen delay,
            // matching this project's established pause-not-reset convention.
            if (starRisk.ShieldRegenMultiplier > 0f) Durability.Tick(dt);

            if (BoostRemaining > 0f)
            {
                BoostRemaining -= dt;
                if (BoostRemaining <= 0f) { BoostRemaining = 0f; BoostCooldownRemaining = CombatCatalog.BoostCooldownSeconds; }
            }
            else if (BoostCooldownRemaining > 0f)
            {
                BoostCooldownRemaining -= dt;
                if (BoostCooldownRemaining < 0f) BoostCooldownRemaining = 0f;
            }

            // No energy regen while boosting (VS-013 §1).
            if (!IsBoosting)
            {
                float regen = energyRegenPerSecond * starRisk.CombatEnergyMultiplier;
                Energy = Mathf.Min(EnergyCapacity, Energy + regen * dt);
            }

            if (WeaponsLocked && Energy >= EnergyCapacity * CombatCatalog.EnergyReopenFraction) WeaponsLocked = false;
        }
    }
}
