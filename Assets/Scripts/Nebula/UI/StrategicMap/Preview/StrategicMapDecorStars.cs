using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The decorative stars the game itself strings along every galaxy's spiral arms.
    ///
    /// These are the stars the player actually sees filling a galaxy from the cockpit, and
    /// without them the map showed four discs of dust with a single tight knot of navigable
    /// systems in one of them — nothing like the sky it is supposed to be a map of.
    ///
    /// They are drawn for EVERY galaxy at every level, because a galaxy's stars are what a
    /// galaxy looks like whatever the player is currently focused on. They are deliberately
    /// not part of the snapshot's system list, so the picker can never resolve one: a
    /// decorative star is not a navigation target (VS-008 §4, REQ-116).
    ///
    /// One mesh, one draw call, built once.
    /// </summary>
    public sealed class StrategicMapDecorStars : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapSystemMarker";

        /// <summary>
        /// Smaller and fainter than a real system's marker. A decorative star is scenery the
        /// eye should read as depth and structure, never as something to aim at — if it
        /// competed with a navigable system the map would be inviting the player to click it.
        /// </summary>
        private const float ScreenHalfExtent = 0.0084f * 0.55f;
        private const float Alpha = 0.62f;

        private Mesh mesh;
        private Material material;

        public int StarCount { get; private set; }

        public static StrategicMapDecorStars Create(Transform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("Decor Stars") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var stars = go.AddComponent<StrategicMapDecorStars>();
            stars.Build(universe);
            return stars;
        }

        private void Build(StrategicMapUniverseSnapshot universe)
        {
            StarCount = universe?.decorStars?.Count ?? 0;
            gameObject.SetActive(StarCount > 0);
            if (StarCount == 0) return;

            var vertices = new Vector3[StarCount * 4];
            var uvs = new Vector2[StarCount * 4];
            var colors = new Color[StarCount * 4];
            var sizes = new Vector2[StarCount * 4];
            var triangles = new int[StarCount * 6];

            for (int i = 0; i < StarCount; i++)
            {
                StrategicMapUniverseSnapshot.MarkerRow star = universe.decorStars[i];

                // Its own galaxy's palette, so each galaxy keeps the colour it has in the sky.
                Color tint = StrategicMapGalaxyPoints.GalaxyPaletteColor(GalaxyIndexOf(universe, star.galaxyId), 0);
                tint.a = Alpha;

                int v = i * 4;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices[v + corner] = star.Position;
                    colors[v + corner] = tint;
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

            mesh = new Mesh { name = "StrategicMapDecorStars", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, sizes);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(universe.UniverseCenter, Vector3.one * 400f);

            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning("[StrategicMap] system marker shader missing; decor stars will not draw");
                return;
            }
            material = new Material(shader) { name = "StrategicMapDecorStars", hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_Highlight", StrategicMapTheme.SpaceText);

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
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
