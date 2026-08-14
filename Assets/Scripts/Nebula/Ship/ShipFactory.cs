using UnityEngine;
using Nebula.Visuals;

namespace Nebula.Ship
{
    /// <summary>
    /// Builds the observation ship out of primitives so no model assets are
    /// required, including the speed-driven thruster flame. Later ship types
    /// can get their own factories or prefabs. Visual parts carry no colliders
    /// — ship collision is handled analytically by the controller.
    /// </summary>
    public static class ShipFactory
    {
        public static ShipController Create(Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("Observation Ship");
            root.transform.SetPositionAndRotation(position, rotation);

            // Part 24 authored Mk/Stage controller. Two fail-safes sit behind it,
            // in order: the authored Mk I base hull, and then the primitive hull
            // if even that will not load. Both are hidden the moment an approved
            // Part 24 prefab loads, and the controller refuses to mount either
            // while one is already on screen.
            var playerVisual = root.AddComponent<PlayerShipVisualController>();
            if (!playerVisual.CreateInitialVisual())
            {
                if (playerVisual.CreateFallbackHullVisual() == null)
                {
                    var fallback = new GameObject("Primitive Hull Fallback");
                    fallback.transform.SetParent(root.transform, false);
                    BuildPrimitiveHull(fallback.transform);
                    playerVisual.RegisterFallback(fallback);
                }
            }

            ParticleSystem flame = CreateThrusterFlame(root.transform);

            Color haloColor = new Color(1f, 0.58f, 0.2f, 0.4f);
            Material haloMaterial = MaterialLibrary.CreateAdditive(haloColor, MaterialLibrary.RadialGlowTexture);
            GameObject engineHalo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            engineHalo.name = "Engine Halo";
            Object.Destroy(engineHalo.GetComponent<Collider>());
            engineHalo.transform.SetParent(root.transform, false);
            engineHalo.transform.localPosition = new Vector3(0f, 0f, -3.4f);
            engineHalo.transform.localScale = Vector3.one * 2.2f;
            engineHalo.AddComponent<Billboard>();
            engineHalo.GetComponent<MeshRenderer>().sharedMaterial = haloMaterial;

            // LGT-03: the hull fill is no longer created here. It is the ship's
            // only real-time light and belongs to ShipLightingPresenter, which
            // owns it inside the lighting budget and drops it entirely on the
            // Low profile — a light spawned by the factory would sit outside
            // that budget with nothing able to switch it off.

            ShipController controller = root.AddComponent<ShipController>();
            root.AddComponent<ThrusterEffect>()
                .Initialize(controller, flame, engineHalo.transform, haloMaterial, haloColor);
            // Extraction slice: logistics hold first so ShipExtraction.Awake finds it.
            root.AddComponent<Nebula.Extraction.ShipLogistics>();
            root.AddComponent<Nebula.Extraction.ShipExtraction>();
            root.AddComponent<Nebula.Extraction.ShipProcessing>();
            // VS-006: ShipCruise reads ShipController/ShipExtraction in its own
            // Awake, so it must exist before ShipModules.ApplyAllFunctionalEffects
            // (called later by the bootstrap) looks it up.
            root.AddComponent<ShipCruise>();
            // VS-007: same reasoning — ShipHangar must exist before ShipModules looks it up.
            root.AddComponent<Nebula.Hangar.ShipHangar>();
            // VS-010: both read ShipController/ShipExtraction/ShipLogistics/ShipHangar in their own Awake, so they must come after all four.
            root.AddComponent<Nebula.Extraction.ShipQuickAssetPackage>();
            root.AddComponent<Nebula.Extraction.ShipPlanetEvacuation>();

            // Module system: anchors first (named after their stable VisualAnchorId
            // so ShipModuleVisuals can transform.Find them), then the components
            // that drive/visualize them. The bootstrap finishes wiring these after
            // ProgressionService.Reset() (ShipModules.InitializeNewCampaign then
            // ShipModuleVisuals.Initialize) since module state depends on that reset.
            CreateModuleAnchor(root.transform, "anchor.industry.logistics_cargo", new Vector3(1.3f, -0.2f, -0.6f));
            CreateModuleAnchor(root.transform, "anchor.industry.resource_processing", new Vector3(-1.3f, -0.2f, -0.6f));
            CreateModuleAnchor(root.transform, "anchor.industry.station_construction", new Vector3(0f, 0.9f, -1.4f));
            CreateModuleAnchor(root.transform, "anchor.exploration.scanner", new Vector3(0f, 0.75f, 1.1f));
            CreateModuleAnchor(root.transform, "anchor.exploration.engine_navigation", new Vector3(0f, -0.85f, -2.4f));
            CreateModuleAnchor(root.transform, "anchor.exploration.hangar_bot", new Vector3(1.3f, 0.9f, -0.6f));
            CreateModuleAnchor(root.transform, "anchor.defense.hull_reinforcement", new Vector3(0f, -0.7f, 0.1f));
            CreateModuleAnchor(root.transform, "anchor.defense.shield_systems", new Vector3(-1.3f, 0.9f, -0.6f));
            CreateModuleAnchor(root.transform, "anchor.defense.weapon_coordination", new Vector3(0f, 1.15f, 0.1f));
            CreateModuleAnchor(root.transform, "anchor.nuclear.star_energy_storage", new Vector3(-1.6f, -0.2f, -1.4f));
            CreateModuleAnchor(root.transform, "anchor.nuclear.laboratory", new Vector3(0f, 1.35f, -1.1f));
            CreateModuleAnchor(root.transform, "anchor.nuclear.interstellar_logistics", new Vector3(0f, 0f, -2.8f));
            root.AddComponent<Nebula.Modules.ShipModules>();
            root.AddComponent<Nebula.Modules.ShipModuleVisuals>();
            // VS-011: reads ShipController/ShipLogistics/ShipModules in its own Awake, so it must come last.
            root.AddComponent<Nebula.Logistics.ShipLogisticsCenter>();
            root.AddComponent<Nebula.Extraction.ShipMaintenanceReserveAccess>();
            // VS-012: reads ShipController/ShipLogistics/ShipHangar in its own Awake.
            root.AddComponent<Nebula.Poi.ShipPoi>();
            // VS-013: ShipCombat reads ShipController/ShipModules/ShipLogistics in its own
            // Awake, so it must come after all three; ShipEmergencyRecovery reads ShipCombat too.
            root.AddComponent<ShipCombat>();
            root.AddComponent<ShipWeaponVisuals>();
            root.AddComponent<ShipEmergencyRecovery>();
            return controller;
        }

        /// <summary>The original primitive ship, the last resort when no authored hull will load at all.</summary>
        private static void BuildPrimitiveHull(Transform root)
        {
            Material hullMaterial = MaterialLibrary.CreateLit(new Color(0.62f, 0.65f, 0.7f), 0.55f, 0.75f);

            GameObject hull = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            hull.name = "Hull";
            Object.Destroy(hull.GetComponent<Collider>());
            hull.transform.SetParent(root.transform, false);
            hull.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // capsule length along +Z
            hull.transform.localScale = new Vector3(1.5f, 3f, 1.5f);
            hull.GetComponent<MeshRenderer>().sharedMaterial = hullMaterial;

            GameObject cockpit = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cockpit.name = "Cockpit";
            Object.Destroy(cockpit.GetComponent<Collider>());
            cockpit.transform.SetParent(root.transform, false);
            cockpit.transform.localPosition = new Vector3(0f, 0.45f, 1.7f);
            cockpit.transform.localScale = new Vector3(0.9f, 0.7f, 1.1f);
            cockpit.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateLit(new Color(0.2f, 0.45f, 0.7f), 0.9f, 0.4f);

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wing.name = side < 0 ? "Wing L" : "Wing R";
                Object.Destroy(wing.GetComponent<Collider>());
                wing.transform.SetParent(root.transform, false);
                wing.transform.localPosition = new Vector3(1.7f * side, 0f, -0.9f);
                wing.transform.localRotation = Quaternion.Euler(0f, -14f * side, 0f);
                wing.transform.localScale = new Vector3(2.6f, 0.1f, 1.3f);
                wing.GetComponent<MeshRenderer>().sharedMaterial = hullMaterial;
            }

            // Warm engine visuals to match the fire of the thruster flame.
            GameObject engine = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            engine.name = "Engine Glow";
            Object.Destroy(engine.GetComponent<Collider>());
            engine.transform.SetParent(root.transform, false);
            engine.transform.localPosition = new Vector3(0f, 0f, -3.2f);
            engine.transform.localScale = new Vector3(0.8f, 0.8f, 0.35f);
            engine.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateUnlit(new Color(1f, 0.72f, 0.35f));
        }

        /// <summary>Empty anchor point named after a module's stable VisualAnchorId, so ShipModuleVisuals can find it by name.</summary>
        private static void CreateModuleAnchor(Transform root, string anchorId, Vector3 localPosition)
        {
            var anchor = new GameObject(anchorId);
            anchor.transform.SetParent(root, false);
            anchor.transform.localPosition = localPosition;
        }

        /// <summary>Fire cone behind the engine; ThrusterEffect drives its intensity from speed.</summary>
        private static ParticleSystem CreateThrusterFlame(Transform root)
        {
            var flameGO = new GameObject("Thruster Flame");
            flameGO.transform.SetParent(root, false);
            flameGO.transform.localPosition = new Vector3(0f, 0f, -3.4f);
            flameGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // emit backward

            var flame = flameGO.AddComponent<ParticleSystem>();

            var main = flame.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // flame trails behind the moving ship
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
            main.startColor = new Color(1f, 0.92f, 0.7f);
            main.maxParticles = 900;

            var emission = flame.emission;
            emission.rateOverTime = 0f; // ThrusterEffect raises this with speed

            var shape = flame.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.28f;

            // White-hot core fading through orange to dark red smoke.
            var colorOverLifetime = flame.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.96f, 0.8f), 0f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.35f),
                    new GradientColorKey(new Color(0.85f, 0.2f, 0.05f), 0.75f)
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.55f, 0.4f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = flame.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f)));

            var flameRenderer = flameGO.GetComponent<ParticleSystemRenderer>();
            flameRenderer.sharedMaterial = MaterialLibrary.CreateAdditive(Color.white, MaterialLibrary.RadialGlowTexture);
            flameRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flameRenderer.receiveShadows = false;

            return flame;
        }
    }
}
