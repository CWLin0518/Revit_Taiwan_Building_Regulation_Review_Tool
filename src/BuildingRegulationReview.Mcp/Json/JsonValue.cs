using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace BuildingRegulationReview.Mcp.Json;

public enum JsonKind
{
    Null,
    Boolean,
    Number,
    String,
    Array,
    Object
}

/// <summary>
/// A JSON value. The add-in runs inside Revit's process, where a second copy of a JSON library can
/// collide with the one Revit or another add-in already loaded; MCP needs so little of JSON that a
/// small DOM of our own is the safer choice (docs/adr/0004).
/// </summary>
/// <remarks>
/// Values convert implicitly from the CLR types a tool result is built from, and a null string or a
/// null nullable converts to <see cref="Null"/>, so <c>obj["name"] = maybeNull</c> writes
/// <c>null</c> rather than throwing. A null <see cref="JsonValue"/> reference is read as JSON null
/// everywhere for the same reason.
/// </remarks>
public abstract class JsonValue
{
    public static JsonValue Null => JsonNull.Instance;

    public abstract JsonKind Kind { get; }

    public bool IsNull => Kind == JsonKind.Null;

    public virtual string? AsString() => null;
    public virtual double? AsNumber() => null;
    public virtual bool? AsBoolean() => null;
    public virtual JsonObject? AsObject() => null;
    public virtual JsonArray? AsArray() => null;

    public static JsonValue Parse(string text) => JsonParser.Parse(text);

    /// <summary>Compact JSON text.</summary>
    public override string ToString() => JsonWriter.Write(this);

    public static implicit operator JsonValue(string? value) => value is null ? JsonNull.Instance : new JsonString(value);
    public static implicit operator JsonValue(bool value) => value ? JsonBoolean.True : JsonBoolean.False;
    public static implicit operator JsonValue(bool? value) => value.HasValue ? (JsonValue)value.Value : JsonNull.Instance;
    public static implicit operator JsonValue(int value) => new JsonNumber(value);
    public static implicit operator JsonValue(int? value) => value.HasValue ? new JsonNumber(value.Value) : JsonNull.Instance;
    public static implicit operator JsonValue(long value) => new JsonNumber(value);
    public static implicit operator JsonValue(double value) => JsonNumber.Of(value);
    public static implicit operator JsonValue(double? value) => value.HasValue ? JsonNumber.Of(value.Value) : JsonNull.Instance;

    public static JsonValue Of(Guid value) => new JsonString(value.ToString("D"));
    public static JsonValue Of(Guid? value) => value.HasValue ? Of(value.Value) : JsonNull.Instance;
    public static JsonValue Of(DateTime value) => new JsonString(value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
    public static JsonValue Of(DateTime? value) => value.HasValue ? Of(value.Value) : JsonNull.Instance;

    /// <summary>An enum by its member name, the spelling the tool schemas use.</summary>
    public static JsonValue Of<TEnum>(TEnum value) where TEnum : struct, Enum => new JsonString(value.ToString());

    public static JsonValue Of<TEnum>(TEnum? value) where TEnum : struct, Enum =>
        value.HasValue ? new JsonString(value.Value.ToString()) : JsonNull.Instance;

    public static JsonArray Array(IEnumerable<string?> values)
    {
        var array = new JsonArray();
        foreach (var value in values) array.Add(value);
        return array;
    }
}

public sealed class JsonNull : JsonValue
{
    public static readonly JsonNull Instance = new();

    private JsonNull()
    {
    }

    public override JsonKind Kind => JsonKind.Null;
}

public sealed class JsonBoolean : JsonValue
{
    public static readonly JsonBoolean True = new(true);
    public static readonly JsonBoolean False = new(false);

    private JsonBoolean(bool value) => Value = value;

    public bool Value { get; }
    public override JsonKind Kind => JsonKind.Boolean;
    public override bool? AsBoolean() => Value;
}

public sealed class JsonNumber : JsonValue
{
    public JsonNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), "JSON has no NaN or Infinity.");
        Value = value;
    }

    public double Value { get; }
    public override JsonKind Kind => JsonKind.Number;
    public override double? AsNumber() => Value;

    /// <summary>The number, or JSON null for NaN and the infinities, which JSON cannot spell.</summary>
    public static JsonValue Of(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? JsonNull.Instance : new JsonNumber(value);
}

public sealed class JsonString : JsonValue
{
    public JsonString(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    public string Value { get; }
    public override JsonKind Kind => JsonKind.String;
    public override string? AsString() => Value;
}

public sealed class JsonArray : JsonValue, IEnumerable<JsonValue>
{
    private readonly List<JsonValue> _items = new();

    public JsonArray()
    {
    }

    public JsonArray(IEnumerable<JsonValue> items)
    {
        foreach (var item in items) Add(item);
    }

    public override JsonKind Kind => JsonKind.Array;
    public override JsonArray? AsArray() => this;

    public int Count => _items.Count;
    public JsonValue this[int index] => _items[index];

    public void Add(JsonValue? item) => _items.Add(item ?? JsonNull.Instance);

    public IEnumerator<JsonValue> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A JSON object that keeps its members in insertion order, so tool output reads in the order it was written.</summary>
public sealed class JsonObject : JsonValue, IEnumerable<KeyValuePair<string, JsonValue>>
{
    private readonly List<KeyValuePair<string, JsonValue>> _members = new();
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public override JsonKind Kind => JsonKind.Object;
    public override JsonObject? AsObject() => this;

    public int Count => _members.Count;
    public IEnumerable<string> Keys
    {
        get
        {
            foreach (var member in _members) yield return member.Key;
        }
    }

    /// <summary>The member, or null when the object has none by that name. Setting replaces in place.</summary>
    public JsonValue? this[string name]
    {
        get => _index.TryGetValue(name, out var i) ? _members[i].Value : null;
        set => Set(name, value);
    }

    public bool Contains(string name) => _index.ContainsKey(name);

    /// <summary>For collection initialisers; a repeated name replaces the earlier value.</summary>
    public void Add(string name, JsonValue? value) => Set(name, value);

    public JsonObject Set(string name, JsonValue? value)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        var member = new KeyValuePair<string, JsonValue>(name, value ?? JsonNull.Instance);
        if (_index.TryGetValue(name, out var i)) _members[i] = member;
        else
        {
            _index[name] = _members.Count;
            _members.Add(member);
        }
        return this;
    }

    public IEnumerator<KeyValuePair<string, JsonValue>> GetEnumerator() => _members.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
