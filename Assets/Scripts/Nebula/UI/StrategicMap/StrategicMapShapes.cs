using System.Collections.Generic;
using UnityEngine;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// Procedural UI chrome for the map: a soft disc and a thin ring.
    ///
    /// These are deliberately NOT game art — they are the shapes a uGUI
    /// <see cref="UnityEngine.UI.Image"/> needs in order to stop being a rectangle.
    /// Without them an orbit renders as a filled square and a star renders as a
    /// stretched 2:1 rectangle, because an Image with no sprite draws a solid quad.
    /// The real shipped textures (star surfaces, coronae, icons, prefab previews)
    /// still carry every bit of identity; these only mask and frame them.
    ///
    /// Built once, cached forever, and never touched per frame.
    /// </summary>
    public static class StrategicMapShapes
    {
        private const int Resolution = 128;

        private static Sprite disc;
        private static Sprite softDisc;
        private static readonly Dictionary<int, Sprite> ringsByThickness = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> panelsByKey = new Dictionary<int, Sprite>();

        /// <summary>A hard-edged filled circle — the base shape for a star/planet body and for masking.</summary>
        public static Sprite Disc => disc != null ? disc : disc = BuildDisc(0.02f);

        /// <summary>A filled circle with a soft falloff — for glows and haloes.</summary>
        public static Sprite SoftDisc => softDisc != null ? softDisc : softDisc = BuildDisc(0.45f);

        /// <summary>A thin annulus for orbit rings and selection rings. Thickness is in texture pixels.</summary>
        public static Sprite Ring(int thicknessPixels = 3)
        {
            int key = Mathf.Clamp(thicknessPixels, 1, Resolution / 4);
            if (ringsByThickness.TryGetValue(key, out Sprite cached) && cached != null) return cached;
            Sprite built = BuildRing(key);
            ringsByThickness[key] = built;
            return built;
        }

        /// <summary>
        /// A rounded-rectangle panel, 9-sliced so a card of any size keeps the same corner
        /// radius. <paramref name="strokePixels"/> of 0 gives a filled panel; anything else
        /// gives just the outline, which is what a glass panel's hairline edge is.
        ///
        /// Without this a panel is a hard-cornered quad, which is the single thing that most
        /// makes a UI look unfinished next to a graph view built out of circles and curves.
        /// </summary>
        public static Sprite Panel(int cornerRadius = 18, int strokePixels = 0)
        {
            int radius = Mathf.Clamp(cornerRadius, 2, Resolution / 2 - 2);
            int stroke = Mathf.Clamp(strokePixels, 0, radius);
            int key = radius * 1000 + stroke;
            if (panelsByKey.TryGetValue(key, out Sprite cached) && cached != null) return cached;
            Sprite built = BuildPanel(radius, stroke);
            panelsByKey[key] = built;
            return built;
        }

        private static Sprite BuildPanel(int cornerRadius, int strokePixels)
        {
            var texture = NewTexture();
            var pixels = new Color32[Resolution * Resolution];

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    // Signed distance to a rounded rectangle inset by one pixel, so the very
                    // edge of the texture stays clear and the 9-slice never bleeds.
                    float dx = Mathf.Abs(x + 0.5f - Resolution * 0.5f) - (Resolution * 0.5f - 1f - cornerRadius);
                    float dy = Mathf.Abs(y + 0.5f - Resolution * 0.5f) - (Resolution * 0.5f - 1f - cornerRadius);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float distance = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - cornerRadius;

                    float a = strokePixels <= 0
                        ? Mathf.Clamp01(-distance)                                  // filled
                        : Mathf.Clamp01(Mathf.Min(-distance, distance + strokePixels)); // outline only

                    pixels[y * Resolution + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            // The border keeps the corners intact at any panel size; the middle simply stretches.
            float slice = cornerRadius + 2f;
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, Resolution, Resolution), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(slice, slice, slice, slice));
            sprite.name = "StrategicMapPanel";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite BuildDisc(float softness)
        {
            var texture = NewTexture();
            var pixels = new Color32[Resolution * Resolution];
            const float radius = Resolution * 0.5f - 1f;
            float inner = radius * (1f - Mathf.Clamp01(softness));

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    float dx = x - Resolution * 0.5f + 0.5f;
                    float dy = y - Resolution * 0.5f + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d <= inner ? 1f : Mathf.Clamp01(1f - (d - inner) / Mathf.Max(0.0001f, radius - inner));
                    pixels[y * Resolution + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            return Finish(texture, pixels);
        }

        private static Sprite BuildRing(int thicknessPixels)
        {
            var texture = NewTexture();
            var pixels = new Color32[Resolution * Resolution];
            const float outer = Resolution * 0.5f - 1f;
            float innerEdge = outer - thicknessPixels;

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    float dx = x - Resolution * 0.5f + 0.5f;
                    float dy = y - Resolution * 0.5f + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // One pixel of feathering on each edge keeps the ring smooth at any scale.
                    float a = Mathf.Clamp01(Mathf.Min(outer - d, d - innerEdge));
                    pixels[y * Resolution + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            return Finish(texture, pixels);
        }

        private static Texture2D NewTexture() =>
            new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
            {
                name = "StrategicMapShape",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

        private static Sprite Finish(Texture2D texture, Color32[] pixels)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, Resolution, Resolution), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "StrategicMapShape";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
