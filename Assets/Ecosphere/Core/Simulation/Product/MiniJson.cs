// Ecosphere — stage 07: tiny JSON writer/reader for save headers (pure, engine-free).
//
// Unity ships no JSON library in the default profile and Newtonsoft is not a dependency
// of this project, so the save header (a flat, well-known object of strings/numbers/
// bools) is written and parsed by hand. This is deliberately NOT a general JSON parser:
// it supports exactly one level of object nesting with scalar values, which is all the
// header format needs, and it fails loudly on anything else.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Minimal flat-object JSON codec used by the save header.</summary>
    public static class MiniJson
    {
        private const string Indent = "  ";

        /// <summary>
        /// Writes a flat object. Values may be string, bool, or any numeric primitive.
        /// Keys keep insertion order (stable diffs in version control and in tests).
        /// </summary>
        public static string WriteObject(IEnumerable<KeyValuePair<string, object>> fields, bool pretty = true)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> field in fields)
            {
                if (!first) sb.Append(pretty ? ",\n" : ",");
                first = false;
                if (pretty) sb.Append('\n').Append(Indent);
                WriteString(sb, field.Key);
                sb.Append(':');
                if (pretty) sb.Append(' ');
                WriteValue(sb, field.Value);
            }
            if (!first && pretty) sb.Append('\n');
            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case uint u:
                    sb.Append(u.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case ulong ul:
                    sb.Append(ul.ToString(CultureInfo.InvariantCulture));
                    break;
                case byte by:
                    sb.Append(by.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MiniJson supports only string/bool/numeric header values, got " + value.GetType().Name);
            }
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>
        /// Parses a flat JSON object into a string→string dictionary. Numbers keep their
        /// invariant text form; callers convert with <see cref="ParseULong"/> etc., so a
        /// malformed header is reported per field instead of corrupting the whole file.
        /// </summary>
        public static bool TryParseObject(string json, out Dictionary<string, string> fields, out string error)
        {
            fields = new Dictionary<string, string>(StringComparer.Ordinal);
            error = null;
            if (string.IsNullOrEmpty(json))
            {
                error = "empty json";
                return false;
            }

            int i = 0;
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '{')
            {
                error = "expected '{'";
                return false;
            }
            i++;

            while (true)
            {
                SkipWhitespace(json, ref i);
                if (i >= json.Length)
                {
                    error = "unterminated object";
                    return false;
                }
                if (json[i] == '}')
                {
                    i++;
                    break;
                }
                if (json[i] == ',')
                {
                    i++;
                    continue;
                }

                string key;
                if (!TryReadString(json, ref i, out key, out error)) return false;
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != ':')
                {
                    error = "expected ':' after key '" + key + "'";
                    return false;
                }
                i++;
                SkipWhitespace(json, ref i);

                string value;
                if (i < json.Length && json[i] == '"')
                {
                    if (!TryReadString(json, ref i, out value, out error)) return false;
                }
                else
                {
                    int start = i;
                    while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != '\n' && json[i] != '\r') i++;
                    value = json.Substring(start, i - start).Trim();
                }
                fields[key] = value;
            }

            return true;
        }

        private static void SkipWhitespace(string json, ref int i)
        {
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        }

        private static bool TryReadString(string json, ref int i, out string value, out string error)
        {
            value = null;
            error = null;
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '"')
            {
                error = "expected string";
                return false;
            }
            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '"')
                {
                    value = sb.ToString();
                    return true;
                }
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= json.Length)
                {
                    error = "dangling escape";
                    return false;
                }
                char escape = json[i++];
                switch (escape)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > json.Length)
                        {
                            error = "truncated unicode escape";
                            return false;
                        }
                        if (!ushort.TryParse(json.Substring(i, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out ushort code))
                        {
                            error = "bad unicode escape";
                            return false;
                        }
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default:
                        error = "unknown escape '\\" + escape + "'";
                        return false;
                }
            }
            error = "unterminated string";
            return false;
        }

        public static ulong ParseULong(Dictionary<string, string> fields, string key, ulong fallback = 0UL)
        {
            if (fields == null || !fields.TryGetValue(key, out string text)) return fallback;
            return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value) ? value : fallback;
        }

        public static long ParseLong(Dictionary<string, string> fields, string key, long fallback = 0L)
        {
            if (fields == null || !fields.TryGetValue(key, out string text)) return fallback;
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : fallback;
        }

        public static int ParseInt(Dictionary<string, string> fields, string key, int fallback = 0)
        {
            if (fields == null || !fields.TryGetValue(key, out string text)) return fallback;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        public static uint ParseUInt(Dictionary<string, string> fields, string key, uint fallback = 0u)
        {
            if (fields == null || !fields.TryGetValue(key, out string text)) return fallback;
            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value) ? value : fallback;
        }

        public static float ParseFloat(Dictionary<string, string> fields, string key, float fallback = 0f)
        {
            if (fields == null || !fields.TryGetValue(key, out string text)) return fallback;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }

        public static string ParseString(Dictionary<string, string> fields, string key, string fallback = "")
        {
            if (fields == null || !fields.TryGetValue(key, out string text)) return fallback;
            return text;
        }

        public static bool ParseBool(Dictionary<string, string> fields, string key, bool fallback = false)
        {
            if (fields == null || !fields.TryGetValue(key, out string text)) return fallback;
            if (text == "true" || text == "1") return true;
            if (text == "false" || text == "0") return false;
            return fallback;
        }

        public static Locale ParseLocale(Dictionary<string, string> fields, string key, Locale fallback = Locale.En)
        {
            if (fields == null || !fields.TryGetValue(key, out string text)) return fallback;
            if (string.IsNullOrEmpty(text)) return fallback;
            return LocCatalog.FromCode(text);
        }
    }
}
