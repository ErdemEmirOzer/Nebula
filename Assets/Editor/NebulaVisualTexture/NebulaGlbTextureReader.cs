using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Nebula.EditorTools.VisualTexture
{
    /// <summary>
    /// The glTF role an image is referenced in, which is the only honest source
    /// of a texture's channel semantics.
    /// <para>
    /// A file name cannot be trusted for this. The delivered exports name their
    /// embedded images <c>image_0</c>..<c>image_4</c> in whatever order the
    /// authoring tool emitted them, and the same bytes appear under different
    /// names in different files. What a texture *is* — colour a human authored,
    /// a packed data map, or a tangent-space normal — is decided by the material
    /// slot that samples it, so that is what this reader records.
    /// </para>
    /// </summary>
    public enum NebulaTextureRole
    {
        /// <summary>Referenced but by no slot this reader understands.</summary>
        Unknown = 0,

        /// <summary>pbrMetallicRoughness.baseColorTexture — sRGB albedo.</summary>
        BaseColor = 1,

        /// <summary>materials[].normalTexture — tangent space, linear.</summary>
        Normal = 2,

        /// <summary>pbrMetallicRoughness.metallicRoughnessTexture — G=roughness, B=metallic, linear.</summary>
        MetallicRoughness = 3,

        /// <summary>materials[].occlusionTexture — R=occlusion, linear.</summary>
        Occlusion = 4,

        /// <summary>materials[].emissiveTexture — sRGB emission.</summary>
        Emissive = 5,

        /// <summary>Present in the container but sampled by no material.</summary>
        Unreferenced = 6
    }

    /// <summary>One material slot that samples a given image, with its context.</summary>
    public sealed class NebulaTextureUsage
    {
        public string MaterialName;
        public int MaterialIndex;
        public NebulaTextureRole Role;

        /// <summary>OPAQUE / MASK / BLEND, verbatim from the glTF material.</summary>
        public string AlphaMode;

        /// <summary>Only meaningful for MASK. Carried so a cutout can never be silently retuned.</summary>
        public float AlphaCutoff;

        public bool DoubleSided;

        /// <summary>materials[].normalTexture.scale — must survive any format change.</summary>
        public float NormalScale;

        /// <summary>materials[].occlusionTexture.strength — must survive any format change.</summary>
        public float OcclusionStrength;
    }

    /// <summary>One embedded image inside one GLB: an <em>occurrence</em>, not a texture.</summary>
    public sealed class NebulaGlbImageOccurrence
    {
        /// <summary>Project-relative path of the GLB that embeds this image.</summary>
        public string AssetPath;

        /// <summary>Index into glTF <c>images</c>. With <see cref="AssetPath"/> this is the occurrence identity.</summary>
        public int ImageIndex;

        public string ImageName;
        public string MimeType;
        public int Width;
        public int Height;

        /// <summary>Length of the embedded, still-encoded blob.</summary>
        public int SourceByteLength;

        /// <summary>SHA-256 of the embedded blob exactly as stored. Detects re-encodes.</summary>
        public string SourceHash;

        /// <summary>SHA-256 of the decoded RGBA32 pixels. The identity that actually matters.</summary>
        public string PixelHash;

        /// <summary>True when at least one decoded pixel has alpha &lt; 255.</summary>
        public bool HasAlphaChannel;

        /// <summary>True when every alpha value is 0 or 255 — a cutout mask rather than a gradient.</summary>
        public bool AlphaIsBinary;

        /// <summary>True when this GLB sits under the derived LOD folder.</summary>
        public bool IsLod;

        /// <summary>Every material slot in this GLB that samples the image, in material order.</summary>
        public readonly List<NebulaTextureUsage> Usages = new List<NebulaTextureUsage>();

        /// <summary>The still-encoded bytes. Populated only while a run needs them.</summary>
        [NonSerialized] public byte[] EncodedBytes;
    }

    /// <summary>
    /// One glTF material, flattened to the fields the Built-in glTFast shader
    /// actually consumes.
    /// <para>
    /// Texture slots are recorded as image indices rather than texture indices.
    /// The sampler wrapper in between is not part of a material's identity here —
    /// every delivered export uses the same repeat/linear sampler — and keeping
    /// the indirection would make two identical materials look different because
    /// their exporter happened to emit the sampler twice.
    /// </para>
    /// </summary>
    public sealed class NebulaGlbMaterial
    {
        public string AssetPath;
        public int MaterialIndex;
        public string Name;

        public float[] BaseColorFactor = { 1f, 1f, 1f, 1f };
        public float MetallicFactor = 1f;
        public float RoughnessFactor = 1f;
        public float[] EmissiveFactor = { 0f, 0f, 0f };
        public string AlphaMode = "OPAQUE";
        public float AlphaCutoff = 0.5f;
        public bool DoubleSided;
        public float NormalScale = 1f;
        public float OcclusionStrength = 1f;

        /// <summary>Image index per slot, or -1. Order matches <see cref="NebulaVisualTextureOptimizer.MaterialSlots"/>.</summary>
        public int BaseColorImage = -1;
        public int MetallicRoughnessImage = -1;
        public int NormalImage = -1;
        public int OcclusionImage = -1;
        public int EmissiveImage = -1;

        /// <summary>Extension names present on the source material, so an unhandled one is visible rather than lost.</summary>
        public readonly List<string> Extensions = new List<string>();
    }

    /// <summary>Everything one GLB contributes to the inventory.</summary>
    public sealed class NebulaGlbDocument
    {
        public string AssetPath;
        public bool IsLod;
        public readonly List<NebulaGlbImageOccurrence> Images = new List<NebulaGlbImageOccurrence>();
        public readonly List<NebulaGlbMaterial> Materials = new List<NebulaGlbMaterial>();
    }

    /// <summary>
    /// Reads the GLB container and its glTF JSON directly, without glTFast and
    /// without the asset database.
    /// <para>
    /// Going through the importer was the obvious route and it is the wrong one.
    /// glTFast hands back <see cref="UnityEngine.Texture2D"/> sub-assets it has
    /// already decoded to RGBA32 and uploaded, which means the thing under
    /// measurement — how many distinct images actually exist and what the source
    /// bytes are — has been destroyed before it can be measured. Parsing the
    /// container gives the embedded blob byte for byte, so an extracted asset can
    /// be a verbatim copy rather than a re-encode, and the inventory can tell a
    /// re-compressed duplicate apart from a true one.
    /// </para>
    /// <para>
    /// Deliberately minimal: this understands the chunk layout, <c>bufferViews</c>,
    /// <c>images</c>, <c>textures</c> and <c>materials</c>, and nothing else. It is
    /// not a glTF loader and must never become one.
    /// </para>
    /// </summary>
    public static class NebulaGlbTextureReader
    {
        private const uint MagicGlb = 0x46546C67;   // "glTF"
        private const uint ChunkJson = 0x4E4F534A;  // "JSON"
        private const uint ChunkBinary = 0x004E4942; // "BIN\0"

        /// <summary>
        /// Reads every embedded image in one GLB, with the material slots that use
        /// it. Pixel data is not decoded here — see <see cref="NebulaVisualTextureOptimizer"/>,
        /// which needs Unity to decode PNG/JPEG and so cannot live in this file.
        /// </summary>
        /// <param name="absolutePath">Absolute path on disk.</param>
        /// <param name="assetPath">Project-relative path recorded on each occurrence.</param>
        /// <param name="isLod">Whether the file is part of the derived LOD set.</param>
        public static NebulaGlbDocument Read(string absolutePath, string assetPath, bool isLod)
        {
            byte[] data = File.ReadAllBytes(absolutePath);
            var result = new NebulaGlbDocument { AssetPath = assetPath, IsLod = isLod };

            if (data.Length < 12 || BitConverter.ToUInt32(data, 0) != MagicGlb)
                throw new InvalidDataException("Not a GLB container: " + assetPath);

            string json = null;
            int binaryOffset = -1;
            int binaryLength = 0;

            int cursor = 12;
            while (cursor + 8 <= data.Length)
            {
                int chunkLength = (int)BitConverter.ToUInt32(data, cursor);
                uint chunkType = BitConverter.ToUInt32(data, cursor + 4);
                int payload = cursor + 8;
                if (payload + chunkLength > data.Length) break;

                if (chunkType == ChunkJson) json = Encoding.UTF8.GetString(data, payload, chunkLength);
                else if (chunkType == ChunkBinary) { binaryOffset = payload; binaryLength = chunkLength; }

                // Chunks are four-byte aligned; the length field excludes the padding.
                cursor = payload + chunkLength;
                int padding = cursor % 4;
                if (padding != 0) cursor += 4 - padding;
            }

            if (string.IsNullOrEmpty(json))
                throw new InvalidDataException("GLB has no JSON chunk: " + assetPath);

            NebulaJsonValue root = NebulaJsonValue.Parse(json);
            NebulaJsonValue images = root["images"];
            NebulaJsonValue bufferViews = root["bufferViews"];
            NebulaJsonValue textures = root["textures"];

            // Which image each texture points at, so material slots can be
            // attributed to the image rather than to the sampler wrapper.
            var textureSource = new int[textures?.Count ?? 0];
            for (int i = 0; i < textureSource.Length; i++)
                textureSource[i] = textures[i]["source"]?.AsInt(-1) ?? -1;

            var usagesByImage = new Dictionary<int, List<NebulaTextureUsage>>();
            CollectMaterialUsages(root["materials"], textureSource, usagesByImage, assetPath, result.Materials);

            if (images == null || images.Count == 0) return result;

            for (int index = 0; index < images.Count; index++)
            {
                NebulaJsonValue image = images[index];
                byte[] bytes = ExtractImageBytes(image, bufferViews, data, binaryOffset, binaryLength);
                if (bytes == null) continue;

                var occurrence = new NebulaGlbImageOccurrence
                {
                    AssetPath = assetPath,
                    ImageIndex = index,
                    ImageName = image["name"]?.AsString() ?? string.Empty,
                    MimeType = image["mimeType"]?.AsString() ?? string.Empty,
                    SourceByteLength = bytes.Length,
                    SourceHash = NebulaVisualTextureManifest.Sha256(bytes),
                    IsLod = isLod,
                    EncodedBytes = bytes
                };
                if (usagesByImage.TryGetValue(index, out List<NebulaTextureUsage> usages))
                    occurrence.Usages.AddRange(usages);
                result.Images.Add(occurrence);
            }

            return result;
        }

        private static void CollectMaterialUsages(
            NebulaJsonValue materials,
            int[] textureSource,
            Dictionary<int, List<NebulaTextureUsage>> usagesByImage,
            string assetPath,
            List<NebulaGlbMaterial> definitions)
        {
            if (materials == null) return;

            for (int materialIndex = 0; materialIndex < materials.Count; materialIndex++)
            {
                NebulaJsonValue material = materials[materialIndex];
                string name = material["name"]?.AsString()
                              ?? "material_" + materialIndex.ToString(CultureInfo.InvariantCulture);
                string alphaMode = material["alphaMode"]?.AsString() ?? "OPAQUE";
                float cutoff = material["alphaCutoff"]?.AsFloat(0.5f) ?? 0.5f;
                bool doubleSided = material["doubleSided"]?.AsBool(false) ?? false;
                NebulaJsonValue pbr = material["pbrMetallicRoughness"];

                var definition = new NebulaGlbMaterial
                {
                    AssetPath = assetPath,
                    MaterialIndex = materialIndex,
                    Name = name,
                    AlphaMode = alphaMode,
                    AlphaCutoff = cutoff,
                    DoubleSided = doubleSided,
                    BaseColorFactor = Floats(pbr?["baseColorFactor"], new[] { 1f, 1f, 1f, 1f }),
                    MetallicFactor = pbr?["metallicFactor"]?.AsFloat(1f) ?? 1f,
                    RoughnessFactor = pbr?["roughnessFactor"]?.AsFloat(1f) ?? 1f,
                    EmissiveFactor = Floats(material["emissiveFactor"], new[] { 0f, 0f, 0f }),
                    NormalScale = material["normalTexture"]?["scale"]?.AsFloat(1f) ?? 1f,
                    OcclusionStrength = material["occlusionTexture"]?["strength"]?.AsFloat(1f) ?? 1f
                };

                NebulaJsonValue extensions = material["extensions"];
                if (extensions != null)
                    foreach (string key in extensions.Keys)
                        definition.Extensions.Add(key);

                definition.BaseColorImage = Add(pbr?["baseColorTexture"], NebulaTextureRole.BaseColor);
                definition.MetallicRoughnessImage =
                    Add(pbr?["metallicRoughnessTexture"], NebulaTextureRole.MetallicRoughness);
                definition.NormalImage = Add(material["normalTexture"], NebulaTextureRole.Normal);
                definition.OcclusionImage = Add(material["occlusionTexture"], NebulaTextureRole.Occlusion);
                definition.EmissiveImage = Add(material["emissiveTexture"], NebulaTextureRole.Emissive);
                definitions.Add(definition);

                int Add(NebulaJsonValue slot, NebulaTextureRole role)
                {
                    if (slot == null) return -1;
                    int textureIndex = slot["index"]?.AsInt(-1) ?? -1;
                    if (textureIndex < 0 || textureIndex >= textureSource.Length) return -1;
                    int imageIndex = textureSource[textureIndex];
                    if (imageIndex < 0) return -1;

                    if (!usagesByImage.TryGetValue(imageIndex, out List<NebulaTextureUsage> list))
                        usagesByImage[imageIndex] = list = new List<NebulaTextureUsage>();

                    list.Add(new NebulaTextureUsage
                    {
                        MaterialName = name,
                        MaterialIndex = materialIndex,
                        Role = role,
                        AlphaMode = alphaMode,
                        AlphaCutoff = cutoff,
                        DoubleSided = doubleSided,
                        NormalScale = role == NebulaTextureRole.Normal ? slot["scale"]?.AsFloat(1f) ?? 1f : 1f,
                        OcclusionStrength = role == NebulaTextureRole.Occlusion
                            ? slot["strength"]?.AsFloat(1f) ?? 1f
                            : 1f
                    });
                    return imageIndex;
                }
            }
        }

        private static float[] Floats(NebulaJsonValue array, float[] fallback)
        {
            if (array == null || array.Count == 0) return fallback;
            var values = new float[array.Count];
            for (int i = 0; i < values.Length; i++) values[i] = array[i].AsFloat(0f);
            return values;
        }

        private static byte[] ExtractImageBytes(
            NebulaJsonValue image,
            NebulaJsonValue bufferViews,
            byte[] data,
            int binaryOffset,
            int binaryLength)
        {
            NebulaJsonValue bufferViewIndex = image["bufferView"];
            if (bufferViewIndex != null && bufferViews != null && binaryOffset >= 0)
            {
                NebulaJsonValue view = bufferViews[bufferViewIndex.AsInt(-1)];
                if (view == null) return null;
                int offset = view["byteOffset"]?.AsInt(0) ?? 0;
                int length = view["byteLength"]?.AsInt(0) ?? 0;
                if (length <= 0 || offset + length > binaryLength) return null;

                var bytes = new byte[length];
                Buffer.BlockCopy(data, binaryOffset + offset, bytes, 0, length);
                return bytes;
            }

            string uri = image["uri"]?.AsString();
            if (!string.IsNullOrEmpty(uri) && uri.StartsWith("data:", StringComparison.Ordinal))
            {
                int comma = uri.IndexOf(',');
                if (comma > 0) return Convert.FromBase64String(uri.Substring(comma + 1));
            }

            // An external file reference. None of the delivered exports use one,
            // and silently resolving it would let a texture enter the inventory
            // whose bytes are not actually inside the GLB being measured.
            return null;
        }
    }
}
