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
/// What 第79條之1 must <em>not</em> do to the shipped rule set
/// (docs/regulations/article-79-1-area-exemption.md §3.6、§5).
///
/// 第79條之1 lets six uses out of 第79條第1項's 一、五○○平方公尺 — but only when they 無法區劃分隔 and
/// 自成一個區劃 with 一小時 防火時效 walls and 防火設備 whose 阻熱性 is also 一小時. The tool can see the
/// declaration and the 用途; it can see neither of the other two. Writing the six words into either
/// area rule's exemption list would therefore have the engine answer 免適用 ＋ 「符合豁免條件」 — the
/// tool declaring a fact it never read, and hiding the largest 區劃 in the project from the review
/// table at the same time. So the exemption stays outside the engine, and these tests are what keep
/// it outside: 決議 2 is easy to undo by accident, because the DSL would accept it.
///
/// 挑空 is not a precedent for doing otherwise. 第83條 itself writes 「除依第七十九條之二規定之垂直
/// 區劃外」 — the article puts them outside the area calculation. 第79條之1's exemption is conditional,
/// and two of its conditions have no field.
/// </summary>
public sealed class Article79_1AreaExemptionTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private static readonly RuleEvaluationContext Today = new(new DateTime(2026, 9, 27), "TW");

    private const string Article79 = "tw-bcr-79-area";
    private const string Article83 = "tw-bcr-83-area";

    // --- 豁免清單一字不動 -----------------------------------------------------------------------

    /// <summary>
    /// The decision itself: neither area rule mentions any of the six words, in any exemption, in any
    /// form. Asserted twice over — once against the exemption 第79條之1 would have written, and once
    /// against the whole source text of every exemption either rule carries, so a hand-rolled variant
    /// (<c>zone.use == "觀眾席" &amp;&amp; …</c>) is caught as well.
    /// </summary>
    [Fact]
    public void The_area_rules_carry_no_article_79_1_exemption()
    {
        foreach (var ruleId in new[] { Article79, Article83 })
        {
            var sources = Exemptions(ruleId);

            foreach (var use in ZoneUses.Article79_1Uses)
            {
                Assert.DoesNotContain(ZoneUses.ExemptionSource(use), sources);
                Assert.All(sources, source => Assert.DoesNotContain(use, source, StringComparison.Ordinal));
            }

            Assert.DoesNotContain("cannotBeSubdivided", string.Join("|", sources), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// And the lists are still exactly the 垂直區劃 of 第79條之2第1項 — nothing was added anywhere else
    /// either, so the two rules remain the pair <c>VerticalCompartmentExemptionTests</c> describes.
    /// </summary>
    [Fact]
    public void Both_lists_are_still_only_the_vertical_compartments()
    {
        var expected = ZoneUses.VerticalCompartments.Select(ZoneUses.ExemptionSource).ToArray();

        Assert.Equal(expected, Exemptions(Article79));
        Assert.Equal(expected, Exemptions(Article83));
    }

    /// <summary>
    /// 決議 8: 第79條之1 does not go through the engine at all, so it gains no
    /// <see cref="RuleCategory"/>. A category with no rule in it would make every
    /// <c>Evaluate</c> answer 人工覆核 ＋ <see cref="RuleOutcomeReason.NoRule"/>, whose message —
    /// 「規則集…沒有…規則」 — reads as a broken rule set rather than as a judgement a person owes.
    /// </summary>
    [Fact]
    public void The_rule_set_gains_no_category_for_the_exemption()
    {
        Assert.Equal(5, Enum.GetValues<RuleCategory>().Length);

        var rules = Shipped().OfCategory(RuleCategory.CompartmentArea).ToArray();
        Assert.Equal(4, rules.Length);
        Assert.All(rules, x => Assert.DoesNotContain("79-1", x.RuleId, StringComparison.Ordinal));
        Assert.All(rules, x => Assert.DoesNotContain("第79條之1", x.Rule.LegalReference, StringComparison.Ordinal));
    }

    // --- 既有判定不動 ---------------------------------------------------------------------------

    /// <summary>
    /// The behavioural half of 決議 2 and 決議 3: a 三○○○平方公尺 觀眾席 that would satisfy every element
    /// 第79條之1 can see is still 未符合 under 第79條第1項. Releasing it is 人工覆寫 (spec §11.8), because
    /// （丁） is what makes it lawful and only a person can read （丁）.
    /// </summary>
    [Theory]
    [InlineData(ZoneUses.Auditorium)]
    [InlineData(ZoneUses.ProductionLine)]
    [InlineData(ZoneUses.Classroom)]
    [InlineData(ZoneUses.Gymnasium)]
    [InlineData(ZoneUses.RetailMarket)]
    [InlineData(ZoneUses.CarPark)]
    public void An_article_79_1_use_is_still_reviewed_against_the_limit(string use)
    {
        var outcome = Outcome(Zone(9, 3000, use));

        Assert.Equal(ReviewStatus.Fail, outcome.Status);
        Assert.Equal(Article79, outcome.RuleId);
        Assert.DoesNotContain("豁免", outcome.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("第79條之1", outcome.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// §2.3: 第79條之1 lifts 「前條第一項」 and nothing else, so a 觀眾席 on the twelfth storey is still
    /// 第83條's 一○○／二○○平方公尺. The engine's priority tiers already do this — 第83條 answers on tier
    /// 20 and 第79條 never gets asked — so there is no code for it, only this test.
    /// </summary>
    [Fact]
    public void Article_79_1_does_not_reach_the_eleventh_storey()
    {
        var twelfth = Outcome(Zone(12, 3000, ZoneUses.Auditorium));

        Assert.Equal(Article83, twelfth.RuleId);
        Assert.Equal(ReviewStatus.Fail, twelfth.Status);
        Assert.DoesNotContain("第79條之1", twelfth.Message, StringComparison.Ordinal);

        // Below it, 第79條 is the one that answers — the storey, not the 用途, picks the article.
        Assert.Equal(Article79, Outcome(Zone(10, 3000, ZoneUses.Auditorium)).RuleId);
    }

    /// <summary>
    /// 決議 12 as a baseline: the 區劃面積 message is the engine's and stays the engine's. Nothing about
    /// 第79條之1 is appended to it — the exemption is its own row in the review table, with the same
    /// <c>SubjectUniqueIds</c>, so the two sit next to each other without either restating the other.
    /// </summary>
    [Fact]
    public void The_area_message_says_nothing_of_the_exemption()
    {
        var auditorium = Outcome(Zone(9, 3000, ZoneUses.Auditorium));
        var office = Outcome(Zone(9, 3000, "辦公"));

        Assert.Equal(office.Message, auditorium.Message);
        Assert.Equal(office.Status, auditorium.Status);
        Assert.Equal(office.RuleVersion, auditorium.RuleVersion);
    }

    // --- helpers --------------------------------------------------------------------------------

    private static string[] Exemptions(string ruleId) =>
        Shipped().OfCategory(RuleCategory.CompartmentArea)
            .Single(x => x.RuleId == ruleId)
            .Rule.Exemptions.Select(x => x.Source).ToArray();

    private static RuleFacts Zone(int floorNumber, double areaSquareMeters, string use) =>
        new RuleFacts(RuleFieldCatalog.Default)
            .Set("building.fireResistiveConstruction", true)
            .Set("building.use", "A-1")
            .Set("building.floorsAboveGround", 15, ReviewUnit.None)
            .Set("zone.id", "zone-1")
            .Set("zone.use", use)
            .Set("zone.floorNumber", floorNumber, ReviewUnit.None)
            .Set("zone.area", areaSquareMeters, ReviewUnit.SquareMeter)
            .Set("zone.sprinklered", false)
            .Set("zone.interiorFinish", InteriorFinishGrades.None)
            // 檢查層對每個不是「第3項免除成立之挑空」的區劃都設這個值（決議 32）。
            .Set("zone.atriumMerged", false);

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
