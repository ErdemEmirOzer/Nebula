using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Nebula.EditorTools
{
    /// <summary>
    /// VS-017A.1 §3: the project's only build authority. Every player this repository
    /// produces comes out of here, so that "what was in that build" is a question with
    /// one answer instead of one answer per person who ran a build.
    /// Output goes under Builds/ (gitignored, see .gitignore), never under Assets/.
    /// <para>
    /// <b>Four entry points, one pipeline.</b> The two Standalone ones
    /// (<see cref="BuildDevelopmentStandalone"/>, <see cref="BuildReleaseStandalone"/>)
    /// differ in exactly one bit — <see cref="BuildOptions.Development"/> — and that bit
    /// is the whole reason the release one exists: it is what sets
    /// <c>Debug.isDebugBuild</c>, the property every diagnostic-overlay gate in the
    /// runtime branches on. They resolve whichever desktop target this Editor can build,
    /// which is what makes them usable on a machine with no Windows module.
    /// </para>
    /// <para>
    /// The two named-platform ones (<see cref="BuildReleaseMacOS"/>,
    /// <see cref="BuildReleaseWindows"/>) are what a distribution is cut from. They name
    /// their target instead of resolving it, and they <b>fail</b> when that target is not
    /// installed rather than falling back — because a fallback is only honest when the
    /// caller did not say which platform they wanted, and these callers did. A shipped
    /// "Windows package" containing a macOS bundle is the exact outcome the refusal
    /// exists to make impossible.
    /// </para>
    /// <para>
    /// Neither pair edits the project's committed build settings for the option that
    /// distinguishes them: <c>BuildOptions</c> is passed per build, so running one entry
    /// point never changes what another produces. The player-connection settings in
    /// <see cref="HardenForRelease"/> are the exception and are deliberate — they live in
    /// <c>EditorUserBuildSettings</c>, which is per-machine and not committed, and
    /// leaving them to whatever state a developer last left the Build dialog in is how a
    /// profiler-autoconnecting "release" build gets shipped.
    /// </para>
    /// </summary>
    public static class NebulaBuildAutomation
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>
        /// Every scene enabled in Build Settings, in their authored order.
        /// <para>
        /// Not <see cref="ScenePath"/> alone, and the difference shipped a broken tutorial.
        /// <c>BuildPlayerOptions.scenes</c> <b>replaces</b> the Build Settings list rather
        /// than adding to it, so passing one path silently dropped
        /// <c>Assets/Scenes/NebulaTutorial.unity</c> — which is enabled, and which
        /// <c>TutorialLauncher</c> loads additively <i>by name</i> at runtime. Every player
        /// built so far contains a single <c>level0</c>, so starting the tutorial from the
        /// main menu could only ever log "'NebulaTutorial' could not be loaded. It must be
        /// added to the build settings" and fail the run.
        /// </para>
        /// <para>
        /// Read from <c>EditorBuildSettings</c> so the build and the project agree by
        /// construction. A scene that is disabled there is deliberately excluded and stays
        /// excluded; a build with no enabled scene at all falls back to the main scene
        /// rather than producing a player with nothing in it.
        /// </para>
        /// </summary>
        private static string[] EnabledScenes()
        {
            var paths = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled && !string.IsNullOrEmpty(scene.path)) paths.Add(scene.path);

            if (paths.Count == 0)
            {
                Debug.LogWarning("[Nebula] No scene is enabled in Build Settings; falling back to " + ScenePath);
                paths.Add(ScenePath);
            }
            return paths.ToArray();
        }
        private const string BuildRootDir = "Builds/DevelopmentStandalone";
        private const string ReleaseBuildRootDir = "Builds/ReleaseStandalone";
        public const string ResultJsonPath = "TestResults/build-result.json";
        public const string ReleaseResultJsonPath = "TestResults/build-result-release.json";

        /// <summary>
        /// Where a distribution build lands. Deterministic and platform-named: the
        /// packager, the smoke scripts and the release documentation all spell these
        /// paths, so they are derived from one constant rather than agreed by convention.
        /// </summary>
        public const string ReleaseRoot = "Builds/Release";

        public const string MacOSReleaseDir = ReleaseRoot + "/macOS";
        public const string WindowsReleaseDir = ReleaseRoot + "/Windows";

        /// <summary>
        /// Reports live beside the platform folders, never inside them. Inside, they
        /// would be swept into the package as payload — a Windows player folder is
        /// archived whole.
        /// </summary>
        public const string ReleaseReportDir = ReleaseRoot + "/Reports";

        [Serializable]
        public class BuildResultReport
        {
            public bool succeeded;
            public string buildTarget;
            /// <summary>Which side of <c>Debug.isDebugBuild</c> the produced player will take.</summary>
            public bool developmentBuild;
            public string platformNote;
            public string outputPath;
            public long totalSizeBytes;
            public double buildTimeSeconds;
            public string unityVersion;
            public string[] errors = Array.Empty<string>();

            // --- distribution identity, recorded from PlayerSettings at build time ----
            // Not restated constants: these are read out of the same PlayerSettings the
            // engine bakes into the player, so the report describes the build that was
            // made rather than the build somebody intended to make.
            public string productName;
            public string companyName;
            public string bundleIdentifier;
            public string displayVersion;
            public string buildNumber;
            public string[] scenes = Array.Empty<string>();
            public string architecture;
            public string buildOptions;

            /// <summary>Each release hardening step and whether it actually took effect.</summary>
            public string[] hardening = Array.Empty<string>();
        }

        [MenuItem("Nebula/Build Development Standalone")]
        public static void BuildDevelopmentStandalone() =>
            Build(BuildOptions.Development, BuildRootDir, ResultJsonPath, "Development Standalone");

        /// <summary>
        /// The same player with <see cref="BuildOptions.Development"/> withheld — the only
        /// configuration in which <c>Debug.isDebugBuild</c> is false.
        /// <para>
        /// Every gate that keeps a diagnostic overlay off a player's screen is a runtime
        /// branch on that one property, and the Editor, the test runners and the
        /// Development player all take the <b>same</b> side of it. So nothing we ran ever
        /// executed the release side: the guard tests could only read the source and
        /// confirm the branch was written, never that it behaves. This entry point exists
        /// so that branch can be run on a real player rather than argued about.
        /// </para>
        /// <para>
        /// Deliberately a sibling of the Development method and not a flag on it: the two
        /// write to different roots and different result files, so a release build can
        /// never overwrite the Development player the smoke depends on, and neither run
        /// can be mistaken for the other after the fact.
        /// </para>
        /// </summary>
        [MenuItem("Nebula/Build Release (non-Development) Standalone")]
        public static void BuildReleaseStandalone() =>
            Build(BuildOptions.None, ReleaseBuildRootDir, ReleaseResultJsonPath, "Release Standalone");

        /// <summary>
        /// The macOS distribution build: Universal (x86_64 + arm64), non-Development.
        /// <para>
        /// Universal rather than host-architecture because the machine that builds this
        /// is one Mac and the machines that run it are not. An arm64-only bundle refuses
        /// to launch on an Intel Mac with a message about the app being damaged, which
        /// reads to the player as a broken download rather than an unsupported CPU.
        /// </para>
        /// </summary>
        [MenuItem("Nebula/Build Release macOS (Universal)")]
        public static void BuildReleaseMacOS() =>
            BuildRelease(BuildTarget.StandaloneOSX, MacOSReleaseDir, ".app", "macos");

        /// <summary>
        /// The Windows distribution build: x86_64, non-Development.
        /// <para>
        /// Its own method rather than a parameter on the macOS one so that each names a
        /// single target and a single output folder. The shared work — options, hardening,
        /// identity, the report — is <see cref="BuildRelease"/>, which both call, so the
        /// two players differ only in the ways the two platforms genuinely differ.
        /// </para>
        /// </summary>
        [MenuItem("Nebula/Build Release Windows (x86_64)")]
        public static void BuildReleaseWindows() =>
            BuildRelease(BuildTarget.StandaloneWindows64, WindowsReleaseDir, ".exe", "windows");

        /// <summary>
        /// The shared release pipeline. Refuses an uninstalled target, hardens the
        /// per-machine build settings, builds, and writes a JSON and a Markdown report.
        /// </summary>
        private static void BuildRelease(BuildTarget target, string outputDir, string extension,
            string reportStem)
        {
            string jsonPath = Path.Combine(ReleaseReportDir, "build-report-" + reportStem + ".json");
            string markdownPath = Path.Combine(ReleaseReportDir, "build-report-" + reportStem + ".md");

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            {
                // Recorded, then thrown. A caller that only reads the exit code and a
                // caller that only reads the report must reach the same conclusion --
                // "this platform was not built" -- because a missing report is
                // indistinguishable from a report nobody generated yet.
                var refusal = new BuildResultReport
                {
                    succeeded = false,
                    buildTarget = target.ToString(),
                    developmentBuild = false,
                    platformNote = target + " is not installed in this Editor. This entry point " +
                                   "names its target, so it refuses rather than substituting another " +
                                   "platform: install the matching Build Support module via Unity Hub.",
                    unityVersion = Application.unityVersion,
                    errors = new[] { "build target not supported by this Editor installation" },
                };
                WriteReports(refusal, jsonPath, markdownPath);
                throw new Exception("[Nebula] " + target + " Build Support is not installed -- see " + jsonPath);
            }

            string outputPath = Path.Combine(outputDir, PlayerFileStem() + extension);
            Directory.CreateDirectory(outputDir);

            List<string> hardening = HardenForRelease(target);

            var options = new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = outputPath,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            bool succeeded = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;

            if (succeeded && target == BuildTarget.StandaloneOSX)
                hardening.Add(StampDisplayName(outputPath));
            // Symmetrical to the macOS display-name stamp, and for the same reason: Unity
            // leaves an identity field empty on a platform it cannot fill in from this
            // host, and only a post-process on the build we just made can state it. See
            // NebulaWindowsVersionInfo for why the Windows player shipped carrying Unity's
            // own version resource instead of the game's.
            if (succeeded && target == BuildTarget.StandaloneWindows64)
                hardening.Add(NebulaWindowsVersionInfo.Stamp(outputPath,
                    PlayerSettings.companyName, PlayerSettings.productName, WindowsFileVersion()));

            BuildResultReport result = Describe(report, target, BuildOptions.None, outputPath, succeeded);
            result.platformNote = target == BuildTarget.StandaloneOSX
                ? "macOS Standalone, Universal (x86_64 + arm64), non-Development."
                : "Windows Standalone x86_64, non-Development.";
            result.hardening = hardening.ToArray();

            WriteReports(result, jsonPath, markdownPath);

            Debug.Log($"[Nebula] Release {reportStem} build {(succeeded ? "SUCCEEDED" : "FAILED")} -> {jsonPath}");
            if (!succeeded) throw new Exception("[Nebula] Release " + reportStem + " build failed -- see " + jsonPath);
        }

        /// <summary>
        /// The four-part numeric version Windows stamps into the executable, assembled
        /// from the same two committed fields everything else already reads:
        /// <c>bundleVersion</c> (0.1.0) and the Standalone <c>buildNumber</c> (1), giving
        /// 0.1.0.1.
        /// <para>
        /// Derived rather than restated. <c>Distribution/RELEASE-CHANNEL</c> makes the rule
        /// explicit — the numbers live in ProjectSettings and nowhere else — because a
        /// version literal typed into a second file is how a manifest and an executable
        /// start disagreeing about which build a player is running.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <c>PlayerSettings.macOS.buildNumber</c> despite the Windows caller: in
        /// 2022.3 that property is the accessor for the single <c>buildNumber: Standalone</c>
        /// entry in ProjectSettings.asset, which both desktop platforms share. It is the
        /// same property <see cref="Describe"/> already reports, so the executable and the
        /// build report cannot disagree about the number.
        /// </remarks>
        private static string WindowsFileVersion()
        {
            string build = PlayerSettings.macOS.buildNumber;
            if (string.IsNullOrEmpty(build)) build = "0";
            return PlayerSettings.bundleVersion + "." + build;
        }

        /// <summary>
        /// Writes <c>CFBundleDisplayName</c> into the built bundle's Info.plist.
        /// <para>
        /// Unity does not emit this key at all. macOS falls back to
        /// <c>CFBundleName</c> when it is absent, so the app happens to read "NEBULA"
        /// in Finder either way — but only by fallback. <c>CFBundleName</c> is the
        /// short name, capped at 15 characters and meant for menus;
        /// <c>CFBundleDisplayName</c> is the one the Finder, the Dock and the
        /// localisation machinery are documented to prefer, and it is what a longer
        /// product name would need. Stating it is the difference between the displayed
        /// name being chosen and being inherited.
        /// </para>
        /// <para>
        /// A post-process on the produced bundle rather than a PlayerSettings field,
        /// because Unity offers no setter for it: written through <c>PlistBuddy</c>,
        /// which is part of macOS, and only ever on a build this method just made.
        /// Failure is reported into the build's hardening list rather than thrown —
        /// a missing display name is a cosmetic defect, and destroying an otherwise
        /// good release build over one would be the worse trade.
        /// </para>
        /// </summary>
        private static string StampDisplayName(string appPath)
        {
            string plist = Path.Combine(appPath, "Contents", "Info.plist");
            if (!File.Exists(plist)) return "CFBundleDisplayName: NOT SET (no Info.plist at " + plist + ")";

            // Set-or-add: Add fails when the key exists, Set fails when it does not.
            string name = PlayerSettings.productName;
            if (!PlistBuddy(plist, $"Set :CFBundleDisplayName {name}") &&
                !PlistBuddy(plist, $"Add :CFBundleDisplayName string {name}"))
                return "CFBundleDisplayName: NOT SET (PlistBuddy declined)";

            return "CFBundleDisplayName = " + name;
        }

        private static bool PlistBuddy(string plist, string command)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("/usr/libexec/PlistBuddy")
                {
                    Arguments = $"-c \"{command}\" \"{plist}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nebula] PlistBuddy failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Turns off everything that makes a player talk to a developer's machine, and
        /// records whether each switch actually moved.
        /// <para>
        /// <see cref="BuildOptions.Development"/> being absent is necessary and not
        /// sufficient. <c>connectProfiler</c>, <c>allowDebugging</c> and
        /// <c>waitForPlayerConnection</c> live in <c>EditorUserBuildSettings</c>, are
        /// per-machine, are not committed, and persist from whatever a developer last
        /// ticked in the Build dialog. <c>waitForPlayerConnection</c> in particular makes
        /// a player hang at startup waiting for a profiler that will never attach, which
        /// on a tester's machine is indistinguishable from a hang.
        /// </para>
        /// <para>
        /// The platform-specific switches are reached by reflection, not by a direct
        /// reference. <c>UnityEditor.OSXStandalone</c> and
        /// <c>UnityEditor.WindowsStandalone</c> ship <i>with their Build Support
        /// modules</i>: referencing either directly makes the whole Editor assembly fail
        /// to compile on an installation that lacks that module, which would break this
        /// file on exactly the machines it is supposed to explain itself to. Every lookup
        /// records what it found, so "Universal was requested" and "Universal was
        /// applied" stay distinguishable in the report.
        /// </para>
        /// </summary>
        private static List<string> HardenForRelease(BuildTarget target)
        {
            var notes = new List<string>();

            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.waitForPlayerConnection = false;
            notes.Add("EditorUserBuildSettings: development=false, connectProfiler=false, " +
                      "deepProfiling=false, allowDebugging=false, waitForPlayerConnection=false");

            if (target == BuildTarget.StandaloneOSX)
            {
                notes.Add(SetModuleProperty("UnityEditor.OSXStandalone.UserBuildSettings",
                    "architecture", "UnityEditor.Build.OSArchitecture", "x64ARM64",
                    "macOS architecture -> Universal (x86_64 + arm64)"));
                notes.Add(SetModuleProperty("UnityEditor.OSXStandalone.UserBuildSettings",
                    "createXcodeProject", null, false,
                    "macOS createXcodeProject -> false (build a player, not a project)"));
            }
            else if (target == BuildTarget.StandaloneWindows64)
            {
                notes.Add(SetModuleProperty("UnityEditor.WindowsStandalone.UserBuildSettings",
                    "copyPDBFiles", null, false,
                    "Windows copyPDBFiles -> false (no native debug symbols in the player folder)"));
            }

            return notes;
        }

        /// <summary>
        /// Sets one static property on a module-provided type, reporting in words what
        /// happened. <paramref name="enumTypeName"/> is non-null when the value has to be
        /// parsed out of an enum that also only exists inside the module.
        /// </summary>
        private static string SetModuleProperty(string typeName, string propertyName,
            string enumTypeName, object value, string label)
        {
            Type type = FindType(typeName);
            if (type == null) return label + " -- NOT APPLIED: " + typeName + " is not loaded";

            PropertyInfo property = type.GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Static);
            if (property == null || !property.CanWrite)
                return label + " -- NOT APPLIED: " + typeName + "." + propertyName + " is not a settable static property";

            try
            {
                object typed = value;
                if (enumTypeName != null)
                {
                    Type enumType = FindType(enumTypeName);
                    if (enumType == null)
                        return label + " -- NOT APPLIED: enum " + enumTypeName + " is not loaded";
                    typed = Enum.Parse(enumType, (string)value);
                }

                property.SetValue(null, typed, null);
                object readBack = property.GetValue(null, null);
                return label + " -- applied (reads back as " + readBack + ")";
            }
            catch (Exception e)
            {
                return label + " -- NOT APPLIED: " + e.Message;
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static void Build(BuildOptions buildOptions, string rootDir, string resultJsonPath, string label)
        {
            BuildTarget target = ResolveTarget(out string platformNote, out BuildTargetGroup group);
            string extension = target == BuildTarget.StandaloneWindows64 ? ".exe" : ".app";
            string outputPath = Path.Combine(rootDir, target.ToString(), PlayerFileStem() + extension);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? rootDir);

            var options = new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = outputPath,
                target = target,
                targetGroup = group,
                options = buildOptions
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            bool succeeded = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;

            BuildResultReport result = Describe(report, target, buildOptions, outputPath, succeeded);
            result.platformNote = platformNote;

            Directory.CreateDirectory(Path.GetDirectoryName(resultJsonPath) ?? "TestResults");
            File.WriteAllText(resultJsonPath, JsonUtility.ToJson(result, true));

            Debug.Log($"[Nebula] {label} build {(succeeded ? "SUCCEEDED" : "FAILED")} -> {resultJsonPath}");
            if (!succeeded) throw new Exception($"[Nebula] {label} build failed -- see " + resultJsonPath);
        }

        /// <summary>
        /// Everything a report says about a finished build, read from the build report and
        /// from PlayerSettings. One place, so the four entry points cannot describe
        /// themselves in four slightly different vocabularies.
        /// </summary>
        private static BuildResultReport Describe(BuildReport report, BuildTarget target,
            BuildOptions buildOptions, string outputPath, bool succeeded)
        {
            var result = new BuildResultReport
            {
                succeeded = succeeded,
                buildTarget = target.ToString(),
                developmentBuild = (buildOptions & BuildOptions.Development) != 0,
                outputPath = succeeded ? outputPath : "",
                totalSizeBytes = (long)report.summary.totalSize,
                buildTimeSeconds = report.summary.totalTime.TotalSeconds,
                unityVersion = Application.unityVersion,
                productName = PlayerSettings.productName,
                companyName = PlayerSettings.companyName,
                bundleIdentifier = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Standalone),
                displayVersion = PlayerSettings.bundleVersion,
                buildNumber = PlayerSettings.macOS.buildNumber,
                scenes = EnabledScenes(),
                architecture = target == BuildTarget.StandaloneOSX
                    ? DescribeMacArchitecture()
                    : "x86_64 (StandaloneWindows64)",
                buildOptions = buildOptions.ToString(),
            };

            if (!succeeded)
            {
                var errors = new List<string>();
                foreach (BuildStep step in report.steps)
                    foreach (BuildStepMessage msg in step.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception) errors.Add(msg.content);
                result.errors = errors.ToArray();
            }
            return result;
        }

        /// <summary>What the module says the architecture is, or an honest "unknown".</summary>
        private static string DescribeMacArchitecture()
        {
            Type type = FindType("UnityEditor.OSXStandalone.UserBuildSettings");
            PropertyInfo property = type?.GetProperty("architecture", BindingFlags.Public | BindingFlags.Static);
            if (property == null) return "unknown (macOS module not loaded)";
            try { return property.GetValue(null, null)?.ToString() ?? "unknown"; }
            catch (Exception e) { return "unknown (" + e.Message + ")"; }
        }

        /// <summary>
        /// JSON for a script, Markdown for a person, from the same object — so the two can
        /// never describe different builds.
        /// </summary>
        private static void WriteReports(BuildResultReport result, string jsonPath, string markdownPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath) ?? ReleaseReportDir);
            File.WriteAllText(jsonPath, JsonUtility.ToJson(result, true));

            var text = new StringBuilder();
            text.AppendLine("# Nebula build report — " + result.buildTarget);
            text.AppendLine();
            text.AppendLine("| Field | Value |");
            text.AppendLine("| --- | --- |");
            text.AppendLine("| succeeded | " + result.succeeded + " |");
            text.AppendLine("| build target | " + result.buildTarget + " |");
            text.AppendLine("| architecture | " + result.architecture + " |");
            text.AppendLine("| developmentBuild | " + result.developmentBuild + " |");
            text.AppendLine("| buildOptions | " + result.buildOptions + " |");
            text.AppendLine("| product | " + result.productName + " |");
            text.AppendLine("| company | " + result.companyName + " |");
            text.AppendLine("| bundle identifier | " + result.bundleIdentifier + " |");
            text.AppendLine("| display version | " + result.displayVersion + " |");
            text.AppendLine("| build number | " + result.buildNumber + " |");
            text.AppendLine("| scenes | " + string.Join(", ", result.scenes) + " |");
            text.AppendLine("| output | " + result.outputPath + " |");
            text.AppendLine("| total size (bytes) | " + result.totalSizeBytes + " |");
            text.AppendLine("| build time (s) | " + result.buildTimeSeconds.ToString("F1") + " |");
            text.AppendLine("| Unity | " + result.unityVersion + " |");
            text.AppendLine();
            text.AppendLine("## Release hardening");
            text.AppendLine();
            if (result.hardening.Length == 0) text.AppendLine("_not a hardened release build_");
            foreach (string note in result.hardening) text.AppendLine("- " + note);
            text.AppendLine();
            text.AppendLine("## Platform note");
            text.AppendLine();
            text.AppendLine(result.platformNote);
            if (result.errors.Length > 0)
            {
                text.AppendLine();
                text.AppendLine("## Errors");
                text.AppendLine();
                foreach (string error in result.errors) text.AppendLine("- " + error);
            }

            File.WriteAllText(markdownPath, text.ToString());
        }

        /// <summary>
        /// What the produced player is called on disk, taken from
        /// <see cref="PlayerSettings.productName"/> rather than restated here.
        /// <para>
        /// It used to be the literal <c>"Nebula"</c>, which was harmless only for as long
        /// as the product was also called Nebula. On macOS the bundle's filename <i>is</i>
        /// the name Finder and the Dock show, so a hardcoded stem means renaming the
        /// product renames it everywhere except the one place the player actually reads
        /// it. Deriving it is also what Unity's own Build dialog does, so a build made
        /// from the menu and a build made from here now agree.
        /// </para>
        /// <para>
        /// Characters that cannot appear in a filename are dropped rather than replaced:
        /// substituting them would invent a name nobody chose, and the empty case falls
        /// back to a fixed stem so a build can never be attempted against a path ending
        /// in nothing at all.
        /// </para>
        /// </summary>
        public static string PlayerFileStem()
        {
            string product = PlayerSettings.productName ?? string.Empty;
            var stem = new StringBuilder(product.Length);
            foreach (char character in product)
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), character) < 0) stem.Append(character);

            string cleaned = stem.ToString().Trim();
            return cleaned.Length == 0 ? "Player" : cleaned;
        }

        /// <summary>Windows x86_64 if the module is actually installed in this Editor; otherwise an honest macOS fallback -- never a silent platform substitution.</summary>
        private static BuildTarget ResolveTarget(out string note, out BuildTargetGroup group)
        {
            if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                note = "Windows x86_64 (primary target) -- Windows Build Support module is installed in this Editor.";
                group = BuildTargetGroup.Standalone;
                return BuildTarget.StandaloneWindows64;
            }
            // Says platform only. It used to say "macOS Development Standalone build",
            // which was accurate while this was the only entry point and became a lie
            // the moment a release one existed -- a release result JSON that describes
            // itself as a Development build is exactly the sentence somebody would
            // later quote as evidence. Which configuration was produced is reported
            // separately, by BuildResultReport.developmentBuild, from the build options
            // actually passed.
            note = "Windows Build Support module is NOT installed in this Editor -- falling back to a macOS Standalone build. " +
                   "This is a real, honest fallback, not the primary target: install the Windows module to build the primary Windows x86_64 target.";
            group = BuildTargetGroup.Standalone;
            return BuildTarget.StandaloneOSX;
        }
    }
}
