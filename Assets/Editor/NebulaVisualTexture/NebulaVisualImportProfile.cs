using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Nebula.EditorTools.VisualTexture
{
    /// <summary>
    /// Import policy for the textures extracted out of the delivered GLBs.
    /// <para>
    /// It exists because glTFast does not give one. Its importer builds every
    /// embedded image as a <c>Texture2D</c> at <c>R8G8B8A8_SRGB</c> or
    /// <c>R8G8B8A8_UNorm</c> with a mip chain — measured in
    /// <c>GltfImport.CreateEmptyTexture</c>, and confirmed by the numbers: 929
    /// embedded images at four bytes per texel plus mips is 2.97 GB in the player
    /// for 389 MB of source. There is no exposed knob for it, and the importer has
    /// no external-object remap, so the only way to get a compressed texture out of
    /// these files is to take the image out and import it as an image.
    /// </para>
    /// <para>
    /// This is an <c>AssetPostprocessor</c> rather than a one-shot pass for the
    /// same reason <c>NebulaBrandAssetImportSettings</c> is: settings applied once
    /// by a menu command revert the next time anything triggers a re-import, and
    /// nobody notices until a build is 3 GB again. Every rule below is a function
    /// of the asset path alone — the role folder and the <c>_a</c> suffix that
    /// <see cref="NebulaVisualTextureManifest.AssetId"/> writes — so it cannot
    /// depend on the manifest having been imported first.
    /// </para>
    /// </summary>
    public sealed class NebulaVisualImportProfile : AssetPostprocessor
    {
        /// <summary>Platform string for the desktop players this project builds.</summary>
        public const string Standalone = "Standalone";

        /// <summary>
        /// No downscaling, ever. The cap is set far above the largest delivered
        /// image — 2048 — so that a texture can never be silently resampled.
        /// <para>
        /// Setting it to the largest observed size instead would look tidier and be
        /// a trap: the next delivery with a 4096 plate would import at half
        /// resolution and the only symptom would be a slightly soft hull.
        /// </para>
        /// </summary>
        public const int NoDownscaleCap = 8192;

        /// <summary>
        /// Bumped when the rules below change.
        /// <para>
        /// Unity folds the importer version into an asset's import result, so
        /// without this a policy edit only reaches assets that happen to be
        /// re-imported for some other reason — which is how half a texture set ends
        /// up on the previous format with nothing to show for it.
        /// </para>
        /// <para>1: first desktop profile — BC7 colour and data, BC5 normals.</para>
        /// <para>2: two normals moved off BC5 after the quality gate measured them
        /// over threshold. See <see cref="LosslessNormals"/>.</para>
        /// </summary>
        public override uint GetVersion() => 2;

        /// <summary>
        /// Normal maps that are imported uncompressed because BC5 measurably fails
        /// them.
        /// <para>
        /// The quality gate holds normals to a mean angular error of two degrees.
        /// These two came back at 3.543 and 3.899 degrees — both 512x256 maps whose
        /// detail sits at the block scale BC5 has no room for. The alternative to
        /// naming them was raising the threshold to 4 degrees, which would have
        /// passed them by redefining the test rather than fixing anything, and
        /// taken every other normal's guarantee down with it.
        /// </para>
        /// <para>
        /// The cost of the exception is 0.75 MB across both, including mips. That
        /// is not a number worth trading a visible seam for.
        /// </para>
        /// </summary>
        public static readonly HashSet<string> LosslessNormals = new HashSet<string>(StringComparer.Ordinal)
        {
            "normal_512x256_2888b776cb18",
            "normal_512x256_e6ee84d4ccc6"
        };

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(NebulaVisualTextureManifest.TextureRoot, StringComparison.Ordinal))
                return;

            Apply((TextureImporter)assetImporter, assetPath);
        }

        /// <summary>
        /// Applies the whole policy for one extracted texture.
        /// <para>
        /// Public and static so the tests can drive it against a synthetic asset
        /// path without an import cycle, and so the plan report can state the exact
        /// settings a texture is going to get before anything is written.
        /// </para>
        /// </summary>
        public static void Apply(TextureImporter importer, string assetPath)
        {
            if (!TryParse(assetPath, out List<NebulaTextureRole> roles, out bool alphaRequired)) return;

            NebulaTextureColorSpace space = ColorSpaceOf(roles);
            bool isNormal = roles.Count == 1 && roles[0] == NebulaTextureRole.Normal;

            // Shared across every extracted texture.
            //
            // Mipmaps on because the source GLBs were imported with
            // generateMipMaps: 1 and these assets stand in for those textures
            // exactly; turning them off would change how every distant hull
            // shimmers, which is a visual change disguised as an optimisation.
            //
            // Read/Write off because nothing reads these on the CPU. Leaving it on
            // keeps a second copy of every texture in system memory for the whole
            // session, which would give back a large part of what compression wins.
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.streamingMipmaps = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Repeat;   // the glTF sampler default
            importer.filterMode = FilterMode.Bilinear;    // glTFast: min/mag 9729 (LINEAR)
            importer.anisoLevel = 1;                      // glTFast: anisotropicFilterLevel 1

            if (isNormal) ApplyNormal(importer, System.IO.Path.GetFileNameWithoutExtension(assetPath));
            else ApplyColorOrData(importer, space, alphaRequired);
        }

        /// <summary>
        /// Tangent-space normals.
        /// <para>
        /// BC5, and the reason is worth writing down because "a normal map lost its
        /// blue channel" reads like a bug. The Built-in shader these materials use
        /// is glTFast's <c>glTF/PbrMetallicRoughness</c>, which samples through
        /// <c>UnpackScaleNormal</c>. On desktop that resolves to
        /// <c>UnpackScaleNormalRGorAG</c>'s third branch: it takes X and Y and
        /// computes <c>z = sqrt(1 - dot(xy, xy))</c>. The blue channel is already
        /// discarded by the shader today, on the uncompressed RGBA32 texture
        /// glTFast produces. BC5 stores X and Y at eight bits per pixel between two
        /// channels instead of spreading the same budget over four, so the two
        /// channels that are actually read come out more accurate than they are
        /// now, not less.
        /// </para>
        /// <para>
        /// <c>normalTexture.scale</c> is a material property, not a texture
        /// property, so it is carried on the generated material and is untouched
        /// here. Nothing in this method changes normal strength.
        /// </para>
        /// </summary>
        private static void ApplyNormal(TextureImporter importer, string assetName)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.convertToNormalmap = false;   // the source already is one; never re-derive from grayscale
            importer.sRGBTexture = false;          // implied by NormalMap, stated so a diff shows it
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;

            bool lossless = LosslessNormals.Contains(assetName);
            SetPlatform(
                importer,
                lossless ? TextureImporterFormat.RGBA32 : TextureImporterFormat.BC5,
                lossless ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ);
        }

        /// <summary>
        /// Everything that is not a normal map: authored colour in sRGB, packed
        /// data in linear.
        /// <para>
        /// BC7 for both. It is the only desktop block format that holds a packed
        /// occlusion/roughness/metallic map without visible banding in the green
        /// channel, and both build targets this project produces — DX11 on Windows
        /// and Metal on macOS — support it natively, so there is no fallback path
        /// that would silently expand it back to RGBA32.
        /// </para>
        /// <para>
        /// Alpha is kept only where a material actually asks for it. Of the 93
        /// alpha-bearing occurrences, 72 are sampled exclusively by OPAQUE
        /// materials, whose shader path ignores alpha entirely. Telling the encoder
        /// the channel is constant is not a size saving at BC7's fixed eight bits
        /// per pixel — it is a quality one, because the block modes that spend bits
        /// on a channel nobody reads stop being chosen.
        /// </para>
        /// </summary>
        private static void ApplyColorOrData(
            TextureImporter importer, NebulaTextureColorSpace space, bool alphaRequired)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = space == NebulaTextureColorSpace.Srgb;
            importer.alphaSource = alphaRequired
                ? TextureImporterAlphaSource.FromInput
                : TextureImporterAlphaSource.None;

            // Only meaningful for authored colour. On a packed data map it would
            // bleed colour under low-alpha texels and corrupt the packing, so it
            // stays off for anything linear even when alpha survives.
            importer.alphaIsTransparency =
                alphaRequired && space == NebulaTextureColorSpace.Srgb;

            SetPlatform(importer, TextureImporterFormat.BC7);
        }

        /// <summary>
        /// Writes the desktop platform override.
        /// <para>
        /// Explicit rather than <c>Automatic</c>: the automatic choice depends on
        /// the active build target at import time, so a project that last built for
        /// macOS and a project that last built for Windows would produce different
        /// bytes from the same source. Naming the format makes the result the same
        /// on both.
        /// </para>
        /// <para>
        /// Crunch is off and stays off. It is a lossy pass on top of the block
        /// format that trades noticeable quality for a smaller file on disk while
        /// costing exactly the same GPU memory at runtime, and it decompresses on
        /// the CPU at load — which is the thing this whole pipeline is removing.
        /// </para>
        /// </summary>
        private static void SetPlatform(
            TextureImporter importer,
            TextureImporterFormat format,
            TextureImporterCompression compression = TextureImporterCompression.CompressedHQ)
        {
            importer.crunchedCompression = false;
            importer.textureCompression = compression;
            importer.maxTextureSize = NoDownscaleCap;

            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(Standalone);
            settings.overridden = true;
            settings.name = Standalone;
            settings.maxTextureSize = NoDownscaleCap;
            settings.format = format;
            settings.textureCompression = compression;
            settings.crunchedCompression = false;
            settings.compressionQuality = 100;
            settings.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            importer.SetPlatformTextureSettings(settings);
        }

        // ---- path is policy --------------------------------------------------

        /// <summary>
        /// Recovers the role set and the alpha requirement from an extracted
        /// texture's path. The inverse of
        /// <see cref="NebulaVisualTextureManifest.AssetId"/>'s naming.
        /// </summary>
        /// <returns>False when the path is not a recognised extracted texture.</returns>
        public static bool TryParse(
            string assetPath, out List<NebulaTextureRole> roles, out bool alphaRequired)
        {
            roles = new List<NebulaTextureRole>();
            alphaRequired = false;

            if (!assetPath.StartsWith(NebulaVisualTextureManifest.TextureRoot, StringComparison.Ordinal))
                return false;

            string tail = assetPath.Substring(NebulaVisualTextureManifest.TextureRoot.Length);
            int slash = tail.IndexOf('/');
            if (slash <= 0) return false;

            foreach (string part in tail.Substring(0, slash).Split('-'))
            {
                if (!Enum.TryParse(part, false, out NebulaTextureRole role)) return false;
                roles.Add(role);
            }
            if (roles.Count == 0) return false;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(tail);
            alphaRequired = fileName.EndsWith("_a", StringComparison.Ordinal);
            return true;
        }

        /// <summary>
        /// The colour space a role set demands.
        /// <para>
        /// A set that mixes an sRGB role with a linear one has no correct answer,
        /// and the pipeline never produces one — it splits such an image into two
        /// assets, one per space, before naming either. Reaching this line with a
        /// mixed set means the split was skipped, so it throws rather than picking
        /// a space and producing a texture that is wrong half the time.
        /// </para>
        /// </summary>
        public static NebulaTextureColorSpace ColorSpaceOf(IList<NebulaTextureRole> roles)
        {
            NebulaTextureColorSpace space = NebulaVisualTextureManifest.ColorSpaceOf(roles[0]);
            for (int i = 1; i < roles.Count; i++)
                if (NebulaVisualTextureManifest.ColorSpaceOf(roles[i]) != space)
                    throw new InvalidOperationException(
                        "Role set mixes colour spaces and should have been split: "
                        + NebulaVisualTextureManifest.RoleSignature(roles));
            return space;
        }

        /// <summary>The plan a path will be imported under, for the report and the manifest.</summary>
        public static NebulaTextureImportPlan PlanFor(string assetPath)
        {
            if (!TryParse(assetPath, out List<NebulaTextureRole> roles, out bool alphaRequired))
                return default;

            bool isNormal = roles.Count == 1 && roles[0] == NebulaTextureRole.Normal;
            NebulaTextureColorSpace space = ColorSpaceOf(roles);
            bool lossless = isNormal
                && LosslessNormals.Contains(System.IO.Path.GetFileNameWithoutExtension(assetPath));

            return new NebulaTextureImportPlan
            {
                TextureType = isNormal ? "NormalMap" : "Default",
                SrgbTexture = !isNormal && space == NebulaTextureColorSpace.Srgb,
                Format = lossless ? "RGBA32" : isNormal ? "BC5" : "BC7",
                Compression = lossless ? "Uncompressed" : "CompressedHQ",
                MipmapEnabled = true,
                Readable = false,
                CrunchedCompression = false,
                MaxTextureSize = NoDownscaleCap,
                StreamingMipmaps = false,
                AlphaSource = !isNormal && alphaRequired ? "FromInput" : "None"
            };
        }
    }
}
