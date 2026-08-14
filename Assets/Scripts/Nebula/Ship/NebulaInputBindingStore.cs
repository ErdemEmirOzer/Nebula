using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Nebula.Ship
{
    /// <summary>One saved binding. Named by action id, never by ordinal — a reordered catalog must not shuffle a player's keys.</summary>
    [Serializable]
    public class InputBindingEntry
    {
        public string actionId;
        public string key;
    }

    /// <summary>The whole file. Versioned, so an older shape can be recognised and declined rather than misread.</summary>
    [Serializable]
    public class InputBindingDocument
    {
        public int settingsVersion;
        public List<InputBindingEntry> bindings = new List<InputBindingEntry>();
    }

    /// <summary>
    /// Where a player's rebinds live between sessions.
    /// <para>
    /// Deliberately <b>not</b> the campaign save. A key layout is a property of the
    /// person and their keyboard, not of a run: loading an old save, starting a new
    /// campaign, or deleting every slot must all leave it exactly where it was. Put
    /// in the save it would have been none of those things, and it would have moved
    /// <c>SaveService.CurrentSchema</c> for a setting that has nothing to do with the
    /// world.
    /// </para>
    /// <para>
    /// The repo had no user-settings authority at all to reuse — there is no
    /// <c>PlayerPrefs</c> call anywhere under <c>Assets/</c>, and the only file writers
    /// are the campaign save, telemetry and the session log. So this is a new store,
    /// kept as small as the job: one directory, one file, input bindings only. It
    /// copies the save's write discipline (temp file, read back, then commit) because
    /// that discipline is the reason a half-written settings file cannot cost a player
    /// their layout — not because it shares any of its code or its schema.
    /// </para>
    /// </summary>
    public static class NebulaInputBindingStore
    {
        /// <summary>Bumped only when the file's shape changes. An unrecognised version is declined whole.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Directory override for tests, so a run never writes over the operator's real settings.</summary>
        public static string DirectoryOverride { get; set; }

        /// <summary>
        /// Set false to make <see cref="Save"/> a no-op. Exists for the mutation proof
        /// the persistence tests require: disabling the write must turn the restart
        /// test red, and a test that cannot break is not evidence of anything.
        /// </summary>
        public static bool WritesEnabled { get; set; } = true;

        /// <summary>
        /// Test override, then the smoke sandbox, then the engine's folder. See
        /// <c>SaveService.Dir</c> for the ordering rationale; the sandbox term is
        /// <c>null</c> in every shipped launch, so a player's rebinds always come from
        /// <c>Application.persistentDataPath</c>.
        /// </summary>
        public static string Directory =>
            DirectoryOverride
            ?? Platform.NebulaSmokeDataRoot.Folder("settings")
            ?? Platform.NebulaEditorScratch.Folder("settings")
            ?? Path.Combine(Application.persistentDataPath, "nebula", "settings");

        public static string FilePath => Path.Combine(Directory, "input-bindings.json");

        private static string TempPath => Path.Combine(Directory, "input-bindings.tmp.json");

        /// <summary>The last thing the store did, for the settings screen and for tests.</summary>
        public static string LastStatus { get; private set; } = "";

        /// <summary>
        /// Reads the saved layout at startup, before any scene runs.
        /// <para>
        /// Here rather than in a session bootstrap because bindings outlive sessions:
        /// the main menu's own settings screen can show and change them before a
        /// campaign exists, and it must show the ones the player actually has.
        /// </para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void LoadOnStartup() => Load();

        /// <summary>
        /// Applies the saved layout over the shipped defaults. Returns how many
        /// bindings were taken.
        /// <para>
        /// Every entry goes through <see cref="CombatBindings.TrySetBinding"/>, the
        /// same validator the settings screen uses. A file that names a reserved key,
        /// a key another action already took, or an action that no longer exists loses
        /// that one entry and nothing else — a settings file is not a reason to put
        /// the game in a state the settings screen would have refused.
        /// </para>
        /// </summary>
        public static int Load()
        {
            InputBindingDocument document = Read(out string problem);
            if (document == null)
            {
                LastStatus = problem;
                return 0;
            }

            int applied = 0, rejected = 0;
            for (int i = 0; i < document.bindings.Count; i++)
            {
                InputBindingEntry entry = document.bindings[i];
                if (entry == null) { rejected++; continue; }

                InputActionDefinition action = NebulaInputActions.Find(entry.actionId);
                if (action == null || !action.Rebindable) { rejected++; continue; }
                if (!Enum.TryParse(entry.key, out KeyCode key)) { rejected++; continue; }
                if (!Enum.IsDefined(typeof(KeyCode), key)) { rejected++; continue; }

                if (NebulaInputActions.TrySetBinding(action.Id, key)) applied++;
                else rejected++;
            }

            LastStatus = rejected == 0
                ? $"loaded {applied} binding(s)"
                : $"loaded {applied} binding(s), ignored {rejected}";
            return applied;
        }

        /// <summary>
        /// Writes the current rebindable layout. Temp file first, read back, then
        /// commit — a crash mid-write leaves the previous file intact rather than a
        /// truncated one.
        /// </summary>
        public static bool Save()
        {
            if (!WritesEnabled) { LastStatus = "writes disabled"; return false; }

            try
            {
                var document = new InputBindingDocument { settingsVersion = CurrentVersion };
                foreach (InputActionDefinition action in NebulaInputActions.All)
                {
                    // Load accepts every rebindable action; Save used to accept only the
                    // combat rows, so an extraction/logistics/POI rebind worked for the
                    // rest of the session and was silently gone on the next launch.
                    if (!action.Persisted) continue;
                    document.bindings.Add(new InputBindingEntry
                    {
                        actionId = action.Id,
                        key = action.Key.ToString()
                    });
                }

                System.IO.Directory.CreateDirectory(Directory);
                string json = JsonUtility.ToJson(document, true);

                File.WriteAllText(TempPath, json);
                if (JsonUtility.FromJson<InputBindingDocument>(File.ReadAllText(TempPath)) == null)
                {
                    File.Delete(TempPath);
                    LastStatus = "write failed validation";
                    return false;
                }

                File.Copy(TempPath, FilePath, true);
                File.Delete(TempPath);
                LastStatus = $"saved {document.bindings.Count} binding(s)";
                return true;
            }
            catch (Exception e)
            {
                LastStatus = "save failed: " + e.Message;
                Debug.LogWarning("[Nebula] Input binding save failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Forgets the player's layout entirely, so the shipped defaults are what the
        /// next session reads.
        /// <para>
        /// The file is removed rather than rewritten with today's defaults. A file
        /// holding a copy of the defaults would pin a player who pressed "restore
        /// defaults" to <i>this build's</i> answer forever; absence means "whatever
        /// the game ships with", which is what the control actually promises.
        /// </para>
        /// </summary>
        public static bool Clear()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                if (File.Exists(TempPath)) File.Delete(TempPath);
                LastStatus = "cleared";
                return true;
            }
            catch (Exception e)
            {
                LastStatus = "clear failed: " + e.Message;
                return false;
            }
        }

        /// <summary>True when a saved layout exists on disk.</summary>
        public static bool Exists() => File.Exists(FilePath);

        private static InputBindingDocument Read(out string problem)
        {
            problem = "";
            try
            {
                if (!File.Exists(FilePath)) { problem = "no settings file"; return null; }

                string json = File.ReadAllText(FilePath);
                InputBindingDocument document;
                try { document = JsonUtility.FromJson<InputBindingDocument>(json); }
                catch (Exception) { problem = "settings file unreadable — defaults kept"; return null; }

                if (document == null) { problem = "settings file unreadable — defaults kept"; return null; }
                if (document.settingsVersion != CurrentVersion)
                {
                    // Declined, not deleted and not guessed at. A future version can
                    // migrate it; today the honest answer is the shipped layout.
                    problem = $"settings version {document.settingsVersion} not readable by this build — defaults kept";
                    return null;
                }

                document.bindings ??= new List<InputBindingEntry>();
                return document;
            }
            catch (Exception e)
            {
                problem = "settings read failed: " + e.Message;
                return null;
            }
        }
    }
}
