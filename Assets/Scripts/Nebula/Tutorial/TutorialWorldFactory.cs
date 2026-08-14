using System;
using System.Collections.Generic;
using UnityEngine;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.Tutorial
{
    /// <summary>
    /// The tutorial's only way to put something in the world.
    /// <para>
    /// Every path here ends in the project's own authored asset: a
    /// <see cref="NebulaVisualCatalog"/> entry resolved through
    /// <see cref="NebulaVisualResolver"/>, an authored prefab under
    /// <c>Resources</c>, or a real pooled lease. There is deliberately no
    /// fallback branch. <c>TrainingArea.BuildDummy</c> has one -- a grey
    /// <c>CreatePrimitive</c> cube when the dummy prefab is missing -- and that
    /// is exactly the failure this class refuses to repeat: a tutorial that
    /// renders a capsule still passes a not-blank screenshot check and still
    /// teaches the player something untrue about the game. A requirement that
    /// does not resolve returns <c>null</c> and records a blocker naming the id,
    /// the path and the step's stated purpose.
    /// </para>
    /// <para>
    /// The gameplay/visual collider split the project already keeps is preserved
    /// by construction: <see cref="Spawn"/> returns a bare holder GameObject and
    /// parents the authored visual under it as a render-only child (the resolver
    /// disables every collider, camera and light inside the visual). A step that
    /// needs a gameplay collider adds it to the holder, which is the same shape
    /// <c>TrainingArea</c> uses for its dummies.
    /// </para>
    /// </summary>
    public sealed class TutorialWorldFactory : ITutorialWorld
    {
        private static Dictionary<string, NebulaVisualDefinition> catalogById;

        private readonly Func<ITutorialStepScope> activeScope;
        private readonly NebulaContext gameplay;
        private readonly List<string> blockers = new List<string>();

        /// <summary>Every requirement that failed to resolve this run, newest last. Never silently swallowed.</summary>
        public IReadOnlyList<string> Blockers => blockers;

        /// <summary>
        /// <paramref name="scopeAccessor"/> is a function rather than a scope so
        /// one factory serves the whole run: the director swaps the active step
        /// scope underneath it, and a spawn always lands in the scope that is
        /// actually current rather than the one that was current at construction.
        /// <paramref name="gameplayContext"/> may be null -- an asset test resolves
        /// requirements with no scene and no session at all.
        /// </summary>
        public TutorialWorldFactory(Func<ITutorialStepScope> scopeAccessor, NebulaContext gameplayContext = null)
        {
            activeScope = scopeAccessor ?? throw new ArgumentNullException(nameof(scopeAccessor));
            gameplay = gameplayContext;
        }

        public void ClearBlockers() => blockers.Clear();

        // ---- resolution -------------------------------------------------------

        public bool CanResolve(TutorialAssetRequirement requirement, out string reason)
        {
            if (string.IsNullOrWhiteSpace(requirement.Id))
            {
                reason = $"{requirement.Kind} requirement has an empty id ({requirement.Purpose}).";
                return false;
            }

            switch (requirement.Kind)
            {
                case TutorialAssetKind.VisualCatalogId:
                case TutorialAssetKind.Vfx:
                {
                    if (!TryGetDefinition(requirement.Id, out NebulaVisualDefinition definition))
                    {
                        reason = $"'{requirement.Id}' is not a NebulaVisualCatalog visual id ({requirement.Purpose}).";
                        return false;
                    }
                    if (NebulaVisualResolver.Load(definition.ResourcePath) == null)
                    {
                        reason = $"'{requirement.Id}' resolves to Resources path '{definition.ResourcePath}', which has no prefab ({requirement.Purpose}).";
                        return false;
                    }
                    reason = null;
                    return true;
                }

                case TutorialAssetKind.Prefab:
                {
                    if (LoadPrefab(requirement.Id) == null)
                    {
                        reason = $"No authored prefab at '{requirement.Id}' ({requirement.Purpose}). " +
                                 "Runtime loads are Resources-relative; an Assets/... path only resolves in the Editor.";
                        return false;
                    }
                    reason = null;
                    return true;
                }
            }

            reason = $"Unknown requirement kind {requirement.Kind} for '{requirement.Id}'.";
            return false;
        }

        /// <summary>The catalog definition behind a visual id, or null. Exposed so an asset test can assert the path as well as the resolve.</summary>
        public static bool TryGetDefinition(string visualId, out NebulaVisualDefinition definition)
        {
            if (catalogById == null)
            {
                catalogById = new Dictionary<string, NebulaVisualDefinition>(StringComparer.Ordinal);
                IReadOnlyList<NebulaVisualDefinition> all = NebulaVisualCatalog.All;
                for (int i = 0; i < all.Count; i++)
                {
                    NebulaVisualDefinition entry = all[i];
                    // First wins: a handful of ids are shared by two catalog
                    // entries (the player and enemy plasma projectile point at
                    // one prefab), and both resolve to the same asset anyway.
                    if (entry != null && !catalogById.ContainsKey(entry.VisualId))
                        catalogById[entry.VisualId] = entry;
                }
            }
            return catalogById.TryGetValue(visualId ?? string.Empty, out definition);
        }

        private static GameObject LoadPrefab(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return null;

            // A player build can only load from Resources, so that is the path a
            // step should declare. The Editor branch exists so an authored prefab
            // outside Resources still resolves while the curriculum is being
            // written, and never so a step can ship depending on it.
            GameObject fromResources = Resources.Load<GameObject>(NormalizeResourcePath(assetPath));
            if (fromResources != null) return fromResources;

#if UNITY_EDITOR
            if (assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
#endif
            return null;
        }

        private static string NormalizeResourcePath(string assetPath)
        {
            const string marker = "Resources/";
            int index = assetPath.LastIndexOf(marker, StringComparison.Ordinal);
            string path = index >= 0 ? assetPath.Substring(index + marker.Length) : assetPath;
            int extension = path.LastIndexOf('.');
            return extension > 0 ? path.Substring(0, extension) : path;
        }

        // ---- spawning ---------------------------------------------------------

        public GameObject Spawn(TutorialAssetRequirement requirement, Vector3 position, Quaternion rotation)
        {
            ITutorialStepScope scope = activeScope();
            if (scope == null)
            {
                Record($"{requirement} was requested with no active step scope -- nothing can own it.");
                return null;
            }

            if (!CanResolve(requirement, out string reason))
            {
                Record(reason);
                return null;
            }

            if (requirement.Kind == TutorialAssetKind.Vfx) return SpawnVfx(requirement, scope, position, rotation);

            GameObject holder = scope.NewGroup(HolderName(requirement));
            holder.transform.SetPositionAndRotation(position, rotation);

            if (requirement.Kind == TutorialAssetKind.Prefab)
            {
                GameObject prefab = LoadPrefab(requirement.Id);
                GameObject instance = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
                instance.name = "Authored :: " + requirement.Id;
                return holder;
            }

            TryGetDefinition(requirement.Id, out NebulaVisualDefinition definition);

            // Through the resolver, never Instantiate directly: it is what applies
            // the authored placement anchor (anchor.surface_mount,
            // anchor.hardpoint.mount, ...), the scale policy, and the render-only
            // sanitisation that keeps the visual's colliders out of gameplay.
            GameObject visual = NebulaVisualResolver.Instantiate(
                definition, holder.transform, instanceName: "Visual :: " + definition.VisualId);

            if (visual == null)
            {
                Record($"'{requirement.Id}' resolved to '{definition.ResourcePath}' but produced no instance ({requirement.Purpose}).");
                return null;
            }
            return holder;
        }

        /// <summary>
        /// Mounts a requirement on a real anchor of an existing object -- a weapon
        /// on the ship's hardpoint, an adapter on a structure's mount.
        /// <para>
        /// Not on <see cref="ITutorialWorld"/>: that interface is frozen and takes
        /// a world position only. A step that needs a hardpoint casts its
        /// <c>context.World</c> to this type. Returns null, with a blocker, when
        /// the host has no anchor by that name -- an anchor typo must not quietly
        /// become a free-floating object next to the ship.
        /// </para>
        /// </summary>
        public GameObject SpawnAttached(TutorialAssetRequirement requirement, Transform host, string hostAnchorName)
        {
            if (host == null)
            {
                Record($"{requirement} was asked to mount on a null host.");
                return null;
            }

            Transform anchor = string.IsNullOrEmpty(hostAnchorName)
                ? host
                : NebulaVisualResolver.FindAnchor(host, hostAnchorName);

            if (anchor == null)
            {
                Record($"{requirement} could not mount: '{host.name}' has no anchor named '{hostAnchorName}'.");
                return null;
            }

            GameObject spawned = Spawn(requirement, anchor.position, anchor.rotation);
            if (spawned == null) return null;

            spawned.transform.SetParent(anchor, false);
            spawned.transform.localPosition = Vector3.zero;
            spawned.transform.localRotation = Quaternion.identity;
            return spawned;
        }

        /// <summary>
        /// A pooled effect, taken on a real lease and registered for return.
        /// <para>
        /// When the id names a pooled item proxy and the session composed the
        /// animation director, this is the project's own
        /// <see cref="ItemProxyPool"/> lease and the scope releases it. Otherwise
        /// it is a scope-owned catalog instance: there is no id-addressable VFX
        /// pool in the project today (the three pools that exist are keyed by
        /// sprite kind, item content id and capsule), so a lease is not available
        /// for every effect and the fallback is ownership rather than a stand-in.
        /// </para>
        /// </summary>
        private GameObject SpawnVfx(TutorialAssetRequirement requirement, ITutorialStepScope scope,
            Vector3 position, Quaternion rotation)
        {
            ItemProxyPool pool = ItemProxies();
            if (pool != null && pool.TryLease(requirement.Id, out ItemProxyLease lease, out Transform proxy) && proxy != null)
            {
                ItemProxyPool leasedFrom = pool;
                ItemProxyLease heldLease = lease;
                scope.OnCleanup(() => leasedFrom.Release(heldLease));
                proxy.SetPositionAndRotation(position, rotation);
                return proxy.gameObject;
            }

            TryGetDefinition(requirement.Id, out NebulaVisualDefinition definition);
            GameObject holder = scope.NewGroup(HolderName(requirement));
            holder.transform.SetPositionAndRotation(position, rotation);

            GameObject instance = NebulaVisualResolver.Instantiate(
                definition, holder.transform, instanceName: "VFX :: " + definition.VisualId);
            if (instance != null) return holder;

            Record($"'{requirement.Id}' resolved to '{definition.ResourcePath}' but produced no VFX instance ({requirement.Purpose}).");
            return null;
        }

        private ItemProxyPool ItemProxies()
        {
            if (gameplay == null) return null;
            try { return gameplay.AnimationDirector != null ? gameplay.AnimationDirector.ItemProxies : null; }
            catch (InvalidOperationException) { return null; } // never composed this run -- documented, not exceptional
        }

        private static string HolderName(TutorialAssetRequirement requirement) =>
            $"Tutorial {requirement.Kind} :: {requirement.Id}";

        private void Record(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return;
            blockers.Add(reason);
            Debug.LogError("[Tutorial] " + reason);
        }
    }
}
