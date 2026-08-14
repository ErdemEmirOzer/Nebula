using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The asteroid belt of the focused system: a slowly shearing ring of rocks around the
    /// star, outside the outermost orbit.
    ///
    /// The snapshot stores only how many rocks each system has, not where they are -- the
    /// reference generates the sample points itself from <c>mulberry32(41023)</c>, walking
    /// the systems in order and drawing exactly six numbers per rock. That single shared
    /// stream is why the generation is reproduced whole rather than per system: skipping a
    /// system would shift every later one's rocks.
    ///
    /// Only <c>min(asteroidCount, 54)</c> rocks are ever sampled per system, which is the
    /// reference's own cap -- a belt of 400 objects is still drawn with 54 dots.
    ///
    /// Like the orbits, this exists only for the system the camera is looking at.
    /// </summary>
    public sealed class StrategicMapAsteroidBelt : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapAdditivePoint";

        /// <summary>The reference's own per-system sample cap.</summary>
        /// <summary>
        /// How many of a system's real 500 rocks the map actually draws. The retired
        /// prototype drew 54, which at its own short frame was a visible dusting; at a real
        /// 1080- or 1440-tall frame the same 54 sub-pixel dots vanish entirely and the belt
        /// simply is not there. Raised until the ring reads as a ring, still far below the
        /// real count because a strategic map shows a belt, not every rock in it.
        /// </summary>
        public const int MaxSamplesPerSystem = 160;

        /// <summary>
        /// The same correction the galaxy dust needed, for the same reason: every pixel size
        /// in the prototype was quoted against a frame roughly 2.25 times shorter than the
        /// one the map renders at now. See StrategicMapGalaxyCloud.ArmProminence.
        /// </summary>
        private const float BeltProminence = 2.25f;

        /// <summary>Reference: mulberry32(41023), one stream shared by every system.</summary>
        private const uint Seed = 41023;

        /// <summary>
        /// Reference: size = clamp(rock.size * scale * 0.026, 0.45, 1.4) and the dot is drawn
        /// at 0.62 of that. A quad half-extent s lands at s * scale pixels, so the same dot is
        /// s = rock.size * 0.026 * 0.62, with the clamp carried over as a full-side pixel range.
        /// </summary>
        private const float SizeToHalfExtent = 0.026f * 0.62f;
        private const float MinPixelSize = 0.45f * 2f * 0.62f;
        private const float MaxPixelSize = 1.4f * 2f * 0.62f;

        /// <summary>The reference's palette.muted, which is not one of the map's UI colours.</summary>
        private static readonly Color Muted = new Color(148f / 255f, 164f / 255f, 188f / 255f);

        /// <summary>One sampled rock. Pure data, so the generation can be verified without a scene.</summary>
        public struct Rock
        {
            public Vector3 Offset;      // from the system, already scaled for the focused view
            public float Drift;
            public float Radius;
            public int VisualVariant;
            /// <summary>Reference: clamp(0.4 + sqrt(radius / 120) * 1.05, 0.42, 1.45).</summary>
            public float DrawSize;
        }

        private Mesh mesh;
        private MeshFilter meshFilter;
        private Material material;
        private Rock[] rocks = new Rock[0];
        private Vector3[] vertices = new Vector3[0];
        private Vector3 systemPosition;
        private string builtForSystemId;
        private float elapsed;

        public int RockCount => rocks.Length;

        public static StrategicMapAsteroidBelt Create(Transform parent)
        {
            var go = new GameObject("Asteroid Belt") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var belt = go.AddComponent<StrategicMapAsteroidBelt>();
            belt.Initialise();
            go.SetActive(false);
            return belt;
        }

        private void Initialise()
        {
            // The component is still allowed to receive focus changes when its optional
            // presentation shader is absent (for example while an asset bundle is still
            // loading).  Install the data sink first so that graceful visual degradation
            // can never turn a later ShowSystem call into a MeshFilter null-reference.
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();

            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning("[StrategicMap] additive point shader missing; asteroids will not draw");
                return;
            }
            material = new Material(shader) { name = "StrategicMapAsteroids", hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_ScreenSpaceSize", 0f);
            material.SetFloat("_MinPixelSize", MinPixelSize);
            material.SetFloat("_MaxPixelSize", MaxPixelSize);

            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>Shows one system's belt. Rebuilds only when the focused system changes.</summary>
        public void ShowSystem(StrategicMapUniverseSnapshot universe, string systemId)
        {
            if (string.IsNullOrEmpty(systemId))
            {
                builtForSystemId = null;
                gameObject.SetActive(false);
                return;
            }
            if (systemId == builtForSystemId) { gameObject.SetActive(true); return; }

            Build(universe, systemId);
            builtForSystemId = systemId;
            gameObject.SetActive(rocks.Length > 0);
        }

        private void Build(StrategicMapUniverseSnapshot universe, string systemId)
        {
            rocks = Sample(universe, systemId);
            for (int i = 0; i < universe.systems.Count; i++)
                if (universe.systems[i].id == systemId) { systemPosition = universe.systems[i].Position; break; }
            BuildMesh();
        }

        /// <summary>
        /// The reference's buildAsteroidPoints, stopped at the system we want.
        ///
        /// One mulberry32 stream serves the whole universe and is walked in system order, so
        /// the rocks of any given system depend on every draw made for the systems before it.
        /// The earlier systems are therefore sampled and discarded rather than skipped --
        /// skipping would shift every rock in every later belt.
        ///
        /// Pure, so the generation can be checked without a scene or a camera.
        /// </summary>
        public static Rock[] Sample(StrategicMapUniverseSnapshot universe, string systemId)
        {
            var random = new Mulberry32(Seed);

            for (int i = 0; i < universe.systems.Count; i++)
            {
                StrategicMapUniverseSnapshot.SystemRow system = universe.systems[i];
                int sampleCount = Mathf.Min(system.asteroidCount, MaxSamplesPerSystem);
                bool wanted = system.id == systemId;

                float visualScale = wanted ? StrategicMapOrbits.VisualScale(system) : 1f;
                // The belt the game itself builds, not the retired prototype's: SystemMaterializer
                // rings a system from NavigationRadius * 0.28 out to the outermost orbit * 1.15.
                // The prototype measured its inner edge from the FIRST orbit, which put the belt
                // in among the inner planets instead of outside the last one.
                float inner = system.navigationRadius * Sectors.SystemMaterializer.BeltInnerRadiusFactor
                              / StrategicMapOrbits.MapUnit;
                float outer = system.outermostOrbit * Sectors.SystemMaterializer.BeltOuterRadiusFactor
                              / StrategicMapOrbits.MapUnit;
                var sampled = wanted ? new Rock[sampleCount] : null;

                for (int s = 0; s < sampleCount; s++)
                {
                    // Exactly the reference's six draws, in its order: any deviation here
                    // silently shifts every rock in every later system.
                    float angle = random.Next() * Mathf.PI * 2f;
                    float radial = inner + (outer - inner) * Mathf.Sqrt(random.Next());
                    float radius = 6f + Mathf.Pow(random.Next(), 2.2f) * 104f;
                    int visualVariant = random.Next() < 0.2f ? 2 : 0;
                    float verticalJitter = (random.Next() - 0.5f) * radial * 0.12f;
                    float drift = 0.0015f + random.Next() * 0.0045f;

                    if (!wanted) continue;

                    sampled[s] = new Rock
                    {
                        Offset = new Vector3(
                            Mathf.Cos(angle) * radial * visualScale,
                            verticalJitter * visualScale,
                            Mathf.Sin(angle) * radial * visualScale),
                        Drift = drift,
                        Radius = radius,
                        VisualVariant = visualVariant,
                        DrawSize = Mathf.Clamp((0.4f + Mathf.Sqrt(radius / 120f) * 1.05f) * BeltProminence,
                            0.42f * BeltProminence, 1.45f * BeltProminence)
                    };
                }

                // Nothing drawn past this system can affect the belt we asked for.
                if (wanted) return sampled;
            }

            return new Rock[0];
        }

        /// <summary>The reference's per-rock tint and opacity, both decided by its variant roll.</summary>
        private static Color RockColor(int visualVariant)
        {
            Color color = visualVariant == 1 ? StrategicMapTheme.Green
                : visualVariant == 2 ? StrategicMapTheme.Violet
                : Muted;
            // Reference: alpha = min(0.7, rock.alpha * 2.4), rock.alpha = 0.16 or 0.3.
            color.a = Mathf.Min(0.7f, (visualVariant == 0 ? 0.16f : 0.3f) * 2.4f);
            return color;
        }

        private void BuildMesh()
        {
            // Also tolerate a component restored from an incomplete/legacy prefab where
            // Initialise never ran.  The belt simply remains invisible; map interaction
            // and system selection must continue to function.
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) return;

            if (mesh == null)
                mesh = new Mesh { name = "StrategicMapAsteroids", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.Clear();
            if (rocks.Length == 0)
            {
                meshFilter.sharedMesh = mesh;
                return;
            }

            vertices = new Vector3[rocks.Length * 4];
            var uvs = new Vector2[rocks.Length * 4];
            var colors = new Color[rocks.Length * 4];
            var sizes = new Vector2[rocks.Length * 4];
            var triangles = new int[rocks.Length * 6];

            for (int i = 0; i < rocks.Length; i++)
            {
                int v = i * 4;
                float halfExtent = rocks[i].DrawSize * SizeToHalfExtent;
                var half = new Vector2(halfExtent, halfExtent);
                Color tint = RockColor(rocks[i].VisualVariant);
                for (int corner = 0; corner < 4; corner++)
                {
                    colors[v + corner] = tint;
                    sizes[v + corner] = half;
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
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, sizes);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(systemPosition, Vector3.one * 8f);
            meshFilter.sharedMesh = mesh;
        }

        /// <summary>
        /// The belt shears rather than spinning: each rock carries its own drift rate, so the
        /// inner and outer edges separate over time exactly as they do in the reference.
        /// </summary>
        private void WritePositions(float time)
        {
            for (int i = 0; i < rocks.Length; i++)
            {
                Rock rock = rocks[i];
                float angle = time * rock.Drift;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                var position = new Vector3(
                    systemPosition.x + rock.Offset.x * cos - rock.Offset.z * sin,
                    systemPosition.y + rock.Offset.y + Mathf.Sin(time * rock.Drift * 1.7f + rock.Radius) * 0.0014f,
                    systemPosition.z + rock.Offset.x * sin + rock.Offset.z * cos);

                int v = i * 4;
                vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = position;
            }
        }

        private void Update()
        {
            if (rocks.Length == 0 || mesh == null) return;
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
