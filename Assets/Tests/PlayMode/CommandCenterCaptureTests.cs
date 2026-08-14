using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Shell;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Photographs the surface-construction card in every state it has, from one
    /// booted session.
    /// <para>
    /// Every state below is reached by playing: the planet is really unscanned,
    /// then really scanned; the ship is really moved out of range and back; the
    /// hold really is short and then really is stocked; and the Command Center is
    /// really built by clicking the control and then ticked through its own
    /// lifecycle. Nothing writes a caption, a lifecycle or a cost to make a
    /// picture come out — a capture of a hand-set state proves the art exists and
    /// nothing about whether the game can reach it.
    /// </para>
    /// <para>
    /// One test, not twelve. Each <c>[UnityTest]</c> that reloads the scene costs
    /// a full session teardown, and a fixture that stacks them drives
    /// <c>UnloadUnusedAssets</c> from 10ms into the tens of seconds until the run
    /// stops producing output at all.
    /// </para>
    /// <para>
    /// <b>No control is ever operated past its own gate.</b> The build used to be
    /// started with <c>onClick.Invoke()</c>, which runs the listener whether or not
    /// the button is interactable — so a regression that left the control permanently
    /// disabled would still have produced this whole sequence of photographs, of
    /// states no player could reach. Every press now goes through the pointer
    /// handlers, after the control has been asked whether a player could press it.
    /// And a refusal is photographed the way a player meets it: a real hover, and the
    /// tooltip the production code puts up, with its text and its visibility both
    /// asserted against the reason the card is carrying.
    /// </para>
    /// </summary>
    // Graphics: every test here renders a live camera into a RenderTexture and reads the pixels back. Under -nographics the
    // render is a segfault risk in RenderOffscreenCameras, not merely a blank frame, so this
    // fixture must never be selected into a headless worker. It carries no ReleaseRegression
    // category on purpose -- the headless gate selects by that name and must not reach here.
    [Category("Graphics")]
    public class CommandCenterCaptureTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime", "Command");

        /// <summary>Every file this fixture writes, in the order it writes them.</summary>
        public static readonly string[] Captures =
        {
            "command-01-unscanned-1920x1080.png",
            "command-02-evacuation-locked-1920x1080.png",
            "command-03-out-of-range-1920x1080.png",
            "command-04-missing-resources-1920x1080.png",
            "command-05-buildable-1920x1080.png",
            "command-06-buildable-1280x720.png",
            "command-07-buildable-3440x1440.png",
            "command-08-buildable-1920x1080-scale140.png",
            "command-09-capsule-in-transit-1920x1080.png",
            "command-10-constructing-1920x1080.png",
            "command-11-active-t1-1920x1080.png",
            "command-12-t2-gate-partly-met-1920x1080.png",
            "command-13-refusal-tooltip-1920x1080.png",
        };

        private Camera captureCamera;
        private RenderTexture captureTarget;
        private EventSystem eventSystem;
        private Selectable hovered;
        private float savedUiScale;
        private float savedTimeScale;

        [SetUp]
        public void Setup()
        {
            savedUiScale = NebulaUiShell.UiScale;
            savedTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            Directory.CreateDirectory(OutputDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (captureCamera != null) Object.DestroyImmediate(captureCamera.gameObject);
            if (captureTarget != null) { captureTarget.Release(); Object.DestroyImmediate(captureTarget); }
            NebulaUiShell.UiScale = savedUiScale;
            Time.timeScale = savedTimeScale;

            SolarSystemBootstrap boot = Object.FindObjectOfType<SolarSystemBootstrap>();
            if (boot != null && boot.SessionActive) boot.EndSession();
        }

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); run this fixture with graphics enabled");
        }

        // =====================================================================

        [UnityTest]
        public IEnumerator TheSurfaceConstructionCardInEveryStateFromARealSession()
        {
            RequireGraphicsDevice();
            yield return LoadSceneFresh();

            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            Assert.IsNotNull(context?.Presentation?.UiShell, "the session built no shell");

            PlanetIdentity planet = OuterPlanet(context);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            var extraction = context.Ship.GetComponent<ShipExtraction>();
            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            NebulaHudPlanetContext card = context.Presentation.HudPlanetContext;
            Assert.IsNotNull(card, "the session built no planet-context panel");

            eventSystem = Object.FindObjectOfType<EventSystem>();
            Assert.IsNotNull(eventSystem, "the session built no EventSystem — no control can be pressed or hovered");
            Assert.IsNotNull(NebulaTooltip.Active,
                "the session built no tooltip: a refusing control's reason has nowhere to appear");

            // ---- 01 unscanned -----------------------------------------------
            cargo.Clear();
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 10000f));
            yield return Park(context, planet, planet.Body.Radius * 1.1f);
            yield return Settle(card);
            Assert.IsFalse(profile.Scanned, "the capture needs a genuinely unscanned planet");
            Assert.AreEqual("ÖNCE GEZEGENİ TARA", card.CommandBlockedReason);
            AssertRefusalIsReachableByPointer(context, card);
            yield return Shoot(context, 1920, 1080, 1f, Captures[0]);

            // ---- 02 evacuation locked ---------------------------------------
            ExtractionState.SetEvacuationLocked(planet.WorldId, true);
            yield return Settle(card);
            StringAssert.Contains("TAHLİYE", card.CommandBlockedReason ?? "");
            AssertRefusalIsReachableByPointer(context, card);
            yield return Shoot(context, 1920, 1080, 1f, Captures[1]);
            ExtractionState.SetEvacuationLocked(planet.WorldId, false);

            // ---- 03 scanned, out of range -----------------------------------
            ExtractionState.CompletePlanetScan(planet);
            yield return Park(context, planet, planet.Body.Radius * 2.6f);
            yield return Settle(card);
            Assert.AreSame(planet, extraction.TargetPlanet, "the ship lost the planet entirely");
            StringAssert.Contains("GEZEGENE YAKLAŞ", card.CommandBlockedReason ?? "");
            yield return Shoot(context, 1920, 1080, 1f, Captures[2]);

            // ---- 04 in range, hold short ------------------------------------
            yield return Park(context, planet, planet.Body.Radius * 1.1f);
            yield return Settle(card);
            StringAssert.Contains("KAYNAK YETERSİZ", card.CommandBlockedReason ?? "");
            string shortHold = card.CommandBlockedReason;
            AssertRefusalIsReachableByPointer(context, card);
            yield return Shoot(context, 1920, 1080, 1f, Captures[3]);

            // ---- 13 the refusal as the player meets it -----------------------
            // The same state as 04, photographed with the tooltip actually up. A reason
            // that is stored and never drawn is the finding this card was built to close,
            // so one frame has to show it drawn.
            yield return Shoot(context, 1920, 1080, 1f, Captures[12],
                () => StageTooltip(CommandControl(context), shortHold));

            // ---- 05-08 buildable, at every supported size and scale ----------
            foreach (var (itemId, amount) in CommandBuildService.BuildT1Cost(profile.Family))
                cargo.Add(itemId, amount * 3f);
            yield return Settle(card);
            Assert.IsTrue(card.CommandAvailable,
                $"the control is still refusing with the recipe in the hold: {card.CommandBlockedReason}");
            Assert.AreEqual("KUR · T1", card.CommandCaption);
            AssertNothingIsExplainedByAWorkingControl(context);

            yield return Shoot(context, 1920, 1080, 1f, Captures[4]);
            yield return Shoot(context, 1280, 720, 1f, Captures[5]);
            yield return Shoot(context, 3440, 1440, 1f, Captures[6]);
            yield return Shoot(context, 1920, 1080, NebulaUiShell.MaxUiScale, Captures[7]);

            // ---- 09 the capsule, launched by the real control ----------------
            // Click, not Invoke. `onClick.Invoke()` runs the listener whether or not the
            // control is interactable, so it would have produced this photograph from a
            // permanently dead button just as happily.
            Click(CommandControl(context));
            yield return null;

            SurfaceStructure command = ExtractionState.FindStructure(
                planet.WorldId, SurfaceStructureType.Command);
            Assert.IsNotNull(command, "the capture click built nothing");
            Assert.AreEqual(DrillLifecycle.CapsuleInTransit, command.Runtime.State);
            yield return Settle(card);
            Assert.AreEqual("KAPSÜL YOLDA", card.CommandStatusText);
            yield return Shoot(context, 1920, 1080, 1f, Captures[8]);

            // ---- 10 construction, through the production tick ----------------
            command.Runtime.Tick(command.Runtime.CapsuleRemaining + 15f);
            yield return Settle(card);
            Assert.AreEqual(DrillLifecycle.Constructing, command.Runtime.State);
            StringAssert.StartsWith("İNŞA EDİLİYOR", card.CommandStatusText);
            yield return Shoot(context, 1920, 1080, 1f, Captures[9]);

            // ---- 11 active T1 ------------------------------------------------
            for (int i = 0; i < 400 && command.Runtime.State != DrillLifecycle.Active; i++)
            {
                command.Runtime.Tick(1f);
                yield return null;
            }
            Assert.AreEqual(DrillLifecycle.Active, command.Runtime.State);
            yield return FeedbackLapses();
            // The planet has been orbiting away the whole time the build ran, so the
            // ship has to catch up before the card is photographed — otherwise this
            // is a capture of the out-of-range state wearing the wrong caption.
            yield return Park(context, planet, planet.Body.Radius * 1.1f);
            yield return Settle(card);
            Assert.AreEqual("AKTİF · T1", card.CommandStatusText);
            Assert.IsFalse(extraction.CommandFeedback.HasValue,
                "the capture is still showing feedback from the original click");
            yield return Shoot(context, 1920, 1080, 1f, Captures[10]);

            // ---- 12 the T2 gates, one of them cleared -------------------------
            // The T1 shot above already shows the first gate refusing. This one
            // has to show the gate chain actually moving, so the level gate is
            // really met and the refusal has to become the next one down the list
            // — otherwise this capture is the previous capture again.
            Assert.AreEqual("YÜKSELT · T2", card.CommandCaption,
                "the control did not become the upgrade on a finished T1");
            Assert.IsFalse(card.CommandAvailable,
                "the T2 upgrade is already unlocked, so this capture cannot show a gate refusing it "
                + $"(level {ProgressionService.State.Level} of "
                + $"{PlanetaryDefenseCatalog.CommandT2.RequiredCharacterLevel}, controlled planet "
                + $"{ExtractionState.HasAnyControlledPlanet()})");
            string firstGate = card.CommandBlockedReason;
            Assert.IsFalse(string.IsNullOrEmpty(firstGate),
                "the upgrade is refused with no reason on screen");
            AssertRefusalIsReachableByPointer(context, card);

            for (int i = 0; ProgressionService.State.Level
                     < PlanetaryDefenseCatalog.CommandT2.RequiredCharacterLevel && i < 40; i++)
                ProgressionService.Award("capture.command-t2-" + i, 20000);
            foreach (var (itemId, amount) in PlanetaryDefenseCatalog.CommandT2.FullCost())
                cargo.Add(itemId, amount * 2f);
            yield return FeedbackLapses();
            yield return Park(context, planet, planet.Body.Radius * 1.1f);
            yield return Settle(card);

            Assert.IsFalse(card.CommandAvailable, "every T2 gate is met; this capture needs one still missing");
            Assert.AreNotEqual(firstGate, card.CommandBlockedReason,
                "clearing the level gate changed nothing the player can read");
            yield return Shoot(context, 1920, 1080, 1f, Captures[11]);

            foreach (string file in Captures)
                Assert.IsTrue(File.Exists(Path.Combine(OutputDir, file)), $"{file} was never written");
        }

        // =====================================================================
        // Session, planet, control
        // =====================================================================

        private static IEnumerator LoadSceneFresh()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;
            Object.FindObjectOfType<SolarSystemBootstrap>()?.BeginSession();
            for (int i = 0; i < 12; i++) yield return null;
        }

        private static PlanetIdentity OuterPlanet(NebulaContext context)
        {
            PlanetManifest outer = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            return ExtractionState.FindPlanet(outer.PlanetId);
        }

        private static IEnumerator Park(NebulaContext context, PlanetIdentity planet, float offset)
        {
            context.Ship.transform.position =
                planet.Body.transform.position + new Vector3(offset, 0f, 0f);
            yield return null;
            yield return null;
        }

        /// <summary>
        /// Waits out the ship's transient feedback window so the card falls back to
        /// showing why the control is refusing.
        /// <para>
        /// The lifecycle here is driven by ticking the runtime directly, which
        /// advances the build without advancing the clock — so the "capsule away"
        /// line from the original click is still inside its 3.5s window when the
        /// structure is already finished. A player never sees that combination;
        /// a capture should not either.
        /// </para>
        /// </summary>
        private static IEnumerator FeedbackLapses()
        {
            float until = Time.realtimeSinceStartup + ShipExtraction.MessageSeconds + 0.4f;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        /// <summary>
        /// Lets the card's own 0.2s refresh land, measured on wall-clock time.
        /// <para>
        /// Not a frame count: a batchmode frame is far shorter than a real one, so
        /// "wait thirty frames" is a couple of milliseconds here and would
        /// photograph the state before this one.
        /// </para>
        /// </summary>
        private static IEnumerator Settle(NebulaHudPlanetContext card)
        {
            float until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(card.CommandVisible, "the surface-construction card is not up");
        }

        private static Button CommandControl(NebulaContext context)
        {
            foreach (Button button in context.Presentation.UiShell
                         .Root(UiLayer.HudStatic).GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == NebulaHudPlanetContext.CommandActionButtonName) return button;
            Assert.Fail("there is no Command control on the card (aranan ad: \""
                        + NebulaHudPlanetContext.CommandActionButtonName + "\")");
            return null;
        }

        // =====================================================================
        // A real pointer: the only kind a player has
        // =====================================================================

        /// <summary>The centre of a control, in the coordinate space its own canvas raycasts in.</summary>
        private static Vector2 ScreenPointOf(Selectable control)
        {
            Canvas canvas = control.GetComponentInParent<Canvas>();
            Assert.IsNotNull(canvas, $"{control.gameObject.name} is under no canvas");

            var corners = new Vector3[4];
            ((RectTransform)control.transform).GetWorldCorners(corners);
            Vector3 centre = (corners[0] + corners[2]) * 0.5f;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.WorldToScreenPoint(camera, centre);
        }

        /// <summary>
        /// A real hover: the EventSystem's own raycast decides what is under the pointer,
        /// and the enter event goes to whatever that raycast returned.
        /// <para>
        /// The raycast is the load-bearing part. It asserts that a <b>refusing</b> control
        /// is still hit-testable — if it were not, its reason would be unreachable by
        /// every player who owns a mouse and the fact that the string is stored would mean
        /// nothing at all.
        /// </para>
        /// </summary>
        private void Hover(Selectable control)
        {
            var pointer = new PointerEventData(eventSystem)
            {
                position = ScreenPointOf(control),
                button = PointerEventData.InputButton.Left
            };

            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, hits);
            Assert.IsNotEmpty(hits,
                $"a real pointer over {control.gameObject.name} finds nothing — the control takes no raycast, "
                + "so its reason can never reach a player");

            GameObject top = hits[0].gameObject;
            Assert.IsTrue(top == control.gameObject || top.transform.IsChildOf(control.transform),
                $"the pointer over {control.gameObject.name} hits something else: {top.name}");

            pointer.pointerEnter = top;
            GameObject handler = ExecuteEvents.ExecuteHierarchy(top, pointer, ExecuteEvents.pointerEnterHandler);
            Assert.IsNotNull(handler, $"nobody answers pointerEnter on {control.gameObject.name}");
            hovered = control;
        }

        /// <summary>
        /// The product claim, in the configuration the game ships in: the card refuses,
        /// and hovering the control shows the service's own words — then leaving it takes
        /// them away again.
        /// </summary>
        private void AssertRefusalIsReachableByPointer(NebulaContext context, NebulaHudPlanetContext card)
        {
            string reason = card.CommandBlockedReason;
            Assert.IsFalse(string.IsNullOrEmpty(reason),
                "the card is refusing with no reason at all — the assertion would be blind");

            Button control = CommandControl(context);
            Assert.IsFalse(control.IsInteractable(),
                "the card carries a refusal but the control is still offered — the dead button is back");

            Hover(control);
            Assert.IsTrue(NebulaTooltip.Active.Visible,
                "the control is refusing and carries its reason, but hovering it shows nothing — "
                + "the reason is stored and unreachable");
            Assert.AreEqual(reason, NebulaTooltip.Active.Text,
                "the reason on screen is not the reason the card is carrying");
            ReleaseHover();
            Assert.IsFalse(NebulaTooltip.Active.Visible, "the tooltip stayed up after the pointer left");
        }

        /// <summary>
        /// The negative control. Without it, a tooltip that showed unconditionally would
        /// satisfy every other assertion here.
        /// </summary>
        private void AssertNothingIsExplainedByAWorkingControl(NebulaContext context)
        {
            Button control = CommandControl(context);
            Assert.IsTrue(control.IsInteractable(), "this check needs a control a player could press");
            Hover(control);
            Assert.IsFalse(NebulaTooltip.Active.Visible,
                $"{control.gameObject.name} can be pressed and is still explaining itself "
                + $"(\"{NebulaTooltip.Active.Text}\")");
            ReleaseHover();
        }

        /// <summary>
        /// Puts the tooltip up for the frame about to be photographed. Runs inside
        /// <see cref="Shoot"/>, where the canvases are already laid out at the capture
        /// resolution, so the panel is placed for the picture rather than for whatever
        /// size the screen happened to be.
        /// </summary>
        private void StageTooltip(Selectable control, string expectedReason)
        {
            var pointer = new PointerEventData(eventSystem)
            {
                position = ScreenPointOf(control),
                pointerEnter = control.gameObject
            };
            ExecuteEvents.ExecuteHierarchy(control.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            hovered = control;
            Assert.IsTrue(NebulaTooltip.Active.Visible, "the tooltip did not come up for the capture");
            Assert.AreEqual(expectedReason, NebulaTooltip.Active.Text,
                "the tooltip about to be photographed carries a different reason");
        }

        /// <summary>
        /// Silent on purpose: it also runs from <see cref="Shoot"/>'s finally block, and
        /// an assertion thrown there would replace the failure that actually mattered.
        /// </summary>
        private void ReleaseHover()
        {
            if (hovered == null) return;
            var pointer = new PointerEventData(eventSystem) { position = ScreenPointOf(hovered) };
            ExecuteEvents.ExecuteHierarchy(hovered.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            hovered = null;
        }

        /// <summary>
        /// A real click, through the same handlers the input module drives — and refused
        /// outright if the control is not one a player could press.
        /// </summary>
        private void Click(Selectable control)
        {
            Assert.IsTrue(control.IsInteractable(),
                $"{control.gameObject.name} is disabled: a disabled control is not clicked, its reason is read");

            var pointer = new PointerEventData(eventSystem)
            {
                position = ScreenPointOf(control),
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };
            ExecuteEvents.Execute(control.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(control.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(control.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        // =====================================================================
        // The camera
        // =====================================================================

        /// <summary>
        /// Renders the live shell at a given screen size and interface scale.
        /// <para>
        /// The scaler cannot be asked for another screen size — it reads
        /// <c>Screen</c>, and a batchmode player has exactly one. So the factor
        /// that screen size <i>would</i> produce is computed with the scaler's own
        /// formula (ScaleWithScreenSize, MatchWidthOrHeight 0.5, the interface
        /// scale folded into the reference resolution, exactly as
        /// <c>NebulaUiShell.ApplyScale</c> folds it) and applied directly, with
        /// the render target sized to match. The result is the layout that
        /// resolution really gets, not a stretched picture of this one.
        /// </para>
        /// <para>
        /// The shell is disabled across the render because its <c>LateUpdate</c>
        /// re-applies the live scale every frame and would overwrite the factor
        /// between the assignment and the draw.
        /// </para>
        /// </summary>
        /// <param name="beforeRender">
        /// Runs with the canvases already laid out at the size about to be photographed.
        /// The hover lives here rather than before the call so the tooltip is placed for
        /// the frame that is actually taken.
        /// </param>
        private IEnumerator Shoot(NebulaContext context, int width, int height, float uiScale, string fileName,
            System.Action beforeRender = null)
        {
            NebulaUiShell shell = context.Presentation.UiShell;
            Vector2 reference = NebulaUiShell.ReferenceResolution / uiScale;
            float factor = Mathf.Sqrt((width / reference.x) * (height / reference.y));

            if (captureCamera == null)
            {
                var go = new GameObject("CommandCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
                captureCamera = go.AddComponent<Camera>();
                captureCamera.orthographic = true;
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = NebulaUiDesignTokens.SpaceVoid;
                captureCamera.transform.position = new Vector3(0f, 0f, -100f);
            }

            captureCamera.targetTexture = null;
            if (captureTarget != null) { captureTarget.Release(); Object.DestroyImmediate(captureTarget); }
            captureTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            captureCamera.targetTexture = captureTarget;

            var canvases = new List<Canvas>();
            var savedMode = new List<RenderMode>();
            var savedCamera = new List<Camera>();
            var savedScalerMode = new List<CanvasScaler.ScaleMode>();
            var savedScaleFactor = new List<float>();
            RenderTexture previousActive = RenderTexture.active;

            shell.enabled = false;
            try
            {
                foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>(true))
                {
                    if (!canvas.isActiveAndEnabled) continue;
                    canvases.Add(canvas);
                    savedMode.Add(canvas.renderMode);
                    savedCamera.Add(canvas.worldCamera);

                    var scaler = canvas.GetComponent<CanvasScaler>();
                    savedScalerMode.Add(scaler != null ? scaler.uiScaleMode : CanvasScaler.ScaleMode.ConstantPixelSize);
                    savedScaleFactor.Add(scaler != null ? scaler.scaleFactor : 1f);

                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = captureCamera;
                    canvas.planeDistance = 100f;
                    if (scaler == null) continue;
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.scaleFactor = factor;
                }

                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();

                if (beforeRender != null)
                {
                    beforeRender();
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                }

                captureCamera.Render();

                RenderTexture.active = captureTarget;
                var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                shot.Apply();
                Save(shot, fileName);
            }
            finally
            {
                RenderTexture.active = previousActive;
                for (int i = 0; i < canvases.Count; i++)
                {
                    if (canvases[i] == null) continue;
                    canvases[i].renderMode = savedMode[i];
                    canvases[i].worldCamera = savedCamera[i];
                    var scaler = canvases[i].GetComponent<CanvasScaler>();
                    if (scaler == null) continue;
                    scaler.uiScaleMode = savedScalerMode[i];
                    scaler.scaleFactor = savedScaleFactor[i];
                }
                captureCamera.targetTexture = null;
                shell.enabled = true;
                ReleaseHover();
            }
        }

        /// <summary>
        /// A capture that rendered nothing must fail rather than ship. This suite
        /// has shipped seventeen flat grey rectangles before, and stayed green the
        /// whole time, because writing a PNG succeeds whether or not anything was
        /// drawn.
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
            Assert.IsFalse(uniform, $"{fileName} is a single flat colour — nothing rendered");

            File.WriteAllBytes(Path.Combine(OutputDir, fileName), shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
        }
    }
}
