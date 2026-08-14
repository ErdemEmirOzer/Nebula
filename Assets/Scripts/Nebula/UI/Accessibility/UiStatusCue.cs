using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Visuals;

namespace Nebula.UI.Accessibility
{
    /// <summary>
    /// What a cue is saying, as opposed to how urgent it is.
    /// <para>
    /// <see cref="UiStatusLevel"/> already collapses every reading in the game
    /// onto six urgencies, and that is the right vocabulary for colour. It is
    /// the wrong vocabulary for a shape, because two things a player must never
    /// confuse both land on <see cref="UiStatusLevel.Inactive"/>: a link that
    /// has gone quiet and a control that is barred. "No signal" and "you may
    /// not do this" are different sentences and get different marks.
    /// </para>
    /// </summary>
    public enum UiCueKind
    {
        /// <summary>No mark. Either nothing to say, or the player turned cues off.</summary>
        None,

        /// <summary>Success, or a reading inside its normal band.</summary>
        Nominal,

        /// <summary>Worth noticing.</summary>
        Caution,

        /// <summary>Needs action now.</summary>
        Warning,

        /// <summary>Failing or failed.</summary>
        Critical,

        /// <summary>Repair, construction, upgrade — work in progress.</summary>
        Working,

        /// <summary>Off, or simply not running.</summary>
        Inactive,

        /// <summary>Out of contact. The reading is missing, not bad.</summary>
        CommunicationLoss,

        /// <summary>Understood and refused. The action exists; the player may not take it yet.</summary>
        Blocked
    }

    /// <summary>The mark itself, as geometry rather than as a sprite.</summary>
    public enum UiCueShape
    {
        None,

        /// <summary>Two strokes, a tick. Success.</summary>
        Check,

        /// <summary>Four strokes, a rotated square outline. Caution.</summary>
        Diamond,

        /// <summary>Three strokes, an upright triangle outline. Warning.</summary>
        Triangle,

        /// <summary>Two strokes crossed. Critical.</summary>
        Cross,

        /// <summary>Four strokes, a double chevron. Working.</summary>
        Chevron,

        /// <summary>One stroke, a flat dash. Inactive.</summary>
        Dash,

        /// <summary>Two collinear segments with a gap and a slash across it. Communication loss.</summary>
        BrokenLink,

        /// <summary>A square outline with a slash through it. Blocked.</summary>
        Barred
    }

    /// <summary>
    /// A cue a HUD can draw: a status level for the colour, a shape for the
    /// silhouette, and the state's own word for the text channel.
    /// <para>
    /// Three channels, because one is never enough — and three is what the
    /// approved style board asks for in as many words: <i>status badges — colour
    /// + shape + word, never colour alone</i>. Colour alone fails a colour-blind
    /// player, shape alone fails at the size a status dot is drawn at, and a word
    /// alone is slower to read than either. Every cue this type hands back
    /// carries all three or none of them.
    /// </para>
    /// <para>
    /// The words are the ones the component sheet already uses, so the running
    /// game and the approved reference say the same thing rather than two
    /// synonyms.
    /// </para>
    /// </summary>
    public readonly struct UiStatusCue
    {
        public readonly UiCueKind Kind;
        public readonly UiCueShape Shape;
        public readonly UiStatusLevel Level;

        /// <summary>The state's word, as the component sheet writes it.</summary>
        public readonly string Marker;

        private UiStatusCue(UiCueKind kind, UiCueShape shape, UiStatusLevel level, string marker)
        {
            Kind = kind;
            Shape = shape;
            Level = level;
            Marker = marker;
        }

        /// <summary>Nothing drawn. What every accessor returns when cues are switched off.</summary>
        public static UiStatusCue None =>
            new UiStatusCue(UiCueKind.None, UiCueShape.None, UiStatusLevel.Nominal, string.Empty);

        /// <summary>True when there is a mark to draw at all.</summary>
        public bool HasMark => Shape != UiCueShape.None;

        /// <summary>The colour this cue is drawn in — the one status table, after accessibility.</summary>
        public UnityEngine.Color Color => LightingUiPalette.Resolve(Level);

        // ---- the switch ------------------------------------------------------

        /// <summary>
        /// Whether the shape and marker channels are on.
        /// <para>
        /// This is the whole of the setting's effect and it is deliberately
        /// visual-only: colour, text and layout are identical either way, so a
        /// player who turns cues off loses a redundancy and never loses a fact.
        /// </para>
        /// </summary>
        public static bool Enabled => CombatAccessibilitySettings.AudioVisualCueForWarnings;

        // ---- from a status level --------------------------------------------

        /// <summary>The default cue for an urgency, when nothing more specific is known.</summary>
        public static UiStatusCue For(UiStatusLevel level)
        {
            switch (level)
            {
                case UiStatusLevel.Nominal: return Of(UiCueKind.Nominal);
                case UiStatusLevel.Caution: return Of(UiCueKind.Caution);
                case UiStatusLevel.Warning: return Of(UiCueKind.Warning);
                case UiStatusLevel.Critical: return Of(UiCueKind.Critical);
                case UiStatusLevel.Working: return Of(UiCueKind.Working);
                default: return Of(UiCueKind.Inactive);
            }
        }

        /// <summary>
        /// The cue for a kind, with its mark stripped when the player has cues
        /// off.
        /// <para>
        /// Stripped, not replaced by <see cref="None"/>: the level survives even
        /// when the mark does not, because the level is the colour channel and the
        /// switch is not a colour setting. Returning a blank cue here made every
        /// reading on the status legend resolve as Nominal the moment cues were
        /// turned off — a switch that claims to remove a redundancy and silently
        /// removed the primary channel instead.
        /// </para>
        /// </summary>
        public static UiStatusCue Of(UiCueKind kind)
        {
            if (kind == UiCueKind.None) return None;
            UiStatusCue cue = Authored(kind);
            return Enabled ? cue : cue.WithoutMark();
        }

        /// <summary>The same state, carrying its colour but drawing no mark.</summary>
        public UiStatusCue WithoutMark() =>
            new UiStatusCue(Kind, UiCueShape.None, Level, string.Empty);

        private static UiStatusCue Authored(UiCueKind kind)
        {
            switch (kind)
            {
                case UiCueKind.Nominal:
                    return new UiStatusCue(kind, UiCueShape.Check, UiStatusLevel.Nominal, "NOMİNAL");
                case UiCueKind.Caution:
                    return new UiStatusCue(kind, UiCueShape.Diamond, UiStatusLevel.Caution, "DİKKAT");
                case UiCueKind.Warning:
                    return new UiStatusCue(kind, UiCueShape.Triangle, UiStatusLevel.Warning, "UYARI");
                case UiCueKind.Critical:
                    return new UiStatusCue(kind, UiCueShape.Cross, UiStatusLevel.Critical, "KRİTİK");
                case UiCueKind.Working:
                    return new UiStatusCue(kind, UiCueShape.Chevron, UiStatusLevel.Working, "ÇALIŞIYOR");
                case UiCueKind.Inactive:
                    return new UiStatusCue(kind, UiCueShape.Dash, UiStatusLevel.Inactive, "DEVRE DIŞI");
                case UiCueKind.CommunicationLoss:
                    return new UiStatusCue(kind, UiCueShape.BrokenLink, UiStatusLevel.Inactive,
                        "BAĞLANTI YOK");
                case UiCueKind.Blocked:
                    return new UiStatusCue(kind, UiCueShape.Barred, UiStatusLevel.Warning, "KİLİTLİ");
                default:
                    return None;
            }
        }

        // ---- from the state the game already has ----------------------------
        // Every accessor below reads a value some existing system already
        // produces and routes it through LightingUiPalette, which is where the
        // thresholds live. Nothing new is measured, nothing is subscribed to, and
        // no event travels: this is a second reading of the same numbers the HUD
        // is already colouring, so a cue can never disagree with the colour beside
        // it.

        /// <summary>Shield, armour or hull as a fraction of its maximum.</summary>
        public static UiStatusCue ForDurability(float current, float max) =>
            For(LightingUiPalette.DurabilityLayer(current, max));

        /// <summary>Combat energy.</summary>
        public static UiStatusCue ForEnergy(float current, float max) =>
            For(LightingUiPalette.Energy(current, max));

        /// <summary>Hardpoint heat against its overheat point.</summary>
        public static UiStatusCue ForHeat(float current, float overheatAt) =>
            For(LightingUiPalette.Heat(current, overheatAt));

        /// <summary>Drill or Logistics Center fill.</summary>
        public static UiStatusCue ForStorage(float stored, float capacity) =>
            For(LightingUiPalette.Storage(stored, capacity));

        /// <summary>A structure or drill lifecycle.</summary>
        public static UiStatusCue ForStructure(DrillLifecycle state) =>
            For(LightingUiPalette.Structure(state));

        /// <summary>Planet or system control.</summary>
        public static UiStatusCue ForDominance(DominanceState state) =>
            For(LightingUiPalette.Dominance(state));

        /// <summary>
        /// A communication link. Out of contact gets its own mark rather than the
        /// Inactive dash, because "the reading is missing" and "the thing is off"
        /// are the two readings a player most needs to tell apart on a map.
        /// </summary>
        public static UiStatusCue ForCommunicationLink(bool live, bool hasLastKnown)
        {
            if (live) return Of(UiCueKind.Nominal);
            if (hasLastKnown) return Of(UiCueKind.Caution);
            return Of(UiCueKind.CommunicationLoss);
        }

        /// <summary>
        /// A control the player cannot use. An unavailable action is Blocked, not
        /// Inactive: the difference is whether there is a reason to read.
        /// </summary>
        public static UiStatusCue ForAvailability(bool available) =>
            available ? None : Of(UiCueKind.Blocked);

        /// <summary>
        /// A module upgrade, from the reason the progression service already gave.
        /// A refusal the player could act on reads as Blocked; one they cannot yet
        /// reach keeps the quieter Inactive dash.
        /// </summary>
        public static UiStatusCue ForProgression(UpgradeRejectReason reason, bool upgradeActive = false)
        {
            if (upgradeActive) return Of(UiCueKind.Working);
            if (reason == UpgradeRejectReason.InsufficientPoints
                || reason == UpgradeRejectReason.InsufficientResources
                || reason == UpgradeRejectReason.MilestoneNotMet)
                return Of(UiCueKind.Blocked);
            return For(LightingUiPalette.Progression(reason, false));
        }
    }
}
