using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using BuildingRegulationReview.Domain.Rules.Expressions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Rules;

public sealed class RuleSetCompilerTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static RuleSetDocument Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "Fixtures", name);
        return JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json)!;
    }

    private static Rule Make(string ruleId, string appliesWhen, string requiredValue, int priority = 10,
        RuleCategory category = RuleCategory.CompartmentArea, string jurisdiction = "TW") =>
        new(ruleId, "1", category, "第79條", new DateTime(2024, 1, 1), jurisdiction, priority,
            new RuleExpression(appliesWhen), new RuleExpression(requiredValue));

    private static RuleSetValidationReport Check(params Rule[] rules) =>
        RuleSetCompiler.Check(new RuleSet("set", "1", "t", rules), null, out _);

    [Fact]
    public void Valid_fixture_compiles_and_reviews_end_to_end()
    {
        var loaded = RuleSetCompiler.Load(Fixture("valid-ruleset.json"));
        Assert.True(loaded.IsSuccess, loaded.IsFailure ? loaded.Error.TechnicalDetail : null);

        var set = loaded.Value;
        Assert.Same(RuleFieldCatalog.Default, set.Catalog);
        Assert.Equal(3, set.Rules.Count);

        var engine = new RuleEngine(set);
        var facts = new RuleFacts(set.Catalog)
            .Set("building.fireResistiveConstruction", true)
            .Set("zone.sprinklered", true)
            .Set("zone.use", "辦公")
            .Set("zone.area", 2800, ReviewUnit.SquareMeter);

        var outcome = engine.Evaluate(RuleCategory.CompartmentArea, facts, new RuleEvaluationContext(new DateTime(2024, 6, 1), "TW"));

        Assert.Equal(ReviewStatus.Pass, outcome.Status);
        Assert.Equal("tw-bcr-79-area", outcome.RuleId);
        Assert.Equal(ReviewValue.Quantity(3000, ReviewUnit.SquareMeter), outcome.RequiredValue);
    }

    [Fact]
    public void Invalid_expressions_fixture_lists_every_problem_and_loads_nothing()
    {
        var document = Fixture("invalid-expressions.json");
        Assert.True(RuleSetSchemaValidator.Validate(document).IsValid);

        var report = RuleSetCompiler.Check(RuleSetDocumentMapper.FromDocument(document).Value, null, out var compiled);

        Assert.Null(compiled);
        Assert.Equal(new[]
        {
            ("rules[0].appliesWhen", RuleExpressionErrorCode.UnknownField),
            ("rules[0].requiredValue", RuleExpressionErrorCode.TypeMismatch),
            ("rules[0].exemptions[0]", RuleExpressionErrorCode.Syntax),
            ("rules[0].evidenceFields[0]", RuleExpressionErrorCode.UnknownField),
            ("rules[0].evidenceFields[1]", RuleExpressionErrorCode.FieldNotAvailable),
            ("rules[1].appliesWhen", RuleExpressionErrorCode.FieldNotAvailable),
            ("rules[1].requiredValue", RuleExpressionErrorCode.InvalidForm)
        }, report.Issues.Select(x => (x.Path, x.Code)).ToArray());
        Assert.Contains("第 1 個字元", report.Issues[0].Message);

        var loaded = RuleSetCompiler.Load(document);
        Assert.True(loaded.IsFailure);
        Assert.Equal(ReviewErrorCode.RuleSchemaInvalid, loaded.Error.Code);
        Assert.Contains("7 處", loaded.Error.Message);
        Assert.Contains("rules[1].requiredValue", loaded.Error.TechnicalDetail);
    }

    [Fact]
    public void A_structurally_invalid_file_fails_before_compiling()
    {
        var loaded = RuleSetCompiler.Load(Fixture("invalid-missing-fields.json"));

        Assert.True(loaded.IsFailure);
        Assert.Equal(ReviewErrorCode.RuleSchemaInvalid, loaded.Error.Code);
        Assert.True(RuleSetCompiler.Load(null).IsFailure);
    }

    [Fact]
    public void Rules_certain_to_conflict_are_refused_with_the_conflict_code()
    {
        var rules = new[]
        {
            Make("a", "zone.sprinklered == false", "zone.area <= 1500 m2"),
            Make("b", "zone.sprinklered==false", "zone.area <= 1000 m2")
        };

        var report = Check(rules);
        var issue = Assert.Single(report.Issues);
        Assert.Equal("rules[1].requiredValue", issue.Path);
        Assert.Equal(RuleSetCompiler.ConflictIssueCode, issue.Code);
        Assert.Contains("a", issue.Message);

        var compiled = RuleSetCompiler.Compile(new RuleSet("set", "1", "t", rules));
        Assert.True(compiled.IsFailure);
        Assert.Equal(ReviewErrorCode.RuleConflict, compiled.Error.Code);
    }

    [Fact]
    public void Same_condition_is_not_a_conflict_when_priority_jurisdiction_or_requirement_differ()
    {
        Assert.True(Check(
            Make("a", "zone.sprinklered == false", "zone.area <= 1500 m2", priority: 10),
            Make("b", "zone.sprinklered == false", "zone.area <= 1000 m2", priority: 20)).IsValid);

        Assert.True(Check(
            Make("a", "zone.sprinklered == false", "zone.area <= 1500 m2", jurisdiction: "TW"),
            Make("b", "zone.sprinklered == false", "zone.area <= 1000 m2", jurisdiction: "TW-TPE")).IsValid);

        Assert.True(Check(
            Make("a", "zone.sprinklered == false", "zone.area <= 1500 m2"),
            Make("b", "zone.sprinklered == false", "zone.area <= (1500 m2)")).IsValid);

        Assert.True(Check(
            Make("a", "zone.sprinklered == false", "zone.area <= 1500 m2"),
            Make("b", "zone.sprinklered == false", "zone.area <= 1000 m2", category: RuleCategory.FireResistance)).IsValid);
    }

    [Fact]
    public void A_mixed_report_uses_the_schema_code()
    {
        var compiled = RuleSetCompiler.Compile(new RuleSet("set", "1", "t", new[]
        {
            Make("a", "zone.sprinklered == false", "zone.area <= 1500 m2"),
            Make("b", "zone.sprinklered == false", "zone.area <= 1000 m2"),
            Make("c", "nope.field", "zone.area <= 1000 m2")
        }));

        Assert.True(compiled.IsFailure);
        Assert.Equal(ReviewErrorCode.RuleSchemaInvalid, compiled.Error.Code);
    }

    [Fact]
    public void A_compiled_set_must_cover_every_rule_exactly_once()
    {
        var set = new RuleSet("set", "1", "t", new[] { Make("a", "true", "zone.area <= 1 m2"), Make("b", "true", "zone.area <= 1 m2") });
        var compiled = RuleSetCompiler.Compile(set).Value;

        Assert.Throws<ArgumentException>(() => new CompiledRuleSet(set, RuleFieldCatalog.Default, compiled.Rules.Take(1)));
        Assert.Throws<ArgumentException>(() => new CompiledRuleSet(set, RuleFieldCatalog.Default, compiled.Rules.Concat(compiled.Rules.Take(1))));
    }
}
