using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Accessibility;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Photographs every interaction state of a real widget, driven the way a
    /// player drives it.
    /// <para>
    /// The page captures show ten screens sitting still. They cannot show whether
    /// a control answers a hover, whether a press reads as a press, whether a
    /// player driving the interface from the keyboard can see where the focus is,
    /// or whether a disabled control looks disabled. Those are the states a
    /// screenshot suite never catches and a player meets on the first click.
    /// </para>
    /// <para>
    /// Every state here is reached through the real uGUI path -- pointer events
    /// sent through <see cref="ExecuteEvents"/>, focus set on the real
    /// <see cref="EventSystem"/>, availability changed through the real
    /// <c>SetAvailable</c> API. Nothing hand-sets a sprite. That distinction is
    /// the whole point: assigning the hover sprite directly would photograph the
    /// art and prove nothing about whether the widget is wired to it, and the
    /// wiring is exactly what was missing -- ten navigation tabs and every list
    /// row in the interface were built with a transition that could not draw.
    /// </para>
    /// <para>
    /// Each capture is compared against the same widget's resting frame and must
    /// differ. A state that renders identically to Default is not a passing
    /// capture; it is the finding.
    /// </para>
    /// </summary>
    // Graphics: every test here renders a live camera into a RenderTexture and reads the pixels back. Under -nographics the
    // render is a segfault risk in RenderOffscreenCameras, not merely a blank frame, so this
    // fixture must never be selected into a headless worker. It carries no ReleaseRegression
    // category on purpose -- the headless gate selects by that name and must not reach here.
    [Category("Graphics")]
    public class UiFinalInteractionStateCaptureTests
    {
        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime");

        /// <summary>Every file this fixture writes. The disk audit reads the same list.</summary>
        public static readonly string[] StateCaptures =
        {
            "state-button-default.png",
            "state-button-hover.png",
            "state-button-pressed.png",
            "state-button-focused.png",
            "state-button-disabled.png",
            "state-tab-default.png",
            "state-tab-hover.png",
            "state-tab-pressed.png",
            "state-tab-focused.png",
            "state-tab-selected.png",
            "state-row-default.png",
            "state-row-hover.png",
            "state-row-pressed.png",
            "state-row-focused.png",
            "state-settings-control-default.png",
            "state-settings-control-hover.png",
            "state-settings-control-pressed.png",
            "state-settings-control-focused.png",
            "state-settings-control-disabled.png",
            "state-slider-default.png",
            "state-slider-hover.png",
            "state-slider-pressed.png",
            "state-slider-focused.png",
            "state-slider-disabled.png",
            "state-toggle-off.png",
            "state-toggle-on.png",
            "state-toggle-hover.png",
            "state-toggle-focused.png",
            "state-toggle-disabled.png",
            "state-longtext-turkish.png",
        };

        /// <inheritdoc cref="AccessibilityModes"/>
        public static readonly string[] AccessibilityCaptures =
        {
            "state-a11y-baseline.png",
            "state-a11y-motion-off.png",
            "state-a11y-reduced-motion.png",
            "state-a11y-flash-reduction.png",
            "state-a11y-photosensitivity-safe.png",
            "state-a11y-high-contrast.png",
            "state-a11y-visual-cue.png",
        };

        private const int Width = 1280;
        private const int Height = 720;

        private NebulaUiShell shell;
        private GameObject eventSystemObject;
        private EventSystem eventSystem;

        /// <summary>
        /// True only when this fixture is the thing that installed the
        /// EventSystem, so teardown destroys what it created and nothing else.
        /// </summary>
        private bool ownsEventSystem;

        private bool savedReducedMotion, savedMotionOff, savedCameraComfort;
        private bool savedFlash, savedSafeMode, savedHighContrast, savedCue;
        private float savedUiScale;

        [SetUp]
        public void Setup()
        {
            // Nothing here waits on seconds, but the shell's transitions run on
            // scaled time, so the single frame each capture waits has to be a frame
            // that actually advances. Normalized rather than restored-as-found, for
            // the reason spelled out in VisualAssetsPart1IntegrationTests: a
            // neighbour's stopped clock is stopped here, not carried forward.
            if (!Mathf.Approximately(Time.timeScale, 1f))
                Debug.LogWarning($"[test isolation] Time.timeScale was {Time.timeScale} on "
                                 + "entry to UiFinalInteractionStateCaptureTests; a previous "
                                 + "fixture did not restore it.");
            Time.timeScale = 1f;

            savedReducedMotion = AnimationAccessibility.ReducedMotion;
            savedMotionOff = AnimationAccessibility.MotionOff;
            savedCameraComfort = AnimationAccessibility.CameraComfort;
            savedFlash = LightingAccessibility.FlashReduction;
            savedSafeMode = LightingAccessibility.PhotosensitivitySafeMode;
            savedHighContrast = LightingAccessibility.HighContrast;
            savedCue = Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings;
            savedUiScale = NebulaUiShell.UiScale;

            Directory.CreateDirectory(OutputDir);
            ExclusiveModal.ResetForSession();

            // Exactly one EventSystem in the scene, ever.
            // NebulaUiShell.Create installs one when the scene has none, so a
            // fixture that unconditionally installs its own leaves two behind and
            // makes EventSystem.current depend on destruction order rather than on
            // intent. UiFinalLifecyclePerformanceTests asserts the single-instance
            // invariant; this fixture holds to it rather than working around it.
            eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                eventSystemObject = new GameObject("Interaction EventSystem",
                    typeof(EventSystem), typeof(StandaloneInputModule));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
                ownsEventSystem = true;
            }

            shell = NebulaUiShell.Create();

            // The shell adopts an existing EventSystem and installs one otherwise,
            // so after this line there is one and it is the one to drive.
            eventSystem = EventSystem.current;
            Assert.IsNotNull(eventSystem,
                "there is no EventSystem to drive the interface with — every pointer and "
                + "focus state in this fixture would be photographing a widget nothing "
                + "was ever sent to.");
        }

        [TearDown]
        public void Teardown()
        {
            // Selection first, and before anything it points at is destroyed.
            if (eventSystem != null) eventSystem.SetSelectedGameObject(null);

            if (shell != null)
            {
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
                shell = null;
            }

            // Only what this fixture created, and never by assigning to
            // EventSystem.current. That setter takes an index into Unity's own
            // registry and logs an error for anything it does not find -- including
            // null -- and the PlayMode runner fails a test on any unhandled error
            // log. Restoring "whatever was current before" through it turned ten
            // passing captures red in teardown without one assertion firing. The
            // getter already falls back to whatever is still registered, so there
            // is nothing to restore: destroying ours is the whole of it.
            if (ownsEventSystem && eventSystemObject != null)
                Object.DestroyImmediate(eventSystemObject);
            eventSystemObject = null;
            ownsEventSystem = false;
            eventSystem = null;

            AnimationAccessibility.ReducedMotion = savedReducedMotion;
            AnimationAccessibility.MotionOff = savedMotionOff;
            AnimationAccessibility.CameraComfort = savedCameraComfort;
            LightingAccessibility.FlashReduction = savedFlash;
            LightingAccessibility.PhotosensitivitySafeMode = savedSafeMode;
            LightingAccessibility.HighContrast = savedHighContrast;
            Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = savedCue;
            NebulaUiShell.UiScale = savedUiScale;
            Time.timeScale = 1f;

            ExclusiveModal.ResetForSession();
        }

        /// <summary>
        /// Without a device <c>Camera.Render</c> draws nothing and
        /// <c>ReadPixels</c> hands back a flat rectangle, which is how seventeen
        /// blank PNGs once shipped green. Skipping is the only honest outcome, and
        /// it has to say so out loud: an ignored result names the missing device, so
        /// the twenty-seven files this fixture owns are visibly not-produced rather
        /// than quietly counted as passing.
        /// </summary>
        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore(
                    "SKIPPED — no graphics device (graphicsDeviceType=Null, i.e. -nographics). "
                    + "This fixture writes the 27 state captures under UIDesign/Runtime that "
                    + "UiFinalResolutionAccessibilityTests audits; without a device it can only "
                    + "produce flat rectangles. Re-run in the shots/liveshots configuration.");
        }

        /// <summary>
        /// A widget caught mid-transition photographs differently on every frame,
        /// which would let <see cref="AssertAllDistinct"/> pass without the state
        /// ever having been reached — the frames would differ because time passed,
        /// not because the control answered. Motion Off makes every transition land
        /// inside the single frame each capture waits, so a frame that differs
        /// differs for the one reason this fixture is measuring.
        /// <para>
        /// Not used by the accessibility test, which sets each mode itself.
        /// </para>
        /// </summary>
        private static void FreezeMotionSoAFrameIsReproducible()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
            AnimationAccessibility.MotionOff = true;
            Assert.IsTrue(NebulaUiDesignTokens.SnapTransitions,
                "state captures need transitions to snap, or a capture photographs a tween");
        }

        // ---- 1. a main-menu button ------------------------------------------

        /// <summary>
        /// The first button a player ever touches, photographed on the real main
        /// menu rather than on a button built for the photograph.
        /// <para>
        /// A synthesized <c>ButtonControl</c> proves the factory works. It does not
        /// prove that the screen shipping to players wired one up, and the screen
        /// is where the wiring has actually been missing before.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator MainMenuButton_LooksDifferentInEveryState()
        {
            RequireGraphicsDevice();
            FreezeMotionSoAFrameIsReproducible();

            NebulaMainMenuScreen menu = NebulaMainMenuScreen.Create(shell, new SilentLauncher());
            menu.OnOpened(shell);
            yield return null;

            Button entry = FindButton(shell.Root(UiLayer.Menu), "New");
            Assert.IsNotNull(entry, "the main menu built no \"YENİ SEFER\" entry to photograph");

            // The baseline has to be the resting state, and on this screen it is not the
            // state the button is in when the screen opens.
            //
            // OnOpened calls FocusInitialControl (NebulaMainMenuScreen.cs:681-693), which
            // selects DEVAM ET when there is a campaign to continue and YENİ SEFER
            // otherwise — and in this fixture there is no save, so the entry photographed
            // here is already the selected one before the first shutter. Unity ranks
            // Selected above Highlighted in Selectable.currentSelectionState, so from
            // that baseline "hover" cannot change anything and "focus it" is a no-op,
            // while "press" and "disable" both outrank Selected and do change. That is
            // exactly the 2-differ/2-identical pattern this test reported, and it is a
            // property of the baseline rather than of the widget's wiring.
            //
            // Cleared, so each frame below is the state it is named after. This is a
            // state a player really reaches — arrowing to the next entry returns this one
            // to Normal — and it strengthens the assertion rather than relaxing it: with
            // the baseline honest, all five states must now differ from one another.
            Focus(null);
            yield return null;

            var frames = new Dictionary<string, byte[]>();
            frames["default"] = Capture("state-button-default.png");

            PointerEnter(entry.gameObject);
            yield return null;
            frames["hover"] = Capture("state-button-hover.png");

            // Asked separately from the pixels, and asked here because two very
            // different faults produce one identical pair of frames: a control that
            // never answered the hover, and a control that answered it perfectly in a
            // frame the camera was not showing. Unity's SpriteSwap writes
            // overrideSprite (never sprite), so this reads the swap itself. It adds a
            // claim, it does not replace AssertAllDistinct below — a button whose art
            // changes in a frame no player can see is still a finding.
            var face = entry.targetGraphic as Image;
            Assert.IsNotNull(face, "the menu entry has no target graphic to change state with");
            Assert.AreEqual(Selectable.Transition.SpriteSwap, entry.transition,
                "the menu entry does not swap art per state at all — Transition is "
                + entry.transition + ", so no state can ever look different");
            Assert.IsNotNull(entry.spriteState.highlightedSprite,
                "the menu entry carries no hover sprite, so hover cannot differ from default");
            Assert.AreEqual(entry.spriteState.highlightedSprite, face.overrideSprite,
                "the menu entry did not take its hover art on a real pointerEnter. "
                + $"overrideSprite={(face.overrideSprite == null ? "null" : face.overrideSprite.name)}, "
                + $"sprite={(face.sprite == null ? "null" : face.sprite.name)}, "
                + $"interactable={entry.interactable}, active={entry.gameObject.activeInHierarchy}, "
                + $"alpha={(entry.GetComponentInParent<CanvasGroup>() == null ? 1f : entry.GetComponentInParent<CanvasGroup>().alpha):0.##}");

            AssertEachStateChangesTheFaceAndNotJustItsEdge(entry);

            PointerDown(entry.gameObject);
            yield return null;
            frames["pressed"] = Capture("state-button-pressed.png");

            PointerUp(entry.gameObject);
            PointerExit(entry.gameObject);
            yield return null;

            Focus(entry.gameObject);
            yield return null;
            frames["focused"] = Capture("state-button-focused.png");

            Focus(null);
            // Through Selectable, which is the path the screen's own Refresh uses
            // when Continue has no save to resume; nothing here hand-sets a sprite.
            entry.interactable = false;
            yield return null;
            frames["disabled"] = Capture("state-button-disabled.png");

            Object.DestroyImmediate(menu.gameObject);
            AssertAllDistinct(frames, "main-menu button");
        }

        /// <summary>A launcher that records instead of launching. Nothing here may start a session.</summary>
        private sealed class SilentLauncher : Nebula.UI.Session.INebulaSessionLauncher
        {
            public void StartNewCampaign() { }
            public void ContinueFromSlot(int slot) { }
            public void QuitGame() { }
            public void ReturnToMainMenu() { }
            public void RestartCampaign() { }
        }

        // ---- 2. a navigation tab --------------------------------------------

        [UnityTest]
        public IEnumerator NavigationTab_LooksDifferentInEveryState()
        {
            RequireGraphicsDevice();
            FreezeMotionSoAFrameIsReproducible();

            NebulaParentMenuScreen menu = BuildMenu();
            menu.Open(NebulaUiAssetCatalog.Page.Overview);
            yield return null;

            // The rail entry for a page that is NOT open, so hover and focus are
            // measured against a resting tab rather than against the selected one.
            GameObject tab = FindRailEntry(NebulaUiAssetCatalog.Page.Fleet);
            Assert.IsNotNull(tab, "the Fleet rail entry is missing from the menu");

            var frames = new Dictionary<string, byte[]>();
            frames["default"] = Capture("state-tab-default.png");

            PointerEnter(tab);
            yield return null;
            frames["hover"] = Capture("state-tab-hover.png");

            PointerDown(tab);
            yield return null;
            frames["pressed"] = Capture("state-tab-pressed.png");

            PointerUp(tab);
            PointerExit(tab);
            yield return null;

            Focus(tab);
            yield return null;
            frames["focused"] = Capture("state-tab-focused.png");

            Focus(null);
            // Selected is a different fact from focused: this is the open page.
            menu.ShowPage(NebulaUiAssetCatalog.Page.Fleet);
            yield return null;
            frames["selected"] = Capture("state-tab-selected.png");

            AssertAllDistinct(frames, "navigation tab");
        }

        // ---- 3. a list row ---------------------------------------------------

        [UnityTest]
        public IEnumerator ModuleRow_LooksDifferentInEveryState()
        {
            RequireGraphicsDevice();
            FreezeMotionSoAFrameIsReproducible();

            NebulaParentMenuScreen menu = BuildMenu();
            menu.Open(NebulaUiAssetCatalog.Page.Progression);
            menu.ShowPage(NebulaUiAssetCatalog.Page.Progression);
            yield return null;

            GameObject row = FirstRowWithAButton(
                Find(NebulaUiAssetCatalog.Page.Progression + " Page"));
            Assert.IsNotNull(row, "the progression page built no clickable module rows");

            var frames = new Dictionary<string, byte[]>();
            frames["default"] = Capture("state-row-default.png");

            PointerEnter(row);
            yield return null;
            frames["hover"] = Capture("state-row-hover.png");

            PointerDown(row);
            yield return null;
            frames["pressed"] = Capture("state-row-pressed.png");

            PointerUp(row);
            PointerExit(row);
            yield return null;

            Focus(row);
            yield return null;
            frames["focused"] = Capture("state-row-focused.png");
            Focus(null);

            AssertAllDistinct(frames, "module row");
        }

        // ---- 4. a settings control ------------------------------------------

        [UnityTest]
        public IEnumerator SettingsControl_LooksDifferentInEveryState()
        {
            RequireGraphicsDevice();
            FreezeMotionSoAFrameIsReproducible();

            RectTransform host = Host("Settings Host");
            var page = new NebulaSettingsPage();
            page.Build(host);
            yield return null;

            NebulaUiFactory.ButtonHandle probe = NebulaUiFactory.ButtonControl(host, "Probe",
                "DEĞİŞTİR", NebulaUiAssetCatalog.ButtonKind.Secondary, () => { });
            RectTransform probeRect = probe.Button.GetComponent<RectTransform>();
            NebulaUiFactory.Place(probeRect, new Vector2(0.5f, 0.5f), new Vector2(0f, -260f),
                new Vector2(96f, NebulaUiDesignTokens.MinimumHitTarget));

            // The design system's own floor, asserted on a real control rather
            // than trusted: this page shipped 30px controls against a stated 32.
            Assert.GreaterOrEqual(probeRect.sizeDelta.y, NebulaUiDesignTokens.MinimumHitTarget,
                "a settings control is below the minimum hit target");

            var frames = new Dictionary<string, byte[]>();
            frames["default"] = Capture("state-settings-control-default.png");

            PointerEnter(probe.Button.gameObject);
            yield return null;
            frames["hover"] = Capture("state-settings-control-hover.png");

            PointerDown(probe.Button.gameObject);
            yield return null;
            frames["pressed"] = Capture("state-settings-control-pressed.png");

            PointerUp(probe.Button.gameObject);
            PointerExit(probe.Button.gameObject);
            yield return null;

            Focus(probe.Button.gameObject);
            yield return null;
            frames["focused"] = Capture("state-settings-control-focused.png");

            Focus(null);
            probe.SetAvailable(false, "OTURUM YOK");
            yield return null;
            frames["disabled"] = Capture("state-settings-control-disabled.png");

            AssertAllDistinct(frames, "settings control");
        }

        // ---- 4b. a real settings toggle -------------------------------------

        /// <summary>
        /// The accessibility toggle itself, driven the way a player drives it.
        /// <para>
        /// A toggle has a state a button does not: it is <i>on</i> or <i>off</i>,
        /// and that reading has to survive at a glance. Both are photographed
        /// alongside the pointer states, because a toggle whose two settings look
        /// the same is the defect a hover capture cannot find.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator SettingsToggle_ShowsBothItsSettingsAndEveryPointerState()
        {
            RequireGraphicsDevice();
            FreezeMotionSoAFrameIsReproducible();

            RectTransform host = Host("Toggle Host");
            var page = new NebulaSettingsPage();
            page.Build(host);
            yield return null;

            // A real row on the real settings page, found by the label it is built
            // with rather than by an index into the column.
            //
            // Camera Comfort rather than High Contrast, deliberately. High Contrast
            // now repaints the token palette — including the backdrop this capture
            // clears to — so photographing its own toggle would make every frame
            // after the click differ for a reason that has nothing to do with the
            // widget. Camera Comfort changes a bank ceiling and not one pixel of
            // this frame, so the only thing that moves between "off" and "on" is
            // the toggle saying so.
            Button toggle = FindButton(host, "Toggle", within: "Kamera Konforu");
            Assert.IsNotNull(toggle,
                "the settings page built no Camera Comfort toggle to photograph");

            bool savedComfort = AnimationAccessibility.CameraComfort;
            var frames = new Dictionary<string, byte[]>();
            try
            {
                AnimationAccessibility.CameraComfort = false;
                page.Refresh();
                yield return null;
                frames["off"] = Capture("state-toggle-off.png");

                toggle.onClick.Invoke();
                page.Refresh();
                yield return null;
                Assert.IsTrue(AnimationAccessibility.CameraComfort,
                    "the toggle's click did not reach the setting it names");
                frames["on"] = Capture("state-toggle-on.png");

                PointerEnter(toggle.gameObject);
                yield return null;
                frames["hover"] = Capture("state-toggle-hover.png");

                PointerExit(toggle.gameObject);
                Focus(toggle.gameObject);
                yield return null;
                frames["focused"] = Capture("state-toggle-focused.png");

                Focus(null);
                toggle.interactable = false;
                yield return null;
                frames["disabled"] = Capture("state-toggle-disabled.png");
            }
            finally
            {
                AnimationAccessibility.CameraComfort = savedComfort;
            }

            AssertAllDistinct(frames, "settings toggle");
        }

        // ---- 4c. a slider / stepper row -------------------------------------

        /// <summary>
        /// The UI-scale row: a continuous value with a control at each end. Its
        /// hit target is the one the design system's 32 px floor was written for
        /// and the one this page used to undercut, so the floor is re-asserted on
        /// the real control before it is photographed.
        /// </summary>
        [UnityTest]
        public IEnumerator ScaleRow_LooksDifferentInEveryState()
        {
            RequireGraphicsDevice();
            FreezeMotionSoAFrameIsReproducible();

            RectTransform host = Host("Scale Host");
            var page = new NebulaSettingsPage();
            page.Build(host);
            yield return null;

            Button up = FindButton(host, "Up", within: "Arayüz Ölçeği");
            Assert.IsNotNull(up, "the settings page built no UI-scale stepper to photograph");
            Assert.GreaterOrEqual(up.GetComponent<RectTransform>().sizeDelta.y,
                NebulaUiDesignTokens.MinimumHitTarget,
                "the scale row's stepper is below the minimum hit target");

            float savedScale = NebulaUiShell.UiScale;
            var frames = new Dictionary<string, byte[]>();
            try
            {
                frames["default"] = Capture("state-slider-default.png");

                PointerEnter(up.gameObject);
                yield return null;
                frames["hover"] = Capture("state-slider-hover.png");

                PointerDown(up.gameObject);
                yield return null;
                frames["pressed"] = Capture("state-slider-pressed.png");

                PointerUp(up.gameObject);
                PointerExit(up.gameObject);
                yield return null;

                Focus(up.gameObject);
                yield return null;
                frames["focused"] = Capture("state-slider-focused.png");

                Focus(null);
                up.interactable = false;
                yield return null;
                frames["disabled"] = Capture("state-slider-disabled.png");
            }
            finally
            {
                NebulaUiShell.UiScale = savedScale;
            }

            AssertAllDistinct(frames, "scale row");
        }

        // ---- 5. long Turkish text -------------------------------------------

        /// <summary>
        /// Turkish is longer than the English these rects were sized against, and
        /// the labels ellipsize rather than spill -- deliberately, because a spill
        /// is invisible in review and an ellipsis is not. That only helps if
        /// something looks. This looks.
        /// <para>
        /// Measured at the source rather than in pixels: a non-wrapping label whose
        /// preferred width exceeds its rect is going to lose characters, and which
        /// ones is not the player's problem to guess. "YÖRÜNGE SABİTLE" rendered as
        /// "YÖRÜNGE SA…" on the HUD's own action bar and nothing failed.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator LongTurkishText_FitsEveryControlItIsPutIn()
        {
            RequireGraphicsDevice();
            FreezeMotionSoAFrameIsReproducible();

            RectTransform host = Host("Long Text Host");
            var settings = new NebulaSettingsPage();
            settings.Build(host);

            // The longest strings the interface has, on the surface with the least
            // room for them. Named once and reused, because the test's real claim
            // is that it looked at *these*.
            string[] required =
            {
                "SONDAJ T2'YE YÜKSELT",
                "İSTASYON İNŞASI T2 GEREKLİ",
                "YÖRÜNGE SABİTLE",
                "GEZEGENİ TAHLİYE ET",
            };

            var bar = Nebula.UI.Hud.NebulaHudContextBar.Create(shell);
            bar.Show("VEGA-7 / II   ·   METAL   ·   RARE   ·   SAKİN",
                new Nebula.UI.Hud.HudAction(required[0], KeyCode.F,
                    "30 PARÇA + 40 METAL ALAŞIM", available: false,
                    blockedReason: required[1]),
                new List<Nebula.UI.Hud.HudAction>
                {
                    new Nebula.UI.Hud.HudAction(required[2], KeyCode.B),
                    new Nebula.UI.Hud.HudAction(required[3], KeyCode.V),
                });

            yield return null;
            Canvas.ForceUpdateCanvases();

            var overflowing = new List<string>();
            var inspected = new List<string>();
            foreach (TMPro.TextMeshProUGUI text in
                     shell.gameObject.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                if (text.enableWordWrapping) continue;          // wrapping is allowed to be tall
                if (!text.gameObject.activeInHierarchy) continue;

                inspected.Add(text.text);

                text.ForceMeshUpdate();
                float available = text.rectTransform.rect.width;
                if (available <= 1f) continue;                  // a stretched rect not yet laid out
                if (text.preferredWidth > available + 0.5f)
                    overflowing.Add($"\"{text.text}\" needs {text.preferredWidth:0} of " +
                                    $"{available:0} in {HierarchyPath(text.transform)}");
            }

            // An empty failure list is the dangerous outcome here, not a long one:
            // a sweep that reached none of the labels is exactly as green as a
            // sweep that found nothing wrong. So the sweep has to have reached
            // something, and specifically these.
            Assert.Greater(inspected.Count, 20,
                $"only {inspected.Count} labels were inspected — a settings page and a "
                + "context bar carry far more than that, so the sweep is not reaching the "
                + "interface and an empty overflow list would mean nothing.");

            var unreached = new List<string>();
            foreach (string phrase in required)
            {
                bool found = false;
                foreach (string seen in inspected)
                    if (seen != null && seen.Contains(phrase)) { found = true; break; }
                if (!found) unreached.Add(phrase);
            }
            Assert.IsEmpty(unreached,
                "the strings this test exists for were never looked at: "
                + string.Join(", ", unreached)
                + ".\nThe sweep walks shell.gameObject, so a widget parented anywhere "
                + "else is invisible to it and this test would pass vacuously.");

            // Both layers, composited. The settings page is on Menu and the context
            // bar is on HUD Static, and this used to photograph Menu alone -- so the
            // evidence for the Turkish worst case contained none of the strings that
            // make it the worst case, and a reviewer reading the contact sheet would
            // have concluded it was covered.
            Capture("state-longtext-turkish.png", UiLayer.HudStatic, UiLayer.Menu);

            Assert.IsEmpty(overflowing,
                "Turkish text does not fit its control and is being ellipsized:\n  "
                + string.Join("\n  ", overflowing));
        }

        private static string HierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        // ---- 6. accessibility -----------------------------------------------

        /// <summary>
        /// The six switches, each photographed on a surface built while that
        /// switch was already on.
        /// <para>
        /// The previous version of this test set a mode and then took a picture of
        /// a menu that had been built before it. Colour is resolved once, when a
        /// widget is created, so High Contrast reached exactly the widgets that
        /// happened to be rebuilt afterwards — which was none of them, and the
        /// resulting frame was byte-identical to the four motion modes beside it.
        /// The whole menu and the status legend are rebuilt per mode now, which is
        /// what a player gets: they flip the switch and the screen they are
        /// looking at is redrawn.
        /// </para>
        /// <para>
        /// The baseline has every aid off, <i>including the visual cue</i>, which
        /// ships on. A baseline that already has one of the six modes running is
        /// not a baseline, and it is the reason a cue capture could never have
        /// differed from it.
        /// </para>
        /// <para>
        /// What each mode is then held to lives in the EditMode fixture
        /// <c>UiFinalResolutionAccessibilityTests</c>, which measures the motion
        /// modes over time and these two against pixels. This test's job is to
        /// produce evidence honestly, not to grade it.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator EveryAccessibilityMode_IsCapturedOnASurfaceBuiltUnderIt()
        {
            RequireGraphicsDevice();

            // The first surface a cold shell builds does not photograph like the
            // ones after it: the input module makes its first pass over a scene
            // that has just appeared and leaves a highlight on a rail row, which
            // showed up as a 4,644-pixel difference between the baseline and every
            // mode after it. That difference is real and has nothing to do with any
            // accessibility setting, and a comparison that counts it is measuring
            // frame order. So the baseline is built, photographed and thrown away
            // once before the run that counts.
            yield return CaptureMode("state-a11y-baseline.png", () => { });
            yield return CaptureMode("state-a11y-baseline.png", () => { });

            yield return CaptureMode("state-a11y-motion-off.png",
                () => AnimationAccessibility.MotionOff = true);
            Assert.IsTrue(NebulaUiDesignTokens.SnapTransitions,
                "Motion Off must make every transition snap.");

            yield return CaptureMode("state-a11y-reduced-motion.png",
                () => AnimationAccessibility.ReducedMotion = true);

            yield return CaptureMode("state-a11y-flash-reduction.png",
                () => LightingAccessibility.FlashReduction = true);

            yield return CaptureMode("state-a11y-photosensitivity-safe.png",
                () => LightingAccessibility.PhotosensitivitySafeMode = true);

            yield return CaptureMode("state-a11y-high-contrast.png",
                () => LightingAccessibility.HighContrast = true);

            yield return CaptureMode("state-a11y-visual-cue.png",
                () => Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = true);

            ResetAccessibility();

            foreach (string file in AccessibilityCaptures)
                Assert.IsTrue(File.Exists(Path.Combine(OutputDir, file)), file + " was not written");

            // The two that promise a still-frame difference are checked here as
            // well as in the audit, so a run that produced the files never reports
            // success while having produced two copies of the same picture.
            string baseline = Fingerprint(File.ReadAllBytes(
                Path.Combine(OutputDir, "state-a11y-baseline.png")));
            foreach (string mode in new[] { "state-a11y-high-contrast.png", "state-a11y-visual-cue.png" })
                Assert.AreNotEqual(baseline,
                    Fingerprint(File.ReadAllBytes(Path.Combine(OutputDir, mode))),
                    mode + " is byte-identical to the baseline — the setting reached no pixel.");
        }

        /// <summary>
        /// Builds the whole surface with one mode already applied, photographs it,
        /// and tears it down again. Every mode gets the same surface, so a
        /// difference between two captures is the setting and nothing else.
        /// </summary>
        private IEnumerator CaptureMode(string fileName, System.Action apply)
        {
            ResetAccessibility();
            apply();

            NebulaParentMenuScreen menu = BuildMenu();
            menu.Open(NebulaUiAssetCatalog.Page.Combat);
            menu.ShowPage(NebulaUiAssetCatalog.Page.Combat);

            // The status legend, built from real readings by the real consumer.
            // It is present in every mode, so the visual-cue capture differs from
            // the baseline by the marks appearing -- not by a panel appearing.
            RectTransform host = Host("Cue Host");
            UiStatusCueStrip.Handle legend = UiStatusCueStrip.Build(host);
            legend.Root.anchoredPosition = new Vector2(330f, -40f);

            // Nothing is hovered and nothing is focused in any of the seven
            // frames, so a stale highlight cannot be read as a setting.
            if (eventSystem != null) eventSystem.SetSelectedGameObject(null);
            yield return null;
            yield return null;
            Capture(fileName);

            Object.DestroyImmediate(host.gameObject);
            menu.Close();
            Object.DestroyImmediate(menu.gameObject);
        }

        /// <summary>
        /// The visual-cue switch reaches the interface, and this is where that is
        /// stated in the assembly that draws it.
        /// <para>
        /// It replaces a test that recorded the opposite — that
        /// <c>AudioVisualCueForWarnings</c> was written by the settings page and
        /// read nowhere — and it holds the shape of the wiring rather than only
        /// its existence: cues are a redundant channel, so switching them must add
        /// and remove marks while leaving every colour exactly where it was. A
        /// "cue" that changed the palette would be a second, silent colour setting.
        /// </para>
        /// </summary>
        [Test]
        public void VisualCueForWarnings_AddsMarksAndMovesNoColour()
        {
            ResetAccessibility();
            Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = false;

            var off = new Dictionary<UiStatusLevel, Color>();
            foreach (UiStatusLevel level in (UiStatusLevel[])System.Enum.GetValues(typeof(UiStatusLevel)))
            {
                off[level] = NebulaUiDesignTokens.Status(level);
                Assert.IsFalse(UiStatusCue.For(level).HasMark,
                    level + " drew a mark with the cue switched off");
            }
            bool repeatOff = NebulaUiDesignTokens.AllowsRepeatingEmphasis;

            Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = true;

            foreach (UiStatusLevel level in (UiStatusLevel[])System.Enum.GetValues(typeof(UiStatusLevel)))
            {
                Assert.IsTrue(UiStatusCue.For(level).HasMark,
                    level + " has no mark with the cue switched on");
                Assert.AreEqual(off[level], NebulaUiDesignTokens.Status(level),
                    "the visual cue moved " + level + "'s colour; it is meant to add a channel, "
                    + "not to change the one that was already there");
            }
            Assert.AreEqual(repeatOff, NebulaUiDesignTokens.AllowsRepeatingEmphasis);
        }

        /// <summary>
        /// The legend the capture photographs, checked without a screen: five real
        /// readings, five marks, and nothing drawn once the switch is off.
        /// </summary>
        [UnityTest]
        public IEnumerator StatusCueLegend_DrawsAMarkForEveryStateItClaims()
        {
            ResetAccessibility();
            Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = true;

            RectTransform host = Host("Legend Host");
            UiStatusCueStrip.Handle on = UiStatusCueStrip.Build(host, "Legend On");
            yield return null;

            Assert.AreEqual(5, on.Marks.Count, "the legend lost one of its five states");
            Assert.AreEqual(5, on.VisibleMarks, "a legend row has a colour and no mark");
            foreach (UiStatusCueView.Handle mark in on.Marks)
                Assert.Greater(mark.StrokeCount, 0,
                    mark.Root.name + " built a mark object that draws no geometry");

            Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = false;
            UiStatusCueStrip.Handle offLegend = UiStatusCueStrip.Build(host, "Legend Off");
            yield return null;
            Assert.AreEqual(0, offLegend.VisibleMarks,
                "the legend still drew marks with the cue switched off");

            Object.DestroyImmediate(host.gameObject);
        }

        /// <summary>
        /// The baseline is "no accessibility aid is running", so the visual cue —
        /// which ships on — is off in it. Stated here because it is the difference
        /// between a comparison that can find something and one that cannot.
        /// </summary>
        private static void ResetAccessibility()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
            Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = false;
        }

        // ---- the real uGUI path ---------------------------------------------

        private PointerEventData Pointer() => new PointerEventData(eventSystem)
        {
            button = PointerEventData.InputButton.Left,
            position = new Vector2(Width * 0.5f, Height * 0.5f),
        };

        private void PointerEnter(GameObject target) =>
            ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerEnterHandler);

        private void PointerExit(GameObject target) =>
            ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerExitHandler);

        private void PointerDown(GameObject target) =>
            ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerDownHandler);

        private void PointerUp(GameObject target) =>
            ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerUpHandler);

        /// <summary>Keyboard focus, through the EventSystem a real player's Tab goes through.</summary>
        private void Focus(GameObject target) => eventSystem.SetSelectedGameObject(target);

        // ---- scene helpers ---------------------------------------------------

        private RectTransform Host(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(shell.Root(UiLayer.Menu), false);
            NebulaUiFactory.Fill(rect);
            return rect;
        }

        private NebulaParentMenuScreen BuildMenu()
        {
            NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
            menu.AddPage(new NebulaOverviewPage(null));
            menu.AddPage(new NebulaMapPage(null));
            menu.AddPage(new NebulaProgressionPage(null));
            menu.AddPage(new NebulaFleetPage(null));
            menu.AddPage(new NebulaPlanetsPage(null));
            menu.AddPage(new NebulaResourcesPage(null));
            menu.AddPage(new NebulaCombatPage(null));
            menu.AddPage(new NebulaDiscoveryPage(null));
            menu.AddPage(new NebulaSettingsPage());
            menu.AddPage(new NebulaSessionPage(null));
            return menu;
        }

        /// <summary>The rail entry the screen builds for a page, found by its own name.</summary>
        private GameObject FindRailEntry(NebulaUiAssetCatalog.Page page)
        {
            foreach (Transform child in shell.Root(UiLayer.Menu).GetComponentsInChildren<Transform>(true))
                if (child.name == page + " Rail" && child.GetComponent<Button>() != null)
                    return child.gameObject;
            return null;
        }

        private Transform Find(string name)
        {
            foreach (Transform child in shell.Root(UiLayer.Menu).GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        /// <summary>
        /// A real control, found by the name the screen built it with — optionally
        /// only inside a named ancestor, because every settings row calls its
        /// control "Toggle" and only one of them is the row under test.
        /// </summary>
        private static Button FindButton(Transform root, string name, string within = null)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.name != name) continue;
                if (within == null) return button;

                for (Transform t = button.transform; t != null; t = t.parent)
                    if (t.name == within) return button;
            }
            return null;
        }

        private static GameObject FirstRowWithAButton(Transform root)
        {
            if (root == null) return null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                var button = child.GetComponent<Button>();
                if (button != null && button.transition == Selectable.Transition.SpriteSwap)
                    return child.gameObject;
            }
            return null;
        }

        // ---- capture ---------------------------------------------------------

        // ---- state art -------------------------------------------------------

        /// <summary>
        /// A state has to change the control's face, not only the tone of the
        /// hairline around it.
        /// <para>
        /// <see cref="AssertAllDistinct"/> hashes whole frames, so one altered pixel
        /// satisfies it — and that is very nearly what the secondary button family
        /// was shipping. Its rest fill was fully transparent and the generator
        /// produced each reactive state by scaling that fill's alpha, which is a
        /// no-op on zero: hover and pressed differed from rest only in the alpha of
        /// the one-pixel border, over 9% of the sprite. Nine-slice stretching then
        /// spends that 9% on the perimeter of a 280-pixel-wide button, and the
        /// player sees a control that does not answer the pointer at all.
        /// </para>
        /// <para>
        /// So the claim is about area, which is what makes it a claim about a second
        /// channel: a state that reads only as "the same edge, slightly brighter" has
        /// nothing but colour to say, and colour alone is what this fails on.
        /// </para>
        /// </summary>
        private static void AssertEachStateChangesTheFaceAndNotJustItsEdge(Button control)
        {
            var face = control.targetGraphic as Image;
            Assert.IsNotNull(face, "the control has no target graphic to change state with");

            Sprite rest = face.sprite;
            Assert.IsNotNull(rest, "the control has no rest art to compare its states against");
            SpriteState states = control.spriteState;

            AssertFaceChanged(rest, states.highlightedSprite, "rest", "hover");
            AssertFaceChanged(rest, states.pressedSprite, "rest", "pressed");
            AssertFaceChanged(states.highlightedSprite, states.pressedSprite, "hover", "pressed");

            // Focus is the one state whose whole channel IS the edge — a ring, drawn
            // where nothing was. It is asked for the opposite thing: not area, but an
            // edge nobody could mistake for a tint.
            Assert.That(MaxAlphaDelta(rest, states.selectedSprite), Is.GreaterThanOrEqualTo(128),
                "the focus art differs from rest only by a tint, not by a ring: "
                + "keyboard and pad focus is a guess");

            AssertDisabledIsNotOnlyATone(rest, states.disabledSprite);
        }

        /// <summary>
        /// Disabled has to be a different shape, not the same shape dimmed.
        /// <para>
        /// This one cannot be satisfied by any amount of alpha. It counts pixels the
        /// rest art paints and disabled does not, and pixels disabled paints and rest
        /// does not — which is zero and zero for every "same frame, lower opacity"
        /// treatment, however far the opacity is taken. It is non-zero only when the
        /// frame itself has moved, and a moved frame is a channel a player who cannot
        /// separate those two tones still reads.
        /// </para>
        /// </summary>
        private static void AssertDisabledIsNotOnlyATone(Sprite rest, Sprite disabled)
        {
            Assert.IsNotNull(disabled, "the control carries no disabled art at all");
            Color[] pa = Face(rest);
            Color[] pb = Face(disabled);
            Assert.AreEqual(pa.Length, pb.Length, "rest and disabled are different sizes");

            int vacated = 0, claimed = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                if (pa[i].a > 0f && pb[i].a <= 0f) vacated++;
                if (pa[i].a <= 0f && pb[i].a > 0f) claimed++;
            }

            float area = pa.Length;
            Assert.That(Mathf.Min(vacated, claimed) / area, Is.GreaterThan(0.01f),
                $"disabled paints the same pixels as rest ({vacated / area:P1} vacated, "
                + $"{claimed / area:P1} newly drawn) — it is the rest frame with the tone "
                + "turned down, so colour is the only thing separating available from "
                + "unavailable on this control.");
        }

        /// <summary>How much of the sprite a state actually repaints.</summary>
        private const float MinimumStateArea = 0.25f;

        private static void AssertFaceChanged(Sprite from, Sprite to, string fromName, string toName)
        {
            Assert.IsNotNull(to, $"the control carries no {toName} art at all");
            float changed = ChangedFraction(from, to);
            Assert.That(changed, Is.GreaterThanOrEqualTo(MinimumStateArea),
                $"{toName} repaints only {changed:P1} of the control's face against {fromName} "
                + $"(needs {MinimumStateArea:P0}). A state carried by the border alone is colour "
                + "and nothing else, and nine-slice stretching spends it on a hairline the player "
                + "cannot see.");
        }

        /// <summary>
        /// The share of the sprite's own pixels that differ. Read through
        /// <see cref="Sprite.rect"/> rather than off the whole texture, so this still
        /// measures the sprite and not its neighbours once these are atlased.
        /// </summary>
        private static float ChangedFraction(Sprite a, Sprite b)
        {
            Color[] pa = Face(a);
            Color[] pb = Face(b);
            Assert.AreEqual(pa.Length, pb.Length,
                $"{a.name} and {b.name} are different sizes, so no pixel of one answers for the other");

            const float epsilon = 2f / 255f;
            int changed = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                if (Mathf.Abs(pa[i].a - pb[i].a) >= epsilon) { changed++; continue; }
                // Colour only counts where something is actually drawn: the RGB under a
                // fully transparent pixel is arbitrary and would count as a change that
                // nobody can see.
                if (pa[i].a <= 0f && pb[i].a <= 0f) continue;
                if (Mathf.Abs(pa[i].r - pb[i].r) >= epsilon
                    || Mathf.Abs(pa[i].g - pb[i].g) >= epsilon
                    || Mathf.Abs(pa[i].b - pb[i].b) >= epsilon) changed++;
            }
            return changed / (float)pa.Length;
        }

        private static int MaxAlphaDelta(Sprite a, Sprite b)
        {
            Assert.IsNotNull(b, "the control carries no art for that state");
            Color[] pa = Face(a);
            Color[] pb = Face(b);
            float worst = 0f;
            for (int i = 0; i < pa.Length && i < pb.Length; i++)
                worst = Mathf.Max(worst, Mathf.Abs(pa[i].a - pb[i].a));
            return Mathf.RoundToInt(worst * 255f);
        }

        private static Color[] Face(Sprite sprite)
        {
            Assert.IsNotNull(sprite, "a state with no sprite cannot look like anything");
            Rect rect = sprite.rect;
            return sprite.texture.GetPixels(
                Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y),
                Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height));
        }

        private static void AssertAllDistinct(Dictionary<string, byte[]> frames, string what)
        {
            var seen = new Dictionary<string, string>();
            foreach (KeyValuePair<string, byte[]> frame in frames)
            {
                string hash = Fingerprint(frame.Value);
                Assert.IsFalse(seen.ContainsKey(hash),
                    $"the {what} renders identically in the {frame.Key} and " +
                    $"{(seen.TryGetValue(hash, out string other) ? other : "another")} states — " +
                    "the widget is not wired to that state, which is the finding, not a pass");
                seen[hash] = frame.Key;
            }
        }

        private static string Fingerprint(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(sha.ComputeHash(bytes));
        }

        /// <summary>
        /// Renders one or more shell layers to a PNG and returns its raw pixels,
        /// so the caller can compare states without re-reading the file.
        /// <para>
        /// Several layers at once because a widget belongs to exactly one of them
        /// and a screenshot of a different one is not evidence about it: the
        /// Turkish overflow case builds a settings page on Menu and a context bar
        /// on HUD Static, and photographing Menu alone produced a capture that
        /// contained none of the strings the case is about. Layers composite in
        /// their own sorting order, so the picture is the one the player would
        /// see.
        /// </para>
        /// <para>
        /// Every canvas is restored afterwards: one left in ScreenSpaceCamera mode
        /// silently breaks every later test in the run.
        /// </para>
        /// </summary>
        private byte[] Capture(string fileName) => Capture(fileName, UiLayer.Menu);

        private byte[] Capture(string fileName, params UiLayer[] layers)
        {
            var canvasesUsed = new List<Canvas>();
            var savedModes = new List<RenderMode>();
            var savedCameras = new List<Camera>();
            var savedPlanes = new List<float>();
            foreach (UiLayer layer in layers)
            {
                Canvas c = shell.CanvasFor(layer);
                Assert.IsNotNull(c, "the shell has no canvas for " + layer);
                canvasesUsed.Add(c);
                savedModes.Add(c.renderMode);
                savedCameras.Add(c.worldCamera);
                savedPlanes.Add(c.planeDistance);
            }
            RenderTexture previousActive = RenderTexture.active;

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("StateCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            byte[] raw;

            try
            {
                // A device can exist and still refuse the allocation. Asking first
                // turns that into a named skip instead of a ReadPixels against a
                // texture that was never there.
                if (!target.Create())
                    Assert.Ignore(
                        "SKIPPED — the graphics device would not allocate a "
                        + Width + "x" + Height + " render texture, so " + fileName
                        + " cannot be captured. Re-run in the shots/liveshots configuration.");

                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = NebulaUiDesignTokens.SpaceVoid;
                camera.orthographic = true;
                camera.targetTexture = target;
                camera.transform.position = new Vector3(0f, 0f, -100f);

                foreach (Canvas c in canvasesUsed)
                {
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = camera;
                    c.planeDistance = 100f;
                }

                Canvas.ForceUpdateCanvases();
                camera.Render();

                RenderTexture.active = target;
                var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();

                AssertNotFlat(texture, fileName);
                raw = texture.GetRawTextureData();
                File.WriteAllBytes(Path.Combine(OutputDir, fileName), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            finally
            {
                RenderTexture.active = previousActive;
                for (int i = 0; i < canvasesUsed.Count; i++)
                {
                    canvasesUsed[i].renderMode = savedModes[i];
                    canvasesUsed[i].worldCamera = savedCameras[i];
                    canvasesUsed[i].planeDistance = savedPlanes[i];
                }
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
            }

            return raw;
        }

        private static void AssertNotFlat(Texture2D texture, string fileName)
        {
            Color32[] pixels = texture.GetPixels32();
            Color32 first = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (p.r != first.r || p.g != first.g || p.b != first.b) return;
            }
            Assert.Fail(fileName + " is a single flat colour — nothing rendered.");
        }
    }
}
