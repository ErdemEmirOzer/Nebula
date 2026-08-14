using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// What a release player is allowed to put on screen.
    /// <para>
    /// Two diagnostic overlays exist: a sector/streaming readout and a test-flight
    /// speed panel. One was gated behind <see cref="Debug.isDebugBuild"/> and the
    /// other was not, so a shipped build drew a green wall of manifest cache counts
    /// and live GameObject totals over the corner of the game. Nobody noticed
    /// because in the Editor <c>isDebugBuild</c> is always true — the gate that was
    /// there and the gate that was missing look identical from inside Unity.
    /// </para>
    /// <para>
    /// Read from the composition source rather than from a booted session for
    /// exactly that reason: a PlayMode assertion could only ever confirm the
    /// development behaviour. What matters is that the release branch does not
    /// construct these at all — hiding them is not the same thing, because a
    /// hidden canvas is still a canvas, still allocated, still in the hierarchy.
    /// </para>
    /// </summary>
    public class UiFinalReleaseVisibilityTests
    {
        private const string InstallerPath =
            "Assets/Scripts/Nebula/World/Composition/PresentationInstaller.cs";

        private static string Installer()
        {
            string full = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, InstallerPath);
            Assert.IsTrue(File.Exists(full), InstallerPath + " has moved; this guard needs its new path");
            return File.ReadAllText(full);
        }

        [TestCase("SectorDebugHud")]
        [TestCase("FlightTestSpeedHud")]
        public void EveryDiagnosticOverlay_IsBuiltOnlyInADevelopmentBuild(string hud)
        {
            string source = Installer();
            var creation = new Regex(@"UI\." + hud + @"\.Create\(");
            Assert.IsTrue(creation.IsMatch(source),
                hud + " is no longer created here; this guard needs to follow it");

            // The construction must sit inside an isDebugBuild branch. Checked by
            // brace depth rather than by proximity, so moving the call around
            // inside the branch does not quietly disarm the guard.
            int call = creation.Match(source).Index;
            int gate = source.LastIndexOf("if (Debug.isDebugBuild)", call, System.StringComparison.Ordinal);
            Assert.Greater(gate, -1, hud + " is constructed with no isDebugBuild gate above it at all");

            int depth = 0;
            for (int i = gate; i < call; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}') depth--;
            }
            Assert.Greater(depth, 0,
                hud + " is created outside the isDebugBuild branch -- a release player would build it");
        }

        /// <summary>
        /// The function keys were a developer's toggles for those same overlays.
        /// None of them is a player binding, and none may be left anywhere a
        /// player build compiles.
        /// </summary>
        [Test]
        public void NoFunctionKeyBindingSurvivesAnywhereInTheRuntime()
        {
            string root = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Assets/Scripts/Nebula");
            var pattern = new Regex(@"KeyCode\.F([1-9]|1[0-2])\b");

            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                Match hit = pattern.Match(File.ReadAllText(file));
                Assert.IsFalse(hit.Success,
                    $"{file.Substring(root.Length)} still binds {hit.Value}");
            }
        }
    }
}
