using System;
using System.Collections.Generic;
using System.IO;

namespace Nebula.Diagnostics
{
    /// <summary>VS-017A §7: S0 = save corruption, data loss, boot failure, permanent soft-lock, determinism break. S1 = resource duplication/loss, duplicate results, an invisible major-progress blocker, serious combat/streaming inconsistency.</summary>
    public enum QualityGateSeverity { S0, S1 }

    [Serializable]
    public class QualityGateFinding
    {
        public string severity; // "S0" or "S1" -- string, not the enum, so JsonUtility can serialize it directly
        public string category;
        public string description;
        public string source;

        public QualityGateFinding() { }
        public QualityGateFinding(QualityGateSeverity severity, string category, string description, string source)
        {
            this.severity = severity.ToString();
            this.category = category ?? "";
            this.description = description ?? "";
            this.source = source ?? "";
        }
    }

    [Serializable]
    internal class QualityGateReportFile
    {
        public string generatedAtUtc;
        public int s0Count;
        public int s1Count;
        public bool passes;
        public List<QualityGateFinding> findings = new List<QualityGateFinding>();
    }

    /// <summary>
    /// Machine-readable quality gate: an automated package only ever "passes" at
    /// S0=0 and S1=0. This class only classifies/aggregates/exports findings a
    /// caller hands it (real test-run results, manual audit notes, etc.) -- it
    /// never itself runs tests or decides what counts as a finding.
    /// </summary>
    public class QualityGateReport
    {
        public readonly List<QualityGateFinding> Findings = new List<QualityGateFinding>();

        public void AddS0(string category, string description, string source = "") =>
            Findings.Add(new QualityGateFinding(QualityGateSeverity.S0, category, description, source));

        public void AddS1(string category, string description, string source = "") =>
            Findings.Add(new QualityGateFinding(QualityGateSeverity.S1, category, description, source));

        public int S0Count => CountOf(QualityGateSeverity.S0);
        public int S1Count => CountOf(QualityGateSeverity.S1);
        public bool Passes => S0Count == 0 && S1Count == 0;

        private int CountOf(QualityGateSeverity severity)
        {
            int n = 0;
            foreach (QualityGateFinding f in Findings) if (f.severity == severity.ToString()) n++;
            return n;
        }

        public string ToJson()
        {
            var file = new QualityGateReportFile
            {
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                s0Count = S0Count, s1Count = S1Count, passes = Passes,
                findings = Findings
            };
            return UnityEngine.JsonUtility.ToJson(file, prettyPrint: true);
        }

        /// <summary>Writes under TestResults/ (already gitignored -- see .gitignore) so a report never gets committed as a stray large/binary artifact.</summary>
        public void WriteToTestResults(string projectRoot, string fileName)
        {
            string dir = Path.Combine(projectRoot, "TestResults");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), ToJson());
        }
    }
}
