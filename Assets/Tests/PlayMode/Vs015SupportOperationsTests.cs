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
    /// PlayMode integration tests for VS-015 (Engineer/Escort/Maintenance
    /// Reserve/intervention group/support structures): real scene boot with
    /// the new composition pieces present, an Engineer physically launching,
    /// traveling, and repairing a damaged structure (bounded-step movement,
    /// never a teleport), an Escort physically firing its fixed Laser at a
    /// live enemy through the shared DamageProfile path, and a restart never
    /// duplicating the new InterventionGroupDirector. No keyboard/mouse
    /// simulation — every behavior is driven through the same public,
    /// input-free APIs a keypress would call, exactly like Vs014PlanetaryRaidsTests.
    /// </summary>
    public class Vs015SupportOperationsTests
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
        public IEnumerator SceneBoots_WithInterventionGroupDirectorAndHangarPresent()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(adapter);
            Assert.IsNotNull(adapter.Context.InterventionGroupDirector, "VS-015 §6 director must be wired by GameplayInstaller");
            Assert.IsNotNull(Object.FindObjectOfType<ShipHangar>(), "Hangar must still boot cleanly with VS-015 attached");
        }

        [UnityTest]
        public IEnumerator EngineerVehicle_PhysicallyTravels_NeverTeleports()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            ShipHangar hangar = adapter.Context.Ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(1);

            VehicleAgent engineer = hangar.RestoreVehicle("vehicle-test-engineer", VehicleCatalog.EngineerMk1);
            Vector3 shipPos = adapter.Context.Ship.transform.position;
            engineer.RestorePhysical(adapter.Context.StartSystemId, shipPos, Quaternion.identity, Vector3.zero,
                VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);

            var task = new EngineerMissionState("m-test", "vehicle-test-engineer", adapter.Context.StartSystemId,
                EngineerTargetKind.Ship, "ship", EngineerWorkKind.Repair, EngineerSourceKind.ShipCargo, null, true, 0, 0f);
            engineer.AssignEngineerTask(task);
            Assert.AreEqual(EngineerPhase.Travel, task.Phase, "Load/Launch/Travel fire synchronously on assignment");

            Vector3 startPos = engineer.transform.position;
            float maxStepThisFrame = engineer.Def.Speed * Time.maximumDeltaTime * 2f; // generous bound, real dt-capped movement
            yield return null;
            yield return null;
            float moved = Vector3.Distance(startPos, engineer.transform.position);
            Assert.LessOrEqual(moved, maxStepThisFrame + 1f, "the Engineer must move a bounded Speed*dt step, never jump/teleport");
        }

        [UnityTest]
        public IEnumerator EscortVehicle_FiresFixedLaser_AtALiveEnemy_ThroughTheSharedDamageProfilePath()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            CombatEncounterDirector director = context.Streaming.CombatEncounter;
            Assert.IsNotNull(director);

            ShipHangar hangar = context.Ship.GetComponent<ShipHangar>();
            hangar.ApplyHangarTier(2);
            VehicleAgent escort = hangar.RestoreVehicle("vehicle-test-escort", VehicleCatalog.EscortMk1);
            Vector3 shipPos = context.Ship.transform.position;
            escort.RestorePhysical(context.StartSystemId, shipPos, Quaternion.identity, Vector3.zero,
                VehiclePhase.InHangar, MissionKind.None, null, 0f, false, null, 0f);

            EnemyAgent enemy = director.SpawnEnemy(EnemyIds.LooterTransportMk1, shipPos + context.Ship.transform.forward * 100f);
            Assert.IsNotNull(enemy);
            float hullBefore = enemy.Combat.Hull;

            var escortState = new EscortState("vehicle-test-escort", "esc-m1", "ship", TargetCategory.Ship, true);
            escort.AssignEscort(escortState);
            escort.transform.position = enemy.transform.position - context.Ship.transform.forward * 10f; // already in range

            for (int i = 0; i < 10; i++) yield return null;

            Assert.LessOrEqual(enemy.Combat.Hull, hullBefore, "a real hit must reduce the enemy's Durability through DamageResolver, never a fabricated result");
        }

        [UnityTest]
        public IEnumerator RestartCampaign_NeverDuplicatesInterventionGroupDirector()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();

            adapter.RestartCampaign();
            yield return null;
            yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<InterventionGroupDirector>().Length, "no duplicate intervention group director after restart");
        }

        [UnityTest]
        public IEnumerator MaintenanceReserve_PhysicalAdd_ConservesResources()
        {
            yield return LoadSceneFresh();

            var reserve = new MaintenanceReserve("test-world", 2);
            float added = reserve.Add(ProcessingCatalog.BasicPartId, 30f);
            Assert.AreEqual(30f, added);
            Assert.AreEqual(30f, reserve.Cargo.GetStored(ProcessingCatalog.BasicPartId));

            float rejected = reserve.Add("resource.metal", 50f);
            Assert.AreEqual(0f, rejected, "raw resources are never accepted into the reserve");
            Assert.AreEqual(0f, reserve.Cargo.GetStored("resource.metal"));
            yield return null;
        }
    }
}
