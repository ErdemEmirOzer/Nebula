using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    public sealed class NebulaAudioPart03AssetTests
    {
        private const string Root = "Assets/Resources/Audio/Nebula/Part03";

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
                    "SKIPPED — " + Root + " is not present in this checkout. The 107 "
                    + "Part03 clips are produced but untracked pending the 73MB LFS decision; "
                    + "commit the audio tree and these assertions run unchanged.");
        }

        [Test]
        public void Part03_ContainsExactly107LoadableUniqueAudioClips()
        {
            string[] paths = AssetDatabase.FindAssets("t:AudioClip", new[] { Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.AreEqual(107, paths.Length);
            CollectionAssert.AllItemsAreUnique(paths);
            foreach (string path in paths)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.NotNull(clip, path);
                Assert.AreEqual(48000, clip.frequency, path);
                Assert.AreEqual(1, clip.channels, path);
                Assert.Greater(clip.samples, 0, path);
            }
        }

        [Test]
        public void Part03_CategoryCountsCoverEveryApprovedCombatFamily()
        {
            var expected = new Dictionary<string, int>
            {
                { "PlayerWeapons", 22 },
                { "Damage", 17 },
                { "Defense", 10 },
                { "EnemyCommon", 15 },
                { "Enemies", 21 },
                { "Raid", 10 },
                { "PirateBase", 7 },
                { "Training", 5 }
            };
            foreach (var pair in expected)
            {
                string[] guids = AssetDatabase.FindAssets(
                    "t:AudioClip", new[] { $"{Root}/{pair.Key}" });
                Assert.AreEqual(pair.Value, guids.Length, pair.Key);
            }
        }

        [Test]
        public void Part03_LoopsAndOneShotsUseTheirCorrectImportProfiles()
        {
            AudioImporter laserLoop = Importer("PlayerWeapons/laser_loop.wav");
            AudioImporter plasmaImpact = Importer("PlayerWeapons/plasma_impact.wav");
            AudioImporter siegeLoop = Importer("Enemies/siege_projectile_loop.wav");

            Assert.IsTrue(laserLoop.forceToMono);
            Assert.AreEqual(AudioClipLoadType.CompressedInMemory,
                laserLoop.defaultSampleSettings.loadType);
            Assert.AreEqual(AudioCompressionFormat.Vorbis,
                laserLoop.defaultSampleSettings.compressionFormat);
            Assert.AreEqual(AudioClipLoadType.CompressedInMemory,
                siegeLoop.defaultSampleSettings.loadType);
            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad,
                plasmaImpact.defaultSampleSettings.loadType);
            Assert.AreEqual(AudioCompressionFormat.PCM,
                plasmaImpact.defaultSampleSettings.compressionFormat);
        }

        [Test]
        public void Part03_WavFilesHaveNoDuplicateBytes()
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
            Assert.AreEqual(107, hashes.Count);
        }

        private static AudioImporter Importer(string relative) =>
            (AudioImporter)AssetImporter.GetAtPath($"{Root}/{relative}");
    }
}
