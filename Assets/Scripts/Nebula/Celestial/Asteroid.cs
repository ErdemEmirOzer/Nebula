using System.Collections.Generic;
using UnityEngine;
using Nebula.Visuals;

namespace Nebula.Celestial
{
    /// <summary>
    /// A drifting rock. Not part of the N-body simulation — asteroids float
    /// through open space on slowly wandering paths and tumble as they go.
    /// The ship still collides with them through the same solid-sphere rules
    /// as planets (see ShipController), using the static registry below.
    /// </summary>
    public class Asteroid : MonoBehaviour
    {
        private static readonly List<Asteroid> all = new List<Asteroid>();
        public static IReadOnlyList<Asteroid> All => all;

        private Vector3 velocity;
        private Vector3 tumbleAxis = Vector3.up;
        private float tumbleSpeedDegrees;
        private Vector3 wanderAxis = Vector3.up;
        private float wanderSpeedDegrees;

        /// <summary>Approximate solid radius in world units (for collisions).</summary>
        public float Radius { get; private set; }

        public void Initialize(float radius, Vector3 driftVelocity, Vector3 spinAxis, float spinSpeedDegrees)
        {
            Radius = radius;
            velocity = driftVelocity;
            tumbleAxis = spinAxis;
            tumbleSpeedDegrees = spinSpeedDegrees;
            wanderAxis = Random.onUnitSphere;
            wanderSpeedDegrees = Random.Range(0.2f, 1.2f);
        }

        private void OnEnable()
        {
            all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
        }

        /// <summary>
        /// One drift/tumble step, driven by <see cref="AsteroidDriftSystem"/>
        /// (per-rock Update() calls were the single biggest CPU cost with
        /// ~12k rocks). The drift direction wanders very slowly, so rocks
        /// feel carried by invisible currents instead of flying on rails.
        /// </summary>
        public Vector3 Step(float dt, Vector3 currentPosition)
        {
            velocity = Quaternion.AngleAxis(wanderSpeedDegrees * dt, wanderAxis) * velocity;
            Vector3 next = currentPosition + velocity * dt;
            transform.position = next;
            // ANI-05.3: the tumble is decoration and answers Motion Off; the
            // drift above is this rock's real movement and never stops. This
            // system stays the single writer of both — no second presenter.
            if (!Nebula.Visuals.AnimationAccessibility.MotionOff)
                transform.Rotate(tumbleAxis, tumbleSpeedDegrees * dt * Nebula.Visuals.AnimationAccessibility.MotionScale,
                    Space.World);

            return next;
        }
    }

    /// <summary>
    /// Moves every asteroid from one central Update with a cached position
    /// table: no per-rock Update() overhead, no Transform reads for the far
    /// ones. Rocks beyond the active range are frozen entirely — they are
    /// sub-pixel at that distance, so the freeze is invisible. The cache
    /// follows floating-origin rebases via <see cref="World.FloatingOrigin"/>.
    /// </summary>
    public class AsteroidDriftSystem : MonoBehaviour
    {
        [Tooltip("Rocks farther than this from the camera do not drift or tumble (invisible anyway).")]
        [SerializeField] private float activeRange = 150000f;
        [Tooltip("Rocks farther than this stop rendering entirely: sub-pixel size, but each renderer still costs culling time.")]
        [SerializeField] private float renderRange = 250000f;

        private struct Entry
        {
            public Asteroid rock;
            public Vector3 position;
            public Renderer rockRenderer;
            public AsteroidAuthoredVisual authoredVisual;
            public bool visible;
            public int lod;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private Camera viewCamera;

        private void Start()
        {
            // The whole universe is built in Awake; every rock exists by now.
            foreach (Asteroid rock in Asteroid.All)
            {
                entries.Add(new Entry
                {
                    rock = rock,
                    position = rock.transform.position,
                    rockRenderer = rock.GetComponent<Renderer>(),
                    authoredVisual = rock.GetComponent<AsteroidAuthoredVisual>(),
                    visible = true,
                    lod = 0
                });
            }
        }

        private void OnEnable()
        {
            World.FloatingOrigin.OriginShifted += OnOriginShifted;
        }

        private void OnDisable()
        {
            World.FloatingOrigin.OriginShifted -= OnOriginShifted;
        }

        private void OnOriginShifted(Vector3 offset)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                entry.position -= offset;
                entries[i] = entry;
            }
        }

        private void Update()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
                if (viewCamera == null) return;
            }

            Vector3 cameraPosition = viewCamera.transform.position;
            float activeSqr = activeRange * activeRange;
            float renderSqr = renderRange * renderRange;
            float dt = Time.deltaTime;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (entry.rock == null)
                {
                    // Mined away: swap-remove.
                    entries[i] = entries[entries.Count - 1];
                    entries.RemoveAt(entries.Count - 1);
                    continue;
                }

                float distanceSqr = (entry.position - cameraPosition).sqrMagnitude;

                // Far rocks leave the render list entirely; 12k always-on
                // renderers cost real culling time every frame.
                bool shouldRender = distanceSqr <= renderSqr;
                if (shouldRender != entry.visible)
                {
                    if (entry.rockRenderer != null) entry.rockRenderer.enabled = shouldRender;
                    entry.visible = shouldRender;
                    entries[i] = entry;
                }

                if (shouldRender && entry.authoredVisual != null)
                {
                    float distance = Mathf.Sqrt(Mathf.Max(1f, distanceSqr));
                    float apparentRadius = entry.rock.Radius / distance;
                    int desiredLod = apparentRadius >= 0.004f ? 0 : apparentRadius >= 0.0013f ? 1 : 2;
                    if (desiredLod != entry.lod)
                    {
                        entry.authoredVisual.SetLod(desiredLod);
                        entry.lod = desiredLod;
                        entries[i] = entry;
                    }
                }

                if (distanceSqr > activeSqr) continue;

                entry.position = entry.rock.Step(dt, entry.position);
                entries[i] = entry;
            }
        }
    }
}
