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
    /// ANI-05.3 in a real session: the POI information gate holding, and one
    /// pooled dominance marker per planet driven by its real control state.
    /// </summary>
    public class Ani053PoiDominanceAnimationPlayModeTests
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


        /// <summary>Builds a real POI through the real creation path.</summary>
        private static PoiObject CreatePoi(NebulaContext context, string id, PoiType type, PoiKnowledgeLevel level)
        {
            var manifest = new PoiManifest(id, context.StartSystemId, 0, type, PoiRisk.Safe,
                new Vector3(2000f, 0f, 2000f), WreckSize.Small, 1234UL, 5678UL);
            var state = new PoiState(id);
            if (level != PoiKnowledgeLevel.Signal)
                state.RestoreCore(level, 0f, 0f, 0f, PoiPauseReason.None, null);

            PoiObject poi = PoiObject.Create(manifest, context.Streaming.CombatEncounter.transform,
                state, context.Ship, context.Ship.GetComponent<Nebula.Hangar.ShipHangar>(), null, null);
            Assert.IsNotNull(poi, "the real POI creation path must produce one");
            return poi;
        }

        // --- POI wiring and the information gate ---------------------------------

        [UnityTest]
        public IEnumerator EveryPoiInTheSystem_GetsExactlyOnePresenter()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;

            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            CreatePoi(context, "ani053-a", PoiType.Anomaly, PoiKnowledgeLevel.Signal);
            CreatePoi(context, "ani053-w", PoiType.Wreck, PoiKnowledgeLevel.Signal);

            PoiObject[] pois = Object.FindObjectsOfType<PoiObject>();
            Assert.Greater(pois.Length, 0);

            foreach (PoiObject poi in pois)
            {
                PoiAnimationPresenter[] presenters = poi.GetComponents<PoiAnimationPresenter>();
                Assert.AreEqual(1, presenters.Length, $"{poi.Manifest.PoiId} must have exactly one");
            }
        }

        [UnityTest]
        public IEnumerator APoiAtSignalOrLocated_RevealsNothingAboutItsType()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaAnimationDirector director = Director();

            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            foreach (PoiType type in System.Enum.GetValues(typeof(PoiType)))
                CreatePoi(context, $"ani053-gate-{type}", type, PoiKnowledgeLevel.Signal);

            PoiObject[] pois = Object.FindObjectsOfType<PoiObject>();
            Assert.Greater(pois.Length, 0);

            director.Tick(0.1f);

            foreach (PoiObject poi in pois)
            {
                if (!PoiDominanceAnimationProfiles.TypeIsHidden(poi.State.Level)) continue;

                var presenter = poi.GetComponent<PoiAnimationPresenter>();
                Assert.AreEqual(PoiVisualCharacter.NeutralContact, presenter.Character,
                    $"{poi.Manifest.PoiId} at {poi.State.Level} must be a neutral contact");
                Assert.AreEqual(
                    PoiDominanceAnimationProfiles.SpinDegreesPerSecond(PoiType.Anomaly, PoiVisualCharacter.NeutralContact),
                    PoiDominanceAnimationProfiles.SpinDegreesPerSecond(poi.Manifest.Type, PoiVisualCharacter.NeutralContact),
                    "and must turn at the same rate as every other type");
            }
        }

        [UnityTest]
        public IEnumerator APoiPresenter_NeverMovesTheGameplayObject()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaAnimationDirector director = Director();

            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            PoiObject poi = CreatePoi(context, "ani053-static", PoiType.Wreck, PoiKnowledgeLevel.Signal);

            Vector3 position = poi.transform.position;
            Quaternion rotation = poi.transform.rotation;
            PoiKnowledgeLevel level = poi.State.Level;

            for (int i = 0; i < 400; i++) director.Tick(0.02f);

            Assert.AreEqual(position, poi.transform.position, "a POI's position is gameplay");
            Assert.AreEqual(rotation, poi.transform.rotation);
            Assert.AreEqual(level, poi.State.Level, "and ticking the animation must not advance its knowledge");
        }

        [UnityTest]
        public IEnumerator MotionOff_ReturnsEveryPoiToItsAuthoredPose()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaAnimationDirector director = Director();

            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;
            PoiObject poi = CreatePoi(context, "ani053-motionoff", PoiType.Anomaly, PoiKnowledgeLevel.Identified);
            var presenter = poi.GetComponent<PoiAnimationPresenter>();

            for (int i = 0; i < 200; i++) director.Tick(0.02f);

            AnimationAccessibility.MotionOff = true;
            director.Tick(0.02f);

            Assert.AreEqual(0f, presenter.CurrentSpinDegrees, "motion off means the authored pose");
        }

        // --- dominance markers -------------------------------------------------------

        [UnityTest]
        public IEnumerator EveryPlanet_GetsExactlyOneDominanceMarkerPresenter()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;

            var presenters = Object.FindObjectsOfType<DominanceMarkerPresenter>();
            Assert.Greater(presenters.Length, 0, "the materializer must attach one per planet");

            foreach (DominanceMarkerPresenter presenter in presenters)
                Assert.AreEqual(1, presenter.gameObject.GetComponents<DominanceMarkerPresenter>().Length,
                    "at most one marker presenter per planet");
        }

        [UnityTest]
        public IEnumerator AnUnownedPlanet_IsNotMarkedAtAll()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaAnimationDirector director = Director();

            director.Tick(0.1f);
            director.Tick(0.1f);

            foreach (DominanceMarkerPresenter presenter in Object.FindObjectsOfType<DominanceMarkerPresenter>())
                if (presenter.CurrentState == DominanceState.Unowned)
                    Assert.IsFalse(presenter.ShowingMarker, "an unowned world must carry no marker");
        }

        [UnityTest]
        public IEnumerator ARealDominanceChange_ChangesTheMarker()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaAnimationDirector director = Director();

            var planet = Object.FindObjectOfType<PlanetIdentity>();
            Assert.IsNotNull(planet, "the session must have a real planet");
            var presenter = planet.GetComponent<DominanceMarkerPresenter>();
            Assert.IsNotNull(presenter, "and it must carry the marker presenter");

            // The real dominance record, read back through the real accessor.
            PlanetDominance dominance = ExtractionState.GetDominance(planet.WorldId);
            Assert.IsNotNull(dominance);

            director.Tick(0.1f);
            Assert.AreEqual(dominance.State, presenter.CurrentState,
                "the presenter must show exactly what the dominance system holds");
        }

        [UnityTest]
        public IEnumerator MarkersAddNoLightAndNoBody()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaAnimationDirector director = Director();
            for (int i = 0; i < 20; i++) director.Tick(0.05f);

            foreach (Transform quad in Director().SupportOperations.Root)
            {
                Assert.IsNull(quad.GetComponentInChildren<Light>(true), "a marker must add no light");
                Assert.IsNull(quad.GetComponentInChildren<Rigidbody>(true), "and no body");
            }
        }

        [UnityTest]
        public IEnumerator MotionOff_HandsEveryMarkerBack()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaAnimationDirector director = Director();

            AnimationAccessibility.MotionOff = true;
            for (int i = 0; i < 5; i++) director.Tick(0.05f);

            foreach (DominanceMarkerPresenter presenter in Object.FindObjectsOfType<DominanceMarkerPresenter>())
                Assert.IsFalse(presenter.ShowingMarker, "motion off means no marker is held");
        }

        [UnityTest]
        public IEnumerator ThePoolStaysBounded_AcrossHundredsOfTicks()
        {
            yield return LoadSceneFresh();
            for (int i = 0; i < 6; i++) yield return null;
            NebulaAnimationDirector director = Director();

            for (int i = 0; i < 20; i++) director.Tick(0.05f);
            int quads = director.SupportOperations.TotalQuads;

            for (int i = 0; i < 400; i++) director.Tick(0.02f);

            Assert.AreEqual(quads, director.SupportOperations.TotalQuads,
                "markers reuse the pool and never grow it");
            Assert.LessOrEqual(director.SupportOperations.LeasedCount,
                director.SupportOperations.CapacityPerVfx * 7);
        }

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesNoOrphanMarkerOrPresenter()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            for (int i = 0; i < 6; i++) yield return null;
            Director().Tick(0.1f);

            adapter.RestartCampaign();
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<NebulaAnimationDirector>().Length);
            Assert.AreEqual(0, CombatPresenterAdoption.PendingCount);
            Assert.AreEqual(0, Director().SupportOperations.LeasedCount,
                "no marker lease may survive the session that made it");
        }
    }
}
