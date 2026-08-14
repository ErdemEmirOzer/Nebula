using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 3 — engine probe, not a product test.
    /// <para>
    /// The hundred-cycle lifecycle gate reported that <c>UnityEngine.Object.Destroy</c>
    /// stops taking effect from roughly the forty-sixth campaign onward: the same
    /// objects that received <c>SetActive(false)</c> + <c>Destroy</c> in cycles 1–45
    /// and died, receive it in cycle 46 and live. Every product-side hypothesis was
    /// eliminated (ownership, exceptions, a too-short deferred window, memory
    /// pressure, a second session), which leaves exactly one candidate: the batchmode
    /// PlayMode test runner itself.
    /// </para>
    /// <para>
    /// This fixture settles that. It touches <b>no</b> Nebula code — no session, no
    /// world, no bootstrap, no scene. It creates plain <c>GameObject</c>s, destroys
    /// them the same way the session teardown does, drains the same deferred window
    /// and counts what survived. If this goes red at the same threshold, the
    /// threshold belongs to the engine and no ownership fix can move it; if it stays
    /// green for a hundred cycles at campaign scale, the defect is ours.
    /// </para>
    /// </summary>
    // Diagnostic, and the whole fixture: this is the bisection ladder for the cycle-46
    // destroy incident, and its own header says it -- "engine probe, not a product test".
    // It touches no Nebula code on four of its five leaves and asserts about
    // UnityEngine.Object.Destroy rather than about this game, so a red here reads "the
    // engine stopped honouring deferred destruction", which is not a release signal and
    // must not gate a release. It carries no ReleaseRegression category on purpose --
    // the release lane selects by that name and must not reach here.
    [Category("Diagnostic")]
    public class EngineDestroyProbePlayModeTests
    {
        /// <summary>Same count as the lifecycle gate, so the thresholds are comparable.</summary>
        private const int ProbeCycles = 100;

        /// <summary>
        /// The instrumented run measured 160 campaign roots + 16 UI roots torn down
        /// per cycle. Matching that shape means an allocation- or handle-driven limit
        /// would be reproduced here, not merely a per-call one.
        /// </summary>
        private const int CampaignScaleRoots = 176;

        /// <summary>The session teardown's own deferred window, reproduced exactly.</summary>
        private static IEnumerator DrainDeferredTeardown()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        /// <summary>
        /// The minimal reading: a handful of bare GameObjects per cycle. No
        /// components, no children, no allocation pressure — if <c>Destroy</c> is
        /// broken as such, it breaks here.
        /// </summary>
        [UnityTest]
        public IEnumerator PlainGameObjects_AreDestroyedOnEveryOneOfAHundredCycles()
        {
            yield return RunProbe(4, false, "plain");
        }

        /// <summary>
        /// The campaign-shaped reading: as many roots per cycle as a real session
        /// tears down, each carrying children and components, so the probe allocates
        /// and releases on the same order as the product does. This is the run that
        /// distinguishes "Destroy is fine, our teardown is not" from "the process
        /// stops honouring deferred destruction after N objects".
        /// </summary>
        [UnityTest]
        public IEnumerator CampaignScaleGameObjects_AreDestroyedOnEveryOneOfAHundredCycles()
        {
            yield return RunProbe(CampaignScaleRoots, true, "campaign-scale");
        }

        /// <summary>
        /// The airtight reading, and the one that decides the architecture.
        /// <para>
        /// The two probes above allocate 4 and 704 objects per cycle; the product
        /// allocates 3,536 GameObjects and 11,283 Components per cycle. A limit driven
        /// by <em>cumulative</em> object count would therefore be reached by the
        /// product at cycle 46 (~162,000 objects) and never reached by a probe that
        /// only ever creates 70,400. That is a real alternative explanation, and this
        /// test is what excludes it: same per-cycle shape as a campaign, same hundred
        /// cycles, ~353,000 GameObjects and ~1,056,000 Components in one process.
        /// </para>
        /// <para>
        /// If deferred destruction survives this, no allocation- or handle-exhaustion
        /// story can explain the product's cycle-46 threshold.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ProductScaleGameObjects_AreDestroyedOnEveryOneOfAHundredCycles()
        {
            yield return RunProbe(CampaignScaleRoots, true, "product-scale", ChildrenPerProductRoot);
        }

        /// <summary>
        /// 176 roots × 20 = 3,520 GameObjects per cycle against the product's measured
        /// 3,536, and three Components each (Transform + two) = 10,560 against the
        /// product's 11,283. Deliberately just under, so the probe can never be
        /// accused of passing because it did less work than the thing it models.
        /// </summary>
        private const int ChildrenPerProductRoot = 19;

        /// <summary>A MonoBehaviour of our own, so the probe exercises the managed-wrapper path too.</summary>
        private sealed class ProbeBehaviour : MonoBehaviour
        {
        }

        /// <summary>
        /// The bisection. The three probes above pass without a scene; the lifecycle
        /// fixture's in-situ probes — plain GameObjects, destroyed in a frame with no
        /// teardown at all — start surviving at cycle 46. The only differences between
        /// the two are the loaded Nebula scene and the session that has run in it.
        /// <para>
        /// This test removes the session and keeps the scene: load Nebula.unity once,
        /// never call BeginSession, then run the same hundred plain destroy cycles. If
        /// destruction dies here too, the scene alone is sufficient and no session code
        /// is implicated; if it stays green, the session is required to trigger it.
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator WithTheNebulaSceneLoadedButNoSession_PlainDestroyStillWorks()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/Nebula.unity",
                new UnityEngine.SceneManagement.LoadSceneParameters(
                    UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
                "Nebula", UnityEngine.SceneManagement.LoadSceneMode.Single);
#endif
            yield return null;
            yield return RunProbe(CampaignScaleRoots, true, "nebula-scene-no-session");
        }

        /// <summary>
        /// The last environmental confound. Every green probe so far finishes in under
        /// ten seconds, while the lifecycle fixture reaches its cycle 46 at 176.6s — so
        /// "destruction stops after about three minutes of play" is not yet excluded by
        /// measurement, only by implausibility. This run excludes it by measurement:
        /// the Nebula scene is loaded and each cycle is padded with real time so the
        /// probe passes 200 seconds, well beyond where the product dies.
        /// </summary>
        // NUnit's default 180s per-test bound is below the 176.6s this run has to pass
        // through plus its own overhead. Raising the infrastructure bound is not a
        // loosened assertion — the assertion below is untouched, and the run still sits
        // inside runscoped.sh's 900s watchdog.
        [UnityTest, Timeout(400000)]
        public IEnumerator PastTheElapsedTimeWhereTheProductDies_PlainDestroyStillWorks()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/Nebula.unity",
                new UnityEngine.SceneManagement.LoadSceneParameters(
                    UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
                "Nebula", UnityEngine.SceneManagement.LoadSceneMode.Single);
#endif
            yield return null;
            yield return RunProbe(CampaignScaleRoots, true, "past-176s", 3, 2.1f);
        }

        /// <summary>
        /// Creates <paramref name="rootsPerCycle"/> roots, tears them down the way
        /// <c>NebulaSessionLifetime.DestroyAllReverse</c> does — reverse order,
        /// <c>SetActive(false)</c> then <c>Destroy</c> — and reports, per cycle, how
        /// many of the objects it destroyed are still alive after the deferred window.
        /// A survivor is given forty extra frames before it is called a survivor, so
        /// "not destroyed yet" can never be mistaken for "never destroyed".
        /// </summary>
        private static IEnumerator RunProbe(int rootsPerCycle, bool withChildren, string label,
                                           int childrenPerRoot = 3, float padSecondsPerCycle = 0f)
        {
            // A frame before the first measurement, so the runner's own scene setup
            // is not counted inside cycle 1.
            yield return null;

            var trail = new StringBuilder();
            int firstSurvivingCycle = -1;
            int totalCreated = 0;
            long totalSurvivors = 0;
            // Every GameObject and Component the probe brings into existence, so the
            // failure message can be compared directly against the product's measured
            // per-cycle counters instead of being taken on trust.
            long created = 0;

            for (int cycle = 1; cycle <= ProbeCycles; cycle++)
            {
                var roots = new List<GameObject>(rootsPerCycle);
                for (int i = 0; i < rootsPerCycle; i++)
                {
                    var root = new GameObject($"probe.{label}.{cycle}.{i}");
                    if (withChildren)
                    {
                        // Components and children are what a campaign root actually
                        // carries; a bare transform would under-exercise the same
                        // native allocator the product hits.
                        root.AddComponent<MeshFilter>();
                        root.AddComponent<MeshRenderer>();
                        created += 2;
                        for (int c = 0; c < childrenPerRoot; c++)
                        {
                            var child = new GameObject($"probe.child.{c}");
                            child.transform.SetParent(root.transform, false);
                            child.AddComponent<BoxCollider>();
                            child.AddComponent<ProbeBehaviour>();
                            created += 2;
                        }
                        created += childrenPerRoot;
                    }
                    roots.Add(root);
                }
                created += rootsPerCycle;
                totalCreated += rootsPerCycle;

                // Exactly the teardown shape under test.
                for (int i = roots.Count - 1; i >= 0; i--)
                {
                    GameObject root = roots[i];
                    if (root == null) continue;
                    root.SetActive(false);
                    Object.Destroy(root);
                }

                yield return DrainDeferredTeardown();

                // Real seconds, spent AFTER the destroy call and its drain, so the
                // deferred window itself is unchanged and only the clock moves.
                if (padSecondsPerCycle > 0f)
                {
                    float until = Time.realtimeSinceStartup + padSecondsPerCycle;
                    while (Time.realtimeSinceStartup < until) yield return null;
                }

                int alive = 0;
                for (int i = 0; i < roots.Count; i++) if (roots[i] != null) alive++;

                if (alive > 0)
                {
                    // Never destroyed, or merely not destroyed yet? Ten times the
                    // window answers that without changing what the product does.
                    for (int extra = 0; extra < 40; extra++) yield return null;
                    int settled = 0;
                    for (int i = 0; i < roots.Count; i++) if (roots[i] != null) settled++;

                    if (firstSurvivingCycle < 0)
                    {
                        firstSurvivingCycle = cycle;
                        // Elapsed and frame are here so this can be compared directly
                        // against the lifecycle fixture, which reports elapsed=176.6s
                        // frame=447 at its own cycle 46. If a threshold is really about
                        // wall time or frame count rather than the scene, the two runs
                        // will agree on that number and not on the cycle.
                        trail.Append($"\n  >>> first surviving cycle: {cycle}"
                            + $" alive@4frames={alive}/{rootsPerCycle}"
                            + $" alive@44frames={settled}/{rootsPerCycle}"
                            + $" elapsed={Time.realtimeSinceStartup:F1}s frame={Time.frameCount}");
                        GameObject sample = null;
                        for (int i = 0; i < roots.Count && sample == null; i++) sample = roots[i];
                        if (sample != null)
                            trail.Append($"\n      survivor '{sample.name}' id={sample.GetInstanceID()}"
                                + $" scene='{sample.scene.name}' handle={sample.scene.handle}"
                                + $" flags={sample.hideFlags} active={sample.activeInHierarchy}");
                    }
                    else if (cycle % 10 == 0)
                    {
                        trail.Append($"\n  cycle {cycle}: alive@4frames={alive}"
                            + $" alive@44frames={settled} of {rootsPerCycle}");
                    }
                    totalSurvivors += settled;
                }
            }

            // A green probe has to show its work, or it proves nothing: the whole point
            // is the SIZE of the run it survived. This line lands in the result XML.
            Debug.Log($"[probe:{label}] {ProbeCycles} cycles × {rootsPerCycle} roots"
                + $" ({childrenPerRoot} children each) — {created} GameObjects+Components created"
                + $" and destroyed in one batchmode process; survivors={totalSurvivors}"
                + $"; ended at elapsed={Time.realtimeSinceStartup:F1}s frame={Time.frameCount}");

            Assert.That(firstSurvivingCycle, Is.LessThan(0),
                $"[{label}] UnityEngine.Object.Destroy stopped taking effect inside a single "
                + $"batchmode PlayMode process. {ProbeCycles} cycles × {rootsPerCycle} plain roots "
                + $"= {totalCreated} objects created, {totalSurvivors} still alive after their "
                + $"deferred window. No Nebula code participates in this fixture.{trail}");
        }
    }
}
