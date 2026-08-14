using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Nebula.Content;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// ANI-03.1: which real state produces which of the seven existing VFX
    /// sprites, when a transfer is worth drawing at all, and the guarantee that
    /// the pool behind it never grows.
    /// </summary>
    public class Ani031SupportOperationVisualsTests
    {
        private GameObject poolRoot;

        [TearDown]
        public void Cleanup()
        {
            AnimationAccessibility.ResetToDefaults();
            if (poolRoot != null) UnityEngine.Object.DestroyImmediate(poolRoot);
            poolRoot = null;
        }

        private SupportOperationPool NewPool(int capacityPerVfx)
        {
            poolRoot = new GameObject("ani031-pool");
            return new SupportOperationPool(poolRoot.transform, capacityPerVfx);
        }

        // --- scout scan -------------------------------------------------------

        [Test]
        public void TheScanEffect_AppearsOnlyWhileTheRealCounterIsAdvancing()
        {
            Assert.AreEqual(SupportOperationVisual.Scan, SupportOperationProfiles.ForScout(scanAdvancing: true));
            Assert.AreEqual(SupportOperationVisual.None, SupportOperationProfiles.ForScout(scanAdvancing: false),
                "approaching, finished and cancelled scans all show nothing");
        }

        [Test]
        public void TheScanUsesTheExistingScanSprite()
            => Assert.AreEqual(NebulaCommonVfx.ScanPulse, SupportOperationProfiles.Vfx(SupportOperationVisual.Scan));

        // --- engineer ---------------------------------------------------------

        [Test]
        public void TheEightSecondSafetyGate_ShowsNoWork()
        {
            Assert.AreEqual(SupportOperationVisual.None,
                SupportOperationProfiles.ForEngineer(EngineerPhase.WaitForSafety, EngineerWorkKind.Repair, healingNow: true),
                "waiting for safety is waiting, not working");
            Assert.AreEqual(8f, EngineerMissionState.RequiredSafeSeconds,
                "ANI-03.1 must not move the real safety gate");
        }

        [Test]
        public void RepairShows_OnlyWhenTheTargetIsReallyGainingHull()
        {
            Assert.AreEqual(SupportOperationVisual.Repair,
                SupportOperationProfiles.ForEngineer(EngineerPhase.Working, EngineerWorkKind.Repair, healingNow: true));
            Assert.AreEqual(SupportOperationVisual.None,
                SupportOperationProfiles.ForEngineer(EngineerPhase.Working, EngineerWorkKind.Repair, healingNow: false),
                "a stalled repair must not look like a working one");
        }

        [Test]
        public void RecoveryIsItsOwnVisual_DistinctFromARepair()
        {
            Assert.AreEqual(SupportOperationVisual.Recovery,
                SupportOperationProfiles.ForEngineer(EngineerPhase.Working, EngineerWorkKind.Recover, healingNow: true));
            Assert.AreNotEqual(SupportOperationProfiles.Vfx(SupportOperationVisual.Repair),
                SupportOperationProfiles.Vfx(SupportOperationVisual.Recovery));
        }

        [Test]
        public void NoEngineerPhaseOtherThanWorking_EverDrawsWork()
        {
            foreach (EngineerPhase phase in Enum.GetValues(typeof(EngineerPhase)).Cast<EngineerPhase>())
            {
                if (phase == EngineerPhase.Working) continue;
                foreach (EngineerWorkKind work in Enum.GetValues(typeof(EngineerWorkKind)).Cast<EngineerWorkKind>())
                    Assert.AreEqual(SupportOperationVisual.None,
                        SupportOperationProfiles.ForEngineer(phase, work, healingNow: true),
                        $"{phase} is not work");
            }
        }

        // --- escort -----------------------------------------------------------

        [Test]
        public void EscortMarksOnlyTheStatesThatHaveAnHonestSprite()
        {
            Assert.AreEqual(SupportOperationVisual.RetreatWarning,
                SupportOperationProfiles.ForEscort(EscortSubState.Retreating));
            Assert.AreEqual(SupportOperationVisual.Protect,
                SupportOperationProfiles.ForEscort(EscortSubState.Following));

            // Weapon behaviour is ANI-04's, and there is no muzzle node yet.
            Assert.AreEqual(SupportOperationVisual.None, SupportOperationProfiles.ForEscort(EscortSubState.Engaging));
            Assert.AreEqual(SupportOperationVisual.None, SupportOperationProfiles.ForEscort(EscortSubState.Chasing));
        }

        // --- poi: no information leaks ---------------------------------------

        [Test]
        public void APoiJobLeaksNothingBeforeItIsWorked()
        {
            foreach (VehicleClass cls in Enum.GetValues(typeof(VehicleClass)).Cast<VehicleClass>())
            {
                Assert.AreEqual(SupportOperationVisual.None,
                    SupportOperationProfiles.ForPoi(cls, PoiTaskPhaseKind.ToTarget),
                    $"{cls} must reveal nothing on the way there");
                Assert.AreEqual(SupportOperationVisual.None,
                    SupportOperationProfiles.ForPoi(cls, PoiTaskPhaseKind.Returning),
                    $"{cls} must not replay the job on the way home");
            }
        }

        [Test]
        public void OnlyAScoutScansAPoi_TheOthersJustCarryThings()
        {
            Assert.AreEqual(SupportOperationVisual.Scan,
                SupportOperationProfiles.ForPoi(VehicleClass.Scout, PoiTaskPhaseKind.Working));

            // A salvage is told entirely by the real item proxy; inventing a
            // second effect for it would say more than the game knows.
            Assert.AreEqual(SupportOperationVisual.None,
                SupportOperationProfiles.ForPoi(VehicleClass.Collector, PoiTaskPhaseKind.Working));
        }

        // --- transfers --------------------------------------------------------

        [Test]
        public void AZeroTransfer_ProducesNoProxy()
        {
            Assert.AreEqual(TransferDirection.None, SupportOperationProfiles.Direction(0f));
            Assert.AreEqual(TransferDirection.None, SupportOperationProfiles.Direction(-0f));
            Assert.AreEqual(TransferDirection.None,
                SupportOperationProfiles.Direction(SupportOperationProfiles.TransferEpsilon * 0.5f),
                "float noise is not a transfer");
        }

        [Test]
        public void ARealTransfer_ReadsItsDirectionFromTheVehiclesOwnCargo()
        {
            Assert.AreEqual(TransferDirection.Inbound, SupportOperationProfiles.Direction(12.5f));
            Assert.AreEqual(TransferDirection.Outbound, SupportOperationProfiles.Direction(-12.5f));
        }

        [Test]
        public void EveryTransferredItem_ResolvesThroughItsRealStableId()
        {
            // The proxy pool refuses anything the catalog does not know, so a
            // typo shows nothing rather than something wrong.
            Assert.IsTrue(NebulaVisualCatalog.TryGetItem(ProcessingCatalog.BasicPartId, out var part));
            Assert.IsNotNull(part);
            Assert.IsFalse(NebulaVisualCatalog.TryGetItem("item.that.does.not.exist", out _));
            Assert.IsFalse(NebulaVisualCatalog.TryGetItem(null, out _));
        }

        // --- the sprite mapping is total and honest ---------------------------

        [Test]
        public void EveryOperationMapsToOneOfTheSevenExistingSprites()
        {
            foreach (SupportOperationVisual visual in Enum.GetValues(typeof(SupportOperationVisual)).Cast<SupportOperationVisual>())
            {
                NebulaCommonVfx vfx = SupportOperationProfiles.Vfx(visual);
                Assert.IsTrue(Enum.IsDefined(typeof(NebulaCommonVfx), vfx), $"{visual} must map to a real sprite");
                Assert.IsNotNull(NebulaProducedVisualCatalog.Vfx(vfx), $"{vfx} must have a resource path");
            }
        }

        [Test]
        public void NoneDrawsNothing()
        {
            Assert.IsFalse(SupportOperationProfiles.HasVfx(SupportOperationVisual.None));
            Assert.IsFalse(SupportOperationProfiles.DrawsOnTarget(SupportOperationVisual.None));
        }

        [Test]
        public void WorkIsDrawnOnTheThingBeingWorkedOn_NotOnTheMechanic()
        {
            Assert.IsTrue(SupportOperationProfiles.DrawsOnTarget(SupportOperationVisual.Repair));
            Assert.IsTrue(SupportOperationProfiles.DrawsOnTarget(SupportOperationVisual.Recovery));
            Assert.IsFalse(SupportOperationProfiles.DrawsOnTarget(SupportOperationVisual.Scan),
                "a scan is emitted by the scout itself");
        }

        // --- pool -------------------------------------------------------------

        [Test]
        public void ThePool_HandsOutAtMostItsCapacity_AndTakesItAllBack()
        {
            SupportOperationPool pool = NewPool(2);

            Assert.IsTrue(pool.TryLease(NebulaCommonVfx.ScanPulse, out var first, out Transform a));
            Assert.IsTrue(pool.TryLease(NebulaCommonVfx.ScanPulse, out var second, out Transform b));
            Assert.IsFalse(pool.TryLease(NebulaCommonVfx.ScanPulse, out _, out _),
                "a full pool refuses rather than allocating");
            Assert.AreNotSame(a, b);
            Assert.AreEqual(2, pool.LeasedCount);

            pool.Release(first);
            Assert.AreEqual(1, pool.LeasedCount);
            Assert.IsTrue(pool.TryLease(NebulaCommonVfx.ScanPulse, out _, out _), "a released slot comes back");

            pool.ReleaseAll();
            Assert.AreEqual(0, pool.LeasedCount);
            Assert.IsFalse(pool.IsHolder(second), "ReleaseAll invalidates every outstanding lease");
        }

        [Test]
        public void ReleasingTwice_NeverStealsTheSlotFromItsNewHolder()
        {
            SupportOperationPool pool = NewPool(1);

            Assert.IsTrue(pool.TryLease(NebulaCommonVfx.RepairPulse, out var stale, out _));
            pool.Release(stale);
            Assert.IsTrue(pool.TryLease(NebulaCommonVfx.RepairPulse, out var fresh, out _));

            pool.Release(stale); // the stale lease releases nothing
            Assert.IsTrue(pool.IsHolder(fresh), "the new holder keeps its slot");
            Assert.AreEqual(1, pool.LeasedCount);
        }

        [Test]
        public void AHundredOperationCycles_NeverGrowThePool()
        {
            SupportOperationPool pool = NewPool(2);

            pool.TryLease(NebulaCommonVfx.ScanPulse, out var warm, out _);
            pool.Release(warm);
            int quadsAfterFirst = pool.TotalQuads;

            for (int i = 0; i < 100; i++)
            {
                Assert.IsTrue(pool.TryLease(NebulaCommonVfx.ScanPulse, out var lease, out _));
                pool.Release(lease);
            }

            Assert.AreEqual(quadsAfterFirst, pool.TotalQuads, "100 cycles must reuse, never allocate");
            Assert.AreEqual(0, pool.LeasedCount);
        }

        [Test]
        public void ThePoolIsBounded_ByCapacityTimesSpriteCount()
        {
            SupportOperationPool pool = NewPool(3);

            foreach (NebulaCommonVfx vfx in Enum.GetValues(typeof(NebulaCommonVfx)).Cast<NebulaCommonVfx>())
                for (int i = 0; i < 10; i++) pool.TryLease(vfx, out _, out _);

            int sprites = Enum.GetValues(typeof(NebulaCommonVfx)).Length;
            Assert.LessOrEqual(pool.TotalQuads, 3 * sprites, "the ceiling is capacity × sprites, whatever is asked for");
            Assert.LessOrEqual(pool.LeasedCount, 3 * sprites);
        }

        [Test]
        public void PooledEffectsCarryNoColliderAndNoLight()
        {
            SupportOperationPool pool = NewPool(1);
            Assert.IsTrue(pool.TryLease(NebulaCommonVfx.WarningPulse, out _, out Transform quad));
            Assert.IsNotNull(quad);

            Assert.IsNull(quad.GetComponentInChildren<Light>(true), "an operation effect must not add to the light budget");
            Assert.IsNull(quad.GetComponentInChildren<Rigidbody>(true), "scenery is never physical");

            var renderer = quad.GetComponent<MeshRenderer>();
            Assert.IsNotNull(renderer);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, renderer.shadowCastingMode);
            Assert.IsFalse(renderer.receiveShadows);
        }

        // --- versions the animation phase must not move -----------------------

        [Test]
        public void TheGenerationAndContentVersions_AreUntouched()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }

        [Test]
        public void TheSaveSchema_IsUntouched()
        {
            // The animation phase writes nothing to save. SaveService is the
            // authority (VS-016A moved it to 14); the envelope's own field
            // default stays at 13 because that is what a pre-14 file migrates
            // from, not what the game writes.
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion); // P2-SAVE-201: absent schema is unknown, never v13
        }
    }
}
