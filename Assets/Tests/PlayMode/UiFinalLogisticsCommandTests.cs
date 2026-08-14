using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Sectors;
using Nebula.Ship;
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
    /// The four Logistics Centre operations that had a wired control and no run
    /// behind it: creating a route, dismantling, reading a centre in a system the
    /// ship is not in, and the reservation figure.
    /// <para>
    /// <b>What these are for.</b> <c>UiFinalLogisticsTests</c> already proves the
    /// reading, the upgrade, the deposit and the out-of-range refusal.
    /// <c>ShipHangar.TryCreateRoute</c> and <c>ShipLogisticsCenter.TryDismantle</c>
    /// were wired to controls and never exercised on their success path by anything,
    /// so "the button calls the method" was the whole of the evidence. A refusal path
    /// on its own proves a gate, not a feature.
    /// </para>
    /// <para>
    /// <b>How they command.</b> Every operation asserted below is issued by invoking
    /// a real control's <c>onClick</c>, and every outcome is read back off the
    /// backend — <c>LogisticsState</c>, the vehicle's own <c>Route</c>, the command
    /// ledger, the ship's cargo — never off the screen that just claimed it. Nothing
    /// here writes route or centre state directly; where the fixture has to undo
    /// something between two halves of a comparison it says so at the call site.
    /// </para>
    /// </summary>
    public class UiFinalLogisticsCommandTests
    {
        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slotBackup;

        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slotBackup = UiFinalPlayMode.BackupSlots();
            UiFinalPlayMode.DeleteEverySlot();
        }

        /// <summary>
        /// Every screen closes before anything else happens: a shell still holding the
        /// pause restores <c>InputGate.Suppressed</c> from its own saved value at
        /// whatever moment the next scene load destroys it, which hands the next test
        /// a gated world in which no contextual ship component ever updates again.
        /// </summary>
        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            foreach (NebulaUiShell shell in Object.FindObjectsOfType<NebulaUiShell>(true))
                if (shell != null) shell.Shutdown();

            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();

            LogisticsState.Reset();
            ExtractionState.DestroyAllDrills();
            UiFinalPlayMode.ClearSessionStatics();
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slotBackup);
        }

        // =====================================================================
        // Fixture
        // =====================================================================

        private static Transform MenuLayer() => UiFinalPlayMode.Shell().Root(UiLayer.Menu);

        private static ShipLogisticsCenter ShipSide() =>
            UiFinalPlayMode.Ctx().Ship.GetComponent<ShipLogisticsCenter>();

        private static Button Live(Transform root, string name, string why)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name && button.gameObject.activeInHierarchy) return button;
            Assert.Fail($"no control named \"{name}\" is on screen — {why}");
            return null;
        }

        /// <summary>Every live control of that name, in the order they were built.</summary>
        private static List<Button> AllLive(Transform root, string name)
        {
            var found = new List<Button>();
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name && button.gameObject.activeInHierarchy) found.Add(button);
            return found;
        }

        /// <summary>
        /// A finished centre at the start system's anchor with the ship parked on it,
        /// exactly as <c>UiFinalLogisticsTests</c> builds one: through
        /// <c>LogisticsCenter.Create</c> (the factory the ship's own build command
        /// calls) and then the centre's own clock, never a state poke.
        /// </summary>
        private static IEnumerator GiveTheShipACentre(System.Action<LogisticsCenter> then)
        {
            NebulaContext context = UiFinalPlayMode.Ctx();
            ShipLogisticsCenter shipSide = ShipSide();
            Assert.IsNotNull(shipSide, "the ship carries no logistics interaction component");
            Assert.IsFalse(InputGate.Suppressed,
                "input is gated with no screen open, so no contextual ship component will ever update");

            string systemId = context.StartSystemId;
            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(systemId);
            Assert.IsNotNull(manifest, "the start system has no manifest");

            yield return UiFinalPlayMode.Until(
                () => context.Streaming.TryGetStarTransform(systemId) != null,
                "the start system to finish materialising");
            Transform star = context.Streaming.TryGetStarTransform(systemId);

            Assert.IsTrue(LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out _),
                "the start system has no safe anchor, so no centre can exist in it");

            LogisticsCenter centre = LogisticsCenter.Create(manifest, star, offset, 60, 18, 15);
            centre.Tick(LogisticsCenterCatalog.T1.BuildSeconds + 1f);
            Assert.AreEqual(LogisticsCenterState.Active, centre.Runtime.State,
                "the centre did not finish building");

            context.Ship.transform.position = centre.transform.position;
            yield return UiFinalPlayMode.Until(() => shipSide.CurrentSystemId == systemId,
                $"the ship parked on the anchor to register as being inside {systemId}");
            yield return UiFinalPlayMode.Until(() => shipSide.InRange,
                "the ship parked on the anchor to come into the centre's command range");

            then(centre);
        }

        private static IEnumerator OpenThePanel(System.Action<NebulaFleetPage> then)
        {
            NebulaContext context = UiFinalPlayMode.Ctx();
            NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Fleet), "the fleet page would not open");
            yield return null;

            NebulaFleetPage page = menu.PageOf<NebulaFleetPage>();
            Assert.IsNotNull(page);
            Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.OpenControlName,
                "the fleet page offers no way to reach the logistics centre").onClick.Invoke();
            yield return null;

            Assert.IsTrue(page.LogisticsPanelOpen, "the logistics control opened nothing");
            then(page);
        }

        /// <summary>
        /// An Active drill on a real planet of the ship's own system, restored the
        /// same way a save restore builds one. Fixture, not the thing under test: what
        /// is under test is the route the panel then creates from it.
        /// </summary>
        private static DrillStructure GiveTheSystemADrill(string systemId)
        {
            foreach (PlanetIdentity planet in ExtractionState.AllPlanets)
            {
                if (planet == null) continue;
                if (WorldIdCodec.SystemIdFromPlanetWorldId(planet.WorldId) != systemId) continue;
                if (LogisticsState.FindRouteByDrill(planet.WorldId) != null) continue;

                PlanetProfile profile = ExtractionState.GetProfile(planet);
                if (profile == null) continue;
                if (ExtractionState.IsNodeOccupied(planet.WorldId, profile.DrillNode.Id)) continue;

                var runtime = new DrillRuntime(profile, DrillLifecycle.Active, 0f, 0f, 0f, 0f);
                DrillStructure drill = DrillStructure.Restore(planet, profile, runtime,
                    DrillStructure.BuildSource.Kit, 0);
                Assert.AreEqual(DrillLifecycle.Active, drill.Runtime.State,
                    "the fixture's drill is not Active, so no route could ever be created from it");
                return drill;
            }

            Assert.Fail($"{systemId} has no planet with a free drill node, so this test has no route source");
            return null;
        }

        /// <summary>
        /// A Collector sitting in the hangar, through the hangar's own restore seam —
        /// the same call the save path makes — plus the command capacity a Hangar-Bot
        /// T1 grants, applied through the module-tier effect rather than by writing
        /// the ledger.
        /// </summary>
        private static VehicleAgent GiveTheHangarACollector(ShipHangar hangar, string id = "test-collector-1")
        {
            hangar.ApplyHangarTier(1);
            Assert.Greater(hangar.Command.Capacity, 0,
                "a Hangar-Bot T1 grants no command capacity, so no route can ever be created");

            VehicleAgent collector = hangar.RestoreVehicle(id, VehicleCatalog.Collector);
            Assert.IsNotNull(collector);
            Assert.IsTrue(collector.Mission.CanLaunch, "the restored Collector is not idle in the hangar");
            return collector;
        }

        // =====================================================================
        // 1. Creating a route — the success path TryCreateRoute never had
        // =====================================================================

        [UnityTest]
        public IEnumerator AssignRoute_CreatesTheRealRoute_WithTheRightVehicleSourceAndDestination()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaContext context = UiFinalPlayMode.Ctx();
            ShipHangar hangar = context.Hangar;
            VehicleAgent collector = GiveTheHangarACollector(hangar);
            DrillStructure drill = GiveTheSystemADrill(centre.SystemId);
            yield return null;

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            // The vehicle the route is built from is the fleet page's own selection —
            // the panel reads it through the same accessor the page publishes.
            page.Select(collector.VehicleId);
            page.Logistics.Refresh();

            Assert.AreEqual(0, LogisticsState.Routes.Count, "the fixture already had a route");
            Assert.Greater(page.Logistics.VisibleSources, 0,
                "the panel offers no drill to route from, so its assign control can never be reached");
            int usedBefore = hangar.Command.Used;

            List<Button> assign = AllLive(MenuLayer(),
                UI.Logistics.NebulaLogisticsCenterPanel.AssignRouteControlName);
            Assert.AreEqual(page.Logistics.VisibleSources, assign.Count,
                "the number of assign controls on screen and the number of offered sources disagree");
            Assert.IsTrue(assign[0].interactable,
                $"the assign control is dead with everything in place: {page.Logistics.ReachBlockedReason}");

            assign[0].onClick.Invoke();

            Assert.AreEqual(1, LogisticsState.Routes.Count,
                $"the assign control created no route: {page.Logistics.StatusText}");
            CollectorRoute route = LogisticsState.Routes[0];

            Assert.AreEqual(collector.VehicleId, route.VehicleId, "the route was given a different vehicle");
            Assert.AreEqual(drill.WorldId, route.DrillWorldId, "the route was given a different source");
            Assert.AreEqual(centre.SystemId, route.SystemId, "the route was filed under a different system");
            Assert.AreEqual(RoutePhase.ToSource, route.Phase,
                "a freshly created route must start on its way to the source");
            Assert.AreEqual(RouteWaitReason.None, route.WaitReason, "a fresh route starts already waiting");
            Assert.IsFalse(string.IsNullOrEmpty(route.RouteId), "the route has no id");

            // The vehicle knows about it too. A route the backend holds and the
            // vehicle does not is a route nothing will ever drive.
            Assert.AreSame(route, collector.Route, "the vehicle was not told about its own route");
            Assert.AreSame(route, LogisticsState.FindRouteByVehicle(collector.VehicleId));
            Assert.AreSame(route, LogisticsState.FindRouteByDrill(drill.WorldId));

            Assert.AreEqual(usedBefore + VehicleCatalog.Collector.CommandCost, hangar.Command.Used,
                "the route did not reserve the vehicle's own command cost");

            StringAssert.Contains("ROTA", page.Logistics.StatusText ?? string.Empty,
                "a route was created and the player was told nothing");
        }

        /// <summary>
        /// The same drill and the same vehicle cannot be routed twice. Both are real
        /// backend refusals with their own reason, and both must reach the screen as
        /// that reason rather than as a silent second route.
        /// </summary>
        [UnityTest]
        public IEnumerator AssignRoute_RefusesADuplicate_AndRefusesAnOccupiedVehicle()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaContext context = UiFinalPlayMode.Ctx();
            ShipHangar hangar = context.Hangar;
            VehicleAgent collector = GiveTheHangarACollector(hangar);
            DrillStructure first = GiveTheSystemADrill(centre.SystemId);
            DrillStructure second = GiveTheSystemADrill(centre.SystemId);
            Assert.AreNotEqual(first.WorldId, second.WorldId,
                "the fixture produced one drill twice, so the occupied-vehicle case cannot be reached");
            yield return null;

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);
            page.Select(collector.VehicleId);
            page.Logistics.Refresh();

            int sourcesBefore = page.Logistics.VisibleSources;
            Assert.GreaterOrEqual(sourcesBefore, 2, "both fixture drills should be offered as sources");

            AllLive(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.AssignRouteControlName)[0]
                .onClick.Invoke();
            Assert.AreEqual(1, LogisticsState.Routes.Count, "the first route was not created");
            string routedDrill = LogisticsState.Routes[0].DrillWorldId;

            // The routed drill stops being offered at all, which is the panel's own
            // half of "no duplicate": there is no control left to press twice.
            Assert.AreEqual(sourcesBefore - 1, page.Logistics.VisibleSources,
                "a drill that already has a route is still offered as a route source");

            // The remaining drill is a genuinely valid source. What is no longer valid
            // is the vehicle, which is already on a route -- so this is the backend's
            // VehicleAlreadyRouted refusal and nothing else.
            Assert.AreEqual(UI.Logistics.NebulaLogisticsCenterPanel.RouteRejectText(
                    RouteCreateRejectReason.VehicleAlreadyRouted),
                ExpectedRefusalFor(hangar, collector, OtherDrill(routedDrill), centre),
                "the backend refuses the second assignment for some other reason than the vehicle");

            AllLive(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.AssignRouteControlName)[0]
                .onClick.Invoke();

            Assert.AreEqual(1, LogisticsState.Routes.Count,
                "a second route was created for a vehicle that already had one");
            Assert.AreEqual(UI.Logistics.NebulaLogisticsCenterPanel.RouteRejectText(
                    RouteCreateRejectReason.VehicleAlreadyRouted),
                page.Logistics.StatusText,
                "the panel invented its own refusal instead of quoting the backend's");

            // And with nothing selected at all the panel refuses before it reaches the
            // backend, without creating anything.
            page.Select(null);
            page.Logistics.Refresh();
            AllLive(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.AssignRouteControlName)[0]
                .onClick.Invoke();
            Assert.AreEqual(1, LogisticsState.Routes.Count,
                "a route was created with no vehicle selected");
            StringAssert.Contains("TOPLAYICI", page.Logistics.StatusText ?? string.Empty,
                "assigning with nothing selected did not say a Collector has to be picked first");
        }

        private static string OtherDrill(string routedDrillWorldId)
        {
            foreach (DrillStructure drill in ExtractionState.Drills)
                if (drill != null && drill.WorldId != routedDrillWorldId) return drill.WorldId;
            return null;
        }

        /// <summary>
        /// What the backend would answer for that assignment, asked without changing
        /// anything — so the assertion above is comparing the panel's text to the real
        /// reason and not to a reason this test picked.
        /// </summary>
        private static string ExpectedRefusalFor(ShipHangar hangar, VehicleAgent vehicle,
            string drillWorldId, LogisticsCenter centre)
        {
            bool vehicleIsCollector = vehicle.Def.Class == VehicleClass.Collector;
            bool vehicleEligible = vehicle.Mission.CanLaunch && vehicle.EngineerTask == null
                                   && vehicle.EscortState == null && vehicle.ResupplyTask == null;
            bool vehicleAlreadyRouted = LogisticsState.FindRouteByVehicle(vehicle.VehicleId) != null;
            bool drillAlreadyRouted = LogisticsState.FindRouteByDrill(drillWorldId) != null;

            DrillStructure drill = null;
            foreach (DrillStructure d in ExtractionState.Drills)
                if (d != null && d.WorldId == drillWorldId) drill = d;

            RouteCreateRejectReason reason = CollectorRouteService.CheckCreate(
                centre.Runtime.IsActive,
                LogisticsState.RouteCountForSystem(centre.SystemId), centre.Runtime.Def.MaxRoutes,
                vehicleIsCollector, vehicleEligible, vehicleAlreadyRouted, drillAlreadyRouted,
                drill != null && drill.Runtime.State == DrillLifecycle.Active,
                true, hangar.Command.Capacity - hangar.Command.Used > 0);
            return UI.Logistics.NebulaLogisticsCenterPanel.RouteRejectText(reason);
        }

        [UnityTest]
        public IEnumerator ARouteCreatedFromThePanel_SurvivesASaveAndLoad()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaContext context = UiFinalPlayMode.Ctx();
            VehicleAgent collector = GiveTheHangarACollector(context.Hangar);
            DrillStructure drill = GiveTheSystemADrill(centre.SystemId);
            yield return null;

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);
            page.Select(collector.VehicleId);
            page.Logistics.Refresh();
            AllLive(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.AssignRouteControlName)[0]
                .onClick.Invoke();

            Assert.AreEqual(1, LogisticsState.Routes.Count, "no route to save");
            string routeId = LogisticsState.Routes[0].RouteId;
            string vehicleId = LogisticsState.Routes[0].VehicleId;
            string drillWorldId = LogisticsState.Routes[0].DrillWorldId;
            RoutePhase phase = LogisticsState.Routes[0].Phase;

            ExtractionDirector director = context.ExtractionDirector;
            director.ActiveSlot = NebulaSaveSlotQuery.FirstSlot + 1;
            director.SaveNow("ROUTE PERSISTENCE");
            yield return null;

            director.LoadNow();
            yield return null;

            Assert.AreEqual(1, LogisticsState.Routes.Count,
                "the load produced a different number of routes than were saved");
            CollectorRoute restored = LogisticsState.Routes[0];
            Assert.AreEqual(routeId, restored.RouteId, "the restored route has a different id");
            Assert.AreEqual(vehicleId, restored.VehicleId, "the restored route lost its vehicle");
            Assert.AreEqual(drillWorldId, restored.DrillWorldId, "the restored route lost its source");
            Assert.AreEqual(centre.SystemId, restored.SystemId, "the restored route moved system");
            Assert.AreEqual(phase, restored.Phase, "the restored route came back in a different phase");

            Assert.AreSame(restored, LogisticsState.FindRouteByDrill(drillWorldId),
                "the restored route is not findable by the drill it feeds from");
            Assert.IsNotNull(drill, "the fixture drill vanished");
        }

        /// <summary>
        /// The panel must not change the outcome it is describing. The same command
        /// is issued twice — once through the public backend seam with the panel
        /// closed, once through the panel's own control — and the two resulting
        /// backend states are compared field by field.
        /// </summary>
        [UnityTest]
        public IEnumerator TheRouteIsIdentical_WhetherIssuedThroughThePanelOrTheSeamBehindIt()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaContext context = UiFinalPlayMode.Ctx();
            ShipHangar hangar = context.Hangar;
            VehicleAgent collector = GiveTheHangarACollector(hangar);
            DrillStructure drill = GiveTheSystemADrill(centre.SystemId);
            yield return null;

            // --- closed: the same public seam the panel calls -------------------
            Assert.AreEqual(RouteCreateRejectReason.None,
                hangar.TryCreateRoute(collector, drill, centre, RoutePriority.Normal),
                "the backend refused the route with the panel closed, so there is nothing to compare");

            CollectorRoute viaSeam = LogisticsState.Routes[0];
            (string vehicle, string source, string system, RoutePhase phase, RoutePriority priority) closed =
                (viaSeam.VehicleId, viaSeam.DrillWorldId, viaSeam.SystemId, viaSeam.Phase, viaSeam.Priority);
            int commandUsedClosed = hangar.Command.Used;

            // Put the world back exactly as it was, through the same public
            // bookkeeping the backend itself uses when a route ends. This is the one
            // place the fixture unwinds state, and it unwinds only its own setup.
            LogisticsState.RemoveRoute(viaSeam);
            collector.ClearRoute();
            hangar.Command.Release(collector.Def.CommandCost);
            Assert.AreEqual(0, LogisticsState.Routes.Count, "the unwind left a route behind");

            // --- open: the panel's own control ---------------------------------
            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);
            page.Select(collector.VehicleId);
            page.Logistics.Refresh();
            AllLive(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.AssignRouteControlName)[0]
                .onClick.Invoke();

            Assert.AreEqual(1, LogisticsState.Routes.Count,
                $"the panel created no route: {page.Logistics.StatusText}");
            CollectorRoute viaPanel = LogisticsState.Routes[0];

            Assert.AreEqual(closed.vehicle, viaPanel.VehicleId, "the panel routed a different vehicle");
            Assert.AreEqual(closed.source, viaPanel.DrillWorldId, "the panel routed from a different source");
            Assert.AreEqual(closed.system, viaPanel.SystemId, "the panel filed the route elsewhere");
            Assert.AreEqual(closed.phase, viaPanel.Phase, "the panel started the route in a different phase");
            Assert.AreEqual(closed.priority, viaPanel.Priority, "the panel chose a different priority");
            Assert.AreEqual(commandUsedClosed, hangar.Command.Used,
                "the panel reserved a different amount of command capacity");
        }

        // =====================================================================
        // 2. Dismantling — the success path, and what it must not do twice
        // =====================================================================

        /// <summary>
        /// A centre that may genuinely be dismantled, taken down through the panel's
        /// own control.
        /// <para>
        /// The centre's dismantle is atomic and has no progress of its own:
        /// <c>DismantleNow</c> refunds, flips the runtime to <c>Dismantled</c> and
        /// destroys the object in one call, so there is no intermediate state to
        /// watch. That is asserted rather than assumed — a dismantle that silently
        /// grew a duration would leave a live centre behind and this would go red.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Dismantle_RefundsTheCatalogsOwnShare_AndLeavesNothingBehind()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);
            string systemId = centre.SystemId;
            GameObject centreObject = centre.gameObject;

            LogisticsCargo cargo = UiFinalPlayMode.Ctx().Ship.GetComponent<ShipLogistics>().Cargo;
            cargo.Clear();
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 5000f));

            Assert.IsTrue(centre.CanDismantle(),
                "the fixture's centre cannot be dismantled, so this test would only prove the refusal again");

            var expected = new Dictionary<string, float>();
            foreach ((string itemId, float amount) in centre.Runtime.Def.Cost())
                expected[itemId] = Mathf.Floor(amount * LogisticsCenterCatalog.DismantleRefundFraction);
            Assert.Greater(expected.Count, 0, "a T1 centre costs nothing, so there is no refund to check");

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            Button dismantle = Live(MenuLayer(),
                UI.Logistics.NebulaLogisticsCenterPanel.DismantleControlName, "no dismantle control");
            Assert.IsTrue(dismantle.interactable,
                $"the dismantle control is dead on a dismantlable centre: {page.Logistics.ReachBlockedReason}");

            dismantle.onClick.Invoke();

            Assert.AreEqual(LogisticsCenterState.Dismantled, centre.Runtime.State,
                "the dismantle control did not take the centre down");
            foreach (KeyValuePair<string, float> kv in expected)
                Assert.AreEqual(kv.Value, cargo.GetStored(kv.Key), 0.01f,
                    $"{kv.Key} was refunded at something other than the catalog's own " +
                    $"{LogisticsCenterCatalog.DismantleRefundFraction:P0}");
            StringAssert.Contains("SÖKÜLDÜ", page.Logistics.StatusText ?? string.Empty,
                "a centre was dismantled and the player was told nothing");

            // The object itself goes on a deferred Destroy, like every other
            // structure here, so the census is taken after it has flushed.
            yield return UiFinalPlayMode.Frames(2);

            Assert.IsTrue(centreObject == null, "the dismantled centre's GameObject is still alive");
            Assert.IsNull(LogisticsState.FindCenter(systemId),
                "the dismantled centre is still registered in LogisticsState");
            foreach (LogisticsCenter alive in LogisticsState.Centers)
                Assert.AreNotEqual(systemId, alive?.SystemId, "a second centre is registered for that system");

            // No orphan presenter: the mechanical presenter lives on the centre's own
            // GameObject, so a surviving one is a surviving centre nobody can see.
            foreach (EconomyMechanicalPresenter presenter
                     in Object.FindObjectsOfType<EconomyMechanicalPresenter>(true))
                Assert.IsFalse(presenter != null && presenter.gameObject.name.Contains("Logistics Center"),
                    "a logistics-centre presenter outlived the centre it was drawing");

            // The screen has to have caught up too, not merely the backend.
            page.Logistics.Refresh();
            Assert.AreEqual(0, page.Logistics.VisibleCenters,
                "the panel still lists a centre that no longer exists");
            Assert.IsNull(page.Logistics.SelectedSystemId,
                "the panel is still acting on the centre it just dismantled");
        }

        [UnityTest]
        public IEnumerator Dismantle_RefundsExactlyOnce_AndTheCentreDoesNotComeBackFromASave()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);
            string systemId = centre.SystemId;

            NebulaContext context = UiFinalPlayMode.Ctx();
            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            cargo.Clear();
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 5000f));

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);
            Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.DismantleControlName,
                "no dismantle control").onClick.Invoke();
            yield return UiFinalPlayMode.Frames(2);

            var afterOnce = new Dictionary<string, float>();
            foreach ((string itemId, float _) in LogisticsCenterCatalog.T1.Cost())
                afterOnce[itemId] = cargo.GetStored(itemId);

            // A second attempt has nothing to act on. It must not pay again, and it
            // must not throw: the seam is asked directly here because the control is
            // gone from the screen along with its row, which is itself the answer.
            Assert.IsFalse(ShipSide().TryDismantle(),
                "dismantling a centre that no longer exists reported success");
            foreach (KeyValuePair<string, float> kv in afterOnce)
                Assert.AreEqual(kv.Value, cargo.GetStored(kv.Key), 0.01f,
                    $"{kv.Key} was refunded a second time");

            // And it stays gone across a save and a load. A dismantled centre that
            // comes back is a duplicate refund waiting to happen.
            ExtractionDirector director = context.ExtractionDirector;
            director.ActiveSlot = NebulaSaveSlotQuery.FirstSlot + 1;
            director.SaveNow("DISMANTLED CENTRE");
            yield return null;
            director.LoadNow();
            yield return null;

            Assert.IsNull(LogisticsState.FindCenter(systemId),
                "a dismantled centre came back from a save");
            page.Logistics.Refresh();
            Assert.AreEqual(0, page.Logistics.VisibleCenters,
                "the panel lists a centre the load did not restore");
        }

        /// <summary>
        /// The state gate, reached through the real flow rather than by asking
        /// <c>CanDismantle</c>: a centre holding cargo refuses, and putting the cargo
        /// back where it belongs — through the panel's own withdraw control — makes it
        /// dismantlable again.
        /// </summary>
        [UnityTest]
        public IEnumerator Dismantle_IsRefusedWhileTheCentreHoldsCargo_AndAllowedOnceItIsEmptied()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            LogisticsCargo cargo = UiFinalPlayMode.Ctx().Ship.GetComponent<ShipLogistics>().Cargo;
            cargo.Clear();
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 5000f));

            string item = ExtractionCatalog.ResourceIds[0];
            centre.Inventory.Add(item, LogisticsCenterCatalog.T1.TransferVolumePerSecond);
            centre.Tick(0.1f);
            Assert.IsFalse(centre.CanDismantle(), "a centre holding cargo reported itself dismantlable");

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            Button dismantle = Live(MenuLayer(),
                UI.Logistics.NebulaLogisticsCenterPanel.DismantleControlName, "no dismantle control");
            Assert.IsFalse(dismantle.interactable, "a centre holding cargo offers a live dismantle control");

            // A blocked control's reason is not drawn beside it — it is carried on the
            // control's own tooltip, which is the surface a player reaches by hovering.
            // "Stored" and "reachable" are different claims and only the second one
            // matters, so this drives the real pointer handler and then reads what the
            // tooltip really put on screen.
            var reasonSource = dismantle.GetComponent<NebulaTooltipSource>();
            Assert.IsNotNull(reasonSource,
                "the refused dismantle control carries no way to tell the player why");
            reasonSource.OnPointerEnter(new PointerEventData(EventSystem.current));
            Assert.IsTrue(HasVisibleReading(UiFinalPlayMode.Shell().Root(UiLayer.Overlay), "SÖKÜM İÇİN"),
                "hovering the refused dismantle control showed nothing, so the reason is stored "
                + "and unreachable");
            reasonSource.OnPointerExit(new PointerEventData(EventSystem.current));

            // And the backend's own refusal, through the seam the key press uses, says
            // the same thing and changes nothing.
            Assert.IsFalse(ShipSide().TryDismantle(), "a centre holding cargo reported a dismantle");
            StringAssert.Contains("SÖKÜM İÇİN", ShipSide().StatusMessage ?? string.Empty,
                "the backend refused without saying what has to be true first");
            Assert.AreEqual(LogisticsCenterState.Active, centre.Runtime.State,
                "a refused dismantle took the centre down anyway");

            // Empty it the way a player would: the panel's own withdraw control.
            Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.WithdrawControlName,
                "no withdraw control").onClick.Invoke();
            Assert.IsTrue(centre.Inventory.IsEmpty,
                "one transfer budget did not empty a centre holding exactly one budget");

            page.Logistics.Refresh();
            Assert.IsTrue(Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.DismantleControlName,
                    "no dismantle control").interactable,
                "emptying the centre through the real flow did not make it dismantlable");
        }

        /// <summary>
        /// The one cancel that exists on this surface. A dismantle cannot be
        /// cancelled — it is atomic — but a centre that has not finished building can,
        /// at the catalog's own 75%, and that is the path a player takes when they
        /// change their mind about a centre.
        /// </summary>
        [UnityTest]
        public IEnumerator AnUnfinishedCentre_CancelsBackWithTheCatalogsOwnRefund()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            NebulaContext context = UiFinalPlayMode.Ctx();
            ShipLogisticsCenter shipSide = ShipSide();
            string systemId = context.StartSystemId;
            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(systemId);

            yield return UiFinalPlayMode.Until(
                () => context.Streaming.TryGetStarTransform(systemId) != null,
                "the start system to finish materialising");
            Transform star = context.Streaming.TryGetStarTransform(systemId);
            LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out _);

            int refundParts = Mathf.FloorToInt(
                LogisticsCenterCatalog.T1.PartsCost * LogisticsCenterCatalog.RefundFraction);
            LogisticsCenter centre = LogisticsCenter.Create(manifest, star, offset, refundParts, 0, 0);
            Assert.AreEqual(LogisticsCenterState.Constructing, centre.Runtime.State,
                "a fresh centre is not Constructing, so there is nothing to cancel");
            GameObject centreObject = centre.gameObject;

            context.Ship.transform.position = centre.transform.position;
            yield return UiFinalPlayMode.Until(() => shipSide.InRange, "the ship to reach the anchor");

            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            cargo.Clear();
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 5000f));

            Assert.IsTrue(shipSide.TryCancelBuild(), "an unfinished centre refused to cancel");
            Assert.AreEqual(refundParts, cargo.GetStored(ProcessingCatalog.BasicPartId), 0.01f,
                "cancelling an unfinished build did not refund the recorded share");

            yield return UiFinalPlayMode.Frames(2);
            Assert.IsTrue(centreObject == null, "a cancelled build left its centre standing");
            Assert.IsNull(LogisticsState.FindCenter(systemId),
                "a cancelled build left its centre registered");
        }

        // =====================================================================
        // 3. Reservations — the figure that could never move
        // =====================================================================

        /// <summary>
        /// A centre's inventory is a <c>LogisticsCargo</c> and therefore carries the
        /// reservation API, and nothing in the game ever reserves into one. This walks
        /// the operations that plausibly would — a deposit, a withdraw, an upgrade
        /// started and cancelled — and holds the invariant afterwards, so the claim
        /// the screen makes is a measured one rather than a comment.
        /// </summary>
        [UnityTest]
        public IEnumerator NoRealOperation_EverReservesInsideACentresInventory()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaContext context = UiFinalPlayMode.Ctx();
            LogisticsCargo shipCargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            shipCargo.Clear();
            shipCargo.SetCapacity(Mathf.Max(shipCargo.Capacity, 8000f));
            shipCargo.Add(ExtractionCatalog.ResourceIds[0], 300f);

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.DepositControlName,
                "no deposit control").onClick.Invoke();
            AssertNothingIsReserved(centre, "a deposit");

            Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.WithdrawControlName,
                "no withdraw control").onClick.Invoke();
            AssertNothingIsReserved(centre, "a withdraw");

            MakeTheUpgradeLegal(context, centre.SystemId);
            page.Logistics.Refresh();
            Assert.AreEqual(CenterUpgradeRejectReason.None, ShipSide().CheckUpgrade(),
                "the fixture could not make the upgrade legal, so that branch is unexercised");

            Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.UpgradeControlName,
                "no upgrade control").onClick.Invoke();
            Assert.IsTrue(centre.Runtime.Upgrading, "the upgrade did not start");
            AssertNothingIsReserved(centre, "an upgrade that is running");

            Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.CancelUpgradeControlName,
                "no cancel-upgrade control").onClick.Invoke();
            AssertNothingIsReserved(centre, "a cancelled upgrade");

            // Something back in the centre, so the contents line has contents to
            // describe — an empty centre says "İÇERİK: BOŞ" and nothing else, which
            // is correct and is not what this assertion is about.
            Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.DepositControlName,
                "no deposit control").onClick.Invoke();
            Assert.IsFalse(centre.Inventory.IsEmpty,
                "the fixture could not put anything back in the centre to describe");
            AssertNothingIsReserved(centre, "a second deposit");

            // The screen says the design fact rather than drawing a zero.
            Assert.IsTrue(HasVisibleReading(MenuLayer(), "REZERVASYON KULLANMAZ"),
                "the panel does not state that this centre does not use reservations");
            Assert.IsFalse(HasVisibleReading(MenuLayer(), "REZERVE: 0"),
                "the panel draws a reserved figure that can never move");
        }

        /// <summary>
        /// And the line is not hard-coded. A reservation made through the real
        /// <c>LogisticsCargo</c> API — the same call every production reservation
        /// site makes, here against the centre's own inventory, which is precisely
        /// what nothing in the game does — has to show up on the screen.
        /// <para>
        /// This is the guard on the sentence above: without it, "this centre does not
        /// use reservations" would be a string the panel prints unconditionally, and
        /// the day something does reserve here the screen would keep saying it
        /// doesn't.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator IfSomethingEverDidReserveInACentre_TheScreenWouldReportIt()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            string item = ExtractionCatalog.ResourceIds[0];
            centre.Inventory.Add(item, 100f);

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);
            Assert.IsTrue(HasVisibleReading(MenuLayer(), "REZERVASYON KULLANMAZ"),
                "with nothing reserved the panel does not state the design fact");

            Assert.IsTrue(centre.Inventory.TryReserve("test.what-a-writer-would-do", item, 40f),
                "the centre's inventory refused a reservation the real API should accept");

            page.Logistics.Refresh();
            Assert.IsTrue(HasVisibleReading(MenuLayer(), "REZERVE:"),
                "a real reservation inside the centre's inventory never reached the screen");
            Assert.IsFalse(HasVisibleReading(MenuLayer(), "REZERVASYON KULLANMAZ"),
                "the screen kept claiming this centre does not use reservations while one was held");

            centre.Inventory.ReleaseReservation("test.what-a-writer-would-do");
        }

        private static void AssertNothingIsReserved(LogisticsCenter centre, string after)
        {
            foreach (KeyValuePair<string, float> item in centre.Inventory.Items)
                Assert.AreEqual(0f, centre.Inventory.GetReserved(item.Key), 0.0001f,
                    $"{after} left {item.Key} reserved inside the centre's own inventory — " +
                    "the panel's statement that this centre does not use reservations is now false");
        }

        // =====================================================================
        // 4. A centre in a system the ship is not in
        // =====================================================================

        /// <summary>
        /// The communication gate, driven from genuinely different systems.
        /// <para>
        /// The gate was unreachable before this: the panel only ever listed the
        /// streamed-in system's live centre, so <c>SystemStatusQuery.Link</c> was
        /// always asked about the ship's own system and always answered
        /// <c>Live</c>. What makes it reachable is that a remote centre is a real
        /// thing the backend already carries — the <c>LogisticsCenterDto</c> inside
        /// its system's <c>SystemDelta</c> — and the panel now reads it.
        /// </para>
        /// <para>
        /// Nothing here writes a delta. The ship flies into a second system, which is
        /// what makes the streaming director demote the first one, and the demotion's
        /// own capture is what produces the record the panel then reads. Every state
        /// below is one production code path away from a player doing the same thing.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ACentreInAnotherSystem_ReadsAsLastKnown_AndRefusesEveryCommand()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaContext context = UiFinalPlayMode.Ctx();
            string homeSystemId = centre.SystemId;

            string item = ExtractionCatalog.ResourceIds[0];
            centre.Inventory.Add(item, 90f);
            centre.Tick(0.1f);
            float storedBefore = centre.Inventory.Total;
            LogisticsCenterTier tierBefore = centre.Runtime.Tier;

            // A second real system. Flying into its navigation radius is what demotes
            // the first one, and demotion is what captures its delta.
            StarSystemRecord other = FindAnotherSystem(homeSystemId);
            context.Ship.transform.position = other.AbsoluteCenter - FloatingOrigin.TotalOffset;

            yield return UiFinalPlayMode.Until(
                () => context.Streaming.GetState(homeSystemId) != SystemStreamingState.Full,
                "the start system to stream out once the ship reached another one");
            yield return UiFinalPlayMode.Until(() => ShipSide().CurrentSystemId == other.SystemId,
                $"the ship to register as being inside {other.SystemId}");

            // Destroy is deferred in play mode and the centre unregisters from its
            // own OnDestroy, so this is a wait and not an instant.
            yield return UiFinalPlayMode.Until(() => LogisticsState.FindCenter(homeSystemId) == null,
                "the demoted system's centre to be torn down with its system");

            SystemDelta delta = StarSystemRegistry.Deltas.TryGet(homeSystemId);
            Assert.IsNotNull(delta, "streaming the system out captured no delta at all");
            Assert.Greater(delta.logisticsCenters.Count, 0,
                "the demotion captured a delta with no logistics centre in it, so there is nothing remote to read");

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            // Still listed, and listed as what it is.
            Assert.AreEqual(1, page.Logistics.VisibleCenters,
                "the centre disappeared from the list the moment its system streamed out");
            Assert.AreEqual(homeSystemId, page.Logistics.SelectedSystemId);
            Assert.AreEqual(false, page.Logistics.SelectedIsMaterialized,
                "a centre read from a delta is being presented as live state");
            Assert.AreEqual(SystemLinkState.LastKnown, page.Logistics.SelectedLink,
                "the link to a system with a snapshot and no relay is not LastKnown");

            // The reading is the real stored one, not a blank.
            Assert.IsTrue(HasVisibleReading(MenuLayer(), $"{storedBefore:0} / {LogisticsCenterCatalog.T1.Capacity:0}"),
                "the last-known reading does not quote what the centre was holding");
            Assert.IsTrue(HasVisibleReading(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.TierName(tierBefore)),
                "the last-known reading does not quote the centre's tier");
            Assert.IsTrue(HasVisibleReading(MenuLayer(), "SON BİLİNEN"),
                "a last-known reading is not labelled as one");

            // And nothing may be commanded, with a reason that is about the link.
            Assert.IsNotNull(page.Logistics.ReachBlockedReason);
            StringAssert.Contains("SON BİLİNEN", page.Logistics.ReachBlockedReason,
                "a LastKnown centre must refuse as a communication problem");

            foreach (string control in new[]
                     {
                         UI.Logistics.NebulaLogisticsCenterPanel.UpgradeControlName,
                         UI.Logistics.NebulaLogisticsCenterPanel.DepositControlName,
                         UI.Logistics.NebulaLogisticsCenterPanel.WithdrawControlName,
                         UI.Logistics.NebulaLogisticsCenterPanel.DismantleControlName,
                     })
                Assert.IsFalse(Live(MenuLayer(), control, $"the panel has no {control}").interactable,
                    $"{control} is live on a centre the ship has no live link to");

            Assert.AreEqual(0, page.Logistics.VisibleSources,
                "route sources are offered for a system that is not even streamed in");
        }

        /// <summary>
        /// The third state. A ship outside every system's navigation radius has no
        /// link to anything — the resolver's Live case needs a Command relay at both
        /// ends and there is no "this end" — so the same centre reads Disconnected
        /// and refuses with a different sentence.
        /// </summary>
        [UnityTest]
        public IEnumerator FromDeepSpace_TheSameCentreReadsAsDisconnected()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaContext context = UiFinalPlayMode.Ctx();
            string homeSystemId = centre.SystemId;

            StarSystemRecord other = FindAnotherSystem(homeSystemId);
            context.Ship.transform.position = other.AbsoluteCenter - FloatingOrigin.TotalOffset;
            yield return UiFinalPlayMode.Until(
                () => context.Streaming.GetState(homeSystemId) != SystemStreamingState.Full,
                "the start system to stream out");
            yield return UiFinalPlayMode.Until(() => LogisticsState.FindCenter(homeSystemId) == null,
                "the demoted system's centre to be torn down with its system");

            // Now out of every system: a point verified empty before the ship is put
            // there, so "no system" is a fact and not a hope.
            Vector3 empty = FindEmptySpaceNear(other);
            context.Ship.transform.position = empty - FloatingOrigin.TotalOffset;
            yield return UiFinalPlayMode.Until(() => ShipSide().CurrentSystemId == null,
                "the ship to be outside every system's navigation radius");

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            Assert.AreEqual(1, page.Logistics.VisibleCenters,
                "the last-known centre stopped being listed in deep space");
            Assert.AreEqual(SystemLinkState.Disconnected, page.Logistics.SelectedLink,
                "a ship with no system of its own still claims a link");
            StringAssert.Contains("BAĞLANTI YOK", page.Logistics.ReachBlockedReason ?? string.Empty,
                "a disconnected centre must refuse as being out of the command network, " +
                "not as stale data the player could refresh by waiting");

            Assert.IsFalse(Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.DepositControlName,
                "no deposit control").interactable, "a disconnected centre offers a live transfer");
        }

        /// <summary>
        /// And the Live case is still Live: the ship's own system, which is the
        /// resolver's first branch and the only situation in which the panel lets a
        /// command through at all.
        /// </summary>
        [UnityTest]
        public IEnumerator TheShipsOwnSystem_ReadsAsLive_AndIsTheOnlyOneThatMayBeCommanded()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            Assert.AreEqual(SystemLinkState.Live, page.Logistics.SelectedLink,
                "the system the ship is standing in does not read as Live");
            Assert.AreEqual(true, page.Logistics.SelectedIsMaterialized);
            Assert.IsNull(page.Logistics.ReachBlockedReason,
                $"a live, in-range centre is gated: {page.Logistics.ReachBlockedReason}");

            Assert.IsTrue(Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.DepositControlName,
                "no deposit control").interactable, "a Live centre in range refuses a transfer");
            Assert.IsTrue(HasVisibleReading(MenuLayer(), "CANLI VERİ"),
                "live state is not labelled as live");
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static bool HasVisibleReading(Transform root, string needle)
        {
            foreach (TMPro.TextMeshProUGUI text in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                if (!text.gameObject.activeInHierarchy) continue;
                if (!string.IsNullOrEmpty(text.text) && text.text.Contains(needle)) return true;
            }
            return false;
        }

        /// <summary>
        /// A real second system, found through the registry's own neighbourhood query
        /// rather than invented. Fails loudly rather than skipping: a galaxy with one
        /// reachable system would make every remote assertion in this file vacuous.
        /// </summary>
        private static StarSystemRecord FindAnotherSystem(string excluding)
        {
            StarSystemRegistry.TryGet(excluding, out StarSystemRecord home);
            StarSystemRecord best = default;
            float bestDistance = float.MaxValue;

            foreach (StarSystemRecord candidate in StarSystemRegistry.SystemsNear(home.AbsoluteCenter))
            {
                if (candidate.SystemId == excluding) continue;
                float d = Vector3.Distance(candidate.AbsoluteCenter, home.AbsoluteCenter);
                if (d >= bestDistance) continue;
                best = candidate;
                bestDistance = d;
            }

            Assert.IsFalse(string.IsNullOrEmpty(best.SystemId),
                $"no system other than {excluding} exists in its own sector or the 26 around it, " +
                "so a remote centre cannot be produced and every assertion about one would be vacuous");
            return best;
        }

        /// <summary>
        /// A point outside every system's navigation radius, verified before it is
        /// used. Searched outward rather than guessed, and asserted rather than
        /// assumed, because a "deep space" test that quietly lands inside a system
        /// proves the wrong link state and still goes green.
        /// </summary>
        private static Vector3 FindEmptySpaceNear(StarSystemRecord anchor)
        {
            for (int step = 2; step <= 64; step *= 2)
            {
                foreach (Vector3 direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    Vector3 probe = anchor.AbsoluteCenter + direction * (anchor.NavigationRadius * step);
                    if (!StarSystemRegistry.TryFindContaining(probe, out _)) return probe;
                }
            }

            Assert.Fail("no point outside every system could be found near " + anchor.SystemId);
            return Vector3.zero;
        }

        /// <summary>
        /// Everything <c>CheckUpgrade</c> asks for, set through the services that own
        /// each answer rather than by reaching into the check itself. The same helper
        /// <c>UiFinalLogisticsTests</c> uses, duplicated rather than shared because
        /// each fixture owns the world it builds.
        /// </summary>
        private static void MakeTheUpgradeLegal(NebulaContext context, string systemId)
        {
            Progression.ProgressionService.Award("test.logistics-command-upgrade",
                (int)Progression.ProgressionMath.TotalXpToReach(LogisticsCenterCatalog.T2.RequiredLevel + 1));

            context.Ship.GetComponent<Nebula.Modules.ShipModules>().Modules.SetTier(
                LogisticsCenterCatalog.StationConstructionModuleId,
                LogisticsCenterCatalog.T2.RequiredModuleTier);

            int controlled = 0;
            foreach (PlanetIdentity planet in ExtractionState.AllPlanets)
            {
                if (planet == null) continue;
                if (WorldIdCodec.SystemIdFromPlanetWorldId(planet.WorldId) != systemId) continue;
                ExtractionState.GetDominance(planet.WorldId).Restore(DominanceState.Controlled, 0f, 0f);
                if (++controlled >= 2) break;
            }
            Assert.GreaterOrEqual(controlled, 2,
                "the ship's system has fewer than two planets, so the upgrade can never be legal here");

            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 8000f));
            foreach ((string itemId, float amount) in LogisticsCenterCatalog.T2.Cost())
                cargo.Add(itemId, amount * 2f);
        }
    }
}
