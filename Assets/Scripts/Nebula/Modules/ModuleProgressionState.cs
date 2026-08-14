using System.Collections.Generic;

namespace Nebula.Modules
{
    /// <summary>Per-module current tier (0 = not owned/T0). Pure, testable.</summary>
    public class ModuleProgressionState
    {
        private readonly Dictionary<string, int> tiers = new Dictionary<string, int>();

        public int GetTier(string moduleId) => tiers.TryGetValue(moduleId, out int t) ? t : 0;
        public void SetTier(string moduleId, int tier) => tiers[moduleId] = tier;

        /// <summary>New-campaign grant: the three free Industry modules start at T1, everything else at T0.</summary>
        public void GrantFreeStartingModules()
        {
            tiers.Clear();
            foreach (string id in ModuleCatalog.FreeStartingModuleIds) tiers[id] = 1;
        }

        public void Reset() => tiers.Clear();

        /// <summary>All non-zero tiers (for save).</summary>
        public IEnumerable<KeyValuePair<string, int>> OwnedTiers => tiers;
    }
}
