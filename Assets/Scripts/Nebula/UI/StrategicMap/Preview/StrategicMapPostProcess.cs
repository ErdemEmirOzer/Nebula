using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// The map's finishing pass: colour filter, grade, haze, grain, scan band, scanlines
    /// and vignette, applied to the map camera's output only.
    ///
    /// This sits on the map camera rather than the main one on purpose. The reference's
    /// stack is CSS on the map element, so it applies to the map and nothing else; putting
    /// it on the main camera would grade the whole game.
    ///
    /// Under reduced motion the animated layers hold still rather than disappearing --
    /// the grain, the scan band and the scanlines stay where they are.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class StrategicMapPostProcess : MonoBehaviour
    {
        private const string ShaderResourcePath = "StrategicMap/StrategicMapPostProcess";

        private Material material;
        private float elapsed;
        private float focusPulseAge = FocusPulseSeconds;

        /// <summary>Reference: nhm-focus-lock runs for 900ms.</summary>
        private const float FocusPulseSeconds = 0.9f;

        /// <summary>The reference dims its scanlines when a system is focused.</summary>
        public bool SystemView { get; set; }

        /// <summary>
        /// Starts the focus lock: a ring that expands from the centre of the frame and fades,
        /// the reference's confirmation that a level change was actually registered.
        /// </summary>
        public void TriggerFocusPulse() => focusPulseAge = 0f;

        public static StrategicMapPostProcess Attach(Camera camera)
        {
            var post = camera.gameObject.AddComponent<StrategicMapPostProcess>();
            post.Initialise();
            return post;
        }

        private void Initialise()
        {
            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning("[StrategicMap] post-process shader missing; the map will render ungraded");
                enabled = false;
                return;
            }
            material = new Material(shader) { name = "StrategicMapPostProcess", hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_Cyan", StrategicMapTheme.Cyan);
            material.SetColor("_Violet", StrategicMapTheme.Violet);
            material.SetColor("_Blue", StrategicMapTheme.Blue);
            material.SetColor("_Gold", StrategicMapTheme.Gold);
            material.SetColor("_Space", StrategicMapTheme.Space);
            material.SetColor("_Highlight", StrategicMapTheme.SpaceText);
        }

        private void Update()
        {
            if (StrategicMapTheme.ReducedMotion) return;
            elapsed += Time.unscaledDeltaTime;
            if (focusPulseAge < FocusPulseSeconds) focusPulseAge += Time.unscaledDeltaTime;
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }
            material.SetFloat("_Time01", elapsed);
            material.SetFloat("_SystemView", SystemView ? 1f : 0f);
            material.SetFloat("_FocusPulse", Mathf.Clamp01(focusPulseAge / FocusPulseSeconds));
            Graphics.Blit(source, destination, material);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
