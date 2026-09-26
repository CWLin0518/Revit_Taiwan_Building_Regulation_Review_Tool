using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Rules;

/// <summary>
/// The shipped VerticalCompartment rules — 第79條之2第1項
/// (docs/regulations/vertical-compartment.md).
///
/// 第79條之2 gives the five 垂直區劃 their own 區劃分隔, which is why both area rules exempt them
/// (see <see cref="VerticalCompartmentExemptionTests"/>). What these rules add is only what the
/// 區劃 rules cannot say:
///
/// <list type="bullet">
/// <item>昇降機道裝設之防火設備應具有遮煙性能，第2項之昇降機間但書成立者得免。</item>
/// <item>管道間之維修門應具有一小時以上防火時效。</item>
/// <item>管道間之維修門應具有遮煙性能。</item>
/// </list>
///
/// 第1項本文's 一小時牆壁 and 防火門窗等防火設備 are deliberately absent: a 垂直區劃 is a 區劃, so
/// <c>tw-bcr-79-wall-rating</c> and <c>tw-bcr-79-opening</c> already require exactly those of its
/// boundary, whichever article created the 區劃. Writing them again here would give one wall two
/// verdicts, and — as <see cref="Article_79_reviews_the_boundary_without_reading_the_zone_use"/>
/// records — would have to key off <c>zone.use</c> at a higher priority, which is precisely what the
/// engine must never be asked to do.
/// </summary>
public sealed class Article79_2VerticalCompartmentRuleTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private static readonly RuleEvaluationContext Today = new(new DateTime(2026, 9, 26), "TW");

    private const string Hoistway = "tw-bcr-79-2-hoistway-smoke-seal";
    private const string ShaftDoorRating = "tw-bcr-79-2-shaft-door-rating";
    private const string ShaftDoorSmoke = "tw-bcr-79-2-shaft-door-smoke-seal";

    public static TheoryData<VerticalCompartmentRequirement> Requirements()
    {
        var data = new TheoryData<VerticalCompartmentRequirement>();
        foreach (var requirement in VerticalCompartmentRequirements.All) data.Add(requirement);
        return data;
    }

    // --- 規則檔本身 -----------------------------------------------------------------------------

    [Fact]
    public void Shipped_rule_set_compiles_the_three_vertical_compartment_rules()
    {
        var rules = Shipped().OfCategory(RuleCategory.VerticalCompartment).ToList();

        Assert.Equal(
            new[] { Hoistway, ShaftDoorRating, ShaftDoorSmoke },
            rules.Select(x => x.RuleId).OrderBy(x => x, StringComparer.Ordinal));

        Assert.All(rules, rule => Assert.All(
            rule.AppliesWhen.Fields.Concat(rule.EvidenceFields).Concat(new[] { rule.Requirement.Actual }),
            field => Assert.True(field.IsAvailableIn(RuleCategory.VerticalCompartment))));
    }

    /// <summary>
    /// One 維修門 owes two requirements at once, so the subject is a (設備, 要求) pair and the rules
    /// are told apart by <c>shaft.requirement</c> alone. All three therefore sit at one priority and
    /// never overlap — no subject can reach the engine's Conflict path.
    /// </summary>
    [Fact]
    public void The_three_rules_are_one_priority_and_mutually_exclusive()
    {
        var rules = Shipped().OfCategory(RuleCategory.VerticalCompartment).ToList();

        Assert.All(rules, rule => Assert.Equal(10, rule.Priority));
        Assert.Equal(
            VerticalCompartmentRequirements.All.Select(VerticalCompartmentRequirements.RuleText)
                .OrderBy(x => x, StringComparer.Ordinal),
            rules.Select(x => Requirement(x.RuleId)).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>Every rule names its 條文 as 第79條之2, and none of them claims to be 第79條 or 第83條.</summary>
    [Fact]
    public void Every_rule_cites_article_79_2()
    {
        var rules = Shipped().OfCategory(RuleCategory.VerticalCompartment).ToList();

        Assert.All(rules, rule => Assert.Contains("第79條之2", rule.Rule.LegalReference, StringComparison.Ordinal));
        Assert.All(rules, rule => Assert.DoesNotContain("第83條", rule.Rule.LegalReference, StringComparison.Ordinal));
    }

    [Fact]
    public void Shaft_fields_are_hidden_from_the_other_categories()
    {
        var catalog = RuleFieldCatalog.Default;

        foreach (var name in new[]
                 {
                     "shaft.requirement", "shaft.elementUniqueId", "shaft.providedFireRating",
                     "shaft.providedSmokeProtection", "shaft.elevatorLobbyProtected"
                 })
        {
            var field = catalog.Find(name);
            Assert.NotNull(field);
            Assert.Equal(new[] { RuleCategory.VerticalCompartment }, field!.AvailableIn);
        }

        // The other way round: a 第79條之2 rule reaches for neither an element, an opening nor a junction.
        Assert.False(catalog.Find("element.providedFireRating")!.IsAvailableIn(RuleCategory.VerticalCompartment));
        Assert.False(catalog.Find("opening.providedFireProtection")!.IsAvailableIn(RuleCategory.VerticalCompartment));
        Assert.False(catalog.Find("junction.minFireRating")!.IsAvailableIn(RuleCategory.VerticalCompartment));

        // zone.* and building.* describe the compartment and stay open to every category.
        Assert.True(catalog.Find("zone.use")!.IsAvailableIn(RuleCategory.VerticalCompartment));
        Assert.True(catalog.Find("building.fireResistiveConstruction")!.IsAvailableIn(RuleCategory.VerticalCompartment));
    }

    /// <summary>
    /// The vocabulary is one thing: <see cref="VerticalCompartmentRequirements"/> is what the check
    /// will write into <c>shaft.requirement</c>, and it has to be spelled exactly as the shipped
    /// rules compare it, or every subject would quietly fall through to 不適用.
    /// </summary>
    [Theory]
    [MemberData(nameof(Requirements))]
    public void Each_requirement_of_the_vocabulary_has_a_rule_that_answers_it(VerticalCompartmentRequirement requirement)
    {
        var text = VerticalCompartmentRequirements.RuleText(requirement);
        var rules = Shipped().OfCategory(RuleCategory.VerticalCompartment)
            .Where(x => Requirement(x.RuleId) == text).ToList();

        Assert.Single(rules);
    }

    /// <summary>
    /// And each requirement belongs to a 用途 the panel actually offers — 昇降機道 and 管道間 are both
    /// on the 第79條之2第1項 list in <see cref="ZoneUses"/>, so the 用字表 and the rules cannot drift.
    /// </summary>
    [Theory]
    [MemberData(nameof(Requirements))]
    public void Each_requirement_belongs_to_a_use_the_panel_offers(VerticalCompartmentRequirement requirement)
    {
        var use = VerticalCompartmentRequirements.UseOf(requirement);

        Assert.True(ZoneUses.IsVerticalCompartment(use));
        Assert.Contains(use, ZoneUses.VerticalCompartments);
    }

    [Fact]
    public void Only_the_hoistway_and_the_shaft_carry_extra_requirements()
    {
        Assert.Equal(
            new[] { VerticalCompartmentRequirement.HoistwaySmokeSeal },
            VerticalCompartmentRequirements.ForUse(ZoneUses.ElevatorShaft));
        Assert.Equal(
            new[] { VerticalCompartmentRequirement.ShaftDoorRating, VerticalCompartmentRequirement.ShaftDoorSmokeSeal },
            VerticalCompartmentRequirements.ForUse(ZoneUses.Shaft));

        // 挑空、昇降階梯間、樓梯間 are區劃分隔 by 第1項本文 alone: nothing here is asked of them.
        Assert.Empty(VerticalCompartmentRequirements.ForUse(ZoneUses.Atrium));
        Assert.Empty(VerticalCompartmentRequirements.ForUse(ZoneUses.EscalatorWell));
        Assert.Empty(VerticalCompartmentRequirements.ForUse(ZoneUses.Stairwell));
        Assert.Empty(VerticalCompartmentRequirements.ForUse("辦公"));
        Assert.Empty(VerticalCompartmentRequirements.ForUse(null));
    }

    // --- 昇降機道之防火設備遮煙性能（第1項第2句、第2項但書） -------------------------------------

    [Fact]
    public void A_smoke_sealed_hoistway_device_passes()
    {
        var outcome = Outcome(Shaft(VerticalCompartmentRequirement.HoistwaySmokeSeal).Set("shaft.providedSmokeProtection", "是"));

        Assert.Equal(ReviewStatus.Pass, outcome.Status);
        Assert.Equal(Hoistway, outcome.RuleId);
    }

    /// <summary>
    /// 否 alone is not yet a 未符合: the 第2項 但書 has to be settled first, so the flag is answered
    /// here. <see cref="An_undecidable_elevator_lobby_only_matters_when_the_seal_is_absent"/> is the
    /// case where it is not.
    /// </summary>
    [Fact]
    public void A_hoistway_device_with_neither_a_smoke_seal_nor_the_proviso_fails()
    {
        var outcome = Outcome(Shaft(VerticalCompartmentRequirement.HoistwaySmokeSeal)
            .Set("shaft.providedSmokeProtection", "否")
            .Set("shaft.elevatorLobbyProtected", false));

        Assert.Equal(ReviewStatus.Fail, outcome.Status);
        Assert.Equal(Hoistway, outcome.RuleId);
    }

    /// <summary>No value is 資料不足, never 未符合 (spec 11.3).</summary>
    [Fact]
    public void An_unanswered_smoke_seal_is_insufficient_data()
    {
        var outcome = Outcome(Shaft(VerticalCompartmentRequirement.HoistwaySmokeSeal));

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
        Assert.Equal("shaft.providedSmokeProtection", Assert.Single(outcome.Gaps).Field);
    }

    /// <summary>
    /// 第2項：昇降機道前設有昇降機間且併同區劃、其出入口具遮煙性能者，昇降機道出入口得免。
    /// </summary>
    [Fact]
    public void A_protected_elevator_lobby_exempts_the_hoistway_device()
    {
        var outcome = Outcome(Shaft(VerticalCompartmentRequirement.HoistwaySmokeSeal)
            .Set("shaft.providedSmokeProtection", "否")
            .Set("shaft.elevatorLobbyProtected", true));

        Assert.Equal(ReviewStatus.NotApplicable, outcome.Status);
        Assert.Equal(RuleOutcomeReason.Exempt, outcome.Reason);
        Assert.Equal(Hoistway, outcome.RuleId);
    }

    /// <summary>
    /// An undecidable 但書 only matters once the requirement has failed — the same shape 第79條's
    /// 九十公分 exemption has, and the reason 資料不足 is never reported as 未符合 (spec 11.3).
    /// </summary>
    [Fact]
    public void An_undecidable_elevator_lobby_only_matters_when_the_seal_is_absent()
    {
        var sealed_ = Outcome(Shaft(VerticalCompartmentRequirement.HoistwaySmokeSeal).Set("shaft.providedSmokeProtection", "是"));
        var unsealed = Outcome(Shaft(VerticalCompartmentRequirement.HoistwaySmokeSeal).Set("shaft.providedSmokeProtection", "否"));

        Assert.Equal(ReviewStatus.Pass, sealed_.Status);
        Assert.Equal(ReviewStatus.InsufficientData, unsealed.Status);
        Assert.Equal("shaft.elevatorLobbyProtected", Assert.Single(unsealed.Gaps).Field);
    }

    // --- 管道間維修門（第1項第3句） -------------------------------------------------------------

    [Theory]
    [InlineData(60, ReviewStatus.Pass)]
    [InlineData(120, ReviewStatus.Pass)]
    [InlineData(59, ReviewStatus.Fail)]
    [InlineData(30, ReviewStatus.Fail)]
    public void A_shaft_maintenance_door_needs_a_full_hour(double minutes, ReviewStatus expected)
    {
        var outcome = Outcome(Shaft(VerticalCompartmentRequirement.ShaftDoorRating)
            .Set("shaft.providedFireRating", minutes, ReviewUnit.Minute));

        Assert.Equal(expected, outcome.Status);
        Assert.Equal(ShaftDoorRating, outcome.RuleId);
    }

    [Fact]
    public void An_unanswered_shaft_door_rating_is_insufficient_data()
    {
        var outcome = Outcome(Shaft(VerticalCompartmentRequirement.ShaftDoorRating));

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
        Assert.Equal("shaft.providedFireRating", Assert.Single(outcome.Gaps).Field);
    }

    [Theory]
    [InlineData("是", ReviewStatus.Pass)]
    [InlineData("否", ReviewStatus.Fail)]
    public void A_shaft_maintenance_door_needs_a_smoke_seal_too(string provided, ReviewStatus expected)
    {
        var outcome = Outcome(Shaft(VerticalCompartmentRequirement.ShaftDoorSmokeSeal)
            .Set("shaft.providedSmokeProtection", provided));

        Assert.Equal(expected, outcome.Status);
        Assert.Equal(ShaftDoorSmoke, outcome.RuleId);
    }

    /// <summary>
    /// The 昇降機間 但書 is written for 昇降機道 出入口 only. A 管道間維修門 is not relieved by it, so the
    /// rule carries no exemption at all and the flag is simply not read.
    /// </summary>
    [Fact]
    public void The_elevator_lobby_proviso_does_not_reach_the_shaft_door()
    {
        var rating = Outcome(Shaft(VerticalCompartmentRequirement.ShaftDoorRating)
            .Set("shaft.providedFireRating", 30, ReviewUnit.Minute)
            .Set("shaft.elevatorLobbyProtected", true));
        var smoke = Outcome(Shaft(VerticalCompartmentRequirement.ShaftDoorSmokeSeal)
            .Set("shaft.providedSmokeProtection", "否")
            .Set("shaft.elevatorLobbyProtected", true));

        Assert.Equal(ReviewStatus.Fail, rating.Status);
        Assert.Equal(ReviewStatus.Fail, smoke.Status);
    }

    // --- 適用範圍 -------------------------------------------------------------------------------

    /// <summary>第79條之2 opens with 「防火構造建築物內之……」, so a building that is not one is out of scope.</summary>
    [Theory]
    [MemberData(nameof(Requirements))]
    public void A_building_that_is_not_fire_resistive_is_out_of_scope(VerticalCompartmentRequirement requirement)
    {
        var outcome = Outcome(Shaft(requirement)
            .Set("building.fireResistiveConstruction", false)
            .Set("shaft.providedSmokeProtection", "否")
            .Set("shaft.providedFireRating", 0, ReviewUnit.Minute));

        Assert.Equal(ReviewStatus.NotApplicable, outcome.Status);
        Assert.Equal(RuleOutcomeReason.NoRuleApplies, outcome.Reason);
    }

    /// <summary>A requirement no rule knows is 不適用, not a silent pass.</summary>
    [Fact]
    public void A_requirement_outside_the_vocabulary_matches_no_rule()
    {
        var outcome = Outcome(new RuleFacts(RuleFieldCatalog.Default)
            .Set("building.fireResistiveConstruction", true)
            .Set("zone.id", "zone-1")
            .Set("zone.use", ZoneUses.Shaft)
            .Set("shaft.requirement", "ShaftDoorThermalInsulation")
            .Set("shaft.providedSmokeProtection", "否"));

        Assert.Equal(ReviewStatus.NotApplicable, outcome.Status);
        Assert.Equal(RuleOutcomeReason.NoRuleApplies, outcome.Reason);
    }

    // --- 與第79條本文的分工 ---------------------------------------------------------------------

    /// <summary>
    /// The 一小時區劃牆壁 and 防火設備 of 第1項本文 stay with 第79條's rules, and those rules must keep
    /// deciding a boundary <em>without</em> reading <c>zone.use</c>.
    ///
    /// This is not tidiness. The engine stops at the highest priority that has an applicable rule,
    /// and a rule whose applicability cannot be decided blocks every lower priority
    /// (<see cref="RuleEngineTests"/>). <c>zone.use</c> is absent whenever 防火檢討_區劃用途 is blank —
    /// which is the normal state of an ordinary room — so a 第79條之2 copy of those requirements,
    /// keyed off <c>zone.use</c> above them, would turn every unlabelled wall and door in the project
    /// into 資料不足. The extra requirements in this category are safe from that because their subject
    /// only exists once the 用途 is known.
    /// </summary>
    [Fact]
    public void Article_79_reviews_the_boundary_without_reading_the_zone_use()
    {
        var set = Shipped();

        foreach (var ruleId in new[] { "tw-bcr-79-wall-rating", "tw-bcr-79-floor-rating", "tw-bcr-79-opening" })
        {
            var rule = set.Rules.Single(x => x.RuleId == ruleId);
            Assert.DoesNotContain("zone.use", rule.AppliesWhen.Fields.Select(f => f.Name));
        }

        // And 第79條之2 adds no rule of those categories, so a boundary keeps exactly one verdict.
        Assert.All(set.OfCategory(RuleCategory.VerticalCompartment),
            rule => Assert.Equal(RuleCategory.VerticalCompartment, rule.Rule.Category));
        Assert.DoesNotContain("第79條之2",
            string.Concat(set.OfCategory(RuleCategory.FireResistance)
                .Concat(set.OfCategory(RuleCategory.OpeningProtection))
                .Select(x => x.Rule.LegalReference)));
    }

    /// <summary>
    /// And the 區劃面積 exemption now says who took over, instead of only naming the condition that
    /// held (see <see cref="VerticalCompartmentExemptionTests"/> for the exemption itself).
    /// </summary>
    [Fact]
    public void The_handoff_note_names_the_article_that_reviews_the_vertical_compartment()
    {
        Assert.Contains("第79條之2", ZoneUses.VerticalCompartmentHandoff, StringComparison.Ordinal);
    }

    // --- helpers --------------------------------------------------------------------------------

    /// <summary>The <c>shaft.requirement</c> literal a rule's applicability condition compares against.</summary>
    private static string Requirement(string ruleId)
    {
        var source = Shipped().Rules.Single(x => x.RuleId == ruleId).Rule.AppliesWhen.Source;
        var marker = "shaft.requirement == \"";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{ruleId} 的適用條件沒有比對 shaft.requirement：{source}");
        start += marker.Length;
        return source.Substring(start, source.IndexOf('"', start) - start);
    }

    private static RuleFacts Shaft(VerticalCompartmentRequirement requirement) =>
        new RuleFacts(RuleFieldCatalog.Default)
            .Set("building.fireResistiveConstruction", true)
            .Set("zone.id", "zone-1")
            .Set("zone.use", VerticalCompartmentRequirements.UseOf(requirement))
            .Set("shaft.requirement", VerticalCompartmentRequirements.RuleText(requirement))
            .Set("shaft.elementUniqueId", "opening-1");

    private static RuleOutcome Outcome(RuleFacts facts) =>
        new RuleEngine(Shipped()).Evaluate(RuleCategory.VerticalCompartment, facts, Today);

    private static CompiledRuleSet Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return loaded.Value;
    }
}
