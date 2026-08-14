using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Three short, direct regressions for the session/resource-ownership defects
    /// that only ever showed up when the whole PlayMode suite ran in one Unity
    /// process (2026-08-13, five sharded workers, 79 red).
    /// <para>
    /// Deliberately not endurance loops. Every failure these guard against is a
    /// single wrong ordering or a single unrestored global — one construction and
    /// one dispose is enough to see each of them, and a thousand-cycle harness only
    /// makes the eventual red harder to read.
    /// </para>
    /// </summary>
    public class SessionLeakRegressionTests
    {
        // =====================================================================
        // A. The project's own quality level decides whether the ship has a light
        // =====================================================================

        /// <summary>
        /// Fourteen PlayMode failures across <c>Ani02</c>, <c>Ani021</c>,
        /// <c>Ani022</c>, <c>Lgt02</c>-<c>Lgt053</c> and
        /// <c>LightingDirectorPlayModeTests</c> were read as a LEAKED
        /// "System Key Light". They are the opposite. Every one of them reports
        /// <c>Expected: 2 But was: 1</c> with a world-light list of exactly
        /// <c>System Key Light</c> — too FEW lights, and the missing one is the
        /// ship's "Hull Light", which <c>ShipLightingPresenter.ApplyProfile</c>
        /// deactivates on the Low tier and only on the Low tier.
        /// <para>
        /// The tier is not chosen by any of those fixtures. It is
        /// <c>LightingQualityProfile.FromQualitySettings()</c> reading Unity's
        /// project-wide level at <c>LightingInstaller.WireLighting</c>, and the
        /// working tree had that level at 0 (Very Low) against a committed 5
        /// (Ultra) — inherited, unchanged, by all five shard mirrors. So those
        /// fourteen assertions were asserting on
        /// <c>ProjectSettings/QualitySettings.asset</c>.
        /// </para>
        /// <para>
        /// This test says so out loud, once, instead of fourteen fixtures each
        /// reporting it as a lighting leak they cannot explain.
        /// </para>
        /// </summary>
        [Test]
        [Category("ReleaseRegression")]
        public void TheProjectsQualityLevelStillLeavesTheShipItsHullFill()
        {
            LightingQualityProfile profile = LightingQualityProfile.FromQualitySettings();

            Assert.AreNotEqual(LightingQuality.Low, profile.Quality,
                $"the project's quality level is {QualitySettings.GetQualityLevel()} "
                + $"({QualitySettings.names[QualitySettings.GetQualityLevel()]}), which resolves to Low. "
                + "Low drops the ship's hull fill by design, so every 'one key light plus one hull fill' "
                + "assertion in the suite sees one light instead of two. This is the project setting, "
                + "not a leaked light: restore ProjectSettings/QualitySettings.asset (committed: "
                + "m_CurrentQuality: 5).");
        }

        /// <summary>
        /// A session overrides two project-wide render values and has to put them
        /// back. <see cref="RenderSettings"/> would forgive this — it is per-scene
        /// and resets on the next load — but <see cref="QualitySettings"/> is
        /// per-project, and in the Editor a play-mode write to it is serialized
        /// into <c>ProjectSettings/QualitySettings.asset</c> and inherited by every
        /// later run. That is how the Ultra tier's authored
        /// <c>pixelLightCount: 4 / shadowDistance: 150</c> became
        /// <c>2 / 0</c> — <see cref="LightingQualityProfile.Medium"/>'s two numbers,
        /// left behind in the project by a session that ended.
        /// </summary>
        [Test]
        [Category("ReleaseRegression")]
        public void ASessionPutsBackTheProjectWideRenderBudgetItOverrode()
        {
            // The session is constructed BEFORE the baseline is read, and the order is
            // the whole point. A session left standing by an earlier fixture still has
            // its own override in force, and this constructor unwinds it (the
            // live-session handover below) — which restores the PROJECT's authored
            // budget. Reading the baseline first reads the other session's override
            // instead, and then asserts the project's own value is wrong.
            //
            // Measured, and this test caught it itself: green in a four-fixture run,
            // red in the 839-leaf gate with "Expected: 3 But was: 4". 3 is
            // LightingQualityProfile.High's budget — a live session's override — and 4
            // is what ProjectSettings/QualitySettings.asset authors. The product put
            // back exactly the right number; the baseline was taken one line too early.
            var lifetime = new NebulaSessionLifetime();
            int pixelsBefore = QualitySettings.pixelLightCount;
            float shadowBefore = QualitySettings.shadowDistance;

            // Whichever tier does not already match, so the override is real and the
            // restore below cannot pass by doing nothing. The three tiers have three
            // distinct pixel-light budgets (1/2/3), so at most one can be a no-op.
            LightingQualityProfile probe = pixelsBefore == LightingQualityProfile.High.PixelLightBudget
                ? LightingQualityProfile.Low
                : LightingQualityProfile.High;

            try
            {
                // The same call EnvironmentInstaller.ConfigureEnvironment makes as
                // step 4 of composition.
                LightingEnvironment.Apply(probe);
                Assert.AreEqual(probe.PixelLightBudget, QualitySettings.pixelLightCount,
                    "the profile's budget never reached QualitySettings, so this test proves nothing");
                Assert.AreNotEqual(pixelsBefore, QualitySettings.pixelLightCount,
                    "the probe tier matched the ambient one; this test cannot see a restore");
            }
            finally
            {
                lifetime.Dispose();
            }

            Assert.AreEqual(pixelsBefore, QualitySettings.pixelLightCount,
                "the session kept its pixel-light budget after it ended — in the Editor that is "
                + "written into ProjectSettings/QualitySettings.asset and inherited by every later run");
            Assert.AreEqual(shadowBefore, QualitySettings.shadowDistance, 0.0001f,
                "the session kept its shadow distance after it ended");
        }

        // =====================================================================
        // B. A second session must END the first, not amputate it
        // =====================================================================

        /// <summary>
        /// Constructing a second <see cref="NebulaSessionLifetime"/> while one is
        /// still standing used to reach
        /// <c>RuntimeResourceScope.BeginSession</c>, which released the orphaned
        /// scope's Materials, Meshes and Textures on its own — with the GameObjects
        /// that draw with them still ACTIVE and still ticking. That is precisely the
        /// order <see cref="NebulaSessionLifetime.Dispose"/> exists to prevent, and
        /// the measured symptom was <c>GalaxyDustFader.LateUpdate</c>
        /// (<c>GalaxyGenerator.cs:433</c>) calling <c>Material.HasProperty</c> on a
        /// destroyed cloud material on every subsequent frame: six red in
        /// <c>TutorialChapterABKGameplayTests</c> and one in
        /// <c>UiFinalFeatureParityPlayModeTests</c>, neither of which builds a world
        /// of its own — they only construct a second lifetime while the previous
        /// fixture's booted scene is still alive.
        /// <para>
        /// The assertion is on the ORDER, not on the exception: the outgoing
        /// session's roots must be down by the time the new one exists. A
        /// <c>MissingReferenceException</c> is a frame late and belongs to whichever
        /// test happens to be running then, which is exactly why this cost a
        /// bisection to find.
        /// </para>
        /// </summary>
        [UnityTest]
        [Category("ReleaseRegression")]
        public IEnumerator ASecondSessionEndsTheFirstBeforeItsResourcesAreReleased()
        {
            var first = new NebulaSessionLifetime();
            var firstRoot = new GameObject("Session Leak Probe Root");
            first.Track(SessionLifetime.CampaignSession, firstRoot);
            // Registered in the first session's RuntimeResourceScope, exactly as
            // GalaxyGenerator's cloud materials are.
            Material firstMaterial = MaterialLibrary.CreateUnlit(Color.white);

            Assert.IsFalse(first.IsDisposed);
            Assert.IsTrue(firstRoot.activeSelf);
            Assert.IsTrue(firstMaterial != null);

            // The shape TutorialSession.cs:50 has: a bare field initializer, with no
            // idea that a session is already live.
            var second = new NebulaSessionLifetime();
            Material secondMaterial = MaterialLibrary.CreateUnlit(Color.white);
            try
            {
                Assert.IsTrue(first.IsDisposed,
                    "a second session took over without ending the first — the first session's runtime "
                    + "resources are released anyway, while its GameObjects are still live and ticking");
                // Destruction is deferred in play mode, so the root is normally
                // still reachable here — inactive is the guarantee, not gone.
                Assert.IsFalse(firstRoot != null && firstRoot.activeSelf,
                    "the outgoing session's roots must be inactive BEFORE its Materials are destroyed, "
                    + "or every LateUpdate still holding one throws MissingReferenceException");

                yield return null; // deferred destruction lands

                Assert.IsTrue(firstMaterial == null,
                    "the outgoing session's material outlived the session that owned it");
                Assert.IsTrue(secondMaterial != null,
                    "the incoming session's material was destroyed along with its predecessor's — "
                    + "the resource scopes are not separated by ownership");
            }
            finally
            {
                // first.Dispose() is idempotent and a no-op once the second session
                // has ended it. It is here for the failing case: a red assertion
                // above means the first session is still standing, and leaving its
                // root in the scene would spread this failure into the next fixture.
                first.Dispose();
                second.Dispose();
            }

            yield return null;

            Assert.IsTrue(firstRoot == null, "the outgoing session's root outlived the session that owned it");
            Assert.IsTrue(secondMaterial == null, "a disposed session left its own material behind");
        }

        /// <summary>
        /// The same guarantee for callers that reach
        /// <see cref="RuntimeResourceScope.BeginSession"/> <b>directly</b>, without
        /// constructing a <see cref="NebulaSessionLifetime"/> at all.
        /// <para>
        /// The test above covers only the constructor route, and covering only that
        /// route is what left fifteen reds in a release gate: five call sites in the
        /// suite begin a bare session scope
        /// (<c>LifecycleOwnershipRegressionTests.cs:173</c>,
        /// <c>AuditPart2LifecycleDiagnosticTests.cs:673/694/722/762</c>), and one of
        /// them running while a booted world is still live released that world's
        /// Materials with its GameObjects still active.
        /// <c>GalaxyDustFader.LateUpdate</c> (<c>GalaxyGenerator.cs:433</c>) then
        /// threw on every frame for the remainder of the process, failing eight
        /// downstream fixtures that had composed nothing at all.
        /// </para>
        /// <para>
        /// The gate before it was green on identical product code. The only
        /// difference was that a shard scheduled a world-booting test immediately
        /// before one of those call sites — which is precisely why this belongs in a
        /// test that states the invariant instead of in a shard layout.
        /// </para>
        /// </summary>
        [UnityTest]
        [Category("ReleaseRegression")]
        public IEnumerator BeginningAResourceScopeDirectlyStillEndsTheStandingSessionInOrder()
        {
            var standing = new NebulaSessionLifetime();
            var standingRoot = new GameObject("Direct BeginSession Probe Root");
            standing.Track(SessionLifetime.CampaignSession, standingRoot);
            Material standingMaterial = MaterialLibrary.CreateUnlit(Color.white);

            Assert.IsFalse(standing.IsDisposed);
            Assert.IsTrue(standingRoot.activeSelf);

            // A bare scope, with no lifetime behind it — the shape those five call
            // sites have.
            RuntimeResourceScope bare = RuntimeResourceScope.BeginSession();
            try
            {
                Assert.IsTrue(standing.IsDisposed,
                    "beginning a resource scope directly took the session's resources without ending the "
                    + "session — its GameObjects are still live and still ticking over destroyed Materials");
                Assert.IsFalse(standingRoot != null && standingRoot.activeSelf,
                    "the standing session's roots must be inactive BEFORE its Materials are destroyed");

                yield return null; // deferred destruction lands

                Assert.IsTrue(standingMaterial == null,
                    "the standing session's material outlived the session that owned it");
            }
            finally
            {
                standing.Dispose();
                bare.Dispose();
            }

            yield return null;

            Assert.IsTrue(standingRoot == null,
                "the standing session's root outlived the session that owned it");
        }
    }
}
