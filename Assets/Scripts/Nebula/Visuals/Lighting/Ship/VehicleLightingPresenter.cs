using UnityEngine;
using Nebula.Hangar;

namespace Nebula.Visuals
{
    /// <summary>
    /// Lights one support vehicle entirely through emission. A Scout, a
    /// Collector, an Engineer and an Escort are told apart by colour and by how
    /// much of their hull is lit — never by a light of their own. Twenty
    /// launched vehicles cost the scene zero extra real-time lights; they are
    /// all lit by the same global key light as everything else.
    /// <para>
    /// Reads <see cref="VehicleAgent"/> state and changes none of it: missions,
    /// speeds, durability, transfers and save behaviour are untouched.
    /// </para>
    /// </summary>
    public class VehicleLightingPresenter : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private VehicleAgent agent;
        private MaterialPropertyBlock block;
        private Renderer[] renderers = System.Array.Empty<Renderer>();
        private VehicleLightingState appliedState = (VehicleLightingState)(-1);
        private bool appliedAccessibilityDirty = true;

        public VehicleLightingState CurrentState { get; private set; } = VehicleLightingState.Docked;
        public VehicleClass VehicleClass => agent != null && agent.Def != null ? agent.Def.Class : VehicleClass.Scout;
        public int Mk => agent != null && agent.Def != null ? agent.Def.Mk : 1;

        /// <summary>The emission last written. Test-facing, so a fixture can assert colour without reading the GPU.</summary>
        public Color AppliedEmission { get; private set; }

        public void Initialize(VehicleAgent vehicleAgent)
        {
            agent = vehicleAgent;
            block = new MaterialPropertyBlock();
            RebuildRendererCache();
            Tick();
        }

        /// <summary>Re-collects renderers after the vehicle's visuals are rebuilt. Cheap, and never called per frame.</summary>
        public void RebuildRendererCache()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) MaterialVariantCache.EnsureEmissiveCapable(renderers[i]);
            appliedAccessibilityDirty = true;
        }

        private void LateUpdate() => Tick();

        /// <summary>One step of state → emission. Writes only when the state actually changed.</summary>
        public void Tick()
        {
            VehicleLightingState state = ResolveState();
            CurrentState = state;

            if (!appliedAccessibilityDirty && state == appliedState) return;
            appliedState = state;
            appliedAccessibilityDirty = false;

            (Color color, float strength) = ShipLightingProfiles.Vehicle(VehicleClass, Mk, state);
            AppliedEmission = ShipLightingProfiles.Resolve(color, strength, state == VehicleLightingState.Disabled);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;

                renderer.GetPropertyBlock(block);
                block.SetColor(EmissionColorId, AppliedEmission);
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Maps the vehicle's real mission phase and durability onto a lighting state.</summary>
        private VehicleLightingState ResolveState()
        {
            if (agent == null) return VehicleLightingState.Docked;

            if (agent.DurabilityRef != null && agent.DurabilityRef.Disabled) return VehicleLightingState.Disabled;

            switch (agent.Mission != null ? agent.Mission.Phase : VehiclePhase.InHangar)
            {
                case VehiclePhase.InHangar: return VehicleLightingState.Docked;
                case VehiclePhase.Launching: return VehicleLightingState.Launched;
                case VehiclePhase.TravelOut:
                case VehiclePhase.OnStation:
                case VehiclePhase.Waiting: return VehicleLightingState.MissionActive;
                case VehiclePhase.TravelReturn:
                case VehiclePhase.Docking: return VehicleLightingState.Returning;
                default: return VehicleLightingState.Docked;
            }
        }
    }
}
