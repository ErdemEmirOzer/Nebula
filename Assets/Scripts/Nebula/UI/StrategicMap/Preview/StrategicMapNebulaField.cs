using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The nebula volumes and globular clusters: large, very dim, squashed additive blobs
    /// seated on the spiral arms.
    ///
    /// These are what give the galaxies their colour wash rather than reading as bare point
    /// clouds. Each is one camera-facing quad with a soft radial falloff, squashed on its
    /// minor axis and rotated exactly as the reference specifies. Alphas are tiny by design
    /// (0.012-0.034) — the effect comes from many of them overlapping additively, which is
    /// the same thing the reference's "lighter" compositing does.
    ///
    /// All of them are one mesh, one draw call, built once.
    /// </summary>
    public sealed class StrategicMapNebulaField : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapAdditivePoint";

        private Mesh mesh;
        private Material material;

        public int CloudCount { get; private set; }

        public static StrategicMapNebulaField Create(Transform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("Nebula Field") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var field = go.AddComponent<StrategicMapNebulaField>();
            field.Build(universe);
            return field;
        }

        private void Build(StrategicMapUniverseSnapshot universe)
        {
            StrategicNebulaCloud[] clouds = StrategicMapSpaceLayers.Nebulae(universe);
            CloudCount = clouds.Length;
            if (CloudCount == 0) return;

            var vertices = new Vector3[CloudCount * 4];
            var uvs = new Vector2[CloudCount * 4];
            var colors = new Color[CloudCount * 4];
            var sizes = new Vector2[CloudCount * 4];
            var triangles = new int[CloudCount * 6];

            for (int i = 0; i < CloudCount; i++)
            {
                StrategicNebulaCloud cloud = clouds[i];
                Color tint = StrategicMapGalaxyPoints.GalaxyPaletteColor(cloud.GalaxyIndex, cloud.ColorIndex);
                tint.a = cloud.Alpha;

                // The rotation is baked into the quad's corner offsets, so the mesh never has
                // to be rebuilt and every cloud still keeps its own orientation.
                float sin = Mathf.Sin(cloud.Rotation);
                float cos = Mathf.Cos(cloud.Rotation);

                int v = i * 4;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices[v + corner] = cloud.Position;
                    colors[v + corner] = tint;
                }
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);

                // Half-extents along the cloud's own rotated axes: full radius on the major
                // axis, squashed on the minor one.
                var half = new Vector2(cloud.Radius, cloud.Radius * cloud.Squash);
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector2 c = uvs[v + corner] * 2f - Vector2.one;
                    Vector2 scaled = new Vector2(c.x * half.x, c.y * half.y);
                    Vector2 rotated = new Vector2(scaled.x * cos - scaled.y * sin, scaled.x * sin + scaled.y * cos);
                    // The shader multiplies uv-corner by size, so store the per-corner extent
                    // it needs to reproduce this rotated, squashed offset.
                    sizes[v + corner] = new Vector2(
                        c.x != 0f ? rotated.x / c.x : rotated.x,
                        c.y != 0f ? rotated.y / c.y : rotated.y);
                }

                int t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }

            mesh = new Mesh { name = "StrategicMapNebulaField", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, sizes);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(universe.UniverseCenter, Vector3.one * 800f);

            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null) return;
            material = new Material(shader) { name = "StrategicMapNebulaField", hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_ScreenSpaceSize", 0f);   // real world-space volumes
            material.SetFloat("_Softness", 0.95f);       // very soft, cloud-like falloff
            material.SetFloat("_Intensity", 1.4f);

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
