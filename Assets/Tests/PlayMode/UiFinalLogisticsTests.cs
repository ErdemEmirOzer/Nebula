using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The System Logistics Centre, through the surface a player can actually reach.
    /// <para>
    /// The VS-011 backend shipped complete and invisible: capacity, fill, docks,
    /// routes, the T2 cost and every refusal existed only behind four contextual
    /// keys, and <c>ShipHangar.TryCreateRoute</c> had no caller in the interface at
    /// all. What these tests hold to is that the panel <b>reads</b> the real backend
    /// and <b>commands</b> it through the same public seam the keys use — never by
    /// writing logistics state itself.
    /// </para>
    /// <para>
    /// The centre itself is created through <c>LogisticsCenter.Create</c>, the same
    /// factory the ship's build command calls. That is fixture setup, not the thing
    /// under test: every operation asserted below is issued by clicking a real
    /// control, and every outcome is read back off the backend rather than off the
    /// screen that just claimed it.
    /// </para>
    /// </summary>
    public class UiFinalLogisticsTests
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
        /// Every screen closes before anything else happens.
        /// <para>
        /// A shell holding the input gate when the test ends does not simply leak a
        /// bool. <c>ReleaseInput</c> restores <c>InputGate.Suppressed</c> to the value that
        /// shell <i>saved</i>, and the next scene load destroys it at a moment nobody
        /// chose — so a test that left the parent menu open hands the next test a
        /// gated world with no screen on it. Nothing looks wrong; every contextual
        /// ship component has simply stopped updating, because they all begin with
        /// <c>if (InputGate.Suppressed) return;</c>. That cost me two Unity runs.
        /// </para>
        /// <para>
        /// <c>Shutdown</c> rather than <c>PopAll</c>, and before <c>EndSession</c>:
        /// the menu host that <c>EndSession</c> builds captures the gate as it finds
        /// it, so the restore has to have happened already or the leak is simply
        /// copied into the replacement shell.
        /// </para>
        /// </summary>
        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            foreach (NebulaUiShell shell in Object.FindObjectsOfType<NebulaUiShell>(true))
                if (shell != null) shell.Shutdown();

            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();

            LogisticsState.Reset();
            UiFinalPlayMode.ClearSessionStatics();
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slotBackup);
        }

        // =====================================================================
        // Fixture: a live centre, and the panel open on it
        // =====================================================================

        private static Transform MenuLayer() => UiFinalPlayMode.Shell().Root(UiLayer.Menu);

        private static Button Live(Transform root, string name, string why)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name && button.gameObject.activeInHierarchy) return button;
            Assert.Fail($"no control named \"{name}\" is on screen — {why}");
            return null;
        }

        private static ShipLogisticsCenter ShipSide() =>
            UiFinalPlayMode.Ctx().Ship.GetComponent<ShipLogisticsCenter>();

        /// <summary>
        /// Builds a centre at the campaign's start-system anchor and parks the ship
        /// on it.
        /// <para>
        /// The system is taken from <c>NebulaContext.StartSystemId</c> rather than
        /// from <c>ShipLogisticsCenter.CurrentSystemId</c>, and the ordering is the
        /// reason: the ship spawns some distance from its star, so the contextual
        /// component reports no system at all until the ship has been moved to the
        /// anchor. Asking it first is asking before there is an answer.
        /// </para>
        /// <para>
        /// The wait afterwards is a real wait on a real condition. The component
        /// skips its entire update while input is gated — which is exactly what an
        /// open menu does — so <c>InRange</c> only becomes true on an un-gated frame,
        /// and that frame is what everything below depends on.
        /// </para>
        /// </summary>
        private static IEnumerator GiveTheShipACentre(System.Action<LogisticsCenter> then)
        {
            NebulaContext context = UiFinalPlayMode.Ctx();
            ShipLogisticsCenter shipSide = ShipSide();
            Assert.IsNotNull(shipSide, "the ship carries no logistics interaction component");
            Assert.IsFalse(InputGate.Suppressed,
                "input is gated with no screen open, so no contextual ship component will ever update");

            string systemId = context.StartSystemId;
            Assert.IsNotNull(systemId, "the campaign composed no start system");

            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(systemId);
            Assert.IsNotNull(manifest, "the start system has no manifest");

            yield return UiFinalPlayMode.Until(
                () => context.Streaming.TryGetStarTransform(systemId) != null,
                "the start system to finish materialising, so its star has a transform to anchor against");
            Transform star = context.Streaming.TryGetStarTransform(systemId);

            Assert.IsTrue(LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out _),
                "the start system has no safe anchor, so no centre can exist in it");

            LogisticsCenter centre = LogisticsCenter.Create(manifest, star, offset, 60, 18, 15);
            Assert.IsNotNull(centre);

            // Finish the build. Tick is the centre's own clock, not a state poke.
            centre.Tick(LogisticsCenterCatalog.T1.BuildSeconds + 1f);
            Assert.AreEqual(LogisticsCenterState.Active, centre.Runtime.State,
                "the centre did not finish building");

            context.Ship.transform.position = centre.transform.position;

            yield return UiFinalPlayMode.Until(() => shipSide.CurrentSystemId == systemId,
                $"the ship parked on the anchor to register as being inside {systemId}");
            yield return UiFinalPlayMode.Until(() => shipSide.InRange,
                "the ship parked on the anchor to come into the centre's command range");
            Assert.AreSame(centre, shipSide.NearbyCenter,
                "the ship resolved a different centre than the one that was just built");

            then(centre);
        }

        /// <summary>Opens the fleet page and, from it, the logistics panel — both through their own controls.</summary>
        private static IEnumerator OpenThePanel(System.Action<NebulaFleetPage> then)
        {
            NebulaContext context = UiFinalPlayMode.Ctx();
            NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Fleet), "the fleet page would not open");
            yield return null;

            NebulaFleetPage page = menu.PageOf<NebulaFleetPage>();
            Assert.IsNotNull(page);
            Assert.IsFalse(page.LogisticsPanelOpen, "the logistics panel is open before anyone asked");

            Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.OpenControlName,
                "the fleet page offers no way to reach the logistics centre").onClick.Invoke();
            yield return null;

            Assert.IsTrue(page.LogisticsPanelOpen, "the logistics control opened nothing");
            then(page);
        }

        private static bool HasVisibleReading(Transform root, string needle)
        {
            foreach (TMPro.TextMeshProUGUI text in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                if (!text.gameObject.activeInHierarchy) continue;
                if (!string.IsNullOrEmpty(text.text) && text.text.Contains(needle)) return true;
            }
            return false;
        }

        // =====================================================================
        // Reading
        // =====================================================================

        [UnityTest]
        public IEnumerator TheCentre_IsListedOnce_WithItsRealTierCapacityAndFill()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            // Real contents, put in through the centre's own inventory the way a
            // collector unload does.
            centre.Inventory.Add(ExtractionCatalog.ResourceIds[0], 150f);
            centre.Tick(0.1f);

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            Assert.AreEqual(1, page.Logistics.VisibleCenters, "the live centre is not listed exactly once");
            Assert.AreEqual(centre.SystemId, page.Logistics.SelectedSystemId,
                "the panel selected a centre that is not the one that exists");

            float expectedFill = centre.Inventory.Total;
            Assert.Greater(expectedFill, 0f, "the fixture put nothing in the centre");

            Assert.IsTrue(HasVisibleReading(MenuLayer(), $"{expectedFill:0} / {LogisticsCenterCatalog.T1.Capacity:0}"),
                "the panel does not show the centre's real fill against its real capacity");
            Assert.IsTrue(HasVisibleReading(MenuLayer(), "T1"), "the panel does not state the centre's tier");
            Assert.IsTrue(HasVisibleReading(MenuLayer(), "ETKİN"), "the panel does not state the centre's state");
            Assert.IsTrue(HasVisibleReading(MenuLayer(),
                    $"DOCK {centre.DocksInUse} / {LogisticsCenterCatalog.T1.DockCount}"),
                "the panel does not show the dock state");
            // Not "RESERVED: 0". Nothing in the game reserves inside a centre's
            // inventory — every TryReserveAll call site targets the ship's own hold
            // or a planet's Maintenance Reserve, and the centre's save record has no
            // reservation field to carry one — so a reserved figure here would be a
            // number that can never move, which a player reads as "nothing is spoken
            // for right now". The line states the contract instead, and
            // UiFinalLogisticsCommandTests holds both halves of it: that no real
            // operation ever reserves here, and that a reservation which somehow did
            // appear would still be reported.
            Assert.IsTrue(HasVisibleReading(MenuLayer(), "REZERVASYON KULLANMAZ"),
                "the panel does not state what this centre does about reservations");
            Assert.IsFalse(HasVisibleReading(MenuLayer(), "REZERVE:"),
                "the panel is back to drawing a reserved figure that can never move");
            Assert.IsTrue(HasVisibleReading(MenuLayer(), "AKTİF ROTA YOK"),
                "the panel says nothing about routes");

            // The upgrade is described before it is taken: what it needs, what it
            // costs and how long it takes.
            Assert.IsTrue(HasVisibleReading(MenuLayer(), LogisticsCenterCatalog.T2.PartsCost.ToString()),
                "the panel does not state what a T2 upgrade costs");
            Assert.IsTrue(HasVisibleReading(MenuLayer(), $"{LogisticsCenterCatalog.T2.BuildSeconds:0}sn"),
                "the panel does not state how long a T2 upgrade takes");
            Assert.IsTrue(HasVisibleReading(MenuLayer(),
                    $"SEVİYE {LogisticsCenterCatalog.T2.RequiredLevel}"),
                "the panel does not state the level a T2 upgrade requires");
        }

        /// <summary>
        /// A centre registered twice — the shape a restore-over-a-live-registration
        /// produces — is still one row and one selection.
        /// </summary>
        [UnityTest]
        public IEnumerator ACentreRegisteredTwice_StillDrawsOneRow()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            // A genuinely second registration for the same system, which is the shape
            // a restore landing on top of a live centre produces. Re-registering the
            // same object proves nothing: LogisticsState already refuses that.
            NebulaContext context = UiFinalPlayMode.Ctx();
            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(centre.SystemId);
            Transform star = context.Streaming.TryGetStarTransform(centre.SystemId);
            LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out _);
            LogisticsCenter twin = LogisticsCenter.Create(manifest, star, offset, 60, 18, 15);

            int registered = 0;
            foreach (LogisticsCenter c in LogisticsState.Centers)
                if (c != null && c.SystemId == centre.SystemId) registered++;
            Assert.AreEqual(2, registered,
                "the fixture failed to produce the duplicate this test exists to survive");

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            Assert.AreEqual(1, page.Logistics.VisibleCenters,
                "the same centre was drawn more than once");

            int rowsOnScreen = 0;
            foreach (Transform t in MenuLayer().GetComponentsInChildren<Transform>(true))
                if (t.gameObject.name.StartsWith("Center ") && t.gameObject.activeInHierarchy) rowsOnScreen++;
            Assert.AreEqual(1, rowsOnScreen, "more than one centre row is on screen");
            Assert.AreEqual(centre.SystemId, page.Logistics.SelectedSystemId,
                "the duplicate left the panel acting on a different centre");

            Object.DestroyImmediate(twin.gameObject);
        }

        // =====================================================================
        // Commanding
        // =====================================================================

        [UnityTest]
        public IEnumerator Upgrade_RefusesWithTheBackendsOwnReason_WhenTheRequirementsAreNotMet()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            // A fresh campaign is level 1 with no station-construction module and no
            // controlled planets, so the upgrade must be refused -- and must say which
            // of those it is.
            Assert.IsNotNull(page.Logistics.UpgradeBlockedReason,
                "the upgrade control is offered on a campaign that cannot possibly upgrade");

            Button upgrade = Live(MenuLayer(),
                UI.Logistics.NebulaLogisticsCenterPanel.UpgradeControlName,
                "the panel has no upgrade control");
            Assert.IsFalse(upgrade.interactable, "a refused upgrade is offered as available");

            CenterUpgradeRejectReason expected = ShipSide().CheckUpgrade();
            Assert.AreNotEqual(CenterUpgradeRejectReason.None, expected);
            Assert.AreEqual(
                UI.Logistics.NebulaLogisticsCenterPanel.UpgradeRejectText(expected),
                page.Logistics.UpgradeBlockedReason,
                "the panel invented its own refusal instead of quoting the backend's");

            Assert.AreEqual(LogisticsCenterTier.T1, centre.Runtime.Tier, "a refused upgrade still ran");
            Assert.IsFalse(centre.Runtime.Upgrading, "a refused upgrade still started");
        }

        [UnityTest]
        public IEnumerator Upgrade_Starts_AndCancelling_RefundsThroughTheSameSeam()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaContext context = UiFinalPlayMode.Ctx();
            MakeTheUpgradeLegal(context, centre.SystemId);
            yield return null;

            Assert.AreEqual(CenterUpgradeRejectReason.None, ShipSide().CheckUpgrade(),
                "the fixture did not manage to make the upgrade legal");

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;
            float partsBefore = cargo.Get(ProcessingCatalog.BasicPartId);

            Button upgrade = Live(MenuLayer(),
                UI.Logistics.NebulaLogisticsCenterPanel.UpgradeControlName, "no upgrade control");
            Assert.IsTrue(upgrade.interactable,
                $"the upgrade is legal and the control is dead: {page.Logistics.UpgradeBlockedReason}");
            upgrade.onClick.Invoke();

            Assert.IsTrue(centre.Runtime.Upgrading, "the upgrade control did not start an upgrade");
            Assert.AreEqual(LogisticsCenterCatalog.T2.BuildSeconds, centre.Runtime.UpgradeRemaining, 0.5f,
                "the upgrade did not take the catalog's own time");
            Assert.AreEqual(partsBefore - LogisticsCenterCatalog.T2.PartsCost,
                cargo.Get(ProcessingCatalog.BasicPartId), 0.5f,
                "the upgrade did not charge the T2 cost");
            StringAssert.Contains("YÜKSELTME", page.Logistics.StatusText ?? string.Empty,
                "an upgrade that started told the player nothing");

            // Cancel, through the panel's own control, and the refund lands in the
            // ship's real hold at the catalog's own 75%.
            float partsAfterCharge = cargo.Get(ProcessingCatalog.BasicPartId);
            int expectedRefund = centre.UpgradeRefundParts;
            Assert.Greater(expectedRefund, 0, "the backend recorded no refund to give back");

            Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.CancelUpgradeControlName,
                "the panel has no way to cancel an upgrade").onClick.Invoke();

            Assert.IsFalse(centre.Runtime.Upgrading, "cancel did not stop the upgrade");
            Assert.AreEqual(LogisticsCenterTier.T1, centre.Runtime.Tier,
                "cancelling an upgrade still applied it");
            Assert.AreEqual(partsAfterCharge + expectedRefund,
                cargo.Get(ProcessingCatalog.BasicPartId), 0.5f,
                "cancelling the upgrade did not refund 75% of the parts");
            StringAssert.Contains("İADE", page.Logistics.StatusText ?? string.Empty,
                "a refund happened and the player was not told");
        }

        /// <summary>
        /// A transfer command that really moves cargo, through the seam the deposit
        /// key uses. The panel never touches either inventory itself.
        /// </summary>
        [UnityTest]
        public IEnumerator Deposit_MovesRealCargoThroughTheShipsOwnSeam()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaContext context = UiFinalPlayMode.Ctx();
            LogisticsCargo cargo = context.Ship.GetComponent<ShipLogistics>().Cargo;

            // One item and nothing else in the hold, so the units that leave the ship
            // and the units that arrive at the centre are the same units and can be
            // compared without guessing at per-item volume.
            cargo.Clear();
            cargo.SetCapacity(Mathf.Max(cargo.Capacity, 5000f));

            string item = ExtractionCatalog.ResourceIds[0];
            cargo.Add(item, 200f);
            float shipBefore = cargo.GetStored(item);
            float centreBefore = centre.Inventory.GetStored(item);
            Assert.AreEqual(200f, shipBefore, 0.01f, "the fixture did not load the hold");

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            Live(MenuLayer(), UI.Logistics.NebulaLogisticsCenterPanel.DepositControlName,
                "the panel has no transfer control").onClick.Invoke();

            float leftTheShip = shipBefore - cargo.GetStored(item);
            float reachedTheCentre = centre.Inventory.GetStored(item) - centreBefore;

            Assert.Greater(leftTheShip, 0f, "the deposit control moved nothing out of the hold");
            Assert.AreEqual(leftTheShip, reachedTheCentre, 0.01f,
                "cargo was destroyed or duplicated between the ship and the centre");
            Assert.AreEqual(LogisticsCenterCatalog.T1.TransferVolumePerSecond, leftTheShip, 0.01f,
                "the transfer moved something other than the centre's own one-second rate");
            StringAssert.Contains("YÜKLEND", page.Logistics.StatusText ?? string.Empty,
                "a transfer that happened told the player nothing");
        }

        // =====================================================================
        // Gates
        // =====================================================================

        [UnityTest]
        public IEnumerator OutOfRange_EveryCommandIsUnavailable_AndSaysSoAsAProximityProblem()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            NebulaContext context = UiFinalPlayMode.Ctx();
            MakeTheUpgradeLegal(context, centre.SystemId);

            // Three times the anchor's approach range and no further: out of range,
            // still unambiguously inside the system, so what the panel reports has to
            // be proximity and not communication.
            context.Ship.transform.position = centre.transform.position + new Vector3(1500f, 0f, 0f);
            yield return UiFinalPlayMode.Until(() => !ShipSide().InRange,
                "the ship moved off the anchor to leave the centre's command range");
            Assert.AreEqual(centre.SystemId, ShipSide().CurrentSystemId,
                "the ship left the system entirely, so this would be a communication test, not a proximity one");

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);

            Assert.IsNotNull(page.Logistics.ReachBlockedReason,
                "the ship is out of range and the panel does not say so");
            StringAssert.Contains("MENZİL", page.Logistics.ReachBlockedReason,
                "an out-of-range refusal must read as a proximity problem, not a generic one");

            foreach (string control in new[]
                     {
                         UI.Logistics.NebulaLogisticsCenterPanel.UpgradeControlName,
                         UI.Logistics.NebulaLogisticsCenterPanel.DepositControlName,
                         UI.Logistics.NebulaLogisticsCenterPanel.WithdrawControlName,
                         UI.Logistics.NebulaLogisticsCenterPanel.DismantleControlName,
                     })
            {
                Button button = Live(MenuLayer(), control, $"the panel has no {control}");
                Assert.IsFalse(button.interactable,
                    $"{control} is live with the ship out of the centre's range");
            }

            Assert.IsTrue(HasVisibleReading(MenuLayer(), "ÇAPAYA YAKLAŞIN"),
                "the reason is stored and never shown");

            // And the reading is still correct while the commands are refused: an
            // unreachable centre is still a centre the player may look at.
            Assert.AreEqual(1, page.Logistics.VisibleCenters,
                "an out-of-range centre disappeared from the list");
        }

        /// <summary>
        /// Whether the panel is open or not must make no difference to what the
        /// simulation does. A screen that changes the outcome it is describing is
        /// worse than no screen.
        /// </summary>
        [UnityTest]
        public IEnumerator TheOutcomeIsIdentical_WhetherThePanelIsOpenOrClosed()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            centre.Inventory.Add(ExtractionCatalog.ResourceIds[0], 100f);

            // Closed: tick the centre's own clock and record what it produced.
            float fillClosed = centre.FillFraction01;
            centre.Tick(1f);
            LogisticsCenterState stateClosed = centre.Runtime.State;
            float totalClosed = centre.Inventory.Total;

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);
            page.Logistics.Refresh();
            page.Refresh();

            Assert.AreEqual(stateClosed, centre.Runtime.State,
                "opening the panel changed the centre's state");
            Assert.AreEqual(totalClosed, centre.Inventory.Total, 0.001f,
                "opening the panel changed what is in the centre");
            Assert.AreEqual(LogisticsCenterCatalog.T1.Capacity, centre.Inventory.Capacity, 0.001f,
                "opening the panel changed the centre's capacity");

            centre.Tick(1f);
            float openDelta = centre.FillFraction01 - fillClosed;
            Assert.AreEqual(0f, openDelta, 0.001f,
                "the centre filled differently with the panel open");

            // And closing it changes nothing back.
            LogisticsCenterState before = centre.Runtime.State;
            page.Logistics.Close();
            page.Refresh();
            Assert.AreEqual(before, centre.Runtime.State, "closing the panel changed the centre");
        }

        // =====================================================================
        // Persistence
        // =====================================================================

        [UnityTest]
        public IEnumerator TheCentre_SurvivesASaveAndLoad_WithoutBeingDuplicated()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            LogisticsCenter centre = null;
            yield return GiveTheShipACentre(c => centre = c);

            string systemId = centre.SystemId;
            centre.Inventory.Add(ExtractionCatalog.ResourceIds[0], 120f);
            float storedBefore = centre.Inventory.Total;
            LogisticsCenterTier tierBefore = centre.Runtime.Tier;

            ExtractionDirector director = UiFinalPlayMode.Ctx().ExtractionDirector;
            director.ActiveSlot = NebulaSaveSlotQuery.FirstSlot + 1;
            director.SaveNow("LOGISTICS PANEL");
            yield return null;

            director.LoadNow();
            yield return null;

            LogisticsCenter restored = LogisticsState.FindCenter(systemId);
            Assert.IsNotNull(restored, "the centre did not survive a save and load");
            Assert.AreEqual(tierBefore, restored.Runtime.Tier, "the restored centre is a different tier");
            Assert.AreEqual(storedBefore, restored.Inventory.Total, 1f,
                "the restored centre holds something other than what was saved");

            int registered = 0;
            foreach (LogisticsCenter c in LogisticsState.Centers)
                if (c != null && c.SystemId == systemId) registered++;
            Assert.AreEqual(1, registered, "the load left two centres registered for one system");

            UiFinalPlayMode.Ctx().Ship.transform.position = restored.transform.position;
            yield return null;

            NebulaFleetPage page = null;
            yield return OpenThePanel(p => page = p);
            Assert.AreEqual(1, page.Logistics.VisibleCenters,
                "the panel draws the restored centre more than once");
        }

        // =====================================================================
        // The page this panel lives on — its status line
        // =====================================================================

        /// <summary>
        /// Producing a Scout with nothing selected has to report either a start or a
        /// refusal, and the report has to survive the refresh the action itself
        /// triggers.
        /// <para>
        /// Every action handler on the fleet page ends in <c>Refresh</c>, and the
        /// refresh wrote the contextual status line — which with no vehicle selected
        /// is the empty string. So the one action on the page that can be taken with
        /// nothing selected was the one action that cleared its own answer: the button
        /// clicked, the page refreshed, and the player was told nothing whether it
        /// worked or not.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ProducingAVehicle_ReportsAStartOrARefusal_AndTheReportSurvivesTheRefresh()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();

            NebulaContext context = UiFinalPlayMode.Ctx();
            NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            Assert.IsTrue(menu.Open(NebulaUiAssetCatalog.Page.Fleet));
            yield return null;

            NebulaFleetPage page = menu.PageOf<NebulaFleetPage>();
            Assert.IsNotNull(page);
            page.Select(null);
            page.Refresh();

            string before = page.StatusText;
            Live(MenuLayer(), "Produce", "the fleet page has no way to produce a vehicle").onClick.Invoke();

            Assert.AreNotEqual(before, page.StatusText,
                "producing a vehicle reported neither a start nor a refusal");
            Assert.IsNotEmpty(page.StatusText ?? string.Empty,
                "the report was written and then blanked by the action's own refresh");

            string reported = page.StatusText;

            // The refresh the parent menu runs on its own timer must not eat it either.
            page.Refresh();
            page.Refresh();
            Assert.AreEqual(reported, page.StatusText,
                "the next refresh cleared what the player was just told");

            // A new selection is a new subject, so the report is allowed to go then.
            page.Select("no-such-vehicle");
            page.Refresh();
            Assert.AreNotEqual(reported, page.StatusText,
                "the report outlived the thing it was describing");
        }

        // =====================================================================
        // Fixture helper
        // =====================================================================

        /// <summary>
        /// Everything <c>CheckUpgrade</c> asks for, set through the services that own
        /// each answer rather than by reaching into the check itself.
        /// </summary>
        private static void MakeTheUpgradeLegal(NebulaContext context, string systemId)
        {
            ProgressionService.Award("test.logistics-upgrade",
                (int)ProgressionMath.TotalXpToReach(LogisticsCenterCatalog.T2.RequiredLevel + 1));
            Assert.GreaterOrEqual(ProgressionService.State.Level, LogisticsCenterCatalog.T2.RequiredLevel,
                "the fixture could not reach the level the upgrade requires");

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

            // The ship must also be slow enough to work on the station. Nothing to do:
            // ShipController carries its own Velocity rather than a Rigidbody, and a
            // ship that has only been teleported has not accelerated. CheckUpgrade's
            // TooFast branch is asserted against below rather than assumed away.
        }
    }
}
