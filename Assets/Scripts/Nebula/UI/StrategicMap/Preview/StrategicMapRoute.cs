using System.Collections.Generic;
using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The navigation route: a dashed arc from the ship to the waypoint, with a pulse
    /// travelling along it.
    ///
    /// It arcs rather than running straight, and the arc height scales with the distance --
    /// a short hop is nearly flat, a crossing between galaxies bows well out of the plane.
    /// That is what stops a route from disappearing into the objects it passes through.
    ///
    /// The dashes crawl toward the destination, so the line reads as a direction rather than
    /// as a static connection.
    /// </summary>
    public sealed class StrategicMapRoute : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapLine";

        /// <summary>Reference: 42 segments, a 5/9 dash, and an arc of clamp(distance * 0.1, 0.08, 3.2).</summary>
        private const int Segments = 42;
        private const float DashOn = 5f;
        private const float DashPeriod = 14f;
        private const float DashCrawl = 9f;

        private Mesh mesh;
        private Material material;
        private Vector3 shipPosition;
        private Vector3 destination;
        private bool hasRoute;
        private bool displayEnabled = true;
        private float elapsed;
        private readonly List<Vector3> vertices = new List<Vector3>(Segments * 2 + 2);
        private readonly List<Color> colors = new List<Color>(Segments * 2 + 2);
        private readonly List<int> indices = new List<int>(Segments * 2 + 2);

        public bool HasRoute => hasRoute;

        public static StrategicMapRoute Create(Transform parent, StrategicMapUniverseSnapshot universe)
        {
            var go = new GameObject("Route") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var route = go.AddComponent<StrategicMapRoute>();
            route.shipPosition = new Vector3(universe.shipX, universe.shipY, universe.shipZ);
            route.Initialise(universe);
            go.SetActive(false);
            return route;
        }

        private void Initialise(StrategicMapUniverseSnapshot universe)
        {
            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning("[StrategicMap] line shader missing; the route will not draw");
                return;
            }
            material = new Material(shader) { name = "StrategicMapRoute", hideFlags = HideFlags.HideAndDontSave };

            mesh = new Mesh { name = "StrategicMapRoute" };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>The live map moves the ship end of the route as the ship actually travels; the preview set it once from the static snapshot.</summary>
        public void SetShipPosition(Vector3 position) => shipPosition = position;

        /// <summary>Plots a route to a world position, or clears it when given nothing.</summary>
        public void SetDestination(Vector3? target)
        {
            hasRoute = target.HasValue;
            gameObject.SetActive(hasRoute && displayEnabled);
            if (hasRoute) destination = target.Value;
        }

        public void SetDisplayEnabled(bool enabled)
        {
            displayEnabled = enabled;
            gameObject.SetActive(hasRoute && displayEnabled);
        }

        /// <summary>Reference: base + sin(pi * t) * arcHeight vertically, and a gentler double bow in z.</summary>
        private Vector3 ArcPoint(float progress, float arcHeight)
        {
            Vector3 baseline = Vector3.Lerp(shipPosition, destination, progress);
            return new Vector3(
                baseline.x,
                baseline.y + Mathf.Sin(Mathf.PI * progress) * arcHeight,
                baseline.z + Mathf.Sin(Mathf.PI * progress * 2f) * arcHeight * 0.08f);
        }

        private void Rebuild()
        {
            if (mesh == null || !hasRoute) return;

            float arcHeight = Mathf.Clamp(Vector3.Distance(shipPosition, destination) * 0.1f, 0.08f, 3.2f);
            // The dashes crawl toward the destination, so the route reads as a direction.
            float crawl = StrategicMapTheme.ReducedMotion ? 0f : elapsed * DashCrawl;

            vertices.Clear();
            colors.Clear();
            indices.Clear();

            Color line = StrategicMapTheme.Cyan;
            line.a = 0.35f;

            Vector3 previous = ArcPoint(0f, arcHeight);
            float travelled = 0f;
            for (int i = 1; i <= Segments; i++)
            {
                Vector3 point = ArcPoint(i / (float)Segments, arcHeight);
                float length = Vector3.Distance(previous, point);

                // The dash pattern is measured along the route, not per segment, so it stays
                // even where the arc's segments bunch up.
                float phase = Mathf.Repeat((travelled + crawl) * 40f, DashPeriod);
                if (phase < DashOn)
                {
                    vertices.Add(previous); vertices.Add(point);
                    colors.Add(line); colors.Add(line);
                }

                travelled += length;
                previous = point;
            }

            // The pulse: a short bright run sliding along the whole route.
            float pulseProgress = StrategicMapTheme.ReducedMotion ? 0.58f : Mathf.Repeat(elapsed * 0.085f, 1f);
            Vector3 pulseFrom = ArcPoint(Mathf.Max(0f, pulseProgress - 0.02f), arcHeight);
            Vector3 pulseTo = ArcPoint(Mathf.Min(1f, pulseProgress + 0.02f), arcHeight);
            Color pulse = StrategicMapTheme.SpaceText;
            pulse.a = 0.9f;
            vertices.Add(pulseFrom); vertices.Add(pulseTo);
            colors.Add(pulse); colors.Add(pulse);

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            for (int i = 0; i < vertices.Count; i++) indices.Add(i);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.bounds = new Bounds((shipPosition + destination) * 0.5f, Vector3.one * 400f);
        }

        private void Update()
        {
            if (!hasRoute) return;
            if (!StrategicMapTheme.ReducedMotion) elapsed += Time.unscaledDeltaTime;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
