using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Captures the redesigned interface to PNG so it can be held against the
    /// approved mockups in <c>UIDesign/Renders/</c>.
    /// <para>
    /// A screen-space-overlay canvas has nothing to render into outside a running
    /// player, so each capture temporarily repoints the canvas at a dedicated
    /// camera drawing into a RenderTexture, then restores it — the same approach
    /// the Strategic Map's own screenshot test already uses in this project.
    /// </para>
    /// <para>
    /// Every widget here is fed representative values directly rather than through
    /// a live session: the point is to prove the interface renders and lays out at
    /// each resolution, not to re-test gameplay.
    /// </para>
    /// </summary>
    public class NebulaUiScreenshotTests
    {
        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime");

        private NebulaUiShell shell;
        private float enteredTimeScale;

        /// <summary>
        /// Under <c>-nographics</c> there is no device to render with, so the only
        /// honest outcomes are "skipped" or "blank". Saying so out loud beats both
        /// the old silent grey rectangles and a red suite in a run that was never
        /// able to capture anything.
        /// </summary>
        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); run ./UIDesign/runtests.sh shots");
        }

        [SetUp]
        public void Setup()
        {
            enteredTimeScale = Time.timeScale;
            ExclusiveModal.ResetForSession();
            Directory.CreateDirectory(OutputDir);
            shell = NebulaUiShell.Create();
        }

        [TearDown]
        public void Teardown()
        {
            if (shell != null)
            {
                shell.Shutdown();
                Object.DestroyImmediate(shell.gameObject);
            }
            ExclusiveModal.ResetForSession();
            Time.timeScale = enteredTimeScale > 0f ? enteredTimeScale : 1f;
        }

        // ---- the captures ----------------------------------------------------

        [Test]
        public void Capture_ParentMenu_AtEverySupportedResolution()
        {
            RequireGraphicsDevice();
            NebulaParentMenuScreen menu = BuildMenu();
            menu.Open(NebulaUiAssetCatalog.Page.Overview);

            foreach ((int width, int height, string label) in Resolutions())
                CaptureMenuLayer($"parent-menu-{label}.png", width, height);
        }

        /// <summary>
        /// The UI scale, at both ends of its range and in the middle, on the two
        /// surfaces where it matters most: the parent menu and the settings page,
        /// which is the densest screen in the game and the one a player who needed
        /// a larger interface would be reading when they went looking for the
        /// setting.
        /// <para>
        /// The scale is asserted at the product before anything is photographed.
        /// The 80% and 100% captures came out byte-identical while 140% rendered
        /// correctly, and a pair of identical PNGs cannot say whether the setting
        /// failed to reach the canvas or the capture failed to keep it — so the
        /// canvas is read back immediately after the setter, which is the product's
        /// own promise ("applied immediately rather than waiting for the next
        /// LateUpdate"), and then again inside the capture right before the render.
        /// </para>
        /// </summary>
        [Test]
        public void Capture_ParentMenu_AtEveryUiScale()
        {
            RequireGraphicsDevice();
            NebulaParentMenuScreen menu = BuildMenu();
            menu.Open(NebulaUiAssetCatalog.Page.Overview);

            foreach (float scale in new[] { 0.8f, 1.0f, 1.4f })
            {
                NebulaUiShell.UiScale = scale;
                AssertScaleReachedTheCanvas(scale);
                CaptureMenuLayer($"parent-menu-scale-{scale * 100f:0}.png", 1920, 1080, scale);
            }

            menu.ShowPage(NebulaUiAssetCatalog.Page.Settings);
            foreach (float scale in new[] { 0.8f, 1.0f, 1.4f })
            {
                NebulaUiShell.UiScale = scale;
                AssertScaleReachedTheCanvas(scale);
                CaptureMenuLayer($"page-settings-scale-{scale * 100f:0}.png", 1920, 1080, scale);
            }

            NebulaUiShell.UiScale = 1f;
        }

        /// <summary>
        /// The two ways the UI scale used to get lost, as a test rather than as a
        /// comment — because both of them are things a player meets and neither
        /// was visible in a screenshot.
        /// <para>
        /// First: <c>UiScale</c> is static and its setter pushed the value out by
        /// walking the list of live shells, which reaches every shell that exists
        /// when it is set and none built afterwards. So a player who chose 80% and
        /// then did anything that stands a fresh shell up — returning to the main
        /// menu, starting a campaign, restarting one — got a 100% interface back.
        /// </para>
        /// <para>
        /// Second: the value was written onto <c>canvas.scaleFactor</c>, which the
        /// <c>CanvasScaler</c> owns and recomputes on every canvas update, so even
        /// a shell that did receive the push lost it at the next rebuild. Both are
        /// asserted here, in that order, because fixing either one alone still
        /// leaves an interface that comes back the wrong size.
        /// </para>
        /// </summary>
        [Test]
        public void AShellBuiltAfterTheScaleWasChosenAdoptsItAndKeepsIt()
        {
            float saved = NebulaUiShell.UiScale;
            try
            {
                // What the interface draws at with the setting untouched, on
                // whatever screen this run happens to have. The interface scale is
                // a multiplier on this, not a replacement for it — asserting an
                // absolute scaleFactor would pass only at exactly 1920x1080 and
                // fail in batchmode, where the screen is 640x480 and the natural
                // factor is 0.385.
                NebulaUiShell.UiScale = 1f;
                NebulaUiShell reference = NebulaUiShell.Create();
                float natural;
                try
                {
                    Canvas.ForceUpdateCanvases();
                    natural = reference.CanvasFor(UiLayer.Menu).scaleFactor;
                }
                finally
                {
                    reference.Shutdown();
                    Object.DestroyImmediate(reference.gameObject);
                }
                Assert.Greater(natural, 0f, "the reference shell drew at no scale at all");

                NebulaUiShell.UiScale = 0.8f;
                NebulaUiShell late = NebulaUiShell.Create();
                try
                {
                    Canvas menu = late.CanvasFor(UiLayer.Menu);
                    Canvas.ForceUpdateCanvases();
                    Assert.AreEqual(0.8f * natural, menu.scaleFactor, 0.001f,
                        "a shell built while the interface scale was 80% is drawing at "
                        + $"{menu.scaleFactor / natural:0.00} of natural size. Either the scale "
                        + "never reached a shell built after the player chose it, or a canvas "
                        + "rebuild put it back — both of which it used to do.");

                    // And again, because the rebuild is the part that used to undo it.
                    Canvas.ForceUpdateCanvases();
                    Canvas.ForceUpdateCanvases();
                    Assert.AreEqual(0.8f * natural, menu.scaleFactor, 0.001f,
                        "further canvas rebuilds moved the interface scale.");
                }
                finally
                {
                    late.Shutdown();
                    Object.DestroyImmediate(late.gameObject);
                }
            }
            finally { NebulaUiShell.UiScale = saved; }
        }

        /// <summary>
        /// The whole range the player can actually choose, not only the three
        /// points the captures happen to photograph.
        /// <para>
        /// Three sampled values would have passed while everything between them
        /// was broken, and the settings row drives this continuously in 0.1 steps
        /// from <see cref="NebulaUiShell.MinUiScale"/> to
        /// <see cref="NebulaUiShell.MaxUiScale"/>. Monotonic and proportional
        /// across the sweep, and clamped outside it.
        /// </para>
        /// </summary>
        [Test]
        public void EveryUiScaleAcrossTheWholeRangeReachesTheCanvasProportionally()
        {
            float saved = NebulaUiShell.UiScale;
            try
            {
                NebulaUiShell.UiScale = 1f;
                Canvas.ForceUpdateCanvases();
                float natural = shell.CanvasFor(UiLayer.Menu).scaleFactor;
                Assert.Greater(natural, 0f);

                float previous = 0f;
                for (float scale = NebulaUiShell.MinUiScale;
                     scale <= NebulaUiShell.MaxUiScale + 0.001f;
                     scale += 0.1f)
                {
                    NebulaUiShell.UiScale = scale;
                    Canvas.ForceUpdateCanvases();
                    float actual = shell.CanvasFor(UiLayer.Menu).scaleFactor;

                    Assert.AreEqual(scale * natural, actual, 0.001f,
                        $"at {scale * 100f:0}% the interface is drawing at "
                        + $"{actual / natural:0.000} of natural size.");
                    Assert.Greater(actual, previous,
                        $"{scale * 100f:0}% is not larger than the step below it.");
                    previous = actual;
                }

                // Outside the range the setter clamps, and the canvas has to follow
                // the clamped value rather than the asked-for one.
                foreach ((float asked, float clamped) in new[]
                         {
                             (0.1f, NebulaUiShell.MinUiScale), (9f, NebulaUiShell.MaxUiScale),
                         })
                {
                    NebulaUiShell.UiScale = asked;
                    Canvas.ForceUpdateCanvases();
                    Assert.AreEqual(clamped, NebulaUiShell.UiScale, 0.0001f);
                    Assert.AreEqual(clamped * natural,
                        shell.CanvasFor(UiLayer.Menu).scaleFactor, 0.001f,
                        $"asking for {asked:0.0} clamped the setting but not the canvas.");
                }
            }
            finally { NebulaUiShell.UiScale = saved; }
        }

        /// <summary>
        /// The product half of the claim: setting the scale must reach the thing
        /// that decides how big the interface is drawn, with no frame in between.
        /// <para>
        /// Asserted on the scaler's reference resolution rather than on
        /// <c>canvas.scaleFactor</c>, because that is the mechanism: under
        /// ScaleWithScreenSize the scaler owns scaleFactor and recomputes it from
        /// this on every rebuild, so this is the value that survives and the
        /// other one is the value that used to get overwritten.
        /// </para>
        /// </summary>
        private void AssertScaleReachedTheCanvas(float expected)
        {
            var scaler = shell.CanvasFor(UiLayer.Menu).GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler, "the menu canvas has no CanvasScaler to scale with");

            Vector2 wanted = NebulaUiShell.ReferenceResolution / expected;
            Assert.AreEqual(wanted.x, scaler.referenceResolution.x, 0.5f,
                $"UiScale was set to {expected:0.00} and the menu scaler is still working to "
                + $"a {scaler.referenceResolution.x:0}x{scaler.referenceResolution.y:0} "
                + "reference. A player who needs a smaller or larger interface gets the size "
                + "they already had.");
        }

        [Test]
        public void Capture_FleetAndResourcesPages()
        {
            RequireGraphicsDevice();
            NebulaParentMenuScreen menu = BuildMenu();

            menu.ShowPage(NebulaUiAssetCatalog.Page.Fleet);
            menu.Open(NebulaUiAssetCatalog.Page.Fleet);
            CaptureMenuLayer("fleet-1920x1080.png", 1920, 1080);

            menu.ShowPage(NebulaUiAssetCatalog.Page.Resources);
            CaptureMenuLayer("resources-1920x1080.png", 1920, 1080);

            menu.ShowPage(NebulaUiAssetCatalog.Page.Progression);
            CaptureMenuLayer("progression-1920x1080.png", 1920, 1080);

            menu.ShowPage(NebulaUiAssetCatalog.Page.Planets);
            CaptureMenuLayer("planets-1920x1080.png", 1920, 1080);

            menu.ShowPage(NebulaUiAssetCatalog.Page.Combat);
            CaptureMenuLayer("combat-1920x1080.png", 1920, 1080);

            menu.ShowPage(NebulaUiAssetCatalog.Page.Discovery);
            CaptureMenuLayer("discovery-1920x1080.png", 1920, 1080);

            menu.ShowPage(NebulaUiAssetCatalog.Page.Map);
            CaptureMenuLayer("map-1920x1080.png", 1920, 1080);
        }

        /// <summary>
        /// The whole matrix: every page the rail can reach, at every resolution the
        /// game supports.
        /// <para>
        /// Until now only the parent menu was swept and each page was photographed
        /// once, at 1920×1080 — which is the one resolution a layout bug is least
        /// likely to show up in, because it is the one everything was authored
        /// against. A two-column settings page that collapses at 1280×720, or a rail
        /// that leaves a gutter at 3440×1440, is invisible in a single-resolution
        /// set. The canvas here really does resize: in ScreenSpaceCamera mode it
        /// takes its dimensions from the capture camera's target, so the scaler runs
        /// for each one instead of the same layout being resampled.
        /// </para>
        /// </summary>
        [Test]
        public void Capture_EveryPage_AtEveryResolution()
        {
            RequireGraphicsDevice();
            NebulaParentMenuScreen menu = BuildMenu();
            menu.Open(NebulaUiAssetCatalog.Page.Overview);

            foreach ((int width, int height, string label) in Resolutions())
            {
                foreach (NebulaUiAssetCatalog.Page page in
                         (NebulaUiAssetCatalog.Page[])System.Enum.GetValues(typeof(NebulaUiAssetCatalog.Page)))
                {
                    menu.ShowPage(page);
                    CaptureMenuLayer($"page-{page.ToString().ToLowerInvariant()}-{label}.png", width, height);
                }
            }
        }

        /// <summary>
        /// The front door and the two screens behind it.
        /// <para>
        /// The capture matrix used to start at the parent menu, which is the second
        /// thing a player sees. AYARLAR, KAYIT YÜKLE and KÜNYE now open real
        /// screens, and a screen nobody photographs is a screen nobody reviews —
        /// the save-slot list in particular carries a per-slot refusal reason,
        /// including one for a save written by a newer build, and a refusal that is
        /// never looked at is the classic place for an untranslated or empty
        /// string to ship.
        /// </para>
        /// </summary>
        [Test]
        public void Capture_MainMenuAndTheScreensItOpens()
        {
            RequireGraphicsDevice();

            var launcher = new SilentLauncher();

            NebulaMainMenuScreen menu = NebulaMainMenuScreen.Create(shell, launcher);
            menu.OnOpened(shell);
            CaptureMenuLayer("main-menu-1920x1080.png", 1920, 1080);
            CaptureMenuLayer("main-menu-1280x720.png", 1280, 720);
            menu.OnClosed();

            NebulaSaveSlotScreen slots = NebulaSaveSlotScreen.Create(shell, launcher);
            slots.OnOpened(shell);
            CaptureMenuLayer("save-slots-1920x1080.png", 1920, 1080);
            CaptureMenuLayer("save-slots-1280x720.png", 1280, 720);
            slots.OnClosed();

            NebulaCreditsScreen credits = NebulaCreditsScreen.Create(shell);
            credits.OnOpened(shell);
            CaptureMenuLayer("credits-1920x1080.png", 1920, 1080);
            credits.OnClosed();
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

        /// <summary>
        /// The two second-level surfaces that live inside a page rather than as
        /// screens of their own: the key-rebinding panel inside Settings and the
        /// System Logistics Centre inside Fleet. Both are opened through their own
        /// control, so a capture that appears proves the opener is wired.
        /// </summary>
        [Test]
        public void Capture_TheTwoPanelsThatOpenInsideAPage()
        {
            RequireGraphicsDevice();
            NebulaParentMenuScreen menu = BuildMenu();

            menu.Open(NebulaUiAssetCatalog.Page.Settings);
            NebulaSettingsPage settings = menu.PageOf<NebulaSettingsPage>();
            Assert.IsNotNull(settings, "the settings page is not on the rail");
            settings.OpenBindingPanel();
            Assert.IsTrue(settings.BindingPanelOpen,
                "the rebinding panel did not open, so there is nothing to photograph");
            CaptureMenuLayer("settings-bindings-1920x1080.png", 1920, 1080);
            CaptureMenuLayer("settings-bindings-1280x720.png", 1280, 720);
            settings.CloseBindingPanel();

            menu.ShowPage(NebulaUiAssetCatalog.Page.Fleet);
            NebulaFleetPage fleet = menu.PageOf<NebulaFleetPage>();
            Assert.IsNotNull(fleet, "the fleet page is not on the rail");
            fleet.OpenLogistics();
            Assert.IsTrue(fleet.LogisticsPanelOpen,
                "the Logistics Centre panel did not open, so there is nothing to photograph");
            CaptureMenuLayer("fleet-logistics-1920x1080.png", 1920, 1080);
            CaptureMenuLayer("fleet-logistics-1280x720.png", 1280, 720);
        }

        [Test]
        public void Capture_FlightHud()
        {
            RequireGraphicsDevice();
            NebulaHudNavigation.Create(shell, null, null);
            NebulaHudAlerts.Create(shell, null, null);
            CaptureLayer(UiLayer.HudDynamic, "hud-flight.png", 1920, 1080);
            CaptureLayer(UiLayer.HudStatic, "hud-site.png", 1920, 1080);
        }

        [Test]
        public void Capture_SettingsPage()
        {
            RequireGraphicsDevice();
            NebulaParentMenuScreen menu = BuildMenu();
            menu.Open(NebulaUiAssetCatalog.Page.Settings);
            CaptureMenuLayer("settings-1920x1080.png", 1920, 1080);

            menu.ShowPage(NebulaUiAssetCatalog.Page.Session);
            CaptureMenuLayer("session-1920x1080.png", 1920, 1080);
        }

        [Test]
        public void Capture_HudContextBar_InEveryActionState()
        {
            RequireGraphicsDevice();
            NebulaHudContextBar bar = NebulaHudContextBar.Create(shell);

            var secondaries = new List<HudAction>
            {
                new HudAction("YÖRÜNGE SABİTLE", KeyCode.B),
                new HudAction("AKTAR", KeyCode.T),
            };

            bar.Show("VEGA-7 / II   ·   METAL   ·   RARE   ·   SAKİN",
                new HudAction("SONDAJ T2'YE YÜKSELT", KeyCode.F, "30 PARÇA + 40 METAL ALAŞIM"),
                secondaries);
            CaptureLayer(UiLayer.HudStatic, "hud-context-available.png", 1920, 1080);

            bar.Show("VEGA-7 / II   ·   METAL   ·   RARE   ·   SAKİN",
                new HudAction("SONDAJ T2'YE YÜKSELT", KeyCode.F, "30 PARÇA + 40 METAL ALAŞIM",
                    available: false, blockedReason: "İSTASYON İNŞASI T2 GEREKLİ"),
                secondaries);
            CaptureLayer(UiLayer.HudStatic, "hud-context-blocked.png", 1920, 1080);

            bar.Show("VEGA-7 / II   ·   YÜKSEKLİK 320   ·   TARANMADI",
                new HudAction("TARA", KeyCode.R, "%48"));
            CaptureLayer(UiLayer.HudStatic, "hud-context-scan.png", 1920, 1080);
        }

        // ---- machinery -------------------------------------------------------

        private static IEnumerable<(int, int, string)> Resolutions()
        {
            yield return (1280, 720, "1280x720");
            yield return (1920, 1080, "1920x1080");
            yield return (2560, 1440, "2560x1440");
            yield return (3440, 1440, "3440x1440-ultrawide");
            yield return (1920, 1200, "1920x1200-16x10");
        }

        private NebulaParentMenuScreen BuildMenu()
        {
            NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
            // Pages tolerate a null context on purpose, so the shell can be captured
            // and reasoned about without standing up a universe first.
            // Same set and same order PresentationInstaller registers. The captures
            // are the evidence for this work, so a rail here that does not match
            // the real one is evidence of something that does not exist -- Harita
            // was missing from every screenshot taken before this.
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

        private void CaptureMenuLayer(string fileName, int width, int height,
            float? expectedScale = null) =>
            CaptureLayer(UiLayer.Menu, fileName, width, height, expectedScale);

        /// <summary>
        /// Renders one layer to a PNG. Restores the canvas afterwards, because a
        /// canvas left in ScreenSpaceCamera mode would silently break every later
        /// test in the run.
        /// </summary>
        /// <summary>
        /// A capture that renders nothing must fail, not ship.
        /// <para>
        /// Seventeen of these files were byte-identical flat grey rectangles and
        /// every test still passed, because writing a PNG succeeds whether or not
        /// anything was drawn into it. Under <c>-nographics</c> there is no
        /// graphics device, <c>Camera.Render</c> draws nothing, and
        /// <c>ReadPixels</c> returns that grey. A screenshot suite that is green
        /// while producing blank evidence is worse than having none.
        /// </para>
        /// </summary>
        private static void AssertNotBlank(Texture2D texture, string fileName)
        {
            Color32[] pixels = texture.GetPixels32();
            Color32 first = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (p.r != first.r || p.g != first.g || p.b != first.b) return;
            }

            Assert.Fail($"{fileName} is a single flat colour -- nothing rendered. " +
                "Run the capture with graphics enabled: ./UIDesign/runtests.sh shots");
        }

        private void CaptureLayer(UiLayer layer, string fileName, int width, int height,
            float? expectedScale = null)
        {
            Canvas canvas = shell.CanvasFor(layer);
            RenderMode savedMode = canvas.renderMode;
            Camera savedCamera = canvas.worldCamera;
            float savedPlaneDistance = canvas.planeDistance;
            RenderTexture previousActive = RenderTexture.active;

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("UiCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();

            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                // The design assumes a dark backdrop; clearing to the void colour is
                // what the interface will really sit on, so contrast reads honestly.
                camera.backgroundColor = NebulaUiDesignTokens.SpaceVoid;
                camera.orthographic = true;
                camera.targetTexture = target;
                camera.transform.position = new Vector3(0f, 0f, -100f);

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 100f;

                // Repointing a canvas at a camera re-runs its CanvasScaler, and the
                // scaler owns `scaleFactor` — so the capture harness itself was
                // overwriting the player's UI scale between the setter and the
                // render. Re-applied here, and then asserted, so the frame that is
                // written is the frame the setting asked for rather than whatever
                // the scaler last computed.
                shell.ApplyScale();
                Canvas.ForceUpdateCanvases();

                if (expectedScale.HasValue)
                    Assert.AreEqual(expectedScale.Value, canvas.scaleFactor, 0.0001f,
                        $"{fileName} was about to be rendered at scale "
                        + $"{canvas.scaleFactor:0.00} instead of {expectedScale.Value:0.00}.");

                camera.Render();

                RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                AssertNotBlank(texture, fileName);
                File.WriteAllBytes(Path.Combine(OutputDir, fileName), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            finally
            {
                RenderTexture.active = previousActive;
                canvas.renderMode = savedMode;
                canvas.worldCamera = savedCamera;
                canvas.planeDistance = savedPlaneDistance;
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
            }

            var written = new FileInfo(Path.Combine(OutputDir, fileName));
            Assert.IsTrue(written.Exists && written.Length > 0,
                $"{fileName} was not written — the interface produced no pixels.");
        }
    }
}
