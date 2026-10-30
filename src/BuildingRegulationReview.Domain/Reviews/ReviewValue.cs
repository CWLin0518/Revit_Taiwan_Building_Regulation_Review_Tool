using System;
using System.Globalization;

namespace BuildingRegulationReview.Domain.Reviews;

public enum ReviewValueKind
{
    Quantity,
    Text,
    Boolean
}

/// <summary>
/// The unit a quantity is expressed in. Spec 11.2 wants every unit converted before a value enters
/// the domain, so a quantity always says what it measures instead of borrowing the project's units.
/// </summary>
public enum ReviewUnit
{
    None,
    SquareMeter,
    Meter,
    Minute,
    Count
}

/// <summary>
/// An actual value, a required value or one evidence entry of a result (spec 11.3 "object").
/// Deliberately small: a number with an explicit unit, a text such as <c>是</c>/<c>否</c>, or a flag.
/// </summary>
public sealed class ReviewValue : IEquatable<ReviewValue>
{
    private ReviewValue(ReviewValueKind kind, double number, ReviewUnit unit, string text, bool flag)
    {
        Kind = kind;
        Number = number;
        Unit = unit;
        Text = text;
        Flag = flag;
    }

    public ReviewValueKind Kind { get; }

    /// <summary>The magnitude of a <see cref="ReviewValueKind.Quantity"/>; zero otherwise.</summary>
    public double Number { get; }

    public ReviewUnit Unit { get; }

    /// <summary>The text of a <see cref="ReviewValueKind.Text"/>; empty otherwise.</summary>
    public string Text { get; }

    /// <summary>The value of a <see cref="ReviewValueKind.Boolean"/>; false otherwise.</summary>
    public bool Flag { get; }

    public static ReviewValue Quantity(double number, ReviewUnit unit)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
            throw new ArgumentOutOfRangeException(nameof(number), "A quantity must be a finite number.");
        if (!Enum.IsDefined(typeof(ReviewUnit), unit))
            throw new ArgumentOutOfRangeException(nameof(unit));

        return new ReviewValue(ReviewValueKind.Quantity, number, unit, string.Empty, false);
    }

    public static ReviewValue OfText(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        return new ReviewValue(ReviewValueKind.Text, 0, ReviewUnit.None, text.Trim(), false);
    }

    public static ReviewValue OfBoolean(bool flag) =>
        new ReviewValue(ReviewValueKind.Boolean, 0, ReviewUnit.None, string.Empty, flag);

    public bool Equals(ReviewValue? other) =>
        other is not null &&
        Kind == other.Kind &&
        Number.Equals(other.Number) &&
        Unit == other.Unit &&
        string.Equals(Text, other.Text, StringComparison.Ordinal) &&
        Flag == other.Flag;

    public override bool Equals(object? obj) => Equals(obj as ReviewValue);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + (int)Kind;
            hash = (hash * 31) + Number.GetHashCode();
            hash = (hash * 31) + (int)Unit;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Text);
            hash = (hash * 31) + (Flag ? 1 : 0);
            return hash;
        }
    }

    public override string ToString() => Kind switch
    {
        ReviewValueKind.Quantity => Unit == ReviewUnit.None
            ? Number.ToString("R", CultureInfo.InvariantCulture)
            : $"{Number.ToString("R", CultureInfo.InvariantCulture)} {Unit}",
        ReviewValueKind.Text => Text,
        _ => Flag ? "true" : "false"
    };
}
