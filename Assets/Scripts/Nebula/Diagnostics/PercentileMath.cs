using System.Collections.Generic;

namespace Nebula.Diagnostics
{
    /// <summary>Shared nearest-rank percentile helper -- deterministic, no interpolation ambiguity, used by every perf/timing recorder in the project (save timing, standalone frame/soak timing) so there is exactly one percentile definition.</summary>
    public static class PercentileMath
    {
        public readonly struct Stats
        {
            public readonly double P50, P95, P99, Max;
            public readonly int Count;
            public Stats(double p50, double p95, double p99, double max, int count) { P50 = p50; P95 = p95; P99 = p99; Max = max; Count = count; }
            public override string ToString() => $"p50={P50:0.00} p95={P95:0.00} p99={P99:0.00} max={Max:0.00} (n={Count})";
        }

        public static double Percentile(IReadOnlyList<double> samples, double p)
        {
            if (samples == null || samples.Count == 0) return 0.0;
            var sorted = new List<double>(samples);
            sorted.Sort();
            int rank = (int)System.Math.Ceiling(p / 100.0 * sorted.Count);
            int index = System.Math.Max(0, System.Math.Min(sorted.Count - 1, rank - 1));
            return sorted[index];
        }

        public static Stats Compute(IReadOnlyList<double> samples)
        {
            if (samples == null || samples.Count == 0) return new Stats(0, 0, 0, 0, 0);
            var sorted = new List<double>(samples);
            sorted.Sort();
            return new Stats(Percentile(sorted, 50), Percentile(sorted, 95), Percentile(sorted, 99), sorted[sorted.Count - 1], sorted.Count);
        }
    }
}
