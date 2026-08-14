using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Source-scan EditMode tests proving the F1-F12 removal is real, not
    /// just spot-checked: no runtime .cs file under Assets/Scripts/Nebula may
    /// reference KeyCode.F1-F12, and no old bracketed function-key hint must
    /// still be shown to the player (§6/§8).
    /// </summary>
    public class NoFunctionKeySourceReferencesTests
    {
        private static readonly Regex FunctionKeyCodeRegex = new Regex(@"KeyCode\.F(1[0-2]|[1-9])\b");
        private static readonly string[] BannedHints = { "[F1]", "[F2]", "[F3]", "[F4]", "[F5]", "[F6]", "[F7]", "[F8]", "[F9]", "[F10]", "[F11]", "[F12]" };

        private static string RuntimeScriptsRoot => Path.Combine(Application.dataPath, "Scripts", "Nebula");

        private static IEnumerable<string> AllRuntimeCsFiles()
        {
            return Directory.GetFiles(RuntimeScriptsRoot, "*.cs", SearchOption.AllDirectories);
        }

        [Test]
        public void NoRuntimeScript_ReferencesAnyFunctionKeyCode()
        {
            var offenders = new List<string>();
            foreach (string file in AllRuntimeCsFiles())
            {
                if (FunctionKeyCodeRegex.IsMatch(File.ReadAllText(file)))
                    offenders.Add(file);
            }
            Assert.IsEmpty(offenders, "no runtime script may reference KeyCode.F1-F12: " + string.Join(", ", offenders));
        }

        [Test]
        public void NoRuntimeScript_ShowsAnOldBracketedFunctionKeyHint()
        {
            var offenders = new List<string>();
            foreach (string file in AllRuntimeCsFiles())
            {
                string content = File.ReadAllText(file);
                foreach (string hint in BannedHints)
                {
                    if (content.Contains(hint)) offenders.Add($"{file} contains \"{hint}\"");
                }
            }
            Assert.IsEmpty(offenders, string.Join(", ", offenders));
        }
    }
}
