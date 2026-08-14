using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// LGT-04: every real structure, drill family and POI type has a bounded
    /// lighting profile, the shared lifecycle resolver is complete, and the POI
    /// information gates never leak what the player has not earned.
    /// </summary>
    public class Lgt04StructurePoiLightingTests
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
            foreach (Material m in madeMaterials)
                if (m != null) UnityEngine.Object.DestroyImmediate(m);
            madeMaterials.Clear();
            LightingAccessibility.ResetToDefaults();
        }

        private static ResourceFamily[] AllFamilies => (ResourceFamily[])Enum.GetValues(typeof(ResourceFamily));
        private static SurfaceStructureType[] AllStructures => (SurfaceStructureType[])Enum.GetValues(typeof(SurfaceStructureType));
        private static DrillLifecycle[] AllLifecycles => (DrillLifecycle[])Enum.GetValues(typeof(DrillLifecycle));
        private static PoiType[] AllPoiTypes => (PoiType[])Enum.GetValues(typeof(PoiType));
        private static PoiKnowledgeLevel[] AllKnowledge => (PoiKnowledgeLevel[])Enum.GetValues(typeof(PoiKnowledgeLevel));
        private static LightingQuality[] AllQualities => (LightingQuality[])Enum.GetValues(typeof(LightingQuality));

        // ---- the real type sets --------------------------------------------

        [Test]
        public void TheGameHasFiveResourceFamiliesFiveStructureTypesAndFourPoiTypes()
        {
            Assert.AreEqual(5, AllFamilies.Length);
            Assert.AreEqual(5, AllStructures.Length);
            Assert.AreEqual(4, AllPoiTypes.Length);
            Assert.AreEqual(6, AllKnowledge.Length, "Unknown, Signal, Located, Identified, Active, Resolved");
            Assert.AreEqual(7, AllLifecycles.Length,
                "Planned, CapsuleInTransit, Constructing, Active, Damaged, Disabled, Dismantled");
        }

        // ---- shared lifecycle resolver --------------------------------------

        [Test]
        public void TheLifecycleResolverCoversEveryRealStateAndStaysBounded()
        {
            foreach (DrillLifecycle state in AllLifecycles)
            {
                (Color color, float strength) = StructureLightingProfiles.Lifecycle(state);
                Assert.That(strength, Is.InRange(0f, StructureLightingProfiles.MaxEmissionStrength), $"{state}");
                Assert.Greater(color.maxColorComponent, 0f, $"{state} has no colour");
            }

            Assert.AreEqual(0f, StructureLightingProfiles.Lifecycle(DrillLifecycle.Planned).strength, 1e-4f,
                "nothing has been built yet, so nothing glows");
            Assert.AreEqual(0f, StructureLightingProfiles.Lifecycle(DrillLifecycle.Dismantled).strength, 1e-4f);
        }

        [Test]
        public void DamagedAndDisabledAreTheOnlyAlarmStates()
        {
            foreach (DrillLifecycle state in AllLifecycles)
            {
                bool expected = state == DrillLifecycle.Damaged || state == DrillLifecycle.Disabled;
                Assert.AreEqual(expected, StructureLightingProfiles.IsAlarmState(state), $"{state}");
            }

            Assert.AreEqual(ShipLightingProfiles.Warning, StructureLightingProfiles.Lifecycle(DrillLifecycle.Damaged).color);
            Assert.AreEqual(ShipLightingProfiles.Emergency, StructureLightingProfiles.Lifecycle(DrillLifecycle.Disabled).color);
        }

        [Test]
        public void ADisabledStructureKeepsALowEmergencyGlowRatherThanVanishing()
        {
            float disabled = StructureLightingProfiles.Lifecycle(DrillLifecycle.Disabled).strength;

            Assert.Greater(disabled, 0f, "a dead structure must still be findable on a planet's dark side");
            Assert.Less(disabled, StructureLightingProfiles.Lifecycle(DrillLifecycle.Active).strength);
        }

        // ---- drills -----------------------------------------------------------

        [Test]
        public void EveryResourceFamilyHasItsOwnDrillColour()
        {
            var colors = AllFamilies.Select(StructureLightingProfiles.ResourceFamilyColor).ToArray();

            Assert.AreEqual(5, colors.Distinct().Count(), "five families must be told apart at a glance");
            foreach (Color color in colors) Assert.Greater(color.maxColorComponent, 0f);

            Color water = StructureLightingProfiles.ResourceFamilyColor(ResourceFamily.Water);
            Assert.Greater(water.b, water.r, "Water reads blue/cyan, as its world does");
            Color wood = StructureLightingProfiles.ResourceFamilyColor(ResourceFamily.Wood);
            Assert.Greater(wood.r, wood.b, "Wood reads warm, as its world does");
        }

        [Test]
        public void AnActiveDrillWearsItsFamilyColourAndTierOnlyBrightensIt()
        {
            foreach (ResourceFamily family in AllFamilies)
            {
                (Color t1Color, float t1) = StructureLightingProfiles.Drill(family, 1, DrillLifecycle.Active, false, false);
                (Color t2Color, float t2) = StructureLightingProfiles.Drill(family, 2, DrillLifecycle.Active, false, false);

                Assert.AreEqual(StructureLightingProfiles.ResourceFamilyColor(family), t1Color, $"{family}");
                Assert.AreEqual(t1Color, t2Color, $"{family}: T2 must not be a different colour");
                Assert.Greater(t2, t1, $"{family}: T2 reads as more lit panel");
            }
        }

        [Test]
        public void ADrillThatIsNotRunningFallsBackToTheSharedLifecycleLook()
        {
            foreach (DrillLifecycle state in AllLifecycles.Where(s => s != DrillLifecycle.Active))
            {
                (Color color, float strength) = StructureLightingProfiles.Drill(ResourceFamily.Diamond, 2, state, false, false);
                (Color expectedColor, float expectedStrength) = StructureLightingProfiles.Lifecycle(state);

                Assert.AreEqual(expectedColor, color, $"{state}");
                Assert.AreEqual(expectedStrength, strength, 1e-4f, $"{state}");
            }
        }

        [Test]
        public void AFullOrUpgradingDrillShowsSteadyAmberRatherThanItsFamilyColour()
        {
            (Color fullColor, float fullStrength) =
                StructureLightingProfiles.Drill(ResourceFamily.Metal, 1, DrillLifecycle.Active, storageFull: true, upgrading: false);
            Assert.AreEqual(ShipLightingProfiles.Industrial, fullColor, "a stalled drill reads as an amber warning");
            Assert.Greater(fullStrength, 0f);

            (Color upgradeColor, _) =
                StructureLightingProfiles.Drill(ResourceFamily.Metal, 1, DrillLifecycle.Active, storageFull: false, upgrading: true);
            Assert.Greater(upgradeColor.r, upgradeColor.b, "upgrade work light is amber-white");
        }

        [Test]
        public void CollectableDrillOutputIsVisibleWithoutBecomingABeacon()
        {
            foreach (ResourceFamily family in AllFamilies)
            {
                (Color color, float strength) = StructureLightingProfiles.DrillOutputReady(family);
                Assert.AreEqual(StructureLightingProfiles.ResourceFamilyColor(family), color);
                Assert.That(strength, Is.InRange(0.05f, 0.5f), $"{family}: limited showing only");
            }
        }

        // ---- surface structures --------------------------------------------------

        [Test]
        public void EveryStructureTypeHasItsOwnFunctionalColour()
        {
            var colors = AllStructures.Select(StructureLightingProfiles.StructureColor).ToArray();
            Assert.AreEqual(5, colors.Distinct().Count());

            Color command = StructureLightingProfiles.StructureColor(SurfaceStructureType.Command);
            Assert.Greater(command.b, command.r, "Command is white-cyan");

            Assert.AreEqual(ShipLightingProfiles.Defense,
                StructureLightingProfiles.StructureColor(SurfaceStructureType.SensorArray),
                "sensors share the ship's sensor cyan");
            Assert.AreEqual(ShipLightingProfiles.Engineering,
                StructureLightingProfiles.StructureColor(SurfaceStructureType.MaintenancePlatform),
                "maintenance shares the ship's engineering turquoise");
        }

        [Test]
        public void EveryStructureTypeAndTierAndStateStaysBounded()
        {
            foreach (SurfaceStructureType type in AllStructures)
                foreach (int tier in new[] { 1, 2 })
                    foreach (DrillLifecycle state in AllLifecycles)
                        foreach (bool ready in new[] { false, true })
                        {
                            (Color color, float strength) = StructureLightingProfiles.Structure(type, tier, state, ready);
                            Assert.That(strength, Is.InRange(0f, StructureLightingProfiles.MaxEmissionStrength),
                                $"{type}/T{tier}/{state}");
                            Assert.LessOrEqual(color.maxColorComponent, 1f);
                        }
        }

        [Test]
        public void TierTwoBrightensAStructureWithoutRepaintingIt()
        {
            foreach (SurfaceStructureType type in AllStructures)
            {
                (Color t1Color, float t1) = StructureLightingProfiles.Structure(type, 1, DrillLifecycle.Active, true);
                (Color t2Color, float t2) = StructureLightingProfiles.Structure(type, 2, DrillLifecycle.Active, true);

                Assert.AreEqual(t1Color, t2Color, $"{type}: T2 must not change colour");
                Assert.Greater(t2, t1, $"{type}: T2 must read as more capable");
            }
        }

        [Test]
        public void DefensiveStructuresSitLowOnStandbyAndRiseToAStaticReadyLevel()
        {
            foreach (SurfaceStructureType type in new[] { SurfaceStructureType.Laser, SurfaceStructureType.Plasma })
            {
                float standby = StructureLightingProfiles.Structure(type, 1, DrillLifecycle.Active, combatReady: false).strength;
                float ready = StructureLightingProfiles.Structure(type, 1, DrillLifecycle.Active, combatReady: true).strength;

                Assert.Less(standby, ready, $"{type}: ready must read brighter than standby");
                Assert.Greater(standby, 0f, $"{type}: standby is low, not off");
            }
        }

        [Test]
        public void ABlockedSensorOrMaintenancePlatformDimsWithoutGoingDark()
        {
            foreach (SurfaceStructureType type in new[] { SurfaceStructureType.SensorArray, SurfaceStructureType.MaintenancePlatform })
            {
                (Color _, float blocked) = StructureLightingProfiles.StructureBlocked(type);
                float active = StructureLightingProfiles.Structure(type, 1, DrillLifecycle.Active, false).strength;

                Assert.Greater(blocked, 0f, $"{type}: a blocked structure is still there");
                Assert.Less(blocked, active, $"{type}: blocked must read quieter than working");
            }
        }

        // ---- logistics center -------------------------------------------------------

        [Test]
        public void TheLogisticsCenterReadsBusierAsItFillsAndWarnsWhenItCannotMove()
        {
            var tier = default(LogisticsCenterTier);

            float idle = StructureLightingProfiles.Logistics(tier, LogisticsCenterState.Active, 0f, false, false, false).strength;
            float route = StructureLightingProfiles.Logistics(tier, LogisticsCenterState.Active, 0f, false, true, false).strength;
            float dock = StructureLightingProfiles.Logistics(tier, LogisticsCenterState.Active, 0f, false, false, true).strength;

            Assert.Less(idle, route, "an active route reads busier than an idle bay");
            Assert.Less(route, dock, "an occupied dock is the busiest of the three");

            (Color warnColor, _) = StructureLightingProfiles.Logistics(tier, LogisticsCenterState.Active, 0.85f, false, false, false);
            Assert.AreEqual(ShipLightingProfiles.Industrial, warnColor, "past 80% the centre reads as a problem");

            (Color fullColor, _) = StructureLightingProfiles.Logistics(tier, LogisticsCenterState.Active, 1f, false, false, false);
            Assert.AreEqual(ShipLightingProfiles.Warning, fullColor, "a full centre is a warning, not just busy");
        }

        [Test]
        public void EveryLogisticsStateStaysBounded()
        {
            foreach (LogisticsCenterState state in (LogisticsCenterState[])Enum.GetValues(typeof(LogisticsCenterState)))
                foreach (float fill in new[] { 0f, 0.5f, 0.85f, 1f })
                    foreach (bool upgrading in new[] { false, true })
                    {
                        (Color _, float strength) = StructureLightingProfiles.Logistics(
                            default, state, fill, upgrading, false, false);
                        Assert.That(strength, Is.InRange(0f, StructureLightingProfiles.MaxEmissionStrength),
                            $"{state}/fill {fill}/upgrading {upgrading}");
                    }

            Assert.AreEqual(0f,
                StructureLightingProfiles.Logistics(default, LogisticsCenterState.Dismantled, 0f, false, false, false).strength,
                1e-4f, "a dismantled centre is dark");
        }

        // ---- POI information gates ------------------------------------------------------

        [Test]
        public void AtSignalAndLocatedEveryPoiTypeLooksExactlyTheSame()
        {
            foreach (PoiKnowledgeLevel level in new[] { PoiKnowledgeLevel.Signal, PoiKnowledgeLevel.Located })
            {
                var results = AllPoiTypes.Select(t => StructureLightingProfiles.Poi(t, level)).ToArray();

                Assert.AreEqual(1, results.Select(r => r.color).Distinct().Count(),
                    $"{level}: colour must not reveal the POI type");
                Assert.AreEqual(1, results.Select(r => r.strength).Distinct().Count(),
                    $"{level}: brightness must not reveal the POI type either");
                Assert.AreEqual(StructureLightingProfiles.PoiContactColor, results[0].color);
            }
        }

        [Test]
        public void AnUnknownPoiEmitsNothingAtAll()
        {
            foreach (PoiType type in AllPoiTypes)
                Assert.AreEqual(0f, StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Unknown).strength, 1e-4f,
                    $"{type}: an undiscovered POI must be invisible");
        }

        [Test]
        public void OnlyIdentifiedOpensThePoiTypesRealProfile()
        {
            var identified = AllPoiTypes
                .Select(t => StructureLightingProfiles.Poi(t, PoiKnowledgeLevel.Identified).color)
                .ToArray();

            Assert.AreEqual(4, identified.Distinct().Count(),
                "once identified, each POI type must be readable as itself");
            foreach (Color color in identified)
                Assert.AreNotEqual(StructureLightingProfiles.PoiContactColor, color,
                    "an identified POI must stop looking like an abstract contact");
        }

        [Test]
        public void ContactVisibilityRisesAsThePlayerClosesInOnAnUnidentifiedPoi()
        {
            // Before identification every POI shares one ramp, and it only
            // rises: picking up a signal and then locating it must always make
            // the contact easier to see, whatever it turns out to be.
            foreach (PoiType type in AllPoiTypes)
            {
                float unknown = StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Unknown).strength;
                float signal = StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Signal).strength;
                float located = StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Located).strength;

                Assert.Less(unknown, signal, $"{type}");
                Assert.Less(signal, located, $"{type}");
            }
        }

        [Test]
        public void OnceIdentifiedAPoiIsBrightestWhileOperatingAndQuietestOnceSpent()
        {
            // Deliberately NOT a rule that identifying something brightens it.
            // A wreck identified as a hulk reads dimmer than the abstract
            // contact did, and that is the correct result: the game just told
            // the player it is scrap rather than a facility.
            foreach (PoiType type in AllPoiTypes)
            {
                float identified = StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Identified).strength;
                float active = StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Active).strength;
                float resolved = StructureLightingProfiles.Poi(type, PoiKnowledgeLevel.Resolved).strength;

                Assert.Greater(identified, 0f, $"{type}: an identified POI must still be findable");
                Assert.Less(identified, active, $"{type}: an operating POI is the brightest it gets");
                Assert.Less(resolved, active, $"{type}: a spent POI goes quiet");
            }
        }

        [Test]
        public void EveryPoiTypeAndKnowledgeLevelStaysBounded()
        {
            foreach (PoiType type in AllPoiTypes)
                foreach (PoiKnowledgeLevel level in AllKnowledge)
                {
                    (Color color, float strength) = StructureLightingProfiles.Poi(type, level);
                    Assert.That(strength, Is.InRange(0f, StructureLightingProfiles.MaxEmissionStrength), $"{type}/{level}");
                    Assert.LessOrEqual(color.maxColorComponent, 1f, $"{type}/{level}");
                }
        }

        [Test]
        public void AWreckStaysDimEnoughToNeverReadAsAWorkingShip()
        {
            float wreck = StructureLightingProfiles.Poi(PoiType.Wreck, PoiKnowledgeLevel.Active).strength;
            float anomaly = StructureLightingProfiles.Poi(PoiType.Anomaly, PoiKnowledgeLevel.Active).strength;

            Assert.Less(wreck, anomaly, "a hulk is the dimmest thing in the sky");
            Assert.Less(wreck, 0.3f);
        }

        [Test]
        public void AnAbandonedFacilityShowsOnlyAFewEmergencyPanels()
        {
            (Color color, float strength) = StructureLightingProfiles.Poi(PoiType.AbandonedFacility, PoiKnowledgeLevel.Identified);

            Assert.Greater(color.r, color.b, "the surviving panels are amber");
            Assert.Less(strength, 0.35f, "most systems are dead");
        }

        [Test]
        public void APirateBaseUsesAHostilePaletteAndGoesDarkWhenResolved()
        {
            (Color standbyColor, float standby) = StructureLightingProfiles.PirateBaseStandby(resolved: false);
            (Color resolvedColor, float resolved) = StructureLightingProfiles.PirateBaseStandby(resolved: true);

            Assert.AreEqual(StructureLightingProfiles.PirateBaseColor, standbyColor);
            Assert.Greater(standbyColor.r, standbyColor.b, "hostile identity is dirty red-amber");
            Assert.AreNotEqual(ShipLightingProfiles.Warning, standbyColor,
                "an enemy must not be confused with the player's own damage warning");
            Assert.Less(resolved, standby, "a cleared base powers down");
            Assert.AreEqual(ShipLightingProfiles.Emergency, resolvedColor);
        }

        [Test]
        public void SafetyMarkerAndTrainingDummyAreDistinctAndNeitherLooksHostile()
        {
            (Color safety, float safetyStrength) = StructureLightingProfiles.SafetyMarker();
            (Color training, float trainingStrength) = StructureLightingProfiles.TrainingDummy();

            Assert.Greater(safety.b, safety.r, "the safe zone reads white-blue");
            Assert.Greater(training.r, training.b, "training gear reads neutral yellow-amber");
            Assert.AreNotEqual(StructureLightingProfiles.PirateBaseColor, training,
                "a dummy must never be mistaken for a real enemy");

            foreach (float strength in new[] { safetyStrength, trainingStrength })
                Assert.That(strength, Is.InRange(0.1f, 0.8f), "visible from a distance, never a flare");
        }

        // ---- resources and salvage ------------------------------------------------------------

        [Test]
        public void UndiscoveredContentEmitsNothing()
        {
            foreach (ResourceFamily family in AllFamilies)
                Assert.AreEqual(0f, StructureLightingProfiles.Pickup(family, discovered: false).strength, 1e-4f,
                    $"{family}: lighting must never leak content the player has not found");

            Assert.AreEqual(0f, StructureLightingProfiles.SalvageNode(discovered: false).strength, 1e-4f);
        }

        [Test]
        public void ACollectablePickupCarriesItsFamilyColourWithoutEncodingRarity()
        {
            foreach (ResourceFamily family in AllFamilies)
            {
                (Color color, float strength) = StructureLightingProfiles.Pickup(family, discovered: true);

                Assert.AreEqual(StructureLightingProfiles.ResourceFamilyColor(family), color, $"{family}");
                Assert.That(strength, Is.InRange(0.05f, 0.6f), $"{family}");
            }

            // Rarity is carried by the existing rarity icons. This part adds no
            // second colour code for it, so every rarity of the same family
            // looks identical here.
            var rarities = (Rarity[])Enum.GetValues(typeof(Rarity));
            Assert.AreEqual(5, rarities.Length);
        }

        // ---- quality and accessibility ------------------------------------------------------------

        [Test]
        public void LowTrimsEmissionAndTrimsWarningsHarder()
        {
            Color normalLow = StructureLightingProfiles.Resolve(ShipLightingProfiles.Industrial, 0.5f, LightingQuality.Low, false);
            Color normalMedium = StructureLightingProfiles.Resolve(ShipLightingProfiles.Industrial, 0.5f, LightingQuality.Medium, false);
            Color warningLow = StructureLightingProfiles.Resolve(ShipLightingProfiles.Warning, 0.5f, LightingQuality.Low, true);
            Color warningMedium = StructureLightingProfiles.Resolve(ShipLightingProfiles.Warning, 0.5f, LightingQuality.Medium, true);

            Assert.Less(normalLow.maxColorComponent, normalMedium.maxColorComponent);
            Assert.Less(warningLow.maxColorComponent, warningMedium.maxColorComponent);
            Assert.Less(
                warningLow.maxColorComponent / warningMedium.maxColorComponent,
                normalLow.maxColorComponent / normalMedium.maxColorComponent,
                "Low must cut hazard emission harder than ordinary emission");
        }

        [Test]
        public void EveryResolvedValueStaysInsideTheEmissionCeiling()
        {
            foreach (LightingQuality quality in AllQualities)
                foreach (float strength in new[] { 0f, 0.5f, 1f, 5f })
                    foreach (bool warning in new[] { false, true })
                    {
                        Color resolved = StructureLightingProfiles.Resolve(Color.white, strength, quality, warning);
                        Assert.LessOrEqual(resolved.maxColorComponent,
                            StructureLightingProfiles.MaxEmissionStrength + 1e-4f);
                    }
        }

        [Test]
        public void SafeModeSoftensWarningsWithoutErasingTheirColour()
        {
            Color normal = StructureLightingProfiles.Resolve(ShipLightingProfiles.Warning, 0.5f, LightingQuality.Medium, true);
            LightingAccessibility.PhotosensitivitySafeMode = true;
            Color safe = StructureLightingProfiles.Resolve(ShipLightingProfiles.Warning, 0.5f, LightingQuality.Medium, true);

            Assert.Less(safe.maxColorComponent, normal.maxColorComponent);
            Assert.Greater(safe.maxColorComponent, 0f, "the state must stay readable");
            Assert.Greater(safe.r, safe.b, "it is still recognisably a warning");
        }

        // ---- no lights, shared variants ------------------------------------------------------------

        [Test]
        public void NoStructureOrPoiPresenterEverCreatesALight()
        {
            var structureGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(structureGo);
            structureGo.AddComponent<StructureLightingPresenter>();

            var poiGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(poiGo);
            poiGo.AddComponent<PoiLightingPresenter>();

            Assert.AreEqual(0, structureGo.GetComponentsInChildren<Light>(true).Length);
            Assert.AreEqual(0, poiGo.GetComponentsInChildren<Light>(true).Length);
        }

        [Test]
        public void AHundredStructuresSharingOneMaterialProduceOneVariantAndNoLights()
        {
            Shader standard = Shader.Find("Standard");
            Assert.IsNotNull(standard);
            var shared = new Material(standard) { name = "Shared Structure Material" };
            madeMaterials.Add(shared);

            for (int i = 0; i < 100; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spawned.Add(go);
                go.GetComponent<MeshRenderer>().sharedMaterial = shared;
                go.AddComponent<StructureLightingPresenter>().RebuildRendererCache();
            }

            Assert.AreEqual(1, MaterialVariantCache.VariantCount,
                "a hundred structures must share one emissive variant");
            Assert.AreEqual(0, spawned.Sum(go => go.GetComponentsInChildren<Light>(true).Length),
                "and add no real-time light at all");
            Assert.IsFalse(shared.IsKeywordEnabled("_EMISSION"),
                "the shared material file must not be mutated at runtime");
        }

        // ---- preserved authorities ----------------------------------------------------------------

        [Test]
        public void StructureAndPoiLightingDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
