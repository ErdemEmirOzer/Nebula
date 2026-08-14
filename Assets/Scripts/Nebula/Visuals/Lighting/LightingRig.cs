using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Every light role in Nebula, one named spec each. Before this existed the
    /// same handful of numbers were retyped inside <c>Star</c>,
    /// <c>BlackHole</c>, <c>ShipFactory</c> and the preview stages, so nobody
    /// could answer "how bright is a sun compared to the hull fill?" without
    /// grepping four files. Roles are named after what they do in the frame
    /// (key, fill, source), not after the object that happens to carry them.
    /// </summary>
    public static class LightingRig
    {
        /// <summary>
        /// Base intensity of the star key light.
        /// <see cref="NebulaLightingDirector"/> drives it up from here as the
        /// camera closes in, so this is the far-away floor rather than the
        /// value seen up close.
        /// </summary>
        public const float StarIntensity = 2.4f;

        /// <summary>
        /// Cool fill riding on the player's hull, so the ship stays readable in
        /// the long stretches between stars where the key light is effectively
        /// gone. Short range on purpose — it is there to shape the hull, not to
        /// light the neighbourhood.
        /// </summary>
        public static readonly LightSpec ShipHullFill =
            LightSpec.Point(new Color(0.75f, 0.85f, 1f), 0.7f, 14f);

        /// <summary>
        /// Key light for an off-screen preview stage (hangar, progression ship
        /// portrait). Culled to the preview layer so it never leaks onto the
        /// live scene sharing the same world.
        /// </summary>
        public static LightSpec PreviewKey(int previewLayer)
            => LightSpec.Directional(Color.white, 1.1f, cullingMask: 1 << previewLayer);

        /// <summary>
        /// The one real-time key light in a session, sitting on the active Full
        /// system's star. A point light rather than a directional one because
        /// inside a system every planet has to be lit from where the star
        /// actually is — a single direction would light the far side of the
        /// system from the wrong angle.
        /// <para>
        /// Shadows are decided by the quality profile, not here: only this
        /// light is ever allowed to cast them.
        /// </para>
        /// </summary>
        public static LightSpec SystemKey(Color lightColor, float range)
            => LightSpec.Point(lightColor, StarIntensity, range);

        /// <summary>
        /// The shape every pooled effect light starts as. Values are
        /// placeholders — <see cref="TransientLightPool"/> overwrites colour,
        /// intensity and range on each claim — but the render settings here
        /// hold for the whole pool: never a shadow caster, and never promoted
        /// past the per-pixel budget the profile set.
        /// </summary>
        public static readonly LightSpec TransientEffect =
            LightSpec.Point(Color.white, 0f, 1f);
    }
}
