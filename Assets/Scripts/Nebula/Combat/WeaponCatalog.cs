using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Combat
{
    public enum WeaponFamily { Laser, Plasma, Em }

    /// <summary>One tier's build/upgrade cost for a ship weapon (VS-013 §2/§3).</summary>
    public struct WeaponTierCost
    {
        public int Parts;
        public string MaterialId;
        public int Material;
        public float Seconds;
    }

    /// <summary>
    /// Pure T1-T5 data + resolvers for the three ship weapons (VS-013 §3).
    /// Every base stat is transcribed exactly from the design brief; T1-T5
    /// multiplier tables apply to DPS/range/energy only (no multiplier table
    /// was specified for heat cost, so heat per shot/tick stays constant
    /// across tiers rather than inventing a scaling curve). Runtime
    /// progression is capped at T2 this slice (<see cref="MaxTierThisSlice"/>).
    ///
    /// Ship weapon production cost = 50% (rounded up) of the "matching"
    /// planetary structure's own cost (<see cref="SurfaceStructureCatalog"/>):
    /// Laser -&gt; planetary Laser (15 parts/5 material/20s -&gt; 8/3/10s); Plasma
    /// and EM both map to planetary Command Center (30/10/60s -&gt; 15/5/30s)
    /// since only two planetary weapon archetypes exist to reference — this
    /// is a documented, literal reading of "eş tier gezegen silahının %50'si",
    /// not an invented number. T2 cost is this project's standard ~2x
    /// tier-to-tier growth (mirrors ModuleCatalog.StandardTiers' own ratio).
    /// </summary>
    public static class WeaponCatalog
    {
        public const int MaxTierThisSlice = 2;

        public static int TierIndex(int weaponTier) => Mathf.Clamp(weaponTier - 1, 0, 4);

        public static readonly float[] DpsMultiplierByTier = { 1f, 1.35f, 1.8f, 2.4f, 3.2f };
        public static readonly float[] RangeMultiplierByTier = { 1f, 1.1f, 1.2f, 1.3f, 1.4f };
        public static readonly float[] EnergyMultiplierByTier = { 1f, 1.1f, 1.2f, 1.3f, 1.4f };

        // --- Hardpoint heat (VS-013 §3, shared by every weapon family) ---------
        public const float HardpointMaxHeat = 100f;
        public const float HardpointCoolPerSecond = 20f;
        public const float HardpointCoolIdleDelaySeconds = 0.75f;
        /// <summary>Only the Laser's exact overheat duration is specified; generalized to every family's overheat lockout (VS-013 §3's "0-100 local heat" is itself introduced as a shared, not Laser-only, mechanic).</summary>
        public const float OverheatLockoutSeconds = 3f;

        // --- Laser T1 (VS-013 §3) ------------------------------------------------
        public const float LaserBaseDps = 80f;
        public const float LaserBaseRange = 1000f;
        public const float LaserGimbalDegrees = 25f;
        public const float LaserStandardAimAssistDegrees = 3f;
        public const float LaserBaseEnergyPerSecond = 12f;
        public const float LaserContinuousFireSecondsBeforeOverheat = 8f;
        /// <summary>Derived so 8s of continuous fire reaches the 100-heat cap exactly (100/8) -- unifies Laser onto the same 0-100 heat model as Plasma/EM.</summary>
        public const float LaserHeatPerSecond = HardpointMaxHeat / LaserContinuousFireSecondsBeforeOverheat;

        public static float LaserDps(int tier) => LaserBaseDps * DpsMultiplierByTier[TierIndex(tier)];
        public static float LaserRange(int tier) => LaserBaseRange * RangeMultiplierByTier[TierIndex(tier)];
        public static float LaserEnergyPerSecond(int tier) => LaserBaseEnergyPerSecond * EnergyMultiplierByTier[TierIndex(tier)];
        public static float LaserDamagePerTick(int tier, float dt) => LaserDps(tier) * dt;
        public static float LaserEnergyPerTick(int tier, float dt) => LaserEnergyPerSecond(tier) * dt;

        // --- Plasma T1 (VS-013 §3) -----------------------------------------------
        public const float PlasmaBaseDps = 120f;
        public const float PlasmaBaseRange = 2500f;
        public const float PlasmaProjectileSpeed = 1100f;
        public const float PlasmaFireIntervalSeconds = 1.5f;
        public const float PlasmaEnergyPerShot = 30f;
        public const float PlasmaHeatPerShot = 35f;
        public const float PlasmaSplashRadius = 120f;
        public const float PlasmaSplashMaxFractionOfDirect = 0.25f;

        public static float PlasmaRange(int tier) => PlasmaBaseRange * RangeMultiplierByTier[TierIndex(tier)];
        /// <summary>Direct-hit damage for one shot: DPS x the weapon's own fire interval (its natural "time between hits").</summary>
        public static float PlasmaDirectDamage(int tier) => PlasmaBaseDps * DpsMultiplierByTier[TierIndex(tier)] * PlasmaFireIntervalSeconds;

        /// <summary>Linear falloff to 0 at the splash radius; 0 at/after the radius; never friendly-fire (caller only ever applies this to enemies).</summary>
        public static float PlasmaSplashDamage(int tier, float distanceFromImpact) => PlasmaSplashDamageForDirect(PlasmaDirectDamage(tier), distanceFromImpact);

        /// <summary>
        /// VS-014 §15: same radius/max-fraction falloff shape as the ship's own Plasma,
        /// parameterized by an already-known direct-hit amount -- shared by planetary
        /// Plasma defenses (whose direct damage comes from PlanetaryDefenseCatalog's DPS,
        /// never this catalog's tier table) so the splash formula is never duplicated.
        /// </summary>
        public static float PlasmaSplashDamageForDirect(float directDamage, float distanceFromImpact)
        {
            if (distanceFromImpact >= PlasmaSplashRadius || distanceFromImpact < 0f) return 0f;
            float falloff = 1f - (distanceFromImpact / PlasmaSplashRadius);
            return directDamage * PlasmaSplashMaxFractionOfDirect * falloff;
        }

        // --- Heat-Guided EM T1 (VS-013 §3) --------------------------------------
        public const float EmBaseDps = 45f;
        public const float EmBaseRange = 5000f;
        public const float EmLockConeDegrees = 15f;
        public const float EmLockSeconds = 1.5f;
        public const float EmProjectileSpeed = 1600f;
        public const float EmMaxTurnRateDegreesPerSecond = 45f;
        public const float EmEnergyPerShot = 50f;
        public const float EmHeatPerShot = 60f;
        public const float EmDisruptSeconds = 3f;
        public const float EmImmunityAfterDisruptSeconds = 8f;
        public const float EmLockBreakGraceSeconds = 2f;

        public static float EmRange(int tier) => EmBaseRange * RangeMultiplierByTier[TierIndex(tier)];
        /// <summary>Damage for one fired missile: DPS x the lock time (the weapon's own bottleneck cadence before a shot can be fired at all).</summary>
        public static float EmDamagePerShot(int tier) => EmBaseDps * DpsMultiplierByTier[TierIndex(tier)] * EmLockSeconds;

        // --- Ship weapon production cost (VS-013 §2/§3) --------------------------
        private const string MetalAlloyId = "material.metal_alloy";
        private const string RefinedGasId = "material.refined_gas";

        public static WeaponTierCost CostForTier(WeaponFamily family, int targetTier)
        {
            return (family, targetTier) switch
            {
                (WeaponFamily.Laser, 1) => new WeaponTierCost { Parts = 8, MaterialId = MetalAlloyId, Material = 3, Seconds = 10f },
                (WeaponFamily.Laser, 2) => new WeaponTierCost { Parts = 16, MaterialId = MetalAlloyId, Material = 6, Seconds = 20f },
                (WeaponFamily.Plasma, 1) => new WeaponTierCost { Parts = 15, MaterialId = MetalAlloyId, Material = 5, Seconds = 30f },
                (WeaponFamily.Plasma, 2) => new WeaponTierCost { Parts = 30, MaterialId = MetalAlloyId, Material = 10, Seconds = 60f },
                (WeaponFamily.Em, 1) => new WeaponTierCost { Parts = 15, MaterialId = RefinedGasId, Material = 5, Seconds = 30f },
                (WeaponFamily.Em, 2) => new WeaponTierCost { Parts = 30, MaterialId = RefinedGasId, Material = 10, Seconds = 60f },
                _ => default
            };
        }

        /// <summary>Reuses the project's existing 75% cancel-refund standard (SurfaceStructureCatalog.RefundFraction) rather than inventing a second one.</summary>
        public const float ProductionRefundFraction = SurfaceStructureCatalog.RefundFraction;
    }
}
