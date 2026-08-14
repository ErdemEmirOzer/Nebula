namespace Nebula.Combat
{
    /// <summary>VS-015 §6: a purely-derived planetary readiness tag — never its own saved stat/bonus/XP source.</summary>
    public enum ReadinessLabel { Undefended, BasicDefense, Ready, Fortified }

    /// <summary>
    /// VS-015 §6: pure function from already-existing planetary state to a
    /// readiness label. Every input is read straight off real Command/
    /// defense/structure/reserve data elsewhere — this class stores nothing
    /// and never grants a stat bonus or XP.
    /// </summary>
    public static class ReadinessLabelResolver
    {
        public const float ReadyReserveFraction = 0.25f;
        public const float FortifiedReserveFraction = 0.50f;

        public static ReadinessLabel Resolve(bool commandActive, bool hasAnyActiveWeapon, bool hasUnlockedDefenseLayersActive,
            bool criticalStructuresStanding, float reserveFillFraction, bool hasLocalEngineer, bool hasAssignedDefenseGroup,
            bool allUnlockedNodesComplete)
        {
            if (!commandActive || !hasAnyActiveWeapon) return ReadinessLabel.Undefended;

            if (allUnlockedNodesComplete && reserveFillFraction >= FortifiedReserveFraction && hasLocalEngineer && hasAssignedDefenseGroup)
                return ReadinessLabel.Fortified;

            if (hasUnlockedDefenseLayersActive && criticalStructuresStanding && reserveFillFraction >= ReadyReserveFraction)
                return ReadinessLabel.Ready;

            return ReadinessLabel.BasicDefense;
        }
    }
}
