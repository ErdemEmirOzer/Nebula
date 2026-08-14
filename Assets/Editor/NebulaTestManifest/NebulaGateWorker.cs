using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using NUnit.Framework.Interfaces;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Nebula.EditorTools.TestGate
{
    /// <summary>
    /// One gate worker: run exactly the leaves it was given, journal each one the
    /// instant it finishes, write the result XML, and exit.
    ///
    ///   Unity -batchmode -nographics -projectPath &lt;mirror&gt; -logFile &lt;log&gt; \
    ///     -executeMethod Nebula.EditorTools.TestGate.NebulaGateWorker.Run \
    ///     -nebulaWorkerPlan &lt;plan.json&gt;
    ///
    /// Not -runTests. Two reasons, both load-bearing.
    ///
    /// 1. -runTests selects with -testFilter, and SettingsBuilder.cs:67 assigns that to
    ///    Filter.groupNames, which Filter.cs:25 documents as REGEX. Every leaf name
    ///    contains dots, and a [TestCase] leaf's name contains parentheses; passed raw,
    ///    such a name does not match itself literally and does match siblings it was
    ///    never meant to select. Filter.testNames (Filter.cs:22) is compared by
    ///    FullNameFilter with IsRegex left false, i.e. ordinal equality, so a leaf
    ///    either is or is not in this worker's set. "Every leaf ran exactly once" is
    ///    only provable on the exact-match path.
    ///
    /// 2. -runTests offers no hook to install a per-leaf journal, and without one a
    ///    worker that dies at leaf 300 of 400 proves nothing about the 299 that had
    ///    already passed. That has happened twice on this project: Unity writes its
    ///    result XML only when a run reaches its end, so a killed worker leaves no
    ///    evidence whatsoever.
    ///
    /// Everything -runTests would have done for us is done here instead: the result XML
    /// is written in the shape ResultsWriter.cs writes it, and the exit codes are
    /// Executer.ReturnCodes (0 ok, 2 failed, 3 run error) so the Python side does not
    /// need to know which driver produced them.
    /// </summary>
    public static class NebulaGateWorker
    {
        private const string PlanVariable = "NEBULA_GATE_PLAN";
        private const string PlanArgument = "-nebulaWorkerPlan";

        public static void Run()
        {
            string path = null;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == PlanArgument)
                {
                    path = args[i + 1];
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                path = Environment.GetEnvironmentVariable(PlanVariable);
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError("NEBULA GATE FAILED — no worker plan at '" + (path ?? "<unset>") + "'");
                EditorApplication.Exit(3);
                return;
            }

            NebulaGateWorkerPlan plan;
            try
            {
                plan = JsonUtility.FromJson<NebulaGateWorkerPlan>(File.ReadAllText(path));
            }
            catch (Exception error)
            {
                Debug.LogError("NEBULA GATE FAILED — worker plan is not readable: " + error);
                EditorApplication.Exit(3);
                return;
            }

            if (plan == null || plan.testNames == null || plan.testNames.Length == 0)
            {
                Debug.LogError("NEBULA GATE FAILED — worker plan names no tests");
                EditorApplication.Exit(3);
                return;
            }

            // A name beginning with '!' is read by RuntimeTestRunnerFilter.AddFilters as
            // an EXCLUSION, so one such leaf would silently subtract itself and every
            // other match from the run. The planner refuses these too; this second check
            // is here because this is the process that would be wrong.
            foreach (var name in plan.testNames)
            {
                if (string.IsNullOrEmpty(name) || name[0] == '!')
                {
                    Debug.LogError("NEBULA GATE FAILED — unusable test name '" + name + "'");
                    EditorApplication.Exit(3);
                    return;
                }
            }

            var state = NebulaGateRunState.instance;
            state.active = true;
            state.worker = plan.worker;
            state.runId = plan.runId;
            state.lane = plan.lane;
            state.sourceSha256 = plan.sourceSha256;
            state.runtimeInputSha256 = plan.runtimeInputSha256;
            state.resultPath = plan.resultPath;
            state.journalPath = plan.journalPath;
            state.expectedLeaves = plan.testNames.Length;

            NebulaGateBootstrap.Install();

            var filter = new Filter
            {
                testMode = TestMode.PlayMode,
                testNames = plan.testNames,
            };
            if (!string.IsNullOrEmpty(plan.assembly))
            {
                filter.assemblyNames = new[] { plan.assembly };
            }

            try
            {
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.Execute(new ExecutionSettings(filter));
            }
            catch (Exception error)
            {
                Debug.LogError("NEBULA GATE FAILED — Execute threw: " + error);
                NebulaGateBootstrap.Callbacks().Note("run-error", error.ToString());
                state.active = false;
                EditorApplication.Exit(3);
            }
        }
    }

    [Serializable]
    internal class NebulaGateWorkerPlan
    {
        public int worker;
        public string runId;
        public string lane;
        public string assembly;
        public string sourceSha256;

        /// <summary>
        /// The second half of the run's identity: scenes, prefabs, TMP assets,
        /// sprites, materials, shaders, ProjectSettings and the package manifest.
        /// Echoed into every journal line and into the result XML for the same
        /// reason sourceSha256 is -- so evidence carries the identity of the tree
        /// that produced it and cannot be merged with evidence from another one.
        /// A font atlas rebuild leaves sourceSha256 untouched.
        /// </summary>
        public string runtimeInputSha256;
        public string resultPath;
        public string journalPath;
        public string[] testNames;
    }

    /// <summary>
    /// The run's identity, carried across domain reloads.
    ///
    /// This is not optional bookkeeping. Callbacks registered with
    /// TestRunnerApi.RegisterCallbacks live in CallbacksHolder.m_Callbacks, a plain
    /// List of interface references on a ScriptableSingleton with no [SerializeField]
    /// on it (CallbacksHolder.cs:9) -- so every registration is lost the moment the
    /// domain reloads, and entering Play Mode reloads the domain. The test framework
    /// solves this by re-registering from [InitializeOnLoad] against state kept in a
    /// ScriptableSingleton (TestStarter.cs:31, RunData.cs); anything else means the
    /// journal stops recording at exactly the point the tests start running.
    ///
    /// Save() is deliberately never called. It is protected on ScriptableSingleton and
    /// needs a [FilePath] to mean anything; survival here comes from the same place
    /// RunData's does -- a HideAndDontSave ScriptableObject is carried through an
    /// assembly reload by the Editor itself.
    /// </summary>
    internal class NebulaGateRunState : ScriptableSingleton<NebulaGateRunState>
    {
        public bool active;
        public int worker;
        public string runId;
        public string lane;
        public string sourceSha256;
        public string runtimeInputSha256;
        public string resultPath;
        public string journalPath;
        public int expectedLeaves;
    }

    [InitializeOnLoad]
    internal static class NebulaGateBootstrap
    {
        private static NebulaGateCallbacks s_Installed;

        static NebulaGateBootstrap()
        {
            if (NebulaGateRunState.instance.active)
            {
                Install();
            }
        }

        internal static NebulaGateCallbacks Callbacks()
        {
            Install();
            return s_Installed;
        }

        internal static void Install()
        {
            // Once per domain. Registering twice would double every journal line, and a
            // duplicated leaf in the evidence is indistinguishable from a leaf that
            // genuinely ran on two workers -- a distinction the aggregate has to keep.
            if (s_Installed != null)
            {
                return;
            }

            var state = NebulaGateRunState.instance;
            s_Installed = ScriptableObject.CreateInstance<NebulaGateCallbacks>();
            s_Installed.hideFlags = HideFlags.HideAndDontSave;
            s_Installed.Configure(state.worker, state.runId, state.sourceSha256,
                state.runtimeInputSha256, state.resultPath, state.journalPath,
                state.expectedLeaves);
            ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(s_Installed, -20);
        }
    }

    /// <summary>
    /// The crash-survivable evidence, and the result file.
    ///
    /// One JSON line per finished leaf, flushed and pushed to disk before the callback
    /// returns. That ordering is the whole point: if the Editor dies on the next leaf,
    /// everything already written is provable, and the gate resumes only the leaves
    /// with no line. A buffered writer would lose the last few hundred lines on a
    /// crash, which is as good as losing all of them -- the resume plan would re-run
    /// work that had already passed and the aggregate could not tell which evidence
    /// was real.
    /// </summary>
    internal class NebulaGateCallbacks : ScriptableObject, IErrorCallbacks
    {
        [SerializeField] private int m_Worker;
        [SerializeField] private string m_RunId;
        [SerializeField] private string m_SourceSha256;
        [SerializeField] private string m_RuntimeInputSha256;
        [SerializeField] private string m_ResultPath;
        [SerializeField] private string m_JournalPath;
        [SerializeField] private int m_ExpectedLeaves;
        [SerializeField] private int m_Sequence = -1;

        internal void Configure(int worker, string runId, string sourceSha256,
            string runtimeInputSha256, string resultPath, string journalPath,
            int expectedLeaves)
        {
            m_Worker = worker;
            m_RunId = runId;
            m_SourceSha256 = sourceSha256;
            m_RuntimeInputSha256 = runtimeInputSha256;
            m_ResultPath = resultPath;
            m_JournalPath = journalPath;
            m_ExpectedLeaves = expectedLeaves;
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            var loaded = testsToRun == null ? -1 : testsToRun.TestCaseCount;
            Note("run-started", "loaded=" + loaded.ToString(CultureInfo.InvariantCulture)
                                + " expected=" + m_ExpectedLeaves.ToString(CultureInfo.InvariantCulture));
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result == null || result.Test == null || result.Test.IsSuite)
            {
                return;
            }

            var line = new StringBuilder();
            line.Append('{');
            NebulaGateJson.Field(line, "kind", "leaf");
            NebulaGateJson.Field(line, "worker", m_Worker);
            NebulaGateJson.Field(line, "runId", m_RunId);
            NebulaGateJson.Field(line, "sourceSha256", m_SourceSha256);
            NebulaGateJson.Field(line, "runtimeInputSha256", m_RuntimeInputSha256);
            NebulaGateJson.Field(line, "pid", CurrentProcessId());
            NebulaGateJson.Field(line, "sequence", NextSequence());
            NebulaGateJson.Field(line, "fullName", result.Test.FullName);
            NebulaGateJson.Field(line, "uniqueName", result.Test.UniqueName);
            NebulaGateJson.Field(line, "selectionName",
                NebulaTestManifest.SelectionName(result.Test.FullName, result.Test.ChildIndex));
            NebulaGateJson.Field(line, "start", result.StartTime.ToString("o", CultureInfo.InvariantCulture));
            NebulaGateJson.Field(line, "end", result.EndTime.ToString("o", CultureInfo.InvariantCulture));
            NebulaGateJson.Field(line, "duration", result.Duration);
            NebulaGateJson.Field(line, "result", result.TestStatus.ToString());
            NebulaGateJson.Field(line, "resultState", result.ResultState);
            NebulaGateJson.Field(line, "message", Trim(result.Message), false);
            line.Append('}');
            Append(line.ToString());
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            var failed = false;
            try
            {
                if (result != null)
                {
                    failed = result.FailCount > 0 || result.InconclusiveCount > 0;
                    WriteResult(result);
                }

                Note("run-finished", result == null
                    ? "no result"
                    : string.Format(CultureInfo.InvariantCulture,
                        "passed={0} failed={1} skipped={2} inconclusive={3}",
                        result.PassCount, result.FailCount, result.SkipCount, result.InconclusiveCount));
            }
            catch (Exception error)
            {
                Debug.LogError("NEBULA GATE — writing the result failed: " + error);
                Note("run-error", "result write failed: " + error);
                NebulaGateRunState.instance.active = false;
                EditorApplication.Exit(3);
                return;
            }

            NebulaGateRunState.instance.active = false;
            EditorApplication.Exit(failed ? 2 : 0);
        }

        public void OnError(string message)
        {
            // A prebuild-setup failure or a build error never reaches RunFinished, so
            // without this the worker would sit in batchmode until the outer watchdog
            // killed it, and the kill would then be reported as a hang rather than as
            // the setup error it actually is.
            Note("run-error", message);
            NebulaGateRunState.instance.active = false;
            EditorApplication.Exit(3);
        }

        internal void Note(string kind, string message)
        {
            var line = new StringBuilder();
            line.Append('{');
            NebulaGateJson.Field(line, "kind", kind);
            NebulaGateJson.Field(line, "worker", m_Worker);
            NebulaGateJson.Field(line, "runId", m_RunId);
            NebulaGateJson.Field(line, "sourceSha256", m_SourceSha256);
            NebulaGateJson.Field(line, "runtimeInputSha256", m_RuntimeInputSha256);
            NebulaGateJson.Field(line, "pid", CurrentProcessId());
            NebulaGateJson.Field(line, "sequence", NextSequence());
            NebulaGateJson.Field(line, "utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            NebulaGateJson.Field(line, "message", Trim(message), false);
            line.Append('}');
            Append(line.ToString());
        }

        private static long CurrentProcessId()
        {
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static string Trim(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length <= 4000 ? value : value.Substring(0, 4000);
        }

        private int NextSequence()
        {
            if (m_Sequence < 0)
            {
                // Recovered from the file rather than reset to zero. The domain reloads
                // at least twice per PlayMode run and this object is rebuilt each time;
                // a sequence that restarted at 0 would make the journal's ordering
                // useless for telling a resumed run from a duplicated one.
                m_Sequence = 0;
                try
                {
                    if (!string.IsNullOrEmpty(m_JournalPath) && File.Exists(m_JournalPath))
                    {
                        foreach (var line in File.ReadAllLines(m_JournalPath))
                        {
                            if (line.Length > 0)
                            {
                                m_Sequence++;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    m_Sequence = 0;
                }
            }

            return m_Sequence++;
        }

        private void Append(string line)
        {
            if (string.IsNullOrEmpty(m_JournalPath))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(m_JournalPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var bytes = new UTF8Encoding(false).GetBytes(line + "\n");
                using (var stream = new FileStream(m_JournalPath, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite, 1, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    // Flush(true) pushes through the OS cache, not just the managed
                    // buffer. Without it the "evidence" is a page in the kernel that a
                    // crashing process never gets to write out.
                    stream.Flush(true);
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("NEBULA GATE — journal append failed: " + error.Message);
            }
        }

        /// <summary>
        /// The same document ResultsWriter.cs produces for -testResults, so every
        /// existing reader of this project's XML keeps working. Reproduced rather than
        /// called, because ResultsWriter is internal to the package.
        /// </summary>
        private void WriteResult(ITestResultAdaptor result)
        {
            if (string.IsNullOrEmpty(m_ResultPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(m_ResultPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
            var run = new TNode("test-run");
            run.AddAttribute("id", "2");
            run.AddAttribute("testcasecount", total.ToString(CultureInfo.InvariantCulture));
            run.AddAttribute("result", result.ResultState);
            run.AddAttribute("total", total.ToString(CultureInfo.InvariantCulture));
            run.AddAttribute("passed", result.PassCount.ToString(CultureInfo.InvariantCulture));
            run.AddAttribute("failed", result.FailCount.ToString(CultureInfo.InvariantCulture));
            run.AddAttribute("inconclusive", result.InconclusiveCount.ToString(CultureInfo.InvariantCulture));
            run.AddAttribute("skipped", result.SkipCount.ToString(CultureInfo.InvariantCulture));
            run.AddAttribute("asserts", result.AssertCount.ToString(CultureInfo.InvariantCulture));
            run.AddAttribute("engine-version", "3.5.0.0");
            run.AddAttribute("clr-version", Environment.Version.ToString());
            run.AddAttribute("start-time", result.StartTime.ToString("u", CultureInfo.InvariantCulture));
            run.AddAttribute("end-time", result.EndTime.ToString("u", CultureInfo.InvariantCulture));
            run.AddAttribute("duration", result.Duration.ToString("R", CultureInfo.InvariantCulture));
            run.AddAttribute("nebula-worker", m_Worker.ToString(CultureInfo.InvariantCulture));
            run.AddAttribute("nebula-run-id", m_RunId ?? string.Empty);
            run.AddAttribute("nebula-source-sha256", m_SourceSha256 ?? string.Empty);
            run.AddAttribute("nebula-runtime-input-sha256", m_RuntimeInputSha256 ?? string.Empty);
            run.ChildNodes.Add(result.ToXml());

            // Written beside the target and moved into place. The aggregate refuses any
            // XML whose mtime predates the worker's start; a file that appears at the
            // final path only when it is complete additionally cannot be read
            // half-written by a reader that arrives between two flushes.
            var temporary = m_ResultPath + ".partial";
            var settings = new XmlWriterSettings { Indent = true, NewLineOnAttributes = false };
            using (var text = File.CreateText(temporary))
            using (var xml = XmlWriter.Create(text, settings))
            {
                run.WriteTo(xml);
            }

            if (File.Exists(m_ResultPath))
            {
                File.Delete(m_ResultPath);
            }

            File.Move(temporary, m_ResultPath);
        }
    }
}
