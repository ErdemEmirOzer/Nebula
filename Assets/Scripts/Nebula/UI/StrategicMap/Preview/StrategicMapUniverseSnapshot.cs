using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The static, read-only universe the strategic map renders during visual bring-up.
    ///
    /// It is the flattened form of the reference map's own exact snapshot (seed 12345),
    /// converted once by <c>Tools/convert-map-snapshot.py</c> into a JsonUtility-friendly
    /// asset. It is NOT a gameplay authority: nothing here is saved, ticked, or allowed to
    /// influence the simulation. When the map is wired to live data this whole type is
    /// replaced behind <see cref="IStrategicMapUniverseSource"/> and the renderer is untouched.
    /// </summary>
    [Serializable]
    public sealed class StrategicMapUniverseSnapshot
    {
        public int version;
        public int seed;
        public float shipX, shipY, shipZ;

        public int starfieldStarCount;
        public int haloStarsPerGalaxy;
        public int clustersPerGalaxy;
        public int bridgeStarCount;
        public int nebulaCount;
        public int totalAsteroids;

        public List<GalaxyRow> galaxies = new List<GalaxyRow>();
        public List<SystemRow> systems = new List<SystemRow>();
        public List<PlanetRow> planets = new List<PlanetRow>();
        /// <summary>
        /// The decorative arm stars the game itself strings along every galaxy's spiral.
        /// They are what makes a galaxy look like a galaxy full of stars from the cockpit,
        /// and the map draws them for exactly that reason — never as navigation targets, so
        /// they are deliberately NOT in <see cref="systems"/> and can never be picked.
        /// </summary>
        public List<MarkerRow> decorStars = new List<MarkerRow>();

        public List<ClusterRow> clusters = new List<ClusterRow>();
        public List<MarkerRow> derelicts = new List<MarkerRow>();
        public List<MarkerRow> anomalies = new List<MarkerRow>();
        /// <summary>Known POIs whose exact type has not been identified yet.</summary>
        public List<MarkerRow> discoveries = new List<MarkerRow>();

        public Vector3 ShipPosition => new Vector3(shipX, shipY, shipZ);

        /// <summary>The mean of the galaxy centres — the reference's own initial camera target.</summary>
        public Vector3 UniverseCenter
        {
            get
            {
                if (galaxies.Count == 0) return Vector3.zero;
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < galaxies.Count; i++) sum += galaxies[i].Center;
                return sum / galaxies.Count;
            }
        }

        [Serializable]
        public sealed class GalaxyRow
        {
            public string id;
            public string name;
            public float centerX, centerY, centerZ;
            public float radius;
            public int particles;
            public float blackHoleRadius;
            public float whiteness;
            public float phase;

            public Vector3 Center => new Vector3(centerX, centerY, centerZ);

            /// <summary>Every galaxy in the reference has four arms.</summary>
            public const int Arms = 4;

            /// <summary>The reference draws 2200 points per galaxy regardless of its particle budget.</summary>
            public const int PointCount = 2200;
        }

        [Serializable]
        public sealed class SystemRow
        {
            public string id;
            public string name;
            public string galaxyId;
            public float x, y, z;
            public float starRadius;
            /// <summary>The system's real navigation radius, in world units — the asteroid belt is measured from it exactly as the world measures it.</summary>
            public float navigationRadius;
            public int planetCount;
            public float firstOrbitRadius;
            public float outermostOrbit;
            public float maxInclination;
            public string preset;
            public string origin;
            public bool lightweight;
            public int asteroidCount;
            /// <summary>
            /// SystemKnowledgeLevel as an int. The map draws every real star's POSITION
            /// whatever this says — a star is something the player can see out of the
            /// window — and gates only what it IS. Anything below Located reads as
            /// undiscovered: no name, no star, no planets, drawn soft and unresolved.
            /// </summary>
            public int knowledge;

            /// <summary>
            /// The star's real <see cref="Sectors.StarClass"/> as an int. Never gated: a
            /// star's colour is what the player sees looking out of the window, and a field
            /// of identical yellow dots is both wrong and useless to navigate by.
            /// </summary>
            public int starClass;

            /// <summary>True once the player has actually located this system; false for a star they have merely seen.</summary>
            public bool Discovered => knowledge >= 2;
            /// <summary>Slice into <see cref="StrategicMapUniverseSnapshot.planets"/> — avoids a per-system list allocation.</summary>
            public int planetFrom, planetTo;

            public Vector3 Position => new Vector3(x, y, z);
        }

        [Serializable]
        public sealed class PlanetRow
        {
            public string id;
            public string name;
            public string systemId;
            public float radius;
            public float orbit;
            public float inclinationDegrees;
            public float ascendingNodeDegrees;
            public float phase;
            public float axialTilt;
            public float spinSpeed;
            public float speed;
            /// <summary>
            /// How fast this world sweeps around its star, in radians per second, taken
            /// straight from the world's own circular-orbit law
            /// (<see cref="Sectors.SystemMaterializer.OrbitalAngularSpeed"/>). The map animates
            /// at this rate, so a full turn on the map takes exactly as long as a full turn
            /// out of the cockpit window.
            /// </summary>
            public float orbitAngularSpeed;
            /// <summary>
            /// True when <see cref="phase"/> is the last authoritative observation and no
            /// domain clock exists that may advance it (for example a demoted system snapshot).
            /// </summary>
            public bool phaseFrozen;
            /// <summary>Scaled simulation time at which <see cref="phase"/> was sampled.</summary>
            public double phaseEpochSeconds;
            public int materialIndex;
            public float trailHue;
            public float initialX, initialY, initialZ;

            // Pre-computed by the converter from the reference's own extraction maths,
            // so the runtime never hashes anything.
            public string worldId;
            /// <summary>
            /// The planet's real resource family, which is what its 3D body is modelled
            /// from. Never gated: how a world LOOKS from orbit is not what a surface scan
            /// buys — the scan buys its yield, rarity and node layout, and those live on
            /// the gameplay snapshot the card reads, not here.
            /// </summary>
            public string resourceFamily;
            /// <summary>True once the surface has actually been scanned; an unscanned world is drawn unresolved.</summary>
            public bool scanned;
            public string rarity;
            /// <summary>
            /// The world's real <see cref="Sectors.EnvironmentClass"/> as an int. Carried so a
            /// detail card can be built for a system the gameplay snapshot has no row for —
            /// the map draws far more systems than the player has visited, and every one of
            /// them is a real place with a real environment.
            /// </summary>
            public int environment;
            public int t1Yield;
            public float productionPerMinute;
            public float localStorage;
            public bool recommendedFirstSetup;

            public Vector3 InitialPosition => new Vector3(initialX, initialY, initialZ);

            /// <summary>
            /// The rate the map turns this world at. The live universe fills
            /// <see cref="orbitAngularSpeed"/> from the world's own gravity; the retired
            /// reference's fixture carries only its own <see cref="speed"/>, so that stands
            /// in for it and the renderer never has to know which source it is drawing.
            /// </summary>
            public float AngularSpeed => orbitAngularSpeed > 0f ? orbitAngularSpeed : speed;

            /// <summary>Seconds for one full turn around the star — what the card states in plain words.</summary>
            public float OrbitPeriodSeconds => AngularSpeed > 0f ? 2f * Mathf.PI / AngularSpeed : 0f;
        }

        /// <summary>A globular cluster: a compact, dim ball of stars the reference draws as a soft nebula.</summary>
        [Serializable]
        public sealed class ClusterRow
        {
            public float x, y, z;
            public float radius;
            public int galaxyIndex;
            public Vector3 Position => new Vector3(x, y, z);
        }

        [Serializable]
        public sealed class MarkerRow
        {
            public string id;
            public string name;
            public string systemId;
            public string galaxyId;
            public int knowledge;
            /// <summary>PoiType/PoiRisk once identified; -1 while gated or for decor stars.</summary>
            public int type = -1;
            public int risk = -1;
            public float x, y, z;
            public Vector3 Position => new Vector3(x, y, z);
        }
    }

    /// <summary>
    /// The single seam between the map renderer and whatever supplies its universe.
    /// Today a static snapshot; later the live game authorities. Swapping the
    /// implementation must not require the renderer to change.
    /// </summary>
    public interface IStrategicMapUniverseSource
    {
        StrategicMapUniverseSnapshot Universe { get; }
    }

    /// <summary>Loads the converted snapshot asset from Resources exactly once per process.</summary>
    public sealed class StrategicMapStaticPreviewSource : IStrategicMapUniverseSource
    {
        public const string ResourcePath = "StrategicMap/universe-snapshot";

        private static StrategicMapUniverseSnapshot cached;

        public StrategicMapUniverseSnapshot Universe => Load();

        public static StrategicMapUniverseSnapshot Load()
        {
            if (cached != null) return cached;
            var text = Resources.Load<TextAsset>(ResourcePath);
            if (text == null)
            {
                Debug.LogWarning($"[StrategicMap] preview snapshot missing at Resources/{ResourcePath}");
                return cached = new StrategicMapUniverseSnapshot();
            }
            cached = JsonUtility.FromJson<StrategicMapUniverseSnapshot>(text.text)
                     ?? new StrategicMapUniverseSnapshot();
            return cached;
        }

        /// <summary>Test hook: forces the next <see cref="Load"/> to re-read the asset.</summary>
        public static void ClearCache() => cached = null;
    }
}
