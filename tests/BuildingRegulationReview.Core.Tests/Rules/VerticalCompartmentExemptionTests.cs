using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Rules;

/// <summary>
/// The 垂直區劃 of 第79條之2第1項, as both shipped 區劃面積 rules exempt them.
///
/// 第83條 says it in words — 「除依第七十九條之二規定之垂直區劃外」 — and 第79條 does not, but the
/// reason is the same in both: 挑空部分、昇降階梯間、安全梯之樓梯間、昇降機道、垂直貫穿樓板之管道間
/// are區劃分隔 by 第79條之2 in their own right, and nothing about which storey they sit on moves them
/// from one area rule to the other. So the two rules carry the same exemption list, and
/// <see cref="ZoneUses"/> is the one place that list is written down.
///
/// 「其他類似部分」 cannot be enumerated, so a 用途 outside the list is simply not exempt. That is the
/// safe direction: it reviews a 區劃 that might not have needed it, never the other way round.
/// </summary>
public sealed class VerticalCompartmentExemptionTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private static readonly RuleEvaluationContext Today = new(new DateTime(2026, 9, 26), "TW");

    private const string Article79 = "tw-bcr-79-area";
    private const string Article83 = "tw-bcr-83-area";

    public static TheoryData<string> VerticalCompartments()
    {
        var data = new TheoryData<string>();
        foreach (var use in ZoneUses.VerticalCompartments) data.Add(use);
        return data;
    }

    // --- 兩條規則的豁免清單一致 -----------------------------------------------------------------

    /// <summary>
    /// The unification itself. Before this, 第79條 exempted 樓梯間 alone while 第83條 exempted four
    /// uses, so the same 樓梯間-adjacent 區劃 was 免適用 on the eleventh storey and reviewed on the
    /// tenth — a difference neither article states.
    /// </summary>
    [Fact]
    public void Both_area_rules_carry_the_same_exemption_list()
    {
        Assert.Equal(Exemptions(Article79), Exemptions(Article83));
    }

    /// <summary>
    /// And that shared list is exactly <see cref="ZoneUses.VerticalCompartments"/> — the panel offers
    /// what the rules read, spelled as the rules compare it, in the order they list it.
    /// </summary>
    [Fact]
    public void The_shared_list_is_the_vocabulary_the_panel_offers()
    {
        var expected = ZoneUses.VerticalCompartments.Select(ZoneUses.ExemptionSource).ToArray();

        Assert.Equal(expected, Exemptions(Article83));
    }

    [Fact]
    public void The_vocabulary_matches_only_the_exact_wording()
    {
        Assert.All(ZoneUses.VerticalCompartments, use => Assert.True(ZoneUses.IsVerticalCompartment(use)));
        Assert.True(ZoneUses.IsVerticalCompartment(" 樓梯間 "));

        Assert.False(ZoneUses.IsVerticalCompartment("梯間"));
        Assert.False(ZoneUses.IsVerticalCompartment("電梯井"));
        Assert.False(ZoneUses.IsVerticalCompartment("辦公"));
        Assert.False(ZoneUses.IsVerticalCompartment(""));
        Assert.False(ZoneUses.IsVerticalCompartment(null));
    }

    // --- 兩條規則對同一個用途的判定一致 ---------------------------------------------------------

    /// <summary>
    /// Below the eleventh storey 第79條 decides; every vertical compartment is 免適用 there, however
    /// far over 一、五○○平方公尺 it is.
    /// </summary>
    [Theory]
    [MemberData(nameof(VerticalCompartments))]
    public void Article_79_exempts_every_vertical_compartment(string use)
    {
        var outcome = Outcome(Zone(floorNumber: 9, areaSquareMeters: 9000, use));

        Assert.Equal(ReviewStatus.NotApplicable, outcome.Status);
        Assert.Equal(Article79, outcome.RuleId);
        Assert.Equal(RuleOutcomeReason.Exempt, outcome.Reason);
        Assert.Equal(ReviewValue.OfText(use), outcome.Evidence.Find("zone.use"));
    }

    /// <summary>From the eleventh storey up 第83條 decides, and reaches the same answer.</summary>
    [Theory]
    [MemberData(nameof(VerticalCompartments))]
    public void Article_83_exempts_every_vertical_compartment(string use)
    {
        var outcome = Outcome(Zone(floorNumber: 11, areaSquareMeters: 9000, use));

        Assert.Equal(ReviewStatus.NotApplicable, outcome.Status);
        Assert.Equal(Article83, outcome.RuleId);
        Assert.Equal(RuleOutcomeReason.Exempt, outcome.Reason);
    }

    /// <summary>
    /// The point of unifying: moving a 區劃 across the tenth/eleventh storey line no longer changes
    /// whether it is exempt.
    /// </summary>
    [Theory]
    [MemberData(nameof(VerticalCompartments))]
    public void The_storey_no_longer_decides_whether_a_vertical_compartment_is_exempt(string use)
    {
        Assert.Equal(
            Outcome(Zone(10, 9000, use)).Status,
            Outcome(Zone(11, 9000, use)).Status);
    }

    /// <summary>
    /// 昇降階梯間 is the one 第79條之2 names that neither rule listed before. It is exempt now, on
    /// both storeys — the whole list comes from that article, not from what happened to be typed.
    /// </summary>
    [Fact]
    public void The_escalator_well_is_exempt_under_both_articles()
    {
        Assert.Contains(ZoneUses.EscalatorWell, ZoneUses.VerticalCompartments);
        Assert.Equal(ReviewStatus.NotApplicable, Outcome(Zone(9, 9000, ZoneUses.EscalatorWell)).Status);
        Assert.Equal(ReviewStatus.NotApplicable, Outcome(Zone(11, 9000, ZoneUses.EscalatorWell)).Status);
    }

    /// <summary>A 用途 outside the list is reviewed, not exempted — under either article.</summary>
    [Theory]
    [InlineData(9)]
    [InlineData(11)]
    public void A_use_outside_the_list_is_still_reviewed(int floorNumber)
    {
        Assert.Equal(ReviewStatus.Fail, Outcome(Zone(floorNumber, 9000, "辦公")).Status);
    }

    // --- 面板與規則一致 -------------------------------------------------------------------------

    /// <summary>
    /// The panel says 免適用 for exactly the 用途 the rules exempt, and names the article that got to
    /// decide — so a 區劃 is never shown a limit the review then declines to apply.
    /// </summary>
    [Theory]
    [MemberData(nameof(VerticalCompartments))]
    public void The_panel_shows_the_exemption_the_rules_apply(string use)
    {
        var below = ZoneAreaLimit.ForZone(9, sprinklered: null, buildingUse: null, use);
        var above = ZoneAreaLimit.ForZone(11, sprinklered: null, buildingUse: null, use);

        Assert.True(below.IsExempt);
        Assert.True(above.IsExempt);
        Assert.Null(below.SquareMeters);
        Assert.Equal("第79條 免適用（第79條之2 垂直區劃）", below.Description);
        Assert.Equal("第83條 免適用（第79條之2 垂直區劃）", above.Description);
    }

    /// <summary>
    /// An exemption is decided before the requirement, so an exempt 區劃 waits on none of the boxes
    /// the limit would otherwise need — the panel must not ask for them either.
    /// </summary>
    [Fact]
    public void An_exempt_zone_waits_on_no_other_box()
    {
        var shown = ZoneAreaLimit.ForZone(11, sprinklered: null, buildingUse: null, ZoneUses.Stairwell);

        Assert.Equal(ZoneAreaLimitGap.None, shown.Gaps);
        Assert.DoesNotContain("未填", shown.Description);
    }

    /// <summary>樓層序 still comes first: it is what picks the article the exemption is read under.</summary>
    [Fact]
    public void With_no_storey_the_panel_still_names_only_the_storey()
    {
        var shown = ZoneAreaLimit.ForZone(null, sprinklered: null, buildingUse: null, ZoneUses.Stairwell);

        Assert.False(shown.IsExempt);
        Assert.Equal(ZoneAreaLimitGap.FloorNumber, shown.Gaps);
    }

    [Fact]
    public void A_use_outside_the_list_leaves_the_panel_showing_the_limit()
    {
        var shown = ZoneAreaLimit.ForZone(9, sprinklered: false, buildingUse: null, "辦公");

        Assert.False(shown.IsExempt);
        Assert.Equal(1500, shown.SquareMeters);
    }

    // --- helpers --------------------------------------------------------------------------------

    private static string[] Exemptions(string ruleId) =>
        Shipped().OfCategory(RuleCategory.CompartmentArea)
            .Single(x => x.RuleId == ruleId)
            .Rule.Exemptions.Select(x => x.Source).ToArray();

    private static RuleFacts Zone(int floorNumber, double areaSquareMeters, string use) =>
        new RuleFacts(RuleFieldCatalog.Default)
            .Set("building.fireResistiveConstruction", true)
            .Set("building.use", "B-2")
            .Set("building.floorsAboveGround", 15, ReviewUnit.None)
            .Set("zone.id", "zone-1")
            .Set("zone.use", use)
            .Set("zone.floorNumber", floorNumber, ReviewUnit.None)
            .Set("zone.area", areaSquareMeters, ReviewUnit.SquareMeter)
            .Set("zone.sprinklered", false)
            .Set("zone.interiorFinish", InteriorFinishGrades.None);

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
