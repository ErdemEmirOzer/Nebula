using UnityEngine;
using Nebula.Hangar;
using Nebula.Modules;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Visuals;

namespace Nebula.World.Composition
{
    /// <summary>
    /// Composition step 6/7: spawns the ship at the start manifest's
    /// recommended position, the chase camera, and initializes the ship's
    /// hangar/module state for a fresh campaign. Does not touch
    /// <see cref="ShipFactory"/>'s own component-order/anchor/fallback
    /// contracts — those stay exactly as VS-001–012 built them.
    /// </summary>
    public sealed class PlayerInstaller
    {
        public void CreatePlayerAndCamera(NebulaContext context, NebulaSessionLifetime lifetime)
        {
            ShipController ship = CreateShip(context.StartSystem);
            lifetime.Track(SessionLifetime.CampaignSession, ship.gameObject);
            context.SetShip(ship);

            Camera camera = CreateCamera(ship.transform, context.GalaxyMaps, lifetime);
            context.SetMainCamera(camera);

            var shipLogistics = ship.GetComponent<Extraction.ShipLogistics>();
            shipLogistics.InitializeNewCampaign();

            var shipHangar = ship.GetComponent<ShipHangar>();
            shipHangar.InitializeNewCampaign();
            context.SetHangar(shipHangar);

            var shipModules = ship.GetComponent<ShipModules>();
            shipModules.InitializeNewCampaign();
            context.SetModules(shipModules);

            var playerShipVisual = ship.GetComponent<PlayerShipVisualController>();
            playerShipVisual.Initialize(shipModules.Modules);

            var shipModuleVisuals = ship.GetComponent<ShipModuleVisuals>();
            shipModuleVisuals.Initialize(shipModules.Modules, shipModules.Upgrades);
            context.SetModuleVisuals(shipModuleVisuals);

            // VS-013: reads ShipController/ShipModules/ShipLogistics off the same ship (all above already exist).
            var shipCombat = ship.GetComponent<ShipCombat>();
            shipCombat.InitializeNewCampaign();
            context.SetShipCombat(shipCombat);
        }

        private static ShipController CreateShip(SystemManifest homeSystem)
        {
            float firstOrbit = homeSystem.Planets.Count > 0 ? homeSystem.Planets[0].OrbitRadius : 40000f;
            Vector3 spawn = homeSystem.AbsoluteCenter + new Vector3(0f, firstOrbit * 0.15f, -firstOrbit * 0.85f);
            Quaternion look = Quaternion.LookRotation(homeSystem.AbsoluteCenter - spawn);
            return ShipFactory.Create(spawn, look);
        }

        private static Camera CreateCamera(Transform shipTransform, System.Collections.Generic.List<GalaxyMap> galaxyMaps, NebulaSessionLifetime lifetime)
        {
            var cameraGO = new GameObject("Main Camera");
            lifetime.Track(SessionLifetime.CampaignSession, cameraGO);
            cameraGO.tag = "MainCamera";
            var camera = cameraGO.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.016f, 0.035f);
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 40000000f;
            camera.allowHDR = true;
            cameraGO.AddComponent<AudioListener>();
            cameraGO.AddComponent<ChaseCamera>().SetTarget(shipTransform);
            cameraGO.AddComponent<CameraLensFlare>();

            var farClipFitter = cameraGO.AddComponent<CameraFarClipFitter>();
            foreach (GalaxyMap map in galaxyMaps) farClipFitter.Track(map);

            return camera;
        }
    }
}
