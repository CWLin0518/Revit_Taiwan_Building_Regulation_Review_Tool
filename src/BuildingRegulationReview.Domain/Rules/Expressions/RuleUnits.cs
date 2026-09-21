using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Domain.Rules.Expressions;

/// <summary>
/// The units a rule may write after a number, and how unit arithmetic works. Every literal is
/// converted to one canonical unit per dimension as it is parsed (mm → m, h → min), so the engine
/// only ever compares like with like (spec 11.2 "所有單位進入 Domain 層前轉成明確單位值").
/// </summary>
public static class RuleUnits
{
    private static readonly IReadOnlyDictionary<string, (ReviewUnit Unit, double Factor)> Suffixes =
        new Dictionary<string, (ReviewUnit, double)>(StringComparer.Ordinal)
        {
            ["m2"] = (ReviewUnit.SquareMeter, 1),
            ["㎡"] = (ReviewUnit.SquareMeter, 1),
            ["m"] = (ReviewUnit.Meter, 1),
            ["cm"] = (ReviewUnit.Meter, 0.01),
            ["mm"] = (ReviewUnit.Meter, 0.001),
            ["min"] = (ReviewUnit.Minute, 1),
            ["h"] = (ReviewUnit.Minute, 60),
            ["hr"] = (ReviewUnit.Minute, 60)
        };

    /// <summary>The suffixes a number literal may carry, e.g. <c>1500 m2</c>, <c>2 h</c>.</summary>
    public static IEnumerable<string> LiteralSuffixes => Suffixes.Keys;

    public static bool TryGetSuffix(string text, out ReviewUnit unit, out double factor)
    {
        if (text is not null && Suffixes.TryGetValue(text, out var entry))
        {
            unit = entry.Unit;
            factor = entry.Factor;
            return true;
        }

        unit = ReviewUnit.None;
        factor = 1;
        return false;
    }

    public static string Symbol(ReviewUnit unit) => unit switch
    {
        ReviewUnit.None => string.Empty,
        ReviewUnit.SquareMeter => "m2",
        ReviewUnit.Meter => "m",
        ReviewUnit.Minute => "min",
        ReviewUnit.Count => "count",
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    /// <summary>The unit of <c>left * right</c>, or null when the product has no unit the engine knows.</summary>
    public static ReviewUnit? Multiply(ReviewUnit left, ReviewUnit right)
    {
        if (left == ReviewUnit.None) return right;
        if (right == ReviewUnit.None) return left;
        if (left == ReviewUnit.Meter && right == ReviewUnit.Meter) return ReviewUnit.SquareMeter;
        return null;
    }

    /// <summary>The unit of <c>left / right</c>, or null when the quotient has no unit the engine knows.</summary>
    public static ReviewUnit? Divide(ReviewUnit left, ReviewUnit right)
    {
        if (right == ReviewUnit.None) return left;
        if (left == right) return ReviewUnit.None;
        if (left == ReviewUnit.SquareMeter && right == ReviewUnit.Meter) return ReviewUnit.Meter;
        return null;
    }

    /// <summary>
    /// Whether two magnitudes are the same number. Unit conversion and Revit's own rounding leave
    /// noise far below any regulated value, so a relative tolerance keeps "exactly at the limit" a
    /// pass instead of a coin toss.
    /// </summary>
    public static bool AreEqual(double left, double right) =>
        Math.Abs(left - right) <= 1e-9 * new[] { 1.0, Math.Abs(left), Math.Abs(right) }.Max();

    public static string Format(ReviewValue value) => value.Kind switch
    {
        ReviewValueKind.Quantity => value.Unit == ReviewUnit.None
            ? FormatNumber(value.Number)
            : $"{FormatNumber(value.Number)} {Symbol(value.Unit)}",
        ReviewValueKind.Text => $"「{value.Text}」",
        _ => value.Flag ? "true" : "false"
    };

    private static string FormatNumber(double number) =>
        number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
