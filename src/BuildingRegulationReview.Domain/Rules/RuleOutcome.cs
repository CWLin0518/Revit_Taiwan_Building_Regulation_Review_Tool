using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules.Expressions;

namespace BuildingRegulationReview.Domain.Rules;

/// <summary>Why the engine reached its status; the review layer maps the non-routine ones to error codes.</summary>
public enum RuleOutcomeReason
{
    /// <summary>A rule applied and its requirement was compared: Pass or Fail.</summary>
    Compared,

    /// <summary>No rule's applicability condition held.</summary>
    NoRuleApplies,

    /// <summary>The deciding rule applied but one of its exemptions held.</summary>
    Exempt,

    /// <summary>A field needed to decide was missing or unreadable.</summary>
    MissingData,

    /// <summary>The rule set has no rule for this category, jurisdiction and date (spec 14 規則缺失).</summary>
    NoRule,

    /// <summary>Rules of the same, highest priority applied and disagree (spec 14 條件衝突).</summary>
    Conflict,

    /// <summary>An expression could not be computed, e.g. a division by zero (spec 14 運算錯誤).</summary>
    ComputationFailed
}

/// <summary>
/// The engine's verdict on one subject for one category: a status from the six-state model plus
/// everything a <see cref="ReviewResult"/> needs except the run, package and subject identity, which
/// only the check that asked knows.
/// </summary>
public sealed class RuleOutcome
{
    internal RuleOutcome(
        ReviewStatus status,
        RuleOutcomeReason reason,
        string ruleId,
        string ruleVersion,
        string legalReference,
        RuleSeverity severity,
        string message,
        ReviewValue? actualValue = null,
        ReviewValue? requiredValue = null,
        RuleComparator? comparator = null,
        ReviewEvidence? evidence = null,
        IEnumerable<RuleFactGap>? gaps = null,
        IEnumerable<string>? consideredRuleIds = null)
    {
        Status = status;
        Reason = reason;
        RuleId = ruleId;
        RuleVersion = ruleVersion;
        LegalReference = legalReference;
        Severity = severity;
        Message = message;
        ActualValue = actualValue;
        RequiredValue = requiredValue;
        Comparator = comparator;
        Evidence = evidence ?? ReviewEvidence.Empty;
        Gaps = new ReadOnlyCollection<RuleFactGap>((gaps ?? Enumerable.Empty<RuleFactGap>()).Distinct().ToList());
        ConsideredRuleIds = new ReadOnlyCollection<string>((consideredRuleIds ?? new[] { ruleId }).Distinct(StringComparer.Ordinal).ToList());
    }

    public ReviewStatus Status { get; }
    public RuleOutcomeReason Reason { get; }

    /// <summary>The rule that decided; for <see cref="RuleOutcomeReason.NoRule"/>, the rule set itself.</summary>
    public string RuleId { get; }

    public string RuleVersion { get; }
    public string LegalReference { get; }
    public RuleSeverity Severity { get; }
    public string Message { get; }
    public ReviewValue? ActualValue { get; }

    /// <summary>What the rule requires, whenever it could be computed — even if the actual value is missing.</summary>
    public ReviewValue? RequiredValue { get; }

    public RuleComparator? Comparator { get; }
    public ReviewEvidence Evidence { get; }

    /// <summary>The missing or unreadable fields behind an InsufficientData outcome.</summary>
    public IReadOnlyList<RuleFactGap> Gaps { get; }

    /// <summary>The rules this outcome speaks for: the decider, or every rule in a conflict.</summary>
    public IReadOnlyList<string> ConsideredRuleIds { get; }

    /// <summary>
    /// True when the engine could not tell whether the rule applied at all, so the rule named here
    /// decided nothing — it is only the first of the rules still in doubt. Every outcome of a rule
    /// that did apply carries that rule's comparator; this one alone has none.
    /// </summary>
    public bool IsApplicabilityUndecided => Reason == RuleOutcomeReason.MissingData && Comparator is null;

    public ReviewResult ToReviewResult(
        Guid resultId,
        Guid runId,
        Guid packageId,
        string checkType,
        IEnumerable<string>? subjectUniqueIds,
        string? zoneId) =>
        new(resultId, runId, packageId, checkType, subjectUniqueIds, zoneId, Status,
            ActualValue, RequiredValue, RuleId, RuleVersion, LegalReference, Message, Evidence);

    public override string ToString() => $"{Status} ({RuleId}): {Message}";
}
