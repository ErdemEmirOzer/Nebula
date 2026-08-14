using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Content;

namespace Nebula.Tests.EditMode
{
    /// <summary>A fully empty, controllable IContentCatalog test double — every list defaults empty and settable, so each test only populates what it's actually exercising.</summary>
    internal sealed class FakeContentCatalog : IContentCatalog
    {
        public List<ContentItem> Items { get; } = new List<ContentItem>();
        public List<ContentRecipe> Recipes { get; } = new List<ContentRecipe>();
        public List<ContentModule> Modules { get; } = new List<ContentModule>();
        public List<ContentVehicle> Vehicles { get; } = new List<ContentVehicle>();
        public List<ContentStructure> Structures { get; } = new List<ContentStructure>();
        public List<ContentLogisticsCenterTier> LogisticsCenterTiers { get; } = new List<ContentLogisticsCenterTier>();
        public List<ContentWeapon> Weapons { get; } = new List<ContentWeapon>();
        public List<ContentEnemy> Enemies { get; } = new List<ContentEnemy>();
        public List<ContentPoiType> PoiTypes { get; } = new List<ContentPoiType>();
        public List<string> FreeStartingModuleIds { get; } = new List<string>();

        IReadOnlyList<ContentItem> IContentCatalog.Items => Items;
        IReadOnlyList<ContentRecipe> IContentCatalog.Recipes => Recipes;
        IReadOnlyList<ContentModule> IContentCatalog.Modules => Modules;
        IReadOnlyList<ContentVehicle> IContentCatalog.Vehicles => Vehicles;
        IReadOnlyList<ContentStructure> IContentCatalog.Structures => Structures;
        IReadOnlyList<ContentLogisticsCenterTier> IContentCatalog.LogisticsCenterTiers => LogisticsCenterTiers;
        IReadOnlyList<ContentWeapon> IContentCatalog.Weapons => Weapons;
        IReadOnlyList<ContentEnemy> IContentCatalog.Enemies => Enemies;
        IReadOnlyList<ContentPoiType> IContentCatalog.PoiTypes => PoiTypes;
        IReadOnlyList<string> IContentCatalog.FreeStartingModuleIds => FreeStartingModuleIds;

        private static ContentModuleTierCost EmptyTier(int tier) => new ContentModuleTierCost(tier, 0, 0f, new List<ContentResourceCost>());

        public static List<ContentModuleTierCost> FiveEmptyTiers()
        {
            var list = new List<ContentModuleTierCost>();
            for (int t = 1; t <= 5; t++) list.Add(EmptyTier(t));
            return list;
        }
    }

    /// <summary>TF-003 EditMode tests for ContentCatalogValidator's stable-id and cross-reference rules.</summary>
    public class ContentCatalogValidatorTests
    {
        [Test]
        public void RealCSharpContentCatalog_ProducesZeroValidationErrors()
        {
            List<string> errors = ContentCatalogValidator.Validate(new CSharpContentCatalog());
            CollectionAssert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void EmptyId_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Items.Add(new ContentItem("", "Empty", 1f));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("invalid id")));
        }

        [Test]
        public void UppercaseId_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Items.Add(new ContentItem("Item.Bad", "Bad", 1f));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("uppercase")));
        }

        [Test]
        public void WhitespaceId_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Items.Add(new ContentItem(" item.bad ", "Bad", 1f));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("whitespace")));
        }

        [Test]
        public void DuplicateId_InTheSameDomain_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Items.Add(new ContentItem("item.dup", "A", 1f));
            fake.Items.Add(new ContentItem("item.dup", "B", 1f));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("duplicate id")));
        }

        [Test]
        public void DuplicateId_AcrossDifferentDomains_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Items.Add(new ContentItem("shared.id", "Item", 1f));
            fake.Enemies.Add(new ContentEnemy("shared.id", "Enemy", 1f, 1f, 1f, 1f, false, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("duplicate id") && e.Contains("shared.id")));
        }

        [Test]
        public void RecipeWithMissingInputItem_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Items.Add(new ContentItem("item.real_output", "Out", 1f));
            fake.Recipes.Add(new ContentRecipe("recipe.bad", "item.missing_input", 2, "item.real_output", 1, 1f, "Bad"));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("unknown item id 'item.missing_input'")));
        }

        [Test]
        public void RecipeWithMissingOutputItem_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Items.Add(new ContentItem("item.real_input", "In", 1f));
            fake.Recipes.Add(new ContentRecipe("recipe.bad", "item.real_input", 2, "item.missing_output", 1, 1f, "Bad"));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("unknown item id 'item.missing_output'")));
        }

        [Test]
        public void ModuleWithMissingCostItem_IsRejected()
        {
            var fake = new FakeContentCatalog();
            var tiers = FakeContentCatalog.FiveEmptyTiers();
            tiers[0] = new ContentModuleTierCost(1, 1, 30f, new List<ContentResourceCost> { new ContentResourceCost("item.missing", 10f) });
            fake.Modules.Add(new ContentModule("module.test", "Test", 1, 5, false, tiers));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("unknown item id 'item.missing'")));
        }

        [Test]
        public void VehicleWithMissingUpgradeCostItem_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Vehicles.Add(new ContentVehicle("vehicle.test", "Test", 1, 1, 1, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f, 0, 0,
                new List<ContentResourceCost> { new ContentResourceCost("item.missing", 5f) }));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("unknown item id 'item.missing'")));
        }

        [Test]
        public void WeaponWithMissingCostItem_IsRejected()
        {
            var fake = new FakeContentCatalog();
            var tierCosts = new List<ContentWeaponTierCost> { new ContentWeaponTierCost(1, new List<ContentResourceCost> { new ContentResourceCost("item.missing", 5f) }, 10f) };
            fake.Weapons.Add(new ContentWeapon("weapon.test", "Test", 1, 10f, 100f, tierCosts));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("unknown item id 'item.missing'")));
        }

        [Test]
        public void StructureWithPartsCost_ButNoBasicPartItemRegistered_IsRejected()
        {
            var fake = new FakeContentCatalog(); // Items left empty -- part.basic is never registered
            fake.Structures.Add(new ContentStructure("structure.test", "Test", 10, 5, true, 20f, 0f, 100f, 100f));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("unknown item id")));
        }

        [Test]
        public void LogisticsCenterTierWithMissingCostItem_IsRejected()
        {
            var fake = new FakeContentCatalog();
            var costs = new List<ContentResourceCost> { new ContentResourceCost("item.missing", 10f) };
            fake.LogisticsCenterTiers.Add(new ContentLogisticsCenterTier("station.test.t1", "Test", 1, 1, 1, costs, 10f, 100f, 1, 1, 1f, 1f, 1f, 1f));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("unknown item id 'item.missing'")));
        }

        [Test]
        public void PoiTypeWithMissingCostItem_IsRejected()
        {
            var fake = new FakeContentCatalog();
            var costs = new List<ContentResourceCost> { new ContentResourceCost("item.missing", 5f) };
            fake.PoiTypes.Add(new ContentPoiType("poi.test", "Test", 1f, 1f, 100, costs));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("unknown item id 'item.missing'")));
        }

        [Test]
        public void MissingStartingModule_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Modules.Add(new ContentModule("module.real", "Real", 1, 5, false, FakeContentCatalog.FiveEmptyTiers()));
            fake.FreeStartingModuleIds.Add("module.does_not_exist");
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("does not correspond to any real module")));
        }

        [Test]
        public void NegativeAmount_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Items.Add(new ContentItem("item.real", "Real", 1f));
            var tiers = FakeContentCatalog.FiveEmptyTiers();
            tiers[0] = new ContentModuleTierCost(1, 1, 30f, new List<ContentResourceCost> { new ContentResourceCost("item.real", -5f) });
            fake.Modules.Add(new ContentModule("module.test", "Test", 1, 5, false, tiers));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("negative amount")));
        }

        [Test]
        public void NegativeSeconds_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Recipes.Add(new ContentRecipe("recipe.test", null, 1, null, 1, -1f, "Test"));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("negative Seconds")));
        }

        [Test]
        public void NegativeStat_OnEnemy_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Enemies.Add(new ContentEnemy("enemy.test", "Test", -1f, 0f, 0f, 0f, false, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("negative Speed")));
        }

        [Test]
        public void WeaponTierCostCount_NotMatchingMaxTierThisSlice_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Weapons.Add(new ContentWeapon("weapon.test", "Test", 2, 1f, 1f, new List<ContentWeaponTierCost>())); // MaxTierThisSlice=2 but 0 tier costs
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("expected 2 tier cost")));
        }

        [Test]
        public void ModuleTierCostCount_NotFive_IsRejected()
        {
            var fake = new FakeContentCatalog();
            fake.Modules.Add(new ContentModule("module.test", "Test", 1, 1, false, new List<ContentModuleTierCost> { FakeContentCatalogTierCost() }));
            List<string> errors = ContentCatalogValidator.Validate(fake);
            Assert.IsTrue(errors.Exists(e => e.Contains("expected 5 tier costs")));
        }

        private static ContentModuleTierCost FakeContentCatalogTierCost() => new ContentModuleTierCost(1, 0, 0f, new List<ContentResourceCost>());

        [Test]
        public void Errors_AreReturnedInADeterministicOrder_AcrossRepeatedRuns()
        {
            var fake = new FakeContentCatalog();
            fake.Items.Add(new ContentItem("Bad.One", "A", 1f));
            fake.Items.Add(new ContentItem("Bad.Two", "B", 1f));
            fake.Enemies.Add(new ContentEnemy("Bad.Three", "C", -1f, 0f, 0f, 0f, false, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f));

            List<string> first = ContentCatalogValidator.Validate(fake);
            List<string> second = ContentCatalogValidator.Validate(fake);
            CollectionAssert.AreEqual(first, second);
        }
    }
}
