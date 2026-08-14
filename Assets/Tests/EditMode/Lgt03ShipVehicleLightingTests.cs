using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Hangar;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// LGT-03: the emission-keyword fix, the ship's single-fill budget, and a
    /// lighting profile for every real vehicle in the catalog.
    /// </summary>
    public class Lgt03ShipVehicleLightingTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<Material> madeMaterials = new List<Material>();

        [TearDown]
        public void TearDown()
        {
            MaterialVariantCache.Clear();
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            foreach (Material material in madeMaterials)
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            madeMaterials.Clear();
            LightingAccessibility.ResetToDefaults();
        }

        private Material MakeStandard(bool emissionOn)
        {
            Shader shader = Shader.Find("Standard");
            Assert.IsNotNull(shader, "the Built-in Standard shader must be available");
            var material = new Material(shader) { name = emissionOn ? "Emissive Source" : "Plain Source" };
            if (emissionOn)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            madeMaterials.Add(material);
            return material;
        }

        // ---- MaterialVariantCache -------------------------------------------

        [Test]
        public void TheSameSourceMaterialAlwaysGetsTheSameVariant()
        {
            Material source = MakeStandard(emissionOn: false);

            Material first = MaterialVariantCache.GetEmissiveVariant(source);
            Material second = MaterialVariantCache.GetEmissiveVariant(source);

            Assert.AreSame(first, second, "a second ask must reuse the cached variant, never allocate another");
            Assert.AreEqual(1, MaterialVariantCache.VariantCount);
        }

        [Test]
        public void ManyRenderersSharingOneMaterialProduceExactlyOneVariant()
        {
            Material source = MakeStandard(emissionOn: false);

            for (int i = 0; i < 25; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spawned.Add(go);
                go.GetComponent<MeshRenderer>().sharedMaterial = source;
                MaterialVariantCache.EnsureEmissiveCapable(go.GetComponent<MeshRenderer>());
            }

            Assert.AreEqual(1, MaterialVariantCache.VariantCount,
                "variant count must track distinct source materials, never renderer count");
        }

        [Test]
        public void TheVariantNeverMutatesTheSourceMaterial()
        {
            Material source = MakeStandard(emissionOn: false);
            Assert.IsFalse(source.IsKeywordEnabled("_EMISSION"));

            Material variant = MaterialVariantCache.GetEmissiveVariant(source);

            Assert.AreNotSame(source, variant);
            Assert.IsFalse(source.IsKeywordEnabled("_EMISSION"),
                "the imported/shared material must come out of this untouched");
            Assert.IsTrue(variant.IsKeywordEnabled("_EMISSION"),
                "the whole point is that a property block's _EmissionColor becomes visible");
        }

        [Test]
        public void TheVariantKeepsTheSourcesShaderTexturesAndRenderQueue()
        {
            Material source = MakeStandard(emissionOn: false);
            var texture = new Texture2D(4, 4);
            source.mainTexture = texture;
            source.SetColor("_Color", new Color(0.3f, 0.4f, 0.5f));
            source.renderQueue = 2450;

            Material variant = MaterialVariantCache.GetEmissiveVariant(source);

            Assert.AreSame(source.shader, variant.shader);
            Assert.AreSame(texture, variant.mainTexture);
            Assert.AreEqual(source.GetColor("_Color"), variant.GetColor("_Color"));
            Assert.AreEqual(source.renderQueue, variant.renderQueue);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        [Test]
        public void AMaterialThatCanAlreadyShowEmissionGetsNoVariantAtAll()
        {
            Material alreadyEmissive = MakeStandard(emissionOn: true);

            Material result = MaterialVariantCache.GetEmissiveVariant(alreadyEmissive);

            Assert.AreSame(alreadyEmissive, result, "no copy is needed, so none must be made");
            Assert.AreEqual(0, MaterialVariantCache.VariantCount);
            Assert.IsTrue(MaterialVariantCache.AlreadyEmissive(alreadyEmissive));
        }

        [Test]
        public void AdoptingAVariantDoesNotChangeHowAnythingLooksUntilEmissionIsWritten()
        {
            Material source = MakeStandard(emissionOn: false);
            Material variant = MaterialVariantCache.GetEmissiveVariant(source);

            Assert.AreEqual(Color.black, variant.GetColor("_EmissionColor"),
                "the variant starts dark; the property block decides what glows");
        }

        [Test]
        public void SessionCleanupDestroysEveryRuntimeVariant()
        {
            MaterialVariantCache.GetEmissiveVariant(MakeStandard(emissionOn: false));
            MaterialVariantCache.GetEmissiveVariant(MakeStandard(emissionOn: false));
            Assert.AreEqual(2, MaterialVariantCache.VariantCount);

            MaterialVariantCache.Clear();

            Assert.AreEqual(0, MaterialVariantCache.VariantCount,
                "variants are session-lifetime and must not survive a restart");
        }

        [Test]
        public void EnsureEmissiveCapableSwapsSharedMaterialWithoutCloningPerRenderer()
        {
            Material source = MakeStandard(emissionOn: false);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(go);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = source;

            MaterialVariantCache.EnsureEmissiveCapable(renderer);

            Assert.AreNotSame(source, renderer.sharedMaterial);
            Assert.IsTrue(renderer.sharedMaterial.IsKeywordEnabled("_EMISSION"));
            Assert.AreEqual(1, MaterialVariantCache.VariantCount);
        }

        // ---- LGT-02 values must survive the keyword fix -------------------------

        [Test]
        public void TheEmissionFixDidNotMoveTheDiamondClampFromLgt02()
        {
            PlanetSurfaceProfile diamond = CelestialLightingProfiles.ForPlanet(PlanetArchetype.CrystalShell);

            Assert.AreEqual(0.35f, CelestialLightingProfiles.MaxSafeEmissionLuminance, 1e-4f);
            Assert.AreEqual(0.75f, CelestialLightingProfiles.MaxSurfaceSmoothness, 1e-4f);
            Assert.LessOrEqual(diamond.Emission.maxColorComponent, CelestialLightingProfiles.MaxSafeEmissionLuminance);
            Assert.Greater(diamond.Emission.b, diamond.Emission.r, "still cyan-lilac, never white");

            foreach (EnvironmentClass environment in (EnvironmentClass[])Enum.GetValues(typeof(EnvironmentClass)))
            {
                CelestialSurfaceResponse r = CelestialLightingResolver.Resolve(
                    PlanetArchetype.CrystalShell, environment, LightingQuality.High);
                Assert.LessOrEqual(r.Emission.maxColorComponent,
                    CelestialLightingProfiles.MaxSafeEmissionLuminance + 1e-4f,
                    $"Diamond/{environment} clips to white");
            }
        }

        // ---- vehicles -------------------------------------------------------------

        [Test]
        public void EveryRealVehicleInTheCatalogHasALightingProfile()
        {
            VehicleDef[] all = VehicleCatalog.All;
            Assert.AreEqual(7, all.Length,
                "Scout Mk I plus Collector/Engineer/Escort Mk I and Mk II — the real catalog");

            foreach (VehicleDef def in all)
            {
                (Color color, float strength) = ShipLightingProfiles.Vehicle(
                    def.Class, def.Mk, VehicleLightingState.MissionActive);

                Assert.Greater(strength, 0f, $"{def.Id} has no lighting");
                Assert.LessOrEqual(strength, ShipLightingProfiles.MaxEmissionStrength, $"{def.Id} is too bright");
                Assert.Greater(color.maxColorComponent, 0f, $"{def.Id} has no colour");
            }
        }

        [Test]
        public void TheFourVehicleClassesAreToldApartByColour()
        {
            var classes = (VehicleClass[])Enum.GetValues(typeof(VehicleClass));
            var colors = classes.Select(ShipLightingProfiles.VehicleClassColor).ToArray();

            Assert.AreEqual(4, classes.Length);
            Assert.AreEqual(4, colors.Distinct().Count(), "each class must be readable by colour alone");

            Color collector = ShipLightingProfiles.VehicleClassColor(VehicleClass.Collector);
            Assert.Greater(collector.r, collector.b, "Collector is industrial amber");

            Color scout = ShipLightingProfiles.VehicleClassColor(VehicleClass.Scout);
            Assert.Greater(scout.b, scout.r, "Scout is sensor cyan");

            Color engineer = ShipLightingProfiles.VehicleClassColor(VehicleClass.Engineer);
            Assert.Greater(engineer.g, engineer.r, "Engineer is turquoise");

            Color escort = ShipLightingProfiles.VehicleClassColor(VehicleClass.Escort);
            Assert.Greater(escort.b, escort.r, "Escort is cold defensive blue");
        }

        [Test]
        public void MkTwoAddsLitPanelWithoutChangingTheClassColour()
        {
            foreach (VehicleClass vehicleClass in (VehicleClass[])Enum.GetValues(typeof(VehicleClass)))
            {
                (Color mk1Color, float mk1) = ShipLightingProfiles.Vehicle(vehicleClass, 1, VehicleLightingState.MissionActive);
                (Color mk2Color, float mk2) = ShipLightingProfiles.Vehicle(vehicleClass, 2, VehicleLightingState.MissionActive);

                Assert.AreEqual(mk1Color, mk2Color, $"{vehicleClass} Mk II must not be a different visual language");
                Assert.Greater(mk2, mk1, $"{vehicleClass} Mk II should read as slightly more capable");
                Assert.Less(mk2 - mk1, 0.2f, $"{vehicleClass} Mk II must stay a controlled increment");
            }
        }

        [Test]
        public void EveryVehicleStateIsDistinctAndBounded()
        {
            var states = (VehicleLightingState[])Enum.GetValues(typeof(VehicleLightingState));

            foreach (VehicleClass vehicleClass in (VehicleClass[])Enum.GetValues(typeof(VehicleClass)))
            {
                var strengths = states
                    .Select(s => ShipLightingProfiles.Vehicle(vehicleClass, 1, s).strength)
                    .ToArray();

                foreach (float strength in strengths)
                    Assert.That(strength, Is.InRange(0f, ShipLightingProfiles.MaxEmissionStrength));

                float docked = ShipLightingProfiles.Vehicle(vehicleClass, 1, VehicleLightingState.Docked).strength;
                float onMission = ShipLightingProfiles.Vehicle(vehicleClass, 1, VehicleLightingState.MissionActive).strength;
                Assert.Less(docked, onMission, $"{vehicleClass}: a working vehicle must read brighter than a parked one");
            }
        }

        [Test]
        public void ADisabledVehicleDropsToEmergencyColourWhateverItsClass()
        {
            foreach (VehicleClass vehicleClass in (VehicleClass[])Enum.GetValues(typeof(VehicleClass)))
            {
                (Color color, float strength) = ShipLightingProfiles.Vehicle(vehicleClass, 2, VehicleLightingState.Disabled);

                Assert.AreEqual(ShipLightingProfiles.Emergency, color, $"{vehicleClass} disabled must read as emergency");
                Assert.Less(strength, 0.3f, "a dead vehicle is dim");
            }
        }

        [Test]
        public void AVehiclePresenterAsksForNoRealtimeLight()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(go);
            go.AddComponent<VehicleLightingPresenter>();

            Assert.AreEqual(0, go.GetComponentsInChildren<Light>(true).Length,
                "vehicles are lit by the global key light and their own emission, never by a light of their own");
        }

        // ---- ship states -------------------------------------------------------------

        [Test]
        public void EngineEmissionRisesWithThrottleAndPeaksUnderBoost()
        {
            float idle = ShipLightingProfiles.Engine(ShipLightingState.Idle, 0f).strength;
            float lowThrottle = ShipLightingProfiles.Engine(ShipLightingState.Throttle, 0.2f).strength;
            float fullThrottle = ShipLightingProfiles.Engine(ShipLightingState.Throttle, 1f).strength;
            float boost = ShipLightingProfiles.Engine(ShipLightingState.Boost, 1f).strength;

            Assert.Less(idle, lowThrottle);
            Assert.Less(lowThrottle, fullThrottle);
            Assert.Greater(boost, fullThrottle);
            Assert.LessOrEqual(boost, ShipLightingProfiles.MaxEmissionStrength, "boost is clamped, not unbounded");
        }

        [Test]
        public void CruiseIsSteadyAndDoesNotTrackTheThrottle()
        {
            float atZero = ShipLightingProfiles.Engine(ShipLightingState.Cruise, 0f).strength;
            float atFull = ShipLightingProfiles.Engine(ShipLightingState.Cruise, 1f).strength;

            Assert.AreEqual(atZero, atFull, 1e-4f, "long-haul cruise light is stable by definition");
            Assert.Greater(atZero, 0f);
        }

        [Test]
        public void DisabledCutsMainPowerButKeepsLimitedEmergencyLighting()
        {
            Assert.AreEqual(0f, ShipLightingProfiles.Engine(ShipLightingState.Disabled, 1f).strength, 1e-4f,
                "main power is gone, so the engines are dark");

            (Color cockpitColor, float cockpitStrength) = ShipLightingProfiles.Cockpit(ShipLightingState.Disabled);
            Assert.Greater(cockpitStrength, 0f, "limited emergency lighting must remain");
            Assert.AreEqual(ShipLightingProfiles.Emergency, cockpitColor);
        }

        [Test]
        public void DamagedAndRepairUseTheirOwnFunctionalColours()
        {
            Assert.AreEqual(ShipLightingProfiles.Warning, ShipLightingProfiles.SystemStatus(ShipLightingState.Damaged).color,
                "damage is the only red on the hull");
            Assert.AreEqual(ShipLightingProfiles.Engineering, ShipLightingProfiles.SystemStatus(ShipLightingState.Repair).color,
                "repair work reads turquoise/white");
        }

        [Test]
        public void EveryShipStateResolvesToABoundedEmission()
        {
            foreach (ShipLightingState state in (ShipLightingState[])Enum.GetValues(typeof(ShipLightingState)))
                foreach (float throttle in new[] { 0f, 0.5f, 1f })
                {
                    (Color engineColor, float engineStrength) = ShipLightingProfiles.Engine(state, throttle);
                    Assert.That(engineStrength, Is.InRange(0f, ShipLightingProfiles.MaxEmissionStrength), $"{state} engine");

                    Color resolved = ShipLightingProfiles.Resolve(engineColor, engineStrength, false);
                    Assert.LessOrEqual(resolved.maxColorComponent, ShipLightingProfiles.MaxEmissionStrength + 1e-4f);
                }
        }

        // ---- modules and hangar --------------------------------------------------------

        [Test]
        public void ALockedModuleIsDarkAndAnUpgradingOneShowsASteadyAmberWorkLight()
        {
            Assert.AreEqual(0f,
                ShipLightingProfiles.Module(ModuleLightingState.Locked, ShipLightingProfiles.Energy, 1).strength, 1e-4f);

            (Color color, float strength) = ShipLightingProfiles.Module(ModuleLightingState.Upgrading, ShipLightingProfiles.Energy, 1);
            Assert.AreEqual(ShipLightingProfiles.Industrial, color, "construction work is amber");
            Assert.Greater(strength, 0f);
        }

        [Test]
        public void ARisingModuleTierBrightensThePanelRatherThanRepaintingIt()
        {
            (Color lowTierColor, float lowTier) = ShipLightingProfiles.Module(ModuleLightingState.Installed, ShipLightingProfiles.Defense, 1);
            (Color highTierColor, float highTier) = ShipLightingProfiles.Module(ModuleLightingState.Installed, ShipLightingProfiles.Defense, 5);

            Assert.AreEqual(lowTierColor, highTierColor, "progression must not become a different device");
            Assert.Greater(highTier, lowTier);
        }

        [Test]
        public void ModuleFunctionColoursComeFromTheRealAnchorIds()
        {
            Assert.AreEqual(ShipLightingProfiles.Industrial,
                ShipLightingProfiles.ModuleFunctionColor("anchor.industry.resource_processing"));
            Assert.AreEqual(ShipLightingProfiles.Defense,
                ShipLightingProfiles.ModuleFunctionColor("anchor.defense.shield_systems"));
            Assert.AreEqual(ShipLightingProfiles.Energy,
                ShipLightingProfiles.ModuleFunctionColor("anchor.nuclear.laboratory"));
        }

        [Test]
        public void EveryRealShipModuleAnchorResolvesToAFunctionalColour()
        {
            string[] anchors = Nebula.Ship.PlayerShipVisualController.LogicalModuleAnchors;
            Assert.AreEqual(12, anchors.Length, "the ship has twelve logical module anchors");

            foreach (string anchor in anchors)
                Assert.Greater(ShipLightingProfiles.ModuleFunctionColor(anchor).maxColorComponent, 0f,
                    $"{anchor} resolved to no colour");
        }

        [Test]
        public void HangarStatesAreDistinctAndUseTheIndustrialPalette()
        {
            Assert.AreEqual(ShipLightingProfiles.Industrial, ShipLightingProfiles.Hangar(HangarLightingState.Occupied).color);
            Assert.AreEqual(ShipLightingProfiles.Industrial, ShipLightingProfiles.Hangar(HangarLightingState.LaunchPreparation).color);
            Assert.AreEqual(ShipLightingProfiles.Warning, ShipLightingProfiles.Hangar(HangarLightingState.Unavailable).color);

            float available = ShipLightingProfiles.Hangar(HangarLightingState.DockAvailable).strength;
            float occupied = ShipLightingProfiles.Hangar(HangarLightingState.Occupied).strength;
            Assert.Less(available, occupied, "a bay in use reads brighter than an empty one");
        }

        // ---- quality and accessibility ---------------------------------------------------

        [Test]
        public void OnlyOneHullFillIsEverBudgetedAndLowDropsIt()
        {
            // The ship's fill is the single non-key real-time light the game
            // allows, and it must fit inside every profile's budget.
            foreach (LightingQuality quality in (LightingQuality[])Enum.GetValues(typeof(LightingQuality)))
            {
                LightingQualityProfile profile = LightingQualityProfile.Resolve(quality);
                const int keyLight = 1;
                const int hullFill = 1;
                Assert.LessOrEqual(keyLight + hullFill, profile.MaxRealtimeLights,
                    $"{quality}: key light plus hull fill must fit the real-time budget");
            }

            Assert.AreEqual(LightShadows.None, LightingRig.ShipHullFill.Shadows,
                "the fill never casts shadows — only the key light may, and only on High");
        }

        [Test]
        public void FlashReductionSoftensWarningAndBoostWithoutErasingTheColour()
        {
            Color normalWarning = ShipLightingProfiles.Resolve(ShipLightingProfiles.Warning, 0.55f, true);

            LightingAccessibility.FlashReduction = true;
            Color reducedWarning = ShipLightingProfiles.Resolve(ShipLightingProfiles.Warning, 0.55f, true);

            Assert.Less(reducedWarning.maxColorComponent, normalWarning.maxColorComponent);
            Assert.Greater(reducedWarning.maxColorComponent, 0f, "the state must stay readable");
            Assert.Greater(reducedWarning.r, reducedWarning.b, "it is still recognisably a warning colour");
        }

        [Test]
        public void SteadyShipLightIsNotTreatedAsAFlash()
        {
            Color before = ShipLightingProfiles.Resolve(ShipLightingProfiles.Energy, 0.4f, false);
            LightingAccessibility.FlashReduction = true;
            Color after = ShipLightingProfiles.Resolve(ShipLightingProfiles.Energy, 0.4f, false);

            Assert.AreEqual(before, after, "Flash Reduction is about flashes, not about steady running lights");
        }

        // ---- authorities -------------------------------------------------------------------

        [Test]
        public void ShipAndVehicleLightingDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
