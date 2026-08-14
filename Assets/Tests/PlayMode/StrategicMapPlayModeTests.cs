using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.UI.StrategicMap;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Strategic map M7: the map inside a real booted session — key ownership, the
    /// input gate, the universe → system → planet-card journey, real assets actually
    /// resolving, dominance effects, single-screen/single-subscription across restart,
    /// selection continuity across streaming, and open/refresh cost at scale.
    /// </summary>
    public class StrategicMapPlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            InputGate.Suppressed = false;
            StrategicMapTheme.ReducedMotion = false;
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

        private static StrategicMapScreen Screen() => Object.FindObjectOfType<StrategicMapScreen>();
        private static NebulaContext Context() => Object.FindObjectOfType<SolarSystemBootstrap>().Context;

        // ==================== composition, key ownership, input gate ====================

        [UnityTest]
        public IEnumerator TheMapIsComposedExactlyOnce_AndStartsClosed()
        {
            yield return LoadSceneFresh();

            StrategicMapScreen[] screens = Object.FindObjectsOfType<StrategicMapScreen>();
            Assert.AreEqual(1, screens.Length, "composition must wire exactly one strategic map screen");
            Assert.IsFalse(screens[0].IsOpen, "the map must start closed");
        }

        [UnityTest]
        public IEnumerator TheOlderNavigationScreen_HasYieldedTheMapKey_SoMHasASingleOwner()
        {
            yield return LoadSceneFresh();

            // The fallback navigation screen that used to share M has been deleted,
            // so the only way the key can gain a second owner now is the parent
            // menu binding it -- which it briefly did, and which would have made
            // one press both open the map and open the menu on its map page.
            Assert.IsNotNull(Screen(), "the strategic map must exist to own the key");
            Assert.AreNotEqual(KeyCode.M, UI.Menu.NebulaParentMenuScreen.ProgressionKey);
            Assert.AreNotEqual(KeyCode.M, UI.Menu.NebulaParentMenuScreen.FleetKey);
        }

        /// <summary>
        /// UI-03: the menu hands the map over without changing anything about it.
        /// The shell holds ExclusiveModal and freezes the game while a screen is
        /// open; the map refuses to open over another modal owner and deliberately
        /// keeps the simulation running. So the handoff has to close the menu
        /// completely first. Getting the order wrong would either be refused
        /// outright or leave the map running over a frozen world.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMenusMapPage_HandsOverWithoutFreezingTheWorldOrHoldingTheModal()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            StrategicMapScreen map = Screen();

            var menu = context.Presentation.ParentMenu;
            var page = menu.PageOf<UI.Menu.NebulaMapPage>();
            Assert.IsNotNull(page, "the rail must carry a map entry");

            menu.Open(UI.Design.NebulaUiAssetCatalog.Page.Map);
            yield return null;
            Assert.IsTrue(menu.IsOpen, "the menu owns the screen at this point");

            Assert.IsTrue(page.OpenMap(), "the handoff must succeed");
            yield return null;

            Assert.IsFalse(menu.IsOpen, "the menu closes; the two are never both up");
            Assert.IsTrue(map.IsOpen, "and the map is the surface now");
            Assert.AreSame(map, ExclusiveModal.Owner,
                "the map owns the screen, not the shell that handed it over");
            Assert.AreNotEqual(0f, Time.timeScale,
                "the map keeps the simulation running");
            Assert.IsTrue(InputGate.Suppressed,
                "the handoff dropped the input gate: the shell's PopAll must not land after "
                + "the map has raised it, or the ship flies while the map is up");

            map.SetOpen(false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpeningTheMap_GatesShipInput_ButNeverFreezesTheSimulation()
        {
            yield return LoadSceneFresh();
            StrategicMapScreen map = Screen();

            float timeScaleBefore = Time.timeScale;
            Assert.IsFalse(InputGate.Suppressed);

            map.SetOpen(true);
            yield return null;

            Assert.IsTrue(map.IsOpen);
            Assert.IsTrue(InputGate.Suppressed, "ship/world/combat input must be gated while the map is open");
            Assert.AreEqual(timeScaleBefore, Time.timeScale, "the map must never freeze the simulation");
            Assert.IsTrue(Cursor.visible, "the cursor must be usable while the map is open");

            map.SetOpen(false);
            yield return null;

            Assert.IsFalse(map.IsOpen);
            Assert.IsFalse(InputGate.Suppressed, "closing must restore the input gate exactly");
            Assert.AreEqual(timeScaleBefore, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator TheMapTakesTheExclusiveModal_SoASecondModalCannotOpenOverIt()
        {
            yield return LoadSceneFresh();
            StrategicMapScreen map = Screen();

            map.SetOpen(true);
            yield return null;
            Assert.IsTrue(ExclusiveModal.IsHeldByOther(new object()), "the map must hold the exclusive modal while open");

            map.SetOpen(false);
            yield return null;
            Assert.IsFalse(ExclusiveModal.IsHeldByOther(new object()), "closing must release it");
        }

        [UnityTest]
        public IEnumerator TheMapKeyItself_OpensAndClosesTheMap_AndActuallyShowsItOnScreen()
        {
            yield return LoadSceneFresh();
            StrategicMapScreen map = Screen();
            Assert.IsFalse(map.IsOpen, "starts closed");

            // Exactly what pressing M runs.
            Assert.IsTrue(map.ToggleFromMapKey(), "the map key must open the map");
            yield return null;

            Assert.IsTrue(map.IsOpen);

            // It must genuinely be on screen: an active root under an enabled, drawing canvas.
            var canvas = map.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas, "the map must own a canvas");
            Assert.IsTrue(canvas.gameObject.activeInHierarchy, "the canvas must actually be active in the hierarchy");
            Assert.IsTrue(canvas.enabled, "the canvas must be enabled");
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode, "an overlay canvas needs no camera to be visible");

            var graphics = map.GetComponentsInChildren<UnityEngine.UI.Graphic>(false);
            Assert.Greater(graphics.Length, 0, "the open map must have visible graphics, not an empty canvas");

            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.IsNotNull(scaler, "the map needs a scaler so it lays out at any resolution");

            // And pressing it again closes it.
            Assert.IsTrue(map.ToggleFromMapKey(), "the map key must close the map again");
            yield return null;
            Assert.IsFalse(map.IsOpen);
            Assert.IsFalse(InputGate.Suppressed, "closing restores ship input");
        }

        [UnityTest]
        public IEnumerator TheMapKeyIsIgnored_WhileAnotherModalOwnsTheScreen()
        {
            yield return LoadSceneFresh();
            StrategicMapScreen map = Screen();

            var otherModal = new object();
            Assert.IsTrue(ExclusiveModal.TryAcquire(otherModal), "another modal takes the screen first");
            try
            {
                Assert.IsFalse(map.ToggleFromMapKey(), "the map must not open over another modal");
                Assert.IsFalse(map.IsOpen);
            }
            finally { ExclusiveModal.Release(otherModal); }

            Assert.IsTrue(map.ToggleFromMapKey(), "and it opens normally once the screen is free");
            Assert.IsTrue(map.IsOpen);
            map.SetOpen(false);
        }

        // ==================== universe -> system -> planet card ====================

        [UnityTest]
        public IEnumerator TheFullJourney_UniverseToSystemToPlanetCard_WorksOnRealBootedData()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            StrategicMapScreen map = Screen();

            // The start system is genuinely visited at boot, and its first planet is scanned
            // so the card has earned surface detail to show.
            string startSystemId = context.StartSystemId;
            PlanetManifest planetManifest = context.StartSystem.Planets[0];
            ExtractionState.GetProfile(planetManifest.PlanetId).Scanned = true;

            map.SetOpen(true);
            yield return null;
            map.Refresh();

            // The map opens on the system the player is standing in, not on the universe
            // overview (DEC-172). Stepping back is what takes them out to the wider view.
            Assert.AreEqual(StrategicMapLevel.System, map.Presenter.Level);
            Assert.AreEqual(startSystemId, map.Presenter.SelectedSystemId);
            Assert.IsTrue(map.Presenter.StepBack(), "backing out of the opening system stays inside the map");

            Assert.IsNotNull(map.Snapshot);
            Assert.IsTrue(map.Snapshot.TryGetSystem(startSystemId, out StrategicSystemView start),
                "the booted start system must be in the snapshot");
            Assert.IsTrue(start.DetailsKnown, "the start system is visited, so its details are earned");

            map.Presenter.EnterSystem(startSystemId);
            Assert.AreEqual(StrategicMapLevel.System, map.Presenter.Level);

            map.Presenter.SelectPlanet(planetManifest.PlanetId);
            yield return null;
            Assert.AreEqual(StrategicMapLevel.Planet, map.Presenter.Level);
            Assert.IsTrue(map.Presenter.CardOpen);

            map.Refresh();
            Assert.IsTrue(map.Snapshot.TryGetSystem(startSystemId, out StrategicSystemView again));
            StrategicPlanetView planet = FindPlanet(again, planetManifest.PlanetId);
            Assert.IsTrue(planet.Scanned);
            Assert.AreEqual(12, planet.Slots.Count, "a scanned planet must expose all twelve canonical slots on its card");

            // Escape walks back down the hierarchy one step at a time. Opening on the
            // player's own system records the galaxy it belongs to, so backing out of the
            // system lands in that galaxy before reaching the universe.
            Assert.IsTrue(map.Presenter.StepBack());
            Assert.AreEqual(StrategicMapLevel.System, map.Presenter.Level);
            Assert.IsTrue(map.Presenter.StepBack());
            Assert.AreEqual(StrategicMapLevel.Galaxy, map.Presenter.Level);
            Assert.IsTrue(map.Presenter.StepBack());
            Assert.AreEqual(StrategicMapLevel.Universe, map.Presenter.Level);
            Assert.IsFalse(map.Presenter.StepBack(), "at the top the map closes instead");

            map.SetOpen(false);
        }

        // ==================== real assets ====================

        [UnityTest]
        public IEnumerator TheMapResolvesRealShippedAssets_NotPlaceholders()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();

            // The real star texture for the actual booted start system.
            Sprite star = StrategicMapVisualResolver.StarSurface(context.StartSystem.StarClass);
            Assert.IsNotNull(star, "the booted system's own star must resolve to its real produced texture");
            Assert.IsNotNull(star.texture);
            Assert.Greater(star.texture.width, 8, "a real star texture, not a 1px stand-in");

            // A real planet prefab preview, rendered from the actual shipped prefab.
            PlanetManifest pm = context.StartSystem.Planets[0];
            PlanetArchetype archetype = PlanetArchetypeCatalog.ForFamily(pm.Family);
            Assert.IsTrue(Visuals.NebulaVisualCatalog.TryGetPlanet(archetype, out Visuals.NebulaVisualDefinition planetDef),
                $"{archetype} must map to a real catalog entry");
            Assert.IsNotNull(Visuals.NebulaVisualResolver.Load(planetDef.ResourcePath),
                $"{archetype} must resolve to a real shipped prefab, never a placeholder");

            Sprite planet = StrategicMapVisualResolver.PlanetThumbnail(archetype);
            yield return null;
            bool hasGpu = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            if (hasGpu) Assert.IsNotNull(planet, $"{archetype} must render a preview from its real prefab");
            else Assert.IsNull(planet, "with no graphics device the preview must degrade to the neutral fallback, never crash");

            // Rendering a preview must never leave a stray gameplay object behind.
            Assert.IsNull(GameObject.Find("StrategicMapPreview (temporary)"), "a preview stage must not survive");
            Assert.IsNull(GameObject.Find("StrategicMapPreviewCamera"), "a preview camera must not survive");
        }

        [UnityTest]
        public IEnumerator DominanceChanges_AreReflectedInTheSnapshotTheMapDraws()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            StrategicMapScreen map = Screen();

            PlanetManifest pm = context.StartSystem.Planets[0];
            ExtractionState.GetProfile(pm.PlanetId).Scanned = true;

            map.SetOpen(true);
            map.Refresh();
            map.Snapshot.TryGetSystem(context.StartSystemId, out StrategicSystemView before);
            Assert.AreEqual(DominanceState.Unowned, FindPlanet(before, pm.PlanetId).Dominance);

            ExtractionState.GetDominance(pm.PlanetId).Restore(DominanceState.Controlled, 0f, 0f);
            map.Refresh();
            map.Snapshot.TryGetSystem(context.StartSystemId, out StrategicSystemView controlled);
            Assert.AreEqual(DominanceState.Controlled, FindPlanet(controlled, pm.PlanetId).Dominance,
                "a real Controlled transition must show up on the map");

            ExtractionState.GetDominance(pm.PlanetId).Restore(DominanceState.Contested, 0f, 0f);
            map.Refresh();
            map.Snapshot.TryGetSystem(context.StartSystemId, out StrategicSystemView contested);
            Assert.AreEqual(DominanceState.Contested, FindPlanet(contested, pm.PlanetId).Dominance);

            // Each state has its own real icon, so the change is never colour-only.
            Assert.IsNotNull(StrategicMapVisualResolver.Dominance(DominanceState.Controlled));
            Assert.IsNotNull(StrategicMapVisualResolver.Dominance(DominanceState.Contested));

            map.SetOpen(false);
        }

        // ==================== restart / save-load ====================

        [UnityTest]
        public IEnumerator RestartCampaign_LeavesExactlyOneMapScreen_AndNoStuckInputGate()
        {
            yield return LoadSceneFresh();
            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();

            StrategicMapScreen before = Screen();
            before.SetOpen(true);
            yield return null;
            Assert.IsTrue(InputGate.Suppressed);

            // Restart while the map is still open -- the teardown safety net must fire.
            adapter.RestartCampaign();
            yield return null;
            yield return null;
            yield return null;

            StrategicMapScreen[] after = Object.FindObjectsOfType<StrategicMapScreen>();
            Assert.AreEqual(1, after.Length, "a restart must leave exactly one map screen, never two");
            Assert.AreNotSame(before, after[0], "the restart must have produced a genuinely new screen");
            Assert.IsFalse(after[0].IsOpen, "the fresh screen starts closed");
            Assert.IsFalse(InputGate.Suppressed, "a screen destroyed while open must not leave the input gate stuck");
            Assert.IsFalse(ExclusiveModal.IsHeldByOther(new object()), "nor the exclusive modal");
        }

        [UnityTest]
        public IEnumerator ARealSaveAndLoad_LeavesTheMapUsableAndStillSingle()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            StrategicMapScreen map = Screen();

            context.ExtractionDirector.SaveNow(null);
            yield return null;
            context.ExtractionDirector.LoadNow();
            yield return null;
            yield return null;

            Assert.AreEqual(1, Object.FindObjectsOfType<StrategicMapScreen>().Length, "a load must not duplicate the map");

            map.SetOpen(true);
            map.Refresh();
            Assert.IsNotNull(map.Snapshot);
            Assert.Greater(map.Snapshot.Systems.Count, 0, "the map must still have real data after a load");
            map.SetOpen(false);
        }

        // ==================== streaming continuity ====================

        /// <summary>
        /// Any system that is not the start one, from the start sector if it has a
        /// second, otherwise from a neighbouring sector. Returns null only if the
        /// surrounding space genuinely holds nothing else, which is worth failing on
        /// loudly rather than dereferencing.
        /// </summary>
        private static SystemManifest FindNeighbourOf(string startSystemId, SectorCoordinate sector)
        {
            SectorManifest own = StarSystemRegistry.Manifests.Resolve(sector);
            foreach (SystemManifest candidate in own.Systems)
                if (candidate.SystemId != startSystemId) return candidate;

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                SectorManifest near = StarSystemRegistry.Manifests.Resolve(
                    new SectorCoordinate(sector.X + dx, sector.Y + dy, sector.Z));
                foreach (SystemManifest candidate in near.Systems)
                    if (candidate.SystemId != startSystemId) return candidate;
            }
            return null;
        }

        [UnityTest]
        public IEnumerator SelectionAndKnowledgeSurviveASystemDemotionAndPromotion()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            StrategicMapScreen map = Screen();
            string startSystemId = context.StartSystemId;
            SystemManifest startManifest = StarSystemRegistry.Manifests.ResolveSystem(startSystemId);

            map.SetOpen(true);
            map.Refresh();
            map.Presenter.EnterSystem(startSystemId);
            Assert.AreEqual(startSystemId, map.Presenter.SelectedSystemId);

            // Physically leave the system, then come back -- the real streaming path.
            SystemDelta demoted = context.Streaming.CaptureCurrentFullDelta();
            if (demoted != null) StarSystemRegistry.Deltas.Set(demoted);
            WorldIdCodec.TryParseSystemId(startSystemId, out SectorCoordinate sector, out _);

            // Not index 1. How many systems a sector holds is however many stars
            // GalaxyStarField placed there for this seed, so "the second one"
            // is an assumption about world generation rather than about the map --
            // and when the start sector held exactly one, ResolveSystem returned
            // null and the next line dereferenced it. Ask the sector what it has.
            SystemManifest neighbor = FindNeighbourOf(startSystemId, sector);
            Assert.IsNotNull(neighbor,
                "the test needs a second system to fly to; no sector near the start had one");
            context.Ship.transform.position = neighbor.AbsoluteCenter - FloatingOrigin.TotalOffset;
            context.Streaming.ResetAndBootFull(neighbor);
            yield return null;

            map.Refresh();
            Assert.AreEqual(startSystemId, map.Presenter.SelectedSystemId,
                "a demoted-but-still-known system must stay selected");
            Assert.IsTrue(map.Snapshot.TryGetSystem(startSystemId, out StrategicSystemView away));
            Assert.IsTrue(away.DetailsKnown, "knowledge earned by visiting must survive demotion");

            context.Ship.transform.position = startManifest.AbsoluteCenter - FloatingOrigin.TotalOffset;
            context.Streaming.ResetAndBootFull(startManifest);
            yield return null;

            map.Refresh();
            Assert.AreEqual(startSystemId, map.Presenter.SelectedSystemId, "selection must survive promotion back");
            Assert.IsTrue(map.Snapshot.TryGetSystem(startSystemId, out StrategicSystemView back));
            Assert.IsTrue(back.DetailsKnown);
            map.SetOpen(false);
        }

        // ==================== scale / performance ====================

        [UnityTest]
        public IEnumerator OpeningAndRefreshingWithOver32KnownSystems_StaysInsideTheFrameBudget()
        {
            yield return LoadSceneFresh();
            NebulaContext context = Context();
            StrategicMapScreen map = Screen();

            SystemKnowledgeService knowledge = context.NavigationDirector.Knowledge;
            // From the galaxy's own star list, not by walking sectors. The old loop
            // ran `for (sx = 0; known < 36; sx++)` along one axis and resolved a
            // full sector manifest per step; the repository caches every one, and
            // the star field is clustered rather than uniform -- measured, 64
            // sectors hold 5 systems and 1728 hold 26 -- so reaching 36 meant
            // thousands of cached manifests and the run died of OutOfMemory.
            //
            // GalaxyStarField.All is where the stars actually are. Only the sectors
            // that contain one get resolved, which is a handful, and every id is a
            // real generated system rather than a synthesised string.
            var populated = new List<SectorCoordinate>();
            var seen = new HashSet<SectorCoordinate>();
            foreach (GalaxyStarField.Placement placement in
                     GalaxyStarField.All(StarSystemRegistry.Manifests.Seed))
            {
                SectorCoordinate sector = SectorCoordinate.FromAbsolutePosition(placement.AbsolutePosition);
                if (seen.Add(sector)) populated.Add(sector);
            }

            var knownIds = new HashSet<string>();
            foreach (SectorCoordinate sector in populated)
            {
                if (knownIds.Count >= 36) break;
                foreach (SystemManifest system in StarSystemRegistry.Manifests.Resolve(sector).Systems)
                {
                    if (knownIds.Count >= 36) break;
                    if (!knownIds.Add(system.SystemId)) continue;
                    knowledge.ForceLevel(system.SystemId, SystemKnowledgeLevel.Visited);
                }
            }

            int known = knownIds.Count;
            Assert.GreaterOrEqual(known, 32,
                "the galaxy's own star list must supply at least thirty-two distinct systems");

            var openWatch = Stopwatch.StartNew();
            map.SetOpen(true);
            openWatch.Stop();
            yield return null;

            Assert.GreaterOrEqual(map.Snapshot.Systems.Count, 32, "every known system must be on the map");

            var refreshWatch = Stopwatch.StartNew();
            const int Refreshes = 10;
            for (int i = 0; i < Refreshes; i++) map.Refresh();
            refreshWatch.Stop();

            double perRefreshMs = refreshWatch.Elapsed.TotalMilliseconds / Refreshes;
            TestContext.WriteLine($"[strategic map playmode] {known} systems · open {openWatch.Elapsed.TotalMilliseconds:0.0} ms · refresh {perRefreshMs:0.00} ms");

            Assert.Less(perRefreshMs, 16.0, $"a {known}-system refresh took {perRefreshMs:0.00} ms");

            // And it still renders for several real frames without throwing.
            for (int i = 0; i < 10; i++) yield return null;
            map.SetOpen(false);
        }

        [UnityTest]
        public IEnumerator AClosedMapCostsNothing_NoSnapshotIsBuiltWhileItIsShut()
        {
            yield return LoadSceneFresh();
            StrategicMapScreen map = Screen();

            Assert.IsFalse(map.IsOpen);
            Assert.IsNull(map.Snapshot, "a closed map must not have built a snapshot at all");

            for (int i = 0; i < 20; i++) yield return null;
            Assert.IsNull(map.Snapshot, "and must still not build one while it stays closed");
        }

        // ==================== helpers ====================

        private static StrategicPlanetView FindPlanet(StrategicSystemView system, string planetId)
        {
            foreach (StrategicPlanetView p in system.Planets) if (p.PlanetId == planetId) return p;
            Assert.Fail($"planet {planetId} missing from the system view");
            return default;
        }
    }
}
