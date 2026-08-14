using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-05.3: the celestial runtime contracts — property-block ownership,
    /// purely-time-derived rotation, and the single-writer rules for the black
    /// hole and the asteroid field.
    /// </summary>
    public class Ani053CelestialRuntimeAnimationTests
    {
        [TearDown]
        public void Cleanup()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
        }

        [Test]
        public void AllFourStarClasses_ResolveToTheirOwnProfile()
        {
            var rates = Enum.GetValues(typeof(StarClass)).Cast<StarClass>()
                .Select(c => CelestialAnimationProfiles.Star(c).SpinDegreesPerSecond).ToArray();

            Assert.AreEqual(4, rates.Length);
            Assert.AreEqual(4, rates.Distinct().Count(), "each class must carry itself differently");
            foreach (float rate in rates) Assert.Greater(rate, 0f);
        }

        [Test]
        public void AStarsMotion_IsAlwaysSlowEnoughToReadAsAStar()
        {
            foreach (StarClass starClass in Enum.GetValues(typeof(StarClass)).Cast<StarClass>())
            {
                CelestialMotion motion = CelestialAnimationProfiles.Star(starClass);
                Assert.Less(motion.SpinDegreesPerSecond, 1f, $"{starClass} surface");
                Assert.Less(motion.SecondaryDegreesPerSecond, 2f, $"{starClass} corona");
                Assert.Greater(motion.SecondaryDegreesPerSecond, 0f, "the corona must drift, not blink");
            }
        }

        [Test]
        public void RotationIsAPureFunctionOfAge_SoNothingAccumulates()
        {
            float first = CelestialAnimationProfiles.SpinDegrees(0.5f, 1234.5f);
            Assert.AreEqual(first, CelestialAnimationProfiles.SpinDegrees(0.5f, 1234.5f), 1e-4f);
            Assert.GreaterOrEqual(first, 0f);
            Assert.Less(first, 360f, "and no save field is ever needed for it");
        }

        [Test]
        public void TenThousandTicksWorthOfAge_StaysWrapped()
        {
            float far = CelestialAnimationProfiles.SpinDegrees(0.9f, 10000f * 0.016f);
            Assert.GreaterOrEqual(far, 0f);
            Assert.Less(far, 360f);
        }

        // --- black hole -----------------------------------------------------------

        [Test]
        public void TheEventHorizonNeverTurns_OnlyTheDiskDoes()
        {
            Assert.IsTrue(CelestialAnimationProfiles.BlackHoleCore.IsStill);

            CelestialMotion disk = CelestialAnimationProfiles.BlackHoleDisk;
            Assert.AreEqual(0f, disk.SpinDegreesPerSecond, "the body itself is fixed");
            Assert.AreEqual(0f, disk.SurfaceFlowPerSecond, "and its surface does not flow");
            Assert.Greater(disk.SecondaryDegreesPerSecond, 0f, "the accretion disk turns");
        }

        [Test]
        public void PhotosensitivityDampsTheDiskHardest()
        {
            Assert.AreEqual(1f, CelestialAnimationProfiles.PhotosensitivityScale);
            LightingAccessibility.FlashReduction = true;
            float reduced = CelestialAnimationProfiles.PhotosensitivityScale;
            LightingAccessibility.PhotosensitivitySafeMode = true;
            Assert.Less(CelestialAnimationProfiles.PhotosensitivityScale, reduced);
        }

        // --- asteroids -------------------------------------------------------------

        [Test]
        public void TheSameRockAlwaysTumblesTheSameWay()
        {
            Vector3 axis = CelestialAnimationProfiles.AsteroidAxis("rock-99");
            Assert.AreEqual(axis, CelestialAnimationProfiles.AsteroidAxis("rock-99"));
            Assert.AreEqual(1f, axis.magnitude, 1e-4f);
            Assert.AreNotEqual(axis, CelestialAnimationProfiles.AsteroidAxis("rock-100"));
        }

        [Test]
        public void MotionOff_StopsTheTumbleButNotTheDrift()
        {
            // The drift/tumble split: AsteroidDriftSystem stays the single
            // writer, and only the decorative half answers the setting.
            AnimationAccessibility.MotionOff = true;
            Assert.AreEqual(0f, AnimationAccessibility.MotionScale,
                "with motion off the tumble multiplier is zero");
            Assert.AreEqual(0f, CelestialAnimationProfiles.SpinDegrees(2f, 500f));
        }

        [Test]
        public void ReducedMotion_SlowsTheWorldWithoutStoppingIt()
        {
            float full = CelestialAnimationProfiles.SpinDegrees(1f, 10f);
            AnimationAccessibility.ReducedMotion = true;
            float reduced = CelestialAnimationProfiles.SpinDegrees(1f, 10f);
            Assert.Less(reduced, full);
            Assert.Greater(reduced, 0f);
        }

        // --- property-block ownership ------------------------------------------------

        [Test]
        public void LightingAndAnimation_OwnDisjointChannels()
        {
            Assert.IsEmpty(MaterialPropertyChannels.LightingOwned
                .Intersect(MaterialPropertyChannels.AnimationOwned),
                "a shared channel would let one system wipe the other");
        }

        [Test]
        public void AnimationOnlyClaimsTheTextureOffsetChannels()
        {
            foreach (int id in MaterialPropertyChannels.AnimationOwned)
            {
                Assert.IsTrue(MaterialPropertyChannels.IsAnimationOwned(id));
                Assert.IsFalse(MaterialPropertyChannels.IsLightingOwned(id));
            }
            Assert.IsTrue(MaterialPropertyChannels.IsLightingOwned(Shader.PropertyToID("_EmissionColor")));
        }

        [Test]
        public void WritingTheOffset_LeavesLightingValuesIntact()
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(MaterialPropertyChannels.EmissionColorId, new Color(0.2f, 0.4f, 0.6f));
            block.SetFloat(MaterialPropertyChannels.SmoothnessId, 0.33f);

            block.SetVector(MaterialPropertyChannels.MainTexStId, new Vector4(1f, 1f, 0.75f, 0f));

            Color emission = block.GetColor(MaterialPropertyChannels.EmissionColorId);
            Assert.AreEqual(0.2f, emission.r, 1e-4f);
            Assert.AreEqual(0.4f, emission.g, 1e-4f);
            Assert.AreEqual(0.6f, emission.b, 1e-4f);
            Assert.AreEqual(0.33f, block.GetFloat(MaterialPropertyChannels.SmoothnessId), 1e-4f);
        }

        // --- versions -------------------------------------------------------------------

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
