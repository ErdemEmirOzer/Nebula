using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Diagnostics
{
    /// <summary>
    /// VS-017A §6: reusable Development Standalone perf/soak recorder -- pure
    /// C# (no editor/batchmode dependency) so <see cref="PerformanceRecorderComponent"/>
    /// (a thin MonoBehaviour wrapper, attach-and-forget) can drive it in a real
    /// Player build. Records CPU frame time into "normal" vs "heavy" scene
    /// buckets, named promotion-duration events, and memory growth relative to
    /// an explicit baseline sample. This file only ever measures -- it never
    /// changes Time.timeScale, frame pacing, or promotion behavior itself.
    /// </summary>
    public class PerformanceRecorder
    {
        public const int ReferenceWidth = 1920;
        public const int ReferenceHeight = 1080;
        public const double NormalFrameP95TargetMs = 16.7;
        public const double HeavyFrameP99TargetMs = 25.0;
        public const double PromotionTargetMs = 2000.0;
        public const double PromotionHitchTargetMs = 100.0;
        public const double MaxMemoryGrowthFraction = 0.10;
        /// <summary>A run below this for longer than this many consecutive seconds counts as the "long sustained sub-30fps" failure this slice must never silently accept.</summary>
        public const double SustainedSubThirtyFpsFrameMs = 1000.0 / 30.0;

        private readonly List<double> normalFrameMs = new List<double>();
        private readonly List<double> heavyFrameMs = new List<double>();
        private readonly List<double> promotionMs = new List<double>();

        private long baselineMemoryBytes = -1;
        private long peakMemoryBytes;
        public int CrashOrExceptionCount { get; private set; }

        private double subThirtyStreakSeconds;
        public double LongestSustainedSubThirtyFpsSeconds { get; private set; }

        public void RecordNormalFrame(double ms) => normalFrameMs.Add(ms);
        public void RecordHeavyFrame(double ms) => heavyFrameMs.Add(ms);
        public void RecordPromotion(double ms) => promotionMs.Add(ms);
        public void RecordException() => CrashOrExceptionCount++;

        /// <summary>Feed every real frame's own delta time here (regardless of which bucket it also goes to) to track how long a below-30fps stretch lasts.</summary>
        public void TickFrameForSustainedFpsCheck(double frameMs)
        {
            if (frameMs > SustainedSubThirtyFpsFrameMs)
            {
                subThirtyStreakSeconds += frameMs / 1000.0;
                if (subThirtyStreakSeconds > LongestSustainedSubThirtyFpsSeconds) LongestSustainedSubThirtyFpsSeconds = subThirtyStreakSeconds;
            }
            else subThirtyStreakSeconds = 0.0;
        }

        public void SampleMemory(long bytes)
        {
            if (baselineMemoryBytes < 0) baselineMemoryBytes = bytes;
            if (bytes > peakMemoryBytes) peakMemoryBytes = bytes;
        }

        public double MemoryGrowthFraction => baselineMemoryBytes <= 0 ? 0.0 : (double)(peakMemoryBytes - baselineMemoryBytes) / baselineMemoryBytes;

        public PercentileMath.Stats NormalFrameStats => PercentileMath.Compute(normalFrameMs);
        public PercentileMath.Stats HeavyFrameStats => PercentileMath.Compute(heavyFrameMs);
        public PercentileMath.Stats PromotionStats => PercentileMath.Compute(promotionMs);

        public string ExportReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Nebula Standalone Perf/Soak Report");
            sb.AppendLine($"Reference resolution: {ReferenceWidth}x{ReferenceHeight}");
            sb.AppendLine($"Normal frame:   {NormalFrameStats} (target p95<={NormalFrameP95TargetMs}ms)");
            sb.AppendLine($"Heavy frame:    {HeavyFrameStats} (target p99<={HeavyFrameP99TargetMs}ms)");
            sb.AppendLine($"Promotion:      {PromotionStats} (target <= {PromotionTargetMs}ms, single hitch <= {PromotionHitchTargetMs}ms)");
            sb.AppendLine($"Memory growth:  {MemoryGrowthFraction:P2} (target < {MaxMemoryGrowthFraction:P0})");
            sb.AppendLine($"Longest sustained <30fps stretch: {LongestSustainedSubThirtyFpsSeconds:0.0}s (target: never sustained)");
            sb.AppendLine($"Crashes/unhandled exceptions: {CrashOrExceptionCount} (target: 0)");
            return sb.ToString();
        }

        public void Clear()
        {
            normalFrameMs.Clear(); heavyFrameMs.Clear(); promotionMs.Clear();
            baselineMemoryBytes = -1; peakMemoryBytes = 0; subThirtyStreakSeconds = 0; LongestSustainedSubThirtyFpsSeconds = 0; CrashOrExceptionCount = 0;
        }
    }

    /// <summary>
    /// Attach to any persistent GameObject in a Development Standalone build to run a real
    /// soak: it records every frame's CPU time via Time.unscaledDeltaTime (never reads/alters
    /// Time.timeScale) into the "normal" bucket, samples GC.GetTotalMemory every 5s, and
    /// exposes public methods a scene-heavy stress harness (or a manual test) can call to mark
    /// "heavy scene" frames and promotion events. Not wired into any automated test run --
    /// this is the manual/real-hardware entrypoint the VS-017A report documents.
    ///
    /// To run a real soak:
    ///   1. Build a Development Standalone Player.
    ///   2. Add this component to a persistent (DontDestroyOnLoad) GameObject.
    ///   3. Play normally (or drive a scripted stress scenario) for the target duration (2h / 10h).
    ///   4. Call ExportReport() (e.g. bound to a debug key) and save its output alongside the run.
    /// </summary>
    public class PerformanceRecorderComponent : MonoBehaviour
    {
        public readonly PerformanceRecorder Recorder = new PerformanceRecorder();
        private float memorySampleTimer;
        private const float MemorySampleIntervalSeconds = 5f;

        private void Awake() => DontDestroyOnLoad(gameObject);

        private void Update()
        {
            double frameMs = Time.unscaledDeltaTime * 1000.0;
            Recorder.RecordNormalFrame(frameMs);
            Recorder.TickFrameForSustainedFpsCheck(frameMs);

            memorySampleTimer += Time.unscaledDeltaTime;
            if (memorySampleTimer >= MemorySampleIntervalSeconds)
            {
                memorySampleTimer = 0f;
                Recorder.SampleMemory(System.GC.GetTotalMemory(false));
            }
        }

        public void MarkHeavyFrame(double ms) => Recorder.RecordHeavyFrame(ms);
        public void MarkPromotion(double ms) => Recorder.RecordPromotion(ms);
        public string ExportReport() => Recorder.ExportReport();
    }
}
