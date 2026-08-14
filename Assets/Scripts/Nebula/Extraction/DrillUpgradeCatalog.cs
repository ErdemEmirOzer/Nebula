using System.Collections.Generic;

namespace Nebula.Extraction
{
    /// <summary>
    /// VS-017A.2 §B1/§B2: the immutable Drill T1→T2 upgrade definition. Only
    /// T1→T2 exists — no T3+, no new resource, no new currency, no new item id.
    /// The special-material half of the cost always resolves through the
    /// planet's own single resource family via the existing
    /// <see cref="ProcessingCatalog.MaterialIds"/> table, so a planet keeps
    /// carrying exactly one resource and no new mapping authority is created.
    /// </summary>
    public static class DrillUpgradeCatalog
    {
        public const int MaxTier = 2;

        /// <summary>Functional gate: the existing Station Construction module at T2 (the same module id the Logistics Center path already gates on). No new level/milestone/world gate is invented for this.</summary>
        public const string GateModuleId = Logistics.LogisticsCenterCatalog.StationConstructionModuleId;
        public const int RequiredStationConstructionTier = 2;

        public const int PartsCost = 35;
        public const int MaterialCost = 10;
        public const float UpgradeSeconds = 60f;

        /// <summary>Reuses the project-wide 75% cancel-refund rule — never a second refund authority.</summary>
        public const float RefundFraction = SurfaceStructureCatalog.RefundFraction;

        /// <summary>The processed special material a given planet family's drill upgrade consumes: Metal→metal_alloy, Wood→biocomposite, Gas→refined_gas, Water→purified_water, Diamond→industrial_diamond.</summary>
        public static string MaterialIdFor(ResourceFamily family) => ProcessingCatalog.MaterialIds[(int)family];

        /// <summary>The full T1→T2 cost for a drill on a planet of this resource family.</summary>
        public static List<(string itemId, float amount)> Cost(ResourceFamily family) => new List<(string, float)>
        {
            (ProcessingCatalog.BasicPartId, PartsCost),
            (MaterialIdFor(family), MaterialCost)
        };
    }
}
