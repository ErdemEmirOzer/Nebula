using System.Collections.Generic;
using Nebula.Extraction;

namespace Nebula.Hangar
{
    /// <summary>VS-015 §1: Engineer and Escort are purely additive — Scout=0 and Collector=1 keep their existing save-facing ordinals.</summary>
    public enum VehicleClass { Scout, Collector, Engineer, Escort }

    /// <summary>Static definition of one buildable vehicle. Stable id, never renamed.</summary>
    public class VehicleDef
    {
        public string Id { get; }
        public VehicleClass Class { get; }
        public string DisplayName { get; }
        public int UnlockLevel { get; }
        public int RequiredHangarTier { get; }
        public int BasicPartCost { get; }
        public int SpecialMaterialCost { get; }
        public float BuildSeconds { get; }

        public float Speed { get; }
        public float Shield { get; }
        public float Armor { get; }
        public float Hull { get; }

        // Scout only.
        public float ScanRange { get; }
        public float ScanSeconds { get; }

        // Collector only.
        public float CargoCapacity { get; }
        public float TransferVolumePerSecond { get; }

        /// <summary>VS-011: Mk tier within the same VehicleClass (1 = base, 2 = Mk II). A Scout only ever has Mk 1.</summary>
        public int Mk { get; }

        /// <summary>VS-015 §1: command capacity consumed by one launched instance of this vehicle (Scout/Collector stay 1; Engineer/Escort cost 2).</summary>
        public int CommandCost { get; }

        /// <summary>VS-015 §1: Engineer Mk I unlock also requires at least one Controlled planet, beyond level/hangar-tier.</summary>
        public bool RequiresControlledPlanet { get; }

        // Engineer only (VS-015 §3).
        public float RepairRatePerSecond { get; }
        public float DisabledVehicleRecoverSeconds { get; }
        public float CargoCapacityGeneric { get; }

        // Escort only (VS-015 §4).
        public float FixedLaserDps { get; }
        public float FixedLaserRange { get; }

        public VehicleDef(string id, VehicleClass cls, string displayName, int unlockLevel, int requiredHangarTier,
            int basicPartCost, int specialMaterialCost, float buildSeconds, float speed, float shield, float armor, float hull,
            float scanRange, float scanSeconds, float cargoCapacity, float transferVolumePerSecond, int mk = 1,
            int commandCost = 1, bool requiresControlledPlanet = false,
            float repairRatePerSecond = 0f, float disabledVehicleRecoverSeconds = 0f,
            float fixedLaserDps = 0f, float fixedLaserRange = 0f)
        {
            Id = id; Class = cls; DisplayName = displayName; UnlockLevel = unlockLevel; RequiredHangarTier = requiredHangarTier;
            BasicPartCost = basicPartCost; SpecialMaterialCost = specialMaterialCost; BuildSeconds = buildSeconds;
            Speed = speed; Shield = shield; Armor = armor; Hull = hull;
            ScanRange = scanRange; ScanSeconds = scanSeconds;
            CargoCapacity = cargoCapacity; TransferVolumePerSecond = transferVolumePerSecond;
            Mk = mk; CommandCost = commandCost; RequiresControlledPlanet = requiresControlledPlanet;
            RepairRatePerSecond = repairRatePerSecond; DisabledVehicleRecoverSeconds = disabledVehicleRecoverSeconds;
            FixedLaserDps = fixedLaserDps; FixedLaserRange = fixedLaserRange;
        }
    }

    /// <summary>VS-015 §1: a fixed (non player-chosen) multi-item resource recipe — used for a Mk II vehicle's direct build or a Mk I-&gt;Mk II in-place upgrade.</summary>
    public readonly struct VehicleUpgradeRecipe
    {
        public readonly string FromVehicleId;
        public readonly string ToVehicleId;
        public readonly int PartsCost;
        public readonly IReadOnlyList<(string itemId, float amount)> MaterialCosts;
        public readonly float Seconds;
        public readonly float RefundFraction;

        public VehicleUpgradeRecipe(string fromVehicleId, string toVehicleId, int partsCost,
            IReadOnlyList<(string itemId, float amount)> materialCosts, float seconds, float refundFraction)
        {
            FromVehicleId = fromVehicleId; ToVehicleId = toVehicleId; PartsCost = partsCost;
            MaterialCosts = materialCosts; Seconds = seconds; RefundFraction = refundFraction;
        }

        public List<(string itemId, float amount)> FullCost()
        {
            var list = new List<(string, float)> { (ProcessingCatalog.BasicPartId, PartsCost) };
            foreach (var m in MaterialCosts) list.Add(m);
            return list;
        }
    }

    /// <summary>
    /// VS-007's Scout/Collector plus VS-015's Engineer/Escort. Pure data; no
    /// UnityEngine.Random, no scene dependency. "Any special material" costs
    /// are never auto-picked — the player chooses one of
    /// <see cref="ProcessingCatalog.MaterialIds"/> and that exact item id is
    /// what gets reserved/consumed/saved. A Mk II vehicle's DIRECT build and
    /// Mk I-&gt;Mk II UPGRADE always use a fixed multi-item recipe instead
    /// (<see cref="VehicleUpgradeRecipe"/> / <see cref="FixedBuildRecipe"/>).
    /// </summary>
    public static class VehicleCatalog
    {
        public const string ScoutId = "vehicle.scout.mk1";
        public const string CollectorId = "vehicle.collector.mk1";
        public const string CollectorMk2Id = "vehicle.collector.mk2";
        public const string EngineerMk1Id = "vehicle.engineer.mk1";
        public const string EngineerMk2Id = "vehicle.engineer.mk2";
        public const string EscortMk1Id = "vehicle.escort.mk1";
        public const string EscortMk2Id = "vehicle.escort.mk2";

        public const string MetalAlloyId = "material.metal_alloy";
        public const string BiocompositeId = "material.biocomposite";
        public const string IndustrialDiamondId = "material.industrial_diamond";

        public static readonly VehicleDef Scout = new VehicleDef(
            ScoutId, VehicleClass.Scout, "Scout Mk I",
            unlockLevel: 10, requiredHangarTier: 1,
            basicPartCost: 20, specialMaterialCost: 15, buildSeconds: 30f,
            speed: 220f, shield: 100f, armor: 80f, hull: 120f,
            scanRange: 1500f, scanSeconds: 60f,
            cargoCapacity: 0f, transferVolumePerSecond: 0f);

        public static readonly VehicleDef Collector = new VehicleDef(
            CollectorId, VehicleClass.Collector, "Collector Mk I",
            unlockLevel: 15, requiredHangarTier: 1,
            basicPartCost: 30, specialMaterialCost: 20, buildSeconds: 45f,
            speed: 140f, shield: 100f, armor: 180f, hull: 250f,
            scanRange: 0f, scanSeconds: 0f,
            cargoCapacity: 40f, transferVolumePerSecond: 10f);

        /// <summary>VS-011 §5: Collector Mk II — direct build (Level 21 + Hangar T2) or an in-place Mk I upgrade.</summary>
        public static readonly VehicleDef CollectorMk2 = new VehicleDef(
            CollectorMk2Id, VehicleClass.Collector, "Collector Mk II",
            unlockLevel: 21, requiredHangarTier: 2,
            basicPartCost: 50, specialMaterialCost: 12, buildSeconds: 70f,
            speed: 154f, shield: 150f, armor: 270f, hull: 380f,
            scanRange: 0f, scanSeconds: 0f,
            cargoCapacity: 80f, transferVolumePerSecond: 20f, mk: 2);

        // VS-011 §5: Mk I -> Mk II in-place upgrade (parts + metal_alloy + biocomposite; a SECOND special-material
        // amount, distinct from CollectorMk2.SpecialMaterialCost above, which is direct-build's single-material cost).
        public const int Mk2UpgradePartsCost = 20;
        public const int Mk2UpgradeMetalAlloyCost = 12;
        public const int Mk2UpgradeBiocompositeCost = 8;
        public const float Mk2UpgradeSeconds = 42f;
        public const float Mk2UpgradeRefundFraction = 0.75f;

        /// <summary>VS-015 §1: Engineer Mk I — L18, Hangar T1, at least one Controlled planet, command cost 2.</summary>
        public static readonly VehicleDef EngineerMk1 = new VehicleDef(
            EngineerMk1Id, VehicleClass.Engineer, "Engineer Mk I",
            unlockLevel: 18, requiredHangarTier: 1,
            basicPartCost: 45, specialMaterialCost: 40, buildSeconds: 75f,
            speed: 150f, shield: 150f, armor: 300f, hull: 400f,
            scanRange: 0f, scanSeconds: 0f,
            cargoCapacity: 20f, transferVolumePerSecond: 0f,
            mk: 1, commandCost: 2, requiresControlledPlanet: true,
            repairRatePerSecond: 5f, disabledVehicleRecoverSeconds: 60f);

        /// <summary>VS-015 §1: Engineer Mk II — L21, Hangar T2, fixed-recipe direct build or upgrade.</summary>
        public static readonly VehicleDef EngineerMk2 = new VehicleDef(
            EngineerMk2Id, VehicleClass.Engineer, "Engineer Mk II",
            unlockLevel: 21, requiredHangarTier: 2,
            basicPartCost: 75, specialMaterialCost: 0, buildSeconds: 115f,
            speed: 165f, shield: 230f, armor: 450f, hull: 600f,
            scanRange: 0f, scanSeconds: 0f,
            cargoCapacity: 40f, transferVolumePerSecond: 0f,
            mk: 2, commandCost: 2,
            repairRatePerSecond: 9f, disabledVehicleRecoverSeconds: 45f);

        public const int EngineerMk2DirectMetalAlloyCost = 15;
        public const int EngineerMk2DirectBiocompositeCost = 15;
        public const int EngineerMk2UpgradePartsCost = 30;
        public const int EngineerMk2UpgradeMetalAlloyCost = 15;
        public const int EngineerMk2UpgradeBiocompositeCost = 15;
        public const float EngineerMk2UpgradeSeconds = 69f;

        /// <summary>VS-015 §1: Escort Mk I — L21, Hangar T2, command cost 2, fixed Laser (60 DPS / 800 range).</summary>
        public static readonly VehicleDef EscortMk1 = new VehicleDef(
            EscortMk1Id, VehicleClass.Escort, "Escort Mk I",
            unlockLevel: 21, requiredHangarTier: 2,
            basicPartCost: 40, specialMaterialCost: 30, buildSeconds: 60f,
            speed: 190f, shield: 200f, armor: 250f, hull: 350f,
            scanRange: 0f, scanSeconds: 0f,
            cargoCapacity: 0f, transferVolumePerSecond: 0f,
            mk: 1, commandCost: 2,
            fixedLaserDps: 60f, fixedLaserRange: 800f);

        /// <summary>VS-015 §1: Escort Mk II — fixed-recipe direct build or upgrade, Laser 100 DPS / 1000 range.</summary>
        public static readonly VehicleDef EscortMk2 = new VehicleDef(
            EscortMk2Id, VehicleClass.Escort, "Escort Mk II",
            unlockLevel: 21, requiredHangarTier: 2,
            basicPartCost: 65, specialMaterialCost: 0, buildSeconds: 90f,
            speed: 209f, shield: 300f, armor: 380f, hull: 530f,
            scanRange: 0f, scanSeconds: 0f,
            cargoCapacity: 0f, transferVolumePerSecond: 0f,
            mk: 2, commandCost: 2,
            fixedLaserDps: 100f, fixedLaserRange: 1000f);

        public const int EscortMk2DirectMetalAlloyCost = 18;
        public const int EscortMk2DirectIndustrialDiamondCost = 10;
        public const int EscortMk2UpgradePartsCost = 25;
        public const int EscortMk2UpgradeMetalAlloyCost = 18;
        public const int EscortMk2UpgradeIndustrialDiamondCost = 10;
        public const float EscortMk2UpgradeSeconds = 54f;

        public static readonly VehicleDef[] All = { Scout, Collector, CollectorMk2, EngineerMk1, EngineerMk2, EscortMk1, EscortMk2 };

        private static readonly Dictionary<string, VehicleDef> ById = BuildById();

        private static Dictionary<string, VehicleDef> BuildById()
        {
            var dict = new Dictionary<string, VehicleDef>();
            foreach (VehicleDef def in All) dict[def.Id] = def;
            return dict;
        }

        public static VehicleDef Get(string id) => ById.TryGetValue(id, out VehicleDef def) ? def : null;

        public static VehicleDef Get(VehicleClass cls) => cls switch
        {
            VehicleClass.Scout => Scout,
            VehicleClass.Collector => Collector,
            VehicleClass.Engineer => EngineerMk1,
            VehicleClass.Escort => EscortMk1,
            _ => Scout
        };

        /// <summary>VS-011/VS-015: resolves a class + Mk tier (Scout only ever has Mk 1; an unknown/invalid combination falls back to Mk I).</summary>
        public static VehicleDef Get(VehicleClass cls, int mk) => cls switch
        {
            VehicleClass.Collector => mk >= 2 ? CollectorMk2 : Collector,
            VehicleClass.Engineer => mk >= 2 ? EngineerMk2 : EngineerMk1,
            VehicleClass.Escort => mk >= 2 ? EscortMk2 : EscortMk1,
            _ => Get(cls)
        };

        /// <summary>The five processed materials the player can choose from for a vehicle's special-material cost.</summary>
        public static string[] SpecialMaterialIds => ProcessingCatalog.MaterialIds;

        public static List<(string itemId, float amount)> Mk2UpgradeCost() => new List<(string, float)>
        {
            (ProcessingCatalog.BasicPartId, Mk2UpgradePartsCost),
            (MetalAlloyId, Mk2UpgradeMetalAlloyCost),
            (BiocompositeId, Mk2UpgradeBiocompositeCost)
        };

        // VS-015 §1: stable vehicle-id/recipe-based fixed-cost tables, generalizing HangarProductionService
        // beyond the Collector-specific upgrade code — the Collector recipe below reproduces its exact
        // existing numbers so the live Collector Mk II flow is unchanged, just table-driven now too.

        public static readonly VehicleUpgradeRecipe CollectorUpgradeRecipe = new VehicleUpgradeRecipe(
            CollectorId, CollectorMk2Id, Mk2UpgradePartsCost,
            new (string, float)[] { (MetalAlloyId, Mk2UpgradeMetalAlloyCost), (BiocompositeId, Mk2UpgradeBiocompositeCost) },
            Mk2UpgradeSeconds, Mk2UpgradeRefundFraction);

        public static readonly VehicleUpgradeRecipe EngineerUpgradeRecipe = new VehicleUpgradeRecipe(
            EngineerMk1Id, EngineerMk2Id, EngineerMk2UpgradePartsCost,
            new (string, float)[] { (MetalAlloyId, EngineerMk2UpgradeMetalAlloyCost), (BiocompositeId, EngineerMk2UpgradeBiocompositeCost) },
            EngineerMk2UpgradeSeconds, Mk2UpgradeRefundFraction);

        public static readonly VehicleUpgradeRecipe EscortUpgradeRecipe = new VehicleUpgradeRecipe(
            EscortMk1Id, EscortMk2Id, EscortMk2UpgradePartsCost,
            new (string, float)[] { (MetalAlloyId, EscortMk2UpgradeMetalAlloyCost), (IndustrialDiamondId, EscortMk2UpgradeIndustrialDiamondCost) },
            EscortMk2UpgradeSeconds, Mk2UpgradeRefundFraction);

        private static readonly Dictionary<string, VehicleUpgradeRecipe> UpgradeRecipesByFromId = new Dictionary<string, VehicleUpgradeRecipe>
        {
            { CollectorId, CollectorUpgradeRecipe },
            { EngineerMk1Id, EngineerUpgradeRecipe },
            { EscortMk1Id, EscortUpgradeRecipe }
        };

        public static bool TryGetUpgradeRecipe(string fromVehicleId, out VehicleUpgradeRecipe recipe) =>
            UpgradeRecipesByFromId.TryGetValue(fromVehicleId, out recipe);

        /// <summary>VS-015 §1: fixed multi-item DIRECT build recipe for a Mk II vehicle (no player-chosen material, unlike Scout/Collector/Mk I Engineer/Escort).</summary>
        public static readonly VehicleUpgradeRecipe EngineerMk2DirectRecipe = new VehicleUpgradeRecipe(
            null, EngineerMk2Id, EngineerMk2.BasicPartCost,
            new (string, float)[] { (MetalAlloyId, EngineerMk2DirectMetalAlloyCost), (BiocompositeId, EngineerMk2DirectBiocompositeCost) },
            EngineerMk2.BuildSeconds, 0.75f);

        public static readonly VehicleUpgradeRecipe EscortMk2DirectRecipe = new VehicleUpgradeRecipe(
            null, EscortMk2Id, EscortMk2.BasicPartCost,
            new (string, float)[] { (MetalAlloyId, EscortMk2DirectMetalAlloyCost), (IndustrialDiamondId, EscortMk2DirectIndustrialDiamondCost) },
            EscortMk2.BuildSeconds, 0.75f);

        private static readonly Dictionary<string, VehicleUpgradeRecipe> FixedDirectBuildRecipesById = new Dictionary<string, VehicleUpgradeRecipe>
        {
            { EngineerMk2Id, EngineerMk2DirectRecipe },
            { EscortMk2Id, EscortMk2DirectRecipe }
        };

        public static bool TryGetFixedDirectBuildRecipe(string vehicleId, out VehicleUpgradeRecipe recipe) =>
            FixedDirectBuildRecipesById.TryGetValue(vehicleId, out recipe);
    }
}
