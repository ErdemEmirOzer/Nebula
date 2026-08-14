using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-02.2 in the real booted scene: processing proxies come from real
    /// recipe ids, and the Quick Asset schedule is driven by the real package
    /// state rather than by anything in the animation layer.
    /// </summary>
    public class Ani022ProcessingProxyQuickAssetPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        private IEnumerator BootScene()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it -- explicitly, through the same idempotent
            // entry the menu's New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            UnityEngine.Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        [TearDown]
        public void TearDown() => AnimationAccessibility.ResetToDefaults();

        private static NebulaAnimationDirector Director()
        {
            var director = UnityEngine.Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(director, "the animation director must exist after boot");
            return director;
        }

        // ---- the item proxy pool in a live session -------------------------------

        [UnityTest]
        public IEnumerator TheSessionHasAnItemProxyPoolSeparateFromTheCapsulePool()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();

            Assert.IsNotNull(director.ItemProxies, "processing needs its own pool");
            Assert.IsNotNull(director.CapsuleProxies);
            Assert.AreNotSame((object)director.ItemProxies, (object)director.CapsuleProxies,
                "the two pools hold different things and must never lease each other's");
            Assert.AreEqual(0, director.ItemProxies.LeasedCount, "a fresh session has nothing in flight");
        }

        [UnityTest]
        public IEnumerator EveryRealRecipeItemResolvesToAProxyInALiveSession()
        {
            yield return BootScene();

            ItemProxyPool pool = Director().ItemProxies;
            var leases = new List<ItemProxyLease>();
            try
            {
                foreach (string rawId in ExtractionCatalog.ResourceIds)
                {
                    Assert.IsTrue(pool.TryLease(rawId, out ItemProxyLease lease, out Transform proxy),
                        $"{rawId} must resolve to a real authored visual");
                    Assert.IsNotNull(proxy);
                    Assert.AreEqual(0, proxy.GetComponentsInChildren<Collider>(true).Length,
                        $"{rawId} proxy must be scenery, never collidable");
                    leases.Add(lease);
                }
            }
            finally
            {
                foreach (ItemProxyLease lease in leases) pool.Release(lease);
            }

            Assert.AreEqual(0, pool.LeasedCount);
        }

        [UnityTest]
        public IEnumerator AHundredBatchesLeaveThePoolTheSameSizeAndAddNoLight()
        {
            yield return BootScene();

            ItemProxyPool pool = Director().ItemProxies;
            int lightsBefore = UnityEngine.Object.FindObjectsOfType<Light>(true).Length;

            pool.TryLease("resource.metal", out ItemProxyLease warm, out _);
            pool.Release(warm);
            int proxiesAfterWarmUp = pool.TotalProxies;

            for (int i = 0; i < 100; i++)
            {
                pool.TryLease("resource.metal", out ItemProxyLease inLease, out _);
                pool.TryLease("material.metal_alloy", out ItemProxyLease outLease, out _);
                pool.Release(inLease);
                pool.Release(outLease);
            }

            Assert.AreEqual(0, pool.LeasedCount);
            Assert.AreEqual(proxiesAfterWarmUp + 2, pool.TotalProxies,
                "a hundred batches must not create a hundred lumps of ore");
            Assert.AreEqual(lightsBefore, UnityEngine.Object.FindObjectsOfType<Light>(true).Length,
                "a proxy must never add a Light");

            yield return null;
        }

        // ---- the processing presenter against the ship's real slots ------------------

        [UnityTest]
        public IEnumerator TheShipHasOneProcessingPresenterBoundToTheRealPool()
        {
            yield return BootScene();

            var presenters = UnityEngine.Object.FindObjectsOfType<ProcessingAnimationPresenter>();
            Assert.AreEqual(1, presenters.Length, "one processing presenter per session");

            var processing = UnityEngine.Object.FindObjectOfType<ShipProcessing>();
            Assert.IsNotNull(processing?.Bench, "and it must have the real bench to read");
        }

        [UnityTest]
        public IEnumerator AnIdleModuleShowsNoProxyAndTouchesNoCargoValue()
        {
            yield return BootScene();

            var presenter = UnityEngine.Object.FindObjectOfType<ProcessingAnimationPresenter>();
            var processing = UnityEngine.Object.FindObjectOfType<ShipProcessing>();
            NebulaAnimationDirector director = Director();

            // VS-017A.2: the slot bank is gone; the presenter reads the bench instead.
            long conversionsBefore = processing.Bench.ConversionsTotal;
            int benchBefore = processing.Bench.InputAmount;
            int leasedBefore = director.ItemProxies.LeasedCount;

            for (int i = 0; i < 300; i++) director.Tick(1f / 60f);

            Assert.AreEqual(ProcessingAnimationState.Idle, presenter.CurrentState);
            Assert.IsFalse(presenter.HasInputProxy, "an idle refinery has nothing coming in");
            Assert.IsFalse(presenter.HasOutputProxy);
            Assert.AreEqual(leasedBefore, director.ItemProxies.LeasedCount);

            Assert.AreEqual(conversionsBefore, processing.Bench.ConversionsTotal,
                "animation must never produce a conversion");
            Assert.AreEqual(benchBefore, processing.Bench.InputAmount);
        }

        [UnityTest]
        public IEnumerator ResettingThePresenterReleasesEveryProxyItHeld()
        {
            yield return BootScene();

            var presenter = UnityEngine.Object.FindObjectOfType<ProcessingAnimationPresenter>();
            NebulaAnimationDirector director = Director();
            int leasedBefore = director.ItemProxies.LeasedCount;

            presenter.ResetToBasePose();

            Assert.IsFalse(presenter.HasInputProxy);
            Assert.IsFalse(presenter.HasOutputProxy);
            Assert.AreEqual(leasedBefore, director.ItemProxies.LeasedCount,
                "a cancel or module rebuild must leave nothing floating beside the module");

            yield return null;
        }

        // ---- Quick Asset: the real driver on the real schedule ------------------------

        /// <summary>
        /// Drives the real <see cref="ShipQuickAssetPackage"/> through a
        /// restored transaction — the same production path a loaded save uses
        /// to resume a package — and watches which capsules it really sends.
        /// </summary>
        private static ShipQuickAssetPackage RestoreActivePackage(string worldId)
        {
            var package = UnityEngine.Object.FindObjectOfType<ShipQuickAssetPackage>();
            Assert.IsNotNull(package, "the ship must carry the quick-asset driver");

            // Stocked the way the real load path stocks it. Since Part 3 a restore
            // re-reserves everything the package still owes (ShipQuickAssetPackage.cs:363-370),
            // and ExtractionDirector restores cargo *before* it restores the package
            // (ExtractionDirector.cs:499-509 then :683) precisely so that reservation can
            // succeed. A fresh campaign hold carries no Basic Parts, so this helper was
            // handing every test below a package that had already been refused --
            // reported as "the restored package must be live" against a null transaction,
            // which is the fail-closed behaviour AuditPart2TransactionTests
            // .P2ECO107_RestoredQuickPackage_MustChargeEveryUnsentNode requires. The
            // sibling fixture UiFinalInputRoutingTests.LivePackage already stocks the hold
            // for the same reason; this one never did.
            LogisticsCargo hold = package.GetComponent<ShipLogistics>()?.Cargo;
            Assert.IsNotNull(hold, "the ship carries no cargo hold to reserve against");
            foreach (string itemId in new[] { ProcessingCatalog.BasicPartId, ProcessingCatalog.MaterialIds[0] })
                hold.LoadItem(itemId, 500f);

            package.RestoreTransaction(new QuickPackageDto
            {
                active = true,
                transactionId = "ani022-package",
                planetWorldId = worldId,
                commandMaterialId = ProcessingCatalog.MaterialIds[0],
                laserMaterialId = ProcessingCatalog.MaterialIds[0],
                drillSent = false,
                commandSent = false,
                laserSent = false,
                elapsedSeconds = 0f
            });

            return package;
        }

        [UnityTest]
        public IEnumerator ARestoredPackageStartsWithNothingSentAndOnTheRealSchedule()
        {
            yield return BootScene();

            var planet = UnityEngine.Object.FindObjectOfType<PlanetIdentity>();
            Assert.IsNotNull(planet, "the Full system must have materialized planets");

            ShipQuickAssetPackage package = RestoreActivePackage(planet.WorldId);
            QuickAssetPackageTransaction tx = package.ActiveTransaction;

            Assert.IsNotNull(tx, "the restored package must be live");
            Assert.IsFalse(tx.AnySent, "a package resumed at t=0 has sent nothing yet");
            Assert.IsFalse(tx.Completed);
            Assert.AreEqual(0f, tx.ElapsedSeconds, 1e-4f);

            // The schedule the driver will follow is gameplay's, not animation's.
            Assert.AreEqual(0f, QuickAssetPackageService.DrillSendDelay, 1e-4f);
            Assert.AreEqual(2f, QuickAssetPackageService.CommandSendDelay, 1e-4f);
            Assert.AreEqual(4f, QuickAssetPackageService.LaserSendDelay, 1e-4f);

            package.Cancel();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CancellingAPackageLeavesNoCapsuleProxyBehind()
        {
            yield return BootScene();

            var planet = UnityEngine.Object.FindObjectOfType<PlanetIdentity>();
            NebulaAnimationDirector director = Director();
            director.CapsuleProxies.ReleaseAll();
            int leasedBefore = director.CapsuleProxies.LeasedCount;

            ShipQuickAssetPackage package = RestoreActivePackage(planet.WorldId);
            package.Cancel();
            yield return null;
            director.Tick(1f);

            Assert.AreEqual(leasedBefore, director.CapsuleProxies.LeasedCount,
                "a cancelled package must not leave a capsule in the sky");

            QuickAssetPackageTransaction tx = package.ActiveTransaction;
            if (tx != null) Assert.IsFalse(tx.AnySent, "and it must not have sent anything");
        }

        [UnityTest]
        public IEnumerator EachStructureCreationPathAttachesExactlyOneCapsulePresenter()
        {
            yield return BootScene();

            // The three capsules the package sends go through these creation
            // calls; each must produce one presenter and no more.
            var planet = UnityEngine.Object.FindObjectOfType<PlanetIdentity>();
            PlanetProfile profile = ExtractionState.GetProfile(planet);
            var ship = UnityEngine.Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(profile);
            Assert.IsNotNull(ship);

            DrillStructure drill = DrillStructure.Create(planet, profile, ship.transform.position,
                DrillStructure.BuildSource.Parts, refundParts: 0);
            try
            {
                Assert.AreEqual(1, drill.GetComponents<BuildCapsulePresenter>().Length,
                    "the drill path (t=0) must produce exactly one capsule presenter");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(drill.gameObject);
            }

            SurfaceStructure command = SurfaceStructure.CreateWithFlatRefund(planet, profile,
                SurfaceStructureCatalog.Command, profile.CommandNode, ship.transform.position,
                new List<(string itemId, float amount)>());
            try
            {
                Assert.AreEqual(1, command.GetComponents<BuildCapsulePresenter>().Length,
                    "the command path (t=2) must produce exactly one");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(command.gameObject);
            }

            SurfaceStructure laser = SurfaceStructure.CreateWithFlatRefund(planet, profile,
                SurfaceStructureCatalog.Laser, profile.FirstDefenseNode, ship.transform.position,
                new List<(string itemId, float amount)>());
            try
            {
                Assert.AreEqual(1, laser.GetComponents<BuildCapsulePresenter>().Length,
                    "the laser path (t=4) must produce exactly one");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(laser.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator ThreeSimultaneousCapsulesFitAndAFourthIsRefusedWithoutAffectingGameplay()
        {
            yield return BootScene();

            NebulaAnimationDirector director = Director();
            director.CapsuleProxies.ReleaseAll();

            var leases = new List<CapsuleProxyLease>();
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(director.CapsuleProxies.TryLease(out CapsuleProxyLease lease, out _),
                    $"quick-asset capsule {i} must get a proxy");
                leases.Add(lease);
            }

            Assert.IsFalse(director.CapsuleProxies.TryLease(out _, out _),
                "a fourth is refused — scenery only, the package is unaffected");
            Assert.AreEqual(65, QuickAssetPackageService.TotalBasicCost,
                "and the package's cost is untouched by any of it");

            foreach (CapsuleProxyLease lease in leases) director.CapsuleProxies.Release(lease);
            Assert.AreEqual(0, director.CapsuleProxies.LeasedCount);

            yield return null;
        }

        // ---- teardown ----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator RestartLeavesOnePoolOnePresenterAndNothingLeased()
        {
            yield return BootScene();

            var bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap);

            bootstrap.RestartCampaign();
            yield return null;
            yield return null;

            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<ProcessingAnimationPresenter>().Length,
                "a restart must not accumulate processing presenters");

            NebulaAnimationDirector director = Director();
            Assert.IsNotNull(director.ItemProxies, "the new session gets its own item pool");
            Assert.AreEqual(0, director.ItemProxies.LeasedCount,
                "and nothing is leased in it from the session before");
            Assert.AreEqual(0, director.CapsuleProxies.LeasedCount);
        }

        [UnityTest]
        public IEnumerator TheWorldStillRunsOnOneKeyLightPlusOneHullFill()
        {
            yield return BootScene();

            Light[] worldLights = UnityEngine.Object.FindObjectsOfType<Light>()
                .Where(l => l != null && l.gameObject.activeInHierarchy && l.cullingMask == -1)
                .ToArray();

            Assert.AreEqual(2, worldLights.Length,
                "ANI-02.2 must not have disturbed the lighting budget: "
                + string.Join(", ", worldLights.Select(l => l.gameObject.name)));
        }
    }
}
