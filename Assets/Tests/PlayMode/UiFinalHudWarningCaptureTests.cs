using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Combat;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The two urgent HUD warnings, photographed on a running campaign.
    /// <para>
    /// <c>UiFinalHudWarningContainmentTests</c> already measures a live raid
    /// banner and a live over-capacity warning and finds them contained and clear
    /// of each other. What it cannot do is show anybody the screen: it reads
    /// rects. The finalization ledger recorded that honestly — "not captured as
    /// an image: a live raid warning and a full-cargo warning on the HUD" — and
    /// this fixture is what closes it, so the two states a player most needs to
    /// be able to read are in the review sheets like every other surface.
    /// </para>
    /// <para>
    /// <b>The states are not staged.</b> Every one of them comes from
    /// <see cref="UiFinalHudWarningState"/>, which is the same file the
    /// containment fixture drives its measurements from: a real raid through
    /// <c>ThreatPressureState.ScheduleRaid</c>, a real hold shrunk under its own
    /// contents through <c>LogisticsCargo.SetCapacity</c>, and a real context bar
    /// reached by flying the ship to a real planet. Nothing here writes label
    /// text, forces a private field, or switches a panel on to be photographed.
    /// A capture that did any of those would be a photograph of the capture code.
    /// </para>
    /// <para>
    /// <b>What the picture is checked against.</b> The geometry is measured on
    /// the very frame that is written — after the canvases are pointed at the
    /// capture camera and laid out at the capture resolution, before
    /// <c>ReadPixels</c>. A capture fixture that renders one layout and asserts
    /// against another is not evidence of anything, and the two are one call
    /// apart here on purpose.
    /// </para>
    /// <para>
    /// <b>Bounds are what is drawn, not what is reserved.</b> The context bar
    /// owns a 1100-unit-wide container and draws a centred row perhaps 870 wide
    /// inside it; its context line owns the full width and draws a short centred
    /// sentence. Comparing container rects would report an overlap with the vital
    /// cluster that no player can see, which is a false alarm and worse than no
    /// check. So text contributes its rendered glyph bounds and images their
    /// rects, and "these two do not overlap" means it about the picture.
    /// </para>
    /// <para>
    /// Needs a graphics device. Run it alone —
    /// <c>./UIDesign/runscoped.sh playmode &lt;tag&gt;
    /// "Nebula.Tests.PlayMode.UiFinalHudWarningCaptureTests" --graphics</c> —
    /// because capture fixtures stacked into one Unity process drove
    /// <c>UnloadUnusedAssets</c> from 10ms to 64s and produced nothing for
    /// sixteen minutes.
    /// </para>
    /// </summary>
    // Graphics: every test here renders a live camera into a RenderTexture and reads the pixels back. Under -nographics the
    // render is a segfault risk in RenderOffscreenCameras, not merely a blank frame, so this
    // fixture must never be selected into a headless worker. It carries no ReleaseRegression
    // category on purpose -- the headless gate selects by that name and must not reach here.
    [Category("Graphics")]
    public class UiFinalHudWarningCaptureTests
    {
        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime");

        /// <summary>Every file this fixture writes. The contact sheets name the same list.</summary>
        public static readonly string[] WarningCaptures =
        {
            "hud-warning-raid-cargo-1920x1080.png",
            "hud-warning-raid-cargo-1280x720.png",
            "hud-warning-a11y-photosensitivity-safe.png",
            "hud-warning-a11y-flash-reduction.png",
            "hud-warning-a11y-high-contrast.png",
            "hud-warning-a11y-visual-cue.png",
        };

        /// <summary>The canvas the layout is authored against. Both captures land here.</summary>
        private const float ReferenceWidth = 1920f;

        /// <summary>Sub-pixel rounding and deliberate hairline bleed are not defects.</summary>
        private const float Tolerance = 2f;

        /// <summary>WCAG AA for body text, which is what both warnings are.</summary>
        private const float MinimumContrast = 4.5f;

        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slotBackup;

        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slotBackup = UiFinalPlayMode.BackupSlots();
            UiFinalPlayMode.DeleteEverySlot();

            // The capture is of the interface at its own size. UI scale and HUD
            // scale are separate axes with their own captures, and leaving a
            // neighbour's setting in place would silently photograph that instead.
            // Both are in Globals and go back on the way out.
            NebulaUiShell.UiScale = 1f;
            CombatAccessibilitySettings.HudScale = 1f;
            Directory.CreateDirectory(OutputDir);
        }

        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            foreach (NebulaUiShell shell in Object.FindObjectsOfType<NebulaUiShell>(true))
                if (shell != null) shell.Shutdown();

            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();

            UiFinalPlayMode.ClearSessionStatics();
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slotBackup);
        }

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); re-run this fixture with --graphics");
        }

        // =====================================================================
        // 1. Both warnings, both reference resolutions
        // =====================================================================

        /// <summary>
        /// One booted campaign, both warnings up, photographed at 1280x720 and at
        /// 1920x1080.
        /// <para>
        /// The two are not the same picture at two sizes. Pointing a canvas at a
        /// camera with a 1280x720 target re-runs its <c>CanvasScaler</c>, so the
        /// second capture is a genuine re-layout at the smaller viewport — which
        /// is asserted rather than assumed, by checking the scale factor the
        /// scaler actually computed before the frame is rendered. A capture
        /// harness that quietly rendered both at the screen's real size would
        /// otherwise write two files named for resolutions neither of them is.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator BothWarningsTogether_ArePhotographedAtBothReferenceResolutions()
        {
            RequireGraphicsDevice();

            yield return UiFinalPlayMode.LoadSceneCold();

            // Every aid off before the HUD is composed, including the warning
            // visual cue, which ships ON. Without this line the pair below is the
            // cue capture under a different name: the first run of this fixture
            // wrote a "raid + cargo" file byte-identical to its own
            // "visual cue" file, and two contact-sheet cells claiming to show
            // different things showed one thing twice.
            ResetAccessibility();

            yield return UiFinalPlayMode.BeginSession();
            yield return BringTheWholeHudUp();

            Assert.AreEqual(0, UiFinalHudWarningState.Alerts().RaidCueStrokes,
                "the raid banner drew a cue with cues switched off, so this is not a baseline");
            Assert.AreEqual(0, UiFinalHudWarningState.Alerts().CargoCueStrokes,
                "the cargo warning drew a cue with cues switched off, so this is not a baseline");

            CaptureAndMeasure(1920, 1080, "hud-warning-raid-cargo-1920x1080.png");
            CaptureAndMeasure(1280, 720, "hud-warning-raid-cargo-1280x720.png");

            // Two files named for two resolutions must not be one picture twice.
            Assert.AreNotEqual(
                Fingerprint("hud-warning-raid-cargo-1920x1080.png"),
                Fingerprint("hud-warning-raid-cargo-1280x720.png"),
                "the 1280x720 capture is byte-identical to the 1920x1080 one, so the "
                + "resolution never reached the render");
        }

        // =====================================================================
        // 2. The accessibility modes
        // =====================================================================

        /// <summary>
        /// Photosensitivity Safe Mode and Flash Reduction, each with both warnings
        /// live under it.
        /// <para>
        /// The mode is set <b>before</b> the campaign composes its HUD, because
        /// that is the situation a player who has it switched on is in — the
        /// widgets are built from the palette the mode selects, and the cargo
        /// warning in particular takes its colour once, at build. A capture that
        /// flipped the switch afterwards would photograph a stale palette and
        /// report it as the mode.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator BothWarnings_StayReadableUnderPhotosensitivityAndFlashReduction()
        {
            RequireGraphicsDevice();

            yield return CaptureUnderMode("hud-warning-a11y-photosensitivity-safe.png",
                "Photosensitivity Safe Mode",
                () => LightingAccessibility.PhotosensitivitySafeMode = true);

            yield return CaptureUnderMode("hud-warning-a11y-flash-reduction.png",
                "Flash Reduction",
                () => LightingAccessibility.FlashReduction = true);
        }

        /// <inheritdoc cref="BothWarnings_StayReadableUnderPhotosensitivityAndFlashReduction"/>
        [UnityTest]
        public IEnumerator BothWarnings_StayReadableUnderHighContrastAndTheVisualCue()
        {
            RequireGraphicsDevice();

            yield return CaptureUnderMode("hud-warning-a11y-high-contrast.png",
                "High Contrast",
                () => LightingAccessibility.HighContrast = true);

            yield return CaptureUnderMode("hud-warning-a11y-visual-cue.png",
                "the warning Visual Cue",
                () => CombatAccessibilitySettings.AudioVisualCueForWarnings = true);
        }

        /// <summary>
        /// A cold scene, one accessibility mode switched on before the campaign
        /// starts, both warnings driven up, one frame photographed and measured.
        /// </summary>
        private IEnumerator CaptureUnderMode(string fileName, string modeName, System.Action apply)
        {
            yield return UiFinalPlayMode.LoadSceneCold();

            ResetAccessibility();
            apply();

            yield return UiFinalPlayMode.BeginSession();
            yield return BringTheWholeHudUp();

            // The cue is the one mode with a claim about the widgets themselves,
            // and it is asserted in both directions elsewhere. Here it only has
            // to be true that the mode reached the thing being photographed,
            // otherwise the file is mislabelled.
            if (CombatAccessibilitySettings.AudioVisualCueForWarnings)
            {
                Assert.Greater(UiFinalHudWarningState.Alerts().RaidCueStrokes, 0,
                    modeName + ": the raid banner drew no cue, so this capture is not of that mode");
                Assert.Greater(UiFinalHudWarningState.Alerts().CargoCueStrokes, 0,
                    modeName + ": the cargo warning drew no cue, so this capture is not of that mode");
            }

            CaptureAndMeasure(1920, 1080, fileName, modeName);

            ResetAccessibility();
            yield return UiFinalPlayMode.EndSessionAndSettle();
        }

        private static void ResetAccessibility()
        {
            LightingAccessibility.PhotosensitivitySafeMode = false;
            LightingAccessibility.FlashReduction = false;
            LightingAccessibility.HighContrast = false;
            CombatAccessibilitySettings.AudioVisualCueForWarnings = false;
        }

        // =====================================================================
        // Getting the HUD into the state that is worth photographing
        // =====================================================================

        /// <summary>
        /// Both warnings and the context bar, all live at once, through
        /// <see cref="UiFinalHudWarningState"/> and nothing else.
        /// <para>
        /// The ship is flown to its planet first. A raid and a full hold are
        /// carried state and survive the trip; a context bar reached afterwards
        /// would be one more thing changing between the state and the shutter.
        /// </para>
        /// </summary>
        private static IEnumerator BringTheWholeHudUp()
        {
            yield return UiFinalHudWarningState.BringUpTheContextBar();
            yield return UiFinalHudWarningState.RaiseARaidWarning();
            yield return UiFinalHudWarningState.OverfillTheHold();

            Assert.IsTrue(UiFinalHudWarningState.Alerts().RaidVisible,
                "the raid banner went away before the shutter");
            Assert.IsTrue(UiFinalHudWarningState.CargoWarningVisible(),
                "the cargo warning went away before the shutter");

            // The vital cluster shows itself off its own combat readings; nothing
            // here switches it on, so it is waited for rather than assumed.
            yield return UiFinalPlayMode.Until(
                () => Panel("Vitals") != null && Panel("Vitals").gameObject.activeInHierarchy,
                "the ship's vital cluster (KALKAN / ZIRH / GÖVDE) to come up on the HUD");
        }

        /// <summary>The named HUD panel, from whichever of the two HUD layers owns it.</summary>
        private static RectTransform Panel(string name) =>
            UiFinalHudWarningState.Find(UiFinalHudWarningState.HudRoot(), name)
            ?? UiFinalHudWarningState.Find(UiFinalHudWarningState.HudStaticRoot(), name);

        // =====================================================================
        // The capture, and the measurements taken on the captured frame
        // =====================================================================

        private void CaptureAndMeasure(int width, int height, string fileName, string modeName = null)
        {
            NebulaUiShell shell = UiFinalPlayMode.Shell();
            Assert.IsNotNull(shell, "there is no shell to photograph");

            Canvas[] canvases = Object.FindObjectsOfType<Canvas>(true);
            var savedMode = new RenderMode[canvases.Length];
            var savedCamera = new Camera[canvases.Length];
            var savedDistance = new float[canvases.Length];
            RenderTexture previousActive = RenderTexture.active;

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("HudWarningCaptureCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Camera camera = cameraObject.AddComponent<Camera>();

            try
            {
                // A device can exist and still refuse the allocation. Asking first
                // turns that into a named skip rather than a ReadPixels against a
                // texture that was never there.
                if (!target.Create())
                    Assert.Ignore($"SKIPPED — the graphics device would not allocate a "
                                  + $"{width}x{height} render texture, so {fileName} cannot be captured");

                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = NebulaUiDesignTokens.SpaceVoid;
                camera.orthographic = true;
                camera.targetTexture = target;
                camera.transform.position = new Vector3(0f, 0f, -100f);

                for (int i = 0; i < canvases.Length; i++)
                {
                    savedMode[i] = canvases[i].renderMode;
                    savedCamera[i] = canvases[i].worldCamera;
                    savedDistance[i] = canvases[i].planeDistance;
                    if (!canvases[i].isActiveAndEnabled) continue;
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = camera;
                    canvases[i].planeDistance = 100f;
                }

                // Repointing a canvas at a camera re-runs its CanvasScaler, and the
                // scaler owns scaleFactor — so the shell's own scale is re-applied
                // after the repoint rather than before it.
                shell.ApplyScale();
                Canvas.ForceUpdateCanvases();

                AssertTheCanvasReallyTookThisResolution(shell, width, height, fileName);
                AssertTheWarningsAreContainedAndClear(shell, camera, fileName);

                camera.Render();

                RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                try
                {
                    // Read once. Two labels and a colour census over a 1920x1080
                    // frame is three 8MB copies if each helper fetches its own.
                    Color32[] pixels = texture.GetPixels32();

                    AssertTheFrameHasAPictureOnIt(pixels, width, height, fileName);
                    AssertBothWarningsAreLegible(pixels, width, height, camera, fileName, modeName);
                    File.WriteAllBytes(Path.Combine(OutputDir, fileName), texture.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(texture);
                }
            }
            finally
            {
                RenderTexture.active = previousActive;
                for (int i = 0; i < canvases.Length; i++)
                {
                    if (canvases[i] == null) continue;
                    canvases[i].renderMode = savedMode[i];
                    canvases[i].worldCamera = savedCamera[i];
                    canvases[i].planeDistance = savedDistance[i];
                }
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
                Canvas.ForceUpdateCanvases();
            }
        }

        /// <summary>
        /// The frame about to be rendered really is laid out at the resolution the
        /// file is named for.
        /// <para>
        /// Every shell canvas scales with <c>MatchWidthOrHeight</c> at 0.5 against
        /// a 1920x1080 reference, so on any 16:9 viewport the canvas stays 1920x1080
        /// canvas units and only the scale factor moves. Both are checked: the
        /// canvas units because the layout below is measured in them, and the
        /// scale factor because it is the only thing that differs between the two
        /// captures and therefore the only proof the second one re-laid out at all.
        /// </para>
        /// </summary>
        private static void AssertTheCanvasReallyTookThisResolution(
            NebulaUiShell shell, int width, int height, string fileName)
        {
            foreach (UiLayer layer in new[] { UiLayer.HudDynamic, UiLayer.HudStatic })
            {
                Canvas canvas = shell.CanvasFor(layer);
                Assert.IsNotNull(canvas, "the shell has no canvas for " + layer);

                float expected = width / ReferenceWidth * NebulaUiShell.ScaleFor(layer);
                Assert.AreEqual(expected, canvas.scaleFactor, 0.01f,
                    $"{fileName}: the {layer} canvas was about to render at scale "
                    + $"{canvas.scaleFactor:0.000} rather than the {expected:0.000} a "
                    + $"{width}x{height} viewport asks for, so this file would be named for a "
                    + "resolution it was not rendered at");

                var rect = (RectTransform)canvas.transform;
                Assert.AreEqual(1920f, rect.rect.width, Tolerance,
                    $"{fileName}: the {layer} canvas is {rect.rect.width:0} units wide, not the "
                    + "1920 the layout is authored against");
                Assert.AreEqual(1080f, rect.rect.height, Tolerance,
                    $"{fileName}: the {layer} canvas is {rect.rect.height:0} units tall, not 1080");
            }
        }

        /// <summary>
        /// Both warnings inside the safe area at once, and the five things the HUD
        /// says clear of one another.
        /// </summary>
        private static void AssertTheWarningsAreContainedAndClear(NebulaUiShell shell, Camera camera,
            string fileName)
        {
            var safeArea = (RectTransform)shell.CanvasFor(UiLayer.HudDynamic).transform;

            RectTransform raid = Panel("Raid");
            RectTransform cargo = Panel("Cargo");
            AssertInside(camera, safeArea, raid, "the raid banner", fileName);
            AssertInside(camera, safeArea, cargo, "the cargo cluster", fileName);

            var clusters = new List<(string What, RectTransform Panel)>
            {
                ("the KZG readout (Vitals)", Panel("Vitals")),
                ("the objective (Site)", Panel("Site")),
                ("the context bar", Panel("Context Bar")),
                ("the raid banner", raid),
                ("the cargo cluster", cargo),
            };

            var bounds = new List<(string What, Rect Rect)>();
            foreach ((string what, RectTransform panel) in clusters)
            {
                Assert.IsNotNull(panel, $"{fileName}: there is no '{what}' on the HUD to measure");
                Assert.IsTrue(panel.gameObject.activeInHierarchy,
                    $"{fileName}: '{what}' is switched off, so checking that nothing covers it "
                    + "would prove nothing");
                Assert.IsTrue(DrawnBounds(camera, panel, out Rect drawn),
                    $"{fileName}: '{what}' is switched on but draws nothing, so it is not really "
                    + "on screen and this capture does not show what it claims to");
                bounds.Add((what, drawn));
            }

            var collisions = new List<string>();
            for (int i = 0; i < bounds.Count; i++)
                for (int j = i + 1; j < bounds.Count; j++)
                    if (Overlaps(bounds[i].Rect, bounds[j].Rect))
                        collisions.Add(
                            $"{bounds[i].What} x[{bounds[i].Rect.xMin:0},{bounds[i].Rect.xMax:0}] "
                            + $"y[{bounds[i].Rect.yMin:0},{bounds[i].Rect.yMax:0}]  vs  "
                            + $"{bounds[j].What} x[{bounds[j].Rect.xMin:0},{bounds[j].Rect.xMax:0}] "
                            + $"y[{bounds[j].Rect.yMin:0},{bounds[j].Rect.yMax:0}]");

            Assert.IsEmpty(collisions,
                $"{fileName}: the HUD draws these on top of each other:\n  "
                + string.Join("\n  ", collisions));
        }

        /// <summary>Every drawn label under a panel, asserted to sit inside the safe area.</summary>
        private static void AssertInside(Camera camera, RectTransform safeArea, RectTransform panel,
            string what, string fileName)
        {
            Assert.IsNotNull(panel, $"{fileName}: there is no '{what}' on screen to measure");
            Assert.IsTrue(panel.gameObject.activeInHierarchy,
                $"{fileName}: '{what}' is switched off, so measuring it proves nothing");

            Rect outer = ScreenRect(camera, safeArea);
            var failures = new List<string>();
            int measured = 0;

            foreach (RectTransform child in panel.GetComponentsInChildren<RectTransform>(true))
            {
                if (!child.gameObject.activeInHierarchy) continue;
                measured++;
                Rect inner = ScreenRect(camera, child);
                if (inner.xMin < outer.xMin - Tolerance || inner.xMax > outer.xMax + Tolerance
                    || inner.yMin < outer.yMin - Tolerance || inner.yMax > outer.yMax + Tolerance)
                {
                    failures.Add($"{child.name} x[{inner.xMin:0},{inner.xMax:0}] "
                                 + $"y[{inner.yMin:0},{inner.yMax:0}]");
                }
            }

            Assert.Greater(measured, 0, $"{fileName}: '{what}' drew nothing, so containment was never checked");
            Assert.IsEmpty(failures,
                $"{fileName}: '{what}' renders outside the safe area x[{outer.xMin:0},{outer.xMax:0}] "
                + $"y[{outer.yMin:0},{outer.yMax:0}]:\n  " + string.Join("\n  ", failures));
        }

        // =====================================================================
        // Geometry
        // =====================================================================

        /// <summary>
        /// A rect in the pixels of the frame being written, which is the only
        /// frame these assertions are about.
        /// <para>
        /// Not <c>GetWorldCorners</c> on its own, which was the first version of
        /// this and was almost worthless. A ScreenSpaceCamera canvas is scaled
        /// into the camera's world box, so one world unit is about 108 canvas
        /// units here — and a 2-unit tolerance, copied over from the containment
        /// fixture where the canvases really are 1920x1080 world units, was
        /// silently granting about 216 canvas units of slack. It was caught by
        /// deliberately feeding this check overlapping rects and watching it pass
        /// anyway. Everything is converted through the capture camera instead, so
        /// the tolerance is two pixels of the actual picture and means that.
        /// </para>
        /// </summary>
        private static Rect ScreenRect(Camera camera, RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return ScreenRect(camera, corners[0], corners[2]);
        }

        private static Rect ScreenRect(Camera camera, Vector3 a, Vector3 b)
        {
            Vector3 pa = RectTransformUtility.WorldToScreenPoint(camera, a);
            Vector3 pb = RectTransformUtility.WorldToScreenPoint(camera, b);
            return Rect.MinMaxRect(
                Mathf.Min(pa.x, pb.x), Mathf.Min(pa.y, pb.y),
                Mathf.Max(pa.x, pb.x), Mathf.Max(pa.y, pb.y));
        }

        private static bool Overlaps(Rect a, Rect b) =>
            a.xMin < b.xMax - Tolerance && b.xMin < a.xMax - Tolerance &&
            a.yMin < b.yMax - Tolerance && b.yMin < a.yMax - Tolerance;

        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(
            Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        /// <summary>
        /// What a panel actually puts on the screen: images by their rects, text by
        /// the bounds of the glyphs it really rendered. See the class summary for
        /// why the reserved rect is the wrong answer for text.
        /// </summary>
        private static bool DrawnBounds(Camera camera, RectTransform panel, out Rect bounds)
        {
            bounds = new Rect();
            bool any = false;

            foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null || !graphic.enabled) continue;
                if (!graphic.gameObject.activeInHierarchy) continue;
                if (graphic.color.a <= 0.01f) continue;

                // TextMeshPro spawns a TMP_SubMeshUI child whenever a label needs a
                // second atlas, and it is a Graphic that is not a TMP_Text and whose
                // rect is its parent label's whole reserved rect. It draws the same
                // glyphs the parent is already measured for, so counting it adds
                // nothing true and drags the union out to the reserved width — which
                // is exactly how the context bar came to report a 14px overlap with
                // the vital cluster that no pixel of the capture actually contains.
                if (graphic is TMPro.TMP_SubMeshUI) continue;

                Rect drawn;
                var text = graphic as TMP_Text;
                if (text != null)
                {
                    if (string.IsNullOrEmpty(text.text)) continue;
                    if (!GlyphRect(camera, text, out drawn)) continue;
                }
                else
                {
                    drawn = ScreenRect(camera, graphic.rectTransform);
                }

                bounds = any ? Union(bounds, drawn) : drawn;
                any = true;
            }

            return any;
        }

        /// <summary>
        /// The box the glyphs of one label really cover, walked character by
        /// character.
        /// <para>
        /// <c>characterInfo</c> rather than <c>TMP_Text.textBounds</c>: it is the
        /// per-glyph record the same <c>ForceMeshUpdate</c> fills in, so what
        /// comes back is the box the letters occupy and not the box reserved for
        /// them. The difference is the whole point here — the context line
        /// reserves the full 1100 units the bar owns and draws a short centred
        /// sentence in the middle of it, measured at x[810,1109] against a
        /// reserved x[410,1510].
        /// </para>
        /// <para>
        /// Returns false when nothing visible was laid out — an all-whitespace
        /// label draws nothing and must not drag a union anywhere.
        /// </para>
        /// </summary>
        private static bool GlyphRect(Camera camera, TMP_Text text, out Rect rect)
        {
            rect = new Rect();
            text.ForceMeshUpdate();

            TMPro.TMP_TextInfo info = text.textInfo;
            if (info == null) return false;

            float xMin = float.MaxValue, yMin = float.MaxValue;
            float xMax = float.MinValue, yMax = float.MinValue;
            bool visible = false;

            int count = Mathf.Min(info.characterCount, info.characterInfo.Length);
            for (int i = 0; i < count; i++)
            {
                TMPro.TMP_CharacterInfo ch = info.characterInfo[i];
                if (!ch.isVisible) continue;
                xMin = Mathf.Min(xMin, Mathf.Min(ch.bottomLeft.x, ch.topRight.x));
                xMax = Mathf.Max(xMax, Mathf.Max(ch.bottomLeft.x, ch.topRight.x));
                yMin = Mathf.Min(yMin, Mathf.Min(ch.bottomLeft.y, ch.topRight.y));
                yMax = Mathf.Max(yMax, Mathf.Max(ch.bottomLeft.y, ch.topRight.y));
                visible = true;
            }

            if (!visible) return false;

            rect = ScreenRect(camera,
                text.rectTransform.TransformPoint(new Vector3(xMin, yMin, 0f)),
                text.rectTransform.TransformPoint(new Vector3(xMax, yMax, 0f)));
            return true;
        }

        // =====================================================================
        // Pixels
        // =====================================================================

        /// <summary>
        /// A capture that rendered nothing must fail rather than ship. Seventeen
        /// captures in this suite were once flat rectangles and it stayed green,
        /// because writing a PNG succeeds whether or not anything was drawn — so
        /// "not one flat colour" is not enough on its own either. A HUD over a
        /// starfield has thousands of distinct colours; a backdrop with one stray
        /// lit pixel has two, and would pass the flat test.
        /// </summary>
        private static void AssertTheFrameHasAPictureOnIt(Color32[] pixels, int width, int height,
            string fileName)
        {
            var distinct = new HashSet<int>();
            for (int i = 0; i < pixels.Length; i += 2)
                distinct.Add((pixels[i].r << 16) | (pixels[i].g << 8) | pixels[i].b);

            Assert.Greater(distinct.Count, 1, fileName + " is a single flat colour — nothing rendered");
            Assert.Greater(distinct.Count, 64,
                $"{fileName} has only {distinct.Count} distinct colours in it, which is not a "
                + "rendered interface");
            Debug.Log($"[hud-warning-capture] {fileName}: {width}x{height}, "
                      + $"{distinct.Count} distinct colours");
        }

        /// <summary>
        /// Both warnings measured on the pixels that were really written, in the
        /// mode they were really written under.
        /// <para>
        /// The containment fixture measures the colour the label is carrying,
        /// against the token for the surface behind it, and says plainly that
        /// pixel measurement belongs here because this is what owns the render.
        /// This is that measurement: the label's own glyph box is located in the
        /// captured frame, the lightest pixel in it is the ink the player sees and
        /// the most common one is the surface it sits on, and the ratio between
        /// them is what a reader actually gets. It catches what a token check
        /// cannot — a correct colour drawn at the wrong opacity, behind something,
        /// or onto a surface the palette did not expect.
        /// </para>
        /// </summary>
        private static void AssertBothWarningsAreLegible(Color32[] pixels, int width, int height,
            Camera camera, string fileName, string modeName)
        {
            string where = modeName == null ? fileName : $"{fileName} ({modeName})";

            foreach ((string what, RectTransform label) in new[]
                     {
                         ("the raid headline",
                             UiFinalHudWarningState.Find(Panel("Raid"), "Title")),
                         ("the cargo over-capacity warning",
                             UiFinalHudWarningState.Find(Panel("Cargo"), "Warning")),
                     })
            {
                Assert.IsNotNull(label, $"{where}: there is no '{what}' in the captured frame");
                var text = label.GetComponent<TMP_Text>();
                Assert.IsNotNull(text, $"{where}: '{what}' is not a label");
                Assert.IsFalse(string.IsNullOrEmpty(text.text),
                    $"{where}: '{what}' is on screen with nothing written on it");

                float ratio = MeasuredContrast(pixels, width, height, camera, label,
                    out Color32 ink, out Color32 surface, out int sampled);

                Assert.Greater(sampled, 32,
                    $"{where}: '{what}' covers only {sampled} pixels of the captured frame, "
                    + "which is too few to have been measured at all");
                Assert.AreNotEqual(
                    (ink.r << 16) | (ink.g << 8) | ink.b,
                    (surface.r << 16) | (surface.g << 8) | surface.b,
                    $"{where}: every pixel of '{what}' is the same colour, so the label is "
                    + "not actually drawn in the frame that was written");
                Assert.GreaterOrEqual(ratio, MinimumContrast,
                    $"{where}: '{what}' measures {ratio:0.00}:1 in the captured pixels — ink "
                    + $"({ink.r},{ink.g},{ink.b}) on ({surface.r},{surface.g},{surface.b}) — "
                    + $"below the {MinimumContrast:0.0}:1 WCAG AA floor for text");

                Debug.Log($"[hud-warning-capture] {where}: {what} {ratio:0.00}:1 "
                          + $"ink ({ink.r},{ink.g},{ink.b}) on ({surface.r},{surface.g},{surface.b}) "
                          + $"over {sampled} px");
            }
        }

        /// <summary>
        /// The contrast a reader gets from one label in one captured frame.
        /// Lightest pixel is the ink (both warnings are light text on a dark
        /// card); most common pixel is the surface under it.
        /// </summary>
        private static float MeasuredContrast(Color32[] pixels, int width, int height, Camera camera,
            RectTransform label, out Color32 ink, out Color32 surface, out int sampled)
        {
            Rect box = PixelRect(camera, label, width, height);
            ink = new Color32(0, 0, 0, 255);
            surface = new Color32(0, 0, 0, 255);
            sampled = 0;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(box.xMin), 0, width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(box.xMax), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(box.yMin), 0, height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(box.yMax), 0, height - 1);
            if (x1 <= x0 || y1 <= y0) return 0f;

            var counts = new Dictionary<int, int>();
            float brightest = -1f;
            int bestKey = 0, bestCount = -1;

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Color32 p = pixels[y * width + x];
                    sampled++;

                    float lum = Luminance(p);
                    if (lum > brightest) { brightest = lum; ink = p; }

                    int key = (p.r << 16) | (p.g << 8) | p.b;
                    counts.TryGetValue(key, out int n);
                    counts[key] = n + 1;
                    if (n + 1 > bestCount) { bestCount = n + 1; bestKey = key; }
                }
            }

            surface = new Color32((byte)((bestKey >> 16) & 0xFF), (byte)((bestKey >> 8) & 0xFF),
                (byte)(bestKey & 0xFF), 255);
            return ContrastRatio(ink, surface);
        }

        /// <summary>
        /// A label's glyph box in the captured texture's pixels. ReadPixels and the
        /// camera both put (0,0) at the bottom left, so no flip is needed and one
        /// introduced "to be safe" would measure the wrong end of the screen.
        /// </summary>
        private static Rect PixelRect(Camera camera, RectTransform label, int width, int height)
        {
            Rect r = ScreenRect(camera, label);
            return Rect.MinMaxRect(
                Mathf.Clamp(r.xMin, 0f, width - 1f), Mathf.Clamp(r.yMin, 0f, height - 1f),
                Mathf.Clamp(r.xMax, 0f, width - 1f), Mathf.Clamp(r.yMax, 0f, height - 1f));
        }

        private static string Fingerprint(string fileName)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.Convert.ToBase64String(
                    sha.ComputeHash(File.ReadAllBytes(Path.Combine(OutputDir, fileName))));
        }

        // =====================================================================
        // WCAG
        // =====================================================================

        private static float ContrastRatio(Color32 a, Color32 b)
        {
            float la = Luminance(a), lb = Luminance(b);
            float hi = Mathf.Max(la, lb), lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        private static float Luminance(Color32 c) =>
            0.2126f * Channel(c.r / 255f) + 0.7152f * Channel(c.g / 255f) + 0.0722f * Channel(c.b / 255f);

        private static float Channel(float v) =>
            v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
    }
}
