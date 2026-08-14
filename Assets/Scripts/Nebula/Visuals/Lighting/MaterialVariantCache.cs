using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Hands out emission-capable copies of shared materials, one per source
    /// material for the life of a session.
    /// <para>
    /// This exists because of a specific Built-in pipeline rule: a
    /// <see cref="MaterialPropertyBlock"/> can write <c>_EmissionColor</c>, but
    /// it cannot enable a shader keyword. On a Standard material with
    /// <c>_EMISSION</c> off, the value lands and renders as nothing. LGT-02
    /// shipped with that gap open.
    /// </para>
    /// <para>
    /// The fix is deliberately not "enable the keyword on the shared
    /// material": those materials are imported assets shared by every body
    /// using them, and mutating one would change objects nobody asked to
    /// change and dirty the asset. Instead each source material gets at most
    /// one runtime copy — same shader, same textures, same render queue, same
    /// property values, plus the keyword — and every renderer that needs
    /// emission points at that one copy. Material count therefore grows with
    /// the number of distinct source materials that actually need emission,
    /// never with the number of renderers or frames.
    /// </para>
    /// <para>
    /// "Distinct source materials" is the load-bearing phrase, and it is what
    /// made this class the only unbounded Material allocator left in the
    /// project. A promoted system's planets are given a <b>freshly created</b>
    /// source material every promotion (<c>SystemMaterializer.ArchetypeMaterial</c>
    /// → <c>MaterialLibrary.CreateLit</c>, SystemMaterializer.cs:204/336), so
    /// every promotion presents new instance IDs, gets new variants, and — while
    /// the variants were owned by nothing but a static list cleared only at a
    /// session boundary — kept them. Measured on the single-cycle counter
    /// regressions: <c>Material(all-loaded)</c> +10 per promote/demote round trip
    /// and +4 per save/load, with Texture and Mesh dead flat, because a
    /// <c>new Material(source)</c> copy shares the source's textures and
    /// allocates neither.
    /// </para>
    /// </summary>
    public static class MaterialVariantCache
    {
        private const string EmissionKeyword = "_EMISSION";
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static readonly Dictionary<int, Material> EmissiveVariants = new Dictionary<int, Material>();
        private static readonly List<Material> Created = new List<Material>();
        private static readonly List<int> DeadKeys = new List<int>();

        /// <summary>Runtime copies alive right now. Test-facing, and the number that must not grow per renderer.</summary>
        public static int VariantCount
        {
            get
            {
                int live = 0;
                for (int i = 0; i < Created.Count; i++) if (Created[i] != null) live++;
                return live;
            }
        }

        /// <summary>True when the material can already show emission and needs no copy at all.</summary>
        public static bool AlreadyEmissive(Material material)
            => material != null
               && material.HasProperty(EmissionColorId)
               && material.IsKeywordEnabled(EmissionKeyword);

        /// <summary>
        /// The material a renderer should draw with in order for a property
        /// block's <c>_EmissionColor</c> to be visible.
        /// <list type="bullet">
        /// <item>Already emissive, or no emission property at all: the source
        /// is returned untouched and nothing is allocated.</item>
        /// <item>Otherwise: the one cached copy for that source, created on
        /// first ask and reused forever after.</item>
        /// </list>
        /// The source material is never modified either way.
        /// </summary>
        public static Material GetEmissiveVariant(Material source)
        {
            if (source == null) return null;

            // No emission slot on this shader — a variant would buy nothing.
            if (!source.HasProperty(EmissionColorId)) return source;

            // Already able to show emission: no copy needed.
            if (source.IsKeywordEnabled(EmissionKeyword)) return source;

            int key = source.GetInstanceID();
            if (EmissiveVariants.TryGetValue(key, out Material cached) && cached != null) return cached;

            // Copy-construction carries the shader, every texture, every
            // property value and the render queue across unchanged.
            var variant = new Material(source) { name = source.name + " (Emissive Variant)" };
            variant.EnableKeyword(EmissionKeyword);
            variant.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            // Start black: the property block decides what actually glows, so
            // adopting the variant alone never changes how anything looks.
            variant.SetColor(EmissionColorId, Color.black);
            variant.enableInstancing = source.enableInstancing;

            // A variant is a COPY of its source, so it has exactly the source's
            // lifetime and belongs in exactly the source's owner. The scope active
            // at this moment IS that owner in every call path: a planet surface asks
            // from inside the promotion's FullResources block that just created its
            // source (SystemMaterializer.cs:171/204), and a ship/structure presenter
            // asks with no nested scope active, so Own falls through to the session —
            // which is where an asset-lifetime source's variant belongs.
            //
            // This deliberately reverses the earlier decision to leave variants
            // unowned "because a demotion would destroy a variant a session-lifetime
            // renderer still holds". That case cannot arise: a renderer outliving the
            // materialized system does not draw with a material created inside it. The
            // case that DID arise is the opposite one, and it was measured — see the
            // class comment. Clear() stays as the session-boundary sweep for the
            // asset-sourced variants; it is now a second line, not the only one.
            RuntimeResourceScope.Own(variant);

            DropDeadEntries();
            EmissiveVariants[key] = variant;
            Created.Add(variant);
            return variant;
        }

        /// <summary>
        /// Forgets every entry whose variant has been destroyed by its owning scope.
        /// <para>
        /// Not cosmetic. Unity REUSES instance IDs, and the dictionary is keyed by
        /// the source's, so a dead key left standing is a collision waiting for the
        /// next material to be allocated at that ID — which would hand a renderer a
        /// variant of some other material entirely. The <c>cached != null</c> guard
        /// in <see cref="GetEmissiveVariant"/> catches the destroyed-value half of
        /// that; this catches the rest, and keeps the two managed collections from
        /// growing across a long session even though the Materials no longer do.
        /// </para>
        /// </summary>
        private static void DropDeadEntries()
        {
            for (int i = Created.Count - 1; i >= 0; i--)
                if (Created[i] == null) Created.RemoveAt(i);

            DeadKeys.Clear();
            foreach (KeyValuePair<int, Material> entry in EmissiveVariants)
                if (entry.Value == null) DeadKeys.Add(entry.Key);
            for (int i = 0; i < DeadKeys.Count; i++) EmissiveVariants.Remove(DeadKeys[i]);
            DeadKeys.Clear();
        }

        /// <summary>
        /// Points a renderer at the emissive variant of whatever it currently
        /// draws with. Reads <see cref="Renderer.sharedMaterial"/> and writes
        /// it back — never <c>renderer.material</c>, which would clone per
        /// renderer and defeat the whole point of this class.
        /// </summary>
        public static void EnsureEmissiveCapable(Renderer renderer)
        {
            if (renderer == null) return;

            Material source = renderer.sharedMaterial;
            Material variant = GetEmissiveVariant(source);
            if (variant != null && !ReferenceEquals(variant, source)) renderer.sharedMaterial = variant;
        }

        /// <summary>
        /// Destroys every runtime copy still standing and empties the lookup.
        /// Called on session shutdown/restart so variants never accumulate across
        /// campaigns — the cache is session-lifetime, not application-lifetime.
        /// <para>
        /// Since every variant now also has a <see cref="RuntimeResourceScope"/>
        /// owner, most of these are already destroyed by the time this runs and the
        /// loop skips them. It stays because it is the only thing that empties the
        /// LOOKUP, and because a variant whose owner is the session scope is
        /// released by that scope in the same <c>Dispose</c> — one of the two has to
        /// run first and neither may assume it was the other.
        /// </para>
        /// </summary>
        public static void Clear()
        {
            for (int i = 0; i < Created.Count; i++)
            {
                Material variant = Created[i];
                if (variant == null) continue;
                if (Application.isPlaying) Object.Destroy(variant);
                else Object.DestroyImmediate(variant);
            }

            Created.Clear();
            EmissiveVariants.Clear();
        }
    }
}
