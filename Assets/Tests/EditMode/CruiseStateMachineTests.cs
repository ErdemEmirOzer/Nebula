using NUnit.Framework;
using Nebula.Navigation;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for VS-006's pure cruise flight state machine (spec §9, tests 9-10, 17).</summary>
    public class CruiseStateMachineTests
    {
        private static CruiseEntryConditions AllMet => new CruiseEntryConditions
        {
            PlayerArmed = true,
            ThrottleHigh = true, NotStabilized = true, BuildModeOff = true,
            NoPhysicalInteractionActive = true, NotNearHazard = true, InputFree = true, NoSharpTurn = true,
            NotInStellarRisk = true, NotDamaged = true
        };

        private const float LocalMax = 180f;
        private const float CruiseTargetT1 = 1800f;

        [Test]
        public void StellarRisk_BlocksCruiseEntry_EvenWithEveryOtherConditionMet()
        {
            var machine = new CruiseStateMachine();
            machine.SetEngineTier(1);
            CruiseEntryConditions inRiskZone = AllMet;
            inRiskZone.NotInStellarRisk = false;

            for (int i = 0; i < 600; i++) machine.Tick(1f / 60f, inRiskZone, LocalMax, CruiseTargetT1);

            Assert.AreEqual(FlightMode.LocalFlight, machine.Mode, "VS-009 §2: a Warning/Critical stellar-risk zone must reject cruise exactly like any other blocked condition");
            Assert.AreEqual(1f, machine.SpeedMultiplier);
        }

        [Test]
        public void StellarRisk_LostMidCruise_TriggersDeceleration()
        {
            var machine = new CruiseStateMachine();
            machine.SetEngineTier(1);
            float dt = 1f / 60f;
            for (float t = 0f; t < 9f; t += dt) machine.Tick(dt, AllMet, LocalMax, CruiseTargetT1); // reach cruise

            Assert.AreEqual(FlightMode.DeepSpaceCruise, machine.Mode);

            CruiseEntryConditions riskLost = AllMet;
            riskLost.NotInStellarRisk = false;
            machine.Tick(dt, riskLost, LocalMax, CruiseTargetT1);
            Assert.AreEqual(FlightMode.CruiseDeceleration, machine.Mode);
        }

        [Test]
        public void NoEngineTier_NeverEntersCruise_EvenWithAllOtherConditionsMet()
        {
            var machine = new CruiseStateMachine();
            machine.SetEngineTier(0);
            for (int i = 0; i < 600; i++) machine.Tick(1f / 60f, AllMet, LocalMax, CruiseTargetT1);

            Assert.AreEqual(FlightMode.LocalFlight, machine.Mode);
            Assert.AreEqual(1f, machine.SpeedMultiplier);
        }

        [Test]
        public void MissingAnyOneCondition_BlocksEntry()
        {
            var machine = new CruiseStateMachine();
            machine.SetEngineTier(1);
            CruiseEntryConditions blocked = AllMet;
            blocked.NotNearHazard = false;

            for (int i = 0; i < 600; i++) machine.Tick(1f / 60f, blocked, LocalMax, CruiseTargetT1);

            Assert.AreEqual(FlightMode.LocalFlight, machine.Mode);
        }

        [Test]
        public void EligibleForEightSeconds_RampsPhysicallyThenReachesDeepSpaceCruise()
        {
            var machine = new CruiseStateMachine();
            machine.SetEngineTier(1);
            float dt = 1f / 60f;

            // Mid-ramp: still ramping, multiplier strictly between 1 and target ratio.
            for (float t = 0f; t < 4f; t += dt) machine.Tick(dt, AllMet, LocalMax, CruiseTargetT1);
            Assert.AreEqual(FlightMode.CruiseRamp, machine.Mode);
            Assert.Greater(machine.SpeedMultiplier, 1f);
            Assert.Less(machine.SpeedMultiplier, CruiseTargetT1 / LocalMax);

            for (float t = 0f; t < 5f; t += dt) machine.Tick(dt, AllMet, LocalMax, CruiseTargetT1);
            Assert.AreEqual(FlightMode.DeepSpaceCruise, machine.Mode);
            Assert.AreEqual(CruiseTargetT1 / LocalMax, machine.SpeedMultiplier, 0.01f);
        }

        [Test]
        public void LosingConditionsMidCruise_StartsGradualDeceleration_NeverSnapsToLocal()
        {
            var machine = new CruiseStateMachine();
            machine.SetEngineTier(1);
            float dt = 1f / 60f;
            for (float t = 0f; t < 9f; t += dt) machine.Tick(dt, AllMet, LocalMax, CruiseTargetT1); // reach cruise

            CruiseEntryConditions lost = AllMet;
            lost.NotStabilized = false; // e.g. orbit-stabilize engaged
            machine.Tick(dt, lost, LocalMax, CruiseTargetT1);

            Assert.AreEqual(FlightMode.CruiseDeceleration, machine.Mode);
            // Immediately after losing eligibility the multiplier must still be
            // near the cruise ratio (a gradual departure), not snapped to 1.
            Assert.Greater(machine.SpeedMultiplier, (CruiseTargetT1 / LocalMax) * 0.9f);

            for (float t = 0f; t < 9f; t += dt) machine.Tick(dt, lost, LocalMax, CruiseTargetT1);
            Assert.AreEqual(FlightMode.LocalFlight, machine.Mode);
            Assert.AreEqual(1f, machine.SpeedMultiplier);
        }

        [Test]
        public void SharpTurnCondition_PreventsOrEndsCruise()
        {
            var machine = new CruiseStateMachine();
            machine.SetEngineTier(1);
            float dt = 1f / 60f;
            for (float t = 0f; t < 9f; t += dt) machine.Tick(dt, AllMet, LocalMax, CruiseTargetT1);
            Assert.AreEqual(FlightMode.DeepSpaceCruise, machine.Mode);

            CruiseEntryConditions sharpTurn = AllMet;
            sharpTurn.NoSharpTurn = false;
            machine.Tick(dt, sharpTurn, LocalMax, CruiseTargetT1);

            Assert.AreEqual(FlightMode.CruiseDeceleration, machine.Mode);
        }

        [Test]
        public void T1AndT2_TargetSpeedsMatchSpec()
        {
            Assert.AreEqual(1800f, Nebula.Modules.ModuleCatalog.CruiseSpeedByTier[0]);
            Assert.AreEqual(4500f, Nebula.Modules.ModuleCatalog.CruiseSpeedByTier[1]);
        }

        [Test]
        public void HeliosCyanisRoute_PureDistanceOverSpeed_FallsWithinSpecRanges()
        {
            // (450000,0,350000) - authored Cyanis position, origin Helios.
            float distance = System.MathF.Sqrt(450000f * 450000f + 350000f * 350000f);

            float t1Minutes = distance / Nebula.Modules.ModuleCatalog.CruiseSpeedByTier[0] / 60f;
            float t2Minutes = distance / Nebula.Modules.ModuleCatalog.CruiseSpeedByTier[1] / 60f;

            Assert.GreaterOrEqual(t1Minutes, 4f);
            Assert.LessOrEqual(t1Minutes, 7f);
            Assert.GreaterOrEqual(t2Minutes, 2f);
            Assert.LessOrEqual(t2Minutes, 3f);
        }
    }
}
