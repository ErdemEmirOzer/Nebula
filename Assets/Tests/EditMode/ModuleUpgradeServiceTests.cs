using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Progression;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for the atomic module-upgrade transaction (VS-005 §8,
    /// tests 4, 6-19). Everything here is plain C# — no scene/GameObject
    /// needed, matching how ModuleUpgradeService/LogisticsCargo/ProgressionState
    /// are designed to be used.
    /// </summary>
    public class ModuleUpgradeServiceTests
    {
        private const string CargoId = "industry.logistics_cargo";
        private const string ProcId = "industry.resource_processing";

        private static ProgressionState MakeProgression(int level, int points)
        {
            var p = new ProgressionState();
            long xp = level <= 1 ? 0L : ProgressionMath.TotalXpToReach(level);
            p.Restore(xp, points);
            return p;
        }

        private static LogisticsCargo RichCargo()
        {
            var cargo = new LogisticsCargo(999999f);
            cargo.LoadItem(ProcessingCatalog.BasicPartId, 999999f);
            foreach (string m in ProcessingCatalog.MaterialIds) cargo.LoadItem(m, 999999f);
            cargo.LoadItem(ModuleCatalog.EnergyNuclearId, 999999f);
            cargo.LoadItem(ModuleCatalog.CoreNuclearId, 999999f);
            return cargo;
        }

        [Test]
        public void NewCampaign_GrantsThreeFreeIndustryT1_RestT0()
        {
            var modules = new ModuleProgressionState();
            modules.GrantFreeStartingModules();
            foreach (string id in ModuleCatalog.FreeStartingModuleIds) Assert.AreEqual(1, modules.GetTier(id), id);
            foreach (ModuleDef def in ModuleCatalog.Modules)
            {
                if (System.Array.IndexOf(ModuleCatalog.FreeStartingModuleIds, def.Id) >= 0) continue;
                Assert.AreEqual(0, modules.GetTier(def.Id), def.Id);
            }
        }

        [Test]
        public void NonFunctionalModule_RejectsWithNotImplemented_AndChangesNoState()
        {
            var modules = new ModuleProgressionState();
            var progression = MakeProgression(100, 999);
            LogisticsCargo cargo = RichCargo();
            var service = new ModuleUpgradeService(modules, progression, cargo);

            int pointsBefore = progression.DevelopmentPoints;
            float partsBefore = cargo.GetStored(ProcessingCatalog.BasicPartId);

            // exploration.scanner (VS-006), exploration.hangar_bot (VS-007), and
            // defense.hull_reinforcement/defense.weapon_coordination/defense.shield_systems
            // (VS-013/VS-013.1) all became functional — nuclear.star_energy_storage is
            // still genuinely MaxTierThisSlice=0 (no consumer implemented this slice),
            // so it's the one used here.
            UpgradeRejectReason reason = service.TryStartUpgrade("nuclear.star_energy_storage");

            Assert.AreEqual(UpgradeRejectReason.NotImplemented, reason);
            Assert.AreEqual(pointsBefore, progression.DevelopmentPoints);
            Assert.AreEqual(partsBefore, cargo.GetStored(ProcessingCatalog.BasicPartId));
            Assert.AreEqual(0, modules.GetTier("nuclear.star_energy_storage"));
            Assert.IsNull(service.ActiveJob);
        }

        [Test]
        public void InsufficientLevel_RejectsAtomically()
        {
            var modules = new ModuleProgressionState();
            modules.SetTier(CargoId, 1); // T2 requires level 21
            var progression = MakeProgression(1, 10);
            LogisticsCargo cargo = RichCargo();
            var service = new ModuleUpgradeService(modules, progression, cargo);

            UpgradeRejectReason reason = service.TryStartUpgrade(CargoId);

            Assert.AreEqual(UpgradeRejectReason.LevelTooLow, reason);
            Assert.AreEqual(10, progression.DevelopmentPoints);
            Assert.IsNull(service.ActiveJob);
        }

        [Test]
        public void InsufficientMilestone_RejectsAtomically()
        {
            // No functional module in this slice carries a real milestone gate (see report);
            // the validator's milestone parameter is exercised directly here instead.
            var modules = new ModuleProgressionState();
            var progression = MakeProgression(100, 10);
            LogisticsCargo cargo = RichCargo();
            var service = new ModuleUpgradeService(modules, progression, cargo);

            UpgradeRejectReason reason = service.TryStartUpgrade(CargoId, milestoneSatisfied: false);

            Assert.AreEqual(UpgradeRejectReason.MilestoneNotMet, reason);
            Assert.AreEqual(10, progression.DevelopmentPoints);
        }

        [Test]
        public void InsufficientPoints_RejectsAtomically()
        {
            var modules = new ModuleProgressionState();
            var progression = MakeProgression(100, 0);
            LogisticsCargo cargo = RichCargo();
            var service = new ModuleUpgradeService(modules, progression, cargo);

            UpgradeRejectReason reason = service.TryStartUpgrade(ProcId); // T1 costs 1 point

            Assert.AreEqual(UpgradeRejectReason.InsufficientPoints, reason);
            Assert.AreEqual(0, progression.DevelopmentPoints);
        }

        [Test]
        public void InsufficientResources_RejectsAtomically_AndRefundsAlreadySpentPoints()
        {
            var modules = new ModuleProgressionState();
            var progression = MakeProgression(100, 10);
            var cargo = new LogisticsCargo(999f); // no part.basic at all
            var service = new ModuleUpgradeService(modules, progression, cargo);

            UpgradeRejectReason reason = service.TryStartUpgrade(ProcId);

            Assert.AreEqual(UpgradeRejectReason.InsufficientResources, reason);
            Assert.AreEqual(10, progression.DevelopmentPoints, "points must roll back on resource failure");
            Assert.IsNull(service.ActiveJob);
        }

        [Test]
        public void FullQueue_RejectsSecondUpgrade()
        {
            var modules = new ModuleProgressionState();
            var progression = MakeProgression(100, 10);
            LogisticsCargo cargo = RichCargo();
            var service = new ModuleUpgradeService(modules, progression, cargo);

            Assert.AreEqual(UpgradeRejectReason.None, service.TryStartUpgrade(CargoId));
            int pointsAfterFirst = progression.DevelopmentPoints;

            UpgradeRejectReason reason = service.TryStartUpgrade(ProcId);

            Assert.AreEqual(UpgradeRejectReason.QueueBusy, reason);
            Assert.AreEqual(pointsAfterFirst, progression.DevelopmentPoints);
        }

        [Test]
        public void TierSkip_IsRejected()
        {
            ModuleDef def = ModuleCatalog.Get(CargoId);
            LogisticsCargo cargo = RichCargo();
            UpgradeRejectReason reason = ModuleUpgradeValidator.Validate(def, currentTier: 0, targetTier: 3,
                characterLevel: 100, milestoneSatisfied: true, queueBusy: false, availablePoints: 99, cargo: cargo);
            Assert.AreEqual(UpgradeRejectReason.TierSkip, reason);
        }

        [Test]
        public void OnlyOneActiveUpgradeJobAtATime()
        {
            var modules = new ModuleProgressionState();
            var progression = MakeProgression(100, 10);
            LogisticsCargo cargo = RichCargo();
            var service = new ModuleUpgradeService(modules, progression, cargo);

            service.TryStartUpgrade(CargoId);
            Assert.IsNotNull(service.ActiveJob);
            Assert.AreEqual(CargoId, service.ActiveJob.ModuleId);
        }

        [Test]
        public void OldTierEffectStaysActiveWhileUpgrading()
        {
            var modules = new ModuleProgressionState();
            modules.SetTier(CargoId, 1);
            var progression = MakeProgression(100, 10);
            LogisticsCargo cargo = RichCargo();
            var service = new ModuleUpgradeService(modules, progression, cargo);

            service.TryStartUpgrade(CargoId); // T1 -> T2 (30-second... no: T2 is 90s)
            service.Tick(50f); // well under the 90s T2 duration

            Assert.AreEqual(1, modules.GetTier(CargoId), "tier must not bump until completion");
        }

        [Test]
        public void Cancel_RefundsFloor75PercentResources_And100PercentPoints()
        {
            var modules = new ModuleProgressionState();
            modules.SetTier(CargoId, 1); // so the purchase is T2: 1 pt, 40 part, 10 primary material
            var progression = MakeProgression(100, 10);
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(ProcessingCatalog.BasicPartId, 40f);
            cargo.LoadItem(ProcessingCatalog.MaterialIds[1], 10f); // biocomposite: logistics_cargo's primary material
            var service = new ModuleUpgradeService(modules, progression, cargo);

            service.TryStartUpgrade(CargoId);
            Assert.AreEqual(9, progression.DevelopmentPoints);

            bool cancelled = service.CancelActiveJob();

            Assert.IsTrue(cancelled);
            Assert.AreEqual(10, progression.DevelopmentPoints);
            Assert.AreEqual(30f, cargo.GetStored(ProcessingCatalog.BasicPartId)); // floor(40*0.75)
            Assert.AreEqual(7f, cargo.GetStored(ProcessingCatalog.MaterialIds[1])); // floor(10*0.75)
        }

        [Test]
        public void DoubleCancel_IsSafeNoOp()
        {
            var modules = new ModuleProgressionState();
            var progression = MakeProgression(100, 10);
            LogisticsCargo cargo = RichCargo();
            var service = new ModuleUpgradeService(modules, progression, cargo);

            service.TryStartUpgrade(ProcId);
            Assert.IsTrue(service.CancelActiveJob());
            int pointsAfterFirst = progression.DevelopmentPoints;

            Assert.IsFalse(service.CancelActiveJob());
            Assert.AreEqual(pointsAfterFirst, progression.DevelopmentPoints);
        }

        private class ToggleSafety : IUpgradeSafetyProvider
        {
            public bool Safe = true;
            public bool IsSafe() => Safe;
        }

        [Test]
        public void UnsafeState_PausesTheTimer()
        {
            var modules = new ModuleProgressionState();
            var progression = MakeProgression(100, 10);
            LogisticsCargo cargo = RichCargo();
            var service = new ModuleUpgradeService(modules, progression, cargo);
            var safety = new ToggleSafety { Safe = false };
            service.SetSafetyProvider(safety);

            service.TryStartUpgrade(ProcId);
            float remaining = service.ActiveJob.RemainingSeconds;

            service.Tick(5f);
            service.Tick(5f);

            Assert.AreEqual(remaining, service.ActiveJob.RemainingSeconds);
            Assert.IsTrue(service.ActiveJob.Paused);
        }

        [Test]
        public void SafeAgainFor8Seconds_ResumesProgress()
        {
            var modules = new ModuleProgressionState();
            var progression = MakeProgression(100, 10);
            LogisticsCargo cargo = RichCargo();
            var service = new ModuleUpgradeService(modules, progression, cargo);
            var safety = new ToggleSafety { Safe = false };
            service.SetSafetyProvider(safety);

            service.TryStartUpgrade(ProcId);
            service.Tick(1f); // becomes paused
            safety.Safe = true;

            for (int i = 0; i < 7; i++) service.Tick(1f);
            Assert.IsTrue(service.ActiveJob.Paused, "must still be paused just under 8s of safety");

            service.Tick(1.01f);
            Assert.IsFalse(service.ActiveJob.Paused, "must resume once 8 continuous safe seconds pass");
        }

        [Test]
        public void ScreenPaused_BlocksAllProgress()
        {
            var modules = new ModuleProgressionState();
            var progression = MakeProgression(100, 10);
            LogisticsCargo cargo = RichCargo();
            var service = new ModuleUpgradeService(modules, progression, cargo);

            service.TryStartUpgrade(ProcId);
            float remaining = service.ActiveJob.RemainingSeconds;
            service.ScreenPaused = true;
            service.Tick(100f);

            Assert.AreEqual(remaining, service.ActiveJob.RemainingSeconds);
        }

        [Test]
        public void RestoreActiveJob_DoesNotReSpend()
        {
            var modules = new ModuleProgressionState();
            var progression = MakeProgression(100, 10);
            var cargo = new LogisticsCargo(999f);
            cargo.LoadItem(ProcessingCatalog.BasicPartId, 20f); // exactly the T1 cost, already "spent" per the save
            var service = new ModuleUpgradeService(modules, progression, cargo);

            service.RestoreActiveJob(ProcId, fromTier: 0, toTier: 1, reservedPoints: 1,
                reservedResources: new (string, float)[] { (ProcessingCatalog.BasicPartId, 20f) },
                totalSeconds: 30f, remainingSeconds: 12f, paused: false, safeStreakSeconds: 0f);

            Assert.IsNotNull(service.ActiveJob);
            Assert.AreEqual(12f, service.ActiveJob.RemainingSeconds);
            Assert.AreEqual(10, progression.DevelopmentPoints, "restore must not spend points again");
            Assert.AreEqual(0f, cargo.GetAvailable(ProcessingCatalog.BasicPartId), "the 20 parts must be reserved again, not freely available");
        }
    }
}
