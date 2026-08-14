using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>Keeps the object facing the main camera. Used by glow quads.</summary>
    public class Billboard : MonoBehaviour
    {
        private Camera targetCamera;

        private void LateUpdate()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
                if (targetCamera == null) return;
            }

            Vector3 toQuad = transform.position - targetCamera.transform.position;
            if (toQuad.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(toQuad);
        }
    }
}
