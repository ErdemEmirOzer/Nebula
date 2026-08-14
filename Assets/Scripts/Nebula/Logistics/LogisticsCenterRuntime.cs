using UnityEngine;
using Nebula.Extraction;

namespace Nebula.Logistics
{
    public enum LogisticsCenterState { Constructing, Active, Disabled, Dismantled }

    /// <summary>
    /// Pure lifecycle + durability for the orbital Logistics Center (VS-011
    /// §2). Built directly at its anchor (no capsule-transit leg — it is not
    /// sent from the ship like a surface structure) so it starts straight in
    /// Constructing. No environment build-duration multiplier applies (it is
    /// orbital, not on a planet surface). While a T1-&gt;T2 upgrade is in
    /// progress the center stays fully Active at T1 the whole time; on
    /// completion Tier, capacity/route/dock caps and durability maxes jump to
    /// T2 atomically, preserving the current shield/armor/hull FRACTION (not
    /// raw value) so a freshly-built T1 doesn't suddenly read as "full" T2.
    /// </summary>
    public class LogisticsCenterRuntime
    {
        private const float DamageResumeSeconds = 8f;

        public LogisticsCenterTier Tier { get; private set; }
        public LogisticsCenterState State { get; private set; }
        public float BuildRemaining { get; private set; }
        public Durability Durability { get; private set; }

        public bool Upgrading { get; private set; }
        public float UpgradeRemaining { get; private set; }

        private float damageResumeTimer;

        public bool IsActive => State == LogisticsCenterState.Active;
        public bool IsDisabled => State == LogisticsCenterState.Disabled;

        public LogisticsCenterTierDef Def => LogisticsCenterCatalog.Get(Tier);

        /// <summary>Fresh T1 build.</summary>
        public LogisticsCenterRuntime(LogisticsCenterTierDef t1Def)
        {
            Tier = LogisticsCenterTier.T1;
            State = LogisticsCenterState.Constructing;
            BuildRemaining = t1Def.BuildSeconds;
            Durability = new Durability(t1Def.ShieldMax, t1Def.ArmorMax, t1Def.HullMax, 0.5f);
        }

        /// <summary>Save-restore constructor.</summary>
        public LogisticsCenterRuntime(LogisticsCenterTier tier, LogisticsCenterState state, float buildRemaining,
            Durability durability, bool upgrading, float upgradeRemaining)
        {
            Tier = tier; State = state; BuildRemaining = buildRemaining; Durability = durability;
            Upgrading = upgrading; UpgradeRemaining = upgradeRemaining;
        }

        public void Tick(float dt)
        {
            switch (State)
            {
                case LogisticsCenterState.Constructing:
                    if (damageResumeTimer > 0f) { damageResumeTimer -= dt; if (damageResumeTimer > 0f) break; }
                    BuildRemaining -= dt;
                    if (BuildRemaining <= 0f) { BuildRemaining = 0f; State = LogisticsCenterState.Active; }
                    break;

                case LogisticsCenterState.Active:
                    Durability.Tick(dt);
                    if (Durability.Disabled) { State = LogisticsCenterState.Disabled; break; }
                    if (Upgrading)
                    {
                        UpgradeRemaining -= dt;
                        if (UpgradeRemaining <= 0f) CompleteUpgrade();
                    }
                    break;

                case LogisticsCenterState.Disabled:
                    if (!Durability.Disabled) State = LogisticsCenterState.Active; // repaired back up
                    break;
            }
        }

        public bool CanCancelForRefund => State == LogisticsCenterState.Constructing;

        // --- T1 -> T2 upgrade --------------------------------------------------
        public bool CanStartUpgrade => Tier == LogisticsCenterTier.T1 && State == LogisticsCenterState.Active && !Upgrading;

        public void StartUpgrade()
        {
            if (!CanStartUpgrade) return;
            Upgrading = true;
            UpgradeRemaining = LogisticsCenterCatalog.T2.BuildSeconds;
        }

        /// <summary>Cancel refund is the caller's responsibility (cargo-side); this only clears the in-progress flag.</summary>
        public void CancelUpgrade()
        {
            Upgrading = false;
            UpgradeRemaining = 0f;
        }

        private void CompleteUpgrade()
        {
            LogisticsCenterTierDef t2 = LogisticsCenterCatalog.T2;
            float shieldFrac = Durability.ShieldMax > 0f ? Durability.Shield / Durability.ShieldMax : 0f;
            float armorFrac = Durability.ArmorMax > 0f ? Durability.Armor / Durability.ArmorMax : 0f;
            float hullFrac = Durability.HullMax > 0f ? Durability.Hull / Durability.HullMax : 0f;

            var newDurability = new Durability(t2.ShieldMax, t2.ArmorMax, t2.HullMax, 0f);
            newDurability.RestoreFromSave(t2.ShieldMax * shieldFrac, t2.ArmorMax * armorFrac, t2.HullMax * hullFrac, Durability.SavedRegenTimer);

            Tier = LogisticsCenterTier.T2;
            Durability = newDurability;
            Upgrading = false;
            UpgradeRemaining = 0f;
        }

        // --- Damage/repair (no combat yet — real API for a future consumer) ---
        public void ApplyDamage(float amount) => ApplyDamage(amount, Combat.DamageProfile.Neutral);

        /// <summary>VS-014 §6: real weapon damage entry point (Siege targets the Logistics Center by name) — routes through the shared DamageResolver.</summary>
        public void ApplyDamage(float rawAmount, Combat.DamageProfile profile)
        {
            Combat.DamageResolver.Apply(Durability, rawAmount, profile);
            if (Durability.Disabled) { State = LogisticsCenterState.Disabled; return; }
            if (State == LogisticsCenterState.Constructing) damageResumeTimer = DamageResumeSeconds;
        }

        public void Repair(float amount)
        {
            Durability.Repair(amount);
            if (State == LogisticsCenterState.Disabled && !Durability.Disabled) State = LogisticsCenterState.Active;
        }

        // --- Dismantle -----------------------------------------------------------
        public bool CanDismantle(bool inventoryEmpty, bool noActiveUnloadTransaction, bool noLoadedWaitingCollector) =>
            State != LogisticsCenterState.Dismantled && inventoryEmpty && noActiveUnloadTransaction && noLoadedWaitingCollector;

        public void Dismantle() => State = LogisticsCenterState.Dismantled;
    }
}
