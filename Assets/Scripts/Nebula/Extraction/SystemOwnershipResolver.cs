namespace Nebula.Extraction
{
    /// <summary>
    /// System-level ownership tag (VS-010 §1) — informational only: never
    /// grants a bonus or XP, and is never written as a separate save
    /// authority. Always re-derived from live/delta dominance state.
    /// </summary>
    public enum SystemOwnershipTag { Undiscovered, Discovered, PioneerPresence, EstablishedBase, DominantCore }

    /// <summary>
    /// Pure resolver: turns real per-planet dominance facts about one system
    /// into a single ownership tag. <paramref name="hasActiveLogisticsCenter"/>
    /// and <paramref name="hasWorkingCollectorRoute"/> are wired to real state
    /// (VS-011): <see cref="World.SystemStatusQuery.HasActiveLogisticsCenter"/>
    /// and <see cref="World.SystemStatusQuery.HasWorkingCollectorRoute"/>.
    /// </summary>
    public static class SystemOwnershipResolver
    {
        public static SystemOwnershipTag Resolve(bool visited, int totalRealPlanets, int controlledPlanets,
            bool hasActiveLogisticsCenter, bool hasWorkingCollectorRoute, bool hasActiveDefense)
        {
            if (!visited) return SystemOwnershipTag.Undiscovered;
            if (controlledPlanets <= 0) return SystemOwnershipTag.Discovered;
            if (totalRealPlanets > 0 && controlledPlanets >= totalRealPlanets) return SystemOwnershipTag.DominantCore;
            if (hasActiveLogisticsCenter && hasWorkingCollectorRoute && hasActiveDefense) return SystemOwnershipTag.EstablishedBase;
            return SystemOwnershipTag.PioneerPresence;
        }
    }
}
