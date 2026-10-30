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
        string message,
        int unwrittenZoneUses = 0)
    {
        Package = package;
        PreviousStatus = previousStatus;
        PreviousBoundaryRevision = previousBoundaryRevision;
        Blockers = new ReadOnlyCollection<string>(blockers.ToList());
        Message = message;
        UnwrittenZoneUses = unwrittenZoneUses;
    }

    /// <summary>
    /// 區劃用途 the run could not write. They do not block the package (驗證清單 C-25) but they are
    /// still something the user has to fix, so the log marks the state line as a warning.
    /// </summary>
    public int UnwrittenZoneUses { get; }

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
    /// Four cases, and the middle ones are the ones that are easy to get wrong. A rollback changed
    /// nothing, so the package is left exactly as it was. A run that wrote nothing and measured
    /// nothing learnt nothing, so it also leaves the package alone rather than promoting one whose
    /// boundaries nobody checked this time. A run that only wrote a parameter — 區劃用途 — moved no
    /// boundary, so it may not take <c>boundaryRevision</c> forward either, but it did write
    /// something and says so (驗證清單 C-09). Otherwise there is a verdict, and it is Ready only when
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

        // 驗證清單 C-09：只有幾何的變動算「動過」。spec 13.1 在 boundaryRevision 變動時作廢既有檢討
        // 成果（連帶單線圖與出圖），理由是「邊界變了」；只設一個區劃用途邊界一動也沒動，卻因為用途
        // 寫回被記成 Updated 而落進 touched，於是 revision +1、已檢討被退回可開始檢討（2026-10-13 實測
        // FL2：revision 7→10、Reviewed→Ready）。改用途確實該讓既有檢討變成「已失效」——它是檢討的
        // 輸入，ReviewBaselineBuilder 會把它算進簽章——但那是 ReviewStaleness 的工作，不是重畫邊界
        // 等級的作廢。所以純參數的寫入分開數：它不碰 revision，也不會把已檢討／已出圖降級（下面
        // AwaitingBoundaries 那道守衛負責這件事）。它唯一的作用是讓訊息說得出「有東西被寫進去了」，
        // 以及讓還在等邊界的套件（設定中／區劃草稿）照舊能被晉升——那是改動前後一致的既有行為。
        var touched = result.GeometryChangeCount;
        var parameters = result.ParameterWriteCount;

        // 驗證清單 C-25：C-09 的反面。用途寫入失敗（多半是沒跑「防火檢討參數設定」，Area 上沒有
        // 防火檢討_區劃用途）邊界一樣一動也沒動，卻曾因 FailedCount > 0 被當成阻擋，把套件退回區劃
        // 草稿——已檢討／已出圖也照退（2026-10-30 空白專案實測 Ready → BoundaryDraft）。補救是去跑參數
        // 設定，不是重畫邊界，所以它只進訊息與日誌，不進 Blockers。
        var unwrittenUses = result.ParameterFailureCount;

        // 只有這一種情形才可以說「模型已經與草稿一致」：真的一個字都沒寫進去。寫了用途卻說已經一致
        // 是假話，所以純參數的套用必須往下走，由 Message 誠實說出寫了什麼。
        if (touched == 0 && parameters == 0 && result.AreaFindings.Count == 0 && result.FailedCount == 0)
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

        // A run whose only line is a 用途 that failed wrote nothing and measured nothing, so — like the
        // early return above — it has no grounds to promote a package still waiting for its boundaries.
        // Reaching here with no blockers means FailedCount > 0 but no geometry failed, so this catches
        // exactly the C-25 case; for states past the boundaries it repeats the guard above.
        var measured = result.AreaFindings.Count > 0;
        if (touched == 0 && parameters == 0 && !measured && blockers.Count == 0) next = status;

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
            message: Message(next, blockers.Count, geometryChanges: touched, parameterWrites: parameters, measured: measured)
                + UnwrittenUsesMessage(unwrittenUses),
            unwrittenZoneUses: unwrittenUses);
    }

    private static string UnwrittenUsesMessage(int count) => count == 0
        ? string.Empty
        : string.Format(
            CultureInfo.InvariantCulture,
            "另有 {0} 個面積的區劃用途沒有寫入（原因見寫回明細）；這一項與邊界無關，不影響套件狀態。",
            count);

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
            outcome.Blockers.Count == 0 && outcome.UnwrittenZoneUses == 0 ? ReviewSeverity.Info : ReviewSeverity.Warning,
            outcome.Message,
            suggestion: outcome.Blockers.Count == 0 && outcome.UnwrittenZoneUses > 0
                ? "依寫回明細處理（多半是尚未執行「防火檢討參數設定」）後重新套用。"
                : null,
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

        // 只數幾何：用途寫入失敗不是邊界問題（驗證清單 C-25），由 UnwrittenUsesMessage 另外說。
        if (result.GeometryFailureCount > 0)
        {
            yield return string.Format(
                CultureInfo.InvariantCulture,
                "有 {0} 個元素沒有寫入模型，草稿與模型還不一致。",
                result.GeometryFailureCount);
        }
    }

    /// <summary>
    /// What the run is allowed to claim. A verify-only run must not say it wrote anything: it measured
    /// what was already there. A run that only wrote 區劃用途 must not say that either — nothing of
    /// the boundary moved, but something did reach the model (驗證清單 C-09), and the user has to know
    /// that the review it already has is now out of date even though the package kept its state.
    /// </summary>
    private static string Message(ReviewPackageStatus status, int blockerCount, int geometryChanges, int parameterWrites, bool measured)
    {
        if (blockerCount > 0)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "有 {0} 項問題未解決，套件維持在「{1}」，尚不得進入「{2}」。",
                blockerCount,
                Describe(ReviewPackageStatus.BoundaryDraft),
                Describe(ReviewPackageStatus.Ready));
        }

        if (geometryChanges > 0)
            return "面積與邊界都已寫入且互相符合，套件進入「" + Describe(status) + "」。";

        if (parameterWrites > 0)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "已設定 {0} 個面積的區劃用途；邊界未變更，套件狀態為「{1}」。既有檢討結果會因輸入改變而標示為需更新。",
                parameterWrites,
                Describe(status));
        }

        // 沒量到任何面積就不能說「相符」（C-25：只有一筆失敗的用途時會走到這裡）。
        return measured
            ? "模型未變更；各區劃面積與草算相符，套件狀態為「" + Describe(status) + "」。"
            : "模型未變更，套件狀態維持「" + Describe(status) + "」。";
    }
}
