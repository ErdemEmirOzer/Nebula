using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The 135 star systems as they appear in the universe view: a warm radial glow with a
    /// bright white-hot core and a faint cross-hair, drawn at a constant on-screen size.
    ///
    /// Straight from the reference's system branch of <c>drawObject</c>: base radius 3.2,
    /// glow at 2.7x that, a core dot at 33% of the radius mixed 58% toward white, and a
    /// cross-hair spanning 90% of the glow horizontally and 65% vertically at alpha 0.5.
    ///
    /// The reference tints each system by its galaxy's palette, warm or cool depending on
    /// the galaxy — reproduced here from the same palette order.
    ///
    /// Only the active galaxy's systems are shown. That is the reference's own rule:
    /// drawObjects skips any system whose galaxyId is not activeGalaxyId, so at the universe
    /// overview it draws none of the 135 and each galaxy reads as a cloud rather than as a
    /// scatter of labelled points. Drawing them all made the overview far busier than the
    /// map it reproduces.
    ///
    /// One mesh, one draw call, rebuilt only when the active galaxy changes.
    /// </summary>
    public sealed class StrategicMapSystemMarkers : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapSystemMarker";

        /// <summary>
        /// Derived the same way as the black-hole extent: on-screen half-height fraction is
        /// size / tan(fov/2). The reference's glow reaches 3.2 * 2.7 ~= 8.6px of a 480px view,
        /// so size = (8.6 / 240) * 0.466 ~= 0.0084.
        /// </summary>
        private const float ScreenHalfExtent = 0.0084f;

        /// <summary>
        /// Target stars must remain distinct from the fine decorative star field. The old
        /// 0.55 multiplier reduced the complete glow to a dim pinprick, even though the
        /// picker treated it as an interaction target. This restores a clear 20-ish pixel
        /// diameter at the authored framing while retaining ample space between systems.
        /// </summary>
        private const float DrawnHalfExtent = ScreenHalfExtent * 1.15f;

        /// <summary>Controlled additive lift: bright enough to read, below a clipped white blob.</summary>
        private const float MarkerIntensity = 1.55f;

        private Mesh mesh;
        private Material material;
        private StrategicMapUniverseSnapshot universe;
        private string activeGalaxyId;
        private bool built;

        /// <summary>How many markers are currently drawn -- zero at the universe overview.</summary>
        public int MarkerCount { get; private set; }

        public static StrategicMapSystemMarkers Create(Transform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("System Markers") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var markers = go.AddComponent<StrategicMapSystemMarkers>();
            markers.universe = universe;
            markers.Build();
            return markers;
        }

        /// <summary>Shows one galaxy's systems, or none when the id is null.</summary>
        public void SetActiveGalaxy(string galaxyId)
        {
            if (galaxyId == activeGalaxyId && built) return;
            activeGalaxyId = galaxyId;
            Build();
        }

        private void Build()
        {
            built = true;
            var shown = new System.Collections.Generic.List<StrategicMapUniverseSnapshot.SystemRow>();
            if (!string.IsNullOrEmpty(activeGalaxyId))
                for (int i = 0; i < universe.systems.Count; i++)
                    if (universe.systems[i].galaxyId == activeGalaxyId) shown.Add(universe.systems[i]);

            MarkerCount = shown.Count;
            gameObject.SetActive(MarkerCount > 0);
            if (MarkerCount == 0)
            {
                if (mesh != null) mesh.Clear();
                return;
            }

            var vertices = new Vector3[MarkerCount * 4];
            var uvs = new Vector2[MarkerCount * 4];
            var colors = new Color[MarkerCount * 4];
            var sizes = new Vector2[MarkerCount * 4];
            var triangles = new int[MarkerCount * 6];

            for (int i = 0; i < MarkerCount; i++)
            {
                StrategicMapUniverseSnapshot.SystemRow system = shown[i];
                // Its own star's colour, not its galaxy's palette. Tinting every marker by
                // the galaxy made a red dwarf and a blue giant identical, which threw away
                // the one property that tells the player what kind of place they are
                // looking at from across the map.
                var knowledge = (Navigation.SystemKnowledgeLevel)system.knowledge;
                Color tint = knowledge >= Navigation.SystemKnowledgeLevel.Visited
                    ? StrategicMapTheme.StarColor((Sectors.StarClass)system.starClass)
                    : StrategicMapTheme.Knowledge(knowledge);
                tint.a = knowledge == Navigation.SystemKnowledgeLevel.Signal ? 0.86f : 1f;

                // Every star is drawn identically, discovered or not. The map hides nothing
                // and states nothing about what the player has been to -- a star is a star.
                float extent = knowledge == Navigation.SystemKnowledgeLevel.Signal
                    ? DrawnHalfExtent * 1.28f : DrawnHalfExtent;

                int v = i * 4;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices[v + corner] = system.Position;
                    colors[v + corner] = tint;
                    sizes[v + corner] = new Vector2(extent, extent);
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
                mesh = new Mesh { name = "StrategicMapSystemMarkers", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, sizes);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(universe.UniverseCenter, Vector3.one * 200f);

            if (material == null)
            {
                Shader shader = Resources.Load<Shader>(ShaderResourcePath);
                if (shader == null)
                {
                    Debug.LogWarning("[StrategicMap] system marker shader missing; systems will not draw");
                    return;
                }
                material = new Material(shader) { name = "StrategicMapSystemMarkers", hideFlags = HideFlags.HideAndDontSave };
                material.SetColor("_Highlight", StrategicMapTheme.SpaceText);
                material.SetFloat("_Intensity", MarkerIntensity);
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

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
