using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Logistics;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the Logistics Center's pure lifecycle/durability/upgrade state machine (VS-011 §2/§10.2/§10.4/§10.13).</summary>
    public class LogisticsCenterRuntimeTests
    {
        [Test]
        public void FreshBuild_StartsConstructing_NoEnvironmentMultiplier_50PercentDurability()
        {
            var runtime = new LogisticsCenterRuntime(LogisticsCenterCatalog.T1);
            Assert.AreEqual(LogisticsCenterState.Constructing, runtime.State);
            Assert.AreEqual(LogisticsCenterTier.T1, runtime.Tier);
            Assert.AreEqual(180f, runtime.BuildRemaining, "orbital build never scales by planet environment");
            Assert.AreEqual(0f, runtime.Durability.Shield);
            Assert.AreEqual(400f, runtime.Durability.Armor);
            Assert.AreEqual(600f, runtime.Durability.Hull);
        }

        [Test]
        public void Constructing_BecomesActive_WhenBuildRemainingElapses()
        {
            var runtime = new LogisticsCenterRuntime(LogisticsCenterCatalog.T1);
            runtime.Tick(179f);
            Assert.AreEqual(LogisticsCenterState.Constructing, runtime.State);
            runtime.Tick(2f);
            Assert.AreEqual(LogisticsCenterState.Active, runtime.State);
            Assert.AreEqual(0f, runtime.BuildRemaining);
        }

        [Test]
        public void CanCancelForRefund_OnlyWhileConstructing()
        {
            var runtime = new LogisticsCenterRuntime(LogisticsCenterCatalog.T1);
            Assert.IsTrue(runtime.CanCancelForRefund);
            runtime.Tick(180f);
            Assert.AreEqual(LogisticsCenterState.Active, runtime.State);
            Assert.IsFalse(runtime.CanCancelForRefund);
        }

        [Test]
        public void T1StaysFullyActive_WhileT2UpgradeInProgress()
        {
            var runtime = new LogisticsCenterRuntime(LogisticsCenterCatalog.T1);
            runtime.Tick(180f); // -> Active, T1

            Assert.IsTrue(runtime.CanStartUpgrade);
            runtime.StartUpgrade();
            Assert.IsTrue(runtime.Upgrading);
            Assert.AreEqual(LogisticsCenterTier.T1, runtime.Tier, "T1 keeps working the whole time the upgrade is in progress");
            Assert.IsTrue(runtime.IsActive);
            Assert.AreEqual(600f, runtime.Def.Capacity, "T1 capacity/routes/docks/transfer stay in effect until the upgrade actually completes");

            runtime.Tick(300f); // not yet done (360s total)
            Assert.AreEqual(LogisticsCenterTier.T1, runtime.Tier);
            Assert.IsTrue(runtime.Upgrading);
        }

        [Test]
        public void UpgradeCompletion_JumpsCapacityRoutesDocksAtomically_PreservingDurabilityFraction()
        {
            var runtime = new LogisticsCenterRuntime(LogisticsCenterCatalog.T1);
            runtime.Tick(180f); // Active T1
            runtime.Durability.Repair(1000f); // full 400/800/1200
            Assert.AreEqual(800f, runtime.Durability.Armor);
            Assert.AreEqual(1200f, runtime.Durability.Hull);

            runtime.StartUpgrade();
            runtime.Tick(360f);

            Assert.AreEqual(LogisticsCenterTier.T2, runtime.Tier);
            Assert.IsFalse(runtime.Upgrading);
            Assert.AreEqual(1800f, runtime.Def.Capacity);
            Assert.AreEqual(5, runtime.Def.MaxRoutes);
            Assert.AreEqual(2, runtime.Def.DockCount);
            Assert.AreEqual(40f, runtime.Def.TransferVolumePerSecond);
            // Armor/Hull were at 100% of T1's max -> must read 100% of T2's max (fraction preserved, not raw value).
            Assert.AreEqual(1600f, runtime.Durability.Armor, 0.01f);
            Assert.AreEqual(2400f, runtime.Durability.Hull, 0.01f);
        }

        [Test]
        public void CancelUpgrade_ClearsInProgressFlag_T1KeepsWorking()
        {
            var runtime = new LogisticsCenterRuntime(LogisticsCenterCatalog.T1);
            runtime.Tick(180f);
            runtime.StartUpgrade();
            runtime.Tick(50f);

            runtime.CancelUpgrade();

            Assert.IsFalse(runtime.Upgrading);
            Assert.AreEqual(0f, runtime.UpgradeRemaining);
            Assert.AreEqual(LogisticsCenterTier.T1, runtime.Tier);
            Assert.IsTrue(runtime.IsActive);
        }

        [Test]
        public void HullReachingZero_Disables_NeverDestroys()
        {
            var runtime = new LogisticsCenterRuntime(LogisticsCenterCatalog.T1);
            runtime.Tick(180f); // Active
            runtime.ApplyDamage(100000f);
            Assert.AreEqual(LogisticsCenterState.Disabled, runtime.State);
            Assert.IsTrue(runtime.Durability.Disabled);
        }

        [Test]
        public void Repair_BringsDisabledBackToActive()
        {
            var runtime = new LogisticsCenterRuntime(LogisticsCenterCatalog.T1);
            runtime.Tick(180f);
            runtime.ApplyDamage(100000f);
            Assert.AreEqual(LogisticsCenterState.Disabled, runtime.State);

            runtime.Repair(1000f);
            Assert.AreEqual(LogisticsCenterState.Active, runtime.State);
        }

        [Test]
        public void CanDismantle_RequiresEmptyInventoryNoActiveUnloadNoLoadedWaitingCollector()
        {
            var runtime = new LogisticsCenterRuntime(LogisticsCenterCatalog.T1);
            runtime.Tick(180f);

            Assert.IsTrue(runtime.CanDismantle(inventoryEmpty: true, noActiveUnloadTransaction: true, noLoadedWaitingCollector: true));
            Assert.IsFalse(runtime.CanDismantle(inventoryEmpty: false, noActiveUnloadTransaction: true, noLoadedWaitingCollector: true));
            Assert.IsFalse(runtime.CanDismantle(inventoryEmpty: true, noActiveUnloadTransaction: false, noLoadedWaitingCollector: true));
            Assert.IsFalse(runtime.CanDismantle(inventoryEmpty: true, noActiveUnloadTransaction: true, noLoadedWaitingCollector: false));
        }

        [Test]
        public void RestoreConstructor_RoundTripsExactState()
        {
            var durability = new Durability(400f, 800f, 1200f, 0f);
            durability.RestoreFromSave(120f, 500f, 900f, 3f);
            var runtime = new LogisticsCenterRuntime(LogisticsCenterTier.T1, LogisticsCenterState.Active, 0f, durability, true, 42f);

            Assert.AreEqual(LogisticsCenterTier.T1, runtime.Tier);
            Assert.AreEqual(LogisticsCenterState.Active, runtime.State);
            Assert.IsTrue(runtime.Upgrading);
            Assert.AreEqual(42f, runtime.UpgradeRemaining);
            Assert.AreEqual(120f, runtime.Durability.Shield);
        }
    }
}
