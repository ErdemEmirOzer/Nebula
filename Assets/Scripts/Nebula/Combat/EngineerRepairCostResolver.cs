using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Combat
{
    /// <summary>
    /// VS-015 §3/§5/§7: Engineer field-repair caps and the pure cost formulas
    /// for structure/vehicle repair and disabled-vehicle recovery. The main
    /// SHIP keeps using <see cref="RepairCatalog"/>/<see cref="ShipRepairService"/>
    /// untouched — this class only prices structure/drill/support and vehicle
    /// targets, which never had a repair cost model before VS-015.
    /// </summary>
    public static class EngineerRepairCatalog
    {
        /// <summary>Field cap for the ship and structures while an Engineer works on them away from a safe zone.</summary>
        public const float StructureFieldCapFraction = 0.60f;
        public const float StructureSafeCapFraction = 0.60f; // VS-015 §3: field cap for structures/ship is 60% ARMOR and 60% HULL regardless of safe-zone — see spec §3.
        /// <summary>Vehicle repair cap while any combat is active in the system vs. fully out of combat.</summary>
        public const float VehicleCombatCapFraction = 0.60f;
        public const float VehicleNonCombatCapFraction = 1.00f;

        public const float RecoveryCostFraction = 0.10f;
        public const float RecoveryCompleteHullFraction = 0.25f;

        /// <summary>Structure/drill/support repair item cost: base build recipe amount * actually-restored durability fraction * 0.5, each item rounded UP.</summary>
        public static int StructureRepairItemCost(float baseRecipeAmount, float restoredFraction) =>
            Mathf.CeilToInt(baseRecipeAmount * Mathf.Clamp01(restoredFraction) * 0.5f);

        /// <summary>Vehicle repair item cost: the target's real tier production recipe amount * missing fraction * 0.3, each item rounded UP.</summary>
        public static int VehicleRepairItemCost(float baseTierRecipeAmount, float missingFraction) =>
            Mathf.CeilToInt(baseTierRecipeAmount * Mathf.Clamp01(missingFraction) * 0.3f);

        /// <summary>Disabled-vehicle recovery cost: 10% of the vehicle's tier production recipe, each item rounded UP.</summary>
        public static int RecoveryItemCost(float baseTierRecipeAmount) =>
            Mathf.CeilToInt(baseTierRecipeAmount * RecoveryCostFraction);

        public static List<(string itemId, int amount)> StructureRepairCost(IReadOnlyList<(string itemId, float amount)> baseRecipe, float restoredFraction)
        {
            var list = new List<(string, int)>();
            foreach (var (itemId, amount) in baseRecipe)
            {
                int cost = StructureRepairItemCost(amount, restoredFraction);
                if (cost > 0) list.Add((itemId, cost));
            }
            return list;
        }

        public static List<(string itemId, int amount)> VehicleRepairCost(IReadOnlyList<(string itemId, float amount)> baseTierRecipe, float missingFraction)
        {
            var list = new List<(string, int)>();
            foreach (var (itemId, amount) in baseTierRecipe)
            {
                int cost = VehicleRepairItemCost(amount, missingFraction);
                if (cost > 0) list.Add((itemId, cost));
            }
            return list;
        }

        public static List<(string itemId, int amount)> RecoveryCost(IReadOnlyList<(string itemId, float amount)> baseTierRecipe)
        {
            var list = new List<(string, int)>();
            foreach (var (itemId, amount) in baseTierRecipe)
            {
                int cost = RecoveryItemCost(amount);
                if (cost > 0) list.Add((itemId, cost));
            }
            return list;
        }
    }
}
