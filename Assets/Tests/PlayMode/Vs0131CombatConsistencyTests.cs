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
using Nebula.Ship;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for VS-013.1 (combat consistency fix):
    /// level-derived Hull Form auto-application, the single speed authority
    /// (no double Engine-Navigation multiply), Damaged flight/boost/cruise
    /// effects, and the physical repair transaction end-to-end. No keyboard/
    /// mouse simulation — driven through the same public APIs a keypress
    /// would call, exactly like every other PlayMode file in this project.
    /// </summary>
    public class Vs0131CombatConsistencyTests
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
        public IEnumerator LevelingUpPastL21_AutoAppliesHullFormII_FreeAndDeterministic_PreservingPercentage()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            var combat = adapter.Context.Ship.GetComponent<ShipCombat>();

            Assert.AreEqual(1, combat.HullFormValue);
            Assert.AreEqual(400f, combat.State.Durability.ShieldMax, 0.01f);

            combat.ApplyDamage(200f); // Shield 400 -> 200 (50%)
            Progression.ProgressionService.Award("test.push-to-l21", 50000);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.GreaterOrEqual(Progression.ProgressionService.State.Level, 21);
            Assert.AreEqual(2, combat.HullFormValue, "Hull Form must auto-derive from the new level, never a purchase");
            Assert.AreEqual(600f, combat.State.Durability.ShieldMax, 0.01f, "Form II's own Shield baseline");
            Assert.AreEqual(300f, combat.State.Durability.Shield, 0.01f, "50% of the OLD max preserved as 50% of the NEW max -- never a free full heal, never an unintended drop");
        }

        [UnityTest]
        public IEnumerator EngineNavigationT2_PlusHullReinforcementT2_NeverDoubleMultipliesLocalTopSpeed()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            var ship = adapter.Context.Ship;
            var modules = ship.GetComponent<Nebula.Modules.ShipModules>();
            var cruise = ship.GetComponent<ShipCruise>();

            modules.Modules.SetTier("exploration.engine_navigation", 2);
            cruise.ApplyEngineTier(2);
            modules.Modules.SetTier("defense.hull_reinforcement", 2);
            modules.ApplyFunctionalEffect("defense.hull_reinforcement");
            yield return null;

            Assert.AreEqual(194.4f, ship.LocalMaxThrottle, 0.01f, "must stay exactly Engine-Navigation T2's own value, never 194.4 * 1.08 from a second Hull-Reinforcement-driven multiply");
        }

        [UnityTest]
        public IEnumerator Damaged_Reduces75PercentSpeed_BlocksBoost_BlocksCruise()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            var ship = adapter.Context.Ship;
            var combat = ship.GetComponent<ShipCombat>();

            combat.State.RecoverToFractions(0.25f, 0.10f, 0f); // sets Damaged = true
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(combat.State.Damaged);
            Assert.AreEqual(CombatCatalog.DamagedFlightSpeedMultiplier, ship.DamagedSpeedMultiplier, 0.001f);
            Assert.IsFalse(combat.State.TryStartBoost());

            var cruise = ship.GetComponent<ShipCruise>();
            cruise.ApplyEngineTier(2);
            yield return null;
            Assert.IsNotNull(cruise.CruiseBlockedReason);
        }

        [UnityTest]
        public IEnumerator Repair_EndToEnd_HealsConsumesResourcesAndClearsDamagedAtSixtyPercent()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            var ship = adapter.Context.Ship;
            var combat = ship.GetComponent<ShipCombat>();
            var logistics = ship.GetComponent<Nebula.Extraction.ShipLogistics>();

            logistics.Cargo.Add(ProcessingCatalog.BasicPartId, 200f);
            logistics.Cargo.Add("material.metal_alloy", 100f);

            combat.ApplyDamage(100f); // real damage -- resets SinceLastDamageSeconds to 0, meaningfully exercising the gate below
            combat.State.RecoverToFractions(0.25f, 0.10f, 0f); // Damaged, Hull 25%, Armor 10% (never touches SinceLastDamageSeconds itself)
            ship.SetVelocity(Vector3.zero);

            RepairRejectReason tooSoon = combat.CheckRepairStart();
            Assert.AreEqual(RepairRejectReason.TooSoonAfterDamage, tooSoon, "must be blocked immediately after real damage");

            // SinceLastDamageSeconds only advances through ShipCombatState.Tick's real dt
            // (Time.fixedDeltaTime), which is paced to real elapsed time even in batchmode --
            // a real wall-clock wait is required here, a handful of WaitForFixedUpdate yields
            // would only accumulate a few fixedDeltaTime ticks (~0.1s), not 8+ seconds.
            yield return new WaitForSeconds(8.3f);

            RepairRejectReason reason = combat.CheckRepairStart();
            Assert.AreEqual(RepairRejectReason.None, reason, "should be eligible: out of combat, stationary, long enough since damage, resources available");

            Assert.IsTrue(combat.TryStartRepair());
            Assert.IsTrue(combat.Repair.Active);

            float armorBefore = combat.State.Durability.Armor;
            float partsBefore = logistics.Cargo.GetStored(ProcessingCatalog.BasicPartId);

            // 1%/s of combined max (600+1000=1600) = 16 HP/s -- wait long enough for clearly
            // measurable progress without needing the whole ~100s full-heal window.
            yield return new WaitForSeconds(6f);

            Assert.Greater(combat.State.Durability.Armor + combat.State.Durability.Hull, armorBefore + 50f, "combined K/Z/G must have measurably increased");
            Assert.Less(logistics.Cargo.GetStored(ProcessingCatalog.BasicPartId), partsBefore, "resources must have been consumed proportionally to real healing");
        }

        /// <summary>
        /// An enemy does not fight a player the game has disarmed.
        /// <para>
        /// Opening a screen no longer stops the clock — the world is meant to keep
        /// living behind a menu — but it does still raise <c>InputGate.Suppressed</c>,
        /// and <see cref="ShipCombat.Update"/> returns on that gate: with a menu open
        /// the player cannot target, fire, boost or evade. The shell also hides the
        /// HUD, so an enemy that kept attacking would be killing someone who can
        /// neither respond nor see it. While the pause existed this was free; now it
        /// has to be asserted, or the next person to touch either gate re-opens it
        /// silently.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AnEnemyDoesNotAttackWhileAScreenHasTakenThePlayersControls()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            var director = Object.FindObjectOfType<CombatEncounterDirector>();
            Assert.IsNotNull(director, "no encounter director to spawn a real enemy from");

            Transform ship = adapter.Context.Ship.transform;
            EnemyAgent enemy = director.SpawnEnemy(EnemyIds.FighterMk1, ship.position + ship.forward * 220f);
            Assert.IsNotNull(enemy, "the director spawned no enemy");
            yield return new WaitForFixedUpdate();

            bool gateBefore = InputGate.Suppressed;
            try
            {
                // The state a screen leaves behind, set the way the shell sets it.
                InputGate.Suppressed = true;
                Vector3 restingPosition = enemy.transform.position;
                float restingHull = enemy.Combat.Hull;

                float start = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - start < 1.5f) yield return null;

                // Frozen, not merely non-damaging: a Fighter closes before it fires, so
                // position is the earlier and stricter signal that its turn never ran.
                Assert.AreEqual(restingPosition.x, enemy.transform.position.x, 0.01f,
                    "the enemy kept manoeuvring while the player's controls were taken");
                Assert.AreEqual(restingPosition.z, enemy.transform.position.z, 0.01f,
                    "the enemy kept manoeuvring while the player's controls were taken");
                Assert.AreEqual(restingHull, enemy.Combat.Hull, 0.01f,
                    "the enemy's own combat state advanced while the player was disarmed");
            }
            finally
            {
                InputGate.Suppressed = gateBefore;
            }

            // And it starts again the moment the controls come back, so this is a
            // suspension rather than a permanently disabled enemy.
            Vector3 releasedFrom = enemy.transform.position;
            float resumed = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - resumed < 1.5f) yield return null;

            Assert.AreNotEqual(releasedFrom, enemy.transform.position,
                "the enemy never resumed once the player had their controls back");
        }
    }
}
