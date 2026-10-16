using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Nebula.EditorTools
{
    /// <summary>
    /// Guarantees that every shader the game resolves <i>by name at runtime</i> is
    /// actually inside the player, by registering it in Graphics Settings'
    /// "Always Included Shaders" before a release build.
    /// <para>
    /// <b>Why this exists.</b> <c>Shader.Find</c> only sees shaders that were pulled
    /// into the build, and a built-in shader is pulled in only when some material in a
    /// scene or under <c>Resources/</c> references it. Nothing in this project
    /// references <c>Standard</c> that way any more, so <c>Shader.Find("Standard")</c>
    /// returned null in the player, <see cref="Nebula.Visuals.MaterialLibrary"/> fell
    /// back to <c>Hidden/InternalErrorShader</c>, and the shipped 0.1.0 build rendered
    /// every procedural material magenta. The build log said so twenty-eight times and
    /// no gate was reading it, because the smoke run only ever opened the main menu.
    /// </para>
    /// <para>
    /// <b>Why a build step and not a hand-edited asset.</b> Always Included Shaders is
    /// a list of object references; writing it by hand means writing Unity's internal
    /// built-in file IDs by hand, and a wrong ID silently registers the wrong shader.
    /// Resolving the names through <c>Shader.Find</c> in the Editor — where every
    /// built-in shader is present — is the only spelling that cannot be silently wrong.
    /// Running it on every release build is also what stops this regressing the next
    /// time an asset reference moves.
    /// </para>
    /// <para>
    /// <b>Groups, not a flat list.</b> The runtime asks for alternatives in order and
    /// takes the first that resolves (URP name first, built-in fallback second). So the
    /// requirement is "at least one member of this group must be present", and the one
    /// that gets registered is the first that resolves here — registering a URP shader
    /// that does not exist in this project would be a no-op, not a fix.
    /// </para>
    /// </summary>
    public static class NebulaRequiredShaders
    {
        /// <summary>
        /// One runtime shader requirement: the candidates, in the same order the runtime
        /// tries them, and the call site that asks, so a failure names the code to fix.
        /// </summary>
        private readonly struct Requirement
        {
            public readonly string CallSite;
            public readonly string[] Candidates;
            /// <summary>
            /// False for a safety net whose absence does not by itself break rendering.
            /// A missing <i>required</i> shader fails the build instead of shipping.
            /// </summary>
            public readonly bool Mandatory;

            public Requirement(string callSite, bool mandatory, params string[] candidates)
            {
                CallSite = callSite;
                Mandatory = mandatory;
                Candidates = candidates;
            }
        }

        /// <summary>
        /// Mirrors every by-name shader lookup under <c>Assets/Scripts</c>. Keep this in
        /// step with the call sites: a lookup that is not listed here is a lookup that
        /// can go magenta in a player without anything failing first.
        /// </summary>
        private static readonly Requirement[] Required =
        {
            new Requirement("MaterialLibrary.CreateLit", true,
                "Universal Render Pipeline/Lit", "Standard"),
            new Requirement("MaterialLibrary.CreateUnlit(Color)", true,
                "Universal Render Pipeline/Unlit", "Unlit/Color"),
            new Requirement("MaterialLibrary.CreateUnlit(Texture)", true,
                "Universal Render Pipeline/Unlit", "Unlit/Texture"),
            new Requirement("MaterialLibrary.CreateParticle", true,
                "Legacy Shaders/Particles/Additive", "Particles/Standard Unlit",
                "Universal Render Pipeline/Particles/Unlit"),
            new Requirement("MaterialLibrary.CreateStreak", true, "Nebula/AdditiveFade"),
            new Requirement("SupportOperationPool", true, "Sprites/Default"),
            new Requirement("SkyboxBaker", true, "Skybox/Cubemap"),
            // The fallback itself. If it is missing too, a failed lookup yields a null
            // shader rather than a visible magenta one -- worse to debug, not worse to
            // look at. Not mandatory: when everything above resolves it is never reached.
            new Requirement("MaterialLibrary.FindShader fallback", false,
                "Hidden/InternalErrorShader"),
        };

        private const string SettingsPath = "ProjectSettings/GraphicsSettings.asset";
        private const string ListProperty = "m_AlwaysIncludedShaders";

        /// <summary>
        /// Registers every missing required shader and returns one report line per
        /// requirement, in the same "what was asked / what happened" form the rest of
        /// the build hardening uses.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// A mandatory requirement no candidate of which exists in the Editor. Shipping
        /// that build means shipping magenta, so it stops here instead.
        /// </exception>
        public static List<string> Ensure()
        {
            var notes = new List<string>();

            UnityEngine.Object settings = LoadSettings();
            if (settings == null)
            {
                notes.Add("Always Included Shaders: NOT APPLIED (could not load " + SettingsPath + ")");
                return notes;
            }

            var serialized = new SerializedObject(settings);
            SerializedProperty list = serialized.FindProperty(ListProperty);
            if (list == null || !list.isArray)
            {
                notes.Add("Always Included Shaders: NOT APPLIED (no " + ListProperty + " on " + SettingsPath + ")");
                return notes;
            }

            // The list is rebuilt rather than edited in place. SerializedProperty's
            // DeleteArrayElementAtIndex nulls an object-reference element instead of
            // removing it, and a null entry in this list is itself a build error, so
            // "delete the bad one" is the one operation that must not be done that way.
            var keep = new List<Shader>();
            var seen = new HashSet<Shader>();
            int dropped = 0;

            for (int i = 0; i < list.arraySize; i++)
            {
                var entry = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (entry == null)
                {
                    notes.Add("Always Included Shaders -= <empty entry> -- removed");
                    dropped++;
                    continue;
                }
                if (!IsShippable(entry))
                {
                    notes.Add("Always Included Shaders -= \"" + entry.name +
                              "\" -- removed (engine-internal, cannot be shipped)");
                    dropped++;
                    continue;
                }
                if (seen.Add(entry)) keep.Add(entry);
                else dropped++;
            }

            int originalKept = keep.Count;
            var missing = new List<string>();

            foreach (Requirement requirement in Required)
            {
                Shader resolved = null;
                string resolvedName = null;

                foreach (string candidate in requirement.Candidates)
                {
                    Shader found = Shader.Find(candidate);
                    if (found == null) continue;
                    resolved = found;
                    resolvedName = candidate;
                    break;
                }

                if (resolved == null)
                {
                    string message = requirement.CallSite + ": NONE OF " +
                                     string.Join(", ", requirement.Candidates) + " exists in this project";
                    notes.Add("Always Included Shaders -- " + message);
                    if (requirement.Mandatory) missing.Add(message);
                    continue;
                }

                if (!IsShippable(resolved))
                {
                    // Engine-internal shaders cannot be listed here, and do not need to
                    // be: the engine always has them. Listing one fails the build with
                    // "An asset is marked with HideFlags.DontSave but is included in the
                    // build" and takes Resources/unity_builtin_extra down with it.
                    notes.Add("Always Included Shaders: \"" + resolvedName + "\" (for " +
                              requirement.CallSite + ") -- present in the engine, not listed " +
                              "(engine-internal; listing it would fail the build)");
                    continue;
                }

                if (seen.Add(resolved))
                {
                    keep.Add(resolved);
                    notes.Add("Always Included Shaders += \"" + resolvedName + "\" (for " +
                              requirement.CallSite + ") -- registered");
                }
                else
                {
                    notes.Add("Always Included Shaders: \"" + resolvedName + "\" (for " +
                              requirement.CallSite + ") -- already present");
                }
            }

            int added = keep.Count - originalKept;
            if (added > 0 || dropped > 0)
            {
                list.arraySize = keep.Count;
                for (int i = 0; i < keep.Count; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }

            notes.Add("Always Included Shaders: " + added + " added, " + dropped +
                      " removed, " + keep.Count + " total after this step");

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "[Nebula] Release build refused: " + missing.Count +
                    " runtime shader lookup(s) cannot be satisfied, so the player would " +
                    "render magenta. " + string.Join(" | ", missing));
            }

            return notes;
        }

        /// <summary>
        /// Whether Unity will let this shader be an explicit build reference. Engine
        /// data carries <see cref="HideFlags.DontSave"/>; including it fails the player
        /// build outright. This is Unity's own criterion for that error, rather than a
        /// guess based on the "Hidden/" name prefix -- plenty of shippable shaders are
        /// named Hidden/ and plenty of internal ones would not be caught by the name.
        /// </summary>
        private static bool IsShippable(Shader shader) =>
            (shader.hideFlags & HideFlags.DontSave) == 0;

        /// <summary>
        /// GraphicsSettings.asset is a project-settings asset, not a project asset, so it
        /// is reached by path through <c>LoadAllAssetsAtPath</c> rather than by type.
        /// </summary>
        private static UnityEngine.Object LoadSettings()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(SettingsPath);
            if (assets == null) return null;
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset != null) return asset;
            }
            return null;
        }
    }
}
