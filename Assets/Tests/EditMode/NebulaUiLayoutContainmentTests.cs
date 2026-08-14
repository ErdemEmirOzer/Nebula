using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Nothing may render outside the panel it belongs to.
    /// <para>
    /// Written after a screenshot showed a "GAZ" label sitting below its own card
    /// and a "u/sn" label overlapping the throttle bar. Both came from the same
    /// mistake — setting <c>anchoredPosition</c> on a rect whose anchors were still
    /// Unity's centred default — and both were invisible to every other test,
    /// because the widgets existed, held the right text, and were simply in the
    /// wrong place. Checking geometry is the only thing that catches that.
    /// </para>
    /// </summary>
    public class NebulaUiLayoutContainmentTests
    {
        private NebulaUiShell shell;
        private float enteredTimeScale;

        /// <summary>Sub-pixel rounding and deliberate hairline bleed are not defects.</summary>
        private const float Tolerance = 2f;

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

        [Test]
        public void FlightAndSiteClusters_KeepEveryLabelInsideItsPanel()
        {
            NebulaHudNavigation.Create(shell, null, null);
            Canvas.ForceUpdateCanvases();

            AssertContained(shell.Root(UiLayer.HudDynamic), "Flight");
            AssertContained(shell.Root(UiLayer.HudStatic), "Site");
        }

        /// <summary>
        /// Both alert clusters are driven by live campaign state — the raid banner by
        /// a <c>ThreatPressureState</c> phase, the cargo banner by a hold that is
        /// actually filling — and <c>Create(shell, null, null)</c> has neither. So
        /// neither renders here and neither can be containment-checked here.
        /// <para>
        /// This test used to claim it checked their containment and checked nothing:
        /// its enumeration walked <c>UnityEngine.UI.Text</c>, of which this interface
        /// has none, so it passed on both a hidden panel and a broken one. Retargeted
        /// to TMP, the truth came out — both clusters are switched off. Rather than
        /// keep a containment name over an empty check, the test now asserts what is
        /// genuinely true and provable at this level, which is that an alert cluster
        /// does not appear when there is nothing to alert about.
        /// </para>
        /// <para>
        /// Their containment is no longer merely deferred: it is measured in
        /// <c>UiFinalHudWarningContainmentTests</c>, which drives a real raid through
        /// <c>ThreatPressureState</c> and a real over-capacity hold through
        /// <c>LogisticsCargo</c>, holds the HUD canvases at the 1280x720 reference
        /// viewport, and checks both clusters for containment, mutual overlap, overlap
        /// with the vital cluster, readability under High Contrast and the warning
        /// Visual Cue, and that nothing is left drawn once a warning clears. What is
        /// asserted here is only that neither draws when there is nothing to say.
        /// </para>
        /// </summary>
        [Test]
        public void AlertClusters_StayHiddenUntilThereIsSomethingToReport()
        {
            NebulaHudAlerts.Create(shell, null, null);
            Canvas.ForceUpdateCanvases();

            AssertHiddenAndThereforeUnchecked(shell.Root(UiLayer.HudDynamic), "Raid");
            AssertHiddenAndThereforeUnchecked(shell.Root(UiLayer.HudDynamic), "Cargo");
        }

        /// <summary>
        /// Records that a panel is switched off in this state and is therefore not
        /// containment-checked here. Deliberately an assertion and not a silent
        /// skip: the moment the panel starts rendering by default this goes red and
        /// somebody has to decide again, which is the difference between an
        /// exclusion a reader can see and one nobody knows about.
        /// </summary>
        private static void AssertHiddenAndThereforeUnchecked(RectTransform root, string panelName)
        {
            RectTransform panel = Find(root, panelName);
            Assert.IsNotNull(panel, $"Could not find a panel named '{panelName}'.");
            Assert.IsFalse(panel.gameObject.activeInHierarchy,
                $"'{panelName}' now renders by default, so it can and must be containment-checked " +
                "instead of declared unchecked");
        }

        [Test]
        public void ContextBar_KeepsEveryLabelInsideItsAction()
        {
            NebulaHudContextBar bar = NebulaHudContextBar.Create(shell);
            bar.Show("BAĞLAM", new HudAction("BİRİNCİL", KeyCode.F, "30 PARÇA"),
                new List<HudAction> { new HudAction("İKİNCİL", KeyCode.B) });
            Canvas.ForceUpdateCanvases();

            AssertContained(shell.Root(UiLayer.HudStatic), "Context Bar");
        }

        /// <summary>
        /// A label whose rect is shorter than its own glyphs renders as nothing.
        /// The factory defaults to 200x18 so that a forgotten Place() lands in a
        /// corner rather than floating; a 76pt wordmark in that default simply
        /// vanished, and the main menu shipped with no title on it because nothing
        /// had ever rendered the screen.
        /// </summary>
        /// <summary>
        /// Two panels collapse to the height of what they are actually showing and
        /// grow when their optional row appears -- otherwise they sit there padded
        /// for content they do not have, which reads as a panel that failed to
        /// draw. Skipping hidden rows in the containment check above is only safe
        /// because the grown height is checked here to still clear them.
        /// </summary>
        [Test]
        public void EveryCollapsiblePanel_GrowsEnoughForTheRowItHides()
        {
            Assert.GreaterOrEqual(NebulaHudAlerts.CargoFullHeight, NebulaHudAlerts.CargoWarningBottom,
                "the cargo panel must clear its over-capacity warning when it shows one");
            Assert.Less(NebulaHudAlerts.CargoShortHeight, NebulaHudAlerts.CargoFullHeight,
                "and must actually be shorter without it, or collapsing achieves nothing");

            Assert.GreaterOrEqual(NebulaHudNavigation.SiteFullHeight, NebulaHudNavigation.SiteRouteBottom,
                "the site panel must clear its route row when it shows one");
            Assert.Less(NebulaHudNavigation.SiteShortHeight, NebulaHudNavigation.SiteFullHeight);
        }

        [Test]
        public void NoLabelIsTallerThanTheRectItIsDrawnIn()
        {
            var host = new GameObject("Host", typeof(RectTransform));
            host.transform.SetParent(shell.Root(UiLayer.Menu), false);
            var rect = (RectTransform)host.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;

            NebulaMainMenuScreen.Create(shell, null);

            // Every page too, not just the menu that happened to be caught: the
            // defect is invisible until something renders the screen, and most of
            // these had never been rendered either.
            NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
            menu.AddPage(new NebulaOverviewPage(null));
            menu.AddPage(new NebulaMapPage(null));
            menu.AddPage(new NebulaProgressionPage(null));
            menu.AddPage(new NebulaFleetPage(null));
            menu.AddPage(new NebulaPlanetsPage(null));
            menu.AddPage(new NebulaResourcesPage(null));
            menu.AddPage(new NebulaCombatPage(null));
            menu.AddPage(new NebulaDiscoveryPage(null));
            menu.AddPage(new NebulaSettingsPage());
            menu.AddPage(new NebulaSessionPage(null));
            foreach (NebulaUiAssetCatalog.Page page in
                     System.Enum.GetValues(typeof(NebulaUiAssetCatalog.Page)))
                menu.ShowPage(page);

            NebulaConfirmationDialog.Create(shell);
            Canvas.ForceUpdateCanvases();

            // TextMeshProUGUI, not UnityEngine.UI.Text. Nothing on any screen in this
            // interface is a legacy Text -- NebulaUiFactory.Label builds TMP and every
            // label on every page comes from that one method -- so this enumeration
            // was empty and every assertion inside it unreachable. The test passed on
            // any layout at all, including one with every label off screen.
            int measured = 0;
            foreach (TextMeshProUGUI text in
                     shell.Root(UiLayer.Menu).GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                measured++;
                Assert.GreaterOrEqual(text.rectTransform.rect.height, text.fontSize - Tolerance,
                    $"{text.name} is {text.fontSize}pt in a {text.rectTransform.rect.height:0}px rect");
            }

            // The guard the original shape needed. A containment test that measured
            // nothing reported the same green as one that measured everything, and
            // there was no way to tell the two apart from the outside.
            Assert.Greater(measured, 0,
                "no label was measured, so this test proved nothing about any layout");
        }

        /// <summary>
        /// The width sibling of the test above, which is the half that was missing.
        /// <para>
        /// Height was measured and width was not, so a label whose rect was too
        /// narrow simply ellipsized and every test stayed green. The main menu
        /// footer shipped reading "NEBULA · MECHANICAL ALPH…" — an unplaced label
        /// keeping <c>NebulaUiFactory</c>'s 200x18 default while holding a string
        /// four times that wide. It was visible in every capture of the front door
        /// and no assertion looked at it.
        /// </para>
        /// <para>
        /// Wrapping labels are excluded on purpose: a wrapped label is supposed to
        /// have more text than fits on one line, so <c>preferredWidth</c> says
        /// nothing about whether it is being cut off.
        /// </para>
        /// </summary>
        [Test]
        public void NoSingleLineLabelIsNarrowerThanTheTextItHolds()
        {
            var host = new GameObject("Host", typeof(RectTransform));
            host.transform.SetParent(shell.Root(UiLayer.Menu), false);
            var rect = (RectTransform)host.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;

            NebulaMainMenuScreen.Create(shell, null);
            Canvas.ForceUpdateCanvases();

            var offenders = new List<string>();
            int measured = 0;
            foreach (TextMeshProUGUI text in
                     shell.Root(UiLayer.Menu).GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                if (text.enableWordWrapping) continue;

                measured++;
                float available = text.rectTransform.rect.width;
                float needed = text.preferredWidth;
                if (needed > available + Tolerance)
                    offenders.Add($"{text.name} needs {needed:0}px for \"{text.text}\" "
                        + $"and was given {available:0}px");
            }

            Assert.Greater(measured, 0,
                "no label was measured, so this test proved nothing about any layout");
            Assert.IsEmpty(offenders,
                "labels cut off by their own rect: " + string.Join("; ", offenders));
        }

        [Test]
        public void FleetPage_KeepsItsVehicleListClearOfBothActionRails()
        {
            // Containment is not enough on its own: two rects can each sit inside
            // the page and still sit on top of each other. Adding the contextual
            // rail pushed the vehicle list 16px into it at the content host's real
            // height, which no containment check would have noticed.
            var host = new GameObject("Host", typeof(RectTransform));
            host.transform.SetParent(shell.Root(UiLayer.Menu), false);
            var rect = (RectTransform)host.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(236f, 62f);   // the parent menu's content host
            rect.offsetMax = new Vector2(0f, -78f);

            var page = new NebulaFleetPage(null);
            page.Build(rect);
            page.Refresh();
            Canvas.ForceUpdateCanvases();

            Rect list = WorldRect(Find(rect, "Vehicles"));
            Rect actions = WorldRect(Find(rect, "Context Actions"));

            Assert.GreaterOrEqual(list.yMin, actions.yMax - Tolerance,
                "the vehicle list must sit entirely above the contextual action rail");
        }

        [Test]
        public void SettingsRows_KeepTheirLabelsInside()
        {
            var host = new GameObject("Host", typeof(RectTransform));
            host.transform.SetParent(shell.Root(UiLayer.Menu), false);
            var rect = (RectTransform)host.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            new NebulaSettingsPage().Build(rect);
            Canvas.ForceUpdateCanvases();

            AssertContained(shell.Root(UiLayer.Menu), "Host");
        }

        /// <summary>
        /// The settings page is the one screen whose length is decided by data rather
        /// than by layout: the controls section prints a reading per featured
        /// non-rebindable action, so the column grows whenever the input catalog does.
        /// A row past the bottom edge is not clipped visibly — it is simply absent,
        /// and the player has no way to know a control exists. 1.4 is where it bites
        /// first, because the canvas scaler divides the reference resolution by the UI
        /// scale, so 1080 of usable height becomes 771.
        /// </summary>
        [Test]
        public void BothSettingsColumns_FitThePageAtMaximumUiScale()
        {
            float enteredScale = NebulaUiShell.UiScale;
            try
            {
                NebulaUiShell.UiScale = NebulaUiShell.MaxUiScale;

                var host = new GameObject("Host", typeof(RectTransform));
                host.transform.SetParent(shell.Root(UiLayer.Menu), false);
                var rect = (RectTransform)host.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                new NebulaSettingsPage().Build(rect);
                Canvas.ForceUpdateCanvases();

                AssertColumnHoldsItsRows(rect, "Settings Column");
                AssertColumnHoldsItsRows(rect, "Settings Column 2");
            }
            finally
            {
                NebulaUiShell.UiScale = enteredScale;
            }
        }

        /// <summary>
        /// Every control the combat page offers is inside the page. Written because the
        /// Part 4 capture matrix reported the weapon-production buttons outside the
        /// frame, and a screenshot cannot tell "the layout is wrong" from "the capture
        /// harness is showing it at the wrong size" — this can, because it measures the
        /// rects against the host they were built into and never renders anything.
        /// </summary>
        [Test]
        // No session context: these pages read it only when a control is used, and
        // these tests are about where the controls are drawn, not about what they do.
        public void CombatPageControls_StayInsideThePage() =>
            AssertPageKeepsItsControlsInside("Combat Host", new NebulaCombatPage(null));

        /// <summary>
        /// The resources page, for the same reason and found the same way: the capture
        /// matrix reported a cargo row drawn to the left of the frame at 3440x1440, and
        /// a screenshot cannot say whether that is the layout or the harness.
        /// </summary>
        [Test]
        public void ResourcesPageControls_StayInsideThePage() =>
            AssertPageKeepsItsControlsInside("Resources Host", new NebulaResourcesPage(null));

        private void AssertPageKeepsItsControlsInside(string hostName, INebulaMenuPage menuPage)
        {
            var host = new GameObject(hostName, typeof(RectTransform));
            host.transform.SetParent(shell.Root(UiLayer.Menu), false);
            var rect = (RectTransform)host.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            menuPage.Build(rect);
            Canvas.ForceUpdateCanvases();

            Rect page = WorldRect(rect);
            var outside = new List<string>();
            foreach (Selectable control in rect.GetComponentsInChildren<Selectable>(true))
            {
                // Inside a mask, "outside the page" is scrolling, not overflow.
                if (control.GetComponentInParent<RectMask2D>() != null) continue;
                if (control.GetComponentInParent<Mask>() != null) continue;

                Rect area = WorldRect((RectTransform)control.transform);
                if (area.xMin < page.xMin - Tolerance || area.xMax > page.xMax + Tolerance
                    || area.yMin < page.yMin - Tolerance || area.yMax > page.yMax + Tolerance)
                    outside.Add($"{control.gameObject.name} @ {area.xMin:F0},{area.yMin:F0} " +
                                $"(page {page.xMin:F0},{page.yMin:F0} → {page.xMax:F0},{page.yMax:F0})");
            }

            Assert.IsEmpty(outside,
                $"these {hostName} controls are drawn outside the page, so no player can reach them:\n  "
                + string.Join("\n  ", outside));
        }

        /// <summary>
        /// Asserts every row placed in a settings column ends above the column's own
        /// bottom edge. Measured on the built rects rather than on a running total of
        /// row heights, so the check still holds when a row changes size.
        /// </summary>
        private static void AssertColumnHoldsItsRows(RectTransform root, string columnName)
        {
            RectTransform column = Find(root, columnName);
            Assert.IsNotNull(column, $"Could not find a settings column named '{columnName}'.");

            Rect outer = WorldRect(column);
            float lowest = outer.yMin;
            string offender = null;

            for (int i = 0; i < column.childCount; i++)
            {
                var child = column.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeInHierarchy) continue;
                Rect inner = WorldRect(child);
                if (inner.yMin < lowest)
                {
                    lowest = inner.yMin;
                    offender = child.name;
                }
            }

            Assert.GreaterOrEqual(lowest, outer.yMin - Tolerance,
                $"'{columnName}' runs {outer.yMin - lowest:F0}px past its own bottom edge at " +
                $"UI scale {NebulaUiShell.MaxUiScale}; the first row off the page is '{offender}'. " +
                "A row below the fold is unreachable, not merely ugly.");
        }

        /// <summary>
        /// Walks every panel under <paramref name="root"/> whose name matches, and
        /// asserts each descendant label sits inside it.
        /// </summary>
        private static void AssertContained(RectTransform root, string panelName)
        {
            RectTransform panel = Find(root, panelName);
            Assert.IsNotNull(panel, $"Could not find a panel named '{panelName}'.");

            Assert.IsTrue(panel.gameObject.activeInHierarchy,
                $"'{panelName}' is switched off, so this call checks nothing. Either drive it " +
                "into the state where it renders, or declare the exclusion with " +
                "AssertHiddenAndThereforeUnchecked so a reader can see it is unchecked on purpose.");

            var failures = new List<string>();
            int measured = 0;
            // TextMeshProUGUI, not UnityEngine.UI.Text: see the note in
            // EveryScreen_KeepsItsLabelsInsideItsPanels. This walked an empty
            // collection, so every containment failure it existed to catch went
            // through it untouched.
            foreach (TextMeshProUGUI text in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                // Only what is drawn. Panels that carry an optional row -- the cargo
                // over-capacity warning, the site cluster's route -- collapse to the
                // height of what they are actually showing and grow when the row
                // appears, so measuring a hidden row against the collapsed height
                // reports a defect the player can never see. What the test was
                // written for was a visible label sitting outside its card, and that
                // is still exactly what it catches.
                if (!text.gameObject.activeInHierarchy) continue;

                measured++;
                RectTransform child = text.rectTransform;
                if (!IsInside(panel, child))
                    failures.Add($"{Path(panel, child)} — {Describe(panel, child)}");
            }

            Assert.Greater(measured, 0,
                $"'{panelName}' contained no visible label, so containment was never checked");
            Assert.IsEmpty(failures,
                $"Widgets rendered outside '{panelName}':\n  " + string.Join("\n  ", failures));
        }

        private static bool IsInside(RectTransform panel, RectTransform child)
        {
            Rect outer = WorldRect(panel);
            Rect inner = WorldRect(child);
            return inner.xMin >= outer.xMin - Tolerance
                   && inner.xMax <= outer.xMax + Tolerance
                   && inner.yMin >= outer.yMin - Tolerance
                   && inner.yMax <= outer.yMax + Tolerance;
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float xMin = Mathf.Min(corners[0].x, corners[2].x);
            float xMax = Mathf.Max(corners[0].x, corners[2].x);
            float yMin = Mathf.Min(corners[0].y, corners[2].y);
            float yMax = Mathf.Max(corners[0].y, corners[2].y);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static string Describe(RectTransform panel, RectTransform child)
        {
            Rect outer = WorldRect(panel);
            Rect inner = WorldRect(child);
            return $"panel x[{outer.xMin:0},{outer.xMax:0}] y[{outer.yMin:0},{outer.yMax:0}]  " +
                   $"child x[{inner.xMin:0},{inner.xMax:0}] y[{inner.yMin:0},{inner.yMax:0}]";
        }

        private static string Path(Transform root, Transform node)
        {
            var parts = new List<string>();
            for (Transform t = node; t != null && t != root.parent; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static RectTransform Find(RectTransform root, string name)
        {
            foreach (RectTransform candidate in root.GetComponentsInChildren<RectTransform>(true))
                if (candidate.name == name) return candidate;
            return null;
        }
    }
}
