using UnityEngine;
using Nebula.Hangar;

namespace Nebula.Visuals
{
    /// <summary>
    /// What a support vehicle is visibly doing. Derived from the real mission,
    /// route, engineer, escort and resupply states — never decided here.
    /// <para>
    /// There is deliberately no "production complete" or "launch preparation"
    /// pose: the hangar keeps no such state on a vehicle, and the ship has no
    /// bay geometry to play them against (ANI-GAP-003). Adding them would mean
    /// inventing a source.
    /// </para>
    /// </summary>
    public enum VehicleAnimationState
    {
        Docked,
        Launching,
        Travel,
        Approach,
        Working,
        Returning,
        Docking,
        Disabled,
        Recovery,
        Completed
    }

    /// <summary>
    /// How each vehicle class carries itself, and how each real mission phase
    /// reads as a pose.
    /// <para>
    /// Everything here moves the visual child only, because that is all the
    /// current art supports: every one of the seven vehicle prefabs is a single
    /// mesh with no thruster, dock, cargo, tool or sensor node. Per-part
    /// mechanics — a cargo bay opening, a tool arm deploying, a sensor dish
    /// sweeping — need geometry that does not exist, and this part does not
    /// fake them by shaking the whole hull.
    /// </para>
    /// </summary>
    public static class VehicleAnimationProfiles
    {
        /// <summary>
        /// Hard ceiling on visual bank, in degrees. Never exceeded by any class
        /// or setting, and deliberately kept within the same comfort limit the
        /// player ship obeys.
        /// </summary>
        public const float MaxBankDegrees = AnimationAccessibility.MaxVisualBankDegrees;

        /// <summary>Ceiling on visual pitch from acceleration, in degrees.</summary>
        public const float MaxPitchDegrees = 12f;

        /// <summary>How quickly a pose chases its target. A visual filter only.</summary>
        public const float PoseDamping = 5f;

        /// <summary>
        /// Maps the real <see cref="VehiclePhase"/> the mission state machine
        /// already owns onto a pose. Nothing here advances a phase.
        /// </summary>
        public static VehicleAnimationState FromMissionPhase(VehiclePhase phase)
        {
            switch (phase)
            {
                case VehiclePhase.InHangar: return VehicleAnimationState.Docked;
                case VehiclePhase.Launching: return VehicleAnimationState.Launching;
                case VehiclePhase.TravelOut: return VehicleAnimationState.Travel;
                case VehiclePhase.OnStation: return VehicleAnimationState.Working;
                case VehiclePhase.TravelReturn: return VehicleAnimationState.Returning;
                case VehiclePhase.Waiting: return VehicleAnimationState.Approach;
                case VehiclePhase.Docking: return VehicleAnimationState.Docking;
                default: return VehicleAnimationState.Docked;
            }
        }

        /// <summary>
        /// Maps the resupply mission's real phases. Mk I never runs one — that
        /// rejection is the hangar's rule and is untouched here.
        /// </summary>
        public static VehicleAnimationState FromResupplyPhase(MaintenanceResupplyPhase phase)
        {
            switch (phase)
            {
                case MaintenanceResupplyPhase.Loading: return VehicleAnimationState.Working;
                case MaintenanceResupplyPhase.ToReserve: return VehicleAnimationState.Travel;
                case MaintenanceResupplyPhase.Unloading: return VehicleAnimationState.Working;
                case MaintenanceResupplyPhase.Returning: return VehicleAnimationState.Returning;
                case MaintenanceResupplyPhase.Completed:
                case MaintenanceResupplyPhase.Cancelled: return VehicleAnimationState.Completed;
                default: return VehicleAnimationState.Docked;
            }
        }

        /// <summary>
        /// Maps the escort's real behaviour. Stance thresholds, the follow
        /// limit and target selection are the escort resolver's and are only
        /// read.
        /// </summary>
        public static VehicleAnimationState FromEscortSubState(EscortSubState subState)
        {
            switch (subState)
            {
                case EscortSubState.Idle: return VehicleAnimationState.Approach;
                case EscortSubState.Following: return VehicleAnimationState.Travel;
                case EscortSubState.Engaging: return VehicleAnimationState.Working;
                case EscortSubState.Chasing: return VehicleAnimationState.Travel;
                case EscortSubState.Retreating: return VehicleAnimationState.Returning;
                default: return VehicleAnimationState.Approach;
            }
        }

        /// <summary>True while the vehicle should read as parked rather than flying.</summary>
        public static bool IsParked(VehicleAnimationState state)
            => state == VehicleAnimationState.Docked
               || state == VehicleAnimationState.Completed;

        /// <summary>True while it should read as under way, and therefore as banking into its turns.</summary>
        public static bool IsFlying(VehicleAnimationState state)
            => state == VehicleAnimationState.Launching
               || state == VehicleAnimationState.Travel
               || state == VehicleAnimationState.Approach
               || state == VehicleAnimationState.Returning
               || state == VehicleAnimationState.Docking;

        /// <summary>
        /// How hard a class leans into a turn, and how much it settles under
        /// acceleration. A Scout is light and agile; a laden Collector is not.
        /// </summary>
        public static (float bankScale, float pitchScale) FlightCharacter(VehicleClass vehicleClass)
        {
            switch (vehicleClass)
            {
                case VehicleClass.Scout: return (1f, 1f);          // light and quick
                case VehicleClass.Collector: return (0.45f, 0.6f); // heavy, laden, unhurried
                case VehicleClass.Engineer: return (0.6f, 0.7f);   // steady work platform
                case VehicleClass.Escort: return (0.9f, 0.85f);    // sharp, but not a Scout
                default: return (0.7f, 0.7f);
            }
        }

        /// <summary>
        /// Amplitude damping for a state. Approach and docking are deliberately
        /// calmer than open travel — a vehicle lining up on a hangar should look
        /// careful, not sporty.
        /// </summary>
        public static float StateAmplitude(VehicleAnimationState state)
        {
            switch (state)
            {
                case VehicleAnimationState.Approach: return 0.4f;
                case VehicleAnimationState.Docking: return 0.2f;
                case VehicleAnimationState.Launching: return 0.6f;
                // Safe closed pose: a dead vehicle does not lean into anything.
                case VehicleAnimationState.Disabled:
                case VehicleAnimationState.Recovery: return 0f;
                default: return IsFlying(state) ? 1f : 0f;
            }
        }
    }
}
