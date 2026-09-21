using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Application.ReviewPackages;

/// <summary>
/// What the adapter found when it looked at the model a package points at. Plain observations, no
/// judgement: <see cref="ReviewStaleness"/> decides what they mean.
/// </summary>
/// <remarks>
/// There is deliberately no stored fingerprint to compare against. Every baseline spec 13.1 needs is
/// already in the model: the views are found by the UniqueIds on the package, and the boundaries and
/// Areas each carry the signature that was written on them, which is itself derived from the
/// geometry tolerance — so a tolerance change shows up as a changed signature rather than needing a
/// field of its own. Adding a field would have meant a new Revit Extensible Storage schema and a
/// migration for everybody's existing packages, to learn nothing the model does not already say.
/// </remarks>
public sealed class ReviewModelObservation
{
    public ReviewModelObservation(
        bool sourceFloorPlanFound,
        bool levelFound,
        bool areaSchemeFound,
        bool areaPlanFound,
        bool? draftingViewFound = null,
        string? areaPlanLevelUniqueId = null,
        string? areaPlanAreaSchemeUniqueId = null,
        int managedElementCount = 0,
        int changedManagedElementCount = 0,
        int unreadableManagedElementCount = 0,
        string? ruleSetVersion = null,
        DateTime? observedAtUtc = null)
    {
        if (managedElementCount < 0) throw new ArgumentOutOfRangeException(nameof(managedElementCount));
        if (changedManagedElementCount < 0) throw new ArgumentOutOfRangeException(nameof(changedManagedElementCount));
        if (unreadableManagedElementCount < 0) throw new ArgumentOutOfRangeException(nameof(unreadableManagedElementCount));

        SourceFloorPlanFound = sourceFloorPlanFound;
        LevelFound = levelFound;
        AreaSchemeFound = areaSchemeFound;
        AreaPlanFound = areaPlanFound;
        DraftingViewFound = draftingViewFound;
        AreaPlanLevelUniqueId = Clean(areaPlanLevelUniqueId);
        AreaPlanAreaSchemeUniqueId = Clean(areaPlanAreaSchemeUniqueId);
        ManagedElementCount = managedElementCount;
        ChangedManagedElementCount = changedManagedElementCount;
        UnreadableManagedElementCount = unreadableManagedElementCount;
        RuleSetVersion = Clean(ruleSetVersion);
        ObservedAtUtc = (observedAtUtc ?? DateTime.UtcNow).ToUniversalTime();
    }

    public bool SourceFloorPlanFound { get; }
    public bool LevelFound { get; }
    public bool AreaSchemeFound { get; }
    public bool AreaPlanFound { get; }

    /// <summary>Null when the package never recorded a Drafting View, so there is none to miss.</summary>
    public bool? DraftingViewFound { get; }

    /// <summary>The Level the Area Plan is on now; null when the plan itself is gone.</summary>
    public string? AreaPlanLevelUniqueId { get; }

    /// <summary>The Area Scheme the Area Plan uses now; null when the plan itself is gone.</summary>
    public string? AreaPlanAreaSchemeUniqueId { get; }

    /// <summary>How many elements carrying this package's mark are still in the model.</summary>
    public int ManagedElementCount { get; }

    /// <summary>How many of those no longer match the signature that was written on them.</summary>
    public int ChangedManagedElementCount { get; }

    /// <summary>How many carry a mark that no longer parses, which makes them untouchable.</summary>
    public int UnreadableManagedElementCount { get; }

    /// <summary>The rule set the project offers now; null when nothing has said (spec 13.1 規則版本).</summary>
    public string? RuleSetVersion { get; }

    public DateTime ObservedAtUtc { get; }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}

/// <summary>Whether a package's results still describe the model, and why not (spec 13.1).</summary>
public sealed class StalenessVerdict
{
    internal StalenessVerdict(
        ReviewPackage package,
        ReviewPackageStatus previousStatus,
        IEnumerable<string> reasons,
        string message)
    {
        Package = package;
        PreviousStatus = previousStatus;
        Reasons = new ReadOnlyCollection<string>(reasons.ToList());
        Message = message;
    }

    /// <summary>The package as it should now be stored, already carrying <see cref="Status"/>.</summary>
    public ReviewPackage Package { get; }

    /// <summary>What the package's status becomes; the same as before when nothing invalidated it.</summary>
    public ReviewPackageStatus Status => Package.Status;

    /// <summary>The status the package had when it was observed.</summary>
    public ReviewPackageStatus PreviousStatus { get; }

    /// <summary>Every reason found, whether or not the status moved.</summary>
    public IReadOnlyList<string> Reasons { get; }

    public bool IsStale => Status == ReviewPackageStatus.Stale;

    public bool IsError => Status == ReviewPackageStatus.Error;

    /// <summary>True when the caller has something new to save.</summary>
    public bool Changed => Status != PreviousStatus;

    public string Message { get; }

    public override string ToString() => Message;
}

/// <summary>
/// Decides whether what a package wrote still describes the model (spec 13.1: 來源視圖／Level、
/// Area Boundary、Area、參與檢討的元素、參數、Area Scheme、規則版本 或幾何容差 的變更使相關結果
/// 成為 Stale).
/// </summary>
public static class ReviewStaleness
{
    /// <summary>
    /// Judges one package against what the adapter observed.
    /// </summary>
    /// <remarks>
    /// A package that has not produced a result yet cannot have a stale one: Setup and
    /// 區劃草稿 already say the work is unfinished, and marking them Stale would replace that with a
    /// vaguer statement. The reasons are still reported, because the Editor shows them either way.
    /// A missing Area Plan is the one observation that is an error rather than staleness — there is
    /// nothing left to write to, so re-running would not help.
    /// </remarks>
    public static StalenessVerdict Evaluate(
        ReviewPackage package,
        ReviewModelObservation observation,
        DateTime? nowUtc = null)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        if (observation is null) throw new ArgumentNullException(nameof(observation));

        var reasons = Reasons(package, observation).ToList();
        var previous = package.Status;

        if (!observation.AreaPlanFound)
        {
            return Verdict(package, previous, ReviewPackageStatus.Error, reasons,
                "這個套件的 Area Plan 已不在模型中，無法再寫入，請重新執行「防火區劃設定」。", nowUtc);
        }

        if (reasons.Count == 0)
        {
            return Verdict(package, previous, previous, reasons,
                "模型與這個套件寫入的結果一致，狀態維持「" + ReviewPackageProgress.Describe(previous) + "」。",
                nowUtc);
        }

        if (!HasPublishedResult(previous))
        {
            return Verdict(package, previous, previous, reasons,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "模型有 {0} 項變更，但這個套件還在「{1}」，尚未有可失效的結果。",
                    reasons.Count,
                    ReviewPackageProgress.Describe(previous)),
                nowUtc);
        }

        return Verdict(package, previous, ReviewPackageStatus.Stale, reasons,
            string.Format(
                CultureInfo.InvariantCulture,
                "模型有 {0} 項變更，這個套件的結果已失效，請重新開啟編輯器並重新套用。",
                reasons.Count),
            nowUtc);
    }

    /// <summary>The log entries the verdict is worth (spec 14).</summary>
    public static ReviewLog Explain(StalenessVerdict verdict, DateTime? timestampUtc = null)
    {
        if (verdict is null) throw new ArgumentNullException(nameof(verdict));

        var log = new ReviewLog.Builder(verdict.Package.PackageId, timestampUtc);

        foreach (var reason in verdict.Reasons)
        {
            log.Add(
                ReviewErrorCode.StatusStale,
                ReviewStage.Status,
                verdict.IsError ? ReviewSeverity.Error : ReviewSeverity.Warning,
                reason,
                suggestion: "請重新開啟防火區劃編輯器，確認區劃後重新套用。");
        }

        log.Add(
            verdict.IsError ? ReviewErrorCode.StatusBlocked : ReviewErrorCode.StatusStale,
            ReviewStage.Status,
            verdict.IsError ? ReviewSeverity.Error : verdict.IsStale ? ReviewSeverity.Warning : ReviewSeverity.Info,
            verdict.Message,
            technicalDetail: string.Format(
                CultureInfo.InvariantCulture,
                "Status {0} -> {1}",
                verdict.PreviousStatus,
                verdict.Status));

        return log.Build();
    }

    /// <summary>Statuses that stand for a published result, which is the only thing that can go stale.</summary>
    private static bool HasPublishedResult(ReviewPackageStatus status) =>
        status == ReviewPackageStatus.Ready ||
        status == ReviewPackageStatus.Reviewed ||
        status == ReviewPackageStatus.Documented;

    private static IEnumerable<string> Reasons(ReviewPackage package, ReviewModelObservation observation)
    {
        if (!observation.AreaPlanFound) yield return "這個套件的 Area Plan 已不在模型中。";
        if (!observation.SourceFloorPlanFound) yield return "來源樓層平面已不在模型中。";
        if (!observation.LevelFound) yield return "這個套件的樓層已不在模型中。";
        if (!observation.AreaSchemeFound) yield return "這個套件的 Area Scheme 已不在模型中。";

        if (observation.AreaPlanFound &&
            observation.AreaPlanLevelUniqueId is not null &&
            !string.Equals(observation.AreaPlanLevelUniqueId, package.LevelUniqueId, StringComparison.Ordinal))
        {
            yield return "Area Plan 現在對應的樓層與這個套件記錄的不同。";
        }

        if (observation.AreaPlanFound &&
            observation.AreaPlanAreaSchemeUniqueId is not null &&
            !string.Equals(observation.AreaPlanAreaSchemeUniqueId, package.AreaSchemeUniqueId, StringComparison.Ordinal))
        {
            yield return "Area Plan 現在使用的 Area Scheme 與這個套件記錄的不同。";
        }

        if (observation.DraftingViewFound == false) yield return "這個套件的單線圖視圖已不在模型中。";

        if (observation.ChangedManagedElementCount > 0)
        {
            // Signatures are quantized with GeometryTolerance.ClosureFeet, so this one reason covers
            // both halves of spec 13.1: a boundary somebody dragged, and a tolerance somebody changed.
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "有 {0} 個面積邊界或面積已被修改，與這個套件寫入時不同。",
                observation.ChangedManagedElementCount);
        }

        if (observation.UnreadableManagedElementCount > 0)
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "有 {0} 個工具產生的元素已讀不到擁有權標記，不會再被更新或刪除。",
                observation.UnreadableManagedElementCount);
        }

        if (package.BoundaryRevision > 0 && observation.ManagedElementCount == 0)
        {
            yield return "這個套件寫入的面積邊界與面積都已不在模型中。";
        }

        if (observation.RuleSetVersion is not null &&
            package.RuleSetVersion is not null &&
            !string.Equals(observation.RuleSetVersion, package.RuleSetVersion, StringComparison.Ordinal))
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "規則版本已從 {0} 更新為 {1}。",
                package.RuleSetVersion,
                observation.RuleSetVersion);
        }
    }

    private static StalenessVerdict Verdict(
        ReviewPackage package,
        ReviewPackageStatus previous,
        ReviewPackageStatus next,
        IReadOnlyList<string> reasons,
        string message,
        DateTime? nowUtc)
    {
        var updated = next == previous ? package : package.WithProgress(next, updatedAtUtc: nowUtc);
        return new StalenessVerdict(updated, previous, reasons, message);
    }
}
