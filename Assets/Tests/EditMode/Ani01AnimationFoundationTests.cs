using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-01: the animation foundation — registration safety, pose safety,
    /// quality and accessibility, and the guarantee that ten thousand ticks
    /// leave a transform exactly where it started.
    /// </summary>
    public class Ani01AnimationFoundationTests
    {
        /// <summary>A participant that records what it was asked to do and poses one transform additively.</summary>
        private sealed class ProbeParticipant : IVisualAnimationParticipant
        {
            private PoseAnchor anchor;
            public int Ticks;
            public int Resets;
            public bool Dead;
            public Action<float> OnTick;

            public ProbeParticipant(Transform target = null) => anchor = PoseAnchor.Capture(target);

            public bool IsAlive => !Dead;

            public void VisualTick(float deltaSeconds)
            {
                Ticks++;
                OnTick?.Invoke(deltaSeconds);
                // Always base + offset, never current + offset.
                anchor.Apply(new Vector3(0.01f, 0f, 0f), Quaternion.Euler(0f, 3f, 0f));
            }

            public void ResetToBasePose()
            {
                Resets++;
                anchor.Reset();
            }

            public bool IsAtBasePose() => anchor.IsAtBasePose();
        }

        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            AnimationAccessibility.ResetToDefaults();
        }

        private NebulaAnimationDirector MakeDirector(AnimationQualityProfile profile)
        {
            var go = new GameObject("Nebula Animation");
            spawned.Add(go);
            var director = go.AddComponent<NebulaAnimationDirector>();
            director.Initialize(profile);
            return director;
        }

        private Transform MakeTarget(string name, Vector3 position)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(5f, 10f, 15f);
            go.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            return go.transform;
        }

        // ---- registration safety ------------------------------------------

        [Test]
        public void RegisteringTwiceStillLeavesOneParticipant()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.High);
            var probe = new ProbeParticipant();

            director.Register(probe);
            director.Register(probe);

            Assert.AreEqual(1, director.ParticipantCount);
        }

        [Test]
        public void OnlyTheReceiptCanUnregister_AndReleasingTwiceIsHarmless()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.High);
            var probe = new ProbeParticipant();

            AnimationRegistration receipt = director.Register(probe);
            Assert.IsTrue(receipt.IsValid);
            Assert.AreEqual(1, director.ParticipantCount);

            receipt.Release();
            Assert.AreEqual(0, director.ParticipantCount);

            Assert.DoesNotThrow(() => receipt.Release());
            Assert.AreEqual(0, director.ParticipantCount);
        }

        [Test]
        public void AnInvalidReceiptDoesNothing()
        {
            var receipt = default(AnimationRegistration);
            Assert.IsFalse(receipt.IsValid);
            Assert.DoesNotThrow(() => receipt.Release());
        }

        [Test]
        public void UnregisteringPutsTheParticipantBackToItsAuthoredPose()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.High);
            Transform target = MakeTarget("Visual", new Vector3(1f, 2f, 3f));
            var probe = new ProbeParticipant(target);

            AnimationRegistration receipt = director.Register(probe);
            director.Tick(1f);
            Assert.IsFalse(probe.IsAtBasePose(), "the probe should have moved");

            receipt.Release();

            Assert.IsTrue(probe.IsAtBasePose(),
                "unregistering must leave the model in its authored pose, not frozen mid-animation");
        }

        [Test]
        public void RegisteringDuringATickDoesNotCorruptTheWalkAndTakesEffectNext()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.High);
            var late = new ProbeParticipant();
            var first = new ProbeParticipant();
            first.OnTick = _ => director.Register(late);

            director.Register(first);
            Assert.DoesNotThrow(() => director.Tick(1f));

            Assert.AreEqual(1, first.Ticks);
            Assert.AreEqual(0, late.Ticks, "a participant added mid-tick must not be ticked in that same pass");
            Assert.AreEqual(2, director.ParticipantCount, "but it must be in by the time the tick ends");

            director.Tick(1f);
            Assert.AreEqual(1, late.Ticks);
        }

        [Test]
        public void UnregisteringDuringATickDoesNotCorruptTheWalk()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.High);
            var a = new ProbeParticipant();
            var b = new ProbeParticipant();
            var c = new ProbeParticipant();
            a.OnTick = _ => director.Unregister(b);

            director.Register(a);
            director.Register(b);
            director.Register(c);

            Assert.DoesNotThrow(() => director.Tick(1f));

            Assert.AreEqual(1, a.Ticks);
            Assert.AreEqual(1, c.Ticks, "removing one participant must not skip the ones after it");
            Assert.AreEqual(2, director.ParticipantCount);
        }

        [Test]
        public void AParticipantThatDiesIsDroppedWithoutThrowing()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.High);
            var alive = new ProbeParticipant();
            var doomed = new ProbeParticipant();

            director.Register(doomed);
            director.Register(alive);
            doomed.Dead = true;

            Assert.DoesNotThrow(() => director.Tick(1f));

            Assert.AreEqual(1, director.ParticipantCount, "a dead participant must be dropped");
            Assert.AreEqual(1, alive.Ticks, "and the living one must still have been ticked");
        }

        // ---- restart ordering ---------------------------------------------------

        [Test]
        public void AnOutgoingDirectorDestroyedLateDoesNotDisturbTheNewSession()
        {
            // Unity defers Destroy to end of frame while RestartCampaign builds
            // the next session immediately, so the old director's OnDestroy runs
            // *after* the new one already registered its participants.
            NebulaAnimationDirector oldDirector = MakeDirector(AnimationQualityProfile.High);
            var oldProbe = new ProbeParticipant();
            oldDirector.Register(oldProbe);

            NebulaAnimationDirector newDirector = MakeDirector(AnimationQualityProfile.High);
            Transform target = MakeTarget("New Visual", Vector3.one);
            var newProbe = new ProbeParticipant(target);
            newDirector.Register(newProbe);

            UnityEngine.Object.DestroyImmediate(oldDirector.gameObject);

            Assert.AreEqual(1, newDirector.ParticipantCount,
                "the outgoing director must not clear the incoming session's registrations");
            newDirector.Tick(1f);
            Assert.AreEqual(1, newProbe.Ticks);
        }

        /// <summary>
        /// Accessibility settings belong to the player, not to a campaign, so a session reset
        /// must leave them exactly as they were found.
        /// <para>
        /// This test previously asserted the opposite -- that <c>ResetSessionState</c> cleared
        /// all three. That was the behaviour, and it was a defect: a player who turned Motion Off
        /// on, then started or restarted a campaign, silently got full motion back, which is the
        /// one setting a player most needs to survive a scene change. The product was corrected;
        /// this assertion was left behind and has been inverted to match the contract.
        /// </para>
        /// </summary>
        [Test]
        public void SessionStateResetKeepsAccessibilityForTheNextCampaign()
        {
            bool savedReduced = AnimationAccessibility.ReducedMotion;
            bool savedMotionOff = AnimationAccessibility.MotionOff;
            bool savedComfort = AnimationAccessibility.CameraComfort;
            try
            {
                AnimationAccessibility.ReducedMotion = true;
                AnimationAccessibility.MotionOff = true;
                AnimationAccessibility.CameraComfort = true;

                NebulaAnimationDirector.ResetSessionState();

                Assert.IsTrue(AnimationAccessibility.ReducedMotion, "Reduced Motion is the player's setting, not the campaign's");
                Assert.IsTrue(AnimationAccessibility.MotionOff, "Motion Off must survive a session reset");
                Assert.IsTrue(AnimationAccessibility.CameraComfort, "Camera Comfort must survive a session reset");
            }
            finally
            {
                AnimationAccessibility.ReducedMotion = savedReduced;
                AnimationAccessibility.MotionOff = savedMotionOff;
                AnimationAccessibility.CameraComfort = savedComfort;
            }
        }

        // ---- pause and tick rate --------------------------------------------------

        [Test]
        public void AZeroDeltaAdvancesNothing_WhichIsWhatAPausedGameProduces()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.High);
            var probe = new ProbeParticipant();
            director.Register(probe);

            director.Tick(0f);
            director.Tick(-1f);

            Assert.AreEqual(0, probe.Ticks, "a paused world must not animate");
            Assert.AreEqual(0, director.TickCount);
        }

        [Test]
        public void TheProfileRateDecidesHowOftenParticipantsArePosed()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.Low);
            var probe = new ProbeParticipant();
            director.Register(probe);

            // Well under Low's interval: accumulates, does not tick.
            director.Tick(0.001f);
            Assert.AreEqual(0, probe.Ticks);

            // Past it: one tick, not a burst.
            director.Tick(1f);
            Assert.AreEqual(1, probe.Ticks, "a long frame must produce one pose, not a catch-up spasm");
        }

        [Test]
        public void QualityTiersGetProgressivelyMoreExpensive()
        {
            Assert.Less(AnimationQualityProfile.Low.TickHz, AnimationQualityProfile.Medium.TickHz);
            Assert.Less(AnimationQualityProfile.Medium.TickHz, AnimationQualityProfile.High.TickHz);

            Assert.IsFalse(AnimationQualityProfile.Low.SecondaryMotion, "Low drops the secondary motion entirely");
            Assert.IsTrue(AnimationQualityProfile.Medium.SecondaryMotion);
            Assert.IsTrue(AnimationQualityProfile.High.SecondaryMotion);
            Assert.Less(AnimationQualityProfile.Medium.SecondaryScale, AnimationQualityProfile.High.SecondaryScale);

            Assert.That(AnimationQualityProfile.Low.TickHz, Is.InRange(20f, 30f));
            Assert.That(AnimationQualityProfile.Medium.TickHz, Is.InRange(30f, 45f));
            Assert.AreEqual(60f, AnimationQualityProfile.High.TickHz, 1e-3f);
        }

        [Test]
        public void AnUnknownQualityResolvesToMediumRatherThanThrowing()
        {
            Assert.AreEqual(AnimationQuality.Medium, AnimationQualityProfile.Resolve((AnimationQuality)(-9)).Quality);
        }

        [Test]
        public void AnimationQualityFollowsTheLightingTier()
        {
            Assert.AreEqual(AnimationQuality.Low, AnimationQualityProfile.FromLightingQuality(LightingQuality.Low).Quality);
            Assert.AreEqual(AnimationQuality.Medium, AnimationQualityProfile.FromLightingQuality(LightingQuality.Medium).Quality);
            Assert.AreEqual(AnimationQuality.High, AnimationQualityProfile.FromLightingQuality(LightingQuality.High).Quality);
        }

        [Test]
        public void ChangingQualityAddsNoParticipantAndDoesNotRebaseAnything()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.Low);
            Transform target = MakeTarget("Visual", Vector3.one);
            var probe = new ProbeParticipant(target);
            director.Register(probe);

            director.SetQuality(AnimationQuality.High);
            director.SetQuality(AnimationQuality.Low);

            Assert.AreEqual(1, director.ParticipantCount, "a quality switch must not duplicate a presenter");
            Assert.AreEqual(AnimationQuality.Low, director.Profile.Quality);
        }

        // ---- accessibility ------------------------------------------------------------

        [Test]
        public void ReducedMotionCutsDecorativeAmplitudeWithoutRemovingIt()
        {
            float full = AnimationAccessibility.ScaleDecorative(1f);
            AnimationAccessibility.ReducedMotion = true;
            float reduced = AnimationAccessibility.ScaleDecorative(1f);

            Assert.Less(reduced, full);
            Assert.Greater(reduced, 0f, "the ship must still visibly answer a turn");
        }

        [Test]
        public void MotionOffRemovesDecorativeMotionEntirelyAndSnapsStateChanges()
        {
            AnimationAccessibility.MotionOff = true;

            Assert.AreEqual(0f, AnimationAccessibility.MotionScale, 1e-5f);
            Assert.AreEqual(0f, AnimationAccessibility.ScaleDecorative(10f), 1e-5f);
            Assert.AreEqual(0f, AnimationAccessibility.BankCeilingDegrees, 1e-5f);
            Assert.IsTrue(AnimationAccessibility.SnapStateChanges,
                "a required mechanical pose must still be reached, just not watched travelling");
        }

        [Test]
        public void CameraComfortTightensTheBankCeilingWithoutTouchingTheCamera()
        {
            float normal = AnimationAccessibility.BankCeilingDegrees;
            AnimationAccessibility.CameraComfort = true;
            float comfort = AnimationAccessibility.BankCeilingDegrees;

            Assert.Less(comfort, normal);
            Assert.Greater(comfort, 0f);
            Assert.LessOrEqual(normal, AnimationAccessibility.MaxVisualBankDegrees,
                "the ceiling can never exceed the hard limit");
        }

        // ---- pose safety and drift -------------------------------------------------------

        [Test]
        public void AnAnchorAppliedWithNoOffsetIsExactlyTheAuthoredPose()
        {
            Transform target = MakeTarget("Visual", new Vector3(3f, -2f, 7f));
            PoseAnchor anchor = PoseAnchor.Capture(target);

            anchor.Apply(Vector3.zero, Quaternion.identity);

            Assert.IsTrue(anchor.IsAtBasePose());
            Assert.AreEqual(new Vector3(3f, -2f, 7f), target.localPosition);
        }

        [Test]
        public void AnUnboundAnchorIsASafeNoOpSoAMissingVisualRootCannotBreakTheBoot()
        {
            PoseAnchor anchor = PoseAnchor.Capture(null);

            Assert.IsFalse(anchor.IsValid);
            Assert.DoesNotThrow(() => anchor.Apply(Vector3.one, Quaternion.Euler(10f, 20f, 30f)));
            Assert.DoesNotThrow(() => anchor.Reset());
        }

        [Test]
        public void TenThousandTicksLeaveTheTransformExactlyWhereItStarted()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.High);
            Transform target = MakeTarget("Visual", new Vector3(1.5f, -3.25f, 0.75f));
            Vector3 startPosition = target.localPosition;
            Quaternion startRotation = target.localRotation;
            Vector3 startScale = target.localScale;

            var probe = new ProbeParticipant(target);
            AnimationRegistration receipt = director.Register(probe);

            for (int i = 0; i < 10000; i++) director.Tick(1f / 60f);

            Assert.AreEqual(10000, probe.Ticks);

            receipt.Release();

            Assert.AreEqual(startPosition.x, target.localPosition.x, 1e-4f, "position drifted");
            Assert.AreEqual(startPosition.y, target.localPosition.y, 1e-4f);
            Assert.AreEqual(startPosition.z, target.localPosition.z, 1e-4f);
            Assert.Less(Quaternion.Angle(startRotation, target.localRotation), 0.01f, "rotation drifted");
            Assert.AreEqual(startScale.x, target.localScale.x, 1e-4f, "scale drifted");
        }

        [Test]
        public void TenThousandTicksGrowNoParticipantOrObjectCount()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.High);
            Transform target = MakeTarget("Visual", Vector3.one);
            director.Register(new ProbeParticipant(target));

            int childrenBefore = target.GetComponentsInChildren<Transform>(true).Length;

            for (int i = 0; i < 10000; i++) director.Tick(1f / 60f);

            Assert.AreEqual(1, director.ParticipantCount, "ticking must not register anything");
            Assert.AreEqual(childrenBefore, target.GetComponentsInChildren<Transform>(true).Length,
                "ticking must not spawn a GameObject");
        }

        [Test]
        public void ResetAllPutsEveryLiveParticipantBackWithoutUnregisteringThem()
        {
            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.High);
            Transform a = MakeTarget("A", Vector3.zero);
            Transform b = MakeTarget("B", Vector3.one);
            var pa = new ProbeParticipant(a);
            var pb = new ProbeParticipant(b);
            director.Register(pa);
            director.Register(pb);

            director.Tick(1f);
            director.ResetAllToBasePose();

            Assert.IsTrue(pa.IsAtBasePose());
            Assert.IsTrue(pb.IsAtBasePose());
            Assert.AreEqual(2, director.ParticipantCount, "resetting is not unregistering");
        }

        // ---- gameplay separation ----------------------------------------------------------

        [Test]
        public void AVisualRootIsAChildSoAnimatingItCannotMoveTheGameplayRoot()
        {
            var gameplayRoot = new GameObject("Ship (gameplay root)");
            spawned.Add(gameplayRoot);
            gameplayRoot.AddComponent<Rigidbody>();
            gameplayRoot.AddComponent<SphereCollider>();

            var visual = new GameObject("Ship Visual");
            visual.transform.SetParent(gameplayRoot.transform, false);

            Vector3 rootPositionBefore = gameplayRoot.transform.position;
            Quaternion rootRotationBefore = gameplayRoot.transform.rotation;

            NebulaAnimationDirector director = MakeDirector(AnimationQualityProfile.High);
            var probe = new ProbeParticipant(visual.transform);
            director.Register(probe);
            for (int i = 0; i < 100; i++) director.Tick(1f / 60f);

            Assert.AreEqual(rootPositionBefore, gameplayRoot.transform.position,
                "animation must never move the gameplay root");
            Assert.AreEqual(rootRotationBefore, gameplayRoot.transform.rotation);
            Assert.IsNotNull(gameplayRoot.GetComponent<Rigidbody>(), "the Rigidbody must be untouched");
            Assert.IsNotNull(gameplayRoot.GetComponent<SphereCollider>(), "the collider must be untouched");
        }

        [Test]
        public void ThePresenterNeverCreatesALightOrAnAnimator()
        {
            var go = new GameObject("Ship");
            spawned.Add(go);
            go.AddComponent<PlayerShipAnimationPresenter>();

            Assert.AreEqual(0, go.GetComponentsInChildren<Light>(true).Length,
                "animation is not allowed to add lighting — that is LGT's");
            Assert.AreEqual(0, go.GetComponentsInChildren<Animator>(true).Length,
                "this foundation is procedural; it introduces no Animator");
        }

        // ---- preserved authorities -----------------------------------------------------------

        [Test]
        public void AnimationDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }

        [Test]
        public void ThePlayerShipSourceModelIsUntouched()
        {
            // Part 4 moved this out of Resources/Ship/ into the source-only tree.
            // The bytes did not change, so the hash below did not either.
            string path = Path.Combine(
                Application.dataPath, "VisualSource/PlayerShip/Legacy/PlayerShip.glb");
            Assert.IsTrue(File.Exists(path), "the player ship model must still be where it was");

            using var sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();

            Assert.AreEqual("be56c48db3f1c99fdfde089f", hash.Substring(0, 24),
                "ANI-01 must not have modified the player ship model");
        }
    }
}
