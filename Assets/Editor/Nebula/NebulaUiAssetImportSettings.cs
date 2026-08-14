using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Nebula.EditorTools
{
    /// <summary>
    /// Applies the UI import policy to every generated sprite under
    /// <c>Assets/Resources/UI/</c> as it is imported.
    /// <para>
    /// The sprites are authored as SVG and rasterised outside Unity, so their
    /// 9-slice borders live in the generator, not in a <c>.meta</c> a human
    /// maintains. Re-importing must therefore re-derive them, or a regenerated
    /// frame silently comes back with a zero border and stretches its own corners
    /// into mush the first time a panel is not exactly sprite-sized.
    /// </para>
    /// <para>
    /// Borders are keyed by folder and sprite family rather than read back from
    /// the manifest JSON, so the policy still applies on a clean clone where the
    /// manifest has not been generated yet.
    /// </para>
    /// </summary>
    public sealed class NebulaUiAssetImportSettings : AssetPostprocessor
    {
        private const string UiRoot = "Assets/Resources/UI/";

        /// <summary>Brand art under the UI root that this policy must not claim.</summary>
        private const string BrandRoot = "Assets/Resources/UI/Brand/";

        /// <summary>Uniform 9-slice border per sprite family, in pixels.</summary>
        private static readonly Dictionary<string, int> BorderByPrefix = new Dictionary<string, int>
        {
            // frames
            { "frame.modal", 20 },
            { "frame.tooltip", 10 },
            { "frame.toast", 10 },
            { "frame.focus", 10 },
            { "frame.selection", 10 },
            { "frame.panel", 16 },
            // controls
            { "button.icon.focused", 15 },
            { "button.icon", 12 },
            { "button.", 14 },          // plain kinds; focused variants overridden below
            // content
            { "row.", 14 },
            { "slot.", 14 },
            { "card.", 18 },
            { "menu.item", 16 },
            { "menu.divider", 10 },
            // bars
            { "bar.track", 5 },
            { "bar.fill", 5 },
            { "bar.ticks", 0 },
            { "bar.ring", 0 },
            // status / hud / nav are drawn whole, never stretched
            { "badge.", 10 },
            { "rarity.hex", 0 },
            { "glyph.", 0 },
            { "hud.", 0 },
            { "nav.page", 0 },
            { "nav.rail", 0 },
        };

        /// <summary>Focused button art carries the ring at the sprite edge, so its border grows with it.</summary>
        private const int FocusedButtonBorder = 17;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiRoot)) return;

            // Brand art lives under the same root and obeys the opposite policy:
            // 4K plates, a 15x5 multi-sprite sheet, block compression. Applying the
            // chrome policy to it would clamp a 3840px key art to 512 and re-import
            // the loading sheet as one Single sprite, destroying the 75 frames.
            // Owned by NebulaBrandAssetImportSettings instead.
            if (assetPath.StartsWith(BrandRoot)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;          // screen-space UI never samples a mip
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 512;
            importer.isReadable = true;              // the UI-01 asset tests sample pixels

            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            // Small chrome stays uncompressed: a 48x40 button loses its hairline
            // to DXT blocking, and the whole set is only a few hundred kilobytes.
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            int border = ResolveBorder(name);
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.spritePivot = new Vector2(0.5f, 0.5f);

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;   // 9-slice needs the full quad
            settings.spriteBorder = new Vector4(border, border, border, border);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
        }

        private static int ResolveBorder(string spriteName)
        {
            if (spriteName.StartsWith("button.") && spriteName.EndsWith(".focused")
                && !spriteName.StartsWith("button.icon"))
            {
                return FocusedButtonBorder;
            }

            int best = 0;
            int bestLength = -1;
            foreach (KeyValuePair<string, int> entry in BorderByPrefix)
            {
                if (spriteName.StartsWith(entry.Key) && entry.Key.Length > bestLength)
                {
                    best = entry.Value;
                    bestLength = entry.Key.Length;
                }
            }
            return best;
        }
    }
}
