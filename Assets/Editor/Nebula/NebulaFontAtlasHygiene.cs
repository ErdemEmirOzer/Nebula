using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Nebula.EditorTools
{
    /// <summary>
    /// Owns the state of the project's font assets, so their bytes are a fact
    /// rather than a side effect of whoever last opened the editor.
    /// <para>
    /// Two tools live here because they are two halves of one architecture, and
    /// splitting them let them drift: the clearing half used to describe a
    /// dynamic fallback baking Turkish glyphs forever as normal, which it was
    /// not — it was the symptom.
    /// </para>
    /// <para><b>The architecture.</b> Every player-facing glyph ships inside the
    /// STATIC atlas of <c>LiberationSans SDF.asset</c>. Nothing reaches the
    /// dynamic fallback on the normal path, so the fallback stays empty and the
    /// runtime never grows an atlas. <see cref="RebuildPlayerFontAtlas"/>
    /// produces that atlas; <see cref="ClearDynamicFontData"/> proves the
    /// fallback is empty afterwards.
    /// </para>
    /// <para><b>What it replaced, and why.</b> The atlas shipped as TextMesh
    /// Pro's stock 250-glyph bake over <c>32 - 126, 160 - 255, 8192 - 8303</c> —
    /// ASCII and Latin-1. This interface is written in Turkish, and
    /// <c>Ğ ğ İ ı Ş ş</c> are Latin Extended-A, outside that range; so are the
    /// arrows on the parent menu's key strip and the U+2212 minus on the
    /// steppers. Every one of those characters missed the primary atlas and
    /// resolved through <c>LiberationSans SDF - Fallback.asset</c>, which is
    /// DYNAMIC. Two separate failures came out of that one gap, and neither was
    /// diagnosable from the other:
    /// </para>
    /// <list type="number">
    /// <item><description><c>TMPro_UGUI_Private.cs:1403</c> spawns a
    /// <c>TMP_SubMeshUI</c> — a <c>HideFlags.DontSave</c> GameObject — for every
    /// material a label needs beyond its first. A fallback font means a second
    /// material, so every Turkish label grew a child object. A 1000-cycle
    /// session soak watched <c>sessionui</c> counts climb on exactly that.
    /// </description></item>
    /// <item><description>A dynamic atlas bakes each newly-rendered glyph into
    /// its own .asset file on disk, so the fallback was dirty after any editor
    /// session that rendered the game, and
    /// <c>VisualAssetIntegrityGateTests</c> said so.</description></item>
    /// </list>
    /// <para>
    /// Note which fix this rules out. A STATIC Turkish fallback would have made
    /// the second symptom go away and left the first untouched, because the
    /// sub-object is created by the material change, not by the population
    /// mode. Only glyphs in the primary atlas fix both, which is why this tool
    /// extends the primary rather than authoring a second font asset.
    /// </para>
    /// </summary>
    public static class NebulaFontAtlasHygiene
    {
        private const string FontRoot = "Assets/TextMesh Pro/Resources/Fonts & Materials";
        private const string PrimaryFontPath = FontRoot + "/LiberationSans SDF.asset";

        /// <summary>
        /// Deliberately outside <c>Assets/</c>. The inventory is build input, not
        /// content: shipping it would put a 244-line manifest of the atlas into
        /// the player for nothing.
        /// </summary>
        private const string InventoryPath = "Tools/Fonts/player-character-inventory.txt";

        /// <summary>
        /// The atlas page size, owned here rather than left to whatever the asset
        /// happens to say, so a clean checkout reproduces the committed bytes.
        /// <para>
        /// It was 1024x1024, and the first rebuild failed with 17 of 249
        /// characters unplaced — including every symbol on the strategic map
        /// legend. The reason was not the inventory. Summing
        /// <c>m_UsedGlyphRects</c> out of the original asset shows TextMesh Pro's
        /// stock bake filled <b>96.0%</b> of that page (1,006,141 px of
        /// 1,048,576). There was no headroom at all; the atlas had been full
        /// since the day it shipped, and any added glyph was going to fail.
        /// </para>
        /// <para>
        /// A second effect compounds it. The stock bake was packed by the Font
        /// Asset Creator with ContactPointRule, and
        /// <see cref="TMP_FontAsset.TryAddCharacters(uint[], out uint[], bool)"/>
        /// hardcodes BestShortSideFit, which measured 90.8% on this same content
        /// — about five points looser. So the rebuild needs roughly 7% more area
        /// than the old atlas for the old glyphs alone, before adding any.
        /// </para>
        /// <para>
        /// 1024x2048 rather than 2048x2048 because the arithmetic says it is
        /// enough and doubling twice is not free: ~235 rects at the measured mean
        /// of 4,346 px is ~1.02 M px of glyph, which at the packer's demonstrated
        /// 90.8% wants ~1.13 M px of page. This page is 2.10 M — it needs 54%
        /// occupancy from a packer that reaches 91%, a margin of 1.9x. The cost
        /// is stated where it is paid: +1.00 MiB, see <see cref="RebuildPlayerFontAtlas"/>.
        /// </para>
        /// <para>
        /// Point size and padding are deliberately NOT touched. Both feed glyph
        /// metrics and <c>_GradientScale</c>, so moving either to buy space would
        /// change how every label measures and renders — paying for atlas room
        /// with a silent UI-wide layout shift.
        /// </para>
        /// </summary>
        private const int AtlasWidth = 1024;
        private const int AtlasHeight = 2048;

        // ---- primary static atlas --------------------------------------------

        /// <summary>
        /// Rebakes <c>LiberationSans SDF.asset</c> from
        /// <c>Tools/Fonts/player-character-inventory.txt</c>.
        /// <para>
        /// From scratch rather than incrementally, so the result is a function of
        /// (font file, inventory, atlas settings) and a clean checkout reproduces
        /// the committed asset. An incremental "add the six missing letters" would
        /// have produced an atlas whose layout depended on the order in which
        /// people happened to add things — reproducible by nobody.
        /// </para>
        /// <para>
        /// It goes through <see cref="TMP_FontAsset.TryAddCharacters(uint[], out uint[], bool)"/>,
        /// which is the same path the Font Asset Creator window uses: the glyphs
        /// are rendered by the font engine and packed by the real packer. Editing
        /// the asset's YAML glyph table by hand would produce metrics and rects
        /// that no font engine ever computed.
        /// </para>
        /// <para>
        /// The population mode is flipped to Dynamic for the duration and back
        /// afterwards. That is not a trick — TMP's own setter is what binds
        /// <c>m_SourceFontFile</c> from the editor-only reference on the way in
        /// and nulls it on the way out, so the asset a player receives carries no
        /// reference to the .ttf and cannot bake anything at runtime.
        /// </para>
        /// </summary>
        [MenuItem("Nebula/Release/Rebuild player font atlas")]
        public static void RebuildPlayerFontAtlas()
        {
            uint[] inventory = ReadInventory();

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PrimaryFontPath);
            if (font == null)
                throw new InvalidOperationException($"{PrimaryFontPath} is missing.");

            // A failed rebuild used to be worse than no rebuild. The run that
            // discovered the atlas was full left the asset holding 232 of its
            // original 250 characters and a new hash — the exception was accurate
            // and the asset was still broken, which is the worst combination to
            // hand the next operator. Everything past this point is therefore
            // undone on failure. The .asset file carries the material and the
            // atlas texture as sub-assets, so restoring the file restores all
            // three, and the backup lives under Temp/ so the AssetDatabase never
            // sees a stray asset appear next to the real one.
            string absolute = Path.Combine(ProjectRoot, PrimaryFontPath);
            string backupDirectory = Path.Combine(ProjectRoot, "Temp");
            Directory.CreateDirectory(backupDirectory);
            string backup = Path.Combine(backupDirectory, "nebula-font-atlas-backup.asset");
            File.Copy(absolute, backup, true);

            try
            {
                RebuildInto(font, inventory);
            }
            catch
            {
                File.Copy(backup, absolute, true);
                AssetDatabase.ImportAsset(PrimaryFontPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
                throw;
            }
            finally
            {
                File.Delete(backup);
            }

            // One action, one shipping state. Rebuilding the primary is exactly
            // when the fallback's cache stops being needed, so leaving it for a
            // second menu click would just reproduce the drift this closes.
            ClearDynamicFontData();
        }

        /// <summary>
        /// The rebuild proper. Throws on any failure without swallowing anything,
        /// so the caller's restore path is the single place that decides what a
        /// failure leaves behind.
        /// </summary>
        private static void RebuildInto(TMP_FontAsset font, uint[] inventory)
        {
            int padding = font.atlasPadding;
            GlyphRenderMode renderMode = font.atlasRenderMode;
            int pointSize = font.faceInfo.pointSize;

            // A second atlas texture would be a second sub-asset with a second
            // material, and every prefab in the project binds the first one by
            // fileID. If the inventory ever outgrows one page the correct outcome
            // is this tool failing, not a silent rebind nobody notices until a
            // label renders untextured.
            font.isMultiAtlasTexturesEnabled = false;

            // Before anything is cleared, because ClearFontAssetData seeds the
            // free-rect list from these and resizes the atlas texture to them.
            // Written through SerializedObject because TMP marks both setters
            // internal — the serialized fields are the supported way in, and it
            // keeps m_AtlasTextures[0] the same object, so the texture sub-asset's
            // fileID survives the resize along with both asset GUIDs.
            ApplyAtlasPageSize(font);

            // Binds m_SourceFontFile from m_SourceFontFile_EditorRef. Checked
            // before anything is cleared: past this point the old atlas is gone,
            // and discovering the .ttf is unreachable afterwards would leave the
            // asset empty with nothing to rebuild it from.
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            if (font.sourceFontFile == null)
            {
                font.atlasPopulationMode = AtlasPopulationMode.Static;
                throw new InvalidOperationException(
                    "the source .ttf could not be resolved from the font asset's editor "
                    + "reference, so there is nothing to render glyphs from. Nothing was "
                    + "cleared. Check that Assets/TextMesh Pro/Fonts/LiberationSans.ttf "
                    + "imports with includeFontData: 1.");
            }

            // setAtlasSizeToZero: false keeps m_AtlasTextures[0] at its authored
            // size and, more to the point, keeps it as the same Texture2D
            // sub-object. Resizing to zero and back would still be one object, but
            // the explicit false says the size is authored, not incidental.
            font.ClearFontAssetData(setAtlasSizeToZero: false);

            font.TryAddCharacters(inventory, out uint[] missing, includeFontFeatures: true);

            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.creationSettings = RecordedSettings(font, inventory);
            font.ReadFontAssetDefinition();
            SyncMaterialToAtlasSize(font);
            RestoreAtlasTextureNotReadable(font);

            // The success criterion is "every inventory character is in the
            // character table", never "no exception was thrown". TryAddCharacters
            // returns false for a full atlas and for a glyph the face does not
            // have, and neither of those raises; the first rebuild returned
            // normally while leaving 17 characters unplaced. Checked here, before
            // anything is written, so a rebuild that failed does not reach disk in
            // the first place — the caller's restore is the second line of
            // defence, not the only one.
            var present = new HashSet<uint>(font.characterTable.Select(c => c.unicode));
            var absent = inventory.Where(u => !present.Contains(u)).ToList();

            if (absent.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{absent.Count} of {inventory.Length} inventory characters are not in the "
                    + "rebuilt atlas: " + string.Join(" ", absent.Take(24).Select(u => $"U+{u:X4}"))
                    + (absent.Count > 24 ? " ..." : string.Empty)
                    + $"\nThe page is {font.atlasWidth}x{font.atlasHeight} at point size "
                    + $"{pointSize} with padding {padding}, and it is full — TryAddCharacters "
                    + $"reported {(missing?.Length ?? 0)} unicode(s) it could not place, which it "
                    + "does not distinguish from ones the face genuinely lacks. Before assuming a "
                    + "missing glyph, check the page: sum m_UsedGlyphRects and compare against "
                    + "width*height. Raise AtlasWidth/AtlasHeight in this file — do NOT enable "
                    + "multi-atlas textures, which would add a second material that no prefab is "
                    + "bound to, and do NOT lower point size or padding, which would move every "
                    + "label's metrics.\nThe asset has been restored to its previous contents; "
                    + "nothing was left half-rebuilt.");
            }

            EditorUtility.SetDirty(font);
            if (font.material != null) EditorUtility.SetDirty(font.material);
            if (font.atlasTexture != null) EditorUtility.SetDirty(font.atlasTexture);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "NEBULA-FONT-ATLAS-REBUILT " + inventory.Length
                + "\n  asset      " + PrimaryFontPath
                + "\n  inventory  " + InventoryPath
                + "\n  atlas      " + font.atlasWidth + "x" + font.atlasHeight
                + " padding " + padding + " renderMode " + (int)renderMode
                + "\n  face       point size " + pointSize + " (unchanged; metrics are authored)"
                + "\n  characters " + font.characterTable.Count + " in table, "
                + font.glyphTable.Count + " glyphs"
                + "\n  sha256     " + Sha256(PrimaryFontPath));
        }

        /// <summary>Batch entry point. Exits non-zero so a release script can tell it failed.</summary>
        public static void RebuildPlayerFontAtlasBatch()
        {
            try
            {
                RebuildPlayerFontAtlas();
            }
            catch (Exception exception)
            {
                Debug.LogError("NEBULA-FONT-ATLAS-REBUILD-FAILED\n" + exception);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Parses the generated inventory into sorted, distinct codepoints.
        /// <para>
        /// The <c>COUNT</c> header is checked against the rows because a truncated
        /// inventory is the one failure that would otherwise be silent and
        /// catastrophic: fewer rows means fewer glyphs, fewer glyphs means the
        /// missing ones resolve through the dynamic fallback again, and the atlas
        /// would look perfectly healthy while being wrong.
        /// </para>
        /// </summary>
        private static uint[] ReadInventory()
        {
            string absolute = Path.Combine(ProjectRoot, InventoryPath);
            if (!File.Exists(absolute))
                throw new InvalidOperationException(
                    $"{InventoryPath} is missing. Generate it with "
                    + "`python3 Tools/Fonts/nebula-font-inventory.py`.");

            var codes = new List<uint>();
            int declared = -1;

            foreach (string raw in File.ReadAllLines(absolute))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("#"))
                {
                    const string marker = "# COUNT ";
                    if (line.StartsWith(marker))
                        declared = int.Parse(line.Substring(marker.Length).Trim(),
                            CultureInfo.InvariantCulture);
                    continue;
                }

                string field = line.Split('\t')[0].Trim();
                if (!field.StartsWith("U+"))
                    throw new InvalidOperationException(
                        $"{InventoryPath}: unparsable row '{raw}'. Rows are "
                        + "'U+XXXX<tab>char<tab>name<tab>origin'.");

                codes.Add(uint.Parse(field.Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture));
            }

            uint[] sorted = codes.Distinct().OrderBy(c => c).ToArray();

            if (declared < 0)
                throw new InvalidOperationException($"{InventoryPath}: no '# COUNT n' header.");
            if (declared != sorted.Length)
                throw new InvalidOperationException(
                    $"{InventoryPath} declares {declared} characters but yields {sorted.Length}. "
                    + "The file is truncated or hand-edited; regenerate it with "
                    + "`python3 Tools/Fonts/nebula-font-inventory.py`.");
            if (sorted.Length == 0)
                throw new InvalidOperationException($"{InventoryPath} is empty.");

            return sorted;
        }

        /// <summary>
        /// Writes <see cref="AtlasWidth"/>/<see cref="AtlasHeight"/> onto the font
        /// asset. TMP marks <c>atlasWidth</c>/<c>atlasHeight</c> internal, so the
        /// serialized fields are the reachable surface.
        /// </summary>
        private static void ApplyAtlasPageSize(TMP_FontAsset font)
        {
            var serialized = new SerializedObject(font);
            SerializedProperty width = serialized.FindProperty("m_AtlasWidth");
            SerializedProperty height = serialized.FindProperty("m_AtlasHeight");

            if (width == null || height == null)
                throw new InvalidOperationException(
                    "TMP_FontAsset no longer serializes m_AtlasWidth/m_AtlasHeight under those "
                    + "names, so the atlas page size cannot be set. Nothing was cleared.");

            width.intValue = AtlasWidth;
            height.intValue = AtlasHeight;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Points the shared material's <c>_TextureWidth</c>/<c>_TextureHeight</c>
        /// at the page that now exists.
        /// <para>
        /// TextMesh Pro writes these two floats once, when a font asset is created
        /// (<c>TMP_FontAsset.cs:507</c>), and never again — nothing recomputes
        /// them from the font asset at load or at runtime. Resize the atlas
        /// without touching them and the material keeps describing a 1024x1024
        /// page that is no longer there, which the SDF shader reads for its
        /// screen-space derivatives. The symptom would be text that is subtly
        /// soft or crunchy at some sizes and fine at others: the kind of defect
        /// that survives a screenshot diff.
        /// </para>
        /// <para>
        /// This is the only material write in this file, and it is not a preset
        /// change. Face dilate, outline width and softness, weights, render queue
        /// and the scale ratios are all untouched —
        /// <c>ShaderUtilities.UpdateShaderRatios</c> derives those from
        /// <c>_GradientScale</c> (padding + 1) and the dilate/outline values, none
        /// of which move, so the ratios stay correct without being recomputed.
        /// </para>
        /// </summary>
        private static void SyncMaterialToAtlasSize(TMP_FontAsset font)
        {
            Material material = font.material;
            if (material == null) return;

            if (material.HasProperty(ShaderUtilities.ID_TextureWidth))
                material.SetFloat(ShaderUtilities.ID_TextureWidth, font.atlasWidth);
            if (material.HasProperty(ShaderUtilities.ID_TextureHeight))
                material.SetFloat(ShaderUtilities.ID_TextureHeight, font.atlasHeight);
        }

        /// <summary>
        /// Puts the atlas texture back to non-readable.
        /// <para>
        /// <c>ClearFontAssetData</c> forces <c>m_IsReadable</c> to true so the font
        /// engine can write pixels into it, and never puts it back. Left that way
        /// the shipped asset carries a permanent 1 MB CPU-side copy of a
        /// 1024x1024 Alpha8 texture that only the shader ever reads — and the
        /// committed asset says <c>m_IsReadable: 0</c>, so it would also show up as
        /// an unexplained diff on a file whose bytes are pinned by a provenance
        /// hash.
        /// </para>
        /// <para>
        /// This is what TextMesh Pro's own Font Asset Creator does at the end of a
        /// static bake (<c>TMPro_FontAssetCreatorWindow.cs:1499</c>), for the same
        /// reason. It is not an optimisation invented here.
        /// </para>
        /// </summary>
        private static void RestoreAtlasTextureNotReadable(TMP_FontAsset font)
        {
            if (font.atlasTexture == null) return;

            // Driven through SerializedObject rather than the TextCore editor
            // utility TMP itself calls: m_IsReadable is a serialized field of the
            // sub-asset, this is the public way to reach it, and it does not bind
            // this tool to an editor module's visibility.
            var serialized = new SerializedObject(font.atlasTexture);
            SerializedProperty readable = serialized.FindProperty("m_IsReadable");
            if (readable == null) return;

            readable.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Rewrites the asset's creation settings to describe what was actually
        /// baked, so the Font Asset Creator window opens on the truth.
        /// <para>
        /// It described the stock <c>32 - 126, 160 - 255, ...</c> range and a
        /// packing mode this path does not use, which is worse than describing
        /// nothing: anyone who hit "Generate Font Atlas" from that window would
        /// have quietly rebuilt the asset without a single Turkish letter in it.
        /// </para>
        /// </summary>
        private static FontAssetCreationSettings RecordedSettings(
            TMP_FontAsset font, uint[] inventory)
        {
            FontAssetCreationSettings settings = font.creationSettings;

            settings.characterSetSelectionMode = 6;          // "Unicode Range (Hex)"
            settings.characterSequence = HexSequence(inventory);
            // TryAddCharacters packs with BestShortSideFit unconditionally; the
            // asset previously claimed ContactPointRule, which was never what ran.
            settings.packingMode = (int)GlyphPackingMode.BestShortSideFit;
            settings.includeFontFeatures = true;
            settings.pointSize = font.faceInfo.pointSize;
            settings.padding = font.atlasPadding;
            settings.atlasWidth = font.atlasWidth;
            settings.atlasHeight = font.atlasHeight;
            settings.renderMode = (int)font.atlasRenderMode;

            return settings;
        }

        /// <summary>Collapses the inventory into TMP's "A-B,C" hex range notation.</summary>
        private static string HexSequence(uint[] sorted)
        {
            var builder = new StringBuilder();
            int index = 0;
            while (index < sorted.Length)
            {
                int end = index;
                while (end + 1 < sorted.Length && sorted[end + 1] == sorted[end] + 1) end++;

                if (builder.Length > 0) builder.Append(',');
                builder.Append(sorted[index].ToString("X4", CultureInfo.InvariantCulture));
                if (end != index)
                    builder.Append('-').Append(sorted[end].ToString("X4", CultureInfo.InvariantCulture));

                index = end + 1;
            }
            return builder.ToString();
        }

        // ---- dynamic fallback -------------------------------------------------

        /// <summary>
        /// Empties every dynamic font atlas under the font root.
        /// <para>
        /// <c>LiberationSans SDF - Fallback.asset</c> is dynamic
        /// (<c>m_AtlasPopulationMode: 1</c>) with <c>m_ClearDynamicDataOnBuild: 1</c>,
        /// which means anything it bakes changes the file on disk and none of it
        /// ships — TextMesh Pro empties the whole cache during every player build.
        /// A raw-byte hash of the working-tree file therefore records a transient
        /// editor state and describes something no player will ever hold, so the
        /// provenance record carries the cleared hash and this produces it on
        /// demand.
        /// </para>
        /// <para>
        /// Since <see cref="RebuildPlayerFontAtlas"/> this should have nothing to
        /// do, and that is the point: the primary static atlas covers every
        /// player-facing character, so nothing reaches the fallback to bake. If
        /// this ever reports a non-zero glyph count again, the finding is not "the
        /// cache needed clearing" — it is that some string escaped the inventory
        /// and the whole architecture has a hole in it. Regenerate the inventory
        /// and rebuild before reaching for this.
        /// </para>
        /// <para>
        /// The fallback stays bound rather than being unlinked. Unbinding would
        /// make the gate that watches it unfalsifiable, and the eight player-facing
        /// characters LiberationSans.ttf has no glyph for — the strategic map's
        /// legend diamonds, the risk band's warning and skull — reach it today and
        /// bake nothing, because a glyph the face lacks cannot be cached. The
        /// fallback is a witness, not a leak.
        /// </para>
        /// <para>
        /// A deliberate editor action and not an <c>AssetPostprocessor</c>:
        /// clearing an atlas mid-session while text is on screen would make TMP
        /// rebuild it immediately and achieve nothing but churn.
        /// </para>
        /// </summary>
        [MenuItem("Nebula/Release/Clear dynamic font atlases")]
        public static void ClearDynamicFontData()
        {
            var report = new List<string>();
            int cleared = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { FontRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null) continue;

                // Static atlases are authored content: their glyphs were baked on
                // purpose and are the thing that ships. Only dynamic ones cache.
                if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic)
                {
                    report.Add($"{path}: static atlas, left alone");
                    continue;
                }

                int glyphsBefore = font.glyphTable?.Count ?? 0;
                font.ClearFontAssetData(true);
                EditorUtility.SetDirty(font);
                cleared++;
                report.Add($"{path}: cleared {glyphsBefore} baked glyph(s)"
                    + (glyphsBefore > 0
                        ? "  <-- the static atlas has a hole; rerun the inventory scan"
                        : string.Empty));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { FontRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                report.Add($"{path}: sha256 {Sha256(path)}");
            }

            Debug.Log("NEBULA-FONT-ATLAS-CLEARED " + cleared + "\n  " + string.Join("\n  ", report));
        }

        /// <summary>Batch entry point. Exits non-zero so a release script can tell it failed.</summary>
        public static void ClearDynamicFontDataBatch()
        {
            try
            {
                ClearDynamicFontData();
            }
            catch (Exception exception)
            {
                Debug.LogError("NEBULA-FONT-ATLAS-CLEAR-FAILED\n" + exception);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        private static string ProjectRoot =>
            Directory.GetParent(Application.dataPath).FullName;

        private static string Sha256(string assetPath)
        {
            string absolute = Path.Combine(ProjectRoot, assetPath);
            if (!File.Exists(absolute)) return "missing";
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(absolute))
                return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }
    }
}
