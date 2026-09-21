using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.Reviews;

/// <summary>
/// The outcome of one check against one subject (spec 11.3). Every result names the rule, its
/// version and the clause it came from, so a verdict can always be traced back (spec 18 item 4).
/// </summary>
public sealed class ReviewResult
{
    public ReviewResult(
        Guid resultId,
        Guid runId,
        Guid packageId,
        string checkType,
        IEnumerable<string>? subjectUniqueIds,
        string? zoneId,
        ReviewStatus status,
        ReviewValue? actualValue,
        ReviewValue? requiredValue,
        string ruleId,
        string ruleVersion,
        string legalReference,
        string? message,
        ReviewEvidence? evidence = null,
        string? reviewedBy = null,
        DateTime? reviewedAtUtc = null)
    {
        if (resultId == Guid.Empty) throw new ArgumentException("Result ID cannot be empty.", nameof(resultId));
        if (runId == Guid.Empty) throw new ArgumentException("Run ID cannot be empty.", nameof(runId));
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(checkType)) throw new ArgumentException("Check type is required.", nameof(checkType));
        if (!Enum.IsDefined(typeof(ReviewStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (string.IsNullOrWhiteSpace(ruleId)) throw new ArgumentException("Rule ID is required.", nameof(ruleId));
        if (string.IsNullOrWhiteSpace(ruleVersion)) throw new ArgumentException("Rule version is required.", nameof(ruleVersion));
        if (string.IsNullOrWhiteSpace(legalReference)) throw new ArgumentException("Legal reference is required.", nameof(legalReference));

        var subjects = new ReadOnlyCollection<string>((subjectUniqueIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal).ToList());
        var zone = string.IsNullOrWhiteSpace(zoneId) ? null : zoneId!.Trim();
        var text = message?.Trim() ?? string.Empty;
        var reviewer = string.IsNullOrWhiteSpace(reviewedBy) ? null : reviewedBy!.Trim();

        // A verdict that was not measured has no business looking like one: pass and fail are the
        // only states that compare, and they cannot compare against nothing (spec 18 item 5).
        if (ReviewStatusText.IsComparison(status) && (actualValue is null || requiredValue is null))
            throw new ArgumentException(
                $"A {status} result must carry both the actual and the required value; without them the state is InsufficientData.",
                nameof(status));

        // Spec 12.1: anything short of a pass explains itself instead of printing a bare status.
        if ((status == ReviewStatus.Fail || status == ReviewStatus.InsufficientData || status == ReviewStatus.ManualReview)
            && text.Length == 0)
            throw new ArgumentException($"A {status} result must say why.", nameof(message));

        if (status != ReviewStatus.NotRun && subjects.Count == 0 && zone is null)
            throw new ArgumentException("A result must name the zone or the elements it is about.", nameof(subjectUniqueIds));

        if ((reviewer is null) != (reviewedAtUtc is null))
            throw new ArgumentException("A manual review records both who reviewed and when.", nameof(reviewedBy));

        ResultId = resultId;
        RunId = runId;
        PackageId = packageId;
        CheckType = checkType.Trim();
        SubjectUniqueIds = subjects;
        ZoneId = zone;
        Status = status;
        ActualValue = actualValue;
        RequiredValue = requiredValue;
        RuleId = ruleId.Trim();
        RuleVersion = ruleVersion.Trim();
        LegalReference = legalReference.Trim();
        Message = text;
        Evidence = evidence ?? ReviewEvidence.Empty;
        ReviewedBy = reviewer;
        ReviewedAtUtc = reviewedAtUtc?.ToUniversalTime();
    }

    public Guid ResultId { get; }
    public Guid RunId { get; }
    public Guid PackageId { get; }
    public string CheckType { get; }
    public IReadOnlyList<string> SubjectUniqueIds { get; }
    public string? ZoneId { get; }
    public ReviewStatus Status { get; }
    public ReviewValue? ActualValue { get; }
    public ReviewValue? RequiredValue { get; }
    public string RuleId { get; }
    public string RuleVersion { get; }
    public string LegalReference { get; }
    public string Message { get; }
    public ReviewEvidence Evidence { get; }
    public string? ReviewedBy { get; }
    public DateTime? ReviewedAtUtc { get; }
}
