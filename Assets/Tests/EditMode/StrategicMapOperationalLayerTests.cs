using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Navigation;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.UI.StrategicMap;
using Nebula.UI.StrategicMap.Preview;

namespace Nebula.Tests.EditMode
{
    public class StrategicMapOperationalLayerTests
    {
        private static StrategicMapUniverseSnapshot.SystemRow System(string id = "sys.1",
            string galaxyId = "aetheria", Vector3? position = null) =>
            new StrategicMapUniverseSnapshot.SystemRow
            {
                id = id,
                galaxyId = galaxyId,
                knowledge = (int)SystemKnowledgeLevel.Visited,
                x = (position ?? new Vector3(2f, 3f, 4f)).x,
                y = (position ?? new Vector3(2f, 3f, 4f)).y,
                z = (position ?? new Vector3(2f, 3f, 4f)).z
            };

        [Test]
        public void StoredFleet_IsOneExplicitSystemSummary_AndPreservesLastKnownGate()
        {
            StrategicMapUniverseSnapshot.SystemRow system = System();
            var delta = new SystemDelta { systemId = system.id };
            delta.vehicles.Add(new VehicleDto
            {
                vehicleId = "collector.1",
                vehicleClass = (int)VehicleClass.Collector,
                phase = (int)VehiclePhase.TravelOut,
                px = 9000000f,
                py = 7000000f,
                pz = 5000000f
            });
            delta.vehicles.Add(new VehicleDto
            {
                vehicleId = "escort.1",
                vehicleClass = (int)VehicleClass.Escort,
                phase = (int)VehiclePhase.Waiting,
                stranded = true
            });
            var rows = new List<StrategicMapOperationalMarker>();

            StrategicMapOperationalDataSource.AppendStoredFleet(
                delta, system, SystemLinkState.LastKnown, rows);

            Assert.AreEqual(1, rows.Count, "the operational map aggregates a system fleet instead of spraying unit pins");
            Assert.AreEqual(StrategicMapOperationalKind.Fleet, rows[0].Kind);
            Assert.AreEqual(system.Position, rows[0].Position,
                "an aggregate is explicitly a system-centre schematic, never a fabricated fleet coordinate");
            Assert.IsTrue(rows[0].IsSchematic);
            Assert.IsTrue(rows[0].IsLastKnown);
            Assert.IsTrue(rows[0].NeedsAttention);
            StringAssert.Contains("2", rows[0].Label);
            StringAssert.Contains("1 arızalı", rows[0].Label);
        }

        [Test]
        public void StoredLogistics_UsesSystemCentreBecauseDtoPersistsNoPhysicalPosition()
        {
            StrategicMapUniverseSnapshot.SystemRow system = System();
            var reading = new LogisticsCentreReading
            {
                SystemId = system.id,
                Tier = LogisticsCenterTier.T2,
                State = LogisticsCenterState.Active,
                Capacity = 100f,
                StoredVolume = 90f
            };
            reading.Routes.Add(new CentreRouteReading
            {
                DrillWorldId = system.id + "/planet.0",
                Phase = RoutePhase.Holding,
                WaitReason = RouteWaitReason.CenterFull
            });
            var rows = new List<StrategicMapOperationalMarker>();

            StrategicMapOperationalDataSource.AppendLogistics(
                reading, system, SystemLinkState.Live, rows);

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(StrategicMapOperationalKind.Logistics, rows[0].Kind);
            Assert.AreEqual(system.Position, rows[0].Position);
            Assert.IsTrue(rows[0].IsSchematic);
            Assert.IsTrue(rows[0].NeedsAttention, "a real centre-fill/route wait must survive aggregation");
            StringAssert.Contains("T2", rows[0].Label);
            StringAssert.Contains("sistem göstergesi", rows[0].Label);
        }

        [Test]
        public void StoredThreats_AggregateEnemiesAndProbe_ThenMergeAttentionWithoutOverlap()
        {
            StrategicMapUniverseSnapshot.SystemRow system = System();
            var delta = new SystemDelta { systemId = system.id };
            delta.enemies.Add(new EnemyDto
            {
                enemyId = "enemy.1",
                defId = EnemyIds.FighterMk1
            });
            delta.enemies.Add(new EnemyDto
            {
                enemyId = "enemy.2",
                defId = EnemyIds.EmDisruptorMk1,
                telegraphRemaining = 0.8f
            });
            delta.remoteProbe = new RemoteProbeDto
            {
                phase = (int)RemoteProbePhase.Scanning,
                enemyId = "probe.1"
            };
            var rows = new List<StrategicMapOperationalMarker>();

            StrategicMapOperationalDataSource.AppendStoredThreats(
                delta, system, SystemLinkState.Live, rows);
            StrategicMapOperationalDataSource.AppendThreatAttention(
                new SystemAttentionInputs { RaidDetected = true, RaidEtaSeconds = 18f },
                system, SystemLinkState.Live, rows);

            Assert.AreEqual(1, rows.Count, "system threat and its warning must merge into one readable marker");
            Assert.AreEqual(system.Position, rows[0].Position);
            Assert.IsTrue(rows[0].IsSchematic);
            Assert.IsTrue(rows[0].NeedsAttention);
            StringAssert.Contains("3", rows[0].Label);
            StringAssert.Contains("Baskın yaklaşıyor", rows[0].Label);
        }

        [Test]
        public void DisconnectedSystems_ExposeNoOperationalCountsOrPositions()
        {
            StrategicMapUniverseSnapshot.SystemRow system = System();
            var delta = new SystemDelta { systemId = system.id };
            delta.vehicles.Add(new VehicleDto { vehicleId = "vehicle.1" });
            delta.enemies.Add(new EnemyDto { enemyId = "enemy.1", defId = EnemyIds.FighterMk1 });
            var reading = new LogisticsCentreReading
            {
                SystemId = system.id,
                Tier = LogisticsCenterTier.T1,
                State = LogisticsCenterState.Active
            };
            var rows = new List<StrategicMapOperationalMarker>();

            StrategicMapOperationalDataSource.AppendStoredFleet(
                delta, system, SystemLinkState.Disconnected, rows);
            StrategicMapOperationalDataSource.AppendLogistics(
                reading, system, SystemLinkState.Disconnected, rows);
            StrategicMapOperationalDataSource.AppendStoredThreats(
                delta, system, SystemLinkState.Disconnected, rows);
            StrategicMapOperationalDataSource.AppendThreatAttention(
                new SystemAttentionInputs { RaidDetected = true },
                system, SystemLinkState.Disconnected, rows);

            Assert.AreEqual(0, rows.Count);
        }

        [Test]
        public void OperationalSnapshot_FreezesPublishedRows()
        {
            var fleet = new List<StrategicMapOperationalMarker>
            {
                Marker("fleet", StrategicMapOperationalKind.Fleet, "g1")
            };
            var snapshot = new StrategicMapOperationalSnapshot(
                fleet, new List<StrategicMapOperationalMarker>(), new List<StrategicMapOperationalMarker>());

            fleet.Clear();

            Assert.AreEqual(1, snapshot.Fleet.Count);
        }

        [Test]
        public void MarkerLayer_DefaultsToThreat_AndScopesByGalaxyWithoutNeedingRendererGeometry()
        {
            var fleet = new[] { Marker("fleet", StrategicMapOperationalKind.Fleet, "g1") };
            var logistics = new[] { Marker("logistics", StrategicMapOperationalKind.Logistics, "g2") };
            var threats = new[] { Marker("threat", StrategicMapOperationalKind.Threat, "g1") };
            var snapshot = new StrategicMapOperationalSnapshot(fleet, logistics, threats);
            var go = new GameObject("Operational marker state fixture");
            var view = go.AddComponent<StrategicMapOperationalMarkers>();

            try
            {
                view.Refresh(snapshot);
                Assert.AreEqual(1, view.VisibleMarkerCount, "Threat is the only operational layer enabled by default");

                view.SetLayerVisible(StrategicMapOperationalKind.Fleet, true);
                view.SetLayerVisible(StrategicMapOperationalKind.Logistics, true);
                Assert.AreEqual(3, view.VisibleMarkerCount);

                view.SetScope("g2", null);
                Assert.AreEqual(1, view.VisibleMarkerCount, "galaxy focus must suppress other galaxies' summaries");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static StrategicMapOperationalMarker Marker(string id,
            StrategicMapOperationalKind kind, string galaxyId) =>
            new StrategicMapOperationalMarker(
                id, "system." + id, galaxyId, id, kind, Vector3.zero,
                isLastKnown: false, isSchematic: true, needsAttention: false);
    }
}
