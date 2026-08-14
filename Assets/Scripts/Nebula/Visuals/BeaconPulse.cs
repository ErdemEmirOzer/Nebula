using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Slow sine pulse of scale (and tint alpha when the material supports
    /// it). Gives beacons and anomaly glows a living rhythm so points of
    /// interest can be spotted against the static backdrop.
    /// </summary>
    public class BeaconPulse : MonoBehaviour
    {
        [SerializeField] private float period = 2.5f;
        [Tooltip("Scale/alpha at the bottom of the pulse, as a fraction of the base value.")]
        [SerializeField] private float minimum = 0.55f;

        private Vector3 baseScale;
        private Material material;
        private Color baseColor;
        private float phase;

        public void Configure(float pulsePeriod, float pulseMinimum)
        {
            period = Mathf.Max(0.1f, pulsePeriod);
            minimum = Mathf.Clamp01(pulseMinimum);
        }

        private void Start()
        {
            // Start, not Awake: generators set the final scale right after AddComponent.
            baseScale = transform.localScale;
            phase = Random.Range(0f, Mathf.PI * 2f);

            var attachedRenderer = GetComponent<Renderer>();
            if (attachedRenderer != null && attachedRenderer.sharedMaterial != null
                && attachedRenderer.sharedMaterial.HasProperty("_TintColor"))
            {
                material = attachedRenderer.sharedMaterial;
                baseColor = material.GetColor("_TintColor");
            }
        }

        private void Update()
        {
            float t = (Mathf.Sin(Time.time * (Mathf.PI * 2f) / period + phase) + 1f) * 0.5f;
            float k = Mathf.Lerp(minimum, 1f, t);
            transform.localScale = baseScale * k;
            if (material != null)
            {
                Color color = baseColor;
                color.a *= k;
                material.SetColor("_TintColor", color);
            }
        }
    }
}
