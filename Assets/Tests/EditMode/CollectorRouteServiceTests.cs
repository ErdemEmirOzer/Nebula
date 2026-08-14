using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Logistics;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for CollectorRouteService's pure create-validation and deterministic dock tie-break (VS-011 §4/§10.9/§10.10).</summary>
    public class CollectorRouteServiceTests
    {
        [Test]
        public void CheckCreate_AllValid_ReturnsNone()
        {
            RouteCreateRejectReason r = CollectorRouteService.CheckCreate(centerActive: true, currentRouteCountAtCenter: 0, maxRoutes: 2,
                vehicleIsCollector: true, vehicleEligible: true, vehicleAlreadyRouted: false, drillAlreadyRouted: false,
                drillActive: true, sameSystem: true, hasCommandCapacity: true);
            Assert.AreEqual(RouteCreateRejectReason.None, r);
        }

        [Test]
        public void CheckCreate_ChecksInPriorityOrder()
        {
            Assert.AreEqual(RouteCreateRejectReason.NotSameSystem, CollectorRouteService.CheckCreate(
                true, 0, 2, true, true, false, false, true, sameSystem: false, hasCommandCapacity: true));

            Assert.AreEqual(RouteCreateRejectReason.CenterNotActive, CollectorRouteService.CheckCreate(
                centerActive: false, currentRouteCountAtCenter: 0, maxRoutes: 2, vehicleIsCollector: true, vehicleEligible: true,
                vehicleAlreadyRouted: false, drillAlreadyRouted: false, drillActive: true, sameSystem: true, hasCommandCapacity: true));

            Assert.AreEqual(RouteCreateRejectReason.RouteCapReached, CollectorRouteService.CheckCreate(
                true, currentRouteCountAtCenter: 2, maxRoutes: 2, vehicleIsCollector: true, vehicleEligible: true,
                vehicleAlreadyRouted: false, drillAlreadyRouted: false, drillActive: true, sameSystem: true, hasCommandCapacity: true));

            Assert.AreEqual(RouteCreateRejectReason.VehicleNotCollector, CollectorRouteService.CheckCreate(
                true, 0, 2, vehicleIsCollector: false, vehicleEligible: true, vehicleAlreadyRouted: false,
                drillAlreadyRouted: false, drillActive: true, sameSystem: true, hasCommandCapacity: true));

            Assert.AreEqual(RouteCreateRejectReason.VehicleAlreadyRouted, CollectorRouteService.CheckCreate(
                true, 0, 2, true, true, vehicleAlreadyRouted: true, drillAlreadyRouted: false, drillActive: true, sameSystem: true, hasCommandCapacity: true));

            Assert.AreEqual(RouteCreateRejectReason.DrillAlreadyRouted, CollectorRouteService.CheckCreate(
                true, 0, 2, true, true, false, drillAlreadyRouted: true, drillActive: true, sameSystem: true, hasCommandCapacity: true));

            Assert.AreEqual(RouteCreateRejectReason.DrillNotActive, CollectorRouteService.CheckCreate(
                true, 0, 2, true, true, false, false, drillActive: false, sameSystem: true, hasCommandCapacity: true));

            Assert.AreEqual(RouteCreateRejectReason.NoCommandCapacity, CollectorRouteService.CheckCreate(
                true, 0, 2, true, true, false, false, true, sameSystem: true, hasCommandCapacity: false));
        }

        [Test]
        public void RouteCap_T1TwoT2Five_EnforcedViaMaxRoutesParam()
        {
            Assert.AreEqual(RouteCreateRejectReason.None, CollectorRouteService.CheckCreate(
                true, 1, LogisticsCenterCatalog.T1.MaxRoutes, true, true, false, false, true, true, true));
            Assert.AreEqual(RouteCreateRejectReason.RouteCapReached, CollectorRouteService.CheckCreate(
                true, 2, LogisticsCenterCatalog.T1.MaxRoutes, true, true, false, false, true, true, true));
            Assert.AreEqual(RouteCreateRejectReason.None, CollectorRouteService.CheckCreate(
                true, 4, LogisticsCenterCatalog.T2.MaxRoutes, true, true, false, false, true, true, true));
            Assert.AreEqual(RouteCreateRejectReason.RouteCapReached, CollectorRouteService.CheckCreate(
                true, 5, LogisticsCenterCatalog.T2.MaxRoutes, true, true, false, false, true, true, true));
        }

        [Test]
        public void DockTieBreak_HoldingAlwaysBeforeQueued()
        {
            var queued = new CollectorRouteService.DockCandidate("r1", "vehicle-9999", isHolding: false, sourceDrillFull: true, RoutePriority.High, waitStartedAt: 0f);
            var holding = new CollectorRouteService.DockCandidate("r2", "vehicle-0001", isHolding: true, sourceDrillFull: false, RoutePriority.Low, waitStartedAt: 100f);

            var ordered = CollectorRouteService.OrderForDockAssignment(new List<CollectorRouteService.DockCandidate> { queued, holding });
            Assert.AreEqual("r2", ordered[0].RouteId, "a Holding (loaded, backpressured) vehicle always unloads before a fresh Queued one");
        }

        [Test]
        public void DockTieBreak_SourceDrillFull_BeatsPriority_WithinSameHoldingGroup()
        {
            var a = new CollectorRouteService.DockCandidate("a", "vehicle-0002", isHolding: false, sourceDrillFull: true, RoutePriority.Low, waitStartedAt: 5f);
            var b = new CollectorRouteService.DockCandidate("b", "vehicle-0001", isHolding: false, sourceDrillFull: false, RoutePriority.High, waitStartedAt: 0f);

            var ordered = CollectorRouteService.OrderForDockAssignment(new List<CollectorRouteService.DockCandidate> { b, a });
            Assert.AreEqual("a", ordered[0].RouteId, "a full source drill takes priority even over a higher route priority");
        }

        [Test]
        public void DockTieBreak_PriorityBeatsWaitTime_WhenDrillFullnessTied()
        {
            var high = new CollectorRouteService.DockCandidate("high", "vehicle-0002", false, false, RoutePriority.High, waitStartedAt: 100f);
            var normal = new CollectorRouteService.DockCandidate("normal", "vehicle-0001", false, false, RoutePriority.Normal, waitStartedAt: 0f);

            var ordered = CollectorRouteService.OrderForDockAssignment(new List<CollectorRouteService.DockCandidate> { normal, high });
            Assert.AreEqual("high", ordered[0].RouteId);
        }

        [Test]
        public void DockTieBreak_WaitTimeBeatsVehicleId_WhenPriorityTied()
        {
            var later = new CollectorRouteService.DockCandidate("later", "vehicle-0001", false, false, RoutePriority.Normal, waitStartedAt: 50f);
            var earlier = new CollectorRouteService.DockCandidate("earlier", "vehicle-0099", false, false, RoutePriority.Normal, waitStartedAt: 10f);

            var ordered = CollectorRouteService.OrderForDockAssignment(new List<CollectorRouteService.DockCandidate> { later, earlier });
            Assert.AreEqual("earlier", ordered[0].RouteId, "oldest wait time wins when priority ties");
        }

        [Test]
        public void DockTieBreak_CanonicalVehicleId_IsFinalDeterministicTieBreak()
        {
            var b = new CollectorRouteService.DockCandidate("routeB", "vehicle-0002", false, false, RoutePriority.Normal, waitStartedAt: 10f);
            var a = new CollectorRouteService.DockCandidate("routeA", "vehicle-0001", false, false, RoutePriority.Normal, waitStartedAt: 10f);

            var ordered = CollectorRouteService.OrderForDockAssignment(new List<CollectorRouteService.DockCandidate> { b, a });
            Assert.AreEqual("routeA", ordered[0].RouteId, "lexicographically-lowest canonical vehicle id wins the final tie");
        }

        [Test]
        public void NextRouteId_IsStableFormat()
        {
            Assert.AreEqual("route-0000", CollectorRouteService.NextRouteId(0));
            Assert.AreEqual("route-0042", CollectorRouteService.NextRouteId(42));
        }
    }
}
