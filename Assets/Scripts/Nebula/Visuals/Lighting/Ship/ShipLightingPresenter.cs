using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Navigation;
using Nebula.Ship;

namespace Nebula.Visuals
{
    /// <summary>
    /// The player ship's one lighting authority. Reads live gameplay state —
    /// throttle, flight mode, hull, emergency shutdown, repair — and turns it
    /// into emission. It owns no state of its own: it never decides whether the
    /// ship is damaged, only how a damaged ship looks.
    /// <para>
    /// The whole ship carries exactly one real-time light, the hull fill from
    /// <see cref="LightingRig.ShipHullFill"/>, and the Low profile drops even
    /// that. Engines, cockpit, status runs, modules and hangar bays are all
    /// emission on the existing meshes — a ship with twelve modules installed
    /// costs the same number of lights as a bare one.
    /// </para>
    /// <para>
    /// Dependencies arrive through composition. Nothing here searches the
    /// scene, and the anchors it reads come from the real prefab hierarchy via
    /// <see cref="PlayerShipVisualController.FindCurrentAnchor"/>, which
    /// returns null rather than throwing when a node is absent.
    /// </para>
    /// </summary>
    public class ShipLightingPresenter : MonoBehaviour
    {
        /// <summary>The real thruster nodes on the player ship prefab.</summary>
        private static readonly string[] EngineAnchors = { "anchor.thruster.L", "anchor.thruster.R" };

        /// <summary>Cockpit/navigation and status runs ride the core mount; both names are tried, and a missing one is simply skipped.</summary>
        private static readonly string[] CoreAnchors = { "anchor.mount.core", "anchor.foundation" };

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private ShipController controller;
        private ShipCruise cruise;
        private ShipCombat combat;
        private PlayerShipVisualController visualController;
        private ShipModuleVisuals moduleVisuals;

        private Light hullFill;
        private LightingQuality quality = LightingQuality.Medium;
        private MaterialPropertyBlock block;

        private Renderer[] engineRenderers = System.Array.Empty<Renderer>();
        private Renderer[] coreRenderers = System.Array.Empty<Renderer>();
        private readonly List<(string anchorId, Renderer[] renderers)> moduleGroups = new List<(string, Renderer[])>();
        private readonly HashSet<string> anchorsUnderConstruction = new HashSet<string>();

        private GameObject cachedVisual;
        private ShipLightingState appliedState = (ShipLightingState)(-1);
        private int appliedThrottleBucket = -1;
        private bool dirty = true;

        public ShipLightingState CurrentState { get; private set; } = ShipLightingState.Idle;

        /// <summary>The ship's single real-time light. Null on the Low profile, where it is dropped entirely.</summary>
        public Light HullFill => hullFill;

        public LightingQuality Quality => quality;

        /// <summary>How many renderer groups this presenter drives. Test-facing; must not grow per frame.</summary>
        public int CachedRendererGroupCount => moduleGroups.Count + 2;

        public void Initialize(ShipController shipController, ShipModuleVisuals visuals, in LightingQualityProfile profile)
        {
            controller = shipController;
            cruise = shipController != null ? shipController.GetComponent<ShipCruise>() : null;
            combat = shipController != null ? shipController.GetComponent<ShipCombat>() : null;
            visualController = shipController != null ? shipController.GetComponent<PlayerShipVisualController>() : null;
            moduleVisuals = visuals;

            block = new MaterialPropertyBlock();
            hullFill = LightingRig.ShipHullFill.Create(transform, "Hull Light");
            hullFill.transform.localPosition = new Vector3(0f, 3f, -1f);

            if (moduleVisuals != null) moduleVisuals.ConstructionStageChanged += OnConstructionStageChanged;

            ApplyProfile(profile);
            RebuildRendererCache();
            Tick();
        }

        public void ApplyProfile(in LightingQualityProfile profile)
        {
            quality = profile.Quality;

            // Low drops the ship's only real-time light: at that tier the hull
            // reads by the star's key light and its own emission alone.
            if (hullFill != null) hullFill.gameObject.SetActive(quality != LightingQuality.Low);
            if (hullFill != null)
                hullFill.intensity = LightingAccessibility.ScaleIntensity(
                    LightingRig.ShipHullFill.Intensity * (quality == LightingQuality.High ? 1.15f : 1f));

            dirty = true;
        }

        private void LateUpdate()
        {
            // The authored visual is rebuilt whenever the ship's module stage
            // changes, which invalidates every anchor we cached.
            if (visualController != null && !ReferenceEquals(visualController.CurrentVisual, cachedVisual))
                RebuildRendererCache();

            Tick();
        }

        /// <summary>One step of state → emission. Public so a test can drive it without waiting on frames.</summary>
        public void Tick()
        {
            ShipLightingState state = ResolveState();
            float throttle = controller != null ? Mathf.Clamp01(controller.GetThrottleFraction()) : 0f;

            // Quantised so a hand on the throttle does not rewrite every
            // property block sixty times a second for changes no one can see.
            int throttleBucket = Mathf.RoundToInt(throttle * 20f);

            CurrentState = state;
            if (!dirty && state == appliedState && throttleBucket == appliedThrottleBucket) return;

            appliedState = state;
            appliedThrottleBucket = throttleBucket;
            dirty = false;

            (Color engineColor, float engineStrength) = ShipLightingProfiles.Engine(state, throttle);
            bool boostOrWarning = state == ShipLightingState.Boost
                                  || state == ShipLightingState.Damaged
                                  || state == ShipLightingState.Disabled;
            WriteEmission(engineRenderers, ShipLightingProfiles.Resolve(engineColor, engineStrength, boostOrWarning));

            (Color coreColor, float coreStrength) = ShipLightingProfiles.SystemStatus(state);
            WriteEmission(coreRenderers, ShipLightingProfiles.Resolve(coreColor, coreStrength, boostOrWarning));

            for (int i = 0; i < moduleGroups.Count; i++)
            {
                (string anchorId, Renderer[] renderers) = moduleGroups[i];
                ModuleLightingState moduleState = ResolveModuleState(anchorId, state);
                (Color color, float strength) = ShipLightingProfiles.Module(
                    moduleState, ShipLightingProfiles.ModuleFunctionColor(anchorId), tier: 1);
                WriteEmission(renderers, ShipLightingProfiles.Resolve(color, strength,
                    moduleState == ModuleLightingState.Disabled));
            }
        }

        /// <summary>Maps live gameplay onto a lighting state. Reads only; changes nothing.</summary>
        private ShipLightingState ResolveState()
        {
            if (combat != null && combat.State != null)
            {
                if (combat.State.EmergencyShutdown) return ShipLightingState.Disabled;
                if (combat.Repair != null && combat.Repair.Active) return ShipLightingState.Repair;

                Durability durability = combat.State.Durability;
                if (durability != null && durability.HullMax > 0f && durability.Hull / durability.HullMax < 0.5f)
                    return ShipLightingState.Damaged;
            }

            if (cruise != null && (cruise.Mode == FlightMode.DeepSpaceCruise || cruise.Mode == FlightMode.CruiseRamp))
                return ShipLightingState.Cruise;

            if (controller != null)
            {
                if (controller.CombatBoostSpeedMultiplier > 1.001f) return ShipLightingState.Boost;
                if (controller.GetThrottleFraction() > 0.02f) return ShipLightingState.Throttle;
            }

            return ShipLightingState.Idle;
        }

        private ModuleLightingState ResolveModuleState(string anchorId, ShipLightingState shipState)
        {
            if (shipState == ShipLightingState.Disabled) return ModuleLightingState.Disabled;
            if (anchorsUnderConstruction.Contains(anchorId)) return ModuleLightingState.Upgrading;
            return ModuleLightingState.Installed; // the anchor only exists once the module does
        }

        private void OnConstructionStageChanged(ModuleVisualHookArgs args)
        {
            if (string.IsNullOrEmpty(args.AnchorId)) return;

            if (args.Stage == ConstructionStage.None) anchorsUnderConstruction.Remove(args.AnchorId);
            else anchorsUnderConstruction.Add(args.AnchorId);

            dirty = true;
        }

        /// <summary>
        /// Collects the renderers under each real anchor, once. A missing
        /// anchor yields an empty group rather than an exception — the prefab
        /// is the source of truth about which nodes exist, and it changes as
        /// modules are installed.
        /// </summary>
        private void RebuildRendererCache()
        {
            cachedVisual = visualController != null ? visualController.CurrentVisual : null;
            moduleGroups.Clear();

            engineRenderers = CollectUnder(EngineAnchors);
            coreRenderers = CollectUnder(CoreAnchors);

            if (visualController != null)
            {
                foreach (string anchorId in PlayerShipVisualController.LogicalModuleAnchors)
                {
                    Renderer[] renderers = CollectUnder(anchorId);
                    if (renderers.Length > 0) moduleGroups.Add((anchorId, renderers));
                }
            }

            // Anything that will glow needs a material that can show emission.
            EnsureEmissive(engineRenderers);
            EnsureEmissive(coreRenderers);
            for (int i = 0; i < moduleGroups.Count; i++) EnsureEmissive(moduleGroups[i].renderers);

            dirty = true;
        }

        private Renderer[] CollectUnder(params string[] anchorNames)
        {
            if (visualController == null) return System.Array.Empty<Renderer>();

            var collected = new List<Renderer>();
            foreach (string anchorName in anchorNames)
            {
                Transform anchor = visualController.FindCurrentAnchor(anchorName);
                if (anchor == null) continue; // safe fallback: this ship simply has no such node

                // Inactive children included: a module panel that is switched
                // off still has to be found so it can be lit when it comes back.
                foreach (Renderer renderer in anchor.GetComponentsInChildren<Renderer>(true))
                    if (renderer != null && !collected.Contains(renderer)) collected.Add(renderer);
            }

            return collected.ToArray();
        }

        private static void EnsureEmissive(Renderer[] renderers)
        {
            for (int i = 0; i < renderers.Length; i++) MaterialVariantCache.EnsureEmissiveCapable(renderers[i]);
        }

        private void WriteEmission(Renderer[] renderers, Color emission)
        {
            if (renderers == null || block == null) return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;

                renderer.GetPropertyBlock(block);
                block.SetColor(EmissionColorId, emission);
                renderer.SetPropertyBlock(block);
            }
        }

        private void OnDestroy()
        {
            if (moduleVisuals != null) moduleVisuals.ConstructionStageChanged -= OnConstructionStageChanged;
            moduleGroups.Clear();
            anchorsUnderConstruction.Clear();
        }
    }
}
