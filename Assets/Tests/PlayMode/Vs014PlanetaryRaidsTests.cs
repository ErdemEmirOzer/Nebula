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
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for VS-014 planetary defense and raids
    /// (§18): real scene boot, physical spawn of the four new enemy roles
    /// through the live materialization pipeline, planetary Laser/Plasma
    /// actually damaging an enemy through the shared ICombatTarget/
    /// DamageProfile path, the raid scheduler driving real RaidPhase
    /// transitions, save/load round-tripping active raid/pressure state,
    /// and Pirate Base materializing as a real physical GameObject. No
    /// keyboard/mouse simulation — every behavior is driven through the
    /// same public, input-free APIs a keypress or AI decision would call,
    /// exactly like Vs013CombatTests.
    /// </summary>
    public class Vs014PlanetaryRaidsTests
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
        public void BackupRealGameplaySlot()
        {
            slot1Backup.Clear();
            foreach (string f in Slot1Files())
                slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
            CombatCampaignState.Reset();
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
        {
            foreach (var kv in slot1Backup)
            {
                if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                else File.WriteAllText(kv.Key, kv.Value);
            }
            CombatCampaignState.Reset();
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

        /// <summary>
        /// The world id of any planet the boot system actually materialized. A raid needs a
        /// target that <see cref="ExtractionState.FindPlanet"/> can resolve to a live
        /// <c>CelestialBody</c> — an invented id sends SpawnComposition straight to
        /// RetreatCleanup, which is a fine production behavior and a useless test premise.
        /// </summary>
        private static string FirstMaterializedPlanetWorldId()
        {
            foreach (PlanetIdentity planet in ExtractionState.AllPlanets) return planet.WorldId;
            return null;
        }

        /// <summary>Live entries in the shared enemy registry — the same view <see cref="RaidScheduler"/>'s own "is anything still alive" check uses.</summary>
        private static int CountLiveEnemyAgents()
        {
            int count = 0;
            foreach (EnemyAgent agent in EnemyAgent.All.Values) if (agent != null && !agent.Destroyed) count++;
            return count;
        }

        /// <summary>
        /// Physical <see cref="EnemyAgent"/> objects of one def, counted in the scene rather
        /// than in the registry — the registry is keyed by id, so a duplicate restored on top
        /// of a surviving spawn would silently overwrite its own entry and count as one.
        /// Inactive objects are included deliberately: a duplicate that happens to be
        /// disabled is still a duplicate.
        /// </summary>
        private static int CountPhysicalEnemyObjects(string defId)
        {
            int count = 0;
            foreach (EnemyAgent agent in Object.FindObjectsOfType<EnemyAgent>(true))
                if (agent != null && !agent.Destroyed && agent.Def != null && agent.Def.Id == defId) count++;
            return count;
        }

        [UnityTest]
        public IEnumerator EveryNewEnemyRole_SpawnsPhysically_AndResolvesInTheRegistry()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            CombatEncounterDirector director = context.Streaming.CombatEncounter;
            Assert.IsNotNull(director);

            Vector3 basePos = context.Ship.transform.position + context.Ship.transform.forward * 4000f;
            string[] defIds = { EnemyIds.LooterTransportMk1, EnemyIds.EmDisruptorMk1, EnemyIds.HeavyAssaultMk1, EnemyIds.SiegeMk1 };
            foreach (string defId in defIds)
            {
                EnemyAgent agent = director.SpawnEnemy(defId, basePos + Random.insideUnitSphere * 200f);
                Assert.IsNotNull(agent, $"SpawnEnemy must succeed for {defId}");
                Assert.AreEqual(agent, EnemyAgent.Find(agent.Id), $"the static registry must resolve {defId} by id");
                Assert.AreEqual(EnemyCatalog.Get(defId).Role, agent.Def.Role);
            }
        }

        [UnityTest]
        public IEnumerator PlanetaryLaser_PhysicallyDamagesASpawnedEnemy_ThroughTheSharedDamageProfilePath()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            CombatEncounterDirector director = context.Streaming.CombatEncounter;

            // The assertion below is about the shared ICombatTarget/DamageResolver path
            // itself (the same one TickDefenseCombat's real Laser fire routes through) --
            // it never actually needs a physically-built Laser structure to exist, only a
            // real spawn position, so this never depends on the boot scene's fixture state.
            EnemyAgent enemy = director.SpawnEnemy(EnemyIds.FighterMk1, context.Ship.transform.position + context.Ship.transform.forward * 3000f);
            Assert.IsNotNull(enemy);
            float shieldBefore = enemy.Combat.Shield;

            // Directly exercise the same shared ICombatTarget/DamageResolver path TickDefenseCombat itself uses.
            enemy.ApplyDamage(50f, DamageProfile.Laser);
            Assert.Less(enemy.Combat.Shield, shieldBefore, "Laser damage through the shared DamageProfile path must actually reduce the enemy's shield");
        }

        [UnityTest]
        public IEnumerator DefenseCoordinator_CycleModeAndFocus_AffectSharedTargetSelection()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            CombatEncounterDirector director = context.Streaming.CombatEncounter;

            Vector3 pos = context.Ship.transform.position + context.Ship.transform.forward * 2000f;
            EnemyAgent target = director.SpawnEnemy(EnemyIds.FighterMk1, pos);
            Assert.IsNotNull(target);

            director.DefenseCoordinator.CycleMode("planet.test"); // -> Balanced
            director.DefenseCoordinator.CycleMode("planet.test"); // -> FocusTarget
            Assert.AreEqual(DefenseMode.FocusTarget, director.DefenseCoordinator.GetMode("planet.test"));

            director.DefenseCoordinator.SetFocus("planet.test", target.Id);
            EnemyAgent resolved = director.DefenseCoordinator.ResolveSharedTarget("planet.test", true, SurfaceStructureType.Laser,
                new List<EnemyAgent> { target }, pos, 0.1f);
            Assert.AreEqual(target, resolved, "Focus Target mode must lock onto the manually-focused enemy");
        }

        [UnityTest]
        public IEnumerator RaidScheduler_DrivesRealPhaseTransitions_ScheduledThroughActive()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            CombatEncounterDirector director = context.Streaming.CombatEncounter;

            // A real planet is required: SpawnComposition looks the target planet up via
            // ExtractionState.FindPlanet and aborts to RetreatCleanup for an unknown id --
            // a fake "planet.test" id would never actually reach Active/spawn anything.
            string realPlanetWorldId = FirstMaterializedPlanetWorldId();
            Assert.IsNotNull(realPlanetWorldId, "the boot scene's start system must have at least one materialized planet");

            director.Pressure.ScheduleRaid(RaidStage.Pirate, RaidDoctrine.Loot, realPlanetWorldId,
                new List<string> { EnemyIds.FighterMk1 }, 12345, warningSeconds: 0.05f);
            Assert.AreEqual(RaidPhase.Scheduled, director.Pressure.Phase);

            RaidScheduler.Tick(director, 0.02f); // Scheduled -> Warning
            Assert.AreEqual(RaidPhase.Warning, director.Pressure.Phase);

            RaidScheduler.Tick(director, 0.2f); // warning expires -> Spawning
            Assert.AreEqual(RaidPhase.Spawning, director.Pressure.Phase);

            RaidScheduler.Tick(director, 0.02f); // Spawning -> Active (physical spawn happens here)
            Assert.AreEqual(RaidPhase.Active, director.Pressure.Phase);
            Assert.AreEqual(1, director.Pressure.SpawnedEnemyIds.Count, "the composed Fighter must have actually been physically spawned");
        }

        /// <summary>
        /// The round trip a real raid actually has to survive: not a phase enum
        /// written by hand, but a raid that was scheduled, warned, spawned and
        /// became <see cref="RaidPhase.Active"/> the only way production ever
        /// reaches Active — through <see cref="RaidScheduler"/>, against a real
        /// planet, with a real enemy physically standing in the world.
        /// <para>
        /// That premise is not decoration; it is the whole point. This test used
        /// to set <c>Phase = Active</c> directly and never spawn anything, and it
        /// only passed because the scheduler was dead: every load left
        /// <see cref="StarSystemRegistry.Streaming"/> null, so
        /// <c>CombatCampaignDirector</c> ticked <c>RaidScheduler</c> with a null
        /// director and the hand-written phase simply froze there. Repairing the
        /// registry woke the scheduler up, and it immediately gave the correct
        /// answer about a raid with no raiders — <c>TickActive</c>'s
        /// <c>!anyAlive</c> branch retired it to <c>RetreatCleanup</c>. A test
        /// that a working scheduler fails is a test describing a world that
        /// cannot exist, so the premise is rebuilt rather than the assertion
        /// relaxed.
        /// </para>
        /// <para>
        /// Two things are being asserted at once and both matter. The obvious one
        /// is that raid identity, target, pressure/participation state and the
        /// physical raider all survive the real <c>SaveService</c> round trip
        /// unchanged and un-duplicated. The less obvious one is the last block:
        /// the raid's only enemy is destroyed after the load, and the raid must
        /// then retire on its own. Nothing in the test can move the phase, so a
        /// raid still sitting at Active there means the scheduler is not running
        /// against the reloaded director — the exact defect the registry fix
        /// addressed, caught in the vocabulary a player would notice it in
        /// ("raids stopped ending after I loaded").
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator SaveThenLoad_ActiveRaidPressureState_SurvivesRoundTrip_NoRerollOrDuplication()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            CombatEncounterDirector director = context.Streaming.CombatEncounter;

            string targetWorldId = FirstMaterializedPlanetWorldId();
            Assert.IsNotNull(targetWorldId, "the boot scene's start system must have at least one materialized planet");

            // Scheduled -> Warning -> Spawning -> Active, driven the same way
            // RaidScheduler_DrivesRealPhaseTransitions above drives it, and all inside a
            // single frame: CombatCampaignDirector also ticks the scheduler once per frame,
            // so yielding mid-sequence would leave which tick performed which transition
            // ambiguous. The physical spawn happens inside the Spawning tick.
            director.Pressure.ScheduleRaid(RaidStage.Assault, RaidDoctrine.Suppression, targetWorldId,
                new List<string> { EnemyIds.HeavyAssaultMk1 }, 555, warningSeconds: 0.05f);
            RaidScheduler.Tick(director, 0.02f); // Scheduled -> Warning
            RaidScheduler.Tick(director, 0.2f);  // warning expires -> Spawning
            RaidScheduler.Tick(director, 0.02f); // Spawning -> physical spawn -> Active

            Assert.AreEqual(RaidPhase.Active, director.Pressure.Phase, "the raid must reach Active through the scheduler, never by assignment");
            Assert.AreEqual(1, director.Pressure.SpawnedEnemyIds.Count, "Spawning must have physically composed exactly the one Heavy Assault");

            string raiderId = director.Pressure.SpawnedEnemyIds[0];
            EnemyAgent raider = EnemyAgent.Find(raiderId);
            Assert.IsNotNull(raider, "the raid's enemy must be a real live agent in the runtime, not just a recorded id");
            Assert.IsFalse(raider.Destroyed);

            // Real damage through the shared ICombatTarget/DamageResolver path -- enough to
            // strip the shield and bite into armor, nowhere near Heavy Assault's 15% hull
            // retreat threshold, so the raider stays engaged across the round trip. Armor and
            // hull never regenerate (only shield does, and only 8s after the last hit), which
            // is exactly what makes them safe to compare for equality after the load.
            raider.ApplyDamage(500f);
            Assert.AreEqual(0f, raider.Combat.Shield, 0.01f, "500 damage must have taken the whole 300-point shield");
            Assert.Less(raider.Combat.Armor, raider.Combat.ArmorMax, "the overflow must actually have reached armor");

            // The same public, input-free call the target-select keypress makes. Production
            // routes it through ShipCombat.RegisterThreat, which is the ONE place a §13
            // meaningful-participation signal is recorded during an Active raid -- so the
            // guard asserted after the load below was set by the game, not by the test
            // reaching past it for the setter.
            Assert.IsTrue(context.ShipCombat.TrySelectNearestTarget(), "the ship must be able to lock the raider");
            Assert.AreEqual(raiderId, context.ShipCombat.Target.Id, "the raid's own enemy is the only one alive in this system");
            Assert.IsTrue(director.Pressure.PlayerDealtMeaningfulDamage,
                "engaging a raider during an Active raid must record meaningful participation through the live ship");

            // One real frame at Active before the save: it lets the per-frame scheduler tick
            // confirm a raid WITH a live raider is left alone, and it starts the active-raid
            // clock, so ElapsedActiveSeconds below is real elapsed gameplay rather than zero.
            yield return null;
            Assert.AreEqual(RaidPhase.Active, director.Pressure.Phase, "a raid with a live raider must not retire");

            RaidStage? stageBefore = director.Pressure.Stage;
            RaidDoctrine? doctrineBefore = director.Pressure.Doctrine;
            ulong seedBefore = director.Pressure.EncounterSeed;
            float pressureBefore = director.Pressure.Pressure;
            float graceBefore = director.Pressure.GraceRemainingActiveMinutes;
            float elapsedActiveBefore = director.Pressure.ElapsedActiveSeconds;
            float armorBefore = raider.Combat.Armor;
            float hullBefore = raider.Combat.Hull;
            int liveEnemiesBefore = CountLiveEnemyAgents();
            int physicalRaidersBefore = CountPhysicalEnemyObjects(EnemyIds.HeavyAssaultMk1);
            Assert.Greater(elapsedActiveBefore, 0f, "the active-raid clock must have been running before the save");
            Assert.AreEqual(1, physicalRaidersBefore, "exactly one Heavy Assault should physically exist before the save");

            context.ExtractionDirector.SaveNow(null);
            yield return null;
            context.ExtractionDirector.LoadNow();
            yield return null;

            // A load re-materializes the current system, so this is a genuinely new director
            // rebuilt from the save -- never the same instance carried over.
            CombatEncounterDirector reloaded = context.Streaming.CombatEncounter;
            ThreatPressureState pressure = reloaded.Pressure;

            Assert.AreEqual(RaidPhase.Active, pressure.Phase, "an active raid's phase must survive a save/load round trip exactly");
            Assert.AreEqual(stageBefore, pressure.Stage);
            Assert.AreEqual(doctrineBefore, pressure.Doctrine);
            Assert.AreEqual(targetWorldId, pressure.TargetPlanetWorldId, "the raid must still be aimed at the same real planet");
            Assert.AreEqual(seedBefore, pressure.EncounterSeed,
                "the encounter seed must round-trip exactly -- a reroll here would desync composition/salvage determinism");
            Assert.AreEqual(pressureBefore, pressure.Pressure, 0.001f);
            Assert.AreEqual(graceBefore, pressure.GraceRemainingActiveMinutes, 0.001f);
            Assert.GreaterOrEqual(pressure.ElapsedActiveSeconds, elapsedActiveBefore,
                "the active-raid clock must resume from where it was saved, never restart at zero");
            Assert.IsTrue(pressure.PlayerDealtMeaningfulDamage, "meaningful-participation guard must survive the round trip, never reset");
            CollectionAssert.AreEqual(new[] { EnemyIds.HeavyAssaultMk1 }, pressure.CompositionDefIds,
                "the composition recipe must be restored verbatim, never recomposed");
            CollectionAssert.AreEqual(new[] { raiderId }, pressure.SpawnedEnemyIds,
                "the raid must still own the same physical enemy, by the same id -- a re-spawn would mint a new one");

            EnemyAgent reloadedRaider = EnemyAgent.Find(raiderId);
            Assert.IsNotNull(reloadedRaider, "the raid's enemy must come back as a live agent under its original id");
            Assert.IsFalse(reloadedRaider.Destroyed);
            Assert.AreEqual(armorBefore, reloadedRaider.Combat.Armor, 0.01f,
                "the raider's accumulated damage must round-trip -- a fresh full-health spawn would be a silent reroll");
            Assert.AreEqual(hullBefore, reloadedRaider.Combat.Hull, 0.01f);
            Assert.AreSame(reloadedRaider, context.ShipCombat.Target, "the ship's saved lock must resolve back to the same restored agent");
            Assert.AreEqual(liveEnemiesBefore, CountLiveEnemyAgents(), "the load changed how many enemies are alive");
            Assert.AreEqual(physicalRaidersBefore, CountPhysicalEnemyObjects(EnemyIds.HeavyAssaultMk1),
                "the load duplicated the raid's physical enemy -- restore ran on top of a spawn that was never cleared");

            // --- the scheduler must be genuinely running against the RELOADED director ----
            // State that merely looks right is not proof of life; only a raid that reacts is.
            // Destroying the raid's only enemy is the one input that must make a running
            // scheduler act (TickActive's !anyAlive branch retires the raid), and nothing
            // else in this test can move Phase -- so still-Active below means nothing ticks.
            Assert.AreSame(reloaded, StarSystemRegistry.Streaming.CombatEncounter,
                "the scheduler ticks whatever the registry publishes, so the registry must hold this director");
            reloadedRaider.ApplyDamage(reloadedRaider.Combat.Armor + reloadedRaider.Combat.Hull + 1f);
            Assert.IsTrue(reloadedRaider.Destroyed, "the raider must actually be destroyed for the retire check to mean anything");

            float waited = 0f;
            while (pressure.Phase == RaidPhase.Active && waited < 2f) { waited += Time.deltaTime; yield return null; }
            Assert.AreNotEqual(RaidPhase.Active, pressure.Phase,
                "the raid scheduler is not ticking after the load: an Active raid whose last raider is dead never retired");
        }

        [UnityTest]
        public IEnumerator PirateBase_MaterializesPhysically_WhenTheCampaignLedgerSaysItWasSpawnedHere()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            string systemId = context.Streaming.CombatEncounter.SystemId;

            CombatCampaignState.RaidCampaign.MarkPirateBaseSpawned("poi.pirate_base", systemId);

            // Save (captures the ledger just set) then Load -- LoadNow re-materializes the
            // current system's CombatEncounterDirector fresh, exactly like a real system
            // re-entry, without wiping campaign-lifetime state the way a brand-new-campaign
            // scene boot would (that only happens via WorldInstaller's own Reset path).
            context.ExtractionDirector.SaveNow(null);
            yield return null;
            context.ExtractionDirector.LoadNow();
            yield return null;

            CombatEncounterDirector director = context.Streaming.CombatEncounter;
            Assert.IsNotNull(director.PirateBase, "the Pirate Base must physically materialize once the campaign ledger says it belongs to this system");
            Assert.IsFalse(director.PirateBase.Resolved);
        }
    }
}
