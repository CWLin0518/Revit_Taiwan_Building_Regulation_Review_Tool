using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.Rules;

/// <summary>The kinds of check spec 11 performs; a rule belongs to exactly one.</summary>
public enum RuleCategory
{
    CompartmentArea,
    FireResistance,
    OpeningProtection,

    /// <summary>
    /// Whether a compartment stays continuous where its boundary meets a curtain wall
    /// (第79條第3、4項、第79-3條、第79-4條). Its subject is a junction, not a single element,
    /// so it reads <c>junction.*</c> rather than <c>element.*</c> or <c>opening.*</c>.
    /// </summary>
    CompartmentContinuity
}

/// <summary>How loudly a failed rule speaks (spec 11.2). It never changes the six-state outcome.</summary>
public enum RuleSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>
/// The source text of one expression in the restricted rule DSL (spec 11.2).
/// </summary>
/// <remarks>
/// Held as text on purpose: parsing, the field whitelist and unit handling are the rule engine's
/// job (P3-T02). What this type guarantees is only that an expression is never blank, so a rule
/// that forgot its condition cannot quietly read as "always applies".
/// </remarks>
public sealed class RuleExpression : IEquatable<RuleExpression>
{
    public RuleExpression(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("A rule expression cannot be blank.", nameof(source));

        Source = source.Trim();
    }

    public string Source { get; }

    public bool Equals(RuleExpression? other) =>
        other is not null && string.Equals(Source, other.Source, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as RuleExpression);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Source);
    public override string ToString() => Source;
}

/// <summary>
/// One versioned, structured regulation rule (spec 11.2). A rule is data: it says when it applies,
/// what value is required, what exempts a subject and which evidence a result must carry — it
/// never contains code.
/// </summary>
public sealed class Rule
{
    public Rule(
        string ruleId,
        string version,
        RuleCategory category,
        string legalReference,
        DateTime effectiveDate,
        string jurisdiction,
        int priority,
        RuleExpression appliesWhen,
        RuleExpression requiredValue,
        IEnumerable<RuleExpression>? exemptions = null,
        IEnumerable<string>? evidenceFields = null,
        RuleSeverity severity = RuleSeverity.Error)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) throw new ArgumentException("Rule ID is required.", nameof(ruleId));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Rule version is required.", nameof(version));
        if (!Enum.IsDefined(typeof(RuleCategory), category)) throw new ArgumentOutOfRangeException(nameof(category));
        if (string.IsNullOrWhiteSpace(legalReference)) throw new ArgumentException("Legal reference is required.", nameof(legalReference));
        if (string.IsNullOrWhiteSpace(jurisdiction)) throw new ArgumentException("Jurisdiction is required.", nameof(jurisdiction));
        if (priority < 0) throw new ArgumentOutOfRangeException(nameof(priority), "Priority cannot be negative.");
        if (!Enum.IsDefined(typeof(RuleSeverity), severity)) throw new ArgumentOutOfRangeException(nameof(severity));

        var exemptionList = (exemptions ?? Array.Empty<RuleExpression>()).ToList();
        if (exemptionList.Any(x => x is null))
            throw new ArgumentException("Exemptions cannot contain a missing expression.", nameof(exemptions));

        var fieldList = (evidenceFields ?? Array.Empty<string>()).ToList();
        if (fieldList.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Evidence fields cannot be blank.", nameof(evidenceFields));
        var trimmedFields = fieldList.Select(x => x.Trim()).ToList();
        if (trimmedFields.Distinct(StringComparer.Ordinal).Count() != trimmedFields.Count)
            throw new ArgumentException("Evidence fields must be unique.", nameof(evidenceFields));

        RuleId = ruleId.Trim();
        Version = version.Trim();
        Category = category;
        LegalReference = legalReference.Trim();
        EffectiveDate = effectiveDate.Date;
        Jurisdiction = jurisdiction.Trim();
        Priority = priority;
        AppliesWhen = appliesWhen ?? throw new ArgumentNullException(nameof(appliesWhen));
        RequiredValue = requiredValue ?? throw new ArgumentNullException(nameof(requiredValue));
        Exemptions = new ReadOnlyCollection<RuleExpression>(exemptionList);
        EvidenceFields = new ReadOnlyCollection<string>(trimmedFields);
        Severity = severity;
    }

    public string RuleId { get; }
    public string Version { get; }
    public RuleCategory Category { get; }
    public string LegalReference { get; }

    /// <summary>The calendar date the rule takes effect; the time of day is dropped.</summary>
    public DateTime EffectiveDate { get; }

    public string Jurisdiction { get; }

    /// <summary>Higher wins when two applicable rules disagree (the engine resolves that in P3-T02).</summary>
    public int Priority { get; }

    public RuleExpression AppliesWhen { get; }
    public RuleExpression RequiredValue { get; }
    public IReadOnlyList<RuleExpression> Exemptions { get; }
    public IReadOnlyList<string> EvidenceFields { get; }
    public RuleSeverity Severity { get; }
}
