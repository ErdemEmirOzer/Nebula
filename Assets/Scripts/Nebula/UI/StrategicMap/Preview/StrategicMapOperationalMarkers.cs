using System.Collections.Generic;
using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// One compact procedural overlay for Fleet, Logistics, and Threat summaries.
    /// Every glyph represents a whole system, not a cloud of individual units: this keeps
    /// the galaxy readable while the underlying labels/counts still come from real data.
    /// </summary>
    public sealed class StrategicMapOperationalMarkers : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapOperationalMarker";

        private Mesh mesh;
        private Material material;
        private StrategicMapOperationalSnapshot snapshot = StrategicMapOperationalSnapshot.Empty;
        private string activeGalaxyId;
        private string activeSystemId;
        private bool fleetVisible;
        private bool logisticsVisible;
        private bool threatVisible = true;
        private float elapsed;

        private readonly List<StrategicMapOperationalMarker> visible =
            new List<StrategicMapOperationalMarker>();

        public int VisibleMarkerCount { get; private set; }
        public int FleetCount => snapshot?.Fleet?.Count ?? 0;
        public int LogisticsCount => snapshot?.Logistics?.Count ?? 0;
        public int ThreatCount => snapshot?.Threats?.Count ?? 0;

        public static StrategicMapOperationalMarkers Create(Transform parent,
            StrategicMapOperationalSnapshot initial)
        {
            var go = new GameObject("Operational Markers")
            {
                layer = StrategicMapUniverseRenderer.MapLayer
            };
            go.transform.SetParent(parent, false);
            var markers = go.AddComponent<StrategicMapOperationalMarkers>();
            markers.Initialise();
            markers.Refresh(initial);
            return markers;
        }

        private void Initialise()
        {
            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning("[StrategicMap] operational marker shader missing; operational layers will not draw");
                return;
            }

            material = new Material(shader)
            {
                name = "StrategicMapOperationalMarkers",
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetColor("_Highlight", StrategicMapTheme.SpaceText);

            mesh = new Mesh
            {
                name = "StrategicMapOperationalMarkers",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        public void Refresh(StrategicMapOperationalSnapshot fresh)
        {
            snapshot = fresh ?? StrategicMapOperationalSnapshot.Empty;
            Rebuild();
        }

        public void SetScope(string galaxyId, string systemId)
        {
            if (activeGalaxyId == galaxyId && activeSystemId == systemId) return;
            activeGalaxyId = galaxyId;
            activeSystemId = systemId;
            Rebuild();
        }

        public void SetLayerVisible(StrategicMapOperationalKind kind, bool value)
        {
            switch (kind)
            {
                case StrategicMapOperationalKind.Fleet: fleetVisible = value; break;
                case StrategicMapOperationalKind.Logistics: logisticsVisible = value; break;
                case StrategicMapOperationalKind.Threat: threatVisible = value; break;
            }
            Rebuild();
        }

        private void Rebuild()
        {
            visible.Clear();
            if (fleetVisible) AddVisible(snapshot.Fleet);
            if (logisticsVisible) AddVisible(snapshot.Logistics);
            if (threatVisible) AddVisible(snapshot.Threats);

            VisibleMarkerCount = visible.Count;
            gameObject.SetActive(VisibleMarkerCount > 0);
            if (mesh == null) return;
            mesh.Clear();
            if (VisibleMarkerCount == 0) return;

            var vertices = new Vector3[VisibleMarkerCount * 4];
            var uvs = new Vector2[VisibleMarkerCount * 4];
            var specs = new Vector2[VisibleMarkerCount * 4];
            var colors = new Color[VisibleMarkerCount * 4];
            var triangles = new int[VisibleMarkerCount * 6];

            Bounds bounds = new Bounds(visible[0].Position, Vector3.zero);
            for (int i = 0; i < VisibleMarkerCount; i++)
            {
                StrategicMapOperationalMarker marker = visible[i];
                Color color = ColorFor(marker.Kind);
                color.a = marker.IsLastKnown ? 0.54f : 0.92f;
                float flags = (marker.IsLastKnown ? 1f : 0f)
                              + (marker.IsSchematic ? 2f : 0f)
                              + (marker.NeedsAttention ? 4f : 0f);
                var spec = new Vector2((float)marker.Kind, flags);

                int v = i * 4;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices[v + corner] = marker.Position;
                    colors[v + corner] = color;
                    specs[v + corner] = spec;
                }
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);

                int t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
                bounds.Encapsulate(marker.Position);
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, specs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            bounds.Expand(Vector3.one * 2f);
            mesh.bounds = bounds;
        }

        private void AddVisible(IReadOnlyList<StrategicMapOperationalMarker> source)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                StrategicMapOperationalMarker marker = source[i];
                if (!string.IsNullOrEmpty(activeSystemId) && marker.SystemId != activeSystemId) continue;
                if (string.IsNullOrEmpty(activeSystemId)
                    && !string.IsNullOrEmpty(activeGalaxyId)
                    && marker.GalaxyId != activeGalaxyId) continue;
                visible.Add(marker);
            }
        }

        private static Color ColorFor(StrategicMapOperationalKind kind) => kind switch
        {
            StrategicMapOperationalKind.Fleet => StrategicMapTheme.Cyan,
            StrategicMapOperationalKind.Logistics => StrategicMapTheme.Green,
            _ => StrategicMapTheme.Red
        };

        private void Update()
        {
            if (material == null) return;
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
