using System.Collections.Generic;

namespace Nebula.Sectors
{
    /// <summary>
    /// Resolves and caches immutable <see cref="SectorManifest"/>s (VS-008
    /// §1/§4/§5): the single authority a materializer or navigation query
    /// asks "what real content lives here". Every sector is always
    /// reproducible from (seed, GenerationVersion, coordinate) alone — there
    /// is no persisted/frozen variant; save data only ever carries the
    /// mutable <see cref="SystemDelta"/> on top of whatever this repository
    /// regenerates identically on the next load.
    /// <para>
    /// <b>The cache is bounded (P2-PERF-003).</b> It used to have no capacity, no
    /// eviction and no lifetime: the only way anything ever left it was
    /// <see cref="Clear"/> at campaign reset, so the longer a player flew the more
    /// sectors it held, with no upper limit anywhere in the file. It now holds at
    /// most <see cref="Capacity"/> sectors and evicts in strict least-recently-used
    /// order — deterministic, not heuristic: the same sequence of
    /// <see cref="Resolve"/> calls always evicts the same coordinates.
    /// </para>
    /// <para>
    /// <b>Eviction is safe precisely because this is not an authority.</b> A manifest
    /// is a pure function of (seed, coordinate, isStart), so an evicted sector comes
    /// back byte-identical on the next resolve; everything mutable — what the player
    /// mined, built or destroyed — lives in <see cref="SystemDelta"/> and the world
    /// delta repository, which this class neither holds nor evicts. Nothing here is
    /// ever written to a save. The one sector the generator alone cannot reproduce is
    /// the start sector, which runs through <see cref="FrontierGuaranteePass"/>; it is
    /// pinned and never evicted, so the guarantee pass runs once per campaign exactly
    /// as it did before.
    /// </para>
    /// </summary>
    public class SectorManifestRepository
    {
        /// <summary>
        /// The upper bound on cached sectors, and the number the audit documents.
        /// <para>
        /// The live working set is 27 — the sector the ship is in plus its 26
        /// neighbours, which is what <c>StarSystemRegistry.SystemsNear</c> walks — so
        /// 512 is roughly nineteen times the set anything actually reads at once, and
        /// still an explicit ceiling rather than "however far the player flew". Chosen
        /// large enough that ordinary back-and-forth travel never evicts a sector that
        /// is about to be asked for again, and small enough that the ceiling is reached
        /// in a long session rather than never.
        /// </para>
        /// </summary>
        public const int DefaultCapacity = 512;

        private readonly ulong seed;
        private readonly int capacity;

        /// <summary>Coordinate → its node in <see cref="order"/>; the node carries the manifest.</summary>
        private readonly Dictionary<SectorCoordinate, LinkedListNode<CacheEntry>> cache =
            new Dictionary<SectorCoordinate, LinkedListNode<CacheEntry>>();

        /// <summary>Most-recently-resolved at the front, least-recently at the back.</summary>
        private readonly LinkedList<CacheEntry> order = new LinkedList<CacheEntry>();

        private readonly Dictionary<string, SystemManifest> systemIndex = new Dictionary<string, SystemManifest>();
        private SectorCoordinate startSector;
        private bool hasStartSector;

        private sealed class CacheEntry
        {
            public SectorCoordinate Sector;
            public SectorManifest Manifest;
        }

        public SectorManifestRepository(ulong seed) : this(seed, DefaultCapacity) { }

        /// <param name="capacity">
        /// Sectors kept resolved at once. Clamped to at least 28 — the 27-sector
        /// neighbourhood plus the pinned start sector — because a capacity below the
        /// live working set would evict a sector inside the very loop that is reading
        /// it and turn a cache into a generator called once per neighbour.
        /// </param>
        public SectorManifestRepository(ulong seed, int capacity)
        {
            this.seed = seed;
            this.capacity = capacity < MinimumCapacity ? MinimumCapacity : capacity;
        }

        /// <summary>27 neighbourhood sectors plus the pinned start sector.</summary>
        public const int MinimumCapacity = 28;

        /// <summary>The universe seed these manifests come from — needed by callers that enumerate star positions without resolving whole sectors.</summary>
        public ulong Seed => seed;

        /// <summary>The documented upper bound on <see cref="CachedSectorCount"/>.</summary>
        public int Capacity => capacity;

        /// <summary>Test-facing: how many sectors this repository has evicted since it was built.</summary>
        public int EvictedSectorCount { get; private set; }

        public void SetStartSector(SectorCoordinate sector) { startSector = sector; hasStartSector = true; }

        public SectorManifest Resolve(SectorCoordinate sector)
        {
            if (cache.TryGetValue(sector, out LinkedListNode<CacheEntry> cached))
            {
                // A read is a use: without this the "least recently used" order would be
                // insertion order, and the sector the ship has been sitting in all session
                // would be the first one thrown away.
                Touch(cached);
                return cached.Value.Manifest;
            }

            bool isStart = hasStartSector && sector.Equals(startSector);
            SectorManifest generated = SectorManifestGenerator.Generate(seed, sector, isStart);
            LinkedListNode<CacheEntry> node =
                order.AddFirst(new CacheEntry { Sector = sector, Manifest = generated });
            cache[sector] = node;
            IndexSystems(generated);
            EvictWhileOverCapacity();
            return generated;
        }

        private void Touch(LinkedListNode<CacheEntry> node)
        {
            if (ReferenceEquals(node, order.First)) return;
            order.Remove(node);
            order.AddFirst(node);
        }

        /// <summary>
        /// Drops least-recently-used sectors until the cache is inside its capacity,
        /// skipping the pinned start sector. Deterministic: the order is exact LRU, so
        /// the same access sequence always evicts the same coordinates in the same order.
        /// </summary>
        private void EvictWhileOverCapacity()
        {
            while (cache.Count > capacity)
            {
                LinkedListNode<CacheEntry> victim = order.Last;
                while (victim != null && hasStartSector && victim.Value.Sector.Equals(startSector))
                    victim = victim.Previous;

                // Only reachable if every entry is the pinned sector, which cannot happen
                // while capacity >= MinimumCapacity; bailing rather than looping is still
                // the right answer if that ever stops being true.
                if (victim == null) return;

                cache.Remove(victim.Value.Sector);
                order.Remove(victim);
                DeindexSystems(victim.Value.Manifest);
                EvictedSectorCount++;
            }
        }

        private void IndexSystems(SectorManifest manifest)
        {
            foreach (SystemManifest system in manifest.Systems) systemIndex[system.SystemId] = system;
        }

        /// <summary>
        /// Removes an evicted sector's systems from the id index, so the index is bounded
        /// by the same capacity the sector cache is. Only entries that still point at this
        /// manifest's own systems are removed — a system id re-indexed by a later resolve
        /// belongs to that resolve, not to the sector being dropped.
        /// </summary>
        private void DeindexSystems(SectorManifest manifest)
        {
            foreach (SystemManifest system in manifest.Systems)
                if (systemIndex.TryGetValue(system.SystemId, out SystemManifest indexed)
                    && ReferenceEquals(indexed, system))
                    systemIndex.Remove(system.SystemId);
        }

        public SystemManifest ResolveSystem(string systemId)
        {
            // "No system" is a real answer, not a crash. A null id reaches here
            // whenever the ship is inside no system's navigation radius
            // (NavigationDirector.CurrentSystemId is null across every interstellar
            // crossing) and a Dictionary lookup on a null key throws
            // ArgumentNullException — once per caller, per frame.
            if (string.IsNullOrEmpty(systemId)) return null;

            if (systemIndex.TryGetValue(systemId, out SystemManifest cached)) return cached;
            if (!WorldIdCodec.TryParseSystemId(systemId, out SectorCoordinate sector, out _)) return null;
            return Resolve(sector).FindSystem(systemId);
        }

        public IReadOnlyCollection<SectorCoordinate> CachedSectorCoordinates => cache.Keys;
        public int CachedSectorCount => cache.Count;

        /// <summary>
        /// The already-resolved manifest for a sector, or null. Lets a caller that only
        /// wants to LOOK at a sector (the strategic map drawing distant stars) prefer real
        /// resolved content where it exists without forcing generation — and therefore
        /// without paying for planets, resources and node layouts it will never read.
        /// <para>
        /// Deliberately does <i>not</i> count as a use: looking at a sector on the map must
        /// not push the sector the ship is flying through out of the cache.
        /// </para>
        /// </summary>
        public SectorManifest TryGetResolved(SectorCoordinate sector) =>
            cache.TryGetValue(sector, out LinkedListNode<CacheEntry> cached) ? cached.Value.Manifest : null;

        /// <summary>
        /// True when this sector's content is not reproducible from the generator alone —
        /// the start sector runs through <see cref="FrontierGuaranteePass"/>, which may move
        /// systems and rewrite star classes. A cheap positional enumeration would disagree
        /// with the real manifest there, so callers must resolve it properly instead.
        /// </summary>
        public bool RequiresFullResolve(SectorCoordinate sector) =>
            hasStartSector && sector.Equals(startSector);

        public void Clear()
        {
            cache.Clear();
            order.Clear();
            systemIndex.Clear();
            hasStartSector = false;
        }
    }
}
