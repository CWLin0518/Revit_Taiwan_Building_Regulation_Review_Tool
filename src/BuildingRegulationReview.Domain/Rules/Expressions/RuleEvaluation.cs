using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Domain.Rules.Expressions;

/// <summary>
/// What evaluating one expression against one subject produced: a value, "unknown" because fields
/// were missing, or a computation failure such as division by zero. Unknown is a first-class
/// outcome so missing data can travel up to InsufficientData instead of defaulting to false.
/// </summary>
public sealed class RuleEvaluation
{
    private static readonly IReadOnlyList<RuleFactGap> NoGaps = new ReadOnlyCollection<RuleFactGap>(new RuleFactGap[0]);

    private RuleEvaluation(ReviewValue? value, IReadOnlyList<RuleFactGap> gaps, string? failure)
    {
        Value = value;
        Gaps = gaps;
        Failure = failure;
    }

    public ReviewValue? Value { get; }

    /// <summary>The fields that kept the value unknown; empty unless <see cref="IsUnknown"/>.</summary>
    public IReadOnlyList<RuleFactGap> Gaps { get; }

    /// <summary>Why the computation failed; null unless <see cref="IsFailed"/>.</summary>
    public string? Failure { get; }

    public bool IsKnown => Value is not null;
    public bool IsUnknown => Value is null && Failure is null;
    public bool IsFailed => Failure is not null;

    public bool IsTrue => Value is { Kind: ReviewValueKind.Boolean, Flag: true };
    public bool IsFalse => Value is { Kind: ReviewValueKind.Boolean, Flag: false };

    public static RuleEvaluation Known(ReviewValue value) =>
        new(value ?? throw new ArgumentNullException(nameof(value)), NoGaps, null);

    public static RuleEvaluation Unknown(IEnumerable<RuleFactGap> gaps)
    {
        var list = (gaps ?? throw new ArgumentNullException(nameof(gaps))).Distinct().ToList();
        if (list.Count == 0) throw new ArgumentException("An unknown value must say which fields were missing.", nameof(gaps));
        return new RuleEvaluation(null, new ReadOnlyCollection<RuleFactGap>(list), null);
    }

    public static RuleEvaluation Failed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A failure must say why.", nameof(reason));
        return new RuleEvaluation(null, NoGaps, reason.Trim());
    }

    /// <summary>
    /// Combines operands that must all be known: a failure wins, then unknown with every gap, else null
    /// (meaning the caller may compute).
    /// </summary>
    internal static RuleEvaluation? Blocking(params RuleEvaluation[] operands)
    {
        var failed = operands.FirstOrDefault(x => x.IsFailed);
        if (failed is not null) return failed;

        var gaps = operands.Where(x => x.IsUnknown).SelectMany(x => x.Gaps).ToList();
        return gaps.Count > 0 ? Unknown(gaps) : null;
    }
}
