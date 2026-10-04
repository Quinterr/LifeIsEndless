// Ecosphere — Genome JSON export/import. Pure C#, no engine references.
// Round-trips exactly: export → import → identical Gene list.

using System;
using System.Collections.Generic;
using System.Text;

namespace Ecosphere.Core.Simulation
{
    /// <summary>
    /// Lightweight JSON serializer/deserializer for genomes.
    /// Minimal implementation — handles objects, arrays, strings, numbers.
    /// No allocations on the hot path (import/export are rare operations).
    /// </summary>
    public static class GenomeJson
    {
        // ── Export ───────────────────────────────────────────────────

        /// <summary>Serialize a genome to JSON string. Round-trips with <see cref="Import"/>.</summary>
        public static string Export(IReadOnlyList<Gene> genes, GeneKingdom kingdom, uint genomeSeed)
        {
            var sb = new StringBuilder(genes.Count * 64 + 64);
            sb.Append("{\"version\":1,\"kingdom\":");
            sb.Append((int)kingdom);
            sb.Append(",\"genomeSeed\":");
            sb.Append(genomeSeed);
            sb.Append(",\"genes\":[");
            for (int i = 0; i < genes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Gene g = genes[i];
                sb.Append("{\"id\":");
                sb.Append(g.TypeId);
                sb.Append(",\"v\":");
                AppendFloat(sb, g.Value);
                sb.Append(",\"d\":");
                AppendFloat(sb, g.Dominance);
                sb.Append(",\"m\":");
                AppendFloat(sb, g.MutationRate);
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendFloat(StringBuilder sb, float v)
        {
            // Round-trip safe: 7 significant digits
            sb.Append(v.ToString("G7", System.Globalization.CultureInfo.InvariantCulture));
        }

        // ── Import ───────────────────────────────────────────────────

        /// <summary>
        /// Deserialize a genome from JSON. Returns the gene list, kingdom, and genome seed.
        /// Throws FormatException on malformed input.
        /// </summary>
        public static (List<Gene> genes, GeneKingdom kingdom, uint genomeSeed) Import(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new FormatException("Empty genome JSON");
            var reader = new JsonReader(json);
            var obj = reader.ReadObject();

            if (!obj.TryGetValue("kingdom", out object kingdomObj))
                throw new FormatException("Missing 'kingdom' field");
            GeneKingdom kingdom = (GeneKingdom)(int)(double)kingdomObj;

            uint genomeSeed = 0;
            if (obj.TryGetValue("genomeSeed", out object seedObj))
                genomeSeed = (uint)(double)seedObj;

            if (!obj.TryGetValue("genes", out object genesObj) || genesObj is not List<object> geneArray)
                throw new FormatException("Missing 'genes' array");

            var genes = new List<Gene>(geneArray.Count);
            foreach (object geneObj in geneArray)
            {
                if (geneObj is not Dictionary<string, object> geneDict)
                    throw new FormatException("Gene entry is not an object");
                genes.Add(new Gene
                {
                    TypeId = (ushort)(double)geneDict["id"],
                    Value = (float)(double)geneDict["v"],
                    Dominance = (float)(double)geneDict["d"],
                    MutationRate = (float)(double)geneDict["m"]
                });
            }
            return (genes, kingdom, genomeSeed);
        }

        // ── Minimal JSON parser ──────────────────────────────────────
        // Handles: object {}, array [], string "", number, bool, null.

        private sealed class JsonReader
        {
            private readonly string _text;
            private int _pos;

            public JsonReader(string text) { _text = text; _pos = 0; }

            public Dictionary<string, object> ReadObject()
            {
                SkipWhitespace();
                Expect('{');
                var dict = new Dictionary<string, object>();
                SkipWhitespace();
                if (Peek() == '}') { Advance(); return dict; }
                while (true)
                {
                    SkipWhitespace();
                    string key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    object value = ReadValue();
                    dict[key] = value;
                    SkipWhitespace();
                    if (Peek() == ',') { Advance(); continue; }
                    break;
                }
                Expect('}');
                return dict;
            }

            private object ReadValue()
            {
                SkipWhitespace();
                char c = Peek();
                if (c == '"') return ReadString();
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == 't') { ExpectLiteral("true"); return true; }
                if (c == 'f') { ExpectLiteral("false"); return false; }
                if (c == 'n') { ExpectLiteral("null"); return null; }
                return ReadNumber();
            }

            private List<object> ReadArray()
            {
                Expect('[');
                var list = new List<object>();
                SkipWhitespace();
                if (Peek() == ']') { Advance(); return list; }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (Peek() == ',') { Advance(); continue; }
                    break;
                }
                Expect(']');
                return list;
            }

            private string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (_pos < _text.Length && _text[_pos] != '"')
                {
                    if (_text[_pos] == '\\')
                    {
                        _pos++;
                        if (_pos >= _text.Length) throw new FormatException("Unterminated string escape");
                        char esc = _text[_pos++];
                        sb.Append(esc switch
                        {
                            '"' => '"', '\\' => '\\', '/' => '/',
                            'n' => '\n', 'r' => '\r', 't' => '\t',
                            _ => esc
                        });
                    }
                    else
                    {
                        sb.Append(_text[_pos++]);
                    }
                }
                Expect('"');
                return sb.ToString();
            }

            private double ReadNumber()
            {
                int start = _pos;
                if (_pos < _text.Length && (_text[_pos] == '-' || _text[_pos] == '+')) _pos++;
                while (_pos < _text.Length && char.IsDigit(_text[_pos])) _pos++;
                if (_pos < _text.Length && _text[_pos] == '.')
                {
                    _pos++;
                    while (_pos < _text.Length && char.IsDigit(_text[_pos])) _pos++;
                }
                if (_pos < _text.Length && (_text[_pos] == 'e' || _text[_pos] == 'E'))
                {
                    _pos++;
                    if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                    while (_pos < _text.Length && char.IsDigit(_text[_pos])) _pos++;
                }
                string numStr = _text.Substring(start, _pos - start);
                return double.Parse(numStr, System.Globalization.CultureInfo.InvariantCulture);
            }

            private void SkipWhitespace()
            {
                while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
            }

            private char Peek()
            {
                if (_pos >= _text.Length) throw new FormatException("Unexpected end of JSON");
                return _text[_pos];
            }

            private void Advance() { _pos++; }

            private void Expect(char c)
            {
                if (_pos >= _text.Length || _text[_pos] != c)
                    throw new FormatException($"Expected '{c}' at position {_pos}");
                _pos++;
            }

            private void ExpectLiteral(string literal)
            {
                for (int i = 0; i < literal.Length; i++)
                    if (_pos >= _text.Length || _text[_pos++] != literal[i])
                        throw new FormatException($"Expected '{literal}'");
            }
        }
    }
}