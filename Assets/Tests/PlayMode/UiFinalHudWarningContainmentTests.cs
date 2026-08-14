using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The two HUD warnings, on screen, measured.
    /// <para>
    /// <c>NebulaUiLayoutContainmentTests.AlertClusters_StayHiddenUntilThereIsSomethingToReport</c>
    /// says exactly what it does and does not prove: in EditMode both clusters are
    /// switched off, because neither has any campaign state to report, so neither can
    /// be containment-checked there. It records the exclusion honestly and then
    /// leaves it — which means the raid banner and the cargo over-capacity warning,
    /// the two most urgent things this interface can say, had never been measured in
    /// the state a player sees them in.
    /// </para>
    /// <para>
    /// <b>How the state gets there.</b> Through the type-safe seams that feed the
    /// widgets' own readers, never by writing UI text. The raid comes from
    /// <c>ThreatPressureState.ScheduleRaid</c> and is advanced into Warning by the
    /// real <c>RaidScheduler</c>, which is the production transition; the over-capacity
    /// warning comes from the ship's real hold going over its real capacity through
    /// <c>LogisticsCargo.SetCapacity</c>, the module-tier effect whose own summary
    /// names this exact case. The widgets then decide for themselves whether to draw,
    /// which is the thing under test.
    /// </para>
    /// <para>
    /// <b>What "at 1280x720" means here.</b> Every layer scales with
    /// <c>MatchWidthOrHeight</c> at 0.5 against a 1920x1080 reference, so on any 16:9
    /// viewport the canvas is 1920x1080 canvas units and the layout question is
    /// identical at 1280x720 and at 1920x1080. Rather than depend on whatever
    /// resolution a batch-mode run happens to have — which may not be 16:9 at all, and
    /// would then be measuring a different aspect and quietly reporting it as this one
    /// — the HUD canvases are held at that exact logical viewport for the duration of
    /// each measurement and put back afterwards.
    /// </para>
    /// </summary>
    public class UiFinalHudWarningContainmentTests
    {
        private UiFinalPlayMode.Globals globals;
        private Dictionary<string, string> slotBackup;

        /// <summary>Sub-pixel rounding and deliberate hairline bleed are not defects.</summary>
        private const float Tolerance = 2f;

        /// <summary>The reference viewport, in canvas units. See the class summary.</summary>
        private static readonly Vector2 Viewport1280x720 = new Vector2(1920f, 1080f);

        [SetUp]
        public void CaptureTheWorldAsItWasFound()
        {
            globals = UiFinalPlayMode.CaptureGlobals();
            slotBackup = UiFinalPlayMode.BackupSlots();
            UiFinalPlayMode.DeleteEverySlot();
        }

        [TearDown]
        public void LeaveNothingForTheNextTest()
        {
            foreach (NebulaUiShell shell in Object.FindObjectsOfType<NebulaUiShell>(true))
                if (shell != null) shell.Shutdown();

            SolarSystemBootstrap boot = UiFinalPlayMode.Boot();
            if (boot != null && boot.SessionActive) boot.EndSession();

            UiFinalPlayMode.ClearSessionStatics();
            UiFinalPlayMode.RestoreGlobals(globals);
            UiFinalPlayMode.RestoreSlots(slotBackup);
        }

        // =====================================================================
        // Driving the two warnings through the state the widgets really read
        // =====================================================================

        // Every one of these now lives in UiFinalHudWarningState, which is shared
        // with UiFinalHudWarningCaptureTests. The two fixtures have to be looking
        // at the same raid and the same overfilled hold for the pictures that
        // fixture writes to be evidence about what this one measures, and "the
        // same" is only checkable if there is one copy. These forwarders keep the
        // tests below reading exactly as they did.
        private static NebulaHudAlerts Alerts() => UiFinalHudWarningState.Alerts();

        private static ShipCombat Combat() => UiFinalHudWarningState.Combat();

        private static LogisticsCargo ShipCargo() => UiFinalHudWarningState.ShipCargo();

        /// <inheritdoc cref="UiFinalHudWarningState.RaiseARaidWarning"/>
        private static IEnumerator RaiseARaidWarning() =>
            UiFinalHudWarningState.RaiseARaidWarning();

        /// <inheritdoc cref="UiFinalHudWarningState.OverfillTheHold"/>
        private static IEnumerator OverfillTheHold() =>
            UiFinalHudWarningState.OverfillTheHold();

        private static RectTransform Find(Transform root, string name) =>
            UiFinalHudWarningState.Find(root, name);

        private static RectTransform HudRoot() => UiFinalHudWarningState.HudRoot();

        private static bool CargoWarningVisible() => UiFinalHudWarningState.CargoWarningVisible();

        // =====================================================================
        // The reference viewport
        // =====================================================================

        private struct CanvasHold
        {
            public Canvas Canvas;
            public RenderMode Mode;
            public Vector2 Size, AnchorMin, AnchorMax, Pivot;
            public Vector3 Scale, LocalPosition;
        }

        /// <summary>
        /// Holds the HUD canvases at an exact logical viewport for one synchronous
        /// measurement, then puts them back.
        /// <para>
        /// A ScreenSpaceOverlay canvas's rect is driven by Unity from the real screen,
        /// so it cannot be set. World space is the one mode where the rect belongs to
        /// the author, and the widgets underneath are anchored to that rect and lay
        /// out against it exactly as they do on screen. Restored in a <c>finally</c>:
        /// a canvas left in world space would silently break every later test in the
        /// run, which is the failure the interaction-capture fixture already documents
        /// for the same surgery.
        /// </para>
        /// </summary>
        private static void MeasureAt(Vector2 logicalSize, System.Action<RectTransform> measure)
        {
            NebulaUiShell shell = UiFinalPlayMode.Shell();
            Assert.IsNotNull(shell, "there is no shell to measure");

            var held = new List<CanvasHold>();
            try
            {
                // Only the dynamic layer, which is where all three clusters live. The
                // static layer carries a GraphicRaycaster, and a world-space canvas
                // with a raycaster and no event camera is a warning nobody needs.
                foreach (UiLayer layer in new[] { UiLayer.HudDynamic })
                {
                    Canvas canvas = shell.CanvasFor(layer);
                    Assert.IsNotNull(canvas, "the shell has no canvas for " + layer);
                    var rect = (RectTransform)canvas.transform;

                    held.Add(new CanvasHold
                    {
                        Canvas = canvas,
                        Mode = canvas.renderMode,
                        Size = rect.sizeDelta,
                        AnchorMin = rect.anchorMin,
                        AnchorMax = rect.anchorMax,
                        Pivot = rect.pivot,
                        Scale = rect.localScale,
                        LocalPosition = rect.localPosition,
                    });

                    canvas.renderMode = RenderMode.WorldSpace;
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.localScale = Vector3.one;
                    rect.localPosition = Vector3.zero;
                    rect.sizeDelta = logicalSize;
                }

                Canvas.ForceUpdateCanvases();

                RectTransform hud = (RectTransform)shell.CanvasFor(UiLayer.HudDynamic).transform;
                Assert.AreEqual(logicalSize.x, hud.rect.width, Tolerance,
                    "the HUD canvas would not take the reference viewport width, so nothing " +
                    "measured below is measured at that viewport");
                Assert.AreEqual(logicalSize.y, hud.rect.height, Tolerance,
                    "the HUD canvas would not take the reference viewport height");

                measure(hud);
            }
            finally
            {
                foreach (CanvasHold hold in held)
                {
                    var rect = (RectTransform)hold.Canvas.transform;
                    hold.Canvas.renderMode = hold.Mode;
                    rect.anchorMin = hold.AnchorMin;
                    rect.anchorMax = hold.AnchorMax;
                    rect.pivot = hold.Pivot;
                    rect.localScale = hold.Scale;
                    rect.localPosition = hold.LocalPosition;
                    rect.sizeDelta = hold.Size;
                }
                Canvas.ForceUpdateCanvases();
            }
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

        private static bool Overlaps(Rect a, Rect b) =>
            a.xMin < b.xMax - Tolerance && b.xMin < a.xMax - Tolerance &&
            a.yMin < b.yMax - Tolerance && b.yMin < a.yMax - Tolerance;

        /// <summary>Every drawn label under a panel, asserted to sit inside the safe area.</summary>
        private static void AssertInside(RectTransform safeArea, RectTransform panel, string what)
        {
            Assert.IsNotNull(panel, $"there is no '{what}' on screen to measure");
            Assert.IsTrue(panel.gameObject.activeInHierarchy,
                $"'{what}' is switched off, so measuring it proves nothing");

            Rect outer = WorldRect(safeArea);
            var failures = new List<string>();
            int measured = 0;

            foreach (RectTransform child in panel.GetComponentsInChildren<RectTransform>(true))
            {
                if (!child.gameObject.activeInHierarchy) continue;
                measured++;
                Rect inner = WorldRect(child);
                if (inner.xMin < outer.xMin - Tolerance || inner.xMax > outer.xMax + Tolerance
                    || inner.yMin < outer.yMin - Tolerance || inner.yMax > outer.yMax + Tolerance)
                {
                    failures.Add($"{child.name} x[{inner.xMin:0},{inner.xMax:0}] y[{inner.yMin:0},{inner.yMax:0}]");
                }
            }

            Assert.Greater(measured, 0, $"'{what}' drew nothing, so containment was never checked");
            Assert.IsEmpty(failures,
                $"'{what}' renders outside the 1280x720 safe area x[{outer.xMin:0},{outer.xMax:0}] "
                + $"y[{outer.yMin:0},{outer.yMax:0}]:\n  " + string.Join("\n  ", failures));
        }

        // =====================================================================
        // 1. Each warning appears at all
        // =====================================================================

        [UnityTest]
        public IEnumerator TheRaidWarning_AppearsWhenARaidIsReallyPending()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            yield return RaiseARaidWarning();

            NebulaHudAlerts alerts = Alerts();
            Assert.IsTrue(alerts.RaidVisible, "a pending raid put no banner on screen");
            Assert.IsFalse(string.IsNullOrEmpty(alerts.RaidHeadline),
                "the raid banner is on screen with nothing written on it");
            StringAssert.Contains("AKIN", alerts.RaidHeadline,
                "the raid banner's headline does not name what it is warning about");
        }

        [UnityTest]
        public IEnumerator TheCargoWarning_AppearsWhenTheHoldIsReallyOverCapacity()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            yield return OverfillTheHold();

            Assert.IsTrue(CargoWarningVisible(), "an over-capacity hold put no warning on screen");

            RectTransform warning = Find(Find(HudRoot(), "Cargo"), "Warning");
            var label = warning.GetComponent<TextMeshProUGUI>();
            Assert.IsNotNull(label, "the cargo warning is not a label");
            StringAssert.Contains("KAPASİTE", label.text,
                "the cargo warning does not say what is wrong");
        }

        // =====================================================================
        // 2. Both at once, contained, and clear of everything else
        // =====================================================================

        [UnityTest]
        public IEnumerator BothWarningsTogether_StayInsideTheSafeAreaAndOffEachOtherAndOffTheVitals()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            yield return RaiseARaidWarning();
            yield return OverfillTheHold();

            Assert.IsTrue(Alerts().RaidVisible, "the raid banner went away when the cargo filled");
            Assert.IsTrue(CargoWarningVisible(), "the cargo warning went away when the raid started");

            MeasureAt(Viewport1280x720, safeArea =>
            {
                RectTransform raid = Find(safeArea, "Raid");
                RectTransform cargo = Find(safeArea, "Cargo");
                RectTransform vitals = Find(safeArea, "Vitals");

                AssertInside(safeArea, raid, "Raid");
                AssertInside(safeArea, cargo, "Cargo");
                Assert.IsNotNull(vitals, "the HUD has no vital cluster to stay clear of");
                Assert.IsTrue(vitals.gameObject.activeInHierarchy,
                    "the vital cluster is switched off, so 'the warnings do not cover it' is vacuous");

                Rect raidRect = WorldRect(raid);
                Rect cargoRect = WorldRect(cargo);
                Rect vitalsRect = WorldRect(vitals);

                Assert.IsFalse(Overlaps(raidRect, cargoRect),
                    $"the raid banner and the cargo cluster overlap — raid y[{raidRect.yMin:0},{raidRect.yMax:0}] "
                    + $"cargo y[{cargoRect.yMin:0},{cargoRect.yMax:0}]");
                Assert.IsFalse(Overlaps(raidRect, vitalsRect),
                    "the raid banner covers the ship's own vital cluster");
                Assert.IsFalse(Overlaps(cargoRect, vitalsRect),
                    "the cargo cluster covers the ship's own vital cluster");
            });
        }

        /// <summary>
        /// The cargo cluster grows when it shows its warning, and the grown height is
        /// what has to clear the banner above it. The collapsed height clearing it
        /// proves nothing, which is exactly the gap the EditMode constant check leaves
        /// open by comparing two numbers rather than two rendered rects.
        /// </summary>
        [UnityTest]
        public IEnumerator TheGrownCargoCluster_StillClearsTheBannerAboveIt()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            yield return RaiseARaidWarning();

            float collapsedBottom = 0f;
            MeasureAt(Viewport1280x720, safeArea =>
                collapsedBottom = WorldRect(Find(safeArea, "Cargo")).yMin);

            yield return OverfillTheHold();

            MeasureAt(Viewport1280x720, safeArea =>
            {
                RectTransform cargo = Find(safeArea, "Cargo");
                Rect grown = WorldRect(cargo);
                Assert.Less(grown.yMin, collapsedBottom - Tolerance,
                    "the cargo cluster did not grow when its warning appeared, so the warning "
                    + "is being drawn in space the panel does not own");

                AssertInside(safeArea, cargo, "Cargo");
                Assert.IsFalse(Overlaps(grown, WorldRect(Find(safeArea, "Raid"))),
                    "the grown cargo cluster now overlaps the raid banner");
            });
        }

        // =====================================================================
        // 3. Accessibility modes
        // =====================================================================

        /// <summary>
        /// Both warnings under High Contrast and under the warning Visual Cue.
        /// <para>
        /// The modes are set <b>before</b> the campaign composes its HUD, because that
        /// is the situation a player who has them switched on is in: the widgets are
        /// built from the palette that mode selects.
        /// </para>
        /// <para>
        /// Readability is measured on the colour the label is really carrying, against
        /// <c>Surface3</c> — the card and row surface these clusters are drawn on, and
        /// itself mode-dependent, so both halves of the ratio move with the mode.
        /// Token colours, not captured pixels: pixel measurement belongs to the capture
        /// fixture, which owns the render, and is recorded there. What this adds is
        /// that the widget on screen really is carrying that colour in that mode, which
        /// no token test can see — the cargo warning in particular takes its colour
        /// once, at build, and would keep a stale palette if the mode were only ever
        /// switched on afterwards.
        /// </para>
        /// <para>
        /// <b>What the Visual Cue leg claims.</b>
        /// <c>CombatAccessibilitySettings.AudioVisualCueForWarnings</c> now has a
        /// consumer on both of these warnings: each carries a
        /// <c>UiStatusCueView</c> mark that draws strokes when the switch is on and
        /// none when it is off. This was K-10, and it was open for a real reason —
        /// the audio half of the cue belongs to the audio workstream — but the
        /// visual half is this interface's, and it was left drawing nothing outside
        /// a capture fixture. The assertion below is the difference between a
        /// setting and a checkbox: strokes appear if and only if the player asked
        /// for them, and both warnings stay readable either way.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator BothWarnings_StayVisibleAndReadableUnderHighContrastAndTheVisualCue()
        {
            foreach ((string name, bool highContrast, bool cue) mode in new[]
                     {
                         ("High Contrast", true, false),
                         ("Visual Cue", false, true),
                         ("both together", true, true),
                     })
            {
                yield return UiFinalPlayMode.LoadSceneCold();

                LightingAccessibility.HighContrast = mode.highContrast;
                CombatAccessibilitySettings.AudioVisualCueForWarnings = mode.cue;

                yield return UiFinalPlayMode.BeginSession();
                yield return RaiseARaidWarning();
                yield return OverfillTheHold();

                Assert.IsTrue(Alerts().RaidVisible, $"{mode.name} hid the raid banner");
                Assert.IsTrue(CargoWarningVisible(), $"{mode.name} hid the cargo warning");

                // The switch's whole effect, asserted in both directions. "> 0 when
                // on" alone would pass for a mark that is always drawn, which is the
                // failure this setting has already had once.
                if (mode.cue)
                {
                    Assert.Greater(Alerts().RaidCueStrokes, 0,
                        $"{mode.name}: the raid banner drew no cue with cues switched on");
                    Assert.Greater(Alerts().CargoCueStrokes, 0,
                        $"{mode.name}: the cargo warning drew no cue with cues switched on");
                }
                else
                {
                    Assert.AreEqual(0, Alerts().RaidCueStrokes,
                        $"{mode.name}: the raid banner drew a cue with cues switched off");
                    Assert.AreEqual(0, Alerts().CargoCueStrokes,
                        $"{mode.name}: the cargo warning drew a cue with cues switched off");
                }

                var raidTitle = Find(Find(HudRoot(), "Raid"), "Title").GetComponent<TextMeshProUGUI>();
                var cargoWarning = Find(Find(HudRoot(), "Cargo"), "Warning").GetComponent<TextMeshProUGUI>();

                Assert.IsFalse(string.IsNullOrEmpty(raidTitle.text), $"{mode.name} emptied the raid headline");
                Assert.IsFalse(string.IsNullOrEmpty(cargoWarning.text), $"{mode.name} emptied the cargo warning");

                foreach ((string what, Color ink) in new[]
                         {
                             ("the raid headline", raidTitle.color),
                             ("the cargo warning", cargoWarning.color),
                         })
                {
                    float ratio = ContrastRatio(ink, NebulaUiDesignTokens.Surface3);
                    Assert.GreaterOrEqual(ratio, 4.5f,
                        $"under {mode.name}, {what} is {ratio:0.00}:1 against the surface a HUD "
                        + "card is drawn on — below the WCAG AA floor for text");
                    Assert.GreaterOrEqual(ink.a, 0.9f,
                        $"under {mode.name}, {what} is drawn nearly transparent");
                }

                // And the geometry is unchanged by either mode: a palette must not
                // move a rect.
                MeasureAt(Viewport1280x720, safeArea =>
                {
                    AssertInside(safeArea, Find(safeArea, "Raid"), "Raid");
                    AssertInside(safeArea, Find(safeArea, "Cargo"), "Cargo");
                    Assert.IsFalse(Overlaps(WorldRect(Find(safeArea, "Raid")),
                            WorldRect(Find(safeArea, "Cargo"))),
                        $"{mode.name} pushed the two warnings into each other");
                });

                yield return UiFinalPlayMode.EndSessionAndSettle();
            }
        }

        // =====================================================================
        // 4. Leaving nothing behind
        // =====================================================================

        /// <summary>
        /// A warning that has stopped applying must leave nothing drawn. A label that
        /// keeps its last text and merely loses its panel is the shape that produces a
        /// stale "CAPACITY EXCEEDED" over a half-empty hold.
        /// </summary>
        [UnityTest]
        public IEnumerator WhenAWarningClears_NothingIsLeftDrawn()
        {
            yield return UiFinalPlayMode.LoadSceneWithSession();
            yield return RaiseARaidWarning();
            yield return OverfillTheHold();

            int drawnWithBoth = DrawnLabelsUnder("Raid") + DrawnLabelsUnder("Cargo");
            Assert.Greater(drawnWithBoth, 0, "neither warning drew anything, so clearing them proves nothing");

            // Back to no raid, through the pressure state's own idle call.
            Combat().Director.Pressure.ClearToIdle();
            yield return UiFinalPlayMode.Until(() => !Alerts().RaidVisible,
                "the raid banner to come down once the raid is over");

            Assert.AreEqual(0, DrawnLabelsUnder("Raid"),
                "the raid banner's labels are still drawn after the banner came down");

            // And back under capacity, through the same module-tier effect that put
            // the hold over it.
            ShipCargo().SetCapacity(4000f);
            yield return UiFinalPlayMode.Until(() => !CargoWarningVisible(),
                "the over-capacity warning to come down once the hold fits again");

            RectTransform cargo = Find(HudRoot(), "Cargo");
            Assert.IsTrue(cargo.gameObject.activeInHierarchy,
                "the cargo cluster itself disappeared; only its warning row should have");
            Assert.IsFalse(Find(cargo, "Warning").gameObject.activeInHierarchy,
                "the over-capacity warning is still drawn over a hold that fits");

            // The cluster collapses back rather than leaving a hole where the warning
            // was, and everything still fits.
            MeasureAt(Viewport1280x720, safeArea =>
            {
                AssertInside(safeArea, Find(safeArea, "Cargo"), "Cargo");
                Assert.AreEqual(NebulaHudAlerts.CargoShortHeight,
                    WorldRect(Find(safeArea, "Cargo")).height, Tolerance,
                    "the cargo cluster stayed at its grown height with nothing to grow for");
            });
        }

        private static int DrawnLabelsUnder(string panelName)
        {
            RectTransform panel = Find(HudRoot(), panelName);
            if (panel == null || !panel.gameObject.activeInHierarchy) return 0;

            int drawn = 0;
            foreach (TextMeshProUGUI text in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (text.gameObject.activeInHierarchy && !string.IsNullOrEmpty(text.text)) drawn++;
            return drawn;
        }

        // =====================================================================
        // WCAG
        // =====================================================================

        private static float ContrastRatio(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            float hi = Mathf.Max(la, lb), lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        private static float Luminance(Color c) =>
            0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);

        private static float Channel(float v) =>
            v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
    }
}
