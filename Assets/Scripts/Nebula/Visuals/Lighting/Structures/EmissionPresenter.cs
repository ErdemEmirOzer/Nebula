using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Shared machinery for every visual-only presenter that lights an object
    /// through emission: renderers collected once, one property block reused
    /// forever, emissive material variants shared through
    /// <see cref="MaterialVariantCache"/>, and a write that only happens when
    /// the resolved colour actually changed.
    /// <para>
    /// Subclasses answer one question — what colour should this be right now —
    /// and never touch renderers, materials or lights themselves. None of them
    /// may create a <see cref="Light"/>.
    /// </para>
    /// </summary>
    public abstract class EmissionPresenter : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock block;
        private Renderer[] renderers = System.Array.Empty<Renderer>();
        private Color applied = new Color(-1f, -1f, -1f, -1f);
        private bool cached;

        /// <summary>The quality tier this presenter resolves against. Pushed by the lighting director, never polled.</summary>
        public LightingQuality Quality { get; private set; } = LightingQuality.Medium;

        /// <summary>The emission last written. Test-facing.</summary>
        public Color AppliedEmission { get; private set; }

        /// <summary>How many renderers this presenter drives. Must not grow frame to frame.</summary>
        public int CachedRendererCount => renderers.Length;

        /// <summary>What this object should be emitting right now, already resolved for quality and accessibility.</summary>
        protected abstract Color ResolveEmission();

        /// <summary>Collects renderers and prepares their materials to show emission. Safe to call again after the visual is rebuilt.</summary>
        public void RebuildRendererCache()
        {
            block ??= new MaterialPropertyBlock();
            renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) MaterialVariantCache.EnsureEmissiveCapable(renderers[i]);
            cached = true;
            applied = new Color(-1f, -1f, -1f, -1f); // force the next Apply through
        }

        /// <summary>Re-resolves for a new quality tier. A no-op when the tier has not changed.</summary>
        public void SetQuality(LightingQuality quality)
        {
            if (Quality == quality) return;
            Quality = quality;
            Apply();
        }

        /// <summary>
        /// Writes the current emission if — and only if — it differs from what
        /// is already on the renderers. Callers may invoke this as often as
        /// they like; a structure whose state has not moved costs nothing.
        /// </summary>
        public void Apply()
        {
            if (!cached) RebuildRendererCache();

            Color emission = ResolveEmission();
            if (emission == applied) return;
            applied = emission;
            AppliedEmission = emission;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;

                renderer.GetPropertyBlock(block);
                block.SetColor(EmissionColorId, emission);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
