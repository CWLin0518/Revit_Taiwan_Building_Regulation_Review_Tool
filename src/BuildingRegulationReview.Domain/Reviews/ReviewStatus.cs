using System;

namespace BuildingRegulationReview.Domain.Reviews;

/// <summary>
/// The six outcomes of one check (spec 11.3). <see cref="InsufficientData"/> is its own state so
/// that missing data can never be read as <see cref="Fail"/> — or as <see cref="Pass"/>
/// (spec 18 item 5).
/// </summary>
public enum ReviewStatus
{
    NotRun,
    Pass,
    Fail,
    InsufficientData,
    NotApplicable,
    ManualReview
}

public static class ReviewStatusText
{
    /// <summary>The label the review table and the drawings show (spec 11.3 wording).</summary>
    public static string Label(ReviewStatus status) => status switch
    {
        ReviewStatus.NotRun => "未檢討",
        ReviewStatus.Pass => "符合",
        ReviewStatus.Fail => "未符合",
        ReviewStatus.InsufficientData => "資料不足",
        ReviewStatus.NotApplicable => "不適用",
        ReviewStatus.ManualReview => "人工覆核",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    /// <summary>
    /// Whether a result in this state carries a verdict that was measured: only a pass or a fail
    /// compares an actual value with a required one, so only those two must hold both.
    /// </summary>
    public static bool IsComparison(ReviewStatus status) =>
        status == ReviewStatus.Pass || status == ReviewStatus.Fail;
}
