using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Nebula.World;

namespace Nebula.EditorTools
{
    /// <summary>
    /// Creates the Nebula space scene: an empty scene containing only the
    /// <see cref="SolarSystemBootstrap"/> object (everything else is generated
    /// at runtime). Runable from the menu or via -executeMethod in batch mode.
    /// </summary>
    public static class NebulaSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        [MenuItem("Nebula/Build Space Scene")]
        public static void BuildScene()
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bootstrapGO = new GameObject("Solar System Bootstrap");
            bootstrapGO.AddComponent<SolarSystemBootstrap>();

            Directory.CreateDirectory("Assets/Scenes");
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new System.Exception("[Nebula] Failed to save scene at " + ScenePath);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log("[Nebula] Scene built OK at " + ScenePath);
        }
    }
}
