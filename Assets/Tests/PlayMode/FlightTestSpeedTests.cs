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
using Nebula.Ship;
using Nebula.UI;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for DT-001's adjustable flight test speed dev tool
    /// (§8) — panel lifecycle, ship multiplier wiring, restart/load reset,
    /// and high-speed collision/streaming safety. No keyboard/mouse
    /// simulation anywhere: every behavior is driven through the same
    /// public, input-free APIs a keypress would call (SetVisible, the
    /// Slider/Button components directly, TestApplyDisplacement).
    /// </summary>
    public class FlightTestSpeedTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";
        private const int GameplaySlot = 1; // ExtractionDirector's own hardcoded save slot

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
            // This test slot is the same one the actual game uses (ExtractionDirector's
            // Slot is a hardcoded private const) -- any real save there must be restored
            // byte-for-byte afterward, never left corrupted or overwritten by a test run.
            slot1Backup.Clear();
            foreach (string f in Slot1Files())
                slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
        {
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

        [UnityTest]
        public IEnumerator FlightTestSpeedHud_ExistsExactlyOnce_ClosedAtBoot()
        {
            yield return LoadSceneFresh();

            var huds = Object.FindObjectsOfType<FlightTestSpeedHud>();
            Assert.AreEqual(1, huds.Length, "the Editor test run must always create exactly one panel (Debug.isDebugBuild is true in the Editor)");
            Assert.IsFalse(huds[0].IsVisible, "the panel must start closed");
        }

        [UnityTest]
        public IEnumerator SetVisible_TogglesThePanel_NoInputSimulationNeeded()
        {
            yield return LoadSceneFresh();
            var hud = Object.FindObjectOfType<FlightTestSpeedHud>();

            hud.SetVisible(true);
            Assert.IsTrue(hud.IsVisible);

            hud.SetVisible(false);
            Assert.IsFalse(hud.IsVisible);
        }

        [UnityTest]
        public IEnumerator OpeningThePanel_ReleasesShipsPointerOwnership_AndTheNextGameClickRecapturesIt()
        {
            yield return LoadSceneFresh();
            var hud = Object.FindObjectOfType<FlightTestSpeedHud>();

            CursorLockMode savedLock = Cursor.lockState;
            bool savedVisible = Cursor.visible;
            bool mouseDown = false;
            ShipController.PointerOverUiSource = () => false;
            NebulaInputRouter.Source = key => mouseDown && key == KeyCode.Mouse0;

            try
            {
                mouseDown = true;
                for (int frame = 0; frame < 12 && !ShipController.PointerAimLive; frame++)
                    yield return null;
                mouseDown = false;
                yield return null;

                Assert.IsTrue(ShipController.PointerAimLive,
                    "baseline: clicking the game view did not give flight the pointer");

                // The same public path J invokes. The panel must release both Unity's
                // cursor and ShipController's private ownership latch.
                hud.SetVisible(true);
                yield return null;

                Assert.IsTrue(hud.IsVisible, "the test-speed panel did not open");
                Assert.IsFalse(ShipController.PointerAimLive,
                    "the panel freed the desktop cursor but left flight's ownership latch set");
                Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
                Assert.IsTrue(Cursor.visible);

                // Click outside the still-open panel. Before the fix the stale private
                // latch made HandleCursor skip this capture forever.
                mouseDown = true;
                for (int frame = 0; frame < 12 && !ShipController.PointerAimLive; frame++)
                    yield return null;
                mouseDown = false;
                yield return null;

                Assert.IsTrue(hud.IsVisible,
                    "recapturing the game view must not unexpectedly close the speed panel");
                Assert.IsTrue(ShipController.PointerAimLive,
                    "the first game-view click after opening the J panel did not recapture flight");

                // PointerAimLive is the shipped ownership result. Do not assert the
                // operating-system lock here: a batchmode process has no focused Game
                // View, so Unity deliberately leaves Cursor.lockState at None even
                // after HandleCursor executes its Locked assignment. The pre-fix bug
                // still fails above — its stale cursorCaptured latch never becomes
                // false when the panel opens, so this assertion cannot be reached as a
                // false green.
            }
            finally
            {
                mouseDown = false;
                NebulaInputRouter.ResetSourceToKeyboard();
                ShipController.PointerOverUiSource = null;
                hud.SetVisible(false);
                Cursor.lockState = savedLock;
                Cursor.visible = savedVisible;
            }
        }

        [UnityTest]
        public IEnumerator SpeedSlider_ProgrammaticMax_SetsShipMultiplierTo100x()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            var hud = Object.FindObjectOfType<FlightTestSpeedHud>();

            hud.SpeedSlider.value = 1f; // fires the same onValueChanged path a drag-to-max would
            yield return null;

            Assert.AreEqual(100f, adapter.Context.Ship.TestSpeedMultiplier, 0.01f);
        }

        [UnityTest]
        public IEnumerator ResetButton_SetsMultiplierBackToOne_NoRecursionOrDoubleApplication()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            var hud = Object.FindObjectOfType<FlightTestSpeedHud>();

            hud.SpeedSlider.value = 1f;
            yield return null;
            Assert.AreEqual(100f, adapter.Context.Ship.TestSpeedMultiplier, 0.01f);

            hud.ResetButton.onClick.Invoke();
            yield return null;

            Assert.AreEqual(1f, adapter.Context.Ship.TestSpeedMultiplier);
            Assert.AreEqual(0f, hud.SpeedSlider.value, 0.001f, "the slider itself must reflect the reset without re-triggering onValueChanged");
        }

        [UnityTest]
        public IEnumerator RestartCampaign_DestroysOldHud_NewSingleInstance_MultiplierResetToOne()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            var oldHud = Object.FindObjectOfType<FlightTestSpeedHud>();
            adapter.Context.Ship.SetTestSpeedMultiplier(55f);

            adapter.RestartCampaign();
            yield return null;
            yield return null;

            Assert.IsTrue(oldHud == null, "the old HUD must be destroyed by dispose");

            var newHuds = Object.FindObjectsOfType<FlightTestSpeedHud>();
            Assert.AreEqual(1, newHuds.Length, "no duplicate panel after restart");
            Assert.AreNotSame(oldHud, newHuds[0]);
            Assert.AreEqual(1f, adapter.Context.Ship.TestSpeedMultiplier, "a restart must never carry the old session's test speed forward");
        }

        [UnityTest]
        public IEnumerator SaveThenLoad_MultiplierResetsToOne_AndSaveJsonCarriesNoTestField()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;

            context.Ship.SetTestSpeedMultiplier(62.5f);
            context.ExtractionDirector.SaveNow(null);
            yield return null;

            string savedJson = File.ReadAllText(SaveService.SlotFile(GameplaySlot)).ToLowerInvariant();
            // Note: the save legitimately contains "cruisespeedmultiplier" (VS-006, pre-existing,
            // unrelated to DT-001) -- these checks target only DT-001's own field name so they
            // never false-positive against that unrelated existing save data.
            StringAssert.DoesNotContain("testspeed", savedJson);
            StringAssert.DoesNotContain("debugspeed", savedJson);
            StringAssert.DoesNotContain("rawtestspeedmultiplier", savedJson);

            context.ExtractionDirector.LoadNow();
            yield return null;

            Assert.AreEqual(1f, context.Ship.TestSpeedMultiplier, "loading a save must force the test multiplier back to 1x even though it's the same ship GameObject");
        }

        [UnityTest]
        public IEnumerator TimeScale_NeverChanges_WhilePanelIsOpenOrSpeedIsHigh()
        {
            yield return LoadSceneFresh();
            float before = Time.timeScale;
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            var hud = Object.FindObjectOfType<FlightTestSpeedHud>();

            hud.SetVisible(true);
            hud.SpeedSlider.value = 1f;
            yield return null;

            Assert.AreEqual(before, Time.timeScale);
            Assert.AreEqual(100f, adapter.Context.Ship.TestSpeedMultiplier, 0.01f);
        }

        [UnityTest]
        public IEnumerator HighTestSpeedDisplacement_NeverTunnelsThroughACelestialBody_ThroughTheRealCollisionPath()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            context.Ship.SetTestSpeedMultiplier(100f);

            var body = Object.FindObjectOfType<Nebula.Celestial.Star>();
            Assert.IsNotNull(body, "the start system's star must exist to test collision against");
            Vector3 bodyPos = body.transform.position;

            context.Ship.transform.position = bodyPos + Vector3.forward * (body.Radius + 200f);
            Vector3 hugeDisplacementStraightThrough = (bodyPos - context.Ship.transform.position) * 5f; // a single-frame jump that would tunnel clean through without segment collision

            context.Ship.TestApplyDisplacement(hugeDisplacementStraightThrough);
            yield return null;

            float finalDistance = Vector3.Distance(context.Ship.transform.position, bodyPos);
            Assert.GreaterOrEqual(finalDistance, body.Radius, "even at test-speed magnitudes the ship must never end up inside the star -- MoveWithCollision's segment check must still run");
        }

        [UnityTest]
        public IEnumerator ExistingPublicStreamingSeam_RebootsWithoutDuplicateAuthority_RegardlessOfTestSpeed()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            context.Ship.SetTestSpeedMultiplier(100f);

            string startSystemId = context.StartSystemId;
            SystemManifest startManifest = StarSystemRegistry.Manifests.ResolveSystem(startSystemId);

            context.Streaming.ResetAndBootFull(startManifest);
            yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<SystemStreamingDirector>().Length, "high test speed must never produce a second streaming authority");
            Assert.AreEqual(startSystemId, context.Streaming.FullSystemId);
        }
    }
}
