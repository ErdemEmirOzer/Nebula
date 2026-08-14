using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.UI.Brand;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The Reduced Motion sheets, and the claim that makes them work.
    /// <para>
    /// The reduced export encodes 25 real frames and repeats each across three
    /// consecutive cells, so it is still 75 cells at 30fps over 2.5 seconds. That is
    /// what lets playback keep one clock and one index and simply change picture a
    /// third as often — no second timer, no interpolation, no invented frames.
    /// </para>
    /// <para>
    /// If the repeat pattern were wrong, playback would still run and still look
    /// smooth-ish, and nothing else in the suite would notice. This is the test that
    /// reads the actual pixels.
    /// </para>
    /// </summary>
    public class NebulaReducedMotionImportTests
    {
        private const string LoadingRoot = "Assets/Resources/UI/Brand/Loading";
        private const string Reduced256 = LoadingRoot + "/brand.loading.reduced.256.png";
        private const string ReducedJson = LoadingRoot + "/brand.loading.reduced.unity.json";

        [SetUp]
        public void Setup() => NebulaBrandAssetCatalog.Release();

        [TearDown]
        public void Teardown() => NebulaBrandAssetCatalog.Release();

        [Test]
        public void TheReducedMetadataAgreesWithThePlaybackConstants()
        {
            Assert.IsTrue(File.Exists(ReducedJson), "the reduced metadata must ship with its sheet");
            string json = File.ReadAllText(ReducedJson);

            Assert.AreEqual(75, ReadInt(json, "runtimeFrameCount"), "runtime frames");
            Assert.AreEqual(30, ReadInt(json, "fps"), "runtime fps");
            Assert.AreEqual(25, ReadInt(json, "encodedSourceFrameCount"), "real frames");
            Assert.AreEqual(3, ReadInt(json, "sourceFrameRepeat"), "cell repeat");
            Assert.AreEqual(15, ReadInt(json, "columns"), "columns");
            Assert.AreEqual(5, ReadInt(json, "rows"), "rows");

            Assert.AreEqual(75, NebulaBrandAssetCatalog.LoadingFrameCount);
            Assert.AreEqual(30f, NebulaBrandAssetCatalog.LoadingFps);
            Assert.AreEqual(3, NebulaBrandAssetCatalog.ReducedCellRepeat);
            Assert.AreEqual(25, NebulaBrandAssetCatalog.ReducedDistinctFrames);
            Assert.AreEqual(2.5f, NebulaBrandAssetCatalog.LoadingLoopSeconds, 0.0001f);
        }

        /// <summary>
        /// Each real frame occupies exactly three consecutive cells.
        /// <para>
        /// Read from the source PNG into a temporary readable texture: the imported
        /// sheet is deliberately non-readable, and sampling it back through BC7 would
        /// compare the compressor's output rather than the artwork's.
        /// </para>
        /// </summary>
        [Test]
        public void EveryRealFrameOccupiesThreeConsecutiveCells()
        {
            const int tile = 256, columns = 15;
            Texture2D sheet = Decode(Reduced256);
            try
            {
                Color32[] pixels = sheet.GetPixels32();
                var problems = new List<string>();

                for (int group = 0; group < NebulaBrandAssetCatalog.ReducedDistinctFrames; group++)
                {
                    int first = group * NebulaBrandAssetCatalog.ReducedCellRepeat;

                    // The three cells of a group are the same picture.
                    for (int step = 1; step < NebulaBrandAssetCatalog.ReducedCellRepeat; step++)
                    {
                        double delta = TileDistance(pixels, sheet.width, tile, columns, first, first + step);
                        if (delta > 0d)
                            problems.Add($"cells {first} and {first + step} differ ({delta:F0}); "
                                         + "the group should be one repeated frame");
                    }

                    // And the next group is a different picture, so the loop is not
                    // 75 copies of one plate.
                    int next = first + NebulaBrandAssetCatalog.ReducedCellRepeat;
                    if (next >= NebulaBrandAssetCatalog.LoadingFrameCount) continue;
                    double step2 = TileDistance(pixels, sheet.width, tile, columns, first, next);
                    if (step2 <= 0d) problems.Add($"cells {first} and {next} are identical; the loop is not advancing");
                }

                Assert.IsEmpty(problems, string.Join("\n  ", problems));
            }
            finally { Object.DestroyImmediate(sheet); }
        }

        /// <summary>
        /// The reduced loop really is calmer than the normal one.
        /// <para>
        /// Not a tautology from the repeat pattern: it compares the mean step between
        /// consecutive cells across the two sheets. A "reduced" sheet that repeated
        /// cells but moved further each group would pass the pattern test and still
        /// be the wrong asset to hand a player who asked for less motion.
        /// </para>
        /// </summary>
        [Test]
        public void TheReducedLoopMovesLessPerFrameThanTheNormalOne()
        {
            const int tile = 256, columns = 15;
            Texture2D reduced = Decode(Reduced256);
            Texture2D normal = Decode(LoadingRoot + "/brand.loading.sheet.256.png");
            try
            {
                double reducedMean = MeanStep(reduced.GetPixels32(), reduced.width, tile, columns);
                double normalMean = MeanStep(normal.GetPixels32(), normal.width, tile, columns);

                Assert.Greater(normalMean, 0d, "the normal sheet is not animating at all");
                Assert.Less(reducedMean, normalMean,
                    $"reduced moves {reducedMean:F0} per frame against normal's {normalMean:F0}");
            }
            finally
            {
                Object.DestroyImmediate(reduced);
                Object.DestroyImmediate(normal);
            }
        }

        /// <summary>The reduced loop closes as cleanly as the normal one.</summary>
        [Test]
        public void TheReducedLoopHasNoVisibleSeam()
        {
            const int tile = 256, columns = 15;
            Texture2D sheet = Decode(Reduced256);
            try
            {
                Color32[] pixels = sheet.GetPixels32();
                // Measured against the steps that actually move, not against all 74.
                // Two cells in every group of three are repeats and contribute a zero,
                // so a mean over all consecutive pairs is a third of the real step
                // size and would call any honest wrap a seam.
                double total = 0d;
                int moves = 0;
                for (int i = 0; i < 74; i++)
                {
                    double step = TileDistance(pixels, sheet.width, tile, columns, i, i + 1);
                    if (step <= 0d) continue;
                    total += step;
                    moves++;
                }

                Assert.AreEqual(NebulaBrandAssetCatalog.ReducedDistinctFrames - 1, moves,
                    "a 25-frame loop repeated three times over should move exactly 24 times");
                double mean = total / moves;

                double wrap = TileDistance(pixels, sheet.width, tile, columns, 74, 0);
                Assert.Less(wrap, mean * 2d,
                    $"the 74->0 wrap ({wrap:F0}) is far larger than a real frame step ({mean:F0})");
            }
            finally { Object.DestroyImmediate(sheet); }
        }

        // ---- helpers ---------------------------------------------------------

        private static Texture2D Decode(string assetPath)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(assetPath)), assetPath + " could not be decoded");
            return texture;
        }

        private static double MeanStep(Color32[] pixels, int width, int tile, int columns)
        {
            double total = 0d;
            for (int i = 0; i < 74; i++) total += TileDistance(pixels, width, tile, columns, i, i + 1);
            return total / 74d;
        }

        private static double TileDistance(Color32[] pixels, int sheetWidth, int tile, int columns, int a, int b)
        {
            int sheetHeight = pixels.Length / sheetWidth;
            double total = 0d;
            for (int y = 0; y < tile; y += 4)
            {
                for (int x = 0; x < tile; x += 4)
                {
                    Color32 pa = pixels[Sample(sheetWidth, sheetHeight, tile, columns, a, x, y)];
                    Color32 pb = pixels[Sample(sheetWidth, sheetHeight, tile, columns, b, x, y)];
                    total += Mathf.Abs(pa.r - pb.r) + Mathf.Abs(pa.g - pb.g)
                           + Mathf.Abs(pa.b - pb.b) + Mathf.Abs(pa.a - pb.a);
                }
            }
            return total;
        }

        /// <summary>
        /// One pixel of one cell. <c>GetPixels32</c> is bottom-up and the sheet is
        /// authored top-down, so the row is flipped exactly as the importer flips it
        /// when writing the slice rects.
        /// </summary>
        private static int Sample(int sheetWidth, int sheetHeight, int tile, int columns,
            int frame, int x, int y)
        {
            int column = frame % columns;
            int row = frame / columns;
            int originX = column * tile;
            int originY = sheetHeight - (row + 1) * tile;
            return (originY + (tile - 1 - y)) * sheetWidth + originX + x;
        }

        private static int ReadInt(string json, string key)
        {
            int at = json.IndexOf($"\"{key}\"", System.StringComparison.Ordinal);
            Assert.Greater(at, -1, $"the reduced metadata has no \"{key}\"");
            int start = json.IndexOf(':', at) + 1;
            while (start < json.Length && !char.IsDigit(json[start])) start++;
            int end = start;
            while (end < json.Length && char.IsDigit(json[end])) end++;
            Assert.Greater(end, start, $"\"{key}\" is not a number");
            return int.Parse(json.Substring(start, end - start));
        }
    }
}
