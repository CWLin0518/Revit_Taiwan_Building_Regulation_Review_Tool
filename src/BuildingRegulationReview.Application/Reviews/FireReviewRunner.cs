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
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
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
            log.Add(ReviewErrorCode.ReviewPerformance, ReviewStage.Review, ReviewSeverity.Info,
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
            .Select(r => WithZoneName(r, set))
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
        UnrelatedOpenings(log, set);
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

    /// <summary>
    /// 每一列有區劃的結果都帶上 <c>zone.name</c>（驗證清單 C-21）。
    /// </summary>
    /// <remarks>
    /// 檢討表的區劃名稱只從存下來的結果重建（<c>ReviewTable.ZoneNames</c>），而原本只有區劃面積、
    /// 垂直區劃、帷幕牆交接、第79條之1 四個 check 會記這一欄。一個樓梯間區劃既不檢討面積、又沒有
    /// 需要檢的門時，四者都不出列，它的構件時效與防火門窗結果就全部沒有名字。名字在這裡統一補，
    /// 不必每個 check 各記一份、也不會因為哪天新增一個 check 而復發；已經記了的（check 自己的值）不動。
    /// </remarks>
    private static ReviewResult WithZoneName(ReviewResult result, CandidateSet set)
    {
        if (result.ZoneId is null || result.Evidence.Find(ZoneNameField) is not null) return result;
        if (!Guid.TryParse(result.ZoneId, out var zoneId) || set.Zone(zoneId) is not { } zone) return result;

        return new ReviewResult(result.ResultId, result.RunId, result.PackageId, result.CheckType,
            result.SubjectUniqueIds, result.ZoneId, result.Status, result.ActualValue, result.RequiredValue,
            result.RuleId, result.RuleVersion, result.LegalReference, result.Message,
            new ReviewEvidence(result.Evidence.Items.Concat(new[]
            {
                new ReviewEvidenceItem(ZoneNameField, ReviewValue.OfText(zone.Name))
            })),
            result.ReviewedBy, result.ReviewedAtUtc);
    }

    private const string ZoneNameField = "zone.name";

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
            log.Add(ReviewErrorCode.ReviewCheckNotRun, ReviewStage.Review, ReviewSeverity.Info,
                request.CurtainWallReader is null
                    ? "本次檢討沒有讀取帷幕牆幾何，帷幕牆區劃交接未檢討。"
                    : "這個工作包沒有 Area Plan，無法讀取帷幕牆幾何，帷幕牆區劃交接未檢討。");
            return Result.Success(new CurtainWallJunctionReview(
                Array.Empty<CurtainWallJunctionFinding>(), Array.Empty<string>()));
        }

        var merged = MergedAtriums.Of(set, request.Inputs.Area);
        var doubts = BoundaryDoubtWalls(set, merged);
        var references = HostLegalReferences(set, area, merged, doubts);

        // 關係存疑的牆以「可能是區劃牆」讀入（驗證清單 C-19 第二層）：它們與帷幕牆的交接處列為人工
        // 覆核。沒有真正交接於帷幕牆的那幾道在檢討表上不會有列，這一行是它們的紀錄。排在 Read 之前：
        // 讀取失敗會終止整個 run，這一行在那種情形下同樣要留在日誌裡。
        BoundaryDoubtWallsRead(log, set, doubts);

        var read = request.CurtainWallReader.Read(new CurtainWallReadRequest(
            set.PackageId, request.Package.AreaPlanUniqueId,
            RequiredRatings(rating, references.Keys), references, request.BareNumberUnit, request.JunctionOptions,
            doubts.ToDictionary(d => d.UniqueId, d => d.Label, StringComparer.Ordinal)));
        if (read.IsFailure) return Result.Failure<CurtainWallJunctionReview>(read.Error);

        // 幾何讀取器講的是它讀到什麼，不是「檢討完成」（驗證清單 B-03）。
        foreach (var warning in read.Value.Warnings)
            log.Add(ReviewErrorCode.CurtainWallReaderWarning, ReviewStage.Review, ReviewSeverity.Warning, warning);

        var junctions = CurtainWallJunctionResolver.Resolve(read.Value, request.JunctionOptions,
            VerticalCompartmentZones(set, request.Inputs.Area));
        return CurtainWallJunctionCheck.Review(set, new CurtainWallJunctionInputs(request.Inputs.Area, junctions),
            engine, request.Context, runId, request.JunctionOptions, newId);
    }

    /// <summary>
    /// The 區劃 whose 用途 is a 第79條之2 垂直區劃, read from the same zone.use the area rules read. A 用途
    /// that cannot be read is not one: the curtain wall in front of it goes to 人工覆核, not to 不適用.
    /// </summary>
    private static IEnumerable<Guid> VerticalCompartmentZones(CandidateSet set, CompartmentAreaInputs inputs) =>
        set.Zones
            .Where(zone => inputs.ForZone(zone.ZoneId)
                .Any(i => string.Equals(i.Field, "zone.use", StringComparison.Ordinal) &&
                          i is { IsUnreadable: false, Value: { Kind: ReviewValueKind.Text } text } &&
                          ZoneUses.IsVerticalCompartment(text.Text.Trim())))
            .Select(zone => zone.ZoneId)
            .ToList();

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
    /// here is what makes the reader read it as a compartment boundary, so every wall the resolver
    /// decided <i>does</i> bound a 區劃, every wall it could not tell (<paramref name="doubts"/>), and
    /// every 樓地板 of the storey, is listed — whether or not a required rating was worked out for it.
    /// </summary>
    /// <remarks>
    /// 驗證清單 C-19：關係存疑的牆（跨越邊界、邊界落在厚度內但偏離中心線、邊界貼著輪廓）原本一律不進
    /// 清單，它們與帷幕牆的交接處一列都不會產生。第二層起它們以「可能是區劃牆」列進來，疑義另由
    /// <see cref="CurtainWallReadRequest.BoundaryDoubts"/> 帶給讀取器——不是逕自當成區劃牆：交接處的
    /// 結論只在「它是區劃牆」的假設下成立，所以一律人工覆核（<c>CurtainWallJunctionCheck</c>）。
    /// 它的條文取它**存疑**的那幾個區劃，與確定的區劃牆取它構成邊界的區劃同一個道理。
    /// <para>
    /// 第83條 is not read from the model: a 區劃 is 第83條's when that is the clause the 區劃面積 rule
    /// decided it under, which is the rule engine's answer and no one else's. A wall between a 第79條
    /// 區劃 and a 第83條 one is recorded as 第79條, the general clause — one junction yields one result,
    /// and it must read the same on every rerun.
    /// </para>
    /// <para>
    /// An outcome whose applicability the engine could not decide names a rule that decided nothing:
    /// it is merely the first rule still in doubt. A 2F 挑空 whose 第79條之2第3項 is undecided is in
    /// doubt under <c>tw-bcr-83-area-atrium</c>, and must not make its walls 第83條's for it
    /// (垂直區劃規格 §3.7). Such a 區劃 falls back to the general clause like any other.
    /// </para>
    /// </remarks>
    private static IReadOnlyDictionary<string, string> HostLegalReferences(
        CandidateSet set, CompartmentAreaReview area, MergedAtriums merged, IReadOnlyList<DoubtWall> doubts)
    {
        // The 垂直區劃 are not reported as area results but were still decided under a clause, and a
        // stairwell of a 第83條 storey keeps its walls 第83條's.
        var article83 = new HashSet<Guid>(area.Findings.Concat(area.VerticalCompartments)
            .Where(f => f.Outcome is { IsApplicabilityUndecided: false } &&
                        f.Result.LegalReference.IndexOf(CurtainWallJunctionReferences.Article83, StringComparison.Ordinal) >= 0)
            .Select(f => f.ZoneId));
        var doubtful = doubts.ToDictionary(d => d.UniqueId, StringComparer.Ordinal);

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
            var zones = doubtful.TryGetValue(uniqueId, out var doubt)
                ? doubt.ZoneIds
                : member.Relations.Where(r => r.IsBoundary).Select(r => r.ZoneId).ToList();

            // The line between a merged 挑空 and its 連通區劃 is no 區劃牆, so it owes the façade no
            // 交接處 either (垂直區劃規格決議 37). A wall in doubt that is not interior is already in
            // doubts with its zones (驗證清單 C-19 第二層).
            if (zones.Count == 0 || merged.IsInterior(member)) continue;
            references[uniqueId] = zones.All(article83.Contains)
                ? CurtainWallJunctionReferences.Article83
                : CurtainWallJunctionReferences.Article79;
        }
        return references;
    }

    /// <summary>
    /// 一道「可能是區劃牆」的牆：它與任何區劃的關係都沒有確定構成邊界，但至少對一個區劃掛著邊界疑義
    /// （驗證清單 C-19 第二層）。
    /// </summary>
    private sealed class DoubtWall
    {
        public DoubtWall(string uniqueId, string? typeName, IReadOnlyList<string> kinds, IReadOnlyList<Guid> zoneIds)
        {
            UniqueId = uniqueId;
            TypeName = typeName;
            Kinds = kinds;
            ZoneIds = zoneIds;
        }

        public string UniqueId { get; }
        public string? TypeName { get; }

        /// <summary>疑義種類，排序且不重複——一道牆可能同時跨越 A 區邊界又在 B 區偏離中心線。</summary>
        public IReadOnlyList<string> Kinds { get; }

        /// <summary>它存疑的那幾個區劃：若它是區劃牆，就是這幾個區劃的區劃牆。</summary>
        public IReadOnlyList<Guid> ZoneIds { get; }

        public string Label => string.Join("／", Kinds);
    }

    /// <summary>
    /// 與區劃的關係只是存疑的牆（驗證清單 C-19）。帷幕牆不算：它是交接的另一側，不是區劃牆；決議 37 的
    /// 挑空分界牆也不算：法規已經答過它不是區劃邊界。確定構成某個區劃邊界的牆同樣不算——它已經是區劃牆，
    /// 對另一個區劃的疑義不改變這件事。
    /// </summary>
    /// <remarks>
    /// 順序不是美觀問題：同一個模型每次 run 的日誌要讀起來一樣，件數多時被點名與列進技術細節的也必須是
    /// 同幾道。這裡不另外排序——<c>CandidateSet.Members</c> 已經以 Category 再
    /// <c>CandidateOrdering.Sources</c>（ElementUniqueId 的 string.CompareOrdinal）排好，而這裡只取 Wall
    /// 一種 Category，所以逐出來就是 UniqueId 的 ordinal 順序。
    /// </remarks>
    private static IReadOnlyList<DoubtWall> BoundaryDoubtWalls(CandidateSet set, MergedAtriums merged) =>
        set.Members
            .Where(m => m.Category == CandidateCategory.Wall &&
                        !m.Observation.IsCurtainWall &&
                        !m.Relations.Any(r => r.IsBoundary) &&
                        !merged.IsInterior(m) &&
                        m.Relations.Any(BoundaryDoubts.Carries))
            .Select(m => new DoubtWall(
                m.Observation.Source.ElementUniqueId,
                m.Observation.TypeName,
                m.Relations.Select(r => BoundaryDoubts.Label(r.Ambiguity)).OfType<string>()
                    .Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList(),
                m.Relations.Where(BoundaryDoubts.Carries).Select(r => r.ZoneId).Distinct().ToList()))
            .ToList();

    /// <summary>
    /// 以「可能是區劃牆」讀入帷幕牆交接的牆，一行總結（驗證清單 C-19；第一層點名、第二層起改為總結）。
    /// </summary>
    /// <remarks>
    /// 它們與帷幕牆的交接處已經是檢討表上的人工覆核列
    /// （<see cref="ReviewErrorCode.CandidateBoundaryDoubtJunction"/>），所以這一行是資訊，不是警告。
    /// 仍要它，是因為沒有真正交接於帷幕牆的那幾道在檢討表上不會有列，審查者要能從日誌看出
    /// 「這幾道牆的身分工具沒有定」。
    /// <para>
    /// 零噪音有兩道閘門。第一道是**這一層得有帷幕牆**：沒有帷幕牆就沒有交接處，而
    /// <c>BoundaryOffCenterline</c> 一點都不罕見（挑空以 room separation line 封邊時就是常態），少了這道
    /// 閘門會在正常模型上變成每次 run 固定一行。第二道是**真的有這種牆**才出聲。
    /// </para>
    /// <para>
    /// 件數多時比照 <see cref="ReviewErrorCode.CandidateOpeningNotOnBoundary"/>：訊息只點前幾個 Id、
    /// 其餘以「等 N 道」帶過，technicalDetail 也只列前 20 道——<c>ReviewLogEntry</c> 不截斷，而 MCP 的
    /// log digest 會把整串帶出去。
    /// </para>
    /// </remarks>
    private static void BoundaryDoubtWallsRead(ReviewLog.Builder log, CandidateSet set, IReadOnlyList<DoubtWall> doubts)
    {
        if (!set.Members.Any(m => m.Observation.IsCurtainWall)) return;
        if (doubts.Count == 0) return;

        const int Named = 3;
        const int Detailed = 20;

        // 「其中」＋那句但書：一道牆可能同時屬於兩種，括號內加總會大於前面的牆數。
        var byKind = string.Join("、", doubts
            .SelectMany(w => w.Kinds)
            .GroupBy(k => k, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key} {g.Count()} 道"));
        var list = string.Join("、", doubts.Take(Named).Select(w => $"{w.UniqueId}（{w.Label}）"));
        var more = doubts.Count > Named ? $" 等 {doubts.Count} 道" : string.Empty;
        var detailed = doubts.Count > Detailed ? $"前 {Detailed} 道" : "全部";

        log.Add(ReviewErrorCode.CandidateBoundaryDoubtNotJunctionRead, ReviewStage.Review, ReviewSeverity.Info,
            $"帷幕牆區劃交接：{doubts.Count} 道牆與區劃的關係存疑（其中 {byKind}；" +
            "一道牆可能同時屬於兩種），以「可能是區劃牆」讀入：" +
            $"{list}{more}。它們與帷幕牆的交接處列為人工覆核，結論只在它是區劃牆時成立；" +
            "沒有交接於帷幕牆的不出列。請人工確認這幾道牆是否為區劃牆：若是，請把 Area 邊界對齊到牆的中心線後重新檢討" +
            $"（技術細節列出{detailed}的 UniqueId 與關係種類）。",
            technicalDetail: string.Join("; ", doubts.Take(Detailed).Select(w =>
                $"{w.UniqueId}({string.Join("/", w.Kinds)}{(w.TypeName is null ? string.Empty : ";" + w.TypeName)})")));
    }

    /// <summary>
    /// 讀到了、但不是任何區劃候選的開口，一行總結（驗證清單 C-16）。
    /// </summary>
    /// <remarks>
    /// 這些開口原本只進候選元素總數一個數字，而「開口因位置讀錯而假性落空」與「開口本來就不在
    /// 區劃邊界上」在那個數字上長得一模一樣——實機就是一扇帷幕牆門因位置回退到含門扇的外接矩形而
    /// 整扇消失，檢討表、日誌、統計三邊都沒有一句話。多數情形這一行報的是預期結果（第79條第1項
    /// 只問區劃邊界上的開口），所以它只報總結與分類計數，不逐件列成檢討表的一列；件數與身分都留在
    /// 日誌裡，下一次同類問題才驗得出來。真正判不出來的那些（位置偏離 host 位置線的）已經由
    /// <c>CandidateAmbiguityKind.OpeningOffHostCenterline</c> 列為人工覆核，不在這一行裡。
    /// </remarks>
    private static void UnrelatedOpenings(ReviewLog.Builder log, CandidateSet set)
    {
        if (set.UnrelatedOpenings.Count == 0) return;

        var byCategory = string.Join("、", set.UnrelatedOpenings
            .GroupBy(o => o.Category)
            .OrderBy(g => g.Key)
            .Select(g => $"{CandidateCategories.Label(g.Key)} {g.Count()} 件"));
        var inside = set.UnrelatedOpenings.Count(o => o.IsInsideAZone);
        var measured = set.UnrelatedOpenings
            .Where(o => o.NearestBoundaryDistanceFeet.HasValue)
            .Select(o => o.NearestBoundaryDistanceFeet!.Value)
            .ToList();
        double? nearest = measured.Count == 0 ? null : measured.Min();

        log.Add(ReviewErrorCode.CandidateOpeningNotOnBoundary, ReviewStage.Review, ReviewSeverity.Warning,
            $"防火門窗：{set.UnrelatedOpenings.Count} 件開口不在任何區劃邊界上，未列入檢討（{byCategory}；" +
            $"其中 {inside} 件位於區劃內部" +
            (nearest is double feet ? $"；最近者距邊界 {Meters(feet)} m" : string.Empty) + "）。" +
            "第79條第1項只問區劃邊界上的開口，所以這通常是預期結果；若其中有本該檢討的，" +
            "請確認它的 Host 牆與平面位置（技術細節列出前 20 件的 UniqueId）。",
            technicalDetail: string.Join("; ", set.UnrelatedOpenings.Take(20)
                .Select(o => $"{o.Source.ElementUniqueId}({CandidateCategories.Label(o.Category)}{(o.TypeName is null ? string.Empty : "/" + o.TypeName)})")));
    }

    private static string Meters(double feet) =>
        PlanUnits.FeetToMeters(feet).ToString("0.###", CultureInfo.InvariantCulture);

    private static void Findings(
        ReviewLog.Builder log,
        CompartmentAreaReview area,
        Article79_1ExemptionReview exemption,
        FireResistanceReview rating,
        OpeningProtectionReview opening,
        CurtainWallJunctionReview junction,
        VerticalCompartmentReview shaft)
    {
        // 驗證清單 C-02：六個檢查的警告原本先 Concat 成一串、再跨來源 Distinct，有兩個後果。一是讀者
        // 看不出一條警告是哪個檢查提出的——「某個參數讀不到」要去哪裡補，取決於是區劃面積在抱怨還是
        // 防火門窗在抱怨。二是跨來源去重會把兩個不同檢查吐出的同文警告併成一條，於是其中一個檢查在
        // 日誌上完全沒有聲音，讀者會以為只有一個檢查遇到麻煩。改成逐來源處理：警告冠上該檢查在檢討表
        // 上的分節標題，去重只在同一個來源內做（同一個檢查重複吐同一句仍只記一條，不同檢查的同一句
        // 各記一條）。沿用 BCR-RUN-006 不另立碼，因為「哪個檢查」是訊息內容而非錯誤類別，碼的語意
        // （檢討項目警告）本來就涵蓋六個檢查。標題取自 ReviewTable.Title，與檢討表分節同一份字串，
        // 不在這裡另造中文名稱——兩邊各寫一份遲早會對不起來。
        var warningSources = new[]
        {
            (CheckType: ReviewCheckTypes.CompartmentArea, Warnings: area.Warnings),
            (CheckType: ReviewCheckTypes.AreaExemption, Warnings: exemption.Warnings),
            (CheckType: ReviewCheckTypes.FireResistance, Warnings: rating.Warnings),
            (CheckType: ReviewCheckTypes.OpeningProtection, Warnings: opening.Warnings),
            (CheckType: ReviewCheckTypes.CompartmentContinuity, Warnings: junction.Warnings),
            (CheckType: ReviewCheckTypes.VerticalCompartment, Warnings: shaft.Warnings)
        };
        foreach (var source in warningSources)
        foreach (var warning in source.Warnings.Distinct(StringComparer.Ordinal))
            log.Add(ReviewErrorCode.ReviewCheckWarning, ReviewStage.Review, ReviewSeverity.Warning,
                $"〔{ReviewTable.Title(source.CheckType)}〕{warning}");

        // 垂直區劃規格 §7: all four rows are logged even when a row is empty, because a 管道間 whose
        // 維修門 was never modelled — or a storey with no 挑空 — reads the same as a row that was not
        // reviewed unless the count says 0. The fourth row is 第3項, so the line cites the article
        // rather than 第1項 alone (§7.3).
        if (shaft.Findings.Count > 0)
        {
            log.Add(ReviewErrorCode.ReviewCheckSummary, ReviewStage.Review, ReviewSeverity.Info,
                "垂直區劃（第79條之2）：" + string.Join("；", shaft.Groups.Select(g =>
                    $"{g.Label} {g.DeviceCount} 件{ReviewStatusText.Label(g.Status)}")) + "。");
        }

        var findings = area.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.Zone.AreaUniqueIds.FirstOrDefault()))
            .Concat(exemption.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.ElementUniqueId)))
            .Concat(rating.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.ElementUniqueId)))
            .Concat(opening.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.ElementUniqueId)))
            .Concat(junction.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.Junction.CurtainWallUniqueId)))
            .Concat(shaft.Findings.Select(f => (f.Result, f.ErrorCode, Element: (string?)f.ElementUniqueId)));
        var inferred = opening.Findings.Count(f => f.ErrorCode == ReviewErrorCode.CandidateFacadeInferred);
        if (inferred > 0)
        {
            log.Add(ReviewErrorCode.CandidateFacadeInferred, ReviewStage.Review, ReviewSeverity.Warning,
                $"防火門窗：{inferred} 件帷幕牆開口因帷幕牆另一側沒有任何區劃而推定為外牆，判「不適用」。" +
                $"另一側若是未建 Area 或屬其他工作包的室內空間，判定即不成立；請在檢討表搜尋「{OpeningProtectionCheck.FacadeInferredTag}」抽查。");
        }

        foreach (var (result, code, element) in findings)
        {
            if (code is null && result.Status != ReviewStatus.Fail) continue;

            // 驗證清單 C-01：log.warnings 要回答的是「工具有沒有遇到麻煩」，判定的統計則由檢討表負責。
            // 結果自己帶錯誤碼，就是某個檢查在抱怨它讀到的輸入（缺參數、時效讀不出來、幾何歧義、面積
            // 來源無法確認），那才是工具的麻煩，不論它最後判成資料不足、人工覆核還是未符合，一律升為
            // Warning，ReviewLogDigest 依嚴重度取樣的前 20 筆才會先端出它們。沒有自己錯誤碼的（只會在
            // Fail 時走到這裡）純粹是判定，記 Info——否則幾百筆未符合會把上面那些資料問題全部擠掉。
            log.Add(code ?? ReviewErrorCode.ReviewFinding, ReviewStage.Review,
                code is not null ? ReviewSeverity.Warning : ReviewSeverity.Info,
                $"{ReviewTable.Title(result.CheckType)}「{ReviewStatusText.Label(result.Status)}」：{result.Message}",
                elementUniqueId: element,
                technicalDetail: $"Result {result.ResultId:D}; Rule {result.RuleId} v{result.RuleVersion}; Zone {result.ZoneId}");
        }
    }

    private static void CarryOverLog(ReviewLog.Builder log, OverrideCarryOverReport report)
    {
        foreach (var entry in report.Entries)
        {
            // 驗證清單 C-01：三種結局三個碼。Kept 與 Dropped 共用一個碼時，日誌會出現「碼寫沿用、訊息寫
            // 不再沿用」的列——那正是 C-01 要消滅的錯配。每個結局逐一列出、剩下的丟例外，是為了讓將來
            // 新增結局時當場炸開，而不是默默掛到某個語意不符的碼上。
            var (code, severity) = entry.Outcome switch
            {
                OverrideCarryOverOutcome.Kept => (ReviewErrorCode.OverrideCarriedOver, ReviewSeverity.Info),
                OverrideCarryOverOutcome.NeedsReconfirmation => (ReviewErrorCode.OverrideNeedsReconfirmation, ReviewSeverity.Warning),
                OverrideCarryOverOutcome.Dropped => (ReviewErrorCode.OverrideDropped, ReviewSeverity.Info),
                _ => throw new InvalidOperationException($"人工覆寫沿用結局 {entry.Outcome} 沒有對應的錯誤碼。")
            };

            log.Add(code, ReviewStage.Review, severity,
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
