using System.Collections.Generic;

namespace Nebula.UI.Accessibility
{
    /// <summary>
    /// One stroke of a cue mark, in a unit box: <c>(0,0)</c> is the bottom-left
    /// corner and <c>(1,1)</c> the top-right, so one table draws at any size.
    /// </summary>
    public readonly struct UiCueStroke
    {
        /// <summary>Centre of the stroke, 0..1 across the box.</summary>
        public readonly float X, Y;

        /// <summary>Length and thickness as fractions of the box.</summary>
        public readonly float Length, Thickness;

        /// <summary>Rotation in degrees, counter-clockwise, zero being horizontal.</summary>
        public readonly float Degrees;

        public UiCueStroke(float x, float y, float length, float thickness, float degrees)
        {
            X = x; Y = y; Length = length; Thickness = thickness; Degrees = degrees;
        }
    }

    /// <summary>
    /// The geometry behind every cue mark, as data.
    /// <para>
    /// Deliberately not sprites. A sprite set is another folder of PNGs to keep
    /// in step with an atlas, it cannot be asserted on without a graphics device,
    /// and a missing one draws the magenta square this project already checks
    /// for. A stroke table is a handful of numbers: it renders as real geometry,
    /// it scales without an import setting, and a test with no screen can read
    /// it and prove two marks are actually different shapes rather than the same
    /// shape in two colours.
    /// </para>
    /// </summary>
    public static class UiCueShapes
    {
        private static readonly UiCueStroke[] Empty = new UiCueStroke[0];

        // A tick. Short fall, long rise -- the asymmetry is what stops it reading
        // as a chevron at 12 px.
        private static readonly UiCueStroke[] CheckStrokes =
        {
            new UiCueStroke(0.32f, 0.38f, 0.36f, 0.13f, -45f),
            new UiCueStroke(0.60f, 0.55f, 0.64f, 0.13f, 45f),
        };

        // A rotated square, open in the middle so it never reads as a filled dot.
        private static readonly UiCueStroke[] DiamondStrokes =
        {
            new UiCueStroke(0.28f, 0.72f, 0.56f, 0.10f, -45f),
            new UiCueStroke(0.72f, 0.72f, 0.56f, 0.10f, 45f),
            new UiCueStroke(0.28f, 0.28f, 0.56f, 0.10f, 45f),
            new UiCueStroke(0.72f, 0.28f, 0.56f, 0.10f, -45f),
        };

        // The one upward-pointing outline in the set. Point-up is the shape a
        // player already reads as "warning" before anything is explained.
        private static readonly UiCueStroke[] TriangleStrokes =
        {
            new UiCueStroke(0.29f, 0.47f, 0.80f, 0.11f, 60f),
            new UiCueStroke(0.71f, 0.47f, 0.80f, 0.11f, -60f),
            new UiCueStroke(0.50f, 0.11f, 0.86f, 0.11f, 0f),
        };

        // Two strokes through the centre. The heaviest mark in the set, because
        // Critical is the one state that may interrupt.
        private static readonly UiCueStroke[] CrossStrokes =
        {
            new UiCueStroke(0.50f, 0.50f, 0.88f, 0.16f, 45f),
            new UiCueStroke(0.50f, 0.50f, 0.88f, 0.16f, -45f),
        };

        // A double chevron: motion, without anything actually moving.
        private static readonly UiCueStroke[] ChevronStrokes =
        {
            new UiCueStroke(0.30f, 0.70f, 0.48f, 0.12f, -45f),
            new UiCueStroke(0.30f, 0.30f, 0.48f, 0.12f, 45f),
            new UiCueStroke(0.66f, 0.70f, 0.48f, 0.12f, -45f),
            new UiCueStroke(0.66f, 0.30f, 0.48f, 0.12f, 45f),
        };

        // One flat stroke. Nothing is happening, and the mark says so by being
        // the least mark in the set.
        private static readonly UiCueStroke[] DashStrokes =
        {
            new UiCueStroke(0.50f, 0.50f, 0.70f, 0.12f, 0f),
        };

        // A line with a piece missing and a slash where it broke.
        private static readonly UiCueStroke[] BrokenLinkStrokes =
        {
            new UiCueStroke(0.18f, 0.50f, 0.32f, 0.13f, 0f),
            new UiCueStroke(0.82f, 0.50f, 0.32f, 0.13f, 0f),
            new UiCueStroke(0.50f, 0.50f, 0.60f, 0.11f, 62f),
        };

        // A closed box with a bar through it: the shape of a refusal.
        private static readonly UiCueStroke[] BarredStrokes =
        {
            new UiCueStroke(0.50f, 0.87f, 0.82f, 0.10f, 0f),
            new UiCueStroke(0.50f, 0.13f, 0.82f, 0.10f, 0f),
            new UiCueStroke(0.13f, 0.50f, 0.82f, 0.10f, 90f),
            new UiCueStroke(0.87f, 0.50f, 0.82f, 0.10f, 90f),
            new UiCueStroke(0.50f, 0.50f, 0.98f, 0.12f, -45f),
        };

        /// <summary>The strokes that make up a mark. Never null; an empty list means no mark.</summary>
        public static IReadOnlyList<UiCueStroke> Strokes(UiCueShape shape)
        {
            switch (shape)
            {
                case UiCueShape.Check: return CheckStrokes;
                case UiCueShape.Diamond: return DiamondStrokes;
                case UiCueShape.Triangle: return TriangleStrokes;
                case UiCueShape.Cross: return CrossStrokes;
                case UiCueShape.Chevron: return ChevronStrokes;
                case UiCueShape.Dash: return DashStrokes;
                case UiCueShape.BrokenLink: return BrokenLinkStrokes;
                case UiCueShape.Barred: return BarredStrokes;
                default: return Empty;
            }
        }

        /// <summary>
        /// A stable, order-independent summary of a mark's geometry, for a test
        /// that has to say two shapes really are different without a screen.
        /// Quantised to hundredths so it survives float arithmetic.
        /// </summary>
        public static string Signature(UiCueShape shape)
        {
            IReadOnlyList<UiCueStroke> strokes = Strokes(shape);
            var parts = new List<string>(strokes.Count);
            foreach (UiCueStroke s in strokes)
                parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.00},{1:0.00},{2:0.00},{3:0.00},{4:0}",
                    s.X, s.Y, s.Length, s.Thickness, s.Degrees));
            parts.Sort(System.StringComparer.Ordinal);
            return string.Join("|", parts.ToArray());
        }
    }
}
