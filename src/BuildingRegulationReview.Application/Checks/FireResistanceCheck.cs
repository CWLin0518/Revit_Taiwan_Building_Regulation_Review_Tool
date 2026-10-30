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

/// <summary>The 構件防火時效 verdict for one member in one zone, or for a member whose relation is ambiguous.</summary>
public sealed class MemberRatingFinding
{
    internal MemberRatingFinding(
        ReviewResult result,
        string elementUniqueId,
        CandidateCategory? category,
        string? typeUniqueId,
        string? typeName,
        ProvidedFireRating? provided,
        RuleOutcome? outcome,
        CandidateAmbiguity? ambiguity,
        string? errorCode,
        bool isExteriorCurtainWall = false)
    {
        IsExteriorCurtainWall = isExteriorCurtainWall;
        Result = result;
        ElementUniqueId = elementUniqueId;
        Category = category;
        TypeUniqueId = typeUniqueId;
        TypeName = typeName;
        Provided = provided;
        Outcome = outcome;
        Ambiguity = ambiguity;
        ErrorCode = errorCode;
    }

    public ReviewResult Result { get; }
    public ReviewStatus Status => Result.Status;
    public Guid? ZoneId => Result.ZoneId is null ? (Guid?)null : Guid.Parse(Result.ZoneId);
    public string ElementUniqueId { get; }

    /// <summary>The member category; null only when an ambiguity did not say.</summary>
    public CandidateCategory? Category { get; }

    public string? TypeUniqueId { get; }
    public string? TypeName { get; }

    /// <summary>The design rating as read, untouched by the review; null when the member never reached the rules.</summary>
    public ProvidedFireRating? Provided { get; }

    /// <summary>What the rule engine said, or null when no rule ran (ambiguity or zone problem).</summary>
    public RuleOutcome? Outcome { get; }

    /// <summary>
    /// The candidate ambiguity behind the result, when there was one: behind a ManualReview, or behind a
    /// verdict settled because it was the same whether or not the member bounds the zone.
    /// </summary>
    public CandidateAmbiguity? Ambiguity { get; }

    /// <summary>The spec 14 code a log entry about this member carries; null for a routine verdict.</summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// An 外牆帷幕牆 the member rules set aside: not reported as a 構件防火時效 result
    /// (see <see cref="FireResistanceReview.ExteriorCurtainWalls"/>).
    /// </summary>
    public bool IsExteriorCurtainWall { get; }

    /// <summary>The required rating in minutes, whenever the rules could compute it.</summary>
    public double? RequiredMinutes =>
        Result.RequiredValue is { Kind: ReviewValueKind.Quantity, Unit: ReviewUnit.Minute } required ? required.Number : (double?)null;

    public override string ToString() => $"{ElementUniqueId}: {ReviewStatusText.Label(Status)} — {Result.Message}";
}

/// <summary>
/// The results of one Type, gathered for the 防火時效 legend and the 檢討表 (spec 11.5 step 7, 11.7:
/// 依類別與 Type 統計). A Type appears once however many instances it has.
/// </summary>
public sealed class TypeRatingSummary
{
    internal TypeRatingSummary(
        CandidateCategory? category,
        string? typeUniqueId,
        string? typeName,
        TypeFireRating? provided,
        IEnumerable<MemberRatingFinding> findings)
    {
        Category = category;
        TypeUniqueId = typeUniqueId;
        TypeName = typeName;
        Provided = provided;
        Findings = new ReadOnlyCollection<MemberRatingFinding>(findings.ToList());
        ElementUniqueIds = new ReadOnlyCollection<string>(Findings.Select(f => f.ElementUniqueId)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());
        Status = ReviewStatusSeverity.Worst(Findings.Select(f => f.Status));
        var required = Findings.Select(f => f.RequiredMinutes).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        HighestRequiredMinutes = required.Count > 0 ? required.Max() : (double?)null;
    }

    public CandidateCategory? Category { get; }
    public string? TypeUniqueId { get; }
    public string? TypeName { get; }

    /// <summary>The design rating read for the Type, or null when none was supplied.</summary>
    public TypeFireRating? Provided { get; }

    public IReadOnlyList<MemberRatingFinding> Findings { get; }

    /// <summary>Each instance once, even when it bounds several zones.</summary>
    public IReadOnlyList<string> ElementUniqueIds { get; }

    public int InstanceCount => ElementUniqueIds.Count;

    /// <summary>The highest rating any of its results requires; what a legend states as 要求防火時效.</summary>
    public double? HighestRequiredMinutes { get; }

    /// <summary>
    /// Fail when any result fails, otherwise the most doubtful state present (人工覆核, then 資料不足),
    /// and Pass only when nothing is doubtful — a Type is never shown as passing on the strength of
    /// some of its instances.
    /// </summary>
    public ReviewStatus Status { get; }

    public int Count(ReviewStatus status) => Findings.Count(f => f.Status == status);

    public override string ToString() =>
        $"{(Category.HasValue ? CandidateCategories.Label(Category.Value) : "?")}「{TypeName ?? TypeUniqueId ?? "無 Type"}」×{InstanceCount}：{ReviewStatusText.Label(Status)}";
}

/// <summary>The 構件防火時效 results of one run.</summary>
public sealed class FireResistanceReview
{
    internal FireResistanceReview(
        IEnumerable<MemberRatingFinding> findings,
        IEnumerable<MemberRatingFinding> exteriorCurtainWalls,
        IEnumerable<TypeRatingSummary> types,
        IEnumerable<string> warnings)
    {
        Findings = new ReadOnlyCollection<MemberRatingFinding>(findings.ToList());
        ExteriorCurtainWalls = new ReadOnlyCollection<MemberRatingFinding>(exteriorCurtainWalls.ToList());
        Types = new ReadOnlyCollection<TypeRatingSummary>(types.ToList());
        Warnings = new ReadOnlyCollection<string>(warnings.ToList());
    }

    /// <summary>
    /// One per member and zone, plus one per ambiguous member relation; zone, category, element order.
    /// 外牆帷幕牆 are not among them.
    /// </summary>
    public IReadOnlyList<MemberRatingFinding> Findings { get; }

    /// <summary>
    /// The 外牆帷幕牆, which no member rule asks a rating of: they are not 區劃牆壁, and their fire
    /// requirements are the 帷幕牆區劃交接 check's (第79條第3項、第4項、第79條之3、第79條之4). Not
    /// reported here, in the 檢討表 or in the Type summary. A 室內 curtain wall is a 區劃牆壁 and stays
    /// in <see cref="Findings"/>; one whose 室內外 is undecided stays there as a 人工覆核.
    /// </summary>
    public IReadOnlyList<MemberRatingFinding> ExteriorCurtainWalls { get; }

    public IEnumerable<ReviewResult> Results => Findings.Select(x => x.Result);

    /// <summary>The findings by Type, in category and Type order.</summary>
    public IReadOnlyList<TypeRatingSummary> Types { get; }

    public IReadOnlyList<string> Warnings { get; }

    public IEnumerable<MemberRatingFinding> For(string elementUniqueId) =>
        Findings.Where(f => string.Equals(f.ElementUniqueId, elementUniqueId, StringComparison.Ordinal));

    public MemberRatingFinding? For(string elementUniqueId, Guid zoneId) =>
        For(elementUniqueId).FirstOrDefault(f => f.ZoneId == zoneId);

    public TypeRatingSummary? Type(string typeUniqueId) =>
        Types.FirstOrDefault(t => string.Equals(t.TypeUniqueId, typeUniqueId, StringComparison.Ordinal));

    public int Count(ReviewStatus status) => Findings.Count(x => x.Status == status);
}

/// <summary>
/// 柱、梁、牆、樓板防火時效 (spec 11.5). For every member the candidate set relates to a zone:
/// <list type="number">
/// <item>An ambiguous relation is already a ManualReview (P3-T03); a member of a zone whose extent is in
/// doubt is ManualReview too. No rule runs on either.</item>
/// <item>Otherwise the facts are the member's relation, category and Type, the building and zone inputs,
/// and the Type's design rating as <c>element.providedFireRating</c>. The rule engine computes the
/// required rating and compares: <c>Provided ≥ Required</c> passes.</item>
/// <item>A missing or unreadable design rating is 資料不足; a composite construction with no single
/// rating is 人工覆核 when it is the only thing missing. Neither ever becomes a Fail.</item>
/// <item>The required rating is only ever the result's required value: the design rating is read, kept
/// as the actual value and never replaced (spec 11.5 steps 3–4).</item>
/// <item>The results are also gathered by Type (spec 11.5 step 7).</item>
/// </list>
/// </summary>
public static class FireResistanceCheck
{
    private const string ProvidedField = "element.providedFireRating";

    public static Result<FireResistanceReview> Review(
        CandidateSet set,
        FireResistanceInputs inputs,
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

        var ready = ReviewPreconditions.Zones(set, "構件防火時效");
        if (ready.IsFailure) return Result.Failure<FireResistanceReview>(ready.Error);

        var warnings = inputs.Context.ZoneIds.Where(id => set.Zone(id) is null)
            .Select(id => $"輸入資料指定的區劃 {id:D} 不在此工作包中，已略過。")
            .Concat(inputs.TypeRatings
                .Where(t => !set.Members.Any(m => string.Equals(m.Observation.TypeUniqueId, t.TypeUniqueId, StringComparison.Ordinal)))
                .Select(t => $"Type「{t.TypeName ?? t.TypeUniqueId}」有防火時效資料，但此工作包沒有該 Type 的候選構件。"))
            .ToList();

        // 第79條之2第3項: the line between a merged 挑空 and its 連通區劃 is no 區劃邊界 (決議 37).
        var merged = MergedAtriums.Of(set, inputs.Context);

        var findings = new List<MemberRatingFinding>();
        foreach (var zone in set.Zones)
        {
            foreach (var member in set.MembersOf(zone.ZoneId))
            {
                var relation = member.RelationTo(zone.ZoneId)!;

                // CarriesAmbiguity 而不是 IsAmbiguous：跨越區劃邊界的牆（Crossing ＋ BoundaryCrossed）
                // 關係量得出來，但算不算這道區劃的邊界沒有答案，所以也由下面的 ambiguity 迴圈接手
                // （驗證清單 C-18）。不在這裡跳過的話，同一個構件×區劃會出兩列：這裡 Decide 得到
                // isCompartmentBoundary=false 的不適用，下面 Settled 又出一列逕行判定。
                // CandidateResolver 為每一個 CarriesAmbiguity 的關係都登了一筆疑義，所以這裡跳掉幾列
                // 下面就補回幾列。
                if (relation.CarriesAmbiguity) continue;

                // 室內外未定的帷幕牆在區劃邊界上：這一列改由下面的 ambiguity 迴圈產出人工覆核
                // （CandidateResolver 已為每一個 boundary 關係登了 CurtainWallExposureUndecided）。
                // 不在這裡跳過的話，tw-bcr-79-wall-rating 版本 3 的 appliesWhen 明文排除 Unknown，
                // 這一輪只會得到 NoRuleApplies，於是同一個構件×區劃會同時出現「不適用」與「人工覆核」
                // 兩列，而規格要的是一列（docs/tools/fire-review/rules/curtain-wall-fire-compartment.md §4.8
                // 的分流表：室內外未定時「另出一列人工覆核（CurtainWallExposureUndecided）」；§5.5
                // 版本 3 則是三分流的出處：外牆不列入、室內照常列出、室內外未定仍是一列人工覆核）。
                if (IsExposureUndecidedBoundary(set, member, relation, inputs, merged)) continue;

                findings.Add(zone.IsClear
                    ? Decide(set, zone, member, inputs, engine, context, runId, newResultId(), merged: merged)
                    : Withhold(set, zone, member, engine.RuleSet, runId, newResultId()));
            }
        }

        foreach (var ambiguity in set.Ambiguities)
        {
            var member = AmbiguousMember(set, ambiguity) ?? ExposureUndecidedMember(set, ambiguity, inputs, merged);
            if (member is null && ambiguity.Kind != CandidateAmbiguityKind.NoPlanGeometry) continue;

            var resultId = newResultId();
            if (member is not null && Settled(set, member, ambiguity, inputs, engine, context, runId, resultId, merged) is { } settled)
            {
                findings.Add(settled);
                continue;
            }

            var result = ambiguity.ToReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.FireResistance, engine.RuleSet);
            var typeName = member?.Observation.TypeName ?? Text(ambiguity.Evidence.Find("source.typeName"));
            var category = member?.Category ?? CategoryOf(ambiguity.Evidence.Find("source.category"));
            findings.Add(new MemberRatingFinding(result, ambiguity.SubjectUniqueIds[0], category,
                member?.Observation.TypeUniqueId ?? TypeUniqueIdOf(set, category, typeName), typeName, null, null, ambiguity, ambiguity.ErrorCode));
        }

        var ordered = findings
            .OrderBy(f => f.Result.ZoneId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(f => f.Category.HasValue ? (int)f.Category.Value : int.MaxValue)
            .ThenBy(f => f.ElementUniqueId, StringComparer.Ordinal)
            .ToList();
        var reported = ordered.Where(f => !f.IsExteriorCurtainWall).ToList();

        return Result.Success(new FireResistanceReview(reported, ordered.Where(f => f.IsExteriorCurtainWall),
            Summarize(reported, inputs), warnings));
    }

    private static MemberRatingFinding Decide(
        CandidateSet set,
        CandidateZone zone,
        MemberCandidate member,
        FireResistanceInputs inputs,
        RuleEngine engine,
        RuleEvaluationContext context,
        Guid runId,
        Guid resultId,
        MemberCandidate? assumed = null,
        MergedAtriums? merged = null,
        bool interiorOverride = false)
    {
        var observation = member.Observation;
        var facts = CandidateFacts.ForMember(set, assumed ?? member, zone.ZoneId, engine.RuleSet.Catalog);
        var interior = interiorOverride || (assumed is null && merged is not null && merged.IsInterior(member));
        if (interior) facts.Set("element.isCompartmentBoundary", false);
        var supplied = inputs.Context.Building.Concat(inputs.Context.ForZone(zone.ZoneId)).ToList();
        foreach (var input in supplied) input.ApplyTo(facts);

        var typeRating = inputs.ForType(observation.TypeUniqueId);
        var provided = typeRating?.Rating ?? ProvidedFireRating.Missing(observation.TypeUniqueId is null
            ? "構件沒有可讀取的 Type"
            : "Type 未提供防火時效");
        switch (provided.Kind)
        {
            case ProvidedFireRatingKind.Rated:
                facts.Set(ProvidedField, provided.Minutes!.Value, ReviewUnit.Minute);
                break;
            case ProvidedFireRatingKind.Unreadable:
            case ProvidedFireRatingKind.Undeterminable:
                facts.MarkUnreadable(ProvidedField, $"「{provided.RawText}」{provided.Reason}");
                break;
        }

        var outcome = engine.Evaluate(RuleCategory.FireResistance, facts, context);
        var subject = $"{CandidateCategories.Label(observation.Category)}「{observation.TypeName ?? "無 Type 名稱"}」（{observation.Source}）於區劃「{zone.Name}」";
        var status = outcome.Status;
        var exteriorCurtainWall = IsExteriorCurtainWallSetAside(assumed ?? member, outcome, facts);
        var message = (exteriorCurtainWall ? $"{subject}：{ExteriorCurtainWallNote}" : $"{subject}：{outcome.Message}") +
                      (interior ? MergedAtriums.Note : string.Empty);
        var legalReference = exteriorCurtainWall ? ExteriorCurtainWallReference : outcome.LegalReference;
        var errorCode = RuleOutcomeErrorCode.For(outcome);

        var providedGap = outcome.Gaps.FirstOrDefault(g => g.Field == ProvidedField);
        if (status == ReviewStatus.InsufficientData && providedGap is not null)
        {
            if (provided.Kind == ProvidedFireRatingKind.Undeterminable && outcome.Gaps.Count == 1)
            {
                status = ReviewStatus.ManualReview;
                message = $"{subject}：設計防火時效「{provided.RawText}」{provided.Reason}，需人工覆核是否達到要求" +
                          (outcome.RequiredValue is { } required ? $" {FireRatingText.Format(required.Number)}。" : "。");
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

        var evidence = new ReviewEvidence(outcome.Evidence.Items
            .Concat(member.EvidenceFor(zone.ZoneId).Items)
            .Concat(interior ? new[] { merged!.Evidence() } : Enumerable.Empty<ReviewEvidenceItem>())
            .Concat(ProvidedEvidence(observation, typeRating, provided))
            .Concat(InputEvidence(supplied))
            .Concat(GapEvidence(outcome)));

        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.FireResistance,
            new[] { observation.Source.ElementUniqueId }, zone.ZoneIdText, status, outcome.ActualValue, outcome.RequiredValue,
            outcome.RuleId, outcome.RuleVersion, legalReference, message, evidence);
        return new MemberRatingFinding(result, observation.Source.ElementUniqueId, observation.Category,
            observation.TypeUniqueId, observation.TypeName, provided, outcome, null, errorCode, exteriorCurtainWall);
    }

    /// <summary>
    /// Where an 外牆帷幕牆's fire requirements are actually reviewed. It is not a 區劃牆壁 of 第79條第1項
    /// — <c>tw-bcr-79-wall-rating</c> sets it aside — and its own rules are the curtain-wall ones, which
    /// the 帷幕牆區劃交接 check answers (docs/tools/fire-review/rules/curtain-wall-fire-compartment.md §5).
    /// </summary>
    public const string ExteriorCurtainWallReference =
        "建築技術規則建築設計施工編第79條第3項、第4項；建築技術規則建築設計施工編第79條之3；" +
        "建築技術規則建築設計施工編第79條之4（外牆帷幕牆依帷幕牆區劃交接原則檢討，不檢討構件防火時效）";

    public const string ExteriorCurtainWallNote =
        "外牆帷幕牆不是第79條第1項的區劃牆壁，不檢討構件防火時效。依帷幕牆的判定原則，其防火要求在區劃與帷幕牆的交接處：" +
        "第79條第3項、第4項（區劃牆壁及樓地板與帷幕牆交接）、第79條之3（層間帶）、第79條之4（其他部分外牆），" +
        "請見「帷幕牆區劃交接」檢討項目。";

    /// <summary>
    /// A 不適用 that is the 外牆帷幕牆 exclusion and nothing else: the building is 防火構造 (else
    /// nothing would be asked of any wall) and no rule applied.
    /// </summary>
    private static bool IsExteriorCurtainWallSetAside(MemberCandidate member, RuleOutcome outcome, RuleFacts facts) =>
        outcome.Reason == RuleOutcomeReason.NoRuleApplies &&
        member.Observation.IsCurtainWall &&
        member.CurtainWallExposure?.IsExterior == true &&
        Equals(facts.Find("building.fireResistiveConstruction"), ReviewValue.OfBoolean(true));

    /// <summary>
    /// A member of a zone whose extent is in doubt (未封閉、重疊): whether it bounds the zone, and so
    /// what it must achieve, cannot be relied on.
    /// </summary>
    private static MemberRatingFinding Withhold(CandidateSet set, CandidateZone zone, MemberCandidate member, CompiledRuleSet ruleSet, Guid runId, Guid resultId)
    {
        var observation = member.Observation;
        var evidence = new ReviewEvidence(member.EvidenceFor(zone.ZoneId).Items.Concat(new[]
        {
            new ReviewEvidenceItem("zone.problems", ReviewValue.OfText(string.Join(",", zone.Problems)))
        }));

        var info = ruleSet.RuleSet;
        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.FireResistance,
            new[] { observation.Source.ElementUniqueId }, zone.ZoneIdText, ReviewStatus.ManualReview, null, null,
            info.RuleSetId, info.Version, info.FallbackLegalReference,
            $"{CandidateCategories.Label(observation.Category)}「{observation.TypeName ?? "無 Type 名稱"}」（{observation.Source}）" +
            $"不檢討防火時效：區劃「{zone.Name}」的範圍有問題（{string.Join("、", zone.Problems)}），無法確認構件與區劃的關係，需人工覆核。",
            evidence);
        return new MemberRatingFinding(result, observation.Source.ElementUniqueId, observation.Category,
            observation.TypeUniqueId, observation.TypeName, null, null, null, ReviewErrorCode.CandidateZoneUnusable);
    }

    /// <summary>
    /// The geometric doubts that are only about <i>whether</i> the member bounds the zone: the Area
    /// edge runs along a column face, or inside a wall's thickness off its centreline, or the member's
    /// centreline crosses the edge with neither side on it (驗證清單 C-18). Linked elements, missing
    /// geometry and doubtful zones are doubts about the data itself and are never settled.
    /// </summary>
    private static bool IsBoundaryDoubt(CandidateAmbiguityKind kind) => BoundaryDoubts.Is(kind);

    /// <summary>
    /// Settles a boundary doubt when the law gives the same answer either way. The rules are evaluated
    /// once as if the member bounds the zone (第79條：區劃牆壁一小時) and once as if it does not; only
    /// <c>element.isCompartmentBoundary</c> differs between the two. The doubt does not matter when
    /// <list type="bullet">
    /// <item>both give the same verdict — e.g. a structural column, whose 第70條 rating does not depend on
    /// the compartment at all (第79條 asks the 牆壁 for one hour, never the column); or</item>
    /// <item>one passes and the other has no requirement — the member meets whatever applies.</item>
    /// </list>
    /// Otherwise it stays ManualReview: one passes and one fails, data is missing on either side (the two
    /// sides may lack different fields), or a side is itself a manual review (composite rating, rule conflict).
    /// </summary>
    private static MemberRatingFinding? Settled(
        CandidateSet set,
        MemberCandidate member,
        CandidateAmbiguity ambiguity,
        FireResistanceInputs inputs,
        RuleEngine engine,
        RuleEvaluationContext context,
        Guid runId,
        Guid resultId,
        MergedAtriums merged)
    {
        if (!IsBoundaryDoubt(ambiguity.Kind) || ambiguity.ZoneId is not Guid zoneId) return null;

        // 室內外未定的帷幕牆不能走這條路。tw-bcr-79-wall-rating 版本 3 的 appliesWhen 排除 Unknown，
        // 所以「假設它構成邊界」與「假設它不構成」兩支都評成不適用，下面的「不論是否構成區劃邊界，
        // 規則的結論都相同」便把一個**真正的空缺**讀成一致的答案，逕行判定不適用——連人工覆核都不留。
        // 這正是 Assuming 的 remarks 寫下要防的那個機制，只是那裡防的是判定被複本丟掉，這裡是判定
        // 本來就判不出來。回 null 讓那一列保持 ambiguity 自己的人工覆核（此時 AmbiguousMember 找得到
        // 它，kind 是 BoundaryOffCenterline／BoundaryAlongOutline／BoundaryCrossed，訊息講的是邊界
        // 存疑而不是室內外；那不是最精準的說法，但比一列靜默的不適用好得多，而且使用者修邊界時室內外
        // 通常跟著判得出來）。
        //
        // 這道防線只擋「兩支都不適用」這一種走法。同一類失效還有另一個入口，是在這個函式之外被堵上
        // 的：一道**跨越**區劃邊界的牆原本連疑義都沒有（Crossing 不帶 CandidateAmbiguityKind），於是
        // 整列走主迴圈的 Decide，isCompartmentBoundary 逕取 false，第79條第1項靜默不適用，連
        // Settled 都進不來（驗證清單 C-18）。那一個已由 CandidateResolver 在源頭掛上
        // BoundaryCrossed、並納入 IsBoundaryDoubt 修掉：這類牆現在一律走到這裡評兩支，所以下面
        // 「兩支結論相同」「一支符合一支不適用」的推理本身就是那個缺口的答案，不必在此另設防線。
        if (member.CurtainWallExposure?.IsUnknown == true) return null;

        if (set.Zone(zoneId) is not { IsClear: true } zone) return null;

        // A line on the face of an atrium's railing is in doubt only about whether it bounds; between
        // an exempt 挑空 and its 連通區劃 it is interior either way (決議 37).
        if (merged.IsInterior(member))
        {
            var interior = Decide(set, zone, member, inputs, engine, context, runId, resultId,
                Assuming(member, zoneId, ZoneRelationKind.Inside), merged, interiorOverride: true);
            return new MemberRatingFinding(interior.Result, interior.ElementUniqueId, interior.Category, interior.TypeUniqueId,
                interior.TypeName, interior.Provided, interior.Outcome, ambiguity, interior.ErrorCode, interior.IsExteriorCurtainWall);
        }

        var asBoundary = Decide(set, zone, member, inputs, engine, context, runId, resultId, Assuming(member, zoneId, ZoneRelationKind.Boundary));
        var asNot = Decide(set, zone, member, inputs, engine, context, runId, resultId, Assuming(member, zoneId, ZoneRelationKind.Inside));

        MemberRatingFinding chosen;
        string reason;
        if (asBoundary.Status == asNot.Status && IsVerdict(asBoundary.Status))
        {
            chosen = asBoundary;
            reason = "不論此構件是否構成區劃邊界，規則的結論都相同";
        }
        else if (asBoundary.Status == ReviewStatus.Pass && asNot.Status == ReviewStatus.NotApplicable)
        {
            chosen = asBoundary;
            reason = "若構成區劃邊界則符合第79條區劃牆壁之要求，若不構成邊界則無此要求，兩種情形皆無不符";
        }
        else if (asNot.Status == ReviewStatus.Pass && asBoundary.Status == ReviewStatus.NotApplicable)
        {
            chosen = asNot;
            reason = "兩種情形皆無不符";
        }
        else
        {
            return null;
        }

        var label = CandidateCategories.Label(member.Category);
        var decided = chosen.Result;
        var evidence = new ReviewEvidence(decided.Evidence.Items.Concat(new[]
        {
            new ReviewEvidenceItem("candidate.settledBy", ReviewValue.OfText("SameVerdictEitherWay")),
            new ReviewEvidenceItem("candidate.verdictIfBoundary", ReviewValue.OfText(ReviewStatusText.Label(asBoundary.Status))),
            new ReviewEvidenceItem("candidate.verdictIfNotBoundary", ReviewValue.OfText(ReviewStatusText.Label(asNot.Status)))
        }));
        var result = new ReviewResult(decided.ResultId, decided.RunId, decided.PackageId, decided.CheckType,
            decided.SubjectUniqueIds, decided.ZoneId, decided.Status, decided.ActualValue, decided.RequiredValue,
            decided.RuleId, decided.RuleVersion, decided.LegalReference,
            $"{decided.Message}（區劃「{zone.Name}」的邊界與此{label}的關係無法由幾何判定，但{reason}，故逕行判定，免人工覆核。）",
            evidence);
        return new MemberRatingFinding(result, chosen.ElementUniqueId, chosen.Category, chosen.TypeUniqueId, chosen.TypeName,
            chosen.Provided, chosen.Outcome, ambiguity, chosen.ErrorCode, chosen.IsExteriorCurtainWall);
    }

    /// <summary>A verdict the law gives outright; 資料不足 and 人工覆核 are not, so two of them never settle a doubt.</summary>
    private static bool IsVerdict(ReviewStatus status) =>
        status is ReviewStatus.Pass or ReviewStatus.Fail or ReviewStatus.NotApplicable;

    /// <summary>
    /// The member as if its relation to one zone were decided; every other relation as observed.
    /// </summary>
    /// <remarks>
    /// The 室內外 classification is carried over, and it has to be: this stands in for the member in
    /// <see cref="Decide"/>, which builds the facts, and <c>tw-bcr-79-wall-rating</c> 版本 3 reads
    /// <c>element.curtainWallExposure</c> off it. Dropping it would read as <c>Unknown</c> — making
    /// both branches 不適用, which <see cref="Settled"/> would then take as「不論是否構成邊界結論都
    /// 相同」and file as 不適用 without even a 人工覆核. A 室內 curtain wall whose Area boundary runs
    /// along its face instead of through it would silently lose its 第79條第1項 rating check
    /// (docs/tools/fire-review/rules/curtain-wall-fire-compartment.md §4.8).
    /// </remarks>
    private static MemberCandidate Assuming(MemberCandidate member, Guid zoneId, ZoneRelationKind kind) =>
        new MemberCandidate(
            member.Observation,
            member.Relations.Select(r => r.ZoneId != zoneId
                ? r
                : new ZoneRelation(zoneId, kind, r.Message, r.Measurements)),
            member.CurtainWallExposure);

    /// <summary>
    /// 室內外未定的帷幕牆與某區劃的 boundary 關係，而且「問室內外」確實是這一列唯一缺的答案：
    /// 主迴圈不為它作答，由 ambiguity 迴圈出那一列人工覆核。
    /// </summary>
    /// <remarks>
    /// <para>
    /// skip（主迴圈）與查找（<see cref="ExposureUndecidedMember"/>）兩端共用這個述詞，不是為了少寫
    /// 一次：兩端的條件一旦有一邊寬一邊嚴，那一列就會兩邊落空（主迴圈跳掉、ambiguity 迴圈又比不到）
    /// 而靜默消失，或兩邊都收而出兩列。共用讓「跳掉幾列就補回幾列」成為結構保證。
    /// </para>
    /// <para>
    /// 只認 boundary，是因為 <see cref="CandidateResolver"/> 只為 boundary 關係登
    /// <see cref="CandidateAmbiguityKind.CurtainWallExposureUndecided"/>；facade 關係沒有那一列，
    /// 跳掉就真的是靜默略過了。
    /// </para>
    /// <para>
    /// 後三個條件是護欄：它們成立時，這一列本來就只會是「無規則適用」的不適用，換成人工覆核才是
    /// 淨賺。它們不成立時，原本那一列講的是**別的、而且更可執行的**事，不能被「問室內外」蓋掉——
    /// </para>
    /// <list type="bullet">
    /// <item>區劃範圍有問題（未封閉／重疊）時原本走 <see cref="Withhold"/>。因果方向是區劃形狀決定
    /// 室內外判定（<c>CandidateResolver.CurtainWallExposureOf</c> 量的就是區劃），所以區劃壞掉時
    /// 「判不出室內外」只是症狀，「先去修區劃邊界」才是病因；而且 errorCode 會從
    /// <c>CandidateZoneUnusable</c> 變成帷幕牆的那一個，構件防火時效這張表從此看不到區劃壞掉的線索。</item>
    /// <item>非防火構造建築物：六條 FireResistance 規則的 appliesWhen 全都以
    /// <c>building.fireResistiveConstruction == true</c> 開頭，第79條對這棟樓根本不適用，每一列都該是
    /// 不適用。這與 <see cref="IsExteriorCurtainWallSetAside"/> 要求同一個事實為 true 是同一道護欄，
    /// 兩者必須對稱。</item>
    /// <item>防火構造沒填：原本走 UndecidedApplicability 的資料不足，訊息指名「要填防火構造」。那是
    /// 未填模型最常見的狀態，也是使用者真正能動手的那個 gap，不能換成一個他無從下手的室內外問題。</item>
    /// <item>merged 挑空的內部線（決議 37）：那條線兩種假設下都是內部，原本出不適用加
    /// <c>MergedAtriums.Note</c>。這一條目前是**防禦性**的：<c>MergedAtriums.IsInterior</c> 要求沿線
    /// 每一處都是「挑空｜連通區劃」，那正是幾何判 Interior 的條件，而室內外判定在幾何判 Interior 時
    /// 不會回 Unknown。兩者是各自取樣的兩套實作（深度與間距都不同），所以留著這一條，讓它們在病態
    /// 模型上答案分歧時仍然是挑空那一列勝出，而不是突然冒出一個室內外問題。</item>
    /// </list>
    /// </remarks>
    private static bool IsExposureUndecidedBoundary(
        CandidateSet set,
        MemberCandidate member,
        ZoneRelation relation,
        FireResistanceInputs inputs,
        MergedAtriums merged) =>
        relation.IsBoundary &&
        member.Observation.IsCurtainWall &&
        member.CurtainWallExposure?.IsUnknown == true &&
        set.Zone(relation.ZoneId) is { IsClear: true } &&
        IsFireResistiveConstruction(inputs, relation.ZoneId) &&
        !merged.IsInterior(member);

    /// <summary>
    /// <c>building.fireResistiveConstruction</c> 已知為 true，讀法與規則引擎看到的完全一致。
    /// </summary>
    /// <remarks>
    /// 主迴圈那個時點 facts 還沒建（<see cref="Decide"/> 才建），所以不能像
    /// <see cref="IsExteriorCurtainWallSetAside"/> 那樣 <c>facts.Find</c>，只能從輸入讀。為了語意相同，
    /// 這裡照 <see cref="Decide"/> 建 facts 的順序串接 building 與該區劃的輸入並取**最後**一筆——
    /// <c>RuleFacts.Set</c> 後蓋前，所以最後一筆才是引擎會看到的值。未填（找不到）與讀不懂
    /// （<c>IsUnreadable</c>）都回 false：那兩種情形原本各有自己更可執行的那一列。
    /// </remarks>
    private static bool IsFireResistiveConstruction(FireResistanceInputs inputs, Guid zoneId) =>
        inputs.Context.Building.Concat(inputs.Context.ForZone(zoneId))
            .LastOrDefault(i => string.Equals(i.Field, "building.fireResistiveConstruction", StringComparison.Ordinal))
            is { IsUnreadable: false } supplied &&
        Equals(supplied.Value, ReviewValue.OfBoolean(true));

    /// <summary>
    /// <see cref="CandidateAmbiguityKind.CurtainWallExposureUndecided"/> 所指的那道帷幕牆。
    /// </summary>
    /// <remarks>
    /// 這個 ambiguity 掛在**boundary** 關係上，不是 Ambiguous 關係，而那個 boundary 關係自己的
    /// <c>Ambiguity</c> 是 null（關係本身已確定，只有室內外存疑），所以 <c>CarriesAmbiguity</c> 為
    /// false、<see cref="AmbiguousMember"/> 永遠比不到它；少了這條查找路徑，上面的迴圈會 <c>continue</c>，
    /// 規格要的那一列人工覆核就被丟掉。故意另開一個函式而不放寬 <see cref="AmbiguousMember"/>：
    /// 後者的語意是「關係本身存疑」，那一條路通往 <see cref="Settled"/> 的「不論是否構成邊界結論
    /// 都相同」推理，混進一個關係其實已確定、只有室內外存疑的構件會把那層推理弄亂。
    /// </remarks>
    private static MemberCandidate? ExposureUndecidedMember(
        CandidateSet set, CandidateAmbiguity ambiguity, FireResistanceInputs inputs, MergedAtriums merged)
    {
        if (ambiguity.Kind != CandidateAmbiguityKind.CurtainWallExposureUndecided) return null;
        if (ambiguity.ZoneId is not Guid zoneId || ambiguity.SubjectUniqueIds.Count != 1) return null;
        return set.Members.FirstOrDefault(m =>
            string.Equals(m.Source.ElementUniqueId, ambiguity.SubjectUniqueIds[0], StringComparison.Ordinal) &&
            m.RelationTo(zoneId) is { } relation && IsExposureUndecidedBoundary(set, m, relation, inputs, merged));
    }

    /// <summary>
    /// The member an ambiguity is about, when the relation itself is the thing in doubt.
    /// </summary>
    /// <remarks>
    /// 比的是「這個關係掛著這一筆疑義」（<see cref="ZoneRelation.CarriesAmbiguity"/>）而不是
    /// 「這個關係是 Ambiguous」：跨越區劃邊界的牆關係是 <see cref="ZoneRelationKind.Crossing"/>，
    /// 疑義卻是 <see cref="CandidateAmbiguityKind.BoundaryCrossed"/>（驗證清單 C-18），比 IsAmbiguous
    /// 就永遠比不到它，主迴圈跳掉的那一列會憑空消失。
    /// <see cref="CandidateAmbiguityKind.CurtainWallExposureUndecided"/> 仍然比不到——它掛在一個
    /// <c>Boundary</c> 關係上，而那個關係的 <c>Ambiguity</c> 是 null（關係本身已確定，只有室內外存疑），
    /// 所以它照舊走 <see cref="ExposureUndecidedMember"/>，不會混進 <see cref="Settled"/> 的
    /// 「不論是否構成邊界結論都相同」推理。
    /// </remarks>
    private static MemberCandidate? AmbiguousMember(CandidateSet set, CandidateAmbiguity ambiguity)
    {
        if (ambiguity.ZoneId is not Guid zoneId || ambiguity.SubjectUniqueIds.Count != 1) return null;
        return set.Members.FirstOrDefault(m =>
            string.Equals(m.Source.ElementUniqueId, ambiguity.SubjectUniqueIds[0], StringComparison.Ordinal) &&
            m.RelationTo(zoneId) is { CarriesAmbiguity: true } relation &&
            relation.Ambiguity == ambiguity.Kind);
    }

    /// <summary>
    /// A member without plan geometry is not in the set, only named by its evidence; it joins the Type
    /// its category and Type name identify, when exactly one Type in the set carries that name.
    /// </summary>
    private static string? TypeUniqueIdOf(CandidateSet set, CandidateCategory? category, string? typeName)
    {
        if (category is null || typeName is null) return null;
        var ids = set.Members
            .Where(m => m.Category == category && string.Equals(m.Observation.TypeName, typeName, StringComparison.Ordinal))
            .Select(m => m.Observation.TypeUniqueId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return ids.Count == 1 ? ids[0] : null;
    }

    private static IEnumerable<TypeRatingSummary> Summarize(IReadOnlyList<MemberRatingFinding> findings, FireResistanceInputs inputs) =>
        findings
            .GroupBy(f => (f.Category, Key: f.TypeUniqueId is not null ? "uid:" + f.TypeUniqueId : "name:" + (f.TypeName ?? string.Empty)))
            .OrderBy(g => g.Key.Category.HasValue ? (int)g.Key.Category.Value : int.MaxValue)
            .ThenBy(g => g.First().TypeName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Key, StringComparer.Ordinal)
            .Select(g => new TypeRatingSummary(g.Key.Category, g.First().TypeUniqueId, g.First().TypeName,
                inputs.ForType(g.First().TypeUniqueId), g));

    private static IEnumerable<ReviewEvidenceItem> ProvidedEvidence(MemberObservation observation, TypeFireRating? typeRating, ProvidedFireRating provided)
    {
        if (observation.TypeUniqueId is not null)
            yield return new ReviewEvidenceItem("source.typeUniqueId", ReviewValue.OfText(observation.TypeUniqueId));
        yield return new ReviewEvidenceItem("provided.kind", ReviewValue.OfText(provided.Kind.ToString()));
        if (typeRating is not null)
            yield return new ReviewEvidenceItem("provided.parameter", ReviewValue.OfText(typeRating.Source));
        if (provided.RawText is not null)
            yield return new ReviewEvidenceItem("provided.raw", ReviewValue.OfText(provided.RawText));
        if (provided.Reason is not null)
            yield return new ReviewEvidenceItem("provided.reason", ReviewValue.OfText(provided.Reason));
    }

    private static IEnumerable<ReviewEvidenceItem> InputEvidence(IEnumerable<ReviewInput> inputs) =>
        inputs.Where(x => x.Source is not null)
            .Select(x => new ReviewEvidenceItem($"source[{x.Field}]", ReviewValue.OfText(x.Source!)));

    private static IEnumerable<ReviewEvidenceItem> GapEvidence(RuleOutcome outcome) =>
        outcome.Gaps.Count == 0
            ? Enumerable.Empty<ReviewEvidenceItem>()
            : new[] { new ReviewEvidenceItem("rule.gaps", ReviewValue.OfText(string.Join("、", outcome.Gaps))) };

    private static string? Text(ReviewValue? value) => value is { Kind: ReviewValueKind.Text } text ? text.Text : null;

    private static CandidateCategory? CategoryOf(ReviewValue? value)
    {
        var text = Text(value);
        return text is null
            ? null
            : CandidateCategories.Members.Where(c => CandidateCategories.RuleText(c) == text).Select(c => (CandidateCategory?)c).FirstOrDefault();
    }
}
