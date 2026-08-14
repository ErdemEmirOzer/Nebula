using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the deduplicated 80% fill warning and the 100%-fill no-overflow/no-loss guarantee (VS-011 §3/§10.11).</summary>
    public class LogisticsCenterFillWarningTests
    {
        private static SystemManifest Manifest(string systemId)
        {
            var planets = new List<PlanetManifest>
            {
                new PlanetManifest(0, WorldIdCodec.PlanetId(systemId, 0), ResourceFamily.Metal, Rarity.Common,
                    30000f, 0f, 0f, 0f, 1000f, false, EnvironmentClass.Calm, 1UL)
            };
            return new SystemManifest(systemId, new SectorCoordinate(0, 0, 0), 0, systemId, StarClass.Yellow,
                Vector3.zero, 1_000_000f, 1000f, false, SystemProfileKind.Balanced, SectorManifestGenerator.GenerationVersion, planets);
        }

        [Test]
        public void FillWarning_TogglesAt80Percent_And100PercentNeverOverflowsOrLoses()
        {
            SystemManifest manifest = Manifest("sector.0.0.0/system.900");
            var starGO = new GameObject("Star");
            LogisticsCenter center = null;
            try
            {
                LogisticsCenterAnchor.TryComputeAnchor(manifest, out Vector3 offset, out _);
                center = LogisticsCenter.Create(manifest, starGO.transform, offset, 0, 0, 0);

                Assert.IsFalse(center.FillWarningActive);

                // T1 capacity = 600, raw resources are 1 volume/unit.
                center.Inventory.Add("resource.metal", 470f); // 78.3%
                center.Tick(0.016f);
                Assert.IsFalse(center.FillWarningActive, "under 80% must not warn");

                center.Inventory.Add("resource.metal", 20f); // -> 490/600 = 81.67%
                center.Tick(0.016f);
                Assert.IsTrue(center.FillWarningActive, "crossing 80% must set the warning");

                float added = center.Inventory.Add("resource.metal", 1000f); // try to massively overfill
                Assert.AreEqual(600f - 490f, added, "Add() is always capacity-bounded");
                Assert.AreEqual(600f, center.Inventory.Total, "100% full -- exactly at capacity, never beyond");
                center.Tick(0.016f);
                Assert.IsTrue(center.FillWarningActive);

                center.Inventory.TryConsumeImmediate("resource.metal", 250f); // -> 350/600 = 58.3%
                center.Tick(0.016f);
                Assert.IsFalse(center.FillWarningActive, "dropping back under 80% resets the warning so a future re-cross fires again");
                Assert.AreEqual(350f, center.Inventory.GetStored("resource.metal"), "nothing was ever silently deleted");
            }
            finally
            {
                if (center != null) Object.DestroyImmediate(center.gameObject);
                Object.DestroyImmediate(starGO);
                LogisticsState.Reset();
            }
        }
    }
}
