using NUnit.Framework;
using Nebula.Combat;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the VS-013.1 §4 First Probe trigger fix: real defense WEAPON only (never a lone Command Center), no reset on temporary disable, and the finalized (no-longer-provisional) XP constant.</summary>
    public class FirstProbeTriggerFixTests
    {
        [Test]
        public void IsActiveDefenseWeapon_CommandCenterAlone_NeverQualifies()
        {
            Assert.IsFalse(SurfaceStructureCatalog.IsActiveDefenseWeapon(SurfaceStructureType.Command));
        }

        [Test]
        public void IsActiveDefenseWeapon_Laser_Qualifies()
        {
            Assert.IsTrue(SurfaceStructureCatalog.IsActiveDefenseWeapon(SurfaceStructureType.Laser));
        }

        [Test]
        public void ScheduleWarningIfNeeded_RepeatedCalls_NeverResetTheCountdown()
        {
            // Simulates "temporary disable never resets the schedule": the real caller
            // (CombatCampaignDirector) only calls this when HasAnyActiveDefense() is
            // currently true; ScheduleWarningIfNeeded's own idempotency (Status != NotScheduled
            // -> no-op) is what guarantees a defense going temporarily inactive and back
            // active again can never re-trigger or reset the warning.
            var probe = new FirstProbeState();
            probe.ScheduleWarningIfNeeded();
            probe.TickWarning(100f); // 150 -> 50 remaining

            // "Defense goes inactive" in practice just means the caller stops calling
            // ScheduleWarningIfNeeded for a while -- it does NOT call anything that could
            // reset Status. Simulate "defense comes back active" as a second call.
            probe.ScheduleWarningIfNeeded();

            Assert.AreEqual(50f, probe.WarningRemainingSeconds, 0.01f, "a re-trigger while already scheduled must never reset the countdown");
        }

        [Test]
        public void FirstProbeDefeatXp_IsFinalized_NotProvisionalAnymore()
        {
            Assert.AreEqual(400, CombatCatalog.FirstProbeDefeatXp);
        }
    }
}
