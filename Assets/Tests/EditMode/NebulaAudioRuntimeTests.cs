using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Nebula.Audio;
using Nebula.Combat;
using Nebula.Ship;
using Nebula.Remote;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    public sealed class NebulaAudioRuntimeTests
    {
        private const string CatalogPath = "Assets/Resources/Audio/NebulaAudioCatalog.asset";

        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>
        /// The built catalog is generated from the 73MB audio tree, which is
        /// produced but untracked. Absent, the three tests that read it would fail
        /// by construction on a clean checkout and say nothing true. This names the
        /// missing input instead — and only for those three: the five tests in this
        /// fixture that exercise the runtime service, the remote-command seam and
        /// the damage seam need no assets and always run.
        /// </summary>
        private static NebulaAudioCatalog RequireBuiltCatalog()
        {
            NebulaAudioCatalog catalog =
                AssetDatabase.LoadAssetAtPath<NebulaAudioCatalog>(CatalogPath);
            if (catalog == null && !File.Exists(Path.Combine(ProjectRoot, CatalogPath)))
                Assert.Ignore(
                    "SKIPPED — " + CatalogPath + " is not present in this checkout. It is "
                    + "generated from the untracked 73MB audio tree; commit the audio and "
                    + "this assertion runs unchanged.");

            Assert.NotNull(catalog, CatalogPath + " exists on disk but did not import");
            return catalog;
        }

        /// <summary>
        /// Every gameplay source file, read once each and matched in place.
        /// <para>
        /// The 422 files under <c>Assets/Scripts/Nebula</c> come to 3.6MB. Joining
        /// them into one string to run a single regex over allocates that 3.6MB on
        /// the large-object heap for no gain — the pattern never spans a file
        /// boundary. Scanning per file is the same answer without the allocation.
        /// The path is absolute because a relative one silently depends on the
        /// editor's working directory, and <c>Directory.GetFiles</c> on a wrong
        /// relative path throws rather than reporting what it was looking for.
        /// </para>
        /// </summary>
        private static HashSet<string> EventIdsReferencedIn(string relativeDirectory, string pattern)
        {
            string directory = Path.Combine(ProjectRoot, relativeDirectory);
            Assert.IsTrue(Directory.Exists(directory), "source directory is missing: " + directory);

            string[] files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
            Assert.Greater(files.Length, 0,
                $"no {pattern} sources under {directory} — the scan would pass vacuously");

            var referenced = new HashSet<string>(StringComparer.Ordinal);
            var expression = new Regex("\"(audio\\.[a-zA-Z0-9_.-]+)\"", RegexOptions.Compiled);
            foreach (string file in files)
                foreach (Match match in expression.Matches(File.ReadAllText(file)))
                    referenced.Add(match.Groups[1].Value);
            return referenced;
        }

        [Test]
        public void Catalog_IndexesUniqueEntriesAndMissingIdIsSafe()
        {
            var catalog = ScriptableObject.CreateInstance<NebulaAudioCatalog>();
            var clip = AudioClip.Create("test", 480, 1, 48000, false);
            catalog.ReplaceEntries(new List<NebulaAudioEntry>
            {
                new NebulaAudioEntry("audio.test", clip, NebulaAudioBus.UI,
                    NebulaAudioSpatialMode.ListenerRelative, false)
            });

            Assert.IsTrue(catalog.TryGet("audio.test", out NebulaAudioEntry entry));
            Assert.AreSame(clip, entry.Clip);
            Assert.IsFalse(catalog.TryGet("audio.missing", out _));
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(clip);
        }

        [Test]
        public void Service_MissingCatalogOrEventNeverThrowsOrCreatesLoops()
        {
            var go = new GameObject("Audio Test");
            var service = go.AddComponent<NebulaAudioService>();
            service.Initialize(null);
            Assert.IsFalse(service.PlayOneShot("audio.missing"));
            Assert.IsFalse(service.StartLoop("owner", "audio.missing"));
            Assert.AreEqual(0, service.ActiveLoopCount);
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void Service_KeyedLoopIsIdempotentAndReplacementDoesNotDuplicate()
        {
            var catalog = ScriptableObject.CreateInstance<NebulaAudioCatalog>();
            var first = AudioClip.Create("first", 480, 1, 48000, false);
            var second = AudioClip.Create("second", 480, 1, 48000, false);
            catalog.ReplaceEntries(new List<NebulaAudioEntry>
            {
                new NebulaAudioEntry("audio.loop.a", first, NebulaAudioBus.Ambience,
                    NebulaAudioSpatialMode.World3D, true),
                new NebulaAudioEntry("audio.loop.b", second, NebulaAudioBus.Ambience,
                    NebulaAudioSpatialMode.World3D, true)
            });
            var go = new GameObject("Audio Test");
            var service = go.AddComponent<NebulaAudioService>();
            service.Initialize(catalog);

            Assert.IsTrue(service.StartLoop("world", "audio.loop.a"));
            Assert.IsTrue(service.StartLoop("world", "audio.loop.a"));
            Assert.AreEqual(1, service.ActiveLoopCount);
            Assert.IsTrue(service.StartLoop("world", "audio.loop.b"));
            Assert.AreEqual(1, service.ActiveLoopCount);
            Assert.IsTrue(service.IsLoopPlaying("world", "audio.loop.b"));
            service.StopLoop("world");
            Assert.AreEqual(0, service.ActiveLoopCount);

            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }

        [Test]
        public void Service_UsesBoundedPoolAndClampsBusVolumes()
        {
            var go = new GameObject("Audio Test");
            var service = go.AddComponent<NebulaAudioService>();
            service.Initialize(null);
            Assert.AreEqual(NebulaAudioService.OneShotPoolSize,
                go.GetComponentsInChildren<AudioSource>(true).Length);
            service.SetMasterVolume(2f);
            service.SetBusVolume(NebulaAudioBus.Combat, -1f);
            Assert.AreEqual(0f, service.GetBusVolume(NebulaAudioBus.Combat));
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void BuiltCatalog_Has292UniqueLazyPathsAndNoSerializedClipDependencies()
        {
            NebulaAudioCatalog catalog = RequireBuiltCatalog();
            Assert.AreEqual(292, catalog.Entries.Count);

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (NebulaAudioEntry entry in catalog.Entries)
            {
                Assert.IsTrue(ids.Add(entry.EventId), entry.EventId);
                Assert.IsTrue(paths.Add(entry.ResourcePath), entry.ResourcePath);
                string clipPath = "Assets/Resources/" + entry.ResourcePath + ".wav";
                Assert.NotNull(AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath), entry.EventId);
            }

            string yaml = File.ReadAllText(Path.Combine(ProjectRoot, CatalogPath));
            StringAssert.DoesNotContain("\n    clip:", yaml,
                "The runtime catalog must keep lazy paths, not 292 eager AudioClip references.");
        }

        [Test]
        public void GameplayBridge_EveryLiteralEventIdExistsInBuiltCatalog()
        {
            NebulaAudioCatalog catalog = RequireBuiltCatalog();
            HashSet<string> referenced = EventIdsReferencedIn(
                "Assets/Scripts/Nebula/Audio", "NebulaAudioGameplayBridge*.cs");
            Assert.Greater(referenced.Count, 100);
            foreach (string eventId in referenced)
            {
                if (eventId.EndsWith(".", StringComparison.Ordinal)) continue;
                Assert.IsTrue(catalog.TryGet(eventId, out _),
                    "Gameplay bridge references an event missing from the approved catalog: "
                    + eventId);
            }
            foreach (string layer in new[] { "shield", "armor", "hull" })
            {
                foreach (string suffix in new[] { "break", "heavy", "light.01", "light.02" })
                    Assert.IsTrue(catalog.TryGet(
                        $"audio.damage.{layer}.{suffix}", out _),
                        $"Missing dynamically composed damage event: audio.damage.{layer}.{suffix}");
            }
        }

        [Test]
        public void ProducedCatalog_All292EventsHaveRuntimeTriggerCoverage()
        {
            NebulaAudioCatalog catalog = RequireBuiltCatalog();
            HashSet<string> referenced =
                EventIdsReferencedIn("Assets/Scripts/Nebula", "*.cs");

            foreach (string layer in new[] { "shield", "armor", "hull" })
                foreach (string suffix in new[] { "break", "heavy", "light.01", "light.02" })
                    referenced.Add($"audio.damage.{layer}.{suffix}");
            foreach (string role in new[]
                     { "probe", "fighter", "looter", "disruptor", "heavy", "siege" })
                referenced.Add($"audio.enemy.{role}.engine.loop");
            foreach (string weight in new[] { "light", "heavy" })
                foreach (string variant in new[] { "01", "02" })
                    referenced.Add($"audio.ship.collision_{weight}.{variant}");
            foreach (string variant in new[] { "01", "02" })
                referenced.Add($"audio.world.debris.light.{variant}");

            foreach (NebulaAudioEntry entry in catalog.Entries)
                Assert.IsTrue(referenced.Contains(entry.EventId),
                    "Produced audio is cataloged but has no runtime trigger: " + entry.EventId);
        }

        [Test]
        public void RemoteCommand_ResultSeamReportsAcceptAndReject()
        {
            var results = new List<RemoteCommandRejectReason>();
            void Handler(RemoteCommandRejectReason result) => results.Add(result);
            RemoteCommandService.CommandCompleted += Handler;
            try
            {
                var delta = new SystemDelta();
                Assert.AreEqual(RemoteCommandRejectReason.None,
                    RemoteCommandService.SetInterventionPolicy(delta,
                        SystemLinkState.Live, Combat.InterventionPolicy.Manual));
                Assert.AreEqual(RemoteCommandRejectReason.NotAllowedRemotely,
                    RemoteCommandService.RejectTeleport());
                CollectionAssert.AreEqual(new[]
                {
                    RemoteCommandRejectReason.None,
                    RemoteCommandRejectReason.NotAllowedRemotely
                }, results);
            }
            finally
            {
                RemoteCommandService.CommandCompleted -= Handler;
            }
        }

        [Test]
        public void ShipCombat_DamageEventReportsRealLayerSeverityAndBreak()
        {
            var go = new GameObject("Ship Combat Audio Seam Test");
            ShipCombat combat = go.AddComponent<ShipCombat>();
            combat.InitializeNewCampaign();
            var events = new List<(ShipDamageLayer layer, bool heavy, bool broke)>();
            combat.DamageApplied += (layer, heavy, broke) =>
                events.Add((layer, heavy, broke));

            combat.ApplyDamage(1f, DamageProfile.Neutral);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(ShipDamageLayer.Shield, events[0].layer);
            Assert.IsFalse(events[0].heavy);
            Assert.IsFalse(events[0].broke);

            // A blow worth twice the shield does not stop at the shield, and since
            // P1-COMBAT-006 it does not pretend to either: it reports the shield
            // breaking *and* the armour it spilled into. The audio seam wants both,
            // because a hit that reaches armour and plays only the shield cue tells the
            // player their armour is untouched while it is being eaten.
            combat.ApplyDamage(combat.State.Durability.ShieldMax * 2f,
                DamageProfile.Neutral);
            Assert.AreEqual(3, events.Count,
                "the cascade reported " + (events.Count - 1) + " layer(s) for a blow that "
                + "went through the shield — every layer it touches has to be announced");
            Assert.AreEqual(ShipDamageLayer.Shield, events[1].layer);
            Assert.IsTrue(events[1].heavy);
            Assert.IsTrue(events[1].broke, "the shield absorbed twice its maximum without breaking");
            Assert.AreEqual(ShipDamageLayer.Armor, events[2].layer,
                "the overflow past a broken shield was not reported as armour");
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
