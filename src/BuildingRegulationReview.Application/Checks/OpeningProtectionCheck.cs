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

/// <summary>The rows of the 防火門窗 statistics (spec 11.7: 依門／窗／幕牆統計).</summary>
public enum OpeningGroup
{
    Door,
    Window,

    /// <summary>Curtain panels, and doors or windows set in a curtain wall.</summary>
    CurtainWall
}

public static class OpeningGroups
{
    public static IReadOnlyList<OpeningGroup> All { get; } =
        new ReadOnlyCollection<OpeningGroup>(new[] { OpeningGroup.Door, OpeningGroup.Window, OpeningGroup.CurtainWall });

    public static string Label(OpeningGroup group) => group switch
    {
        OpeningGroup.Door => "門",
        OpeningGroup.Window => "窗",
        OpeningGroup.CurtainWall => "幕牆",
        _ => throw new ArgumentOutOfRangeException(nameof(group))
    };

    internal static OpeningGroup Of(CandidateCategory category, bool inCurtainWall) =>
        category == CandidateCategory.CurtainPanel || inCurtainWall ? OpeningGroup.CurtainWall
        : category == CandidateCategory.Window ? OpeningGroup.Window
        : OpeningGroup.Door;
}

/// <summary>The 防火門窗 verdict for one opening in one zone, or for an opening whose relation is ambiguous.</summary>
public sealed class OpeningProtectionFinding
{
    internal OpeningProtectionFinding(
        ReviewResult result,
        string elementUniqueId,
        CandidateCategory? category,
        OpeningGroup? group,
        string? typeUniqueId,
        string? typeName,
        string? hostUniqueId,
        OpeningFireProtection? source,
        ProvidedFireProtection? provided,
        RuleOutcome? outcome,
        CandidateAmbiguity? ambiguity,
        string? errorCode)
    {
        Result = result;
        ElementUniqueId = elementUniqueId;
        Category = category;
        Group = group;
        TypeUniqueId = typeUniqueId;
        TypeName = typeName;
        HostUniqueId = hostUniqueId;
        Source = source;
        Provided = provided;
        Outcome = outcome;
        Ambiguity = ambiguity;
        ErrorCode = errorCode;
    }

    public ReviewResult Result { get; }
    public ReviewStatus Status => Result.Status;
    public Guid? ZoneId => Result.ZoneId is null ? (Guid?)null : Guid.Parse(Result.ZoneId);
    public string ElementUniqueId { get; }

    /// <summary>Door, Window or CurtainPanel; null only when an ambiguity did not say.</summary>
    public CandidateCategory? Category { get; }

    /// <summary>The statistics row; null only when the category is unknown.</summary>
    public OpeningGroup? Group { get; }

    public string? TypeUniqueId { get; }
    public string? TypeName { get; }
    public string? HostUniqueId { get; }

    /// <summary>Where the design value came from (instance or Type, and the parameter), when one was read.</summary>
    public OpeningFireProtection? Source { get; }

    /// <summary>The design fire protection as read; null when the opening never reached the rules.</summary>
    public ProvidedFireProtection? Provided { get; }

    /// <summary>What the rule engine said, or null when no rule ran (ambiguity or zone problem).</summary>
    public RuleOutcome? Outcome { get; }

    public CandidateAmbiguity? Ambiguity { get; }

    /// <summary>The spec 14 code a log entry about this opening carries; null for a routine verdict.</summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Whether the rules require this opening to be protected (spec 11.6 step 2): true when a rule
    /// applied, false when none did, null when that could not be decided.
    /// </summary>
    public bool? RequiresProtection { get; internal set; }

    public override string ToString() => $"{ElementUniqueId}: {ReviewStatusText.Label(Status)} — {Result.Message}";
}

/// <summary>One row of the 防火門窗 statistics: the results of all doors, windows or curtain-wall openings.</summary>
public sealed class OpeningGroupSummary
{
    internal OpeningGroupSummary(OpeningGroup group, IEnumerable<OpeningProtectionFinding> findings)
    {
        Group = group;
        Findings = new ReadOnlyCollection<OpeningProtectionFinding>(findings.ToList());
        ElementUniqueIds = new ReadOnlyCollection<string>(Findings.Select(f => f.ElementUniqueId)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());
        Status = ReviewStatusSeverity.Worst(Findings.Select(f => f.Status));
    }

    public OpeningGroup Group { get; }
    public IReadOnlyList<OpeningProtectionFinding> Findings { get; }

    /// <summary>Each opening once, even when it lies on the boundary of two zones.</summary>
    public IReadOnlyList<string> ElementUniqueIds { get; }

    public int OpeningCount => ElementUniqueIds.Count;

    /// <summary>Fail when any fails, otherwise the most doubtful state; 未檢討 when the group is empty.</summary>
    public ReviewStatus Status { get; }

    public int Count(ReviewStatus status) => Findings.Count(f => f.Status == status);

    public override string ToString() => $"{OpeningGroups.Label(Group)} ×{OpeningCount}：{ReviewStatusText.Label(Status)}";
}

/// <summary>The 防火門窗 results of one run.</summary>
public sealed class OpeningProtectionReview
{
    internal OpeningProtectionReview(IEnumerable<OpeningProtectionFinding> findings, IEnumerable<string> warnings)
    {
        Findings = new ReadOnlyCollection<OpeningProtectionFinding>(findings.ToList());
        Groups = new ReadOnlyCollection<OpeningGroupSummary>(OpeningGroups.All
            .Select(g => new OpeningGroupSummary(g, Findings.Where(f => f.Group == g))).ToList());
        Warnings = new ReadOnlyCollection<string>(warnings.ToList());
    }

    /// <summary>One per opening and zone, plus one per ambiguous opening relation; zone, category, element order.</summary>
    public IReadOnlyList<OpeningProtectionFinding> Findings { get; }

    public IEnumerable<ReviewResult> Results => Findings.Select(x => x.Result);

    /// <summary>Door, window and curtain-wall rows, always all three, in that order.</summary>
    public IReadOnlyList<OpeningGroupSummary> Groups { get; }

    public IReadOnlyList<string> Warnings { get; }

    public OpeningGroupSummary Group(OpeningGroup group) => Groups.First(g => g.Group == group);

    public IEnumerable<OpeningProtectionFinding> For(string elementUniqueId) =>
        Findings.Where(f => string.Equals(f.ElementUniqueId, elementUniqueId, StringComparison.Ordinal));

    public OpeningProtectionFinding? For(string elementUniqueId, Guid zoneId) =>
        For(elementUniqueId).FirstOrDefault(f => f.ZoneId == zoneId);

    public int Count(ReviewStatus status) => Findings.Count(x => x.Status == status);
}

/// <summary>
/// 防火門窗 (spec 11.6). For every opening the candidate set relates to a zone — by default the doors
/// and windows hosted in its boundary walls:
/// <list type="number">
/// <item>An ambiguous relation (curtain wall, not hosted, host not read, linked…) is already a
/// ManualReview under the MVP policy (P3-T03); an opening of a zone whose extent is in doubt is
/// ManualReview too. No rule runs on either.</item>
/// <item>Otherwise the facts are the opening's kind and host relation, the building and zone inputs,
/// and its design protection as <c>opening.providedFireProtection</c> (是／否). The rules decide first
/// whether the opening must be protected at all — lying on a boundary is not enough by itself.</item>
/// <item>是 where protection is required passes; 否 fails; 未設定 or an unreadable value is 資料不足;
/// where no rule applies the result is 不適用, with the facts the rules looked at as evidence.</item>
/// </list>
/// </summary>
public static class OpeningProtectionCheck
{
    private const string ProvidedField = "opening.providedFireProtection";
    private const string InsulationField = "opening.providedInsulation";

    /// <summary>
    /// Leads every message of a facade opening, so the 檢討表 search finds all of them: 「外側沒有區劃」
    /// is an inference, not a fact the model states, and these rows are the ones to spot-check.
    /// </summary>
    public const string FacadeInferredTag = "【外牆推定】";

    public static Result<OpeningProtectionReview> Review(
        CandidateSet set,
        OpeningProtectionInputs inputs,
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

        var ready = ReviewPreconditions.Zones(set, "防火門窗");
        if (ready.IsFailure) return Result.Failure<OpeningProtectionReview>(ready.Error);

        var warnings = inputs.Context.ZoneIds.Where(id => set.Zone(id) is null)
            .Select(id => $"輸入資料指定的區劃 {id:D} 不在此工作包中，已略過。")
            .Concat(inputs.Protections.Where(p => !set.Openings.Any(o => Matches(p, o.Observation)))
                .Select(p => p.Scope == FireProtectionScope.Instance
                    ? $"開口 {p.UniqueId} 有防火保護資料，但它不是此工作包的候選開口。"
                    : $"Type {p.UniqueId} 有防火保護資料，但此工作包沒有該 Type 的候選開口。"))
            .ToList();

        // 第79條之2第3項: an opening between a merged 挑空 and its 連通區劃 is no 區劃開口 (決議 37).
        var merged = MergedAtriums.Of(set, inputs.Context);

        var findings = new List<OpeningProtectionFinding>();
        foreach (var zone in set.Zones)
        {
            foreach (var opening in set.OpeningsOf(zone.ZoneId))
            {
                var relation = opening.RelationTo(zone.ZoneId)!;
                if (relation.IsAmbiguous) continue;

                findings.Add(!zone.IsClear ? Withhold(set, zone, opening, engine.RuleSet, runId, newResultId())
                    : relation.IsFacade ? Facade(set, zone, opening, relation, engine.RuleSet, runId, newResultId())
                    : Decide(set, zone, opening, inputs, engine, context, runId, newResultId(), merged));
            }
        }

        foreach (var ambiguity in set.Ambiguities)
        {
            var opening = AmbiguousOpening(set, ambiguity);

            // An opening whose host lies on a line drawn on its face, between an exempt 挑空 and its
            // 連通區劃, is interior either way (決議 37).
            if (opening is not null && ambiguity.ZoneId is Guid ambiguousZone &&
                set.Zone(ambiguousZone) is { IsClear: true } clearZone && merged.IsInterior(opening))
            {
                findings.Add(Decide(set, clearZone, opening, inputs, engine, context, runId, newResultId(), merged,
                    Assuming(opening, ambiguousZone)));
                continue;
            }

            var category = opening?.Category ?? CategoryOf(ambiguity.Evidence.Find("source.category"));
            if (opening is null && !(ambiguity.Kind == CandidateAmbiguityKind.OpeningLocationUnknown &&
                                     category is CandidateCategory known && CandidateCategories.IsOpening(known)))
                continue;

            var result = ambiguity.ToReviewResult(newResultId(), runId, set.PackageId, ReviewCheckTypes.OpeningProtection, engine.RuleSet);
            var inCurtainWall =
                ambiguity.Kind is CandidateAmbiguityKind.CurtainWallOpening
                    or CandidateAmbiguityKind.CurtainPanelIsConstruction
                    or CandidateAmbiguityKind.CurtainPanelKindUndeclared ||
                opening?.Host?.IsCurtainWall == true;
            findings.Add(new OpeningProtectionFinding(result, ambiguity.SubjectUniqueIds[0], category,
                category.HasValue ? OpeningGroups.Of(category.Value, inCurtainWall) : (OpeningGroup?)null,
                opening?.Observation.TypeUniqueId, opening?.Observation.TypeName ?? Text(ambiguity.Evidence.Find("source.typeName")),
                opening?.Observation.HostUniqueId, null, null, null, ambiguity, ambiguity.ErrorCode));
        }

        var ordered = findings
            .OrderBy(f => f.Result.ZoneId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(f => f.Category.HasValue ? (int)f.Category.Value : int.MaxValue)
            .ThenBy(f => f.ElementUniqueId, StringComparer.Ordinal)
            .ToList();

        return Result.Success(new OpeningProtectionReview(ordered, warnings));
    }

    private static OpeningProtectionFinding Decide(
        CandidateSet set,
        CandidateZone zone,
        OpeningCandidate opening,
        OpeningProtectionInputs inputs,
        RuleEngine engine,
        RuleEvaluationContext context,
        Guid runId,
        Guid resultId,
        MergedAtriums merged,
        OpeningCandidate? assumed = null)
    {
        var observation = opening.Observation;
        var facts = CandidateFacts.ForOpening(set, assumed ?? opening, zone.ZoneId, engine.RuleSet.Catalog);
        var interior = merged.IsInterior(opening);
        if (interior) facts.Set("opening.hostIsCompartmentBoundary", false);
        var supplied = inputs.Context.Building.Concat(inputs.Context.ForZone(zone.ZoneId)).ToList();
        foreach (var input in supplied) input.ApplyTo(facts);

        var source = inputs.For(observation);
        var provided = source?.Protection ?? ProvidedFireProtection.Missing(observation.TypeUniqueId is null
            ? "開口沒有防火保護值，也沒有可讀取的 Type"
            : "開口與其 Type 都未提供防火保護");
        switch (provided.Kind)
        {
            case ProvidedFireProtectionKind.Yes:
            case ProvidedFireProtectionKind.No:
                facts.Set(ProvidedField, provided.RuleText!);
                break;
            case ProvidedFireProtectionKind.Unreadable:
                facts.MarkUnreadable(ProvidedField, $"「{provided.RawText}」{provided.Reason}");
                break;
        }

        // 第79條第1項之阻熱性 (決議 38): only asked once the opening is a 防火設備, by the higher-priority
        // tw-bcr-79-opening-insulation; a 否 here is a 未符合 of that rule, not of 防火門窗 itself.
        var insulation = inputs.InsulationFor(observation);
        switch (insulation.Kind)
        {
            case ProvidedFireProtectionKind.Yes:
            case ProvidedFireProtectionKind.No:
                facts.Set(InsulationField, insulation.RuleText!);
                break;
            case ProvidedFireProtectionKind.Unreadable:
                facts.MarkUnreadable(InsulationField, $"「{insulation.RawText}」{insulation.Reason}");
                break;
        }

        var outcome = Evaluate(engine, facts, context);
        var errorCode = RuleOutcomeErrorCode.For(outcome);
        var providedGap = outcome.Gaps.Any(g => g.Field == ProvidedField);
        var insulationGap = outcome.Gaps.Any(g => g.Field == InsulationField);
        if (outcome.Status == ReviewStatus.InsufficientData && providedGap)
            errorCode ??= provided.Kind == ProvidedFireProtectionKind.Missing ? ReviewErrorCode.ParameterMissing : ReviewErrorCode.ParameterTypeMismatch;
        if (outcome.Status == ReviewStatus.InsufficientData && insulationGap)
            errorCode ??= insulation.Kind == ProvidedFireProtectionKind.Missing ? ReviewErrorCode.ParameterMissing : ReviewErrorCode.ParameterTypeMismatch;

        var evidence = new ReviewEvidence(outcome.Evidence.Items
            .Concat(opening.EvidenceFor(zone.ZoneId).Items)
            .Concat(interior ? new[] { merged.Evidence() } : Enumerable.Empty<ReviewEvidenceItem>())
            .Concat(ProvidedEvidence(observation, source, provided))
            .Concat(new[] { new ReviewEvidenceItem("insulation.kind", ReviewValue.OfText(insulation.Kind.ToString())) })
            .Concat(InputEvidence(supplied))
            .Concat(GapEvidence(outcome)));

        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.OpeningProtection,
            new[] { observation.Source.ElementUniqueId }, zone.ZoneIdText, outcome.Status, outcome.ActualValue, outcome.RequiredValue,
            outcome.RuleId, outcome.RuleVersion, outcome.LegalReference,
            $"{Subject(observation)}於區劃「{zone.Name}」：{outcome.Message}" + (interior ? MergedAtriums.Note : string.Empty), evidence);

        return new OpeningProtectionFinding(result, observation.Source.ElementUniqueId, observation.Category,
            OpeningGroups.Of(observation.Category, opening.Host?.IsCurtainWall == true),
            observation.TypeUniqueId, observation.TypeName, observation.HostUniqueId, source, provided, outcome, null, errorCode)
        {
            RequiresProtection = outcome.Status switch
            {
                ReviewStatus.Pass or ReviewStatus.Fail => true,
                ReviewStatus.NotApplicable => false,
                // Only the design value is missing: a rule applied and stated what it requires.
                ReviewStatus.InsufficientData when providedGap && outcome.Gaps.Count == 1 && outcome.RequiredValue is not null => true,
                // A 防火設備 whose 阻熱性 is not stated is still one the 區劃 requires.
                ReviewStatus.InsufficientData when insulationGap && outcome.Gaps.Count == 1 && outcome.RequiredValue is not null => true,
                _ => null
            }
        };
    }

    /// <summary>
    /// An opening of a zone whose extent is in doubt (未封閉、重疊): whether its wall bounds the zone,
    /// and so whether it must be protected, cannot be relied on.
    /// </summary>
    private static OpeningProtectionFinding Withhold(CandidateSet set, CandidateZone zone, OpeningCandidate opening, CompiledRuleSet ruleSet, Guid runId, Guid resultId)
    {
        var observation = opening.Observation;
        var evidence = new ReviewEvidence(opening.EvidenceFor(zone.ZoneId).Items.Concat(new[]
        {
            new ReviewEvidenceItem("zone.problems", ReviewValue.OfText(string.Join(",", zone.Problems)))
        }));

        var info = ruleSet.RuleSet;
        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.OpeningProtection,
            new[] { observation.Source.ElementUniqueId }, zone.ZoneIdText, ReviewStatus.ManualReview, null, null,
            info.RuleSetId, info.Version, $"規則集「{info.Title}」",
            $"{Subject(observation)}不檢討防火保護：區劃「{zone.Name}」的範圍有問題（{string.Join("、", zone.Problems)}），" +
            "無法確認開口與區劃的關係，需人工覆核。",
            evidence);
        return new OpeningProtectionFinding(result, observation.Source.ElementUniqueId, observation.Category,
            OpeningGroups.Of(observation.Category, opening.Host?.IsCurtainWall == true),
            observation.TypeUniqueId, observation.TypeName, observation.HostUniqueId, null, null, null, null,
            ReviewErrorCode.CandidateZoneUnusable);
    }

    /// <summary>
    /// An opening in the building's outer curtain-wall facade (no zone beyond it). 第79條 divides the
    /// building with 牆壁、防火門窗等防火設備 between its parts; the outer facade is not one of those
    /// 區劃分隔, so the rule's own premise — an opening in a compartment wall — is absent and no rule
    /// runs. The facade is reviewed where the law places it: 第79條第3項、第79條之3 at the junctions and
    /// 第79條之4 for the rest (帷幕牆區劃交接). 第110條 (防火間隔) also reaches exterior openings and is
    /// outside this tool, which the message says.
    /// </summary>
    private static OpeningProtectionFinding Facade(CandidateSet set, CandidateZone zone, OpeningCandidate opening, ZoneRelation relation, CompiledRuleSet ruleSet, Guid runId, Guid resultId)
    {
        var observation = opening.Observation;
        var info = ruleSet.RuleSet;
        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.OpeningProtection,
            new[] { observation.Source.ElementUniqueId }, zone.ZoneIdText, ReviewStatus.NotApplicable, null, null,
            info.RuleSetId, info.Version, FacadeReference,
            $"{FacadeInferredTag}{Subject(observation)}於區劃「{zone.Name}」：{relation.Message}" +
            "第79條之防火門窗係指區劃分隔處之開口，外牆開口不在此列；此帷幕牆改依第79條第3項、第79條之3、第79條之4" +
            "於「帷幕牆區劃交接」檢討，本項不適用。外牆係由「帷幕牆另一側沒有任何區劃」推定，" +
            "若另一側實為未建 Area 或屬其他工作包之室內空間，此帷幕牆即為區劃分隔，請抽查。" +
            "外牆開口另受第110條防火間隔規定，本工具未檢討。",
            opening.EvidenceFor(zone.ZoneId));
        return new OpeningProtectionFinding(result, observation.Source.ElementUniqueId, observation.Category,
            OpeningGroups.Of(observation.Category, opening.Host?.IsCurtainWall == true),
            observation.TypeUniqueId, observation.TypeName, observation.HostUniqueId, null, null, null, null,
            ReviewErrorCode.CandidateFacadeInferred)
        {
            RequiresProtection = false
        };
    }

    private const string FacadeReference =
        "建築技術規則建築設計施工編第79條第1項（區劃分隔）；外牆依第79條第3項、第79條之3、第79條之4檢討";

    private static string Subject(OpeningObservation observation) =>
        $"{CandidateCategories.Label(observation.Category)}「{observation.TypeName ?? "無 Type 名稱"}」（{observation.Source}）";

    /// <summary>
    /// The engine's verdict, with one correction. tw-bcr-79-opening-insulation asks 阻熱性 only of a
    /// 防火設備, so its applicability reads 設計防火保護 (決議 38). When that is the only fact missing,
    /// the engine stops at the insulation rule's priority as 「cannot tell whether it applies」 — and an
    /// opening whose 防火門窗 nobody filled in would be filed under 阻熱性, with no required value. The
    /// question that is really open is 防火門窗 itself, so the highest rule that does apply regardless
    /// answers instead: the same 資料不足, under 第79條第1項's 防火門窗 rule, requiring 「是」.
    /// </summary>
    private static RuleOutcome Evaluate(RuleEngine engine, RuleFacts facts, RuleEvaluationContext context)
    {
        var outcome = engine.Evaluate(RuleCategory.OpeningProtection, facts, context);
        if (!outcome.IsApplicabilityUndecided || outcome.Gaps.Any(g => g.Field != ProvidedField)) return outcome;

        var fallback = engine.RuleSet.OfCategory(RuleCategory.OpeningProtection)
            .Where(r => context.IsInForce(r.Rule))
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.RuleId, StringComparer.Ordinal)
            .FirstOrDefault(r => r.AppliesWhen.Evaluate(facts).IsTrue);
        return fallback is null ? outcome : engine.EvaluateApplicable(fallback, facts);
    }

    /// <summary>The opening with its doubtful relation to one zone taken as 「not a boundary」.</summary>
    private static OpeningCandidate Assuming(OpeningCandidate opening, Guid zoneId) =>
        new OpeningCandidate(opening.Observation, opening.Relations.Select(r => r.ZoneId != zoneId
            ? r
            : new ZoneRelation(zoneId, ZoneRelationKind.Inside, r.Message, r.Measurements)), opening.Host);

    /// <summary>The opening an ambiguity is about, when it is an ambiguous opening relation.</summary>
    private static OpeningCandidate? AmbiguousOpening(CandidateSet set, CandidateAmbiguity ambiguity)
    {
        if (ambiguity.ZoneId is not Guid zoneId || ambiguity.SubjectUniqueIds.Count != 1) return null;
        return set.Openings.FirstOrDefault(o =>
            string.Equals(o.Source.ElementUniqueId, ambiguity.SubjectUniqueIds[0], StringComparison.Ordinal) &&
            o.RelationTo(zoneId) is { IsAmbiguous: true } relation &&
            relation.Ambiguity == ambiguity.Kind);
    }

    private static bool Matches(OpeningFireProtection protection, OpeningObservation opening) =>
        protection.Scope == FireProtectionScope.Instance
            ? string.Equals(protection.UniqueId, opening.Source.ElementUniqueId, StringComparison.Ordinal)
            : string.Equals(protection.UniqueId, opening.TypeUniqueId, StringComparison.Ordinal);

    private static IEnumerable<ReviewEvidenceItem> ProvidedEvidence(OpeningObservation observation, OpeningFireProtection? source, ProvidedFireProtection provided)
    {
        if (observation.TypeUniqueId is not null)
            yield return new ReviewEvidenceItem("source.typeUniqueId", ReviewValue.OfText(observation.TypeUniqueId));
        yield return new ReviewEvidenceItem("provided.kind", ReviewValue.OfText(provided.Kind.ToString()));
        if (source is not null)
        {
            yield return new ReviewEvidenceItem("provided.scope", ReviewValue.OfText(source.Scope.ToString()));
            yield return new ReviewEvidenceItem("provided.parameter", ReviewValue.OfText(source.Source));
        }
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
            : CandidateCategories.Openings.Where(c => CandidateCategories.RuleText(c) == text).Select(c => (CandidateCategory?)c).FirstOrDefault();
    }
}
