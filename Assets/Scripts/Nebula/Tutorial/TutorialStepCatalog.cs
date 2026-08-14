using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Nebula.Tutorial
{
    /// <summary>
    /// The ordered curriculum: every step, in chapter order A→K and in
    /// registration order inside a chapter, numbered contiguously from 1.
    /// <para>
    /// The numbering is done here and nowhere else. A step that carried its own
    /// global number would make "42 / 61" a fact forty-odd separate constants
    /// have to agree about, and inserting one step into chapter C would silently
    /// renumber chapter J out from under every test that named a number. Here it
    /// is a property of how the list is built, so it cannot drift.
    /// </para>
    /// <para>
    /// An empty catalog is a legal, working state, not an error: chapter files
    /// arrive one at a time, and the director, the BÖLÜMLER list and the HUD all
    /// have to keep compiling and running while they do.
    /// </para>
    /// </summary>
    public static class TutorialStepCatalog
    {
        /// <summary>The chapter headings, in curriculum order. The letters are the player's, so they live with the chapter rather than being derived from the enum at the call site.</summary>
        private static readonly TutorialChapter[] ChapterList =
        {
            new TutorialChapter(TutorialChapterId.Flight,      "A", "UÇUŞ"),
            new TutorialChapter(TutorialChapterId.Exploration, "B", "KEŞİF"),
            new TutorialChapter(TutorialChapterId.Extraction,  "C", "ÇIKARIM"),
            new TutorialChapter(TutorialChapterId.Economy,     "D", "EKONOMİ"),
            new TutorialChapter(TutorialChapterId.Progression, "E", "İLERLEME"),
            new TutorialChapter(TutorialChapterId.Hangar,      "F", "HANGAR"),
            new TutorialChapter(TutorialChapterId.Structures,  "G", "YAPILAR"),
            new TutorialChapter(TutorialChapterId.Combat,      "H", "SAVAŞ"),
            new TutorialChapter(TutorialChapterId.Raids,       "I", "AKINLAR"),
            new TutorialChapter(TutorialChapterId.Dominance,   "J", "HÂKİMİYET"),
            new TutorialChapter(TutorialChapterId.Session,     "K", "OTURUM"),
        };

        private static readonly Dictionary<TutorialChapterId, List<TutorialStepDefinition>> Registered =
            new Dictionary<TutorialChapterId, List<TutorialStepDefinition>>();

        private static readonly List<TutorialStepDefinition> Ordered = new List<TutorialStepDefinition>();
        private static readonly Dictionary<string, TutorialStepDefinition> ById =
            new Dictionary<string, TutorialStepDefinition>(StringComparer.Ordinal);

        private static bool discovered;
        private static bool orderedDirty = true;

        // ---- public surface ---------------------------------------------------

        public static IReadOnlyList<TutorialChapter> Chapters => ChapterList;

        public static IReadOnlyList<TutorialStepDefinition> All
        {
            get { EnsureBuilt(); return Ordered; }
        }

        public static int Count
        {
            get { EnsureBuilt(); return Ordered.Count; }
        }

        /// <summary>The step with this stable id, or <c>null</c>. Ids are exact and case-sensitive: they are test/deep-entry keys, never player-facing text.</summary>
        public static TutorialStepDefinition Find(string stepId)
        {
            EnsureBuilt();
            if (string.IsNullOrEmpty(stepId)) return null;
            return ById.TryGetValue(stepId, out TutorialStepDefinition step) ? step : null;
        }

        /// <summary>The step at a 1-based global position, or <c>null</c> when the number is outside the curriculum.</summary>
        public static TutorialStepDefinition ByGlobalNumber(int number)
        {
            EnsureBuilt();
            if (number < 1 || number > Ordered.Count) return null;
            return Ordered[number - 1];
        }

        /// <summary>The steps of one chapter, in their registered order, already numbered.</summary>
        public static IReadOnlyList<TutorialStepDefinition> InChapter(TutorialChapterId chapter)
        {
            EnsureBuilt();
            return Registered.TryGetValue(chapter, out List<TutorialStepDefinition> steps)
                ? (IReadOnlyList<TutorialStepDefinition>)steps
                : Array.Empty<TutorialStepDefinition>();
        }

        public static TutorialChapter Chapter(TutorialChapterId id)
        {
            for (int i = 0; i < ChapterList.Length; i++)
                if (ChapterList[i].Id == id) return ChapterList[i];
            return null;
        }

        /// <summary>
        /// The aggregation seam. A chapter file hands its steps over here; the
        /// catalog owns where they land and what they are numbered.
        /// <para>
        /// Registering the same chapter twice replaces it rather than appending,
        /// so a domain reload that re-runs discovery cannot double the curriculum.
        /// A duplicate step id is rejected loudly: two steps answering to "C-04"
        /// would make deep entry pick whichever was registered first, silently.
        /// </para>
        /// </summary>
        public static void Register(TutorialChapterId chapter, IEnumerable<TutorialStepDefinition> steps)
        {
            var list = new List<TutorialStepDefinition>();
            if (steps != null)
                foreach (TutorialStepDefinition step in steps)
                    if (step != null) list.Add(step);

            Registered[chapter] = list;
            orderedDirty = true;
        }

        /// <summary>Test-facing: forgets every registration and re-arms discovery, so a fixture can build a catalog of its own and leave nothing behind.</summary>
        public static void ResetForTests()
        {
            Registered.Clear();
            Ordered.Clear();
            ById.Clear();
            discovered = false;
            orderedDirty = true;
        }

        /// <summary>Test-facing: registers a fixture's own steps and suppresses discovery, so an assertion about numbering is not at the mercy of how many chapters have shipped.</summary>
        public static void OverrideForTests(TutorialChapterId chapter, IEnumerable<TutorialStepDefinition> steps)
        {
            Registered.Clear();
            discovered = true;
            Register(chapter, steps);
        }

        // ---- building ---------------------------------------------------------

        private static void EnsureBuilt()
        {
            if (!discovered) Discover();
            if (!orderedDirty) return;

            Ordered.Clear();
            ById.Clear();

            int globalNumber = 0;
            for (int c = 0; c < ChapterList.Length; c++)
            {
                if (!Registered.TryGetValue(ChapterList[c].Id, out List<TutorialStepDefinition> steps)) continue;

                for (int s = 0; s < steps.Count; s++)
                {
                    TutorialStepDefinition step = steps[s];
                    if (ById.ContainsKey(step.StepId))
                    {
                        Debug.LogError($"[Tutorial] Duplicate step id '{step.StepId}' -- the second one is dropped. " +
                                       "Deep entry and every test that names an id resolve to exactly one step.");
                        continue;
                    }

                    step.GlobalNumber = ++globalNumber;
                    Ordered.Add(step);
                    ById[step.StepId] = step;
                }
            }

            orderedDirty = false;
        }

        /// <summary>
        /// Finds the chapter files that exist today.
        /// <para>
        /// Reflection rather than a hand-written list of eleven <c>using</c>s: the
        /// chapters land one file at a time, and a catalog that named a type it
        /// could not yet see would not compile -- which would block the HUD and
        /// the director on work that has not started. The convention is exact and
        /// narrow: a public static class in <c>Nebula.Tutorial.Steps</c> with a
        /// <c>public static IEnumerable&lt;TutorialStepDefinition&gt; Build()</c>.
        /// </para>
        /// </summary>
        private static void Discover()
        {
            discovered = true;
            if (Registered.Count > 0) return; // an explicit Register already won

            Type[] types;
            try { types = typeof(TutorialStepCatalog).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException partial) { types = partial.Types; }

            var found = new List<(TutorialChapterId chapter, MethodInfo build, string name)>();

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null || !type.IsClass || !type.IsAbstract || !type.IsSealed) continue; // static class
                if (type.Namespace != "Nebula.Tutorial.Steps") continue;

                MethodInfo build = type.GetMethod("Build", BindingFlags.Public | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (build == null || !typeof(IEnumerable<TutorialStepDefinition>).IsAssignableFrom(build.ReturnType)) continue;

                if (!TryChapterFromTypeName(type.Name, out TutorialChapterId chapter)) continue;
                found.Add((chapter, build, type.Name));
            }

            for (int i = 0; i < found.Count; i++)
            {
                try
                {
                    var steps = (IEnumerable<TutorialStepDefinition>)found[i].build.Invoke(null, null);
                    Register(found[i].chapter, steps);
                }
                catch (Exception error)
                {
                    // One broken chapter must not take the whole curriculum down:
                    // the other ten still run, and the failure is named.
                    Debug.LogError($"[Tutorial] {found[i].name}.Build() threw; that chapter is empty. {error}");
                }
            }
        }

        /// <summary>Maps <c>Chapter&lt;X&gt;Steps</c> to its chapter, by the enum name the file is named after ("ChapterFlightSteps") or by its letter ("ChapterASteps").</summary>
        private static bool TryChapterFromTypeName(string typeName, out TutorialChapterId chapter)
        {
            chapter = default;
            if (typeName == null || !typeName.StartsWith("Chapter", StringComparison.Ordinal)) return false;

            string middle = typeName.Substring("Chapter".Length);
            if (middle.EndsWith("Steps", StringComparison.Ordinal))
                middle = middle.Substring(0, middle.Length - "Steps".Length);
            if (middle.Length == 0) return false;

            for (int i = 0; i < ChapterList.Length; i++)
            {
                if (string.Equals(ChapterList[i].Letter, middle, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ChapterList[i].Id.ToString(), middle, StringComparison.OrdinalIgnoreCase))
                {
                    chapter = ChapterList[i].Id;
                    return true;
                }
            }
            return false;
        }
    }
}
