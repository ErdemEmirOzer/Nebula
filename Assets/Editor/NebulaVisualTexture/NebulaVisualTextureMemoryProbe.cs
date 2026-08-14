using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Nebula.EditorTools.VisualTexture
{
    /// <summary>
    /// Measures what the texture set costs in memory, before and after, and
    /// whether repeated use of a visual leaks new copies of it.
    /// <para>
    /// The "before" number is not a model. It loads the actual
    /// <see cref="Texture2D"/> sub-assets glTFast built inside the GLBs and asks
    /// the profiler what they weigh, which is the same question the player asks.
    /// The "after" number does the same for the extracted assets. Both come from
    /// <see cref="Profiler.GetRuntimeMemorySizeLong"/> rather than from width x
    /// height x 4, because that arithmetic silently ignores whether Unity actually
    /// gave the format that was asked for.
    /// </para>
    /// <para>
    /// What this cannot measure from the Editor is the part of the brief that
    /// needs a running game: a hundred system promote/demote cycles are gameplay
    /// services, not asset loads, and they live in PlayMode. The instantiate and
    /// destroy loop below covers the asset-level half of the same question —
    /// whether repeated instantiation allocates a second copy of an image — and
    /// the report says as much rather than implying more.
    /// </para>
    /// </summary>
    public static class NebulaVisualTextureMemoryProbe
    {
        /// <summary>How many spawn/despawn cycles the growth check runs.</summary>
        public const int GrowthCycles = 100;

        [MenuItem("Nebula/Visuals/Texture Optimizer/Memory probe", false, 103)]
        public static void MenuRun() => Debug.Log(Run());

        public static void RunFromCommandLine()
        {
            try
            {
                Debug.Log("NEBULA-TEXTURE-MEMORY-OK\n" + Run());
            }
            catch (Exception exception)
            {
                Debug.LogError("NEBULA-TEXTURE-MEMORY-FAILED\n" + exception);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        public static string Run()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Visual texture runtime memory");
            builder.AppendLine();
            builder.AppendLine(
                "Measured with `Profiler.GetRuntimeMemorySizeLong` on the real assets. "
                + "\"Before\" is what glTFast's sub-asset textures weigh today; \"after\" is what the "
                + "extracted, deduplicated, block-compressed set weighs.");
            builder.AppendLine();

            MeasureTextureSets(builder);
            MeasureSpawnBehaviour(builder);

            string root = Path.Combine(
                NebulaVisualTextureOptimizer.ProjectRoot, NebulaVisualTextureManifest.ReportRoot);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "visual-texture-memory.md"), builder.ToString());
            return builder.ToString();
        }

        private static void MeasureTextureSets(StringBuilder builder)
        {
            long beforeBytes = 0;
            int beforeCount = 0;
            var beforeFormats = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (string assetPath in NebulaVisualTextureOptimizer.FindSourceGlbs())
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (!(asset is Texture2D texture)) continue;
                beforeCount++;
                beforeBytes += Profiler.GetRuntimeMemorySizeLong(texture);
                string format = texture.format.ToString();
                beforeFormats[format] = beforeFormats.TryGetValue(format, out int n) ? n + 1 : 1;
            }

            long afterBytes = 0;
            int afterCount = 0;
            var afterFormats = new Dictionary<string, int>(StringComparer.Ordinal);
            string textureRoot = NebulaVisualTextureManifest.TextureRoot.TrimEnd('/');

            if (AssetDatabase.IsValidFolder(textureRoot))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { textureRoot }))
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                    if (texture == null) continue;
                    afterCount++;
                    afterBytes += Profiler.GetRuntimeMemorySizeLong(texture);
                    string format = texture.format.ToString();
                    afterFormats[format] = afterFormats.TryGetValue(format, out int n) ? n + 1 : 1;
                }
            }

            builder.AppendLine("## Texture set");
            builder.AppendLine();
            builder.AppendLine("| | Before (GLB sub-assets) | After (extracted) |");
            builder.AppendLine("|---|---:|---:|");
            builder.AppendLine("| Texture2D count | " + beforeCount + " | " + afterCount + " |");
            builder.AppendLine("| Runtime memory | " + Mb(beforeBytes) + " | " + Mb(afterBytes) + " |");
            builder.AppendLine("| Reduction | | "
                + (beforeBytes > 0
                    ? (1.0 - (double)afterBytes / beforeBytes).ToString("P2", CultureInfo.InvariantCulture)
                    : "n/a")
                + " |");
            builder.AppendLine();

            builder.AppendLine("### Actual imported formats");
            builder.AppendLine();
            builder.AppendLine("| Format | Before | After |");
            builder.AppendLine("|---|---:|---:|");
            foreach (string format in beforeFormats.Keys.Concat(afterFormats.Keys)
                         .Distinct().OrderBy(f => f, StringComparer.Ordinal))
                builder.AppendLine("| " + format
                    + " | " + (beforeFormats.TryGetValue(format, out int b) ? b : 0)
                    + " | " + (afterFormats.TryGetValue(format, out int a) ? a : 0) + " |");
            builder.AppendLine();
        }

        /// <summary>
        /// Instantiates a visual prefab repeatedly and watches the texture and
        /// material populations.
        /// <para>
        /// The invariant under test is that the second spawn of the same visual
        /// costs no new <see cref="Texture2D"/>. Unity gives that for free as long
        /// as nothing calls <c>material</c> instead of <c>sharedMaterial</c> or
        /// copies a texture at load — which is precisely what a runtime GLB parse
        /// would have done, and is why this probe exists rather than being assumed.
        /// </para>
        /// </summary>
        private static void MeasureSpawnBehaviour(StringBuilder builder)
        {
            string[] prefabGuids = AssetDatabase.FindAssets(
                "t:Prefab", new[] { "Assets/Resources/VisualPrefabs" });
            builder.AppendLine("## Spawn behaviour");
            builder.AppendLine();

            if (prefabGuids.Length == 0)
            {
                builder.AppendLine("No visual prefabs found; spawn behaviour not measured.");
                return;
            }

            string prefabPath = AssetDatabase.GUIDToAssetPath(
                prefabGuids.OrderBy(g => AssetDatabase.GUIDToAssetPath(g), StringComparer.Ordinal).First());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            int baselineTextures = CountLoaded<Texture2D>();
            int baselineMaterials = CountLoaded<Material>();

            GameObject first = UnityEngine.Object.Instantiate(prefab);
            int afterFirstTextures = CountLoaded<Texture2D>();
            int afterFirstMaterials = CountLoaded<Material>();
            HashSet<int> firstTextureIds = TextureIdsOf(first);

            GameObject second = UnityEngine.Object.Instantiate(prefab);
            int afterSecondTextures = CountLoaded<Texture2D>();
            HashSet<int> secondTextureIds = TextureIdsOf(second);

            bool shared = firstTextureIds.SetEquals(secondTextureIds);

            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);

            int beforeCycles = CountLoaded<Texture2D>();
            for (int i = 0; i < GrowthCycles; i++)
            {
                GameObject instance = UnityEngine.Object.Instantiate(prefab);
                UnityEngine.Object.DestroyImmediate(instance);
            }
            int afterCycles = CountLoaded<Texture2D>();
            int materialsAfterCycles = CountLoaded<Material>();

            EditorUtility.UnloadUnusedAssetsImmediate();
            int afterUnload = CountLoaded<Texture2D>();

            builder.AppendLine("Prefab under test: `" + prefabPath + "`");
            builder.AppendLine();
            builder.AppendLine("| | |");
            builder.AppendLine("|---|---:|");
            builder.AppendLine("| Loaded Texture2D at baseline | " + baselineTextures + " |");
            builder.AppendLine("| After first spawn | " + afterFirstTextures
                + " (delta " + (afterFirstTextures - baselineTextures) + ") |");
            builder.AppendLine("| After second spawn | " + afterSecondTextures
                + " (delta " + (afterSecondTextures - afterFirstTextures) + ") |");
            builder.AppendLine("| Both spawns reference the same texture instances | " + shared + " |");
            builder.AppendLine("| Loaded Material at baseline | " + baselineMaterials + " |");
            builder.AppendLine("| After first spawn | " + afterFirstMaterials + " |");
            builder.AppendLine("| Texture2D before " + GrowthCycles + " spawn/destroy cycles | "
                + beforeCycles + " |");
            builder.AppendLine("| Texture2D after " + GrowthCycles + " cycles | " + afterCycles
                + " (growth " + (afterCycles - beforeCycles) + ") |");
            builder.AppendLine("| Material after cycles | " + materialsAfterCycles + " |");
            builder.AppendLine("| Texture2D after unloading unused assets | " + afterUnload + " |");
            builder.AppendLine();
            builder.AppendLine(
                "Promote/demote of a star system is a PlayMode gameplay path owned by another workstream and "
                + "is not measured here. What is measured is the asset-level invariant underneath it: "
                + "repeated instantiation of a visual allocates no additional texture.");
        }

        private static int CountLoaded<T>() where T : UnityEngine.Object =>
            Resources.FindObjectsOfTypeAll<T>().Length;

        private static HashSet<int> TextureIdsOf(GameObject instance)
        {
            var ids = new HashSet<int>();
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                foreach (string slot in NebulaVisualTextureOptimizer.MaterialSlots)
                {
                    if (!material.HasProperty(slot)) continue;
                    Texture texture = material.GetTexture(slot);
                    if (texture != null) ids.Add(texture.GetInstanceID());
                }
            }
            return ids;
        }

        private static string Mb(long bytes) =>
            (bytes / 1048576.0).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
    }
}
