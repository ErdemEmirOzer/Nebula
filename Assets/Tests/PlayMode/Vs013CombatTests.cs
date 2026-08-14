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
    /// PlayMode integration tests for VS-013 active combat (§11): real scene
    /// boot with combat systems attached, AI physics/movement in the open
    /// world, combat open/close timing, Full-&gt;demote-&gt;Full encounter
    /// continuity, save/load persistence, no duplicate components after
    /// restart, and DT-001 speed non-interference at the MonoBehaviour
    /// level. No keyboard/mouse simulation — every behavior is driven
    /// through the same public, input-free APIs a keypress would call,
    /// exactly like BootstrapSmokeTests/FlightTestSpeedTests.
    /// </summary>
    public class Vs013CombatTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1; // ExtractionDirector's own hardcoded save slot

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
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
        {
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

        [UnityTest]
        public IEnumerator SceneBoots_AllCombatSystemsPresentAndWiredOnTheShip()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            ShipController ship = adapter.Context.Ship;

            var combat = ship.GetComponent<ShipCombat>();
            Assert.IsNotNull(combat, "VS-013 ShipCombat missing from the ship");
            Assert.IsNotNull(combat.State, "a fresh campaign must have already called InitializeNewCampaign");
            Assert.AreEqual(400f, combat.State.Durability.Shield, "a freshly commissioned ship must start at FULL shield, not 0");
            Assert.AreEqual(600f, combat.State.Durability.Armor);
            Assert.AreEqual(1000f, combat.State.Durability.Hull);
            Assert.AreEqual(100f, combat.State.Energy);

            Assert.AreEqual(5, combat.Hardpoints.Length, "all 5 hull-stage-V hardpoint slots must be allocated even though only some are active this early");
            Assert.IsFalse(combat.Hardpoints[0].IsEmpty, "hardpoint 0 must carry the free, fixed, pre-equipped T1 Laser");
            Assert.AreEqual(WeaponFamily.Laser, combat.Hardpoints[0].Weapon);
            Assert.AreEqual(1, combat.Hardpoints[0].Tier);
            Assert.AreEqual(1, combat.ActiveHardpointCount, "T0 Hull Reinforcement + T0 Weapon Coordination -> exactly 1 active slot");

            Assert.IsNotNull(ship.GetComponent<ShipEmergencyRecovery>(), "VS-013 ShipEmergencyRecovery missing from the ship");

            Assert.AreEqual(1, Object.FindObjectsOfType<CombatCampaignDirector>().Length, "exactly one campaign-session combat director");
            Assert.AreEqual(1, Object.FindObjectsOfType<TrainingArea>().Length, "exactly one training area");
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.UI.Hud.NebulaHudVitals>(true).Length, "exactly one combat HUD");

            Assert.IsNotNull(adapter.Context.Streaming.CombatEncounter, "the start system boots Full -- its combat encounter director must exist immediately");
            Assert.IsFalse(combat.InCombat, "a fresh campaign must never start already in combat");
        }

        [UnityTest]
        public IEnumerator RestartCampaign_NoDuplicateCombatComponents_FreshCombatStateNotStaleDamage()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            ShipController oldShip = adapter.Context.Ship;
            oldShip.GetComponent<ShipCombat>().ApplyDamage(300f); // dent the shield so a stale-state carry-over would be observable
            var oldDirector = Object.FindObjectOfType<CombatCampaignDirector>();
            var oldTrainingArea = Object.FindObjectOfType<TrainingArea>();
            var oldHud = Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudVitals>();

            adapter.RestartCampaign();
            yield return null;
            yield return null;

            Assert.IsTrue(oldDirector == null, "the old combat campaign director must be destroyed by dispose");
            Assert.IsTrue(oldTrainingArea == null, "the old training area must be destroyed by dispose");
            Assert.IsTrue(oldHud == null, "the old combat HUD must be destroyed by dispose");

            Assert.AreEqual(1, Object.FindObjectsOfType<CombatCampaignDirector>().Length, "no duplicate combat campaign director after restart");
            Assert.AreEqual(1, Object.FindObjectsOfType<TrainingArea>().Length, "no duplicate training area after restart");
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.UI.Hud.NebulaHudVitals>(true).Length, "no duplicate combat HUD after restart");

            ShipController newShip = adapter.Context.Ship;
            Assert.AreNotSame(oldShip, newShip);
            var newCombat = newShip.GetComponent<ShipCombat>();
            Assert.AreEqual(400f, newCombat.State.Durability.Shield, "a restart must never carry the old campaign's combat damage forward");

            Assert.AreEqual(FirstProbeStatus.NotScheduled, CombatCampaignState.FirstProbe.Status, "a restart must never carry the old campaign's Probe warning state forward");
        }

        [UnityTest]
        public IEnumerator SpawnedFighter_MovesPhysicallyEveryFixedUpdate_NeverTeleports()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            CombatEncounterDirector director = context.Streaming.CombatEncounter;
            Assert.IsNotNull(director);

            Vector3 spawnPos = context.Ship.transform.position + context.Ship.transform.forward * 3000f;
            EnemyAgent enemy = director.SpawnEnemy(EnemyIds.FighterMk1, spawnPos);
            Assert.IsNotNull(enemy, "SpawnEnemy must succeed with a real ship + director present");
            Assert.AreEqual(enemy, EnemyAgent.Find(enemy.Id), "the static registry must resolve the freshly spawned enemy by id");

            Vector3 lastPos = enemy.transform.position;
            bool everMoved = false;
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForFixedUpdate();
                if (enemy == null) break; // safety: a Fighter this close could in principle already resolve, though not expected within 20 physics ticks
                float step = Vector3.Distance(enemy.transform.position, lastPos);
                // EnemyCatalog.FighterMk1 speed is 200 u/s; a single FixedUpdate step (default 0.02s) can move
                // at most a small bounded distance -- a teleport/instant-snap would blow past this by orders of magnitude.
                Assert.LessOrEqual(step, 200f * Time.fixedDeltaTime * 3f, "must never move further in one physics tick than its own top speed allows (no teleport)");
                if (step > 0.0001f) everMoved = true;
                lastPos = enemy.transform.position;
            }
            Assert.IsTrue(everMoved, "a live enemy with a real player target must actually move under its own AI, not sit frozen");
        }

        [UnityTest]
        public IEnumerator CombatEncounter_OpensOnThreatEvent_ClosesAfter8SecondsOfSilence()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            var combat = context.Ship.GetComponent<ShipCombat>();
            CombatEncounterDirector director = context.Streaming.CombatEncounter;

            Assert.IsFalse(combat.InCombat);
            director.Encounter.RegisterThreatEvent();
            yield return null;
            Assert.IsTrue(combat.InCombat, "InCombat must flip true the same frame a threat event is registered");

            yield return new WaitForSeconds(8.3f);
            Assert.IsFalse(combat.InCombat, "combat must close exactly ~8s after the last threat event with no new ones");
        }

        [UnityTest]
        public IEnumerator LoadoutChange_BlockedWhileInCombat_AllowedOnceCombatCloses()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            var combat = context.Ship.GetComponent<ShipCombat>();
            CombatEncounterDirector director = context.Streaming.CombatEncounter;

            director.Encounter.RegisterThreatEvent();
            yield return null;
            Assert.IsTrue(combat.InCombat);

            WeaponProductionRejectReason reject = combat.TryStartProduction(WeaponFamily.Laser, 1);
            Assert.AreEqual(WeaponProductionRejectReason.CombatActive, reject, "loadout/production must be blocked the instant combat is open, before any resource check");

            yield return new WaitForSeconds(8.3f);
            Assert.IsFalse(combat.InCombat);

            WeaponProductionRejectReason rejectOutside = combat.TryStartProduction(WeaponFamily.Laser, 1);
            Assert.AreNotEqual(WeaponProductionRejectReason.CombatActive, rejectOutside, "outside combat the CombatActive rejection must never fire (resources may still be insufficient, which is a different, expected reason)");
        }

        [UnityTest]
        public IEnumerator SaveThenLoad_ShipCombatDamageAndHardpointsSurvive_NoDuplicationOrReroll()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            var combat = context.Ship.GetComponent<ShipCombat>();

            combat.ApplyDamage(150f); // Shield 400 -> 250
            combat.Hardpoints[1].Equip(WeaponFamily.Plasma, 1);

            context.ExtractionDirector.SaveNow(null);
            yield return null;

            context.ExtractionDirector.LoadNow();
            yield return null;

            var reloadedCombat = context.Ship.GetComponent<ShipCombat>();
            Assert.AreEqual(250f, reloadedCombat.State.Durability.Shield, 0.5f, "combat damage must survive a save/load round trip, never silently reset to full");
            Assert.IsFalse(reloadedCombat.Hardpoints[1].IsEmpty, "the manually-equipped hardpoint must survive the round trip");
            Assert.AreEqual(WeaponFamily.Plasma, reloadedCombat.Hardpoints[1].Weapon);

            // A second save/load cycle must never duplicate or re-roll anything further.
            context.ExtractionDirector.SaveNow(null);
            yield return null;
            context.ExtractionDirector.LoadNow();
            yield return null;
            var reloadedAgain = context.Ship.GetComponent<ShipCombat>();
            Assert.AreEqual(250f, reloadedAgain.State.Durability.Shield, 0.5f);
        }

        [UnityTest]
        public IEnumerator FullDemoteFullCycle_CombatEncounterAndSpawnedEnemy_SurviveWithoutDuplication()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            string startSystemId = context.StartSystemId;
            SystemManifest startManifest = StarSystemRegistry.Manifests.ResolveSystem(startSystemId);

            CombatEncounterDirector directorBefore = context.Streaming.CombatEncounter;
            Vector3 spawnPos = context.Ship.transform.position + context.Ship.transform.forward * 3000f;
            EnemyAgent enemy = directorBefore.SpawnEnemy(EnemyIds.FighterMk1, spawnPos);
            Assert.IsNotNull(enemy);
            string enemyId = enemy.Id;

            // Same capture-then-reboot pattern ExtractionDirector.LoadNow itself uses for a
            // real save/load round trip -- the public seam for exercising the per-system
            // CaptureCombat -> MaterializeCombat continuity path without simulating ship travel.
            SystemDelta delta = context.Streaming.CaptureCurrentFullDelta();
            Assert.IsNotNull(delta);
            Assert.AreEqual(1, delta.enemies.Count, "the live Fighter must have been captured into the system delta");
            StarSystemRegistry.Deltas.Set(delta);

            context.Streaming.ResetAndBootFull(startManifest);
            yield return null;
            yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<CombatEncounterDirector>().Length, "the reboot must never leave a second combat encounter authority");
            CombatEncounterDirector directorAfter = context.Streaming.CombatEncounter;
            Assert.IsNotNull(directorAfter);
            Assert.AreNotSame(directorBefore, directorAfter, "a genuinely fresh materialized-system-lifetime director must be created, not reused stale");

            EnemyAgent restored = EnemyAgent.Find(enemyId);
            Assert.IsNotNull(restored, "the captured enemy must be restored by MaterializeCombat after the reboot");
            Assert.AreEqual(1, Object.FindObjectsOfType<EnemyAgent>().Length, "exactly one enemy after the round trip -- never duplicated");
        }

        [UnityTest]
        public IEnumerator HundredXTestSpeed_NeverChangesCombatEnergyRegenRate()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            var combat = context.Ship.GetComponent<ShipCombat>();

            combat.State.TryConsumeEnergy(80f); // Energy 100 -> 20, leaves headroom to observe real regen
            float energyAt1x = combat.State.Energy;
            context.Ship.SetTestSpeedMultiplier(100f);

            const int ticks = 10;
            for (int i = 0; i < ticks; i++) yield return new WaitForFixedUpdate();

            float regenAt100xSpeed = combat.State.Energy - energyAt1x;
            // Independently compute the exact expected regen for `ticks` real FixedUpdate
            // steps at T0's 15/s rate in Safe space, to prove the 100x ship-speed slider
            // did not scale it in either direction.
            float expectedRegen = 15f * Time.fixedDeltaTime * ticks;
            Assert.AreEqual(expectedRegen, regenAt100xSpeed, expectedRegen * 0.25f + 0.5f,
                "DT-001's test speed multiplier must never accelerate or slow combat energy regen -- only commanded ship movement speed");
        }
    }
}
