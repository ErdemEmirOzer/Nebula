using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Nebula.Modules;
using Nebula.UI.Design;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Every module the catalog can show the player has a Turkish name.
    /// <para>
    /// The progression screen printed <see cref="ModuleDef.DisplayName"/> straight
    /// from the catalog, so twelve English rows sat inside a screen whose every
    /// other word — including their own rejection reasons a few pixels to the
    /// right — is Turkish. The fallback keeps a new module visible rather than
    /// blank, which means a missing translation is quiet at runtime. This is what
    /// makes it loud.
    /// </para>
    /// </summary>
    public class NebulaModuleNameTests
    {
        [Test]
        public void EveryCatalogModuleHasATurkishName()
        {
            var untranslated = new List<string>();
            foreach (ModuleDef module in ModuleCatalog.Modules)
                if (NebulaModuleName.Of(module) == module.DisplayName) untranslated.Add(module.Id);

            Assert.IsEmpty(untranslated,
                "these modules would render in English on a Turkish screen: " + string.Join(", ", untranslated));
        }

        /// <summary>
        /// The reverse direction: a renamed or removed module leaves an entry here
        /// pointing at nothing, and nothing else would ever notice.
        /// </summary>
        [Test]
        public void NoTranslationPointsAtAModuleThatNoLongerExists()
        {
            var ids = new HashSet<string>();
            foreach (ModuleDef module in ModuleCatalog.Modules) ids.Add(module.Id);

            var orphans = new List<string>();
            foreach (string id in NebulaModuleName.KnownIds)
                if (!ids.Contains(id)) orphans.Add(id);

            Assert.IsEmpty(orphans, "translations for modules the catalog no longer has: " + string.Join(", ", orphans));
        }

        /// <summary>
        /// The catalog name is the stable internal one and must stay exactly as it
        /// is: <c>ContentFingerprint</c> hashes it into the content identity a save
        /// is validated against, so translating in place would move a pinned number.
        /// </summary>
        [Test]
        public void TheCatalogItselfWasNotTranslated()
        {
            var ascii = new Regex(@"^[\x20-\x7E]+$");
            foreach (ModuleDef module in ModuleCatalog.Modules)
                Assert.IsTrue(ascii.IsMatch(module.DisplayName),
                    $"{module.Id}: the catalog display name feeds ContentFingerprint and must stay as authored");
        }

        [Test]
        public void AnUnknownIdFallsBackRatherThanRenderingEmpty()
        {
            Assert.AreEqual("Kalkan Sistemleri", NebulaModuleName.Of("defense.shield_systems"));
            Assert.AreEqual("industry.made_up", NebulaModuleName.Of("industry.made_up"));
            Assert.AreEqual("Fallback", NebulaModuleName.Of("industry.made_up", "Fallback"));
            Assert.AreEqual(string.Empty, NebulaModuleName.Of((ModuleDef)null));
        }
    }
}
