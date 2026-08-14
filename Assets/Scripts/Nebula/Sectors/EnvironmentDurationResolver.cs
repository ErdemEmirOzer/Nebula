namespace Nebula.Sectors
{
    /// <summary>
    /// Shared duration scaler for whatever a caller's base seconds represent
    /// (VS-009 §3): post-capsule structure construction (wired, DrillRuntime
    /// and StructureRuntime), and future structure-upgrade / field-repair
    /// timers (no consumer yet — deliberately not fabricated here).
    /// </summary>
    public static class EnvironmentDurationResolver
    {
        public static float ScaleDuration(float baseSeconds, EnvironmentClass environment) =>
            baseSeconds * EnvironmentClassCatalog.DurationMultiplier(environment);
    }
}
