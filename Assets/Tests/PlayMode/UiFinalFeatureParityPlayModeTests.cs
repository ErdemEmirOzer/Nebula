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
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Sectors;
using Nebula.Navigation;
using Nebula.Progression;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Shell;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The seven areas the parity matrix admitted it had only read in the source,
    /// driven in a running session.
    /// <para>
    /// Each area is checked the same way: take the real control, invoke the real
    /// callback, and then look at the <b>backend</b> — did the state move on the
    /// success path, did it stay exactly where it was on the refusal path, and did
    /// the refusal reach the screen as words. A control that does nothing quietly
    /// is the failure this whole redesign exists to remove, so "no exception" is
    /// never the assertion.
    /// </para>
    /// <para>
    /// There is no reflection left in here. The drill and Laser/Plasma flows used to
    /// be reached by <c>GetMethod(..., NonPublic)</c> because their handlers were
    /// private and no control could call them; both now have public seams
    /// (<see cref="ShipExtraction.TryBuildOrUpgradeDrill"/>,
    /// <see cref="ShipExtraction.TryBuildOrUpgradeDefense"/>) that the HUD's own
    /// controls call, and the node selection has
    /// <see cref="ShipExtraction.StepDefenseNode"/> in place of a <c>SetValue</c> on a
    /// private setter. A rename is now a compile error rather than a string lookup
    /// that fails into an assertion nobody reaches.
    /// </para>
    /// <para>
    /// The Command Center is no longer one of them. It has a real control on the
    /// surface-construction card, so its test below clicks that control — the
    /// reflection this fixture used to need for it has been deleted rather than
    /// kept alongside.
    /// </para>
    /// </summary>
    public class UiFinalFeatureParityPlayModeTests
    {
        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slotBackup;

        /// <summary>
        /// Every slot, not just the one this fixture writes to. A director whose
        /// active slot moves — which is the whole point of the slot work — writes
        /// wherever it was last pointed, and a backup that only covers slot 1 quietly
        /// stops protecting the developer's saves the moment that happens.
        /// </summary>
        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slotBackup = UiFinalPlayMode.BackupSlots();
        }

        /// <summary>
        /// Every test here leaves a live universe, a real save file and whatever the
        /// interface did to the global clock, the input gate and the modal gate. All
        /// three are process-wide: what is not put back becomes the next fixture's
        /// starting state, and the suite starts depending on its own running order.
        /// </summary>
        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();

            UiFinalPlayMode.ClearSessionStatics();
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slotBackup);
        }

        // =====================================================================
        // Session
        // =====================================================================

        /// <summary>
        /// The scene boots to the menu in production; a parity test wants the world,
        /// so it asks through the same idempotent entry the New Campaign button
        /// calls. Every wait inside is bounded and named — counting four frames and
        /// hoping is how a fixture ends up dereferencing a world that is one frame
        /// away from existing and failing somewhere unrelated.
        /// </summary>
        private static IEnumerator LoadSceneFresh()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
        }

        private static NebulaContext Ctx()
        {
            NebulaContext context = UiFinalPlayMode.Ctx();
            Assert.IsNotNull(context, "there is no live session behind this test");
            return context;
        }

        private static NebulaParentMenuScreen Menu() => Ctx().Presentation.ParentMenu;

        /// <summary>
        /// Opens a page the way the player does: through the parent menu, on the
        /// rail row, not by calling the page object directly.
        /// </summary>
        private static T OpenPage<T>(NebulaUiAssetCatalog.Page page) where T : class, INebulaMenuPage
        {
            NebulaParentMenuScreen menu = Menu();
            Assert.IsNotNull(menu, "the session built no parent menu");
            Assert.IsTrue(menu.Open(page), $"the menu refused to open on {page}");
            Assert.AreEqual(page, menu.CurrentPage);

            T typed = menu.PageOf<T>();
            Assert.IsNotNull(typed, $"{typeof(T).Name} is not registered on the rail");
            return typed;
        }

        /// <summary>The control the player clicks, found under the open page by name.</summary>
        private static Button Control(string name)
        {
            NebulaUiShell shell = Ctx().Presentation.UiShell;
            foreach (Button button in shell.Root(UiLayer.Menu).GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name) return button;
            return null;
        }

        private static Button HudControl(string name)
        {
            NebulaUiShell shell = Ctx().Presentation.UiShell;
            foreach (Button button in shell.Root(UiLayer.HudStatic).GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name) return button;
            return null;
        }

        /// <summary>
        /// Clicks the HUD context bar's primary control — the drill verb, as a player
        /// reaches it.
        /// <para>
        /// This fixture used to call <c>ShipExtraction.ConfirmBuild</c> by reflection,
        /// with a comment explaining that no control existed to click. One does: the
        /// bar's primary widget carries the <c>extraction.confirm</c> action id, the bar
        /// raises it, and <c>NebulaExtractionContextDriver</c> turns it into
        /// <see cref="ShipExtraction.TryBuildOrUpgradeDrill"/> — the same method
        /// <c>NebulaInputRouter.Pressed(extraction.confirm)</c> reaches. So the click
        /// and the keystroke are provably one command.
        /// </para>
        /// <para>
        /// The bar is refreshed and waited for on wall-clock time first, because the
        /// driver polls on its own 0.25s cadence and a batchmode frame is a fraction of
        /// a real one — sixty of them are milliseconds.
        /// </para>
        /// </summary>
        private static IEnumerator ClickDrillControl(NebulaContext context)
        {
            NebulaHudContextBar bar = context.Presentation.HudContextBar;
            Assert.IsNotNull(bar, "the session built no HUD context bar");

            float deadline = Time.realtimeSinceStartup + 10f;
            while (bar.VisibleActionCount == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.Greater(bar.VisibleActionCount, 0,
                "the context bar showed nothing at all at a scanned planet the ship is parked on");

            Button primary = HudControl("Primary");
            Assert.IsNotNull(primary, "the HUD context bar has no primary control");
            Assert.IsTrue(primary.interactable,
                "the bar's primary control is not interactable — the drill widget carries no action id");

            primary.onClick.Invoke();
            yield return null;
        }

        /// <summary>
        /// The bar's primary control, disabled, with the reason a player can read.
        /// <para>
        /// Two of the drill cases are not clicks at all: a T2 upgrade below its module
        /// gate, and a second press while the upgrade is already running. In both the
        /// interface's correct behaviour is a control that is <b>off</b> and says why —
        /// so asserting a click would be asserting the wrong thing. The reason is read
        /// out of the widget's own label rather than out of the state that produced it,
        /// which is the only way to know it reached the screen.
        /// </para>
        /// </summary>
        private static IEnumerator AssertDrillControlIsBlocked(NebulaContext context, string reasonFragment)
        {
            NebulaHudContextBar bar = context.Presentation.HudContextBar;
            Assert.IsNotNull(bar, "the session built no HUD context bar");

            float deadline = Time.realtimeSinceStartup + 10f;
            while (bar.VisibleActionCount == 0 && Time.realtimeSinceStartup < deadline) yield return null;

            Button primary = HudControl("Primary");
            Assert.IsNotNull(primary, "the HUD context bar has no primary control");
            Assert.IsFalse(primary.interactable,
                "the drill control is live in a state the seam would refuse — a control that "
                + "offers something and then says no is the dead control this redesign removes");

            // When this fails, say what the bar is ACTUALLY showing. "expected X, was
            // empty" cannot distinguish "the reason is missing" from "the widget is
            // hidden" from "a later frame overwrote it with a different action", and
            // those need three different fixes.
            StringAssert.Contains(reasonFragment, LabelUnder(primary, "Cost"),
                "the disabled control does not name the reason it is disabled. "
                + $"bar: visibleActions={bar.VisibleActionCount} dropped={bar.DroppedActionCount}; "
                + $"primary: active={primary.gameObject.activeInHierarchy} "
                + $"interactable={primary.interactable} "
                + $"caption='{LabelUnder(primary, "Caption")}' key='{LabelUnder(primary, "Key")}' "
                + $"cost='{LabelUnder(primary, "Cost")}'");
        }

        /// <summary>A named label's text under a control. The player reads this, so the test reads this.</summary>
        /// <summary>
        /// Reads a named label under a control. TMP, not uGUI: every label in this
        /// interface is a <see cref="TMPro.TextMeshProUGUI"/> (NebulaUiFactory.LabelHandle
        /// wraps one), so searching for <c>UnityEngine.UI.Text</c> found nothing and this
        /// helper returned <see cref="string.Empty"/> for every label ever asked for —
        /// silently, which made every assertion built on it measure nothing at all.
        /// uGUI is still checked as a fallback so a legacy label would not be missed.
        /// </summary>
        private static string LabelUnder(Component control, string childName)
        {
            foreach (TMPro.TextMeshProUGUI label in control.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                if (label.gameObject.name == childName) return label.text ?? string.Empty;
            foreach (Text text in control.GetComponentsInChildren<Text>(true))
                if (text.gameObject.name == childName) return text.text ?? string.Empty;
            return string.Empty;
        }

        /// <summary>
        /// Clicks the surface-defense card's build control — the L verb, as a player
        /// reaches it. Same story as the drill: reflection into
        /// <c>BuildOrUpgradeDefense</c> before, a real control on the planet-context
        /// panel now, and both it and the key end in
        /// <see cref="ShipExtraction.TryBuildOrUpgradeDefense"/>.
        /// </summary>
        private static IEnumerator ClickDefenseControl(NebulaContext context)
        {
            NebulaHudPlanetContext card = context.Presentation.HudPlanetContext;
            Assert.IsNotNull(card, "the session built no planet-context panel");

            float deadline = Time.realtimeSinceStartup + 10f;
            while (!card.DefenseVisible && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(card.DefenseVisible, "the surface-defense card never appeared");

            Button build = HudControl(NebulaHudPlanetContext.DefenseBuildButtonName);
            Assert.IsNotNull(build, "the surface-defense card has no build control");
            Assert.IsTrue(build.interactable,
                $"the defense control is dead on a scanned planet: {card.DefenseBlockedReason}");

            build.onClick.Invoke();
            yield return null;
        }

        private static void FillCargo(LogisticsCargo cargo)
        {
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 10000f));
            cargo.Add(ProcessingCatalog.BasicPartId, 1000f);
            foreach (string m in ProcessingCatalog.MaterialIds) cargo.Add(m, 1000f);
        }

        /// <summary>The outermost planet of the start system, scanned, with the ship parked on it.</summary>
        private static IEnumerator ParkOnAScannedPlanet(NebulaContext context)
        {
            PlanetManifest outer = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            PlanetIdentity planet = ExtractionState.FindPlanet(outer.PlanetId);
            ExtractionState.CompletePlanetScan(planet);

            // Physically alongside it. Nothing about the extraction driver is
            // bypassed -- it still has to notice the planet on its own.
            context.Ship.transform.position =
                planet.Body.transform.position + new Vector3(planet.Body.Radius * 1.1f, 0f, 0f);
            yield return null;
            yield return null;
        }

        private static PlanetIdentity ParkedPlanet(NebulaContext context)
        {
            PlanetManifest outer = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            return ExtractionState.FindPlanet(outer.PlanetId);
        }

        // =====================================================================
        // 1 — Drill: build, T2 upgrade, cancel, and every refusal
        // =====================================================================

        [UnityTest]
        public IEnumerator Drill_TheKitBuildDeploysARealDrill_AndAnnouncesIt()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            var extraction = context.Ship.GetComponent<ShipExtraction>();
            FillCargo(context.Ship.GetComponent<ShipLogistics>().Cargo);

            yield return ParkOnAScannedPlanet(context);
            PlanetIdentity planet = ParkedPlanet(context);
            Assert.AreSame(planet, extraction.TargetPlanet,
                "the ship never noticed the planet it is parked on");

            int before = ExtractionState.Drills.Count;
            yield return ClickDrillControl(context);
            yield return null;

            Assert.AreEqual(before + 1, ExtractionState.Drills.Count,
                $"the build produced no drill. Msg={extraction.TransientMessage}");
            Assert.IsFalse(string.IsNullOrEmpty(extraction.TransientMessage),
                "a build that happened said nothing");
        }

        [UnityTest]
        public IEnumerator Drill_ASecondBuildWithoutPartsIsRefused_AndBuildsNothing()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            var extraction = context.Ship.GetComponent<ShipExtraction>();
            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;

            yield return ParkOnAScannedPlanet(context);

            // The one-shot pioneer kit first, so the next build is the paid one.
            yield return ClickDrillControl(context);
            yield return null;
            int afterKit = ExtractionState.Drills.Count;

            // The paid path needs 20 Basic Building Parts and the hold is empty.
            cargo.TryRemove(ProcessingCatalog.BasicPartId, cargo.Get(ProcessingCatalog.BasicPartId));
            DrillStructure drill = ExtractionState.Drills[ExtractionState.Drills.Count - 1];
            Assert.IsNotNull(drill);

            // The kit drill is still riding its capsule down, so the verb on the bar has
            // already become the T2 upgrade and it is off with its reason showing. The
            // seam the key drives is then asked directly, so the backend claim is made
            // against the shipping path and not only against a disabled button.
            yield return AssertDrillControlIsBlocked(context, "SONDAJ HENÜZ AKTİF DEĞİL");

            extraction.TryBuildOrUpgradeDrill();
            yield return null;

            Assert.AreEqual(afterKit, ExtractionState.Drills.Count,
                "a refused build created a drill anyway");
            Assert.IsFalse(string.IsNullOrEmpty(extraction.TransientMessage),
                "a refused build said nothing at all");
        }

        /// <summary>
        /// The T2 upgrade's only functional gate is the Station Construction
        /// module tier. Below it the upgrade must not start, and the player must
        /// be told which tier is missing — the exact string the catalog names.
        /// </summary>
        [UnityTest]
        public IEnumerator DrillT2_BelowTheModuleTier_StartsNothingAndNamesTheGate()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            var extraction = context.Ship.GetComponent<ShipExtraction>();
            FillCargo(context.Ship.GetComponent<ShipLogistics>().Cargo);

            yield return ParkOnAScannedPlanet(context);
            PlanetIdentity planet = ParkedPlanet(context);
            PlanetProfile profile = ExtractionState.GetProfile(planet);

            // A real, already-Active T1 drill, so the flow reaches the upgrade
            // branch rather than the build branch.
            var runtime = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, 0f, 0f);
            DrillStructure drill = DrillStructure.Restore(planet, profile, runtime,
                DrillStructure.BuildSource.Kit, 0);
            yield return null;

            context.Ship.GetComponent<ShipModules>().Modules
                .SetTier(DrillUpgradeCatalog.GateModuleId, 0);
            Assert.AreEqual(0, extraction.StationConstructionTierForHud);

            // The control is off, and it names the gate. Then the seam the key drives is
            // asked directly -- the same public method NebulaInputRouter reaches -- so
            // the backend claim ("a gated upgrade starts nothing") is made against the
            // shipping path rather than only against a disabled button.
            yield return AssertDrillControlIsBlocked(context, "İSTASYON İNŞASI");

            extraction.TryBuildOrUpgradeDrill();
            yield return null;

            Assert.IsNull(drill.ActiveUpgradeJob, "a gated upgrade started anyway");
            Assert.AreEqual(
                ShipExtraction.DrillUpgradeRejectMessage(DrillUpgradeRejectReason.ModuleTierNotMet),
                extraction.TransientMessage,
                "the refusal shown is not the production copy for this reason");

            Object.Destroy(drill.gameObject);
        }

        /// <summary>
        /// With the gate met the upgrade starts once. Pressing again while it runs
        /// must report that it is already running rather than starting a second
        /// job or silently doing nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator DrillT2_StartsExactlyOnce_AndCancelsBackToNoJob()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            var extraction = context.Ship.GetComponent<ShipExtraction>();
            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            FillCargo(cargo);

            yield return ParkOnAScannedPlanet(context);
            PlanetIdentity planet = ParkedPlanet(context);
            PlanetProfile profile = ExtractionState.GetProfile(planet);

            var runtime = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, 0f, 0f);
            DrillStructure drill = DrillStructure.Restore(planet, profile, runtime,
                DrillStructure.BuildSource.Kit, 0);
            yield return null;

            context.Ship.GetComponent<ShipModules>().Modules.SetTier(
                DrillUpgradeCatalog.GateModuleId, DrillUpgradeCatalog.RequiredStationConstructionTier);

            // The gate is met on the ship's real module state. If the extraction
            // driver cannot see that, the T2 upgrade is unreachable in a real
            // game and no amount of module progression will open it.
            Assert.AreEqual(DrillUpgradeCatalog.RequiredStationConstructionTier,
                extraction.StationConstructionTierForHud,
                "ShipExtraction reports a different Station Construction tier than the ship's own " +
                "module state holds. ShipFactory adds ShipExtraction before ShipModules, so " +
                "ShipExtraction.Awake caches a null ShipModules and every tier it reads is 0.");

            yield return ClickDrillControl(context);
            yield return null;
            Assert.IsNotNull(drill.ActiveUpgradeJob,
                $"the upgrade never started. Msg={extraction.TransientMessage}");
            DrillUpgradeJob first = drill.ActiveUpgradeJob;

            // While the upgrade runs the control is off and says production has stopped;
            // the seam still has to refuse in its own words, so both are asserted.
            yield return AssertDrillControlIsBlocked(context, "ÜRETİM DURDU");

            extraction.TryBuildOrUpgradeDrill();
            yield return null;
            Assert.AreSame(first, drill.ActiveUpgradeJob, "a second job was started on top of the first");
            Assert.AreEqual(
                ShipExtraction.DrillUpgradeRejectMessage(DrillUpgradeRejectReason.AlreadyUpgrading),
                extraction.TransientMessage,
                "pressing again while it runs must say so");

            // Cancel is the same control the player uses -- the drill's own public
            // cancel, which is what the Escape branch of the handler calls.
            drill.CancelUpgrade(cargo);
            Assert.IsNull(drill.ActiveUpgradeJob, "cancel left the job in place");

            Object.Destroy(drill.gameObject);
        }

        // =====================================================================
        // 2 — Processing bench
        // =====================================================================

        /// <summary>
        /// The bench on the Resources page is a view of the real bench, not a copy
        /// of it, and the recipe cards it offers are the real ones the loaded input
        /// feeds.
        /// <para>
        /// This used to assert one row per processor slot and that the rows followed
        /// the bay when a tier grew it. Both halves are gone with the timed model:
        /// there is one bench, and a tier buys no second one. Rewriting the
        /// assertions to match would have left a test that passes and checks nothing,
        /// so what it checks now is the property that actually replaced the old one —
        /// the page reads through to <see cref="ProcessingBenchRuntime"/> rather than
        /// keeping its own number, which is the whole reason the bench is a single
        /// authority.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Processing_TheBenchIsAViewOfTheRealBenchNotACopy()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            var processing = context.Ship.GetComponent<ShipProcessing>();
            Assert.IsNotNull(processing, "the ship carries no processing bay");
            LogisticsCargo hold = context.Ship.GetComponent<ShipExtraction>().Cargo;

            NebulaResourcesPage page = OpenPage<NebulaResourcesPage>(NebulaUiAssetCatalog.Page.Resources);
            page.Refresh();

            Assert.AreEqual(0, page.BenchAmount, "precondition: the bench starts empty");
            Assert.AreEqual(0, page.VisibleRecipeCards,
                "an empty bench offered recipe cards for an input it does not hold");

            // Moved through the backend, never through the page: if the page kept its
            // own bench figure this would not reach it.
            string raw = ExtractionCatalog.ResourceIds[0];
            hold.Add(raw, 12f);
            Assert.AreEqual(BenchDepositReason.None,
                ProcessingBenchCommands.TryDeposit(processing.Bench, hold, raw, 7),
                "the deposit the page is supposed to be showing was refused");

            page.Refresh();
            Assert.AreEqual(7, page.BenchAmount,
                "the page did not follow the real bench — it is keeping its own count");
            Assert.AreEqual(raw, page.BenchItemId, "the page shows the wrong bench item");

            int expectedCards = ProcessingBenchCommands.RecipeIndicesFor(raw).Count;
            Assert.Greater(expectedCards, 0, "the catalog offers no recipe for the first raw resource");
            Assert.AreEqual(expectedCards, page.VisibleRecipeCards,
                "the page does not offer exactly the recipes this input feeds");

            // The tier buys nothing now, and must not quietly become a cap.
            processing.ApplyTier(5);
            page.Refresh();
            Assert.AreEqual(7, page.BenchAmount, "a tier change moved the bench");
            Assert.AreEqual(expectedCards, page.VisibleRecipeCards,
                "a tier change altered which recipes exist");

            Menu().Close();
            yield return null;
        }

        /// <summary>
        /// Cargo the ship really holds shows up as rows, and cargo it does not
        /// hold does not. The hold's own empty-state must give way to real rows.
        /// </summary>
        [UnityTest]
        public IEnumerator Processing_TheHoldShowsWhatTheShipActuallyCarries()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;

            NebulaResourcesPage page = OpenPage<NebulaResourcesPage>(NebulaUiAssetCatalog.Page.Resources);
            page.Refresh();
            int before = page.VisibleItemRows;

            // Something the ship is definitely not carrying: a campaign starts
            // with building parts in the hold, so adding more of those would only
            // change a number on a row that already existed.
            string unheld = null;
            foreach (string material in ProcessingCatalog.MaterialIds)
                if (cargo.Get(material) < 0.5f) { unheld = material; break; }
            Assert.IsNotNull(unheld, "the ship starts holding every material; this test has nothing to add");

            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 1000f));
            cargo.Add(unheld, 42f);
            page.Refresh();

            Assert.AreEqual(before + 1, page.VisibleItemRows,
                $"a real item ({unheld}) in the hold produced no row");

            cargo.TryRemove(unheld, 42f);
            page.Refresh();
            Assert.AreEqual(before, page.VisibleItemRows,
                "the row outlived the cargo it was showing");

            Menu().Close();
            yield return null;
        }

        // =====================================================================
        // 3 — Logistics Center
        // =====================================================================

        /// <summary>
        /// The centre exists in gameplay and refuses with a typed reason. There is
        /// no UI surface for it anywhere in the redesign — no page, no HUD panel,
        /// no context action — which is recorded as a finding rather than
        /// asserted as correct here. What is asserted is that the seam a future
        /// control would call already answers with a reason instead of silence.
        /// </summary>
        [UnityTest]
        public IEnumerator LogisticsCenter_TheBuildSeamAnswersWithATypedReason()
        {
            yield return LoadSceneFresh();
            var centre = Ctx().Ship.GetComponent<ShipLogisticsCenter>();
            Assert.IsNotNull(centre, "the ship carries no logistics-centre driver");

            CenterBuildRejectReason build = centre.CheckBuild();
            CenterUpgradeRejectReason upgrade = centre.CheckUpgrade();

            Assert.IsTrue(System.Enum.IsDefined(typeof(CenterBuildRejectReason), build));
            Assert.IsTrue(System.Enum.IsDefined(typeof(CenterUpgradeRejectReason), upgrade));
            Assert.AreNotEqual(CenterUpgradeRejectReason.None, upgrade,
                "with no centre in the system an upgrade cannot be available");
        }

        // =====================================================================
        // 4 — Maintenance Reserve, through the real panel
        // =====================================================================

        [UnityTest]
        public IEnumerator Reserve_ADepositMovesRealStock_AndIsShownExactlyOnce()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            ShipController ship = context.Ship;
            LogisticsCargo cargo = ship.GetComponent<ShipLogistics>().Cargo;
            FillCargo(cargo);

            SurfaceStructure command = BuildActiveCommand(context, out PlanetIdentity planet);
            yield return null;

            // Inside the access range the panel itself uses. The panel repaints on
            // its own 0.2 second timer, and batch mode runs uncapped -- forty
            // frames there can be forty milliseconds, which is why a frame count
            // is the wrong unit. Waited on the clock, with a real deadline.
            ship.transform.position = command.transform.position;
            NebulaHudPlanetContext panel = context.Presentation.HudPlanetContext;
            Assert.IsNotNull(panel);

            yield return UiFinalPlayMode.Until(() => panel.ReserveVisible,
                "the reserve panel's own 0.2s repaint to notice the ship is in range", 5f);

            var access = ship.GetComponent<ShipMaintenanceReserveAccess>();
            Assert.IsTrue(access.InRange, "the ship is on top of the command centre and still out of range");

            Assert.IsTrue(panel.ReserveVisible, "the reserve panel never appeared");

            MaintenanceReserve store = command.MaintenanceReserve;
            float storedBefore = store.Cargo.GetStored(ProcessingCatalog.BasicPartId);
            float cargoBefore = cargo.Get(ProcessingCatalog.BasicPartId);
            int shownBefore = panel.ReserveDeltasShown;

            HudControl(ProcessingCatalog.BasicPartId + " +").onClick.Invoke();

            Assert.AreEqual(storedBefore + ShipMaintenanceReserveAccess.DepositWithdrawIncrement,
                store.Cargo.GetStored(ProcessingCatalog.BasicPartId), 0.01f,
                $"the deposit never reached the reserve. Refusal={panel.LastRejectShown}");
            Assert.AreEqual(cargoBefore - ShipMaintenanceReserveAccess.DepositWithdrawIncrement,
                cargo.Get(ProcessingCatalog.BasicPartId), 0.01f,
                "the reserve gained stock the ship never gave up");
            Assert.AreEqual(shownBefore + 1, panel.ReserveDeltasShown,
                "one accepted transfer must produce exactly one acceptance");
            Assert.IsNull(panel.LastRejectShown, "an accepted transfer must show no refusal");

            Object.Destroy(command.gameObject);
        }

        [UnityTest]
        public IEnumerator Reserve_AWithdrawalOfStockThatIsNotThere_IsRefusedAndMovesNothing()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            ShipController ship = context.Ship;

            SurfaceStructure command = BuildActiveCommand(context, out _);
            yield return null;
            ship.transform.position = command.transform.position;
            yield return null;
            yield return null;

            NebulaHudPlanetContext panel = context.Presentation.HudPlanetContext;
            MaintenanceReserve store = command.MaintenanceReserve;
            string item = ProcessingCatalog.BasicPartId;

            float storedBefore = store.Cargo.GetStored(item);
            Assert.AreEqual(0f, storedBefore, 0.01f, "the reserve was expected to start empty");
            int shownBefore = panel.ReserveDeltasShown;

            HudControl(item + " -").onClick.Invoke();

            Assert.AreEqual(storedBefore, store.Cargo.GetStored(item), 0.01f,
                "a refused withdrawal moved stock anyway");
            Assert.AreEqual(shownBefore, panel.ReserveDeltasShown,
                "a refused withdrawal was shown as accepted");
            Assert.AreEqual(
                NebulaHudPlanetContext.ReserveRejectText(ReserveTransferRejectReason.InsufficientAmount),
                panel.LastRejectShown,
                "the refusal shown is not the production copy for this reason");

            Object.Destroy(command.gameObject);
        }

        [UnityTest]
        public IEnumerator Reserve_OutOfRange_HidesThePanelRatherThanOfferingADeadControl()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();

            SurfaceStructure command = BuildActiveCommand(context, out _);
            yield return null;

            context.Ship.transform.position = command.transform.position
                                              + new Vector3(ShipMaintenanceReserveAccess.ApproachRange * 4f, 0f, 0f);
            yield return null;
            yield return null;

            Assert.IsFalse(context.Ship.GetComponent<ShipMaintenanceReserveAccess>().InRange);
            Assert.IsFalse(context.Presentation.HudPlanetContext.ReserveVisible,
                "the reserve panel stayed up with nothing to transfer into");

            Object.Destroy(command.gameObject);
        }

        /// <summary>A real, Active Command T2 on the outer planet — the reserve's only host.</summary>
        private static SurfaceStructure BuildActiveCommand(NebulaContext context, out PlanetIdentity planet)
        {
            PlanetManifest outer = context.StartSystem.Planets[context.StartSystem.Planets.Count - 1];
            planet = ExtractionState.FindPlanet(outer.PlanetId);
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            ExtractionState.CompletePlanetScan(planet);

            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            var durability = new Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 1f);
            var runtime = new StructureRuntime(def, DrillLifecycle.Active, 0f, 0f, durability, tier: 2);
            return SurfaceStructure.Restore(planet, profile, def, profile.CommandNode, runtime, null, null);
        }

        // =====================================================================
        // 5 — Command / Laser / Plasma
        // =====================================================================

        /// <summary>
        /// Driven through the real control now, not through the private handler.
        /// <para>
        /// This test used to reach <c>BuildOrUpgradeCommand</c> by reflection and
        /// said so in the fixture summary: there was no clickable control at all,
        /// only the K key. There is one, so the reflection is gone — clicking the
        /// button a player can see is the whole claim being made.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Command_TheCardsButtonBuildsARealStructure_AndTheSecondClickDoesNotDuplicateIt()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            FillCargo(context.Ship.GetComponent<ShipLogistics>().Cargo);
            ProgressionService.Award("test.parity-command", 500000);

            yield return ParkOnAScannedPlanet(context);
            PlanetIdentity planet = ParkedPlanet(context);

            NebulaHudPlanetContext card = context.Presentation.HudPlanetContext;
            yield return WaitForCard(card);

            Button build = HudControl(NebulaHudPlanetContext.CommandActionButtonName);
            Assert.IsNotNull(build, "the surface-construction card has no Command control");
            Assert.IsTrue(build.interactable,
                $"the control is dead on a scanned planet the ship is parked on: {card.CommandBlockedReason}");

            build.onClick.Invoke();
            yield return null;

            SurfaceStructure command = ExtractionState.FindStructure(
                planet.WorldId, SurfaceStructureType.Command);
            Assert.IsNotNull(command,
                $"the command centre was never built. Msg={card.CommandMessageShown}");

            int structuresAfterBuild = ExtractionState.Structures.Count;
            build.onClick.Invoke();
            yield return null;

            Assert.AreEqual(structuresAfterBuild, ExtractionState.Structures.Count,
                "a second click built a second command centre instead of refusing");
            Assert.IsFalse(string.IsNullOrEmpty(card.CommandMessageShown),
                "the second click said nothing");

            Object.Destroy(command.gameObject);
        }

        /// <summary>
        /// Weapon production, from the button a player can see to the job on the ship.
        /// <para>
        /// This is the assertion <c>P1A3DiagReachabilityTests.WeaponProduction_IsReachableFromSomePlayerSurface</c>
        /// was reaching for and could never make. That test greps every runtime source
        /// for the literal <c>"TryStartProduction(WeaponFamily"</c> and asserts some
        /// file other than <c>ShipCombat</c> contains it — which the real caller,
        /// <c>NebulaCombatPage.StartProduction</c>, does not, because it passes a
        /// variable (<c>TryStartProduction(family, 1)</c>). The grep is therefore
        /// unsatisfiable no matter how reachable the mechanic becomes, and a green
        /// version of it would only mean somebody wrote the magic characters
        /// somewhere. Here the chain is executed instead of matched: open the page the
        /// way a player opens it, click the control, and read the ship.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator WeaponProduction_RunsFromTheCombatPagesOwnButtonToARealJob()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            FillCargo(context.Ship.GetComponent<ShipLogistics>().Cargo);

            var combat = context.Ship.GetComponent<ShipCombat>();
            Assert.IsNotNull(combat, "the session built no ShipCombat");
            Assert.IsNull(combat.ActiveProductionJob, "the fixture starts with a job already running");

            OpenPage<NebulaCombatPage>(NebulaUiAssetCatalog.Page.Combat);
            yield return null;

            Button produce = Control(NebulaCombatPage.LaserProductionControlName);
            Assert.IsNotNull(produce, "the combat page has no Laser production control");

            // Form I has one active hardpoint and the birth Laser is already in it, so a
            // full hold is not enough — the player has to free the slot first, and the
            // page must say so rather than offering a button that refuses the click.
            Assert.IsFalse(produce.interactable,
                "the production control is live with no empty hardpoint; clicking it can "
                + "only be refused, which is the dead control this redesign removes");

            Button release = Control(NebulaCombatPage.ReleaseControlName + "0");
            Assert.IsNotNull(release, "the combat page has no hardpoint release control");
            Assert.IsTrue(release.interactable,
                "the occupied hardpoint cannot be released outside combat, so the loadout "
                + "is frozen for the whole campaign");
            release.onClick.Invoke();
            yield return null;

            Assert.IsTrue(produce.interactable,
                "the slot is free and the hold is full, but production is still refused");

            produce.onClick.Invoke();
            yield return null;

            Assert.IsNotNull(combat.ActiveProductionJob,
                "clicking the production control started no job on the ship");
            Assert.AreEqual(WeaponFamily.Laser, combat.ActiveProductionJob.Family,
                "the control started a job for a family it does not name");

            combat.CancelProduction();
        }

        /// <summary>
        /// Focus on open, even in the state that used to lose it.
        /// <para>
        /// A running production job disables all three family buttons at once, and the
        /// page's focus method used to select the Laser button or nothing. So opening
        /// Savaş mid-production left the EventSystem with no selection — and Unity
        /// navigates outwards from the selection, so the arrows and the stick did
        /// nothing at all. Asserted with a job actually running, because with no job
        /// the old code passed.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator CombatPage_EstablishesFocusItself_EvenWhileProductionIsBusy()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            FillCargo(context.Ship.GetComponent<ShipLogistics>().Cargo);

            var combat = context.Ship.GetComponent<ShipCombat>();
            // Form I's single active hardpoint holds the birth Laser, so a job cannot
            // start until it is freed. Through the real service, exactly as the page's
            // own SÖK control does — the fixture is setting up the busy state, not
            // inventing a way around the loadout rule.
            Assert.AreEqual(HardpointReleaseRejectReason.None,
                WeaponLoadoutService.TryRelease(combat.ActiveProductionJob, combat.Hardpoints[0], combat.InCombat),
                "the fixture could not free the hardpoint the busy state needs");
            Assert.AreEqual(WeaponProductionRejectReason.None,
                combat.TryStartProduction(WeaponFamily.Laser, 1),
                "the fixture could not put the queue in the busy state it is about to measure");

            // Nothing selected going in, and the fixture never selects anything: if a
            // selection exists after the open, production established it.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            NebulaCombatPage page = OpenPage<NebulaCombatPage>(NebulaUiAssetCatalog.Page.Combat);
            yield return null;

            Assert.IsNotNull(EventSystem.current, "there is no EventSystem to hold a selection");
            GameObject focused = EventSystem.current.currentSelectedGameObject;
            Assert.IsNotNull(focused,
                "the Combat page established no focus while production was busy; "
                + "keyboard and gamepad have nothing to navigate from");

            var selectable = focused.GetComponent<Selectable>();
            Assert.IsNotNull(selectable, "the focused object is not a Selectable");
            Assert.IsTrue(selectable.IsInteractable(),
                $"focus landed on {focused.name}, which the player cannot use");

            combat.CancelProduction();
            Menu().Close();
            yield return null;
        }

        /// <summary>
        /// The HUD context bar's own control, driving the drill seam the key drives.
        /// <para>
        /// The point is the identity of the two paths, not that a build happens: the
        /// widget carries a semantic action id, the bar raises it, and the driver turns
        /// it into <see cref="ShipExtraction.TryBuildOrUpgradeDrill"/> — the same method
        /// <c>NebulaInputRouter.Pressed(extraction.confirm)</c> reaches. If they ever
        /// diverge, one of the two is a second gameplay path.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Drill_TheHudBarsPrimaryControlDeploysTheKit()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            var extraction = context.Ship.GetComponent<ShipExtraction>();
            FillCargo(context.Ship.GetComponent<ShipLogistics>().Cargo);

            yield return ParkOnAScannedPlanet(context);
            Assert.AreSame(ParkedPlanet(context), extraction.TargetPlanet,
                "the ship never noticed the planet it is parked on");

            // The driver polls on its own 0.25s cadence, so this waits on wall-clock
            // time rather than counting frames — a batchmode frame is a fraction of a
            // real one and sixty of them are milliseconds.
            NebulaHudContextBar bar = context.Presentation.HudContextBar;
            Assert.IsNotNull(bar, "the session built no HUD context bar");
            float deadline = Time.realtimeSinceStartup + 10f;
            while (bar.VisibleActionCount == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.Greater(bar.VisibleActionCount, 0, "the context bar never showed anything at a scanned planet");

            Button primary = HudControl("Primary");
            Assert.IsNotNull(primary, "the HUD context bar has no primary control");
            Assert.IsTrue(primary.interactable,
                "the bar's primary control is dead on a scanned planet — it carries no action id");

            int before = ExtractionState.Drills.Count;
            primary.onClick.Invoke();
            yield return null;

            Assert.AreEqual(before + 1, ExtractionState.Drills.Count,
                $"the bar's own control built nothing. Msg={extraction.TransientMessage}");
        }

        /// <summary>
        /// The card refreshes on its own 0.2s cadence; this waits for one on
        /// wall-clock time. Not a frame count — a batchmode frame is a fraction of
        /// a real one, so sixty of them are milliseconds and would time out on a
        /// panel that is working perfectly.
        /// </summary>
        private static IEnumerator WaitForCard(NebulaHudPlanetContext card)
        {
            Assert.IsNotNull(card, "the session built no planet-context panel");
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!card.CommandVisible && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(card.CommandVisible, "the surface-construction card never appeared");

            float settled = Time.realtimeSinceStartup + 0.35f;
            while (Time.realtimeSinceStartup < settled) yield return null;
        }

        /// <summary>
        /// Plasma has three gates of its own and must refuse before touching the
        /// node. With no command centre on the planet at all, the refusal has to
        /// name the missing gate and leave the node empty.
        /// </summary>
        [UnityTest]
        public IEnumerator Plasma_WithoutItsGates_BuildsNothingAndNamesTheGate()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            var extraction = context.Ship.GetComponent<ShipExtraction>();
            FillCargo(context.Ship.GetComponent<ShipLogistics>().Cargo);
            ProgressionService.Award("test.parity-plasma", 500000);

            yield return ParkOnAScannedPlanet(context);
            PlanetIdentity planet = ParkedPlanet(context);

            // Node 1 is Plasma; node 0 is the existing Laser.
            // Through the same public stepper the planet card's node control and the
            // two bracket keys use. The property's setter is private and this used to
            // be a reflection SetValue -- which also meant no player could ever select
            // the Plasma node, because nothing on screen could call what the test was
            // reaching around.
            Assert.AreEqual(0, extraction.SelectedDefenseNodeIndex);
            extraction.StepDefenseNode(1);
            Assert.AreEqual(1, extraction.SelectedDefenseNodeIndex);

            int before = ExtractionState.Structures.Count;
            yield return ClickDefenseControl(context);
            yield return null;

            Assert.AreEqual(before, ExtractionState.Structures.Count,
                "plasma was built with none of its gates met");
            Assert.AreEqual("KOMUTA MERKEZİ T2 GEREKLİ", extraction.TransientMessage,
                "the refusal did not name the gate that is missing");
            Assert.IsNull(ExtractionState.FindStructure(planet.WorldId, SurfaceStructureType.Plasma));
        }

        /// <summary>
        /// The laser needs an active command centre on the same planet. Without
        /// one it refuses and says so, and the node stays empty.
        /// </summary>
        [UnityTest]
        public IEnumerator Laser_WithoutACommandCentre_BuildsNothingAndSaysWhy()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            var extraction = context.Ship.GetComponent<ShipExtraction>();
            FillCargo(context.Ship.GetComponent<ShipLogistics>().Cargo);

            yield return ParkOnAScannedPlanet(context);
            PlanetIdentity planet = ParkedPlanet(context);

            int before = ExtractionState.Structures.Count;
            yield return ClickDefenseControl(context);
            yield return null;

            Assert.AreEqual(before, ExtractionState.Structures.Count);
            Assert.AreEqual("ETKİN BİR KOMUTA MERKEZİ GEREKLİ", extraction.TransientMessage);
            Assert.IsNull(ExtractionState.FindStructure(planet.WorldId, SurfaceStructureType.Laser));
        }

        // =====================================================================
        // 6 — Dominance and reclaim
        // =====================================================================

        /// <summary>
        /// The one control carries both directions and must never act blind. With
        /// nothing selected it is disabled with a reason, and clicking it changes
        /// no dominance state at all.
        /// </summary>
        [UnityTest]
        public IEnumerator Dominance_TheEvacuateControlRefusesWithAReason_AndChangesNothing()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            var evacuation = context.Ship.GetComponent<ShipPlanetEvacuation>();
            Assert.IsNotNull(evacuation, "the ship carries no evacuation driver");

            NebulaPlanetsPage page = OpenPage<NebulaPlanetsPage>(NebulaUiAssetCatalog.Page.Planets);
            page.Select(null);
            page.Refresh();

            Button evacuate = Control("Evacuate");
            Assert.IsNotNull(evacuate);
            Assert.IsFalse(evacuate.interactable, "the control offered an action it cannot take");
            Assert.IsNotNull(page.EvacuateBlockedReason, "and offered no reason");

            evacuate.onClick.Invoke();
            yield return null;

            Assert.IsNull(evacuation.ActiveTransaction,
                "a refused evacuation began a real transaction anyway");

            Menu().Close();
            yield return null;
        }

        /// <summary>
        /// A selection that is not eligible must be refused by the service, not by
        /// the page's opinion of it: the page's blocked reason has to be the
        /// service's own answer.
        /// </summary>
        [UnityTest]
        public IEnumerator Dominance_ThePageQuotesTheServicesOwnRefusal()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            var evacuation = context.Ship.GetComponent<ShipPlanetEvacuation>();

            PlanetIdentity planet = ParkedPlanet(context);
            NebulaPlanetsPage page = OpenPage<NebulaPlanetsPage>(NebulaUiAssetCatalog.Page.Planets);
            page.Select(planet.WorldId);
            page.Refresh();

            // The page drops a selection that has left its list, which is correct
            // and is why the answer is read back from the page rather than assumed.
            if (page.SelectedWorldId == null)
            {
                Assert.AreEqual("BİR GEZEGEN SEÇİN", page.EvacuateBlockedReason,
                    "a dropped selection must say so rather than report a service refusal");
            }
            else
            {
                bool abandoned = ExtractionState.IsPlanetAbandoned(page.SelectedWorldId);
                string expected = abandoned
                    ? (evacuation.CanReclaim(out ReclaimRejectReason reclaim)
                        ? null : ShipPlanetEvacuation.RejectMessage(reclaim))
                    : (evacuation.CanEvacuate(out EvacuationRejectReason evacuate)
                        ? null : ShipPlanetEvacuation.RejectMessage(evacuate));

                Assert.AreEqual(expected, page.EvacuateBlockedReason,
                    "the page invented its own answer instead of quoting the service");
            }

            Menu().Close();
            yield return null;
        }

        /// <summary>
        /// The refusal the player reads has to be in the player's language. Every
        /// other refusal on the redesign is Turkish; these come straight out of
        /// <see cref="ShipPlanetEvacuation.RejectMessage"/> and land on the
        /// planets page verbatim.
        /// </summary>
        [UnityTest]
        public IEnumerator Dominance_TheRefusalsReachThePlayerInTurkish()
        {
            yield return LoadSceneFresh();

            // Words that cannot appear in this game's Turkish copy. Chosen so a
            // Turkish sentence that happens to be pure ASCII ("REZERV DOLU")
            // cannot trip the check.
            string[] englishOnly =
            {
                "MOVE", "CLOSER", "ALREADY", "DETECTED", "ENGAGE", "THREAT",
                "PROGRESS", "ABANDONED", "CONTESTED", "NEEDS", "ACTIVE",
            };

            var english = new List<string>();
            var copies = new List<string>();
            foreach (EvacuationRejectReason reason in
                     System.Enum.GetValues(typeof(EvacuationRejectReason)))
                copies.Add(reason + " → " + ShipPlanetEvacuation.RejectMessage(reason));
            foreach (ReclaimRejectReason reason in
                     System.Enum.GetValues(typeof(ReclaimRejectReason)))
                copies.Add(reason + " → " + ShipPlanetEvacuation.RejectMessage(reason));

            foreach (string entry in copies)
                foreach (string word in englishOnly)
                    if (entry.Contains(" " + word) || entry.EndsWith(word))
                    { english.Add(entry); break; }

            Assert.IsEmpty(english,
                "the evacuation and reclaim refusals reach the planets page in English and are shown " +
                "to the player verbatim by NebulaPlanetsPage.RefreshActions:\n" + string.Join("\n", english) +
                "\nThe project's own pattern for this is a presentation-layer table like " +
                "NebulaDiscoveryPage.AnalysisRejectText or NebulaModuleName.");
        }

        // =====================================================================
        // 7 — Signal analysis, POI gates, bindings screen
        // =====================================================================

        [UnityTest]
        public IEnumerator Discovery_AnalysisStartsExactlyOnce_AndCancelStopsIt()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            NavigationDirector director = context.NavigationDirector;
            Assert.IsNotNull(director);

            string signal = RequireSignalSystem(context, director);

            context.Ship.GetComponent<ShipModules>().Modules.SetTier("exploration.scanner", 1);
            Assert.GreaterOrEqual(director.ScannerTier, 1,
                "the navigation director cannot see the ship's real scanner tier");

            NebulaDiscoveryPage page = OpenPage<NebulaDiscoveryPage>(NebulaUiAssetCatalog.Page.Discovery);
            page.Select(signal);
            page.Refresh();

            Assert.IsNull(page.AnalysisBlockedReason,
                $"the analysis is blocked for a reason the test did not expect: {page.AnalysisBlockedReason}");

            Control("Analyze").onClick.Invoke();
            Assert.IsTrue(director.Analysis.Active, "the analysis never started");
            float remaining = director.Analysis.RemainingSeconds;

            // Pressing again while one runs must not restart or stack it.
            Control("Analyze").onClick.Invoke();
            Assert.IsTrue(director.Analysis.Active);
            Assert.AreEqual(remaining, director.Analysis.RemainingSeconds, 0.001f,
                "a second press restarted the analysis instead of refusing");

            Control("CancelAnalysis").onClick.Invoke();
            Assert.IsFalse(director.Analysis.Active, "cancel left the analysis running");

            Menu().Close();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Discovery_WithoutTheScannerTier_AnalysisIsRefusedAndNothingStarts()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            NavigationDirector director = context.NavigationDirector;

            string signal = RequireSignalSystem(context, director);

            context.Ship.GetComponent<ShipModules>().Modules.SetTier("exploration.scanner", 0);

            NebulaDiscoveryPage page = OpenPage<NebulaDiscoveryPage>(NebulaUiAssetCatalog.Page.Discovery);
            page.Select(signal);
            page.Refresh();

            Assert.AreEqual("TARAYICI T1 GEREKLİ", page.AnalysisBlockedReason);

            Control("Analyze").onClick.Invoke();
            Assert.IsFalse(director.Analysis.Active, "a refused analysis started anyway");
            StringAssert.Contains("TARAYICI", page.StatusText ?? string.Empty,
                "the refusal never reached the screen");

            Menu().Close();
            yield return null;
        }

        /// <summary>
        /// The information gate, on live data: a system known only as a signal is
        /// listed but never named, even though its real name is in the registry.
        /// </summary>
        [UnityTest]
        public IEnumerator Discovery_ASignalIsListedButNeverNamed()
        {
            yield return LoadSceneFresh();
            NavigationDirector director = Ctx().NavigationDirector;

            string signal = RequireSignalSystem(Ctx(), director);

            Assert.IsTrue(director.TryGetSystem(signal, out StarSystemRecord record),
                "the signal has no record behind it");
            string realName = record.DisplayName;

            string shown = NebulaDiscoveryPage.DisplayName(signal, SystemKnowledgeLevel.Signal, director);
            if (!string.IsNullOrEmpty(realName))
                StringAssert.DoesNotContain(realName, shown,
                    "a signal disclosed the real name of a place the player has not found");
            StringAssert.DoesNotContain(signal, shown, "the signal disclosed its own system id");
        }

        /// <summary>
        /// A system the player knows only as a signal.
        /// <para>
        /// A fresh campaign has none — knowledge starts at the home system and
        /// signals arrive from the frontier service over time. Rather than wait
        /// for one, this puts a real neighbouring system at Signal through
        /// <see cref="SystemKnowledgeService.ForceLevel"/>, which is the same
        /// call the save restore path uses. The system is a real one out of the
        /// galaxy's own list, never an invented id.
        /// </para>
        /// </summary>
        private static string RequireSignalSystem(NebulaContext context, NavigationDirector director)
        {
            foreach (KeyValuePair<string, SystemKnowledgeLevel> entry in director.Knowledge.All)
                if (entry.Value == SystemKnowledgeLevel.Signal) return entry.Key;

            string home = context.StartSystemId;
            foreach (StarSystemRecord record in director.SystemsNearShip())
            {
                if (record.SystemId == home) continue;
                director.Knowledge.ForceLevel(record.SystemId, SystemKnowledgeLevel.Signal);
                Assert.AreEqual(SystemKnowledgeLevel.Signal, director.Knowledge.GetLevel(record.SystemId));
                return record.SystemId;
            }

            Assert.Fail("the galaxy offers no system near the ship other than the one it starts in");
            return null;
        }

        // =====================================================================
        // Save/load must not replay yesterday's news
        // =====================================================================

        /// <summary>
        /// A notification is a report of something that just happened. Loading a
        /// save must adopt state silently: replaying the celebration the save was
        /// taken after would tell the player they levelled up twice.
        /// </summary>
        [UnityTest]
        public IEnumerator SaveAndLoad_DoNotReplayAnOldNotification()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Ctx();
            NebulaHudNotifications notifications = context.Presentation.HudNotifications;
            Assert.IsNotNull(notifications, "the HUD carries no notification widget");

            // Something worth announcing, before the save. The celebration counter
            // moves inside the Awarded handler, so it is checked straight away --
            // and it is checked at all because "nothing replayed" is worth nothing
            // if nothing ever played in the first place.
            int celebratedAtStart = notifications.LevelUpsCelebrated;
            ProgressionService.Award("test.parity-notify", 5000);
            Assert.Greater(notifications.LevelUpsCelebrated, celebratedAtStart,
                "the award produced no celebration at all, so a no-replay assertion proves nothing");

            // The notification queue coalesces on a real 2 second window before it
            // plays anything. Counting frames here passed vacuously in batch mode:
            // four frames can be four milliseconds, the notification had not played,
            // and the test then proved that nothing replayed something that had
            // never happened.
            yield return UiFinalPlayMode.Until(() => notifications.NotificationsPlayed > 0,
                $"the notification to reach the screen through its own " +
                $"{NebulaHudNotifications.CoalesceSeconds}s coalescing window", 12f);

            int playedBefore = notifications.NotificationsPlayed;
            int celebratedBefore = notifications.LevelUpsCelebrated;

            context.ExtractionDirector.SaveNow("PARITY TEST SAVE");
            yield return null;
            context.ExtractionDirector.LoadNow();

            // A replayed celebration would be counted synchronously by the load's own
            // Awarded burst; a replayed notification needs the same coalescing window
            // to surface, so the quiet is measured over a window long enough for one
            // to appear in. Real time, because timeScale is whatever the interface
            // last left it at.
            yield return new WaitForSecondsRealtime(NebulaHudNotifications.CoalesceSeconds + 0.5f);

            Assert.AreEqual(playedBefore, notifications.NotificationsPlayed,
                "loading replayed a notification the player had already seen");
            Assert.AreEqual(celebratedBefore, notifications.LevelUpsCelebrated,
                "loading celebrated a level-up the player had already been shown");
        }
    }
}
