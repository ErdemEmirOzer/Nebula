using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Nebula.Tutorial;

namespace Nebula.EditorTools
{
    /// <summary>
    /// Generates <c>Assets/Scenes/NebulaTutorial.unity</c>.
    /// <para>
    /// The scene is one GameObject carrying one <see cref="TutorialSceneHost"/>,
    /// exactly as <c>NebulaSceneBuilder</c> produces one GameObject carrying
    /// <c>SolarSystemBootstrap</c>: every camera, light, ship and HUD in a
    /// tutorial run is created at runtime by composition, so there is nothing
    /// else for a scene to hold. Generated rather than hand-dragged so it is
    /// reproducible, reviewable as code, and rebuildable in batch mode after a
    /// merge.
    /// </para>
    /// </summary>
    public static class NebulaTutorialSceneBuilder
    {
        public const string TutorialScenePath = "Assets/Scenes/NebulaTutorial.unity";

        /// <summary>The campaign scene. It must stay first and enabled: it is the boot scene, and index 0 is what a player build opens.</summary>
        public const string MainScenePath = "Assets/Scenes/Nebula.unity";

        [MenuItem("Nebula/Build Tutorial Scene")]
        public static void BuildScene()
        {
            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var hostGO = new GameObject("Tutorial Scene Host");
            hostGO.AddComponent<TutorialSceneHost>();

            Directory.CreateDirectory("Assets/Scenes");
            if (!EditorSceneManager.SaveScene(scene, TutorialScenePath))
                throw new System.Exception("[Nebula] Failed to save the tutorial scene at " + TutorialScenePath);

            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();

            Debug.Log("[Nebula] Tutorial scene built OK at " + TutorialScenePath);
        }

        /// <summary>
        /// Appends the tutorial scene to Build Settings without disturbing what is
        /// already there.
        /// <para>
        /// Additive on purpose. <c>NebulaSceneBuilder.BuildScene</c> assigns
        /// <c>EditorBuildSettings.scenes</c> a brand new one-element array, which
        /// is correct for the only scene that existed when it was written and
        /// would now silently drop the tutorial. This method reads the current
        /// list, keeps every existing entry and its enabled flag, guarantees
        /// <see cref="MainScenePath"/> is first and enabled, and adds the tutorial
        /// only if it is not already listed.
        /// </para>
        /// </summary>
        [MenuItem("Nebula/Register Tutorial Scene In Build Settings")]
        public static void RegisterInBuildSettings()
        {
            var kept = new List<EditorBuildSettingsScene>();
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;

            // The main scene first, enabled, whatever order it was in before.
            EditorBuildSettingsScene main = Find(existing, MainScenePath);
            kept.Add(new EditorBuildSettingsScene(MainScenePath, true));

            for (int i = 0; i < existing.Length; i++)
            {
                EditorBuildSettingsScene entry = existing[i];
                if (entry == null || string.IsNullOrEmpty(entry.path)) continue;
                if (entry.path == MainScenePath) continue;          // already placed, flag preserved as enabled
                if (entry.path == TutorialScenePath) continue;      // appended below, once
                kept.Add(entry);                                    // every other entry, flag untouched
            }

            // Enabled: it is loaded additively by name at runtime, and a disabled
            // entry is not in the build, so BAŞTAN BAŞLA would fail in a player
            // while working perfectly in the Editor.
            kept.Add(new EditorBuildSettingsScene(TutorialScenePath, true));

            EditorBuildSettings.scenes = kept.ToArray();

            if (main == null)
                Debug.LogWarning("[Nebula] " + MainScenePath + " was not in Build Settings and has been added as the first scene.");
            Debug.Log("[Nebula] Build Settings now list " + kept.Count + " scene(s); " + TutorialScenePath + " is registered.");
        }

        private static EditorBuildSettingsScene Find(EditorBuildSettingsScene[] scenes, string path)
        {
            for (int i = 0; i < scenes.Length; i++)
                if (scenes[i] != null && scenes[i].path == path) return scenes[i];
            return null;
        }
    }
}
