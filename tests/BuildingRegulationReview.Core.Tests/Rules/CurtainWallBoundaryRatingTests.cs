using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Core.Tests.Candidates;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using BuildingRegulationReview.Domain.Rules.Expressions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Rules;

/// <summary>
/// 第79條第1項's 區劃牆壁 requirement must not reach a 帷幕牆.
///
/// <para>
/// In Revit a curtain wall is a <c>Wall</c> (<c>WallKind.Curtain</c>), so it is collected as a
/// <see cref="MemberObservation"/> like any other wall and — because
/// <c>CandidateResolver.RelateLinear</c> decides on geometry alone — it is marked
/// <c>ZoneRelationKind.Boundary</c> whenever its centreline runs along a 區劃 boundary. Before
/// <c>tw-bcr-79-wall-rating</c> version 2 that was enough to demand 一小時以上防火時效 of it, and a
/// real model produced exactly that: a 2.5 cm curtain wall running 7.05 m along 區劃 1 came back
/// 資料不足 against 60 min.
/// </para>
///
/// <para>
/// That is a misreading of the article. 第79條第1項's 牆壁 is what divides one 區劃 from another; a
/// curtain wall is the 外牆 and divides inside from outside. What it owes at a 區劃 boundary is
/// 第79條第3項's 交接處 construction — the 區劃牆壁 projecting 50 cm, or 90 cm of 外牆面 at the
/// junction with equal 防火時效 — and 第79條之4's 半小時 for 其他部分外牆. Both are already reviewed,
/// through <c>junction.*</c>: <c>tw-bcr-79-curtain-wall-junction</c> and
/// <c>tw-bcr-79-4-curtain-wall-other</c>. So the fix removes a requirement that was never this
/// wall's, and adds none.
/// </para>
///
/// <para>
/// 第70條 needs no such exclusion: <c>tw-bcr-70-bearing-wall-rating</c> already requires
/// <c>element.isStructural == true</c>, which a curtain wall is not.
/// </para>
/// </summary>
public sealed class CurtainWallBoundaryRatingTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private const string WallRating = "tw-bcr-79-wall-rating";
    private const string BearingWallRating = "tw-bcr-70-bearing-wall-rating";

    private static CompiledRuleSet Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return loaded.Value;
    }

    private static CompiledRule Rule(string ruleId) => Shipped().Rules.Single(x => x.RuleId == ruleId);

    /// <summary>
    /// The exclusion is in the shipped rule, and it is keyed off the室內外 classification rather than
    /// the type name — a name test would break the moment a project names a type differently.
    ///
    /// <para>
    /// Version 3 narrowed it (帷幕牆規格 §4.8). The reasoning above holds for an 外牆 curtain wall and
    /// only for one: a curtain wall the user drew <em>inside</em> the building divides one 區劃 from
    /// another, which is exactly what 第79條第1項's 牆壁 is, and excluding it left that wall's rating
    /// unanswered by any rule at all. So the condition reads the classification, not the flag:
    /// 外牆 and 室內外未定 are out, 室內 and 非帷幕牆 are in.
    /// </para>
    /// </summary>
    [Fact]
    public void The_boundary_wall_rating_rule_excludes_an_exterior_curtain_wall_only()
    {
        var rule = Rule(WallRating);

        Assert.Contains("element.curtainWallExposure", rule.AppliesWhen.Fields.Select(f => f.Name));
        Assert.Contains("element.curtainWallExposure != \"Exterior\"", rule.Rule.AppliesWhen.Source);

        // 室內外未定也不適用——但它會在帷幕牆區劃交接拿到一列「室內外未定」的人工覆核，不是靜默略過。
        Assert.Contains("element.curtainWallExposure != \"Unknown\"", rule.Rule.AppliesWhen.Source);
    }

    /// <summary>
    /// Bumped twice, because the same model answers differently each time: a boundary curtain wall
    /// read 資料不足 under version 1, 不適用 under version 2, and under version 3 an <em>interior</em>
    /// one is judged again while an 外牆 one stays 不適用. A stored result carries the version it was
    /// decided by, so leaving the version alone would make them indistinguishable.
    /// </summary>
    [Fact]
    public void The_rule_version_records_that_the_judgement_changed()
    {
        Assert.Equal("3", Rule(WallRating).Rule.Version);
    }

    /// <summary>
    /// The classification is whitelisted as a text element field, which is what lets the assembly
    /// layer set it on every wall without <c>ApplyTo</c> throwing.
    /// </summary>
    [Fact]
    public void The_exposure_is_a_whitelisted_element_field()
    {
        var field = RuleFieldCatalog.Default.Fields.Single(x => x.Name == "element.curtainWallExposure");

        Assert.True(field.IsAvailableIn(RuleCategory.FireResistance));
        Assert.Equal(RuleValueType.Text, field.Type);
    }

    /// <summary>
    /// 非帷幕牆 is an answer and not a gap, for the same reason
    /// <see cref="The_assembly_layer_answers_whether_a_boundary_wall_is_a_curtain_wall"/> spells out:
    /// the rule's own <c>appliesWhen</c> now reads this field, so a wall the pipeline left it off
    /// would turn 資料不足 — silently, because the wall still gets a row.
    /// </summary>
    [Theory]
    [InlineData(false, MemberCandidate.NotCurtainWallText)]
    [InlineData(true, "Interior")]
    public void The_assembly_layer_answers_every_boundary_walls_exposure(bool curtain, string expected)
    {
        // 兩側都有區劃的那一片牆：區劃 A 與 B 以 x = 10 為界，牆就畫在界上，所以帷幕牆判為室內。
        var wall = CandidateModel.Wall("W-mid", 10, 0, 10, 10, curtain: curtain);
        var set = CandidateResolver.Resolve(CandidateModel.Observations(
            zones: CandidateModel.Zones(),
            members: new[] { wall },
            openings: Array.Empty<OpeningObservation>()));

        var member = set.Members.Single(m => m.Source.ElementUniqueId == "W-mid");
        Assert.True(member.RelationTo(CandidateModel.ZoneA)?.IsBoundary,
            "此測試的前提是這片牆構成區劃邊界；不成立的話下面的斷言沒有意義。");

        var value = CandidateFacts.ForMember(set, member, CandidateModel.ZoneA).Find("element.curtainWallExposure");

        Assert.NotNull(value);
        Assert.Equal(expected, value!.Text);
    }

    /// <summary>
    /// The shipped rule, evaluated: this is the end of the chain the two tests above only check the
    /// ends of. An interior curtain wall is judged, an exterior one and an undecided one are not.
    /// </summary>
    [Theory]
    [InlineData("Interior", ReviewStatus.Pass)]
    [InlineData(MemberCandidate.NotCurtainWallText, ReviewStatus.Pass)]
    [InlineData("Exterior", ReviewStatus.NotApplicable)]
    [InlineData("Unknown", ReviewStatus.NotApplicable)]
    public void The_shipped_rule_judges_an_interior_curtain_wall_and_not_an_exterior_one(
        string exposure, ReviewStatus expected)
    {
        var rules = Shipped();
        var facts = new RuleFacts(rules.Catalog);
        facts.Set("building.fireResistiveConstruction", true);
        facts.Set("element.category", "Walls");
        facts.Set("element.isStructural", false);
        facts.Set("element.isCompartmentBoundary", true);
        facts.Set("element.isCurtainWall", exposure != MemberCandidate.NotCurtainWallText);
        facts.Set("element.curtainWallExposure", exposure);
        facts.Set("element.providedFireRating", 60, ReviewUnit.Minute);

        var outcome = new RuleEngine(rules).Evaluate(
            RuleCategory.FireResistance, facts, new RuleEvaluationContext(new DateTime(2025, 6, 1), "TW"));

        Assert.Equal(expected, outcome.Status);
    }

    /// <summary>
    /// A 2.5 cm interior curtain wall with nothing filled in reads 資料不足, not 不適用: that is the
    /// whole point of version 3 — the wall is asked the question instead of being let off it.
    /// </summary>
    [Fact]
    public void An_interior_curtain_wall_without_a_rating_is_insufficient_data_not_exempt()
    {
        var rules = Shipped();
        var facts = new RuleFacts(rules.Catalog);
        facts.Set("building.fireResistiveConstruction", true);
        facts.Set("element.category", "Walls");
        facts.Set("element.isStructural", false);
        facts.Set("element.isCompartmentBoundary", true);
        facts.Set("element.isCurtainWall", true);
        facts.Set("element.curtainWallExposure", "Interior");

        var outcome = new RuleEngine(rules).Evaluate(
            RuleCategory.FireResistance, facts, new RuleEvaluationContext(new DateTime(2025, 6, 1), "TW"));

        Assert.Equal(ReviewStatus.InsufficientData, outcome.Status);
    }

    /// <summary>
    /// A column carries no <c>element.curtainWallExposure</c> — the fact is only set for walls — and
    /// that must not make 第79條's wall rule 適用性未定 for it.
    ///
    /// <para>
    /// It does not, because <c>&amp;&amp;</c> is three-valued: <c>element.category == "Walls"</c> is
    /// decisively false for a column, so the missing field downstream of it cannot change the answer
    /// (<c>LogicalNode.Evaluate</c>). This test exists because the opposite would be the worst kind of
    /// regression — silent, and on every column and beam in the project — and because it is the thing
    /// that would break if the condition were ever reordered to read the exposure first.
    /// </para>
    /// </summary>
    [Fact]
    public void A_column_without_an_exposure_fact_is_not_left_undecided_by_the_wall_rule()
    {
        var rules = Shipped();
        var facts = new RuleFacts(rules.Catalog);
        facts.Set("building.fireResistiveConstruction", true);
        facts.Set("element.category", "Columns");
        facts.Set("element.isStructural", true);
        facts.Set("element.providedFireRating", 60, ReviewUnit.Minute);
        facts.Set("building.floorsAboveGround", 3, ReviewUnit.None);
        facts.Set("zone.floorNumber", 1, ReviewUnit.None);

        var outcome = new RuleEngine(rules).Evaluate(
            RuleCategory.FireResistance, facts, new RuleEvaluationContext(new DateTime(2025, 6, 1), "TW"));

        Assert.False(outcome.IsApplicabilityUndecided);
        Assert.Equal(ReviewStatus.Pass, outcome.Status);
        Assert.Contains("第70條", outcome.LegalReference);
    }

    /// <summary>
    /// 第70條 is untouched — its own 承重牆壁 condition already keeps a curtain wall out, so the
    /// exclusion belongs in one rule and not two.
    /// </summary>
    [Fact]
    public void The_article_70_bearing_wall_rule_needs_no_curtain_wall_exclusion()
    {
        var rule = Rule(BearingWallRating);

        Assert.Contains("element.isStructural == true", rule.Rule.AppliesWhen.Source);
        Assert.DoesNotContain("element.isCurtainWall", rule.AppliesWhen.Fields.Select(f => f.Name));
    }

    /// <summary>
    /// The two rules that do hold a 帷幕牆 to something are still there. Removing the 第79條第1項
    /// requirement would be wrong if it left the wall unreviewed, and this is what says it does not.
    /// </summary>
    [Fact]
    public void The_curtain_wall_is_still_reviewed_by_the_junction_rules()
    {
        var ids = Shipped().Rules.Select(x => x.RuleId).ToList();

        Assert.Contains("tw-bcr-79-curtain-wall-junction", ids);
        Assert.Contains("tw-bcr-79-3-curtain-wall-spandrel", ids);
        Assert.Contains("tw-bcr-79-4-curtain-wall-other", ids);
    }

    /// <summary>
    /// The field is whitelisted for the element category, which is what lets the assembly layer set
    /// it on every member without the ApplyTo throwing.
    /// </summary>
    [Fact]
    public void The_curtain_wall_flag_is_a_whitelisted_element_field()
    {
        var field = RuleFieldCatalog.Default.Fields.Single(x => x.Name == "element.isCurtainWall");

        Assert.True(field.IsAvailableIn(RuleCategory.FireResistance));
        Assert.Equal(RuleValueType.Boolean, field.Type);
    }

    /// <summary>
    /// The assembly layer answers this for every member, through the real
    /// <see cref="CandidateFacts.ForMember"/> path and not a hand-built fact set.
    ///
    /// <para>
    /// This is the test that matters most here. 「不是帷幕牆」 is an answer and not a gap, so the fact
    /// has to be set unconditionally: the moment the pipeline leaves it out, the rule can no longer
    /// decide its own <c>appliesWhen</c> and <em>every</em> ordinary boundary wall in the project
    /// turns 資料不足 — a far worse failure than the one this change fixes, and a silent one, because
    /// each wall still gets a row. <c>FireReviewIntegrationTests</c> showed exactly that shape when
    /// its hand-built facts had not been told yet.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_assembly_layer_answers_whether_a_boundary_wall_is_a_curtain_wall(bool curtain)
    {
        var wall = CandidateModel.Wall("W-edge", 0, 0, 0, 10, curtain: curtain);
        var set = CandidateResolver.Resolve(CandidateModel.Observations(
            zones: CandidateModel.Zones().Take(1),
            members: new[] { wall },
            openings: Array.Empty<OpeningObservation>()));

        var member = set.Members.Single(m => m.Source.ElementUniqueId == "W-edge");
        Assert.True(member.RelationTo(CandidateModel.ZoneA)?.IsBoundary,
            "此測試的前提是這片牆構成區劃邊界；不成立的話下面的斷言沒有意義。");

        var value = CandidateFacts.ForMember(set, member, CandidateModel.ZoneA).Find("element.isCurtainWall");

        Assert.NotNull(value);
        Assert.Equal(curtain, value!.Flag);
    }
}
