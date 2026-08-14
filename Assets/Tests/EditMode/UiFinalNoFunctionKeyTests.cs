using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// No function key is a player binding, and none may survive in the runtime.
    /// <para>
    /// F1–F12 were a developer's toggles for the diagnostic overlays. They are not
    /// in the settings screen, not in the key hint rail, and not in anything the
    /// player is ever told about — so a build that still listens for one has a
    /// hidden control surface. The check is a scan of every runtime source file
    /// rather than a list of the classes that used to have them, because the
    /// failure mode is somebody adding a thirteenth.
    /// </para>
    /// </summary>
    public class UiFinalNoFunctionKeyTests
    {
        private static string RuntimeRoot =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Assets/Scripts/Nebula");

        [Test]
        public void NoRuntimeSourceBindsAFunctionKey()
        {
            var pattern = new Regex(@"KeyCode\.F([1-9]|1[0-2])\b");
            var offenders = new List<string>();

            foreach (string file in Directory.GetFiles(RuntimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                Match hit = pattern.Match(File.ReadAllText(file));
                if (hit.Success) offenders.Add($"{file.Substring(RuntimeRoot.Length)}: {hit.Value}");
            }

            Assert.IsEmpty(offenders,
                "function keys are developer toggles, not player bindings: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// The scan has to be able to fail, or it is decoration. A pattern that
        /// matched nothing anywhere would pass this suite forever.
        /// </summary>
        [Test]
        public void TheScanRecognisesAFunctionKeyWhenItSeesOne()
        {
            var pattern = new Regex(@"KeyCode\.F([1-9]|1[0-2])\b");
            Assert.IsTrue(pattern.IsMatch("if (Input.GetKeyDown(KeyCode.F3)) Toggle();"));
            Assert.IsTrue(pattern.IsMatch("KeyCode.F12"));
            Assert.IsFalse(pattern.IsMatch("KeyCode.F"), "the plain F key is a real player binding");
            Assert.IsFalse(pattern.IsMatch("KeyCode.F13"), "F13+ is not what this rule is about");
        }
    }
}
