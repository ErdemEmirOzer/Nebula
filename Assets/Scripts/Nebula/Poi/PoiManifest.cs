using UnityEngine;

namespace Nebula.Poi
{
    /// <summary>
    /// Immutable per-POI manifest data (VS-012 §1) — type, risk, system-local
    /// position, size (Wreck only), and the two seeds every reward/visual
    /// roll derives from. Reproducible from (seed, systemId, index) plus
    /// whatever of <see cref="PoiVisitLedger"/> was already recorded the
    /// first time it was resolved — see <see cref="PoiManifestGenerator"/>
    /// for exactly how that stays stable forever after.
    /// </summary>
    public sealed class PoiManifest
    {
        public string PoiId { get; }
        public string SystemId { get; }
        public int Index { get; }
        public PoiType Type { get; }
        public PoiRisk Risk { get; }
        /// <summary>System-local offset from the star (floating-origin safe — never an absolute coordinate).</summary>
        public Vector3 SystemLocalPosition { get; }
        public WreckSize Size { get; }
        public ulong RewardSeed { get; }
        public ulong VisualSeed { get; }

        public PoiManifest(string poiId, string systemId, int index, PoiType type, PoiRisk risk,
            Vector3 systemLocalPosition, WreckSize size, ulong rewardSeed, ulong visualSeed)
        {
            PoiId = poiId; SystemId = systemId; Index = index; Type = type; Risk = risk;
            SystemLocalPosition = systemLocalPosition; Size = size; RewardSeed = rewardSeed; VisualSeed = visualSeed;
        }
    }
}
