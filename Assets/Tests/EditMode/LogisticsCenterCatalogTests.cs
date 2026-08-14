using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Logistics;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the System Logistics Center's pure catalog data (VS-011 §2/§3/§10.3).</summary>
    public class LogisticsCenterCatalogTests
    {
        [Test]
        public void T1_HasSpecifiedGateCostDurationCapacityRoutesDocksTransferAndDurability()
        {
            LogisticsCenterTierDef t1 = LogisticsCenterCatalog.T1;
            Assert.AreEqual(18, t1.RequiredLevel);
            Assert.AreEqual(1, t1.RequiredModuleTier);
            Assert.AreEqual(80, t1.PartsCost);
            Assert.AreEqual(25, t1.MetalAlloyCost);
            Assert.AreEqual(20, t1.BiocompositeCost);
            Assert.AreEqual(180f, t1.BuildSeconds);
            Assert.AreEqual(600f, t1.Capacity);
            Assert.AreEqual(2, t1.MaxRoutes);
            Assert.AreEqual(1, t1.DockCount);
            Assert.AreEqual(20f, t1.TransferVolumePerSecond);
            Assert.AreEqual(400f, t1.ShieldMax);
            Assert.AreEqual(800f, t1.ArmorMax);
            Assert.AreEqual(1200f, t1.HullMax);
        }

        [Test]
        public void T2_HasSpecifiedGateCostDurationCapacityRoutesDocksTransferAndDurability()
        {
            LogisticsCenterTierDef t2 = LogisticsCenterCatalog.T2;
            Assert.AreEqual(25, t2.RequiredLevel);
            Assert.AreEqual(2, t2.RequiredModuleTier);
            Assert.AreEqual(150, t2.PartsCost);
            Assert.AreEqual(60, t2.MetalAlloyCost);
            Assert.AreEqual(45, t2.BiocompositeCost);
            Assert.AreEqual(360f, t2.BuildSeconds);
            Assert.AreEqual(1800f, t2.Capacity);
            Assert.AreEqual(5, t2.MaxRoutes);
            Assert.AreEqual(2, t2.DockCount);
            Assert.AreEqual(40f, t2.TransferVolumePerSecond);
            Assert.AreEqual(800f, t2.ShieldMax);
            Assert.AreEqual(1600f, t2.ArmorMax);
            Assert.AreEqual(2400f, t2.HullMax);
        }

        [Test]
        public void Get_ResolvesByTier()
        {
            Assert.AreSame(LogisticsCenterCatalog.T1, LogisticsCenterCatalog.Get(LogisticsCenterTier.T1));
            Assert.AreSame(LogisticsCenterCatalog.T2, LogisticsCenterCatalog.Get(LogisticsCenterTier.T2));
        }

        [Test]
        public void RefundFraction_Matches75Percent_SameAsSurfaceStructures()
        {
            Assert.AreEqual(0.75f, LogisticsCenterCatalog.RefundFraction);
            Assert.AreEqual(SurfaceStructureCatalog.RefundFraction, LogisticsCenterCatalog.RefundFraction);
        }

        [Test]
        public void DismantleRefundFraction_Is50Percent()
        {
            Assert.AreEqual(0.5f, LogisticsCenterCatalog.DismantleRefundFraction);
        }

        [Test]
        public void AcceptedItems_ExactlyFiveRawPlusPartBasicPlusFiveMaterials()
        {
            foreach (string raw in ExtractionCatalog.ResourceIds)
                Assert.IsTrue(LogisticsCenterCatalog.IsAcceptedItem(raw), raw);
            Assert.IsTrue(LogisticsCenterCatalog.IsAcceptedItem(ProcessingCatalog.BasicPartId));
            foreach (string mat in ProcessingCatalog.MaterialIds)
                Assert.IsTrue(LogisticsCenterCatalog.IsAcceptedItem(mat), mat);
        }

        [Test]
        public void RejectedItems_NuclearEnergyCoreAndUnknownIds()
        {
            Assert.IsFalse(LogisticsCenterCatalog.IsAcceptedItem(Modules.ModuleCatalog.EnergyNuclearId));
            Assert.IsFalse(LogisticsCenterCatalog.IsAcceptedItem(Modules.ModuleCatalog.CoreNuclearId));
            Assert.IsFalse(LogisticsCenterCatalog.IsAcceptedItem("item.universal_pioneer_build_kit"));
            Assert.IsFalse(LogisticsCenterCatalog.IsAcceptedItem("resource.unknown"));
            Assert.IsFalse(LogisticsCenterCatalog.IsAcceptedItem(null));
        }
    }
}
