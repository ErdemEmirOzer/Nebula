using System;
using System.IO;
using UnityEngine;
using Nebula.Combat;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.UI.Menu
{
    [Serializable]
    public class NebulaAccessibilitySettingsDocument
    {
        public int settingsVersion;
        public float uiScale = 1f;
        public float hudScale = 1f;
        public bool reducedMotion;
        public bool motionOff;
        public bool cameraComfort;
        public bool flashReduction;
        public bool photosensitivitySafeMode;
        public bool audioVisualCueForWarnings = true;
        public bool highContrast;
    }

    /// <summary>
    /// Player-wide interface/accessibility preferences. These deliberately live
    /// beside input bindings, outside campaign saves, and use the same
    /// temp-write/read-back discipline so a partial file never becomes success.
    /// </summary>
    public static class NebulaAccessibilitySettingsStore
    {
        public const int CurrentVersion = 1;
        public static string DirectoryOverride { get; set; }
        public static bool WritesEnabled { get; set; } = true;
        public static string LastStatus { get; private set; } = "";

        public static string Directory =>
            DirectoryOverride
            ?? Platform.NebulaSmokeDataRoot.Folder("settings")
            ?? Platform.NebulaEditorScratch.Folder("settings")
            ?? Path.Combine(Application.persistentDataPath, "nebula", "settings");

        public static string FilePath => Path.Combine(Directory, "accessibility.json");
        private static string TempPath => Path.Combine(Directory, "accessibility.tmp.json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void LoadOnStartup() => Load();

        public static bool Load()
        {
            try
            {
                if (!File.Exists(FilePath)) { LastStatus = "no settings file"; return false; }
                NebulaAccessibilitySettingsDocument document =
                    JsonUtility.FromJson<NebulaAccessibilitySettingsDocument>(File.ReadAllText(FilePath));
                if (document == null || document.settingsVersion != CurrentVersion)
                { LastStatus = "settings version unreadable — defaults kept"; return false; }

                NebulaUiShell.UiScale = document.uiScale;
                CombatAccessibilitySettings.HudScale = CombatAccessibilitySettings.ClampHudScale(document.hudScale);
                AnimationAccessibility.ReducedMotion = document.reducedMotion;
                AnimationAccessibility.MotionOff = document.motionOff;
                AnimationAccessibility.CameraComfort = document.cameraComfort;
                LightingAccessibility.FlashReduction = document.flashReduction;
                LightingAccessibility.PhotosensitivitySafeMode = document.photosensitivitySafeMode;
                CombatAccessibilitySettings.AudioVisualCueForWarnings = document.audioVisualCueForWarnings;
                LightingAccessibility.HighContrast = document.highContrast;
                LastStatus = "loaded";
                return true;
            }
            catch (Exception e)
            {
                LastStatus = "load failed: " + e.Message;
                return false;
            }
        }

        public static bool Save()
        {
            if (!WritesEnabled) { LastStatus = "writes disabled"; return false; }
            try
            {
                var document = new NebulaAccessibilitySettingsDocument
                {
                    settingsVersion = CurrentVersion,
                    uiScale = NebulaUiShell.UiScale,
                    hudScale = CombatAccessibilitySettings.HudScale,
                    reducedMotion = AnimationAccessibility.ReducedMotion,
                    motionOff = AnimationAccessibility.MotionOff,
                    cameraComfort = AnimationAccessibility.CameraComfort,
                    flashReduction = LightingAccessibility.FlashReduction,
                    photosensitivitySafeMode = LightingAccessibility.PhotosensitivitySafeMode,
                    audioVisualCueForWarnings = CombatAccessibilitySettings.AudioVisualCueForWarnings,
                    highContrast = LightingAccessibility.HighContrast
                };
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(TempPath, JsonUtility.ToJson(document, true));
                NebulaAccessibilitySettingsDocument readBack =
                    JsonUtility.FromJson<NebulaAccessibilitySettingsDocument>(File.ReadAllText(TempPath));
                if (readBack == null || readBack.settingsVersion != CurrentVersion)
                { File.Delete(TempPath); LastStatus = "write failed validation"; return false; }
                File.Copy(TempPath, FilePath, true);
                File.Delete(TempPath);
                LastStatus = "saved";
                return true;
            }
            catch (Exception e)
            {
                LastStatus = "save failed: " + e.Message;
                Debug.LogWarning("[Nebula] Accessibility settings save failed: " + e.Message);
                return false;
            }
        }
    }
}
