using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Audio;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    public sealed class NebulaAudioRuntimePlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>
        /// A stopped clock is the one way a wait in this fixture could never end:
        /// <c>WaitForSeconds</c> is scaled, and a neighbour that leaves
        /// <c>Time.timeScale</c> at zero makes it wait forever. The waits below are
        /// unscaled for that reason; the clock is normalized here as well, so the
        /// bridge under test is given real frames to run in.
        /// <para>Normalized rather than restored-as-found, for the reason spelled
        /// out in <c>VisualAssetsPart1IntegrationTests</c>: a neighbour's stopped
        /// clock is stopped here, not carried forward.</para>
        /// </summary>
        [SetUp]
        public void NormalizeClock()
        {
            if (!Mathf.Approximately(Time.timeScale, 1f))
                Debug.LogWarning($"[test isolation] Time.timeScale was {Time.timeScale} on "
                                 + "entry to NebulaAudioRuntimePlayModeTests; a "
                                 + "previous fixture did not restore it.");
            Time.timeScale = 1f;
        }

        [TearDown]
        public void RestoreClock() => Time.timeScale = 1f;

        /// <summary>
        /// Ends the session this test started and unloads the scene it loaded.
        /// <para>
        /// <see cref="LoadSceneFresh"/> calls <c>BeginSession()</c> — the real "New
        /// Campaign" entry — and nothing here called the matching exit. Nothing in a
        /// PlayMode run unloads a single-loaded scene either, so the last world this
        /// fixture booted kept running for the rest of the process: a ship, an
        /// extraction director, a streaming director and four audio loops, all
        /// ticking and all allocating, every frame, inside whatever the next
        /// fixture was measuring.
        /// </para>
        /// <para>
        /// What that cost, concretely, was the fixture two places later.
        /// <c>NebulaBrandLifecyclePlayModeTests.ThreeHundredLoadingFramesDoNotGrowTheManagedHeap</c>
        /// holds the loading loop for three hundred real frames and asserts the
        /// managed heap moves by under 128KB. Alone it does. Behind this fixture it
        /// reported 1,134,592 bytes — roughly 3.7KB a frame, which is what a live
        /// campaign world costs per frame and has nothing to do with the brand path
        /// it was supposedly measuring. The loading overlay and the main menu are
        /// main-menu-time surfaces: measuring them with a campaign running measures
        /// the campaign.
        /// </para>
        /// <para>
        /// <c>EndSession</c> is the production exit, so the world dies exactly the
        /// way it does in the game; the scene unload afterwards is because
        /// <c>EndSession</c> deliberately leaves a main menu standing, and a live
        /// menu gates ship input for anything that comes next.
        /// </para>
        /// </summary>
        [UnityTearDown]
        public IEnumerator EndTheSessionThisTestStarted()
        {
            if (!bootedASessionThisTest) yield break;
            bootedASessionThisTest = false;

            Object.FindObjectOfType<SolarSystemBootstrap>()?.EndSession();
            yield return null;
            yield return null;

            Assert.AreEqual(0, Object.FindObjectsOfType<Nebula.Sectors.SystemStreamingDirector>().Length,
                "this test left a streaming director behind — every later fixture in the run " +
                "would be sharing its world");

            Scene loaded = SceneManager.GetActiveScene();
            if (!loaded.IsValid() || loaded.path != ScenePath) yield break;
            Scene empty = SceneManager.CreateScene("audio-empty-" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(loaded);
            yield return null;
        }

        /// <summary>Set by <see cref="LoadSceneFresh"/>, read by <see cref="EndTheSessionThisTestStarted"/> — so the teardown only ever ends a session this fixture is responsible for.</summary>
        private static bool bootedASessionThisTest;

        [UnityTest]
        public IEnumerator Catalog_LoadsAll292ApprovedMusicFreeEvents()
        {
            NebulaAudioCatalog catalog =
                Resources.Load<NebulaAudioCatalog>("Audio/NebulaAudioCatalog");
            Assert.NotNull(catalog);
            Assert.AreEqual(292, catalog.Entries.Count);
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (NebulaAudioEntry entry in catalog.Entries)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.EventId));
                Assert.IsTrue(ids.Add(entry.EventId), entry.EventId);
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.ResourcePath), entry.EventId);
            }
            foreach (string representativeId in new[]
                     {
                         "audio.ui.confirm", "audio.ship.engine_idle.loop",
                         "audio.drill.t2.loop", "audio.weapon.plasma.fire",
                         "audio.world.star.yellow.loop", "audio.poi.anomaly.local.loop"
                     })
            {
                Assert.IsTrue(catalog.TryGet(representativeId, out NebulaAudioEntry entry));
                Assert.NotNull(entry.Clip, representativeId);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneBoot_CreatesExactlyOneAudioAuthorityAndCoreLoops()
        {
            yield return LoadSceneFresh();
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAudioService>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAudioGameplayBridge>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaUiAudioFeedback>().Length);

            NebulaAudioService service = Object.FindObjectOfType<NebulaAudioService>();
            Assert.NotNull(service.Catalog);
            Assert.IsTrue(service.IsLoopPlaying(
                "listener/deep-space", "audio.world.deep_space.loop"));
            Assert.IsTrue(service.IsLoopPlaying(
                "ship/cockpit", "audio.ship.cockpit_ambience.loop"));
            Assert.IsTrue(service.IsLoopPlaying(
                "world/current-star", "audio.world.star.yellow.loop"));

            SolarSystemBootstrap adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.AreSame(service, adapter.Context.AudioService);
            Assert.AreSame(Object.FindObjectOfType<NebulaAudioGameplayBridge>(),
                adapter.Context.AudioBridge);
        }

        [UnityTest]
        public IEnumerator RestartCampaign_ReplacesAudioSessionWithoutDuplicates()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaAudioService oldService = adapter.Context.AudioService;

            adapter.RestartCampaign();
            yield return null;
            yield return null;

            Assert.IsTrue(oldService == null);
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAudioService>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAudioGameplayBridge>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaUiAudioFeedback>().Length);
            Assert.IsTrue(adapter.Context.AudioService.IsLoopPlaying("listener/deep-space"));
        }

        [UnityTest]
        public IEnumerator CompletionBridge_StartsProximityAndEnvironmentLoopsWithoutDuplicateAuthorities()
        {
            yield return LoadSceneFresh();
            yield return new WaitForSecondsRealtime(0.35f);

            NebulaAudioService service = Object.FindObjectOfType<NebulaAudioService>();
            Assert.NotNull(service);
            Assert.IsTrue(service.IsLoopPlaying("world/environment"),
                "The current planet-environment bed must be driven by the completion bridge.");
            Assert.IsTrue(service.IsLoopPlaying("world/safety-marker"),
                "A new campaign starts physically at the Safety Marker, so its local loop must be audible.");
            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAudioGameplayBridge>().Length);
        }

        private static IEnumerator LoadSceneFresh()
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
            bootedASessionThisTest = true;
            yield return null;
        }
    }
}
