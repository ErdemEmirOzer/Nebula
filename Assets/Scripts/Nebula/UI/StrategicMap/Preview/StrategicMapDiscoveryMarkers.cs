using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The deep-space discovery points: four derelict hulks and four anomalies, sitting
    /// between the galaxies rather than inside any system.
    ///
    /// The reference draws both as animated screen-space glyphs, and the animation is not
    /// decoration -- the derelict's slow turn and the anomaly's breathing rings are how you
    /// tell them apart from a star at a glance. Both glyphs live in one shader and one mesh,
    /// distinguished by a per-vertex variant flag, so the whole layer is a single draw call.
    ///
    /// The pulse phase is the object id's length, exactly as the reference uses it: that is
    /// what stops eight markers from breathing in unison.
    /// </summary>
    public sealed class StrategicMapDiscoveryMarkers : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapDiscoveryMarker";

        private Mesh mesh;
        private Material material;
        private float elapsed;

        public int MarkerCount { get; private set; }

        public static StrategicMapDiscoveryMarkers Create(Transform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("Discovery Markers") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var markers = go.AddComponent<StrategicMapDiscoveryMarkers>();
            markers.Build(universe);
            return markers;
        }

        private void Build(StrategicMapUniverseSnapshot universe)
        {
            int unidentified = universe.discoveries.Count;
            int derelicts = universe.derelicts.Count;
            int anomalies = universe.anomalies.Count;
            MarkerCount = unidentified + derelicts + anomalies;
            if (MarkerCount == 0) return;

            var vertices = new Vector3[MarkerCount * 4];
            var uvs = new Vector2[MarkerCount * 4];
            var specs = new Vector2[MarkerCount * 4];
            var colors = new Color[MarkerCount * 4];
            var triangles = new int[MarkerCount * 6];

            for (int i = 0; i < MarkerCount; i++)
            {
                bool unknown = i < unidentified;
                bool anomaly = !unknown && i >= unidentified + derelicts;
                StrategicMapUniverseSnapshot.MarkerRow row = unknown
                    ? universe.discoveries[i]
                    : anomaly
                        ? universe.anomalies[i - unidentified - derelicts]
                        : universe.derelicts[i - unidentified];

                // Reference: derelict: palette.red, anomaly: palette.violet.
                Color tint = unknown ? StrategicMapTheme.Cyan
                    : anomaly ? StrategicMapTheme.Violet : StrategicMapTheme.Gold;
                tint.a = 1f;

                // The reference seeds each marker's pulse with its own id length.
                var spec = new Vector2(row.id != null ? row.id.Length : 0, (unknown || anomaly) ? 1f : 0f);

                int v = i * 4;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices[v + corner] = row.Position;
                    colors[v + corner] = tint;
                    specs[v + corner] = spec;
                }
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);

                int t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }

            mesh = new Mesh { name = "StrategicMapDiscoveryMarkers" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, specs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(universe.UniverseCenter, Vector3.one * 200f);

            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning("[StrategicMap] discovery marker shader missing; derelicts and anomalies will not draw");
                return;
            }
            material = new Material(shader) { name = "StrategicMapDiscoveryMarkers", hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_Highlight", StrategicMapTheme.SpaceText);

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private void Update()
        {
            if (material == null) return;
            // Unscaled, so the markers keep their rhythm whatever the simulation is doing.
            if (!StrategicMapTheme.ReducedMotion) elapsed += Time.unscaledDeltaTime;
            material.SetFloat("_Time01", elapsed);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
