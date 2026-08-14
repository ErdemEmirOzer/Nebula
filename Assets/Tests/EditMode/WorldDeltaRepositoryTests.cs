using NUnit.Framework;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for mutable-only persistence (VS-008 §5/§10): an empty system never counts as persisted.</summary>
    public class WorldDeltaRepositoryTests
    {
        [Test]
        public void GetOrCreate_EmptyDelta_IsNotCountedAsPersisted()
        {
            var repo = new WorldDeltaRepository();
            SystemDelta d = repo.GetOrCreate("sector.0.0.0/system.000");
            Assert.IsTrue(d.IsEmpty);
            Assert.AreEqual(0, repo.PersistedCount);
        }

        [Test]
        public void NonEmptyDelta_IsCountedAsPersisted_AndAppearsInAllNonEmpty()
        {
            var repo = new WorldDeltaRepository();
            SystemDelta d = repo.GetOrCreate("sector.0.0.0/system.001");
            d.scanned.Add("sector.0.0.0/system.001/planet.00");
            repo.Set(d);

            Assert.AreEqual(1, repo.PersistedCount);
            int count = 0;
            foreach (SystemDelta nonEmpty in repo.AllNonEmpty) count++;
            Assert.AreEqual(1, count);
        }

        [Test]
        public void PruneIfEmpty_RemovesADeltaThatHasNothingLeft()
        {
            var repo = new WorldDeltaRepository();
            SystemDelta d = repo.GetOrCreate("sector.0.0.0/system.002");
            d.scanned.Add("x");
            repo.Set(d);
            Assert.AreEqual(1, repo.PersistedCount);

            d.scanned.Clear();
            repo.PruneIfEmpty("sector.0.0.0/system.002");
            Assert.IsNull(repo.TryGet("sector.0.0.0/system.002"));
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            var repo = new WorldDeltaRepository();
            SystemDelta d = repo.GetOrCreate("s");
            d.scanned.Add("x");
            repo.Set(d);
            repo.Clear();
            Assert.AreEqual(0, repo.PersistedCount);
        }
    }
}
