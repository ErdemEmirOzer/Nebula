using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Ship
{
    /// <summary>
    /// Semantic, reassignable combat input layer (VS-013.1 §3) — separate
    /// from <see cref="NebulaInputBindings"/> (that one is the small fixed
    /// set of non-combat system shortcuts). Every field here is a plain
    /// mutable property, not a const, so a future rebinding UI can change
    /// them at runtime without touching any consumer code. No F1-F12
    /// anywhere. VS-013.1 moved EM/cycle-target/manual-target off V/X/N
    /// (which collided with planet evacuation, System Logistics Center
    /// dismantle, and System Logistics Center deposit respectively) onto
    /// Mouse2/Alpha4/Alpha5, and added Repair on Alpha6 — see
    /// <see cref="CombatBindingValidator"/> for the full reserved-key list
    /// this was chosen against.
    /// </summary>
    public static class CombatBindings
    {
        /// <summary>Left mouse button — dual-purpose with ShipController's own cursor-capture-on-click: only ever reaches here once the cursor is already locked (ShipController only captures on click while the cursor is free).</summary>
        public static KeyCode FireLaserKey { get; set; } = KeyCode.Mouse0;
        public static KeyCode FirePlasmaKey { get; set; } = KeyCode.Mouse1;
        public static KeyCode FireEmKey { get; set; } = KeyCode.Mouse2;
        public static KeyCode CombatBoostKey { get; set; } = KeyCode.Alpha3;
        public static KeyCode CycleTargetKey { get; set; } = KeyCode.Alpha4;
        public static KeyCode ManualTargetSelectKey { get; set; } = KeyCode.Alpha5;
        public static KeyCode RepairKey { get; set; } = KeyCode.Alpha6;
        /// <summary>VS-014 §4: cycles the active raid target planet's defense mode (Role Priority -> Balanced -> Focus Target).</summary>
        public static KeyCode CycleDefenseModeKey { get; set; } = KeyCode.Alpha7;
        /// <summary>VS-014 §4: makes the ship's currently-selected enemy (Target) that planet's Focus Target.</summary>
        public static KeyCode SetFocusTargetKey { get; set; } = KeyCode.Alpha8;

        // The nine Held()/Pressed() readers that used to live here are gone. Part 3 moved
        // every combat read onto NebulaInputRouter.Pressed/Held(NebulaInputActions.Combat*)
        // — see ShipCombat.cs:236-237, 307, 312, 354, 372-374, 584 — because the router is
        // where key ownership is arbitrated between contexts and where the substitutable
        // input Source lives. Leaving the old readers standing kept a second, unarbitrated
        // read path for the same nine keys: it had no callers, but anything reaching for it
        // would have bypassed both the arbitration and the test seam without any diagnostic
        // saying so. The class stays as the binding STORE (the key properties below,
        // CurrentBindings, TrySetBinding, ResetToDefaults), which is very much alive.

        public static IEnumerable<(CombatAction action, KeyCode key)> CurrentBindings()
        {
            yield return (CombatAction.FireLaser, FireLaserKey);
            yield return (CombatAction.FirePlasma, FirePlasmaKey);
            yield return (CombatAction.FireEm, FireEmKey);
            yield return (CombatAction.CombatBoost, CombatBoostKey);
            yield return (CombatAction.CycleTarget, CycleTargetKey);
            yield return (CombatAction.ManualTargetSelect, ManualTargetSelectKey);
            yield return (CombatAction.Repair, RepairKey);
            yield return (CombatAction.CycleDefenseMode, CycleDefenseModeKey);
            yield return (CombatAction.SetFocusTarget, SetFocusTargetKey);
        }

        /// <summary>Rejects (returns false, binding left unchanged) if `key` is reserved by another system or already used by a different combat action — the pure validator this delegates to is CombatBindingValidator, independently unit-testable.</summary>
        public static bool TrySetBinding(CombatAction action, KeyCode key)
        {
            if (CombatBindingValidator.WouldConflict(action, key, CurrentBindings())) return false;
            switch (action)
            {
                case CombatAction.FireLaser: FireLaserKey = key; break;
                case CombatAction.FirePlasma: FirePlasmaKey = key; break;
                case CombatAction.FireEm: FireEmKey = key; break;
                case CombatAction.CombatBoost: CombatBoostKey = key; break;
                case CombatAction.CycleTarget: CycleTargetKey = key; break;
                case CombatAction.ManualTargetSelect: ManualTargetSelectKey = key; break;
                case CombatAction.Repair: RepairKey = key; break;
                case CombatAction.CycleDefenseMode: CycleDefenseModeKey = key; break;
                case CombatAction.SetFocusTarget: SetFocusTargetKey = key; break;
            }
            return true;
        }

        /// <summary>Resets every binding back to its default — for tests and any future "restore defaults" UI action.</summary>
        public static void ResetToDefaults()
        {
            FireLaserKey = KeyCode.Mouse0;
            FirePlasmaKey = KeyCode.Mouse1;
            FireEmKey = KeyCode.Mouse2;
            CombatBoostKey = KeyCode.Alpha3;
            CycleTargetKey = KeyCode.Alpha4;
            ManualTargetSelectKey = KeyCode.Alpha5;
            RepairKey = KeyCode.Alpha6;
            CycleDefenseModeKey = KeyCode.Alpha7;
            SetFocusTargetKey = KeyCode.Alpha8;
        }
    }
}
