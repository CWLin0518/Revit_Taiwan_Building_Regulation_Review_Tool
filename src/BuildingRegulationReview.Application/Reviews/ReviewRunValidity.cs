using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>Whether a stored run still describes the model and the rule set, and which of its results do not (spec 13.1).</summary>
public sealed class ReviewRunFreshness
{
    internal ReviewRunFreshness(
        ReviewRun run,
        bool invalidatesAll,
        IEnumerable<string> reasons,
        IEnumerable<Guid> staleResultIds,
        IEnumerable<string> addedSubjects,
        IEnumerable<string> removedSubjects,
        IEnumerable<string> changedSubjects)
    {
        Run = run;
        InvalidatesAll = invalidatesAll;
        Reasons = new ReadOnlyCollection<string>(reasons.ToList());
        StaleResultIds = new ReadOnlyCollection<Guid>(staleResultIds.ToList());
        AddedSubjects = new ReadOnlyCollection<string>(addedSubjects.ToList());
        RemovedSubjects = new ReadOnlyCollection<string>(removedSubjects.ToList());
        ChangedSubjects = new ReadOnlyCollection<string>(changedSubjects.ToList());
    }

    public ReviewRun Run { get; }

    /// <summary>Something every result shares moved (rule set, environment, run never finished, no evidence).</summary>
    public bool InvalidatesAll { get; }

    public IReadOnlyList<string> Reasons { get; }

    /// <summary>Results whose evidence moved, in run order. Every result when <see cref="InvalidatesAll"/>.</summary>
    public IReadOnlyList<Guid> StaleResultIds { get; }

    /// <summary>Subjects the model has now that the run never reviewed, e.g. a new wall on a boundary.</summary>
    public IReadOnlyList<string> AddedSubjects { get; }

    public IReadOnlyList<string> RemovedSubjects { get; }
    public IReadOnlyList<string> ChangedSubjects { get; }

    /// <summary>
    /// A run is stale as soon as anything moved — including a subject the run never saw, which has no
    /// result to go stale but still means the run no longer covers the model.
    /// </summary>
    public bool IsStale => Reasons.Count > 0;

    public bool IsResultStale(Guid resultId) => StaleResultIds.Contains(resultId);

    /// <summary>Active overrides this verdict takes away (spec 11.8「狀態改為需重新確認」).</summary>
    public IEnumerable<ReviewOverride> OverridesToReconfirm =>
        Run.Overrides.Where(x => x.IsActive && IsResultStale(x.ResultId));

    public string Message => IsStale
        ? string.Format(CultureInfo.InvariantCulture,
            "檢討結果已失效：{0}；{1} 項結果需要重新檢討，請重新執行「開始檢討」。",
            string.Join("；", Reasons), StaleResultIds.Count)
        : "模型與規則版本與此次檢討一致，結果仍有效。";
}

/// <summary>
/// Compares a stored run with the model and rule set as they are now (spec 13.1: 來源視圖／Level、
/// Area Boundary、Area、參與檢討的元素、參數、Area Scheme、規則版本、Phase／Design Option 或幾何容差
/// 的變更使相關結果成為 Stale), and carries the verdict to the package and to the overrides.
/// </summary>
public static class ReviewRunValidity
{
    /// <param name="run">The stored run.</param>
    /// <param name="current">The baseline built from the model as it is now.</param>
    /// <param name="ruleSetId">The rule set the project uses now.</param>
    /// <param name="ruleSetVersion">Its locked version now.</param>
    /// <param name="boundaryRevision">The package's boundary revision now; null when not known.</param>
    public static ReviewRunFreshness Evaluate(
        ReviewRun run,
        ReviewBaseline current,
        string ruleSetId,
        string ruleSetVersion,
        int? boundaryRevision = null)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (current is null) throw new ArgumentNullException(nameof(current));
        if (string.IsNullOrWhiteSpace(ruleSetId)) throw new ArgumentException("Rule set ID is required.", nameof(ruleSetId));
        if (string.IsNullOrWhiteSpace(ruleSetVersion)) throw new ArgumentException("Rule set version is required.", nameof(ruleSetVersion));
        if (!current.IsRecorded) throw new ArgumentException("The current baseline must be built from the model.", nameof(current));

        var global = new List<string>();
        if (run.State != ReviewRunState.Completed)
            global.Add($"此次檢討沒有完成（{run.State}），結果不可沿用");
        if (!run.Baseline.IsRecorded)
            global.Add("此次檢討沒有保存元素證據，無法確認結果仍符合模型");
        if (!string.Equals(run.RuleSetId, ruleSetId.Trim(), StringComparison.Ordinal))
            global.Add($"規則集已從「{run.RuleSetId}」改為「{ruleSetId.Trim()}」");
        else if (!string.Equals(run.RuleSetVersion, ruleSetVersion.Trim(), StringComparison.Ordinal))
            global.Add($"規則版本已從 {run.RuleSetVersion} 更新為 {ruleSetVersion.Trim()}");
        if (boundaryRevision is int revision && revision != run.BoundaryRevision)
            global.Add($"區劃邊界已重新套用（版次 {run.BoundaryRevision} → {revision}）");
        if (run.Baseline.IsRecorded &&
            !string.Equals(run.Baseline.ContextFingerprint, current.ContextFingerprint, StringComparison.Ordinal))
            global.Add("來源視圖、樓層、Area Scheme、Phase、Design Option、幾何容差、專案單位或建築物輸入已變更");

        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<string>();
        if (run.Baseline.IsRecorded)
        {
            foreach (var key in run.Baseline.SubjectIds)
            {
                var now = current.FingerprintOf(key);
                if (now is null) removed.Add(key);
                else if (!string.Equals(now, run.Baseline.Subjects[key], StringComparison.Ordinal)) changed.Add(key);
            }

            added.AddRange(current.SubjectIds.Where(x => run.Baseline.FingerprintOf(x) is null));
        }

        var reasons = new List<string>(global);
        if (changed.Count > 0) reasons.Add(Count("參與檢討的元素或區劃有 {0} 項已被修改", changed));
        if (removed.Count > 0) reasons.Add(Count("參與檢討的元素或區劃有 {0} 項已不在模型中", removed));
        if (added.Count > 0) reasons.Add(Count("模型新增了 {0} 個此次檢討沒有涵蓋的元素或區劃", added));

        var invalidatesAll = global.Count > 0;
        var moved = new HashSet<string>(changed.Concat(removed), StringComparer.Ordinal);
        var stale = run.Results
            .Where(r => invalidatesAll || ReviewBaselineKeys.Of(r).Any(moved.Contains))
            .Select(r => r.ResultId);

        return new ReviewRunFreshness(run, invalidatesAll, reasons, stale, added, removed, changed);
    }

    /// <summary>
    /// The run as it should be stored after the verdict: every active override on a stale result now
    /// awaits confirmation (spec 11.8). Returns the same instance when nothing changes.
    /// </summary>
    public static ReviewRun WithOverridesSuspended(ReviewRunFreshness freshness)
    {
        if (freshness is null) throw new ArgumentNullException(nameof(freshness));
        var run = freshness.Run;
        var suspend = new HashSet<Guid>(freshness.OverridesToReconfirm.Select(x => x.OverrideId));
        if (suspend.Count == 0) return run;

        var reason = "模型或規則版本在覆寫後已變更：" + string.Join("；", freshness.Reasons);
        return run.WithOverrides(run.Overrides.Select(x => suspend.Contains(x.OverrideId)
            ? x.WithStanding(ReviewOverrideStanding.NeedsReconfirmation, reason)
            : x));
    }

    /// <summary>
    /// The package as it should be stored after the verdict: a Reviewed or Documented package whose
    /// run went stale becomes Stale (spec 7「上游資料改變時，下游狀態必須標示為需更新」); any other
    /// status says more than Stale would and is left alone.
    /// </summary>
    public static ReviewPackage ApplyTo(ReviewPackage package, ReviewRunFreshness freshness, DateTime? nowUtc = null)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        if (freshness is null) throw new ArgumentNullException(nameof(freshness));
        if (package.PackageId != freshness.Run.PackageId)
            throw new ArgumentException("The run belongs to another package.", nameof(freshness));

        var reviewed = package.Status == ReviewPackageStatus.Reviewed || package.Status == ReviewPackageStatus.Documented;
        return freshness.IsStale && reviewed ? package.WithProgress(ReviewPackageStatus.Stale, updatedAtUtc: nowUtc) : package;
    }

    /// <summary>The log entries the verdict is worth (spec 14).</summary>
    public static ReviewLog Explain(ReviewRunFreshness freshness, DateTime? timestampUtc = null)
    {
        if (freshness is null) throw new ArgumentNullException(nameof(freshness));

        var log = new ReviewLog.Builder(freshness.Run.PackageId, timestampUtc);
        foreach (var reason in freshness.Reasons)
        {
            log.Add(ReviewErrorCode.StatusStale, ReviewStage.Review, ReviewSeverity.Warning, reason,
                technicalDetail: "Run " + freshness.Run.RunId.ToString("D"),
                suggestion: "請重新執行「開始檢討」。");
        }

        foreach (var entry in freshness.OverridesToReconfirm)
        {
            log.Add(ReviewErrorCode.OverrideNeedsReconfirmation, ReviewStage.Review, ReviewSeverity.Warning,
                $"{entry.OverriddenBy} 將結果改為「{ReviewStatusText.Label(entry.OverriddenStatus)}」的人工覆寫需要重新確認。",
                technicalDetail: $"Override {entry.OverrideId:D} on result {entry.ResultId:D}",
                suggestion: "請重新檢討後，逐項確認人工覆寫是否仍然成立。");
        }

        log.Add(ReviewErrorCode.StatusStale, ReviewStage.Review,
            freshness.IsStale ? ReviewSeverity.Warning : ReviewSeverity.Info, freshness.Message,
            technicalDetail: string.Format(CultureInfo.InvariantCulture, "Stale results {0}/{1}; added {2}, removed {3}, changed {4}",
                freshness.StaleResultIds.Count, freshness.Run.Results.Count,
                freshness.AddedSubjects.Count, freshness.RemovedSubjects.Count, freshness.ChangedSubjects.Count));
        return log.Build();
    }

    private static string Count(string format, ICollection<string> keys) =>
        string.Format(CultureInfo.InvariantCulture, format, keys.Count);
}
