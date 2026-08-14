using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Audio
{
    public enum NebulaAudioBus
    {
        UI,
        Ship,
        Combat,
        Industry,
        World,
        Ambience
    }

    public enum NebulaAudioSpatialMode
    {
        ListenerRelative,
        World3D
    }

    [Serializable]
    public sealed class NebulaAudioEntry
    {
        [SerializeField] private string eventId;
        [SerializeField] private string resourcePath;
        [SerializeField] private NebulaAudioBus bus;
        [SerializeField] private NebulaAudioSpatialMode spatialMode;
        [SerializeField] private bool loop;
        [NonSerialized] private AudioClip loadedClip;
        [NonSerialized] private AudioClip directClip;

        public string EventId => eventId;
        public string ResourcePath => resourcePath;
        public AudioClip Clip
        {
            get
            {
                if (directClip != null) return directClip;
                if (loadedClip == null && !string.IsNullOrEmpty(resourcePath))
                    loadedClip = Resources.Load<AudioClip>(resourcePath);
                return loadedClip;
            }
        }
        public NebulaAudioBus Bus => bus;
        public NebulaAudioSpatialMode SpatialMode => spatialMode;
        public bool Loop => loop;

        public NebulaAudioEntry(string id, string clipResourcePath, NebulaAudioBus audioBus,
            NebulaAudioSpatialMode spatial, bool isLoop)
        {
            eventId = id;
            resourcePath = clipResourcePath;
            bus = audioBus;
            spatialMode = spatial;
            loop = isLoop;
        }

        /// <summary>EditMode-only/test convenience; production catalog entries use resource paths.</summary>
        public NebulaAudioEntry(string id, AudioClip audioClip, NebulaAudioBus audioBus,
            NebulaAudioSpatialMode spatial, bool isLoop)
            : this(id, (string)null, audioBus, spatial, isLoop)
        {
            directClip = audioClip;
        }

        public void ReleaseLoadedClip()
        {
            if (loadedClip != null)
            {
                Resources.UnloadAsset(loadedClip);
                loadedClip = null;
            }
        }
    }

    /// <summary>
    /// Visual-only style runtime reference catalog for all approved Nebula
    /// audio masters. It contains imported clip references and playback
    /// metadata only; it never owns gameplay state, save state or timing.
    /// </summary>
    public sealed class NebulaAudioCatalog : ScriptableObject
    {
        [SerializeField] private List<NebulaAudioEntry> entries = new List<NebulaAudioEntry>();
        private Dictionary<string, NebulaAudioEntry> byId;

        public IReadOnlyList<NebulaAudioEntry> Entries => entries;

        public bool TryGet(string eventId, out NebulaAudioEntry entry)
        {
            EnsureIndex();
            entry = null;
            return !string.IsNullOrEmpty(eventId) && byId.TryGetValue(eventId, out entry);
        }

        public void ReplaceEntries(IReadOnlyList<NebulaAudioEntry> replacement)
        {
            entries.Clear();
            if (replacement != null) entries.AddRange(replacement);
            byId = null;
        }

        private void OnEnable() => byId = null;

        private void EnsureIndex()
        {
            if (byId != null) return;
            byId = new Dictionary<string, NebulaAudioEntry>(StringComparer.Ordinal);
            foreach (NebulaAudioEntry entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.EventId)) continue;
                if (!byId.ContainsKey(entry.EventId)) byId.Add(entry.EventId, entry);
            }
        }

        public void ReleaseLoadedClips()
        {
            foreach (NebulaAudioEntry entry in entries) entry?.ReleaseLoadedClip();
        }
    }
}
