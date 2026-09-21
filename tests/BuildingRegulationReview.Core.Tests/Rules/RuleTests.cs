using System;
using System.Linq;
using BuildingRegulationReview.Domain.Rules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Rules;

public sealed class RuleTests
{
    private static Rule Make(
        string ruleId = "r-1",
        string version = "1",
        RuleCategory category = RuleCategory.CompartmentArea,
        string legalReference = "第79條",
        string jurisdiction = "TW",
        int priority = 0,
        string[]? evidenceFields = null,
        RuleSeverity severity = RuleSeverity.Error) =>
        new Rule(ruleId, version, category, legalReference, new DateTime(2024, 1, 1, 13, 45, 0), jurisdiction, priority,
            new RuleExpression("true"), new RuleExpression("1500 m2"),
            new[] { new RuleExpression(" zone.use == \"樓梯間\" ") }, evidenceFields ?? new[] { " zone.area " }, severity);

    [Fact]
    public void Rule_trims_its_text_and_keeps_only_the_calendar_date()
    {
        var rule = Make(ruleId: " r-1 ", legalReference: " 第79條 ");

        Assert.Equal("r-1", rule.RuleId);
        Assert.Equal("第79條", rule.LegalReference);
        Assert.Equal(new DateTime(2024, 1, 1), rule.EffectiveDate);
        Assert.Equal("zone.use == \"樓梯間\"", rule.Exemptions.Single().Source);
        Assert.Equal(new[] { "zone.area" }, rule.EvidenceFields);
    }

    [Fact]
    public void Rule_rejects_missing_identity_reference_and_jurisdiction()
    {
        Assert.Throws<ArgumentException>(() => Make(ruleId: " "));
        Assert.Throws<ArgumentException>(() => Make(version: ""));
        Assert.Throws<ArgumentException>(() => Make(legalReference: " "));
        Assert.Throws<ArgumentException>(() => Make(jurisdiction: ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(priority: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(category: (RuleCategory)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(severity: (RuleSeverity)99));
    }

    [Fact]
    public void Rule_rejects_blank_or_duplicate_evidence_fields()
    {
        Assert.Throws<ArgumentException>(() => Make(evidenceFields: new[] { "zone.area", " " }));
        Assert.Throws<ArgumentException>(() => Make(evidenceFields: new[] { "zone.area", " zone.area" }));
    }

    [Fact]
    public void Expressions_are_never_blank_and_compare_by_source()
    {
        Assert.Throws<ArgumentException>(() => new RuleExpression(" "));
        Assert.Throws<ArgumentException>(() => new RuleExpression(null!));
        Assert.Equal(new RuleExpression("a == 1"), new RuleExpression(" a == 1 "));
        Assert.NotEqual(new RuleExpression("a == 1"), new RuleExpression("a==1"));
    }

    [Fact]
    public void Rule_set_pins_one_version_of_each_rule()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new RuleSet("set", "1", "title", new[] { Make(version: "1"), Make(version: "2") }));
        Assert.Contains("r-1", error.Message);

        Assert.Throws<ArgumentException>(() => new RuleSet("set", "1", "title", Array.Empty<Rule>()));
        Assert.Throws<ArgumentException>(() => new RuleSet(" ", "1", "title", new[] { Make() }));
        Assert.Throws<ArgumentException>(() => new RuleSet("set", " ", "title", new[] { Make() }));
    }

    [Fact]
    public void Rule_set_finds_rules_by_id_and_groups_them_by_category()
    {
        var set = new RuleSet(" set ", " 1 ", null!, new[]
        {
            Make(ruleId: "area"),
            Make(ruleId: "wall", category: RuleCategory.FireResistance),
            Make(ruleId: "beam", category: RuleCategory.FireResistance)
        });

        Assert.Equal("set", set.RuleSetId);
        Assert.Equal("1", set.Version);
        Assert.Equal(string.Empty, set.Title);
        Assert.Equal(RuleSet.CurrentSchemaVersion, set.SchemaVersion);
        Assert.Same(set.Rules[1], set.Find("wall"));
        Assert.Null(set.Find("column"));
        Assert.Null(set.Find(null!));
        Assert.Equal(new[] { "wall", "beam" }, set.OfCategory(RuleCategory.FireResistance).Select(x => x.RuleId));
        Assert.Empty(set.OfCategory(RuleCategory.OpeningProtection));
    }
}
