using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>What happened to one override when a new run replaced the one it was made on.</summary>
public enum OverrideCarryOverOutcome
{
    /// <summary>Same rule version, same evidence, same computed status: the override still decides the result.</summary>
    Kept,

    /// <summary>Something moved; the override is attached to the new result but awaits confirmation (spec 11.8).</summary>
    NeedsReconfirmation,

    /// <summary>The new run has no matching result, or it already computes what the override said.</summary>
    Dropped
}

public sealed class OverrideCarryOver
{
    internal OverrideCarryOver(ReviewOverride previous, OverrideCarryOverOutcome outcome, Guid? newResultId, string message)
    {
        Previous = previous;
        Outcome = outcome;
        NewResultId = newResultId;
        Message = message;
    }

    /// <summary>The entry on the previous run.</summary>
    public ReviewOverride Previous { get; }

    public OverrideCarryOverOutcome Outcome { get; }

    /// <summary>The result it now belongs to; null when dropped.</summary>
    public Guid? NewResultId { get; }

    public string Message { get; }
}

public sealed class OverrideCarryOverReport
{
    internal OverrideCarryOverReport(ReviewRun run, IEnumerable<OverrideCarryOver> entries)
    {
        Run = run;
        Entries = new ReadOnlyCollection<OverrideCarryOver>(entries.ToList());
    }

    /// <summary>The new run with the carried entries attached.</summary>
    public ReviewRun Run { get; }

    public IReadOnlyList<OverrideCarryOver> Entries { get; }

    public int Count(OverrideCarryOverOutcome outcome) => Entries.Count(x => x.Outcome == outcome);
}

/// <summary>
/// Manual overrides (spec 11.8): a reason is mandatory, the operator and time are recorded together
/// with the computed status and the status put in its place, and an override never survives a model
/// or rule version change unconfirmed. The audit trail is append-only — every change adds an entry
/// and supersedes the one before it, so the history of a result can always be read back.
/// </summary>
public static class ReviewOverrides
{
    /// <summary>Overrides one result of a completed run.</summary>
    /// <param name="freshness">When given, a result the verdict calls stale cannot be overridden.</param>
    public static Result<ReviewRun> Apply(
        ReviewRun run,
        Guid resultId,
        ReviewStatus status,
        string reason,
        string overriddenBy,
        DateTime overriddenAtUtc,
        string? comment = null,
        ReviewRunFreshness? freshness = null,
        Func<Guid>? newId = null)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));

        var check = Target(run, resultId, freshness);
        if (check.IsFailure) return Result.Failure<ReviewRun>(check.Error);
        var result = check.Value;

        if (!Enum.IsDefined(typeof(ReviewStatus), status) || status == ReviewStatus.NotRun)
            return Rejected("人工覆寫必須選擇符合、未符合、資料不足、不適用或人工覆核其中一項。");
        if (status == result.Status)
            return Rejected($"檢討結果已經是「{ReviewStatusText.Label(status)}」，不需要覆寫；要撤回既有覆寫請使用撤回。");
        if (string.IsNullOrWhiteSpace(reason)) return Rejected("人工覆寫必須填寫原因。");
        if (string.IsNullOrWhiteSpace(overriddenBy)) return Rejected("人工覆寫必須記錄操作者。");

        var current = run.CurrentOverrideFor(resultId);
        var entry = new ReviewOverride(
            (newId ?? Guid.NewGuid)(), resultId, result.Status, status, reason, comment, overriddenBy, overriddenAtUtc,
            result.RuleVersion, ReviewBaselineBuilder.DependencyFingerprint(run.Baseline, result),
            previousOverrideId: current?.OverrideId);

        return Result.Success(run.WithOverrides(Replace(run, current,
            $"已由 {entry.OverriddenBy} 的新覆寫取代", entry)));
    }

    /// <summary>
    /// Confirms an override that awaits confirmation against the result as it is now. The reason may
    /// be restated; when omitted the original reason stands, and the confirmation itself is recorded.
    /// </summary>
    public static Result<ReviewRun> Reconfirm(
        ReviewRun run,
        Guid resultId,
        string confirmedBy,
        DateTime confirmedAtUtc,
        string? reason = null,
        string? comment = null,
        ReviewRunFreshness? freshness = null,
        Func<Guid>? newId = null)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));

        var check = Target(run, resultId, freshness);
        if (check.IsFailure) return Result.Failure<ReviewRun>(check.Error);
        var result = check.Value;

        var current = run.CurrentOverrideFor(resultId);
        if (current is null || current.Standing != ReviewOverrideStanding.NeedsReconfirmation)
            return Rejected("這個結果沒有需要重新確認的人工覆寫。");
        if (string.IsNullOrWhiteSpace(confirmedBy)) return Rejected("重新確認必須記錄操作者。");
        if (current.OverriddenStatus == result.Status)
            return Rejected($"檢討結果已經是「{ReviewStatusText.Label(result.Status)}」，這筆覆寫不需要再確認，請撤回。");

        var entry = new ReviewOverride(
            (newId ?? Guid.NewGuid)(), resultId, result.Status, current.OverriddenStatus,
            string.IsNullOrWhiteSpace(reason) ? current.Reason : reason!, comment ?? current.Comment,
            confirmedBy, confirmedAtUtc, result.RuleVersion,
            ReviewBaselineBuilder.DependencyFingerprint(run.Baseline, result),
            previousOverrideId: current.OverrideId);

        return Result.Success(run.WithOverrides(Replace(run, current,
            $"已由 {entry.OverriddenBy} 重新確認", entry)));
    }

    /// <summary>Withdraws the override on a result; the computed status applies again and the entry stays in the audit trail.</summary>
    public static Result<ReviewRun> Withdraw(
        ReviewRun run,
        Guid resultId,
        string reason,
        string withdrawnBy,
        DateTime withdrawnAtUtc)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (run.Result(resultId) is null) return Rejected("此次檢討沒有這個結果。");
        var current = run.CurrentOverrideFor(resultId);
        if (current is null) return Rejected("這個結果沒有人工覆寫可以撤回。");
        if (string.IsNullOrWhiteSpace(reason)) return Rejected("撤回人工覆寫必須填寫原因。");
        if (string.IsNullOrWhiteSpace(withdrawnBy)) return Rejected("撤回人工覆寫必須記錄操作者。");

        var note = $"已由 {withdrawnBy.Trim()} 於 {withdrawnAtUtc.ToUniversalTime():yyyy-MM-dd HH:mm} UTC 撤回：{reason.Trim()}";
        return Result.Success(run.WithOverrides(Replace(run, current, note, null)));
    }

    /// <summary>
    /// Moves the overrides of the previous run onto the matching results of a new one. A result
    /// matches when it has the same check, rule, zone and subjects. An override is kept only when
    /// the rule version, the evidence and the computed status are all unchanged; otherwise it comes
    /// along awaiting confirmation (spec 11.8「狀態改為需重新確認」).
    /// </summary>
    public static OverrideCarryOverReport CarryOver(ReviewRun previous, ReviewRun next, Func<Guid>? newId = null)
    {
        if (previous is null) throw new ArgumentNullException(nameof(previous));
        if (next is null) throw new ArgumentNullException(nameof(next));
        if (previous.PackageId != next.PackageId)
            throw new ArgumentException("Overrides can only move between runs of one package.", nameof(next));
        if (previous.RunId == next.RunId)
            throw new ArgumentException("Overrides move from an earlier run to a new one.", nameof(next));
        if (next.State != ReviewRunState.Completed)
            throw new ArgumentException("Overrides can only move onto a completed run.", nameof(next));

        var id = newId ?? Guid.NewGuid;
        var matches = next.Results.GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var attached = new List<ReviewOverride>(next.Overrides);
        var report = new List<OverrideCarryOver>();

        foreach (var entry in previous.Overrides.Where(x => x.IsCurrent))
        {
            var old = previous.Result(entry.ResultId)!;
            if (!matches.TryGetValue(Key(old), out var candidates) || candidates.Count != 1)
            {
                report.Add(new OverrideCarryOver(entry, OverrideCarryOverOutcome.Dropped, null,
                    "新的檢討沒有對應的結果（元素或區劃已不再參與檢討），覆寫不再沿用。"));
                continue;
            }

            var target = candidates[0];
            if (attached.Any(x => x.ResultId == target.ResultId && x.IsCurrent))
            {
                report.Add(new OverrideCarryOver(entry, OverrideCarryOverOutcome.Dropped, target.ResultId,
                    "新的檢討結果已有自己的人工覆寫。"));
                continue;
            }

            if (target.Status == entry.OverriddenStatus)
            {
                report.Add(new OverrideCarryOver(entry, OverrideCarryOverOutcome.Dropped, target.ResultId,
                    $"重新檢討的結果已是「{ReviewStatusText.Label(target.Status)}」，與覆寫相同，覆寫不再需要。"));
                continue;
            }

            var fingerprint = ReviewBaselineBuilder.DependencyFingerprint(next.Baseline, target);
            var why = Unconfirmed(entry, target, fingerprint);
            var carried = new ReviewOverride(
                id(), target.ResultId, entry.OriginalStatus, entry.OverriddenStatus, entry.Reason, entry.Comment,
                entry.OverriddenBy, entry.OverriddenAtUtc, entry.RuleVersion, entry.DependencyFingerprint,
                why is null ? ReviewOverrideStanding.Active : ReviewOverrideStanding.NeedsReconfirmation, why,
                entry.OverrideId);
            attached.Add(carried);
            report.Add(why is null
                ? new OverrideCarryOver(entry, OverrideCarryOverOutcome.Kept, target.ResultId,
                    "規則版本、元素證據與計算結果都未變更，覆寫沿用。")
                : new OverrideCarryOver(entry, OverrideCarryOverOutcome.NeedsReconfirmation, target.ResultId, why));
        }

        return new OverrideCarryOverReport(next.WithOverrides(attached), report);
    }

    /// <summary>Why a carried override cannot decide its new result on its own; null when it can.</summary>
    private static string? Unconfirmed(ReviewOverride entry, ReviewResult target, string? fingerprint)
    {
        if (entry.Standing == ReviewOverrideStanding.NeedsReconfirmation)
            return entry.StandingReason ?? "前次檢討時已需要重新確認。";

        var reasons = new List<string>();
        if (!string.Equals(entry.RuleVersion, target.RuleVersion, StringComparison.Ordinal))
            reasons.Add($"規則 {target.RuleId} 已從 {entry.RuleVersion} 更新為 {target.RuleVersion}");
        if (entry.DependencyFingerprint is null || fingerprint is null)
            reasons.Add("覆寫時沒有保存元素證據，無法確認模型未變更");
        else if (!string.Equals(entry.DependencyFingerprint, fingerprint, StringComparison.Ordinal))
            reasons.Add("相關元素、參數或區劃在覆寫後已變更");
        if (entry.OriginalStatus != target.Status)
            reasons.Add($"計算結果已從「{ReviewStatusText.Label(entry.OriginalStatus)}」變為「{ReviewStatusText.Label(target.Status)}」");

        return reasons.Count == 0 ? null : string.Join("；", reasons) + "。";
    }

    private static string Key(ReviewResult result) => string.Join("\u001f", new[]
    {
        result.CheckType,
        result.RuleId,
        result.ZoneId ?? string.Empty,
        string.Join("\u001e", result.SubjectUniqueIds.OrderBy(x => x, StringComparer.Ordinal))
    });

    private static Result<ReviewResult> Target(ReviewRun run, Guid resultId, ReviewRunFreshness? freshness)
    {
        if (run.State != ReviewRunState.Completed)
            return Result.Failure<ReviewResult>(Rejection("只有已完成的檢討可以人工覆寫。"));
        var result = run.Result(resultId);
        if (result is null) return Result.Failure<ReviewResult>(Rejection("此次檢討沒有這個結果。"));
        if (freshness is not null)
        {
            if (freshness.Run.RunId != run.RunId)
                throw new ArgumentException("The freshness verdict is about another run.", nameof(freshness));
            if (freshness.IsResultStale(resultId))
                return Result.Failure<ReviewResult>(Rejection("這個結果已失效，請先重新檢討，再決定是否覆寫。"));
        }

        return Result.Success(result);
    }

    private static IEnumerable<ReviewOverride> Replace(ReviewRun run, ReviewOverride? current, string note, ReviewOverride? added)
    {
        foreach (var entry in run.Overrides)
            yield return current is not null && entry.OverrideId == current.OverrideId
                ? entry.WithStanding(ReviewOverrideStanding.Superseded, note)
                : entry;
        if (added is not null) yield return added;
    }

    private static Error Rejection(string message) => new Error(ReviewErrorCode.OverrideRejected, message);

    private static Result<ReviewRun> Rejected(string message) => Result.Failure<ReviewRun>(Rejection(message));
}
