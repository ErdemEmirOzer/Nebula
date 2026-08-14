using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-014 §14/§18: deterministic position, garrison composition (Heavy gate), reward ranges, KZG.</summary>
    public class Vs014PirateBaseCatalogTests
    {
        [Test]
        public void ExactKzg()
        {
            Assert.AreEqual(500f, PirateBaseCatalog.ShieldMax, 0.001f);
            Assert.AreEqual(900f, PirateBaseCatalog.ArmorMax, 0.001f);
            Assert.AreEqual(1400f, PirateBaseCatalog.HullMax, 0.001f);
        }

        [Test]
        public void ComputePosition_IsDeterministic_SameSeedAndSystemSamePosition()
        {
            UnityEngine.Vector3 a = PirateBaseCatalog.ComputePosition(123, "system.a");
            UnityEngine.Vector3 b = PirateBaseCatalog.ComputePosition(123, "system.a");
            Assert.AreEqual(a, b);
        }

        [Test]
        public void ComputePosition_DifferentSystems_DifferentPositions()
        {
            UnityEngine.Vector3 a = PirateBaseCatalog.ComputePosition(123, "system.a");
            UnityEngine.Vector3 b = PirateBaseCatalog.ComputePosition(123, "system.b");
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void ComputePosition_AtTheSafeDistanceFromStar()
        {
            UnityEngine.Vector3 pos = PirateBaseCatalog.ComputePosition(999, "system.z");
            Assert.AreEqual(PirateBaseCatalog.SafeDistanceFromStar, pos.magnitude, 1f);
        }

        [Test]
        public void Garrison_WithoutHeavyEligible_IsExactlyTwoFightersAndOneLooter()
        {
            var garrison = PirateBaseCatalog.Garrison(heavyTaughtAndStageProgressed: false);
            Assert.AreEqual(3, garrison.Count);
            Assert.AreEqual(2, garrison.FindAll(id => id == EnemyIds.FighterMk1).Count);
            Assert.AreEqual(1, garrison.FindAll(id => id == EnemyIds.LooterTransportMk1).Count);
            Assert.IsFalse(garrison.Contains(EnemyIds.HeavyAssaultMk1));
        }

        [Test]
        public void Garrison_WithHeavyEligible_AddsExactlyOneHeavy()
        {
            var garrison = PirateBaseCatalog.Garrison(heavyTaughtAndStageProgressed: true);
            Assert.AreEqual(4, garrison.Count);
            Assert.AreEqual(1, garrison.FindAll(id => id == EnemyIds.HeavyAssaultMk1).Count);
        }

        [Test]
        public void ResolveBasicPartsReward_WithinRange()
        {
            for (ulong seed = 0; seed < 30; seed++)
            {
                int parts = PirateBaseCatalog.ResolveBasicPartsReward(seed);
                Assert.GreaterOrEqual(parts, PirateBaseCatalog.RewardBasicPartsMin);
                Assert.LessOrEqual(parts, PirateBaseCatalog.RewardBasicPartsMax);
            }
        }

        [Test]
        public void ResolveMaterialReward_WithinRange_AndRealMaterialId()
        {
            for (ulong seed = 0; seed < 30; seed++)
            {
                int amount = PirateBaseCatalog.ResolveMaterialReward(seed, out string materialId);
                Assert.GreaterOrEqual(amount, PirateBaseCatalog.RewardMaterialMin);
                Assert.LessOrEqual(amount, PirateBaseCatalog.RewardMaterialMax);
                Assert.Contains(materialId, Extraction.ProcessingCatalog.MaterialIds);
            }
        }

        [Test]
        public void RewardXp_IsExactly500()
        {
            Assert.AreEqual(500, PirateBaseCatalog.RewardXp);
        }

        [Test]
        public void RewardThreatPressureReduction_IsExactly15()
        {
            Assert.AreEqual(15f, PirateBaseCatalog.RewardThreatPressureReduction, 0.001f);
        }
    }
}
