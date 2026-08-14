using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The star at the centre of the focused system.
    ///
    /// Its absence was the thing that made the system view read as a diagram: ten lit worlds
    /// orbiting nothing. The orbits converge on a point, and until now that point was empty.
    ///
    /// Colour follows the reference's own rule -- a system is either warm or cool, and that
    /// decides gold or cyan for the whole star, corona and rays included.
    /// </summary>
    public sealed class StrategicMapSystemStar : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapSystemStar";

        private Mesh mesh;
        private Material material;
        private string builtForSystemId;
        private float elapsed;

        public static StrategicMapSystemStar Create(Transform parent)
        {
            var go = new GameObject("System Star") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var star = go.AddComponent<StrategicMapSystemStar>();
            star.Initialise();
            go.SetActive(false);
            return star;
        }

        private void Initialise()
        {
            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning("[StrategicMap] star shader missing; systems will have no star");
                return;
            }
            material = new Material(shader) { name = "StrategicMapSystemStar", hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_Highlight", StrategicMapTheme.SpaceText);
            material.SetColor("_Red", StrategicMapTheme.Red);

            mesh = new Mesh { name = "StrategicMapSystemStar" };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>Lights the star of one system, or puts it out when told nothing.</summary>
        public void ShowSystem(StrategicMapUniverseSnapshot universe, string systemId)
        {
            if (string.IsNullOrEmpty(systemId))
            {
                builtForSystemId = null;
                gameObject.SetActive(false);
                return;
            }
            if (systemId == builtForSystemId) { gameObject.SetActive(true); return; }

            StrategicMapUniverseSnapshot.SystemRow system = null;
            for (int i = 0; i < universe.systems.Count; i++)
                if (universe.systems[i].id == systemId) { system = universe.systems[i]; break; }
            if (system == null) { gameObject.SetActive(false); return; }

            Build(system);
            builtForSystemId = systemId;
            gameObject.SetActive(true);
        }

        private void Build(StrategicMapUniverseSnapshot.SystemRow system)
        {
            if (mesh == null) return;

            var vertices = new Vector3[4];
            var uvs = new Vector2[4];
            for (int corner = 0; corner < 4; corner++) vertices[corner] = system.Position;
            uvs[0] = new Vector2(0f, 0f);
            uvs[1] = new Vector2(1f, 0f);
            uvs[2] = new Vector2(1f, 1f);
            uvs[3] = new Vector2(0f, 1f);

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.bounds = new Bounds(system.Position, Vector3.one * 4f);

            if (material == null) return;
            material.SetFloat("_StarRadius", system.starRadius);
            // The star's real class colours the whole thing, corona and rays included. This
            // used to key off a "preset" name the retired prototype carried; live systems
            // have no such string, so every star in the game came out the same gold.
            // Located/Signal rows deliberately carry no real class. Their focused system
            // view still needs a visible centre, but it must not invent a yellow star; cyan
            // presents a neutral unresolved contact until the class is actually known.
            var knowledge = (Navigation.SystemKnowledgeLevel)system.knowledge;
            material.SetColor("_Star", knowledge >= Navigation.SystemKnowledgeLevel.Visited
                ? StrategicMapTheme.StarColor((Sectors.StarClass)system.starClass)
                : StrategicMapTheme.Cyan);
        }

        private void Update()
        {
            if (material == null) return;
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
