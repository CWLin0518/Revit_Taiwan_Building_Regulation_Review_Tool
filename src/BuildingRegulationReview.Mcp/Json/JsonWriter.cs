using System;
using System.Globalization;
using System.Text;

namespace BuildingRegulationReview.Mcp.Json;

/// <summary>Compact JSON. Non-ASCII text is written as is; the transport encodes it as UTF-8.</summary>
public static class JsonWriter
{
    public static string Write(JsonValue? value)
    {
        var builder = new StringBuilder();
        Write(builder, value);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, JsonValue? value)
    {
        switch (value)
        {
            case null:
            case JsonNull _:
                builder.Append("null");
                break;
            case JsonBoolean boolean:
                builder.Append(boolean.Value ? "true" : "false");
                break;
            case JsonNumber number:
                builder.Append(FormatNumber(number.Value));
                break;
            case JsonString text:
                WriteString(builder, text.Value);
                break;
            case JsonArray array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in array)
                {
                    if (!firstItem) builder.Append(',');
                    firstItem = false;
                    Write(builder, item);
                }
                builder.Append(']');
                break;
            case JsonObject obj:
                builder.Append('{');
                var firstMember = true;
                foreach (var member in obj)
                {
                    if (!firstMember) builder.Append(',');
                    firstMember = false;
                    WriteString(builder, member.Key);
                    builder.Append(':');
                    Write(builder, member.Value);
                }
                builder.Append('}');
                break;
            default:
                throw new ArgumentException("Unknown JSON value type.", nameof(value));
        }
    }

    /// <summary>Whole numbers without a fraction, so an element id reads 12345 rather than 12345.0 or 1.2345E+4.</summary>
    internal static string FormatNumber(double value)
    {
        if (Math.Abs(value) < 1e15 && Math.Floor(value) == value)
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default:
                    if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }
}
