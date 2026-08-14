using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Captures the interface from a real booted session.
    /// <para>
    /// Every other capture in this project builds widgets standalone in EditMode,
    /// which proves they render but not that a session wires them. This boots the
    /// actual scene and photographs what a player would see — the only evidence
    /// that survives the question "yes, but does it work in the game?".
    /// </para>
    /// <para>
    /// Needs a graphics device. Under <c>-nographics</c> there is nothing to read
    /// pixels from and a capture would be a flat rectangle, so it skips loudly
    /// rather than writing blank files, which this suite has shipped before.
    /// </para>
    /// </summary>
    // Graphics: every test here renders a live camera into a RenderTexture and reads the pixels back. Under -nographics the
    // render is a segfault risk in RenderOffscreenCameras, not merely a blank frame, so this
    // fixture must never be selected into a headless worker. It carries no ReleaseRegression
    // category on purpose -- the headless gate selects by that name and must not reach here.
    [Category("Graphics")]
    public class NebulaLiveSessionCaptureTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime");

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); run ./UIDesign/runtests.sh liveshots");
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

        /// <summary>
        /// A capture that renders nothing must fail rather than ship. Seventeen
        /// EditMode captures were flat grey rectangles and the suite stayed green,
        /// because writing a PNG succeeds whether or not anything was drawn.
        /// </summary>
        private static void Save(Texture2D shot, string fileName)
        {
            Assert.IsNotNull(shot, $"{fileName}: nothing was captured");

            Color32[] pixels = shot.GetPixels32();
            Color32 first = pixels[0];
            bool uniform = true;
            for (int i = 1; i < pixels.Length && uniform; i++)
                if (pixels[i].r != first.r || pixels[i].g != first.g || pixels[i].b != first.b)
                    uniform = false;
            Assert.IsFalse(uniform, $"{fileName} is a single flat colour -- nothing rendered");

            Directory.CreateDirectory(OutputDir);
            File.WriteAllBytes(Path.Combine(OutputDir, fileName), shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
        }

        /// <summary>
        /// Renders the live shell's canvases through a capture camera.
        /// <para>
        /// Not <c>WaitForEndOfFrame</c> + <c>CaptureScreenshotAsTexture</c>: that
        /// coroutine never resumes in batch mode and simply hung all three of these
        /// until the 180s timeout. Driving a camera works with no presented frame,
        /// which is the same reason the EditMode captures do it this way.
        /// </para>
        /// </summary>
        private static IEnumerator Capture(string fileName)
        {
            yield return null;

            const int width = 1920, height = 1080;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("LiveCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();

            var canvases = Object.FindObjectsOfType<Canvas>(true);
            var savedMode = new RenderMode[canvases.Length];
            var savedCamera = new Camera[canvases.Length];
            var savedDistance = new float[canvases.Length];
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Nebula.UI.Design.NebulaUiDesignTokens.SpaceVoid;
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

                Canvas.ForceUpdateCanvases();
                camera.Render();

                RenderTexture.active = target;
                var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                shot.Apply();
                Save(shot, fileName);
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
            }
        }

        /// <summary>
        /// The menu-first launch. It cannot be reached by booting the scene, whose
        /// bootstrap starts a session in Awake, so the host is stood up directly --
        /// which is exactly what DeferToMainMenu does one frame earlier.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMainMenuRendersWithNoSessionAtAll()
        {
            RequireGraphicsDevice();
            yield return LoadSceneFresh();

            var bootstrap = Object.FindObjectOfType<SolarSystemBootstrap>();
            bootstrap.EndSession();
            for (int i = 0; i < 4; i++) yield return null;

            Nebula.UI.Session.NebulaMainMenuHost host =
                Nebula.UI.Session.NebulaMainMenuHost.Create(bootstrap);
            for (int i = 0; i < 6; i++) yield return null;

            Assert.IsTrue(host.MenuVisible, "a menu-first launch shows a menu");
            yield return Capture("live-main-menu-1920x1080.png");

            host.Dismiss();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheBootedSessionRendersItsHud()
        {
            RequireGraphicsDevice();
            yield return LoadSceneFresh();
            for (int i = 0; i < 12; i++) yield return null;

            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            Assert.IsNotNull(context.Presentation.UiShell, "the session must build the shell");

            yield return Capture("live-hud-1920x1080.png");
        }

        [UnityTest]
        public IEnumerator TheBootedSessionRendersItsCommandMenu()
        {
            RequireGraphicsDevice();
            yield return LoadSceneFresh();
            for (int i = 0; i < 12; i++) yield return null;

            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            Assert.IsNotNull(menu, "the session must build the command menu");

            menu.Open(Nebula.UI.Design.NebulaUiAssetCatalog.Page.Overview);
            for (int i = 0; i < 8; i++) yield return null;
            Assert.IsTrue(menu.IsOpen);

            yield return Capture("live-menu-overview-1920x1080.png");

            menu.ShowPage(Nebula.UI.Design.NebulaUiAssetCatalog.Page.Map);
            for (int i = 0; i < 8; i++) yield return null;
            yield return Capture("live-menu-map-1920x1080.png");

            menu.Close();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheStrategicMapStillRendersWhenHandedOver()
        {
            RequireGraphicsDevice();
            yield return LoadSceneFresh();
            for (int i = 0; i < 12; i++) yield return null;

            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            var page = context.Presentation.ParentMenu.PageOf<NebulaMapPage>();
            Assert.IsNotNull(page);

            context.Presentation.ParentMenu.Open(Nebula.UI.Design.NebulaUiAssetCatalog.Page.Map);
            for (int i = 0; i < 6; i++) yield return null;

            Assert.IsTrue(page.OpenMap(), "the handoff must succeed");
            for (int i = 0; i < 20; i++) yield return null;

            Assert.IsTrue(context.Presentation.StrategicMapScreen.IsOpen);
            yield return Capture("live-strategic-map-1920x1080.png");

            context.Presentation.StrategicMapScreen.SetOpen(false);
            yield return null;
        }
    }
}
