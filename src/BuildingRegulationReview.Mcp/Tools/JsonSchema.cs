using System.Collections.Generic;
using BuildingRegulationReview.Mcp.Json;

namespace BuildingRegulationReview.Mcp.Tools;

/// <summary>Builders for the small part of JSON Schema the tool definitions use.</summary>
public static class JsonSchema
{
    public static JsonObject Object(JsonObject properties, params string[] required)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Length > 0) schema["required"] = JsonValue.Array(required);
        schema["additionalProperties"] = false;
        return schema;
    }

    public static JsonObject Empty() => Object(new JsonObject());

    public static JsonObject String(string description) =>
        new() { ["type"] = "string", ["description"] = description };

    /// <summary>A string, or null to clear the value it sets.</summary>
    public static JsonObject NullableString(string description) =>
        new() { ["type"] = new JsonArray { "string", "null" }, ["description"] = description };

    public static JsonObject Enum(string description, IEnumerable<string> values) =>
        new() { ["type"] = "string", ["enum"] = JsonValue.Array(values), ["description"] = description };

    public static JsonObject Boolean(string description, bool? fallback = null)
    {
        var schema = new JsonObject { ["type"] = "boolean", ["description"] = description };
        if (fallback.HasValue) schema["default"] = fallback.Value;
        return schema;
    }

    /// <summary>true／false, or null to clear the value — a 是／否／未填 parameter.</summary>
    public static JsonObject NullableBoolean(string description) =>
        new() { ["type"] = new JsonArray { "boolean", "null" }, ["description"] = description };

    public static JsonObject Integer(string description, int? minimum = null, int? maximum = null, int? fallback = null)
    {
        var schema = new JsonObject { ["type"] = "integer", ["description"] = description };
        if (minimum.HasValue) schema["minimum"] = minimum.Value;
        if (maximum.HasValue) schema["maximum"] = maximum.Value;
        if (fallback.HasValue) schema["default"] = fallback.Value;
        return schema;
    }

    public static JsonObject NullableInteger(string description) =>
        new() { ["type"] = new JsonArray { "integer", "null" }, ["description"] = description };

    public static JsonObject NullableNumber(string description, double? minimum = null)
    {
        var schema = new JsonObject { ["type"] = new JsonArray { "number", "null" }, ["description"] = description };
        if (minimum.HasValue) schema["minimum"] = minimum.Value;
        return schema;
    }

    public static JsonObject Array(JsonObject items, string description) =>
        new() { ["type"] = "array", ["items"] = items, ["description"] = description };
}
