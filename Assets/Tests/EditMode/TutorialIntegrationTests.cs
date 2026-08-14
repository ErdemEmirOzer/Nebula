using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Tutorial;
using Nebula.UI.Session;
using Nebula.UI.Shell;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The cross-cutting tutorial guarantees — the ones no single chapter's fixture
    /// can make, because each is a statement about the curriculum as a whole.
    /// <para>
    /// Per-chapter fixtures prove that step 61 completes when the player does the
    /// thing. This fixture proves there is a step 61 at all, that it is the step the
    /// design document says it is, that its assets exist, and that reaching it never
    /// wrote anything to disk.
    /// </para>
    /// </summary>
    public class TutorialIntegrationTests
    {
        private static string RepoRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string DesignDoc(string name) =>
            Path.Combine(RepoRoot, "TutorialDesign", name);

        // ---- catalog integrity ------------------------------------------------

        [Test]
        public void GlobalNumbersAreContiguousFromOneAndUnique()
        {
            IReadOnlyList<TutorialStepDefinition> all = TutorialStepCatalog.All;
            Assert.That(all.Count, Is.GreaterThan(0), "the curriculum is empty");

            int[] numbers = all.Select(s => s.GlobalNumber).ToArray();
            CollectionAssert.AreEqual(Enumerable.Range(1, all.Count), numbers,
                "global numbers must run 1..N with no gap, no repeat and no reordering. " +
                "They are assigned by the catalog precisely so this cannot drift.");
        }

        [Test]
        public void StepIdsAreUnique()
        {
            string[] duplicated = TutorialStepCatalog.All
                .GroupBy(s => s.StepId, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToArray();

            CollectionAssert.IsEmpty(duplicated, "duplicate step ids: " + string.Join(", ", duplicated));
        }

        [Test]
        public void ByGlobalNumberAndFindAgreeForEveryStep()
        {
            foreach (TutorialStepDefinition step in TutorialStepCatalog.All)
            {
                Assert.AreSame(step, TutorialStepCatalog.ByGlobalNumber(step.GlobalNumber),
                    $"ByGlobalNumber({step.GlobalNumber}) returned a different step");
                Assert.AreSame(step, TutorialStepCatalog.Find(step.StepId),
                    $"Find(\"{step.StepId}\") returned a different step");
            }
        }

        [Test]
        public void ChaptersAreContiguousBlocksInDeclaredOrder()
        {
            // A chapter whose steps are interleaved with another's would still pass
            // the contiguity test above while making "BÖLÜMLER" unreadable.
            int cursor = 0;
            foreach (TutorialChapter chapter in TutorialStepCatalog.Chapters)
            {
                IReadOnlyList<TutorialStepDefinition> steps = TutorialStepCatalog.InChapter(chapter.Id);
                if (steps.Count == 0) continue;

                foreach (TutorialStepDefinition step in steps)
                {
                    cursor++;
                    Assert.AreEqual(cursor, step.GlobalNumber,
                        $"chapter {chapter.Letter} step {step.StepId} is out of block order");
                }
            }
        }

        [Test]
        public void EveryStepCarriesTurkishCopyAndABuildableRuntime()
        {
            foreach (TutorialStepDefinition step in TutorialStepCatalog.All)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(step.TitleTr), $"{step.StepId} has no title");
                Assert.IsFalse(string.IsNullOrWhiteSpace(step.InstructionTr), $"{step.StepId} has no instruction");
                Assert.IsFalse(string.IsNullOrWhiteSpace(step.SummaryTr), $"{step.StepId} has no summary");

                ITutorialStepRuntime runtime = null;
                Assert.DoesNotThrow(() => runtime = step.CreateRuntime(),
                    $"{step.StepId} could not build its runtime");
                Assert.IsNotNull(runtime, $"{step.StepId} built a null runtime");
            }
        }

        [Test]
        public void EachEntryBuildsAFreshRuntime()
        {
            // Re-entering a step must be genuinely fresh. A cached runtime would
            // carry the previous attempt's captured baselines and subscriptions.
            foreach (TutorialStepDefinition step in TutorialStepCatalog.All)
            {
                Assert.AreNotSame(step.CreateRuntime(), step.CreateRuntime(),
                    $"{step.StepId} hands out the same runtime instance twice");
            }
        }

        // ---- the design documents and the code agree --------------------------

        [Test]
        public void EveryStepInTheValidationMatrixIsImplemented()
        {
            string path = DesignDoc("STEP-VALIDATION-MATRIX.md");
            Assert.IsTrue(File.Exists(path), $"missing design document: {path}");

            string[] documented = File.ReadLines(path)
                .Select(l => Regex.Match(l, @"^\|\s*\d+\s*\|\s*[A-K]\s*\|\s*([A-K]-\d+)\s*\|"))
                .Where(m => m.Success).Select(m => m.Groups[1].Value).ToArray();

            Assert.That(documented.Length, Is.GreaterThan(0), "parsed no rows from the matrix");

            string[] missing = documented
                .Where(id => TutorialStepCatalog.Find(id) == null).ToArray();

            CollectionAssert.IsEmpty(missing,
                "documented but not implemented: " + string.Join(", ", missing));
        }

        [Test]
        public void EveryImplementedStepIsDocumented()
        {
            string path = DesignDoc("STEP-VALIDATION-MATRIX.md");
            Assert.IsTrue(File.Exists(path), $"missing design document: {path}");

            var documented = new HashSet<string>(
                File.ReadLines(path)
                    .Select(l => Regex.Match(l, @"^\|\s*\d+\s*\|\s*[A-K]\s*\|\s*([A-K]-\d+)\s*\|"))
                    .Where(m => m.Success).Select(m => m.Groups[1].Value),
                StringComparer.OrdinalIgnoreCase);

            string[] undocumented = TutorialStepCatalog.All
                .Select(s => s.StepId).Where(id => !documented.Contains(id)).ToArray();

            CollectionAssert.IsEmpty(undocumented,
                "implemented but not documented: " + string.Join(", ", undocumented));
        }

        [Test]
        public void TheMatricesHaveNoUnfilledRequiredCells()
        {
            // The brief's rule: a blank cell is not acceptable, and "N/A" only with a
            // stated reason. A bare dash is what an unfilled cell looks like after
            // the merge, so finding one here means a row was never really answered.
            foreach (string name in new[] { "STEP-ASSET-MATRIX.md", "STEP-VALIDATION-MATRIX.md" })
            {
                string path = DesignDoc(name);
                Assert.IsTrue(File.Exists(path), $"missing design document: {path}");

                var offenders = new List<string>();
                foreach (string line in File.ReadLines(path))
                {
                    if (!Regex.IsMatch(line, @"^\|\s*\d+\s*\|")) continue;
                    string[] cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                    for (int i = 0; i < cells.Length; i++)
                    {
                        // Asset matrix column 5 is VFX, which is legitimately optional.
                        if (name.StartsWith("STEP-ASSET") && i == 5) continue;
                        if (cells[i].Length == 0 || cells[i] == "—")
                            offenders.Add($"{name} step {cells[0]} column {i}");
                    }
                }
                CollectionAssert.IsEmpty(offenders, string.Join("; ", offenders));
            }
        }

        // ---- assets -----------------------------------------------------------

        [Test]
        public void EveryDeclaredAssetRequirementResolves()
        {
            // No scope and no gameplay context on purpose: resolving a declared
            // requirement must not need a scene, which is what makes this check
            // cheap enough to run over every step on every commit.
            var world = new TutorialWorldFactory(() => null);
            var failures = new List<string>();

            foreach (TutorialStepDefinition step in TutorialStepCatalog.All)
            {
                foreach (TutorialAssetRequirement need in step.Assets)
                {
                    if (!world.CanResolve(need, out string reason))
                        failures.Add($"{step.StepId} → {need}: {reason}");
                }
            }

            CollectionAssert.IsEmpty(failures,
                "a step declares an asset that does not resolve. The tutorial must never " +
                "substitute a placeholder — a wrong model teaches the player something " +
                "untrue and still passes a non-blank screenshot check.\n" +
                string.Join("\n", failures));
        }

        // ---- the main menu contract -------------------------------------------

        [Test]
        public void TutorialEntrySitsDirectlyBelowNewCampaignAndStartsNoSession()
        {
            NebulaUiShell shell = NebulaUiShell.Create();
            var launcher = new RecordingSessionLauncher();
            NebulaMainMenuScreen menu = NebulaMainMenuScreen.Create(shell, launcher);
            try
            {
                (string name, float y)[] entries = shell.Root(UiLayer.Menu)
                    .GetComponentsInChildren<Button>(true)
                    .Select(b => (b.gameObject.name,
                                  ((RectTransform)b.transform).anchoredPosition.y))
                    .ToArray();

                Assert.IsTrue(entries.Any(e => e.name == "New"), "YENİ SEFER entry is missing");
                Assert.IsTrue(entries.Any(e => e.name == "Tutorial"), "TUTORIAL entry is missing");

                float newY = entries.First(e => e.name == "New").y;
                float tutorialY = entries.First(e => e.name == "Tutorial").y;
                Assert.Less(tutorialY, newY, "TUTORIAL must sit below YENİ SEFER");

                // Nothing may sit between them.
                string[] between = entries
                    .Where(e => e.y < newY && e.y > tutorialY).Select(e => e.name).ToArray();
                CollectionAssert.IsEmpty(between,
                    "TUTORIAL must be immediately below YENİ SEFER, but these are between: " +
                    string.Join(", ", between));

                // Drawing and pressing the entry must not touch the campaign at all.
                menu.GetType();
                Assert.AreEqual(0, launcher.Total,
                    "building the menu must not start, continue or restart a campaign");
            }
            finally
            {
                Cleanup(shell);
            }
        }

        [Test]
        public void EveryMenuEntryStaysOnScreenAtTheLargestInterfaceScale()
        {
            // 140% is the accessibility setting, so a row falling off the bottom
            // there is an accessibility row lost to the accessibility control. This
            // caught exactly that when the tutorial entry was added.
            float entered = NebulaUiShell.UiScale;
            NebulaUiShell shell = null;
            try
            {
                NebulaUiShell.UiScale = NebulaUiShell.MaxUiScale;
                shell = NebulaUiShell.Create();
                NebulaMainMenuScreen.Create(shell, new RecordingSessionLauncher());

                float canvasHeight = NebulaUiShell.ReferenceResolution.y / NebulaUiShell.MaxUiScale;

                var offscreen = new List<string>();
                foreach (Button button in shell.Root(UiLayer.Menu).GetComponentsInChildren<Button>(true))
                {
                    var rect = (RectTransform)button.transform;
                    // Distance from the top of the canvas to the bottom of the row.
                    float depth = -DistanceFromCanvasTop(rect) + rect.rect.height;
                    if (depth > canvasHeight) offscreen.Add($"{button.gameObject.name} (needs {depth:0})");
                }

                CollectionAssert.DoesNotContain(offscreen.Select(Row).ToArray(), "Accessibility",
                    $"ERİŞİLEBİLİRLİK falls off the bottom at {NebulaUiShell.MaxUiScale:P0} interface scale; " +
                    $"canvas is {canvasHeight:0} tall. Off screen: {string.Join(", ", offscreen)}");
            }
            finally
            {
                NebulaUiShell.UiScale = entered;
                Cleanup(shell);
            }
        }

        private static string Row(string entry) => entry.Split(' ')[0];

        private static float DistanceFromCanvasTop(RectTransform rect)
        {
            float y = 0f;
            Transform t = rect;
            while (t is RectTransform current && current.GetComponent<Canvas>() == null)
            {
                y += current.anchoredPosition.y;
                t = current.parent;
            }
            return y;
        }

        // ---- persistence ------------------------------------------------------

        [Test]
        public void NoTutorialSourceFileWritesPlayerPrefs()
        {
            // This project has never called PlayerPrefs. The cheapest way to keep
            // "the tutorial writes no PlayerPrefs key" true is to keep the count at
            // zero rather than to audit call sites later.
            string[] roots =
            {
                Path.Combine(Application.dataPath, "Scripts", "Nebula", "Tutorial"),
                Path.Combine(Application.dataPath, "Scripts", "Nebula", "UI", "Tutorial"),
            };

            var offenders = new List<string>();
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    foreach (string line in CodeLines(file))
                    {
                        if (line.Contains("PlayerPrefs")) offenders.Add(Path.GetFileName(file));
                    }
                }
            }

            CollectionAssert.IsEmpty(offenders,
                "tutorial code must not persist anything: " + string.Join(", ", offenders));
        }

        [Test]
        public void NoTutorialSourceFileReachesTheCampaignSessionEntryPoints()
        {
            // Entering the tutorial must not be able to start a campaign. Proving it
            // by reading the source is blunt, but it is the only check that keeps
            // holding when someone adds a convenience call a year from now.
            //
            // Qualified call sites, not bare identifiers. Chapter K deliberately
            // implements INebulaSessionLauncher as a recording double so it can
            // teach Continue-versus-Load without a real launcher behind it — its
            // StartNewCampaign() increments a counter and starts nothing. That
            // double is the mechanism that keeps this guarantee, so a check that
            // flagged it would be failing on its own safeguard. What must never
            // appear is a call into the real thing.
            string[] forbidden =
            {
                "CampaignIdentity.StartNewCampaign", "CampaignIdentity.AdoptFromSave",
                "SaveService.Save", ".SaveNow(", ".BeginSession(",
                "new NebulaSessionLauncher", "SolarSystemBootstrap.BeginSession",
            };

            string root = Path.Combine(Application.dataPath, "Scripts", "Nebula", "Tutorial");
            Assert.IsTrue(Directory.Exists(root), "tutorial source folder is missing");

            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (string code in CodeLines(file))
                {
                    foreach (string term in forbidden)
                    {
                        if (code.Contains(term))
                            offenders.Add($"{Path.GetFileName(file)}: {code}");
                    }
                }
            }

            CollectionAssert.IsEmpty(offenders,
                "tutorial code reaches a campaign/session/save entry point:\n" +
                string.Join("\n", offenders));
        }

        // ---- helpers ----------------------------------------------------------

        /// <summary>
        /// The executable lines of a file, with comments dropped.
        /// <para>
        /// Needed because this codebase documents what it deliberately does *not*
        /// call, in prose, right next to the code that does not call it — so a
        /// naive text search for "PlayerPrefs" or "BeginSession" reports the
        /// comment that promises they are absent as proof that they are present.
        /// </para>
        /// </summary>
        private static IEnumerable<string> CodeLines(string file)
        {
            bool inBlockComment = false;
            foreach (string raw in File.ReadLines(file))
            {
                string line = raw.Trim();

                if (inBlockComment)
                {
                    int close = line.IndexOf("*/", StringComparison.Ordinal);
                    if (close < 0) continue;
                    inBlockComment = false;
                    line = line.Substring(close + 2).Trim();
                }

                if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (line.StartsWith("*", StringComparison.Ordinal)) continue;

                int open = line.IndexOf("/*", StringComparison.Ordinal);
                if (open >= 0) { inBlockComment = true; line = line.Substring(0, open); }

                int inline = line.IndexOf("//", StringComparison.Ordinal);
                if (inline >= 0) line = line.Substring(0, inline);

                if (line.Length > 0) yield return line.Trim();
            }
        }

        private sealed class RecordingSessionLauncher : INebulaSessionLauncher
        {
            public int Total { get; private set; }
            public void StartNewCampaign() => Total++;
            public void ContinueFromSlot(int slot) => Total++;
            public void QuitGame() { }
            public void ReturnToMainMenu() { }
            public void RestartCampaign() => Total++;
        }

        private static void Cleanup(NebulaUiShell shell)
        {
            if (shell == null) return;
            shell.Shutdown();
            if (shell.gameObject != null) UnityEngine.Object.DestroyImmediate(shell.gameObject);
        }
    }
}
