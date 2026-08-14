using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-05.4.2: the notification's enter/hold/exit contract, and the silent
    /// adoption every call site depends on.
    /// </summary>
    public class Ani0542WidgetCallSiteTests
    {
        [TearDown]
        public void Cleanup()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
        }

        [Test]
        public void ANotificationEntersHoldsAndExits_Once()
        {
            var notify = new UiNotificationAnimator();
            notify.AdoptSilently(false);

            for (int i = 0; i < 40; i++) notify.Tick(true, 0.016f);
            Assert.AreEqual(UiNotificationPhase.Holding, notify.Phase);
            Assert.AreEqual(1f, notify.Alpha, 1e-3f);
            Assert.AreEqual(1, notify.EntriesPlayed);

            for (int i = 0; i < 40; i++) notify.Tick(false, 0.016f);
            Assert.AreEqual(UiNotificationPhase.Idle, notify.Phase);
            Assert.AreEqual(0f, notify.Alpha, 1e-3f);
            Assert.AreEqual(1, notify.EntriesPlayed, "and it does not re-enter on the way out");
        }

        [Test]
        public void HoldingDoesNotReplayTheEntrance()
        {
            var notify = new UiNotificationAnimator();
            notify.AdoptSilently(false);

            for (int i = 0; i < 300; i++) notify.Tick(true, 0.016f);
            Assert.AreEqual(1, notify.EntriesPlayed,
                "however long the panel holds it, it entered once");
        }

        [Test]
        public void AMessageAlreadyOnScreen_IsAdoptedWithoutAnEntrance()
        {
            var notify = new UiNotificationAnimator();
            notify.AdoptSilently(true);

            Assert.AreEqual(1f, notify.Alpha, "a loaded save shows what was already there");
            Assert.AreEqual(0, notify.EntriesPlayed, "and never replays its arrival");
            Assert.AreEqual(UiNotificationPhase.Holding, notify.Phase);
        }

        [Test]
        public void MotionOff_ShowsAndHidesInOneFrame()
        {
            AnimationAccessibility.MotionOff = true;
            var notify = new UiNotificationAnimator();
            notify.AdoptSilently(false);

            Assert.AreEqual(1f, notify.Tick(true, 0.001f), 1e-4f);
            Assert.AreEqual(0f, notify.Tick(false, 0.001f), 1e-4f);
        }

        [Test]
        public void TheAlphaNeverLeavesItsRange()
        {
            var notify = new UiNotificationAnimator();
            notify.AdoptSilently(false);

            for (int i = 0; i < 200; i++)
            {
                float a = notify.Tick(i % 20 < 10, 0.016f);
                Assert.GreaterOrEqual(a, 0f);
                Assert.LessOrEqual(a, 1f, "no overshoot and no flash");
            }
        }

        [Test]
        public void ResetClearsTheCounter()
        {
            var notify = new UiNotificationAnimator();
            notify.AdoptSilently(false);
            notify.Tick(true, 0.016f);
            notify.Reset();

            Assert.AreEqual(0, notify.EntriesPlayed);
            Assert.AreEqual(0f, notify.Alpha);
            Assert.AreEqual(UiNotificationPhase.Idle, notify.Phase);
        }

        // --- silent adoption across the board --------------------------------

        [Test]
        public void EveryWidgetAdoptsItsCurrentStateSilently()
        {
            var bar = new UiValueAnimator();
            var notify = new UiNotificationAnimator();
            var rows = new UiStableRowRegistry();

            bar.Tick(0.7f, 0.016f);
            notify.AdoptSilently(true);
            rows.BeginRefresh();
            rows.Observe("existing");
            rows.EndRefresh();

            Assert.AreEqual(0, bar.HighlightsShown, "a restored bar flashes nothing");
            Assert.AreEqual(0, notify.EntriesPlayed, "a restored message replays nothing");

            rows.BeginRefresh();
            Assert.AreEqual(UiRowPhase.Held, rows.Observe("existing"),
                "and a restored row does not re-enter");
            rows.EndRefresh();
        }

        [Test]
        public void ARejectionCannotProduceASuccessResponse()
        {
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0.5f);

            foreach (float refused in new[] { 0f, -1f, -0.0001f })
                bar.RaiseDelta(refused);

            Assert.AreEqual(0, bar.HighlightsShown,
                "every shape a refusal takes must stay silent");
        }

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
