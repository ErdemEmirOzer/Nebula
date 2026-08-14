using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 2 / AJAN 2 — the hundred-cycle endurance measurements.
    /// Every leaf is <c>[Category("Soak")]</c> and none of them belongs in a release
    /// gate: the measured Begin/End hundred alone takes 410.9s, which is a third of
    /// the whole gate's budget for one leaf.
    /// <para>
    /// Cycle counts are unchanged and must stay unchanged. These tests measure a
    /// PER-CYCLE delta, and the loop is the amplifier: a leak of one object per
    /// campaign is one object at N=1 — inside the noise of a warm-up — and a hundred
    /// at N=100, which no reader can mistake. Shortening a soak to fit a gate
    /// produces a green that measured nothing, which is the same failure the sixty-
    /// cycle anchor probe made in the diagnostic fixture.
    /// </para>
    /// <para>
    /// The release lane does not go uncovered as a result. For each scenario here,
    /// <see cref="LifecycleOwnershipRegressionTests"/> carries a short, loop-free
    /// regression that pins the same invariant by asserting on the OWNER — who
    /// registered the resource, who released it, and what is left standing one frame
    /// later — rather than by repeating the scenario until arithmetic makes a leak
    /// visible. Each of those names the production line whose deletion turns it red.
    /// </para>
    /// </summary>
    public class AuditPart2LifecycleSoakTests : LifecycleAuditFixture
    {
        /// <summary>The Part 2 close-out contract: every lifecycle scenario runs one hundred production-path cycles in its own Unity process/filter.</summary>
        private const int LifecycleCycles = 100;

        /// <summary>Screen push/pop is cheap, so this one really does run the full hundred.</summary>
        private const int ScreenCycles = 100;

        /// <summary>
        /// Fifteen minutes for a hundred production-path cycles plus warm-up. The
        /// measured Begin/End hundred took 7m06s (4.07s/cycle); the derived warm-up
        /// below adds at most <see cref="WarmupCycleCap"/> - 1 further cycles, about
        /// 45s, and the old ten-minute budget was already only 1.46x the measurement.
        /// <para>
        /// This is a harness budget, not a leak tolerance: it bounds how long the
        /// framework waits, and no assertion in this file reads it. Nothing about what
        /// counts as a leak moves.
        /// </para>
        /// </summary>
        private const int GateTimeoutMilliseconds = 900000;

        /// <summary>
        /// Consecutive warm-up cycles that must change NOTHING before a baseline is
        /// taken from them.
        /// <para>
        /// Why a stable point rather than a fixed cycle: every counter these tests
        /// watch is settled by cycle 1 except the lazily-created, deliberately
        /// persistent ones — seventeen <c>HideAndDontSave</c> StrategicMap layer
        /// materials, glTF model materials, animation materials — which are built on
        /// first use and then never again. A baseline captured mid-warm-up counts
        /// those as growth. Measured: <c>BeginEndSessionCycles</c> and
        /// <c>RestartCampaignCycles</c> reported +38 Material with every one of the 80
        /// records carrying <c>firstCycle=1</c>, and <c>SaveLoadCycles</c> +2
        /// GameObject/+4 Component with records spread over <c>firstCycle</c> 1-5. The
        /// "0.38/cycle" in those failures was 38 divided by 100, not a measured rate.
        /// </para>
        /// <para>
        /// Three, not one: a single quiet cycle inside a still-warming sequence would
        /// end the warm-up early. Three is not load-bearing for leak DETECTION and
        /// must not be read as a tolerance — the detector is the
        /// <see cref="LifecycleCycles"/>-cycle comparison below, which starts from
        /// wherever this stops. A leak that accumulates still accumulates across a
        /// hundred cycles from any starting point, so ending the warm-up early costs
        /// sensitivity nowhere; ending it too LATE is the only real risk, and
        /// <see cref="WarmupCycleCap"/> is what bounds that.
        /// </para>
        /// </summary>
        private const int StableCycles = 3;

        /// <summary>
        /// The ceiling on warm-up, and the reason this fix cannot blind the test.
        /// <para>
        /// A genuine per-cycle leak never produces <see cref="StableCycles"/>
        /// unchanged cycles, so it cannot be absorbed as warm-up: it exhausts this cap
        /// and <see cref="WarmUpUntilStable"/> FAILS, naming the counters that would
        /// not settle and the per-cycle drift of each. The cap is asserted, never
        /// assumed — reaching it is a red test, not a fallback to "measure anyway".
        /// </para>
        /// <para>
        /// Twelve is 2.4x the longest warm-up any of these scenarios has been observed
        /// to need (<c>SaveLoadCycles</c>, whose last first-use record is cycle 5),
        /// which leaves room for a slower machine without leaving room for a leak: one
        /// object per cycle exhausts it in twelve cycles and reports.
        /// </para>
        /// </summary>
        private const int WarmupCycleCap = 12;

        /// <summary>
        /// The suffix <see cref="Snapshot"/> gives every whole-process resource-family
        /// counter: <c>Material</c>, <c>Texture</c>, <c>Mesh</c>, <c>RenderTexture</c>,
        /// <c>Cubemap</c>.
        /// </summary>
        private const string ResourceCounterMarker = "(all-loaded)";

        /// <summary>
        /// The resource counters, read off the snapshot itself rather than listed here.
        /// <para>
        /// This replaces the prefix pair <c>"Material", "Texture"</c>, which was the
        /// same trap as an allow-list: it was written to mean "the resource counters"
        /// and silently did not cover <c>RenderTexture(all-loaded)</c> or
        /// <c>Cubemap(all-loaded)</c>, because neither STARTS with either prefix — they
        /// are the two counters <see cref="Snapshot"/> broke out precisely so they
        /// could not hide inside <c>Texture</c>. So a counter this test explicitly
        /// meant to ignore was measured, and it produced the RenderTexture 3 -> 1 red.
        /// </para>
        /// <para>
        /// Derived, the list cannot fall out of step: a resource family added to
        /// <see cref="Snapshot"/> tomorrow is excluded the day it appears. And the
        /// direction of the remaining risk is the safe one — <c>DifferencesAgainst</c>
        /// measures every counter it is not handed, so a counter added WITHOUT the
        /// marker is measured rather than skipped. That is a loud false positive, not
        /// a silent gap, which is the trade an allow-list gets backwards.
        /// </para>
        /// <para>
        /// Excluding them costs no coverage, because nothing now rests on them. The
        /// binding leaf never meant to measure resources at all, and the three session
        /// leaves assert on <see cref="RuntimeResourceCounts"/> instead — which counts
        /// the same families more sharply, restricted to the instances this process
        /// created. The one leaf still reading these counters unfiltered is
        /// <c>StreamingPromoteDemoteCycles</c>, deliberately: it revisits the same two
        /// systems every cycle, so its content variety is bounded by construction and
        /// its all-loaded counts really do settle. It is also the leaf that measured
        /// the <c>MaterialVariantCache</c> fix, and it passes.
        /// </para>
        /// </summary>
        private static string[] ResourceCounterKeys(Snapshot snapshot)
        {
            var keys = new List<string>();
            foreach (KeyValuePair<string, long> counter in snapshot.Values)
                if (counter.Key.EndsWith(ResourceCounterMarker, System.StringComparison.Ordinal))
                    keys.Add(counter.Key);

            // Whole key names, passed to a prefix filter: a full name is a prefix of
            // itself and of nothing else here, so no near-miss is possible. Empty
            // would mean the marker convention had been dropped and this exclusion had
            // quietly become a no-op.
            Assert.That(keys, Is.Not.Empty,
                $"no counter carries the '{ResourceCounterMarker}' marker, so the resource "
                + "exclusion matches nothing — Snapshot.Take's naming convention changed "
                + "and this filter needs to change with it");
            return keys.ToArray();
        }

        /// <summary>
        /// True for a resource this process CREATED, false for one loaded from an
        /// asset. <c>IsPersistent</c> is the exact question: an asset on disk is
        /// persistent, a <c>new Material(...)</c> instance is not.
        /// <para>
        /// Editor-only, like the rest of this fixture — <c>LoadSceneFresh</c> already
        /// boots through <c>EditorSceneManager</c>, so there is no player path to
        /// degrade to. A player build would have no way to answer this and must not
        /// silently answer it wrongly, so it counts nothing and
        /// <see cref="RuntimeResourceCounts"/> asserts the count is live.
        /// </para>
        /// </summary>
        private static bool IsRuntimeInstance(UnityEngine.Object resource)
        {
#if UNITY_EDITOR
            return resource != null && !UnityEditor.EditorUtility.IsPersistent(resource);
#else
            return false;
#endif
        }

        private static long CountRuntimeInstances<T>() where T : UnityEngine.Object
        {
            long count = 0;
            foreach (T resource in Resources.FindObjectsOfTypeAll<T>())
                if (IsRuntimeInstance(resource)) count++;
            return count;
        }

        /// <summary>
        /// The resource counters these session leaves actually assert on: instances
        /// this process created, not assets it has loaded.
        /// <para>
        /// <see cref="Snapshot"/>'s <c>(all-loaded)</c> families cannot carry this
        /// assertion and no amount of warm-up makes them able to.
        /// <c>Resources.FindObjectsOfTypeAll&lt;Material&gt;</c> counts everything
        /// LOADED, and Unity asset loading is one-way — a material loaded once stays
        /// until something unloads it. These cycles materialise procedurally varied
        /// content (<c>System sector.0.0.0</c>, <c>Observation Ship</c>,
        /// <c>Anomaly 04</c>, <c>Thalassa Galaxy</c>), so a later cycle visits systems
        /// an earlier one did not and pulls their glTF materials into the cache. The
        /// count therefore rises with content VARIETY, not with time, and "returns to
        /// baseline after N cycles" is simply false for it.
        /// </para>
        /// <para>
        /// The measured proof it is the counter and not the instant: warm-up cycles
        /// 2-4 changed no counter at all, and then the first MEASURED cycle created
        /// 38 materials. With identical warm-up and measured bodies that is impossible
        /// for a per-cycle leak and unremarkable for a monotonic cache.
        /// </para>
        /// <para>
        /// This is strictly SHARPER, not weaker. The leak this fixture exists to catch
        /// is a resource the session created and failed to release — every one of
        /// which is a runtime instance and is counted here. The four per-session
        /// generator meshes that once leaked invisibly (starfield, galaxy clouds, dust
        /// tiers, halo) are runtime instances too, so they stay caught, and with the
        /// asset-cache noise gone a single leaked material is now visible where it
        /// previously had to clear a moving floor of dozens.
        /// </para>
        /// </summary>
        private static Dictionary<string, long> RuntimeResourceCounts()
        {
            var counts = new Dictionary<string, long>
            {
                { "Material(runtime-instance)", CountRuntimeInstances<Material>() },
                { "Texture(runtime-instance)", CountRuntimeInstances<Texture>() },
                { "Mesh(runtime-instance)", CountRuntimeInstances<Mesh>() },
                { "RenderTexture(runtime-instance)", CountRuntimeInstances<RenderTexture>() },
            };

            // A booted session always holds runtime-created resources. Zero means
            // IsRuntimeInstance answered "no" to everything — a player build, or an
            // editor API that changed under us — and an assertion that can only ever
            // compare 0 to 0 is the exact shape of a test that measures nothing.
            long total = 0;
            foreach (KeyValuePair<string, long> counter in counts) total += counter.Value;
            Assert.That(total, Is.GreaterThan(0),
                "no runtime-created resource was found at all, so this assertion could "
                + "only ever compare zero to zero — IsRuntimeInstance is not working in "
                + "this environment and the leak measurement is not live");
            return counts;
        }

        /// <summary>Every runtime-instance counter that grew, formatted like Snapshot's own.</summary>
        private static string RuntimeGrowth(Dictionary<string, long> before,
            Dictionary<string, long> after, int cycles)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, long> counter in after)
            {
                long start = before.TryGetValue(counter.Key, out long b) ? b : 0;
                long delta = counter.Value - start;
                if (delta <= 0) continue;
                sb.Append($"\n  {counter.Key}: {start} -> {counter.Value}  "
                          + $"(delta {delta:+#;-#;0}, {(double)delta / cycles:0.##}/cycle)");
            }
            return sb.Length == 0 ? "" : sb.ToString();
        }

        /// <summary>
        /// Growth of every counter EXCEPT the ones named — <c>Snapshot.GrowthAgainst</c>
        /// filters the other way round (an allow-list), and an allow-list of counters
        /// to watch is how a counter added later stops being watched by nobody's
        /// decision.
        /// </summary>
        private static string GrowthExcluding(Snapshot after, Snapshot baseline, int cycles,
            string[] excludedKeys)
        {
            var excluded = new HashSet<string>(excludedKeys);
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, long> counter in after.Values)
            {
                if (excluded.Contains(counter.Key)) continue;
                long start = baseline.Values.TryGetValue(counter.Key, out long b) ? b : 0;
                long delta = counter.Value - start;
                if (delta <= 0) continue;
                sb.Append($"\n  {counter.Key}: {start} -> {counter.Value}  "
                          + $"(delta {delta:+#;-#;0}, {(double)delta / cycles:0.##}/cycle)");
            }
            return sb.Length == 0 ? "" : sb.ToString();
        }

        /// <summary>
        /// Every counter that MOVED between two readings, in either direction, over
        /// exactly the quantities the test goes on to assert on.
        /// <para>
        /// The exclusion matters as much here as in the assertion. Settling the
        /// warm-up on <c>Material(all-loaded)</c> — a counter that rises with content
        /// variety and never falls — means a run whose warm-up happens to visit new
        /// systems exhausts <see cref="WarmupCycleCap"/> and fails with "the counters
        /// never held still", which would be a false RED replacing a false green. The
        /// warm-up must settle on what the assertion measures, and on nothing else.
        /// </para>
        /// </summary>
        private static string DriftBetween(Snapshot previous, Snapshot now,
            Dictionary<string, long> runtimeBefore, Dictionary<string, long> runtimeAfter)
        {
            var sb = new StringBuilder();
            var excluded = new HashSet<string>(ResourceCounterKeys(now));
            foreach (KeyValuePair<string, long> counter in now.Values)
            {
                if (excluded.Contains(counter.Key)) continue;
                long start = previous.Values.TryGetValue(counter.Key, out long b) ? b : 0;
                if (counter.Value != start)
                    sb.Append($"\n    {counter.Key}: {start} -> {counter.Value}");
            }
            foreach (KeyValuePair<string, long> counter in runtimeAfter)
            {
                long start = runtimeBefore.TryGetValue(counter.Key, out long b) ? b : 0;
                if (counter.Value != start)
                    sb.Append($"\n    {counter.Key}: {start} -> {counter.Value}");
            }
            return sb.Length == 0 ? "" : sb.ToString();
        }

        /// <summary>
        /// Names what the <c>GameObject</c> and <c>Component</c> counters counted.
        /// <para>
        /// Those two are the only counters in this fixture that could move without
        /// anything saying WHAT moved, and a delta with no name costs a whole run to
        /// turn into a hypothesis. <see cref="MaterialTracker"/> has named its own
        /// additions since it was written; this is the same instrument pointed at
        /// objects, and it exists because "+2 GameObject, +4 Component" was reported
        /// twice tonight with no way to tell which two objects.
        /// </para>
        /// <para>
        /// <c>FirstCycle</c> is the field that settles the argument, exactly as it did
        /// for the materials: an addition at cycle 1 is warm-up that the baseline
        /// missed, an addition every cycle is a leak, and a single addition at cycle
        /// 63 is something that happened once — which is a third thing, and is the
        /// shape this counter has actually shown.
        /// </para>
        /// </summary>
        internal sealed class ObjectTracker
        {
            private readonly HashSet<int> baselineObjects = new HashSet<int>();
            private readonly HashSet<int> baselineComponents = new HashSet<int>();
            private readonly HashSet<int> recorded = new HashSet<int>();

            // References, not descriptions. Describing at OBSERVE time answered the
            // wrong question and answered it 61218 times: a promote/demote cycle
            // materialises a whole world, so "created during the window" is the world,
            // and the two objects that matter were somewhere in a list nobody could
            // read. The leak is what was created AND is still alive at the end, which
            // is only knowable at report time -- so the object is kept and the string
            // is built later, for survivors alone. It is also far cheaper: no path or
            // component list is formatted for the 61216 objects that were correctly
            // destroyed.
            private readonly List<GameObject> created = new List<GameObject>();
            private readonly List<int> createdCycle = new List<int>();
            private readonly List<Component> attached = new List<Component>();
            private readonly List<int> attachedCycle = new List<int>();

            public ObjectTracker()
            {
                foreach (Transform transform in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                    if (transform != null) baselineObjects.Add(transform.gameObject.GetInstanceID());
                foreach (Component component in UnityEngine.Object.FindObjectsOfType<Component>(true))
                    if (component != null) baselineComponents.Add(component.GetInstanceID());
            }

            public void Observe(int cycle)
            {
                foreach (Transform transform in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                {
                    if (transform == null) continue;
                    GameObject go = transform.gameObject;
                    int id = go.GetInstanceID();
                    if (baselineObjects.Contains(id) || !recorded.Add(id)) continue;
                    created.Add(go);
                    createdCycle.Add(cycle);
                }

                // A component added to an object that already existed moves the
                // Component counter without moving the GameObject one, and is a
                // completely different defect from a new object. Reported separately
                // so the two deltas can never be read as one story.
                foreach (Component component in UnityEngine.Object.FindObjectsOfType<Component>(true))
                {
                    if (component == null) continue;
                    int id = component.GetInstanceID();
                    if (baselineComponents.Contains(id) || !recorded.Add(id)) continue;
                    if (!baselineObjects.Contains(component.gameObject.GetInstanceID())) continue;
                    attached.Add(component);
                    attachedCycle.Add(cycle);
                }
            }

            /// <summary>
            /// Its own, rather than the fixture's: a nested type does not inherit the
            /// enclosing type's base members, and a path is two lines.
            /// </summary>
            private static string PathOf(Transform transform)
            {
                if (transform == null) return "<destroyed>";
                var sb = new StringBuilder(transform.name);
                for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                    sb.Insert(0, parent.name + "/");
                return sb.ToString();
            }

            /// <summary>
            /// The objects created inside the measured window that are STILL ALIVE.
            /// <para>
            /// That set is the leak, and it is the only set worth printing: a
            /// promote/demote cycle materialises and destroys a whole world, so
            /// "created during the window" is tens of thousands of objects and says
            /// nothing. Survivors should number exactly the counter delta.
            /// </para>
            /// <para>
            /// When it does not, that is reported rather than glossed, because the two
            /// cases are different defects. Fewer survivors than the delta means
            /// something created BEFORE the window was destroyed inside it — a
            /// disappearance, not a leak, and looking for a leaked object would be
            /// looking for the wrong thing entirely. More survivors than the delta
            /// means something else was destroyed to compensate, and the net figure is
            /// hiding two faults that happen to cancel.
            /// </para>
            /// </summary>
            /// <remarks>
            /// Takes the two deltas rather than the two snapshots: <c>Snapshot</c> is
            /// protected on the base fixture, and an internal method cannot expose a
            /// protected type (CS0051). Passing longs also lets the other fixture reuse
            /// this without inheriting from this one.
            /// </remarks>
            public string Report(long objectDelta, long componentDelta, int maxDetails = 40)
            {
                var sb = new StringBuilder();
                var liveObjects = new List<int>();
                for (int i = 0; i < created.Count; i++)
                    if (created[i] != null) liveObjects.Add(i);
                var liveComponents = new List<int>();
                for (int i = 0; i < attached.Count; i++)
                    if (attached[i] != null) liveComponents.Add(i);

                sb.Append($"\nobjects created during the measured window: {created.Count}, "
                          + $"of which STILL ALIVE at the final snapshot: {liveObjects.Count}");
                sb.Append($"\ncomponents added to pre-existing objects: {attached.Count}, "
                          + $"still alive: {liveComponents.Count}");

                if (liveObjects.Count != objectDelta)
                    sb.Append($"\n  MISMATCH: the GameObject counter moved by {objectDelta} but "
                              + $"{liveObjects.Count} created object(s) survived. The difference is "
                              + "NOT a leaked object — it is something that existed before the "
                              + "window and was destroyed inside it, which is a different defect.");
                if (liveComponents.Count + liveObjects.Count > componentDelta)
                    sb.Append($"\n  note: the Component counter moved by {componentDelta}; a "
                              + "surviving object carries its own components, so these are not "
                              + "independent totals.");

                for (int n = 0; n < liveObjects.Count && n < maxDetails; n++)
                {
                    int i = liveObjects[n];
                    GameObject go = created[i];
                    var types = new StringBuilder();
                    foreach (Component component in go.GetComponents<Component>())
                    {
                        if (types.Length > 0) types.Append(", ");
                        types.Append(component == null ? "<missing script>" : component.GetType().Name);
                    }
                    sb.Append($"\n  SURVIVOR cycle {createdCycle[i]}: '{PathOf(go.transform)}' "
                              + $"scene='{go.scene.name}' active={go.activeInHierarchy} "
                              + $"hideFlags={go.hideFlags} components=[{types}]");
                }
                for (int n = 0; n < liveComponents.Count && n < maxDetails; n++)
                {
                    int i = liveComponents[n];
                    sb.Append($"\n  SURVIVOR cycle {attachedCycle[i]}: "
                              + $"{attached[i].GetType().Name} still attached to the pre-existing "
                              + $"'{PathOf(attached[i].transform)}'");
                }
                return sb.ToString();
            }

        }

        /// <summary>How far one counter moved between two snapshots.</summary>
        private static long CounterDelta(Snapshot baseline, Snapshot after, string key)
        {
            long before = baseline.Values.TryGetValue(key, out long b) ? b : 0;
            long now = after.Values.TryGetValue(key, out long a) ? a : 0;
            return now - before;
        }

        /// <summary>The warm-up's result: the snapshot to measure against, and how it was reached.</summary>
        private sealed class StableBaseline
        {
            public Snapshot Snapshot;

            /// <summary>
            /// Read at the same instant as <see cref="Snapshot"/>. Callers use this
            /// rather than re-reading, so the two halves of the baseline cannot come
            /// from different frames — the asymmetry that produced the RenderTexture
            /// 3 -&gt; 1 decrease, one level down.
            /// </summary>
            public Dictionary<string, long> Runtime;
            public int Cycles;
            public string Trace;

            public string Describe() =>
                $"baseline taken after {Cycles} warm-up cycle(s), the last {StableCycles} of "
                + $"which changed no counter:{Trace}";
        }

        /// <summary>
        /// Runs <paramref name="cycle"/> until the counters stop moving, and takes the
        /// baseline there.
        /// <para>
        /// <paramref name="cycle"/> must be the SAME operation the measured loop
        /// repeats, or stability established here says nothing about the loop that
        /// follows. Each caller passes its measured body minus the tracker call.
        /// </para>
        /// <para>
        /// The failure path is the point of the whole helper. Warming up "until things
        /// settle" is only safe if never settling is louder than settling, so
        /// exhausting <see cref="WarmupCycleCap"/> fails here, with the drift of the
        /// last cycle and the full per-cycle trace — a per-cycle leak is reported by
        /// this method rather than swallowed by it.
        /// </para>
        /// </summary>
        private static IEnumerator WarmUpUntilStable(string what, StableBaseline result,
            System.Func<IEnumerator> cycle)
        {
            var trace = new StringBuilder();
            Snapshot previous = null;
            Dictionary<string, long> previousRuntime = null;
            string drift = null;
            int unchanged = 0;

            for (int i = 1; i <= WarmupCycleCap; i++)
            {
                yield return cycle();

                Snapshot now = Snapshot.Take();
                Dictionary<string, long> runtimeNow = RuntimeResourceCounts();
                if (previous != null)
                {
                    drift = DriftBetween(previous, now, previousRuntime, runtimeNow);
                    trace.Append($"\n  warm-up cycle {i}: "
                                 + (drift.Length == 0 ? "no counter changed" : drift));
                    unchanged = drift.Length == 0 ? unchanged + 1 : 0;
                }
                previous = now;
                previousRuntime = runtimeNow;

                if (unchanged >= StableCycles)
                {
                    result.Snapshot = now;
                    result.Runtime = runtimeNow;
                    result.Cycles = i;
                    result.Trace = trace.ToString();
                    yield break;
                }
            }

            Assert.Fail(
                $"{what}: the counters never held still for {StableCycles} consecutive cycles "
                + $"within {WarmupCycleCap} warm-up cycles, so no baseline exists to measure "
                + "against. Something moves EVERY cycle, which is the per-cycle leak this "
                + $"fixture exists to catch — last cycle's drift:{drift}\nfull trace:{trace}");
        }

        /// <summary>
        /// Begin/End session, repeated. The warm-up runs until the counters hold still
        /// — caches, shared meshes and lazily-built persistent materials are created
        /// on first use by design — and every cycle after that must return every
        /// counter to the value it settled on.
        /// <para>
        /// Release-lane counterpart:
        /// <c>LifecycleOwnershipRegressionTests.OneBeginEndSessionCycle_ReturnsEveryCounterToItsBaseline</c>.
        /// </para>
        /// </summary>
        [UnityTest, Category("Soak"), Timeout(GateTimeoutMilliseconds)]
        public IEnumerator BeginEndSessionCycles_ReturnEveryCounterToItsBaseline()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();

            IEnumerator Cycle()
            {
                adapter.BeginSession();
                yield return null;
                adapter.EndSession();
                yield return DrainDeferredTeardown();
            }

            var stable = new StableBaseline();
            yield return WarmUpUntilStable("BeginSession/EndSession", stable, Cycle);
            Snapshot baseline = stable.Snapshot;
            Dictionary<string, long> runtimeBefore = stable.Runtime;
            Dictionary<int, Material> materialsBefore = LoadedMaterials();
            var materialTracker = new MaterialTracker(materialsBefore);
            var objectTracker = new ObjectTracker();

            for (int i = 0; i < LifecycleCycles; i++)
            {
                adapter.BeginSession();
                yield return null;
                materialTracker.Observe(i + 1);
                objectTracker.Observe(i + 1);
                adapter.EndSession();
                yield return DrainDeferredTeardown();
            }

            Snapshot after = Snapshot.Take();
            Dictionary<string, long> runtimeAfter = RuntimeResourceCounts();
            string growth = GrowthExcluding(after, baseline, LifecycleCycles,
                ResourceCounterKeys(baseline));
            string runtimeLeak = RuntimeGrowth(runtimeBefore, runtimeAfter, LifecycleCycles);

            Assert.That(growth, Is.Empty,
                $"{LifecycleCycles} BeginSession/EndSession cycles did not return to the settled baseline:{growth}"
                + $"\n{stable.Describe()}"
                + $"\nfull baseline was:{baseline.Full()}" + objectTracker.Report(CounterDelta(baseline, after, "GameObject"), CounterDelta(baseline, after, "Component")));
            Assert.That(runtimeLeak, Is.Empty,
                $"{LifecycleCycles} BeginSession/EndSession cycles left runtime-created "
                + $"resources the session never released:{runtimeLeak}"
                + $"\n{stable.Describe()}" + materialTracker.Report());
        }

        /// <summary>
        /// RestartCampaign is the harder path: it disposes and rebuilds inside one
        /// call, so every deferred OnDestroy from the outgoing session lands on the
        /// incoming one.
        /// <para>
        /// Release-lane counterpart:
        /// <c>LifecycleOwnershipRegressionTests.OneRestartCampaignCycle_ReturnsEveryCounterToItsBaseline</c>.
        /// </para>
        /// </summary>
        [UnityTest, Category("Soak"), Timeout(GateTimeoutMilliseconds)]
        public IEnumerator RestartCampaignCycles_ReturnEveryCounterToItsBaseline()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();

            adapter.BeginSession();
            yield return null;

            IEnumerator Cycle()
            {
                adapter.RestartCampaign();
                yield return null;
                yield return DrainDeferredTeardown();
            }

            var stable = new StableBaseline();
            yield return WarmUpUntilStable("RestartCampaign", stable, Cycle);
            Snapshot baseline = stable.Snapshot;
            Dictionary<string, long> runtimeBefore = stable.Runtime;
            Dictionary<int, Material> materialsBefore = LoadedMaterials();
            var materialTracker = new MaterialTracker(materialsBefore);
            var objectTracker = new ObjectTracker();

            for (int i = 0; i < LifecycleCycles; i++)
            {
                adapter.RestartCampaign();
                yield return null;
                materialTracker.Observe(i + 1);
                objectTracker.Observe(i + 1);
                yield return DrainDeferredTeardown();
            }

            Snapshot after = Snapshot.Take();
            Dictionary<string, long> runtimeAfter = RuntimeResourceCounts();
            string growth = GrowthExcluding(after, baseline, LifecycleCycles,
                ResourceCounterKeys(baseline));
            string runtimeLeak = RuntimeGrowth(runtimeBefore, runtimeAfter, LifecycleCycles);

            Assert.That(growth, Is.Empty,
                $"{LifecycleCycles} RestartCampaign cycles did not return to the settled baseline:{growth}"
                + $"\n{stable.Describe()}"
                + $"\nfull baseline was:{baseline.Full()}" + objectTracker.Report(CounterDelta(baseline, after, "GameObject"), CounterDelta(baseline, after, "Component")));
            Assert.That(runtimeLeak, Is.Empty,
                $"{LifecycleCycles} RestartCampaign cycles left runtime-created resources "
                + $"the session never released:{runtimeLeak}"
                + $"\n{stable.Describe()}" + materialTracker.Report());

            adapter.EndSession();
        }

        /// <summary>
        /// Screen open/close, a hundred times. The counters that matter here are
        /// the ownership ones — <c>InputGate.Suppressed</c>, the
        /// <c>ExclusiveModal</c> owner, <c>Time.timeScale</c> and the shell's own
        /// stack depth — and they carry no tolerance whatsoever.
        /// <para>
        /// This is the cheapest leaf in the fixture by a wide margin — the measured
        /// run is 4.8s including the scene load and the session boot, because a
        /// push/pop of an already-built screen allocates nothing. It stays here
        /// anyway: it is a hundred-cycle endurance loop by construction, the release
        /// lane admits leaves by category rather than by stopwatch, and
        /// <c>ShellPushPop_TakesTheGateAndTheModalOnceAndHandsBothBack</c> states the
        /// same ownership contract in two cycles with a named mutation. If the
        /// coordinator would rather the gate carried the hundred directly, this is
        /// the one leaf where that is affordable.
        /// </para>
        /// </summary>
        [UnityTest, Category("Soak"), Timeout(GateTimeoutMilliseconds)]
        public IEnumerator ScreenOpenCloseCycles_LeaveNoInputOrModalOrClockResidue()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            UI.Shell.NebulaUiShell shell = context.Presentation.UiShell;
            UI.Menu.NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            Assert.That(shell, Is.Not.Null, "baseline: the session built a shell");
            Assert.That(menu, Is.Not.Null, "baseline: the session built the command menu");

            shell.Push(menu);
            yield return null;
            shell.Pop();
            yield return null;
            Snapshot baseline = Snapshot.Take();

            for (int i = 0; i < ScreenCycles; i++)
            {
                shell.Push(menu);
                yield return null;
                shell.Pop();
                yield return null;
            }

            Snapshot after = Snapshot.Take();

            string ownership = $"[gate={InputGate.Suppressed} modal={(ExclusiveModal.Owner != null)} "
                + $"stack={shell.StackDepth} timeScale={Time.timeScale:0.###}]";

            Assert.That(InputGate.Suppressed, Is.False,
                $"after {ScreenCycles} open/close cycles the global input gate is still raised {ownership} — "
                + "NebulaUiShell.CaptureInput/ReleaseInput is a save/restore pair, so an unbalanced pop leaves it up");
            Assert.That(ExclusiveModal.Owner, Is.Null,
                $"after {ScreenCycles} open/close cycles the exclusive-modal owner is still held {ownership}; "
                + "every future modal would be refused for the rest of the session");
            Assert.That(shell.StackDepth, Is.Zero,
                $"the shell's screen stack did not unwind {ownership}");
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.0001f),
                $"a screen moved the clock {ownership} — no shell is supposed to write Time.timeScale any more (3fa9dda)");

            string growth = after.GrowthAgainst(baseline, ScreenCycles);
            Assert.That(growth, Is.Empty,
                $"{ScreenCycles} screen open/close cycles grew:{growth}");

            adapter.EndSession();
        }

        /// <summary>
        /// Full-system promote/demote, repeated on the real streaming director.
        /// A demotion captures a delta and tears the system down; the promotion
        /// rebuilds it. Nothing campaign-scoped may move.
        /// <para>
        /// Release-lane counterpart:
        /// <c>LifecycleOwnershipRegressionTests.OneStreamingPromoteDemoteRoundTrip_ReturnsEveryCounterToItsBaseline</c>.
        /// </para>
        /// </summary>
        [UnityTest, Category("Soak"), Timeout(GateTimeoutMilliseconds)]
        public IEnumerator StreamingPromoteDemoteCycles_ReturnEveryCounterToItsBaseline()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            SystemStreamingDirector streaming = context.Streaming;
            SystemManifest home = StarSystemRegistry.Manifests.ResolveSystem(streaming.FullSystemId);
            SystemManifest foreign = FirstForeignManifest(context);
            Assert.That(home, Is.Not.Null, "baseline: the Full system resolves a manifest");

            PlaceShipAtAbsolute(context, foreign.AbsoluteCenter);
            yield return null;
            yield return null;
            Assert.That(streaming.FullSystemId, Is.EqualTo(foreign.SystemId), "warm/full promotion did not reach the foreign system");
            PlaceShipAtAbsolute(context, home.AbsoluteCenter);
            yield return null;
            yield return null;
            Assert.That(streaming.FullSystemId, Is.EqualTo(home.SystemId), "demote/promote round trip did not return home");
            Snapshot baseline = Snapshot.Take();
            Dictionary<int, Material> materialsBefore = LoadedMaterials();
            var materialTracker = new MaterialTracker(materialsBefore);
            var objectTracker = new ObjectTracker();

            for (int i = 0; i < LifecycleCycles; i++)
            {
                PlaceShipAtAbsolute(context, foreign.AbsoluteCenter);
                yield return null;
                yield return null;
                Assert.That(streaming.FullSystemId, Is.EqualTo(foreign.SystemId), $"cycle {i + 1}: foreign promotion failed");
                materialTracker.Observe(i + 1);
                objectTracker.Observe(i + 1);
                PlaceShipAtAbsolute(context, home.AbsoluteCenter);
                yield return null;
                yield return null;
                Assert.That(streaming.FullSystemId, Is.EqualTo(home.SystemId), $"cycle {i + 1}: home promotion failed");
            }

            Snapshot after = Snapshot.Take();
            string growth = after.GrowthAgainst(baseline, LifecycleCycles);

            Assert.That(growth, Is.Empty,
                $"{LifecycleCycles} Full-system rebuild cycles did not return to baseline:{growth}"
                + $"\nfull baseline was:{baseline.Full()}" + materialTracker.Report() + objectTracker.Report(CounterDelta(baseline, after, "GameObject"), CounterDelta(baseline, after, "Component")));

            adapter.EndSession();
        }

        /// <summary>
        /// Separate 100-cycle production-path encounter/director rebind scenario.
        /// CombatLightingBridge observes <c>SystemStreamingDirector.CombatEncounter</c>
        /// and rebinds in LateUpdate. The older CombatAnimationBridge captures only
        /// the start encounter and has no rebind path; this test measures both the
        /// old/new director subscriber counts and every participant/event/director
        /// counter independently of the general streaming leak measurement.
        /// <para>
        /// Release-lane counterpart:
        /// <c>LifecycleOwnershipRegressionTests.OneEncounterDirectorRebind_LeavesExactlyTwoLiveBindings</c>.
        /// </para>
        /// </summary>
        [UnityTest, Category("Soak"), Timeout(GateTimeoutMilliseconds)]
        public IEnumerator EncounterDirectorRebindCycles_KeepExactlyOneLiveBindingAndNoGrowth()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            SystemStreamingDirector streaming = context.Streaming;
            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(streaming.FullSystemId);
            Assert.That(manifest, Is.Not.Null, "baseline: the Full system resolves a manifest");

            streaming.ResetAndBootFull(manifest);
            yield return null;
            yield return null;
            CombatEncounterDirector baselineEncounter = streaming.CombatEncounter;
            Assert.That(baselineEncounter, Is.Not.Null, "baseline: rebuild produced an encounter director");
            int baselineSubscribers = InvocationCount(baselineEncounter, nameof(CombatEncounterDirector.ProjectileResolved));
            Assert.That(baselineSubscribers, Is.GreaterThanOrEqualTo(1),
                "baseline: at least CombatLightingBridge must have rebound to the replacement encounter");
            // Reached by exactly the sequence one measured cycle is: a boot, its two
            // frames, then the drain. The baseline used to be taken here WITHOUT the
            // drain while `after` took it WITH one, so the baseline counted objects
            // still queued for deferred destruction and the comparison read as a
            // DECREASE — RenderTexture 3 -> 1, measured. Nothing was being destroyed
            // that should have survived; the two sides were simply read at different
            // instants. Both sides now end on the same drain, which is why neither is
            // a frame count chosen here.
            yield return DrainDeferredTeardown();
            Snapshot baseline = Snapshot.Take();

            var objectTracker = new ObjectTracker();
            CombatEncounterDirector previous = baselineEncounter;
            var transitions = new StringBuilder();
            var bindingFailures = new List<string>();
            for (int i = 0; i < LifecycleCycles; i++)
            {
                objectTracker.Observe(i + 1);
                streaming.ResetAndBootFull(manifest);
                CombatEncounterDirector current = streaming.CombatEncounter;
                Assert.That(current, Is.Not.Null.And.Not.SameAs(previous), $"cycle {i + 1}: encounter director was not replaced");
                int rightAfter = InvocationCount(current, nameof(CombatEncounterDirector.ProjectileResolved));
                yield return null; // CombatLightingBridge.LateUpdate performs its production rebind
                int afterLateUpdate = InvocationCount(current, nameof(CombatEncounterDirector.ProjectileResolved));
                yield return null;
                if (i < 10 || i == LifecycleCycles - 1)
                    transitions.Append($"\n  cycle {i + 1}: new encounter subscribers {rightAfter} -> {afterLateUpdate}");
                if (afterLateUpdate != 2)
                    bindingFailures.Add($"cycle {i + 1}: {afterLateUpdate} subscriber(s), expected CombatLightingBridge + CombatAnimationBridge");
                previous = current;
            }

            // The final replacement destroys the outgoing encounter hierarchy at the
            // frame boundary. The same drain the baseline ended on, so the two
            // snapshots are read at the same point in a cycle.
            yield return DrainDeferredTeardown();
            Snapshot after = Snapshot.Take();
            string nonResourceDrift = after.DifferencesAgainst(baseline, LifecycleCycles,
                ResourceCounterKeys(baseline));
            Assert.That(bindingFailures, Is.Empty,
                $"{LifecycleCycles} encounter/director replacements completed, but bindings were incomplete: "
                + string.Join("; ", bindingFailures)
                + $"\nparticipant/event/director/object drift (Materials/Textures intentionally excluded here):{nonResourceDrift}"
                + transitions + $"\nfull baseline was:{baseline.Full()}" + objectTracker.Report(CounterDelta(baseline, after, "GameObject"), CounterDelta(baseline, after, "Component")));
            Assert.That(nonResourceDrift, Is.Empty,
                $"{LifecycleCycles} encounter/director replacements changed participant/event/director or object counters:{nonResourceDrift}"
                + transitions + $"\nfull baseline was:{baseline.Full()}" + objectTracker.Report(CounterDelta(baseline, after, "GameObject"), CounterDelta(baseline, after, "Component")));

            adapter.EndSession();
        }

        /// <summary>
        /// Save/load, repeated through the real director. A manual load reboots
        /// streaming without going through composition, which is the one path that
        /// rebuilds the world while the session's directors stay alive.
        /// <para>
        /// Release-lane counterpart:
        /// <c>LifecycleOwnershipRegressionTests.OneSaveLoadCycle_ReturnsEveryCounterToItsBaseline</c>.
        /// </para>
        /// </summary>
        [UnityTest, Category("Soak"), Timeout(GateTimeoutMilliseconds)]
        public IEnumerator SaveLoadCycles_ReturnEveryCounterToItsBaseline()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            ExtractionDirector extraction = context.ExtractionDirector;

            IEnumerator Cycle()
            {
                extraction.SaveNow("AUDIT");
                extraction.LoadNow();
                yield return null;
                yield return null;
            }

            var stable = new StableBaseline();
            yield return WarmUpUntilStable("save/load", stable, Cycle);
            Snapshot baseline = stable.Snapshot;
            Dictionary<string, long> runtimeBefore = stable.Runtime;
            Dictionary<int, Material> materialsBefore = LoadedMaterials();
            var materialTracker = new MaterialTracker(materialsBefore);
            var objectTracker = new ObjectTracker();

            for (int i = 0; i < LifecycleCycles; i++)
            {
                extraction.SaveNow("AUDIT");
                extraction.LoadNow();
                yield return null;
                materialTracker.Observe(i + 1);
                objectTracker.Observe(i + 1);
                yield return null;
            }

            Snapshot after = Snapshot.Take();
            Dictionary<string, long> runtimeAfter = RuntimeResourceCounts();
            string growth = GrowthExcluding(after, baseline, LifecycleCycles,
                ResourceCounterKeys(baseline));
            string runtimeLeak = RuntimeGrowth(runtimeBefore, runtimeAfter, LifecycleCycles);

            Assert.That(growth, Is.Empty,
                $"{LifecycleCycles} save/load cycles did not return to the settled baseline:{growth}"
                + $"\n{stable.Describe()}"
                + $"\nfull baseline was:{baseline.Full()}" + objectTracker.Report(CounterDelta(baseline, after, "GameObject"), CounterDelta(baseline, after, "Component")));
            Assert.That(runtimeLeak, Is.Empty,
                $"{LifecycleCycles} save/load cycles left runtime-created resources the "
                + $"session never released:{runtimeLeak}"
                + $"\n{stable.Describe()}" + materialTracker.Report());

            adapter.EndSession();
        }
    }
}
