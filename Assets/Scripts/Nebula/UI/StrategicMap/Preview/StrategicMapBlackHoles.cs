using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The supermassive black hole sitting at each galactic core — one per galaxy, four in
    /// total, drawn as screen-facing billboards by <c>Nebula/StrategicMapBlackHole</c>.
    ///
    /// The reference sizes these in screen pixels after projection (base radius 7.5, scaled
    /// by camera distance and clamped), and paints the accretion disk as a screen-space
    /// ellipse. The billboard reproduces that: constant on-screen size, with the whole disk,
    /// jets, horizon and photon ring built in the fragment shader.
    ///
    /// The disk rotates at the reference's own rate on unscaled time, and stops dead when
    /// reduced motion is on.
    /// </summary>
    public sealed class StrategicMapBlackHoles : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapBlackHole";

        /// <summary>Reference: diskRotation = -0.12 + time * 0.026.</summary>
        private const float BaseRotation = -0.12f;
        private const float RotationSpeed = 0.026f;

        private Mesh mesh;
        private Material material;
        private float elapsed;

        public int Count { get; private set; }

        public static StrategicMapBlackHoles Create(Transform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("Black Holes") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var holes = go.AddComponent<StrategicMapBlackHoles>();
            holes.Build(universe);
            return holes;
        }

        private void Build(StrategicMapUniverseSnapshot universe)
        {
            Count = universe.galaxies.Count;
            if (Count == 0) return;

            var vertices = new Vector3[Count * 4];
            var uvs = new Vector2[Count * 4];
            var colors = new Color[Count * 4];
            var sizes = new Vector2[Count * 4];
            var triangles = new int[Count * 6];

            for (int i = 0; i < Count; i++)
            {
                StrategicMapUniverseSnapshot.GalaxyRow galaxy = universe.galaxies[i];

                // The quad spans the reference's `outer` extent (radius * 3.15); the shader
                // places the event horizon at 1/3.15 of that, so the proportions match.
                //
                // Derived, not eyeballed: in screen-space mode the on-screen half-height
                // fraction is size / tan(fov/2) = size / 0.466. The reference clamps its core
                // to 10px and draws `outer` at 3.15x that, i.e. ~31px of a 480px view, so
                // size = 0.065 * 0.466 ~= 0.055.
                const float ScreenHalfExtent = 0.055f;

                int v = i * 4;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices[v + corner] = galaxy.Center;
                    colors[v + corner] = Color.white;
                    sizes[v + corner] = new Vector2(ScreenHalfExtent, ScreenHalfExtent);
                }
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);

                int t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }

            mesh = new Mesh { name = "StrategicMapBlackHoles" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, sizes);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(universe.UniverseCenter, Vector3.one * 200f);

            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning("[StrategicMap] black hole shader missing; cores will not draw");
                return;
            }
            material = new Material(shader) { name = "StrategicMapBlackHoles", hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_Gold", StrategicMapTheme.Gold);
            material.SetColor("_Red", StrategicMapTheme.Red);
            material.SetColor("_Blue", StrategicMapTheme.Blue);
            material.SetColor("_SpaceText", StrategicMapTheme.SpaceText);

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
            // Unscaled: the map animates at the same rate whatever the simulation is doing.
            if (!StrategicMapTheme.ReducedMotion) elapsed += Time.unscaledDeltaTime;
            material.SetFloat("_Rotation", BaseRotation + elapsed * RotationSpeed);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
