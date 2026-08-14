using System;
using UnityEngine;

namespace Nebula.Tutorial
{
    /// <summary>
    /// The one object the tutorial scene contains, and the only thing that
    /// composes a tutorial world.
    /// <para>
    /// It is the tutorial's answer to <c>SolarSystemBootstrap</c>: an adapter
    /// with no logic of its own that hands composition to
    /// <see cref="TutorialSession"/> and the run to
    /// <see cref="TutorialDirector"/>. Keeping it this thin is what makes
    /// "entering the tutorial never starts a campaign" checkable by reading one
    /// short file -- there is no second path into a world here.
    /// </para>
    /// <para>
    /// Everything it builds is destroyed again in <see cref="ShutDown"/>, which
    /// also runs from <c>OnDestroy</c>. Unloading the tutorial scene is
    /// therefore sufficient on its own: no static holds a reference, no pooled
    /// lease outlives it, and re-entering always composes from nothing.
    /// </para>
    /// </summary>
    public sealed class TutorialSceneHost : MonoBehaviour
    {
        [Tooltip("Opening this scene directly and pressing Play runs the curriculum from step 1. " +
                 "Entering through the menu always supplies its own run instead.")]
        [SerializeField] private bool autoStartWhenOpenedDirectly = true;

        private TutorialLauncher owner;

        /// <summary>The shell this host built for the on-screen HUD, or null when the caller supplied its own.</summary>
        private UI.Shell.NebulaUiShell shell;

        public TutorialSession Session { get; private set; }
        public TutorialDirector Director { get; private set; }
        public ITutorialHud Hud { get; private set; }

        /// <summary>True between a successful <see cref="StartRun"/> and <see cref="ShutDown"/>.</summary>
        public bool IsRunning => Director != null && Director.IsRunning;

        /// <summary>The composition failure, if the world could not be built. The run then never starts rather than starting half-built.</summary>
        public Exception CompositionError { get; private set; }

        private void Awake()
        {
            if (TutorialRunRequest.TryConsume(out TutorialRunRequest.Pending pending))
            {
                owner = pending.Owner;
                StartRun(pending.Mode, pending.StepId, pending.Hud);
                return;
            }

            if (autoStartWhenOpenedDirectly) StartRun(TutorialRunMode.Sequential, null, null);
        }

        /// <summary>
        /// Composes the tutorial world and starts the run. Directly callable, with
        /// no scene load and no input, so a PlayMode test drives exactly what the
        /// menu drives.
        /// </summary>
        public void StartRun(TutorialRunMode mode, string stepId, ITutorialHud hud)
        {
            if (Director != null) return;

            ClaimActiveScene();

            try { Session = TutorialSession.Compose(); }
            catch (Exception error)
            {
                CompositionError = error;
                Debug.LogException(error);
                Debug.LogError("[Tutorial] The tutorial world could not be composed; the run does not start.");
                owner?.NotifyRunEnded(mode, false);
                return;
            }

            // A caller may supply its own HUD -- a test usually supplies a recorder.
            // When nobody does, build the real card rather than falling back to the
            // recorder: a recorder renders nothing, so that fallback produced a
            // tutorial run with no instruction card on screen at all, which is the
            // one failure this whole feature cannot survive.
            Hud = hud ?? BuildOnScreenHud();

            Director = TutorialDirector.Create(Session, Hud);
            Director.transform.SetParent(transform, false);
            Director.RunEnded += OnRunEnded;

            if (mode == TutorialRunMode.Standalone) Director.RunSingleStep(stepId);
            else Director.RunSequential();
        }

        /// <summary>
        /// Builds the tutorial's own shell and instruction card.
        /// <para>
        /// Its own shell, not the campaign's: the tutorial composition deliberately
        /// skips <c>PresentationInstaller</c>, so <c>NebulaContext.Presentation</c>
        /// is unset and reading it throws. This is the same shape the main menu
        /// uses before a session exists -- one owner, torn down with the scene.
        /// </para>
        /// </summary>
        private ITutorialHud BuildOnScreenHud()
        {
            shell = UI.Shell.NebulaUiShell.Create();
            return UI.Tutorial.TutorialHud.Create(shell, owner);
        }

        private void OnRunEnded(TutorialRunMode mode, bool completed)
        {
            // The launcher owns what happens next (unload the scene, restore the
            // menu). The host only reports; a host that navigated would be a
            // second owner of the same decision.
            if (owner != null) owner.NotifyRunEnded(mode, completed);
            else ShutDown();
        }

        /// <summary>
        /// Destroys the director and disposes the tutorial world. Idempotent, and
        /// the same path <c>OnDestroy</c> takes, so an unloaded scene and an
        /// explicit exit leave the process in the same state.
        /// </summary>
        public void ShutDown()
        {
            if (Director != null)
            {
                Director.RunEnded -= OnRunEnded;
                TutorialDirector doomed = Director;
                Director = null;
                DestroyNow(doomed.gameObject);
            }

            if (Session != null)
            {
                TutorialSession doomedSession = Session;
                Session = null;
                doomedSession.Dispose();
            }

            Hud = null;

            // Only the shell this host built. A caller-supplied HUD brought its own
            // surface and disposes it itself; tearing down something we did not
            // create would take the caller's canvas with it.
            if (shell != null)
            {
                UI.Shell.NebulaUiShell doomedShell = shell;
                shell = null;
                doomedShell.Shutdown();
                DestroyNow(doomedShell.gameObject);
            }

            owner = null;
            ReleaseActiveScene();
        }

        private void OnDestroy() => ShutDown();

        /// <summary>
        /// Makes the tutorial scene the active one for the length of the run.
        /// <para>
        /// A runtime <c>new GameObject()</c> lands in whatever scene is active, and
        /// the tutorial scene is loaded additively on top of the menu scene -- so
        /// without this every ship, camera, galaxy and director the tutorial builds
        /// would be created in the <i>menu's</i> scene and would survive unloading
        /// the tutorial's. <see cref="TutorialSession.Dispose"/> is the real
        /// guarantee and destroys all of it explicitly; this is the second one, and
        /// it is the one that also catches a root nobody remembered to track. That
        /// has happened before in this project: the intergalactic halo and every
        /// decor POI outlived their sessions for thirteen sessions running.
        /// </para>
        /// </summary>
        private void ClaimActiveScene()
        {
            if (!Application.isPlaying) return;

            UnityEngine.SceneManagement.Scene mine = gameObject.scene;
            UnityEngine.SceneManagement.Scene current =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!mine.IsValid() || !mine.isLoaded || mine == current) return;

            previousActiveScene = current;
            claimedActiveScene = UnityEngine.SceneManagement.SceneManager.SetActiveScene(mine);
        }

        private void ReleaseActiveScene()
        {
            if (!claimedActiveScene) return;
            claimedActiveScene = false;

            if (!Application.isPlaying) return;
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousActiveScene);
        }

        private UnityEngine.SceneManagement.Scene previousActiveScene;
        private bool claimedActiveScene;

        /// <summary>Same reason as <c>NebulaMainMenuHost.DestroyNow</c>: outside play mode <c>Destroy</c> is deferred and errors, and an EditMode fixture would leak a director into the next test.</summary>
        private static void DestroyNow(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
