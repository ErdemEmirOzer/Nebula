using NUnit.Framework;
using UnityEngine;
using Nebula.UI.Brand;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Which authored composition a viewport gets, and where the wordmark sits on it.
    /// <para>
    /// Pure selection logic, so it is checked here rather than through a canvas.
    /// The rule that matters is that each aspect is an <b>authored export</b>: 21:9
    /// is not 16:9 stretched, it is a different painting with 880px of dark canvas on
    /// the left that the 16:9 plate does not have.
    /// </para>
    /// </summary>
    public class NebulaBrandAspectSelectionTests
    {
        [Test]
        public void EachTargetResolutionPicksItsAuthoredAspect()
        {
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine,
                NebulaBrandAssetCatalog.AspectFor(1280, 720), "1280x720");
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine,
                NebulaBrandAssetCatalog.AspectFor(1920, 1080), "1920x1080");
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine,
                NebulaBrandAssetCatalog.AspectFor(3840, 2160), "3840x2160");
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByTen,
                NebulaBrandAssetCatalog.AspectFor(2560, 1600), "2560x1600");
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByTen,
                NebulaBrandAssetCatalog.AspectFor(1920, 1200), "1920x1200");
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.UltraWide,
                NebulaBrandAssetCatalog.AspectFor(3440, 1440), "3440x1440");
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.UltraWide,
                NebulaBrandAssetCatalog.AspectFor(2560, 1080), "2560x1080");
        }

        /// <summary>
        /// An unrecognised ratio takes the nearest authored aspect rather than
        /// stretching one to fit.
        /// </summary>
        [Test]
        public void AnUnknownRatioFallsToTheNearestAuthoredAspect()
        {
            // 4:3 and 5:4 are nearer 16:10 than 16:9 in ratio, but both sit below the
            // 16:10 band, so they take 16:9 and are letterbox-cropped rather than
            // squeezed into a composition painted for a wider frame.
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine,
                NebulaBrandAssetCatalog.AspectFor(1024, 768), "4:3");
            // 2:1 is between 16:10 and 21:9 and takes 16:9, whose crop is the smaller
            // of the two errors.
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine,
                NebulaBrandAssetCatalog.AspectFor(2048, 1024), "2:1");
            // A zero height cannot divide; it must not throw.
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine,
                NebulaBrandAssetCatalog.AspectFor(1920, 0), "degenerate");
        }

        [Test]
        public void TheSixteenByNineLadderFollowsQualityAndHeight()
        {
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtDetail.Low,
                NebulaBrandAssetCatalog.DetailFor(720, 5), "720p takes the low plate at any quality");
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtDetail.Low,
                NebulaBrandAssetCatalog.DetailFor(2160, 1), "Low quality takes the low plate at any height");
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtDetail.Hd,
                NebulaBrandAssetCatalog.DetailFor(1080, 5), "1080p");
            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtDetail.Uhd,
                NebulaBrandAssetCatalog.DetailFor(2160, 5), "4K at high quality");
        }

        /// <summary>
        /// A real 4K panel at the quality level the project itself calls High takes
        /// the authored 3840x2160 plate.
        /// <para>
        /// The selectors compared against Unity's raw ladder index — <c>qualityLevel
        /// &lt;= 3</c> — and Unity's default ladder puts High at index 3, so the one
        /// configuration the 4K export exists for resolved to the 1920x1080 plate and
        /// the 8.3MB asset was unreachable from the shipping matrix. The level is
        /// looked up by its authored name here rather than hard-coded, because the
        /// index is exactly the thing that must stop deciding this.
        /// </para>
        /// </summary>
        [Test]
        public void FourKAtTheHighQualityLevelTakesTheAuthoredUhdPlate()
        {
            int high = QualityLevelNamed("High");
            Assert.GreaterOrEqual(high, 0, "the project ladder no longer has a level named High");

            Assert.AreEqual(NebulaBrandAssetCatalog.KeyArtDetail.Uhd,
                NebulaBrandAssetCatalog.DetailFor(2160, high),
                "3840x2160 at High must draw brand.keyart.clean.uhd, not the HD plate");
        }

        /// <summary>
        /// The 2048 wordmark is reachable. It is the top rung of the mark ladder and
        /// the only export large enough for the 1424px the mark is drawn at on a 4K
        /// 16:9 plate; before this it could not be selected by any shipping
        /// configuration at all.
        /// </summary>
        [Test]
        public void TheLargestMarkIsReachableAtFourK()
        {
            int high = QualityLevelNamed("High");
            Assert.GreaterOrEqual(high, 0, "the project ladder no longer has a level named High");

            Assert.AreEqual(NebulaBrandAssetCatalog.MarkQuality.High,
                NebulaBrandAssetCatalog.MarkQualityFor(2160, high),
                "3840x2160 at High must take the 2048 wordmark");
        }

        /// <summary>
        /// Reordering the quality ladder in ProjectSettings must not silently change
        /// which plate a screen gets.
        /// <para>
        /// Asserted by walking the live ladder: whichever index the project's Low,
        /// Medium and High rungs happen to sit at, each must select the plate its own
        /// tier is for. An index comparison passes this only by accident of ordering.
        /// </para>
        /// </summary>
        [Test]
        public void TheLadderIsReadByTierSoReorderingLevelsCannotBreakSelection()
        {
            AssertTier("Low", 2160, NebulaBrandAssetCatalog.KeyArtDetail.Low);
            AssertTier("Medium", 2160, NebulaBrandAssetCatalog.KeyArtDetail.Hd);
            AssertTier("High", 2160, NebulaBrandAssetCatalog.KeyArtDetail.Uhd);
        }

        private static void AssertTier(string levelName, int height,
            NebulaBrandAssetCatalog.KeyArtDetail expected)
        {
            int level = QualityLevelNamed(levelName);
            Assert.GreaterOrEqual(level, 0, $"the project ladder has no level named {levelName}");
            Assert.AreEqual(expected, NebulaBrandAssetCatalog.DetailFor(height, level),
                $"{height}p at the level named {levelName} (index {level})");
        }

        /// <summary>The index of an authored quality level, or -1. Never a hard-coded number.</summary>
        private static int QualityLevelNamed(string name)
        {
            string[] names = QualitySettings.names;
            if (names == null) return -1;
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(names[i], name, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        [Test]
        public void HighContrastSelectsTheFlatMark()
        {
            Assert.AreEqual(NebulaBrandAssetCatalog.MarkStyle.Flat,
                NebulaBrandAssetCatalog.StyleFor(highContrast: true));
            Assert.AreEqual(NebulaBrandAssetCatalog.MarkStyle.Effects,
                NebulaBrandAssetCatalog.StyleFor(highContrast: false));
        }

        [Test]
        public void TheMarkTierFollowsQualityAndHeight()
        {
            Assert.AreEqual(NebulaBrandAssetCatalog.MarkQuality.Compact,
                NebulaBrandAssetCatalog.MarkQualityFor(600, 0));
            Assert.AreEqual(NebulaBrandAssetCatalog.MarkQuality.Low,
                NebulaBrandAssetCatalog.MarkQualityFor(720, 3));
            Assert.AreEqual(NebulaBrandAssetCatalog.MarkQuality.Medium,
                NebulaBrandAssetCatalog.MarkQualityFor(1080, 5));
            Assert.AreEqual(NebulaBrandAssetCatalog.MarkQuality.High,
                NebulaBrandAssetCatalog.MarkQualityFor(2160, 5));
        }

        // ---- logo layout profiles --------------------------------------------

        /// <summary>
        /// The 21:9 profile keeps the wordmark inside the dark left extension.
        /// <para>
        /// The extension is x 0..880 of 3440 — everything left of 0.2558 — and it is
        /// the only part of that composition which is not painted scene. A profile
        /// that spilled past it would put the wordmark on the starfield, and on a
        /// wider screen would drag it toward the middle.
        /// </para>
        /// </summary>
        [Test]
        public void TheUltraWideLogoStaysInsideTheDarkExtension()
        {
            const float extensionEdge = 880f / 3440f;
            NebulaBrandKeyArtView.LogoProfile profile =
                NebulaBrandKeyArtView.LogoProfileFor(NebulaBrandAssetCatalog.KeyArtAspect.UltraWide);

            Assert.Greater(profile.XMin, 0f, "the wordmark must not touch the left edge");
            Assert.Less(profile.XMin + profile.Width, extensionEdge,
                $"the wordmark runs to {profile.XMin + profile.Width:0.0000}, past the scene edge at {extensionEdge:0.0000}");
        }

        /// <summary>
        /// The 16:10 profile resolves y against the authored band rather than the
        /// whole canvas.
        /// <para>
        /// That export is the 2560x1440 authored image at y=80 with new canvas above
        /// and below, so a 16:9 normalized v sits at 0.05 + 0.9v. Using the 16:9
        /// number directly would lift the wordmark into the empty top band.
        /// </para>
        /// </summary>
        [Test]
        public void TheSixteenByTenLogoIsResolvedAgainstTheAuthoredBand()
        {
            NebulaBrandKeyArtView.LogoProfile wide =
                NebulaBrandKeyArtView.LogoProfileFor(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByTen);
            NebulaBrandKeyArtView.LogoProfile standard =
                NebulaBrandKeyArtView.LogoProfileFor(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine);

            Assert.AreEqual(0.05f + 0.9f * standard.YMin, wide.YMin, 0.0001f,
                "y must be mapped through the 80px top band");
            Assert.AreEqual(standard.XMin, wide.XMin, 0.0001f, "x is unchanged; the width is the same 2560");
            // Narrower, not taller. Height follows the mark's own aspect from the
            // width, and this plate is taller relative to its width, so keeping 16:9's
            // width would render a mark that reaches past the menu column's top edge.
            Assert.LessOrEqual(wide.Width, standard.Width,
                "the 16:10 wordmark must not be wider than the 16:9 one");
            Assert.Greater(wide.Width, standard.Width * 0.8f,
                "the 16:10 wordmark has been shrunk far enough to read as a different lockup");
        }

        /// <summary>
        /// 16:9 keeps the footprint the composited wordmark occupied, so switching
        /// from the combined plate to clean-plus-logo does not move it.
        /// </summary>
        [Test]
        public void TheSixteenByNineLogoKeepsTheMeasuredFootprint()
        {
            NebulaBrandKeyArtView.LogoProfile profile =
                NebulaBrandKeyArtView.LogoProfileFor(NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine);

            Assert.AreEqual(0.0275f, profile.XMin, 0.0001f);
            Assert.AreEqual(0.0595f, profile.YMin, 0.0001f);
            Assert.AreEqual(0.3708f, profile.Width, 0.0001f);
        }

        /// <summary>
        /// No profile carries a height. It is derived from the mark's own aspect at
        /// layout time, which is what makes "never compress the logo" structural
        /// rather than a number someone has to keep correct.
        /// </summary>
        [Test]
        public void EverySafeAreaFitsInsideItsPlate()
        {
            foreach (NebulaBrandAssetCatalog.KeyArtAspect aspect in
                     new[] { NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine,
                             NebulaBrandAssetCatalog.KeyArtAspect.SixteenByTen,
                             NebulaBrandAssetCatalog.KeyArtAspect.UltraWide })
            {
                Rect safe = NebulaBrandKeyArtView.CriticalSafeAreaFor(aspect);
                Assert.GreaterOrEqual(safe.xMin, 0f, aspect + ": safe area starts off the plate");
                Assert.LessOrEqual(safe.xMax, 1f, aspect + ": safe area runs off the plate");
                Assert.GreaterOrEqual(safe.yMin, 0f, aspect + ": safe area starts above the plate");
                Assert.LessOrEqual(safe.yMax, 1f, aspect + ": safe area runs below the plate");

                NebulaBrandKeyArtView.LogoProfile profile = NebulaBrandKeyArtView.LogoProfileFor(aspect);
                Assert.GreaterOrEqual(profile.XMin, safe.xMin - 0.0001f,
                    aspect + ": the wordmark starts outside the safe area");
                Assert.LessOrEqual(profile.XMin + profile.Width, safe.xMax + 0.0001f,
                    aspect + ": the wordmark ends outside the safe area");
            }
        }
    }
}
