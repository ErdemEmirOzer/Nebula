using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Faint additive guide circle showing a planet's initial orbit radius.
    /// Purely visual; the actual path comes from the gravity simulation.
    /// </summary>
    public class OrbitRing : MonoBehaviour
    {
        public static OrbitRing Create(Transform parent, float radius, Quaternion orbitPlane, Color color, int segments = 160)
        {
            var go = new GameObject("Orbit Ring");
            go.transform.SetParent(parent, false);
            go.transform.localRotation = orbitPlane;
            OrbitRing ring = go.AddComponent<OrbitRing>();

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = segments;
            line.widthMultiplier = Mathf.Max(1.2f, radius * 0.0012f);
            line.material = MaterialLibrary.CreateAdditive(Color.white);
            line.startColor = color;
            line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            var points = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                points[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }
            line.SetPositions(points);

            return ring;
        }
    }
}
