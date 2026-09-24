using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Rules;

/// <summary>
/// The shipped 第83條 區劃面積 rule — 十一層以上部分之區劃
/// (docs/regulations/curtain-wall-fire-compartment.md §2.5、§5.3).
///
/// 第83條 states three tiers of 區劃 area and reads them off the 室內裝修 grade: 一○○平方公尺 as the
/// baseline (二○○ for Ｈ－２組), 二○○ once 自地板面起一‧二公尺以上之室內牆面及天花板 are 耐燃一級
/// (四○○ for Ｈ－２組), and 五○○ once 牆面及天花板（包括底材）are all 耐燃一級. 第四款 lets an
/// effectively sprinklered 區劃 count half its area, which the rule writes as twice the limit — the
/// same shape <c>tw-bcr-79-area</c> already uses.
///
/// The rule sits at priority 20, above 第79條's 一、五○○平方公尺 at priority 10, because its limit is
/// stricter at every tier (五○○ doubled is still 一、○○○). So above the tenth storey 第83條 decides,
/// and below it 第83條 simply does not apply and 第79條 decides — the same two-tier arrangement
/// 第70條 and 第79條 already use for 防火時效.
/// </summary>
public sealed class Article83AreaRuleTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private static readonly RuleEvaluationContext Today = new(new DateTime(2026, 9, 25), "TW");

    private const string Article83 = "第83條";

    private static CompiledRuleSet Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return loaded.Value;
    }

    /// <summary>A zone on the given storey, with every input the two area rules read except 裝修等級.</summary>
    private static RuleFacts Zone(
        int floorNumber,
        double areaSquareMeters,
        bool sprinklered = false,
        string buildingUse = "B-2",
        string zoneUse = "辦公") =>
        new RuleFacts(RuleFieldCatalog.Default)
            .Set("building.fireResistiveConstruction", true)
            .Set("building.use", buildingUse)
            .Set("building.floorsAboveGround", 15, ReviewUnit.None)
            .Set("zone.id", "zone-1")
            .Set("zone.use", zoneUse)
            .Set("zone.floorNumber", floorNumber, ReviewUnit.None)
            .Set("zone.area", areaSquareMeters, ReviewUnit.SquareMeter)
            .Set("zone.sprinklered", sprinklered);

    private static string Reference(CompiledRuleSet set, string ruleId) =>
        set.Rules.Single(x => string.Equals(x.RuleId, ruleId, StringComparison.Ordinal)).Rule.LegalReference;

    private static RuleOutcome Outcome(RuleFacts facts) =>
        new RuleEngine(Shipped()).Evaluate(RuleCategory.CompartmentArea, facts, Today);

    private static ReviewStatus Status(RuleFacts facts) => Outcome(facts).Status;

    // --- the rule file itself -------------------------------------------------------------------

    [Fact]
    public void Shipped_rule_set_compiles_both_area_rules_with_the_stricter_one_on_top()
    {
        var rules = Shipped().OfCategory(RuleCategory.CompartmentArea)
            .OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();

        Assert.Equal(new[] { "tw-bcr-79-area", "tw-bcr-83-area" }, rules.Select(x => x.RuleId));
        Assert.Equal(10, rules[0].Priority);
        Assert.Equal(20, rules[1].Priority);
    }

    /// <summary>
    /// What makes a 區劃 第83條's is the clause the 區劃面積 rule decided it under, and
    /// <c>FireReviewRunner</c> reads that off the result's 法源條文 as plain text (帷幕牆規格 §2.5).
    /// So 第83條 has to appear in this rule's 法源條文 and nowhere else a 區劃 result can borrow it
    /// from — not in 第79條's, and not in the rule set's title, which is what a withheld 區劃 and a
    /// category with no rule at all report instead.
    /// </summary>
    [Fact]
    public void Only_the_article_83_rule_names_article_83_as_its_legal_reference()
    {
        var set = Shipped();

        Assert.Contains(Article83, Reference(set, "tw-bcr-83-area"));
        Assert.DoesNotContain(Article83, Reference(set, "tw-bcr-79-area"));
        Assert.DoesNotContain(Article83, set.RuleSet.Title);
    }

    // --- 誰決定：第79條 還是第83條 ---------------------------------------------------------------

    [Fact]
    public void Below_the_eleventh_storey_article_79_still_decides()
    {
        var outcome = Outcome(Zone(10, 1200).Set("zone.interiorFinish", "無"));

        Assert.Equal("tw-bcr-79-area", outcome.RuleId);
        Assert.Equal(ReviewStatus.Pass, outcome.Status);
    }

    [Fact]
    public void From_the_eleventh_storey_up_article_83_decides()
    {
        var outcome = Outcome(Zone(11, 90).Set("zone.interiorFinish", "無"));

        Assert.Equal("tw-bcr-83-area", outcome.RuleId);
        Assert.Equal(ReviewStatus.Pass, outcome.Status);
        Assert.Equal(ReviewValue.Quantity(100, ReviewUnit.SquareMeter), outcome.RequiredValue);
    }

    /// <summary>
    /// The point of the priority: a 一、二○○平方公尺 區劃 on the twelfth storey is well within
    /// 第79條's 一、五○○, and it still fails — 第79條 never gets to answer for it.
    /// </summary>
    [Fact]
    public void The_lenient_article_79_limit_cannot_rescue_a_zone_above_the_tenth_storey()
    {
        var outcome = Outcome(Zone(12, 1200).Set("zone.interiorFinish", "無"));

        Assert.Equal("tw-bcr-83-area", outcome.RuleId);
        Assert.Equal(ReviewStatus.Fail, outcome.Status);
    }

    [Fact]
    public void Neither_rule_applies_to_a_building_that_is_not_fire_resistive()
    {
        var facts = Zone(12, 90).Set("building.fireResistiveConstruction", false).Set("zone.interiorFinish", "無");

        Assert.Equal(ReviewStatus.NotApplicable, Status(facts));
    }

    // --- 第一款至第三款的三段上限 ---------------------------------------------------------------

    [Theory]
    [InlineData("無", 100)]
    [InlineData("耐燃一級", 200)]
    [InlineData("耐燃一級含底材", 500)]
    public void Each_finish_grade_sets_its_own_limit(string finish, double limit)
    {
        var inside = Outcome(Zone(11, limit).Set("zone.interiorFinish", finish));
        var outside = Outcome(Zone(11, limit + 1).Set("zone.interiorFinish", finish));

        Assert.Equal(ReviewValue.Quantity(limit, ReviewUnit.SquareMeter), inside.RequiredValue);
        Assert.Equal(ReviewStatus.Pass, inside.Status);
        Assert.Equal(ReviewStatus.Fail, outside.Status);
    }

    /// <summary>A grade the rule does not know is not a 放寬: it falls back to 第一款's 一○○平方公尺.</summary>
    [Fact]
    public void An_unrecognised_finish_grade_falls_back_to_the_baseline_limit()
    {
        var outcome = Outcome(Zone(11, 150).Set("zone.interiorFinish", "耐燃二級"));

        Assert.Equal(ReviewValue.Quantity(100, ReviewUnit.SquareMeter), outcome.RequiredValue);
        Assert.Equal(ReviewStatus.Fail, outcome.Status);
    }

    // --- 第一款、第二款的 Ｈ－２組但書 -----------------------------------------------------------

    [Theory]
    [InlineData("無", 200)]
    [InlineData("耐燃一級", 400)]
    public void Use_group_h2_doubles_the_first_two_tiers(string finish, double limit)
    {
        var outcome = Outcome(Zone(11, limit, buildingUse: "H-2").Set("zone.interiorFinish", finish));

        Assert.Equal(ReviewValue.Quantity(limit, ReviewUnit.SquareMeter), outcome.RequiredValue);
        Assert.Equal(ReviewStatus.Pass, outcome.Status);
    }

    /// <summary>第三款 names no use group, so Ｈ－２組 gets no more than the 五○○平方公尺 anyone gets.</summary>
    [Fact]
    public void Use_group_h2_gets_no_relaxation_of_the_third_tier()
    {
        var outcome = Outcome(Zone(11, 520, buildingUse: "H-2").Set("zone.interiorFinish", "耐燃一級含底材"));

        Assert.Equal(ReviewValue.Quantity(500, ReviewUnit.SquareMeter), outcome.RequiredValue);
        Assert.Equal(ReviewStatus.Fail, outcome.Status);
    }

    // --- 第四款：有效自動滅火設備 ---------------------------------------------------------------

    /// <summary>
    /// 第四款 lets a sprinklered 區劃 leave half of its area out of the count, which is the same
    /// comparison as twice the limit — written that way because the rule DSL compares Revit's Area
    /// as it stands (規格 11.4 步驟 2) and never rewrites the actual value.
    /// </summary>
    [Theory]
    [InlineData("無", 200)]
    [InlineData("耐燃一級", 400)]
    [InlineData("耐燃一級含底材", 1000)]
    public void An_effective_sprinkler_system_doubles_every_tier(string finish, double limit)
    {
        var outcome = Outcome(Zone(11, limit, sprinklered: true).Set("zone.interiorFinish", finish));

        Assert.Equal(ReviewValue.Quantity(limit, ReviewUnit.SquareMeter), outcome.RequiredValue);
        Assert.Equal(ReviewStatus.Pass, outcome.Status);
    }

    /// <summary>
    /// Even the widest 第83條 limit stays under 第79條's 一、五○○平方公尺, which is why putting
    /// 第83條 above it can never let a 區劃 through that 第79條 would have stopped.
    /// </summary>
    [Fact]
    public void The_widest_article_83_limit_is_still_stricter_than_article_79()
    {
        var outcome = Outcome(Zone(11, 1400, sprinklered: true).Set("zone.interiorFinish", "耐燃一級含底材"));

        Assert.Equal(ReviewStatus.Fail, outcome.Status);
    }

    // --- 資料不足與豁免 -------------------------------------------------------------------------

    /// <summary>
    /// 第83條's 放寬 is a permission the design has to earn, so a 區劃 whose 裝修等級 nobody filled in
    /// is 資料不足 — not a quiet pass on 五○○, and not a Fail on 一○○ either (規格 11.3). This is the
    /// same answer 第79條 gives a 區劃 with no 灑水 value.
    /// </summary>
    [Fact]
    public void A_zone_with_no_finish_grade_is_insufficient_data_and_never_a_fail()
    {
        var wide = Outcome(Zone(11, 1000));
        var narrow = Outcome(Zone(11, 10));

        Assert.Equal(ReviewStatus.InsufficientData, wide.Status);
        Assert.Equal("tw-bcr-83-area", wide.RuleId);
        Assert.Contains("zone.interiorFinish", string.Join("、", wide.Gaps));
        Assert.Equal(ReviewStatus.InsufficientData, narrow.Status);
    }

    /// <summary>
    /// 第83條 excludes the 垂直區劃 第79條之2 governs, so a 樓梯間、昇降機道、管道間 or 挑空 區劃 is
    /// exempt here and is reviewed by that article instead (not in this version).
    /// </summary>
    [Theory]
    [InlineData("樓梯間")]
    [InlineData("昇降機道")]
    [InlineData("管道間")]
    [InlineData("挑空")]
    public void The_vertical_compartments_of_article_79_2_are_exempt(string use)
    {
        var facts = Zone(11, 900, zoneUse: use).Set("zone.interiorFinish", "無");

        Assert.Equal(ReviewStatus.NotApplicable, Status(facts));
    }

    /// <summary>An exemption is decided before the limit, so it holds even with no 裝修等級 at all.</summary>
    [Fact]
    public void A_vertical_compartment_is_exempt_before_the_finish_grade_is_needed()
    {
        Assert.Equal(ReviewStatus.NotApplicable, Status(Zone(11, 900, zoneUse: "管道間")));
    }

    /// <summary>
    /// An over-limit 區劃 whose 用途 nobody filled in is 資料不足 as well: the exemption might have
    /// held. Fail is reserved for a 區劃 that is over the limit and demonstrably not exempt — the
    /// same reading <c>tw-bcr-79-area</c> already has of its own 樓梯間 exemption.
    /// </summary>
    [Fact]
    public void An_over_limit_zone_with_no_use_is_insufficient_data_rather_than_a_fail()
    {
        var facts = new RuleFacts(RuleFieldCatalog.Default)
            .Set("building.fireResistiveConstruction", true)
            .Set("building.use", "B-2")
            .Set("zone.floorNumber", 11, ReviewUnit.None)
            .Set("zone.area", 900, ReviewUnit.SquareMeter)
            .Set("zone.sprinklered", false)
            .Set("zone.interiorFinish", "無");

        var outcome = Outcome(facts);

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
        Assert.Equal("tw-bcr-83-area", outcome.RuleId);
    }
}
