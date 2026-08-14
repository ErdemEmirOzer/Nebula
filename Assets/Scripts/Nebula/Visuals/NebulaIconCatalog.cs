using System;
using System.Collections.Generic;
using Nebula.Extraction;
using Nebula.Modules;
using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Visual-only icon mapping for Part 24. The downloaded export called the
    /// lime nuclear icon "resource.nuclear"; Nebula deliberately maps it to
    /// the real gameplay identity <see cref="ModuleCatalog.EnergyNuclearId"/>.
    /// It is not exposed as a new resource or save/content ID.
    /// </summary>
    public static class NebulaIconCatalog
    {
        private const string ResourceRoot = "VisualIcons/Resources/";
        private const string RarityRoot = "VisualIcons/Rarity/";

        private static readonly string[] FamilyPaths =
        {
            ResourceRoot + "resource_metal",
            ResourceRoot + "resource_wood",
            ResourceRoot + "resource_gas",
            ResourceRoot + "resource_water",
            ResourceRoot + "resource_diamond"
        };

        private static readonly string[] RarityPaths =
        {
            RarityRoot + "rarity_common",
            RarityRoot + "rarity_uncommon",
            RarityRoot + "rarity_rare",
            RarityRoot + "rarity_very_rare",
            RarityRoot + "rarity_legendary"
        };

        private static readonly Dictionary<string, ResourceFamily> ItemFamilies =
            new Dictionary<string, ResourceFamily>(StringComparer.Ordinal)
            {
                { "resource.metal", ResourceFamily.Metal },
                { "material.metal_alloy", ResourceFamily.Metal },
                { "resource.wood", ResourceFamily.Wood },
                { "material.biocomposite", ResourceFamily.Wood },
                { "resource.gas", ResourceFamily.Gas },
                { "material.refined_gas", ResourceFamily.Gas },
                { "resource.water", ResourceFamily.Water },
                { "material.purified_water", ResourceFamily.Water },
                { "resource.diamond", ResourceFamily.Diamond },
                { "material.industrial_diamond", ResourceFamily.Diamond }
            };

        public static string ResourcePath(ResourceFamily family) =>
            FamilyPaths[Mathf.Clamp((int)family, 0, FamilyPaths.Length - 1)];

        public static string RarityPath(Rarity rarity) =>
            RarityPaths[Mathf.Clamp((int)rarity, 0, RarityPaths.Length - 1)];

        public static bool TryGetItemPath(string itemId, out string resourcePath)
        {
            if (string.Equals(itemId, ModuleCatalog.EnergyNuclearId, StringComparison.Ordinal))
            {
                resourcePath = ResourceRoot + "resource_nuclear";
                return true;
            }

            if (itemId != null && ItemFamilies.TryGetValue(itemId, out ResourceFamily family))
            {
                resourcePath = ResourcePath(family);
                return true;
            }

            resourcePath = null;
            return false;
        }
    }

    /// <summary>
    /// Cached Texture2D/Sprite loader. Icons remain presentation-only and
    /// never become content, economy, rarity, or save authorities.
    /// </summary>
    public static class NebulaIconResolver
    {
        private static readonly Dictionary<string, Texture2D> Textures =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Sprite> Sprites =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static Sprite Resource(ResourceFamily family) =>
            LoadSprite(NebulaIconCatalog.ResourcePath(family));

        public static Sprite Rarity(Rarity rarity) =>
            LoadSprite(NebulaIconCatalog.RarityPath(rarity));

        public static Sprite Item(string itemId) =>
            NebulaIconCatalog.TryGetItemPath(itemId, out string path) ? LoadSprite(path) : null;

        public static Texture2D LoadTexture(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath)) return null;
            if (!Textures.TryGetValue(resourcePath, out Texture2D texture))
            {
                texture = Resources.Load<Texture2D>(resourcePath);
                Textures[resourcePath] = texture;
            }
            return texture;
        }

        public static Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath)) return null;
            if (Sprites.TryGetValue(resourcePath, out Sprite sprite)) return sprite;
            Texture2D texture = LoadTexture(resourcePath);
            if (texture == null) return null;
            sprite = Sprite.Create(
                texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "Sprite :: " + resourcePath;
            Sprites[resourcePath] = sprite;
            return sprite;
        }

        public static void ClearCache()
        {
            foreach (Sprite sprite in Sprites.Values)
            {
                if (sprite == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(sprite);
                else UnityEngine.Object.DestroyImmediate(sprite);
            }
            Sprites.Clear();
            Textures.Clear();
        }
    }
}
