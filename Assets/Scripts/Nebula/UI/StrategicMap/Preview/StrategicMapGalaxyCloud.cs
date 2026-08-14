using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// Draws every galaxy particle as one additive, camera-facing quad mesh.
    ///
    /// All four galaxies (4 × 2200 points) go into a single mesh built once, so the
    /// whole cloud is one draw call with no per-frame allocation and no per-point
    /// GameObject. Billboarding happens in the shader, so the mesh never needs
    /// rebuilding when the camera orbits.
    /// </summary>
    public sealed class StrategicMapGalaxyCloud : MonoBehaviour
    {
        private const string ShaderName = "Nebula/StrategicMapAdditivePoint";
        /// <summary>Loading from Resources is the reliable path: Shader.Find only sees shaders already pulled into the build.</summary>
        private const string ShaderResourcePath = "StrategicMap/StrategicMapAdditivePoint";

        private Mesh mesh;
        private Material material;

        public int PointCount { get; private set; }

        /// <summary>
        /// How much larger the galaxy dust is drawn than the reference draws it.
        ///
        /// A deliberate departure, asked for and not a mistake: the reference's own clamp is
        /// 0.55..2.4 pixels, which at a true 1080 gives a very fine dust and arms that read as
        /// a faint haze. The chunkier arms wanted here are what the map looked like while
        /// every capture was accidentally 640x480 -- a frame 2.25 times shorter, so every
        /// pixel budget in it went 2.25 times further. That ratio is the factor, rather than
        /// a number picked until it looked right: it reproduces exactly the grain that was
        /// being seen, now at the real resolution and on purpose.
        /// </summary>
        private const float ArmProminence = 2.25f;

        /// <summary>
        /// The reference draws a point as a square of side size*(focal/depth)*0.58, and our
        /// quad's half-extent s gives a side of s*H/(t*depth) where focal = H/(2t) -- so the
        /// two agree exactly at s = size * 0.29. Scaled by the same prominence factor, since
        /// raising only the clamp would make every point sit pinned at the maximum and lose
        /// the size variation between them.
        /// </summary>
        private const float ArmGrain = 0.29f * ArmProminence;

        public static StrategicMapGalaxyCloud Create(Transform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("Galaxy Cloud") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var cloud = go.AddComponent<StrategicMapGalaxyCloud>();
            StrategicGalaxyPoint[] points = StrategicMapGalaxyPoints.Build(universe);
            var positions = new Vector3[points.Length];
            var colors = new Color[points.Length];
            var sizes = new float[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                positions[i] = points[i].Position;
                Color tint = StrategicMapGalaxyPoints.GalaxyPaletteColor(points[i].GalaxyIndex, points[i].ColorIndex);
                tint.a = points[i].Alpha;
                colors[i] = tint;
                sizes[i] = points[i].Size * ArmGrain;
            }
            cloud.Build(positions, colors, sizes, universe.UniverseCenter, 400f, "StrategicMapGalaxyCloud",
                minPixelSize: 0.55f * ArmProminence, maxPixelSize: 2.4f * ArmProminence);
            return cloud;
        }

        /// <summary>Halo shell + intergalactic bridge, and the dust lanes — same mesh path, different point sets.</summary>
        public static StrategicMapGalaxyCloud CreateSpaceLayer(Transform parent, StrategicMapUniverseSnapshot universe,
            bool dustLanes)
        {
            var go = new GameObject(dustLanes ? "Dust Lanes" : "Halo") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var cloud = go.AddComponent<StrategicMapGalaxyCloud>();

            StrategicSpacePoint[] points = dustLanes
                ? StrategicMapSpaceLayers.DustLanes(universe)
                : StrategicMapSpaceLayers.Halo(universe);

            var positions = new Vector3[points.Length];
            var colors = new Color[points.Length];
            var sizes = new float[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                positions[i] = points[i].Position;
                // Dust lanes are not a glow. The reference paints them source-over in the
                // space colour: they are the dark bands between the arms, and drawing them
                // additively lights up exactly the region they are supposed to obscure.
                Color tint = dustLanes
                    ? StrategicMapTheme.Space
                    : StrategicMapGalaxyPoints.GalaxyPaletteColor(points[i].GalaxyIndex, points[i].ColorIndex);
                tint.a = points[i].Alpha;
                colors[i] = tint;
                // Both layers are clamped to a pixel range by the reference, exactly like the
                // galaxy dust: halo at size*scale*3.2 within 0.42..2.2, dust lanes at
                // size*scale*0.045 within 0.8..3.8. A quad half-extent s lands at s*scale
                // pixels, so s is half the reference's own factor.
                sizes[i] = points[i].Size * (dustLanes ? 0.045f * 0.5f : 3.2f * 0.5f);
            }

            cloud.Build(positions, colors, sizes, universe.UniverseCenter, 400f,
                dustLanes ? "StrategicMapDustLanes" : "StrategicMapHalo",
                minPixelSize: dustLanes ? 0.8f : 0.42f,
                maxPixelSize: dustLanes ? 3.8f : 2.2f,
                subtractive: dustLanes);
            return cloud;
        }

        /// <summary>The background starfield uses the same additive point mesh, just a different point set.</summary>
        public static StrategicMapGalaxyCloud CreateStarfield(Transform parent, Vector3 universeCenter)
        {
            var go = new GameObject("Starfield") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var cloud = go.AddComponent<StrategicMapGalaxyCloud>();
            StrategicStarfieldPoint[] stars = StrategicMapStarfield.Build(universeCenter);
            var positions = new Vector3[stars.Length];
            var colors = new Color[stars.Length];
            var sizes = new float[stars.Length];
            for (int i = 0; i < stars.Length; i++)
            {
                positions[i] = stars[i].Position;
                Color tint = StrategicMapStarfield.TemperatureColor(stars[i].Temperature);
                tint.a = stars[i].Alpha;
                colors[i] = tint;
                // The reference's star sizes are literal pixels (0.75 / 1.5), held constant at
                // any distance; the shader's screen-space mode reproduces that.
                sizes[i] = stars[i].Size * 0.0016f;
            }
            cloud.Build(positions, colors, sizes, universeCenter, 260f, "StrategicMapStarfield", screenSpaceSize: true);
            return cloud;
        }

        private void Build(Vector3[] pointPositions, Color[] pointColors, float[] pointSizes,
            Vector3 boundsCenter, float boundsExtent, string meshName, bool screenSpaceSize = false,
            float minPixelSize = 0f, float maxPixelSize = 0f, bool subtractive = false)
        {
            PointCount = pointPositions.Length;
            if (PointCount == 0) return;

            var vertices = new Vector3[PointCount * 4];
            var uvs = new Vector2[PointCount * 4];
            var colors = new Color[PointCount * 4];
            // uv2 carries the quad's half-size so the shader can expand it in view space.
            var sizes = new Vector2[PointCount * 4];
            var triangles = new int[PointCount * 6];

            for (int i = 0; i < PointCount; i++)
            {
                Color tint = pointColors[i];

                int v = i * 4;
                // All four corners sit at the point's own position; the shader offsets them.
                vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = pointPositions[i];
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = tint;
                var half = new Vector2(pointSizes[i], pointSizes[i]);
                sizes[v] = sizes[v + 1] = sizes[v + 2] = sizes[v + 3] = half;

                int t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }

            mesh = new Mesh { name = meshName, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, sizes);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            // The cloud spans the whole universe and is always in view; a generous manual
            // bounds keeps Unity from culling it as the camera orbits.
            mesh.bounds = new Bounds(boundsCenter, Vector3.one * boundsExtent * 2f);

            Shader shader = Resources.Load<Shader>(ShaderResourcePath) ?? Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[StrategicMap] shader '{ShaderName}' not found; galaxy cloud will not draw");
                return;
            }
            material = new Material(shader) { name = meshName, hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_ScreenSpaceSize", screenSpaceSize ? 1f : 0f);
            material.SetFloat("_MinPixelSize", minPixelSize);
            material.SetFloat("_MaxPixelSize", maxPixelSize);
            // OneMinusSrcAlpha lets the space-coloured dust lanes cover what is behind them.
            material.SetFloat("_DstBlend", subtractive ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
                                                       : (float)UnityEngine.Rendering.BlendMode.One);

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
