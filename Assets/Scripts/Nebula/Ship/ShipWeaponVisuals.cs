using System.Collections.Generic;
using UnityEngine;
using Nebula.Combat;
using Nebula.Visuals;

namespace Nebula.Ship
{
    /// <summary>
    /// Render-only adapter for the ship's equipped hardpoints. The existing
    /// HardpointState remains the sole equipment authority; this component
    /// only mirrors it into authored weapon prefabs.
    /// </summary>
    public sealed class ShipWeaponVisuals : MonoBehaviour
    {
        private ShipCombat combat;
        private PlayerShipVisualController playerShipVisual;
        private readonly Dictionary<int, GameObject> visuals = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, WeaponFamily?> shownFamilies = new Dictionary<int, WeaponFamily?>();
        private readonly Dictionary<int, Transform> mounts = new Dictionary<int, Transform>();

        /// <summary>
        /// One stable muzzle per hardpoint, parented to the mount rather than to
        /// the weapon model. ANI-04.1 animates the model's gimbal and slides it
        /// back on recoil; a projectile spawned off a node inside that hierarchy
        /// would therefore leave from wherever the animation happened to be.
        /// These follow the ship and the mount and nothing else.
        /// </summary>
        private readonly Dictionary<int, Transform> gameplayMuzzles = new Dictionary<int, Transform>();

        private void Awake()
        {
            combat = GetComponent<ShipCombat>();
            playerShipVisual = GetComponent<PlayerShipVisualController>();
        }

        private void Update() => Refresh();

        public void Refresh()
        {
            if (combat == null || combat.Hardpoints == null) return;
            for (int index = 0; index < combat.Hardpoints.Length; index++)
            {
                HardpointState hardpoint = combat.Hardpoints[index];
                WeaponFamily? family = hardpoint.IsEmpty ? null : hardpoint.Weapon;
                Transform mount = GetOrCreateMount(index);
                SyncMount(index, mount);
                if (shownFamilies.TryGetValue(index, out WeaponFamily? shown) && shown == family) continue;
                shownFamilies[index] = family;

                if (visuals.TryGetValue(index, out GameObject old) && old != null)
                    NebulaVisualResolver.DestroyInstance(old);
                visuals.Remove(index);
                DestroyGameplayMuzzle(index);
                MountedSerial++;
                if (!family.HasValue) continue;

                if (!NebulaVisualCatalog.TryGetWeapon(family.Value, out NebulaVisualDefinition definition)) continue;
                Transform visualParent = mount;
                GameObject scaleRoot = null;
                if (playerShipVisual != null && playerShipVisual.CurrentVisual != null)
                {
                    scaleRoot = new GameObject($"Part 24 {family.Value} Hardpoint {index} Scale");
                    scaleRoot.transform.SetParent(mount, false);
                    scaleRoot.transform.localScale =
                        Vector3.one * PlayerShipVisualController.ExternalWeaponVisualScale;
                    visualParent = scaleRoot.transform;
                }

                GameObject visual = NebulaVisualResolver.Instantiate(
                    definition, visualParent, instanceName: $"Authored {family.Value} Hardpoint {index}");
                if (visual != null)
                {
                    visuals[index] = scaleRoot != null ? scaleRoot : visual;
                    BuildGameplayMuzzle(index, family.Value, mount, visual.transform);
                    MountedSerial++;
                }
                else if (scaleRoot != null)
                    NebulaVisualResolver.DestroyInstance(scaleRoot);
            }
        }

        /// <summary>
        /// The weapon visuals currently mounted, as (family, instance). A
        /// read-only view for presentation — ANI-04.1 uses it to find each
        /// model's own gimbal parts. Nothing here is a second loadout authority.
        /// </summary>
        public IEnumerable<(WeaponFamily family, GameObject visual)> MountedWeapons
        {
            get
            {
                foreach (KeyValuePair<int, GameObject> pair in visuals)
                {
                    if (pair.Value == null) continue;
                    if (!shownFamilies.TryGetValue(pair.Key, out WeaponFamily? shown) || !shown.HasValue) continue;
                    yield return (shown.Value, pair.Value);
                }
            }
        }

        /// <summary>Changes whenever the mounted set changes, so a presenter knows to rebind.</summary>
        public int MountedSerial { get; private set; }

        /// <summary>
        /// Where a shot really leaves from. Stable by construction: it lives
        /// outside the animated gimbal/recoil hierarchy, so the same shot spawns
        /// in the same place and direction whether or not the weapon is mid-kick.
        /// </summary>
        public Transform GetMuzzle(WeaponFamily family)
        {
            foreach (KeyValuePair<int, WeaponFamily?> shown in shownFamilies)
            {
                if (!shown.Value.HasValue || shown.Value.Value != family) continue;
                if (gameplayMuzzles.TryGetValue(shown.Key, out Transform stable) && stable != null) return stable;
            }
            return transform;
        }

        /// <summary>
        /// Where the muzzle currently *looks* like it is — inside the animated
        /// model, so a flash sits on the barrel as it kicks. Presentation only:
        /// nothing that spawns a projectile may use this.
        /// </summary>
        public Transform GetVisualMuzzle(WeaponFamily family)
        {
            foreach (KeyValuePair<int, GameObject> pair in visuals)
            {
                if (!shownFamilies.TryGetValue(pair.Key, out WeaponFamily? shown) || shown != family || pair.Value == null) continue;
                Transform anchor = NebulaVisualResolver.FindAnchor(pair.Value.transform, MuzzleAnchorName(family));
                if (anchor != null) return anchor;
            }
            return GetMuzzle(family);
        }

        private static string MuzzleAnchorName(WeaponFamily family)
            => family == WeaponFamily.Laser ? "anchor.muzzle.laser" : "anchor.projectile.spawn";

        /// <summary>
        /// Copies the authored muzzle's pose onto a transform parented to the
        /// mount. Taken the instant the model is built, before any animation has
        /// touched it, so it records where the weapon points at rest.
        /// </summary>
        private void BuildGameplayMuzzle(int index, WeaponFamily family, Transform mount, Transform visualRoot)
        {
            DestroyGameplayMuzzle(index);

            Transform authored = NebulaVisualResolver.FindAnchor(visualRoot, MuzzleAnchorName(family));
            if (authored == null || mount == null) return;

            var go = new GameObject($"gameplay.muzzle.hardpoint.{index}");
            go.transform.SetParent(mount, false);
            go.transform.SetPositionAndRotation(authored.position, authored.rotation);
            gameplayMuzzles[index] = go.transform;
        }

        private void DestroyGameplayMuzzle(int index)
        {
            if (!gameplayMuzzles.TryGetValue(index, out Transform stable)) return;
            gameplayMuzzles.Remove(index);
            if (stable == null) return;
            if (Application.isPlaying) Destroy(stable.gameObject);
            else DestroyImmediate(stable.gameObject);
        }

        private void OnDestroy()
        {
            // Never leave a stable anchor behind a weapon that is gone.
            var indices = new List<int>(gameplayMuzzles.Keys);
            for (int i = 0; i < indices.Count; i++) DestroyGameplayMuzzle(indices[i]);
        }

        private Transform GetOrCreateMount(int index)
        {
            if (mounts.TryGetValue(index, out Transform mount) && mount != null) return mount;
            var go = new GameObject($"anchor.weapon.hardpoint.{index}");
            go.transform.SetParent(transform, false);
            float side = index % 2 == 0 ? -1f : 1f;
            int row = index / 2;
            go.transform.localPosition = new Vector3(side * (1.05f + row * 0.35f), 0.25f + row * 0.22f, 0.75f - row * 0.8f);
            go.transform.localRotation = Quaternion.identity;
            mounts[index] = go.transform;
            return go.transform;
        }

        private void SyncMount(int index, Transform mount)
        {
            Transform authored = playerShipVisual != null
                ? playerShipVisual.FindCurrentAnchor($"anchor.hardpoint.{index + 1:00}")
                : null;
            if (authored == null) return;
            mount.SetPositionAndRotation(authored.position, authored.rotation);
        }
    }
}
