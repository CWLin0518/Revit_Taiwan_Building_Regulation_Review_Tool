using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Domain.Common;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// Spec 11.1「來源元素與區劃空間關係可解析」, shared by every check: without zones, or with a zone
/// that has no enclosed Area at all, nothing can be reviewed — 任一關鍵條件不成立時停止檢討並列出修正方式.
/// </summary>
internal static class ReviewPreconditions
{
    /// <param name="subject">What the check reviews, as the message names it, e.g. 面積.</param>
    public static Result Zones(CandidateSet set, string subject)
    {
        if (set.Zones.Count == 0)
            return Result.Failure(new Error(ReviewErrorCode.CandidateZoneUnusable,
                $"此工作包沒有任何區劃，無法檢討{subject}；請先在「建立區劃範圍」套用區劃。"));

        var unmeasurable = set.Zones.Where(z => !z.IsMeasurable).ToList();
        if (unmeasurable.Count > 0)
            return Result.Failure(new Error(ReviewErrorCode.CandidateZoneUnusable,
                $"區劃 {string.Join("、", unmeasurable.Select(z => $"「{z.Name}」"))} 沒有任何封閉的面積，無法檢討；" +
                "請回到「建立區劃範圍」修正邊界後重新套用。",
                string.Join(", ", unmeasurable.Select(z => z.ZoneIdText))));

        return Result.Success();
    }
}
