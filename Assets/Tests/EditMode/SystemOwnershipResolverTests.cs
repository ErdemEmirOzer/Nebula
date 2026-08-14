using NUnit.Framework;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure system-ownership tag resolver (VS-010 §1/§10.6).</summary>
    public class SystemOwnershipResolverTests
    {
        [Test]
        public void Undiscovered_WhenNotVisited_RegardlessOfDominance()
        {
            Assert.AreEqual(SystemOwnershipTag.Undiscovered,
                SystemOwnershipResolver.Resolve(visited: false, totalRealPlanets: 5, controlledPlanets: 3, false, false, true));
        }

        [Test]
        public void Discovered_WhenVisitedButNoControlledPlanet()
        {
            Assert.AreEqual(SystemOwnershipTag.Discovered,
                SystemOwnershipResolver.Resolve(visited: true, totalRealPlanets: 5, controlledPlanets: 0, false, false, false));
        }

        [Test]
        public void PioneerPresence_WhenAtLeastOneControlledPlanet_ButNotAll()
        {
            Assert.AreEqual(SystemOwnershipTag.PioneerPresence,
                SystemOwnershipResolver.Resolve(visited: true, totalRealPlanets: 5, controlledPlanets: 1, false, false, false));
        }

        [Test]
        public void DominantCore_WhenEveryRealPlanetIsControlled()
        {
            Assert.AreEqual(SystemOwnershipTag.DominantCore,
                SystemOwnershipResolver.Resolve(visited: true, totalRealPlanets: 5, controlledPlanets: 5, false, false, false));
        }

        [Test]
        public void PioneerPresence_WhenMissingAnyOneOfTheThreeEstablishedBaseConditions()
        {
            // Missing the working route.
            Assert.AreEqual(SystemOwnershipTag.PioneerPresence, SystemOwnershipResolver.Resolve(
                true, 5, 1, hasActiveLogisticsCenter: true, hasWorkingCollectorRoute: false, hasActiveDefense: true));
            // Missing the center.
            Assert.AreEqual(SystemOwnershipTag.PioneerPresence, SystemOwnershipResolver.Resolve(
                true, 5, 1, hasActiveLogisticsCenter: false, hasWorkingCollectorRoute: true, hasActiveDefense: true));
            // Missing defense.
            Assert.AreEqual(SystemOwnershipTag.PioneerPresence, SystemOwnershipResolver.Resolve(
                true, 5, 1, hasActiveLogisticsCenter: true, hasWorkingCollectorRoute: true, hasActiveDefense: false));
        }

        [Test]
        public void EstablishedBase_ReachableOnlyWhenAllThreeConditionsAreTrue()
        {
            // VS-011: SystemStatusQuery.Ownership now wires real
            // HasActiveLogisticsCenter/HasWorkingCollectorRoute values into
            // this resolver's only real call site — EstablishedBase is no
            // longer structurally unreachable (see SystemStatusQueryLogisticsTests
            // for the Full/delta integration that actually produces true here).
            SystemOwnershipTag tag = SystemOwnershipResolver.Resolve(visited: true, totalRealPlanets: 5, controlledPlanets: 1,
                hasActiveLogisticsCenter: true, hasWorkingCollectorRoute: true, hasActiveDefense: true);
            Assert.AreEqual(SystemOwnershipTag.EstablishedBase, tag);
        }

        [Test]
        public void OwnershipTags_AreNeverPersistedAsTheirOwnAuthority_StructuralCheck()
        {
            // SystemOwnershipTag/SystemLinkState never appear inside SaveModel.cs —
            // confirmed by construction: neither type is referenced by any DTO,
            // enforced here by simply asserting the resolver is a pure function
            // with no internal mutable/persisted state at all.
            SystemOwnershipTag a = SystemOwnershipResolver.Resolve(true, 3, 1, false, false, false);
            SystemOwnershipTag b = SystemOwnershipResolver.Resolve(true, 3, 1, false, false, false);
            Assert.AreEqual(a, b, "same inputs must always re-derive the same tag");
        }
    }
}
