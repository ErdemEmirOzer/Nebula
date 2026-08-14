using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the deterministic Logistics Center anchor (VS-011 §2/§10.1).</summary>
    public class LogisticsCenterAnchorTests
    {
        private static PlanetManifest Planet(string systemId, int index, float orbitRadius) => new PlanetManifest(
            index, WorldIdCodec.PlanetId(systemId, index), ResourceFamily.Metal, Rarity.Common,
            orbitRadius, 0f, 0f, 0f, 1000f, false, EnvironmentClass.Calm, 1UL);

        private static SystemManifest Manifest(string systemId, StarClass starClass, float starRadius, params float[] orbitRadii)
        {
            var planets = new List<PlanetManifest>();
            for (int i = 0; i < orbitRadii.Length; i++) planets.Add(Planet(systemId, i, orbitRadii[i]));
            return new SystemManifest(systemId, new SectorCoordinate(0, 0, 0), 0, systemId, starClass,
                Vector3.zero, 1_000_000f, starRadius, false, SystemProfileKind.Balanced, SectorManifestGenerator.GenerationVersion, planets);
        }

        [Test]
        public void ComputeAnchor_IsDeterministic_SameManifestSameOffset()
        {
            SystemManifest manifest = Manifest("sector.0.0.0/system.777", StarClass.Yellow, 1000f, 20000f, 30000f);

            bool ok1 = LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset1, out _);
            bool ok2 = LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset2, out _);

            Assert.IsTrue(ok1); Assert.IsTrue(ok2);
            Assert.AreEqual(offset1, offset2);
        }

        [Test]
        public void ComputeAnchor_StrictlyBetweenDangerBoundaryAndInnermostOrbit_WithMargins()
        {
            SystemManifest manifest = Manifest("sector.0.0.0/system.778", StarClass.Yellow, 1000f, 20000f, 30000f);
            float dangerRadius = manifest.StarRadius * StarClassCatalog.WarningRadiusMultiplier(manifest.StarClass);

            Assert.IsTrue(LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out AnchorRejectReason reason));
            Assert.AreEqual(AnchorRejectReason.None, reason);

            float radius = offset.magnitude;
            Assert.Greater(radius, dangerRadius, "anchor must be strictly outside the star's danger boundary");
            Assert.Less(radius, 20000f, "anchor must be strictly inside the innermost planet's orbit");
            Assert.AreEqual(0f, offset.y, 0.001f, "system-local anchor uses the star's base plane (y=0)");
        }

        [Test]
        public void ComputeAnchor_UsesInnermostOrbit_NotFirstListedPlanet()
        {
            // Deliberately out of order: index 0 has the LARGER orbit, index 1 the smaller (innermost) one.
            SystemManifest manifest = Manifest("sector.0.0.0/system.779", StarClass.RedDwarf, 800f, 50000f, 15000f);
            Assert.IsTrue(LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out _));
            Assert.Less(offset.magnitude, 15000f, "must respect the true innermost orbit regardless of list order");
        }

        [Test]
        public void ComputeAnchor_NoSafeGap_ReturnsExplicitRejection_NeverClamps()
        {
            // Innermost orbit sits essentially at the star's danger boundary -- no safe gap exists.
            var manifest = Manifest("sector.0.0.0/system.780", StarClass.BlueGiant, 5000f, 9000f);
            float dangerRadius = manifest.StarRadius * StarClassCatalog.WarningRadiusMultiplier(manifest.StarClass);
            Assert.Less(9000f * 0.85f, dangerRadius * 1.10f, "test setup sanity: no safe gap should exist");

            bool ok = LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out AnchorRejectReason reason);

            Assert.IsFalse(ok);
            Assert.AreEqual(AnchorRejectReason.NoSafeGap, reason);
            Assert.AreEqual(Vector3.zero, offset);
        }

        [Test]
        public void ComputeAnchor_NoPlanets_ReturnsExplicitRejection()
        {
            SystemManifest manifest = Manifest("sector.0.0.0/system.781", StarClass.Yellow, 1000f);
            bool ok = LogisticsCenterAnchor.TryComputeAnchor(manifest, out _, out AnchorRejectReason reason);
            Assert.IsFalse(ok);
            Assert.AreEqual(AnchorRejectReason.NoSafeGap, reason);
        }

        [Test]
        public void TryComputeAbsolutePosition_AddsSystemAbsoluteCenter()
        {
            var planets = new List<PlanetManifest> { Planet("sector.1.2.3/system.005", 0, 20000f) };
            var manifest = new SystemManifest("sector.1.2.3/system.005", new SectorCoordinate(1, 2, 3), 5, "Test",
                StarClass.Yellow, new Vector3(4_000_000f, 0f, 6_000_000f), 1_000_000f, 1000f, false,
                SystemProfileKind.Balanced, SectorManifestGenerator.GenerationVersion, planets);

            Assert.IsTrue(LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out _));
            Assert.IsTrue(LogisticsCenterAnchor.TryComputeAbsolutePosition(manifest, out Vector3 abs, out _));
            Assert.AreEqual(manifest.AbsoluteCenter + offset, abs);
        }
    }
}
