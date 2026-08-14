using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;
using Nebula.Simulation;
using Nebula.Visuals;

namespace Nebula.World.Composition
{
    /// <summary>
    /// Render/quality environment setup, the decorative galaxy background
    /// (particle spiral, arm stars, companion galaxies, intergalactic halo,
    /// decor POIs, asteroid drift), the planet material pool, gravity, and
    /// finally the starfield/skybox that need the player's camera to exist
    /// first. Every <see cref="Random"/>-consuming call this class makes
    /// happens inside <see cref="BuildWorldFoundation"/>, in the exact same
    /// relative order as the original monolith — that single contiguous
    /// block, seeded once by <see cref="NebulaBootstrap"/> before anything
    /// else touches <see cref="Random"/>, is what keeps decor generation
    /// byte-for-byte reproducible for a given seed. Generator algorithms,
    /// call counts, and content counts are untouched — only the containing
    /// method changed.
    /// </summary>
    public sealed class EnvironmentInstaller
    {
        private static readonly (string name, Color light, Color glow, Color low, Color high)[] StarPresets =
        {
            ("Rubeus",  new Color(1f, 0.6f, 0.5f),    new Color(1f, 0.25f, 0.1f),  new Color(1f, 0.3f, 0.12f),  new Color(1f, 0.75f, 0.5f)),
            ("Aurelia", new Color(1f, 0.82f, 0.55f),  new Color(1f, 0.6f, 0.15f),  new Color(1f, 0.62f, 0.2f),  new Color(1f, 0.95f, 0.75f)),
            ("Albion",  new Color(0.95f, 0.95f, 1f),  new Color(0.85f, 0.9f, 1f),  new Color(0.8f, 0.85f, 1f),  new Color(1f, 1f, 1f)),
            ("Boreas",  new Color(0.7f, 0.8f, 1f),    new Color(0.4f, 0.6f, 1f),   new Color(0.45f, 0.6f, 1f),  new Color(0.9f, 0.95f, 1f)),
            ("Vespera", new Color(0.85f, 0.75f, 1f),  new Color(0.6f, 0.45f, 1f),  new Color(0.6f, 0.5f, 1f),   new Color(0.95f, 0.9f, 1f))
        };

        private readonly List<Vector3> decorPositions = new List<Vector3>();

        /// <summary>
        /// Composition step 4. Order within this method is load-bearing —
        /// see the class doc comment — and must never be reordered relative
        /// to itself: ConfigureEnvironment (no RNG) → gravity (no RNG) →
        /// planet material pool (RNG) → primary galaxy + its arm decor (RNG)
        /// → each companion galaxy + its arm decor (RNG) → intergalactic
        /// halo (RNG) → decor POI anomalies (RNG) → asteroid drift (no RNG).
        /// </summary>
        public void BuildWorldFoundation(NebulaBootstrapConfig config, NebulaContext context, NebulaSessionLifetime lifetime)
        {
            ConfigureEnvironment();

            var gravityGO = new GameObject("Gravity System");
            lifetime.Track(SessionLifetime.CampaignSession, gravityGO);
            var gravity = gravityGO.AddComponent<GravitySystem>();
            gravity.SetGravitationalConstant(config.GravitationalConstant);
            context.SetGravity(gravity);

            List<Material> planetMaterialPool = BuildPlanetMaterialPool();
            context.SetPlanetMaterialPool(planetMaterialPool);

            var galaxyMaps = new List<GalaxyMap>();

            GameObject galaxyRoot = GalaxyGenerator.Generate(config.GalaxyName, config.GalaxyCenter, config.GalaxyRadius,
                config.GalaxyParticleCount, config.GalaxyArmCount, config.GalaxyRotationSpeedDegPerSec, config.BlackHoleRadius);
            lifetime.Track(SessionLifetime.CampaignSession, galaxyRoot);
            GalaxyMap primaryMap = galaxyRoot.GetComponent<GalaxyMap>();
            LocationRegistry.RegisterGalaxy(config.GalaxyName, primaryMap);
            galaxyMaps.Add(primaryMap);

            CreateArmDecorationStars(primaryMap, config.StarsPerArm, "A", config.ArmStarRadiusRange, lifetime);

            for (int g = 0; g < config.CompanionGalaxies.Length; g++)
            {
                NebulaBootstrapConfig.CompanionGalaxy companion = config.CompanionGalaxies[g];
                if (companion.Scale <= 0f) continue;

                GameObject companionRoot = GalaxyGenerator.Generate(companion.Name, companion.Center,
                    config.GalaxyRadius * companion.Scale, Mathf.RoundToInt(config.GalaxyParticleCount * companion.Scale),
                    config.GalaxyArmCount, config.GalaxyRotationSpeedDegPerSec, config.BlackHoleRadius * companion.Scale, companion.CloudWhiteness);
                lifetime.Track(SessionLifetime.CampaignSession, companionRoot);
                GalaxyMap companionMap = companionRoot.GetComponent<GalaxyMap>();
                LocationRegistry.RegisterGalaxy(companion.Name, companionMap);
                galaxyMaps.Add(companionMap);

                string namePrefix = ((char)('B' + g)).ToString();
                CreateArmDecorationStars(companionMap, Mathf.RoundToInt(config.StarsPerArm * companion.Scale), namePrefix, config.ArmStarRadiusRange, lifetime);
            }

            // Both of these build scene roots and neither was tracked, so the halo
            // and every decor wreck/anomaly outlived the session that made them: a
            // census over thirteen sessions found thirteen "Intergalactic Halo"
            // objects and thirteen of each "Derelict"/"Anomaly", all still being
            // shifted by the floating origin every frame. Nothing about how they are
            // generated changes — only who owns them.
            lifetime.Track(SessionLifetime.CampaignSession,
                IntergalacticHaloGenerator.Generate(galaxyMaps, config.HaloStarsPerGalaxy,
                    config.ClustersPerGalaxy, config.BridgeStarCount));

            foreach (GameObject poi in PointOfInterestGenerator.GenerateAnomalies(galaxyMaps, config.AnomalyCount))
                lifetime.Track(SessionLifetime.CampaignSession, poi);

            context.SetGalaxyMaps(galaxyMaps);
            // The decorative stars strung along every galaxy's arms, in absolute coordinates
            // captured before any floating-origin shift. The strategic map draws these as the
            // body of each galaxy so it shows the same sky the player flies through; they are
            // never navigation targets (VS-008 §4, REQ-116).
            context.SetDecorStarPositions(new List<Vector3>(decorPositions));

            var asteroidDriftGO = new GameObject("Asteroid Drift");
            lifetime.Track(SessionLifetime.CampaignSession, asteroidDriftGO);
            asteroidDriftGO.AddComponent<AsteroidDriftSystem>();
        }

        /// <summary>
        /// Composition step 6 (player-dependent tail): the starfield and its
        /// skybox bake need the camera <see cref="PlayerInstaller"/> just
        /// created, exactly as in the original ordering. Consumes no
        /// <see cref="Random"/> state (verified: neither generator touches it).
        /// </summary>
        public void BuildCameraPresentationBindings(NebulaBootstrapConfig config, NebulaContext context, NebulaSessionLifetime lifetime)
        {
            var starfield = new GameObject("Starfield").AddComponent<StarfieldGenerator>();
            lifetime.Track(SessionLifetime.CampaignSession, starfield.gameObject);
            starfield.Initialize(config.StarfieldStarCount, config.StarfieldRadius, config.NebulaCount);

            Camera camera = context.MainCamera;
            if (SkyboxBaker.TryBake(starfield.gameObject, camera.backgroundColor))
            {
                camera.clearFlags = CameraClearFlags.Skybox;
            }
        }

        /// <summary>Purely decorative background stars along each spiral arm — never registered as real navigation targets (VS-008 §4).</summary>
        private void CreateArmDecorationStars(GalaxyMap map, int starsPerArmCount, string namePrefix, Vector2 armStarRadiusRange, NebulaSessionLifetime lifetime)
        {
            if (map == null || starsPerArmCount <= 0) return;

            for (int arm = 0; arm < map.ArmCount; arm++)
            {
                for (int i = 0; i < starsPerArmCount; i++)
                {
                    Vector3 world = Vector3.zero;
                    bool found = false;
                    for (int attempt = 0; attempt < 8 && !found; attempt++)
                    {
                        float t = Mathf.Lerp(0.12f, 0.98f, (i + Random.value) / starsPerArmCount);
                        Vector3 local = map.ArmLocalPoint(arm, t);
                        float r = new Vector2(local.x, local.z).magnitude;
                        Vector2 jitter = Random.insideUnitCircle * (map.Radius * 0.006f + r * 0.02f);
                        local += new Vector3(jitter.x, Random.Range(-1f, 1f) * (map.Radius * 0.003f + r * 0.01f), jitter.y);
                        world = map.transform.TransformPoint(local);

                        found = true;
                        foreach (Vector3 existing in decorPositions)
                            if ((world - existing).sqrMagnitude < 250000f * 250000f) { found = false; break; }
                    }
                    if (!found) continue;

                    var preset = StarPresets[(arm * starsPerArmCount + i) % StarPresets.Length];
                    float starRadius = Random.Range(armStarRadiusRange.x, armStarRadiusRange.y);
                    string name = $"{namePrefix}{arm + 1}-{i + 1:00}";

                    var starGO = new GameObject(name + " Star (Decor)");
                    lifetime.Track(SessionLifetime.CampaignSession, starGO);
                    starGO.transform.position = world;
                    var star = starGO.AddComponent<Star>();
                    star.InitializeStar(starRadius, 1.4f, lightRange: starRadius * 6f,
                        preset.light, preset.low, preset.high, preset.glow);

                    LocationRegistry.RegisterStar(name, starGO.transform, starRadius * 8f);
                    decorPositions.Add(world);
                }
            }
        }

        private static List<Material> BuildPlanetMaterialPool(int poolSize = 24)
        {
            var pool = new List<Material>(poolSize);
            for (int i = 0; i < poolSize; i++)
            {
                float hue = Random.value;
                float saturation = Random.Range(0.25f, 0.6f);
                float value = Random.Range(0.55f, 0.95f);
                Color primary = Color.HSVToRGB(hue, saturation, value);
                Color secondary = Color.HSVToRGB(
                    Mathf.Repeat(hue + Random.Range(-0.12f, 0.12f), 1f), Mathf.Clamp01(saturation * 1.3f), value * 0.55f);
                Texture2D surface = MaterialLibrary.CreatePerlinTexture(256, secondary, primary, Random.Range(3f, 9f));
                pool.Add(MaterialLibrary.CreateLit(Color.white, Random.Range(0.05f, 0.3f), 0f, surface));
            }
            return pool;
        }

        private static void ConfigureEnvironment()
        {
            RenderSettings.skybox = null;
            Time.maximumDeltaTime = 0.05f;
            // The lighting director re-applies the same profile when it is
            // installed (step 10.7); doing it here too means the world is built
            // under the right ambient rather than flashing to it later.
            LightingEnvironment.Apply(LightingQualityProfile.FromQualitySettings());
        }
    }
}
