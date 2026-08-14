using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for SystemStatusQuery's VS-011 §8 wiring
    /// (HasActiveLogisticsCenter/HasWorkingCollectorRoute) on the delta
    /// (non-Full) path — StarSystemRegistry.Streaming stays null after
    /// Initialize(), so every query here reads a persisted SystemDelta only,
    /// with no live GameObject/materialization required at all.
    /// </summary>
    public class SystemStatusQueryLogisticsTests
    {
        private const string SystemId = "sector.0.0.0/system.000";

        [TearDown]
        public void Cleanup() => StarSystemRegistry.Clear();

        private static void SeedRegistry()
        {
            StarSystemRegistry.Initialize(123UL);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, SystemId);
        }

        [Test]
        public void HasActiveLogisticsCenter_FalseWithNoDelta()
        {
            SeedRegistry();
            Assert.IsFalse(SystemStatusQuery.HasActiveLogisticsCenter(SystemId));
        }

        [Test]
        public void HasActiveLogisticsCenter_TrueOnlyWhenDeltaCenterStateIsActive()
        {
            SeedRegistry();
            var delta = new SystemDelta { systemId = SystemId };
            delta.logisticsCenters.Add(new LogisticsCenterDto { systemId = SystemId, tier = 1, state = (int)LogisticsCenterState.Constructing });
            StarSystemRegistry.Deltas.Set(delta);

            Assert.IsFalse(SystemStatusQuery.HasActiveLogisticsCenter(SystemId), "still Constructing -- not Active yet");

            delta.logisticsCenters[0].state = (int)LogisticsCenterState.Active;
            Assert.IsTrue(SystemStatusQuery.HasActiveLogisticsCenter(SystemId));

            delta.logisticsCenters[0].state = (int)LogisticsCenterState.Disabled;
            Assert.IsFalse(SystemStatusQuery.HasActiveLogisticsCenter(SystemId), "Disabled must not read as active");
        }

        [Test]
        public void HasWorkingCollectorRoute_TrueOnlyWhenAtLeastOneNonPausedRouteExists()
        {
            SeedRegistry();
            var delta = new SystemDelta { systemId = SystemId };
            delta.collectorRoutes.Add(new CollectorRouteDto { systemId = SystemId, routeId = "route-0000", vehicleId = "vehicle-0000", phase = (int)RoutePhase.Paused });
            StarSystemRegistry.Deltas.Set(delta);

            Assert.IsFalse(SystemStatusQuery.HasWorkingCollectorRoute(SystemId), "a Paused route is not 'working'");

            delta.collectorRoutes[0].phase = (int)RoutePhase.ToCenter;
            Assert.IsTrue(SystemStatusQuery.HasWorkingCollectorRoute(SystemId));
        }

        [Test]
        public void Ownership_ReachesEstablishedBase_WhenCenterRouteAndDefenseAllReal()
        {
            SeedRegistry();
            var delta = new SystemDelta { systemId = SystemId };
            delta.dominance.Add(new DominanceDto { worldId = SystemId + "/planet.00", state = (int)DominanceState.Controlled });
            delta.logisticsCenters.Add(new LogisticsCenterDto { systemId = SystemId, tier = 1, state = (int)LogisticsCenterState.Active });
            delta.collectorRoutes.Add(new CollectorRouteDto { systemId = SystemId, routeId = "route-0000", vehicleId = "vehicle-0000", phase = (int)RoutePhase.Loading });
            delta.structures.Add(new StructureDto { worldId = SystemId + "/planet.00", type = (int)SurfaceStructureType.Laser, state = (int)DrillLifecycle.Active });
            StarSystemRegistry.Deltas.Set(delta);

            bool hasCenter = SystemStatusQuery.HasActiveLogisticsCenter(SystemId);
            bool hasRoute = SystemStatusQuery.HasWorkingCollectorRoute(SystemId);
            bool hasDefense = SystemStatusQuery.HasActiveDefenseAnywhere(SystemId);

            Assert.IsTrue(hasCenter); Assert.IsTrue(hasRoute); Assert.IsTrue(hasDefense);

            SystemOwnershipTag tag = SystemOwnershipResolver.Resolve(visited: true, totalRealPlanets: 5, controlledPlanets: 1,
                hasActiveLogisticsCenter: hasCenter, hasWorkingCollectorRoute: hasRoute, hasActiveDefense: hasDefense);
            Assert.AreEqual(SystemOwnershipTag.EstablishedBase, tag);
        }
    }
}
