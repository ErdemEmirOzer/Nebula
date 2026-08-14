using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Nebula.Sectors;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 2 / AJAN 2 — the P2-LIFE-012 bisection ladder, and
    /// nothing else. Every leaf here is <c>[Category("Diagnostic")]</c>: it was
    /// written to bisect a specific past incident, it costs minutes rather than
    /// seconds, and it is not a statement about what the shipped game must do.
    /// <para>
    /// These lived in <see cref="AuditPart2LifecyclePlayModeTests"/> until the
    /// combined fixture became unrunnable — 31 tests, several carrying a
    /// twenty-minute <c>Timeout</c> of their own, measured at 174-198s each.
    /// Nothing here has been shortened. A probe's cycle budget IS its measurement:
    /// sixty cycles was already wrong once (a green at 174.1s against a collapse at
    /// 178.6s, i.e. a run that stopped four seconds before the thing it existed to
    /// detect), and trimming a probe to fit a gate would repeat that mistake on
    /// purpose. They are separated by category instead, which costs them nothing.
    /// </para>
    /// <para>
    /// Established by measurement, not by argument:
    /// </para>
    /// <list type="bullet">
    /// <item>the session's roots stop dying at cycle 46, deterministically, in three
    /// separate runs;</item>
    /// <item>an in-situ probe — a plain GameObject created and destroyed in a frame
    /// that contains no teardown at all — stops dying at the SAME cycle, so the
    /// failure is process-wide, not an ownership or teardown-order defect;</item>
    /// <item>a product-independent probe survives 100 cycles, 1,056,000 objects,
    /// 1,845,267 frames and 213 seconds with the Nebula scene loaded, so cycle count,
    /// object count, frame count, wall time and the scene are all excluded;</item>
    /// <item>no Error/Exception/Assert is logged in any cycle, and nothing under
    /// Assets/Scripts touches the PlayerLoop.</item>
    /// </list>
    /// <para>
    /// What remains is: something a Nebula SESSION accumulates disables Unity's
    /// deferred destruction for the whole process after ~45 repetitions. The probes
    /// below halve that space. Each one runs strictly less than a session and then
    /// asks the one question that matters — "does a plain GameObject destroyed in a
    /// quiet frame still die?". The cheapest probe that still collapses owns the
    /// defect; the most expensive one that stays green exonerates everything inside
    /// it.
    /// </para>
    /// <para>
    /// Runs of this fixture must raise the runner's own watchdog to match:
    /// <c>NEBULA_UNITY_TEST_TIMEOUT_SECONDS=1800 ./UIDesign/runscoped.sh playmode …</c>
    /// </para>
    /// </summary>
    public class AuditPart2LifecycleDiagnosticTests : LifecycleAuditFixture
    {
        /// <summary>
        /// The root-naming diagnostic answers "what survives, and from which cycle".
        /// Fifty is deliberate, not arbitrary: teardown is complete and correct for
        /// the first forty-five consecutive campaigns and stops working at the
        /// forty-sixth, so a shorter run reports a clean bill of health.
        /// <para><c>NEBULA_DIAGNOSTIC_CYCLES</c> overrides it for a single run.</para>
        /// </summary>
        private static readonly int RootDiagnosticCycles = CycleBudget("NEBULA_DIAGNOSTIC_CYCLES", 50);

        /// <summary>
        /// Every bisection probe's cycle budget. Sixty was wrong and the runs say so:
        /// <c>DeferredDestruction_SurvivesSixtyFullSessionCycles</c> completed sixty
        /// real BeginSession/EndSession cycles GREEN in 174.1s, while the root
        /// diagnostic collapses at cycle 47 / 178.6s. Sixty is past the collapsing
        /// CYCLE and four seconds short of the collapsing SECOND, so the anchor probe
        /// was stopping just before the thing it exists to detect — a green that
        /// measured nothing. A hundred and fifty clears both readings by a wide
        /// margin at every plausible per-cycle cost.
        /// <para>
        /// Overridable per run with <c>NEBULA_COLLAPSE_CYCLES</c> so one probe can be
        /// pushed further without a recompile — recompiling between runs is what
        /// <c>runscoped.sh</c>'s source hash exists to refuse.
        /// </para>
        /// </summary>
        private static readonly int CollapseProbeCycles = CycleBudget("NEBULA_COLLAPSE_CYCLES", 150);

        /// <summary>
        /// The elapsed-time budget for the probes that are gated on the clock rather
        /// than on a cycle count. Three hundred seconds is comfortably past the 178.6s
        /// at which the product collapses and past the 213.2s a session-free control
        /// reached green, so a green here is a real exoneration rather than an
        /// experiment that stopped too early. <c>NEBULA_COLLAPSE_SECONDS</c> overrides.
        /// </summary>
        private static readonly float CollapseProbeSeconds = CycleBudget("NEBULA_COLLAPSE_SECONDS", 300);

        /// <summary>
        /// How often a probe pays for the object-table census. Every cycle carries the
        /// cheap line (clock, frame, memory); the census — which is itself a
        /// <c>FindObjectsOfType</c> sweep, i.e. one of the things under suspicion —
        /// is taken on this stride, on the first cycle and on every collapsed cycle.
        /// </summary>
        private const int CensusEveryNthCycle = 10;

        /// <summary>Per-cycle trail lines a probe writes before it falls back to the stride alone.</summary>
        private const int MaxTrailLines = 400;

        /// <summary>
        /// Wall-clock length of one padded Begin/End cycle. Twelve seconds is roughly
        /// four times what a real cycle costs, so the padded probe reaches the
        /// collapsing 178.6s in about fifteen cycles instead of about sixty — which is
        /// exactly the separation between "the clock did it" and "the repetition did
        /// it" that no run so far has been able to make.
        /// </summary>
        private const float PaddedCycleSeconds = 12f;

        /// <summary>
        /// The hierarchy the scan-churn control sweeps. A live session puts about
        /// 3,500 transforms in front of <c>SceneRoots()</c>, and this control has to
        /// cost the same per sweep or it proves nothing about the diagnostic.
        /// </summary>
        private const int ScanScaffoldObjects = 3500;

        /// <summary>
        /// Sweeps per cycle in that control. <c>EndSession_LeavesNoSceneRootBehind</c>
        /// calls <c>SceneRoots()</c> for the root count and again for the quiet-probe
        /// window on every cycle, plus the baseline and the final census outside the
        /// loop; four per cycle is the generous reading, which is the right way round
        /// for a negative control.
        /// </summary>
        private const int ScanSweepsPerCycle = 4;

        /// <summary>
        /// Cycles a probe keeps running after the first collapse. The collapse has
        /// never once healed in any measured run, so a handful of confirming cycles
        /// is all the evidence a red probe can still add — and stopping there is what
        /// keeps a red 150-cycle probe inside the runner's watchdog, since every cycle
        /// after the collapse is slower than the one before it.
        /// </summary>
        private const int CyclesToConfirmACollapse = 3;

        /// <summary>
        /// Twenty minutes for the bisection probes. NUnit's default per-test bound is
        /// 180s and the collapse under test happens at ~178s, so on the default a full
        /// session probe reports "Timeout value of 180000ms was exceeded" a couple of
        /// seconds before the thing it exists to measure — and a timeout is not a
        /// measurement. At 150 cycles the longest composition probe needs more than the
        /// ten minutes the hundred-cycle gates use.
        /// <para>
        /// This constant is also the reason this fixture cannot be in the release lane
        /// under any budget: one leaf of it is allowed to run for longer than the
        /// entire release gate.
        /// </para>
        /// </summary>
        private const int ProbeTimeoutMilliseconds = 1200000;

        /// <summary>Ten minutes for the fifty-cycle root diagnostic. The measured runs land at 182-198s.</summary>
        private const int DiagnosticTimeoutMilliseconds = 600000;

        /// <summary>
        /// Session-scale volumes, taken from the measured runs rather than invented:
        /// the material tracker counted 50,224 new material instances over a hundred
        /// campaigns (~500 per session) and the Texture(all-loaded) delta implies
        /// ~110 runtime textures per session. Each churn probe allocates and releases
        /// the same order of magnitude with no session anywhere near it.
        /// </summary>
        private const int MaterialTriplesPerCycle = 167;   // ×3 factory calls = ~501 materials
        private const int TexturePairsPerCycle = 55;       // ×2 factory calls = 110 textures
        private const int RuntimeMeshesPerCycle = 4;       // starfield, galaxy clouds, dust, halo
        private const int VerticesPerProbeMesh = 60000;

        // ================================================================
        // The root diagnostic
        // ================================================================

        /// <summary>
        /// The counter tests say <em>how much</em> survives a session; this one says
        /// <em>what</em>, <em>from which cycle</em>, and — now that the engine probe
        /// (<see cref="EngineDestroyProbePlayModeTests"/>) has shown
        /// <c>UnityEngine.Object.Destroy</c> working for over a million plain objects
        /// inside one batchmode process — <em>which of the two remaining product
        /// explanations it is</em>.
        /// <para>
        /// Three in-situ probes run alongside the real teardown. They are ordinary
        /// GameObjects the session never sees: nothing tracks them, no component of
        /// ours is attached, and they are torn down with the identical
        /// <c>SetActive(false)</c> + <c>Destroy</c> pair
        /// <c>NebulaSessionLifetime.DestroyAllReverse</c> uses. Their placement is
        /// the whole experiment:
        /// <list type="bullet">
        /// <item><b>before</b> — destroyed in the same frame as the session, queued
        /// BEFORE its ~176 roots;</item>
        /// <item><b>after</b> — same frame, queued AFTER them;</item>
        /// <item><b>quiet</b> — a later frame that contains no session teardown at all.</item>
        /// </list>
        /// All three dying while the session's own roots survive means destruction
        /// is healthy and the defect is in what the session does to its roots. Only
        /// "before" dying means the frame's destruction stopped part-way through the
        /// queue. "quiet" surviving too means destruction is broken for the whole
        /// process from that moment on.
        /// </para>
        /// <para>
        /// Two further things are recorded, because their absence was previously
        /// read as evidence. Every <c>Error</c>/<c>Exception</c>/<c>Assert</c> Unity
        /// logs is captured with the cycle it landed in: a throw inside
        /// <c>OnDisable</c>/<c>OnDestroy</c> is swallowed into the log rather than
        /// propagated to the caller, so the existing <c>beginFault=none
        /// endFault=none</c> never ruled one out. And each survivor's instance id is
        /// compared against the ids that existed immediately before teardown, which
        /// separates "received Destroy and did not die" from "was built again
        /// afterwards" — the two candidate mechanisms. Wall-clock and frame number
        /// are stamped on the break so a threshold in elapsed time can be told apart
        /// from a threshold in cycles.
        /// </para>
        /// <para>
        /// The release lane's loop-free statement of the same product invariant is
        /// <c>LifecycleOwnershipRegressionTests.EndSession_LeavesTheSceneWithExactlyTheRootsItStartedWith</c>:
        /// one cycle, no census, no probes. This one keeps the fifty because the
        /// question it answers is "at which cycle, and what does the engine look like
        /// on either side of it" — which one cycle cannot answer at all.
        /// </para>
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(DiagnosticTimeoutMilliseconds)]
        public IEnumerator EndSession_LeavesNoSceneRootBehind()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();

            var logFaults = new List<string>();
            int loggingCycle = 0;
            void CaptureLog(string condition, string stack, LogType type)
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                // Bounded: a throw inside a teardown callback repeats once per object,
                // and a thousand identical lines would push the real trail out of the XML.
                if (logFaults.Count >= 12) return;
                logFaults.Add($"\n  [{type}] cycle {loggingCycle}: {condition}"
                    + (string.IsNullOrEmpty(stack) ? "" : "\n      " + stack.Replace("\n", "\n      ").TrimEnd()));
            }

            Application.logMessageReceived += CaptureLog;
            try
            {
                adapter.BeginSession();
                yield return null;
                adapter.EndSession();
                yield return DrainDeferredTeardown();
                HashSet<string> baseline = SceneRootNames();
                int baselineRoots = 0;
                foreach (GameObject unused in SceneRoots()) baselineRoots++;

                var trail = new StringBuilder();
                int firstGrowthCycle = -1;
                int firstBeforeProbeSurvival = -1, firstAfterProbeSurvival = -1, firstQuietProbeSurvival = -1;
                // The bisection probes' clock, reused here so this run and every probe
                // report elapsed seconds and frames on the same basis. Comparing a
                // probe's green against this run's red is the whole method, and it can
                // only be done when both sides publish the same two numbers.
                var watch = new DestructionCollapseWatch();

                for (int i = 0; i < RootDiagnosticCycles; i++)
                {
                    loggingCycle = i + 1;
                    string beginFault = null, endFault = null;
                    try { adapter.BeginSession(); }
                    catch (Exception e) { beginFault = e.GetType().Name + ": " + e.Message; }
                    bool activeAfterBegin = adapter.SessionActive;

                    // Created inside the live session, destroyed on either side of its
                    // teardown, in the same frame. Nothing in the product can reach them.
                    var beforeProbe = new GameObject($"inSituProbe.before.{i + 1}");
                    var afterProbe = new GameObject($"inSituProbe.after.{i + 1}");
                    yield return null;

                    TearDownExactlyLikeATrackedRoot(beforeProbe);
                    try { adapter.EndSession(); }
                    catch (Exception e) { endFault = e.GetType().Name + ": " + e.Message; }
                    TearDownExactlyLikeATrackedRoot(afterProbe);
                    bool activeAfterEnd = adapter.SessionActive;

                    // The roots alive at the moment teardown was asked for. A survivor
                    // whose id is in here received Destroy and did not die; a survivor
                    // whose id is not was created after the teardown. The names are
                    // kept as well, because the set that DID die in the same frame is
                    // the discriminating evidence: a contiguous head of the teardown
                    // order means the frame's destruction stopped part-way, an
                    // arbitrary scatter means it did not.
                    var rootsBeforeDrain = new Dictionary<int, string>();
                    foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                        rootsBeforeDrain[root.GetInstanceID()] = root.name;
                    var idsBeforeDrain = new HashSet<int>(rootsBeforeDrain.Keys);

                    yield return DrainDeferredTeardown();

                    bool beforeSurvived = beforeProbe != null;
                    bool afterSurvived = afterProbe != null;
                    if (beforeSurvived && firstBeforeProbeSurvival < 0) firstBeforeProbeSurvival = i + 1;
                    if (afterSurvived && firstAfterProbeSurvival < 0) firstAfterProbeSurvival = i + 1;

                    int roots = 0;
                    foreach (GameObject unused in SceneRoots()) roots++;
                    if (roots > baselineRoots && firstGrowthCycle < 0)
                    {
                        firstGrowthCycle = i + 1;
                        // Distinguish "never destroyed" from "not destroyed yet": give the
                        // deferred queue an order of magnitude more frames than the normal
                        // window and see whether the survivors clear.
                        for (int extra = 0; extra < 40; extra++) yield return null;
                        int settled = 0;
                        foreach (GameObject unused in SceneRoots()) settled++;

                        // What ARE the survivors? Scene handle, hide flags and instance id
                        // separate "the objects we destroyed refused to die" from "these are
                        // different objects nobody tracked".
                        var active = SceneManager.GetActiveScene();
                        int shown = 0;
                        foreach (GameObject root in SceneRoots())
                        {
                            if (baseline.Contains(root.name) || shown++ >= 5) continue;
                            trail.Append($"\n      survivor '{root.name}' id={root.GetInstanceID()}"
                                + $" existedBeforeTeardown={idsBeforeDrain.Contains(root.GetInstanceID())}"
                                + $" scene='{root.scene.name}' handle={root.scene.handle}"
                                + $" activeHandle={active.handle} loaded={root.scene.isLoaded}"
                                + $" flags={root.hideFlags} active={root.activeInHierarchy}");
                        }
                        // Of everything that was standing when Destroy was asked for,
                        // WHICH entries actually went? Naming them is what separates
                        // "the frame's destruction stopped part-way through the queue"
                        // from "these particular objects are not reaching a Destroy".
                        var stillHere = new HashSet<int>();
                        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                            stillHere.Add(root.GetInstanceID());
                        var died = new List<string>();
                        foreach (var kv in rootsBeforeDrain)
                            if (!stillHere.Contains(kv.Key)) died.Add(kv.Value);
                        died.Sort();
                        trail.Append($"\n      of {rootsBeforeDrain.Count} roots standing when teardown was asked for, "
                            + $"{died.Count} died: "
                            + (died.Count == 0 ? "(none)"
                                : string.Join(", ", died.GetRange(0, Mathf.Min(20, died.Count)))
                                  + (died.Count > 20 ? $", … +{died.Count - 20}" : "")));
                        trail.Append($"\n  >>> cycle {i + 1}: activeAfterBegin={activeAfterBegin} activeAfterEnd={activeAfterEnd}"
                            + $" beginFault={beginFault ?? "none"} endFault={endFault ?? "none"}"
                            + $" roots@4frames={roots} roots@44frames={settled}"
                            + $" inSituProbe.before={(beforeSurvived ? "SURVIVED" : "destroyed")}"
                            + $" inSituProbe.after={(afterSurvived ? "SURVIVED" : "destroyed")}"
                            + $" {EngineGlobals(watch, census: true)}");
                    }

                    // A frame of its own, with no session teardown anywhere in it. If this
                    // one survives, nothing about the session's teardown is what stopped
                    // destruction working.
                    var quietProbe = new GameObject($"inSituProbe.quiet.{i + 1}");
                    TearDownExactlyLikeATrackedRoot(quietProbe);
                    yield return DrainDeferredTeardown();
                    if (quietProbe != null && firstQuietProbeSurvival < 0) firstQuietProbeSurvival = i + 1;

                    if (beginFault != null || endFault != null || !activeAfterBegin || activeAfterEnd)
                        trail.Append($"\n  cycle {i + 1}: activeAfterBegin={activeAfterBegin} activeAfterEnd={activeAfterEnd}"
                            + $" beginFault={beginFault ?? "none"} endFault={endFault ?? "none"}");
                    if (roots != baselineRoots) trail.Append($"\n  after cycle {i + 1}: {roots} roots (baseline {baselineRoots})");

                    // A standing sample on the stride, healthy cycles included. Without
                    // it this run reported its clock exactly once — on the cycle that
                    // broke — and there was nothing to compare a green probe against.
                    if ((i + 1) % CensusEveryNthCycle == 0)
                        trail.Append($"\n  cycle {i + 1}: roots={roots} {EngineGlobals(watch, census: true)}");
                }

                trail.Insert(0, "\n  in-situ probes (plain GameObjects the session never sees, torn down the same way):"
                    + $"\n    before session teardown, same frame: {DescribeProbe(firstBeforeProbeSurvival)}"
                    + $"\n    after  session teardown, same frame: {DescribeProbe(firstAfterProbeSurvival)}"
                    + $"\n    in a frame with no teardown at all:  {DescribeProbe(firstQuietProbeSurvival)}"
                    + (logFaults.Count == 0
                        ? "\n  no Error/Exception/Assert was logged in any cycle"
                        : $"\n  logged faults ({logFaults.Count}, capped):" + string.Concat(logFaults)));
                if (firstGrowthCycle > 0)
                    trail.Insert(0, $"\n  first cycle that left a root behind: {firstGrowthCycle}");

                var survivors = new List<string>();
                foreach (string name in SceneRootNames())
                    if (!baseline.Contains(name)) survivors.Add(name);
                survivors.Sort();

                var counts = new Dictionary<string, int>();
                foreach (GameObject root in SceneRoots())
                    counts[root.name] = counts.TryGetValue(root.name, out int c) ? c + 1 : 1;

                var report = new StringBuilder();
                foreach (string name in survivors) report.Append($"\n  {name} ×{counts[name]}");
                foreach (var kv in counts)
                    if (baseline.Contains(kv.Key) && kv.Value > 1)
                        report.Append($"\n  {kv.Key} ×{kv.Value}  (duplicated, was in baseline)");

                Assert.That(report.ToString(), Is.Empty,
                    $"{RootDiagnosticCycles} BeginSession/EndSession cycles left scene roots behind:{report}{trail}");
            }
            finally
            {
                Application.logMessageReceived -= CaptureLog;
            }
        }

        private static string DescribeProbe(int firstSurvivingCycle) =>
            firstSurvivingCycle < 0
                ? "destroyed on every cycle"
                : $"first survived at cycle {firstSurvivingCycle}";

        // ================================================================
        // P2-LIFE-012 — bisecting what a session accumulates
        // ================================================================

        /// <summary>
        /// Carries the first collapsed cycle and the engine-global trail across a
        /// probe's cycles, and — the part that lets probes be compared against each
        /// other and against the root diagnostic — the wall clock and frame number the
        /// probe's own measured stretch started at. The diagnostic collapses at
        /// <c>elapsed=178.6s frame=451</c>; a probe that reports the same two numbers
        /// on the same basis can be read directly against it, and one that reports only
        /// a cycle index cannot.
        /// </summary>
        private sealed class DestructionCollapseWatch
        {
            public int FirstCollapsedCycle = -1;
            public int CyclesRun;
            public int TrailLines;
            public readonly StringBuilder Trail = new StringBuilder();
            public readonly float StartedAt = Time.realtimeSinceStartup;
            public readonly int StartedAtFrame = Time.frameCount;

            public float Elapsed => Time.realtimeSinceStartup - StartedAt;
            public int Frames => Time.frameCount - StartedAtFrame;

            /// <summary>
            /// True once the collapse has been observed and confirmed for a few more
            /// cycles. Probes stop here rather than grinding through the remaining
            /// budget: the collapse has never healed in any measured run, and every
            /// post-collapse cycle leaves a whole session standing, so continuing only
            /// slows the run towards the runner's watchdog and adds nothing.
            /// </summary>
            public bool CollapseConfirmed =>
                FirstCollapsedCycle > 0 && CyclesRun >= FirstCollapsedCycle + CyclesToConfirmACollapse;
        }

        /// <summary>
        /// Every engine-global that can be read without changing anything.
        /// <para>
        /// Two tiers, deliberately. The cheap tier reads clocks, memory counters and
        /// scene state and touches no object table at all, so it can be taken on every
        /// single cycle without becoming part of the experiment. The census tier walks
        /// the object table by type, which is exactly the kind of
        /// <c>FindObjectsOfType</c> sweep the root diagnostic does several times per
        /// cycle and the anchor probe does not — the one measured behavioural
        /// difference between a red 47-cycle diagnostic and a green 60-cycle anchor.
        /// Instrumentation that is itself under suspicion must be applied on a known
        /// stride rather than everywhere, or the ladder cannot be read.
        /// </para>
        /// <para>
        /// Graphics memory is reported because the shutdown allocator dumps of the
        /// existing runs disagree by more than a factor of two on exactly that
        /// allocator (<c>ALLOC_GFX_MAIN</c>: 8.4 MB with the scene loaded and no
        /// session, 247 MB across sixty green sessions, 0.58 GB in the collapsed
        /// diagnostic) and nothing in-process was sampling it.
        /// </para>
        /// </summary>
        private static string EngineGlobals(DestructionCollapseWatch watch, bool census)
        {
            var sb = new StringBuilder();
            sb.Append($"elapsed={watch.Elapsed:0.0}s frames={watch.Frames}");
            sb.Append($" frame={Time.frameCount} t={Time.realtimeSinceStartup:0.0}s");
            sb.Append($" scenes={SceneManager.sceneCount} timeScale={Time.timeScale:0.###}");
            sb.Append($" maxDeltaTime={Time.maximumDeltaTime:0.###} isPlaying={Application.isPlaying}");
            sb.Append($" managedMB={GC.GetTotalMemory(false) / 1048576}");
            sb.Append($" monoMB={UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576}");
            sb.Append($" allocatedMB={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576}");
            sb.Append($" reservedMB={UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576}");
            sb.Append($" gfxDriverMB={UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576}");
            sb.Append($" textureMB={Texture.currentTextureMemory / 1048576}");
            sb.Append($" nonStreamingTextureMB={Texture.nonStreamingTextureMemory / 1048576}");
            if (census)
            {
                sb.Append($" Material={Resources.FindObjectsOfTypeAll<Material>().Length}");
                sb.Append($" Texture={Resources.FindObjectsOfTypeAll<Texture>().Length}");
                sb.Append($" RenderTexture={Resources.FindObjectsOfTypeAll<RenderTexture>().Length}");
                sb.Append($" Cubemap={Resources.FindObjectsOfTypeAll<Cubemap>().Length}");
                sb.Append($" Mesh={Resources.FindObjectsOfTypeAll<Mesh>().Length}");
                sb.Append($" Shader={Resources.FindObjectsOfTypeAll<Shader>().Length}");
                sb.Append($" Camera={Resources.FindObjectsOfTypeAll<Camera>().Length}");
                sb.Append($" allEngineObjects={Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length}");
                sb.Append($" Transform={UnityEngine.Object.FindObjectsOfType<Transform>(true).Length}");
                sb.Append($" Component={UnityEngine.Object.FindObjectsOfType<Component>(true).Length}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// The detector every probe shares. A plain GameObject, created and torn
        /// down with the identical <c>SetActive(false)</c> + <c>Destroy</c> pair the
        /// session uses, in a frame of its own. A survivor is given forty extra
        /// frames before it is called a survivor, so "not destroyed yet" can never be
        /// read as "never destroyed".
        /// <para>
        /// The cheap line is written on every cycle, so elapsed seconds and frame
        /// number are on record continuously and the last healthy reading is never
        /// more than one cycle away from the collapse. The census rides the stride,
        /// the first cycle and every collapsed cycle.
        /// </para>
        /// </summary>
        private static IEnumerator MeasureDeferredDestruction(DestructionCollapseWatch watch, int cycle)
        {
            watch.CyclesRun = cycle;
            var probe = new GameObject($"collapseProbe.{cycle}");
            probe.SetActive(false);
            UnityEngine.Object.Destroy(probe);
            yield return DrainDeferredTeardown();

            bool alive = probe != null;
            if (alive)
            {
                for (int extra = 0; extra < 40; extra++) yield return null;
                alive = probe != null;
            }

            bool onStride = alive || cycle == 1 || cycle % CensusEveryNthCycle == 0;
            if (alive && watch.FirstCollapsedCycle < 0)
            {
                watch.FirstCollapsedCycle = cycle;
                watch.Trail.Append($"\n  >>> deferred destruction stopped taking effect at cycle {cycle}"
                    + $" — elapsed={watch.Elapsed:0.0}s frames={watch.Frames} frame={Time.frameCount}"
                    + "   (the root diagnostic collapses at elapsed=178.6s frame=451, cycle 47)");
            }
            // The clock-gated probes run for as long as it takes rather than for a
            // fixed count, so the per-cycle line is capped: past the cap only the
            // stride and the collapse are written, and the trail cannot grow into a
            // megabyte of XML that nobody can read.
            if (!onStride && watch.TrailLines >= MaxTrailLines) yield break;
            watch.TrailLines++;
            watch.Trail.Append($"\n  cycle {cycle}: probeDestroyed={!alive} {EngineGlobals(watch, onStride)}");
        }

        private static void AssertDeferredDestructionHeld(DestructionCollapseWatch watch, string what)
        {
            Assert.That(watch.FirstCollapsedCycle, Is.LessThan(0),
                $"{watch.CyclesRun} cycles of {what} disabled Unity's deferred destruction for the whole "
                + $"process at cycle {watch.FirstCollapsedCycle}. From that cycle on, a plain GameObject created "
                + "and destroyed in a frame containing nothing else survives. This is the P2-LIFE-012 collapse "
                + "reproduced by strictly less than a full session, so the cause is inside this workload."
                + watch.Trail);

            // A probe that stayed green has to publish how far it actually got, or the
            // next reader repeats the sixty-cycle mistake: green at 174.1s proved
            // nothing whatever about a collapse at 178.6s, and the XML of that run
            // carried no number at all to show it. NUnit captures this into the
            // test-case <output> element, so a pass is now readable evidence.
            TestContext.Out.WriteLine($"GREEN: {watch.CyclesRun} cycles of {what} — reached "
                + $"elapsed={watch.Elapsed:0.0}s frames={watch.Frames} frame={Time.frameCount}. "
                + "The root diagnostic collapses at elapsed=178.6s frame=451 cycle 47; this run is only an "
                + "exoneration where it passed those." + watch.Trail);
        }

        /// <summary>
        /// Composition, truncated at a numbered depth. Every depth is
        /// <c>NebulaBootstrap.StartSession</c>'s own step list, cut short: the same
        /// production installers, in the same order, with the same
        /// <c>NebulaSessionLifetime</c> tearing it all back down. No test-only
        /// composition path exists here, so a red depth names a production step and a
        /// green depth exonerates every step at or below it.
        /// <list type="bullet">
        /// <item>1 — world foundation (gravity, four galaxies with their arm decor,
        /// the intergalactic halo, decor POIs, the planet material pool, asteroid drift)</item>
        /// <item>2 — + player, camera, modules/hangar, starfield and the skybox bake</item>
        /// <item>3 — + the start system's Full streaming boot</item>
        /// <item>4 — + gameplay/extraction/logistics/save and navigation: the directors</item>
        /// <item>5 — + telemetry</item>
        /// <item>6 — + lighting and animation</item>
        /// <item>7 — + presentation (the UI shell), audio and the floating-origin
        /// binding, which is the whole session</item>
        /// </list>
        /// The interesting cuts are 3→4 (directors with no UI) and 6→7 (the UI shell
        /// on top of everything else); those two differences are what "UI shell only"
        /// and "directors only" actually mean inside a composition that cannot build
        /// its UI without a world to show.
        /// </summary>
        private static void RunTruncatedSessionCycle(NebulaBootstrapConfig config,
            Nebula.Content.RuntimeCatalog catalog, int depth)
        {
            var lifetime = new NebulaSessionLifetime();
            try
            {
                var context = new NebulaContext();
                context.SetRuntimeCatalog(catalog);
                int seed = config.Seed == 0 ? 12345 : config.Seed;
                UnityEngine.Random.InitState(seed);
                context.SetConfig(config);
                context.SetResolvedSeed(seed);

                var world = new WorldInstaller();
                var environment = new EnvironmentInstaller();
                world.PrepareSession(seed, context);
                environment.BuildWorldFoundation(config, context, lifetime);
                if (depth >= 2)
                {
                    new PlayerInstaller().CreatePlayerAndCamera(context, lifetime);
                    environment.BuildCameraPresentationBindings(config, context, lifetime);
                }
                if (depth >= 3)
                    world.StartStreaming(context, lifetime, config.ShowOrbitRings, config.ShowPlanetTrails);
                if (depth >= 4)
                {
                    new GameplayInstaller().WireGameplay(context, lifetime, seed);
                    new NavigationInstaller().WireNavigation(context, lifetime);
                }
                if (depth >= 5)
                    new TelemetryInstaller().WireTelemetry(context, lifetime);
                if (depth >= 6)
                {
                    new LightingInstaller().WireLighting(context, lifetime);
                    new AnimationInstaller().WireAnimation(context, lifetime);
                }
                if (depth >= 7)
                {
                    new PresentationInstaller().CreatePresentation(context, lifetime);
                    new AudioInstaller().WireAudio(context, lifetime);
                    var floatingOriginGO = new GameObject("Floating Origin");
                    lifetime.Track(SessionLifetime.CampaignSession, floatingOriginGO);
                    var floatingOrigin = floatingOriginGO.AddComponent<FloatingOrigin>();
                    floatingOrigin.Initialize(context.Ship.transform);
                    context.SetFloatingOrigin(floatingOrigin);
                }
            }
            finally
            {
                lifetime.Dispose();
            }
        }

        /// <summary>The real Inspector values, read from one real session rather than restated in a fixture.</summary>
        private static NebulaBootstrapConfig CaptureProductionConfig(SolarSystemBootstrap adapter)
        {
            NebulaContext context = adapter.BeginSession();
            NebulaBootstrapConfig config = context.Config;
            Assert.That(config, Is.Not.Null, "baseline: a real session published its config");
            return config;
        }

        // ---------------------------------------------------------- axis 2: resources
        //
        // Every probe below carries [Timeout(ProbeTimeoutMilliseconds)] — see that
        // constant for why NUnit's 180s default is disqualifying here — and runs
        // CollapseProbeCycles cycles, stopping a few cycles after a collapse it does
        // observe.
        //
        // The release lane's loop-free statement of the PRODUCT invariant underneath
        // these four is LifecycleOwnershipRegressionTests: the material, texture and
        // mesh probes exist because a session creates ~500 materials, ~110 textures
        // and 4 generator meshes, and the question is whether the session's own scope
        // owns and destroys them — which is asserted directly there, on one of each,
        // in one session, in about half a second.

        /// <summary>
        /// Materials only. Session scale (~500 per cycle), through the production
        /// factory and the production owner, with no GameObject, no installer and no
        /// session anywhere. If this collapses, the accumulating thing is the
        /// material family and nothing else has to be read.
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesSessionScaleMaterialChurn()
        {
            yield return LoadSceneFresh();
            var watch = new DestructionCollapseWatch();
            for (int cycle = 1; cycle <= CollapseProbeCycles && !watch.CollapseConfirmed; cycle++)
            {
                RuntimeResourceScope scope = RuntimeResourceScope.BeginSession();
                for (int i = 0; i < MaterialTriplesPerCycle; i++)
                {
                    MaterialLibrary.CreateLit(Color.white, 0.2f, 0f);
                    MaterialLibrary.CreateAdditive(Color.white);
                    MaterialLibrary.CreateUnlit(Color.white);
                }
                scope.Dispose();
                yield return MeasureDeferredDestruction(watch, cycle);
            }
            AssertDeferredDestructionHeld(watch, "session-scale runtime Material creation and release");
        }

        /// <summary>Textures only, at the same scale and through the same owner.</summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesSessionScaleTextureChurn()
        {
            yield return LoadSceneFresh();
            var watch = new DestructionCollapseWatch();
            for (int cycle = 1; cycle <= CollapseProbeCycles && !watch.CollapseConfirmed; cycle++)
            {
                RuntimeResourceScope scope = RuntimeResourceScope.BeginSession();
                for (int i = 0; i < TexturePairsPerCycle; i++)
                {
                    MaterialLibrary.CreatePerlinTexture(128, Color.black, Color.white, 5f);
                    MaterialLibrary.CreateNebulaTexture(128, Color.blue, Color.magenta);
                }
                scope.Dispose();
                yield return MeasureDeferredDestruction(watch, cycle);
            }
            AssertDeferredDestructionHeld(watch, "session-scale runtime Texture2D creation and release");
        }

        /// <summary>
        /// The four large generator meshes a session builds (starfield, galaxy
        /// clouds, dust tiers, halo), at the vertex order of magnitude the real ones
        /// carry and with the same 32-bit index format.
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesSessionScaleRuntimeMeshChurn()
        {
            yield return LoadSceneFresh();
            var watch = new DestructionCollapseWatch();
            var vertices = new Vector3[VerticesPerProbeMesh];
            var triangles = new int[VerticesPerProbeMesh];
            for (int i = 0; i < VerticesPerProbeMesh; i++) { vertices[i] = Vector3.one * i; triangles[i] = i % VerticesPerProbeMesh; }

            for (int cycle = 1; cycle <= CollapseProbeCycles && !watch.CollapseConfirmed; cycle++)
            {
                RuntimeResourceScope scope = RuntimeResourceScope.BeginSession();
                for (int i = 0; i < RuntimeMeshesPerCycle; i++)
                {
                    Mesh mesh = RuntimeResourceScope.Own(new Mesh
                    {
                        name = $"collapseProbeMesh.{cycle}.{i}",
                        indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
                    });
                    mesh.vertices = vertices;
                    mesh.triangles = triangles;
                }
                scope.Dispose();
                yield return MeasureDeferredDestruction(watch, cycle);
            }
            AssertDeferredDestructionHeld(watch, "session-scale runtime Mesh creation and release");
        }

        /// <summary>
        /// The skybox bake, alone. One 1024³ cube <c>RenderTexture</c>, one
        /// <c>RenderToCubemap</c>, one <c>Shader.Find</c>, one skybox material and one
        /// <c>RenderSettings.skybox</c> assignment per cycle — exactly what
        /// <c>EnvironmentInstaller.BuildCameraPresentationBindings</c> does once per
        /// session. It is the largest per-session native graphics allocation the
        /// product makes, which is why it gets its own probe rather than being folded
        /// into the world-foundation one.
        /// <para>
        /// Under <c>-nographics</c> the device is <c>NullGfxDevice</c> and
        /// <c>RenderToCubemap</c> takes the fallback branch, so a green result here
        /// must be re-run with <c>--graphics</c> before it counts as an exoneration of
        /// the bake itself. The cube <c>RenderTexture</c> is allocated on both branches
        /// either way, which is the part this probe can measure headless.
        /// </para>
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesRepeatedSkyboxBaking()
        {
            yield return LoadSceneFresh();
            var watch = new DestructionCollapseWatch();
            for (int cycle = 1; cycle <= CollapseProbeCycles && !watch.CollapseConfirmed; cycle++)
            {
                RuntimeResourceScope scope = RuntimeResourceScope.BeginSession();
                var backdrop = new GameObject($"skyboxProbeBackdrop.{cycle}");
                // TryBake destroys the backdrop itself on the success path and leaves
                // it standing on the fallback path, exactly as production relies on.
                if (!SkyboxBaker.TryBake(backdrop, Color.black)) UnityEngine.Object.Destroy(backdrop);
                RenderSettings.skybox = null;
                scope.Dispose();
                yield return MeasureDeferredDestruction(watch, cycle);
            }
            AssertDeferredDestructionHeld(watch, "the per-session skybox bake (cube RenderTexture + skybox material)");
        }

        // -------------------------------------------------------- axis 1: composition

        /// <summary>
        /// The world foundation alone — no player, no camera, no streaming, no
        /// gameplay, no UI. This is the single largest allocator in a session and the
        /// first place to look, and it needs none of the rest of composition to run.
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesWorldFoundationOnlyCycles()
        {
            yield return RunTruncatedSessionProbe(depth: 1, what: "world-foundation composition only (galaxies, halo, decor, gravity)");
        }

        /// <summary>World foundation + player/camera + the starfield and its skybox bake.</summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesWorldPlusPlayerCycles()
        {
            yield return RunTruncatedSessionProbe(depth: 2, what: "world foundation + player/camera + starfield/skybox bake");
        }

        /// <summary>
        /// Everything above plus the start system's Full streaming boot — planets,
        /// orbit rings, trails, the asteroid belt, POIs and the combat encounter.
        /// Green here with the full session red would leave only gameplay,
        /// navigation, telemetry, lighting, animation and presentation.
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesWorldPlusStreamingCycles()
        {
            yield return RunTruncatedSessionProbe(depth: 3, what: "world foundation + player + Full system streaming");
        }

        /// <summary>
        /// Everything above plus the directors: extraction, gameplay, logistics, save
        /// wiring and navigation. The 3→4 step is "the whole world with directors and
        /// no UI whatsoever" — the half of the composition the shell never sees.
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesWorldPlusDirectorCycles()
        {
            yield return RunTruncatedSessionProbe(depth: 4, what: "world + streaming + gameplay/extraction/navigation directors, no UI");
        }

        /// <summary>
        /// Everything above plus telemetry. It gets a step of its own because it is
        /// the one installer whose per-session state is neither a GameObject nor a
        /// runtime resource: a static dictionary keyed by campaign id, and a file
        /// rewritten in full on every funnel event, both of which grow with the number
        /// of campaigns this process has ever started.
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesWorldPlusTelemetryCycles()
        {
            yield return RunTruncatedSessionProbe(depth: 5, what: "world + directors + telemetry");
        }

        /// <summary>Everything above plus lighting and animation — every visual director, still with no UI.</summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesWorldPlusLightingAndAnimationCycles()
        {
            yield return RunTruncatedSessionProbe(depth: 6, what: "world + directors + telemetry + lighting + animation, no UI");
        }

        /// <summary>
        /// The whole composition, assembled step by step rather than through the
        /// adapter: presentation (the UI shell), audio and the floating-origin
        /// binding on top of depth 6. Red here with depth 6 green puts the defect in
        /// the UI shell and nothing else; red at both makes the shell innocent.
        /// It is also the control for <c>DeferredDestruction_SurvivesFullSessionCycles</c>:
        /// the two build the same session by two different routes and must agree.
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesWholeCompositionCycles()
        {
            yield return RunTruncatedSessionProbe(depth: 7, what: "the whole composition including the UI shell, audio and floating origin");
        }

        private IEnumerator RunTruncatedSessionProbe(int depth, string what)
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();

            // One real session, only to read the adapter's real Inspector values and
            // build the content catalog the installers need. Ended and drained before
            // the measurement starts, so it is not part of any cycle.
            NebulaBootstrapConfig config = CaptureProductionConfig(adapter);
            adapter.EndSession();
            yield return DrainDeferredTeardown();

            Nebula.Content.RuntimeCatalog catalog =
                Nebula.Content.RuntimeCatalogBuilder.Build(new Nebula.Content.CSharpContentCatalog());

            var watch = new DestructionCollapseWatch();
            for (int cycle = 1; cycle <= CollapseProbeCycles && !watch.CollapseConfirmed; cycle++)
            {
                RunTruncatedSessionCycle(config, catalog, depth);
                yield return MeasureDeferredDestruction(watch, cycle);
            }
            AssertDeferredDestructionHeld(watch, what);
        }

        // ------------------------------------------------------- axis 3: the boundary

        /// <summary>
        /// The anchor and the boundary instrument in one run: real
        /// BeginSession/EndSession cycles with the engine-globals line on every cycle
        /// and the object-table census on the stride. Whatever steps between the last
        /// working cycle and the first broken one is the lead, and this is the run
        /// that shows it. It also validates every probe above: this one is the most
        /// expensive workload in the ladder, so if it does NOT collapse, no cheaper
        /// probe's green means anything.
        /// <para>
        /// This test was the vehicle's own blind spot. At sixty cycles it completed
        /// green in 174.1s while the root diagnostic collapses at cycle 47 / 178.6s —
        /// past the collapsing cycle and four seconds short of the collapsing second.
        /// The budget is now <see cref="CollapseProbeCycles"/>, which clears both.
        /// </para>
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesFullSessionCycles()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();

            var watch = new DestructionCollapseWatch();
            for (int cycle = 1; cycle <= CollapseProbeCycles && !watch.CollapseConfirmed; cycle++)
            {
                adapter.BeginSession();
                yield return null;
                adapter.EndSession();
                yield return DrainDeferredTeardown();
                yield return MeasureDeferredDestruction(watch, cycle);
            }
            AssertDeferredDestructionHeld(watch, "full BeginSession/EndSession composition");
        }

        // ------------------------------------- axis 4: what the two runs disagree on
        //
        // Cycles are excluded (60 green > 47 red). Frames are excluded (the anchor
        // ran ~540 frames green against the diagnostic's 451 red, and a session-free
        // control ran 1,845,267). Wall clock with no session is excluded (213.2s
        // green). Three measurable differences remain between the red diagnostic and
        // the green anchor, and each gets a probe of its own here:
        //
        //   1. elapsed seconds with a session ALIVE, rather than with sessions being
        //      built and torn down — the diagnostic reached 178.6s, the anchor 174.1s;
        //   2. elapsed seconds with session CHURN, held apart from the cycle count by
        //      padding a small number of cycles out to the same wall clock;
        //   3. the scene-scan churn the diagnostic adds and the anchor does not —
        //      SceneRoots() is a FindObjectsOfType<Transform>(true) sweep over ~3,500
        //      objects, called several times per cycle in the diagnostic and never in
        //      the anchor.
        //
        // These are discriminators, not suspects: two of the three are instrumentation
        // rather than product, and a red there means the measuring apparatus is part of
        // the phenomenon and the composition ladder has to be re-read in that light.

        /// <summary>
        /// One session, begun once and never ended, sat in for longer than the product
        /// survives. Nothing churns: no teardown, no second scope, no registry reset,
        /// no composition. Only the shared detector runs, in its own frame, again and
        /// again, until the clock passes <see cref="CollapseProbeSeconds"/>.
        /// <para>
        /// Red here means a single live session is sufficient and teardown, scope
        /// churn and session count are all innocent — the cause is something a running
        /// session does per frame or on a timer, and the ladder should bisect by
        /// director rather than by composition depth. Green past 300s means session
        /// churn is required and the boundary work is where to look.
        /// </para>
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesASingleLiveSessionPastTheCollapseTime()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();

            adapter.BeginSession();
            yield return null;

            // Started after the session is up, so "elapsed" means "elapsed with a live
            // session" and is directly comparable with the diagnostic's 178.6s.
            var watch = new DestructionCollapseWatch();
            int cycle = 0;
            while (watch.Elapsed < CollapseProbeSeconds && !watch.CollapseConfirmed)
                yield return MeasureDeferredDestruction(watch, ++cycle);

            Assert.That(adapter.SessionActive, Is.True,
                "the session under measurement ended on its own, so this run measured something else"
                + watch.Trail);
            AssertDeferredDestructionHeld(watch,
                $"one live session left running for {CollapseProbeSeconds:0}s with no teardown of any kind");

            adapter.EndSession();
        }

        /// <summary>
        /// Session churn held constant while the clock is pushed past the collapse:
        /// a small number of real Begin/End cycles, each padded with quiet frames
        /// until the cycle has consumed <see cref="PaddedCycleSeconds"/> of wall time.
        /// The padding is frames, not a busy-wait, because frames are the one axis
        /// already excluded by measurement.
        /// <para>
        /// Read against the anchor: if the anchor needs ~60 cycles to collapse and
        /// this one collapses in ~15, elapsed time — not repetition — carries the
        /// defect. If this one runs the whole budget green while the anchor goes red,
        /// repetition carries it and the clock is a coincidence of pacing.
        /// </para>
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesFullSessionCyclesPaddedPastTheCollapseTime()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();

            var watch = new DestructionCollapseWatch();
            int cycles = Mathf.Max(2, Mathf.CeilToInt(CollapseProbeSeconds / PaddedCycleSeconds));
            for (int cycle = 1; cycle <= cycles && !watch.CollapseConfirmed; cycle++)
            {
                float cycleStarted = Time.realtimeSinceStartup;
                adapter.BeginSession();
                yield return null;
                adapter.EndSession();
                yield return DrainDeferredTeardown();
                while (Time.realtimeSinceStartup - cycleStarted < PaddedCycleSeconds) yield return null;
                yield return MeasureDeferredDestruction(watch, cycle);
            }
            AssertDeferredDestructionHeld(watch,
                $"{cycles} full session cycles paced to {PaddedCycleSeconds:0}s each so the clock, not the count, reaches the collapse");
        }

        /// <summary>
        /// The root diagnostic's own instrumentation, with no session anywhere near
        /// it: a scene-sized hierarchy of plain GameObjects built once, then the same
        /// <c>FindObjectsOfType&lt;Transform&gt;(true)</c> sweeps the diagnostic makes
        /// per cycle, then the shared detector.
        /// <para>
        /// This is the negative control for the measuring apparatus itself. The
        /// diagnostic pays ~0.9s per cycle more than the anchor for these sweeps and
        /// is the only one of the two that goes red; if the sweeps alone collapse
        /// destruction, the composition ladder cannot be read at face value and the
        /// bisection has to move to the instrument. Green is the expected and much
        /// more useful answer: it removes the last difference between the two runs
        /// that is not the clock.
        /// </para>
        /// </summary>
        [UnityTest, Category("Diagnostic"), Timeout(ProbeTimeoutMilliseconds)]
        public IEnumerator DeferredDestruction_SurvivesTheSceneScanChurnTheDiagnosticAdds()
        {
            yield return LoadSceneFresh();

            // Built once and left standing, so the sweeps cost what they cost in the
            // diagnostic — where a live session is what puts ~3,500 transforms in
            // front of them — without a session existing to be blamed for the result.
            var scaffold = new GameObject("sceneScanScaffold");
            for (int i = 0; i < ScanScaffoldObjects; i++)
            {
                var child = new GameObject($"sceneScanScaffold.{i}");
                child.transform.SetParent(scaffold.transform, false);
            }
            yield return null;

            var watch = new DestructionCollapseWatch();
            int swept = 0;
            for (int cycle = 1; cycle <= CollapseProbeCycles && !watch.CollapseConfirmed; cycle++)
            {
                for (int sweep = 0; sweep < ScanSweepsPerCycle; sweep++)
                {
                    swept += UnityEngine.Object.FindObjectsOfType<Transform>(true).Length;
                    foreach (GameObject unused in SceneManager.GetActiveScene().GetRootGameObjects()) swept++;
                }
                yield return MeasureDeferredDestruction(watch, cycle);
            }

            UnityEngine.Object.Destroy(scaffold);
            Assert.That(swept, Is.GreaterThan(0), "the sweeps returned nothing, so this probe measured no churn at all");
            AssertDeferredDestructionHeld(watch,
                $"{ScanSweepsPerCycle} full scene-transform sweeps per cycle over a {ScanScaffoldObjects}-object hierarchy, with no session");
        }
    }
}
