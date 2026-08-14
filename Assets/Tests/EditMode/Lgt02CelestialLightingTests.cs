using System;
using System.Collections.Generic;
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
    /// LGT-02: every celestial class the game actually has must resolve to a
    /// distinct, valid and bounded lighting response — resolved from the typed
    /// enums, never from names.
    /// </summary>
    public class Lgt02CelestialLightingTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            LightingAccessibility.ResetToDefaults();
        }

        private static StarClass[] AllStarClasses => (StarClass[])Enum.GetValues(typeof(StarClass));
        private static PlanetArchetype[] AllArchetypes => (PlanetArchetype[])Enum.GetValues(typeof(PlanetArchetype));
        private static EnvironmentClass[] AllEnvironments => (EnvironmentClass[])Enum.GetValues(typeof(EnvironmentClass));

        // ---- coverage: the real enums, not guessed names -------------------

        [Test]
        public void TheGameHasFourStarClassesFivePlanetArchetypesAndThreeEnvironments()
        {
            Assert.AreEqual(4, AllStarClasses.Length);
            Assert.AreEqual(5, AllArchetypes.Length);
            Assert.AreEqual(3, AllEnvironments.Length);
        }

        [Test]
        public void EveryStarClassHasItsOwnProfile_AndNoTwoLightAlike()
        {
            var profiles = AllStarClasses.Select(CelestialLightingProfiles.ForStar).ToArray();

            foreach (StarClass cls in AllStarClasses)
                Assert.AreEqual(cls, CelestialLightingProfiles.ForStar(cls).StarClass,
                    $"{cls} resolved to the wrong profile");

            Assert.AreEqual(4, profiles.Select(p => p.KeyLightColor).Distinct().Count(),
                "four star classes must not share a key light colour — that was the LGT-01 state this part fixes");
            Assert.AreEqual(4, profiles.Select(p => p.IntensityScale).Distinct().Count());
        }

        [Test]
        public void EveryPlanetArchetypeHasItsOwnProfile()
        {
            foreach (PlanetArchetype archetype in AllArchetypes)
                Assert.AreEqual(archetype, CelestialLightingProfiles.ForPlanet(archetype).Archetype);

            Assert.AreEqual(5, CelestialLightingProfiles.AllPlanets.Select(p => p.Smoothness).Distinct().Count(),
                "each family must answer light differently");
        }

        [Test]
        public void EveryEnvironmentClassHasItsOwnProfile()
        {
            foreach (EnvironmentClass environment in AllEnvironments)
                Assert.AreEqual(environment, CelestialLightingProfiles.ForEnvironment(environment).Environment);
        }

        [Test]
        public void EveryPlanetFamilyIsReachableFromItsResourceFamily()
        {
            var families = (ResourceFamily[])Enum.GetValues(typeof(ResourceFamily));
            var reached = families.Select(PlanetArchetypeCatalog.ForFamily).Distinct().ToArray();

            Assert.AreEqual(5, reached.Length, "all five resource families must map onto distinct archetypes");
            foreach (PlanetArchetype archetype in reached)
                Assert.AreEqual(archetype, CelestialLightingProfiles.ForPlanet(archetype).Archetype);
        }

        // ---- star character -------------------------------------------------

        [Test]
        public void StarClassesReadFromWarmAndDimToColdAndHard()
        {
            StarLightProfile red = CelestialLightingProfiles.ForStar(StarClass.RedDwarf);
            StarLightProfile yellow = CelestialLightingProfiles.ForStar(StarClass.Yellow);
            StarLightProfile white = CelestialLightingProfiles.ForStar(StarClass.White);
            StarLightProfile blue = CelestialLightingProfiles.ForStar(StarClass.BlueGiant);

            Assert.Greater(red.KeyLightColor.r, red.KeyLightColor.b, "a red dwarf must read warm");
            Assert.Greater(blue.KeyLightColor.b, blue.KeyLightColor.r, "a blue giant must read cold");
            Assert.AreEqual(white.KeyLightColor.r, white.KeyLightColor.g, 0.05f, "a white star must read neutral");

            Assert.Less(red.IntensityScale, yellow.IntensityScale, "a red dwarf is the dim one");
            Assert.Less(yellow.IntensityScale, white.IntensityScale);
            Assert.Less(white.IntensityScale, blue.IntensityScale, "a blue giant is the fierce one");

            Assert.Less(red.SpecularSharpness, blue.SpecularSharpness, "hard blue light throws a tighter highlight");
            Assert.Greater(red.HazardReadability, white.HazardReadability,
                "a danger zone must stay legible under a dim red sun");
        }

        [Test]
        public void NoStarProfileCanBlowSurfacesToWhite()
        {
            foreach (StarLightProfile profile in CelestialLightingProfiles.AllStars)
            {
                Assert.Greater(profile.IntensityScale, 0f, $"{profile.StarClass} would light nothing");
                Assert.LessOrEqual(profile.IntensityScale, 1.5f,
                    $"{profile.StarClass} intensity is into white-clipping territory");
                Assert.LessOrEqual(profile.KeyLightColor.maxColorComponent, 1f,
                    $"{profile.StarClass} key colour exceeds unity range");
                Assert.Greater(profile.RangeMultiplier, 0f);
            }
        }

        [Test]
        public void AStarsClassScalesBothTheReachAndTheIntensityTheManifestAskedFor()
        {
            const float manifestRange = 1000f;

            (Color redColor, float redIntensity, float redRange) =
                CelestialLightingResolver.ResolveStarKeyLight(StarClass.RedDwarf, manifestRange);
            (Color blueColor, float blueIntensity, float blueRange) =
                CelestialLightingResolver.ResolveStarKeyLight(StarClass.BlueGiant, manifestRange);

            Assert.Less(redRange, blueRange, "a blue giant lights its system much further out");
            Assert.Less(redIntensity, blueIntensity);
            Assert.AreNotEqual(redColor, blueColor);
        }

        // ---- planet families ------------------------------------------------

        [Test]
        public void MetalIsMetallicWithoutBecomingChrome()
        {
            PlanetSurfaceProfile metal = CelestialLightingProfiles.ForPlanet(PlanetArchetype.MetallicRocky);

            Assert.Greater(metal.Metallic, 0.3f, "metal must actually read as metal");
            Assert.Less(metal.Metallic, 0.8f, "full metallic reads as a chrome ball, not a world");
            Assert.Less(metal.Smoothness, 0.5f, "a mirror finish is the failure mode here");
        }

        [Test]
        public void WoodStaysWarmAndSoftSoReliefAndSilhouetteSurvive()
        {
            PlanetSurfaceProfile wood = CelestialLightingProfiles.ForPlanet(PlanetArchetype.Biomass);

            Assert.AreEqual(0f, wood.Metallic, 1e-4f, "an organic world is not metallic");
            Assert.Less(wood.Smoothness, 0.3f, "low specular keeps organic relief readable");
            Assert.GreaterOrEqual(wood.Emission.r, wood.Emission.b, "wood reads warm, never cold");
        }

        [Test]
        public void GasHasTheBroadestHighlightAndStaysOpaque()
        {
            PlanetSurfaceProfile gas = CelestialLightingProfiles.ForPlanet(PlanetArchetype.UpperAtmospherePlatform);
            PlanetSurfaceProfile water = CelestialLightingProfiles.ForPlanet(PlanetArchetype.FloatingOceanic);

            Assert.Less(gas.Smoothness, water.Smoothness,
                "lower smoothness is what makes a gas highlight broad and soft");
            Assert.AreEqual(0f, gas.Metallic, 1e-4f);
        }

        [Test]
        public void WaterReflectsBlueWithoutTurningToPlasticOrGlass()
        {
            PlanetSurfaceProfile water = CelestialLightingProfiles.ForPlanet(PlanetArchetype.FloatingOceanic);

            Assert.Greater(water.Smoothness, 0.4f, "water has to actually reflect");
            Assert.Less(water.Smoothness, CelestialLightingProfiles.MaxSurfaceSmoothness,
                "past this it reads as wet plastic");
            Assert.Greater(water.Emission.b, water.Emission.r, "water reads blue/cyan");
        }

        [Test]
        public void DiamondStaysBelowTheWhiteClippingCeiling_AndKeepsItsCyanLilacCharacter()
        {
            PlanetSurfaceProfile diamond = CelestialLightingProfiles.ForPlanet(PlanetArchetype.CrystalShell);

            Assert.LessOrEqual(diamond.Emission.maxColorComponent, CelestialLightingProfiles.MaxSafeEmissionLuminance,
                "this family's failure mode is becoming a white sheet");
            Assert.Greater(diamond.Emission.b, diamond.Emission.r,
                "diamond reads cyan-lilac, never white");
            Assert.LessOrEqual(diamond.Smoothness, CelestialLightingProfiles.MaxSurfaceSmoothness,
                "a sharp highlight, not a bare glass shell");
        }

        [Test]
        public void EveryFamilyAndEnvironmentCombinationStaysInsideTheSafeCeilings()
        {
            foreach (PlanetArchetype archetype in AllArchetypes)
                foreach (EnvironmentClass environment in AllEnvironments)
                    foreach (LightingQuality quality in (LightingQuality[])Enum.GetValues(typeof(LightingQuality)))
                    {
                        CelestialSurfaceResponse r = CelestialLightingResolver.Resolve(archetype, environment, quality);

                        Assert.LessOrEqual(r.Emission.maxColorComponent,
                            CelestialLightingProfiles.MaxSafeEmissionLuminance + 1e-4f,
                            $"{archetype}/{environment}/{quality} clips to white");
                        Assert.LessOrEqual(r.Smoothness, CelestialLightingProfiles.MaxSurfaceSmoothness + 1e-4f,
                            $"{archetype}/{environment}/{quality} is too glossy");
                        Assert.GreaterOrEqual(r.Smoothness, 0f);
                        Assert.That(r.Metallic, Is.InRange(0f, 1f));
                    }
        }

        [Test]
        public void EveryFamilyKeepsAReadableDarkSideWithoutASecondFillLight()
        {
            foreach (PlanetSurfaceProfile planet in CelestialLightingProfiles.AllPlanets)
            {
                Assert.Greater(planet.DarkSideFloor, 0f,
                    $"{planet.Archetype}: the unlit side must not go completely unreadable");
                Assert.Less(planet.DarkSideFloor, 0.4f,
                    $"{planet.Archetype}: too much floor and the star side stops reading as lit");
            }
        }

        // ---- environments ---------------------------------------------------

        [Test]
        public void ContrastRisesFromCalmThroughHarshToExtreme()
        {
            EnvironmentLightProfile calm = CelestialLightingProfiles.ForEnvironment(EnvironmentClass.Calm);
            EnvironmentLightProfile harsh = CelestialLightingProfiles.ForEnvironment(EnvironmentClass.Harsh);
            EnvironmentLightProfile extreme = CelestialLightingProfiles.ForEnvironment(EnvironmentClass.Extreme);

            Assert.Less(calm.ContrastScale, harsh.ContrastScale);
            Assert.Less(harsh.ContrastScale, extreme.ContrastScale);

            Assert.AreEqual(0f, calm.HazardRim, 1e-4f, "a calm world has no hazard to rim");
            Assert.Less(harsh.HazardRim, extreme.HazardRim);
        }

        [Test]
        public void AnEnvironmentTintNeverHidesTheFamilysOwnColour()
        {
            foreach (EnvironmentLightProfile env in CelestialLightingProfiles.AllEnvironments)
            {
                float weakestChannel = Mathf.Min(env.Tint.r, Mathf.Min(env.Tint.g, env.Tint.b));
                Assert.Greater(weakestChannel, 0.7f,
                    $"{env.Environment}: a tint this strong would mask the resource family's colour");
                Assert.LessOrEqual(env.Tint.maxColorComponent, 1f);
            }
        }

        [Test]
        public void FlashReductionSoftensTheHazardRimWithoutTouchingCalmWorlds()
        {
            CelestialSurfaceResponse extremeNormal =
                CelestialLightingResolver.Resolve(PlanetArchetype.MetallicRocky, EnvironmentClass.Extreme, LightingQuality.Medium);
            CelestialSurfaceResponse calmNormal =
                CelestialLightingResolver.Resolve(PlanetArchetype.MetallicRocky, EnvironmentClass.Calm, LightingQuality.Medium);

            LightingAccessibility.PhotosensitivitySafeMode = true;

            CelestialSurfaceResponse extremeSafe =
                CelestialLightingResolver.Resolve(PlanetArchetype.MetallicRocky, EnvironmentClass.Extreme, LightingQuality.Medium);
            CelestialSurfaceResponse calmSafe =
                CelestialLightingResolver.Resolve(PlanetArchetype.MetallicRocky, EnvironmentClass.Calm, LightingQuality.Medium);

            Assert.Less(extremeSafe.Emission.maxColorComponent, extremeNormal.Emission.maxColorComponent,
                "Safe Mode must pull the hazard rim down");
            Assert.AreEqual(calmNormal.Emission.maxColorComponent, calmSafe.Emission.maxColorComponent, 1e-4f,
                "a calm world has no hazard rim to soften");
        }

        // ---- determinism ------------------------------------------------------

        [Test]
        public void ResolvingTheSameBodyTwiceAlwaysGivesTheSameAnswer()
        {
            foreach (PlanetArchetype archetype in AllArchetypes)
                foreach (EnvironmentClass environment in AllEnvironments)
                {
                    CelestialSurfaceResponse first = CelestialLightingResolver.Resolve(archetype, environment, LightingQuality.Medium);
                    CelestialSurfaceResponse second = CelestialLightingResolver.Resolve(archetype, environment, LightingQuality.Medium);

                    Assert.AreEqual(first.Smoothness, second.Smoothness, 1e-6f, $"{archetype}/{environment}");
                    Assert.AreEqual(first.Metallic, second.Metallic, 1e-6f);
                    Assert.AreEqual(first.Emission, second.Emission);
                }
        }

        [Test]
        public void ClampEmissionPreservesHueRatherThanDraggingColoursTowardWhite()
        {
            var overBright = new Color(0.2f, 0.8f, 1f);
            Color clamped = CelestialLightingResolver.ClampEmission(overBright);

            Assert.LessOrEqual(clamped.maxColorComponent, CelestialLightingProfiles.MaxSafeEmissionLuminance + 1e-5f);
            Assert.AreEqual(overBright.r / overBright.b, clamped.r / clamped.b, 1e-4f,
                "clamping each channel separately would wash the colour toward white");
        }

        // ---- quality tiers -----------------------------------------------------

        [Test]
        public void LowIsTheSimplestResponseAndHighTheRichest()
        {
            CelestialSurfaceResponse low = CelestialLightingResolver.Resolve(PlanetArchetype.FloatingOceanic, EnvironmentClass.Extreme, LightingQuality.Low);
            CelestialSurfaceResponse medium = CelestialLightingResolver.Resolve(PlanetArchetype.FloatingOceanic, EnvironmentClass.Extreme, LightingQuality.Medium);
            CelestialSurfaceResponse high = CelestialLightingResolver.Resolve(PlanetArchetype.FloatingOceanic, EnvironmentClass.Extreme, LightingQuality.High);

            Assert.Less(low.Smoothness, medium.Smoothness, "Low must be the plainest material response");
            Assert.LessOrEqual(medium.Smoothness, high.Smoothness);
            Assert.Less(low.Emission.maxColorComponent, medium.Emission.maxColorComponent,
                "Low reduces hazard rim and character emission");
            Assert.AreEqual(low.Metallic, high.Metallic, 1e-6f,
                "quality changes how a surface is lit, never what it is made of");
        }

        // ---- presenter ----------------------------------------------------------

        [Test]
        public void ThePresenterWritesThroughAPropertyBlockAndNeverClonesAMaterial()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spawned.Add(go);
            var renderer = go.GetComponent<MeshRenderer>();
            Material sharedBefore = renderer.sharedMaterial;

            var presenter = go.AddComponent<CelestialSurfacePresenter>();
            presenter.Configure(PlanetArchetype.CrystalShell, EnvironmentClass.Harsh, LightingQuality.Medium);

            Assert.AreSame(sharedBefore, renderer.sharedMaterial,
                "touching renderer.material would clone the shared material once per planet");
            Assert.IsTrue(renderer.HasPropertyBlock(), "the response must be written through a property block");

            var readBack = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(readBack);
            Assert.AreEqual(presenter.AppliedResponse.Metallic, readBack.GetFloat("_Metallic"), 1e-4f);
        }

        [Test]
        public void ChangingQualityRewritesTheBlockWithoutAddingRenderersOrObjects()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spawned.Add(go);
            var presenter = go.AddComponent<CelestialSurfacePresenter>();
            presenter.Configure(PlanetArchetype.MetallicRocky, EnvironmentClass.Calm, LightingQuality.Low);

            int renderersBefore = go.GetComponentsInChildren<Renderer>(true).Length;
            int childrenBefore = go.GetComponentsInChildren<Transform>(true).Length;
            float smoothnessAtLow = presenter.AppliedResponse.Smoothness;

            presenter.SetQuality(LightingQuality.High);

            Assert.AreEqual(LightingQuality.High, presenter.Quality);
            Assert.Greater(presenter.AppliedResponse.Smoothness, smoothnessAtLow);
            Assert.AreEqual(renderersBefore, go.GetComponentsInChildren<Renderer>(true).Length);
            Assert.AreEqual(childrenBefore, go.GetComponentsInChildren<Transform>(true).Length,
                "a quality switch must not spawn a single GameObject");
        }

        [Test]
        public void ThePresenterAddsNoRealtimeLightToAnyCelestialBody()
        {
            foreach (PlanetArchetype archetype in AllArchetypes)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                spawned.Add(go);
                go.AddComponent<CelestialSurfacePresenter>()
                    .Configure(archetype, EnvironmentClass.Extreme, LightingQuality.High);

                Assert.AreEqual(0, go.GetComponentsInChildren<Light>(true).Length,
                    $"{archetype} must read through material response, never its own light");
            }
        }

        // ---- authorities this part was not allowed to move ------------------------

        [Test]
        public void CelestialLightingDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion,
                "LGT-02 is presentation only and must not touch world generation");
            Assert.AreEqual(3, ContentFingerprint.ContentRevision,
                "LGT-02 adds no content, so the content revision must stand still");
        }

        [Test]
        public void PresentationProfilesDidNotCopyOrOverrideGameplayCatalogValues()
        {
            // The gameplay meaning of these classes still comes from their own
            // catalogs; LGT-02 only added a presentation layer beside them.
            Assert.AreEqual(1f, EnvironmentClassCatalog.DurationMultiplier(EnvironmentClass.Calm), 1e-4f);
            Assert.AreEqual(1.15f, EnvironmentClassCatalog.DurationMultiplier(EnvironmentClass.Harsh), 1e-4f);
            Assert.AreEqual(1.30f, EnvironmentClassCatalog.DurationMultiplier(EnvironmentClass.Extreme), 1e-4f);
        }
    }
}
