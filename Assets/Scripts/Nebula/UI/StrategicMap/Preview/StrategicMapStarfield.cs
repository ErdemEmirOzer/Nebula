using System.Collections.Generic;
using UnityEngine;

namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// A bit-exact port of the reference map's <c>mulberry32</c>.
    ///
    /// The reference uses this fixed-seed stream for everything decorative that is not part
    /// of the captured Unity RNG sequence — the starfield, the asteroid samples, the nebula
    /// textures. Porting it keeps those layers identical rather than merely similar.
    /// </summary>
    public sealed class Mulberry32
    {
        private uint value;

        public Mulberry32(uint seed) => value = seed;

        public float Next()
        {
            unchecked
            {
                value += 0x6D2B79F5u;
                uint result = value;
                result = Imul(result ^ (result >> 15), result | 1u);
                result ^= result + Imul(result ^ (result >> 7), result | 61u);
                return ((result ^ (result >> 14)) & 0xFFFFFFFFu) / 4294967296f;
            }
        }

        /// <summary>JavaScript's Math.imul — 32-bit wrapping multiply, which plain C# * already is for uint.</summary>
        private static uint Imul(uint a, uint b) { unchecked { return a * b; } }
    }

    /// <summary>One background star: a fixed point on a far shell, with its own twinkle.</summary>
    public struct StrategicStarfieldPoint
    {
        public Vector3 Position;
        public float Alpha;
        public float Size;
        /// <summary>0 = cool/blue, 1 = neutral, 2 = warm — the reference's three temperature bands.</summary>
        public int Temperature;
        public float Phase;
        public float Twinkle;
        public bool Flare;
    }

    /// <summary>
    /// The deep-space starfield: a line-for-line port of the reference's
    /// <c>buildStarfield</c> — 1900 stars on a shell of radius 86..158 around the universe
    /// centre, seeded with mulberry32(73111) so they land exactly where the reference put them.
    ///
    /// Note the reference's own snapshot reports a 12000-star budget, but it only ever
    /// *draws* 1900; the visible sampling is what is reproduced here, exactly as the brief
    /// requires ("HTML'nin gerçekten çizdiği görsel örnekleme mantığını taşı").
    /// </summary>
    public static class StrategicMapStarfield
    {
        public const int StarCount = 1900;
        private const uint Seed = 73111u;
        private const float Tau = Mathf.PI * 2f;

        private static StrategicStarfieldPoint[] cached;

        public static StrategicStarfieldPoint[] Build(Vector3 universeCenter)
        {
            if (cached != null) return cached;

            var random = new Mulberry32(Seed);
            var points = new List<StrategicStarfieldPoint>(StarCount);

            for (int i = 0; i < StarCount; i++)
            {
                float radius = 86f + random.Next() * 72f;
                float theta = random.Next() * Tau;
                float phi = Mathf.Acos(random.Next() * 2f - 1f);
                float temperatureRoll = random.Next();

                points.Add(new StrategicStarfieldPoint
                {
                    Position = new Vector3(
                        universeCenter.x + radius * Mathf.Sin(phi) * Mathf.Cos(theta),
                        universeCenter.y + radius * Mathf.Cos(phi),
                        universeCenter.z + radius * Mathf.Sin(phi) * Mathf.Sin(theta)),
                    Alpha = 0.12f + random.Next() * 0.42f,
                    Size = random.Next() < 0.08f ? 1.5f : 0.75f,
                    Temperature = temperatureRoll < 0.12f ? 0 : (temperatureRoll > 0.78f ? 2 : 1),
                    Phase = random.Next() * Tau,
                    Twinkle = 0.45f + random.Next() * 1.35f,
                    Flare = random.Next() < 0.026f
                });
            }

            return cached = points.ToArray();
        }

        public static void ClearCache() => cached = null;

        /// <summary>The reference's three star temperatures, mixed from the shared palette.</summary>
        public static Color TemperatureColor(int temperature)
        {
            switch (temperature)
            {
                case 0: return Color.Lerp(StrategicMapTheme.Blue, StrategicMapTheme.SpaceText, 0.35f);
                case 2: return Color.Lerp(StrategicMapTheme.Gold, StrategicMapTheme.SpaceText, 0.4f);
                default: return StrategicMapTheme.SpaceText;
            }
        }
    }
}
