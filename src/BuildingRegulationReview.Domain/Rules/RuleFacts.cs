using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules.Expressions;

namespace BuildingRegulationReview.Domain.Rules;

public enum RuleFactGapKind
{
    /// <summary>Nobody supplied the field: the parameter is empty or was never read.</summary>
    Missing,

    /// <summary>The field was read but could not be understood, e.g. a fire rating written as "一小時半".</summary>
    Unreadable
}

/// <summary>A field an evaluation needed but did not have. Gaps are why a result is InsufficientData.</summary>
public sealed class RuleFactGap : IEquatable<RuleFactGap>
{
    public RuleFactGap(string field, RuleFactGapKind kind, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(field)) throw new ArgumentException("Field is required.", nameof(field));
        if (!Enum.IsDefined(typeof(RuleFactGapKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));

        Field = field.Trim();
        Kind = kind;
        Reason = reason?.Trim() ?? string.Empty;
    }

    public string Field { get; }
    public RuleFactGapKind Kind { get; }
    public string Reason { get; }

    public bool Equals(RuleFactGap? other) =>
        other is not null && string.Equals(Field, other.Field, StringComparison.Ordinal) && Kind == other.Kind;

    public override bool Equals(object? obj) => Equals(obj as RuleFactGap);
    public override int GetHashCode() => (StringComparer.Ordinal.GetHashCode(Field) * 31) + (int)Kind;

    public override string ToString() => Kind == RuleFactGapKind.Missing
        ? $"{Field} 未設定"
        : Reason.Length == 0 ? $"{Field} 格式無法判讀" : $"{Field} 格式無法判讀（{Reason}）";
}

/// <summary>
/// The values one subject offers the rule engine, keyed by whitelisted field name. Every value is
/// checked against the catalog when it is set — a zone area has to arrive in m2, a fire rating in
/// minutes — so unit conversion happens before the engine, never inside it (spec 11.2).
/// </summary>
/// <remarks>
/// A field that is not set is missing, which is not the same as false or zero: the engine turns it
/// into InsufficientData, never into a fail (spec 11.3).
/// </remarks>
public sealed class RuleFacts
{
    private readonly Dictionary<string, ReviewValue> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unreadable = new(StringComparer.Ordinal);

    public RuleFacts(RuleFieldCatalog catalog)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public RuleFieldCatalog Catalog { get; }

    public RuleFacts Set(string field, ReviewValue value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        var definition = Resolve(field);
        if (!definition.Type.Accepts(value))
            throw new ArgumentException(
                $"Field '{definition.Name}' is {definition.Type} but was given {RuleValueType.Of(value)} ({value}).", nameof(value));

        _unreadable.Remove(definition.Name);
        _values[definition.Name] = value;
        return this;
    }

    public RuleFacts Set(string field, bool flag) => Set(field, ReviewValue.OfBoolean(flag));
    public RuleFacts Set(string field, string text) => Set(field, ReviewValue.OfText(text));
    public RuleFacts Set(string field, double number, ReviewUnit unit) => Set(field, ReviewValue.Quantity(number, unit));

    /// <summary>Records that the field was read but its content could not be understood.</summary>
    public RuleFacts MarkUnreadable(string field, string reason)
    {
        var definition = Resolve(field);
        _values.Remove(definition.Name);
        _unreadable[definition.Name] = reason?.Trim() ?? string.Empty;
        return this;
    }

    public ReviewValue? Find(string field) =>
        field is not null && _values.TryGetValue(field.Trim(), out var value) ? value : null;

    /// <summary>Why the field has no value, or null when it has one.</summary>
    public RuleFactGap? GapFor(string field)
    {
        if (field is null) throw new ArgumentNullException(nameof(field));
        var name = field.Trim();
        if (_values.ContainsKey(name)) return null;
        return _unreadable.TryGetValue(name, out var reason)
            ? new RuleFactGap(name, RuleFactGapKind.Unreadable, reason)
            : new RuleFactGap(name, RuleFactGapKind.Missing);
    }

    public IEnumerable<string> KnownFields => _values.Keys.OrderBy(x => x, StringComparer.Ordinal);

    private RuleFieldDefinition Resolve(string field)
    {
        if (string.IsNullOrWhiteSpace(field)) throw new ArgumentException("Field is required.", nameof(field));
        return Catalog.Find(field)
            ?? throw new ArgumentException($"Field '{field.Trim()}' is not on the rule field whitelist.", nameof(field));
    }
}
