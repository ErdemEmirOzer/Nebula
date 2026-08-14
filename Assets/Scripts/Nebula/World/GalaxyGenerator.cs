using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Nebula.Celestial;
using Nebula.Visuals;

namespace Nebula.World
{
    /// <summary>
    /// Builds a realistically structured spiral galaxy. The visible body comes
    /// from a layer of ~1500 huge nebula-textured cloud sprites lying in the
    /// galactic plane — overlapping additively they form fat, milky spiral
    /// arms with a glowing warm core, exactly like long-exposure photos —
    /// while two tiers of fine dust sprites (cross-faded by camera distance,
    /// see <see cref="GalaxyDustFader"/>) add grain up close. All layers are
    /// static meshes (one draw call each; 150k GameObjects would be
    /// impossible). The first arm is phased to pass through the play area
    /// (world origin), the whole structure slowly rotates, and a supermassive
    /// black hole sits at the core.
    /// </summary>
    public static class GalaxyGenerator
    {
        // Shared spiral geometry so dust and clouds trace the same arms.
        private struct SpiralGeometry
        {
            public float radius;
            public float bulgeRadius;
            public float r0;
            public float b;
            public float maxTheta;
            public float armPhase0;
            public int armCount;
        }

        public static GameObject Generate(string name, Vector3 center, float radius, int particleCount,
            int armCount, float rotationSpeedDegPerSec, float blackHoleRadius, float cloudWhiteness = 0f)
        {
            var root = new GameObject(name + " Galaxy");
            root.transform.position = center;
            var spin = root.AddComponent<RotateSelf>();
            spin.axis = Vector3.up;
            spin.degreesPerSecond = rotationSpeedDegPerSec;

            var geometry = new SpiralGeometry
            {
                radius = radius,
                bulgeRadius = radius * 0.13f,
                // More wraps = tightly wound arms that close in on each other
                // toward the rim, for a compact spiral.
                maxTheta = Mathf.PI * 4.6f,
                armCount = Mathf.Max(1, armCount)
            };
            geometry.r0 = geometry.bulgeRadius * 0.6f;
            geometry.b = Mathf.Log(radius / geometry.r0) / geometry.maxTheta;
            // Phase the first arm so it runs through the play area (world origin).
            Vector3 toOrigin = -center;
            float originDistance = Mathf.Max(toOrigin.magnitude, geometry.r0 * 1.5f);
            geometry.armPhase0 = Mathf.Atan2(toOrigin.z, toOrigin.x)
                                 - Mathf.Log(originDistance / geometry.r0) / geometry.b;

            var map = root.AddComponent<GalaxyMap>();
            map.Configure(radius, geometry.bulgeRadius, geometry.r0, geometry.b,
                geometry.maxTheta, geometry.armPhase0, geometry.armCount);

            var fader = root.AddComponent<GalaxyDustFader>();
            fader.Initialize(radius);

            // The cloud layer IS the galaxy's visible body: it thickens as the
            // camera pulls away (denser spiral from afar) and thins up close.
            int cloudCount = Mathf.Clamp(particleCount / 55, 800, 5400);
            BuildCloudLayer(root.transform, cloudCount, geometry, fader, cloudWhiteness);

            // Fine dust: detail up close only. Both tiers stop rendering at a
            // couple of radii out — their sprites are sub-pixel from there,
            // but with six galaxies they would still cost millions of vertices
            // every frame.
            MeshRenderer nearDust = BuildDustTier(root.transform, "Dust Near", particleCount / 2, 1.3f, 1.3f,
                geometry, cloudWhiteness);
            fader.AddTier(nearDust.sharedMaterial, nearDust, 0.5f, 1.5f, 1.2f, 0.5f, maxVisibleDistance: 1.8f);
            MeshRenderer midDust = BuildDustTier(root.transform, "Dust Mid", particleCount / 6, 7f, 0.6f,
                geometry, cloudWhiteness);
            fader.AddTier(midDust.sharedMaterial, midDust, 0.5f, 1.3f, 0f, 1f, maxVisibleDistance: 3f);

            var blackHoleGO = new GameObject("Sagittarius Core");
            blackHoleGO.transform.SetParent(root.transform, false);
            blackHoleGO.AddComponent<BlackHole>().InitializeBlackHole(blackHoleRadius);
            LocationRegistry.RegisterStar(name + " Black Hole", blackHoleGO.transform, blackHoleRadius * 15f);

            return root;
        }

        /// <summary>
        /// Huge soft clouds lying (almost) flat in the galactic plane: seen
        /// from above they overlap into fat milky arms; seen from inside the
        /// disc they foreshorten into a thin glowing band, like the real
        /// Milky Way. Split across three texture variants to hide repetition.
        /// </summary>
        private static void BuildCloudLayer(Transform root, int cloudCount, SpiralGeometry geometry,
            GalaxyDustFader fader, float whiteness)
        {
            const int variants = 3;
            int perVariant = Mathf.Max(100, cloudCount / variants);

            // whiteness pulls the whole palette toward white for pale galaxies.
            Color coreWarm = Color.Lerp(new Color(1f, 0.85f, 0.6f), Color.white, whiteness);
            Color armCool = Color.Lerp(new Color(0.6f, 0.72f, 1f), Color.white, whiteness);
            Color armWarm = Color.Lerp(new Color(1f, 0.85f, 0.68f), Color.white, whiteness);
            Color hiiPink = Color.Lerp(new Color(1f, 0.5f, 0.62f), Color.white, whiteness);
            float radius = geometry.radius;

            for (int v = 0; v < variants; v++)
            {
                var vertices = new Vector3[perVariant * 4];
                var colors = new Color32[perVariant * 4];
                var uvs = new Vector2[perVariant * 4];
                var centers = new Vector3[perVariant * 4];
                var triangles = new int[perVariant * 6];

                for (int i = 0; i < perVariant; i++)
                {
                    Vector3 pos;
                    Color color;
                    float size;
                    float category = Random.value;

                    if (category < 0.2f)
                    {
                        // Glowing core clouds.
                        pos = new Vector3(
                            Gauss() * geometry.bulgeRadius * 0.8f,
                            Gauss() * geometry.bulgeRadius * 0.5f,
                            Gauss() * geometry.bulgeRadius * 0.8f);
                        color = coreWarm;
                        color.a = Random.Range(0.1f, 0.22f);
                        size = radius * Random.Range(0.02f, 0.06f);
                    }
                    else if (category < 0.9f)
                    {
                        // Arm clouds: tight spread so the arms stay fat but distinct.
                        float t = Mathf.Sqrt(Random.value);
                        float theta = t * geometry.maxTheta;
                        float r = geometry.r0 * Mathf.Exp(geometry.b * theta);
                        float angle = theta + geometry.armPhase0
                                      + Random.Range(0, geometry.armCount) * (Mathf.PI * 2f / geometry.armCount);

                        float spread = radius * 0.01f + r * 0.035f; // narrow: keeps tight arms distinct
                        // Vertical sigma doubled for a fatter disc; keep in
                        // sync with GalaxyMap.ArmIndexAt's height tolerance.
                        pos = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r
                              + new Vector3(
                                  Gauss() * spread,
                                  Gauss() * (radius * 0.006f + r * 0.03f),
                                  Gauss() * spread);

                        float mix = Random.value;
                        color = Random.value < 0.08f ? hiiPink : Color.Lerp(armCool, armWarm, mix * mix);
                        color.a = Random.Range(0.11f, 0.21f);
                        size = radius * Random.Range(0.03f, 0.1f) * (0.55f + 0.75f * t);
                    }
                    else
                    {
                        // Faint inter-arm haze.
                        float angle = Random.Range(0f, Mathf.PI * 2f);
                        float r = radius * Mathf.Sqrt(Random.value);
                        pos = new Vector3(
                            Mathf.Cos(angle) * r,
                            Gauss() * (radius * 0.012f),
                            Mathf.Sin(angle) * r);
                        color = armCool;
                        color.a = Random.Range(0.04f, 0.08f);
                        size = radius * Random.Range(0.04f, 0.09f);
                    }

                    // Almost flat in the plane, slight tilt for organic volume
                    // (a bit more tilt fills the thicker disc seen edge-on).
                    Quaternion tilt = Quaternion.Euler(
                        Random.Range(-16f, 16f), Random.Range(0f, 360f), Random.Range(-16f, 16f));
                    WriteQuad(i, pos, size, color,
                        tilt * Vector3.right, tilt * Vector3.forward,
                        vertices, colors, uvs, centers, triangles);
                }

                // Destroying a MeshFilter's GameObject does not destroy its mesh, and
                // these are the largest allocations a session makes. Unowned, one full
                // set survived every campaign.
                var mesh = RuntimeResourceScope.Own(
                    new Mesh { name = "Galaxy Clouds " + v, indexFormat = IndexFormat.UInt32 });
                mesh.vertices = vertices;
                mesh.uv = uvs;
                mesh.SetUVs(1, centers);
                mesh.colors32 = colors;
                mesh.triangles = triangles;
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (radius * 2.4f));

                var clouds = new GameObject("Clouds " + v);
                clouds.transform.SetParent(root, false);
                clouds.AddComponent<MeshFilter>().sharedMesh = mesh;
                var cloudRenderer = clouds.AddComponent<MeshRenderer>();
                // Neutral-toned cloud texture; the hue comes from vertex colors.
                // Distance fade: clouds around the ship never rasterize, so
                // flying inside an arm does not drown the GPU in overdraw.
                Material cloudMaterial = MaterialLibrary.CreateAdditiveFade(Color.white,
                    MaterialLibrary.CreateNebulaTexture(256, Color.white, new Color(0.62f, 0.62f, 0.66f)),
                    fadeNear: 120000f, fadeFar: 450000f);
                cloudRenderer.sharedMaterial = cloudMaterial;
                cloudRenderer.shadowCastingMode = ShadowCastingMode.Off;
                cloudRenderer.receiveShadows = false;
                fader.AddTier(cloudMaterial, cloudRenderer, 0.4f, 1.6f, 0.7f, 1.5f);
            }
        }

        private static MeshRenderer BuildDustTier(Transform root, string tierName, int particleCount,
            float sizeMultiplier, float alphaMultiplier, SpiralGeometry geometry, float whiteness)
        {
            particleCount = Mathf.Clamp(particleCount, 500, 400000);
            float radius = geometry.radius;

            var vertices = new Vector3[particleCount * 4];
            var colors = new Color32[particleCount * 4];
            var uvs = new Vector2[particleCount * 4];
            var centers = new Vector3[particleCount * 4];
            var triangles = new int[particleCount * 6];

            Color bulgeColor = Color.Lerp(new Color(1f, 0.88f, 0.68f), Color.white, whiteness);
            Color armCool = Color.Lerp(new Color(0.62f, 0.72f, 1f), Color.white, whiteness);
            Color armWarm = Color.Lerp(new Color(1f, 0.86f, 0.7f), Color.white, whiteness);
            Color hiiPink = Color.Lerp(new Color(1f, 0.45f, 0.6f), Color.white, whiteness);

            for (int i = 0; i < particleCount; i++)
            {
                Vector3 pos;
                Color color;
                float size;
                float category = Random.value;

                if (category < 0.22f)
                {
                    pos = new Vector3(
                        Gauss() * geometry.bulgeRadius,
                        Gauss() * geometry.bulgeRadius * 0.65f,
                        Gauss() * geometry.bulgeRadius);
                    color = bulgeColor;
                    color.a = Random.Range(0.04f, 0.1f);
                    size = radius * Random.Range(0.0004f, 0.0012f);
                }
                else if (category < 0.88f)
                {
                    float t = Mathf.Sqrt(Random.value);
                    float theta = t * geometry.maxTheta;
                    float r = geometry.r0 * Mathf.Exp(geometry.b * theta);
                    float angle = theta + geometry.armPhase0
                                  + Random.Range(0, geometry.armCount) * (Mathf.PI * 2f / geometry.armCount);

                    float spread = radius * 0.012f + r * 0.05f;
                    pos = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r
                          + new Vector3(
                              Gauss() * spread,
                              Gauss() * (radius * 0.008f + r * 0.04f),
                              Gauss() * spread);

                    float mix = Random.value;
                    color = Random.value < 0.06f ? hiiPink : Color.Lerp(armCool, armWarm, mix * mix);
                    color.a = Random.Range(0.04f, 0.11f);
                    size = radius * Random.Range(0.0004f, 0.0011f);
                }
                else
                {
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    float r = radius * Mathf.Sqrt(Random.value);
                    pos = new Vector3(
                        Mathf.Cos(angle) * r,
                        Gauss() * (radius * 0.016f),
                        Mathf.Sin(angle) * r);
                    color = Color.Lerp(armCool, bulgeColor, Random.value);
                    color.a = Random.Range(0.015f, 0.05f);
                    size = radius * Random.Range(0.0003f, 0.0008f);
                }

                color.a *= alphaMultiplier;

                // Random orientation (billboarding 150k sprites would be too costly).
                Vector3 normal = Random.onUnitSphere;
                Vector3 right = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
                Vector3 up = Vector3.Cross(right, normal);
                WriteQuad(i, pos, size * sizeMultiplier, color, right, up, vertices, colors, uvs, centers, triangles);
            }

            var mesh = RuntimeResourceScope.Own(
                new Mesh { name = tierName, indexFormat = IndexFormat.UInt32 });
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.SetUVs(1, centers);
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (radius * 2.4f));

            var dust = new GameObject(tierName);
            dust.transform.SetParent(root, false);
            dust.AddComponent<MeshFilter>().sharedMesh = mesh;
            var dustRenderer = dust.AddComponent<MeshRenderer>();
            // Small fade range: kills only the sprites immediately around the
            // camera, which are the ones that cover half the screen each.
            Material material = MaterialLibrary.CreateAdditiveFade(Color.white,
                MaterialLibrary.RadialGlowTexture,
                fadeNear: 6000f * sizeMultiplier, fadeFar: 25000f * sizeMultiplier);
            dustRenderer.sharedMaterial = material;
            dustRenderer.shadowCastingMode = ShadowCastingMode.Off;
            dustRenderer.receiveShadows = false;
            return dustRenderer;
        }

        /// <summary>Writes one camera-independent sprite quad into the shared mesh arrays (also used by the halo generator).</summary>
        public static void WriteQuad(int index, Vector3 center, float size, Color color,
            Vector3 right, Vector3 up, Vector3[] vertices, Color32[] colors, Vector2[] uvs,
            Vector3[] centers, int[] triangles)
        {
            float half = size * 0.5f;

            int v = index * 4;
            centers[v] = center;
            centers[v + 1] = center;
            centers[v + 2] = center;
            centers[v + 3] = center;
            vertices[v] = center + (-right - up) * half;
            vertices[v + 1] = center + (-right + up) * half;
            vertices[v + 2] = center + (right + up) * half;
            vertices[v + 3] = center + (right - up) * half;

            uvs[v] = new Vector2(0f, 0f);
            uvs[v + 1] = new Vector2(0f, 1f);
            uvs[v + 2] = new Vector2(1f, 1f);
            uvs[v + 3] = new Vector2(1f, 0f);

            Color32 c32 = color;
            colors[v] = c32;
            colors[v + 1] = c32;
            colors[v + 2] = c32;
            colors[v + 3] = c32;

            int t = index * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
        }

        /// <summary>Cheap approximate standard normal (sum of three uniforms).</summary>
        public static float Gauss()
        {
            return (Random.value + Random.value + Random.value - 1.5f) * 0.8165f;
        }
    }

    /// <summary>
    /// Blends every galaxy layer's intensity by camera distance (in units of
    /// the galaxy radius): each tier ramps from its near weight to its far
    /// weight between fadeStart and fadeEnd. Clouds thicken with distance
    /// (denser spiral from afar); fine dust strengthens up close (detail).
    /// </summary>
    public class GalaxyDustFader : MonoBehaviour
    {
        private struct Tier
        {
            public Material material;
            public Renderer renderer;
            public float fadeStart;
            public float fadeEnd;
            public float nearWeight;
            public float farWeight;
            public float maxVisibleDistance; // in galaxy radii; 0 = always visible
        }

        private readonly List<Tier> tiers = new List<Tier>();
        private float radius = 1f;
        private Camera cachedCamera;

        public void Initialize(float galaxyRadius)
        {
            radius = Mathf.Max(1f, galaxyRadius);
        }

        /// <summary>
        /// Intensity ramps nearWeight -> farWeight across [fadeStart, fadeEnd].
        /// Beyond maxVisibleDistance (in radii) the renderer is switched off
        /// entirely — used for the fine dust, which is sub-pixel from afar.
        /// </summary>
        public void AddTier(Material material, Renderer renderer, float fadeStart, float fadeEnd,
            float nearWeight, float farWeight, float maxVisibleDistance = 0f)
        {
            tiers.Add(new Tier
            {
                material = material,
                renderer = renderer,
                fadeStart = fadeStart,
                fadeEnd = fadeEnd,
                nearWeight = nearWeight,
                farWeight = farWeight,
                maxVisibleDistance = maxVisibleDistance
            });
        }

        private void LateUpdate()
        {
            if (cachedCamera == null)
            {
                cachedCamera = Camera.main;
                if (cachedCamera == null) return;
            }

            float distance = Vector3.Distance(cachedCamera.transform.position, transform.position) / radius;

            for (int i = 0; i < tiers.Count; i++)
            {
                Tier tier = tiers[i];
                float t = tier.fadeEnd <= tier.fadeStart
                    ? 0f
                    : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(tier.fadeStart, tier.fadeEnd, distance));
                float weight = Mathf.Lerp(tier.nearWeight, tier.farWeight, t);

                // Fully faded or too far to resolve = don't render at all;
                // an invisible tier would still cost its full vertex load.
                bool visible = weight > 0.004f
                               && (tier.maxVisibleDistance <= 0f || distance < tier.maxVisibleDistance);
                if (tier.renderer != null && tier.renderer.enabled != visible)
                {
                    tier.renderer.enabled = visible;
                }
                if (!visible) continue;

                var color = new Color(1f, 1f, 1f, weight);
                if (tier.material.HasProperty("_TintColor")) tier.material.SetColor("_TintColor", color);
                if (tier.material.HasProperty("_Color")) tier.material.SetColor("_Color", color);
            }
        }
    }
}
