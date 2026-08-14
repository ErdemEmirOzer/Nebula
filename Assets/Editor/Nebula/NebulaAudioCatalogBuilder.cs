using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Nebula.Audio;

namespace Nebula.Editor
{
    public static class NebulaAudioCatalogBuilder
    {
        private const string AssetPath = "Assets/Resources/Audio/NebulaAudioCatalog.asset";

        [Serializable]
        private sealed class Report
        {
            public ReportEntry[] assets;
        }

        [Serializable]
        private sealed class ReportEntry
        {
            public string eventId;
            public string assetPath;
            public string bus;
            public string spatial;
            public bool loop;
        }

        [MenuItem("Nebula/Audio/Rebuild Runtime Catalog")]
        public static void RebuildRuntimeCatalog()
        {
            var entries = new List<NebulaAudioEntry>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            for (int part = 1; part <= 4; part++)
            {
                string reportPath = Path.Combine(
                    projectRoot, "AudioProduction", $"Part{part:00}", "production-report.json");
                if (!File.Exists(reportPath))
                    throw new FileNotFoundException("Missing approved audio production report.", reportPath);
                Report report = JsonUtility.FromJson<Report>(File.ReadAllText(reportPath));
                if (report?.assets == null)
                    throw new InvalidDataException("Invalid audio production report: " + reportPath);

                foreach (ReportEntry source in report.assets)
                {
                    if (!ids.Add(source.eventId))
                        throw new InvalidDataException("Duplicate audio event id: " + source.eventId);
                    AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(source.assetPath);
                    if (clip == null)
                        throw new InvalidDataException(
                            $"Audio event '{source.eventId}' cannot load clip '{source.assetPath}'.");
                    entries.Add(new NebulaAudioEntry(
                        source.eventId,
                        ToResourcePath(source.assetPath),
                        ParseBus(source.bus),
                        source.spatial == "3DMono"
                            ? NebulaAudioSpatialMode.World3D
                            : NebulaAudioSpatialMode.ListenerRelative,
                        source.loop));
                }
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.EventId, b.EventId));
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            NebulaAudioCatalog catalog =
                AssetDatabase.LoadAssetAtPath<NebulaAudioCatalog>(AssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<NebulaAudioCatalog>();
                AssetDatabase.CreateAsset(catalog, AssetPath);
            }
            catalog.ReplaceEntries(entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Nebula runtime audio catalog rebuilt: {entries.Count} entries at {AssetPath}");
        }

        private static string ToResourcePath(string assetPath)
        {
            const string marker = "Assets/Resources/";
            if (!assetPath.StartsWith(marker, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Runtime audio must live under Resources for lazy loading: {assetPath}");
            string resourcePath = assetPath.Substring(marker.Length);
            string extension = Path.GetExtension(resourcePath);
            return string.IsNullOrEmpty(extension)
                ? resourcePath
                : resourcePath.Substring(0, resourcePath.Length - extension.Length);
        }

        private static NebulaAudioBus ParseBus(string value) => value switch
        {
            "UI" => NebulaAudioBus.UI,
            "Ship" => NebulaAudioBus.Ship,
            "Combat" => NebulaAudioBus.Combat,
            "Industry" => NebulaAudioBus.Industry,
            "World" => NebulaAudioBus.World,
            "Ambience" => NebulaAudioBus.Ambience,
            _ => throw new InvalidDataException("Unknown audio bus: " + value)
        };
    }
}
