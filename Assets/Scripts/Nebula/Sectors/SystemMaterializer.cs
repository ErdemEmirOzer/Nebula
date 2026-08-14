using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Nebula.Celestial;
using Nebula.Extraction;
using Nebula.Simulation;
using Nebula.Visuals;
using Nebula.World;

namespace Nebula.Sectors
{
    /// <summary>
    /// Builds/tears down the live GameObjects for one system from its
    /// immutable <see cref="SystemManifest"/> (VS-008 §5). Mirrors the old
    /// SolarSystemBootstrap.CreateStarSystem/CreatePlanets shape, but every
    /// physical value (orbit radius, inclination, planet radius...) is read
    /// straight from the manifest instead of drawn from UnityEngine.Random,
    /// so materializing the same system twice always looks identical.
    /// </summary>
    public class MaterializedSystem
    {
        public string SystemId;
        public GameObject Root;
        public Transform StarTransform;
        public Star Star;
        public Transform OrbitGuides;
        public readonly Dictionary<int, PlanetIdentity> PlanetsByIndex = new Dictionary<int, PlanetIdentity>();
        public readonly RuntimeResourceScope Resources = new RuntimeResourceScope();
        public RuntimeResourceScope WarmResources { get; private set; }
        public RuntimeResourceScope FullResources { get; private set; }
        public bool IsFull;

        public RuntimeResourceScope BeginWarmResources()
        {
            WarmResources?.Dispose();
            WarmResources = new RuntimeResourceScope();
            return WarmResources;
        }

        public void ReleaseWarmResources()
        {
            WarmResources?.Dispose();
            WarmResources = null;
        }

        public RuntimeResourceScope BeginFullResources()
        {
            FullResources?.Dispose();
            FullResources = new RuntimeResourceScope();
            return FullResources;
        }

        public void ReleaseFullResources()
        {
            FullResources?.Dispose();
            FullResources = null;
        }
    }

    public static class SystemMaterializer
    {
        /// <summary>
        /// The asteroid belt's extent, as factors of the system's own NavigationRadius and
        /// outermost orbit. Named because the strategic map has to ring a system exactly
        /// where the world does — a map that draws the belt somewhere else is drawing a
        /// different system.
        /// </summary>
        public const float BeltInnerRadiusFactor = 0.28f;
        public const float BeltOuterRadiusFactor = 1.15f;

        /// <summary>How many rocks a fully materialized system's belt carries.</summary>
        public const int AsteroidsPerSystem = 500;

        /// <summary>
        /// The density every system star is built with.
        /// Named for the same reason the belt factors above are: the strategic map has to
        /// derive the same star mass the world does, or its planets would circle at a rate
        /// the cockpit window never shows.
        /// </summary>
        public const float StarDensity = 1.4f;

        /// <summary>The mass a star of this radius carries, by <see cref="Celestial.CelestialBody"/>'s own law: density * (4/3)pi r^3.</summary>
        public static float StarMass(float starRadius) =>
            StarDensity * (4f / 3f) * Mathf.PI * starRadius * starRadius * starRadius;

        /// <summary>
        /// How fast a planet at this orbit sweeps around its star, in radians per second.
        ///
        /// This is exactly the circular-orbit speed every planet is launched with below
        /// (<see cref="Simulation.GravitySystem.CircularOrbitSpeed"/>, v = sqrt(GM/r)),
        /// expressed as an angular rate: w = v / r. One full turn therefore takes 2pi/w
        /// seconds — the real orbital period of that world, whether anyone is watching it
        /// from the cockpit or from the strategic map.
        /// </summary>
        public static float OrbitalAngularSpeed(float gravitationalConstant, float starRadius, float orbitRadius)
        {
            float radius = Mathf.Max(orbitRadius, 0.01f);
            return Mathf.Sqrt(Mathf.Max(0f, gravitationalConstant) * StarMass(starRadius) / radius) / radius;
        }

        private static readonly Color LightColor = new Color(0.92f, 0.9f, 0.85f);

        /// <summary>Distinct surface-low/surface-high/glow colors per star class (VS-009 §6) — a real, visible cue alongside the HUD's text+symbol label, never color alone.</summary>
        /// <summary>
        /// The surface and glow tints one star class is painted with. Public so a
        /// test can build a probe star of each class through the same colours the
        /// materializer uses, rather than restating the art in a fixture.
        /// </summary>
        public static (Color low, Color high, Color glow) StarColorsFor(StarClass starClass) =>
            StarColors(starClass);

        private static (Color low, Color high, Color glow) StarColors(StarClass starClass) => starClass switch
        {
            StarClass.RedDwarf => (new Color(0.45f, 0.12f, 0.08f), new Color(0.95f, 0.35f, 0.2f), new Color(0.9f, 0.25f, 0.1f)),
            StarClass.Yellow => (new Color(0.6f, 0.4f, 0.25f), new Color(1f, 0.95f, 0.85f), new Color(0.9f, 0.6f, 0.3f)),
            StarClass.White => (new Color(0.7f, 0.72f, 0.78f), new Color(1f, 1f, 1f), new Color(0.85f, 0.9f, 1f)),
            StarClass.BlueGiant => (new Color(0.25f, 0.35f, 0.75f), new Color(0.75f, 0.85f, 1f), new Color(0.4f, 0.55f, 1f)),
            _ => (new Color(0.6f, 0.4f, 0.25f), new Color(1f, 0.95f, 0.85f), new Color(0.9f, 0.6f, 0.3f))
        };

        /// <summary>Warm: a cheap star (with its gravity body) plus simple orbit-ring placeholders — no planet bodies at all.</summary>
        public static MaterializedSystem CreateWarm(SystemManifest manifest, GravitySystem gravity, bool orbitGuidesVisible)
        {
            var system = new MaterializedSystem { SystemId = manifest.SystemId, IsFull = false };
            using (system.Resources.Activate())
            {
            var root = new GameObject($"System {manifest.SystemId} (Warm)");
            root.transform.position = manifest.AbsoluteCenter;

            var starGO = new GameObject(manifest.DisplayName + " Star");
            starGO.transform.SetParent(root.transform, false);
            var star = starGO.AddComponent<Star>();
            (Color low, Color high, Color glow) = StarColors(manifest.StarClass);
            star.InitializeStar(manifest.StarRadius, StarDensity, lightRange: manifest.NavigationRadius * 0.6f,
                LightColor, low, high, glow, starClass: manifest.StarClass);

                // ANI-05.3: presentation only — this star turns on its own axis
                // and its corona drifts. The star's position, radius, light
                // range and energy stay exactly where the sector system put them.
                starGO.AddComponent<Nebula.Visuals.CelestialAnimationPresenter>()
                    .Configure(Nebula.Celestial.CelestialBodyType.Star, manifest.StarClass,
                        default, Sectors.EnvironmentClass.Calm, manifest.SystemId);

            GameObject guidesGO;
            using (system.BeginWarmResources().Activate())
            {
                guidesGO = new GameObject(manifest.DisplayName + " Orbit Guides (Warm)");
                guidesGO.transform.SetParent(root.transform, false);
                foreach (PlanetManifest p in manifest.Planets)
                {
                    Quaternion plane = OrbitPlane(p);
                    OrbitRing.Create(guidesGO.transform, p.OrbitRadius, plane, new Color(0.4f, 0.6f, 1f, 0.08f));
                }
            }
            guidesGO.SetActive(orbitGuidesVisible);

                system.Root = root;
                system.StarTransform = starGO.transform;
                system.Star = star;
                system.OrbitGuides = guidesGO.transform;
            }
            return system;
        }

        /// <summary>Full: adds real planet bodies (gravity, PlanetIdentity, asteroid belt, trails) on top of an existing Warm (or fresh) system.</summary>
        public static void UpgradeToFull(MaterializedSystem system, SystemManifest manifest, GravitySystem gravity,
            List<Material> planetMaterialPool, bool showOrbitRings, bool showPlanetTrails, int asteroidCount, bool orbitGuidesVisible)
        {
            if (system.IsFull) return;
            system.ReleaseWarmResources();
            using (system.BeginFullResources().Activate())
            {
            if (system.OrbitGuides != null) Object.Destroy(system.OrbitGuides.gameObject); // replaced by the real per-planet rings below

            var guidesGO = new GameObject(manifest.DisplayName + " Orbit Guides");
            guidesGO.transform.SetParent(system.Root.transform, false);
            system.OrbitGuides = guidesGO.transform;

            foreach (PlanetManifest p in manifest.Planets)
            {
                Quaternion plane = OrbitPlane(p);
                Vector3 localPos = new Vector3(Mathf.Cos(p.StartAngleDegrees * Mathf.Deg2Rad), 0f, Mathf.Sin(p.StartAngleDegrees * Mathf.Deg2Rad)) * p.OrbitRadius;
                Vector3 localTangent = new Vector3(-Mathf.Sin(p.StartAngleDegrees * Mathf.Deg2Rad), 0f, Mathf.Cos(p.StartAngleDegrees * Mathf.Deg2Rad));

                GameObject planetGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                planetGO.name = $"{manifest.DisplayName} Planet {p.PlanetIndex + 1:00}";
                Object.Destroy(planetGO.GetComponent<SphereCollider>());
                planetGO.transform.SetParent(system.Root.transform, false);
                var planet = planetGO.AddComponent<Planet>();
                planet.InitializePlanet(p.PlanetRadius, 0.12f, axialTiltDegrees: 12f, spinSpeed: 1.2f);
                planetGO.transform.position = system.StarTransform.position + plane * localPos;

                float speed = gravity.CircularOrbitSpeed(system.Star.Mass, p.OrbitRadius);
                planet.Velocity = plane * (localTangent * speed);

                var identity = planetGO.AddComponent<PlanetIdentity>();
                identity.Initialize(p.PlanetId, planet, manifest.IsStartSystem && p.RecommendedStart);
                system.PlanetsByIndex[p.PlanetIndex] = identity;

                // VS-009 §6: archetype (from Family) picks the base material
                // treatment; environment tints it — a placeholder visual hint,
                // never a text/number leak (family/rarity stay scan-gated).
                MeshRenderer placeholderRenderer = planetGO.GetComponent<MeshRenderer>();
                placeholderRenderer.sharedMaterial = ArchetypeMaterial(p);
                PlanetArchetype archetype = PlanetArchetypeCatalog.ForFamily(p.Family);
                if (NebulaVisualCatalog.TryGetPlanet(archetype, out NebulaVisualDefinition planetVisual)
                    && NebulaVisualResolver.Instantiate(planetVisual, planetGO.transform, runtimeDiameter: 1f,
                        instanceName: "Authored Planet Visual") != null)
                {
                    // The root sphere still owns the gameplay transform/radius;
                    // only its procedural renderer is replaced.
                    placeholderRenderer.enabled = false;
                }

                // LGT-02: family and environment decide how this world answers
                // the star's light. Applied once, here, through a property
                // block — the authored albedo and textures are untouched.
                planetGO.AddComponent<CelestialSurfacePresenter>()
                    .Configure(archetype, p.Environment, LightingQualityProfile.FromQualitySettings().Quality);

                // ANI-05: presentation only — this world turns on its own axis.
                // The orbit, the radius and every surface slot stay where the
                // sector system put them.
                planetGO.AddComponent<CelestialAnimationPresenter>()
                    .Configure(Nebula.Celestial.CelestialBodyType.Planet, default, archetype,
                        p.Environment, planetGO.name);

                // ANI-05.3: one pooled marker per planet, from the control state
                // the dominance system really holds. Adds no light and no body.
                string dominanceWorldId = identity.WorldId;
                planetGO.AddComponent<DominanceMarkerPresenter>()
                    .Initialize(() => Nebula.Extraction.ExtractionState.GetDominance(dominanceWorldId));

                if (showOrbitRings) OrbitRing.Create(guidesGO.transform, p.OrbitRadius, plane, new Color(0.4f, 0.6f, 1f, 0.16f));
                if (showPlanetTrails)
                {
                    Color trailColor = Color.HSVToRGB(Mathf.Repeat(p.PlanetIndex * 0.191f, 1f), 0.5f, 0.9f);
                    AddTrail(planetGO, trailColor, p.OrbitRadius, speed, p.PlanetRadius);
                }
            }

            guidesGO.SetActive(orbitGuidesVisible);

            float outermost = manifest.Planets.Count > 0 ? manifest.Planets[manifest.Planets.Count - 1].OrbitRadius : manifest.NavigationRadius / 2.5f;
            GameObject asteroidBelt = AsteroidFieldGenerator.Generate(
                manifest.DisplayName + " Asteroid Field", system.StarTransform.position,
                innerRadius: manifest.NavigationRadius * BeltInnerRadiusFactor, outerRadius: outermost * BeltOuterRadiusFactor,
                count: asteroidCount, sizeRange: new Vector2(1.5f, 90f), driftSpeedRange: new Vector2(4f, 40f));

            // The generator hands back a scene ROOT, and nothing ever adopted it: the
            // belt outlived the system it belongs to. DowngradeToWarm looked for it
            // under system.Root and therefore never found it, and Destroy(system)
            // takes down system.Root alone -- so every Full materialization stranded
            // its own five hundred rocks. Measured at 38 live belts after 13 sessions
            // and 25 loads, where one is correct. Adopting it here is what makes both
            // of those paths actually reach it; worldPositionStays keeps the belt
            // exactly where the generator placed it.
            if (asteroidBelt != null) asteroidBelt.transform.SetParent(system.Root.transform, true);

            system.IsFull = true;
            }
        }

        /// <summary>Destroys every planet body (and, via each PlanetIdentity/DrillStructure/SurfaceStructure's own OnDestroy, unregisters them) — call ONLY after the caller has captured their state into a delta.</summary>
        public static void DowngradeToWarm(MaterializedSystem system, SystemManifest manifest, bool orbitGuidesVisible)
        {
            if (!system.IsFull) return;
            // Everything created by UpgradeToFull (planet surfaces, trails,
            // rings, POIs and encounter visuals) belongs to this promotion,
            // not to the system's persistent warm star. Release that owner on
            // every demotion so the next promotion cannot accumulate resources.
            // Deactivate-then-destroy the renderers BEFORE releasing the resources they
            // draw with. Destroy() takes effect immediately for a Material and only at
            // end of frame for a GameObject, so releasing first left a frame of "the
            // object of type 'Material' has been destroyed but you are still trying to
            // access it". Destroy(system) already gets this order right.
            foreach (var kv in system.PlanetsByIndex)
                if (kv.Value != null) { kv.Value.gameObject.SetActive(false); Object.Destroy(kv.Value.gameObject); }
            system.PlanetsByIndex.Clear();

            Transform asteroidField = system.Root.transform.Find(manifest.DisplayName + " Asteroid Field");
            if (asteroidField != null) { asteroidField.gameObject.SetActive(false); Object.Destroy(asteroidField.gameObject); }

            if (system.OrbitGuides != null)
            {
                system.OrbitGuides.gameObject.SetActive(false);
                Object.Destroy(system.OrbitGuides.gameObject);
            }
            system.ReleaseFullResources();
            GameObject guidesGO;
            using (system.BeginWarmResources().Activate())
            {
                guidesGO = new GameObject(manifest.DisplayName + " Orbit Guides (Warm)");
                guidesGO.transform.SetParent(system.Root.transform, false);
                foreach (PlanetManifest p in manifest.Planets)
                    OrbitRing.Create(guidesGO.transform, p.OrbitRadius, OrbitPlane(p), new Color(0.4f, 0.6f, 1f, 0.08f));
            }
            guidesGO.SetActive(orbitGuidesVisible);
            system.OrbitGuides = guidesGO.transform;

            system.IsFull = false;
        }

        public static void Destroy(MaterializedSystem system)
        {
            // Destroy is deferred in play mode and simply refused outside it, so an
            // EditMode fixture tearing a session down kept every system it ever
            // materialized. Same pattern NebulaSessionLifetime.DestroyAllReverse uses.
            if (system?.Root != null)
            {
                system.Root.SetActive(false);
                system.ReleaseWarmResources();
                system.ReleaseFullResources();
                system.Resources.Dispose();
                if (Application.isPlaying) Object.Destroy(system.Root);
                else Object.DestroyImmediate(system.Root);
            }
        }

        /// <summary>Per-archetype base color/specular, tinted by environment (VS-009 §6 placeholder visual differentiation — text/symbol labels carry the authoritative info, this is only a rendered hint).</summary>
        private static Material ArchetypeMaterial(PlanetManifest p)
        {
            PlanetArchetype archetype = PlanetArchetypeCatalog.ForFamily(p.Family);
            (Color albedo, float smoothness, float metallic) = archetype switch
            {
                PlanetArchetype.MetallicRocky => (new Color(0.62f, 0.66f, 0.72f), 0.55f, 0.7f),
                PlanetArchetype.Biomass => (new Color(0.3f, 0.62f, 0.32f), 0.15f, 0f),
                PlanetArchetype.UpperAtmospherePlatform => (new Color(0.55f, 0.85f, 0.8f), 0.4f, 0f),
                PlanetArchetype.FloatingOceanic => (new Color(0.25f, 0.5f, 0.85f), 0.7f, 0.1f),
                PlanetArchetype.CrystalShell => (new Color(0.8f, 0.65f, 0.95f), 0.85f, 0.3f),
                _ => (Color.gray, 0.2f, 0f)
            };

            float envMul = p.Environment switch { EnvironmentClass.Harsh => 0.8f, EnvironmentClass.Extreme => 0.6f, _ => 1f };
            albedo = new Color(albedo.r * envMul, albedo.g * envMul, albedo.b * envMul);
            return MaterialLibrary.CreateLit(albedo, smoothness, metallic);
        }

        private static Quaternion OrbitPlane(PlanetManifest p) =>
            Quaternion.AngleAxis(p.AscendingNodeDegrees, Vector3.up) * Quaternion.AngleAxis(p.OrbitInclinationDegrees, Vector3.right);

        private static void AddTrail(GameObject planetGO, Color color, float orbitRadius, float speed, float planetRadius)
        {
            var trail = planetGO.AddComponent<TrailRenderer>();
            float period = 2f * Mathf.PI * orbitRadius / Mathf.Max(speed, 0.01f);
            trail.time = Mathf.Clamp(period * 0.05f, 20f, 600f);
            trail.minVertexDistance = Mathf.Max(2f, speed * trail.time / 180f);
            trail.widthMultiplier = planetRadius * 0.5f;
            trail.material = MaterialLibrary.CreateAdditive(Color.white);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            Color start = color; start.a = 0.35f;
            Color end = color; end.a = 0f;
            trail.startColor = start; trail.endColor = end;
        }
    }
}
