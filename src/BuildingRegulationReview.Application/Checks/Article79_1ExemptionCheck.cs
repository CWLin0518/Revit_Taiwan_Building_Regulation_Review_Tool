using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// The 第79條之1 answer for one 區劃 (docs/regulations/article-79-1-area-exemption.md §3.2).
/// </summary>
public sealed class Article79_1ExemptionFinding
{
    internal Article79_1ExemptionFinding(
        CandidateZone zone,
        ReviewResult result,
        Article79_1Exemption? exemption,
        string? errorCode)
    {
        Zone = zone;
        Result = result;
        Exemption = exemption;
        ErrorCode = errorCode;
    }

    public CandidateZone Zone { get; }
    public Guid ZoneId => Zone.ZoneId;
    public ReviewResult Result { get; }
    public ReviewStatus Status => Result.Status;

    /// <summary>
    /// The judgement, and null when the 區劃 never reached it — its extent is in doubt, so no fact
    /// about it was read at all.
    /// </summary>
    public Article79_1Exemption? Exemption { get; }

    /// <summary>What counts this result: the 區劃's first Area, the same subject 區劃面積 counts.</summary>
    public string ElementUniqueId => Zone.AreaUniqueIds.FirstOrDefault() ?? Zone.ZoneIdText;

    /// <summary>The spec 14 code a log entry about this 區劃 carries; null for a routine answer.</summary>
    public string? ErrorCode { get; }

    public override string ToString() => $"{Zone.Name}: {ReviewStatusText.Label(Status)} — {Result.Message}";
}

/// <summary>The 第79條之1 results of one run: one per 區劃 that the article can reach, in 區劃 order.</summary>
public sealed class Article79_1ExemptionReview
{
    internal Article79_1ExemptionReview(IEnumerable<Article79_1ExemptionFinding> findings, IEnumerable<string> warnings)
    {
        Findings = new ReadOnlyCollection<Article79_1ExemptionFinding>(findings.ToList());
        Warnings = new ReadOnlyCollection<string>(warnings.ToList());
    }

    public IReadOnlyList<Article79_1ExemptionFinding> Findings { get; }
    public IEnumerable<ReviewResult> Results => Findings.Select(x => x.Result);
    public IReadOnlyList<string> Warnings { get; }

    public Article79_1ExemptionFinding? For(Guid zoneId) => Findings.FirstOrDefault(x => x.ZoneId == zoneId);

    public int Count(ReviewStatus status) => Findings.Count(x => x.Status == status);
}

/// <summary>
/// 第79條之1 之無法區劃分隔部分得不受第79條第1項之面積限制
/// (docs/regulations/article-79-1-area-exemption.md). A classification, not a requirement, so no rule
/// runs here — <see cref="Article79_1Exemption"/> decides and this turns its three states into a
/// result:
/// <list type="number">
/// <item>Only a 區劃 whose <c>zone.use</c> is one of the six words the article names gets a subject at
/// all (決議 10). That is almost every 區劃 filtered out, and deliberately: a 辦公 區劃 owes 第79條之1
/// nothing, and a row of empty 不適用 would bury the 觀眾席 this check exists to surface.</item>
/// <item>From the eleventh storey up there is no subject either. 第79條之1 lifts 「前條第一項」 and
/// nothing else, so a 觀眾席 on the twelfth storey is 第83條's 一○○／二○○平方公尺 and this article never
/// reaches it (§2.3) — the same answer the engine's priority tiers already give the area row.</item>
/// <item>The status is 人工覆核, 資料不足 or 不適用, never 符合 and never 未符合 (§3.5). Two of the
/// article's four elements — 自成一個區劃 and the 防火設備's 一小時阻熱性 — have no field, so the tool
/// can say the 區劃 looks like one of the two 款 but not that it is therefore lawful. Releasing the
/// area result is 人工覆寫 (spec §11.8), and the message names what the person overriding it has to
/// confirm.</item>
/// <item>The 區劃面積 result is untouched. This is a second row on the same 區劃, with the same
/// <c>SubjectUniqueIds</c>, not a change to the first one (決議 12).</item>
/// </list>
/// </summary>
public static class Article79_1ExemptionCheck
{
    private const string ZoneUseField = "zone.use";
    private const string FloorNumberField = "zone.floorNumber";
    private const string CannotBeSubdividedField = "zone.cannotBeSubdivided";
    private const string FireResistiveField = "building.fireResistiveConstruction";
    private const string BuildingUseField = "building.use";

    /// <summary>
    /// The storey from which 第83條 decides the 區劃面積 instead — <c>tw-bcr-83-area</c>'s own
    /// <c>zone.floorNumber &gt;= 11</c>, and the storey from which this check produces nothing (§2.3).
    /// </summary>
    private const int Article83FirstStorey = 11;

    /// <summary>
    /// 第79條之1 lifts 第79條第1項's area limit alone. 第83條 must not appear here: it is another
    /// article, and this article says nothing about it (§2.3、§3.2).
    /// </summary>
    public const string LegalReference = "建築技術規則建築設計施工編第79條之1（無法區劃分隔部分得不受第79條第1項限制）";

    /// <summary>The fields the judgement reads, in the order §3.2 lists them as evidence.</summary>
    private static readonly string[] ReadFields =
        { "zone.id", ZoneUseField, BuildingUseField, FireResistiveField, CannotBeSubdividedField, FloorNumberField };

    public static Result<Article79_1ExemptionReview> Review(
        CandidateSet set,
        CompartmentAreaInputs inputs,
        CompiledRuleSet ruleSet,
        Guid runId,
        Func<Guid>? newResultId = null)
    {
        if (set is null) throw new ArgumentNullException(nameof(set));
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));
        if (ruleSet is null) throw new ArgumentNullException(nameof(ruleSet));
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
        newResultId ??= Guid.NewGuid;

        var ready = ReviewPreconditions.Zones(set, "第79條之1之免除");
        if (ready.IsFailure) return Result.Failure<Article79_1ExemptionReview>(ready.Error);

        var warnings = inputs.ZoneIds.Where(id => set.Zone(id) is null)
            .Select(id => $"輸入資料指定的區劃 {id:D} 不在此工作包中，已略過。")
            .ToList();

        var findings = new List<Article79_1ExemptionFinding>();
        foreach (var zone in set.Zones)
        {
            var supplied = inputs.Building.Concat(inputs.ForZone(zone.ZoneId)).ToList();

            // 決議 10: a 用途 outside the six words produces no subject. The judgement still answers
            // 不適用 for such a 區劃 — that answer is for the panel, which shows every 區劃 — but the
            // review table would drown in it.
            var use = Text(supplied, ZoneUseField);
            if (!ZoneUses.IsArticle79_1Use(use)) continue;

            // §2.3: from the eleventh storey up 第83條 decides the area, and 第79條之1 does not reach
            // it. A storey nobody filled in is not this branch — it is a gap, and Decide reports it.
            var floorNumber = FloorNumber(supplied);
            if (floorNumber >= Article83FirstStorey) continue;

            findings.Add(zone.IsClear
                ? Decide(set, zone, supplied, use!, floorNumber, ruleSet, runId, newResultId())
                : Withhold(set, zone, use!, ruleSet, runId, newResultId()));
        }

        return Result.Success(new Article79_1ExemptionReview(findings, warnings));
    }

    private static Article79_1ExemptionFinding Decide(
        CandidateSet set,
        CandidateZone zone,
        IReadOnlyList<ReviewInput> supplied,
        string use,
        int? floorNumber,
        CompiledRuleSet ruleSet,
        Guid runId,
        Guid resultId)
    {
        var fireResistive = Flag(supplied, FireResistiveField);
        var buildingUse = Text(supplied, BuildingUseField);
        var cannotBeSubdivided = Flag(supplied, CannotBeSubdividedField);

        var exemption = Article79_1Exemption.For(fireResistive, buildingUse, use, cannotBeSubdivided);

        // 樓層序 decides which article limits the area, so it decides what an exemption from
        // 第79條第1項 would even be worth. It is asked for last and only when the exemption might
        // hold: a 區劃 that claims nothing under 第79條之1 is 不適用 whichever article applies to it,
        // which is §3.5's 「已確定不成立的要素讓其他缺口不必再問」 one level up.
        var floorIsGap = floorNumber is null && !exemption.IsInapplicable;

        var status = floorIsGap ? ReviewStatus.InsufficientData
            : exemption.Holds ? ReviewStatus.ManualReview
            : exemption.IsUndecided ? ReviewStatus.InsufficientData
            : ReviewStatus.NotApplicable;

        var subject = $"區劃「{zone.Name}」";
        var message = floorIsGap
            ? $"{subject}：缺樓層序，無法判定本區劃之區劃面積係依第79條第1項或第83條；十一層以上部分由第83條作答，第79條之1不適用。"
            : $"{subject}：{exemption.Description}";

        var errorCode = status == ReviewStatus.InsufficientData ? ReviewErrorCode.ParameterMissing : null;

        var evidence = new ReviewEvidence(
            ZoneEvidence(zone)
                .Concat(FactEvidence(fireResistive, buildingUse, use, cannotBeSubdivided, floorNumber))
                .Concat(ExemptionEvidence(exemption))
                .Concat(InputEvidence(supplied)));

        var info = ruleSet.RuleSet;
        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.AreaExemption,
            zone.AreaUniqueIds, zone.ZoneIdText, status, null, null,
            info.RuleSetId, info.Version, LegalReference, message, evidence);
        return new Article79_1ExemptionFinding(zone, result, exemption, errorCode);
    }

    /// <summary>
    /// A 區劃 whose extent is in doubt is not judged, exactly as the 區劃面積 row does not measure it
    /// (§3.5 first row, §4). No fact is read, so the result carries no judgement — the same line
    /// 第79條之2第3項 draws for a 挑空.
    /// </summary>
    private static Article79_1ExemptionFinding Withhold(
        CandidateSet set, CandidateZone zone, string use, CompiledRuleSet ruleSet, Guid runId, Guid resultId)
    {
        var evidence = new ReviewEvidence(ZoneEvidence(zone)
            .Concat(new[]
            {
                new ReviewEvidenceItem(ZoneUseField, ReviewValue.OfText(use)),
                new ReviewEvidenceItem("zone.problems", ReviewValue.OfText(string.Join(",", zone.Problems)))
            }));

        var info = ruleSet.RuleSet;
        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.AreaExemption,
            zone.AreaUniqueIds, zone.ZoneIdText, ReviewStatus.ManualReview, null, null,
            info.RuleSetId, info.Version, LegalReference,
            $"區劃「{zone.Name}」不檢討第79條之1之免除：區劃範圍有問題（{string.Join("、", zone.Problems)}），需人工覆核。",
            evidence);
        return new Article79_1ExemptionFinding(zone, result, null, ReviewErrorCode.CandidateZoneUnusable);
    }

    /// <summary>What every 第79條之1 result carries, whether or not it reached the judgement.</summary>
    private static IEnumerable<ReviewEvidenceItem> ZoneEvidence(CandidateZone zone)
    {
        yield return new ReviewEvidenceItem("zone.id", ReviewValue.OfText(zone.ZoneIdText));
        yield return new ReviewEvidenceItem("zone.name", ReviewValue.OfText(zone.Name));
    }

    /// <summary>
    /// The facts the judgement read, each recorded only when it was read at all — a field left out
    /// says 「沒有這個事實」 exactly as the rules' own evidence does. 區劃用途 is always present, because
    /// a 區劃 with no 用途 never gets this far.
    /// </summary>
    private static IEnumerable<ReviewEvidenceItem> FactEvidence(
        bool? fireResistive, string? buildingUse, string use, bool? cannotBeSubdivided, int? floorNumber)
    {
        yield return new ReviewEvidenceItem(ZoneUseField, ReviewValue.OfText(use));
        if (buildingUse is not null)
            yield return new ReviewEvidenceItem(BuildingUseField, ReviewValue.OfText(buildingUse));
        if (fireResistive is bool construction)
            yield return new ReviewEvidenceItem(FireResistiveField, ReviewValue.OfBoolean(construction));
        if (cannotBeSubdivided is bool declared)
            yield return new ReviewEvidenceItem(CannotBeSubdividedField, ReviewValue.OfBoolean(declared));
        if (floorNumber is int storey)
            yield return new ReviewEvidenceItem(FloorNumberField, ReviewValue.Quantity(storey, ReviewUnit.None));
    }

    private static IEnumerable<ReviewEvidenceItem> ExemptionEvidence(Article79_1Exemption exemption)
    {
        yield return new ReviewEvidenceItem("article79_1.clause", ReviewValue.OfText(exemption.Clause.ToString()));
        yield return new ReviewEvidenceItem("article79_1.gaps", ReviewValue.OfText(exemption.Gaps.ToString()));
    }

    /// <summary>
    /// Where each fact came from, for the fields this judgement actually reads. The other inputs of
    /// the 區劃 belong to the area row, and naming them here would suggest 第79條之1 weighed them.
    /// </summary>
    private static IEnumerable<ReviewEvidenceItem> InputEvidence(IEnumerable<ReviewInput> inputs) =>
        inputs.Where(x => x.Source is not null && ReadFields.Contains(x.Field, StringComparer.Ordinal))
            .Select(x => new ReviewEvidenceItem($"source[{x.Field}]", ReviewValue.OfText(x.Source!)));

    /// <summary>
    /// One supplied fact as <see cref="Article79_1Exemption.For"/> wants it: a value, or null. Turning
    /// the three states of a <see cref="ReviewInput"/> into two is the caller's job by design —
    /// 「沒有」 and 「同一區劃各 Area 填得不一致」 are the same kind of gap, and neither picks a value
    /// (§3.5 last paragraph).
    /// </summary>
    private static ReviewValue? Supplied(IEnumerable<ReviewInput> inputs, string field) =>
        inputs.FirstOrDefault(x => string.Equals(x.Field, field, StringComparison.Ordinal)) is { IsUnreadable: false } input
            ? input.Value
            : null;

    private static bool? Flag(IEnumerable<ReviewInput> inputs, string field) =>
        Supplied(inputs, field) is { Kind: ReviewValueKind.Boolean } value ? value.Flag : (bool?)null;

    private static string? Text(IEnumerable<ReviewInput> inputs, string field) =>
        Supplied(inputs, field) is { Kind: ReviewValueKind.Text } value && value.Text.Length > 0 ? value.Text : null;

    private static int? FloorNumber(IEnumerable<ReviewInput> inputs) =>
        Supplied(inputs, FloorNumberField) is { Kind: ReviewValueKind.Quantity } value
            ? (int)Math.Round(value.Number, MidpointRounding.AwayFromZero)
            : (int?)null;
}
