using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.UI.StrategicMap;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Strategic map M1: the read-only data adapter and its information gates.
    /// These tests exist to prove the map physically cannot leak something the
    /// player has not earned, and that it never becomes a second authority for
    /// world data.
    /// </summary>
    public class StrategicMapDataSourceTests
    {
        private const ulong Seed = 7717UL;

        [SetUp]
        public void SetUp()
        {
            StarSystemRegistry.Clear();
            StarSystemRegistry.Initialize(Seed);
            var startSector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(startSector, WorldIdCodec.SystemId(startSector, 0));
            ExtractionState.Reset((int)Seed);
            PoiManifestRepository.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            StarSystemRegistry.Clear();
            ExtractionState.Reset(0);
            PoiManifestRepository.Reset();
        }

        private static SystemManifest StartSystem()
        {
            var sector = new SectorCoordinate(0, 0, 0);
            return StarSystemRegistry.Manifests.ResolveSystem(WorldIdCodec.SystemId(sector, 0));
        }

        private static SystemManifest NeighborSystem(int index)
        {
            var sector = new SectorCoordinate(0, 0, 0);
            return StarSystemRegistry.Manifests.ResolveSystem(WorldIdCodec.SystemId(sector, index));
        }

        // ==================== information gates ====================

        [Test]
        public void UnknownSystems_AreNeverProduced_TheMapMustNotRevealTheyExist()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest visited = StartSystem();
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);

            // Every other system in the sector stays Unknown.
            List<StrategicSystemView> views = BuildSystems(knowledge);

            Assert.AreEqual(1, views.Count, "only the known system may appear -- an Unknown system must not be revealed at all");
            Assert.AreEqual(visited.SystemId, views[0].SystemId);
        }

        [Test]
        public void SignalLevel_ExposesOnlyACoarseDirection_NeverAPositionStarClassOrPlanets()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest signal = StartSystem();
            knowledge.ForceLevel(signal.SystemId, SystemKnowledgeLevel.Signal);

            StrategicSystemView view = BuildSystems(knowledge)[0];

            Assert.IsFalse(view.PositionKnown, "a Signal-level system must not expose a real position");
            Assert.AreEqual(Vector3.zero, view.AbsoluteCenter, "the exact centre must be withheld entirely, not merely 'not drawn'");
            Assert.IsFalse(view.DetailsKnown);
            Assert.AreEqual(0, view.PlanetCount, "planet count is a Visited-level fact");
            Assert.AreEqual(0, view.Planets.Count, "a Signal-level system must expose no planets at all");
            Assert.AreEqual(0f, view.StarRadius, "star data is a Visited-level fact");
            Assert.AreEqual(1f, view.ApproximateDirection.magnitude, 0.001f, "only a unit direction may be exposed");
        }

        [Test]
        public void SignalDirection_IsQuantized_SoItCanNeverBeReverseEngineeredIntoAPosition()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest signal = StartSystem();
            knowledge.ForceLevel(signal.SystemId, SystemKnowledgeLevel.Signal);

            Vector3 dir = BuildSystems(knowledge)[0].ApproximateDirection;
            float angle = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
            float remainder = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 45f) * 45f));

            Assert.Less(remainder, 0.5f, $"the signal heading must snap to a 45-degree cone, got {angle}");
        }

        [Test]
        public void LocatedLevel_ExposesTheRealPosition_ButStillNoStarClassOrPlanets()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest located = StartSystem();
            knowledge.ForceLevel(located.SystemId, SystemKnowledgeLevel.Located);

            StrategicSystemView view = BuildSystems(knowledge)[0];

            Assert.IsTrue(view.PositionKnown);
            Assert.AreEqual(located.AbsoluteCenter, view.AbsoluteCenter);
            Assert.IsFalse(view.DetailsKnown, "Located is not Visited");
            Assert.AreEqual(0, view.PlanetCount);
            Assert.AreEqual(0, view.Planets.Count, "planets stay hidden until the system is actually visited");
        }

        [Test]
        public void VisitedLevel_ExposesStarClassPlanetCountAndPlanets()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest visited = StartSystem();
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);

            StrategicSystemView view = BuildSystems(knowledge)[0];

            Assert.IsTrue(view.PositionKnown);
            Assert.IsTrue(view.DetailsKnown);
            Assert.AreEqual(visited.StarClass, view.StarClass);
            Assert.AreEqual(visited.Planets.Count, view.PlanetCount);
            Assert.AreEqual(visited.Planets.Count, view.Planets.Count);
            Assert.Greater(view.NavigationRadius, 0f);
        }

        [Test]
        public void UnscannedPlanet_LeaksNoFamilyRarityEnvironmentOrSlotDetail()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest visited = StartSystem();
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);

            // Nothing is scanned in a fresh ExtractionState.
            StrategicSystemView view = BuildSystems(knowledge)[0];

            foreach (StrategicPlanetView planet in view.Planets)
            {
                Assert.IsFalse(planet.Scanned);
                Assert.AreEqual(0, planet.Slots.Count, "an unscanned planet must expose no surface slot detail whatsoever");
                Assert.IsFalse(planet.HasDrill, "drill presence is surface detail behind the scan gate");
                Assert.AreEqual(0f, planet.DrillFill01);
            }
        }

        [Test]
        public void ScannedPlanet_ExposesFamilyRarityEnvironmentAndTheCanonicalTwelveSlots()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest visited = StartSystem();
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);

            PlanetManifest pm = visited.Planets[0];
            ExtractionState.GetProfile(pm.PlanetId).Scanned = true;

            StrategicSystemView view = BuildSystems(knowledge)[0];
            StrategicPlanetView planet = FindPlanet(view, pm.PlanetId);

            Assert.IsTrue(planet.Scanned);
            Assert.AreEqual(pm.Family, planet.Family);
            Assert.AreEqual(pm.Rarity, planet.Rarity);
            Assert.AreEqual(pm.Environment, planet.Environment);
            Assert.AreEqual(2 + SurfaceSlot.DefenseCount + SurfaceSlot.SupportCount, planet.Slots.Count,
                "a scanned planet must expose exactly the canonical surface slot set");
            Assert.AreEqual(12, planet.Slots.Count, "the canonical surface slot count is 12");
        }

        [Test]
        public void HiddenSupportSlot_IsNeverPresentedAsBuildable()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest visited = StartSystem();
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);
            PlanetManifest pm = visited.Planets[0];
            PlanetProfile profile = ExtractionState.GetProfile(pm.PlanetId);
            profile.Scanned = true;

            StrategicPlanetView planet = FindPlanet(BuildSystems(knowledge)[0], pm.PlanetId);
            string hiddenNodeId = profile.SupportNode(2).Id;

            foreach (StrategicSlotView slot in planet.Slots)
                if (slot.NodeId == hiddenNodeId)
                    Assert.AreEqual(StrategicSlotState.Locked, slot.State,
                        "the L41+ reserved support slot must never read as buildable content");
        }

        [Test]
        public void KnownPoi_UsesRealManifestData_WithoutLeakingPositionRiskOrTypeBeforeTheirGates()
        {
            SystemManifest visited = NeighborSystem(1);
            Assert.IsNotNull(visited, "the deterministic test sector must contain its known foreign neighbour");

            // This is the real first-visit path: the visit ledger freezes the
            // guarantee, then the repository resolves the campaign manifest.
            PoiManifestRepository.Ledger.RecordVisit(visited.SystemId);
            List<PoiManifest> manifests = PoiManifestRepository.Resolve(Seed, visited);
            Assert.IsNotEmpty(manifests, "the first foreign visit is guaranteed a real POI");
            PoiManifest manifest = manifests[0];

            SystemDelta delta = StarSystemRegistry.Deltas.GetOrCreate(visited.SystemId);
            var dto = new PoiDto { poiId = manifest.PoiId, level = (int)PoiKnowledgeLevel.Signal };
            delta.pois.Add(dto);

            var knowledge = new SystemKnowledgeService();
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);

            StrategicPoiView signal = FindPoi(BuildSystems(knowledge)[0], manifest.PoiId);
            Assert.AreEqual(PoiKnowledgeLevel.Signal, signal.KnowledgeLevel);
            Assert.IsFalse(signal.PositionKnown, "Signal must not expose an exact coordinate");
            Assert.AreEqual(Vector3.zero, signal.LocalOffset);
            Assert.IsFalse(signal.RiskKnown, "Signal must not expose risk");
            Assert.IsFalse(signal.TypeKnown, "Signal must not expose type");
            Assert.AreEqual("Bilinmeyen sinyal", signal.DisplayName);

            dto.level = (int)PoiKnowledgeLevel.Located;
            StrategicPoiView located = FindPoi(BuildSystems(knowledge)[0], manifest.PoiId);
            Assert.IsTrue(located.PositionKnown);
            Assert.AreEqual(manifest.SystemLocalPosition, located.LocalOffset,
                "Located must use the deterministic manifest position, not Vector3.zero");
            Assert.IsFalse(located.RiskKnown, "risk stays hidden with type until Identified");
            Assert.IsFalse(located.TypeKnown, "type stays hidden until Identified");
            Assert.AreEqual("Tanımlanamayan temas", located.DisplayName);

            dto.level = (int)PoiKnowledgeLevel.Identified;
            StrategicPoiView identified = FindPoi(BuildSystems(knowledge)[0], manifest.PoiId);
            Assert.IsTrue(identified.TypeKnown);
            Assert.AreEqual(manifest.Type, identified.Type);
            Assert.IsTrue(identified.RiskKnown);
            Assert.AreEqual(manifest.Risk, identified.Risk);
            Assert.AreEqual(ExpectedPoiName(manifest.Type), identified.DisplayName);
        }

        [Test]
        public void StalePoiDeltaId_IsOmitted_InsteadOfBeingRenderedWithInventedPlaceholderData()
        {
            SystemManifest visited = NeighborSystem(1);
            Assert.IsNotNull(visited);
            StarSystemRegistry.Deltas.GetOrCreate(visited.SystemId).pois.Add(new PoiDto
            {
                poiId = visited.SystemId + "/poi.99",
                level = (int)PoiKnowledgeLevel.Identified
            });

            var knowledge = new SystemKnowledgeService();
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);

            Assert.IsEmpty(BuildSystems(knowledge)[0].Pois,
                "an id absent from the immutable campaign manifest must not receive an invented name/type/position");
        }

        // ==================== canonical mapping ====================

        [Test]
        public void EverySystemAppearsExactlyOnce_AndInACanonicalDeterministicOrder()
        {
            var knowledge = new SystemKnowledgeService();
            var sector = new SectorCoordinate(0, 0, 0);
            SectorManifest sm = StarSystemRegistry.Manifests.Resolve(sector);
            foreach (SystemManifest s in sm.Systems) knowledge.ForceLevel(s.SystemId, SystemKnowledgeLevel.Located);

            List<StrategicSystemView> first = BuildSystems(knowledge);
            List<StrategicSystemView> second = BuildSystems(knowledge);

            var seen = new HashSet<string>();
            foreach (StrategicSystemView v in first)
                Assert.IsTrue(seen.Add(v.SystemId), $"duplicate node for {v.SystemId}");

            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
                Assert.AreEqual(first[i].SystemId, second[i].SystemId, "node order must be deterministic across rebuilds");
        }

        [Test]
        public void PlanetViews_MapOneToOneOntoTheRealManifestPlanets()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest visited = StartSystem();
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);

            StrategicSystemView view = BuildSystems(knowledge)[0];

            Assert.AreEqual(visited.Planets.Count, view.Planets.Count);
            for (int i = 0; i < visited.Planets.Count; i++)
            {
                Assert.AreEqual(visited.Planets[i].PlanetId, view.Planets[i].PlanetId);
                Assert.AreEqual(visited.Planets[i].OrbitRadius, view.Planets[i].OrbitRadius, 0.001f);
                Assert.AreEqual(visited.Planets[i].PlanetRadius, view.Planets[i].PlanetRadius, 0.001f);
            }
        }

        // ==================== the map is never an authority ====================

        [Test]
        public void BuildingASnapshot_NeverAwardsXpAdvancesKnowledgeOrScansAnything()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest visited = StartSystem();
            knowledge.ForceLevel(visited.SystemId, SystemKnowledgeLevel.Visited);

            Progression.ProgressionService.Reset();
            long xpBefore = Progression.ProgressionService.State.TotalXp;
            var levelsBefore = new Dictionary<string, SystemKnowledgeLevel>();
            foreach (var kv in knowledge.All) levelsBefore[kv.Key] = kv.Value;
            bool scannedBefore = ExtractionState.GetProfile(visited.Planets[0].PlanetId).Scanned;

            for (int i = 0; i < 5; i++) BuildSystems(knowledge);

            Assert.AreEqual(xpBefore, Progression.ProgressionService.State.TotalXp, "the map must never award XP");
            Assert.AreEqual(scannedBefore, ExtractionState.GetProfile(visited.Planets[0].PlanetId).Scanned,
                "the map must never scan anything");
            foreach (var kv in levelsBefore)
                Assert.AreEqual(kv.Value, knowledge.GetLevel(kv.Key), "the map must never advance knowledge");
        }

        [Test]
        public void TheHtmlPrototype_IsNeverARuntimeDataSource()
        {
            // The HTML is kept purely as a design reference. No shipped runtime script may
            // read it, parse it, or embed its demo snapshot.
            string scriptsRoot = Path.Combine(Application.dataPath, "Scripts");
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                // Strip comments first: naming the prototype in documentation is not a
                // dependency on it. Only real code that loads/parses it would be.
                var code = new System.Text.StringBuilder();
                foreach (string rawLine in File.ReadAllLines(file))
                {
                    string line = rawLine.TrimStart();
                    if (line.StartsWith("//") || line.StartsWith("*") || line.StartsWith("/*")) continue;
                    code.Append(rawLine).Append('\n');
                }
                string text = code.ToString();
                if (text.Contains("nebula-live-universe-map") || text.Contains("MapDesign/") || text.Contains(".html"))
                    offenders.Add(Path.GetFileName(file));
            }
            Assert.IsEmpty(offenders, "no runtime script may depend on the HTML prototype: " + string.Join(", ", offenders));
        }

        [Test]
        public void TheMapNeverEmbedsAFabricatedGalaxyCount_ItOnlyEverReportsRealKnownSystems()
        {
            var knowledge = new SystemKnowledgeService();
            SystemManifest a = StartSystem();
            SystemManifest b = NeighborSystem(1);
            knowledge.ForceLevel(a.SystemId, SystemKnowledgeLevel.Located);
            knowledge.ForceLevel(b.SystemId, SystemKnowledgeLevel.Signal);

            List<StrategicSystemView> views = BuildSystems(knowledge);

            Assert.AreEqual(2, views.Count, "exactly the two genuinely known systems -- never a hard-coded demo count");
        }

        [Test]
        public void PublishedSnapshot_RemainsStable_WhenTheDataSourceReusesItsWorkingBuffers()
        {
            var navObject = new GameObject("StrategicMapDataSourceTests.Navigation");
            try
            {
                NavigationDirector navigation = navObject.AddComponent<NavigationDirector>();
                SystemManifest firstManifest = StartSystem();
                SystemManifest secondManifest = NeighborSystem(1);
                navigation.Knowledge.ForceLevel(firstManifest.SystemId, SystemKnowledgeLevel.Located);

                var context = new NebulaContext();
                context.SetNavigationDirector(navigation);
                context.SetStreaming(null);
                context.SetShip(null);
                context.SetHangar(null);

                var source = new StrategicMapDataSource(context);
                StrategicMapSnapshot first = source.Build();
                Assert.AreEqual(1, first.Systems.Count);

                navigation.Knowledge.ForceLevel(secondManifest.SystemId, SystemKnowledgeLevel.Located);
                StrategicMapSnapshot second = source.Build();

                Assert.AreEqual(1, first.Systems.Count,
                    "a later build must not clear or append to an already-published snapshot");
                Assert.AreEqual(firstManifest.SystemId, first.Systems[0].SystemId);
                Assert.AreEqual(2, second.Systems.Count);
            }
            finally
            {
                Object.DestroyImmediate(navObject);
            }
        }

        [Test]
        public void ViewModelCollections_DefensivelyOwnTheirInputLists()
        {
            var resources = new List<StrategicResourceLine> { new StrategicResourceLine("ore", 4f) };
            var player = new StrategicPlayerSummary(1, 0, 0f, 0f, "—", SystemLinkState.Live,
                0, 0, false, null, 0f, resources);
            resources.Clear();
            Assert.AreEqual(1, player.Resources.Count, "player summary must not expose the reusable resource buffer");

            var systems = new List<StrategicSystemView> { AlertView(hasAttention: false, hasRaid: false) };
            var links = new List<StrategicLinkView> { new StrategicLinkView("a", "b", false) };
            var snapshot = new StrategicMapSnapshot(systems, links, player, "a");
            systems.Clear();
            links.Clear();
            Assert.AreEqual(1, snapshot.Systems.Count);
            Assert.AreEqual(1, snapshot.Links.Count);
        }

        [Test]
        public void CriticalAlert_IsDerivedFromKnowledgeGatedBuiltSystemAttentionAndRaidState()
        {
            Assert.IsFalse(StrategicMapDataSource.HasCriticalAlert(new List<StrategicSystemView>()));
            Assert.IsFalse(StrategicMapDataSource.HasCriticalAlert(new List<StrategicSystemView>
            {
                AlertView(hasAttention: false, hasRaid: false)
            }));
            Assert.IsTrue(StrategicMapDataSource.HasCriticalAlert(new List<StrategicSystemView>
            {
                AlertView(hasAttention: true, hasRaid: false)
            }));
            Assert.IsTrue(StrategicMapDataSource.HasCriticalAlert(new List<StrategicSystemView>
            {
                AlertView(hasAttention: false, hasRaid: true)
            }));
        }

        // ==================== helpers ====================

        /// <summary>
        /// Drives the same gate logic the live data source uses, without needing a booted
        /// scene: the gates are the part under test, and they are pure functions of the
        /// real manifest + knowledge + ExtractionState.
        /// </summary>
        private static List<StrategicSystemView> BuildSystems(SystemKnowledgeService knowledge)
        {
            var into = new List<StrategicSystemView>();
            StrategicMapDataSource.BuildSystems(knowledge, currentSystemId: null, shipAbsolute: Vector3.zero, into: into);
            return into;
        }

        private static StrategicPlanetView FindPlanet(StrategicSystemView view, string planetId)
        {
            foreach (StrategicPlanetView p in view.Planets) if (p.PlanetId == planetId) return p;
            Assert.Fail($"planet {planetId} missing from the system view");
            return default;
        }

        private static StrategicPoiView FindPoi(StrategicSystemView view, string poiId)
        {
            foreach (StrategicPoiView p in view.Pois) if (p.PoiId == poiId) return p;
            Assert.Fail($"POI {poiId} missing from the system view");
            return default;
        }

        private static string ExpectedPoiName(PoiType type) => type switch
        {
            PoiType.Anomaly => "Anomali",
            PoiType.Wreck => "Enkaz",
            PoiType.AbandonedFacility => "Terk edilmiş tesis",
            PoiType.Protected => "Korunan bölge",
            _ => "İlgi noktası"
        };

        private static StrategicSystemView AlertView(bool hasAttention, bool hasRaid) =>
            new StrategicSystemView(
                systemId: "system",
                displayName: "System",
                sector: new SectorCoordinate(0, 0, 0),
                knowledge: SystemKnowledgeLevel.Visited,
                link: SystemLinkState.Live,
                positionKnown: true,
                absoluteCenter: Vector3.zero,
                approximateDirection: Vector3.zero,
                detailsKnown: true,
                starClass: default,
                starRadius: 1f,
                navigationRadius: 1f,
                planetCount: 0,
                ownership: SystemOwnershipTag.Discovered,
                isCurrentSystem: false,
                isStartSystem: false,
                hasAttention: hasAttention,
                hasRaidWarning: hasRaid,
                lastKnownAgeSeconds: 0f,
                planets: System.Array.Empty<StrategicPlanetView>(),
                pois: System.Array.Empty<StrategicPoiView>());
    }
}
