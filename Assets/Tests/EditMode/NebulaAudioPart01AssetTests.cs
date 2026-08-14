using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    public sealed class NebulaAudioPart01AssetTests
    {
        private const string Root = "Assets/Resources/Audio/Nebula/Part01";

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
                    "SKIPPED — " + Root + " is not present in this checkout. The 46 "
                    + "Part01 clips are produced but untracked pending the 73MB LFS decision; "
                    + "commit the audio tree and these assertions run unchanged.");
        }

        [Test]
        public void Part01_ContainsExactly46LoadableUniqueAudioClips()
        {
            string[] paths = AssetDatabase.FindAssets("t:AudioClip", new[] { Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.AreEqual(46, paths.Length);
            CollectionAssert.AllItemsAreUnique(paths);
            foreach (string path in paths)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.NotNull(clip, path);
                Assert.AreEqual(48000, clip.frequency, path);
                Assert.Greater(clip.samples, 0, path);
                Assert.IsTrue(clip.channels == 1 || clip.channels == 2, path);
            }
        }

        [Test]
        public void Part01_SpatialAndStereoContractsMatchTheProductionPlan()
        {
            string[] stereo =
            {
                "UI/ui_confirm.wav",
                "UI/ui_map_open.wav",
                "Navigation/nav_heading_lock.wav",
                "Navigation/nav_system_enter.wav",
                "Ship/ship_cockpit_ambience_loop.wav",
                "Ship/ship_energy_low.wav",
                "Ship/ship_overheat_warning.wav"
            };
            string[] mono =
            {
                "Ship/ship_engine_idle_loop.wav",
                "Ship/ship_engine_thrust_loop.wav",
                "Ship/ship_boost_start.wav",
                "Ship/ship_collision_heavy_01.wav",
                "Ship/ship_dock.wav"
            };

            foreach (string relative in stereo)
                Assert.AreEqual(2, Load(relative).channels, relative);
            foreach (string relative in mono)
                Assert.AreEqual(1, Load(relative).channels, relative);
        }

        [Test]
        public void Part01_ImportSettingsSeparateShortAndLoopingContent()
        {
            AudioImporter ui = Importer("UI/ui_confirm.wav");
            AudioImporter loop = Importer("Ship/ship_engine_idle_loop.wav");
            AudioImporter physical = Importer("Ship/ship_collision_heavy_01.wav");

            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, ui.defaultSampleSettings.loadType);
            Assert.AreEqual(AudioCompressionFormat.PCM, ui.defaultSampleSettings.compressionFormat);
            Assert.IsFalse(ui.forceToMono);

            Assert.AreEqual(AudioClipLoadType.CompressedInMemory, loop.defaultSampleSettings.loadType);
            Assert.AreEqual(AudioCompressionFormat.Vorbis, loop.defaultSampleSettings.compressionFormat);
            Assert.IsTrue(loop.forceToMono);
            Assert.IsTrue(loop.loadInBackground);

            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, physical.defaultSampleSettings.loadType);
            Assert.IsTrue(physical.forceToMono);
        }

        [Test]
        public void Part01_WavFilesHaveNoDuplicateBytes()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullRoot = Path.Combine(projectRoot, Root);
            string[] files = Directory.GetFiles(fullRoot, "*.wav", SearchOption.AllDirectories);
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                string digest = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file)))
                    .Replace("-", string.Empty);
                Assert.IsTrue(hashes.Add(digest), $"Duplicate waveform bytes: {file}");
            }
            Assert.AreEqual(46, hashes.Count);
        }

        private static AudioClip Load(string relative) =>
            AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/{relative}");

        private static AudioImporter Importer(string relative) =>
            (AudioImporter)AssetImporter.GetAtPath($"{Root}/{relative}");
    }
}
