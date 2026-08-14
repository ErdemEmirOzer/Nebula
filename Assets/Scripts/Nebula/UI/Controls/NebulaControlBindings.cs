using System.Collections.Generic;
using UnityEngine;
using Nebula.Ship;

namespace Nebula.UI.Controls
{
    /// <summary>Why a rebind was refused, or that it was taken.</summary>
    public enum ControlRebindResult
    {
        /// <summary>The binding service took the key.</summary>
        Accepted,

        /// <summary>F1–F12. Developer overlays, never a player binding.</summary>
        FunctionKey,

        /// <summary>Owned by a non-combat system: flight, extraction, logistics, save/load.</summary>
        ReservedBySystem,

        /// <summary>Already assigned to a different combat action.</summary>
        ConflictsWithAction,

        /// <summary>Nothing was captured, or the service refused for a reason this layer cannot name.</summary>
        Refused,
    }

    /// <summary>
    /// The presentation layer over the rebinding service.
    /// <para>
    /// It introduces <b>no</b> binding authority of its own. <see cref="CombatBindings"/>
    /// stores the keys, <see cref="CombatBindingValidator"/> decides what may be
    /// bound — conflicts, reserved system keys and F1–F12 alike — and
    /// <see cref="NebulaInputBindings"/> owns the fixed system shortcuts.
    /// </para>
    /// <para>
    /// What this class adds is what a screen needs and a service should not carry: a
    /// Turkish name per action, a readable name per key, and the ability to say
    /// <i>which</i> refusal it was. The validator answers one boolean, correctly, for
    /// every caller in the process; a player needs a sentence, and only the surface
    /// they are looking at can write it.
    /// </para>
    /// </summary>
    public static class NebulaControlBindings
    {
        /// <summary>Every rebindable action, in the order the screen lists them.</summary>
        public static IReadOnlyList<CombatAction> Actions => ActionOrder;

        private static readonly CombatAction[] ActionOrder =
        {
            CombatAction.FireLaser,
            CombatAction.FirePlasma,
            CombatAction.FireEm,
            CombatAction.CombatBoost,
            CombatAction.CycleTarget,
            CombatAction.ManualTargetSelect,
            CombatAction.Repair,
            CombatAction.CycleDefenseMode,
            CombatAction.SetFocusTarget,
        };

        /// <summary>The key an action is bound to right now, read from the service the combat code reads.</summary>
        public static KeyCode KeyFor(CombatAction action)
        {
            foreach ((CombatAction bound, KeyCode key) in CombatBindings.CurrentBindings())
                if (bound == action) return key;
            return KeyCode.None;
        }

        /// <summary>
        /// Player-facing name of the action. Turkish, like every other label.
        /// <para>
        /// Forwarded to the action catalog rather than held here. The tutorial prompts,
        /// the audit and the docs-drift check all need these names, and a name that
        /// lives on one screen is a name the other three write out again in their own
        /// words.
        /// </para>
        /// </summary>
        public static string ActionName(CombatAction action) => NebulaInputActions.CombatActionName(action);

        /// <summary>
        /// What the player calls a group of bindings — the settings rail's own words
        /// for an <see cref="InputContext"/>.
        /// <para>
        /// The enum name is never shown. <c>QuickAssetPackage</c> is a type in this
        /// codebase and "Hızlı Kurulum" is a thing in the game, and only one of those
        /// two belongs on a screen. Every context the rail can offer has an entry; the
        /// fallback exists so a context added later shows something readable rather
        /// than a class name, and a test asserts the table is complete so the fallback
        /// is never what a player actually sees.
        /// </para>
        /// </summary>
        public static string GroupName(InputContext context)
        {
            switch (context)
            {
                case InputContext.Global: return "Genel";
                case InputContext.Flight: return "Uçuş";
                case InputContext.Extraction: return "Çıkarım";
                case InputContext.BuildMode: return "İnşa Kipi";
                case InputContext.Processing: return "İşleme";
                case InputContext.QuickAssetPackage: return "Hızlı Kurulum";
                case InputContext.Logistics: return "Lojistik Merkezi";
                case InputContext.Poi: return "İlgi Noktası";
                case InputContext.Evacuation: return "Tahliye";
                case InputContext.Combat: return "Muharebe";
                case InputContext.Tutorial: return "Eğitim";
                case InputContext.Menu: return "Komuta Menüsü";
                case InputContext.StrategicMap: return "Stratejik Harita";
                case InputContext.Rebind: return "Tuş Atama";
                case InputContext.Debug: return "Geliştirici";
                default: return "Diğer";
            }
        }

        /// <summary>
        /// How a key reads on screen.
        /// <para>
        /// <c>KeyCode.Mouse0.ToString()</c> is "Mouse0" and <c>KeyCode.Alpha6</c> is
        /// "Alpha6"; both are engine identifiers, and neither is what is printed on
        /// the thing the player presses. Six of the nine default combat bindings are
        /// one of those two families, so without this table two thirds of the screen
        /// would be quoting names no player could match to their hardware.
        /// </para>
        /// </summary>
        public static string KeyLabel(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.None: return "—";
                case KeyCode.Mouse0: return "SOL FARE";
                case KeyCode.Mouse1: return "SAĞ FARE";
                case KeyCode.Mouse2: return "ORTA FARE";
                case KeyCode.Mouse3: return "FARE 4";
                case KeyCode.Mouse4: return "FARE 5";
                case KeyCode.Mouse5: return "FARE 6";
                case KeyCode.Mouse6: return "FARE 7";
                case KeyCode.Space: return "BOŞLUK";
                case KeyCode.Escape: return "ESC";
                case KeyCode.Return: return "ENTER";
                case KeyCode.Tab: return "TAB";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.LeftShift: return "SOL SHIFT";
                case KeyCode.RightShift: return "SAĞ SHIFT";
                case KeyCode.LeftControl: return "SOL CTRL";
                case KeyCode.RightControl: return "SAĞ CTRL";
                case KeyCode.LeftCommand: return "SOL CMD";
                case KeyCode.RightCommand: return "SAĞ CMD";
            }

            string name = key.ToString();

            // Alpha6 is "6" and Keypad6 is "NUM 6". Both reach the player as the
            // digit they actually press.
            if (name.StartsWith("Alpha") && name.Length > 5) return name.Substring(5);
            if (name.StartsWith("Keypad") && name.Length > 6) return "NUM " + name.Substring(6);

            return Design.NebulaText.UpperAscii(name);
        }

        /// <summary>
        /// True for F1–F12, forwarded from the binding authority.
        /// <para>
        /// Forwarded rather than re-implemented on purpose. A screen that carried its
        /// own copy of this rule would be a screen that could disagree with the
        /// service about what is bindable — and the copy that mattered would be the
        /// one in the service, because every other caller only ever reaches that one.
        /// </para>
        /// </summary>
        public static bool IsFunctionKey(KeyCode key) => CombatBindingValidator.IsFunctionKey(key);

        /// <summary>
        /// True when a key already belongs to a non-combat system that is live at the
        /// same time combat is.
        /// <para>
        /// The four system shortcuts used to be re-checked here on top of the
        /// validator's own list, because that list was hand-kept and could miss one.
        /// It is derived from the action catalog now, and the catalog carries the
        /// system shortcuts, so re-stating them here would only create a second place
        /// that could disagree with the first.
        /// </para>
        /// </summary>
        public static bool IsReserved(KeyCode key) => CombatBindingValidator.IsReservedBySystem(key);

        /// <summary>
        /// A key that is free to bind but is also used somewhere the player will
        /// recognise — inside the map, or inside the command menu. Returns the note to
        /// show, or <c>null</c> when there is nothing to say.
        /// <para>
        /// A warning rather than a refusal, on purpose. Those surfaces suppress
        /// gameplay input while they are open, so the two uses can never fire on one
        /// press; refusing the key would be the settings screen inventing a conflict
        /// that does not exist.
        /// </para>
        /// </summary>
        public static string ContextNote(KeyCode key)
        {
            InputActionDefinition owner = CombatBindingValidator.ContextualOwnerOf(key);
            if (owner == null) return null;

            string where = owner.Context == InputContext.StrategicMap ? "STRATEJİK HARİTADA"
                : owner.Context == InputContext.Rebind ? "TUŞ ATAMA EKRANINDA"
                : "KOMUTA MENÜSÜNDE";
            return $"{KeyLabel(key)} AYRICA {where} KULLANILIYOR — ÇAKIŞMAZ";
        }

        /// <summary>
        /// The conflicts this screen is responsible for: a key two combat actions both
        /// hold, or a combat key that a live non-combat owner already has.
        /// <para>
        /// Deliberately narrower than <see cref="NebulaInputActions.Collisions"/>. That
        /// scan is the audit of the whole game and includes shared keys on surfaces
        /// this screen cannot rebind — reporting those here would be a permanent
        /// warning about something the player is being shown no way to fix, which
        /// teaches them to ignore the line that will one day matter.
        /// </para>
        /// <para>
        /// <see cref="TryRebind"/> refuses both of these, so in normal play the list is
        /// empty. It is not decoration: <see cref="CombatBindings"/> exposes every key
        /// as a settable property, so anything writing one directly — a future surface,
        /// a migration, a settings file read by a build that has since moved a key —
        /// lands here instead of silently double-firing in combat.
        /// </para>
        /// </summary>
        public static IReadOnlyList<string> ConflictWarnings()
        {
            var lines = new List<string>();
            var bindings = new List<(CombatAction action, KeyCode key)>();
            foreach ((CombatAction action, KeyCode key) in CombatBindings.CurrentBindings())
                bindings.Add((action, key));

            for (int i = 0; i < bindings.Count; i++)
            {
                (CombatAction action, KeyCode key) = bindings[i];
                if (key == KeyCode.None) continue;

                for (int j = i + 1; j < bindings.Count; j++)
                {
                    if (bindings[j].key != key) continue;
                    lines.Add($"{KeyLabel(key)}: {Design.NebulaText.Upper(ActionName(action))}" +
                              $" / {Design.NebulaText.Upper(ActionName(bindings[j].action))}");
                }

                if (IsReserved(key))
                    lines.Add($"{KeyLabel(key)}: {Design.NebulaText.Upper(ActionName(action))} / BAŞKA BİR SİSTEM");
            }

            return lines;
        }

        /// <summary>The combat action already holding this key, or <c>null</c>.</summary>
        public static CombatAction? HolderOf(KeyCode key, CombatAction excluding)
        {
            foreach ((CombatAction action, KeyCode bound) in CombatBindings.CurrentBindings())
                if (action != excluding && bound == key) return action;
            return null;
        }

        /// <summary>
        /// What would happen, without changing anything. The screen uses this to
        /// explain a refusal in the same words a refused attempt produces, so the
        /// preview and the outcome can never disagree.
        /// </summary>
        public static ControlRebindResult Check(CombatAction action, KeyCode key)
        {
            if (key == KeyCode.None) return ControlRebindResult.Refused;
            if (IsFunctionKey(key)) return ControlRebindResult.FunctionKey;
            if (IsReserved(key)) return ControlRebindResult.ReservedBySystem;
            if (HolderOf(key, action) != null) return ControlRebindResult.ConflictsWithAction;
            return ControlRebindResult.Accepted;
        }

        /// <summary>
        /// Applies a rebind through the service, or refuses without touching it.
        /// <para>
        /// The service is asked even after <see cref="Check"/> has passed, and its
        /// answer is what decides the result. Re-deriving "it would have worked"
        /// from the UI's own copy of the rules is exactly how a screen ends up
        /// reporting a change the game never made.
        /// </para>
        /// </summary>
        public static ControlRebindResult TryRebind(CombatAction action, KeyCode key)
        {
            ControlRebindResult predicted = Check(action, key);
            if (predicted != ControlRebindResult.Accepted) return predicted;

            if (!CombatBindings.TrySetBinding(action, key)) return ControlRebindResult.Refused;

            // Written the moment it is taken, not on some later "apply". There is no
            // apply step on this screen — the change is live in combat on the next
            // frame — so a separate commit would be a promise the interface does not
            // make, and the one a player closing the game would never reach.
            NebulaInputBindingStore.Save();
            return ControlRebindResult.Accepted;
        }

        /// <summary>Restores every combat binding to its shipped default, and forgets the saved layout.</summary>
        public static void ResetToDefaults()
        {
            CombatBindings.ResetToDefaults();
            NebulaInputBindingStore.Clear();
        }

        /// <summary>What the player is told, in their language.</summary>
        public static string ResultText(ControlRebindResult result, CombatAction action, KeyCode key)
        {
            switch (result)
            {
                case ControlRebindResult.Accepted:
                {
                    string note = ContextNote(key);
                    string taken = $"{Design.NebulaText.Upper(ActionName(action))} → {KeyLabel(key)}";
                    return note == null ? taken : $"{taken}  ·  {note}";
                }
                case ControlRebindResult.FunctionKey:
                    return "F1–F12 TUŞLARI ATANAMAZ";
                case ControlRebindResult.ReservedBySystem:
                    return $"{KeyLabel(key)} BAŞKA BİR SİSTEME AİT";
                case ControlRebindResult.ConflictsWithAction:
                {
                    CombatAction? holder = HolderOf(key, action);
                    return holder.HasValue
                        ? $"{KeyLabel(key)} ZATEN {Design.NebulaText.Upper(ActionName(holder.Value))} TUŞU"
                        : $"{KeyLabel(key)} ZATEN KULLANILIYOR";
                }
                default:
                    return "TUŞ ATANAMADI";
            }
        }
    }
}
