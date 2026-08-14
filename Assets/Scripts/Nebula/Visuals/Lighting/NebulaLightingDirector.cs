using UnityEngine;
using Nebula.Celestial;
using Nebula.Sectors;

namespace Nebula.Visuals
{
    /// <summary>
    /// The single owner of real-time lighting for a campaign session. Created
    /// once by <c>LightingInstaller</c> and reached only through
    /// <c>NebulaContext</c> — it never searches for its dependencies and is
    /// never a second composition root.
    /// <para>
    /// It owns exactly one key light. That light sits on the star of whichever
    /// system is currently Full, and no other star in the galaxy — Warm
    /// neighbours, decorative arm stars, companion galaxies — produces a
    /// real-time light at all; they read purely through their emissive halos.
    /// That is what keeps a sky containing hundreds of visible stars down to a
    /// single per-pixel light.
    /// </para>
    /// <para>
    /// Presentation only. Nothing here is serialized, nothing feeds simulation,
    /// and no gameplay outcome may depend on a light existing — the transient
    /// pool is explicitly allowed to refuse.
    /// </para>
    /// </summary>
    public class NebulaLightingDirector : MonoBehaviour
    {
        /// <summary>Base distance past which an effect light stops being drawn, before the profile's scale.</summary>
        public const float TransientCullBaseDistance = 2500f;

        /// <summary>How much brighter the key light gets right at the star, matching the halo bloom that rises with it.</summary>
        public const float ProximityIntensityBoost = 1.3f;

        private IFullSystemStarSource streaming;
        private ShipLightingPresenter shipLighting;
        private Transform cameraTransform;
        private LightingQualityProfile profile;
        private Light keyLight;
        private TransientLightPool transientLights;

        private string litSystemId;
        private Transform litStar;
        private float litStarRadius;
        private float keyBaseIntensity;

        public LightingQualityProfile Profile => profile;
        public Light KeyLight => keyLight;
        public TransientLightPool TransientLights => transientLights;

        /// <summary>The system whose star currently owns the key light, or null when the ship is between systems.</summary>
        public string LitSystemId => litSystemId;

        /// <summary>True while a real star is driving the key light.</summary>
        public bool HasKeyLight => litStar != null && keyLight != null && keyLight.gameObject.activeSelf;

        /// <summary>
        /// The pool is built once at the highest capacity any profile asks
        /// for; profiles then raise and lower the active ceiling. A quality
        /// change therefore never allocates or destroys a light.
        /// </summary>
        public void Initialize(IFullSystemStarSource starSource, Camera camera, LightingQualityProfile startingProfile)
        {
            streaming = starSource;
            cameraTransform = camera != null ? camera.transform : null;

            transientLights = TransientLightPool.Create(transform, LightingQualityProfile.High.TransientLightCapacity);

            keyLight = LightingRig.SystemKey(Color.white, 1f).Create(transform, "System Key Light");
            keyLight.gameObject.SetActive(false);

            ApplyProfile(startingProfile);

            // Resolve ownership immediately so the very first rendered frame is
            // already lit, rather than dark until the first LateUpdate.
            SyncKeyLightOwnership();
            DriveKeyLight();
        }

        /// <summary>Runtime quality switch. Visual only — never written to a save, never read by simulation.</summary>
        public void SetQuality(LightingQuality quality) => ApplyProfile(LightingQualityProfile.Resolve(quality));

        /// <summary>
        /// LGT-03: registers the ship's presenter so quality changes reach the
        /// hull fill, which is the only real-time light the director does not
        /// create itself but still budgets for.
        /// </summary>
        public void BindShipLighting(ShipLightingPresenter presenter)
        {
            shipLighting = presenter;
            shipLighting?.ApplyProfile(profile);
        }

        /// <summary>LGT-05: the combat observer. Held only so the session owns one, and so tests can reach it without searching the scene.</summary>
        public CombatLightingBridge CombatLighting { get; private set; }

        public void BindCombatLighting(CombatLightingBridge bridge) => CombatLighting = bridge;

        public void ApplyProfile(in LightingQualityProfile newProfile)
        {
            profile = newProfile;
            LightingEnvironment.Apply(profile);

            if (keyLight != null)
            {
                // The key light is the only light in the game permitted to cast
                // shadows, and only when the profile pays for it.
                keyLight.shadows = profile.KeyLightShadows;
                keyLight.shadowResolution = profile.ShadowResolution;
            }

            // The real-time ceiling always accounts for the key light itself,
            // so a profile can never be configured into exceeding its own budget.
            transientLights?.SetActiveCapacity(
                Mathf.Min(profile.TransientLightCapacity, Mathf.Max(0, profile.MaxRealtimeLights - 1)));

            // LGT-02: celestial surfaces re-resolve their response for the new
            // tier. Pushed here, on a change — never polled per frame.
            CelestialSurfacePresenter.ApplyQualityToAll(profile.Quality);

            // LGT-03: the ship's hull fill follows the same tier, and vanishes
            // entirely on Low.
            shipLighting?.ApplyProfile(profile);

            // LGT-04: structures and POIs re-resolve too. Pushed on change —
            // nothing sweeps the built world looking for work.
            StructureLightingPresenter.ApplyQualityToAll(profile.Quality);
            PoiLightingPresenter.ApplyQualityToAll(profile.Quality);
        }

        /// <summary>
        /// Asks for a short-lived effect light. Returning false is an ordinary
        /// outcome at capacity and callers must degrade to emissive rather than
        /// change behaviour — see <see cref="LightingQualityProfile.EmissiveOnlyFallback"/>.
        /// </summary>
        public bool TryAcquireTransientLight(in TransientLightRequest request, out TransientLightHandle handle)
        {
            if (transientLights == null) { handle = TransientLightHandle.None; return false; }
            return transientLights.TryAcquire(request, out handle);
        }

        public void ReleaseTransientLight(TransientLightHandle handle) => transientLights?.Release(handle);

        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>
        /// One frame of lighting upkeep. Called from <c>LateUpdate</c> in a
        /// live session; exposed so a test can step it deterministically
        /// instead of waiting on frames.
        /// </summary>
        public void Tick(float deltaTime)
        {
            SyncKeyLightOwnership();
            DriveKeyLight();

            transientLights?.Tick(deltaTime,
                cameraTransform != null ? cameraTransform.position : Vector3.zero,
                TransientCullBaseDistance * profile.CullDistanceScale);
        }

        /// <summary>
        /// Moves the key light onto the current Full system's star, and takes
        /// it off the old one in the same step — a demoted system can never
        /// leave a light behind, because there is only ever the one light.
        /// Re-resolves when the star reference has died under us, which is what
        /// a Full → Warm → Full round trip through the same system looks like.
        /// </summary>
        private void SyncKeyLightOwnership()
        {
            string fullId = streaming != null ? streaming.FullSystemId : null;
            bool sameSystemStillLit = fullId == litSystemId && (fullId == null || litStar != null);
            if (sameSystemStillLit) return;

            litSystemId = fullId;
            litStar = null;
            litStarRadius = 0f;

            if (fullId != null && streaming != null)
            {
                Transform starTransform = streaming.TryGetStarTransform(fullId);
                var star = starTransform != null ? starTransform.GetComponent<IKeyLightSource>() : null;
                if (star != null)
                {
                    litStar = starTransform;
                    litStarRadius = Mathf.Max(1f, star.Radius);
                    // LGT-02: the star's class scales the base intensity, so a
                    // red dwarf's system is genuinely dimmer than a blue giant's.
                    keyBaseIntensity = LightingRig.StarIntensity * Mathf.Max(0f, star.KeyLightIntensityScale);
                    if (keyLight != null)
                    {
                        keyLight.color = star.KeyLightColor;
                        keyLight.range = star.KeyLightRange;
                    }
                }
            }

            if (keyLight != null) keyLight.gameObject.SetActive(litStar != null);
        }

        /// <summary>
        /// Reads the light's position straight off the star's transform every
        /// frame. That is the whole floating-origin story: when the world
        /// shifts, the star moves and the light is already where it should be,
        /// with no offset arithmetic of its own to drift or double-apply.
        /// </summary>
        private void DriveKeyLight()
        {
            if (keyLight == null || litStar == null) return;

            Vector3 starPosition = litStar.position;
            keyLight.transform.position = starPosition;

            float proximity = 0f;
            if (cameraTransform != null)
            {
                float distance = Vector3.Distance(cameraTransform.position, starPosition);
                proximity = 1f - Mathf.Clamp01((distance - litStarRadius * 3f) / (litStarRadius * 30f));
            }

            keyLight.intensity = LightingAccessibility.ScaleIntensity(
                keyBaseIntensity * (1f + proximity * ProximityIntensityBoost));
        }

        private void OnDestroy()
        {
            transientLights?.ReleaseAll();
            // Deliberately NO static cleanup here. Unity defers Destroy to the
            // end of the frame, but RestartCampaign builds the next session
            // immediately — so anything global wiped from this OnDestroy would
            // land on the session that already replaced us, destroying its
            // material variants and unsubscribing its bridge. Session-scoped
            // statics are therefore reset at session START, in
            // LightingInstaller; the combat bridge unsubscribes itself, which
            // is precise and ordering-independent.
            streaming = null;
            shipLighting = null;
            cameraTransform = null;
            litStar = null;
        }
    }
}
