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
    /// ANI-04.2: the rule that keeps a shot's origin out of the animation's
    /// reach, and the contracts the runtime combat presenters are built on.
    /// </summary>
    public class Ani042RuntimeCombatPresenterTests
    {
        private GameObject scratch;

        [TearDown]
        public void Cleanup()
        {
            AnimationAccessibility.ResetToDefaults();
            if (scratch != null) UnityEngine.Object.DestroyImmediate(scratch);
            scratch = null;
        }

        // --- the gameplay / visual muzzle split ---------------------------------

        [Test]
        public void AStableAnchorParentedToTheMount_IsImmuneToWhatMovesInsideTheModel()
        {
            // This is the shape of the fix: the gameplay muzzle hangs off the
            // mount, the animated parts hang off the model.
            scratch = new GameObject("ani042-mount");
            Transform mount = scratch.transform;

            var model = new GameObject("model").transform;
            model.SetParent(mount, false);
            var gimbal = new GameObject("part.gimbal_yaw").transform;
            gimbal.SetParent(model, false);
            var authored = new GameObject("anchor.projectile.spawn").transform;
            authored.SetParent(gimbal, false);
            authored.localPosition = new Vector3(0f, 0f, 2f);

            var stable = new GameObject("gameplay.muzzle").transform;
            stable.SetParent(mount, false);
            stable.SetPositionAndRotation(authored.position, authored.rotation);

            Vector3 spawnBefore = stable.position;
            Quaternion aimBefore = stable.rotation;

            // Animate the model exactly as the gimbal presenter does.
            gimbal.localRotation = Quaternion.Euler(0f, 20f, 0f);
            model.localPosition = new Vector3(0f, 0f, -0.18f); // recoil

            Assert.AreNotEqual(spawnBefore, authored.position,
                "the visual muzzle is expected to move — that is the point of the animation");
            Assert.AreEqual(spawnBefore, stable.position, "the shot's origin must not have moved");
            Assert.AreEqual(aimBefore, stable.rotation, "nor its direction");
        }

        [Test]
        public void TheStableAnchorStillFollowsTheShipAndItsMount()
        {
            scratch = new GameObject("ani042-ship");
            Transform ship = scratch.transform;
            var mount = new GameObject("mount").transform;
            mount.SetParent(ship, false);
            mount.localPosition = new Vector3(1f, 0f, 0f);

            var stable = new GameObject("gameplay.muzzle").transform;
            stable.SetParent(mount, false);
            stable.localPosition = new Vector3(0f, 0f, 2f);

            Vector3 before = stable.position;
            ship.position += new Vector3(0f, 0f, 100f);

            Assert.AreEqual(before + new Vector3(0f, 0f, 100f), stable.position,
                "the muzzle moves with the ship — it is only the animation it ignores");
        }

        // --- recoil is presentation, never a gameplay offset ----------------------

        [Test]
        public void RecoilIsBoundedAndAlwaysReturns()
        {
            Assert.Greater(CombatAnimationProfiles.RecoilMetres, 0f);
            Assert.Less(CombatAnimationProfiles.RecoilMetres, 1f, "a weapon slides, it does not fall off");
            Assert.AreEqual(0f, CombatMechanicsProfiles.RecoilCurve01(
                CombatMechanicsProfiles.RecoilRecoverySeconds + 1f), 1e-4f);
        }

        [Test]
        public void MotionOff_LeavesNoRecoilOffsetBehind()
        {
            AnimationAccessibility.MotionOff = true;
            Assert.AreEqual(0f, AnimationAccessibility.MotionScale,
                "with motion off nothing decorative may be left displaced");
        }

        // --- projectile presenter contract ------------------------------------------

        [Test]
        public void EveryProjectileFamily_HasRealNodesToDrive()
        {
            foreach (ProjectileKind kind in Enum.GetValues(typeof(ProjectileKind)).Cast<ProjectileKind>())
            {
                CombatMechanicsProfiles.ProjectileNodes nodes = CombatMechanicsProfiles.NodesFor(kind);
                Assert.IsTrue(nodes.HasCore, $"{kind} must have a real core node");
                Assert.IsTrue(nodes.HasFins, $"{kind} must have real moving surfaces");
            }
        }

        [Test]
        public void OnlyALiveRoundIsAnimated_AResolvedOneIsDone()
        {
            foreach (ProjectileVisualPhase phase in Enum.GetValues(typeof(ProjectileVisualPhase)).Cast<ProjectileVisualPhase>())
            {
                bool live = CombatMechanicsProfiles.IsLive(phase);
                bool expectedLive = phase == ProjectileVisualPhase.Launch || phase == ProjectileVisualPhase.Flight;
                Assert.AreEqual(expectedLive, live, $"{phase}");
            }
        }

        [Test]
        public void AnInterceptedRound_NeverReadsAsAHit()
        {
            Assert.AreEqual(ProjectileVisualPhase.Intercepted,
                CombatMechanicsProfiles.PhaseOf(2f, 1f, resolved: true, hit: true, intercepted: true),
                "interception outranks whatever else the resolution said");
        }

        // --- turret presenter contract -------------------------------------------------

        [Test]
        public void EveryTurretTheGameHas_ResolvesToDriveableNodes()
        {
            var turrets = new[]
            {
                CombatMechanicsProfiles.LaserDefenseT1, CombatMechanicsProfiles.LaserDefenseT2,
                CombatMechanicsProfiles.PlasmaDefenseT1, CombatMechanicsProfiles.PlasmaDefenseT2,
                CombatMechanicsProfiles.PirateBaseTurret(true), CombatMechanicsProfiles.PirateBaseTurret(false)
            };

            foreach (CombatMechanicsProfiles.TurretNodes nodes in turrets)
            {
                Assert.IsTrue(nodes.HasYaw && nodes.HasPitch,
                    "a turret with no yaw or pitch would have nothing to present");
            }
        }

        [Test]
        public void ATurretWithoutARecoilNode_GetsNoInventedOne()
        {
            // Only LaserDefenseT2 has one; the rest must fall back to VFX alone.
            Assert.IsTrue(CombatMechanicsProfiles.LaserDefenseT2.HasRecoil);
            Assert.IsFalse(CombatMechanicsProfiles.PirateBaseTurret(true).HasRecoil);
            Assert.IsFalse(CombatMechanicsProfiles.PlasmaDefenseT2.HasRecoil);
        }

        // --- raid and wreckage presenter contracts -----------------------------------------

        [Test]
        public void OnlyWarningDraws_AndResolvedHandsEverythingBack()
        {
            foreach (RaidPhase phase in Enum.GetValues(typeof(RaidPhase)).Cast<RaidPhase>())
            {
                bool draws = CombatMechanicsProfiles.RaidVfx(phase) != null;
                Assert.AreEqual(phase == RaidPhase.Warning, draws, $"{phase}");
            }
            Assert.IsTrue(CombatMechanicsProfiles.RaidReleasesVisuals(RaidPhase.Resolved));
        }

        [Test]
        public void AWrecksDrift_SurvivesAReloadWithoutBeingSaved()
        {
            Vector3 axis = CombatMechanicsProfiles.WreckageDriftAxis("wreck-42");
            Assert.AreEqual(axis, CombatMechanicsProfiles.WreckageDriftAxis("wreck-42"));

            // Recomputed from age, never accumulated — so a demote and a promote
            // land on exactly the same angle.
            float at30 = CombatMechanicsProfiles.WreckageSpinDegrees("wreck-42", 30f);
            Assert.AreEqual(at30, CombatMechanicsProfiles.WreckageSpinDegrees("wreck-42", 30f), 1e-4f);
        }

        // --- versions -------------------------------------------------------------------------

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
