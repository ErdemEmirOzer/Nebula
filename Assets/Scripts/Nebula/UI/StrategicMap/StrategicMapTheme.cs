using UnityEngine;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Visuals;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// The strategic map's visual language, carried over from the
    /// <c>MapDesign/nebula-live-universe-map.html</c> prototype: deep space
    /// background, glass/holographic panels, and the cyan/violet/gold accent
    /// family. Only the LOOK is taken from the prototype — none of its seed,
    /// fake galaxies or demo snapshot data comes anywhere near runtime.
    ///
    /// Colour is never the only carrier of meaning here: every state that has a
    /// colour also has its own real icon (see <c>NebulaProducedVisualResolver</c>)
    /// and, where relevant, its own shape/label.
    /// </summary>
    public static class StrategicMapTheme
    {
        // --- palette (1:1 with the prototype's CSS custom properties) --------------
        public static readonly Color Space = Hex(0x030409);
        public static readonly Color Panel = Hex(0x090D14);
        public static readonly Color PanelText = Hex(0xF1F5FF);
        public static readonly Color SpaceText = Hex(0xEDF3FF);
        public static readonly Color Cyan = Hex(0x79DCE9);
        public static readonly Color Blue = Hex(0x86A9FF);
        public static readonly Color Violet = Hex(0x9C82E8);
        public static readonly Color Gold = Hex(0xFFD999);
        public static readonly Color Green = Hex(0x63D8B5);
        public static readonly Color Red = Hex(0xFF809E);

        /// <summary>
        /// A star's own colour, from its real <see cref="Sectors.StarClass"/>.
        ///
        /// The single source of truth for both the marker in the galaxy view and the star
        /// drawn at the centre of a system, so the same star can never be one colour at
        /// distance and another close up. Colour is the one thing about a star that is
        /// legible with the naked eye from anywhere, which is why it is never gated: a map
        /// where every star is the same yellow tells the player nothing about where to go.
        /// </summary>
        public static Color StarColor(Sectors.StarClass starClass) => starClass switch
        {
            Sectors.StarClass.RedDwarf => Hex(0xFF9166),
            Sectors.StarClass.White => Hex(0xEAF1FF),
            Sectors.StarClass.BlueGiant => Hex(0xA8C6FF),
            _ => Hex(0xFFD08A) // Yellow
        };

        /// <summary>Glass panel fill — the prototype's panel colour at holographic opacity.</summary>
        public static Color PanelFill => new Color(Panel.r, Panel.g, Panel.b, 0.86f);
        public static Color PanelStroke => new Color(Cyan.r, Cyan.g, Cyan.b, 0.28f);
        public static Color Backdrop => new Color(Space.r, Space.g, Space.b, 0.94f);
        public static Color MutedText => new Color(PanelText.r, PanelText.g, PanelText.b, 0.62f);

        // --- typography -----------------------------------------------------------
        // 2560x1440 first, and every size still legible at 1920x1080.
        public const int TitleSize = 30;
        public const int HeadingSize = 21;
        public const int BodySize = 16;
        public const int CaptionSize = 13;

        // --- motion ---------------------------------------------------------------
        /// <summary>Transitions stay inside the 180-450 ms band the design calls for.</summary>
        public const float FastTransitionSeconds = 0.18f;
        public const float NormalTransitionSeconds = 0.28f;
        public const float SlowTransitionSeconds = 0.45f;

        /// <summary>
        /// Unity's accessibility equivalent of the prototype's <c>prefers-reduced-motion</c>.
        /// While true, parallax, pulsing and camera overshoot are suppressed — motion is
        /// reduced, never information.
        /// </summary>
        public static bool ReducedMotion
        {
            get => AnimationAccessibility.ReducedMotion || AnimationAccessibility.MotionOff;
            set => AnimationAccessibility.ReducedMotion = value;
        }

        /// <summary>
        /// The player's global Motion Off setting. State changes still complete, but the map
        /// must apply their final pose immediately and keep every decorative layer static.
        /// </summary>
        public static bool MotionOff => AnimationAccessibility.MotionOff;

        /// <summary>Spring/ease used for camera focus moves; collapses to a plain ease when reduced motion is on.</summary>
        public static float EaseFocus(float t)
        {
            t = Mathf.Clamp01(t);
            if (ReducedMotion) return 1f - Mathf.Pow(1f - t, 3f);                 // smooth, no overshoot
            const float c = 1.70158f * 1.05f;                                     // gentle spring settle
            float u = t - 1f;
            return 1f + (c + 1f) * u * u * u + c * u * u;
        }

        /// <summary>Pulse amplitude for "needs attention" markers — zero while reduced motion is on.</summary>
        public static float Pulse(float time, float amplitude = 0.18f) =>
            ReducedMotion ? 0f : Mathf.Sin(time * 3.1f) * amplitude;

        /// <summary>
        /// A slow 0..1 breath at the given rate, for anything that should look alive rather
        /// than blink: an open slot's ring, a selection halo, a live link. Settles at its
        /// midpoint with reduced motion on, so nothing ever sits at an extreme.
        /// </summary>
        public static float Breathe(float time, float hertz = 0.5f) =>
            ReducedMotion ? 0.5f : 0.5f + 0.5f * Mathf.Sin(time * hertz * Mathf.PI * 2f);

        /// <summary>The everyday ease for panels and fades: fast to start, gentle to land, no overshoot.</summary>
        public static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        /// <summary>
        /// The card's own entrance: a small overshoot so a panel arrives rather than appears.
        /// Collapses to <see cref="EaseOut"/> with reduced motion on.
        /// </summary>
        public static float EaseArrive(float t)
        {
            t = Mathf.Clamp01(t);
            if (ReducedMotion) return EaseOut(t);
            const float c = 1.70158f * 0.62f; // gentler than the camera's settle: a panel is close to the eye
            float u = t - 1f;
            return 1f + (c + 1f) * u * u * u + c * u * u;
        }

        /// <summary>Same colour, different alpha — the fade every panel and mark is animated through.</summary>
        public static Color WithAlpha(Color color, float alpha) =>
            new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));

        // --- semantic colours (always paired with a real icon, never colour alone) --
        public static Color Dominance(DominanceState state) => state switch
        {
            DominanceState.Controlled => Green,
            DominanceState.Developing => Cyan,
            DominanceState.Contested => Gold,
            DominanceState.Lost => Red,
            _ => MutedText
        };

        public static Color Link(SystemLinkState link) => link switch
        {
            SystemLinkState.Live => Cyan,
            SystemLinkState.LastKnown => Gold,
            _ => Red
        };

        public static Color Knowledge(SystemKnowledgeLevel level) => level switch
        {
            SystemKnowledgeLevel.Visited => PanelText,
            SystemKnowledgeLevel.Located => Blue,
            SystemKnowledgeLevel.Signal => Violet,
            _ => MutedText
        };

        public static Color Ownership(SystemOwnershipTag tag) => tag switch
        {
            SystemOwnershipTag.DominantCore => Gold,
            SystemOwnershipTag.EstablishedBase => Green,
            SystemOwnershipTag.PioneerPresence => Cyan,
            _ => MutedText
        };

        private static Color Hex(uint rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);
    }
}
