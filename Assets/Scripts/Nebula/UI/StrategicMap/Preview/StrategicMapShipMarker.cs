using System.Collections.Generic;
using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// Where the player's ship actually is, on the map.
    ///
    /// The map drew every star in the neighbourhood and no indication of where the person
    /// reading it was standing, which is the one position on a map that is never in doubt
    /// anywhere else. This is that marker: a plain blue chevron with a soft ring behind it,
    /// deliberately the simplest shape on the map so it is never mistaken for a body.
    ///
    /// Its position is pushed live every frame from the navigation authority rather than read
    /// off the universe snapshot, which is only rebuilt when the player's KNOWLEDGE changes —
    /// a ship marker that moved twice a minute would be worse than none at all.
    ///
    /// It billboards toward the camera and holds a near-constant screen size, so it stays
    /// legible from the universe overview down to a single system. It is drawn, never picked:
    /// the ship is not a destination.
    /// </summary>
    public sealed class StrategicMapShipMarker : MonoBehaviour
    {
        private const string LineShaderPath = "StrategicMap/StrategicMapLine";

        /// <summary>Roughly how tall the chevron stands on screen, as a fraction of the frame height.</summary>
        private const float ScreenHeightFraction = 0.030f;

        /// <summary>Clamps on the world size, so the marker never becomes a speck or swallows a system.</summary>
        private const float MinWorldSize = 0.012f;
        private const float MaxWorldSize = 2.2f;

        private Mesh mesh;
        private Material material;
        private Camera viewCamera;
        private Vector3 position;

        public Vector3 Position => position;

        public static StrategicMapShipMarker Create(Transform parent, Camera viewCamera, Vector3 initialPosition)
        {
            var go = new GameObject("Ship Marker") { layer = StrategicMapUniverseRenderer.MapLayer };
            go.transform.SetParent(parent, false);
            var marker = go.AddComponent<StrategicMapShipMarker>();
            marker.viewCamera = viewCamera;
            marker.position = initialPosition;
            marker.Initialise();
            return marker;
        }

        private void Initialise()
        {
            Shader shader = Resources.Load<Shader>(LineShaderPath);
            if (shader != null)
                material = new Material(shader) { name = "StrategicMapShipMarker", hideFlags = HideFlags.HideAndDontSave };

            mesh = BuildMesh();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// A chevron pointing up, with a faint halo ring around it. Built once in local space
        /// as unit-sized geometry; the transform does the rest.
        ///
        /// The shader is the map's own additive vertex-coloured line material, which draws
        /// triangles just as happily as lines — so this needs no shader of its own.
        /// </summary>
        private static Mesh BuildMesh()
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();

            Color core = StrategicMapTheme.WithAlpha(StrategicMapTheme.Cyan, 0.95f);
            Color body = StrategicMapTheme.WithAlpha(StrategicMapTheme.Blue, 0.85f);
            Color faint = StrategicMapTheme.WithAlpha(StrategicMapTheme.Blue, 0.0f);

            // The chevron: a solid arrowhead with a notch cut out of its base, so it reads as
            // a heading rather than as a triangle.
            var tip = new Vector3(0f, 0.62f, 0f);
            var left = new Vector3(-0.42f, -0.42f, 0f);
            var right = new Vector3(0.42f, -0.42f, 0f);
            var notch = new Vector3(0f, -0.16f, 0f);

            AddTriangle(vertices, colors, triangles, tip, left, notch, core, body, body);
            AddTriangle(vertices, colors, triangles, tip, notch, right, core, body, body);

            // A soft ring around it: a thin annulus fading outward, which is what stops the
            // marker from disappearing against a bright galaxy core.
            const int segments = 40;
            const float inner = 0.86f;
            const float outer = 1.18f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;

                var innerA = new Vector3(Mathf.Cos(a0) * inner, Mathf.Sin(a0) * inner, 0f);
                var innerB = new Vector3(Mathf.Cos(a1) * inner, Mathf.Sin(a1) * inner, 0f);
                var outerA = new Vector3(Mathf.Cos(a0) * outer, Mathf.Sin(a0) * outer, 0f);
                var outerB = new Vector3(Mathf.Cos(a1) * outer, Mathf.Sin(a1) * outer, 0f);

                Color ring = StrategicMapTheme.WithAlpha(StrategicMapTheme.Blue, 0.34f);
                AddTriangle(vertices, colors, triangles, innerA, outerA, innerB, ring, faint, ring);
                AddTriangle(vertices, colors, triangles, innerB, outerA, outerB, ring, faint, faint);
            }

            var built = new Mesh { name = "StrategicMapShipMarker" };
            built.SetVertices(vertices);
            built.SetColors(colors);
            built.SetTriangles(triangles, 0);
            // Fixed, generous bounds: the transform is rescaled every frame and a recalculated
            // bound would have the marker culled the moment it left its build-time size.
            built.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return built;
        }

        private static void AddTriangle(List<Vector3> vertices, List<Color> colors, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            colors.Add(ca); colors.Add(cb); colors.Add(cc);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }

        /// <summary>Puts the marker where the ship is, in map units.</summary>
        public void SetPosition(Vector3 mapPosition)
        {
            if (float.IsNaN(mapPosition.x) || float.IsInfinity(mapPosition.x)) return;
            position = mapPosition;
        }

        private void LateUpdate()
        {
            if (viewCamera == null) return;

            transform.position = position;
            // Face the camera squarely, so the chevron is never seen edge-on.
            transform.rotation = viewCamera.transform.rotation;

            // Hold a near-constant screen size: the distance to the camera decides the world
            // size, which is what keeps the marker readable at every zoom level.
            float distance = Vector3.Distance(viewCamera.transform.position, position);
            float worldPerFrameHeight = 2f * distance * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float size = Mathf.Clamp(worldPerFrameHeight * ScreenHeightFraction, MinWorldSize, MaxWorldSize);

            // A slow breath, so the one live thing on the map looks live.
            size *= Mathf.Lerp(0.94f, 1.06f, StrategicMapTheme.Breathe(Time.unscaledTime, 0.35f));
            transform.localScale = Vector3.one * size;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
