using Nebula.Extraction;

namespace Nebula.Modules
{
    /// <summary>
    /// Pure atomic validation: every gate is checked against a single
    /// consistent snapshot and the whole call either fully passes (None) or
    /// fully fails with one reason — no partial state is ever touched here.
    /// Order matters for the guaranteed test cases: functional cap, then
    /// tier-skip/max, then level, then milestone, then queue, then points,
    /// then resources.
    /// </summary>
    public static class ModuleUpgradeValidator
    {
        public static UpgradeRejectReason Validate(ModuleDef def, int currentTier, int targetTier, int characterLevel,
            bool milestoneSatisfied, bool queueBusy, int availablePoints, LogisticsCargo cargo)
        {
            if (def == null) return UpgradeRejectReason.UnknownModule;
            if (currentTier >= ModuleDef.MaxTier) return UpgradeRejectReason.AlreadyMaxTier;
            if (targetTier != currentTier + 1) return UpgradeRejectReason.TierSkip;
            if (targetTier > def.MaxTierThisSlice) return UpgradeRejectReason.NotImplemented;

            int requiredLevel = def.RequiredLevelForTier(targetTier);
            if (characterLevel < requiredLevel) return UpgradeRejectReason.LevelTooLow;
            if (!milestoneSatisfied) return UpgradeRejectReason.MilestoneNotMet;
            if (queueBusy) return UpgradeRejectReason.QueueBusy;

            ModuleTierCost cost = def.TierCosts[targetTier - 1];
            if (availablePoints < cost.DevelopmentPoints) return UpgradeRejectReason.InsufficientPoints;

            if (cargo != null)
            {
                foreach (ModuleResourceCost r in cost.Resources)
                    if (cargo.GetAvailable(r.ItemId) < r.Amount - 0.0001f) return UpgradeRejectReason.InsufficientResources;
            }

            return UpgradeRejectReason.None;
        }
    }
}
