using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-04.1: the gimbal maths, the node adapters that drive legacy-named
    /// models without renaming them, and the rules that keep recoil, raids and
    /// wreckage honest.
    /// </summary>
    public class Ani041CombatMechanicsAnimationTests
    {
        private GameObject scratch;

        [TearDown]
        public void Cleanup()
        {
            AnimationAccessibility.ResetToDefaults();
            if (scratch != null) UnityEngine.Object.DestroyImmediate(scratch);
            scratch = null;
        }

        private Transform Mount(Vector3 position, Quaternion rotation)
        {
            scratch = new GameObject("ani041-mount");
            scratch.transform.SetPositionAndRotation(position, rotation);
            return scratch.transform;
        }

        // --- gimbal maths ------------------------------------------------------

        [Test]
        public void AMountAlreadyPointedAtItsTarget_NeedsNoSwing()
        {
            Transform mount = Mount(Vector3.zero, Quaternion.identity);
            Vector2 angles = CombatMechanicsProfiles.AimAngles(mount, Vector3.forward * 100f, 25f);

            Assert.AreEqual(0f, angles.x, 0.01f);
            Assert.AreEqual(0f, angles.y, 0.01f);
        }

        [Test]
        public void ATargetToTheRight_SwingsTheYawRight()
        {
            Transform mount = Mount(Vector3.zero, Quaternion.identity);
            Vector2 angles = CombatMechanicsProfiles.AimAngles(mount, new Vector3(50f, 0f, 100f), 45f);

            Assert.Greater(angles.x, 0f, "a target to starboard swings the mount to starboard");
            Assert.AreEqual(0f, angles.y, 0.01f, "a level target needs no pitch");
        }

        [Test]
        public void ATargetAbove_PitchesUp()
        {
            Transform mount = Mount(Vector3.zero, Quaternion.identity);
            Vector2 angles = CombatMechanicsProfiles.AimAngles(mount, new Vector3(0f, 50f, 100f), 45f);

            Assert.Less(angles.y, 0f, "pitching up is a negative Euler X in Unity");
            Assert.AreEqual(0f, angles.x, 0.01f);
        }

        [Test]
        public void TheGimbalNeverExceedsTheGamesOwnLimit()
        {
            Transform mount = Mount(Vector3.zero, Quaternion.identity);
            float limit = WeaponCatalog.LaserGimbalDegrees;

            // A target hard on the beam — far outside anything the weapon can reach.
            Vector2 angles = CombatMechanicsProfiles.AimAngles(mount, new Vector3(1000f, 1000f, 1f), limit);

            Assert.LessOrEqual(Mathf.Abs(angles.x), limit + 0.001f, "the clamp is a hard ceiling");
            Assert.LessOrEqual(Mathf.Abs(angles.y), limit + 0.001f);
        }

        [Test]
        public void TheVisualLimit_IsReadFromTheRealWeaponRule()
        {
            foreach (WeaponFamily family in Enum.GetValues(typeof(WeaponFamily)).Cast<WeaponFamily>())
                Assert.AreEqual(WeaponCatalog.LaserGimbalDegrees,
                    CombatMechanicsProfiles.VisualGimbalLimitDegrees(family),
                    $"{family}'s visual swing must be bounded by the game's own gimbal rule");
        }

        [Test]
        public void ANullMount_IsASafeNoOp()
            => Assert.AreEqual(Vector2.zero, CombatMechanicsProfiles.AimAngles(null, Vector3.one, 25f));

        // --- recoil -------------------------------------------------------------

        [Test]
        public void RecoilStartsFull_AndReturnsToTheAuthoredPose()
        {
            Assert.AreEqual(1f, CombatMechanicsProfiles.RecoilCurve01(0f), 1e-4f, "the shot is the peak");
            Assert.AreEqual(0f, CombatMechanicsProfiles.RecoilCurve01(
                CombatMechanicsProfiles.RecoilRecoverySeconds), 1e-4f, "it must come all the way back");
            Assert.AreEqual(0f, CombatMechanicsProfiles.RecoilCurve01(999f), 1e-4f);
        }

        [Test]
        public void RecoilDecaysMonotonically()
        {
            float previous = float.MaxValue;
            for (float t = 0f; t <= CombatMechanicsProfiles.RecoilRecoverySeconds; t += 0.02f)
            {
                float value = CombatMechanicsProfiles.RecoilCurve01(t);
                Assert.LessOrEqual(value, previous + 1e-5f, $"recoil must not bounce back at t={t}");
                previous = value;
            }
        }

        [Test]
        public void NoShipWeaponHasARecoilNode_SoNoneIsInvented()
        {
            foreach (WeaponFamily family in Enum.GetValues(typeof(WeaponFamily)).Cast<WeaponFamily>())
                Assert.IsFalse(CombatMechanicsProfiles.HasRecoilNode(family),
                    $"{family}'s model carries no recoil node; the audit says so");
        }

        // --- turret adapters ------------------------------------------------------

        [Test]
        public void OnlyLaserT2_HasTheGamesOneRealRecoilNode()
        {
            Assert.IsTrue(CombatMechanicsProfiles.LaserDefenseT2.HasRecoil);
            Assert.AreEqual("part.laser_lance_recoil", CombatMechanicsProfiles.LaserDefenseT2.Recoil);

            Assert.IsFalse(CombatMechanicsProfiles.LaserDefenseT1.HasRecoil);
            Assert.IsFalse(CombatMechanicsProfiles.PlasmaDefenseT1.HasRecoil);
            Assert.IsFalse(CombatMechanicsProfiles.PlasmaDefenseT2.HasRecoil);
        }

        [Test]
        public void EveryDefenseTurret_HasARealYawAndPitchNode()
        {
            var all = new[]
            {
                CombatMechanicsProfiles.LaserDefenseT1, CombatMechanicsProfiles.LaserDefenseT2,
                CombatMechanicsProfiles.PlasmaDefenseT1, CombatMechanicsProfiles.PlasmaDefenseT2
            };

            foreach (CombatMechanicsProfiles.TurretNodes nodes in all)
            {
                Assert.IsTrue(nodes.HasYaw, "every turret must have a real yaw node");
                Assert.IsTrue(nodes.HasPitch, "every turret must have a real pitch node");
            }
        }

        [Test]
        public void PlasmaT1_IsDrivenByItsOwnLegacyNames_NotRenamed()
        {
            // The names are real; addressing them is an adapter, not a gap, and
            // the model file is never touched.
            Assert.AreEqual("swivel-joint", CombatMechanicsProfiles.PlasmaDefenseT1.Yaw);
            Assert.AreEqual("turret-head", CombatMechanicsProfiles.PlasmaDefenseT1.Pitch);
            Assert.IsFalse(CombatMechanicsProfiles.PlasmaDefenseT1.Yaw.StartsWith("part."),
                "this model predates the convention and is used as it is");
        }

        [Test]
        public void DefenseTurretResolution_PicksTheRightModelPerFamilyAndTier()
        {
            Assert.AreEqual(CombatMechanicsProfiles.LaserDefenseT1.Yaw,
                CombatMechanicsProfiles.DefenseTurret("structure.defense.laser", 1).Yaw);
            Assert.AreEqual(CombatMechanicsProfiles.LaserDefenseT2.Recoil,
                CombatMechanicsProfiles.DefenseTurret("structure.defense.laser", 2).Recoil);
            Assert.AreEqual(CombatMechanicsProfiles.PlasmaDefenseT1.Yaw,
                CombatMechanicsProfiles.DefenseTurret("structure.defense.plasma", 1).Yaw);
            Assert.AreEqual(CombatMechanicsProfiles.PlasmaDefenseT2.Yaw,
                CombatMechanicsProfiles.DefenseTurret("structure.defense.plasma", 2).Yaw);
        }

        [Test]
        public void ThePirateBase_HasTwoDistinctRealTurrets()
        {
            CombatMechanicsProfiles.TurretNodes port = CombatMechanicsProfiles.PirateBaseTurret(port: true);
            CombatMechanicsProfiles.TurretNodes starboard = CombatMechanicsProfiles.PirateBaseTurret(port: false);

            Assert.AreEqual("part.laser_turret_port", port.Yaw);
            Assert.AreEqual("part.laser_yoke_port", port.Pitch);
            Assert.AreEqual("anchor.laser.port", port.MuzzleAnchor);
            Assert.AreNotEqual(port.Yaw, starboard.Yaw, "the two turrets are separate mechanisms");
        }

        // --- projectile adapters ----------------------------------------------------

        [Test]
        public void TheEmRound_UsesTheStandardConvention()
        {
            CombatMechanicsProfiles.ProjectileNodes nodes = CombatMechanicsProfiles.NodesFor(ProjectileKind.Em);

            Assert.AreEqual("part.field_gimbal", nodes.Core);
            Assert.AreEqual(4, nodes.Fins.Length, "the EM round has four guidance fins");
            Assert.IsFalse(CombatMechanicsProfiles.UsesLegacyNames(ProjectileKind.Em));
        }

        [Test]
        public void ThePlasmaRound_IsDrivenByItsOwnLegacyNames()
        {
            CombatMechanicsProfiles.ProjectileNodes nodes = CombatMechanicsProfiles.NodesFor(ProjectileKind.Plasma);

            Assert.AreEqual("plasma-core", nodes.Core);
            Assert.IsTrue(nodes.HasFins);
            Assert.IsTrue(CombatMechanicsProfiles.UsesLegacyNames(ProjectileKind.Plasma));
            Assert.IsTrue(CombatMechanicsProfiles.UsesLegacyNames(ProjectileKind.EnemyPlasma));
        }

        [Test]
        public void TheSiegeRound_HasItsFourBreachingJaws()
        {
            CombatMechanicsProfiles.ProjectileNodes nodes = CombatMechanicsProfiles.NodesFor(ProjectileKind.EnemySiege);
            Assert.AreEqual(4, nodes.Fins.Length);
            Assert.IsTrue(nodes.Fins.All(f => f.StartsWith("part.breach_jaw_")));
        }

        [Test]
        public void EveryProjectileKind_ResolvesToRealNodes()
        {
            foreach (ProjectileKind kind in Enum.GetValues(typeof(ProjectileKind)).Cast<ProjectileKind>())
            {
                CombatMechanicsProfiles.ProjectileNodes nodes = CombatMechanicsProfiles.NodesFor(kind);
                Assert.IsTrue(nodes.HasCore, $"{kind} must resolve to a real core node");
            }
        }

        [Test]
        public void AProjectilesPhase_IsReadFromItsOwnLife()
        {
            Assert.AreEqual(ProjectileVisualPhase.Launch,
                CombatMechanicsProfiles.PhaseOf(0.05f, 3f, false, false, false));
            Assert.AreEqual(ProjectileVisualPhase.Flight,
                CombatMechanicsProfiles.PhaseOf(1f, 2f, false, false, false));
            Assert.AreEqual(ProjectileVisualPhase.Impact,
                CombatMechanicsProfiles.PhaseOf(1f, 0f, resolved: true, hit: true, intercepted: false));
            Assert.AreEqual(ProjectileVisualPhase.Expired,
                CombatMechanicsProfiles.PhaseOf(1f, 0f, resolved: true, hit: false, intercepted: false));
            Assert.AreEqual(ProjectileVisualPhase.Intercepted,
                CombatMechanicsProfiles.PhaseOf(1f, 1f, resolved: false, hit: false, intercepted: true));
        }

        [Test]
        public void AResolvedRound_StopsBeingAnimated()
        {
            Assert.IsTrue(CombatMechanicsProfiles.IsLive(ProjectileVisualPhase.Launch));
            Assert.IsTrue(CombatMechanicsProfiles.IsLive(ProjectileVisualPhase.Flight));
            Assert.IsFalse(CombatMechanicsProfiles.IsLive(ProjectileVisualPhase.Impact));
            Assert.IsFalse(CombatMechanicsProfiles.IsLive(ProjectileVisualPhase.Intercepted));
            Assert.IsFalse(CombatMechanicsProfiles.IsLive(ProjectileVisualPhase.Expired));
        }

        // --- raid ---------------------------------------------------------------------

        [Test]
        public void AScheduledRaid_LeaksNothing()
        {
            Assert.IsNull(CombatMechanicsProfiles.RaidVfx(RaidPhase.Scheduled),
                "the player must not learn a raid is coming from an effect");
            Assert.IsTrue(CombatMechanicsProfiles.RaidIsSilent(RaidPhase.Scheduled));
        }

        [Test]
        public void OnlyTheWarningPhase_Draws()
        {
            foreach (RaidPhase phase in Enum.GetValues(typeof(RaidPhase)).Cast<RaidPhase>())
            {
                bool draws = CombatMechanicsProfiles.RaidVfx(phase) != null;
                Assert.AreEqual(phase == RaidPhase.Warning, draws, $"{phase} must{(phase == RaidPhase.Warning ? "" : " not")} draw");
            }

            Assert.AreEqual(NebulaCommonVfx.WarningPulse, CombatMechanicsProfiles.RaidVfx(RaidPhase.Warning).Value);
        }

        [Test]
        public void SpawningDrawsNothing_SoNoWarpEffectCanAppear()
            => Assert.IsTrue(CombatMechanicsProfiles.RaidIsSilent(RaidPhase.Spawning));

        [Test]
        public void AResolvedRaid_HandsItsVisualsBack()
        {
            Assert.IsTrue(CombatMechanicsProfiles.RaidReleasesVisuals(RaidPhase.Resolved));
            Assert.IsTrue(CombatMechanicsProfiles.RaidReleasesVisuals(RaidPhase.None));
            Assert.IsFalse(CombatMechanicsProfiles.RaidReleasesVisuals(RaidPhase.Active));
        }

        // --- wreckage --------------------------------------------------------------------

        [Test]
        public void AWreckAlwaysDriftsTheSameWay_WithoutASingleSavedByte()
        {
            Vector3 first = CombatMechanicsProfiles.WreckageDriftAxis("wreck-alpha");
            Vector3 again = CombatMechanicsProfiles.WreckageDriftAxis("wreck-alpha");

            Assert.AreEqual(first, again, "the same wreck must drift identically after a reload");
            Assert.AreEqual(1f, first.magnitude, 1e-4f, "the axis must be normalised");
            Assert.AreNotEqual(first, CombatMechanicsProfiles.WreckageDriftAxis("wreck-beta"),
                "different wrecks must not all tumble alike");
        }

        [Test]
        public void AWreckWithNoId_StillHasASafeAxis()
        {
            Assert.AreEqual(Vector3.up, CombatMechanicsProfiles.WreckageDriftAxis(null));
            Assert.AreEqual(Vector3.up, CombatMechanicsProfiles.WreckageDriftAxis(""));
        }

        [Test]
        public void WreckageSpin_IsAPureFunctionOfAge_SoNothingAccumulates()
        {
            float atTen = CombatMechanicsProfiles.WreckageSpinDegrees("wreck-alpha", 10f);
            float atTenAgain = CombatMechanicsProfiles.WreckageSpinDegrees("wreck-alpha", 10f);

            Assert.AreEqual(atTen, atTenAgain, 1e-4f, "recomputing must never drift");
            Assert.Greater(CombatMechanicsProfiles.WreckageSpinDegrees("wreck-alpha", 20f), atTen);
        }

        [Test]
        public void MotionOff_StopsTheWreckDead()
        {
            AnimationAccessibility.MotionOff = true;
            Assert.AreEqual(0f, CombatMechanicsProfiles.WreckageSpinDegrees("wreck-alpha", 100f));
        }

        [Test]
        public void ReducedMotion_SlowsTheTumbleWithoutStoppingIt()
        {
            float full = CombatMechanicsProfiles.WreckageSpinDegrees("wreck-alpha", 10f);
            AnimationAccessibility.ReducedMotion = true;
            float reduced = CombatMechanicsProfiles.WreckageSpinDegrees("wreck-alpha", 10f);

            Assert.Less(reduced, full);
            Assert.Greater(reduced, 0f);
        }

        // --- accessibility and quality --------------------------------------------------

        [Test]
        public void MotionOff_SnapsTheGimbalToWhereItReallyPoints()
        {
            AnimationAccessibility.MotionOff = true;
            Assert.IsTrue(CombatMechanicsProfiles.SnapsToTargetPose,
                "the mount must still point at the target — it just stops swinging there");
        }

        [Test]
        public void LowQuality_DampsSecondaryMotion_MediumAndHighDoNot()
        {
            float low = CombatMechanicsProfiles.SecondaryScale(AnimationQualityProfile.Low);
            float high = CombatMechanicsProfiles.SecondaryScale(AnimationQualityProfile.High);

            Assert.Less(low, high, "Low keeps the reads and drops the decoration");
            Assert.Greater(low, 0f, "Low still shows the basics");
            Assert.AreEqual(1f, high, 1e-4f);
        }

        // --- versions -----------------------------------------------------------------------

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
