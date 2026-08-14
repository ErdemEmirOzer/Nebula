using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    public sealed class NebulaAudioPart04AssetTests
    {
        private const string Root = "Assets/Resources/Audio/Nebula/Part04";

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
                    "SKIPPED — " + Root + " is not present in this checkout. The 52 "
                    + "Part04 clips are produced but untracked pending the 73MB LFS decision; "
                    + "commit the audio tree and these assertions run unchanged.");
        }

        [Test]
        public void Part04_ContainsExactly52LoadableAudioClips()
        {
            string[] paths = AssetDatabase.FindAssets("t:AudioClip", new[] { Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.AreEqual(52, paths.Length);
            CollectionAssert.AllItemsAreUnique(paths);
            foreach (string path in paths)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.NotNull(clip, path);
                Assert.AreEqual(48000, clip.frequency, path);
                Assert.That(clip.channels, Is.EqualTo(1).Or.EqualTo(2), path);
                Assert.Greater(clip.samples, 0, path);
            }
        }

        [Test]
        public void Part04_CategoryCountsCoverTheApprovedMusicFreeScope()
        {
            var expected = new Dictionary<string, int>
            {
                { "World", 20 },
                { "Anomaly", 5 },
                { "Wreck", 6 },
                { "Facility", 7 },
                { "POI", 3 },
                { "Remote", 11 }
            };
            foreach (var pair in expected)
            {
                string[] guids = AssetDatabase.FindAssets(
                    "t:AudioClip", new[] { $"{Root}/{pair.Key}" });
                Assert.AreEqual(pair.Value, guids.Length, pair.Key);
            }
        }

        [Test]
        public void Part04_SpatialAndImportProfilesAreCorrect()
        {
            AudioClip deepSpace = Clip("World/world_deep_space_loop.wav");
            AudioClip redStar = Clip("World/star_red_loop.wav");
            AudioImporter deepSpaceImporter = Importer("World/world_deep_space_loop.wav");
            AudioImporter starImporter = Importer("World/star_red_loop.wav");
            AudioImporter anomalySignal = Importer("Anomaly/anomaly_signal.wav");

            Assert.AreEqual(2, deepSpace.channels);
            Assert.AreEqual(1, redStar.channels);
            Assert.IsFalse(deepSpaceImporter.forceToMono);
            Assert.IsTrue(starImporter.forceToMono);
            Assert.AreEqual(AudioClipLoadType.CompressedInMemory,
                deepSpaceImporter.defaultSampleSettings.loadType);
            Assert.AreEqual(AudioCompressionFormat.Vorbis,
                starImporter.defaultSampleSettings.compressionFormat);
            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad,
                anomalySignal.defaultSampleSettings.loadType);
            Assert.AreEqual(AudioCompressionFormat.PCM,
                anomalySignal.defaultSampleSettings.compressionFormat);
        }

        [Test]
        public void Part04_WavFilesHaveNoDuplicateBytes()
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
            Assert.AreEqual(52, hashes.Count);
        }

        private static AudioClip Clip(string relative) =>
            AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/{relative}");

        private static AudioImporter Importer(string relative) =>
            (AudioImporter)AssetImporter.GetAtPath($"{Root}/{relative}");
    }
}
