using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>A claim on one pooled item proxy. Generation-stamped, so a stale lease can never hide a live proxy.</summary>
    public readonly struct ItemProxyLease
    {
        internal readonly string ContentId;
        internal readonly int Slot;
        internal readonly int Generation;

        internal ItemProxyLease(string contentId, int slot, int generation)
        {
            ContentId = contentId;
            Slot = slot;
            Generation = generation;
        }

        public bool IsValid => Generation != 0;

        public static ItemProxyLease None => default;
    }

    /// <summary>
    /// Pooled visual stand-ins for real items — a lump of ore approaching the
    /// refinery, a finished part leaving it.
    /// <para>
    /// Proxies are resolved from the item's own stable content id through the
    /// visual catalog, so a recipe that consumes <c>resource.metal</c> shows
    /// the metal the game already has art for. An id with no visual gets no
    /// proxy at all: showing the wrong lump would be worse than showing none.
    /// </para>
    /// <para>
    /// A proxy is scenery. It carries no collider, no Rigidbody and no
    /// quantity: it is not an item, cannot be picked up, and its existence
    /// changes no cargo total.
    /// </para>
    /// </summary>
    public sealed class ItemProxyPool
    {
        private sealed class Bucket
        {
            public Transform[] Proxies;
            public int[] Generations;
            public bool[] Leased;
        }

        private readonly Dictionary<string, Bucket> buckets = new Dictionary<string, Bucket>(System.StringComparer.Ordinal);
        private readonly Transform root;
        private readonly int perItemCapacity;
        private int nextGeneration = 1;
        private int leasedCount;

        public ItemProxyPool(Transform poolRoot, int capacityPerItem)
        {
            root = poolRoot;
            perItemCapacity = Mathf.Max(1, capacityPerItem);
        }

        /// <summary>How many distinct items have ever been asked for. Buckets are built once, on first ask.</summary>
        public int DistinctItemCount => buckets.Count;

        public int LeasedCount => leasedCount;
        public int CapacityPerItem => perItemCapacity;
        public Transform Root => root;

        /// <summary>Total proxies in existence. Bounded by items seen × per-item capacity, never by batches run.</summary>
        public int TotalProxies
        {
            get
            {
                int total = 0;
                foreach (KeyValuePair<string, Bucket> entry in buckets) total += entry.Value.Proxies.Length;
                return total;
            }
        }

        /// <summary>
        /// Claims a proxy for an item id. Returns false when the id has no
        /// authored visual, or when this item's slice of the pool is already
        /// out — both of which cost scenery and nothing else.
        /// </summary>
        public bool TryLease(string contentId, out ItemProxyLease lease, out Transform proxy)
        {
            lease = ItemProxyLease.None;
            proxy = null;
            if (string.IsNullOrEmpty(contentId)) return false;

            if (!buckets.TryGetValue(contentId, out Bucket bucket))
            {
                bucket = BuildBucket(contentId);
                if (bucket == null) return false; // no visual for this id — documented no-op
                buckets[contentId] = bucket;
            }

            for (int i = 0; i < bucket.Leased.Length; i++)
            {
                if (bucket.Leased[i]) continue;

                bucket.Leased[i] = true;
                bucket.Generations[i] = nextGeneration++;
                if (nextGeneration == 0) nextGeneration = 1;
                leasedCount++;

                if (bucket.Proxies[i] != null) bucket.Proxies[i].gameObject.SetActive(true);

                lease = new ItemProxyLease(contentId, i, bucket.Generations[i]);
                proxy = bucket.Proxies[i];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Builds this item's proxies once, from its authored visual. Returns
        /// null when the catalog has no entry — the caller then shows nothing
        /// rather than something wrong.
        /// </summary>
        private Bucket BuildBucket(string contentId)
        {
            if (!NebulaVisualCatalog.TryGetItem(contentId, out NebulaVisualDefinition definition)) return null;

            GameObject prefab = NebulaVisualResolver.Load(definition.ResourcePath);
            if (prefab == null) return null;

            var bucket = new Bucket
            {
                Proxies = new Transform[perItemCapacity],
                Generations = new int[perItemCapacity],
                Leased = new bool[perItemCapacity]
            };

            for (int i = 0; i < perItemCapacity; i++)
            {
                GameObject instance = Object.Instantiate(prefab, root);
                instance.name = $"{contentId} Proxy {i}";
                StripPhysics(instance);
                instance.SetActive(false);
                bucket.Proxies[i] = instance.transform;
            }

            return bucket;
        }

        /// <summary>A proxy is scenery. Anything that would let it join the world is removed here, once.</summary>
        private static void StripPhysics(GameObject proxy)
        {
            foreach (Collider collider in proxy.GetComponentsInChildren<Collider>(true))
                if (collider != null) Object.DestroyImmediate(collider);
            foreach (Rigidbody body in proxy.GetComponentsInChildren<Rigidbody>(true))
                if (body != null) Object.DestroyImmediate(body);
        }

        public bool IsHolder(ItemProxyLease lease)
            => lease.IsValid
               && !string.IsNullOrEmpty(lease.ContentId)
               && buckets.TryGetValue(lease.ContentId, out Bucket bucket)
               && lease.Slot >= 0 && lease.Slot < bucket.Leased.Length
               && bucket.Leased[lease.Slot]
               && bucket.Generations[lease.Slot] == lease.Generation;

        public bool TryGetProxy(ItemProxyLease lease, out Transform proxy)
        {
            if (!IsHolder(lease)) { proxy = null; return false; }
            proxy = buckets[lease.ContentId].Proxies[lease.Slot];
            return true;
        }

        /// <summary>Hands a proxy back. Idempotent; a stale lease is ignored rather than hiding the proxy that replaced it.</summary>
        public void Release(ItemProxyLease lease)
        {
            if (!IsHolder(lease)) return;

            Bucket bucket = buckets[lease.ContentId];
            bucket.Leased[lease.Slot] = false;
            // Generation deliberately not reset: this lease must stay
            // non-matching for as long as the pool lives.
            if (bucket.Proxies[lease.Slot] != null) bucket.Proxies[lease.Slot].gameObject.SetActive(false);
            leasedCount--;
        }

        /// <summary>Returns every proxy — a cancel, a module rebuild, or session teardown.</summary>
        public void ReleaseAll()
        {
            foreach (KeyValuePair<string, Bucket> entry in buckets)
            {
                Bucket bucket = entry.Value;
                for (int i = 0; i < bucket.Leased.Length; i++)
                {
                    if (!bucket.Leased[i]) continue;
                    bucket.Leased[i] = false;
                    if (bucket.Proxies[i] != null) bucket.Proxies[i].gameObject.SetActive(false);
                    leasedCount--;
                }
            }
        }
    }
}
