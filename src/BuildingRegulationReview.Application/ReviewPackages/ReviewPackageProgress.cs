using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Application.ReviewPackages;

/// <summary>What one write-back did to the package's own state.</summary>
public sealed class ReviewPackageProgressOutcome
{
    internal ReviewPackageProgressOutcome(
        ReviewPackage package,
        ReviewPackageStatus previousStatus,
        int previousBoundaryRevision,
        IEnumerable<string> blockers,
        string message)
    {
        Package = package;
        PreviousStatus = previousStatus;
        PreviousBoundaryRevision = previousBoundaryRevision;
        Blockers = new ReadOnlyCollection<string>(blockers.ToList());
        Message = message;
    }

    /// <summary>The package as it should now be stored. Identical to the input when nothing moved.</summary>
    public ReviewPackage Package { get; }

    public ReviewPackageStatus PreviousStatus { get; }
    public int PreviousBoundaryRevision { get; }

    public ReviewPackageStatus Status => Package.Status;
    public int BoundaryRevision => Package.BoundaryRevision;

    /// <summary>True when the caller has something new to save.</summary>
    public bool Changed => Status != PreviousStatus || BoundaryRevision != PreviousBoundaryRevision;

    /// <summary>
    /// Why the package is not Ready, in the user's words. Empty when nothing stood in the way —
    /// which is not the same as being Ready, because a run that wrote nothing learns nothing.
    /// </summary>
    public IReadOnlyList<string> Blockers { get; }

    public bool IsReady => Status == ReviewPackageStatus.Ready;

    /// <summary>One line for the status bar.</summary>
    public string Message { get; }

    public override string ToString() => Message;
}

/// <summary>
/// The one place that moves a <see cref="ReviewPackage"/> forward after a write-back (spec 13), and
/// the one that enforces spec 10.6's rule that an Area disagreeing with the draft by more than the
/// tolerance is barred from Ready.
/// </summary>
/// <remarks>
/// Until P2-T09 nothing changed a package's status at all: write-back reported the area
/// disagreement to the log and the package stayed in whatever state setup had left it in. The rule
/// lives here rather than in the Revit adapter because it is about what a run means, not about how
/// Revit was driven, and because a refusal to advance has to be assertable with no document open.
/// </remarks>
public static class ReviewPackageProgress
{
    /// <summary>
    /// Works out the package's new state from what the run reported.
    /// </summary>
    /// <remarks>
    /// Three cases, and the middle one is the one that is easy to get wrong. A rollback changed
    /// nothing, so the package is left exactly as it was. A run that wrote nothing and measured
    /// nothing learnt nothing, so it also leaves the package alone rather than promoting one whose
    /// boundaries nobody checked this time. Otherwise there is a verdict, and it is Ready only when
    /// every Area that was measured agrees with the draft and every element the plan listed reached
    /// the model.
    /// <para>
    /// A verdict does not need the model to have been touched — only measured. An already-correct
    /// model has nothing to write, and demanding a write before advancing made that the one state a
    /// package could never leave: being right was what left nothing to write. The write-back
    /// therefore measures the Areas it left alone as well as the ones it placed, so such a run
    /// arrives here with real evidence and this method can judge it on that.
    /// </para>
    /// </remarks>
    public static ReviewPackageProgressOutcome After(
        ReviewPackage package,
        ApplyResult result,
        DateTime? nowUtc = null)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (result.PackageId != package.PackageId)
            throw new ArgumentException("This result belongs to another package.", nameof(result));

        var status = package.Status;
        var revision = package.BoundaryRevision;

        if (result.IsRolledBack)
        {
            return new ReviewPackageProgressOutcome(
                package, status, revision,
                new[] { result.FatalError! },
                "寫回已全部復原，套件狀態維持「" + Describe(status) + "」。");
        }

        var touched = result.CreatedCount + result.UpdatedCount + result.DeletedCount;
        if (touched == 0 && result.AreaFindings.Count == 0 && result.FailedCount == 0)
        {
            return new ReviewPackageProgressOutcome(
                package, status, revision,
                Array.Empty<string>(),
                "模型已經與草稿一致，套件狀態維持「" + Describe(status) + "」。");
        }

        var blockers = Blockers(result).ToList();
        var next = blockers.Count == 0 ? ReviewPackageStatus.Ready : ReviewPackageStatus.BoundaryDraft;

        // A run that moved nothing may confirm a package that is still waiting for its boundaries,
        // but it may not re-label one that is already past that point. Nothing was invalidated, so
        // 已檢討／已出圖 would be thrown away for no reason; and 已失效 is ReviewStaleness's verdict,
        // given for things — a missing 單線圖 view, a swapped Area Scheme, a new rule version — that
        // measuring an area says nothing about. Blockers are the exception: a boundary that really
        // does disagree is worth dropping back for, whether or not this run wrote anything.
        if (touched == 0 && blockers.Count == 0 && !AwaitingBoundaries(status)) next = status;

        // A boundary that changed invalidates whatever was reviewed or documented on top of it
        // (spec 13.1), so the package drops back to what this run itself can vouch for.
        var advanced = touched > 0
            ? package.WithNextBoundaryRevision(next, nowUtc)
            : package.WithProgress(next, updatedAtUtc: nowUtc);

        return new ReviewPackageProgressOutcome(
            package: advanced,
            previousStatus: status,
            previousBoundaryRevision: revision,
            blockers: blockers,
            message: Message(next, blockers.Count, wrote: touched > 0));
    }

    /// <summary>The log entries the state change is worth (spec 14).</summary>
    public static ReviewLog Explain(
        ReviewPackageProgressOutcome outcome,
        DateTime? timestampUtc = null)
    {
        if (outcome is null) throw new ArgumentNullException(nameof(outcome));

        var log = new ReviewLog.Builder(outcome.Package.PackageId, timestampUtc);

        foreach (var blocker in outcome.Blockers)
        {
            log.Add(
                ReviewErrorCode.StatusBlocked,
                ReviewStage.Status,
                ReviewSeverity.Error,
                blocker,
                suggestion: "修正後重新套用；面積相符且所有元素都寫入模型時，套件才會進入 Ready。");
        }

        log.Add(
            outcome.Blockers.Count == 0 ? ReviewErrorCode.StatusAdvanced : ReviewErrorCode.StatusBlocked,
            ReviewStage.Status,
            outcome.Blockers.Count == 0 ? ReviewSeverity.Info : ReviewSeverity.Warning,
            outcome.Message,
            technicalDetail: string.Format(
                CultureInfo.InvariantCulture,
                "Status {0} -> {1}, BoundaryRevision {2} -> {3}",
                outcome.PreviousStatus,
                outcome.Status,
                outcome.PreviousBoundaryRevision,
                outcome.BoundaryRevision));

        return log.Build();
    }

    /// <summary>The Chinese name of a status, used in every message about one.</summary>
    public static string Describe(ReviewPackageStatus status) => status switch
    {
        ReviewPackageStatus.Setup => "設定中",
        ReviewPackageStatus.BoundaryDraft => "區劃草稿",
        ReviewPackageStatus.Ready => "可開始檢討",
        ReviewPackageStatus.Reviewed => "已檢討",
        ReviewPackageStatus.Documented => "已出圖",
        ReviewPackageStatus.Stale => "已失效",
        ReviewPackageStatus.Error => "錯誤",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    /// <summary>
    /// The states whose whole remaining question is whether the boundaries are written and sound —
    /// the only ones a run that measured without writing may move.
    /// </summary>
    private static bool AwaitingBoundaries(ReviewPackageStatus status) =>
        status == ReviewPackageStatus.Setup || status == ReviewPackageStatus.BoundaryDraft;

    private static IEnumerable<string> Blockers(ApplyResult result)
    {
        // Spec 10.6: this is the refusal that clause asks for, and it comes first because it points
        // at a boundary problem rather than at a Revit refusal.
        foreach (var finding in result.AreaDisagreements) yield return finding.Message!;

        if (result.FailedCount > 0)
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "有 {0} 個元素沒有寫入模型，草稿與模型還不一致。",
                result.FailedCount);
        }
    }

    // A verify-only run must not claim to have written anything: it measured what was already there.
    private static string Message(ReviewPackageStatus status, int blockerCount, bool wrote) => blockerCount == 0
        ? (wrote
            ? "面積與邊界都已寫入且互相符合，套件進入「" + Describe(status) + "」。"
            : "模型未變更；各區劃面積與草算相符，套件狀態為「" + Describe(status) + "」。")
        : string.Format(
            CultureInfo.InvariantCulture,
            "有 {0} 項問題未解決，套件維持在「{1}」，尚不得進入「{2}」。",
            blockerCount,
            Describe(ReviewPackageStatus.BoundaryDraft),
            Describe(ReviewPackageStatus.Ready));
}
