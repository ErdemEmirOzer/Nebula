using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// A claim on one pooled light. Carries a generation stamp, so a handle
    /// held past its light's expiry silently stops matching instead of letting
    /// two owners drive the same light.
    /// </summary>
    public readonly struct TransientLightHandle
    {
        internal readonly int Slot;
        internal readonly int Generation;

        internal TransientLightHandle(int slot, int generation)
        {
            Slot = slot;
            Generation = generation;
        }

        /// <summary>Generation 0 is never issued, so the default value is always the "no light" handle.</summary>
        public bool IsValid => Generation != 0;

        public static TransientLightHandle None => default;
    }

    /// <summary>What an effect is asking the pool for. Duration and intensity are requests, not guarantees — accessibility settings may trim both.</summary>
    public readonly struct TransientLightRequest
    {
        public readonly Vector3 Position;
        public readonly Color Color;
        public readonly float Intensity;
        public readonly float Range;
        public readonly float Duration;

        /// <summary>Higher wins. A request may take over a live light only if that light's priority is strictly lower.</summary>
        public readonly int Priority;

        public TransientLightRequest(Vector3 position, Color color, float intensity, float range, float duration, int priority = 0)
        {
            Position = position;
            Color = color;
            Intensity = intensity;
            Range = range;
            Duration = duration;
            Priority = priority;
        }
    }

    /// <summary>
    /// The fixed set of short-lived lights that weapons, explosions, damage and
    /// structure effects draw from. Every light exists from the moment the pool
    /// is built and is only ever enabled and disabled — there is no
    /// Instantiate on a muzzle flash, no Destroy when it ends, and nothing is
    /// loaded while the game is running.
    /// <para>
    /// The pool can always say no. <see cref="TryAcquire"/> returning false is
    /// a normal outcome at capacity, and callers must treat a missing light as
    /// a purely cosmetic loss: no gameplay result may ever depend on whether an
    /// effect got one.
    /// </para>
    /// </summary>
    public sealed class TransientLightPool
    {
        private struct Slot
        {
            public Light Light;
            public int Generation;
            public float Remaining;
            public int Priority;
            public bool Active;
            public float BaseIntensity;
        }

        private readonly Slot[] slots;
        private readonly Transform root;
        private int nextGeneration = 1;
        private int activeCount;
        private int activeCapacity;

        private TransientLightPool(Transform poolRoot, Slot[] preallocated)
        {
            root = poolRoot;
            slots = preallocated;
            activeCapacity = preallocated.Length;
        }

        /// <summary>Lights that physically exist. Fixed for the life of the pool.</summary>
        public int Capacity => slots.Length;

        /// <summary>
        /// How many of them the current quality profile permits. Lowering this
        /// is how a quality downgrade takes effect: slots above the new ceiling
        /// are handed back immediately and refused to new claims, but the
        /// lights themselves are never destroyed, so raising quality again
        /// costs no allocation.
        /// </summary>
        public int ActiveCapacity => activeCapacity;

        public int ActiveCount => activeCount;
        public Transform Root => root;

        public void SetActiveCapacity(int capacity)
        {
            activeCapacity = Mathf.Clamp(capacity, 0, slots.Length);
            for (int i = activeCapacity; i < slots.Length; i++)
                if (slots[i].Active) Deactivate(i);
        }

        /// <summary>
        /// Builds the pool and every light in it up front, all disabled.
        /// Capacity comes from the quality profile and never changes for the
        /// life of the pool — growing on demand is exactly the allocation
        /// storm this class exists to prevent.
        /// </summary>
        public static TransientLightPool Create(Transform parent, int capacity)
        {
            capacity = Mathf.Max(0, capacity);

            var poolRoot = new GameObject("Transient Lights");
            if (parent != null) poolRoot.transform.SetParent(parent, false);

            var preallocated = new Slot[capacity];
            for (int i = 0; i < capacity; i++)
            {
                Light light = LightingRig.TransientEffect.Create(poolRoot.transform, "Transient Light " + i);
                light.gameObject.SetActive(false);
                preallocated[i] = new Slot { Light = light, Generation = 0, Active = false };
            }

            return new TransientLightPool(poolRoot.transform, preallocated);
        }

        /// <summary>
        /// Claims a light for the request, or reports false when none is
        /// available and no live light is worth displacing. On take-over the
        /// displaced owner's handle is invalidated in the same step, so the
        /// light is never under two owners even for a frame.
        /// </summary>
        public bool TryAcquire(in TransientLightRequest request, out TransientLightHandle handle)
        {
            int slotIndex = FindFreeSlot();
            if (slotIndex < 0) slotIndex = FindDisplaceableSlot(request.Priority);

            if (slotIndex < 0)
            {
                handle = TransientLightHandle.None;
                return false;
            }

            ref Slot slot = ref slots[slotIndex];
            if (!slot.Active) activeCount++;

            // Bumping the generation is what severs any previous owner: their
            // handle no longer matches, so their Release is a no-op.
            slot.Generation = nextGeneration++;
            if (nextGeneration == 0) nextGeneration = 1; // never hand out the "no light" generation
            slot.Active = true;
            slot.Priority = request.Priority;
            slot.Remaining = Mathf.Max(0f, LightingAccessibility.ResolveFlashDuration(request.Duration));
            slot.BaseIntensity = LightingAccessibility.ScaleFlashIntensity(request.Intensity);

            slot.Light.transform.position = request.Position;
            slot.Light.color = request.Color;
            slot.Light.intensity = slot.BaseIntensity;
            slot.Light.range = request.Range;
            slot.Light.gameObject.SetActive(true);

            handle = new TransientLightHandle(slotIndex, slot.Generation);
            return true;
        }

        /// <summary>True only while this exact claim still owns its light.</summary>
        public bool IsOwner(TransientLightHandle handle)
            => handle.IsValid
               && handle.Slot >= 0 && handle.Slot < slots.Length
               && slots[handle.Slot].Active
               && slots[handle.Slot].Generation == handle.Generation;

        /// <summary>The live light behind a claim, for an owner that wants to move or retint it mid-life.</summary>
        public bool TryGetLight(TransientLightHandle handle, out Light light)
        {
            if (!IsOwner(handle)) { light = null; return false; }
            light = slots[handle.Slot].Light;
            return true;
        }

        /// <summary>Hands a light back early. A stale handle is ignored rather than stealing the light from its current owner.</summary>
        public void Release(TransientLightHandle handle)
        {
            if (!IsOwner(handle)) return;
            Deactivate(handle.Slot);
        }

        /// <summary>
        /// Ages every live light and returns the expired ones. Lights further
        /// from the camera than <paramref name="cullDistance"/> stop being
        /// drawn but keep aging, so distance never extends an effect's life.
        /// </summary>
        public void Tick(float deltaTime, Vector3 cameraPosition, float cullDistance)
        {
            float cullSqr = cullDistance * cullDistance;

            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Active) continue;

                slots[i].Remaining -= deltaTime;
                if (slots[i].Remaining <= 0f)
                {
                    Deactivate(i);
                    continue;
                }

                Light light = slots[i].Light;
                bool withinCull = cullDistance <= 0f
                    || (light.transform.position - cameraPosition).sqrMagnitude <= cullSqr;
                if (light.gameObject.activeSelf != withinCull) light.gameObject.SetActive(withinCull);
            }
        }

        /// <summary>Returns every light to the pool — session teardown, or a hard cut where nothing should carry over.</summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Active) Deactivate(i);
        }

        private void Deactivate(int index)
        {
            ref Slot slot = ref slots[index];
            slot.Active = false;
            slot.Remaining = 0f;
            slot.Priority = 0;
            // Generation is deliberately NOT reset: a handle from this claim
            // must stay non-matching for as long as the pool lives.
            if (slot.Light != null) slot.Light.gameObject.SetActive(false);
            activeCount--;
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < activeCapacity; i++)
                if (!slots[i].Active) return i;
            return -1;
        }

        /// <summary>The lowest-priority live light, but only if the incoming request outranks it. Ties never displace.</summary>
        private int FindDisplaceableSlot(int incomingPriority)
        {
            int best = -1;
            int bestPriority = incomingPriority;

            for (int i = 0; i < activeCapacity; i++)
            {
                if (!slots[i].Active) continue;
                if (slots[i].Priority < bestPriority)
                {
                    bestPriority = slots[i].Priority;
                    best = i;
                }
            }

            return best;
        }
    }
}
