using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Extraction;
using Nebula.Modules;
using Nebula.Ship;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// PlayMode smoke test: loads the real Nebula scene and verifies the
    /// bootstrap completes without throwing and every VS-001-005 system is
    /// actually present and wired on the spawned ship. This is intentionally
    /// scoped to "does it boot cleanly with the new systems attached" — it
    /// does NOT simulate keyboard/mouse input to drive the full scan → drill
    /// → transfer → process → 2nd drill → Command → Laser → dominance →
    /// save/load flow (UnityEngine.Input cannot be reliably faked from an
    /// automated test without the Input System's test utilities, which this
    /// project does not use). That interactive flow must be verified
    /// manually in the Editor — see the implementation report.
    /// </summary>
    public class BootstrapSmokeTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        [UnityTest]
        public IEnumerator SceneBoots_AndAllVS005SystemsArePresentOnTheShip()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null; // let Awake()/Start() of every bootstrapped object run

            // Production boots to the main menu, so the world exists only once it is
            // asked for. This test is about what a session builds, not about how it
            // is started, so it asks -- through the same entry the menu's button uses.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;

            var ship = Object.FindObjectOfType<ShipController>();
            Assert.IsNotNull(ship, "SolarSystemBootstrap must spawn the ship without throwing");

            Assert.IsNotNull(ship.GetComponent<ShipLogistics>(), "VS-001 logistics cargo missing");
            Assert.IsNotNull(ship.GetComponent<ShipExtraction>(), "VS-001 extraction controller missing");
            Assert.IsNotNull(ship.GetComponent<ShipProcessing>(), "VS-002 processing bay missing");

            var modules = ship.GetComponent<ShipModules>();
            Assert.IsNotNull(modules, "VS-005 module host missing");
            Assert.IsNotNull(modules.Modules, "module state must be initialized by the bootstrap");
            Assert.IsNotNull(modules.Upgrades, "upgrade service must be initialized by the bootstrap");

            foreach (string id in ModuleCatalog.FreeStartingModuleIds)
                Assert.AreEqual(1, modules.Modules.GetTier(id), $"{id} must start at T1 for free");

            Assert.IsNotNull(ship.GetComponent<ShipModuleVisuals>(), "VS-005 ship module visuals missing");

            var director = Object.FindObjectOfType<ExtractionDirector>();
            Assert.IsNotNull(director, "VS-001 save director missing");

            // Progression, navigation and hangar are pages inside the one parent
            // menu now, so what a boot must produce is that menu, closed, with the
            // page registered.
            var menu = Object.FindObjectOfType<Nebula.UI.Menu.NebulaParentMenuScreen>(true);
            Assert.IsNotNull(menu, "the parent command menu missing");
            Assert.IsFalse(menu.IsOpen, "the menu must start closed");
            Assert.IsNotNull(menu.PageOf<Nebula.UI.Menu.NebulaProgressionPage>(), "VS-005 progression page missing");

            // --- VS-006: registry, navigation service/screen/HUD, Scanner/Motor consumers.
            Assert.IsNotNull(StarSystemRegistry.StartSystemId, "the start system must be registered");
            Assert.IsTrue(StarSystemRegistry.TryGet(StarSystemRegistry.StartSystemId, out _), "the start system must resolve from its manifest");

            var cruise = ship.GetComponent<ShipCruise>();
            Assert.IsNotNull(cruise, "VS-006 ShipCruise missing from the ship");
            Assert.AreEqual(Nebula.Navigation.FlightMode.LocalFlight, cruise.Mode, "a fresh campaign must start in LocalFlight");
            Assert.AreEqual(0, cruise.EngineTier, "Engine-Navigation starts at T0 (not a free starting module)");
            Assert.AreEqual(0, modules.Modules.GetTier("exploration.scanner"), "Scanner starts at T0");

            var navigationDirector = Object.FindObjectOfType<NavigationDirector>();
            Assert.IsNotNull(navigationDirector, "VS-006 navigation director missing");
            Assert.AreEqual(Nebula.Navigation.SystemKnowledgeLevel.Visited, navigationDirector.Knowledge.GetLevel(StarSystemRegistry.StartSystemId),
                "the start system must be Visited from the very first frame");

            Assert.IsNotNull(menu.PageOf<Nebula.UI.Menu.NebulaDiscoveryPage>(), "VS-006 discovery page missing");
            Assert.IsNotNull(Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudNavigation>(), "VS-006 navigation HUD missing");

            // --- VS-007: Hangar-Bot module host, fleet, and UI.
            var hangar = ship.GetComponent<Nebula.Hangar.ShipHangar>();
            Assert.IsNotNull(hangar, "VS-007 ShipHangar missing from the ship");
            Assert.AreEqual(0, modules.Modules.GetTier("exploration.hangar_bot"), "Hangar-Bot starts at T0 (not a free starting module)");
            Assert.AreEqual(0, hangar.HangarTier);
            Assert.AreEqual(0, hangar.Command.Capacity, "T0 grants no command capacity");
            Assert.IsNotNull(hangar.Production, "the production queue must be initialized by the bootstrap");
            Assert.IsNull(hangar.Production.ActiveJob, "a fresh campaign starts with no production job running");
            Assert.AreEqual(0, hangar.Vehicles.Count, "a fresh campaign starts with an empty fleet");

            Assert.IsNotNull(ship.transform.Find("anchor.exploration.hangar_bot"), "VS-007 Hangar-Bot visual anchor missing");

            Assert.IsNotNull(menu.PageOf<Nebula.UI.Menu.NebulaFleetPage>(), "VS-007 fleet page missing");
            Assert.IsNotNull(Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudContextBar>(), "VS-007 fleet HUD strip missing");

            // --- VS-008: sector/manifest streaming boots with ONLY the start system Full.
            var streaming = Object.FindObjectOfType<Nebula.Sectors.SystemStreamingDirector>();
            Assert.IsNotNull(streaming, "VS-008 streaming director missing");
            Assert.AreEqual(StarSystemRegistry.StartSystemId, streaming.FullSystemId, "the start system must boot Full");
            Assert.IsNull(streaming.WarmSystemId, "nothing should be Warm at boot — FrontierGuaranteePass keeps the nearest neighbor >= 450k away, beyond the 300k Warm-prepare distance");

            int livePlanetCount = 0;
            foreach (var _ in ExtractionState.AllPlanets) livePlanetCount++;
            Nebula.Sectors.SystemManifest startManifest = StarSystemRegistry.Manifests.ResolveSystem(StarSystemRegistry.StartSystemId);
            Assert.AreEqual(startManifest.Planets.Count, livePlanetCount, "exactly the start system's planets must be materialized, nothing more");

            Assert.IsNotNull(Object.FindObjectOfType<Nebula.UI.SectorDebugHud>(), "VS-008 sector debug HUD missing");

            // Every other real system nearby must resolve from its manifest without ever being materialized.
            bool foundUnmaterializedNeighbor = false;
            foreach (StarSystemRecord record in StarSystemRegistry.SystemsNear(startManifest.AbsoluteCenter))
            {
                if (record.SystemId == StarSystemRegistry.StartSystemId) continue;
                Assert.IsNull(record.StarTransform, $"{record.SystemId} must not be materialized this far from the ship");
                foundUnmaterializedNeighbor = true;
            }
            Assert.IsTrue(foundUnmaterializedNeighbor, "the start sector must contain at least one other real system (FrontierGuaranteePass §3)");
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

            // Then wait for the front door to stand down, because the boot is not
            // finished until it has. The main menu builds a shell of its own before any
            // session exists (NebulaMainMenuHost.cs:98) and tears it down from its own
            // Update on the first frame it sees SessionActive
            // (NebulaMainMenuHost.cs:255) -- deliberately polled rather than pushed by
            // the launcher, so that a session started any other way dismisses it too
            // (the reason is written out at NebulaMainMenuHost.cs:246-249). So for one
            // frame after BeginSession there are legitimately two shells, and Unity then
            // defers the Destroy to the end of that frame. Sampling a single frame after
            // BeginSession caught that handover mid-flight and counted the menu's
            // outgoing shell as a duplicate of the session's: "exactly one NebulaUiShell
            // -- found 2: Nebula UI Shell (active, scene=Nebula) | Nebula UI Shell
            // (active, scene=Nebula)". Nothing is loosened here -- every census below
            // still demands exactly one -- the boot is simply allowed to finish first.
            for (int frame = 0; frame < 32 && Nebula.UI.Session.NebulaMainMenuHost.Active != null; frame++)
                yield return null;

            // One more: Dismiss clears its own reference immediately but the GameObject
            // itself is not gone until the end of the frame that destroyed it.
            yield return null;
        }

        /// <summary>TF-002 §10/§11: the composition root must never produce a duplicate director/singleton, whether at first boot or after any later event.</summary>
        [UnityTest]
        public IEnumerator SceneBoots_ExactlyOneOfEachSingletonDirectorAndComponent()
        {
            yield return LoadSceneFresh();

            Assert.AreEqual(1, Object.FindObjectsOfType<ShipController>().Length, "exactly one ship");
            Assert.AreEqual(1, Object.FindObjectsOfType<Camera>().Length, "exactly one camera");
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.Simulation.GravitySystem>().Length, "exactly one gravity system");
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.Sectors.SystemStreamingDirector>().Length, "exactly one streaming director");
            Assert.AreEqual(1, Object.FindObjectsOfType<ExtractionDirector>().Length, "exactly one extraction director");
            Assert.AreEqual(1, Object.FindObjectsOfType<DominanceSystem>().Length, "exactly one dominance system");
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.Logistics.CollectorRouteDirector>().Length, "exactly one collector route director");
            Assert.AreEqual(1, Object.FindObjectsOfType<NavigationDirector>().Length, "exactly one navigation director");
            Assert.AreEqual(1, Object.FindObjectsOfType<FloatingOrigin>().Length, "exactly one floating origin");
            Assert.AreEqual(1, Object.FindObjectsOfType<SolarSystemBootstrap>().Length, "exactly one scene adapter");
        }

        /// <summary>TF-002 §6 PresentationInstaller: every HUD/Screen exactly once, every modal closed at boot.</summary>
        [UnityTest]
        public IEnumerator SceneBoots_ExactlyOneOfEachHudAndScreen_AllModalsClosed()
        {
            yield return LoadSceneFresh();

            // Widgets start with their panels hidden, so the census has to include
            // inactive objects -- a duplicate hidden widget is still a duplicate.
            // Each census names itself and lists what it found. "Expected: 1 But was: 2"
            // over ten identical assertions says a duplicate exists and nothing about
            // which type it is or where it came from -- and a duplicate here is always
            // an object that outlived a session, so the owning scene and object name
            // are the whole diagnosis.
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudNavigation>();
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudAlerts>();
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudVitals>();
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudPlanetContext>();
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudNotifications>();
            AssertExactlyOne<Nebula.UI.Hud.NebulaHudContextBar>();
            AssertExactlyOne<Nebula.UI.Menu.NebulaParentMenuScreen>();
            AssertExactlyOne<Nebula.UI.Shell.NebulaConfirmationDialog>();
            AssertExactlyOne<Nebula.UI.Shell.NebulaUiShell>();
            AssertExactlyOne<Nebula.UI.SectorDebugHud>(includeInactive: false);

            Assert.IsFalse(Object.FindObjectOfType<Nebula.UI.Menu.NebulaParentMenuScreen>(true).IsOpen,
                "the parent menu is open at boot");
            Assert.IsFalse(Object.FindObjectOfType<Nebula.UI.Shell.NebulaConfirmationDialog>(true).IsOpen,
                "a confirmation dialog is open at boot");
            Assert.IsNull(ExclusiveModal.Owner, "no modal should be held at boot");
        }

        /// <summary>
        /// One of a presentation singleton, named, with every instance listed when the
        /// count is wrong: its object name, whether it is active, and which scene it
        /// belongs to. A survivor of a <c>LoadSceneMode.Single</c> reports
        /// "DontDestroyOnLoad" as its scene, which distinguishes a leaked predecessor
        /// from a composition root that genuinely built two.
        /// </summary>
        private static void AssertExactlyOne<T>(bool includeInactive = true) where T : Component
        {
            T[] found = Object.FindObjectsOfType<T>(includeInactive);
            if (found.Length == 1) return;

            var detail = new System.Text.StringBuilder();
            for (int i = 0; i < found.Length; i++)
                detail.Append(i == 0 ? "" : " | ")
                      .Append(found[i].gameObject.name)
                      .Append(found[i].gameObject.activeInHierarchy ? " (active" : " (inactive")
                      .Append(", scene=").Append(found[i].gameObject.scene.name).Append(')');

            Assert.AreEqual(1, found.Length,
                $"exactly one {typeof(T).Name} — found {found.Length}: "
                + (found.Length == 0 ? "none" : detail.ToString()));
        }

        /// <summary>TF-002 §5/§8: the typed context exposes exactly the singletons the scene actually wired, and composition finished (session active) in the correct order.</summary>
        [UnityTest]
        public IEnumerator Context_ExposesTheSameWiredSingletons_CompositionFinishedInOrder()
        {
            yield return LoadSceneFresh();

            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            Assert.IsNotNull(context, "the scene adapter must expose the finished session's context");

            Assert.AreSame(Object.FindObjectOfType<ShipController>(), context.Ship);
            Assert.AreSame(Object.FindObjectOfType<Nebula.Sectors.SystemStreamingDirector>(), context.Streaming);
            Assert.AreSame(Object.FindObjectOfType<ExtractionDirector>(), context.ExtractionDirector);
            Assert.AreSame(Object.FindObjectOfType<NavigationDirector>(), context.NavigationDirector);
            Assert.AreEqual(StarSystemRegistry.StartSystemId, context.StartSystemId);
            Assert.AreEqual(context.StartSystemId, context.Streaming.FullSystemId, "step 8 must have already booted the start system Full by the time the session is active");
            Assert.IsNull(context.Streaming.WarmSystemId);
            Assert.IsNotNull(context.Presentation, "step 11 presentation must have completed — the context's presentation handle must be set");
            Assert.AreSame(Object.FindObjectOfType<Nebula.UI.Hud.NebulaHudNavigation>(), context.Presentation.HudNavigation);
            Assert.IsNotNull(context.FloatingOrigin, "step 12 final environment binding must have completed");
        }

        /// <summary>TF-002 §7: RestartCampaign disposes the old session cleanly and boots an equivalent new one with no leaked references or duplicates.</summary>
        [UnityTest]
        public IEnumerator RestartCampaign_DisposesOldSession_NoDuplicates_NewReferencesDiffer_SameStartResult()
        {
            yield return LoadSceneFresh();

            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            ShipController oldShip = adapter.Context.Ship;
            Nebula.Sectors.SystemStreamingDirector oldStreaming = adapter.Context.Streaming;
            string oldStartSystemId = adapter.Context.StartSystemId;

            adapter.RestartCampaign();
            yield return null;
            yield return null;

            Assert.IsTrue(oldShip == null, "the old ship must be destroyed by dispose");
            Assert.IsTrue(oldStreaming == null, "the old streaming director must be destroyed by dispose");

            ShipController newShip = adapter.Context.Ship;
            Assert.IsNotNull(newShip);
            Assert.AreNotSame(oldShip, newShip, "a restart must produce a genuinely new ship instance, not reuse the old (destroyed) one");

            Assert.AreEqual(1, Object.FindObjectsOfType<ShipController>().Length, "no duplicate ship after restart");
            Assert.AreEqual(1, Object.FindObjectsOfType<Camera>().Length, "no duplicate camera after restart");
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.Sectors.SystemStreamingDirector>().Length, "no duplicate streaming director after restart");
            Assert.AreEqual(1, Object.FindObjectsOfType<ExtractionDirector>().Length, "no duplicate extraction director after restart");
            Assert.AreEqual(1, Object.FindObjectsOfType<NavigationDirector>().Length, "no duplicate navigation director after restart");
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.UI.Hud.NebulaHudNavigation>(true).Length, "no duplicate navigation HUD after restart");
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.UI.Menu.NebulaParentMenuScreen>(true).Length, "no duplicate parent menu after restart");

            Assert.IsNull(ExclusiveModal.Owner, "restart must never leave a stale modal owner behind");
            Assert.AreEqual(oldStartSystemId, adapter.Context.StartSystemId, "the scene's fixed non-zero seed must reproduce the exact same start system after restart");
        }

        /// <summary>TF-002 §7: a full scene reload (not just RestartCampaign) must also never leave duplicate singletons behind.</summary>
        [UnityTest]
        public IEnumerator SceneReload_ProducesNoDuplicateSingletons()
        {
            yield return LoadSceneFresh();
            yield return LoadSceneFresh();

            Assert.AreEqual(1, Object.FindObjectsOfType<ShipController>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.Sectors.SystemStreamingDirector>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<NavigationDirector>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.UI.Hud.NebulaHudNavigation>(true).Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<SolarSystemBootstrap>().Length);
        }

        /// <summary>TF-002 §6 WorldInstaller: a demote/reboot cycle through the existing public API preserves campaign state (navigation knowledge) while only materialized-system state changes.</summary>
        [UnityTest]
        public IEnumerator DemoteAndRebootFull_PreservesCampaignDelta_OnlyMaterializedStateChanges()
        {
            yield return LoadSceneFresh();

            var adapter = Object.FindObjectOfType<SolarSystemBootstrap>();
            NebulaContext context = adapter.Context;
            string startSystemId = context.StartSystemId;
            Nebula.Sectors.SystemManifest startManifest = StarSystemRegistry.Manifests.ResolveSystem(startSystemId);

            Assert.AreEqual(Nebula.Navigation.SystemKnowledgeLevel.Visited,
                context.NavigationDirector.Knowledge.GetLevel(startSystemId), "sanity: the start system is Visited before any demote/reboot");

            context.Streaming.ResetAndBootFull(startManifest);
            yield return null;

            Assert.AreEqual(startSystemId, context.Streaming.FullSystemId, "rebooting the same manifest Full must land back on the same system");
            Assert.AreEqual(Nebula.Navigation.SystemKnowledgeLevel.Visited,
                context.NavigationDirector.Knowledge.GetLevel(startSystemId), "campaign navigation knowledge must survive a materialized-system reboot untouched");
            Assert.AreEqual(1, Object.FindObjectsOfType<Nebula.Sectors.SystemStreamingDirector>().Length, "the reboot must never create a second streaming authority");

            int livePlanetCount = 0;
            foreach (var _ in ExtractionState.AllPlanets) livePlanetCount++;
            Assert.AreEqual(startManifest.Planets.Count, livePlanetCount, "exactly the (re-)materialized start system's planets, no duplicates");
        }
    }
}
