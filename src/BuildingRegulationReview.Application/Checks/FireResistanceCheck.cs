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
        string? errorCode)
    {
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
    internal FireResistanceReview(IEnumerable<MemberRatingFinding> findings, IEnumerable<TypeRatingSummary> types, IEnumerable<string> warnings)
    {
        Findings = new ReadOnlyCollection<MemberRatingFinding>(findings.ToList());
        Types = new ReadOnlyCollection<TypeRatingSummary>(types.ToList());
        Warnings = new ReadOnlyCollection<string>(warnings.ToList());
    }

    /// <summary>One per member and zone, plus one per ambiguous member relation; zone, category, element order.</summary>
    public IReadOnlyList<MemberRatingFinding> Findings { get; }

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
                if (relation.IsAmbiguous) continue;

                findings.Add(zone.IsClear
                    ? Decide(set, zone, member, inputs, engine, context, runId, newResultId(), merged: merged)
                    : Withhold(set, zone, member, engine.RuleSet, runId, newResultId()));
            }
        }

        foreach (var ambiguity in set.Ambiguities)
        {
            var member = AmbiguousMember(set, ambiguity);
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

        return Result.Success(new FireResistanceReview(ordered, Summarize(ordered, inputs), warnings));
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
        var message = $"{subject}：{outcome.Message}" + (interior ? MergedAtriums.Note : string.Empty);
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
            outcome.RuleId, outcome.RuleVersion, outcome.LegalReference, message, evidence);
        return new MemberRatingFinding(result, observation.Source.ElementUniqueId, observation.Category,
            observation.TypeUniqueId, observation.TypeName, provided, outcome, null, errorCode);
    }

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
            info.RuleSetId, info.Version, $"規則集「{info.Title}」",
            $"{CandidateCategories.Label(observation.Category)}「{observation.TypeName ?? "無 Type 名稱"}」（{observation.Source}）" +
            $"不檢討防火時效：區劃「{zone.Name}」的範圍有問題（{string.Join("、", zone.Problems)}），無法確認構件與區劃的關係，需人工覆核。",
            evidence);
        return new MemberRatingFinding(result, observation.Source.ElementUniqueId, observation.Category,
            observation.TypeUniqueId, observation.TypeName, null, null, null, ReviewErrorCode.CandidateZoneUnusable);
    }

    /// <summary>
    /// The geometric doubts that are only about <i>whether</i> the member bounds the zone: the Area
    /// edge runs along a column face, or inside a wall's thickness off its centreline. Linked elements,
    /// missing geometry and doubtful zones are doubts about the data itself and are never settled.
    /// </summary>
    private static bool IsBoundaryDoubt(CandidateAmbiguityKind kind) =>
        kind == CandidateAmbiguityKind.BoundaryAlongOutline || kind == CandidateAmbiguityKind.BoundaryOffCenterline;

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
        if (set.Zone(zoneId) is not { IsClear: true } zone) return null;

        // A line on the face of an atrium's railing is in doubt only about whether it bounds; between
        // an exempt 挑空 and its 連通區劃 it is interior either way (決議 37).
        if (merged.IsInterior(member))
        {
            var interior = Decide(set, zone, member, inputs, engine, context, runId, resultId,
                Assuming(member, zoneId, ZoneRelationKind.Inside), merged, interiorOverride: true);
            return new MemberRatingFinding(interior.Result, interior.ElementUniqueId, interior.Category, interior.TypeUniqueId,
                interior.TypeName, interior.Provided, interior.Outcome, ambiguity, interior.ErrorCode);
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
            chosen.Provided, chosen.Outcome, ambiguity, chosen.ErrorCode);
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
    /// (docs/regulations/curtain-wall-fire-compartment.md §4.8).
    /// </remarks>
    private static MemberCandidate Assuming(MemberCandidate member, Guid zoneId, ZoneRelationKind kind) =>
        new MemberCandidate(
            member.Observation,
            member.Relations.Select(r => r.ZoneId != zoneId
                ? r
                : new ZoneRelation(zoneId, kind, r.Message, r.Measurements)),
            member.CurtainWallExposure);

    /// <summary>The member an ambiguity is about, when it is an ambiguous member relation.</summary>
    private static MemberCandidate? AmbiguousMember(CandidateSet set, CandidateAmbiguity ambiguity)
    {
        if (ambiguity.ZoneId is not Guid zoneId || ambiguity.SubjectUniqueIds.Count != 1) return null;
        return set.Members.FirstOrDefault(m =>
            string.Equals(m.Source.ElementUniqueId, ambiguity.SubjectUniqueIds[0], StringComparison.Ordinal) &&
            m.RelationTo(zoneId) is { IsAmbiguous: true } relation &&
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
