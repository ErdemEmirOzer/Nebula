using NUnit.Framework;
using UnityEngine;
using Nebula.Navigation;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for Yön Sabitle / Heading Lock and Arrival Assist (spec §9, tests 11-12).</summary>
    public class HeadingLockAndArrivalAssistTests
    {
        [Test]
        public void Engage_HoldsCapturedForward_NeverPointsAtAnythingElse()
        {
            var state = new HeadingLockState();
            state.Engage(new Vector3(0f, 0f, 1f));

            Assert.IsTrue(state.Engaged);
            Assert.AreEqual(new Vector3(0f, 0f, 1f), state.HeldDirection);
        }

        [Test]
        public void Toggle_DisengagesOnSecondCall()
        {
            var state = new HeadingLockState();
            state.Toggle(Vector3.forward);
            Assert.IsTrue(state.Engaged);
            state.Toggle(Vector3.right); // real steering input arrived — this call represents the disengage path
            Assert.IsFalse(state.Engaged);
        }

        [Test]
        public void Restore_NormalizesAndSetsExactState()
        {
            var state = new HeadingLockState();
            state.Restore(true, new Vector3(0f, 0f, 5f));
            Assert.IsTrue(state.Engaged);
            Assert.AreEqual(1f, state.HeldDirection.magnitude, 0.0001f);
        }

        [Test]
        public void ArrivalAssist_NeverProducesEtaBelowMinimumSpeed()
        {
            Assert.AreEqual(-1f, ArrivalAssist.EtaSeconds(100000f, 0f));
            Assert.Greater(ArrivalAssist.EtaSeconds(100000f, 1000f), 0f);
        }

        [Test]
        public void ArrivalAssist_BrakeDistanceScalesWithSpeedDelta()
        {
            float d1 = ArrivalAssist.BrakeDistance(1800f, 180f, 8f);
            float d2 = ArrivalAssist.BrakeDistance(4500f, 194.4f, 8f);
            Assert.Greater(d2, d1); // higher cruise speed needs more room to shed
            Assert.Greater(d1, 0f);
        }

        [Test]
        public void ArrivalAssist_WarnsOnlyWithinBrakeDistance()
        {
            float brake = ArrivalAssist.BrakeDistance(1800f, 180f, 8f);
            Assert.IsTrue(ArrivalAssist.ShouldWarn(brake * 0.5f, 1800f, 180f, 8f));
            Assert.IsFalse(ArrivalAssist.ShouldWarn(brake * 10f, 1800f, 180f, 8f));
        }
    }
}
