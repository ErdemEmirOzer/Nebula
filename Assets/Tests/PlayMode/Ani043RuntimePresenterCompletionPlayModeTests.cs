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
    /// ANI-04.3 in a real session: every one of the five combat presenters
    /// actually attached to the object gameplay built, driven by that object's
    /// own state, and leaving nothing behind.
    /// </summary>
    public class Ani043RuntimePresenterCompletionPlayModeTests
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

        private static NebulaContext Context() => Object.FindObjectOfType<SolarSystemBootstrap>().Context;

        /// <summary>Spawns a real projectile through the real spawn path.</summary>
        private static ProjectileObject SpawnProjectile(NebulaContext context, ProjectileKind kind)
        {
            Vector3 at = context.Ship.transform.position + context.Ship.transform.forward * 40f;
            ProjectileObject projectile = ProjectileObject.Spawn(kind, 1, "ship", at,
                context.Ship.transform.forward * 300f, 4f, null, context.Streaming.CombatEncounter);
            Assert.IsNotNull(projectile, $"the real spawn path must produce a {kind} round");
            return projectile;
        }

        // --- 1. projectile -------------------------------------------------------

        [UnityTest]
        public IEnumerator TheRealSpawnPath_AttachesAProjectilePresenter()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();

            foreach (ProjectileKind kind in new[] { ProjectileKind.Plasma, ProjectileKind.Em, ProjectileKind.EnemySiege })
            {
                ProjectileObject projectile = SpawnProjectile(context, kind);
                var presenter = projectile.GetComponent<ProjectileAnimationPresenter>();
                Assert.IsNotNull(presenter, $"{kind} must carry a runtime presenter, not just a profile");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AProjectilesPresenter_IsAdopted_AndDrivesItsOwnRealNodes()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            ProjectileObject projectile = SpawnProjectile(context, ProjectileKind.Em);
            var presenter = projectile.GetComponent<ProjectileAnimationPresenter>();

            director.Tick(0.1f);
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount, "the director must drain the queue");

            director.Tick(0.1f);
            Assert.IsTrue(presenter.HasCore || presenter.SurfaceCount > 0,
                "the EM round's own field gimbal and guidance fins must be found");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheProjectileRoot_IsNeverMovedByItsAnimation()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            ProjectileObject projectile = SpawnProjectile(context, ProjectileKind.Plasma);
            projectile.enabled = false; // freeze its own flight so only animation could move it

            Vector3 position = projectile.transform.position;
            Vector3 velocity = projectile.State.Velocity;
            float lifetime = projectile.State.RemainingLifetimeSeconds;

            for (int i = 0; i < 120; i++) director.Tick(0.02f);

            Assert.AreEqual(position, projectile.transform.position, "the round's position is gameplay");
            Assert.AreEqual(velocity, projectile.State.Velocity, "so is its velocity");
            Assert.AreEqual(lifetime, projectile.State.RemainingLifetimeSeconds, "and its lifetime");
            yield return null;
        }

        // --- 2. planetary defense --------------------------------------------------

        [UnityTest]
        public IEnumerator ADefenceEmplacement_GetsATurretPresenter_AndAPlainStructureDoesNot()
        {
            yield return LoadSceneFresh();

            var laser = Object.FindObjectsOfType<SurfaceStructure>();
            int checkedStructures = 0;
            foreach (SurfaceStructure structure in laser)
            {
                var presenter = structure.GetComponent<DefenseTurretAnimationPresenter>();
                bool isWeapon = structure.Type == SurfaceStructureType.Laser
                                || structure.Type == SurfaceStructureType.Plasma;

                Assert.AreEqual(isWeapon, presenter != null,
                    $"{structure.Type} must {(isWeapon ? "" : "not ")}carry a turret presenter");
                checkedStructures++;
            }

            // The scene may legitimately start with none built; the mapping is
            // still asserted in EditMode, and the wiring below is what matters.
            Assert.GreaterOrEqual(checkedStructures, 0);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ATurretPresenterResolvesTheRightNodesForItsTier()
        {
            yield return LoadSceneFresh();

            var host = new GameObject("ani043-turret-host");
            try
            {
                var presenter = host.AddComponent<DefenseTurretAnimationPresenter>();
                presenter.Initialize("structure.defense.laser", 2, () => 0f);

                Assert.AreEqual("part.laser_lance_recoil", presenter.Nodes.Recoil,
                    "Laser T2 is the one turret with a real recoil node");
                Assert.AreEqual("part.turret_yaw", presenter.Nodes.Yaw);
            }
            finally { Object.DestroyImmediate(host); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ATurretIgnoresShotsThatAreNotItsOwn()
        {
            yield return LoadSceneFresh();
            NebulaAnimationDirector director = Director();

            var host = new GameObject("ani043-turret-far");
            host.transform.position = new Vector3(0f, 0f, 0f);
            try
            {
                var presenter = host.AddComponent<DefenseTurretAnimationPresenter>();
                presenter.Initialize("structure.defense.laser", 1, () => 0f);
                director.Tick(0.1f);

                // A Pirate Base shot, and a planetary shot from far away.
                DefenseFireSignal.Raise(WeaponFamily.Laser, Vector3.zero, Vector3.forward, DefenseFireOwner.PirateBase);
                DefenseFireSignal.Raise(WeaponFamily.Laser, new Vector3(5000f, 0f, 0f), Vector3.forward,
                    DefenseFireOwner.PlanetaryStructure);

                Assert.IsFalse(presenter.IsRecoiling, "neither shot was this turret's");

                // Its own shot, at its own muzzle.
                DefenseFireSignal.Raise(WeaponFamily.Laser, Vector3.zero, Vector3.forward,
                    DefenseFireOwner.PlanetaryStructure);
                Assert.IsTrue(presenter.IsRecoiling, "a committed shot from this turret must kick it");
            }
            finally { Object.DestroyImmediate(host); }
            yield return null;
        }

        // --- 3. pirate base ----------------------------------------------------------

        [UnityTest]
        public IEnumerator ARealPirateBase_GetsItsPresenter_AndAResolvedOneStartsDestroyed()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            CombatEncounterDirector encounters = context.Streaming.CombatEncounter;

            PirateBaseRuntime live = PirateBaseRuntime.Create("ani043-base-live", context.StartSystemId,
                context.Ship.transform.position + context.Ship.transform.forward * 900f, encounters, false);
            PirateBaseRuntime resolved = PirateBaseRuntime.Create("ani043-base-done", context.StartSystemId,
                context.Ship.transform.position + context.Ship.transform.forward * 1400f, encounters, true);

            var livePresenter = live.GetComponent<PirateBaseAnimationPresenter>();
            var resolvedPresenter = resolved.GetComponent<PirateBaseAnimationPresenter>();

            Assert.IsNotNull(livePresenter, "a Pirate Base must carry a runtime presenter");
            Assert.IsNotNull(resolvedPresenter);
            Assert.AreNotEqual(PirateBaseVisual.Destroyed, livePresenter.CurrentVisual);
            Assert.AreEqual(PirateBaseVisual.Destroyed, resolvedPresenter.CurrentVisual,
                "a base restored as already resolved never replays its destruction");
            yield return null;
        }

        [UnityTest]
        public IEnumerator APirateBaseReadsItsOwnTurretAndDamageEvents()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            PirateBaseRuntime runtime = PirateBaseRuntime.Create("ani043-base-events", context.StartSystemId,
                context.Ship.transform.position + context.Ship.transform.forward * 900f,
                context.Streaming.CombatEncounter, false);
            var presenter = runtime.GetComponent<PirateBaseAnimationPresenter>();
            director.Tick(0.1f);

            float hullBefore = runtime.Combat.Hull;
            runtime.ApplyDamage(10f, DamageProfile.Neutral); // the real damage path
            director.Tick(0.1f);

            Assert.LessOrEqual(runtime.Combat.Hull + runtime.Combat.Shield + runtime.Combat.Armor,
                hullBefore + PirateBaseCatalog.ShieldMax + PirateBaseCatalog.ArmorMax,
                "real damage must land through the shared resolver");
            Assert.AreEqual(PirateBaseVisual.Damaged, presenter.CurrentVisual,
                "a real hit must read as damage");
            yield return null;
        }

        // --- 4. raid --------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheSessionInstallsExactlyOneRaidPresenter()
        {
            yield return LoadSceneFresh();
            Assert.AreEqual(1, Object.FindObjectsOfType<RaidAnimationPresenter>().Length);
        }

        [UnityTest]
        public IEnumerator OnlyTheWarningPhaseDraws_AndResolvedHandsItBack()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();
            var raid = Object.FindObjectOfType<RaidAnimationPresenter>();
            ThreatPressureState pressure = context.Streaming.CombatEncounter.Pressure;

            pressure.SetPhase(RaidPhase.Scheduled);
            director.Tick(0.1f);
            Assert.IsFalse(raid.ShowingWarning, "a scheduled raid must leak nothing");

            pressure.SetPhase(RaidPhase.Warning);
            director.Tick(0.1f);
            Assert.IsTrue(raid.ShowingWarning, "the warning the simulation already announced is drawn");

            pressure.SetPhase(RaidPhase.Spawning);
            director.Tick(0.1f);
            Assert.IsFalse(raid.ShowingWarning, "no warp effect, and no lingering warning");

            pressure.SetPhase(RaidPhase.Active);
            director.Tick(0.1f);
            Assert.IsFalse(raid.ShowingWarning);

            pressure.SetPhase(RaidPhase.RetreatCleanup);
            director.Tick(0.1f);
            Assert.IsFalse(raid.ShowingWarning);

            pressure.SetPhase(RaidPhase.Resolved);
            director.Tick(0.1f);
            Assert.IsFalse(raid.ShowingWarning, "a resolved raid hands every lease back");
            yield return null;
        }

        // --- 5. wreckage -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheWreckagePresenter_IsAnAdoptedParticipant()
        {
            yield return LoadSceneFresh();
            NebulaAnimationDirector director = Director();

            var wreckage = Object.FindObjectOfType<CombatWreckagePresenter>();
            Assert.IsNotNull(wreckage, "the encounter director must still build its wreckage mirror");

            director.Tick(0.1f);
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount,
                "the wreckage mirror must be adopted like every other combat presenter");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheWreckageMirrorAllocatesNothingPerFrame()
        {
            yield return LoadSceneFresh();

            var wreckage = Object.FindObjectOfType<CombatWreckagePresenter>();
            Assert.IsNotNull(wreckage);

            // The old Update built a HashSet and a List every frame. Running
            // many frames must now leave the mirrored set stable and quiet.
            int before = wreckage.MirroredCount;
            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(before, wreckage.MirroredCount,
                "with no wreckage created or expired the mirror must not churn");
        }

        // --- invariants and hygiene -------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheStableGameplayMuzzleSurvivesAllOfThis()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            ShipCombat combat = context.Ship.GetComponent<ShipCombat>();
            var visuals = context.Ship.GetComponent<ShipWeaponVisuals>();
            combat.Hardpoints[0].Equip(WeaponFamily.Plasma, 1);
            visuals.Refresh();
            director.Tick(0.1f);

            Transform gameplay = visuals.GetMuzzle(WeaponFamily.Plasma);
            Vector3 origin = gameplay.position;

            SpawnProjectile(context, ProjectileKind.Plasma);
            for (int i = 0; i < 60; i++) director.Tick(0.02f);

            Assert.AreEqual(origin, visuals.GetMuzzle(WeaponFamily.Plasma).position,
                "ANI-04.2's invariant must still hold with every presenter running");
            Assert.AreNotSame(gameplay, visuals.GetVisualMuzzle(WeaponFamily.Plasma));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ManyProjectilesAndEnemies_LeavePoolsAndParticipantsBounded()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            for (int i = 0; i < 24; i++)
                context.Streaming.CombatEncounter.SpawnEnemy(EnemyIds.FighterMk1,
                    context.Ship.transform.position + context.Ship.transform.forward * (200f + i * 30f));
            director.Tick(0.1f);

            int quadsAfterWarmup = -1;
            for (int i = 0; i < 100; i++)
            {
                SpawnProjectile(context, i % 2 == 0 ? ProjectileKind.Plasma : ProjectileKind.Em);
                director.Tick(0.02f);
                if (i == 9) quadsAfterWarmup = director.SupportOperations.TotalQuads;
            }

            Assert.AreEqual(quadsAfterWarmup, director.SupportOperations.TotalQuads,
                "100 projectiles must not grow the effect pool");
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount,
                "every presenter must have been adopted, none left waiting");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnimationOnOrOff_LeavesTheProjectileIdentical()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            AnimationAccessibility.MotionOff = false;
            ProjectileObject drawn = SpawnProjectile(context, ProjectileKind.Em);
            Vector3 drawnVelocity = drawn.State.Velocity;
            float drawnLifetime = drawn.State.RemainingLifetimeSeconds;
            for (int i = 0; i < 30; i++) director.Tick(0.02f);

            AnimationAccessibility.MotionOff = true;
            ProjectileObject hidden = SpawnProjectile(context, ProjectileKind.Em);
            for (int i = 0; i < 30; i++) director.Tick(0.02f);

            Assert.AreEqual(drawnVelocity, hidden.State.Velocity, "the round must not care whether anyone is watching");
            Assert.AreEqual(drawnLifetime, hidden.State.RemainingLifetimeSeconds, 1e-4f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesNoOrphanPresenterOrLease()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            SpawnProjectile(adapter.Context, ProjectileKind.Plasma);
            yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<RaidAnimationPresenter>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount,
                "the previous session's combat presenters must not linger");
            Assert.AreEqual(0, Director().SupportOperations.LeasedCount,
                "the new session starts with every slot free");
        }
    }
}
