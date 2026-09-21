using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Rules;

/// <summary>
/// Turns an authored rule file into a <see cref="RuleSet"/> and back. Loading goes through
/// <see cref="RuleSetSchemaValidator"/> first, so a malformed file becomes one failure listing every
/// problem rather than an exception from whichever constructor tripped first.
/// </summary>
public static class RuleSetDocumentMapper
{
    public static Result<RuleSet> FromDocument(RuleSetDocument? document)
    {
        var report = RuleSetSchemaValidator.Validate(document);
        if (!report.IsValid)
            return Result.Failure<RuleSet>(new Error(
                ReviewErrorCode.RuleSchemaInvalid,
                $"規則檔有 {report.Issues.Count} 處不符合 schema，未載入任何規則。",
                report.ToText()));

        var rules = document!.Rules.Select(ToRule).ToList();
        return Result.Success(new RuleSet(document.RuleSetId, document.Version, document.Title, rules));
    }

    public static RuleSetDocument ToDocument(RuleSet ruleSet)
    {
        if (ruleSet is null) throw new ArgumentNullException(nameof(ruleSet));

        return new RuleSetDocument
        {
            SchemaVersion = RuleSet.CurrentSchemaVersion,
            RuleSetId = ruleSet.RuleSetId,
            Version = ruleSet.Version,
            Title = ruleSet.Title,
            Rules = ruleSet.Rules.Select(ToDocument).ToList()
        };
    }

    private static Rule ToRule(RuleDocument document)
    {
        // The validator has already vouched for every field below.
        RuleSetSchemaValidator.TryParseName<RuleCategory>(document.Category, out var category);
        RuleSetSchemaValidator.TryParseName<RuleSeverity>(document.Severity, out var severity);
        RuleSetSchemaValidator.TryParseDate(document.EffectiveDate, out var effectiveDate);

        return new Rule(
            document.RuleId,
            document.Version,
            category,
            document.LegalReference,
            effectiveDate,
            document.Jurisdiction,
            document.Priority,
            new RuleExpression(document.AppliesWhen),
            new RuleExpression(document.RequiredValue),
            (document.Exemptions ?? new List<string>()).Select(x => new RuleExpression(x)),
            document.EvidenceFields ?? new List<string>(),
            severity);
    }

    private static RuleDocument ToDocument(Rule rule) => new RuleDocument
    {
        RuleId = rule.RuleId,
        Version = rule.Version,
        Category = rule.Category.ToString(),
        LegalReference = rule.LegalReference,
        EffectiveDate = rule.EffectiveDate.ToString(RuleSetSchemaValidator.DateFormat, CultureInfo.InvariantCulture),
        Jurisdiction = rule.Jurisdiction,
        Priority = rule.Priority,
        AppliesWhen = rule.AppliesWhen.Source,
        RequiredValue = rule.RequiredValue.Source,
        Exemptions = rule.Exemptions.Select(x => x.Source).ToList(),
        EvidenceFields = rule.EvidenceFields.ToList(),
        Severity = rule.Severity.ToString()
    };
}
