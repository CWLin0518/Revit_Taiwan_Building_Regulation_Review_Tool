using System;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// One rule field a user, a project setting or a parameter supplied for a check, together with where
/// it came from. A field nobody supplied is simply absent — that is what makes it missing (spec 11.3:
/// 資料不足不可誤判為未符合); a field that was read but not understood is <see cref="Unreadable"/>.
/// </summary>
/// <remarks>
/// Where these inputs finally come from (區劃用途、灑水、構造 — spec 19 item 6) is still open, so the
/// check takes them as plain data and records the source text as evidence.
/// </remarks>
public sealed class ReviewInput
{
    private ReviewInput(string field, ReviewValue? value, string? unreadableReason, string? source)
    {
        if (string.IsNullOrWhiteSpace(field)) throw new ArgumentException("Field is required.", nameof(field));

        Field = field.Trim();
        Value = value;
        UnreadableReason = unreadableReason;
        Source = string.IsNullOrWhiteSpace(source) ? null : source!.Trim();
    }

    public static ReviewInput Known(string field, ReviewValue value, string? source = null) =>
        new(field, value ?? throw new ArgumentNullException(nameof(value)), null, source);

    public static ReviewInput Known(string field, bool flag, string? source = null) => Known(field, ReviewValue.OfBoolean(flag), source);
    public static ReviewInput Known(string field, string text, string? source = null) => Known(field, ReviewValue.OfText(text), source);
    public static ReviewInput Known(string field, double number, ReviewUnit unit, string? source = null) =>
        Known(field, ReviewValue.Quantity(number, unit), source);

    /// <summary>The field was read but its content could not be understood, e.g. 灑水 written as "部分".</summary>
    public static ReviewInput Unreadable(string field, string reason, string? source = null)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Say why the value could not be read.", nameof(reason));
        return new ReviewInput(field, null, reason.Trim(), source);
    }

    /// <summary>The whitelisted rule field this input fills.</summary>
    public string Field { get; }

    /// <summary>The value, or null when <see cref="IsUnreadable"/>.</summary>
    public ReviewValue? Value { get; }

    public string? UnreadableReason { get; }
    public bool IsUnreadable => Value is null;

    /// <summary>Where the value came from, e.g. "專案設定" or a parameter name; recorded as evidence.</summary>
    public string? Source { get; }

    /// <summary>Puts the input on the facts; the facts check the field is whitelisted and the value has the field's type and unit.</summary>
    public void ApplyTo(RuleFacts facts)
    {
        if (facts is null) throw new ArgumentNullException(nameof(facts));
        if (IsUnreadable) facts.MarkUnreadable(Field, UnreadableReason!);
        else facts.Set(Field, Value!);
    }

    public override string ToString() => IsUnreadable ? $"{Field} 格式無法判讀（{UnreadableReason}）" : $"{Field} = {Value}";
}
