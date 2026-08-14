namespace Nebula.Ship
{
    /// <summary>
    /// Small static input-context gate (VS-013.1 §3), the same shape as the
    /// existing <see cref="InputGate"/>/<see cref="ExclusiveModal"/> pattern
    /// — never a service locator, never a component searching another
    /// directly. Set by <see cref="Combat.CombatEncounterDirector"/>/
    /// <see cref="ShipCombat"/> (CombatActive, mirroring InCombat) and
    /// <see cref="TrainingArea"/> (TrainingActive, while the player is
    /// physically engaged with the training site). Read by every
    /// world-interaction consumer (evacuation, System Logistics Center,
    /// surface construction, quick asset package, POI, processing) so a
    /// Combat- or Training-owned key press can never also trigger a
    /// world-interaction action, and vice versa. "Management/UI/Pause"
    /// blocking flight/combat/world input is already fully covered by the
    /// existing <see cref="InputGate.Suppressed"/> flag (every modal screen
    /// sets it) — this class adds only the one new rule that flag didn't
    /// already express.
    /// </summary>
    public static class CombatInputContext
    {
        public static bool CombatActive { get; set; }
        public static bool TrainingActive { get; set; }

        /// <summary>True while evacuation/reclaim, System Logistics Center transfer/build/dismantle, surface construction, quick asset package, POI, and processing shortcuts must all be blocked.</summary>
        public static bool WorldInteractionBlocked => CombatActive || TrainingActive;
    }
}
