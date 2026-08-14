using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.UI.StrategicMap;
using Nebula.UI.StrategicMap.Preview;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The strategic map's LIVE universe source: the geometry the 3D renderer draws is
    /// built from the real manifests under the same information gates as the data
    /// adapter. These tests prove the geometry cannot leak an unearned system or an
    /// unscanned surface, that ids are the real ones, and that the world→map scale and
    /// ordering are exact and deterministic.
    /// </summary>
    public class StrategicMapLiveUniverseSourceTests
    {
        private const ulong Seed = 9182UL;
        private const float MapUnit = StrategicMapOrbits.MapUnit;

        [SetUp]
        public void SetUp()
        {
            StarSystemRegistry.Clear();
            StarSystemRegistry.Initialize(Seed);
            var startSector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(startSector, WorldIdCodec.SystemId(startSector, 0));
            ExtractionState.Reset((int)Seed);
            PoiManifestRepository.Reset();
            PoiLiveState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            StarSystemRegistry.Clear();
            ExtractionState.Reset(0);
            PoiManifestRepository.Reset();
            PoiLiveState.Reset();
        }

        private static SystemManifest Sector0System(int index)
        {
            var sector = new SectorCoordinate(0, 0, 0);
            return StarSystemRegistry.Manifests.ResolveSystem(WorldIdCodec.SystemId(sector, index));
        }

        /// <summary>The map now also draws the surrounding sectors' stars, so a row must be found by id, never by index.</summary>
        private static StrategicMapUniverseSnapshot.SystemRow RowOf(StrategicMapUniverseSnapshot snapshot, string systemId)
        {
            foreach (StrategicMapUniverseSnapshot.SystemRow row in snapshot.systems)
                if (row.id == systemId) return row;
            Assert.Fail($"system {systemId} is missing from the geometry");
            return null;
        }

        private static List<StrategicMapLiveUniverseSource.GalaxyDescriptor> TwoGalaxies() =>
            new List<StrategicMapLiveUniverseSource.GalaxyDescriptor>
            {
                new StrategicMapLiveUniverseSource.GalaxyDescriptor("Aetheria", new Vector3(-4_500_000f, 0f, 0f),
                    10_000_000f, 150000, 120000f, 0f, -9.9f),
                new StrategicMapLiveUniverseSource.GalaxyDescriptor("Thalassa", new Vector3(14_500_000f, 0f, 0f),
                    8_000_000f, 120000, 96000f, 0.2f, -20.9f)
            };

        // ==================== information gates ====================

        [Test]
        public void OnlyTheSystemsThePlayerKnows_AreDrawnAsNavigableStars()
        {
            var knowledge = new SystemKnowledgeService();
            knowledge.ForceLevel(Sector0System(0).SystemId, SystemKnowledgeLevel.Visited);

            StrategicMapUniverseSnapshot snapshot =
                StrategicMapLiveUniverseSource.Build(knowledge, TwoGalaxies(), Vector3.zero, null);

            // Only what the player actually knows. The map never conjures a cloud of
            // sector-generated systems around the ship: the stars it shows beyond the
            // player's own are the galaxies', drawn by the decor-star layer.
            Assert.AreEqual(1, snapshot.systems.Count);
            Assert.AreEqual(Sector0System(0).SystemId, snapshot.systems[0].id);
        }

        [Test]
        public void TheMapGeneratesNoSectorStarsOfItsOwn()
        {
            List<StrategicMapLiveUniverseSource.GalaxyDescriptor> galaxies = TwoGalaxies();
            var sectors = new HashSet<SectorCoordinate>();
            foreach (SectorCoordinate sector in
                     StrategicMapLiveUniverseSource.SectorsToDraw(galaxies, Vector3.zero))
                Assert.IsTrue(sectors.Add(sector), $"{sector} was walked twice");

            Assert.AreEqual(0, sectors.Count,
                "the map generates no stars of its own -- the galaxies' arm stars are the sky");
        }

        [Test]
        public void SignalSystem_UsesOnlyAFixedQuantizedSurrogate_AndCarriesNoRealDetails()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest signal = Sector0System(0);
            knowledge.ForceLevel(signal.SystemId, SystemKnowledgeLevel.Signal);
            Vector3 shipAbsolute = signal.AbsoluteCenter + new Vector3(1234f, 765f, 5678f);

            StrategicMapUniverseSnapshot snapshot =
                StrategicMapLiveUniverseSource.Build(knowledge, TwoGalaxies(), shipAbsolute, null);

            StrategicMapUniverseSnapshot.SystemRow row = RowOf(snapshot, signal.SystemId);
            Vector3 shipMap = shipAbsolute / MapUnit;
            Vector3 surrogateOffset = row.Position - shipMap;

            Assert.AreEqual((int)SystemKnowledgeLevel.Signal, row.knowledge);
            Assert.AreEqual("signal", row.origin);
            Assert.AreEqual("Bilinmeyen sinyal", row.name, "the real system name is not a Signal-level fact");
            Assert.AreNotEqual(signal.AbsoluteCenter / MapUnit, row.Position,
                "a Signal marker must never be placed at the real system coordinate");
            Assert.AreEqual(StrategicMapLiveUniverseSource.SignalDisplayRadiusMapUnits,
                new Vector2(surrogateOffset.x, surrogateOffset.z).magnitude, 0.0001f,
                "the display marker carries a fixed presentation radius, never the real distance");
            Assert.AreEqual(0f, surrogateOffset.y, 0.000001f, "the Signal gate reveals no vertical displacement");

            float angle = Mathf.Atan2(surrogateOffset.z, surrogateOffset.x) * Mathf.Rad2Deg;
            float quantizationError = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 45f) * 45f));
            Assert.Less(quantizationError, 0.01f, "Signal headings are restricted to 45-degree cones");

            Assert.AreEqual(0f, row.starRadius);
            Assert.AreEqual(0f, row.navigationRadius);
            Assert.AreEqual(0, row.planetCount);
            Assert.AreEqual(row.planetFrom, row.planetTo);
            Assert.IsEmpty(snapshot.planets, "Signal geometry must not smuggle planet count or surface data");
        }

        [Test]
        public void ACheaplyEnumeratedStar_AgreesWithItsFullyResolvedManifest()
        {
            // The map enumerates distant stars without building their planets. If that short
            // path ever drifted from the generator, the map would draw stars where they are not.
            var sector = new SectorCoordinate(3, -1, 2);
            var placements = new List<SectorManifestGenerator.StarPlacement>();
            SectorManifestGenerator.EnumerateStars(Seed, sector, placements);

            SectorManifest resolved = StarSystemRegistry.Manifests.Resolve(sector);
            Assert.AreEqual(resolved.Systems.Count, placements.Count);

            foreach (SectorManifestGenerator.StarPlacement placement in placements)
            {
                SystemManifest real = resolved.FindSystem(placement.SystemId);
                Assert.IsNotNull(real, $"{placement.SystemId} must exist in the resolved sector");
                Assert.AreEqual(real.AbsoluteCenter, placement.AbsoluteCenter,
                    "a cheaply drawn star must be exactly where the real system is");
                Assert.AreEqual(real.StarClass, placement.StarClass);
            }
        }

        [Test]
        public void LocatedSystem_HasTheRealScaledPosition_ButNoStarOrPlanetGeometry()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest located = Sector0System(0);
            knowledge.ForceLevel(located.SystemId, SystemKnowledgeLevel.Located);

            StrategicMapUniverseSnapshot snapshot =
                StrategicMapLiveUniverseSource.Build(knowledge, TwoGalaxies(), Vector3.zero, null);

            StrategicMapUniverseSnapshot.SystemRow row = RowOf(snapshot, located.SystemId);
            Assert.AreEqual(located.AbsoluteCenter.x / MapUnit, row.x, 0.000001f);
            Assert.AreEqual(located.AbsoluteCenter.y / MapUnit, row.y, 0.000001f);
            Assert.AreEqual(located.AbsoluteCenter.z / MapUnit, row.z, 0.000001f);
            Assert.AreEqual(0f, row.starRadius, "Located earns a coordinate, not stellar details");
            Assert.AreEqual(0f, row.navigationRadius);
            Assert.AreEqual(0, row.planetCount);
            Assert.AreEqual(0, row.planetTo - row.planetFrom);
        }

        [Test]
        public void VisitedSystem_CarriesRealPlanetGeometry_WithRealIds()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest visited = Sector0System(0);
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);

            StrategicMapUniverseSnapshot snapshot =
                StrategicMapLiveUniverseSource.Build(knowledge, TwoGalaxies(), Vector3.zero, null);

            StrategicMapUniverseSnapshot.SystemRow row = RowOf(snapshot, visited.SystemId);
            Assert.AreEqual(visited.StarRadius, row.starRadius);
            Assert.AreEqual(visited.Planets.Count, row.planetCount);
            Assert.AreEqual(visited.Planets.Count, row.planetTo - row.planetFrom);

            for (int i = 0; i < visited.Planets.Count; i++)
            {
                PlanetManifest pm = visited.Planets[i];
                StrategicMapUniverseSnapshot.PlanetRow planet = snapshot.planets[row.planetFrom + i];

                Assert.AreEqual(pm.PlanetId, planet.id, "the 3D map must use the REAL world id, so picks resolve against the gameplay snapshot");
                Assert.AreEqual(pm.PlanetId, planet.worldId);
                Assert.AreEqual(visited.SystemId, planet.systemId);
                Assert.AreEqual(pm.OrbitRadius / MapUnit, planet.orbit, 0.000001f, "orbit is stored in map units");
                Assert.AreEqual(pm.PlanetRadius, planet.radius, "planet radius stays in world units, as the converted asset did");
                Assert.AreEqual(pm.StartAngleDegrees * Mathf.Deg2Rad, planet.phase, 0.0001f, "phase is stored in radians");
                Assert.AreEqual(pm.OrbitInclinationDegrees, planet.inclinationDegrees);
                Assert.AreEqual(pm.AscendingNodeDegrees, planet.ascendingNodeDegrees);
            }
        }

        [Test]
        public void PlanetsKeepTheirOwnFamilies_SoASystemIsNotFiveCopiesOfOneWorld()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest visited = Sector0System(0);
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);
            foreach (PlanetManifest planet in visited.Planets) ExtractionState.MarkScanned(planet.PlanetId);

            StrategicMapUniverseSnapshot snapshot =
                StrategicMapLiveUniverseSource.Build(knowledge, TwoGalaxies(), Vector3.zero, null);
            StrategicMapUniverseSnapshot.SystemRow row = RowOf(snapshot, visited.SystemId);

            var families = new HashSet<string>();
            for (int p = row.planetFrom; p < row.planetTo; p++)
            {
                StrategicMapUniverseSnapshot.PlanetRow planet = snapshot.planets[p];
                Assert.IsNotEmpty(planet.resourceFamily,
                    "an empty family falls through to the catalog's default body, which is what made every planet identical");
                families.Add(planet.resourceFamily);
            }

            Assert.Greater(families.Count, 1,
                "the start system is generated with a balanced spread of families; the map must show that spread");
        }

        [Test]
        public void SignalPoi_HasNoGeometryMarkerAndCannotLeakItsPositionTypeOrRisk()
        {
            SystemManifest visited = Sector0System(1);
            Assert.IsNotNull(visited);
            PoiManifestRepository.Ledger.RecordVisit(visited.SystemId);
            List<PoiManifest> manifests = PoiManifestRepository.Resolve(Seed, visited);
            Assert.IsNotEmpty(manifests, "the first foreign visit must provide a real POI for the gate test");
            PoiManifest poi = manifests[0];

            SystemDelta delta = StarSystemRegistry.Deltas.GetOrCreate(visited.SystemId);
            delta.pois.Add(new PoiDto { poiId = poi.PoiId, level = (int)PoiKnowledgeLevel.Signal });
            var knowledge = new SystemKnowledgeService();
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);

            StrategicMapUniverseSnapshot snapshot =
                StrategicMapLiveUniverseSource.Build(knowledge, TwoGalaxies(), Vector3.zero, null);

            Assert.IsFalse(Contains(snapshot.discoveries, poi.PoiId));
            Assert.IsFalse(Contains(snapshot.anomalies, poi.PoiId));
            Assert.IsFalse(Contains(snapshot.derelicts, poi.PoiId),
                "a Signal POI has only an existence hint; emitting any positioned marker would leak its exact manifest coordinate");
        }

        // ==================== determinism and ordering ====================

        [Test]
        public void Systems_AreCanonicallyOrdered_AndTwoBuildsAreIdentical()
        {
            var knowledge = new SystemKnowledgeService();
            knowledge.ForceLevel(Sector0System(2).SystemId, SystemKnowledgeLevel.Visited);
            knowledge.ForceLevel(Sector0System(0).SystemId, SystemKnowledgeLevel.Visited);
            knowledge.ForceLevel(Sector0System(1).SystemId, SystemKnowledgeLevel.Located);

            StrategicMapUniverseSnapshot a =
                StrategicMapLiveUniverseSource.Build(knowledge, TwoGalaxies(), Vector3.one * 5000f, null);
            StrategicMapUniverseSnapshot b =
                StrategicMapLiveUniverseSource.Build(knowledge, TwoGalaxies(), Vector3.one * 5000f, null);

            for (int i = 1; i < a.systems.Count; i++)
                Assert.Less(string.CompareOrdinal(a.systems[i - 1].id, a.systems[i].id), 0,
                    "node identity must never shuffle between rebuilds");

            Assert.AreEqual(a.systems.Count, b.systems.Count);
            for (int i = 0; i < a.systems.Count; i++)
            {
                Assert.AreEqual(a.systems[i].id, b.systems[i].id);
                Assert.AreEqual(a.systems[i].Position, b.systems[i].Position);
                Assert.AreEqual(a.systems[i].starClass, b.systems[i].starClass);
            }
        }

        // ==================== galaxies, ship, degradation ====================

        [Test]
        public void GalaxyRows_AreScaledToMapUnits_AndKeepTheAssetsUnitConventions()
        {
            var knowledge = new SystemKnowledgeService();
            StrategicMapUniverseSnapshot snapshot =
                StrategicMapLiveUniverseSource.Build(knowledge, TwoGalaxies(), Vector3.zero, null);

            Assert.AreEqual(2, snapshot.galaxies.Count);
            StrategicMapUniverseSnapshot.GalaxyRow aetheria = snapshot.galaxies[0];
            Assert.AreEqual("aetheria", aetheria.id, "galaxy ids are the lowercase name, as the reference's were");
            Assert.AreEqual(-4.5f, aetheria.centerX, 0.0001f);
            Assert.AreEqual(10f, aetheria.radius, 0.0001f, "galaxy radius is in map units");
            Assert.AreEqual(120000f, aetheria.blackHoleRadius, "black-hole radius stays in world units, matching the converted asset");
        }

        [Test]
        public void SystemGalaxyMembership_UsesRadiusNormalizedDistance()
        {
            List<StrategicMapLiveUniverseSource.GalaxyDescriptor> galaxies = TwoGalaxies();

            Assert.AreEqual("aetheria",
                StrategicMapLiveUniverseSource.NearestGalaxyId(new Vector3(-4_000_000f, 0f, 0f), galaxies));
            Assert.AreEqual("thalassa",
                StrategicMapLiveUniverseSource.NearestGalaxyId(new Vector3(13_000_000f, 0f, 0f), galaxies));
            Assert.AreEqual("", StrategicMapLiveUniverseSource.NearestGalaxyId(Vector3.zero,
                new List<StrategicMapLiveUniverseSource.GalaxyDescriptor>()), "no galaxies degrades to empty, never throws");
        }

        [Test]
        public void ShipPosition_IsScaledToMapUnits()
        {
            var knowledge = new SystemKnowledgeService();
            StrategicMapUniverseSnapshot snapshot = StrategicMapLiveUniverseSource.Build(
                knowledge, TwoGalaxies(), new Vector3(450_000f, 6300f, -35_700f), null);

            Assert.AreEqual(0.45f, snapshot.shipX, 0.000001f);
            Assert.AreEqual(0.0063f, snapshot.shipY, 0.000001f);
            Assert.AreEqual(-0.0357f, snapshot.shipZ, 0.000001f);
        }

        [Test]
        public void MissingAuthorities_DegradeGracefully_NeverThrow()
        {
            StrategicMapUniverseSnapshot noKnowledge =
                StrategicMapLiveUniverseSource.Build(null, TwoGalaxies(), Vector3.zero, null);
            Assert.AreEqual(2, noKnowledge.galaxies.Count, "scenery galaxies are session facts, not knowledge-gated");
            foreach (StrategicMapUniverseSnapshot.SystemRow row in noKnowledge.systems)
                Assert.IsFalse(row.Discovered,
                    "with no knowledge service at all, every star must read as undiscovered rather than as a known place");

            StarSystemRegistry.Clear();
            var knowledge = new SystemKnowledgeService();
            knowledge.ForceLevel("sys000", SystemKnowledgeLevel.Visited);
            StrategicMapUniverseSnapshot noRegistry =
                StrategicMapLiveUniverseSource.Build(knowledge, TwoGalaxies(), Vector3.zero, null);
            Assert.AreEqual(0, noRegistry.systems.Count, "a presentation source must degrade, never throw");
        }
        [Test]
        public void DecorativeGalaxyStars_AreSceneryOnly_NotSystemsPlanetsOrPickTargets()
        {
            List<StrategicMapLiveUniverseSource.GalaxyDescriptor> galaxies = TwoGalaxies();
            var snapshot = new StrategicMapUniverseSnapshot();
            var positions = new List<Vector3>();
            for (int i = 0; i < 40; i++)
                positions.Add(new Vector3(-4_500_000f + i * 120_000f, i * 9_000f, i * 40_000f));

            StrategicMapLiveUniverseSource.AddDecorStars(snapshot, galaxies, positions);

            Assert.AreEqual(positions.Count, snapshot.decorStars.Count);
            Assert.IsEmpty(snapshot.systems,
                "decorative arm points are not gameplay systems and must never become navigation targets");
            Assert.IsEmpty(snapshot.planets, "scenery stars must never synthesize planets or gameplay facts");

            for (int i = 0; i < snapshot.decorStars.Count; i++)
            {
                StrategicMapUniverseSnapshot.MarkerRow marker = snapshot.decorStars[i];
                Assert.AreEqual("decor." + i, marker.id);
                Assert.IsEmpty(marker.name);
                Assert.IsEmpty(marker.systemId);
                Assert.AreEqual(-1, marker.type);
                Assert.AreEqual(-1, marker.risk);
                Assert.AreEqual(positions[i] / MapUnit, marker.Position);
            }

            var cameraObject = new GameObject("DecorStarPicking.Camera");
            var target = new RenderTexture(256, 256, 0);
            Camera camera = null;
            try
            {
                camera = cameraObject.AddComponent<Camera>();
                camera.targetTexture = target;
                camera.transform.position = snapshot.decorStars[0].Position + Vector3.back * 10f;
                camera.transform.rotation = Quaternion.identity;

                Vector3 projected = camera.WorldToScreenPoint(snapshot.decorStars[0].Position);
                StrategicMapPick pick = StrategicMapPicker.Pick(camera, snapshot,
                    new Vector2(projected.x, projected.y), StrategicMapLevel.Galaxy,
                    snapshot.decorStars[0].galaxyId, null, 1f);

                Assert.AreEqual(StrategicMapPickKind.None, pick.Kind,
                    "clicking directly on a decorative point must not fabricate a selectable system");
            }
            finally
            {
                // Order matters, and getting it wrong is an engine ERROR, not a warning:
                // "Releasing render texture that is set as Camera.targetTexture!" fails the
                // test outright. The camera has to let go of the target before the target
                // can be released.
                if (camera != null) camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }

        private static bool Contains(IReadOnlyList<StrategicMapUniverseSnapshot.MarkerRow> rows, string id)
        {
            for (int i = 0; i < rows.Count; i++) if (rows[i].id == id) return true;
            return false;
        }

    }
}
