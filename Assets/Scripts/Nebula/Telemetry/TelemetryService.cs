using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Nebula.Telemetry
{
    /// <summary>
    /// VS-017A §3: the single local, anonymous telemetry authority. No network call anywhere
    /// in this class -- every method only ever touches memory and the local filesystem. No
    /// personal data, device fingerprint, or raw input is ever recorded, only named funnel
    /// events (see <see cref="TelemetryEvents"/>) with a handful of small structured fields.
    /// Every public entrypoint that can fail (file I/O) swallows its own exception -- telemetry
    /// must never be the reason gameplay or a save operation stops working. Time is always an
    /// explicit caller-supplied "now" (never Time.* read internally), matching every other pure
    /// Tick(dt)-style system in this project (DT-001).
    /// </summary>
    [Serializable]
    public class TelemetryEventRecord
    {
        public string eventId;
        /// <summary>Seconds since TelemetryService.StartSession was called, per the caller's own clock -- used only for local ordering/ring-buffer eviction, never uploaded or compared against a real timestamp.</summary>
        public double sessionSeconds;
        public float simTimeSeconds;
        public float activePlayTimeSeconds;
        public string outcome;
        public int level;
        public string systemId;
        public string contentId;
        public string build;
        public int saveSchema;
        public int generationVersion;
        public int contentRevision;
    }

    /// <summary>VS-017A §3: the minimum funnel this slice measures -- new game through first full system dominance.</summary>
    public static class TelemetryEvents
    {
        public const string NewGame = "new_game";
        public const string FirstScan = "first_scan";
        public const string FirstDrill = "first_drill";
        public const string FirstCollection = "first_collection";
        public const string FirstProcessing = "first_processing";
        public const string SecondDrill = "second_drill";
        public const string FirstControlledPlanet = "first_controlled_planet";
        public const string FirstFrontierSignal = "first_frontier_signal";
        public const string EngineT1 = "engine_t1";
        public const string ManualExitStartSystem = "manual_exit_start_system";
        /// <summary>VS-017A.2 §A1: the campaign's SECOND distinct star system reaching a real Asset state (≥1 genuinely Controlled planet). Deliberately replaces the old "second_entity", which was mis-measured off a second Anomaly/POI completion — an entirely different thing. Old records are never migrated into this id.</summary>
        public const string SecondSystemAsset = "second_system_asset";
        public const string FirstCollector = "first_collector";
        public const string FirstEngineer = "first_engineer";
        public const string FirstFullSystemDominance = "first_full_system_dominance";
    }

    [Serializable]
    internal class TelemetrySessionFile
    {
        public string sessionId;
        public string campaignId;
        public List<TelemetryEventRecord> events = new List<TelemetryEventRecord>();
    }

    [Serializable]
    internal class FunnelDedupEntry { public string campaignId; public string eventId; }

    [Serializable]
    internal class FunnelDedupIndexFile { public List<FunnelDedupEntry> entries = new List<FunnelDedupEntry>(); }

    public static class TelemetryService
    {
        private const float RingBufferSeconds = 120f;
        private const int MaxSessionFiles = 20;
        private const long MaxTotalBytes = 10L * 1024 * 1024;
        private const string FilePrefix = "session-";

        public static string SessionId { get; private set; }
        public static string CampaignId { get; private set; }
        public static bool SessionActive { get; private set; }

        private static readonly List<TelemetryEventRecord> sessionEvents = new List<TelemetryEventRecord>();
        private static readonly List<TelemetryEventRecord> ringBuffer = new List<TelemetryEventRecord>();

        /// <summary>Random per-session id, and a random per-campaign id the first time a campaign is seen (callers persist/reuse CampaignId across sessions of the same save if they want session-to-session continuity -- this service never reads or infers one from save data itself).</summary>
        public static void StartSession(string campaignId)
        {
            CampaignId = string.IsNullOrEmpty(campaignId) ? NewRandomId() : campaignId;
            SessionId = NewRandomId();
            sessionEvents.Clear();
            ringBuffer.Clear();
            SessionActive = true;
        }

        /// <summary>
        /// VS-017A.2 §A2: re-scopes the live session onto a different campaign identity —
        /// used when a load adopts the identity its save envelope carried. The session id and
        /// already-recorded events are untouched; only which campaign later funnel events are
        /// deduplicated against changes. Telemetry never mints a campaign id of its own here.
        /// </summary>
        public static void SetCampaignId(string campaignId)
        {
            if (!string.IsNullOrEmpty(campaignId)) CampaignId = campaignId;
        }

        private static string NewRandomId() => Guid.NewGuid().ToString("N");

        /// <summary>Records one funnel event. A no-op (not an exception) if no session is active or if anything about the local environment fails -- telemetry must never be able to stop or destabilize gameplay.</summary>
        public static void Record(string eventId, double nowSeconds, float simTimeSeconds, float activePlayTimeSeconds,
            string outcome = "", int level = 0, string systemId = "", string contentId = "")
        {
            if (!SessionActive || string.IsNullOrEmpty(eventId)) return;
            try
            {
                var record = new TelemetryEventRecord
                {
                    eventId = eventId, sessionSeconds = nowSeconds, simTimeSeconds = simTimeSeconds,
                    activePlayTimeSeconds = activePlayTimeSeconds, outcome = outcome ?? "", level = level,
                    systemId = systemId ?? "", contentId = contentId ?? "",
                    build = Application.version,
                    saveSchema = Extraction.SaveService.CurrentSchema,
                    generationVersion = Sectors.SectorManifestGenerator.GenerationVersion,
                    contentRevision = Content.ContentFingerprint.ContentRevision
                };
                sessionEvents.Add(record);
                ringBuffer.Add(record);
                ringBuffer.RemoveAll(e => nowSeconds - e.sessionSeconds > RingBufferSeconds);
            }
            catch
            {
                // Never let a telemetry failure propagate into gameplay/save.
            }
        }

        // --- VS-017A.1 §1: campaign-scoped funnel uniqueness -------------------------------------
        // A "first_X" funnel event must fire at most once per campaign, surviving save/load,
        // session restart, Full<->1Hz, demote/promote, UI reopen, and event-subscriber re-wiring --
        // i.e. it must survive even a full process restart, so an in-memory-only flag isn't enough.
        // Keyed by (campaignId, eventId) and persisted in its own small file, deliberately separate
        // from the rotation-subject session-*.json files so log rotation can never reset it.
        private const string FunnelIndexFileName = "funnel-index.json";
        private static readonly Dictionary<string, HashSet<string>> funnelSeenByCampaign = new Dictionary<string, HashSet<string>>();
        private static bool funnelIndexLoaded;
        private static string funnelIndexDirectoryLoaded;

        /// <summary>Records a funnel event that must only ever count once for this campaignId -- a second call for the same (CampaignId, eventId) is a silent, total no-op (never appended to the session, ring buffer, or export). Use this (not the raw <see cref="Record"/>) for every "first_X"/"second_X" milestone.</summary>
        public static void RecordFunnelEventOnce(string eventId, double nowSeconds, float simTimeSeconds, float activePlayTimeSeconds,
            string outcome = "", int level = 0, string systemId = "", string contentId = "", string directoryOverride = null)
        {
            if (!SessionActive || string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(CampaignId)) return;
            try
            {
                EnsureFunnelIndexLoaded(directoryOverride);
                if (!funnelSeenByCampaign.TryGetValue(CampaignId, out HashSet<string> seen))
                {
                    seen = new HashSet<string>();
                    funnelSeenByCampaign[CampaignId] = seen;
                }
                if (!seen.Add(eventId)) return; // already recorded for this campaign -- total no-op

                Record(eventId, nowSeconds, simTimeSeconds, activePlayTimeSeconds, outcome, level, systemId, contentId);
                SaveFunnelIndex(directoryOverride);
            }
            catch
            {
                // Never let a telemetry failure propagate into gameplay/save.
            }
        }

        /// <summary>Test/debug-only: whether a given campaign+event pair has already been recorded, without recording anything.</summary>
        public static bool HasFunnelEventBeenRecorded(string campaignId, string eventId, string directoryOverride = null)
        {
            EnsureFunnelIndexLoaded(directoryOverride);
            return !string.IsNullOrEmpty(campaignId) && funnelSeenByCampaign.TryGetValue(campaignId, out HashSet<string> seen) && seen.Contains(eventId);
        }

        private static void EnsureFunnelIndexLoaded(string directoryOverride)
        {
            string dir = directoryOverride ?? TelemetryDirectory;
            if (funnelIndexLoaded && funnelIndexDirectoryLoaded == dir) return;
            funnelSeenByCampaign.Clear();
            try
            {
                string path = Path.Combine(dir, FunnelIndexFileName);
                if (File.Exists(path))
                {
                    var index = JsonUtility.FromJson<FunnelDedupIndexFile>(File.ReadAllText(path));
                    if (index?.entries != null)
                    {
                        foreach (FunnelDedupEntry e in index.entries)
                        {
                            if (e == null || string.IsNullOrEmpty(e.campaignId) || string.IsNullOrEmpty(e.eventId)) continue;
                            if (!funnelSeenByCampaign.TryGetValue(e.campaignId, out HashSet<string> seen)) funnelSeenByCampaign[e.campaignId] = seen = new HashSet<string>();
                            seen.Add(e.eventId);
                        }
                    }
                }
            }
            catch
            {
                // A corrupt/unreadable index starts fresh rather than ever blocking gameplay.
            }
            funnelIndexLoaded = true;
            funnelIndexDirectoryLoaded = dir;
        }

        private static void SaveFunnelIndex(string directoryOverride)
        {
            string dir = directoryOverride ?? TelemetryDirectory;
            Directory.CreateDirectory(dir);
            var index = new FunnelDedupIndexFile();
            foreach (var kv in funnelSeenByCampaign)
                foreach (string eventId in kv.Value)
                    index.entries.Add(new FunnelDedupEntry { campaignId = kv.Key, eventId = eventId });
            File.WriteAllText(Path.Combine(dir, FunnelIndexFileName), JsonUtility.ToJson(index));
        }

        /// <summary>Test-only: forces the next funnel check/record to reload the index file from disk instead of trusting the in-memory cache -- EditMode/PlayMode tests reuse the same static class across many isolated temp directories within one process.</summary>
        public static void InvalidateFunnelIndexCache() { funnelIndexLoaded = false; funnelIndexDirectoryLoaded = null; }

        /// <summary>Every event still within the last 120s of the caller's own clock -- read-only, for a debug view or a crash-adjacent dump; never itself the persisted record (EndSession's full session file is).</summary>
        public static IReadOnlyList<TelemetryEventRecord> RingBufferSnapshot => ringBuffer;
        public static IReadOnlyList<TelemetryEventRecord> SessionEventsSnapshot => sessionEvents;

        /// <summary>
        /// Directory override for tests, so a run never writes into the operator's real
        /// telemetry folder. The per-call <c>directoryOverride</c> arguments already
        /// redirect the writers; this redirects <see cref="TelemetryDirectory"/> itself,
        /// which is what a caller with no argument to pass ends up using.
        /// </summary>
        public static string DirectoryOverride { get; set; }

        /// <summary>
        /// Test override, then the smoke sandbox, then the engine's folder.
        /// <para>
        /// The sandbox term is here for a specific, recorded reason: the file a smoke run
        /// once damaged on the operator's machine was <c>funnel-index.json</c>, which
        /// lives in this directory. See <c>NebulaSmokeDataRoot</c>.
        /// </para>
        /// </summary>
        public static string TelemetryDirectory =>
            DirectoryOverride
            ?? Platform.NebulaSmokeDataRoot.Folder("telemetry")
            ?? Platform.NebulaEditorScratch.Folder("telemetry")
            ?? Path.Combine(Application.persistentDataPath, "nebula", "telemetry");

        public static string ExportJson()
        {
            var file = new TelemetrySessionFile { sessionId = SessionId, campaignId = CampaignId, events = new List<TelemetryEventRecord>(sessionEvents) };
            return JsonUtility.ToJson(file, prettyPrint: true);
        }

        public static string ExportCsv()
        {
            var sb = new StringBuilder();
            sb.Append("eventId,sessionSeconds,simTimeSeconds,activePlayTimeSeconds,outcome,level,systemId,contentId,build,saveSchema,generationVersion,contentRevision\n");
            foreach (TelemetryEventRecord e in sessionEvents)
            {
                sb.Append(CsvField(e.eventId)).Append(',')
                  .Append(e.sessionSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.simTimeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.activePlayTimeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(CsvField(e.outcome)).Append(',')
                  .Append(e.level).Append(',')
                  .Append(CsvField(e.systemId)).Append(',')
                  .Append(CsvField(e.contentId)).Append(',')
                  .Append(CsvField(e.build)).Append(',')
                  .Append(e.saveSchema).Append(',')
                  .Append(e.generationVersion).Append(',')
                  .Append(e.contentRevision).Append('\n');
            }
            return sb.ToString();
        }

        private static string CsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            bool needsQuote = value.IndexOfAny(new[] { ',', '"', '\n' }) >= 0;
            string escaped = value.Replace("\"", "\"\"");
            return needsQuote ? "\"" + escaped + "\"" : escaped;
        }

        /// <summary>Writes this session's file (separate from the campaign save directory entirely) and rotates old sessions deterministically. A no-op session (StartSession never called) does nothing. Never throws.</summary>
        public static void EndSession(string directoryOverride = null)
        {
            if (!SessionActive) return;
            try
            {
                WriteSessionFile(directoryOverride);
                RotateOldSessions(directoryOverride);
            }
            catch
            {
                // Never let a telemetry failure propagate into gameplay/save.
            }
            finally
            {
                SessionActive = false;
                // Once a session ends, its data has already been (best-effort) persisted to disk --
                // in-memory session/ring-buffer state belongs to the active session, not the next one.
                sessionEvents.Clear();
                ringBuffer.Clear();
            }
        }

        private static void WriteSessionFile(string directoryOverride)
        {
            string dir = directoryOverride ?? TelemetryDirectory;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, FilePrefix + SessionId + ".json");
            File.WriteAllText(path, ExportJson());
        }

        /// <summary>Oldest-first by file creation time (a stable, deterministic order -- never filesystem enumeration order) until both the count and total-size caps are satisfied.</summary>
        private static void RotateOldSessions(string directoryOverride)
        {
            string dir = directoryOverride ?? TelemetryDirectory;
            if (!Directory.Exists(dir)) return;

            var files = new List<FileInfo>(new DirectoryInfo(dir).GetFiles(FilePrefix + "*.json"));
            files.Sort((a, b) => a.CreationTimeUtc.CompareTo(b.CreationTimeUtc));

            long total = 0;
            foreach (FileInfo f in files) total += f.Length;

            int i = 0;
            while (i < files.Count && (files.Count - i > MaxSessionFiles || total > MaxTotalBytes))
            {
                total -= files[i].Length;
                try { files[i].Delete(); } catch { /* best-effort rotation */ }
                i++;
            }
        }
    }
}
