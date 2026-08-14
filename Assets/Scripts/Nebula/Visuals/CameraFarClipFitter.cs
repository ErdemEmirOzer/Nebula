using System.Collections.Generic;
using UnityEngine;
using Nebula.World;

namespace Nebula.Visuals
{
    /// <summary>
    /// Keeps tracked galaxies inside the camera frustum forever: each frame
    /// the far clip plane grows just enough to contain the farthest galaxy
    /// disc. Inside the play area the authored far plane is used unchanged,
    /// so depth precision in normal play stays exactly as before — the plane
    /// only stretches while the ship wanders out into intergalactic space.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFarClipFitter : MonoBehaviour
    {
        private readonly List<GalaxyMap> galaxies = new List<GalaxyMap>();
        private Camera attachedCamera;
        private float baseFarClip;

        public void Track(GalaxyMap map)
        {
            if (map != null) galaxies.Add(map);
        }

        private void Awake()
        {
            attachedCamera = GetComponent<Camera>();
            baseFarClip = attachedCamera.farClipPlane;
        }

        private void LateUpdate()
        {
            float farthest = 0f;
            for (int i = 0; i < galaxies.Count; i++)
            {
                GalaxyMap map = galaxies[i];
                if (map == null) continue;
                // Radial distance is a safe upper bound on view-axis depth;
                // 2.8 x radius covers the rim haze plus the halo stars and
                // globular clusters orbiting out to ~2.5 radii.
                float required = Vector3.Distance(transform.position, map.transform.position)
                                 + map.Radius * 2.8f;
                if (required > farthest) farthest = required;
            }
            attachedCamera.farClipPlane = Mathf.Max(baseFarClip, farthest * 1.02f);
        }
    }
}
