using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Domain.Rules.Expressions;

namespace BuildingRegulationReview.Domain.Rules;

/// <summary>A rule whose expressions have been parsed and type-checked against the field whitelist.</summary>
public sealed class CompiledRule
{
    public CompiledRule(
        Rule rule,
        RuleExpressionNode appliesWhen,
        RuleRequirement requirement,
        IEnumerable<RuleExpressionNode> exemptions,
        IEnumerable<RuleFieldDefinition> evidenceFields)
    {
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        AppliesWhen = appliesWhen ?? throw new ArgumentNullException(nameof(appliesWhen));
        Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        if (!appliesWhen.Type.IsBoolean) throw new ArgumentException("appliesWhen must be a condition.", nameof(appliesWhen));

        var exemptionList = (exemptions ?? throw new ArgumentNullException(nameof(exemptions))).ToList();
        if (exemptionList.Any(x => x is null || !x.Type.IsBoolean))
            throw new ArgumentException("Every exemption must be a condition.", nameof(exemptions));
        if (exemptionList.Count != rule.Exemptions.Count)
            throw new ArgumentException("Each exemption of the rule must be compiled exactly once.", nameof(exemptions));

        var fieldList = (evidenceFields ?? throw new ArgumentNullException(nameof(evidenceFields))).ToList();
        if (fieldList.Any(x => x is null)) throw new ArgumentException("Evidence fields cannot be missing.", nameof(evidenceFields));

        Exemptions = new ReadOnlyCollection<RuleExpressionNode>(exemptionList);
        EvidenceFields = new ReadOnlyCollection<RuleFieldDefinition>(fieldList);
    }

    public Rule Rule { get; }
    public RuleExpressionNode AppliesWhen { get; }
    public RuleRequirement Requirement { get; }
    public IReadOnlyList<RuleExpressionNode> Exemptions { get; }
    public IReadOnlyList<RuleFieldDefinition> EvidenceFields { get; }

    public string RuleId => Rule.RuleId;
    public int Priority => Rule.Priority;
}

/// <summary>
/// A <see cref="RuleSet"/> every expression of which compiled. Only a compiled set reaches the
/// engine, so a review never starts on a rule that cannot be read.
/// </summary>
public sealed class CompiledRuleSet
{
    public CompiledRuleSet(RuleSet ruleSet, RuleFieldCatalog catalog, IEnumerable<CompiledRule> rules)
    {
        RuleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        var list = (rules ?? throw new ArgumentNullException(nameof(rules))).ToList();
        if (list.Any(x => x is null)) throw new ArgumentException("A compiled set cannot hold a missing rule.", nameof(rules));
        if (list.Count != ruleSet.Rules.Count || list.Select(x => x.Rule).Except(ruleSet.Rules).Any())
            throw new ArgumentException("Every rule of the set must be compiled exactly once.", nameof(rules));

        Rules = new ReadOnlyCollection<CompiledRule>(list);
    }

    public RuleSet RuleSet { get; }
    public RuleFieldCatalog Catalog { get; }
    public IReadOnlyList<CompiledRule> Rules { get; }

    public IEnumerable<CompiledRule> OfCategory(RuleCategory category) => Rules.Where(x => x.Rule.Category == category);
}
