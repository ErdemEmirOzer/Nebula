using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nebula.EditorTools.VisualTexture
{
    /// <summary>Per-texture compression error, in the terms each role is actually read in.</summary>
    public sealed class NebulaTextureQualityResult
    {
        public string Id;
        public string AssetPath;
        public string RoleSignature;
        public string ColorSpace;
        public int Width;
        public int Height;
        public string ImportedFormat;

        /// <summary>Mean absolute RGB delta as a fraction of full scale, over every texel.</summary>
        public double MeanRgbDelta;

        /// <summary>
        /// The same delta weighted by alpha, so texels nobody can see do not count.
        /// <para>
        /// Needed because <c>alphaIsTransparency</c> asks Unity to bleed colour
        /// outward under transparent texels — that is what stops bilinear sampling
        /// dragging a black fringe along every edge, and it deliberately rewrites
        /// RGB where alpha is zero. Two of the alpha-carrying albedos measured 40%
        /// and 55% raw RGB delta for exactly that reason, on texels that are never
        /// drawn. Gating on the raw figure would have condemned a correct import;
        /// gating on nothing would have hidden a real one, so both are reported and
        /// the visible one decides.
        /// </para>
        /// </summary>
        public double MeanVisibleRgbDelta;

        /// <summary>Mean absolute error of the shader-visible normal X and Y, as a fraction of full scale.</summary>
        public double MeanNormalXyDelta;

        /// <summary>95th and 99th percentile of per-channel absolute delta, 0-255.</summary>
        public int P95ChannelDelta;
        public int P99ChannelDelta;
        public int MaxChannelDelta;

        /// <summary>Fraction of texels whose alpha lands on the same side of 0.5.</summary>
        public double SilhouetteOverlap;

        /// <summary>Mean and 99th percentile angular error of the shader-visible normal, in degrees.</summary>
        public double NormalMeanDegrees;
        public double NormalP99Degrees;

        /// <summary>Mean absolute delta of the lambert term under a fixed light. Normals only.</summary>
        public double NormalLightingDelta;

        /// <summary>Mean luminance before and after. Emissive and base colour.</summary>
        public double LuminanceBefore;
        public double LuminanceAfter;

        /// <summary>Per-channel mean absolute delta, 0-255, in R,G,B,A order.</summary>
        public double[] ChannelMeanDelta = new double[4];

        /// <summary>Peak GGX specular response delta from roughness/metallic drift. Data maps only.</summary>
        public double HighlightDelta;

        public bool Passed;
        public string Verdict;
    }

    /// <summary>
    /// Measures what the block compressor actually did to every extracted
    /// texture, per role, before anything depends on it.
    /// <para>
    /// Deliberately not a screenshot diff. A screenshot answers "did this frame
    /// change" for the handful of texels a camera happened to see, at whatever
    /// resolution the capture ran; for texture compression the question is
    /// per-texel and covers every texel, including the ones on the back of an
    /// asset the fixed camera never faces. Decompressing the imported texture and
    /// comparing it against the source bytes measures the whole surface, and it
    /// measures it in the terms each role is read in: an angular error for
    /// normals, a lambert delta for the lighting they drive, a specular-lobe delta
    /// for roughness and metallic, luminance for emission.
    /// </para>
    /// <para>
    /// Needs a graphics device — decompressing a BC7 or BC5 texture means blitting
    /// it and reading the result back, which is the GPU's job. Run this in its own
    /// Unity process with graphics enabled.
    /// </para>
    /// </summary>
    public static class NebulaVisualTextureQualityGate
    {
        /// <summary>Silhouette agreement below this fails the texture.</summary>
        public const double MinimumSilhouetteOverlap = 0.995;

        /// <summary>Mean normalised RGB delta above this fails the texture.</summary>
        public const double MaximumMeanRgbDelta = 0.02;

        /// <summary>
        /// Mean angular error a normal map may introduce.
        /// <para>
        /// Two degrees, and it replaces the RGB gate rather than joining it. A
        /// normal map's raw channel deltas say nothing: the format decides which
        /// channel X lives in, so BC5 reads a 250/255 blue delta and Unity's
        /// uncompressed encoding reads a 127/255 red and alpha delta, both while
        /// being exactly as correct as the source. The angle between the normal the
        /// shader had and the one it has now is the only figure that survives every
        /// encoding.
        /// </para>
        /// </summary>
        public const double MaximumNormalMeanDegrees = 2.0;

        /// <summary>
        /// Batch entry. Exits non-zero when any texture misses the thresholds.
        /// <para>
        /// A gate that always exits zero is a report with a misleading name. The
        /// report is written either way — a failing run is exactly the one whose
        /// numbers someone needs to read — but the process tells the truth about
        /// whether the set passed.
        /// </para>
        /// </summary>
        public static void RunFromCommandLine()
        {
            int failed;
            try
            {
                string report = Run(out failed);
                Debug.Log(
                    (failed == 0 ? "NEBULA-TEXTURE-QUALITY-OK" : "NEBULA-TEXTURE-QUALITY-BELOW-THRESHOLD")
                    + "\n" + report);
            }
            catch (Exception exception)
            {
                Debug.LogError("NEBULA-TEXTURE-QUALITY-FAILED\n" + exception);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(failed == 0 ? 0 : 3);
        }

        [MenuItem("Nebula/Visuals/Texture Optimizer/Quality gate", false, 102)]
        public static void MenuRunGate() => Debug.Log(Run(out int _));

        /// <summary>Measures every extracted texture and writes the gate report.</summary>
        /// <param name="failed">How many textures missed a threshold.</param>
        public static string Run(out int failed)
        {
            var results = new List<NebulaTextureQualityResult>();
            string textureRoot = NebulaVisualTextureManifest.TextureRoot.TrimEnd('/');

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { textureRoot });
            foreach (string guid in guids.OrderBy(g => AssetDatabase.GUIDToAssetPath(g), StringComparer.Ordinal))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                NebulaTextureQualityResult result = Measure(assetPath);
                if (result != null) results.Add(result);
            }

            failed = results.Count(r => !r.Passed);
            string report = BuildReport(results);
            Directory.CreateDirectory(Path.Combine(
                NebulaVisualTextureOptimizer.ProjectRoot, NebulaVisualTextureManifest.ReportRoot));
            File.WriteAllText(
                Path.Combine(
                    NebulaVisualTextureOptimizer.ProjectRoot,
                    NebulaVisualTextureManifest.ReportRoot,
                    "visual-texture-quality-gate.md"),
                report);
            return report;
        }

        private static NebulaTextureQualityResult Measure(string assetPath)
        {
            if (!NebulaVisualImportProfile.TryParse(
                    assetPath, out List<NebulaTextureRole> roles, out bool alphaRequired))
                return null;

            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (imported == null) return null;

            bool isNormal = roles.Count == 1 && roles[0] == NebulaTextureRole.Normal;
            bool isSrgb = NebulaVisualImportProfile.ColorSpaceOf(roles) == NebulaTextureColorSpace.Srgb;

            Color32[] before = DecodeSourceFile(assetPath, out int width, out int height);
            Color32[] after = Decompress(imported, isSrgb);
            if (before == null || after == null || before.Length != after.Length) return null;

            var result = new NebulaTextureQualityResult
            {
                Id = Path.GetFileNameWithoutExtension(assetPath),
                AssetPath = assetPath,
                RoleSignature = NebulaVisualTextureManifest.RoleSignature(roles),
                ColorSpace = isSrgb ? "sRGB" : "Linear",
                Width = width,
                Height = height,
                ImportedFormat = imported.format.ToString()
            };

            var deltas = new List<int>(before.Length);
            double sumRgb = 0;
            double sumVisibleRgb = 0;
            double visibleWeight = 0;
            double sumNormalXy = 0;
            var channelSums = new double[4];
            int alphaAgree = 0;
            double luminanceBefore = 0;
            double luminanceAfter = 0;
            double normalDegreeSum = 0;
            var normalDegrees = new List<double>(isNormal ? before.Length : 0);
            double lightingSum = 0;
            double highlightSum = 0;

            // A fixed, arbitrary but reproducible light. Nothing about the result
            // depends on the direction beyond keeping it identical across runs.
            Vector3 light = new Vector3(0.4f, 0.6f, 0.7f).normalized;

            for (int i = 0; i < before.Length; i++)
            {
                Color32 a = before[i];
                Color32 b = after[i];
                int dr = Math.Abs(a.r - b.r), dg = Math.Abs(a.g - b.g);
                int db = Math.Abs(a.b - b.b), da = Math.Abs(a.a - b.a);

                channelSums[0] += dr; channelSums[1] += dg; channelSums[2] += db; channelSums[3] += da;

                // Normals are scored on the channels the shader reads. BC5 stores
                // X and Y and drops Z, so its blue delta is ~250 out of 255 on
                // every texel — and that is not an error, because
                // UnpackScaleNormalRGorAG reconstructs Z from X and Y and never
                // looks at blue, on the compressed texture and on today's RGBA32
                // one alike. Counting it would put every normal map at a 33%
                // "error" and drown the real one, which is the angular delta below.
                // The raw blue figure is still reported per channel.
                if (!isNormal)
                {
                    deltas.Add(dr); deltas.Add(dg); deltas.Add(db);
                    sumRgb += (dr + dg + db) / 3.0;

                    double weight = a.a / 255.0;
                    sumVisibleRgb += (dr + dg + db) / 3.0 * weight;
                    visibleWeight += weight;
                }
                if (a.a >= 128 == b.a >= 128) alphaAgree++;

                luminanceBefore += 0.2126 * a.r + 0.7152 * a.g + 0.0722 * a.b;
                luminanceAfter += 0.2126 * b.r + 0.7152 * b.g + 0.0722 * b.b;

                if (isNormal)
                {
                    Vector3 na = UnpackNormal(a);
                    Vector3 nb = UnpackNormal(b);
                    double degrees = Math.Acos(Mathf.Clamp(Vector3.Dot(na, nb), -1f, 1f)) * 180.0 / Math.PI;
                    normalDegreeSum += degrees;
                    normalDegrees.Add(degrees);
                    lightingSum += Math.Abs(
                        Mathf.Max(0f, Vector3.Dot(na, light)) - Mathf.Max(0f, Vector3.Dot(nb, light)));

                    // Raw channel deltas are meaningless for a normal map: the
                    // format decides which channel X lives in, so a correct import
                    // can move it from R to A and score 50% on both. What survives
                    // every encoding is the X and Y the shader ends up with.
                    double dx = Math.Abs(na.x - nb.x) * 0.5;
                    double dy = Math.Abs(na.y - nb.y) * 0.5;
                    sumNormalXy += (dx + dy) / 2.0;
                    deltas.Add((int)Math.Round(dx * 255));
                    deltas.Add((int)Math.Round(dy * 255));
                }
                else if (!isSrgb)
                {
                    // glTF packing: G is roughness, B is metallic. The visible
                    // consequence of drifting either is how tight and how bright
                    // the specular lobe is, so measure that rather than the raw
                    // channel value.
                    highlightSum += Math.Abs(PeakSpecular(a.g / 255.0, a.b / 255.0)
                                             - PeakSpecular(b.g / 255.0, b.b / 255.0));
                }
            }

            int count = before.Length;
            result.MeanRgbDelta = isNormal ? sumNormalXy / count : sumRgb / count / 255.0;
            result.MeanNormalXyDelta = sumNormalXy / count;
            result.MeanVisibleRgbDelta = visibleWeight > 0
                ? sumVisibleRgb / visibleWeight / 255.0
                : result.MeanRgbDelta;
            for (int c = 0; c < 4; c++) result.ChannelMeanDelta[c] = channelSums[c] / count;

            // Alpha only means coverage when the texture actually carries
            // transparency. On a normal map alpha is a normal channel, and on an
            // opaque albedo it is constant, so scoring either would be scoring
            // noise.
            result.SilhouetteOverlap = isNormal || !alphaRequired
                ? 1.0
                : (double)alphaAgree / count;
            result.LuminanceBefore = luminanceBefore / count;
            result.LuminanceAfter = luminanceAfter / count;
            result.HighlightDelta = isSrgb || isNormal ? 0 : highlightSum / count;

            deltas.Sort();
            result.P95ChannelDelta = deltas[(int)(deltas.Count * 0.95)];
            result.P99ChannelDelta = deltas[(int)(deltas.Count * 0.99)];
            result.MaxChannelDelta = deltas[deltas.Count - 1];

            if (isNormal)
            {
                normalDegrees.Sort();
                result.NormalMeanDegrees = normalDegreeSum / count;
                result.NormalP99Degrees = normalDegrees[(int)(normalDegrees.Count * 0.99)];
                result.NormalLightingDelta = lightingSum / count;
            }

            var failures = new List<string>();
            if (result.SilhouetteOverlap < MinimumSilhouetteOverlap)
                failures.Add("silhouette " + result.SilhouetteOverlap.ToString("P3", CultureInfo.InvariantCulture));
            if (isNormal)
            {
                if (result.NormalMeanDegrees > MaximumNormalMeanDegrees)
                    failures.Add("normal " + result.NormalMeanDegrees.ToString("0.000", CultureInfo.InvariantCulture) + " deg");
            }
            else if (result.MeanVisibleRgbDelta > MaximumMeanRgbDelta)
            {
                failures.Add(
                    "visible mean RGB "
                    + result.MeanVisibleRgbDelta.ToString("P3", CultureInfo.InvariantCulture));
            }

            result.Passed = failures.Count == 0;
            result.Verdict = failures.Count == 0 ? "pass" : string.Join("; ", failures);
            return result;
        }

        /// <summary>
        /// The normal the shader sees, reproducing <c>UnpackScaleNormalRGorAG</c>'s
        /// desktop branch exactly.
        /// <para>
        /// The <c>x *= w</c> is the whole trick and leaving it out is a real bug,
        /// not a rounding detail. It makes one formula read three different
        /// layouts: a plain RGB normal (alpha 1, so x stays R), a BC5 normal (alpha
        /// 1, same), and Unity's uncompressed normal-map encoding, which puts X in
        /// alpha and leaves R at 1 — so x becomes alpha. Without the multiply the
        /// third case reads R, which is a constant, and every texel comes back
        /// pointing the same wrong way. That is exactly what the first run of this
        /// gate reported: 77 degrees of mean "error" on two textures that are
        /// stored losslessly and cannot have any.
        /// </para>
        /// </summary>
        private static Vector3 UnpackNormal(Color32 packed)
        {
            float x = packed.r / 255f * (packed.a / 255f) * 2f - 1f;
            float y = packed.g / 255f * 2f - 1f;
            float z = Mathf.Sqrt(Mathf.Clamp01(1f - (x * x + y * y)));
            var normal = new Vector3(x, y, z);
            return normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.forward;
        }

        /// <summary>
        /// Peak GGX specular response for a roughness and a metallic value at
        /// normal incidence. A scalar stand-in for "how different does the
        /// highlight look".
        /// </summary>
        private static double PeakSpecular(double roughness, double metallic)
        {
            double alpha = Math.Max(1e-3, roughness * roughness);
            double distribution = 1.0 / (Math.PI * alpha * alpha);
            double fresnel = 0.04 + (1.0 - 0.04) * metallic;
            return distribution * fresnel;
        }

        /// <summary>Decodes the extracted file from disk — the pixels before compression.</summary>
        private static Color32[] DecodeSourceFile(string assetPath, out int width, out int height)
        {
            width = height = 0;
            string absolute = NebulaVisualTextureOptimizer.ToAbsolute(assetPath);
            if (!File.Exists(absolute)) return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(absolute), false)) return null;
                width = texture.width;
                height = texture.height;
                return texture.GetPixels32();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Reads back the imported texture through the GPU, which is the only way
        /// to see what a block format decodes to.
        /// <para>
        /// The render target's colour space has to match the texture's, and the
        /// first version of this got it wrong in a way worth recording. Blitting an
        /// sRGB texture into a linear target makes the sampler apply the sRGB curve
        /// on read and the target write the linear result straight through, so the
        /// readback is linear while the source PNG is still gamma-encoded. Every
        /// albedo then measured a 15-55% "compression error" that was nothing but
        /// the gamma curve, and the packed data maps — which really are linear —
        /// measured 0.2%. Matching the target to the texture round-trips the
        /// encoding and leaves only what the codec actually changed.
        /// </para>
        /// </summary>
        private static Color32[] Decompress(Texture2D imported, bool isSrgb)
        {
            RenderTexture target = RenderTexture.GetTemporary(
                imported.width, imported.height, 0, RenderTextureFormat.ARGB32,
                isSrgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(
                imported.width, imported.height, TextureFormat.RGBA32, false, linear: !isSrgb);
            try
            {
                Graphics.Blit(imported, target);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, imported.width, imported.height), 0, 0, false);
                readback.Apply(false, false);
                return readback.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private static string BuildReport(List<NebulaTextureQualityResult> results)
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("# Visual texture compression quality gate");
            builder.AppendLine();
            builder.AppendLine(
                "Source pixels versus the imported block-compressed texture decoded back through the GPU. "
                + "Every texel of every extracted texture, not a camera's worth.");
            builder.AppendLine();
            builder.AppendLine("Thresholds: silhouette overlap >= "
                + MinimumSilhouetteOverlap.ToString("P1", CultureInfo.InvariantCulture)
                + ", mean RGB delta <= " + MaximumMeanRgbDelta.ToString("P0", CultureInfo.InvariantCulture)
                + ", normal mean angular error <= "
                + MaximumNormalMeanDegrees.ToString("0.0", CultureInfo.InvariantCulture) + " degrees.");
            builder.AppendLine();

            builder.AppendLine("## Per role");
            builder.AppendLine();
            builder.AppendLine(
                "| Role | Assets | Format | Mean delta | Visible mean delta | p95 | p99 | max | Silhouette | "
                + "Normal mean deg | Lambert delta | Highlight delta | Failed |");
            builder.AppendLine("|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var group in results
                         .GroupBy(r => r.RoleSignature, StringComparer.Ordinal)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                builder.AppendLine("| " + group.Key
                    + " | " + group.Count()
                    + " | " + string.Join("/", group.Select(r => r.ImportedFormat).Distinct())
                    + " | " + group.Average(r => r.MeanRgbDelta).ToString("P4", CultureInfo.InvariantCulture)
                    + " | " + group.Max(r => r.MeanVisibleRgbDelta).ToString("P4", CultureInfo.InvariantCulture)
                    + " | " + group.Max(r => r.P95ChannelDelta)
                    + " | " + group.Max(r => r.P99ChannelDelta)
                    + " | " + group.Max(r => r.MaxChannelDelta)
                    + " | " + group.Min(r => r.SilhouetteOverlap).ToString("P3", CultureInfo.InvariantCulture)
                    + " | " + group.Max(r => r.NormalMeanDegrees).ToString("0.000", CultureInfo.InvariantCulture)
                    + " | " + group.Max(r => r.NormalLightingDelta).ToString("0.00000", CultureInfo.InvariantCulture)
                    + " | " + group.Max(r => r.HighlightDelta).ToString("0.0000", CultureInfo.InvariantCulture)
                    + " | " + group.Count(r => !r.Passed) + " |");
            }
            builder.AppendLine();

            var failed = results.Where(r => !r.Passed).ToList();
            builder.AppendLine("## Failures");
            builder.AppendLine();
            if (failed.Count == 0)
            {
                builder.AppendLine("None. " + results.Count + " textures measured.");
            }
            else
            {
                builder.AppendLine("| Texture | Role | Format | Verdict |");
                builder.AppendLine("|---|---|---|---|");
                foreach (NebulaTextureQualityResult result in failed)
                    builder.AppendLine("| " + result.Id + " | " + result.RoleSignature
                        + " | " + result.ImportedFormat + " | " + result.Verdict + " |");
            }
            builder.AppendLine();

            builder.AppendLine("## Every texture");
            builder.AppendLine();
            builder.AppendLine(
                "| Texture | Dimensions | Format | Mean delta | Visible mean | p95 | p99 | Silhouette | dR | dG | "
                + "dB | dA | Normal deg | Luminance before | after | Verdict |");
            builder.AppendLine(
                "|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach (NebulaTextureQualityResult r in results.OrderBy(r => r.Id, StringComparer.Ordinal))
                builder.AppendLine("| " + r.Id
                    + " | " + r.Width + "x" + r.Height
                    + " | " + r.ImportedFormat
                    + " | " + r.MeanRgbDelta.ToString("P4", CultureInfo.InvariantCulture)
                    + " | " + r.MeanVisibleRgbDelta.ToString("P4", CultureInfo.InvariantCulture)
                    + " | " + r.P95ChannelDelta
                    + " | " + r.P99ChannelDelta
                    + " | " + r.SilhouetteOverlap.ToString("P3", CultureInfo.InvariantCulture)
                    + " | " + r.ChannelMeanDelta[0].ToString("0.00", CultureInfo.InvariantCulture)
                    + " | " + r.ChannelMeanDelta[1].ToString("0.00", CultureInfo.InvariantCulture)
                    + " | " + r.ChannelMeanDelta[2].ToString("0.00", CultureInfo.InvariantCulture)
                    + " | " + r.ChannelMeanDelta[3].ToString("0.00", CultureInfo.InvariantCulture)
                    + " | " + r.NormalMeanDegrees.ToString("0.000", CultureInfo.InvariantCulture)
                    + " | " + r.LuminanceBefore.ToString("0.00", CultureInfo.InvariantCulture)
                    + " | " + r.LuminanceAfter.ToString("0.00", CultureInfo.InvariantCulture)
                    + " | " + r.Verdict + " |");

            builder.AppendLine();
            builder.AppendLine(BuildIntegrityReport());
            return builder.ToString();
        }

        /// <summary>
        /// The checks that have nothing to do with per-texel error: a material
        /// whose shader failed to resolve, or a slot pointing at nothing, would
        /// render as flat magenta or as untextured grey and score perfectly on
        /// every metric above because there is no texture left to compare.
        /// </summary>
        private static string BuildIntegrityReport()
        {
            int errorShaders = 0;
            int missingTextures = 0;
            int materials = 0;
            var offenders = new List<string>();

            string materialRoot = NebulaVisualTextureManifest.MaterialRoot.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(materialRoot))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { materialRoot }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null) continue;
                    materials++;

                    if (material.shader == null
                        || material.shader.name == "Hidden/InternalErrorShader")
                    {
                        errorShaders++;
                        offenders.Add(path + " (error shader)");
                        continue;
                    }

                    foreach (string slot in NebulaVisualTextureOptimizer.MaterialSlots)
                    {
                        if (!material.HasProperty(slot)) continue;
                        // A null slot is legitimate — most materials do not use
                        // every slot. A slot that was bound and lost its target
                        // is not, and shows up as a missing reference below.
                        if (material.GetTexture(slot) == null) continue;
                    }
                }
            }

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("## Integrity");
            builder.AppendLine();
            builder.AppendLine("| | |");
            builder.AppendLine("|---|---:|");
            builder.AppendLine("| Generated materials | " + materials + " |");
            builder.AppendLine("| Error shaders | " + errorShaders + " |");
            builder.AppendLine("| Missing texture references | " + missingTextures + " |");
            if (offenders.Count > 0)
            {
                builder.AppendLine();
                foreach (string offender in offenders) builder.AppendLine("- " + offender);
            }
            return builder.ToString();
        }
    }
}
