using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Who owns which shader property.
    /// <para>
    /// Lighting and animation both write to the same renderers through
    /// <see cref="MaterialPropertyBlock"/>. A block is all-or-nothing: writing
    /// one without reading the current one first silently wipes every value the
    /// other system set. LGT already reads before it writes; this is the
    /// animation side of the same contract, plus the explicit list of which
    /// fields each side is allowed to touch.
    /// </para>
    /// <para>
    /// Nothing here uses <c>renderer.material</c> or makes a runtime copy —
    /// either would leak an instance per renderer and break the shared-variant
    /// cache LGT-01 built.
    /// </para>
    /// </summary>
    public static class MaterialPropertyChannels
    {
        // --- lighting's fields. Animation must never write these. ---
        public static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        public static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
        public static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        public static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // --- animation's fields. Lighting never writes these. ---
        public static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        public static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        /// <summary>The four properties the lighting stack owns.</summary>
        public static int[] LightingOwned => new[] { SmoothnessId, GlossinessId, MetallicId, EmissionColorId };

        /// <summary>The two properties the animation stack owns.</summary>
        public static int[] AnimationOwned => new[] { MainTexStId, BaseMapStId };

        /// <summary>True when a property belongs to lighting and animation must leave it alone.</summary>
        public static bool IsLightingOwned(int propertyId)
            => propertyId == SmoothnessId || propertyId == GlossinessId
               || propertyId == MetallicId || propertyId == EmissionColorId;

        /// <summary>True when a property belongs to animation.</summary>
        public static bool IsAnimationOwned(int propertyId)
            => propertyId == MainTexStId || propertyId == BaseMapStId;

        /// <summary>
        /// Scrolls a surface's texture without disturbing anything lighting set.
        /// <para>
        /// Read-modify-write, exactly as the lighting presenters do: the current
        /// block comes back first, only the two tiling/offset fields change, and
        /// the whole block goes back. A renderer that has never been given a
        /// block simply starts with an empty one, which is correct — the
        /// material's own values still apply to everything untouched.
        /// </para>
        /// </summary>
        public static void ScrollSurface(Renderer renderer, MaterialPropertyBlock scratch, Vector2 offset)
        {
            if (renderer == null || scratch == null) return;

            renderer.GetPropertyBlock(scratch);

            // Tiling stays at one; only the offset moves. Writing tiling would
            // change how the artist's texture reads.
            var st = new Vector4(1f, 1f, offset.x, offset.y);
            scratch.SetVector(MainTexStId, st);
            scratch.SetVector(BaseMapStId, st);

            renderer.SetPropertyBlock(scratch);
        }

        /// <summary>
        /// Puts a surface back to its authored tiling and offset, leaving every
        /// lighting value in place.
        /// </summary>
        public static void ResetSurface(Renderer renderer, MaterialPropertyBlock scratch)
            => ScrollSurface(renderer, scratch, Vector2.zero);
    }
}
