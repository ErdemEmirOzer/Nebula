using System.Collections;
using System.Diagnostics;
using Nebula.UI.StrategicMap;
using Nebula.UI.StrategicMap.Preview;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// What the map costs to draw.
    ///
    /// The number that matters is the map's own render, not the frame time around it: a
    /// batchmode frame is dominated by everything else the session is doing, and would tell
    /// us nothing about whether the map fits in a budget. RenderNow is timed directly, and
    /// the budget is stated against 60fps -- 16.7ms for a whole frame, of which a map drawn
    /// over a paused game may reasonably take a large share, but not all of it.
    ///
    /// Draw calls are counted through the editor's own statistics, which is the only place
    /// they are exposed. That is a real number for the batcher, not an estimate from the
    /// number of layers.
    /// </summary>
    // Graphics: the single test measures real batches and draw calls, which only exist on a real device. Under -nographics the
    // render is a segfault risk in RenderOffscreenCameras, not merely a blank frame, so this
    // fixture must never be selected into a headless worker. It carries no ReleaseRegression
    // category on purpose -- the headless gate selects by that name and must not reach here.
    [Category("Graphics")]
    public sealed class StrategicMapPerformanceTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>A generous share of a 60fps frame. The map draws over a paused game.</summary>
        private const double RenderBudgetMs = 8.0;

        private static bool HasGpu => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

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
        public IEnumerator TheMapDrawsInsideItsFrameBudget()
        {
            if (!HasGpu)
            {
                UnityEngine.Debug.Log("[map perf] no graphics device; skipping");
                yield break;
            }

            yield return LoadSceneFresh();
            var map = Object.FindObjectOfType<StrategicMapScreen>();
            Assert.IsNotNull(map, "the strategic map must exist in the scene");

            map.SetOpen(true);
            yield return null;
            var renderer3d = Object.FindObjectOfType<StrategicMapUniverseRenderer>();
            Assert.IsNotNull(renderer3d, "the 3D map renderer must exist once the map is open");
            renderer3d.EnsureTarget(1920, 1080);
            map.Refresh();
            yield return null;

            double universeMs = yield_MeasureUniverse(renderer3d, out int universeSamples);
            UnityEngine.Debug.Log($"[map perf] universe {universeMs:F2} ms over {universeSamples} renders " +
                                  $"({renderer3d.Target.width}x{renderer3d.Target.height})");

            // The system view is the heavier one: it adds real planet prefabs, their lights
            // and the asteroid belt on top of everything the universe view already draws.
            StrategicMapUniverseSnapshot universe = StrategicMapStaticPreviewSource.Load();
            renderer3d.FocusSystem(universe.systems[0].id);
            for (int i = 0; i < 30; i++) yield return null;

            double systemMs = yield_MeasureUniverse(renderer3d, out int systemSamples);
            UnityEngine.Debug.Log($"[map perf] system {systemMs:F2} ms over {systemSamples} renders");

#if UNITY_EDITOR
            renderer3d.RenderNow();
            UnityEngine.Debug.Log($"[map perf] draw calls {UnityEditor.UnityStats.drawCalls}, " +
                                  $"batches {UnityEditor.UnityStats.batches}, " +
                                  $"triangles {UnityEditor.UnityStats.triangles}");
#endif

            Assert.Less(universeMs, RenderBudgetMs,
                $"the universe view must draw inside {RenderBudgetMs} ms to leave room in a 60fps frame");
            Assert.Less(systemMs, RenderBudgetMs,
                $"the system view must draw inside {RenderBudgetMs} ms to leave room in a 60fps frame");
        }

        /// <summary>
        /// Times RenderNow directly, discarding the first few renders: the first one through
        /// pays for shader compilation and buffer creation, which is a real cost but a
        /// one-off, and averaging it in would make the steady state look far worse than it is.
        /// </summary>
        private static double yield_MeasureUniverse(StrategicMapUniverseRenderer renderer, out int samples)
        {
            for (int warmup = 0; warmup < 5; warmup++) renderer.RenderNow();

            const int Renders = 30;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < Renders; i++) renderer.RenderNow();
            watch.Stop();

            samples = Renders;
            return watch.Elapsed.TotalMilliseconds / Renders;
        }
    }
}
