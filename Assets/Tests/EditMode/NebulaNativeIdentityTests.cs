using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.UI.Brand;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The native icon containers, read as containers rather than trusted as files.
    /// <para>
    /// Unity builds its own <c>.ico</c> and <c>.icns</c> from the PNG set bound in
    /// PlayerSettings, so these are not what the player build consumes today — they
    /// are the hand-authored sources a Windows installer or a macOS document type
    /// would need, and the reference the build verification compares against. Their
    /// job is to be correct and reopenable; this checks that they are.
    /// </para>
    /// </summary>
    public class NebulaNativeIdentityTests
    {
        /// <summary>Layer sizes the delivery states the .ico carries.</summary>
        private static readonly int[] ExpectedIcoLayers = { 16, 24, 32, 48, 64, 128, 256 };

        private static readonly string[] ExpectedIconsetFiles =
        {
            "icon_16x16.png", "icon_16x16@2x.png",
            "icon_32x32.png", "icon_32x32@2x.png",
            "icon_128x128.png", "icon_128x128@2x.png",
            "icon_256x256.png", "icon_256x256@2x.png",
            "icon_512x512.png", "icon_512x512@2x.png",
        };

        /// <summary>
        /// Native containers must not sit under Resources.
        /// <para>
        /// Unity cannot import an <c>.ico</c> or an <c>.icns</c> as a texture, so
        /// under Resources they would be dead weight in the player with no way to
        /// load them.
        /// </para>
        /// </summary>
        [Test]
        public void NativeContainersLiveOutsideResources()
        {
            foreach (string path in new[]
                     { NebulaBrandAssetCatalog.WindowsIcoPath, NebulaBrandAssetCatalog.MacIcnsPath })
            {
                Assert.IsTrue(File.Exists(path), path + " is missing");
                StringAssert.DoesNotContain("/Resources/", path, path + " would ship as a loadable resource");
            }
        }

        /// <summary>
        /// The Windows container carries the seven expected layers.
        /// <para>
        /// Parsed from the ICONDIR header rather than taken on faith: an .ico with one
        /// 256px layer opens fine in a viewer and looks like mush in a 16px taskbar,
        /// which is exactly the case a size list is meant to prevent.
        /// </para>
        /// </summary>
        [Test]
        public void TheWindowsIcoCarriesEveryExpectedLayer()
        {
            byte[] data = File.ReadAllBytes(NebulaBrandAssetCatalog.WindowsIcoPath);
            Assert.Greater(data.Length, 22, "the .ico is too small to hold a directory");

            // ICONDIR: reserved(2)=0, type(2)=1 for icon, count(2).
            Assert.AreEqual(0, System.BitConverter.ToUInt16(data, 0), "reserved word");
            Assert.AreEqual(1, System.BitConverter.ToUInt16(data, 2), "resource type must be icon");

            int count = System.BitConverter.ToUInt16(data, 4);
            Assert.AreEqual(ExpectedIcoLayers.Length, count, "layer count");

            var found = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                int entry = 6 + i * 16;
                // Width and height are one byte each; zero means 256.
                int width = data[entry] == 0 ? 256 : data[entry];
                int height = data[entry + 1] == 0 ? 256 : data[entry + 1];
                Assert.AreEqual(width, height, $"layer {i} is not square ({width}x{height})");

                int bytesInRes = (int)System.BitConverter.ToUInt32(data, entry + 8);
                int offset = (int)System.BitConverter.ToUInt32(data, entry + 12);
                Assert.Greater(bytesInRes, 0, $"layer {width} has no data");
                Assert.LessOrEqual(offset + bytesInRes, data.Length,
                    $"layer {width} points past the end of the file");

                found.Add(width);
            }

            found.Sort();
            CollectionAssert.AreEqual(ExpectedIcoLayers, found, "layer sizes");
        }

        /// <summary>
        /// The macOS container reopens: correct magic, and a declared length that
        /// matches the file.
        /// </summary>
        [Test]
        public void TheMacIcnsContainerReopens()
        {
            byte[] data = File.ReadAllBytes(NebulaBrandAssetCatalog.MacIcnsPath);
            Assert.Greater(data.Length, 8, "the .icns is too small to hold a header");

            Assert.AreEqual("icns", System.Text.Encoding.ASCII.GetString(data, 0, 4), "magic");

            // Length is big-endian and covers the whole container, header included.
            int declared = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];
            Assert.AreEqual(data.Length, declared,
                "the declared container length does not match the file; it is truncated or padded");

            // Walk the chunks so a container that is the right length but internally
            // malformed still fails.
            int pos = 8, chunks = 0;
            while (pos + 8 <= data.Length)
            {
                int size = (data[pos + 4] << 24) | (data[pos + 5] << 16) | (data[pos + 6] << 8) | data[pos + 7];
                Assert.GreaterOrEqual(size, 8, $"chunk at {pos} declares a nonsense size");
                Assert.LessOrEqual(pos + size, data.Length, $"chunk at {pos} runs past the end");
                pos += size;
                chunks++;
            }

            Assert.AreEqual(data.Length, pos, "the chunk walk did not land exactly on the end of the file");
            Assert.Greater(chunks, 1, "the container holds no icon variants");
        }

        [Test]
        public void TheIconsetCarriesEverySizeAndScale()
        {
            var missing = new List<string>();
            foreach (string file in ExpectedIconsetFiles)
            {
                string path = $"{NebulaBrandAssetCatalog.MacIconsetFolder}/{file}";
                if (!File.Exists(path)) missing.Add(file);
            }

            Assert.IsEmpty(missing, "iconset entries missing: " + string.Join(", ", missing));
        }

        /// <summary>
        /// Each iconset entry is the pixel size its name claims.
        /// <para>
        /// The <c>@2x</c> convention names the point size, not the pixel size, so
        /// <c>icon_256x256@2x.png</c> must be 512px. Getting that backwards produces
        /// an iconset that builds without complaint and renders soft on a Retina
        /// display.
        /// </para>
        /// </summary>
        [Test]
        public void EveryIconsetEntryIsThePixelSizeItsNameClaims()
        {
            var problems = new List<string>();
            foreach (string file in ExpectedIconsetFiles)
            {
                string path = $"{NebulaBrandAssetCatalog.MacIconsetFolder}/{file}";
                if (!File.Exists(path)) continue;

                string stem = Path.GetFileNameWithoutExtension(file);
                bool retina = stem.EndsWith("@2x");
                string points = stem.Replace("icon_", string.Empty).Replace("@2x", string.Empty);
                int declared = int.Parse(points.Substring(0, points.IndexOf('x')));
                int expected = retina ? declared * 2 : declared;

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!texture.LoadImage(File.ReadAllBytes(path))) { problems.Add(file + ": undecodable"); continue; }
                    if (texture.width != expected || texture.height != expected)
                        problems.Add($"{file}: {texture.width}x{texture.height}, expected {expected}x{expected}");
                }
                finally { Object.DestroyImmediate(texture); }
            }

            Assert.IsEmpty(problems, string.Join("\n  ", problems));
        }
    }
}
