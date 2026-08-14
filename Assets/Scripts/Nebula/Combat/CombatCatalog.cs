namespace Nebula.Combat
{
    /// <summary>
    /// Pure numeric data authority for VS-013 ship combat: level-derived Hull
    /// Form baseline (speed reference/K-Z-G/turn/hardpoints), the separate
    /// defense.hull_reinforcement (Armor+Hull only) and defense.shield_systems
    /// (Shield only) module multipliers, combat energy capacity/regen (scaled
    /// by defense.weapon_coordination tier), and Combat Boost constants. No
    /// UnityEngine.Random, no scene dependency — every number here is exactly
    /// as specified, nothing invented (VS-013.1 §1/§2).
    /// </summary>
    public static class CombatCatalog
    {
        // --- Hull Form (VS-013.1 §1): level-derived, NEVER a purchasable module ---
        // L1-20 -> Form I, L21-40 -> Form II. This slice never exceeds Form II
        // regardless of actual character level (L41+ content is out of scope).
        // Form III-V numeric data below is preserved catalog/test data only,
        // carried forward unchanged from the retired hull-stage tables — never
        // reachable via HullFormForLevel this slice.
        public const int MaxHullFormThisSlice = 2;

        public static int HullFormForLevel(int level)
        {
            int raw = level <= 20 ? 1 : 2;
            return UnityEngine.Mathf.Min(raw, MaxHullFormThisSlice);
        }

        private static int FormIndex(int hullForm) => UnityEngine.Mathf.Clamp(hullForm - 1, 0, HullFormDurabilityMultiplierByForm.Length - 1);

        public const float BaseSpeed = 180f;
        public const float BaseShield = 400f;
        public const float BaseArmor = 600f;
        public const float BaseHull = 1000f;

        /// <summary>
        /// Informational only — NEVER applied to ShipController. Form I/II's
        /// reference speed intentionally equals Engine-Navigation's own
        /// LocalTopSpeedByTier T0/T2 values (180/194.4): Engine-Navigation
        /// (ShipCruise.ApplyEngineTier -&gt; ShipController.SetLocalMaxThrottle)
        /// is the single, exclusive authority over actual commanded ship
        /// speed. This table exists purely so a test can assert the two
        /// numbers stay in sync without either ever multiplying the other.
        /// </summary>
        public static readonly float[] HullFormSpeedReferenceByForm = { 180f, 194.4f, 208.8f, 223.2f, 237.6f };
        public static readonly float[] HullFormDurabilityMultiplierByForm = { 1f, 1.5f, 2.2f, 3.1f, 4.2f };
        public static readonly float[] HullFormTurnMultiplierByForm = { 1f, 1.05f, 1.10f, 1.15f, 1.20f };
        public static readonly int[] HullFormHardpointsByForm = { 1, 2, 3, 4, 5 };

        public static float HullFormSpeedReference(int hullForm) => HullFormSpeedReferenceByForm[FormIndex(hullForm)];
        public static float HullFormDurabilityMultiplier(int hullForm) => HullFormDurabilityMultiplierByForm[FormIndex(hullForm)];
        public static float HullFormTurnMultiplier(int hullForm) => HullFormTurnMultiplierByForm[FormIndex(hullForm)];
        public static int HullFormHardpoints(int hullForm) => HullFormHardpointsByForm[FormIndex(hullForm)];

        // --- Hull Reinforcement (defense.hull_reinforcement, VS-013.1 §1) ------------
        // Pure Armor+Hull multiplier applied ON TOP of the current Hull Form's
        // baseline Armor/Hull -- never touches Shield/speed/turn/hardpoints/
        // energy/weapon damage. Indexed directly by tier (no T0/T1 collapse):
        // T0 is a real, distinct no-op baseline; T1 is a real, distinct step up.
        public static readonly float[] HullReinforcementArmorHullMultiplierByTier = { 1f, 1.15f, 1.30f };
        public static float HullReinforcementMultiplier(int tier) => HullReinforcementArmorHullMultiplierByTier[UnityEngine.Mathf.Clamp(tier, 0, HullReinforcementArmorHullMultiplierByTier.Length - 1)];

        // --- Shield Systems (defense.shield_systems, VS-013.1 §2) --------------------
        // Pure Shield-max multiplier, applied on top of the Hull Form's own
        // Shield baseline -- never touches Armor/Hull/speed/turn/hardpoints/
        // energy/weapon damage. T2 also shortens the post-hit regen delay
        // (see ShipCombatState/Durability's regenDelaySeconds parameter).
        public static readonly float[] ShieldSystemsMultiplierByTier = { 1f, 1.25f, 1.50f };
        public static float ShieldSystemsMultiplier(int tier) => ShieldSystemsMultiplierByTier[UnityEngine.Mathf.Clamp(tier, 0, ShieldSystemsMultiplierByTier.Length - 1)];
        public const float ShieldSystemsT2RegenDelaySeconds = 7f;
        public const int ShieldSystemsT2Tier = 2;

        public static float ResolvedShieldMax(int hullForm, int shieldSystemsTier) => BaseShield * HullFormDurabilityMultiplier(hullForm) * ShieldSystemsMultiplier(shieldSystemsTier);
        public static float ResolvedArmorMax(int hullForm, int hullReinforcementTier) => BaseArmor * HullFormDurabilityMultiplier(hullForm) * HullReinforcementMultiplier(hullReinforcementTier);
        public static float ResolvedHullMax(int hullForm, int hullReinforcementTier) => BaseHull * HullFormDurabilityMultiplier(hullForm) * HullReinforcementMultiplier(hullReinforcementTier);

        // --- Combat energy (VS-013 §1, scaled by defense.weapon_coordination tier) ---
        public static readonly float[] EnergyCapacityByTier = { 100f, 150f, 220f, 310f, 420f };
        public static readonly float[] EnergyRegenPerSecondByTier = { 15f, 22f, 32f, 45f, 60f };

        /// <summary>Unchanged from VS-013: T0 and T1 both use index 0 for the energy tables (this collapse was never part of the hull-stage bug this fix targets — only defense.hull_reinforcement's own multiplier lost it).</summary>
        private static int WeaponTierIndex(int weaponCoordinationTier) => UnityEngine.Mathf.Clamp(weaponCoordinationTier - 1, 0, EnergyCapacityByTier.Length - 1);
        public static float EnergyCapacity(int weaponCoordinationTier) => EnergyCapacityByTier[WeaponTierIndex(weaponCoordinationTier)];
        public static float EnergyRegenPerSecond(int weaponCoordinationTier) => EnergyRegenPerSecondByTier[WeaponTierIndex(weaponCoordinationTier)];

        /// <summary>Energy hits 0 -&gt; weapons/Boost stay locked out until the pool refills back up to this fraction of capacity.</summary>
        public const float EnergyReopenFraction = 0.20f;

        /// <summary>min(Hull Form hardpoint count, Weapon Coordination tier + 1) -- VS-013.1 fix: hardpoint count comes from Hull Form, never from defense.hull_reinforcement.</summary>
        public static int ActiveHardpointSlots(int hullForm, int weaponCoordinationTier)
        {
            int formHardpoints = HullFormHardpoints(hullForm);
            return UnityEngine.Mathf.Min(formHardpoints, weaponCoordinationTier + 1);
        }

        // --- Combat Boost (VS-013 §1) --------------------------------------------
        public const float BoostEnergyCost = 40f;
        public const float BoostDurationSeconds = 3f;
        public const float BoostSpeedMultiplier = 1.40f;
        public const float BoostCooldownSeconds = 6f;

        // --- Damaged state (VS-013.1 §5) -----------------------------------------
        /// <summary>Local flight max speed multiplier while ShipCombatState.Damaged. Boost and Deep Space Cruise are separately made fully unavailable (never combined with this).</summary>
        public const float DamagedFlightSpeedMultiplier = 0.75f;

        // --- Weapon family unlock (VS-013 §2): Laser is always free/fixed T1 ----
        public const int PlasmaUnlockWeaponCoordinationTier = 1;
        public const int EmUnlockWeaponCoordinationTier = 2;

        // --- Ship-ship collision (VS-013 §6) -------------------------------------
        public const float CollisionSafeRelativeSpeed = 80f;
        /// <summary>A single collision event never costs more than this fraction of the victim's OWN max Hull.</summary>
        public const float CollisionMaxHullFractionPerEvent = 0.15f;
        public const float CollisionPairImmunitySeconds = 1.5f;

        // --- First Probe XP (VS-013.1 §4): finalized to the catalog value 400 --
        // no longer provisional, per explicit instruction to convert the temporary
        // value into a real, centrally-named catalog constant.
        public const int FirstProbeDefeatXp = 400;
    }
}
