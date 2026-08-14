using UnityEngine;
using Nebula.Visuals;

namespace Nebula.UI.Design
{
    /// <summary>
    /// The one type-safe source of truth for the interface's colour, type,
    /// spacing, shape and motion. Mirrors <c>UIDesign/DesignSystem/tokens.css</c>
    /// value for value, so an approved mockup and the running game cannot drift.
    /// <para>
    /// This class deliberately owns only the things that had no owner before —
    /// surfaces, borders, spacing, shape and type. It is <b>not</b> a second
    /// authority for anything that already has one:
    /// </para>
    /// <list type="bullet">
    /// <item>status colour delegates to <see cref="LightingUiPalette"/>, so the
    /// accessibility response (flash reduction, safe mode, high contrast) keeps
    /// reaching the interface through the path it already travels;</item>
    /// <item>motion duration and easing delegate to <see cref="UiAnimationProfiles"/>,
    /// so Motion Off / Reduced Motion stay in one place.</item>
    /// </list>
    /// <para>
    /// Pure and allocation-free: every member is a readonly value or a lookup
    /// over them, safe to call from a per-frame refresh.
    /// </para>
    /// </summary>
    public static class NebulaUiDesignTokens
    {
        // ---- HIGH CONTRAST ---------------------------------------------------

        /// <summary>
        /// True when the interface should draw from its second, high-contrast
        /// palette. Read on every token access rather than cached, because the
        /// setting is a switch the player flips mid-session.
        /// </summary>
        public static bool HighContrast => LightingAccessibility.HighContrast;

        /// <summary>Picks between the authored palette and the high-contrast one.</summary>
        private static Color Pick(Color normal, Color contrast) => HighContrast ? contrast : normal;

        // ---- SURFACE ---------------------------------------------------------
        // A near-black obsidian family. The game's icons already carry all of the
        // saturation and gloss, so the chrome around them is a matte mount rather
        // than a competitor -- that is what keeps a saturated rarity hexagon and a
        // line-art status glyph legible on the same panel.
        //
        // High Contrast is a second set of authored values, not a multiplier on
        // the first. The surfaces go down toward black while the inks and edges go
        // up: a tint moves both ends the same way and therefore changes no ratio at
        // all, which is how a "high contrast" setting ends up rendering a frame
        // byte-identical to the one beside it.

        /// <summary>Behind everything. Fullscreen backdrop and letterboxing.</summary>
        public static Color SpaceVoid => Pick(Hex(0x05070A), Hex(0x000000));

        /// <summary>Primary panel base.</summary>
        public static Color SurfaceObsidian => Pick(Hex(0x0A0E14), Hex(0x020406));

        /// <summary>Content well inside a panel.</summary>
        public static Color Surface1 => Pick(Hex(0x0E141C), Hex(0x05080C));

        /// <summary>Row and card.</summary>
        public static Color Surface2 => Pick(Hex(0x141B25), Hex(0x090D13));

        /// <summary>Hovered row, nested card.</summary>
        public static Color Surface3 => Pick(Hex(0x1A2330), Hex(0x0E141C));

        /// <summary>Modal, dropdown, tooltip -- the only elevated surface.</summary>
        public static Color SurfaceElevated => Pick(Hex(0x202B3A), Hex(0x151D28));

        /// <summary>Scrim behind a modal. Dark enough that gameplay never competes.</summary>
        public static Color OverlayDim => Pick(
            new Color(0.012f, 0.020f, 0.031f, 0.80f),
            new Color(0f, 0f, 0f, 0.92f));

        // ---- BORDER ----------------------------------------------------------
        // Under High Contrast an edge stops being a hint and becomes the thing that
        // says where one panel ends. The hue is kept and the opacity roughly
        // trebled, because a border a player cannot see is a border that forces
        // them to read the layout off the text alone.

        /// <summary>Default panel hairline.</summary>
        public static Color BorderSubtle => Pick(
            new Color(0.486f, 0.643f, 0.769f, 0.13f),
            new Color(0.741f, 0.851f, 0.937f, 0.45f));

        /// <summary>Interactive control edge.</summary>
        public static Color BorderDefault => Pick(
            new Color(0.486f, 0.643f, 0.769f, 0.22f),
            new Color(0.741f, 0.851f, 0.937f, 0.66f));

        /// <summary>Hovered control edge.</summary>
        public static Color BorderStrong => Pick(
            new Color(0.486f, 0.643f, 0.769f, 0.34f),
            new Color(0.827f, 0.914f, 1f, 0.92f));

        /// <summary>Keyboard focus ring. Never used for anything else.</summary>
        public static Color BorderFocused => Pick(Hex(0x7FC4E8), Hex(0xE8F6FF));

        /// <summary>Divider between sections of one panel.</summary>
        public static Color Divider => Pick(
            new Color(0.486f, 0.643f, 0.769f, 0.10f),
            new Color(0.741f, 0.851f, 0.937f, 0.35f));

        /// <summary>
        /// The empty part of a bar. A recess, never a band.
        /// <para>
        /// The track sprite is a white wash and was being drawn untinted, so it
        /// composited brighter than the panel it sits on and a bar at 0% read as a
        /// full one — the fill is the only channel carrying the value, and an empty
        /// track that looks filled cancels it out. Tinted toward the darkest surface
        /// the track reads as the well it is, and length is legible again.
        /// </para>
        /// </summary>
        public static Color BarTrackTint => SurfaceObsidian;

        // ---- ACTION ----------------------------------------------------------
        // Deliberately a near-neighbour of LightingUiPalette.Nominal so an action
        // never reads as a status and a status never reads as a button.
        //
        // Selected, hovered and pressed keep their ordering under High Contrast --
        // each one is stronger than the last, and each one is further from Default
        // than it was -- because those three are how a player driving from the
        // keyboard knows where they are.

        public static Color ActionPrimary => Pick(Hex(0x8FD4F5), Hex(0xBFE9FF));
        public static Color ActionPrimaryFill => Pick(
            new Color(0.561f, 0.831f, 0.961f, 0.14f),
            new Color(0.749f, 0.914f, 1f, 0.30f));
        public static Color ActionSecondary => Pick(Hex(0x7C93A6), Hex(0xAFC4D6));
        public static Color SelectedFill => Pick(
            new Color(0.561f, 0.831f, 0.961f, 0.10f),
            new Color(0.749f, 0.914f, 1f, 0.26f));
        public static Color SelectedEdge => Pick(Hex(0x8FD4F5), Hex(0xE8F6FF));
        public static Color HoverFill => Pick(
            new Color(0.561f, 0.831f, 0.961f, 0.06f),
            new Color(0.749f, 0.914f, 1f, 0.16f));
        public static Color PressedFill => Pick(
            new Color(0.561f, 0.831f, 0.961f, 0.20f),
            new Color(0.749f, 0.914f, 1f, 0.44f));

        /// <summary>
        /// Disabled ink. High Contrast lifts it from 2.2:1 to 5.8:1 on a row, so
        /// the reason a control is unavailable can be read; it is still 3.2:1 away
        /// from <see cref="InkPrimary"/>, so it never stops reading as disabled.
        /// Making a disabled control legible and making it look enabled are two
        /// different changes and only the first one is wanted.
        /// </summary>
        public static Color DisabledInk => Pick(Hex(0x4C5866), Hex(0x8391A1));
        public static Color DisabledFill => Pick(
            new Color(0.486f, 0.643f, 0.769f, 0.05f),
            new Color(0.514f, 0.569f, 0.631f, 0.14f));

        // ---- TEXT ------------------------------------------------------------

        // Contrast ratios below are WCAG 2.1 and were re-measured against the
        // surfaces as authored; the previous figures were quoted "on SpaceVoid"
        // but were actually the Surface3 numbers, which understated all three.
        // The row background is the worst case an ink ever lands on, so both
        // ends are stated rather than only the flattering one.

        /// <summary>Values the player must read. 16.3:1 on <see cref="SpaceVoid"/>, 12.8:1 on <see cref="Surface3"/>; 21.0:1 / 18.5:1 in High Contrast.</summary>
        public static Color InkPrimary => Pick(Hex(0xDCE9F2), Hex(0xFFFFFF));

        /// <summary>Supporting copy. 8.3:1 on <see cref="SpaceVoid"/>, 6.5:1 on <see cref="Surface3"/>; 17.0:1 / 15.0:1 in High Contrast.</summary>
        public static Color InkSecondary => Pick(Hex(0x94A9BB), Hex(0xDCE9F4));

        /// <summary>
        /// Labels and decoration only -- never a value. 4.2:1 on
        /// <see cref="SpaceVoid"/> but only 3.3:1 on <see cref="Surface3"/>, so
        /// it clears the 3:1 bar for a non-text mark and misses the 4.5:1 bar
        /// for body copy. Anything the player has to read is
        /// <see cref="InkSecondary"/> or better. High Contrast takes it to
        /// 11.4:1 / 10.0:1, which clears the text bar as well.
        /// </summary>
        public static Color InkMuted => Pick(Hex(0x61748A), Hex(0xAEC1D2));

        // ---- RARITY ----------------------------------------------------------
        // Paired with the existing rarity hexagon sprite and a letter, so colour is
        // never the only channel. Legendary is kept 22% darker and 14% more
        // saturated than Caution so a rarity can never be misread as a warning.

        public static Color RarityCommon => Pick(Hex(0xA8B2BC), Hex(0xD2DAE2));
        public static Color RarityUncommon => Pick(Hex(0x3FBF6F), Hex(0x5FE894));
        public static Color RarityRare => Pick(Hex(0x3B82E0), Hex(0x74AEFF));
        public static Color RarityVeryRare => Pick(Hex(0xA855D8), Hex(0xCA8BF2));
        public static Color RarityLegendary => Pick(Hex(0xE8A020), Hex(0xFFC44D));

        /// <summary>
        /// Level-up and major discovery only. A celebration is not a status, which
        /// is why it is here and not in <see cref="LightingUiPalette"/>.
        /// </summary>
        public static Color Celebration => Pick(Hex(0xFFD966), Hex(0xFFE9A3));

        // ---- STATUS (delegated) ---------------------------------------------

        /// <summary>
        /// The colour for a status level. Delegates to <see cref="LightingUiPalette"/>
        /// so accessibility folding happens exactly once, in the place that already
        /// owns it. Resolved on read because those settings change mid-session.
        /// </summary>
        public static Color Status(UiStatusLevel level) => LightingUiPalette.Resolve(level);

        /// <inheritdoc cref="Status(UiStatusLevel)"/>
        public static Color Status(UiStatusLevel level, float alpha) =>
            LightingUiPalette.Resolve(level, alpha);

        /// <summary>
        /// The colour a status may be <b>written</b> in, as opposed to drawn in.
        /// <para>
        /// <see cref="Status(UiStatusLevel)"/> is a mark colour: a bar, a dot, an
        /// edge. WCAG asks 3:1 of a mark and 4.5:1 of text, and
        /// <see cref="UiStatusLevel.Inactive"/> is the one level that sits between
        /// the two -- measured 3.1:1 on the disabled row it is normally used on.
        /// That was enough for the dot beside the row and not enough for the
        /// sentence inside it, and "SEVİYE 5 GEREKLİ" is a sentence the player has
        /// to read: it is the whole answer to why a module is locked.
        /// </para>
        /// <para>
        /// So Inactive text is lifted to <see cref="InkSecondary"/> -- 6.5:1 on the
        /// same row, and the same cool grey family, so nothing about the look
        /// changes except that it can be read. Nothing is lost by dropping the
        /// hue: Inactive has no hue to lose, and the row's disabled background,
        /// its dimmed key prompt and the words themselves all still carry the
        /// state. Every other level keeps its own colour untouched, because those
        /// hues <i>are</i> information for a colour-blind player reading them
        /// alongside the icon.
        /// </para>
        /// <para>
        /// Routed through <see cref="LightingUiPalette.Apply"/> rather than
        /// returned raw, so High Contrast and the brightness trim still reach it
        /// by the one path that owns them.
        /// </para>
        /// </summary>
        public static Color StatusInk(UiStatusLevel level) =>
            level == UiStatusLevel.Inactive
                ? LightingUiPalette.Apply(InkSecondary, false)
                : Status(level);

        /// <inheritdoc cref="StatusInk(UiStatusLevel)"/>
        public static Color StatusInk(UiStatusLevel level, float alpha)
        {
            Color color = StatusInk(level);
            color.a = alpha;
            return color;
        }

        // ---- TYPOGRAPHY ------------------------------------------------------
        // Sizes are in reference pixels at the 1920x1080 base. The canvas scaler
        // and the UI-scale setting do the scaling; nothing here is re-derived.

        public const int SizeDisplay = 34;
        public const int SizeScreenTitle = 22;
        public const int SizeSectionTitle = 12;
        public const int SizeBody = 14;
        public const int SizeCompact = 12;
        public const int SizeCaption = 11;
        public const int SizeButton = 13;
        public const int SizeKeyPrompt = 11;
        public const int SizeValueLarge = 28;
        public const int SizeValue = 18;

        /// <summary>Below this, Turkish diacritics stop resolving cleanly at 1080p.</summary>
        public const int MinimumReadableSize = 11;

        // Tracking is what makes the aerospace voice work; uGUI Text cannot do it,
        // which is the whole reason player-facing text moves to TextMeshPro.
        public const float TrackingDisplay = 0.26f;
        public const float TrackingScreenTitle = 0.22f;
        public const float TrackingSectionTitle = 0.24f;
        public const float TrackingButton = 0.16f;
        public const float TrackingCaption = 0.12f;
        public const float TrackingBody = 0f;

        // ---- SPACING (8px grid) ---------------------------------------------

        public const float S1 = 4f;
        public const float S2 = 8f;
        public const float S3 = 12f;
        public const float S4 = 16f;
        public const float S6 = 24f;
        public const float S8 = 32f;
        public const float S12 = 48f;
        public const float S16 = 64f;

        public const float RowHeight = 40f;
        public const float RowHeightLarge = 56f;
        public const float IconLabelGap = 10f;

        /// <summary>Nothing interactive may be smaller than this in either axis.</summary>
        public const float MinimumHitTarget = 32f;

        // ---- SHAPE -----------------------------------------------------------

        public const float RadiusSmall = 2f;
        public const float RadiusMedium = 3f;
        public const float RadiusLarge = 4f;
        public const float Hairline = 1f;

        /// <summary>The aerospace corner cut on a primary panel. Two corners only.</summary>
        public const float Chamfer = 10f;

        public const float BarHeight = 7f;
        public const float BarHeightLarge = 10f;

        // ---- MOTION (delegated) ---------------------------------------------

        /// <inheritdoc cref="UiAnimationProfiles.DurationFor"/>
        public static float MotionDuration(UiPanelPhase phase) => UiAnimationProfiles.DurationFor(phase);

        /// <inheritdoc cref="UiAnimationProfiles.Ease"/>
        public static float Ease(float t) => UiAnimationProfiles.Ease(t);

        /// <inheritdoc cref="UiAnimationProfiles.SlideDistance"/>
        public static float SlideDistance() => UiAnimationProfiles.SlideDistance();

        /// <summary>
        /// False under Photosensitivity Safe Mode. Anything that would repeat --
        /// a pulse, a blink, a looping highlight -- must check this and hold a
        /// steady state instead. A status that has to move to be noticed is a
        /// status some players cannot use.
        /// </summary>
        public static bool AllowsRepeatingEmphasis => UiAnimationProfiles.AllowsRepeatingEmphasis;

        /// <summary>True when every transition should snap rather than tween.</summary>
        public static bool SnapTransitions => AnimationAccessibility.SnapStateChanges;

        // ---- helpers ---------------------------------------------------------

        /// <summary>0xRRGGBB to an opaque linear-safe UI colour.</summary>
        private static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);

        /// <summary>The same colour at a different opacity, without re-declaring it.</summary>
        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
