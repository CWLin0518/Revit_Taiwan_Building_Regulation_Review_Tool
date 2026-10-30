using System;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Domain.Rules.Expressions;

/// <summary>
/// The static type of a field or an expression: true/false, text, or a number in one explicit unit.
/// Every expression is typed when the rule set is compiled, so a unit mistake stops the rule from
/// loading instead of producing a wrong comparison at review time (spec 11.2).
/// </summary>
public readonly struct RuleValueType : IEquatable<RuleValueType>
{
    private RuleValueType(ReviewValueKind kind, ReviewUnit unit)
    {
        Kind = kind;
        Unit = unit;
    }

    public static RuleValueType Boolean => new(ReviewValueKind.Boolean, ReviewUnit.None);
    public static RuleValueType Text => new(ReviewValueKind.Text, ReviewUnit.None);

    public static RuleValueType Quantity(ReviewUnit unit)
    {
        if (!Enum.IsDefined(typeof(ReviewUnit), unit)) throw new ArgumentOutOfRangeException(nameof(unit));
        return new RuleValueType(ReviewValueKind.Quantity, unit);
    }

    public ReviewValueKind Kind { get; }

    /// <summary>The unit of a quantity; <see cref="ReviewUnit.None"/> for a plain number and for the other kinds.</summary>
    public ReviewUnit Unit { get; }

    public bool IsBoolean => Kind == ReviewValueKind.Boolean;
    public bool IsText => Kind == ReviewValueKind.Text;
    public bool IsQuantity => Kind == ReviewValueKind.Quantity;

    public static RuleValueType Of(ReviewValue value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        return new RuleValueType(value.Kind, value.Kind == ReviewValueKind.Quantity ? value.Unit : ReviewUnit.None);
    }

    public bool Accepts(ReviewValue value) => value is not null && Equals(Of(value));

    public bool Equals(RuleValueType other) => Kind == other.Kind && Unit == other.Unit;
    public override bool Equals(object? obj) => obj is RuleValueType other && Equals(other);
    public override int GetHashCode() => ((int)Kind * 31) + (int)Unit;
    public static bool operator ==(RuleValueType left, RuleValueType right) => left.Equals(right);
    public static bool operator !=(RuleValueType left, RuleValueType right) => !left.Equals(right);

    /// <summary>How the type reads in an error message.</summary>
    public override string ToString() => Kind switch
    {
        ReviewValueKind.Boolean => "是非值",
        ReviewValueKind.Text => "文字",
        _ => Unit == ReviewUnit.None ? "純數值" : $"數值（{RuleUnits.Symbol(Unit)}）"
    };
}
