using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.UI.Accessibility;
using Nebula.UI.Design;

namespace Nebula.UI.Tutorial
{
    /// <summary>
    /// The six states a tutorial control or checklist row can be in, plus the
    /// resting one.
    /// <para>
    /// Named as an enum rather than left implicit in a pile of sprite assignments
    /// because the accessibility rule is a claim about the <i>set</i> -- that all
    /// six are told apart -- and a claim about a set can only be tested if the set
    /// exists as a thing.
    /// </para>
    /// </summary>
    public enum TutorialUiState
    {
        /// <summary>At rest. No mark, no wash.</summary>
        Default,

        /// <summary>Pointer is over it.</summary>
        Hover,

        /// <summary>Pointer is down on it.</summary>
        Pressed,

        /// <summary>Keyboard focus is on it.</summary>
        Focused,

        /// <summary>It cannot be used, and says why elsewhere.</summary>
        Disabled,

        /// <summary>An objective that has been met.</summary>
        Complete,

        /// <summary>An objective that has not been met yet.</summary>
        Incomplete,
    }

    /// <summary>
    /// How one state is drawn, in all four channels at once.
    /// <para>
    /// Four, not one. Colour is the fastest channel and the one a colour-blind
    /// player does not have; the shape survives colour blindness but not a 12px
    /// mark; the word survives both and is the slowest to read; the surface says
    /// where the row's edge is. Every state in this table carries all four, which
    /// is the whole reason the table exists instead of a switch on a colour.
    /// </para>
    /// </summary>
    public readonly struct TutorialStateVisual
    {
        /// <summary>Ink for the row's own text.</summary>
        public readonly Color Ink;

        /// <summary>The wash behind it. Distinct per state, including in alpha.</summary>
        public readonly Color Fill;

        /// <summary>The mark's geometry. Never <see cref="UiCueShape.None"/> for the six real states.</summary>
        public readonly UiCueShape Shape;

        /// <summary>The state's word. The channel that survives everything.</summary>
        public readonly string Marker;

        /// <summary>The authored surface underneath.</summary>
        public readonly Sprite Surface;

        internal TutorialStateVisual(Color ink, Color fill, UiCueShape shape, string marker, Sprite surface)
        {
            Ink = ink;
            Fill = fill;
            Shape = shape;
            Marker = marker;
            Surface = surface;
        }
    }

    /// <summary>
    /// The one table the tutorial's surfaces draw their states from.
    /// <para>
    /// Resolved on every read rather than cached, because High Contrast is a
    /// switch the player flips mid-run and the tokens underneath it answer
    /// differently the moment it moves.
    /// </para>
    /// </summary>
    public static class TutorialUiStates
    {
        /// <summary>
        /// The six states the accessibility rule is about. Deliberately excludes
        /// <see cref="TutorialUiState.Default"/>, which is the absence of a state
        /// and therefore has nothing to be distinct from.
        /// </summary>
        public static readonly IReadOnlyList<TutorialUiState> DistinctStates = new[]
        {
            TutorialUiState.Hover,
            TutorialUiState.Pressed,
            TutorialUiState.Focused,
            TutorialUiState.Disabled,
            TutorialUiState.Complete,
            TutorialUiState.Incomplete,
        };

        /// <summary>How a state is drawn, in every channel.</summary>
        public static TutorialStateVisual Visual(TutorialUiState state)
        {
            switch (state)
            {
                case TutorialUiState.Hover:
                    return new TutorialStateVisual(
                        NebulaUiDesignTokens.ActionPrimary,
                        NebulaUiDesignTokens.HoverFill,
                        UiCueShape.Chevron, "ÜZERİNDE",
                        NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Selected));

                case TutorialUiState.Pressed:
                    return new TutorialStateVisual(
                        NebulaUiDesignTokens.InkPrimary,
                        NebulaUiDesignTokens.PressedFill,
                        UiCueShape.Diamond, "BASILI",
                        NebulaUiAssetCatalog.SelectionRing);

                case TutorialUiState.Focused:
                    return new TutorialStateVisual(
                        NebulaUiDesignTokens.BorderFocused,
                        NebulaUiDesignTokens.SelectedFill,
                        UiCueShape.Triangle, "ODAKTA",
                        NebulaUiAssetCatalog.FocusRing);

                case TutorialUiState.Disabled:
                    return new TutorialStateVisual(
                        NebulaUiDesignTokens.DisabledInk,
                        NebulaUiDesignTokens.DisabledFill,
                        UiCueShape.Barred, "KULLANILAMAZ",
                        NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Disabled));

                case TutorialUiState.Complete:
                    return new TutorialStateVisual(
                        NebulaUiDesignTokens.Status(Visuals.UiStatusLevel.Nominal),
                        NebulaUiDesignTokens.Status(Visuals.UiStatusLevel.Nominal, 0.12f),
                        UiCueShape.Check, "TAMAM",
                        NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));

                case TutorialUiState.Incomplete:
                    return new TutorialStateVisual(
                        NebulaUiDesignTokens.InkSecondary,
                        new Color(0f, 0f, 0f, 0f),
                        UiCueShape.Dash, "BEKLİYOR",
                        NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));

                default:
                    return new TutorialStateVisual(
                        NebulaUiDesignTokens.InkPrimary,
                        new Color(0f, 0f, 0f, 0f),
                        UiCueShape.None, string.Empty,
                        NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            }
        }

        /// <summary>
        /// A stable summary of everything one state looks like, so a test can say
        /// two states really are told apart without a screen -- and can say
        /// <i>which</i> channel collapsed when they are not.
        /// <para>
        /// The geometry goes in as <see cref="UiCueShapes.Signature"/> rather than
        /// as the enum name, because two enum values pointing at the same stroke
        /// table would be two names for one shape and the point is the picture.
        /// </para>
        /// </summary>
        public static string Signature(TutorialUiState state)
        {
            TutorialStateVisual v = Visual(state);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "ink:{0:0.000},{1:0.000},{2:0.000},{3:0.000}|fill:{4:0.000},{5:0.000},{6:0.000},{7:0.000}|word:{8}|shape:{9}",
                v.Ink.r, v.Ink.g, v.Ink.b, v.Ink.a,
                v.Fill.r, v.Fill.g, v.Fill.b, v.Fill.a,
                v.Marker, UiCueShapes.Signature(v.Shape));
        }

        /// <summary>
        /// The cue for a checklist objective. Routed through
        /// <see cref="UiStatusCue"/> so the shape and word channels obey the
        /// player's cue switch exactly as they do everywhere else in the game --
        /// and so turning cues off strips the redundancy and never the colour.
        /// </summary>
        public static UiStatusCue ObjectiveCue(bool done) =>
            UiStatusCue.Of(done ? UiCueKind.Nominal : UiCueKind.Inactive);

        /// <summary>
        /// The cue for a feedback line. <see cref="Nebula.Tutorial.TutorialFeedbackKind.Reject"/>
        /// is Caution rather than Critical: the game refused something, which is
        /// worth noticing and is not a failure state.
        /// </summary>
        public static UiStatusCue FeedbackCue(Nebula.Tutorial.TutorialFeedbackKind kind)
        {
            switch (kind)
            {
                case Nebula.Tutorial.TutorialFeedbackKind.Success: return UiStatusCue.Of(UiCueKind.Nominal);
                case Nebula.Tutorial.TutorialFeedbackKind.Reject: return UiStatusCue.Of(UiCueKind.Caution);
                case Nebula.Tutorial.TutorialFeedbackKind.Progress: return UiStatusCue.Of(UiCueKind.Working);
                default: return UiStatusCue.None;
            }
        }

        /// <summary>
        /// Wires a control so hover, pressed, focused and disabled are all visibly
        /// different, rather than four names for the same picture.
        /// <para>
        /// Sprite swap, for the reason the asset catalog gives: a tint is a
        /// brightness ramp over one image and throws the authored edge away. Every
        /// list row in this project shipped with <c>Transition.None</c> once, so
        /// this is applied explicitly rather than assumed.
        /// </para>
        /// </summary>
        public static void ApplyRowInteraction(Button button)
        {
            if (button == null) return;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = NebulaUiAssetCatalog.RowInteraction();
        }
    }
}
