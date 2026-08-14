using System.Collections.Generic;

namespace Nebula.Extraction
{
    /// <summary>VS-015 §5: how a Command Center's Maintenance Reserve auto-spends itself.</summary>
    public enum MaintenancePolicy { Manual, Emergency, FullField }

    /// <summary>
    /// VS-015 §5: a single Command Center's own physical resource pool — never
    /// build/upgrade/production stock, never global. Capacity comes from the
    /// Command Center's own tier (40 at T1, 120 at T2). Storage/reservation
    /// reuses <see cref="LogisticsCargo"/> (the same reservation-transaction
    /// engine everything else in this project uses) with an explicit
    /// accepted-item whitelist enforced on every physical Add.
    /// </summary>
    public class MaintenanceReserve
    {
        public const float Tier1Capacity = 40f;
        public const float Tier2Capacity = 120f;

        public string CommandWorldId { get; }
        public LogisticsCargo Cargo { get; }
        public MaintenancePolicy Policy { get; private set; } = MaintenancePolicy.Manual;

        /// <summary>Planet worldId currently under an active manual/automatic Engineer maintenance target (single active Engineer-target per planet, VS-015 §5).</summary>
        public string ActiveEngineerTargetId { get; private set; }
        public readonly List<string> QueuedTargetIds = new List<string>();

        private static readonly HashSet<string> AcceptedItems = BuildWhitelist();

        private static HashSet<string> BuildWhitelist()
        {
            var set = new HashSet<string> { ProcessingCatalog.BasicPartId };
            foreach (string m in ProcessingCatalog.MaterialIds) set.Add(m);
            return set;
        }

        public static bool IsAccepted(string itemId) => !string.IsNullOrEmpty(itemId) && AcceptedItems.Contains(itemId);

        public MaintenanceReserve(string commandWorldId, int tier)
        {
            CommandWorldId = commandWorldId;
            Cargo = new LogisticsCargo(tier >= 2 ? Tier2Capacity : Tier1Capacity);
        }

        public void ApplyTier(int tier) => Cargo.SetCapacity(tier >= 2 ? Tier2Capacity : Tier1Capacity);

        /// <summary>Physically adds to the reserve; rejects (returns 0) any non-whitelisted item without touching storage.</summary>
        public float Add(string itemId, float amount)
        {
            if (!IsAccepted(itemId) || amount <= 0f) return 0f;
            return Cargo.Add(itemId, amount);
        }

        public void SetPolicy(MaintenancePolicy policy) => Policy = policy;

        public bool TrySetActiveTarget(string targetId)
        {
            if (!string.IsNullOrEmpty(ActiveEngineerTargetId) && ActiveEngineerTargetId != targetId) return false;
            ActiveEngineerTargetId = targetId;
            return true;
        }

        public void ClearActiveTarget(string targetId)
        {
            if (ActiveEngineerTargetId == targetId) ActiveEngineerTargetId = null;
        }

        public void Enqueue(string targetId) { if (!QueuedTargetIds.Contains(targetId)) QueuedTargetIds.Add(targetId); }
        public void Dequeue(string targetId) => QueuedTargetIds.Remove(targetId);
    }
}
