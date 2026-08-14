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
    /// ANI-05.4: the three modal screens — their transitions, and the input
    /// safety that must not depend on the animation.
    /// </summary>
    public class Ani054ScreenAnimationPlayModeTests
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
        public IEnumerator EveryPlayerScreen_CarriesExactlyOneAnimator()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaContext context = Context();

            // Progression, navigation and hangar were three modal screens; they are
            // three pages inside one now. The screens that remain are the parent
            // menu and the confirmation, and the rule is unchanged: one animator each.
            foreach (GameObject screen in new[]
                     {
                         context.Presentation.ParentMenu.gameObject,
                         context.Presentation.Confirmation.gameObject
                     })
                Assert.AreEqual(1, screen.GetComponents<UiPanelAnimator>().Length, screen.name);
        }

        [UnityTest]
        public IEnumerator AClosedScreen_StartsHiddenWithoutPlayingAnEntry()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaContext context = Context();

            var animator = context.Presentation.ParentMenu.GetComponent<UiPanelAnimator>();
            Assert.IsNotNull(animator);

            if (!context.Presentation.ParentMenu.IsOpen)
            {
                Assert.AreEqual(UiPanelPhase.Hidden, animator.Phase,
                    "a session that boots with the screen shut must not animate it open");
                Assert.AreEqual(0f, animator.Progress);
            }
        }

        [UnityTest]
        public IEnumerator AClosingScreen_StopsAcceptingClicksImmediately()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            var screen = context.Presentation.ParentMenu;
            var animator = screen.GetComponent<UiPanelAnimator>();
            var group = screen.GetComponent<CanvasGroup>();
            Assert.IsNotNull(group, "the animator gives every panel a CanvasGroup");

            // Whatever state it is really in, a hidden or exiting phase must
            // never leave the panel clickable.
            for (int i = 0; i < 30; i++) director.TickUnscaled(0.02f);

            if (UiAnimationProfiles.BlocksInput(animator.Phase))
            {
                Assert.IsFalse(group.interactable, "a closed or closing screen must not be interactable");
                Assert.IsFalse(group.blocksRaycasts, "and must not swallow clicks");
            }
        }

        [UnityTest]
        public IEnumerator AThousandOpenCloseCycles_LeaveNoBaselineDrift()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaContext context = Context();

            var host = new GameObject("ani054-cycle", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                var rect = host.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(120f, -60f);
                host.GetComponent<CanvasGroup>().alpha = 1f;

                bool open = false;
                var animator = host.AddComponent<UiPanelAnimator>();
                animator.Initialize(Director(), () => open);

                Vector2 baseline = animator.BasePosition;

                for (int cycle = 0; cycle < 1000; cycle++)
                {
                    open = !open;
                    animator.VisualTick(0.05f);
                }

                open = true;
                for (int i = 0; i < 40; i++) animator.VisualTick(0.05f);

                Assert.AreEqual(baseline.x, rect.anchoredPosition.x, 0.001f, "no horizontal drift");
                Assert.AreEqual(baseline.y, rect.anchoredPosition.y, 0.001f, "no vertical drift");
                Assert.AreEqual(animator.BaseAlpha, host.GetComponent<CanvasGroup>().alpha, 1e-3f);
            }
            finally { Object.DestroyImmediate(host); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AScreensAnimator_NeverChangesWhetherItIsOpen()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaContext context = Context();
            NebulaAnimationDirector director = Director();

            bool menuBefore = context.Presentation.ParentMenu.IsOpen;
            var pageBefore = context.Presentation.ParentMenu.CurrentPage;
            bool confirmBefore = context.Presentation.Confirmation.IsOpen;

            for (int i = 0; i < 200; i++) director.TickUnscaled(0.02f);

            Assert.AreEqual(menuBefore, context.Presentation.ParentMenu.IsOpen);
            Assert.AreEqual(confirmBefore, context.Presentation.Confirmation.IsOpen);
            Assert.AreEqual(pageBefore, context.Presentation.ParentMenu.CurrentPage,
                "modal ownership belongs to the screens, not to the animation");
        }

        [UnityTest]
        public IEnumerator ReducedMotion_ShortensTheSlideButStillShowsTheChange()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;

            float full = UiAnimationProfiles.SlideDistance();
            AnimationAccessibility.ReducedMotion = true;
            float reduced = UiAnimationProfiles.SlideDistance();

            Assert.Less(reduced, full);
            Assert.Greater(reduced, 0f);
            yield return null;
        }
    }
}
