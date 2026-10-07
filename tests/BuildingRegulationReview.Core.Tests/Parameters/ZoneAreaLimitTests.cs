using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Parameters;

/// <summary>
/// The 適用上限 the batch panel shows beside each 區劃, before anything is written or reviewed.
///
/// The shipped rules are the authority; this type only restates them so the consequence of each box
/// is visible while it is being filled. So most of what follows evaluates
/// <c>tw-bcr-79-area</c>／<c>tw-bcr-83-area</c> for the same inputs and asserts the two agree — both
/// on the number, and on which field being blank makes the answer 資料不足.
/// </summary>
public sealed class ZoneAreaLimitTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private static readonly RuleEvaluationContext Today = new(new DateTime(2026, 9, 25), "TW");

    // --- 第79條：十層以下 -----------------------------------------------------------------------

    [Theory]
    [InlineData(true, 3000)]
    [InlineData(false, 1500)]
    public void Below_the_eleventh_storey_the_panel_shows_the_article_79_limit(bool sprinklered, double limit)
    {
        var shown = ZoneAreaLimit.For(9, sprinklered, interiorFinish: null, buildingUse: null);

        Assert.Equal(limit, shown.SquareMeters);
        Assert.Equal("第79條", shown.Clause);
        Assert.True(shown.IsKnown);
    }

    /// <summary>第79條 never reads 裝修等級, so leaving it blank must not hold the limit back.</summary>
    [Fact]
    public void Below_the_eleventh_storey_a_blank_finish_grade_changes_nothing()
    {
        Assert.Equal(
            ZoneAreaLimit.For(9, true, InteriorFinishGrades.ClassOneWithSubstrate, "H-2"),
            ZoneAreaLimit.For(9, true, null, null));
    }

    // --- 第83條：十一層以上的三段上限 -----------------------------------------------------------

    [Theory]
    [InlineData(InteriorFinishGrades.None, 100)]
    [InlineData(InteriorFinishGrades.ClassOne, 200)]
    [InlineData(InteriorFinishGrades.ClassOneWithSubstrate, 500)]
    public void From_the_eleventh_storey_up_each_grade_shows_its_own_limit(string finish, double limit)
    {
        var shown = ZoneAreaLimit.For(11, false, finish, "B-2");

        Assert.Equal(limit, shown.SquareMeters);
        Assert.Equal("第83條", shown.Clause);
        Assert.False(shown.FinishUnrecognised);
    }

    [Theory]
    [InlineData(InteriorFinishGrades.None, 200)]
    [InlineData(InteriorFinishGrades.ClassOne, 400)]
    [InlineData(InteriorFinishGrades.ClassOneWithSubstrate, 500)]
    public void Use_group_h2_widens_only_the_first_two_tiers(string finish, double limit)
    {
        Assert.Equal(limit, ZoneAreaLimit.For(11, false, finish, "H-2").SquareMeters);
    }

    [Theory]
    [InlineData(InteriorFinishGrades.None, 200)]
    [InlineData(InteriorFinishGrades.ClassOne, 400)]
    [InlineData(InteriorFinishGrades.ClassOneWithSubstrate, 1000)]
    public void A_sprinklered_zone_doubles_every_tier(string finish, double limit)
    {
        Assert.Equal(limit, ZoneAreaLimit.For(11, true, finish, "B-2").SquareMeters);
    }

    /// <summary>
    /// A grade the rule does not know is a 放寬 that was not earned, not 資料不足: the limit falls
    /// back to 第一款 and the panel says so rather than showing 五○○ or a blank.
    /// </summary>
    [Fact]
    public void An_unrecognised_grade_shows_the_baseline_limit_and_says_why()
    {
        var shown = ZoneAreaLimit.For(11, false, "耐燃二級", "B-2");

        Assert.Equal(100, shown.SquareMeters);
        Assert.True(shown.FinishUnrecognised);
        Assert.Contains("非放寬條件", shown.Description);
    }

    // --- 未填的欄位 -----------------------------------------------------------------------------

    /// <summary>樓層序 is what picks the article, so nothing else can be said until it is filled.</summary>
    [Fact]
    public void With_no_storey_the_panel_names_only_the_storey()
    {
        var shown = ZoneAreaLimit.For(null, null, null, null);

        Assert.Equal(ZoneAreaLimitGap.FloorNumber, shown.Gaps);
        Assert.Null(shown.SquareMeters);
        Assert.Equal("未填樓層序，無法判定上限", shown.Description);
    }

    [Fact]
    public void An_article_79_zone_waits_only_on_the_sprinklers()
    {
        var shown = ZoneAreaLimit.For(9, null, null, null);

        Assert.Equal(ZoneAreaLimitGap.Sprinklered, shown.Gaps);
        Assert.Equal("未填滅火設備，無法判定上限", shown.Description);
    }

    [Fact]
    public void An_article_83_zone_names_every_box_it_is_still_waiting_on()
    {
        var shown = ZoneAreaLimit.For(11, null, null, null);

        Assert.Equal(
            ZoneAreaLimitGap.InteriorFinish | ZoneAreaLimitGap.BuildingUse | ZoneAreaLimitGap.Sprinklered,
            shown.Gaps);
        Assert.Equal("未填模型牆面／天花板耐燃等級、用途類組、滅火設備，無法判定上限", shown.Description);
    }

    /// <summary>
    /// 第三款 names no use group, so the rule's Ｈ－２組 branch is never reached for it — and the
    /// panel does not ask for 用途類組 the review would not read either.
    /// </summary>
    [Fact]
    public void The_third_tier_does_not_wait_on_the_use_group()
    {
        var shown = ZoneAreaLimit.For(11, false, InteriorFinishGrades.ClassOneWithSubstrate, null);

        Assert.True(shown.IsKnown);
        Assert.Equal(500, shown.SquareMeters);
    }

    // --- 與出貨規則一致 -------------------------------------------------------------------------

    /// <summary>
    /// The panel's number is the rule's number. Anything else would have the panel promise a limit
    /// the review then judges against a different one, which is the one failure mode restating the
    /// rules in C# can have.
    /// </summary>
    [Theory]
    [InlineData(9, false, null, "B-2")]
    [InlineData(9, true, null, "B-2")]
    [InlineData(11, false, InteriorFinishGrades.None, "B-2")]
    [InlineData(11, false, InteriorFinishGrades.ClassOne, "B-2")]
    [InlineData(11, false, InteriorFinishGrades.ClassOneWithSubstrate, "B-2")]
    [InlineData(11, true, InteriorFinishGrades.None, "B-2")]
    [InlineData(11, true, InteriorFinishGrades.ClassOne, "B-2")]
    [InlineData(11, true, InteriorFinishGrades.ClassOneWithSubstrate, "B-2")]
    [InlineData(11, false, InteriorFinishGrades.None, "H-2")]
    [InlineData(11, false, InteriorFinishGrades.ClassOne, "H-2")]
    [InlineData(11, false, InteriorFinishGrades.ClassOneWithSubstrate, "H-2")]
    [InlineData(20, true, "耐燃二級", "H-2")]
    public void The_shown_limit_is_the_limit_the_shipped_rules_require(
        int floorNumber, bool sprinklered, string? finish, string? buildingUse)
    {
        var shown = ZoneAreaLimit.For(floorNumber, sprinklered, finish, buildingUse);
        var required = Outcome(Facts(floorNumber, sprinklered, finish, buildingUse)).RequiredValue;

        Assert.Equal(ReviewValue.Quantity(shown.SquareMeters!.Value, ReviewUnit.SquareMeter), required);
    }

    /// <summary>
    /// And what the panel calls 未填 is what the engine calls 資料不足 — the same fields, so the
    /// panel cannot report a zone as ready and then have the review withhold an answer for it.
    /// </summary>
    [Theory]
    [InlineData(9, null, InteriorFinishGrades.None, "B-2")]
    [InlineData(11, null, InteriorFinishGrades.None, "B-2")]
    [InlineData(11, false, null, "B-2")]
    [InlineData(11, false, InteriorFinishGrades.None, null)]
    [InlineData(11, false, InteriorFinishGrades.ClassOne, null)]
    public void A_gap_the_panel_names_is_a_gap_the_rules_report_as_insufficient_data(
        int floorNumber, bool? sprinklered, string? finish, string? buildingUse)
    {
        var shown = ZoneAreaLimit.For(floorNumber, sprinklered, finish, buildingUse);
        Assert.False(shown.IsKnown);

        // 面積 well inside every tier, so only the missing field can make this anything but a Pass.
        var outcome = Outcome(Facts(floorNumber, sprinklered, finish, buildingUse, areaSquareMeters: 10));
        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
    }

    /// <summary>
    /// The three grades are spelled exactly as the rule compares them; that is the whole reason the
    /// panel offers a list instead of a text box.
    /// </summary>
    [Fact]
    public void Every_grade_the_panel_offers_is_one_the_rule_recognises()
    {
        var requirement = Shipped().OfCategory(RuleCategory.CompartmentArea)
            .Single(x => x.RuleId == "tw-bcr-83-area").Rule.RequiredValue.Source;

        foreach (var grade in InteriorFinishGrades.All.Where(g => g != InteriorFinishGrades.None))
            Assert.Contains($"\"{grade}\"", requirement, StringComparison.Ordinal);

        // 無 is the baseline: the rule names no literal for it, it is simply what falls through.
        Assert.DoesNotContain($"\"{InteriorFinishGrades.None}\"", requirement, StringComparison.Ordinal);
        Assert.True(InteriorFinishGrades.All.All(InteriorFinishGrades.IsKnown));
        Assert.False(InteriorFinishGrades.IsKnown("耐燃二級"));
        Assert.False(InteriorFinishGrades.IsKnown(null));
    }

    // --- 第79條之1：上限照舊，但等一個人確認 -----------------------------------------------------

    /// <summary>
    /// 決議 11. The six uses 第79條之1 names are not exempt from anything the panel can see: the limit
    /// is the same number, with the same article, and only a note is added.
    /// </summary>
    [Theory]
    [InlineData(ZoneUses.Auditorium, false, 1500)]
    [InlineData(ZoneUses.Auditorium, true, 3000)]
    [InlineData(ZoneUses.ProductionLine, false, 1500)]
    [InlineData(ZoneUses.Classroom, false, 1500)]
    [InlineData(ZoneUses.Gymnasium, false, 1500)]
    [InlineData(ZoneUses.RetailMarket, false, 1500)]
    [InlineData(ZoneUses.CarPark, true, 3000)]
    public void An_article_79_1_use_keeps_the_article_79_limit(string use, bool sprinklered, double limit)
    {
        var shown = ZoneAreaLimit.For(9, sprinklered, interiorFinish: null, buildingUse: null, use);

        Assert.Equal(limit, shown.SquareMeters);
        Assert.Equal("第79條", shown.Clause);
        Assert.True(shown.IsKnown);
        Assert.False(shown.IsExempt);
        Assert.True(shown.NeedsArticle79_1Confirmation);
    }

    /// <summary>
    /// The cell must not read 免適用 — that is the misreading 決議 11 exists to prevent. It shows the
    /// number and says a person still has to confirm the rest.
    /// </summary>
    [Fact]
    public void The_limit_cell_shows_the_number_and_says_the_confirmation_is_pending()
    {
        var shown = ZoneAreaLimit.For(9, false, null, null, ZoneUses.Auditorium);

        Assert.Equal("第79條 上限 1500 m²" + ZoneAreaLimit.Article79_1Pending, shown.Description);
        Assert.Contains("1500", shown.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("免適用", shown.Description, StringComparison.Ordinal);
    }

    /// <summary>A 垂直區劃 reads exactly as it did: the two exemptions do not bleed into each other.</summary>
    [Fact]
    public void A_vertical_compartment_still_reads_as_exempt_and_asks_for_no_confirmation()
    {
        var shown = ZoneAreaLimit.For(9, false, null, null, ZoneUses.Atrium);

        Assert.True(shown.IsExempt);
        Assert.False(shown.NeedsArticle79_1Confirmation);
        Assert.Equal("第79條 免適用（第79條之2 垂直區劃）" + ZoneAreaLimit.AtriumNote, shown.Description);
    }

    /// <summary>
    /// §2.3: 第79條之1 only lets a 區劃 out of 前條第一項, and from the eleventh storey up 第83條 is the
    /// one deciding. So the note stops at the same storey the article does.
    /// </summary>
    [Fact]
    public void From_the_eleventh_storey_up_no_article_79_1_confirmation_is_pending()
    {
        var shown = ZoneAreaLimit.For(12, false, InteriorFinishGrades.None, "A-1", ZoneUses.Auditorium);

        Assert.Equal("第83條", shown.Clause);
        Assert.Equal(100, shown.SquareMeters);
        Assert.False(shown.NeedsArticle79_1Confirmation);
        Assert.DoesNotContain("第79條之1", shown.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("辦公")]
    [InlineData("觀眾廳")]
    [InlineData("停車場")]
    [InlineData(null)]
    public void A_use_outside_the_article_asks_for_no_confirmation(string? use)
    {
        Assert.False(ZoneAreaLimit.For(9, false, null, null, use).NeedsArticle79_1Confirmation);
    }

    /// <summary>
    /// The note rides along while a box is still blank, so a 觀眾席 whose 滅火設備 is unfilled still
    /// shows the 第79條之1 question rather than hiding it until the limit resolves.
    /// </summary>
    [Fact]
    public void An_article_79_1_zone_still_names_the_box_it_waits_on()
    {
        var shown = ZoneAreaLimit.For(9, null, null, null, ZoneUses.Auditorium);

        Assert.Equal(ZoneAreaLimitGap.Sprinklered, shown.Gaps);
        Assert.Equal("未填滅火設備，無法判定上限" + ZoneAreaLimit.Article79_1Pending, shown.Description);
    }

    /// <summary>
    /// The panel builds its cell from <see cref="ZoneAreaLimit.ForZone"/>, so the note has to survive
    /// that door too — and stop at the eleventh storey there as well.
    /// </summary>
    [Fact]
    public void The_panel_path_carries_the_note_below_the_eleventh_storey_only()
    {
        Assert.True(ZoneAreaLimit.ForZone(9, false, null, ZoneUses.Auditorium).NeedsArticle79_1Confirmation);
        Assert.False(ZoneAreaLimit.ForZone(12, false, null, ZoneUses.Auditorium).NeedsArticle79_1Confirmation);
    }

    /// <summary>
    /// 限制條件: 第79條之1 must not move the number. The rules stay the authority here as everywhere
    /// else in this file — evaluated for a 觀眾席, they still require 一、五○○平方公尺.
    /// </summary>
    [Theory]
    [InlineData(ZoneUses.Auditorium)]
    [InlineData(ZoneUses.CarPark)]
    public void The_note_does_not_move_the_number_the_rules_require(string use)
    {
        var shown = ZoneAreaLimit.For(9, false, null, null, use);
        var required = Outcome(Facts(9, false, null, null, zoneUse: use)).RequiredValue;

        Assert.True(shown.NeedsArticle79_1Confirmation);
        Assert.Equal(ReviewValue.Quantity(shown.SquareMeters!.Value, ReviewUnit.SquareMeter), required);
        Assert.Equal(1500, shown.SquareMeters);
    }

    // --- 面板路徑：十一層以上的裝修等級由模型推導 -------------------------------------------------

    /// <summary>
    /// B-09. 裝修等級 left the panel in V-12 步驟 9: it is derived from the modelled walls and ceilings
    /// when the review runs. So the cell must not call it 未填 — there is no box to fill, and the
    /// reader would go looking for one that does not exist.
    /// </summary>
    [Theory]
    [InlineData(11, null)]
    [InlineData(22, "辦公")]
    [InlineData(11, ZoneUses.Auditorium)]
    public void From_the_eleventh_storey_up_the_panel_says_the_grade_comes_from_the_model(int floorNumber, string? use)
    {
        var shown = ZoneAreaLimit.ForZone(floorNumber, true, "B-2", use);

        Assert.Equal("第83條 上限待檢討時由模型牆面／天花板耐燃等級推導", shown.Description);
        Assert.True(shown.FinishComesFromModel);
        Assert.DoesNotContain("未填", shown.Description, StringComparison.Ordinal);
        Assert.Null(shown.SquareMeters);
        Assert.False(shown.IsKnown);
        Assert.False(shown.IsExempt);
    }

    /// <summary>
    /// 限制條件: only the panel path changes. The review path reads a grade it really has, and every
    /// number and sentence it produces stays exactly as it was.
    /// </summary>
    [Fact]
    public void The_review_path_still_names_the_grade_it_was_given()
    {
        Assert.Equal("第83條 上限 400 m²", ZoneAreaLimit.For(11, false, InteriorFinishGrades.ClassOne, "H-2").Description);
        Assert.Equal("第83條 上限 100 m²（裝修等級非放寬條件）", ZoneAreaLimit.For(11, false, "耐燃二級", "B-2").Description);
        Assert.Equal("未填模型牆面／天花板耐燃等級、用途類組、滅火設備，無法判定上限",
            ZoneAreaLimit.For(11, null, null, null).Description);
        Assert.False(ZoneAreaLimit.For(11, false, InteriorFinishGrades.ClassOne, "H-2").FinishComesFromModel);
    }

    /// <summary>A 垂直區劃 of the eleventh storey needs no grade at all, so it still reads 免適用.</summary>
    [Fact]
    public void An_exempt_zone_of_the_eleventh_storey_is_untouched_by_the_derived_grade()
    {
        var shown = ZoneAreaLimit.ForZone(11, false, "B-2", ZoneUses.Atrium);

        Assert.True(shown.IsExempt);
        Assert.False(shown.FinishComesFromModel);
        Assert.Equal("第83條 免適用（第79條之2 垂直區劃）" + ZoneAreaLimit.AtriumNote, shown.Description);
    }

    private static RuleFacts Facts(
        int floorNumber,
        bool? sprinklered,
        string? finish,
        string? buildingUse,
        double areaSquareMeters = 10,
        string zoneUse = "辦公")
    {
        var facts = new RuleFacts(RuleFieldCatalog.Default)
            .Set("building.fireResistiveConstruction", true)
            .Set("building.floorsAboveGround", 20, ReviewUnit.None)
            .Set("zone.id", "zone-1")
            .Set("zone.use", zoneUse)
            .Set("zone.floorNumber", floorNumber, ReviewUnit.None)
            .Set("zone.area", areaSquareMeters, ReviewUnit.SquareMeter)
            // 檢查層對每個不是「第3項免除成立之挑空」的區劃都設這個值（決議 32）。
            .Set("zone.atriumMerged", false);

        if (sprinklered is bool value) facts = facts.Set("zone.sprinklered", value);
        if (buildingUse is not null) facts = facts.Set("building.use", buildingUse);
        if (finish is not null) facts = facts.Set("zone.interiorFinish", finish);
        return facts;
    }

    private static RuleOutcome Outcome(RuleFacts facts) =>
        new RuleEngine(Shipped()).Evaluate(RuleCategory.CompartmentArea, facts, Today);

    private static CompiledRuleSet Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return loaded.Value;
    }
}
