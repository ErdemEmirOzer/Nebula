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
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for VS-016A: a real Full-&gt;demote-&gt;1 Hz
    /// remote tick-&gt;promote cycle, save/load without duplication, restart
    /// without a second scheduler/director, and same-system support/logistics
    /// continuity across a real demotion. Time.timeScale is used only to make
    /// an otherwise multi-hundred-real-second remote wait observable within a
    /// test's time budget -- per VS-016A §2 this only changes how long (real,
    /// human time) it takes to reach a given simulated state, never the state
    /// itself, so accelerating it here never invalidates what's being checked.
    /// </summary>
    public class Vs016aRemoteSimulationIntegrationTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;
        private float originalTimeScale;
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
        public void BackupRealGameplaySlot()
        {
            originalTimeScale = Time.timeScale;
            slot1Backup.Clear();
            foreach (string f in Slot1Files()) slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
        {
            Time.timeScale = originalTimeScale;
            foreach (var kv in slot1Backup)
            {
                if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                else File.WriteAllText(kv.Key, kv.Value);
            }
        }

        /// <summary>
        /// A real second system to demote the start system in favour of.
        /// <para>
        /// Every one of these tests used to name its neighbour by hand as
        /// <c>system.001</c> of the start sector, which assumes a sector holds at
        /// least two systems. The galaxy's star field is clustered and does not
        /// promise that — the start sector holds exactly one — so
        /// <c>ResolveSystem</c> returned null and four fixtures died on a
        /// NullReferenceException before they reached anything they were written to
        /// check. The neighbour is asked of the galaxy instead: the sector's own
        /// list first, then the ring of sectors around it. Nothing about what the
        /// tests assert changes; they just get a system that exists.
        /// </para>
        /// </summary>
        private static SystemManifest FindNeighbourOf(string startSystemId, SectorCoordinate sector)
        {
            SectorManifest own = StarSystemRegistry.Manifests.Resolve(sector);
            foreach (SystemManifest candidate in own.Systems)
                if (candidate.SystemId != startSystemId) return candidate;

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                SectorManifest near = StarSystemRegistry.Manifests.Resolve(
                    new SectorCoordinate(sector.X + dx, sector.Y + dy, sector.Z));
                foreach (SystemManifest candidate in near.Systems)
                    if (candidate.SystemId != startSystemId) return candidate;
            }
            return null;
        }

        /// <summary>The same neighbour, with the assertion that makes a missing one legible.</summary>
        private static SystemManifest RequireNeighbourOf(string startSystemId, SectorCoordinate sector)
        {
            SystemManifest neighbour = FindNeighbourOf(startSystemId, sector);
            Assert.IsNotNull(neighbour,
                "this test needs a second real system to demote the start system in favour of; " +
                "no sector in the ring around the start had one");
            return neighbour;
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

        [UnityTest]
        public IEnumerator SceneBoots_WithExactlyOneSystemStreamingDirector()
        {
            yield return LoadSceneFresh();
            Assert.AreEqual(1, Object.FindObjectsOfType<SystemStreamingDirector>().Length);
        }

        [UnityTest]
        public IEnumerator RestartCampaign_NeverDuplicatesTheStreamingDirector()
        {
            yield return LoadSceneFresh();
            yield return LoadSceneFresh();
            yield return LoadSceneFresh();
            Assert.AreEqual(1, Object.FindObjectsOfType<SystemStreamingDirector>().Length,
                "repeated restarts must never leave a second (stale) SystemStreamingDirector -- and therefore never a second remote scheduler either");
        }

        [UnityTest]
        public IEnumerator FullDemoteRemoteTickPromote_StructureUpgradeContinuesAndCompletesRemotely()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            string startSystemId = context.StartSystemId;

            // A real, already-Active Command T1 with an in-flight T1->T2 upgrade started right before demotion.
            SurfaceStructureDef commandDef = SurfaceStructureCatalog.Command;
            var durability = new Durability(commandDef.ShieldMax, commandDef.ArmorMax, commandDef.HullMax, 0.5f);
            var runtime = new StructureRuntime(commandDef, DrillLifecycle.Active, 0f, 0f, durability, tier: 1);
            PlanetManifest outerPlanet = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            PlanetIdentity planet = ExtractionState.FindPlanet(outerPlanet.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            SurfaceStructure command = SurfaceStructure.Restore(planet, profile, commandDef, profile.CommandNode, runtime, null, null);
            yield return null;

            StructureUpgradeRejectReason startReason = command.StartUpgrade(30, 1, 0, true, context.Ship.GetComponent<ShipLogistics>().Cargo);
            if (startReason != StructureUpgradeRejectReason.None)
            {
                // Ensure affordability deterministically rather than depending on starting cargo contents.
                LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
                cargo.SetCapacity(Mathf.Max(cargo.Capacity, 10000f));
                cargo.Add(ProcessingCatalog.BasicPartId, 200f);
                cargo.Add("material.biocomposite", 100f);
                cargo.Add("material.metal_alloy", 100f);
                startReason = command.StartUpgrade(30, 1, 0, true, cargo);
            }
            Assert.AreEqual(StructureUpgradeRejectReason.None, startReason, "sanity: the upgrade must actually be in flight before demotion for this test to mean anything");

            // Demote via the same real capture-then-reboot public seam ExtractionDirector.LoadNow itself uses
            // (exactly like Vs013CombatTests' own FullDemoteFullCycle test). The ship must already be
            // positioned near the neighbor system's own center BEFORE the reboot call -- otherwise the very
            // next Update()'s ordinary TickPromotionDemotion distance check still finds the ship sitting where
            // "system.000" is and immediately re-promotes it back before the test ever gets a chance to move
            // it (a raw large-offset teleport was tried instead and risked an unrelated pre-existing
            // NavigationDirector.CurrentSystemId edge case outside every known sector).
            SystemDelta capturedDelta = context.Streaming.CaptureCurrentFullDelta();
            StarSystemRegistry.Deltas.Set(capturedDelta);
            WorldIdCodec.TryParseSystemId(startSystemId, out SectorCoordinate sector, out _);
            SystemManifest neighborManifest = RequireNeighbourOf(startSystemId, sector);
            context.Ship.transform.position = neighborManifest.AbsoluteCenter - FloatingOrigin.TotalOffset;
            context.Streaming.ResetAndBootFull(neighborManifest);
            yield return null;
            yield return null;
            Assert.AreNotEqual(startSystemId, context.Streaming.FullSystemId, "sanity: the start system must now genuinely be non-Full");

            // Accelerate real time so the remote tick's own real-second-based scheduler covers a large
            // number of SIMULATED seconds quickly -- the simulated result itself is time-scale-independent (§2).
            Time.timeScale = 50f;
            float upgradeSeconds = PlanetaryDefenseCatalog.CommandT2.BuildSeconds;
            float waited = 0f;
            while (waited < upgradeSeconds + 30f)
            {
                yield return null;
                waited += Time.unscaledDeltaTime * Time.timeScale;
            }
            Time.timeScale = 1f;

            SystemDelta delta = StarSystemRegistry.Deltas.TryGet(startSystemId);
            Assert.IsNotNull(delta);
            StructureDto commandDto = null;
            foreach (StructureDto s in delta.structures) if ((SurfaceStructureType)s.type == SurfaceStructureType.Command) commandDto = s;
            Assert.IsNotNull(commandDto, "the Command structure must have been captured into the demoted system's own delta");
            Assert.IsFalse(commandDto.upgrading, "the in-flight upgrade must have completed purely from remote 1 Hz ticking while demoted");
            Assert.AreEqual(2, commandDto.tier);
        }

        [UnityTest]
        public IEnumerator SaveThenLoad_RemoteSystemState_NeverDuplicatesPendingAttacksEnemiesResultOrSalvage()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;

            SystemDelta delta = StarSystemRegistry.Deltas.GetOrCreate("sector.0.0.0/system.005");
            delta.pendingAttacks.Add(new PendingAttackDto { attackId = "atk-1", damageAmount = 10f, remainingFlightSeconds = 5f });
            delta.enemies.Add(new EnemyDto { enemyId = "enemy-1", defId = EnemyIds.FighterMk1, shield = 0f, armor = 50f, hull = 100f });
            // VS-016A.1 §4: a raid's own single-result/single-salvage guards (ResultApplied/SalvageApplied)
            // and its wreckage must round-trip exactly once too, not just enemies/pendingAttacks.
            delta.threatPressure = new ThreatPressureDto { phase = (int)RaidPhase.Resolved, resultApplied = true, salvageApplied = true };
            var wreck = new CombatWreckageDto { wreckageId = "wreck-1", remainingActiveSeconds = 30f };
            wreck.nodes.Add(new CombatWreckageNodeDto { itemId = "part.basic", total = 10f, remaining = 10f });
            delta.combatWreckage.Add(wreck);

            context.ExtractionDirector.SaveNow(null);
            yield return null;
            context.ExtractionDirector.LoadNow();
            yield return null;
            yield return null;

            SystemDelta reloaded = StarSystemRegistry.Deltas.TryGet("sector.0.0.0/system.005");
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(1, reloaded.pendingAttacks.Count, "save/load must never duplicate a pending attack");
            Assert.AreEqual(1, reloaded.enemies.Count, "save/load must never duplicate a remote enemy");
            Assert.AreEqual(1, reloaded.combatWreckage.Count, "save/load must never duplicate salvage wreckage");
            Assert.IsTrue(reloaded.threatPressure.resultApplied && reloaded.threatPressure.salvageApplied,
                "a raid's single-result/single-salvage guards must survive the round trip exactly as they were, never reset to allow a second award");
        }

        [UnityTest]
        public IEnumerator FullDemoteRemoteTickPromote_InFlightPlayerEmProjectile_ResolvesRemotelyExactlyOnce()
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
            // EM's DamageProfile has a 2x ShieldMultiplier and a fresh Fighter Mk I spawns at full
            // 120 shield -- a modest EM hit can land entirely on shield and never touch Hull at all,
            // so the durability POOL total (not Hull specifically) is what actually proves the hit landed.
            float totalDurabilityBefore = enemy.Combat.Shield + enemy.Combat.Armor + enemy.Combat.Hull;

            // A real, currently-in-flight EM shot locked onto that enemy -- the only live spawn site
            // in this codebase that ever sets a real lockedTargetId (ShipCombat.cs), positioned close
            // enough that its deterministic distance/speed remaining flight time is small.
            Vector3 shotPos = context.Ship.transform.position;
            Vector3 toEnemy = (enemy.transform.position - shotPos).normalized;
            ProjectileObject.Spawn(ProjectileKind.Em, 1, "ship", shotPos, toEnemy * WeaponCatalog.EmProjectileSpeed,
                20f, enemyId, director, ProjectileFaction.Player);
            yield return null;

            SystemDelta delta = context.Streaming.CaptureCurrentFullDelta();
            Assert.AreEqual(1, delta.pendingAttacks.Count, "the live EM shot must become the single carryover PendingAttackDto");
            Assert.AreEqual(0, delta.projectiles.Count, "never both a ProjectileDto and a PendingAttackDto for the same attack");
            StarSystemRegistry.Deltas.Set(delta);

            WorldIdCodec.TryParseSystemId(startSystemId, out SectorCoordinate sector, out _);
            SystemManifest neighborManifest = RequireNeighbourOf(startSystemId, sector);
            context.Ship.transform.position = neighborManifest.AbsoluteCenter - FloatingOrigin.TotalOffset;
            context.Streaming.ResetAndBootFull(neighborManifest);
            yield return null;
            yield return null;
            Assert.AreNotEqual(startSystemId, context.Streaming.FullSystemId, "sanity: the start system must now genuinely be non-Full");

            // A short simulated-seconds target at a very high timeScale (as the structure-upgrade
            // test above uses for its own much longer ~150s budget) leaves too little REAL wall-clock
            // time in batchmode for the 1 Hz scheduler's own real-time sparseTickTimer accumulator to
            // ever cross its 1s threshold even once. Bounding this loop by real elapsed time instead,
            // at a moderate timeScale, guarantees both plenty of real frames AND a large simulated-time
            // budget (this shot's own remaining flight time is only a few seconds).
            Time.timeScale = 20f;
            float realStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - realStart < 6f) yield return null;
            Time.timeScale = 1f;

            SystemDelta reloaded = StarSystemRegistry.Deltas.TryGet(startSystemId);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(0, reloaded.pendingAttacks.Count, "the shot must have resolved remotely well within this window");
            EnemyDto enemyDto = reloaded.enemies.Find(e => e.enemyId == enemyId);
            Assert.IsNotNull(enemyDto, "Fighter Mk I must still exist remotely -- one EM hit is nowhere near its total shield+armor+hull pool");
            float totalDurabilityAfter = enemyDto.shield + enemyDto.armor + enemyDto.hull;
            Assert.Less(totalDurabilityAfter, totalDurabilityBefore, "the pending attack must have applied its real damage exactly once");
        }

        [UnityTest]
        public IEnumerator FullDemoteThenImmediatePromote_UnresolvedPendingAttack_RestoredAsLiveProjectileIntact()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            string startSystemId = context.StartSystemId;
            SystemManifest startManifest = StarSystemRegistry.Manifests.ResolveSystem(startSystemId);
            CombatEncounterDirector director = context.Streaming.CombatEncounter;

            // A real hostile Siege shot far from anything -- no locked target (matches every live
            // spawn site for Siege/Plasma in this codebase, which are proximity-hit, never locked),
            // so its only carried value is a long remaining lifetime that must still have plenty
            // left after a fast demote/promote round trip with essentially zero elapsed real time.
            Vector3 shotPos = context.Ship.transform.position + context.Ship.transform.forward * 5000f;
            ProjectileObject.Spawn(ProjectileKind.EnemySiege, 1, "test-enemy", shotPos, Vector3.forward * ProjectileCatalog.SiegeProjectileSpeed,
                ProjectileCatalog.SiegeLifetimeSeconds, null, director, ProjectileFaction.Hostile,
                interceptable: true, interceptionDurability: ProjectileCatalog.InterceptionDurability, damageAmount: 100f);
            yield return null;

            SystemDelta delta = context.Streaming.CaptureCurrentFullDelta();
            Assert.AreEqual(1, delta.pendingAttacks.Count);
            string attackId = delta.pendingAttacks[0].attackId;
            float capturedRemaining = delta.pendingAttacks[0].remainingFlightSeconds;
            Assert.Greater(capturedRemaining, 15f, "sanity: a fresh Siege shot's remaining lifetime must still be close to its full ~20s budget");
            StarSystemRegistry.Deltas.Set(delta);

            WorldIdCodec.TryParseSystemId(startSystemId, out SectorCoordinate sector, out _);
            SystemManifest neighborManifest = RequireNeighbourOf(startSystemId, sector);
            context.Ship.transform.position = neighborManifest.AbsoluteCenter - FloatingOrigin.TotalOffset;
            context.Streaming.ResetAndBootFull(neighborManifest);
            yield return null;
            yield return null;

            // Promote straight back before any real remote tick has had a chance to resolve it.
            context.Ship.transform.position = startManifest.AbsoluteCenter - FloatingOrigin.TotalOffset;
            context.Streaming.ResetAndBootFull(startManifest);
            yield return null;
            yield return null;

            ProjectileObject restored = null;
            foreach (ProjectileObject p in Object.FindObjectsOfType<ProjectileObject>()) if (p.State.ProjectileId == attackId) restored = p;
            Assert.IsNotNull(restored, "an unresolved pending attack must be rematerialized as a real live projectile on promotion, same attack ID");
            Assert.Greater(restored.State.RemainingLifetimeSeconds, 0f);
            Assert.LessOrEqual(restored.State.RemainingLifetimeSeconds, capturedRemaining + 0.01f,
                "the restored projectile must carry forward its real remaining flight time, never a fresh full budget");

            SystemDelta reloaded = StarSystemRegistry.Deltas.TryGet(startSystemId);
            Assert.AreEqual(0, reloaded.pendingAttacks.Count, "once rematerialized as a live projectile, it must never also linger as a delta-side pending attack");
        }

        [UnityTest]
        public IEnumerator RemoteSystem_ActiveLaserDefense_DamagesEnemyThroughRealBackgroundScheduler()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            string startSystemId = context.StartSystemId;
            SystemManifest startManifest = StarSystemRegistry.Manifests.ResolveSystem(startSystemId);
            WorldIdCodec.TryParseSystemId(startSystemId, out SectorCoordinate sector, out _);
            string remoteSystemId = RequireNeighbourOf(startSystemId, sector).SystemId;
            SystemManifest remoteManifest = StarSystemRegistry.Manifests.ResolveSystem(remoteSystemId);
            string planetId = remoteManifest.Planets[0].PlanetId;
            foreach (PlanetManifest p in remoteManifest.Planets) ExtractionState.GetDominance(p.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);

            var delta = new SystemDelta { systemId = remoteSystemId };
            delta.structures.Add(new StructureDto
            {
                worldId = planetId, nodeId = "defense.0", type = (int)SurfaceStructureType.Laser, state = (int)DrillLifecycle.Active, tier = 1,
                shield = 0f, armor = SurfaceStructureCatalog.Laser.ArmorMax, hull = SurfaceStructureCatalog.Laser.HullMax
            });
            // RemoteRaidContext.LoadFromDelta always builds Durability with the catalog def's own
            // max (FighterMk1: 120/150/220) regardless of what's put in the DTO, so an oversized hull
            // figure here would just get clamped back down -- can't out-tank the real cap this way.
            delta.enemies.Add(new EnemyDto { enemyId = "remote-enemy-e1", defId = EnemyIds.FighterMk1, shield = 0f, armor = 0f, hull = 220f, aiPhase = (int)EnemyAiPhase.AttackOrScan });
            var pressureDto = new ThreatPressureDto { phase = (int)RaidPhase.Active, stage = (int)RaidStage.Assault, targetPlanetWorldId = planetId, encounterSeed = "1" };
            pressureDto.spawnedEnemyIds.Add("remote-enemy-e1");
            delta.threatPressure = pressureDto;
            StarSystemRegistry.Deltas.Set(delta);

            // Rebooting the currently-Full start system in place re-runs RepopulateSparseSystemsFromDeltas,
            // which is the only thing that ever adds a delta to the real 1 Hz remote-tick set outside the
            // normal travel-triggered demote path -- the same mechanism a save/load already relies on.
            context.Streaming.ResetAndBootFull(startManifest);
            yield return null;
            yield return null;

            // No timeScale acceleration here on purpose: the scheduler's own real-time sparseTickTimer
            // needs >=1s of accumulated real time to fire even one dispatch, and a T1 Laser's DPS (with
            // DamageProfile.Laser's 1.25x Hull multiplier) would kill this Fighter's real 220-hull cap in
            // well under 2 dispatches -- an accelerated multi-dispatch window would just race it to death
            // (a dead enemy is then correctly dropped from delta.enemies, which is a different, harder
            // thing to assert cleanly here). ~1.3 real seconds reliably yields exactly one dispatch.
            float realStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - realStart < 1.3f) yield return null;

            SystemDelta reloaded = StarSystemRegistry.Deltas.TryGet(remoteSystemId);
            Assert.IsNotNull(reloaded);
            EnemyDto enemyAfter = reloaded.enemies.Find(e => e.enemyId == "remote-enemy-e1");
            Assert.IsNotNull(enemyAfter, "one Laser dispatch (~125 effective damage) must not be enough to kill a real 220-hull Fighter Mk I");
            Assert.Less(enemyAfter.hull, 220f, "an active planetary Laser must genuinely damage the enemy through the real background remote-tick scheduler (SystemStreamingDirector.TickRemoteSlice), not just in an isolated unit test");
        }

        [UnityTest]
        public IEnumerator InvalidRemoteRaidTarget_ProducesZeroRuntimeEnemies_ViaRealScheduler()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            string startSystemId = context.StartSystemId;
            SystemManifest startManifest = StarSystemRegistry.Manifests.ResolveSystem(startSystemId);
            WorldIdCodec.TryParseSystemId(startSystemId, out SectorCoordinate sector, out _);
            string remoteSystemId = RequireNeighbourOf(startSystemId, sector).SystemId;

            var delta = new SystemDelta { systemId = remoteSystemId };
            var pressureDto = new ThreatPressureDto { phase = (int)RaidPhase.Spawning, stage = (int)RaidStage.Pirate, targetPlanetWorldId = remoteSystemId + "/planet.99", encounterSeed = "1" };
            pressureDto.compositionDefIds.Add(EnemyIds.FighterMk1);
            pressureDto.compositionDefIds.Add(EnemyIds.FighterMk1);
            delta.threatPressure = pressureDto;
            StarSystemRegistry.Deltas.Set(delta);

            context.Streaming.ResetAndBootFull(startManifest);
            yield return null;
            yield return null;

            // Bounded by real elapsed time so the real background scheduler actually gets to run
            // this through at least one full tick, not just trivially pass because nothing ran at all.
            Time.timeScale = 20f;
            float realStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - realStart < 6f) yield return null;
            Time.timeScale = 1f;

            SystemDelta reloaded = StarSystemRegistry.Deltas.TryGet(remoteSystemId);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(0, reloaded.enemies.Count, "an invalid raid target must produce zero runtime enemies, remote or otherwise");
            Assert.AreEqual(0, Object.FindObjectsOfType<EnemyAgent>().Length, "and certainly never a live materialized one in the currently-Full system");
        }
    }
}
