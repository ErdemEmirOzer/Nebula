using System.Collections.Generic;
using Nebula.Sectors;

namespace Nebula.Poi
{
    /// <summary>
    /// Session cache for <see cref="PoiManifestGenerator.Generate"/> results
    /// — purely a performance layer (VS-012's generator is provably stable
    /// for an already-recorded ledger entry, see its own doc comment), never
    /// a second authority. Cleared alongside every other campaign-scoped
    /// registry on a fresh game/load.
    /// </summary>
    public static class PoiManifestRepository
    {
        private static readonly Dictionary<string, List<PoiManifest>> cache = new Dictionary<string, List<PoiManifest>>();
        public static PoiVisitLedger Ledger { get; private set; } = new PoiVisitLedger();

        public static void Reset()
        {
            cache.Clear();
            Ledger = new PoiVisitLedger();
        }

        /// <summary>Resolves (and caches) a system's POI manifest. Safe to call repeatedly and from any system's promotion path.</summary>
        public static List<PoiManifest> Resolve(ulong seed, SystemManifest system)
        {
            if (system == null) return new List<PoiManifest>();
            if (cache.TryGetValue(system.SystemId, out List<PoiManifest> cached)) return cached;

            List<PoiManifest> generated = PoiManifestGenerator.Generate(seed, system, Ledger);
            cache[system.SystemId] = generated;
            return generated;
        }

        /// <summary>Drops a cached entry so it re-resolves next time (used only when the ledger just grew for THIS system's own first visit, so the cached pre-visit/no-guarantee result doesn't linger).</summary>
        public static void Invalidate(string systemId)
        {
            if (systemId != null) cache.Remove(systemId);
        }

        public static PoiManifest Find(string poiId)
        {
            if (string.IsNullOrEmpty(poiId)) return null;
            string systemId = WorldIdCodec.SystemIdFromPoiWorldId(poiId);
            if (systemId == null || !cache.TryGetValue(systemId, out List<PoiManifest> list)) return null;
            foreach (PoiManifest p in list) if (p.PoiId == poiId) return p;
            return null;
        }
    }
}
