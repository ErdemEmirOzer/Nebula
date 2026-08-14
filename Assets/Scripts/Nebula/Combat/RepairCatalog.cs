using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Combat
{
    /// <summary>
    /// Pure data + cost table for the physical ship repair transaction
    /// (VS-013.1 §5). Repair proceeds in 10%-of-combined-Armor+Hull-capacity
    /// packages at a combined 1%/s rate (so one package always takes exactly
    /// 10 real seconds, independent of the ship's actual max capacity — the
    /// rate and package size cancel out). Only Hull Form I-II package costs
    /// are reachable this slice (CombatCatalog.MaxHullFormThisSlice); III-V
    /// are preserved catalog/test data only, following the same real-vs-
    /// provisional-scope pattern as every other "future tier" table in this
    /// project.
    /// </summary>
    public static class RepairCatalog
    {
        public const float RatePerSecondOfCombinedMax = 0.01f;
        public const float PackageFraction = 0.10f;
        public const float PackageDurationSeconds = PackageFraction / RatePerSecondOfCombinedMax; // 10s, independent of capacity

        public const float FieldCapFraction = 0.60f;
        public const float SafeZoneCapFraction = 1.00f;
        public const float RefundFraction = SurfaceStructureCatalog.RefundFraction;

        public const float RequiredSafeSecondsSinceLastDamage = 8f;
        public const float RequiredMaxShipSpeed = 25f;
        public const string RepairCargoOwnerId = "ship-repair";

        /// <summary>VS-013.2: physical world-unit radius for the 100% safe-repair cap, measured from either the Safety Marker or a live active Command Center structure.</summary>
        public const float RepairSafeRadius = 1500f;

        private const string MetalAlloyId = "material.metal_alloy";
        private const string IndustrialDiamondId = "material.industrial_diamond";
        private const string EnergyNuclearId = "energy.nuclear";

        /// <summary>One 10%-package's resource cost for the given Hull Form (1-5).</summary>
        public static List<(string itemId, float amount)> PackageCost(int hullForm)
        {
            switch (Mathf.Clamp(hullForm, 1, 5))
            {
                case 1: return new List<(string, float)> { (ProcessingCatalog.BasicPartId, 2f) };
                case 2: return new List<(string, float)> { (ProcessingCatalog.BasicPartId, 3f), (MetalAlloyId, 1f) };
                case 3: return new List<(string, float)> { (ProcessingCatalog.BasicPartId, 5f), (MetalAlloyId, 2f) };
                case 4: return new List<(string, float)> { (ProcessingCatalog.BasicPartId, 8f), (MetalAlloyId, 3f), (IndustrialDiamondId, 1f) };
                default: return new List<(string, float)> { (ProcessingCatalog.BasicPartId, 12f), (MetalAlloyId, 5f), (IndustrialDiamondId, 2f), (EnergyNuclearId, 2f) };
            }
        }

        /// <summary>
        /// VS-013.2: the still-UNCONSUMED cost of a package at a given save-time
        /// progress (0..1) — a package that was already <c>packageProgress01</c> paid
        /// for and physically consumed before save has only this fraction left to
        /// reserve on restore, never the full package cost again.
        /// </summary>
        public static List<(string itemId, float amount)> RemainingCost(int hullForm, float packageProgress01)
        {
            float remainingFraction = 1f - Mathf.Clamp01(packageProgress01);
            List<(string itemId, float amount)> full = PackageCost(hullForm);
            var remaining = new List<(string, float)>(full.Count);
            foreach (var (itemId, amount) in full) remaining.Add((itemId, amount * remainingFraction));
            return remaining;
        }
    }
}
