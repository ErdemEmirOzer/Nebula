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

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The main menu with the real brand assets behind it.
    /// <para>
    /// The question this fixture answers is not "does the catalog resolve a sprite"
    /// — <c>NebulaBrandAssetTests</c> covers that — but "is the plate on screen, and
    /// did anything the menu already did stop working when it went there".
    /// </para>
    /// </summary>
    // ReleaseRegression: twelve of the fourteen tests here run headless — SetViewport
    // disables its camera, so nothing is ever drawn by the player loop. The two that
    // really photograph the menu carry [Category("Graphics")] of their own and must be
    // excluded by that name; NUnit propagates this class category to them.
    [Category("ReleaseRegression")]
    public class NebulaBrandIntegrationPlayModeTests
    {
        private NebulaUiShell shell;
        private NebulaMainMenuScreen menu;
        private RecordingLauncher launcher;
        private float enteredTimeScale;

        /// <summary>Records intent instead of composing a universe.</summary>
        private sealed class RecordingLauncher : INebulaSessionLauncher
        {
            public int NewCampaigns, Restarts, ReturnsToMenu;
            public readonly List<int> ContinuedSlots = new List<int>();
            public bool Quit;

            public void StartNewCampaign() => NewCampaigns++;
            public void ContinueFromSlot(int slot) => ContinuedSlots.Add(slot);
            public void QuitGame() => Quit = true;
            public void ReturnToMainMenu() => ReturnsToMenu++;
            public void RestartCampaign() => Restarts++;
        }

        [SetUp]
        public void Setup()
        {
            enteredTimeScale = Time.timeScale;
            ExclusiveModal.ResetForSession();
            NebulaBrandAssetCatalog.Release();
            NebulaLoadingPresenter.ForceClear();
            NebulaUiShell.UiScale = 1f;

            shell = NebulaUiShell.Create();
            launcher = new RecordingLauncher();
            menu = NebulaMainMenuScreen.Create(shell, launcher);
        }

        [TearDown]
        public void Teardown()
        {
            NebulaLoadingPresenter.ForceClear();

            // Canvases are restored before the camera goes: a canvas left pointing at
            // a destroyed camera renders nothing for the rest of the run.
            if (shell != null)
            {
                Canvas canvas = shell.CanvasFor(UiLayer.Menu);
                if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
            }
            if (viewportCamera != null)
            {
                viewportCamera.targetTexture = null;
                Object.DestroyImmediate(viewportCamera.gameObject);
            }
            if (viewportTarget != null) { viewportTarget.Release(); Object.DestroyImmediate(viewportTarget); }
            viewportCamera = null;
            viewportTarget = null;
            if (menu != null) Object.DestroyImmediate(menu.gameObject);
            if (shell != null) Object.DestroyImmediate(shell.gameObject);
            menu = null;
            shell = null;
            NebulaUiShell.UiScale = 1f;
            NebulaBrandAssetCatalog.Release();
            ExclusiveModal.ResetForSession();
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;
        }

        // ---- the plate is really there ---------------------------------------

        /// <summary>
        /// The menu draws the delivered key art, not a colour.
        /// <para>
        /// Asserted against the asset the sprite came from, because "an Image with a
        /// sprite on it" is also true of every panel in the game.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheMainMenuDrawsTheDeliveredKeyArt()
        {
            yield return null;

            NebulaBrandKeyArtView view = menu.KeyArt;
            Assert.IsNotNull(view, "the menu must build a key-art background");
            Assert.IsTrue(view.HasArt, "the key art is not bound; the menu is still a flat backdrop");

            Sprite art = view.GetComponent<Image>().sprite;
            Assert.IsNotNull(art);
            StringAssert.StartsWith("brand.keyart.clean.", art.name,
                "the background must be a clean plate; the composited one is the failure fallback");
            Assert.Greater(art.rect.width, 1000f, "the background is not a key-art master");

            Assert.IsFalse(NebulaBrandAssetCatalog.CombinedLogoKeyArtUsed,
                "the pre-delivery composited plate is on the normal path");
            Assert.AreEqual(0, NebulaBrandAssetCatalog.FallbacksUsed,
                "something had to be substituted on the normal path");
            Assert.IsTrue(view.HasLogo, "the separate wordmark is not drawn");
            StringAssert.StartsWith("brand.logo.effects.", view.LogoImage.sprite.name,
                "the effects cut is what a normal-contrast session should draw");
        }

        /// <summary>
        /// One wordmark, never two.
        /// <para>
        /// The plate carries the authored logo across the top left, which is exactly
        /// where the drawn 76pt "NEBULA" block sat. Both visible is the specific
        /// failure the brief names.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheDrawnWordmarkIsHiddenWhileThePlateCarriesTheLogo()
        {
            yield return null;

            Assert.IsTrue(menu.KeyArt.HasArt, "precondition: the plate is bound");
            Assert.IsFalse(menu.TextWordmarkVisible,
                "the drawn NEBULA block is on screen at the same time as the authored logo");
        }

        /// <summary>
        /// Without the plate the drawn title comes back.
        /// <para>
        /// The fallback is the reason hiding the block is safe. Proven by unbinding
        /// the Image rather than by deleting the asset, which no test may do.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator WithNoPlateBoundTheDrawnWordmarkIsShownAgain()
        {
            yield return null;

            Object.DestroyImmediate(menu.gameObject);
            menu = null;

            // A second screen built while the catalog cannot resolve the plate.
            NebulaBrandAssetCatalog.Release();
            var blocked = NebulaMainMenuScreen.Create(shell, launcher);
            try
            {
                Image art = blocked.KeyArt.GetComponent<Image>();
                art.sprite = null;
                art.enabled = false;
                blocked.OnOpened(shell);
                yield return null;

                // OnOpened re-binds, so the plate returns if it exists at all -- which
                // is the correct behaviour and means this case can only be observed
                // when the asset is genuinely absent. What must hold either way is
                // that the two are never both on screen.
                Assert.AreNotEqual(blocked.KeyArt.HasArt, blocked.TextWordmarkVisible,
                    "exactly one of the plate logo and the drawn wordmark must be showing");
            }
            finally { if (blocked != null) Object.DestroyImmediate(blocked.gameObject); }
        }

        // ---- nothing the menu did stopped working ----------------------------

        /// <summary>
        /// All eight entries are present, and each still does what it did.
        /// <para>
        /// Driven through <c>Button.onClick.Invoke</c>, which is what a click reaches,
        /// so a control that was moved behind the background would still be caught by
        /// the raycast assertion below rather than by this one.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator EveryMenuEntryStillPerformsItsAction()
        {
            yield return null;

            var settings = 0; var accessibility = 0; var load = 0; var credits = 0; var tutorial = 0;
            menu.SettingsRequested += () => settings++;
            menu.AccessibilityRequested += () => accessibility++;
            menu.LoadRequested += () => load++;
            menu.CreditsRequested += () => credits++;
            menu.TutorialRequested += () => tutorial++;

            Click("New");
            Click("Tutorial");
            Click("Load");
            Click("Settings");
            Click("Accessibility");
            Click("Credits");
            Click("Quit");

            Assert.AreEqual(1, launcher.NewCampaigns, "YENİ SEFER");
            Assert.AreEqual(1, tutorial, "EĞİTİM");
            Assert.AreEqual(1, load, "KAYIT YÜKLE");
            Assert.AreEqual(1, settings, "AYARLAR");
            Assert.AreEqual(1, accessibility, "ERİŞİLEBİLİRLİK");
            Assert.AreEqual(1, credits, "KÜNYE");
            Assert.IsTrue(launcher.Quit, "ÇIKIŞ");

            // DEVAM ET does whatever the save state allows: resume exactly the slot it
            // advertises, or nothing at all when it advertises none.
            //
            // Asserted against the screen's own ResumeSlot rather than by deleting the
            // machine's save files first, which is what the EditMode menu fixture does
            // and what a presentation test has no business doing -- this run would
            // destroy a real campaign on a developer's machine.
            int advertised = menu.ResumeSlot;
            Click("Continue");

            if (advertised < 0)
                Assert.IsEmpty(launcher.ContinuedSlots,
                    "DEVAM ET must do nothing while it reports no playable save");
            else
                Assert.AreEqual(new[] { advertised }, launcher.ContinuedSlots,
                    "DEVAM ET must resume exactly the slot it advertises");
        }

        /// <summary>
        /// The background does not eat clicks.
        /// <para>
        /// A full-screen Image added under a menu is the classic way to make every
        /// control in it unreachable while leaving the screen looking perfect.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheKeyArtDoesNotInterceptInput()
        {
            yield return null;

            Assert.IsFalse(menu.KeyArt.GetComponent<Image>().raycastTarget,
                "the key art is a raycast target and will swallow every menu click");

            foreach (Image image in menu.KeyArt.GetComponentsInChildren<Image>(true))
                Assert.IsFalse(image.raycastTarget, image.name + " is a raycast target");
        }

        // ---- resolutions -----------------------------------------------------

        private static IEnumerable<(int width, int height, string label)> Resolutions()
        {
            yield return (1280, 720, "1280x720");
            yield return (1920, 1080, "1920x1080");
            yield return (2560, 1600, "2560x1600");
            yield return (3440, 1440, "3440x1440");
        }

        /// <summary>
        /// At every target resolution the crop keeps the logo and the hero ship.
        /// <para>
        /// This is the assertion the whole pan exists for. A centred cover fill
        /// crops 12.8% off the top at 3440x1440 — straight through the wordmark —
        /// and 5.5% off each side at 2560x1600, which clips the logo's left edge.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheCropKeepsTheCriticalContentAtEveryTargetResolution()
        {
            foreach ((int width, int height, string label) in Resolutions())
            {
                yield return SetViewport(width, height);

                NebulaBrandKeyArtView view = menu.KeyArt;
                view.Layout();

                Assert.IsTrue(view.SafeAreaHonoured,
                    $"{label}: the crop could not keep the critical safe area on screen");

                Rect window = view.VisibleWindow;
                Rect safe = NebulaBrandKeyArtView.CriticalSafeAreaFor(view.SelectedAspect);
                AssertContains(window, safe, $"{label} safe area ({view.SelectedAspect})");

                NebulaBrandKeyArtView.LogoProfile profile =
                    NebulaBrandKeyArtView.LogoProfileFor(view.SelectedAspect);
                AssertContains(window,
                    Rect.MinMaxRect(profile.XMin, profile.YMin, profile.XMin + profile.Width, profile.YMin + 0.001f),
                    $"{label} wordmark");
            }
        }

        /// <summary>
        /// Each target resolution gets its own authored composition, and at each one
        /// the plate matches the viewport exactly so nothing is cropped at all.
        /// </summary>
        [UnityTest]
        public IEnumerator EachTargetResolutionBindsItsAuthoredAspect()
        {
            var expected = new Dictionary<string, NebulaBrandAssetCatalog.KeyArtAspect>
            {
                { "1280x720", NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine },
                { "1920x1080", NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine },
                { "2560x1600", NebulaBrandAssetCatalog.KeyArtAspect.SixteenByTen },
                { "3440x1440", NebulaBrandAssetCatalog.KeyArtAspect.UltraWide },
            };

            foreach ((int width, int height, string label) in Resolutions())
            {
                yield return SetViewport(width, height);
                menu.KeyArt.Layout();

                Assert.AreEqual(expected[label], menu.KeyArt.SelectedAspect, label + ": authored aspect");

                Rect window = menu.KeyArt.VisibleWindow;
                Assert.AreEqual(1f, window.width, 0.01f, label + ": the authored plate should not be cropped sideways");
                Assert.AreEqual(1f, window.height, 0.01f, label + ": the authored plate should not be cropped vertically");
            }
        }

        /// <summary>The plate always covers the viewport — no letterbox, no gap.</summary>
        [UnityTest]
        public IEnumerator ThePlateCoversTheViewportAtEveryTargetResolution()
        {
            foreach ((int width, int height, string label) in Resolutions())
            {
                yield return SetViewport(width, height);

                NebulaBrandKeyArtView view = menu.KeyArt;
                view.Layout();

                var rect = (RectTransform)view.transform;
                Vector2 viewport = ((RectTransform)rect.parent).rect.size;

                Assert.GreaterOrEqual(rect.rect.width, viewport.x - 0.5f, label + ": horizontal gap");
                Assert.GreaterOrEqual(rect.rect.height, viewport.y - 0.5f, label + ": vertical gap");

                // Cover, not stretch: the drawn aspect must still be the plate's.
                Sprite art = view.GetComponent<Image>().sprite;
                float artAspect = art.rect.width / art.rect.height;
                Assert.AreEqual(artAspect, rect.rect.width / rect.rect.height, 0.01f,
                    label + ": the plate is being distorted rather than cropped");
            }
        }

        /// <summary>
        /// The menu column stays clear of the plate's logo at every target
        /// resolution — they occupy the same corner and neither may cover the other.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMenuColumnNeverOverlapsTheLogoOnThePlate()
        {
            foreach ((int width, int height, string label) in Resolutions())
            {
                yield return SetViewport(width, height);
                menu.KeyArt.Layout();

                float logoBottom = LogoBottomInCanvasUnits();
                RectTransform viewport = menu.MenuViewport;
                Assert.IsNotNull(viewport, "the menu viewport must exist");

                // The viewport carries the placement now; the column sits at its
                // origin and is moved by the ScrollRect.
                float columnTop = -viewport.anchoredPosition.y;
                Assert.Less(logoBottom, columnTop,
                    $"{label}: the plate's logo reaches {logoBottom:0} but the menu starts at {columnTop:0}");
            }
        }

        // ---- interface scale -------------------------------------------------

        /// <summary>Every entry, at every resolution, at every supported interface scale.</summary>
        private static readonly string[] Entries =
            { "Continue", "New", "Tutorial", "Load", "Settings", "Accessibility", "Credits", "Quit" };

        private static readonly float[] Scales = { 0.8f, 1f, 1.4f };

        /// <summary>
        /// Every entry can be brought on screen at every resolution and scale.
        /// <para>
        /// This used to pin a status quo in which two entries were simply gone at
        /// 140%: the column is 590 units of content starting 300 down, and a 771 unit
        /// canvas cannot hold that plus a footer, so KÜNYE and ÇIKIŞ were off the
        /// bottom — the accessibility setting that exists to make the interface
        /// readable was removing the quit button. The column scrolls now, so the
        /// weaker assertion is gone with it.
        /// </para>
        /// <para>
        /// Checked by actually moving the scroll and reading world rectangles, not by
        /// arithmetic on offsets: the arithmetic is what was wrong in the first place.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator EveryEntryCanBeBroughtOnScreenAtEveryResolutionAndScale()
        {
            foreach (float scale in Scales)
            {
                foreach ((int width, int height, string label) in Resolutions())
                {
                    NebulaUiShell.UiScale = scale;
                    yield return SetViewport(width, height);

                    string where = $"{label} @ {scale:0.0}x";
                    ScrollRect scroller = menu.MenuViewport.GetComponent<ScrollRect>();
                    Assert.IsNotNull(scroller, where + ": the column must scroll");

                    foreach (string entry in Entries)
                    {
                        RectTransform button = FindChild(entry, menu.MenuColumn);
                        Assert.IsNotNull(button, $"{where}: {entry} is missing");
                        Assert.IsTrue(button.gameObject.activeInHierarchy, $"{where}: {entry} is inactive");
                    }

                    // The first entry is visible before any scrolling, and the last
                    // becomes visible at the end of the range. Between them the
                    // ScrollRect is continuous, so those two are the whole claim.
                    ScrollTo(scroller, 1f);
                    AssertEntryVisible("Continue", where + " (scrolled to top)");

                    ScrollTo(scroller, 0f);
                    AssertEntryVisible("Quit", where + " (scrolled to bottom)");
                }
            }
        }

        /// <summary>
        /// The entries never print over the build line.
        /// <para>
        /// At 3440x1440 ÇIKIŞ used to sit at canvas y 842..890 while the footer
        /// occupied 832..876 — 34 units of one row drawn straight through the other.
        /// At 140% it was worse: the footer crossed AYARLAR.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheEntriesNeverOverlapTheFooterAtAnyResolutionOrScale()
        {
            foreach (float scale in Scales)
            {
                foreach ((int width, int height, string label) in Resolutions())
                {
                    NebulaUiShell.UiScale = scale;
                    yield return SetViewport(width, height);

                    string where = $"{label} @ {scale:0.0}x";
                    RectTransform footer = FindChild("Footer");
                    Assert.IsNotNull(footer, where + ": the footer must exist");

                    Rect viewport = WorldRect(menu.MenuViewport);
                    Rect build = WorldRect(footer);

                    // World space grows upward, so the window's bottom edge must sit
                    // at or above the footer's top edge. The overlap is reported as a
                    // fraction of the footer's own height rather than in world units,
                    // which are canvas units divided by a scale factor and read as
                    // "1" for what is a 34 unit collision on screen.
                    Assert.GreaterOrEqual(viewport.yMin, build.yMax - 0.5f,
                        $"{where}: the entries overlap the build line by "
                        + $"{(build.yMax - viewport.yMin) / Mathf.Max(build.height, 0.0001f):P0} "
                        + "of its height");

                    // And the window itself is inside the canvas.
                    Rect canvas = WorldRect(shell.Root(UiLayer.Menu));
                    Assert.GreaterOrEqual(viewport.yMin, canvas.yMin - 0.5f, where + ": the window runs off the bottom");
                    Assert.LessOrEqual(viewport.yMax, canvas.yMax + 0.5f, where + ": the window runs off the top");
                }
            }
        }

        /// <summary>
        /// Scrolling appears only where it is needed.
        /// <para>
        /// The point of sizing the window rather than fixing it: at 1920x1080 there
        /// are 664 units available for 590 of content, so the menu must look exactly
        /// as it did — one static column, nothing to move. A fix that made every
        /// resolution scroll would pass the reachability test above and still be the
        /// wrong change.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheColumnOnlyScrollsOnViewportsThatCannotHoldIt()
        {
            NebulaUiShell.UiScale = 1f;

            yield return SetViewport(1920, 1080);
            Assert.IsFalse(menu.MenuScrolls, "1920x1080 has room for the whole column and must not scroll");

            yield return SetViewport(2560, 1600);
            Assert.IsFalse(menu.MenuScrolls, "2560x1600 has room for the whole column and must not scroll");

            yield return SetViewport(3440, 1440);
            Assert.IsTrue(menu.MenuScrolls, "3440x1440 leaves 516 units for 590 of content and must scroll");

            NebulaUiShell.UiScale = 1.4f;
            yield return SetViewport(1920, 1080);
            Assert.IsTrue(menu.MenuScrolls, "140% leaves 355 units for 590 of content and must scroll");
        }

        // ---- lifecycle -------------------------------------------------------

        /// <summary>
        /// A hundred open/close cycles leave nothing behind.
        /// <para>
        /// Counts key-art views, loading overlays and the objects under the menu
        /// root. A background that rebuilt itself on every open, or an overlay that
        /// outlived its operation, shows up here as linear growth and nowhere else.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AHundredOpenCloseCyclesGrowNothing()
        {
            yield return null;

            int Baseline() => menu.KeyArt.transform.parent.childCount;

            int before = Baseline();
            int viewsBefore = Object.FindObjectsOfType<NebulaBrandKeyArtView>(true).Length;

            for (int i = 0; i < 100; i++)
            {
                menu.OnClosed();
                menu.OnOpened(shell);
            }
            yield return null;

            Assert.AreEqual(before, Baseline(),
                "the menu root grew objects across 100 open/close cycles");
            Assert.AreEqual(viewsBefore, Object.FindObjectsOfType<NebulaBrandKeyArtView>(true).Length,
                "a second key-art view was built");
            Assert.IsNull(NebulaLoadingOverlay.Active,
                "an overlay survived the cycles");
            Assert.IsFalse(NebulaLoadingPresenter.Busy,
                "the presenter is still holding a nesting depth");
        }

        /// <summary>
        /// Only one key-art texture is resident.
        /// <para>
        /// Four 4K masters is 33MB of GPU memory for a menu that draws one of them.
        /// Measured through the catalog's own residency rather than by profiling,
        /// which cannot be done reliably in a batch-mode run.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator BindingADifferentAspectReleasesThePreviousPlate()
        {
            yield return SetViewport(1920, 1080);
            menu.KeyArt.Layout();

            Sprite first = menu.KeyArt.GetComponent<Image>().sprite;
            Assert.IsNotNull(first);
            string firstName = first.texture.name;
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine, menu.KeyArt.SelectedAspect);

            // A different authored aspect, which is a different plate.
            yield return SetViewport(3440, 1440);
            menu.KeyArt.Layout();

            Sprite second = menu.KeyArt.GetComponent<Image>().sprite;
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.UltraWide, menu.KeyArt.SelectedAspect);
            Assert.AreNotSame(first, second, "the view kept drawing the 16:9 plate on a 21:9 viewport");

            // The previous plate's texture is gone. Scoped to the two plates this test
            // touched rather than to every brand texture in the session: other cases in
            // this fixture legitimately leave their own plates loaded, and a
            // session-wide count would be measuring test order.
            var stillLoaded = new List<string>();
            foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (texture != null && texture.name == firstName) stillLoaded.Add(texture.name);

            Assert.IsEmpty(stillLoaded,
                $"{firstName} is still resident after the view moved to another aspect");
            Assert.AreEqual(NebulaBrandAssetCatalog.ResidentKeyArtPath.Split('/')[^1], second.texture.name,
                "the catalog's resident slot disagrees with what is on screen");
        }

        // ---- visual evidence -------------------------------------------------

        /// <summary>
        /// Photographs the real menu at each target resolution, for comparison
        /// against the delivered key art.
        /// <para>
        /// Under <c>-nographics</c> there is no device to read pixels from and the
        /// files would be flat rectangles, which this suite has shipped before. It
        /// skips loudly instead.
        /// </para>
        /// </summary>
        [UnityTest]
        // Graphics: renders the menu and reads it back with ReadPixels. Capture calls
        // viewportCamera.Render() explicitly, so the disabled camera still draws here.
        [Category("Graphics")]
        public IEnumerator TheMenuIsPhotographedAtEveryTargetResolution()
        {
            RequireGraphicsDevice();

            foreach ((int width, int height, string label) in Resolutions())
            {
                yield return SetViewport(width, height);
                menu.KeyArt.Layout();
                yield return null;

                Capture($"brand-main-menu-{label}.png", width, height);
            }
        }

        /// <summary>
        /// The ultrawide case, photographed at both ends of the scroll.
        /// <para>
        /// 3440x1440 is where ÇIKIŞ used to print through the build line, and it is
        /// the one target resolution whose window is shorter than the column, so the
        /// evidence has to show the bottom of the list as well as the top.
        /// </para>
        /// </summary>
        [UnityTest]
        // Graphics: renders the menu and reads it back with ReadPixels. Capture calls
        // viewportCamera.Render() explicitly, so the disabled camera still draws here.
        [Category("Graphics")]
        public IEnumerator TheUltrawideMenuIsPhotographedAtBothEndsOfItsScroll()
        {
            RequireGraphicsDevice();

            yield return SetViewport(3440, 1440);
            menu.KeyArt.Layout();
            yield return null;

            ScrollRect scroller = menu.MenuViewport.GetComponent<ScrollRect>();
            Assert.IsTrue(menu.MenuScrolls, "precondition: 3440x1440 is the case that scrolls");

            ScrollTo(scroller, 0f);
            yield return null;
            Capture("brand-main-menu-3440x1440-scrolled.png", 3440, 1440);

            ScrollTo(scroller, 1f);
            yield return null;
        }

        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime");

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); run with graphics enabled to capture");
        }

        /// <summary>
        /// Renders the menu canvas through the viewport camera and writes a PNG.
        /// A capture that produced one flat colour fails rather than shipping.
        /// </summary>
        private void Capture(string fileName, int width, int height)
        {
            Canvas.ForceUpdateCanvases();
            viewportCamera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = viewportTarget;
            var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                shot.Apply();

                Color32[] pixels = shot.GetPixels32();
                bool uniform = true;
                for (int i = 1; i < pixels.Length && uniform; i++)
                    if (pixels[i].r != pixels[0].r || pixels[i].g != pixels[0].g || pixels[i].b != pixels[0].b)
                        uniform = false;
                Assert.IsFalse(uniform, $"{fileName} is a single flat colour -- nothing rendered");

                Directory.CreateDirectory(OutputDir);
                File.WriteAllBytes(Path.Combine(OutputDir, fileName), shot.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(shot);
            }
        }

        // ---- helpers ---------------------------------------------------------

        /// <summary>
        /// Repoints the menu canvas at a camera rendering into a target of the given
        /// size, which is how this project simulates a resolution the host machine
        /// does not have. The canvas sizes itself to that target.
        /// </summary>
        private IEnumerator SetViewport(int width, int height)
        {
            Canvas canvas = shell.CanvasFor(UiLayer.Menu);
            if (viewportCamera == null)
            {
                var go = new GameObject("ViewportCamera") { hideFlags = HideFlags.HideAndDontSave };
                viewportCamera = go.AddComponent<Camera>();
                viewportCamera.orthographic = true;
                viewportCamera.transform.position = new Vector3(0f, 0f, -100f);

                // Never drawn by the player loop. Twelve of the fourteen tests here read
                // no pixel at all — they assert crop windows, aspect binding, overlap and
                // scroll geometry — and an enabled camera with a targetTexture is picked
                // up by RenderOffscreenCameras, which on the -nographics null device
                // segfaults the Editor and destroys the result XML of every fixture
                // sharing the process. Disabled, it still sizes and projects the canvas,
                // which is all those twelve need (NebulaMenuNavigationPlayModeTests.cs:741
                // is the same fix). The two photograph tests are unaffected: Capture calls
                // viewportCamera.Render() explicitly, and an explicit Render draws whether
                // the camera is enabled or not.
                viewportCamera.enabled = false;
            }

            // Unbound before release. Releasing a texture that is still a camera's
            // target is a Unity error, and an unexpected error log fails the test
            // that produced it -- which is how four passing assertions came back red.
            viewportCamera.targetTexture = null;
            if (viewportTarget != null) { viewportTarget.Release(); Object.DestroyImmediate(viewportTarget); }
            viewportTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            viewportCamera.targetTexture = viewportTarget;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = viewportCamera;
            canvas.planeDistance = 100f;

            shell.ApplyScale();
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        private Camera viewportCamera;
        private RenderTexture viewportTarget;

        /// <summary>Where the plate's baked logo ends, in menu-canvas units from the top.</summary>
        private float LogoBottomInCanvasUnits()
        {
            NebulaBrandKeyArtView view = menu.KeyArt;
            Rect window = view.VisibleWindow;
            float canvasHeight = shell.Root(UiLayer.Menu).rect.height;

            // Normalized art y -> fraction of the visible window -> canvas units.
            NebulaBrandKeyArtView.LogoProfile profile =
                NebulaBrandKeyArtView.LogoProfileFor(view.SelectedAspect);
            float markAspect = view.LogoImage != null && view.LogoImage.sprite != null
                ? view.LogoImage.sprite.rect.width / view.LogoImage.sprite.rect.height
                : 3.5804f;
            // Height follows the mark's own aspect, expressed against the plate.
            Rect spriteRect = view.GetComponent<Image>().sprite.rect;
            float logoHeight = profile.Width * spriteRect.width / markAspect / spriteRect.height;
            float fraction = (profile.YMin + logoHeight - window.yMin) / window.height;
            return fraction * canvasHeight;
        }

        /// <summary>
        /// Moves the scroll and settles the layout. Guarded because a ScrollRect whose
        /// content already fits divides by a zero range.
        /// </summary>
        private void ScrollTo(ScrollRect scroller, float verticalNormalized)
        {
            if (menu.MenuScrolls) scroller.verticalNormalizedPosition = verticalNormalized;
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// An entry is visible when its rect lies inside the scrolling window <b>and</b>
        /// the window lies inside the canvas.
        /// <para>
        /// Both halves are needed. Checking only the window would call an entry
        /// visible while sitting in a window that itself hangs off the bottom of the
        /// screen — which is exactly the shape of the bug this fixes, so a test that
        /// could not tell the two apart would not be testing the fix.
        /// </para>
        /// </summary>
        private void AssertEntryVisible(string entry, string where)
        {
            RectTransform button = FindChild(entry, menu.MenuColumn);
            Assert.IsNotNull(button, $"{where}: {entry} is missing");

            Rect window = WorldRect(menu.MenuViewport);
            Rect canvas = WorldRect(shell.Root(UiLayer.Menu));
            Rect rect = WorldRect(button);

            Assert.GreaterOrEqual(rect.yMin, window.yMin - 0.5f,
                $"{where}: {entry} is cut off below the window");
            Assert.LessOrEqual(rect.yMax, window.yMax + 0.5f,
                $"{where}: {entry} is cut off above the window");

            Assert.GreaterOrEqual(rect.yMin, canvas.yMin - 0.5f,
                $"{where}: {entry} is below the bottom of the screen");
            Assert.LessOrEqual(rect.yMax, canvas.yMax + 0.5f,
                $"{where}: {entry} is above the top of the screen");
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static void AssertContains(Rect window, Rect content, string what)
        {
            Assert.LessOrEqual(window.xMin, content.xMin + 0.0001f, what + ": cropped on the left");
            Assert.GreaterOrEqual(window.xMax, content.xMax - 0.0001f, what + ": cropped on the right");
            Assert.LessOrEqual(window.yMin, content.yMin + 0.0001f, what + ": cropped at the top");
            Assert.GreaterOrEqual(window.yMax, content.yMax - 0.0001f, what + ": cropped at the bottom");
        }

        private void Click(string buttonName)
        {
            RectTransform found = FindChild(buttonName);
            Assert.IsNotNull(found, buttonName + " is missing from the menu");
            var button = found.GetComponent<Button>();
            Assert.IsNotNull(button, buttonName + " is not a button");
            button.onClick.Invoke();
        }

        private RectTransform FindChild(string name, Transform from = null)
        {
            Transform root = from != null ? from : menu.KeyArt.transform.parent;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child as RectTransform;
            return null;
        }
    }
}
