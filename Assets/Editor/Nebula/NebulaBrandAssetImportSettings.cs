using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Nebula.EditorTools
{
    /// <summary>
    /// Import policy for the delivered brand art under <c>Assets/Resources/UI/Brand/</c>.
    /// <para>
    /// Separate from <see cref="NebulaUiAssetImportSettings"/>, which owns the rest of
    /// <c>Resources/UI</c> and applies the opposite rules: 512px cap, one Single sprite
    /// per file, uncompressed. Under that policy a 3840px key art would be resampled to
    /// 512 and the loading sheet would import as one sprite instead of 75 — so Brand is
    /// excluded there and claimed here.
    /// </para>
    /// <para>
    /// The loading sheet's slice rectangles are the playback order. They are written on
    /// import rather than stored in a hand-maintained <c>.meta</c>, so re-importing the
    /// sheet cannot come back with Unity's automatic slicing, which orders by discovered
    /// island and would play the loop scrambled.
    /// </para>
    /// </summary>
    public sealed class NebulaBrandAssetImportSettings : AssetPostprocessor
    {
        private const string BrandRoot = "Assets/Resources/UI/Brand/";

        /// <summary>
        /// Brand art that must <b>not</b> be in Resources: the app icon set, the
        /// splash plates and the native icon containers.
        /// <para>
        /// They were under Resources in the first pass, which put the icon set into
        /// the player twice — once as the executable's icon and again as a loadable
        /// resource. Nothing ever loaded them at runtime, so they moved out; this
        /// policy followed them so their import settings did not silently revert to
        /// Unity's defaults on the way.
        /// </para>
        /// </summary>
        private const string BrandSourceRoot = "Assets/BrandSource/";

        /// <summary>
        /// The one delivered plate whose dimensions are not multiples of four.
        /// <para>
        /// 1672x941. Block compression needs both axes divisible by four, and Unity
        /// silently falls back to an uncompressed format rather than telling you, so
        /// this one is asked for RGB24 honestly. Every authored plate added in the
        /// second pass — 1280x720, 1920x1080, 2560x1600, 3440x1440 — is compressible.
        /// </para>
        /// </summary>
        private const string UncompressiblePlate = "brand.keyart.logo.hd";

        /// <summary>Platform string for the desktop players this project builds.</summary>
        private const string Standalone = "Standalone";

        /// <summary>
        /// Bumped whenever the rules below change.
        /// <para>
        /// Unity hashes the importer version into an asset's import result, so without
        /// this a policy edit applies only to assets that happen to be re-imported for
        /// some other reason. That is not academic: raising the identity cap from 2048
        /// to 4096 left the 2560x1600 splash sitting at the resampled 2048x1280 it had
        /// already been imported as, and the only reason anyone noticed is that the
        /// manifest gate compares imported dimensions against delivered ones.
        /// </para>
        /// <para>
        /// 2: the second delivery — marks, emblems, reduced sheets, authored aspects,
        /// and the 4096 identity cap.
        /// </para>
        /// </summary>
        public override uint GetVersion() => 2;

        // ---- loading sheet geometry -----------------------------------------
        //
        // From nebula-loading-depth-unity.json: 75 frames, 15 columns x 5 rows,
        // left-to-right then top-to-bottom, 2.5s at 30fps. Taken from the runtime
        // catalog rather than declared again here, so the rectangles this writes and
        // the indices playback reads cannot disagree. NebulaBrandAssetTests holds
        // that one copy against the delivered JSON.

        private const int LoadingColumns = Nebula.UI.Brand.NebulaBrandAssetCatalog.LoadingColumns;
        private const int LoadingRows = Nebula.UI.Brand.NebulaBrandAssetCatalog.LoadingRows;
        private const int LoadingFrameCount = Nebula.UI.Brand.NebulaBrandAssetCatalog.LoadingFrameCount;

        /// <summary>Tile edge in pixels, keyed by sheet asset name.</summary>
        private static readonly Dictionary<string, int> LoadingTileSize = new Dictionary<string, int>
        {
            { "brand.loading.sheet.256", 256 },
            { "brand.loading.sheet.128", 128 },
            // Reduced Motion. Same 15x5 grid and the same 75 cells, but each of the
            // 25 real frames occupies three consecutive cells -- so it is sliced
            // identically and simply steps through repeats, which is what makes the
            // motion read as slower without the playback clock changing.
            { "brand.loading.reduced.256", 256 },
            { "brand.loading.reduced.128", 128 },
        };

        /// <summary>The sprite name a sliced frame carries, zero-padded so it sorts.</summary>
        public static string LoadingFrameName(string sheetName, int index) => $"{sheetName}_{index:D2}";

        private void OnPreprocessTexture()
        {
            bool inResources = assetPath.StartsWith(BrandRoot);
            bool inSource = assetPath.StartsWith(BrandSourceRoot);
            if (!inResources && !inSource) return;

            var importer = (TextureImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            // Shared across every brand asset. sRGB because all of it is colour a
            // human authored, not data; NPOT scaling off because a 3840x2160 plate
            // resampled to the next power of two is both blurrier and larger.
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;

            if (assetPath.StartsWith(BrandRoot + "Loading/")) ApplyLoading(importer, name);
            else if (assetPath.StartsWith(BrandRoot + "KeyArt/")) ApplyKeyArt(importer, name);
            else if (assetPath.StartsWith(BrandRoot + "Logo/")) ApplyMark(importer);
            else if (assetPath.StartsWith(BrandRoot + "Emblem/")) ApplyMark(importer);
            else if (inSource) ApplyIdentity(importer, name);
        }

        /// <summary>
        /// The transparent logo and emblem: authored marks drawn over the key art.
        /// <para>
        /// Uncompressed, and that is the whole point of them being separate assets.
        /// The logo is a metallic bevel with a cyan underglow and a violet tip over
        /// straight alpha; BC7 is good but it is still a block codec, and the one
        /// thing this replacement exists to deliver is a wordmark that is not baked
        /// into a compressed photograph. The largest is 2048x572 at 4.7MB and only
        /// one resolution is ever resident.
        /// </para>
        /// </summary>
        private static void ApplyMark(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.spritePixelsPerUnit = 100f;

            // Straight alpha, and the delivery has already sanitised fully
            // transparent pixels to RGB 0,0,0 -- so AlphaIsTransparency's colour
            // bleed is what keeps bilinear sampling from dragging that black out
            // along every edge as a halo.
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteBorder = Vector4.zero;
            importer.SetTextureSettings(settings);

            SetPlatform(importer, Standalone,
                maxSize: 2048,
                format: TextureImporterFormat.RGBA32,
                compression: TextureImporterCompression.Uncompressed);
        }

        // ---- loading ---------------------------------------------------------

        private static void ApplyLoading(TextureImporter importer, string name)
        {
            importer.textureType = TextureImporterType.Sprite;

            // Straight alpha, as the delivery states. FromInput keeps the authored
            // channel; AlphaIsTransparency makes Unity bleed colour outward under the
            // transparent pixels, without which the relay's thin cyan strokes pick up
            // a black fringe everywhere bilinear sampling straddles an edge.
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = 100f;

            if (LoadingTileSize.TryGetValue(name, out int tile))
            {
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.spritesheet = BuildFrameSlices(name, tile);
            }
            else
            {
                // The Motion Off plate. One sprite, drawn whole.
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePivot = new Vector2(0.5f, 0.5f);
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;   // no tight mesh; frames align by rect
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteBorder = Vector4.zero;                // never 9-sliced
            importer.SetTextureSettings(settings);

            bool sheet = LoadingTileSize.ContainsKey(name);
            SetPlatform(importer, Standalone,
                maxSize: 4096,
                // The static plate is 512px of glow the player stares at while a
                // Motion Off session composes; it stays uncompressed. The sheets are
                // large enough to be worth compressing and BC7 holds the strokes.
                format: sheet ? TextureImporterFormat.BC7 : TextureImporterFormat.RGBA32,
                compression: sheet ? TextureImporterCompression.Compressed
                                   : TextureImporterCompression.Uncompressed);
        }

        /// <summary>
        /// The 15x5 grid, in playback order.
        /// <para>
        /// Unity texture space has its origin at the bottom-left and the sheet is
        /// authored top-down, so row <c>r</c> of the artwork sits at
        /// <c>height - (r+1)*tile</c>. Getting this backwards produces a loop that
        /// plays five correct-looking runs of fifteen frames in the wrong order --
        /// which is why <see cref="LoadingFrameName"/> encodes the index and the
        /// runtime sorts by it instead of trusting load order.
        /// </para>
        /// </summary>
        private static SpriteMetaData[] BuildFrameSlices(string sheetName, int tile)
        {
            int height = LoadingRows * tile;
            var slices = new SpriteMetaData[LoadingFrameCount];
            for (int i = 0; i < LoadingFrameCount; i++)
            {
                int column = i % LoadingColumns;
                int row = i / LoadingColumns;
                slices[i] = new SpriteMetaData
                {
                    name = LoadingFrameName(sheetName, i),
                    rect = new Rect(column * tile, height - (row + 1) * tile, tile, tile),
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                };
            }
            return slices;
        }

        // ---- key art ---------------------------------------------------------

        private static void ApplyKeyArt(TextureImporter importer, string name)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.spritePixelsPerUnit = 100f;

            // Both plates are fully opaque -- verified against the delivered PNGs,
            // whose alpha channel is 255 everywhere. Carrying it anyway would cost a
            // quarter of the texture for a channel that is a constant.
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteBorder = Vector4.zero;
            importer.SetTextureSettings(settings);

            // BC7 rather than DXT1: the wordmark's metallic bevel and the dust band's
            // indigo ramp are exactly what 4bpp block compression bands, and the brief
            // is explicit that letter edges must not blocky. BC7 is 8bpp -- 8.3MB for
            // the 4K plate against 33MB uncompressed -- and holds both.
            //
            // Block compression needs both dimensions to be multiples of four, which
            // every authored plate is and the one legacy combined plate is not.
            bool blockCompressible = name != UncompressiblePlate;
            SetPlatform(importer, Standalone,
                maxSize: 4096,
                format: blockCompressible ? TextureImporterFormat.BC7 : TextureImporterFormat.RGB24,
                compression: blockCompressible ? TextureImporterCompression.CompressedHQ
                                               : TextureImporterCompression.Uncompressed);
        }

        // ---- app identity ----------------------------------------------------

        private static void ApplyIdentity(TextureImporter importer, string name)
        {
            bool splash = name.StartsWith("brand.splash");

            // The splash is handed to PlayerSettings.SplashScreen, which takes a
            // Sprite. The icons are handed to PlayerSettings.SetIcons, which takes
            // Texture2D -- as a Sprite they would still resolve, but Default is what
            // they are.
            importer.textureType = splash ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (splash)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePivot = new Vector2(0.5f, 0.5f);
            }

            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;

            // Never compressed. A 16px icon is 256 pixels of emblem and DXT would eat
            // it whole; the whole set is under two megabytes.
            //
            // 4096, not 2048. The 16:10 splash is 2560x1600 and a 2048 cap silently
            // resampled it to 2048x1280 -- the file on disk was right, the asset the
            // build would use was not. Caught by the manifest gate, which compares
            // imported dimensions against the delivered ones for exactly this reason.
            SetPlatform(importer, Standalone,
                maxSize: 4096,
                format: TextureImporterFormat.RGBA32,
                compression: TextureImporterCompression.Uncompressed);
        }

        // ---- shared ----------------------------------------------------------

        private static void SetPlatform(TextureImporter importer, string platform, int maxSize,
            TextureImporterFormat format, TextureImporterCompression compression)
        {
            var settings = new TextureImporterPlatformSettings
            {
                name = platform,
                overridden = true,
                maxTextureSize = maxSize,
                format = format,
                textureCompression = compression,
                crunchedCompression = false,          // the delivery forbids it; it also re-quantises
                compressionQuality = 100,
            };
            importer.SetPlatformTextureSettings(settings);

            // The default platform is what the Editor falls back to when no override
            // matches -- including in a test run on a machine whose active target is
            // not the one above. Left at Unity's default it would re-introduce the
            // 2048 cap that this policy exists to lift.
            var fallback = importer.GetDefaultPlatformTextureSettings();
            fallback.maxTextureSize = maxSize;
            fallback.textureCompression = compression;
            fallback.crunchedCompression = false;
            importer.SetPlatformTextureSettings(fallback);
        }
    }
}
