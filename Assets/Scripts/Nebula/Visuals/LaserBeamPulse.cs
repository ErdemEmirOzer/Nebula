using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>Short-lived authored hitscan presentation; never deals damage.</summary>
    public sealed class LaserBeamPulse : MonoBehaviour
    {
        private float remaining;

        public static GameObject Spawn(Vector3 start, Vector3 end, Transform parent = null, float seconds = 0.08f)
        {
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < 0.0001f) return null;
            var root = new GameObject("Laser Beam Pulse");
            if (parent != null) root.transform.SetParent(parent, true);
            root.transform.position = start;
            root.transform.rotation = Quaternion.LookRotation(delta.normalized);

            GameObject visual = NebulaVisualResolver.Instantiate(
                NebulaVisualCatalog.LaserBeam, root.transform, instanceName: "Authored Laser Beam");
            if (visual == null)
            {
                if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
                return null;
            }

            Transform muzzle = NebulaVisualResolver.FindAnchor(visual.transform, "anchor.muzzle");
            Transform impact = NebulaVisualResolver.FindAnchor(visual.transform, "anchor.impact");
            float authoredLength = muzzle != null && impact != null
                ? Vector3.Distance(muzzle.position, impact.position)
                : 1f;
            if (authoredLength > 0.0001f)
                visual.transform.localScale = new Vector3(1f, 1f, delta.magnitude / authoredLength);
            if (muzzle != null) root.transform.position += start - muzzle.position;

            root.AddComponent<LaserBeamPulse>().remaining = Mathf.Max(0.02f, seconds);
            return root;
        }

        private void Update()
        {
            remaining -= Time.deltaTime;
            if (remaining <= 0f) Destroy(gameObject);
        }
    }
}
