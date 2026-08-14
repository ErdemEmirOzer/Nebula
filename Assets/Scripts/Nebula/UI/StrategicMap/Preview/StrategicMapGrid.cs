using System.Collections.Generic;
using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The universe reference plane: a faint blue ground grid plus two ring markers around
    /// each galaxy, giving the otherwise floating galaxies a sense of scale and depth.
    ///
    /// Straight from the reference's <c>drawGrid</c>: the grid spans x -20..30 and z -15..15
    /// in steps of 5 at y = -0.1, drawn in blue at alpha 0.032; each galaxy then gets a ring
    /// at half its radius (alpha 0.022) and one at its full radius (alpha 0.048), sixty-four
    /// segments each, at y = -0.12 in that galaxy's own colour.
    ///
    /// One line mesh, one draw call, built once.
    /// </summary>
    public sealed class StrategicMapGrid : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapLine";

        private const float XMin = -20f, XMax = 30f, ZMin = -15f, ZMax = 15f, Step = 5f;
        private const float GridY = -0.1f, RingY = -0.12f;
        private const int RingSegments = 64;

        /// <summary>
        /// The reference strokes these at sub-pixel widths (0.55 for the grid, 0.5 for the
        /// rings), so each line only covers part of a pixel. A GL line always covers a whole
        /// one, which would make the same alpha read roughly twice as bright — so the alpha is
        /// scaled by the reference's own line width to match the light it actually puts down.
        /// </summary>
        private const float GridLineWidth = 0.55f;
        private const float RingLineWidth = 0.5f;

        private Mesh mesh;
        private Material material;

        public int SegmentCount { get; private set; }

        public static StrategicMapGrid Create(Transform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("Grid") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var grid = go.AddComponent<StrategicMapGrid>();
            grid.Build(universe);
            return grid;
        }

        private void Build(StrategicMapUniverseSnapshot universe)
        {
            var vertices = new List<Vector3>(1024);
            var colors = new List<Color>(1024);

            Color gridColor = StrategicMapTheme.Blue;
            gridColor.a = 0.032f * GridLineWidth;

            for (float x = XMin; x <= XMax + 0.001f; x += Step) AddSegment(vertices, colors,
                new Vector3(x, GridY, ZMin), new Vector3(x, GridY, ZMax), gridColor);
            for (float z = ZMin; z <= ZMax + 0.001f; z += Step) AddSegment(vertices, colors,
                new Vector3(XMin, GridY, z), new Vector3(XMax, GridY, z), gridColor);

            for (int g = 0; g < universe.galaxies.Count; g++)
            {
                StrategicMapUniverseSnapshot.GalaxyRow galaxy = universe.galaxies[g];
                AddRing(vertices, colors, galaxy, galaxy.radius * 0.5f, 0.022f * RingLineWidth, g);
                AddRing(vertices, colors, galaxy, galaxy.radius, 0.048f * RingLineWidth, g);
            }

            SegmentCount = vertices.Count / 2;
            if (SegmentCount == 0) return;

            var indices = new int[vertices.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;

            mesh = new Mesh { name = "StrategicMapGrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.bounds = new Bounds(universe.UniverseCenter, Vector3.one * 200f);

            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null) return;
            material = new Material(shader) { name = "StrategicMapGrid", hideFlags = HideFlags.HideAndDontSave };

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private static void AddSegment(List<Vector3> vertices, List<Color> colors, Vector3 a, Vector3 b, Color color)
        {
            vertices.Add(a); vertices.Add(b);
            colors.Add(color); colors.Add(color);
        }

        private static void AddRing(List<Vector3> vertices, List<Color> colors,
            StrategicMapUniverseSnapshot.GalaxyRow galaxy, float radius, float alpha, int galaxyIndex)
        {
            Color color = StrategicMapGalaxyPoints.GalaxyPaletteColor(galaxyIndex, 0);
            color.a = alpha;

            Vector3 previous = default;
            for (int i = 0; i <= RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;
                var point = new Vector3(
                    galaxy.centerX + Mathf.Cos(angle) * radius,
                    RingY,
                    galaxy.centerZ + Mathf.Sin(angle) * radius);
                if (i > 0) AddSegment(vertices, colors, previous, point, color);
                previous = point;
            }
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
