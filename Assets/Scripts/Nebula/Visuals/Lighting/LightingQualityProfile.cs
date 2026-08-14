using UnityEngine;
using UnityEngine.Rendering;

namespace Nebula.Visuals
{
    /// <summary>The three lighting cost tiers Nebula ships. Never serialized — see <see cref="LightingQualityProfile"/>.</summary>
    public enum LightingQuality
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>
    /// What one quality tier is allowed to spend on lighting. Held as data so
    /// the director can be handed a profile in a test without a scene, and so
    /// "what does High actually cost?" is answerable from one file.
    /// <para>
    /// A profile is a presentation budget and nothing else: it is never
    /// written to a save, never feeds simulation, and switching tiers at
    /// runtime changes only how the same world is drawn.
    /// </para>
    /// </summary>
    public readonly struct LightingQualityProfile
    {
        public readonly LightingQuality Quality;

        /// <summary>Ceiling on simultaneous real-time lights the director will allow, key light included.</summary>
        public readonly int MaxRealtimeLights;

        /// <summary>What goes into <see cref="QualitySettings.pixelLightCount"/>. Everything past it falls back to per-vertex.</summary>
        public readonly int PixelLightBudget;

        /// <summary>Only ever applied to the single key light — see <see cref="ShadowCasterBudget"/>.</summary>
        public readonly LightShadows KeyLightShadows;

        public readonly float ShadowDistance;
        public readonly LightShadowResolution ShadowResolution;

        /// <summary>How many lights the transient pool preallocates. Hard ceiling on effect lights — the pool never grows.</summary>
        public readonly int TransientLightCapacity;

        /// <summary>When set, effects that cannot get a pool light are expected to read as emissive only rather than to wait or to force a light.</summary>
        public readonly bool EmissiveOnlyFallback;

        /// <summary>
        /// Multiplies the distance at which a transient light stops being
        /// worth drawing. Below 1 the cheap tiers cull effect lights closer to
        /// the camera; above 1 the expensive tier keeps them alive further out.
        /// </summary>
        public readonly float CullDistanceScale;

        /// <summary>At most one shadow caster, ever: the key light. Cube-map shadows for every point light is the one cost this game cannot absorb.</summary>
        public int ShadowCasterBudget => KeyLightShadows == LightShadows.None ? 0 : 1;

        public LightingQualityProfile(LightingQuality quality, int maxRealtimeLights, int pixelLightBudget,
            LightShadows keyLightShadows, float shadowDistance, LightShadowResolution shadowResolution,
            int transientLightCapacity, bool emissiveOnlyFallback, float cullDistanceScale)
        {
            Quality = quality;
            MaxRealtimeLights = maxRealtimeLights;
            PixelLightBudget = pixelLightBudget;
            KeyLightShadows = keyLightShadows;
            ShadowDistance = shadowDistance;
            ShadowResolution = shadowResolution;
            TransientLightCapacity = transientLightCapacity;
            EmissiveOnlyFallback = emissiveOnlyFallback;
            CullDistanceScale = cullDistanceScale;
        }

        /// <summary>
        /// Integrated graphics and mobile. One per-pixel light — the star —
        /// and everything else per-vertex or emissive.
        /// </summary>
        public static readonly LightingQualityProfile Low = new LightingQualityProfile(
            LightingQuality.Low, maxRealtimeLights: 6, pixelLightBudget: 1,
            keyLightShadows: LightShadows.None, shadowDistance: 0f, shadowResolution: LightShadowResolution.Low,
            transientLightCapacity: 4, emissiveOnlyFallback: true, cullDistanceScale: 0.45f);

        /// <summary>
        /// The default, and the fallback whenever a profile cannot be
        /// resolved. Star plus hull fill per-pixel, still no shadows: point
        /// lights pay for a cube map, which is not worth it at this tier.
        /// </summary>
        public static readonly LightingQualityProfile Medium = new LightingQualityProfile(
            LightingQuality.Medium, maxRealtimeLights: 10, pixelLightBudget: 2,
            keyLightShadows: LightShadows.None, shadowDistance: 0f, shadowResolution: LightShadowResolution.Medium,
            transientLightCapacity: 8, emissiveOnlyFallback: true, cullDistanceScale: 1f);

        /// <summary>
        /// The tier that buys the heavy industrial look: the key light casts
        /// soft shadows, so hull plating and structures read as solid objects
        /// rather than as flat shaded shells. Shadow distance is deliberately
        /// small — shadows matter on the ship and what it is next to, not
        /// across a solar system.
        /// </summary>
        public static readonly LightingQualityProfile High = new LightingQualityProfile(
            LightingQuality.High, maxRealtimeLights: 16, pixelLightBudget: 3,
            keyLightShadows: LightShadows.Soft, shadowDistance: 600f, shadowResolution: LightShadowResolution.High,
            transientLightCapacity: 12, emissiveOnlyFallback: false, cullDistanceScale: 1.6f);

        /// <summary>
        /// Never throws and never returns a half-built profile: anything that
        /// is not a tier we ship resolves to <see cref="Medium"/>. A bad
        /// settings value must cost the player some fidelity, never a session.
        /// </summary>
        public static LightingQualityProfile Resolve(LightingQuality quality)
        {
            switch (quality)
            {
                case LightingQuality.Low: return Low;
                case LightingQuality.High: return High;
                case LightingQuality.Medium: return Medium;
                default: return Medium;
            }
        }

        /// <summary>Case-insensitive name lookup for settings files and console commands. Unknown names land on <see cref="Medium"/> and report false.</summary>
        public static bool TryResolve(string name, out LightingQualityProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                string trimmed = name.Trim();
                if (string.Equals(trimmed, "Low", System.StringComparison.OrdinalIgnoreCase)) { profile = Low; return true; }
                if (string.Equals(trimmed, "Medium", System.StringComparison.OrdinalIgnoreCase)) { profile = Medium; return true; }
                if (string.Equals(trimmed, "High", System.StringComparison.OrdinalIgnoreCase)) { profile = High; return true; }
            }

            profile = Medium;
            return false;
        }

        /// <summary>
        /// Maps Unity's own six-rung quality ladder onto our three tiers, so
        /// the player's existing quality choice picks the lighting budget
        /// instead of being silently overridden. Anything out of range lands
        /// on <see cref="Medium"/>.
        /// </summary>
        public static LightingQualityProfile FromUnityQualityLevel(int unityQualityLevel)
        {
            if (unityQualityLevel <= 1) return Low;      // Very Low, Low
            if (unityQualityLevel <= 3) return Medium;   // Medium, High
            if (unityQualityLevel <= 5) return High;     // Very High, Ultra
            return Medium;
        }

        /// <summary>The profile implied by the live <see cref="QualitySettings"/> level.</summary>
        public static LightingQualityProfile FromQualitySettings()
            => FromUnityQualityLevel(QualitySettings.GetQualityLevel());
    }
}
