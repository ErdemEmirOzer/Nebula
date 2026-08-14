using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Nebula.Sectors
{
    /// <summary>
    /// One cell of the 2,000,000-unit cubic sector grid the galaxy is
    /// partitioned into (VS-008 §1). Pure value type; culture-invariant
    /// parse/format so negative coordinates round-trip safely regardless of
    /// the machine's locale (no thousands separators, no locale-dependent
    /// minus sign).
    /// </summary>
    public readonly struct SectorCoordinate : IEquatable<SectorCoordinate>
    {
        public const float SectorSize = 2_000_000f;

        public readonly int X, Y, Z;

        public SectorCoordinate(int x, int y, int z) { X = x; Y = y; Z = z; }

        /// <summary>The sector containing an absolute (universe-space) position.</summary>
        public static SectorCoordinate FromAbsolutePosition(Vector3 absolute) => new SectorCoordinate(
            FloorDiv(absolute.x), FloorDiv(absolute.y), FloorDiv(absolute.z));

        private static int FloorDiv(float value) => Mathf.FloorToInt(value / SectorSize);

        public Vector3 Origin => new Vector3(X * SectorSize, Y * SectorSize, Z * SectorSize);
        public Vector3 Center => Origin + new Vector3(SectorSize, SectorSize, SectorSize) * 0.5f;

        public string Id => string.Format(CultureInfo.InvariantCulture, "sector.{0}.{1}.{2}", X, Y, Z);

        public override string ToString() => Id;

        public static bool TryParse(string id, out SectorCoordinate coordinate)
        {
            coordinate = default;
            if (string.IsNullOrEmpty(id) || !id.StartsWith("sector.", StringComparison.Ordinal)) return false;

            string[] parts = id.Substring("sector.".Length).Split('.');
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) return false;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z)) return false;

            coordinate = new SectorCoordinate(x, y, z);
            return true;
        }

        /// <summary>The 26 face/edge/corner-adjacent sectors (never includes this one).</summary>
        public IEnumerable<SectorCoordinate> Neighbors26()
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        yield return new SectorCoordinate(X + dx, Y + dy, Z + dz);
                    }
        }

        public bool Equals(SectorCoordinate other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is SectorCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public static bool operator ==(SectorCoordinate a, SectorCoordinate b) => a.Equals(b);
        public static bool operator !=(SectorCoordinate a, SectorCoordinate b) => !a.Equals(b);
    }
}
