using System;
using System.Collections.Generic;
using Nebula.Combat;
using Nebula.Modules;
using Nebula.Progression;
using Nebula.Visuals;
using UnityEngine;

namespace Nebula.Ship
{
    /// <summary>
    /// Presentation-only owner of the Part 24 player-ship hull. It mirrors the
    /// existing level/module authorities into one Mk I/Mk II cumulative stage
    /// prefab; it never writes progression, gameplay, collision, or save data.
    /// Non-contiguous owned modules remain handled by ShipModuleVisuals.
    /// </summary>
    public sealed class PlayerShipVisualController : MonoBehaviour
    {
        public const int MaxStage = 12;
        public const float MkIEnvelope = 8.5f;
        public const float MkIIEnvelope = 10.5f;
        // Part 24 hardpoint weapons were authored at a standalone showcase
        // scale. Mounting them at 1:1 makes a laser almost as long as the Mk I
        // hull, so keep the gameplay hardpoint while fitting only its visual.
        public const float ExternalWeaponVisualScale = 0.28f;

        /// <summary>
        /// The hull the fail-safe path draws, named as a <c>Resources</c> path
        /// rather than resolved through <see cref="NebulaVisualCatalog"/>.
        /// <para>
        /// It is the same authored asset the catalog would hand back for
        /// <c>(hullForm 1, stage 0)</c> — deliberately, because the alternative is
        /// a second player-ship visual authority that would have to be re-authored
        /// every time the real one changes and would be wrong the first time
        /// nobody remembered to. What is duplicated here is a single string, not a
        /// model, and <see cref="Vs018FallbackBindingIsThePrefabTheCatalogWouldPick"/>
        /// in the test suite pins the two together so the duplication cannot drift.
        /// </para>
        /// <para>
        /// The load is spelled out rather than routed through the catalog because
        /// of when it runs: <see cref="CreateFallbackHullVisual"/> is only reached
        /// after <see cref="CreateInitialVisual"/> has already failed, and the
        /// failure modes that get it there are exactly the ones that make the
        /// catalog untrustworthy — a null definition table, a type initialiser
        /// that threw, a content revision that resolved to nothing. A fail-safe
        /// that asks the failing subsystem for the answer is not a fail-safe. This
        /// path touches nothing but <see cref="Resources"/>.
        /// </para>
        /// <para>
        /// It replaced <c>Resources/Ship/PlayerShip.glb</c>, a 25.7 MB raw GLB that
        /// expanded to 70.0 MB in the player — 10.3% of the whole build and its
        /// largest single asset — for a picture the player sees only when the
        /// entire 26-prefab Part 24 family has failed to load. The prefab named
        /// here already ships for the normal path, so the fail-safe now costs
        /// nothing at all.
        /// </para>
        /// </summary>
        public const string FallbackHullResourcePath = "VisualPrefabs/PlayerShip/MkI/PlayerShip_MkI_Base";

        public static readonly string[] StageModuleIds =
        {
            "industry.logistics_cargo",
            "industry.resource_processing",
            "industry.station_construction",
            "exploration.scanner",
            "exploration.engine_navigation",
            "exploration.hangar_bot",
            "defense.hull_reinforcement",
            "defense.shield_systems",
            "defense.weapon_coordination",
            "nuclear.star_energy_storage",
            "nuclear.laboratory",
            "nuclear.interstellar_logistics"
        };

        /// <summary>LGT-03: read by <c>ShipLightingPresenter</c> so module panel lighting resolves from the real prefab anchors rather than a second hardcoded list.</summary>
        public static readonly string[] LogicalModuleAnchors =
        {
            "anchor.industry.logistics_cargo",
            "anchor.industry.resource_processing",
            "anchor.industry.station_construction",
            "anchor.exploration.scanner",
            "anchor.exploration.engine_navigation",
            "anchor.exploration.hangar_bot",
            "anchor.defense.hull_reinforcement",
            "anchor.defense.shield_systems",
            "anchor.defense.weapon_coordination",
            "anchor.nuclear.star_energy_storage",
            "anchor.nuclear.laboratory",
            "anchor.nuclear.interstellar_logistics"
        };

        /// <summary>Name given to the fail-safe hull, so a scene walk can tell it from a catalog visual.</summary>
        public const string FallbackVisualName = "Player Ship Fail-Safe Hull";

        private ModuleProgressionState modules;
        private GameObject visual;
        private GameObject fallbackVisual;
        private readonly Dictionary<string, Transform> currentAnchors =
            new Dictionary<string, Transform>(StringComparer.Ordinal);
        private int shownHullForm = -1;
        private int shownStage = -1;
        private bool catalogUnavailable;
        private bool fallbackIsOwned;

        public int CurrentHullForm => shownHullForm;
        public int CurrentStage => shownStage;
        public GameObject CurrentVisual => visual;

        /// <summary>The fail-safe hull, once one has been mounted. Null on the normal path.</summary>
        public GameObject CurrentFallback => fallbackVisual;

        /// <summary>
        /// True once a catalog lookup threw rather than merely declining. Latched:
        /// <see cref="Update"/> calls <see cref="Refresh"/> every frame, so a
        /// catalog that is broken rather than empty would otherwise throw sixty
        /// times a second behind a caught exception nobody reads.
        /// </summary>
        public bool CatalogUnavailable => catalogUnavailable;

        public bool CreateInitialVisual() => Refresh(force: true);

        /// <summary>
        /// Mounts the fail-safe hull, and is the only place allowed to.
        /// <para>
        /// The two guards are the whole point. The first refuses to draw anything
        /// while the catalog path already has a hull on screen — two player ships
        /// occupying the same transform is not a degraded picture, it is z-fighting
        /// on every surface plus a doubled silhouette, and it is the failure a
        /// "just spawn a spare" fail-safe produces the first time the ordering
        /// changes. The second makes the call idempotent, so a second caller (or a
        /// retry after a transient failure) gets the instance that already exists
        /// instead of a second copy of a 26-renderer prefab.
        /// </para>
        /// <para>
        /// Presentation only. It mounts a render-only child of the ship root and
        /// touches nothing else: no collider, no <c>Rigidbody</c>, no anchor, no
        /// module stage, no progression. The gameplay envelope is unchanged whether
        /// this ran or not, which is what makes it safe to reach for at any point
        /// during startup.
        /// </para>
        /// </summary>
        /// <returns>The mounted hull, the one already mounted, or null when the
        /// catalog path owns the picture or the prefab is absent. Null is a normal
        /// answer, and the caller drops to the primitive hull.</returns>
        public GameObject CreateFallbackHullVisual()
        {
            if (visual != null) return null;
            if (fallbackVisual != null) return fallbackVisual;

            GameObject instance = NebulaVisualResolver.InstantiatePrefabPath(
                FallbackHullResourcePath, transform, MkIEnvelope, FallbackVisualName);
            if (instance == null) return null;

            fallbackVisual = instance;
            fallbackIsOwned = true;
            return instance;
        }

        /// <summary>
        /// Adopts a fail-safe somebody else built — in practice the factory's
        /// primitive hull, the last resort when even the authored prefab will not
        /// load.
        /// <para>
        /// A registered fallback is only ever hidden, never destroyed, because its
        /// lifetime belongs to whoever built it and the caller may still hold a
        /// reference. One this controller created is destroyed instead: see
        /// <see cref="Refresh"/>.
        /// </para>
        /// </summary>
        public void RegisterFallback(GameObject fallback)
        {
            fallbackVisual = fallback;
            fallbackIsOwned = false;
            if (visual != null && fallbackVisual != null) fallbackVisual.SetActive(false);
        }

        public void Initialize(ModuleProgressionState moduleState)
        {
            modules = moduleState;
            Refresh(force: true);
        }

        private void Update() => Refresh(force: false);

        public static int ResolveCumulativeStage(ModuleProgressionState state)
        {
            if (state == null) return 0;
            int stage = 0;
            for (int index = 0; index < StageModuleIds.Length; index++)
            {
                if (state.GetTier(StageModuleIds[index]) <= 0) break;
                stage++;
            }
            return stage;
        }

        public bool IncludesModule(string moduleId)
        {
            int index = Array.IndexOf(StageModuleIds, moduleId);
            return index >= 0 && index < shownStage;
        }

        public Transform FindCurrentAnchor(string exactName)
        {
            if (string.IsNullOrEmpty(exactName)) return null;
            return currentAnchors.TryGetValue(exactName, out Transform anchor) ? anchor : null;
        }

        private bool Refresh(bool force)
        {
            // Once the catalog has thrown, stop asking. Update() runs this every
            // frame, and a broken table throws every time it is touched — the
            // fail-safe hull is already on screen and re-deriving that conclusion
            // sixty times a second buys nothing but garbage and log noise.
            if (catalogUnavailable) return false;

            int hullForm = CombatCatalog.HullFormForLevel(ProgressionService.State.Level);
            int stage = ResolveCumulativeStage(modules);
            if (!force && hullForm == shownHullForm && stage == shownStage && visual != null)
            {
                SyncLogicalAnchors();
                return true;
            }

            // The catalog is allowed to decline (a null definition) and it is
            // allowed to be broken (a type initialiser that threw, a table that
            // resolved to nothing). Both have to end up at the same place — a
            // false return, so the caller reaches the fail-safe hull — because an
            // exception escaping here propagates out of ShipFactory.Create and
            // takes the whole ship down, and a player ship that fails to exist is
            // a far worse outcome than one drawn from the wrong stage prefab.
            GameObject replacement;
            try
            {
                if (!NebulaVisualCatalog.TryGetPlayerShip(
                        hullForm, stage, out NebulaVisualDefinition definition))
                    return false;

                replacement = NebulaVisualResolver.Instantiate(
                    definition, transform, hullForm >= 2 ? MkIIEnvelope : MkIEnvelope,
                    $"Player Ship Mk {(hullForm >= 2 ? "II" : "I")} Stage {stage:00}");
            }
            catch (Exception exception)
            {
                if (!catalogUnavailable)
                {
                    catalogUnavailable = true;
                    Debug.LogWarning(
                        "PlayerShipVisualController: the visual catalog is unavailable, so the "
                        + "ship falls back to its fail-safe hull. " + exception);
                }
                return false;
            }

            if (replacement == null) return false;

            if (visual != null) NebulaVisualResolver.DestroyInstance(visual);
            visual = replacement;
            shownHullForm = hullForm;
            shownStage = stage;

            // The catalog path has the picture now, so the fail-safe goes away.
            // A hull this controller created is destroyed rather than hidden: it
            // is a twenty-six-renderer prefab instance, and leaving one resident
            // and invisible behind every ship is the same kind of waste the pass
            // that introduced it exists to remove. It costs nothing to recreate.
            // One somebody else registered is only hidden — its lifetime is the
            // caller's, and destroying an object another component still points at
            // trades a duplicate for a null reference.
            if (fallbackVisual != null)
            {
                if (fallbackIsOwned)
                {
                    NebulaVisualResolver.DestroyInstance(fallbackVisual);
                    fallbackVisual = null;
                    fallbackIsOwned = false;
                }
                else
                {
                    fallbackVisual.SetActive(false);
                }
            }
            IndexCurrentAnchors();
            SyncLogicalAnchors();
            return true;
        }

        private void IndexCurrentAnchors()
        {
            currentAnchors.Clear();
            if (visual == null) return;
            foreach (Transform candidate in visual.GetComponentsInChildren<Transform>(true))
            {
                if (!candidate.name.StartsWith("anchor.", StringComparison.Ordinal)) continue;
                if (!currentAnchors.ContainsKey(candidate.name))
                    currentAnchors.Add(candidate.name, candidate);
            }
        }

        private void SyncLogicalAnchors()
        {
            if (visual == null) return;
            for (int index = 0; index < LogicalModuleAnchors.Length; index++)
            {
                Transform source = FindCurrentAnchor($"anchor.module.{index + 1:00}");
                Transform target = transform.Find(LogicalModuleAnchors[index]);
                if (source == null || target == null) continue;
                target.SetPositionAndRotation(source.position, source.rotation);
            }
        }
    }
}
