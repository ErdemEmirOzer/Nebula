using UnityEditor;
using UnityEngine;

namespace Nebula.Editor
{
    /// <summary>
    /// Deterministic import profile for final Nebula audio masters. Source WAVs
    /// remain 48 kHz/24-bit; Unity chooses an appropriate runtime representation
    /// by semantic folder and duration role.
    /// </summary>
    public sealed class NebulaAudioImportSettings : AssetPostprocessor
    {
        private const string Root = "Assets/Resources/Audio/Nebula/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root, System.StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            bool ui = assetPath.Contains("/UI/") || assetPath.Contains("/Navigation/") ||
                      assetPath.Contains("/Status/") || assetPath.Contains("/Remote/");
            bool listenerWarning = assetPath.EndsWith("ship_energy_low.wav") ||
                                   assetPath.EndsWith("ship_overheat_warning.wav");
            bool listenerBed = assetPath.EndsWith("ship_cockpit_ambience_loop.wav") ||
                               assetPath.EndsWith("world_deep_space_loop.wav");
            bool loop = assetPath.EndsWith("_loop.wav");

            importer.forceToMono = !(ui || listenerWarning || listenerBed);
            importer.ambisonic = false;
            importer.loadInBackground = loop;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = !loop;
            settings.quality = ui ? 0.75f : 0.80f;
            if (loop)
            {
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
            }
            else
            {
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
            }
            importer.defaultSampleSettings = settings;
        }

        [MenuItem("Nebula/Audio/Reimport Final Audio")]
        public static void ReimportFinalAudio()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { Root.TrimEnd('/') });
            foreach (string guid in guids)
                AssetDatabase.ImportAsset(
                    AssetDatabase.GUIDToAssetPath(guid),
                    ImportAssetOptions.ForceUpdate);
            Debug.Log($"Reimported {guids.Length} Nebula final audio clips.");
        }
    }
}
