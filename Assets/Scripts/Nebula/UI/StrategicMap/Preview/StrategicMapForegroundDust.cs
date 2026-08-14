using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The 128 motes drifting between the camera and the universe.
    ///
    /// This is the layer that sells depth: because the motes sit close to the camera on a
    /// wide ring, they slide across the frame far faster than the galaxies behind them, so
    /// the parallax reads as real distance rather than as a flat picture.
    ///
    /// Each mote breathes along its own phase, and the whole set is drawn additively over
    /// everything else, as in the reference.
    /// </summary>
    public sealed class StrategicMapForegroundDust : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapAdditivePoint";

        /// <summary>
        /// The reference draws a mote at size * 50 / depth pixels of radius, clamped to
        /// 0.65..6.5. A quad half-extent s lands at s * focal / depth pixels, and focal at the
        /// reference's own 1080-tall view is (1080/2) / tan(25 degrees) = 1158. So the same
        /// mote is s = size * 50 / 1158, with the clamp carried over as a full-side range.
        /// </summary>
        private const float SizeToHalfExtent = 50f / 1158f;
        private const float MinPixelSize = 0.65f * 2f;
        private const float MaxPixelSize = 6.5f * 2f;

        /// <summary>Reference: float = sin(time * 0.08 + phase) * 0.22, and z moves against x.</summary>
        private const float DriftRate = 0.08f;
        private const float DriftAmplitude = 0.22f;

        private Mesh mesh;
        private Material material;
        private StrategicSpacePoint[] motes = new StrategicSpacePoint[0];
        private Vector3[] vertices = new Vector3[0];
        private float elapsed;

        public int MoteCount => motes.Length;

        public static StrategicMapForegroundDust Create(Transform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("Foreground Dust") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var dust = go.AddComponent<StrategicMapForegroundDust>();
            dust.Build(universe);
            return dust;
        }

        private void Build(StrategicMapUniverseSnapshot universe)
        {
            motes = StrategicMapSpaceLayers.ForegroundDust(universe);
            if (motes.Length == 0) return;

            vertices = new Vector3[motes.Length * 4];
            var uvs = new Vector2[motes.Length * 4];
            var sizes = new Vector2[motes.Length * 4];
            var colors = new Color[motes.Length * 4];
            var triangles = new int[motes.Length * 6];

            for (int i = 0; i < motes.Length; i++)
            {
                // Reference: every third mote is cyan, the rest near-white, both at 0.5 alpha
                // before the mote's own opacity.
                Color tint = motes[i].ColorIndex == 1 ? StrategicMapTheme.Cyan : StrategicMapTheme.SpaceText;
                tint.a = motes[i].Alpha * 0.5f;

                float half = motes[i].Size * SizeToHalfExtent;
                int v = i * 4;
                for (int corner = 0; corner < 4; corner++)
                {
                    colors[v + corner] = tint;
                    sizes[v + corner] = new Vector2(half, half);
                }
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);

                int t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }

            WritePositions(0f);

            mesh = new Mesh { name = "StrategicMapForegroundDust", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, sizes);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            // The motes ring the whole universe and are always in shot; a generous manual
            // bounds keeps Unity from culling the set as the camera orbits.
            mesh.bounds = new Bounds(universe.UniverseCenter, Vector3.one * 200f);

            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning("[StrategicMap] additive point shader missing; foreground dust will not draw");
                return;
            }
            material = new Material(shader) { name = "StrategicMapForegroundDust", hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_ScreenSpaceSize", 0f);
            material.SetFloat("_MinPixelSize", MinPixelSize);
            material.SetFloat("_MaxPixelSize", MaxPixelSize);
            // Drawn last: these motes are in front of everything, including the markers.
            material.renderQueue = 3500;

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private void WritePositions(float time)
        {
            for (int i = 0; i < motes.Length; i++)
            {
                float drift = Mathf.Sin(time * DriftRate + motes[i].Phase) * DriftAmplitude;
                Vector3 position = motes[i].Position + new Vector3(drift, 0f, -drift * 0.6f);
                int v = i * 4;
                vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = position;
            }
        }

        private void Update()
        {
            if (mesh == null || motes.Length == 0) return;
            if (StrategicMapTheme.ReducedMotion) return;
            elapsed += Time.unscaledDeltaTime;
            WritePositions(elapsed);
            mesh.SetVertices(vertices);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
