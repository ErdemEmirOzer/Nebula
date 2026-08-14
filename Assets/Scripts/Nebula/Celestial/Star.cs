using UnityEngine;
using Nebula.Sectors;
using Nebula.Visuals;

namespace Nebula.Celestial
{
    /// <summary>
    /// A sun. Anchored in place (it exerts gravity but is not moved by it),
    /// carries layered additive halos whose brightness rises as the camera
    /// approaches, and a procedural lens flare. Colors are parameterized so
    /// systems can have differently tinted stars.
    /// <para>
    /// A star does not create a real-time light. It describes the light it
    /// would cast — <see cref="KeyLightColor"/> and <see cref="KeyLightRange"/>
    /// — and <see cref="NebulaLightingDirector"/> decides whether to realize
    /// it, which it does for the single Full system only. Every other star in
    /// the sky, decorative or Warm, reads entirely through its halos. With
    /// hundreds of stars visible at once, one real light is the difference
    /// between a frame budget and a slideshow.
    /// </para>
    /// </summary>
    public class Star : CelestialBody, IKeyLightSource
    {
        /// <summary>Colour the key light takes when this star is the one lighting the session.</summary>
        public Color KeyLightColor { get; private set; }

        /// <summary>Reach the key light takes when this star is the one lighting the session.</summary>
        public float KeyLightRange { get; private set; }

        /// <summary>Intensity multiplier from this star's class — see <see cref="CelestialLightingProfiles"/>.</summary>
        public float KeyLightIntensityScale { get; private set; } = 1f;

        public void InitializeStar(float bodyRadius, float bodyDensity, float lightRange,
            Color lightColor, Color surfaceColorLow, Color surfaceColorHigh, Color glowTint,
            StarClass? starClass = null)
        {
            Configure(CelestialBodyType.Star, bodyRadius, bodyDensity, movedByGravity: false);

            // LGT-02: a star's class decides how it lights its system. The
            // colour and reach the caller passed are the manifest's request;
            // the class profile scales them and sets the intensity.
            if (starClass.HasValue)
            {
                (Color classColor, float intensityScale, float classRange) =
                    CelestialLightingResolver.ResolveStarKeyLight(starClass.Value, lightRange);
                lightColor = classColor;
                lightRange = classRange;
                KeyLightIntensityScale = intensityScale;
            }

            KeyLightColor = lightColor;
            KeyLightRange = lightRange;

            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            surface.name = "Surface";
            surface.transform.SetParent(transform, false);
            surface.transform.localScale = Vector3.one * (bodyRadius * 2f);
            Texture2D surfaceTexture = starClass.HasValue
                ? NebulaProducedVisualResolver.StarSurface(starClass.Value)
                : null;
            if (surfaceTexture == null)
                surfaceTexture = MaterialLibrary.CreatePerlinTexture(
                    256, surfaceColorLow, surfaceColorHigh, 7f);
            else
                surfaceTexture.wrapMode = TextureWrapMode.Repeat;
            surface.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.CreateUnlit(Color.white, surfaceTexture);

            var glow = new GameObject("Glow");
            glow.transform.SetParent(transform, false);
            Texture2D corona = starClass.HasValue
                ? NebulaProducedVisualResolver.StarCorona(starClass.Value)
                : null;
            if (corona != null) corona.wrapMode = TextureWrapMode.Clamp;
            glow.AddComponent<SunGlow>().Initialize(bodyRadius, glowTint, corona);

            CameraLensFlare.RegisterStar(this, glowTint, bodyRadius);
        }
    }
}
