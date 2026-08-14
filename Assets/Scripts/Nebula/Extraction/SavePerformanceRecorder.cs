using System;
using System.Collections.Generic;

namespace Nebula.Extraction
{
    /// <summary>
    /// VS-017A §5: Stopwatch-based, gameplay-independent save/load timing recorder --
    /// wraps whatever the caller already does (SaveService.Save, a payload snapshot
    /// build, SaveService.Load) and records real elapsed milliseconds into one of
    /// three buckets. Never itself calls SaveService, never changes atomic-save/
    /// backup/migration behavior -- purely an external stopwatch around the existing
    /// real calls, so a real harness can report true p50/p95/max instead of a
    /// synthetic number.
    /// </summary>
    public class SavePerformanceRecorder
    {
        public const double MainThreadSnapshotTargetMs = 50.0;
        public const double BackgroundWriteTargetMs = 2000.0;
        public const double LoadTargetMs = 5000.0;

        private readonly List<double> mainThreadSnapshotMs = new List<double>();
        private readonly List<double> backgroundWriteMs = new List<double>();
        private readonly List<double> loadMs = new List<double>();

        public IReadOnlyList<double> MainThreadSnapshotSamples => mainThreadSnapshotMs;
        public IReadOnlyList<double> BackgroundWriteSamples => backgroundWriteMs;
        public IReadOnlyList<double> LoadSamples => loadMs;

        public double RecordMainThreadSnapshot(Action action)
        {
            double ms = Measure(action);
            mainThreadSnapshotMs.Add(ms);
            return ms;
        }

        public double RecordBackgroundWrite(Action action)
        {
            double ms = Measure(action);
            backgroundWriteMs.Add(ms);
            return ms;
        }

        public double RecordLoad(Action action)
        {
            double ms = Measure(action);
            loadMs.Add(ms);
            return ms;
        }

        private static double Measure(Action action)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        public readonly struct Stats
        {
            public readonly double P50, P95, Max;
            public readonly int Count;
            public Stats(double p50, double p95, double max, int count) { P50 = p50; P95 = p95; Max = max; Count = count; }
            public override string ToString() => $"p50={P50:0.0}ms p95={P95:0.0}ms max={Max:0.0}ms (n={Count})";
        }

        public Stats MainThreadStats => Compute(mainThreadSnapshotMs);
        public Stats BackgroundWriteStats => Compute(backgroundWriteMs);
        public Stats LoadStats => Compute(loadMs);

        /// <summary>Nearest-rank percentile over a sorted copy -- deterministic, no interpolation ambiguity.</summary>
        public static double Percentile(IReadOnlyList<double> samples, double p)
        {
            if (samples == null || samples.Count == 0) return 0.0;
            var sorted = new List<double>(samples);
            sorted.Sort();
            int rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
            int index = Math.Max(0, Math.Min(sorted.Count - 1, rank - 1));
            return sorted[index];
        }

        private static Stats Compute(List<double> samples)
        {
            if (samples.Count == 0) return new Stats(0, 0, 0, 0);
            var sorted = new List<double>(samples);
            sorted.Sort();
            return new Stats(Percentile(sorted, 50), Percentile(sorted, 95), sorted[sorted.Count - 1], sorted.Count);
        }

        public void Clear()
        {
            mainThreadSnapshotMs.Clear();
            backgroundWriteMs.Clear();
            loadMs.Clear();
        }
    }
}
