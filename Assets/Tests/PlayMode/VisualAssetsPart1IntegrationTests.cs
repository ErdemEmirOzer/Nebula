using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    public class VisualAssetsPart1IntegrationTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>
        /// This fixture waits in frames, never in seconds, so it cannot hang on a stopped
        /// clock — but a session booted at <c>timeScale == 0</c> never reaches the state
        /// being asserted either.
        /// <para>
        /// One is the only correct resting value for <c>Time.timeScale</c>, and it
        /// persists into ProjectSettings/TimeManager.asset — <c>runtests.sh</c> fails
        /// a run that leaves it dirty, because a teardown once did exactly that. So
        /// the clock is normalized on the way in and left normalized on the way out
        /// rather than put back as found: restoring a neighbour's zero would carry
        /// the leak forward instead of stopping it here. Anything but one on entry is
        /// reported, so the leak stays attributable to whoever caused it.
        /// </para>
        /// </summary>
        [SetUp]
        public void NormalizeClock()
        {
            if (!Mathf.Approximately(Time.timeScale, 1f))
                Debug.LogWarning($"[test isolation] Time.timeScale was {Time.timeScale} on entry "
                                 + "to VisualAssetsPart1IntegrationTests; a previous fixture did not restore it.");
            Time.timeScale = 1f;
        }

        [TearDown]
        public void RestoreClock() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator RealScene_UsesPart1AuthoredVisuals_WithoutVisualAuthorityLeaks()
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
            // entry the menu's New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;

            Assert.Greater(Object.FindObjectsOfType<NebulaVisualInstance>(true).Length, 0);

            foreach (PlanetIdentity planet in Object.FindObjectsOfType<PlanetIdentity>())
            {
                Assert.IsNotNull(planet.GetComponentInChildren<NebulaVisualInstance>(true),
                    $"{planet.WorldId} has no authored planet visual");
                MeshRenderer placeholder = planet.GetComponent<MeshRenderer>();
                Assert.IsNotNull(placeholder);
                Assert.IsFalse(placeholder.enabled, $"{planet.WorldId} still renders its placeholder sphere");
            }

            Star[] stars = Object.FindObjectsOfType<Star>();
            Assert.Greater(stars.Length, 0);
            foreach (Star star in stars)
            {
                Assert.IsNull(star.GetComponentInChildren<NebulaVisualInstance>(true),
                    "unapproved authored star visual is still active");
                Assert.AreEqual(1, star.GetComponentsInChildren<SunGlow>(true).Length,
                    "the established procedural star glow must be restored");
                Assert.AreEqual(0, star.GetComponentsInChildren<Light>(true).Length,
                    "LGT-01: a star describes its light and never creates one — only the lighting " +
                    "director realizes a key light, and only for the single Full system");
            }
            Assert.IsTrue(Object.FindObjectsOfType<BlackHole>().All(
                x => x.GetComponentInChildren<NebulaVisualInstance>(true) != null),
                "every generated galactic black hole must use the authored visual");

            SafetyMarkerAnchor marker = Object.FindObjectOfType<SafetyMarkerAnchor>();
            Assert.IsNotNull(marker);
            Assert.IsNotNull(marker.GetComponentInChildren<NebulaVisualInstance>(true));

            GameObject[] dummies = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(x => x.scene.IsValid() && x.name.StartsWith("Training Dummy ")).ToArray();
            Assert.AreEqual(3, dummies.Length);
            Assert.IsTrue(dummies.All(x => x.GetComponentInChildren<NebulaVisualInstance>(true) != null));

            Asteroid asteroid = Object.FindObjectOfType<Asteroid>();
            Assert.IsNotNull(asteroid);
            Assert.IsNotNull(asteroid.GetComponent<AsteroidAuthoredVisual>());
            Assert.AreEqual(1, asteroid.GetComponentsInChildren<Renderer>(true).Length,
                "asteroid optimization regressed to a multi-renderer prefab hierarchy");

            foreach (NebulaVisualInstance visual in Object.FindObjectsOfType<NebulaVisualInstance>(true))
            {
                Assert.IsFalse(visual.GetComponentsInChildren<Collider>(true).Any(x => x.enabled),
                    $"{visual.VisualId} gained an active visual collider");
                Assert.IsFalse(visual.GetComponentsInChildren<Camera>(true).Any(x => x.enabled));
                Assert.IsFalse(visual.GetComponentsInChildren<Light>(true).Any(x => x.enabled));
                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.IsNotNull(material, $"{visual.VisualId} has a missing runtime material");
                    Assert.IsNotNull(material.shader, $"{visual.VisualId} has a missing runtime shader");
                    Assert.AreNotEqual("Hidden/InternalErrorShader", material.shader.name);
                }
            }
        }
    }
}
