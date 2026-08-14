using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// The session's live streaming director must still be findable through
    /// <see cref="StarSystemRegistry.Streaming"/> after a load.
    /// <para>
    /// This exists because it was not. <c>ExtractionDirector</c>'s load path calls
    /// <c>StarSystemRegistry.Initialize</c>, which nulls <c>Streaming</c> as part of
    /// re-seeding the world, and for a long time the only code that ever put the pointer
    /// back was <c>WorldInstaller</c> — which runs at composition time and never again. So
    /// the first manual load of a session emptied the registry and nothing refilled it.
    /// </para>
    /// <para>
    /// The reason it survived so long is that <b>nothing failed</b>. The director itself is
    /// fine and the load works, because <c>ExtractionDirector</c> holds its own reference.
    /// Only code reading the <i>static</i> was affected, and every one of those readers
    /// treats null as "no system is streamed" and carries on: status and attention queries,
    /// module milestones, the hangar's in-combat guards, the raid scheduler's tick target,
    /// surface structures, and two tutorial chapters. A defect whose entire symptom is a
    /// quiet wrong answer needs a test that asks the question out loud.
    /// </para>
    /// <para>
    /// Asserted against object identity, not just non-null. A registry holding <i>some</i>
    /// director would pass a null check while still pointing at a dead one from a previous
    /// session, which is the same bug wearing a better disguise.
    /// </para>
    /// </summary>
    public class StreamingRegistryAfterLoadPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;

        private readonly Dictionary<string, string> slotBackup = new Dictionary<string, string>();

        private static IEnumerable<string> SlotFiles()
        {
            string main = SaveService.SlotFile(GameplaySlot);
            string dir = Path.GetDirectoryName(main);
            yield return main;
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak1.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak2.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak3.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.tmp.json");
        }

        [SetUp]
        public void SetUp()
        {
            slotBackup.Clear();
            foreach (string f in SlotFiles()) slotBackup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var kv in slotBackup)
            {
                if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                else File.WriteAllText(kv.Key, kv.Value);
            }
        }

        private static IEnumerator LoadSceneFresh()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;
            Object.FindObjectOfType<SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheRegistryStillHoldsTheLiveDirectorAfterASaveLoadRoundTrip()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;

            SystemStreamingDirector booted = StarSystemRegistry.Streaming;
            Assert.IsNotNull(booted, "composition must publish the streaming director");

            context.ExtractionDirector.SaveNow(null);
            context.ExtractionDirector.LoadNow();
            yield return null;

            Assert.IsNotNull(StarSystemRegistry.Streaming,
                "a load emptied the registry: every reader of it now believes nothing is streamed");
            Assert.AreSame(booted, StarSystemRegistry.Streaming,
                "the registry must hold the director the session is actually running, not a stale one");
        }

        /// <summary>
        /// The observable consequence, stated in the vocabulary the callers use: after a
        /// load, the system the player is in still reports as Full. Every one of the eight
        /// call sites this defect reached asks a question shaped like this one, so a
        /// registry that answers correctly here answers correctly for all of them.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCurrentSystemStillReportsAsFullAfterALoad()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;

            string systemId = StarSystemRegistry.Streaming.FullSystemId;
            Assert.IsNotNull(systemId, "the session must boot with a Full system");

            context.ExtractionDirector.SaveNow(null);
            context.ExtractionDirector.LoadNow();
            yield return null;

            Assert.AreEqual(SystemStreamingState.Full,
                StarSystemRegistry.Streaming.GetState(StarSystemRegistry.Streaming.FullSystemId),
                "after a load the player's own system reported as not streamed");
        }

        /// <summary>
        /// Repeated loads keep one director rather than accumulating them. The fix
        /// publishes from <c>BootFull</c>, so a path that rebooted a second director would
        /// quietly swap the registry over — worth pinning, since that would be a new leak
        /// wearing the shape of the fix.
        /// </summary>
        [UnityTest]
        public IEnumerator RepeatedLoadsKeepExactlyOneStreamingDirector()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;

            SystemStreamingDirector first = StarSystemRegistry.Streaming;
            // Pinned before the loop, or the AreSame below passes on null == null — which
            // is precisely the broken state, agreeing with itself.
            Assert.IsNotNull(first, "composition must publish the streaming director");

            for (int i = 0; i < 3; i++)
            {
                context.ExtractionDirector.SaveNow(null);
                context.ExtractionDirector.LoadNow();
                yield return null;
            }

            Assert.AreSame(first, StarSystemRegistry.Streaming, "a load replaced the live director");
            Assert.AreEqual(1, Object.FindObjectsOfType<SystemStreamingDirector>(true).Length,
                "loads accumulated streaming directors");
        }
    }
}
