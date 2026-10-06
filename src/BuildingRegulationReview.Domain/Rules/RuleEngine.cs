using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules.Expressions;

namespace BuildingRegulationReview.Domain.Rules;

/// <summary>Which rules are in force: the review date and the jurisdiction the project sits in.</summary>
public sealed class RuleEvaluationContext
{
    public RuleEvaluationContext(DateTime reviewDate, string jurisdiction)
    {
        if (string.IsNullOrWhiteSpace(jurisdiction)) throw new ArgumentException("Jurisdiction is required.", nameof(jurisdiction));

        ReviewDate = reviewDate.Date;
        Jurisdiction = jurisdiction.Trim();
    }

    public DateTime ReviewDate { get; }
    public string Jurisdiction { get; }

    public bool IsInForce(Rule rule) =>
        rule.EffectiveDate <= ReviewDate && string.Equals(rule.Jurisdiction, Jurisdiction, StringComparison.Ordinal);
}

/// <summary>
/// Decides one subject against the rules of one category (spec 11.2):
/// <list type="number">
/// <item>Only rules in force for the review date and jurisdiction are considered; none at all is ManualReview.</item>
/// <item>Rules are taken from the highest priority down. The first priority with an applicable rule decides;
/// if any rule at that priority or above cannot tell whether it applies, the answer is InsufficientData —
/// a lower-priority rule never decides while a higher one might still apply.</item>
/// <item>Several deciding rules at the same priority must agree on status and required value, otherwise the
/// outcome is ManualReview with the conflict named.</item>
/// <item>A deciding rule whose exemption holds gives NotApplicable. An exemption that cannot be decided only
/// matters when the requirement fails, and then turns the fail into InsufficientData.</item>
/// <item>A missing actual or required value is InsufficientData, never Fail (spec 11.3).</item>
/// </list>
/// </summary>
public sealed class RuleEngine
{
    public RuleEngine(CompiledRuleSet ruleSet)
    {
        RuleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
    }

    public CompiledRuleSet RuleSet { get; }

    public RuleOutcome Evaluate(RuleCategory category, RuleFacts facts, RuleEvaluationContext context)
    {
        CheckFacts(facts);
        if (context is null) throw new ArgumentNullException(nameof(context));

        var candidates = RuleSet.OfCategory(category)
            .Where(x => context.IsInForce(x.Rule))
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.RuleId, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
            return new RuleOutcome(ReviewStatus.ManualReview, RuleOutcomeReason.NoRule,
                RuleSet.RuleSet.RuleSetId, RuleSet.RuleSet.Version, $"規則集「{RuleSet.RuleSet.Title}」", RuleSeverity.Error,
                $"規則集 {RuleSet.RuleSet.RuleSetId} {RuleSet.RuleSet.Version} 沒有 {context.Jurisdiction} 於 {context.ReviewDate:yyyy-MM-dd} 生效的 {category} 規則，需人工覆核。",
                consideredRuleIds: Array.Empty<string>());

        var undecided = new List<(CompiledRule Rule, RuleEvaluation Applies)>();
        foreach (var tier in candidates.GroupBy(x => x.Priority))
        {
            var applying = new List<CompiledRule>();
            foreach (var rule in tier)
            {
                var applies = rule.AppliesWhen.Evaluate(facts);
                if (applies.IsFailed) return ComputationFailed(rule, "適用條件", applies.Failure!, facts);
                if (applies.IsTrue) applying.Add(rule);
                else if (applies.IsUnknown) undecided.Add((rule, applies));
            }

            if (undecided.Count > 0) return UndecidedApplicability(undecided, facts);
            if (applying.Count == 0) continue;

            var outcomes = applying.Select(x => EvaluateApplicable(x, facts)).ToList();
            return outcomes.Count == 1 || outcomes.All(x => Agrees(x, outcomes[0]))
                ? outcomes[0]
                : Conflict(applying, outcomes, facts);
        }

        return NoRuleApplies(candidates, facts);
    }

    /// <summary>
    /// The 依據條文 of an outcome no rule applied to. It names the rule set by id and version, never by
    /// its title: the title is a design note citing half the code, and every 不適用 row would show it.
    /// </summary>
    private string NoRuleAppliesReference => $"規則集 {RuleSet.RuleSet.RuleSetId} {RuleSet.RuleSet.Version}（所列規則均不適用，無單一依據條文）";

    /// <summary>
    /// Evaluates one rule on the assumption that it applies: exemptions, then the requirement. The
    /// checks use this to compute a required value on its own (spec 11.5 "由規則引擎算出要求防火時效").
    /// </summary>
    public RuleOutcome EvaluateApplicable(CompiledRule rule, RuleFacts facts)
    {
        if (rule is null) throw new ArgumentNullException(nameof(rule));
        CheckFacts(facts);

        var exemptionGaps = new List<RuleFactGap>();
        for (var i = 0; i < rule.Exemptions.Count; i++)
        {
            var exempt = rule.Exemptions[i].Evaluate(facts);
            if (exempt.IsFailed) return ComputationFailed(rule, $"豁免條件 {i + 1}", exempt.Failure!, facts);
            if (exempt.IsTrue)
                return Make(rule, ReviewStatus.NotApplicable, RuleOutcomeReason.Exempt,
                    $"符合豁免條件：{rule.Rule.Exemptions[i].Source}。", facts, fields: rule.Exemptions[i].Fields);
            if (exempt.IsUnknown) exemptionGaps.AddRange(exempt.Gaps);
        }

        var requirement = rule.Requirement;
        var required = requirement.Required.Evaluate(facts);
        if (required.IsFailed) return ComputationFailed(rule, "要求值", required.Failure!, facts);

        var actual = facts.Find(requirement.Actual.Name);
        var gaps = new List<RuleFactGap>();
        if (actual is null) gaps.Add(facts.GapFor(requirement.Actual.Name)!);
        gaps.AddRange(required.Gaps);

        var target = $"{RuleComparatorText.Symbol(requirement.Comparator)} {Describe(required.Value)}";
        if (gaps.Count > 0)
            return Make(rule, ReviewStatus.InsufficientData, RuleOutcomeReason.MissingData,
                $"資料不足，無法判定是否 {target}：{Describe(gaps)}。", facts, actual, required.Value, gaps);

        if (RuleComparatorText.Holds(requirement.Comparator, actual!, required.Value!))
            return Make(rule, ReviewStatus.Pass, RuleOutcomeReason.Compared,
                $"{requirement.Actual.Name} = {RuleUnits.Format(actual!)}，符合 {target}。", facts, actual, required.Value);

        if (exemptionGaps.Count > 0)
            return Make(rule, ReviewStatus.InsufficientData, RuleOutcomeReason.MissingData,
                $"{requirement.Actual.Name} = {RuleUnits.Format(actual!)} 未達 {target}，但無法確認是否符合豁免條件：{Describe(exemptionGaps)}。",
                facts, actual, required.Value, exemptionGaps);

        return Make(rule, ReviewStatus.Fail, RuleOutcomeReason.Compared,
            $"{requirement.Actual.Name} = {RuleUnits.Format(actual!)}，未符合 {target}。", facts, actual, required.Value);
    }

    private void CheckFacts(RuleFacts facts)
    {
        if (facts is null) throw new ArgumentNullException(nameof(facts));
        if (!ReferenceEquals(facts.Catalog, RuleSet.Catalog))
            throw new ArgumentException("The facts were built against a different field whitelist than the rule set.", nameof(facts));
    }

    private static bool Agrees(RuleOutcome a, RuleOutcome b) =>
        a.Status == b.Status && Equals(a.RequiredValue, b.RequiredValue) && a.Comparator == b.Comparator;

    private static RuleOutcome Conflict(IReadOnlyList<CompiledRule> rules, IReadOnlyList<RuleOutcome> outcomes, RuleFacts facts)
    {
        var detail = string.Join("；", outcomes.Select(x =>
            $"{x.RuleId}（{x.LegalReference}）→ {ReviewStatusText.Label(x.Status)}" +
            (x.RequiredValue is null ? string.Empty : $"，要求 {RuleComparatorText.Symbol(x.Comparator!.Value)} {RuleUnits.Format(x.RequiredValue)}")));
        var first = rules[0];
        return new RuleOutcome(ReviewStatus.ManualReview, RuleOutcomeReason.Conflict,
            first.RuleId, first.Rule.Version, first.Rule.LegalReference, first.Rule.Severity,
            $"優先序 {first.Priority} 的 {rules.Count} 條規則同時適用但結論不一致，需人工覆核：{detail}。",
            evidence: EvidenceOf(facts, rules.SelectMany(x => x.AppliesWhen.Fields.Concat(x.EvidenceFields))),
            consideredRuleIds: rules.Select(x => x.RuleId));
    }

    private static RuleOutcome UndecidedApplicability(IReadOnlyList<(CompiledRule Rule, RuleEvaluation Applies)> undecided, RuleFacts facts)
    {
        var first = undecided[0].Rule;
        var gaps = undecided.SelectMany(x => x.Applies.Gaps).Distinct().ToList();
        return new RuleOutcome(ReviewStatus.InsufficientData, RuleOutcomeReason.MissingData,
            first.RuleId, first.Rule.Version, first.Rule.LegalReference, first.Rule.Severity,
            $"資料不足，無法判定規則 {string.Join("、", undecided.Select(x => x.Rule.RuleId))} 是否適用：{Describe(gaps)}。",
            evidence: EvidenceOf(facts, undecided.SelectMany(x => x.Rule.AppliesWhen.Fields)),
            gaps: gaps,
            consideredRuleIds: undecided.Select(x => x.Rule.RuleId));
    }

    /// <summary>
    /// None of the rules applies, so none of them is the answer's 依據: the outcome speaks for the rule
    /// set, as <see cref="RuleOutcomeReason.NoRule"/> does. Naming the first candidate instead would
    /// read as if, say, a wall had been reviewed under 第70條's beam rule. The severity is moot for a
    /// 不適用 and stays the first candidate's only because an outcome must carry one.
    /// </summary>
    private RuleOutcome NoRuleApplies(IReadOnlyList<CompiledRule> candidates, RuleFacts facts)
    {
        return new RuleOutcome(ReviewStatus.NotApplicable, RuleOutcomeReason.NoRuleApplies,
            RuleSet.RuleSet.RuleSetId, RuleSet.RuleSet.Version, NoRuleAppliesReference, candidates[0].Rule.Severity,
            $"規則 {string.Join("、", candidates.Select(x => x.RuleId))} 的適用條件均不成立。",
            evidence: EvidenceOf(facts, candidates.SelectMany(x => x.AppliesWhen.Fields)),
            consideredRuleIds: candidates.Select(x => x.RuleId));
    }

    private static RuleOutcome ComputationFailed(CompiledRule rule, string part, string failure, RuleFacts facts) =>
        Make(rule, ReviewStatus.ManualReview, RuleOutcomeReason.ComputationFailed,
            $"規則 {rule.RuleId} 的{part}無法計算，需人工覆核：{failure}", facts);

    private static RuleOutcome Make(
        CompiledRule rule,
        ReviewStatus status,
        RuleOutcomeReason reason,
        string message,
        RuleFacts facts,
        ReviewValue? actual = null,
        ReviewValue? required = null,
        IEnumerable<RuleFactGap>? gaps = null,
        IEnumerable<RuleFieldDefinition>? fields = null) =>
        new(status, reason, rule.RuleId, rule.Rule.Version, rule.Rule.LegalReference, rule.Rule.Severity, message,
            actual, required, rule.Requirement.Comparator,
            EvidenceOf(facts, new[] { rule.Requirement.Actual }.Concat(rule.EvidenceFields).Concat(fields ?? Enumerable.Empty<RuleFieldDefinition>())),
            gaps);

    /// <summary>The known values of the given fields, in first-mention order; missing ones are reported as gaps instead.</summary>
    private static ReviewEvidence EvidenceOf(RuleFacts facts, IEnumerable<RuleFieldDefinition> fields) =>
        new(fields.Select(x => x.Name).Distinct(StringComparer.Ordinal)
            .Select(name => (name, value: facts.Find(name)))
            .Where(x => x.value is not null)
            .Select(x => new ReviewEvidenceItem(x.name, x.value!)));

    private static string Describe(ReviewValue? value) => value is null ? "（無法計算）" : RuleUnits.Format(value);
    private static string Describe(IEnumerable<RuleFactGap> gaps) => string.Join("、", gaps.Distinct());
}
