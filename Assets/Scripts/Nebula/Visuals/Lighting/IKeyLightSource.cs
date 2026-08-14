using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// What a star tells the lighting director about the light it would cast.
    /// A star describes; <see cref="NebulaLightingDirector"/> decides whether
    /// to realize it, and does so for the single Full system only.
    /// <para>
    /// Narrow on purpose: the director has no business knowing about a star's
    /// mesh, corona, gravity or orbit, and a test should not have to build any
    /// of those to check where the light ends up.
    /// </para>
    /// </summary>
    public interface IKeyLightSource
    {
        Color KeyLightColor { get; }
        float KeyLightRange { get; }

        /// <summary>
        /// Multiplies <see cref="LightingRig.StarIntensity"/>. This is what
        /// makes a red dwarf's system dimmer than a blue giant's rather than
        /// merely differently tinted.
        /// </summary>
        float KeyLightIntensityScale { get; }

        /// <summary>Drives the proximity brightening as the camera closes in.</summary>
        float Radius { get; }
    }
}
