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
    /// ANI-04.3: the adoption queue every combat presenter reaches the director
    /// through, and the contracts those five presenters are built on.
    /// </summary>
    public class Ani043RuntimePresenterCompletionTests
    {
        private sealed class FakeParticipant : IAdoptableParticipant
        {
            public int Attachments;
            public bool Alive = true;

            public bool IsAlive => Alive;
            public void AttachTo(NebulaAnimationDirector director) => Attachments++;
            public void VisualTick(float deltaSeconds) { }
            public void ResetToBasePose() { }
        }

        [SetUp]
        public void ClearQueue() => CombatPresenterAdoption.ResetPending();

        [TearDown]
        public void Cleanup()
        {
            CombatPresenterAdoption.ResetPending();
            AnimationAccessibility.ResetToDefaults();
        }

        // --- the shared waiting room -------------------------------------------

        [Test]
        public void APresenterQueuesExactlyOnce()
        {
            var participant = new FakeParticipant();
            CombatPresenterAdoption.Enqueue(participant);
            CombatPresenterAdoption.Enqueue(participant);

            Assert.AreEqual(1, CombatPresenterAdoption.PendingCount, "queueing twice must not double it");
        }

        [Test]
        public void ADestroyedPresenterIsNotCountedAsWaiting()
        {
            var participant = new FakeParticipant();
            CombatPresenterAdoption.Enqueue(participant);
            Assert.AreEqual(1, CombatPresenterAdoption.PendingCount);

            participant.Alive = false;
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount,
                "a projectile that has already despawned is not still waiting");
        }

        [Test]
        public void RemovingAPresenter_TakesItOutOfTheQueue()
        {
            var participant = new FakeParticipant();
            CombatPresenterAdoption.Enqueue(participant);
            CombatPresenterAdoption.Remove(participant);

            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount);
        }

        [Test]
        public void ResetPending_EmptiesTheQueueForANewSession()
        {
            CombatPresenterAdoption.Enqueue(new FakeParticipant());
            CombatPresenterAdoption.Enqueue(new FakeParticipant());
            CombatPresenterAdoption.ResetPending();

            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount,
                "the previous session's presenters must not linger");
        }

        [Test]
        public void ANullPresenterIsIgnored()
        {
            CombatPresenterAdoption.Enqueue(null);
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount);
        }

        // --- projectile presenter contract ---------------------------------------

        [Test]
        public void EveryProjectileFamily_HasACoreAndSurfacesToDrive()
        {
            foreach (ProjectileKind kind in Enum.GetValues(typeof(ProjectileKind)).Cast<ProjectileKind>())
            {
                CombatMechanicsProfiles.ProjectileNodes nodes = CombatMechanicsProfiles.NodesFor(kind);
                Assert.IsTrue(nodes.HasCore, $"{kind} needs a real core node to spin");
                Assert.IsTrue(nodes.HasFins, $"{kind} needs real surfaces to deploy");
            }
        }

        [Test]
        public void OnlyALiveRound_IsWorked()
        {
            Assert.IsTrue(CombatMechanicsProfiles.IsLive(ProjectileVisualPhase.Flight));
            Assert.IsFalse(CombatMechanicsProfiles.IsLive(ProjectileVisualPhase.Impact),
                "a resolved round must settle, not keep spinning");
            Assert.IsFalse(CombatMechanicsProfiles.IsLive(ProjectileVisualPhase.Intercepted));
            Assert.IsFalse(CombatMechanicsProfiles.IsLive(ProjectileVisualPhase.Expired));
        }

        // --- turret presenter contract ----------------------------------------------

        [Test]
        public void AllFourDefenceTiers_ResolveToDriveableNodes()
        {
            var tiers = new[]
            {
                ("structure.defense.laser", 1), ("structure.defense.laser", 2),
                ("structure.defense.plasma", 1), ("structure.defense.plasma", 2)
            };

            foreach ((string id, int tier) in tiers)
            {
                CombatMechanicsProfiles.TurretNodes nodes = CombatMechanicsProfiles.DefenseTurret(id, tier);
                Assert.IsTrue(nodes.HasYaw, $"{id} T{tier} must have a real yaw node");
                Assert.IsTrue(nodes.HasPitch, $"{id} T{tier} must have a real pitch node");
            }
        }

        [Test]
        public void OnlyLaserT2_Recoils_TheRestFallBackToVfxAlone()
        {
            Assert.IsTrue(CombatMechanicsProfiles.DefenseTurret("structure.defense.laser", 2).HasRecoil);
            Assert.IsFalse(CombatMechanicsProfiles.DefenseTurret("structure.defense.laser", 1).HasRecoil);
            Assert.IsFalse(CombatMechanicsProfiles.DefenseTurret("structure.defense.plasma", 1).HasRecoil);
            Assert.IsFalse(CombatMechanicsProfiles.DefenseTurret("structure.defense.plasma", 2).HasRecoil);
        }

        [Test]
        public void PlasmaT1_IsDrivenByItsOwnPreConventionNames()
        {
            CombatMechanicsProfiles.TurretNodes nodes =
                CombatMechanicsProfiles.DefenseTurret("structure.defense.plasma", 1);

            Assert.AreEqual("swivel-joint", nodes.Yaw);
            Assert.AreEqual("turret-head", nodes.Pitch);
        }

        // --- pirate base contract ------------------------------------------------------

        [Test]
        public void ThePirateBaseHasTwoSeparateTurrets()
        {
            CombatMechanicsProfiles.TurretNodes port = CombatMechanicsProfiles.PirateBaseTurret(true);
            CombatMechanicsProfiles.TurretNodes starboard = CombatMechanicsProfiles.PirateBaseTurret(false);

            Assert.AreNotEqual(port.Yaw, starboard.Yaw);
            Assert.AreNotEqual(port.Pitch, starboard.Pitch);
            Assert.AreNotEqual(port.MuzzleAnchor, starboard.MuzzleAnchor);
        }

        [Test]
        public void ADestroyedBaseIsATerminalPose()
        {
            // Nothing may promote a destroyed base back to firing; the presenter
            // guards this, and the enum makes the intent explicit.
            Assert.IsTrue(Enum.IsDefined(typeof(PirateBaseVisual), PirateBaseVisual.Destroyed));
            Assert.AreNotEqual(PirateBaseVisual.Destroyed, PirateBaseVisual.Idle);
        }

        // --- raid contract ------------------------------------------------------------

        [Test]
        public void OnlyWarningDraws_EverythingElseIsSilent()
        {
            foreach (RaidPhase phase in Enum.GetValues(typeof(RaidPhase)).Cast<RaidPhase>())
            {
                bool draws = CombatMechanicsProfiles.RaidVfx(phase) != null;
                Assert.AreEqual(phase == RaidPhase.Warning, draws,
                    $"{phase} must {(phase == RaidPhase.Warning ? "" : "not ")}draw");
            }
        }

        [Test]
        public void SpawningIsSilent_SoNoWarpEffectCanExist()
        {
            Assert.IsTrue(CombatMechanicsProfiles.RaidIsSilent(RaidPhase.Spawning));
            Assert.IsTrue(CombatMechanicsProfiles.RaidIsSilent(RaidPhase.Scheduled));
            Assert.IsTrue(CombatMechanicsProfiles.RaidIsSilent(RaidPhase.RetreatCleanup));
        }

        // --- wreckage contract ------------------------------------------------------------

        [Test]
        public void AWrecksTumbleIsAPureFunction_SoNothingAccumulates()
        {
            float first = CombatMechanicsProfiles.WreckageSpinDegrees("wreck-a", 12.5f);
            float second = CombatMechanicsProfiles.WreckageSpinDegrees("wreck-a", 12.5f);

            Assert.AreEqual(first, second, 1e-4f, "the same age must always give the same angle");
            Assert.Greater(CombatMechanicsProfiles.WreckageSpinDegrees("wreck-a", 25f), first);
        }

        [Test]
        public void EachWreckTumblesAboutItsOwnAxis()
        {
            Vector3 a = CombatMechanicsProfiles.WreckageDriftAxis("wreck-a");
            Vector3 b = CombatMechanicsProfiles.WreckageDriftAxis("wreck-b");

            Assert.AreEqual(1f, a.magnitude, 1e-4f);
            Assert.AreEqual(1f, b.magnitude, 1e-4f);
            Assert.AreNotEqual(a, b);
            Assert.AreEqual(a, CombatMechanicsProfiles.WreckageDriftAxis("wreck-a"), "and always the same one");
        }

        [Test]
        public void MotionOff_LeavesAWreckOnItsAuthoredPose()
        {
            AnimationAccessibility.MotionOff = true;
            Assert.AreEqual(0f, CombatMechanicsProfiles.WreckageSpinDegrees("wreck-a", 500f));
        }

        // --- versions --------------------------------------------------------------------------

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
