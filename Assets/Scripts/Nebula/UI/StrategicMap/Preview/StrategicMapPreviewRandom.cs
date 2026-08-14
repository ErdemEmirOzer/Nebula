namespace Nebula.UI.StrategicMap.Preview
{
    /// <summary>
    /// A bit-exact port of the reference map's <c>UnityMapRandom</c> — the Xorshift128
    /// stream Unity's own generator uses, resumed from the snapshot's captured state so
    /// the galaxy point cloud lands in exactly the same places as the reference.
    ///
    /// Deliberately its own type rather than <c>UnityEngine.Random</c>: the preview
    /// renderer must never touch, advance or reseed the game's world-generation RNG.
    /// It is only ever used to rebuild presentation geometry.
    /// </summary>
    public sealed class StrategicMapPreviewRandom
    {
        private uint s0, s1, s2, s3;

        public StrategicMapPreviewRandom(uint a, uint b, uint c, uint d)
        {
            s0 = a; s1 = b; s2 = c; s3 = d;
        }

        public uint Next()
        {
            uint x = s0;
            uint y = s3;
            x ^= x << 11;
            x ^= x >> 8;
            y ^= y >> 19;
            uint result = y ^ x;
            s0 = s1; s1 = s2; s2 = s3; s3 = result;
            return result;
        }

        /// <summary>The reference takes the low 23 bits and normalises — matching float32 precision exactly.</summary>
        private static float Raw(uint value) => (value & 8388607u) / 8388607f;

        public float Value() => Raw(Next());

        /// <summary>Note the reference's argument order: <c>raw * (min - max) + max</c>.</summary>
        public float RangeFloat(float min, float max) => Raw(Next()) * (min - max) + max;

        /// <summary>Integer range, reproducing the reference's <c>next() % (min - max) + min</c> exactly.</summary>
        public int RangeInt(int min, int max)
        {
            int span = min - max;
            if (span == 0) return min;
            return (int)(Next() % (uint)System.Math.Abs(span)) + min;
        }

        /// <summary>Sum-of-three approximation of a normal distribution, exactly as the reference's <c>gaussUnity</c>.</summary>
        public float Gauss() => (Value() + Value() + Value() - 1.5f) * 0.8165f;

        /// <summary>
        /// The reference draws a unit-sphere sample after every galaxy point purely to keep
        /// the stream aligned with Unity's own generation order. It consumes two draws and
        /// the result is discarded — but skipping it would desynchronise every later point.
        /// </summary>
        public void ConsumeUnitSphere()
        {
            Next();
            Next();
        }
    }
}
