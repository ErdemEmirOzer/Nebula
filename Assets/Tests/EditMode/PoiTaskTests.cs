using NUnit.Framework;
using Nebula.Poi;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure single-assignment POI vehicle task (VS-012 §6/§8/§10.8).</summary>
    public class PoiTaskTests
    {
        [Test]
        public void FreshTask_StartsAtToTarget_NoRecallRequested()
        {
            var task = new PoiTask("sys/poi.00", "vehicle-0000", "sys");
            Assert.AreEqual(PoiTaskPhase.ToTarget, task.Phase);
            Assert.IsFalse(task.RecallRequested);
            Assert.AreEqual("sys/poi.00", task.PoiId);
            Assert.AreEqual("vehicle-0000", task.VehicleId);
            Assert.AreEqual("sys", task.SystemId);
        }

        [Test]
        public void ArriveAtTarget_OnlyAdvancesFromToTarget()
        {
            var task = new PoiTask("sys/poi.00", "vehicle-0000", "sys");
            task.ArriveAtTarget();
            Assert.AreEqual(PoiTaskPhase.Working, task.Phase);

            task.BeginReturn();
            Assert.AreEqual(PoiTaskPhase.Returning, task.Phase);
            task.ArriveAtTarget(); // must be a no-op once past ToTarget
            Assert.AreEqual(PoiTaskPhase.Returning, task.Phase);
        }

        [Test]
        public void BeginReturn_IsIdempotent_FromAnyPhase()
        {
            var task = new PoiTask("sys/poi.00", "vehicle-0000", "sys");
            task.BeginReturn(); // straight from ToTarget -- e.g. an immediate cancel
            Assert.AreEqual(PoiTaskPhase.Returning, task.Phase);
            task.BeginReturn();
            Assert.AreEqual(PoiTaskPhase.Returning, task.Phase);
        }

        [Test]
        public void RequestRecall_SetsFlagAndBeginsReturn_FromWorking()
        {
            var task = new PoiTask("sys/poi.00", "vehicle-0000", "sys");
            task.ArriveAtTarget();
            task.RequestRecall();

            Assert.IsTrue(task.RecallRequested);
            Assert.AreEqual(PoiTaskPhase.Returning, task.Phase);
        }

        [Test]
        public void RestoreConstructor_RoundTripsExactPhaseAndRecallFlag()
        {
            var task = new PoiTask("sys/poi.01", "vehicle-0001", "sys", PoiTaskPhase.Returning, recallRequested: true);
            Assert.AreEqual(PoiTaskPhase.Returning, task.Phase);
            Assert.IsTrue(task.RecallRequested);
        }
    }
}
