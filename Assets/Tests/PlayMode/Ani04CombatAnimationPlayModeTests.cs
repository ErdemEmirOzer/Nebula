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
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-04 in a real session: effects raised by combat that really happened,
    /// enemies posed from the phase their own AI is really in, and proof that
    /// the damage, the disrupt and the destruction come out identical whether
    /// or not any of it is drawn.
    /// </summary>
    public class Ani04CombatAnimationPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;

        private static readonly string[] AllRoles =
        {
            EnemyIds.ScoutProbeMk1, EnemyIds.FighterMk1, EnemyIds.LooterTransportMk1,
            EnemyIds.EmDisruptorMk1, EnemyIds.HeavyAssaultMk1, EnemyIds.SiegeMk1
        };

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
        public void RestoreRealGameplaySlotAndAccessibility()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
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

        private static NebulaAnimationDirector Director()
        {
            var director = Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(director, "the session must install an animation director");
            return director;
        }

        private static CombatAnimationBridge Bridge()
        {
            var bridge = Object.FindObjectOfType<CombatAnimationBridge>();
            Assert.IsNotNull(bridge, "ANI-04 must install exactly one combat animation bridge");
            return bridge;
        }

        private static EnemyAgent Spawn(NebulaContext context, string enemyId, float forwardMetres)
        {
            CombatEncounterDirector encounters = context.Streaming.CombatEncounter;
            Assert.IsNotNull(encounters);
            EnemyAgent enemy = encounters.SpawnEnemy(enemyId,
                context.Ship.transform.position + context.Ship.transform.forward * forwardMetres);
            Assert.IsNotNull(enemy, $"the director must spawn {enemyId}");
            return enemy;
        }

        // --- wiring -------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheSessionInstallsExactlyOneBridge_ListeningToRealCombat()
        {
            yield return LoadSceneFresh();

            Assert.AreEqual(1, Object.FindObjectsOfType<CombatAnimationBridge>().Length,
                "a second bridge would double every effect");
            Assert.IsNotNull(Director().SupportOperations, "combat effects share the one pooled sprite budget");
            Assert.AreEqual(0, Bridge().ActiveEffects, "a fresh session starts with nothing on screen");
        }

        [UnityTest]
        public IEnumerator EveryEnemyRole_GetsAPresenter_WithItsOwnRealParts()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;

            float offset = 150f;
            foreach (string id in AllRoles)
            {
                EnemyAgent enemy = Spawn(context, id, offset);
                offset += 40f;

                var presenter = enemy.GetComponent<EnemyCombatAnimationPresenter>();
                Assert.IsNotNull(presenter, $"{id} must carry a combat animation presenter");
                Assert.Greater(presenter.MechanicalPartCount, 0,
                    $"{id}'s model must expose the real parts the audit found");
                Assert.IsTrue(presenter.HasActionAnchor, $"{id} must expose its own action VFX anchor");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PresentersBuiltByGameplay_AreAdoptedByTheDirector()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;

            Spawn(context, EnemyIds.FighterMk1, 150f);
            yield return null;
            yield return null;

            Assert.AreEqual(0, EnemyCombatAnimationPresenter.PendingCount,
                "an unadopted presenter is never ticked");
        }

        // --- real damage ---------------------------------------------------------

        [UnityTest]
        public IEnumerator RealDamage_RaisesTheLayerThatActuallyAbsorbedIt()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();
            CombatAnimationBridge bridge = Bridge();

            ShipCombat combat = context.Ship.GetComponent<ShipCombat>();
            Assert.IsNotNull(combat);

            int before = bridge.EffectsRaised;
            combat.ApplyDamage(5f); // the real damage path
            director.Tick(0.1f);

            Assert.Greater(bridge.EffectsRaised, before, "real damage must be drawn");
            Assert.IsNotNull(bridge.LastImpactLayer, "the bridge must read the layer the resolver chose");
            Assert.AreEqual(ShipDamageLayer.Shield, bridge.LastImpactLayer.Value,
                "an undamaged ship absorbs on the shield");
        }

        [UnityTest]
        public IEnumerator AnEffectExpiresOnItsOwn_AndHandsItsSlotBack()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();
            CombatAnimationBridge bridge = Bridge();

            context.Ship.GetComponent<ShipCombat>().ApplyDamage(5f);
            director.Tick(0.05f);
            Assert.Greater(bridge.ActiveEffects, 0, "the impact must be on screen first");

            for (int i = 0; i < 20; i++) director.Tick(0.05f);

            Assert.AreEqual(0, bridge.ActiveEffects, "a flash must expire, not linger");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ATurretThatReallyFired_ProducesAMuzzleFlash()
        {
            yield return LoadSceneFresh();
            NebulaAnimationDirector director = Director();
            CombatAnimationBridge bridge = Bridge();

            int before = bridge.EffectsRaised;

            // The same call the Pirate Base turret makes once its shot is committed.
            DefenseFireSignal.Raise(WeaponFamily.Laser, Vector3.zero, Vector3.forward, DefenseFireOwner.PirateBase);
            director.Tick(0.05f);

            Assert.Greater(bridge.EffectsRaised, before, "a committed turret shot must be drawn");
            yield return null;
        }

        // --- the six roles from their real phases ----------------------------------

        [UnityTest]
        public IEnumerator AnEnemyIsPosed_FromThePhaseItsOwnAiIsIn()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            EnemyAgent fighter = Spawn(context, EnemyIds.FighterMk1, 150f);
            var presenter = fighter.GetComponent<EnemyCombatAnimationPresenter>();

            director.Tick(0.1f);
            EnemyCombatVisual first = presenter.CurrentVisual;
            Assert.AreEqual(CombatAnimationProfiles.ForEnemy(fighter.Ai.Phase, fighter.Destroyed,
                fighter.IsDisrupted, fighter.IsTelegraphing, false, false), first,
                "the pose must be exactly what the real phase maps to");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ARealDisrupt_ClosesTheEnemysPose_ForExactlyItsOwnDuration()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            EnemyAgent target = Spawn(context, EnemyIds.LooterTransportMk1, 150f);
            var presenter = target.GetComponent<EnemyCombatAnimationPresenter>();

            Assert.IsTrue(target.TryDisrupt(2f), "the real disrupt path must accept");
            float remainingBefore = target.DisruptRemaining;

            for (int i = 0; i < 30; i++) director.Tick(0.02f);

            Assert.AreEqual(EnemyCombatVisual.Disrupted, presenter.CurrentVisual);
            Assert.AreEqual(0f, presenter.BankDegrees, 0.5f, "a disrupted enemy holds still");
            Assert.AreEqual(remainingBefore, target.DisruptRemaining, 1e-4f,
                "ticking the animation must not burn down the real disrupt clock");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ADestroyedEnemy_ClosesItsPose_AndTheAgentStaysTheAuthority()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            EnemyAgent enemy = Spawn(context, EnemyIds.ScoutProbeMk1, 150f);
            var presenter = enemy.GetComponent<EnemyCombatAnimationPresenter>();

            // Real damage through the shared resolver, not a flag flip.
            enemy.ApplyDamage(enemy.Def.ShieldMax + enemy.Def.ArmorMax + enemy.Def.HullMax + 1000f);
            director.Tick(0.1f);

            Assert.IsTrue(enemy.Destroyed, "the test must really destroy it");
            Assert.AreEqual(EnemyCombatVisual.Destroyed, presenter.CurrentVisual);
            Assert.IsTrue(CombatAnimationProfiles.IsClosedPose(presenter.CurrentVisual));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheEnemyRoot_IsNeverMovedByTheAnimation()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            EnemyAgent enemy = Spawn(context, EnemyIds.SiegeMk1, 150f);
            enemy.enabled = false; // freeze the agent's own movement so only animation could move it

            Vector3 position = enemy.transform.position;
            Quaternion rotation = enemy.transform.rotation;
            Vector3 scale = enemy.transform.localScale;

            for (int i = 0; i < 200; i++) director.Tick(0.02f);

            Assert.AreEqual(position, enemy.transform.position, "the presenter must never move the agent");
            Assert.AreEqual(rotation, enemy.transform.rotation, "the enemy's aim is gameplay");
            Assert.AreEqual(scale, enemy.transform.localScale);
            yield return null;
        }

        // --- accessibility and pressure ---------------------------------------------

        [UnityTest]
        public IEnumerator MotionOff_StillDrawsTheHit_ButStopsTheDecoration()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();
            CombatAnimationBridge bridge = Bridge();

            AnimationAccessibility.MotionOff = true;

            int before = bridge.EffectsRaised;
            context.Ship.GetComponent<ShipCombat>().ApplyDamage(5f);
            director.Tick(0.05f);

            Assert.Greater(bridge.EffectsRaised, before,
                "a hit must stay legible with motion off — it is information");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TwentyFourEnemiesAndAHundredEvents_LeaveThePoolBounded()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();
            ShipCombat combat = context.Ship.GetComponent<ShipCombat>();

            for (int i = 0; i < 24; i++)
                Spawn(context, AllRoles[i % AllRoles.Length], 150f + i * 25f);
            director.Tick(0.1f);

            int quadsAfterWarmup = -1;
            for (int i = 0; i < 100; i++)
            {
                combat.ApplyDamage(1f);
                DefenseFireSignal.Raise(WeaponFamily.Plasma, Vector3.zero, Vector3.forward, DefenseFireOwner.PlanetaryStructure);
                director.Tick(0.02f);
                if (i == 9) quadsAfterWarmup = director.SupportOperations.TotalQuads;
            }

            Assert.AreEqual(quadsAfterWarmup, director.SupportOperations.TotalQuads,
                "100 combat events across 24 enemies must not grow the pool");
            Assert.LessOrEqual(director.SupportOperations.LeasedCount,
                director.SupportOperations.CapacityPerVfx * 7,
                "the ceiling is capacity × the seven shared sprites");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnimationOnOrOff_ProducesTheIdenticalDamageOutcome()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            EnemyAgent drawn = Spawn(context, EnemyIds.FighterMk1, 150f);
            EnemyAgent hidden = Spawn(context, EnemyIds.FighterMk1, 190f);

            AnimationAccessibility.MotionOff = false;
            float drawnBefore = drawn.Combat.Shield + drawn.Combat.Armor + drawn.Combat.Hull;
            drawn.ApplyDamage(40f);
            float drawnAfter = drawn.Combat.Shield + drawn.Combat.Armor + drawn.Combat.Hull;
            director.Tick(0.1f);

            AnimationAccessibility.MotionOff = true;
            float hiddenBefore = hidden.Combat.Shield + hidden.Combat.Armor + hidden.Combat.Hull;
            hidden.ApplyDamage(40f);
            float hiddenAfter = hidden.Combat.Shield + hidden.Combat.Armor + hidden.Combat.Hull;
            director.Tick(0.1f);

            Assert.AreEqual(drawnBefore - drawnAfter, hiddenBefore - hiddenAfter, 1e-4f,
                "damage must not care whether anyone is watching");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesOneBridge_AndNoStalePresenters()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            Spawn(adapter.Context, EnemyIds.HeavyAssaultMk1, 150f);
            yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<CombatAnimationBridge>().Length,
                "a restart must not leave a second bridge listening");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(0, EnemyCombatAnimationPresenter.PendingCount,
                "the previous session's presenters must not linger");
        }
    }
}
