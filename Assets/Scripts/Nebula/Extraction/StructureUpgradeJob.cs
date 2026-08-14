namespace Nebula.Extraction
{
    /// <summary>VS-014 §2: a single active structure tier upgrade (Command T1→T2, Laser T1→T2, Plasma T1→T2) — mirrors WeaponProductionJob/ModuleUpgradeJob's exact reserve→tick→complete shape.</summary>
    public class StructureUpgradeJob
    {
        public const string CargoOwnerPrefix = "structure-upgrade:";

        public SurfaceStructureType Type { get; }
        public int TargetTier { get; }
        public float TotalSeconds { get; }
        public float RemainingSeconds { get; private set; }

        public float Progress01 => TotalSeconds > 0f ? UnityEngine.Mathf.Clamp01(1f - RemainingSeconds / TotalSeconds) : 1f;

        public StructureUpgradeJob(SurfaceStructureType type, int targetTier, float totalSeconds, float remainingSeconds)
        {
            Type = type; TargetTier = targetTier; TotalSeconds = totalSeconds; RemainingSeconds = remainingSeconds;
        }

        /// <summary>Returns true the tick that completes the job.</summary>
        public bool Tick(float dt)
        {
            if (RemainingSeconds <= 0f) return false;
            RemainingSeconds -= dt;
            if (RemainingSeconds > 0f) return false;
            RemainingSeconds = 0f;
            return true;
        }
    }
}
