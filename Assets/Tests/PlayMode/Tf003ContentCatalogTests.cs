using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for TF-003 (validated runtime content
    /// catalog): a real scene boot carries a full RuntimeCatalog through
    /// NebulaContext, restart never duplicates/mutates it, and a
    /// deliberately-broken content source fails the whole boot before a
    /// single world/player/director object exists.
    /// </summary>
    public class Tf003ContentCatalogTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>Set by <see cref="LoadSceneFresh"/>, read by <see cref="EndTheSessionThisTestStarted"/> — so the teardown only ever ends a session this fixture is responsible for.</summary>
        private static bool bootedASessionThisTest;

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
            bootedASessionThisTest = true;
            yield return null;
        }

        /// <summary>
        /// Ends the session this test started, through the production exit.
        /// <para>
        /// <see cref="LoadSceneFresh"/> calls <c>BeginSession()</c> — the real
        /// "New Campaign" entry — and nothing here used to call the matching exit.
        /// A PlayMode run never unloads a scene a fixture single-loaded (the
        /// comment inside <see cref="DeliberatelyBrokenContentSource_FailsBootFast_NoPartialWorldPlayerOrDirector"/>
        /// already says so), so the last session this fixture booted stayed live for
        /// the whole remainder of the run: a second <c>ShipController</c>, a second
        /// <c>ExtractionDirector</c> and — the one that actually bit — a second
        /// <see cref="Nebula.Sectors.SystemStreamingDirector"/> still running
        /// <c>Update</c> every frame against campaign-scoped statics that a later
        /// fixture had already reset out from under it.
        /// </para>
        /// <para>
        /// Two concrete failures came out of that, both in fixtures that pass on
        /// their own: <c>TutorialChapterABKGameplayTests</c> composed a second world
        /// on top of this one and lost five steps to the collision, and
        /// <c>TutorialChapterFHIJGameplayTests</c> died eighteen times over on a
        /// <c>NullReferenceException</c> per frame, because this orphan director kept
        /// resolving manifests after that fixture's own teardown had called
        /// <c>StarSystemRegistry.Clear()</c>.
        /// </para>
        /// <para>
        /// <c>EndSession</c> rather than a bespoke cleanup: it is the same call the
        /// menu's "leave the campaign" path makes, so this fixture ends a session
        /// exactly the way the game does, and a future change to session teardown is
        /// picked up here for free. The trailing frames let Unity's deferred
        /// <c>Destroy</c> actually retire the objects before the next test looks.
        /// </para>
        /// <para>
        /// Gated on <see cref="bootedASessionThisTest"/> rather than run
        /// unconditionally, for the same reason the broken-content test measures a
        /// delta instead of an absolute: a test that never loaded a scene inherits
        /// whatever the previous fixture left, and this teardown is answerable for
        /// this fixture's own sessions, not for someone else's.
        /// </para>
        /// <para>
        /// The scene is then unloaded, and that part is not optional either.
        /// <c>EndSession</c> deliberately leaves a <c>NebulaMainMenuHost</c> behind
        /// — in the game that is the whole point, since a player who leaves a
        /// campaign has to be looking at something. In a test process there is
        /// nobody looking, and a live main menu is not inert: it gates ship input,
        /// which quietly zeroed the flight controls of the next fixture's tutorial
        /// world and cost <c>BoostCompletesOnlyOnARealRefusedNowhereElse</c> its
        /// boost. Ending the session is what this fixture owes the run; unloading
        /// the scene it single-loaded is what it owes the scene.
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

            // Absolute, not a delta: LoadSceneFresh single-loads the scene, which
            // destroys every object any earlier fixture left, so the session just
            // ended is provably the only one that existed.
            Assert.AreEqual(0, Object.FindObjectsOfType<Nebula.Sectors.SystemStreamingDirector>().Length,
                "this test left a streaming director behind — every later fixture in the run " +
                "would be sharing its world");

            yield return UnloadTheScene();
        }

        /// <summary>
        /// Leaves the process on an empty active scene, with the Nebula scene this
        /// fixture loaded genuinely unloaded.
        /// <para>
        /// A scene cannot be unloaded while it is the only one, hence the empty
        /// stand-in: it is created and made active first, so the unload has
        /// somewhere to leave the process. Anything the test runner owns lives in
        /// <c>DontDestroyOnLoad</c> and is untouched by either step.
        /// </para>
        /// </summary>
        private static IEnumerator UnloadTheScene()
        {
            Scene loaded = SceneManager.GetActiveScene();
            if (!loaded.IsValid() || loaded.path != ScenePath) yield break;

            Scene empty = SceneManager.CreateScene("tf003-empty-" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(loaded);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NebulaBootstrap_BootsTheRealScene_NebulaContextCarriesAFullRuntimeCatalog()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            RuntimeCatalog catalog = adapter.Context.RuntimeCatalog;

            Assert.IsNotNull(catalog);
            Assert.Greater(catalog.Items.Count, 0);
            Assert.Greater(catalog.Modules.Count, 0);
            Assert.Greater(catalog.Vehicles.Count, 0);
            Assert.Greater(catalog.Weapons.Count, 0);
            Assert.Greater(catalog.Enemies.Count, 0);
            Assert.Greater(catalog.PoiTypes.Count, 0);
            Assert.IsTrue(catalog.Modules.ContainsKey("industry.logistics_cargo"));
            Assert.IsTrue(catalog.Weapons.ContainsKey("weapon.plasma"));
        }

        [UnityTest]
        public IEnumerator RestartCampaign_ProducesAFreshRuntimeCatalog_NeverTheSameMutatedInstance()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            RuntimeCatalog before = adapter.Context.RuntimeCatalog;
            ulong fingerprintBefore = ContentFingerprint.Compute(before);

            adapter.RestartCampaign();
            yield return null;
            yield return null;

            RuntimeCatalog after = adapter.Context.RuntimeCatalog;
            Assert.AreNotSame(before, after, "a restart must build a genuinely new RuntimeCatalog instance, never reuse/mutate the old (disposed) one");
            Assert.AreEqual(fingerprintBefore, ContentFingerprint.Compute(after), "the same real static catalogs must still produce identical content after restart");
        }

        [UnityTest]
        public IEnumerator DeliberatelyBrokenContentSource_FailsBootFast_NoPartialWorldPlayerOrDirector()
        {
            // Measured as a before/after DELTA rather than an absolute "must be zero" count --
            // a previous test in this run may already have a fully-booted Nebula.unity loaded
            // (every PlayMode test file in this project single-loads its own scene but never
            // explicitly unloads it afterward), so an absolute zero-count assertion would be a
            // false failure. The real property under test is "this failed boot attempt created
            // nothing", which a delta of zero proves regardless of what already existed.
            int shipsBefore = Object.FindObjectsOfType<ShipController>().Length;
            int streamingBefore = Object.FindObjectsOfType<Nebula.Sectors.SystemStreamingDirector>().Length;
            int extractionBefore = Object.FindObjectsOfType<ExtractionDirector>().Length;

            var config = new NebulaBootstrapConfig(); // content validation runs before config.Validate(), so an all-default config never matters here
            var broken = new BrokenTestContentCatalog();

            bool threw = false;
            try
            {
                NebulaBootstrap.StartSession(config, broken);
            }
            catch (ContentValidationException)
            {
                threw = true;
            }
            Assert.IsTrue(threw, "a broken content source must fail boot with ContentValidationException");

            yield return null;

            Assert.AreEqual(shipsBefore, Object.FindObjectsOfType<ShipController>().Length, "no ship must ever be created when content validation fails");
            Assert.AreEqual(streamingBefore, Object.FindObjectsOfType<Nebula.Sectors.SystemStreamingDirector>().Length, "no streaming director must ever be created");
            Assert.AreEqual(extractionBefore, Object.FindObjectsOfType<ExtractionDirector>().Length, "no extraction director must ever be created");
        }

        /// <summary>An IContentCatalog whose one item has an invalid (uppercase) id — must be rejected by ContentCatalogValidator before RuntimeCatalogBuilder ever runs.</summary>
        private sealed class BrokenTestContentCatalog : IContentCatalog
        {
            public IReadOnlyList<ContentItem> Items { get; } = new List<ContentItem> { new ContentItem("Item.Broken", "Broken", 1f) };
            public IReadOnlyList<ContentRecipe> Recipes { get; } = new List<ContentRecipe>();
            public IReadOnlyList<ContentModule> Modules { get; } = new List<ContentModule>();
            public IReadOnlyList<ContentVehicle> Vehicles { get; } = new List<ContentVehicle>();
            public IReadOnlyList<ContentStructure> Structures { get; } = new List<ContentStructure>();
            public IReadOnlyList<ContentLogisticsCenterTier> LogisticsCenterTiers { get; } = new List<ContentLogisticsCenterTier>();
            public IReadOnlyList<ContentWeapon> Weapons { get; } = new List<ContentWeapon>();
            public IReadOnlyList<ContentEnemy> Enemies { get; } = new List<ContentEnemy>();
            public IReadOnlyList<ContentPoiType> PoiTypes { get; } = new List<ContentPoiType>();
            public IReadOnlyList<string> FreeStartingModuleIds { get; } = new List<string>();
        }
    }
}
