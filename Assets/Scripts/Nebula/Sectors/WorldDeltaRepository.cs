using System.Collections.Generic;

namespace Nebula.Sectors
{
    /// <summary>
    /// Every non-empty <see cref="SystemDelta"/> currently known, keyed by
    /// canonical system id (VS-008 §5). A system with nothing mutable
    /// (unvisited, or visited-but-untouched) simply has no entry here at
    /// all — never persisted, matching "yalnız analiz edilen, ziyaret
    /// edilen veya mutable delta taşıyan manifestleri kalıcılaştır."
    /// </summary>
    public class WorldDeltaRepository
    {
        private readonly Dictionary<string, SystemDelta> deltas = new Dictionary<string, SystemDelta>();

        public SystemDelta GetOrCreate(string systemId)
        {
            if (!deltas.TryGetValue(systemId, out SystemDelta delta))
            {
                delta = new SystemDelta { systemId = systemId };
                deltas[systemId] = delta;
            }
            return delta;
        }

        public SystemDelta TryGet(string systemId) => deltas.TryGetValue(systemId, out SystemDelta d) ? d : null;

        public void Set(SystemDelta delta) => deltas[delta.systemId] = delta;

        /// <summary>Drops an empty delta so it stops taking up (even in-memory) space once a system has nothing left worth tracking.</summary>
        public void PruneIfEmpty(string systemId)
        {
            if (deltas.TryGetValue(systemId, out SystemDelta d) && d.IsEmpty) deltas.Remove(systemId);
        }

        public IEnumerable<SystemDelta> AllNonEmpty
        {
            get { foreach (SystemDelta d in deltas.Values) if (!d.IsEmpty) yield return d; }
        }

        public int PersistedCount
        {
            get { int n = 0; foreach (SystemDelta d in deltas.Values) if (!d.IsEmpty) n++; return n; }
        }

        public void Clear() => deltas.Clear();
    }
}
