using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Brand;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The brand presentation photographed and pinned across the fourteen shipping
    /// configurations — five resolution/quality pairs, six accessibility variants at
    /// 1080p, and three more at 140% interface scale.
    /// <para>
    /// This is a capture fixture and runs <b>alone in its own Unity process</b>. Stacking
    /// capture fixtures in one run is what drove <c>UnloadUnusedAssets</c> from 10ms to
    /// 64 seconds in this project and eventually stopped the run producing output at all.
    /// </para>
    /// <para>
    /// Every visual rule the brief names is asserted here as well as photographed:
    /// nothing is left to be judged by looking at a PNG, because a person looking at a
    /// contact sheet cannot see that the 21:9 plate was stretched from the 16:9 master
    /// rather than being the authored ultrawide export. The pictures corroborate; the
    /// assertions decide.
    /// </para>
    /// </summary>
    // Graphics: every test here renders a live camera into a RenderTexture and reads the pixels back. Under -nographics the
    // render is a segfault risk in RenderOffscreenCameras, not merely a blank frame, so this
    // fixture must never be selected into a headless worker. It carries no ReleaseRegression
    // category on purpose -- the headless gate selects by that name and must not reach here.
    [Category("Graphics")]
    public class NebulaBrandMatrixCaptureTests
    {
        // ---- the matrix ------------------------------------------------------

        /// <summary>
        /// One shipping configuration.
        /// <para>
        /// Quality is carried explicitly rather than derived: the brief pins a quality
        /// level to each of the five base rows, and the variants inherit the level of
        /// the base row at their own resolution — 1080p variants are Medium because the
        /// 1080p base row is, the 140% rows take the level of the resolution they
        /// re-test. Deriving it instead would have made the table a function of this
        /// file rather than of the brief.
        /// </para>
        /// </summary>
        private readonly struct Config
        {
            public readonly string Id;
            public readonly int Width;
            public readonly int Height;
            public readonly int Quality;
            public readonly string QualityName;
            public readonly string Accessibility;
            public readonly float Scale;

            public Config(string id, int width, int height, int quality, string qualityName,
                string accessibility, float scale)
            {
                Id = id; Width = width; Height = height;
                Quality = quality; QualityName = qualityName;
                Accessibility = accessibility; Scale = scale;
            }

            public override string ToString() => Id;
        }

        /// <summary>Quality level indices, from the project's own ladder.</summary>
        private const int Low = 1, Medium = 2, High = 3;

        private const string None = "none";
        private const string HighContrast = "high-contrast";
        private const string ReducedMotion = "reduced-motion";
        private const string MotionOff = "motion-off";
        private const string Photosensitivity = "photosensitivity-safe";
        private const string FlashReduction = "flash-reduction";

        private static readonly Config[] Matrix =
        {
            // base: resolution x quality
            new Config("01-1280x720-low",            1280,  720, Low,    "Low",    None, 1f),
            new Config("02-1920x1080-medium",        1920, 1080, Medium, "Medium", None, 1f),
            new Config("03-3840x2160-high",          3840, 2160, High,   "High",   None, 1f),
            new Config("04-2560x1600-high",          2560, 1600, High,   "High",   None, 1f),
            new Config("05-3440x1440-high",          3440, 1440, High,   "High",   None, 1f),

            // 1920x1080 accessibility variants
            new Config("06-1920x1080-high-contrast", 1920, 1080, Medium, "Medium", HighContrast,    1f),
            new Config("07-1920x1080-reduced-motion",1920, 1080, Medium, "Medium", ReducedMotion,   1f),
            new Config("08-1920x1080-motion-off",    1920, 1080, Medium, "Medium", MotionOff,       1f),
            new Config("09-1920x1080-photosensitivity-safe", 1920, 1080, Medium, "Medium", Photosensitivity, 1f),
            new Config("10-1920x1080-flash-reduction",1920,1080, Medium, "Medium", FlashReduction,  1f),
            new Config("11-1920x1080-scale140",      1920, 1080, Medium, "Medium", None, 1.4f),

            // the remaining 140% rows
            new Config("12-1280x720-scale140",       1280,  720, Low,    "Low",    None, 1.4f),
            new Config("13-2560x1600-scale140",      2560, 1600, High,   "High",   None, 1.4f),
            new Config("14-3440x1440-scale140",      3440, 1440, High,   "High",   None, 1.4f),
        };

        // ---- evidence records ------------------------------------------------

        [Serializable]
        public class MatrixCell
        {
            public string id;
            public int width;
            public int height;
            public int qualityLevel;
            public string quality;
            public string accessibility;
            public float uiScale;

            public string keyArtAspect;
            public string keyArtDetail;
            public string keyArtSprite;
            public string keyArtResidentPath;
            public int keyArtPixelWidth;
            public int keyArtPixelHeight;
            public float visibleWindowWidth;
            public float visibleWindowHeight;
            public bool combinedPlateUsed;

            public string logoStyle;
            public string logoQuality;
            public string logoSprite;
            public string logoResidentPath;
            public float logoAspect;
            public float logoDrawnAspect;

            public string loadingPresentation;
            public string loadingFidelity;
            public string loadingSprite;
            public int loadingFrameCount;
            public bool loadingAnimated;
            public bool reducedFellBackToStatic;

            public bool textWordmarkVisible;
            public bool menuScrolls;
            public int fallbacksUsed;
            public int discoveredOptions;
            public float footerClearance;

            public string menuImage;
            public string loadingImage;
            public string logoImage;
            public int menuImageWidth;
            public int menuImageHeight;
            public int distinctColours;
        }

        [Serializable]
        public class MatrixManifest
        {
            public string unityVersion;
            public string graphicsDevice;
            public string capturedUtc;
            public int captureLongEdgeCap;
            public List<MatrixCell> cells = new List<MatrixCell>();
        }

        /// <summary>
        /// The long edge the stored full-frame evidence is capped at.
        /// <para>
        /// The render happens at the configuration's real resolution — that is what
        /// decides the authored aspect, the detail rung, the mark tier and the whole
        /// layout — and only the written file is bounded. Fourteen configurations at
        /// native size, three files each, is around 90MB of PNG in a repository whose
        /// pack is already 400MB, for pictures whose job is to be looked at in a contact
        /// sheet. The logo crops below are written at native pixels precisely because
        /// they are the ones where resampling would hide the defect.
        /// </para>
        /// </summary>
        private const int LongEdgeCap = 1600;

        private NebulaUiShell shell;
        private NebulaMainMenuScreen menu;
        private Camera captureCamera;
        private RenderTexture captureTarget;
        private int enteredQuality;
        private static MatrixManifest manifest;

        private sealed class StubLauncher : INebulaSessionLauncher
        {
            public void StartNewCampaign() { }
            public void ContinueFromSlot(int slot) { }
            public void QuitGame() { }
            public void ReturnToMainMenu() { }
            public void RestartCampaign() { }
        }

        [SetUp]
        public void Setup()
        {
            enteredQuality = QualitySettings.GetQualityLevel();
            ExclusiveModal.ResetForSession();
            NebulaBrandAssetCatalog.Release();
            NebulaLoadingPresenter.ForceClear();
            AnimationAccessibility.ResetToDefaults();
            ResetLighting();
            NebulaUiShell.UiScale = 1f;

            shell = NebulaUiShell.Create();
            menu = NebulaMainMenuScreen.Create(shell, new StubLauncher());
        }

        [TearDown]
        public void Teardown()
        {
            NebulaLoadingPresenter.ForceClear();

            if (shell != null)
                foreach (UiLayer layer in new[] { UiLayer.Menu, UiLayer.Overlay })
                {
                    Canvas canvas = shell.CanvasFor(layer);
                    if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                }

            if (captureCamera != null)
            {
                captureCamera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(captureCamera.gameObject);
            }
            if (captureTarget != null) { captureTarget.Release(); UnityEngine.Object.DestroyImmediate(captureTarget); }
            captureCamera = null;
            captureTarget = null;

            if (menu != null) UnityEngine.Object.DestroyImmediate(menu.gameObject);
            if (shell != null) UnityEngine.Object.DestroyImmediate(shell.gameObject);
            menu = null;
            shell = null;

            NebulaUiShell.UiScale = 1f;
            AnimationAccessibility.ResetToDefaults();
            ResetLighting();
            NebulaBrandAssetCatalog.Release();
            ExclusiveModal.ResetForSession();
            QualitySettings.SetQualityLevel(enteredQuality, false);
        }

        private static void ResetLighting()
        {
            LightingAccessibility.HighContrast = false;
            LightingAccessibility.PhotosensitivitySafeMode = false;
            LightingAccessibility.FlashReduction = false;
        }

        // ---- the capture -----------------------------------------------------

        /// <summary>
        /// Walks the whole matrix, photographs three surfaces per configuration and
        /// records what each one selected.
        /// <para>
        /// One test rather than fourteen: the shell, the menu and the key-art textures
        /// are rebuilt per fixture case, and fourteen rebuilds inside one process is
        /// the shape that has previously turned a two minute run into an hour.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator EveryConfigurationIsPhotographedAndRecorded()
        {
            RequireGraphicsDevice();

            manifest = new MatrixManifest
            {
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                capturedUtc = DateTime.UtcNow.ToString("O"),
                captureLongEdgeCap = LongEdgeCap,
            };

            Directory.CreateDirectory(OutputDir);

            foreach (Config config in Matrix)
            {
                yield return Apply(config);

                var cell = new MatrixCell
                {
                    id = config.Id,
                    width = config.Width,
                    height = config.Height,
                    qualityLevel = config.Quality,
                    quality = config.QualityName,
                    accessibility = config.Accessibility,
                    uiScale = config.Scale,
                };

                RecordSelection(cell);
                AssertPresentationRules(config, cell);

                // 1. the main menu
                cell.menuImage = $"brand-matrix-{config.Id}-menu.png";
                Texture2D frame = Grab(config.Width, config.Height);
                try
                {
                    cell.distinctColours = DistinctColours(frame);
                    Assert.Greater(cell.distinctColours, 64,
                        $"{config.Id}: the menu frame has only {cell.distinctColours} distinct colours; "
                        + "nothing meaningful rendered");

                    WriteScaled(frame, cell.menuImage, out int mw, out int mh);
                    cell.menuImageWidth = mw;
                    cell.menuImageHeight = mh;

                    // 3. the logo region, at native pixels
                    cell.logoImage = $"brand-matrix-{config.Id}-logo.png";
                    WriteLogoCrop(frame, config, cell.logoImage);
                }
                finally { UnityEngine.Object.DestroyImmediate(frame); }

                // 2. the loading presentation, over this same menu. Asserted after the
                // capture rather than before it, because what the surface actually drew
                // is only known once it has been built and stepped.
                cell.loadingImage = $"brand-matrix-{config.Id}-loading.png";
                yield return CaptureLoading(config, cell);
                AssertLoadingRules(config, cell);

                manifest.cells.Add(cell);
            }

            Assert.AreEqual(Matrix.Length, manifest.cells.Count, "a configuration produced no cell");
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest, true));
        }

        /// <summary>
        /// The manifest is complete: fourteen cells, every field filled, every named
        /// file on disk with bytes in it.
        /// <para>
        /// Ordered after the capture by NUnit's alphabetical case order, and guarded so
        /// that a run of this case alone says so rather than passing on an empty
        /// manifest — a "no PENDING cells" check that can itself be vacuous is the same
        /// failure it exists to catch.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheManifestHasNoPendingCells()
        {
            yield return null;

            Assert.IsNotNull(manifest,
                "the capture case did not run in this process; the manifest cannot be checked");
            Assert.AreEqual(Matrix.Length, manifest.cells.Count, "the manifest is short of the matrix");

            foreach (MatrixCell cell in manifest.cells)
            {
                Assert.IsNotEmpty(cell.keyArtSprite, cell.id + ": no key art recorded");
                Assert.IsNotEmpty(cell.logoSprite, cell.id + ": no logo recorded");
                Assert.IsNotEmpty(cell.loadingPresentation, cell.id + ": no loading presentation recorded");
                Assert.IsNotEmpty(cell.loadingSprite, cell.id + ": no loading sprite recorded");

                foreach (string file in new[] { cell.menuImage, cell.loadingImage, cell.logoImage })
                {
                    string path = Path.Combine(OutputDir, file);
                    Assert.IsTrue(File.Exists(path), cell.id + ": " + file + " was never written");
                    Assert.Greater(new FileInfo(path).Length, 4096, cell.id + ": " + file + " is a stub");
                }
            }
        }

        /// <summary>
        /// The wordmark is not merely bound — it puts pixels on the screen, and they
        /// are the pixels of the cut that was selected.
        /// <para>
        /// Photographed twice at the same viewport, once with the wordmark active and
        /// once without, and the two frames must differ over the wordmark's own
        /// rectangle. Nothing short of reading the pixels can tell a drawn logo from
        /// one that is correctly positioned underneath an opaque backdrop, which is the
        /// state this menu was actually shipping.
        /// </para>
        /// <para>
        /// Then again with High Contrast on. The flat cut and the effects cut are
        /// different images, so a frame that is byte-identical across that switch is a
        /// frame in which neither of them is reaching the screen.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheWordmarkPutsItsOwnPixelsOnTheScreen()
        {
            RequireGraphicsDevice();

            var config = new Config("pixel-check", 1920, 1080, Medium, "Medium", None, 1f);
            yield return Apply(config);

            GameObject markObject = menu.KeyArt.LogoImage.gameObject;
            Rect region = ScreenRect((RectTransform)markObject.transform, config.Width, config.Height);
            Assert.Greater(region.width, 8f, "the wordmark has no width to photograph");

            long withMark = RegionSignature(config, region);

            markObject.SetActive(false);
            Canvas.ForceUpdateCanvases();
            yield return null;
            long withoutMark = RegionSignature(config, region);

            markObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.AreNotEqual(withoutMark, withMark,
                "the wordmark's rectangle is pixel-identical with the wordmark switched off; "
                + "it is bound but something is drawn over it");

            // And the two authored cuts really do reach the screen as different pictures.
            long effects = RegionSignature(config, region);
            LightingAccessibility.HighContrast = true;
            menu.KeyArt.Bind();
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.AreEqual("Flat", menu.KeyArt.SelectedMarkStyle.ToString(),
                "precondition: High Contrast must select the flat cut");
            long flat = RegionSignature(config, region);

            Assert.AreNotEqual(effects, flat,
                "High Contrast selected the flat cut but the screen did not change; "
                + "the selection is not reaching the pixels");
        }

        /// <summary>A cheap content hash of one rectangle of the rendered frame.</summary>
        private long RegionSignature(Config config, Rect region)
        {
            Texture2D frame = Grab(config.Width, config.Height);
            try
            {
                Color32[] pixels = frame.GetPixels32();
                long hash = 17;
                int x0 = Mathf.RoundToInt(region.xMin), x1 = Mathf.RoundToInt(region.xMax);
                int y0 = Mathf.RoundToInt(region.yMin), y1 = Mathf.RoundToInt(region.yMax);
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        Color32 pixel = pixels[y * frame.width + x];
                        hash = hash * 31 + pixel.r;
                        hash = hash * 31 + pixel.g;
                        hash = hash * 31 + pixel.b;
                    }
                return hash;
            }
            finally { UnityEngine.Object.DestroyImmediate(frame); }
        }

        // ---- the rules -------------------------------------------------------

        /// <summary>
        /// The plate, the mark and the layout, for the configuration that was just set
        /// up and is about to be photographed.
        /// </summary>
        private void AssertPresentationRules(Config config, MatrixCell cell)
        {
            string where = config.Id;

            // The plate is authored for the aspect and is not cropped.
            NebulaBrandAssetCatalog.KeyArtAspect expected =
                NebulaBrandAssetCatalog.AspectFor(config.Width, config.Height);
            Assert.AreEqual(expected.ToString(), cell.keyArtAspect, where + ": wrong authored aspect");
            Assert.AreEqual(1f, cell.visibleWindowWidth, 0.01f, where + ": the plate is cropped sideways");
            Assert.AreEqual(1f, cell.visibleWindowHeight, 0.01f, where + ": the plate is cropped vertically");
            Assert.IsTrue(menu.KeyArt.SafeAreaHonoured, where + ": the crop lost the critical safe area");

            // The logo keeps its own proportions.
            Assert.AreEqual(cell.logoAspect, cell.logoDrawnAspect, 0.01f,
                where + ": the wordmark is being distorted to fill its box");

            // Never the combined plate, never the drawn text title.
            Assert.IsFalse(cell.combinedPlateUsed, where + ": the pre-delivery composited plate is on screen");
            Assert.IsFalse(cell.textWordmarkVisible, where + ": the drawn text wordmark is visible");
            Assert.AreEqual(0, cell.fallbacksUsed, where + ": something had to be substituted");

            // High Contrast draws the flat cut; everything else draws the effects cut.
            string wantedStyle = config.Accessibility == HighContrast ? "Flat" : "Effects";
            Assert.AreEqual(wantedStyle, cell.logoStyle, where + ": wrong mark cut");
            StringAssert.Contains(config.Accessibility == HighContrast ? "flat" : "effects", cell.logoSprite,
                where + ": the drawn sprite disagrees with the selected cut");

            // 21:9 keeps the wordmark in the dark left extension.
            if (expected == NebulaBrandAssetCatalog.KeyArtAspect.UltraWide)
            {
                NebulaBrandKeyArtView.LogoProfile profile =
                    NebulaBrandKeyArtView.LogoProfileFor(expected);
                Assert.LessOrEqual(profile.XMin + profile.Width, 0.2558f,
                    where + ": the ultrawide wordmark reaches out of the dark left extension "
                    + "and onto the painted scene");
            }

            // 16:10 keeps the wordmark clear of the menu column.
            Rect logo = WorldRect((RectTransform)menu.KeyArt.LogoImage.transform);
            Rect window = WorldRect(menu.MenuViewport);
            Assert.GreaterOrEqual(logo.yMin, window.yMax - 0.5f,
                where + ": the wordmark runs into the menu column");

            // The footer is clear of the entries at both ends of the scroll.
            Assert.GreaterOrEqual(cell.footerClearance, -0.5f,
                where + $": the entries overlap the build line by {-cell.footerClearance:0.###} world units");
        }

        /// <summary>
        /// Motion Off is the still plate and nothing else; Reduced Motion and
        /// Photosensitivity Safe Mode play the reduced sheet and are never the still;
        /// everything else plays the full loop.
        /// </summary>
        private void AssertLoadingRules(Config config, MatrixCell cell)
        {
            string where = config.Id;
            switch (config.Accessibility)
            {
                case MotionOff:
                    Assert.AreEqual("Static", cell.loadingPresentation, where + ": Motion Off must be static");
                    Assert.IsFalse(cell.loadingAnimated, where + ": Motion Off is animating");
                    StringAssert.Contains("static", cell.loadingSprite, where + ": Motion Off is not the still plate");
                    break;
                case ReducedMotion:
                case Photosensitivity:
                    Assert.AreEqual("ReducedAnimated", cell.loadingPresentation,
                        where + ": must play the reduced sheet");
                    Assert.IsTrue(cell.loadingAnimated, where + ": Reduced Motion fell back to a still");
                    Assert.IsFalse(cell.reducedFellBackToStatic,
                        where + ": the reduced sheet did not resolve and the still plate was drawn");
                    StringAssert.Contains("reduced", cell.loadingSprite, where + ": not a reduced-sheet frame");
                    break;
                default:
                    Assert.AreEqual("Animated", cell.loadingPresentation, where + ": must play the full loop");
                    Assert.IsTrue(cell.loadingAnimated, where + ": the full loop is not animating");
                    break;
            }
        }

        // ---- setting a configuration up --------------------------------------

        private IEnumerator Apply(Config config)
        {
            QualitySettings.SetQualityLevel(config.Quality, false);

            AnimationAccessibility.ResetToDefaults();
            ResetLighting();
            switch (config.Accessibility)
            {
                case HighContrast: LightingAccessibility.HighContrast = true; break;
                case ReducedMotion: AnimationAccessibility.ReducedMotion = true; break;
                case MotionOff: AnimationAccessibility.MotionOff = true; break;
                case Photosensitivity: LightingAccessibility.PhotosensitivitySafeMode = true; break;
                case FlashReduction: LightingAccessibility.FlashReduction = true; break;
            }

            NebulaUiShell.UiScale = config.Scale;
            yield return SetViewport(config.Width, config.Height);

            menu.KeyArt.Bind();
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        private void RecordSelection(MatrixCell cell)
        {
            NebulaBrandKeyArtView view = menu.KeyArt;
            Sprite art = view.GetComponent<Image>().sprite;
            Sprite mark = view.LogoImage == null ? null : view.LogoImage.sprite;

            cell.keyArtAspect = view.SelectedAspect.ToString();
            cell.keyArtDetail = view.SelectedDetail.ToString();
            cell.keyArtSprite = art == null ? "" : art.name;
            cell.keyArtResidentPath = NebulaBrandAssetCatalog.ResidentKeyArtPath ?? "";
            cell.keyArtPixelWidth = art == null ? 0 : Mathf.RoundToInt(art.rect.width);
            cell.keyArtPixelHeight = art == null ? 0 : Mathf.RoundToInt(art.rect.height);
            cell.visibleWindowWidth = view.VisibleWindow.width;
            cell.visibleWindowHeight = view.VisibleWindow.height;
            cell.combinedPlateUsed = NebulaBrandAssetCatalog.CombinedLogoKeyArtUsed;

            cell.logoStyle = view.SelectedMarkStyle.ToString();
            cell.logoQuality = NebulaBrandAssetCatalog
                .MarkQualityFor(RealHeight(), QualitySettings.GetQualityLevel()).ToString();
            cell.logoSprite = mark == null ? "" : mark.name;
            cell.logoResidentPath = NebulaBrandAssetCatalog.ResidentLogoPath ?? "";
            cell.logoAspect = mark == null ? 0f : mark.rect.width / mark.rect.height;
            var logoRect = (RectTransform)view.LogoImage.transform;
            cell.logoDrawnAspect = logoRect.rect.height <= 0f ? 0f : logoRect.rect.width / logoRect.rect.height;

            cell.textWordmarkVisible = menu.TextWordmarkVisible;
            cell.menuScrolls = menu.MenuScrolls;
            cell.fallbacksUsed = NebulaBrandAssetCatalog.FallbacksUsed;
            cell.discoveredOptions = CountOptions();
            cell.footerClearance = FooterClearance();
        }

        /// <summary>Real viewport pixels, which is what the mark tier is chosen from.</summary>
        private int RealHeight()
        {
            Canvas canvas = shell.CanvasFor(UiLayer.Menu);
            float scale = canvas == null || canvas.scaleFactor <= 0f ? 1f : canvas.scaleFactor;
            return Mathf.Max(Mathf.RoundToInt(shell.Root(UiLayer.Menu).rect.height * scale), 1);
        }

        private int CountOptions()
        {
            int count = 0;
            foreach (Button button in menu.MenuColumn.GetComponentsInChildren<Button>(true))
                if (button.gameObject.activeInHierarchy && button.IsInteractable()) count++;
            return count;
        }

        /// <summary>
        /// The smallest gap between any entry and the build line, measured at both ends
        /// of the scroll. Negative is a collision.
        /// </summary>
        private float FooterClearance()
        {
            RectTransform footer = FindChild("Footer");
            if (footer == null) return 0f;
            Rect build = WorldRect(footer);

            var scroller = menu.MenuViewport.GetComponent<ScrollRect>();
            float worst = float.MaxValue;

            foreach (float position in new[] { 1f, 0f })
            {
                if (menu.MenuScrolls && scroller != null) scroller.verticalNormalizedPosition = position;
                Canvas.ForceUpdateCanvases();

                foreach (Button button in menu.MenuColumn.GetComponentsInChildren<Button>(true))
                {
                    if (!button.gameObject.activeInHierarchy) continue;
                    Rect entry = WorldRect((RectTransform)button.transform);
                    // Only entries actually inside the window can collide with the
                    // footer; the ones scrolled out are clipped by the mask.
                    Rect window = WorldRect(menu.MenuViewport);
                    if (entry.yMax < window.yMin || entry.yMin > window.yMax) continue;
                    worst = Mathf.Min(worst, entry.yMin - build.yMax);
                }
            }

            if (menu.MenuScrolls && scroller != null) scroller.verticalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();
            return worst == float.MaxValue ? 0f : worst;
        }

        // ---- photography -----------------------------------------------------

        private IEnumerator CaptureLoading(Config config, MatrixCell cell)
        {
            NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, NebulaLoadingOverlay.DefaultCaption);
            Assert.IsNotNull(overlay, config.Id + ": the loading overlay did not build");

            // A frame into the loop rather than frame zero, so an animated sheet that
            // resolved but never advances is visibly different from a still plate.
            if (overlay.IsAnimated) overlay.AdvanceForTest(11f / NebulaBrandAssetCatalog.LoadingFps);
            yield return null;
            Canvas.ForceUpdateCanvases();

            cell.loadingPresentation = overlay.ResolvedPresentation.ToString();
            cell.loadingFidelity = NebulaBrandAssetCatalog.CurrentFidelity().ToString();
            cell.loadingAnimated = overlay.IsAnimated;
            cell.loadingFrameCount = overlay.FrameCount;
            cell.loadingSprite = overlay.CurrentSprite == null ? "" : overlay.CurrentSprite.name;
            cell.reducedFellBackToStatic = NebulaBrandAssetCatalog.ReducedUsesStatic;

            Texture2D frame = Grab(config.Width, config.Height);
            try
            {
                Assert.Greater(DistinctColours(frame), 32,
                    config.Id + ": the loading frame is effectively blank");
                WriteScaled(frame, cell.loadingImage, out _, out _);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(frame);
                NebulaLoadingOverlay.HideActive();
            }

            yield return null;
        }

        /// <summary>Renders the shell through the capture camera and reads the frame back.</summary>
        private Texture2D Grab(int width, int height)
        {
            Canvas.ForceUpdateCanvases();
            captureCamera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = captureTarget;
            var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                shot.Apply();
                return shot;
            }
            finally { RenderTexture.active = previous; }
        }

        /// <summary>
        /// Writes the frame with its long edge capped, through a GPU blit so the
        /// downscale is filtered rather than point-sampled — a nearest-neighbour
        /// reduction of a 3840px plate produces aliasing that reads as a rendering
        /// defect in the contact sheet.
        /// </summary>
        private void WriteScaled(Texture2D frame, string fileName, out int outWidth, out int outHeight)
        {
            int longEdge = Mathf.Max(frame.width, frame.height);
            if (longEdge <= LongEdgeCap)
            {
                outWidth = frame.width;
                outHeight = frame.height;
                File.WriteAllBytes(Path.Combine(OutputDir, fileName), frame.EncodeToPNG());
                return;
            }

            float ratio = (float)LongEdgeCap / longEdge;
            outWidth = Mathf.Max(1, Mathf.RoundToInt(frame.width * ratio));
            outHeight = Mathf.Max(1, Mathf.RoundToInt(frame.height * ratio));

            RenderTexture small = RenderTexture.GetTemporary(outWidth, outHeight, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            var reduced = new Texture2D(outWidth, outHeight, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(frame, small);
                RenderTexture.active = small;
                reduced.ReadPixels(new Rect(0, 0, outWidth, outHeight), 0, 0);
                reduced.Apply();
                File.WriteAllBytes(Path.Combine(OutputDir, fileName), reduced.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(small);
                UnityEngine.Object.DestroyImmediate(reduced);
            }
        }

        /// <summary>
        /// The wordmark and the top of the menu column, cut out of the native-resolution
        /// frame. Never resampled: this is the crop that has to show whether the mark
        /// is the flat or the effects cut and whether it is sitting on the painted scene.
        /// </summary>
        private void WriteLogoCrop(Texture2D frame, Config config, string fileName)
        {
            var logoRect = (RectTransform)menu.KeyArt.LogoImage.transform;
            Rect logo = ScreenRect(logoRect, config.Width, config.Height);
            Rect window = ScreenRect(menu.MenuViewport, config.Width, config.Height);

            // Padded outward, and taken down far enough to include the first menu
            // entries, so the crop shows the clearance rather than only the mark.
            float pad = Mathf.Max(24f, logo.height * 0.35f);
            float xMin = Mathf.Clamp(logo.xMin - pad, 0f, config.Width - 2f);
            float xMax = Mathf.Clamp(Mathf.Max(logo.xMax, window.xMax) + pad, xMin + 2f, config.Width);
            float yMax = Mathf.Clamp(logo.yMax + pad, 2f, config.Height);
            float yMin = Mathf.Clamp(Mathf.Min(logo.yMin, window.yMax - logo.height) - pad, 0f, yMax - 2f);

            var crop = new Texture2D(Mathf.RoundToInt(xMax - xMin), Mathf.RoundToInt(yMax - yMin),
                TextureFormat.RGB24, false);
            try
            {
                crop.SetPixels(frame.GetPixels(Mathf.RoundToInt(xMin), Mathf.RoundToInt(yMin),
                    crop.width, crop.height));
                crop.Apply();
                Assert.Greater(DistinctColours(crop), 32,
                    config.Id + ": the logo crop is effectively blank");
                File.WriteAllBytes(Path.Combine(OutputDir, fileName), crop.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(crop); }
        }

        /// <summary>A rect's place in the captured frame, in pixels with y up from the bottom.</summary>
        private Rect ScreenRect(RectTransform rect, int width, int height)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            Vector2 min = RectTransformUtility.WorldToScreenPoint(captureCamera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(captureCamera, corners[2]);
            return Rect.MinMaxRect(
                Mathf.Clamp(Mathf.Min(min.x, max.x), 0f, width),
                Mathf.Clamp(Mathf.Min(min.y, max.y), 0f, height),
                Mathf.Clamp(Mathf.Max(min.x, max.x), 0f, width),
                Mathf.Clamp(Mathf.Max(min.y, max.y), 0f, height));
        }

        /// <summary>
        /// Distinct colours over a subsample, quantised to 5 bits a channel.
        /// <para>
        /// A cheaper and stricter test than "is every pixel identical": a frame that
        /// rendered the backdrop and nothing else has two or three colours and passes a
        /// uniformity check, which is how this suite once shipped seventeen blank PNGs.
        /// </para>
        /// </summary>
        private static int DistinctColours(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            var seen = new HashSet<int>();
            int step = Mathf.Max(1, pixels.Length / 40000);
            for (int i = 0; i < pixels.Length; i += step)
                seen.Add((pixels[i].r >> 3) << 10 | (pixels[i].g >> 3) << 5 | (pixels[i].b >> 3));
            return seen.Count;
        }

        // ---- viewport --------------------------------------------------------

        /// <summary>
        /// Points both the menu canvas and the overlay canvas at one camera rendering
        /// into a target of the requested size.
        /// <para>
        /// Both, not just the menu: the loading surface lives on
        /// <see cref="UiLayer.Overlay"/>, which is a different canvas, and a capture
        /// that repointed only the menu would photograph a menu with no loading
        /// picture on it and call that evidence.
        /// </para>
        /// </summary>
        private IEnumerator SetViewport(int width, int height)
        {
            if (captureCamera == null)
            {
                var go = new GameObject("MatrixCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
                captureCamera = go.AddComponent<Camera>();
                captureCamera.orthographic = true;
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = Nebula.UI.Design.NebulaUiDesignTokens.SpaceVoid;
                captureCamera.transform.position = new Vector3(0f, 0f, -100f);
            }

            captureCamera.targetTexture = null;
            if (captureTarget != null) { captureTarget.Release(); UnityEngine.Object.DestroyImmediate(captureTarget); }
            captureTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            captureCamera.targetTexture = captureTarget;

            foreach (UiLayer layer in new[] { UiLayer.Menu, UiLayer.Overlay })
            {
                Canvas canvas = shell.CanvasFor(layer);
                if (canvas == null) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = captureCamera;
                canvas.planeDistance = 100f;
            }

            shell.ApplyScale();
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        // ---- paths and helpers -----------------------------------------------

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        public static string OutputDir => Path.Combine(ProjectRoot, "UIDesign", "Runtime", "BrandMatrix");

        public static string ManifestPath => Path.Combine(OutputDir, "brand-matrix.json");

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); run with graphics enabled to capture");
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private RectTransform FindChild(string name)
        {
            foreach (Transform child in menu.KeyArt.transform.parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child as RectTransform;
            return null;
        }
    }
}
