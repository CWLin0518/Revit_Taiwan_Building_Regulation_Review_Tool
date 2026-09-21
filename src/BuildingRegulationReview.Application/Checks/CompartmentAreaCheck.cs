using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>The check types results are filed under (spec 11.3 <c>checkType</c>, spec 11.7 檢討表).</summary>
public static class ReviewCheckTypes
{
    public const string CompartmentArea = "CompartmentArea";
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

        // Spec 11.1: 任一關鍵條件不成立時停止檢討並列出修正方式.
        if (set.Zones.Count == 0)
            return Result.Failure<CompartmentAreaReview>(new Error(ReviewErrorCode.CandidateZoneUnusable,
                "此工作包沒有任何區劃，無法檢討面積；請先在「建立區劃範圍」套用區劃。"));

        var unmeasurable = set.Zones.Where(z => !z.IsMeasurable).ToList();
        if (unmeasurable.Count > 0)
            return Result.Failure<CompartmentAreaReview>(new Error(ReviewErrorCode.CandidateZoneUnusable,
                $"區劃 {string.Join("、", unmeasurable.Select(z => $"「{z.Name}」"))} 沒有任何封閉的面積，無法檢討；" +
                "請回到「建立區劃範圍」修正邊界後重新套用。",
                string.Join(", ", unmeasurable.Select(z => z.ZoneIdText))));

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

        var outcome = engine.Evaluate(RuleCategory.CompartmentArea, facts, context);
        var (crossCheck, difference) = CrossCheck(zone, options);

        var status = outcome.Status;
        var message = $"區劃「{zone.Name}」：{outcome.Message}";
        var errorCode = RuleOutcomeErrorCode.For(outcome);
        if (crossCheck == AreaCrossCheck.Differs)
        {
            var note = string.Format(CultureInfo.InvariantCulture,
                "Revit 面積 {0:0.##} m² 與邊界量算 {1:0.##} m² 相差 {2:0.##}%（容許 {3:0.##}%），面積來源無法確認",
                revit, zone.GeometricAreaSquareMeters, difference * 100.0, options.CrossCheckRelativeTolerance * 100.0);
            if (ReviewStatusText.IsComparison(status))
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

    private static IEnumerable<ReviewEvidenceItem> InputEvidence(IEnumerable<ReviewInput> inputs) =>
        inputs.Where(x => x.Source is not null)
            .Select(x => new ReviewEvidenceItem($"source[{x.Field}]", ReviewValue.OfText(x.Source!)));

    private static IEnumerable<ReviewEvidenceItem> GapEvidence(RuleOutcome outcome) =>
        outcome.Gaps.Count == 0
            ? Enumerable.Empty<ReviewEvidenceItem>()
            : new[] { new ReviewEvidenceItem("rule.gaps", ReviewValue.OfText(string.Join("、", outcome.Gaps))) };
}
