using System.Globalization;

namespace Nebula.UI.Design
{
    /// <summary>
    /// Casing for player-facing text.
    /// <para>
    /// The interface uppercases a lot of Turkish -- headings, labels, badges, costs.
    /// <c>ToUpperInvariant()</c> is wrong for all of it: the invariant culture does
    /// not map the dotless <c>ı</c> (U+0131), so <c>"Metal Alaşım"</c> came back as
    /// <c>"METAL ALAŞıM"</c> with one lowercase letter stranded mid-word. It also
    /// maps <c>i</c> to <c>I</c> rather than <c>İ</c>, which is simply not how
    /// Turkish capitalises.
    /// </para>
    /// <para>
    /// So every uppercase in the UI goes through here, against the Turkish culture:
    /// <c>i → İ</c>, <c>ı → I</c>. The culture is resolved once and cached, because
    /// <see cref="CultureInfo.GetCultureInfo(string)"/> on a per-frame refresh is
    /// not free.
    /// </para>
    /// </summary>
    public static class NebulaText
    {
        private static readonly CultureInfo Turkish = ResolveTurkish();

        private static CultureInfo ResolveTurkish()
        {
            try
            {
                return CultureInfo.GetCultureInfo("tr-TR");
            }
            catch (CultureNotFoundException)
            {
                // A stripped ICU build has no named cultures. Falling back to
                // invariant would silently reintroduce the stranded-ı bug, so the
                // manual mapping below covers the two letters that actually differ.
                return null;
            }
        }

        /// <summary>Uppercases player-facing text the way Turkish actually works.</summary>
        public static string Upper(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (Turkish != null) return value.ToUpper(Turkish);

            var buffer = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == 'i') buffer.Append('İ');
                else if (c == 'ı') buffer.Append('I');
                else buffer.Append(char.ToUpperInvariant(c));
            }
            return buffer.ToString();
        }

        /// <summary>
        /// Uppercases text that is deliberately <b>not</b> Turkish — a key name, a
        /// stable identifier, an enum name.
        /// <para>
        /// Turkish casing would be actively wrong here: <c>tr-TR</c> maps
        /// <c>i</c> to <c>İ</c>, so a KeyCode called <c>Insert</c> would be shown
        /// to the player as <c>İNSERT</c>. Having a named method for this keeps the
        /// blanket ban on <c>ToUpperInvariant</c> in player-facing code intact,
        /// while making each deliberate exception obvious at the call site.
        /// </para>
        /// </summary>
        public static string UpperAscii(string value) =>
            string.IsNullOrEmpty(value) ? value : value.ToUpperInvariant();

        /// <summary>Lowercases player-facing text the way Turkish actually works.</summary>
        public static string Lower(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (Turkish != null) return value.ToLower(Turkish);

            var buffer = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == 'I') buffer.Append('ı');
                else if (c == 'İ') buffer.Append('i');
                else buffer.Append(char.ToLowerInvariant(c));
            }
            return buffer.ToString();
        }
    }
}
