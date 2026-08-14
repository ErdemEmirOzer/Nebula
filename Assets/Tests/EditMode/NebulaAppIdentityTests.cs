using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Nebula.Platform;
using Nebula.UI.Brand;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The committed build identity.
    /// <para>
    /// Reads PlayerSettings as it stands in <c>ProjectSettings.asset</c> and never
    /// calls the installer first. Applying and then asserting would test the setter;
    /// what has to be true is that the values are <b>in the repository</b>, because
    /// that is what a build reads.
    /// </para>
    /// </summary>
    public class NebulaAppIdentityTests
    {
        [Test]
        public void EveryStandaloneIconSlotIsBoundToADeliveredIcon()
        {
            var target = NamedBuildTarget.Standalone;
            int[] slots = PlayerSettings.GetIconSizes(target, IconKind.Any);
            Assert.IsNotEmpty(slots, "standalone declares no icon slots");

            Texture2D[] icons = PlayerSettings.GetIcons(target, IconKind.Any);
            Assert.AreEqual(slots.Length, icons.Length,
                "every declared slot must have an icon; run Nebula/Brand/Apply App Identity");

            var unbound = new List<int>();
            for (int i = 0; i < icons.Length; i++)
                if (icons[i] == null) unbound.Add(slots[i]);

            Assert.IsEmpty(unbound, "icon slots left empty: " + string.Join(", ", unbound));
        }

        /// <summary>
        /// Each slot carries a Nebula icon at least as large as the slot.
        /// <para>
        /// The size check is the point. Unity happily accepts a 32px texture in the
        /// 1024 slot and upscales it, producing a build whose icon is a blur at every
        /// size the OS shows it large.
        /// </para>
        /// </summary>
        [Test]
        public void NoIconSlotIsFilledByAnUpscaledSource()
        {
            var target = NamedBuildTarget.Standalone;
            int[] slots = PlayerSettings.GetIconSizes(target, IconKind.Any);
            Texture2D[] icons = PlayerSettings.GetIcons(target, IconKind.Any);

            var wrong = new List<string>();
            for (int i = 0; i < icons.Length && i < slots.Length; i++)
            {
                if (icons[i] == null) continue;

                string path = AssetDatabase.GetAssetPath(icons[i]);
                if (!path.StartsWith(NebulaBrandAssetCatalog.IdentityAssetFolder))
                    wrong.Add($"slot {slots[i]} is bound to {path}, which is not a delivered brand icon");
                else if (icons[i].width < slots[i])
                    wrong.Add($"slot {slots[i]} is bound to a {icons[i].width}px source");
            }

            Assert.IsEmpty(wrong, string.Join("; ", wrong));
        }

        /// <summary>The mapping the installer uses picks the smallest covering size, never a smaller one.</summary>
        [Test]
        public void TheIconSizeMappingNeverPicksASourceSmallerThanTheSlot()
        {
            foreach (int slot in new[] { 16, 32, 48, 64, 128, 256, 512, 1024 })
            {
                int chosen = NebulaBrandAssetCatalog.IconSizeFor(slot);
                Assert.GreaterOrEqual(chosen, slot, $"slot {slot} would be upscaled from {chosen}");
                Assert.Contains(chosen, NebulaBrandAssetCatalog.IconSizes,
                    $"slot {slot} resolved to {chosen}, which is not a delivered size");
            }
        }

        [Test]
        public void TheSplashSequenceCarriesTheDeliveredPlate()
        {
            PlayerSettings.SplashScreenLogo[] logos = PlayerSettings.SplashScreen.logos;
            Assert.IsNotEmpty(logos, "no splash logo is bound; run Nebula/Brand/Apply App Identity");

            bool found = false;
            foreach (PlayerSettings.SplashScreenLogo logo in logos)
            {
                if (logo.logo == null) continue;
                string path = AssetDatabase.GetAssetPath(logo.logo);
                if (path != NebulaBrandAssetCatalog.SplashAssetPath(NebulaBrandAssetCatalog.SplashAspect.SixteenByNine)) continue;

                found = true;
                Assert.GreaterOrEqual(logo.duration, NebulaBrandAssetCatalog.SplashLogoSeconds,
                    "the plate must hold at least its authored duration");
            }

            Assert.IsTrue(found, "the delivered splash plate is not in the splash sequence");
        }

        /// <summary>
        /// The engine splash is left alone.
        /// <para>
        /// The brief is explicit that the Unity splash requirement must not be worked
        /// around, and it would be trivially easy to do so from the same API this
        /// integration already uses. This is the guard against that happening later
        /// by accident — or by someone deciding it looks better.
        /// </para>
        /// </summary>
        [Test]
        public void TheEngineSplashRequirementIsNotBypassed()
        {
            Assert.IsTrue(PlayerSettings.SplashScreen.show,
                "the splash screen must remain enabled");
            Assert.IsTrue(PlayerSettings.SplashScreen.showUnityLogo,
                "the Unity logo must not be switched off by this integration");
        }

        /// <summary>
        /// The committed naming is the final one, and it is the naming the code believes in.
        /// <para>
        /// Asserted against <see cref="NebulaAppIdentity"/> rather than against literals
        /// spelled here, because those constants are what the rest of the codebase acts
        /// on — the legacy-data migration decides where a player's old folder was by
        /// comparing them. Two independent copies of these strings would let the asset and
        /// the code disagree while both tests stayed green, which is the exact failure
        /// this is here to catch.
        /// </para>
        /// <para>
        /// It also still does the original job: PlayerSettings is one object holding
        /// several hundred unrelated fields, and the cheapest way for the icon and splash
        /// installer to do damage is to move one of them.
        /// </para>
        /// </summary>
        [Test]
        public void TheCommittedProductIdentityIsTheOneTheCodeUses()
        {
            Assert.AreEqual(NebulaAppIdentity.Product, PlayerSettings.productName, "product name");
            Assert.AreEqual(NebulaAppIdentity.Company, PlayerSettings.companyName, "company name");
        }

        /// <summary>
        /// The identity is actually final, not a leftover default.
        /// <para>
        /// Stated as literals on purpose, and only here: this one test is where the
        /// decision is written down, so changing <see cref="NebulaAppIdentity"/> to
        /// something else cannot quietly agree with itself. <c>DefaultCompany</c> is
        /// called out by name because it is the value Unity ships and the one a fresh
        /// clone would drift back to.
        /// </para>
        /// </summary>
        [Test]
        public void TheShippedIdentityIsNotUnitysDefault()
        {
            Assert.AreEqual("Erdem", PlayerSettings.companyName, "company name");
            Assert.AreEqual("NEBULA", PlayerSettings.productName, "product name");
            Assert.AreNotEqual("DefaultCompany", PlayerSettings.companyName,
                "the project is still on Unity's placeholder company");
        }

        /// <summary>
        /// The standalone bundle identifier is the explicit one, not the name Unity
        /// synthesises from company and product.
        /// <para>
        /// Worth its own test because the synthesised answer — <c>com.Erdem.NEBULA</c> —
        /// is plausible enough to pass a careless eye and is a different string wherever
        /// identifiers are compared case-sensitively. If
        /// <c>overrideDefaultApplicationIdentifier</c> is ever switched back off in
        /// ProjectSettings.asset, this is what notices.
        /// </para>
        /// </summary>
        [Test]
        public void TheStandaloneBundleIdentifierIsTheExplicitOne()
        {
            string identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            Assert.AreEqual(NebulaAppIdentity.BundleIdentifier, identifier, "standalone application identifier");
            Assert.AreEqual("com.erdem.nebula", identifier, "the agreed private test-distribution identifier");
        }

        /// <summary>
        /// The identity is stated in exactly one source file.
        /// <para>
        /// A source scan and not a behaviour check, because the failure it guards against
        /// is not a wrong value — it is a <i>second</i> copy of the right value, which
        /// passes every other test in this file on the day it is added and silently stops
        /// matching six months later. The strings picked are the ones with no legitimate
        /// second use: the company name, the bundle identifier, and the legacy company the
        /// data migration keys off. The product name is deliberately not among them,
        /// because "NEBULA" is also the wordmark the main menu and the credits draw, and a
        /// rule that forbade that would be a rule about the wrong thing.
        /// </para>
        /// </summary>
        [Test]
        public void TheIdentityStringsAppearInExactlyOneSourceFile()
        {
            string[] mustBeUnique = { "\"Erdem\"", "com.erdem.nebula", "\"DefaultCompany\"" };
            string authority = Path.Combine(Application.dataPath, "Scripts", "Nebula", "Platform", "NebulaAppIdentity.cs");
            Assert.IsTrue(File.Exists(authority), "the identity authority file is missing: " + authority);

            var offenders = new List<string>();
            foreach (string file in ProductionSourceFiles())
            {
                if (string.Equals(file, authority, StringComparison.Ordinal)) continue;

                string content = File.ReadAllText(file);
                foreach (string literal in mustBeUnique)
                    if (content.Contains(literal))
                        offenders.Add($"{file} also states {literal}");
            }

            Assert.IsEmpty(offenders,
                "the app identity must be stated only in NebulaAppIdentity.cs:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// The produced player's filename is derived, not restated.
        /// <para>
        /// Source-level because the method that does the deriving lives in the Editor
        /// assembly, which an assembly-definition test fixture cannot reference. What is
        /// checked is the thing that would actually be wrong: a hardcoded stem, which is
        /// how the file read before the rename and which would have shipped a
        /// <c>Nebula.app</c> whose app is called NEBULA.
        /// </para>
        /// </summary>
        [Test]
        public void TheBuiltPlayerIsNamedFromTheProductName()
        {
            string automation = Path.Combine(Application.dataPath, "Editor", "Nebula", "NebulaBuildAutomation.cs");
            Assert.IsTrue(File.Exists(automation), "the build automation is missing: " + automation);

            string content = File.ReadAllText(automation);
            StringAssert.DoesNotContain("\"Nebula\" + extension", content,
                "the output filename must not be a hardcoded product name");
            StringAssert.Contains("PlayerSettings.productName", content,
                "the output filename must be derived from PlayerSettings.productName");
        }

        /// <summary>Runtime and editor sources, which is where a duplicated identity would do damage.</summary>
        private static IEnumerable<string> ProductionSourceFiles()
        {
            foreach (string root in new[]
                     {
                         Path.Combine(Application.dataPath, "Scripts", "Nebula"),
                         Path.Combine(Application.dataPath, "Editor"),
                     })
            {
                if (!Directory.Exists(root)) continue;
                foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                    yield return file;
            }
        }
    }
}
