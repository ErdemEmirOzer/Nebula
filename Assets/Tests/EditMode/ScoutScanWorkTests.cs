using NUnit.Framework;
using Nebula.Hangar;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the Scout on-station scan timer (VS-007 §4: 1500 range / 60 active seconds).</summary>
    public class ScoutScanWorkTests
    {
        [Test]
        public void Tick_CompletesExactlyAtRequiredSeconds()
        {
            var scan = new ScoutScanWork();
            Assert.IsFalse(scan.Tick(ScoutScanWork.RequiredSeconds - 0.01f, inRange: true));
            Assert.IsTrue(scan.Tick(0.02f, inRange: true));
        }

        [Test]
        public void Tick_OutOfRange_PausesButNeverResets()
        {
            var scan = new ScoutScanWork();
            scan.Tick(30f, inRange: true);
            float elapsedAt30 = scan.Elapsed;

            Assert.IsFalse(scan.Tick(10f, inRange: false)); // out of range: no progress
            Assert.AreEqual(elapsedAt30, scan.Elapsed, "leaving range must pause, not reset, progress");

            Assert.IsFalse(scan.Tick(29.9f, inRange: true));
            Assert.IsTrue(scan.Tick(0.2f, inRange: true));
        }

        [Test]
        public void Tick_ZeroDt_NeverAdvances()
        {
            var scan = new ScoutScanWork();
            scan.Tick(0f, inRange: true);
            Assert.AreEqual(0f, scan.Elapsed);
        }

        [Test]
        public void Restore_ClampsToValidRange()
        {
            var scan = new ScoutScanWork();
            scan.Restore(-5f);
            Assert.AreEqual(0f, scan.Elapsed);
            scan.Restore(9999f);
            Assert.AreEqual(ScoutScanWork.RequiredSeconds, scan.Elapsed);
        }
    }
}
