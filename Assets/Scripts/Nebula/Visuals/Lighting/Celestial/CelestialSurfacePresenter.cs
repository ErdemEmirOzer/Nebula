using System.Collections.Generic;
using UnityEngine;
using Nebula.Sectors;

namespace Nebula.Visuals
{
    /// <summary>
    /// Applies one celestial body's lighting response to its renderers through
    /// a <see cref="MaterialPropertyBlock"/> — never <c>renderer.material</c>,
    /// which would clone the shared material once per planet and leak an
    /// instance per body per session.
    /// <para>
    /// Applies on materialization, on a class change, and on a quality change.
    /// Never per frame: there is no Update here, and nothing sweeps the
    /// celestial bodies looking for work.
    /// </para>
    /// <para>
    /// The static registry below is a list of presenters to push to, not a
    /// second lighting authority — <see cref="NebulaLightingDirector"/> is
    /// still the only thing that decides what the quality is, and it is what
    /// calls <see cref="ApplyQualityToAll"/>.
    /// </para>
    /// </summary>
    public class CelestialSurfacePresenter : MonoBehaviour
    {
        private static readonly List<CelestialSurfacePresenter> Live = new List<CelestialSurfacePresenter>();

        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock block;
        private Renderer[] renderers;
        private PlanetArchetype archetype;
        private EnvironmentClass environment;
        private LightingQuality quality = LightingQuality.Medium;
        private bool configured;

        public PlanetArchetype Archetype => archetype;
        public EnvironmentClass Environment => environment;
        public LightingQuality Quality => quality;

        /// <summary>The response currently written to the renderers. Exposed so a test can assert what was applied without reading back from the GPU.</summary>
        public CelestialSurfaceResponse AppliedResponse { get; private set; }

        /// <summary>How many presenters are live this session. Test-facing.</summary>
        public static int LiveCount => Live.Count;

        /// <summary>
        /// Binds the body to its family and environment and writes the response
        /// once. Called at materialization.
        /// </summary>
        public void Configure(PlanetArchetype planetArchetype, EnvironmentClass environmentClass, LightingQuality startingQuality)
        {
            archetype = planetArchetype;
            environment = environmentClass;
            quality = startingQuality;
            configured = true;

            // Collected once: a planet's renderer set does not change after it
            // is built, and re-querying per apply would allocate on every
            // quality switch.
            renderers = GetComponentsInChildren<Renderer>(true);
            block ??= new MaterialPropertyBlock();

            // LGT-03 §1: a property block cannot enable _EMISSION, so a family
            // that actually glows (Diamond, and any world under an Extreme
            // hazard rim) needs its material swapped for the shared emissive
            // variant first. Families with black emission are left alone —
            // no variant is created for a material that will never glow.
            if (CelestialLightingResolver.Resolve(archetype, environment, quality).Emission.maxColorComponent > 0f)
                for (int i = 0; i < renderers.Length; i++)
                    MaterialVariantCache.EnsureEmissiveCapable(renderers[i]);

            Apply();
        }

        /// <summary>Re-resolves for a new quality tier. A no-op if the tier has not actually changed.</summary>
        public void SetQuality(LightingQuality newQuality)
        {
            if (!configured || quality == newQuality) return;
            quality = newQuality;
            Apply();
        }

        /// <summary>Pushes a quality change to every live celestial surface. Called by the lighting director, and only by it.</summary>
        public static void ApplyQualityToAll(LightingQuality newQuality)
        {
            for (int i = 0; i < Live.Count; i++)
                if (Live[i] != null) Live[i].SetQuality(newQuality);
        }

        private void Apply()
        {
            if (renderers == null || block == null) return;

            CelestialSurfaceResponse response = CelestialLightingResolver.Resolve(archetype, environment, quality);
            AppliedResponse = response;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;

                // Read-modify-write so this never clobbers property-block values
                // another system already set on the same renderer.
                renderer.GetPropertyBlock(block);
                // Both names on purpose: Standard calls it _Glossiness, the URP
                // Lit fallback calls it _Smoothness, and MaterialLibrary already
                // sets whichever exists.
                block.SetFloat(SmoothnessId, response.Smoothness);
                block.SetFloat(GlossinessId, response.Smoothness);
                block.SetFloat(MetallicId, response.Metallic);
                block.SetColor(EmissionColorId, response.Emission);
                renderer.SetPropertyBlock(block);
            }
        }

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);
    }
}
