using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.UI.Accessibility;
using Nebula.UI.Design;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The resolution, UI-scale, long-text and accessibility evidence — each
    /// mode judged against what that mode actually promises.
    /// <para>
    /// The first version of this file judged all six accessibility modes the same
    /// way: it hashed one still frame per mode and treated a repeated hash as a
    /// defect. That is the right question for exactly two of them and the wrong
    /// question for the other four, and asking it uniformly produced a green
    /// suite over six captures sharing one MD5. Two different mistakes were
    /// hiding inside that one number.
    /// </para>
    /// <para>
    /// A motion setting has no business changing a still frame. Motion Off
    /// promises that the picture stops moving, which means every frame it
    /// produces is identical to the one before it — a mode whose captures match
    /// the baseline is passing, not failing, and the only honest way to see it is
    /// to watch the same surface over time. Reduced Motion promises a shorter
    /// journey and the *same destination*, so its final frame is supposed to
    /// match. Those three are measured here as time series.
    /// </para>
    /// <para>
    /// High Contrast and Visual Cue are the opposite. They promise a difference a
    /// player can see standing still, so for those two an identical frame is
    /// exactly the finding it was taken for, and both are held to a real pixel
    /// difference — plus, for High Contrast, WCAG ratios computed here from the
    /// tokens rather than quoted from a comment.
    /// </para>
    /// </summary>
    public class UiFinalResolutionAccessibilityTests
    {
        private static string Dir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime");

        /// <summary>Every resolution the capture matrix sweeps, worst case first.</summary>
        private static readonly string[] Resolutions =
        {
            "1280x720", "1920x1080", "2560x1440", "3440x1440-ultrawide", "1920x1200-16x10",
        };

        /// <summary>The UI-scale range <c>NebulaUiShell</c> clamps to, at both ends and the middle.</summary>
        private static readonly string[] Scales = { "80", "100", "140" };

        /// <summary>
        /// Written by <c>UiFinalInteractionStateCaptureTests</c>. Duplicated
        /// deliberately: that fixture is in the PlayMode assembly and this one
        /// cannot see it, and a list this audit derived from the producer could
        /// never catch the producer failing to produce.
        /// </summary>
        private static readonly string[] InteractionStates =
        {
            "state-button-default", "state-button-hover", "state-button-pressed",
            "state-button-focused", "state-button-disabled",
            "state-tab-default", "state-tab-hover", "state-tab-pressed",
            "state-tab-focused", "state-tab-selected",
            "state-row-default", "state-row-hover", "state-row-pressed", "state-row-focused",
            "state-settings-control-default", "state-settings-control-hover",
            "state-settings-control-pressed", "state-settings-control-focused",
            "state-settings-control-disabled",
            "state-slider-default", "state-slider-hover", "state-slider-pressed",
            "state-slider-focused", "state-slider-disabled",
            "state-toggle-off", "state-toggle-on", "state-toggle-hover",
            "state-toggle-focused", "state-toggle-disabled",
        };

        /// <summary>The six switches, plus the baseline every one of them is compared against.</summary>
        private static readonly string[] AccessibilityModes =
        {
            "state-a11y-baseline",
            "state-a11y-motion-off",
            "state-a11y-reduced-motion",
            "state-a11y-flash-reduction",
            "state-a11y-photosensitivity-safe",
            "state-a11y-high-contrast",
            "state-a11y-visual-cue",
        };

        /// <summary>
        /// The modes that govern motion. A still frame cannot show any of them, so
        /// none of them is required to differ from the baseline and none is
        /// compared against pixels.
        /// </summary>
        private static readonly string[] MotionGovernedModes =
        {
            "state-a11y-motion-off",
            "state-a11y-reduced-motion",
            "state-a11y-flash-reduction",
            "state-a11y-photosensitivity-safe",
        };

        /// <summary>The modes that promise a visible difference standing still.</summary>
        private static readonly string[] StaticallyVisibleModes =
        {
            "state-a11y-high-contrast",
            "state-a11y-visual-cue",
        };

        /// <summary>
        /// The player-facing screens that are not pages on the rail, and the two
        /// panels that open inside a page.
        /// <para>
        /// The capture matrix used to begin at the parent menu, which is the second
        /// thing a player sees. These are the first thing and the two second-level
        /// surfaces behind it — a screen nobody photographs is a screen nobody
        /// reviews, and the save-slot list carries a per-slot refusal reason, which
        /// is exactly the kind of string that ships empty when nothing looks at it.
        /// </para>
        /// </summary>
        private static readonly string[] StandaloneScreens =
        {
            "main-menu-1920x1080", "main-menu-1280x720",
            "save-slots-1920x1080", "save-slots-1280x720",
            "credits-1920x1080",
            "settings-bindings-1920x1080", "settings-bindings-1280x720",
            "fleet-logistics-1920x1080", "fleet-logistics-1280x720",
        };

        private const string LongText = "state-longtext-turkish";
        private const string Baseline = "state-a11y-baseline";

        /// <summary>The size <c>UiFinalInteractionStateCaptureTests</c> renders at.</summary>
        private const int StateWidth = 1280;
        private const int StateHeight = 720;

        // The time series below are sampled at a fixed rate rather than at
        // whatever the editor happens to tick at, so a slow machine measures the
        // same numbers as a fast one.
        private const float SampleSeconds = 1f / 120f;
        private const int MotionFrames = 120;      // one second of a 0.19 s transition
        private const int PulseFrames = 600;       // five seconds, enough for five 1 Hz cycles

        private bool savedMotionOff, savedReducedMotion, savedComfort;
        private bool savedFlash, savedSafeMode, savedHighContrast, savedCue;

        [SetUp]
        public void Setup()
        {
            savedMotionOff = AnimationAccessibility.MotionOff;
            savedReducedMotion = AnimationAccessibility.ReducedMotion;
            savedComfort = AnimationAccessibility.CameraComfort;
            savedFlash = LightingAccessibility.FlashReduction;
            savedSafeMode = LightingAccessibility.PhotosensitivitySafeMode;
            savedHighContrast = LightingAccessibility.HighContrast;
            savedCue = Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings;
            AllOff();
        }

        [TearDown]
        public void Teardown()
        {
            AnimationAccessibility.MotionOff = savedMotionOff;
            AnimationAccessibility.ReducedMotion = savedReducedMotion;
            AnimationAccessibility.CameraComfort = savedComfort;
            LightingAccessibility.FlashReduction = savedFlash;
            LightingAccessibility.PhotosensitivitySafeMode = savedSafeMode;
            LightingAccessibility.HighContrast = savedHighContrast;
            Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = savedCue;
        }

        /// <summary>
        /// The baseline every mode is measured against: nothing on, including the
        /// visual cue, which ships on by default and therefore cannot be part of a
        /// baseline that means "no accessibility aid is running".
        /// </summary>
        private static void AllOff()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
            Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = false;
        }

        // =====================================================================
        // 1. the disk audit — was the evidence produced at all
        // =====================================================================

        [Test]
        public void ParentMenuWasCapturedAtEveryResolution()
        {
            var missing = new List<string>();
            foreach (string resolution in Resolutions)
            {
                string file = $"parent-menu-{resolution}.png";
                if (!File.Exists(Path.Combine(Dir, file))) missing.Add(file);
            }
            Assert.IsEmpty(missing, "resolutions never photographed: " + string.Join(", ", missing));
        }

        /// <summary>The surfaces swept at every UI scale, not just the first one.</summary>
        private static readonly string[] ScaledSurfaces = { "parent-menu", "page-settings" };

        [Test]
        public void EveryScaledSurfaceWasCapturedAtEveryUiScale()
        {
            var missing = new List<string>();
            foreach (string surface in ScaledSurfaces)
            foreach (string scale in Scales)
            {
                string file = $"{surface}-scale-{scale}.png";
                if (!File.Exists(Path.Combine(Dir, file))) missing.Add(file);
            }
            Assert.IsEmpty(missing,
                "the UI scale is a setting the player can change; it has to be seen at both ends. "
                + "Missing: " + string.Join(", ", missing));
        }

        [Test]
        public void EveryResolutionCaptureHasTheSizeItsNameClaims()
        {
            foreach (string resolution in Resolutions)
                AssertDeclaredSize(Dir, $"parent-menu-{resolution}.png");
        }

        [Test]
        public void NoResolutionOrScaleCaptureIsBlank()
        {
            foreach (string resolution in Resolutions)
                AssertNotBlank(Dir, $"parent-menu-{resolution}.png");
            foreach (string surface in ScaledSurfaces)
            foreach (string scale in Scales)
                AssertNotBlank(Dir, $"{surface}-scale-{scale}.png");
        }

        /// <summary>
        /// Three different UI scales that came out identical would mean the scale
        /// never reached the canvas — which is exactly the failure the setting
        /// would have if it were bound to nothing.
        /// </summary>
        [Test]
        public void NoTwoUiScalesRenderedIdentically()
        {
            foreach (string surface in ScaledSurfaces)
            {
                var files = new List<string>();
                foreach (string scale in Scales) files.Add($"{surface}-scale-{scale}.png");
                AssertAllDistinct(Dir, files, surface + " UI scale");
            }
        }

        /// <summary>
        /// The main menu, the save-slot list, the credits, the rebinding panel and
        /// the Logistics Centre — present, real, and the size their names claim.
        /// </summary>
        [Test]
        public void EveryStandaloneScreenWasCaptured()
        {
            AssertPresent(StandaloneScreens, "standalone screen");
            foreach (string screen in StandaloneScreens)
            {
                AssertNotBlank(Dir, screen + ".png");
                AssertDeclaredSize(Dir, screen + ".png");
            }
        }

        /// <summary>
        /// A second-level panel that opens over a page must not photograph
        /// identically to the page it opened over — that would mean the opener is
        /// wired to nothing, which is the defect these two surfaces are new enough
        /// to still have.
        /// </summary>
        [Test]
        public void EachInPagePanelChangesThePageItOpensOver()
        {
            foreach ((string page, string withPanel) in new[]
                     {
                         ("page-settings-1920x1080", "settings-bindings-1920x1080"),
                         ("page-fleet-1920x1080", "fleet-logistics-1920x1080"),
                     })
            {
                Assert.AreNotEqual(
                    Fingerprint(Path.Combine(Dir, page + ".png")),
                    Fingerprint(Path.Combine(Dir, withPanel + ".png")),
                    withPanel + " is byte-identical to " + page
                    + " — the panel's opener reached nothing.");

                ImageDelta delta = Compare(page, withPanel);
                Assert.Greater(delta.StronglyChangedPixels, 5000,
                    $"only {delta.StronglyChangedPixels} pixels changed when {withPanel} "
                    + "opened; a panel covers rather more of a page than that.");
            }
        }

        [Test]
        public void EveryInteractionStateWasCaptured()
        {
            AssertPresent(InteractionStates, "interaction state");
        }

        [Test]
        public void EveryInteractionStateCaptureIsRealAndCorrectlySized()
        {
            foreach (string state in InteractionStates)
            {
                AssertNotBlank(Dir, state + ".png");
                AssertSize(Dir, state + ".png", StateWidth, StateHeight);
            }
        }

        /// <summary>
        /// The point of the whole state set. Grouped per widget, because two
        /// widgets naturally differ and would hide a widget whose own states do
        /// not: a button that looks the same hovered as at rest is the defect.
        /// </summary>
        [Test]
        public void NoWidgetRendersTwoOfItsOwnStatesIdentically()
        {
            foreach (string prefix in new[]
                     {
                         "state-button-", "state-tab-", "state-row-",
                         "state-settings-control-", "state-slider-", "state-toggle-",
                     })
            {
                var group = new List<string>();
                foreach (string state in InteractionStates)
                    if (state.StartsWith(prefix)) group.Add(state + ".png");
                Assert.Greater(group.Count, 1, prefix + " has no states to compare");
                AssertAllDistinct(Dir, group, prefix.TrimEnd('-'));
            }
        }

        /// <summary>
        /// Pairs that are legitimately one picture, each with the reason it is.
        /// Everything else that matches is a capture whose filename claims a
        /// subject it does not show.
        /// </summary>
        private static readonly string[][] IdenticalOnPurpose =
        {
            new[]
            {
                "state-slider-default", "state-toggle-off",
                "both are the settings page with nothing touched, and a toggle at rest is "
                + "off — so the resting frame of the scale row and the resting frame of the "
                + "toggle row are the same photograph of the same page. Every frame in "
                + "which something is done to either widget differs, which is what those "
                + "two rows are there to show.",
            },
        };

        /// <summary>
        /// <c>AssertAllDistinct</c> in the capture fixture compares frames within
        /// one widget's own states, which is the right question for that fixture
        /// and leaves a gap this one has to close: two captures produced by two
        /// different tests, claiming two different subjects, can be byte-identical
        /// and nothing notices.
        /// <para>
        /// It happened. <c>state-slider-default</c>, <c>state-toggle-off</c> and
        /// <c>state-longtext-turkish</c> shared one MD5 — a picture of the plain
        /// settings page. The first two are the same page at rest and are
        /// allow-listed with that stated. The third was not defensible: the
        /// Turkish worst case builds a context bar carrying the longest strings in
        /// the game and the capture rendered a layer that bar is not on, so the
        /// evidence for the case contained none of the strings that make it one.
        /// </para>
        /// </summary>
        [Test]
        public void NoTwoCapturesOfDifferentSubjectsAreTheSamePicture()
        {
            var files = new List<string>(InteractionStates) { LongText };
            var byHash = new Dictionary<string, string>();
            var collisions = new List<string>();

            foreach (string file in files)
            {
                string path = Path.Combine(Dir, file + ".png");
                Assert.IsTrue(File.Exists(path), file + ".png is missing from " + Dir);
                string hash = Fingerprint(path);

                if (byHash.TryGetValue(hash, out string other))
                {
                    if (!IsAllowedTwin(file, other))
                        collisions.Add($"{file} and {other}");
                }
                else byHash[hash] = file;
            }

            Assert.IsEmpty(collisions,
                "two captures claiming different subjects are byte-identical:\n  "
                + string.Join("\n  ", collisions)
                + "\nEither the second capture never photographed what its name says, or "
                + "the pair is legitimately one picture and belongs in IdenticalOnPurpose "
                + "with the reason written down.");

            // An allow-list nobody checks rots into a mute list. Every entry has to
            // still be describing a real collision.
            foreach (string[] entry in IdenticalOnPurpose)
                Assert.AreEqual(Fingerprint(Path.Combine(Dir, entry[0] + ".png")),
                    Fingerprint(Path.Combine(Dir, entry[1] + ".png")),
                    $"{entry[0]} and {entry[1]} are no longer identical, so the allow-list "
                    + "entry excusing them is stale and should be deleted.");
        }

        private static bool IsAllowedTwin(string a, string b)
        {
            foreach (string[] entry in IdenticalOnPurpose)
                if ((entry[0] == a && entry[1] == b) || (entry[0] == b && entry[1] == a))
                    return true;
            return false;
        }

        /// <summary>
        /// The captures whose whole purpose is to look different from something else.
        /// A mode that promises a visible change, and the three interaction states
        /// that exist to be told apart from a resting one, can never be excused for
        /// matching another picture — an allow-list entry naming one of these would be
        /// muting the finding it was taken for.
        /// </summary>
        private static readonly string[] NeverAllowListable =
        {
            "high-contrast", "visual-cue", "-selected", "-disabled", "-focused",
        };

        /// <summary>
        /// The allow-list itself, held to its own rules.
        /// <para>
        /// An allow-list is the part of a check that can quietly turn it off, so it is
        /// checked as tightly as the thing it excuses. Three rules, and each is one a
        /// real mistake would break: every entry names two <b>specific</b> captures
        /// that actually exist and carries a written reason, so no entry can be a
        /// pattern that swallows a family; nothing may be excused whose entire purpose
        /// is to differ; and matching is by exact name in both directions, so an entry
        /// cannot leak onto a capture it does not name.
        /// </para>
        /// <para>
        /// The staleness check lives in
        /// <see cref="NoTwoCapturesOfDifferentSubjectsAreTheSamePicture"/>: an entry
        /// whose two captures have stopped matching is deleted rather than kept.
        /// </para>
        /// </summary>
        [Test]
        public void TheIdenticalOnPurposeAllowList_NamesSpecificCapturesAndExcusesNothingThatMustDiffer()
        {
            var known = new List<string>(InteractionStates) { LongText };
            known.AddRange(AccessibilityModes);

            foreach (string[] entry in IdenticalOnPurpose)
            {
                Assert.AreEqual(3, entry.Length,
                    "every allow-list entry is exactly two capture names and one written reason; "
                    + "an entry of any other shape is a rule nobody can read: "
                    + string.Join(" | ", entry));

                for (int i = 0; i < 2; i++)
                {
                    string name = entry[i];
                    Assert.IsTrue(known.Contains(name),
                        $"{name} is allow-listed and is not a capture this audit knows about. "
                        + "An entry naming nothing is an entry that will never be re-checked.");
                    Assert.IsFalse(name.Contains("*") || name.Contains("?"),
                        $"{name} looks like a pattern. Entries name one capture each — a pattern "
                        + "would excuse captures nobody decided about.");

                    foreach (string forbidden in NeverAllowListable)
                        Assert.IsFalse(name.Contains(forbidden),
                            $"{name} is allow-listed, and a capture containing '{forbidden}' exists "
                            + "precisely to differ from another picture. Excusing it would mute the "
                            + "finding it was taken for.");
                }

                Assert.AreNotEqual(entry[0], entry[1],
                    "an entry excusing a capture against itself excuses nothing and hides a typo");
                Assert.Greater(entry[2].Length, 60,
                    $"{entry[0]}/{entry[1]} is excused by {entry[2].Length} characters of reason. "
                    + "The reason is the whole value of the entry; a one-liner is a mute button.");
            }

            // Matching is by exact name in both directions and nothing else. The
            // negative case is what proves it: two captures that must always differ
            // are not excused just because an allow-list exists.
            Assert.IsFalse(IsAllowedTwin("state-a11y-high-contrast", Baseline),
                "High Contrast is being excused against the baseline");
            Assert.IsFalse(IsAllowedTwin("state-a11y-visual-cue", Baseline),
                "the warning Visual Cue is being excused against the baseline");
            Assert.IsFalse(IsAllowedTwin("state-tab-selected", "state-tab-default"),
                "a selected tab is being excused against a resting one");
            Assert.IsFalse(IsAllowedTwin("state-button-disabled", "state-button-default"),
                "a disabled button is being excused against an enabled one");
            Assert.IsFalse(IsAllowedTwin("state-button-focused", "state-button-default"),
                "a focused button is being excused against an unfocused one");

            // And the one legitimate pair is still expressed as a pair, not as a
            // blanket permission: swapping either half for a stranger is not allowed.
            Assert.IsTrue(IsAllowedTwin("state-slider-default", "state-toggle-off"));
            Assert.IsTrue(IsAllowedTwin("state-toggle-off", "state-slider-default"),
                "the allow-list is order-dependent, so half of every collision goes unexcused");
            Assert.IsFalse(IsAllowedTwin("state-slider-default", "state-toggle-on"),
                "the resting-page excuse leaked onto a toggle that has been switched on");
            Assert.IsFalse(IsAllowedTwin("state-slider-default", LongText),
                "the resting-page excuse leaked onto the Turkish long-text case, which is "
                + "the exact collision that was found and rejected once already");
        }

        [Test]
        public void EveryAccessibilityModeWasCaptured()
        {
            AssertPresent(AccessibilityModes, "accessibility mode");
        }

        [Test]
        public void EveryAccessibilityCaptureIsRealAndCorrectlySized()
        {
            foreach (string mode in AccessibilityModes)
            {
                AssertNotBlank(Dir, mode + ".png");
                AssertSize(Dir, mode + ".png", StateWidth, StateHeight);
            }
        }

        [Test]
        public void TheLongTurkishTextCaseWasCaptured()
        {
            string path = Path.Combine(Dir, LongText + ".png");
            Assert.IsTrue(File.Exists(path),
                LongText + ".png is missing — Turkish is longer than the English these rects "
                + "were sized against, and it is the case that finds a clipped control");
            AssertNotBlank(Dir, LongText + ".png");
            AssertSize(Dir, LongText + ".png", StateWidth, StateHeight);
        }

        // =====================================================================
        // 2. motion off — a transition that lands in one frame and then holds
        // =====================================================================

        /// <summary>
        /// Motion Off promises three things and each is a separate way to fail:
        /// the transition reaches its final pose within a single frame, every
        /// frame after that is identical to the one before it, and nothing
        /// travels any distance to get there.
        /// <para>
        /// Measured on the real <see cref="UiPanelAnimator"/>, driven a frame at a
        /// time. Not on a still capture — a still capture of a motionless surface
        /// and a still capture of a surface caught mid-slide are the same picture.
        /// </para>
        /// </summary>
        [Test]
        public void MotionOff_LandsInOneFrameAndNeverMovesAgain()
        {
            AllOff();
            AnimationAccessibility.MotionOff = true;

            Series motion = PanelOpening(MotionFrames);

            Assert.AreEqual(0f, motion.Offset[0], 0.0001f,
                "Motion Off must reach the final pose on the first frame; the panel was still "
                + motion.Offset[0].ToString("0.###") + " units away from it.");
            Assert.AreEqual(1f, motion.Alpha[0], 0.0001f,
                "Motion Off must reach full opacity on the first frame.");

            Assert.AreEqual(0f, motion.PeakOffset, 0.0001f,
                "Motion Off left the panel a distance to travel: peak offset "
                + motion.PeakOffset.ToString("0.###"));
            Assert.AreEqual(0f, motion.MaxStep, 0.0001f,
                "two consecutive frames differ under Motion Off — the biggest single-frame "
                + "change was " + motion.MaxStep.ToString("0.####")
                + ", so something is still moving.");
            Assert.AreEqual(0f, motion.MaxAlphaStep, 0.0001f,
                "the panel is still fading under Motion Off.");

            // ...and the settings it is built out of say the same thing.
            Assert.IsTrue(NebulaUiDesignTokens.SnapTransitions);
            Assert.AreEqual(0f, NebulaUiDesignTokens.MotionDuration(UiPanelPhase.Entering));
            Assert.AreEqual(0f, NebulaUiDesignTokens.SlideDistance());
            Assert.IsFalse(UiEmphasisPulse.Repeats,
                "nothing may repeat under Motion Off either — a pulse is motion.");
        }

        /// <summary>
        /// Nothing anywhere in the interface may slide, pulse, bank or repeat once
        /// Motion Off is on, so every producer of motion this project has is swept
        /// rather than only the panel.
        /// </summary>
        [Test]
        public void MotionOff_SilencesEverySourceOfMotionInTheInterface()
        {
            AllOff();
            AnimationAccessibility.MotionOff = true;

            Assert.AreEqual(0f, AnimationAccessibility.MotionScale, 0.0001f, "decorative amplitude");
            Assert.AreEqual(0f, AnimationAccessibility.ScaleDecorative(100f), 0.0001f, "decorative amplitude");
            Assert.AreEqual(0f, AnimationAccessibility.BankCeilingDegrees, 0.0001f, "visual bank");
            Assert.AreEqual(0f, UiAnimationProfiles.SlideDistance(), 0.0001f, "panel slide");
            Assert.AreEqual(0f, UiAnimationProfiles.DurationFor(UiPanelPhase.Entering), 0.0001f);
            Assert.AreEqual(0f, UiAnimationProfiles.DurationFor(UiPanelPhase.Exiting), 0.0001f);

            // A bar chases its value; under Motion Off it is handed the real value.
            Assert.AreEqual(0.42f, UiAnimationProfiles.ChaseFill(0f, 0.42f, SampleSeconds), 0.0001f,
                "a bar is still animating toward its value under Motion Off.");

            // A notification fades; under Motion Off it appears.
            var notification = new UiNotificationAnimator();
            Assert.AreEqual(1f, notification.Tick(true, SampleSeconds), 0.0001f,
                "a notification is still fading in under Motion Off.");

            // A value highlight is a one-shot flash and must not be raised at all.
            var value = new UiValueAnimator();
            value.AdoptSilently(0f);
            value.RaiseDelta(10f);
            Assert.IsFalse(value.IsHighlighting,
                "a delta highlight fired under Motion Off.");

            var pulse = new UiEmphasisPulse();
            Series pulses = PulseSeries(pulse, PulseFrames);
            Assert.AreEqual(0f, pulses.PeakOffset, 0.0001f,
                "the emphasis pulse moved under Motion Off.");
        }

        // =====================================================================
        // 3. reduced motion — a shorter journey to the same place
        // =====================================================================

        /// <summary>
        /// Reduced Motion is the mode a single-frame hash gets most wrong. It
        /// deliberately ends where normal motion ends — the destination is the
        /// authored layout and moving it would be a bug — so its final frame
        /// matching the baseline is the design working. What must differ is how
        /// far the panel travelled to get there, and that only exists over time.
        /// </summary>
        [Test]
        public void ReducedMotion_TravelsMeasurablyLessAndStillArrives()
        {
            AllOff();
            Series normal = PanelOpening(MotionFrames);

            AllOff();
            AnimationAccessibility.ReducedMotion = true;
            Series reduced = PanelOpening(MotionFrames);

            Assert.Greater(normal.PeakOffset, 0f,
                "normal motion did not move the panel at all, so there is nothing to reduce.");
            Assert.Greater(reduced.PeakOffset, 0f,
                "Reduced Motion removed the transition instead of shortening it. "
                + "A change the player cannot see happen is a change they have to notice "
                + "afterwards; that is Motion Off's job, not this one's.");

            Assert.Less(reduced.PeakOffset, normal.PeakOffset * 0.5f,
                $"Reduced Motion travelled {reduced.PeakOffset:0.##} against normal's "
                + $"{normal.PeakOffset:0.##} — not a reduction a player would notice.");
            Assert.Less(reduced.PathLength, normal.PathLength * 0.5f,
                $"total distance travelled barely changed: {reduced.PathLength:0.##} "
                + $"against {normal.PathLength:0.##}.");
            Assert.Less(reduced.MaxStep, normal.MaxStep,
                "the biggest single-frame jump did not get smaller, so the picture still "
                + "swims at the same rate.");

            // The destination is deliberately identical. Asserted, so that a later
            // change that moves the reduced-motion layout is caught rather than
            // congratulated for producing a difference.
            Assert.AreEqual(normal.Offset[normal.Offset.Count - 1],
                reduced.Offset[reduced.Offset.Count - 1], 0.0001f,
                "Reduced Motion must arrive exactly where normal motion arrives.");
            Assert.AreEqual(normal.Alpha[normal.Alpha.Count - 1],
                reduced.Alpha[reduced.Alpha.Count - 1], 0.0001f);

            Assert.AreEqual(UiAnimationProfiles.PanelSlideDistance
                            * UiAnimationProfiles.ReducedMotionDistanceScale,
                UiAnimationProfiles.SlideDistance(), 0.0001f);

            WriteSeriesStrip("state-a11y-motion-timeseries.png",
                new[] { normal.Offset, reduced.Offset },
                new[] { LightingUiPalette.Caution, LightingUiPalette.Nominal });
        }

        // =====================================================================
        // 4. flash reduction — how often anything is allowed to repeat
        // =====================================================================

        /// <summary>
        /// Measured as a real time series off the one thing in the interface
        /// allowed to repeat, <see cref="UiEmphasisPulse"/>, sampled at 120 Hz for
        /// five seconds and counted by upward crossings of its own midpoint.
        /// <para>
        /// Two numbers, not one. Rate is the famous one — three hertz is the
        /// general flash threshold and this design sits at two, dropping to one —
        /// but a slow deep pulse and a fast shallow one are not equally safe, so
        /// the depth of the swing is measured alongside it.
        /// </para>
        /// </summary>
        [Test]
        public void FlashReduction_HalvesTheRepeatRateAndShallowsTheSwing()
        {
            AllOff();
            var pulse = new UiEmphasisPulse();
            Series normal = PulseSeries(pulse, PulseFrames);
            float normalHz = normal.Hz(PulseFrames * SampleSeconds);

            AllOff();
            LightingAccessibility.FlashReduction = true;
            pulse.Reset();
            Series reduced = PulseSeries(pulse, PulseFrames);
            float reducedHz = reduced.Hz(PulseFrames * SampleSeconds);

            Assert.Greater(normalHz, 0f, "nothing repeated at all, so there is no rate to reduce.");
            Assert.LessOrEqual(normalHz, UiEmphasisPulse.AbsoluteCeilingHz,
                $"the interface repeats at {normalHz:0.00} Hz with no accessibility setting on — "
                + "above the 3 Hz general flash threshold.");
            Assert.LessOrEqual(reducedHz, UiEmphasisPulse.AbsoluteCeilingHz);

            Assert.Less(reducedHz, normalHz,
                $"Flash Reduction left the rate at {reducedHz:0.00} Hz against a normal "
                + $"{normalHz:0.00} Hz.");
            Assert.AreEqual(1f, reducedHz, 0.15f,
                $"the design's stated limit under Flash Reduction is about 1 Hz; measured "
                + $"{reducedHz:0.00} Hz.");

            Assert.Less(reduced.Range, normal.Range * 0.75f,
                $"the swing barely shallowed: {reduced.Range:0.###} against {normal.Range:0.###}. "
                + "Halving the rate while keeping the depth halves the number of flashes and "
                + "not the size of any of them.");

            // Luminance change per second is the thing a player's eye integrates,
            // and it is the product of the two numbers above.
            Assert.Less(reduced.ChangePerSecond(PulseFrames * SampleSeconds),
                normal.ChangePerSecond(PulseFrames * SampleSeconds) * 0.5f,
                "total luminance change per second did not halve.");

            Assert.AreEqual(1f, UiAnimationProfiles.MaxRepeatHz, 0.0001f,
                "the declared ceiling and the measured rate must be the same number.");

            WriteSeriesStrip("state-a11y-flash-timeseries.png",
                new[] { normal.Alpha, reduced.Alpha },
                new[] { LightingUiPalette.Critical, LightingUiPalette.Nominal });
        }

        /// <summary>
        /// The ceiling is a ceiling in every configuration, not only the two the
        /// test above happens to look at.
        /// </summary>
        [Test]
        public void NothingInTheInterfaceEverRepeatsAboveThreeHertz()
        {
            foreach (bool flash in new[] { false, true })
            foreach (bool safe in new[] { false, true })
            foreach (bool motionOff in new[] { false, true })
            {
                AllOff();
                LightingAccessibility.FlashReduction = flash;
                LightingAccessibility.PhotosensitivitySafeMode = safe;
                AnimationAccessibility.MotionOff = motionOff;

                var pulse = new UiEmphasisPulse();
                Series series = PulseSeries(pulse, PulseFrames);
                float hz = series.Hz(PulseFrames * SampleSeconds);

                string what = $"flash={flash} safe={safe} motionOff={motionOff}";
                Assert.LessOrEqual(hz, UiEmphasisPulse.AbsoluteCeilingHz,
                    $"{what} repeats at {hz:0.00} Hz.");
                Assert.LessOrEqual(UiAnimationProfiles.MaxRepeatHz,
                    UiEmphasisPulse.AbsoluteCeilingHz, what);
                Assert.LessOrEqual(UiEmphasisPulse.Hz, UiEmphasisPulse.AbsoluteCeilingHz, what);
            }
        }

        // =====================================================================
        // 5. photosensitivity — no repeat at all, and the state still readable
        // =====================================================================

        /// <summary>
        /// Safe Mode's promise is stronger than "slower". Nothing repeats, which
        /// on a time series means a flat line: every luminance jump is zero. And
        /// because the emphasis is gone, whatever it was carrying has to be
        /// carried by something that does not move — so the steady colour, the
        /// steady shape and the steady marker are checked in the same test, since
        /// removing the flash without them would be a regression dressed as a
        /// safety feature.
        /// </summary>
        [Test]
        public void PhotosensitivitySafeMode_HoldsSteadyAndStillReadsAsCritical()
        {
            AllOff();
            var pulse = new UiEmphasisPulse();
            Series normal = LuminanceSeries(pulse, UiStatusLevel.Critical, PulseFrames);

            AllOff();
            LightingAccessibility.PhotosensitivitySafeMode = true;
            Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = true;
            pulse.Reset();
            Series safe = LuminanceSeries(pulse, UiStatusLevel.Critical, PulseFrames);

            Assert.Greater(normal.MaxStep, 0f,
                "nothing changed luminance even with no setting on, so there is nothing to make safe.");
            Assert.AreEqual(0f, safe.MaxStep, 1e-6f,
                $"a luminance jump of {safe.MaxStep:0.0000} survived Safe Mode — the state is "
                + "still flashing, just less.");
            Assert.AreEqual(0f, safe.Range, 1e-6f,
                "the critical reading still swings between two brightnesses under Safe Mode.");
            Assert.AreEqual(0f, safe.Hz(PulseFrames * SampleSeconds), 0.0001f);

            Assert.IsFalse(NebulaUiDesignTokens.AllowsRepeatingEmphasis,
                "Safe Mode must stop repeating emphasis outright, not soften it.");
            Assert.AreEqual(0f, UiAnimationProfiles.MaxRepeatHz);
            Assert.IsFalse(UiEmphasisPulse.Repeats);

            // ...and every state is still legible without it. Not Critical alone:
            // if one state was legible because it pulsed, its siblings were too,
            // and finding them one red test at a time is the slow way.
            var report = new List<string>();
            foreach (UiStatusLevel level in
                     (UiStatusLevel[])System.Enum.GetValues(typeof(UiStatusLevel)))
            {
                float worst = WorstRatioAcrossSurfaces(NebulaUiDesignTokens.Status(level));
                report.Add($"{level,-10} {worst,6:0.00}:1");
                Assert.GreaterOrEqual(worst, 4.5f,
                    $"with the flash gone, {level} is only {worst:0.00}:1 against the surface "
                    + "it lands on worst. The steady colour has to carry the state on its own "
                    + "now, so this is the mode it has to be strongest in, not weakest.");

                UiStatusCue cue = UiStatusCue.For(level);
                Assert.AreNotEqual(UiCueShape.None, cue.Shape,
                    "Safe Mode removed the flash from " + level + " and left no shape behind it.");
                Assert.IsNotEmpty(cue.Marker, level + " has no word either.");
                Assert.Greater(UiCueShapes.Strokes(cue.Shape).Count, 0);
            }

            // The two readings that are not a level of their own, and that a player
            // most needs to tell apart when nothing is moving.
            foreach (UiStatusCue cue in new[]
                     {
                         UiStatusCue.ForCommunicationLink(live: false, hasLastKnown: false),
                         UiStatusCue.ForAvailability(available: false),
                     })
            {
                Assert.IsTrue(cue.HasMark, cue.Kind + " has no steady mark under Safe Mode.");
                float worst = WorstRatioAcrossSurfaces(
                    NebulaUiDesignTokens.StatusInk(cue.Level));
                Assert.GreaterOrEqual(worst, 4.5f,
                    $"{cue.Kind} reads at {worst:0.00}:1 with nothing moving.");
            }

            TestContext.Out.WriteLine("Photosensitivity Safe Mode, worst ratio over the six "
                                      + "surfaces\n  " + string.Join("\n  ", report));

            WriteSeriesStrip("state-a11y-photosensitivity-timeseries.png",
                new[] { normal.Alpha, safe.Alpha },
                new[] { LightingUiPalette.Critical, LightingUiPalette.Working });
        }

        /// <summary>
        /// The general form of the defect Safe Mode had: an accessibility setting
        /// that makes something harder to see.
        /// <para>
        /// Critical was found at 1.53:1 under Safe Mode — a third of the WCAG AA
        /// floor — because the flash limit was being applied to the resting colour
        /// of a status and not only to a flash. That was one state in one mode, and
        /// the same mistake could be made in any of them, so every mode is swept
        /// against every state here. A mode a player turns on to see better may
        /// never leave anything less legible than it was with everything off.
        /// </para>
        /// </summary>
        [Test]
        public void NoAccessibilityModeMakesAnyStatusLessLegibleThanWithEverythingOff()
        {
            AllOff();
            var baseline = new Dictionary<UiStatusLevel, float>();
            foreach (UiStatusLevel level in
                     (UiStatusLevel[])System.Enum.GetValues(typeof(UiStatusLevel)))
                baseline[level] = WorstRatioAcrossSurfaces(NebulaUiDesignTokens.Status(level));

            var modes = new Dictionary<string, System.Action>
            {
                ["Motion Off"] = () => AnimationAccessibility.MotionOff = true,
                ["Reduced Motion"] = () => AnimationAccessibility.ReducedMotion = true,
                ["Flash Reduction"] = () => LightingAccessibility.FlashReduction = true,
                ["Safe Mode"] = () => LightingAccessibility.PhotosensitivitySafeMode = true,
                ["High Contrast"] = () => LightingAccessibility.HighContrast = true,
                ["Visual Cue"] = () =>
                    Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = true,
            };

            var regressions = new List<string>();
            foreach (KeyValuePair<string, System.Action> mode in modes)
            {
                AllOff();
                mode.Value();
                foreach (UiStatusLevel level in
                         (UiStatusLevel[])System.Enum.GetValues(typeof(UiStatusLevel)))
                {
                    float now = WorstRatioAcrossSurfaces(NebulaUiDesignTokens.Status(level));
                    if (now < baseline[level] - 0.01f)
                        regressions.Add($"{mode.Key} drops {level} from "
                                        + $"{baseline[level]:0.00}:1 to {now:0.00}:1");
                }
            }
            AllOff();

            Assert.IsEmpty(regressions,
                "an accessibility setting made something harder to see:\n  "
                + string.Join("\n  ", regressions));
        }

        // =====================================================================
        // 6. high contrast — a second palette, and the numbers to prove it
        // =====================================================================

        /// <summary>
        /// The WCAG figures, computed here from the live tokens rather than read
        /// off a comment beside them. Both palettes are measured, so the claim is
        /// "this went up", not "this is fine".
        /// </summary>
        [Test]
        public void HighContrast_RaisesEveryMeasuredRatioAndClearsWcagAa()
        {
            var report = new List<string>();

            foreach (string ink in new[] { "InkPrimary", "InkSecondary", "InkMuted", "DisabledInk" })
            {
                AllOff();
                float normal = WorstRatioAcrossSurfaces(Ink(ink));
                LightingAccessibility.HighContrast = true;
                float contrast = WorstRatioAcrossSurfaces(Ink(ink));
                AllOff();

                report.Add($"{ink,-14} {normal,6:0.00}:1 -> {contrast,6:0.00}:1");
                Assert.Greater(contrast, normal,
                    $"High Contrast did not improve {ink}: {normal:0.00}:1 -> {contrast:0.00}:1");
                Assert.GreaterOrEqual(contrast, 4.5f,
                    $"{ink} is {contrast:0.00}:1 in High Contrast — below WCAG AA for text, "
                    + "and High Contrast is the mode a player turns on precisely because "
                    + "the normal palette was not enough.");
            }

            foreach (UiStatusLevel level in
                     (UiStatusLevel[])System.Enum.GetValues(typeof(UiStatusLevel)))
            {
                AllOff();
                float normal = WorstRatioAcrossSurfaces(NebulaUiDesignTokens.Status(level));
                LightingAccessibility.HighContrast = true;
                float contrast = WorstRatioAcrossSurfaces(NebulaUiDesignTokens.Status(level));
                AllOff();

                report.Add($"{level,-14} {normal,6:0.00}:1 -> {contrast,6:0.00}:1");
                Assert.Greater(contrast, normal, $"High Contrast did not improve {level}.");
                Assert.GreaterOrEqual(contrast, 4.5f,
                    $"{level} is {contrast:0.00}:1 in High Contrast, below WCAG AA.");
            }

            TestContext.Out.WriteLine("WCAG 2.1 worst-case ratio over the six panel surfaces\n  "
                                      + string.Join("\n  ", report));
        }

        /// <summary>
        /// The failure this replaces. High Contrast used to be one multiplier over
        /// every colour, which raises the whole screen and changes no ratio at all
        /// — two states that were hard to tell apart stayed exactly as hard.
        /// <para>
        /// "Not a tint" is testable and this is the test: a single multiplier
        /// cannot move the surfaces down while it moves the inks up. If somebody
        /// reintroduces a global scale, this fails.
        /// </para>
        /// </summary>
        [Test]
        public void HighContrast_IsASecondPaletteAndNotAGlobalTint()
        {
            AllOff();
            float voidNormal = Luminance(NebulaUiDesignTokens.SpaceVoid);
            float rowNormal = Luminance(NebulaUiDesignTokens.Surface3);
            float inkNormal = Luminance(NebulaUiDesignTokens.InkPrimary);
            float borderNormal = NebulaUiDesignTokens.BorderDefault.a;

            LightingAccessibility.HighContrast = true;
            float voidContrast = Luminance(NebulaUiDesignTokens.SpaceVoid);
            float rowContrast = Luminance(NebulaUiDesignTokens.Surface3);
            float inkContrast = Luminance(NebulaUiDesignTokens.InkPrimary);
            float borderContrast = NebulaUiDesignTokens.BorderDefault.a;

            Assert.Less(voidContrast, voidNormal,
                "the backdrop did not get darker; a palette that only brightens is a tint.");
            Assert.Less(rowContrast, rowNormal, "the row surface did not get darker.");
            Assert.Greater(inkContrast, inkNormal, "the primary ink did not get brighter.");
            Assert.Greater(borderContrast, borderNormal * 1.5f,
                "borders stayed as faint as they were; in High Contrast an edge is the thing "
                + "that says where a panel ends.");
        }

        /// <summary>
        /// A palette that separates text from background and loses the difference
        /// between selected and unselected has traded one accessibility problem
        /// for another. Every distinction the interface relies on is re-checked
        /// inside the mode.
        /// </summary>
        [Test]
        public void HighContrast_KeepsSelectedDisabledAndWarningTellingThemselvesApart()
        {
            AllOff();
            LightingAccessibility.HighContrast = true;

            // Selected / hovered / pressed keep their ordering and each is further
            // from the resting surface than it was.
            Assert.Greater(NebulaUiDesignTokens.SelectedFill.a, NebulaUiDesignTokens.HoverFill.a,
                "selected stopped outranking hovered.");
            Assert.Greater(NebulaUiDesignTokens.PressedFill.a, NebulaUiDesignTokens.SelectedFill.a,
                "pressed stopped outranking selected.");

            // Disabled is readable and still unmistakably disabled.
            float disabledOnRow = ContrastRatio(
                NebulaUiDesignTokens.DisabledInk, NebulaUiDesignTokens.Surface3);
            float disabledVsEnabled = ContrastRatio(
                NebulaUiDesignTokens.DisabledInk, NebulaUiDesignTokens.InkPrimary);
            Assert.GreaterOrEqual(disabledOnRow, 4.5f,
                $"a disabled control's reason text is {disabledOnRow:0.00}:1 and cannot be read.");
            Assert.GreaterOrEqual(disabledVsEnabled, 1.8f,
                $"disabled ink is only {disabledVsEnabled:0.00}:1 from enabled ink — making it "
                + "readable must not make it look available.");

            // The six states stay six states.
            var levels = (UiStatusLevel[])System.Enum.GetValues(typeof(UiStatusLevel));
            for (int i = 0; i < levels.Length; i++)
            for (int j = i + 1; j < levels.Length; j++)
            {
                Color a = NebulaUiDesignTokens.Status(levels[i]);
                Color b = NebulaUiDesignTokens.Status(levels[j]);
                float separation = Mathf.Max(Mathf.Abs(a.r - b.r),
                    Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
                Assert.Greater(separation, 0.10f,
                    $"{levels[i]} and {levels[j]} collapsed into the same colour in High Contrast "
                    + $"(largest channel difference {separation:0.###}).");
            }

            // Warning must not be readable as Critical, in either direction.
            float warningVsCritical = ContrastRatio(
                NebulaUiDesignTokens.Status(UiStatusLevel.Warning),
                NebulaUiDesignTokens.Status(UiStatusLevel.Critical));
            Assert.GreaterOrEqual(warningVsCritical, 1.25f,
                $"Warning and Critical are {warningVsCritical:0.00}:1 apart in High Contrast.");
        }

        /// <summary>
        /// The pixel half of the same claim. Not a hash comparison: a hash says
        /// "something changed", and one changed pixel would satisfy it. High
        /// Contrast is a whole-palette change and must show up as one.
        /// </summary>
        [Test]
        public void HighContrast_ChangesTheFrameAPlayerActuallySees()
        {
            ImageDelta delta = Compare(Baseline, "state-a11y-high-contrast");

            Assert.Greater(delta.ChangedFraction, 0.05f,
                $"only {delta.ChangedFraction * 100f:0.00}% of the frame changed under High "
                + "Contrast. The setting reached a handful of pixels, not the palette.");
            Assert.Greater(delta.MeanAbsolute, 1f,
                $"the average pixel moved {delta.MeanAbsolute:0.00} levels — invisible.");
        }

        // =====================================================================
        // 7. visual cue — shape and text, not only colour
        // =====================================================================

        /// <summary>
        /// The five states the requirement names, each with its own silhouette.
        /// Shapes are compared by their stroke tables rather than by their enum
        /// names, so two cues pointed at the same geometry are caught.
        /// </summary>
        [Test]
        public void VisualCue_GivesEveryCriticalStateItsOwnShapeAndMarker()
        {
            AllOff();
            Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = true;

            var required = new Dictionary<string, UiStatusCue>
            {
                ["warning"] = UiStatusCue.For(UiStatusLevel.Warning),
                ["critical"] = UiStatusCue.For(UiStatusLevel.Critical),
                ["success / nominal"] = UiStatusCue.For(UiStatusLevel.Nominal),
                ["communication loss"] =
                    UiStatusCue.ForCommunicationLink(live: false, hasLastKnown: false),
                ["disabled / blocked"] = UiStatusCue.ForAvailability(available: false),
            };

            var shapes = new Dictionary<string, string>();
            var markers = new Dictionary<string, string>();

            foreach (KeyValuePair<string, UiStatusCue> pair in required)
            {
                UiStatusCue cue = pair.Value;
                Assert.IsTrue(cue.HasMark, pair.Key + " has no shape cue, only a colour.");
                Assert.IsNotEmpty(cue.Marker, pair.Key + " has no text marker.");

                string signature = UiCueShapes.Signature(cue.Shape);
                Assert.IsNotEmpty(signature, pair.Key + "'s shape draws no strokes.");
                Assert.IsFalse(shapes.ContainsKey(signature),
                    $"{pair.Key} draws exactly the same shape as {Value(shapes, signature)} — "
                    + "two states that share a silhouette are one state with two colours.");
                shapes[signature] = pair.Key;

                Assert.IsFalse(markers.ContainsKey(cue.Marker),
                    $"{pair.Key} and {Value(markers, cue.Marker)} share the marker "
                    + $"\"{cue.Marker}\".");
                markers[cue.Marker] = pair.Key;
            }

            // Communication loss and a blocked control both sit on Inactive-ish
            // colour, so the shape is the only thing telling them apart. That is
            // the case the whole cue vocabulary exists for.
            Assert.AreNotEqual(
                UiCueShapes.Signature(required["communication loss"].Shape),
                UiCueShapes.Signature(required["disabled / blocked"].Shape));
        }

        /// <summary>
        /// The switch has to reach something. Off means no marks at all and an
        /// unchanged colour — the setting removes a redundancy, never a fact.
        /// </summary>
        [Test]
        public void VisualCue_SwitchTurnsTheMarksOnAndOffAndTouchesNothingElse()
        {
            AllOff();
            Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = false;
            Assert.IsFalse(UiStatusCue.Enabled);
            Color offColor = NebulaUiDesignTokens.Status(UiStatusLevel.Warning);

            foreach (UiStatusLevel level in
                     (UiStatusLevel[])System.Enum.GetValues(typeof(UiStatusLevel)))
                Assert.IsFalse(UiStatusCue.For(level).HasMark,
                    level + " still drew a mark with the cue switched off.");
            Assert.IsFalse(UiStatusCue.ForCommunicationLink(false, false).HasMark);
            Assert.IsFalse(UiStatusCue.ForAvailability(false).HasMark);

            Nebula.Combat.CombatAccessibilitySettings.AudioVisualCueForWarnings = true;
            Assert.IsTrue(UiStatusCue.Enabled);
            foreach (UiStatusLevel level in
                     (UiStatusLevel[])System.Enum.GetValues(typeof(UiStatusLevel)))
                Assert.IsTrue(UiStatusCue.For(level).HasMark,
                    level + " has no mark even with the cue switched on.");

            Assert.AreEqual(offColor, NebulaUiDesignTokens.Status(UiStatusLevel.Warning),
                "the visual cue moved a status colour. Colour is the channel the cue is "
                + "redundant with; changing it means a player who turns cues off sees "
                + "different information, not less of it.");
        }

        /// <summary>
        /// The cue's own capture. Smaller than High Contrast's by design — five
        /// marks on one panel rather than a repainted screen — so it is held to a
        /// pixel count rather than a fraction of the frame.
        /// </summary>
        [Test]
        public void VisualCue_ChangesTheFrameAPlayerActuallySees()
        {
            ImageDelta delta = Compare(Baseline, "state-a11y-visual-cue");

            Assert.Greater(delta.StronglyChangedPixels, 200,
                $"only {delta.StronglyChangedPixels} pixels changed when the visual cue was "
                + "switched on. The setting is still reaching nothing that draws — which is "
                + "the state this whole file was written to stop being invisible.");
        }

        // =====================================================================
        // 8. the policy the old fixture got wrong
        // =====================================================================

        /// <summary>
        /// Stated as a test so it cannot quietly drift back. Six modes sharing one
        /// hash is not, on its own, a defect: four of them govern motion and are
        /// entitled to leave a still frame exactly as they found it. Only the two
        /// that promise a visible difference are compared against pixels, and they
        /// are compared above.
        /// <para>
        /// What is still a defect, and is checked here, is a motion mode's capture
        /// being missing, blank, or the wrong size — because those mean the frame
        /// was never taken, and "identical to the baseline" is only evidence when
        /// something actually rendered.
        /// </para>
        /// </summary>
        [Test]
        public void MotionGovernedModesMayShareAFrameWithTheBaseline()
        {
            string baselineHash = Fingerprint(Path.Combine(Dir, Baseline + ".png"));

            foreach (string mode in MotionGovernedModes)
            {
                AssertNotBlank(Dir, mode + ".png");
                AssertSize(Dir, mode + ".png", StateWidth, StateHeight);
            }

            // The assertion is that nothing above fails when they match, which is
            // what this whole test executing to the end demonstrates. The hashes
            // are reported rather than judged.
            foreach (string mode in MotionGovernedModes)
                TestContext.Out.WriteLine($"{mode,-38} "
                    + (Fingerprint(Path.Combine(Dir, mode + ".png")) == baselineHash
                        ? "matches the baseline (permitted: this mode governs motion)"
                        : "differs from the baseline"));

            foreach (string mode in StaticallyVisibleModes)
                Assert.AreNotEqual(baselineHash, Fingerprint(Path.Combine(Dir, mode + ".png")),
                    mode + " is byte-identical to the baseline. This one is not permitted to "
                    + "be: it promises a difference a player can see standing still.");
        }

        // =====================================================================
        // 9. proof that all of the above can fail
        // =====================================================================

        /// <summary>
        /// Green is not the same as covered. Seventeen flat-grey PNGs once passed
        /// a whole screenshot suite, and six identical accessibility captures
        /// passed the fixture written to catch exactly that — so every helper here
        /// is run against a set built to break it, and the ones that measure
        /// behaviour are run against a deliberately broken configuration.
        /// </summary>
        [Test]
        public void TheAuditCanFail()
        {
            string scratch = Path.Combine(Path.GetTempPath(),
                "nebula-ui-audit-selftest-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                WritePng(Path.Combine(scratch, "real.png"), StateWidth, StateHeight, noise: true);
                WritePng(Path.Combine(scratch, "flat.png"), StateWidth, StateHeight, noise: false);
                WritePng(Path.Combine(scratch, "wrong-size.png"), 640, 360, noise: true);
                File.Copy(Path.Combine(scratch, "real.png"), Path.Combine(scratch, "twin.png"));
                WritePng(Path.Combine(scratch, "parent-menu-1280x720.png"), 800, 600, noise: true);

                Assert.Throws<AssertionException>(
                    () => AssertNotBlank(scratch, "absent.png"),
                    "a missing capture passed the presence check");

                Assert.Throws<AssertionException>(
                    () => AssertNotBlank(scratch, "flat.png"),
                    "a flat rectangle passed the blank check — this is the exact failure "
                    + "seventeen shipped PNGs got past once already");

                Assert.Throws<AssertionException>(
                    () => AssertSize(scratch, "wrong-size.png", StateWidth, StateHeight),
                    "a capture of the wrong size passed the size check");

                Assert.Throws<AssertionException>(
                    () => AssertDeclaredSize(scratch, "parent-menu-1280x720.png"),
                    "a capture that lied about its size in its own name passed");

                Assert.Throws<AssertionException>(
                    () => AssertAllDistinct(scratch,
                        new List<string> { "real.png", "twin.png" }, "self-test"),
                    "two byte-identical captures passed the distinctness check");

                Assert.DoesNotThrow(() => AssertNotBlank(scratch, "real.png"));
                Assert.DoesNotThrow(() => AssertSize(scratch, "real.png", StateWidth, StateHeight));
                Assert.DoesNotThrow(() => AssertAllDistinct(scratch,
                    new List<string> { "real.png", "wrong-size.png" }, "self-test"));

                // Two identical files must read as no difference at all, or the
                // High Contrast and Visual Cue pixel checks are decoration.
                ImageDelta same = Compare(scratch, "real.png", "twin.png");
                Assert.AreEqual(0, same.StronglyChangedPixels,
                    "the image comparison found a difference between two identical files");
                Assert.AreEqual(0f, same.ChangedFraction, 1e-6f);

                ImageDelta different = Compare(scratch, "real.png", "flat.png");
                Assert.Greater(different.StronglyChangedPixels, 0,
                    "the image comparison found no difference between two obviously "
                    + "different files");
            }
            finally
            {
                if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            }
        }

        /// <summary>
        /// The behavioural half of the same proof: the measurements really do
        /// return different answers for different settings, so a mode that stopped
        /// working could not pass by returning a constant.
        /// </summary>
        [Test]
        public void TheMeasurementsCanTellTheModesApart()
        {
            AllOff();
            Series normalMotion = PanelOpening(MotionFrames);
            AnimationAccessibility.ReducedMotion = true;
            Series reducedMotion = PanelOpening(MotionFrames);
            AllOff();
            AnimationAccessibility.MotionOff = true;
            Series noMotion = PanelOpening(MotionFrames);
            AllOff();

            Assert.Greater(normalMotion.PeakOffset, reducedMotion.PeakOffset);
            Assert.Greater(reducedMotion.PeakOffset, noMotion.PeakOffset);
            Assert.AreEqual(0f, noMotion.PeakOffset, 0.0001f);

            var pulse = new UiEmphasisPulse();
            float duration = PulseFrames * SampleSeconds;

            AllOff();
            float normalHz = PulseSeries(pulse, PulseFrames).Hz(duration);
            AllOff();
            LightingAccessibility.FlashReduction = true;
            pulse.Reset();
            float reducedHz = PulseSeries(pulse, PulseFrames).Hz(duration);
            AllOff();
            LightingAccessibility.PhotosensitivitySafeMode = true;
            pulse.Reset();
            float safeHz = PulseSeries(pulse, PulseFrames).Hz(duration);
            AllOff();

            Assert.Greater(normalHz, reducedHz, "the rate measurement returns a constant");
            Assert.Greater(reducedHz, safeHz);
            Assert.AreEqual(0f, safeHz, 0.0001f);

            // And the contrast measurement is not a constant either.
            AllOff();
            float normalRatio = WorstRatioAcrossSurfaces(NebulaUiDesignTokens.InkMuted);
            LightingAccessibility.HighContrast = true;
            float contrastRatio = WorstRatioAcrossSurfaces(NebulaUiDesignTokens.InkMuted);
            AllOff();
            Assert.Greater(contrastRatio, normalRatio + 1f,
                "the WCAG measurement does not respond to the setting it is measuring");
        }

        // =====================================================================
        // time series
        // =====================================================================

        /// <summary>
        /// One recorded run: a value per sampled frame, plus the derived numbers
        /// every accessibility claim above is actually made of.
        /// </summary>
        private sealed class Series
        {
            public readonly List<float> Offset = new List<float>();
            public readonly List<float> Alpha = new List<float>();

            /// <summary>The furthest anything got from where it ends up.</summary>
            public float PeakOffset
            {
                get
                {
                    float peak = 0f;
                    foreach (float value in Offset) peak = Mathf.Max(peak, Mathf.Abs(value));
                    return peak;
                }
            }

            /// <summary>Total distance travelled, which a big slow move and a small fast one differ in.</summary>
            public float PathLength
            {
                get
                {
                    float total = 0f;
                    for (int i = 1; i < Offset.Count; i++)
                        total += Mathf.Abs(Offset[i] - Offset[i - 1]);
                    return total;
                }
            }

            /// <summary>The biggest change between two consecutive frames.</summary>
            public float MaxStep => BiggestStep(Offset);
            public float MaxAlphaStep => BiggestStep(Alpha);

            /// <summary>Peak-to-trough of the recorded value.</summary>
            public float Range
            {
                get
                {
                    if (Alpha.Count == 0) return 0f;
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (float value in Alpha)
                    {
                        lo = Mathf.Min(lo, value);
                        hi = Mathf.Max(hi, value);
                    }
                    return hi - lo;
                }
            }

            /// <summary>Total change over the run, per second — what an eye integrates.</summary>
            public float ChangePerSecond(float seconds)
            {
                if (seconds <= 0f) return 0f;
                float total = 0f;
                for (int i = 1; i < Alpha.Count; i++) total += Mathf.Abs(Alpha[i] - Alpha[i - 1]);
                return total / seconds;
            }

            /// <summary>
            /// Repeat rate, counted as upward crossings of the run's own midpoint.
            /// A flat line crosses nothing and reports zero, which is the answer
            /// Safe Mode is supposed to produce.
            /// </summary>
            public float Hz(float seconds)
            {
                if (Alpha.Count < 3 || seconds <= 0f) return 0f;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (float value in Alpha)
                {
                    lo = Mathf.Min(lo, value);
                    hi = Mathf.Max(hi, value);
                }
                if (hi - lo < 1e-5f) return 0f;

                float mid = (hi + lo) * 0.5f;
                int crossings = 0;
                for (int i = 1; i < Alpha.Count; i++)
                    if (Alpha[i - 1] <= mid && Alpha[i] > mid) crossings++;
                return crossings / seconds;
            }

            private static float BiggestStep(List<float> values)
            {
                float biggest = 0f;
                for (int i = 1; i < values.Count; i++)
                    biggest = Mathf.Max(biggest, Mathf.Abs(values[i] - values[i - 1]));
                return biggest;
            }
        }

        /// <summary>
        /// Opens a real <see cref="UiPanelAnimator"/> and records where it is on
        /// every frame. Offsets are relative to the authored pose, so zero means
        /// "arrived" whatever the panel's own layout is.
        /// </summary>
        private static Series PanelOpening(int frames)
        {
            var go = new GameObject("Motion Probe", typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = new Vector2(120f, 240f);
            go.GetComponent<CanvasGroup>().alpha = 1f;

            var animator = go.AddComponent<UiPanelAnimator>();
            bool open = false;
            // Null director: this test drives the ticks itself, so nothing is
            // registered and no other fixture's clock can reach it.
            animator.Initialize(null, () => open);
            open = true;

            var series = new Series();
            var group = go.GetComponent<CanvasGroup>();
            try
            {
                for (int i = 0; i < frames; i++)
                {
                    animator.VisualTick(SampleSeconds);
                    series.Offset.Add(rect.anchoredPosition.y - animator.BasePosition.y);
                    series.Alpha.Add(group.alpha);
                }
            }
            finally { Object.DestroyImmediate(go); }

            return series;
        }

        /// <summary>The emphasis pulse's own level, frame by frame.</summary>
        private static Series PulseSeries(UiEmphasisPulse pulse, int frames)
        {
            pulse.Reset();
            var series = new Series();
            for (int i = 0; i < frames; i++)
            {
                float level = pulse.Tick(SampleSeconds);
                series.Alpha.Add(level);
                series.Offset.Add(level - 1f);
            }
            return series;
        }

        /// <summary>
        /// The pulse applied to a real status colour, recorded as WCAG relative
        /// luminance — which is the quantity a photosensitivity limit is actually
        /// about, rather than the multiplier that produced it.
        /// </summary>
        private static Series LuminanceSeries(UiEmphasisPulse pulse, UiStatusLevel level, int frames)
        {
            pulse.Reset();
            Color color = NebulaUiDesignTokens.Status(level);
            var series = new Series();
            for (int i = 0; i < frames; i++)
            {
                pulse.Tick(SampleSeconds);
                float luminance = Luminance(pulse.Apply(color));
                series.Alpha.Add(luminance);
                series.Offset.Add(luminance);
            }
            return series;
        }

        /// <summary>
        /// Plots the recorded runs into a PNG beside the captures, so the claim
        /// "this one moves less" has a picture as well as a number. Written on the
        /// CPU, so it needs no graphics device.
        /// </summary>
        private static void WriteSeriesStrip(string fileName, IReadOnlyList<List<float>> runs,
            IReadOnlyList<Color> colors)
        {
            const int width = 960, height = 240;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color32[width * height];

            Color32 background = NebulaUiDesignTokens.SurfaceObsidian;
            Color32 grid = NebulaUiDesignTokens.Divider;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = background;
            for (int y = 0; y < height; y += 40)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = grid;

            float lo = float.MaxValue, hi = float.MinValue;
            foreach (List<float> run in runs)
            foreach (float value in run)
            {
                lo = Mathf.Min(lo, value);
                hi = Mathf.Max(hi, value);
            }
            if (hi - lo < 1e-5f) { hi = lo + 1f; }

            for (int r = 0; r < runs.Count; r++)
            {
                List<float> run = runs[r];
                Color32 ink = colors[r % colors.Count];
                for (int x = 0; x < width; x++)
                {
                    int index = Mathf.Clamp(x * run.Count / width, 0, run.Count - 1);
                    float normalized = (run[index] - lo) / (hi - lo);
                    int y = Mathf.Clamp(Mathf.RoundToInt(normalized * (height - 9)) + 4, 0, height - 1);
                    for (int t = -1; t <= 1; t++)
                    {
                        int yy = Mathf.Clamp(y + t, 0, height - 1);
                        pixels[yy * width + x] = ink;
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, fileName), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        // =====================================================================
        // colour maths
        // =====================================================================

        private static Color Ink(string name)
        {
            switch (name)
            {
                case "InkPrimary": return NebulaUiDesignTokens.InkPrimary;
                case "InkSecondary": return NebulaUiDesignTokens.InkSecondary;
                case "InkMuted": return NebulaUiDesignTokens.InkMuted;
                case "DisabledInk": return NebulaUiDesignTokens.DisabledInk;
                default: throw new System.ArgumentException(name);
            }
        }

        /// <summary>The worst ratio an ink gets across every surface it can land on.</summary>
        private static float WorstRatioAcrossSurfaces(Color ink)
        {
            Color[] surfaces =
            {
                NebulaUiDesignTokens.SpaceVoid, NebulaUiDesignTokens.SurfaceObsidian,
                NebulaUiDesignTokens.Surface1, NebulaUiDesignTokens.Surface2,
                NebulaUiDesignTokens.Surface3, NebulaUiDesignTokens.SurfaceElevated,
            };
            float worst = float.MaxValue;
            foreach (Color surface in surfaces)
                worst = Mathf.Min(worst, ContrastRatio(ink, surface));
            return worst;
        }

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

        // =====================================================================
        // image comparison
        // =====================================================================

        /// <summary>How far apart two captures really are, rather than whether they differ at all.</summary>
        private struct ImageDelta
        {
            /// <summary>Mean absolute channel difference, in 0..255 levels.</summary>
            public float MeanAbsolute;

            /// <summary>Fraction of pixels differing by more than 8 levels in any channel.</summary>
            public float ChangedFraction;

            /// <summary>Count of pixels differing by more than 24 levels in any channel.</summary>
            public int StronglyChangedPixels;
        }

        private static ImageDelta Compare(string a, string b) =>
            Compare(Dir, a + ".png", b + ".png");

        private static ImageDelta Compare(string directory, string fileA, string fileB)
        {
            Texture2D first = Load(directory, fileA);
            Texture2D second = Load(directory, fileB);
            try
            {
                Assert.AreEqual(first.width, second.width, fileA + " and " + fileB + " differ in width");
                Assert.AreEqual(first.height, second.height, fileA + " and " + fileB + " differ in height");

                Color32[] left = first.GetPixels32();
                Color32[] right = second.GetPixels32();

                long total = 0;
                int changed = 0, strong = 0;
                for (int i = 0; i < left.Length; i++)
                {
                    int dr = Mathf.Abs(left[i].r - right[i].r);
                    int dg = Mathf.Abs(left[i].g - right[i].g);
                    int db = Mathf.Abs(left[i].b - right[i].b);
                    total += dr + dg + db;
                    int worst = Mathf.Max(dr, Mathf.Max(dg, db));
                    if (worst > 8) changed++;
                    if (worst > 24) strong++;
                }

                return new ImageDelta
                {
                    MeanAbsolute = total / (float)(left.Length * 3),
                    ChangedFraction = changed / (float)left.Length,
                    StronglyChangedPixels = strong,
                };
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        // =====================================================================
        // file helpers
        // =====================================================================

        private static string Value(Dictionary<string, string> map, string key) =>
            map.TryGetValue(key, out string found) ? found : "another cue";

        private static void AssertPresent(IEnumerable<string> names, string what)
        {
            var missing = new List<string>();
            foreach (string name in names)
                if (!File.Exists(Path.Combine(Dir, name + ".png"))) missing.Add(name + ".png");

            Assert.IsEmpty(missing,
                $"{what} captures were never written. Run the PlayMode fixture "
                + "UiFinalInteractionStateCaptureTests with graphics enabled. Missing: "
                + string.Join(", ", missing));
        }

        private static Texture2D Load(string directory, string file)
        {
            string path = Path.Combine(directory, file);
            Assert.IsTrue(File.Exists(path), file + " is missing from " + directory);
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(path)), file + " is not a readable PNG");
            return texture;
        }

        /// <summary>
        /// A real interface spans a wide range of luminance and does not spend
        /// nearly all of itself on one value. Both are measured, because a capture
        /// that failed can still contain two colours.
        /// </summary>
        private static void AssertNotBlank(string directory, string file)
        {
            Texture2D texture = Load(directory, file);
            try
            {
                Color32[] pixels = texture.GetPixels32();
                var histogram = new int[256];
                int min = 255, max = 0;
                foreach (Color32 p in pixels)
                {
                    int luma = (p.r * 30 + p.g * 59 + p.b * 11) / 100;
                    histogram[luma]++;
                    if (luma < min) min = luma;
                    if (luma > max) max = luma;
                }

                Assert.Greater(max - min, 24,
                    $"{file} spans only {max - min} levels of luminance — nothing was drawn into it");

                int busiest = 0;
                foreach (int count in histogram) if (count > busiest) busiest = count;
                Assert.Less(busiest / (float)pixels.Length, 0.995f,
                    $"{file} is {busiest * 100f / pixels.Length:0.0}% a single value");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static void AssertSize(string directory, string file, int width, int height)
        {
            Texture2D texture = Load(directory, file);
            try
            {
                Assert.AreEqual(width, texture.width, file + " width");
                Assert.AreEqual(height, texture.height, file + " height");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        /// <summary>Reads the size out of the file's own name and holds it to it.</summary>
        private static void AssertDeclaredSize(string directory, string file)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            string[] parts = stem.Split('-');
            // The first WxH token, not the last. Names carry an optional aspect-ratio
            // suffix after the resolution -- parent-menu-1920x1200-16x10 -- and taking
            // the last match read "16x10" as the size, then failed a correctly sized
            // 1920x1200 capture for being 1920 wide instead of 16. The resolution
            // always precedes the suffix, so the first match is the declared size.
            string declared = null;
            foreach (string part in parts)
                if (declared == null && part.Contains("x") && part.Length > 3 && char.IsDigit(part[0]))
                    declared = part;

            Assert.IsNotNull(declared, file + " does not declare a resolution in its name");
            string[] size = declared.Split('x');
            AssertSize(directory, file, int.Parse(size[0]), int.Parse(size[1]));
        }

        private static void AssertAllDistinct(string directory, IEnumerable<string> files, string what)
        {
            var seen = new Dictionary<string, string>();
            foreach (string file in files)
            {
                string path = Path.Combine(directory, file);
                Assert.IsTrue(File.Exists(path), file + " is missing from " + directory);
                string hash = Fingerprint(path);
                Assert.IsFalse(seen.ContainsKey(hash),
                    $"{file} is byte-identical to " +
                    $"{(seen.TryGetValue(hash, out string other) ? other : "another capture")} — " +
                    $"one of the two {what} states never actually happened");
                seen[hash] = file;
            }
        }

        private static string Fingerprint(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return System.BitConverter.ToString(sha.ComputeHash(stream));
        }

        /// <summary>
        /// A gradient plus a diagonal, which spans enough luminance to read as
        /// drawn; or one flat value, which is what a capture with no graphics
        /// device produces.
        /// </summary>
        private static void WritePng(string path, int width, int height, bool noise)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte v = noise ? (byte)((x * 255 / width + (x == y ? 128 : 0)) & 0xFF) : (byte)32;
                pixels[y * width + x] = new Color32(v, v, v, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
