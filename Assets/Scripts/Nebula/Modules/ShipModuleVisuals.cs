using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Nebula.Visuals;

namespace Nebula.Modules
{
    public enum ConstructionStage { None, Skeleton, Assembly, Calibration }

    /// <summary>Stable identity for a future final-asset/animation/VFX/SFX pass to key off.</summary>
    public struct ModuleVisualHookArgs
    {
        public string AssetId;
        public string AnchorId;
        public string TierVisualId;
        public ConstructionStage Stage;
    }

    /// <summary>
    /// Placeholder physical visualization of the three free Industry modules
    /// on the ship: a primitive body per anchor that grows and warms in color
    /// with tier (plus stacked "tier pip" cubes, so tier is never color-only
    /// readable), and cycles through Skeleton (0-25%) / Assembly (25-75%) /
    /// Calibration (75-100%, pulsing) while that module has an active
    /// upgrade job. Reads <see cref="ModuleProgressionState"/> and the active
    /// <see cref="ModuleUpgradeJob"/> only — it never writes gameplay state,
    /// so it is exactly as safe to run on the live ship as on the
    /// progression screen's inert preview clone (which simply never calls
    /// Initialize, so its geometry is a frozen snapshot). AssetId/AnchorId/
    /// TierVisualId/Stage are exposed via two events as the stable hook point
    /// for a future final-asset/animation/VFX/SFX pass — no such assets are
    /// created here.
    /// </summary>
    public class ShipModuleVisuals : MonoBehaviour
    {
        public static readonly string[] VisualizedModuleIds =
        {
            "industry.logistics_cargo", "industry.resource_processing", "industry.station_construction",
            "exploration.scanner", "exploration.engine_navigation", "exploration.hangar_bot",
            "defense.hull_reinforcement", "defense.shield_systems", "defense.weapon_coordination",
            "nuclear.star_energy_storage", "nuclear.laboratory", "nuclear.interstellar_logistics"
        };

        // Modules that may legitimately sit at T0 (hidden until first purchased
        // or while their T0->T1 upgrade job is in progress) — the original
        // three free Industry modules are always >=T1 and keep the old
        // always-visible behavior untouched.
        private static readonly HashSet<string> CanBeHiddenAtT0 = new HashSet<string>
        {
            "exploration.scanner", "exploration.engine_navigation", "exploration.hangar_bot",
            "defense.hull_reinforcement", "defense.shield_systems", "defense.weapon_coordination",
            "nuclear.star_energy_storage", "nuclear.laboratory", "nuclear.interstellar_logistics"
        };

        public event Action<ModuleVisualHookArgs> TierVisualChanged;
        public event Action<ModuleVisualHookArgs> ConstructionStageChanged;

        private ModuleProgressionState modules;
        private ModuleUpgradeService upgrades;
        private Nebula.Ship.PlayerShipVisualController playerShipVisual;
        private readonly Dictionary<string, Transform> anchors = new Dictionary<string, Transform>();
        private readonly Dictionary<string, GameObject> anchorBodies = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, int> lastShownTier = new Dictionary<string, int>();
        private readonly Dictionary<string, ConstructionStage> lastStage = new Dictionary<string, ConstructionStage>();
        private readonly Dictionary<string, bool> lastVisible = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> lastEmbedded = new Dictionary<string, bool>();

        public void Initialize(ModuleProgressionState moduleState, ModuleUpgradeService upgradeService)
        {
            modules = moduleState;
            upgrades = upgradeService;
            playerShipVisual = GetComponent<Nebula.Ship.PlayerShipVisualController>();
            foreach (string id in VisualizedModuleIds)
            {
                ModuleDef def = ModuleCatalog.Get(id);
                Transform anchor = transform.Find(def.VisualAnchorId);
                if (anchor == null) continue;
                anchors[id] = anchor;
                lastShownTier[id] = -1;
                lastStage[id] = ConstructionStage.None;
                lastVisible[id] = false;
                lastEmbedded[id] = false;
            }
            RefreshAll();
        }

        /// <summary>
        /// ANI-05.1.1 presentation read: a module is working while it really has
        /// a job. The job's own timer, recipe and output stay where they are.
        /// </summary>
        private Nebula.Extraction.ShipProcessing processingCache;

        private Nebula.Visuals.MechanicalDrive ResolveModuleDrive(string moduleId, ConstructionStage stage)
        {
            // A module still being built or upgraded works to its own real job
            // stage; nothing here reads or advances that job.
            if (stage == ConstructionStage.Skeleton) return Nebula.Visuals.MechanicalDrive.Stowed;
            if (stage == ConstructionStage.Assembly) return Nebula.Visuals.MechanicalDrive.Working;

            // Only the processing module has loaded material to follow; the rest
            // sit installed and ready.
            if (moduleId != "industry.resource_processing") return Nebula.Visuals.MechanicalDrive.Ready;

            var shipProcessing = processingCache != null
                ? processingCache
                : (processingCache = GetComponentInParent<Nebula.Extraction.ShipProcessing>());
            if (shipProcessing == null) return Nebula.Visuals.MechanicalDrive.Ready;

            // VS-017A.2: the timed slot bank this used to walk is gone — a conversion
            // is instant, so there is no running job to catch mid-flight. The honest
            // equivalent is the bench holding material: that is the only processing
            // state with any duration left. The bench, its contents and its
            // transactions stay entirely ShipProcessing's.
            return shipProcessing.Bench.IsEmpty
                ? Nebula.Visuals.MechanicalDrive.Ready
                : Nebula.Visuals.MechanicalDrive.Working;
        }


        private void Update() => RefreshAll();

        private void RefreshAll()
        {
            if (modules == null) return;
            foreach (string id in VisualizedModuleIds) Refresh(id);
        }

        private void Refresh(string moduleId)
        {
            if (!anchors.TryGetValue(moduleId, out Transform anchor)) return;

            int currentTier = modules.GetTier(moduleId);
            ConstructionStage stage = ConstructionStage.None;
            float progress = 0f;

            ModuleUpgradeJob job = upgrades?.ActiveJob;
            bool buildingThis = job != null && job.ModuleId == moduleId && !job.Completed && !job.Cancelled;
            if (buildingThis)
            {
                progress = job.Progress01;
                stage = progress < 0.25f ? ConstructionStage.Skeleton
                       : progress < 0.75f ? ConstructionStage.Assembly
                       : ConstructionStage.Calibration;
            }

            // T0-hideable modules (Scanner/Engine-Navigation) are invisible
            // until first purchased, except while an upgrade job from T0 is
            // actively building them (so the construction stages are visible).
            bool visible = !CanBeHiddenAtT0.Contains(moduleId) || currentTier >= 1 || buildingThis;
            int tier = Mathf.Max(1, currentTier); // size/color/pip formulas assume >=1 while anything is shown
            bool embedded = !buildingThis
                            && playerShipVisual != null
                            && playerShipVisual.IncludesModule(moduleId);

            bool tierChanged = lastShownTier[moduleId] != tier;
            bool stageChanged = lastStage[moduleId] != stage;
            bool visibilityChanged = lastVisible[moduleId] != visible;
            bool embeddedChanged = lastEmbedded[moduleId] != embedded;
            if (!tierChanged && !stageChanged && !visibilityChanged && !embeddedChanged) return;

            lastShownTier[moduleId] = tier;
            lastStage[moduleId] = stage;
            lastVisible[moduleId] = visible;
            lastEmbedded[moduleId] = embedded;

            if (!visible)
            {
                if (anchorBodies.TryGetValue(moduleId, out GameObject hidden) && hidden != null) Destroy(hidden);
                anchorBodies.Remove(moduleId);
                return;
            }

            RebuildAnchorBody(moduleId, anchor, tier, stage, progress);
            if (anchorBodies.TryGetValue(moduleId, out GameObject body) && body != null)
                body.SetActive(!embedded);

            var args = new ModuleVisualHookArgs
            {
                AssetId = $"module.{moduleId}.t{tier}",
                AnchorId = ModuleCatalog.Get(moduleId).VisualAnchorId,
                TierVisualId = $"{moduleId}.tier{tier}",
                Stage = stage
            };
            if (tierChanged) TierVisualChanged?.Invoke(args);
            if (stageChanged) ConstructionStageChanged?.Invoke(args);
        }

        private void RebuildAnchorBody(string moduleId, Transform anchor, int tier, ConstructionStage stage, float progress)
        {
            if (anchorBodies.TryGetValue(moduleId, out GameObject old) && old != null) Destroy(old);

            var body = new GameObject($"{moduleId} Visual (T{tier}{(stage != ConstructionStage.None ? " " + stage : "")})");
            body.transform.SetParent(anchor, false);
            anchorBodies[moduleId] = body;

            if (NebulaVisualCatalog.TryGetModule(moduleId, tier, out NebulaVisualDefinition definition))
            {
                GameObject authored = NebulaVisualResolver.Instantiate(
                    definition, body.transform, instanceName: $"Authored {moduleId} T{tier} Visual");
                if (authored != null)
                {
                    // Keep the existing construction-stage readability without
                    // mutating imported materials or any gameplay state.
                    float stageScale = stage switch
                    {
                        ConstructionStage.Skeleton => 0.55f,
                        ConstructionStage.Assembly => Mathf.Lerp(0.7f, 0.95f, Mathf.InverseLerp(0.25f, 0.75f, progress)),
                        _ => 1f
                    };
                    authored.transform.localScale *= stageScale;

                    // ANI-05.1.1: this module's own mechanisms. The body is
                    // rebuilt on every tier or stage change, so the presenter
                    // goes on the fresh body and the old one dies with the old
                    // body — no duplicate, no stale node.
                    if (Nebula.Visuals.EconomyMechanicalProfiles.HasRealMechanisms(definition.VisualId))
                    {
                        ConstructionStage capturedStage = stage;
                        body.AddComponent<Nebula.Visuals.EconomyMechanicalPresenter>()
                            .Initialize(definition.VisualId, authored.transform,
                                () => ResolveModuleDrive(moduleId, capturedStage), () => 1f);
                    }
                    return;
                }
            }

            float baseSize = 0.6f + (tier - 1) * 0.18f;
            Color color = Color.Lerp(new Color(0.55f, 0.6f, 0.66f), new Color(1f, 0.82f, 0.4f),
                (tier - 1) / (float)(ModuleDef.MaxTier - 1));

            if (stage == ConstructionStage.None)
            {
                BuildCore(body.transform, baseSize, MaterialLibrary.CreateLit(color, 0.4f, 0.5f));
            }
            else if (stage == ConstructionStage.Skeleton)
            {
                // Thin, dim, half-size frame — reads as "just started".
                BuildCore(body.transform, baseSize * 0.5f,
                    MaterialLibrary.CreateAdditive(new Color(color.r, color.g, color.b, 0.25f)));
            }
            else
            {
                // Assembly/Calibration: size grows toward the next tier's footprint.
                float t = stage == ConstructionStage.Calibration ? 1f : Mathf.InverseLerp(0.25f, 0.75f, progress);
                float grownSize = Mathf.Lerp(baseSize, baseSize * 1.18f, t);
                BuildCore(body.transform, grownSize, MaterialLibrary.CreateLit(color, 0.4f, 0.5f));

                if (stage == ConstructionStage.Calibration)
                {
                    GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    glow.name = "Calibration Glow";
                    Destroy(glow.GetComponent<Collider>());
                    glow.transform.SetParent(body.transform, false);
                    glow.transform.localScale = Vector3.one * (grownSize * 2.2f);
                    glow.AddComponent<Billboard>();
                    var glowRenderer = glow.GetComponent<MeshRenderer>();
                    glowRenderer.sharedMaterial = MaterialLibrary.CreateAdditive(
                        new Color(0.4f, 0.9f, 1f, 0.35f), MaterialLibrary.RadialGlowTexture);
                    glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    glow.AddComponent<BeaconPulse>().Configure(0.9f, 0.5f);
                }
            }

            // Tier pips: stacked cubes so tier reads without relying on color alone.
            for (int i = 0; i < tier; i++)
            {
                GameObject pip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pip.name = "Tier Pip " + i;
                Destroy(pip.GetComponent<Collider>());
                pip.transform.SetParent(body.transform, false);
                pip.transform.localPosition = new Vector3(0f, baseSize * 0.5f + 0.08f + i * 0.1f, 0f);
                pip.transform.localScale = Vector3.one * 0.06f;
                pip.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateUnlit(new Color(0.85f, 0.9f, 1f));
            }
        }

        private static void BuildCore(Transform parent, float size, Material material)
        {
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Cube);
            core.name = "Core";
            Destroy(core.GetComponent<Collider>());
            core.transform.SetParent(parent, false);
            core.transform.localScale = Vector3.one * size;
            var coreRenderer = core.GetComponent<MeshRenderer>();
            coreRenderer.sharedMaterial = material;
            coreRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
