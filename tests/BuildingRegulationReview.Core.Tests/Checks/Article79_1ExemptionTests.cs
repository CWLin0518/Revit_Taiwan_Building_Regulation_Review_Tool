using System;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Checks;

/// <summary>
/// 第79條之1's two 款 (docs/regulations/article-79-1-area-exemption.md §3.5).
///
/// Like 第79條之2第3項, this is a classification and not a requirement: it says whether 第79條第1項's
/// 一、五○○平方公尺 reaches a 區劃 at all. So there is no 符合 and no 未符合 here — only 免除成立
/// (which the review reports as 人工覆核), 不成立 (不適用) and 資料不足. What follows walks the §3.5
/// state table row by row, then the 用字 table's six words one at a time.
///
/// The one thing these tests must never start allowing is an exemption that holds turning into a 符合,
/// or into a 免適用 on the area rules: （丁）「自成一個區劃」 and 第2項's 阻熱性 have no field, so only a
/// person can finish this judgement. 第79條之1 is deliberately absent from both area rules' exemption
/// lists — <c>Article79_1AreaExemptionTests</c> guards that half.
/// </summary>
public sealed class Article79_1ExemptionTests
{
    // --- §3.5 狀態表 ----------------------------------------------------------------------------

    /// <summary>第79條第1項 only reaches a 防火構造建築物, so there is nothing for 第79條之1 to lift.</summary>
    [Fact]
    public void A_non_fire_resistive_building_is_inapplicable()
    {
        var exemption = Exemption(fireResistive: false, "A-1", ZoneUses.Auditorium, cannotBeSubdivided: true);

        Assert.True(exemption.IsInapplicable);
        Assert.False(exemption.Holds);
        Assert.Equal(Article79_1Clause.None, exemption.Clause);
        Assert.Equal(Article79_1Gap.None, exemption.Gaps);
        Assert.Equal("非防火構造建築物，第79條第1項本不適用，無免除可言。", exemption.Description);
    }

    /// <summary>Without 防火構造 it is not even known whether 第79條第1項 applies, let alone 第79條之1.</summary>
    [Fact]
    public void A_missing_construction_is_insufficient_data()
    {
        var exemption = Exemption(fireResistive: null, "A-1", ZoneUses.Auditorium, cannotBeSubdivided: true);

        Assert.True(exemption.IsUndecided);
        Assert.Equal(Article79_1Gap.FireResistiveConstruction, exemption.Gaps);
        Assert.Equal("缺建築物防火構造，無法判定是否符合第79條之1。", exemption.Description);
    }

    [Fact]
    public void An_auditorium_of_group_a1_with_the_declaration_holds()
    {
        var exemption = Exemption(fireResistive: true, "A-1", ZoneUses.Auditorium, cannotBeSubdivided: true);

        Assert.True(exemption.Holds);
        Assert.Equal(Article79_1Clause.FirstClause, exemption.Clause);
        Assert.Equal(Article79_1Gap.None, exemption.Gaps);
        Assert.StartsWith("符合第一款（觀眾席、用途類組 A-1、設計者宣告無法區劃分隔）。", exemption.Description, StringComparison.Ordinal);
    }

    /// <summary>第一款 names two groups, and 「Ａ－１組」-style spellings reach them both (第83條 proviso work).</summary>
    [Theory]
    [InlineData("D-2")]
    [InlineData("Ｄ－２組")]
    [InlineData("D類第二組")]
    public void An_auditorium_of_group_d2_holds_too(string buildingUse)
    {
        var exemption = Exemption(fireResistive: true, buildingUse, ZoneUses.Auditorium, cannotBeSubdivided: true);

        Assert.True(exemption.Holds);
        Assert.Equal(Article79_1Clause.FirstClause, exemption.Clause);
        Assert.StartsWith("符合第一款（觀眾席、用途類組 D-2、", exemption.Description, StringComparison.Ordinal);
    }

    /// <summary>第二款 says 「Ｃ類之生產線部分」 — the class, so both of its groups.</summary>
    [Theory]
    [InlineData("C-1")]
    [InlineData("C-2")]
    public void A_production_line_holds_for_both_c_groups(string buildingUse)
    {
        var exemption = Exemption(fireResistive: true, buildingUse, ZoneUses.ProductionLine, cannotBeSubdivided: true);

        Assert.True(exemption.Holds);
        Assert.Equal(Article79_1Clause.SecondClause, exemption.Clause);
        Assert.StartsWith($"符合第二款（生產線、用途類組 {buildingUse}、", exemption.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// 教室 is qualified 「Ｄ－３組或Ｄ－４組之」, and unlike 生產線 that is two groups of Ｄ類, not all
    /// five — a Ｄ－１組 or Ｄ－５組 教室 is outside 第二款.
    /// </summary>
    [Theory]
    [InlineData("D-3", true)]
    [InlineData("D-4", true)]
    [InlineData("D-1", false)]
    [InlineData("D-5", false)]
    public void A_classroom_holds_only_for_groups_d3_and_d4(string buildingUse, bool holds)
    {
        var exemption = Exemption(fireResistive: true, buildingUse, ZoneUses.Classroom, cannotBeSubdivided: true);

        Assert.Equal(holds, exemption.Holds);
        Assert.Equal(!holds, exemption.IsInapplicable);
        if (!holds) Assert.Equal($"不符合第79條之1（教室，用途類組 {buildingUse} 非 D-3、D-4），第79條第1項照常適用。", exemption.Description);
    }

    /// <summary>
    /// 決議 5, the wide reading: 「Ｄ－３組或Ｄ－４組之」 is taken to qualify 教室 only, so a 體育館 is
    /// within 第二款 whatever the building's group is — even when the group is blank, which is why it
    /// is not a gap here. Reading it wide costs one 人工覆核; reading it narrow would hide an exemption
    /// the designer is entitled to claim, and neither reading can produce a 符合.
    /// </summary>
    [Theory]
    [InlineData("B-2")]
    [InlineData("D-1")]
    [InlineData("H-2")]
    [InlineData("")]
    [InlineData(null)]
    public void A_gymnasium_holds_whatever_the_group_is(string? buildingUse)
    {
        var exemption = Exemption(fireResistive: true, buildingUse, ZoneUses.Gymnasium, cannotBeSubdivided: true);

        Assert.True(exemption.Holds);
        Assert.Equal(Article79_1Clause.SecondClause, exemption.Clause);
        Assert.Equal(Article79_1Gap.None, exemption.Gaps);
        Assert.StartsWith("符合第二款（體育館、設計者宣告無法區劃分隔）。", exemption.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Neither 零售市場 nor 停車空間 is a Ｄ類 use, so 「Ｄ－３組或Ｄ－４組之」 cannot reach them on any
    /// reading — this one is plain text, not the asymmetry that settled 體育館.
    /// </summary>
    [Theory]
    [InlineData(ZoneUses.RetailMarket)]
    [InlineData(ZoneUses.CarPark)]
    public void A_retail_market_and_a_car_park_ignore_the_group(string zoneUse)
    {
        var exemption = Exemption(fireResistive: true, "B-2", zoneUse, cannotBeSubdivided: true);

        Assert.True(exemption.Holds);
        Assert.Equal(Article79_1Clause.SecondClause, exemption.Clause);
        Assert.StartsWith($"符合第二款（{zoneUse}、設計者宣告無法區劃分隔）。", exemption.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// 觀眾席 in a Ｂ－２組 building: 第一款 wants Ａ－１組 or Ｄ－２組, and 觀眾席 is not among 第二款's
    /// uses, so the article is settled as not reaching this 區劃 — 不適用, not 資料不足. This is §9 限制 5
    /// in practice: 建築物使用類組 is one value for the whole building, so a real Ａ－１組 觀眾席 inside a
    /// Ｂ－２組 building cannot be told apart from one that is genuinely outside 第一款.
    /// </summary>
    [Fact]
    public void An_auditorium_of_another_group_is_inapplicable()
    {
        var exemption = Exemption(fireResistive: true, "B-2", ZoneUses.Auditorium, cannotBeSubdivided: true);

        Assert.True(exemption.IsInapplicable);
        Assert.Equal(Article79_1Gap.None, exemption.Gaps);
        Assert.Equal("不符合第79條之1（觀眾席，用途類組 B-2 非 A-1、D-2），第79條第1項照常適用。", exemption.Description);
    }

    /// <summary>
    /// A 類組 that was filled in but names no group of 第3-3條 reads as 「不是這幾組」 rather than as a
    /// gap — the same reading <c>ZoneAreaLimit</c> gives 第83條's Ｈ－２組 proviso. The text is quoted
    /// back so the reviewer sees what the review actually read.
    /// </summary>
    [Fact]
    public void An_unrecognised_group_is_not_a_gap()
    {
        var exemption = Exemption(fireResistive: true, "住宿類", ZoneUses.Auditorium, cannotBeSubdivided: true);

        Assert.True(exemption.IsInapplicable);
        Assert.Equal("不符合第79條之1（觀眾席，用途類組 住宿類 非 A-1、D-2），第79條第1項照常適用。", exemption.Description);
    }

    /// <summary>
    /// 「觀眾席、類組未填」: whether 第一款 is met cannot be worked out, and nothing else has settled the
    /// article, so 資料不足 — and the message names the parameter that is missing, not just that
    /// something is.
    /// </summary>
    [Fact]
    public void A_missing_group_is_insufficient_data()
    {
        var exemption = Exemption(fireResistive: true, null, ZoneUses.Auditorium, cannotBeSubdivided: true);

        Assert.True(exemption.IsUndecided);
        Assert.Equal(Article79_1Gap.BuildingUse, exemption.Gaps);
        Assert.Equal("缺建築物用途類組，無法判定是否符合第79條之1。", exemption.Description);
    }

    /// <summary>（丙）＝否 settles the article on its own: 第79條第1項 applies as usual.</summary>
    [Fact]
    public void Without_the_declaration_nothing_holds()
    {
        var exemption = Exemption(fireResistive: true, "A-1", ZoneUses.Auditorium, cannotBeSubdivided: false);

        Assert.True(exemption.IsInapplicable);
        Assert.Equal(Article79_1Clause.None, exemption.Clause);
        Assert.Equal("不符合第79條之1（無法區劃分隔＝否），第79條第1項照常適用。", exemption.Description);
    }

    /// <summary>
    /// §9 限制 4: a Revit YESNO has no blank state, so a bound-but-unticked 防火檢討_無法區劃分隔 reads
    /// as 否 and lands here. The direction is the strict one — the default is no exemption.
    /// </summary>
    [Fact]
    public void A_declared_no_needs_no_other_fact()
    {
        var exemption = Exemption(fireResistive: null, null, ZoneUses.Auditorium, cannotBeSubdivided: false);

        Assert.True(exemption.IsInapplicable);
        Assert.Equal(Article79_1Gap.None, exemption.Gaps);
        Assert.Equal("不符合第79條之1（無法區劃分隔＝否），第79條第1項照常適用。", exemption.Description);
    }

    /// <summary>
    /// 「體育館、無法區劃分隔未填」: the one fact only a designer can state is the one that is missing.
    /// The tool must never fill it in — nothing in the model says whether a 體育館 can be cut in two
    /// and still be a 體育館.
    /// </summary>
    [Fact]
    public void A_missing_declaration_is_insufficient_data()
    {
        var exemption = Exemption(fireResistive: true, "B-2", ZoneUses.Gymnasium, cannotBeSubdivided: null);

        Assert.True(exemption.IsUndecided);
        Assert.Equal(Article79_1Gap.CannotBeSubdivided, exemption.Gaps);
        Assert.Equal("缺無法區劃分隔，無法判定是否符合第79條之1。", exemption.Description);
    }

    /// <summary>
    /// Several gaps at once are listed in the order they were read — （丙）、（甲）、（乙） — so the
    /// sentence walks the cheapest fact first, the same order §3.5 settles them in.
    /// </summary>
    [Fact]
    public void Every_missing_fact_is_named_in_reading_order()
    {
        var exemption = Exemption(fireResistive: null, null, ZoneUses.Auditorium, cannotBeSubdivided: null);

        Assert.Equal(
            Article79_1Gap.CannotBeSubdivided | Article79_1Gap.FireResistiveConstruction | Article79_1Gap.BuildingUse,
            exemption.Gaps);
        Assert.Equal("缺無法區劃分隔、建築物防火構造、建築物用途類組，無法判定是否符合第79條之1。", exemption.Description);
    }

    // --- 用字表 ---------------------------------------------------------------------------------

    /// <summary>
    /// 決議 10: a 用途 outside the six words claims nothing under 第79條之1. The check layer does not
    /// even raise a subject for such a 區劃 (that half is <c>Article79_1ExemptionCheck</c>'s); the
    /// judgement itself still has to answer, because the panel shows every 區劃.
    /// </summary>
    [Theory]
    [InlineData("辦公")]
    [InlineData("觀眾廳")]
    [InlineData("看台")]
    [InlineData("停車場")]
    [InlineData(ZoneUses.Atrium)]
    public void A_use_outside_the_list_claims_nothing(string zoneUse)
    {
        var exemption = Exemption(fireResistive: true, "A-1", zoneUse, cannotBeSubdivided: true);

        Assert.True(exemption.IsInapplicable);
        Assert.Equal($"不符合第79條之1（區劃用途「{zoneUse}」不在第79條之1兩款之列），第79條第1項照常適用。", exemption.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_unfilled_use_claims_nothing_either(string? zoneUse)
    {
        var exemption = Exemption(fireResistive: true, "A-1", zoneUse, cannotBeSubdivided: true);

        Assert.True(exemption.IsInapplicable);
        Assert.Equal("不符合第79條之1（區劃用途未填），第79條第1項照常適用。", exemption.Description);
    }

    /// <summary>
    /// 決議 7: no synonym folding on <c>zone.use</c>. 觀眾廳、看台、停車場 are plausible and are not the
    /// article's words; widening the vocabulary is an edit to <see cref="ZoneUses.Article79_1Uses"/>,
    /// which is the one place the list is written down.
    /// </summary>
    [Fact]
    public void The_vocabulary_matches_only_the_exact_wording()
    {
        Assert.Equal(
            new[] { "觀眾席", "生產線", "教室", "體育館", "零售市場", "停車空間" },
            ZoneUses.Article79_1Uses);

        Assert.All(ZoneUses.Article79_1Uses, use => Assert.True(ZoneUses.IsArticle79_1Use(use)));
        Assert.True(ZoneUses.IsArticle79_1Use(" 觀眾席 "));

        Assert.False(ZoneUses.IsArticle79_1Use("觀眾廳"));
        Assert.False(ZoneUses.IsArticle79_1Use("看台"));
        Assert.False(ZoneUses.IsArticle79_1Use("停車場"));
        Assert.False(ZoneUses.IsArticle79_1Use("教室部分"));
        Assert.False(ZoneUses.IsArticle79_1Use(""));
        Assert.False(ZoneUses.IsArticle79_1Use(null));
    }

    /// <summary>
    /// The two vocabularies stay apart: the 垂直區劃 of 第79條之2第1項 are exempt from the area limit
    /// outright, these six are not exempt from anything without a person. A word in both lists would
    /// mean one 用途 answering to both, which is exactly the confusion §7.3 splits the dropdown for.
    /// </summary>
    [Fact]
    public void The_two_vocabularies_share_no_word()
    {
        Assert.All(ZoneUses.Article79_1Uses, use => Assert.False(ZoneUses.IsVerticalCompartment(use)));
        Assert.All(ZoneUses.VerticalCompartments, use => Assert.False(ZoneUses.IsArticle79_1Use(use)));
    }

    /// <summary>
    /// 決議 5 as the vocabulary states it: exactly three of the six words carry a 類組 condition, and
    /// <see cref="ZoneUses.GroupsFor"/> answers empty for the other three — and for anything outside
    /// the list, which is why a caller reads <see cref="ZoneUses.IsArticle79_1Use"/> first.
    /// </summary>
    [Fact]
    public void Only_three_words_are_read_together_with_a_group()
    {
        Assert.Equal(new[] { "A-1", "D-2" }, ZoneUses.GroupsFor(ZoneUses.Auditorium));
        Assert.Equal(new[] { "C-1", "C-2" }, ZoneUses.GroupsFor(ZoneUses.ProductionLine));
        Assert.Equal(new[] { "D-3", "D-4" }, ZoneUses.GroupsFor(ZoneUses.Classroom));

        Assert.Empty(ZoneUses.GroupsFor(ZoneUses.Gymnasium));
        Assert.Empty(ZoneUses.GroupsFor(ZoneUses.RetailMarket));
        Assert.Empty(ZoneUses.GroupsFor(ZoneUses.CarPark));
        Assert.Empty(ZoneUses.GroupsFor("辦公"));
        Assert.Empty(ZoneUses.GroupsFor(null));
    }

    // --- 三態 -----------------------------------------------------------------------------------

    /// <summary>
    /// §3.5: there is no 符合 and no 未符合. The judgement reports exactly one of three states, and an
    /// exemption that holds is the 人工覆核 one — never a pass, and never a 免適用 on the area rules.
    /// (The mapping onto <c>ReviewStatus</c> is <c>Article79_1ExemptionCheck</c>'s, step 3.)
    /// </summary>
    [Fact]
    public void The_exemption_never_passes_and_never_fails()
    {
        bool?[] booleans = { true, false, null };
        string?[] groups = { "A-1", "D-2", "C-1", "D-3", "B-2", "住宿類", null };
        var uses = new[] { ZoneUses.Auditorium, ZoneUses.ProductionLine, ZoneUses.Classroom, ZoneUses.Gymnasium, ZoneUses.RetailMarket, ZoneUses.CarPark, "辦公", null };

        foreach (var fireResistive in booleans)
        foreach (var declared in booleans)
        foreach (var group in groups)
        foreach (var use in uses)
        {
            var exemption = Exemption(fireResistive, group, use, declared);
            var states = (exemption.Holds ? 1 : 0) + (exemption.IsUndecided ? 1 : 0) + (exemption.IsInapplicable ? 1 : 0);

            Assert.Equal(1, states);
            Assert.False(string.IsNullOrWhiteSpace(exemption.Description));
            Assert.Equal(exemption.Description, exemption.ToString());
            Assert.DoesNotContain("符合豁免條件", exemption.Description);
            if (exemption.Holds) Assert.Contains("人工覆寫", exemption.Description);
        }
    }

    /// <summary>
    /// §3.7 landing in the result text: an exemption that holds says what （丁） still needs, because
    /// the person who overrides the 區劃面積 result is the one who has to look at those two things.
    /// </summary>
    [Fact]
    public void The_holding_result_names_what_a_person_must_confirm()
    {
        var exemption = Exemption(fireResistive: true, "A-1", ZoneUses.Auditorium, cannotBeSubdivided: true);

        Assert.Contains("自成一個區劃", exemption.Description);
        Assert.Contains("阻熱性", exemption.Description);
        Assert.Contains("人工覆寫", exemption.Description);
        Assert.Contains(Article79_1Exemption.PersonMustConfirm, exemption.Description);
    }

    /// <summary>
    /// 第83條 is another article and 第79條之1 says 「不受前條第一項之限制」, so nothing here names 第83條
    /// — an eleventh-storey 觀眾席 is reviewed under 第83條 and this judgement never reaches it
    /// (§2.3; the rule-engine half is in <c>Article79_1AreaExemptionTests</c>).
    /// </summary>
    [Fact]
    public void Nothing_here_speaks_of_article_83()
    {
        foreach (var use in ZoneUses.Article79_1Uses)
        {
            Assert.DoesNotContain("第83條", Exemption(true, "A-1", use, true).Description);
            Assert.DoesNotContain("第83條", Exemption(true, "A-1", use, null).Description);
            Assert.DoesNotContain("第83條", Exemption(true, "A-1", use, false).Description);
        }
    }

    private static Article79_1Exemption Exemption(
        bool? fireResistive, string? buildingUse, string? zoneUse, bool? cannotBeSubdivided) =>
        Article79_1Exemption.For(fireResistive, buildingUse, zoneUse, cannotBeSubdivided);
}
