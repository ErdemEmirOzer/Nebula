using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-08: the settings page. Every control binds to a setting that already
    /// existed and already worked — what was missing was a way to reach it. These
    /// tests check the binding, not the settings themselves.
    /// </summary>
    public class NebulaSettingsPageTests
    {
        private NebulaUiShell shell;
        private NebulaSettingsPage page;

        private bool savedReducedMotion, savedMotionOff, savedCameraComfort;
        private bool savedReduceFlash, savedCue, savedHighContrast, savedSafeMode;
        private float savedHudScale, savedUiScale;

        [SetUp]
        public void Setup()
        {
            savedReducedMotion = AnimationAccessibility.ReducedMotion;
            savedMotionOff = AnimationAccessibility.MotionOff;
            savedCameraComfort = AnimationAccessibility.CameraComfort;
            savedReduceFlash = LightingAccessibility.FlashReduction;
            savedCue = CombatAccessibilitySettings.AudioVisualCueForWarnings;
            savedHighContrast = LightingAccessibility.HighContrast;
            savedSafeMode = LightingAccessibility.PhotosensitivitySafeMode;
            savedHudScale = CombatAccessibilitySettings.HudScale;
            savedUiScale = NebulaUiShell.UiScale;

            ExclusiveModal.ResetForSession();
            shell = NebulaUiShell.Create();

            var host = new GameObject("Host", typeof(RectTransform));
            host.transform.SetParent(shell.Root(UiLayer.Menu), false);
            page = new NebulaSettingsPage();
            page.Build((RectTransform)host.transform);
        }

        [TearDown]
        public void Teardown()
        {
            if (shell != null) Object.DestroyImmediate(shell.gameObject);
            AnimationAccessibility.ReducedMotion = savedReducedMotion;
            AnimationAccessibility.MotionOff = savedMotionOff;
            AnimationAccessibility.CameraComfort = savedCameraComfort;
            LightingAccessibility.FlashReduction = savedReduceFlash;
            CombatAccessibilitySettings.AudioVisualCueForWarnings = savedCue;
            LightingAccessibility.HighContrast = savedHighContrast;
            LightingAccessibility.PhotosensitivitySafeMode = savedSafeMode;
            CombatAccessibilitySettings.HudScale = savedHudScale;
            NebulaUiShell.UiScale = savedUiScale;
            ExclusiveModal.ResetForSession();
        }

        [Test]
        public void PageExposesEveryAccessibilitySettingTheGameAlreadyHonours()
        {
            // Interface: UI scale, HUD scale.
            // Motion: reduced motion, motion off, camera comfort.
            // Accessibility: flash reduction, photosensitivity safe mode,
            //   warning cue, high contrast.
            // Graphics: quality, vertical sync.
            // Audio adds six more when a session is running; this page is built
            // without one, so its bus rows are absent here by design.
            const int EngineSettings = 11;

            // Plus one reading per featured action the player may NOT rebind. That
            // count is the catalog's to state, not this test's: it was pinned at eight
            // and Part 3/4 made four of those eight genuinely rebindable, which moved
            // them into the binding panel — the player gained a control and the test
            // went red for it. A literal here would have to be re-pinned every time the
            // catalog grows a row, and re-pinning a number is how a test stops meaning
            // anything. Derived, it still fails for the case worth failing on: a
            // setting that leaves the page without becoming rebindable anywhere.
            int readings = 0;
            foreach (InputActionDefinition action in NebulaInputActions.PlayerVisible(Debug.isDebugBuild))
                if (action.Featured && !action.Rebindable) readings++;

            Assert.Greater(readings, 0,
                "no featured non-rebindable action left — the controls section would be an empty heading");

            Assert.AreEqual(EngineSettings + readings, page.RowCount,
                "A setting the engine honours but the player cannot reach may as well not exist.");
        }

        [Test]
        public void RefreshReadsLiveValues_NotACachedSnapshot()
        {
            AnimationAccessibility.MotionOff = false;
            page.Refresh();

            AnimationAccessibility.MotionOff = true;
            Assert.DoesNotThrow(() => page.Refresh(),
                "The page must tolerate a setting changing underneath it.");
            Assert.IsTrue(AnimationAccessibility.MotionOff,
                "Refreshing must not write back over a live value.");
        }

        [Test]
        public void UiScaleStaysWithinTheSupportedRange()
        {
            NebulaUiShell.UiScale = 99f;
            Assert.LessOrEqual(NebulaUiShell.UiScale, NebulaUiShell.MaxUiScale);

            NebulaUiShell.UiScale = -5f;
            Assert.GreaterOrEqual(NebulaUiShell.UiScale, NebulaUiShell.MinUiScale);
        }

        [Test]
        public void HudScaleStaysWithinTheExistingAccessibilityRange()
        {
            // 75-150% is a pre-existing contract; the settings page must not widen it.
            CombatAccessibilitySettings.HudScale = CombatAccessibilitySettings.ClampHudScale(99f);
            Assert.LessOrEqual(CombatAccessibilitySettings.HudScale, CombatAccessibilitySettings.MaxHudScale);

            CombatAccessibilitySettings.HudScale = CombatAccessibilitySettings.ClampHudScale(0f);
            Assert.GreaterOrEqual(CombatAccessibilitySettings.HudScale, CombatAccessibilitySettings.MinHudScale);
        }

        [Test]
        public void SettingsRaiseNoAttentionBadge()
        {
            Assert.AreEqual(0, page.AttentionCount,
                "An attention badge on Settings would train the player to ignore attention badges.");
        }

        [Test]
        public void SettingsOwnNoNestedState_SoBackFallsThrough()
        {
            Assert.IsFalse(page.HandleBack());
        }

        [Test]
        public void MotionOffReachesTheTokenLayer()
        {
            // The page is only useful if the setting it writes is the one the
            // interface actually reads.
            AnimationAccessibility.MotionOff = true;
            Assert.IsTrue(NebulaUiDesignTokens.SnapTransitions);

            AnimationAccessibility.MotionOff = false;
            Assert.IsFalse(NebulaUiDesignTokens.SnapTransitions);
        }

        /// <summary>
        /// The row was bound to <c>CombatAccessibilitySettings.ReduceFlash</c>,
        /// which nothing outside this page reads -- so the toggle flipped its own
        /// label and left the picture alone. This asserts the switch the engine
        /// honours, not the one that merely has the right name.
        /// </summary>
        [Test]
        public void FlashReductionReachesTheSystemsThatHonourIt()
        {
            LightingAccessibility.PhotosensitivitySafeMode = false;

            LightingAccessibility.FlashReduction = false;
            float full = LightingAccessibility.ScaleFlashIntensity(1f);
            float repeatFull = UiAnimationProfiles.MaxRepeatHz;

            LightingAccessibility.FlashReduction = true;
            Assert.Less(LightingAccessibility.ScaleFlashIntensity(1f), full,
                "Flash Reduction did not reach the flash the player sees.");
            Assert.Less(UiAnimationProfiles.MaxRepeatHz, repeatFull,
                "Flash Reduction did not reach the repeat ceiling.");
        }

        /// <summary>
        /// Safe Mode had no control at all: four runtime systems branch on it and
        /// the player could not reach any of them.
        /// </summary>
        [Test]
        public void PhotosensitivitySafeModeReachesTheRepeatCeiling()
        {
            LightingAccessibility.PhotosensitivitySafeMode = false;
            Assert.IsTrue(NebulaUiDesignTokens.AllowsRepeatingEmphasis);

            LightingAccessibility.PhotosensitivitySafeMode = true;
            Assert.IsFalse(NebulaUiDesignTokens.AllowsRepeatingEmphasis,
                "Safe Mode must stop every repeating emphasis, not soften it.");
            Assert.AreEqual(0f, UiAnimationProfiles.MaxRepeatHz);

            // It also outranks Flash Reduction downward, which is the guarantee.
            LightingAccessibility.FlashReduction = false;
            Assert.AreEqual(LightingAccessibility.PhotosensitivityFlashFactor,
                LightingAccessibility.ScaleFlashIntensity(1f), 1e-4f);
        }

        [Test]
        public void HighContrastReachesTheStatusColours()
        {
            LightingAccessibility.HighContrast = false;
            Color plain = NebulaUiDesignTokens.Status(UiStatusLevel.Critical);

            LightingAccessibility.HighContrast = true;
            Assert.AreNotEqual(plain, NebulaUiDesignTokens.Status(UiStatusLevel.Critical),
                "High Contrast did not reach the colours the new UI draws with.");
        }
    }
}
