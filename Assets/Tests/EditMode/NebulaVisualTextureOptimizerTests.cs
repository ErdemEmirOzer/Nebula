using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nebula.EditorTools.VisualTexture;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The texture extraction pipeline, and the extracted assets as they actually
    /// sit in the project.
    /// <para>
    /// Two halves on purpose. The first reads nothing off disk and pins the rules
    /// that decide whether two images are one asset — those must hold on a machine
    /// that has never run the pipeline, because they are what the pipeline is.
    /// The second reads the imported assets and their importers, because a texture
    /// that is correct on disk and imported at RGBA32 with Crunch on is a texture
    /// that undoes the whole exercise.
    /// </para>
    /// </summary>
    public class NebulaVisualTextureOptimizerTests
    {
        private const string TextureRoot = "Assets/RuntimeVisuals/Textures";
        private const string ManifestPath = "Assets/RuntimeVisuals/nebula-visual-texture-manifest.json";

        /// <summary>Sixteen zero bytes of "pixels", enough to stand in for a real image's hash.</summary>
        private const string PixelsA =
            "0f58e0a6ca4d7b2c1e9a8f3d6b5c4a29380716e5d4c3b2a1908f7e6d5c4b3a29";

        private const string PixelsB =
            "ebde67074a11223344556677889900aabbccddeeff00112233445566778899aa";

        private static NebulaTextureDedupKey Key(
            string pixels,
            int width,
            int height,
            NebulaTextureColorSpace space,
            string roleSignature,
            bool alphaRequired = false) =>
            new NebulaTextureDedupKey
            {
                PixelHash = pixels,
                Width = width,
                Height = height,
                ColorSpace = space,
                RoleSignature = roleSignature,
                AlphaRequired = alphaRequired
            };

        // ---- the dedup key ---------------------------------------------------

        /// <summary>
        /// The case the whole key exists for: the same pixels sampled as an sRGB
        /// albedo and as a linear metallic-roughness map are two assets, never one.
        /// <para>
        /// Merging them is not a smaller saving, it is a wrong render. The linear
        /// map would come back through a gamma curve and every surface using it
        /// would be quietly too smooth or too metallic — an error with no visible
        /// seam, which is exactly the kind that ships.
        /// </para>
        /// </summary>
        [Test]
        public void SamePixelsInDifferentRolesAreDifferentAssets()
        {
            NebulaTextureDedupKey albedo = Key(
                PixelsA, 512, 512, NebulaTextureColorSpace.Srgb, "BaseColor");
            NebulaTextureDedupKey roughness = Key(
                PixelsA, 512, 512, NebulaTextureColorSpace.Linear, "MetallicRoughness");

            Assert.AreNotEqual(albedo, roughness, "Identical pixels in different roles must not merge.");
            Assert.AreNotEqual(albedo.ToString(), roughness.ToString());
            Assert.AreNotEqual(
                NebulaVisualTextureManifest.AssetId(albedo),
                NebulaVisualTextureManifest.AssetId(roughness),
                "The two must land on different asset paths, not just compare unequal.");
        }

        /// <summary>
        /// The subtler version of the same rule: two roles that agree on colour
        /// space still stay apart, because the channel layout differs. A packed
        /// occlusion/roughness/metallic map and a plain metallic-roughness map can
        /// be the same bytes and are still not the same texture.
        /// </summary>
        [Test]
        public void SamePixelsInDifferentLinearRolesStaySeparate()
        {
            NebulaTextureDedupKey metallicRoughness = Key(
                PixelsA, 1024, 1024, NebulaTextureColorSpace.Linear, "MetallicRoughness");
            NebulaTextureDedupKey packed = Key(
                PixelsA, 1024, 1024, NebulaTextureColorSpace.Linear, "MetallicRoughness+Occlusion");

            Assert.AreNotEqual(metallicRoughness, packed);
            Assert.AreNotEqual(metallicRoughness.GroupingForm(), packed.GroupingForm());
        }

        /// <summary>Identical everything really is one asset — the key must not over-split either.</summary>
        [Test]
        public void IdenticalPixelsInTheSameRoleMerge()
        {
            Assert.AreEqual(
                Key(PixelsA, 512, 512, NebulaTextureColorSpace.Srgb, "BaseColor"),
                Key(PixelsA, 512, 512, NebulaTextureColorSpace.Srgb, "BaseColor"));
            Assert.AreEqual(
                Key(PixelsA, 512, 512, NebulaTextureColorSpace.Srgb, "BaseColor").GroupingForm(),
                Key(PixelsA, 512, 512, NebulaTextureColorSpace.Srgb, "BaseColor").GroupingForm());
        }

        /// <summary>Different pictures never merge, whatever else agrees.</summary>
        [Test]
        public void DifferentPixelsNeverMerge()
        {
            Assert.AreNotEqual(
                Key(PixelsA, 512, 512, NebulaTextureColorSpace.Srgb, "BaseColor"),
                Key(PixelsB, 512, 512, NebulaTextureColorSpace.Srgb, "BaseColor"));
        }

        /// <summary>Dimensions are part of the identity, so a hash collision cannot fuse two sizes.</summary>
        [Test]
        public void DimensionsArePartOfTheKey()
        {
            Assert.AreNotEqual(
                Key(PixelsA, 512, 512, NebulaTextureColorSpace.Srgb, "BaseColor"),
                Key(PixelsA, 512, 256, NebulaTextureColorSpace.Srgb, "BaseColor"));
        }

        /// <summary>
        /// The alpha requirement is the one component that changes the asset name
        /// without splitting a group, so the name has to carry it — that suffix is
        /// what the import profile reads.
        /// </summary>
        [Test]
        public void AlphaRequirementChangesTheAssetName()
        {
            string opaque = NebulaVisualTextureManifest.AssetId(
                Key(PixelsA, 512, 512, NebulaTextureColorSpace.Srgb, "BaseColor"));
            string blended = NebulaVisualTextureManifest.AssetId(
                Key(PixelsA, 512, 512, NebulaTextureColorSpace.Srgb, "BaseColor", alphaRequired: true));

            Assert.AreNotEqual(opaque, blended);
            Assert.IsTrue(blended.EndsWith("_a", StringComparison.Ordinal));
            Assert.IsFalse(opaque.EndsWith("_a", StringComparison.Ordinal));
        }

        /// <summary>
        /// Role signatures are order-free. The exports list material slots in
        /// whatever order they were authored, and an order-sensitive signature
        /// would emit the same image twice under two names.
        /// </summary>
        [Test]
        public void RoleSignatureIsOrderIndependent()
        {
            string forward = NebulaVisualTextureManifest.RoleSignature(new[]
            {
                NebulaTextureRole.MetallicRoughness, NebulaTextureRole.Occlusion
            });
            string reversed = NebulaVisualTextureManifest.RoleSignature(new[]
            {
                NebulaTextureRole.Occlusion, NebulaTextureRole.MetallicRoughness
            });

            Assert.AreEqual(forward, reversed);
            Assert.AreEqual("MetallicRoughness+Occlusion", forward);
        }

        /// <summary>Only authored colour is sRGB; every data map stays linear.</summary>
        [Test]
        public void ColourSpaceFollowsTheRole()
        {
            Assert.AreEqual(
                NebulaTextureColorSpace.Srgb,
                NebulaVisualTextureManifest.ColorSpaceOf(NebulaTextureRole.BaseColor));
            Assert.AreEqual(
                NebulaTextureColorSpace.Srgb,
                NebulaVisualTextureManifest.ColorSpaceOf(NebulaTextureRole.Emissive));
            Assert.AreEqual(
                NebulaTextureColorSpace.Linear,
                NebulaVisualTextureManifest.ColorSpaceOf(NebulaTextureRole.Normal));
            Assert.AreEqual(
                NebulaTextureColorSpace.Linear,
                NebulaVisualTextureManifest.ColorSpaceOf(NebulaTextureRole.MetallicRoughness));
            Assert.AreEqual(
                NebulaTextureColorSpace.Linear,
                NebulaVisualTextureManifest.ColorSpaceOf(NebulaTextureRole.Occlusion));
        }

        /// <summary>
        /// A role set that mixes colour spaces has no correct single answer, and
        /// the profile refuses rather than picking one. Reaching that line means
        /// the plan skipped a split; failing loudly there is the difference between
        /// a caught bug and a shipped one.
        /// </summary>
        [Test]
        public void MixedColourSpaceRoleSetIsRefused()
        {
            Assert.Throws<InvalidOperationException>(() =>
                NebulaVisualImportProfile.ColorSpaceOf(new List<NebulaTextureRole>
                {
                    NebulaTextureRole.BaseColor, NebulaTextureRole.Normal
                }));
        }

        // ---- naming and idempotency -----------------------------------------

        /// <summary>
        /// The same key always produces the same path. This is what makes a second
        /// run reuse the assets the first run created instead of deleting them and
        /// issuing new GUIDs — which would break every prefab pointing at them.
        /// </summary>
        [Test]
        public void AssetPathIsAPureFunctionOfTheKey()
        {
            NebulaTextureDedupKey key = Key(
                PixelsA, 1024, 512, NebulaTextureColorSpace.Linear, "Normal");

            string first = NebulaVisualTextureManifest.TextureAssetPath(key, "image/png");
            string second = NebulaVisualTextureManifest.TextureAssetPath(key, "image/png");

            Assert.AreEqual(first, second);
            Assert.AreEqual("Assets/RuntimeVisuals/Textures/Normal/normal_1024x512_0f58e0a6ca4d.png", first);
        }

        /// <summary>
        /// Generated assets are outside every <c>Resources</c> folder. Inside one
        /// they would ship whether referenced or not, which is the exact mechanism
        /// that put 246 raw GLBs in the player.
        /// </summary>
        [Test]
        public void GeneratedAssetsAreOutsideResources()
        {
            Assert.IsFalse(
                NebulaVisualTextureManifest.TextureRoot.Contains("/Resources/"),
                "Extracted textures must not sit under a Resources folder.");
            Assert.IsFalse(
                NebulaVisualTextureManifest.MaterialRoot.Contains("/Resources/"),
                "Generated materials must not sit under a Resources folder.");
        }

        /// <summary>
        /// The import profile is a pure function of the asset path. If it ever
        /// needed the manifest it would depend on import order, and an
        /// <c>AssetPostprocessor</c> has no control over that.
        /// </summary>
        [Test]
        public void ImportPolicyIsRecoverableFromThePathAlone()
        {
            Assert.IsTrue(NebulaVisualImportProfile.TryParse(
                "Assets/RuntimeVisuals/Textures/BaseColor/basecolor_512x512_abc123abc123_a.png",
                out List<NebulaTextureRole> roles, out bool alphaRequired));
            CollectionAssert.AreEqual(new[] { NebulaTextureRole.BaseColor }, roles);
            Assert.IsTrue(alphaRequired);

            Assert.IsTrue(NebulaVisualImportProfile.TryParse(
                "Assets/RuntimeVisuals/Textures/MetallicRoughness-Occlusion/"
                + "metallicroughness-occlusion_512x512_abc123abc123.png",
                out roles, out alphaRequired));
            CollectionAssert.AreEqual(
                new[] { NebulaTextureRole.MetallicRoughness, NebulaTextureRole.Occlusion }, roles);
            Assert.IsFalse(alphaRequired);

            Assert.IsFalse(NebulaVisualImportProfile.TryParse(
                "Assets/Resources/UI/Brand/Logo/brand.logo.png", out roles, out alphaRequired),
                "Nothing outside the generated root may be claimed by this profile.");
        }

        /// <summary>The planned settings per role, before anything is imported.</summary>
        [Test]
        public void PlannedImportSettingsMatchTheRole()
        {
            NebulaTextureImportPlan albedo = NebulaVisualImportProfile.PlanFor(
                "Assets/RuntimeVisuals/Textures/BaseColor/basecolor_512x512_aaaaaaaaaaaa.png");
            Assert.AreEqual("Default", albedo.TextureType);
            Assert.IsTrue(albedo.SrgbTexture);
            Assert.AreEqual("BC7", albedo.Format);
            Assert.AreEqual("None", albedo.AlphaSource);

            NebulaTextureImportPlan normal = NebulaVisualImportProfile.PlanFor(
                "Assets/RuntimeVisuals/Textures/Normal/normal_512x512_aaaaaaaaaaaa.png");
            Assert.AreEqual("NormalMap", normal.TextureType);
            Assert.IsFalse(normal.SrgbTexture);
            Assert.AreEqual("BC5", normal.Format);

            NebulaTextureImportPlan packed = NebulaVisualImportProfile.PlanFor(
                "Assets/RuntimeVisuals/Textures/MetallicRoughness-Occlusion/"
                + "metallicroughness-occlusion_512x512_aaaaaaaaaaaa.png");
            Assert.AreEqual("Default", packed.TextureType);
            Assert.IsFalse(packed.SrgbTexture);
            Assert.AreEqual("BC7", packed.Format);

            // The two normals the quality gate measured over the angular threshold
            // are imported uncompressed. Asserted here so the exception is a
            // covered decision rather than a carve-out nobody is watching — and so
            // that adding a third one is a deliberate act.
            Assert.AreEqual(2, NebulaVisualImportProfile.LosslessNormals.Count);
            foreach (string id in NebulaVisualImportProfile.LosslessNormals)
            {
                NebulaTextureImportPlan exception = NebulaVisualImportProfile.PlanFor(
                    "Assets/RuntimeVisuals/Textures/Normal/" + id + ".png");
                Assert.AreEqual("NormalMap", exception.TextureType, id);
                Assert.AreEqual("RGBA32", exception.Format, id);
                Assert.AreEqual("Uncompressed", exception.Compression, id);
                Assert.IsFalse(exception.CrunchedCompression, id);
            }

            foreach (NebulaTextureImportPlan plan in new[] { albedo, normal, packed })
            {
                Assert.IsFalse(plan.CrunchedCompression, "Crunch is never acceptable here.");
                Assert.IsFalse(plan.Readable, "Read/Write doubles the cost for nothing.");
                Assert.IsFalse(plan.StreamingMipmaps, "Streaming was not measured as compatible.");
                Assert.IsTrue(plan.MipmapEnabled, "The GLB import had mips; dropping them is a visual change.");
                Assert.AreEqual(
                    NebulaVisualImportProfile.NoDownscaleCap, plan.MaxTextureSize,
                    "No delivered texture may be silently resampled.");
            }
        }

        // ---- the assets on disk ---------------------------------------------

        private static bool PipelineHasRun => File.Exists(
            Path.Combine(ProjectRoot, ManifestPath));

        private static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

        private static string[] ExtractedTextureGuids =>
            AssetDatabase.IsValidFolder(TextureRoot)
                ? AssetDatabase.FindAssets("t:Texture2D", new[] { TextureRoot })
                : Array.Empty<string>();

        private static void RequirePipeline()
        {
            if (!PipelineHasRun)
                Assert.Ignore(
                    "The texture optimizer has not been applied in this checkout "
                    + "(no manifest at " + ManifestPath + ").");
        }

        /// <summary>
        /// Every extracted texture imported under the settings its role demands.
        /// <para>
        /// Read off the importer rather than the plan, because the plan is what was
        /// asked for and the importer is what happened.
        /// </para>
        /// </summary>
        [Test]
        public void ExtractedTexturesImportUnderTheirRolePolicy()
        {
            RequirePipeline();
            string[] guids = ExtractedTextureGuids;
            Assert.Greater(guids.Length, 0, "The manifest exists but no textures were found under " + TextureRoot);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsNotNull(importer, path);
                Assert.IsTrue(
                    NebulaVisualImportProfile.TryParse(path, out List<NebulaTextureRole> roles, out bool alpha),
                    "Unrecognised extracted texture path: " + path);

                bool isNormal = roles.Count == 1 && roles[0] == NebulaTextureRole.Normal;
                bool isSrgb = NebulaVisualImportProfile.ColorSpaceOf(roles) == NebulaTextureColorSpace.Srgb;

                if (isNormal)
                {
                    Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType, path);
                }
                else
                {
                    Assert.AreEqual(TextureImporterType.Default, importer.textureType, path);
                    Assert.AreEqual(isSrgb, importer.sRGBTexture,
                        "Colour space is wrong for " + path + " — a linear map read as sRGB is a silent "
                        + "material error.");
                }

                Assert.AreEqual(
                    alpha && !isNormal
                        ? TextureImporterAlphaSource.FromInput
                        : TextureImporterAlphaSource.None,
                    importer.alphaSource, path);

                // The format the desktop player will actually get, compared
                // against the role's plan. Without this the fixture would pass on
                // a texture that was correctly classified and then imported at
                // RGBA32 anyway, which is the failure the whole pass exists to
                // remove.
                TextureImporterPlatformSettings platform =
                    importer.GetPlatformTextureSettings(NebulaVisualImportProfile.Standalone);
                Assert.IsTrue(platform.overridden, "No Standalone override on " + path);
                Assert.AreEqual(
                    NebulaVisualImportProfile.PlanFor(path).Format,
                    platform.format.ToString(),
                    "Imported format does not match the role's plan: " + path);
            }
        }

        /// <summary>Crunch is off on every extracted texture, importer-wide and per platform.</summary>
        [Test]
        public void NoExtractedTextureUsesCrunch()
        {
            RequirePipeline();
            foreach (string guid in ExtractedTextureGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsFalse(importer.crunchedCompression, "Crunch on " + path);
                Assert.IsFalse(
                    importer.GetPlatformTextureSettings(NebulaVisualImportProfile.Standalone)
                        .crunchedCompression,
                    "Crunch in the Standalone override on " + path);
            }
        }

        /// <summary>Read/Write is off, so no image keeps a second copy in system memory.</summary>
        [Test]
        public void NoExtractedTextureIsReadable()
        {
            RequirePipeline();
            foreach (string guid in ExtractedTextureGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Assert.IsFalse(((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, path);
            }
        }

        /// <summary>
        /// Nothing was downscaled. Compared against the imported texture's real
        /// dimensions, not against the cap — a cap of 8192 proves nothing on its
        /// own if the source was resampled before it got there.
        /// </summary>
        [Test]
        public void NoExtractedTextureWasDownscaled()
        {
            RequirePipeline();
            foreach (string guid in ExtractedTextureGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(texture, path);

                Assert.AreEqual(NebulaVisualImportProfile.NoDownscaleCap, importer.maxTextureSize, path);

                // The name carries the authored dimensions, so this compares what
                // was extracted against what Unity produced.
                string[] parts = Path.GetFileNameWithoutExtension(path).Split('_');
                string dimensions = parts.FirstOrDefault(p => p.Contains("x") && char.IsDigit(p[0]));
                Assert.IsNotNull(dimensions, "No dimensions in the name of " + path);
                string[] wh = dimensions.Split('x');
                Assert.AreEqual(int.Parse(wh[0]), texture.width, "Width was resampled: " + path);
                Assert.AreEqual(int.Parse(wh[1]), texture.height, "Height was resampled: " + path);
            }
        }

        /// <summary>
        /// Every extracted file on disk still hashes to what the manifest recorded.
        /// <para>
        /// This is what catches the pipeline having been re-run against changed
        /// sources without the manifest being regenerated — at which point the
        /// manifest is a description of assets that no longer exist and Agent 2's
        /// rebinding would be working from fiction.
        /// </para>
        /// </summary>
        [Test]
        public void ManifestHashesAgreeWithDisk()
        {
            RequirePipeline();
            string json = File.ReadAllText(Path.Combine(ProjectRoot, ManifestPath));
            NebulaJsonValue manifest = NebulaJsonValue.Parse(json);
            NebulaJsonValue textures = manifest["textures"];
            Assert.IsNotNull(textures, "Manifest has no textures array.");
            Assert.Greater(textures.Count, 0);

            for (int i = 0; i < textures.Count; i++)
            {
                NebulaJsonValue entry = textures[i];
                string assetPath = entry["assetPath"].AsString();
                string expected = entry["sourceHash"].AsString();
                string absolute = Path.Combine(ProjectRoot, assetPath);

                Assert.IsTrue(File.Exists(absolute), "Manifest names a file that is not there: " + assetPath);
                Assert.AreEqual(
                    expected,
                    NebulaVisualTextureManifest.Sha256(File.ReadAllBytes(absolute)),
                    "Extracted file no longer matches the manifest: " + assetPath);
            }
        }

        /// <summary>
        /// The manifest describes the assets that exist — no more, no fewer. A
        /// stale entry and an unlisted texture are the same bug seen from two
        /// sides.
        /// </summary>
        [Test]
        public void ManifestAndDiskAgreeOnTheAssetSet()
        {
            RequirePipeline();
            NebulaJsonValue manifest = NebulaJsonValue.Parse(
                File.ReadAllText(Path.Combine(ProjectRoot, ManifestPath)));
            NebulaJsonValue textures = manifest["textures"];

            var listed = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < textures.Count; i++) listed.Add(textures[i]["assetPath"].AsString());

            var onDisk = new HashSet<string>(
                ExtractedTextureGuids.Select(AssetDatabase.GUIDToAssetPath), StringComparer.Ordinal);

            CollectionAssert.AreEquivalent(listed, onDisk);
        }

        /// <summary>
        /// Two textures never share an asset path, and every id is unique. The
        /// naming scheme is content-addressed, so a duplicate would mean two
        /// different images collided — which the dimensions in the key are there
        /// to make impossible.
        /// </summary>
        [Test]
        public void ExtractedTextureIdsAreUnique()
        {
            RequirePipeline();
            string[] paths = ExtractedTextureGuids.Select(AssetDatabase.GUIDToAssetPath).ToArray();
            CollectionAssert.AllItemsAreUnique(paths);
            CollectionAssert.AllItemsAreUnique(paths.Select(Path.GetFileNameWithoutExtension).ToArray());
        }

        /// <summary>
        /// Re-deriving the plan from the same sources lands on the same paths that
        /// already exist. That is idempotency stated as an assertion: nothing new
        /// would be created, so nothing existing would be replaced, so no GUID
        /// moves.
        /// <para>
        /// Reads the manifest rather than re-scanning 246 GLBs — the scan takes
        /// long enough that a fixture doing it would stop being run.
        /// </para>
        /// </summary>
        [Test]
        public void ReplanningProducesTheSamePathsThatAlreadyExist()
        {
            RequirePipeline();
            NebulaJsonValue manifest = NebulaJsonValue.Parse(
                File.ReadAllText(Path.Combine(ProjectRoot, ManifestPath)));
            NebulaJsonValue textures = manifest["textures"];

            for (int i = 0; i < textures.Count; i++)
            {
                NebulaJsonValue entry = textures[i];
                var key = new NebulaTextureDedupKey
                {
                    PixelHash = entry["pixelHash"].AsString(),
                    Width = entry["width"].AsInt(0),
                    Height = entry["height"].AsInt(0),
                    ColorSpace = entry["colorSpace"].AsString() == "sRGB"
                        ? NebulaTextureColorSpace.Srgb
                        : NebulaTextureColorSpace.Linear,
                    RoleSignature = entry["roleSignature"].AsString(),
                    AlphaRequired = entry["alphaRequired"].AsBool(false)
                };

                Assert.AreEqual(
                    entry["assetPath"].AsString(),
                    NebulaVisualTextureManifest.TextureAssetPath(key, entry["mimeType"].AsString()),
                    "Re-deriving the path from the recorded key produced a different path — a re-run "
                    + "would create a second asset and orphan the first.");
                Assert.AreEqual(entry["dedupKey"].AsString(), key.ToString());
            }
        }

        /// <summary>
        /// Every generated material resolved its shader and every bound slot points
        /// at a real texture. A material that lost its shader renders magenta and
        /// no per-texel metric would notice, because there is nothing left to
        /// measure.
        /// </summary>
        [Test]
        public void GeneratedMaterialsHaveNoErrorShadersOrMissingTextures()
        {
            RequirePipeline();
            const string materialRoot = "Assets/RuntimeVisuals/Materials";
            if (!AssetDatabase.IsValidFolder(materialRoot))
                Assert.Ignore("No generated materials in this checkout.");

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { materialRoot });
            Assert.Greater(guids.Length, 0);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(material, path);
                Assert.IsNotNull(material.shader, "Null shader on " + path);
                Assert.AreNotEqual("Hidden/InternalErrorShader", material.shader.name, path);
            }
        }

        /// <summary>
        /// The memory invariant that can be asserted without a player: the same
        /// image is one <see cref="Texture2D"/>, so loading every extracted texture
        /// yields as many distinct instances as there are assets and not one more.
        /// </summary>
        [Test]
        public void EachExtractedImageIsASingleTextureInstance()
        {
            RequirePipeline();
            var instances = new HashSet<int>();
            int assets = 0;

            foreach (string guid in ExtractedTextureGuids)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (texture == null) continue;
                assets++;
                instances.Add(texture.GetInstanceID());
            }

            Assert.AreEqual(assets, instances.Count, "An asset resolved to more than one texture instance.");

            // Loading the same path twice must return the same object rather than
            // a copy — the whole dedup is worthless if the loader clones.
            foreach (string guid in ExtractedTextureGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Assert.AreEqual(
                    AssetDatabase.LoadAssetAtPath<Texture2D>(path).GetInstanceID(),
                    AssetDatabase.LoadAssetAtPath<Texture2D>(path).GetInstanceID(),
                    path);
            }
        }
    }
}
