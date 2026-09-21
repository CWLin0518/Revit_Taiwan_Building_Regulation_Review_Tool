using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Rules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Rules;

public sealed class RuleSetSchemaTests
{
    // Rule files are authored in camelCase; an unknown property is a typo the file's author should
    // hear about, not a field to drop on the floor.
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static RuleSetDocument Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "Fixtures", name);
        return JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json)!;
    }

    private static (string path, string code)[] IssuesOf(RuleSetDocument document) =>
        RuleSetSchemaValidator.Validate(document).Issues.Select(x => (x.Path, x.Code)).ToArray();

    [Fact]
    public void Valid_fixture_passes_the_schema_and_loads_every_rule()
    {
        var document = Fixture("valid-ruleset.json");

        Assert.True(RuleSetSchemaValidator.Validate(document).IsValid);

        var loaded = RuleSetDocumentMapper.FromDocument(document);
        Assert.True(loaded.IsSuccess);

        var set = loaded.Value;
        Assert.Equal("tw-bcr-fire", set.RuleSetId);
        Assert.Equal("2024.1", set.Version);
        Assert.Equal(3, set.Rules.Count);
        Assert.Single(set.OfCategory(RuleCategory.CompartmentArea));
        Assert.Single(set.OfCategory(RuleCategory.FireResistance));
        Assert.Single(set.OfCategory(RuleCategory.OpeningProtection));

        var area = set.Find(" tw-bcr-79-area ")!;
        Assert.Equal("1", area.Version);
        Assert.Equal("建築技術規則建築設計施工編第79條第1項", area.LegalReference);
        Assert.Equal(new DateTime(2024, 1, 1), area.EffectiveDate);
        Assert.Equal("TW", area.Jurisdiction);
        Assert.Equal(10, area.Priority);
        Assert.Equal("building.fireResistiveConstruction == true", area.AppliesWhen.Source);
        Assert.Single(area.Exemptions);
        Assert.Equal(new[] { "zone.area", "zone.sprinklered", "building.fireResistiveConstruction" }, area.EvidenceFields);
        Assert.Equal(RuleSeverity.Error, area.Severity);

        var door = set.Find("tw-bcr-76-door")!;
        Assert.Empty(door.Exemptions);
        Assert.Equal(RuleSeverity.Warning, door.Severity);
    }

    [Fact]
    public void Missing_required_fields_are_all_reported_at_once()
    {
        var issues = IssuesOf(Fixture("invalid-missing-fields.json"));

        Assert.Equal(
            new[]
            {
                ("ruleSetId", RuleSchemaIssueCode.Missing),
                ("rules[0].version", RuleSchemaIssueCode.Missing),
                ("rules[0].legalReference", RuleSchemaIssueCode.Missing),
                ("rules[0].appliesWhen", RuleSchemaIssueCode.Missing),
                ("rules[0].requiredValue", RuleSchemaIssueCode.Missing)
            },
            issues);
    }

    [Fact]
    public void Enumerations_must_be_spelled_by_their_exact_name()
    {
        // "compartmentArea" differs only in case and "1" is the numeric value of Warning; both are
        // what a lenient Enum.TryParse would accept, and both are what a rule file must not rely on.
        var issues = IssuesOf(Fixture("invalid-unknown-names.json"));

        Assert.Equal(
            new[]
            {
                ("rules[0].category", RuleSchemaIssueCode.UnknownValue),
                ("rules[0].severity", RuleSchemaIssueCode.UnknownValue)
            },
            issues);
    }

    [Fact]
    public void Bad_dates_negative_priority_blank_entries_and_duplicates_are_reported()
    {
        var issues = IssuesOf(Fixture("invalid-values.json"));

        Assert.Equal(
            new[]
            {
                ("rules[0].effectiveDate", RuleSchemaIssueCode.InvalidDate),
                ("rules[0].priority", RuleSchemaIssueCode.OutOfRange),
                ("rules[0].exemptions[0]", RuleSchemaIssueCode.Empty),
                ("rules[0].evidenceFields[1]", RuleSchemaIssueCode.Duplicate),
                ("rules[0].evidenceFields[2]", RuleSchemaIssueCode.Empty),
                ("rules[1].effectiveDate", RuleSchemaIssueCode.InvalidDate),
                ("rules[1].ruleId", RuleSchemaIssueCode.Duplicate)
            },
            issues);
    }

    [Fact]
    public void An_unsupported_schema_version_and_an_empty_rule_list_are_refused()
    {
        var issues = IssuesOf(Fixture("invalid-schema-version.json"));

        Assert.Equal(
            new[]
            {
                ("schemaVersion", RuleSchemaIssueCode.UnsupportedSchemaVersion),
                ("rules", RuleSchemaIssueCode.Empty)
            },
            issues);
    }

    [Theory]
    [InlineData("invalid-missing-fields.json", 5)]
    [InlineData("invalid-unknown-names.json", 2)]
    [InlineData("invalid-values.json", 7)]
    [InlineData("invalid-schema-version.json", 2)]
    public void Invalid_fixtures_fail_to_load_with_one_error_listing_every_issue(string fixture, int expectedIssues)
    {
        var loaded = RuleSetDocumentMapper.FromDocument(Fixture(fixture));

        Assert.True(loaded.IsFailure);
        Assert.Equal(ReviewErrorCode.RuleSchemaInvalid, loaded.Error.Code);
        Assert.Contains($"{expectedIssues} 處", loaded.Error.Message);
        Assert.Equal(expectedIssues, loaded.Error.TechnicalDetail!.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length);
    }

    [Fact]
    public void A_missing_document_or_rule_is_an_issue_not_an_exception()
    {
        Assert.Equal(new[] { ("$", RuleSchemaIssueCode.Missing) }, IssuesOf(null!));

        var document = Fixture("valid-ruleset.json");
        document.Rules[1] = null!;
        Assert.Equal(new[] { ("rules[1]", RuleSchemaIssueCode.Missing) }, IssuesOf(document));
    }

    [Fact]
    public void Missing_category_severity_and_date_read_as_missing_rather_than_unknown()
    {
        var document = Fixture("valid-ruleset.json");
        document.Rules[0].Category = "";
        document.Rules[0].Severity = " ";
        document.Rules[0].EffectiveDate = "";

        Assert.Equal(
            new[]
            {
                ("rules[0].category", RuleSchemaIssueCode.Missing),
                ("rules[0].severity", RuleSchemaIssueCode.Missing),
                ("rules[0].effectiveDate", RuleSchemaIssueCode.Missing)
            },
            IssuesOf(document));
    }

    [Fact]
    public void A_rule_file_with_an_unknown_property_does_not_deserialize()
    {
        const string text = "{\"schemaVersion\":\"1.0\",\"ruleSetId\":\"a\",\"version\":\"1\",\"rulez\":[]}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RuleSetDocument>(text, Json));
    }

    [Fact]
    public void Loaded_rule_set_survives_a_json_round_trip_unchanged()
    {
        var first = RuleSetDocumentMapper.FromDocument(Fixture("valid-ruleset.json")).Value;

        var json = JsonSerializer.Serialize(RuleSetDocumentMapper.ToDocument(first), Json);
        var second = RuleSetDocumentMapper.FromDocument(JsonSerializer.Deserialize<RuleSetDocument>(json, Json)).Value;

        Assert.Equal(first.RuleSetId, second.RuleSetId);
        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.Title, second.Title);
        Assert.Equal(first.Rules.Count, second.Rules.Count);
        foreach (var (a, b) in first.Rules.Zip(second.Rules, (a, b) => (a, b)))
        {
            Assert.Equal(a.RuleId, b.RuleId);
            Assert.Equal(a.Version, b.Version);
            Assert.Equal(a.Category, b.Category);
            Assert.Equal(a.LegalReference, b.LegalReference);
            Assert.Equal(a.EffectiveDate, b.EffectiveDate);
            Assert.Equal(a.Jurisdiction, b.Jurisdiction);
            Assert.Equal(a.Priority, b.Priority);
            Assert.Equal(a.AppliesWhen, b.AppliesWhen);
            Assert.Equal(a.RequiredValue, b.RequiredValue);
            Assert.Equal(a.Exemptions, b.Exemptions);
            Assert.Equal(a.EvidenceFields, b.EvidenceFields);
            Assert.Equal(a.Severity, b.Severity);
        }
        Assert.Contains("\"effectiveDate\":\"2024-01-01\"", json);
        Assert.Contains("\"category\":\"CompartmentArea\"", json);
    }
}
