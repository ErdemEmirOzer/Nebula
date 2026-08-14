using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Sectors;
using Nebula.UI.StrategicMap;
using Nebula.UI.StrategicMap.Preview;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Captures real PNGs of the strategic map so it can be looked at rather than
    /// argued about.
    ///
    /// The map's canvas is ScreenSpaceOverlay, which a batchmode run cannot capture
    /// (ScreenCapture needs a presented backbuffer, and WaitForEndOfFrame never
    /// resumes without a real render loop). So the canvas is temporarily re-pointed
    /// at a dedicated capture camera rendering into a RenderTexture, and restored to
    /// overlay immediately afterwards — the map itself is never modified.
    /// </summary>
    // Graphics: every test here renders the map through a capture camera and reads the pixels back. Under -nographics the
    // render is a segfault risk in RenderOffscreenCameras, not merely a blank frame, so this
    // fixture must never be selected into a headless worker. It carries no ReleaseRegression
    // category on purpose -- the headless gate selects by that name and must not reach here.
    [Category("Graphics")]
    public class StrategicMapScreenshotTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const string OutputDir = "/tmp/nebula-map-shots";

        // The capture resolution is driven by the test, not by Screen.
        //
        // -screen-width and -screen-height do not reach Screen.width/height in a batchmode
        // PlayMode run: it reports 640x480 whatever they say. Every capture was coming out at
        // 640x480, and worse, the map's own render target was sized from Screen too -- so the
        // reference's pixel sizes, which are all against a 1080-tall frame, were being applied
        // to a 480-tall one. Everything was more than twice as large on screen as it should be.
        private static int Width = 1920;
        private static int Height = 1080;

        /// <summary>Captures are filed under their resolution so two runs do not overwrite each other.</summary>
        private static string ResolutionDir => Path.Combine(OutputDir, Width + "x" + Height);

        private static bool HasGpu => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        private static IEnumerator LoadSceneFresh()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;

            // The scene boots to the main menu in production. A capture test wants the
            // world, so it asks for it -- through the same idempotent entry the menu\'s
            // New Campaign button calls.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureTheMapAtEveryLevel([Values(1080, 1440)] int captureHeight)
        {
            if (!HasGpu) { Assert.Ignore("no graphics device -- run without -nographics to capture"); yield break; }

            yield return LoadSceneFresh();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            var map = Object.FindObjectOfType<StrategicMapScreen>();
            Height = captureHeight;
            Width = Mathf.RoundToInt(captureHeight * 16f / 9f);
            Directory.CreateDirectory(ResolutionDir);

            // --- a genuinely explored galaxy, so the map has something real to show ---
            //
            // THIS LOOP USED TO NEVER END. It was
            //     for (int sx = 0; count < 22; sx++)
            //         ... Manifests.Resolve(new SectorCoordinate(sx, 0, 0))
            // — walk the x axis until 22 systems have been collected. Since the world's
            // systems became the stars GalaxyStarField strings along the galaxies' arms, a
            // sector no arm passes through holds nothing, and the line (sx, 0, 0) leaves
            // every arm within a dozen sectors. `count` stopped climbing at a handful, `sx`
            // climbed forever, and each turn resolved AND CACHED another empty SectorManifest:
            // the run pegged a core, grew to 16 GB, stopped logging and wrote no PNG at all.
            // SectorManifestGenerator's own remarks record the same shape of bug costing a
            // full EditMode suite.
            //
            // The walk is now over the canonical star field itself — every sector that
            // genuinely holds a star, each one once — so it is bounded by the world, and it
            // says so out loud if the world cannot supply the systems this capture needs.
            SystemKnowledgeService knowledge = context.NavigationDirector.Knowledge;
            const int WantedSystems = 22;
            int count = 0;
            foreach (SectorCoordinate sector in StrategicMapUsableOnOpenTests.SectorsOfTheStarField())
            {
                if (count >= WantedSystems) break;
                foreach (SystemManifest s in StarSystemRegistry.Manifests.Resolve(sector).Systems)
                {
                    if (count >= WantedSystems) break;
                    SystemKnowledgeLevel level = count < 9 ? SystemKnowledgeLevel.Visited
                                               : count < 16 ? SystemKnowledgeLevel.Located
                                               : SystemKnowledgeLevel.Signal;
                    knowledge.ForceLevel(s.SystemId, level);
                    count++;
                }
            }
            Assert.AreEqual(WantedSystems, count,
                $"the canonical star field yielded only {count} systems, so this capture would "
                + "show a map with almost nothing on it. The field is the world's own star list "
                + "-- if it is this small, the galaxies are misconfigured, not the capture.");

            foreach (PlanetManifest pm in context.StartSystem.Planets)
                ExtractionState.GetProfile(pm.PlanetId).Scanned = true;

            PlanetManifest first = context.StartSystem.Planets[0];
            PlanetIdentity planet = ExtractionState.FindPlanet(first.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            ExtractionState.GetDominance(first.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);

            var drill = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, 120f, 0f);
            DrillStructure.Restore(planet, profile, drill, DrillStructure.BuildSource.Kit, 0);
            SurfaceStructureDef commandDef = SurfaceStructureCatalog.Command;
            var dur = new Durability(commandDef.ShieldMax, commandDef.ArmorMax, commandDef.HullMax, 1f);
            SurfaceStructure.Restore(planet, profile, commandDef, profile.CommandNode,
                new StructureRuntime(commandDef, DrillLifecycle.Active, 0f, 0f, dur, 1), null, null);
            SurfaceStructureDef laserDef = SurfaceStructureCatalog.Laser;
            var ldur = new Durability(laserDef.ShieldMax, laserDef.ArmorMax, laserDef.HullMax, 1f);
            SurfaceStructure.Restore(planet, profile, laserDef, profile.FirstDefenseNode,
                new StructureRuntime(laserDef, DrillLifecycle.Active, 0f, 0f, ldur, 1), null, null);

            if (context.StartSystem.Planets.Count > 1)
                ExtractionState.GetDominance(context.StartSystem.Planets[1].PlanetId).Restore(DominanceState.Contested, 0f, 0f);
            yield return null;

            map.SetOpen(true);
            yield return null;
            // The screen sizes the map's target from Screen, which batchmode pins at 640x480.
            // Force the resolution being captured, or the whole map renders into a 480-tall
            // frame while every pixel size in it is quoted against a 1080-tall one.
            var sized = Object.FindObjectOfType<StrategicMapUniverseRenderer>();
            if (sized != null) sized.EnsureTarget(Width, Height);
            map.Refresh();
            yield return null;

            var cloud = Object.FindObjectOfType<StrategicMapGalaxyCloud>();
            var renderer3d = Object.FindObjectOfType<StrategicMapUniverseRenderer>();
            TestContext.WriteLine($"[map diag] cloud={(cloud == null ? "NULL" : cloud.PointCount.ToString())} " +
                $"rendererActive={(renderer3d == null ? "NULL" : renderer3d.gameObject.activeInHierarchy.ToString())} " +
                $"camEnabled={(renderer3d?.Camera == null ? "NULL" : renderer3d.Camera.enabled.ToString())} " +
                $"hasRT={(renderer3d?.Camera != null && renderer3d.Camera.targetTexture != null)} " +
                $"shader={(Resources.Load<Shader>("StrategicMap/StrategicMapAdditivePoint") != null)} " +
                $"galaxies={(renderer3d?.Universe == null ? -1 : renderer3d.Universe.galaxies.Count)} " +
                $"systems={(renderer3d?.Universe == null ? -1 : renderer3d.Universe.systems.Count)}");

            if (renderer3d != null)
            {
                Bounds b = renderer3d.ContentBounds;
                Camera c = renderer3d.Camera;
                TestContext.WriteLine($"[map geom] contentCenter={b.center} extents={b.extents} " +
                    $"camPos={c.transform.position} camFwd={c.transform.forward} " +
                    $"fov={c.fieldOfView} near={c.nearClipPlane} far={c.farClipPlane} mask={c.cullingMask} " +
                    $"cloudLayer={(cloud != null ? cloud.gameObject.layer : -1)} " +
                    $"hasMeshRenderer={(cloud != null && cloud.GetComponent<MeshRenderer>() != null)}");
            }

            Capture(map, "1-universe.png");

            var universeRenderer = Object.FindObjectOfType<StrategicMapUniverseRenderer>();
            // The LIVE universe the renderer is actually drawing — since the live source
            // landed, focusing by the retired HTML snapshot's ids would resolve nothing.
            StrategicMapUniverseSnapshot snapshot = universeRenderer != null ? universeRenderer.Universe : null;

            // The galaxy level: only this galaxy's systems are drawn, which is the level the
            // reference spends most of its time in.
            if (universeRenderer != null && snapshot != null && snapshot.galaxies.Count > 0)
            {
                universeRenderer.FocusGalaxy(snapshot.galaxies[0].id);
                // The camera's own smoothing needs well over a hundred frames to cross a
                // 55 -> 2 move, and a batchmode frame with a booted world can take seconds --
                // which is what turned this capture run into an hour and a half. SnapCamera
                // puts it where it is already heading, which is the same framing without the
                // flight. The wait that follows is for the focus pulse, which runs on real
                // seconds and is part of what is being looked at.
                universeRenderer.SnapCamera();
            }
            yield return WaitUnscaled(1.2f);
            Capture(map, "2-galaxy.png");

            map.Presenter.EnterSystem(context.StartSystemId);
            map.Refresh();
            // Focus the start system — a real, Visited row in the live universe — so the
            // 3D orbit layer has something to show.
            if (universeRenderer != null)
                universeRenderer.FocusSystem(context.StartSystemId);
            // The reference's own smoothing is 1 - pow(0.002, dt), about 10% per frame at 60fps,
            // so a 55 -> 1.72 move needs well over a hundred frames to actually arrive.
            // Select a world so the reticle has something to sit on, and hover its neighbour
            // so the two states can be told apart in one frame.
            if (universeRenderer != null && snapshot != null)
            {
                for (int i = 0; i < snapshot.systems.Count; i++)
                {
                    StrategicMapUniverseSnapshot.SystemRow shown = snapshot.systems[i];
                    if (shown.id != context.StartSystemId) continue;
                    int planetCount = shown.planetTo - shown.planetFrom;
                    if (planetCount >= 2)
                        universeRenderer.SetSelection(
                            snapshot.planets[shown.planetFrom + Mathf.Min(3, planetCount - 1)].id,
                            snapshot.planets[shown.planetFrom + Mathf.Min(5, planetCount - 2)].id);
                    universeRenderer.SetWaypoint(shown.id);
                    break;
                }
            }
            if (universeRenderer != null) universeRenderer.SnapCamera();
            // The focus lock runs on unscaled seconds, so the capture waits real time for it
            // rather than a frame count -- otherwise it freezes the expanding ring halfway
            // across the frame.
            yield return WaitUnscaled(1.2f);
            Capture(map, "3-system.png");

            // The star of the system the player is standing in, on the same card layout.
            map.Presenter.SelectStar();
            map.Refresh();
            yield return WaitUnscaled(1.0f);
            Capture(map, "4-star-card.png");

            map.Presenter.SelectPlanet(first.PlanetId);
            map.Refresh();
            // The card slides in over ~280ms of unscaled time and its slot marks stagger in
            // behind that. Capturing after two frames caught it half-arrived and made every
            // screenshot an argument about whether the card was broken.
            yield return WaitUnscaled(1.0f);
            Capture(map, "5-planet-card.png");

            map.SetOpen(false);
            TestContext.WriteLine($"[map shots] written to {OutputDir}");
        }

        /// <summary>Writes the map's own RenderTexture straight to disk — direct proof the 3D layer produced pixels.</summary>
        private static void DumpRenderTexture(RenderTexture rt, string fileName)
        {
            if (rt == null) return;
            RenderTexture previous = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                RenderTexture.active = rt;
                texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false) { name = CaptureReadbackName };
                texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                texture.Apply();

                // Report how much of it is genuinely lit, so an all-black target cannot pass silently.
                Color32[] pixels = texture.GetPixels32();
                int lit = 0;
                for (int i = 0; i < pixels.Length; i += 37)
                    if (pixels[i].r > 8 || pixels[i].g > 8 || pixels[i].b > 8) lit++;
                TestContext.WriteLine($"[map rt] {fileName} {rt.width}x{rt.height} litSamples={lit}/{pixels.Length / 37}");

                File.WriteAllBytes(Path.Combine(ResolutionDir, fileName), texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (texture != null) Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Waits real seconds, which a fixed frame count does not in batchmode — but bounded
        /// by a frame budget as well, so it can fail instead of hanging.
        /// <para>
        /// This was <c>while (Time.unscaledTime &lt; until) yield return null;</c> with no ceiling
        /// of any kind. If the clock ever stops advancing the loop spins at 100% CPU forever:
        /// the fixture was observed silent for 18+ minutes, and again killed at 9, each time
        /// writing no results at all — and because a hang is not a failure, it took the whole
        /// PlayMode run with it rather than reporting anything.
        /// </para>
        /// <para>
        /// Two ceilings rather than one: real seconds cannot bound a stalled clock, and a frame
        /// count cannot bound a stalled player loop. Whichever gives out first ends the wait,
        /// and running out of frames is an explicit failure naming both clocks — a wait that
        /// can only ever hang tells you nothing about why.
        /// </para>
        /// </summary>
        private static IEnumerator WaitUnscaled(float seconds)
        {
            // Generous: ~60 fps for the longest wait here is ~72 frames, so this only trips
            // when the clock is genuinely not advancing, never on a merely slow machine.
            const int MaxFrames = 3000;

            float started = Time.unscaledTime;
            float until = started + seconds;
            int frames = 0;

            while (Time.unscaledTime < until)
            {
                if (++frames > MaxFrames)
                {
                    Assert.Fail(
                        $"waited {MaxFrames} frames for {seconds:0.00}s of unscaled time that never " +
                        $"elapsed — unscaledTime started at {started:0.000}, is {Time.unscaledTime:0.000}, " +
                        $"target {until:0.000} (advanced {Time.unscaledTime - started:0.000}s); " +
                        $"timeScale={Time.timeScale}, captureDeltaTime={Time.captureDeltaTime}. " +
                        "The clock is not advancing, so this wait would never return.");
                }
                yield return null;
            }
        }

        /// <summary>The capture's own temporaries, named so they can be looked for by name afterwards.</summary>
        private const string CaptureCameraName = "MapCaptureCamera";
        private const string CaptureTargetName = "MapCaptureTarget";
        private const string CaptureReadbackName = "MapCaptureReadback";

        /// <summary>Every live object of a kind, hidden and asset objects included.</summary>
        private static int LiveCount<T>() where T : Object => Resources.FindObjectsOfTypeAll<T>().Length;

        /// <summary>
        /// Whether any object of this kind is still alive under this exact name.
        ///
        /// Deliberately by name rather than by a before/after count: Unity's own temporary
        /// render-target pool grows and shrinks on its own schedule between captures — it was
        /// measured going 11 -> 12 inside one capture and starting the next at 5 — so a bare
        /// count difference reports the pool, not a leak. A capture's own camera and target
        /// are named, so asking for them by name asks exactly the question worth asking.
        /// </summary>
        private static bool AnyAlive<T>(string name) where T : Object
        {
            foreach (T candidate in Resources.FindObjectsOfTypeAll<T>())
                if (candidate != null && candidate.name == name) return true;
            return false;
        }

        private static void Capture(StrategicMapScreen map, string fileName)
        {
            int camerasBefore = LiveCount<Camera>();
            int targetsBefore = LiveCount<RenderTexture>();

            // The 3D map draws into its own RenderTexture; force that draw first so the
            // RawImage below is showing a genuinely rendered frame, not a stale one.
            var universe = Object.FindObjectOfType<StrategicMapUniverseRenderer>();
            if (universe != null)
            {
                universe.RenderNow();
                DumpRenderTexture(universe.Target, "rt-" + fileName);
            }

            Canvas canvas = map.GetComponentInChildren<Canvas>(true);
            RenderMode savedMode = canvas.renderMode;
            Camera savedCamera = canvas.worldCamera;

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = CaptureTargetName };
            var camGo = new GameObject(CaptureCameraName) { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            RenderTexture previous = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.01f, 0.015f, 0.035f, 1f);
                cam.orthographic = true;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 1000f;
                cam.cullingMask = ~0;
                cam.targetTexture = rt;
                cam.enabled = false;
                // Far below the play area, NOT at the origin: with a live booted world the
                // origin is inside the start system — the star's additive glow quads and the
                // planet's limb (structures and all) render straight over the canvas and wash
                // the whole capture grey. The ScreenSpaceCamera canvas follows the camera, so
                // from empty space the capture is the map UI and nothing else.
                camGo.transform.position = new Vector3(0f, -80000f, -500f);

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 100f;
                Canvas.ForceUpdateCanvases();
                cam.Render();

                RenderTexture.active = rt;
                texture = new Texture2D(Width, Height, TextureFormat.RGB24, false) { name = CaptureReadbackName };
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();

                // A file being written is not a screenshot. This suite has already shipped
                // seventeen blank PNGs once; a capture that is one flat colour, or entirely
                // black, is a failed capture and says so here rather than in a review.
                Color32[] pixels = texture.GetPixels32();
                var distinct = new HashSet<int>();
                int lit = 0;
                for (int i = 0; i < pixels.Length; i += 101)
                {
                    Color32 p = pixels[i];
                    distinct.Add((p.r << 16) | (p.g << 8) | p.b);
                    if (p.r > 8 || p.g > 8 || p.b > 8) lit++;
                }

                byte[] png = texture.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(ResolutionDir, fileName), png);
                TestContext.WriteLine($"[map shots] {fileName} {Width}x{Height} " +
                    $"bytes={png.Length} distinctColours={distinct.Count} lit={lit}/{pixels.Length / 101}");

                Assert.Greater(distinct.Count, 8,
                    $"{fileName} came out as {distinct.Count} colour(s) -- a flat frame, not a capture of the map");
                Assert.Greater(lit, 0, $"{fileName} is entirely black");
            }
            finally
            {
                RenderTexture.active = previous;
                canvas.renderMode = savedMode;
                canvas.worldCamera = savedCamera;
                Canvas.ForceUpdateCanvases();
                if (texture != null) Object.DestroyImmediate(texture);
                // The camera must let go of the target before the target is released, or the
                // engine logs an ERROR ("Releasing render texture that is set as
                // Camera.targetTexture!") which fails the test outright.
                cam.targetTexture = null;
                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            TestContext.WriteLine($"[map shots] {fileName} cameras {camerasBefore}->{LiveCount<Camera>()} " +
                $"renderTextures {targetsBefore}->{LiveCount<RenderTexture>()}");
            Assert.IsFalse(AnyAlive<Camera>(CaptureCameraName),
                $"{fileName}: the capture camera outlived the capture");
            Assert.IsFalse(AnyAlive<RenderTexture>(CaptureTargetName),
                $"{fileName}: the capture's render target outlived the capture");
            Assert.IsFalse(AnyAlive<Texture2D>(CaptureReadbackName),
                $"{fileName}: the read-back texture outlived the capture");
        }
    }
}
