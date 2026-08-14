using System.Collections.Generic;
using Nebula.Extraction;

namespace Nebula.Logistics
{
    public enum LogisticsCenterTier { T1 = 1, T2 = 2 }

    /// <summary>Immutable per-tier definition of the orbital System Logistics Center (VS-011 §2).</summary>
    public class LogisticsCenterTierDef
    {
        public LogisticsCenterTier Tier { get; }
        public int RequiredLevel { get; }
        public int RequiredModuleTier { get; }
        public int PartsCost { get; }
        public int MetalAlloyCost { get; }
        public int BiocompositeCost { get; }
        public float BuildSeconds { get; }
        public float Capacity { get; }
        public int MaxRoutes { get; }
        public int DockCount { get; }
        public float TransferVolumePerSecond { get; }
        public float ShieldMax { get; }
        public float ArmorMax { get; }
        public float HullMax { get; }

        public LogisticsCenterTierDef(LogisticsCenterTier tier, int requiredLevel, int requiredModuleTier,
            int partsCost, int metalAlloyCost, int biocompositeCost, float buildSeconds, float capacity,
            int maxRoutes, int dockCount, float transferVolumePerSecond, float shieldMax, float armorMax, float hullMax)
        {
            Tier = tier; RequiredLevel = requiredLevel; RequiredModuleTier = requiredModuleTier;
            PartsCost = partsCost; MetalAlloyCost = metalAlloyCost; BiocompositeCost = biocompositeCost;
            BuildSeconds = buildSeconds; Capacity = capacity; MaxRoutes = maxRoutes; DockCount = dockCount;
            TransferVolumePerSecond = transferVolumePerSecond;
            ShieldMax = shieldMax; ArmorMax = armorMax; HullMax = hullMax;
        }

        public List<(string itemId, float amount)> Cost() => new List<(string, float)>
        {
            (ProcessingCatalog.BasicPartId, PartsCost),
            ("material.metal_alloy", MetalAlloyCost),
            ("material.biocomposite", BiocompositeCost)
        };
    }

    /// <summary>
    /// The orbital System Logistics Center (VS-011 §2). Stable id, T1/T2 exact
    /// values from spec. One center per real system, never player-placed —
    /// see <see cref="LogisticsCenterAnchor"/> for its deterministic anchor.
    /// </summary>
    public static class LogisticsCenterCatalog
    {
        public const string CenterId = "station.system_logistics";
        public const string StationConstructionModuleId = "industry.station_construction";

        /// <summary>Reuses the project-wide 75% cancel-refund rule (SurfaceStructureCatalog.RefundFraction).</summary>
        public const float RefundFraction = SurfaceStructureCatalog.RefundFraction;
        /// <summary>Dismantle (fully built, idle) refund rule — half back, distinct from a mid-build cancel.</summary>
        public const float DismantleRefundFraction = 0.5f;

        public static readonly LogisticsCenterTierDef T1 = new LogisticsCenterTierDef(
            LogisticsCenterTier.T1, requiredLevel: 18, requiredModuleTier: 1,
            partsCost: 80, metalAlloyCost: 25, biocompositeCost: 20, buildSeconds: 180f,
            capacity: 600f, maxRoutes: 2, dockCount: 1, transferVolumePerSecond: 20f,
            shieldMax: 400f, armorMax: 800f, hullMax: 1200f);

        public static readonly LogisticsCenterTierDef T2 = new LogisticsCenterTierDef(
            LogisticsCenterTier.T2, requiredLevel: 25, requiredModuleTier: 2,
            partsCost: 150, metalAlloyCost: 60, biocompositeCost: 45, buildSeconds: 360f,
            capacity: 1800f, maxRoutes: 5, dockCount: 2, transferVolumePerSecond: 40f,
            shieldMax: 800f, armorMax: 1600f, hullMax: 2400f);

        public static LogisticsCenterTierDef Get(LogisticsCenterTier tier) => tier == LogisticsCenterTier.T1 ? T1 : T2;

        /// <summary>
        /// Exactly the five raw families + part.basic + five processed
        /// materials (VS-011 §3). Nuclear energy/core, active job
        /// reservations and anything else are rejected — the center never
        /// produces, processes or runs recipes, so nothing beyond this fixed
        /// set is ever a legitimate deposit.
        /// </summary>
        private static readonly HashSet<string> AcceptedItems = BuildAcceptedItems();

        private static HashSet<string> BuildAcceptedItems()
        {
            var set = new HashSet<string>();
            foreach (string id in ExtractionCatalog.ResourceIds) set.Add(id);
            set.Add(ProcessingCatalog.BasicPartId);
            foreach (string id in ProcessingCatalog.MaterialIds) set.Add(id);
            return set;
        }

        public static bool IsAcceptedItem(string itemId) => itemId != null && AcceptedItems.Contains(itemId);
    }
}
