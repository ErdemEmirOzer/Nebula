using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    public sealed class NebulaAudioPart02AssetTests
    {
        private const string Root = "Assets/Resources/Audio/Nebula/Part02";

        /// <summary>
        /// The 73MB Nebula audio tree is produced but not yet in git — Assets/Resources/Audio/**
        /// has zero tracked files while the LFS decision is open. Without this
        /// guard every test below fails by construction on a clean checkout, which
        /// says "the audio is broken" when the truth is "the audio is not here".
        /// <para>
        /// A named skip is not a pass: NUnit reports it as ignored with this reason
        /// attached. The guard is the directory's existence and nothing weaker — if
        /// the tree is present, every count and import assertion runs unmodified, so
        /// a tree that is present and wrong is still red.
        /// </para>
        /// </summary>
        [SetUp]
        public void RequireTheAudioTree()
        {
            string full = System.IO.Path.Combine(
                System.IO.Path.GetFullPath(Path.Combine(Application.dataPath, "..")), Root);
            if (!Directory.Exists(full))
                Assert.Ignore(
                    "SKIPPED — " + Root + " is not present in this checkout. The 87 "
                    + "Part02 clips are produced but untracked pending the 73MB LFS decision; "
                    + "commit the audio tree and these assertions run unchanged.");
        }

        [Test]
        public void Part02_ContainsExactly87LoadableUniqueAudioClips()
        {
            string[] paths = AssetDatabase.FindAssets("t:AudioClip", new[] { Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.AreEqual(87, paths.Length);
            CollectionAssert.AllItemsAreUnique(paths);
            foreach (string path in paths)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.NotNull(clip, path);
                Assert.AreEqual(48000, clip.frequency, path);
                Assert.Greater(clip.samples, 0, path);
            }
        }

        [Test]
        public void Part02_CategoryCountsCoverEveryApprovedIndustrialFamily()
        {
            var expected = new Dictionary<string, int>
            {
                { "Exploration", 8 },
                { "Construction", 15 },
                { "Drilling", 13 },
                { "Processing", 10 },
                { "Hangar", 8 },
                { "Vehicles", 10 },
                { "Logistics", 16 },
                { "Status", 7 }
            };
            foreach (var pair in expected)
            {
                string[] guids = AssetDatabase.FindAssets(
                    "t:AudioClip", new[] { $"{Root}/{pair.Key}" });
                Assert.AreEqual(pair.Value, guids.Length, pair.Key);
            }
        }

        [Test]
        public void Part02_SpatialAndImportContractsMatchTheProductionPlan()
        {
            AudioClip status = Load("Status/dominance_controlled.wav");
            AudioClip drill = Load("Drilling/drill_t2_loop.wav");
            AudioImporter statusImporter = Importer("Status/dominance_controlled.wav");
            AudioImporter drillImporter = Importer("Drilling/drill_t2_loop.wav");
            AudioImporter impactImporter = Importer("Construction/build_capsule_impact.wav");

            Assert.AreEqual(2, status.channels);
            Assert.AreEqual(1, drill.channels);
            Assert.IsFalse(statusImporter.forceToMono);
            Assert.IsTrue(drillImporter.forceToMono);
            Assert.AreEqual(AudioClipLoadType.CompressedInMemory,
                drillImporter.defaultSampleSettings.loadType);
            Assert.AreEqual(AudioCompressionFormat.Vorbis,
                drillImporter.defaultSampleSettings.compressionFormat);
            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad,
                impactImporter.defaultSampleSettings.loadType);
            Assert.AreEqual(AudioCompressionFormat.PCM,
                impactImporter.defaultSampleSettings.compressionFormat);
        }

        [Test]
        public void Part02_WavFilesHaveNoDuplicateBytes()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] files = Directory.GetFiles(
                Path.Combine(projectRoot, Root), "*.wav", SearchOption.AllDirectories);
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                string digest = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file)))
                    .Replace("-", string.Empty);
                Assert.IsTrue(hashes.Add(digest), $"Duplicate waveform bytes: {file}");
            }
            Assert.AreEqual(87, hashes.Count);
        }

        private static AudioClip Load(string relative) =>
            AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/{relative}");

        private static AudioImporter Importer(string relative) =>
            (AudioImporter)AssetImporter.GetAtPath($"{Root}/{relative}");
    }
}
