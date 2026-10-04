using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Parameters;

/// <summary>
/// 建築物用途類組 as the review reads it (docs/regulations/building-use-groups.md).
///
/// 第83條第一款、第二款 double their 區劃 area limit 「供建築物使用類組Ｈ–２組使用者」, and the rule
/// compares <c>building.use</c> against one literal. The code prints that group at least four ways,
/// so the spelling is canonicalised where a parameter becomes a rule input. What these tests guard
/// is that the widening is exactly that — a spelling question — and never lets text that names no
/// group be read as one.
/// </summary>
public sealed class BuildingUseGroupsTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private static readonly RuleEvaluationContext Today = new(new DateTime(2026, 9, 25), "TW");

    /// <summary>
    /// Every spelling of Ｈ－２組 this tool accepts. The first four are the code's own, article by
    /// article; the rest are what a user types.
    /// </summary>
    public static TheoryData<string> H2Spellings => new()
    {
        "Ｈ–２組",      // 第83條 — full-width letter and digit, en dash
        "H-2 組",       // 第86條
        "H-2",          // 第88條's table
        "Ｈ類第二組",   // 第310條
        "Ｈ－２",       // full-width hyphen-minus, no 組
        "H-2組",
        "H2",
        "h-2",
        "  H-2  "
    };

    // --- 第3-3條的組別清單 ----------------------------------------------------------------------

    /// <summary>
    /// The list is the codes as the review compares them, so every entry has to be its own canonical
    /// form — otherwise the panel would offer a value the rules do not recognise.
    /// </summary>
    [Fact]
    public void Every_group_the_panel_offers_is_already_its_canonical_form()
    {
        Assert.Equal(24, BuildingUseGroups.All.Count);
        Assert.Equal(BuildingUseGroups.All, BuildingUseGroups.All.Select(BuildingUseGroups.Canonical));
        Assert.All(BuildingUseGroups.All, code => Assert.False(BuildingUseGroups.IsRespelled(code)));
    }

    /// <summary>Ｅ類 and Ｉ類 have no group number, so the bare letter is the code.</summary>
    [Fact]
    public void The_two_classes_with_no_group_are_the_bare_letter()
    {
        Assert.Equal("E", BuildingUseGroups.Canonical("Ｅ類"));
        Assert.Equal("I", BuildingUseGroups.Canonical("Ｉ類"));
        Assert.DoesNotContain(BuildingUseGroups.All, code => code is "E-1" or "I-1");
    }

    // --- 同一組的各種寫法 -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(H2Spellings))]
    public void Every_spelling_the_code_itself_prints_names_the_same_group(string spelling)
    {
        Assert.Equal(BuildingUseGroups.H2, BuildingUseGroups.Canonical(spelling));
        Assert.True(BuildingUseGroups.IsH2(spelling));
    }

    /// <summary>The same folding works for the other classes, which the code spells just as loosely.</summary>
    [Theory]
    [InlineData("Ｄ－３", "D-3")]     // 第79-1條
    [InlineData("Ｆ–１組", "F-1")]    // 第92條
    [InlineData("Ｂ－２組", "B-2")]   // 第88條's table
    [InlineData("g-2", "G-2")]
    [InlineData("A1", "A-1")]
    public void A_group_of_any_class_is_read_the_same_way(string spelling, string code) =>
        Assert.Equal(code, BuildingUseGroups.Canonical(spelling));

    // --- 不是組別的文字 -------------------------------------------------------------------------

    /// <summary>
    /// The widening is a spelling question and nothing more. Text that names no single group is left
    /// exactly as it was, so it is compared literally — the behaviour before any of this existed.
    /// </summary>
    [Theory]
    [InlineData("辦公")]
    [InlineData("住宿類")]
    [InlineData("H")]          // a class is not a group
    [InlineData("H-9")]        // Ｈ類 has no third group
    [InlineData("J-1")]        // there is no Ｊ類
    [InlineData("H-2、G-2")]   // two groups name no one group
    [InlineData("H-2組集合住宅")]
    public void Text_that_names_no_group_is_left_exactly_as_it_was(string text)
    {
        Assert.Null(BuildingUseGroups.Canonical(text));
        Assert.False(BuildingUseGroups.IsKnown(text));
        Assert.False(BuildingUseGroups.IsH2(text));
        Assert.False(BuildingUseGroups.IsRespelled(text));
        Assert.Equal(text, BuildingUseGroups.Normalize(text));
    }

    [Fact]
    public void Nothing_supplied_stays_nothing()
    {
        Assert.Null(BuildingUseGroups.Canonical(null));
        Assert.Null(BuildingUseGroups.Canonical("   "));
        Assert.Null(BuildingUseGroups.Normalize(null));
        Assert.Null(BuildingUseGroups.Normalize("   "));
    }

    /// <summary>Ｈ－２組 is the only group any rule reads; nothing else may answer to it.</summary>
    [Fact]
    public void Only_the_h2_group_satisfies_the_proviso()
    {
        Assert.Equal(new[] { BuildingUseGroups.H2 }, BuildingUseGroups.All.Where(BuildingUseGroups.IsH2));
    }

    /// <summary>
    /// The constant and the shipped rule have to name the same literal — the rule is the authority,
    /// and a canonical form that did not match it would widen nothing.
    /// </summary>
    [Fact]
    public void The_canonical_form_is_the_literal_the_shipped_rule_compares()
    {
        var requirement = Shipped().OfCategory(RuleCategory.CompartmentArea)
            .Single(x => x.RuleId == "tw-bcr-83-area").Rule.RequiredValue.Source;

        Assert.Contains($"building.use == \"{BuildingUseGroups.H2}\"", requirement, StringComparison.Ordinal);
    }

    // --- 從參數到規則：兩邊讀出同一個上限 -------------------------------------------------------

    /// <summary>
    /// The point of the whole exercise: a project that spells the group any of these ways gets
    /// 第一款's 二○○平方公尺, in the review and in the panel alike. Before this, only the exact
    /// 「H-2」 did, and everything else quietly fell back to 一○○.
    /// </summary>
    [Theory]
    [MemberData(nameof(H2Spellings))]
    public void A_project_that_spells_the_group_any_way_gets_the_wider_limit(string spelling)
    {
        var outcome = Outcome(spelling, areaSquareMeters: 150);

        Assert.Equal("tw-bcr-83-area", outcome.RuleId);
        Assert.Equal(ReviewValue.Quantity(200, ReviewUnit.SquareMeter), outcome.RequiredValue);
        Assert.Equal(ReviewStatus.Pass, outcome.Status);

        var shown = ZoneAreaLimit.For(11, sprinklered: false, InteriorFinishGrades.None, spelling);
        Assert.Equal(200, shown.SquareMeters);
    }

    /// <summary>
    /// And the safe direction is untouched: a 用途類組 that names no group is still not Ｈ－２組, so
    /// the same 一五○平方公尺 區劃 is over 第一款's 一○○.
    /// </summary>
    [Fact]
    public void A_use_group_that_names_no_group_still_gets_the_stricter_limit()
    {
        var outcome = Outcome("辦公", areaSquareMeters: 150);

        Assert.Equal(ReviewValue.Quantity(100, ReviewUnit.SquareMeter), outcome.RequiredValue);
        Assert.Equal(ReviewStatus.Fail, outcome.Status);
        Assert.Equal(100, ZoneAreaLimit.For(11, false, InteriorFinishGrades.None, "辦公").SquareMeters);
    }

    // --- 證據：改寫不是默默改寫 -----------------------------------------------------------------

    /// <summary>
    /// A respelling is recorded, not hidden: the evidence names the text the model carries as well as
    /// the group it was read as, so a reviewer can see why the wider limit applied.
    /// </summary>
    [Fact]
    public void The_evidence_records_what_was_typed_beside_what_it_was_read_as()
    {
        var input = Assemble("Ｈ–２組")!;

        Assert.Equal(ReviewValue.OfText(BuildingUseGroups.H2), input.Value);
        Assert.Equal("專案資訊：建築物用途類組（填「Ｈ–２組」，判讀為 H-2）", input.Source);
    }

    /// <summary>
    /// A project that already spells the group the canonical way reads exactly as it did before —
    /// same value, same source — so its stored run's evidence baseline does not move under it
    /// (規格 13.1).
    /// </summary>
    [Theory]
    [InlineData("H-2")]
    [InlineData("辦公")]
    public void A_text_that_needed_no_respelling_reads_exactly_as_it_did_before(string text)
    {
        var input = Assemble(text)!;

        Assert.Equal(ReviewValue.OfText(text), input.Value);
        Assert.Equal("專案資訊：建築物用途類組", input.Source);
    }

    /// <summary>Only 建築物用途類組 is folded; every other text field is passed through as typed.</summary>
    [Fact]
    public void No_other_text_field_is_folded()
    {
        var field = RuleFieldCatalog.Default.Find("zone.use")!;
        var input = ReviewInputAssembler.Convert(field, ParameterReading.OfText("Ｈ－２組"), "面積：防火檢討_區劃用途")!;

        Assert.Equal(ReviewValue.OfText("Ｈ－２組"), input.Value);
        Assert.Equal("面積：防火檢討_區劃用途", input.Source);
    }

    private static ReviewInput? Assemble(string text) => ReviewInputAssembler.Convert(
        RuleFieldCatalog.Default.Find("building.use")!, ParameterReading.OfText(text), "專案資訊：建築物用途類組");

    /// <summary>
    /// A 十一層 zone whose 用途類組 went through the assembler, so the rule sees exactly what a real
    /// review would put in front of it.
    /// </summary>
    private static RuleOutcome Outcome(string buildingUse, double areaSquareMeters)
    {
        var facts = new RuleFacts(RuleFieldCatalog.Default)
            .Set("building.fireResistiveConstruction", true)
            .Set("building.floorsAboveGround", 20, ReviewUnit.None)
            .Set("zone.id", "zone-1")
            .Set("zone.use", "辦公")
            .Set("zone.floorNumber", 11, ReviewUnit.None)
            .Set("zone.area", areaSquareMeters, ReviewUnit.SquareMeter)
            .Set("zone.sprinklered", false)
            .Set("zone.interiorFinish", InteriorFinishGrades.None)
            // 檢查層對每個不是「第3項免除成立之挑空」的區劃都設這個值（決議 32）。
            .Set("zone.atriumMerged", false);

        Assemble(buildingUse)!.ApplyTo(facts);
        return new RuleEngine(Shipped()).Evaluate(RuleCategory.CompartmentArea, facts, Today);
    }

    private static CompiledRuleSet Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return loaded.Value;
    }
}
