namespace Nebula.Modules
{
    public enum UpgradeRejectReason
    {
        None,
        UnknownModule,
        AlreadyMaxTier,
        TierSkip,
        NotImplemented,
        LevelTooLow,
        MilestoneNotMet,
        QueueBusy,
        InsufficientPoints,
        InsufficientResources,
        ScreenPaused
    }

    /// <summary>
    /// Read-only-from-outside record of the single active upgrade. Owner id
    /// for cargo reservations is stable ("module-upgrade") since only one job
    /// can ever be active at a time.
    /// </summary>
    public class ModuleUpgradeJob
    {
        public const string CargoOwnerId = "module-upgrade";

        public string ModuleId { get; }
        public int FromTier { get; }
        public int ToTier { get; }
        public int ReservedPoints { get; }
        public (string itemId, float amount)[] ReservedResources { get; }
        public float TotalSeconds { get; }

        public float RemainingSeconds { get; internal set; }
        public bool Paused { get; internal set; }
        public float SafeStreakSeconds { get; internal set; }
        public bool Cancelled { get; internal set; }
        public bool Completed { get; internal set; }

        public float Progress01 => TotalSeconds > 0f
            ? UnityEngine.Mathf.Clamp01(1f - RemainingSeconds / TotalSeconds)
            : 1f;

        public ModuleUpgradeJob(string moduleId, int fromTier, int toTier, int reservedPoints,
            (string itemId, float amount)[] reservedResources, float totalSeconds)
        {
            ModuleId = moduleId; FromTier = fromTier; ToTier = toTier;
            ReservedPoints = reservedPoints; ReservedResources = reservedResources;
            TotalSeconds = totalSeconds; RemainingSeconds = totalSeconds;
        }
    }

    /// <summary>
    /// Safety gate for active-upgrade ticking, kept open for a future combat
    /// system. Nothing in today's project tracks "under attack" or "disabled"
    /// state, so the default provider is always safe — this interface exists
    /// purely so a real provider can be swapped in later without touching the
    /// upgrade service.
    /// </summary>
    public interface IUpgradeSafetyProvider
    {
        bool IsSafe();
    }

    public class AlwaysSafeProvider : IUpgradeSafetyProvider
    {
        public bool IsSafe() => true;
    }
}
