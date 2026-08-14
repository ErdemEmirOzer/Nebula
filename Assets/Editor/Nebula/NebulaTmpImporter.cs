using UnityEditor;
using UnityEngine;

namespace Nebula.EditorTools
{
    /// <summary>
    /// Imports TextMeshPro's essential resources without the GUI prompt.
    /// <para>
    /// The package has been in the manifest all along; only its resources were
    /// missing, which is why every label in the game was still on the Arial-metric
    /// bitmap font. The importer is normally a modal dialog, so a headless project
    /// can never get past it -- this is the same call the dialog makes.
    /// </para>
    /// </summary>
    public static class NebulaTmpImporter
    {
        [MenuItem("Nebula/UI/Import TextMeshPro Essentials")]
        public static void Import()
        {
            const string marker = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(marker) != null)
            {
                Debug.Log("[Nebula] TMP essentials already imported.");
                return;
            }

            string package = System.IO.Path.Combine(
                UnityEngine.Application.dataPath, "..", "Library", "PackageCache");

            foreach (string dir in System.IO.Directory.GetDirectories(package, "com.unity.textmeshpro*"))
            {
                string essentials = System.IO.Path.Combine(
                    dir, "Package Resources", "TMP Essential Resources.unitypackage");
                if (!System.IO.File.Exists(essentials)) continue;

                AssetDatabase.ImportPackage(essentials, false);
                AssetDatabase.Refresh();
                Debug.Log($"[Nebula] Imported TMP essentials from {dir}");
                return;
            }

            Debug.LogError("[Nebula] Could not find TMP Essential Resources.unitypackage.");
        }
    }
}
