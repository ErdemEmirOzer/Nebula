using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Nebula.Poi;
using Nebula.Ship;
using Nebula.UI.Controls;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 3 — yalnız Part 3'te DEĞİŞEN UI yüzeylerinin fotoğrafı.
    /// <para>
    /// Kapsam: savaş silah üretimi açık ve kapalı hâliyle (P2-DEAD-001/P1-COMBAT-001),
    /// POI görevlendirme (P2-DEAD-002/P1-WORLD-001), Part 3'te katalog satırı kazanan
    /// yeni UI eylemlerinin rebind satırları (P1-ECO-006/021, P1-DATA-001), tipli ret
    /// metni (P1-UI-003/P1-ECO-007/017), devre dışı durum ve ilk odak
    /// (P1-UI-002/P2-TEST-005). Chapter H'nin silah üretimi <b>aynı</b> karttır:
    /// tutorial artık normal oyunla aynı <c>ShipCombat.TryStartProduction</c>
    /// dikişini sürüyor, dolayısıyla o dersin ekranı bu ekrandır.
    /// </para>
    /// <para>
    /// <b>Kapalı bir kontrol tıklanarak fotoğraflanmaz.</b> Üretim düzeltmesinden
    /// sonra buton doğru şekilde kapalıdır ve gerekçesini
    /// <c>NebulaUiFactory.ButtonHandle.DisabledReason</c> içinde taşır; o gerekçe
    /// ekrana <b>tooltip</b> olarak çıkar. Bu yüzden ret, kontrolün üzerine gerçek
    /// bir işaretçiyle gelinerek — gerçek raycast, gerçek <c>pointerEnter</c> —
    /// gösterilir ve fotoğraf ancak tooltip <i>gerçekten görünürken</i> çekilir.
    /// Ret metni ayrıca <c>DisabledReason</c> üzerinden doğrudan doğrulanır.
    /// </para>
    /// <para>
    /// <b>Tek [UnityTest], tek süreç.</b> Sahneyi yeniden yükleyen her ek test bir
    /// tam oturum yıkımı demek ve bunları üst üste dizmek
    /// <c>UnloadUnusedAssets</c>'i 10ms'den on saniyelere çıkarıp koşuyu çıktı
    /// üretemez hâle getiriyor. Bu fixture kendi sürecinde tek başına koşar.
    /// </para>
    /// <para>
    /// Yazılan her PNG <b>aynı koşuda</b> denetlenir: boş/düz değil, beyan edilen
    /// boyutta, metin kırpılmamış, güvenli alan içinde, tooltip ekran dışına
    /// taşmamış, odak gerçekten görünür, ikon ve metin okunur, placeholder/debug
    /// metni yok, iki durum birbirinin aynısı değil. Part 1 bu suiti 17 boş PNG ile
    /// yeşil geçirdi; buradaki iddiaların tamamı tam olarak bunu imkânsız kılmak
    /// içindir.
    /// </para>
    /// </summary>
    // Graphics: every test here renders a live camera into a RenderTexture and reads the pixels back. Under -nographics the
    // render is a segfault risk in RenderOffscreenCameras, not merely a blank frame, so this
    // fixture must never be selected into a headless worker. It carries no ReleaseRegression
    // category on purpose -- the headless gate selects by that name and must not reach here.
    [Category("Graphics")]
    public class Part3UiCaptureTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>The Scout the POI surface needs. Restored the way a loaded save restores its roster.</summary>
        private const string CaptureScoutId = "part3-capture-scout";

        private static string OutputDir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime", "Part3");

        /// <summary>Every file this fixture writes, in the order it writes them.</summary>
        public static readonly string[] Captures =
        {
            "p3-01-weapon-production-blocked-no-hardpoint-1920x1080.png",
            "p3-02-weapon-production-blocked-in-combat-1920x1080.png",
            "p3-03-weapon-production-enabled-1920x1080.png",
            "p3-04-weapon-production-focus-1920x1080.png",
            "p3-05-weapon-production-produced-1920x1080.png",
            "p3-06-weapon-production-1280x720.png",
            "p3-07-weapon-production-3440x1440.png",
            "p3-08-weapon-production-1920x1080-scale140.png",
            "p3-09-weapon-production-1920x1080-highcontrast.png",
            "p3-10-weapon-production-1920x1080-motionoff.png",
            "p3-11-poi-dispatch-1920x1080.png",
            "p3-12-poi-dispatch-focus-1920x1080.png",
            "p3-13-poi-dispatch-picker-1920x1080.png",
            "p3-14-poi-dispatch-picker-3440x1440.png",
            "p3-15-new-actions-bindings-1920x1080.png",
            "p3-16-new-actions-bindings-1280x720.png",
            "p3-17-new-actions-bindings-1920x1080-scale140.png",
            "p3-18-new-actions-bindings-1920x1080-highcontrast.png",
        };

        /// <summary>
        /// The actions Part 3 gave a real, rebindable catalog row. Named here on
        /// purpose: if Agent 2's set differs from this list the fixture fails and
        /// says which id is missing, rather than photographing a shorter panel and
        /// passing.
        /// </summary>
        public static readonly string[] Part3NewActionIds =
        {
            NebulaInputActions.FlightHeadingLock,
            NebulaInputActions.MenuQuantityDecrease,
            NebulaInputActions.MenuQuantityIncrease,
            NebulaInputActions.GamepadSubmit,
            NebulaInputActions.GamepadCancel,
            NebulaInputActions.ExtractionPrevDefenseNode,
            NebulaInputActions.ExtractionNextDefenseNode,
        };

        /// <summary>Text that must never reach a shipped screen.</summary>
        private static readonly string[] ForbiddenText =
        {
            "lorem", "ipsum", "placeholder", "todo", "tbd", "fixme", "xxx",
            "null", "nan", "infinity", "debug", "test only", "sample text",
            "new text", "button", "system.", "unnamed"
        };

        private Camera captureCamera;
        private RenderTexture captureTarget;
        private NebulaUiShell shell;
        private EventSystem eventSystem;
        private Selectable hovered;
        private float savedUiScale, savedTimeScale;
        private bool savedHighContrast, savedMotionOff;

        [SetUp]
        public void Setup()
        {
            savedUiScale = NebulaUiShell.UiScale;
            savedTimeScale = Time.timeScale;
            savedHighContrast = LightingAccessibility.HighContrast;
            savedMotionOff = AnimationAccessibility.MotionOff;
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
            LightingAccessibility.HighContrast = savedHighContrast;
            AnimationAccessibility.MotionOff = savedMotionOff;

            SolarSystemBootstrap boot = Object.FindObjectOfType<SolarSystemBootstrap>();
            if (boot != null && boot.SessionActive) boot.EndSession();
        }

        // =====================================================================

        [UnityTest]
        public IEnumerator EveryUiSurfacePart3ChangedPhotographedFromOneRealSession()
        {
            RequireGraphicsDevice();
            yield return LoadSceneFresh();

            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            Assert.IsNotNull(context?.Presentation?.UiShell, "the session built no shell");
            shell = context.Presentation.UiShell;
            eventSystem = Object.FindObjectOfType<EventSystem>();
            Assert.IsNotNull(eventSystem, "the session built no EventSystem — focus cannot be photographed");
            Assert.IsNotNull(NebulaTooltip.Active,
                "the session built no tooltip: a disabled control's reason has nowhere to appear");

            NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            Assert.IsNotNull(menu, "the session built no parent menu screen");

            var combat = context.Ship.GetComponent<ShipCombat>();
            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            Assert.IsNotNull(combat?.Hardpoints, "the session's ship has no combat state");

            // =================================================================
            // Weapon production — the surface P2-DEAD-001/P1-COMBAT-001 created
            // =================================================================
            // Both the family gate and the resource check answer before the hardpoint
            // check, so each has to be satisfied or the photograph documents the wrong
            // refusal. Tier and materials are the ones the catalog already asks for —
            // nothing invented — leaving the single filled hardpoint as the only reason left.
            context.Ship.GetComponent<ShipModules>().Modules
                .SetTier("defense.weapon_coordination", CombatCatalog.PlasmaUnlockWeaponCoordinationTier);
            WeaponTierCost plasmaCost = WeaponCatalog.CostForTier(WeaponFamily.Plasma, 1);
            cargo.SetCapacity(10000f);
            cargo.Add(ProcessingCatalog.BasicPartId, plasmaCost.Parts);
            cargo.Add(plasmaCost.MaterialId, plasmaCost.Material);

            // Open, not just ShowPage. ShowPage only chooses which page is current
            // (NebulaParentMenuScreen.cs:355); Open is what puts the screen on the shell
            // and therefore what makes its hierarchy active (:487-497, and it calls
            // ShowPage itself). Driven with ShowPage alone the whole menu stayed inactive,
            // so every control on it reported activeInHierarchy=False — and an inactive
            // object receives no raycast by definition, which is why a real pointer over
            // a genuinely-constructed, genuinely-refusing Produce Plasma found nothing at
            // all. The page's own state was correct throughout: Refresh runs fine on an
            // inactive hierarchy, which is why the interactable and blocked-reason
            // assertions above passed while the pointer assertion below could not.
            // UiFinalLifecyclePerformanceTests drives the same screen the same way
            // (Open first, then ShowPage per page).
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Combat),
                "the parent menu refused to open — a modal already owns input, so nothing "
                + "below is being photographed in the state a player would see");
            NebulaCombatPage page = menu.PageOf<NebulaCombatPage>();
            Assert.IsNotNull(page, "the menu carries no combat page");
            page.Refresh();
            yield return Settle();

            Button plasma = RequireControl(NebulaCombatPage.PlasmaProductionControlName,
                "there is no way to produce Plasma from the Combat page");
            RequireControl(NebulaCombatPage.LaserProductionControlName, "no Laser production control");
            RequireControl(NebulaCombatPage.EmProductionControlName, "no EM production control");

            // ---- refused, and the refusal is readable -------------------------
            // A Form I ship's single active hardpoint is filled by the birth Laser.
            // CheckProduction is a pure precondition: asking it changes nothing, and
            // it is the very call TryStartProduction runs, so the control and the
            // service cannot disagree.
            string noHardpoint = NebulaCombatPage.WeaponProductionRejectText(
                WeaponProductionRejectReason.NoHardpointAvailable);
            Assert.AreEqual(WeaponProductionRejectReason.NoHardpointAvailable,
                combat.CheckProduction(WeaponFamily.Plasma, 1),
                "the capture needs a genuinely refused production");
            Assert.IsFalse(plasma.interactable,
                "the service refuses, but the control still offers itself — the dead button is back");
            Assert.AreEqual(noHardpoint, page.ProductionBlockedReason(WeaponFamily.Plasma),
                "the control carries a different reason than the service gave");
            AssertRefusalIsReachableByPointer(plasma, noHardpoint);
            AssertRefusalIsReachableByFocus(plasma, noHardpoint);
            yield return Shoot(1920, 1080, 1f, Captures[0], () => StageTooltip(plasma, noHardpoint));

            // ---- refused for a second, different reason ----------------------
            string inCombat = NebulaCombatPage.WeaponProductionRejectText(
                WeaponProductionRejectReason.CombatActive);
            OpenAnEncounter(combat);
            page.Refresh();
            yield return Settle();

            // The service is asked first, and asked AFTER the frames above have run.
            // The flag this refusal rests on is republished every frame by
            // CombatEncounterDirector.Update, so a state poked straight into
            // ShipCombat is gone by the next frame and every assertion below would
            // be about the previous refusal wearing this one's name.
            Assert.AreEqual(WeaponProductionRejectReason.CombatActive,
                combat.CheckProduction(WeaponFamily.Plasma, 1),
                "the encounter did not survive the frames this capture needs, so what "
                + "is being photographed is not combat's refusal");
            Assert.IsFalse(plasma.interactable,
                "loadout is locked in combat, but the control is still offered");
            Assert.AreEqual(inCombat, page.ProductionBlockedReason(WeaponFamily.Plasma),
                "in combat the control does not say that combat is the reason");
            AssertRefusalIsReachableByPointer(plasma, inCombat);
            AssertRefusalIsReachableByFocus(plasma, inCombat);
            yield return Shoot(1920, 1080, 1f, Captures[1], () => StageTooltip(plasma, inCombat));

            // Motion Off is an accessibility preference, not a permission: the same
            // refusal has to be readable with it on. Asserted here rather than only
            // photographed, because two frames that differ prove nothing about
            // whether the sentence was still reachable in the second one.
            AnimationAccessibility.MotionOff = true;
            page.Refresh();
            yield return Settle();
            AssertRefusalIsReachableByPointer(plasma, inCombat);
            AssertRefusalIsReachableByFocus(plasma, inCombat);
            AnimationAccessibility.MotionOff = savedMotionOff;

            CloseTheEncounter(combat);
            page.Refresh();
            yield return Settle();

            // ---- the player frees the slot, with the page's own control -------
            Button release = RequireLiveControl(NebulaCombatPage.ReleaseControlName + "0",
                "the occupied hardpoint cannot be freed from the Combat page");
            Click(release);
            page.Refresh();
            yield return Settle();
            Assert.IsTrue(combat.Hardpoints[0].IsEmpty, "the release control freed nothing");

            // ---- enabled, and an enabled control has nothing to explain -------
            Assert.IsTrue(plasma.interactable,
                "with an empty active hardpoint and the cost in the hold, production is still refused");
            Assert.IsNull(page.ProductionBlockedReason(WeaponFamily.Plasma),
                "a control that can be pressed is still carrying a refusal");
            AssertNothingIsExplainedByAWorkingControl(plasma);
            yield return Shoot(1920, 1080, 1f, Captures[2]);

            // ---- focus -------------------------------------------------------
            eventSystem.SetSelectedGameObject(plasma.gameObject);
            yield return Settle();
            Assert.AreSame(plasma.gameObject, eventSystem.currentSelectedGameObject,
                "the production control cannot take focus");
            yield return Shoot(1920, 1080, 1f, Captures[3]);
            eventSystem.SetSelectedGameObject(null);
            AssertVisiblyDifferent(Captures[2], Captures[3], 0.0002f,
                "odak hiçbir şey çizmiyor: odaklı ve odaksız kare pratik olarak aynı");

            // ---- produced: the Chapter H lesson, on the real card -------------
            Click(plasma);
            WeaponProductionJob job = combat.ActiveProductionJob;
            Assert.IsNotNull(job, "the click started no production job");
            Assert.AreEqual(plasmaCost.Seconds, job.TotalSeconds, 0.0001f,
                "the job carries a duration the catalog never named");
            Assert.IsTrue(combat.AdvanceProduction(plasmaCost.Seconds), "the job never completed");
            page.Refresh();
            yield return Settle();
            Assert.AreEqual(WeaponFamily.Plasma, combat.Hardpoints[0].Weapon,
                "the produced weapon is not on the ship");
            yield return Shoot(1920, 1080, 1f, Captures[4]);

            // ---- resolutions and accessibility modes -------------------------
            yield return Shoot(1280, 720, 1f, Captures[5]);
            yield return Shoot(3440, 1440, 1f, Captures[6]);
            yield return Shoot(1920, 1080, NebulaUiShell.MaxUiScale, Captures[7]);

            LightingAccessibility.HighContrast = true;
            page.Refresh();
            yield return Settle();
            yield return Shoot(1920, 1080, 1f, Captures[8]);
            LightingAccessibility.HighContrast = savedHighContrast;
            AssertVisiblyDifferent(Captures[4], Captures[8], 0.0002f,
                "yüksek kontrast açıkken sayfa aynı çiziliyor: mod ekrana hiç ulaşmamış");

            // ---- Motion Off, over the one thing on this page that ever moves ----
            // Photographed against a *running* job on purpose. Nothing on this page
            // reads AnimationAccessibility at all — every consumer of MotionOff /
            // SnapStateChanges lives in Visuals/Animation (the 3D presenters), in
            // UiEmphasisPulse (the resources page and the tutorial card), in
            // StrategicMapTheme, or in the brand loading art. So a settled combat card
            // photographs identically with the mode on and off, and it is right that
            // it does: there is no decorative motion here for the setting to remove.
            // Photographing the still and demanding it differ asserted that this page
            // has motion, which is the opposite of what the setting is for.
            //
            // A job in flight is the page's only moving part, and the fixture never
            // photographed one at all — before and after completion, never during. So
            // the frame now differs because the page is genuinely in a state no other
            // capture holds, and the claim made of Motion Off is the honest one: with
            // it on, the progress the player is waiting on is still legible.
            Button releaseForJob = RequireLiveControl(NebulaCombatPage.ReleaseControlName + "0",
                "the produced weapon cannot be cleared to run a second job");
            Click(releaseForJob);
            cargo.Add(ProcessingCatalog.BasicPartId, plasmaCost.Parts);
            cargo.Add(plasmaCost.MaterialId, plasmaCost.Material);
            page.Refresh();
            yield return Settle();

            Assert.IsTrue(plasma.interactable, "the slot is free and paid for, and production is still refused");
            Click(plasma);
            Assert.IsNotNull(combat.ActiveProductionJob, "the second click started no production job");
            Assert.IsFalse(combat.AdvanceProduction(plasmaCost.Seconds * 0.5f),
                "half the job's duration completed it — there is no in-flight state left to photograph");

            AnimationAccessibility.MotionOff = true;
            page.Refresh();
            yield return Settle();
            Assert.IsNotNull(combat.ActiveProductionJob,
                "the job ended before the Motion Off frame was taken");
            AssertScreenShows("ÜRETİLİYOR",
                "hareket kapalıyken devam eden üretim ekranda okunmuyor — mod bilgiyi götürüyor");
            yield return Shoot(1920, 1080, 1f, Captures[9]);
            AnimationAccessibility.MotionOff = savedMotionOff;

            // =================================================================
            // POI dispatch — the surface P2-DEAD-002/P1-WORLD-001 created
            // =================================================================
            // The control only applies to a Scout or a Collector, so with an empty
            // hangar there is nothing to photograph but an absence. The roster is
            // restored the way a loaded save restores it — the same public seam,
            // no vehicle invented and nothing spent.
            menu.ShowPage(NebulaUiAssetCatalog.Page.Fleet);
            NebulaFleetPage fleet = menu.PageOf<NebulaFleetPage>();
            Assert.IsNotNull(fleet, "the menu carries no fleet page");

            Nebula.Hangar.VehicleDef scoutDef = Nebula.Hangar.VehicleCatalog.Get(Nebula.Hangar.VehicleClass.Scout, 1);
            Assert.IsNotNull(scoutDef, "the catalog has no Mk I Scout");
            Nebula.Hangar.VehicleAgent scout = context.Hangar.RestoreVehicle(CaptureScoutId, scoutDef);
            Assert.IsNotNull(scout, "the hangar restored no Scout");
            fleet.Select(scout.VehicleId);
            fleet.Refresh();
            yield return Settle();

            Button dispatch = RequireLiveControl("PoiDispatch",
                "P2-DEAD-002: there is still no way to send a vehicle to a POI");
            yield return Shoot(1920, 1080, 1f, Captures[10]);

            eventSystem.SetSelectedGameObject(dispatch.gameObject);
            yield return Settle();
            Assert.AreSame(dispatch.gameObject, eventSystem.currentSelectedGameObject,
                "the POI dispatch control cannot take focus");
            yield return Shoot(1920, 1080, 1f, Captures[11]);
            eventSystem.SetSelectedGameObject(null);
            AssertVisiblyDifferent(Captures[10], Captures[11], 0.0002f,
                "POI kontrolünde odak hiçbir şey çizmiyor");

            Click(dispatch);
            fleet.Refresh();
            yield return Settle();
            Assert.IsTrue(fleet.PoiTargetPickerOpen,
                "the POI control was pressed and no mission picker opened");
            AssertScreenShows("POI GÖREVİ SEÇ", "the picker opened without its own heading");
            yield return Shoot(1920, 1080, 1f, Captures[12]);
            yield return Shoot(3440, 1440, 1f, Captures[13]);

            // =================================================================
            // The new rebindable actions
            // =================================================================
            menu.ShowPage(NebulaUiAssetCatalog.Page.Settings);
            NebulaSettingsPage settings = menu.PageOf<NebulaSettingsPage>();
            Assert.IsNotNull(settings, "the menu carries no settings page");
            settings.Refresh();
            yield return Settle();

            // The rebind rows live in a panel, and the panel is built closed
            // (NebulaSettingsPage.cs:334). Photographing the settings column alone
            // documents the one surface that was never supposed to carry them — which
            // is what these four files are named "bindings" for. Opened through the
            // page's own control rather than by calling OpenBindingPanel(): if that
            // control were ever missing or dead, a player could not reach the panel at
            // all and no direct call would notice.
            Button openBindings = RequireLiveControl(NebulaSettingsPage.OpenBindingsControlName,
                "there is no way to reach the rebinding panel from the settings page");
            Click(openBindings);
            settings.Refresh();
            yield return Settle();
            Assert.IsTrue(settings.BindingPanelOpen,
                "the rebinding control was pressed and no panel opened");

            AssertEveryNewActionIsWhereTheCatalogSaysItIs(settings);
            yield return Shoot(1920, 1080, 1f, Captures[14]);
            yield return Shoot(1280, 720, 1f, Captures[15]);
            yield return Shoot(1920, 1080, NebulaUiShell.MaxUiScale, Captures[16]);

            // Refreshed, like the combat page above it. Flipping the mode and only
            // waiting was the asymmetry: whatever this page repaints on Refresh was
            // never asked to, and the frame's difference from the one before it was
            // left entirely to the byte-comparison at the end to notice.
            LightingAccessibility.HighContrast = true;
            settings.Refresh();
            yield return Settle();
            yield return Shoot(1920, 1080, 1f, Captures[17]);
            LightingAccessibility.HighContrast = savedHighContrast;
            AssertVisiblyDifferent(Captures[14], Captures[17], 0.0002f,
                "ayar ekranı yüksek kontrastta aynı çiziliyor: mod bu sayfaya ulaşmıyor");

            // =================================================================
            // Every file, checked in the same run
            // =================================================================
            foreach (string file in Captures)
            {
                AssertWritten(file);
                AssertDeclaredSize(file);
                AssertNotBlank(file);
                AssertLegible(file);
            }
            AssertAllDistinct(Captures);
        }

        // =====================================================================
        // scene / state helpers
        // =====================================================================

        /// <summary>
        /// A capture fixture without a graphics device photographs nothing. It fails
        /// loudly rather than skipping: a silent skip is exactly how Part 1 shipped
        /// seventeen blank PNGs and stayed green. Run this fixture with graphics.
        /// </summary>
        private static void RequireGraphicsDevice()
        {
            Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType,
                "no graphics device: this fixture must be run WITHOUT -nographics. " +
                "It writes screenshots, and a run that cannot render must fail rather than pass empty.");
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
            Object.FindObjectOfType<SolarSystemBootstrap>()?.BeginSession();
            for (int i = 0; i < 12; i++) yield return null;
        }

        /// <summary>Wall-clock, not frames: a batchmode frame is far shorter than a real one.</summary>
        private static IEnumerator Settle()
        {
            float until = Time.realtimeSinceStartup + 0.45f;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        /// <summary>
        /// Opens a real encounter, on the thing that owns one.
        /// <para>
        /// <see cref="ShipCombat.InCombat"/> is not a state anybody may set and keep:
        /// <c>CombatEncounterDirector.Update</c> republishes it from
        /// <c>Encounter.IsOpen</c> every single frame (CombatEncounterDirector.cs:163),
        /// so a flag poked straight into the ship is overwritten on the next one. This
        /// fixture then waits ~0.45s and the parent menu re-refreshes on its own 0.25s
        /// timer (NebulaParentMenuScreen.cs:538-544) — long enough for the director to
        /// have cleared the flag and the page to have repainted the hardpoint refusal
        /// underneath it, wearing the name of the combat one.
        /// </para>
        /// <para>
        /// A threat event is the real door: it is what the four documented triggers
        /// call, and the encounter it opens stays open for
        /// <see cref="CombatEncounterState.CloseAfterSeconds"/> — which outlasts every
        /// frame this capture needs. The ship is brought into step immediately
        /// afterwards exactly as the director's own Update would, so the refresh on
        /// the very next line already sees it.
        /// </para>
        /// </summary>
        private static void OpenAnEncounter(ShipCombat combat)
        {
            CombatEncounterDirector director = combat.Director;
            Assert.IsNotNull(director,
                "the ship has no encounter director, so nothing in this session can open combat "
                + "and a flag set by hand would be overwritten on the next frame anyway");

            director.Encounter.RegisterThreatEvent();
            Assert.IsTrue(director.Encounter.IsOpen, "the threat event opened no encounter");
            combat.SetEncounterOpen(true);
        }

        /// <summary>Closes it the way it closes in play: the timer runs out.</summary>
        private static void CloseTheEncounter(ShipCombat combat)
        {
            CombatEncounterDirector director = combat.Director;
            director.Encounter.Tick(CombatEncounterState.CloseAfterSeconds);
            Assert.IsFalse(director.Encounter.IsOpen, "the encounter outlasted its own close timer");
            combat.SetEncounterOpen(false);
        }

        private Button RequireControl(string name, string because)
        {
            foreach (Button button in shell.Root(UiLayer.Menu).GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name) return button;
            Assert.Fail($"{because} (aranan kontrol adı: \"{name}\")");
            return null;
        }

        /// <summary>
        /// The control has to be on screen and usable, not merely present in the
        /// hierarchy. A hidden or permanently disabled control photographs as an
        /// absence, and an absence is what these two findings were about.
        /// </summary>
        private Button RequireLiveControl(string name, string because)
        {
            Button button = RequireControl(name, because);
            Assert.IsTrue(button.gameObject.activeInHierarchy, $"{because} — \"{name}\" ekranda değil");
            Assert.IsTrue(button.IsInteractable(), $"{because} — \"{name}\" kullanılamıyor");
            return button;
        }

        // ---- real pointer / real click ---------------------------------------

        /// <summary>
        /// Why a real pointer found nothing, in the terms that separate the three
        /// possible causes: the point is off the screen the raycaster tests against,
        /// a mask is clipping the control out, or nothing under the point is a
        /// raycast target at all. An empty RaycastAll says none of them on its own —
        /// it does not even distinguish "the control is elsewhere" from "the control
        /// is right there and inert", and the second assertion below (which names
        /// what was hit instead) never runs to narrow it.
        /// </summary>
        private static string PointerEvidence(Selectable control, Vector2 point)
        {
            var rect = (RectTransform)control.transform;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Canvas canvas = control.GetComponentInParent<Canvas>();

            var masks = new System.Text.StringBuilder();
            foreach (RectMask2D mask in control.GetComponentsInParent<RectMask2D>(true))
                masks.Append(masks.Length == 0 ? "" : ",").Append(mask.gameObject.name);

            var image = control.targetGraphic as Graphic;
            return $"point={point}, screen={Screen.width}x{Screen.height}, "
                   + $"onScreen={(point.x >= 0f && point.y >= 0f && point.x <= Screen.width && point.y <= Screen.height)}, "
                   + $"worldCorners=[{corners[0]}..{corners[2]}], "
                   + $"activeInHierarchy={control.gameObject.activeInHierarchy}, "
                   + $"interactable={control.interactable}, "
                   + $"targetGraphic={(image == null ? "none" : image.gameObject.name)}, "
                   + $"raycastTarget={(image != null && image.raycastTarget)}, "
                   + $"canvas={(canvas == null ? "none" : canvas.gameObject.name)}, "
                   + $"renderMode={(canvas == null ? "-" : canvas.renderMode.ToString())}, "
                   + $"scaleFactor={(canvas == null ? 0f : canvas.scaleFactor):0.###}, "
                   + $"worldCamera={(canvas == null || canvas.worldCamera == null ? "null" : canvas.worldCamera.name)}, "
                   + $"raycaster={(canvas == null || canvas.GetComponent<GraphicRaycaster>() == null ? "MISSING" : "present")}, "
                   + $"rectMasks=[{(masks.Length == 0 ? "none" : masks.ToString())}]";
        }

        /// <summary>The centre of a control, in the coordinate space its own canvas raycasts in.</summary>
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
        /// A real hover: the EventSystem's own raycast decides what is under the
        /// pointer, and the enter event goes to whatever that raycast returned.
        /// <para>
        /// The raycast is the load-bearing part. It is the assertion that a
        /// <b>disabled</b> control is still hit-testable — if it were not, its reason
        /// would be unreachable by every player who owns a mouse, and the fact that
        /// the string is stored would mean nothing.
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
                $"{control.gameObject.name} üzerine gerçek bir işaretçi hiçbir şey bulmuyor — " +
                "kontrol raycast almıyor, dolayısıyla gerekçesi hiçbir oyuncuya ulaşamaz. "
                + PointerEvidence(control, pointer.position));

            GameObject top = hits[0].gameObject;
            Assert.IsTrue(top == control.gameObject || top.transform.IsChildOf(control.transform),
                $"{control.gameObject.name} üzerindeki işaretçi başka bir şeye çarpıyor: {top.name}");

            pointer.pointerEnter = top;
            GameObject handler = ExecuteEvents.ExecuteHierarchy(top, pointer, ExecuteEvents.pointerEnterHandler);
            Assert.IsNotNull(handler, $"{control.gameObject.name} pointerEnter'ı kimse karşılamıyor");
            hovered = control;
        }

        /// <summary>
        /// The product claim, made in the configuration the game actually ships in:
        /// hovering a refusing control shows the service's own words, and then leaving
        /// it takes them away again.
        /// </summary>
        private void AssertRefusalIsReachableByPointer(Selectable control, string expectedReason)
        {
            Assert.IsNotEmpty(expectedReason, "the expected reason is empty — the assertion would be blind");
            Hover(control);
            AssertTooltipReads(control, expectedReason);
            ReleaseHover();
            Assert.IsFalse(NebulaTooltip.Active.Visible, "işaretçi ayrıldığı hâlde tooltip ekranda kaldı");
        }

        /// <summary>
        /// The same claim as <see cref="AssertRefusalIsReachableByPointer"/>, for the
        /// two devices that have no pointer.
        /// <para>
        /// Unity's directional search skips a Selectable that is not interactable, so
        /// a refused control is off the keyboard and off the pad — and with it the
        /// only surface carrying the reason. Three separate claims, deliberately kept
        /// apart so a failure names which one broke: the button is still refused, a
        /// focus stop still exists in its place and is not itself pressable, and
        /// focusing that stop publishes the service's sentence.
        /// </para>
        /// </summary>
        private void AssertRefusalIsReachableByFocus(Selectable control, string expectedReason)
        {
            Assert.IsNotEmpty(expectedReason, "the expected reason is empty — the assertion would be blind");

            // 1. still refused. Reachability that came from re-enabling the control
            //    is not reachability, it is the dead button coming back.
            Assert.IsFalse(control.IsInteractable(),
                $"{control.gameObject.name} yeniden tıklanabilir hâle gelmiş: gerekçe okunur oldu diye "
                + "reddedilen kontrol açılmaz");

            Transform stopTransform = control.transform.Find(NebulaUiFactory.ButtonReasonFocusSlot);
            Assert.IsNotNull(stopTransform,
                $"{control.gameObject.name} reddediyor ama yerine geçecek bir odak durağı yok — "
                + "klavye ve kol için gerekçe ulaşılamaz");
            var stop = stopTransform.GetComponent<Selectable>();

            // 2. reachable, and not a second thing to press.
            Assert.IsTrue(stopTransform.gameObject.activeInHierarchy && stop.IsInteractable(),
                $"{control.gameObject.name} odak durağı kapalı: Unity'nin yön araması "
                + "etkileşimsiz bir Selectable'ı atlar, dolayısıyla oraya hiç varılamaz");
            Assert.IsNull(stopTransform.GetComponent<Button>(),
                "odak durağı bir Button: reddedilen kontrol Submit ile yeniden çalıştırılabilir hâle gelir");
            AssertSameRect(control, stop);

            // 3. and focusing it says why.
            eventSystem.SetSelectedGameObject(stopTransform.gameObject);
            Assert.AreSame(stopTransform.gameObject, eventSystem.currentSelectedGameObject,
                $"{control.gameObject.name} odak durağı odağı almıyor");
            AssertTooltipReads(control, expectedReason);

            eventSystem.SetSelectedGameObject(null);
            Assert.IsFalse(NebulaTooltip.Active.Visible, "odak ayrıldığı hâlde gerekçe ekranda kaldı");
        }

        /// <summary>
        /// The stand-in has to stand exactly where the control does, or the focus
        /// order the player learned changes the moment something is refused.
        /// </summary>
        private static void AssertSameRect(Selectable control, Selectable stop)
        {
            var a = new Vector3[4];
            var b = new Vector3[4];
            ((RectTransform)control.transform).GetWorldCorners(a);
            ((RectTransform)stop.transform).GetWorldCorners(b);
            for (int i = 0; i < 4; i++)
                Assert.That((a[i] - b[i]).magnitude, Is.LessThan(0.01f),
                    $"{control.gameObject.name} odak durağı başka bir yerde duruyor: "
                    + $"{a[i]} yerine {b[i]} — odak sırası kontrol reddedilince kayıyor");
        }

        /// <summary>
        /// The negative control for the assertions above: a control that can be pressed
        /// has nothing to explain, so the same hover must show nothing. Without this, a
        /// tooltip that showed unconditionally would satisfy every other assertion here.
        /// </summary>
        private void AssertNothingIsExplainedByAWorkingControl(Selectable control)
        {
            Hover(control);
            Assert.IsFalse(NebulaTooltip.Active.Visible,
                $"{control.gameObject.name} kullanılabilir durumda ama yine de bir gerekçe gösteriyor " +
                $"(\"{NebulaTooltip.Active.Text}\")");
            ReleaseHover();

            // And the stand-in is gone with it: two focus stops on one usable control
            // would make the player push the stick twice to leave it.
            Transform stop = control.transform.Find(NebulaUiFactory.ButtonReasonFocusSlot);
            Assert.IsFalse(stop != null && stop.gameObject.activeInHierarchy,
                $"{control.gameObject.name} kullanılabilir ama yedek odak durağı hâlâ açık");
        }

        /// <summary>
        /// Puts the tooltip up for the frame that is about to be photographed. Runs
        /// inside <see cref="Shoot"/>, where the canvases are laid out at the capture
        /// resolution, so the panel is placed for the picture rather than for whatever
        /// size the screen happened to be. The enter event goes to the control the
        /// raycast already named in the shipped configuration; re-raycasting through a
        /// canvas temporarily re-pointed at an offscreen camera would test the capture
        /// rig, not the game.
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
            AssertTooltipReads(control, expectedReason);
        }

        private void AssertTooltipReads(Selectable control, string expectedReason)
        {
            NebulaTooltip tooltip = NebulaTooltip.Active;
            Assert.IsTrue(tooltip.Visible,
                $"{control.gameObject.name} kapalı ve gerekçesini taşıyor ama üzerine gelindiğinde " +
                "hiçbir şey göstermiyor — gerekçe saklanıyor, ulaşılamıyor");
            Assert.AreEqual(expectedReason, tooltip.Text,
                "ekrandaki gerekçe servisin verdiği gerekçe değil");
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
        }

        /// <summary>
        /// A real click, through the same handlers the input module drives. A disabled
        /// control silently does nothing here, exactly as it does for the player, so
        /// this method is asserted never to be aimed at one.
        /// </summary>
        private void Click(Selectable control)
        {
            Assert.IsTrue(control.IsInteractable(),
                $"{control.gameObject.name} kapalı: kapalı bir kontrol tıklanmaz, gerekçesi okunur");

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

        // ---- screen text ------------------------------------------------------

        private void AssertScreenShows(string expected, string because)
        {
            Assert.IsNotEmpty(expected, "the expected text is empty — the assertion would be blind");
            bool found = LiveLabels().Any(t => t.text != null && t.text.Contains(expected));
            Assert.IsTrue(found, $"{because} (ekranda aranan metin: \"{expected}\")");
        }

        /// <summary>
        /// Every label really on screen, from both layers that render. Both text
        /// components are asked: uGUI's <c>Text</c> for anything left over and TMP for
        /// the rest, because the interface is TMP and a scan that knows only
        /// <c>UnityEngine.UI.Text</c> silently finds nothing at all.
        /// </summary>
        private IEnumerable<TMPro.TMP_Text> LiveLabels()
        {
            foreach (UiLayer layer in new[] { UiLayer.Menu, UiLayer.Overlay })
            {
                RectTransform root = shell.Root(layer);
                if (root == null) continue;
                foreach (TMPro.TMP_Text text in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    if (text.isActiveAndEnabled) yield return text;
            }
        }

        /// <summary>
        /// Every action Part 3 added must be on the surface its own catalog entry
        /// promises — and be reachable there the way a player reaches it.
        /// <para>
        /// This used to demand one row, on the settings column, for all seven ids. The
        /// catalog says otherwise, in two different ways, and both matter:
        /// </para>
        /// <list type="bullet">
        /// <item><b>Rebindable</b> (<c>flight.heading_lock</c> and the two defence-node
        /// steps) — the row is built by <c>NebulaControlBindingSection.Build</c>
        /// (:212-218) inside the rebinding <i>panel</i>, in its own
        /// <see cref="InputContext"/> group, and only one group is on screen at a time
        /// (<c>ShowGroup</c>). So the claim is: open the panel, press the group's own
        /// rail control, and the label is there. Nothing else proves a player can
        /// reach it.</item>
        /// <item><b>Featured and not rebindable</b> — a reading in the settings column
        /// (<c>NebulaSettingsPage.cs:455-460</c>), which is the only thing that column
        /// ever listed.</item>
        /// <item><b>Neither</b> (the four gamepad/menu ids) — no row anywhere, by the
        /// catalog's own rule rather than by omission: 34 of the 62 non-axis actions
        /// are in this bucket, including <c>flight.thrust</c>, <c>menu.back</c>,
        /// <c>map.close</c> and <c>system.manual_save</c>. Asserting a settings row for
        /// these was asserting something the interface has never claimed. What is
        /// checked instead is that they are really bound to something, so a dead id
        /// cannot hide here — and the branch itself is the guard: the moment one is
        /// marked featured or rebindable, it is held to that surface's claim above.</item>
        /// </list>
        /// <para>
        /// Named ids, not a count, throughout: a panel that lost one row and gained
        /// another would otherwise photograph as correct.
        /// </para>
        /// </summary>
        private void AssertEveryNewActionIsWhereTheCatalogSaysItIs(NebulaSettingsPage settings)
        {
            var missing = new List<string>();

            foreach (string actionId in Part3NewActionIds)
            {
                InputActionDefinition definition = NebulaInputActions.All.FirstOrDefault(a => a.Id == actionId);
                if (definition == null) { missing.Add(actionId + " (katalogda satırı yok)"); continue; }

                if (definition.Rebindable) ShowTheGroupThatOwns(settings, definition, missing);
                else if (definition.Featured) RequireLabelOnScreen(definition, "ayar sütununda", missing);
                else if (definition.Key == KeyCode.None)
                    missing.Add($"{actionId} (\"{definition.LabelTr}\") hiçbir yüzeyde görünmüyor "
                                + "ve varsayılan bir tuşu da yok — ölü bir kimlik");
            }

            Assert.IsEmpty(missing,
                "Part 3'ün yeni UI eylemleri kataloglarının söylediği yüzeyde değil:\n  " +
                string.Join("\n  ", missing));
        }

        /// <summary>
        /// Presses the rail control for this action's context, exactly as a player
        /// does, and then asks whether the row is on screen. The press is the claim:
        /// a group whose rail control is missing or dead is a group whose rows no
        /// keyboard, pad or pointer can ever bring up, however correctly they were
        /// built.
        /// </summary>
        private void ShowTheGroupThatOwns(NebulaSettingsPage settings,
            InputActionDefinition definition, List<string> missing)
        {
            NebulaControlBindingSection section = settings.Bindings;
            if (!section.Groups.Contains(definition.Context))
            {
                missing.Add($"{definition.Id} (\"{definition.LabelTr}\") yeniden atanabilir ama "
                            + $"{definition.Context} grubu tuş atama panelinde hiç yok");
                return;
            }

            string railName = NebulaControlBindingSection.GroupControlName(definition.Context);
            Button rail = RequireControl(railName,
                $"{definition.Context} grubunun raf düğmesi yok, satırları ekrana getirilemiyor");
            Assert.IsTrue(rail.gameObject.activeInHierarchy,
                $"{definition.Context} rafı ekranda değil, grubu hiçbir şekilde öne getirilemez");

            // Only pressable while it is NOT the group in front. The rail of the group
            // already showing is deliberately refused, with "ZATEN AÇIK" on it
            // (NebulaControlBindingSection.cs:552-558) — a press that would change
            // nothing is a refusal with a reason here, not a live control. So the
            // reachability claim is made on the press that actually moves the panel,
            // and the "you are here" state is asserted as itself.
            if (section.SelectedGroup != definition.Context)
            {
                Assert.IsTrue(rail.IsInteractable(),
                    $"{definition.Context} rafı kapalı ama grubu açık değil — o grubun satırlarını "
                    + "ekrana getirmenin hiçbir yolu yok");
                Click(rail);
            }

            if (section.SelectedGroup != definition.Context)
            {
                missing.Add($"{definition.Id}: {definition.Context} rafına basıldı, grup açılmadı "
                            + $"(açık olan: {section.SelectedGroup})");
                return;
            }

            Assert.IsFalse(rail.IsInteractable(),
                $"{definition.Context} grubu ekrandayken rafı hâlâ basılabilir görünüyor — "
                + "oyuncuya hiçbir şey yapmayacak bir düğme sunuluyor");

            RequireLabelOnScreen(definition, $"{definition.Context} grubunda", missing);
        }

        private void RequireLabelOnScreen(InputActionDefinition definition, string where, List<string> missing)
        {
            if (!LiveLabels().Any(t => t.text != null && t.text.Contains(definition.LabelTr)))
                missing.Add($"{definition.Id} (\"{definition.LabelTr}\") {where} ekranda yok");
        }

        // =====================================================================
        // capture
        // =====================================================================

        /// <param name="beforeRender">
        /// Runs with the canvases already laid out at the size about to be
        /// photographed. The hover lives here rather than before the call so the
        /// tooltip is placed for the frame that is actually taken, not for whatever
        /// size the screen happened to be.
        /// </param>
        private IEnumerator Shoot(int width, int height, float factor, string fileName,
            System.Action beforeRender = null)
        {
            if (captureCamera == null)
            {
                var cameraObject = new GameObject("Part3 Capture Camera");
                captureCamera = cameraObject.AddComponent<Camera>();
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
                captureCamera.cullingMask = ~0;
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

                AssertNothingIsClippedOrOutsideTheFrame(fileName);
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
        /// Live layout checks, taken with the canvas already laid out at the size
        /// about to be photographed: text that overflows its own box, a control
        /// pushed outside the frame, and a tooltip that answers off the edge of the
        /// display are all invisible to a pixel histogram and perfectly visible here.
        /// <para>
        /// Containment is measured against each canvas's own rect rather than against
        /// a pixel count, because that rect <i>is</i> what gets rendered whatever the
        /// canvas is currently scaled or placed by. Controls under a mask are skipped:
        /// a row scrolled out of its viewport is governed by the mask, not by the
        /// edge of the screen.
        /// </para>
        /// </summary>
        private void AssertNothingIsClippedOrOutsideTheFrame(string fileName)
        {
            var offenders = new List<string>();

            foreach (TMPro.TMP_Text text in LiveLabels())
            {
                if (string.IsNullOrWhiteSpace(text.text)) continue;

                text.ForceMeshUpdate();
                if (text.isTextOverflowing)
                    offenders.Add($"metin kırpılmış: {PathOf(text.transform)} — \"{Shorten(text.text)}\"");

                foreach (string bad in ForbiddenText)
                    if (text.text.Trim().ToLowerInvariant() == bad)
                        offenders.Add($"placeholder/debug metni ekranda: \"{text.text}\"");
            }

            foreach (Selectable control in shell.Root(UiLayer.Menu).GetComponentsInChildren<Selectable>(true))
            {
                if (!control.isActiveAndEnabled) continue;
                if (control.GetComponentInParent<RectMask2D>() != null) continue;
                if (control.GetComponentInParent<Mask>() != null) continue;
                if (!InsideItsCanvas((RectTransform)control.transform, out Vector2 outside))
                    offenders.Add($"kontrol çerçeve dışında: {PathOf(control.transform)} @ {outside.x:0},{outside.y:0}");
            }

            if (NebulaTooltip.Active != null && NebulaTooltip.Active.Visible)
            {
                RectTransform panel = TooltipPanel();
                if (!InsideItsCanvas(panel, out Vector2 outside))
                    offenders.Add($"tooltip ekran dışına taştı @ {outside.x:0},{outside.y:0} " +
                                  $"(metin: \"{NebulaTooltip.Active.Text}\")");
            }

            Assert.IsEmpty(offenders, $"{fileName} çekilmeden önce yerleşim hatalı:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>The one tooltip panel, found by name so no production seam has to be widened for a test.</summary>
        private RectTransform TooltipPanel()
        {
            foreach (RectTransform rect in shell.Root(UiLayer.Overlay).GetComponentsInChildren<RectTransform>(true))
                if (rect.gameObject.name == "Tooltip Panel") return rect;
            Assert.Fail("tooltip görünür durumda ama paneli bulunamadı (aranan ad: \"Tooltip Panel\")");
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

        /// <summary>A capture that rendered nothing must fail rather than ship.</summary>
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

        // =====================================================================
        // file assertions
        // =====================================================================

        private static void AssertWritten(string file) =>
            Assert.IsTrue(File.Exists(Path.Combine(OutputDir, file)), file + " yazılmadı");

        private static Texture2D Load(string file)
        {
            string path = Path.Combine(OutputDir, file);
            Assert.IsTrue(File.Exists(path), file + " eksik");
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(path)), file + " okunabilir bir PNG değil");
            return texture;
        }

        private static void AssertDeclaredSize(string file)
        {
            string[] parts = Path.GetFileNameWithoutExtension(file).Split('-');
            string declared = parts.FirstOrDefault(p => p.Contains("x") && p.Length > 3 && char.IsDigit(p[0]));
            Assert.IsNotNull(declared, file + " adında çözünürlük yazmıyor");
            string[] size = declared.Split('x');

            Texture2D texture = Load(file);
            try
            {
                Assert.AreEqual(int.Parse(size[0]), texture.width, file + " genişliği");
                Assert.AreEqual(int.Parse(size[1]), texture.height, file + " yüksekliği");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        /// <summary>A real interface spans a wide luminance range and does not spend nearly all of itself on one value.</summary>
        private static void AssertNotBlank(string file)
        {
            Texture2D texture = Load(file);
            try
            {
                Color32[] pixels = texture.GetPixels32();
                var histogram = new int[256];
                int min = 255, max = 0;
                foreach (Color32 p in pixels)
                {
                    int luma = Luma(p);
                    histogram[luma]++;
                    if (luma < min) min = luma;
                    if (luma > max) max = luma;
                }

                Assert.Greater(max - min, 24, $"{file} yalnız {max - min} parlaklık seviyesi kapsıyor — içine bir şey çizilmemiş");

                int busiest = histogram.Max();
                Assert.Less(busiest / (float)pixels.Length, 0.995f,
                    $"{file} görüntünün %{busiest * 100f / pixels.Length:0.0}'ı tek bir değer");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        /// <summary>
        /// Legibility, measured: a screen carrying readable text and icons has a
        /// large population of high-local-contrast pixels. A blurred, washed-out or
        /// almost-empty capture does not, however wide its overall histogram is.
        /// </summary>
        private static void AssertLegible(string file)
        {
            Texture2D texture = Load(file);
            try
            {
                Color32[] pixels = texture.GetPixels32();
                int width = texture.width, height = texture.height;
                int edges = 0;

                for (int y = 1; y < height; y++)
                {
                    int row = y * width;
                    int above = (y - 1) * width;
                    for (int x = 1; x < width; x++)
                    {
                        int here = Luma(pixels[row + x]);
                        if (Mathf.Abs(here - Luma(pixels[row + x - 1])) > 40) { edges++; continue; }
                        if (Mathf.Abs(here - Luma(pixels[above + x])) > 40) edges++;
                    }
                }

                float fraction = edges / (float)pixels.Length;
                Assert.Greater(fraction, 0.0015f,
                    $"{file} okunur bir yüzey değil: yüksek yerel kontrastlı piksel oranı %{fraction * 100f:0.000} " +
                    "— metin ve ikonlar ya çizilmemiş ya da okunamayacak kadar sönük.");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        /// <summary>
        /// Two states of one screen must differ where it matters, not merely differ.
        /// A focus ring drawn one pixel wide in the same colour as the button would
        /// pass a byte comparison and still be invisible; this measures how much of
        /// the frame actually changed.
        /// </summary>
        private static void AssertVisiblyDifferent(string a, string b, float minChangedFraction, string because)
        {
            Texture2D first = Load(a), second = Load(b);
            try
            {
                Assert.AreEqual(first.width, second.width, "iki kare farklı genişlikte — karşılaştırma anlamsız");
                Assert.AreEqual(first.height, second.height, "iki kare farklı yükseklikte — karşılaştırma anlamsız");

                Color32[] left = first.GetPixels32(), right = second.GetPixels32();
                int changed = 0;
                for (int i = 0; i < left.Length; i++)
                    if (Mathf.Abs(Luma(left[i]) - Luma(right[i])) > 8) changed++;

                float fraction = changed / (float)left.Length;
                Assert.Greater(fraction, minChangedFraction,
                    $"{because}: {a} ile {b} arasında yalnız %{fraction * 100f:0.0000} piksel değişti");
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        private static int Luma(Color32 c) => (c.r * 30 + c.g * 59 + c.b * 11) / 100;

        private static void AssertAllDistinct(IEnumerable<string> files)
        {
            var seen = new Dictionary<string, string>();
            foreach (string file in files)
            {
                string path = Path.Combine(OutputDir, file);
                Assert.IsTrue(File.Exists(path), file + " eksik");
                using var sha = System.Security.Cryptography.SHA256.Create();
                using FileStream stream = File.OpenRead(path);
                string hash = System.BitConverter.ToString(sha.ComputeHash(stream));

                Assert.IsFalse(seen.ContainsKey(hash),
                    $"{file}, {(seen.TryGetValue(hash, out string other) ? other : "başka bir çekim")} ile bayt bayt aynı — " +
                    "iki durumdan biri hiç gerçekleşmedi");
                seen[hash] = file;
            }
        }
    }
}
