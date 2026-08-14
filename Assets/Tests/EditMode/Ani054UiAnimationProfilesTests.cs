using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-05.4: the shared UI timings, the accessibility rules on them, and
    /// the input-safety contract a closing panel must honour.
    /// </summary>
    public class Ani054UiAnimationProfilesTests
    {
        [TearDown]
        public void Cleanup()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
        }

        // --- timings ---------------------------------------------------------

        [Test]
        public void EveryDuration_SitsInTheBriefsRange()
        {
            Assert.GreaterOrEqual(UiAnimationProfiles.StateTransitionSeconds, 0.10f);
            Assert.LessOrEqual(UiAnimationProfiles.StateTransitionSeconds, 0.16f);

            Assert.GreaterOrEqual(UiAnimationProfiles.PanelEnterSeconds, 0.16f);
            Assert.LessOrEqual(UiAnimationProfiles.PanelEnterSeconds, 0.22f);

            Assert.GreaterOrEqual(UiAnimationProfiles.PanelExitSeconds, 0.12f);
            Assert.LessOrEqual(UiAnimationProfiles.PanelExitSeconds, 0.18f);

            Assert.AreEqual(0.18f, UiAnimationProfiles.NotificationEnterSeconds, 1e-4f);
            Assert.AreEqual(0.18f, UiAnimationProfiles.NotificationExitSeconds, 1e-4f);
        }

        [Test]
        public void APanelClosesQuickerThanItOpens()
            => Assert.Less(UiAnimationProfiles.PanelExitSeconds, UiAnimationProfiles.PanelEnterSeconds);

        [Test]
        public void OnlyTheTwoTransitionalPhases_HaveADuration()
        {
            Assert.Greater(UiAnimationProfiles.DurationFor(UiPanelPhase.Entering), 0f);
            Assert.Greater(UiAnimationProfiles.DurationFor(UiPanelPhase.Exiting), 0f);
            Assert.AreEqual(0f, UiAnimationProfiles.DurationFor(UiPanelPhase.Shown));
            Assert.AreEqual(0f, UiAnimationProfiles.DurationFor(UiPanelPhase.Hidden));
        }

        // --- easing ------------------------------------------------------------

        [Test]
        public void TheCurveNeverOvershoots()
        {
            for (float t = 0f; t <= 1f; t += 0.02f)
            {
                float eased = UiAnimationProfiles.Ease(t);
                Assert.GreaterOrEqual(eased, 0f, $"t={t}");
                Assert.LessOrEqual(eased, 1f, $"t={t}, a panel must not bounce past its mark");
            }
            Assert.AreEqual(0f, UiAnimationProfiles.Ease(0f), 1e-4f);
            Assert.AreEqual(1f, UiAnimationProfiles.Ease(1f), 1e-4f);
        }

        [Test]
        public void TheCurveIsMonotonic()
        {
            float previous = -1f;
            for (float t = 0f; t <= 1f; t += 0.02f)
            {
                float eased = UiAnimationProfiles.Ease(t);
                Assert.GreaterOrEqual(eased, previous - 1e-5f, $"t={t}");
                previous = eased;
            }
        }

        // --- accessibility --------------------------------------------------------

        [Test]
        public void MotionOff_MakesEveryPanelLandImmediately()
        {
            AnimationAccessibility.MotionOff = true;

            foreach (UiPanelPhase phase in Enum.GetValues(typeof(UiPanelPhase)).Cast<UiPanelPhase>())
                Assert.AreEqual(0f, UiAnimationProfiles.DurationFor(phase), $"{phase}");

            Assert.AreEqual(0f, UiAnimationProfiles.SlideDistance(), "and it does not travel either");
        }

        [Test]
        public void ReducedMotion_ShortensTheJourneyWithoutRemovingIt()
        {
            float full = UiAnimationProfiles.SlideDistance();
            AnimationAccessibility.ReducedMotion = true;
            float reduced = UiAnimationProfiles.SlideDistance();

            Assert.Less(reduced, full, "reduced motion must actually reduce");
            Assert.Greater(reduced, 0f, "but the change must still be visible");
            Assert.AreEqual(full * UiAnimationProfiles.ReducedMotionDistanceScale, reduced, 1e-4f);
        }

        [Test]
        public void ReducedMotion_DoesNotShortenTheDurations()
        {
            float enter = UiAnimationProfiles.DurationFor(UiPanelPhase.Entering);
            AnimationAccessibility.ReducedMotion = true;
            Assert.AreEqual(enter, UiAnimationProfiles.DurationFor(UiPanelPhase.Entering),
                "a state change must stay just as readable");
        }

        [Test]
        public void SafeMode_ForbidsAnyRepeatingEmphasis()
        {
            Assert.IsTrue(UiAnimationProfiles.AllowsRepeatingEmphasis, "normally a slow emphasis is fine");

            LightingAccessibility.FlashReduction = true;
            float reduced = UiAnimationProfiles.MaxRepeatHz;
            Assert.Less(reduced, 2f);
            Assert.Greater(reduced, 0f);

            LightingAccessibility.PhotosensitivitySafeMode = true;
            Assert.AreEqual(0f, UiAnimationProfiles.MaxRepeatHz);
            Assert.IsFalse(UiAnimationProfiles.AllowsRepeatingEmphasis,
                "a critical state under Safe Mode is a steady colour, never a blink");
        }

        [Test]
        public void NothingIsAllowedToStrobe()
            => Assert.LessOrEqual(UiAnimationProfiles.MaxRepeatHz, 3f,
                "anything faster than a few Hz is a strobe");

        // --- bar fill --------------------------------------------------------------

        [Test]
        public void ABarChasesItsRealValue_AndReachesIt()
        {
            float shown = 0f;
            for (int i = 0; i < 200; i++) shown = UiAnimationProfiles.ChaseFill(shown, 1f, 0.016f);
            Assert.AreEqual(1f, shown, 1e-3f, "the fill must arrive, not stall short");
        }

        [Test]
        public void MotionOff_ShowsTheRealValueImmediately()
        {
            AnimationAccessibility.MotionOff = true;
            Assert.AreEqual(0.42f, UiAnimationProfiles.ChaseFill(0f, 0.42f, 0.016f), 1e-5f,
                "with motion off the number is never hidden behind an animation");
        }

        [Test]
        public void AZeroDeltaFrame_ShowsTheRealValue()
            => Assert.AreEqual(0.7f, UiAnimationProfiles.ChaseFill(0.1f, 0.7f, 0f), 1e-5f);

        // --- input safety -------------------------------------------------------------

        [Test]
        public void AClosingPanel_StopsAcceptingClicksImmediately()
        {
            Assert.IsTrue(UiAnimationProfiles.BlocksInput(UiPanelPhase.Exiting),
                "a button on a closing panel must not be pressable");
            Assert.IsTrue(UiAnimationProfiles.BlocksInput(UiPanelPhase.Hidden));
            Assert.IsFalse(UiAnimationProfiles.BlocksInput(UiPanelPhase.Shown));
        }

        [Test]
        public void AnOpeningPanel_IsAlreadyUsable()
        {
            Assert.IsTrue(UiAnimationProfiles.IsInteractive(UiPanelPhase.Entering),
                "the animation must not delay the player");
            Assert.IsTrue(UiAnimationProfiles.IsInteractive(UiPanelPhase.Shown));
            Assert.IsFalse(UiAnimationProfiles.IsInteractive(UiPanelPhase.Exiting));
        }

        [Test]
        public void InteractiveAndBlocked_AreExactOpposites()
        {
            foreach (UiPanelPhase phase in Enum.GetValues(typeof(UiPanelPhase)).Cast<UiPanelPhase>())
                Assert.AreNotEqual(UiAnimationProfiles.IsInteractive(phase),
                    UiAnimationProfiles.BlocksInput(phase), $"{phase}");
        }

        // --- versions --------------------------------------------------------------------

        [Test]
        public void TheSaveAndContentVersions_AreUntouched()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion); // P2-SAVE-201: absent schema is unknown, never v13
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
