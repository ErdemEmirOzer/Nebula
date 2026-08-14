using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Ship
{
    /// <summary>
    /// When an action's owner is listening.
    /// <para>
    /// This is the distinction the codebase was missing. Every key owner in the
    /// game already had a context — build mode, an open processing panel, a live
    /// package transaction, an open screen — but the context lived only inside
    /// the owner's own <c>Update</c>, as an <c>if</c>. Nothing could ask "who owns
    /// this key right now", so nothing could tell a real collision from two owners
    /// that can never be listening at the same moment.
    /// </para>
    /// </summary>
    public enum InputContext
    {
        /// <summary>Live whenever the HUD owns input. The lowest claim on a key.</summary>
        Global,

        /// <summary>Flight: thrust, roll, boost, look. Live with the HUD.</summary>
        Flight,

        /// <summary>Near a scanned planet. Live with the HUD, blocked in combat.</summary>
        Extraction,

        /// <summary>Inside extraction's build mode only.</summary>
        BuildMode,

        /// <summary>While the processing panel is open only.</summary>
        Processing,

        /// <summary>While a Quick Asset Package transaction is live only.</summary>
        QuickAssetPackage,

        /// <summary>At a System Logistics Center. Blocked in combat.</summary>
        Logistics,

        /// <summary>At a point of interest. Blocked in combat.</summary>
        Poi,

        /// <summary>Planet evacuation/reclaim. Blocked in combat.</summary>
        Evacuation,

        /// <summary>Combat. Live with the HUD; blocks every world-interaction context.</summary>
        Combat,

        /// <summary>The tutorial card, which reads keys over live gameplay.</summary>
        Tutorial,

        /// <summary>A management screen owns input (the shell raises InputGate).</summary>
        Menu,

        /// <summary>The strategic map screen owns input.</summary>
        StrategicMap,

        /// <summary>A rebind capture is armed inside the settings screen.</summary>
        Rebind,

        /// <summary>Development-build overlays. Never built in a release player.</summary>
        Debug,
    }

    /// <summary>How the key is read, which decides what "one press" even means.</summary>
    public enum InputPressType
    {
        /// <summary>GetKeyDown — the single frame the key goes down.</summary>
        Down,

        /// <summary>GetKey — every frame the key is held.</summary>
        Held,

        /// <summary>GetKeyDown plus a held modifier.</summary>
        Chord,

        /// <summary>An InputManager axis rather than a KeyCode.</summary>
        Axis,
    }

    /// <summary>One row of the input ownership matrix, as data the game itself reads.</summary>
    public sealed class InputActionDefinition
    {
        /// <summary>Stable identity. Never a display string, never a KeyCode — this is what a saved binding names.</summary>
        public string Id { get; }

        public InputContext Context { get; }

        public InputPressType Press { get; }

        /// <summary>What the player is told this action is. Turkish, like every other label.</summary>
        public string LabelTr { get; }

        /// <summary>True when a player may reassign it from the settings screen.</summary>
        public bool Rebindable { get; }

        /// <summary>True when the action only exists in a development build.</summary>
        public bool DebugOnly { get; }

        /// <summary>The combat action this row is, when it is one. Its key is read live from <see cref="CombatBindings"/>.</summary>
        public CombatAction? Combat { get; }

        /// <summary>
        /// True when the owner resolves this key through <see cref="NebulaInputRouter"/>
        /// rather than reading <c>Input</c> for it directly. Two arbitrated actions may
        /// share a key: the router hands the press to exactly one of them.
        /// </summary>
        public bool Arbitrated { get; }

        /// <summary>
        /// True for a key the game deliberately gives no single owner: Escape.
        /// <para>
        /// Every surface reads it as "back out of whatever <i>I</i> have open", and
        /// each only acts when it has something of its own to close — which is why
        /// <c>NebulaParentMenuScreen</c>'s own summary says Escape "already means five
        /// different cancels elsewhere and cannot be claimed". Two shared-intent rows
        /// on one key are the design, not a double-fire, so the collision scan passes
        /// over them; the audit lists them as their own category instead.
        /// </para>
        /// </summary>
        public bool SharedIntent { get; }

        /// <summary>
        /// True for the handful of rows the settings screen states in its controls
        /// summary. A flag on the catalog rather than a list on the screen: the screen
        /// choosing its own rows is how the summary and the game drifted apart the
        /// first time, and the summary is where a player looks when they have
        /// forgotten a key.
        /// <para>
        /// Kept to eight, deliberately. The settings page's second column already ran
        /// past the bottom of the page once with the audio buses on it, and a control
        /// summary a player cannot scroll to is worse than a shorter one. Escape is not
        /// among them because every screen's own action rail already prints
        /// "ESC  GERİ / KAPAT"; the full list lives in the shipped manual and in
        /// UIDesign/Finalization/INPUT-OWNERSHIP-AUDIT.md.
        /// </para>
        /// </summary>
        public bool Featured { get; }

        /// <summary>
        /// True when the real owner of this key is the project's own
        /// <c>ProjectSettings/InputManager.asset</c> — a Unity axis — and not a Nebula
        /// source file.
        /// <para>
        /// Without this flag the catalog misleads in both directions at once. A source
        /// scan cannot see a read that Unity's <c>StandaloneInputModule</c> performs
        /// inside the engine, so these rows look like catalog entries nothing consumes
        /// (they are live, and P1-DATA-001 was found precisely because the pad buttons
        /// were live and invisible); and a reader of the catalog is told the row is
        /// Nebula's to move when in fact rebinding it here would change the label and
        /// not the key. Marking them says which it is, once, where both the audit and
        /// the settings screen can read it.
        /// </para>
        /// <para>
        /// It is not "unread" and not "dead": <c>menu.submit</c> really does submit,
        /// through the Submit axis (<c>return</c> / <c>joystick button 0</c>), and
        /// <c>menu.navigate</c> really does move the selection, through
        /// Horizontal/Vertical. What the flag records is <i>who</i> reads it.
        /// </para>
        /// </summary>
        public bool UnityAxisOwned { get; }

        private readonly KeyCode fixedKey;
        private KeyCode? reboundKey;

        internal InputActionDefinition(string id, InputContext context, InputPressType press, string labelTr,
            KeyCode key, bool rebindable = false, bool debugOnly = false, CombatAction? combat = null,
            bool arbitrated = false, bool featured = false, bool sharedIntent = false,
            bool unityAxisOwned = false)
        {
            Id = id; Context = context; Press = press; LabelTr = labelTr;
            fixedKey = key; defaultKey = key; Rebindable = rebindable; DebugOnly = debugOnly; Combat = combat;
            Arbitrated = arbitrated; Featured = featured; SharedIntent = sharedIntent;
            UnityAxisOwned = unityAxisOwned;
        }

        private readonly KeyCode defaultKey;

        /// <summary>
        /// The key this action answers to right now. Combat rows read the live
        /// binding service — never a copy of it — so a rebind is visible here on the
        /// same frame it is taken.
        /// </summary>
        public KeyCode Key
        {
            get
            {
                if (!Combat.HasValue) return reboundKey ?? fixedKey;
                foreach ((CombatAction bound, KeyCode key) in CombatBindings.CurrentBindings())
                    if (bound == Combat.Value) return key;
                return KeyCode.None;
            }
        }

        /// <summary>The shipped key, ignoring any rebind. What "restore defaults" means.</summary>
        public KeyCode DefaultKey => defaultKey;

        /// <summary>A rebindable action is exactly the set that is written to the user settings store.</summary>
        public bool Persisted => Rebindable;

        internal void SetReboundKey(KeyCode key) => reboundKey = key;
        internal void ResetReboundKey() => reboundKey = null;

        public override string ToString() => $"{Id} [{Context}] {Key}";
    }

    /// <summary>
    /// The one catalog of every player-facing input action in the game.
    /// <para>
    /// It exists because the answer to "what owns this key" was spread over
    /// eighteen files, a hand-kept reserved-key array, a settings screen that
    /// re-stated the keys as strings, and three Turkish documents that re-stated
    /// them again. Four copies of one fact drift, and they had: the reserved list
    /// was missing four keys that really are owned, carried one (Tab) that nothing
    /// read at all, and the documents shipped a paragraph apologising for a
    /// double-fire nobody had a place to fix.
    /// </para>
    /// <para>
    /// Keys still live on their owners. This catalog does not restate them — it
    /// reads the owner's own constant — so there is still exactly one declaration
    /// per key. What is new here is the <i>context</i>, the stable action id, and
    /// the player-facing label, none of which existed anywhere before.
    /// </para>
    /// </summary>
    public static class NebulaInputActions
    {
        // ---- stable action ids -------------------------------------------------
        // Written down once, referenced everywhere, and persisted verbatim. Renaming
        // one of these is a save-compatibility change for the user settings store,
        // which is why they are constants rather than nameof() of anything.

        public const string FlightThrust = "flight.thrust";
        public const string FlightVertical = "flight.vertical";
        public const string FlightRoll = "flight.roll";
        public const string FlightBoost = "flight.boost";
        public const string FlightLook = "flight.look";
        public const string FlightZoom = "flight.zoom";
        public const string FlightReleaseCursor = "flight.release_cursor";
        /// <summary>
        /// H. Named for what it does — <c>ShipCruise.HandleHeadingLockInput</c> toggles
        /// Yön Sabitle, not cruise. It used to be <c>flight.cruise_toggle</c> / "Seyir
        /// Kipi", so the settings screen promised the player a cruise key that does not
        /// exist (deep-space cruise enters on conditions alone) and left the thing H
        /// really does with no name anywhere. Renaming the id is free here because the
        /// row was not rebindable and therefore never written to the settings store.
        /// </summary>
        public const string FlightHeadingLock = "flight.heading_lock";

        /// <summary>
        /// Deep Space Cruise — the other half of P1-COMBAT-005.
        /// <para>
        /// Renaming H told the truth about what H does; it left cruise with no control
        /// of any kind. Cruise engaged the moment eight world conditions happened to
        /// line up, and the only way out was to break one of them: chop the throttle,
        /// turn hard, or fly at something. The player could neither ask for it nor
        /// decline it, and the mode with the largest effect on how the ship moves was
        /// the one mode they could not address.
        /// </para>
        /// <para>
        /// This row is that address. It arms and disarms cruise; it does not force it,
        /// because the eight conditions still decide. A campaign that never touches the
        /// key behaves exactly as before — armed is the default — so nothing about the
        /// existing feel changes for anyone who does not use it, and pressing it during
        /// a cruise begins the ordinary deceleration rather than a snap.
        /// </para>
        /// <para>
        /// End rather than a letter because every letter on the keyboard is already
        /// spoken for in some context, and the two obvious spares are not spare: Tab is
        /// deliberately left unclaimed and Alpha9 is the free key rebinding tests
        /// assign to. It is rebindable, so a player who wants it under a thumb can put
        /// it there.
        /// </para>
        /// </summary>
        public const string FlightCruiseToggle = "flight.cruise_toggle";

        public const string ExtractionScan = "extraction.scan";
        public const string ExtractionBuildMode = "extraction.build_mode";
        public const string ExtractionConfirm = "extraction.confirm";
        public const string ExtractionTransfer = "extraction.transfer";
        public const string ExtractionCommand = "extraction.command";
        public const string ExtractionLaser = "extraction.laser";
        public const string ExtractionExit = "extraction.exit";
        public const string ExtractionPrevDefenseNode = "extraction.prev_defense_node";
        public const string ExtractionNextDefenseNode = "extraction.next_defense_node";

        public const string ProcessingPanel = "processing.panel";
        public const string ProcessingMakeParts = "processing.make_parts";
        public const string ProcessingMakeMaterial = "processing.make_material";
        public const string ProcessingPrevSlot = "processing.prev_slot";
        public const string ProcessingNextSlot = "processing.next_slot";

        public const string QuickPackageConfirm = "quick_package.confirm";
        public const string QuickPackageCancel = "quick_package.cancel";
        public const string QuickPackageCycleMaterial = "quick_package.cycle_material";

        public const string EvacuationEvacuate = "evacuation.evacuate";
        public const string EvacuationReclaim = "evacuation.reclaim";

        /// <summary>
        /// The other half of the C arbitration. Flight rows are normally referenced only
        /// inside this catalog and so carry inline ids, but this one is read from
        /// <c>ShipController.KeyboardFlightInput</c> as well, and a cross-file id that is
        /// a bare string is exactly the fragility a rename would break silently.
        /// </summary>
        public const string FlightVerticalDown = "flight.vertical_down";

        public const string LogisticsBuild = "logistics.build";
        public const string LogisticsWithdraw = "logistics.withdraw";
        public const string LogisticsDeposit = "logistics.deposit";
        public const string LogisticsDismantle = "logistics.dismantle";
        public const string LogisticsClose = "logistics.close";

        public const string PoiStart = "poi.start";
        public const string PoiSensorRelay = "poi.sensor_relay";
        public const string PoiCancel = "poi.cancel";

        public const string CombatFireLaser = "combat.fire_laser";
        public const string CombatFirePlasma = "combat.fire_plasma";
        public const string CombatFireEm = "combat.fire_em";
        public const string CombatBoost = "combat.boost";
        public const string CombatCycleTarget = "combat.cycle_target";
        public const string CombatManualTarget = "combat.manual_target";
        public const string CombatRepair = "combat.repair";
        public const string CombatCycleDefenseMode = "combat.cycle_defense_mode";
        public const string CombatSetFocusTarget = "combat.set_focus_target";
        public const string CombatCancelRepair = "combat.cancel_repair";

        public const string MenuProgression = "menu.progression";
        public const string MenuFleet = "menu.fleet";
        public const string MenuBack = "menu.back";
        public const string MenuNextPage = "menu.next_page";
        public const string MenuPrevPage = "menu.prev_page";
        public const string MenuNavigate = "menu.navigate";
        public const string MenuSubmit = "menu.submit";
        public const string MenuQuantityDecrease = "menu.quantity_decrease";
        public const string MenuQuantityIncrease = "menu.quantity_increase";
        public const string GamepadSubmit = "gamepad.submit";
        public const string GamepadCancel = "gamepad.cancel";

        public const string MapToggle = "map.toggle";
        public const string MapOverview = "map.overview";
        public const string MapFocus = "map.focus";
        public const string MapAnalysis = "map.analysis";
        public const string MapPlotRoute = "map.plot_route";
        public const string MapClearRoute = "map.clear_route";
        public const string MapClose = "map.close";

        public const string SystemManualSave = "system.manual_save";
        public const string SystemManualLoad = "system.manual_load";
        public const string SystemOrbitGuides = "system.orbit_guides";

        public const string TutorialAcknowledge = "tutorial.acknowledge";
        public const string TutorialAcknowledgeKeypad = "tutorial.acknowledge_keypad";
        public const string TutorialExit = "tutorial.exit";

        public const string RebindCancel = "rebind.cancel";

        public const string DebugSectorOverlay = "debug.sector_overlay";
        public const string DebugFlightSpeedPanel = "debug.flight_speed_panel";

        // ---- the shipped keys this catalog itself owns --------------------------
        // Only for actions whose owning file reads them back from here, so the value
        // is still declared exactly once. Every other row below reads the owner's own
        // constant instead.

        internal const KeyCode MenuProgressionDefault = KeyCode.U;
        internal const KeyCode MenuFleetDefault = KeyCode.Y;
        internal const KeyCode QuickPackageConfirmDefault = KeyCode.P;
        internal const KeyCode QuickPackageCancelDefault = KeyCode.U;
        internal const KeyCode QuickPackageCycleMaterialDefault = KeyCode.I;

        private static InputActionDefinition[] all;

        /// <summary>Every action, in reading order.</summary>
        public static IReadOnlyList<InputActionDefinition> All => all ??= Build();

        /// <summary>The definition with this id, or <c>null</c> — an unknown id is never an exception.</summary>
        public static InputActionDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            IReadOnlyList<InputActionDefinition> list = All;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Id, id, StringComparison.Ordinal)) return list[i];
            return null;
        }

        /// <summary>The key an action answers to right now, or <see cref="KeyCode.None"/> for an unknown id.</summary>
        public static KeyCode Key(string id) => Find(id)?.Key ?? KeyCode.None;

        /// <summary>
        /// Changes one semantic binding at its single catalog authority. Contextual
        /// keys and combat keys use the same collision model and persistence file.
        /// </summary>
        public static bool TrySetBinding(string actionId, KeyCode key)
        {
            InputActionDefinition action = Find(actionId);
            if (action == null || !action.Rebindable || key == KeyCode.None
                || CombatBindingValidator.IsFunctionKey(key)) return false;

            if (action.Combat.HasValue) return CombatBindings.TrySetBinding(action.Combat.Value, key);

            IReadOnlyList<InputActionDefinition> list = All;
            for (int i = 0; i < list.Count; i++)
            {
                InputActionDefinition other = list[i];
                if (other == action || other.Key != key || other.Press == InputPressType.Axis) continue;
                if (!CanCoOccur(action.Context, other.Context)) continue;
                if (action.Arbitrated && other.Arbitrated && Rank(action.Context) != Rank(other.Context)) continue;
                if (action.SharedIntent && other.SharedIntent) continue;
                return false;
            }

            action.SetReboundKey(key);
            return true;
        }

        public static void ResetRebindableToDefaults()
        {
            CombatBindings.ResetToDefaults();
            foreach (InputActionDefinition action in All) action.ResetReboundKey();
        }

        /// <summary>Every action a release build's settings screen may show. Debug rows are not merely hidden — they never reach the list.</summary>
        public static IEnumerable<InputActionDefinition> PlayerVisible(bool includeDebugOnly)
        {
            IReadOnlyList<InputActionDefinition> list = All;
            for (int i = 0; i < list.Count; i++)
            {
                InputActionDefinition action = list[i];
                if (action.DebugOnly && !includeDebugOnly) continue;
                if (action.Press == InputPressType.Axis) continue;
                yield return action;
            }
        }

        // ---- context rules -----------------------------------------------------

        /// <summary>True for a context that only exists while a full-screen surface owns input.</summary>
        public static bool IsScreenContext(InputContext context) =>
            context == InputContext.Menu || context == InputContext.StrategicMap || context == InputContext.Rebind;

        /// <summary>True for a context that is only live while its own sub-mode is open — build mode, the processing panel, a package transaction.</summary>
        public static bool IsSubModeContext(InputContext context) =>
            context == InputContext.BuildMode || context == InputContext.Processing
            || context == InputContext.QuickAssetPackage;

        /// <summary>
        /// Blocked by <see cref="CombatInputContext.WorldInteractionBlocked"/>. The
        /// list is the one that class's own summary names.
        /// </summary>
        public static bool IsWorldInteractionContext(InputContext context) =>
            context == InputContext.Extraction || context == InputContext.BuildMode
            || context == InputContext.Processing || context == InputContext.QuickAssetPackage
            || context == InputContext.Logistics || context == InputContext.Poi
            || context == InputContext.Evacuation;

        /// <summary>
        /// How strong a claim a context has on a shared key. A live specific context
        /// beats the global intent; that is the whole arbitration rule, and it is a
        /// declared property of the context rather than an ordering of <c>if</c>s.
        /// <para>
        /// Two sub-modes needed separating rather than sharing one number. Build mode
        /// and the processing panel can both be open — you can stand in build mode at a
        /// planet and press G — and they both read <c>[</c> and <c>]</c>. Left at equal
        /// rank that is a tie, and a tie is not something
        /// <see cref="NebulaInputRouter.OwnerOf"/> may resolve: it hands the key to
        /// nobody, so the press would silently do nothing while both are open. The
        /// panel is an overlay the player deliberately summoned and is reading right
        /// now, so while it is open the brackets are its slot selector; close it and
        /// build mode has them back for the defense node.
        /// </para>
        /// <para>
        /// The numbers are spaced so a later context can be slotted between two of
        /// these without renumbering the rest. Only their order is meaningful — one
        /// test asserts a sub-mode outranks <see cref="InputContext.Global"/>, and
        /// nothing anywhere depends on a particular value.
        /// </para>
        /// </summary>
        public static int Rank(InputContext context)
        {
            if (context == InputContext.Global) return 0;
            if (IsScreenContext(context)) return 40;
            if (context == InputContext.Processing) return 30;
            // Distinct ranks, not a shared 20: both claim F, and an equal rank makes
            // OwnerOf a tie that resolves to nobody — with a POI and a logistics centre
            // both in range, F silently did nothing at all. POI is the more specific
            // of the two, so it outranks the centre.
            if (context == InputContext.Poi) return 22;
            if (context == InputContext.Logistics) return 20;
            if (IsSubModeContext(context)) return 20;

            // Evacuation over Flight, and for the same reason POI beats Logistics:
            // C is down-thrust everywhere and "reclaim this planet" only while the
            // ship is holding station on one particular abandoned world it has
            // already targeted. That is the narrower, deliberately-entered claim, so
            // it takes the key for exactly as long as it is live and gives it back
            // the moment the planet is no longer the target. Left at the same 10 as
            // Flight the two would tie, and a tie is the silent no-op OwnerOf refuses
            // to guess its way out of — the defect this number exists to prevent.
            if (context == InputContext.Evacuation) return 18;

            // Combat's own rows share no key with a live non-combat owner today, but
            // a rank of its own costs nothing and keeps the invariant "no two
            // co-occurring contexts share a rank" true by construction rather than by
            // luck: Flight and Global are the two contexts combat can co-occur with,
            // and it now outranks both.
            if (context == InputContext.Combat) return 12;

            return 10;
        }

        /// <summary>
        /// Whether two contexts can both be listening on the same frame — the only
        /// question that decides whether a shared key is a collision or a coincidence.
        /// </summary>
        public static bool CanCoOccur(InputContext a, InputContext b)
        {
            // A screen suppresses every gameplay owner through InputGate.
            bool screenA = IsScreenContext(a), screenB = IsScreenContext(b);
            if (screenA != screenB) return false;

            // Two screens cannot both own input: ExclusiveModal and the shell stack
            // each allow exactly one. The exception is a capture armed inside the
            // settings page, which lives inside the menu that is already open.
            if (screenA && screenB)
                return a == b
                       || (a == InputContext.Menu && b == InputContext.Rebind)
                       || (a == InputContext.Rebind && b == InputContext.Menu);

            // Combat blocks every world-interaction owner, and only those.
            if (a == InputContext.Combat && IsWorldInteractionContext(b)) return false;
            if (b == InputContext.Combat && IsWorldInteractionContext(a)) return false;

            return true;
        }

        // ---- reserved keys -----------------------------------------------------

        /// <summary>
        /// Every key that may never be assigned to a combat action, derived rather
        /// than listed.
        /// <para>
        /// A combat action is live whenever the HUD owns input, so any non-combat
        /// owner that is also live then holds its key against combat permanently. An
        /// owner that only listens inside a screen does not: while that screen is up,
        /// <see cref="InputGate"/> has already stopped combat from reading anything.
        /// </para>
        /// </summary>
        public static IEnumerable<KeyCode> GloballyReservedKeys()
        {
            var seen = new HashSet<KeyCode>();
            IReadOnlyList<InputActionDefinition> list = All;
            for (int i = 0; i < list.Count; i++)
            {
                InputActionDefinition action = list[i];
                if (action.Context == InputContext.Combat) continue;
                if (IsScreenContext(action.Context)) continue;
                if (action.Press == InputPressType.Axis) continue;
                if (action.Key == KeyCode.None) continue;
                if (seen.Add(action.Key)) yield return action.Key;
            }

            // The chord modifiers are read by NebulaInputBindings itself rather than
            // being an action of their own, and a combat action on one of them would
            // fire on every manual save.
            KeyCode[] modifiers = { KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftCommand, KeyCode.RightCommand };
            for (int i = 0; i < modifiers.Length; i++)
                if (seen.Add(modifiers[i])) yield return modifiers[i];
        }

        /// <summary>
        /// The screen-only owner of a key, or <c>null</c>. These keys stay assignable
        /// — blocking them would be blocking without evidence — but the player is told
        /// what else uses them, because a key that does two things in two places is
        /// still something they should know before choosing it.
        /// </summary>
        public static InputActionDefinition ContextualRestriction(KeyCode key)
        {
            if (key == KeyCode.None) return null;
            IReadOnlyList<InputActionDefinition> list = All;
            InputActionDefinition found = null;
            for (int i = 0; i < list.Count; i++)
            {
                InputActionDefinition action = list[i];
                if (action.Key != key) continue;
                if (!IsScreenContext(action.Context)) return null; // a live owner reserves it outright
                found ??= action;
            }
            return found;
        }

        // ---- collisions --------------------------------------------------------

        /// <summary>One pair of actions that really can both fire on a single press.</summary>
        public readonly struct InputCollision
        {
            public readonly InputActionDefinition First;
            public readonly InputActionDefinition Second;
            public readonly KeyCode Key;

            public InputCollision(InputActionDefinition first, InputActionDefinition second, KeyCode key)
            { First = first; Second = second; Key = key; }

            public override string ToString() =>
                $"{Key}: {First.Id} [{First.Context}] vs {Second.Id} [{Second.Context}]";
        }

        /// <summary>
        /// Every real double-fire in the catalog: a shared key, two contexts that can
        /// both be listening, and no router arbitration between them. A pair that
        /// shares a key but is separated by context — or resolved by the router — is
        /// not here, because it is not a defect.
        /// </summary>
        public static IReadOnlyList<InputCollision> Collisions()
        {
            var found = new List<InputCollision>();
            IReadOnlyList<InputActionDefinition> list = All;
            for (int i = 0; i < list.Count; i++)
            {
                InputActionDefinition a = list[i];
                if (a.Press == InputPressType.Axis || a.Key == KeyCode.None) continue;
                for (int j = i + 1; j < list.Count; j++)
                {
                    InputActionDefinition b = list[j];
                    if (b.Press == InputPressType.Axis || b.Key != a.Key) continue;
                    if (!CanCoOccur(a.Context, b.Context)) continue;

                    // The router hands one press to exactly one owner, decided by
                    // context rank, so an arbitrated pair cannot double-fire.
                    if (a.Arbitrated && b.Arbitrated) continue;

                    // A chord needs its modifier held; a bare press of the same key is
                    // a different input entirely.
                    if (a.Press == InputPressType.Chord ^ b.Press == InputPressType.Chord) continue;

                    // Escape belongs to everyone on purpose — see SharedIntent.
                    if (a.SharedIntent && b.SharedIntent) continue;

                    found.Add(new InputCollision(a, b, a.Key));
                }
            }
            return found;
        }

        // ---- the catalog -------------------------------------------------------

        private static InputActionDefinition[] Build()
        {
            const InputPressType down = InputPressType.Down;
            const InputPressType held = InputPressType.Held;

            return new[]
            {
                // Flight — ShipController.cs:67-81,258,269-274,323; ShipCruise.cs:165
                New(FlightThrust, InputContext.Flight, held, "İtki (İleri/Geri/Yanal)", KeyCode.W),
                New(FlightVertical, InputContext.Flight, held, "Dikey İtki", KeyCode.Space),
                New(FlightRoll, InputContext.Flight, held, "Yatış", KeyCode.Q),
                New(FlightBoost, InputContext.Flight, held, "Hızlandırma", KeyCode.LeftShift),
                New(FlightLook, InputContext.Flight, InputPressType.Axis, "Bakış", KeyCode.None),
                New(FlightZoom, InputContext.Flight, InputPressType.Axis, "Yakınlaştırma", KeyCode.None),
                New(FlightReleaseCursor, InputContext.Flight, down, "İmleci Bırak", KeyCode.Escape, sharedIntent: true),
                New(FlightHeadingLock, InputContext.Flight, down, "Yön Sabitle", KeyCode.H,
                    rebindable: true, arbitrated: true),
                // P1-COMBAT-005: Deep Space Cruise's own row. Read by
                // ShipCruise.HandleCruiseArmInput; see FlightCruiseToggle for why the
                // key is End and why arming rather than forcing is the right verb.
                // Arbitrated even though nothing contests End today: a rebindable row
                // that is not arbitrated works only while its key stays uncontested, and
                // the whole point of a rebindable row is that the key does not stay put.
                New(FlightCruiseToggle, InputContext.Flight, down, "Seyir Kipi — Aç/Kapat", KeyCode.End,
                    rebindable: true, arbitrated: true),

                // The remaining flight keys are separate physical keys on one action
                // in code; each still needs its own row, or the reserved set loses it.
                New("flight.thrust_back", InputContext.Flight, held, "İtki — Geri", KeyCode.S),
                New("flight.thrust_left", InputContext.Flight, held, "İtki — Sol", KeyCode.A),
                New("flight.thrust_right", InputContext.Flight, held, "İtki — Sağ", KeyCode.D),
                // Arbitrated because it shares C with evacuation.reclaim. Flight ranks
                // below Evacuation, so it holds C everywhere except at a planet the
                // ship has already abandoned — which is the only place the reclaim
                // verb exists at all. See NebulaInputActions.Rank.
                New(FlightVerticalDown, InputContext.Flight, held, "Dikey İtki — Aşağı", KeyCode.C, arbitrated: true),
                New("flight.roll_right", InputContext.Flight, held, "Yatış — Sağ", KeyCode.E),

                // Extraction — ShipExtraction.cs:22-28,247-248
                New(ExtractionScan, InputContext.Extraction, held, "Tara", Extraction.ShipExtraction.ScanKey, rebindable: true, arbitrated: true, featured: true),
                New(ExtractionBuildMode, InputContext.Extraction, down, "Yörünge Sabitle / İnşa Kipi", Extraction.ShipExtraction.BuildModeKey, rebindable: true, arbitrated: true, featured: true),
                New(ExtractionConfirm, InputContext.Extraction, down, "Onayla / Kur", Extraction.ShipExtraction.ConfirmKey, rebindable: true, arbitrated: true, featured: true),
                New(ExtractionTransfer, InputContext.Extraction, held, "Aktar", Extraction.ShipExtraction.TransferKey, rebindable: true, arbitrated: true, featured: true),
                New(ExtractionCommand, InputContext.Extraction, down, "Komuta Yapısı Kur/Yükselt", Extraction.ShipExtraction.CommandKey, rebindable: true, arbitrated: true),
                New(ExtractionLaser, InputContext.Extraction, down, "Savunma Kur/Yükselt", Extraction.ShipExtraction.LaserKey, rebindable: true, arbitrated: true),
                New(ExtractionExit, InputContext.Extraction, down, "Çıkarımdan Çık", Extraction.ShipExtraction.ExitKey, sharedIntent: true),
                New(ExtractionPrevDefenseNode, InputContext.BuildMode, down, "Önceki Savunma Düğümü", KeyCode.LeftBracket, rebindable: true, arbitrated: true),
                New(ExtractionNextDefenseNode, InputContext.BuildMode, down, "Sonraki Savunma Düğümü", KeyCode.RightBracket, rebindable: true, arbitrated: true),

                // Processing — ShipProcessing.cs:18-22.
                // The panel key is Global, not Extraction: the bay is on the ship and
                // ShipProcessing has never required a target planet to open it. Filing
                // it under Extraction would have made the router refuse it in open
                // space, which is a behaviour change disguised as a routing change.
                New(ProcessingPanel, InputContext.Global, down, "İşleme Paneli", Extraction.ShipProcessing.PanelKey, rebindable: true, arbitrated: true),
                New(ProcessingMakeParts, InputContext.Processing, down, "Temel Parça Üret", Extraction.ShipProcessing.MakePartsKey, rebindable: true, arbitrated: true),
                New(ProcessingMakeMaterial, InputContext.Processing, down, "Özel Malzeme Üret", Extraction.ShipProcessing.MakeMaterialKey, rebindable: true, arbitrated: true),
                New(ProcessingPrevSlot, InputContext.Processing, down, "Önceki İşleme Yuvası", Extraction.ShipProcessing.PrevSlotKey, rebindable: true, arbitrated: true),
                New(ProcessingNextSlot, InputContext.Processing, down, "Sonraki İşleme Yuvası", Extraction.ShipProcessing.NextSlotKey, rebindable: true, arbitrated: true),

                // Quick Asset Package — ShipQuickAssetPackage.cs
                New(QuickPackageConfirm, InputContext.Extraction, down, "Hızlı Kurulum — Onayla / Yeniden Dene", QuickPackageConfirmDefault, rebindable: true, arbitrated: true),
                New(QuickPackageCancel, InputContext.QuickAssetPackage, down, "Hızlı Kurulum — İptal", QuickPackageCancelDefault, rebindable: true, arbitrated: true),
                New(QuickPackageCycleMaterial, InputContext.Extraction, down, "Hızlı Kurulum — Lazer Malzemesi", QuickPackageCycleMaterialDefault, rebindable: true, arbitrated: true),

                // Evacuation — ShipPlanetEvacuation.cs:18-19
                // Deliberately NOT arbitrated: nothing else claims V, and declaring an
                // arbitration would tie the first Tahliye press to a context that is
                // only live once the planet is already abandoned or the confirm window
                // is open. It still comes through the router — see
                // NebulaInputRouter.Owns for why an uncontested row needs no claim.
                New(EvacuationEvacuate, InputContext.Evacuation, down, "Gezegeni Tahliye Et", Extraction.ShipPlanetEvacuation.EvacuateKey, featured: true),
                New(EvacuationReclaim, InputContext.Evacuation, down, "Geri Al", Extraction.ShipPlanetEvacuation.ReclaimKey, arbitrated: true),

                // System Logistics Center — ShipLogisticsCenter.cs:34-37,91
                New(LogisticsBuild, InputContext.Logistics, down, "Lojistik Merkezi Kur/Yükselt", Logistics.ShipLogisticsCenter.BuildOrUpgradeKey, rebindable: true, arbitrated: true),
                New(LogisticsWithdraw, InputContext.Logistics, held, "Lojistik Merkezinden Çek", Logistics.ShipLogisticsCenter.WithdrawKey, rebindable: true, arbitrated: true),
                New(LogisticsDeposit, InputContext.Logistics, held, "Lojistik Merkezine Yatır", Logistics.ShipLogisticsCenter.DepositKey, rebindable: true, arbitrated: true),
                New(LogisticsDismantle, InputContext.Logistics, down, "Lojistik Merkezini Sök", Logistics.ShipLogisticsCenter.DismantleKey, rebindable: true, arbitrated: true),
                New(LogisticsClose, InputContext.Logistics, down, "Lojistik Merkezini Kapat", KeyCode.Escape, sharedIntent: true),

                // Points of interest — ShipPoi.cs:22-25
                New(PoiStart, InputContext.Poi, down, "Operasyonu Başlat", Poi.ShipPoi.StartOperationKey, rebindable: true, arbitrated: true),
                New(PoiSensorRelay, InputContext.Poi, down, "Sensör Rölesi", Poi.ShipPoi.RelayChoiceKey, rebindable: true, arbitrated: true),
                New(PoiCancel, InputContext.Poi, down, "Operasyonu İptal Et", Poi.ShipPoi.CancelKey, sharedIntent: true),

                // Combat — the nine rebindable actions, read live from CombatBindings
                CombatRow(CombatFireLaser, CombatAction.FireLaser, KeyCode.Mouse0),
                CombatRow(CombatFirePlasma, CombatAction.FirePlasma, KeyCode.Mouse1),
                CombatRow(CombatFireEm, CombatAction.FireEm, KeyCode.Mouse2),
                CombatRow(CombatBoost, CombatAction.CombatBoost, KeyCode.Alpha3),
                CombatRow(CombatCycleTarget, CombatAction.CycleTarget, KeyCode.Alpha4),
                CombatRow(CombatManualTarget, CombatAction.ManualTargetSelect, KeyCode.Alpha5),
                CombatRow(CombatRepair, CombatAction.Repair, KeyCode.Alpha6),
                CombatRow(CombatCycleDefenseMode, CombatAction.CycleDefenseMode, KeyCode.Alpha7),
                CombatRow(CombatSetFocusTarget, CombatAction.SetFocusTarget, KeyCode.Alpha8),
                New(CombatCancelRepair, InputContext.Combat, down, "Onarımı İptal Et", KeyCode.Escape, sharedIntent: true),

                // Parent command menu — NebulaParentMenuScreen.cs
                New(MenuProgression, InputContext.Global, down, "Komuta Menüsü — İlerleme", MenuProgressionDefault, arbitrated: true, featured: true),
                New(MenuFleet, InputContext.Global, down, "Komuta Menüsü — Filo", MenuFleetDefault, featured: true),
                New(MenuBack, InputContext.Menu, down, "Geri / Kapat", KeyCode.Escape, sharedIntent: true),
                New(MenuNextPage, InputContext.Menu, down, "Sonraki Sayfa", KeyCode.PageDown),
                New(MenuPrevPage, InputContext.Menu, down, "Önceki Sayfa", KeyCode.PageUp),
                // The four Unity-axis-owned rows. Their reader is
                // ProjectSettings/InputManager.asset — Horizontal/Vertical for
                // navigation, Submit (return / joystick button 0) and Cancel (escape /
                // joystick button 1) — consumed inside StandaloneInputModule. No Nebula
                // file reads them and none should: reimplementing menu navigation over
                // uGUI's own would be a second navigation authority. See
                // InputActionDefinition.UnityAxisOwned.
                New(MenuNavigate, InputContext.Menu, InputPressType.Axis, "Menüde Gez", KeyCode.None,
                    unityAxisOwned: true),

                New(MenuSubmit, InputContext.Menu, down, "Seç", KeyCode.Return, unityAxisOwned: true),
                New(MenuQuantityDecrease, InputContext.Menu, down, "Miktarı Azalt (Gamepad)", KeyCode.JoystickButton4),
                New(MenuQuantityIncrease, InputContext.Menu, down, "Miktarı Artır (Gamepad)", KeyCode.JoystickButton5),
                New(GamepadSubmit, InputContext.Menu, down, "Onayla (Gamepad)", KeyCode.JoystickButton0,
                    unityAxisOwned: true),
                New(GamepadCancel, InputContext.Menu, down, "Geri (Gamepad)", KeyCode.JoystickButton1,
                    unityAxisOwned: true),

                // Strategic map — StrategicMapScreen.cs:25,91,184-195
                New(MapToggle, InputContext.Global, down, "Stratejik Harita", UI.StrategicMap.StrategicMapScreen.ToggleKey, featured: true),
                New(MapOverview, InputContext.StrategicMap, down, "Genel Görünüm", KeyCode.Home),
                New(MapFocus, InputContext.StrategicMap, down, "Konuma Odaklan", KeyCode.F),
                New(MapAnalysis, InputContext.StrategicMap, down, "Analiz", KeyCode.A),
                New(MapPlotRoute, InputContext.StrategicMap, down, "Rota Çiz", KeyCode.Return),
                New(MapClearRoute, InputContext.StrategicMap, down, "Rotayı Temizle", KeyCode.Backspace),
                New("map.clear_route_delete", InputContext.StrategicMap, down, "Rotayı Temizle (Delete)", KeyCode.Delete),
                New(MapClose, InputContext.StrategicMap, down, "Haritayı Kapat", KeyCode.Escape, sharedIntent: true),

                // System shortcuts — NebulaInputBindings.cs; SystemStreamingDirector.cs:148
                New(SystemManualSave, InputContext.Global, InputPressType.Chord, "Elle Kaydet (Ctrl/Cmd + ,)", NebulaInputBindings.ManualSaveKey),
                New(SystemManualLoad, InputContext.Global, InputPressType.Chord, "Elle Yükle (Ctrl/Cmd + .)", NebulaInputBindings.ManualLoadKey),
                New(SystemOrbitGuides, InputContext.Global, down, "Yörünge Kılavuzları", KeyCode.O),

                // Tutorial — TutorialHud.cs:169-192. Read over live gameplay, with no
                // InputGate check, which is exactly why Enter is globally reserved.
                New(TutorialAcknowledge, InputContext.Tutorial, down, "Eğitim Adımını Onayla", UI.Tutorial.TutorialAcknowledgeInput.PrimaryKey),
                New(TutorialAcknowledgeKeypad, InputContext.Tutorial, down, "Eğitim Adımını Onayla (NUM)", UI.Tutorial.TutorialAcknowledgeInput.SecondaryKey),
                New(TutorialExit, InputContext.Tutorial, down, "Eğitimden Çık", KeyCode.Escape, sharedIntent: true),

                // Rebind capture. Carried by the shell's Back chain —
                // NebulaSettingsPage.HandleBack cancels an armed capture and stops
                // there — rather than by a key read inside the capture listener. The
                // listener used to sample Escape itself while the shell sampled the
                // same press, so one Escape closed two levels; this row is now the one
                // owner of that intent and NebulaControlBindingSection reads it for the
                // prompt it shows the player.
                New(RebindCancel, InputContext.Rebind, down, "Atamadan Vazgeç", KeyCode.Escape, sharedIntent: true),

                // Development-build overlays. Not built at all in a release player
                // (PresentationInstaller.cs:45,54, Debug.isDebugBuild).
                New(DebugSectorOverlay, InputContext.Debug, down, "Sektör Hata Ayıklama Katmanı",
                    NebulaInputBindings.SectorDebugToggleKey, debugOnly: true),
                New(DebugFlightSpeedPanel, InputContext.Debug, down, "Uçuş Test Hızı Paneli",
                    UI.FlightTestSpeedHud.ToggleKey, debugOnly: true),
            };
        }

        private static InputActionDefinition New(string id, InputContext context, InputPressType press,
            string labelTr, KeyCode key, bool debugOnly = false, bool rebindable = false, bool arbitrated = false,
            bool featured = false, bool sharedIntent = false, bool unityAxisOwned = false) =>
            new InputActionDefinition(id, context, press, labelTr, key, debugOnly: debugOnly, rebindable: rebindable,
                arbitrated: arbitrated, featured: featured, sharedIntent: sharedIntent,
                unityAxisOwned: unityAxisOwned);

        /// <summary>
        /// A combat row. The <paramref name="shipped"/> key is the catalog's copy of
        /// the service's default and exists so "restore defaults" and the docs have
        /// one answer; <c>CombatBindingCatalogTests</c> pins it against
        /// <see cref="CombatBindings.ResetToDefaults"/> so the two cannot drift.
        /// </summary>
        private static InputActionDefinition CombatRow(string id, CombatAction action, KeyCode shipped) =>
            new InputActionDefinition(id, InputContext.Combat, InputPressType.Down,
                CombatActionName(action), shipped, rebindable: true, combat: action, arbitrated: true);

        /// <summary>
        /// The player-facing name of a combat action, in Turkish.
        /// <para>
        /// Here rather than on the settings screen because the tutorial, the docs
        /// validation and the audit all need it, and a name that lives on one surface
        /// is a name the other three re-invent.
        /// </para>
        /// </summary>
        public static string CombatActionName(CombatAction action)
        {
            switch (action)
            {
                case CombatAction.FireLaser: return "Lazer Ateşle";
                case CombatAction.FirePlasma: return "Plazma Ateşle";
                case CombatAction.FireEm: return "EM Ateşle";
                case CombatAction.CombatBoost: return "Savaş Hızlandırma";
                case CombatAction.CycleTarget: return "Hedef Değiştir";
                case CombatAction.ManualTargetSelect: return "Elle Hedef Seç";
                case CombatAction.Repair: return "Onar";
                case CombatAction.CycleDefenseMode: return "Savunma Modu";
                case CombatAction.SetFocusTarget: return "Odak Hedefi Ata";
                default: return action.ToString();
            }
        }

        /// <summary>The catalog row for a combat action, or <c>null</c>.</summary>
        public static InputActionDefinition ForCombat(CombatAction action)
        {
            IReadOnlyList<InputActionDefinition> list = All;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Combat == action) return list[i];
            return null;
        }
    }
}
