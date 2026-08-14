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
using Nebula.Extraction;
using Nebula.UI;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-05.4: the UI layer must not grow, must not touch the world clock,
    /// and must finish its transitions while the game is paused.
    /// </summary>
    public class Ani054UiPerformanceAndAccessibilityPlayModeTests
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
            foreach (string f in Slot1Files()) slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
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
            var d = Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(d);
            return d;
        }

        private static NebulaContext Context() => Object.FindObjectOfType<SolarSystemBootstrap>().Context;

        [UnityTest]
        public IEnumerator TheUiClockRunsWhileTheWorldIsFrozen()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            long worldBefore = director.TickCount;
            long uiBefore = director.UnscaledTickCount;

            // What a pause really is: no world delta, real unscaled delta.
            Time.timeScale = 0f;
            for (int i = 0; i < 20; i++)
            {
                director.Tick(0f);
                director.TickUnscaled(0.02f);
            }

            Assert.AreEqual(worldBefore, director.TickCount, "world animation must not advance while paused");
            Assert.Greater(director.UnscaledTickCount, uiBefore, "but the UI must keep working");
        }

        [UnityTest]
        public IEnumerator APanelCompletesItsTransitionWhilePaused()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;

            var host = new GameObject("ani054-paused", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                bool open = false;
                var animator = host.AddComponent<UiPanelAnimator>();
                animator.Initialize(Director(), () => open);

                Time.timeScale = 0f;
                open = true;
                for (int i = 0; i < 40; i++) animator.VisualTick(0.02f);

                Assert.AreEqual(UiPanelPhase.Shown, animator.Phase,
                    "a panel opened during a pause must still finish opening");
                Assert.AreEqual(1f, animator.Progress, 1e-3f);
            }
            finally { Object.DestroyImmediate(host); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AThousandCycles_GrowNoParticipantOrObject()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            int unscaledBefore = director.UnscaledParticipantCount;
            int worldBefore = director.ParticipantCount;
            int groupsBefore = Object.FindObjectsOfType<CanvasGroup>().Length;
            int animatorsBefore = Object.FindObjectsOfType<UiPanelAnimator>().Length;

            for (int i = 0; i < 1000; i++) director.TickUnscaled(0.02f);

            Assert.AreEqual(unscaledBefore, director.UnscaledParticipantCount,
                "1000 UI ticks must not register anything new");
            Assert.AreEqual(worldBefore, director.ParticipantCount,
                "and must not touch the world channel");
            Assert.AreEqual(groupsBefore, Object.FindObjectsOfType<CanvasGroup>().Length,
                "no CanvasGroup may be created per tick");
            Assert.AreEqual(animatorsBefore, Object.FindObjectsOfType<UiPanelAnimator>().Length);
        }

        [UnityTest]
        public IEnumerator TheUiParticipantCount_IsBoundedByThePanelCount()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            int animators = Object.FindObjectsOfType<UiPanelAnimator>().Length;
            Assert.Greater(animators, 0, "the panels must be wired");
            Assert.LessOrEqual(director.UnscaledParticipantCount, animators + 2,
                "the UI clock carries one participant per panel, not per widget");
        }

        [UnityTest]
        public IEnumerator TheUiAddsNoLightAndNoBody()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;

            foreach (UiPanelAnimator animator in Object.FindObjectsOfType<UiPanelAnimator>())
            {
                Assert.IsNull(animator.GetComponent<Light>(), $"{animator.name} must add no light");
                Assert.IsNull(animator.GetComponent<Rigidbody>(), $"{animator.name} must add no body");
            }
        }

        [UnityTest]
        public IEnumerator MotionOff_SnapsAPanelWithNoIntermediateFrame()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;

            var host = new GameObject("ani054-snap", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                bool open = false;
                var animator = host.AddComponent<UiPanelAnimator>();
                animator.Initialize(Director(), () => open);

                AnimationAccessibility.MotionOff = true;
                open = true;
                animator.VisualTick(0.001f);

                Assert.AreEqual(1f, animator.Progress, "motion off means one frame, not a transition");
                Assert.AreEqual(UiPanelPhase.Shown, animator.Phase);

                open = false;
                animator.VisualTick(0.001f);
                Assert.AreEqual(0f, animator.Progress);
            }
            finally { Object.DestroyImmediate(host); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SafeMode_LeavesNoRepeatingEmphasisAnywhere()
        {
            yield return LoadSceneFresh();
            LightingAccessibility.PhotosensitivitySafeMode = true;

            Assert.IsFalse(UiAnimationProfiles.AllowsRepeatingEmphasis,
                "no critical HUD state may blink under Safe Mode");
            Assert.AreEqual(0f, UiAnimationProfiles.MaxRepeatHz);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheVersionsAreUntouched()
        {
            yield return LoadSceneFresh();
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion); // P2-SAVE-201: absent schema is unknown, never v13
            Assert.AreEqual(3, Nebula.Sectors.SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, Nebula.Content.ContentFingerprint.ContentRevision);
        }
    }
}
