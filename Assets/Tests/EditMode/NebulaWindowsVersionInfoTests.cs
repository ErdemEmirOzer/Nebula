using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Nebula.EditorTools;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Proves the Windows version-resource post-process both works and is incapable of
    /// damaging the executable it edits.
    /// <para>
    /// The subject is a byte-level rewrite inside a PE image, so the only convincing
    /// evidence is byte-level: an icon resource placed <b>before</b> the version blob is
    /// compared before and after, the file length is compared, and the section table is
    /// compared. If the patch ever starts moving data instead of growing into the
    /// section's own padding, the icon comparison is what fails.
    /// </para>
    /// <para>
    /// The fixture is a PE built here rather than a real player checked into the
    /// repository. A committed binary would need to be re-cut whenever Unity changes its
    /// output, and nobody would notice when it stopped resembling one; a fixture the test
    /// constructs states its own assumptions — a single <c>.rsrc</c> section, an icon,
    /// and a version blob at the very end — which are exactly the preconditions the
    /// production code refuses to work without.
    /// </para>
    /// </summary>
    public class NebulaWindowsVersionInfoTests
    {
        private const int IconBytes = 512;

        [Test]
        public void Stamp_WritesTheGameIdentityAndLeavesEveryOtherResourceByteIdentical()
        {
            byte[] original = BuildSamplePe(certificateSize: 0);
            // A unique directory rather than a unique filename, so the file is called
            // exactly what the shipped player is called. OriginalFilename is derived from
            // the path, and a 40-character temp name would silently make the blob far
            // larger than the one the release actually writes.
            string path = Path.Combine(NewTempDirectory(), "NEBULA.exe");
            File.WriteAllBytes(path, original);
            try
            {
                string note = NebulaWindowsVersionInfo.Stamp(path, "Erdem", "NEBULA", "0.1.0.1");
                StringAssert.DoesNotContain("NOT SET", note);

                IReadOnlyDictionary<string, string> read = NebulaWindowsVersionInfo.ReadVersionStrings(path);
                Assert.AreEqual("NEBULA", Value(read, "ProductName"));
                Assert.AreEqual("Erdem", Value(read, "CompanyName"));
                Assert.AreEqual("NEBULA", Value(read, "FileDescription"));
                Assert.AreEqual("0.1.0.1", Value(read, "FileVersion"));
                Assert.AreEqual("0.1.0.1", Value(read, "ProductVersion"));
                Assert.AreEqual("NEBULA.exe", Value(read, "OriginalFilename"));

                byte[] patched = File.ReadAllBytes(path);
                Assert.AreEqual(original.Length, patched.Length,
                    "the rewrite must never change the file's length");

                // The icon sits before the version blob in .rsrc. Any change to it means
                // the patch moved data rather than growing into the section's padding.
                int iconAt = IconOffset(original);
                CollectionAssert.AreEqual(
                    Slice(original, iconAt, IconBytes), Slice(patched, iconAt, IconBytes),
                    "the icon resource must survive the version rewrite byte for byte");

                // The section table may differ in exactly one field: .rsrc's VirtualSize.
                int table = SectionTableOffset(original);
                CollectionAssert.AreEqual(Slice(original, table, 8), Slice(patched, table, 8),
                    "the section name must not change");
                CollectionAssert.AreEqual(Slice(original, table + 12, 28), Slice(patched, table + 12, 28),
                    "section addresses and sizes other than VirtualSize must not change");
            }
            finally
            {
                DeleteTempDirectory(path);
            }
        }

        [Test]
        public void Stamp_RefusesToTouchASignedImage()
        {
            // A signed binary edited in place is silently unsigned afterwards, which is a
            // worse shipping outcome than metadata that reads "Unity". The refusal is the
            // feature; this test is what stops someone "fixing" it later.
            byte[] signed = BuildSamplePe(certificateSize: 4096);
            string path = Path.Combine(NewTempDirectory(), "NEBULA.exe");
            File.WriteAllBytes(path, signed);
            try
            {
                string note = NebulaWindowsVersionInfo.Stamp(path, "Erdem", "NEBULA", "0.1.0.1");
                StringAssert.Contains("NOT SET", note);
                StringAssert.Contains("signed", note);
                CollectionAssert.AreEqual(signed, File.ReadAllBytes(path),
                    "a refused stamp must leave the file exactly as it found it");
            }
            finally
            {
                DeleteTempDirectory(path);
            }
        }

        [Test]
        public void NormalizeVersion_AlwaysProducesAFourPartNumericVersion()
        {
            // Windows compares these as four 16-bit numbers. A channel suffix or a missing
            // part has to degrade to something numeric rather than throw mid-build.
            Assert.AreEqual("0.1.0.1", NebulaWindowsVersionInfo.NormalizeVersion("0.1.0.1"));
            Assert.AreEqual("0.1.0.0", NebulaWindowsVersionInfo.NormalizeVersion("0.1.0"));
            Assert.AreEqual("1.0.0.0", NebulaWindowsVersionInfo.NormalizeVersion("1"));
            Assert.AreEqual("0.1.0.0", NebulaWindowsVersionInfo.NormalizeVersion("0.1.0-private-alpha"));
            Assert.AreEqual("0.0.0.0", NebulaWindowsVersionInfo.NormalizeVersion(""));
        }

        [Test]
        public void BuildVersionInfo_IsDeterministicAndFitsTheUnityPlayersBudget()
        {
            // The real player leaves 632 bytes for this blob (528 used by Unity's own
            // resource plus 104 bytes of .rsrc file-alignment padding). Staying inside it
            // is what lets the patch avoid moving any section at all, so the budget is
            // asserted here rather than discovered during a release build.
            var strings = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("CompanyName", "Erdem"),
                new KeyValuePair<string, string>("FileDescription", "NEBULA"),
                new KeyValuePair<string, string>("FileVersion", "0.1.0.1"),
                new KeyValuePair<string, string>("LegalCopyright", "(c) 2026 Erdem"),
                new KeyValuePair<string, string>("OriginalFilename", "NEBULA.exe"),
                new KeyValuePair<string, string>("ProductName", "NEBULA"),
                new KeyValuePair<string, string>("ProductVersion", "0.1.0.1"),
            };
            byte[] first = NebulaWindowsVersionInfo.BuildVersionInfo(strings, "0.1.0.1");
            byte[] second = NebulaWindowsVersionInfo.BuildVersionInfo(strings, "0.1.0.1");
            CollectionAssert.AreEqual(first, second,
                "two builds of one commit must produce identical version bytes");
            Assert.LessOrEqual(first.Length, 632,
                "the version blob must fit the Unity player's .rsrc budget without moving sections");
            Assert.AreEqual(0, first.Length % 4, "every version-info node is 4-byte aligned");
        }

        private static string Value(IReadOnlyDictionary<string, string> map, string key) =>
            map.TryGetValue(key, out string value) ? value : "<absent>";

        private static string NewTempDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(),
                "nebula-versioninfo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Removes the throwaway directory the fixture executable lived in.
        /// Scoped to the directory this test made, never a path it was handed.</summary>
        private static void DeleteTempDirectory(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) ||
                !Path.GetFileName(dir).StartsWith("nebula-versioninfo-", StringComparison.Ordinal))
                return;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        private static byte[] Slice(byte[] source, int at, int length)
        {
            var slice = new byte[length];
            Array.Copy(source, at, slice, 0, length);
            return slice;
        }

        // -------------------------------------------------------------------
        // A minimal but structurally real PE64 with one .rsrc section holding an
        // RT_ICON and an RT_VERSION, laid out the way a Unity player lays them out:
        // the version blob last, followed by file-alignment padding.
        // -------------------------------------------------------------------

        private const int PeOffset = 0x80;
        private const int FileAlignment = 0x200;
        private const int RsrcRva = 0x1000;
        private const int RsrcRaw = 0x400;
        private const int OptionalHeaderSize = 240;   // PE32+: 112 fixed fields + 16 data directories

        /// <summary>
        /// The resource tree's layout, in bytes from the resource root.
        /// <para>
        /// Every offset is derived from the two structure sizes rather than written out,
        /// because the first version of this fixture hand-computed them and placed five
        /// structures on top of each other — most damagingly the version resource's
        /// language directory, whose entry landed inside the icon's pixel data and was
        /// then overwritten by it. The stamper duly read a garbage offset and threw. A
        /// fixture that is not a faithful PE tests nothing but itself.
        /// </para>
        /// </summary>
        private const int DirHeader = 16;   // IMAGE_RESOURCE_DIRECTORY
        private const int DirEntry = 8;     // IMAGE_RESOURCE_DIRECTORY_ENTRY
        private const int DataEntry = 16;   // IMAGE_RESOURCE_DATA_ENTRY

        private const int TypeDir = 0;                                        // 2 entries: ICON, VERSION
        private const int IconNameDir = TypeDir + DirHeader + 2 * DirEntry;
        private const int VersionNameDir = IconNameDir + DirHeader + DirEntry;
        private const int IconLangDir = VersionNameDir + DirHeader + DirEntry;
        private const int VersionLangDir = IconLangDir + DirHeader + DirEntry;
        private const int IconDataEntry = VersionLangDir + DirHeader + DirEntry;
        private const int VersionDataEntry = IconDataEntry + DataEntry;
        private const int TreeSize = VersionDataEntry + DataEntry;

        /// <summary>Icon pixels start immediately after the tree, and the version blob
        /// immediately after them — so the icon is the byte range directly in front of the
        /// one being rewritten, which is the adjacency the comparison is meant to police.</summary>
        private static int IconOffset(byte[] _) => RsrcRaw + TreeSize;

        private static int VersionOffset(byte[] _) => IconOffset(null) + IconBytes;

        /// <summary>Read from the header rather than assumed, the way the production code
        /// finds it: a fixture that hardcodes the answer cannot catch a header it built wrong.</summary>
        private static int SectionTableOffset(byte[] image)
        {
            int coff = (image[0x3C] | (image[0x3D] << 8) | (image[0x3E] << 16) | (image[0x3F] << 24)) + 4;
            int optionalSize = image[coff + 16] | (image[coff + 17] << 8);
            return coff + 20 + optionalSize;
        }

        private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

        private static byte[] BuildSamplePe(uint certificateSize)
        {
            byte[] version = SeedVersionBlob();
            int iconAt = RsrcRaw + TreeSize;
            int versionAt = iconAt + IconBytes;
            int rsrcUsed = (versionAt + version.Length) - RsrcRaw;
            int rsrcRawSize = Align(rsrcUsed, FileAlignment);

            var image = new byte[RsrcRaw + rsrcRawSize];
            image[0] = (byte)'M';
            image[1] = (byte)'Z';
            Put32(image, 0x3C, PeOffset);
            image[PeOffset] = (byte)'P';
            image[PeOffset + 1] = (byte)'E';

            int coff = PeOffset + 4;
            Put16(image, coff, 0x8664);          // Machine x64
            Put16(image, coff + 2, 1);           // one section
            Put16(image, coff + 16, OptionalHeaderSize);

            int optional = coff + 20;
            Put16(image, optional, 0x20B);       // PE32+
            int directoriesAt = optional + 112;
            Put32(image, directoriesAt + 2 * 8, RsrcRva);       // Resource directory RVA
            Put32(image, directoriesAt + 2 * 8 + 4, rsrcUsed);  // ...and size
            Put32(image, directoriesAt + 4 * 8 + 4, (int)certificateSize);

            int table = optional + OptionalHeaderSize;
            Encoding.ASCII.GetBytes(".rsrc").CopyTo(image, table);
            Put32(image, table + 8, rsrcUsed);       // VirtualSize: the version blob ends it
            Put32(image, table + 12, RsrcRva);       // VirtualAddress
            Put32(image, table + 16, rsrcRawSize);   // SizeOfRawData, with padding to spare
            Put32(image, table + 20, RsrcRaw);       // PointerToRawData

            // Resource tree: type -> name -> language, for RT_ICON (3) and RT_VERSION (16).
            int typeDir = RsrcRaw;
            Put16(image, typeDir + 14, 2);                                   // two id entries
            Put32(image, typeDir + 16, 3);                                   // RT_ICON
            Put32(image, typeDir + 20, unchecked((int)(0x80000000 | IconNameDir)));
            Put32(image, typeDir + 24, 16);                                  // RT_VERSION
            Put32(image, typeDir + 28, unchecked((int)(0x80000000 | VersionNameDir)));

            BuildNameAndLanguage(image, typeDir, IconNameDir, IconLangDir, IconDataEntry);
            BuildNameAndLanguage(image, typeDir, VersionNameDir, VersionLangDir, VersionDataEntry);

            Put32(image, RsrcRaw + IconDataEntry, RsrcRva + (iconAt - RsrcRaw));
            Put32(image, RsrcRaw + IconDataEntry + 4, IconBytes);
            Put32(image, RsrcRaw + VersionDataEntry, RsrcRva + (versionAt - RsrcRaw));
            Put32(image, RsrcRaw + VersionDataEntry + 4, version.Length);

            for (int i = 0; i < IconBytes; i++) image[iconAt + i] = (byte)(i * 7 + 3);
            version.CopyTo(image, versionAt);
            return image;
        }

        private static void BuildNameAndLanguage(byte[] image, int root, int nameDir,
            int languageDir, int dataEntry)
        {
            Put16(image, root + nameDir + 14, 1);
            Put32(image, root + nameDir + 16, 1);
            Put32(image, root + nameDir + 20, unchecked((int)(0x80000000 | languageDir)));
            Put16(image, root + languageDir + 14, 1);
            Put32(image, root + languageDir + 16, 1033);
            Put32(image, root + languageDir + 20, dataEntry);
        }

        /// <summary>A stand-in for Unity's own resource: the bytes the patch must replace.</summary>
        private static byte[] SeedVersionBlob()
        {
            var seed = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("FileVersion", "2022.3.22.8944612"),
                new KeyValuePair<string, string>("LegalCopyright", "(c) 2005-2024 Unity Technologies."),
                new KeyValuePair<string, string>("ProductVersion", "2022.3.22f1"),
            };
            return NebulaWindowsVersionInfo.BuildVersionInfo(seed, "2022.3.22.0");
        }

        private static void Put16(byte[] b, int at, int value)
        {
            b[at] = (byte)value;
            b[at + 1] = (byte)(value >> 8);
        }

        private static void Put32(byte[] b, int at, int value)
        {
            b[at] = (byte)value;
            b[at + 1] = (byte)(value >> 8);
            b[at + 2] = (byte)(value >> 16);
            b[at + 3] = (byte)(value >> 24);
        }
    }
}
