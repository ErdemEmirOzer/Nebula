using NUnit.Framework;
using Nebula.Ship;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure DT-001 flight test speed math (§4/§8).</summary>
    public class FlightTestSpeedMathTests
    {
        [TestCase(-5f)]
        [TestCase(0f)]
        [TestCase(0.999f)]
        public void ClampMultiplier_BelowOne_ClampsToOne(float raw)
        {
            Assert.AreEqual(1f, FlightTestSpeedMath.ClampMultiplier(raw));
        }

        [TestCase(150f)]
        [TestCase(1000f)]
        public void ClampMultiplier_AboveHundred_ClampsToHundred(float raw)
        {
            Assert.AreEqual(100f, FlightTestSpeedMath.ClampMultiplier(raw));
        }

        [Test]
        public void MultiplierFromSlider_Zero_IsOne()
        {
            Assert.AreEqual(1f, FlightTestSpeedMath.MultiplierFromSlider(0f), 0.001f);
        }

        [Test]
        public void MultiplierFromSlider_Half_IsApproximatelyTen()
        {
            Assert.AreEqual(10f, FlightTestSpeedMath.MultiplierFromSlider(0.5f), 0.01f);
        }

        [Test]
        public void MultiplierFromSlider_One_IsHundred()
        {
            Assert.AreEqual(100f, FlightTestSpeedMath.MultiplierFromSlider(1f), 0.01f);
        }

        [TestCase(0f)]
        [TestCase(0.25f)]
        [TestCase(0.5f)]
        [TestCase(0.75f)]
        [TestCase(1f)]
        public void SliderMultiplierRoundTrip_IsStable(float slider)
        {
            float multiplier = FlightTestSpeedMath.MultiplierFromSlider(slider);
            float roundTripped = FlightTestSpeedMath.SliderFromMultiplier(multiplier);
            Assert.AreEqual(slider, roundTripped, 0.001f);
        }

        [Test]
        public void ResolveEffectiveMultiplier_DebugToolsDisallowed_AlwaysOne()
        {
            Assert.AreEqual(1f, FlightTestSpeedMath.ResolveEffectiveMultiplier(50f, debugToolsAllowed: false));
            Assert.AreEqual(1f, FlightTestSpeedMath.ResolveEffectiveMultiplier(100f, debugToolsAllowed: false));
            Assert.AreEqual(1f, FlightTestSpeedMath.ResolveEffectiveMultiplier(1f, debugToolsAllowed: false));
        }

        [Test]
        public void ResolveEffectiveMultiplier_DebugToolsAllowed_ReturnsClampedRawValue()
        {
            Assert.AreEqual(37.5f, FlightTestSpeedMath.ResolveEffectiveMultiplier(37.5f, debugToolsAllowed: true));
            Assert.AreEqual(100f, FlightTestSpeedMath.ResolveEffectiveMultiplier(500f, debugToolsAllowed: true));
        }

        [Test]
        public void CommandedSpeed_MultipliesAllFourFactorsExactlyOnce()
        {
            float result = FlightTestSpeedMath.CommandedSpeed(throttle: 10f, localBoost: 2f, cruiseSpeedMultiplier: 3f, testSpeedMultiplier: 4f);
            Assert.AreEqual(240f, result, "10*2*3*4 -- each factor applied exactly once, never doubled");
        }

        [Test]
        public void CommandedSpeed_AtOneX_MatchesThePreDT001Formula()
        {
            // The original formula (throttle * boost * cruise) must be bit-identical when test == 1x.
            float original = 90f * 4f * 1.5f;
            float withTestMultiplier = FlightTestSpeedMath.CommandedSpeed(90f, 4f, 1.5f, FlightTestSpeedMath.MinMultiplier);
            Assert.AreEqual(original, withTestMultiplier);
        }

        [Test]
        public void HighSpeedExamplesFromSpec_MatchExpectedMagnitudes()
        {
            // T0 local boost: 180 * 4 * 100 = 72,000 u/s
            Assert.AreEqual(72000f, FlightTestSpeedMath.CommandedSpeed(180f, 4f, 1f, 100f), 0.01f);
            // T2 cruise: 4,500 * 100 = 450,000 u/s (boost never applies during cruise, localBoost=1 here)
            Assert.AreEqual(450000f, FlightTestSpeedMath.CommandedSpeed(4500f, 1f, 1f, 100f), 0.01f);
        }
    }
}
