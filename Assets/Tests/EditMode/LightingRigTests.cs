using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// LGT-01: the lighting contract — what each role is worth relative to the
    /// others, what a quality tier is allowed to spend, and what the
    /// visual-only accessibility settings may and may not touch.
    /// </summary>
    public class LightingRigTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        private Light Create(LightSpec spec, string name)
        {
            Light light = spec.Create(null, name);
            spawned.Add(light.gameObject);
            return light;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
            LightingAccessibility.ResetToDefaults();
        }

        // ---- rig roles -------------------------------------------------

        [Test]
        public void Create_BuildsAChildLightCarryingEveryFieldOfTheSpec()
        {
            var parent = new GameObject("Parent");
            spawned.Add(parent);

            var spec = LightSpec.Point(new Color(0.2f, 0.4f, 0.6f), 1.75f, 42f);
            Light light = spec.Create(parent.transform, "Test Light");

            Assert.AreSame(parent.transform, light.transform.parent);
            Assert.AreEqual(LightType.Point, light.type);
            Assert.AreEqual(new Color(0.2f, 0.4f, 0.6f), light.color);
            Assert.AreEqual(1.75f, light.intensity, 1e-4f);
            Assert.AreEqual(42f, light.range, 1e-4f);
            Assert.AreEqual(Vector3.zero, light.transform.localPosition,
                "Create must leave placement to the caller");
        }

        [Test]
        public void SystemKey_IsAPointLightSoEveryPlanetIsLitFromWhereTheStarActuallyIs()
        {
            Light key = Create(LightingRig.SystemKey(Color.white, 800f), "System Key Light");

            Assert.AreEqual(LightType.Point, key.type,
                "a single directional light would light the far side of the system from the wrong angle");
            Assert.AreEqual(LightingRig.StarIntensity, key.intensity, 1e-4f);
            Assert.AreEqual(800f, key.range, 1e-4f);
        }

        [Test]
        public void ShipHullFill_ShapesTheHullWithoutLightingTheNeighbourhood()
        {
            LightSpec fill = LightingRig.ShipHullFill;

            Assert.Less(fill.Intensity, LightingRig.StarIntensity, "fill must never compete with the key light");
            Assert.Less(fill.Range, 50f, "fill is there to shape the hull, not to light space");
            Assert.Greater(fill.Color.b, fill.Color.r, "hull fill reads cold against a warm star");
        }

        [Test]
        public void PreviewKey_LightsOnlyItsOwnLayer()
        {
            const int previewLayer = 30;
            Light key = Create(LightingRig.PreviewKey(previewLayer), "Preview Light");

            Assert.AreEqual(1 << previewLayer, key.cullingMask,
                "a preview stage light must not leak onto the live scene it shares the world with");
        }

        [Test]
        public void NoRigRole_CastsShadowsByItself_ShadowsAreTheProfilesCallOnTheKeyLightAlone()
        {
            var roles = new[]
            {
                LightingRig.SystemKey(Color.white, 800f),
                LightingRig.ShipHullFill,
                LightingRig.PreviewKey(30),
                LightingRig.TransientEffect
            };

            foreach (LightSpec spec in roles)
                Assert.AreEqual(LightShadows.None, spec.Shadows,
                    "only the quality profile may turn shadows on, and only on the key light");
        }

        // ---- quality profiles ------------------------------------------

        [Test]
        public void Profiles_GetStrictlyMoreExpensiveAsTheTierRises()
        {
            LightingQualityProfile low = LightingQualityProfile.Low;
            LightingQualityProfile medium = LightingQualityProfile.Medium;
            LightingQualityProfile high = LightingQualityProfile.High;

            Assert.Less(low.PixelLightBudget, medium.PixelLightBudget);
            Assert.Less(medium.PixelLightBudget, high.PixelLightBudget);
            Assert.Less(low.TransientLightCapacity, medium.TransientLightCapacity);
            Assert.Less(medium.TransientLightCapacity, high.TransientLightCapacity);
            Assert.Less(low.MaxRealtimeLights, medium.MaxRealtimeLights);
            Assert.Less(medium.MaxRealtimeLights, high.MaxRealtimeLights);
            Assert.Less(low.CullDistanceScale, high.CullDistanceScale);
        }

        [Test]
        public void EveryProfile_LeavesRoomForTheKeyLightInsideItsRealtimeBudget()
        {
            foreach (LightingQualityProfile p in new[]
                     { LightingQualityProfile.Low, LightingQualityProfile.Medium, LightingQualityProfile.High })
                Assert.Greater(p.MaxRealtimeLights, p.TransientLightCapacity,
                    $"{p.Quality}: the pool must not be able to fill the budget and crowd out the key light");
        }

        [Test]
        public void OnlyHigh_PaysForShadows_AndNeverForMoreThanOneCaster()
        {
            Assert.AreEqual(LightShadows.None, LightingQualityProfile.Low.KeyLightShadows);
            Assert.AreEqual(LightShadows.None, LightingQualityProfile.Medium.KeyLightShadows);
            Assert.AreNotEqual(LightShadows.None, LightingQualityProfile.High.KeyLightShadows);

            Assert.AreEqual(0, LightingQualityProfile.Low.ShadowCasterBudget);
            Assert.AreEqual(0, LightingQualityProfile.Medium.ShadowCasterBudget);
            Assert.AreEqual(1, LightingQualityProfile.High.ShadowCasterBudget,
                "cube-map shadows for every point light is the one cost this game cannot absorb");
            Assert.Greater(LightingQualityProfile.High.ShadowDistance, 0f);
        }

        [Test]
        public void CheapTiers_FallBackToEmissiveWhenNoLightIsAvailable()
        {
            Assert.IsTrue(LightingQualityProfile.Low.EmissiveOnlyFallback);
            Assert.IsTrue(LightingQualityProfile.Medium.EmissiveOnlyFallback);
            Assert.IsFalse(LightingQualityProfile.High.EmissiveOnlyFallback);
        }

        [TestCase(LightingQuality.Low)]
        [TestCase(LightingQuality.Medium)]
        [TestCase(LightingQuality.High)]
        public void Resolve_ReturnsTheProfileItWasAskedFor(LightingQuality quality)
        {
            Assert.AreEqual(quality, LightingQualityProfile.Resolve(quality).Quality);
        }

        [Test]
        public void Resolve_FallsBackToMedium_RatherThanThrowingOnAnUnknownTier()
        {
            var nonsense = (LightingQuality)(-7);

            Assert.AreEqual(LightingQuality.Medium, LightingQualityProfile.Resolve(nonsense).Quality,
                "a bad settings value must cost fidelity, never a session");
        }

        [TestCase("Low", true, LightingQuality.Low)]
        [TestCase("medium", true, LightingQuality.Medium)]
        [TestCase("  HIGH  ", true, LightingQuality.High)]
        [TestCase("Ultra", false, LightingQuality.Medium)]
        [TestCase("", false, LightingQuality.Medium)]
        [TestCase(null, false, LightingQuality.Medium)]
        public void TryResolve_IsCaseAndWhitespaceTolerant_AndAlwaysHandsBackAUsableProfile(
            string name, bool expectedFound, LightingQuality expectedQuality)
        {
            bool found = LightingQualityProfile.TryResolve(name, out LightingQualityProfile profile);

            Assert.AreEqual(expectedFound, found);
            Assert.AreEqual(expectedQuality, profile.Quality);
        }

        [TestCase(0, LightingQuality.Low)]
        [TestCase(1, LightingQuality.Low)]
        [TestCase(2, LightingQuality.Medium)]
        [TestCase(3, LightingQuality.Medium)]
        [TestCase(4, LightingQuality.High)]
        [TestCase(5, LightingQuality.High)]
        [TestCase(99, LightingQuality.Medium)]
        [TestCase(-1, LightingQuality.Low)]
        public void UnityQualityLadder_MapsOntoOurThreeTiers(int unityLevel, LightingQuality expected)
        {
            Assert.AreEqual(expected, LightingQualityProfile.FromUnityQualityLevel(unityLevel).Quality);
        }

        // ---- environment -----------------------------------------------

        [Test]
        public void DeepSpaceAmbient_LeavesUnlitSurfacesAsColdSilhouettes()
        {
            Color ambient = LightingEnvironment.DeepSpaceAmbient;

            Assert.Greater(ambient.maxColorComponent, 0f, "a pure black floor makes unlit hulls invisible");
            Assert.Less(ambient.maxColorComponent, 0.1f, "space stays dark — never washed with grey fill");
            Assert.Greater(ambient.b, ambient.r, "unlit sides read as cold space, not as a rendering fault");
        }

        // ---- accessibility ---------------------------------------------

        [Test]
        public void AccessibilityDefaults_AreTheUnmodifiedLook()
        {
            Assert.IsFalse(LightingAccessibility.FlashReduction);
            Assert.IsFalse(LightingAccessibility.HighContrast);
            Assert.IsFalse(LightingAccessibility.PhotosensitivitySafeMode);
            Assert.AreEqual(1f, LightingAccessibility.LightIntensityScale, 1e-4f);
            Assert.AreEqual(5f, LightingAccessibility.ScaleIntensity(5f), 1e-4f);
            Assert.AreEqual(5f, LightingAccessibility.ScaleFlashIntensity(5f), 1e-4f);
        }

        [TestCase(-10f)]
        [TestCase(0f)]
        [TestCase(99f)]
        public void LightIntensityScale_IsClampedSoNoCallerCanDriveTheScreenToBlackOrBlowItOut(float requested)
        {
            LightingAccessibility.LightIntensityScale = requested;

            Assert.GreaterOrEqual(LightingAccessibility.LightIntensityScale, LightingAccessibility.MinIntensityScale);
            Assert.LessOrEqual(LightingAccessibility.LightIntensityScale, LightingAccessibility.MaxIntensityScale);
        }

        [Test]
        public void FlashReduction_SoftensFlashesWithoutDimmingSteadyLight()
        {
            LightingAccessibility.FlashReduction = true;

            Assert.AreEqual(10f, LightingAccessibility.ScaleIntensity(10f), 1e-4f,
                "steady light is untouched — this setting is about flashes");
            Assert.Less(LightingAccessibility.ScaleFlashIntensity(10f), 10f);
        }

        [Test]
        public void PhotosensitivitySafeMode_CannotBeSoftenedByLeavingFlashReductionOff()
        {
            LightingAccessibility.FlashReduction = false;
            LightingAccessibility.PhotosensitivitySafeMode = true;

            float safe = LightingAccessibility.ScaleFlashIntensity(10f);
            Assert.Greater(LightingAccessibility.ResolveFlashDuration(0.02f), 0.02f,
                "Safe Mode stretches brief pops so they read as a glow rather than a strobe");

            LightingAccessibility.PhotosensitivitySafeMode = false;
            LightingAccessibility.FlashReduction = true;
            float merelyReduced = LightingAccessibility.ScaleFlashIntensity(10f);

            Assert.Less(safe, merelyReduced, "Safe Mode is the strongest limit and must always win");
        }

        [Test]
        public void HighContrast_DeepensTheGapBetweenLitAndUnlit()
        {
            Color normal = LightingAccessibility.ResolveAmbient(LightingEnvironment.DeepSpaceAmbient);
            LightingAccessibility.HighContrast = true;
            Color contrasted = LightingAccessibility.ResolveAmbient(LightingEnvironment.DeepSpaceAmbient);

            Assert.Less(contrasted.maxColorComponent, normal.maxColorComponent);
            Assert.AreEqual(1f, contrasted.a, 1e-4f, "ambient must stay opaque");
        }

        [Test]
        public void ResetToDefaults_ClearsEverySettingSoOneSessionNeverInheritsAnothers()
        {
            LightingAccessibility.FlashReduction = true;
            LightingAccessibility.HighContrast = true;
            LightingAccessibility.PhotosensitivitySafeMode = true;
            LightingAccessibility.LightIntensityScale = 0.3f;

            LightingAccessibility.ResetToDefaults();

            Assert.IsFalse(LightingAccessibility.FlashReduction);
            Assert.IsFalse(LightingAccessibility.HighContrast);
            Assert.IsFalse(LightingAccessibility.PhotosensitivitySafeMode);
            Assert.AreEqual(1f, LightingAccessibility.LightIntensityScale, 1e-4f);
        }
    }
}
