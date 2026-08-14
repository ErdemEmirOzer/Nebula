using UnityEngine;
using UnityEngine.Rendering;

namespace Nebula.Visuals
{
    /// <summary>
    /// Small render-only carrier for the shared sprite kit. It owns no damage,
    /// timing, targeting, or save state; callers emit it only after the real
    /// gameplay authority has accepted an event.
    /// </summary>
    public sealed class NebulaVfxPulse : MonoBehaviour
    {
        private Material material;
        private Color tint;
        private float duration;
        private float elapsed;
        private float targetSize;

        public static GameObject Emit(
            NebulaCommonVfx vfx, Vector3 worldPosition, float size,
            Color color, float durationSeconds = 0.45f, Transform parent = null)
        {
            Texture2D texture = NebulaProducedVisualResolver.VfxTexture(vfx);
            if (texture == null) return null;

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "VFX :: " + vfx;
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            if (parent != null) quad.transform.SetParent(parent, true);
            quad.transform.position = worldPosition;
            quad.transform.localScale = Vector3.zero;
            quad.AddComponent<Billboard>();

            var pulse = quad.AddComponent<NebulaVfxPulse>();
            pulse.tint = color;
            pulse.duration = Mathf.Max(0.05f, durationSeconds);
            pulse.targetSize = Mathf.Max(0.01f, size);
            pulse.material = MaterialLibrary.CreateAdditive(color, texture);
            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = pulse.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return quad;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float scale = targetSize * Mathf.Sin(progress * Mathf.PI);
            transform.localScale = Vector3.one * scale;
            Color color = tint;
            color.a *= 1f - progress;
            if (material != null)
            {
                if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            }
            if (progress >= 1f)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
