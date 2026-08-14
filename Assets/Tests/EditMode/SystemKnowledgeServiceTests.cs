using NUnit.Framework;
using Nebula.Navigation;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for VS-006's SystemKnowledgeService (spec §9, tests 4-5).
    /// The "reveal nearest three on first Controlled" rule (with provenance)
    /// moved to <see cref="FrontierSignalService"/> as of VS-010 §3 — see
    /// FrontierSignalServiceTests.cs.
    /// </summary>
    public class SystemKnowledgeServiceTests
    {
        [Test]
        public void InitializeNewGame_OnlyStartSystemIsVisited()
        {
            var service = new SystemKnowledgeService();
            service.InitializeNewGame("sys000");

            Assert.AreEqual(SystemKnowledgeLevel.Visited, service.GetLevel("sys000"));
            Assert.AreEqual(SystemKnowledgeLevel.Unknown, service.GetLevel("sys001"));
        }

        [Test]
        public void Raise_NeverDowngrades()
        {
            var service = new SystemKnowledgeService();
            service.Raise("sys001", SystemKnowledgeLevel.Located);
            service.Raise("sys001", SystemKnowledgeLevel.Signal); // must not regress

            Assert.AreEqual(SystemKnowledgeLevel.Located, service.GetLevel("sys001"));
        }

        [Test]
        public void ForceLevel_SetsExactLevelRegardlessOfCurrent()
        {
            var service = new SystemKnowledgeService();
            service.Raise("sys001", SystemKnowledgeLevel.Visited);
            service.ForceLevel("sys001", SystemKnowledgeLevel.Signal); // restore-only path, allowed to "downgrade"

            Assert.AreEqual(SystemKnowledgeLevel.Signal, service.GetLevel("sys001"));
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            var service = new SystemKnowledgeService();
            service.Raise("sys001", SystemKnowledgeLevel.Visited);
            service.Clear();

            Assert.AreEqual(SystemKnowledgeLevel.Unknown, service.GetLevel("sys001"));
        }
    }
}
