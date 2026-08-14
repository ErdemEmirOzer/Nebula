using System.Globalization;
using System.Text;

namespace Nebula.EditorTools.TestGate
{
    /// <summary>
    /// A minimal JSON emitter for the gate's own artefacts.
    ///
    /// UnityEngine.JsonUtility is used for READING the worker plan (a flat class with
    /// string arrays, which is exactly what it handles) and deliberately not for
    /// writing. Writing is where it hurts: it cannot emit a list of heterogeneous
    /// records without a wrapper type per shape, it serialises `null` strings as ""
    /// so "no source file found" and "source file is the empty string" become the
    /// same value, and it has no way to express the nested per-leaf category array
    /// without another serialisable type. The manifest is read by Python and any of
    /// those would be a silent data loss the gate could not detect.
    /// </summary>
    internal static class NebulaGateJson
    {
        internal static void Str(StringBuilder into, string value)
        {
            if (value == null)
            {
                into.Append("null");
                return;
            }

            into.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"': into.Append("\\\""); break;
                    case '\\': into.Append("\\\\"); break;
                    case '\b': into.Append("\\b"); break;
                    case '\f': into.Append("\\f"); break;
                    case '\n': into.Append("\\n"); break;
                    case '\r': into.Append("\\r"); break;
                    case '\t': into.Append("\\t"); break;
                    default:
                        // Test names carry assertion text and, through [TestCase]
                        // arguments, arbitrary literals. Anything below U+0020 has to
                        // be escaped or the line is not JSON; everything else is
                        // written through as UTF-8.
                        if (c < ' ')
                        {
                            into.Append("\\u");
                            into.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            into.Append(c);
                        }
                        break;
                }
            }

            into.Append('"');
        }

        internal static void Field(StringBuilder into, string name, string value, bool comma = true)
        {
            Str(into, name);
            into.Append(':');
            Str(into, value);
            if (comma)
            {
                into.Append(',');
            }
        }

        internal static void Field(StringBuilder into, string name, long value, bool comma = true)
        {
            Str(into, name);
            into.Append(':');
            into.Append(value.ToString(CultureInfo.InvariantCulture));
            if (comma)
            {
                into.Append(',');
            }
        }

        internal static void Field(StringBuilder into, string name, double value, bool comma = true)
        {
            Str(into, name);
            into.Append(':');
            // "R" and the invariant culture together. A Turkish-locale editor writes
            // 0,25 for a quarter of a second with the default format, and the Python
            // side reads that as invalid JSON -- which, because the journal is the
            // crash evidence, would throw away the proof of every leaf that ran.
            into.Append(value.ToString("R", CultureInfo.InvariantCulture));
            if (comma)
            {
                into.Append(',');
            }
        }

        internal static void Array(StringBuilder into, string name, string[] values, bool comma = true)
        {
            Str(into, name);
            into.Append(":[");
            if (values != null)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    if (i > 0)
                    {
                        into.Append(',');
                    }

                    Str(into, values[i]);
                }
            }

            into.Append(']');
            if (comma)
            {
                into.Append(',');
            }
        }
    }
}
