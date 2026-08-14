using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Navigation;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for origin-&gt;target frontier-signal provenance, dedupe and backfill (VS-010 §3/§10.9).</summary>
    public class FrontierSignalServiceTests
    {
        [Test]
        public void OpenOrigin_RevealsClosestThree_SortedByDistanceThenId()
        {
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            var candidates = new List<(string systemId, float distance)>
            {
                ("sys005", 500000f), ("sys002", 100000f), ("sys004", 100000f), ("sys003", 900000f), ("sys006", 300000f)
            };

            bool first = frontier.OpenOrigin("sys000/pl00", candidates, knowledge);

            Assert.IsTrue(first);
            Assert.AreEqual(SystemKnowledgeLevel.Signal, knowledge.GetLevel("sys002"));
            Assert.AreEqual(SystemKnowledgeLevel.Signal, knowledge.GetLevel("sys004"));
            Assert.AreEqual(SystemKnowledgeLevel.Signal, knowledge.GetLevel("sys006"));
            Assert.AreEqual(SystemKnowledgeLevel.Unknown, knowledge.GetLevel("sys005"));
            Assert.AreEqual(SystemKnowledgeLevel.Unknown, knowledge.GetLevel("sys003"));

            CollectionAssert.AreEquivalent(new[] { "sys002", "sys004", "sys006" }, frontier.TargetsFor("sys000/pl00"));
        }

        [Test]
        public void OpenOrigin_IsIdempotent_RepeatedCallsAndReclaimNeverDuplicate()
        {
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            var candidates = new List<(string systemId, float distance)> { ("sys001", 100000f) };

            bool first = frontier.OpenOrigin("sys000/pl00", candidates, knowledge);
            bool second = frontier.OpenOrigin("sys000/pl00", candidates, knowledge); // same planet ticked again
            bool third = frontier.OpenOrigin("sys000/pl00", candidates, knowledge);  // simulates Lost->Reclaim->Controlled again

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.IsFalse(third);
            Assert.AreEqual(SystemKnowledgeLevel.Signal, knowledge.GetLevel("sys001"));
            Assert.AreEqual(1, frontier.TargetsFor("sys000/pl00").Count);
        }

        [Test]
        public void TwoOrigins_ClaimingTheSameTarget_UseOneSignalMarker_ButKeepBothProvenance()
        {
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            var candidatesA = new List<(string systemId, float distance)> { ("sysShared", 100000f) };
            var candidatesB = new List<(string systemId, float distance)> { ("sysShared", 200000f) };

            frontier.OpenOrigin("origin.A", candidatesA, knowledge);
            frontier.OpenOrigin("origin.B", candidatesB, knowledge);

            Assert.AreEqual(SystemKnowledgeLevel.Signal, knowledge.GetLevel("sysShared"), "still exactly one Signal marker, not two");
            var origins = new List<string>(frontier.OriginsFor("sysShared"));
            CollectionAssert.AreEquivalent(new[] { "origin.A", "origin.B" }, origins, "both provenance relationships must be kept");
        }

        [Test]
        public void EveryOrigin_TriesToHoldThreeUniqueUnknownTargets()
        {
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            var candidates = new List<(string systemId, float distance)>
            {
                ("sysA", 100000f), ("sysB", 200000f), ("sysC", 300000f), ("sysD", 400000f)
            };

            frontier.OpenOrigin("origin.1", candidates, knowledge);
            Assert.AreEqual(3, frontier.TargetsFor("origin.1").Count);
        }

        [Test]
        public void Backfill_OnTargetLocated_ClaimsNextNearestUnknown_Deterministically()
        {
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            var candidates = new List<(string systemId, float distance)>
            {
                ("sysA", 100000f), ("sysB", 200000f), ("sysC", 300000f), ("sysD", 400000f)
            };
            frontier.OpenOrigin("origin.1", candidates, knowledge);
            CollectionAssert.AreEquivalent(new[] { "sysA", "sysB", "sysC" }, frontier.TargetsFor("origin.1"));

            // sysA graduates to Located -> origin.1 should backfill with sysD (next nearest still-Unknown).
            knowledge.Raise("sysA", SystemKnowledgeLevel.Located);
            frontier.OnSystemLevelRaised("sysA", SystemKnowledgeLevel.Located, _ => candidates, knowledge);

            CollectionAssert.AreEquivalent(new[] { "sysA", "sysB", "sysC", "sysD" }, frontier.TargetsFor("origin.1"));
            Assert.AreEqual(SystemKnowledgeLevel.Signal, knowledge.GetLevel("sysD"));
            Assert.AreEqual(SystemKnowledgeLevel.Located, knowledge.GetLevel("sysA"), "backfill must never touch the level that triggered it");
        }

        [Test]
        public void Backfill_NeverDowngradesOrChangesAnExistingLocatedOrVisitedLevel()
        {
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            var candidates = new List<(string systemId, float distance)> { ("sysA", 100000f) };
            frontier.OpenOrigin("origin.1", candidates, knowledge);

            knowledge.Raise("sysA", SystemKnowledgeLevel.Visited);
            frontier.OnSystemLevelRaised("sysA", SystemKnowledgeLevel.Visited, _ => candidates, knowledge);

            Assert.AreEqual(SystemKnowledgeLevel.Visited, knowledge.GetLevel("sysA"));
        }

        [Test]
        public void NewOrigin_NeverClaimsATargetThatHasAlreadyProgressedPastSignal()
        {
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            frontier.OpenOrigin("origin.A", new List<(string, float)> { ("sysProgressed", 50000f) }, knowledge);
            knowledge.Raise("sysProgressed", SystemKnowledgeLevel.Visited); // e.g. the player has since physically visited it

            // origin.B's own candidate list also includes sysProgressed as its nearest — but it's
            // already past Signal, so it is NOT a valid fresh target for a new origin (only still-Signal-or-Unknown targets are shareable).
            frontier.OpenOrigin("origin.B", new List<(string, float)> { ("sysProgressed", 60000f), ("sysOnlyForB", 70000f) }, knowledge);

            CollectionAssert.DoesNotContain(frontier.TargetsFor("origin.B"), "sysProgressed");
            CollectionAssert.Contains(frontier.TargetsFor("origin.B"), "sysOnlyForB");
        }

        [Test]
        public void RestoreProvenance_SeedsExactPairs_WithoutRaisingOrReRevealing()
        {
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            frontier.RestoreProvenance(new[] { ("origin.1", "sysA"), ("origin.1", "sysB") });

            Assert.IsTrue(frontier.HasOpened("origin.1"));
            CollectionAssert.AreEquivalent(new[] { "sysA", "sysB" }, frontier.TargetsFor("origin.1"));
            Assert.AreEqual(SystemKnowledgeLevel.Unknown, knowledge.GetLevel("sysA"), "restore must not itself raise any level");
        }

        [Test]
        public void SnapshotPairs_RoundTripsThroughRestoreProvenance()
        {
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            frontier.OpenOrigin("origin.1", new List<(string, float)> { ("sysA", 1f), ("sysB", 2f) }, knowledge);

            var pairs = new List<(string origin, string target)>(frontier.SnapshotPairs());
            var restored = new FrontierSignalService();
            restored.RestoreProvenance(pairs);

            CollectionAssert.AreEquivalent(frontier.TargetsFor("origin.1"), restored.TargetsFor("origin.1"));
        }

        [Test]
        public void RebuildProvenanceFromControlledPlanets_NeverChangesExistingLevels_OnlyBackfillsBookkeeping()
        {
            var knowledge = new SystemKnowledgeService();
            knowledge.ForceLevel("sysA", SystemKnowledgeLevel.Visited); // e.g. restored from a v7 save
            var frontier = new FrontierSignalService();
            var candidates = new List<(string systemId, float distance)> { ("sysA", 1f), ("sysB", 2f) };

            frontier.RebuildProvenanceFromControlledPlanets(new[] { "origin.1" }, _ => candidates, knowledge);

            Assert.AreEqual(SystemKnowledgeLevel.Visited, knowledge.GetLevel("sysA"), "existing level must be untouched");
            Assert.AreEqual(SystemKnowledgeLevel.Signal, knowledge.GetLevel("sysB"), "the still-Unknown candidate gets opened");
            Assert.IsTrue(frontier.HasOpened("origin.1"));
        }

        [Test]
        public void OpenOrigin_NeverAwardsXp()
        {
            Progression.ProgressionService.Reset();
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            frontier.OpenOrigin("origin.1", new List<(string, float)> { ("sysA", 1f) }, knowledge);

            Assert.AreEqual(0L, Progression.ProgressionService.State.TotalXp);
        }
    }
}
