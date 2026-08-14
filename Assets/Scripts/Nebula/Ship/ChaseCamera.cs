using UnityEngine;

namespace Nebula.Ship
{
    /// <summary>
    /// Chase camera rigidly anchored to an offset behind the ship (so the ship
    /// can never outrun it at high speed) with smoothed rotation so the ship
    /// swings naturally in view while turning.
    /// </summary>
    public class ChaseCamera : MonoBehaviour
    {
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 2.8f, -11f);
        [SerializeField] private float lookAheadDistance = 40f;
        [SerializeField] private float rotationSmoothing = 5f;

        private Transform target;

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (target == null) return;
            transform.position = target.TransformPoint(localOffset);
            transform.rotation = Quaternion.LookRotation(
                target.TransformPoint(Vector3.forward * lookAheadDistance) - transform.position, target.up);
        }

        private void LateUpdate()
        {
            if (target == null) return;

            transform.position = target.TransformPoint(localOffset);

            Quaternion desired = Quaternion.LookRotation(
                target.TransformPoint(Vector3.forward * lookAheadDistance) - transform.position, target.up);
            float blend = 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desired, blend);
        }
    }
}
