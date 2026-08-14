using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Audio
{
    /// <summary>
    /// Campaign-lifetime audio playback authority. It caches the catalog once,
    /// owns a bounded one-shot pool and one keyed instance per loop owner.
    /// Missing audio is a safe false return, never a boot/gameplay failure.
    /// </summary>
    public sealed class NebulaAudioService : MonoBehaviour
    {
        public const int OneShotPoolSize = 24;
        public const int MaxSameEventOneShots = 3;

        private sealed class PooledSource
        {
            public AudioSource Source;
            public string EventId;
            public NebulaAudioBus Bus;
            public float BaseVolume;
            public float StartedAt;
        }

        private sealed class LoopSource
        {
            public AudioSource Source;
            public string EventId;
            public NebulaAudioBus Bus;
            public float BaseVolume;
        }

        private readonly List<PooledSource> oneShots = new List<PooledSource>();
        private readonly Dictionary<string, LoopSource> loops =
            new Dictionary<string, LoopSource>(StringComparer.Ordinal);
        private readonly Dictionary<NebulaAudioBus, float> busVolumes =
            new Dictionary<NebulaAudioBus, float>();

        private NebulaAudioCatalog catalog;
        private float masterVolume = 1f;
        private float ambienceDuck = 1f;
        private float ambienceDuckTarget = 1f;
        private float duckRemaining;
        private int nextPoolIndex;

        public NebulaAudioCatalog Catalog => catalog;
        public int ActiveLoopCount
        {
            get
            {
                PruneDestroyedLoops();
                return loops.Count;
            }
        }

        public void Initialize(NebulaAudioCatalog runtimeCatalog)
        {
            catalog = runtimeCatalog;
            foreach (NebulaAudioBus bus in Enum.GetValues(typeof(NebulaAudioBus)))
                busVolumes[bus] = 1f;
            BuildOneShotPool();
        }

        public bool HasEvent(string eventId) =>
            catalog != null && catalog.TryGet(eventId, out _);

        /// <summary>Readable as well as writable, so a settings row can show where it is.</summary>
        public float MasterVolume => masterVolume;

        public void SetMasterVolume(float value) => masterVolume = Mathf.Clamp01(value);

        public void SetBusVolume(NebulaAudioBus bus, float value) =>
            busVolumes[bus] = Mathf.Clamp01(value);

        public float GetBusVolume(NebulaAudioBus bus) =>
            busVolumes.TryGetValue(bus, out float value) ? value : 1f;

        private readonly Dictionary<string, int> oneShotCounts = new Dictionary<string, int>();

        /// <summary>
        /// How many times an event has actually been played as a one-shot this session.
        /// <para>
        /// A test seam, and a narrow one: "exactly one sound per transaction" is a claim
        /// about this service, and without a count the only way to check it is to listen.
        /// Two authorities each playing the same completion cue for the same conversion
        /// is a bug that looks identical to correct code from every other angle — it has
        /// already happened once here.
        /// </para>
        /// </summary>
        public int OneShotCount(string eventId) =>
            eventId != null && oneShotCounts.TryGetValue(eventId, out int n) ? n : 0;

        /// <summary>Forgets the counts, so a test can measure one action rather than a session.</summary>
        public void ResetOneShotCounts() => oneShotCounts.Clear();

        public bool PlayOneShot(string eventId, Transform emitter = null, float volume = 1f)
        {
            if (catalog == null || !catalog.TryGet(eventId, out NebulaAudioEntry entry)
                || entry.Clip == null || entry.Loop)
                return false;

            oneShotCounts.TryGetValue(eventId, out int played);
            oneShotCounts[eventId] = played + 1;

            PooledSource slot = FindOneShotSlot(eventId);
            Configure(slot.Source, entry, emitter, loop: false);
            slot.EventId = eventId;
            slot.Bus = entry.Bus;
            slot.BaseVolume = Mathf.Clamp01(volume);
            slot.StartedAt = Time.unscaledTime;
            slot.Source.volume = EffectiveVolume(slot.Bus, slot.BaseVolume);
            slot.Source.Play();

            if (IsCritical(eventId))
            {
                duckRemaining = 0.85f;
                ambienceDuckTarget = 0.56f;
            }
            return true;
        }

        public bool StartLoop(string ownerKey, string eventId, Transform emitter = null,
            float volume = 1f)
        {
            if (string.IsNullOrEmpty(ownerKey) || catalog == null
                || !catalog.TryGet(eventId, out NebulaAudioEntry entry)
                || entry.Clip == null || !entry.Loop)
                return false;

            PruneDestroyedLoops();
            if (loops.TryGetValue(ownerKey, out LoopSource existing))
            {
                if (existing.Source != null && existing.EventId == eventId)
                {
                    existing.BaseVolume = Mathf.Clamp01(volume);
                    return true;
                }
                DestroyLoop(existing);
                loops.Remove(ownerKey);
            }

            var go = new GameObject("Audio Loop · " + ownerKey);
            go.transform.SetParent(emitter != null ? emitter : transform, false);
            var source = go.AddComponent<AudioSource>();
            Configure(source, entry, emitter, loop: true);
            var loopSource = new LoopSource
            {
                Source = source,
                EventId = eventId,
                Bus = entry.Bus,
                BaseVolume = Mathf.Clamp01(volume)
            };
            source.volume = EffectiveVolume(loopSource.Bus, loopSource.BaseVolume);
            source.Play();
            loops.Add(ownerKey, loopSource);
            return true;
        }

        public void StopLoop(string ownerKey, float fadeSeconds = 0f)
        {
            if (string.IsNullOrEmpty(ownerKey) || !loops.TryGetValue(ownerKey, out LoopSource loop))
                return;
            // The generated masters are click-safe at their boundaries. A later
            // AudioMixer phase may apply envelopes; until then immediate stop is
            // deterministic and prevents orphaned fade coroutines on demotion.
            DestroyLoop(loop);
            loops.Remove(ownerKey);
        }

        public bool IsLoopPlaying(string ownerKey, string eventId = null)
        {
            PruneDestroyedLoops();
            return loops.TryGetValue(ownerKey, out LoopSource loop)
                   && loop.Source != null && loop.Source.isPlaying
                   && (eventId == null || loop.EventId == eventId);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (duckRemaining > 0f)
            {
                duckRemaining = Mathf.Max(0f, duckRemaining - dt);
                if (duckRemaining <= 0f) ambienceDuckTarget = 1f;
            }
            ambienceDuck = Mathf.MoveTowards(
                ambienceDuck, ambienceDuckTarget, dt * (ambienceDuckTarget < ambienceDuck ? 5f : 1.8f));

            foreach (PooledSource slot in oneShots)
            {
                if (slot.Source != null && slot.Source.isPlaying)
                    slot.Source.volume = EffectiveVolume(slot.Bus, slot.BaseVolume);
            }
            foreach (LoopSource loop in loops.Values)
            {
                if (loop.Source != null)
                    loop.Source.volume = EffectiveVolume(loop.Bus, loop.BaseVolume);
            }
            PruneDestroyedLoops();
        }

        private void BuildOneShotPool()
        {
            if (oneShots.Count > 0) return;
            for (int i = 0; i < OneShotPoolSize; i++)
            {
                var go = new GameObject("Audio OneShot " + i.ToString("00"));
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                oneShots.Add(new PooledSource { Source = source });
            }
        }

        private PooledSource FindOneShotSlot(string eventId)
        {
            int sameEvent = 0;
            PooledSource oldestSame = null;
            foreach (PooledSource slot in oneShots)
            {
                if (!slot.Source.isPlaying) return slot;
                if (slot.EventId != eventId) continue;
                sameEvent++;
                if (oldestSame == null || slot.StartedAt < oldestSame.StartedAt) oldestSame = slot;
            }
            if (sameEvent >= MaxSameEventOneShots && oldestSame != null) return oldestSame;
            PooledSource selected = oneShots[nextPoolIndex % oneShots.Count];
            nextPoolIndex = (nextPoolIndex + 1) % oneShots.Count;
            return selected;
        }

        private void Configure(AudioSource source, NebulaAudioEntry entry,
            Transform emitter, bool loop)
        {
            source.Stop();
            source.clip = entry.Clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.dopplerLevel = ShouldUseDoppler(entry.EventId) ? 0.35f : 0f;
            source.spatialBlend = entry.SpatialMode == NebulaAudioSpatialMode.World3D ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = MinDistance(entry);
            source.maxDistance = MaxDistance(entry);
            source.priority = Priority(entry);
            source.transform.position = emitter != null ? emitter.position : transform.position;
        }

        private float EffectiveVolume(NebulaAudioBus bus, float baseVolume)
        {
            float busVolume = GetBusVolume(bus);
            float duck = bus == NebulaAudioBus.Ambience ? ambienceDuck : 1f;
            return Mathf.Clamp01(masterVolume * busVolume * baseVolume * duck);
        }

        private static float MinDistance(NebulaAudioEntry entry) =>
            entry.Bus == NebulaAudioBus.Ambience ? 300f :
            entry.Bus == NebulaAudioBus.World ? 40f : 15f;

        private static float MaxDistance(NebulaAudioEntry entry)
        {
            if (entry.EventId.Contains(".star.") || entry.EventId.Contains("black_hole"))
                return 300000f;
            if (entry.Bus == NebulaAudioBus.Ambience) return 60000f;
            if (entry.Bus == NebulaAudioBus.World) return 18000f;
            if (entry.Bus == NebulaAudioBus.Combat) return 12000f;
            return 6500f;
        }

        private static int Priority(NebulaAudioEntry entry)
        {
            if (IsCritical(entry.EventId)) return 32;
            if (entry.Bus == NebulaAudioBus.UI) return 64;
            if (entry.Bus == NebulaAudioBus.Combat) return 96;
            if (entry.Bus == NebulaAudioBus.Ship) return 112;
            return 160;
        }

        private static bool IsCritical(string eventId) =>
            eventId.Contains("critical") || eventId.Contains("warning_high")
            || eventId.Contains("attention_high") || eventId.Contains("shutdown");

        private static bool ShouldUseDoppler(string eventId) =>
            eventId.Contains("vehicle_") || eventId.Contains("projectile")
            || eventId.Contains("flyby");

        private void PruneDestroyedLoops()
        {
            if (loops.Count == 0) return;
            List<string> remove = null;
            foreach (var pair in loops)
            {
                if (pair.Value.Source != null) continue;
                remove ??= new List<string>();
                remove.Add(pair.Key);
            }
            if (remove == null) return;
            foreach (string key in remove) loops.Remove(key);
        }

        private static void DestroyLoop(LoopSource loop)
        {
            if (loop?.Source == null) return;
            GameObject go = loop.Source.gameObject;
            loop.Source.Stop();
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
