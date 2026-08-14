using System.Collections.Generic;

namespace Nebula.Extraction
{
    /// <summary>
    /// VS-015 §5: pure priority ordering for a Command Center's automatic
    /// Maintenance Reserve policies. Manual has no automatic ordering at all
    /// (only the player's explicit mission exists) — this class only covers
    /// Emergency and Full Field.
    /// </summary>
    public static class MaintenancePolicyResolver
    {
        /// <summary>Emergency: the Command Center itself first (if it actually needs repair), else the first active defense structure.</summary>
        public static string ResolveEmergencyTarget(string commandTargetId, bool commandNeedsRepair, IReadOnlyList<string> activeDefenseTargetIdsInPriorityOrder)
        {
            if (commandNeedsRepair && !string.IsNullOrEmpty(commandTargetId)) return commandTargetId;
            if (activeDefenseTargetIdsInPriorityOrder != null && activeDefenseTargetIdsInPriorityOrder.Count > 0) return activeDefenseTargetIdsInPriorityOrder[0];
            return null;
        }

        /// <summary>Full Field: Command -&gt; active defense -&gt; disabled vehicle -&gt; drill -&gt; other structures, in that fixed order. Every list may be empty; only targets that actually need work should be passed in by the caller.</summary>
        public static List<string> ResolveFullFieldOrder(string commandTargetId, IReadOnlyList<string> activeDefenseTargetIds,
            IReadOnlyList<string> disabledVehicleTargetIds, IReadOnlyList<string> drillTargetIds, IReadOnlyList<string> otherStructureTargetIds)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(commandTargetId)) list.Add(commandTargetId);
            if (activeDefenseTargetIds != null) list.AddRange(activeDefenseTargetIds);
            if (disabledVehicleTargetIds != null) list.AddRange(disabledVehicleTargetIds);
            if (drillTargetIds != null) list.AddRange(drillTargetIds);
            if (otherStructureTargetIds != null) list.AddRange(otherStructureTargetIds);
            return list;
        }
    }
}
