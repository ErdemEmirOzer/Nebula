using NUnit.Framework;
using Nebula.Combat;
using Nebula.Modules;
using Nebula.World;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the two VS-013 milestone predicates (defense.weapon_coordination, defense.shield_systems) — real gates, never hardcoded true (VS-013 §2/§11).</summary>
    public class ModuleMilestonesVs013Tests
    {
        [TearDown]
        public void Cleanup()
        {
            StarSystemRegistry.Streaming = null;
            CombatCampaignState.Reset();
        }

        [Test]
        public void WeaponCoordination_IsFalse_WhenNoSystemIsCurrentlyFull()
        {
            StarSystemRegistry.Streaming = null;
            Assert.IsFalse(ModuleMilestones.IsSatisfied("defense.weapon_coordination"), "with no Full system materialized there is no 'here' to check for a Command Center");
        }

        [Test]
        public void ShieldSystems_IsFalse_UntilTheFirstProbeThreatIsKnown()
        {
            CombatCampaignState.Reset();
            Assert.IsFalse(ModuleMilestones.IsSatisfied("defense.shield_systems"));
        }

        [Test]
        public void ShieldSystems_BecomesTrue_TheInstantTheProbeWarningIsScheduled()
        {
            CombatCampaignState.Reset();
            CombatCampaignState.FirstProbe.ScheduleWarningIfNeeded();
            Assert.IsTrue(ModuleMilestones.IsSatisfied("defense.shield_systems"), "must read the real FirstProbeState.ThreatKnown flag, never a second hardcoded copy");
        }

        [Test]
        public void ShieldSystems_StaysTrue_AfterTheEncounterResolves()
        {
            CombatCampaignState.Reset();
            CombatCampaignState.FirstProbe.ScheduleWarningIfNeeded();
            CombatCampaignState.FirstProbe.MarkResolved(true);
            Assert.IsTrue(ModuleMilestones.IsSatisfied("defense.shield_systems"), "once known, the threat stays known for the rest of the campaign");
        }

        [Test]
        public void UnrelatedModules_DefaultToAlwaysSatisfied()
        {
            Assert.IsTrue(ModuleMilestones.IsSatisfied("industry.logistics_cargo"));
            Assert.IsTrue(ModuleMilestones.IsSatisfied("exploration.scanner"));
        }
    }
}
