using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Ship
{
    public enum CombatAction { FireLaser, FirePlasma, FireEm, CombatBoost, CycleTarget, ManualTargetSelect, Repair, CycleDefenseMode, SetFocusTarget }

    /// <summary>
    /// Pure conflict checker for combat key rebinding (VS-013.1 §3). Every
    /// physical key already owned by a non-combat system is permanently
    /// reserved and can never be assigned to a combat action; within combat
    /// itself no two actions may share a key. No UnityEngine.Input
    /// dependency in the check itself — testable without simulating input.
    /// </summary>
    public static class CombatBindingValidator
    {
        /// <summary>
        /// Every key a non-combat system already owns, <b>derived</b> from
        /// <see cref="NebulaInputActions"/> rather than typed out here.
        /// <para>
        /// This used to be a hand-kept array with the comment "kept in sync manually",
        /// and it was not: it was missing <c>[</c> and <c>]</c> (processing slots and
        /// build-mode defense nodes, both read over live gameplay) and Enter/keypad
        /// Enter (the tutorial card reads them over live gameplay too), while carrying
        /// Tab and right Shift, which nothing anywhere reads. A combat action could be
        /// bound to a key that really was taken, and was refused three keys that were
        /// free.
        /// </para>
        /// <para>
        /// The derivation is the rule itself: combat is live whenever the HUD owns
        /// input, so any non-combat owner that is also live then holds its key
        /// permanently. An owner that only listens while a screen is up does not,
        /// because <see cref="InputGate"/> has already stopped combat reading anything
        /// by then — those keys are a contextual note to the player, not a refusal.
        /// See <see cref="NebulaInputActions.ContextualRestriction"/>.
        /// </para>
        /// </summary>
        /// <para>
        /// Derived on every call, never cached. The keys this reads are the rows'
        /// <i>current</i> keys, so a rebind changes the answer — and a cache with no
        /// invalidation froze the first answer for the whole process. That is not a
        /// theoretical risk: it made a rebind taken early in a run reserve the new key
        /// and release the old one permanently, so a later rebind of the same action
        /// was refused as ReservedBySystem. This runs on the rebind path only, over
        /// roughly ninety rows — never per frame — so the derivation is the cheap
        /// option as well as the correct one.
        /// </para>
        /// </summary>
        public static KeyCode[] ReservedSystemKeys
        {
            get
            {
                var list = new List<KeyCode>(48);
                foreach (KeyCode key in NebulaInputActions.GloballyReservedKeys()) list.Add(key);
                return list.ToArray();
            }
        }

        public static bool IsReservedBySystem(KeyCode key)
        {
            KeyCode[] reserved = ReservedSystemKeys;
            for (int i = 0; i < reserved.Length; i++) if (reserved[i] == key) return true;
            return false;
        }

        /// <summary>
        /// The screen-only owner of a key, or <c>null</c>. Home, Backspace, Delete and
        /// the arrow keys land here: they are real bindings inside the strategic map
        /// and the command menu, and dead everywhere else. Blocking them outright
        /// would be blocking without evidence, so they stay assignable and the player
        /// is told what else uses them.
        /// </summary>
        public static InputActionDefinition ContextualOwnerOf(KeyCode key) =>
            NebulaInputActions.ContextualRestriction(key);

        /// <summary>
        /// True for F1–F12, which no player binding may ever be.
        /// <para>
        /// The rule lives here, in the shared authority, rather than on whichever
        /// screen happens to offer a rebind: "F1–F12 are not assignable" is a fact
        /// about the binding set, and a rule enforced only on one path is a rule any
        /// other caller walks straight past.
        /// </para>
        /// <para>
        /// Matched on the enum's own <c>ToString</c> rather than a range comparison.
        /// A range comparison would have to name the first and last function keys as
        /// <c>KeyCode</c> literals, and no runtime file under
        /// <c>Assets/Scripts/Nebula</c> may contain one of those — a rule enforced by
        /// <c>NoFunctionKeySourceReferencesTests</c> and <c>UiFinalNoFunctionKeyTests</c>,
        /// which read every runtime source file as raw text. Raw text means comments
        /// too: an earlier draft of this very paragraph quoted the literal in order to
        /// explain why it must not appear, and that quotation was itself an offence.
        /// The name match also keeps the plain <c>KeyCode.F</c> (a real player binding)
        /// and F13 upwards out of the rule.
        /// </para>
        /// </summary>
        public static bool IsFunctionKey(KeyCode key)
        {
            string name = key.ToString();
            if (name.Length < 2 || name[0] != 'F') return false;
            if (!int.TryParse(name.Substring(1), out int index)) return false;
            return index >= 1 && index <= 12;
        }

        /// <summary>True if assigning `key` to `action` would collide with a reserved system key or a DIFFERENT already-assigned combat action, or would bind a function key.</summary>
        public static bool WouldConflict(CombatAction action, KeyCode key, IEnumerable<(CombatAction action, KeyCode key)> currentBindings)
        {
            if (IsFunctionKey(key)) return true;
            if (IsReservedBySystem(key)) return true;
            foreach (var (otherAction, otherKey) in currentBindings)
                if (otherAction != action && otherKey == key) return true;
            return false;
        }
    }
}
