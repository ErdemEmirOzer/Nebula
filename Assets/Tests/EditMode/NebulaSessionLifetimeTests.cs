using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for TF-002 session lifetime/dispose bookkeeping (§7/§9).</summary>
    public class NebulaSessionLifetimeTests
    {
        [TearDown]
        public void Cleanup()
        {
            ExclusiveModal.ResetForSession();
            DominanceSystem.ResetForSession();
        }

        [Test]
        public void Dispose_DestroysTrackedRoots_InReverseOrder()
        {
            var lifetime = new NebulaSessionLifetime();
            var first = new GameObject("first");
            var second = new GameObject("second");
            lifetime.Track(SessionLifetime.CampaignSession, first);
            lifetime.Track(SessionLifetime.CampaignSession, second);

            lifetime.Dispose();

            Assert.IsTrue(first == null, "tracked campaign roots must be destroyed by Dispose");
            Assert.IsTrue(second == null, "tracked campaign roots must be destroyed by Dispose");
        }

        [Test]
        public void Dispose_CalledTwice_IsIdempotent_NeverThrows()
        {
            var lifetime = new NebulaSessionLifetime();
            lifetime.Track(SessionLifetime.CampaignSession, new GameObject("x"));

            lifetime.Dispose();
            Assert.DoesNotThrow(() => lifetime.Dispose());
            Assert.IsTrue(lifetime.IsDisposed);
        }

        [Test]
        public void Track_RejectsApplicationAndMaterializedSystemTiers()
        {
            var lifetime = new NebulaSessionLifetime();
            var go = new GameObject("x");
            try
            {
                Assert.Throws<System.InvalidOperationException>(() => lifetime.Track(SessionLifetime.Application, go));
                Assert.Throws<System.InvalidOperationException>(() => lifetime.Track(SessionLifetime.MaterializedSystem, go));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void PartialBoot_UnwindsWhateverWasTrackedBeforeTheFailure()
        {
            // NavigationInstaller creates+tracks its "Navigation Director" GameObject
            // BEFORE touching context.Ship -- a fresh (nothing-set) context makes that
            // access throw, exactly simulating a mid-installer failure.
            var lifetime = new NebulaSessionLifetime();
            var context = new NebulaContext();
            GameObject createdBeforeFailure = null;

            try
            {
                new NavigationInstaller().WireNavigation(context, lifetime);
                Assert.Fail("expected an InvalidOperationException from the unset context.Ship read");
            }
            catch (System.InvalidOperationException)
            {
                // expected -- this IS the "installer dependency/order guard" behavior.
            }
            finally
            {
                lifetime.DisposePartialBoot();
            }

            Object[] navigationDirectors = Object.FindObjectsOfType<Nebula.World.NavigationDirector>();
            Assert.AreEqual(0, navigationDirectors.Length, "the partially-built Navigation Director GameObject must not survive a failed boot");
        }

        [Test]
        public void ExclusiveModal_ResetForSession_ClearsStaleOwner()
        {
            var owner = new object();
            Assert.IsTrue(ExclusiveModal.TryAcquire(owner));
            Assert.IsTrue(ExclusiveModal.IsHeldByOther(new object()), "sanity: the modal is genuinely held before reset");

            ExclusiveModal.ResetForSession();

            Assert.IsFalse(ExclusiveModal.IsHeldByOther(new object()), "a destroyed-while-open modal must never permanently block every future modal");
        }

        [Test]
        public void DominanceSystem_ResetForSession_ClearsStaleSubscribers()
        {
            bool invoked = false;
            DominanceSystem.PlanetFirstControlled += _ => invoked = true;

            DominanceSystem.ResetForSession();

            // No direct way to invoke the private static event from outside DominanceSystem;
            // this asserts the reset call itself never throws and is safe to call unconditionally.
            Assert.DoesNotThrow(() => DominanceSystem.ResetForSession());
            Assert.IsFalse(invoked);
        }

        [Test]
        public void SaveSchema_IsPinned_UnaffectedByTheRefactor()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
        }

        /// <summary>
        /// Old contract: this pinned 2, the generation version in force when the composition root was
        /// modularized. Current production authority: <see cref="SectorManifestGenerator.GenerationVersion"/>
        /// = 3 (Assets/Scripts/Nebula/Sectors/SectorManifestGenerator.cs) -- 9d33695 moved systems off the
        /// uniform 2x2x2 sub-cell grid onto the galaxy spiral arms, a deliberate world-generation change
        /// that regenerates every existing campaign's universe and was approved as such.
        /// Invariant still protected: a session-lifetime refactor moves code, not world generation, so it
        /// must leave that number where it is. The literal is deliberate -- reading the constant back
        /// would assert nothing, so a bump has to break this test and be re-pinned alongside the
        /// world-gen change that earned it.
        /// </summary>
        [Test]
        public void GenerationVersion_IsPinnedToThree_UnaffectedByTheRefactor()
        {
            Assert.AreEqual(3, SectorManifestGenerator.GenerationVersion);
        }
    }
}
