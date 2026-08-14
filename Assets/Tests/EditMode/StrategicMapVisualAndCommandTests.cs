using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Sectors;
using Nebula.UI.StrategicMap;
using Nebula.UI.StrategicMap.Preview;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Strategic map M4-M6: that every mark on the map resolves to a REAL shipped
    /// asset (never a re-drawn placeholder), that missing art degrades safely, that
    /// the command surface refuses rather than guessing, and that building a snapshot
    /// for a large discovered galaxy stays fast.
    /// </summary>
    public class StrategicMapVisualAndCommandTests
    {
        private const ulong Seed = 4242UL;

        [SetUp]
        public void SetUp()
        {
            StarSystemRegistry.Clear();
            StarSystemRegistry.Initialize(Seed);
            var sector = new SectorCoordinate(0, 0, 0);
            StarSystemRegistry.SetStartSystem(sector, WorldIdCodec.SystemId(sector, 0));
            ExtractionState.Reset((int)Seed);
        }

        [TearDown]
        public void TearDown()
        {
            StrategicMapVisualResolver.ClearCache();
            StarSystemRegistry.Clear();
            ExtractionState.Reset(0);
        }

        // ==================== real assets, never placeholders ====================

        [Test]
        public void EveryStarClass_ResolvesToItsRealShippedSurfaceAndCoronaTexture()
        {
            foreach (StarClass starClass in Enum.GetValues(typeof(StarClass)))
            {
                string surfacePath = NebulaProducedVisualCatalog.StarSurface(starClass);
                string coronaPath = NebulaProducedVisualCatalog.StarCorona(starClass);

                Assert.IsNotEmpty(surfacePath, $"{starClass} must map to a real produced surface texture");
                Assert.IsNotEmpty(coronaPath, $"{starClass} must map to a real produced corona texture");
                StringAssert.StartsWith("VisualTextures/Stars/", surfacePath,
                    "star art must come from the shipped produced texture set, never a re-drawn placeholder");

                Assert.IsNotNull(Resources.Load<Texture2D>(surfacePath), $"{surfacePath} is missing from Resources");
                Assert.IsNotNull(Resources.Load<Texture2D>(coronaPath), $"{coronaPath} is missing from Resources");
            }
        }

        [Test]
        public void EveryPlanetArchetype_ResolvesToARealShippedPrefab()
        {
            foreach (PlanetArchetype archetype in Enum.GetValues(typeof(PlanetArchetype)))
            {
                Assert.IsTrue(NebulaVisualCatalog.TryGetPlanet(archetype, out NebulaVisualDefinition definition),
                    $"{archetype} must have a real catalog entry");
                Assert.IsNotNull(NebulaVisualResolver.Load(definition.ResourcePath),
                    $"{archetype} -> {definition.ResourcePath} must be a real loadable prefab, not a placeholder");
            }
        }

        [Test]
        public void EveryResourceFamilyAndRarity_ResolvesToARealShippedIcon()
        {
            foreach (ResourceFamily family in Enum.GetValues(typeof(ResourceFamily)))
                Assert.IsNotNull(Resources.Load<Texture2D>(NebulaIconCatalog.ResourcePath(family)),
                    $"{family} icon missing");
            foreach (Rarity rarity in Enum.GetValues(typeof(Rarity)))
                Assert.IsNotNull(Resources.Load<Texture2D>(NebulaIconCatalog.RarityPath(rarity)),
                    $"{rarity} icon missing");
        }

        [Test]
        public void EveryStatusMarkTheMapDraws_ResolvesToARealShippedIcon()
        {
            foreach (EnvironmentClass environment in Enum.GetValues(typeof(EnvironmentClass)))
                Assert.IsNotNull(Resources.Load<Texture2D>(NebulaProducedVisualCatalog.Environment(environment)),
                    $"{environment} icon missing");

            foreach (SystemLinkState link in Enum.GetValues(typeof(SystemLinkState)))
                Assert.IsNotNull(Resources.Load<Texture2D>(NebulaProducedVisualCatalog.Link(link)),
                    $"{link} icon missing");

            foreach (SystemKnowledgeLevel level in Enum.GetValues(typeof(SystemKnowledgeLevel)))
            {
                if (level == SystemKnowledgeLevel.Unknown) continue;
                Assert.IsNotNull(Resources.Load<Texture2D>(NebulaProducedVisualCatalog.Knowledge(level)),
                    $"{level} icon missing");
            }

            foreach (DominanceState state in new[] { DominanceState.Developing, DominanceState.Controlled,
                                                     DominanceState.Contested, DominanceState.Lost })
                Assert.IsNotNull(Resources.Load<Texture2D>(NebulaProducedVisualCatalog.Dominance(state)),
                    $"{state} icon missing");
        }

        [Test]
        public void AMissingVisual_DegradesToAControlledFallback_AndNeverThrows()
        {
            // An unknown content id must simply yield null so the caller draws its fallback.
            Assert.DoesNotThrow(() => StrategicMapVisualResolver.StructureThumbnail("structure.does.not.exist"));
            Assert.IsNull(StrategicMapVisualResolver.StructureThumbnail("structure.does.not.exist"));
            Assert.IsNull(StrategicMapVisualResolver.StructureThumbnail(null));
            Assert.IsNull(StrategicMapVisualResolver.StructureThumbnail(""));
            Assert.DoesNotThrow(() => StrategicMapVisualResolver.Item("item.does.not.exist"));
        }

        [Test]
        public void VisualIdsAreUnique_SoNoTwoMapMarksCanCollideOnTheSameAsset()
        {
            var seen = new HashSet<string>();
            foreach (NebulaVisualDefinition definition in NebulaVisualCatalog.All)
                Assert.IsTrue(seen.Add(definition.VisualId), $"duplicate visual id {definition.VisualId}");
        }

        [Test]
        public void EveryStructureVisualIdTheDataSourceEmits_ExistsInTheRealCatalog()
        {
            // These are exactly the ids StrategicMapDataSource writes into its slot views.
            string[] emitted =
            {
                "structure.drill.t1", "structure.drill.t2",
                "structure.command.t1", "structure.command.t2",
                "structure.defense.laser.t1", "structure.defense.laser.t2",
                "structure.defense.plasma.t1", "structure.defense.plasma.t2",
                "structure.support.sensor_array.t1", "structure.support.sensor_array.t2",
                "structure.support.maintenance_platform.t1", "structure.support.maintenance_platform.t2"
            };
            var known = new HashSet<string>();
            foreach (NebulaVisualDefinition d in NebulaVisualCatalog.All) known.Add(d.VisualId);

            foreach (string id in emitted)
                Assert.IsTrue(known.Contains(id), $"the map emits '{id}' but the real visual catalog has no such entry");
        }

        // ==================== command surface ====================

        private static StrategicSystemView System(bool positionKnown, bool detailsKnown, SystemLinkState link) =>
            new StrategicSystemView("sys-x", "Sistem X", new SectorCoordinate(0, 0, 0),
                detailsKnown ? SystemKnowledgeLevel.Visited : SystemKnowledgeLevel.Signal, link,
                positionKnown, Vector3.zero, Vector3.forward, detailsKnown, default, 1f, 1f, 0,
                SystemOwnershipTag.Discovered, false, false, false, false, 0f, null, null);

        [Test]
        public void AWaypointToASystemWithNoKnownPosition_IsRefusedWithATypedReason()
        {
            var commands = new StrategicMapCommands(null);
            StrategicCommandResult result = commands.SetWaypoint(System(positionKnown: false, detailsKnown: false, SystemLinkState.Live));

            Assert.AreNotEqual(StrategicCommandResult.Accepted, result);
            Assert.IsNotEmpty(StrategicMapCommands.Describe(result), "a refusal must always have player-facing text");
        }

        [Test]
        public void AnEmptyTarget_IsRefused_NeverSilentlyIgnored()
        {
            var commands = new StrategicMapCommands(null);
            Assert.AreEqual(StrategicCommandResult.Unavailable, commands.SetWaypoint(default));
        }

        [Test]
        public void ADisconnectedSystem_IsNeverOfferedARemoteCommand()
        {
            var commands = new StrategicMapCommands(null);

            Assert.IsFalse(commands.CanIssueRemoteCommand(System(true, true, SystemLinkState.Disconnected)));
            Assert.AreEqual(StrategicCommandResult.NoCommunication,
                commands.CheckRemoteCommand(System(true, true, SystemLinkState.Disconnected)));

            Assert.IsTrue(commands.CanIssueRemoteCommand(System(true, true, SystemLinkState.Live)));
            Assert.AreEqual(StrategicCommandResult.Accepted, commands.CheckRemoteCommand(System(true, true, SystemLinkState.Live)));
        }

        [Test]
        public void LastKnownSystem_IsReadOnlyAndNeverOfferedANewRemoteCommand()
        {
            var commands = new StrategicMapCommands(null);
            StrategicSystemView stale = System(true, true, SystemLinkState.LastKnown);

            Assert.IsFalse(commands.CanIssueRemoteCommand(stale),
                "cached telemetry may be inspected but must never authorize a new remote order");
            Assert.AreEqual(StrategicCommandResult.NoCommunication, commands.CheckRemoteCommand(stale));
        }

        [Test]
        public void AnUnvisitedSystem_IsNeverOfferedARemoteCommand()
        {
            var commands = new StrategicMapCommands(null);
            Assert.IsFalse(commands.CanIssueRemoteCommand(System(true, false, SystemLinkState.Live)));
            Assert.AreEqual(StrategicCommandResult.NotLocated, commands.CheckRemoteCommand(System(true, false, SystemLinkState.Live)));
        }

        [Test]
        public void WaypointCommands_CommitAndClearOnlyThroughTheNavigationAuthority()
        {
            var go = new GameObject("StrategicMapCommands.Navigation");
            try
            {
                NavigationDirector navigation = go.AddComponent<NavigationDirector>();
                navigation.Knowledge.ForceLevel("sys-x", SystemKnowledgeLevel.Located);
                var context = new NebulaContext();
                context.SetNavigationDirector(navigation);
                var commands = new StrategicMapCommands(context);

                Assert.AreEqual(StrategicCommandResult.Accepted,
                    commands.SetWaypoint(System(positionKnown: true, detailsKnown: false, SystemLinkState.Live)));
                Assert.AreEqual("sys-x", navigation.SelectedRouteSystemId);
                Assert.IsTrue(commands.HasWaypoint);

                Assert.AreEqual(StrategicCommandResult.Accepted, commands.ClearWaypoint());
                Assert.IsNull(navigation.SelectedRouteSystemId);
                Assert.IsFalse(commands.HasWaypoint);

                // A stale/inconsistent UI snapshot cannot override the authority's current
                // knowledge. Even if its marker claims a position, NavigationDirector wins.
                navigation.Knowledge.ForceLevel("sys-x", SystemKnowledgeLevel.Signal);
                Assert.AreEqual(StrategicCommandResult.RejectedByNavigation,
                    commands.SetWaypoint(System(positionKnown: true, detailsKnown: false, SystemLinkState.Live)));
                Assert.AreEqual("Rota için sistemin önce konumlandırılması gerekli", commands.LastRejectReason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AnalysisCommands_ReturnTypedGateResultsInsteadOfFailingSilently()
        {
            var go = new GameObject("StrategicMapCommands.Analysis");
            try
            {
                NavigationDirector navigation = go.AddComponent<NavigationDirector>();
                navigation.Knowledge.ForceLevel("sys-x", SystemKnowledgeLevel.Signal);
                var context = new NebulaContext();
                context.SetNavigationDirector(navigation);
                var commands = new StrategicMapCommands(context);

                Assert.AreEqual(StrategicCommandResult.RejectedByNavigation,
                    commands.StartAnalysis(System(positionKnown: false, detailsKnown: false, SystemLinkState.Live)));
                Assert.AreEqual("Sinyal analizi için Tarayıcı T1 gerekli", commands.LastRejectReason);
                Assert.IsFalse(commands.HasActiveAnalysis);
                Assert.AreEqual(StrategicCommandResult.Unavailable, commands.CancelAnalysis());

                Assert.AreEqual(StrategicCommandResult.NotSignal,
                    commands.StartAnalysis(System(positionKnown: true, detailsKnown: true, SystemLinkState.Live)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void EveryCommandResult_HasPlayerFacingTurkishText_AndNeverLeaksAnEnumName()
        {
            foreach (StrategicCommandResult result in Enum.GetValues(typeof(StrategicCommandResult)))
            {
                string text = StrategicMapCommands.Describe(result);
                if (result == StrategicCommandResult.Accepted) { Assert.IsEmpty(text); continue; }
                Assert.IsNotEmpty(text, $"{result} needs player-facing text");
                StringAssert.DoesNotContain(result.ToString(), text, "a refusal must never show the raw enum name");
            }
        }

        // ==================== performance ====================

        [Test]
        public void BuildingASnapshotForOver32KnownSystems_StaysWellInsideAFrameBudget()
        {
            var knowledge = new SystemKnowledgeService();
            int known = 0;
            // Visits the sectors the galaxy actually occupies rather than stepping (sx, 0, 0)
            // until the count is satisfied. That walk could not terminate: sectors are filled
            // from GalaxyStarField, which is finite and strung along spiral arms across every
            // axis, so a straight line at y=z=0 leaves the stars almost at once and every later
            // sector is empty -- `known` stops rising while the loop keeps resolving sectors,
            // each one permanently cached by SectorManifestRepository. This test died on
            // OutOfMemoryException, and the identical walk in Vs017aDeterminismScaleTests took
            // the whole EditMode suite with it.
            foreach (SectorCoordinate sector in OccupiedSectors())
            {
                SectorManifest sm = StarSystemRegistry.Manifests.Resolve(sector);
                foreach (SystemManifest system in sm.Systems)
                {
                    knowledge.ForceLevel(system.SystemId, SystemKnowledgeLevel.Visited);
                    known++;
                }
                if (known >= 40) break;
            }
            Assert.GreaterOrEqual(known, 32, "the scale test needs a genuinely large known galaxy");

            var into = new List<StrategicSystemView>();
            // Warm up first: the first pass also materialises manifests and profiles.
            StrategicMapDataSource.BuildSystems(knowledge, null, Vector3.zero, into);

            var sw = Stopwatch.StartNew();
            const int Rebuilds = 20;
            for (int i = 0; i < Rebuilds; i++)
            {
                into.Clear();
                StrategicMapDataSource.BuildSystems(knowledge, null, Vector3.zero, into);
            }
            sw.Stop();

            double perBuildMs = sw.Elapsed.TotalMilliseconds / Rebuilds;
            TestContext.WriteLine($"[strategic map] {known} systems, snapshot rebuild = {perBuildMs:0.00} ms");

            Assert.AreEqual(known, into.Count, "every known system must appear in the snapshot");
            // The map rebuilds twice a second, so even a generous budget leaves 60 FPS untouched.
            Assert.Less(perBuildMs, 16.0, $"a {known}-system rebuild took {perBuildMs:0.00} ms");
        }

        [Test]
        public void RepeatedSnapshotBuilds_ReturnStableResults_NoDuplicateOrDriftingNodes()
        {
            var knowledge = new SystemKnowledgeService();
            SectorManifest sm = StarSystemRegistry.Manifests.Resolve(new SectorCoordinate(0, 0, 0));
            foreach (SystemManifest system in sm.Systems) knowledge.ForceLevel(system.SystemId, SystemKnowledgeLevel.Visited);

            var first = new List<StrategicSystemView>();
            var second = new List<StrategicSystemView>();
            StrategicMapDataSource.BuildSystems(knowledge, null, Vector3.zero, first);
            StrategicMapDataSource.BuildSystems(knowledge, null, Vector3.zero, second);

            Assert.AreEqual(first.Count, second.Count);
            var ids = new HashSet<string>();
            for (int i = 0; i < first.Count; i++)
            {
                Assert.IsTrue(ids.Add(first[i].SystemId), $"duplicate node {first[i].SystemId}");
                Assert.AreEqual(first[i].SystemId, second[i].SystemId, "node order must not drift between rebuilds");
                Assert.AreEqual(first[i].Planets.Count, second[i].Planets.Count);
            }
        }

        // ==================== card grammar ====================

        [Test]
        public void EveryCardLabelIsTurkishPlayerText_NeverAStableIdOrEnumName()
        {
            // A label may legitimately match an enum name when the Turkish word is the same
            // ("Metal"). What must never appear is stable-id syntax or an empty label.
            void AssertPlayerText(string text, string what)
            {
                Assert.IsNotEmpty(text, $"{what} needs player-facing text");
                StringAssert.DoesNotContain(".", text, $"{what} leaks stable-id syntax");
                StringAssert.DoesNotContain("_", text, $"{what} leaks stable-id syntax");
            }
            foreach (DominanceState s in Enum.GetValues(typeof(DominanceState))) AssertPlayerText(StrategicObjectCard.DominanceName(s), $"{s}");
            foreach (SystemLinkState s in Enum.GetValues(typeof(SystemLinkState))) AssertPlayerText(StrategicObjectCard.LinkName(s), $"{s}");
            foreach (SystemKnowledgeLevel s in Enum.GetValues(typeof(SystemKnowledgeLevel))) AssertPlayerText(StrategicObjectCard.KnowledgeName(s), $"{s}");
            foreach (SystemOwnershipTag s in Enum.GetValues(typeof(SystemOwnershipTag))) AssertPlayerText(StrategicObjectCard.OwnershipName(s), $"{s}");
            foreach (StarClass s in Enum.GetValues(typeof(StarClass))) AssertPlayerText(StrategicObjectCard.StarName(s), $"{s}");
            foreach (ResourceFamily s in Enum.GetValues(typeof(ResourceFamily))) AssertPlayerText(StrategicObjectCard.ResourceName(s), $"{s}");
            foreach (Rarity s in Enum.GetValues(typeof(Rarity))) AssertPlayerText(StrategicObjectCard.RarityName(s), $"{s}");
            foreach (EnvironmentClass s in Enum.GetValues(typeof(EnvironmentClass))) AssertPlayerText(StrategicObjectCard.EnvironmentName(s), $"{s}");
        }

        [Test]
        public void CardLabelsAreDistinctPerState_SoTwoDifferentStatesNeverReadTheSame()
        {
            void AssertDistinct<T>(Func<T, string> name, string what)
            {
                var seen = new HashSet<string>();
                foreach (T value in Enum.GetValues(typeof(T)))
                    Assert.IsTrue(seen.Add(name(value)), $"{what}: two states share the label '{name(value)}'");
            }

            AssertDistinct<DominanceState>(StrategicObjectCard.DominanceName, "dominance");
            AssertDistinct<SystemLinkState>(StrategicObjectCard.LinkName, "link");
            AssertDistinct<SystemKnowledgeLevel>(StrategicObjectCard.KnowledgeName, "knowledge");
            AssertDistinct<SystemOwnershipTag>(StrategicObjectCard.OwnershipName, "ownership");
            AssertDistinct<StarClass>(StrategicObjectCard.StarName, "star class");
            AssertDistinct<ResourceFamily>(StrategicObjectCard.ResourceName, "resource");
            AssertDistinct<Rarity>(StrategicObjectCard.RarityName, "rarity");
            AssertDistinct<EnvironmentClass>(StrategicObjectCard.EnvironmentName, "environment");
        }

        [Test]
        public void StaleDataIsAlwaysTimestamped_SoLastKnownIsNeverMistakenForLive()
        {
            StringAssert.Contains("sn", StrategicSystemStageView.FormatAge(12f));
            StringAssert.Contains("dk", StrategicSystemStageView.FormatAge(300f));
            StringAssert.Contains("sa", StrategicSystemStageView.FormatAge(7200f));
        }

        [Test]
        public void DisplayLayers_DoNotExposeAPlanetVisibilityToggle()
        {
            string[] names = Enum.GetNames(typeof(StrategicMapDisplayLayer));
            CollectionAssert.DoesNotContain(names, "Planet");
            CollectionAssert.DoesNotContain(names, "Planets");
        }

        [Test]
        public void UniverseFallback_PreservesSystemKnowledgeGates()
        {
            StrategicMapUniverseSnapshot universe = StrategicMapStaticPreviewSource.Load();
            StrategicMapUniverseSnapshot.SystemRow row = universe.systems[0];
            Assert.Greater(row.planetTo - row.planetFrom, 0);
            string planetId = universe.planets[row.planetFrom].id;

            row.knowledge = (int)SystemKnowledgeLevel.Located;
            Assert.IsTrue(StrategicMapUniverseViews.TrySystem(universe, row.id, out StrategicSystemView located));
            Assert.IsTrue(located.PositionKnown);
            Assert.IsFalse(located.DetailsKnown);
            Assert.AreEqual(0, located.Planets.Count);
            Assert.IsFalse(StrategicMapUniverseViews.TryPlanet(universe, planetId, out _, out _),
                "Located knowledge must not expose planet detail through the renderer fallback");

            row.knowledge = (int)SystemKnowledgeLevel.Visited;
            Assert.IsTrue(StrategicMapUniverseViews.TryPlanet(universe, planetId,
                out StrategicPlanetView planet, out StrategicSystemView visited));
            Assert.AreEqual(planetId, planet.PlanetId);
            Assert.IsTrue(visited.DetailsKnown);
        }

        /// <summary>
        /// The sectors the galaxy actually places stars in, start-sector first and then in a
        /// fixed lexicographic order, so a walk over them is finite and identical on every run.
        /// Reading the star field is what bounds it -- resolving a sector caches it forever, so
        /// a walk that cannot tell an empty sector from an unreached one cannot be bounded at all.
        /// </summary>
        private static IEnumerable<SectorCoordinate> OccupiedSectors()
        {
            var start = new SectorCoordinate(0, 0, 0);
            var seen = new HashSet<SectorCoordinate>();
            var occupied = new List<SectorCoordinate>();
            foreach (GalaxyStarField.Placement p in GalaxyStarField.All(Seed))
            {
                SectorCoordinate s = SectorCoordinate.FromAbsolutePosition(p.AbsolutePosition);
                if (seen.Add(s)) occupied.Add(s);
            }
            occupied.Sort((a, b) =>
            {
                if (a.Equals(start) != b.Equals(start)) return a.Equals(start) ? -1 : 1;
                if (a.X != b.X) return a.X.CompareTo(b.X);
                if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
                return a.Z.CompareTo(b.Z);
            });
            return occupied;
        }
    }
}
