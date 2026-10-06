using System;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using BuildingRegulationReview.Domain.Rules.Expressions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Rules;

public sealed class RuleEngineTests
{
    private static readonly RuleEvaluationContext Today = new(new DateTime(2025, 6, 1), "TW");

    private static Rule Make(
        string ruleId,
        string appliesWhen,
        string requiredValue,
        RuleCategory category = RuleCategory.CompartmentArea,
        int priority = 10,
        string[]? exemptions = null,
        string[]? evidenceFields = null,
        DateTime? effectiveDate = null,
        string jurisdiction = "TW",
        string version = "1") =>
        new(ruleId, version, category, $"第{ruleId}條", effectiveDate ?? new DateTime(2024, 1, 1), jurisdiction, priority,
            new RuleExpression(appliesWhen), new RuleExpression(requiredValue),
            (exemptions ?? Array.Empty<string>()).Select(x => new RuleExpression(x)), evidenceFields);

    private static RuleEngine Engine(params Rule[] rules)
    {
        var compiled = RuleSetCompiler.Compile(new RuleSet("set", "2024.1", "測試規則集", rules));
        Assert.True(compiled.IsSuccess, compiled.Error.TechnicalDetail);
        return new RuleEngine(compiled.Value);
    }

    private static RuleFacts Facts() => new(RuleFieldCatalog.Default);

    private static Rule AreaRule(int priority = 10) => Make("79", "building.fireResistiveConstruction == true",
        "zone.area <= (zone.sprinklered ? 3000 m2 : 1500 m2)", priority: priority,
        exemptions: new[] { "zone.use == \"樓梯間\"" },
        evidenceFields: new[] { "zone.sprinklered", "building.fireResistiveConstruction" });

    private static RuleFacts Zone(double? area, bool? sprinklered = false, string? use = "辦公")
    {
        var facts = Facts().Set("building.fireResistiveConstruction", true);
        if (area is not null) facts.Set("zone.area", area.Value, ReviewUnit.SquareMeter);
        if (sprinklered is not null) facts.Set("zone.sprinklered", sprinklered.Value);
        if (use is not null) facts.Set("zone.use", use);
        return facts;
    }

    private static RuleOutcome Area(RuleEngine engine, RuleFacts facts) => engine.Evaluate(RuleCategory.CompartmentArea, facts, Today);

    [Theory]
    [InlineData(1499.99, false, ReviewStatus.Pass)]
    [InlineData(1500, false, ReviewStatus.Pass)]
    [InlineData(1500.0000000001, false, ReviewStatus.Pass)]
    [InlineData(1500.01, false, ReviewStatus.Fail)]
    [InlineData(3000, true, ReviewStatus.Pass)]
    [InlineData(3000.5, true, ReviewStatus.Fail)]
    [InlineData(0, false, ReviewStatus.Pass)]
    public void Area_limit_is_inclusive_at_the_boundary(double area, bool sprinklered, ReviewStatus expected)
    {
        var outcome = Area(Engine(AreaRule()), Zone(area, sprinklered));

        Assert.Equal(expected, outcome.Status);
        Assert.Equal(RuleOutcomeReason.Compared, outcome.Reason);
        Assert.Equal(ReviewValue.Quantity(area, ReviewUnit.SquareMeter), outcome.ActualValue);
        Assert.Equal(ReviewValue.Quantity(sprinklered ? 3000 : 1500, ReviewUnit.SquareMeter), outcome.RequiredValue);
        Assert.Equal(RuleComparator.LessOrEqual, outcome.Comparator);
        Assert.Equal("79", outcome.RuleId);
        Assert.Equal("第79條", outcome.LegalReference);
    }

    [Fact]
    public void Fire_rating_limit_is_inclusive_and_hours_convert_to_minutes()
    {
        var engine = Engine(Make("wall", "element.isCompartmentBoundary == true", "element.providedFireRating >= 1 h",
            RuleCategory.FireResistance));
        RuleOutcome Rate(double minutes) => engine.Evaluate(RuleCategory.FireResistance,
            Facts().Set("element.isCompartmentBoundary", true).Set("element.providedFireRating", minutes, ReviewUnit.Minute), Today);

        Assert.Equal(ReviewStatus.Pass, Rate(60).Status);
        Assert.Equal(ReviewStatus.Pass, Rate(120).Status);
        Assert.Equal(ReviewStatus.Fail, Rate(59.9).Status);
        Assert.Equal(ReviewValue.Quantity(60, ReviewUnit.Minute), Rate(30).RequiredValue);
    }

    [Fact]
    public void A_missing_actual_value_is_insufficient_data_and_still_reports_what_is_required()
    {
        var outcome = Area(Engine(AreaRule()), Zone(null));

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
        Assert.Equal(RuleOutcomeReason.MissingData, outcome.Reason);
        Assert.Null(outcome.ActualValue);
        Assert.Equal(ReviewValue.Quantity(1500, ReviewUnit.SquareMeter), outcome.RequiredValue);
        Assert.Equal(new[] { new RuleFactGap("zone.area", RuleFactGapKind.Missing) }, outcome.Gaps);
        Assert.Contains("zone.area 未設定", outcome.Message);
    }

    [Fact]
    public void A_missing_input_to_the_required_value_is_insufficient_data_even_when_the_area_is_large()
    {
        // 5000 m2 would fail both limits, but the verdict still waits for the data (spec 11.3).
        foreach (var area in new[] { 1000.0, 5000.0 })
        {
            var outcome = Area(Engine(AreaRule()), Zone(area, sprinklered: null));

            Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
            Assert.Null(outcome.RequiredValue);
            Assert.Equal("zone.sprinklered", outcome.Gaps.Single().Field);
        }
    }

    [Fact]
    public void An_unreadable_value_is_insufficient_data_that_says_it_could_not_be_read()
    {
        var engine = Engine(Make("wall", "true", "element.providedFireRating >= 60 min", RuleCategory.FireResistance));
        var outcome = engine.Evaluate(RuleCategory.FireResistance,
            Facts().MarkUnreadable("element.providedFireRating", "「一小時半」"), Today);

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
        Assert.Equal(RuleFactGapKind.Unreadable, outcome.Gaps.Single().Kind);
        Assert.Contains("格式無法判讀", outcome.Message);
    }

    [Fact]
    public void A_rule_that_does_not_apply_is_not_applicable_and_keeps_the_applicability_evidence()
    {
        var facts = Zone(5000).Set("building.fireResistiveConstruction", false);
        var outcome = Area(Engine(AreaRule()), facts);

        Assert.Equal(ReviewStatus.NotApplicable, outcome.Status);
        Assert.Equal(RuleOutcomeReason.NoRuleApplies, outcome.Reason);
        Assert.Equal(ReviewValue.OfBoolean(false), outcome.Evidence.Find("building.fireResistiveConstruction"));
    }

    /// <summary>No rule applied, so none of them is the 依據: the outcome speaks for the rule set, not its first candidate.</summary>
    [Fact]
    public void A_not_applicable_outcome_does_not_cite_the_first_rule_it_considered()
    {
        var engine = Engine(AreaRule());
        var outcome = Area(engine, Zone(5000).Set("building.fireResistiveConstruction", false));

        Assert.Equal(engine.RuleSet.RuleSet.RuleSetId, outcome.RuleId);
        Assert.Equal(engine.RuleSet.RuleSet.Version, outcome.RuleVersion);
        Assert.Equal(new[] { AreaRule().RuleId }, outcome.ConsideredRuleIds);
    }

    /// <summary>
    /// The built-in rule set's title is a design note that cites 第70條、第83條 and a 函釋; a 不適用 row
    /// must cite none of it — only the rule set by id and version.
    /// </summary>
    [Fact]
    public void A_not_applicable_outcome_of_the_built_in_rules_cites_no_clause_and_no_letter()
    {
        var json = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(System.Text.Json.JsonSerializer.Deserialize<RuleSetDocument>(System.IO.File.ReadAllText(path), json));
        Assert.True(loaded.IsSuccess, loaded.Error.TechnicalDetail);

        var engine = new RuleEngine(loaded.Value);
        var facts = new RuleFacts(engine.RuleSet.Catalog).Set("building.fireResistiveConstruction", false);
        var outcome = engine.Evaluate(RuleCategory.FireResistance, facts, Today);

        Assert.Equal(RuleOutcomeReason.NoRuleApplies, outcome.Reason);
        var reference = ReviewLegalReference.Parse(outcome.LegalReference);
        Assert.Empty(reference.Clauses);
        Assert.Empty(reference.Letters);
        Assert.Equal(new[] { $"規則集 {loaded.Value.RuleSet.RuleSetId} {loaded.Value.RuleSet.Version}" }, reference.Remarks);
        Assert.Equal(new[] { "所列規則均不適用，無單一依據條文" }, reference.Gists);
    }

    [Fact]
    public void Unknown_applicability_is_insufficient_data()
    {
        var facts = Facts().Set("zone.area", 5000, ReviewUnit.SquareMeter).Set("zone.sprinklered", false);
        var outcome = Area(Engine(AreaRule()), facts);

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
        Assert.Equal("building.fireResistiveConstruction", outcome.Gaps.Single().Field);
        Assert.Contains("是否適用", outcome.Message);
    }

    [Fact]
    public void A_holding_exemption_makes_the_subject_not_applicable_even_over_the_limit()
    {
        var outcome = Area(Engine(AreaRule()), Zone(9999, use: "樓梯間"));

        Assert.Equal(ReviewStatus.NotApplicable, outcome.Status);
        Assert.Equal(RuleOutcomeReason.Exempt, outcome.Reason);
        Assert.Contains("樓梯間", outcome.Message);
        Assert.Equal(ReviewValue.OfText("樓梯間"), outcome.Evidence.Find("zone.use"));
    }

    [Fact]
    public void An_undecidable_exemption_only_matters_when_the_requirement_fails()
    {
        var engine = Engine(AreaRule());

        Assert.Equal(ReviewStatus.Pass, Area(engine, Zone(1000, use: null)).Status);

        var over = Area(engine, Zone(2000, use: null));
        Assert.Equal(ReviewStatus.InsufficientData, over.Status);
        Assert.Equal("zone.use", over.Gaps.Single().Field);
        Assert.Contains("豁免", over.Message);
        Assert.Equal(ReviewValue.Quantity(2000, ReviewUnit.SquareMeter), over.ActualValue);
    }

    [Fact]
    public void A_higher_priority_rule_overrides_a_lower_one()
    {
        var general = AreaRule(priority: 10);
        var warehouse = Make("warehouse", "zone.use == \"倉庫\"", "zone.area <= 1000 m2", priority: 20);
        var engine = Engine(general, warehouse);

        var inWarehouse = Area(engine, Zone(1200, use: "倉庫"));
        Assert.Equal(ReviewStatus.Fail, inWarehouse.Status);
        Assert.Equal("warehouse", inWarehouse.RuleId);

        var inOffice = Area(engine, Zone(1200, use: "辦公"));
        Assert.Equal(ReviewStatus.Pass, inOffice.Status);
        Assert.Equal("79", inOffice.RuleId);
    }

    [Fact]
    public void A_lower_priority_rule_never_decides_while_a_higher_one_might_apply()
    {
        var engine = Engine(AreaRule(priority: 10), Make("warehouse", "zone.use == \"倉庫\"", "zone.area <= 1000 m2", priority: 20));

        var outcome = Area(engine, Zone(1200, use: null));

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
        Assert.Equal("warehouse", outcome.RuleId);
        Assert.Equal("zone.use", outcome.Gaps.Single().Field);
    }

    [Fact]
    public void Same_priority_rules_that_disagree_are_a_conflict_for_manual_review()
    {
        var engine = Engine(
            Make("a", "zone.use == \"倉庫\"", "zone.area <= 1000 m2"),
            Make("b", "zone.sprinklered == false", "zone.area <= 1500 m2"));

        var outcome = Area(engine, Zone(1200, sprinklered: false, use: "倉庫"));

        Assert.Equal(ReviewStatus.ManualReview, outcome.Status);
        Assert.Equal(RuleOutcomeReason.Conflict, outcome.Reason);
        Assert.Equal(new[] { "a", "b" }, outcome.ConsideredRuleIds);
        Assert.Contains("1000 m2", outcome.Message);
        Assert.Contains("1500 m2", outcome.Message);
        Assert.Equal(ReviewErrorCode.RuleConflict, RuleOutcomeErrorCode.For(outcome));

        // Where only one of them applies there is nothing to resolve.
        Assert.Equal(ReviewStatus.Pass, Area(engine, Zone(1200, sprinklered: false, use: "辦公")).Status);
    }

    [Fact]
    public void Same_priority_rules_that_agree_are_not_a_conflict()
    {
        var engine = Engine(
            Make("b", "zone.use == \"倉庫\"", "zone.area <= 1500 m2"),
            Make("a", "zone.sprinklered == false", "zone.area <= 1500.0 m2"));

        var outcome = Area(engine, Zone(1200, sprinklered: false, use: "倉庫"));

        Assert.Equal(ReviewStatus.Pass, outcome.Status);
        Assert.Equal("a", outcome.RuleId);
        Assert.Null(RuleOutcomeErrorCode.For(outcome));
    }

    [Fact]
    public void Rules_not_yet_in_force_or_for_another_jurisdiction_are_ignored()
    {
        var engine = Engine(
            Make("future", "true", "zone.area <= 1 m2", effectiveDate: new DateTime(2025, 6, 2)),
            Make("elsewhere", "true", "zone.area <= 1 m2", jurisdiction: "JP"),
            Make("current", "true", "zone.area <= 1500 m2", priority: 1, effectiveDate: new DateTime(2025, 6, 1)));

        var outcome = Area(engine, Zone(1200));

        Assert.Equal(ReviewStatus.Pass, outcome.Status);
        Assert.Equal("current", outcome.RuleId);
    }

    [Fact]
    public void No_rule_in_force_is_manual_review_with_the_rule_missing_code()
    {
        var engine = Engine(Make("future", "true", "zone.area <= 1 m2", effectiveDate: new DateTime(2030, 1, 1)));

        var outcome = Area(engine, Zone(1200));
        Assert.Equal(ReviewStatus.ManualReview, outcome.Status);
        Assert.Equal(RuleOutcomeReason.NoRule, outcome.Reason);
        Assert.Equal("set", outcome.RuleId);
        Assert.Empty(outcome.ConsideredRuleIds);
        Assert.Equal(ReviewErrorCode.RuleMissing, RuleOutcomeErrorCode.For(outcome));

        var openings = engine.Evaluate(RuleCategory.OpeningProtection, Facts(), Today);
        Assert.Equal(RuleOutcomeReason.NoRule, openings.Reason);
    }

    [Fact]
    public void A_computation_error_is_manual_review_not_fail()
    {
        var engine = Engine(Make("per-floor", "true", "zone.area <= 3000 m2 / building.floorsAboveGround"));

        var outcome = Area(engine, Zone(1200).Set("building.floorsAboveGround", 0, ReviewUnit.None));

        Assert.Equal(ReviewStatus.ManualReview, outcome.Status);
        Assert.Equal(RuleOutcomeReason.ComputationFailed, outcome.Reason);
        Assert.Contains("除以零", outcome.Message);
        Assert.Equal(ReviewErrorCode.RuleComputationFailed, RuleOutcomeErrorCode.For(outcome));
    }

    [Theory]
    [InlineData("是", ReviewStatus.Pass)]
    [InlineData("否", ReviewStatus.Fail)]
    [InlineData(null, ReviewStatus.InsufficientData)]
    public void Opening_protection_is_pass_fail_or_insufficient_data(string? provided, ReviewStatus expected)
    {
        var engine = Engine(Make("76", "opening.hostIsCompartmentBoundary == true", "opening.providedFireProtection == \"是\"",
            RuleCategory.OpeningProtection, evidenceFields: new[] { "opening.hostUniqueId" }));
        var facts = Facts().Set("opening.hostIsCompartmentBoundary", true).Set("opening.hostUniqueId", "wall-1");
        if (provided is not null) facts.Set("opening.providedFireProtection", provided);

        var outcome = engine.Evaluate(RuleCategory.OpeningProtection, facts, Today);

        Assert.Equal(expected, outcome.Status);
        Assert.Equal(ReviewValue.OfText("是"), outcome.RequiredValue);
        Assert.Equal(ReviewValue.OfText("wall-1"), outcome.Evidence.Find("opening.hostUniqueId"));

        var notBoundary = engine.Evaluate(RuleCategory.OpeningProtection, Facts().Set("opening.hostIsCompartmentBoundary", false), Today);
        Assert.Equal(ReviewStatus.NotApplicable, notBoundary.Status);
    }

    [Fact]
    public void Evidence_records_the_actual_and_listed_fields_that_are_known()
    {
        var outcome = Area(Engine(AreaRule()), Zone(1600, sprinklered: false));

        Assert.Equal(ReviewStatus.Fail, outcome.Status);
        Assert.Equal(new[] { "zone.area", "zone.sprinklered", "building.fireResistiveConstruction" },
            outcome.Evidence.Items.Select(x => x.Field));
        Assert.Contains("1600 m2", outcome.Message);
        Assert.Contains("<= 1500 m2", outcome.Message);
    }

    [Theory]
    [InlineData(1000.0, ReviewStatus.Pass)]
    [InlineData(2000.0, ReviewStatus.Fail)]
    [InlineData(null, ReviewStatus.InsufficientData)]
    public void Every_outcome_becomes_a_valid_review_result(double? area, ReviewStatus expected)
    {
        var outcome = Area(Engine(AreaRule()), Zone(area));
        var runId = Guid.NewGuid();

        var result = outcome.ToReviewResult(Guid.NewGuid(), runId, Guid.NewGuid(), "CompartmentArea", null, "Z-01");

        Assert.Equal(expected, result.Status);
        Assert.Equal(runId, result.RunId);
        Assert.Equal("79", result.RuleId);
        Assert.Equal("1", result.RuleVersion);
        Assert.Equal("Z-01", result.ZoneId);
        Assert.Equal(outcome.Message, result.Message);
        Assert.Equal(outcome.RequiredValue, result.RequiredValue);
    }

    [Fact]
    public void Not_applicable_conflict_and_missing_rule_outcomes_also_become_review_results()
    {
        var conflict = Area(Engine(
            Make("a", "true", "zone.area <= 1000 m2"),
            Make("b", "zone.sprinklered == false", "zone.area <= 1500 m2")), Zone(1200));
        var missing = Area(Engine(Make("future", "true", "zone.area <= 1 m2", effectiveDate: new DateTime(2030, 1, 1))), Zone(1));
        var exempt = Area(Engine(AreaRule()), Zone(9999, use: "樓梯間"));

        foreach (var outcome in new[] { conflict, missing, exempt })
        {
            var result = outcome.ToReviewResult(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "CompartmentArea", new[] { "area-1" }, null);
            Assert.Equal(outcome.Status, result.Status);
        }
    }

    [Fact]
    public void The_engine_refuses_facts_built_against_another_whitelist()
    {
        var engine = Engine(AreaRule());
        var other = new RuleFieldCatalog(RuleFieldCatalog.Default.Fields);

        Assert.Throws<ArgumentException>(() => engine.Evaluate(RuleCategory.CompartmentArea, new RuleFacts(other), Today));
        Assert.Throws<ArgumentNullException>(() => engine.Evaluate(RuleCategory.CompartmentArea, null!, Today));
        Assert.Throws<ArgumentNullException>(() => engine.Evaluate(RuleCategory.CompartmentArea, Facts(), null!));
        Assert.Throws<ArgumentException>(() => new RuleEvaluationContext(DateTime.Today, " "));
    }

    [Fact]
    public void A_rule_can_be_evaluated_on_its_own_to_compute_its_required_value()
    {
        var engine = Engine(Make("wall", "element.isCompartmentBoundary == true", "element.providedFireRating >= 2 h",
            RuleCategory.FireResistance));

        var outcome = engine.EvaluateApplicable(engine.RuleSet.Rules.Single(), Facts());

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
        Assert.Equal(ReviewValue.Quantity(120, ReviewUnit.Minute), outcome.RequiredValue);
    }
}
