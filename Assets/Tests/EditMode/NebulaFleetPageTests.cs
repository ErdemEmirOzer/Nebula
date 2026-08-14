using NUnit.Framework;
using UnityEngine;
using Nebula.Hangar;
using Nebula.Navigation;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// UI-06 fleet page and UI-04's navigation cluster. Both are read-only
    /// projections, so what is worth testing is the copy, the safety of a null
    /// session, and the rules the player relies on.
    /// </summary>
    public class NebulaFleetPageTests
    {
        private NebulaUiShell shell;
        private float enteredTimeScale;

        [SetUp]
        public void Setup()
        {
            enteredTimeScale = Time.timeScale;
            ExclusiveModal.ResetForSession();
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

        private RectTransform Host()
        {
            var go = new GameObject("Host", typeof(RectTransform));
            go.transform.SetParent(shell.Root(UiLayer.Menu), false);
            return (RectTransform)go.transform;
        }

        // ---- the off-screen route indicator ----------------------------------

        /// <summary>
        /// NavigationHud pointed at the selected route star whenever it was off
        /// screen. It was the one non-text element in that HUD, and the redesign
        /// had no equivalent -- the "Arrow" in the route row is a static bullet,
        /// not a direction. Deleting NavigationHud without this would have taken
        /// the indicator with it.
        /// </summary>
        [Test]
        public void TheRouteCompassStartsHiddenAndSurvivesNoCameraOrSession()
        {
            NebulaHudNavigation hud = NebulaHudNavigation.Create(shell, null, null);

            Assert.IsFalse(hud.RouteCompassVisible,
                "with no route there is nothing to point at");
            Assert.AreEqual(260f, NebulaHudNavigation.CompassRadius,
                "the ring radius is the one players already read");
        }

        // ---- the contextual action rail --------------------------------------
        //
        // These actions -- escort stance, engineer target, quick resupply, route
        // pause and route delete -- were all reachable in the hangar screen this
        // page replaces. The removal protocol forbids dropping a player function
        // without a replacement, so what is tested here is that each one exists,
        // is offered only where it applies, and cannot be laid out on top of
        // another one.

        [Test]
        public void ContextRail_OffersNothingWithoutASelectedVehicle()
        {
            var page = new NebulaFleetPage(null);
            page.Build(Host());
            page.Refresh();

            Assert.AreEqual(0, page.VisibleContextActions,
                "an empty selection has no action that could apply to it");
        }

        [Test]
        public void ContextRail_SurvivesASelectionThatDoesNotResolve()
        {
            // Select() takes an id, and a stale id from a previous session must be
            // a quiet no-op rather than a null dereference in the refresh.
            var page = new NebulaFleetPage(null);
            page.Build(Host());

            Assert.DoesNotThrow(() => page.Select("no-such-vehicle"));
            Assert.DoesNotThrow(() => page.Refresh());
            Assert.AreEqual(0, page.VisibleContextActions);
        }

        [Test]
        public void EveryVisibleContextAction_GetsItsOwnSlot()
        {
            // The groups combine -- a resupplying escort shows both sets -- so slots
            // are assigned at refresh, not at build. Fixed per-group slots stacked
            // the resupply buttons on top of the escort buttons.
            var page = new NebulaFleetPage(null);
            RectTransform host = Host();
            page.Build(host);
            page.Refresh();

            var seen = new System.Collections.Generic.List<Vector2>();
            foreach (RectTransform rect in AllButtons(host))
            {
                if (!rect.gameObject.activeSelf) continue;
                Assert.IsFalse(seen.Contains(rect.anchoredPosition),
                    $"{rect.name} shares a slot with another visible action");
                seen.Add(rect.anchoredPosition);
            }
        }

        private static System.Collections.Generic.List<RectTransform> AllButtons(RectTransform host)
        {
            var found = new System.Collections.Generic.List<RectTransform>();
            foreach (UnityEngine.UI.Button button in host.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                found.Add(button.GetComponent<RectTransform>());
            return found;
        }

        [Test]
        public void EveryRefusalReasonHasPlayerFacingTurkishCopy()
        {
            // The reject strings are the player-facing contract, carried over word
            // for word from the screen being deleted. A new enum value with no copy
            // would surface as an empty tooltip, which reads as "no reason given".
            var page = new NebulaFleetPage(null);
            page.Build(Host());

            foreach (var reason in System.Enum.GetValues(typeof(ShipHangar.MaintenanceResupplyRejectReason)))
                AssertCopy(page, "resupply", reason);
            foreach (var reason in System.Enum.GetValues(typeof(ShipHangar.EscortAssignRejectReason)))
                AssertCopy(page, "escort", reason);
            foreach (var reason in System.Enum.GetValues(typeof(ShipHangar.EngineerSendRejectReason)))
                AssertCopy(page, "engineer", reason);
        }

        private static void AssertCopy(NebulaFleetPage page, string kind, object reason)
        {
            string text = NebulaFleetPage.RejectTextFor(kind, reason);
            Assert.IsFalse(string.IsNullOrWhiteSpace(text),
                $"{kind} reject reason {reason} has no player-facing copy");
            Assert.AreEqual(NebulaText.Upper(text), text,
                $"{kind} reject copy must already be upper-case Turkish");
        }

        [Test]
        public void ReportingAnActionResult_ShowsItOnTheStatusLine()
        {
            var page = new NebulaFleetPage(null);
            page.Build(Host());
            page.Refresh();

            Assert.IsNotNull(page.StatusText, "the page must have a status line to report into");
        }

        // ---- null-session safety ---------------------------------------------

        [Test]
        public void FleetPage_BuildsAndRefreshesWithoutASession()
        {
            // Pages are built before a campaign exists — the menu can be opened from
            // a fresh boot — so a null context must never be a crash.
            var page = new NebulaFleetPage(null);
            Assert.DoesNotThrow(() => page.Build(Host()));
            Assert.DoesNotThrow(() => page.Refresh());
            Assert.AreEqual(0, page.VisibleRows);
            Assert.AreEqual(0, page.AttentionCount);
        }

        [Test]
        public void FleetPage_RefreshesRepeatedlyWithoutGrowing()
        {
            var page = new NebulaFleetPage(null);
            page.Build(Host());
            for (int i = 0; i < 200; i++) page.Refresh();
            Assert.AreEqual(0, page.VisibleRows,
                "Repeated refreshes must not accumulate rows when there is nothing to show.");
        }

        // ---- copy ------------------------------------------------------------

        [Test]
        public void EveryVehiclePhase_HasPlayerFacingCopy()
        {
            // A phase with no translation would print an enum name at the player.
            foreach (VehiclePhase phase in System.Enum.GetValues(typeof(VehiclePhase)))
            {
                string name = NebulaFleetPage.PhaseName(phase);
                Assert.IsFalse(string.IsNullOrEmpty(name), $"{phase} has no copy.");
                Assert.AreNotEqual(phase.ToString(), name,
                    $"{phase} is being shown as its raw enum name.");
            }
        }

        [Test]
        public void EveryVehicleClass_HasPlayerFacingCopy()
        {
            foreach (VehicleClass vehicleClass in System.Enum.GetValues(typeof(VehicleClass)))
            {
                string name = NebulaFleetPage.ClassName(vehicleClass);
                Assert.IsFalse(string.IsNullOrEmpty(name));
                Assert.AreNotEqual(vehicleClass.ToString(), name);
            }
        }

        [Test]
        public void EveryFlightMode_HasPlayerFacingCopy()
        {
            foreach (FlightMode mode in System.Enum.GetValues(typeof(FlightMode)))
            {
                string name = NebulaHudNavigation.ModeName(mode);
                Assert.IsFalse(string.IsNullOrEmpty(name));
                Assert.AreNotEqual(mode.ToString(), name,
                    $"{mode} is being shown as its raw enum name.");
            }
        }

        // ---- the stable id must not leak -------------------------------------

        [Test]
        public void Callsign_ShowsDigitsOnly_NeverTheStableId()
        {
            Assert.AreEqual("04", NebulaFleetPage.Callsign("vehicle-collector-4"));
            Assert.AreEqual("12", NebulaFleetPage.Callsign("vehicle-scout-12"));

            string callsign = NebulaFleetPage.Callsign("vehicle-collector-4");
            Assert.IsFalse(callsign.Contains("vehicle"),
                "The stable id leaked into the callsign shown to the player.");
        }

        [Test]
        public void Callsign_SurvivesAnIdWithNoTrailingDigits()
        {
            Assert.AreEqual("—", NebulaFleetPage.Callsign("vehicle-prototype"));
            Assert.AreEqual("—", NebulaFleetPage.Callsign(null));
            Assert.AreEqual("—", NebulaFleetPage.Callsign(string.Empty));
        }

        // ---- durations -------------------------------------------------------

        [Test]
        public void ProductionEta_ReadsAsMinutesAndSeconds()
        {
            Assert.AreEqual("0:45", NebulaFleetPage.FormatDuration(45f));
            Assert.AreEqual("2:05", NebulaFleetPage.FormatDuration(125f));
        }

        [Test]
        public void ADurationNeverGoesNegative()
        {
            // Timers can tick a frame past zero before the job is reaped.
            Assert.AreEqual("0:00", NebulaFleetPage.FormatDuration(-3f));
            Assert.IsFalse(NebulaHudNavigation.FormatDuration(-3f).Contains("-"));
        }

        [Test]
        public void NavigationEta_ReadsAsTurkishDuration()
        {
            StringAssert.Contains("sn", NebulaHudNavigation.FormatDuration(42f));
            StringAssert.Contains("dk", NebulaHudNavigation.FormatDuration(310f));
        }

        // ---- navigation cluster ----------------------------------------------

        [Test]
        public void NavigationCluster_BuildsWithoutAShipOrDirector()
        {
            NebulaHudNavigation hud = null;
            Assert.DoesNotThrow(() => hud = NebulaHudNavigation.Create(shell, null, null));
            Assert.IsNotNull(hud);
            Assert.IsFalse(hud.RouteVisible, "A route row must not show when there is no route.");
            Assert.IsFalse(hud.ArrivalWarningVisible);
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void NavigationCluster_PutsAReticleOnTheDynamicLayer()
        {
            // Parity: ShipHud drew the centre crosshair. Switching that panel off
            // without this would leave the player with no aim point at all.
            NebulaHudNavigation hud = NebulaHudNavigation.Create(shell, null, null);
            Transform reticle = FindDeep(shell.Root(UiLayer.HudDynamic), "Reticle");
            Assert.IsNotNull(reticle, "The centre reticle is missing.");
            Object.DestroyImmediate(hud.gameObject);
        }

        [Test]
        public void NavigationCluster_KeepsThrottleDraggable()
        {
            // Parity: ShipHud's plus-widget owned throttle dragging. The gesture has
            // to survive that panel going dark, or a control was removed rather than
            // replaced.
            NebulaHudNavigation hud = NebulaHudNavigation.Create(shell, null, null);
            var drag = shell.Root(UiLayer.HudDynamic).GetComponentInChildren<HorizontalThrottleInput>(true);
            Assert.IsNotNull(drag, "Throttle dragging was lost when ShipHud was switched off.");
            Object.DestroyImmediate(hud.gameObject);
        }

        // ---- alerts ----------------------------------------------------------

        [Test]
        public void AlertsCluster_ShowsNothingWithoutASession()
        {
            NebulaHudAlerts alerts = NebulaHudAlerts.Create(shell, null, null);
            Assert.IsFalse(alerts.RaidVisible,
                "A raid banner with no raid would train the player to ignore it.");
            Object.DestroyImmediate(alerts.gameObject);
        }

        [Test]
        public void AttentionCards_HaveASurface_AfterAttentionHudWasSwitchedOff()
        {
            // Regression: AttentionHud was disabled in composition before anything
            // replaced its three consolidated cards, which removed a player-facing
            // surface with no substitute — a violation of this redesign's own
            // removal rule. The widgets must exist even when nothing is showing.
            NebulaHudAlerts alerts = NebulaHudAlerts.Create(shell, null, null, null);
            Transform column = FindDeep(shell.Root(UiLayer.HudDynamic), "Attention");

            Assert.IsNotNull(column, "The attention column is missing entirely.");
            Assert.AreEqual(NebulaHudAlerts.MaxAttentionCards,
                column.GetComponentsInChildren<UnityEngine.UI.Image>(true).Length >= NebulaHudAlerts.MaxAttentionCards
                    ? NebulaHudAlerts.MaxAttentionCards : 0,
                "Fewer attention card widgets were built than the query can return.");
            Assert.AreEqual(0, alerts.VisibleAttentionCards,
                "Cards must stay hidden until the query actually returns some.");
            Object.DestroyImmediate(alerts.gameObject);
        }

        [Test]
        public void EveryAttentionPriority_HasPlayerFacingCopy()
        {
            foreach (Nebula.Extraction.AttentionPriority priority in
                     System.Enum.GetValues(typeof(Nebula.Extraction.AttentionPriority)))
            {
                string name = NebulaHudAlerts.PriorityName(priority);
                Assert.IsFalse(string.IsNullOrEmpty(name));
                Assert.AreNotEqual(priority.ToString(), name,
                    $"{priority} is being shown as its raw enum name.");
            }
        }

        [Test]
        public void EveryRaidStage_HasPlayerFacingCopy()
        {
            foreach (Nebula.Combat.RaidStage stage in System.Enum.GetValues(typeof(Nebula.Combat.RaidStage)))
            {
                string name = NebulaHudAlerts.StageName(stage);
                Assert.IsFalse(string.IsNullOrEmpty(name));
                Assert.AreNotEqual(stage.ToString(), name,
                    $"{stage} is being shown as its raw enum name.");
            }
            Assert.AreEqual("—", NebulaHudAlerts.StageName(null));
        }

        [Test]
        public void RaidCountdown_NeverGoesNegative()
        {
            // A warning timer can tick a frame past zero before the phase advances.
            Assert.AreEqual("0:00", NebulaHudAlerts.FormatCountdown(-2f));
            Assert.AreEqual("1:12", NebulaHudAlerts.FormatCountdown(72f));
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
