using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// One light described as data instead of as a dozen assignment statements
    /// at whatever factory happens to spawn the object. Every light Nebula
    /// creates comes from a named spec in <see cref="LightingRig"/>, so the
    /// numbers that decide how the game is lit sit in one readable place and
    /// can be asserted on in edit-mode tests without entering play mode.
    /// <para>
    /// Deliberately in the flat <c>Nebula.Visuals</c> namespace even though it
    /// lives in a <c>Lighting/</c> folder: the folder gives the subsystem a
    /// home, while the flat namespace means no existing call site needs a new
    /// <c>using</c>.
    /// </para>
    /// </summary>
    public readonly struct LightSpec
    {
        public readonly LightType Type;
        public readonly Color Color;
        public readonly float Intensity;

        /// <summary>Reach of a point or spot light. Meaningless for directional lights, which reach everywhere.</summary>
        public readonly float Range;

        public readonly LightShadows Shadows;

        /// <summary>
        /// <see cref="LightRenderMode.ForceVertex"/> demotes a light to
        /// per-vertex, which is how scenery survives in bulk — see
        /// <see cref="LightingRig.Star"/>.
        /// </summary>
        public readonly LightRenderMode RenderMode;

        /// <summary>Layer mask this light illuminates. <c>-1</c> (every layer) unless a preview stage narrows it.</summary>
        public readonly int CullingMask;

        public LightSpec(LightType type, Color color, float intensity, float range,
            LightShadows shadows = LightShadows.None,
            LightRenderMode renderMode = LightRenderMode.Auto,
            int cullingMask = -1)
        {
            Type = type;
            Color = color;
            Intensity = intensity;
            Range = range;
            Shadows = shadows;
            RenderMode = renderMode;
            CullingMask = cullingMask;
        }

        public static LightSpec Point(Color color, float intensity, float range,
            LightShadows shadows = LightShadows.None,
            LightRenderMode renderMode = LightRenderMode.Auto)
            => new LightSpec(LightType.Point, color, intensity, range, shadows, renderMode);

        public static LightSpec Directional(Color color, float intensity,
            LightShadows shadows = LightShadows.None, int cullingMask = -1)
            => new LightSpec(LightType.Directional, color, intensity, 0f, shadows, LightRenderMode.Auto, cullingMask);

        public LightSpec WithColor(Color color)
            => new LightSpec(Type, color, Intensity, Range, Shadows, RenderMode, CullingMask);

        public LightSpec WithIntensity(float intensity)
            => new LightSpec(Type, Color, intensity, Range, Shadows, RenderMode, CullingMask);

        public LightSpec WithRange(float range)
            => new LightSpec(Type, Color, Intensity, range, Shadows, RenderMode, CullingMask);

        public LightSpec WithCullingMask(int cullingMask)
            => new LightSpec(Type, Color, Intensity, Range, Shadows, RenderMode, cullingMask);

        /// <summary>
        /// Demotes the light to per-vertex. Only worth it for scenery that
        /// exists in the hundreds; see <see cref="LightingRig.Star"/> for why.
        /// </summary>
        public LightSpec AsVertexLight()
            => new LightSpec(Type, Color, Intensity, Range, Shadows, LightRenderMode.ForceVertex, CullingMask);

        /// <summary>Writes this spec onto a light that already exists.</summary>
        public void ApplyTo(Light light)
        {
            if (light == null) return;

            light.type = Type;
            light.color = Color;
            light.intensity = Intensity;
            if (Type != LightType.Directional) light.range = Range;
            light.shadows = Shadows;
            light.renderMode = RenderMode;
            light.cullingMask = CullingMask;
        }

        /// <summary>
        /// Creates a child GameObject carrying this light — the shape every
        /// call site used to hand-roll. The caller still positions and orients
        /// the transform, because where a light sits is scene composition
        /// rather than part of the light's identity.
        /// </summary>
        public Light Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light>();
            ApplyTo(light);
            return light;
        }
    }
}
