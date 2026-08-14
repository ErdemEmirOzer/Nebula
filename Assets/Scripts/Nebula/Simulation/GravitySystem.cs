using System.Collections.Generic;
using UnityEngine;
using Nebula.Celestial;

namespace Nebula.Simulation
{
    /// <summary>
    /// Newtonian gravity integrator. Only anchored bodies (stars, the black
    /// hole — anything not moved by gravity) act as attractors: planets are
    /// thousands of times lighter by design, so their mutual pulls are
    /// negligible and skipping them keeps hundreds of planets cheap
    /// (O(moving x stars) instead of O(n^2)). Velocities and positions advance
    /// with semi-implicit Euler in FixedUpdate, stable for near-circular
    /// orbits. Distances are softened to the attractor's radius so overlapping
    /// bodies can never produce infinite force.
    /// </summary>
    [DisallowMultipleComponent]
    public class GravitySystem : MonoBehaviour
    {
        [Tooltip("Gravitational constant, game-scaled. Higher = faster orbits at the same distance.")]
        [SerializeField] private float gravitationalConstant = 0.25f;

        // Attractors are snapshotted once per tick (position, mass, radius):
        // with ~750 planets x ~200 stars, reading Transform.position per pair
        // would mean millions of native calls per second.
        private struct Attractor
        {
            public CelestialBody body;
            public Vector3 position;
            public float mass;
            public float radius;
        }

        private static readonly List<CelestialBody> bodies = new List<CelestialBody>();
        private static readonly List<Attractor> attractors = new List<Attractor>();

        public static GravitySystem Instance { get; private set; }
        public static IReadOnlyList<CelestialBody> Bodies => bodies;

        public float GravitationalConstant => gravitationalConstant;

        public static void Register(CelestialBody body)
        {
            if (body != null && !bodies.Contains(body)) bodies.Add(body);
        }

        public static void Unregister(CelestialBody body)
        {
            bodies.Remove(body);
        }

        public void SetGravitationalConstant(float value)
        {
            gravitationalConstant = Mathf.Max(0f, value);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>Covers a deliberate re-enable; the normal path claims it in <see cref="Awake"/>.</summary>
        private void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        /// <summary>
        /// Hands the singleton back the moment this object is deactivated, rather
        /// than at the deferred <c>OnDestroy</c> that follows it.
        /// <para>
        /// <c>Restart Campaign</c> disposes one session and composes the next inside
        /// a single frame, and <c>Destroy</c> does not run until the end of it — so
        /// the outgoing gravity system was still holding <see cref="Instance"/> when
        /// the incoming one woke, the guard above fired, and the <b>new</b> world's
        /// gravity destroyed itself. The old one then went at end of frame and left
        /// the campaign with no gravity system at all: planets on rails, the typed
        /// <c>NebulaContext.Gravity</c> pointing at a destroyed object, and nothing
        /// on screen to say so. A thousand-cycle census caught it as one object that
        /// was destroyed and never rebuilt.
        /// </para>
        /// <para>
        /// The session lifetime deactivates every root before destroying it for
        /// exactly this reason; this is the other half of that contract.
        /// </para>
        /// </summary>
        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private static void RefreshAttractors()
        {
            attractors.Clear();
            for (int i = 0; i < bodies.Count; i++)
            {
                CelestialBody body = bodies[i];
                if (body == null || body.AffectedByGravity) continue;
                attractors.Add(new Attractor
                {
                    body = body,
                    position = body.transform.position,
                    mass = body.Mass,
                    radius = body.Radius
                });
            }
        }

        // Bodies farther than this from the camera integrate on every 4th
        // tick (with 4x dt): invisible at that range, and the orbital periods
        // are hours long, so the coarser step changes nothing visible.
        private const float NearRange = 1000000f;
        private const int CoarseTickInterval = 4;

        private int tickIndex;
        private Camera viewCamera;

        private void FixedUpdate()
        {
            float baseDt = Time.fixedDeltaTime;
            RefreshAttractors();

            tickIndex++;
            bool coarseTick = tickIndex % CoarseTickInterval == 0;

            if (viewCamera == null) viewCamera = Camera.main;
            Vector3 viewPosition = viewCamera != null ? viewCamera.transform.position : Vector3.zero;

            // Single pass is exact here: only anchored stars attract, so a
            // planet's move never changes another planet's acceleration.
            for (int i = 0; i < bodies.Count; i++)
            {
                CelestialBody body = bodies[i];
                if (body == null || !body.AffectedByGravity) continue;

                Vector3 position = body.transform.position;
                bool near = (position - viewPosition).sqrMagnitude < NearRange * NearRange;
                if (!near && !coarseTick) continue;

                float dt = near ? baseDt : baseDt * CoarseTickInterval;
                body.Velocity += AccelerationAt(position, body) * dt;
                body.transform.position = position + body.Velocity * dt;
            }
        }

        /// <summary>
        /// Total gravitational acceleration at a world position.
        /// Pass <paramref name="ignore"/> to exclude a body (usually the querying body itself).
        /// </summary>
        public Vector3 AccelerationAt(Vector3 position, CelestialBody ignore = null)
        {
            // Fall back to a fresh snapshot before the first physics tick.
            if (attractors.Count == 0) RefreshAttractors();

            Vector3 total = Vector3.zero;
            for (int i = 0; i < attractors.Count; i++)
            {
                Attractor other = attractors[i];
                if (other.body == ignore) continue;

                Vector3 delta = other.position - position;
                float distance = delta.magnitude;
                if (distance < 1e-4f) continue;

                float softened = Mathf.Max(distance, other.radius);
                total += delta / distance * (gravitationalConstant * other.mass / (softened * softened));
            }
            return total;
        }

        /// <summary>Speed needed for a circular orbit of the given radius around the given mass.</summary>
        public float CircularOrbitSpeed(float centralMass, float orbitRadius)
        {
            return Mathf.Sqrt(gravitationalConstant * centralMass / Mathf.Max(orbitRadius, 0.01f));
        }
    }
}
