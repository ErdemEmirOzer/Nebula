using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Navigation;
using Nebula.Poi;
using Nebula.Progression;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for pure Anomaly completion logic (VS-012 §5/§10.5).</summary>
    public class AnomalyServiceTests
    {
        [SetUp]
        public void ResetProgression() => ProgressionService.Reset();

        [Test]
        public void RequiredSeconds_MatchesShipAndScoutSafeUnstableSpec()
        {
            Assert.AreEqual(40f, AnomalyService.RequiredSeconds(byScout: false, PoiRisk.Safe));
            Assert.AreEqual(60f, AnomalyService.RequiredSeconds(byScout: false, PoiRisk.Unstable));
            Assert.AreEqual(60f, AnomalyService.RequiredSeconds(byScout: true, PoiRisk.Safe));
            Assert.AreEqual(90f, AnomalyService.RequiredSeconds(byScout: true, PoiRisk.Unstable));
        }

        private static List<(string systemId, float distance)> TenCandidates()
        {
            var list = new List<(string, float)>();
            for (int i = 0; i < 10; i++) list.Add(($"sys{i}", (i + 1) * 1000f));
            return list;
        }

        [Test]
        public void TryComplete_AwardsXpOnce_AndFailsIfNotActive()
        {
            var state = new PoiState("sys/poi.00");
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();

            Assert.IsFalse(AnomalyService.TryComplete(state, "sys/poi.00", TenCandidates(), knowledge, frontier), "not Active yet -- must fail");

            state.TickLocate(12f, true, 12f); state.TickIdentify(15f, true); state.StartOperation(null);
            Assert.IsTrue(AnomalyService.TryComplete(state, "sys/poi.00", TenCandidates(), knowledge, frontier));
            Assert.AreEqual(PoiKnowledgeLevel.Resolved, state.Level);
            Assert.IsTrue(state.AnomalyXpAwarded);
        }

        [Test]
        public void TryComplete_OpensExactlyOneSignal_FromThe4thTo8thNearestOnly()
        {
            var state = new PoiState("sys/poi.00");
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            state.TickLocate(12f, true, 12f); state.TickIdentify(15f, true); state.StartOperation(null);

            AnomalyService.TryComplete(state, "sys/poi.00", TenCandidates(), knowledge, frontier);

            int signalCount = 0;
            for (int i = 0; i < 10; i++)
            {
                SystemKnowledgeLevel level = knowledge.GetLevel("sys" + i);
                if (level == SystemKnowledgeLevel.Signal)
                {
                    signalCount++;
                    Assert.GreaterOrEqual(i, 3, "index " + i + " is outside the 4th-8th nearest window (0-based 3-7)");
                    Assert.LessOrEqual(i, 7, "index " + i + " is outside the 4th-8th nearest window (0-based 3-7)");
                }
                else
                {
                    Assert.AreEqual(SystemKnowledgeLevel.Unknown, level, "index " + i + " must stay Unknown");
                }
            }
            Assert.AreEqual(1, signalCount, "exactly one signal, matching PoiCatalog.AnomalySignalCount");
        }

        [Test]
        public void TryComplete_NeverOpensSignal_WhenFewerThanFourCandidatesExist_ButStillAwardsXp()
        {
            var state = new PoiState("sys/poi.00");
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            state.TickLocate(12f, true, 12f); state.TickIdentify(15f, true); state.StartOperation(null);

            var fewCandidates = new List<(string systemId, float distance)> { ("sysA", 100f), ("sysB", 200f) };
            bool completed = AnomalyService.TryComplete(state, "sys/poi.00", fewCandidates, knowledge, frontier);

            Assert.IsTrue(completed);
            Assert.IsTrue(state.AnomalyXpAwarded, "XP is awarded regardless of signal availability");
            Assert.AreEqual(SystemKnowledgeLevel.Unknown, knowledge.GetLevel("sysA"));
            Assert.AreEqual(SystemKnowledgeLevel.Unknown, knowledge.GetLevel("sysB"));
        }

        [Test]
        public void SimultaneousCompletion_ShipAndScout_LedgerAppliesOnlyOnce()
        {
            var state = new PoiState("sys/poi.00");
            var knowledge = new SystemKnowledgeService();
            var frontier = new FrontierSignalService();
            state.TickLocate(12f, true, 12f); state.TickIdentify(15f, true); state.StartOperation(null);

            bool first = AnomalyService.TryComplete(state, "sys/poi.00", TenCandidates(), knowledge, frontier);
            bool second = AnomalyService.TryComplete(state, "sys/poi.00", TenCandidates(), knowledge, frontier); // already Resolved now

            Assert.IsTrue(first);
            Assert.IsFalse(second, "a second attempt after Resolved must be a no-op, never a second reward");
        }
    }
}
