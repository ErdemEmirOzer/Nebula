using NUnit.Framework;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for command capacity accounting (VS-007 §1): only launched vehicles ever count.</summary>
    public class HangarCommandLedgerTests
    {
        [Test]
        public void HangaredVehicles_NeverConsumeCommandCapacity()
        {
            var ledger = new HangarCommandLedger();
            ledger.SetCapacity(2);
            // Simply existing (never reserved) never touches Used.
            Assert.AreEqual(0, ledger.Used);
        }

        [Test]
        public void TryReserve_FailsOnceCapacityIsFull()
        {
            var ledger = new HangarCommandLedger();
            ledger.SetCapacity(2);

            Assert.IsTrue(ledger.TryReserve());
            Assert.IsTrue(ledger.TryReserve());
            Assert.IsFalse(ledger.TryReserve());
            Assert.AreEqual(2, ledger.Used);
        }

        [Test]
        public void Release_FreesASlotForReuse()
        {
            var ledger = new HangarCommandLedger();
            ledger.SetCapacity(1);
            Assert.IsTrue(ledger.TryReserve());
            Assert.IsFalse(ledger.TryReserve());

            ledger.Release();
            Assert.AreEqual(0, ledger.Used);
            Assert.IsTrue(ledger.TryReserve());
        }

        [Test]
        public void T1ToT2_RaisesCapacityFrom2To4()
        {
            var ledger = new HangarCommandLedger();
            ledger.SetCapacity(ModuleCapacityByTier(1));
            Assert.AreEqual(2, ledger.Capacity);
            ledger.SetCapacity(ModuleCapacityByTier(2));
            Assert.AreEqual(4, ledger.Capacity);
        }

        private static int ModuleCapacityByTier(int tier) =>
            Modules.ModuleCatalog.HangarCommandCapacityByTier[tier - 1];
    }
}
