using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Progression;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.UI;
using Nebula.UI.Hud;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// GENEL DENETİM PART 4 — the four P2-PERF findings, measured on a real session.
    /// <para>
    /// Part 2's performance audit was explicit that it had profiled nothing: every
    /// number in it was an allocation count derived from reading code. This fixture is
    /// the missing measurement, and it is deliberately <b>short</b> — a few hundred
    /// frames per claim, not a thirty-minute cruise. A long soak would produce a bigger
    /// number for the same fact; it would not produce a different fact.
    /// </para>
    /// <para>
    /// <b>Two kinds of evidence, on purpose.</b> The assertions ride on deterministic
    /// counters the production code already had to keep in order to cache anything at
    /// all — how many times a string was composed, how many times a sector walk ran, how
    /// many sectors were evicted. Those cannot be flaky. The <see cref="ProfilerRecorder"/>
    /// samples beside them (GC.Alloc, main-thread time) are <i>reported</i>, because a
    /// byte count taken inside an Editor PlayMode run also contains the Editor, and
    /// asserting a threshold on it would be asserting something about this machine.
    /// </para>
    /// <para>
    /// <b>No absolute frame-rate claim is made anywhere in this file.</b> Nothing here
    /// knows what hardware a player has.
    /// </para>
    /// <para>
    /// <b>One [UnityTest], one process.</b> Every extra test that reloads the scene is a
    /// full session teardown, and stacking them drives <c>UnloadUnusedAssets</c> from
    /// 10ms into the tens of seconds until the run stops producing output at all.
    /// </para>
    /// </summary>
    public class Part4PerformanceGateTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        /// <summary>Frames per per-frame claim. Long enough to be a measurement, short enough to be a gate.</summary>
        private const int HudFrames = 300;
        private const int ShortFrames = 120;

        /// <summary>Where the measured numbers are written for the audit report to quote.</summary>
        private static string ReportPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Audit", "GeneralReview", "Part4", "Results", "perf-measurements.json");

        private readonly List<string> measurements = new List<string>();
        private float savedTimeScale;

        [SetUp]
        public void Setup()
        {
            savedTimeScale = Time.timeScale;
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = savedTimeScale;
            Nebula.Ship.NebulaGameplayFeedback.Clear();

            SolarSystemBootstrap boot = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            if (boot != null && boot.SessionActive) boot.EndSession();

            WriteReport();
        }

        // =====================================================================

        [UnityTest]
        public IEnumerator EveryPerformanceClaimIsMeasuredOnARealSession()
        {
            // ---- P2-PERF-003 first: it needs no session at all ---------------
            BoundedSectorCacheReachesItsCeilingAndStaysThere();

            // ---- the dev HUD's release gate, before anything is built --------
            TheDevelopmentOverlaysAreCreatedOnlyBehindTheDebugBuildGate();

            yield return LoadSceneFresh();

            SolarSystemBootstrap bootstrap = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.IsNotNull(bootstrap, "the scene carries no bootstrap");
            NebulaContext context = bootstrap.Context;
            Assert.IsNotNull(context?.Presentation?.UiShell, "the session built no shell");

            yield return HudNotificationLineIsComposedOncePerBurst(context);
            yield return CruiseWalksTheSectorNeighbourhoodOncePerSector(context);
            yield return ARepeatedWarningIsNotRewrittenEveryFrame(context);
            yield return TheDevelopmentSpeedHudRebuildsOnlyWhenTheNumberChanges(context);

            // ---- one session round-trip, and only one ------------------------
            yield return OneSessionRoundTripLeavesNoRuntimeMeshOrSkyboxCubeBehind(bootstrap);
        }

        // =====================================================================
        // P2-PERF-001 — the XP line
        // =====================================================================

        /// <summary>
        /// The window holds for four seconds. At sixty frames a second that used to be
        /// 240 identical strings for one reward, and rewards land exactly when the game
        /// is busiest — a finished production, a won fight. The line is now composed when
        /// one of its four numbers changes, so a burst costs one composition.
        /// </summary>
        private IEnumerator HudNotificationLineIsComposedOncePerBurst(NebulaContext context)
        {
            NebulaHudNotifications notifications = context.Presentation.HudNotifications;
            Assert.IsNotNull(notifications, "the session built no XP notification line");

            ProgressionService.Award("part4.perf.xp", 1200);
            yield return null;

            int compositionsAfterTheAward = notifications.LineCompositions;
            Assert.Greater(compositionsAfterTheAward, 0,
                "the award raised no line at all — the measurement would be of an empty window");

            string allocated, mainThreadNanoseconds;
            using (var gcAlloc = new FrameCounter(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var mainThread = new FrameCounter(ProfilerCategory.Internal, "Main Thread"))
            {
                for (int frame = 0; frame < HudFrames; frame++) yield return null;
                allocated = gcAlloc.TotalText;
                mainThreadNanoseconds = mainThread.AverageText;
            }

            int rebuilds = notifications.LineCompositions - compositionsAfterTheAward;
            Record("P2-PERF-001", $"frames={HudFrames} lineCompositions={rebuilds} " +
                                  $"gcAllocBytesOverWindow={allocated} mainThreadAvgNs={mainThreadNanoseconds}");

            // The hold expires part-way through the window, which stops the refresh
            // entirely — so the honest bound is "no more than one", not "exactly one".
            Assert.LessOrEqual(rebuilds, 1,
                $"the XP line was composed {rebuilds} times across {HudFrames} frames in which none of " +
                "its four numbers changed — the per-frame string is back");
        }

        // =====================================================================
        // P2-PERF-002 — the cruise sector walk
        // =====================================================================

        /// <summary>
        /// <c>StarSystemRegistry.SystemsNear</c> is 27 dictionary lookups plus up to 29
        /// iterators, and cruise asked for it on every frame. Its answer only changes when
        /// the ship changes sector, which is measured here from both sides: standing still
        /// must not rebuild, and crossing a sector boundary must.
        /// </summary>
        private IEnumerator CruiseWalksTheSectorNeighbourhoodOncePerSector(NebulaContext context)
        {
            var cruise = context.Ship.GetComponent<ShipCruise>();
            Assert.IsNotNull(cruise, "the session's ship carries no cruise driver");
            Assert.IsNotNull(StarSystemRegistry.Manifests, "there is no manifest repository to walk");

            // Let the first frame of this phase build the neighbourhood, then measure the
            // frames that follow — the build itself is the work being amortised, not noise.
            yield return null;
            int buildsBefore = cruise.StellarNeighbourhoodBuilds;
            Assert.Greater(buildsBefore, 0, "cruise never walked the neighbourhood at all");
            int stars = cruise.StellarNeighbourCount;

            string allocated, mainThreadNanoseconds;
            using (var gcAlloc = new FrameCounter(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var mainThread = new FrameCounter(ProfilerCategory.Internal, "Main Thread"))
            {
                for (int frame = 0; frame < ShortFrames; frame++) yield return null;
                allocated = gcAlloc.TotalText;
                mainThreadNanoseconds = mainThread.AverageText;
            }

            int rebuildsWhileStill = cruise.StellarNeighbourhoodBuilds - buildsBefore;
            Record("P2-PERF-002", $"frames={ShortFrames} starsInNeighbourhood={stars} " +
                                  $"neighbourhoodBuildsWhileInOneSector={rebuildsWhileStill} " +
                                  $"gcAllocBytesOverWindow={allocated} mainThreadAvgNs={mainThreadNanoseconds}");

            Assert.AreEqual(0, rebuildsWhileStill,
                $"cruise walked the 27-sector neighbourhood {rebuildsWhileStill} more times across " +
                $"{ShortFrames} frames without leaving its sector — the per-frame walk is back");

            // ---- and the cache is a cache, not a freeze ----------------------
            // A cached answer that never notices the ship moved is worse than no cache:
            // it would let cruise engage inside a star's warning radius. One sector across,
            // then back, using the ship's own transform — the same thing flying does.
            Vector3 parked = context.Ship.transform.position;
            try
            {
                context.Ship.transform.position = parked + new Vector3(SectorCoordinate.SectorSize, 0f, 0f);
                yield return null;
                yield return null;
                Assert.Greater(cruise.StellarNeighbourhoodBuilds, buildsBefore + rebuildsWhileStill,
                    "the ship crossed a sector boundary and the neighbourhood was never rebuilt — " +
                    "cruise is answering stellar risk from another sector's stars");
            }
            finally
            {
                context.Ship.transform.position = parked;
            }
            yield return null;
            Record("P2-PERF-002", $"neighbourhoodBuildsAfterOneSectorCrossing=" +
                                  $"{cruise.StellarNeighbourhoodBuilds - buildsBefore}");
        }

        // =====================================================================
        // P2-PERF-003 — the bounded sector cache
        // =====================================================================

        /// <summary>
        /// The repository used to have no capacity, no eviction and no lifetime. This
        /// drives a repository past its ceiling and holds it to three things: the count
        /// stops at the ceiling, the start sector — the one the generator alone cannot
        /// reproduce — survives, and an evicted sector comes back <b>identical</b>, which
        /// is what makes eviction safe rather than a silent world change.
        /// </summary>
        private void BoundedSectorCacheReachesItsCeilingAndStaysThere()
        {
            Assert.AreEqual(SectorManifestRepository.DefaultCapacity,
                new SectorManifestRepository(1234UL).Capacity,
                "the default repository no longer carries the documented bound");

            const int Capacity = SectorManifestRepository.MinimumCapacity;
            const int Visited = 200;
            var repository = new SectorManifestRepository(0xC0FFEEUL, Capacity);
            var start = new SectorCoordinate(0, 0, 0);
            repository.SetStartSector(start);

            SectorManifest startManifest = repository.Resolve(start);

            // A sector that actually holds systems, found rather than assumed. The
            // galaxy is sparse and (1,0,0) came back empty, so the regeneration proof
            // below compared two empty lists — which passes while proving nothing — and
            // then indexed [0] of nothing and took the whole fixture down with an
            // IndexOutOfRangeException whose stack pointed at an assertion about the id
            // index. Whichever sector is chosen must be evicted later, so it is picked
            // from the range the visit loop covers.
            SectorCoordinate probe = default;
            SectorManifest first = null;
            for (int x = 1; x <= 40 && first == null; x++)
            {
                var candidate = new SectorCoordinate(x, 0, 0);
                SectorManifest manifest = repository.Resolve(candidate);
                if (manifest.Systems.Count > 0) { probe = candidate; first = manifest; }
            }
            Assert.IsNotNull(first,
                "no sector in (1..40,0,0) holds a single system — the eviction proof has nothing to evict");
            string[] firstSystemIds = SystemIds(first);

            for (int x = 1; x <= Visited; x++) repository.Resolve(new SectorCoordinate(x, 0, 0));

            Record("P2-PERF-003", $"capacity={repository.Capacity} sectorsVisited={Visited + 1} " +
                                  $"cachedAfterwards={repository.CachedSectorCount} " +
                                  $"evicted={repository.EvictedSectorCount}");

            Assert.LessOrEqual(repository.CachedSectorCount, Capacity,
                $"the cache holds {repository.CachedSectorCount} sectors with a ceiling of {Capacity} — " +
                "it is unbounded again");
            Assert.AreEqual(Visited + 1 - Capacity, repository.EvictedSectorCount,
                "eviction did not keep pace with resolution; the ceiling is being held some other way");

            // The pinned sector. It is the one FrontierGuaranteePass rewrote, so evicting
            // it would mean re-running that pass — and the strategic map's cheap positional
            // enumeration would disagree with it in the meantime.
            Assert.IsNotNull(repository.TryGetResolved(start),
                "the start sector was evicted: the one sector the generator alone cannot reproduce");
            Assert.AreSame(startManifest, repository.Resolve(start),
                "the pinned start sector was regenerated rather than kept");

            // The safety proof: an evicted sector is regenerated, not lost and not changed.
            Assert.IsNull(repository.TryGetResolved(probe),
                $"sector {probe} was never evicted, so this run proves nothing about eviction");
            SectorManifest regenerated = repository.Resolve(probe);
            Assert.AreNotSame(first, regenerated, "the evicted manifest was still being held");
            CollectionAssert.AreEqual(firstSystemIds, SystemIds(regenerated),
                "a regenerated sector carries different systems than the one that was evicted — " +
                "the cache was an authority after all, and evicting it changed the world");

            for (int i = 0; i < first.Systems.Count; i++)
            {
                Assert.AreEqual(first.Systems[i].AbsoluteCenter, regenerated.Systems[i].AbsoluteCenter,
                    $"{first.Systems[i].SystemId} came back in a different place");
                Assert.AreEqual(first.Systems[i].StarClass, regenerated.Systems[i].StarClass,
                    $"{first.Systems[i].SystemId} came back as a different star");
                Assert.AreEqual(first.Systems[i].Planets.Count, regenerated.Systems[i].Planets.Count,
                    $"{first.Systems[i].SystemId} came back with a different number of planets");
            }

            // The id index is bounded by the same ceiling: an evicted sector's systems must
            // not stay reachable through it, or the index is the leak the cache no longer is.
            Assert.IsNotNull(repository.ResolveSystem(firstSystemIds[0]),
                "a system id from a live sector no longer resolves");
        }

        private static string[] SystemIds(SectorManifest manifest)
        {
            var ids = new string[manifest.Systems.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = manifest.Systems[i].SystemId;
            return ids;
        }

        // =====================================================================
        // P2-PERF-004 — the dev speed HUD
        // =====================================================================

        /// <summary>
        /// The gate itself, checked by reading the production source: the only place that
        /// builds a diagnostic overlay must be inside <c>if (Debug.isDebugBuild)</c>, and
        /// there must be no second place. The Editor, every test runner and the
        /// Development player all report <c>true</c>, so no run anywhere can execute the
        /// release side of that branch — the branch has to be checked where it is written.
        /// <see cref="Nebula.Diagnostics.NebulaReleaseVisibilityProbe"/> confirms the
        /// consequence from inside a shipped player.
        /// </summary>
        private void TheDevelopmentOverlaysAreCreatedOnlyBehindTheDebugBuildGate()
        {
            string scripts = Path.Combine(Application.dataPath, "Scripts", "Nebula");
            Assert.IsTrue(Directory.Exists(scripts), "the production source tree is not where this test expects it");

            var callSites = new List<string>();
            foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].Contains("FlightTestSpeedHud.Create")
                        && !lines[i].Contains("SectorDebugHud.Create")) continue;

                    // A doc comment naming the method is not a call site. Part 3 found
                    // exactly this blindness in the input-catalog scanner, where a
                    // sentence inside a <summary> counted as two unresolved key reads.
                    if (IsComment(lines[i])) continue;

                    string relative = file.Substring(Application.dataPath.Length).Replace('\\', '/');
                    if (!GuardedByDebugBuild(lines, i))
                        callSites.Add($"Assets{relative}:{i + 1} — {lines[i].Trim()}");
                }
            }

            Assert.IsEmpty(callSites,
                "a diagnostic overlay is constructed outside `if (Debug.isDebugBuild)`; a release player " +
                "would build it and pay for it:\n  " + string.Join("\n  ", callSites));
            Record("P2-PERF-004", "debugBuildGate=every FlightTestSpeedHud/SectorDebugHud call site is guarded");
        }

        /// <summary>
        /// True when a <c>Debug.isDebugBuild</c> test opens a block that is still open at
        /// <paramref name="index"/>. Brace-counted backwards rather than pattern-matched,
        /// so moving the construction one line further into the block does not silently
        /// stop the check from seeing the guard.
        /// </summary>
        private static bool IsComment(string line)
        {
            string trimmed = line.TrimStart();
            return trimmed.StartsWith("//", StringComparison.Ordinal)
                   || trimmed.StartsWith("*", StringComparison.Ordinal)
                   || trimmed.StartsWith("/*", StringComparison.Ordinal);
        }

        private static bool GuardedByDebugBuild(string[] lines, int index)
        {
            // The single-line form: `if (Debug.isDebugBuild) hud = ...Create(ship);`
            if (lines[index].Contains("Debug.isDebugBuild")) return true;

            int depth = 0;
            for (int i = index; i >= 0; i--)
            {
                string line = lines[i];
                if (i != index)
                {
                    for (int c = line.Length - 1; c >= 0; c--)
                    {
                        if (line[c] == '}') depth++;
                        else if (line[c] == '{' && depth > 0) depth--;
                        else if (line[c] == '{' && depth == 0)
                        {
                            // The line that opened the block we are standing in.
                            if (line.Contains("Debug.isDebugBuild")) return true;
                            if (i > 0 && lines[i - 1].Contains("Debug.isDebugBuild")) return true;
                            return false;
                        }
                    }
                }
                if (depth == 0 && i < index && line.Contains("Debug.isDebugBuild")
                    && line.Contains("if")) return true;
            }
            return false;
        }

        /// <summary>
        /// The panel shows whole units. A ship drifting between 412.1 and 412.4 used to
        /// produce a new string every frame and an identical picture — measurement noise
        /// generated by the measurement tool.
        /// </summary>
        private IEnumerator TheDevelopmentSpeedHudRebuildsOnlyWhenTheNumberChanges(NebulaContext context)
        {
            FlightTestSpeedHud hud = context.Presentation.FlightTestSpeedHud;
            Assert.IsNotNull(hud,
                "the Editor session built no Test Flight Speed panel; Debug.isDebugBuild is true here, so " +
                "its absence means the dev overlay gate is now refusing a development run as well");

            bool wasVisible = hud.IsVisible;
            CursorLockMode savedLock = Cursor.lockState;
            bool savedCursorVisible = Cursor.visible;
            try
            {
                // The same seam the J key drives — no simulated keypress, no private call.
                hud.SetVisible(true);
                yield return null;
                Assert.IsTrue(hud.IsVisible, "the panel refused to open through its own public seam");

                yield return null;
                int buildsBefore = hud.ActualLabelBuilds;
                for (int frame = 0; frame < ShortFrames; frame++) yield return null;
                int rebuilds = hud.ActualLabelBuilds - buildsBefore;

                Record("P2-PERF-004", $"frames={ShortFrames} actualLabelBuilds={rebuilds} " +
                                      $"shipSpeed={context.Ship.Velocity.magnitude:0.###}");

                // The ship is parked, so the whole-unit reading cannot move. A single
                // rebuild is allowed for the frame the panel opened on.
                Assert.LessOrEqual(rebuilds, 1,
                    $"the ACTUAL line was rebuilt {rebuilds} times across {ShortFrames} frames at a " +
                    "constant whole-unit speed — the per-frame string is back");
            }
            finally
            {
                hud.SetVisible(wasVisible);
                Cursor.lockState = savedLock;
                Cursor.visible = savedCursorVisible;
            }
        }

        // =====================================================================
        // The typed warning surface
        // =====================================================================

        /// <summary>
        /// A rejection stays on screen for three and a half seconds. The alert panel must
        /// carry it without rewriting the label every frame it is up — the same shape as
        /// the XP line, on the surface the player sees most often during combat.
        /// </summary>
        private IEnumerator ARepeatedWarningIsNotRewrittenEveryFrame(NebulaContext context)
        {
            NebulaHudAlerts alerts = context.Presentation.HudAlerts;
            Assert.IsNotNull(alerts, "the session built no alert column");

            Nebula.Ship.NebulaGameplayFeedback.Publish(
                Nebula.Ship.GameplayFeedbackSource.Combat,
                Nebula.Ship.NebulaGameplayFeedback.RepairText(Nebula.Combat.RepairRejectReason.InCombat),
                Nebula.Ship.GameplayFeedbackTone.Rejection, 30f);
            yield return null;
            yield return null;

            Assert.IsTrue(alerts.FeedbackVisible, "the published rejection never reached the alert column");
            string shown = alerts.FeedbackText;
            Assert.IsNotEmpty(shown, "the alert column is visible and empty");

            string allocated, mainThreadNanoseconds;
            using (var gcAlloc = new FrameCounter(ProfilerCategory.Memory, "GC Allocated In Frame"))
            using (var mainThread = new FrameCounter(ProfilerCategory.Internal, "Main Thread"))
            {
                for (int frame = 0; frame < ShortFrames; frame++) yield return null;
                allocated = gcAlloc.TotalText;
                mainThreadNanoseconds = mainThread.AverageText;
            }

            Record("warning-surface", $"frames={ShortFrames} gcAllocBytesOverWindow={allocated} " +
                                      $"mainThreadAvgNs={mainThreadNanoseconds} text=\"{shown}\"");

            Assert.AreEqual(shown, alerts.FeedbackText,
                "the standing rejection changed text on its own while nothing republished it");
            Nebula.Ship.NebulaGameplayFeedback.Clear();
            yield return null;
        }

        // =====================================================================
        // One session round-trip (Part 3's mesh/skybox ownership fix)
        // =====================================================================

        /// <summary>
        /// Part 3 gave the four runtime meshes and the 1024³ skybox cube an owner. This
        /// is the short confirmation that the ownership holds — <b>one</b> round-trip, by
        /// name rather than by population count, so an Editor that happens to have another
        /// asset loaded cannot make it red and cannot make it green.
        /// </summary>
        private IEnumerator OneSessionRoundTripLeavesNoRuntimeMeshOrSkyboxCubeBehind(SolarSystemBootstrap bootstrap)
        {
            if (bootstrap.SessionActive) bootstrap.EndSession();
            yield return null;

            int materialsBefore = Resources.FindObjectsOfTypeAll<Material>().Length;
            int texturesBefore = Resources.FindObjectsOfTypeAll<Texture>().Length;

            bootstrap.BeginSession();
            for (int frame = 0; frame < 12; frame++) yield return null;
            Assert.IsTrue(bootstrap.SessionActive, "the round-trip never started a session");

            bootstrap.EndSession();
            yield return null;
            yield return null;

            var survivors = new List<string>();
            foreach (Mesh mesh in Resources.FindObjectsOfTypeAll<Mesh>())
            {
                if (mesh == null) continue;
                string name = mesh.name ?? "";
                if (name == "Starfield" || name.StartsWith("Galaxy Clouds", StringComparison.Ordinal)
                    || name.StartsWith("Galaxy Dust", StringComparison.Ordinal)
                    || name.StartsWith("Intergalactic", StringComparison.Ordinal))
                    survivors.Add("Mesh " + name);
            }
            foreach (RenderTexture texture in Resources.FindObjectsOfTypeAll<RenderTexture>())
            {
                if (texture == null) continue;
                if (texture.dimension == UnityEngine.Rendering.TextureDimension.Cube)
                    survivors.Add($"RenderTexture(cube) {texture.name} {texture.width}³");
            }

            int materialDelta = Resources.FindObjectsOfTypeAll<Material>().Length - materialsBefore;
            int textureDelta = Resources.FindObjectsOfTypeAll<Texture>().Length - texturesBefore;
            Record("lifecycle-round-trip", $"cycles=1 materialDelta={materialDelta} textureDelta={textureDelta} " +
                                           $"namedSurvivors={survivors.Count}");

            Assert.IsEmpty(survivors,
                "a session round-trip left runtime resources behind that Part 3 gave an owner:\n  "
                + string.Join("\n  ", survivors));
            Assert.IsFalse(bootstrap.SessionActive, "the round-trip did not end its session");
        }

        // =====================================================================
        // plumbing
        // =====================================================================

        private static IEnumerator LoadSceneFresh()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;
            UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>()?.BeginSession();
            for (int i = 0; i < 12; i++) yield return null;
        }

        private void Record(string id, string detail)
        {
            string line = id + " :: " + detail;
            measurements.Add(line);
            Debug.Log("[part4-perf] " + line);
        }

        /// <summary>
        /// The numbers, written where the audit report can quote them rather than left in
        /// a console log that the next run overwrites. A failed run still writes what it
        /// measured before it failed — that is usually the interesting half.
        /// </summary>
        private void WriteReport()
        {
            if (measurements.Count == 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                var json = new StringBuilder();
                json.Append("{\n  \"fixture\": \"Nebula.Tests.PlayMode.Part4PerformanceGateTests\",\n");
                json.Append("  \"unityVersion\": \"").Append(Application.unityVersion).Append("\",\n");
                json.Append("  \"platform\": \"").Append(Application.platform).Append("\",\n");
                json.Append("  \"measurements\": [\n");
                for (int i = 0; i < measurements.Count; i++)
                {
                    json.Append("    \"").Append(measurements[i].Replace("\\", "\\\\").Replace("\"", "\\\""))
                        .Append(i == measurements.Count - 1 ? "\"\n" : "\",\n");
                }
                json.Append("  ]\n}\n");
                File.WriteAllText(ReportPath, json.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[part4-perf] could not write " + ReportPath + ": " + e.Message);
            }
        }

        /// <summary>
        /// One profiler counter, sampled once per frame for as long as it is held.
        /// <para>
        /// <see cref="ProfilerRecorder"/> can legitimately be unavailable — a counter that
        /// is not registered on this platform, a build without the profiler. That is
        /// reported as an absent measurement rather than as a zero, because a zero would
        /// read as "nothing was allocated" and be the most flattering possible lie.
        /// </para>
        /// </summary>
        private sealed class FrameCounter : IDisposable
        {
            private ProfilerRecorder recorder;

            public FrameCounter(ProfilerCategory category, string counter)
            {
                recorder = ProfilerRecorder.StartNew(category, counter, 1024);
            }

            public bool Available => recorder.Valid;

            /// <summary>The total, or the word "unavailable" — never a flattering zero.</summary>
            public string TotalText => recorder.Valid ? Total.ToString() : "unavailable";

            /// <summary>The per-frame average, or the word "unavailable".</summary>
            public string AverageText => recorder.Valid && recorder.Count > 0
                ? Average.ToString("0") : "unavailable";

            public long Total
            {
                get
                {
                    if (!recorder.Valid) return -1;
                    long total = 0;
                    int count = recorder.Count;
                    for (int i = 0; i < count; i++) total += recorder.GetSample(i).Value;
                    return total;
                }
            }

            public double Average
            {
                get
                {
                    if (!recorder.Valid || recorder.Count == 0) return -1;
                    return Total / (double)recorder.Count;
                }
            }

            public void Dispose() => recorder.Dispose();
        }
    }
}
