using System.Collections.Generic;

namespace Nebula.Extraction
{
    /// <summary>One fixed material line item (never "any material"/"planet family" — every VS-014 tier upgrade names its exact materials).</summary>
    public readonly struct StructureResourceCost
    {
        public readonly string ItemId;
        public readonly int Amount;
        public StructureResourceCost(string itemId, int amount) { ItemId = itemId; Amount = amount; }
    }

    /// <summary>
    /// One buildable/upgradeable tier of a planetary structure beyond the
    /// original T1 (VS-014 §2): Command T2, Laser T2, Plasma T1/T2. Combat
    /// stats that don't apply to a given type (Command has none; Laser has
    /// overheat but no splash; Plasma has splash but no overheat) are simply
    /// left at 0 — the same "inapplicable field stays zero" convention
    /// already used by EnemyDef (e.g. the Probe's Dps/AttackRange).
    /// </summary>
    public class StructureTierDef
    {
        public SurfaceStructureType Type { get; }
        public int Tier { get; }
        public string DisplayName { get; }
        public int RequiredCharacterLevel { get; }
        /// <summary>Command tier required on THIS planet's own Command Center to build/upgrade this (0 = no Command requirement, i.e. Command T2 itself only needs an active Command T1).</summary>
        public int RequiredCommandTier { get; }
        public int RequiredWeaponCoordinationTier { get; }
        public int PartsCost { get; }
        public StructureResourceCost[] MaterialCosts { get; }
        public float BuildSeconds { get; }
        public float ShieldMax { get; }
        public float ArmorMax { get; }
        public float HullMax { get; }

        public float Dps { get; }
        public float Range { get; }
        public float TurnDegreesPerSecond { get; }
        /// <summary>Laser only: continuous-fire seconds before it must cool down.</summary>
        public float ContinuousFireSecondsBeforeOverheat { get; }
        public float OverheatCooldownSeconds { get; }
        /// <summary>Plasma only.</summary>
        public float SplashRadius { get; }
        public float SplashMaxFractionOfDirect { get; }

        /// <summary>VS-015 §7: Sensor Array only — extra raid-warning seconds this tier contributes.</summary>
        public float WarningBonusSeconds { get; }
        /// <summary>VS-015 §7: Maintenance Platform only — same-planet Engineer repair rate multiplier.</summary>
        public float EngineerRepairMultiplier { get; }
        /// <summary>VS-015 §7: Maintenance Platform only — same-planet Engineer disabled-vehicle recovery TIME multiplier (below 1 = faster).</summary>
        public float RecoveryTimeMultiplier { get; }
        /// <summary>VS-015 §7: Support-structure-only gate — at least one Engineer physically present on this planet is required (Maintenance Platform T1/T2).</summary>
        public bool RequiresLocalEngineer { get; }

        /// <summary>
        /// Units of a <i>player-chosen</i> material this tier costs, on top of
        /// <see cref="PartsCost"/> — a family the player picks at build time, which is
        /// why it cannot appear in <see cref="MaterialCosts"/> (every entry there names
        /// an exact item id).
        /// <para>
        /// P1-ECO-015: Laser T1 really costs 15 parts plus 5 of any one material, and
        /// <c>LaserT1.MaterialCosts</c> was an empty array, so
        /// <see cref="FullCost"/> answered "15 parts, no material" — correct as a list
        /// of fixed line items and wrong as a price. Nothing charged from it (the Laser
        /// build path reads <c>SurfaceStructureCatalog.Laser</c> directly), so no player
        /// was ever undercharged; a cost screen reading the tier table would have shown
        /// the structure as cheaper than it is. The requirement is recorded here instead
        /// of being absent, and <see cref="FullCost"/> says what it does and does not
        /// include.
        /// </para>
        /// </summary>
        public int PlayerChosenMaterialCost { get; }

        public StructureTierDef(SurfaceStructureType type, int tier, string displayName,
            int requiredCharacterLevel, int requiredCommandTier, int requiredWeaponCoordinationTier,
            int partsCost, StructureResourceCost[] materialCosts, float buildSeconds,
            float shieldMax, float armorMax, float hullMax,
            float dps, float range, float turnDegreesPerSecond,
            float continuousFireSecondsBeforeOverheat, float overheatCooldownSeconds,
            float splashRadius, float splashMaxFractionOfDirect,
            float warningBonusSeconds = 0f, float engineerRepairMultiplier = 1f, float recoveryTimeMultiplier = 1f,
            bool requiresLocalEngineer = false, int playerChosenMaterialCost = 0)
        {
            Type = type; Tier = tier; DisplayName = displayName;
            RequiredCharacterLevel = requiredCharacterLevel; RequiredCommandTier = requiredCommandTier; RequiredWeaponCoordinationTier = requiredWeaponCoordinationTier;
            PartsCost = partsCost; MaterialCosts = materialCosts ?? System.Array.Empty<StructureResourceCost>();
            BuildSeconds = buildSeconds; ShieldMax = shieldMax; ArmorMax = armorMax; HullMax = hullMax;
            Dps = dps; Range = range; TurnDegreesPerSecond = turnDegreesPerSecond;
            ContinuousFireSecondsBeforeOverheat = continuousFireSecondsBeforeOverheat; OverheatCooldownSeconds = overheatCooldownSeconds;
            SplashRadius = splashRadius; SplashMaxFractionOfDirect = splashMaxFractionOfDirect;
            WarningBonusSeconds = warningBonusSeconds; EngineerRepairMultiplier = engineerRepairMultiplier;
            RecoveryTimeMultiplier = recoveryTimeMultiplier; RequiresLocalEngineer = requiresLocalEngineer;
            PlayerChosenMaterialCost = playerChosenMaterialCost;
        }

        /// <summary>
        /// The fixed line items of this tier's price: parts plus every named material.
        /// <para>
        /// It deliberately does NOT include <see cref="PlayerChosenMaterialCost"/>,
        /// which has no item id until the player picks one — a caller that charges from
        /// this list must add that line itself, with the chosen id, exactly as
        /// <c>ShipExtraction</c>'s Laser build does. <see cref="IsFullPrice"/> is the
        /// question a cost display should ask before treating this as the whole bill.
        /// </para>
        /// </summary>
        public List<(string itemId, float amount)> FullCost()
        {
            var list = new List<(string, float)> { (ProcessingCatalog.BasicPartId, PartsCost) };
            foreach (StructureResourceCost c in MaterialCosts) list.Add((c.ItemId, c.Amount));
            return list;
        }

        /// <summary>True when <see cref="FullCost"/> really is everything this tier costs.</summary>
        public bool IsFullPrice => PlayerChosenMaterialCost <= 0;
    }

    /// <summary>
    /// VS-014 §2: the four new planetary-structure tiers beyond the original
    /// VS-003/VS-013 T1 Command/Laser. Pure data; every number transcribed
    /// exactly from the design brief.
    /// </summary>
    public static class PlanetaryDefenseCatalog
    {
        /// <summary>T1 combat stats never existed in code before VS-014 (planetary Laser never actually fired at anything pre-raid) — KZG/cost here mirror the existing SurfaceStructureCatalog.Laser values exactly (never re-typed as a second authority for those), only DPS/Range/hitscan are new. "Hitscan" (per spec) means an in-range+LOS target is hit immediately, so TurnDegreesPerSecond is a sentinel 0 = "no turn-rate gating, always aimed."</summary>
        public static readonly StructureTierDef LaserT1 = new StructureTierDef(
            SurfaceStructureType.Laser, tier: 1, displayName: "Laser Defense T1",
            requiredCharacterLevel: 0, requiredCommandTier: 0, requiredWeaponCoordinationTier: 0,
            partsCost: SurfaceStructureCatalog.Laser.PartsCost, materialCosts: System.Array.Empty<StructureResourceCost>(),
            buildSeconds: SurfaceStructureCatalog.Laser.BuildSeconds,
            shieldMax: SurfaceStructureCatalog.Laser.ShieldMax, armorMax: SurfaceStructureCatalog.Laser.ArmorMax, hullMax: SurfaceStructureCatalog.Laser.HullMax,
            dps: 100f, range: 1000f, turnDegreesPerSecond: 0f, continuousFireSecondsBeforeOverheat: 0f, overheatCooldownSeconds: 0f, splashRadius: 0f, splashMaxFractionOfDirect: 0f,
            // Read from the same Laser def the parts cost is read from, one way, so the
            // tier table and the build path quote one price (P1-ECO-015).
            playerChosenMaterialCost: SurfaceStructureCatalog.Laser.MaterialCost);

        public static readonly StructureTierDef CommandT2 = new StructureTierDef(
            SurfaceStructureType.Command, tier: 2, displayName: "Command Center T2",
            requiredCharacterLevel: 21, requiredCommandTier: 1, requiredWeaponCoordinationTier: 0,
            partsCost: 65, materialCosts: new[] { new StructureResourceCost("material.biocomposite", 25), new StructureResourceCost("material.metal_alloy", 15) },
            buildSeconds: 120f,
            shieldMax: 300f, armorMax: 900f, hullMax: 1500f,
            dps: 0f, range: 0f, turnDegreesPerSecond: 0f, continuousFireSecondsBeforeOverheat: 0f, overheatCooldownSeconds: 0f, splashRadius: 0f, splashMaxFractionOfDirect: 0f);

        public static readonly StructureTierDef LaserT2 = new StructureTierDef(
            SurfaceStructureType.Laser, tier: 2, displayName: "Laser Defense T2",
            requiredCharacterLevel: 30, requiredCommandTier: 2, requiredWeaponCoordinationTier: 2,
            partsCost: 30, materialCosts: new[] { new StructureResourceCost("material.industrial_diamond", 12), new StructureResourceCost("material.biocomposite", 6) },
            buildSeconds: 45f,
            shieldMax: 150f, armorMax: 380f, hullMax: 600f,
            dps: 135f, range: 1100f, turnDegreesPerSecond: 90f, continuousFireSecondsBeforeOverheat: 8f, overheatCooldownSeconds: 3f, splashRadius: 0f, splashMaxFractionOfDirect: 0f);

        /// <summary>VS-014.1: parts/build/KZG read FROM SurfaceStructureCatalog.Plasma (the single source of truth for those, same one-way direction LaserT1 already uses) — never the reverse, which is what caused the eager static-init cycle this hotfix removes.</summary>
        public static readonly StructureTierDef PlasmaT1 = new StructureTierDef(
            SurfaceStructureType.Plasma, tier: 1, displayName: "Plasma Defense T1",
            requiredCharacterLevel: 30, requiredCommandTier: 2, requiredWeaponCoordinationTier: 1,
            partsCost: SurfaceStructureCatalog.Plasma.PartsCost, materialCosts: new[] { new StructureResourceCost("material.metal_alloy", 10), new StructureResourceCost("material.refined_gas", 5) },
            buildSeconds: SurfaceStructureCatalog.Plasma.BuildSeconds,
            shieldMax: SurfaceStructureCatalog.Plasma.ShieldMax, armorMax: SurfaceStructureCatalog.Plasma.ArmorMax, hullMax: SurfaceStructureCatalog.Plasma.HullMax,
            dps: 140f, range: 2500f, turnDegreesPerSecond: 45f, continuousFireSecondsBeforeOverheat: 0f, overheatCooldownSeconds: 0f, splashRadius: 120f, splashMaxFractionOfDirect: 0.25f);

        public static readonly StructureTierDef PlasmaT2 = new StructureTierDef(
            SurfaceStructureType.Plasma, tier: 2, displayName: "Plasma Defense T2",
            requiredCharacterLevel: 30, requiredCommandTier: 2, requiredWeaponCoordinationTier: 2,
            partsCost: 45, materialCosts: new[] { new StructureResourceCost("material.metal_alloy", 20), new StructureResourceCost("material.refined_gas", 10) },
            buildSeconds: 60f,
            shieldMax: 150f, armorMax: 680f, hullMax: 900f,
            dps: 189f, range: 2750f, turnDegreesPerSecond: 45f, continuousFireSecondsBeforeOverheat: 0f, overheatCooldownSeconds: 0f, splashRadius: 120f, splashMaxFractionOfDirect: 0.25f);

        /// <summary>VS-015 §7: Sensor Array T1 — L21 + Command T2 + a foreign Entity/Anomaly nearby. Parts/build/KZG read FROM SurfaceStructureCatalog.SensorArray, same one-way direction as Plasma.</summary>
        public static readonly StructureTierDef SensorArrayT1 = new StructureTierDef(
            SurfaceStructureType.SensorArray, tier: 1, displayName: "Sensor Array T1",
            requiredCharacterLevel: 21, requiredCommandTier: 2, requiredWeaponCoordinationTier: 0,
            partsCost: SurfaceStructureCatalog.SensorArray.PartsCost,
            materialCosts: new[] { new StructureResourceCost("material.industrial_diamond", 12), new StructureResourceCost("material.biocomposite", 8) },
            buildSeconds: SurfaceStructureCatalog.SensorArray.BuildSeconds,
            shieldMax: SurfaceStructureCatalog.SensorArray.ShieldMax, armorMax: SurfaceStructureCatalog.SensorArray.ArmorMax, hullMax: SurfaceStructureCatalog.SensorArray.HullMax,
            dps: 0f, range: 0f, turnDegreesPerSecond: 0f, continuousFireSecondsBeforeOverheat: 0f, overheatCooldownSeconds: 0f, splashRadius: 0f, splashMaxFractionOfDirect: 0f,
            warningBonusSeconds: 20f);

        public static readonly StructureTierDef SensorArrayT2 = new StructureTierDef(
            SurfaceStructureType.SensorArray, tier: 2, displayName: "Sensor Array T2",
            requiredCharacterLevel: 21, requiredCommandTier: 2, requiredWeaponCoordinationTier: 0,
            partsCost: 65, materialCosts: new[] { new StructureResourceCost("material.industrial_diamond", 25), new StructureResourceCost("material.biocomposite", 15) },
            buildSeconds: 120f,
            shieldMax: SurfaceStructureCatalog.SensorArray.ShieldMax * 1.5f, armorMax: SurfaceStructureCatalog.SensorArray.ArmorMax * 1.5f, hullMax: SurfaceStructureCatalog.SensorArray.HullMax * 1.5f,
            dps: 0f, range: 0f, turnDegreesPerSecond: 0f, continuousFireSecondsBeforeOverheat: 0f, overheatCooldownSeconds: 0f, splashRadius: 0f, splashMaxFractionOfDirect: 0f,
            warningBonusSeconds: 35f);

        /// <summary>VS-015 §7: Maintenance Platform T1 — L25 + Command T2 + at least one Engineer present.</summary>
        public static readonly StructureTierDef MaintenancePlatformT1 = new StructureTierDef(
            SurfaceStructureType.MaintenancePlatform, tier: 1, displayName: "Maintenance Platform T1",
            requiredCharacterLevel: 25, requiredCommandTier: 2, requiredWeaponCoordinationTier: 0,
            partsCost: SurfaceStructureCatalog.MaintenancePlatform.PartsCost,
            materialCosts: new[] { new StructureResourceCost("material.metal_alloy", 15), new StructureResourceCost("material.biocomposite", 10) },
            buildSeconds: SurfaceStructureCatalog.MaintenancePlatform.BuildSeconds,
            shieldMax: SurfaceStructureCatalog.MaintenancePlatform.ShieldMax, armorMax: SurfaceStructureCatalog.MaintenancePlatform.ArmorMax, hullMax: SurfaceStructureCatalog.MaintenancePlatform.HullMax,
            dps: 0f, range: 0f, turnDegreesPerSecond: 0f, continuousFireSecondsBeforeOverheat: 0f, overheatCooldownSeconds: 0f, splashRadius: 0f, splashMaxFractionOfDirect: 0f,
            engineerRepairMultiplier: 1.25f, recoveryTimeMultiplier: 0.85f, requiresLocalEngineer: true);

        public static readonly StructureTierDef MaintenancePlatformT2 = new StructureTierDef(
            SurfaceStructureType.MaintenancePlatform, tier: 2, displayName: "Maintenance Platform T2",
            requiredCharacterLevel: 25, requiredCommandTier: 2, requiredWeaponCoordinationTier: 0,
            partsCost: 70, materialCosts: new[] { new StructureResourceCost("material.metal_alloy", 30), new StructureResourceCost("material.biocomposite", 20) },
            buildSeconds: 150f,
            shieldMax: SurfaceStructureCatalog.MaintenancePlatform.ShieldMax * 1.5f, armorMax: SurfaceStructureCatalog.MaintenancePlatform.ArmorMax * 1.5f, hullMax: SurfaceStructureCatalog.MaintenancePlatform.HullMax * 1.5f,
            dps: 0f, range: 0f, turnDegreesPerSecond: 0f, continuousFireSecondsBeforeOverheat: 0f, overheatCooldownSeconds: 0f, splashRadius: 0f, splashMaxFractionOfDirect: 0f,
            engineerRepairMultiplier: 1.50f, recoveryTimeMultiplier: 0.70f, requiresLocalEngineer: true);

        /// <summary>VS-015 §7: total raid-warning bonus (Scout presence + every active Sensor Array on the planet) never exceeds this, on top of the base warning seconds.</summary>
        public const float WarningBonusCapSeconds = 150f;

        /// <summary>
        /// How far from a planet counts as "at this planet" for the two gates that
        /// needed a distance and had none: the Sensor Array's foreign-presence
        /// requirement (P1-ECO-014) and defense coordination's target pick
        /// (P1-COMBAT-008).
        /// <para>
        /// Derived, not chosen: it is the longest reach any planetary defense tier in
        /// this catalog has. Inside it, the ship is within the envelope those defenses
        /// actually cover — which is precisely the region a Sensor Array watches and
        /// the region a coordination order applies to. A new tier with a longer range
        /// moves this with it, so the two gates and the guns can never mean three
        /// different distances.
        /// </para>
        /// </summary>
        public static readonly float PlanetDefenseEnvelopeRange = MaxTierRange();

        private static float MaxTierRange()
        {
            float best = 0f;
            StructureTierDef[] tiers = { LaserT1, LaserT2, PlasmaT1, PlasmaT2 };
            foreach (StructureTierDef t in tiers) if (t.Range > best) best = t.Range;
            return best;
        }

        public static StructureTierDef Get(SurfaceStructureType type, int tier) => (type, tier) switch
        {
            (SurfaceStructureType.Laser, 1) => LaserT1,
            (SurfaceStructureType.Command, 2) => CommandT2,
            (SurfaceStructureType.Laser, 2) => LaserT2,
            (SurfaceStructureType.Plasma, 1) => PlasmaT1,
            (SurfaceStructureType.Plasma, 2) => PlasmaT2,
            (SurfaceStructureType.SensorArray, 1) => SensorArrayT1,
            (SurfaceStructureType.SensorArray, 2) => SensorArrayT2,
            (SurfaceStructureType.MaintenancePlatform, 1) => MaintenancePlatformT1,
            (SurfaceStructureType.MaintenancePlatform, 2) => MaintenancePlatformT2,
            _ => null
        };

        public const float RefundFraction = SurfaceStructureCatalog.RefundFraction;
    }
}
