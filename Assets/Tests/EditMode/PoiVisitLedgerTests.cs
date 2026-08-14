using NUnit.Framework;
using Nebula.Poi;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure, persisted first-visit ledger (VS-012 §2/§10.2).</summary>
    public class PoiVisitLedgerTests
    {
        [Test]
        public void RecordVisit_IsIdempotent_NeverReordersOrDuplicates()
        {
            var ledger = new PoiVisitLedger();
            Assert.IsTrue(ledger.RecordVisit("sysA"));
            Assert.IsTrue(ledger.RecordVisit("sysB"));
            Assert.IsFalse(ledger.RecordVisit("sysA"), "a second recording of the same system must be a no-op");
            Assert.AreEqual(2, ledger.Count);
            Assert.AreEqual("sysA", ledger.VisitOrder[0]);
            Assert.AreEqual("sysB", ledger.VisitOrder[1]);
        }

        [Test]
        public void OrdinalOf_ReturnsMinusOne_ForUnrecordedSystem()
        {
            var ledger = new PoiVisitLedger();
            ledger.RecordVisit("sysA");
            Assert.AreEqual(0, ledger.OrdinalOf("sysA"));
            Assert.AreEqual(-1, ledger.OrdinalOf("sysZ"));
        }

        [Test]
        public void GroupMembers_ReturnsExactlyTheThreeSharingAConsecutiveGroup()
        {
            var ledger = new PoiVisitLedger();
            for (int i = 0; i < 7; i++) ledger.RecordVisit("sys" + i);

            CollectionAssert.AreEqual(new[] { "sys0", "sys1", "sys2" }, ledger.GroupMembers(2));
            CollectionAssert.AreEqual(new[] { "sys3", "sys4", "sys5" }, ledger.GroupMembers(5));
            CollectionAssert.AreEqual(new[] { "sys6" }, ledger.GroupMembers(6), "an incomplete trailing group only returns what exists so far");
        }

        [Test]
        public void RestoreOrder_RoundTripsExactSequence()
        {
            var ledger = new PoiVisitLedger();
            ledger.RestoreOrder(new[] { "sysX", "sysY", "sysZ" });
            Assert.AreEqual(3, ledger.Count);
            Assert.AreEqual(1, ledger.OrdinalOf("sysY"));
        }

        [Test]
        public void Clear_ResetsEverything()
        {
            var ledger = new PoiVisitLedger();
            ledger.RecordVisit("sysA");
            ledger.Clear();
            Assert.AreEqual(0, ledger.Count);
            Assert.AreEqual(-1, ledger.OrdinalOf("sysA"));
            Assert.IsTrue(ledger.RecordVisit("sysA"), "after Clear, the same system can be recorded again as if fresh");
        }
    }
}
