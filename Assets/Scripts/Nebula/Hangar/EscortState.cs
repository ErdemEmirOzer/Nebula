namespace Nebula.Hangar
{
    /// <summary>VS-015 §4: player-issued Escort command.</summary>
    public enum EscortCommand { Follow, Protect, Retreat, ReturnToHangar }

    /// <summary>VS-015 §4: retreat threshold behavior. Safe retreats early, Persistent only when forced.</summary>
    public enum EscortStance { Safe, Balanced, Persistent }

    /// <summary>VS-015 §4: what the Escort is currently doing, purely derived/driven by <see cref="EscortCombatResolver"/>.</summary>
    public enum EscortSubState { Idle, Following, Engaging, Chasing, Retreating }

    /// <summary>
    /// VS-015 §4: pure per-vehicle Escort state — protected target, command,
    /// stance, and the currently-engaged threat. Physical flight/firing is
    /// driven by VehicleAgent using this as the decision input, mirroring the
    /// PoiTask/EngineerMissionState "pure state, physical driver ticks it" split.
    /// </summary>
    public class EscortState
    {
        public const float SafeRetreatFraction = 0.60f;
        public const float BalancedRetreatFraction = 0.35f;
        public const float PersistentRetreatFraction = 0.15f;
        public const float ChaseExtraDistance = 400f;

        public string VehicleId { get; }
        public string ProtectedTargetId { get; private set; }
        public Combat.TargetCategory ProtectedTargetCategory { get; private set; }
        public EscortCommand Command { get; private set; }
        public EscortStance Stance { get; private set; }
        public EscortSubState SubState { get; private set; }
        public string EngagedThreatId { get; private set; }

        /// <summary>VS-015 §8: stable id for the current protection assignment — one manual-XP grant per assignment, awarded the first time it successfully clears a threat while protecting.</summary>
        public string MissionId { get; }
        public bool PlayerManuallyStarted { get; }
        public bool XpAwarded { get; private set; }

        public EscortState(string vehicleId, string missionId, string protectedTargetId, Combat.TargetCategory protectedTargetCategory,
            bool playerManuallyStarted, EscortCommand command = EscortCommand.Protect, EscortStance stance = EscortStance.Balanced)
        {
            VehicleId = vehicleId; MissionId = missionId; ProtectedTargetId = protectedTargetId; ProtectedTargetCategory = protectedTargetCategory;
            PlayerManuallyStarted = playerManuallyStarted; Command = command; Stance = stance; SubState = EscortSubState.Following;
        }

        public void MarkXpAwarded() => XpAwarded = true;

        public void SetProtectedTarget(string targetId, Combat.TargetCategory category)
        {
            ProtectedTargetId = targetId; ProtectedTargetCategory = category;
        }

        public void SetCommand(EscortCommand command)
        {
            Command = command;
            if (command == EscortCommand.Follow || command == EscortCommand.Protect) SubState = EscortSubState.Following;
            else if (command == EscortCommand.Retreat) SubState = EscortSubState.Retreating;
        }

        public void SetStance(EscortStance stance) => Stance = stance;

        public void EngageThreat(string threatId) { EngagedThreatId = threatId; if (SubState != EscortSubState.Retreating) SubState = EscortSubState.Engaging; }
        public void ClearThreat() { EngagedThreatId = null; if (SubState == EscortSubState.Engaging || SubState == EscortSubState.Chasing) SubState = EscortSubState.Following; }
        public void BeginChase() { if (SubState == EscortSubState.Engaging) SubState = EscortSubState.Chasing; }
        public void ForceRetreat() => SubState = EscortSubState.Retreating;
        public void EndRetreat() => SubState = Command == EscortCommand.Retreat ? EscortSubState.Retreating : EscortSubState.Following;

        /// <summary>VS-015 §4: retreat threshold as a fraction of total (Shield+Armor+Hull) durability, by stance.</summary>
        public float RetreatThresholdFraction => Stance switch
        {
            EscortStance.Safe => SafeRetreatFraction,
            EscortStance.Persistent => PersistentRetreatFraction,
            _ => BalancedRetreatFraction
        };

        public static EscortState Restore(string vehicleId, string missionId, string protectedTargetId, Combat.TargetCategory protectedTargetCategory,
            bool playerManuallyStarted, bool xpAwarded, EscortCommand command, EscortStance stance, EscortSubState subState, string engagedThreatId)
        {
            var state = new EscortState(vehicleId, missionId, protectedTargetId, protectedTargetCategory, playerManuallyStarted, command, stance)
            {
                SubState = subState, EngagedThreatId = engagedThreatId
            };
            if (xpAwarded) state.MarkXpAwarded();
            return state;
        }
    }
}
