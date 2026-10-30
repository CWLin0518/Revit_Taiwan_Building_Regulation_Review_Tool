using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Core.Tests.Candidates;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Parameters;

/// <summary>
/// 防火檢討_用途類組 as the review reads it (docs/tools/fire-review/rules/building-use-groups.md).
///
/// 第83條第一款、第二款 double their 區劃 area limit 「供建築物使用類組Ｈ–２組使用者」, and the rule
/// compares <c>zone.useGroup</c> against one literal. The code prints that group at least four ways,
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

        Assert.Contains($"zone.useGroup == \"{BuildingUseGroups.H2}\"", requirement, StringComparison.Ordinal);

        var atrium = Shipped().OfCategory(RuleCategory.CompartmentArea)
            .Single(x => x.RuleId == "tw-bcr-83-area-atrium").Rule.RequiredValue.Source;
        Assert.Contains($"zone.connectedUseGroup == \"{BuildingUseGroups.H2}\"", atrium, StringComparison.Ordinal);
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
        Assert.Equal("面積：防火檢討_用途類組（填「Ｈ–２組」，判讀為 H-2）", input.Source);
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
        Assert.Equal("面積：防火檢討_用途類組", input.Source);
    }

    /// <summary>Only 用途類組 is folded; every other text field is passed through as typed.</summary>
    [Fact]
    public void No_other_text_field_is_folded()
    {
        var field = RuleFieldCatalog.Default.Find("zone.use")!;
        var input = ReviewInputAssembler.Convert(field, ParameterReading.OfText("Ｈ－２組"), "面積：防火檢討_區劃用途")!;

        Assert.Equal(ReviewValue.OfText("Ｈ－２組"), input.Value);
        Assert.Equal("面積：防火檢討_區劃用途", input.Source);
    }

    // --- 一個區劃、多個面積（docs/tools/fire-review/rules/building-use-groups.md §6） ---------------------------

    [Fact]
    public void Areas_that_name_one_group_however_spelled_are_that_group()
    {
        var group = BuildingUseGroups.Combine(new[] { "H-2", "Ｈ－２組", " h2 " });

        Assert.Equal(ZoneUseGroupKind.Single, group.Kind);
        Assert.Equal("H-2", group.Value);
    }

    /// <summary>
    /// 「如同一空間有多種使用類組者，應同時符合各使用類組之規定」（內政部國土管理署115.05.15
    /// 國署建管字第1150047679號函）: the value names every group, in 第3-3條's order, and is no group itself.
    /// </summary>
    [Fact]
    public void Areas_that_name_several_groups_are_mixed()
    {
        var group = BuildingUseGroups.Combine(new[] { "H-2", "B-2", "Ｈ－２組", "辦公" });

        Assert.Equal(ZoneUseGroupKind.Mixed, group.Kind);
        Assert.Equal(new[] { "B-2", "H-2", "辦公" }, group.Groups);
        Assert.Equal("混合（B-2、H-2、辦公）", group.Value);
        Assert.True(BuildingUseGroups.IsMixed(group.Value));
        Assert.Null(BuildingUseGroups.Canonical(group.Value));
        Assert.False(BuildingUseGroups.IsH2(group.Value));
    }

    /// <summary>The groups of a part nobody filled in are unknown, so the 區劃's are too.</summary>
    // --- 面板：整個區劃讀作什麼 -------------------------------------------------------------------

    /// <summary>A 區劃 of one Area needs no zone note: the row's own reading is the 區劃's.</summary>
    [Fact]
    public void A_zone_of_one_area_has_no_zone_note()
    {
        Assert.Null(BuildingUseGroups.Combine(new[] { "H-2" }).PanelNote(1));
    }

    /// <summary>
    /// The case a row alone gets wrong: its own box reads H-2, but another Area of the 區劃 is blank,
    /// so the review reads the 區劃 as unknown.
    /// </summary>
    [Fact]
    public void A_zone_with_a_blank_area_says_the_review_will_find_data_missing()
    {
        var note = BuildingUseGroups.Combine(new[] { "H-2", null }).PanelNote(2);

        Assert.Equal("本區劃共 2 塊面積，其中 1 塊未填用途類組，檢討會判「資料不足」。", note);
    }

    [Fact]
    public void A_mixed_zone_says_the_proviso_does_not_apply()
    {
        var note = BuildingUseGroups.Combine(new[] { "H-2", "B-2" }).PanelNote(2);

        Assert.Equal("本區劃共 2 塊面積，整個區劃讀作 混合（B-2、H-2）：各類組之規定均應符合，不適用Ｈ－２組的放寬。", note);
    }

    [Fact]
    public void A_zone_of_one_group_says_so()
    {
        Assert.Equal("本區劃共 2 塊面積，整個區劃讀作 H-2。",
            BuildingUseGroups.Combine(new[] { "H-2", "Ｈ－２組" }).PanelNote(2));
        Assert.Null(BuildingUseGroups.Combine(new string?[] { null, null }).PanelNote(2));
    }

    [Fact]
    public void A_mixed_value_gives_its_groups_back()
    {
        Assert.Equal(new[] { "B-2", "H-2", "辦公" }, BuildingUseGroups.GroupsOf("混合（B-2、H-2、辦公）"));
        Assert.Empty(BuildingUseGroups.GroupsOf("H-2"));
        Assert.Empty(BuildingUseGroups.GroupsOf(null));
    }

    [Fact]
    public void Some_areas_left_blank_cannot_be_told()
    {
        var group = BuildingUseGroups.Combine(new[] { "H-2", null, "  " });

        Assert.Equal(ZoneUseGroupKind.Partial, group.Kind);
        Assert.Null(group.Value);
        Assert.Contains("3 個面積中有 2 個未填", group.Problem);
    }

    [Fact]
    public void No_area_filled_in_is_nothing_supplied()
    {
        Assert.Equal(ZoneUseGroupKind.Blank, BuildingUseGroups.Combine(new string?[] { null, "" }).Kind);
        Assert.Equal(ZoneUseGroupKind.Blank, BuildingUseGroups.Combine(Array.Empty<string?>()).Kind);
    }

    /// <summary>A 區劃 that is partly Ｈ－２組 does not get the Ｈ－２組 limit — in the rule and the panel alike.</summary>
    [Fact]
    public void A_mixed_zone_gets_the_stricter_limit()
    {
        var mixed = BuildingUseGroups.Mixed(new[] { "B-2", "H-2" });

        var outcome = Outcome(mixed, areaSquareMeters: 150);

        Assert.Equal(ReviewValue.Quantity(100, ReviewUnit.SquareMeter), outcome.RequiredValue);
        Assert.Equal(ReviewStatus.Fail, outcome.Status);
        Assert.Equal(100, ZoneAreaLimit.For(11, false, InteriorFinishGrades.None, mixed).SquareMeters);
    }

    [Fact]
    public void A_two_area_zone_of_one_group_reads_as_that_group()
    {
        var input = ZoneOfTwo("H-2", "Ｈ類第二組");

        Assert.Equal(ReviewValue.OfText("H-2"), input!.Value);
        Assert.Equal("面積：防火檢討_用途類組（填「Ｈ類第二組」，判讀為 H-2）", input.Source);
    }

    /// <summary>
    /// Unlike every other 區劃 parameter, Areas that disagree are no gap: the 區劃 is mixed, and the
    /// evidence says what each Area holds.
    /// </summary>
    [Fact]
    public void A_two_area_zone_of_two_groups_reads_as_mixed_and_says_why()
    {
        var input = ZoneOfTwo("H-2", "B-2");

        Assert.False(input!.IsUnreadable);
        Assert.Equal(ReviewValue.OfText("混合（B-2、H-2）"), input.Value);
        Assert.Contains("各面積分別填 「H-2」、「B-2」", input.Source);
        Assert.Contains("各類組之規定均應符合", input.Source);
    }

    [Fact]
    public void A_two_area_zone_with_one_part_blank_is_unreadable()
    {
        var input = ZoneOfTwo("H-2", null);

        Assert.True(input!.IsUnreadable);
        Assert.Contains("未填用途類組", input.UnreadableReason);
    }

    [Fact]
    public void A_two_area_zone_left_blank_supplies_nothing()
    {
        Assert.Null(ZoneOfTwo(null, null));
    }

    /// <summary>
    /// The old Project Information value is no source at all: a 區劃 nobody filled in must not borrow
    /// the building-wide group (and with it 第83條's Ｈ－２組 limit).
    /// </summary>
    [Fact]
    public void The_old_project_value_never_stands_in_for_a_zone()
    {
        var project = new Dictionary<string, ParameterReading>
        {
            [ReviewInputSources.LegacyBuildingUse] = ParameterReading.OfText("H-2")
        };
        var snapshot = new ReviewParameterSnapshot(null, project, AreasHolding(null, null));

        var assembly = ReviewInputAssembler.Assemble(TwoAreaZone(), snapshot);

        Assert.Empty(assembly.Area.ForZone(CandidateModel.ZoneA));
        Assert.DoesNotContain(assembly.Area.Building, i => i.Field.Contains("use", StringComparison.OrdinalIgnoreCase));
    }

    private static ReviewInput? ZoneOfTwo(string? first, string? second)
    {
        var snapshot = new ReviewParameterSnapshot(null, null, AreasHolding(first, second));
        return ReviewInputAssembler.Assemble(TwoAreaZone(), snapshot).Area.ForZone(CandidateModel.ZoneA)
            .SingleOrDefault(i => i.Field == "zone.useGroup");
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ParameterReading>> AreasHolding(string? first, string? second) =>
        new Dictionary<string, IReadOnlyDictionary<string, ParameterReading>>
        {
            ["area-1"] = new Dictionary<string, ParameterReading> { [ReviewInputSources.UseGroup] = ParameterReading.OfText(first) },
            ["area-2"] = new Dictionary<string, ParameterReading> { [ReviewInputSources.UseGroup] = ParameterReading.OfText(second) }
        };

    /// <summary>One 區劃 drawn as two Areas side by side.</summary>
    private static CandidateSet TwoAreaZone() =>
        CandidateResolver.Resolve(new CandidateObservationSet(CandidateModel.PackageId, "level-1F", "1F",
            new[]
            {
                new ZoneObservation(CandidateModel.ZoneA, "A 區", new[]
                {
                    new ZonePartObservation("area-1", new[] { CandidateModel.Rect(0, 0, 10, 10) }, PlanUnits.SquareMetersToSquareFeet(100)),
                    new ZonePartObservation("area-2", new[] { CandidateModel.Rect(10, 0, 20, 10) }, PlanUnits.SquareMetersToSquareFeet(100))
                })
            },
            Array.Empty<MemberObservation>(), Array.Empty<OpeningObservation>()));

    private static ReviewInput? Assemble(string text) => ReviewInputAssembler.Convert(
        RuleFieldCatalog.Default.Find("zone.useGroup")!, ParameterReading.OfText(text), "面積：防火檢討_用途類組");

    /// <summary>
    /// A 十一層 zone whose 用途類組 went through the assembler, so the rule sees exactly what a real
    /// review would put in front of it.
    /// </summary>
    private static RuleOutcome Outcome(string useGroup, double areaSquareMeters)
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

        Assemble(useGroup)!.ApplyTo(facts);
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
