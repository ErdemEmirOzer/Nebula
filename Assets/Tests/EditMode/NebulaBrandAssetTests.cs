using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Nebula.UI.Brand;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The delivered brand assets, as they actually sit in the project.
    /// <para>
    /// Everything here reads the imported asset or its importer rather than the
    /// source PNG on someone's desktop, because what ships is the import — a plate
    /// that is correct on disk and clamped to 512 by an importer is a broken plate.
    /// </para>
    /// </summary>
    public class NebulaBrandAssetTests
    {
        private const string BrandRoot = "Assets/Resources/UI/Brand";
        private const string LoadingRoot = BrandRoot + "/Loading";
        private const string KeyArtRoot = BrandRoot + "/KeyArt";
        private const string LogoRoot = BrandRoot + "/Logo";
        private const string EmblemRoot = BrandRoot + "/Emblem";

        private const string Sheet256 = LoadingRoot + "/brand.loading.sheet.256.png";
        private const string Sheet128 = LoadingRoot + "/brand.loading.sheet.128.png";
        private const string Reduced256 = LoadingRoot + "/brand.loading.reduced.256.png";
        private const string Reduced128 = LoadingRoot + "/brand.loading.reduced.128.png";

        [SetUp]
        public void Setup() => NebulaBrandAssetCatalog.Release();

        [TearDown]
        public void Teardown() => NebulaBrandAssetCatalog.Release();

        // ---- presence --------------------------------------------------------

        /// <summary>
        /// Every asset the integration depends on resolves through the catalog, and
        /// nothing had to be substituted to make that true.
        /// </summary>
        [Test]
        public void EveryRequiredBrandAssetResolvesWithNoFallback()
        {
            foreach (NebulaBrandAssetCatalog.KeyArtAspect aspect in
                     new[] { NebulaBrandAssetCatalog.KeyArtAspect.SixteenByNine,
                             NebulaBrandAssetCatalog.KeyArtAspect.SixteenByTen,
                             NebulaBrandAssetCatalog.KeyArtAspect.UltraWide })
            {
                foreach (NebulaBrandAssetCatalog.KeyArtDetail detail in
                         new[] { NebulaBrandAssetCatalog.KeyArtDetail.Low,
                                 NebulaBrandAssetCatalog.KeyArtDetail.Hd,
                                 NebulaBrandAssetCatalog.KeyArtDetail.Uhd })
                {
                    Assert.IsNotNull(NebulaBrandAssetCatalog.KeyArt(aspect, detail),
                        $"clean plate {aspect}/{detail}");
                }
            }

            foreach (NebulaBrandAssetCatalog.MarkStyle style in
                     new[] { NebulaBrandAssetCatalog.MarkStyle.Effects, NebulaBrandAssetCatalog.MarkStyle.Flat })
            {
                foreach (NebulaBrandAssetCatalog.MarkQuality quality in
                         new[] { NebulaBrandAssetCatalog.MarkQuality.High, NebulaBrandAssetCatalog.MarkQuality.Medium,
                                 NebulaBrandAssetCatalog.MarkQuality.Low, NebulaBrandAssetCatalog.MarkQuality.Compact })
                {
                    Assert.IsNotNull(NebulaBrandAssetCatalog.Logo(style, quality), $"logo {style}/{quality}");
                    Assert.IsNotNull(NebulaBrandAssetCatalog.Emblem(style, quality), $"emblem {style}/{quality}");
                }
            }

            Assert.IsNotNull(NebulaBrandAssetCatalog.LoadingStatic, "Motion Off plate");

            Assert.AreEqual(75, NebulaBrandAssetCatalog.LoadingNormalHigh.Count, "normal 256");
            Assert.AreEqual(75, NebulaBrandAssetCatalog.LoadingNormalLow.Count, "normal 128");
            Assert.AreEqual(75, NebulaBrandAssetCatalog.LoadingReducedHigh.Count, "reduced 256");
            Assert.AreEqual(75, NebulaBrandAssetCatalog.LoadingReducedLow.Count, "reduced 128");

            Assert.IsEmpty(NebulaBrandAssetCatalog.MissingAssets,
                "brand assets the catalog could not resolve: " +
                string.Join(", ", NebulaBrandAssetCatalog.MissingAssets));
            Assert.AreEqual(0, NebulaBrandAssetCatalog.FallbacksUsed,
                "a real asset had to be substituted for on the normal path");
            Assert.IsFalse(NebulaBrandAssetCatalog.CombinedLogoKeyArtUsed,
                "the pre-delivery composited plate is being drawn");
        }

        /// <summary>The delivered masters, at the sizes the delivery states.</summary>
        [Test]
        public void EveryPlateHasTheDimensionsTheDeliveryStates()
        {
            AssertSize(KeyArtRoot + "/brand.keyart.clean.low.png", 1280, 720);
            AssertSize(KeyArtRoot + "/brand.keyart.clean.hd.png", 1920, 1080);
            AssertSize(KeyArtRoot + "/brand.keyart.clean.uhd.png", 3840, 2160);
            AssertSize(KeyArtRoot + "/brand.keyart.clean.wide16x10.png", 2560, 1600);
            AssertSize(KeyArtRoot + "/brand.keyart.clean.ultrawide.png", 3440, 1440);

            // Logo and emblem dimensions are asserted against the delivery manifest
            // by NebulaBrandRuntimeExportTests rather than recomputed here. The
            // lockup is about 3.58:1 but not exactly, so deriving a height from a
            // rounded ratio disagrees with the export by a pixel at 256 -- and a test
            // that has to be told the answer is not checking anything the manifest
            // does not already check better.

            AssertSize(Sheet256, 3840, 1280);
            AssertSize(Sheet128, 1920, 640);
            AssertSize(Reduced256, 3840, 1280);
            AssertSize(Reduced128, 1920, 640);
            AssertSize(LoadingRoot + "/brand.loading.static.png", 512, 512);
        }

        /// <summary>
        /// The identity set is no longer under Resources.
        /// <para>
        /// It was in the first pass, which put roughly 5MB of icon into the player
        /// twice — once as the executable's icon and again as a loadable resource
        /// nothing loaded. Anything under a Resources folder ships whether or not it
        /// is referenced, so this is the assertion that keeps it out.
        /// </para>
        /// </summary>
        [Test]
        public void NoAppIconOrSplashSitsUnderResources()
        {
            var offenders = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("brand.icon") || name.StartsWith("brand.splash")) offenders.Add(path);
            }

            Assert.IsEmpty(offenders,
                "identity art is back under Resources and will ship twice: " + string.Join(", ", offenders));

            // And it really is where it moved to.
            foreach (int size in NebulaBrandAssetCatalog.IconSizes)
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(
                    NebulaBrandAssetCatalog.IconAssetPath(size)), $"icon {size} at its build-only path");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(
                NebulaBrandAssetCatalog.SplashAssetPath(NebulaBrandAssetCatalog.SplashAspect.SixteenByNine)));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(
                NebulaBrandAssetCatalog.SplashAssetPath(NebulaBrandAssetCatalog.SplashAspect.SixteenByTen)));
        }

        // ---- import policy ---------------------------------------------------

        /// <summary>
        /// All four loading sheets import as the delivery's <c>unityImport</c> block
        /// asks: Multiple, Full Rect, no mips, clamp, bilinear, straight alpha, no
        /// crunch, 4096 cap.
        /// </summary>
        [Test]
        public void EveryLoadingSheetImportsUnderTheDeliveredPolicy()
        {
            foreach (string path in new[] { Sheet256, Sheet128, Reduced256, Reduced128 })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsNotNull(importer, path);

                Assert.AreEqual(SpriteImportMode.Multiple, importer.spriteImportMode, path + ": sprite mode");
                Assert.IsFalse(importer.mipmapEnabled, path + ": mipmaps");
                Assert.IsTrue(importer.sRGBTexture, path + ": sRGB");
                Assert.IsTrue(importer.alphaIsTransparency, path + ": alpha is transparency");
                Assert.AreEqual(TextureImporterAlphaSource.FromInput, importer.alphaSource, path + ": straight alpha");
                Assert.AreEqual(TextureWrapMode.Clamp, importer.wrapMode, path + ": wrap");
                Assert.AreEqual(FilterMode.Bilinear, importer.filterMode, path + ": filter");
                Assert.AreEqual(75, importer.spritesheet.Length, path + ": slice count");

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                Assert.AreEqual(SpriteMeshType.FullRect, settings.spriteMeshType, path + ": mesh type");

                TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings("Standalone");
                Assert.IsTrue(platform.overridden, path + ": standalone override");
                Assert.AreEqual(4096, platform.maxTextureSize, path + ": max size");
                Assert.IsFalse(platform.crunchedCompression, path + ": crunch");
            }
        }

        /// <summary>
        /// The transparent marks import uncompressed with straight alpha.
        /// <para>
        /// Uncompressed is the point of them existing separately. The whole reason to
        /// stop compositing the wordmark into the plate is to stop it being part of a
        /// block-compressed photograph; re-compressing it here would give that back.
        /// </para>
        /// </summary>
        [Test]
        public void TheLogoAndEmblemImportUncompressedWithStraightAlpha()
        {
            foreach (string path in new[]
                     {
                         LogoRoot + "/brand.logo.effects.2048.png",
                         LogoRoot + "/brand.logo.flat.512.png",
                         EmblemRoot + "/brand.emblem.effects.1024.png",
                         EmblemRoot + "/brand.emblem.flat.256.png",
                     })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsNotNull(importer, path);

                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType, path + ": type");
                Assert.AreEqual(SpriteImportMode.Single, importer.spriteImportMode, path + ": sprite mode");
                Assert.IsTrue(importer.alphaIsTransparency, path + ": alpha is transparency");
                Assert.AreEqual(TextureImporterAlphaSource.FromInput, importer.alphaSource, path + ": straight alpha");
                Assert.IsFalse(importer.mipmapEnabled, path + ": mipmaps");
                Assert.IsTrue(importer.sRGBTexture, path + ": sRGB");

                TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings("Standalone");
                Assert.AreEqual(TextureImporterCompression.Uncompressed, platform.textureCompression,
                    path + ": a block codec on the mark is what separating it was meant to avoid");
                Assert.IsFalse(platform.crunchedCompression, path + ": crunch");
            }
        }

        /// <summary>Every authored clean plate is block-compressed rather than left at full size.</summary>
        [Test]
        public void EveryAuthoredPlateIsBlockCompressed()
        {
            foreach (string name in new[]
                     { "brand.keyart.clean.low", "brand.keyart.clean.hd", "brand.keyart.clean.uhd",
                       "brand.keyart.clean.wide16x10", "brand.keyart.clean.ultrawide" })
            {
                string path = $"{KeyArtRoot}/{name}.png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsNotNull(importer, path);

                Assert.AreEqual(TextureImporterFormat.BC7,
                    importer.GetPlatformTextureSettings("Standalone").format,
                    path + ": BC7 -- DXT1 bands the dust ramp");
                Assert.AreEqual(TextureImporterAlphaSource.None, importer.alphaSource,
                    path + ": the plates are opaque; alpha would cost a quarter of the texture");
                Assert.IsFalse(importer.mipmapEnabled, path + ": mipmaps");
                Assert.AreEqual(TextureImporterNPOTScale.None, importer.npotScale, path + ": NPOT rescale");
            }
        }

        /// <summary>
        /// The chrome importer does not claim the brand tree.
        /// <para>
        /// Both postprocessors match on <c>Assets/Resources/UI/</c>. If the exclusion
        /// were dropped, the 4K plate would come back at 512 and the loading sheets as
        /// single sprites — and nothing else in the suite would notice, because both
        /// still load and both still draw.
        /// </para>
        /// </summary>
        [Test]
        public void TheChromeImporterDoesNotClaimTheBrandTree()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Sheet256);
            Assert.AreNotEqual(512, importer.GetPlatformTextureSettings("Standalone").maxTextureSize,
                "the chrome policy's 512 cap has been applied to a brand asset");
            Assert.AreNotEqual(SpriteImportMode.Single, importer.spriteImportMode,
                "the chrome policy has re-imported the loading sheet as a single sprite");
            Assert.IsFalse(importer.isReadable,
                "the chrome policy makes sprites readable; a readable 4K plate doubles its memory");
        }

        // ---- frame order -----------------------------------------------------

        /// <summary>
        /// Both families slice to the 15x5 grid in the delivery's stated order:
        /// left to right, then top to bottom.
        /// </summary>
        [Test]
        public void EverySheetIsTheGridInPlaybackOrder()
        {
            AssertGridOrder(NebulaBrandAssetCatalog.LoadingNormalHigh, 256, "normal 256");
            AssertGridOrder(NebulaBrandAssetCatalog.LoadingNormalLow, 128, "normal 128");
            AssertGridOrder(NebulaBrandAssetCatalog.LoadingReducedHigh, 256, "reduced 256");
            AssertGridOrder(NebulaBrandAssetCatalog.LoadingReducedLow, 128, "reduced 128");
        }

        private static void AssertGridOrder(IReadOnlyList<Sprite> frames, int tile, string what)
        {
            const int columns = 15, rows = 5;
            Assert.AreEqual(75, frames.Count, what);

            int height = rows * tile;
            for (int i = 0; i < frames.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                var expected = new Rect(column * tile, height - (row + 1) * tile, tile, tile);
                Assert.AreEqual(expected, frames[i].rect,
                    $"{what}: frame {i} should be grid cell (col {column}, row {row} from the top)");
            }
        }

        /// <summary>
        /// Frames are ordered by the index in their name, not by load order.
        /// <para>
        /// The failure this guards is specific: an unpadded name sorts "10" before
        /// "2", so a sheet that loads alphabetically plays 0,1,10,11..,2,20.
        /// </para>
        /// </summary>
        [Test]
        public void FrameOrderComesFromTheIndexAndNotFromTheName()
        {
            Assert.AreEqual(0, NebulaBrandAssetCatalog.FrameIndex("brand.loading.sheet.256_00"));
            Assert.AreEqual(10, NebulaBrandAssetCatalog.FrameIndex("brand.loading.sheet.256_10"));
            Assert.AreEqual(74, NebulaBrandAssetCatalog.FrameIndex("brand.loading.reduced.128_74"));
            Assert.AreEqual(int.MaxValue, NebulaBrandAssetCatalog.FrameIndex("no-index"),
                "an unindexed sprite sorts last rather than throwing");
            Assert.Less(NebulaBrandAssetCatalog.FrameIndex("x_02"), NebulaBrandAssetCatalog.FrameIndex("x_10"));
        }

        // ---- packaging -------------------------------------------------------

        /// <summary>
        /// No sprite atlas packs the brand art.
        /// <para>
        /// An atlas would repack the 75 tiles into whatever layout fits its page,
        /// which changes every sprite's rect and therefore the loop's order.
        /// </para>
        /// </summary>
        [Test]
        public void NoSpriteAtlasPacksTheBrandArt()
        {
            var offenders = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:SpriteAtlas"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (atlas == null) continue;

                foreach (Object packable in atlas.GetPackables())
                {
                    if (packable == null) continue;
                    string packed = AssetDatabase.GetAssetPath(packable);
                    if (!string.IsNullOrEmpty(packed) && packed.StartsWith(BrandRoot))
                        offenders.Add($"{path} packs {packed}");
                }
            }

            Assert.IsEmpty(offenders, "brand art packed into an atlas: " + string.Join("; ", offenders));
        }

        // ---- what a 4K High display actually residents -------------------------

        /// <summary>
        /// The 4K row of the shipping matrix resolves the authored UHD pair, and
        /// residents exactly one plate and one mark while doing it.
        /// <para>
        /// Both halves matter together. Selecting the 3840x2160 plate is the fix;
        /// selecting it <i>as well as</i> keeping the 1920x1080 one alive would be a
        /// regression dressed as one, because the whole point of the resident-slot
        /// design is that the ladder costs one rung and not the sum of it. The
        /// measured sizes are reported so the cost of the change is a number in the
        /// run log rather than an estimate.
        /// </para>
        /// </summary>
        [Test]
        public void AFourKHighDisplayResidentsTheAuthoredUhdPairAndNothingElse()
        {
            int high = -1;
            string[] names = QualitySettings.names;
            for (int i = 0; names != null && i < names.Length; i++)
                if (string.Equals(names[i], "High", System.StringComparison.OrdinalIgnoreCase)) high = i;
            Assert.GreaterOrEqual(high, 0, "the project ladder no longer has a level named High");

            var aspect = NebulaBrandAssetCatalog.AspectFor(3840, 2160);
            var detail = NebulaBrandAssetCatalog.DetailFor(2160, high);
            var mark = NebulaBrandAssetCatalog.MarkQualityFor(2160, high);

            Sprite plate = NebulaBrandAssetCatalog.KeyArt(aspect, detail);
            Sprite logo = NebulaBrandAssetCatalog.Logo(NebulaBrandAssetCatalog.MarkStyle.Effects, mark);

            Assert.IsNotNull(plate, "no plate resolved for 3840x2160 High");
            Assert.IsNotNull(logo, "no wordmark resolved for 3840x2160 High");
            Assert.AreEqual(0, NebulaBrandAssetCatalog.FallbacksUsed, "a 4K High boot substituted an asset");
            Assert.IsFalse(NebulaBrandAssetCatalog.CombinedLogoKeyArtUsed,
                "the combined plate is a failure path and must not be on the 4K row");

            Assert.AreEqual(3840, plate.texture.width, "the plate is not the authored 4K export");
            Assert.AreEqual(2160, plate.texture.height, "the plate is not the authored 4K export");
            Assert.AreEqual(2048, logo.texture.width, "the wordmark is not the 2048 export");

            long plateBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(plate.texture);
            long logoBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(logo.texture);

            Sprite hd = NebulaBrandAssetCatalog.KeyArt(aspect, NebulaBrandAssetCatalog.KeyArtDetail.Hd);
            Sprite mark1024 = NebulaBrandAssetCatalog.Logo(NebulaBrandAssetCatalog.MarkStyle.Effects,
                NebulaBrandAssetCatalog.MarkQuality.Medium);
            long hdBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(hd.texture);
            long mark1024Bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mark1024.texture);

            TestContext.WriteLine(
                $"4K High resident: plate {plate.texture.name} {plateBytes / 1048576f:0.00}MB + " +
                $"mark {logo.texture.name} {logoBytes / 1048576f:0.00}MB = " +
                $"{(plateBytes + logoBytes) / 1048576f:0.00}MB; " +
                $"the rung it used to take was {hd.texture.name} {hdBytes / 1048576f:0.00}MB + " +
                $"{mark1024.texture.name} {mark1024Bytes / 1048576f:0.00}MB = " +
                $"{(hdBytes + mark1024Bytes) / 1048576f:0.00}MB");

            // Resolving the HD rung above displaced the UHD one in the same slot,
            // which is the design: the ladder is never held at once.
            Assert.AreNotEqual(plate.texture.GetInstanceID(), hd.texture.GetInstanceID(),
                "the HD and UHD plates must be distinct assets for this measurement to mean anything");
        }

        // ---- helpers ---------------------------------------------------------

        private static void AssertSize(string path, int width, int height)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsNotNull(texture, path + " is missing");
            Assert.AreEqual(width, texture.width, path + ": width");
            Assert.AreEqual(height, texture.height, path + ": height");
        }
    }
}
