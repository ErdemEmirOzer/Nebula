using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-014 §9/§18: deterministic stage escalation, budget/composition, anti-repeat, and difficulty modifiers.</summary>
    public class Vs014RaidComposerTests
    {
        [Test]
        public void NextEligibleStage_NotDominant_FewControlled_IsPirate()
        {
            Assert.AreEqual(RaidStage.Pirate, RaidComposer.NextEligibleStage(systemIsDominant: false, controlledPlanetsInSystem: 1, disruptorTaught: false));
        }

        [Test]
        public void NextEligibleStage_NotDominant_ThreeOrMoreControlled_IsAssault()
        {
            Assert.AreEqual(RaidStage.Assault, RaidComposer.NextEligibleStage(systemIsDominant: false, controlledPlanetsInSystem: 3, disruptorTaught: false));
        }

        [Test]
        public void NextEligibleStage_Dominant_DisruptorUntaught_TeachesViaAssaultFirst()
        {
            Assert.AreEqual(RaidStage.Assault, RaidComposer.NextEligibleStage(systemIsDominant: true, controlledPlanetsInSystem: 5, disruptorTaught: false));
        }

        [Test]
        public void NextEligibleStage_Dominant_DisruptorTaught_IsSiege()
        {
            Assert.AreEqual(RaidStage.Siege, RaidComposer.NextEligibleStage(systemIsDominant: true, controlledPlanetsInSystem: 5, disruptorTaught: true));
        }

        [Test]
        public void WhichNewClassToTeach_FirstPirate_TeachesOnlyLooter()
        {
            var campaign = new RaidCampaignState();
            Assert.AreEqual(EnemyIds.LooterTransportMk1, RaidComposer.WhichNewClassToTeach(RaidStage.Pirate, campaign));
        }

        [Test]
        public void WhichNewClassToTeach_LooterAlreadyTaught_PirateTeachesNothing()
        {
            var campaign = new RaidCampaignState();
            campaign.MarkTaught(EnemyIds.LooterTransportMk1);
            Assert.IsNull(RaidComposer.WhichNewClassToTeach(RaidStage.Pirate, campaign));
        }

        [Test]
        public void WhichNewClassToTeach_FirstAssault_TeachesOnlyHeavy_NeverDisruptorInSameEncounter()
        {
            var campaign = new RaidCampaignState();
            Assert.AreEqual(EnemyIds.HeavyAssaultMk1, RaidComposer.WhichNewClassToTeach(RaidStage.Assault, campaign));
        }

        [Test]
        public void WhichNewClassToTeach_HeavyTaught_NextAssaultTeachesDisruptor()
        {
            var campaign = new RaidCampaignState();
            campaign.MarkTaught(EnemyIds.HeavyAssaultMk1);
            Assert.AreEqual(EnemyIds.EmDisruptorMk1, RaidComposer.WhichNewClassToTeach(RaidStage.Assault, campaign));
        }

        [Test]
        public void WhichNewClassToTeach_BothTaught_AssaultTeachesNothing()
        {
            var campaign = new RaidCampaignState();
            campaign.MarkTaught(EnemyIds.HeavyAssaultMk1);
            campaign.MarkTaught(EnemyIds.EmDisruptorMk1);
            Assert.IsNull(RaidComposer.WhichNewClassToTeach(RaidStage.Assault, campaign));
        }

        [Test]
        public void ResolveBudget_StaysWithinTheStageRange_AcrossManySeeds()
        {
            (int min, int max) = RaidComposer.BudgetRange(RaidStage.Pirate);
            for (ulong seed = 0; seed < 50; seed++)
            {
                int budget = RaidComposer.ResolveBudget(RaidStage.Pirate, RaidDifficulty.Commander, seed);
                Assert.GreaterOrEqual(budget, min);
                Assert.LessOrEqual(budget, max);
            }
        }

        [Test]
        public void ResolveBudget_DifficultyMultiplierApplied()
        {
            int commander = RaidComposer.ResolveBudget(RaidStage.Assault, RaidDifficulty.Commander, 7);
            int explorer = RaidComposer.ResolveBudget(RaidStage.Assault, RaidDifficulty.Explorer, 7);
            int conqueror = RaidComposer.ResolveBudget(RaidStage.Assault, RaidDifficulty.Conqueror, 7);
            Assert.Less(explorer, commander);
            Assert.Greater(conqueror, commander);
        }

        [Test]
        public void Compose_Pirate_AlwaysIncludesAtLeastOneFighterAndOneLooter()
        {
            List<string> composition = RaidComposer.Compose(RaidStage.Pirate, 10, 123, null, new RaidCampaignState());
            Assert.Contains(EnemyIds.FighterMk1, composition);
            Assert.Contains(EnemyIds.LooterTransportMk1, composition);
        }

        [Test]
        public void Compose_Assault_AlwaysIncludesAtLeastOneHeavy()
        {
            List<string> composition = RaidComposer.Compose(RaidStage.Assault, 28, 456, null, new RaidCampaignState());
            Assert.Contains(EnemyIds.HeavyAssaultMk1, composition);
        }

        [Test]
        public void Compose_Siege_AlwaysIncludesSiegeDisruptorAndAtLeastTwoEscorts()
        {
            List<string> composition = RaidComposer.Compose(RaidStage.Siege, 65, 789, null, new RaidCampaignState());
            Assert.Contains(EnemyIds.SiegeMk1, composition);
            Assert.Contains(EnemyIds.EmDisruptorMk1, composition);
            int escortCount = composition.FindAll(id => id == EnemyIds.FighterMk1 || id == EnemyIds.HeavyAssaultMk1).Count;
            Assert.GreaterOrEqual(escortCount, 2);
        }

        [Test]
        public void Compose_NeverExceedsBudget()
        {
            for (ulong seed = 0; seed < 20; seed++)
            {
                int budget = RaidComposer.ResolveBudget(RaidStage.Assault, RaidDifficulty.Commander, seed);
                List<string> composition = RaidComposer.Compose(RaidStage.Assault, budget, seed, null, new RaidCampaignState());
                int totalSpent = 0;
                foreach (string id in composition) totalSpent += EnemyCatalog.Get(id).ThreatBudget;
                Assert.LessOrEqual(totalSpent, budget);
            }
        }

        [Test]
        public void Compose_NeverExceedsMaxConcurrentEnemies()
        {
            List<string> composition = RaidComposer.Compose(RaidStage.Siege, 65, 999, null, new RaidCampaignState());
            Assert.LessOrEqual(composition.Count, RaidComposer.MaxConcurrentEnemies);
        }

        [Test]
        public void Compose_IsDeterministic_SameInputsSameOutput()
        {
            List<string> a = RaidComposer.Compose(RaidStage.Pirate, 10, 42, null, new RaidCampaignState());
            List<string> b = RaidComposer.Compose(RaidStage.Pirate, 10, 42, null, new RaidCampaignState());
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void Compose_AvoidsExactRepeatOfMostRecentHistoryComposition()
        {
            var campaign = new RaidCampaignState();
            List<string> first = RaidComposer.Compose(RaidStage.Pirate, 10, 55, null, campaign);
            campaign.RecordHistory(RaidStage.Pirate, RaidDoctrine.Loot, EnemyIds.LooterTransportMk1, "planet.test", first);

            List<string> second = RaidComposer.Compose(RaidStage.Pirate, 10, 55, null, campaign);
            var sortedFirst = new List<string>(first); sortedFirst.Sort();
            var sortedSecond = new List<string>(second); sortedSecond.Sort();
            CollectionAssert.AreNotEqual(sortedFirst, sortedSecond);
        }

        [Test]
        public void DoctrineFor_MapsStagesCorrectly()
        {
            Assert.AreEqual(RaidDoctrine.Loot, RaidComposer.DoctrineFor(RaidStage.Pirate));
            Assert.AreEqual(RaidDoctrine.Suppression, RaidComposer.DoctrineFor(RaidStage.Assault));
            Assert.AreEqual(RaidDoctrine.Siege, RaidComposer.DoctrineFor(RaidStage.Siege));
        }

        [Test]
        public void SelectTargetPlanet_PicksHighestEconomicValue()
        {
            var candidates = new List<(string, float)> { ("planet.a", 1.0f), ("planet.b", 1.8f), ("planet.c", 1.2f) };
            Assert.AreEqual("planet.b", RaidComposer.SelectTargetPlanet(candidates, null));
        }

        [Test]
        public void SelectTargetPlanet_TwiceConsecutiveSameTarget_PicksAlternativeWhenAvailable()
        {
            var campaign = new RaidCampaignState();
            campaign.RecordHistory(RaidStage.Pirate, RaidDoctrine.Loot, null, "planet.b", new List<string>());
            campaign.RecordHistory(RaidStage.Pirate, RaidDoctrine.Loot, null, "planet.b", new List<string>());

            var candidates = new List<(string, float)> { ("planet.a", 1.0f), ("planet.b", 1.8f) };
            Assert.AreEqual("planet.a", RaidComposer.SelectTargetPlanet(candidates, campaign));
        }

        [Test]
        public void DifficultyModifiers_ExactValues()
        {
            Assert.AreEqual(0.75f, RaidComposer.DifficultyBudgetMultiplier(RaidDifficulty.Explorer), 0.001f);
            Assert.AreEqual(1f, RaidComposer.DifficultyBudgetMultiplier(RaidDifficulty.Commander), 0.001f);
            Assert.AreEqual(1.25f, RaidComposer.DifficultyBudgetMultiplier(RaidDifficulty.Conqueror), 0.001f);

            Assert.AreEqual(0.8f, RaidComposer.DifficultyPressureMultiplier(RaidDifficulty.Explorer), 0.001f);
            Assert.AreEqual(1.15f, RaidComposer.DifficultyPressureMultiplier(RaidDifficulty.Conqueror), 0.001f);

            Assert.AreEqual(30f, RaidComposer.DifficultyWarningOffsetSeconds(RaidDifficulty.Explorer), 0.001f);
            Assert.AreEqual(-15f, RaidComposer.DifficultyWarningOffsetSeconds(RaidDifficulty.Conqueror), 0.001f);
        }
    }
}
