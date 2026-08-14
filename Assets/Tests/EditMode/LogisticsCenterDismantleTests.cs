using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for dismantle rejection while the center is loaded, has an active unload, or a loaded route waiting (VS-011 §6/§10.13).</summary>
    public class LogisticsCenterDismantleTests
    {
        private static SystemManifest Manifest(string systemId)
        {
            var planets = new List<PlanetManifest>
            {
                new PlanetManifest(0, WorldIdCodec.PlanetId(systemId, 0), ResourceFamily.Metal, Rarity.Common,
                    30000f, 0f, 0f, 0f, 1000f, false, EnvironmentClass.Calm, 1UL)
            };
            return new SystemManifest(systemId, new SectorCoordinate(0, 0, 0), 0, systemId, StarClass.Yellow,
                Vector3.zero, 1_000_000f, 1000f, false, SystemProfileKind.Balanced, SectorManifestGenerator.GenerationVersion, planets);
        }

        private static (LogisticsCenter center, GameObject starGO) BuildCenter(string systemId)
        {
            SystemManifest manifest = Manifest(systemId);
            var starGO = new GameObject("Star");
            LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out _);
            LogisticsCenter center = LogisticsCenter.Create(manifest, starGO.transform, offset, 0, 0, 0);
            return (center, starGO);
        }

        [Test]
        public void CanDismantle_TrueWhenEmptyNoDocksNoLoadedRoutes()
        {
            var (center, starGO) = BuildCenter("sector.0.0.0/system.901");
            try { Assert.IsTrue(center.CanDismantle()); }
            finally { Object.DestroyImmediate(center.gameObject); Object.DestroyImmediate(starGO); LogisticsState.Reset(); }
        }

        [Test]
        public void CanDismantle_FalseWhileInventoryNotEmpty()
        {
            var (center, starGO) = BuildCenter("sector.0.0.0/system.902");
            try
            {
                center.Inventory.Add("resource.metal", 1f);
                Assert.IsFalse(center.CanDismantle());
            }
            finally { Object.DestroyImmediate(center.gameObject); Object.DestroyImmediate(starGO); LogisticsState.Reset(); }
        }

        [Test]
        public void CanDismantle_FalseWhileARouteIsActivelyUnloading_DockOccupied()
        {
            var (center, starGO) = BuildCenter("sector.0.0.0/system.903");
            try
            {
                var route = new CollectorRoute("route-0000", center.SystemId, center.SystemId + "/planet.00", "vehicle-0000", RoutePriority.Normal);
                route.EnterUnloading();
                LogisticsState.AddRoute(route);

                Assert.AreEqual(1, center.DocksInUse, "DocksInUse is derived from Unloading-phase routes at this center");
                Assert.IsFalse(center.CanDismantle(), "an active unload transaction blocks dismantle even with an empty inventory");
            }
            finally { Object.DestroyImmediate(center.gameObject); Object.DestroyImmediate(starGO); LogisticsState.Reset(); }
        }

        [Test]
        public void CanDismantle_FalseWhileALoadedCollectorIsWaitingHolding()
        {
            var (center, starGO) = BuildCenter("sector.0.0.0/system.904");
            try
            {
                var route = new CollectorRoute("route-0001", center.SystemId, center.SystemId + "/planet.00", "vehicle-0001", RoutePriority.Normal);
                route.SetHeld("resource.metal", 40f);
                route.EnterHolding(0f);
                LogisticsState.AddRoute(route);

                Assert.AreEqual(0, center.DocksInUse, "Holding does not occupy a dock");
                Assert.IsFalse(center.CanDismantle(), "a loaded Holding Collector must still block dismantle");
            }
            finally { Object.DestroyImmediate(center.gameObject); Object.DestroyImmediate(starGO); LogisticsState.Reset(); }
        }

        [Test]
        public void CanDismantle_TrueAgain_OnceRouteIsEmptyAndUndocked()
        {
            var (center, starGO) = BuildCenter("sector.0.0.0/system.905");
            try
            {
                var route = new CollectorRoute("route-0002", center.SystemId, center.SystemId + "/planet.00", "vehicle-0002", RoutePriority.Normal);
                route.SetHeld("resource.metal", 0f);
                route.SetPhase(RoutePhase.ToSource);
                LogisticsState.AddRoute(route);

                Assert.IsTrue(center.CanDismantle(), "an empty route mid-cycle (not holding cargo, not docked) must not block dismantle");
            }
            finally { Object.DestroyImmediate(center.gameObject); Object.DestroyImmediate(starGO); LogisticsState.Reset(); }
        }
    }
}
