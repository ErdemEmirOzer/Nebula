using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Modules;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-014 §5/§18: exact new-enemy stats.</summary>
    public class Vs014EnemyCatalogTests
    {
        [Test]
        public void LooterTransport_ExactStats()
        {
            EnemyDef d = EnemyCatalog.LooterTransportMk1;
            Assert.AreEqual(135f, d.Speed, 0.001f);
            Assert.AreEqual(250f, d.ShieldMax, 0.001f);
            Assert.AreEqual(400f, d.ArmorMax, 0.001f);
            Assert.AreEqual(650f, d.HullMax, 0.001f);
            Assert.AreEqual(30f, d.Dps, 0.001f);
            Assert.AreEqual(800f, d.AttackRange, 0.001f);
            Assert.AreEqual(4, d.ThreatBudget);
            Assert.AreEqual(120f, d.CargoCapacityVolume, 0.001f);
            Assert.AreEqual(10f, d.CargoTransferVolumePerSecond, 0.001f);
            Assert.AreEqual(EnemyRole.Looter, d.Role);
        }

        [Test]
        public void EmDisruptor_ExactStats()
        {
            EnemyDef d = EnemyCatalog.EmDisruptorMk1;
            Assert.AreEqual(160f, d.Speed, 0.001f);
            Assert.AreEqual(450f, d.ShieldMax, 0.001f);
            Assert.AreEqual(250f, d.ArmorMax, 0.001f);
            Assert.AreEqual(300f, d.HullMax, 0.001f);
            Assert.AreEqual(35f, d.Dps, 0.001f);
            Assert.AreEqual(2500f, d.AttackRange, 0.001f);
            Assert.AreEqual(5, d.ThreatBudget);
            Assert.AreEqual(1.5f, d.TelegraphSeconds, 0.001f);
            Assert.AreEqual(2f, d.DisruptSeconds, 0.001f);
            Assert.AreEqual(10f, d.DisruptImmunitySeconds, 0.001f);
        }

        [Test]
        public void HeavyAssault_ExactStats()
        {
            EnemyDef d = EnemyCatalog.HeavyAssaultMk1;
            Assert.AreEqual(120f, d.Speed, 0.001f);
            Assert.AreEqual(300f, d.ShieldMax, 0.001f);
            Assert.AreEqual(850f, d.ArmorMax, 0.001f);
            Assert.AreEqual(1100f, d.HullMax, 0.001f);
            Assert.AreEqual(130f, d.Dps, 0.001f);
            Assert.AreEqual(1800f, d.AttackRange, 0.001f);
            Assert.AreEqual(8, d.ThreatBudget);
        }

        [Test]
        public void Siege_ExactStats()
        {
            EnemyDef d = EnemyCatalog.SiegeMk1;
            Assert.AreEqual(90f, d.Speed, 0.001f);
            Assert.AreEqual(700f, d.ShieldMax, 0.001f);
            Assert.AreEqual(1200f, d.ArmorMax, 0.001f);
            Assert.AreEqual(1600f, d.HullMax, 0.001f);
            Assert.AreEqual(180f, d.Dps, 0.001f);
            Assert.AreEqual(5000f, d.AttackRange, 0.001f);
            Assert.AreEqual(13, d.ThreatBudget);
            Assert.AreEqual(3f, d.TelegraphSeconds, 0.001f);
        }

        [Test]
        public void All_ContainsExactlySixEnemies_NoCarrier()
        {
            Assert.AreEqual(6, EnemyCatalog.All.Length);
            foreach (EnemyDef d in EnemyCatalog.All) Assert.AreNotEqual("Carrier", d.DisplayName);
        }

        [Test]
        public void Get_ResolvesEveryStableId()
        {
            Assert.IsNotNull(EnemyCatalog.Get(EnemyIds.LooterTransportMk1));
            Assert.IsNotNull(EnemyCatalog.Get(EnemyIds.EmDisruptorMk1));
            Assert.IsNotNull(EnemyCatalog.Get(EnemyIds.HeavyAssaultMk1));
            Assert.IsNotNull(EnemyCatalog.Get(EnemyIds.SiegeMk1));
        }
    }

    /// <summary>VS-014 §7/§18: Looter's stealable whitelist and retreat thresholds (pure logic; drill/vehicle/logistics-center integration is covered by PlayMode tests).</summary>
    public class Vs014LooterCargoServiceTests
    {
        [Test]
        public void IsStealable_RawResourcesBasicPartsAndMaterials_AreStealable()
        {
            foreach (string id in ExtractionCatalog.ResourceIds) Assert.IsTrue(LooterCargoService.IsStealable(id), id);
            Assert.IsTrue(LooterCargoService.IsStealable(ProcessingCatalog.BasicPartId));
            foreach (string id in ProcessingCatalog.MaterialIds) Assert.IsTrue(LooterCargoService.IsStealable(id), id);
        }

        [Test]
        public void IsStealable_NuclearEnergyAndCore_AreNeverStealable()
        {
            Assert.IsFalse(LooterCargoService.IsStealable(ModuleCatalog.EnergyNuclearId));
            Assert.IsFalse(LooterCargoService.IsStealable(ModuleCatalog.CoreNuclearId));
        }

        [Test]
        public void IsStealable_UnknownOrNullItem_IsNeverStealable()
        {
            Assert.IsFalse(LooterCargoService.IsStealable(null));
            Assert.IsFalse(LooterCargoService.IsStealable("item.does_not_exist"));
        }

        [Test]
        public void ShouldOptionalRetreat_At50PercentFill()
        {
            var cargo = new LooterCargoState(100f);
            Assert.IsFalse(LooterCargoService.ShouldOptionalRetreat(cargo));
            cargo.Add(ExtractionCatalog.ResourceIds[0], CargoItemVolumes.PerUnit(ExtractionCatalog.ResourceIds[0]) > 0f
                ? 50f / CargoItemVolumes.PerUnit(ExtractionCatalog.ResourceIds[0]) : 50f);
            Assert.IsTrue(LooterCargoService.ShouldOptionalRetreat(cargo));
        }

        [Test]
        public void MustRetreat_OnlyWhenFull()
        {
            var cargo = new LooterCargoState(10f);
            Assert.IsFalse(LooterCargoService.MustRetreat(cargo));
            cargo.Add(ProcessingCatalog.BasicPartId, 999f); // way past capacity -- Add() clamps
            Assert.IsTrue(LooterCargoService.MustRetreat(cargo));
        }

        [Test]
        public void MustRetreat_NullCargo_IsTrue()
        {
            Assert.IsTrue(LooterCargoService.MustRetreat(null));
        }

        [Test]
        public void ToWreckageNodes_ConvertsEveryHeldItem_NeverAnEmptyOrNegativeOne()
        {
            var cargo = new LooterCargoState(1000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 12f);
            cargo.Add(ProcessingCatalog.MaterialIds[0], 5f);
            var nodes = LooterCargoService.ToWreckageNodes(cargo);
            Assert.AreEqual(2, nodes.Count);
            foreach (var n in nodes) Assert.Greater(n.Total, 0f);
        }

        [Test]
        public void ToWreckageNodes_NullCargo_ReturnsEmptyList()
        {
            Assert.AreEqual(0, LooterCargoService.ToWreckageNodes(null).Count);
        }
    }
}
