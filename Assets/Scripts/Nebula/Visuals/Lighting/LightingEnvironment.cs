using UnityEngine;
using UnityEngine.Rendering;

namespace Nebula.Visuals
{
    /// <summary>
    /// Deep space as a lighting environment: the global render state that has
    /// to be true before a single light is placed.
    /// <para>
    /// There is no skybox to bounce ambient light off and nothing to bake, so
    /// ambient is a flat near-black floor: enough that an unlit hull is a cold
    /// silhouette rather than a hole in the frame, low enough that a star
    /// arriving is still an event. Space stays dark — the scene is never
    /// washed with grey fill.
    /// </para>
    /// <para>
    /// Deliberately does not touch <see cref="RenderSettings.skybox"/>: the
    /// starfield bake in <c>SkyboxBaker</c> owns that, and overwriting it here
    /// would erase the star field.
    /// </para>
    /// </summary>
    public static class LightingEnvironment
    {
        /// <summary>
        /// The floor every surface gets for free, before accessibility. Slightly
        /// blue so unlit sides read as cold space rather than as a rendering
        /// fault.
        /// </summary>
        public static readonly Color DeepSpaceAmbient = new Color(0.028f, 0.032f, 0.05f);

        /// <summary>
        /// The two <see cref="QualitySettings"/> fields this class overrides, as
        /// they were before any session touched them, plus the quality level they
        /// were read from. -1 means "nothing overridden right now".
        /// <para>
        /// These exist because <see cref="RenderSettings"/> is per-scene and
        /// resets itself on the next scene load, while <see cref="QualitySettings"/>
        /// is per-PROJECT and does not: in the Editor a value written here during
        /// play mode is serialized straight back into
        /// <c>ProjectSettings/QualitySettings.asset</c> and inherited by every
        /// later run, including a fresh one. That is not a theory — the tree
        /// carried the receipt: the Ultra tier's authored <c>pixelLightCount: 4</c>
        /// and <c>shadowDistance: 150</c> had become <c>2</c> and <c>0</c>, which
        /// are exactly <see cref="LightingQualityProfile.Medium"/>'s two numbers.
        /// A session had written its own budget into the project and never given
        /// it back.
        /// </para>
        /// </summary>
        private static int overriddenAtQualityLevel = -1;
        private static int capturedPixelLightCount;
        private static float capturedShadowDistance;

        /// <summary>
        /// Applies the profile's global render budget plus the ambient floor.
        /// Fog stays off: it is a fill light by another name and would grey out
        /// the black the art direction depends on.
        /// </summary>
        public static void Apply(in LightingQualityProfile profile)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ResolveAmbient();
            RenderSettings.fog = false;

            // Captured on the first override only, so a mid-session quality switch
            // (Low -> High -> Medium) still restores what the PROJECT authored
            // rather than what the previous switch happened to leave.
            if (overriddenAtQualityLevel < 0)
            {
                overriddenAtQualityLevel = QualitySettings.GetQualityLevel();
                capturedPixelLightCount = QualitySettings.pixelLightCount;
                capturedShadowDistance = QualitySettings.shadowDistance;
            }

            QualitySettings.pixelLightCount = profile.PixelLightBudget;
            QualitySettings.shadowDistance = profile.ShadowDistance;
        }

        /// <summary>
        /// Puts the two project-wide render-budget values back exactly as they
        /// were before this session first overrode them. Called from
        /// <c>NebulaSessionLifetime.ResetProcessWideState</c>, which is the one
        /// place that states what "a session is over" means for static state.
        /// <para>
        /// Both setters write into whichever quality LEVEL is current, so if the
        /// player has changed tiers since the override, the captured numbers no
        /// longer belong to the tier we would be writing them into. The override is
        /// dropped rather than misapplied: corrupting a tier we never touched is
        /// strictly worse than leaving one we did. Restoring the right tier would
        /// mean switching the level back and forth, and the quality level is the
        /// player's setting, not this class's to move.
        /// </para>
        /// </summary>
        public static void RestoreProcessWideBudget()
        {
            if (overriddenAtQualityLevel < 0) return;

            int level = overriddenAtQualityLevel;
            overriddenAtQualityLevel = -1;
            if (QualitySettings.GetQualityLevel() != level) return;

            QualitySettings.pixelLightCount = capturedPixelLightCount;
            QualitySettings.shadowDistance = capturedShadowDistance;
        }

        /// <summary>The ambient floor actually in force, after the visual-only accessibility settings.</summary>
        public static Color ResolveAmbient() => LightingAccessibility.ResolveAmbient(DeepSpaceAmbient);
    }
}
