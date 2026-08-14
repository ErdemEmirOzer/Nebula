using UnityEngine;

namespace Nebula.Celestial
{
    /// <summary>
    /// A planet orbiting the star under gravity. Adds slow self-rotation around
    /// a randomized tilted axis on top of the base body physics.
    /// </summary>
    public class Planet : CelestialBody
    {
        private Vector3 spinAxis = Vector3.up;
        private float spinSpeedDegrees;

        public void InitializePlanet(float bodyRadius, float bodyDensity, float axialTiltDegrees, float spinSpeed)
        {
            Configure(CelestialBodyType.Planet, bodyRadius, bodyDensity, movedByGravity: true);
            transform.localScale = Vector3.one * (bodyRadius * 2f); // sphere primitive has diameter 1
            spinAxis = Quaternion.Euler(axialTiltDegrees, Random.Range(0f, 360f), 0f) * Vector3.up;
            spinSpeedDegrees = spinSpeed;
        }

        private void Update()
        {
            transform.Rotate(spinAxis, spinSpeedDegrees * Time.deltaTime, Space.World);
        }
    }
}
