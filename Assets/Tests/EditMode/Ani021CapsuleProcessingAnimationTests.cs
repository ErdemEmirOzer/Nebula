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
    /// ANI-02.1: the capsule's descent geometry, the proxy pool's lease safety,
    /// and the processing module's state mapping.
    /// </summary>
    public class Ani021CapsuleProcessingAnimationTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            AnimationAccessibility.ResetToDefaults();
        }

        private CapsuleProxyPool MakePool(int capacity)
        {
            var host = new GameObject("Pool Host");
            spawned.Add(host);
            return CapsuleProxyPool.Create(host.transform, capacity, null);
        }

        // ---- descent geometry ---------------------------------------------

        [Test]
        public void TheRouteStartsAtTheShipAndEndsExactlyOnTheSlot()
        {
            var start = new Vector3(0f, 500f, 0f);
            var end = new Vector3(120f, 0f, -40f);
            var normal = Vector3.up;

            Assert.AreEqual(start, CapsuleFlightProfile.Position(start, end, normal, 0f));
            Vector3 landed = CapsuleFlightProfile.Position(start, end, normal, 1f);
            Assert.Less((landed - end).magnitude, 1e-3f, "the capsule must land exactly on its slot");
        }

        [Test]
        public void TheRouteBowsAwayFromTheSurfaceInsteadOfCuttingThroughIt()
        {
            var start = new Vector3(0f, 300f, 0f);
            var end = Vector3.zero;
            var normal = Vector3.up;

            Vector3 midpoint = CapsuleFlightProfile.Position(start, end, normal, 0.5f);
            Vector3 straightLine = Vector3.Lerp(start, end, 0.5f);

            Assert.Greater(Vector3.Dot(midpoint - straightLine, normal), 0f,
                "the descent must arc above the straight line, not drive through the planet");
        }

        [Test]
        public void TheRouteIsContinuousWithNoJumps()
        {
            var start = new Vector3(-200f, 900f, 350f);
            var end = new Vector3(40f, 0f, -15f);
            var normal = new Vector3(0.2f, 1f, -0.1f).normalized;

            Vector3 previous = CapsuleFlightProfile.Position(start, end, normal, 0f);
            float longestStep = 0f;

            for (int i = 1; i <= 200; i++)
            {
                Vector3 here = CapsuleFlightProfile.Position(start, end, normal, i / 200f);
                longestStep = Mathf.Max(longestStep, (here - previous).magnitude);
                previous = here;
            }

            float straightDistance = (end - start).magnitude;
            Assert.Less(longestStep, straightDistance * 0.05f,
                "no single step may look like a teleport");
        }

        [Test]
        public void TheCapsuleStandsUprightOnThePlanetNormalByTheTimeItLands()
        {
            var start = new Vector3(0f, 800f, 0f);
            var end = Vector3.zero;
            var normal = new Vector3(0.3f, 0.9f, 0.1f).normalized;

            Quaternion landed = CapsuleFlightProfile.Orientation(start, end, normal, 1f);
            Vector3 landedUp = landed * Vector3.up;

            Assert.Less(Vector3.Angle(landedUp, normal), 1f,
                "a capsule must land on its feet, aligned to the surface it is standing on");
        }

        [Test]
        public void OrientationTurnsSmoothlyFromTravellingToStanding()
        {
            var start = new Vector3(0f, 600f, 200f);
            var end = Vector3.zero;
            var normal = Vector3.up;

            Quaternion previous = CapsuleFlightProfile.Orientation(start, end, normal, 0f);
            float biggestTurn = 0f;

            for (int i = 1; i <= 100; i++)
            {
                Quaternion here = CapsuleFlightProfile.Orientation(start, end, normal, i / 100f);
                biggestTurn = Mathf.Max(biggestTurn, Quaternion.Angle(previous, here));
                previous = here;
            }

            Assert.Less(biggestTurn, 25f, "the capsule must not snap round mid-flight");
        }

        [Test]
        public void ADegenerateRouteDoesNotThrow()
        {
            Assert.DoesNotThrow(() => CapsuleFlightProfile.Position(Vector3.zero, Vector3.zero, Vector3.zero, 0.5f));
            Assert.DoesNotThrow(() => CapsuleFlightProfile.Orientation(Vector3.zero, Vector3.zero, Vector3.zero, 0.5f));
        }

        // ---- phases and real progress ----------------------------------------

        [TestCase(0f, CapsuleFlightPhase.Prepared)]
        [TestCase(0.05f, CapsuleFlightPhase.Launch)]
        [TestCase(0.4f, CapsuleFlightPhase.Travel)]
        [TestCase(0.8f, CapsuleFlightPhase.SurfaceApproach)]
        [TestCase(0.97f, CapsuleFlightPhase.Settle)]
        [TestCase(1f, CapsuleFlightPhase.ConstructionHandOff)]
        public void PhasesFollowRealProgressInOrder(float progress, CapsuleFlightPhase expected)
        {
            Assert.AreEqual(expected, CapsuleFlightProfile.Phase(progress));
        }

        [Test]
        public void ProgressComesFromTheRealRemainingCountdown()
        {
            Assert.AreEqual(0f, CapsuleFlightProfile.ProgressFromRemaining(10f, 10f), 1e-4f,
                "a capsule that has not started is at the ship");
            Assert.AreEqual(0.5f, CapsuleFlightProfile.ProgressFromRemaining(5f, 10f), 1e-4f);
            Assert.AreEqual(1f, CapsuleFlightProfile.ProgressFromRemaining(0f, 10f), 1e-4f);
        }

        [Test]
        public void ACapsuleLoadedMidFlightResumesWhereItsCountdownSaysNotAtTheShip()
        {
            // A save restored with 3 of 10 seconds left must be 70% down the
            // route, not starting over.
            float progress = CapsuleFlightProfile.ProgressFromRemaining(3f, 10f);

            Assert.AreEqual(0.7f, progress, 1e-4f);

            CapsuleFlightPhase phase = CapsuleFlightProfile.Phase(progress);
            Assert.AreNotEqual(CapsuleFlightPhase.Prepared, phase, "it must not be back on the ship");
            Assert.AreNotEqual(CapsuleFlightPhase.Launch, phase, "nor replaying its launch");
            Assert.Greater(CapsuleFlightProfile.Position(new Vector3(0f, 400f, 0f), Vector3.zero, Vector3.up, progress).y,
                0f, "it is somewhere between the ship and the slot, where its countdown says");

            // And a capsule further along really has reached approach.
            Assert.AreEqual(CapsuleFlightPhase.SurfaceApproach,
                CapsuleFlightProfile.Phase(CapsuleFlightProfile.ProgressFromRemaining(2f, 10f)));
        }

        [Test]
        public void AFinishedCapsuleReportsHandOffAndNeverReplays()
        {
            Assert.AreEqual(CapsuleFlightPhase.ConstructionHandOff,
                CapsuleFlightProfile.Phase(CapsuleFlightProfile.ProgressFromRemaining(0f, 10f)));
            Assert.AreEqual(CapsuleFlightPhase.ConstructionHandOff,
                CapsuleFlightProfile.Phase(CapsuleFlightProfile.ProgressFromRemaining(0f, 0f)),
                "a capsule with no countdown left has already landed");
        }

        [Test]
        public void TheQuickAssetStaggerIsReadFromGameplayAndNotDecidedHere()
        {
            Assert.AreEqual(0f, CapsuleFlightProfile.QuickAssetSendDelay(0), 1e-4f);
            Assert.AreEqual(2f, CapsuleFlightProfile.QuickAssetSendDelay(1), 1e-4f);
            Assert.AreEqual(4f, CapsuleFlightProfile.QuickAssetSendDelay(2), 1e-4f);

            Assert.AreEqual(QuickAssetPackageService.DrillSendDelay, CapsuleFlightProfile.QuickAssetSendDelay(0), 1e-4f);
            Assert.AreEqual(QuickAssetPackageService.CommandSendDelay, CapsuleFlightProfile.QuickAssetSendDelay(1), 1e-4f);
            Assert.AreEqual(QuickAssetPackageService.LaserSendDelay, CapsuleFlightProfile.QuickAssetSendDelay(2), 1e-4f);
        }

        // ---- proxy pool ---------------------------------------------------------

        [Test]
        public void ThePoolBuildsEveryProxyUpFrontAndNoneAreVisibleUntilLeased()
        {
            CapsuleProxyPool pool = MakePool(4);

            Assert.AreEqual(4, pool.Capacity);
            Assert.AreEqual(0, pool.LeasedCount);
            Assert.AreEqual(4, pool.Root.childCount, "a build order must not Instantiate a capsule");
            for (int i = 0; i < pool.Root.childCount; i++)
                Assert.IsFalse(pool.Root.GetChild(i).gameObject.activeSelf);
        }

        [Test]
        public void AProxyCarriesNoColliderAndNoRigidbodySoItIsNeverPartOfTheWorld()
        {
            var host = new GameObject("Pool Host");
            spawned.Add(host);

            var template = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            spawned.Add(template);
            template.AddComponent<Rigidbody>();

            CapsuleProxyPool pool = CapsuleProxyPool.Create(host.transform, 2, template);

            Assert.AreEqual(0, pool.Root.GetComponentsInChildren<Collider>(true).Length,
                "a capsule proxy is scenery and must never collide with anything");
            Assert.AreEqual(0, pool.Root.GetComponentsInChildren<Rigidbody>(true).Length);
        }

        [Test]
        public void AFullPoolRefusesWithoutAffectingAnythingElse()
        {
            CapsuleProxyPool pool = MakePool(1);

            Assert.IsTrue(pool.TryLease(out CapsuleProxyLease first, out _));
            Assert.IsFalse(pool.TryLease(out CapsuleProxyLease refused, out Transform refusedProxy),
                "a full pool must be able to say no — the build is already committed");
            Assert.IsFalse(refused.IsValid);
            Assert.IsNull(refusedProxy);
            Assert.AreEqual(1, pool.Capacity, "the pool never grows");
            Assert.IsTrue(pool.IsHolder(first));
        }

        [Test]
        public void ReleaseIsIdempotentAndAStaleLeaseCannotCloseANewProxy()
        {
            CapsuleProxyPool pool = MakePool(1);

            pool.TryLease(out CapsuleProxyLease stale, out _);
            pool.Release(stale);
            Assert.AreEqual(0, pool.LeasedCount);

            Assert.DoesNotThrow(() => pool.Release(stale));
            Assert.AreEqual(0, pool.LeasedCount, "releasing twice must not go negative");

            pool.TryLease(out CapsuleProxyLease current, out Transform proxy);
            pool.Release(stale);

            Assert.IsTrue(pool.IsHolder(current),
                "an old lease must not switch off the proxy that took its slot");
            Assert.IsTrue(proxy.gameObject.activeSelf);
            Assert.AreEqual(1, pool.LeasedCount);
        }

        [Test]
        public void ReleaseAllHandsEveryProxyBack()
        {
            CapsuleProxyPool pool = MakePool(3);
            pool.TryLease(out _, out _);
            pool.TryLease(out _, out _);
            Assert.AreEqual(2, pool.LeasedCount);

            pool.ReleaseAll();

            Assert.AreEqual(0, pool.LeasedCount);
            for (int i = 0; i < pool.Root.childCount; i++)
                Assert.IsFalse(pool.Root.GetChild(i).gameObject.activeSelf);
        }

        [Test]
        public void AHundredConsecutiveBuildsReuseTheSameProxiesWithoutGrowing()
        {
            CapsuleProxyPool pool = MakePool(3);
            int childrenBefore = pool.Root.childCount;

            for (int i = 0; i < 100; i++)
            {
                Assert.IsTrue(pool.TryLease(out CapsuleProxyLease lease, out _));
                pool.Release(lease);
            }

            Assert.AreEqual(childrenBefore, pool.Root.childCount,
                "a hundred builds must not create a hundred capsules");
            Assert.AreEqual(0, pool.LeasedCount);
        }

        // ---- the capsule presenter ---------------------------------------------------

        [Test]
        public void ThePresenterQueuesForAdoptionWhenBuiltWithoutADirector()
        {
            BuildCapsulePresenter.ResetPending();

            var go = new GameObject("Structure");
            spawned.Add(go);
            go.AddComponent<BuildCapsulePresenter>().BindDrill(null, Vector3.zero, null);

            Assert.AreEqual(1, BuildCapsulePresenter.PendingCount,
                "a capsule built mid-flight must queue rather than look the director up");

            BuildCapsulePresenter.ResetPending();
            Assert.AreEqual(0, BuildCapsulePresenter.PendingCount,
                "and a session reset must clear the queue");
        }

        [Test]
        public void ACapsuleWithNoRuntimeIsTreatedAsAlreadyLandedAndNeverFlies()
        {
            BuildCapsulePresenter.ResetPending();

            var go = new GameObject("Structure");
            spawned.Add(go);
            var presenter = go.AddComponent<BuildCapsulePresenter>();
            presenter.BindDrill(null, Vector3.zero, null);

            Assert.IsTrue(presenter.HandedOff,
                "a build with no live countdown must not start a descent");
            Assert.IsFalse(presenter.HasProxy);

            Assert.DoesNotThrow(() => presenter.VisualTick(1f));
            Assert.IsFalse(presenter.HasProxy, "and it must never lease a proxy");

            BuildCapsulePresenter.ResetPending();
        }

        [Test]
        public void DestroyingTheStructureLeavesNoCapsuleFlying()
        {
            BuildCapsulePresenter.ResetPending();

            var go = new GameObject("Structure");
            var presenter = go.AddComponent<BuildCapsulePresenter>();
            presenter.BindDrill(null, Vector3.zero, null);
            Assert.AreEqual(1, BuildCapsulePresenter.PendingCount);

            UnityEngine.Object.DestroyImmediate(go);

            Assert.AreEqual(0, BuildCapsulePresenter.PendingCount,
                "a cancelled or demoted build must drop out of the queue too");
        }

        // ---- processing ------------------------------------------------------------

        [Test]
        public void ThePresenterNamesTheRealModuleAnchorRatherThanGuessingOne()
        {
            Assert.AreEqual("anchor.industry.resource_processing", ProcessingAnimationPresenter.ModuleAnchorName);
            CollectionAssert.Contains(Nebula.Ship.PlayerShipVisualController.LogicalModuleAnchors,
                ProcessingAnimationPresenter.ModuleAnchorName,
                "the anchor must be one the ship really declares");
        }

        [Test]
        public void AShipWithoutTheProcessingModuleIsASafeNoOp()
        {
            var go = new GameObject("Ship without the module");
            spawned.Add(go);
            var presenter = go.AddComponent<ProcessingAnimationPresenter>();

            Assert.DoesNotThrow(() => presenter.Initialize(null, null, null));
            Assert.IsFalse(presenter.HasModuleAnchor,
                "no module means no anchor, and no invented position on the hull");
            Assert.DoesNotThrow(() => presenter.VisualTick(1f));
            Assert.AreEqual(ProcessingAnimationState.Idle, presenter.CurrentState);
            Assert.DoesNotThrow(() => presenter.ResetToBasePose());
        }

        [Test]
        public void TheProcessingCadenceIsHeavyAndIndustrialRatherThanArcade()
        {
            Assert.Less(ProcessingAnimationPresenter.ProcessingRateHz, 1.5f,
                "a refinery cycles slowly — this is not an arcade orbit");
            Assert.Greater(ProcessingAnimationPresenter.ProcessingRateHz, 0f);
            Assert.LessOrEqual(ProcessingAnimationPresenter.ProcessingAmplitude,
                StructureAnimationProfiles.MaxWorkAmplitude,
                "and it stays inside the shared amplitude ceiling");
        }

        [Test]
        public void EveryProcessingStateIsDistinct()
        {
            var states = (ProcessingAnimationState[])Enum.GetValues(typeof(ProcessingAnimationState));
            Assert.AreEqual(5, states.Length, "Idle, InputAccepted, Processing, OutputReady, Complete");
            Assert.AreEqual(5, states.Distinct().Count());
        }

        [Test]
        public void ThePresenterAddsNoLightAndNoAnimator()
        {
            var go = new GameObject("Ship");
            spawned.Add(go);
            go.AddComponent<ProcessingAnimationPresenter>();

            Assert.AreEqual(0, go.GetComponentsInChildren<Light>(true).Length);
            Assert.AreEqual(0, go.GetComponentsInChildren<Animator>(true).Length);
        }

        // ---- accessibility and quality ----------------------------------------------

        [Test]
        public void MotionOffRemovesSecondaryMotionButLeavesTheRouteIntact()
        {
            AnimationAccessibility.MotionOff = true;

            Assert.AreEqual(0f, AnimationAccessibility.MotionScale, 1e-5f);

            // The route itself is not decorative: a capsule must still travel
            // from ship to slot, or the player cannot see what is happening.
            var start = new Vector3(0f, 400f, 0f);
            var end = Vector3.zero;
            Vector3 mid = CapsuleFlightProfile.Position(start, end, Vector3.up, 0.5f);

            Assert.Greater((mid - start).magnitude, 1f, "the capsule still has to get there");
            Assert.Greater((mid - end).magnitude, 1f);
        }

        [Test]
        public void ReducedMotionKeepsTheRouteAndTrimsTheRest()
        {
            AnimationAccessibility.ReducedMotion = true;

            Assert.Less(AnimationAccessibility.MotionScale, 1f);
            Assert.Greater(AnimationAccessibility.MotionScale, 0f);
            Assert.AreEqual(Vector3.zero,
                CapsuleFlightProfile.Position(Vector3.zero, Vector3.zero, Vector3.up, 1f),
                "route geometry is unaffected by accessibility");
        }

        // ---- preserved authorities -------------------------------------------------------

        [Test]
        public void CapsuleAndProcessingAnimationDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }

        [Test]
        public void TheQuickAssetCostsAndTimingsAreUntouched()
        {
            Assert.AreEqual(0f, QuickAssetPackageService.DrillSendDelay, 1e-4f);
            Assert.AreEqual(2f, QuickAssetPackageService.CommandSendDelay, 1e-4f);
            Assert.AreEqual(4f, QuickAssetPackageService.LaserSendDelay, 1e-4f);
            Assert.AreEqual(65, QuickAssetPackageService.TotalBasicCost);
            Assert.AreEqual(15, QuickAssetPackageService.TotalSpecialCost);
        }
    }
}
