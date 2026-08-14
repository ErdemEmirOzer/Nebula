using System.Collections.Generic;
using UnityEngine;
using Nebula.Visuals;

namespace Nebula.World.Composition
{
    /// <summary>
    /// The four lifetimes a composition session spans (TF-002 §7). Purely
    /// documentation/classification for <see cref="NebulaSessionLifetime.Track"/>
    /// call sites — nothing here is itself disposed generically; each tier
    /// has its own concrete owner, listed below.
    /// </summary>
    public enum SessionLifetime
    {
        /// <summary>
        /// The Unity application process and the scene adapter
        /// (<see cref="SolarSystemBootstrap"/>) itself, plus its serialized
        /// Inspector config. Outlives every campaign session; never created
        /// or destroyed by <see cref="NebulaBootstrap"/>.
        /// </summary>
        Application,

        /// <summary>
        /// Seed, manifest/delta repositories, progression, save/navigation,
        /// the ship, and the main directors — everything <see cref="NebulaSessionLifetime"/>
        /// tracks and destroys in <see cref="NebulaSessionLifetime.Dispose"/>.
        /// Survives a Full-system demotion; ends only at session dispose/restart.
        /// </summary>
        CampaignSession,

        /// <summary>
        /// <see cref="Sectors.SystemStreamingDirector"/>'s currently
        /// Full/Warm materialized star/planet/structure/POI GameObjects.
        /// Owned and torn down entirely by
        /// <see cref="Sectors.SystemStreamingDirector.Shutdown"/> /
        /// the existing promote-demote path — deliberately NOT tracked as
        /// generic roots here, so a routine demotion never touches campaign
        /// state and a session dispose never needs to "guess" which
        /// GameObjects are materialized-system versus campaign.
        /// </summary>
        MaterializedSystem,

        /// <summary>
        /// HUD/Screen/modal GameObjects and their <see cref="Ship.ExclusiveModal"/>
        /// ownership — rebuilt whenever <see cref="PresentationInstaller"/> runs.
        /// </summary>
        TransientUi
    }

    /// <summary>
    /// The "small lifetime component" (TF-002 §2) that actually owns session
    /// teardown bookkeeping: every root GameObject an installer creates is
    /// registered here under its lifetime tier, and <see cref="Dispose"/>
    /// destroys everything in exact reverse creation order, tier by tier,
    /// exactly once. <see cref="NebulaBootstrap"/> owns WHEN/WHAT to build
    /// (composition order); this class owns HOW it all comes back down.
    /// </summary>
    public sealed class NebulaSessionLifetime
    {
        /// <summary>
        /// The session currently standing, or null. One slot, because
        /// <see cref="RuntimeResourceScope"/> has one session slot and the two
        /// have to agree about who is live.
        /// </summary>
        private static NebulaSessionLifetime live;

        /// <summary>
        /// Teaches <see cref="RuntimeResourceScope"/> how to end a standing session
        /// properly, so that <b>every</b> route into
        /// <see cref="RuntimeResourceScope.BeginSession"/> gets the roots-then-resources
        /// order — not just the one that goes through this constructor.
        /// <para>
        /// The constructor handover below cannot cover a caller that reaches
        /// <c>BeginSession</c> directly, and there are five such call sites in the test
        /// suite alone (<c>LifecycleOwnershipRegressionTests.cs:173</c>,
        /// <c>AuditPart2LifecycleDiagnosticTests.cs:673/694/722/762</c>). Any of them
        /// running while a world is live released that world's Materials with its
        /// GameObjects still active, and <c>GalaxyDustFader.LateUpdate</c>
        /// (<c>GalaxyGenerator.cs:433</c>) then threw every frame for the rest of the
        /// process — fifteen reds across eight fixtures in one release gate, and zero
        /// in the gate before it, because the only thing that changed was which test a
        /// shard happened to schedule immediately beforehand.
        /// </para>
        /// <para>
        /// A static constructor is the right home: a standing
        /// <see cref="RuntimeResourceScope"/> session can only exist because a
        /// <see cref="NebulaSessionLifetime"/> was constructed, and constructing one
        /// runs this first. The hook is therefore always installed before anything
        /// could need it.
        /// </para>
        /// </summary>
        static NebulaSessionLifetime()
        {
            RuntimeResourceScope.InstallStandingSessionTerminator(() => live?.Dispose());
        }

        private readonly List<GameObject> campaignRoots = new List<GameObject>();
        private readonly List<GameObject> transientUiRoots = new List<GameObject>();
        private readonly RuntimeResourceScope resources;

        /// <summary>
        /// Ends the session still standing, if any, BEFORE this one takes over.
        /// <para>
        /// This is the ordering half of the contract <see cref="Dispose"/> already
        /// states in code: roots deactivated first, runtime resources released
        /// second, "so deferred GameObject destruction cannot draw with a resource
        /// whose owner has already ended". Until now only the second half survived
        /// a session that was never disposed —
        /// <see cref="RuntimeResourceScope.BeginSession"/> released the orphaned
        /// scope's Materials, Meshes and Textures on its own, while the GameObjects
        /// holding them were still ACTIVE and still ticking. The measured result was
        /// <c>GalaxyDustFader.LateUpdate</c> (<c>GalaxyGenerator.cs:433</c>) calling
        /// <c>Material.HasProperty</c> on a destroyed cloud material every frame:
        /// seven PlayMode failures across <c>TutorialChapterABKGameplayTests</c> and
        /// <c>UiFinalFeatureParityPlayModeTests</c>, none of which composes a world
        /// of its own — they only construct a second lifetime
        /// (<c>TutorialSession.cs:50</c>) while the previous fixture's booted scene
        /// is still alive.
        /// </para>
        /// <para>
        /// Composing over a live session was already destructive; it just failed
        /// halfway. Finishing the teardown instead of amputating it is the whole
        /// change, and in production it is unreachable: every caller
        /// (<c>SolarSystemBootstrap.BeginSession</c>/<c>EndSession</c>/<c>RestartCampaign</c>,
        /// <c>TutorialSceneHost</c> in its own scene) already disposes first, so
        /// <see cref="live"/> is null here and this costs nothing.
        /// </para>
        /// </summary>
        public NebulaSessionLifetime()
        {
            NebulaSessionLifetime outgoing = live;
            // Cleared before the dispose, not by it: the outgoing Dispose's own
            // identity check must not race the assignment below.
            live = null;
            outgoing?.Dispose();

            // `live` stays null ACROSS this call, and the order is not cosmetic.
            // BeginSession now asks the terminator to end whatever session is
            // standing, and the terminator is `live?.Dispose()`. Publishing `this`
            // first would let a leftover bare scope — one begun directly rather than
            // through a lifetime — dispose the session being constructed on the line
            // that constructs it.
            resources = RuntimeResourceScope.BeginSession();
            live = this;
        }

        public bool IsDisposed { get; private set; }

        /// <summary>Registers a root GameObject this session owns. <see cref="SessionLifetime.MaterializedSystem"/> is not accepted here — see the enum's own doc comment.</summary>
        public void Track(SessionLifetime lifetime, GameObject root)
        {
            if (root == null) return;
            switch (lifetime)
            {
                case SessionLifetime.CampaignSession:
                    campaignRoots.Add(root);
                    break;
                case SessionLifetime.TransientUi:
                    transientUiRoots.Add(root);
                    break;
                default:
                    throw new System.InvalidOperationException(
                        $"NebulaSessionLifetime.Track does not accept {lifetime} — Application is never session-owned, and MaterializedSystem is owned by SystemStreamingDirector itself.");
            }
        }

        /// <summary>
        /// Tears down everything tracked so far, in reverse creation order,
        /// UI tier first then campaign tier, followed by the static-state
        /// safety nets that have no other owner to call them. Idempotent:
        /// a second call is a silent no-op — never throws, never double-saves,
        /// never double-fires an event.
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            // Identity-checked for the same reason RuntimeResourceScope checks its
            // own: a session disposed late must never unseat the one that has
            // already replaced it.
            if (ReferenceEquals(live, this)) live = null;

            DestroyAllReverse(transientUiRoots);
            Ship.ExclusiveModal.ResetForSession();
            Ship.InputGate.Suppressed = false;
            Time.timeScale = 1f;

            DestroyAllReverse(campaignRoots);

            // All renderers are inactive before their runtime-created resources
            // are released, so deferred GameObject destruction cannot draw with a
            // resource whose owner has already ended.
            RenderSettings.skybox = null;
            Visuals.MaterialVariantCache.Clear();
            resources.Dispose();

            ResetProcessWideState();
        }

        /// <summary>
        /// Every process-wide value a session leaves behind, put back.
        /// <para>
        /// Lifted out of <see cref="Dispose"/> and made public because it is the
        /// project's <b>one</b> statement of what "a session is over" means for static
        /// state, and until now the only way to invoke it was to own a
        /// <see cref="NebulaSessionLifetime"/> instance. Everything else that needs the
        /// same guarantee — a test fixture that ends where a session would, a harness
        /// unwinding a partial boot — had to re-list the statics by hand, and a
        /// hand-kept list of process-wide state is the same defect as no list at all:
        /// it goes stale silently and the symptom lands in whichever run happens to
        /// follow. <see cref="Dispose"/> calls this, so the contract cannot rot without
        /// the game itself rotting with it.
        /// </para>
        /// <para>
        /// Deliberately not a test hook. It resets nothing that is not genuinely
        /// session-scoped, it takes no arguments that could weaken it, and the shipping
        /// teardown path is its first caller.
        /// </para>
        /// </summary>
        public static void ResetProcessWideState()
        {
            Ship.ExclusiveModal.ResetForSession();
            Ship.InputGate.Suppressed = false;
            Ship.CombatInputContext.CombatActive = false;
            Ship.CombatInputContext.TrainingActive = false;
            Time.timeScale = 1f;

            // QualitySettings is per-PROJECT, not per-scene: unlike RenderSettings
            // it survives a scene load, and in the Editor it is serialized back into
            // ProjectSettings/QualitySettings.asset, so a session's render budget
            // leaks out of the process entirely and into every later run.
            Visuals.LightingEnvironment.RestoreProcessWideBudget();

            Extraction.DominanceSystem.ResetForSession();
            Visuals.DefenseFireSignal.ClearSubscribers();
            Remote.RemoteCommandService.ResetForSession();
            Remote.RemoteCommunicationService.ResetForSession();
            // Symmetry with PrepareSession, which resets all of these on the way in.
            // Without them a rejection raised by the campaign that just ended can
            // flash into the next one's HUD, and a stale context can outlive its owner.
            World.FloatingOrigin.ResetForSession();
            Ship.NebulaGameplayFeedback.Clear();
            Ship.NebulaInputRouter.ResetContexts();
            Ship.NebulaInputRouter.ResetSourceToKeyboard();
            Ship.ShipController.ResetFlightInputToKeyboard();
            Ship.ShipController.ResetPointerAim();
            UI.Controls.NebulaKeyCaptureListener.ResetSourceToKeyboard();
            World.StarSystemRegistry.Streaming = null;
        }

        /// <summary>
        /// A partially-completed boot (an installer threw mid-way) unwinds
        /// through the exact same path as a normal Dispose — whatever was
        /// tracked before the failure is torn down in reverse order, leaving
        /// no half-built session behind.
        /// </summary>
        public void DisposePartialBoot() => Dispose();

        private static void DestroyAllReverse(List<GameObject> roots)
        {
            for (int i = roots.Count - 1; i >= 0; i--)
            {
                GameObject go = roots[i];
                if (go == null) continue;
                // These two roots own resources outside their own hierarchy:
                // the shell's static Live/event/input registrations, and the
                // streaming director's Full/Warm scene roots. Release those
                // explicit owners synchronously before deferred destruction.
                UI.Shell.NebulaUiShell shell = go.GetComponent<UI.Shell.NebulaUiShell>();
                shell?.Shutdown();
                Sectors.SystemStreamingDirector streaming = go.GetComponent<Sectors.SystemStreamingDirector>();
                streaming?.Shutdown();
                // Deactivate first so every OnDisable (CelestialBody's
                // GravitySystem.Unregister, event unsubscribes, etc.) fires
                // synchronously before the object is actually torn down —
                // required for a same-frame dispose-then-restart to leave no
                // stale registrations behind.
                go.SetActive(false);
                if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
            }
            roots.Clear();
        }
    }
}
