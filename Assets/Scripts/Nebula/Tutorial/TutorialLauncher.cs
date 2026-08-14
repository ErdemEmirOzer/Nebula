using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Nebula.World;

namespace Nebula.Tutorial
{
    /// <summary>
    /// The one-frame handoff from the menu scene to the tutorial scene.
    /// <para>
    /// A scene load cannot carry a constructor argument, so the request is
    /// parked here and taken by <see cref="TutorialSceneHost"/> in its
    /// <c>Awake</c>. It is consumed on read and cleared: this is a baton, not
    /// state. Nothing is retained after the host has it, so a tutorial scene
    /// loaded a second time cannot inherit the first run's request.
    /// </para>
    /// </summary>
    public static class TutorialRunRequest
    {
        public readonly struct Pending
        {
            public TutorialRunMode Mode { get; }
            public string StepId { get; }
            public ITutorialHud Hud { get; }
            public TutorialLauncher Owner { get; }

            public Pending(TutorialRunMode mode, string stepId, ITutorialHud hud, TutorialLauncher owner)
            {
                Mode = mode;
                StepId = stepId;
                Hud = hud;
                Owner = owner;
            }
        }

        private static bool armed;
        private static Pending pending;

        public static bool IsArmed => armed;

        public static void Set(TutorialRunMode mode, string stepId, ITutorialHud hud, TutorialLauncher owner)
        {
            pending = new Pending(mode, stepId, hud, owner);
            armed = true;
        }

        public static bool TryConsume(out Pending request)
        {
            request = pending;
            bool had = armed;
            Clear();
            return had;
        }

        /// <summary>Drops an unconsumed request -- a failed scene load, an abandoned start, a test tearing down.</summary>
        public static void Clear()
        {
            pending = default;
            armed = false;
        }
    }

    /// <summary>
    /// How the menus ask for a tutorial run, and how they are told it ended.
    /// <para>
    /// Deliberately the same shape as <c>NebulaSessionLauncher</c>: the menu
    /// holds this and never the scene, the host or the director, so a menu
    /// cannot compose a world as a side effect of being drawn. The difference is
    /// what it starts -- this one loads a separate scene and never calls
    /// <c>BeginSession</c>, so no campaign is created, resumed or written.
    /// </para>
    /// <para>
    /// The tutorial scene is loaded <b>additively</b> rather than singly. A
    /// single load would destroy this launcher along with the menu scene, and
    /// with it the <see cref="RunEnded"/> subscription the menu is waiting on;
    /// additive keeps one owner alive across the whole run and makes the exit a
    /// scene unload, which is the strongest teardown available -- every
    /// GameObject the tutorial made goes with it.
    /// </para>
    /// </summary>
    public sealed class TutorialLauncher : ITutorialLauncher
    {
        public const string TutorialScenePath = "Assets/Scenes/NebulaTutorial.unity";
        public const string TutorialSceneName = "NebulaTutorial";

        private readonly SolarSystemBootstrap bootstrap;

        /// <summary>
        /// The card the run draws with. Set by whoever owns the tutorial UI; left
        /// null, the director records instead of drawing, which keeps a headless
        /// run working rather than throwing.
        /// </summary>
        public ITutorialHud Hud { get; set; }

        /// <summary>
        /// Loads the tutorial scene additively. Overridable so a test can run the
        /// whole launcher without a scene at all -- there is no input to simulate
        /// here, and there should be no scene to stage either.
        /// </summary>
        public Func<bool> LoadTutorialScene { get; set; }

        /// <summary>Unloads it again. Paired with <see cref="LoadTutorialScene"/>; override both or neither.</summary>
        public Action UnloadTutorialScene { get; set; }

        /// <summary>Test-facing: how many runs this launcher has started.</summary>
        public int RunsStarted { get; private set; }

        /// <summary>True from the moment a run is requested until <see cref="RunEnded"/> has fired.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>The mode of the run in flight.</summary>
        public TutorialRunMode CurrentMode { get; private set; }

        public event Action<TutorialRunMode, bool> RunEnded;

        public TutorialLauncher(SolarSystemBootstrap solarSystemBootstrap = null)
        {
            bootstrap = solarSystemBootstrap;
            LoadTutorialScene = LoadAdditive;
            UnloadTutorialScene = UnloadAdditive;
        }

        public void StartFromBeginning() => Begin(TutorialRunMode.Sequential, null);

        public void StartSingleStep(string stepId) => Begin(TutorialRunMode.Standalone, stepId);

        private void Begin(TutorialRunMode mode, string stepId)
        {
            if (IsRunning) return; // a button pressed twice must not be two tutorial worlds

            IsRunning = true;
            CurrentMode = mode;
            RunsStarted++;

            // The menu stands down first. Its shell owns the modal gate, and two
            // shells with one gate is the failure NebulaMainMenuHost.Dismiss
            // exists to prevent.
            DismissMainMenu();

            TutorialRunRequest.Set(mode, stepId, Hud, this);

            bool loaded = LoadTutorialScene != null && LoadTutorialScene();
            if (loaded) return;

            TutorialRunRequest.Clear();
            Debug.LogError($"[Tutorial] '{TutorialSceneName}' could not be loaded. " +
                           "It must be in Build Settings, after Assets/Scenes/Nebula.unity.");
            NotifyRunEnded(mode, false);
        }

        /// <summary>
        /// Abandons the run. The director tears the current step down first, so
        /// leaving mid-step returns every spawned object and every pooled lease
        /// exactly as completing it would.
        /// </summary>
        public void ExitToMainMenu()
        {
            if (!IsRunning) { RestoreMainMenu(); return; }

            TutorialSceneHost host = FindHost();
            if (host != null && host.Director != null) { host.Director.Abandon(); return; }

            NotifyRunEnded(CurrentMode, false);
        }

        /// <summary>
        /// Called by the host when the run is over, by completion or by the player
        /// leaving. Composition-only entry point -- named like
        /// <c>NebulaContext.SetX</c> for the same reason, so it never reads as
        /// something gameplay may call.
        /// </summary>
        public void NotifyRunEnded(TutorialRunMode mode, bool completed)
        {
            if (!IsRunning) return;
            IsRunning = false;

            TutorialSceneHost host = FindHost();
            host?.ShutDown();

            TutorialRunRequest.Clear();
            UnloadTutorialScene?.Invoke();
            RestoreMainMenu();

            RunEnded?.Invoke(mode, completed);
        }

        // ---- scene and menu plumbing -----------------------------------------

        private static TutorialSceneHost FindHost()
        {
            // The tutorial scene holds exactly one, and only for the length of a
            // run. Searching is the only option across a scene boundary, and it is
            // bounded to this one type rather than a general service lookup.
            return UnityEngine.Object.FindObjectOfType<TutorialSceneHost>();
        }

        private static bool LoadAdditive()
        {
            if (!Application.isPlaying) return false;
            try
            {
                SceneManager.LoadScene(TutorialSceneName, LoadSceneMode.Additive);
                return true;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                return false;
            }
        }

        private static void UnloadAdditive()
        {
            if (!Application.isPlaying) return;
            Scene scene = SceneManager.GetSceneByName(TutorialSceneName);
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }

        private void DismissMainMenu()
        {
            UI.Session.NebulaMainMenuHost menu = UnityEngine.Object.FindObjectOfType<UI.Session.NebulaMainMenuHost>();
            menu?.Dismiss();
        }

        /// <summary>
        /// Brings the main menu back. <c>EndSession</c> rather than a direct
        /// <c>Create</c>: in MainMenu boot mode it is the bootstrap's own contract
        /// that the menu is what exists when a session does not, and it refuses to
        /// build a second host when one is already there.
        /// </summary>
        private void RestoreMainMenu()
        {
            if (bootstrap == null) return;
            bootstrap.EndSession();
        }
    }
}
