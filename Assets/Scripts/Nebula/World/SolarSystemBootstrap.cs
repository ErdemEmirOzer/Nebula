using UnityEngine;
using Nebula.World.Composition;

namespace Nebula.World
{
    /// <summary>
    /// Single scene entry point. TF-002: this class is now a thin
    /// compatibility adapter only — it owns exactly the Inspector-serialized
    /// fields the scene's saved values depend on (same names, same types,
    /// same defaults, so <c>Assets/Scenes/Nebula.unity</c>'s serialized
    /// values keep resolving unchanged) and delegates every actual boot
    /// responsibility to <see cref="NebulaBootstrap"/>, which owns
    /// composition order across six installers and a typed
    /// <see cref="NebulaContext"/>. See VS-008+ comments in the composition
    /// classes for how streaming/materialization still works; this file no
    /// longer contains any of that logic itself.
    /// </summary>
    /// <summary>
    /// What a scene does when it loads.
    /// <para>
    /// A bool called <c>deferToMainMenu</c> could only ever say "not the normal
    /// thing", which is how the production default ended up being the test's
    /// default: nobody wants to be the one who sets a negative flag to true.
    /// Naming both states makes the production one sayable.
    /// </para>
    /// </summary>
    public enum NebulaBootMode
    {
        /// <summary>Production. No world exists until the player asks for one.</summary>
        MainMenu = 0,

        /// <summary>Integration tests that want the world without driving the menu.</summary>
        GameplayDirect = 1,
    }

    public class SolarSystemBootstrap : MonoBehaviour
    {
        [Header("Simulation")]
        [Tooltip("0 = a different random layout every run; any other value reproduces the same universe.")]
        [SerializeField] private int seed = 12345;
        // Low G keeps the huge planets sweeping slowly and majestically.
        [SerializeField] private float gravitationalConstant = 0.015f;

        [Header("Arm Decoration")]
        [Tooltip("Purely decorative background stars seeded along each spiral arm — never real navigation targets (VS-008 §4).")]
        [SerializeField] private int starsPerArm = 10;
        [SerializeField] private Vector2 armStarRadiusRange = new Vector2(3000f, 7000f);

        [Header("Galaxy")]
        [SerializeField] private string galaxyName = "Aetheria";
        [Tooltip("Core position; far enough that our systems sit mid-way out one spiral arm.")]
        [SerializeField] private Vector3 galaxyCenter = new Vector3(-4500000f, 0f, 0f);
        [SerializeField] private float galaxyRadius = 10000000f;
        [SerializeField] private int galaxyParticleCount = 150000;
        [SerializeField] private int galaxyArmCount = 4;
        [SerializeField] private float galaxyRotationSpeedDegPerSec = 0.05f;
        [SerializeField] private float blackHoleRadius = 120000f;

        [Header("Companion Galaxies")]
        [Tooltip("Additional galaxies built by the same generator. Scale is uniform relative to the primary: radius, particles, arm stars and black hole. Scale 0 disables an entry.")]
        [SerializeField] private CompanionGalaxySettings[] companionGalaxies =
        {
            new CompanionGalaxySettings { name = "Elysia", scale = 0.75f, center = new Vector3(-4500000f, 4000000f, 0f), cloudWhiteness = 0.35f },
            new CompanionGalaxySettings { name = "Nyxara", scale = 0.6f, center = new Vector3(10500000f, 4000000f, 0f), cloudWhiteness = 0.1f },
            new CompanionGalaxySettings { name = "Thalassa", scale = 0.8f, center = new Vector3(14500000f, 0f, 0f), cloudWhiteness = 0.2f }
        };

        // TF-002 §3: kept here rather than promoted to a shared type — the
        // Inspector's serialized array data is keyed to THIS struct's exact
        // shape/name/field order. NebulaBootstrapConfig.CompanionGalaxy is a
        // separate, independent struct that BuildConfig() copies values
        // into; nothing outside this adapter ever sees this one.
        [System.Serializable]
        private struct CompanionGalaxySettings
        {
            public string name;
            public float scale;
            public Vector3 center;
            [Range(0f, 1f)] public float cloudWhiteness;
        }

        [Header("Intergalactic Halo")]
        [SerializeField] private int haloStarsPerGalaxy = 2200;
        [SerializeField] private int clustersPerGalaxy = 4;
        [SerializeField] private int bridgeStarCount = 1200;

        [Header("Points of Interest")]
        [SerializeField] private int anomalyCount = 8;

        [Header("Visuals")]
        [SerializeField] private bool showOrbitRings = true;
        [SerializeField] private bool showPlanetTrails = true;
        [SerializeField] private int starfieldStarCount = 12000;
        [SerializeField] private float starfieldRadius = 500000f;
        [SerializeField] private int nebulaCount = 26;

        [Header("Session")]
        [Tooltip("MainMenu is the production flow: no world exists until the player asks for one. " +
                 "GameplayDirect skips the menu and is for integration tests that want the world.")]
        [SerializeField] private NebulaBootMode bootMode = NebulaBootMode.MainMenu;

        private NebulaBootstrap session;

        /// <summary>The active session's typed context, or null before the first <see cref="Awake"/>/after a <see cref="RestartCampaign"/> dispose mid-swap. Exposed for tests and any future debug tooling — never mutated from outside composition.</summary>
        public NebulaContext Context => session?.Context;

        /// <summary>True while a session is live. False before the main menu starts one, and after <see cref="EndSession"/>.</summary>
        public bool SessionActive => session != null;

        /// <summary>
        /// How this scene boots. Production is <see cref="NebulaBootMode.MainMenu"/>:
        /// no world, no player and no directors exist until the player asks for a
        /// campaign, which is the only way "New Campaign" can mean anything.
        /// <para>
        /// A test that wants the world does not flip this. It calls
        /// <see cref="BeginSession"/> after loading the scene — the same idempotent
        /// entry the menu's own button calls — so the production default is never
        /// something a test has an opinion about, and nothing depends on a global
        /// flag or on which test ran first.
        /// </para>
        /// </summary>
        public NebulaBootMode BootMode
        {
            get => bootMode;
            set => bootMode = value;
        }

        private void Awake()
        {
            if (bootMode == NebulaBootMode.MainMenu)
            {
                // Without this the flag produced a black screen: no session meant no
                // presentation, no presentation meant no shell, and the main menu had
                // nowhere to live. The host carries its own shell until a campaign
                // begins, then stands down.
                UI.Session.NebulaMainMenuHost.Create(this);
                return;
            }

            session = NebulaBootstrap.StartSession(BuildConfig());
        }

        /// <summary>
        /// Starts a session on demand. Idempotent: calling it while one is already
        /// running is a no-op rather than a second universe, because a menu button
        /// pressed twice must not cost the player their campaign.
        /// </summary>
        public NebulaContext BeginSession()
        {
            if (session != null) return session.Context;
            session = NebulaBootstrap.StartSession(BuildConfig());
            return session.Context;
        }

        /// <summary>
        /// Disposes the running session and returns to a menu-capable state without
        /// destroying this adapter. Safe to call when nothing is running.
        /// </summary>
        public void EndSession()
        {
            session?.Dispose();
            session = null;

            // Disposing the world is only half of leaving a campaign. The menu host
            // stands itself down when a session starts and nothing ever brought it
            // back, so ending one left the player looking at an empty screen with no
            // way forward. In MainMenu boot mode the menu is the thing that exists
            // when a session does not -- that is the whole contract, and it has to
            // hold in both directions.
            if (bootMode != NebulaBootMode.MainMenu) return;
            if (UI.Session.NebulaMainMenuHost.Active != null) return;
            UI.Session.NebulaMainMenuHost.Create(this);
        }

        private void OnDestroy()
        {
            session?.Dispose();
        }

        /// <summary>
        /// Disposes the current session (if any) and starts a brand new one
        /// from this adapter's current Inspector values — same contract a
        /// future main menu "New Campaign" button could call directly. Not
        /// wired to any input/UI in this slice.
        /// </summary>
        public void RestartCampaign()
        {
            session?.Dispose();
            session = NebulaBootstrap.StartSession(BuildConfig());
        }

        private NebulaBootstrapConfig BuildConfig()
        {
            var companions = new NebulaBootstrapConfig.CompanionGalaxy[companionGalaxies.Length];
            for (int i = 0; i < companionGalaxies.Length; i++)
            {
                CompanionGalaxySettings source = companionGalaxies[i];
                companions[i] = new NebulaBootstrapConfig.CompanionGalaxy
                {
                    Name = source.name,
                    Scale = source.scale,
                    Center = source.center,
                    CloudWhiteness = source.cloudWhiteness
                };
            }

            return new NebulaBootstrapConfig
            {
                Seed = seed,
                GravitationalConstant = gravitationalConstant,
                StarsPerArm = starsPerArm,
                ArmStarRadiusRange = armStarRadiusRange,
                GalaxyName = galaxyName,
                GalaxyCenter = galaxyCenter,
                GalaxyRadius = galaxyRadius,
                GalaxyParticleCount = galaxyParticleCount,
                GalaxyArmCount = galaxyArmCount,
                GalaxyRotationSpeedDegPerSec = galaxyRotationSpeedDegPerSec,
                BlackHoleRadius = blackHoleRadius,
                CompanionGalaxies = companions,
                HaloStarsPerGalaxy = haloStarsPerGalaxy,
                ClustersPerGalaxy = clustersPerGalaxy,
                BridgeStarCount = bridgeStarCount,
                AnomalyCount = anomalyCount,
                ShowOrbitRings = showOrbitRings,
                ShowPlanetTrails = showPlanetTrails,
                StarfieldStarCount = starfieldStarCount,
                StarfieldRadius = starfieldRadius,
                NebulaCount = nebulaCount
            };
        }
    }
}
