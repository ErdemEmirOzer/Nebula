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
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// ANI-05.3 in a real session: stars and the black hole turning without
    /// moving, lighting's property values surviving it, and the asteroid field
    /// keeping its single writer.
    /// </summary>
    public class Ani053CelestialRuntimeAnimationPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1;
        private readonly Dictionary<string, string> slot1Backup = new Dictionary<string, string>();

        private static IEnumerable<string> Slot1Files()
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
        public void BackupRealGameplaySlot()
        {
            slot1Backup.Clear();
            foreach (string f in Slot1Files()) slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
        {
            AnimationAccessibility.ResetToDefaults();
            LightingAccessibility.ResetToDefaults();
            foreach (var kv in slot1Backup)
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

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it -- explicitly, through the same idempotent
            // entry the menu\'s New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        private static NebulaAnimationDirector Director()
        {
            var d = Object.FindObjectOfType<NebulaAnimationDirector>();
            Assert.IsNotNull(d);
            return d;
        }

        private static CelestialAnimationPresenter StarPresenter()
        {
            var star = Object.FindObjectOfType<Star>();
            Assert.IsNotNull(star, "the session must have materialized a star");
            var presenter = star.GetComponent<CelestialAnimationPresenter>();
            Assert.IsNotNull(presenter, "ANI-05.3 must attach the presenter at the real star seam");
            return presenter;
        }

        // --- stars ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheSystemStar_GetsItsPresenterWithARealProfile()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;

            CelestialAnimationPresenter presenter = StarPresenter();
            Assert.IsFalse(presenter.Motion.IsStill, "a star must have a real character");
            Assert.Greater(presenter.Motion.SecondaryDegreesPerSecond, 0f, "and a drifting corona");
        }

        [UnityTest]
        public IEnumerator AStarTurns_ButItsGameplayBodyNeverMoves()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            CelestialAnimationPresenter presenter = StarPresenter();
            var body = presenter.GetComponent<CelestialBody>();

            Vector3 position = presenter.transform.position;
            Quaternion rotation = presenter.transform.rotation;
            float radius = body != null ? body.Radius : 0f;
            float mass = body != null ? body.Mass : 0f;

            for (int i = 0; i < 400; i++) director.Tick(0.02f);

            Assert.AreEqual(position, presenter.transform.position, "a star's position is the sector system's");
            Assert.AreEqual(rotation, presenter.transform.rotation, "and so is its gameplay rotation");
            if (body != null)
            {
                Assert.AreEqual(radius, body.Radius, "and its radius");
                Assert.AreEqual(mass, body.Mass, "and its mass");
            }
        }

        [UnityTest]
        public IEnumerator OnlyTheMaterializedStarGetsAPresenter_NotTheGalaxyField()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;

            // The decorative galaxy field is built from the same Star component,
            // so "one presenter per Star" would mean 129 participants for
            // scenery nobody flies to. Exactly one — the system's own star —
            // is the whole point.
            int stars = Object.FindObjectsOfType<Star>().Length;
            int starPresenters = 0;
            foreach (CelestialAnimationPresenter presenter in Object.FindObjectsOfType<CelestialAnimationPresenter>())
                if (presenter.GetComponent<Star>() != null) starPresenters++;

            Assert.AreEqual(1, starPresenters,
                "only the materialized system star is animated");
            Assert.Greater(stars, starPresenters,
                "and the decorative field is left alone");
        }

        // --- lighting's channels survive -------------------------------------------

        [UnityTest]
        public IEnumerator HundredsOfTicks_LeaveLightingsPropertyValuesBitIdentical()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 4; i++) yield return null;
            NebulaAnimationDirector director = Director();

            CelestialAnimationPresenter flowing = null;
            foreach (CelestialAnimationPresenter presenter in Object.FindObjectsOfType<CelestialAnimationPresenter>())
                if (presenter.ScrollingRendererCount > 0) { flowing = presenter; break; }
            if (flowing == null) Assert.Ignore("this system materialized no surface-flow body");

            Renderer renderer = flowing.GetComponentInChildren<Renderer>(true);
            Assert.IsNotNull(renderer);

            var before = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(before);
            Color emission = before.GetColor(MaterialPropertyChannels.EmissionColorId);
            float metallic = before.GetFloat(MaterialPropertyChannels.MetallicId);
            float smoothness = before.GetFloat(MaterialPropertyChannels.SmoothnessId);
            Material shared = renderer.sharedMaterial;

            for (int i = 0; i < 400; i++) director.Tick(0.02f);

            var after = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(after);
            Assert.AreEqual(emission, after.GetColor(MaterialPropertyChannels.EmissionColorId));
            Assert.AreEqual(metallic, after.GetFloat(MaterialPropertyChannels.MetallicId), 1e-5f);
            Assert.AreEqual(smoothness, after.GetFloat(MaterialPropertyChannels.SmoothnessId), 1e-5f);
            Assert.AreSame(shared, renderer.sharedMaterial, "and no runtime material copy was made");
        }

        // --- asteroids -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheAsteroidFieldKeepsItsSingleWriter()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;

            int asteroids = Object.FindObjectsOfType<Asteroid>().Length;
            if (asteroids == 0) Assert.Ignore("this system materialized no asteroid field");

            foreach (Asteroid rock in Object.FindObjectsOfType<Asteroid>())
                Assert.IsNull(rock.GetComponent<CelestialAnimationPresenter>(),
                    "AsteroidDriftSystem is the only writer; no per-rock presenter may exist");
        }

        [UnityTest]
        public IEnumerator TheAsteroidFieldAddsNoParticipantsAndNoLights()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaAnimationDirector director = Director();

            int asteroids = Object.FindObjectsOfType<Asteroid>().Length;
            int participants = director.ParticipantCount;

            for (int i = 0; i < 200; i++) director.Tick(0.02f);

            Assert.AreEqual(participants, director.ParticipantCount,
                $"{asteroids} rocks must not add participants");
            foreach (Asteroid rock in Object.FindObjectsOfType<Asteroid>())
                Assert.IsNull(rock.GetComponentInChildren<Light>(true), "and no rock may add a light");
        }

        [UnityTest]
        public IEnumerator MotionOff_StopsTheTumbleWithoutChangingTheDrift()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;

            var rock = Object.FindObjectOfType<Asteroid>();
            if (rock == null) Assert.Ignore("this system materialized no asteroid field");

            AnimationAccessibility.MotionOff = true;
            Quaternion rotationBefore = rock.transform.rotation;
            Vector3 positionBefore = rock.transform.position;

            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(rotationBefore, rock.transform.rotation,
                "the decorative tumble stops with motion off");
            Assert.AreNotEqual(positionBefore, rock.transform.position,
                "but the rock's real drift carries on");
        }

        // --- session hygiene ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesOneDirectorAndNoOrphans()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            for (int i = 0; i < 4; i++) yield return null;

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount);
            Assert.AreEqual(0, Director().SupportOperations.LeasedCount,
                "the new session starts with every marker slot free");
        }
    }
}
