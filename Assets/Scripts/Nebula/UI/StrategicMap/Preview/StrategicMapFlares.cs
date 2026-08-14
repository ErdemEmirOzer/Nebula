using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// Anamorphic flares over the map's bright objects: the black hole at each galactic core.
    ///
    /// Systems flare only while their galaxy is the active one. The reference gates them the
    /// same way -- drawObjects skips any system whose galaxyId is not activeGalaxyId -- so at
    /// the universe overview only the four cores flare. Flaring all 135 at once turns each
    /// galaxy into a thicket of horizontal streaks.
    ///
    /// The intensity does not just set the brightness: it also drives the streak's width,
    /// so a brighter object throws a visibly longer flare.
    /// </summary>
    public sealed class StrategicMapFlares : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapFlare";

        /// <summary>Reference: drawAnamorphicFlare(projected, palette.gold, 0.26, time).</summary>
        private const float CoreIntensity = 0.26f;

        /// <summary>Reference: 0.38 for a system marker at rest.</summary>
        private const float SystemIntensity = 0.38f;

        private Mesh mesh;
        private Material material;
        private StrategicMapUniverseSnapshot universe;
        private string activeGalaxyId;
        private bool built;
        private float elapsed;

        public int FlareCount { get; private set; }

        public static StrategicMapFlares Create(Transform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("Flares") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var flares = go.AddComponent<StrategicMapFlares>();
            flares.universe = universe;
            flares.Build();
            return flares;
        }

        /// <summary>Adds or removes the active galaxy's system flares.</summary>
        public void SetActiveGalaxy(string galaxyId)
        {
            if (galaxyId == activeGalaxyId && built) return;
            activeGalaxyId = galaxyId;
            Build();
        }

        private void Build()
        {
            built = true;
            var systems = new System.Collections.Generic.List<StrategicMapUniverseSnapshot.SystemRow>();
            if (!string.IsNullOrEmpty(activeGalaxyId))
                for (int i = 0; i < universe.systems.Count; i++)
                {
                    // No system flares at all. Flaring only the located ones marked out where
                    // the player had been, and flaring every one of thousands would smear the
                    // whole galaxy. The galaxy cores still flare; a star is just a star.
                    if (universe.systems[i].galaxyId != activeGalaxyId) continue;
                    break;
                }

            FlareCount = universe.galaxies.Count + systems.Count;
            if (FlareCount == 0) return;

            var vertices = new Vector3[FlareCount * 4];
            var uvs = new Vector2[FlareCount * 4];
            var specs = new Vector2[FlareCount * 4];
            var colors = new Color[FlareCount * 4];
            var triangles = new int[FlareCount * 6];

            // Tints stay sRGB here: the shader composes the flare's contribution in gamma
            // and converts the result, which is where the colour space is reconciled.
            Color tint = StrategicMapTheme.Gold;
            tint.a = 1f;
            // Systems flare in their own galaxy's palette, so a flare can never be a
            // different colour from the star it belongs to.
            int galaxyIndex = GalaxyIndexOf(universe, activeGalaxyId);
            Color systemTint = StrategicMapGalaxyPoints.GalaxyPaletteColor(galaxyIndex, 0);
            systemTint.a = 1f;

            int coreCount = universe.galaxies.Count;
            for (int i = 0; i < FlareCount; i++)
            {
                bool core = i < coreCount;
                Vector3 position = core ? universe.galaxies[i].Center : systems[i - coreCount].Position;
                Color colour = core ? tint : systemTint;
                float intensity = core ? CoreIntensity : SystemIntensity;

                int v = i * 4;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices[v + corner] = position;
                    colors[v + corner] = colour;
                    specs[v + corner] = new Vector2(intensity, 0f);
                }
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);

                int t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }

            if (mesh == null)
                mesh = new Mesh { name = "StrategicMapFlares", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, specs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(universe.UniverseCenter, Vector3.one * 200f);

            if (material == null)
            {
                Shader shader = Resources.Load<Shader>(ShaderResourcePath);
                if (shader == null)
                {
                    Debug.LogWarning("[StrategicMap] flare shader missing; bright objects will not flare");
                    return;
                }
                material = new Material(shader) { name = "StrategicMapFlares", hideFlags = HideFlags.HideAndDontSave };
            }

            // Explicit null checks, not ??: a missing Unity component compares equal to null
            // through an overloaded operator but is not actually a null reference, so the
            // null-coalescing operator happily hands back the missing component.
            var filter = GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private static int GalaxyIndexOf(StrategicMapUniverseSnapshot universe, string galaxyId)
        {
            for (int i = 0; i < universe.galaxies.Count; i++)
                if (universe.galaxies[i].id == galaxyId) return i;
            return 0;
        }

        private void Update()
        {
            if (material == null) return;
            // Only the ghosts move, and they hold still under reduced motion.
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
