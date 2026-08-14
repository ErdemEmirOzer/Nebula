namespace Nebula.Extraction
{
    /// <summary>
    /// VS-017A.2 §B3: one active Drill T1→T2 upgrade. Mirrors
    /// <see cref="StructureUpgradeJob"/>'s exact reserve→tick→complete shape —
    /// advanced only by real active-simulation seconds (never wall clock, so
    /// offline progress stays exactly zero), and completing exactly once.
    /// </summary>
    public class DrillUpgradeJob
    {
        public const string CargoOwnerPrefix = "drill-upgrade:";

        public int TargetTier { get; }
        public float TotalSeconds { get; }
        public float RemainingSeconds { get; private set; }

        public float Progress01 => TotalSeconds > 0f ? UnityEngine.Mathf.Clamp01(1f - RemainingSeconds / TotalSeconds) : 1f;

        public DrillUpgradeJob(int targetTier, float totalSeconds, float remainingSeconds)
        {
            TargetTier = targetTier; TotalSeconds = totalSeconds; RemainingSeconds = remainingSeconds;
        }

        /// <summary>Returns true on the single tick that completes the job.</summary>
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
