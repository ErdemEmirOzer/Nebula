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
    /// ANI-05: how each celestial family carries itself, who owns which shader
    /// property, and the rules that keep world motion deterministic across a
    /// reload.
    /// </summary>
    public class Ani05WorldPoiUiMapAnimationTests
    {
        [TearDown]
        public void Cleanup()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
        }

        // --- §1 property ownership -----------------------------------------------

        [Test]
        public void LightingAndAnimation_OwnDisjointProperties()
        {
            int[] lighting = MaterialPropertyChannels.LightingOwned;
            int[] animation = MaterialPropertyChannels.AnimationOwned;

            Assert.IsNotEmpty(lighting);
            Assert.IsNotEmpty(animation);
            Assert.IsEmpty(lighting.Intersect(animation),
                "a property owned by both would let one system wipe the other");
        }

        [Test]
        public void AnimationNeverClaimsALightingProperty()
        {
            foreach (int id in MaterialPropertyChannels.LightingOwned)
            {
                Assert.IsTrue(MaterialPropertyChannels.IsLightingOwned(id));
                Assert.IsFalse(MaterialPropertyChannels.IsAnimationOwned(id),
                    "emission, smoothness and metallic belong to LGT alone");
            }
        }

        [Test]
        public void TheFourLightingPropertiesAreTheOnesLgtActuallyWrites()
        {
            Assert.IsTrue(MaterialPropertyChannels.IsLightingOwned(Shader.PropertyToID("_EmissionColor")));
            Assert.IsTrue(MaterialPropertyChannels.IsLightingOwned(Shader.PropertyToID("_Smoothness")));
            Assert.IsTrue(MaterialPropertyChannels.IsLightingOwned(Shader.PropertyToID("_Glossiness")));
            Assert.IsTrue(MaterialPropertyChannels.IsLightingOwned(Shader.PropertyToID("_Metallic")));
        }

        [Test]
        public void AWriteToAnEmptyBlock_LeavesLightingValuesAlone()
        {
            // The read-modify-write contract, exercised on a real block.
            var block = new MaterialPropertyBlock();
            block.SetColor(MaterialPropertyChannels.EmissionColorId, Color.red);
            block.SetFloat(MaterialPropertyChannels.MetallicId, 0.7f);

            block.SetVector(MaterialPropertyChannels.MainTexStId, new Vector4(1f, 1f, 0.5f, 0f));

            Assert.AreEqual(Color.red, block.GetColor(MaterialPropertyChannels.EmissionColorId),
                "scrolling a surface must not touch its emission");
            Assert.AreEqual(0.7f, block.GetFloat(MaterialPropertyChannels.MetallicId), 1e-4f);
        }

        // --- §2 stars ----------------------------------------------------------------

        [Test]
        public void EveryStarClass_HasACharacter()
        {
            foreach (StarClass starClass in Enum.GetValues(typeof(StarClass)).Cast<StarClass>())
            {
                CelestialMotion motion = CelestialAnimationProfiles.Star(starClass);
                Assert.Greater(motion.SpinDegreesPerSecond, 0f, $"{starClass} must turn");
                Assert.Less(motion.SpinDegreesPerSecond, 3f, $"{starClass} must not read as a prop");
                Assert.Greater(motion.SecondaryDegreesPerSecond, 0f, $"{starClass} must have a moving corona");
            }
        }

        [Test]
        public void MassReadsAsSlowness_ABlueGiantIsTheMostPonderous()
        {
            float dwarf = CelestialAnimationProfiles.Star(StarClass.RedDwarf).SpinDegreesPerSecond;
            float yellow = CelestialAnimationProfiles.Star(StarClass.Yellow).SpinDegreesPerSecond;
            float white = CelestialAnimationProfiles.Star(StarClass.White).SpinDegreesPerSecond;
            float giant = CelestialAnimationProfiles.Star(StarClass.BlueGiant).SpinDegreesPerSecond;

            Assert.Greater(dwarf, yellow);
            Assert.Greater(yellow, white);
            Assert.Greater(white, giant, "the blue giant must never spin like a toy");
        }

        [Test]
        public void AStarsCoronaMovesDifferentlyFromItsSurface()
        {
            foreach (StarClass starClass in Enum.GetValues(typeof(StarClass)).Cast<StarClass>())
            {
                CelestialMotion motion = CelestialAnimationProfiles.Star(starClass);
                Assert.AreNotEqual(motion.SpinDegreesPerSecond, motion.SecondaryDegreesPerSecond,
                    $"{starClass}'s corona must not move in lockstep with its surface");
            }
        }

        // --- §3 planets -----------------------------------------------------------------

        [Test]
        public void EveryArchetype_HasACharacter()
        {
            foreach (PlanetArchetype archetype in Enum.GetValues(typeof(PlanetArchetype)).Cast<PlanetArchetype>())
            {
                CelestialMotion motion = CelestialAnimationProfiles.Planet(archetype);
                Assert.Greater(motion.SpinDegreesPerSecond, 0f, $"{archetype} must turn on its axis");
                Assert.Less(motion.SpinDegreesPerSecond, 5f, $"{archetype} must stay slow");
            }
        }

        [Test]
        public void MetalIsDeadWeight_AndOnlyGasReallyFlows()
        {
            CelestialMotion metal = CelestialAnimationProfiles.Planet(PlanetArchetype.MetallicRocky);
            CelestialMotion gas = CelestialAnimationProfiles.Planet(PlanetArchetype.UpperAtmospherePlatform);
            CelestialMotion water = CelestialAnimationProfiles.Planet(PlanetArchetype.FloatingOceanic);
            CelestialMotion crystal = CelestialAnimationProfiles.Planet(PlanetArchetype.CrystalShell);

            Assert.AreEqual(0f, metal.SurfaceFlowPerSecond, "solid metal has no surface flow");
            Assert.AreEqual(0f, crystal.SurfaceFlowPerSecond, "nor does a crystal shell");
            Assert.Greater(gas.SurfaceFlowPerSecond, water.SurfaceFlowPerSecond,
                "a gas envelope flows harder than an ocean's surface");
        }

        [Test]
        public void TheEnvironmentChangesAmplitudeOnly_NeverDuration()
        {
            float calm = CelestialAnimationProfiles.EnvironmentAmplitude(EnvironmentClass.Calm);
            float harsh = CelestialAnimationProfiles.EnvironmentAmplitude(EnvironmentClass.Harsh);
            float extreme = CelestialAnimationProfiles.EnvironmentAmplitude(EnvironmentClass.Extreme);

            Assert.AreEqual(1f, calm, "calm is the baseline");
            Assert.Greater(harsh, calm);
            Assert.Greater(extreme, harsh);
            Assert.Less(extreme, 2f, "a hostile world looks agitated, not frantic");
        }

        // --- §4 black hole ---------------------------------------------------------------

        [Test]
        public void ABlackHolesCoreNeverTurns()
        {
            CelestialMotion core = CelestialAnimationProfiles.BlackHoleCore;
            Assert.IsTrue(core.IsStill, "the event horizon is still and black by definition");
        }

        [Test]
        public void OnlyTheDiskTurns_AndItTurnsHeavily()
        {
            CelestialMotion disk = CelestialAnimationProfiles.BlackHoleDisk;
            Assert.AreEqual(0f, disk.SpinDegreesPerSecond, "the body itself does not spin");
            Assert.Greater(disk.SecondaryDegreesPerSecond, 0f, "the accretion disk does");
        }

        [Test]
        public void PhotosensitivitySafeMode_DampsTheBusiestBodyHardest()
        {
            float normal = CelestialAnimationProfiles.PhotosensitivityScale;
            LightingAccessibility.FlashReduction = true;
            float reduced = CelestialAnimationProfiles.PhotosensitivityScale;
            LightingAccessibility.PhotosensitivitySafeMode = true;
            float safe = CelestialAnimationProfiles.PhotosensitivityScale;

            Assert.AreEqual(1f, normal);
            Assert.Less(reduced, normal);
            Assert.Less(safe, reduced, "Safe Mode is the stricter of the two");
        }

        // --- §5 asteroids ------------------------------------------------------------------

        [Test]
        public void TheSameAsteroidAlwaysTumblesTheSameWay()
        {
            Vector3 first = CelestialAnimationProfiles.AsteroidAxis("rock-17");
            Assert.AreEqual(first, CelestialAnimationProfiles.AsteroidAxis("rock-17"),
                "a reload must not put the same rock on a different axis");
            Assert.AreEqual(1f, first.magnitude, 1e-4f);
            Assert.AreNotEqual(first, CelestialAnimationProfiles.AsteroidAxis("rock-18"));
        }

        [Test]
        public void AnAsteroidWithNoId_StillHasASafeAxis()
        {
            Assert.AreEqual(Vector3.up, CelestialAnimationProfiles.AsteroidAxis(null));
            Assert.AreEqual(Vector3.up, CelestialAnimationProfiles.AsteroidAxis(""));
        }

        [Test]
        public void AsteroidRatesAreBoundedAndDeterministic()
        {
            for (int i = 0; i < 50; i++)
            {
                CelestialMotion motion = CelestialAnimationProfiles.Asteroid($"rock-{i}");
                Assert.GreaterOrEqual(motion.SpinDegreesPerSecond, 0.5f);
                Assert.LessOrEqual(motion.SpinDegreesPerSecond, 2f, "a rock drifts, it does not spin");
            }

            Assert.AreEqual(CelestialAnimationProfiles.Asteroid("rock-7").SpinDegreesPerSecond,
                CelestialAnimationProfiles.Asteroid("rock-7").SpinDegreesPerSecond);
        }

        // --- spin is a pure function ------------------------------------------------------------

        [Test]
        public void SpinIsAPureFunctionOfAge_SoNothingAccumulates()
        {
            float at100 = CelestialAnimationProfiles.SpinDegrees(1.5f, 100f);
            Assert.AreEqual(at100, CelestialAnimationProfiles.SpinDegrees(1.5f, 100f), 1e-4f,
                "a demote and a promote must land on the same angle");
            Assert.GreaterOrEqual(at100, 0f);
            Assert.Less(at100, 360f, "the angle must stay wrapped, never grow without bound");
        }

        [Test]
        public void TenThousandTicksWorthOfAge_StillWrapsCleanly()
        {
            float far = CelestialAnimationProfiles.SpinDegrees(2f, 10000f * 0.016f);
            Assert.GreaterOrEqual(far, 0f);
            Assert.Less(far, 360f, "no unbounded drift after a long session");
        }

        // --- §10 accessibility ---------------------------------------------------------------------

        [Test]
        public void MotionOff_StopsEveryWorldRotation()
        {
            AnimationAccessibility.MotionOff = true;
            Assert.AreEqual(0f, CelestialAnimationProfiles.SpinDegrees(5f, 1000f));
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

        [Test]
        public void ABodyTypeWithNoAuthoredCharacter_StandsStillRatherThanGuessing()
        {
            CelestialMotion moon = CelestialAnimationProfiles.ForBodyType(
                CelestialBodyType.Moon, StarClass.Yellow, PlanetArchetype.MetallicRocky, "moon-1");
            Assert.IsTrue(moon.IsStill, "an unhandled family must not invent motion");
        }

        [Test]
        public void EveryHandledBodyType_ResolvesToRealMotion()
        {
            Assert.IsFalse(CelestialAnimationProfiles.ForBodyType(CelestialBodyType.Star,
                StarClass.Yellow, PlanetArchetype.Biomass, "s").IsStill);
            Assert.IsFalse(CelestialAnimationProfiles.ForBodyType(CelestialBodyType.Planet,
                StarClass.Yellow, PlanetArchetype.Biomass, "p").IsStill);
            Assert.IsFalse(CelestialAnimationProfiles.ForBodyType(CelestialBodyType.BlackHole,
                StarClass.Yellow, PlanetArchetype.Biomass, "b").IsStill);
            Assert.IsFalse(CelestialAnimationProfiles.ForBodyType(CelestialBodyType.Asteroid,
                StarClass.Yellow, PlanetArchetype.Biomass, "a").IsStill);
        }

        // --- §13 versions -------------------------------------------------------------------------------

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
