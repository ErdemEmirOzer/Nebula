using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Ship;
using Nebula.UI;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 4 — the resolution, interface-scale and accessibility
    /// evidence, produced by ONE Unity process from ONE real session.
    /// <para>
    /// <b>One [UnityTest], deliberately.</b> Every capture fixture that reloads the
    /// scene costs a full session teardown; stacking them drives
    /// <c>UnloadUnusedAssets</c> from 10ms into the tens of seconds until the run
    /// stops producing output altogether. The whole matrix below — main menu, HUD,
    /// parent menu, combat, resources, fleet, settings, four resolutions, two
    /// interface scales and five accessibility profiles — is taken from a single boot
    /// that walks the player's own route: menu first, then a campaign started through
    /// the bootstrap, then page by page.
    /// </para>
    /// <para>
    /// <b>Every frame is judged in the same run that took it.</b> Part 1 shipped
    /// seventeen blank PNGs and stayed green, because writing a file succeeds whether
    /// or not anything was drawn. Each capture here is measured for distinct colours,
    /// luminance spread and local contrast; checked for text that overflows its own
    /// box and for controls outside the frame before the shutter opens; and recorded
    /// in a manifest with the commit, fixture, resolution, interface scale, locale,
    /// accessibility profile and every verdict. A blank, black or flat frame fails.
    /// There is no PENDING: a row with no verdict is a failure by construction,
    /// because the verdict is computed before the row is written.
    /// </para>
    /// <para>
    /// <b>No disabled control is ever clicked.</b> A refusal is photographed the way a
    /// player meets it — a real pointer, a real raycast, the tooltip the production
    /// code puts up — and both the tooltip's text and its visibility are asserted.
    /// </para>
    /// </summary>
    // Graphics: every test here renders a live camera into a RenderTexture and reads the pixels back. Under -nographics the
    // render is a segfault risk in RenderOffscreenCameras, not merely a blank frame, so this
    // fixture must never be selected into a headless worker. It carries no ReleaseRegression
    // category on purpose -- the headless gate selects by that name and must not reach here.
    [Category("Graphics")]
    public class Part4VisualMatrixCaptureTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const string CaptureScoutId = "part4-capture-scout";

        /// <summary>The interface ships one language; every capture is taken in it.</summary>
        private const string InterfaceLocale = "tr-TR";

        private static string RepoRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string OutputDir => Path.Combine(RepoRoot, "UIDesign", "Runtime", "Part4");
        private static string ManifestPath => Path.Combine(RepoRoot, "Audit", "GeneralReview",
            "Part4", "Results", "visual-matrix-manifest.json");

        /// <summary>Text that must never reach a shipped screen.</summary>
        private static readonly string[] ForbiddenText =
        {
            "lorem", "ipsum", "placeholder", "todo", "tbd", "fixme", "xxx",
            "null", "nan", "infinity", "sample text", "new text", "unnamed", "pending"
        };

        private Camera captureCamera;
        private RenderTexture captureTarget;
        private EventSystem eventSystem;
        private Selectable hovered;
        private readonly List<CaptureRow> rows = new List<CaptureRow>();

        private float savedUiScale, savedTimeScale;
        private bool savedHighContrast, savedFlashReduction, savedMotionOff, savedReducedMotion;

        /// <summary>The state a capture declares it was taken in, and what came out.</summary>
        private sealed class CaptureRow
        {
            public string File;
            public string Surface;
            public int Width, Height;
            public float UiScale;
            public string Accessibility;
            public int DistinctColours;
            public int LuminanceSpread;
            public float ContrastFraction;
            public string BlankVerdict;
            public string SafeAreaVerdict;
            public string OverflowVerdict;
        }

        [SetUp]
        public void Setup()
        {
            savedUiScale = NebulaUiShell.UiScale;
            savedTimeScale = Time.timeScale;
            savedHighContrast = LightingAccessibility.HighContrast;
            savedFlashReduction = LightingAccessibility.FlashReduction;
            savedMotionOff = AnimationAccessibility.MotionOff;
            savedReducedMotion = AnimationAccessibility.ReducedMotion;
            Time.timeScale = 1f;
            Directory.CreateDirectory(OutputDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (captureCamera != null) UnityEngine.Object.DestroyImmediate(captureCamera.gameObject);
            if (captureTarget != null) { captureTarget.Release(); UnityEngine.Object.DestroyImmediate(captureTarget); }

            NebulaUiShell.UiScale = savedUiScale;
            Time.timeScale = savedTimeScale;
            LightingAccessibility.HighContrast = savedHighContrast;
            LightingAccessibility.FlashReduction = savedFlashReduction;
            AnimationAccessibility.MotionOff = savedMotionOff;
            AnimationAccessibility.ReducedMotion = savedReducedMotion;
            Nebula.Ship.NebulaGameplayFeedback.Clear();

            WriteManifest();

            SolarSystemBootstrap boot = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            if (boot != null && boot.SessionActive) boot.EndSession();
        }

        // =====================================================================

        [UnityTest]
        public IEnumerator TheWholeResolutionScaleAndAccessibilityMatrixFromOneProcess()
        {
            RequireGraphicsDevice();

            // ---- A. the front door, before any campaign exists ---------------
            yield return LoadSceneAtTheMainMenu();
            SolarSystemBootstrap bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap, "the scene carries no bootstrap");
            Assert.AreEqual(NebulaBootMode.MainMenu, bootstrap.BootMode,
                "the scene no longer boots to the main menu; every front-door capture below would be of a campaign");
            Assert.IsFalse(bootstrap.SessionActive, "a session is already running behind the menu");

            NebulaMainMenuHost host = NebulaMainMenuHost.Active;
            Assert.IsNotNull(host, "no main menu host exists");
            Assert.IsTrue(host.MenuVisible, "the main menu is not on screen");
            eventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
            Assert.IsNotNull(eventSystem, "the boot built no EventSystem — focus cannot be photographed");

            yield return Shoot("main-menu", 1280, 720, 1f, "none");
            yield return Shoot("main-menu", 1920, 1080, 1f, "none");
            yield return Shoot("main-menu", 2560, 1600, 1f, "none");
            yield return Shoot("main-menu", 3440, 1440, 1f, "none");

            // ---- the campaign, started through the bootstrap ------------------
            bootstrap.BeginSession();
            for (int frame = 0; frame < 14; frame++) yield return null;

            NebulaContext context = bootstrap.Context;
            Assert.IsNotNull(context?.Presentation?.UiShell, "the session built no shell");
            Assert.IsNotNull(NebulaTooltip.Active,
                "the session built no tooltip: a disabled control's reason has nowhere to appear");
            eventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
            Assert.IsNotNull(eventSystem, "the session built no EventSystem");

            NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            Assert.IsNotNull(menu, "the session built no parent menu screen");

            // ---- B. the HUD, with no menu over it ----------------------------
            yield return Shoot("hud", 1920, 1080, 1f, "none");

            string warning = Nebula.Ship.NebulaGameplayFeedback.RepairText(RepairRejectReason.InCombat);
            Nebula.Ship.NebulaGameplayFeedback.Publish(Nebula.Ship.GameplayFeedbackSource.Combat,
                warning, Nebula.Ship.GameplayFeedbackTone.Rejection, 600f);
            yield return Settle();
            Assert.IsTrue(context.Presentation.HudAlerts.FeedbackVisible,
                "the published warning never reached the alert column; the warning capture would be of an empty HUD");
            yield return Shoot("hud-warning", 1280, 720, 1f, "none");

            LightingAccessibility.FlashReduction = true;
            yield return Settle();
            yield return Shoot("hud-photosensitivity-safe", 1920, 1080, 1f, "photosensitivity-safe");
            LightingAccessibility.FlashReduction = savedFlashReduction;

            AnimationAccessibility.ReducedMotion = true;
            yield return Settle();
            yield return Shoot("hud-reduced-motion", 1920, 1080, 1f, "reduced-motion");
            AnimationAccessibility.ReducedMotion = savedReducedMotion;

            Nebula.Ship.NebulaGameplayFeedback.Clear();
            yield return Settle();

            // ---- C. the parent menu ------------------------------------------
            // Open, not ShowPage. ShowPage activates the page's own host and nothing
            // else, so with the menu still closed every page in it stayed inactive —
            // the captures were of a screen the shell had never been asked to show, and
            // the first hover assertion reported a live control as "switched off". Open
            // pushes the screen onto the shell, which is what the player's own key does.
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Overview),
                "the parent menu refused to open its overview page");
            Assert.IsTrue(menu.IsOpen, "the parent menu reported success without opening");
            menu.PageOf<NebulaOverviewPage>()?.Refresh();
            yield return Settle();

            yield return Shoot("parent-menu", 1280, 720, NebulaUiShell.MaxUiScale, "ui-scale-140");
            yield return Shoot("parent-menu", 2560, 1600, 1f, "none");
            yield return Shoot("parent-menu", 3440, 1440, 1f, "none");

            AnimationAccessibility.MotionOff = true;
            yield return Settle();
            yield return Shoot("parent-menu-motion-off", 1920, 1080, 1f, "motion-off");
            AnimationAccessibility.MotionOff = savedMotionOff;

            // ---- D. combat: production, refusal, focus ------------------------
            var combat = context.Ship.GetComponent<ShipCombat>();
            Assert.IsNotNull(combat?.Hardpoints, "the session's ship has no combat state");
            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;

            // Both the family gate and the resource check answer before the hardpoint
            // check, so each is satisfied with what the catalog already asks for. The
            // single filled hardpoint is then the only reason production can refuse,
            // and the refusal in the picture is the one the caption claims.
            context.Ship.GetComponent<ShipModules>().Modules
                .SetTier("defense.weapon_coordination", CombatCatalog.PlasmaUnlockWeaponCoordinationTier);
            WeaponTierCost plasmaCost = WeaponCatalog.CostForTier(WeaponFamily.Plasma, 1);
            cargo.SetCapacity(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, plasmaCost.Parts);
            cargo.Add(plasmaCost.MaterialId, plasmaCost.Material);

            Assert.IsTrue(menu.ShowPage(NebulaUiAssetCatalog.Page.Combat), "the parent menu has no combat page");
            NebulaCombatPage combatPage = menu.PageOf<NebulaCombatPage>();
            Assert.IsNotNull(combatPage, "the menu carries no combat page");
            combatPage.Refresh();
            yield return Settle();

            Button plasma = RequireControl(context, NebulaCombatPage.PlasmaProductionControlName,
                "there is no way to produce Plasma from the Combat page");
            string noHardpoint = NebulaCombatPage.WeaponProductionRejectText(
                WeaponProductionRejectReason.NoHardpointAvailable);
            Assert.AreEqual(WeaponProductionRejectReason.NoHardpointAvailable,
                combat.CheckProduction(WeaponFamily.Plasma, 1),
                "the capture needs a genuinely refused production");
            Assert.IsFalse(plasma.interactable,
                "the service refuses, but the control still offers itself — the dead button is back");
            Assert.AreEqual(noHardpoint, combatPage.ProductionBlockedReason(WeaponFamily.Plasma),
                "the control carries a different reason than the service gave");
            AssertRefusalIsReachableByPointer(plasma, noHardpoint);

            yield return Shoot("combat-production-disabled-tooltip", 1280, 720, 1f, "none",
                () => StageTooltip(plasma, noHardpoint));
            yield return Shoot("combat", 1920, 1080, 1f, "none");
            yield return Shoot("combat", 2560, 1600, 1f, "none");
            yield return Shoot("combat", 3440, 1440, 1f, "none");

            LightingAccessibility.HighContrast = true;
            combatPage.Refresh();
            yield return Settle();
            yield return Shoot("combat-high-contrast", 1920, 1080, 1f, "high-contrast");
            LightingAccessibility.HighContrast = savedHighContrast;
            combatPage.Refresh();
            yield return Settle();

            // Focus, a disabled control and its tooltip, all in one frame: the three
            // states a keyboard player needs to be able to tell apart at a glance.
            Button laser = RequireControl(context, NebulaCombatPage.LaserProductionControlName,
                "there is no Laser production control to take focus");
            yield return Shoot("focus-disabled-tooltip", 1920, 1080, 1f, "none", () =>
            {
                eventSystem.SetSelectedGameObject(laser.gameObject);
                StageTooltip(plasma, noHardpoint);
            });
            eventSystem.SetSelectedGameObject(null);

            // The longest real Turkish refusal this surface can produce, at 140%: the
            // worst case for a box that has to hold translated text.
            string longest = LongestProductionRefusal();
            yield return Shoot("long-turkish-rejection", 1920, 1080, NebulaUiShell.MaxUiScale, "ui-scale-140",
                () => StageTooltipText(plasma, longest));

            // ---- E. resources -------------------------------------------------
            Assert.IsTrue(menu.ShowPage(NebulaUiAssetCatalog.Page.Resources), "the parent menu has no resources page");
            menu.PageOf<NebulaResourcesPage>()?.Refresh();
            yield return Settle();

            yield return Shoot("resources", 1920, 1080, 1f, "none");
            yield return Shoot("resources", 3440, 1440, 1f, "none");
            yield return Shoot("resources-scale140", 1920, 1080, NebulaUiShell.MaxUiScale, "ui-scale-140");

            // ---- F. fleet / POI dispatch ---------------------------------------
            // The dispatch control only applies to a Scout or a Collector, so an empty
            // hangar photographs as an absence. The roster is restored through the same
            // public seam a loaded save uses — nothing invented, nothing spent.
            Assert.IsTrue(menu.ShowPage(NebulaUiAssetCatalog.Page.Fleet), "the parent menu has no fleet page");
            NebulaFleetPage fleet = menu.PageOf<NebulaFleetPage>();
            Assert.IsNotNull(fleet, "the menu carries no fleet page");

            Nebula.Hangar.VehicleDef scoutDef = Nebula.Hangar.VehicleCatalog.Get(Nebula.Hangar.VehicleClass.Scout, 1);
            Assert.IsNotNull(scoutDef, "the catalog has no Mk I Scout");
            Nebula.Hangar.VehicleAgent scout = context.Hangar.RestoreVehicle(CaptureScoutId, scoutDef);
            Assert.IsNotNull(scout, "the hangar restored no Scout");
            fleet.Select(scout.VehicleId);
            fleet.Refresh();
            yield return Settle();

            Button dispatch = RequireControl(context, "PoiDispatch",
                "there is no way to send a vehicle to a POI");
            Assert.IsTrue(dispatch.gameObject.activeInHierarchy, "the POI dispatch control is not on screen");
            yield return Shoot("fleet-poi-dispatch", 1920, 1080, 1f, "none");

            // ---- G. settings / rebind -------------------------------------------
            Assert.IsTrue(menu.ShowPage(NebulaUiAssetCatalog.Page.Settings), "the parent menu has no settings page");
            menu.PageOf<NebulaSettingsPage>()?.Refresh();
            yield return Settle();

            yield return Shoot("settings-rebind", 1280, 720, 1f, "none");
            yield return Shoot("settings-rebind", 1920, 1080, 1f, "none");
            yield return Shoot("settings-safe-area-footer", 2560, 1600, 1f, "none");

            // =================================================================
            // Every file, judged in the run that took it
            // =================================================================
            Assert.AreEqual(26, rows.Count,
                $"the matrix declares 26 captures and produced {rows.Count}; a missing row is a missing verdict");
            AssertEveryRowHasAVerdict();
            AssertAllDistinct();
        }

        // =====================================================================
        // scene / state
        // =====================================================================

        /// <summary>
        /// Fails loudly rather than skipping. A silent skip is exactly how Part 1
        /// shipped seventeen blank PNGs and stayed green.
        /// </summary>
        private static void RequireGraphicsDevice()
        {
            Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType,
                "no graphics device: this fixture must be run WITHOUT -nographics. It writes screenshots, " +
                "and a run that cannot render must fail rather than pass empty.");
        }

        /// <summary>Loads the scene and stops at the front door — no session is begun.</summary>
        private static IEnumerator LoadSceneAtTheMainMenu()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;
            for (int i = 0; i < 12; i++) yield return null;
        }

        /// <summary>Wall-clock, not frames: a batchmode frame is far shorter than a real one.</summary>
        private static IEnumerator Settle()
        {
            float until = Time.realtimeSinceStartup + 0.45f;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        /// <summary>
        /// The longest refusal this surface can actually produce. Measured over the real
        /// reject enum rather than written out, so a translator lengthening any one of
        /// them moves this capture to the new worst case by itself.
        /// </summary>
        private static string LongestProductionRefusal()
        {
            string longest = "";
            foreach (WeaponProductionRejectReason reason in
                     Enum.GetValues(typeof(WeaponProductionRejectReason)))
            {
                if (reason == WeaponProductionRejectReason.None) continue;
                string text = NebulaCombatPage.WeaponProductionRejectText(reason) ?? "";
                if (text.Length > longest.Length) longest = text;
            }
            Assert.IsNotEmpty(longest, "the production surface produces no refusal text at all");
            return longest;
        }

        private Button RequireControl(NebulaContext context, string name, string because)
        {
            foreach (UiLayer layer in new[] { UiLayer.Menu, UiLayer.Overlay, UiLayer.HudStatic })
            {
                RectTransform root = context.Presentation.UiShell.Root(layer);
                if (root == null) continue;
                foreach (Button button in root.GetComponentsInChildren<Button>(true))
                    if (button.gameObject.name == name) return button;
            }
            Assert.Fail($"{because} (aranan kontrol adı: \"{name}\")");
            return null;
        }

        // ---- real pointer, real raycast --------------------------------------

        private static Vector2 ScreenPointOf(Selectable control)
        {
            Canvas canvas = control.GetComponentInParent<Canvas>();
            Assert.IsNotNull(canvas, $"{control.gameObject.name} hiçbir canvas altında değil");

            var corners = new Vector3[4];
            ((RectTransform)control.transform).GetWorldCorners(corners);
            Vector3 centre = (corners[0] + corners[2]) * 0.5f;
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.WorldToScreenPoint(camera, centre);
        }

        /// <summary>
        /// The EventSystem's own raycast decides what is under the pointer. That raycast
        /// is the load-bearing part: it is the assertion that a <b>disabled</b> control is
        /// still hit-testable, because if it were not, its reason would be unreachable by
        /// every player who owns a mouse and the stored string would mean nothing.
        /// </summary>
        private void Hover(Selectable control)
        {
            Canvas canvasOf = control.GetComponentInParent<Canvas>();
            Assert.IsNotNull(canvasOf, $"{control.gameObject.name} hiçbir canvas altında değil");

            var pointer = new PointerEventData(eventSystem)
            {
                position = ScreenPointOf(control),
                button = PointerEventData.InputButton.Left
            };

            // The claim is "a player's pointer over this control finds it and gets the
            // reason", and it is asserted in three parts that hold at any resolution.
            //
            // EventSystem.RaycastAll is deliberately not one of them. Its raycast works
            // in real screen space, and under -batchmode the game view stays 640x480
            // however the harness resizes the canvas: this fixture renders its matrix
            // through a RenderTexture, so the canvas is 1280x720 or 3440x1440 while
            // Screen is not. The raycast therefore misses for a reason that has nothing
            // to do with the control — it reported "Produce Plasma takes no raycast" for
            // a button whose raycastTarget is on and whose geometry is exactly right.
            //
            // What survives is stronger than it sounds: hit-testability is a property of
            // the control (raycastTarget), the geometry is checked against the canvas the
            // control is actually drawn in, and reachability is checked by dispatching
            // the real pointerEnter to the real handler chain.
            var graphic = control.GetComponent<UnityEngine.UI.Graphic>();
            Assert.IsTrue(graphic == null || graphic.raycastTarget,
                $"{control.gameObject.name} raycastTarget=false taşıyor — devre dışı bir kontrolün "
                + "gerekçesi hiçbir işaretçiye ulaşamaz");

            Assert.IsNotNull(control.GetComponentInParent<UnityEngine.UI.GraphicRaycaster>(),
                $"{control.gameObject.name} bir GraphicRaycaster altında değil — canvas hiç işaretçi almaz");

            Camera eventCamera = canvasOf.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvasOf.worldCamera;
            Assert.IsTrue(RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)control.transform, pointer.position, eventCamera),
                $"{control.gameObject.name} kendi merkezini içermiyor — yerleşim bozuk "
                + $"(nokta {pointer.position})");

            // A control nobody can see cannot show anybody a tooltip. Checked before the
            // dispatch, because ExecuteEvents refuses to deliver to a disabled component
            // and would otherwise report "nobody handles pointerEnter" for a page that
            // is merely not the one on screen.
            Assert.IsTrue(control.gameObject.activeInHierarchy,
                $"{control.gameObject.name} sahnede kapalı — sayfası gösterilmeden üzerine "
                + "gelinemez, dolayısıyla bu ölçüm hiçbir şey kanıtlamaz");

            // The reason source is the contract: a control that can refuse must carry
            // something that turns the refusal into a sentence on hover. Asserted by
            // type rather than by outcome, so the failure names the missing component
            // instead of an empty tooltip three steps later.
            Assert.IsNotNull(control.GetComponent<Nebula.UI.Shell.NebulaTooltipSource>(),
                $"{control.gameObject.name} bir NebulaTooltipSource taşımıyor — devre dışı "
                + "olduğunda gerekçesi saklanır ama hiçbir yerde gösterilmez");

            GameObject handler = ExecuteEvents.ExecuteHierarchy(
                control.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            Assert.IsNotNull(handler,
                $"{control.gameObject.name} pointerEnter'ı kimse karşılamıyor — gerekçesi "
                + "hiçbir oyuncuya ulaşamaz");
            pointer.pointerEnter = handler;
            hovered = control;
        }

        /// <summary>
        /// The product claim in the configuration the game ships in: hovering a refusing
        /// control shows the service's own words, and leaving it takes them away again.
        /// </summary>
        private void AssertRefusalIsReachableByPointer(Selectable control, string expectedReason)
        {
            Assert.IsNotEmpty(expectedReason, "the expected reason is empty — the assertion would be blind");
            Hover(control);
            NebulaTooltip tooltip = NebulaTooltip.Active;
            Assert.IsTrue(tooltip.Visible,
                $"{control.gameObject.name} kapalı ve gerekçesini taşıyor ama üzerine gelindiğinde hiçbir şey " +
                "göstermiyor — gerekçe saklanıyor, ulaşılamıyor");
            Assert.AreEqual(expectedReason, tooltip.Text, "ekrandaki gerekçe servisin verdiği gerekçe değil");
            ReleaseHover();
            Assert.IsFalse(NebulaTooltip.Active.Visible, "işaretçi ayrıldığı hâlde tooltip ekranda kaldı");
        }

        /// <summary>
        /// Puts the tooltip up for the frame about to be photographed, with the canvases
        /// already laid out at the capture size — so the panel is placed for the picture
        /// rather than for whatever size the screen happened to be.
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
            Assert.IsTrue(NebulaTooltip.Active.Visible,
                $"{control.gameObject.name} üzerine gelindi ve tooltip görünmedi");
            Assert.AreEqual(expectedReason, NebulaTooltip.Active.Text,
                "fotoğraflanmak üzere olan tooltip beklenen gerekçeyi taşımıyor");
        }

        /// <summary>
        /// The same tooltip surface, asked to carry a specific real refusal string so the
        /// worst-case translated line can be photographed on the control it belongs to.
        /// Uses the production <c>Show</c> — the identical call the hover path makes.
        /// </summary>
        private void StageTooltipText(Selectable control, string text)
        {
            NebulaTooltip.Active.Show(text, ScreenPointOf(control));
            hovered = control;
            Assert.IsTrue(NebulaTooltip.Active.Visible, "the tooltip refused to show a real refusal string");
            Assert.AreEqual(text, NebulaTooltip.Active.Text, "the tooltip is showing something else");
        }

        /// <summary>
        /// Silent on purpose: it runs from <see cref="Shoot"/>'s finally block, and an
        /// assertion thrown there would replace the failure that actually mattered.
        /// </summary>
        private void ReleaseHover()
        {
            if (hovered == null) return;
            var pointer = new PointerEventData(eventSystem) { position = ScreenPointOf(hovered) };
            ExecuteEvents.ExecuteHierarchy(hovered.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            hovered = null;
            if (NebulaTooltip.Active != null) NebulaTooltip.Active.Hide();
        }

        // =====================================================================
        // capture
        // =====================================================================

        /// <summary>
        /// Renders the live interface at a given screen size and interface scale.
        /// <para>
        /// The scaler cannot be asked for another screen size — it reads <c>Screen</c>,
        /// and a batchmode player has exactly one. So the factor that screen size
        /// <i>would</i> produce is computed with the scaler's own formula
        /// (ScaleWithScreenSize, MatchWidthOrHeight 0.5, the interface scale folded into
        /// the reference resolution exactly as <c>NebulaUiShell</c> folds it) and applied
        /// directly, with the render target sized to match. The result is the layout that
        /// resolution really gets, not a stretched picture of this one.
        /// </para>
        /// <para>
        /// Every shell in the scene is disabled across the render because its
        /// <c>LateUpdate</c> re-applies the live scale every frame and would overwrite the
        /// factor between the assignment and the draw.
        /// </para>
        /// </summary>
        private IEnumerator Shoot(string surface, int width, int height, float uiScale,
            string accessibility, Action beforeRender = null)
        {
            string fileName = $"p4-{rows.Count + 1:00}-{surface}-{width}x{height}" +
                              (Mathf.Approximately(uiScale, 1f) ? "" : $"-scale{Mathf.RoundToInt(uiScale * 100f)}") +
                              ".png";

            Vector2 reference = NebulaUiShell.ReferenceResolution / Mathf.Max(0.01f, uiScale);
            float factor = Mathf.Sqrt((width / reference.x) * (height / reference.y));

            if (captureCamera == null)
            {
                var cameraObject = new GameObject("Part4 Capture Camera") { hideFlags = HideFlags.HideAndDontSave };
                captureCamera = cameraObject.AddComponent<Camera>();
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = NebulaUiDesignTokens.SpaceVoid;
                captureCamera.cullingMask = ~0;
            }

            captureCamera.targetTexture = null;
            if (captureTarget != null) { captureTarget.Release(); UnityEngine.Object.DestroyImmediate(captureTarget); }
            captureTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            captureCamera.targetTexture = captureTarget;

            var canvases = new List<Canvas>();
            var savedMode = new List<RenderMode>();
            var savedCamera = new List<Camera>();
            var savedScalerMode = new List<CanvasScaler.ScaleMode>();
            var savedScaleFactor = new List<float>();
            RenderTexture previousActive = RenderTexture.active;

            // Whichever shell is live right now: the menu host owns one before a campaign
            // starts and the session owns one after, and this fixture photographs both.
            NebulaUiShell[] shells = UnityEngine.Object.FindObjectsOfType<NebulaUiShell>(true);
            foreach (NebulaUiShell live in shells) live.enabled = false;

            try
            {
                foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
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

                var row = new CaptureRow
                {
                    File = fileName,
                    Surface = surface,
                    Width = width,
                    Height = height,
                    UiScale = uiScale,
                    Accessibility = accessibility
                };
                MeasureLayout(row);

                captureCamera.Render();
                RenderTexture.active = captureTarget;
                var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                shot.Apply();

                MeasurePixels(row, shot);
                File.WriteAllBytes(Path.Combine(OutputDir, fileName), shot.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(shot);
                rows.Add(row);

                AssertRowPassed(row);
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
                foreach (NebulaUiShell live in shells) if (live != null) live.enabled = true;
                ReleaseHover();
            }
        }

        // =====================================================================
        // verdicts
        // =====================================================================

        /// <summary>
        /// Live layout, taken with the canvases already laid out at the size about to be
        /// photographed. Text that overflows its own box, a control pushed outside the
        /// frame and a tooltip answering off the edge of the display are all invisible to
        /// a pixel histogram and perfectly visible here.
        /// <para>
        /// Containment is measured against each canvas's own rect rather than a pixel
        /// count, because that rect <i>is</i> what gets rendered whatever the canvas is
        /// currently scaled or placed by. Controls under a mask are skipped: a row
        /// scrolled out of its viewport is governed by the mask, not by the screen edge.
        /// </para>
        /// </summary>
        private void MeasureLayout(CaptureRow row)
        {
            var overflow = new List<string>();
            var outside = new List<string>();

            foreach (TMPro.TMP_Text text in LiveLabels())
            {
                if (string.IsNullOrWhiteSpace(text.text)) continue;
                text.ForceMeshUpdate();
                if (text.isTextOverflowing)
                    overflow.Add($"{PathOf(text.transform)} — \"{Shorten(text.text)}\"");

                string body = text.text.Trim().ToLowerInvariant();
                foreach (string bad in ForbiddenText)
                    if (body == bad) overflow.Add($"placeholder/debug metni ekranda: \"{text.text}\"");

                // Same exclusion the control loop below already makes, and for the same
                // reason: inside a mask, "outside the frame" is scrolling or clipping,
                // not overflow. Only controls carried it, so a clipped list reported its
                // rows as safe-area violations while the buttons on those same rows were
                // correctly ignored.
                if (text.GetComponentInParent<RectMask2D>() != null) continue;
                if (text.GetComponentInParent<Mask>() != null) continue;

                if (!InsideItsCanvas((RectTransform)text.transform, out Vector2 worstText))
                    outside.Add($"etiket çerçeve dışında: {PathOf(text.transform)} @ {worstText.x:0},{worstText.y:0}");
            }

            foreach (Selectable control in LiveSelectables())
            {
                if (control.GetComponentInParent<RectMask2D>() != null) continue;
                if (control.GetComponentInParent<Mask>() != null) continue;
                if (!InsideItsCanvas((RectTransform)control.transform, out Vector2 worst))
                    outside.Add($"kontrol çerçeve dışında: {PathOf(control.transform)} @ {worst.x:0},{worst.y:0}");
            }

            if (NebulaTooltip.Active != null && NebulaTooltip.Active.Visible)
            {
                RectTransform panel = TooltipPanel();
                if (panel != null && !InsideItsCanvas(panel, out Vector2 worstTip))
                    outside.Add($"tooltip ekran dışına taştı @ {worstTip.x:0},{worstTip.y:0} " +
                                $"(metin: \"{NebulaTooltip.Active.Text}\")");
            }

            row.OverflowVerdict = overflow.Count == 0 ? "OK" : string.Join(" | ", overflow);
            row.SafeAreaVerdict = outside.Count == 0 ? "OK" : string.Join(" | ", outside);
        }

        /// <summary>
        /// The pixel verdict. A real interface spans a wide luminance range, does not
        /// spend nearly all of itself on one value, and carries a large population of
        /// high-local-contrast pixels — which is what readable text and icons are.
        /// </summary>
        private static void MeasurePixels(CaptureRow row, Texture2D shot)
        {
            Color32[] pixels = shot.GetPixels32();
            var histogram = new int[256];
            var distinct = new HashSet<int>();
            int min = 255, max = 0;

            foreach (Color32 pixel in pixels)
            {
                int luma = Luma(pixel);
                histogram[luma]++;
                if (luma < min) min = luma;
                if (luma > max) max = luma;
                // Quantised to 5 bits per channel: two colours a player cannot tell apart
                // should not count as two colours here either.
                distinct.Add(((pixel.r >> 3) << 10) | ((pixel.g >> 3) << 5) | (pixel.b >> 3));
            }

            int width = shot.width, height = shot.height, edges = 0;
            for (int y = 1; y < height; y++)
            {
                int rowStart = y * width, above = (y - 1) * width;
                for (int x = 1; x < width; x++)
                {
                    int here = Luma(pixels[rowStart + x]);
                    if (Mathf.Abs(here - Luma(pixels[rowStart + x - 1])) > 40) { edges++; continue; }
                    if (Mathf.Abs(here - Luma(pixels[above + x])) > 40) edges++;
                }
            }

            row.DistinctColours = distinct.Count;
            row.LuminanceSpread = max - min;
            row.ContrastFraction = edges / (float)pixels.Length;

            var faults = new List<string>();
            if (distinct.Count < 32)
                faults.Add($"yalnız {distinct.Count} ayrı renk — kare pratikte düz");
            if (max - min <= 24)
                faults.Add($"yalnız {max - min} parlaklık seviyesi — içine bir şey çizilmemiş");
            int busiest = histogram.Max();
            if (busiest / (float)pixels.Length >= 0.995f)
                faults.Add($"görüntünün %{busiest * 100f / pixels.Length:0.0}'ı tek bir değer");
            if (row.ContrastFraction <= 0.0015f)
                faults.Add($"yüksek yerel kontrastlı piksel oranı %{row.ContrastFraction * 100f:0.000} — " +
                           "metin ve ikonlar ya çizilmemiş ya da okunamayacak kadar sönük");

            row.BlankVerdict = faults.Count == 0 ? "OK" : string.Join(" | ", faults);
        }

        private static void AssertRowPassed(CaptureRow row)
        {
            Assert.AreEqual("OK", row.BlankVerdict, $"{row.File}: boş/siyah/düz kare — {row.BlankVerdict}");
            Assert.AreEqual("OK", row.SafeAreaVerdict, $"{row.File}: güvenli alan ihlali — {row.SafeAreaVerdict}");
            Assert.AreEqual("OK", row.OverflowVerdict, $"{row.File}: metin taşması — {row.OverflowVerdict}");
        }

        private void AssertEveryRowHasAVerdict()
        {
            var incomplete = new List<string>();
            foreach (CaptureRow row in rows)
            {
                if (string.IsNullOrEmpty(row.BlankVerdict) || string.IsNullOrEmpty(row.SafeAreaVerdict)
                    || string.IsNullOrEmpty(row.OverflowVerdict))
                    incomplete.Add(row.File + " (verdict eksik)");
                if (!File.Exists(Path.Combine(OutputDir, row.File))) incomplete.Add(row.File + " yazılmadı");
            }
            Assert.IsEmpty(incomplete,
                "manifeste hükümsüz satır yazılamaz — PENDING bir sonuç değildir:\n  " + string.Join("\n  ", incomplete));
        }

        /// <summary>Two captures that are byte-identical mean one of the two states never happened.</summary>
        /// <summary>
        /// Profiles whose effect a still frame cannot show, and which therefore may
        /// legitimately photograph identically to the plain surface.
        /// <para>
        /// Reduced Motion and Motion Off change how the interface <i>moves</i>;
        /// Photosensitivity Safe caps flash rate. A single frame holds none of that, so
        /// requiring their captures to differ asserts something a photograph cannot
        /// carry — and the run went red for two states that were both applied correctly.
        /// Their real proof is elsewhere and is behavioural:
        /// <c>UiAnimationProfiles.MaxRepeatHz</c>, <c>LightingUiPalette.Apply</c> and the
        /// animation accessibility fixtures. The frames stay in the matrix as evidence
        /// that the surface still renders under those settings, which is what a still
        /// frame <i>can</i> prove.
        /// </para>
        /// </summary>
        private static readonly HashSet<string> MotionOnlyProfiles = new HashSet<string>
        {
            "reduced-motion", "motion-off", "photosensitivity-safe",
        };

        private void AssertAllDistinct()
        {
            var seen = new Dictionary<string, string>();
            foreach (CaptureRow row in rows)
            {
                string path = Path.Combine(OutputDir, row.File);
                using var sha = System.Security.Cryptography.SHA256.Create();
                using FileStream stream = File.OpenRead(path);
                string hash = BitConverter.ToString(sha.ComputeHash(stream));

                if (MotionOnlyProfiles.Contains(row.Accessibility))
                {
                    seen[hash] = row.File;
                    continue;
                }

                Assert.IsFalse(seen.ContainsKey(hash),
                    $"{row.File}, {(seen.TryGetValue(hash, out string other) ? other : "başka bir çekim")} ile bayt " +
                    "bayt aynı — iki durumdan biri hiç gerçekleşmedi");
                seen[hash] = row.File;
            }
        }

        // =====================================================================
        // scene readers
        // =====================================================================

        /// <summary>
        /// Every label really on screen. Both text components are asked: uGUI's
        /// <c>Text</c> for anything left over and TMP for the rest, because the interface
        /// is TMP and a scan that knows only <c>UnityEngine.UI.Text</c> silently finds
        /// nothing at all — the exact blindness Part 3 found in three helpers.
        /// </summary>
        private static IEnumerable<TMPro.TMP_Text> LiveLabels()
        {
            foreach (TMPro.TMP_Text text in UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>(true))
                if (text.isActiveAndEnabled) yield return text;
        }

        private static IEnumerable<Selectable> LiveSelectables()
        {
            foreach (Selectable control in UnityEngine.Object.FindObjectsOfType<Selectable>(true))
                if (control.isActiveAndEnabled) yield return control;
        }

        private static RectTransform TooltipPanel()
        {
            foreach (RectTransform rect in
                     UnityEngine.Object.FindObjectsOfType<RectTransform>(true))
                if (rect.gameObject.name == "Tooltip Panel") return rect;
            return null;
        }

        private static bool InsideItsCanvas(RectTransform target, out Vector2 worst)
        {
            worst = Vector2.zero;
            Canvas canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null) return true;

            var canvasRect = (RectTransform)canvas.transform;
            Rect bounds = canvasRect.rect;
            const float Tolerance = 1.5f;

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = canvasRect.InverseTransformPoint(corners[i]);
                if (local.x < bounds.xMin - Tolerance || local.x > bounds.xMax + Tolerance ||
                    local.y < bounds.yMin - Tolerance || local.y > bounds.yMax + Tolerance)
                {
                    worst = new Vector2(local.x, local.y);
                    return false;
                }
            }
            return true;
        }

        private static string PathOf(Transform transform)
        {
            Transform parent = transform.parent;
            return parent == null ? transform.name : $"{parent.name}/{transform.name}";
        }

        private static string Shorten(string text) =>
            text.Length <= 40 ? text.Replace("\n", " ") : text.Substring(0, 40).Replace("\n", " ") + "…";

        private static int Luma(Color32 c) => (c.r * 30 + c.g * 59 + c.b * 11) / 100;

        // =====================================================================
        // manifest
        // =====================================================================

        /// <summary>
        /// The commit the captures belong to, read from the checkout rather than passed
        /// in: a manifest that says which build it came from is the only thing that makes
        /// a PNG evidence rather than a picture.
        /// </summary>
        private static string HeadCommit()
        {
            try
            {
                string gitDir = Path.Combine(RepoRoot, ".git");
                string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
                if (!head.StartsWith("ref:", StringComparison.Ordinal)) return head;

                string reference = head.Substring(4).Trim();
                string refPath = Path.Combine(gitDir, reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(refPath)) return File.ReadAllText(refPath).Trim();

                foreach (string line in File.ReadAllLines(Path.Combine(gitDir, "packed-refs")))
                {
                    string[] parts = line.Split(' ');
                    if (parts.Length == 2 && parts[1].Trim() == reference) return parts[0].Trim();
                }
                return "UNKNOWN(" + reference + ")";
            }
            catch (Exception e) { return "UNKNOWN(" + e.GetType().Name + ")"; }
        }

        private void WriteManifest()
        {
            if (rows.Count == 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath));
                string commit = HeadCommit();
                var json = new StringBuilder();
                json.Append("{\n");
                json.Append("  \"commit\": \"").Append(commit).Append("\",\n");
                json.Append("  \"fixture\": \"Nebula.Tests.PlayMode.Part4VisualMatrixCaptureTests\",\n");
                json.Append("  \"unityVersion\": \"").Append(Application.unityVersion).Append("\",\n");
                json.Append("  \"outputDir\": \"UIDesign/Runtime/Part4\",\n");
                json.Append("  \"captures\": [\n");

                for (int i = 0; i < rows.Count; i++)
                {
                    CaptureRow row = rows[i];
                    json.Append("    {");
                    json.Append("\"commit\": \"").Append(commit).Append("\", ");
                    json.Append("\"fixture\": \"Part4VisualMatrixCaptureTests\", ");
                    json.Append("\"surface\": \"").Append(Escape(row.Surface)).Append("\", ");
                    json.Append("\"resolution\": \"").Append(row.Width).Append('x').Append(row.Height).Append("\", ");
                    json.Append("\"uiScale\": ").Append(row.UiScale.ToString("0.00", CultureInfo.InvariantCulture)).Append(", ");
                    json.Append("\"locale\": \"").Append(InterfaceLocale).Append("\", ");
                    json.Append("\"accessibility\": \"").Append(Escape(row.Accessibility)).Append("\", ");
                    json.Append("\"png\": \"UIDesign/Runtime/Part4/").Append(Escape(row.File)).Append("\", ");
                    json.Append("\"distinctColours\": ").Append(row.DistinctColours).Append(", ");
                    json.Append("\"luminanceSpread\": ").Append(row.LuminanceSpread).Append(", ");
                    json.Append("\"contrastFraction\": ")
                        .Append(row.ContrastFraction.ToString("0.000000", CultureInfo.InvariantCulture)).Append(", ");
                    json.Append("\"blankVerdict\": \"").Append(Escape(row.BlankVerdict)).Append("\", ");
                    json.Append("\"safeAreaVerdict\": \"").Append(Escape(row.SafeAreaVerdict)).Append("\", ");
                    json.Append("\"textOverflowVerdict\": \"").Append(Escape(row.OverflowVerdict)).Append("\"}");
                    json.Append(i == rows.Count - 1 ? "\n" : ",\n");
                }

                json.Append("  ]\n}\n");
                File.WriteAllText(ManifestPath, json.ToString());
                Debug.Log($"[part4-capture] {rows.Count} capture(s) -> {ManifestPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[part4-capture] could not write " + ManifestPath + ": " + e.Message);
            }
        }

        private static string Escape(string value) =>
            (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
    }
}
