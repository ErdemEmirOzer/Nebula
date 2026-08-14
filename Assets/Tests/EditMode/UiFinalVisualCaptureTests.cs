using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The capture set on disk, checked as evidence rather than trusted as
    /// evidence.
    /// <para>
    /// Seventeen of these files once shipped as byte-identical flat grey
    /// rectangles with every test green, because writing a PNG succeeds whether
    /// or not anything was drawn into it. The capture fixture guards the frame it
    /// is about to write; this guards the set that is actually in the repository,
    /// which is the thing anyone reviewing the work will open. It needs no
    /// graphics device — it reads files.
    /// </para>
    /// </summary>
    public class UiFinalVisualCaptureTests
    {
        private static readonly string[] Resolutions =
        {
            "1280x720", "1920x1080", "2560x1440", "3440x1440-ultrawide", "1920x1200-16x10"
        };

        private static readonly string[] Pages =
        {
            "overview", "map", "progression", "resources", "fleet",
            "planets", "combat", "discovery", "settings", "session"
        };

        private static string Dir =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UIDesign", "Runtime");

        private static Texture2D Load(string file)
        {
            string path = Path.Combine(Dir, file);
            Assert.IsTrue(File.Exists(path), file + " is missing from UIDesign/Runtime");
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(path)), file + " is not a readable PNG");
            return texture;
        }

        [Test]
        public void EveryPageWasCapturedAtEveryResolution()
        {
            var missing = new List<string>();
            foreach (string page in Pages)
            foreach (string resolution in Resolutions)
            {
                string file = $"page-{page}-{resolution}.png";
                if (!File.Exists(Path.Combine(Dir, file))) missing.Add(file);
            }
            Assert.IsEmpty(missing,
                "the matrix is ten pages by five resolutions; missing: " + string.Join(", ", missing));
        }

        /// <summary>
        /// A capture is only evidence if something was drawn. Flat is the failure
        /// this whole set exists because of, so it is measured rather than assumed:
        /// a real interface spans a wide range and does not spend nearly all of
        /// itself on one value.
        /// </summary>
        [Test]
        public void NoCaptureIsAFlatRectangle()
        {
            foreach (string page in Pages)
            foreach (string resolution in Resolutions)
            {
                string file = $"page-{page}-{resolution}.png";
                Texture2D texture = Load(file);
                try
                {
                    Color32[] pixels = texture.GetPixels32();
                    var histogram = new int[256];
                    int min = 255, max = 0;
                    foreach (Color32 p in pixels)
                    {
                        int luma = (p.r * 30 + p.g * 59 + p.b * 11) / 100;
                        histogram[luma]++;
                        if (luma < min) min = luma;
                        if (luma > max) max = luma;
                    }

                    Assert.Greater(max - min, 24,
                        $"{file} spans only {max - min} levels of luminance — nothing was drawn into it");

                    int busiest = 0;
                    foreach (int count in histogram) if (count > busiest) busiest = count;
                    Assert.Less(busiest / (float)pixels.Length, 0.995f,
                        $"{file} is {busiest * 100f / pixels.Length:0.0}% a single value");
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }

        [Test]
        public void EveryCaptureHasTheDimensionsItsNameClaims()
        {
            foreach (string page in Pages)
            foreach (string resolution in Resolutions)
            {
                string file = $"page-{page}-{resolution}.png";
                string[] size = resolution.Split('-')[0].Split('x');
                Texture2D texture = Load(file);
                try
                {
                    Assert.AreEqual(int.Parse(size[0]), texture.width, file);
                    Assert.AreEqual(int.Parse(size[1]), texture.height, file);
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }

        /// <summary>
        /// Two pages that render identically mean one of them never switched, which
        /// is exactly what a page-sweep capture loop gets wrong when it forgets to
        /// wait for the switch. Compared within a resolution, where it would show.
        /// </summary>
        [Test]
        public void NoTwoPagesRenderedIdenticallyAtTheSameResolution()
        {
            foreach (string resolution in Resolutions)
            {
                var seen = new Dictionary<string, string>();
                foreach (string page in Pages)
                {
                    string file = $"page-{page}-{resolution}.png";
                    string hash = Fingerprint(Path.Combine(Dir, file));
                    Assert.IsFalse(seen.ContainsKey(hash),
                        $"{file} is byte-identical to {(seen.TryGetValue(hash, out string other) ? other : "another page")} " +
                        "— one of the two never actually switched");
                    seen[hash] = file;
                }
            }
        }

        private static string Fingerprint(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return System.BitConverter.ToString(sha.ComputeHash(stream));
        }
    }
}
