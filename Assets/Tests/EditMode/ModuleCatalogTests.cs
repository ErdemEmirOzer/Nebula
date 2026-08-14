using System.Linq;
using NUnit.Framework;
using Nebula.Modules;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure module catalog data (VS-005 §8, tests 1-3, 5).</summary>
    public class ModuleCatalogTests
    {
        [Test]
        public void Catalog_Has12UniqueIdsAcrossFourBranches()
        {
            Assert.AreEqual(12, ModuleCatalog.Modules.Length);
            Assert.AreEqual(12, ModuleCatalog.Modules.Select(m => m.Id).Distinct().Count());
            var branches = ModuleCatalog.Modules.Select(m => m.Branch).Distinct().ToList();
            Assert.AreEqual(4, branches.Count);
        }

        [Test]
        public void Catalog_EveryModuleHasCompleteT1ToT5Tables()
        {
            foreach (ModuleDef def in ModuleCatalog.Modules)
            {
                Assert.AreEqual(5, def.TierCosts.Length, def.Id);
                foreach (ModuleTierCost cost in def.TierCosts)
                {
                    Assert.IsNotNull(cost.Resources, def.Id);
                    Assert.GreaterOrEqual(cost.DevelopmentPoints, 0, def.Id);
                    Assert.GreaterOrEqual(cost.Seconds, 0f, def.Id);
                }
            }
        }

        [Test]
        public void Catalog_TotalSpendablePointsIs93()
        {
            Assert.AreEqual(93, ModuleCatalog.TotalSpendablePoints());
        }

        [Test]
        public void Catalog_ValidateReturnsNoErrors()
        {
            var errors = ModuleCatalog.Validate();
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Catalog_TierResourceFormulaMatchesSpec()
        {
            ModuleDef def = ModuleCatalog.Get("industry.logistics_cargo");
            Assert.AreEqual(20f, SumBasicPart(def.TierCosts[0]));
            Assert.AreEqual(40f, SumBasicPart(def.TierCosts[1]));
            Assert.AreEqual(70f, SumBasicPart(def.TierCosts[2]));
            Assert.AreEqual(120f, SumBasicPart(def.TierCosts[3]));
            Assert.AreEqual(200f, SumBasicPart(def.TierCosts[4]));

            int[] expectedPoints = { 1, 1, 1, 2, 3 };
            for (int i = 0; i < 5; i++) Assert.AreEqual(expectedPoints[i], def.TierCosts[i].DevelopmentPoints);

            float[] expectedSeconds = { 30f, 90f, 180f, 360f, 720f };
            for (int i = 0; i < 5; i++) Assert.AreEqual(expectedSeconds[i], def.TierCosts[i].Seconds);
        }

        private static float SumBasicPart(ModuleTierCost cost) =>
            cost.Resources.Where(r => r.ItemId == "part.basic").Sum(r => r.Amount);

        [Test]
        public void Catalog_LevelGates_T2_T3_T4_T5_AreGlobal21_41_61_81()
        {
            ModuleDef def = ModuleCatalog.Get("industry.resource_processing");
            Assert.AreEqual(21, def.RequiredLevelForTier(2));
            Assert.AreEqual(41, def.RequiredLevelForTier(3));
            Assert.AreEqual(61, def.RequiredLevelForTier(4));
            Assert.AreEqual(81, def.RequiredLevelForTier(5));
        }

        [Test]
        public void Catalog_FunctionalModulesThisSlice_CargoProcessingScannerEngineNav()
        {
            // VS-005: the two Industry modules are fully functional (T1-T5).
            Assert.AreEqual(ModuleDef.MaxTier, ModuleCatalog.Get("industry.logistics_cargo").MaxTierThisSlice);
            Assert.AreEqual(ModuleDef.MaxTier, ModuleCatalog.Get("industry.resource_processing").MaxTierThisSlice);
            // VS-011: Station Construction becomes functional up to T2 (System Logistics Center T1/T2).
            Assert.AreEqual(2, ModuleCatalog.Get("industry.station_construction").MaxTierThisSlice);
            Assert.IsTrue(ModuleCatalog.Get("industry.station_construction").FunctionalThisSlice);

            // VS-006: Scanner and Engine-Navigation become functional up to T2.
            Assert.AreEqual(2, ModuleCatalog.Get("exploration.scanner").MaxTierThisSlice);
            Assert.AreEqual(2, ModuleCatalog.Get("exploration.engine_navigation").MaxTierThisSlice);
            Assert.IsTrue(ModuleCatalog.Get("exploration.scanner").FunctionalThisSlice);
            Assert.IsTrue(ModuleCatalog.Get("exploration.engine_navigation").FunctionalThisSlice);

            // VS-007: Hangar-Bot also becomes functional up to T2 (production/fleet); no longer in this "still locked" list.
            Assert.AreEqual(2, ModuleCatalog.Get("exploration.hangar_bot").MaxTierThisSlice);
            Assert.IsTrue(ModuleCatalog.Get("exploration.hangar_bot").FunctionalThisSlice);

            // VS-013/VS-013.1: Hull Reinforcement, Weapon Coordination and (VS-013.1) Shield
            // Systems all become functional up to T2; no longer "still locked".
            Assert.AreEqual(2, ModuleCatalog.Get("defense.hull_reinforcement").MaxTierThisSlice);
            Assert.IsTrue(ModuleCatalog.Get("defense.hull_reinforcement").FunctionalThisSlice);
            Assert.AreEqual(2, ModuleCatalog.Get("defense.weapon_coordination").MaxTierThisSlice);
            Assert.IsTrue(ModuleCatalog.Get("defense.weapon_coordination").FunctionalThisSlice);
            Assert.AreEqual(2, ModuleCatalog.Get("defense.shield_systems").MaxTierThisSlice);
            Assert.IsTrue(ModuleCatalog.Get("defense.shield_systems").FunctionalThisSlice);

            string[] stillLocked =
            {
                "nuclear.star_energy_storage", "nuclear.laboratory", "nuclear.interstellar_logistics"
            };
            foreach (string id in stillLocked)
            {
                ModuleDef def = ModuleCatalog.Get(id);
                Assert.AreEqual(0, def.MaxTierThisSlice, id);
                Assert.IsFalse(def.FunctionalThisSlice, id);
            }
        }

        [Test]
        public void Catalog_ScannerAndEngineNav_LevelGatesAndResourceRoles()
        {
            ModuleDef scanner = ModuleCatalog.Get("exploration.scanner");
            Assert.AreEqual(5, scanner.UnlockLevel);
            Assert.AreEqual(21, scanner.RequiredLevelForTier(2));
            Assert.IsTrue(scanner.TierCosts[1].Resources.Any(r => r.ItemId == "material.industrial_diamond"));
            Assert.IsTrue(scanner.TierCosts[2].Resources.Any(r => r.ItemId == "material.biocomposite"));

            ModuleDef engine = ModuleCatalog.Get("exploration.engine_navigation");
            Assert.AreEqual(10, engine.UnlockLevel);
            Assert.AreEqual(21, engine.RequiredLevelForTier(2));
            Assert.IsTrue(engine.TierCosts[1].Resources.Any(r => r.ItemId == "material.refined_gas"));
            Assert.IsTrue(engine.TierCosts[2].Resources.Any(r => r.ItemId == "material.purified_water"));
        }

        [Test]
        public void Catalog_ScannerAndEngineNav_TierEffectTablesMatchSpec()
        {
            CollectionAssert.AreEqual(new[] { 2f, 3f }, ModuleCatalog.ScanRadiusMultiplierByTier);
            CollectionAssert.AreEqual(new[] { 6f, 4f }, ModuleCatalog.ScanSecondsByTier);
            CollectionAssert.AreEqual(new[] { 8f, 4f }, ModuleCatalog.SignalAnalysisSecondsByTier);
            CollectionAssert.AreEqual(new[] { 180f, 194.4f }, ModuleCatalog.LocalTopSpeedByTier);
            CollectionAssert.AreEqual(new[] { 1800f, 4500f }, ModuleCatalog.CruiseSpeedByTier);
        }
    }
}
