using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Nebula.Progression
{
    /// <summary>
    /// Minimal, privacy-clean local event log — the extensible base for later
    /// analytics, NOT a dashboard. Records session id, campaign id, content
    /// version, active session time and named events with a result and a
    /// stable content ID. No network, no auto-upload; no usernames, IPs,
    /// device fingerprints, file paths or raw input. Logging failures never
    /// stop gameplay or saving (all IO is best-effort in try/catch). Old logs
    /// are trimmed past 20 sessions / 10 MB. ExportJsonl() is the open API.
    /// </summary>
    public static class SessionLog
    {
        [Serializable]
        private class Entry { public float t; public string name; public string result; public string contentId; }

        private const int ContentVersion = 4;   // VS-004
        private const long MaxBytes = 10 * 1024 * 1024;
        private const int MaxSessions = 20;

        private static string sessionId;
        private static string campaignId;
        private static float startRealtime;
        private static readonly List<Entry> entries = new List<Entry>();

        /// <summary>
        /// Directory override for tests, so a run never appends to the operator's real
        /// session log. Same seam and same intent as
        /// <c>NebulaInputBindingStore.DirectoryOverride</c>; the game never sets it.
        /// </summary>
        public static string DirectoryOverride { get; set; }

        /// <summary>
        /// Test override, then the smoke sandbox, then the engine's folder. See
        /// <c>SaveService.Dir</c> for why the order is that way and why the last term
        /// must stay unevaluated when either of the first two answers.
        /// </summary>
        private static string Dir =>
            DirectoryOverride
            ?? Platform.NebulaSmokeDataRoot.Folder("logs")
            ?? Platform.NebulaEditorScratch.Folder("logs")
            ?? Path.Combine(Application.persistentDataPath, "nebula", "logs");
        private static string FilePath => Path.Combine(Dir, "sessions.jsonl");

        public static void StartSession(string campaign)
        {
            sessionId = Guid.NewGuid().ToString("N");
            campaignId = campaign;
            startRealtime = Time.realtimeSinceStartup;
            entries.Clear();
            TrimIfNeeded();
            Log("session_start", "content" + ContentVersion, null);
        }

        public static void Log(string name, string result, string contentId)
        {
            try
            {
                entries.Add(new Entry
                {
                    t = Time.realtimeSinceStartup - startRealtime,
                    name = name, result = result, contentId = contentId
                });
            }
            catch { /* logging must never break gameplay */ }
        }

        public static void EndSession()
        {
            Log("session_end", null, null);
            Flush();
        }

        /// <summary>Open export API: current session's events as JSONL.</summary>
        public static string ExportJsonl()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                sb.Append("{\"session\":\"").Append(sessionId)
                  .Append("\",\"campaign\":\"").Append(campaignId)
                  .Append("\",\"t\":").Append(e.t.ToString("0.00"))
                  .Append(",\"name\":\"").Append(e.name).Append('"');
                if (e.result != null) sb.Append(",\"result\":\"").Append(Escape(e.result)).Append('"');
                if (e.contentId != null) sb.Append(",\"contentId\":\"").Append(Escape(e.contentId)).Append('"');
                sb.Append("}\n");
            }
            return sb.ToString();
        }

        private static void Flush()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(FilePath, ExportJsonl());
            }
            catch { /* best-effort */ }
        }

        private static void TrimIfNeeded()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var info = new FileInfo(FilePath);
                if (info.Length <= MaxBytes) return;
                // Over budget: keep only the tail (~last MaxSessions worth by
                // line count is approximate; simplest safe trim is truncate).
                string[] lines = File.ReadAllLines(FilePath);
                int keep = Mathf.Min(lines.Length, MaxSessions * 40);
                var tail = new string[keep];
                Array.Copy(lines, lines.Length - keep, tail, 0, keep);
                File.WriteAllLines(FilePath, tail);
            }
            catch { /* best-effort */ }
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
