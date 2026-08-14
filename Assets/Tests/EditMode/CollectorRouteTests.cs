using NUnit.Framework;
using Nebula.Logistics;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure CollectorRoute state machine (VS-011 §4/§10.9/§10.16).</summary>
    public class CollectorRouteTests
    {
        private static CollectorRoute Fresh() => new CollectorRoute("route-0000", "sector.0.0.0/system.000",
            "sector.0.0.0/system.000/planet.00", "vehicle-0000", RoutePriority.Normal);

        [Test]
        public void FreshRoute_StartsInToSource_WithStableIdentity()
        {
            CollectorRoute route = Fresh();
            Assert.AreEqual(RoutePhase.ToSource, route.Phase);
            Assert.AreEqual("route-0000", route.RouteId);
            Assert.AreEqual("sector.0.0.0/system.000/planet.00", route.DrillWorldId);
            Assert.AreEqual("vehicle-0000", route.VehicleId);
            Assert.IsFalse(route.Paused);
        }

        [Test]
        public void Pause_RemembersExactPhase_ResumeRestoresIt()
        {
            CollectorRoute route = Fresh();
            route.SetPhase(RoutePhase.ToCenter);

            route.Pause();
            Assert.AreEqual(RoutePhase.Paused, route.Phase);
            Assert.IsTrue(route.Paused);

            route.Resume();
            Assert.AreEqual(RoutePhase.ToCenter, route.Phase, "must resume to exactly the phase it was paused from");
        }

        [Test]
        public void Resume_NeverAutomatic_OnlyExplicitCallChangesPhase()
        {
            CollectorRoute route = Fresh();
            route.SetPhase(RoutePhase.Unloading);
            route.Pause();

            // Nothing except an explicit Resume() call may leave Paused.
            Assert.AreEqual(RoutePhase.Paused, route.Phase);
            route.SetWaitReason(RouteWaitReason.CenterDisabled); // unrelated mutation must not resume it
            Assert.AreEqual(RoutePhase.Paused, route.Phase);
        }

        [Test]
        public void Pause_Idempotent_DoesNotOverwriteResumePhaseOnSecondCall()
        {
            CollectorRoute route = Fresh();
            route.SetPhase(RoutePhase.Loading);
            route.Pause();
            route.SetPhase(RoutePhase.Paused); // no-op, still paused
            route.Pause(); // calling pause again while already paused must not corrupt ResumePhase
            route.Resume();
            Assert.AreEqual(RoutePhase.Loading, route.Phase);
        }

        [Test]
        public void SetHeld_ClearsItemIdWhenAmountIsZero()
        {
            CollectorRoute route = Fresh();
            route.SetHeld("resource.metal", 12f);
            Assert.AreEqual("resource.metal", route.HeldItemId);
            Assert.AreEqual(12f, route.HeldAmount);

            route.SetHeld(null, 0f);
            Assert.IsNull(route.HeldItemId);
            Assert.AreEqual(0f, route.HeldAmount);
        }

        [Test]
        public void EnterQueued_EnterHolding_EnterUnloading_SetPhaseAndDockWaitStart()
        {
            CollectorRoute route = Fresh();
            route.EnterQueued(12.5f);
            Assert.AreEqual(RoutePhase.Queued, route.Phase);
            Assert.AreEqual(12.5f, route.DockWaitStartedAt);

            route.EnterUnloading();
            Assert.AreEqual(RoutePhase.Unloading, route.Phase);

            route.EnterHolding(30f);
            Assert.AreEqual(RoutePhase.Holding, route.Phase);
            Assert.AreEqual(30f, route.DockWaitStartedAt);
        }

        [Test]
        public void TransferTransaction_BeginThenEnd_MovesActiveToLast_NeverAppliedTwice()
        {
            CollectorRoute route = Fresh();
            Assert.IsNull(route.ActiveTransferTransactionId);
            Assert.IsNull(route.LastTransferTransactionId);

            route.BeginTransferTransaction("tx-1");
            Assert.AreEqual("tx-1", route.ActiveTransferTransactionId);

            route.EndTransferTransaction();
            Assert.IsNull(route.ActiveTransferTransactionId);
            Assert.AreEqual("tx-1", route.LastTransferTransactionId, "completed transaction id must be remembered so a repeated apply can be detected as a duplicate");
        }

        [Test]
        public void SetPriority_ChangesPriorityOnly()
        {
            CollectorRoute route = Fresh();
            route.SetPriority(RoutePriority.High);
            Assert.AreEqual(RoutePriority.High, route.Priority);
            Assert.AreEqual(RoutePhase.ToSource, route.Phase, "priority change must not affect phase");
        }

        [Test]
        public void RestoreConstructor_RoundTripsExactState()
        {
            var route = new CollectorRoute("route-0007", "sys001", "sys001/pl02", "vehicle-0003", RoutePriority.High,
                RoutePhase.Holding, RoutePhase.ToCenter, RouteWaitReason.CenterFull, "resource.metal", 5f,
                1234f, "tx-active", "tx-last", 77f);

            Assert.AreEqual(RoutePhase.Holding, route.Phase);
            Assert.AreEqual(RoutePhase.ToCenter, route.ResumePhase);
            Assert.AreEqual(RouteWaitReason.CenterFull, route.WaitReason);
            Assert.AreEqual("resource.metal", route.HeldItemId);
            Assert.AreEqual(5f, route.HeldAmount);
            Assert.AreEqual(1234f, route.RemainingDistance);
            Assert.AreEqual("tx-active", route.ActiveTransferTransactionId);
            Assert.AreEqual("tx-last", route.LastTransferTransactionId);
            Assert.AreEqual(77f, route.DockWaitStartedAt);
        }
    }
}
