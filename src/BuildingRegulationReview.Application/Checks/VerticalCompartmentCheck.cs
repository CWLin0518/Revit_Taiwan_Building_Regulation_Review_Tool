using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// The 第79條之2 verdict for one subject: a (防火設備, 要求) pair of 第1項, or — for
/// <see cref="VerticalCompartmentRequirement.AtriumExemption"/> — one 挑空 區劃 (文件 §3.6).
/// </summary>
public sealed class VerticalCompartmentFinding
{
    internal VerticalCompartmentFinding(
        VerticalCompartmentRequirement requirement,
        ReviewResult result,
        string elementUniqueId,
        CandidateCategory? category,
        string? typeUniqueId,
        string? typeName,
        ShaftDeviceProperties? device,
        RuleOutcome? outcome,
        string? errorCode,
        AtriumExemption? exemption = null)
    {
        Requirement = requirement;
        Result = result;
        ElementUniqueId = elementUniqueId;
        Category = category;
        TypeUniqueId = typeUniqueId;
        TypeName = typeName;
        Device = device;
        Outcome = outcome;
        ErrorCode = errorCode;
        Exemption = exemption;
    }

    /// <summary>Which of the 檢討表 rows this result answers.</summary>
    public VerticalCompartmentRequirement Requirement { get; }

    public ReviewResult Result { get; }
    public ReviewStatus Status => Result.Status;
    public Guid? ZoneId => Result.ZoneId is null ? (Guid?)null : Guid.Parse(Result.ZoneId);

    /// <summary>
    /// What this result is counted by: the 防火設備 under review — a 門, a 窗 or a 帷幕嵌板 — or, for
    /// 第3項, the 挑空's first Area, so that row's count is one per 挑空.
    /// </summary>
    public string ElementUniqueId { get; }

    /// <summary>The subject's category, and null for 第3項 — a 區劃 is no candidate category.</summary>
    public CandidateCategory? Category { get; }

    public string? TypeUniqueId { get; }
    public string? TypeName { get; }

    /// <summary>What the Type declared, or null when the subject never reached the rules.</summary>
    public ShaftDeviceProperties? Device { get; }

    /// <summary>What the rule engine said, or null when no rule ran (a doubt, a zone in doubt, or 第3項).</summary>
    public RuleOutcome? Outcome { get; }

    /// <summary>
    /// The 第3項 judgement, and null for every other row. 第3項 is decided by a plain calculation
    /// rather than by a rule (決議 24), so this is where its 款 and its gaps are carried.
    /// </summary>
    public AtriumExemption? Exemption { get; }

    /// <summary>The spec 14 code a log entry about this subject carries; null for a routine verdict.</summary>
    public string? ErrorCode { get; }

    /// <summary>True when 第2項's 昇降機間但書 carried the device — 得免遮煙, which spec 11.7 counts towards 符合.</summary>
    public bool IsExempt => Outcome?.Reason == RuleOutcomeReason.Exempt;

    public override string ToString() => $"{ElementUniqueId} / {Requirement}: {ReviewStatusText.Label(Status)} — {Result.Message}";
}

/// <summary>One row of the 垂直區劃 statistics: every subject counted in one row (文件 §7).</summary>
public sealed class VerticalCompartmentGroupSummary
{
    internal VerticalCompartmentGroupSummary(VerticalCompartmentRequirement requirement, IEnumerable<VerticalCompartmentFinding> findings)
    {
        Requirement = requirement;
        Findings = new ReadOnlyCollection<VerticalCompartmentFinding>(findings.ToList());
        ElementUniqueIds = new ReadOnlyCollection<string>(Findings.Select(f => f.ElementUniqueId)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());
        Status = ReviewStatusSeverity.Worst(Findings.Select(f => f.Status));
    }

    public VerticalCompartmentRequirement Requirement { get; }

    /// <summary>The 檢討表 row name, e.g. 管道間維修門防火時效.</summary>
    public string Label => VerticalCompartmentRequirements.Label(Requirement);

    public IReadOnlyList<VerticalCompartmentFinding> Findings { get; }

    /// <summary>
    /// Each subject once — each 防火設備 even when it lies on the boundary of two 區劃, and for 第3項
    /// each 挑空.
    /// </summary>
    public IReadOnlyList<string> ElementUniqueIds { get; }

    /// <summary>How many 件 the log reports for this row.</summary>
    public int DeviceCount => ElementUniqueIds.Count;

    /// <summary>Fail when any fails, otherwise the most doubtful state; 未檢討 when the row is empty.</summary>
    public ReviewStatus Status { get; }

    public int Count(ReviewStatus status) => Findings.Count(f => f.Status == status);

    public override string ToString() => $"{Label} ×{DeviceCount}：{ReviewStatusText.Label(Status)}";
}

/// <summary>The 垂直區劃 results of one run.</summary>
public sealed class VerticalCompartmentReview
{
    internal VerticalCompartmentReview(IEnumerable<VerticalCompartmentFinding> findings, IEnumerable<string> warnings)
    {
        Findings = new ReadOnlyCollection<VerticalCompartmentFinding>(findings.ToList());
        Groups = new ReadOnlyCollection<VerticalCompartmentGroupSummary>(VerticalCompartmentRequirements.All
            .Select(r => new VerticalCompartmentGroupSummary(r, Findings.Where(f => f.Requirement == r))).ToList());
        Warnings = new ReadOnlyCollection<string>(warnings.ToList());
    }

    /// <summary>One per (設備, 要求) pair and one per 挑空, in zone, requirement and element order.</summary>
    public IReadOnlyList<VerticalCompartmentFinding> Findings { get; }

    public IEnumerable<ReviewResult> Results => Findings.Select(x => x.Result);

    /// <summary>The 檢討表 rows, always all four, in <see cref="VerticalCompartmentRequirements.All"/> order.</summary>
    public IReadOnlyList<VerticalCompartmentGroupSummary> Groups { get; }

    public IReadOnlyList<string> Warnings { get; }

    public VerticalCompartmentGroupSummary Group(VerticalCompartmentRequirement requirement) =>
        Groups.First(g => g.Requirement == requirement);

    /// <summary>Every verdict about one 防火設備 — a 維修門 has two of them (文件 §9 第8項).</summary>
    public IEnumerable<VerticalCompartmentFinding> For(string elementUniqueId) =>
        Findings.Where(f => string.Equals(f.ElementUniqueId, elementUniqueId, StringComparison.Ordinal));

    public VerticalCompartmentFinding? For(string elementUniqueId, VerticalCompartmentRequirement requirement) =>
        For(elementUniqueId).FirstOrDefault(f => f.Requirement == requirement);

    public int Count(ReviewStatus status) => Findings.Count(x => x.Status == status);
}

/// <summary>
/// 垂直區劃之遮煙性能與管道間維修門時效, 第79條之2第1項 (docs/regulations/vertical-compartment.md).
/// <list type="number">
/// <item>The subjects are the candidate openings of a 區劃 whose <c>zone.use</c> is one 第79條之2
/// names — the same batch 防火門窗 reviews — paired with the requirements that 用途 carries
/// (<see cref="VerticalCompartmentRequirements.ForUse"/>). A 用途 outside the vocabulary, or none at
/// all, produces no subject here: 「其他類似部分」 cannot be enumerated (文件 §3.2).</item>
/// <item>One pair is one subject, because a 維修門 owes 時效 and 遮煙性能 at once and a rule's
/// requirement is a single comparison (文件 §3.1). A 維修門 therefore yields two results, and 昇降機道
/// 出入口 one, whether it is a 門, a 窗 or a 帷幕嵌板.</item>
/// <item>The facts are the requirement, the device's identity and the one value that answers it,
/// plus the building and zone inputs. Nothing else is decided here: 第2項's 昇降機間但書 is an
/// exemption on the rule and <c>shaft.elevatorLobbyProtected</c> has no source yet (文件 §9 第2項), so
/// a device with no 遮煙性能 and no answer about its 昇降機間 is 資料不足, never 未符合.</item>
/// <item>A 區劃 whose extent is in doubt, or an opening whose relation to it is, gets 人工覆核 and no
/// rule runs — the same line 防火門窗 draws.</item>
/// </list>
/// </summary>
public static class VerticalCompartmentCheck
{
    private const string ZoneUseField = "zone.use";

    /// <summary>第3項 lifts 第1項 alone; it says nothing about 第83條's 面積 (文件 §3.6、§5.3).</summary>
    private const string AtriumLegalReference = "建築技術規則建築設計施工編第79條之2第3項（挑空得不受第1項限制）";

    public static Result<VerticalCompartmentReview> Review(
        CandidateSet set,
        VerticalCompartmentInputs inputs,
        RuleEngine engine,
        RuleEvaluationContext context,
        Guid runId,
        Func<Guid>? newResultId = null)
    {
        if (set is null) throw new ArgumentNullException(nameof(set));
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));
        if (engine is null) throw new ArgumentNullException(nameof(engine));
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
        newResultId ??= Guid.NewGuid;

        var ready = ReviewPreconditions.Zones(set, "垂直區劃");
        if (ready.IsFailure) return Result.Failure<VerticalCompartmentReview>(ready.Error);

        var warnings = inputs.Context.ZoneIds.Where(id => set.Zone(id) is null)
            .Select(id => $"輸入資料指定的區劃 {id:D} 不在此工作包中，已略過。")
            .Concat(inputs.Devices
                .Where(d => !set.Openings.Any(o => string.Equals(o.Observation.TypeUniqueId, d.TypeUniqueId, StringComparison.Ordinal)))
                .Select(d => $"Type {d.TypeUniqueId} 有遮煙性能或防火時效資料，但此工作包沒有該 Type 的候選開口。"))
            .ToList();

        var findings = new List<VerticalCompartmentFinding>();
        foreach (var zone in set.Zones)
        {
            var use = Use(inputs.Context, zone.ZoneId);

            // 第3項's subject is the 挑空 itself, so it is decided before — and independently of —
            // the opening loop, which 挑空 never enters (文件 §3.6、決議 23).
            if (string.Equals(use, ZoneUses.Atrium, StringComparison.Ordinal))
                findings.Add(Atrium(set, zone, inputs, engine.RuleSet, runId, newResultId()));

            var requirements = VerticalCompartmentRequirements.ForUse(use);
            if (requirements.Count == 0) continue;

            foreach (var opening in set.OpeningsOf(zone.ZoneId))
            {
                var relation = opening.RelationTo(zone.ZoneId)!;
                foreach (var requirement in requirements)
                {
                    if (!VerticalCompartmentRequirements.Categories(requirement).Contains(opening.Category)) continue;

                    findings.Add(
                        !zone.IsClear ? Withhold(set, zone, opening, requirement, engine.RuleSet, runId, newResultId())
                        : relation.IsAmbiguous || relation.IsFacade ? Unresolved(set, zone, opening, relation, requirement, engine.RuleSet, runId, newResultId())
                        : Decide(set, zone, opening, requirement, inputs, engine, context, runId, newResultId()));
                }
            }
        }

        var ordered = findings
            .OrderBy(f => f.Result.ZoneId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(f => (int)f.Requirement)
            .ThenBy(f => f.ElementUniqueId, StringComparer.Ordinal)
            .ToList();

        return Result.Success(new VerticalCompartmentReview(ordered, warnings));
    }

    private static VerticalCompartmentFinding Decide(
        CandidateSet set,
        CandidateZone zone,
        OpeningCandidate opening,
        VerticalCompartmentRequirement requirement,
        VerticalCompartmentInputs inputs,
        RuleEngine engine,
        RuleEvaluationContext context,
        Guid runId,
        Guid resultId)
    {
        var observation = opening.Observation;

        // opening.* is not available to this category (文件 §5.2), so the subject's own facts are
        // built from the zone up rather than with CandidateFacts.ForOpening.
        var facts = CandidateFacts.ForZone(set, zone, engine.RuleSet.Catalog);
        facts.Set(VerticalCompartmentRequirements.RequirementField, VerticalCompartmentRequirements.RuleText(requirement));
        facts.Set(VerticalCompartmentRequirements.ElementField, observation.Source.ElementUniqueId);

        var supplied = inputs.Context.Building.Concat(inputs.Context.ForZone(zone.ZoneId)).ToList();
        foreach (var input in supplied) input.ApplyTo(facts);

        var device = inputs.ForType(observation.TypeUniqueId);
        var field = VerticalCompartmentRequirements.ActualField(requirement);
        var rating = field == VerticalCompartmentRequirements.FireRatingField
            ? device?.FireRating ?? ProvidedFireRating.Missing(MissingReason(observation, FireRatingParameters.Provided))
            : null;
        var smoke = field == VerticalCompartmentRequirements.SmokeProtectionField
            ? device?.SmokeProtection ?? ProvidedFireProtection.Missing(MissingReason(observation, SmokeProtectionParameters.Provided))
            : null;

        if (rating is not null)
        {
            switch (rating.Kind)
            {
                case ProvidedFireRatingKind.Rated:
                    facts.Set(field, rating.Minutes!.Value, ReviewUnit.Minute);
                    break;
                case ProvidedFireRatingKind.Unreadable:
                case ProvidedFireRatingKind.Undeterminable:
                    facts.MarkUnreadable(field, $"「{rating.RawText}」{rating.Reason}");
                    break;
            }
        }
        else if (smoke is not null)
        {
            switch (smoke.Kind)
            {
                case ProvidedFireProtectionKind.Yes:
                case ProvidedFireProtectionKind.No:
                    facts.Set(field, smoke.RuleText!);
                    break;
                case ProvidedFireProtectionKind.Unreadable:
                    facts.MarkUnreadable(field, $"「{smoke.RawText}」{smoke.Reason}");
                    break;
            }
        }

        var outcome = engine.Evaluate(RuleCategory.VerticalCompartment, facts, context);
        var subject = Subject(observation, zone, requirement);
        var status = outcome.Status;
        var message = $"{subject}：{outcome.Message}";
        var errorCode = RuleOutcomeErrorCode.For(outcome);

        var gap = outcome.Gaps.Any(g => g.Field == field);
        if (status == ReviewStatus.InsufficientData && gap)
        {
            // A composite Type such as 「1hr/2hr」 is not a gap a person can fill in — somebody has to
            // say which layer answers — so it is 人工覆核, exactly as 構件防火時效 treats it.
            if (rating is { Kind: ProvidedFireRatingKind.Undeterminable } && outcome.Gaps.Count == 1)
            {
                status = ReviewStatus.ManualReview;
                message = $"{subject}：設計防火時效「{rating.RawText}」{rating.Reason}，需人工覆核是否達到要求" +
                          (outcome.RequiredValue is { } required ? $" {FireRatingText.Format(required.Number)}。" : "。");
                errorCode = ReviewErrorCode.FireRatingUndetermined;
            }
            else
            {
                errorCode ??= rating is not null
                    ? rating.Kind switch
                    {
                        ProvidedFireRatingKind.Missing => ReviewErrorCode.ParameterMissing,
                        ProvidedFireRatingKind.Undeterminable => ReviewErrorCode.FireRatingUndetermined,
                        _ => ReviewErrorCode.ParameterTypeMismatch
                    }
                    : smoke!.Kind == ProvidedFireProtectionKind.Missing
                        ? ReviewErrorCode.ParameterMissing
                        : ReviewErrorCode.ParameterTypeMismatch;
            }
        }

        var evidence = Merge(
            outcome.Evidence.Items,
            opening.EvidenceFor(zone.ZoneId).Items,
            SubjectEvidence(observation, zone, requirement),
            ProvidedEvidence(field, rating, smoke),
            InputEvidence(supplied),
            GapEvidence(outcome));

        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.VerticalCompartment,
            new[] { observation.Source.ElementUniqueId }, zone.ZoneIdText, status, outcome.ActualValue, outcome.RequiredValue,
            outcome.RuleId, outcome.RuleVersion, outcome.LegalReference, message, evidence);

        return new VerticalCompartmentFinding(requirement, result, observation.Source.ElementUniqueId, observation.Category,
            observation.TypeUniqueId, observation.TypeName, device, outcome, errorCode);
    }

    /// <summary>
    /// An opening whose relation to the 區劃 is ambiguous (帷幕牆、未寄主、連結模型…). Whether it is the
    /// 昇降機道出入口 or the 維修門 at all cannot be relied on, so no rule runs — the same MVP policy
    /// 防火門窗 applies (P3-T03). An opening in the outer curtain-wall facade lands here too: 第79條之2
    /// names the 昇降機道 and 管道間 devices without saying whether a facade panel is one of them.
    /// </summary>
    private static VerticalCompartmentFinding Unresolved(
        CandidateSet set,
        CandidateZone zone,
        OpeningCandidate opening,
        ZoneRelation relation,
        VerticalCompartmentRequirement requirement,
        CompiledRuleSet ruleSet,
        Guid runId,
        Guid resultId)
    {
        var observation = opening.Observation;
        var evidence = Merge(opening.EvidenceFor(zone.ZoneId).Items, SubjectEvidence(observation, zone, requirement));

        var info = ruleSet.RuleSet;
        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.VerticalCompartment,
            new[] { observation.Source.ElementUniqueId }, zone.ZoneIdText, ReviewStatus.ManualReview, null, null,
            info.RuleSetId, info.Version, info.FallbackLegalReference,
            $"{Subject(observation, zone, requirement)}不檢討：{relation.Message}" +
            (relation.IsFacade ? "第79條之2 未明定外牆開口是否屬昇降機道或管道間之防火設備，" : string.Empty) + "需人工覆核。", evidence);
        return new VerticalCompartmentFinding(requirement, result, observation.Source.ElementUniqueId, observation.Category,
            observation.TypeUniqueId, observation.TypeName, null, null, ReviewErrorCode.CandidateAmbiguous);
    }

    /// <summary>
    /// An opening of a 區劃 whose extent is in doubt (未封閉、重疊): whether the 區劃 is the 昇降機道 or
    /// 管道間 this opening belongs to cannot be relied on.
    /// </summary>
    private static VerticalCompartmentFinding Withhold(
        CandidateSet set,
        CandidateZone zone,
        OpeningCandidate opening,
        VerticalCompartmentRequirement requirement,
        CompiledRuleSet ruleSet,
        Guid runId,
        Guid resultId)
    {
        var observation = opening.Observation;
        var evidence = Merge(
            opening.EvidenceFor(zone.ZoneId).Items,
            SubjectEvidence(observation, zone, requirement),
            new[] { new ReviewEvidenceItem("zone.problems", ReviewValue.OfText(string.Join(",", zone.Problems))) });

        var info = ruleSet.RuleSet;
        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.VerticalCompartment,
            new[] { observation.Source.ElementUniqueId }, zone.ZoneIdText, ReviewStatus.ManualReview, null, null,
            info.RuleSetId, info.Version, info.FallbackLegalReference,
            $"{Subject(observation, zone, requirement)}不檢討：區劃「{zone.Name}」的範圍有問題" +
            $"（{string.Join("、", zone.Problems)}），無法確認開口與區劃的關係，需人工覆核。",
            evidence);
        return new VerticalCompartmentFinding(requirement, result, observation.Source.ElementUniqueId, observation.Category,
            observation.TypeUniqueId, observation.TypeName, null, null, ReviewErrorCode.CandidateZoneUnusable);
    }

    /// <summary>
    /// 第3項 for one 挑空 (文件 §3.6). No rule runs: 第3項 is a classification rather than a
    /// requirement, so the judgement is <see cref="AtriumExemption.For"/>'s and the result is
    /// attributed to the rule set itself, the same shape <see cref="Withhold"/> and
    /// <see cref="Unresolved"/> take. There are only three states — 免除成立 is 符合, the 連通區劃's
    /// area and separation being reviewed by the 區劃面積, 構件防火時效 and 防火門窗 results (決議 39),
    /// a gap is 資料不足, and neither is 不適用. There is no 未符合: failing 第3項 is not a violation, it
    /// only means 第1項 applies as usual and the existing boundary rules review it.
    /// </summary>
    private static VerticalCompartmentFinding Atrium(
        CandidateSet set,
        CandidateZone zone,
        VerticalCompartmentInputs inputs,
        CompiledRuleSet ruleSet,
        Guid runId,
        Guid resultId)
    {
        const VerticalCompartmentRequirement requirement = VerticalCompartmentRequirement.AtriumExemption;
        var info = ruleSet.RuleSet;
        var subject = $"挑空「{zone.Name}」";
        var element = zone.AreaUniqueIds.FirstOrDefault() ?? zone.ZoneIdText;

        // The same line every other subject draws: a 區劃 whose extent is in doubt is not measured,
        // and 第3項 needs its 樓地板面積 and its identity as one 挑空 just as much (文件 §4).
        if (!zone.IsClear)
        {
            var withheld = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.VerticalCompartment,
                zone.AreaUniqueIds, zone.ZoneIdText, ReviewStatus.ManualReview, null, null,
                info.RuleSetId, info.Version, AtriumLegalReference,
                $"{subject}不檢討第3項免除：區劃範圍有問題（{string.Join("、", zone.Problems)}），需人工覆核。",
                Merge(AtriumEvidence(zone),
                    new[] { new ReviewEvidenceItem("zone.problems", ReviewValue.OfText(string.Join(",", zone.Problems))) }));
            return new VerticalCompartmentFinding(requirement, withheld, element, null, null, null, null, null,
                ReviewErrorCode.CandidateZoneUnusable);
        }

        var supplied = inputs.Context.Building.Concat(inputs.Context.ForZone(zone.ZoneId)).ToList();
        var facts = AtriumExemptionFacts.Read(supplied);
        var exemption = facts.Exemption;
        // 免除成立 is 符合 (決議 39, replacing 決議 25's 人工覆核): what 第3項 leaves to check — the
        // 連通區劃's area and its separation from the rest — is reviewed elsewhere in the same run, by
        // the 區劃面積 rules (決議 32) and by the boundary walls and 防火設備 of its 區劃, 阻熱性
        // included (決議 38). Whether the design complies is read off those results, not this one.
        var status = exemption.Holds ? ReviewStatus.Pass
            : exemption.IsUndecided ? ReviewStatus.InsufficientData
            : ReviewStatus.NotApplicable;

        var message = exemption.Holds
            ? $"{subject}：{exemption.Description}，得不受第1項單獨區劃分隔之限制。" +
              "免除成立後此挑空不再是第79條之2之垂直區劃：其連通區劃之合計樓地板面積改依第79條" +
              "（所跨樓層含第十一層以上者並依第83條）檢討，見區劃面積結果；連通範圍與其他部分之區劃分隔" +
              "（一小時以上防火時效之牆壁、具一小時以上阻熱性之防火設備）見各區劃之構件防火時效與防火門窗結果。"
            : exemption.IsUndecided
                ? $"{subject}：{exemption.Description}，第3項之免除資料不足。"
                : $"{subject}：{exemption.Description}，不適用第3項之免除。" +
                  (facts.FireResistive == true ? "第1項之區劃分隔照常適用，其牆壁與開口由第79條之區劃規則檢討。" : string.Empty);

        var errorCode = exemption.IsUndecided ? ReviewErrorCode.ParameterMissing : null;

        var evidence = Merge(
            AtriumEvidence(zone),
            facts.Evidence(),
            new[]
            {
                new ReviewEvidenceItem("atrium.clause", ReviewValue.OfText(exemption.Clause.ToString())),
                new ReviewEvidenceItem("atrium.gaps", ReviewValue.OfText(exemption.Gaps.ToString()))
            },
            InputEvidence(supplied));

        // A 符合 carries what was compared: the 款 that holds, against 「第一款或第二款」.
        var actual = exemption.Holds
            ? ReviewValue.OfText(exemption.Clause == AtriumExemptionClause.FirstClause ? "第一款" : "第二款")
            : null;
        var required = exemption.Holds ? ReviewValue.OfText("第一款或第二款") : null;

        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.VerticalCompartment,
            zone.AreaUniqueIds, zone.ZoneIdText, status, actual, required,
            info.RuleSetId, info.Version, AtriumLegalReference, message, evidence);

        return new VerticalCompartmentFinding(requirement, result, element, null, null, null, null, null, errorCode, exemption);
    }

    /// <summary>
    /// The 區劃用途 as the zone inputs carry it. An unreadable one (parts of the 區劃 filled in
    /// differently) is no 用途 at all, so the 區劃 produces no subject here — the 區劃面積 check is
    /// where that disagreement is reported.
    /// </summary>
    private static string? Use(CompartmentAreaInputs inputs, Guid zoneId) =>
        inputs.ForZone(zoneId).FirstOrDefault(x => string.Equals(x.Field, ZoneUseField, StringComparison.Ordinal))
            is { IsUnreadable: false, Value: { Kind: ReviewValueKind.Text } value }
            ? value.Text
            : null;

    /// <summary>
    /// Names the device and the one requirement this result answers. A 維修門 gets two results, so
    /// the requirement has to be in the message or the 檢討表 would show the same door twice with no
    /// way to tell the rows apart (文件 §9 第8項).
    /// </summary>
    private static string Subject(OpeningObservation observation, CandidateZone zone, VerticalCompartmentRequirement requirement) =>
        $"{CandidateCategories.Label(observation.Category)}「{observation.TypeName ?? "無 Type 名稱"}」" +
        $"（{observation.Source}）於區劃「{zone.Name}」之{VerticalCompartmentRequirements.Label(requirement)}";

    /// <summary>What every 第3項 result carries, whether or not it reached the judgement.</summary>
    private static IEnumerable<ReviewEvidenceItem> AtriumEvidence(CandidateZone zone)
    {
        yield return new ReviewEvidenceItem(VerticalCompartmentRequirements.RequirementField,
            ReviewValue.OfText(VerticalCompartmentRequirements.RuleText(VerticalCompartmentRequirement.AtriumExemption)));
        yield return new ReviewEvidenceItem("shaft.requirementLabel",
            ReviewValue.OfText(VerticalCompartmentRequirements.Label(VerticalCompartmentRequirement.AtriumExemption)));
        yield return new ReviewEvidenceItem("zone.name", ReviewValue.OfText(zone.Name));
    }

    private static string MissingReason(OpeningObservation observation, string parameterName) =>
        observation.TypeUniqueId is null
            ? $"此開口沒有可讀取的 Type，無法取得 {parameterName}"
            : $"此 Type 沒有參數 {parameterName}";

    private static IEnumerable<ReviewEvidenceItem> SubjectEvidence(
        OpeningObservation observation, CandidateZone zone, VerticalCompartmentRequirement requirement)
    {
        yield return new ReviewEvidenceItem(VerticalCompartmentRequirements.RequirementField,
            ReviewValue.OfText(VerticalCompartmentRequirements.RuleText(requirement)));
        yield return new ReviewEvidenceItem(VerticalCompartmentRequirements.ElementField,
            ReviewValue.OfText(observation.Source.ElementUniqueId));

        // The 檢討表 and the markup are rebuilt from the stored results alone, so the row a result
        // belongs to and the 區劃 it was decided in travel with it.
        yield return new ReviewEvidenceItem("shaft.requirementLabel",
            ReviewValue.OfText(VerticalCompartmentRequirements.Label(requirement)));
        yield return new ReviewEvidenceItem("zone.name", ReviewValue.OfText(zone.Name));
        if (observation.TypeUniqueId is not null)
            yield return new ReviewEvidenceItem("source.typeUniqueId", ReviewValue.OfText(observation.TypeUniqueId));
    }

    private static IEnumerable<ReviewEvidenceItem> ProvidedEvidence(string field, ProvidedFireRating? rating, ProvidedFireProtection? smoke)
    {
        var parameter = rating is not null ? FireRatingParameters.Provided : SmokeProtectionParameters.Provided;
        var kind = rating is not null ? rating.Kind.ToString() : smoke!.Kind.ToString();
        var raw = rating is not null ? rating.RawText : smoke!.RawText;
        var reason = rating is not null ? rating.Reason : smoke!.Reason;

        yield return new ReviewEvidenceItem("provided.field", ReviewValue.OfText(field));
        yield return new ReviewEvidenceItem("provided.kind", ReviewValue.OfText(kind));
        yield return new ReviewEvidenceItem("provided.parameter", ReviewValue.OfText(parameter));
        if (raw is not null) yield return new ReviewEvidenceItem("provided.raw", ReviewValue.OfText(raw));
        if (reason is not null) yield return new ReviewEvidenceItem("provided.reason", ReviewValue.OfText(reason));
    }

    private static IEnumerable<ReviewEvidenceItem> InputEvidence(IEnumerable<ReviewInput> inputs) =>
        inputs.Where(x => x.Source is not null)
            .Select(x => new ReviewEvidenceItem($"source[{x.Field}]", ReviewValue.OfText(x.Source!)));

    private static IEnumerable<ReviewEvidenceItem> GapEvidence(RuleOutcome outcome) =>
        outcome.Gaps.Count == 0
            ? Enumerable.Empty<ReviewEvidenceItem>()
            : new[] { new ReviewEvidenceItem("rule.gaps", ReviewValue.OfText(string.Join("、", outcome.Gaps))) };

    /// <summary>
    /// The parts of the evidence in order, first mention of a field winning: the rules' own record
    /// of what they read must not be overwritten by the check's copy of it.
    /// </summary>
    private static ReviewEvidence Merge(params IEnumerable<ReviewEvidenceItem>[] parts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return new ReviewEvidence(parts.SelectMany(x => x).Where(item => seen.Add(item.Field)).ToList());
    }
}
