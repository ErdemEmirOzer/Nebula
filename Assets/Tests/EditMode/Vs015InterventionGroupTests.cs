using System.Collections.Generic;
using NUnit.Framework;
using Nebula.Combat;

namespace Nebula.Tests.EditMode
{
    /// <summary>VS-015 §6: the single per-system intervention group, its System Intervention target priority, insufficient-known-threat rejection, and the solo-Engineer/no-Escort safety gates.</summary>
    public class Vs015InterventionGroupTests
    {
        [Test]
        public void Group_SingleInstancePerSystem_MembershipIsSetSemantics()
        {
            var group = new InterventionGroup("sys001");
            Assert.IsTrue(group.AddMember("vehicle-1"));
            Assert.IsFalse(group.AddMember("vehicle-1")); // no duplicate membership
            Assert.AreEqual(1, group.MemberVehicleIds.Count);
            group.RemoveMember("vehicle-1");
            Assert.IsTrue(group.IsEmpty);
        }

        [Test]
        public void DefaultPolicy_IsManual()
        {
            Assert.AreEqual(InterventionPolicy.Manual, new InterventionGroup("sys001").Policy);
        }

        [Test]
        public void SelectTarget_RejectsCandidate_WhenKnownThreatExceedsGroupPower()
        {
            var candidates = new List<InterventionCandidate>
            {
                new InterventionCandidate("planet-1", false, false, 0f, 0f, false, knownThreatPower: 500f)
            };
            Assert.IsNull(InterventionGroupResolver.SelectTarget(candidates, knownGroupPower: 200f));
        }

        [Test]
        public void SelectTarget_AcceptsCandidate_WhenGroupPowerMeetsOrExceedsThreat()
        {
            var candidates = new List<InterventionCandidate>
            {
                new InterventionCandidate("planet-1", false, false, 0f, 0f, false, knownThreatPower: 200f)
            };
            Assert.AreEqual("planet-1", InterventionGroupResolver.SelectTarget(candidates, knownGroupPower: 200f));
        }

        [Test]
        public void SelectTarget_Priority_CommandThreatBeatsSiegeBeatsEarlyEngagementBeatsCloseEtaBeatsId()
        {
            var candidates = new List<InterventionCandidate>
            {
                new InterventionCandidate("planet-b", false, true, 0f, 0f, false, 0f),
                new InterventionCandidate("planet-a", true, false, 0f, 0f, false, 0f), // command threatened wins outright
            };
            Assert.AreEqual("planet-a", InterventionGroupResolver.SelectTarget(candidates, 1000f));
        }

        [Test]
        public void SelectTarget_SiegeOrInvasion_BeatsEarlyEngagement()
        {
            var candidates = new List<InterventionCandidate>
            {
                new InterventionCandidate("planet-a", false, false, engagementAgeSeconds: 50f, 0f, false, 0f),
                new InterventionCandidate("planet-b", false, true, engagementAgeSeconds: 0f, 0f, false, 0f),
            };
            Assert.AreEqual("planet-b", InterventionGroupResolver.SelectTarget(candidates, 1000f));
        }

        [Test]
        public void SelectTarget_EarliestEngagement_BeatsCloserEta()
        {
            var candidates = new List<InterventionCandidate>
            {
                new InterventionCandidate("planet-a", false, false, engagementAgeSeconds: 5f, etaSeconds: 10f, false, 0f),
                new InterventionCandidate("planet-b", false, false, engagementAgeSeconds: 20f, etaSeconds: 100f, false, 0f),
            };
            Assert.AreEqual("planet-b", InterventionGroupResolver.SelectTarget(candidates, 1000f));
        }

        [Test]
        public void SelectTarget_CloserEta_BeatsCanonicalIdTiebreak()
        {
            var candidates = new List<InterventionCandidate>
            {
                new InterventionCandidate("planet-z", false, false, 0f, etaSeconds: 5f, false, 0f),
                new InterventionCandidate("planet-a", false, false, 0f, etaSeconds: 50f, false, 0f),
            };
            Assert.AreEqual("planet-z", InterventionGroupResolver.SelectTarget(candidates, 1000f));
        }

        [Test]
        public void SelectTarget_FinalTiebreak_IsCanonicalPlanetId()
        {
            var candidates = new List<InterventionCandidate>
            {
                new InterventionCandidate("planet-z", false, false, 0f, 0f, false, 0f),
                new InterventionCandidate("planet-a", false, false, 0f, 0f, false, 0f),
            };
            Assert.AreEqual("planet-a", InterventionGroupResolver.SelectTarget(candidates, 1000f));
        }

        [Test]
        public void MaySoloEngineerEngage_OnlyTrueForProbeThreat()
        {
            Assert.IsTrue(InterventionGroupResolver.MaySoloEngineerEngage(threatIsProbeOnly: true));
            Assert.IsFalse(InterventionGroupResolver.MaySoloEngineerEngage(threatIsProbeOnly: false));
        }

        [Test]
        public void ShouldHoldOrRetreat_WithNoEscortAndNonProbeThreat()
        {
            Assert.IsTrue(InterventionGroupResolver.ShouldHoldOrRetreat(escortCount: 0, threatIsProbeOnly: false));
            Assert.IsFalse(InterventionGroupResolver.ShouldHoldOrRetreat(escortCount: 1, threatIsProbeOnly: false));
            Assert.IsFalse(InterventionGroupResolver.ShouldHoldOrRetreat(escortCount: 0, threatIsProbeOnly: true));
        }

        [Test]
        public void Group_NeverProducesOrTeleportsVehicles_IsJustMembershipAndPolicyState()
        {
            var group = new InterventionGroup("sys001");
            group.SetPolicy(InterventionPolicy.SystemIntervention);
            group.SetActiveTarget("planet-1");
            Assert.AreEqual("planet-1", group.ActiveTargetPlanetId);
            group.ClearActiveTarget();
            Assert.IsNull(group.ActiveTargetPlanetId);
        }
    }
}
