using NUnit.Framework;
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the single-owner modal gate — the reason M, U and Y can
    /// never be open at the same time. Pure static class, no scene needed. Resets
    /// the gate before/after so this never leaks into (or is polluted by) any
    /// other test.
    /// <para>
    /// The gate used to be shared between NavigationScreen, ProgressionScreen and
    /// HangarScreen, each acquiring it separately. After UI-09 there is one owner:
    /// NebulaUiShell holds it for the whole screen stack, which is what lets a
    /// confirmation sit over a page without either fighting for it. The tests here
    /// never referenced those types — only this comment did — so the contract they
    /// cover is unchanged.
    /// </para>
    /// </summary>
    public class ExclusiveModalTests
    {
        [Test]
        public void TryAcquire_SecondDifferentOwner_IsRejectedWhileFirstHolds()
        {
            var m = new object();
            var u = new object();

            Assert.IsTrue(ExclusiveModal.TryAcquire(m));
            Assert.IsFalse(ExclusiveModal.TryAcquire(u), "a second screen must not be able to open while the first holds the gate");
            Assert.IsTrue(ExclusiveModal.IsHeldByOther(u));

            ExclusiveModal.Release(m);
        }

        [Test]
        public void Release_ByNonOwner_DoesNothing()
        {
            var m = new object();
            var stranger = new object();
            Assert.IsTrue(ExclusiveModal.TryAcquire(m));

            ExclusiveModal.Release(stranger);
            Assert.IsTrue(ExclusiveModal.IsHeldByOther(stranger), "release from a non-owner must not free the gate");

            ExclusiveModal.Release(m);
        }

        [Test]
        public void AfterRelease_AnotherScreenCanAcquire()
        {
            var m = new object();
            var y = new object();
            Assert.IsTrue(ExclusiveModal.TryAcquire(m));
            ExclusiveModal.Release(m);

            Assert.IsFalse(ExclusiveModal.IsHeldByOther(y));
            Assert.IsTrue(ExclusiveModal.TryAcquire(y));

            ExclusiveModal.Release(y);
        }

        [Test]
        public void SameOwner_CanReacquireItsOwnLock()
        {
            var m = new object();
            Assert.IsTrue(ExclusiveModal.TryAcquire(m));
            Assert.IsTrue(ExclusiveModal.TryAcquire(m), "re-entering with the same owner must not be rejected");
            ExclusiveModal.Release(m);
        }
    }
}
