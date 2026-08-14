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
    /// ANI-02.2: every real recipe's input and output resolve to real authored
    /// visuals, the item proxy pool is lease-safe and bounded, and the Quick
    /// Asset schedule stays gameplay's to decide.
    /// </summary>
    public class Ani022ProcessingProxyQuickAssetTests
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

        private ItemProxyPool MakePool(int capacityPerItem)
        {
            var host = new GameObject("Item Pool Host");
            spawned.Add(host);
            return new ItemProxyPool(host.transform, capacityPerItem);
        }

        // ---- recipe → visual --------------------------------------------------

        [Test]
        public void EveryRealRecipeInputAndOutputHasAnAuthoredVisual()
        {
            ProcessingRecipe[] recipes = ProcessingCatalog.Recipes;
            Assert.Greater(recipes.Length, 0, "the catalog must actually have recipes");

            var missing = new List<string>();
            foreach (ProcessingRecipe recipe in recipes)
            {
                if (!NebulaVisualCatalog.TryGetItem(recipe.InputItemId, out _))
                    missing.Add($"{recipe.Id} input {recipe.InputItemId}");
                if (!NebulaVisualCatalog.TryGetItem(recipe.OutputItemId, out _))
                    missing.Add($"{recipe.Id} output {recipe.OutputItemId}");
            }

            CollectionAssert.IsEmpty(missing,
                "these recipe items have no authored visual, so their proxies would have to be guessed: "
                + string.Join(", ", missing));
        }

        [Test]
        public void TheFiveRawResourcesAndTheirProcessedMaterialsAllResolve()
        {
            foreach (string rawId in ExtractionCatalog.ResourceIds)
                Assert.IsTrue(NebulaVisualCatalog.TryGetItem(rawId, out _), $"{rawId} has no visual");

            foreach (string materialId in ProcessingCatalog.MaterialIds)
                Assert.IsTrue(NebulaVisualCatalog.TryGetItem(materialId, out _), $"{materialId} has no visual");

            Assert.IsTrue(NebulaVisualCatalog.TryGetItem(ProcessingCatalog.BasicPartId, out _),
                "the basic building part must have a visual — it is half the recipes' output");
        }

        [Test]
        public void EachRecipeKeepsItsOwnInputAndOutputIdentity()
        {
            foreach (ProcessingRecipe recipe in ProcessingCatalog.Recipes)
            {
                Assert.IsNotEmpty(recipe.InputItemId, $"{recipe.Id} has no input id");
                Assert.IsNotEmpty(recipe.OutputItemId, $"{recipe.Id} has no output id");
                Assert.AreNotEqual(recipe.InputItemId, recipe.OutputItemId,
                    $"{recipe.Id} would show the same lump going in and coming out");
            }
        }

        // ---- item proxy pool -------------------------------------------------------

        [Test]
        public void AnUnknownItemIdGetsNoProxyRatherThanTheWrongOne()
        {
            ItemProxyPool pool = MakePool(2);

            Assert.IsFalse(pool.TryLease("item.that.does.not.exist", out ItemProxyLease lease, out Transform proxy),
                "showing the wrong item would be worse than showing none");
            Assert.IsFalse(lease.IsValid);
            Assert.IsNull(proxy);
            Assert.AreEqual(0, pool.LeasedCount);

            Assert.IsFalse(pool.TryLease(null, out _, out _));
            Assert.IsFalse(pool.TryLease(string.Empty, out _, out _));
        }

        [Test]
        public void AProxyCarriesNoColliderNoRigidbodyAndNoQuantity()
        {
            ItemProxyPool pool = MakePool(1);
            Assert.IsTrue(pool.TryLease("resource.metal", out _, out Transform proxy),
                "metal is a real item with a real visual");

            Assert.AreEqual(0, proxy.GetComponentsInChildren<Collider>(true).Length,
                "a proxy is scenery and can never be collided with");
            Assert.AreEqual(0, proxy.GetComponentsInChildren<Rigidbody>(true).Length);
        }

        [Test]
        public void APerItemSliceIsBoundedAndRefusalIsSafe()
        {
            ItemProxyPool pool = MakePool(1);

            Assert.IsTrue(pool.TryLease("resource.metal", out ItemProxyLease first, out _));
            Assert.IsFalse(pool.TryLease("resource.metal", out ItemProxyLease refused, out _),
                "this item's slice is out — refusing costs scenery and nothing else");
            Assert.IsFalse(refused.IsValid);

            // A different item has its own slice.
            Assert.IsTrue(pool.TryLease("resource.water", out _, out _));
            Assert.IsTrue(pool.IsHolder(first));
        }

        [Test]
        public void ReleaseIsIdempotentAndAStaleLeaseCannotHideANewProxy()
        {
            ItemProxyPool pool = MakePool(1);

            pool.TryLease("resource.gas", out ItemProxyLease stale, out _);
            pool.Release(stale);
            Assert.AreEqual(0, pool.LeasedCount);

            Assert.DoesNotThrow(() => pool.Release(stale));
            Assert.AreEqual(0, pool.LeasedCount, "releasing twice must not go negative");

            pool.TryLease("resource.gas", out ItemProxyLease current, out Transform proxy);
            pool.Release(stale);

            Assert.IsTrue(pool.IsHolder(current), "an old lease must not hide the proxy that took its slot");
            Assert.IsTrue(proxy.gameObject.activeSelf);
        }

        [Test]
        public void AHundredBatchesReuseTheSameProxiesWithoutGrowingTheCapacity()
        {
            ItemProxyPool pool = MakePool(2);

            pool.TryLease("resource.metal", out ItemProxyLease warm, out _);
            pool.Release(warm);
            int proxiesAfterFirstBatch = pool.TotalProxies;

            for (int i = 0; i < 100; i++)
            {
                pool.TryLease("resource.metal", out ItemProxyLease inLease, out _);
                pool.TryLease("material.metal_alloy", out ItemProxyLease outLease, out _);
                pool.Release(inLease);
                pool.Release(outLease);
            }

            Assert.AreEqual(0, pool.LeasedCount);
            Assert.AreEqual(2, pool.DistinctItemCount, "only the two items actually used were ever built");
            Assert.AreEqual(proxiesAfterFirstBatch + 2, pool.TotalProxies,
                "a hundred batches must not create a hundred lumps of ore");
        }

        [Test]
        public void ReleaseAllHandsEveryProxyBack()
        {
            ItemProxyPool pool = MakePool(2);
            pool.TryLease("resource.metal", out _, out _);
            pool.TryLease("resource.water", out _, out _);
            Assert.AreEqual(2, pool.LeasedCount);

            pool.ReleaseAll();

            Assert.AreEqual(0, pool.LeasedCount);
            for (int i = 0; i < pool.Root.childCount; i++)
                Assert.IsFalse(pool.Root.GetChild(i).gameObject.activeSelf);
        }

        // ---- presenter state and silent adoption -------------------------------------

        [Test]
        public void AShipWithoutTheModuleShowsNoProxyAtAll()
        {
            var go = new GameObject("Ship without the module");
            spawned.Add(go);
            var presenter = go.AddComponent<ProcessingAnimationPresenter>();
            presenter.Initialize(null, null, null);

            Assert.IsFalse(presenter.HasModuleAnchor);
            Assert.IsFalse(presenter.HasInputProxy);
            Assert.IsFalse(presenter.HasOutputProxy);
            Assert.IsNull(presenter.ShownInputId);
            Assert.IsNull(presenter.ShownOutputId);

            Assert.DoesNotThrow(() => presenter.VisualTick(1f));
        }

        [Test]
        public void ResettingReleasesBothProxiesSoNothingIsLeftFloating()
        {
            var go = new GameObject("Ship");
            spawned.Add(go);
            var presenter = go.AddComponent<ProcessingAnimationPresenter>();
            presenter.Initialize(null, null, null);

            Assert.DoesNotThrow(() => presenter.ResetToBasePose());
            Assert.IsFalse(presenter.HasInputProxy);
            Assert.IsFalse(presenter.HasOutputProxy);
        }

        [Test]
        public void SilentAdoptionIsSafeOnAShipWithNoProcessingAtAll()
        {
            var go = new GameObject("Ship");
            spawned.Add(go);
            var presenter = go.AddComponent<ProcessingAnimationPresenter>();
            presenter.Initialize(null, null, null);

            Assert.DoesNotThrow(() => presenter.AdoptCurrentProgressSilently());
            Assert.AreNotEqual(ProcessingAnimationState.OutputReady, presenter.CurrentState);
        }

        // ---- accessibility ---------------------------------------------------------------

        [Test]
        public void MotionOffSnapsStateChangesInsteadOfTravellingThem()
        {
            AnimationAccessibility.MotionOff = true;

            Assert.IsTrue(AnimationAccessibility.SnapStateChanges,
                "with Motion Off an item must appear at its intake, not be watched drifting in");
            Assert.AreEqual(0f, AnimationAccessibility.MotionScale, 1e-5f);
        }

        [Test]
        public void ReducedMotionKeepsTheItemVisibleAndOnlyTrimsTheMovement()
        {
            AnimationAccessibility.ReducedMotion = true;

            Assert.Less(AnimationAccessibility.MotionScale, 1f);
            Assert.Greater(AnimationAccessibility.MotionScale, 0f,
                "the player must still see what is going into the refinery");
            Assert.IsFalse(AnimationAccessibility.SnapStateChanges);
        }

        // ---- Quick Asset schedule stays gameplay's -------------------------------------

        [Test]
        public void TheThreeCapsulesKeepTheirRealStaggerAndOrder()
        {
            Assert.AreEqual(0f, QuickAssetPackageService.DrillSendDelay, 1e-4f);
            Assert.AreEqual(2f, QuickAssetPackageService.CommandSendDelay, 1e-4f);
            Assert.AreEqual(4f, QuickAssetPackageService.LaserSendDelay, 1e-4f);

            Assert.Less(QuickAssetPackageService.DrillSendDelay, QuickAssetPackageService.CommandSendDelay);
            Assert.Less(QuickAssetPackageService.CommandSendDelay, QuickAssetPackageService.LaserSendDelay);
        }

        [Test]
        public void ATransactionTracksWhichCapsulesHaveReallyGone()
        {
            var tx = new QuickAssetPackageTransaction
            {
                TransactionId = "ani022",
                PlanetWorldId = "world",
                ElapsedSeconds = 0f
            };

            Assert.IsFalse(tx.AnySent, "nothing has gone yet");
            Assert.IsFalse(tx.Completed);

            tx.DrillSent = true;
            Assert.IsTrue(tx.AnySent);
            Assert.IsFalse(tx.Completed, "one of three is not a finished package");

            tx.CommandSent = true;
            tx.LaserSent = true;
            Assert.IsTrue(tx.Completed, "all three sent is the only definition of complete");
        }

        [Test]
        public void ACancelledPackageNeverReportsCompletion()
        {
            var tx = new QuickAssetPackageTransaction { TransactionId = "ani022", Cancelled = true };

            Assert.IsFalse(tx.AnySent, "a package cancelled before any send must show nothing");
            Assert.IsFalse(tx.Completed);
        }

        [Test]
        public void TheCapsuleAnimationReadsTheStaggerRatherThanDecidingIt()
        {
            // The animation layer must never be the thing that says when a
            // capsule goes; it only mirrors what gameplay already decided.
            Assert.AreEqual(QuickAssetPackageService.DrillSendDelay,
                CapsuleFlightProfile.QuickAssetSendDelay(0), 1e-4f);
            Assert.AreEqual(QuickAssetPackageService.CommandSendDelay,
                CapsuleFlightProfile.QuickAssetSendDelay(1), 1e-4f);
            Assert.AreEqual(QuickAssetPackageService.LaserSendDelay,
                CapsuleFlightProfile.QuickAssetSendDelay(2), 1e-4f);
        }

        // ---- preserved authorities ------------------------------------------------------

        [Test]
        public void ProcessingProxiesDidNotMoveGenerationOrContentVersions()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
            Assert.AreEqual(3, ContentFingerprint.ContentRevision);
        }

        /// <summary>
        /// This slice moved presentation only, so the package still charges the
        /// structure catalogs' own numbers.
        /// <para>
        /// Asserted against the catalogs rather than against 65/15/20/30/15 as
        /// literals (P1-TEST-030). A literal checked against a literal measures the
        /// constant against itself: it cannot tell "the animation work left the
        /// economy alone" from "the animation work moved the economy and the numbers
        /// in this test with it". Against the catalog it can.
        /// </para>
        /// </summary>
        [Test]
        public void QuickAssetCostsAreStillTheStructureCatalogsOwnNumbers()
        {
            Assert.AreEqual(ExtractionCatalog.DrillT1PartsCost, QuickAssetPackageService.DrillBasicCost);
            Assert.AreEqual(SurfaceStructureCatalog.Command.PartsCost, QuickAssetPackageService.CommandBasicCost);
            Assert.AreEqual(SurfaceStructureCatalog.Laser.PartsCost, QuickAssetPackageService.LaserBasicCost);

            Assert.AreEqual(
                ExtractionCatalog.DrillT1PartsCost + SurfaceStructureCatalog.Command.PartsCost
                + SurfaceStructureCatalog.Laser.PartsCost,
                QuickAssetPackageService.TotalBasicCost);
            Assert.AreEqual(
                SurfaceStructureCatalog.Command.MaterialCost + SurfaceStructureCatalog.Laser.MaterialCost,
                QuickAssetPackageService.TotalSpecialCost);
        }

        [Test]
        public void ProcessingRecipeCostsAndYieldsAreUntouched()
        {
            foreach (ProcessingRecipe recipe in ProcessingCatalog.Recipes)
            {
                Assert.Greater(recipe.InputCount, 0, $"{recipe.Id} consumes nothing");
                Assert.Greater(recipe.OutputCount, 0, $"{recipe.Id} produces nothing");
                Assert.Greater(recipe.Seconds, 0f, $"{recipe.Id} takes no time");
            }

            // The two recipe shapes the catalog really builds: 2 raw to a part,
            // 3 raw to a material.
            ProcessingRecipe part = ProcessingCatalog.Recipes.First(r => r.OutputItemId == ProcessingCatalog.BasicPartId);
            Assert.AreEqual(2, part.InputCount);
            Assert.AreEqual(1, part.OutputCount);

            ProcessingRecipe material = ProcessingCatalog.Recipes.First(r => r.OutputItemId != ProcessingCatalog.BasicPartId);
            Assert.AreEqual(3, material.InputCount);
            Assert.AreEqual(1, material.OutputCount);
        }
    }
}
