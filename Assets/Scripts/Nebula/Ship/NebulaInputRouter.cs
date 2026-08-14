using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Ship
{
    /// <summary>
    /// Decides which single action a physical key belongs to right now.
    /// <para>
    /// Two owners used to read the same key in the same frame and both act on it:
    /// <c>U</c> cancelled a live Quick Asset Package <b>and</b> opened the command
    /// menu, because each owner asked <c>Input.GetKeyDown</c> for itself and neither
    /// could see the other. Unity gives no ordering guarantee between two
    /// <c>Update</c>s, so "whoever runs first wins" is not a fix — it is the same
    /// defect with a coin toss in front of it.
    /// </para>
    /// <para>
    /// The rule here is a property of the <i>context</i>, not of the call order: for
    /// a given key, the live claimant with the highest <see cref="NebulaInputActions.Rank"/>
    /// owns the press, and every other claimant is told no. A specific context that
    /// is actually open outranks the global intent; when it closes, the global intent
    /// has the key back. Ask twice in one frame and you get the same answer twice,
    /// whichever order the callers run in.
    /// </para>
    /// <para>
    /// Only contested keys need to come through here. An action whose key nobody else
    /// claims still reads <c>Input</c> directly, and the catalog's collision scan —
    /// enforced by a test — is what proves that set is empty.
    /// </para>
    /// </summary>
    public static class NebulaInputRouter
    {
        /// <summary>
        /// What makes a context live, asked at the moment the question is put.
        /// <para>
        /// A predicate rather than a flag somebody sets each frame. A flag is only
        /// correct if its owner's <c>Update</c> has already run this frame, and Unity
        /// promises no such thing — so on the frame a context opens or closes, half the
        /// callers would read yesterday's answer. Asking is order-independent, which is
        /// the entire point of routing the key through here.
        /// </para>
        /// </summary>
        private static readonly Dictionary<InputContext, System.Func<bool>> live =
            new Dictionary<InputContext, System.Func<bool>>();

        /// <summary>
        /// Where key-down samples come from. Legacy <c>Input</c> cannot be driven from
        /// a batch-mode test, so the arbitration is proven against a substituted
        /// keyboard while every rule about <i>who owns the press</i> stays on this side
        /// of the seam and runs identically either way. <c>null</c> is the real keyboard.
        /// </summary>
        public static System.Func<KeyCode, bool> Source { get; set; }

        /// <summary>Held-key source; null is the real keyboard.</summary>
        public static System.Func<KeyCode, bool> HeldSource { get; set; }

        private static bool Down(KeyCode key) =>
            Source != null ? Source(key) : Input.GetKeyDown(key);

        private static bool IsHeld(KeyCode key) =>
            HeldSource != null ? HeldSource(key) : Input.GetKey(key);

        /// <summary>Puts the real keyboard back. Called by tests; harmless in production.</summary>
        public static void ResetSourceToKeyboard() { Source = null; HeldSource = null; }

        /// <summary>
        /// Is this key physically down right now — an <b>observation</b>, not a claim
        /// on the press.
        /// <para>
        /// There is a real second question the arbitration model does not answer.
        /// "Who owns W?" is <see cref="Held"/>; "is the player touching the stick at
        /// all?" is this, and heading lock asks the second one: it drops as soon as the
        /// player supplies any steering of their own, whoever happens to own those keys.
        /// Routing that through <see cref="Held"/> would have heading lock <i>consume</i>
        /// the roll keys and stop the ship rolling.
        /// </para>
        /// <para>
        /// Callers pass a key read out of <see cref="NebulaInputActions"/>, never a
        /// literal, so a rebind still moves it — and the sample comes from
        /// <see cref="HeldSource"/>, so an observing caller is as drivable from a test
        /// as a commanding one.
        /// </para>
        /// </summary>
        public static bool KeyHeld(KeyCode key) => key != KeyCode.None && IsHeld(key);

        /// <summary>
        /// Did this key go down this frame — the <see cref="KeyHeld"/> counterpart,
        /// and an <b>observation</b> for the same reason.
        /// <para>
        /// Capturing the mouse is the caller this exists for. "Click the game view to
        /// fly" is not an action in the catalog and must not become one: the button it
        /// watches is also <c>combat.fire_laser</c>'s, and a second catalog row on that
        /// key would either be reported as a collision or hand the press to exactly one
        /// of them — while the truth is that the click both captures the pointer and
        /// must not fire, which is a rule about the <i>capture</i>, not about who owns
        /// the button. So the capture observes the button and
        /// <c>ShipController.PointerAimLive</c> is what keeps the same press out of the
        /// weapon, one frame later and independently of which <c>Update</c> ran first.
        /// </para>
        /// <para>
        /// The sample still comes through <see cref="Source"/>, so a test can press the
        /// capture click exactly as the player does.
        /// </para>
        /// </summary>
        public static bool KeyDown(KeyCode key) => key != KeyCode.None && Down(key);

        /// <summary>
        /// Declares how to tell whether a context is listening. Registered by the
        /// context's own owner — the object that already knows, and the only one that
        /// can know. Passing <c>null</c> withdraws the claim, which is what an owner
        /// being disabled or destroyed must do.
        /// </summary>
        public static void SetContextSource(InputContext context, System.Func<bool> isLive)
        {
            if (isLive == null) live.Remove(context);
            else live[context] = isLive;

            // The settled owner is "the arbitration given this frame's set of contexts",
            // so registering or withdrawing one invalidates it. Without this, a context
            // that appears mid-frame could not win a key that had already been resolved,
            // which is a different rule from the one the cache exists to enforce.
            //
            // It does not reopen the same-frame double-fire: that case is a context whose
            // *answer* changes (a package cancels itself) while its registration stands,
            // and this clears nothing there.
            ownerFrame = -1;
            ownerThisFrame.Clear();
        }

        /// <summary>True while the context's owner says it is listening.</summary>
        public static bool IsContextActive(InputContext context)
        {
            if (context == InputContext.Global) return !InputGate.Suppressed;
            return live.TryGetValue(context, out System.Func<bool> isLive) && isLive != null && isLive();
        }

        /// <summary>
        /// Withdraws every declared context. For teardown between tests and for a
        /// session ending — a context left live across a scene load would hand the
        /// next session's presses to an owner that no longer exists.
        /// </summary>
        public static void ResetContexts()
        {
            live.Clear();
            // The per-frame owner cache goes with them. A session that ends and one that
            // begins can share a frame number in the editor, and an owner decided under
            // the old session's contexts must never answer the new one's first press.
            ownerFrame = -1;
            ownerThisFrame.Clear();
        }

        /// <summary>
        /// The action that owns this key at this moment, or <c>null</c> when nothing
        /// live claims it. Pure: it reads no input and changes nothing, so a caller
        /// can ask it to explain itself without consuming anything.
        /// <para>
        /// Decided once per key per frame, and that is load-bearing rather than an
        /// optimisation. Contexts go live and dark <i>inside</i> a frame: press U while
        /// a Quick Asset Package is in flight and the package's own Update cancels it,
        /// which takes the QuickAssetPackage context dark — so the menu, asking the same
        /// question a few milliseconds later in the same frame, was told the key was now
        /// free and opened itself on the very press that cancelled the package. One
        /// press, two commands, and which two depended on script execution order.
        /// </para>
        /// <para>
        /// Answering from the first asker's view of the frame closes that: every
        /// claimant of a key gets one consistent answer for the whole frame, and a
        /// consumer acting on the press cannot hand the key on to the next claimant
        /// behind it. The cache is one entry per key touched this frame and is dropped
        /// the moment the frame number moves.
        /// </para>
        /// </summary>
        public static InputActionDefinition OwnerOf(KeyCode key) => ResolveOwner(key);

        /// <summary>
        /// The owner a press is consumed against: <see cref="OwnerOf"/> as it answered
        /// the first time it was asked this frame.
        /// <para>
        /// Only the press path uses this. <see cref="OwnerOf"/> stays live because it is
        /// a question — the rebind screen and the diagnostics ask "who holds this key
        /// now", and a settled answer would have them describe a world that has already
        /// moved on. Consuming a press is not a question but an event, and an event has
        /// to see one frame consistently.
        /// </para>
        /// </summary>
        private static InputActionDefinition SettledOwnerOf(KeyCode key)
        {
            if (key == KeyCode.None) return null;

            if (ownerFrame != Time.frameCount) { ownerFrame = Time.frameCount; ownerThisFrame.Clear(); }
            if (ownerThisFrame.TryGetValue(key, out InputActionDefinition settled)) return settled;

            InputActionDefinition resolved = ResolveOwner(key);
            ownerThisFrame[key] = resolved;
            return resolved;
        }

        private static int ownerFrame = -1;
        private static readonly Dictionary<KeyCode, InputActionDefinition> ownerThisFrame =
            new Dictionary<KeyCode, InputActionDefinition>();

        private static InputActionDefinition ResolveOwner(KeyCode key)
        {
            InputActionDefinition best = null;
            int bestRank = int.MinValue;
            bool tied = false;

            IReadOnlyList<InputActionDefinition> catalog = NebulaInputActions.All;
            for (int i = 0; i < catalog.Count; i++)
            {
                InputActionDefinition action = catalog[i];
                if (!action.Arbitrated || action.Key != key) continue;
                if (!IsContextActive(action.Context)) continue;

                int rank = NebulaInputActions.Rank(action.Context);
                if (rank < bestRank) continue;

                // Equal rank is a real ambiguity, not something to resolve by catalog
                // order. Two live contexts claiming one key with the same strength is a
                // gap in the ranking, and whichever row happened to be typed first is
                // not an answer to it — so nobody gets the press and the gap stays
                // visible instead of turning into a coin toss that looks deliberate.
                if (rank == bestRank && best != null && best.Id != action.Id) { tied = true; continue; }

                best = action; bestRank = rank; tied = false;
            }

            return tied ? null : best;
        }

        /// <summary>
        /// True on the frame the action's key went down <b>and</b> this action is the
        /// one that owns it. Every other claimant of the same key gets false on the
        /// same frame, so one press can only ever produce one command.
        /// </summary>
        public static bool Pressed(string actionId)
        {
            InputActionDefinition action = NebulaInputActions.Find(actionId);
            if (action == null) return false;

            KeyCode key = action.Key;
            if (key == KeyCode.None) return false;
            if (!Down(key)) return false;

            return Owns(action, key);
        }

        /// <summary>Held counterpart of <see cref="Pressed"/>, with the same single-owner arbitration.</summary>
        public static bool Held(string actionId)
        {
            InputActionDefinition action = NebulaInputActions.Find(actionId);
            if (action == null || action.Key == KeyCode.None || !IsHeld(action.Key)) return false;
            return Owns(action, action.Key);
        }

        /// <summary>
        /// Does this action get the press that just happened on <paramref name="key"/>?
        /// <para>
        /// An arbitrated row is the one <see cref="OwnerOf"/> named, and nobody else.
        /// An <b>unarbitrated</b> row is its own owner as long as no arbitrated
        /// claimant is live on that key right now — which is the correct general rule
        /// and not a loophole. Arbitration exists to settle a contest; a key with no
        /// live contest has nothing to settle, and requiring every routed action to
        /// declare itself arbitrated would mean declaring a contest that does not
        /// exist. What it buys is that an uncontested action can still come through
        /// the router — reading the catalog's current key and the substitutable source
        /// instead of <c>Input.GetKeyDown(SomeConst)</c> — and the moment a specific
        /// context does claim its key, it yields to it without either side changing.
        /// </para>
        /// <para>
        /// The collision scan is unaffected: two unarbitrated rows on one key both see
        /// <see cref="OwnerOf"/> return <c>null</c>, both fire, and that is still
        /// exactly the double-fire <c>NebulaInputActions.Collisions</c> reports.
        /// </para>
        /// </summary>
        private static bool Owns(InputActionDefinition action, KeyCode key)
        {
            // The action's own context first, and for every row rather than only for
            // the arbitrated ones. This used to fall straight through to the ownership
            // question, and for a non-arbitrated row that question is "does nothing
            // else claim this key" — which a suppressed context answers with *yes*,
            // because OwnerOf skips inactive contexts. So raising the input gate made
            // a non-arbitrated key more readable, not less: the strategic map opened
            // from underneath an open menu, and the same hole stood under every other
            // non-arbitrated row. Arbitrated rows were covered only by accident, via
            // OwnerOf returning null for their own suppressed context.
            //
            // Asked only of a context that has actually declared an owner. Five of the
            // fifteen contexts — Menu, Rebind, StrategicMap, Tutorial, Debug — call
            // SetContextSource nowhere in production, so "is this context live" has no
            // answer for them, and treating no-answer as "no" silently killed every row
            // they carry, page stepping included. Their rows keep the behaviour they
            // have always had; NebulaInputActionCatalogTests names them so the gap is
            // written down rather than discovered again the next time this line moves.
            //
            // Escape does not come through here — SharedPressed is its own read, for
            // the reason given there: every surface must answer it while a screen is
            // open, which is precisely when this gate is closed.
            if (action.Context == InputContext.Global || live.ContainsKey(action.Context))
                if (!IsContextActive(action.Context)) return false;

            InputActionDefinition owner = SettledOwnerOf(key);
            return action.Arbitrated ? owner != null && owner.Id == action.Id : owner == null;
        }

        /// <summary>
        /// The read for a <see cref="InputActionDefinition.SharedIntent"/> row —
        /// Escape, and only Escape.
        /// <para>
        /// It deliberately does <b>not</b> arbitrate. Escape means "back out of
        /// whatever <i>I</i> have open", every surface answers it, and each only acts
        /// when it has something of its own to close; handing the press to a single
        /// winner would leave the other six surfaces stuck open. That shield is the
        /// design, which is why the collision scan passes over shared-intent pairs.
        /// </para>
        /// <para>
        /// What this still buys over <c>Input.GetKeyDown(SomeConst)</c>, which is what
        /// every one of these call sites used to be: the key comes from the row rather
        /// than from a constant frozen at the call site, so it follows the catalog, and
        /// the sample comes from <see cref="Source"/>, so the cancel paths are drivable
        /// from a test exactly like every other action. A caller that is not a
        /// shared-intent row is refused rather than quietly given an unarbitrated read
        /// — that would be a back door around <see cref="Pressed"/>.
        /// </para>
        /// </summary>
        public static bool SharedPressed(string actionId)
        {
            InputActionDefinition action = NebulaInputActions.Find(actionId);
            if (action == null || !action.SharedIntent) return false;

            KeyCode key = action.Key;
            return key != KeyCode.None && Down(key);
        }
    }
}
