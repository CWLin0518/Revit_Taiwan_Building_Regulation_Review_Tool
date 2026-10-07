using System;
using System.Globalization;
using System.Text;

namespace BuildingRegulationReview.Mcp.Json;

public sealed class JsonParseException : FormatException
{
    public JsonParseException(string message, int position)
        : base($"{message} (position {position})") => Position = position;

    public int Position { get; }
}

/// <summary>Strict RFC 8259 JSON. Anything else — comments, trailing commas, NaN — is refused.</summary>
public static class JsonParser
{
    /// <summary>Deep enough for any MCP message; shallow enough that hostile input cannot overflow the stack.</summary>
    public const int MaxDepth = 128;

    public static JsonValue Parse(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        var reader = new Reader(text);
        reader.SkipWhitespace();
        var value = reader.ReadValue(0);
        reader.SkipWhitespace();
        if (!reader.AtEnd) throw reader.Error("Unexpected text after the JSON value");
        return value;
    }

    private sealed class Reader
    {
        private readonly string _text;
        private int _position;

        public Reader(string text) => _text = text;

        public bool AtEnd => _position >= _text.Length;

        public JsonParseException Error(string message) => new(message, _position);

        public void SkipWhitespace()
        {
            while (!AtEnd)
            {
                var c = _text[_position];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return;
                _position++;
            }
        }

        public JsonValue ReadValue(int depth)
        {
            if (depth > MaxDepth) throw Error("JSON nested too deeply");
            if (AtEnd) throw Error("Unexpected end of JSON");

            switch (_text[_position])
            {
                case '{': return ReadObject(depth);
                case '[': return ReadArray(depth);
                case '"': return new JsonString(ReadString());
                case 't': Expect("true"); return JsonBoolean.True;
                case 'f': Expect("false"); return JsonBoolean.False;
                case 'n': Expect("null"); return JsonNull.Instance;
                default: return ReadNumber();
            }
        }

        private void Expect(string literal)
        {
            if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0) throw Error("Invalid literal");
            _position += literal.Length;
        }

        private JsonObject ReadObject(int depth)
        {
            var result = new JsonObject();
            _position++;
            SkipWhitespace();
            if (Peek() == '}')
            {
                _position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"') throw Error("Expected a member name");
                var name = ReadString();
                SkipWhitespace();
                if (Peek() != ':') throw Error("Expected ':'");
                _position++;
                SkipWhitespace();
                result.Set(name, ReadValue(depth + 1));
                SkipWhitespace();
                var next = Peek();
                _position++;
                if (next == '}') return result;
                if (next != ',') throw Error("Expected ',' or '}'");
            }
        }

        private JsonArray ReadArray(int depth)
        {
            var result = new JsonArray();
            _position++;
            SkipWhitespace();
            if (Peek() == ']')
            {
                _position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                result.Add(ReadValue(depth + 1));
                SkipWhitespace();
                var next = Peek();
                _position++;
                if (next == ']') return result;
                if (next != ',') throw Error("Expected ',' or ']'");
            }
        }

        private string ReadString()
        {
            _position++;
            var builder = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw Error("Unterminated string");
                var c = _text[_position++];
                if (c == '"') return builder.ToString();
                if (c < 0x20) throw Error("Control character in string");
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (AtEnd) throw Error("Unterminated escape");
                var escape = _text[_position++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u': builder.Append(ReadHex()); break;
                    default: throw Error("Invalid escape");
                }
            }
        }

        private char ReadHex()
        {
            if (_position + 4 > _text.Length) throw Error("Truncated \\u escape");
            if (!ushort.TryParse(_text.Substring(_position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                throw Error("Invalid \\u escape");
            _position += 4;
            return (char)code;
        }

        private JsonValue ReadNumber()
        {
            var start = _position;
            if (Peek() == '-') _position++;

            if (Peek() == '0') _position++;
            else if (IsDigit(Peek())) SkipDigits();
            else throw Error("Unexpected character");

            if (Peek() == '.')
            {
                _position++;
                if (!IsDigit(Peek())) throw Error("Expected a digit after '.'");
                SkipDigits();
            }

            if (Peek() == 'e' || Peek() == 'E')
            {
                _position++;
                if (Peek() == '+' || Peek() == '-') _position++;
                if (!IsDigit(Peek())) throw Error("Expected a digit in the exponent");
                SkipDigits();
            }

            var text = _text.Substring(start, _position - start);
            var value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (double.IsInfinity(value)) throw Error("Number out of range");
            return new JsonNumber(value);
        }

        private void SkipDigits()
        {
            while (IsDigit(Peek())) _position++;
        }

        private char Peek() => AtEnd ? '\0' : _text[_position];

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
    }
}
