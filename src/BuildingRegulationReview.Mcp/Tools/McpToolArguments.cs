using System;
using System.Collections.Generic;
using System.Globalization;
using BuildingRegulationReview.Mcp.Json;

namespace BuildingRegulationReview.Mcp.Tools;

/// <summary>
/// The <c>arguments</c> object of a tool call, read with the checks every tool would otherwise
/// repeat. A wrong type or a missing required argument throws <see cref="McpToolException"/> with a
/// message that names the argument, so the agent can correct the call by itself.
/// </summary>
public sealed class McpToolArguments
{
    public McpToolArguments(JsonObject? arguments) => Json = arguments ?? new JsonObject();

    public JsonObject Json { get; }

    /// <summary>Present at all, even as JSON null — which is how a caller asks to clear a value.</summary>
    public bool Has(string name) => Json.Contains(name);

    public JsonValue? Raw(string name) => Json[name];

    public string RequireString(string name) =>
        OptionalString(name) is { Length: > 0 } value ? value : throw Missing(name, "字串");

    public string? OptionalString(string name)
    {
        var value = Json[name];
        if (value is null || value.IsNull) return null;
        return value.AsString() ?? throw WrongType(name, "字串");
    }

    public Guid RequireGuid(string name)
    {
        var text = RequireString(name);
        return Guid.TryParse(text, out var id) ? id : throw new McpToolException($"參數 {name} 必須是 GUID，收到「{text}」。");
    }

    public Guid? OptionalGuid(string name)
    {
        var text = OptionalString(name);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Guid.TryParse(text, out var id) ? id : throw new McpToolException($"參數 {name} 必須是 GUID，收到「{text}」。");
    }

    public bool OptionalBool(string name, bool fallback) => OptionalNullableBool(name) ?? fallback;

    public bool? OptionalNullableBool(string name)
    {
        var value = Json[name];
        if (value is null || value.IsNull) return null;
        return value.AsBoolean() ?? throw WrongType(name, "true 或 false");
    }

    public int OptionalInt(string name, int fallback, int min = int.MinValue, int max = int.MaxValue) =>
        OptionalNullableInt(name, min, max) ?? fallback;

    public int? OptionalNullableInt(string name, int min = int.MinValue, int max = int.MaxValue)
    {
        var value = Json[name];
        if (value is null || value.IsNull) return null;
        var number = value.AsNumber() ?? throw WrongType(name, "整數");
        if (Math.Floor(number) != number) throw WrongType(name, "整數");
        if (number < min || number > max)
            throw new McpToolException($"參數 {name} 必須介於 {min} 與 {max} 之間，收到 {number.ToString(CultureInfo.InvariantCulture)}。");
        return (int)number;
    }

    public double? OptionalNumber(string name, double min = double.MinValue, double max = double.MaxValue)
    {
        var value = Json[name];
        if (value is null || value.IsNull) return null;
        var number = value.AsNumber() ?? throw WrongType(name, "數字");
        if (number < min || number > max)
            throw new McpToolException($"參數 {name} 必須介於 {min.ToString(CultureInfo.InvariantCulture)} 與 {max.ToString(CultureInfo.InvariantCulture)} 之間。");
        return number;
    }

    public JsonArray? OptionalArray(string name)
    {
        var value = Json[name];
        if (value is null || value.IsNull) return null;
        return value.AsArray() ?? throw WrongType(name, "陣列");
    }

    public IReadOnlyList<string> OptionalStrings(string name)
    {
        var array = OptionalArray(name);
        var result = new List<string>();
        if (array is null) return result;
        foreach (var item in array)
            result.Add(item.AsString() ?? throw WrongType(name, "字串陣列"));
        return result;
    }

    /// <summary>One element of an array argument, read with the same checks; the name says where it came from.</summary>
    public static McpToolArguments Of(JsonValue item, string name) =>
        new(item.AsObject() ?? throw new McpToolException($"{name} 的每一項都必須是物件。"));

    private static McpToolException Missing(string name, string expected) =>
        new($"缺少必要參數 {name}（{expected}）。");

    private static McpToolException WrongType(string name, string expected) =>
        new($"參數 {name} 必須是{expected}。");
}
