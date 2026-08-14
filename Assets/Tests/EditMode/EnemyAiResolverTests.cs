using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the shared Acquire-Escaped AI state machine + AI state bookkeeping (VS-013 §6/§11).</summary>
    public class EnemyAiResolverTests
    {
        [Test]
        public void Acquire_MovesToApproach_OnceATargetExists()
        {
            EnemyAiPhase next = EnemyAiResolver.Resolve(EnemyCatalog.FighterMk1, EnemyAiPhase.Acquire, 5000f, targetLost: false, exitAttackOrScanToRetreat: false);
            Assert.AreEqual(EnemyAiPhase.Approach, next);
        }

        [Test]
        public void Approach_EntersAttackOrScan_WithinEngageBand()
        {
            EnemyDef fighter = EnemyCatalog.FighterMk1;
            EnemyAiPhase next = EnemyAiResolver.Resolve(fighter, EnemyAiPhase.Approach, fighter.EngageBandMax - 10f, false, false);
            Assert.AreEqual(EnemyAiPhase.AttackOrScan, next);
        }

        [Test]
        public void Approach_BreaksOff_IfItOvershootsTooClose()
        {
            EnemyDef fighter = EnemyCatalog.FighterMk1;
            EnemyAiPhase next = EnemyAiResolver.Resolve(fighter, EnemyAiPhase.Approach, fighter.BreakDistance - 10f, false, false);
            Assert.AreEqual(EnemyAiPhase.Break, next);
        }

        [Test]
        public void AttackOrScan_FighterExitsToRetreat_OnLowHullFlag()
        {
            EnemyDef fighter = EnemyCatalog.FighterMk1;
            EnemyAiPhase next = EnemyAiResolver.Resolve(fighter, EnemyAiPhase.AttackOrScan, 700f, false, exitAttackOrScanToRetreat: true);
            Assert.AreEqual(EnemyAiPhase.Retreat, next);
        }

        [Test]
        public void AttackOrScan_ProbeExitsToRetreat_OnScanCompleteFlag()
        {
            EnemyDef probe = EnemyCatalog.ScoutProbeMk1;
            EnemyAiPhase next = EnemyAiResolver.Resolve(probe, EnemyAiPhase.AttackOrScan, 1200f, false, exitAttackOrScanToRetreat: true);
            Assert.AreEqual(EnemyAiPhase.Retreat, next);
        }

        [Test]
        public void AttackOrScan_RepositionsWhenTargetDriftsOutOfBand()
        {
            EnemyDef fighter = EnemyCatalog.FighterMk1;
            EnemyAiPhase next = EnemyAiResolver.Resolve(fighter, EnemyAiPhase.AttackOrScan, fighter.EngageBandMax + 500f, false, false);
            Assert.AreEqual(EnemyAiPhase.Reposition, next);
        }

        [Test]
        public void Break_MovesToReposition_OnceClearOfEngageBandMin()
        {
            EnemyDef fighter = EnemyCatalog.FighterMk1;
            EnemyAiPhase next = EnemyAiResolver.Resolve(fighter, EnemyAiPhase.Break, fighter.EngageBandMin + 10f, false, false);
            Assert.AreEqual(EnemyAiPhase.Reposition, next);
        }

        [Test]
        public void Reposition_ReEngages_OnceBackInBand()
        {
            EnemyDef fighter = EnemyCatalog.FighterMk1;
            EnemyAiPhase next = EnemyAiResolver.Resolve(fighter, EnemyAiPhase.Reposition, fighter.EngageBandMax - 20f, false, false);
            Assert.AreEqual(EnemyAiPhase.ReEngage, next);
        }

        [Test]
        public void ReEngage_FunnelsBackIntoAttackOrScan()
        {
            EnemyAiPhase next = EnemyAiResolver.Resolve(EnemyCatalog.FighterMk1, EnemyAiPhase.ReEngage, 700f, false, false);
            Assert.AreEqual(EnemyAiPhase.AttackOrScan, next);
        }

        [Test]
        public void AnyPhase_TargetLost_ReturnsToAcquire_ExceptRetreatAndEscaped()
        {
            EnemyDef fighter = EnemyCatalog.FighterMk1;
            Assert.AreEqual(EnemyAiPhase.Acquire, EnemyAiResolver.Resolve(fighter, EnemyAiPhase.Approach, 500f, true, false));
            Assert.AreEqual(EnemyAiPhase.Acquire, EnemyAiResolver.Resolve(fighter, EnemyAiPhase.AttackOrScan, 500f, true, false));
            Assert.AreEqual(EnemyAiPhase.Acquire, EnemyAiResolver.Resolve(fighter, EnemyAiPhase.Reposition, 500f, true, false));
        }

        [Test]
        public void Escaped_IsTerminal()
        {
            Assert.AreEqual(EnemyAiPhase.Escaped, EnemyAiResolver.Resolve(EnemyCatalog.FighterMk1, EnemyAiPhase.Escaped, 99999f, false, false));
        }

        [Test]
        public void ProbeAndFighter_ShareTheExactSameResolverFunction_OnlyDataDiffers()
        {
            // Same call, same phase/geometry -- only the EnemyDef instance differs; proves no per-type branching lives in the resolver itself.
            EnemyAiPhase probeResult = EnemyAiResolver.Resolve(EnemyCatalog.ScoutProbeMk1, EnemyAiPhase.Acquire, 1200f, false, false);
            EnemyAiPhase fighterResult = EnemyAiResolver.Resolve(EnemyCatalog.FighterMk1, EnemyAiPhase.Acquire, 1200f, false, false);
            Assert.AreEqual(probeResult, fighterResult);
        }

        // --- EnemyAiState bookkeeping ---------------------------------------------

        [Test]
        public void DecisionDue_FiresAtExactly5Hz()
        {
            var state = new EnemyAiState(EnemyCatalog.FighterMk1);
            int fires = 0;
            for (int i = 0; i < 100; i++) if (state.DecisionDue(0.01f)) fires++; // 100 * 0.01s = 1.0s of simulated time
            Assert.AreEqual(5, fires, "1 second at 5 Hz must produce exactly 5 decisions");
        }

        [Test]
        public void AcquireTarget_HoldsForAtLeast5Seconds()
        {
            var state = new EnemyAiState(EnemyCatalog.FighterMk1);
            state.AcquireTarget("player");
            state.DecisionDue(2f); // well under 5s commitment
            state.AcquireTarget("someone-else");
            Assert.AreEqual("player", state.TargetId, "a target swap within the 5s commitment window must be rejected");
        }

        [Test]
        public void AcquireTarget_AllowsSwap_AfterCommitmentElapses()
        {
            var state = new EnemyAiState(EnemyCatalog.FighterMk1);
            state.AcquireTarget("player");
            state.DecisionDue(6f);
            state.AcquireTarget("someone-else");
            Assert.AreEqual("someone-else", state.TargetId);
        }

        [Test]
        public void ScanTick_PausesNotResets_WhenConditionLost()
        {
            var state = new EnemyAiState(EnemyCatalog.ScoutProbeMk1);
            state.TickScan(20f, true);
            Assert.AreEqual(20f, state.ScanElapsed);
            state.TickScan(100f, false); // out of band -- must not advance
            Assert.AreEqual(20f, state.ScanElapsed);
            state.TickScan(25f, true);
            Assert.IsTrue(state.ScanComplete);
        }

        /// <summary>Fresh state starts at Acquire, which ignores the exit flag entirely (VS-013 §6: Acquire only cares about targetLost) — so reaching Retreat needs the real Acquire->Approach->AttackOrScan->Retreat walk, one MakeDecision per hop, exactly as EnemyAgent's own per-decision calls would drive it.</summary>
        private static EnemyAiState DriveFighterIntoRetreat()
        {
            var state = new EnemyAiState(EnemyCatalog.FighterMk1);
            state.MakeDecision(5000f, false, false); // Acquire -> Approach
            state.MakeDecision(700f, false, false);  // Approach -> AttackOrScan (within engage band, past break distance)
            state.MakeDecision(700f, false, true);   // AttackOrScan -> Retreat (exit flag)
            return state;
        }

        [Test]
        public void RetreatEscape_RequiresDistanceAndContinuousTime()
        {
            EnemyAiState state = DriveFighterIntoRetreat();
            Assert.AreEqual(EnemyAiPhase.Retreat, state.Phase);

            state.TickRetreatEscape(4f, EnemyCatalog.RetreatEscapeDistance + 100f, stillInSensorRange: false);
            Assert.AreEqual(EnemyAiPhase.Retreat, state.Phase, "must not escape before the full continuous window elapses");

            state.TickRetreatEscape(2f, EnemyCatalog.RetreatEscapeDistance + 100f, stillInSensorRange: false);
            Assert.AreEqual(EnemyAiPhase.Escaped, state.Phase);
        }

        [Test]
        public void RetreatEscape_ResetsContinuousTimer_IfSensorReacquiresOrDistanceDrops()
        {
            EnemyAiState state = DriveFighterIntoRetreat();
            state.TickRetreatEscape(4f, EnemyCatalog.RetreatEscapeDistance + 100f, false);
            state.TickRetreatEscape(1f, EnemyCatalog.RetreatEscapeDistance + 100f, stillInSensorRange: true); // reacquired
            state.TickRetreatEscape(4f, EnemyCatalog.RetreatEscapeDistance + 100f, false);
            Assert.AreEqual(EnemyAiPhase.Retreat, state.Phase, "the continuous timer must restart from zero after any break in the condition");
        }
    }
}
