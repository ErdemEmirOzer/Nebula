using NUnit.Framework;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §1: command capacity is charged per real vehicle cost (Engineer/Escort = 2), only for LAUNCHED vehicles, via the existing ledger.</summary>
    public class Vs015CommandLedgerTests
    {
        [Test]
        public void TryReserve_Cost2_ConsumesTwoSlots()
        {
            var ledger = new HangarCommandLedger();
            ledger.SetCapacity(4);
            Assert.IsTrue(ledger.TryReserve(2));
            Assert.AreEqual(2, ledger.Used);
        }

        [Test]
        public void TryReserve_Cost2_RejectsWhenOnlyOneSlotFree()
        {
            var ledger = new HangarCommandLedger();
            ledger.SetCapacity(3);
            Assert.IsTrue(ledger.TryReserve(2));
            Assert.IsFalse(ledger.TryReserve(2)); // only 1 free, needs 2
            Assert.AreEqual(2, ledger.Used);
        }

        [Test]
        public void Release_Cost2_FreesExactlyTwoSlots()
        {
            var ledger = new HangarCommandLedger();
            ledger.SetCapacity(4);
            ledger.TryReserve(2);
            ledger.Release(2);
            Assert.AreEqual(0, ledger.Used);
        }

        [Test]
        public void DefaultCost_StaysOne_BackwardCompatible()
        {
            var ledger = new HangarCommandLedger();
            ledger.SetCapacity(2);
            Assert.IsTrue(ledger.TryReserve());
            Assert.IsTrue(ledger.TryReserve());
            Assert.IsFalse(ledger.TryReserve());
        }

        [Test]
        public void EngineerAndEscortDefs_CostExactlyTwoCommand()
        {
            Assert.AreEqual(2, VehicleCatalog.EngineerMk1.CommandCost);
            Assert.AreEqual(2, VehicleCatalog.EngineerMk2.CommandCost);
            Assert.AreEqual(2, VehicleCatalog.EscortMk1.CommandCost);
            Assert.AreEqual(2, VehicleCatalog.EscortMk2.CommandCost);
        }
    }
}
