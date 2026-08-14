using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Telemetry;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// VS-017A.2 §D PlayMode coverage: the real new-campaign/save/load/restart
    /// campaign-identity + telemetry lifecycle, the real second-system Asset
    /// event (with its Anomaly-negative control), and the real physical Drill
    /// T1→T2 upgrade across save/load, system demotion, remote completion and
    /// promotion back.
    /// </summary>
    public class Vs017a2PlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;
        private readonly Dictionary<string, string> slot1Backup = new Dictionary<string, string>();

        private static IEnumerable<string> Slot1Files()
        {
            string main = SaveService.SlotFile(GameplaySlot);
            string dir = Path.GetDirectoryName(main);
            yield return main;
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak1.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak2.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak3.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.tmp.json");
        }

        [SetUp]
        public void SetUp()
        {
            slot1Backup.Clear();
            foreach (string f in Slot1Files()) slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
            TelemetryService.InvalidateFunnelIndexCache();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            TelemetryService.InvalidateFunnelIndexCache();
            foreach (var kv in slot1Backup)
            {
                if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                else File.WriteAllText(kv.Key, kv.Value);
            }
        }

        private static IEnumerator LoadSceneFresh()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it -- explicitly, through the same idempotent
            // entry the menu\'s New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        private static NebulaContext Context() => Object.FindObjectOfType<SolarSystemBootstrap>().Context;

        // ==================== canonical world fixtures ====================

        /// <summary>
        /// The sectors this campaign's galaxy actually places stars in, start sector first and
        /// then lexicographically — a fixed, finite order, so every system these tests pick is
        /// the same one on every run and every platform.
        /// <para>
        /// Reading <see cref="GalaxyStarField"/> is what makes the walk both bounded and honest.
        /// A sector's content comes from that field: 4 galaxies x 4 arms x ~6-10 stars, 128 in
        /// total, strung along spiral arms spanning millions of units on every axis against
        /// 2,000,000-unit sectors. Almost every sector is therefore empty, and
        /// <see cref="SectorManifestRepository"/> caches every sector it resolves forever — so a
        /// walk that cannot tell an empty sector from an unreached one cannot be bounded at all.
        /// Only the sectors that genuinely hold a star are ever visited here.
        /// </para>
        /// </summary>
        private static List<SectorCoordinate> OccupiedSectors(SectorCoordinate startSector)
        {
            var seen = new HashSet<SectorCoordinate>();
            var occupied = new List<SectorCoordinate>();
            foreach (GalaxyStarField.Placement placement in GalaxyStarField.All(StarSystemRegistry.Manifests.Seed))
            {
                SectorCoordinate sector = SectorCoordinate.FromAbsolutePosition(placement.AbsolutePosition);
                if (seen.Add(sector)) occupied.Add(sector);
            }
            occupied.Sort((a, b) =>
            {
                if (a.Equals(startSector) != b.Equals(startSector)) return a.Equals(startSector) ? -1 : 1;
                if (a.X != b.X) return a.X.CompareTo(b.X);
                if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
                return a.Z.CompareTo(b.Z);
            });
            return occupied;
        }

        /// <summary>
        /// The first real system in that canonical order which is not the campaign's own start
        /// system, resolved through the live registry. Where the start sector genuinely holds a
        /// second star this IS that star; where it does not, it is the first system of the next
        /// sector the galaxy fills — none of the tests below need the two to share a sector,
        /// only to be genuinely different systems.
        /// <para>
        /// Never an id built by index. <c>WorldIdCodec.SystemId(startSector, 1)</c> names slot 1
        /// of the start sector, which only exists when the galaxy happened to drop a second star
        /// inside it; measured over seeds 0..399 sector.0.0.0 holds one system on 301 of them, so
        /// on most campaigns <c>ResolveSystem</c> returned null and every use of it below threw a
        /// NullReferenceException.
        /// </para>
        /// </summary>
        private static SystemManifest ForeignSystem(NebulaContext context)
        {
            foreach (SectorCoordinate sector in OccupiedSectors(context.StartSector))
                foreach (SystemManifest system in StarSystemRegistry.Manifests.Resolve(sector).Systems)
                {
                    if (system.SystemId == context.StartSystemId) continue;
                    Assert.Greater(system.Planets.Count, 0,
                        $"{system.SystemId}: a second system with no planets cannot carry the drill/ownership state these tests put on it");
                    return system;
                }

            Assert.Fail("this campaign's galaxy placed no system at all outside its start system");
            return null;
        }

        // ==================== §A2/§A3: real campaign identity + new_game lifecycle ====================

        [UnityTest]
        public IEnumerator NewCampaign_BootMintsARealCampaignId_AndTelemetryConsumesExactlyThatOne()
        {
            yield return LoadSceneFresh();
            Context();

            Assert.IsTrue(CampaignIdentity.IsRealId(CampaignIdentity.Current),
                "booting a fresh session must mint a real unique campaign identity -- never the old hardcoded 'campaign-1'");
            Assert.AreEqual(CampaignIdentity.Current, TelemetryService.CampaignId,
                "telemetry must consume the gameplay/save-owned campaign identity, never mint one of its own");
        }

        [UnityTest]
        public IEnumerator RealSaveThenLoad_PreservesTheCampaignId_AndNeverProducesASecondNewGame()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            string campaignBeforeSave = CampaignIdentity.Current;

            context.ExtractionDirector.SaveNow(null);
            yield return null;
            context.ExtractionDirector.LoadNow();
            yield return null;

            Assert.AreEqual(campaignBeforeSave, CampaignIdentity.Current,
                "a real save->load round trip must preserve the campaign identity exactly -- a load is not a new campaign");
            Assert.AreEqual(campaignBeforeSave, TelemetryService.CampaignId, "telemetry must have followed the adopted identity");

            int newGameCount = 0;
            foreach (TelemetryEventRecord e in TelemetryService.SessionEventsSnapshot)
                if (e.eventId == TelemetryEvents.NewGame) newGameCount++;
            Assert.AreEqual(1, newGameCount, "loading must never produce a second new_game for the same campaign");
        }

        [UnityTest]
        public IEnumerator RestartCampaign_MintsAGenuinelyDifferentCampaignId()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            string before = CampaignIdentity.Current;

            adapter.RestartCampaign();
            yield return null;
            yield return null;

            Assert.IsTrue(CampaignIdentity.IsRealId(CampaignIdentity.Current));
            Assert.AreNotEqual(before, CampaignIdentity.Current, "a RestartCampaign is a genuinely new campaign and must get its own identity");
            Assert.AreEqual(CampaignIdentity.Current, TelemetryService.CampaignId, "telemetry must be re-scoped onto the new campaign, never carry the old id forward");
        }

        // ==================== §A1: second system Asset, with the Anomaly-negative control ====================

        [UnityTest]
        public IEnumerator SecondSystemAsset_FiresOnlyWhenASecondDistinctSystemGenuinelyHasAControlledPlanet()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            TelemetryGameplayBridge bridge = context.TelemetryBridge;
            Assert.IsNotNull(bridge);

            // One Controlled planet in the START system only -> that is the FIRST asset system,
            // so the second-system event must NOT have fired.
            string startSystemId = context.StartSystemId;
            PlanetManifest startPlanet = context.StartSystem.Planets[0];
            ExtractionState.GetDominance(startPlanet.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);
            yield return null;
            Assert.AreEqual(0, CountRecorded(TelemetryEvents.SecondSystemAsset),
                "one asset system is not two -- the second-system event must not have fired yet");

            // A second, genuinely different system now gets a Controlled planet, recorded in its
            // own persisted delta exactly as a non-Full system's ownership always is. It is the
            // galaxy's own next system, not slot 1 of the start sector: what this test measures
            // is "a second DISTINCT system", never "a second system in the same sector".
            SystemManifest neighbor = ForeignSystem(context);
            Assert.AreNotEqual(startSystemId, neighbor.SystemId,
                "the second asset system must genuinely be a different system from the start system");
            var delta = new SystemDelta { systemId = neighbor.SystemId };
            delta.dominance.Add(new DominanceDto { worldId = neighbor.Planets[0].PlanetId, state = (int)DominanceState.Controlled });
            StarSystemRegistry.Deltas.Set(delta);

            // Fire the real authoritative signal the bridge listens to.
            PlanetManifest secondStartPlanet = context.StartSystem.Planets[1];
            ExtractionState.GetDominance(secondStartPlanet.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);
            RaisePlanetFirstControlled(neighbor.Planets[0].PlanetId);
            yield return null;

            Assert.AreEqual(1, CountRecorded(TelemetryEvents.SecondSystemAsset),
                "a second DISTINCT system reaching a real Controlled/Asset state must fire the event exactly once");
        }

        [UnityTest]
        public IEnumerator AnomalyCompletions_NeverProduceTheSecondSystemAssetEvent()
        {
            yield return LoadSceneFresh();
            Context();

            // Two distinct Anomaly POI XP awards -- the exact signal the OLD, mis-measured
            // "second_entity" fired on. It must now produce nothing at all.
            Progression.ProgressionService.Award("poi.anomaly.xp.poi-alpha", 100);
            Progression.ProgressionService.Award("poi.anomaly.xp.poi-beta", 100);
            yield return null;

            Assert.AreEqual(0, CountRecorded(TelemetryEvents.SecondSystemAsset),
                "a second Anomaly/POI is NOT a second system Asset -- that was the whole mis-measurement being fixed");
        }

        // ==================== §A4: load/restart-safe second drill ====================

        [UnityTest]
        public IEnumerator SecondDrill_CountsAFirstDrillRestoredFromADelta_WhenAGenuineSecondOneCompletes()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();

            // The FIRST drill exists only in another system's persisted delta -- exactly the
            // "restored from save, never observed live by this bridge" case the old RAM-only
            // HashSet got wrong. "Another system" is the galaxy's own next real system; the
            // count second_drill is taken over is distinct PLANETS, so nothing here needs the
            // two systems to share a sector.
            SystemManifest neighbor = ForeignSystem(context);
            Assert.AreNotEqual(context.StartSystemId, neighbor.SystemId,
                "the restored-from-save drill must live in a genuinely different system");
            var delta = new SystemDelta { systemId = neighbor.SystemId };
            delta.drills.Add(new DrillDto { worldId = neighbor.Planets[0].PlanetId, nodeId = "drill", state = (int)DrillLifecycle.Active });
            StarSystemRegistry.Deltas.Set(delta);

            // Now a genuinely NEW second drill completes live in the start system.
            PlanetManifest planetManifest = context.StartSystem.Planets[0];
            PlanetIdentity planet = ExtractionState.FindPlanet(planetManifest.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;
            var runtime = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, 0f, 0f);
            DrillStructure.Restore(planet, profile, runtime, DrillStructure.BuildSource.Kit, 0);
            yield return null;

            Progression.ProgressionService.Award("drill:first:" + planetManifest.PlanetId, 100);
            yield return null;

            Assert.AreEqual(1, CountRecorded(TelemetryEvents.SecondDrill),
                "two distinct planets genuinely having a built drill is a real second_drill, even when the first one came from a save");
        }

        [UnityTest]
        public IEnumerator RestoringTheSameDrillRepeatedly_IsNeverCountedAsASecondDrill()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();

            PlanetManifest planetManifest = context.StartSystem.Planets[0];
            PlanetIdentity planet = ExtractionState.FindPlanet(planetManifest.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;
            var runtime = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, 0f, 0f);
            DrillStructure.Restore(planet, profile, runtime, DrillStructure.BuildSource.Kit, 0);
            yield return null;

            for (int i = 0; i < 3; i++)
            {
                Progression.ProgressionService.Award($"drill:first:{planetManifest.PlanetId}:retry{i}", 10);
                yield return null;
            }

            Assert.AreEqual(0, CountRecorded(TelemetryEvents.SecondDrill),
                "re-materializing the SAME single drill can never be a second drill -- the count is over distinct planets");
        }

        // ==================== §B: the real physical Drill T1→T2 upgrade ====================

        /// <summary>Builds a real Active T1 drill on a start-system planet, with the ship stocked and gated for the T2 upgrade.</summary>
        private static DrillStructure PrepareUpgradableDrill(NebulaContext context, out PlanetProfile profile, out LogisticsCargo cargo)
        {
            PlanetManifest planetManifest = context.StartSystem.Planets[0];
            PlanetIdentity planet = ExtractionState.FindPlanet(planetManifest.PlanetId);
            profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;

            cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 10000f));
            cargo.Add(ProcessingCatalog.BasicPartId, 500f);
            cargo.Add(DrillUpgradeCatalog.MaterialIdFor(profile.Family), 500f);

            var runtime = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, 25f, 0f);
            return DrillStructure.Restore(planet, profile, runtime, DrillStructure.BuildSource.Kit, 0);
        }

        [UnityTest]
        public IEnumerator DrillT2_RealUpgrade_HaltsProduction_ThenCompletesAtTierTwoWithStockPreserved()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            DrillStructure drill = PrepareUpgradableDrill(context, out PlanetProfile profile, out LogisticsCargo cargo);
            yield return null;

            float partsBefore = cargo.Get(ProcessingCatalog.BasicPartId);
            string materialId = DrillUpgradeCatalog.MaterialIdFor(profile.Family);
            float materialBefore = cargo.Get(materialId);
            float storedBefore = drill.Runtime.StoredAmount;

            Assert.AreEqual(DrillUpgradeRejectReason.None, drill.StartUpgrade(2, cargo));
            Assert.IsNotNull(drill.ActiveUpgradeJob);
            Assert.IsFalse(drill.Runtime.IsProducing, "an upgrading drill must produce nothing at all");

            Time.timeScale = 40f;
            float realStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - realStart < 12f && drill.Runtime.Tier < 2) yield return null;
            Time.timeScale = 1f;

            Assert.AreEqual(2, drill.Runtime.Tier, "the real 60s upgrade must have completed through the live drill's own Update tick");
            Assert.IsNull(drill.ActiveUpgradeJob);
            Assert.AreEqual(partsBefore - DrillUpgradeCatalog.PartsCost, cargo.Get(ProcessingCatalog.BasicPartId), 0.01f, "the parts cost is consumed exactly once");
            Assert.AreEqual(materialBefore - DrillUpgradeCatalog.MaterialCost, cargo.Get(materialId), 0.01f, "the material cost is consumed exactly once");
            Assert.GreaterOrEqual(drill.Runtime.StoredAmount, storedBefore, "the drill's existing stock must survive the upgrade -- never wiped");
            Assert.AreEqual(ExtractionCatalog.T2StorageCapacity(profile.Rarity), drill.Runtime.StorageCapacity, 0.01f);
        }

        [UnityTest]
        public IEnumerator DrillT2_CancelMidUpgrade_RefundsSeventyFivePercent_AndLeavesTheDrillAtTierOne()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            DrillStructure drill = PrepareUpgradableDrill(context, out PlanetProfile profile, out LogisticsCargo cargo);
            yield return null;

            string materialId = DrillUpgradeCatalog.MaterialIdFor(profile.Family);
            float partsBefore = cargo.Get(ProcessingCatalog.BasicPartId);
            float materialBefore = cargo.Get(materialId);

            Assert.AreEqual(DrillUpgradeRejectReason.None, drill.StartUpgrade(2, cargo));
            yield return null;
            drill.CancelUpgrade(cargo);

            // floor(35 * 0.75) = 26 parts back, floor(10 * 0.75) = 7 material back.
            Assert.AreEqual(partsBefore - 35f + 26f, cargo.Get(ProcessingCatalog.BasicPartId), 0.01f);
            Assert.AreEqual(materialBefore - 10f + 7f, cargo.Get(materialId), 0.01f);
            Assert.AreEqual(1, drill.Runtime.Tier, "a cancelled upgrade must leave the drill exactly where it was");
            Assert.IsNull(drill.ActiveUpgradeJob);
        }

        [UnityTest]
        public IEnumerator DrillT2_StartedThenSystemLeft_CompletesRemotelyAndSurvivesPromotionBack()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            string startSystemId = context.StartSystemId;
            SystemManifest startManifest = StarSystemRegistry.Manifests.ResolveSystem(startSystemId);
            DrillStructure drill = PrepareUpgradableDrill(context, out PlanetProfile profile, out LogisticsCargo cargo);
            yield return null;

            string drillWorldId = drill.WorldId;
            string drillNodeId = drill.NodeId;
            float storedBefore = drill.Runtime.StoredAmount;
            Assert.AreEqual(DrillUpgradeRejectReason.None, drill.StartUpgrade(2, cargo));

            // Partially advance the upgrade live, then physically leave the system.
            Time.timeScale = 20f;
            float partialStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - partialStart < 1f) yield return null;
            Time.timeScale = 1f;
            Assert.AreEqual(1, drill.Runtime.Tier, "sanity: the upgrade must still be in flight at this point");

            // The real demotion sequence this project uses everywhere: capture the leaving
            // system's mutable delta, register it, then boot the next system Full.
            SystemDelta demoted = context.Streaming.CaptureCurrentFullDelta();
            Assert.IsNotNull(demoted);
            StarSystemRegistry.Deltas.Set(demoted);

            // Somewhere genuinely else -- the galaxy's own next real system. What has to be true
            // for a demotion is that the ship physically leaves this system for another one, not
            // that the other one shares its sector.
            SystemManifest neighborManifest = ForeignSystem(context);
            Assert.AreNotEqual(startSystemId, neighborManifest.SystemId,
                "leaving the system needs a genuinely different system to arrive in");
            context.Ship.transform.position = neighborManifest.AbsoluteCenter - FloatingOrigin.TotalOffset;
            context.Streaming.ResetAndBootFull(neighborManifest);
            yield return null;

            DrillDto demotedDrill = demoted.drills.Find(d => d.worldId == drillWorldId);
            Assert.IsNotNull(demotedDrill);
            Assert.IsTrue(demotedDrill.upgradeActive, "the in-flight upgrade must be carried out with the demoted system");
            Assert.Greater(demotedDrill.upgradeRemainingCost.Count, 0, "the still-owed cost must be carried so a load can re-reserve exactly it");

            // Let the real 1 Hz remote scheduler finish the upgrade while the system is away.
            Time.timeScale = 40f;
            float remoteStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - remoteStart < 15f)
            {
                yield return null;
                DrillDto d = StarSystemRegistry.Deltas.TryGet(startSystemId)?.drills.Find(x => x.worldId == drillWorldId);
                if (d != null && d.tier >= 2) break;
            }
            Time.timeScale = 1f;

            DrillDto remoteFinished = StarSystemRegistry.Deltas.TryGet(startSystemId).drills.Find(d => d.worldId == drillWorldId);
            Assert.AreEqual(2, remoteFinished.tier, "the upgrade must genuinely finish on the remote 1 Hz path while the ship is elsewhere");
            Assert.IsFalse(remoteFinished.upgradeActive);

            // Come back: the same drill, at T2, with its identity and stock intact.
            context.Ship.transform.position = startManifest.AbsoluteCenter - FloatingOrigin.TotalOffset;
            context.Streaming.ResetAndBootFull(startManifest);
            yield return null;
            yield return null;

            var restored = new List<DrillStructure>();
            foreach (DrillStructure d in ExtractionState.Drills) if (d != null && d.WorldId == drillWorldId) restored.Add(d);
            Assert.AreEqual(1, restored.Count, "promotion must restore exactly one drill for that planet -- never a duplicate");
            Assert.AreEqual(drillNodeId, restored[0].NodeId, "the same drill identity must come back");
            Assert.AreEqual(2, restored[0].Runtime.Tier, "it must come back at T2");
            Assert.GreaterOrEqual(restored[0].Runtime.StoredAmount, storedBefore - 0.01f, "its stock must not have been lost across the round trip");
            Assert.IsNull(restored[0].ActiveUpgradeJob, "a completed upgrade must not still look in-flight");
        }

        [UnityTest]
        public IEnumerator DrillT2_SurvivesARealSaveAndLoad_WithTierStockAndReservationIntact()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            DrillStructure drill = PrepareUpgradableDrill(context, out PlanetProfile profile, out LogisticsCargo cargo);
            yield return null;

            string drillWorldId = drill.WorldId;
            Assert.AreEqual(DrillUpgradeRejectReason.None, drill.StartUpgrade(2, cargo));
            float reservedPartsBefore = cargo.Get(ProcessingCatalog.BasicPartId) - cargo.GetAvailable(ProcessingCatalog.BasicPartId);
            Assert.Greater(reservedPartsBefore, 0f, "sanity: the upgrade cost must actually be reserved");

            context.ExtractionDirector.SaveNow(null);
            yield return null;
            context.ExtractionDirector.LoadNow();
            yield return null;
            yield return null;

            DrillStructure reloaded = null;
            int count = 0;
            foreach (DrillStructure d in ExtractionState.Drills)
                if (d != null && d.WorldId == drillWorldId) { reloaded = d; count++; }

            Assert.AreEqual(1, count, "a save/load must never duplicate the drill");
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(1, reloaded.Runtime.Tier, "an unfinished upgrade must come back still at T1");
            Assert.IsNotNull(reloaded.ActiveUpgradeJob, "the in-flight upgrade must resume, not silently vanish");

            LogisticsCargo reloadedCargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            float reservedPartsAfter = reloadedCargo.Get(ProcessingCatalog.BasicPartId) - reloadedCargo.GetAvailable(ProcessingCatalog.BasicPartId);
            Assert.AreEqual(reservedPartsBefore, reservedPartsAfter, 0.01f,
                "the remaining cost must be re-reserved exactly -- never double-charged and never left free");
        }

        [UnityTest]
        public IEnumerator DrillUpgrade_ThroughTheRealShipExtractionUiSeam_StartsAndCancelsTheRealBackendJob()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            DrillStructure drill = PrepareUpgradableDrill(context, out PlanetProfile profile, out LogisticsCargo cargo);
            yield return null;

            var shipExtraction = context.Ship.GetComponent<ShipExtraction>();
            Assert.IsNotNull(shipExtraction, "the real UI seam component must exist on the ship");

            // The gate the UI reads is the real module tier -- with it unmet, the typed rejection
            // is what the player is shown, and no job is ever created.
            Assert.AreEqual(DrillUpgradeRejectReason.ModuleTierNotMet, drill.StartUpgrade(0, cargo));
            Assert.IsNull(drill.ActiveUpgradeJob, "a gated-out upgrade must never create a backend job");
            // Turkish, and built from the catalog constant rather than spelled out. Every
            // player-facing rejection in this project is written in Turkish at the
            // producer (ShipExtraction.cs:776-784, and the reason why at :444-448: the
            // feedback channel prefixes a Turkish phrase, so an English tail produced
            // half a sentence in each language). This line still expected the pre-
            // translation English and was the only place in the suite that did --
            // UiFinalFeatureParityPlayModeTests.cs:392, NebulaHudContextTests.cs:295 and
            // NebulaUiScreenshotTests.cs:460 all already assert the Turkish text.
            StringAssert.Contains($"İSTASYON İNŞASI T{DrillUpgradeCatalog.RequiredStationConstructionTier}",
                ShipExtraction.DrillUpgradeRejectMessage(DrillUpgradeRejectReason.ModuleTierNotMet));

            // With the gate met the very same call reaches the real backend transaction.
            Assert.AreEqual(DrillUpgradeRejectReason.None, drill.StartUpgrade(2, cargo));
            Assert.IsNotNull(drill.ActiveUpgradeJob);
            Assert.AreEqual(DrillUpgradeRejectReason.AlreadyUpgrading, drill.StartUpgrade(2, cargo), "a second start must be rejected, never double-reserve");

            drill.CancelUpgrade(cargo);
            Assert.IsNull(drill.ActiveUpgradeJob);
        }

        // ==================== starting cargo ====================

        [UnityTest]
        public IEnumerator NewCampaign_StartsWithFiftyBasicBuildingParts()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();

            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            Assert.AreEqual(ShipLogistics.StartingBasicParts, cargo.Get(ProcessingCatalog.BasicPartId), 0.0001f,
                "a brand-new campaign must start with its documented Basic Building Parts stock");
            Assert.AreEqual(50f, ShipLogistics.StartingBasicParts, "the documented starting stock is 50 parts");
            Assert.LessOrEqual(cargo.UsedVolume, cargo.Capacity, "the starting stock must fit inside the ship's own base cargo capacity");
        }

        [UnityTest]
        public IEnumerator LoadingASave_ReplacesTheStartingCargo_NeverAddsTheFiftyPartsOnTop()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;

            // Spend some of the starting stock, save, then load it back.
            cargo.TryRemove(ProcessingCatalog.BasicPartId, 20f);
            float expected = cargo.Get(ProcessingCatalog.BasicPartId);
            Assert.AreEqual(30f, expected, 0.0001f, "sanity: 50 starting parts minus 20 spent");

            context.ExtractionDirector.SaveNow(null);
            yield return null;
            context.ExtractionDirector.LoadNow();
            yield return null;

            LogisticsCargo reloaded = context.Ship.GetComponent<ShipLogistics>().Cargo;
            Assert.AreEqual(expected, reloaded.Get(ProcessingCatalog.BasicPartId), 0.0001f,
                "a load must restore exactly the saved stock -- the new-campaign seed must never be re-applied on top of it");
        }

        // ==================== helpers ====================

        private static int CountRecorded(string eventId)
        {
            int count = 0;
            foreach (TelemetryEventRecord e in TelemetryService.SessionEventsSnapshot)
                if (e.eventId == eventId) count++;
            return count;
        }

        /// <summary>
        /// Raises the real <see cref="DominanceSystem.PlanetFirstControlled"/> signal the bridge
        /// actually subscribes to. The planet is genuinely set Controlled first, so what the
        /// bridge then re-derives from authoritative ownership state is real — only the
        /// notification is invoked directly, to avoid waiting out the real 120s stabilization
        /// window inside a PlayMode test.
        /// </summary>
        private static void RaisePlanetFirstControlled(string worldId)
        {
            ExtractionState.GetDominance(worldId).Restore(DominanceState.Controlled, 0f, 0f);
            System.Reflection.FieldInfo field = typeof(DominanceSystem).GetField(
                "PlanetFirstControlled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            (field?.GetValue(null) as System.Action<string>)?.Invoke(worldId);
        }
    }
}
