using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Rules the redesign is not allowed to break, enforced by reading the source
    /// rather than by remembering.
    /// <para>
    /// Every one of these describes something the old interface actually did:
    /// printed <c>[HEX]</c> and <c>[CRIT]</c> as literal text, showed the player
    /// raw <c>WorldId</c> strings, pinned a 137-character control-hint sentence to
    /// the screen, and put absolute universe coordinates on the flight HUD. Fixing
    /// them once is not the same as keeping them fixed.
    /// </para>
    /// </summary>
    public class NebulaUiHygieneTests
    {
        /// <summary>Only the redesigned interface is held to these rules.</summary>
        private static IEnumerable<string> RedesignedSources()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "Nebula", "UI");
            foreach (string directory in new[] { "Design", "Shell", "Menu", "Hud", "Session" })
            {
                string path = Path.Combine(root, directory);
                if (!Directory.Exists(path)) continue;
                foreach (string file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
                    yield return file;
            }
        }

        /// <summary>
        /// Strips comments and XML docs. The rules are about what reaches the
        /// player, and every one of these patterns is quoted in a comment
        /// explaining the defect it replaced.
        /// </summary>
        private static string Code(string file)
        {
            string source = File.ReadAllText(file);
            source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            source = Regex.Replace(source, @"^[ \t]*///.*$", string.Empty, RegexOptions.Multiline);
            source = Regex.Replace(source, @"^[ \t]*//.*$", string.Empty, RegexOptions.Multiline);
            return source;
        }

        private static IEnumerable<string> StringLiterals(string code)
        {
            foreach (Match match in Regex.Matches(code, "\"([^\"\\\\]|\\\\.)*\""))
                yield return match.Value;
        }

        // ---- markup that was never markup ------------------------------------

        [Test]
        public void NoLiteralMarkupTagsReachThePlayer()
        {
            // CombatHud printed "[HEX]K 85%  [PLATE]Z 60%" — bracket tags the
            // renderer never interpreted, so the player read them verbatim.
            //
            // Matched by shape rather than by a list of the ones that existed: a
            // list only ever catches the tags someone already removed, and the
            // defect is the habit, not those ten words. Any all-caps bracket token
            // inside a string literal is the same mistake wearing a new name.
            var tag = new System.Text.RegularExpressions.Regex(@"""[^""]*\[[A-Z][A-Z_ -]*\][^""]*""");
            var failures = new List<string>();

            foreach (string file in RedesignedSources())
            {
                foreach (System.Text.RegularExpressions.Match match in tag.Matches(Code(file)))
                    failures.Add($"{Path.GetFileName(file)} contains {match.Value.Trim()}");
            }

            Assert.IsEmpty(failures,
                "Literal markup tags are back in the interface:\n  " + string.Join("\n  ", failures));
        }

        // ---- stable ids are not player-facing --------------------------------

        [Test]
        public void NoStableIdIsInterpolatedIntoPlayerFacingText()
        {
            // ExtractionHud printed "DRILL {drill.WorldId}" and CombatHud printed
            // "TARGET: {pressure.TargetPlanetWorldId}". Those are save-file keys.
            // Scoped to interpolation holes inside string literals. An earlier
            // version matched across whole code blocks, because [^}]* happily spans
            // newlines — it flagged a loop that merely reads an id into a dictionary
            // key, which is not the same as showing it to anyone.
            var hole = new Regex(@"\{[^{}""\n]*\b(WorldId|SystemId|VehicleId|NodeId|ItemId|CampaignId)\b[^{}""\n]*\}",
                RegexOptions.IgnoreCase);
            var failures = new List<string>();

            foreach (string file in RedesignedSources())
                foreach (string literal in StringLiterals(Code(file)))
                    foreach (Match match in hole.Matches(literal))
                    {
                        // An id handed to a lookup is not an id shown to anybody: what
                        // the hole prints is the call's result. `{drill.WorldId}` puts a
                        // save-file key on screen and is the defect this test exists for;
                        // `{NebulaBenchMessages.ItemName(itemId)}` puts a Turkish item
                        // name there. Matching the id name alone could not tell them
                        // apart and reported six honest call sites as leaks.
                        if (Regex.IsMatch(match.Value, @"\w+\s*\([^()]*\)")) continue;
                        failures.Add($"{Path.GetFileName(file)}: {literal}");
                    }

            // Interpolation was only half of it. The defect that actually shipped
            // was a plain assignment -- `row.Name.Value = worldId` on the planets
            // page -- which no literal-scoped check could ever see.
            var assigned = new Regex(
                @"\.(Value|text|Caption)\s*=\s*[A-Za-z_][A-Za-z0-9_.]*\b(WorldId|SystemId|VehicleId|NodeId|ItemId|CampaignId|worldId|systemId|vehicleId)\b\s*;",
                RegexOptions.IgnoreCase);

            foreach (string file in RedesignedSources())
                foreach (Match match in assigned.Matches(Code(file)))
                    failures.Add($"{Path.GetFileName(file)}: {match.Value.Trim()}");

            Assert.IsEmpty(failures,
                "A stable id is being interpolated into text the player will read:\n  " +
                string.Join("\n  ", failures));
        }

        [Test]
        public void NoAbsoluteCoordinateReadoutOnThePlayerHud()
        {
            // ShipHud printed "X {absolute.x:0}  Y ...  Z ..." — a debug value the
            // player never acts on. It belongs to the dev-only HUD.
            var failures = new List<string>();
            foreach (string file in RedesignedSources())
                foreach (string literal in StringLiterals(Code(file)))
                    if (Regex.IsMatch(literal, @"X\s*\{.*Y\s*\{.*Z\s*\{"))
                        failures.Add($"{Path.GetFileName(file)}: {literal}");

            Assert.IsEmpty(failures,
                "An absolute coordinate readout is back on the player HUD:\n  " +
                string.Join("\n  ", failures));
        }

        // ---- no wall of text -------------------------------------------------

        [Test]
        public void NoPinnedControlHintSentence()
        {
            // ShipHud carried a 137-character hint string listing every binding.
            // Key prompts belong on the actions that are actually available.
            const int wallThreshold = 90;
            var failures = new List<string>();

            foreach (string file in RedesignedSources())
            {
                foreach (string literal in StringLiterals(Code(file)))
                {
                    string body = literal.Trim('"');
                    if (body.Length <= wallThreshold) continue;
                    // A long string is only suspicious when it reads like a list of
                    // bindings rather than a sentence explaining one thing.
                    if (body.Split('·').Length >= 4)
                        failures.Add($"{Path.GetFileName(file)}: {body.Substring(0, 60)}…");
                }
            }

            Assert.IsEmpty(failures,
                "A pinned multi-binding hint string is back:\n  " + string.Join("\n  ", failures));
        }

        // ---- bindings --------------------------------------------------------

        [Test]
        public void TheRedesignedInterfaceBindsNoFunctionKey()
        {
            var banned = new Regex(@"KeyCode\.F([1-9]|1[0-2])\b");
            var failures = new List<string>();

            foreach (string file in RedesignedSources())
                foreach (Match match in banned.Matches(Code(file)))
                    failures.Add($"{Path.GetFileName(file)}: {match.Value}");

            Assert.IsEmpty(failures,
                "A function key is bound in the new interface; those are forbidden project-wide:\n  " +
                string.Join("\n  ", failures));
        }

        // ---- one authority ---------------------------------------------------

        [Test]
        public void TheInterfaceDoesNotReimplementStatusColour()
        {
            // Status colour has exactly one owner. A hand-rolled Color for a status
            // is how twelve slightly different greys ended up on screen last time.
            var failures = new List<string>();
            foreach (string file in RedesignedSources())
            {
                if (Path.GetFileName(file) == "NebulaUiDesignTokens.cs") continue;
                if (Regex.IsMatch(Code(file), @"LightingUiPalette\.(Nominal|Caution|Warning|Critical|Working|Inactive)\b"))
                    failures.Add(Path.GetFileName(file));
            }

            Assert.IsEmpty(failures,
                "A screen reached past the token layer straight into the palette:\n  " +
                string.Join("\n  ", failures));
        }

        [Test]
        public void EveryTextObjectComesFromTheFactory()
        {
            // One creation point is what makes the TextMeshPro migration a
            // one-method change instead of a sweep across every screen.
            var failures = new List<string>();
            foreach (string file in RedesignedSources())
            {
                if (Path.GetFileName(file) == "NebulaUiFactory.cs") continue;
                if (Regex.IsMatch(Code(file), @"AddComponent<\s*(UnityEngine\.UI\.)?Text\s*>"))
                    failures.Add(Path.GetFileName(file));
            }

            Assert.IsEmpty(failures,
                "A screen created a Text directly instead of going through NebulaUiFactory.Label:\n  " +
                string.Join("\n  ", failures));
        }

        [Test]
        public void PlayerFacingTextIsNotUppercasedWithInvariantCulture()
        {
            // ToUpperInvariant leaves the dotless ı alone, which rendered
            // "METAL ALAŞıM" with a lowercase letter stranded mid-word.
            var failures = new List<string>();
            foreach (string file in RedesignedSources())
            {
                if (Path.GetFileName(file) == "NebulaText.cs") continue;
                if (Code(file).Contains("ToUpperInvariant()"))
                    failures.Add(Path.GetFileName(file));
            }

            Assert.IsEmpty(failures,
                "Turkish text is being uppercased with the invariant culture; use NebulaText.Upper:\n  " +
                string.Join("\n  ", failures));
        }

        // ---- dev-only stays dev-only -----------------------------------------

        [Test]
        public void DevOnlyHudsAreStillGatedOutOfReleaseBuilds()
        {
            string installer = File.ReadAllText(Path.Combine(Application.dataPath,
                "Scripts", "Nebula", "World", "Composition", "PresentationInstaller.cs"));

            Assert.IsTrue(installer.Contains("Debug.isDebugBuild"),
                "The development-only HUD gate disappeared from composition; a dev panel " +
                "would then be created in a release player.");

            int gate = installer.IndexOf("Debug.isDebugBuild", System.StringComparison.Ordinal);
            int flightTestHud = installer.IndexOf("FlightTestSpeedHud.Create", System.StringComparison.Ordinal);
            Assert.Greater(flightTestHud, gate,
                "FlightTestSpeedHud is created outside the isDebugBuild gate.");
        }
    }
}
