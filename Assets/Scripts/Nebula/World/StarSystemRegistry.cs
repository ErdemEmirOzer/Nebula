using System.Collections.Generic;
using UnityEngine;
using Nebula.Sectors;

namespace Nebula.World
{
    /// <summary>One real, navigable system — a live VIEW of a <see cref="SystemManifest"/>, never a second authority for it.</summary>
    public struct StarSystemRecord
    {
        public string SystemId;
        public string DisplayName;
        public StarClass StarClass;
        public float StarRadius;
        public Vector3 AbsoluteCenter;
        public float NavigationRadius;
        public int GenerationVersion;
        public bool IsStartSystem;

        /// <summary>Null unless the system is currently Full or Warm materialized.</summary>
        public Transform StarTransform;
        public SystemStreamingState StreamingState;
    }

    /// <summary>
    /// Small seam so a future alternative world source could replace the
    /// manifest/streaming system without touching any navigation consumer.
    /// </summary>
    public interface INavigationWorldProvider
    {
        bool TryGetSystem(string systemId, out StarSystemRecord record);
        string StartSystemId { get; }

        /// <summary>Every real system in the sector containing <paramref name="absolutePosition"/> plus its 26 neighbors — the only bounded, always-available "nearby systems" query (VS-008 §4/§5).</summary>
        IEnumerable<StarSystemRecord> SystemsNear(Vector3 absolutePosition);

        Vector3 AbsolutePosition(StarSystemRecord record);
    }

    /// <summary>
    /// VS-008: the manifest-backed replacement for the old transform-registry
    /// (every real system used to be a live GameObject registered here at
    /// spawn time). Now every real system is a deterministic
    /// <see cref="SystemManifest"/> resolved on demand from
    /// <see cref="SectorManifestRepository"/>; a live Transform only exists
    /// while <see cref="Streaming"/> has that system Full-materialized.
    /// Static-with-Reset, matching every other campaign-scoped registry in
    /// this project (ExtractionState, ProgressionService, LocationRegistry).
    /// </summary>
    public static class StarSystemRegistry
    {
        public static SectorManifestRepository Manifests { get; private set; }
        public static WorldDeltaRepository Deltas { get; private set; }

        /// <summary>Set once the streaming director MonoBehaviour exists — before that, every query behaves as "nothing materialized".</summary>
        public static SystemStreamingDirector Streaming { get; set; }

        public static string StartSystemId { get; private set; }

        public static void Initialize(ulong seed)
        {
            Manifests = new SectorManifestRepository(seed);
            Deltas = new WorldDeltaRepository();
            Streaming = null;
            StartSystemId = null;
        }

        public static void SetStartSystem(SectorCoordinate startSector, string startSystemId)
        {
            Manifests.SetStartSector(startSector);
            StartSystemId = startSystemId;
        }

        public static void Clear()
        {
            Manifests = null;
            Deltas = null;
            Streaming = null;
            StartSystemId = null;
        }

        public static bool TryGet(string systemId, out StarSystemRecord record)
        {
            record = default;
            SystemManifest manifest = Manifests?.ResolveSystem(systemId);
            if (manifest == null) return false;
            record = ToRecord(manifest);
            return true;
        }

        public static IEnumerable<StarSystemRecord> SystemsNear(Vector3 absolutePosition)
        {
            if (Manifests == null) yield break;
            SectorCoordinate center = SectorCoordinate.FromAbsolutePosition(absolutePosition);
            foreach (SystemManifest system in Manifests.Resolve(center).Systems) yield return ToRecord(system);
            foreach (SectorCoordinate neighbor in center.Neighbors26())
                foreach (SystemManifest system in Manifests.Resolve(neighbor).Systems) yield return ToRecord(system);
        }

        /// <summary>The real system (if any) whose navigation radius contains an absolute position — checks the containing sector plus its 26 neighbors.</summary>
        public static bool TryFindContaining(Vector3 absolutePosition, out StarSystemRecord record)
        {
            foreach (StarSystemRecord candidate in SystemsNear(absolutePosition))
            {
                if ((candidate.AbsoluteCenter - absolutePosition).sqrMagnitude <= candidate.NavigationRadius * candidate.NavigationRadius)
                {
                    record = candidate;
                    return true;
                }
            }
            record = default;
            return false;
        }

        private static StarSystemRecord ToRecord(SystemManifest manifest)
        {
            var record = new StarSystemRecord
            {
                SystemId = manifest.SystemId,
                DisplayName = manifest.DisplayName,
                StarClass = manifest.StarClass,
                StarRadius = manifest.StarRadius,
                AbsoluteCenter = manifest.AbsoluteCenter,
                NavigationRadius = manifest.NavigationRadius,
                GenerationVersion = manifest.GenerationVersion,
                IsStartSystem = manifest.IsStartSystem
            };
            if (Streaming != null)
            {
                record.StarTransform = Streaming.TryGetStarTransform(manifest.SystemId);
                record.StreamingState = Streaming.GetState(manifest.SystemId);
            }
            return record;
        }
    }

    /// <summary>This slice's provider: everything routes through the manifest/streaming system.</summary>
    public class BootstrapNavigationWorldProvider : INavigationWorldProvider
    {
        public bool TryGetSystem(string systemId, out StarSystemRecord record) => StarSystemRegistry.TryGet(systemId, out record);
        public string StartSystemId => StarSystemRegistry.StartSystemId;
        public IEnumerable<StarSystemRecord> SystemsNear(Vector3 absolutePosition) => StarSystemRegistry.SystemsNear(absolutePosition);
        public Vector3 AbsolutePosition(StarSystemRecord record) => record.AbsoluteCenter;
    }
}
