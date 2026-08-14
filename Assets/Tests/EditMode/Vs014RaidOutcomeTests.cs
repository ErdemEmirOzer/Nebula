using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-014 §13/§18: outcome resolution priority, exact XP percentages, zero-XP guards, deterministic salvage.</summary>
    public class Vs014RaidOutcomeTests
    {
        [Test]
        public void Resolve_TargetLost_AlwaysFailure_RegardlessOfOtherSignals()
        {
            Assert.AreEqual(RaidOutcome.Failure, RaidOutcomeResolver.Resolve(targetLost: true, playerWasShutdownDuringRaid: false, wasTargetContestedDuringRaid: false, anyResourceStolen: false));
            Assert.AreEqual(RaidOutcome.Failure, RaidOutcomeResolver.Resolve(targetLost: true, playerWasShutdownDuringRaid: true, wasTargetContestedDuringRaid: true, anyResourceStolen: true));
        }

        [Test]
        public void Resolve_PlayerShutdown_Is50PercentEvenIfDefenseOtherwiseWonCleanly()
        {
            Assert.AreEqual(RaidOutcome.PlayerDefeatedDefenseWon, RaidOutcomeResolver.Resolve(false, true, false, false));
        }

        [Test]
        public void Resolve_ContestedOrStolen_IsCostlyVictory()
        {
            Assert.AreEqual(RaidOutcome.CostlyVictory, RaidOutcomeResolver.Resolve(false, false, true, false));
            Assert.AreEqual(RaidOutcome.CostlyVictory, RaidOutcomeResolver.Resolve(false, false, false, true));
        }

        [Test]
        public void Resolve_CleanWin_IsDecisiveVictory()
        {
            Assert.AreEqual(RaidOutcome.DecisiveVictory, RaidOutcomeResolver.Resolve(false, false, false, false));
        }

        [Test]
        public void OutcomeFraction_ExactPercentages()
        {
            Assert.AreEqual(1.00f, RaidOutcomeResolver.OutcomeFraction(RaidOutcome.DecisiveVictory), 0.001f);
            Assert.AreEqual(0.75f, RaidOutcomeResolver.OutcomeFraction(RaidOutcome.CostlyVictory), 0.001f);
            Assert.AreEqual(0.50f, RaidOutcomeResolver.OutcomeFraction(RaidOutcome.PlayerDefeatedDefenseWon), 0.001f);
            Assert.AreEqual(0f, RaidOutcomeResolver.OutcomeFraction(RaidOutcome.Failure), 0.001f);
        }

        [Test]
        public void BaseXp_ExactPerStage()
        {
            Assert.AreEqual(400, RaidOutcomeResolver.BaseXp(RaidStage.Pirate));
            Assert.AreEqual(900, RaidOutcomeResolver.BaseXp(RaidStage.Assault));
            Assert.AreEqual(1800, RaidOutcomeResolver.BaseXp(RaidStage.Siege));
        }

        [Test]
        public void ResolveXp_ZeroWithoutMeaningfulParticipation_EvenOnDecisiveVictory()
        {
            Assert.AreEqual(0, RaidOutcomeResolver.ResolveXp(RaidStage.Siege, RaidOutcome.DecisiveVictory, hasMeaningfulParticipation: false));
        }

        [Test]
        public void ResolveXp_ExactValue_PirateDecisive()
        {
            Assert.AreEqual(400, RaidOutcomeResolver.ResolveXp(RaidStage.Pirate, RaidOutcome.DecisiveVictory, true));
        }

        [Test]
        public void ResolveXp_ExactValue_AssaultCostly()
        {
            Assert.AreEqual(675, RaidOutcomeResolver.ResolveXp(RaidStage.Assault, RaidOutcome.CostlyVictory, true)); // 900*0.75
        }

        [Test]
        public void ResolveXp_ExactValue_SiegeShutdown()
        {
            Assert.AreEqual(900, RaidOutcomeResolver.ResolveXp(RaidStage.Siege, RaidOutcome.PlayerDefeatedDefenseWon, true)); // 1800*0.5
        }

        [Test]
        public void ResolveXp_ZeroOnFailure_RegardlessOfParticipation()
        {
            Assert.AreEqual(0, RaidOutcomeResolver.ResolveXp(RaidStage.Siege, RaidOutcome.Failure, true));
        }
    }

    public class Vs014RaidSalvageResolverTests
    {
        [Test]
        public void Resolve_Failure_YieldsNoSalvage()
        {
            var composition = new List<string> { EnemyIds.FighterMk1, EnemyIds.LooterTransportMk1 };
            CombatWreckageState wreck = RaidSalvageResolver.Resolve("wreck.test", 1, RaidOutcome.Failure, composition);
            Assert.AreEqual(0, wreck.Nodes.Count);
        }

        [Test]
        public void Resolve_OnlyPaysOutExistingBasicPartsAndOneProcessedMaterial()
        {
            var composition = new List<string> { EnemyIds.HeavyAssaultMk1, EnemyIds.FighterMk1 };
            CombatWreckageState wreck = RaidSalvageResolver.Resolve("wreck.test", 5, RaidOutcome.CostlyVictory, composition);
            Assert.Greater(wreck.Nodes.Count, 0);
            foreach (var node in wreck.Nodes)
            {
                bool isBasicPart = node.ItemId == ProcessingCatalog.BasicPartId;
                bool isMaterial = System.Array.IndexOf(ProcessingCatalog.MaterialIds, node.ItemId) >= 0;
                Assert.IsTrue(isBasicPart || isMaterial, $"unexpected salvage item id: {node.ItemId}");
            }
        }

        [Test]
        public void Resolve_DecisiveVictoryYieldsMoreThanCostlyVictory_SameComposition()
        {
            var composition = new List<string> { EnemyIds.SiegeMk1, EnemyIds.EmDisruptorMk1, EnemyIds.FighterMk1, EnemyIds.FighterMk1 };
            CombatWreckageState decisive = RaidSalvageResolver.Resolve("wreck.a", 77, RaidOutcome.DecisiveVictory, composition);
            CombatWreckageState costly = RaidSalvageResolver.Resolve("wreck.b", 77, RaidOutcome.CostlyVictory, composition);

            float decisiveTotal = 0f, costlyTotal = 0f;
            foreach (var n in decisive.Nodes) decisiveTotal += n.Total;
            foreach (var n in costly.Nodes) costlyTotal += n.Total;
            Assert.Greater(decisiveTotal, costlyTotal);
        }

        [Test]
        public void Resolve_IsDeterministic_SameSeedSameOutput()
        {
            var composition = new List<string> { EnemyIds.HeavyAssaultMk1 };
            CombatWreckageState a = RaidSalvageResolver.Resolve("wreck.a", 42, RaidOutcome.DecisiveVictory, composition);
            CombatWreckageState b = RaidSalvageResolver.Resolve("wreck.b", 42, RaidOutcome.DecisiveVictory, composition);
            Assert.AreEqual(a.Nodes.Count, b.Nodes.Count);
            for (int i = 0; i < a.Nodes.Count; i++)
            {
                Assert.AreEqual(a.Nodes[i].ItemId, b.Nodes[i].ItemId);
                Assert.AreEqual(a.Nodes[i].Total, b.Nodes[i].Total, 0.001f);
            }
        }
    }
}
