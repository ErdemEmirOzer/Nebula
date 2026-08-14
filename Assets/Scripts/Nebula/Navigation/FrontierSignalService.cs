using System.Collections.Generic;

namespace Nebula.Navigation
{
    /// <summary>
    /// Origin(originating Controlled planet) -&gt; target(real system) provenance
    /// for the "nearest three Unknown" reveal (VS-010 §3). Replaces
    /// <see cref="SystemKnowledgeService"/>'s old flat <c>revealGuard</c> as the
    /// SINGLE exactly-once authority for "has this origin already opened" —
    /// <see cref="SystemKnowledgeService"/> keeps owning knowledge LEVELS
    /// (Unknown/Signal/Located/Visited) unchanged; this class only owns WHY a
    /// system became Signal and lets it be topped back up to three targets as
    /// old ones graduate to Located/Visited. Pure C#, no UnityEngine
    /// dependency — the caller supplies pre-sorted-by-distance candidate
    /// lists and performs the actual <see cref="SystemKnowledgeService.Raise"/>
    /// calls through this class so both stay perfectly in sync.
    /// </summary>
    public class FrontierSignalService
    {
        private readonly HashSet<string> openedOrigins = new HashSet<string>();
        private readonly Dictionary<string, List<string>> originTargets = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, HashSet<string>> targetOrigins = new Dictionary<string, HashSet<string>>();
        /// <summary>VS-012: origins that claimed their target(s) as a single one-time grant (Anomaly/Sensor Relay POI signals) — never topped back up when a claimed target later graduates, unlike a Controlled-planet origin's ongoing nearest-3 backfill.</summary>
        private readonly HashSet<string> oneShotOrigins = new HashSet<string>();

        public bool HasOpened(string originPlanetWorldId) => openedOrigins.Contains(originPlanetWorldId);

        /// <summary>Every target system id this origin has ever claimed (for provenance display/save).</summary>
        public IReadOnlyList<string> TargetsFor(string originPlanetWorldId) =>
            originTargets.TryGetValue(originPlanetWorldId, out var list) ? list : (IReadOnlyList<string>)System.Array.Empty<string>();

        /// <summary>Every origin planet id that has ever claimed this target (for provenance display).</summary>
        public IEnumerable<string> OriginsFor(string targetSystemId) =>
            targetOrigins.TryGetValue(targetSystemId, out var set) ? set : (IEnumerable<string>)System.Array.Empty<string>();

        public IEnumerable<string> AllOpenedOrigins => openedOrigins;

        /// <summary>VS-017A.1 §1: fired exactly once per origin, only on a genuine (non-idempotent) open -- the single authoritative "a real frontier signal just opened" commit point.</summary>
        public event System.Action<string> OriginOpened;

        /// <summary>
        /// First-time open for a newly-PioneerPresence origin: claims up to the
        /// nearest 3 still-Unknown real systems (deterministic distance, then
        /// canonical id tiebreak) and raises each to Signal. Idempotent per
        /// origin (a second call for the same origin is a no-op). Never awards XP.
        /// </summary>
        public bool OpenOrigin(string originPlanetWorldId, List<(string systemId, float distance)> candidatesExcludingOrigin,
            SystemKnowledgeService knowledge)
        {
            if (string.IsNullOrEmpty(originPlanetWorldId) || openedOrigins.Contains(originPlanetWorldId)) return false;
            openedOrigins.Add(originPlanetWorldId);
            originTargets[originPlanetWorldId] = new List<string>();

            ClaimUpToThreeUnknown(originPlanetWorldId, candidatesExcludingOrigin, knowledge);
            OriginOpened?.Invoke(originPlanetWorldId);
            return true;
        }

        /// <summary>
        /// Call whenever a system's knowledge level rises to Located or
        /// Visited: every origin that had claimed it as a still-Unknown
        /// target deterministically backfills with the next nearest Unknown
        /// system (never re-claims a system another origin already opened —
        /// that system is no longer globally Unknown). Never awards XP.
        /// </summary>
        public void OnSystemLevelRaised(string systemId, SystemKnowledgeLevel newLevel,
            System.Func<string, List<(string systemId, float distance)>> candidatesForOrigin, SystemKnowledgeService knowledge)
        {
            if (newLevel != SystemKnowledgeLevel.Located && newLevel != SystemKnowledgeLevel.Visited) return;
            if (!targetOrigins.TryGetValue(systemId, out var origins)) return;

            foreach (string origin in new List<string>(origins))
            {
                if (oneShotOrigins.Contains(origin)) continue; // VS-012: a one-time POI signal grant never backfills.
                List<(string systemId, float distance)> candidates = candidatesForOrigin(origin);
                if (candidates != null) ClaimUpToThreeUnknown(origin, candidates, knowledge);
            }
        }

        /// <summary>
        /// VS-012 §5/§7: a single one-time grant of up to <paramref name="maxCount"/>
        /// still-Unknown systems from a pre-filtered/pre-sorted candidate
        /// pool (e.g. the 4th-8th nearest for an Anomaly, or the nearest
        /// available for a Sensor Relay) — dedupe/idempotent per origin like
        /// <see cref="OpenOrigin"/>, but NEVER re-claims more later even if
        /// the granted target graduates (see <see cref="OnSystemLevelRaised"/>'s
        /// oneShotOrigins skip). Returns the systemIds actually claimed this
        /// call (empty if the origin already opened, or no eligible candidate existed).
        /// </summary>
        public List<string> TryOpenOneShot(string originId, List<(string systemId, float distance)> candidatesPreFiltered,
            SystemKnowledgeService knowledge, int maxCount)
        {
            var claimed = new List<string>();
            if (string.IsNullOrEmpty(originId) || openedOrigins.Contains(originId) || maxCount <= 0) return claimed;
            openedOrigins.Add(originId);
            oneShotOrigins.Add(originId);
            originTargets[originId] = new List<string>();

            var sorted = new List<(string systemId, float distance)>(candidatesPreFiltered ?? new List<(string, float)>());
            sorted.Sort((a, b) => { int cmp = a.distance.CompareTo(b.distance); return cmp != 0 ? cmp : string.CompareOrdinal(a.systemId, b.systemId); });

            int need = maxCount;
            foreach (var c in sorted)
            {
                if (need <= 0) break;
                if (knowledge.GetLevel(c.systemId) >= SystemKnowledgeLevel.Located) continue; // never re-open a known system
                AddProvenance(originId, c.systemId);
                knowledge.Raise(c.systemId, SystemKnowledgeLevel.Signal);
                claimed.Add(c.systemId);
                need--;
            }
            return claimed;
        }

        private void ClaimUpToThreeUnknown(string origin, List<(string systemId, float distance)> candidates, SystemKnowledgeService knowledge)
        {
            List<string> myTargets = originTargets.TryGetValue(origin, out var list) ? list : (originTargets[origin] = new List<string>());

            // "Still active" = not yet progressed past Signal (Located/Visited
            // graduates out and triggers backfill) — a target another origin
            // already opened to Signal still counts as a valid, currently-held
            // slot for THIS origin too (VS-010 §3's shared-target dual-provenance rule).
            int stillActive = 0;
            foreach (string t in myTargets) if (knowledge.GetLevel(t) < SystemKnowledgeLevel.Located) stillActive++;
            int need = 3 - stillActive;
            if (need <= 0) return;

            var sorted = new List<(string systemId, float distance)>(candidates);
            sorted.Sort((a, b) =>
            {
                int cmp = a.distance.CompareTo(b.distance);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.systemId, b.systemId);
            });

            foreach (var c in sorted)
            {
                if (need <= 0) break;
                if (myTargets.Contains(c.systemId)) continue; // already one of MY claimed targets
                // Eligible if still Unknown, OR already Signal from another
                // origin (shared claim, single marker, dual provenance) — but
                // never Located/Visited (too progressed to be a fresh target).
                if (knowledge.GetLevel(c.systemId) >= SystemKnowledgeLevel.Located) continue;

                AddProvenance(origin, c.systemId);
                knowledge.Raise(c.systemId, SystemKnowledgeLevel.Signal);
                need--;
            }
        }

        private void AddProvenance(string origin, string target)
        {
            originTargets[origin].Add(target);
            if (!targetOrigins.TryGetValue(target, out var origins)) { origins = new HashSet<string>(); targetOrigins[target] = origins; }
            origins.Add(origin);
        }

        public void Clear()
        {
            openedOrigins.Clear();
            originTargets.Clear();
            targetOrigins.Clear();
            oneShotOrigins.Clear();
        }

        // --- Save/load -----------------------------------------------------------
        /// <summary>Restores provenance exactly as saved (v8+) — no re-derivation, no re-raising, no XP.</summary>
        public void RestoreProvenance(IEnumerable<(string origin, string target)> pairs)
        {
            Clear();
            if (pairs == null) return;
            foreach (var (origin, target) in pairs)
            {
                openedOrigins.Add(origin);
                if (!originTargets.TryGetValue(origin, out var list)) { list = new List<string>(); originTargets[origin] = list; }
                if (!list.Contains(target)) list.Add(target);
                if (!targetOrigins.TryGetValue(target, out var origins)) { origins = new HashSet<string>(); targetOrigins[target] = origins; }
                origins.Add(origin);
            }
        }

        /// <summary>VS-012 (schema v10): restores which origins are one-shot (never backfilled) — must run AFTER RestoreProvenance (which Clears everything).</summary>
        public void RestoreOneShotOrigins(IEnumerable<string> ids)
        {
            if (ids == null) return;
            foreach (string id in ids) oneShotOrigins.Add(id);
        }

        public IEnumerable<string> SnapshotOneShotOrigins => oneShotOrigins;

        /// <summary>Flat (origin, target) pairs for save v8.</summary>
        public IEnumerable<(string origin, string target)> SnapshotPairs()
        {
            foreach (var kv in originTargets)
                foreach (string target in kv.Value)
                    yield return (kv.Key, target);
        }

        /// <summary>
        /// v7-&gt;v8 migration only (VS-010 §7): a save with no provenance data
        /// rebuilds it from every CURRENTLY-Controlled planet, using the
        /// SAME nearest-3-Unknown selection — but never re-raises or changes
        /// any EXISTING knowledge level (Located/Visited/Signal already on
        /// the books stays exactly as-is; this only backfills the bookkeeping
        /// for systems that are STILL Unknown, exactly as a live open/backfill
        /// would). No XP either way.
        /// </summary>
        public void RebuildProvenanceFromControlledPlanets(IEnumerable<string> controlledPlanetWorldIds,
            System.Func<string, List<(string systemId, float distance)>> candidatesForOrigin, SystemKnowledgeService knowledge)
        {
            if (controlledPlanetWorldIds == null) return;
            foreach (string origin in controlledPlanetWorldIds)
            {
                if (openedOrigins.Contains(origin)) continue;
                openedOrigins.Add(origin);
                originTargets[origin] = new List<string>();
                List<(string systemId, float distance)> candidates = candidatesForOrigin(origin);
                if (candidates != null) ClaimUpToThreeUnknown(origin, candidates, knowledge);
            }
        }
    }
}
