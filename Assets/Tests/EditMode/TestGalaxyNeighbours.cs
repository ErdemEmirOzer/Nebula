using System.Collections.Generic;
using UnityEngine;
using Nebula.Sectors;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Real remote systems to test against, asked of the galaxy instead of named
    /// by hand.
    /// <para>
    /// A long line of fixtures picked their remote system by writing
    /// <c>WorldIdCodec.SystemId(sector, 1)</c> — "the second system in the start
    /// sector" — which assumes a sector holds at least two. The star field is
    /// clustered and makes no such promise: measured, sixty-four sectors around
    /// the origin hold five systems between them, and the start sector holds
    /// exactly one. <c>ResolveSystem</c> therefore returned null and sixteen
    /// EditMode tests died on a NullReferenceException at their first line of
    /// setup, before reaching a single thing they were written to check.
    /// </para>
    /// <para>
    /// Nothing here loosens anything. The tests still run against a genuine
    /// generated system with genuine planets; they just stop guessing its name.
    /// </para>
    /// </summary>
    public static class TestGalaxyNeighbours
    {
        /// <summary>
        /// <paramref name="count"/> distinct systems that are not
        /// <paramref name="excludeSystemId"/>, taken from the sector itself and
        /// then the ring of sectors around it. Fewer than asked for means the
        /// galaxy genuinely has none, which is worth failing on loudly rather
        /// than dereferencing null.
        /// </summary>
        public static List<SystemManifest> Remote(SectorCoordinate sector, string excludeSystemId, int count = 1)
        {
            var found = new List<SystemManifest>();

            Collect(StarSystemRegistry.Manifests.Resolve(sector), excludeSystemId, count, found);
            if (found.Count >= count) return found;

            // Outward a shell at a time, so a caller that wants one system gets the
            // nearest one and the answer does not jump around between seeds. Two
            // shells is not arbitrary generosity: at the measured density -- five
            // systems across sixty-four sectors -- one shell can genuinely come up
            // empty, and a fixture that needs two distinct remote systems then fails
            // on the galaxy rather than on what it set out to test.
            for (int radius = 1; radius <= 2 && found.Count < count; radius++)
            for (int dx = -radius; dx <= radius && found.Count < count; dx++)
            for (int dy = -radius; dy <= radius && found.Count < count; dy++)
            for (int dz = -1; dz <= 1 && found.Count < count; dz++)
            {
                // Only the shell itself; the inside was covered by a smaller radius.
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius && dz == 0) continue;
                if (dx == 0 && dy == 0 && dz == 0) continue;
                Collect(StarSystemRegistry.Manifests.Resolve(
                    new SectorCoordinate(sector.X + dx, sector.Y + dy, sector.Z + dz)),
                    excludeSystemId, count, found);
            }
            return found;
        }

        private static void Collect(SectorManifest source, string excludeSystemId, int count,
            List<SystemManifest> into)
        {
            if (source == null) return;
            foreach (SystemManifest candidate in source.Systems)
            {
                if (into.Count >= count) return;
                if (candidate.SystemId == excludeSystemId) continue;
                bool already = false;
                for (int i = 0; i < into.Count; i++)
                    if (into[i].SystemId == candidate.SystemId) { already = true; break; }
                if (!already) into.Add(candidate);
            }
        }

        /// <summary>One real remote system, or null when the galaxy has none.</summary>
        public static SystemManifest OneRemote(SectorCoordinate sector, string excludeSystemId)
        {
            List<SystemManifest> found = Remote(sector, excludeSystemId, 1);
            return found.Count > 0 ? found[0] : null;
        }
    }
}
