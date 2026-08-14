using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nebula.EditorTools.VisualTexture
{
    /// <summary>Whether a texture's values are gamma-encoded colour or plain data.</summary>
    public enum NebulaTextureColorSpace
    {
        /// <summary>Authored colour. Imported with sRGB on.</summary>
        Srgb = 0,

        /// <summary>Data — normals, roughness, metallic, occlusion. Imported with sRGB off.</summary>
        Linear = 1
    }

    /// <summary>
    /// The composite identity of one texture asset.
    /// <para>
    /// Never the file name. The delivered GLBs call the same 1024x1024 albedo
    /// <c>image_0</c> in ninety different files and call ninety different images
    /// <c>image_0</c> as well, so a name-keyed merge would both miss real
    /// duplicates and fuse unrelated ones.
    /// </para>
    /// <para>
    /// Every component is here because dropping it would merge two textures that
    /// must not become one asset:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Decoded pixel hash</b> — the actual image. Source bytes
    /// are not enough: four of the delivered images are re-encodes of another,
    /// identical pixel for pixel but different byte for byte, and keying on bytes
    /// would ship both.</description></item>
    /// <item><description><b>Width and height</b> — cheap, and it makes a pixel-hash
    /// collision harmless instead of catastrophic.</description></item>
    /// <item><description><b>Colour space</b> — an sRGB albedo and a linear roughness
    /// map with identical pixels are different assets. Merging them silently
    /// applies a gamma curve to material data, which reads as a subtly wrong
    /// surface rather than an obvious error, and is the single worst failure this
    /// key exists to prevent.</description></item>
    /// <item><description><b>Role signature</b> — the sorted set of glTF slots the
    /// image is sampled through. Roles decide texture type (a normal map is not a
    /// default texture) and channel meaning, so identical pixels in different
    /// roles stay separate assets even when their colour space agrees.</description></item>
    /// <item><description><b>Alpha requirement</b> — whether any material sampling it
    /// is non-OPAQUE. This drives whether the alpha channel is kept or dropped, so
    /// it belongs to the asset's identity rather than to a later decision.</description></item>
    /// </list>
    /// <para>
    /// Alpha requirement is unioned across a group rather than splitting it. If the
    /// same albedo is OPAQUE in one export and BLEND in another, one RGBA asset
    /// serves both correctly — the opaque shader path ignores alpha — whereas
    /// splitting would ship the same pixels twice to no benefit. Every other
    /// component splits.
    /// </para>
    /// </summary>
    public struct NebulaTextureDedupKey : IEquatable<NebulaTextureDedupKey>
    {
        public string PixelHash;
        public int Width;
        public int Height;
        public NebulaTextureColorSpace ColorSpace;

        /// <summary>Sorted, '+'-joined role names. See <see cref="NebulaVisualTextureManifest.RoleSignature"/>.</summary>
        public string RoleSignature;

        public bool AlphaRequired;

        /// <summary>
        /// The canonical string form. This is what gets hashed, logged and
        /// compared; two keys are the same texture exactly when these match.
        /// </summary>
        public override string ToString() =>
            string.Join("|", new[]
            {
                PixelHash ?? string.Empty,
                Width.ToString(CultureInfo.InvariantCulture),
                Height.ToString(CultureInfo.InvariantCulture),
                ColorSpace == NebulaTextureColorSpace.Srgb ? "sRGB" : "Linear",
                RoleSignature ?? string.Empty,
                AlphaRequired ? "alpha" : "noalpha"
            });

        public bool Equals(NebulaTextureDedupKey other) =>
            string.Equals(PixelHash, other.PixelHash, StringComparison.Ordinal)
            && Width == other.Width
            && Height == other.Height
            && ColorSpace == other.ColorSpace
            && string.Equals(RoleSignature, other.RoleSignature, StringComparison.Ordinal)
            && AlphaRequired == other.AlphaRequired;

        public override bool Equals(object obj) => obj is NebulaTextureDedupKey other && Equals(other);

        public override int GetHashCode() => ToString().GetHashCode();

        /// <summary>
        /// The key without <see cref="AlphaRequired"/>. Grouping happens on this,
        /// then the alpha requirement is unioned over the group and folded back in
        /// — see the type remarks for why alpha is the one component that must not
        /// split a group.
        /// </summary>
        public string GroupingForm() =>
            string.Join("|", new[]
            {
                PixelHash ?? string.Empty,
                Width.ToString(CultureInfo.InvariantCulture),
                Height.ToString(CultureInfo.InvariantCulture),
                ColorSpace == NebulaTextureColorSpace.Srgb ? "sRGB" : "Linear",
                RoleSignature ?? string.Empty
            });
    }

    /// <summary>One generated texture asset and every occurrence it replaces.</summary>
    public sealed class NebulaTextureEntry
    {
        public NebulaTextureDedupKey Key;

        /// <summary>Stable asset name, derived from the key. Re-runs reuse it, so the GUID survives.</summary>
        public string Id;

        public string AssetPath;
        public string MimeType;

        /// <summary>SHA-256 of the extracted file's bytes — a verbatim copy of one embedded blob.</summary>
        public string SourceHash;

        public int SourceByteLength;

        /// <summary>Every distinct embedded encoding that decodes to these pixels.</summary>
        public readonly List<string> SourceHashes = new List<string>();

        public bool HasAlphaChannel;
        public bool AlphaIsBinary;

        /// <summary>(assetPath, imageIndex, imageName) of every occurrence this asset stands in for.</summary>
        public readonly List<NebulaGlbImageOccurrence> Occurrences = new List<NebulaGlbImageOccurrence>();

        public int LodOccurrenceCount;

        /// <summary>Resolved import policy, recorded so the manifest states what was asked for.</summary>
        public NebulaTextureImportPlan Import;
    }

    /// <summary>The import settings a role earns, recorded in the manifest rather than implied.</summary>
    public struct NebulaTextureImportPlan
    {
        public string TextureType;
        public bool SrgbTexture;
        public string Format;
        public string Compression;
        public bool MipmapEnabled;
        public bool Readable;
        public bool CrunchedCompression;
        public int MaxTextureSize;
        public bool StreamingMipmaps;
        public string AlphaSource;
    }

    /// <summary>
    /// Manifest constants, hashing, role classification and the naming rules that
    /// make a re-run land on the assets it produced last time.
    /// </summary>
    public static class NebulaVisualTextureManifest
    {
        /// <summary>
        /// Bumped whenever extraction, classification or naming changes in a way
        /// that should invalidate previously generated assets.
        /// <para>
        /// Written into every generated manifest next to the source hashes. The
        /// idempotency check reads it back: an unchanged version with unchanged
        /// source hashes means the run may be skipped entirely, which is what keeps
        /// re-running from churning GUIDs.
        /// </para>
        /// <para>1: first extraction — verbatim blob copy, role-keyed dedup, BC7/BC5 desktop profile.</para>
        /// </summary>
        public const int PipelineVersion = 1;

        /// <summary>
        /// Generated assets live outside every <c>Resources</c> folder, on purpose.
        /// <para>
        /// Anything under a <c>Resources</c> directory is included in the player
        /// whether or not a single reference points at it — that is the whole
        /// reason 246 raw GLBs were in the build before Part 3 moved them out.
        /// Putting the extracted textures there would repeat the mistake at a
        /// smaller scale. Here they
        /// enter the build only through the materials that reference them, so an
        /// unused texture costs nothing.
        /// </para>
        /// <para>
        /// The one production call-site that loads a texture by <c>Resources</c>
        /// path is <c>NebulaIconResolver.LoadTexture</c>, and it only ever resolves
        /// paths built by <c>NebulaIconCatalog</c> under <c>VisualIcons/</c>. No
        /// call-site loads a GLB-embedded texture by path, so nothing here needs a
        /// second resolver.
        /// </para>
        /// </summary>
        public const string GeneratedRoot = "Assets/RuntimeVisuals/";

        public const string TextureRoot = GeneratedRoot + "Textures/";
        public const string MaterialRoot = GeneratedRoot + "Materials/";
        public const string ManifestPath = GeneratedRoot + "nebula-visual-texture-manifest.json";

        /// <summary>Where the committed reports go.</summary>
        public const string ReportRoot = "Distribution/Reports/Optimization/";

        /// <summary>
        /// The single source-of-truth tree for authored models, in scan order.
        /// <para>
        /// One entry, and it is deliberately not a <c>Resources</c> path. Part 3
        /// moved every authored GLB out of <c>Assets/Resources/Visuals</c> into
        /// <see href="Assets/VisualSource"/>, and Part 4 followed it with the last
        /// straggler, the legacy hull that used to sit in
        /// <c>Assets/Resources/Ship</c>. Both of those folders are gone; listing
        /// them here cost nothing at runtime — <c>FindSourceGlbs</c> skips a root
        /// that does not exist — which is precisely why a dead path can survive a
        /// rename indefinitely and then silently halve a scan the day somebody
        /// recreates the folder for an unrelated reason.
        /// </para>
        /// <para>
        /// Keeping exactly one root also makes the dry-run and the apply pass
        /// provably identical: both call <c>FindSourceGlbs</c>, so a root that
        /// resolves differently between the two runs is the only way they could
        /// disagree, and with a single non-optional root there is nothing left to
        /// disagree about.
        /// </para>
        /// <para>
        /// <c>Assets/ArtSource</c> is not here on purpose. It holds the Part 24
        /// master export, which nothing references and nothing ships; scanning it
        /// would invent manifest records for textures that never reach the player.
        /// </para>
        /// </summary>
        public static readonly string[] SourceRoots =
        {
            "Assets/VisualSource"
        };

        /// <summary>
        /// The derived LOD1/LOD2 subtree. Tracked separately in every report so a
        /// texture that only ever appears on a distant LOD is visible as such.
        /// </summary>
        public static readonly string[] LodRoots =
        {
            "Assets/VisualSource/Derived/LOD"
        };

        public static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

        /// <summary>
        /// The colour space a role demands. Only base colour and emission are
        /// authored colour; everything else is data and must stay linear.
        /// </summary>
        public static NebulaTextureColorSpace ColorSpaceOf(NebulaTextureRole role)
        {
            switch (role)
            {
                case NebulaTextureRole.BaseColor:
                case NebulaTextureRole.Emissive:
                    return NebulaTextureColorSpace.Srgb;
                default:
                    return NebulaTextureColorSpace.Linear;
            }
        }

        /// <summary>
        /// Canonical role signature: role names sorted by their enum order and
        /// joined with '+'.
        /// <para>
        /// Sorted because a set has no order but a key needs one, and the delivered
        /// exports list slots in whatever order the material happened to declare
        /// them. Without the sort the same texture would key as
        /// <c>MetallicRoughness+Occlusion</c> in one file and
        /// <c>Occlusion+MetallicRoughness</c> in the next, and the dedup would
        /// produce two assets from one image.
        /// </para>
        /// </summary>
        public static string RoleSignature(IEnumerable<NebulaTextureRole> roles)
        {
            var distinct = new List<NebulaTextureRole>();
            foreach (NebulaTextureRole role in roles)
                if (!distinct.Contains(role)) distinct.Add(role);
            if (distinct.Count == 0) distinct.Add(NebulaTextureRole.Unreferenced);
            distinct.Sort();

            var builder = new StringBuilder();
            for (int i = 0; i < distinct.Count; i++)
            {
                if (i > 0) builder.Append('+');
                builder.Append(distinct[i].ToString());
            }
            return builder.ToString();
        }

        /// <summary>
        /// Stable asset name for a key.
        /// <para>
        /// Content-addressed rather than counter-addressed. A name like
        /// <c>basecolor_01</c> would shift every time an image is added or removed
        /// upstream, and every shifted name is a deleted asset and a new GUID —
        /// which would break exactly the prefab references this pipeline exists to
        /// create. Twelve hex digits of the pixel hash is enough to be unique
        /// across sixty-two assets by a wide margin, and the role and dimensions in
        /// front of it keep the folder readable.
        /// </para>
        /// <para>
        /// The trailing <c>_a</c> on alpha-carrying assets is load-bearing rather
        /// than decorative. <see cref="NebulaVisualImportProfile"/> is an
        /// <c>AssetPostprocessor</c>, so it runs on every re-import including ones
        /// nobody asked for; if it had to consult the manifest to learn whether a
        /// texture needs its alpha channel it would be reading a file that may not
        /// be imported yet, and the answer would depend on import order. Path plus
        /// name carries the entire policy, which makes the profile a pure function
        /// of the asset path and impossible to catch half-initialised.
        /// </para>
        /// </summary>
        public static string AssetId(NebulaTextureDedupKey key) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1}x{2}_{3}{4}",
                RoleFolder(key.RoleSignature).ToLowerInvariant(),
                key.Width,
                key.Height,
                (key.PixelHash ?? string.Empty).Substring(0, 12),
                key.AlphaRequired ? "_a" : string.Empty);

        /// <summary>Folder-safe form of a role signature; '+' is not welcome in asset paths.</summary>
        public static string RoleFolder(string roleSignature) =>
            (roleSignature ?? string.Empty).Replace("+", "-");

        /// <summary>Extension for an extracted blob. The bytes are copied verbatim, so it must match the encoding.</summary>
        public static string ExtensionFor(string mimeType)
        {
            if (string.Equals(mimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase)) return ".jpg";
            return ".png";
        }

        public static string TextureAssetPath(NebulaTextureDedupKey key, string mimeType) =>
            TextureRoot + RoleFolder(key.RoleSignature) + "/" + AssetId(key) + ExtensionFor(mimeType);

        /// <summary>True when the GLB is part of the derived LOD set.</summary>
        public static bool IsLodPath(string assetPath)
        {
            foreach (string root in LodRoots)
                if (assetPath.StartsWith(root, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
