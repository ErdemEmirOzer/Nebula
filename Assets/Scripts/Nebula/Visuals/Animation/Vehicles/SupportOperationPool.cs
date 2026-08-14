using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>A generation-stamped claim on one pooled VFX quad. Releasing twice is harmless.</summary>
    public readonly struct SupportOperationLease
    {
        public readonly NebulaCommonVfx Vfx;
        public readonly int Index;
        public readonly int Generation;

        public SupportOperationLease(NebulaCommonVfx vfx, int index, int generation)
        {
            Vfx = vfx;
            Index = index;
            Generation = generation;
        }

        public bool IsValid => Generation != 0;
        public static SupportOperationLease None => default;
    }

    /// <summary>
    /// The session's operation-VFX quads, built once per sprite and handed out
    /// on lease.
    /// <para>
    /// Everything is drawn with the seven shared sprites that already ship in
    /// <c>Resources/VisualVfx/Common</c>. Each quad is an unlit billboard: it
    /// carries no <see cref="Light"/> and writes no emission, so nothing here
    /// competes with the lighting budget LGT-01 established.
    /// </para>
    /// </summary>
    public sealed class SupportOperationPool
    {
        private sealed class Bucket
        {
            public Transform[] Quads;
            public int[] Generations;
            public bool[] Leased;
        }

        private readonly Dictionary<NebulaCommonVfx, Bucket> buckets = new Dictionary<NebulaCommonVfx, Bucket>();
        private readonly Transform root;
        private readonly int perVfxCapacity;
        private int nextGeneration = 1;
        private int leasedCount;

        public SupportOperationPool(Transform poolRoot, int capacityPerVfx)
        {
            root = poolRoot;
            perVfxCapacity = Mathf.Max(1, capacityPerVfx);
        }

        public int LeasedCount => leasedCount;
        public int CapacityPerVfx => perVfxCapacity;
        public int DistinctVfxCount => buckets.Count;
        public Transform Root => root;

        /// <summary>Every quad this pool has ever built. Bounded by capacity × sprite count, forever.</summary>
        public int TotalQuads
        {
            get
            {
                int total = 0;
                foreach (var kv in buckets) total += kv.Value.Quads.Length;
                return total;
            }
        }

        /// <summary>
        /// Claims a quad for one operation. Returns false when the pool is full,
        /// and the caller then simply shows nothing — an operation must never
        /// depend on getting scenery.
        /// </summary>
        public bool TryLease(NebulaCommonVfx vfx, out SupportOperationLease lease, out Transform quad)
        {
            lease = SupportOperationLease.None;
            quad = null;

            if (!buckets.TryGetValue(vfx, out Bucket bucket))
            {
                bucket = BuildBucket(vfx);
                if (bucket == null) return false; // sprite missing — documented no-op
                buckets[vfx] = bucket;
            }

            for (int i = 0; i < bucket.Leased.Length; i++)
            {
                if (bucket.Leased[i]) continue;

                bucket.Leased[i] = true;
                bucket.Generations[i] = nextGeneration++;
                if (nextGeneration == 0) nextGeneration = 1;
                leasedCount++;

                if (bucket.Quads[i] != null) bucket.Quads[i].gameObject.SetActive(true);

                lease = new SupportOperationLease(vfx, i, bucket.Generations[i]);
                quad = bucket.Quads[i];
                return true;
            }

            return false;
        }

        /// <summary>True while this lease still owns the slot it was given.</summary>
        public bool IsHolder(SupportOperationLease lease)
        {
            if (!lease.IsValid) return false;
            if (!buckets.TryGetValue(lease.Vfx, out Bucket bucket)) return false;
            if (lease.Index < 0 || lease.Index >= bucket.Generations.Length) return false;
            return bucket.Leased[lease.Index] && bucket.Generations[lease.Index] == lease.Generation;
        }

        public bool TryGetQuad(SupportOperationLease lease, out Transform quad)
        {
            quad = null;
            if (!IsHolder(lease)) return false;
            quad = buckets[lease.Vfx].Quads[lease.Index];
            return quad != null;
        }

        /// <summary>Idempotent: a stale lease releases nothing that has since been handed on.</summary>
        public void Release(SupportOperationLease lease)
        {
            if (!IsHolder(lease)) return;

            Bucket bucket = buckets[lease.Vfx];
            bucket.Leased[lease.Index] = false;
            bucket.Generations[lease.Index] = 0;
            leasedCount--;

            Transform quad = bucket.Quads[lease.Index];
            if (quad != null)
            {
                quad.SetParent(root, false);
                quad.localPosition = Vector3.zero;
                quad.localRotation = Quaternion.identity;
                quad.localScale = Vector3.one;
                quad.gameObject.SetActive(false);
            }
        }

        /// <summary>Hands everything back — a demote, a dock, or a session teardown.</summary>
        public void ReleaseAll()
        {
            foreach (var kv in buckets)
            {
                Bucket bucket = kv.Value;
                for (int i = 0; i < bucket.Leased.Length; i++)
                {
                    if (!bucket.Leased[i]) continue;
                    bucket.Leased[i] = false;
                    bucket.Generations[i] = 0;

                    Transform quad = bucket.Quads[i];
                    if (quad == null) continue;
                    quad.SetParent(root, false);
                    quad.localPosition = Vector3.zero;
                    quad.localRotation = Quaternion.identity;
                    quad.localScale = Vector3.one;
                    quad.gameObject.SetActive(false);
                }
            }
            leasedCount = 0;
        }

        /// <summary>
        /// Builds this sprite's quads once. Returns null when the texture is
        /// missing, so a broken import costs a missing effect and nothing else.
        /// </summary>
        private Bucket BuildBucket(NebulaCommonVfx vfx)
        {
            Texture2D texture = NebulaProducedVisualResolver.VfxTexture(vfx);
            if (texture == null) return null;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            // One material per sprite, shared by that sprite's quads: unlit,
            // transparent, and outside the lighting budget entirely.
            var material = RuntimeResourceScope.Own(new Material(shader) { mainTexture = texture });

            var bucket = new Bucket
            {
                Quads = new Transform[perVfxCapacity],
                Generations = new int[perVfxCapacity],
                Leased = new bool[perVfxCapacity]
            };

            for (int i = 0; i < perVfxCapacity; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = $"Operation VFX ({vfx}) {i}";

                // A pooled effect is scenery: nothing about it may be hit,
                // pushed, or picked up.
                Collider collider = go.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying) Object.Destroy(collider);
                    else Object.DestroyImmediate(collider); // edit-mode tests: Destroy would never run
                }

                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }

                go.transform.SetParent(root, false);
                go.SetActive(false);
                bucket.Quads[i] = go.transform;
            }

            return bucket;
        }
    }
}
