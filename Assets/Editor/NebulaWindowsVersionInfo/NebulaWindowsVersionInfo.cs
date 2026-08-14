using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nebula.EditorTools
{
    /// <summary>
    /// Writes the game's own identity into the Windows player's <c>VS_VERSIONINFO</c>
    /// resource, after the build, in place.
    /// <para>
    /// <b>Why this exists.</b> Unity stamps a Windows player's version resource from a
    /// step that only runs on a Windows host. NEBULA's Windows build is produced on a
    /// Mac, so that step never ran, and the shipped <c>NEBULA.exe</c> came out carrying
    /// Unity's own resource instead of the game's: right-clicking it in Explorer showed
    /// <c>2022.3.22f1</c> and "(c) 2005-2024 Unity Technologies", with no ProductName,
    /// CompanyName, FileDescription or OriginalFilename at all. That is worse than empty
    /// metadata — it attributes the product to the engine vendor, and it is the first
    /// thing SmartScreen, an antivirus heuristic and a curious playtester all look at.
    /// <c>PlayerSettings.companyName</c> and <c>productName</c> were already correct;
    /// nothing carried them into the executable.
    /// </para>
    /// <para>
    /// <b>Why a post-process and not a setting.</b> Unity exposes no API for the Windows
    /// version resource, and the fields below are not derivable from anything the player
    /// build writes. This is the same shape as
    /// <see cref="NebulaBuildAutomation"/>'s macOS <c>CFBundleDisplayName</c> step: a
    /// deterministic edit of a file this pipeline just produced, reported into the build's
    /// hardening list rather than thrown, because a cosmetic metadata miss must not
    /// destroy an otherwise good release build.
    /// </para>
    /// <para>
    /// <b>Why it is safe.</b> Rewriting a resource inside a PE image can corrupt the
    /// executable if it moves anything. This one cannot, because it refuses rather than
    /// improvises. Four preconditions are checked and any failure aborts before a single
    /// byte is written:
    /// </para>
    /// <list type="number">
    /// <item>The image carries no Authenticode certificate. Editing a signed binary
    /// silently invalidates its signature, and a silently-unsigned installer is a worse
    /// outcome than unstamped metadata.</item>
    /// <item>The <c>RT_VERSION</c> blob is the <i>last</i> data in <c>.rsrc</c>. That
    /// makes the only space it can grow into the section's own file-alignment padding, so
    /// the rewrite is physically incapable of landing on an icon, the manifest, or any
    /// other resource.</item>
    /// <item>The new blob fits in that budget (old blob + padding).</item>
    /// <item>Growth does not change the section's page-aligned virtual extent. If it did,
    /// <c>.reloc</c> would have to move and <c>SizeOfImage</c> would have to change — real
    /// PE surgery, which this deliberately does not attempt.</item>
    /// </list>
    /// <para>
    /// The result is that exactly three things change: the version blob's bytes, the
    /// four-byte Size field of its resource data entry, and <c>.rsrc</c>'s VirtualSize.
    /// File length, section count, section addresses, every icon byte and the manifest are
    /// untouched. <see cref="ReadVersionStrings"/> exists so a test — and the build report
    /// — can verify that from the produced file rather than take it on trust.
    /// </para>
    /// </summary>
    public static class NebulaWindowsVersionInfo
    {
        /// <summary>Resource type id for <c>RT_VERSION</c>, from winuser.h.</summary>
        private const int RtVersion = 16;

        /// <summary>US English / Unicode: the langID+codepage the Unity player already used.</summary>
        private const string LanguageId = "0409";
        private const string CodePageId = "04b0";

        /// <summary>
        /// Windows' section granularity in memory. Unity's players use the x64 default and
        /// the guard below re-derives everything from it, so a build that ever changed it
        /// would fail the extent check rather than produce a subtly broken image.
        /// </summary>
        private const uint SectionAlignment = 0x1000;

        /// <summary>
        /// Stamps <paramref name="exePath"/> and returns one line for the build report's
        /// hardening list. Never throws: a returned "NOT SET" line is the failure channel,
        /// because the caller is a finished release build that is otherwise good.
        /// </summary>
        public static string Stamp(string exePath, string companyName, string productName,
            string fileVersion)
        {
            try
            {
                if (!File.Exists(exePath)) return "Windows VS_VERSIONINFO: NOT SET (no file at " + exePath + ")";

                string quad = NormalizeVersion(fileVersion);
                var strings = new List<KeyValuePair<string, string>>
                {
                    // Ordered by key. Windows does not care, but a deterministic order
                    // means two builds of the same commit produce identical bytes here.
                    new KeyValuePair<string, string>("CompanyName", companyName),
                    new KeyValuePair<string, string>("FileDescription", productName),
                    new KeyValuePair<string, string>("FileVersion", quad),
                    new KeyValuePair<string, string>("LegalCopyright", "(c) " + CopyrightYear + " " + companyName),
                    new KeyValuePair<string, string>("OriginalFilename", Path.GetFileName(exePath)),
                    new KeyValuePair<string, string>("ProductName", productName),
                    new KeyValuePair<string, string>("ProductVersion", quad),
                };

                byte[] image = File.ReadAllBytes(exePath);
                string refusal = TryPatch(image, strings, quad, out int written);
                if (refusal != null) return "Windows VS_VERSIONINFO: NOT SET (" + refusal + ")";

                File.WriteAllBytes(exePath, image);

                // Read it back out of the bytes now on disk. A stamp that reports success
                // without re-reading is exactly how the Unity-branded resource survived
                // this long unnoticed.
                IReadOnlyDictionary<string, string> readBack = ReadVersionStrings(exePath);
                if (!readBack.TryGetValue("ProductName", out string product) || product != productName)
                    return "Windows VS_VERSIONINFO: WRITTEN BUT UNVERIFIABLE (ProductName read back as '" +
                           (product ?? "<absent>") + "')";

                return "Windows VS_VERSIONINFO = " + productName + " / " + companyName +
                       " / " + quad + " (" + written + " bytes, icons and manifest untouched)";
            }
            catch (Exception e)
            {
                return "Windows VS_VERSIONINFO: NOT SET (" + e.GetType().Name + ": " + e.Message + ")";
            }
        }

        /// <summary>
        /// The year in the copyright line. A constant rather than <c>DateTime.Now</c>:
        /// a build's bytes must not depend on the day it was run, or two builds of one
        /// commit stop being comparable and every provenance hash rots at midnight.
        /// </summary>
        private const string CopyrightYear = "2026";

        // -------------------------------------------------------------------
        // Patching
        // -------------------------------------------------------------------

        /// <summary>
        /// Applies the rewrite to <paramref name="image"/> in place. Returns null on
        /// success, or a human-readable refusal describing which precondition failed.
        /// </summary>
        internal static string TryPatch(byte[] image,
            IReadOnlyList<KeyValuePair<string, string>> strings, string quad, out int written)
        {
            written = 0;
            if (!TryReadHeaders(image, out PeHeaders pe, out string headerError)) return headerError;

            if (pe.CertificateSize > 0)
                return "image is Authenticode-signed (" + pe.CertificateSize +
                       " bytes); rewriting resources would invalidate the signature";

            if (!TryFindVersionEntry(image, pe, out int entryOffset, out uint blobRva, out int blobSize))
                return "no RT_VERSION resource to replace";

            int sectionIndex = SectionContaining(pe, blobRva);
            if (sectionIndex < 0) return "RT_VERSION blob is not inside any section";
            PeSection section = pe.Sections[sectionIndex];

            if (blobRva + (uint)blobSize != section.VirtualAddress + section.VirtualSize)
                return "RT_VERSION is not the last data in " + section.Name +
                       ", so growing it could overwrite another resource";

            long budget = section.VirtualAddress + (long)section.RawSize - blobRva;
            byte[] blob = BuildVersionInfo(strings, quad);
            if (blob.Length > budget)
                return "new version blob is " + blob.Length + " bytes, available budget is " + budget;

            uint newVirtualSize = Math.Max(section.VirtualSize,
                blobRva + (uint)blob.Length - section.VirtualAddress);
            if (AlignUp(newVirtualSize, SectionAlignment) != AlignUp(section.VirtualSize, SectionAlignment))
                return "growth would change " + section.Name + "'s page extent and move the sections after it";

            int blobOffset = (int)(section.RawPointer + (blobRva - section.VirtualAddress));
            int span = Math.Max(blobSize, blob.Length);
            Array.Copy(blob, 0, image, blobOffset, blob.Length);
            // Zero the tail of the old blob so no fragment of the previous resource — in
            // practice Unity's copyright string — survives inside the section.
            for (int i = blob.Length; i < span; i++) image[blobOffset + i] = 0;

            WriteUInt32(image, entryOffset + 4, (uint)blob.Length);
            if (newVirtualSize != section.VirtualSize)
                WriteUInt32(image, section.HeaderOffset + 8, newVirtualSize);

            written = blob.Length;
            return null;
        }

        // -------------------------------------------------------------------
        // VS_VERSIONINFO construction
        // -------------------------------------------------------------------

        /// <summary>
        /// Builds the <c>VS_VERSIONINFO</c> blob. The format is a tree of nodes, each
        /// <c>{ushort length; ushort valueLength; ushort type; wchar key[]; pad; value}</c>
        /// with every node and every value aligned to four bytes. <c>valueLength</c> counts
        /// <i>characters</i> for text nodes and <i>bytes</i> for binary ones — the single
        /// most common way a hand-built version resource comes out unreadable.
        /// </summary>
        public static byte[] BuildVersionInfo(
            IReadOnlyList<KeyValuePair<string, string>> strings, string quad)
        {
            int[] v = ParseQuad(quad);
            var fixedInfo = new List<byte>();
            AddUInt32(fixedInfo, 0xFEEF04BD);                       // dwSignature
            AddUInt32(fixedInfo, 0x00010000);                       // dwStrucVersion 1.0
            AddUInt32(fixedInfo, (uint)((v[0] << 16) | v[1]));      // dwFileVersionMS
            AddUInt32(fixedInfo, (uint)((v[2] << 16) | v[3]));      // dwFileVersionLS
            AddUInt32(fixedInfo, (uint)((v[0] << 16) | v[1]));      // dwProductVersionMS
            AddUInt32(fixedInfo, (uint)((v[2] << 16) | v[3]));      // dwProductVersionLS
            AddUInt32(fixedInfo, 0x0000003F);                       // dwFileFlagsMask
            AddUInt32(fixedInfo, 0x00000000);                       // dwFileFlags: release, not prerelease/debug
            AddUInt32(fixedInfo, 0x00000004);                       // dwFileOS  = VOS_NT_WINDOWS32
            AddUInt32(fixedInfo, 0x00000001);                       // dwFileType = VFT_APP
            AddUInt32(fixedInfo, 0x00000000);                       // dwFileSubtype
            AddUInt32(fixedInfo, 0x00000000);                       // dwFileDateMS
            AddUInt32(fixedInfo, 0x00000000);                       // dwFileDateLS

            var table = new List<byte>();
            foreach (KeyValuePair<string, string> pair in strings)
                table.AddRange(TextNode(pair.Key, pair.Value));
            byte[] stringTable = Container(LanguageId + CodePageId, table.ToArray(), Array.Empty<byte>(), 1);
            byte[] stringFileInfo = Container("StringFileInfo", stringTable, Array.Empty<byte>(), 1);

            var translation = new List<byte>();
            AddUInt16(translation, Convert.ToUInt16(LanguageId, 16));
            AddUInt16(translation, Convert.ToUInt16(CodePageId, 16));
            byte[] varFileInfo = Container("VarFileInfo",
                BinaryNode("Translation", translation.ToArray()), Array.Empty<byte>(), 1);

            var children = new List<byte>();
            children.AddRange(stringFileInfo);
            children.AddRange(varFileInfo);
            return Container("VS_VERSION_INFO", children.ToArray(), fixedInfo.ToArray(), 0);
        }

        private static byte[] TextNode(string key, string value)
        {
            byte[] head = NodeHead(key);
            byte[] body = Pad4(Encoding.Unicode.GetBytes(value + "\0"));
            return Assemble(head, body, value.Length + 1, 1);
        }

        private static byte[] BinaryNode(string key, byte[] value)
        {
            byte[] head = NodeHead(key);
            return Assemble(head, Pad4(value), value.Length, 0);
        }

        private static byte[] Container(string key, byte[] children, byte[] value, ushort type)
        {
            byte[] head = NodeHead(key);
            var body = new List<byte>();
            body.AddRange(Pad4(value));
            body.AddRange(children);
            return Assemble(head, body.ToArray(), value.Length, type);
        }

        /// <summary>The 6-byte header placeholder plus the NUL-terminated UTF-16 key, padded to 4.</summary>
        private static byte[] NodeHead(string key)
        {
            var head = new List<byte> { 0, 0, 0, 0, 0, 0 };
            head.AddRange(Encoding.Unicode.GetBytes(key + "\0"));
            return Pad4(head.ToArray());
        }

        private static byte[] Assemble(byte[] head, byte[] body, int valueLength, ushort type)
        {
            var node = new List<byte>();
            node.AddRange(head);
            node.AddRange(body);
            byte[] bytes = node.ToArray();
            WriteUInt16(bytes, 0, (ushort)bytes.Length);
            WriteUInt16(bytes, 2, (ushort)valueLength);
            WriteUInt16(bytes, 4, type);
            return bytes;
        }

        private static byte[] Pad4(byte[] value)
        {
            int pad = (4 - value.Length % 4) % 4;
            if (pad == 0) return value;
            var padded = new byte[value.Length + pad];
            Array.Copy(value, padded, value.Length);
            return padded;
        }

        /// <summary>
        /// Turns a Unity <c>bundleVersion</c> into the four-part numeric form Windows
        /// requires. "0.1.0" alone would show as "0.1.0.0"; the release is "0.1.0.1", so a
        /// caller may pass either and gets a well-formed quad back. Non-numeric parts
        /// (a "0.1.0-beta") degrade to 0 rather than throwing mid-build.
        /// </summary>
        public static string NormalizeVersion(string version)
        {
            int[] quad = ParseQuad(version);
            return quad[0] + "." + quad[1] + "." + quad[2] + "." + quad[3];
        }

        private static int[] ParseQuad(string version)
        {
            var quad = new int[4];
            if (string.IsNullOrEmpty(version)) return quad;
            string[] parts = version.Split('.');
            for (int i = 0; i < 4 && i < parts.Length; i++)
            {
                int digits = 0;
                while (digits < parts[i].Length && char.IsDigit(parts[i][digits])) digits++;
                if (digits > 0 && int.TryParse(parts[i].Substring(0, digits), out int value))
                    quad[i] = Math.Min(value, ushort.MaxValue);
            }
            return quad;
        }

        // -------------------------------------------------------------------
        // Read-back
        // -------------------------------------------------------------------

        /// <summary>
        /// Parses the string table out of a PE's <c>RT_VERSION</c> resource. Deliberately
        /// an independent reader rather than a cache of what was written, so that a test
        /// asserting on it is testing the file and not this class's intentions.
        /// </summary>
        public static IReadOnlyDictionary<string, string> ReadVersionStrings(string exePath)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            byte[] image = File.ReadAllBytes(exePath);
            if (!TryReadHeaders(image, out PeHeaders pe, out _)) return result;
            if (!TryFindVersionEntry(image, pe, out _, out uint rva, out int size)) return result;
            int sectionIndex = SectionContaining(pe, rva);
            if (sectionIndex < 0) return result;
            PeSection section = pe.Sections[sectionIndex];
            int offset = (int)(section.RawPointer + (rva - section.VirtualAddress));
            if (offset < 0 || offset + size > image.Length) return result;

            // Walk every node in the blob and keep the text ones that sit two levels below
            // StringFileInfo. Structurally naive on purpose: the goal is to read what
            // Explorer would read, not to validate the tree a second time.
            ReadNodes(image, offset, offset + size, result, false);
            return result;
        }

        private static void ReadNodes(byte[] image, int start, int end,
            Dictionary<string, string> into, bool inStringTable)
        {
            int pos = start;
            while (pos + 6 <= end)
            {
                ushort length = ReadUInt16(image, pos);
                if (length < 6 || pos + length > end) return;
                ushort valueLength = ReadUInt16(image, pos + 2);
                ushort type = ReadUInt16(image, pos + 4);

                int keyStart = pos + 6;
                int k = keyStart;
                while (k + 1 < end && !(image[k] == 0 && image[k + 1] == 0)) k += 2;
                string key = Encoding.Unicode.GetString(image, keyStart, k - keyStart);
                int valueStart = Align4(k + 2);

                if (inStringTable && type == 1 && valueLength > 0)
                {
                    int bytes = Math.Min(valueLength * 2, end - valueStart);
                    if (bytes > 0)
                        into[key] = Encoding.Unicode.GetString(image, valueStart, bytes).TrimEnd('\0');
                }
                else
                {
                    int childStart = Align4(valueStart + (type == 1 ? valueLength * 2 : valueLength));
                    if (childStart < pos + length)
                        ReadNodes(image, childStart, pos + length, into,
                            inStringTable || key == "StringFileInfo");
                }

                pos = Align4(pos + length);
                if (pos <= start) return;
                start = pos;
            }
        }

        // -------------------------------------------------------------------
        // Minimal PE reading
        // -------------------------------------------------------------------

        internal struct PeSection
        {
            public string Name;
            public uint VirtualSize;
            public uint VirtualAddress;
            public uint RawSize;
            public uint RawPointer;
            public int HeaderOffset;
        }

        internal struct PeHeaders
        {
            public uint ResourceRva;
            public uint CertificateSize;
            public PeSection[] Sections;
        }

        private static bool TryReadHeaders(byte[] image, out PeHeaders headers, out string error)
        {
            headers = default;
            error = null;
            if (image.Length < 0x40 || image[0] != 'M' || image[1] != 'Z')
            {
                error = "not a PE image (no MZ header)";
                return false;
            }
            int peOffset = (int)ReadUInt32(image, 0x3C);
            if (peOffset <= 0 || peOffset + 24 > image.Length ||
                image[peOffset] != 'P' || image[peOffset + 1] != 'E')
            {
                error = "not a PE image (no PE signature)";
                return false;
            }
            int coff = peOffset + 4;
            int sectionCount = ReadUInt16(image, coff + 2);
            int optionalSize = ReadUInt16(image, coff + 16);
            int optional = coff + 20;
            ushort magic = ReadUInt16(image, optional);
            int directories = magic == 0x20B ? optional + 112 : optional + 96;

            headers.ResourceRva = ReadUInt32(image, directories + 2 * 8);
            headers.CertificateSize = ReadUInt32(image, directories + 4 * 8 + 4);

            var sections = new PeSection[sectionCount];
            int table = optional + optionalSize;
            for (int i = 0; i < sectionCount; i++)
            {
                int at = table + i * 40;
                sections[i] = new PeSection
                {
                    Name = Encoding.ASCII.GetString(image, at, 8).TrimEnd('\0'),
                    VirtualSize = ReadUInt32(image, at + 8),
                    VirtualAddress = ReadUInt32(image, at + 12),
                    RawSize = ReadUInt32(image, at + 16),
                    RawPointer = ReadUInt32(image, at + 20),
                    HeaderOffset = at,
                };
            }
            headers.Sections = sections;
            return true;
        }

        private static int SectionContaining(PeHeaders pe, uint rva)
        {
            for (int i = 0; i < pe.Sections.Length; i++)
            {
                PeSection s = pe.Sections[i];
                uint extent = Math.Max(s.VirtualSize, s.RawSize);
                if (rva >= s.VirtualAddress && rva < s.VirtualAddress + extent) return i;
            }
            return -1;
        }

        /// <summary>
        /// Walks the three-level resource directory (type / name / language) to the single
        /// <c>RT_VERSION</c> data entry, returning the entry's own file offset — which is
        /// what has to be edited to change the blob's recorded size — plus the blob's RVA
        /// and current size.
        /// </summary>
        private static bool TryFindVersionEntry(byte[] image, PeHeaders pe,
            out int entryOffset, out uint blobRva, out int blobSize)
        {
            entryOffset = 0;
            blobRva = 0;
            blobSize = 0;
            int rootSection = SectionContaining(pe, pe.ResourceRva);
            if (pe.ResourceRva == 0 || rootSection < 0) return false;
            PeSection rsrc = pe.Sections[rootSection];
            int root = (int)(rsrc.RawPointer + (pe.ResourceRva - rsrc.VirtualAddress));

            foreach ((uint id, bool isDirectory, int offset) in Entries(image, root, 0))
            {
                if (!isDirectory || id != RtVersion) continue;
                foreach ((uint _, bool nameIsDirectory, int nameOffset) in Entries(image, root, offset))
                {
                    if (!nameIsDirectory) continue;
                    foreach ((uint _, bool langIsDirectory, int langOffset) in Entries(image, root, nameOffset))
                    {
                        if (langIsDirectory) continue;
                        entryOffset = root + langOffset;
                        blobRva = ReadUInt32(image, entryOffset);
                        blobSize = (int)ReadUInt32(image, entryOffset + 4);
                        return true;
                    }
                }
            }
            return false;
        }

        private static IEnumerable<(uint Id, bool IsDirectory, int Offset)> Entries(
            byte[] image, int root, int directoryOffset)
        {
            int at = root + directoryOffset;
            int named = ReadUInt16(image, at + 12);
            int ids = ReadUInt16(image, at + 14);
            int first = at + 16;
            for (int i = 0; i < named + ids; i++)
            {
                uint name = ReadUInt32(image, first + i * 8);
                uint offset = ReadUInt32(image, first + i * 8 + 4);
                yield return (name & 0x7FFFFFFF, (offset & 0x80000000) != 0, (int)(offset & 0x7FFFFFFF));
            }
        }

        // -------------------------------------------------------------------
        // Little-endian primitives
        // -------------------------------------------------------------------

        private static int Align4(int value) => (value + 3) & ~3;
        private static uint AlignUp(uint value, uint alignment) => (value + alignment - 1) / alignment * alignment;

        private static ushort ReadUInt16(byte[] b, int at) => (ushort)(b[at] | (b[at + 1] << 8));
        private static uint ReadUInt32(byte[] b, int at) =>
            (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24));

        private static void WriteUInt16(byte[] b, int at, ushort value)
        {
            b[at] = (byte)value;
            b[at + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] b, int at, uint value)
        {
            b[at] = (byte)value;
            b[at + 1] = (byte)(value >> 8);
            b[at + 2] = (byte)(value >> 16);
            b[at + 3] = (byte)(value >> 24);
        }

        private static void AddUInt16(List<byte> into, ushort value)
        {
            into.Add((byte)value);
            into.Add((byte)(value >> 8));
        }

        private static void AddUInt32(List<byte> into, uint value)
        {
            into.Add((byte)value);
            into.Add((byte)(value >> 8));
            into.Add((byte)(value >> 16));
            into.Add((byte)(value >> 24));
        }
    }
}
