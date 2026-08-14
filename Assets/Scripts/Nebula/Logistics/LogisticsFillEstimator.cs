using System.Collections.Generic;
using Nebula.Extraction;

namespace Nebula.Logistics
{
    /// <summary>VS-017A.2 §C: one deterministic Logistics Center fill measurement — all real numbers, no tuning knobs of its own.</summary>
    public readonly struct LogisticsFillEstimate
    {
        /// <summary>Combined output of every contributing drill, per minute, at its own real tier/rarity.</summary>
        public readonly float TotalProductionPerMinute;
        /// <summary>The center's own real tier capacity.</summary>
        public readonly float CenterCapacity;
        /// <summary>The center's own real inbound transfer ceiling, per minute.</summary>
        public readonly float TransferCeilingPerMinute;
        /// <summary>Whichever of production/transfer actually binds — the real throughput.</summary>
        public readonly float EffectivePerMinute;
        public readonly float FillMinutes;
        /// <summary>True when inbound transfer, not drill output, is the limiting factor.</summary>
        public readonly bool TransferLimited;

        public LogisticsFillEstimate(float totalProductionPerMinute, float centerCapacity, float transferCeilingPerMinute)
        {
            TotalProductionPerMinute = totalProductionPerMinute;
            CenterCapacity = centerCapacity;
            TransferCeilingPerMinute = transferCeilingPerMinute;
            EffectivePerMinute = UnityEngine.Mathf.Min(totalProductionPerMinute, transferCeilingPerMinute);
            TransferLimited = transferCeilingPerMinute < totalProductionPerMinute;
            FillMinutes = EffectivePerMinute > 0.0001f ? centerCapacity / EffectivePerMinute : float.PositiveInfinity;
        }

        public override string ToString() =>
            $"production={TotalProductionPerMinute:0.00}/min transferCeiling={TransferCeilingPerMinute:0.00}/min " +
            $"effective={EffectivePerMinute:0.00}/min capacity={CenterCapacity:0} fill={FillMinutes:0.00}min" +
            (TransferLimited ? " (TRANSFER-LIMITED)" : " (PRODUCTION-LIMITED)");
    }

    /// <summary>
    /// VS-017A.2 §C: a pure, deterministic estimator for "how long does an empty
    /// System Logistics Center take to fill from N drills". Every input is read
    /// from the real shipped catalogs (<see cref="ExtractionCatalog"/> for drill
    /// output at a given rarity/tier, <see cref="LogisticsCenterCatalog"/> for the
    /// center's own capacity and inbound transfer ceiling) — this class holds no
    /// balance numbers of its own and can never silently rebalance anything.
    ///
    /// This is a diagnostics measurement, not a telemetry funnel event.
    /// </summary>
    public static class LogisticsFillEstimator
    {
        /// <summary>Diagnostics metric id — deliberately NOT one of the 14 telemetry funnel events.</summary>
        public const string MetricId = "logistics_t2_fill_minutes";

        /// <summary>Fill time for a center of <paramref name="tier"/> fed by the given drills, each described by its own (rarity, tier).</summary>
        public static LogisticsFillEstimate Estimate(LogisticsCenterTier tier, IReadOnlyList<(Rarity rarity, int drillTier)> drills)
        {
            LogisticsCenterTierDef def = LogisticsCenterCatalog.Get(tier);
            float production = 0f;
            if (drills != null)
                foreach (var (rarity, drillTier) in drills)
                    production += ExtractionCatalog.ProductionPerMinute(rarity, drillTier);

            return new LogisticsFillEstimate(production, def.Capacity, def.TransferVolumePerSecond * 60f);
        }

        /// <summary>The §C reference scenario: <paramref name="drillCount"/> identical drills at one rarity/tier feeding an empty center.</summary>
        public static LogisticsFillEstimate EstimateUniform(LogisticsCenterTier tier, int drillCount, Rarity rarity, int drillTier)
        {
            var drills = new List<(Rarity, int)>();
            for (int i = 0; i < drillCount; i++) drills.Add((rarity, drillTier));
            return Estimate(tier, drills);
        }
    }
}
