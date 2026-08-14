using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-05.4.1: bar fills that never hide a number, delta highlights that
    /// only a real success can raise, and list rows that survive a rebuild.
    /// </summary>
    public class Ani0541WidgetAnimationTests
    {
        [TearDown]
        public void Cleanup()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
        }

        // --- bar fill ---------------------------------------------------------

        [Test]
        public void AFirstTickAdoptsTheCurrentValueSilently()
        {
            var bar = new UiValueAnimator();
            float shown = bar.Tick(0.8f, 0.016f);

            Assert.AreEqual(0.8f, shown, 1e-5f,
                "a restored save must not animate a bar up from zero");
            Assert.IsFalse(bar.IsHighlighting, "and must not flash as if something was earned");
        }

        [Test]
        public void AnExplicitAdoptIsSilentToo()
        {
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0.55f);

            Assert.AreEqual(0.55f, bar.ShownFill, 1e-5f);
            Assert.IsFalse(bar.IsHighlighting);
            Assert.AreEqual(0, bar.HighlightsShown);
        }

        [Test]
        public void TheFillChasesTheRealValueUpwards_AndArrives()
        {
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0f);

            float previous = -1f;
            for (int i = 0; i < 200; i++)
            {
                float shown = bar.Tick(1f, 0.016f);
                Assert.GreaterOrEqual(shown, previous - 1e-5f, "the approach must be monotonic");
                Assert.LessOrEqual(shown, 1.0001f, "and must never overshoot");
                previous = shown;
            }
            Assert.AreEqual(1f, bar.ShownFill, 1e-3f);
        }

        [Test]
        public void TheFillChasesDownwardsJustAsCleanly()
        {
            var bar = new UiValueAnimator();
            bar.AdoptSilently(1f);

            float previous = 2f;
            for (int i = 0; i < 200; i++)
            {
                float shown = bar.Tick(0f, 0.016f);
                Assert.LessOrEqual(shown, previous + 1e-5f, "monotonic on the way down as well");
                Assert.GreaterOrEqual(shown, -0.0001f);
                previous = shown;
            }
            Assert.AreEqual(0f, bar.ShownFill, 1e-3f);
        }

        [Test]
        public void TheRealValueIsAlwaysAvailableForTheText()
        {
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0f);
            bar.Tick(0.9f, 0.016f);

            Assert.AreEqual(0.9f, bar.RealValue, 1e-5f,
                "the printed number must be the truth even while the bar catches up");
            Assert.Less(bar.ShownFill, bar.RealValue, "which is the whole point of the split");
        }

        [Test]
        public void MotionOff_SnapsTheFillToTheRealValue()
        {
            AnimationAccessibility.MotionOff = true;
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0f);

            Assert.AreEqual(0.63f, bar.Tick(0.63f, 0.016f), 1e-5f,
                "with motion off the bar shows the number immediately");
        }

        // --- delta highlight ------------------------------------------------------

        [Test]
        public void ARealGain_FlashesExactlyOnce()
        {
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0.2f);

            bar.RaiseDelta(15f);
            Assert.IsTrue(bar.IsHighlighting);
            Assert.AreEqual(1, bar.HighlightsShown);

            for (int i = 0; i < 60; i++) bar.Tick(0.2f, 0.016f);
            Assert.IsFalse(bar.IsHighlighting, "and it fades on its own");
            Assert.AreEqual(1, bar.HighlightsShown, "without repeating");
        }

        [Test]
        public void AZeroTransfer_FlashesNothing()
        {
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0.5f);

            bar.RaiseDelta(0f);
            Assert.IsFalse(bar.IsHighlighting, "nothing moved, so nothing is celebrated");
            Assert.AreEqual(0, bar.HighlightsShown);
        }

        [Test]
        public void ARefusedTransaction_FlashesNothing()
        {
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0.5f);

            // What a refused deposit really returns.
            bar.RaiseDelta(-4f);
            bar.RaiseDelta(0f);

            Assert.IsFalse(bar.IsHighlighting,
                "a refusal must never look like a success");
            Assert.AreEqual(0, bar.HighlightsShown);
        }

        [Test]
        public void MotionOff_RaisesNoHighlightAtAll()
        {
            AnimationAccessibility.MotionOff = true;
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0.5f);

            bar.RaiseDelta(10f);
            Assert.IsFalse(bar.IsHighlighting);
        }

        [Test]
        public void TheHighlightDecaysSmoothly_AndNeverGoesNegative()
        {
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0.5f);
            bar.RaiseDelta(5f);

            float previous = 2f;
            for (int i = 0; i < 60; i++)
            {
                bar.Tick(0.5f, 0.016f);
                Assert.LessOrEqual(bar.Highlight01, previous + 1e-5f, "monotonic decay");
                Assert.GreaterOrEqual(bar.Highlight01, 0f);
                previous = bar.Highlight01;
            }
        }

        [Test]
        public void ResetClearsEverything()
        {
            var bar = new UiValueAnimator();
            bar.AdoptSilently(0.9f);
            bar.RaiseDelta(3f);
            bar.Reset();

            Assert.AreEqual(0f, bar.ShownFill);
            Assert.AreEqual(0, bar.HighlightsShown);
            Assert.IsFalse(bar.IsHighlighting);
        }

        // --- stable rows -------------------------------------------------------------

        [Test]
        public void ANewRowEnters_AndTheSameRowNeverEntersTwice()
        {
            var rows = new UiStableRowRegistry();

            rows.BeginRefresh();
            Assert.AreEqual(UiRowPhase.Entering, rows.Observe("vehicle-1"));
            rows.EndRefresh();

            rows.BeginRefresh();
            Assert.AreEqual(UiRowPhase.Held, rows.Observe("vehicle-1"),
                "a rebuilt list must not replay every entry");
            rows.EndRefresh();
        }

        [Test]
        public void ARemovedRowIsReportedExactlyOnce()
        {
            var rows = new UiStableRowRegistry();

            rows.BeginRefresh();
            rows.Observe("a");
            rows.Observe("b");
            rows.EndRefresh();

            rows.BeginRefresh();
            rows.Observe("a");
            IReadOnlyList<string> gone = rows.EndRefresh();

            Assert.AreEqual(1, gone.Count);
            Assert.AreEqual("b", gone[0]);

            rows.BeginRefresh();
            rows.Observe("a");
            Assert.AreEqual(0, rows.EndRefresh().Count, "and not again on the next refresh");
        }

        [Test]
        public void ARebuildOfTheSameListEntersNothing()
        {
            var rows = new UiStableRowRegistry();
            string[] ids = { "s1", "s2", "s3" };

            rows.BeginRefresh();
            foreach (string id in ids) rows.Observe(id);
            rows.EndRefresh();
            Assert.AreEqual(3, rows.EnteredLastRefresh);

            for (int cycle = 0; cycle < 50; cycle++)
            {
                rows.BeginRefresh();
                foreach (string id in ids) rows.Observe(id);
                rows.EndRefresh();
                Assert.AreEqual(0, rows.EnteredLastRefresh, $"cycle {cycle}");
            }
        }

        [Test]
        public void AThousandRefreshes_DoNotGrowTheRegistry()
        {
            var rows = new UiStableRowRegistry();
            string[] ids = { "m1", "m2", "m3", "m4" };

            for (int cycle = 0; cycle < 1000; cycle++)
            {
                rows.BeginRefresh();
                foreach (string id in ids) rows.Observe(id);
                rows.EndRefresh();
            }

            Assert.AreEqual(ids.Length, rows.KnownCount,
                "1000 rebuilds must leave exactly the rows that are really there");
        }

        [Test]
        public void AnEmptyIdIsIgnored()
        {
            var rows = new UiStableRowRegistry();
            rows.BeginRefresh();
            Assert.AreEqual(UiRowPhase.Held, rows.Observe(null));
            Assert.AreEqual(UiRowPhase.Held, rows.Observe(""));
            rows.EndRefresh();
            Assert.AreEqual(0, rows.KnownCount);
        }

        [Test]
        public void ClearForgetsEverything()
        {
            var rows = new UiStableRowRegistry();
            rows.BeginRefresh();
            rows.Observe("x");
            rows.EndRefresh();

            rows.Clear();
            Assert.AreEqual(0, rows.KnownCount);
            Assert.IsFalse(rows.IsKnown("x"));
        }

        // --- versions ---------------------------------------------------------------------

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
