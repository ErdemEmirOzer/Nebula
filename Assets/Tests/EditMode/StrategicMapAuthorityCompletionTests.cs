using System.Collections.Generic;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.UI.StrategicMap;
using Nebula.UI.StrategicMap.Preview;
using Nebula.World;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>Focused guards for map freshness, reconnect presentation and orbit authority.</summary>
    public sealed class StrategicMapAuthorityCompletionTests
    {
        private const ulong Seed = 90210UL;

        [SetUp]
        public void SetUp()
        {
            StrategicMapOrbitClock.ClearOverride();
            RemoteCommunicationService.ResetForSession();
            StarSystemRegistry.Clear();
            StarSystemRegistry.Initialize(Seed);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));
            ExtractionState.Reset(unchecked((int)Seed));
        }

        [TearDown]
        public void TearDown()
        {
            StrategicMapOrbitClock.ClearOverride();
            RemoteCommunicationService.ResetForSession();
            StarSystemRegistry.Clear();
            ExtractionState.Reset(0);
        }

        [Test]
        public void LastKnownAge_UsesSnapshotSimulationAge_NotRemoteSchedulerTimestamp()
        {
            SystemManifest system = StartSystem();
            SystemDelta delta = StarSystemRegistry.Deltas.GetOrCreate(system.SystemId);
            delta.simElapsedSeconds = 125f;
            delta.lastRemoteTickSimSeconds = 120f;

            Assert.AreEqual(125f,
                StrategicMapDataSource.LastKnownAge(system.SystemId, SystemLinkState.LastKnown));
            Assert.AreEqual(0f,
                StrategicMapDataSource.LastKnownAge(system.SystemId, SystemLinkState.Live));
            Assert.IsTrue(float.IsNaN(
                StrategicMapDataSource.LastKnownAge(system.SystemId, SystemLinkState.Disconnected)),
                "without a LastKnown snapshot the map must not invent a precise age");
        }

        [Test]
        public void ReconnectSummary_IsKnowledgeAndLinkGated_AndNeverCopiesRawDescriptions()
        {
            SystemManifest system = StartSystem();
            SystemDelta delta = StarSystemRegistry.Deltas.GetOrCreate(system.SystemId);
            delta.pendingReconnectSummary = new ReconnectSummaryDto
            {
                systemId = system.SystemId,
                raidResultText = "raw target id should not be shown",
                wasDamagedOrDisabled = true,
                logisticsMaintenanceStatusText = "raw logistics state"
            };
            delta.pendingReconnectSummary.completedTaskDescriptions.Add("Engineer vehicle.raw-id completed");
            delta.pendingReconnectSummary.stalledTaskDescriptions.Add("Route route.raw-id stalled");

            var output = new List<StrategicReconnectSummaryView>();
            StrategicMapDataSource.BuildReconnectSummaries(new[] { View(system, detailsKnown: true, SystemLinkState.Live) }, output);

            Assert.AreEqual(1, output.Count);
            Assert.AreEqual(1, output[0].CompletedTaskCount);
            Assert.AreEqual(1, output[0].StalledTaskCount);
            string playerText = StrategicMapHud.FormatReconnectSummary(output[0]);
            StringAssert.Contains(system.DisplayName, playerText);
            StringAssert.DoesNotContain("raw-id", playerText);
            StringAssert.DoesNotContain("raw target", playerText);

            output.Clear();
            StrategicMapDataSource.BuildReconnectSummaries(new[] { View(system, detailsKnown: false, SystemLinkState.Live) }, output);
            Assert.IsEmpty(output, "an unvisited system must not expose a reconnect report");

            StrategicMapDataSource.BuildReconnectSummaries(new[] { View(system, detailsKnown: true, SystemLinkState.LastKnown) }, output);
            Assert.IsEmpty(output, "a report is not a reconnect until the authoritative link is Live");
        }

        [Test]
        public void Hud_ConsumesReconnectReportOnlyAfterItHasPresentedIt()
        {
            SystemManifest system = StartSystem();
            SystemDelta delta = StarSystemRegistry.Deltas.GetOrCreate(system.SystemId);
            delta.pendingReconnectSummary = new ReconnectSummaryDto { systemId = system.SystemId };

            var root = new GameObject("StrategicMapAuthorityCompletionTests.Hud");
            try
            {
                RectTransform rect = root.AddComponent<RectTransform>();
                StrategicMapHud hud = StrategicMapHud.Create(rect, new StrategicMapUniverseSnapshot());
                var summary = new StrategicReconnectSummaryView(system.SystemId, system.DisplayName,
                    false, false, 0, 0, 0, 0, false);

                Assert.AreEqual(1, hud.PresentReconnectSummaries(new[] { summary }));
                Assert.IsNull(delta.pendingReconnectSummary,
                    "the domain report is cleared only after the visible HUD accepted it");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OrbitClock_AdvancesFromSampleEpoch_ButNeverProjectsAFrozenSnapshot()
        {
            StrategicMapOrbitClock.OverrideSeconds(10d);
            var moving = new StrategicMapUniverseSnapshot.PlanetRow
            {
                phase = 1f,
                orbitAngularSpeed = 0.5f,
                phaseEpochSeconds = 4d,
                phaseFrozen = false
            };
            var frozen = new StrategicMapUniverseSnapshot.PlanetRow
            {
                phase = 1f,
                orbitAngularSpeed = 0.5f,
                phaseEpochSeconds = 4d,
                phaseFrozen = true
            };

            Assert.AreEqual(4f, StrategicMapOrbitClock.PhaseOf(moving), 0.0001f);
            Assert.AreEqual(1f, StrategicMapOrbitClock.PhaseOf(frozen), 0.0001f);
        }

        [Test]
        public void LiveUniverse_UsesPersistedPlanetPositionAsFrozenAuthoritativePhase()
        {
            SystemManifest system = StartSystem();
            PlanetManifest planet = system.Planets[0];
            const float phase = 1.234f;
            Quaternion plane = Quaternion.AngleAxis(planet.AscendingNodeDegrees, Vector3.up)
                               * Quaternion.AngleAxis(planet.OrbitInclinationDegrees, Vector3.right);
            Vector3 absolute = system.AbsoluteCenter
                               + plane * new Vector3(Mathf.Cos(phase), 0f, Mathf.Sin(phase)) * planet.OrbitRadius;
            StarSystemRegistry.Deltas.GetOrCreate(system.SystemId).planetSnapshots.Add(new PlanetSnapDto
            {
                worldId = planet.PlanetId,
                px = absolute.x,
                py = absolute.y,
                pz = absolute.z
            });
            var knowledge = new SystemKnowledgeService();
            knowledge.ForceLevel(system.SystemId, SystemKnowledgeLevel.Visited);

            StrategicMapUniverseSnapshot universe = StrategicMapLiveUniverseSource.Build(
                knowledge, new List<StrategicMapLiveUniverseSource.GalaxyDescriptor>(), Vector3.zero, null);
            StrategicMapUniverseSnapshot.PlanetRow row = FindPlanet(universe, planet.PlanetId);

            Assert.IsTrue(row.phaseFrozen);
            Assert.AreEqual(0f, Mathf.DeltaAngle(phase * Mathf.Rad2Deg, row.phase * Mathf.Rad2Deg), 0.01f);
            StrategicMapOrbitClock.OverrideSeconds(999d);
            Assert.AreEqual(row.phase, StrategicMapOrbitClock.PhaseOf(row), 0.0001f,
                "last-known geometry must not imply an unseen precise orbit update");
        }

        private static SystemManifest StartSystem() => StarSystemRegistry.Manifests.ResolveSystem(
            WorldIdCodec.SystemId(new SectorCoordinate(0, 0, 0), 0));

        private static StrategicSystemView View(SystemManifest system, bool detailsKnown, SystemLinkState link) =>
            new StrategicSystemView(system.SystemId, system.DisplayName, system.Sector,
                detailsKnown ? SystemKnowledgeLevel.Visited : SystemKnowledgeLevel.Located,
                link, true, system.AbsoluteCenter, Vector3.zero, detailsKnown,
                detailsKnown ? system.StarClass : default, detailsKnown ? system.StarRadius : 0f,
                detailsKnown ? system.NavigationRadius : 0f, detailsKnown ? system.Planets.Count : 0,
                SystemOwnershipTag.Discovered, false, system.IsStartSystem, false, false,
                link == SystemLinkState.LastKnown ? 0f : float.NaN,
                System.Array.Empty<StrategicPlanetView>(), System.Array.Empty<StrategicPoiView>());

        private static StrategicMapUniverseSnapshot.PlanetRow FindPlanet(
            StrategicMapUniverseSnapshot universe, string planetId)
        {
            for (int i = 0; i < universe.planets.Count; i++)
                if (universe.planets[i].id == planetId) return universe.planets[i];
            Assert.Fail($"planet {planetId} missing");
            return null;
        }
    }
}
