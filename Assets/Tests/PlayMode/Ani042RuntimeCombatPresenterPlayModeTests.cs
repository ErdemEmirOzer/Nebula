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
    /// ANI-04.2 in a real session: the proof that a shot leaves from the same
    /// place and in the same direction no matter what the weapon animation is
    /// doing, and that nothing the presenters hold survives a restart.
    /// </summary>
    public class Ani042RuntimeCombatPresenterPlayModeTests
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
            Assert.IsNotNull(director);
            return director;
        }

        private static EnemyAgent Spawn(NebulaContext context, string enemyId, float forwardMetres)
        {
            EnemyAgent enemy = context.Streaming.CombatEncounter.SpawnEnemy(enemyId,
                context.Ship.transform.position + context.Ship.transform.forward * forwardMetres);
            Assert.IsNotNull(enemy);
            return enemy;
        }

        /// <summary>Equips a family through the real hardpoint path and builds its visual.</summary>
        private static ShipWeaponVisuals EquipAndBuild(NebulaContext context, WeaponFamily family)
        {
            ShipCombat combat = context.Ship.GetComponent<ShipCombat>();
            var visuals = context.Ship.GetComponent<ShipWeaponVisuals>();
            Assert.IsNotNull(combat);
            Assert.IsNotNull(visuals);

            combat.Hardpoints[0].Equip(family, 1);
            visuals.Refresh();
            return visuals;
        }

        // --- the invariant this part exists for ------------------------------------

        [UnityTest]
        public IEnumerator TheGameplayMuzzle_IsADifferentAnchorFromTheVisualOne()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;

            ShipWeaponVisuals visuals = EquipAndBuild(context, WeaponFamily.Plasma);

            Transform gameplay = visuals.GetMuzzle(WeaponFamily.Plasma);
            Transform visual = visuals.GetVisualMuzzle(WeaponFamily.Plasma);

            Assert.IsNotNull(gameplay);
            Assert.IsNotNull(visual);
            Assert.AreNotSame(gameplay, visual,
                "a shot's origin and a muzzle flash must not be the same transform");
            Assert.IsNull(gameplay.GetComponentInParent<Renderer>(),
                "the gameplay muzzle must live outside the animated model");
        }

        [UnityTest]
        public IEnumerator VisualRecoilAndGimbal_NeverMoveTheShotsOrigin()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            ShipWeaponVisuals visuals = EquipAndBuild(context, WeaponFamily.Plasma);
            director.Tick(0.1f);

            Transform gameplay = visuals.GetMuzzle(WeaponFamily.Plasma);
            Vector3 originAtRest = gameplay.position;
            Quaternion aimAtRest = gameplay.rotation;
            Vector3 visualAtRest = visuals.GetVisualMuzzle(WeaponFamily.Plasma).position;

            // Drive the real hierarchy exactly as WeaponGimbalPresenter does:
            // swing the model's own gimbal and slide the weapon back on recoil.
            Transform weaponRoot = null;
            foreach ((WeaponFamily family, GameObject visual) in visuals.MountedWeapons)
                if (family == WeaponFamily.Plasma) weaponRoot = visual.transform;
            Assert.IsNotNull(weaponRoot, "the Plasma weapon model must be mounted");

            Transform yaw = NebulaVisualResolver.FindAnchor(weaponRoot, CombatMechanicsProfiles.GimbalYaw);
            Assert.IsNotNull(yaw, "the Plasma model must carry part.gimbal_yaw");

            // Read straight after posing: the presenter's next tick deliberately
            // puts an un-aimed, un-recoiling weapon back on its authored pose.
            yaw.localRotation = Quaternion.Euler(0f, 20f, 0f);
            weaponRoot.localPosition -= new Vector3(0f, 0f, CombatAnimationProfiles.RecoilMetres);

            Vector3 visualNow = visuals.GetVisualMuzzle(WeaponFamily.Plasma).position;

            Assert.AreNotEqual(visualAtRest, visualNow,
                "the visual muzzle is expected to have moved — otherwise this proves nothing");
            Assert.AreEqual(originAtRest, visuals.GetMuzzle(WeaponFamily.Plasma).position,
                "the projectile spawn point must be bit-identical while the model animates");
            Assert.AreEqual(aimAtRest, visuals.GetMuzzle(WeaponFamily.Plasma).rotation,
                "and so must the direction it leaves in");
        }

        [UnityTest]
        public IEnumerator EveryWeaponFamily_GetsItsOwnStableGameplayMuzzle()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            ShipCombat combat = context.Ship.GetComponent<ShipCombat>();
            var visuals = context.Ship.GetComponent<ShipWeaponVisuals>();

            combat.Hardpoints[0].Equip(WeaponFamily.Laser, 1);
            combat.Hardpoints[1].Equip(WeaponFamily.Plasma, 1);
            visuals.Refresh();

            Transform laser = visuals.GetMuzzle(WeaponFamily.Laser);
            Transform plasma = visuals.GetMuzzle(WeaponFamily.Plasma);

            Assert.AreNotSame(laser, plasma, "each hardpoint gets its own anchor");
            Assert.AreNotSame(laser, context.Ship.transform, "a mounted weapon must not fall back to the hull");
            Assert.AreNotSame(plasma, context.Ship.transform);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheGameplayMuzzle_StillFollowsTheShip()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;

            ShipWeaponVisuals visuals = EquipAndBuild(context, WeaponFamily.Em);
            Transform gameplay = visuals.GetMuzzle(WeaponFamily.Em);

            Vector3 offsetBefore = gameplay.position - context.Ship.transform.position;
            context.Ship.transform.position += new Vector3(0f, 0f, 500f);
            yield return null;

            Vector3 offsetAfter = gameplay.position - context.Ship.transform.position;
            Assert.AreEqual(offsetBefore.x, offsetAfter.x, 0.01f, "the muzzle rides with the hull");
            Assert.AreEqual(offsetBefore.y, offsetAfter.y, 0.01f);
            Assert.AreEqual(offsetBefore.z, offsetAfter.z, 0.01f);
        }

        [UnityTest]
        public IEnumerator ChangingTheLoadout_RebuildsTheAnchorAndLeavesNoOrphan()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            ShipCombat combat = context.Ship.GetComponent<ShipCombat>();

            ShipWeaponVisuals visuals = EquipAndBuild(context, WeaponFamily.Laser);
            Transform first = visuals.GetMuzzle(WeaponFamily.Laser);
            Assert.IsNotNull(first);

            // The real swap path.
            combat.Hardpoints[0].Unequip();
            visuals.Refresh();
            yield return null;

            Assert.AreSame(context.Ship.transform, visuals.GetMuzzle(WeaponFamily.Laser),
                "with nothing mounted the muzzle falls back to the hull, exactly as before");

            combat.Hardpoints[0].Equip(WeaponFamily.Plasma, 1);
            visuals.Refresh();
            yield return null;

            Transform rebuilt = visuals.GetMuzzle(WeaponFamily.Plasma);
            Assert.IsNotNull(rebuilt);
            Assert.AreNotSame(context.Ship.transform, rebuilt, "the new weapon gets its own anchor");

            int orphans = 0;
            foreach (Transform t in context.Ship.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("gameplay.muzzle.")) orphans++;
            Assert.AreEqual(1, orphans, "exactly one stable anchor for exactly one mounted weapon");
        }

        [UnityTest]
        public IEnumerator AnimationOnOrOff_LeavesTheShotsOriginIdentical()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            ShipWeaponVisuals visuals = EquipAndBuild(context, WeaponFamily.Plasma);
            Spawn(context, EnemyIds.FighterMk1, 200f);

            AnimationAccessibility.MotionOff = false;
            for (int i = 0; i < 60; i++) director.Tick(0.02f);
            Vector3 drawnOrigin = visuals.GetMuzzle(WeaponFamily.Plasma).position;
            Quaternion drawnAim = visuals.GetMuzzle(WeaponFamily.Plasma).rotation;

            AnimationAccessibility.MotionOff = true;
            for (int i = 0; i < 60; i++) director.Tick(0.02f);
            Vector3 hiddenOrigin = visuals.GetMuzzle(WeaponFamily.Plasma).position;
            Quaternion hiddenAim = visuals.GetMuzzle(WeaponFamily.Plasma).rotation;

            Assert.AreEqual(drawnOrigin, hiddenOrigin, "the shot must not care whether anyone is watching");
            Assert.AreEqual(drawnAim, hiddenAim);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AimingNeverCostsHeatOrTripsOverheat()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaAnimationDirector director = Director();

            ShipCombat combat = context.Ship.GetComponent<ShipCombat>();
            EquipAndBuild(context, WeaponFamily.Laser);
            Spawn(context, EnemyIds.FighterMk1, 250f);

            float heatBefore = combat.Hardpoints[0].Heat;
            for (int i = 0; i < 200; i++) director.Tick(0.02f);

            Assert.AreEqual(heatBefore, combat.Hardpoints[0].Heat, 1e-4f);
            Assert.IsFalse(combat.Hardpoints[0].Overheated);
            yield return null;
        }

        // --- session hygiene ----------------------------------------------------------

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesNoDuplicatePresenterOrAnchor()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            EquipAndBuild(adapter.Context, WeaponFamily.Plasma);
            yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<WeaponGimbalPresenter>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<CombatAnimationBridge>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(0, EnemyCombatAnimationPresenter.PendingCount);
        }

        [UnityTest]
        public IEnumerator NoCombatEffectLeaksAcrossARestart()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaAnimationDirector director = Director();

            adapter.Context.Ship.GetComponent<ShipCombat>().ApplyDamage(5f);
            director.Tick(0.05f);
            Assert.Greater(director.SupportOperations.LeasedCount, 0, "an effect must be out first");

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(0, Director().SupportOperations.LeasedCount,
                "the new session starts with every slot free");
        }
    }
}
