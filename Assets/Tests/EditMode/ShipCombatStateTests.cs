using NUnit.Framework;
using Nebula.Combat;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for ship K/Z/G (Durability reuse) + energy/boost/star-risk (VS-013 §1/§11).</summary>
    public class ShipCombatStateTests
    {
        private static StellarRiskState Safe() => new StellarRiskState(StellarRiskLevel.Safe, 1f, 1f, 1f, 0f, true);
        private static StellarRiskState Warning() => new StellarRiskState(StellarRiskLevel.Warning, 0f, 0.75f, 0.75f, 0f, false);
        private static StellarRiskState Critical() => new StellarRiskState(StellarRiskLevel.Critical, 0f, 0.40f, 0.40f, 0.02f, false);

        [Test]
        public void FreshState_StartsAtFullKZGAndFullEnergy()
        {
            var state = new ShipCombatState(hullForm: 1, hullReinforcementTier: 0, shieldSystemsTier: 0, weaponCoordinationTier: 0);
            Assert.AreEqual(400f, state.Durability.Shield);
            Assert.AreEqual(600f, state.Durability.Armor);
            Assert.AreEqual(1000f, state.Durability.Hull);
            Assert.AreEqual(100f, state.Energy);
            Assert.AreEqual(100f, state.EnergyCapacity);
        }

        [Test]
        public void ApplyDamage_CascadesShieldArmorHull_ViaDurability()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.ApplyDamage(450f); // 400 shield + 50 armor
            Assert.AreEqual(0f, state.Durability.Shield);
            Assert.AreEqual(550f, state.Durability.Armor);
            Assert.AreEqual(1000f, state.Durability.Hull);
        }

        [Test]
        public void EmergencyShutdown_TrueOnlyWhenHullReachesZero()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            Assert.IsFalse(state.EmergencyShutdown);
            state.ApplyDamage(400f + 600f + 1000f);
            Assert.IsTrue(state.EmergencyShutdown);
        }

        [Test]
        public void ShieldRegen_PausesCompletely_InWarningAndCriticalZones()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.ApplyDamage(100f); // shield 400->300, regen delay starts (8s)
            for (int i = 0; i < 20; i++) state.Tick(1f, Warning()); // way past the 8s delay, but Warning = 0 regen
            Assert.AreEqual(300f, state.Durability.Shield, "shield regen must be fully paused in Warning");

            for (int i = 0; i < 20; i++) state.Tick(1f, Critical());
            Assert.AreEqual(300f, state.Durability.Shield, "shield regen must be fully paused in Critical too");
        }

        [Test]
        public void ShieldRegen_ResumesInSafeZone_AfterTheDelay()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.ApplyDamage(100f);
            for (int i = 0; i < 9; i++) state.Tick(1f, Safe()); // 8s delay elapses, then 1s of 5%/s regen
            Assert.Greater(state.Durability.Shield, 300f);
        }

        [Test]
        public void EnergyRegen_ScalesByStarRiskCombatEnergyMultiplier()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.TryConsumeEnergy(50f); // energy 100 -> 50
            state.Tick(1f, Warning()); // 15/s * 0.75 = 11.25
            Assert.AreEqual(50f + 15f * 0.75f, state.Energy, 0.01f);
        }

        [Test]
        public void Energy_LocksWeaponsAtZero_ReopensAt20Percent()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            Assert.IsTrue(state.TryConsumeEnergy(100f)); // drains to exactly 0
            Assert.IsTrue(state.WeaponsLocked);
            Assert.IsFalse(state.TryConsumeEnergy(1f), "locked -- no further spend allowed");

            // Regen up toward the 20% reopen threshold (20 of 100).
            for (int i = 0; i < 20; i++) state.Tick(1f, Safe());
            Assert.IsFalse(state.WeaponsLocked, "must reopen once energy reaches 20% of capacity");
        }

        [Test]
        public void TryConsumeEnergy_NeverPartiallySpends_WhenInsufficient()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            Assert.IsFalse(state.TryConsumeEnergy(150f));
            Assert.AreEqual(100f, state.Energy, "a rejected spend must never partially drain the pool");
        }

        [Test]
        public void Boost_CostsEnergy_GrantsDurationThenCooldown_NoRegenWhileBoosting()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            Assert.IsTrue(state.TryStartBoost());
            Assert.AreEqual(60f, state.Energy, "100 - 40 boost cost");
            Assert.IsTrue(state.IsBoosting);

            state.Tick(1f, Safe()); // mid-boost -- no energy regen
            Assert.AreEqual(60f, state.Energy);

            state.Tick(1f, Safe());
            state.Tick(1.1f, Safe()); // boost duration (3s) elapses -> cooldown starts
            Assert.IsFalse(state.IsBoosting);
            Assert.Greater(state.BoostCooldownRemaining, 0f);

            Assert.IsFalse(state.TryStartBoost(), "must not be able to re-boost during cooldown");
        }

        [Test]
        public void Boost_NeverGrantsInvincibility()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.TryStartBoost();
            Assert.IsTrue(state.IsBoosting);
            float before = state.Durability.Hull;
            // Enough to punch clean through full Shield (400) + Armor (600) and still reach Hull.
            state.ApplyDamage(400f + 600f + 50f);
            Assert.Less(state.Durability.Hull, before, "damage must still apply while boosting");
        }

        [Test]
        public void RecoverToFractions_SetsExactFractionsOfCurrentMax_MarksDamaged()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.RecoverToFractions(0.25f, 0.10f, 0f);
            Assert.AreEqual(250f, state.Durability.Hull, 0.01f);
            Assert.AreEqual(60f, state.Durability.Armor, 0.01f);
            Assert.AreEqual(0f, state.Durability.Shield);
            Assert.IsTrue(state.Damaged);
            Assert.IsFalse(state.EmergencyShutdown, "recovery must never leave the ship still at 0 hull");
        }

        [Test]
        public void ApplyModuleTiers_HullFormII_ScalesMaxDurability_ShieldNotFromHullReinforcement()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.ApplyModuleTiers(hullForm: 2, hullReinforcementTier: 0, shieldSystemsTier: 0, weaponCoordinationTier: 0, resetCurrentToFull: true);
            Assert.AreEqual(600f, state.Durability.ShieldMax, 0.01f);  // Hull Form II alone: 400 * 1.5
            Assert.AreEqual(900f, state.Durability.ArmorMax, 0.01f);  // 600 * 1.5
            Assert.AreEqual(1500f, state.Durability.HullMax, 0.01f);  // 1000 * 1.5
        }

        [Test]
        public void ApplyModuleTiers_HullReinforcementT2_OnlyScalesArmorAndHull_NeverShield()
        {
            var state = new ShipCombatState(2, 0, 0, 0); // Form II baseline
            state.ApplyModuleTiers(hullForm: 2, hullReinforcementTier: 2, shieldSystemsTier: 0, weaponCoordinationTier: 0, resetCurrentToFull: true);
            Assert.AreEqual(600f, state.Durability.ShieldMax, 0.01f, "Hull Reinforcement must never touch Shield");
            Assert.AreEqual(1170f, state.Durability.ArmorMax, 0.01f);  // 900 * 1.30
            Assert.AreEqual(1950f, state.Durability.HullMax, 0.01f);   // 1500 * 1.30
        }

        [Test]
        public void ApplyModuleTiers_ShieldSystemsT2_OnlyScalesShield_NeverArmorOrHull()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.ApplyModuleTiers(hullForm: 1, hullReinforcementTier: 0, shieldSystemsTier: 2, weaponCoordinationTier: 0, resetCurrentToFull: true);
            Assert.AreEqual(600f, state.Durability.ShieldMax, 0.01f);  // 400 * 1.50
            Assert.AreEqual(600f, state.Durability.ArmorMax, 0.01f, "Shield Systems must never touch Armor");
            Assert.AreEqual(1000f, state.Durability.HullMax, 0.01f, "Shield Systems must never touch Hull");
        }

        [Test]
        public void ApplyModuleTiers_NonReset_PreservesPercentage_NeverAFreeFullHeal()
        {
            var state = new ShipCombatState(1, 0, 0, 0); // Shield/Armor/Hull max 400/600/1000
            state.ApplyDamage(700f); // Shield 400->0, Armor 600->300 (50%), Hull untouched (100%)
            Assert.AreEqual(0.5f, state.Durability.Armor / state.Durability.ArmorMax, 0.001f);

            // Hull Form I->II (max Armor 600->900) must preserve the 50% fraction, not the absolute 300.
            state.ApplyModuleTiers(hullForm: 2, hullReinforcementTier: 0, shieldSystemsTier: 0, weaponCoordinationTier: 0, resetCurrentToFull: false);
            Assert.AreEqual(900f, state.Durability.ArmorMax, 0.01f);
            Assert.AreEqual(450f, state.Durability.Armor, 0.01f, "50% of the new 900 max, not the stale absolute 300 and not a free full heal to 900");
            Assert.AreEqual(1500f, state.Durability.HullMax, 0.01f);
            Assert.AreEqual(1500f, state.Durability.Hull, 0.01f, "Hull was at 100% before the change -- 100% of the new max is correct, not a free heal");
        }

        [Test]
        public void ShieldSystemsT2_RegenDelayIsSeven_EightTicksAlreadyRegens()
        {
            // Durability's own delay boundary needs (delaySeconds + 1) whole-second ticks
            // before regen visibly starts (the tick that brings the countdown to exactly 0
            // still returns without regenerating that same tick) -- matching the existing,
            // already-established 8s-delay convention (ShieldRegen_ResumesInSafeZone_AfterTheDelay
            // needs 9 ticks). A 7s delay therefore needs 8 ticks.
            var state = new ShipCombatState(1, 0, 2, 0); // Shield Systems T2 -- regen delay 7s
            state.ApplyDamage(50f); // shield max 600 (400*1.5 from T2), 600->550
            for (int i = 0; i < 8; i++) state.Tick(1f, Safe());
            Assert.Greater(state.Durability.Shield, 550f, "8 ticks must already be enough for the 7s T2 regen delay to have elapsed and regen to have started");
        }

        [Test]
        public void ShieldSystemsT0_RegenDelayIsEight_TheSameEightTicksIsNotEnoughYet()
        {
            var state = new ShipCombatState(1, 0, 0, 0); // no Shield Systems -- regen delay stays 8s
            state.ApplyDamage(50f); // 400->350
            for (int i = 0; i < 8; i++) state.Tick(1f, Safe());
            Assert.AreEqual(350f, state.Durability.Shield, 0.01f, "at T0 the 8s delay has NOT yet elapsed after only 8 ticks -- proving T2's delay is genuinely shorter, not a test artifact");
        }

        // --- VS-013.1 §5: Damaged effects + repair-clear ---------------------------

        [Test]
        public void TryStartBoost_RejectedWhileDamaged()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.RecoverToFractions(0.25f, 0.10f, 0f); // sets Damaged = true
            Assert.IsTrue(state.Damaged);
            Assert.IsFalse(state.TryStartBoost());
        }

        [Test]
        public void ClearDamagedIfHealedEnough_NoOp_BelowSixtyPercent()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.RecoverToFractions(0.25f, 0.10f, 0f); // Hull 25%, Armor 10%
            state.ClearDamagedIfHealedEnough();
            Assert.IsTrue(state.Damaged, "both layers must be under 60% -- still Damaged");
        }

        [Test]
        public void ClearDamagedIfHealedEnough_ClearsOnlyWhenBothLayersReachSixtyPercent()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.RecoverToFractions(0.25f, 0.10f, 0f);
            state.Durability.RepairExact(toArmor: 600f * 0.60f - 60f, toHull: 0f); // Armor -> exactly 60%, Hull still 25%
            state.ClearDamagedIfHealedEnough();
            Assert.IsTrue(state.Damaged, "Hull is still under 60% -- must stay Damaged");

            state.Durability.RepairExact(toArmor: 0f, toHull: 1000f * 0.60f - 250f); // Hull -> exactly 60%
            state.ClearDamagedIfHealedEnough();
            Assert.IsFalse(state.Damaged, "both layers are now at/above 60% -- Damaged must clear");
        }

        [Test]
        public void ApplyDamage_ResetsSinceLastDamageToZero()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            for (int i = 0; i < 20; i++) state.Tick(1f, Safe());
            Assert.Greater(state.SinceLastDamageSeconds, 10f);
            state.ApplyDamage(10f);
            Assert.AreEqual(0f, state.SinceLastDamageSeconds);
        }

        [Test]
        public void SinceLastDamageSeconds_AccumulatesOverTicks()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.ApplyDamage(10f);
            for (int i = 0; i < 8; i++) state.Tick(1f, Safe());
            Assert.AreEqual(8f, state.SinceLastDamageSeconds, 0.01f);
        }

        [Test]
        public void RestoreSinceLastDamage_SetsExactValue_NeverBelowZero()
        {
            var state = new ShipCombatState(1, 0, 0, 0);
            state.RestoreSinceLastDamage(-5f);
            Assert.AreEqual(0f, state.SinceLastDamageSeconds);
            state.RestoreSinceLastDamage(42f);
            Assert.AreEqual(42f, state.SinceLastDamageSeconds);
        }
    }
}
