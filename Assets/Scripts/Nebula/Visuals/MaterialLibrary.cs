using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// Runtime material and texture factory. Everything visual in the world is
    /// generated here so the project needs no imported art assets. Shader lookup
    /// tries URP names first and falls back to built-in pipeline shaders, so a
    /// future URP upgrade will not break the visuals.
    /// </summary>
    public static class MaterialLibrary
    {
        private static Texture2D radialGlow;
        private static Texture2D streak;

        /// <summary>Shared soft radial gradient used by halos, engine glow and the starfield.</summary>
        public static Texture2D RadialGlowTexture
        {
            get
            {
                if (radialGlow == null) radialGlow = CreateRadialGlow(128, 2.2f);
                return radialGlow;
            }
        }

        /// <summary>Thin horizontal light streak used by the lens flare.</summary>
        public static Texture2D StreakTexture
        {
            get
            {
                if (streak == null) streak = CreateStreak(256);
                return streak;
            }
        }

        public static Shader FindShader(params string[] candidates)
        {
            foreach (string name in candidates)
            {
                Shader shader = Shader.Find(name);
                if (shader != null) return shader;
            }
            Debug.LogWarning("[Nebula] None of the requested shaders were found: " + string.Join(", ", candidates));
            return Shader.Find("Hidden/InternalErrorShader");
        }

        public static Material CreateLit(Color albedo, float smoothness = 0.15f, float metallic = 0f, Texture2D texture = null)
        {
            var material = new Material(FindShader("Universal Render Pipeline/Lit", "Standard"));
            SetColor(material, albedo);
            if (texture != null) material.mainTexture = texture;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            // Hundreds of planets share these pooled materials and the same
            // sphere mesh — instancing collapses them into a handful of draws.
            material.enableInstancing = true;
            return RuntimeResourceScope.Own(material);
        }

        /// <summary>
        /// Additive sprite material that fades out (and stops rasterizing)
        /// near the camera. Requires sprite centers in the mesh's UV1 channel;
        /// falls back to plain additive if the shader is missing.
        /// </summary>
        public static Material CreateAdditiveFade(Color tint, Texture2D texture, float fadeNear, float fadeFar)
        {
            Shader shader = Shader.Find("Nebula/AdditiveFade");
            if (shader == null) return CreateAdditive(tint, texture);

            var material = new Material(shader);
            material.SetColor("_TintColor", tint);
            if (texture != null) material.mainTexture = texture;
            material.SetFloat("_FadeNear", fadeNear);
            material.SetFloat("_FadeFar", fadeFar);
            return RuntimeResourceScope.Own(material);
        }

        public static Material CreateUnlit(Color color, Texture2D texture = null)
        {
            Material material;
            if (texture != null)
            {
                material = new Material(FindShader("Universal Render Pipeline/Unlit", "Unlit/Texture"));
                material.mainTexture = texture;
            }
            else
            {
                material = new Material(FindShader("Universal Render Pipeline/Unlit", "Unlit/Color"));
            }
            SetColor(material, color);
            return RuntimeResourceScope.Own(material);
        }

        public static Material CreateAdditive(Color tint, Texture2D texture = null)
        {
            var material = new Material(FindShader(
                "Legacy Shaders/Particles/Additive",
                "Particles/Standard Unlit",
                "Universal Render Pipeline/Particles/Unlit"));
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", tint);
            SetColor(material, tint);
            material.mainTexture = texture != null ? texture : Texture2D.whiteTexture;
            return RuntimeResourceScope.Own(material);
        }

        /// <summary>Fractal-noise texture blended between two colors; used for star and planet surfaces.</summary>
        public static Texture2D CreatePerlinTexture(int size, Color low, Color high, float noiseScale)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.wrapMode = TextureWrapMode.Repeat;

            float offsetX = Random.Range(0f, 1000f);
            float offsetY = Random.Range(0f, 1000f);
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x / (float)size * noiseScale + offsetX;
                    float fy = y / (float)size * noiseScale + offsetY;

                    float value = 0f;
                    float amplitude = 0.5f;
                    float frequency = 1f;
                    for (int octave = 0; octave < 4; octave++)
                    {
                        value += Mathf.PerlinNoise(fx * frequency, fy * frequency) * amplitude;
                        amplitude *= 0.5f;
                        frequency *= 2f;
                    }

                    pixels[y * size + x] = Color.Lerp(low, high, Mathf.Clamp01(value / 0.9375f));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            return RuntimeResourceScope.Own(texture);
        }

        /// <summary>
        /// Soft cloud texture for background nebulae: fractal noise shaped by a
        /// threshold and a radial falloff so the quad edges stay invisible.
        /// Color blends between the two given hues; alpha carries the cloud shape.
        /// </summary>
        public static Texture2D CreateNebulaTexture(int size, Color colorA, Color colorB)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.wrapMode = TextureWrapMode.Clamp;

            float offsetX = Random.Range(0f, 1000f);
            float offsetY = Random.Range(0f, 1000f);
            float noiseScale = Random.Range(2.5f, 5f);
            var pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x / (float)size * noiseScale + offsetX;
                    float fy = y / (float)size * noiseScale + offsetY;

                    float noise = 0f;
                    float amplitude = 0.5f;
                    float frequency = 1f;
                    for (int octave = 0; octave < 5; octave++)
                    {
                        noise += Mathf.PerlinNoise(fx * frequency, fy * frequency) * amplitude;
                        amplitude *= 0.5f;
                        frequency *= 2f;
                    }
                    noise = Mathf.Clamp01(noise / 0.96875f);

                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    falloff = falloff * falloff * (3f - 2f * falloff);

                    float cloud = Mathf.Pow(Mathf.Clamp01((noise - 0.42f) / 0.58f), 2f) * falloff;

                    Color color = Color.Lerp(colorA, colorB, noise);
                    color.a = cloud;
                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            return RuntimeResourceScope.Own(texture);
        }

        /// <summary>
        /// Glowing accretion disk ring: dark hole in the middle, white-hot inner
        /// edge fading to orange, with subtle brightness streaks around the ring.
        /// </summary>
        public static Texture2D CreateAccretionDiskTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;
            Color hot = new Color(1f, 0.96f, 0.9f);
            Color cool = new Color(1f, 0.55f, 0.2f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float t = Mathf.Sqrt(dx * dx + dy * dy);

                    float ring = Mathf.Exp(-Mathf.Pow((t - 0.55f) / 0.16f, 2f));
                    float hole = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.42f, t));
                    // cos/sin sampling keeps the streak noise seamless around the ring
                    float angleStreak = 0.7f + 0.3f * Mathf.PerlinNoise(dx * 2f + 7f, dy * 2f + 3f);
                    float alpha = t > 1f ? 0f : ring * hole * angleStreak;

                    Color color = Color.Lerp(hot, cool, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.85f, t)));
                    color.a = alpha;
                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            return RuntimeResourceScope.Own(texture);
        }

        /// <summary>Thin bright circle (photon ring) for the black hole's lensing look.</summary>
        public static Texture2D CreateRingTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float t = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = t > 1f ? 0f : Mathf.Exp(-Mathf.Pow((t - 0.52f) / 0.045f, 2f));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            return RuntimeResourceScope.Own(texture);
        }

        private static Texture2D CreateStreak(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs((x - half) / half);
                    float dy = Mathf.Abs((y - half) / half);
                    float value = Mathf.Pow(Mathf.Clamp01(1f - dx), 2.5f) * Mathf.Pow(Mathf.Clamp01(1f - dy), 10f);
                    pixels[y * size + x] = new Color(value, value, value, value);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            return texture;
        }

        private static Texture2D CreateRadialGlow(int size, float exponent)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float falloff = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), exponent);
                    pixels[y * size + x] = new Color(falloff, falloff, falloff, falloff);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            return texture;
        }

        private static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }
    }
}
