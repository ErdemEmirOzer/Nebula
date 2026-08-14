using NUnit.Framework;
using Nebula.UI.Design;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Turkish casing. Found the hard way: a HUD cost line rendered as
    /// "METAL ALAŞıM", with one lowercase letter stranded mid-word, because
    /// ToUpperInvariant does not map the dotless ı.
    /// </summary>
    public class NebulaTextCasingTests
    {
        [Test]
        public void DotlessI_UppercasesToCapitalI()
        {
            Assert.AreEqual("ALAŞIM", NebulaText.Upper("Alaşım"),
                "The dotless ı must become I. ToUpperInvariant leaves it lowercase.");
        }

        [Test]
        public void DottedI_UppercasesToDottedCapital()
        {
            Assert.AreEqual("İSTASYON", NebulaText.Upper("istasyon"),
                "Turkish capitalises i as İ, not I.");
        }

        [Test]
        public void CapitalI_LowercasesToDotless()
        {
            Assert.AreEqual("ışık", NebulaText.Lower("IŞIK"));
        }

        [Test]
        public void RoundTripsEveryTurkishSpecificLetter()
        {
            const string sample = "çğıöşü ÇĞİÖŞÜ";
            string upper = NebulaText.Upper(sample);
            Assert.IsFalse(upper.Contains("ı"), $"A dotless ı survived uppercasing: '{upper}'");
            Assert.IsFalse(upper.Contains("ş"), $"An ş survived uppercasing: '{upper}'");
            Assert.IsFalse(upper.Contains("ğ"), $"A ğ survived uppercasing: '{upper}'");
        }

        [Test]
        public void HandlesNullAndEmptyWithoutThrowing()
        {
            Assert.IsNull(NebulaText.Upper(null));
            Assert.AreEqual(string.Empty, NebulaText.Upper(string.Empty));
            Assert.IsNull(NebulaText.Lower(null));
        }

        [Test]
        public void LeavesAsciiAlone()
        {
            Assert.AreEqual("METAL T2", NebulaText.Upper("Metal T2"));
        }
    }
}
