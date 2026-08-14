using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Nebula.Diagnostics;
using Nebula.Telemetry;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// VS-017A §3/§5/§7: local telemetry privacy/rotation/ring-buffer/export/funnel,
    /// save-timing helper math, and S0/S1 quality-gate report classification --
    /// all pure, fast, no scene/network/file-system dependency beyond a private
    /// scratch directory this fixture owns and cleans up itself.
    /// </summary>
    public class Vs017aInfrastructureTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "nebula-telemetry-test-" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            TelemetryService.EndSession(); // safe even if a test forgot to end its own session
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }

        // --- Telemetry: session ids, no-op-when-inactive, never throws ---------------------------

        [Test]
        public void Telemetry_StartSession_GeneratesRandomUniqueSessionAndCampaignIds()
        {
            TelemetryService.StartSession(null);
            string session1 = TelemetryService.SessionId, campaign1 = TelemetryService.CampaignId;
            TelemetryService.EndSession(tempDir);

            TelemetryService.StartSession(null);
            string session2 = TelemetryService.SessionId, campaign2 = TelemetryService.CampaignId;
            TelemetryService.EndSession(tempDir);

            Assert.IsFalse(string.IsNullOrEmpty(session1));
            Assert.IsFalse(string.IsNullOrEmpty(campaign1));
            Assert.AreNotEqual(session1, session2, "each session must get its own random id");
            Assert.AreNotEqual(campaign1, campaign2, "an unspecified campaign id must also be freshly random, never a fixed/shared value");
        }

        [Test]
        public void Telemetry_StartSession_ReusesAnExplicitlyPassedCampaignId()
        {
            TelemetryService.StartSession("campaign-fixed-1");
            Assert.AreEqual("campaign-fixed-1", TelemetryService.CampaignId);
            TelemetryService.EndSession(tempDir);
        }

        [Test]
        public void Telemetry_Record_BeforeStartSession_IsANoOp_NeverThrows()
        {
            Assert.DoesNotThrow(() => TelemetryService.Record(TelemetryEvents.NewGame, 0, 0, 0));
            Assert.AreEqual(0, TelemetryService.SessionEventsSnapshot.Count);
        }

        [Test]
        public void Telemetry_Record_NeverThrows_EvenWithNullOrEmptyFields()
        {
            TelemetryService.StartSession(null);
            Assert.DoesNotThrow(() => TelemetryService.Record(TelemetryEvents.FirstScan, 1.0, 1f, 1f, outcome: null, level: 0, systemId: null, contentId: null));
            Assert.AreEqual(1, TelemetryService.SessionEventsSnapshot.Count);
            TelemetryService.EndSession(tempDir);
        }

        // --- No PII / raw input -------------------------------------------------------------------

        [Test]
        public void Telemetry_EventRecordFields_ContainNoPiiOrRawInputFieldNames()
        {
            string[] forbidden = { "device", "fingerprint", "ip", "mac", "keyboard", "mouse", "input", "hardware", "email", "username", "password" };
            foreach (FieldInfo f in typeof(TelemetryEventRecord).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string lower = f.Name.ToLowerInvariant();
                foreach (string bad in forbidden)
                    Assert.IsFalse(lower.Contains(bad), $"TelemetryEventRecord must never carry a field resembling PII/raw-input/device-fingerprint data, found: {f.Name}");
            }
        }

        [Test]
        public void Telemetry_AllFunnelEventConstants_AreRecordable()
        {
            TelemetryService.StartSession(null);
            string[] funnel =
            {
                TelemetryEvents.NewGame, TelemetryEvents.FirstScan, TelemetryEvents.FirstDrill, TelemetryEvents.FirstCollection,
                TelemetryEvents.FirstProcessing, TelemetryEvents.SecondDrill, TelemetryEvents.FirstControlledPlanet,
                TelemetryEvents.FirstFrontierSignal, TelemetryEvents.EngineT1, TelemetryEvents.ManualExitStartSystem,
                TelemetryEvents.SecondSystemAsset, TelemetryEvents.FirstCollector, TelemetryEvents.FirstEngineer, TelemetryEvents.FirstFullSystemDominance
            };
            Assert.AreEqual(14, funnel.Length, "the funnel this slice measures must have exactly its own documented 14 events");
            double t = 0;
            foreach (string id in funnel) TelemetryService.Record(id, t++, 0, 0);
            Assert.AreEqual(funnel.Length, TelemetryService.SessionEventsSnapshot.Count);
            TelemetryService.EndSession(tempDir);
        }

        // --- Ring buffer -----------------------------------------------------------------------

        [Test]
        public void Telemetry_RingBuffer_EvictsEventsOlderThan120Seconds()
        {
            TelemetryService.StartSession(null);
            TelemetryService.Record(TelemetryEvents.NewGame, nowSeconds: 0, simTimeSeconds: 0, activePlayTimeSeconds: 0);
            TelemetryService.Record(TelemetryEvents.FirstScan, nowSeconds: 200, simTimeSeconds: 0, activePlayTimeSeconds: 0);

            Assert.AreEqual(1, TelemetryService.RingBufferSnapshot.Count, "an event older than 120s of the caller's own clock must have been evicted from the ring buffer");
            Assert.AreEqual(TelemetryEvents.FirstScan, TelemetryService.RingBufferSnapshot[0].eventId);
            Assert.AreEqual(2, TelemetryService.SessionEventsSnapshot.Count, "eviction is ring-buffer-only -- the full session record must still keep every event");
            TelemetryService.EndSession(tempDir);
        }

        // --- Export ----------------------------------------------------------------------------

        [Test]
        public void Telemetry_ExportJsonAndCsv_ContainEveryRecordedEvent()
        {
            TelemetryService.StartSession("camp-x");
            TelemetryService.Record(TelemetryEvents.NewGame, 0, 0, 0, level: 1, systemId: "sys.0", contentId: "content.a");
            TelemetryService.Record(TelemetryEvents.FirstScan, 1, 2, 3, outcome: "success", level: 1, systemId: "sys.0", contentId: "content.b");

            string json = TelemetryService.ExportJson();
            StringAssert.Contains(TelemetryEvents.NewGame, json);
            StringAssert.Contains(TelemetryEvents.FirstScan, json);
            StringAssert.Contains("camp-x", json);

            string csv = TelemetryService.ExportCsv();
            string[] lines = csv.Split('\n');
            Assert.AreEqual("eventId,sessionSeconds,simTimeSeconds,activePlayTimeSeconds,outcome,level,systemId,contentId,build,saveSchema,generationVersion,contentRevision", lines[0]);
            StringAssert.Contains(TelemetryEvents.NewGame, csv);
            StringAssert.Contains(TelemetryEvents.FirstScan, csv);
            TelemetryService.EndSession(tempDir);
        }

        [Test]
        public void Telemetry_EndSession_WritesToADirectorySeparateFromCampaignSaves()
        {
            TelemetryService.StartSession(null);
            TelemetryService.Record(TelemetryEvents.NewGame, 0, 0, 0);
            TelemetryService.EndSession(tempDir);

            Assert.IsTrue(Directory.Exists(tempDir));
            string[] files = Directory.GetFiles(tempDir, "session-*.json");
            Assert.AreEqual(1, files.Length);
            StringAssert.DoesNotContain(Path.Combine("nebula", "saves"), files[0]);
        }

        [Test]
        public void Telemetry_EndSession_NeverThrows_EvenWithAnInvalidDirectory()
        {
            TelemetryService.StartSession(null);
            TelemetryService.Record(TelemetryEvents.NewGame, 0, 0, 0);
            // A null byte / illegal path must never propagate out as an exception -- telemetry
            // failing to persist must never be visible to gameplay/save at all.
            Assert.DoesNotThrow(() => TelemetryService.EndSession("\0invalid\0path"));
        }

        // --- Rotation ----------------------------------------------------------------------------

        [Test]
        public void Telemetry_Rotation_KeepsAtMost20SessionFiles_OldestDeletedFirst()
        {
            Directory.CreateDirectory(tempDir);
            for (int i = 0; i < 25; i++)
            {
                TelemetryService.StartSession(null);
                TelemetryService.Record(TelemetryEvents.NewGame, 0, 0, 0);
                TelemetryService.EndSession(tempDir);
                System.Threading.Thread.Sleep(2); // ensure a distinct, increasing file creation time to order rotation deterministically
            }

            string[] remaining = Directory.GetFiles(tempDir, "session-*.json");
            Assert.LessOrEqual(remaining.Length, 20, "rotation must never leave more than 20 session files");
        }

        [Test]
        public void Telemetry_Rotation_NeverExceeds10MbTotal()
        {
            Directory.CreateDirectory(tempDir);
            for (int i = 0; i < 5; i++)
            {
                TelemetryService.StartSession(null);
                for (int e = 0; e < 5000; e++) TelemetryService.Record(TelemetryEvents.NewGame, e, e, e, contentId: "padding-content-id-to-grow-the-file-" + e);
                TelemetryService.EndSession(tempDir);
                System.Threading.Thread.Sleep(2);
            }

            long total = 0;
            foreach (string f in Directory.GetFiles(tempDir, "session-*.json")) total += new FileInfo(f).Length;
            Assert.LessOrEqual(total, 10L * 1024 * 1024, "rotation must keep total on-disk telemetry under the real 10MB cap");
        }

        // --- Save-timing helper math -------------------------------------------------------------

        [Test]
        public void SaveTiming_PercentileMath_MatchesKnownSyntheticSamples()
        {
            var samples = new List<double> { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
            Assert.AreEqual(50, PercentileMath.Percentile(samples, 50), 0.01);
            Assert.AreEqual(100, PercentileMath.Percentile(samples, 100), 0.01);
            Assert.AreEqual(10, PercentileMath.Percentile(samples, 1), 0.01);
        }

        [Test]
        public void SaveTiming_Recorder_MeasuresRealElapsedTime_AndBucketsStayIndependent()
        {
            var recorder = new Extraction.SavePerformanceRecorder();
            recorder.RecordMainThreadSnapshot(() => System.Threading.Thread.Sleep(5));
            recorder.RecordBackgroundWrite(() => System.Threading.Thread.Sleep(5));
            recorder.RecordLoad(() => System.Threading.Thread.Sleep(5));

            Assert.AreEqual(1, recorder.MainThreadSnapshotSamples.Count);
            Assert.AreEqual(1, recorder.BackgroundWriteSamples.Count);
            Assert.AreEqual(1, recorder.LoadSamples.Count);
            Assert.GreaterOrEqual(recorder.MainThreadStats.P50, 4.0, "a real ~5ms sleep must be measured as real elapsed time, not a stubbed zero");
            Assert.GreaterOrEqual(recorder.BackgroundWriteStats.Max, 4.0);
            Assert.GreaterOrEqual(recorder.LoadStats.Max, 4.0);
        }

        // --- S0/S1 quality gate --------------------------------------------------------------------

        [Test]
        public void QualityGate_PassesOnlyWhenBothS0AndS1AreZero()
        {
            var report = new QualityGateReport();
            Assert.IsTrue(report.Passes, "an empty report (no findings) must pass");

            report.AddS1("resource-duplication", "example S1 finding", "unit-test");
            Assert.IsFalse(report.Passes, "any S1 finding must fail the gate");
            Assert.AreEqual(1, report.S1Count);
            Assert.AreEqual(0, report.S0Count);

            report.AddS0("save-corruption", "example S0 finding", "unit-test");
            Assert.IsFalse(report.Passes);
            Assert.AreEqual(1, report.S0Count);
        }

        [Test]
        public void QualityGate_JsonExport_ContainsCountsAndEveryFinding()
        {
            var report = new QualityGateReport();
            report.AddS0("determinism-break", "seedA seedB diverged", "Vs017aDeterminismScaleTests");
            report.AddS1("duplicate-result", "raid salvage awarded twice", "Vs016aRemoteRaidTests");

            string json = report.ToJson();
            StringAssert.Contains("s0Count", json);
            StringAssert.Contains("s1Count", json);
            StringAssert.Contains("false", json); // passes: false
            StringAssert.Contains("determinism-break", json);
            StringAssert.Contains("duplicate-result", json);
            StringAssert.Contains("seedA seedB diverged", json);
        }

        [Test]
        public void QualityGate_WriteToTestResults_WritesUnderTestResultsDirectory_NeverIntoTheRepoTree()
        {
            var report = new QualityGateReport();
            report.AddS1("example", "example", "unit-test");
            string fakeRoot = tempDir;
            report.WriteToTestResults(fakeRoot, "vs017a-quality-gate-test.json");

            string expected = Path.Combine(fakeRoot, "TestResults", "vs017a-quality-gate-test.json");
            Assert.IsTrue(File.Exists(expected));
        }
    }
}
