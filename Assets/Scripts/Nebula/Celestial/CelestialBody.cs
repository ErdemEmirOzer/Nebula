using UnityEngine;
using Nebula.Simulation;

namespace Nebula.Celestial
{
    /// <summary>
    /// Base component for anything with mass in the world. Mass is always
    /// derived from size: mass = density * (4/3) * pi * radius^3, so bigger
    /// bodies automatically pull harder. Bodies self-register with the
    /// <see cref="GravitySystem"/> which integrates their motion.
    /// </summary>
    [DisallowMultipleComponent]
    public class CelestialBody : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private CelestialBodyType bodyType = CelestialBodyType.Planet;

        [Header("Physical Properties")]
        [Tooltip("Body radius in world units. Drives both the visual scale and the mass.")]
        [SerializeField] private float radius = 10f;
        [Tooltip("Game-scale density. Mass = density * (4/3) * pi * radius^3.")]
        [SerializeField] private float density = 1f;
        [Tooltip("If false the body exerts gravity but is never moved by it (used to anchor the star).")]
        [SerializeField] private bool affectedByGravity = true;

        /// <summary>Current velocity in world units per second, integrated by the GravitySystem.</summary>
        public Vector3 Velocity { get; set; }

        public CelestialBodyType BodyType => bodyType;
        public float Radius => radius;
        public float Mass { get; private set; }
        public bool AffectedByGravity => affectedByGravity;

        /// <summary>Sets the physical identity of the body and recomputes its mass from its size.</summary>
        public void Configure(CelestialBodyType type, float bodyRadius, float bodyDensity, bool movedByGravity)
        {
            bodyType = type;
            radius = Mathf.Max(0.01f, bodyRadius);
            density = Mathf.Max(0.0001f, bodyDensity);
            affectedByGravity = movedByGravity;
            RecalculateMass();
        }

        private void RecalculateMass()
        {
            Mass = density * (4f / 3f) * Mathf.PI * radius * radius * radius;
        }

        private void Awake()
        {
            RecalculateMass();
        }

        private void OnValidate()
        {
            RecalculateMass();
        }

        private void OnEnable()
        {
            GravitySystem.Register(this);
        }

        private void OnDisable()
        {
            GravitySystem.Unregister(this);
        }
    }
}
