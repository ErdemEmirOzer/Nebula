using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Nebula.Combat;
using Nebula.Hangar;
using Nebula.Ship;
using Nebula.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Nebula.Tests.PlayMode
{
    public class VisualAssetsPart2IntegrationTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>
        /// The agents built below are destroyed in the body, and the static
        /// registries they enrol in are swept at both ends — so a failure part-way
        /// through cannot leave vehicles and enemies visible to the next fixture,
        /// and a neighbour's leftovers are not visible to this one either.
        /// <para>The clock is normalized rather than restored-as-found, for the
        /// reason spelled out in <c>VisualAssetsPart1IntegrationTests</c>: a
        /// neighbour's stopped clock is stopped here, not carried forward.</para>
        /// </summary>
        [SetUp]
        public void Setup()
        {
            if (!Mathf.Approximately(Time.timeScale, 1f))
                Debug.LogWarning($"[test isolation] Time.timeScale was {Time.timeScale} on "
                                 + "entry to VisualAssetsPart2IntegrationTests; a previous "
                                 + "fixture did not restore it.");
            Time.timeScale = 1f;
            ClearRegistries();
        }

        [TearDown]
        public void Teardown()
        {
            ClearRegistries();
            Time.timeScale = 1f;
        }

        private static void ClearRegistries()
        {
            EnemyAgent.ClearRegistry();
            VehicleAgent.Registry.Clear();
        }

        [UnityTest]
        public IEnumerator RealScene_RuntimeVehiclesAndEnemies_UsePart2AssetsWithoutColliderLeaks()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it -- explicitly, through the same idempotent
            // entry the menu's New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;

            ShipController ship = Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship);
            var created = new List<GameObject>();

            foreach (VehicleDef definition in VehicleCatalog.All)
            {
                VehicleAgent vehicle = VehicleAgent.Create(
                    ship, "part2-test-" + definition.Id, definition);
                created.Add(vehicle.gameObject);
                AssertVisual(vehicle.gameObject, definition.Id);
            }

            foreach (EnemyDef definition in EnemyCatalog.All)
            {
                EnemyAgent enemy = EnemyAgent.Create(
                    "part2-test-" + definition.Id, definition,
                    ship.transform.position + Vector3.forward * 1000f,
                    ship, null, null);
                created.Add(enemy.gameObject);
                AssertVisual(enemy.gameObject, definition.Id);
            }

            foreach (GameObject root in created) Object.Destroy(root);
            yield return null;
        }

        private static void AssertVisual(GameObject owner, string visualId)
        {
            NebulaVisualInstance visual = owner.GetComponentsInChildren<NebulaVisualInstance>(true)
                .FirstOrDefault(x => x.VisualId == visualId);
            Assert.IsNotNull(visual, $"{owner.name} did not instantiate {visualId}");
            Assert.IsFalse(visual.GetComponentsInChildren<Collider>(true).Any(x => x.enabled),
                $"{visualId} leaked a visual collider into gameplay");
            Assert.Greater(visual.GetComponentsInChildren<Renderer>(true).Length, 0);
        }
    }
}
