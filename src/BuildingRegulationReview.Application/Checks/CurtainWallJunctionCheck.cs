using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>The 帷幕牆區劃交接 verdict for one junction.</summary>
public sealed class CurtainWallJunctionFinding
{
    internal CurtainWallJunctionFinding(
        CurtainWallJunction junction,
        ReviewResult result,
        RuleOutcome? outcome,
        string? errorCode)
    {
        Junction = junction;
        Result = result;
        Outcome = outcome;
        ErrorCode = errorCode;
    }

    public CurtainWallJunction Junction { get; }
    public string JunctionId => Junction.JunctionId;
    public CurtainWallJunctionKind Kind => Junction.Kind;
    public Guid ZoneId => Junction.ZoneId;
    public ReviewResult Result { get; }
    public ReviewStatus Status => Result.Status;

    /// <summary>What the rule engine said, or null when no rule ran (a doubt, or a zone in doubt).</summary>
    public RuleOutcome? Outcome { get; }

    /// <summary>Why the geometry handed over no facts, when that is what happened.</summary>
    public CurtainWallJunctionDoubt? Doubt => Junction.Doubt;

    /// <summary>The spec 14 code a log entry about this junction carries; null for a routine verdict.</summary>
    public string? ErrorCode { get; }

    /// <summary>True when 但書 carried the junction — 得免突出, which spec 11.7 counts towards 符合.</summary>
    public bool IsExempt => Outcome?.Reason == RuleOutcomeReason.Exempt;

    public override string ToString() => $"{JunctionId}: {ReviewStatusText.Label(Status)} — {Result.Message}";
}

/// <summary>One row of the 帷幕牆 statistics: every junction of one kind (docs §7.2).</summary>
public sealed class CurtainWallJunctionGroupSummary
{
    internal CurtainWallJunctionGroupSummary(CurtainWallJunctionKind kind, IEnumerable<CurtainWallJunctionFinding> findings)
    {
        Kind = kind;
        Findings = new ReadOnlyCollection<CurtainWallJunctionFinding>(findings.ToList());
        Status = ReviewStatusSeverity.Worst(Findings.Select(f => f.Status));
        LegalReferences = new ReadOnlyCollection<string>(Findings
            .Select(f => f.Junction.HostLegalReference).Where(x => x is not null).Select(x => x!)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    public CurtainWallJunctionKind Kind { get; }
    public IReadOnlyList<CurtainWallJunctionFinding> Findings { get; }
    public int JunctionCount => Findings.Count;

    /// <summary>Fail when any fails, otherwise the most doubtful state; 未檢討 when the row is empty.</summary>
    public ReviewStatus Status { get; }

    /// <summary>Which clauses put these compartments there — 第79條 and 第83條 are counted apart (docs §2.5).</summary>
    public IReadOnlyList<string> LegalReferences { get; }

    public int Count(ReviewStatus status) => Findings.Count(f => f.Status == status);

    public int CountOf(string legalReference) =>
        Findings.Count(f => string.Equals(f.Junction.HostLegalReference, legalReference, StringComparison.Ordinal));

    public override string ToString() => $"{CurtainWallJunctionKinds.Label(Kind)} ×{JunctionCount}：{ReviewStatusText.Label(Status)}";
}

/// <summary>The 帷幕牆區劃交接 results of one run.</summary>
public sealed class CurtainWallJunctionReview
{
    internal CurtainWallJunctionReview(IEnumerable<CurtainWallJunctionFinding> findings, IEnumerable<string> warnings)
    {
        Findings = new ReadOnlyCollection<CurtainWallJunctionFinding>(findings.ToList());
        Groups = new ReadOnlyCollection<CurtainWallJunctionGroupSummary>(CurtainWallJunctionKinds.All
            .Select(k => new CurtainWallJunctionGroupSummary(k, Findings.Where(f => f.Kind == k))).ToList());
        Warnings = new ReadOnlyCollection<string>(warnings.ToList());
    }

    /// <summary>One per junction, in zone, kind and junction order.</summary>
    public IReadOnlyList<CurtainWallJunctionFinding> Findings { get; }

    public IEnumerable<ReviewResult> Results => Findings.Select(x => x.Result);

    /// <summary>The three 檢討表 rows, always all three, in CW-H, CW-V, CW-O order.</summary>
    public IReadOnlyList<CurtainWallJunctionGroupSummary> Groups { get; }

    public IReadOnlyList<string> Warnings { get; }

    public CurtainWallJunctionGroupSummary Group(CurtainWallJunctionKind kind) => Groups.First(g => g.Kind == kind);

    public CurtainWallJunctionFinding? For(string junctionId) =>
        Findings.FirstOrDefault(f => string.Equals(f.JunctionId, junctionId, StringComparison.Ordinal));

    public int Count(ReviewStatus status) => Findings.Count(x => x.Status == status);
}

/// <summary>
/// 防火區劃與帷幕牆交接 (docs/regulations/curtain-wall-fire-compartment.md). For every junction the
/// geometry layer resolved:
/// <list type="number">
/// <item>A junction of a zone whose extent is in doubt (未封閉、重疊) is 人工覆核; so is one the
/// geometry could not measure — a curved wall, an intersection that did not resolve, a band a grid
/// line cut in two. A 連跨複數樓層 space is 不適用 instead: it belongs to 第79條之2, and the evidence
/// says so. No rule runs on any of them.</item>
/// <item>Otherwise the facts are the junction's kind, identity and measurements, plus the building
/// and zone inputs. The rules are written as the statute is — 應突出五十公分以上 as the requirement,
/// 但……得免突出 as the exemption — so the engine gives the right six-state answer without this check
/// special-casing anything: 突出達標 passes even when the band could not be measured, 但書成立 is
/// 不適用／得免突出, and a band that could not be measured is 資料不足, never 未符合 (spec 11.3).</item>
/// <item>「具同等以上防火時效」 is not a second test: a continuous run only ever counts panels that
/// reach the host's required rating, so the 90 cm band is one quantity against one threshold.</item>
/// <item>The results are also gathered into the three 檢討表 rows, CW-H counting 第79條 and 第83條
/// junctions apart (docs §7.2).</item>
/// </list>
/// </summary>
public static class CurtainWallJunctionCheck
{
    private const string MinRatingField = "junction.minFireRating";
    private const string PanelKindField = "junction.panelKind";
    private const string MinProtectionField = "junction.minFireProtection";
    private const double MillimetersPerMeter = 1000.0;

    /// <summary>The 90 cm bands, whose absence the missing panel rating explains (docs §12 輸入契約).</summary>
    private static readonly string[] MeasurementFields =
        { "junction.continuousFireRatedLength", "junction.continuousFireRatedHeight" };

    public static Result<CurtainWallJunctionReview> Review(
        CandidateSet set,
        CurtainWallJunctionInputs inputs,
        RuleEngine engine,
        RuleEvaluationContext context,
        Guid runId,
        CurtainWallJunctionOptions? options = null,
        Func<Guid>? newResultId = null)
    {
        if (set is null) throw new ArgumentNullException(nameof(set));
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));
        if (engine is null) throw new ArgumentNullException(nameof(engine));
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
        options ??= CurtainWallJunctionOptions.Default;
        newResultId ??= Guid.NewGuid;

        var ready = ReviewPreconditions.Zones(set, "帷幕牆區劃交接");
        if (ready.IsFailure) return Result.Failure<CurtainWallJunctionReview>(ready.Error);

        var warnings = inputs.Context.ZoneIds.Where(id => set.Zone(id) is null)
            .Select(id => $"輸入資料指定的區劃 {id:D} 不在此工作包中，已略過。")
            .Concat(inputs.Junctions.Where(j => set.Zone(j.ZoneId) is null)
                .Select(j => $"交接處「{j.JunctionId}」所屬的區劃 {j.ZoneId:D} 不在此工作包中，已略過。"))
            .ToList();

        var findings = new List<CurtainWallJunctionFinding>();
        foreach (var junction in inputs.Junctions)
        {
            var zone = set.Zone(junction.ZoneId);
            if (zone is null) continue;

            findings.Add(
                !zone.IsClear ? Withhold(set, zone, junction, engine.RuleSet, runId, newResultId())
                : junction.Doubt is not null ? Observe(set, zone, junction, engine.RuleSet, runId, newResultId())
                : Decide(set, zone, junction, inputs, engine, context, runId, options, newResultId()));
        }

        return Result.Success(new CurtainWallJunctionReview(findings, warnings));
    }

    private static CurtainWallJunctionFinding Decide(
        CandidateSet set,
        CandidateZone zone,
        CurtainWallJunction junction,
        CurtainWallJunctionInputs inputs,
        RuleEngine engine,
        RuleEvaluationContext context,
        Guid runId,
        CurtainWallJunctionOptions options,
        Guid resultId)
    {
        var facts = CandidateFacts.ForZone(set, zone, engine.RuleSet.Catalog);
        facts.Set("junction.kind", CurtainWallJunctionKinds.RuleText(junction.Kind));
        facts.Set("junction.zoneId", zone.ZoneIdText);
        facts.Set("junction.curtainWallUniqueId", junction.CurtainWallUniqueId);
        if (junction.HostUniqueId is not null) facts.Set("junction.hostUniqueId", junction.HostUniqueId);
        if (junction.HostLegalReference is not null) facts.Set("junction.hostLegalReference", junction.HostLegalReference);
        if (junction.HostRequiredFireRatingMinutes is double required) facts.Set("junction.hostRequiredFireRating", required, ReviewUnit.Minute);
        if (junction.ProjectionDepthMm is double projection) facts.Set("junction.projectionDepth", projection / MillimetersPerMeter, ReviewUnit.Meter);
        if (junction.ContinuousFireRatedLengthMm is double length) facts.Set("junction.continuousFireRatedLength", length / MillimetersPerMeter, ReviewUnit.Meter);
        if (junction.ContinuousFireRatedHeightMm is double height) facts.Set("junction.continuousFireRatedHeight", height / MillimetersPerMeter, ReviewUnit.Meter);
        if (junction.HasUnprotectedOpening is bool opening) facts.Set("junction.hasUnprotectedOpening", opening);

        var provided = junction.MinFireRating;
        switch (provided?.Kind)
        {
            case ProvidedFireRatingKind.Rated:
                facts.Set(MinRatingField, provided!.Minutes!.Value, ReviewUnit.Minute);
                break;
            case ProvidedFireRatingKind.Unreadable:
            case ProvidedFireRatingKind.Undeterminable:
                facts.MarkUnreadable(MinRatingField, $"「{provided!.RawText}」{provided.Reason}");
                break;
        }

        // CW-O 分兩路（決議 16）。種類沒宣告時 panelKind 刻意不供值：兩條 CW-O 規則的適用條件都算不出來，
        // 引擎自己會答「資料不足，無法判定規則是否適用」並指出缺的欄位，不必在這裡特判。
        if (junction.PanelAnswerKind is string panelKind) facts.Set(PanelKindField, panelKind);

        var protection = junction.MinFireProtection;
        switch (protection?.Kind)
        {
            case ProvidedFireProtectionKind.Yes:
            case ProvidedFireProtectionKind.No:
                facts.Set(MinProtectionField, protection!.RuleText!);
                break;
            case ProvidedFireProtectionKind.Unreadable:
                facts.MarkUnreadable(MinProtectionField, $"「{protection!.RawText}」{protection.Reason}");
                break;
        }

        var supplied = inputs.Context.Building.Concat(inputs.Context.ForZone(zone.ZoneId)).ToList();
        foreach (var input in supplied) input.ApplyTo(facts);

        var outcome = engine.Evaluate(RuleCategory.CompartmentContinuity, facts, context);
        var subject = Subject(junction, zone);
        var status = outcome.Status;
        var message = $"{subject}：{outcome.Message}";
        var errorCode = RuleOutcomeErrorCode.For(outcome);

        // 資料不足時，缺的多半就是 防火檢討_設計防火時效：要麼規則直接讀不到它（CW-O），要麼幾何層
        // 因為它而量不出 90 cm 帶（CW-H、CW-V 的輸入契約）。三者讀的是同一個參數，只是元素不同——
        // CW-H 讀立面內的實體外牆，另兩項讀嵌板（決議 13），所以訊息的主詞要跟著 kind 走。
        var ratingGap = outcome.Gaps.FirstOrDefault(g => g.Field == MinRatingField);
        var measurementGap = ratingGap is not null || outcome.Gaps.Any(g => MeasurementFields.Contains(g.Field, StringComparer.Ordinal));
        if (status == ReviewStatus.InsufficientData && measurementGap && provided is { Kind: not ProvidedFireRatingKind.Rated })
        {
            // A composite construction the rules read directly: like FireResistanceCheck's 「1hr/2hr」,
            // a person has to say which layer answers here — it is not a data gap they can fill.
            if (provided.Kind == ProvidedFireRatingKind.Undeterminable && ratingGap is not null && outcome.Gaps.Count == 1)
            {
                status = ReviewStatus.ManualReview;
                message = $"{subject}：{RatingSubject(junction.Kind)}「{provided.RawText}」{provided.Reason}，需人工覆核是否達到要求" +
                          (outcome.RequiredValue is { } need ? $" {FireRatingText.Format(need.Number)}。" : "。");
                errorCode = ReviewErrorCode.FireRatingUndetermined;
            }
            else
            {
                errorCode ??= provided.Kind switch
                {
                    ProvidedFireRatingKind.Missing => ReviewErrorCode.ParameterMissing,
                    ProvidedFireRatingKind.Undeterminable => ReviewErrorCode.FireRatingUndetermined,
                    _ => ReviewErrorCode.ParameterTypeMismatch
                };
            }
        }

        // 決議 16 的兩個新缺口。種類沒宣告時，缺的不是時效也不是防火保護，而是「該讀哪一個」，所以訊息
        // 要指名參數——使用者在面板上補得起來，但前提是知道要補什麼。
        if (status == ReviewStatus.InsufficientData && outcome.Gaps.Any(g => g.Field == PanelKindField))
        {
            message = $"{subject}：嵌板型別未宣告 {CurtainPanelKindParameters.Provided}（{CurtainPanelKinds.SolidText}／" +
                      $"{CurtainPanelKinds.GlazedText}），無法判定應以設計防火時效或設計防火保護作答。";
            errorCode ??= ReviewErrorCode.ParameterMissing;
        }
        else if (status == ReviewStatus.InsufficientData &&
                 outcome.Gaps.Any(g => g.Field == MinProtectionField) &&
                 protection is not null)
        {
            errorCode ??= protection.Kind == ProvidedFireProtectionKind.Missing
                ? ReviewErrorCode.ParameterMissing
                : ReviewErrorCode.ParameterTypeMismatch;
        }

        var evidence = Merge(
            outcome.Evidence.Items,
            JunctionEvidence(junction, zone),
            ProvidedEvidence(provided),
            ProvidedEvidence(protection),
            OptionEvidence(options),
            InputEvidence(supplied),
            GapEvidence(outcome));

        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.CompartmentContinuity,
            junction.SubjectUniqueIds, zone.ZoneIdText, status, outcome.ActualValue, outcome.RequiredValue,
            outcome.RuleId, outcome.RuleVersion, outcome.LegalReference, message, evidence);
        return new CurtainWallJunctionFinding(junction, result, outcome, errorCode);
    }

    /// <summary>
    /// A junction the geometry could not hand over (docs §3.4). Like the engine's NoRule outcome, the
    /// result is attributed to the rule set itself: no rule decided it.
    /// </summary>
    private static CurtainWallJunctionFinding Observe(
        CandidateSet set, CandidateZone zone, CurtainWallJunction junction, CompiledRuleSet ruleSet, Guid runId, Guid resultId)
    {
        var doubt = junction.Doubt!;
        var evidence = Merge(JunctionEvidence(junction, zone), DoubtEvidence(doubt));

        var info = ruleSet.RuleSet;
        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.CompartmentContinuity,
            junction.SubjectUniqueIds, zone.ZoneIdText, doubt.Status, null, null,
            info.RuleSetId, info.Version, $"規則集「{info.Title}」",
            $"{Subject(junction, zone)}：{doubt.Message}", evidence);
        return new CurtainWallJunctionFinding(junction, result, null, doubt.ErrorCode);
    }

    /// <summary>
    /// A junction of a zone whose extent is in doubt: whether the boundary reaches the curtain wall
    /// here at all, and so whether the clause applies, cannot be relied on.
    /// </summary>
    private static CurtainWallJunctionFinding Withhold(
        CandidateSet set, CandidateZone zone, CurtainWallJunction junction, CompiledRuleSet ruleSet, Guid runId, Guid resultId)
    {
        var evidence = Merge(
            JunctionEvidence(junction, zone),
            junction.Doubt is null ? Enumerable.Empty<ReviewEvidenceItem>() : DoubtEvidence(junction.Doubt),
            new[] { new ReviewEvidenceItem("zone.problems", ReviewValue.OfText(string.Join(",", zone.Problems))) });

        var info = ruleSet.RuleSet;
        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.CompartmentContinuity,
            junction.SubjectUniqueIds, zone.ZoneIdText, ReviewStatus.ManualReview, null, null,
            info.RuleSetId, info.Version, $"規則集「{info.Title}」",
            $"{Subject(junction, zone)}不檢討：區劃「{zone.Name}」的範圍有問題（{string.Join("、", zone.Problems)}），" +
            "無法確認區劃邊界與帷幕牆的關係，需人工覆核。",
            evidence);
        return new CurtainWallJunctionFinding(junction, result, null, ReviewErrorCode.CandidateZoneUnusable);
    }

    private static string Subject(CurtainWallJunction junction, CandidateZone zone) => junction.Kind switch
    {
        CurtainWallJunctionKind.WallToCurtainWall =>
            $"帷幕牆（{junction.CurtainWallUniqueId}）與區劃牆（{junction.HostUniqueId}）之交接處" +
            $"{Reference(junction)}於區劃「{zone.Name}」",
        // 沒有樓板與它交接的那一列（連跨、或交人工覆核）不能說「與區劃樓地板之層間交接處」：那正是它沒有的東西。
        CurtainWallJunctionKind.FloorToCurtainWall when junction.Doubt?.Kind is
                CurtainWallJunctionDoubtKind.VerticalCompartmentSpace or CurtainWallJunctionDoubtKind.FloorNotMeetingCurtainWall =>
            $"帷幕牆（{junction.CurtainWallUniqueId}）於本層之層間交接處於區劃「{zone.Name}」",
        CurtainWallJunctionKind.FloorToCurtainWall =>
            $"帷幕牆（{junction.CurtainWallUniqueId}）與區劃樓地板（{junction.HostUniqueId}）之層間交接處於區劃「{zone.Name}」",
        _ => $"帷幕牆（{junction.CurtainWallUniqueId}）其他部分嵌板於區劃「{zone.Name}」"
    };

    private static string Reference(CurtainWallJunction junction) =>
        junction.HostLegalReference is null ? string.Empty : $"（{junction.HostLegalReference}）";

    /// <summary>
    /// Whose 設計防火時效 <c>junction.minFireRating</c> is, in the words that send the user to the right
    /// element: CW-H reads it off the solid exterior walls in the façade (決議 13), the other two off
    /// the curtain panels.
    /// </summary>
    private static string RatingSubject(CurtainWallJunctionKind kind) =>
        kind == CurtainWallJunctionKind.WallToCurtainWall ? "交接處實體外牆之設計防火時效" : "嵌板設計防火時效";

    /// <summary>
    /// Evidence the rules did not already record: the engine only keeps what the deciding rule read,
    /// so a junction still has to say which it is and what it covers. <see cref="Merge"/> lets the
    /// rules' own values win wherever the two overlap.
    /// </summary>
    private static IEnumerable<ReviewEvidenceItem> JunctionEvidence(CurtainWallJunction junction, CandidateZone zone)
    {
        yield return new ReviewEvidenceItem("junction.id", ReviewValue.OfText(junction.JunctionId));
        yield return new ReviewEvidenceItem("junction.kind", ReviewValue.OfText(CurtainWallJunctionKinds.RuleText(junction.Kind)));

        // Which curtain wall's plane a CW-V band lies on, so the 層間帶 of §7.1 can be drawn in the
        // elevation that shows that wall — the markup plan never re-reads the model to find out.
        yield return new ReviewEvidenceItem("junction.curtainWallUniqueId", ReviewValue.OfText(junction.CurtainWallUniqueId));

        // The 檢討表 splits CW-H by 第79條／第83條 (docs §7.2), and it is rebuilt from the stored
        // results alone — so the clause is recorded even for a junction no rule decided.
        if (junction.HostLegalReference is not null)
            yield return new ReviewEvidenceItem("junction.hostLegalReference", ReviewValue.OfText(junction.HostLegalReference));
        yield return new ReviewEvidenceItem("zone.name", ReviewValue.OfText(zone.Name));

        // §7.1 marks CW-H at its intersection and CW-V over its 層間帶, and the markup plan is rebuilt
        // from the stored results alone — so where the junction is has to travel with them.
        if (junction.Placement is not null)
            yield return new ReviewEvidenceItem("junction.placement", ReviewValue.OfText(junction.Placement.ToEvidenceText()));
        yield return new ReviewEvidenceItem("junction.panelCount", ReviewValue.Quantity(junction.PanelUniqueIds.Count, ReviewUnit.Count));
        if (junction.PanelUniqueIds.Count > 0)
            yield return new ReviewEvidenceItem("junction.panels", ReviewValue.OfText(string.Join(",", junction.PanelUniqueIds)));

        // CW-H 的但書長度是這幾道實體外牆量出來的（決議 13）。要能回答「這個 900 mm 是哪一段外牆」，
        // 證據就得記下它們——嵌板清單答的是另一個問題（帶內有哪些嵌板）。
        if (junction.FacadeWallUniqueIds.Count > 0)
            yield return new ReviewEvidenceItem("junction.facadeWallUniqueIds",
                ReviewValue.OfText(string.Join(",", junction.FacadeWallUniqueIds)));
    }

    private static IEnumerable<ReviewEvidenceItem> ProvidedEvidence(ProvidedFireRating? provided)
    {
        if (provided is null) yield break;

        yield return new ReviewEvidenceItem("provided.kind", ReviewValue.OfText(provided.Kind.ToString()));
        yield return new ReviewEvidenceItem("provided.parameter", ReviewValue.OfText(FireRatingParameters.Provided));
        if (provided.RawText is not null)
            yield return new ReviewEvidenceItem("provided.raw", ReviewValue.OfText(provided.RawText));
        if (provided.Reason is not null)
            yield return new ReviewEvidenceItem("provided.reason", ReviewValue.OfText(provided.Reason));
    }

    /// <summary>
    /// CW-O 玻璃那一路讀到什麼（決議 16）。欄位名與時效那組刻意分開（<c>protection.*</c>），因為同一列
    /// 只會有一組——審查者看到哪一組，就知道這一列是以時效還是以防火設備作答的。
    /// </summary>
    private static IEnumerable<ReviewEvidenceItem> ProvidedEvidence(ProvidedFireProtection? protection)
    {
        if (protection is null) yield break;

        yield return new ReviewEvidenceItem("protection.kind", ReviewValue.OfText(protection.Kind.ToString()));
        yield return new ReviewEvidenceItem("protection.parameter", ReviewValue.OfText(FireProtectionParameters.Provided));
        if (protection.RawText is not null)
            yield return new ReviewEvidenceItem("protection.raw", ReviewValue.OfText(protection.RawText));
        if (protection.Reason is not null)
            yield return new ReviewEvidenceItem("protection.reason", ReviewValue.OfText(protection.Reason));
    }

    /// <summary>Spec docs §3.4: a 連跨複數樓層 space has to record what it was handed on to.</summary>
    private static IEnumerable<ReviewEvidenceItem> DoubtEvidence(CurtainWallJunctionDoubt doubt)
    {
        yield return new ReviewEvidenceItem("junction.doubt", ReviewValue.OfText(doubt.Kind.ToString()));
        yield return new ReviewEvidenceItem("junction.doubtReason", ReviewValue.OfText(doubt.Message));
        if (doubt.SubjectUniqueIds.Count > 0)
            yield return new ReviewEvidenceItem("junction.doubtSubjects", ReviewValue.OfText(string.Join(",", doubt.SubjectUniqueIds)));
        if (doubt.Kind == CurtainWallJunctionDoubtKind.VerticalCompartmentSpace)
            yield return new ReviewEvidenceItem("junction.transferredTo", ReviewValue.OfText(CurtainWallJunctionReferences.Article79_2));
    }

    /// <summary>How the measurements were taken, so a number can be traced back to the settings.</summary>
    private static IEnumerable<ReviewEvidenceItem> OptionEvidence(CurtainWallJunctionOptions options)
    {
        yield return new ReviewEvidenceItem("option.junctionSearchTolerance",
            ReviewValue.Quantity(options.JunctionSearchToleranceMm / MillimetersPerMeter, ReviewUnit.Meter));
        yield return new ReviewEvidenceItem("option.samplingInterval",
            ReviewValue.Quantity(options.SamplingIntervalMm / MillimetersPerMeter, ReviewUnit.Meter));
    }

    /// <summary>
    /// The parts of the evidence in order, first mention of a field winning. Evidence is a set of
    /// fields, and the rules' record of what they read must not be overwritten by the check's own.
    /// </summary>
    private static ReviewEvidence Merge(params IEnumerable<ReviewEvidenceItem>[] parts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return new ReviewEvidence(parts.SelectMany(x => x).Where(item => seen.Add(item.Field)).ToList());
    }

    private static IEnumerable<ReviewEvidenceItem> InputEvidence(IEnumerable<ReviewInput> inputs) =>
        inputs.Where(x => x.Source is not null)
            .Select(x => new ReviewEvidenceItem($"source[{x.Field}]", ReviewValue.OfText(x.Source!)));

    private static IEnumerable<ReviewEvidenceItem> GapEvidence(RuleOutcome outcome) =>
        outcome.Gaps.Count == 0
            ? Enumerable.Empty<ReviewEvidenceItem>()
            : new[] { new ReviewEvidenceItem("rule.gaps", ReviewValue.OfText(string.Join("、", outcome.Gaps))) };
}
