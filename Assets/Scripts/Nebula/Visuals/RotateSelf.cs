using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>Constant slow rotation around a local axis (galaxy spin, accretion disk).</summary>
    public class RotateSelf : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 1f;

        private void Update()
        {
            transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
