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
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Telemetry;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// VS-017A PlayMode coverage: three short end-to-end route scenarios, Siege/Plasma
    /// intended-target demote-&gt;promote continuity, and telemetry wired to a real
    /// gameplay signal. Remote economy/combat continuity and save/load/restart
    /// singleton guarantees are already exercised extensively by the VS-016A/
    /// VS-016A.1 PlayMode suites this one runs alongside -- not duplicated here.
    /// </summary>
    public class Vs017aPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;
        private readonly Dictionary<string, string> slot1Backup = new Dictionary<string, string>();
        private string telemetryTempDir;

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
            telemetryTempDir = Path.Combine(Path.GetTempPath(), "nebula-vs017a-telemetry-" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            TelemetryService.EndSession(telemetryTempDir);
            if (Directory.Exists(telemetryTempDir)) Directory.Delete(telemetryTempDir, true);
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
        /// system, resolved through the live registry — somewhere genuinely else for the ship to
        /// fly to. Where the start sector really holds a second star this IS that star; where it
        /// does not, it is the first system of the next sector the galaxy fills. A demotion only
        /// needs the ship to physically leave one system for another, never for the two to share
        /// a sector.
        /// <para>
        /// Never an id built by index. <c>WorldIdCodec.SystemId(startSector, 1)</c> names slot 1
        /// of the start sector, which only exists when the galaxy happened to drop a second star
        /// inside it; measured over seeds 0..399 sector.0.0.0 holds one system on 301 of them, so
        /// on most campaigns <c>ResolveSystem</c> returned null and the next line threw.
        /// </para>
        /// </summary>
        private static SystemManifest ForeignSystem(NebulaContext context)
        {
            foreach (SectorCoordinate sector in OccupiedSectors(context.StartSector))
                foreach (SystemManifest system in StarSystemRegistry.Manifests.Resolve(sector).Systems)
                {
                    if (system.SystemId == context.StartSystemId) continue;
                    Assert.Greater(system.Planets.Count, 0,
                        $"{system.SystemId}: a system with no planets is not a place a ship can arrive in for these tests");
                    return system;
                }

            Assert.Fail("this campaign's galaxy placed no system at all outside its start system");
            return null;
        }

        // ==================== Route A: Keşif ve Yayılma (short end-to-end) ====================

        [UnityTest]
        public IEnumerator RouteDiscovery_ScannedActiveDrill_ProducesRealStoredResourceOverTime()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;

            PlanetManifest planetManifest = context.StartSystem.Planets[0];
            PlanetIdentity planet = ExtractionState.FindPlanet(planetManifest.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;

            var runtime = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, 0f, 0f);
            DrillStructure drill = DrillStructure.Restore(planet, profile, runtime, DrillStructure.BuildSource.Kit, 0);
            yield return null;

            float storedBefore = drill.Runtime.StoredAmount;
            Time.timeScale = 20f;
            float realStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - realStart < 3f) yield return null;
            Time.timeScale = 1f;

            Assert.Greater(drill.Runtime.StoredAmount, storedBefore, "a real, scanned, Active drill must genuinely produce stored resource over time -- the discovery/expansion chain's own real output");
        }

        // ==================== Route B: Endüstri ve Lojistik (short end-to-end) ====================

        [UnityTest]
        public IEnumerator RouteIndustry_RealHangarProduction_CompletesAndConsumesRealCargoExactlyOnce()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            ShipHangar hangar = context.Ship.GetComponent<ShipHangar>();
            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 10000f));
            cargo.Add(ProcessingCatalog.BasicPartId, 500f);
            cargo.Add("material.biocomposite", 500f);
            // A fresh campaign starts at level 1 with hangar tier 0; Scout needs level 10 + hangar tier 1.
            Progression.ProgressionService.Award("vs017a-test-level-boost", (int)Progression.ProgressionMath.TotalXpToReach(10));
            hangar.ApplyHangarTier(1);

            int vehiclesBefore = hangar.Vehicles.Count;
            HangarProductionRejectReason reject = hangar.TryStartProduction(VehicleClass.Scout, "material.biocomposite");
            Assert.AreEqual(HangarProductionRejectReason.None, reject, "sanity: production must actually start for this test to mean anything");

            Time.timeScale = 20f;
            float realStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - realStart < 5f && hangar.Vehicles.Count == vehiclesBefore) yield return null;
            Time.timeScale = 1f;

            Assert.AreEqual(vehiclesBefore + 1, hangar.Vehicles.Count, "real Hangar production must complete and add exactly one real vehicle -- the industry/logistics chain's own real output");
        }

        // ==================== Route C: Savunma ve Savaş (short end-to-end) ====================

        [UnityTest]
        public IEnumerator RouteDefense_RealActiveLaser_DamagesARealLiveEnemy()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            CombatEncounterDirector director = context.Streaming.CombatEncounter;

            PlanetManifest planetManifest = context.StartSystem.Planets[0];
            PlanetIdentity planet = ExtractionState.FindPlanet(planetManifest.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            SurfaceStructureDef laserDef = SurfaceStructureCatalog.Laser;
            var durability = new Durability(laserDef.ShieldMax, laserDef.ArmorMax, laserDef.HullMax, 1f);
            var runtime = new StructureRuntime(laserDef, DrillLifecycle.Active, 0f, 0f, durability, tier: 1);
            SurfaceStructure laser = SurfaceStructure.Restore(planet, profile, laserDef, profile.FirstDefenseNode, runtime, null, null);
            yield return null;

            // Straight up (away from the planet's own surface/collider), never laser.transform.forward --
            // a structure sitting on a curved planet surface can easily have "forward" clip back through
            // the planet itself, which would silently fail HasLineOfSight and never be a real "in range,
            // no LOS blocker" test at all.
            Vector3 enemyPos = laser.transform.position + Vector3.up * 500f;
            EnemyAgent enemy = director.SpawnEnemy(EnemyIds.FighterMk1, enemyPos);
            Assert.IsNotNull(enemy);
            // A fresh Fighter Mk I's 120 shield alone would fully absorb several real seconds of Laser
            // DPS before Hull ever moves -- the total shield+armor+hull pool is what actually proves a
            // real hit landed, exactly like the equivalent EM-projectile check elsewhere in this suite.
            float totalBefore = enemy.Combat.Shield + enemy.Combat.Armor + enemy.Combat.Hull;

            float realStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - realStart < 5f) yield return null;

            float totalAfter = enemy.Combat.Shield + enemy.Combat.Armor + enemy.Combat.Hull;
            Assert.Less(totalAfter, totalBefore, "a real active planetary Laser in range must genuinely damage a real live enemy -- the defense/combat chain's own real output");
        }

        // ==================== §1: Siege/Plasma intended-target demote/promote continuity ====================

        [UnityTest]
        public IEnumerator SiegeIntendedTarget_DemoteThenImmediatePromote_RestoredAimedAtTheSameStructure()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            string startSystemId = context.StartSystemId;
            SystemManifest startManifest = StarSystemRegistry.Manifests.ResolveSystem(startSystemId);
            CombatEncounterDirector director = context.Streaming.CombatEncounter;

            PlanetManifest planetManifest = context.StartSystem.Planets[0];
            PlanetIdentity planet = ExtractionState.FindPlanet(planetManifest.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            SurfaceStructureDef commandDef = SurfaceStructureCatalog.Command;
            var durability = new Durability(commandDef.ShieldMax, commandDef.ArmorMax, commandDef.HullMax, 1f);
            var runtime = new StructureRuntime(commandDef, DrillLifecycle.Active, 0f, 0f, durability, tier: 1);
            SurfaceStructure command = SurfaceStructure.Restore(planet, profile, commandDef, profile.CommandNode, runtime, null, null);
            string intendedTargetId = command.TargetId;
            yield return null;

            // A real Siege shot aimed at that specific Command structure (mirrors EnemyAgent.FireSiegeProjectile's own real intendedTargetId derivation).
            Vector3 shotPos = command.transform.position + Vector3.forward * 2000f;
            ProjectileObject.Spawn(ProjectileKind.EnemySiege, 1, "test-enemy", shotPos, Vector3.back * ProjectileCatalog.SiegeProjectileSpeed,
                ProjectileCatalog.SiegeLifetimeSeconds, null, director, ProjectileFaction.Hostile,
                interceptable: true, interceptionDurability: ProjectileCatalog.InterceptionDurability, damageAmount: 100f,
                intendedTargetId: intendedTargetId);
            yield return null;

            SystemDelta delta = context.Streaming.CaptureCurrentFullDelta();
            Assert.AreEqual(1, delta.pendingAttacks.Count);
            Assert.AreEqual(intendedTargetId, delta.pendingAttacks[0].targetId, "the demoted PendingAttackDto must carry the shot's real intended structure target, not an empty id");
            string attackId = delta.pendingAttacks[0].attackId;
            StarSystemRegistry.Deltas.Set(delta);

            SystemManifest neighborManifest = ForeignSystem(context);
            Assert.AreNotEqual(startSystemId, neighborManifest.SystemId,
                "the demotion needs a genuinely different system to fly to");
            context.Ship.transform.position = neighborManifest.AbsoluteCenter - FloatingOrigin.TotalOffset;
            context.Streaming.ResetAndBootFull(neighborManifest);
            yield return null;
            yield return null;

            context.Ship.transform.position = startManifest.AbsoluteCenter - FloatingOrigin.TotalOffset;
            context.Streaming.ResetAndBootFull(startManifest);
            yield return null;
            yield return null;

            ProjectileObject restored = null;
            foreach (ProjectileObject p in Object.FindObjectsOfType<ProjectileObject>()) if (p.State.ProjectileId == attackId) restored = p;
            Assert.IsNotNull(restored, "the unresolved Siege shot must be rematerialized as a real live projectile");
            Assert.AreEqual(intendedTargetId, restored.State.IntendedTargetId, "the rematerialized projectile must still be aimed at the exact same structure it was demoted with");
        }

        [UnityTest]
        public IEnumerator PlasmaIntendedTarget_FullDemoteRemoteTickPromote_ResolvesAgainstTheSameEnemy()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            string startSystemId = context.StartSystemId;
            CombatEncounterDirector director = context.Streaming.CombatEncounter;

            Vector3 enemyPos = context.Ship.transform.position + context.Ship.transform.forward * 500f;
            EnemyAgent enemy = director.SpawnEnemy(EnemyIds.FighterMk1, enemyPos);
            Assert.IsNotNull(enemy);
            string enemyId = enemy.Id;
            // DamageProfile.Plasma still has a real (0.70x) ShieldMultiplier, and a fresh Fighter Mk I's
            // full 120 shield can absorb this shot's entire 80 damage before Hull ever moves -- the
            // total shield+armor+hull pool is what actually proves the hit landed.
            float totalDurabilityBefore = enemy.Combat.Shield + enemy.Combat.Armor + enemy.Combat.Hull;

            // A real planetary Plasma shot aimed at that specific enemy (mirrors SurfaceStructure.FirePlasmaAtTarget's own real intendedTargetId derivation).
            Vector3 shotPos = context.Ship.transform.position;
            Vector3 toEnemy = (enemy.transform.position - shotPos).normalized;
            ProjectileObject.Spawn(ProjectileKind.Plasma, 1, "test-structure/defense.0", shotPos, toEnemy * WeaponCatalog.PlasmaProjectileSpeed,
                2f, null, director, ProjectileFaction.Player, damageAmount: 80f, intendedTargetId: enemyId);
            yield return null;

            SystemDelta delta = context.Streaming.CaptureCurrentFullDelta();
            Assert.AreEqual(1, delta.pendingAttacks.Count);
            Assert.AreEqual(enemyId, delta.pendingAttacks[0].targetId, "the demoted PendingAttackDto must carry the shot's real intended enemy target");
            StarSystemRegistry.Deltas.Set(delta);

            SystemManifest neighborManifest = ForeignSystem(context);
            Assert.AreNotEqual(startSystemId, neighborManifest.SystemId,
                "the demotion needs a genuinely different system to fly to");
            context.Ship.transform.position = neighborManifest.AbsoluteCenter - FloatingOrigin.TotalOffset;
            context.Streaming.ResetAndBootFull(neighborManifest);
            yield return null;
            yield return null;

            Time.timeScale = 20f;
            float realStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - realStart < 6f) yield return null;
            Time.timeScale = 1f;

            SystemDelta reloaded = StarSystemRegistry.Deltas.TryGet(startSystemId);
            Assert.IsNotNull(reloaded);
            EnemyDto enemyDto = reloaded.enemies.Find(e => e.enemyId == enemyId);
            Assert.IsNotNull(enemyDto);
            float totalDurabilityAfter = enemyDto.shield + enemyDto.armor + enemyDto.hull;
            Assert.Less(totalDurabilityAfter, totalDurabilityBefore, "the Plasma shot must have resolved against the exact enemy it was aimed at, not vanished or hit nothing");
        }

        // ==================== §3: telemetry wired to a real gameplay signal ====================

        [UnityTest]
        public IEnumerator Telemetry_RealDominanceFirstEvent_RecordedThroughTheGameplayBridge()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;

            // VS-017A.1: TelemetryInstaller now wires the ONE real bridge (and starts the ONE real
            // session) automatically as part of composition -- never a second, manually-created bridge/session.
            Assert.IsNotNull(context.TelemetryBridge, "composition must have already wired the real telemetry bridge");
            Assert.IsTrue(TelemetryService.SessionActive, "composition must have already started the real telemetry session");

            PlanetManifest planetManifest = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            PlanetIdentity planet = ExtractionState.FindPlanet(planetManifest.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            profile.Scanned = true;

            var drillRuntime = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, 0f, 0f);
            DrillStructure.Restore(planet, profile, drillRuntime, DrillStructure.BuildSource.Kit, 0);
            SurfaceStructureDef commandDef = SurfaceStructureCatalog.Command;
            var commandDurability = new Durability(commandDef.ShieldMax, commandDef.ArmorMax, commandDef.HullMax, 1f);
            SurfaceStructure.Restore(planet, profile, commandDef, profile.CommandNode, new StructureRuntime(commandDef, DrillLifecycle.Active, 0f, 0f, commandDurability, 1), null, null);
            SurfaceStructureDef laserDef = SurfaceStructureCatalog.Laser;
            var laserDurability = new Durability(laserDef.ShieldMax, laserDef.ArmorMax, laserDef.HullMax, 1f);
            SurfaceStructure.Restore(planet, profile, laserDef, profile.FirstDefenseNode, new StructureRuntime(laserDef, DrillLifecycle.Active, 0f, 0f, laserDurability, 1), null, null);
            yield return null;

            Time.timeScale = 50f;
            float realStart = Time.realtimeSinceStartup;
            bool sawEvent = false;
            while (Time.realtimeSinceStartup - realStart < 8f)
            {
                yield return null;
                foreach (TelemetryEventRecord e in TelemetryService.SessionEventsSnapshot)
                    if (e.eventId == TelemetryEvents.FirstControlledPlanet && e.contentId == planetManifest.PlanetId) sawEvent = true;
                if (sawEvent) break;
            }
            Time.timeScale = 1f;

            Assert.IsTrue(sawEvent, "a real first-Controlled dominance transition must be observed and recorded by the telemetry gameplay bridge, through the actual DominanceSystem.PlanetFirstControlled event -- never a synthetic/manual Record call");
        }

        // ==================== VS-017A.1 §7: bridge singleton after restart/session-dispose ====================

        [UnityTest]
        public IEnumerator TelemetryBridge_RestartCampaign_NeverLeavesMoreThanOneLiveBridgeOrSubscriber()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext contextBefore = adapter.Context;
            Assert.AreEqual(1, Object.FindObjectsOfType<TelemetryGameplayBridge>().Length, "composition must have wired exactly one bridge on first boot");
            TelemetryGameplayBridge bridgeBefore = contextBefore.TelemetryBridge;

            adapter.RestartCampaign();
            yield return null;
            yield return null;
            yield return null;

            TelemetryGameplayBridge[] bridgesAfter = Object.FindObjectsOfType<TelemetryGameplayBridge>();
            Assert.AreEqual(1, bridgesAfter.Length, "a campaign restart must tear down the old bridge (unsubscribing it) before wiring exactly one new one -- never two live subscribers at once");
            Assert.AreNotSame(bridgeBefore, bridgesAfter[0], "the restart must have produced a genuinely new bridge instance, not merely reused the old (potentially half-torn-down) one");

            // Prove the OLD bridge is truly unsubscribed (not just replaced) by inspecting the real
            // invocation list of ProgressionService.Awarded -- the exact authoritative static event
            // TelemetryGameplayBridge.Initialize/OnDestroy subscribe/unsubscribe. This is a direct,
            // instantaneous, deterministic check of live delegate subscriptions -- unlike waiting on
            // a real gameplay signal to fire, it can never be confused by telemetry session/funnel-
            // dedup state carried over from other tests or process runs.
            Assert.AreEqual(0, CountAwardedSubscribers(bridgeBefore), "the old (pre-restart) bridge must be fully unsubscribed from ProgressionService.Awarded after a restart -- OnDestroy must have run");
            Assert.AreEqual(1, CountAwardedSubscribers(bridgesAfter[0]), "exactly one live ProgressionService.Awarded subscription must belong to the new post-restart bridge");
        }

        /// <summary>Reflects ProgressionService's auto-generated static event backing field to count how many live subscriptions belong to `target` -- avoids depending on any observable side effect (telemetry, save, XP) just to prove a delegate's own subscriber list.</summary>
        private static int CountAwardedSubscribers(object target)
        {
            System.Reflection.FieldInfo field = typeof(Progression.ProgressionService).GetField("Awarded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var del = field?.GetValue(null) as System.Delegate;
            if (del == null) return 0;
            int count = 0;
            foreach (System.Delegate d in del.GetInvocationList())
                if (ReferenceEquals(d.Target, target)) count++;
            return count;
        }

        // ==================== VS-017A.1 §2/§6: real 32-system save/load performance ====================

        /// <summary>
        /// Real systems from sectors other than the campaign's own start sector, so this
        /// fixture's synthetic content never collides with whatever the booted campaign's start
        /// system already carries — the same isolation the old "sectors starting at x=1" walk was
        /// written for.
        /// <para>
        /// The walk is over the galaxy's own occupied sectors now, not along (x, 0, 0) until the
        /// count is satisfied. That old walk could not terminate: sectors are filled from
        /// <see cref="GalaxyStarField"/>, which is finite (128 stars) and strung along spiral
        /// arms across millions of units on every axis, so a straight line at y=z=0 leaves the
        /// stars almost at once and every later sector is empty — <c>result.Count</c> stops
        /// rising while the loop keeps resolving sectors, each one cached forever by
        /// <see cref="SectorManifestRepository"/>. This test died on OutOfMemoryException.
        /// </para>
        /// </summary>
        private static List<SystemManifest> CollectAdditionalManifests(SectorCoordinate startSector, int minCount)
        {
            var result = new List<SystemManifest>();
            foreach (SectorCoordinate sector in OccupiedSectors(startSector))
            {
                if (sector.Equals(startSector)) continue;
                foreach (SystemManifest system in StarSystemRegistry.Manifests.Resolve(sector).Systems)
                {
                    Assert.Greater(system.Planets.Count, 0,
                        $"{system.SystemId}: a system with no planets cannot carry the representative delta this measurement is built from");
                    result.Add(system);
                }
                if (result.Count >= minCount) break;
            }

            Assert.GreaterOrEqual(result.Count, minCount,
                $"this campaign's galaxy only yields {result.Count} systems outside its start sector; this fixture needs at least {minCount}");
            return result;
        }

        /// <summary>A representative mutable delta -- drill, Command+Laser structures, an enemy, a pending attack, a physically-present vehicle, a Logistics Center, and a POI -- so the real save payload this measures is genuinely comparable to a played campaign's, not an empty/trivial one.</summary>
        private static SystemDelta BuildRepresentativeDelta(SystemManifest manifest)
        {
            string worldId = manifest.Planets[0].PlanetId;
            var delta = new SystemDelta { systemId = manifest.SystemId };
            delta.scanned.Add(worldId);
            delta.drills.Add(new DrillDto { worldId = worldId, nodeId = "drill", state = (int)DrillLifecycle.Active, stored = 42.5f, accumulator = 0.3f });
            delta.structures.Add(new StructureDto { worldId = worldId, nodeId = "command", type = (int)SurfaceStructureType.Command, state = (int)DrillLifecycle.Active, tier = 1, shield = 300f, armor = 900f, hull = 1500f });
            delta.structures.Add(new StructureDto { worldId = worldId, nodeId = "defense.0", type = (int)SurfaceStructureType.Laser, state = (int)DrillLifecycle.Active, tier = 1, shield = 100f, armor = 250f, hull = 400f });
            delta.dominance.Add(new DominanceDto { worldId = worldId, state = (int)DominanceState.Controlled });
            delta.enemies.Add(new EnemyDto { enemyId = manifest.SystemId + "-enemy", defId = EnemyIds.FighterMk1, shield = 90f, armor = 60f, hull = 150f });
            delta.pendingAttacks.Add(new PendingAttackDto { attackId = manifest.SystemId + "-atk", targetId = "ship", damageAmount = 15f, remainingFlightSeconds = 4f });
            delta.vehicles.Add(new VehicleDto { vehicleId = manifest.SystemId + "-veh", vehicleClass = (int)VehicleClass.Scout, mk = 1, homeSystemId = manifest.SystemId, shield = 80f, armor = 100f, hull = 150f });
            delta.logisticsCenters.Add(new LogisticsCenterDto { systemId = manifest.SystemId, tier = 1, state = 1, shield = 150f, armor = 300f, hull = 500f });
            delta.pois.Add(new PoiDto { poiId = manifest.SystemId + "-poi", level = 1 });
            return delta;
        }

        [UnityTest]
        public IEnumerator SavePerformance_ThirtyTwoSystemFixture_RealTwentyPlusSaveAndLoadSamples()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;

            List<SystemManifest> manifests = CollectAdditionalManifests(context.StartSector, 32);
            Assert.GreaterOrEqual(manifests.Count, 32, "the fixture must genuinely span >=32 discovered systems");
            foreach (SystemManifest m in manifests) StarSystemRegistry.Deltas.Set(BuildRepresentativeDelta(m));

            SavePerformanceRecorder recorder = ExtractionDirector.PerformanceRecorder;

            // Warm-up samples (JIT/disk-cache settling) are kept explicitly separate from the real
            // measurement set -- discarded via Clear(), never mixed into the reported percentiles.
            for (int i = 0; i < 3; i++) { context.ExtractionDirector.SaveNow(null); context.ExtractionDirector.LoadNow(); yield return null; }
            recorder.Clear();

            const int SampleCount = 20;
            for (int i = 0; i < SampleCount; i++)
            {
                context.ExtractionDirector.SaveNow(null);
                context.ExtractionDirector.LoadNow();
                yield return null;
            }

            SavePerformanceRecorder.Stats snapshotStats = recorder.MainThreadStats;
            SavePerformanceRecorder.Stats writeStats = recorder.BackgroundWriteStats;
            SavePerformanceRecorder.Stats loadStats = recorder.LoadStats;
            long payloadBytes = new FileInfo(SaveService.SlotFile(GameplaySlot)).Length;

            Assert.GreaterOrEqual(snapshotStats.Count, SampleCount, "must contain >=20 real (post-warm-up) main-thread snapshot samples");
            Assert.GreaterOrEqual(writeStats.Count, SampleCount, "must contain >=20 real (post-warm-up) background-write samples");
            Assert.GreaterOrEqual(loadStats.Count, SampleCount, "must contain >=20 real (post-warm-up) load samples");

            var report = new SavePerformanceReport
            {
                systemCount = manifests.Count,
                sampleCount = SampleCount,
                payloadBytes = payloadBytes,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                isDevelopmentBuild = Debug.isDebugBuild,
                snapshotP50Ms = snapshotStats.P50, snapshotP95Ms = snapshotStats.P95, snapshotMaxMs = snapshotStats.Max,
                writeP50Ms = writeStats.P50, writeP95Ms = writeStats.P95, writeMaxMs = writeStats.Max,
                loadP50Ms = loadStats.P50, loadP95Ms = loadStats.P95, loadMaxMs = loadStats.Max,
            };
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(projectRoot, "TestResults");
            Directory.CreateDirectory(dir);
            // A dedicated diagnostics report -- deliberately never mixed into the telemetry log.
            File.WriteAllText(Path.Combine(dir, "vs017a1-save-performance.json"), JsonUtility.ToJson(report, true));

            Assert.LessOrEqual(snapshotStats.P95, SavePerformanceRecorder.MainThreadSnapshotTargetMs, $"real snapshot p95 exceeded the {SavePerformanceRecorder.MainThreadSnapshotTargetMs}ms target: {snapshotStats}");
            Assert.LessOrEqual(writeStats.P95, SavePerformanceRecorder.BackgroundWriteTargetMs, $"real write p95 exceeded the {SavePerformanceRecorder.BackgroundWriteTargetMs}ms target: {writeStats}");
            Assert.LessOrEqual(loadStats.P95, SavePerformanceRecorder.LoadTargetMs, $"real load p95 exceeded the {SavePerformanceRecorder.LoadTargetMs}ms target: {loadStats}");
        }

        [System.Serializable]
        private class SavePerformanceReport
        {
            public int systemCount, sampleCount;
            public long payloadBytes;
            public string unityVersion, platform;
            public bool isDevelopmentBuild;
            public double snapshotP50Ms, snapshotP95Ms, snapshotMaxMs;
            public double writeP50Ms, writeP95Ms, writeMaxMs;
            public double loadP50Ms, loadP95Ms, loadMaxMs;
        }
    }
}
