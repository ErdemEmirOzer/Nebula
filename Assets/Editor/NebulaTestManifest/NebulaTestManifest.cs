using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Nebula.EditorTools.TestGate
{
    /// <summary>
    /// Emits the real leaf-test list, as the test framework itself sees it.
    ///
    ///   Unity -batchmode -nographics -projectPath . -logFile - \
    ///     -executeMethod Nebula.EditorTools.TestGate.NebulaTestManifest.Emit
    ///
    ///   NEBULA_TEST_MANIFEST_PATH   where to write (also -nebulaManifestPath &lt;path&gt;)
    ///   NEBULA_TEST_MANIFEST_MODES  playmode | editmode | both   (default playmode)
    ///   NEBULA_TEST_MANIFEST_SOURCE_HASH
    ///                               echoed verbatim into the manifest so the planner
    ///                               can prove which source tree produced this list
    ///   NEBULA_TEST_MANIFEST_RUNTIME_HASH
    ///                               likewise, for the assets. `playmode_gate.py
    ///                               runtime-hash`. Mandatory: the planner refuses a
    ///                               manifest without it, because a leaf list tied to
    ///                               the C# alone says nothing about the scenes,
    ///                               prefabs, font atlases or ProjectSettings the same
    ///                               leaves will read.
    ///
    /// Why this exists at all: every previous split of this suite was planned from a
    /// regex over the .cs files. A regex cannot see what NUnit will actually run. It
    /// reads one [TestCase]-decorated method as one test when the attribute expands it
    /// into five leaves, it cannot expand [ValueSource] or [TestCaseSource] at all
    /// because the values do not exist until the assembly is loaded, and it cannot see
    /// a category a fixture inherits from its base class. A plan built on those numbers
    /// is wrong before Unity starts, and the shortfall shows up as "missing tests" in
    /// the aggregate with no way to tell a planning error from a lost worker.
    ///
    /// The two identifiers below are not interchangeable, and confusing them is the
    /// specific defect this file is shaped to prevent:
    ///
    ///   fullName       ITestAdaptor.FullName. Unique. For a leaf that shares its NUnit
    ///                  full name with a sibling (generated [TestCaseSource] cases),
    ///                  TestAdaptor.cs:25 injects " GeneratedTestCaseN" into it.
    ///   selectionName  what a filter actually matches. FullNameFilter.Match compares
    ///                  against ITest.GetFullNameWithoutDllPath(), i.e. the RAW NUnit
    ///                  full name with no such suffix. Naming a leaf by its fullName in
    ///                  Filter.testNames therefore selects NOTHING when the suffix is
    ///                  present, and naming it by its selectionName selects ALL of its
    ///                  same-named siblings.
    ///
    /// So both are emitted, plus childIndex, and the planner refuses to split a set of
    /// leaves that share a selectionName across workers.
    /// </summary>
    public static class NebulaTestManifest
    {
        private const string PathVariable = "NEBULA_TEST_MANIFEST_PATH";
        private const string ModesVariable = "NEBULA_TEST_MANIFEST_MODES";
        private const string SourceHashVariable = "NEBULA_TEST_MANIFEST_SOURCE_HASH";
        private const string RuntimeHashVariable = "NEBULA_TEST_MANIFEST_RUNTIME_HASH";
        private const string PathArgument = "-nebulaManifestPath";

        // Retrieval walks every test assembly in the project and builds the NUnit tree.
        // On this checkout that is tens of seconds, not minutes. The cap exists so a
        // retrieval that never calls back fails the batch instead of holding the
        // Editor open until the outer watchdog kills it -- a killed Editor writes
        // nothing at all, and then there is no manifest and no explanation.
        private const double RetrieveTimeoutSeconds = 900.0;

        private static readonly Dictionary<string, string> SourceByType = new Dictionary<string, string>();

        public static void Emit()
        {
            string path = null;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == PathArgument)
                {
                    path = args[i + 1];
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                path = Environment.GetEnvironmentVariable(PathVariable);
            }

            if (string.IsNullOrEmpty(path))
            {
                Fail("no manifest path: set " + PathVariable + " or pass " + PathArgument);
                return;
            }

            var modes = (Environment.GetEnvironmentVariable(ModesVariable) ?? "playmode").Trim().ToLowerInvariant();
            var wantPlay = modes == "playmode" || modes == "both";
            var wantEdit = modes == "editmode" || modes == "both";
            if (!wantPlay && !wantEdit)
            {
                Fail("unrecognised " + ModesVariable + " '" + modes + "'; use playmode, editmode or both");
                return;
            }

            var leaves = new List<StringBuilder>();
            var modeNames = new List<string>();
            var started = DateTime.UtcNow;
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();

            Action finish = () =>
            {
                try
                {
                    Write(path, leaves, modeNames.ToArray(), started);
                    Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                        "NEBULA MANIFEST {0} leaves -> {1}", leaves.Count, path);
                    EditorApplication.Exit(0);
                }
                catch (Exception error)
                {
                    Fail("writing the manifest failed: " + error);
                }
            };

            Action collectEdit = () =>
            {
                if (!wantEdit)
                {
                    finish();
                    return;
                }

                modeNames.Add("EditMode");
                api.RetrieveTestList(TestMode.EditMode, editRoot =>
                {
                    Collect(editRoot, leaves);
                    finish();
                });
            };

            // The retrievals are chained rather than issued together. RetrieveTestList
            // drives its work off EditorApplication.update (TestListJob.cs:24); two of
            // them in flight share the TestListCache and the second callback can arrive
            // holding the first mode's tree.
            if (wantPlay)
            {
                modeNames.Add("PlayMode");
                api.RetrieveTestList(TestMode.PlayMode, playRoot =>
                {
                    Collect(playRoot, leaves);
                    collectEdit();
                });
            }
            else
            {
                collectEdit();
            }

            EditorApplication.CallbackFunction watchdog = null;
            watchdog = () =>
            {
                if ((DateTime.UtcNow - started).TotalSeconds < RetrieveTimeoutSeconds)
                {
                    return;
                }

                EditorApplication.update -= watchdog;
                Fail("test list retrieval did not call back within "
                     + RetrieveTimeoutSeconds.ToString("F0") + "s");
            };
            EditorApplication.update += watchdog;
        }

        private static void Fail(string message)
        {
            Debug.LogError("NEBULA MANIFEST FAILED — " + message);
            EditorApplication.Exit(3);
        }

        private static void Collect(ITestAdaptor node, List<StringBuilder> into)
        {
            if (node == null)
            {
                return;
            }

            if (!node.IsSuite)
            {
                into.Add(Describe(node));
                return;
            }

            if (node.Children == null)
            {
                return;
            }

            foreach (var child in node.Children)
            {
                Collect(child, into);
            }
        }

        private static StringBuilder Describe(ITestAdaptor leaf)
        {
            var fixture = FixtureOf(leaf);
            var assembly = AssemblyOf(leaf);
            var row = new StringBuilder();
            row.Append('{');
            NebulaGateJson.Field(row, "fullName", leaf.FullName);
            NebulaGateJson.Field(row, "selectionName", SelectionName(leaf.FullName, leaf.ChildIndex));
            NebulaGateJson.Field(row, "uniqueName", leaf.UniqueName);
            NebulaGateJson.Field(row, "childIndex", leaf.ChildIndex);
            NebulaGateJson.Field(row, "fixture", fixture == null ? null : fixture.FullName);
            NebulaGateJson.Field(row, "fixtureName", fixture == null ? null : fixture.Name);
            NebulaGateJson.Field(row, "assembly", assembly);
            NebulaGateJson.Array(row, "categories", leaf.Categories);
            NebulaGateJson.Field(row, "runState", leaf.RunState.ToString());
            NebulaGateJson.Field(row, "testMode", leaf.TestMode.ToString());
            NebulaGateJson.Field(row, "skipReason", leaf.SkipReason);
            NebulaGateJson.Field(row, "source", SourceOf(fixture), false);
            row.Append('}');
            return row;
        }

        /// <summary>
        /// The exact inverse of TestExtensions.GetIndexedTestCaseName, which is what
        /// produced the fullName in the first place. Only generated duplicates carry a
        /// childIndex; everything else selects by its own full name.
        /// </summary>
        internal static string SelectionName(string fullName, int childIndex)
        {
            if (fullName == null || childIndex < 0)
            {
                return fullName;
            }

            var suffix = " GeneratedTestCase" + childIndex.ToString();
            if (fullName.EndsWith(suffix + ")", StringComparison.Ordinal))
            {
                return fullName.Substring(0, fullName.Length - suffix.Length - 1) + ")";
            }

            if (fullName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return fullName.Substring(0, fullName.Length - suffix.Length);
            }

            return fullName;
        }

        /// <summary>
        /// The fixture a leaf belongs to. Not simply Parent: a [TestCase] or
        /// [ValueSource] leaf hangs off a ParameterizedMethodSuite, which is a suite
        /// with a Method, so taking the parent gives the method name and loses the
        /// class. The first ancestor that has a type and no method is the class.
        /// </summary>
        private static ITestAdaptor FixtureOf(ITestAdaptor leaf)
        {
            var node = leaf.Parent;
            while (node != null)
            {
                if (!node.IsTestAssembly && node.TypeInfo != null && node.Method == null)
                {
                    return node;
                }

                node = node.Parent;
            }

            return null;
        }

        private static string AssemblyOf(ITestAdaptor leaf)
        {
            // Read through the type rather than the assembly node's Name. NUnit names
            // a TestAssembly node after the path it loaded, which is a Library path
            // here and not the assembly name Filter.assemblyNames matches against.
            var node = leaf;
            while (node != null)
            {
                if (node.TypeInfo != null && node.TypeInfo.Assembly != null)
                {
                    return node.TypeInfo.Assembly.GetName().Name;
                }

                node = node.Parent;
            }

            return null;
        }

        private static string SourceOf(ITestAdaptor fixture)
        {
            if (fixture == null || fixture.TypeInfo == null || fixture.TypeInfo.Type == null)
            {
                return null;
            }

            var type = fixture.TypeInfo.Type;
            var key = type.FullName ?? type.Name;
            string cached;
            if (SourceByType.TryGetValue(key, out cached))
            {
                return cached;
            }

            string found = null;
            try
            {
                // Matching on the short type name and then confirming through
                // MonoScript.GetClass(). Matching on filename alone is what produced
                // the wrong answer for RepairTests.cs, which declares three fixtures
                // and shares its name with none of them.
                foreach (var guid in AssetDatabase.FindAssets("t:MonoScript " + type.Name))
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
                    if (script != null && script.GetClass() == type)
                    {
                        found = assetPath;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                found = null;
            }

            SourceByType[key] = found;
            return found;
        }

        private static void Write(string path, List<StringBuilder> leaves, string[] modes, DateTime started)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var body = new StringBuilder();
            body.Append("{\n");
            NebulaGateJson.Str(body, "schema");
            body.Append(":");
            // /2 adds runtimeInputSha256. The version is bumped rather than the field
            // quietly appended, so a reader that requires the field can say which
            // manifests are known to carry it instead of guessing from its absence.
            NebulaGateJson.Str(body, "nebula-leaf-manifest/2");
            body.Append(",\n");
            NebulaGateJson.Str(body, "generatedUtc");
            body.Append(":");
            NebulaGateJson.Str(body, started.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            body.Append(",\n");
            NebulaGateJson.Str(body, "unityVersion");
            body.Append(":");
            NebulaGateJson.Str(body, Application.unityVersion);
            body.Append(",\n");
            NebulaGateJson.Str(body, "projectPath");
            body.Append(":");
            NebulaGateJson.Str(body, Directory.GetCurrentDirectory().Replace('\\', '/'));
            body.Append(",\n");
            NebulaGateJson.Str(body, "sourceSha256");
            body.Append(":");
            // Echoed, never computed here. The planner computes the tree hash in Python
            // and compares; a second implementation of the same digest in C# would be a
            // second thing to keep in step, and a disagreement between them would look
            // exactly like a dirty tree.
            NebulaGateJson.Str(body, Environment.GetEnvironmentVariable(SourceHashVariable));
            body.Append(",\n");
            NebulaGateJson.Str(body, "runtimeInputSha256");
            body.Append(":");
            // Echoed for the same reason as the source hash, and computed in the same
            // one place. A C# re-implementation of the walk -- which files are skipped,
            // which order they are folded in -- would be a second definition of the
            // same digest, and the day the two drifted the disagreement would present
            // itself as a dirty tree that nobody had touched.
            NebulaGateJson.Str(body, Environment.GetEnvironmentVariable(RuntimeHashVariable));
            body.Append(",\n");
            NebulaGateJson.Str(body, "modes");
            body.Append(":[");
            for (var i = 0; i < modes.Length; i++)
            {
                if (i > 0)
                {
                    body.Append(',');
                }

                NebulaGateJson.Str(body, modes[i]);
            }

            body.Append("],\n");
            NebulaGateJson.Str(body, "leafCount");
            body.Append(":");
            body.Append(leaves.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            body.Append(",\n");
            NebulaGateJson.Str(body, "leaves");
            body.Append(":[\n");
            for (var i = 0; i < leaves.Count; i++)
            {
                body.Append(leaves[i]);
                body.Append(i == leaves.Count - 1 ? "\n" : ",\n");
            }

            body.Append("]\n}\n");

            // Written to a sibling and moved into place. A reader that opens the
            // manifest while it is still being written gets a truncated file that
            // parses as far as it goes, and half a leaf list is the one input that
            // makes the planner confidently plan the wrong suite.
            var temporary = path + ".partial";
            File.WriteAllText(temporary, body.ToString(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }
    }
}
