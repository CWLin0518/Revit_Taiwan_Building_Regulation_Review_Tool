using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// How a group of results (a Type, a kind of opening) is summed up: Fail when any fails, otherwise
/// the most doubtful state present (人工覆核, then 資料不足), and Pass only when nothing is doubtful.
/// </summary>
internal static class ReviewStatusSeverity
{
    /// <summary>Worst first.</summary>
    private static readonly ReviewStatus[] Order =
    {
        ReviewStatus.Fail, ReviewStatus.ManualReview, ReviewStatus.InsufficientData,
        ReviewStatus.Pass, ReviewStatus.NotApplicable, ReviewStatus.NotRun
    };

    /// <summary>The worst status present; NotRun when there is none.</summary>
    public static ReviewStatus Worst(IEnumerable<ReviewStatus> statuses)
    {
        var present = new HashSet<ReviewStatus>(statuses);
        return Order.First(s => s == ReviewStatus.NotRun || present.Contains(s));
    }
}
