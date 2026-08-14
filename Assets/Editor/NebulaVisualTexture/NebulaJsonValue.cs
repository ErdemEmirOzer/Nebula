using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nebula.EditorTools.VisualTexture
{
    /// <summary>
    /// A tiny read-only JSON tree, and a deterministic writer for the reports.
    /// <para>
    /// Unity's <c>JsonUtility</c> cannot do either job here. Reading: glTF is
    /// full of optional members and heterogeneous extension objects, and
    /// <c>JsonUtility</c> needs a concrete field for every one of them — an
    /// unknown extension would be dropped silently, which is exactly the case
    /// that must not be silently dropped when classifying a texture's role.
    /// Writing: the reports are committed artefacts that get diffed between runs,
    /// so member order has to be the order they were added, not reflection order,
    /// or every re-run shows a diff that means nothing.
    /// </para>
    /// <para>
    /// Numbers are kept as their source text and parsed on demand with
    /// <see cref="CultureInfo.InvariantCulture"/>. Round-tripping through
    /// <c>double</c> would quietly reformat, and a manifest whose numbers drift
    /// with the machine locale is not a manifest.
    /// </para>
    /// </summary>
    public sealed class NebulaJsonValue
    {
        private enum Kind { Null, Bool, Number, String, Array, Object }

        private Kind _kind;
        private string _text;
        private List<NebulaJsonValue> _array;
        private List<KeyValuePair<string, NebulaJsonValue>> _members;

        // ---- reading ---------------------------------------------------------

        public int Count => _kind == Kind.Array ? _array.Count : _members?.Count ?? 0;

        /// <summary>Member lookup. Returns null for a missing member or a non-object.</summary>
        public NebulaJsonValue this[string key]
        {
            get
            {
                if (_kind != Kind.Object || _members == null) return null;
                for (int i = 0; i < _members.Count; i++)
                    if (string.Equals(_members[i].Key, key, StringComparison.Ordinal))
                        return _members[i].Value;
                return null;
            }
        }

        /// <summary>Element lookup. Returns null for an out-of-range index or a non-array.</summary>
        public NebulaJsonValue this[int index] =>
            _kind == Kind.Array && index >= 0 && index < _array.Count ? _array[index] : null;

        public IEnumerable<string> Keys
        {
            get
            {
                if (_kind != Kind.Object || _members == null) yield break;
                for (int i = 0; i < _members.Count; i++) yield return _members[i].Key;
            }
        }

        public string AsString() => _kind == Kind.String || _kind == Kind.Number ? _text : null;

        public int AsInt(int fallback) =>
            _kind == Kind.Number && int.TryParse(
                _text, NumberStyles.Any, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;

        public float AsFloat(float fallback) =>
            _kind == Kind.Number && float.TryParse(
                _text, NumberStyles.Any, CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;

        public bool AsBool(bool fallback) =>
            _kind == Kind.Bool ? string.Equals(_text, "true", StringComparison.Ordinal) : fallback;

        public static NebulaJsonValue Parse(string json)
        {
            int index = 0;
            NebulaJsonValue value = ParseValue(json, ref index);
            return value;
        }

        private static NebulaJsonValue ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) return new NebulaJsonValue { _kind = Kind.Null };

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new NebulaJsonValue { _kind = Kind.String, _text = ParseString(s, ref i) };
                case 't': i += 4; return new NebulaJsonValue { _kind = Kind.Bool, _text = "true" };
                case 'f': i += 5; return new NebulaJsonValue { _kind = Kind.Bool, _text = "false" };
                case 'n': i += 4; return new NebulaJsonValue { _kind = Kind.Null };
                default: return ParseNumber(s, ref i);
            }
        }

        private static NebulaJsonValue ParseObject(string s, ref int i)
        {
            var value = new NebulaJsonValue
            {
                _kind = Kind.Object,
                _members = new List<KeyValuePair<string, NebulaJsonValue>>()
            };
            i++; // '{'
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return value; }

            while (i < s.Length)
            {
                SkipWhitespace(s, ref i);
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                NebulaJsonValue member = ParseValue(s, ref i);
                value._members.Add(new KeyValuePair<string, NebulaJsonValue>(key, member));
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return value;
        }

        private static NebulaJsonValue ParseArray(string s, ref int i)
        {
            var value = new NebulaJsonValue { _kind = Kind.Array, _array = new List<NebulaJsonValue>() };
            i++; // '['
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return value; }

            while (i < s.Length)
            {
                value._array.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return value;
        }

        private static NebulaJsonValue ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-.eE0123456789".IndexOf(s[i]) >= 0) i++;
            return new NebulaJsonValue { _kind = Kind.Number, _text = s.Substring(start, i - start) };
        }

        private static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return string.Empty;
            i++;
            var builder = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { builder.Append(c); continue; }

                char escape = s[i++];
                switch (escape)
                {
                    case 'n': builder.Append('\n'); break;
                    case 't': builder.Append('\t'); break;
                    case 'r': builder.Append('\r'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'u':
                        builder.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: builder.Append(escape); break;
                }
            }
            return builder.ToString();
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }

    /// <summary>
    /// Append-only JSON writer used for every generated report and manifest.
    /// <para>
    /// Members come out in the order they were written and floats are formatted
    /// with <c>R</c> under the invariant culture, so re-running the pipeline over
    /// unchanged sources produces a byte-identical file. That is not tidiness: the
    /// idempotency test compares the manifest's own hash across two runs, and a
    /// writer that reordered members or emitted <c>0,5</c> on a Turkish locale
    /// would make that test fail for a reason that has nothing to do with textures.
    /// </para>
    /// </summary>
    public sealed class NebulaJsonWriter
    {
        private readonly StringBuilder _builder = new StringBuilder();
        private readonly List<bool> _firstAtDepth = new List<bool>();
        private int _depth;

        public NebulaJsonWriter() => Push();

        private void Push()
        {
            if (_firstAtDepth.Count <= _depth) _firstAtDepth.Add(true);
            else _firstAtDepth[_depth] = true;
        }

        private void Separator()
        {
            if (_firstAtDepth[_depth]) _firstAtDepth[_depth] = false;
            else _builder.Append(',');
            _builder.Append('\n').Append(new string(' ', _depth * 2));
        }

        public NebulaJsonWriter BeginObject(string key = null)
        {
            Separator();
            if (key != null) _builder.Append(Quote(key)).Append(": ");
            _builder.Append('{');
            _depth++;
            Push();
            return this;
        }

        public NebulaJsonWriter EndObject()
        {
            _depth--;
            if (!_firstAtDepth[_depth + 1]) _builder.Append('\n').Append(new string(' ', _depth * 2 + 2));
            _builder.Append('}');
            return this;
        }

        public NebulaJsonWriter BeginArray(string key = null)
        {
            Separator();
            if (key != null) _builder.Append(Quote(key)).Append(": ");
            _builder.Append('[');
            _depth++;
            Push();
            return this;
        }

        public NebulaJsonWriter EndArray()
        {
            _depth--;
            if (!_firstAtDepth[_depth + 1]) _builder.Append('\n').Append(new string(' ', _depth * 2 + 2));
            _builder.Append(']');
            return this;
        }

        public NebulaJsonWriter Write(string key, string value)
        {
            Separator();
            _builder.Append(Quote(key)).Append(": ").Append(value == null ? "null" : Quote(value));
            return this;
        }

        public NebulaJsonWriter Write(string key, int value)
        {
            Separator();
            _builder.Append(Quote(key)).Append(": ")
                .Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public NebulaJsonWriter Write(string key, long value)
        {
            Separator();
            _builder.Append(Quote(key)).Append(": ")
                .Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public NebulaJsonWriter Write(string key, bool value)
        {
            Separator();
            _builder.Append(Quote(key)).Append(": ").Append(value ? "true" : "false");
            return this;
        }

        public NebulaJsonWriter Write(string key, float value)
        {
            Separator();
            _builder.Append(Quote(key)).Append(": ")
                .Append(value.ToString("R", CultureInfo.InvariantCulture));
            return this;
        }

        public NebulaJsonWriter WriteRaw(string value)
        {
            Separator();
            _builder.Append(value);
            return this;
        }

        public override string ToString() => _builder.ToString().TrimStart('\n') + "\n";

        private static string Quote(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }
    }
}
