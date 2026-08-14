using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The reticle over the selected object and the ring under the hovered one.
    ///
    /// Both are one two-quad mesh that follows its targets every frame rather than being
    /// rebuilt: a planet moves along its orbit, and a reticle that only updated on selection
    /// would slide off it.
    ///
    /// A quad with nothing to mark is switched off through its vertex alpha, not by moving
    /// its corners together: the shader expands the quad in pixels around whatever position
    /// it is given, so coincident corners still produce a full-size ring -- at the world
    /// origin, which is inside the universe.
    /// </summary>
    public sealed class StrategicMapSelectionOverlay : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapReticle";

        /// <summary>Reference: drawSelectionReticle(projected, base, time, radius + 9).</summary>
        private const float SelectedRingPixels = 21f;
        private const float HoverRingPixels = 22f;

        private Mesh mesh;
        private Material material;
        private Vector3[] vertices = new Vector3[8];
        private Color[] colors = new Color[8];
        private float elapsed;

        private Vector3 selectedPosition;
        private Vector3 hoverPosition;
        private bool hasSelected;
        private bool hasHover;

        public bool HasSelection => hasSelected;
        public bool HasHover => hasHover;

        public static StrategicMapSelectionOverlay Create(Transform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("Selection Overlay") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var overlay = go.AddComponent<StrategicMapSelectionOverlay>();
            overlay.Build(universe);
            return overlay;
        }

        private void Build(StrategicMapUniverseSnapshot universe)
        {
            var uvs = new Vector2[8];
            var specs = new Vector2[8];
            var triangles = new int[12];

            for (int quad = 0; quad < 2; quad++)
            {
                bool selected = quad == 0;
                int v = quad * 4;
                for (int corner = 0; corner < 4; corner++)
                {
                    specs[v + corner] = new Vector2(selected ? SelectedRingPixels : HoverRingPixels,
                                                    selected ? 1f : 0f);
                    // Hover is an actionable navigation affordance, not only a pale halo.
                    // Cyan keeps it distinct from both warm stars and white background dust.
                    colors[v + corner] = StrategicMapTheme.Cyan;
                }
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);

                int t = quad * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }

            mesh = new Mesh { name = "StrategicMapSelectionOverlay" };
            WriteGeometry();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, specs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(universe.UniverseCenter, Vector3.one * 400f);

            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning("[StrategicMap] reticle shader missing; selection will not be marked");
                return;
            }
            material = new Material(shader) { name = "StrategicMapSelectionOverlay", hideFlags = HideFlags.HideAndDontSave };

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>Marks a world position as selected, or clears the reticle when told nothing.</summary>
        public void SetSelected(Vector3? position)
        {
            hasSelected = position.HasValue;
            if (hasSelected) selectedPosition = position.Value;
        }

        /// <summary>Marks a world position as hovered, or clears the ring.</summary>
        public void SetHovered(Vector3? position)
        {
            hasHover = position.HasValue;
            if (hasHover) hoverPosition = position.Value;
        }

        private void WriteGeometry()
        {
            for (int corner = 0; corner < 4; corner++)
            {
                vertices[corner] = hasSelected ? selectedPosition : Vector3.zero;
                vertices[4 + corner] = hasHover ? hoverPosition : Vector3.zero;
                colors[corner].a = hasSelected ? 1f : 0f;
                colors[4 + corner].a = hasHover ? 1f : 0f;
            }
        }

        private void Update()
        {
            if (mesh == null) return;
            if (!StrategicMapTheme.ReducedMotion) elapsed += Time.unscaledDeltaTime;
            if (material != null) material.SetFloat("_Time01", elapsed);

            // Followed every frame: an orbiting planet would otherwise leave its reticle behind.
            WriteGeometry();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
