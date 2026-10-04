using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>The check types results are filed under (spec 11.3 <c>checkType</c>, spec 11.7 檢討表).</summary>
public static class ReviewCheckTypes
{
    public const string CompartmentArea = "CompartmentArea";
    public const string FireResistance = "FireResistance";
    public const string OpeningProtection = "OpeningProtection";

    /// <summary>防火區劃與帷幕牆交接（第79條第3、4項、第79條之3、第79條之4）.</summary>
    public const string CompartmentContinuity = "CompartmentContinuity";

    /// <summary>垂直區劃之遮煙性能與管道間維修門時效（第79條之2）.</summary>
    public const string VerticalCompartment = "VerticalCompartment";

    /// <summary>
    /// 區劃面積上限的免除（第79條之1之無法區劃分隔部分）。不是 <see cref="RuleCategory"/>：本檢討
    /// 不走規則引擎，零規則的類別會讓 <c>Evaluate</c> 回一句誤導的「規則集沒有這個類別的規則」
    /// （docs/regulations/article-79-1-area-exemption.md §5.1、決議 8）。名稱取 AreaExemption 而非
    /// Article79_1，是為了讓第79條之2第3項的挑空免除將來能搬進同一個檢討類型（決議 8a）。
    /// </summary>
    public const string AreaExemption = "AreaExemption";
}

/// <summary>How Revit's Area and the measured boundary compared (spec 11.4 step 2).</summary>
public enum AreaCrossCheck
{
    /// <summary>Within tolerance: the same room measured twice.</summary>
    Agrees,

    /// <summary>Beyond tolerance: the Area is not the extent the boundary encloses, so no Pass or Fail is given.</summary>
    Differs,

    /// <summary>One of the two values is missing, so there is nothing to compare.</summary>
    NotComparable
}

/// <summary>The 區劃面積 verdict for one zone, with the numbers it was decided on.</summary>
public sealed class ZoneAreaFinding
{
    internal ZoneAreaFinding(
        CandidateZone zone,
        ReviewResult result,
        RuleOutcome? outcome,
        AreaCrossCheck crossCheck,
        double? relativeDifference,
        string? errorCode)
    {
        Zone = zone;
        Result = result;
        Outcome = outcome;
        CrossCheck = crossCheck;
        RelativeDifference = relativeDifference;
        ErrorCode = errorCode;
    }

    public CandidateZone Zone { get; }
    public Guid ZoneId => Zone.ZoneId;
    public ReviewResult Result { get; }
    public ReviewStatus Status => Result.Status;

    /// <summary>What the rule engine said, or null when the zone never reached it (a zone-level problem).</summary>
    public RuleOutcome? Outcome { get; }

    public double? RevitAreaSquareMeters => Zone.RevitAreaSquareMeters;
    public double? GeometricAreaSquareMeters => Zone.GeometricAreaSquareMeters;
    public AreaCrossCheck CrossCheck { get; }

    /// <summary>|Revit − measured| ÷ measured, when both exist.</summary>
    public double? RelativeDifference { get; }

    /// <summary>The spec 14 code a log entry about this zone carries; null for a routine verdict.</summary>
    public string? ErrorCode { get; }

    public override string ToString() => $"{Zone.Name}: {ReviewStatusText.Label(Status)} — {Result.Message}";
}

/// <summary>The 區劃面積 results of one run: one per zone, in Zone ID order.</summary>
public sealed class CompartmentAreaReview
{
    internal CompartmentAreaReview(IEnumerable<ZoneAreaFinding> findings, IEnumerable<string> warnings)
    {
        Findings = new ReadOnlyCollection<ZoneAreaFinding>(findings.ToList());
        Warnings = new ReadOnlyCollection<string>(warnings.ToList());
    }

    public IReadOnlyList<ZoneAreaFinding> Findings { get; }
    public IEnumerable<ReviewResult> Results => Findings.Select(x => x.Result);
    public IReadOnlyList<string> Warnings { get; }

    public ZoneAreaFinding? For(Guid zoneId) => Findings.FirstOrDefault(x => x.ZoneId == zoneId);

    public int Count(ReviewStatus status) => Findings.Count(x => x.Status == status);
}

/// <summary>
/// 防火區劃面積 (spec 11.4). For every zone of the candidate set:
/// <list type="number">
/// <item>A zone whose extent cannot be relied on (未封閉、與其他區劃重疊) is ManualReview; no rule runs on it.</item>
/// <item>Otherwise the facts are the zone's identity, Revit's Area as <c>zone.area</c> (the primary actual
/// value) and the supplied building and zone inputs; the rule engine decides applicability, the limit,
/// exemptions and missing data.</item>
/// <item>A 挑空 that 第79條之2第3項 exempts from 單獨區劃分隔 is no longer a 垂直區劃, so the use exemption
/// no longer covers it: its 連通區劃 goes back to 第79條 and, when it reaches the eleventh storey, 第83條
/// (docs/regulations/vertical-compartment.md 決議 32). The facts the two atrium rules read are
/// derived here rather than supplied.</item>
/// <item>The measured boundary cross-checks Revit's Area. When they differ beyond tolerance a Pass or Fail is
/// withheld and becomes ManualReview: the comparison would be made on an area the geometry does not confirm.</item>
/// <item>Every result keeps the calculation — each Area's value, the sum, the measured value and their
/// difference — the clause, and where each input came from.</item>
/// </list>
/// </summary>
public static class CompartmentAreaCheck
{
    public static Result<CompartmentAreaReview> Review(
        CandidateSet set,
        CompartmentAreaInputs inputs,
        RuleEngine engine,
        RuleEvaluationContext context,
        Guid runId,
        CompartmentAreaOptions? options = null,
        Func<Guid>? newResultId = null)
    {
        if (set is null) throw new ArgumentNullException(nameof(set));
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));
        if (engine is null) throw new ArgumentNullException(nameof(engine));
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
        options ??= CompartmentAreaOptions.Default;
        newResultId ??= Guid.NewGuid;

        var ready = ReviewPreconditions.Zones(set, "面積");
        if (ready.IsFailure) return Result.Failure<CompartmentAreaReview>(ready.Error);

        var warnings = inputs.ZoneIds.Where(id => set.Zone(id) is null)
            .Select(id => $"輸入資料指定的區劃 {id:D} 不在此工作包中，已略過。")
            .ToList();

        var findings = set.Zones
            .Select(zone => zone.IsClear
                ? Decide(set, zone, inputs, engine, context, runId, options, newResultId())
                : Withhold(set, zone, engine.RuleSet, runId, newResultId()))
            .ToList();

        return Result.Success(new CompartmentAreaReview(findings, warnings));
    }

    private static ZoneAreaFinding Decide(
        CandidateSet set,
        CandidateZone zone,
        CompartmentAreaInputs inputs,
        RuleEngine engine,
        RuleEvaluationContext context,
        Guid runId,
        CompartmentAreaOptions options,
        Guid resultId)
    {
        var facts = CandidateFacts.ForZone(set, zone, engine.RuleSet.Catalog);
        var revit = zone.RevitAreaSquareMeters;
        if (revit is double area)
        {
            if (area > 0) facts.Set("zone.area", area, ReviewUnit.SquareMeter);
            else facts.MarkUnreadable("zone.area", "Revit 回報面積為 0，面積未落在封閉邊界內");
        }

        var supplied = inputs.Building.Concat(inputs.ForZone(zone.ZoneId)).ToList();
        foreach (var input in supplied) input.ApplyTo(facts);
        DeriveAtriumFacts(facts, supplied);

        var outcome = engine.Evaluate(RuleCategory.CompartmentArea, facts, context);
        var (crossCheck, difference) = CrossCheck(zone, options);

        var status = outcome.Status;
        var message = $"區劃「{zone.Name}」：{outcome.Message}";
        if (outcome.Reason == RuleOutcomeReason.Exempt && ZoneUses.IsVerticalCompartment(TextOf(facts, "zone.use")))
            message += ZoneUses.VerticalCompartmentHandoff;
        var errorCode = RuleOutcomeErrorCode.For(outcome);
        if (crossCheck == AreaCrossCheck.Differs)
        {
            var note = string.Format(CultureInfo.InvariantCulture,
                "Revit 面積 {0:0.##} m² 與邊界量算 {1:0.##} m² 相差 {2:0.##}%（容許 {3:0.##}%），面積來源無法確認",
                revit, zone.GeometricAreaSquareMeters, difference * 100.0, options.CrossCheckRelativeTolerance * 100.0);
            // A 挑空 that 第3項 merged into its 連通區劃 is compared on the 合計 the designer declared,
            // not on this Area — so whether this Area agrees with its boundary has no bearing on
            // the verdict, and withholding it would hide a 未符合 behind a 人工覆核 (決議 32).
            if (ReviewStatusText.IsComparison(status) && !IsMergedAtrium(facts))
            {
                message = $"區劃「{zone.Name}」：{note}，不判定{ReviewStatusText.Label(status)}，需人工覆核；請檢查區劃邊界後重新套用。" +
                          $"（依 Revit 面積的比較：{outcome.Message}）";
                status = ReviewStatus.ManualReview;
                errorCode = ReviewErrorCode.AreaDisagrees;
            }
            else
            {
                message += $"另外，{note}。";
                errorCode ??= ReviewErrorCode.AreaDisagrees;
            }
        }

        var evidence = new ReviewEvidence(outcome.Evidence.Items
            .Concat(AreaEvidence(zone, crossCheck, difference, options))
            .Concat(InputEvidence(supplied))
            .Concat(GapEvidence(outcome)));

        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.CompartmentArea,
            zone.AreaUniqueIds, zone.ZoneIdText, status, outcome.ActualValue, outcome.RequiredValue,
            outcome.RuleId, outcome.RuleVersion, outcome.LegalReference, message, evidence);
        return new ZoneAreaFinding(zone, result, outcome, crossCheck, difference, errorCode);
    }

    /// <summary>
    /// A zone whose extent is in doubt gets no verdict at all. Like an ambiguous element, the result is
    /// attributed to the rule set itself, since no rule decided it.
    /// </summary>
    private static ZoneAreaFinding Withhold(CandidateSet set, CandidateZone zone, CompiledRuleSet ruleSet, Guid runId, Guid resultId)
    {
        var ambiguities = set.Ambiguities
            .Where(a => a.ZoneId == zone.ZoneId && zone.Problems.Contains(a.Kind))
            .ToList();
        var reasons = ambiguities.Count > 0
            ? string.Join("", ambiguities.Select(a => a.Message))
            : $"區劃「{zone.Name}」的範圍有問題（{string.Join("、", zone.Problems)}），需人工覆核。";

        var evidence = new ReviewEvidence(new[]
            {
                new ReviewEvidenceItem("zone.problems", ReviewValue.OfText(string.Join(",", zone.Problems)))
            }
            .Concat(AreaEvidence(zone, AreaCrossCheck.NotComparable, null, null)));

        var info = ruleSet.RuleSet;
        var result = new ReviewResult(resultId, runId, set.PackageId, ReviewCheckTypes.CompartmentArea,
            zone.AreaUniqueIds.Concat(ambiguities.SelectMany(a => a.SubjectUniqueIds)), zone.ZoneIdText,
            ReviewStatus.ManualReview, null, null, info.RuleSetId, info.Version, $"規則集「{info.Title}」",
            $"區劃「{zone.Name}」不檢討面積：{reasons}", evidence);
        return new ZoneAreaFinding(zone, result, null, AreaCrossCheck.NotComparable, null, ReviewErrorCode.CandidateZoneUnusable);
    }

    private static (AreaCrossCheck Kind, double? Difference) CrossCheck(CandidateZone zone, CompartmentAreaOptions options)
    {
        if (zone.RevitAreaSquareMeters is not double revit || zone.GeometricAreaSquareMeters is not double measured || measured <= 0)
            return (AreaCrossCheck.NotComparable, null);

        var difference = Math.Abs(revit - measured) / measured;
        return (difference <= options.CrossCheckRelativeTolerance + 1e-9 ? AreaCrossCheck.Agrees : AreaCrossCheck.Differs,
            difference);
    }

    /// <summary>The calculation: each Area's two values, their sums, and how they compared.</summary>
    private static IEnumerable<ReviewEvidenceItem> AreaEvidence(
        CandidateZone zone, AreaCrossCheck crossCheck, double? difference, CompartmentAreaOptions? options)
    {
        yield return new ReviewEvidenceItem("zone.name", ReviewValue.OfText(zone.Name));
        yield return new ReviewEvidenceItem("area.partCount", ReviewValue.Quantity(zone.Parts.Count, ReviewUnit.Count));
        foreach (var part in zone.Parts)
        {
            if (part.RevitAreaSquareMeters is double revit)
                yield return new ReviewEvidenceItem($"area.part[{part.AreaUniqueId}].revit", ReviewValue.Quantity(revit, ReviewUnit.SquareMeter));
            if (part.GeometricAreaSquareMeters is double measured)
                yield return new ReviewEvidenceItem($"area.part[{part.AreaUniqueId}].geometric", ReviewValue.Quantity(measured, ReviewUnit.SquareMeter));
        }

        if (zone.RevitAreaSquareMeters is double total)
            yield return new ReviewEvidenceItem("area.revit", ReviewValue.Quantity(total, ReviewUnit.SquareMeter));
        if (zone.GeometricAreaSquareMeters is double geometric)
            yield return new ReviewEvidenceItem("area.geometric", ReviewValue.Quantity(geometric, ReviewUnit.SquareMeter));
        yield return new ReviewEvidenceItem("area.crossCheck", ReviewValue.OfText(crossCheck.ToString()));
        if (difference is double ratio)
            yield return new ReviewEvidenceItem("area.relativeDifference", ReviewValue.Quantity(ratio, ReviewUnit.None));
        if (options is not null)
            yield return new ReviewEvidenceItem("area.crossCheckTolerance", ReviewValue.Quantity(options.CrossCheckRelativeTolerance, ReviewUnit.None));
    }

    /// <summary>
    /// The three facts the atrium area rules read (決議 32). Every 區劃 that is not plainly a 挑空 gets
    /// <c>zone.atriumMerged = false</c> — set, not left out, because a missing field in an
    /// applicability condition is 資料不足 for every 區劃 in the project. That includes a 區劃 whose
    /// 用途 is blank or unreadable: nobody has said it is a 挑空, and the use exemption of the two
    /// ordinary rules already reports that gap where it matters.
    /// </summary>
    /// <remarks>
    /// For a 挑空 the answer is <see cref="AtriumExemptionFacts"/>'s, the same reading the 第3項
    /// result gives, so the two can never disagree. A fact that cannot be worked out is left unset
    /// rather than guessed: an undecided exemption, a 連通區劃面積 nobody stated, or a 最高樓層序 with
    /// no 起始樓層序 and 連跨樓層數 behind it all become the engine's ordinary 資料不足. An input that
    /// was read but could not be understood — the Areas of one 區劃 filled in differently — passes
    /// its reason on, so the 區劃面積 result says why rather than only 「未設定」.
    /// </remarks>
    private static void DeriveAtriumFacts(RuleFacts facts, IReadOnlyList<ReviewInput> supplied)
    {
        if (!string.Equals(TextOf(facts, "zone.use"), ZoneUses.Atrium, StringComparison.Ordinal))
        {
            facts.Set(AtriumMergedField, false);
            return;
        }

        var atrium = AtriumExemptionFacts.Read(supplied);
        if (!atrium.Exemption.IsUndecided) facts.Set(AtriumMergedField, atrium.Exemption.Holds);

        if (atrium.ConnectedAreaSquareMeters is double connected)
            facts.Set(AtriumCompartmentAreaField, connected, ReviewUnit.SquareMeter);
        else if (UnreadableReason(supplied, AtriumExemptionFacts.ConnectedAreaField) is string areaReason)
            facts.MarkUnreadable(AtriumCompartmentAreaField, areaReason);

        DeriveTopFloor(facts, supplied, atrium);
    }

    /// <summary>
    /// 所跨最高樓層序 = 挑空起始樓層序 + 連跨樓層數 − 1 (決議 34). It is counted from the 挑空's own
    /// lowest storey, never from the storey this Area sits on: a 挑空 drawn as one Area per storey
    /// would otherwise reach 11F from its 9F Area when it really stops at 10F, and be held to
    /// 第83條 for nothing. An Area that sits outside the range it declares is a contradiction in
    /// the facts, and is reported as one rather than resolved either way.
    /// </summary>
    private static void DeriveTopFloor(RuleFacts facts, IReadOnlyList<ReviewInput> supplied, AtriumExemptionFacts atrium)
    {
        if (atrium.SpannedFloors is not int spanned || atrium.BaseFloor is not int baseFloor)
        {
            var reason = UnreadableReason(supplied, AtriumExemptionFacts.BaseFloorField) ??
                         UnreadableReason(supplied, AtriumExemptionFacts.SpannedFloorsField);
            if (reason is not null) facts.MarkUnreadable(AtriumTopFloorField, reason);
            return;
        }

        var top = TopFloor(baseFloor, spanned);
        if (facts.Find("zone.floorNumber") is { Kind: ReviewValueKind.Quantity } own && (own.Number < baseFloor || own.Number > top))
        {
            facts.MarkUnreadable(AtriumTopFloorField, string.Format(CultureInfo.InvariantCulture,
                "本 Area 所在樓層序 {0} 不在挑空起始樓層序 {1} 起連跨 {2} 層的範圍（{1}～{3}）內，" +
                "請核對各樓層區劃的防火檢討_所在樓層序，或是否有樓層尚未建立區劃",
                own.Number, baseFloor, spanned, top));
            return;
        }

        facts.Set(AtriumTopFloorField, top, ReviewUnit.None);
    }

    private static bool IsMergedAtrium(RuleFacts facts) =>
        facts.Find(AtriumMergedField) is { Kind: ReviewValueKind.Boolean, Flag: true };

    private static string? UnreadableReason(IEnumerable<ReviewInput> supplied, string field) =>
        supplied.FirstOrDefault(x => string.Equals(x.Field, field, StringComparison.Ordinal)) is { IsUnreadable: true } input
            ? input.UnreadableReason
            : null;

    /// <summary>
    /// The highest storey a 挑空 spans, counted from its lowest storey. 樓層序 skips 0 — the storey
    /// above B1 is 1F — so a 挑空 that starts underground and rises into the 地上 storeys gains one
    /// when it crosses.
    /// </summary>
    public static double TopFloor(double floorNumber, int spannedFloors)
    {
        var top = floorNumber + spannedFloors - 1;
        return floorNumber < 0 && top >= 0 ? top + 1 : top;
    }

    private const string AtriumMergedField = "zone.atriumMerged";
    private const string AtriumTopFloorField = "zone.atriumTopFloor";
    private const string AtriumCompartmentAreaField = "zone.atriumCompartmentArea";

    private static string? TextOf(RuleFacts facts, string field) =>
        facts.Find(field) is { Kind: ReviewValueKind.Text } value ? value.Text : null;

    private static IEnumerable<ReviewEvidenceItem> InputEvidence(IEnumerable<ReviewInput> inputs) =>
        inputs.Where(x => x.Source is not null)
            .Select(x => new ReviewEvidenceItem($"source[{x.Field}]", ReviewValue.OfText(x.Source!)));

    private static IEnumerable<ReviewEvidenceItem> GapEvidence(RuleOutcome outcome) =>
        outcome.Gaps.Count == 0
            ? Enumerable.Empty<ReviewEvidenceItem>()
            : new[] { new ReviewEvidenceItem("rule.gaps", ReviewValue.OfText(string.Join("、", outcome.Gaps))) };
}
