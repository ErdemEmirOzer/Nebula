using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>A claim on one pooled capsule proxy. Generation-stamped, so a stale lease can never close a live proxy.</summary>
    public readonly struct CapsuleProxyLease
    {
        internal readonly int Slot;
        internal readonly int Generation;

        internal CapsuleProxyLease(int slot, int generation)
        {
            Slot = slot;
            Generation = generation;
        }

        /// <summary>Generation 0 is never issued, so the default value is always "no proxy".</summary>
        public bool IsValid => Generation != 0;

        public static CapsuleProxyLease None => default;
    }

    /// <summary>
    /// The fixed set of delivery-capsule proxies a session can show at once.
    /// Built up front from the authored build-capsule visual and only ever
    /// enabled and disabled — no Instantiate on a build order, no Destroy when
    /// one lands, and nothing loaded while the game is running.
    /// <para>
    /// The pool can always say no. A refused proxy costs the player a piece of
    /// scenery and nothing else: the build is already committed by the time
    /// anything here is asked, so no reservation, cost, refund or timer depends
    /// on a proxy being available.
    /// </para>
    /// </summary>
    public sealed class CapsuleProxyPool
    {
        private struct Slot
        {
            public Transform Proxy;
            public int Generation;
            public bool Leased;
        }

        private readonly Slot[] slots;
        private readonly Transform root;
        private int nextGeneration = 1;
        private int leasedCount;

        private CapsuleProxyPool(Transform poolRoot, Slot[] preallocated)
        {
            root = poolRoot;
            slots = preallocated;
        }

        public int Capacity => slots.Length;
        public int LeasedCount => leasedCount;
        public Transform Root => root;

        /// <summary>
        /// Builds the pool and every proxy in it, all disabled. A capsule proxy
        /// is scenery: it carries no collider and no Rigidbody, so it can never
        /// be hit, block anything, or be mistaken for the gameplay capsule
        /// whose countdown actually decides when the structure lands.
        /// </summary>
        public static CapsuleProxyPool Create(Transform parent, int capacity, GameObject capsuleVisual)
        {
            capacity = Mathf.Max(0, capacity);

            var poolRoot = new GameObject("Build Capsule Proxies");
            if (parent != null) poolRoot.transform.SetParent(parent, false);

            var preallocated = new Slot[capacity];
            for (int i = 0; i < capacity; i++)
            {
                GameObject proxy = capsuleVisual != null
                    ? Object.Instantiate(capsuleVisual, poolRoot.transform)
                    : new GameObject("Capsule Proxy (no visual)");

                proxy.name = "Capsule Proxy " + i;
                if (proxy.transform.parent != poolRoot.transform) proxy.transform.SetParent(poolRoot.transform, false);

                StripPhysics(proxy);
                proxy.SetActive(false);
                preallocated[i] = new Slot { Proxy = proxy.transform, Generation = 0, Leased = false };
            }

            return new CapsuleProxyPool(poolRoot.transform, preallocated);
        }

        /// <summary>A proxy is scenery. Anything that would make it participate in the world is removed here, once.</summary>
        private static void StripPhysics(GameObject proxy)
        {
            foreach (Collider collider in proxy.GetComponentsInChildren<Collider>(true))
                if (collider != null) Object.DestroyImmediate(collider);
            foreach (Rigidbody body in proxy.GetComponentsInChildren<Rigidbody>(true))
                if (body != null) Object.DestroyImmediate(body);
        }

        /// <summary>Claims a proxy, or reports false when the pool is full. Refusal never affects the build.</summary>
        public bool TryLease(out CapsuleProxyLease lease, out Transform proxy)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Leased) continue;

                slots[i].Leased = true;
                slots[i].Generation = nextGeneration++;
                if (nextGeneration == 0) nextGeneration = 1; // never issue the "no proxy" generation
                leasedCount++;

                if (slots[i].Proxy != null) slots[i].Proxy.gameObject.SetActive(true);

                lease = new CapsuleProxyLease(i, slots[i].Generation);
                proxy = slots[i].Proxy;
                return true;
            }

            lease = CapsuleProxyLease.None;
            proxy = null;
            return false;
        }

        /// <summary>True only while this exact claim still holds its proxy.</summary>
        public bool IsHolder(CapsuleProxyLease lease)
            => lease.IsValid
               && lease.Slot >= 0 && lease.Slot < slots.Length
               && slots[lease.Slot].Leased
               && slots[lease.Slot].Generation == lease.Generation;

        public bool TryGetProxy(CapsuleProxyLease lease, out Transform proxy)
        {
            if (!IsHolder(lease)) { proxy = null; return false; }
            proxy = slots[lease.Slot].Proxy;
            return true;
        }

        /// <summary>
        /// Hands a proxy back. Idempotent, and a stale lease is ignored rather
        /// than switching off the proxy that took its place.
        /// </summary>
        public void Release(CapsuleProxyLease lease)
        {
            if (!IsHolder(lease)) return;

            ref Slot slot = ref slots[lease.Slot];
            slot.Leased = false;
            // Generation deliberately not reset: this lease must stay
            // non-matching for as long as the pool lives.
            if (slot.Proxy != null) slot.Proxy.gameObject.SetActive(false);
            leasedCount--;
        }

        /// <summary>Returns every proxy — session teardown, or a demote where nothing should carry over.</summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Leased) continue;
                slots[i].Leased = false;
                if (slots[i].Proxy != null) slots[i].Proxy.gameObject.SetActive(false);
                leasedCount--;
            }
        }
    }
}
