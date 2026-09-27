using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using BuildingRegulationReview.Application.Abstractions;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>Everything one review run needs, already read from the model (spec 15: pure data only).</summary>
public sealed class FireReviewRequest
{
    public FireReviewRequest(
        ReviewPackage package,
        CompiledRuleSet ruleSet,
        RuleEvaluationContext context,
        CandidateSet candidates,
        ReviewInputAssembly inputs,
        ReviewEnvironment? environment = null,
        ReviewRun? previousRun = null,
        TimeSpan prescanElapsed = default,
        CompartmentAreaOptions? areaOptions = null,
        ICurtainWallGeometryReader? curtainWallReader = null,
        CurtainWallJunctionOptions? junctionOptions = null,
        FireRatingUnit bareNumberUnit = FireRatingUnit.Minute)
    {
        Package = package ?? throw new ArgumentNullException(nameof(package));
        RuleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
        if (candidates.PackageId != package.PackageId)
            throw new ArgumentException("The candidates were read for another package.", nameof(candidates));
        if (previousRun is not null && previousRun.PackageId != package.PackageId)
            throw new ArgumentException("The previous run belongs to another package.", nameof(previousRun));
        if (prescanElapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(prescanElapsed));

        Environment = environment ?? ReviewEnvironment.Empty;
        PreviousRun = previousRun;
        PrescanElapsed = prescanElapsed;
        AreaOptions = areaOptions ?? CompartmentAreaOptions.Default;
        CurtainWallReader = curtainWallReader;
        JunctionOptions = junctionOptions ?? CurtainWallJunctionOptions.Default;
        BareNumberUnit = bareNumberUnit;
    }

    public ReviewPackage Package { get; }
    public CompiledRuleSet RuleSet { get; }
    public RuleEvaluationContext Context { get; }
    public CandidateSet Candidates { get; }
    public ReviewInputAssembly Inputs { get; }
    public ReviewEnvironment Environment { get; }

    /// <summary>The package's latest run, whose overrides move to the new one (spec 11.8).</summary>
    public ReviewRun? PreviousRun { get; }

    /// <summary>How long the adapter took to read the model, for the performance diagnosis (spec 15).</summary>
    public TimeSpan PrescanElapsed { get; }

    public CompartmentAreaOptions AreaOptions { get; }

    /// <summary>
    /// Reads the curtain walls of the package's storey. It is the only part of a run that reads the
    /// model while the run is under way: the 交接帶 can only be measured against the ratings 第70條 and
    /// 第79條 require, which the 構件防火時效 check works out in the run itself (帷幕牆規格 §4.1). Null
    /// leaves 帷幕牆區劃交接 未檢討 — nothing is guessed from the model without reading it.
    /// </summary>
    public ICurtainWallGeometryReader? CurtainWallReader { get; }

    public CurtainWallJunctionOptions JunctionOptions { get; }

    /// <summary>What a bare number in 防火檢討_設計防火時效 means, as the project declared it.</summary>
    public FireRatingUnit BareNumberUnit { get; }
}

public enum FireReviewStep
{
    CompartmentArea,

    /// <summary>
    /// 第79條之1 之免除, run straight after the area it is the exception to
    /// (docs/regulations/article-79-1-area-exemption.md §7.1).
    /// </summary>
    AreaExemption,

    FireResistance,
    OpeningProtection,
    CurtainWallJunction,
    VerticalCompartment,
    Evidence
}

/// <summary>Progress of a run, reported after each safe point (spec 15「分段處理並回報進度」).</summary>
public sealed class FireReviewProgress
{
    internal FireReviewProgress(FireReviewStep step, int completed, int total)
    {
        Step = step;
        Completed = completed;
        Total = total;
    }

    public FireReviewStep Step { get; }
    public int Completed { get; }
    public int Total { get; }

    public string Message => $"{FireReviewRunner.Label(Step)}（{Completed}/{Total}）";

    public override string ToString() => Message;
}

public enum FireReviewOutcomeKind
{
    Completed,
    Cancelled,
    Failed
}

/// <summary>What one run produced. Only a completed outcome carries a run to store.</summary>
public sealed class FireReviewOutcome
{
    internal FireReviewOutcome(
        FireReviewOutcomeKind kind,
        ReviewPackage package,
        ReviewRun? run,
        OverrideCarryOverReport? carryOver,
        ReviewLog log,
        ReviewPerformance performance,
        Error? error)
    {
        Kind = kind;
        Package = package;
        Run = run;
        CarryOver = carryOver;
        Log = log;
        Performance = performance;
        Error = error;
        Table = run is null ? null : ReviewTable.Build(run);
    }

    public FireReviewOutcomeKind Kind { get; }
    public bool IsCompleted => Kind == FireReviewOutcomeKind.Completed;

    /// <summary>The package as it should be stored: Reviewed with the rule set locked when completed, unchanged otherwise.</summary>
    public ReviewPackage Package { get; }

    public ReviewRun? Run { get; }
    public OverrideCarryOverReport? CarryOver { get; }
    public ReviewTable? Table { get; }
    public ReviewLog Log { get; }
    public ReviewPerformance Performance { get; }
    public Error? Error { get; }

    public string Message => Kind switch
    {
        FireReviewOutcomeKind.Completed => $"檢討完成：{ReviewVerdictText.Label(Table!.Verdict)}（{Table.Counts.Text}）。",
        FireReviewOutcomeKind.Cancelled => "檢討已取消，模型與既有檢討紀錄都沒有變更。",
        _ => "檢討無法完成：" + (Error?.Message ?? "未知錯誤")
    };
}

/// <summary>
/// Spec 15 目標效能: a storey of 5,000 candidate elements is pre-scanned within 10 seconds and
/// reviewed within 30. The target scales with the candidate count above that, and a run that
/// misses it logs a performance diagnosis with the time of every stage.
/// </summary>
public sealed class ReviewPerformance
{
    public const int BaselineCandidateCount = 5000;
    public static readonly TimeSpan PrescanTarget = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ReviewTarget = TimeSpan.FromSeconds(30);

    public ReviewPerformance(int candidateCount, TimeSpan prescan, IEnumerable<KeyValuePair<string, TimeSpan>>? stages)
    {
        if (candidateCount < 0) throw new ArgumentOutOfRangeException(nameof(candidateCount));
        if (prescan < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(prescan));
        CandidateCount = candidateCount;
        Prescan = prescan;
        Stages = new ReadOnlyCollection<KeyValuePair<string, TimeSpan>>(
            (stages ?? Array.Empty<KeyValuePair<string, TimeSpan>>()).ToList());
    }

    public int CandidateCount { get; }
    public TimeSpan Prescan { get; }
    public IReadOnlyList<KeyValuePair<string, TimeSpan>> Stages { get; }
    public TimeSpan Review => TimeSpan.FromTicks(Stages.Sum(x => x.Value.Ticks));

    public TimeSpan PrescanLimit => Scaled(PrescanTarget);
    public TimeSpan ReviewLimit => Scaled(ReviewTarget);
    public bool PrescanExceeded => Prescan > PrescanLimit;
    public bool ReviewExceeded => Review > ReviewLimit;
    public bool Exceeded => PrescanExceeded || ReviewExceeded;

    public string Summary => string.Format(CultureInfo.InvariantCulture,
        "候選元素 {0} 個；前置掃描 {1:0.0} 秒（目標 {2:0.0} 秒）；檢討 {3:0.0} 秒（目標 {4:0.0} 秒）",
        CandidateCount, Prescan.TotalSeconds, PrescanLimit.TotalSeconds, Review.TotalSeconds, ReviewLimit.TotalSeconds);

    public string StageDetail => string.Join("; ",
        new[] { "prescan=" + Ms(Prescan) }.Concat(Stages.Select(x => x.Key + "=" + Ms(x.Value))));

    public void AppendTo(ReviewLog.Builder log)
    {
        if (log is null) throw new ArgumentNullException(nameof(log));
        if (Exceeded)
        {
            log.Add(ReviewErrorCode.PerformanceExceeded, ReviewStage.Review, ReviewSeverity.Warning,
                "檢討時間超出效能目標：" + Summary + "。",
                technicalDetail: StageDetail,
                suggestion: "請把日誌提供給維護者做效能診斷；可縮小 Area Plan 範圍或關閉不需要的構件類別。");
        }
        else
        {
            log.Add(ReviewErrorCode.ReviewCompleted, ReviewStage.Review, ReviewSeverity.Info,
                "效能：" + Summary + "。", technicalDetail: StageDetail);
        }
    }

    private TimeSpan Scaled(TimeSpan target) => CandidateCount <= BaselineCandidateCount
        ? target
        : TimeSpan.FromTicks((long)(target.Ticks * (CandidateCount / (double)BaselineCandidateCount)));

    private static string Ms(TimeSpan value) => ((long)value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + "ms";
}

/// <summary>
/// P3-T09: runs 區劃面積, 區劃面積免除（第79條之1）, 構件防火時效, 防火門窗, 帷幕牆區劃交接 and 垂直區劃
/// as one review (spec 11, 帷幕牆規格 §8, 垂直區劃規格 §12 步驟 5, 第79條之1文件 §12 步驟 3), on data the
/// adapter has already read — except the curtain walls, which can only be measured
/// once the required ratings are known and are therefore read in the step itself
/// (<see cref="FireReviewRequest.CurtainWallReader"/>). Between checks is a safe point: a cancellation there returns without a run, so
/// nothing is stored and the model is not touched. A completed run carries its evidence baseline
/// (spec 13.1), the previous run's overrides as far as they still hold (spec 11.8), and leaves the
/// package Reviewed with the rule set version locked.
/// </summary>
/// <remarks>
/// The spec 11.1 conditions are checked beforehand by <see cref="ReviewReadiness"/>; a check that
/// still refuses (its own zone precondition) fails the whole run rather than storing a partial one,
/// because a review table with a row missing would read as 未檢討 instead of as an error.
/// </remarks>
public static class FireReviewRunner
{
    public static FireReviewOutcome Run(
        FireReviewRequest request,
        CancellationToken cancellation = default,
        IProgress<FireReviewProgress>? progress = null,
        Func<DateTime>? clock = null,
        Func<Guid>? newId = null)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        clock ??= () => DateTime.UtcNow;
        newId ??= Guid.NewGuid;

        var started = clock().ToUniversalTime();
        var log = new ReviewLog.Builder(request.Package.PackageId, started);
        var stages = new List<KeyValuePair<string, TimeSpan>>();
        var set = request.Candidates;
        var candidateCount = set.Members.Count + set.Openings.Count + set.UnrelatedMemberCount + set.UnrelatedOpeningCount;
        ReviewPerformance Performance() => new(candidateCount, request.PrescanElapsed, stages);

        var ruleSet = request.RuleSet.RuleSet;
        var runId = newId();
        var run = new ReviewRun(runId, request.Package.PackageId, ruleSet.RuleSetId, ruleSet.Version,
            request.Package.BoundaryRevision, started);
        var engine = new RuleEngine(request.RuleSet);
        const int total = 7;

        if (cancellation.IsCancellationRequested) return Cancelled(request, log, Performance());

        var watch = Stopwatch.StartNew();
        var area = CompartmentAreaCheck.Review(set, request.Inputs.Area, engine, request.Context, runId, request.AreaOptions, newId);
        stages.Add(Stage(FireReviewStep.CompartmentArea, watch));
        if (area.IsFailure) return Failed(request, log, Performance(), area.Error, FireReviewStep.CompartmentArea);
        progress?.Report(new FireReviewProgress(FireReviewStep.CompartmentArea, 1, total));
        if (cancellation.IsCancellationRequested) return Cancelled(request, log, Performance());

        // 第79條之1 reads the same 區劃 inputs the area check just read, and says nothing about the
        // area result — it is the exception to that row, reported beside it (第79條之1文件 §3.5、決議 12).
        watch.Restart();
        var exemption = Article79_1ExemptionCheck.Review(set, request.Inputs.Area, request.RuleSet, runId, newId);
        stages.Add(Stage(FireReviewStep.AreaExemption, watch));
        if (exemption.IsFailure) return Failed(request, log, Performance(), exemption.Error, FireReviewStep.AreaExemption);
        progress?.Report(new FireReviewProgress(FireReviewStep.AreaExemption, 2, total));
        if (cancellation.IsCancellationRequested) return Cancelled(request, log, Performance());

        watch.Restart();
        var rating = FireResistanceCheck.Review(set, request.Inputs.Rating, engine, request.Context, runId, newId);
        stages.Add(Stage(FireReviewStep.FireResistance, watch));
        if (rating.IsFailure) return Failed(request, log, Performance(), rating.Error, FireReviewStep.FireResistance);
        progress?.Report(new FireReviewProgress(FireReviewStep.FireResistance, 3, total));
        if (cancellation.IsCancellationRequested) return Cancelled(request, log, Performance());

        watch.Restart();
        var opening = OpeningProtectionCheck.Review(set, request.Inputs.Protection, engine, request.Context, runId, newId);
        stages.Add(Stage(FireReviewStep.OpeningProtection, watch));
        if (opening.IsFailure) return Failed(request, log, Performance(), opening.Error, FireReviewStep.OpeningProtection);
        progress?.Report(new FireReviewProgress(FireReviewStep.OpeningProtection, 4, total));
        if (cancellation.IsCancellationRequested) return Cancelled(request, log, Performance());

        watch.Restart();
        var junction = Junctions(request, area.Value, rating.Value, engine, runId, log, newId);
        stages.Add(Stage(FireReviewStep.CurtainWallJunction, watch));
        if (junction.IsFailure) return Failed(request, log, Performance(), junction.Error, FireReviewStep.CurtainWallJunction);
        progress?.Report(new FireReviewProgress(FireReviewStep.CurtainWallJunction, 5, total));
        if (cancellation.IsCancellationRequested) return Cancelled(request, log, Performance());

        watch.Restart();
        var shaft = VerticalCompartmentCheck.Review(set, request.Inputs.VerticalCompartment, engine, request.Context, runId, newId);
        stages.Add(Stage(FireReviewStep.VerticalCompartment, watch));
        if (shaft.IsFailure) return Failed(request, log, Performance(), shaft.Error, FireReviewStep.VerticalCompartment);
        progress?.Report(new FireReviewProgress(FireReviewStep.VerticalCompartment, 6, total));
        if (cancellation.IsCancellationRequested) return Cancelled(request, log, Performance());

        watch.Restart();
        var baseline = ReviewBaselineBuilder.Build(set, request.Environment, request.Inputs);
        var results = area.Value.Results
            .Concat(exemption.Value.Results)
            .Concat(rating.Value.Results)
            .Concat(opening.Value.Results)
            .Concat(junction.Value.Results)
            .Concat(shaft.Value.Results)
            .ToList();
        var completedAt = clock().ToUniversalTime();
        if (completedAt < started) completedAt = started;
        var completed = run.Complete(results, completedAt, ReviewRunState.Completed, baseline);

        OverrideCarryOverReport? carryOver = null;
        if (request.PreviousRun is { State: ReviewRunState.Completed } previous && previous.Overrides.Any(x => x.IsCurrent))
        {
            carryOver = ReviewOverrides.CarryOver(previous, completed, newId);
            completed = carryOver.Run;
        }
        stages.Add(Stage(FireReviewStep.Evidence, watch));
        progress?.Report(new FireReviewProgress(FireReviewStep.Evidence, 7, total));

        var package = request.Package.WithReviewRun(ruleSet.RuleSetId, ruleSet.Version, runId,
            ReviewPackageStatus.Reviewed, completedAt);

        Findings(log, area.Value, exemption.Value, rating.Value, opening.Value, junction.Value, shaft.Value);
        if (carryOver is not null) CarryOverLog(log, carryOver);
        var table = ReviewTable.Build(completed);
        log.Add(ReviewErrorCode.ReviewCompleted, ReviewStage.Review,
            table.Verdict == ReviewVerdict.Fail ? ReviewSeverity.Warning : ReviewSeverity.Info,
            $"檢討完成（規則集 {ruleSet.RuleSetId} {ruleSet.Version}）：{ReviewVerdictText.Label(table.Verdict)}；" +
            string.Join("；", table.Sections.Select(s => $"{s.Title} {ReviewStatusText.Label(s.Status)}（{s.Counts.Text}）")) + "。",
            technicalDetail: "RunId " + runId.ToString("D"));
        var performance = Performance();
        performance.AppendTo(log);

        return new FireReviewOutcome(FireReviewOutcomeKind.Completed, package, completed, carryOver, log.Build(), performance, null);
    }

    public static string Label(FireReviewStep step) => step switch
    {
        FireReviewStep.CompartmentArea => "防火區劃面積",
        FireReviewStep.AreaExemption => "區劃面積免除（第79條之1）",
        FireReviewStep.FireResistance => "構件防火時效",
        FireReviewStep.OpeningProtection => "防火門窗",
        FireReviewStep.CurtainWallJunction => "帷幕牆區劃交接",
        FireReviewStep.VerticalCompartment => "垂直區劃",
        FireReviewStep.Evidence => "保存證據與人工覆寫",
        _ => step.ToString()
    };

    private static KeyValuePair<string, TimeSpan> Stage(FireReviewStep step, Stopwatch watch) =>
        new(step.ToString(), watch.Elapsed);

    /// <summary>
    /// 帷幕牆區劃交接 (帷幕牆規格 §4、§8). The compartment boundaries are the ones candidate resolution
    /// decided, and the rating each one must reach is what the 構件防火時效 check just required of it —
    /// which is why this is the one step that reads the model while the run is under way. The reader
    /// only converts: the junctions are the resolver's, the verdicts the rules'.
    /// </summary>
    /// <remarks>
    /// A read that fails stops the whole run, like any check that refuses (see the type's remarks). No
    /// reader at all is not a failure: 帷幕牆區劃交接 is simply 未檢討, and the log says so.
    /// </remarks>
    private static Result<CurtainWallJunctionReview> Junctions(
        FireReviewRequest request,
        CompartmentAreaReview area,
        FireResistanceReview rating,
        RuleEngine engine,
        Guid runId,
        ReviewLog.Builder log,
        Func<Guid> newId)
    {
        var set = request.Candidates;
        if (request.CurtainWallReader is null || request.Package.AreaPlanUniqueId is null)
        {
            log.Add(ReviewErrorCode.ReviewCompleted, ReviewStage.Review, ReviewSeverity.Info,
                request.CurtainWallReader is null
                    ? "本次檢討沒有讀取帷幕牆幾何，帷幕牆區劃交接未檢討。"
                    : "這個工作包沒有 Area Plan，無法讀取帷幕牆幾何，帷幕牆區劃交接未檢討。");
            return Result.Success(new CurtainWallJunctionReview(
                Array.Empty<CurtainWallJunctionFinding>(), Array.Empty<string>()));
        }

        var references = HostLegalReferences(set, area);
        var read = request.CurtainWallReader.Read(new CurtainWallReadRequest(
            set.PackageId, request.Package.AreaPlanUniqueId,
            RequiredRatings(rating, references.Keys), references, request.BareNumberUnit, request.JunctionOptions));
        if (read.IsFailure) return Result.Failure<CurtainWallJunctionReview>(read.Error);

        foreach (var warning in read.Value.Warnings)
            log.Add(ReviewErrorCode.ReviewCompleted, ReviewStage.Review, ReviewSeverity.Warning, warning);

        var junctions = CurtainWallJunctionResolver.Resolve(read.Value, request.JunctionOptions);
        return CurtainWallJunctionCheck.Review(set, new CurtainWallJunctionInputs(request.Inputs.Area, junctions),
            engine, request.Context, runId, request.JunctionOptions, newId);
    }

    /// <summary>
    /// What each host must achieve, as the rules required it of that element. A host reviewed in more
    /// than one 區劃 keeps the most onerous of them: the 交接帶 has to satisfy both sides. Only the
    /// hosts are listed — naming anything else would have the reader read a 柱 or an interior wall as a
    /// 區劃 boundary, which is not what 第70條 required a rating of it for.
    /// </summary>
    private static IReadOnlyDictionary<string, double> RequiredRatings(FireResistanceReview rating, IEnumerable<string> hosts)
    {
        var wanted = new HashSet<string>(hosts, StringComparer.Ordinal);
        var required = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var finding in rating.Findings)
        {
            if (finding.RequiredMinutes is not double minutes || !wanted.Contains(finding.ElementUniqueId)) continue;
            if (!required.TryGetValue(finding.ElementUniqueId, out var current) || minutes > current)
                required[finding.ElementUniqueId] = minutes;
        }
        return required;
    }

    /// <summary>
    /// The 區劃 boundaries to read, each with the clause it comes from (帷幕牆規格 §2.5). Naming a host
    /// here is what makes the reader read it as a compartment boundary, so every boundary wall and every
    /// 樓地板 of the storey is listed, whether or not a required rating was worked out for it.
    /// </summary>
    /// <remarks>
    /// 第83條 is not read from the model: a 區劃 is 第83條's when that is the clause the 區劃面積 rule
    /// decided it under, which is the rule engine's answer and no one else's. A wall between a 第79條
    /// 區劃 and a 第83條 one is recorded as 第79條, the general clause — one junction yields one result,
    /// and it must read the same on every rerun.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> HostLegalReferences(CandidateSet set, CompartmentAreaReview area)
    {
        var article83 = new HashSet<Guid>(area.Findings
            .Where(f => f.Result.LegalReference.IndexOf(CurtainWallJunctionReferences.Article83, StringComparison.Ordinal) >= 0)
            .Select(f => f.ZoneId));

        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in set.Members)
        {
            var uniqueId = member.Observation.Source.ElementUniqueId;
            if (member.Category == CandidateCategory.Floor)
            {
                references[uniqueId] = CurtainWallJunctionReferences.Article79_3;
                continue;
            }

            if (member.Category != CandidateCategory.Wall) continue;
            var zones = member.Relations.Where(r => r.IsBoundary).Select(r => r.ZoneId).ToList();
            if (zones.Count == 0) continue;
            references[uniqueId] = zones.All(article83.Contains)
                ? CurtainWallJunctionReferences.Article83
                : CurtainWallJunctionReferences.Article79;
        }
        return references;
    }

    private static void Findings(
        ReviewLog.Builder log,
        CompartmentAreaReview area,
        Article79_1ExemptionReview exemption,
        FireResistanceReview rating,
        OpeningProtectionReview opening,
        CurtainWallJunctionReview junction,
        VerticalCompartmentReview shaft)
    {
        foreach (var warning in area.Warnings.Concat(exemption.Warnings)
                     .Concat(rating.Warnings).Concat(opening.Warnings).Concat(junction.Warnings)
                     .Concat(shaft.Warnings)
                     .Distinct(StringComparer.Ordinal))
            log.Add(ReviewErrorCode.ReviewCompleted, ReviewStage.Review, ReviewSeverity.Warning, warning);

        // 垂直區劃規格 §7: all four rows are logged even when a row is empty, because a 管道間 whose
        // 維修門 was never modelled — or a storey with no 挑空 — reads the same as a row that was not
        // reviewed unless the count says 0. The fourth row is 第3項, so the line cites the article
        // rather than 第1項 alone (§7.3).
        if (shaft.Findings.Count > 0)
        {
            log.Add(ReviewErrorCode.ReviewCompleted, ReviewStage.Review, ReviewSeverity.Info,
                "垂直區劃（第79條之2）：" + string.Join("；", shaft.Groups.Select(g =>
                    $"{g.Label} {g.DeviceCount} 件{ReviewStatusText.Label(g.Status)}")) + "。");
        }

        var findings = area.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.Zone.AreaUniqueIds.FirstOrDefault()))
            .Concat(exemption.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.ElementUniqueId)))
            .Concat(rating.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.ElementUniqueId)))
            .Concat(opening.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.ElementUniqueId)))
            .Concat(junction.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.Junction.CurtainWallUniqueId)))
            .Concat(shaft.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.ElementUniqueId)));
        foreach (var (result, code, element) in findings)
        {
            if (code is null && result.Status != ReviewStatus.Fail) continue;
            log.Add(code ?? ReviewErrorCode.ReviewCompleted, ReviewStage.Review,
                result.Status == ReviewStatus.Fail ? ReviewSeverity.Warning : ReviewSeverity.Info,
                $"{ReviewTable.Title(result.CheckType)}「{ReviewStatusText.Label(result.Status)}」：{result.Message}",
                elementUniqueId: element,
                technicalDetail: $"Result {result.ResultId:D}; Rule {result.RuleId} v{result.RuleVersion}; Zone {result.ZoneId}");
        }
    }

    private static void CarryOverLog(ReviewLog.Builder log, OverrideCarryOverReport report)
    {
        foreach (var entry in report.Entries)
        {
            log.Add(entry.Outcome == OverrideCarryOverOutcome.NeedsReconfirmation
                    ? ReviewErrorCode.OverrideNeedsReconfirmation
                    : ReviewErrorCode.ReviewCompleted,
                ReviewStage.Review,
                entry.Outcome == OverrideCarryOverOutcome.NeedsReconfirmation ? ReviewSeverity.Warning : ReviewSeverity.Info,
                $"人工覆寫（{entry.Previous.OverriddenBy}：{ReviewStatusText.Label(entry.Previous.OverriddenStatus)}）" +
                OutcomeText(entry.Outcome) + "：" + entry.Message,
                technicalDetail: $"Override {entry.Previous.OverrideId:D} -> Result {entry.NewResultId?.ToString("D") ?? "-"}");
        }
    }

    private static string OutcomeText(OverrideCarryOverOutcome outcome) => outcome switch
    {
        OverrideCarryOverOutcome.Kept => "沿用",
        OverrideCarryOverOutcome.NeedsReconfirmation => "需重新確認",
        _ => "不再沿用"
    };

    private static FireReviewOutcome Cancelled(FireReviewRequest request, ReviewLog.Builder log, ReviewPerformance performance)
    {
        log.Add(ReviewErrorCode.ReviewCancelled, ReviewStage.Review, ReviewSeverity.Info,
            "檢討已取消，沒有儲存任何結果，模型未變更。");
        return new FireReviewOutcome(FireReviewOutcomeKind.Cancelled, request.Package, null, null, log.Build(), performance, null);
    }

    private static FireReviewOutcome Failed(
        FireReviewRequest request, ReviewLog.Builder log, ReviewPerformance performance, Error error, FireReviewStep step)
    {
        log.Add(ReviewErrorCode.IsKnown(error.Code) ? error.Code : ReviewErrorCode.ReviewNotReady,
            ReviewStage.Review, ReviewSeverity.Error,
            $"{Label(step)}無法檢討，整次檢討停止：{error.Message}",
            technicalDetail: error.TechnicalDetail,
            suggestion: "請依訊息修正後重新執行前置檢查。");
        return new FireReviewOutcome(FireReviewOutcomeKind.Failed, request.Package, null, null, log.Build(), performance, error);
    }
}
