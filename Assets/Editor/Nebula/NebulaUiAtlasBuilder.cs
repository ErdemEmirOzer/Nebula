using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Nebula.EditorTools
{
    /// <summary>
    /// Packs the interface sprites into one atlas per folder.
    /// <para>
    /// 85 loose PNGs is correct output and the wrong packaging: every one is its
    /// own draw call's worth of texture binding, which is the cost the four-canvas
    /// split (L-12) was meant to avoid paying. The atlas is what makes that split
    /// actually batch.
    /// </para>
    /// <para>
    /// One atlas per folder rather than one for everything, because the folders
    /// already group by when the sprites are needed -- HUD art is resident during
    /// play, menu chrome only while a screen is open.
    /// </para>
    /// </summary>
    public static class NebulaUiAtlasBuilder
    {
        private const string SpriteRoot = "Assets/Resources/UI";
        private const string AtlasRoot = "Assets/Resources/UI/Atlases";

        [MenuItem("Nebula/UI/Rebuild Sprite Atlases")]
        public static void Rebuild()
        {
            if (!Directory.Exists(SpriteRoot))
            {
                Debug.LogWarning($"[Nebula] {SpriteRoot} does not exist; nothing to pack.");
                return;
            }

            Directory.CreateDirectory(AtlasRoot);
            var built = new List<string>();

            foreach (string folder in Directory.GetDirectories(SpriteRoot))
            {
                string name = Path.GetFileName(folder);
                if (name == "Atlases") continue;

                // Brand is skipped by name, not by happening to hold no loose PNGs.
                // Its loading sheet is 75 sprites whose playback order is their rect
                // order in one texture; an atlas repacks them into whatever layout
                // fits and the loop would play scrambled. The 4K key art would also
                // not fit an atlas page, and neither belongs in a batching atlas.
                if (name == "Brand") continue;

                var sprites = new List<Object>();
                foreach (string file in Directory.GetFiles(folder, "*.png"))
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(file.Replace('\\', '/'));
                    if (sprite != null) sprites.Add(sprite);
                }
                if (sprites.Count == 0) continue;

                built.Add(BuildAtlas(name, sprites));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Nebula] Packed {built.Count} atlas(es): {string.Join(", ", built)}");
        }

        private static string BuildAtlas(string name, List<Object> sprites)
        {
            string path = $"{AtlasRoot}/ui.{name.ToLowerInvariant()}.spriteatlas";

            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas == null)
            {
                atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, path);
            }

            // Tight packing would trim the transparent margins that give the
            // 9-sliced frames their chamfer, so the borders must survive rotation
            // and trimming being off.
            atlas.SetPackingSettings(new SpriteAtlasPackingSettings
            {
                enableRotation = false,
                enableTightPacking = false,
                padding = 4,
            });

            atlas.SetTextureSettings(new SpriteAtlasTextureSettings
            {
                sRGB = true,
                filterMode = FilterMode.Bilinear,
                generateMipMaps = false,
            });

            // Left at the default the atlas asset ships with, every one of these
            // would be block-compressed. That is right for photographic art and
            // wrong for this: 9-sliced frames are flat fields with one-pixel
            // hairlines and hard chamfers, and DXT/ASTC turns a hairline into a
            // smear. Uncompressed costs memory that a UI atlas can afford; a
            // visibly mushy panel edge is not affordable at all.
            var platform = new TextureImporterPlatformSettings
            {
                name = "DefaultTexturePlatform",
                overridden = true,
                maxTextureSize = 2048,
                format = TextureImporterFormat.RGBA32,
                textureCompression = TextureImporterCompression.Uncompressed,
                crunchedCompression = false,
            };
            atlas.SetPlatformSettings(platform);

            // Replace rather than append: a rebuild after a sprite is deleted must
            // not keep packing the one that is gone.
            atlas.Remove(atlas.GetPackables());
            atlas.Add(sprites.ToArray());

            EditorUtility.SetDirty(atlas);
            return $"{name}({sprites.Count})";
        }
    }
}
