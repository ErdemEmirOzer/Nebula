using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// LGT-05: every real weapon, damage layer, enemy role, raid phase and
    /// dominance state has a bounded lighting profile; the transient priority
    /// order is safe under pressure; and the whole LGT-01–05 stack is complete
    /// against the game's actual content.
    /// </summary>
    public class Lgt05CombatFinalLightingTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            MaterialVariantCache.Clear();
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            LightingAccessibility.ResetToDefaults();
        }

        private static WeaponFamily[] AllWeapons => (WeaponFamily[])Enum.GetValues(typeof(WeaponFamily));
        private static ShipDamageLayer[] AllLayers => (ShipDamageLayer[])Enum.GetValues(typeof(ShipDamageLayer));
        private static EnemyRole[] AllRoles => (EnemyRole[])Enum.GetValues(typeof(EnemyRole));
        private static EnemyAiPhase[] AllPhases => (EnemyAiPhase[])Enum.GetValues(typeof(EnemyAiPhase));
        private static RaidPhase[] AllRaidPhases => (RaidPhase[])Enum.GetValues(typeof(RaidPhase));
        private static DominanceState[] AllDominance => (DominanceState[])Enum.GetValues(typeof(DominanceState));
        private static LightingQuality[] AllQualities => (LightingQuality[])Enum.GetValues(typeof(LightingQuality));

        private TransientLightPool MakePool(int capacity)
        {
            var host = new GameObject("Pool Host");
            spawned.Add(host);
            return TransientLightPool.Create(host.transform, capacity);
        }

        // ---- the real content sets ------------------------------------------

        [Test]
        public void TheGameHasThreeWeaponFamiliesSixEnemyRolesSevenRaidPhasesAndFiveDominanceStates()
        {
            Assert.AreEqual(3, AllWeapons.Length);
            Assert.AreEqual(3, AllLayers.Length);
            Assert.AreEqual(6, AllRoles.Length);
            Assert.AreEqual(8, AllPhases.Length);
            Assert.AreEqual(7, AllRaidPhases.Length);
            Assert.AreEqual(5, AllDominance.Length);
            Assert.AreEqual(6, EnemyCatalog.All.Length, "six enemy definitions, one per role");
        }

        [Test]
        public void EveryEnemyDefinitionInTheCatalogResolvesToARoleColour()
        {
            foreach (EnemyDef def in EnemyCatalog.All)
                Assert.Greater(CombatLightingProfiles.EnemyRoleColor(def.Role).maxColorComponent, 0f,
                    $"{def.DisplayName} has no lighting colour");

            var roleColors = AllRoles.Select(CombatLightingProfiles.EnemyRoleColor).ToArray();
            Assert.AreEqual(6, roleColors.Distinct().Count(), "six roles must be told apart");
        }

        // ---- weapon flashes ---------------------------------------------------

        [Test]
        public void EveryWeaponFamilyHasABoundedMuzzleAndImpactFlash()
        {
            foreach (WeaponFamily family in AllWeapons)
            {
                CombatFlash muzzle = CombatLightingProfiles.Muzzle(family, TransientLightPriority.PlayerWeapon);
                CombatFlash impact = CombatLightingProfiles.Impact(family, TransientLightPriority.PlayerWeapon);

                Assert.IsTrue(CombatLightingProfiles.IsWithinCombatCeiling(muzzle), $"{family} muzzle");
                Assert.IsTrue(CombatLightingProfiles.IsWithinCombatCeiling(impact), $"{family} impact");
                Assert.Greater(impact.Range, muzzle.Range, $"{family}: an impact spreads wider than a muzzle");
            }
        }

        [Test]
        public void TheThreeWeaponFamiliesAreToldApartByColour()
        {
            var colors = AllWeapons.Select(CombatLightingProfiles.WeaponColor).ToArray();
            Assert.AreEqual(3, colors.Distinct().Count());

            Assert.Greater(CombatLightingProfiles.LaserColor.r, CombatLightingProfiles.LaserColor.b, "laser is hot");
            Assert.Greater(CombatLightingProfiles.EmColor.b, CombatLightingProfiles.EmColor.r, "EM is electric blue-violet");
            Assert.Greater(CombatLightingProfiles.PlasmaColor.b, CombatLightingProfiles.PlasmaColor.r, "plasma reads blue-violet");
        }

        [Test]
        public void NoCombatFlashIsLongEnoughToReadAsALampOrBrightEnoughToStrobe()
        {
            var everyFlash = new List<CombatFlash>();
            foreach (WeaponFamily family in AllWeapons)
            {
                everyFlash.Add(CombatLightingProfiles.Muzzle(family, TransientLightPriority.PlayerWeapon));
                everyFlash.Add(CombatLightingProfiles.Impact(family, TransientLightPriority.PlayerWeapon));
            }
            foreach (ShipDamageLayer layer in AllLayers)
                everyFlash.Add(CombatLightingProfiles.DamageHit(layer, TransientLightPriority.CriticalPlayerImpact));
            everyFlash.Add(CombatLightingProfiles.Explosion(TransientLightPriority.PlayerWeapon));

            foreach (CombatFlash flash in everyFlash)
            {
                Assert.IsTrue(CombatLightingProfiles.IsWithinCombatCeiling(flash));
                Assert.LessOrEqual(flash.Duration, CombatLightingProfiles.MaxFlashDuration);
                Assert.LessOrEqual(flash.Intensity, CombatLightingProfiles.MaxFlashIntensity);
            }
        }

        [Test]
        public void NoCombatFlashIsWhite_TheArtDirectionRulesOutAWhiteoutPeak()
        {
            foreach (WeaponFamily family in AllWeapons)
            {
                Color muzzle = CombatLightingProfiles.Muzzle(family, TransientLightPriority.PlayerWeapon).Color;
                Assert.Less(Mathf.Min(muzzle.r, Mathf.Min(muzzle.g, muzzle.b)), 0.95f,
                    $"{family} muzzle is effectively white");
            }

            Color explosion = CombatLightingProfiles.Explosion(TransientLightPriority.PlayerWeapon).Color;
            Assert.Less(explosion.b, explosion.r, "an explosion is warm, never a white screen-fill");
        }

        [Test]
        public void ProjectileKindsMapOntoTheWeaponPaletteTheyWereFiredFrom()
        {
            Assert.AreEqual(WeaponFamily.Em, CombatLightingBridge.FamilyOf(ProjectileKind.Em));
            Assert.AreEqual(WeaponFamily.Plasma, CombatLightingBridge.FamilyOf(ProjectileKind.Plasma));
            Assert.AreEqual(WeaponFamily.Plasma, CombatLightingBridge.FamilyOf(ProjectileKind.EnemyPlasma));
            Assert.AreEqual(WeaponFamily.Plasma, CombatLightingBridge.FamilyOf(ProjectileKind.EnemySiege));
        }

        // ---- damage layers ------------------------------------------------------

        [Test]
        public void ShieldArmourAndHullHitsAreVisuallyDistinct()
        {
            Color shield = CombatLightingProfiles.DamageHit(ShipDamageLayer.Shield, TransientLightPriority.CriticalPlayerImpact).Color;
            Color armor = CombatLightingProfiles.DamageHit(ShipDamageLayer.Armor, TransientLightPriority.CriticalPlayerImpact).Color;
            Color hull = CombatLightingProfiles.DamageHit(ShipDamageLayer.Hull, TransientLightPriority.CriticalPlayerImpact).Color;

            Assert.AreEqual(3, new[] { shield, armor, hull }.Distinct().Count());
            Assert.Greater(shield.b, shield.r, "a shield absorbing a hit reads cold, never like hull damage");
            Assert.Greater(armor.r, armor.b, "armour reads hot amber");
            Assert.Greater(hull.r, hull.g, "hull damage is the red one");
            Assert.Less(hull.g, armor.g, "hull is darker and more serious than armour");
        }

        // ---- enemy roles and phases -----------------------------------------------

        [Test]
        public void EveryRoleAndPhaseCombinationStaysBounded()
        {
            foreach (EnemyRole role in AllRoles)
                foreach (EnemyAiPhase phase in AllPhases)
                    foreach (bool disrupted in new[] { false, true })
                        foreach (bool disabled in new[] { false, true })
                        {
                            (Color color, float strength) = CombatLightingProfiles.Enemy(role, phase, disrupted, disabled);
                            Assert.That(strength, Is.InRange(0f, ShipLightingProfiles.MaxEmissionStrength),
                                $"{role}/{phase}/disrupted {disrupted}/disabled {disabled}");
                            Assert.LessOrEqual(color.maxColorComponent, 1f);
                        }
        }

        [Test]
        public void AnAttackingEnemyReadsBrighterThanARetreatingOne()
        {
            foreach (EnemyRole role in AllRoles)
            {
                float attacking = CombatLightingProfiles.Enemy(role, EnemyAiPhase.AttackOrScan, false, false).strength;
                float retreating = CombatLightingProfiles.Enemy(role, EnemyAiPhase.Retreat, false, false).strength;
                float escaped = CombatLightingProfiles.Enemy(role, EnemyAiPhase.Escaped, false, false).strength;

                Assert.Greater(attacking, retreating, $"{role}: an attack run must be the readable one");
                Assert.AreEqual(0f, escaped, 1e-4f, $"{role}: an escaped enemy is gone");
            }
        }

        [Test]
        public void DisruptedAndDisabledOverrideTheRoleColour()
        {
            foreach (EnemyRole role in AllRoles)
            {
                Assert.AreEqual(CombatLightingProfiles.EmColor,
                    CombatLightingProfiles.Enemy(role, EnemyAiPhase.AttackOrScan, disrupted: true, disabled: false).color,
                    $"{role}: EM disruption must read as EM on any hull");

                Assert.AreEqual(ShipLightingProfiles.Emergency,
                    CombatLightingProfiles.Enemy(role, EnemyAiPhase.AttackOrScan, disrupted: true, disabled: true).color,
                    $"{role}: disabled wins over disrupted");
            }
        }

        // ---- raid phases -------------------------------------------------------------

        [Test]
        public void AScheduledRaidIsInvisible_ThePlayerMustNotReadItOffTheLighting()
        {
            Assert.AreEqual(0f, CombatLightingProfiles.Raid(RaidPhase.None).strength, 1e-4f);
            Assert.AreEqual(0f, CombatLightingProfiles.Raid(RaidPhase.Scheduled).strength, 1e-4f,
                "a raid the player has not been warned about must not be visible");
            Assert.AreEqual(0f, CombatLightingProfiles.Raid(RaidPhase.Resolved).strength, 1e-4f,
                "a finished raid must clear its emergency state");
        }

        [Test]
        public void RaidIntensityRisesThroughWarningToActiveThenFalls()
        {
            float warning = CombatLightingProfiles.Raid(RaidPhase.Warning).strength;
            float spawning = CombatLightingProfiles.Raid(RaidPhase.Spawning).strength;
            float active = CombatLightingProfiles.Raid(RaidPhase.Active).strength;
            float cleanup = CombatLightingProfiles.Raid(RaidPhase.RetreatCleanup).strength;

            Assert.Less(warning, spawning);
            Assert.Less(spawning, active);
            Assert.Less(cleanup, active, "the pressure comes off during cleanup");
            Assert.Greater(cleanup, 0f);
        }

        // ---- dominance ------------------------------------------------------------------

        [Test]
        public void EveryDominanceStateHasItsOwnReadingAndUnownedShowsNothing()
        {
            Assert.AreEqual(0f, CombatLightingProfiles.Dominance(DominanceState.Unowned).strength, 1e-4f,
                "a system the player knows nothing about must not be revealed by its lighting");

            Color controlled = CombatLightingProfiles.Dominance(DominanceState.Controlled).color;
            Color contested = CombatLightingProfiles.Dominance(DominanceState.Contested).color;
            Color lost = CombatLightingProfiles.Dominance(DominanceState.Lost).color;

            Assert.Greater(controlled.b, controlled.r, "controlled reads friendly cyan-white");
            Assert.Greater(contested.r, contested.b, "contested reads amber");
            Assert.Greater(lost.r, lost.g, "lost reads red");
            Assert.Less(CombatLightingProfiles.Dominance(DominanceState.Lost).strength,
                CombatLightingProfiles.Dominance(DominanceState.Controlled).strength,
                "losing a world turns its operations off, it does not light them up");
        }

        [Test]
        public void OperationsReadAsRunningOnlyWhereThePlayerActuallyHasAFoothold()
        {
            Assert.IsFalse(CombatLightingProfiles.OperationsReadAsRunning(DominanceState.Unowned));
            Assert.IsFalse(CombatLightingProfiles.OperationsReadAsRunning(DominanceState.Lost));
            Assert.IsTrue(CombatLightingProfiles.OperationsReadAsRunning(DominanceState.Developing));
            Assert.IsTrue(CombatLightingProfiles.OperationsReadAsRunning(DominanceState.Controlled));
            Assert.IsTrue(CombatLightingProfiles.OperationsReadAsRunning(DominanceState.Contested));
        }

        // ---- transient priority and lease safety --------------------------------------------

        [Test]
        public void ThePriorityOrderPutsHitsOnThePlayerAboveEverythingElse()
        {
            Assert.Less((int)TransientLightPriority.Decorative, (int)TransientLightPriority.DistantCombat);
            Assert.Less((int)TransientLightPriority.DistantCombat, (int)TransientLightPriority.PlanetaryDefense);
            Assert.Less((int)TransientLightPriority.PlanetaryDefense, (int)TransientLightPriority.PlayerWeapon);
            Assert.Less((int)TransientLightPriority.PlayerWeapon, (int)TransientLightPriority.CriticalPlayerImpact);
        }

        [Test]
        public void AHitOnThePlayerNeverLosesItsLightToDecorationOrDistantCombat()
        {
            TransientLightPool pool = MakePool(2);

            // Fill with the lowest-value lights there are.
            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f,
                (int)TransientLightPriority.Decorative), out var decor);
            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f,
                (int)TransientLightPriority.DistantCombat), out _);

            bool granted = pool.TryAcquire(
                CombatLightingProfiles.DamageHit(ShipDamageLayer.Hull, TransientLightPriority.CriticalPlayerImpact)
                    .ToRequest(Vector3.zero, LightingQuality.Medium), out var critical);

            Assert.IsTrue(granted, "a hit landing on the player must always get its light");
            Assert.IsTrue(pool.IsOwner(critical));
            Assert.IsFalse(pool.IsOwner(decor), "the decorative light was displaced, as designed");
        }

        [Test]
        public void ADisplacedOwnerReleasingLateCannotSwitchOffTheNewOwnersLight()
        {
            TransientLightPool pool = MakePool(1);

            pool.TryAcquire(new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 10f,
                (int)TransientLightPriority.Decorative), out var oldLease);
            pool.TryAcquire(
                CombatLightingProfiles.Muzzle(WeaponFamily.Laser, TransientLightPriority.PlayerWeapon)
                    .ToRequest(Vector3.zero, LightingQuality.Medium), out var newLease);

            // The displaced system finally gets round to releasing its handle.
            pool.Release(oldLease);

            Assert.IsTrue(pool.IsOwner(newLease),
                "a stale release must not switch off the light that took its slot");
            Assert.AreEqual(1, pool.ActiveCount);
        }

        [Test]
        public void AFullPoolRefusesTheLeastImportantRequestWithoutGrowing()
        {
            TransientLightPool pool = MakePool(2);

            for (int i = 0; i < 2; i++)
                pool.TryAcquire(
                    CombatLightingProfiles.DamageHit(ShipDamageLayer.Hull, TransientLightPriority.CriticalPlayerImpact)
                        .ToRequest(Vector3.zero, LightingQuality.Medium), out _);

            bool granted = pool.TryAcquire(
                new TransientLightRequest(Vector3.zero, Color.white, 1f, 5f, 1f, (int)TransientLightPriority.Decorative),
                out var refused);

            Assert.IsFalse(granted, "gameplay must never depend on a light, so refusing is correct");
            Assert.IsFalse(refused.IsValid);
            Assert.AreEqual(2, pool.Capacity, "the pool never grows under pressure");
        }

        [Test]
        public void NoTransientLightEverCastsAShadow()
        {
            TransientLightPool pool = MakePool(3);
            pool.TryAcquire(CombatLightingProfiles.Explosion(TransientLightPriority.PlayerWeapon)
                .ToRequest(Vector3.zero, LightingQuality.High), out var handle);

            Assert.IsTrue(pool.TryGetLight(handle, out Light light));
            Assert.AreEqual(LightShadows.None, light.shadows,
                "a shadow-casting explosion is the one cost this pipeline cannot absorb");
            Assert.AreEqual(LightShadows.None, LightingRig.TransientEffect.Shadows);
        }

        // ---- quality and accessibility ------------------------------------------------------

        [Test]
        public void EveryFlashStaysBoundedAtEveryQualityTier()
        {
            foreach (LightingQuality quality in AllQualities)
                foreach (WeaponFamily family in AllWeapons)
                {
                    TransientLightRequest request = CombatLightingProfiles
                        .Impact(family, TransientLightPriority.PlayerWeapon)
                        .ToRequest(Vector3.zero, quality);

                    Assert.That(request.Intensity, Is.InRange(0f, CombatLightingProfiles.MaxFlashIntensity * 1.2f),
                        $"{family} at {quality}");
                    Assert.Greater(request.Duration, 0f);
                }
        }

        [Test]
        public void FlashReductionLowersThePeakWithoutMakingAnAttackUnreadable()
        {
            TransientLightRequest normal = CombatLightingProfiles
                .DamageHit(ShipDamageLayer.Hull, TransientLightPriority.CriticalPlayerImpact)
                .ToRequest(Vector3.zero, LightingQuality.Medium);

            LightingAccessibility.FlashReduction = true;
            TransientLightRequest reduced = CombatLightingProfiles
                .DamageHit(ShipDamageLayer.Hull, TransientLightPriority.CriticalPlayerImpact)
                .ToRequest(Vector3.zero, LightingQuality.Medium);

            Assert.Less(reduced.Intensity, normal.Intensity);
            Assert.Greater(reduced.Intensity, 0f, "the player must still see that they were hit");
            Assert.AreEqual(normal.Color, reduced.Color, "the colour still says which layer took it");
        }

        [Test]
        public void SafeModeStretchesShortFlashesSoRapidFireCannotStrobe()
        {
            CombatFlash muzzle = CombatLightingProfiles.Muzzle(WeaponFamily.Laser, TransientLightPriority.PlayerWeapon);
            float normalDuration = muzzle.ToRequest(Vector3.zero, LightingQuality.Medium).Duration;

            LightingAccessibility.PhotosensitivitySafeMode = true;
            TransientLightRequest safe = muzzle.ToRequest(Vector3.zero, LightingQuality.Medium);

            Assert.Greater(safe.Duration, normalDuration,
                "Safe Mode holds each flash long enough that repeats merge instead of strobing");
            Assert.Less(safe.Intensity, muzzle.Intensity, "and it lowers the peak");
        }

        [Test]
        public void LightIntensityScaleIsVisualOnlyAndCannotReachZero()
        {
            LightingAccessibility.LightIntensityScale = 0f; // clamped on write
            TransientLightRequest dimmed = CombatLightingProfiles
                .Impact(WeaponFamily.Plasma, TransientLightPriority.PlayerWeapon)
                .ToRequest(Vector3.zero, LightingQuality.Medium);

            Assert.Greater(dimmed.Intensity, 0f, "combat must stay legible at any accessibility setting");
            Assert.AreEqual(CombatLightingProfiles.Impact(WeaponFamily.Plasma, TransientLightPriority.PlayerWeapon).Range,
                dimmed.Range, 1e-4f, "accessibility changes brightness, never reach — reach is gameplay-adjacent");
        }

        // ---- map / world rig isolation --------------------------------------------------------

        [Test]
        public void MapAndPreviewLightsAreLayerCulledSoTheyCanNeverTouchTheWorld()
        {
            LightSpec preview = LightingRig.PreviewKey(30);

            Assert.AreNotEqual(-1, preview.CullingMask,
                "a preview/map light must never illuminate every layer");
            Assert.AreEqual(1 << 30, preview.CullingMask);

            // The world's own lights are the opposite: they light everything.
            Assert.AreEqual(-1, LightingRig.SystemKey(Color.white, 100f).CullingMask);
            Assert.AreEqual(-1, LightingRig.ShipHullFill.CullingMask);
            Assert.AreEqual(-1, LightingRig.TransientEffect.CullingMask);
        }

        // ---- whole-stack completeness -----------------------------------------------------------

        [Test]
        public void EveryLgtProfileTableCoversTheGamesRealContentWithNoGaps()
        {
            // LGT-02
            foreach (StarClass cls in (StarClass[])Enum.GetValues(typeof(StarClass)))
                Assert.AreEqual(cls, CelestialLightingProfiles.ForStar(cls).StarClass);
            foreach (PlanetArchetype archetype in (PlanetArchetype[])Enum.GetValues(typeof(PlanetArchetype)))
                Assert.AreEqual(archetype, CelestialLightingProfiles.ForPlanet(archetype).Archetype);
            foreach (EnvironmentClass env in (EnvironmentClass[])Enum.GetValues(typeof(EnvironmentClass)))
                Assert.AreEqual(env, CelestialLightingProfiles.ForEnvironment(env).Environment);

            // LGT-03
            foreach (Nebula.Hangar.VehicleDef def in Nebula.Hangar.VehicleCatalog.All)
                Assert.Greater(ShipLightingProfiles.Vehicle(def.Class, def.Mk, VehicleLightingState.MissionActive).strength, 0f,
                    $"{def.Id} has no lighting");
            foreach (ShipLightingState state in (ShipLightingState[])Enum.GetValues(typeof(ShipLightingState)))
                Assert.That(ShipLightingProfiles.Engine(state, 1f).strength,
                    Is.InRange(0f, ShipLightingProfiles.MaxEmissionStrength), $"{state}");

            // LGT-04
            foreach (DrillLifecycle lifecycle in (DrillLifecycle[])Enum.GetValues(typeof(DrillLifecycle)))
                Assert.That(StructureLightingProfiles.Lifecycle(lifecycle).strength,
                    Is.InRange(0f, StructureLightingProfiles.MaxEmissionStrength), $"{lifecycle}");
            foreach (SurfaceStructureType type in (SurfaceStructureType[])Enum.GetValues(typeof(SurfaceStructureType)))
                Assert.Greater(StructureLightingProfiles.StructureColor(type).maxColorComponent, 0f, $"{type}");
            foreach (Nebula.Poi.PoiType poi in (Nebula.Poi.PoiType[])Enum.GetValues(typeof(Nebula.Poi.PoiType)))
                Assert.That(StructureLightingProfiles.Poi(poi, Nebula.Poi.PoiKnowledgeLevel.Active).strength,
                    Is.InRange(0f, StructureLightingProfiles.MaxEmissionStrength), $"{poi}");
        }

        [Test]
        public void EveryQualityProfileStillLeavesRoomForTheKeyLightAndTheHullFill()
        {
            foreach (LightingQuality quality in AllQualities)
            {
                LightingQualityProfile profile = LightingQualityProfile.Resolve(quality);
                const int keyLight = 1;
                const int hullFill = 1;

                Assert.LessOrEqual(keyLight + hullFill + profile.TransientLightCapacity, profile.MaxRealtimeLights + 1,
                    $"{quality}: key + fill + pool must fit the declared real-time budget");
                Assert.LessOrEqual(profile.ShadowCasterBudget, 1, $"{quality}: at most one shadow caster, ever");
            }
        }

        [Test]
        public void CombatLightingDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
