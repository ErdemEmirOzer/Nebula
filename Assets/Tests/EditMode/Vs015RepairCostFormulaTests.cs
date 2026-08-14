using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §3: structure/vehicle/recovery repair cost formulas, ceiling rounding per item, and the field/combat caps.</summary>
    public class Vs015RepairCostFormulaTests
    {
        [Test]
        public void StructureRepairItemCost_IsBaseTimesFractionTimesHalf_RoundedUp()
        {
            // 30 base * 0.5 fraction * 0.5 = 7.5 -> ceil = 8
            Assert.AreEqual(8, EngineerRepairCatalog.StructureRepairItemCost(30f, 0.5f));
            // 44 * 0.23 * 0.5 = 5.06 -> ceil = 6 (kept clear of any integer boundary to avoid float-precision flakiness)
            Assert.AreEqual(6, EngineerRepairCatalog.StructureRepairItemCost(44f, 0.23f));
            // tiny fraction still costs at least 1 if nonzero
            Assert.AreEqual(1, EngineerRepairCatalog.StructureRepairItemCost(10f, 0.01f));
        }

        [Test]
        public void VehicleRepairItemCost_IsBaseTimesFractionTimesPoint3_RoundedUp()
        {
            // 33 base * 0.37 missing * 0.3 = 3.663 -> ceil = 4 (kept well clear of any integer boundary to avoid float-precision flakiness)
            Assert.AreEqual(4, EngineerRepairCatalog.VehicleRepairItemCost(33f, 0.37f));
            // 50 * 0.41 * 0.3 = 6.15 -> ceil = 7
            Assert.AreEqual(7, EngineerRepairCatalog.VehicleRepairItemCost(50f, 0.41f));
        }

        [Test]
        public void RecoveryItemCost_IsTenPercentOfTierRecipe_RoundedUp()
        {
            // 75 basic parts * 0.10 = 7.5 -> 8
            Assert.AreEqual(8, EngineerRepairCatalog.RecoveryItemCost(75f));
        }

        [Test]
        public void RecoveryCompleteHullFraction_IsExactlyQuarter()
        {
            Assert.AreEqual(0.25f, EngineerRepairCatalog.RecoveryCompleteHullFraction);
        }

        [Test]
        public void FieldCaps_AreExactlySixtyPercent()
        {
            Assert.AreEqual(0.60f, EngineerRepairCatalog.StructureFieldCapFraction);
            Assert.AreEqual(0.60f, EngineerRepairCatalog.VehicleCombatCapFraction);
        }

        [Test]
        public void NonCombatVehicleCap_IsFullHundredPercent()
        {
            Assert.AreEqual(1.00f, EngineerRepairCatalog.VehicleNonCombatCapFraction);
        }

        [Test]
        public void StructureRepairCost_MultiItemRecipe_EachItemRoundedIndependently()
        {
            var recipe = new List<(string itemId, float amount)> { (ProcessingCatalog.BasicPartId, 35f), ("material.industrial_diamond", 12f) };
            List<(string itemId, int amount)> cost = EngineerRepairCatalog.StructureRepairCost(recipe, 0.3f);
            // 35*0.3*0.5=5.25->6 ; 12*0.3*0.5=1.8->2
            Assert.AreEqual(2, cost.Count);
            Assert.AreEqual(6, cost[0].amount);
            Assert.AreEqual(2, cost[1].amount);
        }

        [Test]
        public void RecoveryCost_ZeroBaseAmount_YieldsNoLineItem()
        {
            var recipe = new List<(string itemId, float amount)> { (ProcessingCatalog.BasicPartId, 0f) };
            List<(string itemId, int amount)> cost = EngineerRepairCatalog.RecoveryCost(recipe);
            Assert.AreEqual(0, cost.Count);
        }
    }
}
