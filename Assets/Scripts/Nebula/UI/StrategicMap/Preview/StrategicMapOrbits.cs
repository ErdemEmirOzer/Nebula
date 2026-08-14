using System.Collections.Generic;
using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// Every planet's orbit path, and the planets themselves, for the systems the map shows.
    ///
    /// The orbital maths is the reference's own <c>planetPosition</c>, kept exactly:
    /// a circle of the planet's orbit radius, tilted by its inclination and then rotated
    /// about its ascending node. Because that is a pure function of (phase, orbit,
    /// inclination, ascendingNode), the orbit ring is just the same function swept through
    /// a full turn — so the ring and the planet on it can never disagree.
    ///
    /// At universe distance the reference draws systems as single markers, not as orbital
    /// diagrams. Drawing 548 orbits over the whole galaxy would be both wrong and slow, so
    /// this layer is built for the currently-focused system only and rebuilt when that
    /// changes — not every frame.
    /// </summary>
    public sealed class StrategicMapOrbits : MonoBehaviour
    {
        private const string LineShaderPath = "StrategicMap/StrategicMapLine";
        private const int OrbitSegments = 96;

        /// <summary>The reference's MAP_UNIT: game units per map unit.</summary>
        public const float MapUnit = 1000000f;

        /// <summary>
        /// The reference's systemVisualScale: clamp(0.62 / belt.radius, 1.65, 32), where the
        /// belt radius is the outermost orbit widened by 1.15. Shared with the asteroid belt,
        /// which has to blow its rocks up by exactly the same factor or the ring would sit in
        /// the wrong place relative to the orbits.
        /// </summary>
        public static float VisualScale(StrategicMapUniverseSnapshot.SystemRow system) =>
            Mathf.Clamp(0.62f / Mathf.Max(system.outermostOrbit * 1.15f / MapUnit, 0.001f), 1.65f, 32f);

        private Mesh orbitMesh;
        private Material orbitMaterial;
        private string builtForSystemId;

        public int OrbitCount { get; private set; }

        public static StrategicMapOrbits Create(Transform parent)
        {
            var go = new GameObject("Orbits") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var orbits = go.AddComponent<StrategicMapOrbits>();
            orbits.Initialise();
            go.SetActive(false); // nothing to draw until a system is focused
            return orbits;
        }

        private void Initialise()
        {
            orbitMaterial = MakeMaterial(LineShaderPath, "StrategicMapOrbits");
            gameObject.AddComponent<MeshFilter>();
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = orbitMaterial;
            StripLighting(meshRenderer);

        }

        /// <summary>Shows one system's orbits and planets. Rebuilds only when the system actually changes.</summary>
        public void ShowSystem(StrategicMapUniverseSnapshot universe, string systemId)
        {
            if (string.IsNullOrEmpty(systemId))
            {
                builtForSystemId = null;
                gameObject.SetActive(false);
                return;
            }
            if (systemId == builtForSystemId) { gameObject.SetActive(true); return; }

            StrategicMapUniverseSnapshot.SystemRow system = null;
            for (int i = 0; i < universe.systems.Count; i++)
                if (universe.systems[i].id == systemId) { system = universe.systems[i]; break; }
            if (system == null) { gameObject.SetActive(false); return; }

            Build(universe, system);
            builtForSystemId = systemId;
            gameObject.SetActive(true);
        }

        private void Build(StrategicMapUniverseSnapshot universe, StrategicMapUniverseSnapshot.SystemRow system)
        {
            float visualScale = VisualScale(system);

            var orbitVertices = new List<Vector3>(OrbitSegments * 8);
            var orbitColors = new List<Color>(OrbitSegments * 8);

            int planetCount = Mathf.Max(0, system.planetTo - system.planetFrom);

            Color orbitColor = StrategicMapTheme.Cyan;
            orbitColor.a = 0.10f;

            for (int p = 0; p < planetCount; p++)
            {
                StrategicMapUniverseSnapshot.PlanetRow planet = universe.planets[system.planetFrom + p];

                // The orbit ring is the planet's own position function swept a full turn, so
                // the path and the body can never disagree.
                Vector3 previous = default;
                for (int s = 0; s <= OrbitSegments; s++)
                {
                    float phase = s / (float)OrbitSegments * Mathf.PI * 2f;
                    Vector3 point = OrbitPoint(system.Position, planet, phase, visualScale);
                    if (s > 0)
                    {
                        orbitVertices.Add(previous); orbitVertices.Add(point);
                        orbitColors.Add(orbitColor); orbitColors.Add(orbitColor);
                    }
                    previous = point;
                }
            }

            OrbitCount = planetCount;

            if (orbitMesh == null) orbitMesh = new Mesh { name = "StrategicMapOrbits", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            orbitMesh.Clear();
            orbitMesh.SetVertices(orbitVertices);
            orbitMesh.SetColors(orbitColors);
            var orbitIndices = new int[orbitVertices.Count];
            for (int i = 0; i < orbitIndices.Length; i++) orbitIndices[i] = i;
            orbitMesh.SetIndices(orbitIndices, MeshTopology.Lines, 0);
            orbitMesh.bounds = new Bounds(system.Position, Vector3.one * 4f);
            GetComponent<MeshFilter>().sharedMesh = orbitMesh;

        }

        /// <summary>The reference's planetPosition, kept exactly: circle, tilt by inclination, rotate about the ascending node.</summary>
        public static Vector3 OrbitPoint(Vector3 systemPosition, StrategicMapUniverseSnapshot.PlanetRow planet,
            float phase, float visualScale)
        {
            float orbit = planet.orbit * visualScale;
            float localX = Mathf.Cos(phase) * orbit;
            float localZ = Mathf.Sin(phase) * orbit;

            float inclination = planet.inclinationDegrees * Mathf.Deg2Rad;
            float tiltedY = -localZ * Mathf.Sin(inclination);
            float tiltedZ = localZ * Mathf.Cos(inclination);

            float node = planet.ascendingNodeDegrees * Mathf.Deg2Rad;
            float cosNode = Mathf.Cos(node);
            float sinNode = Mathf.Sin(node);

            return new Vector3(
                systemPosition.x + localX * cosNode + tiltedZ * sinNode,
                systemPosition.y + tiltedY,
                systemPosition.z - localX * sinNode + tiltedZ * cosNode);
        }


        private static Material MakeMaterial(string resourcePath, string name)
        {
            Shader shader = Resources.Load<Shader>(resourcePath);
            return shader == null ? null : new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
        }

        private static void StripLighting(MeshRenderer meshRenderer)
        {
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private void OnDestroy()
        {
            if (orbitMesh != null) Destroy(orbitMesh);
            if (orbitMaterial != null) Destroy(orbitMaterial);
        }
    }
}
