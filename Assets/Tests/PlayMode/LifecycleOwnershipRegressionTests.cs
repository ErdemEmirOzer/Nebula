using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
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
    /// The release lane's statement of everything
    /// <see cref="AuditPart2LifecycleSoakTests"/> and
    /// <see cref="AuditPart2LifecycleDiagnosticTests"/> measure, without their loops.
    /// <para>
    /// The long tests were not wrong; they were expensive for the wrong reason. A
    /// hundred campaigns is a fine way to SEE a leak of one RenderTexture per
    /// session, and a terrible way to STATE that a RenderTexture must have an owner.
    /// Repetition turns a one-unit ownership defect into a hundred-unit arithmetic
    /// signal, and pays 410 seconds for the conversion. Every test in this file
    /// asserts the ownership directly instead: produce the resource once, then ask
    /// who registered it, who released it, whether the active render target was
    /// cleared, and what is still standing one frame after teardown.
    /// </para>
    /// <para>
    /// Each test names, in its own doc comment, the production line whose deletion or
    /// inversion turns it red. That is not decoration. A green suite proved nothing
    /// here once before — seventeen blank PNGs and three tests that asserted nothing
    /// all passed — so a regression that cannot be broken on demand is not admitted.
    /// Where a mutation can only be observed with a real graphics device, the test
    /// says so and asserts the headless half unconditionally rather than pretending
    /// to cover the other.
    /// </para>
    /// <para>
    /// Ordering is the recurring theme and the reason several assertions are taken in
    /// the SAME frame as the call under test. Unity defers <c>Destroy</c> to the end
    /// of the frame, so "the reference was dropped" and "the object was destroyed"
    /// are two different facts separated by a frame boundary — and a test that waits
    /// for the boundary can no longer tell them apart.
    /// </para>
    /// <para>
    /// <see cref="SessionLeakRegressionTests"/> covers the adjacent case of a SECOND
    /// session taking over from a live one; this file covers one session's own
    /// ownership chain and is deliberately not a duplicate of it.
    /// </para>
    /// </summary>
    public class LifecycleOwnershipRegressionTests : LifecycleAuditFixture
    {
        // =====================================================================
        // Recorder — the only way to observe teardown ORDER from outside
        // =====================================================================

        /// <summary>
        /// Records what a tracked root sees at the moment it is deactivated.
        /// <para>
        /// <c>NebulaSessionLifetime.DestroyAllReverse</c> calls
        /// <c>SetActive(false)</c> before <c>Destroy</c> (NebulaSessionLifetime.cs:242-243)
        /// precisely so every <c>OnDisable</c> runs synchronously, inside the Dispose
        /// call, rather than at the frame boundary. That is what makes the order
        /// observable at all: a recorder that only saw <c>OnDestroy</c> would see the
        /// whole session torn down in one undifferentiated batch at end of frame.
        /// </para>
        /// </summary>
        private sealed class TeardownRecorder : MonoBehaviour
        {
            public static readonly List<string> Order = new List<string>();
            public static readonly Dictionary<string, bool> GateWhenDisabled = new Dictionary<string, bool>();
            public static readonly Dictionary<string, bool> ModalWhenDisabled = new Dictionary<string, bool>();
            public static readonly Dictionary<string, float> TimeScaleWhenDisabled = new Dictionary<string, float>();

            public string Label;

            public static void Reset()
            {
                Order.Clear();
                GateWhenDisabled.Clear();
                ModalWhenDisabled.Clear();
                TimeScaleWhenDisabled.Clear();
            }

            private void OnDisable()
            {
                if (string.IsNullOrEmpty(Label)) return;
                Order.Add(Label);
                GateWhenDisabled[Label] = InputGate.Suppressed;
                ModalWhenDisabled[Label] = ExclusiveModal.Owner != null;
                TimeScaleWhenDisabled[Label] = Time.timeScale;
            }
        }

        private static GameObject TrackedRoot(NebulaSessionLifetime lifetime, SessionLifetime tier, string label)
        {
            var root = new GameObject(label);
            root.AddComponent<TeardownRecorder>().Label = label;
            lifetime.Track(tier, root);
            return root;
        }

        /// <summary>
        /// Every live cube-dimension <see cref="RenderTexture"/>, by instance id.
        /// <para>
        /// The family filter is what makes this readable. <c>Resources.FindObjectsOfTypeAll&lt;RenderTexture&gt;</c>
        /// also returns the editor's own targets, which come and go for reasons that
        /// have nothing to do with this test; <c>SkyboxBaker</c> is the only producer
        /// of a CUBE RenderTexture in the project (SkyboxBaker.cs:25-29), so a cube
        /// that appears and does not leave belongs to the bake and to nothing else.
        /// </para>
        /// </summary>
        private static HashSet<int> LiveCubeRenderTextures()
        {
            var ids = new HashSet<int>();
            foreach (RenderTexture rt in Resources.FindObjectsOfTypeAll<RenderTexture>())
                if (rt != null && rt.dimension == TextureDimension.Cube) ids.Add(rt.GetInstanceID());
            return ids;
        }

        private static List<string> CubesAddedSince(HashSet<int> before)
        {
            var added = new List<string>();
            foreach (RenderTexture rt in Resources.FindObjectsOfTypeAll<RenderTexture>())
                if (rt != null && rt.dimension == TextureDimension.Cube && !before.Contains(rt.GetInstanceID()))
                    added.Add($"'{rt.name}' {rt.width}x{rt.height} id={rt.GetInstanceID()} created={rt.IsCreated()}");
            return added;
        }

        // =====================================================================
        // 1. Cubemap / RenderTexture ownership — the skybox bake
        //    replaces: DeferredDestruction_SurvivesRepeatedSkyboxBaking (150 cycles)
        // =====================================================================

        /// <summary>
        /// One bake, and every ownership question the hundred-and-fifty-cycle probe
        /// could only answer by counting.
        /// <para>
        /// The bake allocates a 1024³ cube <c>RenderTexture</c> (SkyboxBaker.cs:25-29)
        /// BEFORE it knows whether <c>RenderToCubemap</c> will work, which splits the
        /// contract in two and this test follows both halves:
        /// </para>
        /// <list type="bullet">
        /// <item><b>fallback</b> (SkyboxBaker.cs:44-58) — the branch a headless run
        /// always takes, because a <c>NullGfxDevice</c> cannot render to a cubemap and
        /// <c>Shader.Find</c> is therefore never reached. No scope ever took ownership
        /// of the cube, so the branch has to release it itself:
        /// <c>RuntimeResourceScope.Release(cubemap)</c> at SkyboxBaker.cs:57.</item>
        /// <item><b>success</b> (SkyboxBaker.cs:65-68) — the cube outlives the call
        /// inside the skybox material, so it needs the session's owner:
        /// <c>RuntimeResourceScope.Own(cubemap)</c> at :65 and
        /// <c>Own(new Material(skyboxShader))</c> at :66. Without :65 the cube is the
        /// one runtime texture nothing destroys — <c>RenderSettings.skybox = null</c>
        /// at teardown drops the reference but not the surface.</item>
        /// </list>
        /// <para>
        /// MUTATION: delete <c>RuntimeResourceScope.Release(cubemap)</c>
        /// (SkyboxBaker.cs:57) and the headless assertion below goes red — a live
        /// 1024³ cube is still standing after the bake returned false, with no owner
        /// anywhere that could ever destroy it. Delete
        /// <c>RuntimeResourceScope.Own(cubemap)</c> (SkyboxBaker.cs:65) and the
        /// graphics-device assertion goes red: the scope's count does not grow by two
        /// and the cube survives <c>scope.Dispose()</c>.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator SkyboxBake_HandsItsCubeRenderTextureToAnOwnerAndLeavesNoneStanding()
        {
            HashSet<int> cubesBefore = LiveCubeRenderTextures();

            RuntimeResourceScope scope = RuntimeResourceScope.BeginSession();
            var backdrop = new GameObject("skybox-ownership-backdrop");
            int ownedBeforeBake = scope.Count;

            bool baked = SkyboxBaker.TryBake(backdrop, Color.black);
            int ownedAfterBake = scope.Count;

            if (baked)
            {
                // Only reachable with a real graphics device. RenderToCubemap succeeded,
                // so both the cube and the skybox material outlive this call and both
                // must have reached the session's owner.
                Assert.That(ownedAfterBake - ownedBeforeBake, Is.EqualTo(2),
                    "a successful bake must register exactly two runtime resources with the session's "
                    + "RuntimeResourceScope — the cube RenderTexture (SkyboxBaker.cs:65) and the skybox "
                    + $"material (SkyboxBaker.cs:66). The scope went {ownedBeforeBake} -> {ownedAfterBake}.");
                Assert.That(RenderSettings.skybox != null, Is.True,
                    "a successful bake must publish its material through RenderSettings.skybox (SkyboxBaker.cs:68)");
            }
            else
            {
                // The headless branch, and the one the release gate actually runs.
                // TryBake leaves the backdrop standing here by contract, so the caller
                // owns it — exactly as EnvironmentInstaller does.
                UnityEngine.Object.Destroy(backdrop);
                Assert.That(ownedAfterBake - ownedBeforeBake, Is.Zero,
                    "the fallback branch owns nothing: it releases the cube itself (SkyboxBaker.cs:57) "
                    + $"rather than handing it to a scope. The scope went {ownedBeforeBake} -> {ownedAfterBake}.");
            }

            yield return DrainDeferredTeardown();

            if (!baked)
                Assert.That(CubesAddedSince(cubesBefore), Is.Empty,
                    "the bake failed and left its 1024³ cube RenderTexture behind. The cube is allocated at "
                    + "SkyboxBaker.cs:25-29 before anyone knows the bake will work, and no scope ever owns it "
                    + "on this branch, so SkyboxBaker.cs:57 — RuntimeResourceScope.Release(cubemap) — is the "
                    + "only thing that can ever free it. Still standing: ");

            RenderSettings.skybox = null;
            scope.Dispose();
            yield return DrainDeferredTeardown();

            Assert.That(CubesAddedSince(cubesBefore), Is.Empty,
                "a cube RenderTexture produced by the bake outlived the scope that owned it. Still standing: "
                + string.Join(", ", CubesAddedSince(cubesBefore)));
            Assert.That(RenderTexture.active, Is.Null,
                "teardown left a destroyed RenderTexture installed as the active render target — "
                + "RuntimeResourceScope.cs:196 clears it before releasing, and nothing else does");
        }

        /// <summary>
        /// The release contract itself, on the one helper every owner of a
        /// <see cref="RenderTexture"/> in this project goes through.
        /// <para>
        /// A RenderTexture holds a native GPU surface its managed object does not own
        /// on its own terms. Destroying it while it is the active target produces the
        /// "RenderTexture was destroyed while active" complaint and leaves the surface
        /// for the driver to reclaim whenever it likes. <c>RuntimeResourceScope.Release</c>
        /// is the single implementation of the correct order — clear active, release
        /// the surface, then destroy — and it is public specifically so a second
        /// hand-rolled version cannot drift away from it.
        /// </para>
        /// <para>
        /// MUTATION: delete <c>RenderTexture.active = null</c>
        /// (RuntimeResourceScope.cs:196) and the first assertion goes red. Delete
        /// <c>renderTexture.Release()</c> (RuntimeResourceScope.cs:197) and the second
        /// goes red — but only where the device actually created a surface, which is
        /// why that one is guarded and reported rather than assumed. Delete the
        /// <c>Destroy</c> (RuntimeResourceScope.cs:199) and the third goes red.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator RuntimeResourceScopeRelease_ClearsTheActiveTargetAndFreesTheSurfaceBeforeDestroying()
        {
            var probe = new RenderTexture(64, 64, 16) { name = "lifecycle-release-contract" };
            bool created = probe.Create();
            RenderTexture.active = probe;

            RuntimeResourceScope.Release(probe);

            Assert.That(RenderTexture.active, Is.Null,
                "RuntimeResourceScope.Release destroyed a RenderTexture that was still installed as the "
                + "active render target. RuntimeResourceScope.cs:196 is the clear, and it is the only one "
                + "in the project — StrategicBodyPortrait and StrategicMapUniverseRenderer both reach it "
                + "through here.");

            if (created)
                Assert.That(probe.IsCreated(), Is.False,
                    "the native surface was never released. RuntimeResourceScope.cs:197 calls Release() "
                    + "before the Destroy on the line below, and Destroy alone leaves six faces of GPU "
                    + "memory to be reclaimed whenever the driver gets round to it.");
            else
                TestContext.Out.WriteLine(
                    "note: RenderTexture.Create() returned false, so this run has no native surface and the "
                    + "RuntimeResourceScope.cs:197 half of the contract was not exercised. Expected under "
                    + "-nographics (NullGfxDevice); re-run with --graphics to cover it.");

            // Destroy is deferred in play mode, so the managed object is still usable
            // above and must be gone below. Asserting both is what separates "released"
            // from "destroyed" — two different lines, one frame apart.
            yield return DrainDeferredTeardown();

            Assert.That(probe == null, Is.True,
                "RuntimeResourceScope.Release left the RenderTexture object alive — RuntimeResourceScope.cs:199");
        }

        // =====================================================================
        // 2. Material / Texture / Mesh ownership — the production factories
        //    replaces: DeferredDestruction_SurvivesSessionScale{Material,Texture,RuntimeMesh}Churn
        // =====================================================================

        /// <summary>
        /// One of each runtime resource family a session creates, through the
        /// production factory, released by the production owner.
        /// <para>
        /// The three churn probes allocate ~500 materials, ~110 textures and 4 large
        /// meshes per cycle for a hundred and fifty cycles, because at that volume an
        /// unowned resource is visible in a memory counter. The invariant underneath
        /// is per-resource and needs one of each: every <c>MaterialLibrary</c> factory
        /// ends in <c>RuntimeResourceScope.Own</c> (MaterialLibrary.cs:58, 92, 104,
        /// 140, 192), the session's scope is opened by
        /// <c>NebulaSessionLifetime</c>'s constructor (NebulaSessionLifetime.cs:108),
        /// and <c>Dispose</c> releases every registered resource
        /// (NebulaSessionLifetime.cs:159).
        /// </para>
        /// <para>
        /// Meshes are here for a reason the counters learned the hard way: destroying
        /// a MeshFilter's GameObject never destroys its mesh, so four per-session
        /// generator meshes (starfield, galaxy clouds, dust tiers, halo) leaked
        /// invisibly until <c>Mesh</c> got a counter of its own.
        /// </para>
        /// <para>
        /// MUTATION: delete <c>resources.Dispose()</c> (NebulaSessionLifetime.cs:159)
        /// and all six survivors are reported. Change any one factory's
        /// <c>return RuntimeResourceScope.Own(material)</c> to <c>return material</c>
        /// — MaterialLibrary.cs:58 for the lit case — and exactly that one resource is
        /// named in the failure, which is what makes this a locator rather than an
        /// alarm.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator SessionScope_DestroysEveryRuntimeResourceTheProductionFactoriesHandIt()
        {
            // Constructing a lifetime ends any live one first (NebulaSessionLifetime.cs:101-105),
            // so this cannot be handed a scope another fixture left open.
            var lifetime = new NebulaSessionLifetime();

            Material lit = MaterialLibrary.CreateLit(Color.white, 0.2f, 0f);
            Material additive = MaterialLibrary.CreateAdditive(Color.white);
            Material unlit = MaterialLibrary.CreateUnlit(Color.white);
            Texture2D perlin = MaterialLibrary.CreatePerlinTexture(64, Color.black, Color.white, 5f);
            Texture2D nebula = MaterialLibrary.CreateNebulaTexture(64, Color.blue, Color.magenta);
            Mesh mesh = RuntimeResourceScope.Own(new Mesh
            {
                name = "lifecycle-ownership-mesh",
                indexFormat = IndexFormat.UInt32
            });

            Assert.That(lit != null && additive != null && unlit != null
                        && perlin != null && nebula != null && mesh != null, Is.True,
                "baseline: every production factory returned a live resource, so the teardown below has "
                + "something to destroy");

            lifetime.Dispose();
            yield return DrainDeferredTeardown();

            var survivors = new List<string>();
            if (lit != null) survivors.Add("Material from MaterialLibrary.CreateLit (MaterialLibrary.cs:58)");
            if (additive != null) survivors.Add("Material from MaterialLibrary.CreateAdditive (MaterialLibrary.cs:104)");
            if (unlit != null) survivors.Add("Material from MaterialLibrary.CreateUnlit (MaterialLibrary.cs:92)");
            if (perlin != null) survivors.Add("Texture2D from MaterialLibrary.CreatePerlinTexture (MaterialLibrary.cs:140)");
            if (nebula != null) survivors.Add("Texture2D from MaterialLibrary.CreateNebulaTexture (MaterialLibrary.cs:192)");
            if (mesh != null) survivors.Add("Mesh from RuntimeResourceScope.Own");

            Assert.That(survivors, Is.Empty,
                "a session ended and left runtime resources it created behind: " + string.Join("; ", survivors)
                + ". Each factory hands its result to RuntimeResourceScope.Own, the session's scope is opened "
                + "by NebulaSessionLifetime's constructor (:108) and released by NebulaSessionLifetime.cs:159. "
                + "A resource named here reached none of those three.");
        }

        // =====================================================================
        // 3. Teardown ORDER — scene roots and the globals they read on the way out
        //    replaces: EndSession_LeavesNoSceneRootBehind's ordering half (50 cycles)
        // =====================================================================

        /// <summary>
        /// Reverse creation order, UI tier before campaign tier, and every
        /// <c>OnDisable</c> already run by the time <c>Dispose</c> returns.
        /// <para>
        /// All three are load-bearing. Reverse order (NebulaSessionLifetime.cs:225) is
        /// what lets a root created later hold a reference to one created earlier.
        /// The UI tier first (NebulaSessionLifetime.cs:147 before :152) is what stops a
        /// screen from reading a director that is already gone. And
        /// <c>SetActive(false)</c> before <c>Destroy</c>
        /// (NebulaSessionLifetime.cs:242-243) is what makes every <c>OnDisable</c> —
        /// CelestialBody's GravitySystem.Unregister, every event unsubscribe — fire
        /// synchronously inside the Dispose rather than at the frame boundary, which
        /// is required for a same-frame dispose-then-restart to leave no stale
        /// registration behind.
        /// </para>
        /// <para>
        /// The assertion is deliberately taken with no <c>yield</c> after
        /// <c>Dispose</c>. That is the whole point: at that instant nothing has been
        /// destroyed yet, so a recorded order can only have come from the
        /// deactivation pass.
        /// </para>
        /// <para>
        /// MUTATION: delete <c>go.SetActive(false)</c>
        /// (NebulaSessionLifetime.cs:242) and the recorded order is EMPTY — every
        /// OnDisable slides to the frame boundary. Invert the loop at
        /// NebulaSessionLifetime.cs:225 to run forwards and the two campaign roots
        /// swap. Swap :147 and :152 and the UI root moves to the end.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator SessionDispose_TearsTrackedRootsDownInReverseWithUiFirstAndOnDisableAlreadyRun()
        {
            var lifetime = new NebulaSessionLifetime();
            GameObject campaignFirst = TrackedRoot(lifetime, SessionLifetime.CampaignSession, "campaign.first");
            GameObject campaignSecond = TrackedRoot(lifetime, SessionLifetime.CampaignSession, "campaign.second");
            GameObject ui = TrackedRoot(lifetime, SessionLifetime.TransientUi, "ui.only");

            yield return null;
            Assert.That(campaignFirst.activeInHierarchy && campaignSecond.activeInHierarchy && ui.activeInHierarchy,
                Is.True, "baseline: all three tracked roots are live, so OnDisable has something to report");

            TeardownRecorder.Reset();
            lifetime.Dispose();
            // NO yield here — see the doc comment.

            Assert.That(TeardownRecorder.Order, Is.EqualTo(new[] { "ui.only", "campaign.second", "campaign.first" }),
                "session teardown did not deactivate its tracked roots UI-tier-first, in reverse creation order, "
                + "synchronously. Recorded: [" + string.Join(", ", TeardownRecorder.Order) + "]. "
                + "An EMPTY list means NebulaSessionLifetime.cs:242's SetActive(false) is gone and every "
                + "OnDisable has slid to the frame boundary; a reordered list means the reverse loop "
                + "(NebulaSessionLifetime.cs:225) or the tier order (:147 before :152) changed.");

            yield return DrainDeferredTeardown();
            Assert.That(campaignFirst == null && campaignSecond == null && ui == null, Is.True,
                "a tracked root outlived the session that owned it — NebulaSessionLifetime.cs:243");
        }

        /// <summary>
        /// What a campaign root sees at the moment it is deactivated: no input gate,
        /// no exclusive modal, and a clock running at 1.
        /// <para>
        /// <c>Dispose</c> puts all three back at NebulaSessionLifetime.cs:148-150 —
        /// BEFORE it tears the campaign tier down at :152 — and this test asserts on
        /// the position, not merely on the outcome. The outcome alone cannot be
        /// tested: <c>ResetProcessWideState</c> (:161, whose own copies are at
        /// :186-190) restores the same three values afterwards, so a test that only
        /// looked at the end state would stay green with :148-150 deleted and would
        /// be protecting nothing.
        /// </para>
        /// <para>
        /// The UI tier is excluded on purpose. It is torn down at :147, one line
        /// BEFORE the restores, so a screen legitimately sees the gate it raised still
        /// raised; asserting otherwise would be asserting against the production order
        /// rather than for it.
        /// </para>
        /// <para>
        /// MUTATION: delete <c>Ship.InputGate.Suppressed = false</c>
        /// (NebulaSessionLifetime.cs:149) and the campaign root records a raised gate.
        /// Delete <c>Ship.ExclusiveModal.ResetForSession()</c> (:148) or
        /// <c>Time.timeScale = 1f</c> (:150) and the matching assertion goes red. All
        /// three stay green if only the copies at :186-190 are deleted, which is
        /// correct: those are the safety net for a partial boot, not this contract.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator SessionDispose_HasAlreadyPutBackTheGateModalAndClockWhenCampaignRootsTearDown()
        {
            var lifetime = new NebulaSessionLifetime();
            GameObject campaign = TrackedRoot(lifetime, SessionLifetime.CampaignSession, "campaign.observer");
            TrackedRoot(lifetime, SessionLifetime.TransientUi, "ui.holder");
            yield return null;

            // Exactly the state a management screen leaves standing when the player
            // ends a campaign from an open menu.
            InputGate.Suppressed = true;
            ExclusiveModal.TryAcquire(this);
            Time.timeScale = 0.25f;
            Assert.That(ExclusiveModal.Owner, Is.SameAs(this),
                "baseline: the modal was really taken, so releasing it is a real event");

            TeardownRecorder.Reset();
            lifetime.Dispose();

            Assert.That(TeardownRecorder.GateWhenDisabled.ContainsKey("campaign.observer"), Is.True,
                "the campaign root never recorded a deactivation, so this test measured nothing");
            Assert.That(TeardownRecorder.GateWhenDisabled["campaign.observer"], Is.False,
                "a campaign root ran its OnDisable with the input gate still raised. "
                + "NebulaSessionLifetime.cs:149 lowers it at :149, two lines before the campaign tier is "
                + "torn down at :152, so every OnDisable that checks InputGate sees it down.");
            Assert.That(TeardownRecorder.ModalWhenDisabled["campaign.observer"], Is.False,
                "a campaign root ran its OnDisable with the exclusive modal still held — NebulaSessionLifetime.cs:148");
            Assert.That(TeardownRecorder.TimeScaleWhenDisabled["campaign.observer"], Is.EqualTo(1f).Within(0.0001f),
                "a campaign root ran its OnDisable on a stopped clock — NebulaSessionLifetime.cs:150");

            yield return DrainDeferredTeardown();
            Assert.That(campaign == null, Is.True, "baseline: the observed root was really torn down");
        }

        /// <summary>
        /// The skybox reference is dropped before the material behind it is released.
        /// <para>
        /// <c>RenderSettings.skybox = null</c> (NebulaSessionLifetime.cs:157) sits one
        /// line above <c>resources.Dispose()</c> (:159) so that no renderer is still
        /// pointing at a runtime resource whose owner has ended. Both assertions are
        /// taken in the same frame as the Dispose, and the second is the control that
        /// makes the first mean something: destruction is deferred, so the material is
        /// demonstrably still alive at that instant, and a null read from
        /// <c>RenderSettings.skybox</c> can therefore only have come from :157.
        /// </para>
        /// <para>
        /// MUTATION: delete <c>RenderSettings.skybox = null</c>
        /// (NebulaSessionLifetime.cs:157) and the first assertion reads back the live
        /// material and goes red.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator SessionDispose_DropsTheSkyboxReferenceBeforeReleasingTheMaterialBehindIt()
        {
            Material sceneSkybox = RenderSettings.skybox;
            try
            {
                var lifetime = new NebulaSessionLifetime();
                Material skybox = MaterialLibrary.CreateUnlit(Color.white);
                RenderSettings.skybox = skybox;
                Assert.That(RenderSettings.skybox, Is.SameAs(skybox), "baseline: the skybox reference was installed");

                lifetime.Dispose();

                Assert.That(RenderSettings.skybox == null, Is.True,
                    "session teardown released the skybox material while RenderSettings.skybox still pointed "
                    + "at it. NebulaSessionLifetime.cs:157 drops the reference; :159 releases the resource; "
                    + "the order between them is the contract.");
                Assert.That(skybox != null, Is.True,
                    "control: Destroy is deferred to the frame boundary, so the material must still be alive "
                    + "at this instant. If it is not, the assertion above proved nothing.");

                yield return DrainDeferredTeardown();
                Assert.That(skybox == null, Is.True,
                    "the skybox material outlived the scope that owned it — NebulaSessionLifetime.cs:159");
            }
            finally
            {
                RenderSettings.skybox = sceneSkybox;
            }
        }

        // =====================================================================
        // 4. Scene-root ownership on the real adapter
        //    replaces: EndSession_LeavesNoSceneRootBehind (50 cycles)
        // =====================================================================

        /// <summary>
        /// One real campaign, begun and ended, leaves the scene exactly as it found it.
        /// <para>
        /// The fifty-cycle diagnostic exists to answer "at WHICH cycle does teardown
        /// stop working, and what does the engine look like on either side of it" —
        /// a question one cycle cannot answer and should not pretend to. The product
        /// invariant is simpler and is fully visible in one cycle: a session owns
        /// every root it creates and destroys all of them.
        /// </para>
        /// <para>
        /// The baseline assertion is not decoration. A session that built nothing
        /// would leave nothing behind, and this test would pass while measuring
        /// absolutely nothing — the exact failure mode that shipped seventeen blank
        /// screenshots once.
        /// </para>
        /// <para>
        /// MUTATION: delete <c>DestroyAllReverse(campaignRoots)</c>
        /// (NebulaSessionLifetime.cs:152) and roughly 176 roots are named in the
        /// failure. Delete the <c>Destroy</c> at NebulaSessionLifetime.cs:243 and the
        /// same happens one tier at a time.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator EndSession_LeavesTheSceneWithExactlyTheRootsItStartedWith()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();

            HashSet<string> baseline = SceneRootNames();
            int baselineRoots = 0;
            foreach (GameObject unused in SceneRoots()) baselineRoots++;

            // Static events are the half of session ownership that leaves no object
            // behind to count. A subscriber is a delegate on a static field, so a
            // director that never unsubscribes keeps the whole outgoing session
            // reachable and fires into it for the rest of the process — and the scene
            // looks perfectly clean.
            var eventsBefore = new Dictionary<string, int>();
            foreach ((System.Type owner, string ev) in StaticEvents)
                eventsBefore[owner.Name + "." + ev] = SubscriberCount(owner, ev);

            adapter.BeginSession();
            yield return null;

            int liveRoots = 0;
            foreach (GameObject unused in SceneRoots()) liveRoots++;
            Assert.That(liveRoots, Is.GreaterThan(baselineRoots),
                $"baseline: a live session must add scene roots ({baselineRoots} -> {liveRoots}). "
                + "If it adds none, the teardown assertion below cannot fail and this test is worthless.");

            adapter.EndSession();
            yield return DrainDeferredTeardown();

            var counts = new Dictionary<string, int>();
            foreach (GameObject root in SceneRoots())
                counts[root.name] = counts.TryGetValue(root.name, out int c) ? c + 1 : 1;

            var report = new List<string>();
            foreach (var kv in counts)
            {
                if (!baseline.Contains(kv.Key)) report.Add($"{kv.Key} ×{kv.Value}");
                else if (kv.Value > 1) report.Add($"{kv.Key} ×{kv.Value} (duplicated, was in baseline)");
            }
            report.Sort();

            Assert.That(report, Is.Empty,
                $"one BeginSession/EndSession cycle left scene roots behind ({baselineRoots} before the session, "
                + $"{liveRoots} during it, {counts.Count} distinct names after teardown): "
                + string.Join(", ", report)
                + ". NebulaSessionLifetime.Dispose destroys the UI tier at :147 and the campaign tier at :152, "
                + "each in reverse creation order through DestroyAllReverse (:225-243).");

            var stuckSubscriptions = new List<string>();
            foreach ((System.Type owner, string ev) in StaticEvents)
            {
                string key = owner.Name + "." + ev;
                int now = SubscriberCount(owner, ev);
                if (now != eventsBefore[key]) stuckSubscriptions.Add($"{key}: {eventsBefore[key]} -> {now}");
            }

            Assert.That(stuckSubscriptions, Is.Empty,
                "one campaign left subscribers on static events after it ended: "
                + string.Join(", ", stuckSubscriptions)
                + ". A static event holds its subscriber's whole object graph alive and keeps firing into a "
                + "session that no longer exists, and nothing in the scene shows it. "
                + "NebulaSessionLifetime.ResetProcessWideState (:161) clears the ones with no other owner — "
                + "DefenseFireSignal.ClearSubscribers among them; the rest must be unsubscribed by the "
                + "component that subscribed, in the OnDisable that :242's SetActive(false) guarantees.");
        }

        // =====================================================================
        // 4b. Shell input/modal ownership
        //     replaces: ScreenOpenCloseCycles_LeaveNoInputOrModalOrClockResidue (100 cycles)
        // =====================================================================

        /// <summary>
        /// A screen takes the input gate and the exclusive modal exactly once, and
        /// hands back exactly what it found.
        /// <para>
        /// The hundred-cycle soak asserts the END state after a hundred push/pop
        /// pairs, which catches a leak of 1/100th of a gate — but the gate is a
        /// boolean, so the leak is fully present in the first pair. What a hundred
        /// cycles cannot state, and this test can, is the harder half of the contract:
        /// <c>ReleaseInput</c> restores <c>savedInputGateSuppressed</c>
        /// (NebulaUiShell.cs:469) rather than forcing <c>false</c>, because a screen
        /// opened while input was already suppressed for another reason must not
        /// release it. A soak that starts from a lowered gate can never tell those two
        /// apart — both leave it lowered.
        /// </para>
        /// <para>
        /// So this runs two cycles: one with the gate pre-raised by something that is
        /// not the shell, one from a clean start.
        /// </para>
        /// <para>
        /// MUTATION: delete <c>if (stack.Count == 0) ReleaseInput();</c>
        /// (NebulaUiShell.cs:380) and the gate stays raised and the modal stays held
        /// after the pop. Change <c>InputGate.Suppressed = savedInputGateSuppressed</c>
        /// to <c>= false</c> (NebulaUiShell.cs:469) and the FIRST cycle goes red while
        /// the second stays green — which is exactly the distinction the soak cannot
        /// draw. Move <c>HoldInput()</c> (:356) out of the <c>stack.Count == 0</c>
        /// branch and the saved value is overwritten by the shell's own gate.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator ShellPushPop_TakesTheGateAndModalOnceAndRestoresWhatItFound()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            UI.Shell.NebulaUiShell shell = context.Presentation.UiShell;
            UI.Menu.NebulaParentMenuScreen menu = context.Presentation.ParentMenu;
            Assert.That(shell, Is.Not.Null, "baseline: the session built a shell");
            Assert.That(menu, Is.Not.Null, "baseline: the session built the command menu");
            Assert.That(shell.StackDepth, Is.Zero, "baseline: the shell starts with nothing pushed");

            // --- cycle 1: something that is NOT the shell already holds the gate ---
            InputGate.Suppressed = true;

            Assert.That(shell.Push(menu), Is.True, "baseline: the first push was accepted");
            yield return null;
            Assert.That(shell.StackDepth, Is.EqualTo(1));
            Assert.That(ExclusiveModal.Owner, Is.SameAs(shell),
                "the first push must take ExclusiveModal for the shell — NebulaUiShell.cs:355");
            Assert.That(InputGate.Suppressed, Is.True, "baseline: the gate is up while a screen is open");

            Assert.That(shell.Pop(), Is.True);
            yield return null;
            Assert.That(shell.StackDepth, Is.Zero, "the shell's screen stack did not unwind");
            Assert.That(ExclusiveModal.Owner, Is.Null,
                "popping the last screen must hand ExclusiveModal back, or every future modal is refused "
                + "for the rest of the session — NebulaUiShell.cs:380 -> :473");
            Assert.That(InputGate.Suppressed, Is.True,
                "the shell forced the input gate to false instead of restoring the value it found. "
                + "NebulaUiShell.cs:469 restores savedInputGateSuppressed; a screen opened while input was "
                + "already suppressed for another reason must not release that other holder's gate.");

            // --- cycle 2: nothing else holding, so the gate must come all the way back down ---
            InputGate.Suppressed = false;

            Assert.That(shell.Push(menu), Is.True);
            yield return null;
            Assert.That(InputGate.Suppressed, Is.True,
                "an open screen must raise the input gate — NebulaUiShell.cs:356 -> :455");
            Assert.That(shell.Pop(), Is.True);
            yield return null;

            string ownership = $"[gate={InputGate.Suppressed} modal={(ExclusiveModal.Owner != null)} "
                + $"stack={shell.StackDepth} timeScale={Time.timeScale:0.###}]";
            Assert.That(InputGate.Suppressed, Is.False,
                $"a balanced push/pop left the global input gate raised {ownership} — every contextual ship "
                + "component opens with `if (InputGate.Suppressed) return`, so drilling, scanning, processing "
                + "and the save key all silently refuse with no screen left to lower it");
            Assert.That(ExclusiveModal.Owner, Is.Null, $"the exclusive-modal owner is still held {ownership}");
            Assert.That(shell.StackDepth, Is.Zero, $"the shell's screen stack did not unwind {ownership}");
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.0001f),
                $"a screen moved the clock {ownership} — no shell is supposed to write Time.timeScale any more (3fa9dda)");

            adapter.EndSession();
        }

        // =====================================================================
        // 5. Single-cycle counter regressions
        //    replace: the four "…Cycles_ReturnEveryCounterToItsBaseline" soaks
        // =====================================================================
        //
        // Every one of these takes the SAME zero-tolerance Snapshot the hundred-cycle
        // soaks take, over exactly one cycle, after one warm-up cycle has absorbed the
        // application-lifetime allocations (shader caches, shared meshes, lazily-built
        // textures) that are allocated once by design and would otherwise read as a
        // leak.
        //
        // The loop's contribution is amplification, not detection: a leak of k
        // resources per cycle is k here and 100k there. k is already non-zero, and
        // Snapshot's failure message prints the per-cycle rate either way. What the
        // release lane gives up by measuring one cycle is sensitivity to a leak that
        // only starts after the second campaign — which is precisely what the Soak
        // category is retained, at full cycle count, to find.

        private static IEnumerator OneSessionCycle(SolarSystemBootstrap adapter)
        {
            adapter.BeginSession();
            yield return null;
            adapter.EndSession();
            yield return DrainDeferredTeardown();
        }

        /// <summary>
        /// MUTATION: delete <c>resources.Dispose()</c> (NebulaSessionLifetime.cs:159)
        /// and Material/Texture/Mesh grow by roughly 500/110/4 in a single cycle.
        /// Delete <c>DestroyAllReverse(campaignRoots)</c> (:152) and GameObject,
        /// Component and every director counter grow.
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator OneBeginEndSessionCycle_ReturnsEveryCounterToItsBaseline()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();

            yield return OneSessionCycle(adapter);   // warm-up: application-lifetime allocations
            Snapshot baseline = Snapshot.Take();
            yield return OneSessionCycle(adapter);   // the measured cycle
            Snapshot after = Snapshot.Take();

            string growth = after.GrowthAgainst(baseline, 1);
            Assert.That(growth, Is.Empty,
                "one BeginSession/EndSession cycle, taken after a warm-up cycle had already absorbed every "
                + "application-lifetime allocation, did not return to baseline:" + growth
                + "\nfull baseline was:" + baseline.Full());
        }

        /// <summary>
        /// RestartCampaign is the harder path: it disposes and rebuilds inside one
        /// call, so every deferred OnDestroy from the outgoing session lands on the
        /// incoming one. That is a same-frame hazard, not an endurance one, and it is
        /// fully present in a single restart.
        /// <para>
        /// MUTATION: the same two lines as the cycle above — NebulaSessionLifetime.cs:159
        /// and :152 — plus any registry that removes by key without checking the entry
        /// is still its own (PoiObject.cs:75-78 is the open one), which shows up here
        /// as a registry.* counter that FELL rather than grew.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator OneRestartCampaignCycle_ReturnsEveryCounterToItsBaseline()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();

            adapter.BeginSession();
            yield return null;
            adapter.RestartCampaign();               // warm-up restart
            yield return DrainDeferredTeardown();
            Snapshot baseline = Snapshot.Take();

            adapter.RestartCampaign();               // the measured restart
            yield return null;
            yield return DrainDeferredTeardown();
            Snapshot after = Snapshot.Take();

            string growth = after.GrowthAgainst(baseline, 1);
            Assert.That(growth, Is.Empty,
                "one RestartCampaign, taken after a warm-up restart, did not return to baseline:" + growth
                + "\nfull baseline was:" + baseline.Full());

            adapter.EndSession();
        }

        /// <summary>
        /// A manual load reboots streaming without going through composition — the one
        /// path that rebuilds the world while the session's directors stay alive, and
        /// therefore the one path where a director can end up holding two worlds.
        /// <para>
        /// MUTATION: <c>ExtractionDirector.LoadNow</c> destroys the materialized
        /// system and re-materializes the SAME system inside one call. Remove the
        /// teardown half of that and GameObject/Renderer/Material all grow in this
        /// single cycle.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator OneSaveLoadCycle_ReturnsEveryCounterToItsBaseline()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            ExtractionDirector extraction = context.ExtractionDirector;
            extraction.SaveNow("AUDIT");
            extraction.LoadNow();                    // warm-up load
            yield return null;
            yield return null;
            Snapshot baseline = Snapshot.Take();

            extraction.SaveNow("AUDIT");
            extraction.LoadNow();                    // the measured load
            yield return null;
            yield return null;
            Snapshot after = Snapshot.Take();

            string growth = after.GrowthAgainst(baseline, 1);
            Assert.That(growth, Is.Empty,
                "one save/load through the real ExtractionDirector did not return to baseline:" + growth
                + "\nfull baseline was:" + baseline.Full());

            adapter.EndSession();
        }

        /// <summary>
        /// One Full-system demote/promote round trip on the real streaming director.
        /// A demotion captures a delta and tears the system down; the promotion
        /// rebuilds it. Nothing campaign-scoped may move across the pair.
        /// <para>
        /// MUTATION: remove the teardown in <c>SystemStreamingDirector</c>'s demote
        /// path and Renderer/Material/Mesh grow by a whole materialized system in this
        /// one round trip; the counters name which family.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator OneStreamingPromoteDemoteRoundTrip_ReturnsEveryCounterToItsBaseline()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            SystemStreamingDirector streaming = context.Streaming;
            SystemManifest home = StarSystemRegistry.Manifests.ResolveSystem(streaming.FullSystemId);
            SystemManifest foreign = FirstForeignManifest(context);
            Assert.That(home, Is.Not.Null, "baseline: the Full system resolves a manifest");

            // Warm-up round trip, so the measured one starts from a settled world.
            yield return PromoteTo(context, streaming, foreign);
            yield return PromoteTo(context, streaming, home);
            Snapshot baseline = Snapshot.Take();

            // GameObject and Component are the only counters here that can move
            // without anything saying WHAT moved, and this leaf is now the cheapest
            // place in the suite to find that out: it reproduces the +1/+2 in ONE
            // round trip, where AuditPart2LifecycleSoakTests needed a hundred cycles
            // to show the same object twice. Shared with that fixture rather than
            // copied — one instrument, one set of field names in the two reports that
            // have to be read against each other.
            var objectTracker = new AuditPart2LifecycleSoakTests.ObjectTracker();

            yield return PromoteTo(context, streaming, foreign);
            objectTracker.Observe(1);
            yield return PromoteTo(context, streaming, home);
            objectTracker.Observe(2);
            Snapshot after = Snapshot.Take();

            string growth = after.GrowthAgainst(baseline, 1);
            Assert.That(growth, Is.Empty,
                "one Full-system demote/promote round trip did not return to baseline:" + growth
                + objectTracker.Report(CounterDelta(baseline, after, "GameObject"), CounterDelta(baseline, after, "Component"))
                + "\nfull baseline was:" + baseline.Full());

            adapter.EndSession();
        }

        /// <summary>How far one counter moved between two snapshots.</summary>
        private static long CounterDelta(Snapshot baseline, Snapshot after, string key)
        {
            long before = baseline.Values.TryGetValue(key, out long b) ? b : 0;
            long now = after.Values.TryGetValue(key, out long a) ? a : 0;
            return now - before;
        }

        private static IEnumerator PromoteTo(NebulaContext context, SystemStreamingDirector streaming, SystemManifest target)
        {
            PlaceShipAtAbsolute(context, target.AbsoluteCenter);
            yield return null;
            yield return null;
            Assert.That(streaming.FullSystemId, Is.EqualTo(target.SystemId),
                $"the streaming director did not promote '{target.SystemId}' to Full — "
                + "the measurement below would be comparing two different worlds");
        }

        /// <summary>
        /// One encounter-director replacement, and the binding count that has to
        /// survive it.
        /// <para>
        /// <c>CombatLightingBridge</c> observes
        /// <c>SystemStreamingDirector.CombatEncounter</c> and rebinds in LateUpdate;
        /// the older <c>CombatAnimationBridge</c> captures only the start encounter.
        /// Exactly two subscribers on the replacement is the contract, and it is a
        /// per-replacement fact — the hundredth rebind can only tell you what the
        /// first one already did, one hundred times.
        /// </para>
        /// <para>
        /// MUTATION: remove <c>CombatLightingBridge</c>'s LateUpdate rebind and the
        /// count is 1 instead of 2 on the very first replacement. Leave the outgoing
        /// director's subscription in place and the drift assertion catches the
        /// event/participant counter that did not come back.
        /// </para>
        /// </summary>
        [UnityTest, Category("ReleaseRegression")]
        public IEnumerator OneEncounterDirectorRebind_LeavesExactlyTwoLiveBindings()
        {
            yield return LoadSceneFresh();
            SolarSystemBootstrap adapter = Adapter();
            NebulaContext context = adapter.BeginSession();
            yield return null;

            SystemStreamingDirector streaming = context.Streaming;
            SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(streaming.FullSystemId);
            Assert.That(manifest, Is.Not.Null, "baseline: the Full system resolves a manifest");

            streaming.ResetAndBootFull(manifest);    // warm-up replacement
            yield return null;
            yield return null;
            CombatEncounterDirector previous = streaming.CombatEncounter;
            Assert.That(previous, Is.Not.Null, "baseline: the rebuild produced an encounter director");
            Snapshot baseline = Snapshot.Take();

            streaming.ResetAndBootFull(manifest);    // the measured replacement
            CombatEncounterDirector current = streaming.CombatEncounter;
            Assert.That(current, Is.Not.Null.And.Not.SameAs(previous),
                "baseline: the encounter director was not replaced, so no rebind was exercised");
            int rightAfter = InvocationCount(current, nameof(CombatEncounterDirector.ProjectileResolved));
            yield return null;                       // CombatLightingBridge.LateUpdate rebinds here
            int afterLateUpdate = InvocationCount(current, nameof(CombatEncounterDirector.ProjectileResolved));
            yield return null;
            yield return null;

            Assert.That(afterLateUpdate, Is.EqualTo(2),
                $"the replacement encounter director carries {afterLateUpdate} subscriber(s) "
                + $"(it had {rightAfter} before LateUpdate ran), not the CombatLightingBridge + "
                + "CombatAnimationBridge pair the production rebind is supposed to leave behind");

            Snapshot after = Snapshot.Take();
            string drift = after.DifferencesAgainst(baseline, 1, "Material", "Texture");
            Assert.That(drift, Is.Empty,
                "one encounter/director replacement moved a participant/event/director or object counter "
                + "(Materials/Textures excluded here, as in the soak: a rebuild legitimately re-creates "
                + "them and the resource families are covered by the cycle tests above):" + drift);

            adapter.EndSession();
        }
    }
}
