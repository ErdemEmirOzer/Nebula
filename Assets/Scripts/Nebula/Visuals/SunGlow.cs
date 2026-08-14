using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Layered camera-facing additive halos around a star, colored from the
    /// star's glow tint. Fakes intense HDR bloom on the built-in pipeline,
    /// pulses slowly, and brightens as the camera approaches so getting close
    /// to a star feels overwhelming.
    /// <para>
    /// Halos only — this is what a star looks like, not a light source. The
    /// matching brightening of the actual key light belongs to
    /// <see cref="NebulaLightingDirector"/>, which owns the only real-time
    /// light in the session.
    /// </para>
    /// </summary>
    public class SunGlow : MonoBehaviour
    {
        private struct HaloLayer
        {
            public Transform transform;
            public Material material;
            public Color baseColor;
            public float baseSize;
            public float pulsePhase;
        }

        // sizeFactor: quad size relative to star DIAMETER, so a layer reaches
        // sizeFactor star radii from the centre. whiteBlend: how far the layer
        // colour is pushed from the tint toward white.
        //
        // Two layers, both close. The previous table had four, out to 3.6 -- and
        // its own comment claimed that stayed inside firstOrbitRadius "which is at
        // least 4.5 x the star radius". It does not: 3.6 diameters is 3.6 radii from
        // the centre before the close-range swell, 4.9 after, so the faint far glow
        // was drawn over the innermost planet and, at flying distance, filled nearly
        // half the frame. Four copies of one corona texture at four scales also
        // stacked into concentric rings -- the star read as a mandala rather than as
        // a sun.
        //
        // What is left is the star's own limb and one corona just outside it. The
        // reach is asserted against MaxReachRadii rather than left to a comment, so
        // the numbers and the intent cannot come apart again.
        //
        // The inner layer is 1.45 rather than something tighter because the corona
        // texture is a ring, not a disc: its bright band sits at roughly 0.75 of the
        // quad's half-width, so a 1.1 layer put its band at 0.83 radii -- entirely
        // behind the sphere -- and left a dark gap between the surface and the
        // corona that read as a rendering fault.
        private static readonly (float sizeFactor, float whiteBlend, float alpha)[] LayerDefinitions =
        {
            (1.45f, 0.55f, 0.55f), // the rim, sized so its bright ring lands on the limb
            (2.10f, 0.22f, 0.22f)  // the close corona, and nothing beyond it
        };

        /// <summary>
        /// How far the presentation reaches from the star's centre, in star radii,
        /// at the widest it ever gets — authored size, full pulse, full swell.
        /// <para>
        /// The number a test can hold the art to. <c>StarSystemSettings</c>
        /// guarantees at least 4.5 radii between a star and its first orbit, so
        /// anything under that can never be drawn over a planet.
        /// </para>
        /// </summary>
        public static float MaxReachRadii
        {
            get
            {
                float widest = 0f;
                for (int i = 0; i < LayerDefinitions.Length; i++)
                    widest = Mathf.Max(widest, LayerDefinitions[i].sizeFactor);
                return widest * (1f + MaxPulse) * (1f + MaxProximitySwell);
            }
        }

        /// <summary>
        /// How much wider than its authored size a layer may get at point-blank
        /// range. Named so the reach can be checked at its widest rather than at
        /// rest — a halo that only fits when the camera is far away is not a halo
        /// that fits.
        /// </summary>
        public const float MaxProximitySwell = 0.15f;

        /// <summary>Amplitude of the slow breathing scale, as a fraction of the layer's size.</summary>
        public const float MaxPulse = 0.045f;

        /// <summary>
        /// How much brighter a layer gets at point-blank range.
        /// <para>
        /// Was 1.5, which on an additive quad with 0.95 alpha turned the approach
        /// into a white screen. Flying close to a star should be dramatic and still
        /// leave the star visible inside its own light.
        /// </para>
        /// </summary>
        public const float MaxProximityBrightness = 0.5f;

        private HaloLayer[] layers;
        private float starRadius;
        private Camera cachedCamera;
        private int frameOffset;

        public void Initialize(float radius, Color tint, Texture2D coronaTexture = null)
        {
            starRadius = radius;
            frameOffset = Mathf.Abs(GetInstanceID()) % 16; // staggers far-star refreshes

            float diameter = radius * 2f;
            layers = new HaloLayer[LayerDefinitions.Length];

            for (int i = 0; i < LayerDefinitions.Length; i++)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Halo " + i;
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(transform, false);
                quad.AddComponent<Billboard>();

                Color color = Color.Lerp(tint, Color.white, LayerDefinitions[i].whiteBlend);
                color.a = LayerDefinitions[i].alpha;

                Material material = MaterialLibrary.CreateAdditive(
                    color, coronaTexture != null ? coronaTexture : MaterialLibrary.RadialGlowTexture);
                var meshRenderer = quad.GetComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;

                float size = diameter * LayerDefinitions[i].sizeFactor;
                quad.transform.localScale = Vector3.one * size;

                layers[i] = new HaloLayer
                {
                    transform = quad.transform,
                    material = material,
                    baseColor = color,
                    baseSize = size,
                    pulsePhase = i * 1.7f
                };
            }
        }

        private void LateUpdate()
        {
            if (layers == null) return;

            if (cachedCamera == null)
            {
                cachedCamera = Camera.main;
            }

            // 0 far away -> 1 right at the star; plus a long-range dimming so
            // stars stop glaring from across the galaxy.
            float proximity = 0f;
            float farFade = 1f;
            if (cachedCamera != null)
            {
                float distance = Vector3.Distance(cachedCamera.transform.position, transform.position);
                // Distant halos change imperceptibly frame to frame; with 100+
                // stars, refreshing each only every 16th frame (staggered)
                // slashes per-frame material updates.
                if (distance > starRadius * 600f && (Time.frameCount + frameOffset) % 16 != 0) return;
                proximity = 1f - Mathf.Clamp01((distance - starRadius * 3f) / (starRadius * 30f));
                farFade = Mathf.Lerp(1f, 0.22f,
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(starRadius * 80f, starRadius * 500f, distance)));
            }

            float brightness = LightingAccessibility.ScaleIntensity(
                (1f + proximity * MaxProximityBrightness) * farFade);

            for (int i = 0; i < layers.Length; i++)
            {
                HaloLayer layer = layers[i];
                float pulse = 1f + MaxPulse * Mathf.Sin(Time.time * 0.7f + layer.pulsePhase);
                layer.transform.localScale = Vector3.one * (layer.baseSize * pulse * (1f + proximity * MaxProximitySwell));

                Color color = layer.baseColor;
                color.r *= brightness;
                color.g *= brightness;
                color.b *= brightness;
                if (layer.material.HasProperty("_TintColor")) layer.material.SetColor("_TintColor", color);
                if (layer.material.HasProperty("_Color")) layer.material.SetColor("_Color", color);
            }
        }
    }
}
