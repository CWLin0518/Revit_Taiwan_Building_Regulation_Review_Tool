using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Rules;
using BuildingRegulationReview.Domain.Rules.Expressions;

namespace BuildingRegulationReview.Application.Rules;

/// <summary>
/// Compiles every expression of a <see cref="RuleSet"/> against the field whitelist and looks for
/// rules that are bound to contradict each other. Like the schema validator it reports every problem
/// at once, with the same <c>rules[i].field</c> paths, and loads nothing when anything is wrong.
/// </summary>
public static class RuleSetCompiler
{
    /// <summary>Issue code for two rules that are certain to conflict (same category, priority and condition).</summary>
    public const string ConflictIssueCode = "conflict";

    public static Result<CompiledRuleSet> Compile(RuleSet ruleSet, RuleFieldCatalog? catalog = null)
    {
        var report = Check(ruleSet, catalog, out var compiled);
        if (report.IsValid) return Result.Success(compiled!);

        var conflictsOnly = report.Issues.All(x => x.Code == ConflictIssueCode);
        return Result.Failure<CompiledRuleSet>(new Error(
            conflictsOnly ? ReviewErrorCode.RuleConflict : ReviewErrorCode.RuleSchemaInvalid,
            conflictsOnly
                ? $"規則集有 {report.Issues.Count} 組規則必然衝突，未載入任何規則。"
                : $"規則集有 {report.Issues.Count} 處運算式或欄位錯誤，未載入任何規則。",
            report.ToText()));
    }

    /// <summary>Validates the file's structure, then compiles it: the whole path from an authored document to the engine.</summary>
    public static Result<CompiledRuleSet> Load(RuleSetDocument? document, RuleFieldCatalog? catalog = null)
    {
        var ruleSet = RuleSetDocumentMapper.FromDocument(document);
        return ruleSet.IsSuccess ? Compile(ruleSet.Value, catalog) : Result.Failure<CompiledRuleSet>(ruleSet.Error);
    }

    public static RuleSetValidationReport Check(RuleSet ruleSet, RuleFieldCatalog? catalog, out CompiledRuleSet? compiled)
    {
        if (ruleSet is null) throw new ArgumentNullException(nameof(ruleSet));
        catalog ??= RuleFieldCatalog.Default;

        var issues = new List<RuleSchemaIssue>();
        var rules = new List<CompiledRule>();
        for (var i = 0; i < ruleSet.Rules.Count; i++)
        {
            var rule = CompileRule(issues, $"rules[{i}]", ruleSet.Rules[i], catalog);
            if (rule is not null) rules.Add(rule);
        }

        if (rules.Count == ruleSet.Rules.Count)
            FindCertainConflicts(issues, ruleSet, rules);

        compiled = issues.Count == 0 ? new CompiledRuleSet(ruleSet, catalog, rules) : null;
        return new RuleSetValidationReport(issues);
    }

    private static CompiledRule? CompileRule(List<RuleSchemaIssue> issues, string path, Rule rule, RuleFieldCatalog catalog)
    {
        var before = issues.Count;
        var appliesWhen = Try(issues, $"{path}.appliesWhen", () => RuleExpressionParser.ParseCondition(rule.AppliesWhen.Source, catalog, rule.Category));
        var requirement = Try(issues, $"{path}.requiredValue", () => RuleExpressionParser.ParseRequirement(rule.RequiredValue.Source, catalog, rule.Category));
        var exemptions = rule.Exemptions
            .Select((x, i) => Try(issues, $"{path}.exemptions[{i}]", () => RuleExpressionParser.ParseCondition(x.Source, catalog, rule.Category)))
            .ToList();

        var evidence = new List<RuleFieldDefinition>();
        for (var i = 0; i < rule.EvidenceFields.Count; i++)
        {
            var name = rule.EvidenceFields[i];
            var field = catalog.Find(name);
            if (field is null)
                issues.Add(new RuleSchemaIssue($"{path}.evidenceFields[{i}]", RuleExpressionErrorCode.UnknownField,
                    $"證據欄位「{name}」不是白名單欄位。"));
            else if (!field.IsAvailableIn(rule.Category))
                issues.Add(new RuleSchemaIssue($"{path}.evidenceFields[{i}]", RuleExpressionErrorCode.FieldNotAvailable,
                    $"證據欄位 {name} 不適用於 {rule.Category} 規則。"));
            else
                evidence.Add(field);
        }

        return issues.Count == before
            ? new CompiledRule(rule, appliesWhen!, requirement!, exemptions!, evidence)
            : null;
    }

    private static T? Try<T>(List<RuleSchemaIssue> issues, string path, Func<T> compile) where T : class
    {
        try
        {
            return compile();
        }
        catch (RuleExpressionException ex)
        {
            issues.Add(new RuleSchemaIssue(path, ex.Code, $"{ex.Message}（第 {ex.Position + 1} 個字元）"));
            return null;
        }
    }

    // Two rules of one category at one priority whose conditions are the same tree will both apply to
    // every subject either applies to; if their requirements differ, every such subject would end in a
    // conflict. That is an authoring mistake worth refusing at load time. Overlaps that depend on the
    // data are left to the engine, which reports them per subject.
    private static void FindCertainConflicts(List<RuleSchemaIssue> issues, RuleSet ruleSet, IReadOnlyList<CompiledRule> rules)
    {
        var indexOf = ruleSet.Rules.Select((rule, index) => (rule, index)).ToDictionary(x => x.rule, x => x.index);
        var groups = rules
            .GroupBy(x => (x.Rule.Category, x.Priority, Condition: x.AppliesWhen.ToString(), x.Rule.Jurisdiction))
            .Where(g => g.Select(x => x.Requirement.ToString()).Distinct(StringComparer.Ordinal).Count() > 1);

        foreach (var group in groups)
        {
            var first = group.First();
            foreach (var other in group.Skip(1).Where(x => x.Requirement.ToString() != first.Requirement.ToString()))
                issues.Add(new RuleSchemaIssue($"rules[{indexOf[other.Rule]}].requiredValue", ConflictIssueCode,
                    $"與 {first.RuleId} 同類別、同優先序 {first.Priority}、同適用條件，但要求不同（{first.Requirement} ／ {other.Requirement}）；請調整優先序或條件。"));
        }
    }
}
