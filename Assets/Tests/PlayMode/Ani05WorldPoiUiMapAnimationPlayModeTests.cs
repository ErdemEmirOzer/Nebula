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
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-05 in a real session: worlds turning without moving, the UI clock
    /// running while the world's is stopped, and lighting's property values
    /// surviving everything the animation writes.
    /// </summary>
    public class Ani05WorldPoiUiMapAnimationPlayModeTests
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
            Time.timeScale = 1f;
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

        private sealed class CountingParticipant : IVisualAnimationParticipant
        {
            public int Ticks;
            public float Accumulated;
            public bool IsAlive => true;
            public void VisualTick(float deltaSeconds) { Ticks++; Accumulated += deltaSeconds; }
            public void ResetToBasePose() { }
        }

        // --- one director, two clocks ------------------------------------------------

        [UnityTest]
        public IEnumerator TheSessionHasExactlyOneDirector_WithBothTimeDomains()
        {
            yield return LoadSceneFresh();

            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length,
                "a UI clock must not mean a second director");
            NebulaAnimationDirector director = Director();
            Assert.GreaterOrEqual(director.ParticipantCount, 0);

            // ANI-05.4 put the player's panels on the UI clock, so the count is
            // now one per panel rather than zero.
            Assert.AreEqual(Object.FindObjectsOfType<UiPanelAnimator>().Length,
                director.UnscaledParticipantCount,
                "the UI clock carries exactly the session's panels");
        }

        [UnityTest]
        public IEnumerator TheUiClockRuns_WhileTheWorldClockIsStopped()
        {
            yield return LoadSceneFresh();
            NebulaAnimationDirector director = Director();

            int unscaledBefore = director.UnscaledParticipantCount;

            var world = new CountingParticipant();
            var ui = new CountingParticipant();
            director.Register(world);
            director.RegisterUnscaled(ui);

            // A paused game gives the world a delta of zero and the UI a real one.
            director.Tick(0f);
            director.TickUnscaled(0.05f);

            Assert.AreEqual(0, world.Ticks, "world animation must stop dead when the game is paused");
            Assert.AreEqual(1, ui.Ticks, "a panel opened during a pause must still animate");

            director.Unregister(world);
            director.Unregister(ui);
            Assert.AreEqual(unscaledBefore, director.UnscaledParticipantCount,
                "unregistering must clear both lists back to what they were");
        }

        [UnityTest]
        public IEnumerator TheUiClockIsUnrated_SoAPanelNeverWaitsOnTheWorldBudget()
        {
            yield return LoadSceneFresh();
            NebulaAnimationDirector director = Director();

            var ui = new CountingParticipant();
            director.RegisterUnscaled(ui);

            for (int i = 0; i < 5; i++) director.TickUnscaled(0.004f);

            Assert.AreEqual(5, ui.Ticks, "every unscaled tick must reach the UI immediately");
            Assert.AreEqual(0.02f, ui.Accumulated, 1e-4f);

            director.Unregister(ui);
        }

        // --- worlds turn without moving -----------------------------------------------

        [UnityTest]
        public IEnumerator EveryPlanetInTheSystem_GetsACelestialPresenter()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;

            var presenters = Object.FindObjectsOfType<CelestialAnimationPresenter>();
            Assert.Greater(presenters.Length, 0, "the materializer must attach a presenter to each world");

            foreach (CelestialAnimationPresenter presenter in presenters)
                Assert.IsFalse(presenter.Motion.IsStill, "a planet must have a real character");
        }

        [UnityTest]
        public IEnumerator APlanetTurns_ButItsGameplayBodyNeverMoves()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            var presenter = Object.FindObjectOfType<CelestialAnimationPresenter>();
            Assert.IsNotNull(presenter);

            var body = presenter.GetComponent<CelestialBody>();
            Vector3 position = presenter.transform.position;
            Quaternion rotation = presenter.transform.rotation;
            float radius = body != null ? body.Radius : 0f;

            for (int i = 0; i < 400; i++) director.Tick(0.02f);

            Assert.AreEqual(position, presenter.transform.position, "a world's orbit is not the animation's to touch");
            Assert.AreEqual(rotation, presenter.transform.rotation, "nor is its gameplay rotation");
            if (body != null) Assert.AreEqual(radius, body.Radius, "nor its radius");
        }

        [UnityTest]
        public IEnumerator TenThousandTicks_LeaveNoUnboundedDrift()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            var presenter = Object.FindObjectOfType<CelestialAnimationPresenter>();
            Assert.IsNotNull(presenter);

            for (int i = 0; i < 10000; i++) director.Tick(0.016f);

            Assert.GreaterOrEqual(presenter.CurrentSpinDegrees, 0f);
            Assert.Less(presenter.CurrentSpinDegrees, 360f,
                "the angle is recomputed from age and must stay wrapped after 10,000 ticks");
        }

        [UnityTest]
        public IEnumerator MotionOff_PutsEveryWorldBackOnItsAuthoredPose()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            // A world built here so the test always has a visual child to pose;
            // a materialized planet whose authored model failed to load has
            // nothing to turn, and doing nothing is the correct answer there.
            var host = new GameObject("ani05-world");
            try
            {
                var child = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                child.transform.SetParent(host.transform, false);

                var presenter = host.AddComponent<CelestialAnimationPresenter>();
                presenter.Configure(CelestialBodyType.Planet, StarClass.Yellow,
                    PlanetArchetype.Biomass, EnvironmentClass.Calm, "ani05-world");
                Assert.IsTrue(presenter.HasVisual, "the presenter must have found the child to pose");

                for (int i = 0; i < 200; i++) director.Tick(0.02f);
                Assert.Greater(presenter.CurrentSpinDegrees, 0f, "it must be turning first");

                Quaternion posed = child.transform.localRotation;

                AnimationAccessibility.MotionOff = true;
                director.Tick(0.02f);

                Assert.AreEqual(0f, presenter.CurrentSpinDegrees, "motion off means the authored pose");
                Assert.AreNotEqual(posed, child.transform.localRotation, "and the model actually snaps back");
            }
            finally { Object.DestroyImmediate(host); }
            yield return null;
        }

        // --- §1 the invariant this part exists for -----------------------------------------

        [UnityTest]
        public IEnumerator AnimationScrolling_NeverWipesLightingsPropertyValues()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            // Find a world whose surface really flows, so scrolling actually runs.
            CelestialAnimationPresenter flowing = null;
            foreach (CelestialAnimationPresenter presenter in Object.FindObjectsOfType<CelestialAnimationPresenter>())
                if (presenter.ScrollingRendererCount > 0) { flowing = presenter; break; }

            if (flowing == null) Assert.Ignore("this system materialized no surface-flow world");

            Renderer renderer = flowing.GetComponentInChildren<Renderer>(true);
            Assert.IsNotNull(renderer);

            // Whatever LGT put on this renderer, read it back now.
            var before = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(before);
            Color emissionBefore = before.GetColor(MaterialPropertyChannels.EmissionColorId);
            float metallicBefore = before.GetFloat(MaterialPropertyChannels.MetallicId);

            for (int i = 0; i < 200; i++) director.Tick(0.02f);

            var after = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(after);

            Assert.AreEqual(emissionBefore, after.GetColor(MaterialPropertyChannels.EmissionColorId),
                "animation must never clear the emission LGT set");
            Assert.AreEqual(metallicBefore, after.GetFloat(MaterialPropertyChannels.MetallicId), 1e-4f,
                "nor the metallic value");
        }

        [UnityTest]
        public IEnumerator NoRuntimeMaterialCopyIsLeaked()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            var presenter = Object.FindObjectOfType<CelestialAnimationPresenter>();
            Assert.IsNotNull(presenter);
            Renderer renderer = presenter.GetComponentInChildren<Renderer>(true);
            Assert.IsNotNull(renderer);

            Material shared = renderer.sharedMaterial;
            for (int i = 0; i < 200; i++) director.Tick(0.02f);

            Assert.AreSame(shared, renderer.sharedMaterial,
                "scrolling must go through a property block, never renderer.material");
        }

        // --- session hygiene ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesOneDirectorAndNoOrphans()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            for (int i = 0; i < 4; i++) yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount,
                "the previous session's presenters must not linger");
            Assert.AreEqual(Object.FindObjectsOfType<UiPanelAnimator>().Length,
                Director().UnscaledParticipantCount,
                "the new session's UI clock carries its own panels and no orphans");
        }

        [UnityTest]
        public IEnumerator TheCelestialPoolAndParticipantsStayBounded()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            int participantsAfterBoot = director.ParticipantCount;
            for (int i = 0; i < 600; i++) director.Tick(0.016f);

            Assert.AreEqual(participantsAfterBoot, director.ParticipantCount,
                "ticking must not register anything new");
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount);
            yield return null;
        }
    }
}
