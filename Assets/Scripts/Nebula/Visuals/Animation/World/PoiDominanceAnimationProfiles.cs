using Nebula.Extraction;
using Nebula.Poi;

namespace Nebula.Visuals
{
    /// <summary>What a point of interest is visibly doing, once it may be shown at all.</summary>
    public enum PoiVisualCharacter
    {
        /// <summary>A contact with no type revealed. All four look identical here.</summary>
        NeutralContact,

        /// <summary>Type known, nothing happening.</summary>
        IdleIdentified,

        /// <summary>A real operation is running.</summary>
        Working,

        /// <summary>An operation exists but its own timer is not advancing.</summary>
        Paused,

        /// <summary>Finished. A low, permanent resting pose that never replays.</summary>
        Resolved
    }

    /// <summary>
    /// The information gate, and how each dominance state reads.
    /// <para>
    /// Everything here is derived from the state POI and dominance already own.
    /// Nothing invents a level, a timer, a choice or an outcome, and nothing may
    /// show the player something the simulation has not told them yet.
    /// </para>
    /// </summary>
    public static class PoiDominanceAnimationProfiles
    {
        // --- the information gate --------------------------------------------

        /// <summary>
        /// True while the POI's own type must stay hidden. At Signal and Located
        /// the game has not told the player what this is, so an anomaly and a
        /// wreck must move identically.
        /// </summary>
        public static bool TypeIsHidden(PoiKnowledgeLevel level)
            => level == PoiKnowledgeLevel.Unknown
               || level == PoiKnowledgeLevel.Signal
               || level == PoiKnowledgeLevel.Located;

        /// <summary>
        /// What may be shown at this level. Paused is read from the POI's own
        /// pause reason: if its timer is not moving, neither is its animation.
        /// </summary>
        public static PoiVisualCharacter Resolve(PoiKnowledgeLevel level, PoiPauseReason pause)
        {
            if (TypeIsHidden(level)) return PoiVisualCharacter.NeutralContact;

            switch (level)
            {
                case PoiKnowledgeLevel.Identified: return PoiVisualCharacter.IdleIdentified;
                case PoiKnowledgeLevel.Active:
                    return pause == PoiPauseReason.None
                        ? PoiVisualCharacter.Working
                        : PoiVisualCharacter.Paused;
                case PoiKnowledgeLevel.Resolved: return PoiVisualCharacter.Resolved;
                default: return PoiVisualCharacter.NeutralContact;
            }
        }

        /// <summary>True when this character is allowed to move at all.</summary>
        public static bool IsAnimated(PoiVisualCharacter character)
            => character == PoiVisualCharacter.NeutralContact
               || character == PoiVisualCharacter.IdleIdentified
               || character == PoiVisualCharacter.Working;

        /// <summary>
        /// How fast a POI's own visual turns, in degrees per second. Every
        /// hidden-type contact turns at exactly the same rate, so the motion
        /// itself cannot be read as a type.
        /// </summary>
        public static float SpinDegreesPerSecond(PoiType type, PoiVisualCharacter character)
        {
            if (character == PoiVisualCharacter.NeutralContact) return 2f; // identical for all four
            if (!IsAnimated(character)) return 0f;

            float working = character == PoiVisualCharacter.Working ? 1.8f : 1f;

            switch (type)
            {
                case PoiType.Anomaly: return 6f * working;          // a turning field
                case PoiType.Wreck: return 1.2f * working;          // heavy, barely moving
                case PoiType.AbandonedFacility: return 3f * working; // machinery
                case PoiType.Protected: return 2f * working;         // a base, ambient only
                default: return 2f;
            }
        }

        /// <summary>
        /// A facility's character comes from the choice the player really
        /// locked. Before that, both read the same, so the animation cannot
        /// leak which branch is available or which is better.
        /// </summary>
        public static float FacilityCadence(FacilityChoice choice)
        {
            switch (choice)
            {
                case FacilityChoice.Dismantle: return 1.4f;   // stripping it down
                case FacilityChoice.SensorRelay: return 0.7f; // a slow sweep
                default: return 1f;                            // undecided: neutral
            }
        }

        /// <summary>
        /// The scan effect only appears on a real anomaly analysis that is
        /// really running — never at Signal or Located, and never while paused.
        /// </summary>
        public static bool ShowsScanEffect(PoiType type, PoiVisualCharacter character)
            => type == PoiType.Anomaly && character == PoiVisualCharacter.Working;

        // --- dominance ---------------------------------------------------------

        /// <summary>
        /// The marker a planet's real dominance state gets. Unowned draws
        /// nothing at all — an empty world must not be marked.
        /// </summary>
        public static NebulaCommonVfx? DominanceVfx(DominanceState state, bool abandoned)
        {
            if (abandoned) return NebulaCommonVfx.WarningPulse; // never mistaken for Controlled

            switch (state)
            {
                case DominanceState.Developing: return NebulaCommonVfx.BuildHologram;
                case DominanceState.Controlled: return NebulaCommonVfx.ShieldImpact;
                case DominanceState.Contested: return NebulaCommonVfx.WarningPulse;
                case DominanceState.Lost: return NebulaCommonVfx.HitSpark;
                default: return null; // Unowned
            }
        }

        /// <summary>True when this state draws a marker at all.</summary>
        public static bool HasMarker(DominanceState state, bool abandoned)
            => DominanceVfx(state, abandoned) != null;

        /// <summary>
        /// How strongly a marker moves. Contested deliberately does not pulse:
        /// Photosensitivity Safe Mode makes a blinking warning unacceptable, so
        /// it is a steady amber presence instead.
        /// </summary>
        public static float MarkerMotion(DominanceState state)
        {
            switch (state)
            {
                case DominanceState.Developing: return 1f;
                case DominanceState.Controlled: return 0.6f;
                case DominanceState.Contested: return 0.35f; // steady, never blinking
                case DominanceState.Lost: return 0f;         // still and dim
                default: return 0f;
            }
        }

        /// <summary>
        /// Recovery is shown only when the real recovery clock is genuinely
        /// running. No new state is invented for it.
        /// </summary>
        public static bool IsRecovering(DominanceState state, float recoveryTimer)
            => state == DominanceState.Lost && recoveryTimer > 0f;

        /// <summary>
        /// A system reads as dominant only when every one of its real planets
        /// is really Controlled. Derived, never stored.
        /// </summary>
        public static bool SystemIsDominant(System.Collections.Generic.IEnumerable<DominanceState> planetStates)
        {
            bool any = false;
            foreach (DominanceState state in planetStates)
            {
                any = true;
                if (state != DominanceState.Controlled) return false;
            }
            return any;
        }
    }
}
