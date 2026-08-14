using NUnit.Framework;
using Nebula.Poi;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the POI catalog's pure constants (VS-012 §2/§4/§5/§6/§7/§10.1/§10.4).</summary>
    public class PoiCatalogTests
    {
        [Test]
        public void CountDistribution_Sums100Percent_MaxTwo()
        {
            Assert.AreEqual(1f, PoiCatalog.CountZeroWeight + PoiCatalog.CountOneWeight + PoiCatalog.CountTwoWeight, 0.0001f);
            Assert.AreEqual(2, PoiCatalog.MaxPoiPerSystem);
        }

        [Test]
        public void TypeWeights_EqualDefault_ThreeTypes_DocumentedAssumption()
        {
            // No approved type-mix ratio exists in the design brief -- equal weight (1/1/1) by explicit documented choice.
            Assert.AreEqual(3, PoiCatalog.TypeWeights.Length);
            foreach (var (_, weight) in PoiCatalog.TypeWeights) Assert.AreEqual(1f, weight);
        }

        [Test]
        public void RiskDistributions_MatchSpec()
        {
            Assert.AreEqual(0.60f, PoiCatalog.AnomalySafeWeight); Assert.AreEqual(0.40f, PoiCatalog.AnomalyUnstableWeight);
            Assert.AreEqual(0.70f, PoiCatalog.WreckSafeWeight); Assert.AreEqual(0.30f, PoiCatalog.WreckUnstableWeight);
            Assert.AreEqual(0.40f, PoiCatalog.FacilitySafeWeight); Assert.AreEqual(0.60f, PoiCatalog.FacilityUnstableWeight);
            Assert.AreEqual(1.5f, PoiCatalog.UnstableDurationMultiplier);
        }

        [Test]
        public void LocateIdentifyDurations_MatchSpec()
        {
            Assert.AreEqual(12f, PoiCatalog.ScannerT1LocateSeconds);
            Assert.AreEqual(6f, PoiCatalog.ScannerT2LocateSeconds);
            Assert.AreEqual(30f, PoiCatalog.ScoutLocateSeconds);
            Assert.AreEqual(15f, PoiCatalog.IdentifySeconds);
            Assert.AreEqual(2500f, PoiCatalog.IdentifyRange);
        }

        [Test]
        public void AnomalyConstants_MatchSpec()
        {
            Assert.AreEqual(40f, PoiCatalog.AnomalyShipSafeSeconds); Assert.AreEqual(60f, PoiCatalog.AnomalyShipUnstableSeconds);
            Assert.AreEqual(60f, PoiCatalog.AnomalyScoutSafeSeconds); Assert.AreEqual(90f, PoiCatalog.AnomalyScoutUnstableSeconds);
            Assert.AreEqual(250, PoiCatalog.AnomalyXp);
            Assert.AreEqual(3, PoiCatalog.AnomalySignalNearestIndexStart); // 4th nearest, 0-based
            Assert.AreEqual(7, PoiCatalog.AnomalySignalNearestIndexEnd);   // 8th nearest, inclusive
            Assert.AreEqual(1, PoiCatalog.AnomalySignalCount);
        }

        [Test]
        public void WreckRewardRanges_MatchSpec()
        {
            Assert.AreEqual((10, 20, 5, 10), PoiCatalog.WreckRewardRange(WreckSize.Small));
            Assert.AreEqual((20, 30, 10, 15), PoiCatalog.WreckRewardRange(WreckSize.Medium));
            Assert.AreEqual((30, 40, 15, 20), PoiCatalog.WreckRewardRange(WreckSize.Large));
            Assert.AreEqual(2, PoiCatalog.WreckMinNodes); Assert.AreEqual(4, PoiCatalog.WreckMaxNodes);
            Assert.AreEqual(150, PoiCatalog.WreckXp);
            Assert.AreEqual(10f, PoiCatalog.WreckMkITransferPerSecond); Assert.AreEqual(20f, PoiCatalog.WreckMkIITransferPerSecond);
        }

        [Test]
        public void FacilityConstants_MatchSpec()
        {
            Assert.AreEqual(180f, PoiCatalog.FacilityDismantleBaseSeconds); Assert.AreEqual(270f, PoiCatalog.FacilityDismantleUnstableSeconds);
            Assert.AreEqual(30, PoiCatalog.FacilityDismantleMinBasic); Assert.AreEqual(80, PoiCatalog.FacilityDismantleMaxBasic);
            Assert.AreEqual(10, PoiCatalog.FacilityDismantleMinSpecial); Assert.AreEqual(30, PoiCatalog.FacilityDismantleMaxSpecial);

            Assert.AreEqual(120f, PoiCatalog.FacilityRelayBaseSeconds); Assert.AreEqual(180f, PoiCatalog.FacilityRelayUnstableSeconds);
            Assert.AreEqual(25, PoiCatalog.FacilityRelayPartsCost);
            Assert.AreEqual("material.industrial_diamond", PoiCatalog.FacilityRelayDiamondCompositeId); // TF-003: corrected from the never-produced "material.diamond_composite"
            Assert.AreEqual(8, PoiCatalog.FacilityRelayDiamondCompositeCost);
            Assert.AreEqual("material.biocomposite", PoiCatalog.FacilityRelayBiocompositeId);
            Assert.AreEqual(5, PoiCatalog.FacilityRelayBiocompositeCost);
            Assert.AreEqual(2, PoiCatalog.FacilityRelaySignalCount);
            Assert.AreEqual(300, PoiCatalog.FacilityXp);
        }
    }
}
