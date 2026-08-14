using System.Collections;
using System.Linq;
using Nebula.Celestial;
using Nebula.UI;
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
    public class NebulaProducedAssetPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>
        /// The pulse below is asserted to have expired after a wait. A scaled wait
        /// under a stopped clock never returns at all, so the wait is unscaled and
        /// the clock is normalized here — the pulse then either dies on time or the
        /// assertion fails, which are the only two outcomes worth having.
        /// <para>Normalized rather than restored-as-found, for the reason spelled
        /// out in <c>VisualAssetsPart1IntegrationTests</c>: a neighbour's stopped
        /// clock is stopped here, not carried forward.</para>
        /// </summary>
        [SetUp]
        public void NormalizeClock()
        {
            if (!Mathf.Approximately(Time.timeScale, 1f))
                Debug.LogWarning($"[test isolation] Time.timeScale was {Time.timeScale} on "
                                 + "entry to NebulaProducedAssetPlayModeTests; a "
                                 + "previous fixture did not restore it.");
            Time.timeScale = 1f;
        }

        [TearDown]
        public void RestoreClock() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator RealScene_UsesProducedStarUiAndVfxAssets()
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

            Star realSystemStar = Object.FindObjectsOfType<Star>()
                .FirstOrDefault(star => star.transform.parent != null
                    && star.transform.parent.name.StartsWith("System "));
            Assert.IsNotNull(realSystemStar);
            Renderer surface = realSystemStar.transform.Find("Surface")?.GetComponent<Renderer>();
            Assert.IsNotNull(surface);
            Assert.IsNotNull(surface.sharedMaterial.mainTexture);
            StringAssert.Contains("star.", surface.sharedMaterial.mainTexture.name.ToLowerInvariant());

            // The four legacy HUDs this asserted on were replaced by the redesign
            // (UI-09). Same intent -- a booted session really builds its interface --
            // against the widgets that carry those readings now. Inactive included:
            // every widget starts with its panel hidden.
            Assert.IsNotNull(Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudAlerts>(true));
            Assert.IsNotNull(Object.FindObjectOfType<Nebula.UI.Menu.NebulaParentMenuScreen>(true));
            Assert.IsNotNull(Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudVitals>(true));
            Assert.IsNotNull(Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudNavigation>(true));

            GameObject pulse = NebulaVfxPulse.Emit(
                NebulaCommonVfx.ScanPulse, Vector3.zero, 4f, Color.cyan, 0.1f);
            Assert.IsNotNull(pulse);
            Assert.IsNotNull(pulse.GetComponentInChildren<Renderer>());
            Assert.IsFalse(pulse.GetComponentsInChildren<Collider>(true).Any(x => x.enabled));
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.IsTrue(pulse == null);
        }
    }
}
