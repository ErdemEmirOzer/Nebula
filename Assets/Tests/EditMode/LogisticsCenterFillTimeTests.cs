using NUnit.Framework;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Sectors;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode test for VS-011 §10.18: two standard (Common-rarity) T1 drills must fill a T1 Logistics Center in roughly 30-40 active minutes.</summary>
    public class LogisticsCenterFillTimeTests
    {
        private static PlanetProfile Profile(string planetId) => new PlanetProfile(
            new PlanetManifest(0, planetId, ResourceFamily.Metal, Rarity.Common, 40000f, 0f, 0f, 0f, 1200f,
                recommendedStart: false, EnvironmentClass.Calm, surfaceLayoutSeed: 1UL));

        [Test]
        public void TwoCommonRarityT1Drills_ContinuouslyFerried_FillAT1CenterWithin30To40ActiveMinutes()
        {
            // The Collector's transfer rate (10-20 vol/s = 600-1200/min) and the center's own
            // T1 cap (20 vol/s = 1200/min) both vastly exceed a Common drill's 8/min production,
            // so production is always the real bottleneck -- an idealized "drain every second"
            // Collector below is a fair stand-in for the real drill->Collector->center chain's
            // steady-state rate without needing to simulate travel/dock/queue timing.
            var drillA = new DrillRuntime(Profile("sys900/pl00"), DrillLifecycle.Active, 0f, 0f, stored: 0f, accumulator: 0f);
            var drillB = new DrillRuntime(Profile("sys900/pl01"), DrillLifecycle.Active, 0f, 0f, stored: 0f, accumulator: 0f);
            var center = new LogisticsCargo(LogisticsCenterCatalog.T1.Capacity);

            Assert.AreEqual(8f, ExtractionCatalog.T1ProductionPerMinute(Rarity.Common), "standard T1 drill = 8 raw/min");

            const float dt = 1f;
            float elapsedSeconds = 0f;
            const float safetyCapSeconds = 3600f; // never loop forever if something regresses
            while (center.Total < LogisticsCenterCatalog.T1.Capacity - 0.0001f && elapsedSeconds < safetyCapSeconds)
            {
                drillA.Tick(dt);
                drillB.Tick(dt);
                float takenA = drillA.Withdraw(drillA.StoredAmount);
                float takenB = drillB.Withdraw(drillB.StoredAmount);
                if (takenA > 0f) center.Add("resource.metal", takenA);
                if (takenB > 0f) center.Add("resource.metal", takenB);
                elapsedSeconds += dt;
            }

            float minutes = elapsedSeconds / 60f;
            Assert.AreEqual(LogisticsCenterCatalog.T1.Capacity, center.Total, 0.01f, "the center must actually reach full");
            Assert.GreaterOrEqual(minutes, 30f, $"filled in {minutes:0.0} minutes -- too fast for the 30-40 minute target");
            Assert.LessOrEqual(minutes, 40f, $"filled in {minutes:0.0} minutes -- too slow for the 30-40 minute target");
        }

        [Test]
        public void FiveDrillT2Target_25To35Minutes_IsAFutureTelemetryItem_NoDrillT2ContentExistsYet()
        {
            // VS-011 §10 explicitly forbids adding fake Drill T2 production/content just to
            // exercise this number -- Drill T2 doesn't exist in the project yet. This test only
            // proves the T2 capacity contract itself (1800 volume / 5 routes) that a future
            // Drill T2 slice must fill within 25-35 minutes; the real timing is real telemetry
            // for that later slice, not fabricated here.
            Assert.AreEqual(1800f, LogisticsCenterCatalog.T2.Capacity);
            Assert.AreEqual(5, LogisticsCenterCatalog.T2.MaxRoutes);
        }
    }
}
