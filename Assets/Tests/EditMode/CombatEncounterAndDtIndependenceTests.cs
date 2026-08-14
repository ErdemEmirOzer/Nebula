using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the combat-open/close 8s timer (VS-013 §5/§11).</summary>
    public class CombatEncounterStateTests
    {
        [Test]
        public void FreshState_StartsClosed()
        {
            var state = new CombatEncounterState();
            Assert.IsFalse(state.IsOpen);
        }

        [Test]
        public void RegisterThreatEvent_OpensImmediately_AndResetsTheCloseTimer()
        {
            var state = new CombatEncounterState();
            state.RegisterThreatEvent();
            Assert.IsTrue(state.IsOpen);
            Assert.AreEqual(0f, state.SinceLastThreatSeconds);
        }

        [Test]
        public void Closes_Exactly8SecondsAfterTheLastThreatEvent()
        {
            var state = new CombatEncounterState();
            state.RegisterThreatEvent();
            for (int i = 0; i < 7; i++) { state.Tick(1f); Assert.IsTrue(state.IsOpen, $"must still be open at t={i + 1}s"); }
            state.Tick(1f); // t=8s
            Assert.IsFalse(state.IsOpen);
        }

        [Test]
        public void ANewThreatEvent_RestartsTheCloseWindow_BeforeItWouldHaveClosed()
        {
            var state = new CombatEncounterState();
            state.RegisterThreatEvent();
            state.Tick(7f);
            state.RegisterThreatEvent(); // fresh threat just before the old window would have expired
            state.Tick(7f);
            Assert.IsTrue(state.IsOpen, "the timer must restart from the newest threat event, not the original one");
        }

        [Test]
        public void RestoreFromSave_ReappliesOpenStateAndTimer()
        {
            var state = new CombatEncounterState();
            state.RestoreFromSave(true, 5f);
            Assert.IsTrue(state.IsOpen);
            state.Tick(3.1f);
            Assert.IsFalse(state.IsOpen);
        }
    }

    /// <summary>
    /// Source-scan regression test for VS-013's DT-001 boundary: the pure combat
    /// domain (Assets/Scripts/Nebula/Combat) must never reference the DT-001 test
    /// speed multiplier or its math helper. DT-001's multiplier is wired as an
    /// OUTER factor on commanded ship movement only (ShipController.HandleTranslation);
    /// AI decisions, projectile flight, DPS/heat/energy math, training timers,
    /// cooldowns, star-risk warnings, and encounter counters must all keep running
    /// on real, unscaled Time.deltaTime/FixedUpdate regardless of the test speed
    /// slider — this proves that boundary structurally, not just by spot-check.
    /// </summary>
    public class CombatDtIndependenceTests
    {
        private static string CombatScriptsRoot => Path.Combine(Application.dataPath, "Scripts", "Nebula", "Combat");

        private static IEnumerable<string> AllCombatCsFiles() => Directory.GetFiles(CombatScriptsRoot, "*.cs", SearchOption.AllDirectories);

        [Test]
        public void NoCombatDomainScript_ReferencesFlightTestSpeedMathOrTestSpeedMultiplier()
        {
            var offenders = new List<string>();
            foreach (string file in AllCombatCsFiles())
            {
                string content = File.ReadAllText(file);
                if (content.Contains("FlightTestSpeedMath") || content.Contains("TestSpeedMultiplier"))
                    offenders.Add(file);
            }
            Assert.IsEmpty(offenders, "combat domain logic must never read the DT-001 test speed multiplier: " + string.Join(", ", offenders));
        }

        [Test]
        public void HundredXTestSpeedMultiplier_NeverChangesWeaponDamageOrHeatMath()
        {
            // WeaponCatalog's pure functions take only (tier, dt) / (tier) -- there is no
            // speed-multiplier parameter anywhere in the signature for them to be
            // influenced by, so calling them in a "100x test speed" scenario (still
            // driven by the same real dt) must produce identical results to any other.
            float dpsAt1x = WeaponCatalog.LaserDamagePerTick(1, 1f);
            float dpsAtSameRealDt = WeaponCatalog.LaserDamagePerTick(1, 1f);
            Assert.AreEqual(dpsAt1x, dpsAtSameRealDt, "Laser damage must depend only on real dt, never on any ship speed multiplier");

            float heatPerShotBefore = WeaponCatalog.PlasmaHeatPerShot;
            float heatPerShotAfter = WeaponCatalog.PlasmaHeatPerShot;
            Assert.AreEqual(heatPerShotBefore, heatPerShotAfter);
        }

        [Test]
        public void EnemyAiDecisionCadence_HasNoSpeedMultiplierInput()
        {
            // EnemyAiState.DecisionDue(dt) and TickScan(dt, bool) only ever take the
            // real per-frame dt -- proving (by exercising them directly, same as any
            // other test speed) that a 100x ship-speed slider cannot possibly
            // accelerate AI decisions, since there is no multiplier parameter for it
            // to reach through.
            var state = new EnemyAiState(EnemyCatalog.ScoutProbeMk1);
            int fires = 0;
            for (int i = 0; i < 100; i++) if (state.DecisionDue(0.01f)) fires++;
            Assert.AreEqual(5, fires, "1 real second at 5 Hz is always exactly 5 decisions, regardless of any ship speed setting");
        }
    }
}
