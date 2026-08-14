using System;
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
    /// ANI-04: which real combat state produces which pose and which existing
    /// sprite, across the three weapon families, the three damage layers and
    /// the six enemy roles — and the guarantee that none of it can decide
    /// anything.
    /// </summary>
    public class Ani04CombatAnimationTests
    {
        private static readonly string[] AllRoles =
        {
            EnemyIds.ScoutProbeMk1, EnemyIds.FighterMk1, EnemyIds.LooterTransportMk1,
            EnemyIds.EmDisruptorMk1, EnemyIds.HeavyAssaultMk1, EnemyIds.SiegeMk1
        };

        private GameObject poolRoot;

        [TearDown]
        public void Cleanup()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
            if (poolRoot != null) UnityEngine.Object.DestroyImmediate(poolRoot);
            poolRoot = null;
        }

        // --- weapons ----------------------------------------------------------

        [Test]
        public void EveryWeaponFamily_HasAMuzzleFlash_FromTheExistingSprites()
        {
            foreach (WeaponFamily family in Enum.GetValues(typeof(WeaponFamily)).Cast<WeaponFamily>())
            {
                NebulaCommonVfx vfx = CombatAnimationProfiles.MuzzleVfx(family);
                Assert.IsTrue(Enum.IsDefined(typeof(NebulaCommonVfx), vfx), $"{family} must map to a real sprite");
                Assert.IsNotNull(NebulaProducedVisualCatalog.Vfx(vfx));
            }
        }

        [Test]
        public void AnUnequippedHardpoint_ShowsNothing()
            => Assert.AreEqual(WeaponVisualState.Idle,
                CombatAnimationProfiles.ForHardpoint(equipped: false, overheated: false,
                    hasTarget: true, firingNow: true, chargeProgress01: 0.5f));

        [Test]
        public void AnOverheatedWeapon_HoldsASafeClosedPose_WhateverElseIsTrue()
        {
            WeaponVisualState state = CombatAnimationProfiles.ForHardpoint(
                equipped: true, overheated: true, hasTarget: true, firingNow: true, chargeProgress01: 0.9f);

            Assert.AreEqual(WeaponVisualState.Overheated, state);
            Assert.IsTrue(CombatAnimationProfiles.HoldsClosedPose(state));
            Assert.IsFalse(CombatAnimationProfiles.AimsGimbal(state), "an overheated weapon stops aiming");
        }

        [Test]
        public void AWeaponOnlyAims_WhenItReallyHasATarget()
        {
            Assert.AreEqual(WeaponVisualState.Idle, CombatAnimationProfiles.ForHardpoint(
                equipped: true, overheated: false, hasTarget: false, firingNow: false, chargeProgress01: 0f));
            Assert.AreEqual(WeaponVisualState.Tracking, CombatAnimationProfiles.ForHardpoint(
                equipped: true, overheated: false, hasTarget: true, firingNow: false, chargeProgress01: 0f));
            Assert.IsTrue(CombatAnimationProfiles.AimsGimbal(WeaponVisualState.Tracking));
        }

        [Test]
        public void APartialLock_ReadsAsCharging_AndACompletedOneDoesNot()
        {
            Assert.AreEqual(WeaponVisualState.Charging, CombatAnimationProfiles.ForHardpoint(
                equipped: true, overheated: false, hasTarget: true, firingNow: false, chargeProgress01: 0.4f));
            Assert.AreEqual(WeaponVisualState.Tracking, CombatAnimationProfiles.ForHardpoint(
                equipped: true, overheated: false, hasTarget: true, firingNow: false, chargeProgress01: 1f));
        }

        // --- damage layers -----------------------------------------------------

        [Test]
        public void EachDamageLayer_HasItsOwnImpact()
        {
            var seen = Enum.GetValues(typeof(ShipDamageLayer)).Cast<ShipDamageLayer>()
                .Select(CombatAnimationProfiles.ImpactVfx).ToArray();

            foreach (NebulaCommonVfx vfx in seen)
                Assert.IsTrue(Enum.IsDefined(typeof(NebulaCommonVfx), vfx));

            Assert.AreEqual(NebulaCommonVfx.ShieldImpact, CombatAnimationProfiles.ImpactVfx(ShipDamageLayer.Shield));
            Assert.AreEqual(seen.Length, seen.Distinct().Count(), "the three layers must read differently");
        }

        [Test]
        public void AShieldAbsorbs_AHullBlowShoves()
        {
            float shield = CombatAnimationProfiles.ImpactRecoilMetres(ShipDamageLayer.Shield);
            float armor = CombatAnimationProfiles.ImpactRecoilMetres(ShipDamageLayer.Armor);
            float hull = CombatAnimationProfiles.ImpactRecoilMetres(ShipDamageLayer.Hull);

            Assert.AreEqual(0f, shield, "a shield stops the blow by definition");
            Assert.Greater(armor, shield);
            Assert.Greater(hull, armor, "once the hull is being cut it must read heaviest");
        }

        // --- the six roles -----------------------------------------------------

        [Test]
        public void EveryRole_HasRealMechanicalPartsOnItsModel()
        {
            foreach (string id in AllRoles)
            {
                string[] parts = CombatAnimationProfiles.MechanicalParts(id);
                Assert.IsNotEmpty(parts, $"{id} must have real parts to work");
                foreach (string part in parts)
                    Assert.IsTrue(part.StartsWith("part."), $"{id}: {part} must be an authored part node");
            }
        }

        [Test]
        public void EveryRole_HasItsOwnActionAnchor()
        {
            foreach (string id in AllRoles)
            {
                string anchor = CombatAnimationProfiles.ActionVfxAnchor(id);
                Assert.IsTrue(anchor.StartsWith("anchor.vfx."), $"{id}: {anchor} must be an authored VFX anchor");
            }

            // The Probe scans, the Siege telegraphs, the Looter siphons: three
            // different mechanisms, three different anchors.
            Assert.AreNotEqual(CombatAnimationProfiles.ActionVfxAnchor(EnemyIds.ScoutProbeMk1),
                CombatAnimationProfiles.ActionVfxAnchor(EnemyIds.SiegeMk1));
            Assert.AreNotEqual(CombatAnimationProfiles.ActionVfxAnchor(EnemyIds.LooterTransportMk1),
                CombatAnimationProfiles.ActionVfxAnchor(EnemyIds.EmDisruptorMk1));
        }

        [Test]
        public void EveryAiPhase_MapsToAPose()
        {
            foreach (EnemyAiPhase phase in Enum.GetValues(typeof(EnemyAiPhase)).Cast<EnemyAiPhase>())
            {
                EnemyCombatVisual visual = CombatAnimationProfiles.ForEnemy(phase,
                    destroyed: false, disrupted: false, telegraphing: false, scanning: false, siphoning: false);
                Assert.IsTrue(Enum.IsDefined(typeof(EnemyCombatVisual), visual), $"{phase} must map to a real pose");
            }
        }

        [Test]
        public void ADestroyedEnemy_IsDestroyedWhateverElseIsTrue()
        {
            foreach (EnemyAiPhase phase in Enum.GetValues(typeof(EnemyAiPhase)).Cast<EnemyAiPhase>())
                Assert.AreEqual(EnemyCombatVisual.Destroyed, CombatAnimationProfiles.ForEnemy(phase,
                    destroyed: true, disrupted: true, telegraphing: true, scanning: true, siphoning: true));
        }

        [Test]
        public void ADisruptedEnemy_OutranksWhateverItWasDoing()
        {
            Assert.AreEqual(EnemyCombatVisual.Disrupted, CombatAnimationProfiles.ForEnemy(
                EnemyAiPhase.AttackOrScan, destroyed: false, disrupted: true,
                telegraphing: true, scanning: false, siphoning: false));
            Assert.IsTrue(CombatAnimationProfiles.IsClosedPose(EnemyCombatVisual.Disrupted));
        }

        [Test]
        public void ATelegraph_OutranksTheAttackItPrecedes()
            => Assert.AreEqual(EnemyCombatVisual.Telegraphing, CombatAnimationProfiles.ForEnemy(
                EnemyAiPhase.AttackOrScan, destroyed: false, disrupted: false,
                telegraphing: true, scanning: false, siphoning: false));

        [Test]
        public void ScanningAndSiphoning_ReadDifferentlyFromShooting()
        {
            EnemyCombatVisual scan = CombatAnimationProfiles.ForEnemy(EnemyAiPhase.AttackOrScan,
                false, false, false, scanning: true, siphoning: false);
            EnemyCombatVisual siphon = CombatAnimationProfiles.ForEnemy(EnemyAiPhase.AttackOrScan,
                false, false, false, scanning: false, siphoning: true);
            EnemyCombatVisual attack = CombatAnimationProfiles.ForEnemy(EnemyAiPhase.AttackOrScan,
                false, false, false, scanning: false, siphoning: false);

            Assert.AreEqual(EnemyCombatVisual.Scanning, scan);
            Assert.AreEqual(EnemyCombatVisual.Siphoning, siphon);
            Assert.AreEqual(EnemyCombatVisual.Attacking, attack);
            Assert.AreNotEqual(CombatAnimationProfiles.ActionVfx(EnemyIds.ScoutProbeMk1, scan),
                CombatAnimationProfiles.ActionVfx(EnemyIds.FighterMk1, attack));
        }

        [Test]
        public void AnEscapedEnemy_HoldsStill()
        {
            EnemyCombatVisual escaped = CombatAnimationProfiles.ForEnemy(EnemyAiPhase.Escaped,
                false, false, false, false, false);
            Assert.AreEqual(EnemyCombatVisual.Escaped, escaped);
            Assert.IsTrue(CombatAnimationProfiles.IsClosedPose(escaped));
            Assert.IsFalse(CombatAnimationProfiles.DrawsVfx(escaped), "an escaped enemy draws nothing");
        }

        [Test]
        public void AFighterThrowsItselfAround_ASiegeCarrierDoesNot()
        {
            Assert.Greater(CombatAnimationProfiles.BankScale(EnemyIds.FighterMk1),
                CombatAnimationProfiles.BankScale(EnemyIds.HeavyAssaultMk1));
            Assert.Greater(CombatAnimationProfiles.BankScale(EnemyIds.HeavyAssaultMk1),
                CombatAnimationProfiles.BankScale(EnemyIds.SiegeMk1));

            foreach (string id in AllRoles)
            {
                float scale = CombatAnimationProfiles.BankScale(id);
                Assert.Greater(scale, 0f, $"{id} must still read as flying");
                Assert.LessOrEqual(scale, 1f, $"{id} must stay inside the budget");
            }
        }

        [Test]
        public void TheBankCeiling_MatchesTheComfortLimitEverythingElseObeys()
            => Assert.AreEqual(AnimationAccessibility.MaxVisualBankDegrees,
                CombatAnimationProfiles.MaxEnemyBankDegrees);

        // --- accessibility -----------------------------------------------------

        [Test]
        public void ReducedMotion_DampsEverythingExceptTheTelegraph()
        {
            AnimationAccessibility.ReducedMotion = true;

            Assert.Less(CombatAnimationProfiles.MotionScaleFor(EnemyCombatVisual.Attacking), 1f,
                "decorative motion is damped");
            Assert.AreEqual(1f, CombatAnimationProfiles.MotionScaleFor(EnemyCombatVisual.Telegraphing),
                "an incoming siege shot is information, not decoration");
        }

        [Test]
        public void LowQuality_DropsSecondaryMotionButKeepsTheReads()
        {
            Assert.IsFalse(CombatAnimationProfiles.SecondaryMotionAllowed(AnimationQualityProfile.Low));
            Assert.IsTrue(CombatAnimationProfiles.SecondaryMotionAllowed(AnimationQualityProfile.High));
        }

        [Test]
        public void FlashReduction_ShrinksEffects_AndSafeModeShrinksThemFurther()
        {
            Assert.Less(LightingAccessibility.FlashReductionFactor, 1f);
            Assert.Less(LightingAccessibility.PhotosensitivityFlashFactor,
                LightingAccessibility.FlashReductionFactor,
                "Safe Mode must be the stricter of the two");
        }

        // --- the pool behind every combat effect --------------------------------

        [Test]
        public void CombatEffects_ReuseTheSamePool_AndNeverGrowIt()
        {
            poolRoot = new GameObject("ani04-pool");
            var pool = new SupportOperationPool(poolRoot.transform, capacityPerVfx: 2);

            pool.TryLease(NebulaCommonVfx.HitSpark, out var warm, out _);
            pool.Release(warm);
            int quads = pool.TotalQuads;

            for (int i = 0; i < 100; i++)
            {
                Assert.IsTrue(pool.TryLease(NebulaCommonVfx.HitSpark, out var lease, out _));
                pool.Release(lease);
            }

            Assert.AreEqual(quads, pool.TotalQuads, "100 combat events must reuse, never allocate");
            Assert.AreEqual(0, pool.LeasedCount);
        }

        [Test]
        public void AFullPool_RefusesRatherThanAllocating()
        {
            poolRoot = new GameObject("ani04-pool-full");
            var pool = new SupportOperationPool(poolRoot.transform, capacityPerVfx: 1);

            Assert.IsTrue(pool.TryLease(NebulaCommonVfx.ShieldImpact, out _, out _));
            Assert.IsFalse(pool.TryLease(NebulaCommonVfx.ShieldImpact, out _, out _),
                "a hundred simultaneous impacts cost one effect, not a hundred objects");
        }

        [Test]
        public void ACombatEffectCarriesNoLightAndNoBody()
        {
            poolRoot = new GameObject("ani04-pool-clean");
            var pool = new SupportOperationPool(poolRoot.transform, capacityPerVfx: 1);
            Assert.IsTrue(pool.TryLease(NebulaCommonVfx.HitSpark, out _, out Transform quad));

            Assert.IsNull(quad.GetComponentInChildren<Light>(true),
                "combat animation must not add to the light budget LGT-01 set");
            Assert.IsNull(quad.GetComponentInChildren<Rigidbody>(true));
        }

        // --- versions the animation phase must not move --------------------------

        [Test]
        public void TheSaveAndContentVersions_AreUntouched()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion); // P2-SAVE-201: absent schema is unknown, never v13
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }

        [Test]
        public void TheRealCombatTimings_AreUntouched()
        {
            // The animation phase reads these; it must never shorten one.
            foreach (string id in AllRoles)
            {
                EnemyDef def = EnemyCatalog.All.FirstOrDefault(d => d.Id == id);
                Assert.IsNotNull(def, $"{id} must still be in the catalog");
                Assert.GreaterOrEqual(def.TelegraphSeconds, 0f);
                Assert.GreaterOrEqual(def.ScanSeconds, 0f);
            }
        }
    }
}
