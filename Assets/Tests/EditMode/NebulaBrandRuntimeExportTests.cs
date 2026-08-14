using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using Nebula.UI.Brand;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The 38 runtime exports, checked against the delivery's own manifest.
    /// <para>
    /// This is a <b>test and editor gate only</b>. Nothing at runtime hashes these
    /// files: a player recomputing 38 SHA-256 digests over 30MB on every launch would
    /// pay a visible startup cost to learn something that cannot change after the
    /// build is made.
    /// </para>
    /// <para>
    /// The manifest names the delivery's own filenames; the project stages them under
    /// its own naming convention. The mapping below is therefore part of the
    /// assertion — it is what proves the file that landed at
    /// <c>brand.keyart.clean.ultrawide.png</c> really is the authored 3440x1440
    /// export and not a resized copy of something else.
    /// </para>
    /// </summary>
    public class NebulaBrandRuntimeExportTests
    {
        private const string ManifestPath =
            NebulaBrandAssetCatalog.BrandSourceFolder + "/RUNTIME-BRAND-EXPORT-MANIFEST.json";

        private const string BrandRoot = "Assets/Resources/UI/Brand";

        /// <summary>Delivery filename to the path it was staged at.</summary>
        private static string ProjectPathFor(string deliveryFile)
        {
            if (deliveryFile.StartsWith("Nebula.iconset/"))
                return $"{NebulaBrandAssetCatalog.MacIconsetFolder}/{Path.GetFileName(deliveryFile)}";

            switch (deliveryFile)
            {
                case "nebula-emblem-flat.svg":
                    return NebulaBrandAssetCatalog.NativeAssetFolder + "/nebula-emblem-flat.svg";
                case "nebula-windows.ico": return NebulaBrandAssetCatalog.WindowsIcoPath;
                case "Nebula.icns": return NebulaBrandAssetCatalog.MacIcnsPath;
                case "nebula-splash-1920x1080.png":
                    return NebulaBrandAssetCatalog.SplashAssetPath(
                        NebulaBrandAssetCatalog.SplashAspect.SixteenByNine);
                case "nebula-splash-2560x1600.png":
                    return NebulaBrandAssetCatalog.SplashAssetPath(
                        NebulaBrandAssetCatalog.SplashAspect.SixteenByTen);
                case "nebula-key-art-clean-final-1280x720.png":
                    return BrandRoot + "/KeyArt/brand.keyart.clean.low.png";
                case "nebula-key-art-clean-final-1920x1080.png":
                    return BrandRoot + "/KeyArt/brand.keyart.clean.hd.png";
                case "nebula-key-art-clean-final-2560x1600.png":
                    return BrandRoot + "/KeyArt/brand.keyart.clean.wide16x10.png";
                case "nebula-key-art-clean-final-3440x1440.png":
                    return BrandRoot + "/KeyArt/brand.keyart.clean.ultrawide.png";
                case "nebula-loading-depth-reduced-unity.json":
                    return BrandRoot + "/Loading/brand.loading.reduced.unity.json";
            }

            if (deliveryFile.StartsWith("nebula-loading-depth-reduced-spritesheet-"))
                return $"{BrandRoot}/Loading/brand.loading.reduced.{Size(deliveryFile)}.png";
            if (deliveryFile.StartsWith("nebula-logo-effects-transparent-"))
                return $"{BrandRoot}/Logo/brand.logo.effects.{Size(deliveryFile)}.png";
            if (deliveryFile.StartsWith("nebula-logo-flat-transparent-"))
                return $"{BrandRoot}/Logo/brand.logo.flat.{Size(deliveryFile)}.png";
            if (deliveryFile.StartsWith("nebula-emblem-effects-transparent-"))
                return $"{BrandRoot}/Emblem/brand.emblem.effects.{Size(deliveryFile)}.png";
            if (deliveryFile.StartsWith("nebula-emblem-flat-transparent-"))
                return $"{BrandRoot}/Emblem/brand.emblem.flat.{Size(deliveryFile)}.png";

            return null;
        }

        private static string Size(string file)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            return stem.Substring(stem.LastIndexOf('-') + 1);
        }

        private static List<ManifestEntry> ReadManifest()
        {
            Assert.IsTrue(File.Exists(ManifestPath),
                "the delivery manifest must ship with the project so this gate runs on a clean clone");

            var entries = new List<ManifestEntry>(38);
            string json = File.ReadAllText(ManifestPath);

            // Hand-parsed rather than through JsonUtility, which needs a serializable
            // mirror class per nesting level to read four fields per entry.
            int at = json.IndexOf("\"assets\"", System.StringComparison.Ordinal);
            Assert.Greater(at, -1, "the manifest has no assets array");

            while (true)
            {
                at = json.IndexOf("\"file\"", at, System.StringComparison.Ordinal);
                if (at < 0) break;
                entries.Add(new ManifestEntry
                {
                    File = StringField(json, ref at, "file"),
                    Sha256 = StringField(json, ref at, "sha256"),
                    Width = IntField(json, at, "width"),
                    Height = IntField(json, at, "height"),
                    Alpha = StringField(json, ref at, "alpha"),
                });
            }
            return entries;
        }

        private struct ManifestEntry
        {
            public string File, Sha256, Alpha;
            public int Width, Height;
        }

        private static string StringField(string json, ref int from, string key)
        {
            int at = json.IndexOf($"\"{key}\"", from, System.StringComparison.Ordinal);
            if (at < 0) return null;
            int open = json.IndexOf('"', json.IndexOf(':', at) + 1);
            int close = json.IndexOf('"', open + 1);
            from = close;
            return json.Substring(open + 1, close - open - 1);
        }

        private static int IntField(string json, int from, string key)
        {
            int at = json.IndexOf($"\"{key}\"", from, System.StringComparison.Ordinal);
            if (at < 0) return -1;
            int start = json.IndexOf(':', at) + 1;
            while (start < json.Length && !char.IsDigit(json[start])) start++;
            int end = start;
            while (end < json.Length && char.IsDigit(json[end])) end++;
            return end > start ? int.Parse(json.Substring(start, end - start)) : -1;
        }

        // ---- the gate --------------------------------------------------------

        [Test]
        public void TheManifestDescribesThirtyEightAssets()
        {
            Assert.AreEqual(38, ReadManifest().Count);
        }

        /// <summary>
        /// Every export landed in the project, byte for byte.
        /// <para>
        /// SHA-256 over the staged copy, not the delivery folder. The delivery is a
        /// read-only source that is deliberately not committed, so checking it would
        /// pass on this machine and be unrunnable anywhere else — and it would not
        /// answer the question that matters, which is whether the right bytes are in
        /// the build.
        /// </para>
        /// </summary>
        [Test]
        public void EveryExportIsStagedByteForByte()
        {
            var problems = new List<string>();
            using SHA256 sha = SHA256.Create();

            foreach (ManifestEntry entry in ReadManifest())
            {
                string path = ProjectPathFor(entry.File);
                if (path == null) { problems.Add($"{entry.File}: no staging path is defined"); continue; }
                if (!File.Exists(path)) { problems.Add($"{entry.File}: not staged at {path}"); continue; }

                string actual = System.BitConverter
                    .ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty).ToLowerInvariant();
                if (actual != entry.Sha256)
                    problems.Add($"{entry.File}: staged copy at {path} does not match the delivered bytes");
            }

            Assert.IsEmpty(problems, string.Join("\n  ", problems));
        }

        /// <summary>
        /// Imported dimensions match the manifest.
        /// <para>
        /// Separate from the hash check on purpose: identical bytes on disk can still
        /// reach the runtime at the wrong size, because an importer with a max-size
        /// cap resamples on the way in.
        /// </para>
        /// </summary>
        [Test]
        public void EveryImportedTextureKeepsItsDeliveredDimensions()
        {
            var problems = new List<string>();
            foreach (ManifestEntry entry in ReadManifest())
            {
                if (!entry.File.EndsWith(".png")) continue;
                if (entry.File.StartsWith("Nebula.iconset/")) continue;   // not imported as game textures

                string path = ProjectPathFor(entry.File);
                var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) { problems.Add($"{entry.File}: not imported from {path}"); continue; }

                if (texture.width != entry.Width || texture.height != entry.Height)
                    problems.Add($"{entry.File}: imported {texture.width}x{texture.height}, "
                                 + $"delivered {entry.Width}x{entry.Height}");
            }

            Assert.IsEmpty(problems, string.Join("\n  ", problems));
        }

        /// <summary>
        /// Straight-alpha exports carry an alpha channel and no colour under it.
        /// <para>
        /// The delivery sanitises fully transparent pixels to RGB 0,0,0 so bilinear
        /// filtering cannot drag a colour fringe out of them. This reads the source
        /// PNG into a temporary readable texture — the imported assets are
        /// deliberately not readable, and sampling them back through their platform
        /// format would measure the compressor instead of the artwork.
        /// </para>
        /// </summary>
        [Test]
        public void StraightAlphaExportsHaveNoColourUnderTransparentPixels()
        {
            var problems = new List<string>();
            foreach (ManifestEntry entry in ReadManifest())
            {
                if (entry.Alpha != "straight" || !entry.File.EndsWith(".png")) continue;
                if (entry.File.StartsWith("Nebula.iconset/")) continue;
                // Sampling every large plate would make this a minute-long test; the
                // marks are where a halo would actually be visible.
                if (!entry.File.Contains("logo") && !entry.File.Contains("emblem")) continue;

                string path = ProjectPathFor(entry.File);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!texture.LoadImage(File.ReadAllBytes(path)))
                    {
                        problems.Add($"{entry.File}: could not be decoded");
                        continue;
                    }

                    int leaks = 0;
                    foreach (Color32 pixel in texture.GetPixels32())
                        if (pixel.a == 0 && (pixel.r != 0 || pixel.g != 0 || pixel.b != 0)) leaks++;

                    if (leaks > 0) problems.Add($"{entry.File}: {leaks} transparent pixels carry colour");
                }
                finally { Object.DestroyImmediate(texture); }
            }

            Assert.IsEmpty(problems, string.Join("\n  ", problems));
        }
    }
}
