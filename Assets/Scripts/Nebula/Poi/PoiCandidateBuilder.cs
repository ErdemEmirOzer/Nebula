using System.Collections.Generic;
using UnityEngine;
using Nebula.Navigation;
using Nebula.World;

namespace Nebula.Poi
{
    /// <summary>Builds the "still-Unknown real systems near a position, sorted by distance" candidate list Anomaly/Sensor Relay signals slice from (VS-012 §5/§7).</summary>
    public static class PoiCandidateBuilder
    {
        public static List<(string systemId, float distance)> NearestUnknown(Vector3 fromAbsolutePosition, SystemKnowledgeService knowledge)
        {
            var candidates = new List<(string systemId, float distance)>();
            if (knowledge == null) return candidates;
            foreach (StarSystemRecord record in StarSystemRegistry.SystemsNear(fromAbsolutePosition))
            {
                if (knowledge.GetLevel(record.SystemId) != SystemKnowledgeLevel.Unknown) continue;
                candidates.Add((record.SystemId, Vector3.Distance(fromAbsolutePosition, record.AbsoluteCenter)));
            }
            candidates.Sort((a, b) => { int cmp = a.distance.CompareTo(b.distance); return cmp != 0 ? cmp : string.CompareOrdinal(a.systemId, b.systemId); });
            return candidates;
        }
    }
}
