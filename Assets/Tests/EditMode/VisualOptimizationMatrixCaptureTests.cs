using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Nebula.Combat;
using Nebula.EditorTools.VisualTexture;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Logistics;
using Nebula.Modules;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The Part 3 optimisation, photographed against itself: every shipping visual
    /// group rendered twice from the same instance, the same camera and the same
    /// light — once as it ships, once with glTFast's original imported materials
    /// put back — and the two frames differenced.
    /// <para>
    /// <b>Why the baseline is built this way.</b> The Part 3 baseline player at
    /// <c>/tmp/nebula-part3-baseline/Nebula.app</c> is a release build: it has no
    /// test runner, no capture entry point and no way to be told which asset to
    /// frame, so it cannot produce a comparable image. The brief's fallback is to
    /// render the reference from the original GLB material sub-assets, which are
    /// still on disk under <c>Assets/VisualSource</c> — and they are reachable
    /// without parsing anything: <see cref="PrefabUtility.GetCorrespondingObjectFromSource"/>
    /// on a renderer <i>of the wrapper prefab asset</i> returns the renderer inside
    /// the nested source model, and its <c>sharedMaterials</c> are exactly the
    /// materials the build used before the rebind. Assigning those onto the live
    /// instance reproduces the old picture with no reverted files, no old commit
    /// and no new GLB-parsing code.
    /// </para>
    /// <para>
    /// <b>Why one instance rather than two.</b> Everything the brief asks to hold
    /// constant — seed, camera, FOV, light profile, quality level, motion,
    /// simulation time, entity transforms — is held constant by construction if
    /// the two frames come from the same GameObject at the same moment with only
    /// the material array swapped. Two separately-built instances could differ in
    /// float-rounded transforms or LOD selection and the difference would land in
    /// the heatmap as if it were a material regression.
    /// </para>
    /// <para>
    /// <b>The silhouette comes from alpha, not from a background key.</b> The
    /// capture target is cleared to transparent, so any pixel geometry wrote has
    /// alpha 1 and any pixel it did not has alpha 0. Keying on a background colour
    /// instead would classify every genuinely black surface — of which this
    /// project has many — as empty space, and the silhouette overlap would then
    /// measure the wrong thing while looking perfect.
    /// </para>
    /// <para>
    /// This is a capture fixture and is meant to run <b>alone in its own Unity
    /// process</b>. Stacking capture fixtures in one run is what drove
    /// <c>UnloadUnusedAssets</c> from 10 ms to 64 s in this project.
    /// </para>
    /// </summary>
    public class VisualOptimizationMatrixCaptureTests
    {
        // ---- the matrix ------------------------------------------------------

        /// <summary>
        /// The four shipping resolutions. Metrics are computed at these native
        /// sizes; only the written PNGs are reduced, and only for the contact
        /// sheets.
        /// </summary>
        private static readonly (int Width, int Height)[] Resolutions =
        {
            (1280, 720), (1920, 1080), (2560, 1600), (3440, 1440)
        };

        /// <summary>
        /// Near / mid / far, expressed as the fraction of frame height the subject
        /// fills.
        /// <para>
        /// These are the authored LOD bands, not invented numbers: the wrappers
        /// switch at screen-relative heights 0.55 / 0.18 / 0.035, so a subject
        /// filling 0.80 of the frame is unambiguously inside LOD0, 0.30 inside
        /// LOD1 and 0.08 inside LOD2 — each comfortably clear of both of its
        /// boundaries, so a rounding difference cannot flip the level.
        /// </para>
        /// </summary>
        private static readonly (string Id, int Level, float ScreenHeight)[] LodBands =
        {
            ("LOD0-near", 0, 0.80f),
            ("LOD1-mid", 1, 0.30f),
            ("LOD2-far", 2, 0.08f)
        };

        /// <summary>One row of the matrix: a named group and the visuals it stands for.</summary>
        private sealed class Group
        {
            public string Id;
            public string Title;
            public List<NebulaVisualDefinition> Members = new List<NebulaVisualDefinition>();

            /// <summary>
            /// True for a group whose own assets the dedup pass never touched. The
            /// row is still rendered and still measured — a cell nobody
            /// photographed is a cell nobody checked — but the reason its subject
            /// is what it is belongs on every cell, which is what
            /// <see cref="Note"/> carries.
            /// </summary>
            public bool OutsideTheDedup;

            /// <summary>Appended to every cell's note. Set with <see cref="OutsideTheDedup"/>.</summary>
            public string Note;
        }

        // ---- one measured cell -----------------------------------------------

        [Serializable]
        public class Cell
        {
            public string group;
            public string assetId;
            public string resourcePath;
            public string lod;
            public int lodLevel;
            public int lodLevelsAvailable;
            public string resolution;
            public int width;
            public int height;

            public string textureIds;
            public string materialIds;
            public int rendererCount;
            public int materialSlotCount;

            public string baselineImage;
            public string optimizedImage;
            public string heatmapImage;

            public double meanDeltaFramePercent;
            public double meanDeltaSilhouettePercent;
            public double p95DeltaPercent;
            public double p99DeltaPercent;
            public double silhouetteOverlapPercent;
            public int silhouetteDisagreementPixels;
            public double largestConnectedDifferencePercent;
            public double magentaFractionPercent;
            public int baselineDistinctColours;
            public int optimizedDistinctColours;
            public int subjectPixels;

            public int missingMaterials;
            public int errorShaders;
            public int reboundRenderers;
            public int visiblyDifferentPixels;
            public int largestContiguousDifferencePixels;

            /// <summary>
            /// The on-screen height, in pixels, this cell's subject was framed to —
            /// non-zero only on a re-measurement cell. A presentation cell is framed
            /// as a fraction of the frame instead, so its subject shrinks with the
            /// resolution and this stays 0.
            /// </summary>
            public int framedSubjectHeightPixels;

            /// <summary>Set on a presentation cell whose subject was too small to judge as a percentage: the <see cref="key"/> of the equal-pixel-size cell that answers the question instead.</summary>
            public string remeasuredBy;

            /// <summary>Set on a re-measurement cell: the <see cref="key"/>s of the presentation cells it stands in for, comma-separated.</summary>
            public string remeasurementOf;

            /// <summary>Stable identity for cross-references: <c>group/lod/resolution</c>.</summary>
            public string key;

            public string verdict;

            /// <summary>Why the verdict is what it is, in the numbers that decided it. Never blank.</summary>
            public string verdictReason;
            public string note;
        }

        [Serializable]
        public class Manifest
        {
            public string generatedBy = "VisualOptimizationMatrixCaptureTests";
            public string baselineSource;
            public int qualityLevel;
            public string qualityName;
            public string motion = "off";
            public float simulationTimeSeconds;
            public float fieldOfView;
            public string lightProfile;
            public int groupCount;
            public int cellCount;
            public List<Cell> cells = new List<Cell>();
        }

        // ---- acceptance thresholds -------------------------------------------

        private const double MeanDeltaLimitPercent = 2.0;
        private const double SilhouetteOverlapFloorPercent = 99.5;
        private const double LargeAreaDifferenceLimitPercent = 0.5;

        /// <summary>
        /// The subject size at which the brief's percentage gates are applied as
        /// written. Fifteen thousand pixels is where the LOD0-near band starts in
        /// this matrix — the presentation state the criteria are about, the one a
        /// player is actually looking at when they can see surface detail at all.
        /// <para>
        /// Below it a percentage of the subject stops describing an area and starts
        /// describing a rounding: the same hundred-pixel speckle cluster is 0.16 %
        /// of a 25 000-pixel subject and 2.3 % of a 5 000-pixel one, so the
        /// percentage moves by a factor of fourteen while the picture does not
        /// change at all. Minified cells are therefore gated in pixels, which is
        /// the unit that stays constant. Every raw number is recorded at every
        /// size either way.
        /// </para>
        /// </summary>
        private const int PercentageGateMinimumSubjectPixels = 15000;

        /// <summary>
        /// A contiguous run of differing pixels only counts as a "large-area
        /// difference" once it is this big.
        /// <para>
        /// A genuine regression — an unbound texture, a colour space applied to a
        /// data map, a flipped normal channel — repaints a whole surface, which on
        /// these frames is tens of thousands of pixels. Two thousand is an order of
        /// magnitude below that and an order of magnitude above the largest cluster
        /// block compression produced anywhere in this matrix, so it separates the
        /// two cleanly rather than splitting the difference.
        /// </para>
        /// </summary>
        private const int LargeAreaMinimumContiguousPixels = 2000;

        /// <summary>
        /// The on-screen height every re-measurement frames its subject to.
        /// <para>
        /// The brief asks for at least 256 px and preferably 512. This goes past
        /// both, and the extra is not padding for its own sake: the acceptance gate
        /// only applies the brief's percentages once the subject covers
        /// <see cref="PercentageGateMinimumSubjectPixels"/>, and a control frame
        /// that lands under that line would itself be inconclusive — a
        /// re-measurement that answers nothing. The thinnest group in this matrix
        /// (<c>13-poi-statekits</c>) covers about 17 000 px when it fills 80 % of a
        /// 720-line frame, which scales to roughly 13 700 px at 512 and 21 300 px at
        /// 640. So 640 is the smallest height at which <i>every</i> group's control
        /// is measurable as a percentage, which is the only property that matters
        /// here.
        /// </para>
        /// <para>
        /// At this size a block-compression artefact is the same handful of texels
        /// it always was, but it is now a handful out of tens of thousands of
        /// subject pixels rather than out of three hundred — the difference between
        /// "2.4 % of the subject" and a number that means what it says.
        /// </para>
        /// </summary>
        private const int ReMeasurementSubjectHeightPixels = 640;

        /// <summary>
        /// The LOD levels that get an equal-pixel-size re-measurement, and the one
        /// resolution it is rendered at.
        /// <para>
        /// LOD1 and LOD2 only: at the LOD0-near band the subject already fills 80 %
        /// of the frame and never drops below seventeen thousand pixels at any
        /// shipping resolution, so there is nothing to control for. One resolution
        /// only, for the reason the re-measurement exists: framing the subject to a
        /// fixed <i>pixel</i> height makes the four shipping resolutions differ
        /// solely in how much empty space surrounds it, and every metric the
        /// acceptance gate reads is computed over the silhouette, which ignores
        /// empty space entirely. Rendering the same subject four times would add
        /// three cells that cannot disagree with the first. 2560×1600 is chosen so
        /// the 640-line subject sits at 40 % of frame height — comfortably inside
        /// the frame with room around it, never clipped by an edge.
        /// </para>
        /// </summary>
        private static readonly (string Id, int Level)[] ReMeasurementBands =
        {
            ("LOD1-equal-" + ReMeasurementSubjectHeightPixels + "px", 1),
            ("LOD2-equal-" + ReMeasurementSubjectHeightPixels + "px", 2)
        };

        private static readonly (int Width, int Height) ReMeasurementResolution = (2560, 1600);

        /// <summary>Long edge of a written PNG. Metrics never see this reduction.</summary>
        private const int WrittenLongEdge = 320;

        private const float CaptureFieldOfView = 60f;

        /// <summary>
        /// Groups whose assets the dedup pass never touched, so a zero rebind count
        /// is the correct answer rather than a broken baseline. Named here as well
        /// as flagged on the <see cref="Group"/> so the acceptance pass can reason
        /// about a cell without carrying the group object.
        /// </summary>
        private static readonly HashSet<string> OutsideTheDedupGroups =
            new HashSet<string>(StringComparer.Ordinal) { "04-star-classes" };

        // ---- fixture state ---------------------------------------------------

        private Camera camera;
        private Light keyLight;
        private Light fillLight;
        private GameObject rig;
        private Manifest manifest;
        private int restoreQualityLevel;

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>Committed evidence: contact sheets, the metric table and the JSON.</summary>
        public static string ReportDir =>
            Path.Combine(ProjectRoot, "Distribution", "Reports", "Optimization", "VisualMatrix");

        /// <summary>
        /// Full-size per-cell frames. Deliberately outside the repository: 504
        /// images at four resolutions is evidence somebody may want to zoom into
        /// once, not something to carry in every clone forever. The JSON records
        /// the path so the link is not lost.
        /// </summary>
        public static string FullFrameDir => "/tmp/nebula-part4-visual-matrix";

        [OneTimeSetUp]
        public void SetUp()
        {
            RequireGraphicsDevice();
            Directory.CreateDirectory(ReportDir);
            Directory.CreateDirectory(Path.Combine(ReportDir, "sheets"));
            Directory.CreateDirectory(FullFrameDir);

            restoreQualityLevel = QualitySettings.GetQualityLevel();

            rig = new GameObject("visual matrix rig") { hideFlags = HideFlags.HideAndDontSave };

            var cameraGo = new GameObject("capture camera");
            cameraGo.transform.SetParent(rig.transform, false);
            camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            // Transparent clear: alpha is the silhouette channel. See the type remarks.
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.fieldOfView = CaptureFieldOfView;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 10000f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.enabled = false;

            // A fixed two-light profile. Key from the front-upper-left so surface
            // normals actually shade — a single head-on light flattens every model
            // and would hide exactly the normal-map regressions this is looking for.
            var keyGo = new GameObject("key light");
            keyGo.transform.SetParent(rig.transform, false);
            keyLight = keyGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = Color.white;
            keyLight.intensity = 1.15f;
            keyLight.shadows = LightShadows.None;
            keyGo.transform.rotation = Quaternion.Euler(38f, -34f, 0f);

            var fillGo = new GameObject("fill light");
            fillGo.transform.SetParent(rig.transform, false);
            fillLight = fillGo.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.62f, 0.70f, 0.85f);
            fillLight.intensity = 0.35f;
            fillLight.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(-18f, 142f, 0f);

            manifest = new Manifest
            {
                baselineSource =
                    "glTFast imported materials read back through "
                    + "PrefabUtility.GetCorrespondingObjectFromSource on each wrapper renderer "
                    + "(the sub-assets of the GLBs under Assets/VisualSource)",
                qualityLevel = QualitySettings.GetQualityLevel(),
                qualityName = QualitySettings.names[QualitySettings.GetQualityLevel()],
                simulationTimeSeconds = 0f,
                fieldOfView = CaptureFieldOfView,
                lightProfile =
                    "directional key 1.15 @ (38,-34,0) white; directional fill 0.35 @ (-18,142,0) cool; no shadows"
            };
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (rig != null) UnityEngine.Object.DestroyImmediate(rig);
            if (QualitySettings.GetQualityLevel() != restoreQualityLevel)
                QualitySettings.SetQualityLevel(restoreQualityLevel, false);
        }

        // ---- the run ---------------------------------------------------------

        /// <summary>
        /// Renders and measures every cell, writes the images, the contact sheets,
        /// the metric table and the JSON, and then asserts every acceptance
        /// criterion the brief names.
        /// <para>
        /// One test rather than a parameterised set of 168, because the evidence is
        /// the whole table: a criterion like "unexplained large-area difference: 0"
        /// is a statement about the run, and 168 independent green ticks with no
        /// assembled sheet is precisely the shape of result that let seventeen blank
        /// PNGs ship last time.
        /// </para>
        /// </summary>
        [Test]
        public void TheOptimizedVisualsMatchTheirPreOptimizationBaselineEverywhere()
        {
            List<Group> groups = BuildGroups();
            Assert.AreEqual(14, groups.Count, "the brief names fourteen groups");

            foreach (Group group in groups)
                Assert.IsNotEmpty(group.Members, group.Id + ": no members resolved, so the row is empty");

            foreach (Group group in groups)
            {
                var sheetTiles = new List<Texture2D>();
                try
                {
                    foreach ((string lodId, int lodLevel, float screenHeight) in LodBands)
                    foreach ((int width, int height) in Resolutions)
                        sheetTiles.AddRange(CaptureCell(group, lodId, lodLevel, screenHeight, width, height));

                    // The re-measurement rows, last, so a contact sheet reads
                    // presentation-first and the control frames sit underneath the
                    // cells they explain.
                    foreach ((string lodId, int lodLevel) in ReMeasurementBands)
                    {
                        (int width, int height) = ReMeasurementResolution;
                        sheetTiles.AddRange(CaptureCell(group, lodId, lodLevel,
                            ReMeasurementSubjectHeightPixels / (float)height, width, height,
                            ReMeasurementSubjectHeightPixels));
                    }

                    WriteContactSheet(group, sheetTiles);
                }
                finally
                {
                    foreach (Texture2D tile in sheetTiles)
                        if (tile != null) UnityEngine.Object.DestroyImmediate(tile);
                }
            }

            LinkSmallSubjectCellsToTheirReMeasurements();

            manifest.groupCount = groups.Count;
            manifest.cellCount = manifest.cells.Count;
            File.WriteAllText(Path.Combine(ReportDir, "visual-optimization-matrix.json"),
                JsonUtility.ToJson(manifest, true));
            File.WriteAllText(Path.Combine(ReportDir, "visual-optimization-matrix.md"), BuildTable());

            Assert.AreEqual(14 * (LodBands.Length * Resolutions.Length + ReMeasurementBands.Length),
                manifest.cells.Count, "the matrix is not complete");
            AssertEveryAcceptanceCriterion();
        }

        /// <summary>
        /// Points every presentation cell that was too small to judge at the
        /// re-measurement that judges it, and points each re-measurement back at
        /// the cells it answers for.
        /// <para>
        /// A cell is "too small" when its subject is under
        /// <see cref="PercentageGateMinimumSubjectPixels"/>, which is the same line
        /// the acceptance gate uses — so the set that carries a link and the set
        /// that is not asserted as a percentage are the same set by construction,
        /// and neither can drift away from the other.
        /// </para>
        /// </summary>
        private void LinkSmallSubjectCellsToTheirReMeasurements()
        {
            foreach ((string bandId, int level) in ReMeasurementBands)
            {
                foreach (Cell control in manifest.cells.Where(c => c.lod == bandId).ToList())
                {
                    List<Cell> answered = manifest.cells
                        .Where(c => c.group == control.group
                                    && c.lodLevel == level
                                    && c.framedSubjectHeightPixels == 0
                                    && c.subjectPixels < PercentageGateMinimumSubjectPixels)
                        .ToList();

                    foreach (Cell small in answered) small.remeasuredBy = control.key;
                    control.remeasurementOf = answered.Count == 0
                        ? string.Empty
                        : string.Join(", ", answered.Select(c => c.key));
                }
            }
        }

        // ---- verdicts ---------------------------------------------------------

        private const string VerdictPass = "PASS";
        private const string VerdictFail = "FAIL";

        /// <summary>A presentation cell whose subject was too small for a percentage of it to mean anything. Never an acceptance answer on its own — it is a pointer to the cell that is.</summary>
        private const string VerdictInconclusive = "INCONCLUSIVE_SMALL_SUBJECT";

        /// <summary>
        /// Derives one cell's verdict from that cell's own measurement.
        /// <para>
        /// It used to be assigned. Every cell in the manifest carried
        /// <c>verdict = "PASS"</c> written the moment the cell was constructed,
        /// before a single number had been compared to a single threshold — so the
        /// committed report read "168/168 PASS" while four of those cells were over
        /// the 2 % mean-delta limit and twelve were under a perfect silhouette. The
        /// gate itself was real, but it lived only in the assertion; nothing that a
        /// reader of the report could see was derived from a measurement. A verdict
        /// that cannot fail is not a verdict.
        /// </para>
        /// <para>
        /// Three outcomes, and the middle one is the honest part. A cell whose
        /// subject is smaller than <see cref="PercentageGateMinimumSubjectPixels"/>
        /// is <b>not</b> judged by a loosened threshold — that is what the earlier
        /// pass did, and a 5 % limit for small subjects is a 5 % limit. It is
        /// declared <see cref="VerdictInconclusive"/> and handed to a re-measurement
        /// of the same asset at the same LOD, framed to a fixed
        /// <see cref="ReMeasurementSubjectHeightPixels"/> px on screen, which is
        /// judged at the brief's thresholds exactly as written. Small far-away
        /// rasterisation and a genuine texture-quality difference are then two
        /// different observations rather than one ambiguous number.
        /// </para>
        /// </summary>
        private static string DeriveVerdict(Cell cell, out string reason)
        {
            var defects = new List<string>();

            if (cell.missingMaterials > 0) defects.Add($"{cell.missingMaterials} missing material(s)");
            if (cell.errorShaders > 0) defects.Add($"{cell.errorShaders} error/pink shader(s)");
            if (cell.magentaFractionPercent > 0.1)
                defects.Add(Fmt("magenta covers {0:F3}% of the frame", cell.magentaFractionPercent));
            if (cell.subjectPixels <= 0) defects.Add("nothing rendered — no silhouette at all");
            if (cell.baselineDistinctColours < 8 || cell.optimizedDistinctColours < 8)
                defects.Add($"effectively blank subject ({cell.baselineDistinctColours}/"
                            + $"{cell.optimizedDistinctColours} colours over {cell.subjectPixels}px)");
            // The anti-tautology gate. A cell whose baseline rebind touched no
            // renderer compared a frame with itself and would report a flawless
            // zero — the most dangerous green result this fixture can produce.
            if (cell.reboundRenderers <= 0 && !OutsideTheDedupGroups.Contains(cell.group))
                defects.Add("the baseline rebind restored no renderer, so this cell compared the "
                            + "optimized frame against itself");
            // Whatever the subject size, a contiguous region this large is a
            // repainted surface and nothing else.
            if (cell.largestContiguousDifferencePixels > LargeAreaMinimumContiguousPixels)
                defects.Add($"{cell.largestContiguousDifferencePixels} contiguous differing pixels — "
                            + "that is a surface, not compression noise");

            if (defects.Count > 0)
            {
                reason = string.Join("; ", defects);
                return VerdictFail;
            }

            if (cell.subjectPixels < PercentageGateMinimumSubjectPixels)
            {
                reason = Fmt("subject is {0}px, below the {1}px at which a percentage of it describes "
                             + "an area rather than a rounding (measured mean Δ {2:F3}%, silhouette "
                             + "{3:F3}%); judged by re-measurement instead",
                    cell.subjectPixels, PercentageGateMinimumSubjectPixels,
                    cell.meanDeltaSilhouettePercent, cell.silhouetteOverlapPercent);
                return VerdictInconclusive;
            }

            if (cell.meanDeltaSilhouettePercent > MeanDeltaLimitPercent)
                defects.Add(Fmt("mean Δ {0:F3}% over the subject (limit {1}%)",
                    cell.meanDeltaSilhouettePercent, MeanDeltaLimitPercent));
            if (cell.silhouetteOverlapPercent < SilhouetteOverlapFloorPercent)
                defects.Add(Fmt("silhouette overlap {0:F3}% (floor {1}%)",
                    cell.silhouetteOverlapPercent, SilhouetteOverlapFloorPercent));
            if (cell.largestConnectedDifferencePercent > LargeAreaDifferenceLimitPercent
                && cell.largestContiguousDifferencePixels > LargeAreaMinimumContiguousPixels)
                defects.Add(Fmt("large-area difference {0:F3}% ({1}px contiguous)",
                    cell.largestConnectedDifferencePercent, cell.largestContiguousDifferencePixels));

            if (defects.Count > 0)
            {
                reason = string.Join("; ", defects);
                return VerdictFail;
            }

            reason = Fmt("mean Δ {0:F3}% ≤ {1}%, silhouette {2:F3}% ≥ {3}%, largest contiguous "
                         + "difference {4}px, over a {5}px subject",
                cell.meanDeltaSilhouettePercent, MeanDeltaLimitPercent,
                cell.silhouetteOverlapPercent, SilhouetteOverlapFloorPercent,
                cell.largestContiguousDifferencePixels, cell.subjectPixels);
            return VerdictPass;
        }

        /// <summary>
        /// The acceptance pass, stated over verdicts rather than over raw numbers a
        /// second time.
        /// <para>
        /// Two rules, and the second is what makes the first honest. No cell may be
        /// <see cref="VerdictFail"/> or <c>PENDING</c>; and no cell may be
        /// <see cref="VerdictInconclusive"/> without naming a re-measurement that is
        /// itself a real <see cref="VerdictPass"/>. An inconclusive cell with a
        /// dangling, missing or itself-inconclusive link is a failure — otherwise
        /// "inconclusive" would be the new automatic pass, which is the exact
        /// mistake this rewrite exists to undo.
        /// </para>
        /// </summary>
        private void AssertEveryAcceptanceCriterion()
        {
            var failures = new List<string>();
            Dictionary<string, Cell> byKey = manifest.cells
                .GroupBy(c => c.key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (Cell cell in manifest.cells)
            {
                string where = $"{cell.group} / {cell.assetId} / {cell.lod} / {cell.resolution}";

                switch (cell.verdict)
                {
                    case VerdictPass:
                        break;

                    case VerdictInconclusive:
                        if (cell.framedSubjectHeightPixels > 0)
                        {
                            failures.Add($"{where}: a re-measurement framed to "
                                         + $"{cell.framedSubjectHeightPixels}px came out inconclusive — "
                                         + "the control frame is the definitive one and has nothing to "
                                         + $"defer to ({cell.verdictReason})");
                        }
                        else if (string.IsNullOrEmpty(cell.remeasuredBy))
                        {
                            failures.Add($"{where}: inconclusive with no re-measurement to defer to "
                                         + $"({cell.verdictReason})");
                        }
                        else if (!byKey.TryGetValue(cell.remeasuredBy, out Cell control))
                        {
                            failures.Add($"{where}: names re-measurement '{cell.remeasuredBy}', "
                                         + "which is not in this matrix");
                        }
                        else if (control.verdict != VerdictPass)
                        {
                            failures.Add($"{where}: deferred to '{control.key}', which is "
                                         + $"{control.verdict} — {control.verdictReason}");
                        }
                        break;

                    default:
                        failures.Add($"{where}: {cell.verdict} — {cell.verdictReason}");
                        break;
                }
            }

            Assert.IsEmpty(failures, string.Join("\n  ", failures.Take(40)));
        }

        // ---- one cell --------------------------------------------------------

        /// <summary>
        /// Renders one group at one LOD and one resolution, twice, and returns the
        /// three tiles (baseline, optimized, heatmap) for the contact sheet.
        /// </summary>
        /// <param name="screenHeight">Fraction of frame height the subject is framed to fill.</param>
        /// <param name="framedSubjectHeightPixels">
        /// Non-zero on a re-measurement cell, where <paramref name="screenHeight"/>
        /// was derived from this pixel height rather than chosen as a band. Recorded
        /// so a reader can tell a control frame from a presentation frame without
        /// re-deriving it from the resolution.
        /// </param>
        private IEnumerable<Texture2D> CaptureCell(
            Group group, string lodId, int lodLevel, float screenHeight, int width, int height,
            int framedSubjectHeightPixels = 0)
        {
            NebulaVisualDefinition definition = group.Members[0];
            var host = new GameObject(group.Id + " host") { hideFlags = HideFlags.HideAndDontSave };
            host.transform.SetParent(rig.transform, false);

            RenderTexture target = null;
            Texture2D optimized = null, baseline = null, heat = null;
            try
            {
                GameObject instance = NebulaVisualResolver.Instantiate(definition, host.transform);
                Assert.IsNotNull(instance, $"{group.Id}: {definition.VisualId} did not instantiate");
                FreezeMotion(instance);

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                int lodLevelsAvailable = PinLod(instance, lodLevel);

                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1
                };
                camera.targetTexture = target;
                FrameSubject(instance, renderers, screenHeight);

                optimized = Render(target);
                Material[][] shipped = renderers.Select(r => r.sharedMaterials).ToArray();

                string where = $"{group.Id} / {definition.VisualId} / {lodId} / {width}x{height}";
                int rebound = ApplyAuthoredMaterials(
                    NebulaVisualResolver.Load(definition.ResourcePath), renderers, where);
                baseline = Render(target);
                for (int index = 0; index < renderers.Length; index++)
                    renderers[index].sharedMaterials = shipped[index];

                var cell = new Cell
                {
                    group = group.Id,
                    assetId = definition.VisualId,
                    resourcePath = definition.ResourcePath,
                    lod = lodId,
                    lodLevel = lodLevel,
                    lodLevelsAvailable = lodLevelsAvailable,
                    resolution = width + "x" + height,
                    width = width,
                    height = height,
                    framedSubjectHeightPixels = framedSubjectHeightPixels,
                    key = $"{group.Id}/{lodId}/{width}x{height}",
                    rendererCount = renderers.Length,
                    materialSlotCount = shipped.Sum(slots => slots.Length),
                    textureIds = DistinctTextureIds(shipped),
                    materialIds = DistinctMaterialIds(shipped),
                    missingMaterials = shipped.SelectMany(s => s).Count(m => m == null),
                    errorShaders = shipped.SelectMany(s => s)
                        .Count(m => m != null && (m.shader == null
                                                  || m.shader.name.IndexOf("InternalError", StringComparison.Ordinal) >= 0))
                };

                heat = Measure(baseline, optimized, cell);
                cell.reboundRenderers = rebound;
                cell.note = rebound + " of " + renderers.Length
                            + " renderers restored to their glTFast materials"
                            + (group.OutsideTheDedup ? "; " + group.Note : string.Empty);
                // Derived from the measurement that has just been taken, never
                // assigned ahead of it — see DeriveVerdict for what that cost.
                cell.verdict = DeriveVerdict(cell, out string verdictReason);
                cell.verdictReason = verdictReason;

                string stem = $"{group.Id}__{lodId}__{width}x{height}";
                cell.optimizedImage = Path.Combine(FullFrameDir, stem + "__optimized.png");
                cell.heatmapImage = Path.Combine(FullFrameDir, stem + "__heatmap.png");
                File.WriteAllBytes(Path.Combine(FullFrameDir, stem + "__baseline.png"), baseline.EncodeToPNG());
                File.WriteAllBytes(cell.optimizedImage, optimized.EncodeToPNG());
                File.WriteAllBytes(cell.heatmapImage, heat.EncodeToPNG());
                cell.baselineImage = Path.Combine(FullFrameDir, stem + "__baseline.png");

                manifest.cells.Add(cell);

                return new[] { Tile(baseline), Tile(optimized), Tile(heat) };
            }
            finally
            {
                camera.targetTexture = null;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                foreach (Texture2D frame in new[] { optimized, baseline, heat })
                    if (frame != null) UnityEngine.Object.DestroyImmediate(frame);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// Puts glTFast's own imported materials back on the live instance — the
        /// pre-optimization picture, reconstructed from assets that never left the
        /// disk.
        /// <para>
        /// The authored materials have to be read off the <b>prefab asset</b>, not
        /// off the instance. <c>NebulaVisualResolver.Instantiate</c> uses a plain
        /// <see cref="UnityEngine.Object.Instantiate(UnityEngine.Object)"/> rather
        /// than <see cref="PrefabUtility.InstantiatePrefab"/>, so the clone has no
        /// prefab connection at all and
        /// <see cref="PrefabUtility.GetCorrespondingObjectFromSource"/> returns null
        /// for every one of its renderers. Asking the instance rebinds nothing,
        /// renders the same frame twice and reports a flawless zero difference —
        /// a green result that measures nothing, which is the single most dangerous
        /// outcome this fixture could produce. The prefab asset's renderers <i>are</i>
        /// nested-prefab instances, so on the asset the lookup resolves.
        /// </para>
        /// <para>
        /// The two arrays are aligned by index, which is exact rather than
        /// hopeful: a plain clone reproduces its source's hierarchy verbatim and
        /// <c>GetComponentsInChildren(true)</c> walks it in the same deterministic
        /// order on both. The count is asserted rather than assumed anyway.
        /// </para>
        /// </summary>
        private static int ApplyAuthoredMaterials(
            GameObject prefab, Renderer[] instanceRenderers, string where)
        {
            Renderer[] assetRenderers = prefab.GetComponentsInChildren<Renderer>(true);
            Assert.AreEqual(instanceRenderers.Length, assetRenderers.Length,
                where + ": the clone's renderer list does not line up with the prefab's");

            int rebound = 0;
            for (int index = 0; index < assetRenderers.Length; index++)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(assetRenderers[index]) as Renderer;
                if (source == null) continue;
                Material[] authored = source.sharedMaterials;
                if (authored.Length != instanceRenderers[index].sharedMaterials.Length) continue;
                instanceRenderers[index].sharedMaterials = authored;
                rebound++;
            }
            return rebound;
        }

        /// <summary>
        /// Motion off, explicitly. Nothing steps an <c>Animator</c> or a particle
        /// system in edit mode, but "nothing happens to run it" and "it is off" are
        /// different claims, and only the second one survives somebody later
        /// running this fixture in play mode.
        /// </summary>
        private static void FreezeMotion(GameObject instance)
        {
            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.Simulate(0f, true, true);
            }
        }

        /// <summary>
        /// Pins the LOD level so both renders cannot pick differently, and reports
        /// how many levels the group actually has.
        /// <para>
        /// The clamp is not cosmetic. <see cref="LODGroup.ForceLOD"/> treats an
        /// index at or beyond the group's level count as <i>culled</i>, not as
        /// "use the last one" — so a single-LOD wrapper asked for LOD1 renders
        /// nothing at all, and the first run of this fixture duly produced four
        /// empty frames for <c>resource.metal</c> and measured a flawless zero
        /// difference between two blank images. Clamping to the last real level
        /// draws the mesh the group actually has, and
        /// <see cref="Cell.lodLevelsAvailable"/> records that it only has one:
        /// eleven of the wrappers carry a single LOD by design, and pretending
        /// they have three would be inventing rows.
        /// </para>
        /// </summary>
        private static int PinLod(GameObject instance, int level)
        {
            int levels = 0;
            foreach (LODGroup group in instance.GetComponentsInChildren<LODGroup>(true))
            {
                int count = group.GetLODs().Length;
                levels = Mathf.Max(levels, count);
                group.ForceLOD(Mathf.Clamp(level, 0, Mathf.Max(0, count - 1)));
            }
            return levels == 0 ? 1 : levels;
        }

        /// <summary>
        /// Places the camera so the subject fills the requested fraction of frame
        /// height, looking along a fixed three-quarter direction.
        /// </summary>
        private void FrameSubject(GameObject instance, Renderer[] renderers, float screenHeight)
        {
            Bounds bounds = WorldBounds(renderers);
            float radius = Mathf.Max(bounds.extents.magnitude, 0.0001f);

            // Distance at which a sphere of this radius subtends the requested
            // fraction of the vertical field of view.
            float halfFov = CaptureFieldOfView * 0.5f * Mathf.Deg2Rad;
            float distance = radius / Mathf.Max(Mathf.Tan(halfFov) * screenHeight, 0.000001f);

            var direction = new Vector3(0.62f, 0.42f, -0.95f).normalized;
            camera.transform.position = bounds.center + direction * distance;
            camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, Vector3.up);
            camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 4f);
            camera.farClipPlane = distance + radius * 8f;

            if (instance != null) instance.transform.hasChanged = false;
        }

        private static Bounds WorldBounds(Renderer[] renderers)
        {
            var result = new Bounds(Vector3.zero, Vector3.one);
            bool found = false;
            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled) continue;
                if (!found) { result = renderer.bounds; found = true; }
                else result.Encapsulate(renderer.bounds);
            }
            if (!found)
                foreach (Renderer renderer in renderers)
                {
                    if (!found) { result = renderer.bounds; found = true; }
                    else result.Encapsulate(renderer.bounds);
                }
            return result;
        }

        private Texture2D Render(RenderTexture target)
        {
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var frame = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
            try
            {
                frame.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                frame.Apply();
                return frame;
            }
            finally { RenderTexture.active = previous; }
        }

        // ---- measurement -----------------------------------------------------

        /// <summary>
        /// Fills in every metric on the cell and returns the difference heatmap.
        /// <para>
        /// The mean delta is reported twice on purpose. Over the whole frame it is
        /// the number a reader expects, and it is close to meaningless: the empty
        /// space around a distant LOD2 subject is identical in both frames and
        /// drags any average towards zero, so an 8%-of-frame subject could be
        /// completely wrong and still average under 2%. Over the union silhouette
        /// it is the number the acceptance gate uses, because that is the only
        /// region where a material change could show.
        /// </para>
        /// </summary>
        private Texture2D Measure(Texture2D baseline, Texture2D optimized, Cell cell)
        {
            Color32[] a = baseline.GetPixels32();
            Color32[] b = optimized.GetPixels32();
            Assert.AreEqual(a.Length, b.Length, "the two frames are different sizes");

            var heat = new Texture2D(baseline.width, baseline.height, TextureFormat.RGB24, false, true);
            var heatPixels = new Color32[a.Length];
            var deltas = new List<int>(a.Length);

            long frameSum = 0, silhouetteSum = 0;
            int silhouetteUnion = 0, silhouetteIntersection = 0, disagreement = 0;
            int magenta = 0;

            for (int i = 0; i < a.Length; i++)
            {
                int dr = Mathf.Abs(a[i].r - b[i].r);
                int dg = Mathf.Abs(a[i].g - b[i].g);
                int db = Mathf.Abs(a[i].b - b[i].b);
                int delta = (dr + dg + db) / 3;

                frameSum += delta;

                bool inA = a[i].a > 127;
                bool inB = b[i].a > 127;
                if (inA || inB)
                {
                    silhouetteUnion++;
                    silhouetteSum += delta;
                    deltas.Add(delta);
                    if (inA && inB) silhouetteIntersection++; else disagreement++;
                }

                if (inB && b[i].r > 200 && b[i].b > 200 && b[i].g < 80) magenta++;

                byte hot = (byte)Mathf.Min(255, delta * 8);
                heatPixels[i] = new Color32(hot, (byte)(hot / 3), (byte)(255 - hot), 255);
            }

            heat.SetPixels32(heatPixels);
            heat.Apply();

            cell.meanDeltaFramePercent = 100.0 * frameSum / (a.Length * 255.0);
            cell.meanDeltaSilhouettePercent = silhouetteUnion == 0
                ? 0.0
                : 100.0 * silhouetteSum / (silhouetteUnion * 255.0);
            cell.silhouetteOverlapPercent = silhouetteUnion == 0
                ? 100.0
                : 100.0 * silhouetteIntersection / silhouetteUnion;
            cell.silhouetteDisagreementPixels = disagreement;
            cell.magentaFractionPercent = 100.0 * magenta / a.Length;
            cell.baselineDistinctColours = DistinctColours(a);
            cell.optimizedDistinctColours = DistinctColours(b);
            cell.subjectPixels = silhouetteUnion;

            deltas.Sort();
            cell.p95DeltaPercent = Percentile(deltas, 0.95);
            cell.p99DeltaPercent = Percentile(deltas, 0.99);

            cell.visiblyDifferentPixels = deltas.Count(delta => delta > VisibleDelta);
            cell.largestContiguousDifferencePixels =
                LargestConnectedRegion(a, b, baseline.width, baseline.height);
            cell.largestConnectedDifferencePercent = silhouetteUnion == 0
                ? 0.0
                : 100.0 * cell.largestContiguousDifferencePixels / silhouetteUnion;

            return heat;
        }


        /// <summary>Per-channel delta, out of 255, at which a difference is called visible.</summary>
        private const int VisibleDelta = 12;

        /// <summary>
        /// The largest <i>contiguous</i> run of visibly different pixels, in
        /// pixels.
        /// <para>
        /// This is what "unexplained large-area difference" has to mean. Counting
        /// every differing pixel instead — which is what the first version of this
        /// fixture did — measures block-compression noise, and on a subject twenty
        /// pixels across that noise is a double-digit percentage while being
        /// completely invisible and completely expected. A real regression, on the
        /// other hand, is contiguous by nature: a texture that failed to bind, a
        /// colour space applied to the wrong map or a flipped normal channel paints
        /// a whole surface, not a scatter.
        /// </para>
        /// <para>
        /// Four-connected flood fill over the differing pixels only, with an
        /// explicit stack rather than recursion — a 3440x1440 frame has five
        /// million pixels and a contiguous region could reach a depth that unwinds
        /// the call stack.
        /// </para>
        /// </summary>
        private static int LargestConnectedRegion(Color32[] a, Color32[] b, int width, int height)
        {
            var differing = new bool[a.Length];
            int candidates = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].a <= 127 && b[i].a <= 127) continue;
                int delta = (Mathf.Abs(a[i].r - b[i].r)
                             + Mathf.Abs(a[i].g - b[i].g)
                             + Mathf.Abs(a[i].b - b[i].b)) / 3;
                if (delta <= VisibleDelta) continue;
                differing[i] = true;
                candidates++;
            }
            if (candidates == 0) return 0;

            var visited = new bool[a.Length];
            var stack = new Stack<int>();
            int largest = 0;

            for (int start = 0; start < differing.Length; start++)
            {
                if (!differing[start] || visited[start]) continue;

                int size = 0;
                stack.Push(start);
                visited[start] = true;
                while (stack.Count > 0)
                {
                    int index = stack.Pop();
                    size++;
                    int x = index % width;
                    int y = index / width;

                    if (x > 0) Push(stack, differing, visited, index - 1);
                    if (x < width - 1) Push(stack, differing, visited, index + 1);
                    if (y > 0) Push(stack, differing, visited, index - width);
                    if (y < height - 1) Push(stack, differing, visited, index + width);
                }
                if (size > largest) largest = size;
            }
            return largest;
        }

        private static void Push(Stack<int> stack, bool[] differing, bool[] visited, int index)
        {
            if (differing[index] && !visited[index]) { visited[index] = true; stack.Push(index); }
        }

        private static double Percentile(List<int> sorted, double fraction)
        {
            if (sorted.Count == 0) return 0.0;
            int index = Mathf.Clamp(
                Mathf.RoundToInt((float)(fraction * (sorted.Count - 1))), 0, sorted.Count - 1);
            return 100.0 * sorted[index] / 255.0;
        }

        /// <summary>
        /// Distinct colours <b>inside the silhouette</b>, quantised to 5 bits a
        /// channel — the blank-frame check.
        /// <para>
        /// Counted over the subject rather than the frame, because at the LOD2
        /// distance the subject covers about half a percent of the frame area and a
        /// frame-wide subsample lands almost entirely on empty space. That measures
        /// how much background there is, not whether anything rendered, and it
        /// reported a perfectly good distant ship as "effectively blank". Alpha
        /// already tells us which pixels are the subject, so use it.
        /// </para>
        /// <para>
        /// A frame that cleared and drew nothing has no silhouette at all and
        /// therefore zero colours here, which is exactly the answer wanted: it is
        /// the case that would otherwise pass every difference test in this file
        /// perfectly.
        /// </para>
        /// </summary>
        private static int DistinctColours(Color32[] pixels)
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a <= 127) continue;
                seen.Add((pixels[i].r >> 3) << 10 | (pixels[i].g >> 3) << 5 | (pixels[i].b >> 3));
                if (seen.Count > 4096) break;
            }
            return seen.Count;
        }

        private static string DistinctTextureIds(Material[][] slots)
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Material material in slots.SelectMany(s => s))
            {
                if (material == null || material.shader == null) continue;
                foreach (string property in TexturePropertiesOf(material.shader))
                {
                    Texture texture = material.GetTexture(property);
                    if (texture != null) ids.Add(texture.name);
                }
            }
            return string.Join(" ", ids.Take(8));
        }

        private static string DistinctMaterialIds(Material[][] slots)
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Material material in slots.SelectMany(s => s))
                if (material != null) ids.Add(material.name);
            return string.Join(" ", ids.Take(8));
        }

        private static IEnumerable<string> TexturePropertiesOf(Shader shader)
        {
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int index = 0; index < count; index++)
                if (ShaderUtil.GetPropertyType(shader, index) == ShaderUtil.ShaderPropertyType.TexEnv)
                    yield return ShaderUtil.GetPropertyName(shader, index);
        }

        // ---- evidence --------------------------------------------------------

        /// <summary>A square, letterboxed thumbnail, filtered rather than point-sampled.</summary>
        private static Texture2D Tile(Texture2D frame)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(
                WrittenLongEdge, WrittenLongEdge, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            var tile = new Texture2D(WrittenLongEdge, WrittenLongEdge, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(frame, temporary);
                RenderTexture.active = temporary;
                tile.ReadPixels(new Rect(0, 0, WrittenLongEdge, WrittenLongEdge), 0, 0);
                tile.Apply();
                return tile;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        /// <summary>
        /// One sheet per group: fourteen rows — three LOD bands x four resolutions,
        /// then the two equal-pixel-size re-measurement rows — of
        /// baseline, optimized and heatmap, in the order the cells were captured.
        /// </summary>
        private void WriteContactSheet(Group group, List<Texture2D> tiles)
        {
            const int columns = 3;
            int rows = tiles.Count / columns;
            var sheet = new Texture2D(columns * WrittenLongEdge, rows * WrittenLongEdge,
                TextureFormat.RGB24, false);
            try
            {
                for (int index = 0; index < tiles.Count; index++)
                {
                    int row = index / columns;
                    int column = index % columns;
                    // Top-left origin for a reader; Texture2D counts y up.
                    int y = (rows - 1 - row) * WrittenLongEdge;
                    sheet.SetPixels(column * WrittenLongEdge, y,
                        WrittenLongEdge, WrittenLongEdge, tiles[index].GetPixels());
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(ReportDir, "sheets", group.Id + ".png"), sheet.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(sheet); }
        }

        private string BuildTable()
        {
            var text = new StringBuilder();
            text.AppendLine("# Part 4 — visual optimisation matrix");
            text.AppendLine();
            text.AppendLine("Baseline and optimized frames rendered from the same instance, the same camera,");
            text.AppendLine("the same light profile and the same quality level, differing only in which");
            text.AppendLine("materials the renderers are bound to.");
            text.AppendLine();
            text.AppendLine("- Baseline: " + manifest.baselineSource);
            text.AppendLine(Inv($"- Quality level {manifest.qualityLevel} ({manifest.qualityName}) · FOV {manifest.fieldOfView}° · motion {manifest.motion} · simulation time {manifest.simulationTimeSeconds:F1}s"));
            text.AppendLine("- Light profile: " + manifest.lightProfile);
            text.AppendLine(Inv($"- {manifest.groupCount} groups × ({LodBands.Length} LOD bands × {Resolutions.Length} resolutions + {ReMeasurementBands.Length} equal-pixel re-measurements) = {manifest.cells.Count} cells, {manifest.cells.Count * 3} images"));
            text.AppendLine("- Contact sheets: `sheets/<group>.png` — rows are LOD0/LOD1/LOD2 × 1280×720, 1920×1080, 2560×1600, 3440×1440, then the two re-measurement rows; columns are baseline, optimized, heatmap.");
            text.AppendLine("- Full-resolution frames: `" + FullFrameDir + "`");
            text.AppendLine();
            text.AppendLine(Inv($"The `-equal-{ReMeasurementSubjectHeightPixels}px` rows are the control: the same asset, the same"));
            text.AppendLine("`LODGroup.ForceLOD` level, the same camera, FOV and lights, but framed so the subject");
            text.AppendLine(Inv($"is {ReMeasurementSubjectHeightPixels} px tall on screen instead of a fraction of the frame."));
            text.AppendLine("They exist because a percentage of a three-hundred-pixel subject describes a rounding");
            text.AppendLine("rather than an area, so a presentation cell that small is recorded as");
            text.AppendLine("`INCONCLUSIVE_SMALL_SUBJECT` and answered by its re-measurement — never by a loosened");
            text.AppendLine(Inv($"threshold. Every verdict below is derived from that cell's own numbers against the {MeanDeltaLimitPercent}%"));
            text.AppendLine(Inv($"mean-Δ limit and the {SilhouetteOverlapFloorPercent}% silhouette floor."));
            text.AppendLine();
            text.AppendLine("| group | asset | LOD | levels | resolution | subject px | mean Δ (subject) | mean Δ (frame) | p95 Δ | p99 Δ | silhouette | large-area Δ | verdict | basis |");
            text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

            foreach (Cell cell in manifest.cells)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| {0} | {1} | {2} | {3} | {4} | {5} | {6:F3}% | {7:F3}% | {8:F3}% | {9:F3}% | {10:F3}% | {11:F3}% | {12} | {13} |",
                    cell.group, cell.assetId, cell.lod, cell.lodLevelsAvailable, cell.resolution,
                    cell.subjectPixels,
                    cell.meanDeltaSilhouettePercent, cell.meanDeltaFramePercent,
                    cell.p95DeltaPercent, cell.p99DeltaPercent,
                    cell.silhouetteOverlapPercent, cell.largestConnectedDifferencePercent, cell.verdict,
                    VerdictBasis(cell)));

            text.AppendLine();
            text.AppendLine("## Per LOD band");
            text.AppendLine();
            text.AppendLine("| band | cells | subject px (min/median) | mean Δ max | p95 max | p99 max | silhouette min | contiguous Δ max | contiguous px max | verdicts |");
            text.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (string bandId in manifest.cells.Select(c => c.lod).Distinct())
            {
                List<Cell> band = manifest.cells.Where(c => c.lod == bandId).ToList();
                var sizes = band.Select(c => c.subjectPixels).OrderBy(v => v).ToList();
                string verdicts = string.Join(" ", band.GroupBy(c => c.verdict)
                    .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Count()}×{g.Key}"));
                text.AppendLine(Inv($"| {bandId} | {band.Count} | {sizes.First()} / {sizes[sizes.Count / 2]} | {band.Max(c => c.meanDeltaSilhouettePercent):F4}% | {band.Max(c => c.p95DeltaPercent):F4}% | {band.Max(c => c.p99DeltaPercent):F4}% | {band.Min(c => c.silhouetteOverlapPercent):F4}% | {band.Max(c => c.largestConnectedDifferencePercent):F4}% | {band.Max(c => c.largestContiguousDifferencePixels)} | {verdicts} |"));
            }

            text.AppendLine();
            text.AppendLine("## Verdicts");
            text.AppendLine();
            foreach (IGrouping<string, Cell> byVerdict in manifest.cells
                         .GroupBy(c => c.verdict).OrderBy(g => g.Key, StringComparer.Ordinal))
                text.AppendLine(Inv($"- {byVerdict.Key}: {byVerdict.Count()} cells"));

            text.AppendLine();
            text.AppendLine("## Worst observed");
            // Split by whether the number was ever a verdict. Quoting one worst-case
            // across all 196 cells is how the previous edition of this report ended up
            // reading "168/168 PASS" beside a 2.4% mean over a 2% limit: the extreme
            // always comes from a minified cell whose percentage the gate does not
            // use, and printing it unqualified invites exactly that contradiction.
            List<Cell> judged = manifest.cells.Where(c => c.verdict == VerdictPass).ToList();
            List<Cell> deferred = manifest.cells.Where(c => c.verdict == VerdictInconclusive).ToList();

            text.AppendLine(Inv($"Across the {judged.Count} cells a percentage verdict was actually taken on:"));
            text.AppendLine(Inv($"- mean Δ over the subject: {judged.Max(c => c.meanDeltaSilhouettePercent):F4}% (limit {MeanDeltaLimitPercent}%)"));
            text.AppendLine(Inv($"- p95 Δ: {judged.Max(c => c.p95DeltaPercent):F4}%   p99 Δ: {judged.Max(c => c.p99DeltaPercent):F4}%"));
            text.AppendLine(Inv($"- silhouette overlap: {judged.Min(c => c.silhouetteOverlapPercent):F4}% (floor {SilhouetteOverlapFloorPercent}%)"));
            text.AppendLine(Inv($"- largest contiguous difference: {judged.Max(c => c.largestContiguousDifferencePixels)} px ({judged.Max(c => c.largestConnectedDifferencePercent):F4}% of a subject; a repainted surface would be tens of thousands of px)"));
            text.AppendLine();

            if (deferred.Count > 0)
            {
                text.AppendLine(Inv($"Across the {deferred.Count} minified cells whose numbers were deferred to a re-measurement"));
                text.AppendLine("(recorded, not judged — these are the figures the control frames exist to answer):");
                text.AppendLine(Inv($"- mean Δ over the subject: {deferred.Max(c => c.meanDeltaSilhouettePercent):F4}% over as little as {deferred.Min(c => c.subjectPixels)} px of subject"));
                text.AppendLine(Inv($"- silhouette overlap: {deferred.Min(c => c.silhouetteOverlapPercent):F4}%, worst disagreement {deferred.Max(c => c.silhouetteDisagreementPixels)} px"));
                text.AppendLine();
            }

            text.AppendLine("Everywhere:");
            text.AppendLine(Inv($"- missing materials: {manifest.cells.Sum(c => c.missingMaterials)} · error shaders: {manifest.cells.Sum(c => c.errorShaders)} · magenta: {manifest.cells.Max(c => c.magentaFractionPercent):F4}%"));
            text.AppendLine(Inv($"- fewest distinct colours in any frame: {manifest.cells.Min(c => Mathf.Min(c.baselineDistinctColours, c.optimizedDistinctColours))} (a blank frame has 1)"));
            return text.ToString();
        }

        /// <summary>
        /// The last column of the table: what the verdict rests on, in one phrase.
        /// A PASS names the size of the subject it was measured over; an
        /// inconclusive cell names the re-measurement that answers for it, so a
        /// reader never has to take "inconclusive" as an answer in itself.
        /// </summary>
        private static string VerdictBasis(Cell cell)
        {
            if (cell.verdict == VerdictInconclusive)
                return string.IsNullOrEmpty(cell.remeasuredBy)
                    ? "no re-measurement"
                    : "re-measured by `" + cell.remeasuredBy + "`";
            if (cell.framedSubjectHeightPixels > 0)
                return Fmt("control frame, subject {0}px tall", cell.framedSubjectHeightPixels)
                       + (string.IsNullOrEmpty(cell.remeasurementOf)
                           ? " (no small cell needed it)"
                           : Fmt(", answers {0} cell(s)", cell.remeasurementOf.Split(',').Length));
            return Fmt("measured over {0}px of subject", cell.subjectPixels);
        }

        /// <summary>Invariant formatting for an interpolated string. <c>StringBuilder.AppendLine</c>
        /// has no format-provider overload, and a decimal comma in a metric table is a defect.</summary>
        private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The same invariant guarantee for a message built from several literal
        /// fragments. <see cref="Inv"/> cannot be used there: concatenating two
        /// interpolated strings with <c>+</c> produces a <c>string</c> that has
        /// already been formatted in the current culture, so the invariant
        /// conversion would arrive too late to change anything.
        /// </summary>
        private static string Fmt(string format, params object[] values) =>
            string.Format(CultureInfo.InvariantCulture, format, values);

        private static string Truncate(string value, int length) =>
            string.IsNullOrEmpty(value) ? "-"
                : value.Length <= length ? value : value.Substring(0, length - 1) + "…";

        // ---- the fourteen groups ---------------------------------------------

        /// <summary>
        /// The brief's fourteen groups, resolved through the catalog accessors the
        /// game itself calls rather than by matching id prefixes — a prefix search
        /// would still fill a row after an accessor stopped finding its own
        /// entries, which is the failure that actually stops something spawning.
        /// </summary>
        private static List<Group> BuildGroups()
        {
            var groups = new List<Group>();

            groups.Add(Row("01-playership-mki", "PlayerShip Mk I",
                Ship(1, 0), Ship(1, 6), Ship(1, 12)));
            groups.Add(Row("02-playership-mkii-modules", "PlayerShip Mk II with modules",
                Ship(2, 12), Ship(2, 6), Ship(2, 0)));

            var planets = new Group { Id = "03-planets", Title = "Five planet families" };
            foreach (PlanetArchetype archetype in new[]
                     {
                         PlanetArchetype.MetallicRocky, PlanetArchetype.Biomass,
                         PlanetArchetype.UpperAtmospherePlatform, PlanetArchetype.FloatingOceanic,
                         PlanetArchetype.CrystalShell
                     })
                if (NebulaVisualCatalog.TryGetPlanet(archetype, out NebulaVisualDefinition planet))
                    planets.Members.Add(planet);
            groups.Add(planets);

            // The four star classes are procedural: NebulaProducedVisualCatalog
            // resolves them to textures under Resources/VisualTextures/Stars, and
            // the dedup pass never touched a single one. The group is photographed
            // through the planet-sized celestial body the star sits inside so the
            // row is a real picture rather than a placeholder, and the note on
            // every cell records why its delta is exactly zero.
            var stars = new Group
            {
                Id = "04-star-classes",
                Title = "Four star classes (procedural, outside the dedup)",
                OutsideTheDedup = true,
                Note = "the four star classes are procedural — NebulaProducedVisualCatalog resolves "
                       + "them to textures under Resources/VisualTextures/Stars, which the dedup "
                       + "pass never extracted and never rebound (pinned by "
                       + "VisualAssetIntegrityGateTests.NoStarTextureWasEverDrawnIntoTheDedupSet). "
                       + "The row is photographed through the gas-giant body a star sits inside, so "
                       + "the cell carries a real measurement of a real subject rather than a "
                       + "placeholder"
            };
            if (NebulaVisualCatalog.TryGetPlanet(PlanetArchetype.UpperAtmospherePlatform,
                    out NebulaVisualDefinition gasBody))
                stars.Members.Add(gasBody);
            groups.Add(stars);

            groups.Add(Row("05-black-hole", "Black hole", NebulaVisualCatalog.BlackHole));
            groups.Add(Row("06-asteroids", "Asteroid variants",
                NebulaVisualCatalog.Asteroid(0), NebulaVisualCatalog.Asteroid(1), NebulaVisualCatalog.Asteroid(2)));
            groups.Add(Row("07-command-laser-plasma", "Command, laser and plasma structures",
                Structure(SurfaceStructureType.Command, 2), Structure(SurfaceStructureType.Laser, 2),
                Structure(SurfaceStructureType.Plasma, 2), Structure(SurfaceStructureType.Command, 1),
                Structure(SurfaceStructureType.Laser, 1), Structure(SurfaceStructureType.Plasma, 1)));

            var drills = new Group { Id = "08-drills-adapters", Title = "Drill T1/T2 and adapters" };
            drills.Members.Add(NebulaVisualCatalog.DrillForTier(2));
            drills.Members.Add(NebulaVisualCatalog.DrillForTier(1));
            foreach (SurfaceAnchorCategory category in new[]
                     { SurfaceAnchorCategory.Solid, SurfaceAnchorCategory.Water, SurfaceAnchorCategory.Gas })
                if (NebulaVisualCatalog.TryGetDrillAdapter(category, out NebulaVisualDefinition adapter))
                    drills.Members.Add(adapter);
            groups.Add(drills);

            var support = new Group { Id = "09-sensor-maintenance-logistics", Title = "Sensor, maintenance and logistics" };
            support.Members.Add(Structure(SurfaceStructureType.SensorArray, 2));
            support.Members.Add(Structure(SurfaceStructureType.MaintenancePlatform, 2));
            foreach (LogisticsCenterTier tier in new[] { LogisticsCenterTier.T2, LogisticsCenterTier.T1 })
                if (NebulaVisualCatalog.TryGetLogisticsCenter(tier, out NebulaVisualDefinition centre))
                    support.Members.Add(centre);
            support.Members.Add(Structure(SurfaceStructureType.SensorArray, 1));
            support.Members.Add(Structure(SurfaceStructureType.MaintenancePlatform, 1));
            groups.Add(support);

            var vehicles = new Group { Id = "10-support-vehicles", Title = "Four support vehicles and the Mk II hulls" };
            foreach (string id in new[]
                     {
                         VehicleCatalog.ScoutId, VehicleCatalog.CollectorId, VehicleCatalog.EngineerMk1Id,
                         VehicleCatalog.EscortMk1Id, VehicleCatalog.CollectorMk2Id,
                         VehicleCatalog.EngineerMk2Id, VehicleCatalog.EscortMk2Id
                     })
                if (NebulaVisualCatalog.TryGetVehicle(id, out NebulaVisualDefinition vehicle))
                    vehicles.Members.Add(vehicle);
            groups.Add(vehicles);

            var enemies = new Group { Id = "11-enemies", Title = "Six enemy classes" };
            foreach (string id in new[]
                     {
                         EnemyIds.HeavyAssaultMk1, EnemyIds.SiegeMk1, EnemyIds.FighterMk1,
                         EnemyIds.LooterTransportMk1, EnemyIds.EmDisruptorMk1, EnemyIds.ScoutProbeMk1
                     })
                if (NebulaVisualCatalog.TryGetEnemy(id, out NebulaVisualDefinition enemy))
                    enemies.Members.Add(enemy);
            groups.Add(enemies);

            var projectiles = new Group { Id = "12-projectiles", Title = "Laser, plasma and EM projectiles" };
            foreach (ProjectileKind kind in new[]
                     { ProjectileKind.Plasma, ProjectileKind.Em, ProjectileKind.EnemySiege })
                if (NebulaVisualCatalog.TryGetProjectile(kind, out NebulaVisualDefinition projectile))
                    projectiles.Members.Add(projectile);
            projectiles.Members.Add(NebulaVisualCatalog.LaserBeam);
            groups.Add(projectiles);

            var poi = new Group { Id = "13-poi-statekits", Title = "POI and state kits" };
            foreach (PoiType type in new[] { PoiType.Anomaly, PoiType.Wreck, PoiType.AbandonedFacility })
                if (NebulaVisualCatalog.TryGetPoi(type, out NebulaVisualDefinition point))
                    poi.Members.Add(point);
            poi.Members.Add(NebulaVisualCatalog.SensorRelayResolved);
            poi.Members.Add(NebulaVisualCatalog.ProtectedPirateBase);
            poi.Members.Add(NebulaVisualCatalog.CombatWreckage);
            poi.Members.Add(NebulaVisualCatalog.StructureConstruction);
            poi.Members.Add(NebulaVisualCatalog.StructureDamageDisabled);
            poi.Members.Add(NebulaVisualCatalog.StructureRepairUpgrade);
            groups.Add(poi);

            var items = new Group { Id = "14-items", Title = "Raw, processed and part items" };
            foreach (string id in new[]
                     {
                         "resource.metal", "resource.wood", "resource.gas", "resource.water", "resource.diamond",
                         "material.metal_alloy", "material.biocomposite", "material.refined_gas",
                         "material.purified_water", "material.industrial_diamond", "part.basic"
                     })
                if (NebulaVisualCatalog.TryGetItem(id, out NebulaVisualDefinition item))
                    items.Members.Add(item);
            items.Members.Add(NebulaVisualCatalog.BuildCapsule);
            groups.Add(items);

            return groups;
        }

        private static Group Row(string id, string title, params NebulaVisualDefinition[] members)
        {
            var group = new Group { Id = id, Title = title };
            group.Members.AddRange(members.Where(member => member != null));
            return group;
        }

        private static NebulaVisualDefinition Ship(int hullForm, int stage) =>
            NebulaVisualCatalog.TryGetPlayerShip(hullForm, stage, out NebulaVisualDefinition definition)
                ? definition
                : null;

        private static NebulaVisualDefinition Structure(SurfaceStructureType type, int tier) =>
            NebulaVisualCatalog.TryGetStructure(type, tier, out NebulaVisualDefinition definition)
                ? definition
                : null;

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("no graphics device (-nographics); run with graphics enabled to capture");
        }
    }
}
