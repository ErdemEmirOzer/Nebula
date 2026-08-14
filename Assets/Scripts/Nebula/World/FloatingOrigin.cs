using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nebula.World
{
    /// <summary>
    /// Keeps 32-bit float precision high across the huge universe: whenever the
    /// ship strays too far from the world origin, every spatial root object is
    /// shifted back so the ship returns near (0,0,0). All positions move
    /// together, so physics and relative geometry are untouched; world-space
    /// trails and particles are rebased, and the HUD shows absolute universe
    /// coordinates via <see cref="TotalOffset"/>. Runs after every other
    /// LateUpdate so cameras and effects never see a mixed state.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class FloatingOrigin : MonoBehaviour
    {
        [Tooltip("Shift the world once the ship is this far from the origin.")]
        [SerializeField] private float threshold = 200000f;

        private Transform ship;
        private static readonly List<GameObject> rootBuffer = new List<GameObject>(64);
        private ParticleSystem.Particle[] particleBuffer;
        // Cached: the scene is fully built in Awake and nothing spawns later,
        // and at top speed shifts happen every few frames — FindObjectsOfType
        // there would be far too slow.
        private TrailRenderer[] trails;
        private ParticleSystem[] worldParticleSystems;

        /// <summary>Sum of all shifts: absolute position = transform.position + TotalOffset.</summary>
        public static Vector3 TotalOffset { get; private set; }

        /// <summary>Raised after every rebase with the offset that was subtracted from the world.</summary>
        public static event System.Action<Vector3> OriginShifted;

        /// <summary>Campaign-boundary safety net for an interrupted teardown.</summary>
        public static void ResetForSession()
        {
            TotalOffset = Vector3.zero;
            OriginShifted = null;
        }

        public void Initialize(Transform shipTransform)
        {
            ship = shipTransform;
            TotalOffset = Vector3.zero;

            trails = FindObjectsOfType<TrailRenderer>();
            var worldSystems = new List<ParticleSystem>();
            foreach (ParticleSystem system in FindObjectsOfType<ParticleSystem>())
            {
                if (system.main.simulationSpace == ParticleSystemSimulationSpace.World)
                {
                    worldSystems.Add(system);
                }
            }
            worldParticleSystems = worldSystems.ToArray();
        }

        private void LateUpdate()
        {
            if (ship == null) return;
            Vector3 offset = ship.position;
            if (offset.sqrMagnitude < threshold * threshold) return;

            SceneManager.GetActiveScene().GetRootGameObjects(rootBuffer);
            foreach (GameObject rootGO in rootBuffer)
            {
                if (rootGO.transform is RectTransform) continue; // UI canvases are not spatial
                rootGO.transform.position -= offset;
            }
            TotalOffset += offset;

            RebaseTrails();
            RebaseParticles(offset);
            OriginShifted?.Invoke(offset);
        }

        private void RebaseTrails()
        {
            if (trails == null) return;
            // TrailRenderer offers no way to offset laid-down points; clearing
            // prevents trails snapping across space. They rebuild in seconds.
            foreach (TrailRenderer trail in trails)
            {
                if (trail != null) trail.Clear();
            }
        }

        private void RebaseParticles(Vector3 offset)
        {
            if (worldParticleSystems == null) return;
            foreach (ParticleSystem system in worldParticleSystems)
            {
                if (system == null) continue;
                int max = system.main.maxParticles;
                if (particleBuffer == null || particleBuffer.Length < max)
                {
                    particleBuffer = new ParticleSystem.Particle[max];
                }
                int count = system.GetParticles(particleBuffer);
                for (int i = 0; i < count; i++)
                {
                    particleBuffer[i].position -= offset;
                }
                system.SetParticles(particleBuffer, count);
            }
        }
    }
}
