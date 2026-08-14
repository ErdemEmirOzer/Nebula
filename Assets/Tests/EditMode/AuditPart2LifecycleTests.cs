using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Poi;
using Nebula.Progression;
using Nebula.Remote;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENEL DENETİM PART 2 / AJAN 2 — session, static state, event and time
    /// lifecycle. Diagnostic fixture: every assertion states the behaviour the
    /// design already claims in its own doc comments, so a FAILURE here is the
    /// finding's proof, not a regression.
    /// <para>
    /// Deliberately paired: each finding test sits next to a CONTROL test that
    /// exercises the same mechanism where production already gets it right. A
    /// green control plus a red finding is what separates "this is broken" from
    /// "my harness is broken".
    /// </para>
    /// <para>Expected to fail on HEAD=4d16782 in: P2-LIFE-002, P2-LIFE-003, P2-LIFE-005.</para>
    /// </summary>
    public class AuditPart2LifecycleTests
    {
        private string logDirBackup;
        private string scratchLogDir;
        private bool inputGateBackup;

        [SetUp]
        public void CaptureGlobals()
        {
            inputGateBackup = InputGate.Suppressed;
            logDirBackup = SessionLog.DirectoryOverride;
            scratchLogDir = Path.Combine(Path.GetTempPath(), "nebula-audit-part2-a2-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(scratchLogDir);
            SessionLog.DirectoryOverride = scratchLogDir;
        }

        [TearDown]
        public void RestoreGlobals()
        {
            InputGate.Suppressed = inputGateBackup;
            ExclusiveModal.ResetForSession();
            SessionLog.DirectoryOverride = logDirBackup;
            ClearEveryStaticEvent();
            try { if (Directory.Exists(scratchLogDir)) Directory.Delete(scratchLogDir, true); } catch { /* scratch only */ }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Every field-like static event in the runtime assembly that a campaign
        /// session can leave a subscriber on. The compiler backs each of these
        /// with a private static field of the same name, which is the only way to
        /// count subscribers from outside the declaring type.
        /// </summary>
        private static readonly (Type owner, string eventName)[] StaticEvents =
        {
            (typeof(ProgressionService), nameof(ProgressionService.Awarded)),
            (typeof(ResourceTransferService), nameof(ResourceTransferService.Collected)),
            (typeof(CampaignIdentity), nameof(CampaignIdentity.IdentityChanged)),
            (typeof(DominanceSystem), nameof(DominanceSystem.PlanetFirstControlled)),
            (typeof(FloatingOrigin), nameof(FloatingOrigin.OriginShifted)),
            (typeof(DefenseFireSignal), nameof(DefenseFireSignal.Fired)),
            (typeof(EscortFireSignal), nameof(EscortFireSignal.Fired)),
            (typeof(RemoteCommandService), nameof(RemoteCommandService.CommandCompleted))
        };

        private static FieldInfo BackingField(Type owner, string eventName)
        {
            FieldInfo f = owner.GetField(eventName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(f, Is.Not.Null,
                $"{owner.Name}.{eventName} has no field-like backing store — the subscriber count cannot be read, so this audit cannot measure it.");
            return f;
        }

        private static int SubscriberCount(Type owner, string eventName)
        {
            var d = BackingField(owner, eventName).GetValue(null) as Delegate;
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        private static void ClearEveryStaticEvent()
        {
            foreach ((Type owner, string eventName) in StaticEvents)
                BackingField(owner, eventName).SetValue(null, null);
        }

        /// <summary>Subscribes one throwaway handler to every static event, through the real <c>+=</c> the game itself uses.</summary>
        private static void SubscribeOneToEach()
        {
            ProgressionService.Awarded += _ => { };
            ResourceTransferService.Collected += _ => { };
            CampaignIdentity.IdentityChanged += (_, __) => { };
            DominanceSystem.PlanetFirstControlled += _ => { };
            FloatingOrigin.OriginShifted += _ => { };
            DefenseFireSignal.Fired += _ => { };
            EscortFireSignal.Fired += _ => { };
            RemoteCommandService.CommandCompleted += _ => { };
        }

        private static void RunProductionSessionStart(int seed = 4242)
        {
            new WorldInstaller().PrepareSession(seed, new NebulaContext());
        }

        private static void RunProductionSessionDispose()
        {
            new NebulaSessionLifetime().Dispose();
        }

        // --------------------------------------------------- P2-LIFE static events

        /// <summary>
        /// CONTROL. The three static events whose reset <c>WorldInstaller.PrepareSession</c>
        /// really performs (lines 50/53/54). This must stay green: it is what
        /// proves the reflection-based subscriber count below can observe a
        /// successful clear at all.
        /// </summary>
        [Test]
        public void SessionStart_ClearsTheStaticEventsWorldInstallerClaimsToOwn()
        {
            ClearEveryStaticEvent();
            SubscribeOneToEach();

            RunProductionSessionStart();

            Assert.That(SubscriberCount(typeof(ProgressionService), nameof(ProgressionService.Awarded)), Is.Zero,
                "ProgressionService.Reset() nulls Awarded — WorldInstaller.PrepareSession:50");
            Assert.That(SubscriberCount(typeof(ResourceTransferService), nameof(ResourceTransferService.Collected)), Is.Zero,
                "ResourceTransferService.ResetForSession() nulls Collected — WorldInstaller.PrepareSession:53");
            Assert.That(SubscriberCount(typeof(CampaignIdentity), nameof(CampaignIdentity.IdentityChanged)), Is.Zero,
                "CampaignIdentity.ResetSubscribersForSession() nulls IdentityChanged — WorldInstaller.PrepareSession:54");
        }

        /// <summary>
        /// P2-LIFE-002. Three static events have no session-boundary owner at
        /// all: neither <c>WorldInstaller.PrepareSession</c> (session start) nor
        /// <c>NebulaSessionLifetime.Dispose</c> (session end) clears them, so a
        /// subscriber from a disposed campaign is still on the invocation list
        /// when the next one runs.
        /// <para>
        /// Two of them ship a reset helper whose own doc comment says it is
        /// "called on session teardown" — and nothing in Assets/Scripts calls it.
        /// </para>
        /// </summary>
        [Test]
        public void SessionBoundary_ClearsEveryStaticEventACampaignCanLeaveASubscriberOn()
        {
            ClearEveryStaticEvent();
            SubscribeOneToEach();

            RunProductionSessionStart();
            RunProductionSessionDispose();
            RunProductionSessionStart();   // the next campaign the player starts

            var survivors = new List<string>();
            foreach ((Type owner, string eventName) in StaticEvents)
            {
                int n = SubscriberCount(owner, eventName);
                if (n > 0) survivors.Add($"{owner.Name}.{eventName} ({n})");
            }

            Assert.That(survivors, Is.Empty,
                "a disposed campaign's subscribers are still attached after the next session starts: "
                + string.Join(", ", survivors)
                + ". WorldInstaller.PrepareSession:32-62 and NebulaSessionLifetime.Dispose:89-101 are the only two "
                + "session-boundary reset points, and neither names these events.");
        }

        /// <summary>
        /// P2-LIFE-005. A reset helper that exists but is never called is a
        /// safety net that is not there. Each of these documents itself as
        /// session-scoped; the audit only asks that something under
        /// Assets/Scripts actually calls it.
        /// </summary>
        [Test]
        public void EverySessionScopedResetHelper_HasAtLeastOneProductionCallSite()
        {
            string scriptRoot = Path.Combine(Application.dataPath, "Scripts");
            Assert.That(Directory.Exists(scriptRoot), Is.True, $"production script root not found at {scriptRoot}");

            string[] helpers =
            {
                "DefenseFireSignal.ClearSubscribers",
                "NebulaUiAssetCatalog.ResetForSession",
                "NebulaUiPrefabCatalog.ResetForSession",
                "RemoteCommunicationService.ResetForSession",
                // controls: these three are really called from production, and keep
                // this test honest about being able to find a call site at all.
                "EscortFireSignal.ClearSubscribers",
                "ExclusiveModal.ResetForSession",
                "DominanceSystem.ResetForSession"
            };

            var sources = new List<string>();
            foreach (string file in Directory.GetFiles(scriptRoot, "*.cs", SearchOption.AllDirectories))
                sources.Add(File.ReadAllText(file));

            var uncalled = new List<string>();
            foreach (string helper in helpers)
            {
                string declaringFileFragment = helper.Substring(0, helper.IndexOf('.'));
                int callSites = 0;
                foreach (string src in sources)
                {
                    int from = 0;
                    while (true)
                    {
                        int at = src.IndexOf(helper + "(", from, StringComparison.Ordinal);
                        if (at < 0) break;
                        callSites++;
                        from = at + 1;
                    }
                }
                // The declaration itself never contains "Type.Method(", only "Method(",
                // so every hit above is a real call site rather than the definition.
                if (callSites == 0) uncalled.Add(helper + " [declared in " + declaringFileFragment + "]");
            }

            Assert.That(uncalled, Is.Empty,
                "session-scoped reset helpers with zero call sites anywhere under Assets/Scripts: "
                + string.Join(", ", uncalled)
                + ". Their own doc comments say they are called at session start/teardown.");
        }

        // -------------------------------------------------- P2-LIFE input ownership

        /// <summary>
        /// CONTROL for the test below: session dispose really does release the
        /// modal owner, so the dispose path is reached and its resets do run.
        /// </summary>
        [Test]
        public void SessionDispose_ReleasesTheExclusiveModalOwner()
        {
            var owner = new object();
            Assert.That(ExclusiveModal.TryAcquire(owner), Is.True, "baseline: the modal gate could be taken");

            RunProductionSessionDispose();

            Assert.That(ExclusiveModal.Owner, Is.Null,
                "NebulaSessionLifetime.Dispose:95 calls ExclusiveModal.ResetForSession()");
        }

        /// <summary>
        /// P2-LIFE-003. <c>InputGate.Suppressed</c> is the single global that
        /// every contextual ship component opens with. Nothing on the session
        /// boundary owns it: it is written only by the UI shell's capture/restore
        /// pair and the strategic map's, so a session that ends while a screen is
        /// up hands a raised gate to the next campaign — every world interaction
        /// silently refusing, with no screen left to lower it.
        /// <para>
        /// <c>NebulaUiShell.ReleaseInput</c> already documents this exact
        /// player-visible failure ("the new campaign opened with every contextual
        /// ship component silently refusing to answer") and floors it from the
        /// UI side only, for the one provenance it can prove.
        /// </para>
        /// </summary>
        [Test]
        public void SessionBoundary_LowersTheGlobalInputSuppressionItDidNotRaise()
        {
            InputGate.Suppressed = true;      // exactly what any screen raises

            RunProductionSessionDispose();
            RunProductionSessionStart();      // the next campaign

            Assert.That(InputGate.Suppressed, Is.False,
                "a new campaign started with InputGate.Suppressed already true. Neither "
                + "NebulaSessionLifetime.Dispose nor WorldInstaller.PrepareSession names InputGate, "
                + "so nothing on the session boundary can lower a gate whose owner is gone.");
        }

        /// <summary>
        /// CONTROL. <c>CombatInputContext</c> is the sibling global of InputGate
        /// and IS explicitly reset at session start (WorldInstaller:48-49) for
        /// precisely the reason InputGate is not. Green here is what makes the
        /// red above a gap rather than a policy.
        /// </summary>
        [Test]
        public void SessionStart_ResetsTheCombatInputContextGlobals()
        {
            CombatInputContext.CombatActive = true;
            CombatInputContext.TrainingActive = true;

            RunProductionSessionStart();

            Assert.That(CombatInputContext.CombatActive, Is.False,
                "WorldInstaller.PrepareSession:48 explicitly clears CombatInputContext.CombatActive");
            Assert.That(CombatInputContext.TrainingActive, Is.False,
                "WorldInstaller.PrepareSession:49 explicitly clears CombatInputContext.TrainingActive");
        }

        // ------------------------------------------------- P2-LIFE registry removal

        /// <summary>
        /// P2-LIFE-001 (registry half). <c>PoiLiveState.Unregister</c> removes by
        /// KEY, with no check that the entry under that key still belongs to the
        /// caller. Combined with Unity's end-of-frame Destroy — which runs a
        /// demoted POI's OnDestroy after its replacement has already registered —
        /// the outgoing object deletes the incoming one's registration.
        /// <para>
        /// This half is pure and needs no scene; the deferred-destroy half is
        /// proved in <c>AuditPart2LifecyclePlayModeTests</c>.
        /// </para>
        /// </summary>
        [Test]
        public void PoiLiveState_UnregisterFromAnOutgoingOwner_DoesNotDeleteTheCurrentEntry()
        {
            PoiLiveState.Reset();

            const string poiId = "sector.0.0.0/sys.0/poi.0";
            var outgoing = new PoiState(poiId);
            var incoming = new PoiState(poiId);

            PoiLiveState.Register(outgoing);
            PoiLiveState.Register(incoming);       // the rebuilt system's own live state
            Assert.That(PoiLiveState.Find(poiId), Is.SameAs(incoming), "baseline: the newest registration wins");

            // What the outgoing PoiObject's deferred OnDestroy does, one frame later.
            PoiLiveState.Unregister(poiId, outgoing);

            Assert.That(PoiLiveState.Find(poiId), Is.SameAs(incoming),
                "the outgoing owner's unregister deleted the live entry that had already replaced it. "
                + "PoiObject.OnDestroy must call PoiLiveState.Unregister with the outgoing state identity, "
                + "unlike EnemyAgent.cs:160-163 which checks `current == this` first.");

            PoiLiveState.Reset();
        }

        /// <summary>
        /// CONTROL. <c>ExtractionState.UnregisterPlanet</c> is the same shape of
        /// registry and IS identity-guarded (ExtractionState.cs:77-85), so the
        /// project already holds the correct pattern in two places out of three.
        /// </summary>
        [Test]
        public void ExtractionStatePlanetRegistry_IgnoresAnUnregisterFromANonOwner()
        {
            MethodInfo unregister = typeof(ExtractionState).GetMethod(
                nameof(ExtractionState.UnregisterPlanet), BindingFlags.Public | BindingFlags.Static);
            Assert.That(unregister, Is.Not.Null, "ExtractionState.UnregisterPlanet is the control's subject");

            // Null is the only PlanetIdentity this fixture can build without a
            // CelestialBody; the guard's first clause must simply not throw.
            Assert.DoesNotThrow(() => unregister.Invoke(null, new object[] { null }),
                "the guarded unregister tolerates a caller that owns nothing");
        }

        // -------------------------------------------- RuntimeResourceScope ownership

        /// <summary>
        /// P2-LIFE-010 (owner half). <c>BeginSession</c> installs the static
        /// session-lifetime fallback owner. It used to overwrite that field
        /// unconditionally, and the field is the ONLY reference to the scope it
        /// replaced — so a previous session scope still standing there took every
        /// runtime Material, Texture and Mesh it owned with it, unreachable and
        /// therefore undestroyable for the rest of the process.
        /// <para>
        /// Composition never produces that state deliberately (both
        /// <c>SolarSystemBootstrap.EndSession</c> and <c>RestartCampaign</c> dispose
        /// the outgoing lifetime first), which is exactly why nothing caught it: the
        /// leak only appears once a session ends abnormally, and then it is silent
        /// and permanent.
        /// </para>
        /// </summary>
        [Test]
        public void BeginSession_ReleasesAPreviousSessionScopeInsteadOfOrphaningItsResources()
        {
            RuntimeResourceScope abandoned = RuntimeResourceScope.BeginSession();
            Mesh stranded = RuntimeResourceScope.Own(new Mesh { name = "audit-scope-orphan" });
            Assert.That(abandoned.Count, Is.EqualTo(1), "baseline: the session scope took ownership of a runtime mesh");

            RuntimeResourceScope replacement = RuntimeResourceScope.BeginSession();
            try
            {
                Assert.That(abandoned.IsDisposed, Is.True,
                    "the outgoing session scope was dropped rather than released");
                Assert.That(stranded == null, Is.True,
                    "the previous session's runtime mesh outlived the only reference that could ever have destroyed it. "
                    + "BeginSession is the last moment anything can reach an abandoned session scope.");

                RuntimeResourceScope.Own(new Mesh { name = "audit-scope-replacement" });
                Assert.That(replacement.Count, Is.EqualTo(1),
                    "the replacement session must own everything created after it");
                Assert.That(replacement.IsDisposed, Is.False,
                    "releasing the outgoing scope must not tear down the scope that replaced it");
            }
            finally { replacement.Dispose(); }
        }

        /// <summary>
        /// The identity half. An outgoing scope is routinely disposed AFTER its
        /// replacement is already installed — that is what deferred teardown means
        /// here — so its Dispose must clear the static session field only while
        /// that field is still its own entry.
        /// </summary>
        [Test]
        public void ALateDisposeFromAnOutgoingSessionScope_CannotClearTheSessionThatReplacedIt()
        {
            RuntimeResourceScope outgoing = RuntimeResourceScope.BeginSession();
            RuntimeResourceScope incoming = RuntimeResourceScope.BeginSession();
            try
            {
                outgoing.Dispose();      // the second, late dispose from the session that ended

                RuntimeResourceScope.Own(new Mesh { name = "audit-scope-identity" });
                Assert.That(incoming.Count, Is.EqualTo(1),
                    "an outgoing scope's late Dispose cleared the session field that already belonged to its "
                    + "replacement, so every runtime resource created afterwards had no owner at all");
            }
            finally { incoming.Dispose(); }
        }

        /// <summary>
        /// P2-LIFE-010 (nested-scope half). Disposing an ACTIVE nested scope inside
        /// its own <c>using</c> block used to lose the parent scope: <c>Dispose</c>
        /// set <c>current = null</c>, and the nested <c>Activation</c> restores the
        /// parent only while <c>current</c> is still the scope it installed — so it
        /// declined, and the rest of the enclosing block silently registered into
        /// the session scope instead of its real owner.
        /// <para>
        /// This is not hypothetical shape: <c>SystemMaterializer.DowngradeToWarm</c>
        /// releases the Full scope in the middle of a materialization, and
        /// <c>MaterializedSystem.BeginWarmResources</c> disposes the previous warm
        /// scope before activating the next one.
        /// </para>
        /// </summary>
        [Test]
        public void DisposingANestedScopeInsideItsOwnBlock_RestoresTheParentScopeItSuspended()
        {
            RuntimeResourceScope sessionScope = RuntimeResourceScope.BeginSession();
            var outer = new RuntimeResourceScope();
            var inner = new RuntimeResourceScope();
            try
            {
                using (outer.Activate())
                {
                    using (inner.Activate()) { inner.Dispose(); }

                    RuntimeResourceScope.Own(new Mesh { name = "audit-scope-parent" });
                    Assert.That(outer.Count, Is.EqualTo(1),
                        "after a nested scope ended, the enclosing scope must own again — a resource created here "
                        + "belongs to the materialized system that is still being built, not to the campaign");
                    Assert.That(sessionScope.Count, Is.Zero,
                        "the parent scope was lost and the session silently inherited a resource it must outlive");
                }

                RuntimeResourceScope.Own(new Mesh { name = "audit-scope-session" });
                Assert.That(sessionScope.Count, Is.EqualTo(1),
                    "the outermost activation must unwind back to the session fallback owner");
                Assert.That(outer.Count, Is.EqualTo(1),
                    "and must not keep collecting once its own block has ended");
            }
            finally
            {
                outer.Dispose();
                inner.Dispose();
                sessionScope.Dispose();
            }
        }

        /// <summary>
        /// The corollary the fix above must not break: a scope that was disposed
        /// while still active declines ownership (its own documented contract), but
        /// declining must not mean nobody owns the resource. The session scope is
        /// the standing fallback, so a resource created in the deferred window after
        /// a nested owner ended still has a bounded owner.
        /// </summary>
        [Test]
        public void AResourceCreatedWhileTheActiveScopeIsAlreadyDisposed_FallsBackToTheSessionOwner()
        {
            RuntimeResourceScope sessionScope = RuntimeResourceScope.BeginSession();
            var nested = new RuntimeResourceScope();
            try
            {
                using (nested.Activate())
                {
                    nested.Dispose();
                    RuntimeResourceScope.Own(new Mesh { name = "audit-scope-late" });
                }

                Assert.That(nested.Count, Is.Zero, "a disposed scope must never take ownership of anything");
                Assert.That(sessionScope.Count, Is.EqualTo(1),
                    "a runtime resource created after its nested owner ended was left with no owner at all; "
                    + "the live session is the only remaining bounded owner");
            }
            finally
            {
                nested.Dispose();
                sessionScope.Dispose();
            }
        }

        /// <summary>
        /// A <c>RenderTexture</c> is the one resource family whose managed object does
        /// not own its native surface on its own terms, so the scope owes it three
        /// steps in order: stop being the active target, release the surface, then
        /// destroy. A bare <c>Destroy</c> on the currently-active target is what
        /// produces "RenderTexture was destroyed while active" and leaves the surface
        /// itself for the driver to reclaim whenever it feels like it — which, for the
        /// session's 1024³ skybox cube, is the largest single graphics allocation a
        /// campaign makes.
        /// </summary>
        [Test]
        public void DisposingAScopeThatOwnsTheActiveRenderTexture_ClearsItBeforeDestroyingIt()
        {
            RenderTexture previouslyActive = RenderTexture.active;
            RuntimeResourceScope scope = RuntimeResourceScope.BeginSession();
            var target = RuntimeResourceScope.Own(
                new RenderTexture(64, 64, 16, RenderTextureFormat.ARGB32) { name = "audit-scope-rendertexture" });
            RenderTexture.active = target;
            Assert.That(scope.Count, Is.EqualTo(1), "baseline: the session scope took ownership of the render texture");

            try
            {
                scope.Dispose();

                Assert.That(target == null, Is.True,
                    "the scope did not destroy the render texture it owned");
                Assert.That(RenderTexture.active, Is.Null,
                    "the destroyed render texture was left installed as the active target; the next thing to read "
                    + "RenderTexture.active gets a destroyed object, and the surface was never released");
            }
            finally { RenderTexture.active = previouslyActive; }
        }

        /// <summary>
        /// The same three steps for a resource no scope ever took: <c>SkyboxBaker</c>
        /// allocates the cube before it knows whether <c>RenderToCubemap</c> will
        /// succeed, so its fallback branch has to let go of one itself. That branch is
        /// the one every headless run takes, because a null graphics device cannot
        /// render to a cubemap at all.
        /// </summary>
        [Test]
        public void ReleasingARenderTextureNoScopeOwns_GivesItTheSameThreeSteps()
        {
            RenderTexture previouslyActive = RenderTexture.active;
            var orphan = new RenderTexture(64, 64, 16, RenderTextureFormat.ARGB32) { name = "audit-scope-unowned" };
            RenderTexture.active = orphan;

            try
            {
                RuntimeResourceScope.Release(orphan);

                Assert.That(orphan == null, Is.True, "Release must destroy the resource it was handed");
                Assert.That(RenderTexture.active, Is.Null,
                    "a released render texture must not still be the active target");
            }
            finally { RenderTexture.active = previouslyActive; }
        }

        // ------------------------------------------------------- P2-COMBAT shutdown

        /// <summary>
        /// CHARACTERIZATION (passes today, and is the hazard). Once the 8-second
        /// countdown reaches zero, <see cref="EmergencyShutdownState"/> clears its
        /// own <c>Active</c> flag — and its idempotence guard (<c>if (Active)
        /// return</c>) goes with it. <c>ShipCombat.FixedUpdate:168</c> calls
        /// <c>TriggerShutdown()</c> unconditionally on every physics tick while
        /// <c>State.EmergencyShutdown</c> is true, and that flag is
        /// <c>Durability.Disabled</c> — still true until a recovery restores the
        /// hull. <c>UpdateShutdown</c> reaches recovery through
        /// <c>GetComponent&lt;ShipEmergencyRecovery&gt;()?.PerformRecovery()</c>,
        /// so a ship assembled without that component swallows the recovery and
        /// re-arms a fresh 8 seconds, re-raising EmergencyShutdownStarted, forever.
        /// </summary>
        [Test]
        public void ExpiredShutdown_DoesNotRearmUntilRecoveryCompletes()
        {
            var shutdown = new EmergencyShutdownState();

            shutdown.Trigger();
            Assert.That(shutdown.Active, Is.True);
            Assert.That(shutdown.RemainingSeconds, Is.EqualTo(EmergencyShutdownState.ShutdownSeconds).Within(1e-4f));

            shutdown.Trigger();     // idempotent while counting down
            Assert.That(shutdown.RemainingSeconds, Is.EqualTo(EmergencyShutdownState.ShutdownSeconds).Within(1e-4f),
                "baseline: re-triggering an ACTIVE countdown never restarts it");

            Assert.That(shutdown.Tick(EmergencyShutdownState.ShutdownSeconds), Is.True, "the countdown expired");
            Assert.That(shutdown.Active, Is.False);
            Assert.That(shutdown.RecoveryPending, Is.True);

            shutdown.Trigger();     // what FixedUpdate does on the very next tick
            Assert.That(shutdown.Active, Is.False,
                "an expired countdown must not re-arm while its recovery transition is pending");

            shutdown.CompleteRecovery();
            shutdown.Trigger();
            Assert.That(shutdown.Active, Is.True,
                "a later, genuinely new shutdown remains possible after recovery completed");
        }

        /// <summary>
        /// The save format carries Emergency Shutdown as a REMAINING TIME
        /// (ExtractionDirector.cs:261/795), not as an absolute deadline. Whatever
        /// Part 3 decides about the countdown running behind a screen, this is the
        /// persistence contract it has to stay compatible with: a countdown frozen
        /// at save time resumes frozen, never jumps.
        /// </summary>
        [Test]
        public void ShutdownRestore_TakesTheSavedRemainingTimeVerbatim()
        {
            var shutdown = new EmergencyShutdownState();
            shutdown.RestoreFromSave(active: true, remainingSeconds: 3.25f);

            Assert.That(shutdown.Active, Is.True);
            Assert.That(shutdown.RemainingSeconds, Is.EqualTo(3.25f).Within(1e-4f));

            shutdown.RestoreFromSave(active: true, remainingSeconds: -5f);
            Assert.That(shutdown.RemainingSeconds, Is.EqualTo(0f).Within(1e-4f),
                "a negative saved remainder clamps rather than counting up");
        }
    }
}
