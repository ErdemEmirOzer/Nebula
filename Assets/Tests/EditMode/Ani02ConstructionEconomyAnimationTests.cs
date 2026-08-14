using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-02: lifecycle poses, the five drill families' working character, the
    /// Logistics Center's states, and the guarantee that none of it drifts or
    /// touches a gameplay value.
    /// </summary>
    public class Ani02ConstructionEconomyAnimationTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            StructurePosePresenter.ResetPending();
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            AnimationAccessibility.ResetToDefaults();
        }

        private static DrillLifecycle[] AllLifecycles => (DrillLifecycle[])Enum.GetValues(typeof(DrillLifecycle));
        private static ResourceFamily[] AllFamilies => (ResourceFamily[])Enum.GetValues(typeof(ResourceFamily));
        private static LogisticsCenterState[] AllLogisticsStates
            => (LogisticsCenterState[])Enum.GetValues(typeof(LogisticsCenterState));

        // ---- lifecycle poses -------------------------------------------------

        [Test]
        public void EveryLifecycleStateHasABoundedPose()
        {
            foreach (DrillLifecycle state in AllLifecycles)
                foreach (float progress in new[] { 0f, 0.5f, 1f })
                {
                    (float sink, float scale, float tilt) = StructureAnimationProfiles.Lifecycle(state, progress);

                    Assert.That(sink, Is.InRange(0f, StructureAnimationProfiles.ConstructionSinkFraction),
                        $"{state}/{progress} sink");
                    Assert.That(scale, Is.InRange(0f, 1f), $"{state}/{progress} scale");
                    Assert.That(Mathf.Abs(tilt), Is.InRange(0f, StructureAnimationProfiles.DamagedTiltDegrees),
                        $"{state}/{progress} tilt");
                }
        }

        [Test]
        public void NothingIsVisibleBeforeItHasBeenDelivered()
        {
            foreach (DrillLifecycle state in new[] { DrillLifecycle.Planned, DrillLifecycle.CapsuleInTransit })
                Assert.AreEqual(0f, StructureAnimationProfiles.Lifecycle(state, 1f).scale, 1e-5f,
                    $"{state}: a structure that has not landed must not be standing there");
        }

        [Test]
        public void ConstructionRisesOutOfTheSurfaceAsRealProgressAdvances()
        {
            (float sinkStart, float scaleStart, _) =
                StructureAnimationProfiles.Lifecycle(DrillLifecycle.Constructing, 0f);
            (float sinkMid, float scaleMid, _) =
                StructureAnimationProfiles.Lifecycle(DrillLifecycle.Constructing, 0.5f);
            (float sinkEnd, float scaleEnd, _) =
                StructureAnimationProfiles.Lifecycle(DrillLifecycle.Constructing, 1f);

            Assert.Greater(sinkStart, sinkMid);
            Assert.Greater(sinkMid, sinkEnd);
            Assert.AreEqual(0f, sinkEnd, 1e-5f, "a finished build sits exactly on its slot");

            Assert.Less(scaleStart, scaleMid);
            Assert.Less(scaleMid, scaleEnd);
            Assert.AreEqual(1f, scaleEnd, 1e-5f);
            Assert.Greater(scaleStart, 0f, "a build site that vanishes reads as a bug, not as building");
        }

        [Test]
        public void ActiveIsExactlyTheAuthoredPose()
        {
            (float sink, float scale, float tilt) = StructureAnimationProfiles.Lifecycle(DrillLifecycle.Active, 1f);

            Assert.AreEqual(0f, sink, 1e-5f);
            Assert.AreEqual(1f, scale, 1e-5f);
            Assert.AreEqual(0f, tilt, 1e-5f, "only a finished structure may claim the authored pose");
        }

        [Test]
        public void DisabledIsAStillUprightSafePose()
        {
            (float sink, float scale, float tilt) = StructureAnimationProfiles.Lifecycle(DrillLifecycle.Disabled, 1f);

            Assert.AreEqual(0f, sink, 1e-5f, "a disabled structure must not sink into its slot");
            Assert.AreEqual(1f, scale, 1e-5f);
            Assert.AreEqual(0f, tilt, 1e-5f, "safe closed pose is upright and still");
            Assert.IsFalse(StructureAnimationProfiles.IsWorking(DrillLifecycle.Disabled));
        }

        [Test]
        public void DamagedTiltsSlightlyWithoutLeavingItsSlot()
        {
            (float sink, float scale, float tilt) = StructureAnimationProfiles.Lifecycle(DrillLifecycle.Damaged, 1f);

            Assert.AreEqual(0f, sink, 1e-5f, "damage must not move a structure off its surface node");
            Assert.AreEqual(1f, scale, 1e-5f);
            Assert.Greater(tilt, 0f);
            Assert.LessOrEqual(tilt, StructureAnimationProfiles.DamagedTiltDegrees,
                "damaged, not falling over");
        }

        [Test]
        public void DismantleIsTheControlledReverseOfConstruction()
        {
            (float sinkStart, float scaleStart, _) =
                StructureAnimationProfiles.Lifecycle(DrillLifecycle.Dismantled, 0f);
            (float sinkEnd, float scaleEnd, _) =
                StructureAnimationProfiles.Lifecycle(DrillLifecycle.Dismantled, 1f);

            Assert.AreEqual(0f, sinkStart, 1e-5f, "dismantling starts from where it stood");
            Assert.AreEqual(1f, scaleStart, 1e-5f);
            Assert.Greater(sinkEnd, sinkStart, "and sinks back into the surface");
            Assert.Less(scaleEnd, scaleStart);
        }

        [Test]
        public void OnlyAnActiveStructureIsTreatedAsWorking()
        {
            foreach (DrillLifecycle state in AllLifecycles)
                Assert.AreEqual(state == DrillLifecycle.Active,
                    StructureAnimationProfiles.IsWorking(state), $"{state}");
        }

        // ---- the five drill families -------------------------------------------

        [Test]
        public void EveryResourceFamilyHasItsOwnWorkingCadence()
        {
            var profiles = AllFamilies.Select(StructureAnimationProfiles.DrillWork).ToArray();

            Assert.AreEqual(5, profiles.Select(p => p.rateHz).Distinct().Count(),
                "five families must not share one cadence");
            Assert.AreEqual(5, profiles.Select(p => p.amplitude).Distinct().Count());

            foreach ((float rateHz, float amplitude) in profiles)
            {
                Assert.Greater(rateHz, 0f);
                Assert.Greater(amplitude, 0f);
                Assert.LessOrEqual(amplitude, StructureAnimationProfiles.MaxWorkAmplitude);
            }
        }

        [Test]
        public void HeavyFamiliesMoveSlowerAndFurtherThanPreciseOnes()
        {
            (float metalRate, float metalAmplitude) = StructureAnimationProfiles.DrillWork(ResourceFamily.Metal);
            (float diamondRate, float diamondAmplitude) = StructureAnimationProfiles.DrillWork(ResourceFamily.Diamond);
            (float gasRate, _) = StructureAnimationProfiles.DrillWork(ResourceFamily.Gas);
            (float waterRate, float waterAmplitude) = StructureAnimationProfiles.DrillWork(ResourceFamily.Water);

            Assert.Less(metalRate, diamondRate, "heavy percussion is slower than a precision cutter");
            Assert.Greater(metalAmplitude, diamondAmplitude, "and travels further");
            Assert.Less(gasRate, waterRate, "a compressor cycle is slower than turbine flow");
            Assert.Greater(waterRate, metalRate);
            Assert.Less(waterAmplitude, metalAmplitude, "turbine flow is fast and shallow");
        }

        [Test]
        public void AFullStoreStopsTheDrillWorking()
        {
            Assert.IsTrue(StructureAnimationProfiles.StorageBlocksWork(100f, 100f),
                "a drill with nowhere to put its output stops");
            Assert.IsTrue(StructureAnimationProfiles.StorageBlocksWork(100.5f, 100f));
            Assert.IsFalse(StructureAnimationProfiles.StorageBlocksWork(99f, 100f));
            Assert.IsFalse(StructureAnimationProfiles.StorageBlocksWork(0f, 100f));
            Assert.IsFalse(StructureAnimationProfiles.StorageBlocksWork(5f, 0f),
                "a drill with no declared capacity must not be treated as full");
        }

        // ---- logistics center ---------------------------------------------------

        [Test]
        public void OnlyAnActiveLogisticsCentreMoves()
        {
            foreach (LogisticsCenterState state in AllLogisticsStates)
            {
                (float rateHz, float amplitude) = StructureAnimationProfiles.LogisticsWork(state, 0.2f, false);
                if (state == LogisticsCenterState.Active)
                {
                    Assert.Greater(rateHz, 0f, "a working centre reads as busy");
                    Assert.Greater(amplitude, 0f);
                }
                else
                {
                    Assert.AreEqual(0f, rateHz, 1e-5f, $"{state} must be still");
                    Assert.AreEqual(0f, amplitude, 1e-5f);
                }
            }
        }

        [Test]
        public void TheCentreSlowsAsItFillsAndStopsWhenItIsFull()
        {
            (float idleRate, _) = StructureAnimationProfiles.LogisticsWork(LogisticsCenterState.Active, 0.1f, false);
            (float highRate, _) = StructureAnimationProfiles.LogisticsWork(LogisticsCenterState.Active, 0.85f, false);
            (float fullRate, float fullAmplitude) =
                StructureAnimationProfiles.LogisticsWork(LogisticsCenterState.Active, 1f, false);

            Assert.Greater(idleRate, highRate, "a nearly full centre is visibly labouring");
            Assert.AreEqual(0f, fullRate, 1e-5f, "a full centre is holding, not working");
            Assert.AreEqual(0f, fullAmplitude, 1e-5f);
        }

        [Test]
        public void AnUpgradingCentreShowsItsOwnDeliberateCycle()
        {
            (float rateHz, float amplitude) =
                StructureAnimationProfiles.LogisticsWork(LogisticsCenterState.Active, 0.1f, upgrading: true);

            Assert.Greater(rateHz, 0f);
            Assert.LessOrEqual(amplitude, StructureAnimationProfiles.MaxWorkAmplitude);
        }

        // ---- presenters -----------------------------------------------------------

        /// <summary>A presenter driven by values a test sets, so the pose logic can be checked without a materialized planet.</summary>
        private sealed class ProbePresenter : StructurePosePresenter
        {
            public DrillLifecycle State = DrillLifecycle.Active;
            public float Progress = 1f;
            public float RateHz = 2f;
            public float Amplitude = 0.02f;

            public void BindProbe(NebulaAnimationDirector director) => BindAndRegister(director);
            protected override DrillLifecycle ResolveState() => State;
            protected override float ResolveProgress() => Progress;
            protected override (float rateHz, float amplitude) ResolveWork() => (RateHz, Amplitude);
        }

        private (NebulaAnimationDirector director, ProbePresenter presenter, Transform visual) MakeRig()
        {
            var directorGo = new GameObject("Nebula Animation");
            spawned.Add(directorGo);
            var director = directorGo.AddComponent<NebulaAnimationDirector>();
            director.Initialize(AnimationQualityProfile.High);

            var root = new GameObject("Structure (gameplay root)");
            spawned.Add(root);
            root.AddComponent<BoxCollider>();

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);

            var presenter = root.AddComponent<ProbePresenter>();
            presenter.BindProbe(director);
            return (director, presenter, visual.transform);
        }

        [Test]
        public void ThePresenterPosesTheVisualChildAndNeverTheGameplayRoot()
        {
            (NebulaAnimationDirector director, ProbePresenter presenter, Transform visual) = MakeRig();
            Transform root = presenter.transform;

            Vector3 rootBefore = root.localPosition;
            Quaternion rootRotationBefore = root.localRotation;
            int colliders = root.GetComponentsInChildren<Collider>(true).Length;

            presenter.State = DrillLifecycle.Constructing;
            presenter.Progress = 0.3f;
            director.Tick(1f);

            Assert.AreEqual(rootBefore, root.localPosition, "the gameplay root must not move");
            Assert.AreEqual(rootRotationBefore, root.localRotation);
            Assert.AreEqual(colliders, root.GetComponentsInChildren<Collider>(true).Length,
                "animation must not disturb colliders");
            Assert.IsTrue(presenter.HasVisual);
        }

        [Test]
        public void APresenterWithNoVisualChildIsASafeNoOp()
        {
            var directorGo = new GameObject("Nebula Animation");
            spawned.Add(directorGo);
            var director = directorGo.AddComponent<NebulaAnimationDirector>();
            director.Initialize(AnimationQualityProfile.High);

            var bare = new GameObject("Structure with no model");
            spawned.Add(bare);
            var presenter = bare.AddComponent<ProbePresenter>();

            Assert.DoesNotThrow(() => presenter.BindProbe(director));
            Assert.IsFalse(presenter.HasVisual);
            Assert.DoesNotThrow(() => director.Tick(1f));
        }

        [Test]
        public void MotionOffStopsTheWorkingMotionButKeepsTheLifecyclePose()
        {
            (NebulaAnimationDirector director, ProbePresenter presenter, Transform visual) = MakeRig();

            // A half-built structure must still read as half-built with motion off.
            presenter.State = DrillLifecycle.Constructing;
            presenter.Progress = 0.5f;
            AnimationAccessibility.MotionOff = true;
            director.Tick(1f);

            Assert.Less(visual.localScale.x, 1f, "construction progress must stay readable with Motion Off");

            presenter.State = DrillLifecycle.Active;
            presenter.Progress = 1f;
            director.Tick(1f);
            Vector3 first = visual.localPosition;
            director.Tick(1f);

            Assert.AreEqual(first, visual.localPosition, "Motion Off must remove the working motion entirely");
        }

        [Test]
        public void ReducedMotionShrinksTheWorkingMotionWithoutRemovingProgress()
        {
            (NebulaAnimationDirector director, ProbePresenter presenter, Transform visual) = MakeRig();
            presenter.State = DrillLifecycle.Constructing;
            presenter.Progress = 0.5f;

            AnimationAccessibility.ReducedMotion = true;
            director.Tick(1f);

            Assert.Less(visual.localScale.x, 1f, "progress must remain readable");
            Assert.Greater(visual.localScale.x, 0f);
        }

        [Test]
        public void TenThousandTicksLeaveTheVisualExactlyAtItsAuthoredPose()
        {
            (NebulaAnimationDirector director, ProbePresenter presenter, Transform visual) = MakeRig();
            Vector3 basePosition = visual.localPosition;
            Quaternion baseRotation = visual.localRotation;
            Vector3 baseScale = visual.localScale;

            presenter.State = DrillLifecycle.Active;
            presenter.Progress = 1f;
            for (int i = 0; i < 10000; i++) director.Tick(1f / 60f);

            presenter.ResetToBasePose();

            Assert.AreEqual(basePosition.x, visual.localPosition.x, 1e-4f, "position drifted");
            Assert.AreEqual(basePosition.y, visual.localPosition.y, 1e-4f);
            Assert.AreEqual(basePosition.z, visual.localPosition.z, 1e-4f);
            Assert.Less(Quaternion.Angle(baseRotation, visual.localRotation), 0.01f, "rotation drifted");
            Assert.AreEqual(baseScale.x, visual.localScale.x, 1e-4f, "scale drifted");
            Assert.AreEqual(1, director.ParticipantCount, "ticking must not register anything");
        }

        [Test]
        public void DestroyingAPresenterUnregistersItCleanly()
        {
            (NebulaAnimationDirector director, ProbePresenter presenter, Transform _) = MakeRig();
            Assert.AreEqual(1, director.ParticipantCount);

            UnityEngine.Object.DestroyImmediate(presenter);
            director.Tick(1f);

            Assert.AreEqual(0, director.ParticipantCount, "a destroyed presenter must leave no participant behind");
        }

        [Test]
        public void APresenterBuiltWithoutADirectorIsAdoptedOnTheNextTick()
        {
            var directorGo = new GameObject("Nebula Animation");
            spawned.Add(directorGo);
            var director = directorGo.AddComponent<NebulaAnimationDirector>();
            director.Initialize(AnimationQualityProfile.High);

            var root = new GameObject("Structure");
            spawned.Add(root);
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(root.transform, false);
            var presenter = root.AddComponent<ProbePresenter>();

            // This is what a drill built mid-flight looks like: no route to the
            // session context, so it queues instead.
            presenter.BindProbe(null);
            Assert.AreEqual(1, StructurePosePresenter.PendingCount);
            Assert.AreEqual(0, director.ParticipantCount);

            director.Tick(1f);

            Assert.AreEqual(0, StructurePosePresenter.PendingCount, "the queue must drain");
            Assert.AreEqual(1, director.ParticipantCount, "and the director must have adopted it");
        }

        [Test]
        public void SessionResetClearsTheAdoptionQueueSoANewCampaignInheritsNothing()
        {
            var root = new GameObject("Structure");
            spawned.Add(root);
            root.AddComponent<ProbePresenter>().BindProbe(null);
            Assert.AreEqual(1, StructurePosePresenter.PendingCount);

            NebulaAnimationDirector.ResetSessionState();

            Assert.AreEqual(0, StructurePosePresenter.PendingCount);
        }

        [Test]
        public void ThePresentersAddNoLightAndNoAnimator()
        {
            (NebulaAnimationDirector director, ProbePresenter presenter, Transform _) = MakeRig();
            director.Tick(1f);

            Assert.AreEqual(0, presenter.GetComponentsInChildren<Light>(true).Length,
                "animation must not add lighting — that is LGT's");
            Assert.AreEqual(0, presenter.GetComponentsInChildren<Animator>(true).Length);
        }

        // ---- preserved authorities ------------------------------------------------------

        [Test]
        public void StructureAnimationDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }
    }
}
