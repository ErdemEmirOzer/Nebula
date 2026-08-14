using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Modules;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the Hangar-Bot module gate (VS-007 §1): T1 needs
    /// Level 10 + a real Controlled-planet predicate (not just an
    /// informational label like every other module's milestone), T2 needs
    /// the standard global Level 21 gate, and command/production tables.
    /// </summary>
    public class HangarModuleGateTests
    {
        [SetUp]
        [TearDown]
        public void ResetExtractionState() => ExtractionState.Reset(123);

        [Test]
        public void Catalog_HangarBot_IsFunctionalUpToT2()
        {
            ModuleDef def = ModuleCatalog.Get("exploration.hangar_bot");
            Assert.AreEqual(2, def.MaxTierThisSlice);
            Assert.AreEqual(10, def.UnlockLevel);
            Assert.AreEqual(21, def.RequiredLevelForTier(2));
            Assert.AreEqual(ModuleCatalog.LockedMessageExplorationCap, def.LockedMessage);
        }

        [Test]
        public void Catalog_HangarBot_UsesMetalAndBiocompositeMaterialRoles()
        {
            ModuleDef def = ModuleCatalog.Get("exploration.hangar_bot");
            ModuleResourceCost t1Primary = def.TierCosts[1].Resources[1]; // T2 line: parts, then primary material
            Assert.AreEqual("material.metal_alloy", t1Primary.ItemId);
        }

        [Test]
        public void CommandCapacityTable_Is2At1And4At2()
        {
            Assert.AreEqual(2, ModuleCatalog.HangarCommandCapacityByTier[0]);
            Assert.AreEqual(4, ModuleCatalog.HangarCommandCapacityByTier[1]);
        }

        [Test]
        public void ProductionSlotsTable_IsAlwaysOne()
        {
            Assert.AreEqual(1, ModuleCatalog.HangarProductionSlotsByTier[0]);
            Assert.AreEqual(1, ModuleCatalog.HangarProductionSlotsByTier[1]);
        }

        [Test]
        public void Milestone_UnsatisfiedWithoutAnyControlledPlanet()
        {
            Assert.IsFalse(ExtractionState.HasAnyControlledPlanet());
            Assert.IsFalse(ModuleMilestones.IsSatisfied("exploration.hangar_bot"));
        }

        [Test]
        public void Milestone_SatisfiedOnceAnyPlanetIsControlled()
        {
            ExtractionState.GetDominance("sys000/pl00").Restore(DominanceState.Controlled, 0f, 0f);
            Assert.IsTrue(ExtractionState.HasAnyControlledPlanet());
            Assert.IsTrue(ModuleMilestones.IsSatisfied("exploration.hangar_bot"));
        }

        [Test]
        public void Milestone_OtherModules_AlwaysSatisfied_Unaffected()
        {
            Assert.IsTrue(ModuleMilestones.IsSatisfied("exploration.scanner"));
            Assert.IsTrue(ModuleMilestones.IsSatisfied("industry.logistics_cargo"));
        }

        [Test]
        public void Validator_HangarT1_RejectsMilestoneNotMetEvenAtSufficientLevel()
        {
            ModuleDef def = ModuleCatalog.Get("exploration.hangar_bot");
            UpgradeRejectReason reason = ModuleUpgradeValidator.Validate(def, currentTier: 0, targetTier: 1,
                characterLevel: 50, milestoneSatisfied: ModuleMilestones.IsSatisfied(def.Id),
                queueBusy: false, availablePoints: 99, cargo: null);
            Assert.AreEqual(UpgradeRejectReason.MilestoneNotMet, reason);
        }

        [Test]
        public void Validator_HangarT1_PassesOnceControlledPlanetExists()
        {
            ExtractionState.GetDominance("sys000/pl00").Restore(DominanceState.Controlled, 0f, 0f);
            ModuleDef def = ModuleCatalog.Get("exploration.hangar_bot");
            UpgradeRejectReason reason = ModuleUpgradeValidator.Validate(def, currentTier: 0, targetTier: 1,
                characterLevel: 50, milestoneSatisfied: ModuleMilestones.IsSatisfied(def.Id),
                queueBusy: false, availablePoints: 99, cargo: null);
            Assert.AreEqual(UpgradeRejectReason.None, reason);
        }
    }
}
