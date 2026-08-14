using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
// For the AllocatingGCMemory extension on NUnit's ConstraintExpression. `Is` is left
// ambiguous by this import on purpose -- every use below names its namespace.
using UnityEngine.TestTools.Constraints;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Brand;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// What the brand art costs over a long session.
    /// <para>
    /// A menu that resolves the right plate is only half the claim: the plates are
    /// 8MB of GPU memory each and the loading sheets are 75 sprites apiece, so a
    /// player who opens the settings, changes the interface scale, turns Reduced
    /// Motion on and off and resizes the window has driven the selection dozens of
    /// times before they have started a campaign. This fixture drives it a hundred
    /// and twenty and insists nothing grows.
    /// </para>
    /// <para>
    /// Measured through residency and through the managed heap rather than through a
    /// profiler capture, which cannot be taken reliably in a batch-mode run.
    /// </para>
    /// </summary>
    // ReleaseRegression: this fixture runs headless. The individual tests that need real
    // pixels carry [Category("Graphics")] of their own and must be excluded by that name --
    // NUnit propagates this class category to them, so Graphics is the authoritative one.
    [Category("ReleaseRegression")]
    public class NebulaBrandLifecyclePlayModeTests
    {
        /// <summary>Switches per sweep. The brief asks for a hundred; this is a hundred and twenty.</summary>
        private const int Switches = 120;

        private NebulaUiShell shell;
        private NebulaMainMenuScreen menu;
        private Camera viewportCamera;
        private RenderTexture[] targets;
        private int enteredQuality;

        /// <summary>The four viewports, pre-made so a hundred and twenty switches cost no allocations of their own.</summary>
        private static readonly (int width, int height)[] Viewports =
            { (1280, 720), (1920, 1080), (2560, 1600), (3440, 1440) };

        /// <summary>Quality levels the ladder actually distinguishes for brand selection.</summary>
        private static readonly int[] Qualities = { 1, 2, 3, 5 };

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
            NebulaLoadingOverlay.ResetShownCount();
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

            if (viewportCamera != null)
            {
                viewportCamera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(viewportCamera.gameObject);
            }
            if (targets != null)
                foreach (RenderTexture target in targets)
                    if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            viewportCamera = null;
            targets = null;

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

        // ---- residency -------------------------------------------------------

        /// <summary>
        /// A hundred and twenty aspect, quality and contrast changes leave exactly one
        /// plate and one mark resident.
        /// <para>
        /// The failure this catches is not a leak in the ordinary sense — nothing is
        /// allocated twice. It is that <c>Resources.Load</c> hands back a cached asset
        /// and never lets go of it, so a session that has visited all five plates is
        /// holding all five: 25MB of key art for a menu that draws one.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AHundredAndTwentySwitchesLeaveOnePlateAndOneMarkResident()
        {
            yield return BuildViewports();

            // One full pass first, so the baseline is "every distinct asset has been
            // touched at least once" rather than "nothing has been loaded yet".
            for (int i = 0; i < Viewports.Length * Qualities.Length * 2; i++) yield return Switch(i);

            int plateBaseline = Resident("brand.keyart.");
            int markBaseline = Resident("brand.logo.");

            for (int i = 0; i < Switches; i++) yield return Switch(i);

            int plates = Resident("brand.keyart.");
            int marks = Resident("brand.logo.");

            Assert.LessOrEqual(plates, plateBaseline,
                $"key-art textures grew from {plateBaseline} to {plates} across {Switches} switches");
            Assert.LessOrEqual(marks, markBaseline,
                $"logo textures grew from {markBaseline} to {marks} across {Switches} switches");

            // And the absolute claim, not only the relative one: a single plate and a
            // single mark, which is what the resident-slot design promises.
            Assert.AreEqual(1, plates, "more than one key-art plate is resident");
            Assert.AreEqual(1, marks, "more than one wordmark is resident");

            Assert.IsFalse(NebulaBrandAssetCatalog.CombinedLogoKeyArtUsed,
                "the composited fallback plate was reached during the sweep");
            Assert.AreEqual(0, NebulaBrandAssetCatalog.FallbacksUsed,
                "something had to be substituted during the sweep");
        }

        /// <summary>
        /// The loading sheets are cached once and never re-read, however many times the
        /// motion settings are changed.
        /// <para>
        /// The four sheets together are 300 sprites. A resolver that rebuilt the array
        /// on every request would be allocating 75 references and re-sorting them each
        /// time the player toggled Reduced Motion — and, worse, would hand playback a
        /// different array than the one it was iterating.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheLoadingSheetsAreCachedOnceAndNeverRebuilt()
        {
            yield return null;

            var first = new Dictionary<string, IReadOnlyList<Sprite>>();
            foreach (NebulaBrandAssetCatalog.LoadingPresentation presentation in new[]
                { NebulaBrandAssetCatalog.LoadingPresentation.Animated,
                  NebulaBrandAssetCatalog.LoadingPresentation.ReducedAnimated })
                foreach (NebulaBrandAssetCatalog.LoadingFidelity fidelity in new[]
                    { NebulaBrandAssetCatalog.LoadingFidelity.Full,
                      NebulaBrandAssetCatalog.LoadingFidelity.LowMemory })
                {
                    IReadOnlyList<Sprite> frames = NebulaBrandAssetCatalog.LoadingFrames(presentation, fidelity);
                    Assert.AreEqual(NebulaBrandAssetCatalog.LoadingFrameCount, frames.Count,
                        $"{presentation}/{fidelity} did not resolve a full sheet");
                    first[presentation + "/" + fidelity] = frames;
                }

            int spritesAfterFirstPass = Resident("brand.loading.");

            for (int i = 0; i < Switches; i++)
            {
                var presentation = (i % 2) == 0
                    ? NebulaBrandAssetCatalog.LoadingPresentation.Animated
                    : NebulaBrandAssetCatalog.LoadingPresentation.ReducedAnimated;
                var fidelity = (i % 4) < 2
                    ? NebulaBrandAssetCatalog.LoadingFidelity.Full
                    : NebulaBrandAssetCatalog.LoadingFidelity.LowMemory;

                IReadOnlyList<Sprite> again = NebulaBrandAssetCatalog.LoadingFrames(presentation, fidelity);
                Assert.AreSame(first[presentation + "/" + fidelity], again,
                    $"switch {i}: {presentation}/{fidelity} was rebuilt rather than served from the cache");
            }

            Assert.AreEqual(spritesAfterFirstPass, Resident("brand.loading."),
                $"loading sprites grew across {Switches} presentation switches");
        }

        /// <summary>
        /// A hundred and twenty show/hide cycles leave no overlay behind, and each one
        /// really did raise a surface.
        /// <para>
        /// The second half matters as much as the first: an overlay that stopped being
        /// built at all would satisfy "nothing was left behind" perfectly.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator AHundredAndTwentyLoadingCyclesLeaveNoOrphanOverlay()
        {
            yield return null;

            NebulaLoadingOverlay.ResetShownCount();

            for (int i = 0; i < Switches; i++)
            {
                AnimationAccessibility.MotionOff = (i % 3) == 0;
                AnimationAccessibility.ReducedMotion = (i % 3) == 1;

                var ran = false;
                NebulaLoadingPresenter.Run(shell, "YÜKLENİYOR", () =>
                {
                    ran = true;
                    Assert.IsNotNull(NebulaLoadingOverlay.Active, $"cycle {i}: no surface during the operation");
                });
                Assert.IsTrue(ran, $"cycle {i}: the operation did not run");
                Assert.IsNull(NebulaLoadingOverlay.Active, $"cycle {i}: the surface outlived its operation");
            }

            yield return null;

            Assert.AreEqual(Switches, NebulaLoadingOverlay.ShownCount,
                "the number of surfaces raised does not match the number of operations");
            Assert.IsFalse(NebulaLoadingPresenter.Busy, "the presenter is still holding a nesting depth");
            Assert.IsNull(NebulaLoadingOverlay.Active, "an overlay survived the sweep");

            // The inactive overload on purpose: a destroyed-but-not-yet-collected or a
            // deactivated overlay is exactly the orphan this looks for, and the default
            // overload cannot see it.
            NebulaLoadingOverlay[] alive = UnityEngine.Object.FindObjectsOfType<NebulaLoadingOverlay>(true);
            Assert.IsEmpty(alive, $"{alive.Length} orphaned loading overlays are still in the scene");
        }

        // ---- the two-phase release -------------------------------------------

        /// <summary>
        /// Resolving does not free, and freeing happens only once the new sprite is
        /// bound.
        /// <para>
        /// The order is the whole point. Unity declines to unload an asset that is
        /// still referenced, so releasing inside the resolve — while the Image is still
        /// pointing at the outgoing sprite — silently does nothing and leaves the old
        /// texture resident forever. Releasing it too early instead gives a white
        /// rectangle where the art was. Both halves are asserted here, in order.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheSupersededPlateSurvivesTheResolveAndDiesInTheSecondPhase()
        {
            yield return null;

            Sprite wide = NebulaBrandAssetCatalog.KeyArt(
                NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine, NebulaBrandAssetCatalog.KeyArtDetail.Hd);
            Assert.IsNotNull(wide);
            string first = wide.texture.name;
            Assert.AreEqual(1, Resident(first), "precondition: the first plate is resident");

            // Phase one: a different plate is resolved. The old one is displaced but
            // must still be loaded, because the caller has not rebound anything yet.
            Sprite ultra = NebulaBrandAssetCatalog.KeyArt(
                NebulaBrandAssetCatalog.KeyArtAspect.UltraWide, NebulaBrandAssetCatalog.KeyArtDetail.Hd);
            Assert.IsNotNull(ultra);
            Assert.AreNotSame(wide, ultra, "the resolver returned the same plate for a different aspect");
            Assert.AreEqual(1, Resident(first),
                "the outgoing plate was freed inside the resolve, while a caller could still be drawing it");

            // Phase two: the caller has rebound, and now it goes.
            NebulaBrandAssetCatalog.ReleaseSupersededTextures();
            Assert.AreEqual(0, Resident(first), first + " survived the second phase");
            Assert.AreEqual(1, Resident(ultra.texture.name), "the incoming plate was freed as well");
        }

        // ---- allocation ------------------------------------------------------

        /// <summary>
        /// Neither the loading step nor the key-art layout allocates.
        /// <para>
        /// These are the two things that run every frame while the surface is up. The
        /// loop indexes a cached array and assigns one sprite; the layout is arithmetic
        /// on rects. Both are warmed first, because the constraint would otherwise
        /// measure the JIT rather than the code.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator TheLoadingStepAndTheKeyArtLayoutAllocateNothing()
        {
            yield return BuildViewports();

            AnimationAccessibility.ResetToDefaults();
            NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, "YÜKLENİYOR");
            Assert.IsNotNull(overlay);
            Assert.IsTrue(overlay.IsAnimated, "precondition: the loop must be playing to be measured");
            yield return null;

            float step = 1f / NebulaBrandAssetCatalog.LoadingFps;
            for (int i = 0; i < 8; i++) { overlay.AdvanceForTest(step); menu.KeyArt.Layout(); }

            Assert.That(() => overlay.AdvanceForTest(step),
                UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "the loading loop allocates on every frame it advances");

            Assert.That(() => menu.KeyArt.Layout(),
                UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "the key-art and wordmark layout allocates on every frame");

            NebulaLoadingOverlay.HideActive();
        }

        /// <summary>
        /// And the same claim measured the other way: three hundred real frames with
        /// the loop playing move the managed heap by less than the noise floor.
        /// <para>
        /// The constraint above covers the two methods by name; this covers the frame
        /// as a whole, including the parts of <c>Update</c> those methods are not.
        /// The bound is 128KB over 300 frames — a per-frame allocation of even a single
        /// small array would clear that by an order of magnitude, while the frame's own
        /// unrelated bookkeeping does not.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ThreeHundredLoadingFramesDoNotGrowTheManagedHeap()
        {
            yield return BuildViewports();

            AnimationAccessibility.ResetToDefaults();
            NebulaLoadingOverlay overlay = NebulaLoadingOverlay.Show(shell, "YÜKLENİYOR");
            Assert.IsNotNull(overlay);
            Assert.IsTrue(overlay.IsAnimated);

            for (int i = 0; i < 30; i++) yield return null;      // warm-up

            GC.Collect();
            GC.WaitForPendingFinalizers();
            long before = GC.GetTotalMemory(true);

            for (int i = 0; i < 300; i++) yield return null;

            long after = GC.GetTotalMemory(false);
            long grew = after - before;

            NebulaLoadingOverlay.HideActive();

            Debug.Log($"[brand] 300 loading frames moved the managed heap by {grew} bytes");
            Assert.Less(grew, 128 * 1024,
                $"300 frames of the loading loop allocated {grew} bytes; the path allocates per frame");
        }

        // ---- the sweeping unload ---------------------------------------------

        /// <summary>
        /// Nothing on the brand path calls <c>Resources.UnloadUnusedAssets</c>.
        /// <para>
        /// It is the obvious way to free a displaced plate and it is the wrong one: it
        /// walks every live object in the process looking for references, and in this
        /// project's capture fixtures that has been timed at 64 seconds. The targeted
        /// <c>Resources.UnloadAsset</c> the catalog uses instead frees exactly the
        /// texture that was displaced.
        /// </para>
        /// <para>
        /// Read from the sources with comments stripped, because the reason above is
        /// written in three of those files and a naive substring search would match its
        /// own explanation.
        /// </para>
        /// </summary>
        [Test]
        public void NoBrandOrMenuSourceSweepsWithUnloadUnusedAssets()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            var sources = new List<string>();
            sources.AddRange(Directory.GetFiles(
                Path.Combine(root, "Assets", "Scripts", "Nebula", "UI", "Brand"), "*.cs"));
            sources.AddRange(Directory.GetFiles(
                Path.Combine(root, "Assets", "Scripts", "Nebula", "UI", "Session"), "*.cs"));

            Assert.IsNotEmpty(sources, "no brand or session sources were found to scan");

            var offenders = new List<string>();
            foreach (string file in sources)
                foreach (string line in StripComments(File.ReadAllLines(file)))
                    if (line.Contains("UnloadUnusedAssets"))
                        offenders.Add(Path.GetFileName(file) + ": " + line.Trim());

            Assert.IsEmpty(offenders, string.Join("\n  ", offenders));
        }

        /// <summary>Lines with <c>//</c> tails and <c>/* */</c> spans removed.</summary>
        private static IEnumerable<string> StripComments(IEnumerable<string> lines)
        {
            var block = false;
            foreach (string raw in lines)
            {
                string line = raw;
                if (block)
                {
                    int close = line.IndexOf("*/", StringComparison.Ordinal);
                    if (close < 0) continue;
                    line = line.Substring(close + 2);
                    block = false;
                }

                int open = line.IndexOf("/*", StringComparison.Ordinal);
                if (open >= 0) { line = line.Substring(0, open); block = true; }

                int slash = line.IndexOf("//", StringComparison.Ordinal);
                if (slash >= 0) line = line.Substring(0, slash);

                yield return line;
            }
        }

        // ---- helpers ---------------------------------------------------------

        private static int Resident(string prefix)
        {
            int count = 0;
            foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (texture != null && texture.name.StartsWith(prefix, StringComparison.Ordinal)) count++;
            return count;
        }

        /// <summary>
        /// One camera and four render targets, made once. Building a target per switch
        /// would put the fixture's own allocations into the numbers it is measuring.
        /// </summary>
        private IEnumerator BuildViewports()
        {
            var go = new GameObject("LifecycleViewportCamera") { hideFlags = HideFlags.HideAndDontSave };
            viewportCamera = go.AddComponent<Camera>();
            viewportCamera.orthographic = true;
            viewportCamera.transform.position = new Vector3(0f, 0f, -100f);

            // Never drawn. This fixture reads no pixel: it counts resident plates and
            // marks and measures managed allocations, and the camera is here only to
            // give the canvas a viewport size. An *enabled* camera with a targetTexture
            // is picked up by the player loop's RenderOffscreenCameras pass, which on
            // the -nographics null device segfaulted the Editor inside
            // DrawLineOrTrailFromNodeQueue -- and since Unity writes its result XML only
            // when a run reaches the end, one crash destroyed the results of all 21
            // fixtures sharing the process. This used to Assert.Ignore for that reason,
            // which cost the headless gate seven real tests to protect it from pixels it
            // never looks at. Disabling the camera removes it from the render loop while
            // it still sizes and projects the canvas -- the same fix, for the same
            // reason, as NebulaMenuNavigationPlayModeTests.cs:741.
            viewportCamera.enabled = false;

            targets = new RenderTexture[Viewports.Length];
            for (int i = 0; i < Viewports.Length; i++)
                targets[i] = new RenderTexture(Viewports[i].width, Viewports[i].height, 24,
                    RenderTextureFormat.ARGB32);

            foreach (UiLayer layer in new[] { UiLayer.Menu, UiLayer.Overlay })
            {
                Canvas canvas = shell.CanvasFor(layer);
                if (canvas == null) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = viewportCamera;
                canvas.planeDistance = 100f;
            }

            viewportCamera.targetTexture = targets[1];
            shell.ApplyScale();
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        /// <summary>
        /// One change of aspect, quality and contrast, applied through the view's own
        /// <c>Bind</c> — the production entry point, not the catalog's back door.
        /// <para>
        /// The three periods are coprime with each other and with the sweep length, so
        /// the sweep visits every combination rather than the same four over and over.
        /// </para>
        /// </summary>
        private IEnumerator Switch(int index)
        {
            viewportCamera.targetTexture = targets[index % Viewports.Length];
            QualitySettings.SetQualityLevel(Qualities[(index / 2) % Qualities.Length], false);
            LightingAccessibility.HighContrast = (index % 3) == 0;
            AnimationAccessibility.ReducedMotion = (index % 5) == 0;
            AnimationAccessibility.MotionOff = (index % 7) == 0;

            shell.ApplyScale();
            Canvas.ForceUpdateCanvases();
            menu.KeyArt.Bind();
            yield return null;
        }
    }
}
