using System.Linq;
using NUnit.Framework;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Poi;

namespace Nebula.Tests.EditMode
{
    /// <summary>TF-003 EditMode tests for the immutable RuntimeCatalog snapshot built from the real static C# catalogs — pure, no scene/prefab/Unity asset loading required.</summary>
    public class RuntimeCatalogTests
    {
        private static RuntimeCatalog BuildReal() => RuntimeCatalogBuilder.Build(new CSharpContentCatalog());

        [Test]
        public void EveryDomain_SnapshotCountMatchesTheRealSourceCatalog()
        {
            RuntimeCatalog catalog = BuildReal();
            Assert.AreEqual(5 + 1 + 5 + 2, catalog.Items.Count, "5 raw resources + part.basic + 5 materials + energy.nuclear + core.nuclear");
            Assert.AreEqual(ProcessingCatalog.Recipes.Length, catalog.Recipes.Count);
            Assert.AreEqual(ModuleCatalog.Modules.Length, catalog.Modules.Count);
            Assert.AreEqual(VehicleCatalog.All.Length, catalog.Vehicles.Count);
            Assert.AreEqual(10, catalog.Structures.Count, "Command + Laser + VS-014 Command T2/Laser T2/Plasma T1/Plasma T2 + VS-015 Sensor Array T1/T2 + Maintenance Platform T1/T2");
            Assert.AreEqual(2, catalog.LogisticsCenterTiers.Count, "T1 + T2");
            Assert.AreEqual(3, catalog.Weapons.Count, "Laser + Plasma + EM");
            Assert.AreEqual(6, catalog.Enemies.Count, "Scout Probe + Fighter + VS-014 Looter/Disruptor/Heavy/Siege");
            Assert.AreEqual(4, catalog.PoiTypes.Count, "Anomaly + Wreck + Abandoned Facility + VS-014 Pirate Base");
        }

        [Test]
        public void EveryAdapterDefinition_IsPresentInTheSnapshot()
        {
            RuntimeCatalog catalog = BuildReal();
            foreach (string id in ExtractionCatalog.ResourceIds) Assert.IsTrue(catalog.Items.ContainsKey(id), id);
            foreach (string id in ProcessingCatalog.MaterialIds) Assert.IsTrue(catalog.Items.ContainsKey(id), id);
            Assert.IsTrue(catalog.Items.ContainsKey(ProcessingCatalog.BasicPartId));
            Assert.IsTrue(catalog.Items.ContainsKey(ModuleCatalog.EnergyNuclearId));
            Assert.IsTrue(catalog.Items.ContainsKey(ModuleCatalog.CoreNuclearId));
            foreach (ModuleDef def in ModuleCatalog.Modules) Assert.IsTrue(catalog.Modules.ContainsKey(def.Id), def.Id);
            foreach (VehicleDef def in VehicleCatalog.All) Assert.IsTrue(catalog.Vehicles.ContainsKey(def.Id), def.Id);
        }

        [Test]
        public void IndustryLogisticsCargo_MatchesSourceExactly()
        {
            ContentModule m = BuildReal().GetRequiredModule("industry.logistics_cargo");
            Assert.AreEqual("Logistics Cargo", m.DisplayName);
            Assert.AreEqual(1, m.UnlockLevel);
            Assert.AreEqual(5, m.MaxTierThisSlice);
            Assert.IsTrue(m.FreeStarting);
            Assert.AreEqual(5, m.TierCosts.Count);

            ContentModuleTierCost t1 = m.TierCosts[0];
            Assert.AreEqual(1, t1.DevelopmentPoints); Assert.AreEqual(30f, t1.Seconds, 0.001f);
            Assert.AreEqual(1, t1.Resources.Count);
            Assert.AreEqual(ProcessingCatalog.BasicPartId, t1.Resources[0].ItemId);
            Assert.AreEqual(20f, t1.Resources[0].Amount, 0.001f);

            ContentModuleTierCost t2 = m.TierCosts[1];
            Assert.AreEqual(40f, t2.Resources.First(r => r.ItemId == ProcessingCatalog.BasicPartId).Amount, 0.001f);
            Assert.AreEqual(10f, t2.Resources.First(r => r.ItemId == "material.biocomposite").Amount, 0.001f);
        }

        [Test]
        public void VehicleCollectorMk2_MatchesSourceExactly_IncludingUpgradeCost()
        {
            ContentVehicle v = BuildReal().GetRequiredVehicle(VehicleCatalog.CollectorMk2Id);
            Assert.AreEqual("Collector Mk II", v.DisplayName);
            Assert.AreEqual(21, v.UnlockLevel); Assert.AreEqual(2, v.RequiredHangarTier); Assert.AreEqual(2, v.Mk);
            Assert.AreEqual(70f, v.BuildSeconds, 0.001f);
            Assert.AreEqual(154f, v.Speed, 0.001f); Assert.AreEqual(150f, v.Shield, 0.001f); Assert.AreEqual(270f, v.Armor, 0.001f); Assert.AreEqual(380f, v.Hull, 0.001f);
            Assert.AreEqual(0f, v.ScanRange); Assert.AreEqual(0f, v.ScanSeconds);
            Assert.AreEqual(80f, v.CargoCapacity, 0.001f); Assert.AreEqual(20f, v.TransferVolumePerSecond, 0.001f);
            Assert.AreEqual(50, v.BasicPartCost); Assert.AreEqual(12, v.SpecialMaterialCost);

            Assert.AreEqual(3, v.UpgradeCost.Count);
            Assert.AreEqual(20f, v.UpgradeCost.First(c => c.ItemId == ProcessingCatalog.BasicPartId).Amount, 0.001f);
            Assert.AreEqual(12f, v.UpgradeCost.First(c => c.ItemId == VehicleCatalog.MetalAlloyId).Amount, 0.001f);
            Assert.AreEqual(8f, v.UpgradeCost.First(c => c.ItemId == VehicleCatalog.BiocompositeId).Amount, 0.001f);
        }

        [Test]
        public void WeaponPlasma_MatchesSourceExactly()
        {
            ContentWeapon w = BuildReal().GetRequiredWeapon("weapon.plasma");
            Assert.AreEqual(2, w.MaxTierThisSlice);
            Assert.AreEqual(Nebula.Combat.WeaponCatalog.PlasmaBaseDps, w.BaseDps, 0.001f);
            Assert.AreEqual(Nebula.Combat.WeaponCatalog.PlasmaBaseRange, w.BaseRange, 0.001f);
            Assert.AreEqual(2, w.TierCosts.Count);

            ContentWeaponTierCost t1 = w.TierCosts[0];
            Assert.AreEqual(1, t1.Tier); Assert.AreEqual(30f, t1.Seconds, 0.001f);
            Assert.AreEqual(15f, t1.Resources.First(r => r.ItemId == ProcessingCatalog.BasicPartId).Amount, 0.001f);
            Assert.AreEqual(5f, t1.Resources.First(r => r.ItemId == "material.metal_alloy").Amount, 0.001f);

            ContentWeaponTierCost t2 = w.TierCosts[1];
            Assert.AreEqual(60f, t2.Seconds, 0.001f);
            Assert.AreEqual(30f, t2.Resources.First(r => r.ItemId == ProcessingCatalog.BasicPartId).Amount, 0.001f);
            Assert.AreEqual(10f, t2.Resources.First(r => r.ItemId == "material.metal_alloy").Amount, 0.001f);
        }

        [Test]
        public void EnemyFighterMk1_MatchesSourceExactly()
        {
            ContentEnemy e = BuildReal().GetRequiredEnemy(Nebula.Combat.EnemyIds.FighterMk1);
            Assert.AreEqual("Fighter Mk I", e.DisplayName);
            Assert.AreEqual(200f, e.Speed, 0.001f);
            Assert.AreEqual(120f, e.ShieldMax, 0.001f); Assert.AreEqual(150f, e.ArmorMax, 0.001f); Assert.AreEqual(220f, e.HullMax, 0.001f);
            Assert.IsTrue(e.IsCombatCapable);
            Assert.AreEqual(55f, e.Dps, 0.001f); Assert.AreEqual(900f, e.AttackRange, 0.001f);
            Assert.AreEqual(650f, e.EngageBandMin, 0.001f); Assert.AreEqual(850f, e.EngageBandMax, 0.001f);
            Assert.AreEqual(300f, e.BreakDistance, 0.001f); Assert.AreEqual(1100f, e.ApproachThreshold, 0.001f);
            Assert.AreEqual(0.20f, e.RetreatHullFraction, 0.001f); Assert.AreEqual(0f, e.ScanSeconds);
        }

        [Test]
        public void LogisticsCenterT2_MatchesSourceExactly()
        {
            ContentLogisticsCenterTier t = BuildReal().GetRequiredLogisticsCenterTier($"{LogisticsCenterCatalog.CenterId}.t2");
            Assert.AreEqual(2, t.Tier); Assert.AreEqual(25, t.RequiredLevel); Assert.AreEqual(2, t.RequiredModuleTier);
            Assert.AreEqual(360f, t.BuildSeconds, 0.001f); Assert.AreEqual(1800f, t.Capacity, 0.001f);
            Assert.AreEqual(5, t.MaxRoutes); Assert.AreEqual(2, t.DockCount); Assert.AreEqual(40f, t.TransferVolumePerSecond, 0.001f);
            Assert.AreEqual(800f, t.ShieldMax, 0.001f); Assert.AreEqual(1600f, t.ArmorMax, 0.001f); Assert.AreEqual(2400f, t.HullMax, 0.001f);
            Assert.AreEqual(3, t.Costs.Count);
            Assert.AreEqual(150f, t.Costs.First(c => c.ItemId == ProcessingCatalog.BasicPartId).Amount, 0.001f);
            Assert.AreEqual(60f, t.Costs.First(c => c.ItemId == "material.metal_alloy").Amount, 0.001f);
            Assert.AreEqual(45f, t.Costs.First(c => c.ItemId == "material.biocomposite").Amount, 0.001f);
        }

        [Test]
        public void RecipeMaterialChain_MatchesSourceExactly()
        {
            // recipe.material.0 == the Metal family's raw -> metal_alloy recipe (ExtractionCatalog.ResourceIds[0] == "resource.metal").
            ContentRecipe r = BuildReal().GetRequiredRecipe("recipe.material.0");
            Assert.AreEqual("resource.metal", r.InputItemId);
            Assert.AreEqual(3, r.InputCount);
            Assert.AreEqual("material.metal_alloy", r.OutputItemId);
            Assert.AreEqual(1, r.OutputCount);
            Assert.AreEqual(4f, r.Seconds, 0.001f);
        }

        [Test]
        public void SourceArrayMutatedAfterBuild_NeverChangesTheAlreadyBuiltSnapshot()
        {
            RuntimeCatalog catalog = BuildReal();
            string original = ExtractionCatalog.ResourceIds[0];
            ContentItem before = catalog.GetRequiredItem(original);
            float originalVolume = before.VolumePerUnit;

            // Mutate the SOURCE array in place (readonly field, mutable array contents) after the snapshot was built.
            ExtractionCatalog.ResourceIds[0] = "resource.hacked";
            try
            {
                Assert.IsTrue(catalog.Items.ContainsKey(original), "the already-built snapshot must still have the original id");
                Assert.AreEqual(originalVolume, catalog.GetRequiredItem(original).VolumePerUnit, 0.001f);
                Assert.IsFalse(catalog.Items.ContainsKey("resource.hacked"), "the snapshot must never pick up a post-build mutation");
            }
            finally
            {
                ExtractionCatalog.ResourceIds[0] = original; // never leave global static state corrupted for other tests
            }
        }

        [Test]
        public void ExposedItemDictionary_CannotBeMutatedByACaller()
        {
            RuntimeCatalog catalog = BuildReal();
            // ReadOnlyDictionary<TKey,TValue> still implements IDictionary<TKey,TValue> (so foreach/LINQ keep working through
            // the interface), but every mutating member throws -- a caller downcasting past the declared
            // IReadOnlyDictionary<> surface still cannot actually change anything.
            var asDictionary = catalog.Items as System.Collections.Generic.IDictionary<string, ContentItem>;
            Assert.IsNotNull(asDictionary);
            Assert.Throws<System.NotSupportedException>(() => asDictionary[ProcessingCatalog.BasicPartId] = null);
            Assert.Throws<System.NotSupportedException>(() => asDictionary.Clear());
        }

        [Test]
        public void ExposedTierCostList_CannotBeMutatedByACaller()
        {
            ContentModule m = BuildReal().GetRequiredModule("industry.logistics_cargo");
            var asList = m.TierCosts as System.Collections.Generic.IList<ContentModuleTierCost>;
            Assert.Throws<System.NotSupportedException>(() => asList[0] = null, "a ReadOnlyCollection must reject writes even when a caller downcasts to IList<T>");
        }

        [Test]
        public void GetRequired_UnknownId_ThrowsContentValidationException()
        {
            RuntimeCatalog catalog = BuildReal();
            Assert.Throws<ContentValidationException>(() => catalog.GetRequiredItem("item.does_not_exist"));
            Assert.Throws<ContentValidationException>(() => catalog.GetRequiredModule("module.does_not_exist"));
            Assert.Throws<ContentValidationException>(() => catalog.GetRequiredWeapon("weapon.does_not_exist"));
        }

        [Test]
        public void TryGet_UnknownId_ReturnsFalse_NeverThrows()
        {
            RuntimeCatalog catalog = BuildReal();
            Assert.IsFalse(catalog.TryGetItem("item.does_not_exist", out _));
            Assert.IsFalse(catalog.TryGetEnemy("enemy.does_not_exist", out _));
            Assert.IsFalse(catalog.TryGetPoiType("poi.does_not_exist", out _));
        }

        [Test]
        public void ContainsContentId_TrueForAnyRealDomainId_FalseForUnknown()
        {
            RuntimeCatalog catalog = BuildReal();
            Assert.IsTrue(catalog.ContainsContentId("industry.logistics_cargo"));
            Assert.IsTrue(catalog.ContainsContentId("weapon.plasma"));
            Assert.IsTrue(catalog.ContainsContentId("poi.wreck"));
            Assert.IsFalse(catalog.ContainsContentId("nothing.here"));
            Assert.IsFalse(catalog.ContainsContentId(null));
        }
    }
}
