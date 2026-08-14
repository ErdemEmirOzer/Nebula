using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Nebula.EditorTools.VisualTexture
{
    /// <summary>One generated material and the glTF materials it stands in for.</summary>
    public sealed class NebulaMaterialEntry
    {
        public string Id;
        public string AssetPath;
        public string SignatureHash;

        /// <summary>The glTF material this one was cloned from — first in scan order, so it is deterministic.</summary>
        public NebulaGlbMaterial Template;

        /// <summary>Slot name to generated texture id, for the slots that have a texture.</summary>
        public readonly Dictionary<string, string> Slots =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Every (glb, materialIndex) this material replaces.</summary>
        public readonly List<NebulaGlbMaterial> Sources = new List<NebulaGlbMaterial>();
    }

    /// <summary>
    /// What a run intends to do, computed and reportable without touching a
    /// single asset.
    /// <para>
    /// Kept as a value the caller holds rather than as fields on the pipeline so
    /// that plan and apply cannot drift: apply takes a plan and is forbidden to
    /// write anything the plan did not name.
    /// </para>
    /// </summary>
    public sealed class NebulaVisualTexturePlan
    {
        public int GlbCount;
        public int OccurrenceCount;
        public int MaterialDefinitionCount;
        public long EmbeddedSourceBytes;
        public int UniquePixelHashCount;
        public int UniqueSourceHashCount;

        public readonly List<NebulaTextureEntry> Textures = new List<NebulaTextureEntry>();
        public readonly List<NebulaMaterialEntry> Materials = new List<NebulaMaterialEntry>();

        /// <summary>Anything that would make an apply wrong. Non-empty means apply refuses to run.</summary>
        public readonly List<string> Blockers = new List<string>();

        /// <summary>Anything worth a human's attention that is not fatal.</summary>
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Editor-only extraction pipeline for the textures embedded in the delivered
    /// GLBs.
    /// <para>
    /// The problem it solves is not "the textures are uncompressed" — that is only
    /// half of it. 929 embedded images across 246 files decode to 61 distinct
    /// pictures; the same 1024x1024 hull albedo is stored ninety times, once per
    /// export that uses it, and glTFast dutifully builds ninety separate
    /// <c>Texture2D</c> objects at RGBA32 with mips. Compression alone would leave
    /// ninety copies. Deduplication alone would leave them uncompressed. Both
    /// together turn 2.97 GB of player textures into roughly 55 MB.
    /// </para>
    /// <para>
    /// Everything here is Editor-only and runs at author time. The player never
    /// parses a GLB, never decodes an image and never sees this code: it loads
    /// prefabs, prefabs reference materials, materials reference imported textures.
    /// That is the entire runtime contract, and it is unchanged from today's.
    /// </para>
    /// <para>
    /// The pipeline copies embedded blobs <b>verbatim</b> instead of re-encoding
    /// them. A PNG round-tripped through <c>EncodeToPNG</c> would be a different
    /// file every time the encoder changed, which would break the "re-running
    /// produces no new assets" guarantee for a reason unrelated to the source art,
    /// and any re-encode of a JPEG would compound its loss before the block
    /// compressor ever saw it.
    /// </para>
    /// </summary>
    public static class NebulaVisualTextureOptimizer
    {
        /// <summary>Slot names, in the order the manifest and the reports list them.</summary>
        public static readonly string[] MaterialSlots =
        {
            "baseColorTexture",
            "metallicRoughnessTexture",
            "normalTexture",
            "occlusionTexture",
            "emissiveTexture"
        };

        private const string ShaderName = "glTF/PbrMetallicRoughness";

        // ---- entry points ----------------------------------------------------

        [MenuItem("Nebula/Visuals/Texture Optimizer/Plan (dry run)", false, 100)]
        public static void MenuPlan()
        {
            NebulaVisualTexturePlan plan = BuildPlan();
            WriteReports(plan);
            Debug.Log(Summary(plan) + "\nReports written. Nothing was modified.");
        }

        [MenuItem("Nebula/Visuals/Texture Optimizer/Apply", false, 101)]
        public static void MenuApply()
        {
            NebulaVisualTexturePlan plan = BuildPlan();
            WriteReports(plan);
            Apply(plan);
        }

        /// <summary>Batch-mode entry: plan only, never writes an asset.</summary>
        public static void RunPlanFromCommandLine()
        {
            try
            {
                NebulaVisualTexturePlan plan = BuildPlan();
                WriteReports(plan);
                Debug.Log("NEBULA-TEXTURE-PLAN-OK\n" + Summary(plan));
            }
            catch (Exception exception)
            {
                Debug.LogError("NEBULA-TEXTURE-PLAN-FAILED\n" + exception);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        /// <summary>Batch-mode entry: plan, validate, then write the generated assets.</summary>
        public static void RunApplyFromCommandLine()
        {
            try
            {
                NebulaVisualTexturePlan plan = BuildPlan();
                WriteReports(plan);
                if (!Apply(plan))
                {
                    EditorApplication.Exit(2);
                    return;
                }
                Debug.Log("NEBULA-TEXTURE-APPLY-OK\n" + Summary(plan));
            }
            catch (Exception exception)
            {
                Debug.LogError("NEBULA-TEXTURE-APPLY-FAILED\n" + exception);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        // ---- scan ------------------------------------------------------------

        /// <summary>Every GLB under the source roots, in a stable order.</summary>
        public static List<string> FindSourceGlbs()
        {
            var paths = new List<string>();
            foreach (string root in NebulaVisualTextureManifest.SourceRoots)
            {
                string absolute = ToAbsolute(root);
                if (!Directory.Exists(absolute)) continue;
                foreach (string file in Directory.GetFiles(absolute, "*.glb", SearchOption.AllDirectories))
                    paths.Add(ToAssetPath(file));
            }
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        /// <summary>
        /// Reads every GLB and decodes every embedded image far enough to hash its
        /// pixels.
        /// <para>
        /// Decoding is the expensive half and there is no way around it: the source
        /// bytes cannot answer "are these the same picture", because four of the
        /// delivered images are byte-different re-encodes of an image that is
        /// already present. Hashing <see cref="Texture2D.GetPixels32"/> rather than
        /// the raw texture data keeps the answer stable whether Unity decoded a PNG
        /// to RGB24 or RGBA32.
        /// </para>
        /// </summary>
        public static List<NebulaGlbDocument> Scan()
        {
            var documents = new List<NebulaGlbDocument>();
            List<string> glbs = FindSourceGlbs();

            for (int i = 0; i < glbs.Count; i++)
            {
                string assetPath = glbs[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "Nebula texture inventory",
                        assetPath,
                        (float)i / Mathf.Max(1, glbs.Count)))
                {
                    EditorUtility.ClearProgressBar();
                    throw new OperationCanceledException("Inventory cancelled.");
                }

                NebulaGlbDocument document = NebulaGlbTextureReader.Read(
                    ToAbsolute(assetPath), assetPath, NebulaVisualTextureManifest.IsLodPath(assetPath));

                foreach (NebulaGlbImageOccurrence occurrence in document.Images)
                    Decode(occurrence);

                documents.Add(document);
            }
            EditorUtility.ClearProgressBar();
            return documents;
        }

        private static void Decode(NebulaGlbImageOccurrence occurrence)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, occurrence.EncodedBytes, false))
                    throw new InvalidDataException(
                        "Could not decode embedded image " + occurrence.ImageIndex + " of " + occurrence.AssetPath);

                occurrence.Width = texture.width;
                occurrence.Height = texture.height;

                Color32[] pixels = texture.GetPixels32();
                var bytes = new byte[pixels.Length * 4];
                bool anyAlpha = false;
                bool onlyBinaryAlpha = true;
                for (int i = 0, b = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    bytes[b++] = pixel.r;
                    bytes[b++] = pixel.g;
                    bytes[b++] = pixel.b;
                    bytes[b++] = pixel.a;
                    if (pixel.a == 255) continue;
                    anyAlpha = true;
                    if (pixel.a != 0) onlyBinaryAlpha = false;
                }

                occurrence.PixelHash = NebulaVisualTextureManifest.Sha256(bytes);
                occurrence.HasAlphaChannel = anyAlpha;
                occurrence.AlphaIsBinary = anyAlpha && onlyBinaryAlpha;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // ---- plan ------------------------------------------------------------

        /// <summary>
        /// Groups every occurrence into the assets that will represent it, and
        /// works out which generated material replaces which glTF material.
        /// <para>
        /// An occurrence with roles in two colour spaces — the same image sampled
        /// once as albedo and once as roughness — becomes <b>two</b> entries, not
        /// one. None of today's 246 files do that, but the split is not a defensive
        /// nicety: merging those two is the failure that produces a surface that is
        /// subtly wrong everywhere rather than obviously broken somewhere, and it
        /// would survive every screenshot check.
        /// </para>
        /// </summary>
        public static NebulaVisualTexturePlan BuildPlan()
        {
            List<NebulaGlbDocument> documents = Scan();
            var plan = new NebulaVisualTexturePlan { GlbCount = documents.Count };

            var pixelHashes = new HashSet<string>(StringComparer.Ordinal);
            var sourceHashes = new HashSet<string>(StringComparer.Ordinal);

            // Phase 1: group on everything except the alpha requirement.
            var groups = new Dictionary<string, NebulaTextureEntry>(StringComparer.Ordinal);
            var groupOrder = new List<string>();

            foreach (NebulaGlbDocument document in documents)
            {
                plan.MaterialDefinitionCount += document.Materials.Count;
                foreach (NebulaGlbImageOccurrence occurrence in document.Images)
                {
                    plan.OccurrenceCount++;
                    plan.EmbeddedSourceBytes += occurrence.SourceByteLength;
                    pixelHashes.Add(occurrence.PixelHash);
                    sourceHashes.Add(occurrence.SourceHash);

                    foreach (var binding in SplitByColorSpace(occurrence))
                    {
                        var key = new NebulaTextureDedupKey
                        {
                            PixelHash = occurrence.PixelHash,
                            Width = occurrence.Width,
                            Height = occurrence.Height,
                            ColorSpace = binding.Key,
                            RoleSignature = NebulaVisualTextureManifest.RoleSignature(binding.Value)
                        };

                        string grouping = key.GroupingForm();
                        if (!groups.TryGetValue(grouping, out NebulaTextureEntry entry))
                        {
                            groups[grouping] = entry = new NebulaTextureEntry
                            {
                                Key = key,
                                MimeType = occurrence.MimeType,
                                SourceHash = occurrence.SourceHash,
                                SourceByteLength = occurrence.SourceByteLength
                            };
                            groupOrder.Add(grouping);
                        }

                        entry.Occurrences.Add(occurrence);
                        if (occurrence.IsLod) entry.LodOccurrenceCount++;
                        if (!entry.SourceHashes.Contains(occurrence.SourceHash))
                            entry.SourceHashes.Add(occurrence.SourceHash);
                        entry.HasAlphaChannel |= occurrence.HasAlphaChannel;
                        entry.AlphaIsBinary |= occurrence.AlphaIsBinary;

                        // Union rather than split: see NebulaTextureDedupKey.
                        if (occurrence.HasAlphaChannel && RequiresAlpha(occurrence, binding.Value))
                            entry.Key.AlphaRequired = true;
                    }
                }
            }

            plan.UniquePixelHashCount = pixelHashes.Count;
            plan.UniqueSourceHashCount = sourceHashes.Count;

            // Phase 2: names, paths and import plans, now that alpha is settled.
            foreach (string grouping in groupOrder)
            {
                NebulaTextureEntry entry = groups[grouping];
                entry.Id = NebulaVisualTextureManifest.AssetId(entry.Key);
                entry.AssetPath = NebulaVisualTextureManifest.TextureAssetPath(entry.Key, entry.MimeType);
                entry.Import = NebulaVisualImportProfile.PlanFor(entry.AssetPath);
                plan.Textures.Add(entry);
            }
            plan.Textures.Sort((a, b) => string.CompareOrdinal(a.AssetPath, b.AssetPath));

            BuildMaterialPlan(documents, plan);
            Validate(plan);
            return plan;
        }

        /// <summary>
        /// Partitions an occurrence's roles by the colour space each demands.
        /// Almost always one entry; two when an image is sampled as both colour
        /// and data.
        /// </summary>
        private static List<KeyValuePair<NebulaTextureColorSpace, List<NebulaTextureRole>>>
            SplitByColorSpace(NebulaGlbImageOccurrence occurrence)
        {
            var srgb = new List<NebulaTextureRole>();
            var linear = new List<NebulaTextureRole>();

            foreach (NebulaTextureUsage usage in occurrence.Usages)
            {
                List<NebulaTextureRole> bucket =
                    NebulaVisualTextureManifest.ColorSpaceOf(usage.Role) == NebulaTextureColorSpace.Srgb
                        ? srgb
                        : linear;
                if (!bucket.Contains(usage.Role)) bucket.Add(usage.Role);
            }

            var result = new List<KeyValuePair<NebulaTextureColorSpace, List<NebulaTextureRole>>>();
            if (srgb.Count > 0)
                result.Add(new KeyValuePair<NebulaTextureColorSpace, List<NebulaTextureRole>>(
                    NebulaTextureColorSpace.Srgb, srgb));
            if (linear.Count > 0)
                result.Add(new KeyValuePair<NebulaTextureColorSpace, List<NebulaTextureRole>>(
                    NebulaTextureColorSpace.Linear, linear));

            // Embedded but sampled by nothing. It still gets an entry so the
            // inventory can say so out loud instead of the image vanishing from
            // the report, which would read as a miscount.
            if (result.Count == 0)
                result.Add(new KeyValuePair<NebulaTextureColorSpace, List<NebulaTextureRole>>(
                    NebulaTextureColorSpace.Linear,
                    new List<NebulaTextureRole> { NebulaTextureRole.Unreferenced }));

            return result;
        }

        /// <summary>
        /// True when at least one material sampling this image through one of the
        /// given roles is non-OPAQUE.
        /// <para>
        /// An OPAQUE material's shader path never reads alpha, so an alpha channel
        /// present only under OPAQUE materials is dead weight the block encoder
        /// would otherwise spend precision on.
        /// </para>
        /// </summary>
        private static bool RequiresAlpha(
            NebulaGlbImageOccurrence occurrence, List<NebulaTextureRole> roles)
        {
            foreach (NebulaTextureUsage usage in occurrence.Usages)
            {
                if (!roles.Contains(usage.Role)) continue;
                if (!string.Equals(usage.AlphaMode, "OPAQUE", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ---- materials -------------------------------------------------------

        private static void BuildMaterialPlan(List<NebulaGlbDocument> documents, NebulaVisualTexturePlan plan)
        {
            var textureByOccurrence =
                new Dictionary<string, NebulaTextureEntry>(StringComparer.Ordinal);
            foreach (NebulaTextureEntry entry in plan.Textures)
            foreach (NebulaGlbImageOccurrence occurrence in entry.Occurrences)
                textureByOccurrence[OccurrenceKey(occurrence, entry.Key.ColorSpace)] = entry;

            var bySignature = new Dictionary<string, NebulaMaterialEntry>(StringComparer.Ordinal);
            var order = new List<string>();

            foreach (NebulaGlbDocument document in documents)
            foreach (NebulaGlbMaterial material in document.Materials)
            {
                var slots = new Dictionary<string, string>(StringComparer.Ordinal);
                Bind("baseColorTexture", material.BaseColorImage, NebulaTextureColorSpace.Srgb);
                Bind("metallicRoughnessTexture", material.MetallicRoughnessImage, NebulaTextureColorSpace.Linear);
                Bind("normalTexture", material.NormalImage, NebulaTextureColorSpace.Linear);
                Bind("occlusionTexture", material.OcclusionImage, NebulaTextureColorSpace.Linear);
                Bind("emissiveTexture", material.EmissiveImage, NebulaTextureColorSpace.Srgb);

                void Bind(string slot, int imageIndex, NebulaTextureColorSpace space)
                {
                    if (imageIndex < 0) return;
                    string lookup = material.AssetPath + "#" + imageIndex + "#" + space;
                    slots[slot] = textureByOccurrence.TryGetValue(lookup, out NebulaTextureEntry entry)
                        ? entry.Id
                        : null;
                }

                string signature = MaterialSignature(material, slots);
                if (!bySignature.TryGetValue(signature, out NebulaMaterialEntry generated))
                {
                    generated = new NebulaMaterialEntry
                    {
                        SignatureHash = NebulaVisualTextureManifest.Sha256(signature),
                        Template = material
                    };
                    generated.Id = Sanitize(material.Name) + "_" + generated.SignatureHash.Substring(0, 12);
                    generated.AssetPath =
                        NebulaVisualTextureManifest.MaterialRoot + generated.Id + ".mat";
                    foreach (KeyValuePair<string, string> slot in slots) generated.Slots[slot.Key] = slot.Value;
                    bySignature[signature] = generated;
                    order.Add(signature);
                }
                generated.Sources.Add(material);
            }

            foreach (string signature in order) plan.Materials.Add(bySignature[signature]);
            plan.Materials.Sort((a, b) => string.CompareOrdinal(a.AssetPath, b.AssetPath));
        }

        private static string OccurrenceKey(NebulaGlbImageOccurrence occurrence, NebulaTextureColorSpace space) =>
            occurrence.AssetPath + "#" + occurrence.ImageIndex + "#" + space;

        /// <summary>
        /// Everything that makes two glTF materials the same material.
        /// <para>
        /// Textures enter as the generated texture id, not as an image index — two
        /// materials in two files that both point at the ninety-times-duplicated
        /// hull albedo are the same material, and an index-keyed signature would
        /// call them different because their exports numbered the image
        /// differently.
        /// </para>
        /// </summary>
        private static string MaterialSignature(
            NebulaGlbMaterial material, Dictionary<string, string> slots)
        {
            var builder = new StringBuilder();
            builder.Append(material.Name).Append('|')
                .Append(Vector(material.BaseColorFactor)).Append('|')
                .Append(material.MetallicFactor.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(material.RoughnessFactor.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(Vector(material.EmissiveFactor)).Append('|')
                .Append(material.AlphaMode).Append('|')
                .Append(material.AlphaCutoff.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(material.DoubleSided ? "two" : "one").Append('|')
                .Append(material.NormalScale.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(material.OcclusionStrength.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(string.Join(",", material.Extensions.OrderBy(x => x, StringComparer.Ordinal)));

            foreach (string slot in MaterialSlots)
                builder.Append('|').Append(slot).Append('=')
                    .Append(slots.TryGetValue(slot, out string id) ? id ?? "unresolved" : "none");

            return builder.ToString();
        }

        private static string Vector(float[] values) =>
            values == null
                ? string.Empty
                : string.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));

        private static string Sanitize(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
                builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
            string result = builder.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "material" : result;
        }

        // ---- validation ------------------------------------------------------

        /// <summary>
        /// Everything that must hold before a single file is written.
        /// <para>
        /// The pipeline never modifies a source GLB or a prefab, so a failed apply
        /// cannot break an existing asset — but it can still leave a half-written
        /// generated folder that a later run would treat as real. Refusing up front
        /// on any blocker is cheaper than reasoning about partial state.
        /// </para>
        /// </summary>
        private static void Validate(NebulaVisualTexturePlan plan)
        {
            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (NebulaTextureEntry entry in plan.Textures)
            {
                if (!seenPaths.Add(entry.AssetPath))
                    plan.Blockers.Add("Two texture entries claim the same path: " + entry.AssetPath);
                if (entry.Key.Width <= 0 || entry.Key.Height <= 0)
                    plan.Blockers.Add("Texture has no dimensions: " + entry.Id);
                if (entry.Occurrences.Count == 0)
                    plan.Blockers.Add("Texture entry with no occurrences: " + entry.Id);
                if (entry.Key.Width % 4 != 0 || entry.Key.Height % 4 != 0)
                    plan.Warnings.Add(
                        "Not block-compressible without padding, left to Unity's fallback: "
                        + entry.Id + " " + entry.Key.Width + "x" + entry.Key.Height);
                if (entry.Key.RoleSignature == NebulaTextureRole.Unknown.ToString()
                    || entry.Key.RoleSignature == NebulaTextureRole.Unreferenced.ToString())
                    plan.Warnings.Add("Embedded but sampled by no material: " + entry.Id);
            }

            var seenMaterials = new HashSet<string>(StringComparer.Ordinal);
            foreach (NebulaMaterialEntry material in plan.Materials)
            {
                if (!seenMaterials.Add(material.AssetPath))
                    plan.Blockers.Add("Two material entries claim the same path: " + material.AssetPath);
                foreach (KeyValuePair<string, string> slot in material.Slots)
                    if (slot.Value == null)
                        plan.Blockers.Add(
                            "Material " + material.Id + " slot " + slot.Key + " resolved to no texture.");
                if (material.Template.Extensions.Count > 0)
                    plan.Warnings.Add(
                        "Material " + material.Id + " carries glTF extensions this pipeline does not "
                        + "interpret (" + string.Join(", ", material.Template.Extensions)
                        + "); the cloned material keeps whatever glTFast made of them.");
            }
        }

        // ---- apply -----------------------------------------------------------

        /// <summary>
        /// Writes exactly what the plan names and nothing else.
        /// <para>
        /// A file is written only when its bytes differ from what is already there.
        /// That is what makes a second run a no-op instead of a churn of deleted
        /// and recreated assets, and it is why the GUIDs that Agent 2's prefabs
        /// will reference survive a re-run.
        /// </para>
        /// </summary>
        /// <returns>False when the plan had blockers; nothing is written in that case.</returns>
        public static bool Apply(NebulaVisualTexturePlan plan)
        {
            if (plan.Blockers.Count > 0)
            {
                Debug.LogError(
                    "Nebula texture optimizer refused to apply:\n  " + string.Join("\n  ", plan.Blockers));
                return false;
            }

            Directory.CreateDirectory(ToAbsolute(NebulaVisualTextureManifest.TextureRoot));
            Directory.CreateDirectory(ToAbsolute(NebulaVisualTextureManifest.MaterialRoot));

            int written = 0;
            int unchanged = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (NebulaTextureEntry entry in plan.Textures)
                {
                    string absolute = ToAbsolute(entry.AssetPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(absolute) ?? string.Empty);

                    byte[] bytes = Representative(entry).EncodedBytes;
                    if (File.Exists(absolute)
                        && NebulaVisualTextureManifest.Sha256(File.ReadAllBytes(absolute)) == entry.SourceHash)
                    {
                        unchanged++;
                        continue;
                    }

                    File.WriteAllBytes(absolute, bytes);
                    written++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // A plain refresh, never ForceUpdate. Forcing would re-import every
            // asset in the project — including the ones other workstreams have
            // open — to pick up sixty-two new files that a normal refresh already
            // notices.
            AssetDatabase.Refresh();
            int materials = ApplyMaterials(plan);
            WriteManifest(plan);
            AssetDatabase.SaveAssets();

            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "Nebula texture optimizer applied: {0} textures written, {1} already current, {2} materials, "
                + "manifest at {3}.",
                written, unchanged, materials, NebulaVisualTextureManifest.ManifestPath));
            return true;
        }

        /// <summary>
        /// The occurrence whose bytes get extracted: first in scan order.
        /// <para>
        /// Deterministic on purpose. When a group contains several distinct
        /// encodings of the same picture, picking "the smallest" or "the first
        /// PNG" would make the extracted file depend on a comparison that could
        /// change; first in a sorted scan cannot.
        /// </para>
        /// </summary>
        private static NebulaGlbImageOccurrence Representative(NebulaTextureEntry entry) =>
            entry.Occurrences
                .OrderBy(o => o.AssetPath, StringComparer.Ordinal)
                .ThenBy(o => o.ImageIndex)
                .First();

        /// <summary>
        /// Clones each unique glTF material out of the GLB Unity already imported
        /// and retargets its texture slots at the extracted assets.
        /// <para>
        /// Cloning beats rebuilding from the glTF JSON. glTFast decides shader
        /// keywords, render queue, blend state and the gamma handling of
        /// <c>baseColorFactor</c> when it builds its material, and re-deriving all
        /// of that here would be a second implementation of someone else's importer
        /// that drifts the first time they change it. Taking their material and
        /// swapping five texture references changes exactly the thing this pipeline
        /// is meant to change.
        /// </para>
        /// </summary>
        private static int ApplyMaterials(NebulaVisualTexturePlan plan)
        {
            var textures = new Dictionary<string, Texture>(StringComparer.Ordinal);
            foreach (NebulaTextureEntry entry in plan.Textures)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture>(entry.AssetPath);
                if (texture == null)
                {
                    Debug.LogError("Extracted texture did not import: " + entry.AssetPath);
                    continue;
                }
                textures[entry.Id] = texture;
            }

            int count = 0;
            foreach (NebulaMaterialEntry entry in plan.Materials)
            {
                Material source = FindImportedMaterial(entry.Template);
                if (source == null)
                {
                    Debug.LogError(
                        "No imported material named '" + entry.Template.Name + "' in "
                        + entry.Template.AssetPath + "; skipping " + entry.Id);
                    continue;
                }

                var existing = AssetDatabase.LoadAssetAtPath<Material>(entry.AssetPath);
                Material target;
                if (existing != null)
                {
                    // Copy in place. Re-creating would issue a new GUID and break
                    // every prefab already pointing at this material.
                    existing.shader = source.shader;
                    existing.CopyPropertiesFromMaterial(source);
                    target = existing;
                }
                else
                {
                    target = new Material(source);
                    AssetDatabase.CreateAsset(target, entry.AssetPath);
                }

                foreach (string slot in MaterialSlots)
                {
                    if (!target.HasProperty(slot)) continue;
                    if (entry.Slots.TryGetValue(slot, out string id)
                        && id != null
                        && textures.TryGetValue(id, out Texture texture))
                        target.SetTexture(slot, texture);
                    else
                        target.SetTexture(slot, null);
                }

                EditorUtility.SetDirty(target);
                count++;
            }
            return count;
        }

        private static Material FindImportedMaterial(NebulaGlbMaterial material)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(material.AssetPath);
            Material fallback = null;
            int index = 0;
            foreach (UnityEngine.Object asset in assets)
            {
                if (!(asset is Material candidate)) continue;
                if (string.Equals(candidate.name, material.Name, StringComparison.Ordinal)) return candidate;
                if (index++ == material.MaterialIndex) fallback = candidate;
            }
            return fallback;
        }

        // ---- reports and manifest -------------------------------------------

        public static void WriteReports(NebulaVisualTexturePlan plan)
        {
            string root = Path.Combine(ProjectRoot, NebulaVisualTextureManifest.ReportRoot);
            Directory.CreateDirectory(root);

            File.WriteAllText(Path.Combine(root, "visual-texture-inventory.json"), BuildInventoryJson(plan));
            File.WriteAllText(Path.Combine(root, "visual-texture-duplicates.csv"), BuildDuplicatesCsv(plan));
            File.WriteAllText(Path.Combine(root, "visual-texture-dedup-plan.md"), BuildDedupPlanMarkdown(plan));
        }

        private static void WriteManifest(NebulaVisualTexturePlan plan)
        {
            string absolute = ToAbsolute(NebulaVisualTextureManifest.ManifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute) ?? string.Empty);
            File.WriteAllText(absolute, BuildManifestJson(plan));
            AssetDatabase.ImportAsset(
                NebulaVisualTextureManifest.ManifestPath, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>
        /// The handoff document: which generated material replaces which glTF
        /// material, and which generated texture sits in each of its slots.
        /// </summary>
        private static string BuildManifestJson(NebulaVisualTexturePlan plan)
        {
            var writer = new NebulaJsonWriter();
            writer.BeginObject();
            writer.Write("pipelineVersion", NebulaVisualTextureManifest.PipelineVersion);
            writer.Write("importProfileVersion", (int)new NebulaVisualImportProfile().GetVersion());
            writer.Write("dedupKey",
                "sha256(decoded RGBA32) | width | height | colorSpace | roleSignature | alphaRequired");
            writer.Write("shader", ShaderName);
            writer.Write("glbCount", plan.GlbCount);
            writer.Write("occurrenceCount", plan.OccurrenceCount);
            writer.Write("uniqueTextureCount", plan.Textures.Count);
            writer.Write("materialDefinitionCount", plan.MaterialDefinitionCount);
            writer.Write("uniqueMaterialCount", plan.Materials.Count);

            writer.BeginArray("textures");
            foreach (NebulaTextureEntry entry in plan.Textures) WriteTexture(writer, entry);
            writer.EndArray();

            writer.BeginArray("materials");
            foreach (NebulaMaterialEntry entry in plan.Materials)
            {
                writer.BeginObject();
                writer.Write("id", entry.Id);
                writer.Write("assetPath", entry.AssetPath);
                writer.Write("signatureHash", entry.SignatureHash);
                writer.Write("alphaMode", entry.Template.AlphaMode);
                writer.Write("alphaCutoff", entry.Template.AlphaCutoff);
                writer.Write("doubleSided", entry.Template.DoubleSided);
                writer.Write("normalScale", entry.Template.NormalScale);
                writer.Write("occlusionStrength", entry.Template.OcclusionStrength);
                writer.BeginObject("slots");
                foreach (string slot in MaterialSlots)
                    writer.Write(slot, entry.Slots.TryGetValue(slot, out string id) ? id : null);
                writer.EndObject();
                writer.BeginArray("replaces");
                foreach (NebulaGlbMaterial source in entry.Sources)
                {
                    writer.BeginObject();
                    writer.Write("glb", source.AssetPath);
                    writer.Write("materialIndex", source.MaterialIndex);
                    writer.Write("materialName", source.Name);
                    writer.EndObject();
                }
                writer.EndArray();
                writer.EndObject();
            }
            writer.EndArray();
            writer.EndObject();
            return writer.ToString();
        }

        private static void WriteTexture(NebulaJsonWriter writer, NebulaTextureEntry entry)
        {
            writer.BeginObject();
            writer.Write("id", entry.Id);
            writer.Write("assetPath", entry.AssetPath);
            writer.Write("dedupKey", entry.Key.ToString());
            writer.Write("pixelHash", entry.Key.PixelHash);
            writer.Write("width", entry.Key.Width);
            writer.Write("height", entry.Key.Height);
            writer.Write("colorSpace",
                entry.Key.ColorSpace == NebulaTextureColorSpace.Srgb ? "sRGB" : "Linear");
            writer.Write("roleSignature", entry.Key.RoleSignature);
            writer.Write("alphaRequired", entry.Key.AlphaRequired);
            writer.Write("hasAlphaChannel", entry.HasAlphaChannel);
            writer.Write("alphaIsBinary", entry.AlphaIsBinary);
            writer.Write("mimeType", entry.MimeType);
            writer.Write("sourceHash", entry.SourceHash);
            writer.Write("sourceByteLength", entry.SourceByteLength);
            writer.Write("occurrenceCount", entry.Occurrences.Count);
            writer.Write("lodOccurrenceCount", entry.LodOccurrenceCount);

            writer.BeginArray("sourceHashes");
            foreach (string hash in entry.SourceHashes) writer.WriteRaw("\"" + hash + "\"");
            writer.EndArray();

            writer.BeginObject("import");
            writer.Write("textureType", entry.Import.TextureType);
            writer.Write("sRGBTexture", entry.Import.SrgbTexture);
            writer.Write("format", entry.Import.Format);
            writer.Write("compression", entry.Import.Compression);
            writer.Write("mipmapEnabled", entry.Import.MipmapEnabled);
            writer.Write("readable", entry.Import.Readable);
            writer.Write("crunchedCompression", entry.Import.CrunchedCompression);
            writer.Write("maxTextureSize", entry.Import.MaxTextureSize);
            writer.Write("streamingMipmaps", entry.Import.StreamingMipmaps);
            writer.Write("alphaSource", entry.Import.AlphaSource);
            writer.EndObject();

            writer.BeginArray("occurrences");
            foreach (NebulaGlbImageOccurrence occurrence in entry.Occurrences
                         .OrderBy(o => o.AssetPath, StringComparer.Ordinal)
                         .ThenBy(o => o.ImageIndex))
            {
                writer.BeginObject();
                writer.Write("glb", occurrence.AssetPath);
                writer.Write("imageIndex", occurrence.ImageIndex);
                writer.Write("imageName", occurrence.ImageName);
                writer.Write("sourceHash", occurrence.SourceHash);
                writer.Write("isLod", occurrence.IsLod);
                writer.EndObject();
            }
            writer.EndArray();
            writer.EndObject();
        }

        private static string BuildInventoryJson(NebulaVisualTexturePlan plan)
        {
            var writer = new NebulaJsonWriter();
            writer.BeginObject();
            writer.Write("pipelineVersion", NebulaVisualTextureManifest.PipelineVersion);
            writer.Write("dedupKey",
                "sha256(decoded RGBA32) | width | height | colorSpace | roleSignature | alphaRequired");
            writer.Write("glbCount", plan.GlbCount);
            writer.Write("occurrenceCount", plan.OccurrenceCount);
            writer.Write("uniquePixelHashCount", plan.UniquePixelHashCount);
            writer.Write("uniqueSourceHashCount", plan.UniqueSourceHashCount);
            writer.Write("uniqueTextureCount", plan.Textures.Count);
            writer.Write("materialDefinitionCount", plan.MaterialDefinitionCount);
            writer.Write("uniqueMaterialCount", plan.Materials.Count);
            writer.Write("embeddedSourceBytes", plan.EmbeddedSourceBytes);
            writer.Write("dedupedSourceBytes", plan.Textures.Sum(t => (long)t.SourceByteLength));
            writer.Write("rgba32WithMipsBytesToday", plan.Textures.Sum(
                t => (long)Rgba32WithMips(t.Key.Width, t.Key.Height) * t.Occurrences.Count));
            writer.Write("blockCompressedBytesAfter", plan.Textures.Sum(
                t => (long)BlockCompressedWithMips(t.Key.Width, t.Key.Height)));

            writer.BeginArray("blockers");
            foreach (string blocker in plan.Blockers) writer.WriteRaw(Quote(blocker));
            writer.EndArray();
            writer.BeginArray("warnings");
            foreach (string warning in plan.Warnings) writer.WriteRaw(Quote(warning));
            writer.EndArray();

            writer.BeginArray("textures");
            foreach (NebulaTextureEntry entry in plan.Textures) WriteTexture(writer, entry);
            writer.EndArray();
            writer.EndObject();
            return writer.ToString();
        }

        private static string BuildDuplicatesCsv(NebulaVisualTexturePlan plan)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "texture_id,dedup_key,pixel_hash,width,height,color_space,role_signature,alpha_required,"
                + "mime,occurrence_count,lod_occurrence_count,distinct_source_hashes,source_bytes_each,"
                + "source_bytes_total,source_bytes_saved,rgba32_mips_today,block_after,glb,image_index,"
                + "image_name,source_hash,is_lod,is_representative");

            foreach (NebulaTextureEntry entry in plan.Textures)
            {
                NebulaGlbImageOccurrence representative = Representative(entry);
                long each = entry.SourceByteLength;
                long total = entry.Occurrences.Sum(o => (long)o.SourceByteLength);

                foreach (NebulaGlbImageOccurrence occurrence in entry.Occurrences
                             .OrderBy(o => o.AssetPath, StringComparer.Ordinal)
                             .ThenBy(o => o.ImageIndex))
                {
                    builder.Append(Csv(entry.Id)).Append(',')
                        .Append(Csv(entry.Key.ToString())).Append(',')
                        .Append(Csv(entry.Key.PixelHash)).Append(',')
                        .Append(entry.Key.Width).Append(',')
                        .Append(entry.Key.Height).Append(',')
                        .Append(entry.Key.ColorSpace == NebulaTextureColorSpace.Srgb ? "sRGB" : "Linear").Append(',')
                        .Append(Csv(entry.Key.RoleSignature)).Append(',')
                        .Append(entry.Key.AlphaRequired ? "true" : "false").Append(',')
                        .Append(Csv(entry.MimeType)).Append(',')
                        .Append(entry.Occurrences.Count).Append(',')
                        .Append(entry.LodOccurrenceCount).Append(',')
                        .Append(entry.SourceHashes.Count).Append(',')
                        .Append(each).Append(',')
                        .Append(total).Append(',')
                        .Append(total - each).Append(',')
                        .Append((long)Rgba32WithMips(entry.Key.Width, entry.Key.Height) * entry.Occurrences.Count)
                        .Append(',')
                        .Append(BlockCompressedWithMips(entry.Key.Width, entry.Key.Height)).Append(',')
                        .Append(Csv(occurrence.AssetPath)).Append(',')
                        .Append(occurrence.ImageIndex).Append(',')
                        .Append(Csv(occurrence.ImageName)).Append(',')
                        .Append(Csv(occurrence.SourceHash)).Append(',')
                        .Append(occurrence.IsLod ? "true" : "false").Append(',')
                        .Append(ReferenceEquals(occurrence, representative) ? "true" : "false")
                        .AppendLine();
                }
            }
            return builder.ToString();
        }

        private static string BuildDedupPlanMarkdown(NebulaVisualTexturePlan plan)
        {
            long embedded = plan.EmbeddedSourceBytes;
            long deduped = plan.Textures.Sum(t => (long)t.SourceByteLength);
            long today = plan.Textures.Sum(
                t => (long)Rgba32WithMips(t.Key.Width, t.Key.Height) * t.Occurrences.Count);
            long after = plan.Textures.Sum(ImportedBytes);

            var builder = new StringBuilder();
            builder.AppendLine("# Visual texture dedup plan");
            builder.AppendLine();
            builder.AppendLine(
                "Generated by `NebulaVisualTextureOptimizer` (pipeline version "
                + NebulaVisualTextureManifest.PipelineVersion + "). Every number here is measured from the "
                + "GLB containers, not estimated.");
            builder.AppendLine();
            builder.AppendLine("## Dedup key");
            builder.AppendLine();
            builder.AppendLine("```");
            builder.AppendLine("sha256(decoded RGBA32) | width | height | colorSpace | roleSignature | alphaRequired");
            builder.AppendLine("```");
            builder.AppendLine();
            builder.AppendLine(
                "The file name is not part of it. Identical pixels in different roles stay separate assets; "
                + "identical pixels in different colour spaces stay separate assets. Only the alpha "
                + "requirement is unioned across a group rather than splitting it, because one RGBA asset "
                + "serves an OPAQUE and a BLEND user correctly while two would just be the same pixels twice.");
            builder.AppendLine();
            builder.AppendLine("## Measured");
            builder.AppendLine();
            builder.AppendLine("| | |");
            builder.AppendLine("|---|---:|");
            builder.AppendLine("| GLB files scanned | " + plan.GlbCount + " |");
            builder.AppendLine("| Embedded image occurrences | " + plan.OccurrenceCount + " |");
            builder.AppendLine("| Distinct decoded pixel hashes | " + plan.UniquePixelHashCount + " |");
            builder.AppendLine("| Distinct source byte hashes | " + plan.UniqueSourceHashCount + " |");
            builder.AppendLine("| **Unique texture assets (composite key)** | **" + plan.Textures.Count + "** |");
            builder.AppendLine("| glTF material definitions | " + plan.MaterialDefinitionCount + " |");
            builder.AppendLine("| Unique generated materials | " + plan.Materials.Count + " |");
            builder.AppendLine("| Embedded source bytes | " + Mb(embedded) + " |");
            builder.AppendLine("| Deduplicated source bytes | " + Mb(deduped) + " |");
            builder.AppendLine("| Player texture memory today (RGBA32 + mips, every copy) | " + Mb(today) + " |");
            builder.AppendLine("| Player texture memory after (BC7/BC5 + mips, deduplicated) | " + Mb(after) + " |");
            builder.AppendLine();

            builder.AppendLine("## Per role");
            builder.AppendLine();
            builder.AppendLine("| Role signature | Colour space | Assets | Occurrences | Import |");
            builder.AppendLine("|---|---|---:|---:|---|");
            foreach (var group in plan.Textures
                         .GroupBy(t => t.Key.RoleSignature, StringComparer.Ordinal)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                NebulaTextureEntry first = group.First();
                builder.AppendLine("| " + group.Key
                    + " | " + (first.Key.ColorSpace == NebulaTextureColorSpace.Srgb ? "sRGB" : "Linear")
                    + " | " + group.Count()
                    + " | " + group.Sum(t => t.Occurrences.Count)
                    + " | " + first.Import.TextureType + ", " + first.Import.Format + " |");
            }
            builder.AppendLine();

            builder.AppendLine("## Most duplicated");
            builder.AppendLine();
            builder.AppendLine("| Texture | Dimensions | Role | Copies | Source bytes reclaimed |");
            builder.AppendLine("|---|---|---|---:|---:|");
            foreach (NebulaTextureEntry entry in plan.Textures
                         .OrderByDescending(t => t.Occurrences.Count)
                         .ThenBy(t => t.Id, StringComparer.Ordinal)
                         .Take(15))
                builder.AppendLine("| " + entry.Id
                    + " | " + entry.Key.Width + "x" + entry.Key.Height
                    + " | " + entry.Key.RoleSignature
                    + " | " + entry.Occurrences.Count
                    + " | " + Mb(entry.Occurrences.Sum(o => (long)o.SourceByteLength) - entry.SourceByteLength)
                    + " |");
            builder.AppendLine();

            if (plan.Blockers.Count > 0)
            {
                builder.AppendLine("## Blockers");
                builder.AppendLine();
                foreach (string blocker in plan.Blockers) builder.AppendLine("- " + blocker);
                builder.AppendLine();
            }

            builder.AppendLine("## Warnings");
            builder.AppendLine();
            if (plan.Warnings.Count == 0) builder.AppendLine("None.");
            else
                foreach (string warning in plan.Warnings.Distinct().OrderBy(w => w, StringComparer.Ordinal))
                    builder.AppendLine("- " + warning);
            builder.AppendLine();

            builder.AppendLine("## What this does not do");
            builder.AppendLine();
            builder.AppendLine(
                "- It does not move, rewrite or delete a single GLB, and it does not touch a prefab.");
            builder.AppendLine(
                "- **Nothing above is saved until the prefabs are rebound.** The visual prefabs are prefab "
                + "instances of the GLB assets, so every renderer still points at the GLB's own material, "
                + "which still points at the RGBA32 sub-asset texture glTFast built. Those textures are "
                + "reachable from a prefab under `Assets/Resources/`, so they are still in the player. The "
                + "generated materials here are what breaks that chain; binding renderers onto them is the "
                + "other half of the change and is deliberately not done in this pass.");
            return builder.ToString();
        }

        // ---- helpers ---------------------------------------------------------

        public static string Summary(NebulaVisualTexturePlan plan) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "GLBs {0}, occurrences {1}, distinct pixel hashes {2}, distinct source hashes {3}, "
                + "unique texture assets {4}, glTF materials {5} -> {6} generated, blockers {7}, warnings {8}",
                plan.GlbCount, plan.OccurrenceCount, plan.UniquePixelHashCount, plan.UniqueSourceHashCount,
                plan.Textures.Count, plan.MaterialDefinitionCount, plan.Materials.Count,
                plan.Blockers.Count, plan.Warnings.Count);

        /// <summary>
        /// Bytes one generated texture will occupy once imported, honouring the
        /// handful the quality gate sent back to an uncompressed format. Projecting
        /// every asset at one byte per texel would understate the result by the
        /// exact amount those exceptions cost, which is the kind of rounding that
        /// makes a report untrustworthy for the sake of half a megabyte.
        /// </summary>
        private static long ImportedBytes(NebulaTextureEntry entry) =>
            string.Equals(entry.Import.Format, "RGBA32", StringComparison.Ordinal)
                ? Rgba32WithMips(entry.Key.Width, entry.Key.Height)
                : BlockCompressedWithMips(entry.Key.Width, entry.Key.Height);

        /// <summary>Bytes an RGBA32 texture with a full mip chain occupies — what the player pays today.</summary>
        public static long Rgba32WithMips(int width, int height) => WithMips(width, height, 4f);

        /// <summary>Bytes a BC7 or BC5 texture with a full mip chain occupies. Both are one byte per texel.</summary>
        public static long BlockCompressedWithMips(int width, int height) => WithMips(width, height, 1f);

        private static long WithMips(int width, int height, float bytesPerTexel)
        {
            long total = 0;
            while (width >= 1 && height >= 1)
            {
                total += (long)(width * height * bytesPerTexel);
                if (width == 1 && height == 1) break;
                width = Mathf.Max(1, width / 2);
                height = Mathf.Max(1, height / 2);
            }
            return total;
        }

        private static string Mb(long bytes) =>
            (bytes / 1048576.0).ToString("0.00", CultureInfo.InvariantCulture) + " MB";

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\n' }) < 0
                ? value
                : "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        internal static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

        internal static string ToAbsolute(string assetPath) => Path.Combine(ProjectRoot, assetPath);

        internal static string ToAssetPath(string absolute)
        {
            string root = ProjectRoot.Replace('\\', '/');
            string normalized = absolute.Replace('\\', '/');
            return normalized.StartsWith(root, StringComparison.Ordinal)
                ? normalized.Substring(root.Length).TrimStart('/')
                : normalized;
        }
    }
}
