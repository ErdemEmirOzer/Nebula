using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Extraction;
using Nebula.Navigation;
using Nebula.Poi;
using Nebula.Sectors;
using Nebula.UI.StrategicMap;
using Nebula.UI.StrategicMap.Preview;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    public sealed class StrategicMapPoiInteractionTests
    {
        [Test]
        public void LocatedPoiMarker_IsPickableOnlyInsideItsOwningSystem()
        {
            var universe = new StrategicMapUniverseSnapshot();
            universe.systems.Add(new StrategicMapUniverseSnapshot.SystemRow
            {
                id = "system-a", galaxyId = "galaxy", x = 0f, y = 0f, z = 0f,
                planetFrom = 0, planetTo = 0
            });
            universe.discoveries.Add(new StrategicMapUniverseSnapshot.MarkerRow
            {
                id = "system-a/poi.0", name = "Bilinmeyen keşif noktası",
                systemId = "system-a", galaxyId = "galaxy", x = 3f, y = 0f, z = 0f,
                knowledge = (int)PoiKnowledgeLevel.Located
            });

            var cameraObject = new GameObject("StrategicMapPoiPicker.Camera");
            var target = new RenderTexture(512, 512, 0);
            Camera camera = null;
            try
            {
                camera = cameraObject.AddComponent<Camera>();
                camera.targetTexture = target;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.transform.rotation = Quaternion.identity;

                Vector3 projected = camera.WorldToScreenPoint(universe.discoveries[0].Position);
                Vector2 point = new Vector2(projected.x, projected.y);
                StrategicMapPick pick = StrategicMapPicker.Pick(camera, universe, point,
                    StrategicMapLevel.System, "galaxy", "system-a", 1f);
                Assert.AreEqual(StrategicMapPickKind.Poi, pick.Kind);
                Assert.AreEqual("system-a/poi.0", pick.Id);

                StrategicMapPick outsideOwner = StrategicMapPicker.Pick(camera, universe, point,
                    StrategicMapLevel.System, "galaxy", "system-b", 1f);
                Assert.AreNotEqual(StrategicMapPickKind.Poi, outsideOwner.Kind,
                    "a marker from another system must never be selectable through the active system view");
            }
            finally
            {
                // The camera must let go of the target before the target is released, or the
                // engine logs "Releasing render texture that is set as Camera.targetTexture!"
                // -- an ERROR, which fails the test outright. Production already does this in
                // StrategicMapUniverseRenderer.ReleaseTarget; only the fixtures had it backwards.
                if (camera != null) camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }

        [Test]
        public void PoiRoute_TargetsTheOwningSystem_NotTheLocalPoiId()
        {
            var go = new GameObject("StrategicMapPoiCommands.Navigation");
            try
            {
                NavigationDirector navigation = go.AddComponent<NavigationDirector>();
                navigation.Knowledge.ForceLevel("system-a", SystemKnowledgeLevel.Visited);
                var context = new NebulaContext();
                context.SetNavigationDirector(navigation);
                var commands = new StrategicMapCommands(context);
                StrategicSystemView system = System("system-a");
                StrategicPoiView located = Poi("system-a/poi.0", positionKnown: true,
                    typeKnown: false, riskKnown: false);

                Assert.AreEqual(StrategicCommandResult.Accepted,
                    commands.SetWaypointToPoiSystem(system, located));
                Assert.AreEqual("system-a", navigation.SelectedRouteSystemId);
                Assert.AreNotEqual(located.PoiId, navigation.SelectedRouteSystemId,
                    "NavigationDirector has system routes only; the map must not fabricate a local-offset route");

                navigation.ClearRoute();
                StrategicPoiView signal = Poi("system-a/poi.1", positionKnown: false,
                    typeKnown: false, riskKnown: false);
                Assert.AreEqual(StrategicCommandResult.NotLocated,
                    commands.SetWaypointToPoiSystem(system, signal));
                Assert.IsNull(navigation.SelectedRouteSystemId);
                Assert.AreEqual("Keşif noktasının konumu henüz bulunmadı", commands.LastRejectReason);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PoiCard_UsesGatedFactsAndLiveProgress_WithoutLeakingStableIdsOrType()
        {
            var rootObject = new GameObject("StrategicMapPoiCard.Root");
            RectTransform root = rootObject.AddComponent<RectTransform>();
            try
            {
                StrategicObjectCard card = StrategicObjectCard.Create(root);
                StrategicPoiView located = Poi("system-a/poi.secret", positionKnown: true,
                    typeKnown: false, riskKnown: false);
                var live = new PoiState(located.PoiId);
                live.RestoreCore(PoiKnowledgeLevel.Located, 12f, PoiCatalog.IdentifySeconds * 0.5f,
                    0f, PoiPauseReason.None, null);

                card.ShowPoi(located, System("system-a"), live);
                string rendered = AllText(card.gameObject);

                StringAssert.Contains("Tanımlanamayan temas", rendered);
                StringAssert.Contains("Konumu bulundu", rendered);
                StringAssert.Contains("%50", rendered);
                StringAssert.DoesNotContain("Anomali", rendered,
                    "a Located POI has not earned its manifest type yet");
                StringAssert.DoesNotContain("Güvenli", rendered,
                    "a Located POI has not earned its manifest risk yet");
                StringAssert.DoesNotContain(located.PoiId, rendered,
                    "stable world ids are never player-facing card copy");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void PoiProgress_UsesRealStateDurationsAndRejectsAMismatchedState()
        {
            StrategicPoiView anomaly = new StrategicPoiView("poi-a", "Anomali", true, Vector3.one,
                true, PoiRisk.Safe, true, PoiType.Anomaly, PoiKnowledgeLevel.Active);
            var state = new PoiState("poi-a");
            state.RestoreCore(PoiKnowledgeLevel.Active, 0f, PoiCatalog.IdentifySeconds,
                PoiCatalog.AnomalyShipSafeSeconds * 0.5f, PoiPauseReason.None, null);

            Assert.IsTrue(StrategicObjectCard.TryPoiProgress01(anomaly, state, out float progress));
            Assert.AreEqual(0.5f, progress, 0.0001f);
            Assert.IsFalse(StrategicObjectCard.TryPoiProgress01(anomaly, new PoiState("poi-b"), out _));
        }

        private static StrategicPoiView Poi(string id, bool positionKnown, bool typeKnown, bool riskKnown) =>
            new StrategicPoiView(id,
                typeKnown ? "Anomali" : positionKnown ? "Tanımlanamayan temas" : "Bilinmeyen sinyal",
                positionKnown, new Vector3(2000f, 0f, 500f), riskKnown, PoiRisk.Safe,
                typeKnown, PoiType.Anomaly,
                typeKnown ? PoiKnowledgeLevel.Identified
                    : positionKnown ? PoiKnowledgeLevel.Located : PoiKnowledgeLevel.Signal);

        private static StrategicSystemView System(string id) =>
            new StrategicSystemView(id, "Sistem A", new SectorCoordinate(0, 0, 0),
                SystemKnowledgeLevel.Visited, SystemLinkState.Live, true, Vector3.zero,
                Vector3.zero, true, StarClass.Yellow, 6000f, 200000f, 0,
                SystemOwnershipTag.Discovered, false, false, false, false, 0f,
                new List<StrategicPlanetView>(), new List<StrategicPoiView>());

        private static string AllText(GameObject root)
        {
            Text[] labels = root.GetComponentsInChildren<Text>(true);
            var values = new string[labels.Length];
            for (int i = 0; i < labels.Length; i++) values[i] = labels[i].text;
            return string.Join("\n", values);
        }
    }
}
