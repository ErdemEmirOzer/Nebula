using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-014 §8/§11/§13/§18: pressure accumulation, grace, raid phase lifecycle, participation/result guards.</summary>
    public class Vs014ThreatPressureStateTests
    {
        [Test]
        public void TickPressure_NeverExceeds100()
        {
            var p = new ThreatPressureState();
            p.TickPressure(1000f, 3f, 1f);
            Assert.LessOrEqual(p.Pressure, ThreatPressureState.MaxPressure);
        }

        [Test]
        public void TickPressure_NeverAdvancesDuringGrace()
        {
            var p = new ThreatPressureState();
            p.StartGrace(10f);
            p.TickPressure(60f, 3f, 1f);
            Assert.AreEqual(0f, p.Pressure, 0.001f);
        }

        [Test]
        public void TickPressure_NeverAdvancesWhileRaidInProgress()
        {
            var p = new ThreatPressureState();
            p.ScheduleRaid(RaidStage.Pirate, RaidDoctrine.Loot, "planet.a", new List<string>(), 1, 90f);
            p.TickPressure(60f, 3f, 1f);
            Assert.AreEqual(0f, p.Pressure, 0.001f);
        }

        [Test]
        public void TickPressure_SpeedMultiplierClampedAt1Point5()
        {
            var a = new ThreatPressureState();
            var b = new ThreatPressureState();
            a.TickPressure(60f, 3f, 1.5f);
            b.TickPressure(60f, 3f, 5f); // way above the cap
            Assert.AreEqual(a.Pressure, b.Pressure, 0.01f);
        }

        [Test]
        public void Grace_NeverGrantedTwice()
        {
            var p = new ThreatPressureState();
            Assert.IsFalse(p.FirstControlledGraceGranted);
            p.MarkFirstControlledGraceGranted();
            Assert.IsTrue(p.FirstControlledGraceGranted);
        }

        [Test]
        public void Grace_StartGraceNeverShortensALongerExistingWindow()
        {
            var p = new ThreatPressureState();
            p.StartGrace(20f);
            p.StartGrace(5f);
            Assert.AreEqual(20f, p.GraceRemainingActiveMinutes, 0.001f);
        }

        [Test]
        public void TickGrace_CountsDownAndExpires()
        {
            var p = new ThreatPressureState();
            p.StartGrace(1f); // 1 active-minute
            p.TickGrace(30f); // 30s
            Assert.IsTrue(p.InGrace);
            p.TickGrace(31f); // total 61s > 60s
            Assert.IsFalse(p.InGrace);
        }

        [Test]
        public void ScheduleRaid_ResetsEveryPerRaidGuard()
        {
            var p = new ThreatPressureState();
            p.NoteMeaningfulDamage(); p.NoteValidFocusCommand(); p.NoteActiveDefenseParticipation();
            p.NotePlayerShutdown(); p.NoteResourceStolen(); p.MarkResultApplied(); p.MarkSalvageApplied();
            p.MarkPendingProbeEffectConsumed(); p.NoteTargetContested();

            p.ScheduleRaid(RaidStage.Assault, RaidDoctrine.Suppression, "planet.b", new List<string> { "enemy.fighter.mk1" }, 99, 90f);

            Assert.IsFalse(p.PlayerDealtMeaningfulDamage);
            Assert.IsFalse(p.PlayerIssuedValidFocusCommand);
            Assert.IsFalse(p.PlayerActiveDefenseParticipation);
            Assert.IsFalse(p.PlayerWasShutdownDuringRaid);
            Assert.IsFalse(p.AnyResourceStolen);
            Assert.IsFalse(p.ResultApplied);
            Assert.IsFalse(p.SalvageApplied);
            Assert.IsFalse(p.PendingProbeEffectConsumedThisRaid);
            Assert.IsFalse(p.WasTargetContestedDuringRaid);
            Assert.AreEqual(RaidPhase.Scheduled, p.Phase);
            Assert.AreEqual(RaidStage.Assault, p.Stage);
            Assert.AreEqual("planet.b", p.TargetPlanetWorldId);
        }

        [Test]
        public void TickWarning_ReturnsTrueExactlyOnceWhenItExpires()
        {
            var p = new ThreatPressureState();
            p.ScheduleRaid(RaidStage.Pirate, RaidDoctrine.Loot, "planet.a", new List<string>(), 1, 5f);
            p.SetPhase(RaidPhase.Warning);
            Assert.IsFalse(p.TickWarning(4f));
            Assert.IsTrue(p.TickWarning(2f)); // total 6s > 5s
            Assert.IsFalse(p.TickWarning(1f)); // no longer Warning-driven, no double-fire
        }

        [Test]
        public void HasMeaningfulParticipation_TrueForAnyOfTheThreeSignals()
        {
            var damage = new ThreatPressureState(); damage.NoteMeaningfulDamage();
            var focus = new ThreatPressureState(); focus.NoteValidFocusCommand();
            var defense = new ThreatPressureState(); defense.NoteActiveDefenseParticipation();
            var none = new ThreatPressureState();
            Assert.IsTrue(damage.HasMeaningfulParticipation);
            Assert.IsTrue(focus.HasMeaningfulParticipation);
            Assert.IsTrue(defense.HasMeaningfulParticipation);
            Assert.IsFalse(none.HasMeaningfulParticipation);
        }

        [Test]
        public void ReducePressure_NeverGoesBelowZero()
        {
            var p = new ThreatPressureState();
            p.TickPressure(200f, 3f, 1f); // build up some pressure
            p.ReducePressure(1000f);
            Assert.AreEqual(0f, p.Pressure, 0.001f);
        }

        [Test]
        public void SpawnedEnemyIds_RoundTripsViaRestoreFromSave()
        {
            var p = new ThreatPressureState();
            p.RestoreFromSave(50f, 0f, false, 0f, RaidPhase.Active, RaidStage.Pirate, RaidDoctrine.Loot, "planet.a",
                new List<string> { "enemy.fighter.mk1" }, 42, 0f, 30f,
                false, false, false, false, false, false, false, false,
                spawnedEnemyIds: new List<string> { "sys/enemy.0001", "sys/enemy.0002" });
            CollectionAssert.AreEqual(new List<string> { "sys/enemy.0001", "sys/enemy.0002" }, p.SpawnedEnemyIds);
        }
    }

    /// <summary>VS-014 §8/§18: campaign-lifetime global raid cap, taught classes, last-5 history, Pirate Base ledger.</summary>
    public class Vs014RaidCampaignStateTests
    {
        [Test]
        public void TryStartGlobalMajorRaid_SecondAttemptFailsWhileFirstIsActive()
        {
            var c = new RaidCampaignState();
            Assert.IsTrue(c.TryStartGlobalMajorRaid("system.a"));
            Assert.IsFalse(c.TryStartGlobalMajorRaid("system.b"));
            Assert.AreEqual("system.a", c.ActiveMajorRaidSystemId);
        }

        [Test]
        public void EndGlobalMajorRaid_OnlyReleasesIfCallerHoldsTheSlot()
        {
            var c = new RaidCampaignState();
            c.TryStartGlobalMajorRaid("system.a");
            c.EndGlobalMajorRaid("system.b"); // not the holder -- no-op
            Assert.IsTrue(c.HasGlobalMajorRaidActive);
            c.EndGlobalMajorRaid("system.a");
            Assert.IsFalse(c.HasGlobalMajorRaidActive);
        }

        [Test]
        public void History_CappedAtFive_NewestKept()
        {
            var c = new RaidCampaignState();
            for (int i = 0; i < 8; i++) c.RecordHistory(RaidStage.Pirate, RaidDoctrine.Loot, null, "planet." + i, null);
            Assert.AreEqual(5, c.History.Count);
            Assert.AreEqual("planet.7", c.History[4].PlanetWorldId);
            Assert.AreEqual("planet.3", c.History[0].PlanetWorldId);
        }

        [Test]
        public void MarkTaught_IsIdempotentAndQueryable()
        {
            var c = new RaidCampaignState();
            Assert.IsFalse(c.IsTaught(EnemyIds.HeavyAssaultMk1));
            c.MarkTaught(EnemyIds.HeavyAssaultMk1);
            c.MarkTaught(EnemyIds.HeavyAssaultMk1);
            Assert.IsTrue(c.IsTaught(EnemyIds.HeavyAssaultMk1));
            Assert.AreEqual(1, c.TaughtEnemyDefIds.Count);
        }

        [Test]
        public void PirateBase_SpawnedExactlyOnce_SubsequentCallsAreNoOps()
        {
            var c = new RaidCampaignState();
            c.MarkPirateBaseSpawned("poi.pirate_base", "system.a");
            c.MarkPirateBaseSpawned("poi.other", "system.b"); // must never overwrite
            Assert.AreEqual("poi.pirate_base", c.PirateBase.PoiId);
            Assert.AreEqual("system.a", c.PirateBase.SystemId);
        }

        [Test]
        public void PirateBase_ResolvedRequiresSpawnedFirst()
        {
            var c = new RaidCampaignState();
            c.MarkPirateBaseResolved(); // never spawned -- no-op
            Assert.IsFalse(c.PirateBase.Resolved);
        }

        [Test]
        public void PirateBase_RewardAwardedRequiresResolvedFirst()
        {
            var c = new RaidCampaignState();
            c.MarkPirateBaseSpawned("poi.pirate_base", "system.a");
            c.MarkPirateBaseRewardAwarded(); // not yet resolved -- no-op
            Assert.IsFalse(c.PirateBase.RewardAwarded);
            c.MarkPirateBaseResolved();
            c.MarkPirateBaseRewardAwarded();
            Assert.IsTrue(c.PirateBase.RewardAwarded);
        }

        [Test]
        public void RestoreFromSave_RoundTripsEveryField()
        {
            var c = new RaidCampaignState();
            var history = new List<RaidHistoryEntry> { new RaidHistoryEntry { Stage = RaidStage.Siege, Doctrine = RaidDoctrine.Siege, LeaderDefId = EnemyIds.SiegeMk1, PlanetWorldId = "planet.x" } };
            c.RestoreFromSave(RaidDifficulty.Conqueror, "system.z", new List<string> { EnemyIds.HeavyAssaultMk1 }, history,
                true, "poi.pirate_base", "system.y", true, true);

            Assert.AreEqual(RaidDifficulty.Conqueror, c.Difficulty);
            Assert.AreEqual("system.z", c.ActiveMajorRaidSystemId);
            Assert.IsTrue(c.IsTaught(EnemyIds.HeavyAssaultMk1));
            Assert.AreEqual(1, c.History.Count);
            Assert.IsTrue(c.PirateBase.Spawned);
            Assert.IsTrue(c.PirateBase.Resolved);
            Assert.IsTrue(c.PirateBase.RewardAwarded);
        }
    }
}
