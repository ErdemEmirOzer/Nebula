using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §4: Escort target priority, chase-then-return distance rule, the three stances' retreat thresholds, and the fixed Laser's DPS/range/DamageProfile.</summary>
    public class Vs015EscortTests
    {
        [Test]
        public void ResolveTarget_PriorityOrder_AttackerBeatsProjectileBeatsRaiderBeatsFighterBeatsFocus()
        {
            Assert.AreEqual("attacker", EscortCombatResolver.ResolveTarget("attacker", "proj", "raider", "fighter", "focus"));
            Assert.AreEqual("proj", EscortCombatResolver.ResolveTarget(null, "proj", "raider", "fighter", "focus"));
            Assert.AreEqual("raider", EscortCombatResolver.ResolveTarget(null, null, "raider", "fighter", "focus"));
            Assert.AreEqual("fighter", EscortCombatResolver.ResolveTarget(null, null, null, "fighter", "focus"));
            Assert.AreEqual("focus", EscortCombatResolver.ResolveTarget(null, null, null, null, "focus"));
            Assert.IsNull(EscortCombatResolver.ResolveTarget(null, null, null, null, null));
        }

        [Test]
        public void ShouldRetreat_Safe_RetreatsUnder60Percent()
        {
            Assert.IsTrue(EscortCombatResolver.ShouldRetreat(0.59f, EscortStance.Safe, false));
            Assert.IsFalse(EscortCombatResolver.ShouldRetreat(0.60f, EscortStance.Safe, false));
        }

        [Test]
        public void ShouldRetreat_Balanced_RetreatsUnder35PercentOrInsufficientPower()
        {
            Assert.IsTrue(EscortCombatResolver.ShouldRetreat(0.34f, EscortStance.Balanced, false));
            Assert.IsFalse(EscortCombatResolver.ShouldRetreat(0.50f, EscortStance.Balanced, false));
            Assert.IsTrue(EscortCombatResolver.ShouldRetreat(0.90f, EscortStance.Balanced, knownEnemyPowerInsufficient: true));
        }

        [Test]
        public void ShouldRetreat_Persistent_OnlyForcedUnder15Percent()
        {
            Assert.IsFalse(EscortCombatResolver.ShouldRetreat(0.20f, EscortStance.Persistent, true));
            Assert.IsTrue(EscortCombatResolver.ShouldRetreat(0.14f, EscortStance.Persistent, false));
        }

        [Test]
        public void RetreatThresholdFraction_MatchesStanceConstants()
        {
            var s = new EscortState("v1", "m1", "ship", TargetCategory.Ship, true, EscortCommand.Protect, EscortStance.Safe);
            Assert.AreEqual(0.60f, s.RetreatThresholdFraction);
            s.SetStance(EscortStance.Balanced); Assert.AreEqual(0.35f, s.RetreatThresholdFraction);
            s.SetStance(EscortStance.Persistent); Assert.AreEqual(0.15f, s.RetreatThresholdFraction);
        }

        [Test]
        public void ShouldReturnToProtect_ChaseLimitIsExactly400()
        {
            Assert.IsFalse(EscortCombatResolver.ShouldReturnToProtect(399f));
            Assert.IsTrue(EscortCombatResolver.ShouldReturnToProtect(401f));
        }

        [Test]
        public void FixedLaser_Mk1_DpsAndRange()
        {
            Assert.AreEqual(60f, VehicleCatalog.EscortMk1.FixedLaserDps);
            Assert.AreEqual(800f, VehicleCatalog.EscortMk1.FixedLaserRange);
        }

        [Test]
        public void FixedLaser_Mk2_DpsAndRange()
        {
            Assert.AreEqual(100f, VehicleCatalog.EscortMk2.FixedLaserDps);
            Assert.AreEqual(1000f, VehicleCatalog.EscortMk2.FixedLaserRange);
        }

        [Test]
        public void FixedLaser_UsesTheSharedLaserDamageProfile()
        {
            // VS-015 §4: "Sabit Lazer hitscan'dır ve DamageProfile.Laser kullanır" -- never a bespoke Escort profile.
            var d = new Durability(0f, 100f, 100f, 1f);
            float overflow = DamageResolver.Apply(d, 60f, DamageProfile.Laser);
            Assert.AreEqual(0f, overflow);
            Assert.AreEqual(100f - 60f * DamageProfile.Laser.ArmorMultiplier, d.Armor, 0.01f);
        }

        [Test]
        public void EscortState_Command_ReturnToHangar_IsNotAStance()
        {
            var s = new EscortState("v1", "m1", "ship", TargetCategory.Ship, true);
            s.SetCommand(EscortCommand.Retreat);
            Assert.AreEqual(EscortSubState.Retreating, s.SubState);
            s.SetCommand(EscortCommand.Protect);
            Assert.AreEqual(EscortSubState.Following, s.SubState);
        }

        [Test]
        public void Restore_RebuildsSubStateAndEngagedThreat()
        {
            var restored = EscortState.Restore("v1", "m1", "ship", TargetCategory.Ship, true, true,
                EscortCommand.Protect, EscortStance.Persistent, EscortSubState.Engaging, "enemy-3");
            Assert.AreEqual(EscortSubState.Engaging, restored.SubState);
            Assert.AreEqual("enemy-3", restored.EngagedThreatId);
            Assert.IsTrue(restored.XpAwarded);
            Assert.AreEqual(EscortStance.Persistent, restored.Stance);
        }
    }
}
