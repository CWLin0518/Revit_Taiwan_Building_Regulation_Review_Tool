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
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Checks;

/// <summary>
/// 挑空 and the 區劃面積 rules after 第79條之2第3項 (docs/regulations/vertical-compartment.md 決議 31～33),
/// end to end on the shipped rule set: <see cref="CompartmentAreaCheck"/> derives the three
/// <c>zone.atrium*</c> facts, and the two atrium rules decide on them.
/// </summary>
/// <remarks>
/// A 挑空 that 第3項 does not exempt stays a 第79條之2 垂直區劃 and keeps the use exemption. One that
/// 第3項 does exempt is no longer a 垂直區劃, so 第83條本文's 除外 no longer covers it, and — as
/// 內政部106年9月27日內授營建管字第1060814830號函 reads it — the 合計 of its 連通區劃 is reviewed against
/// 第79條, and against 第83條 when the storeys it spans reach the eleventh.
/// </remarks>
public sealed class AtriumAreaReviewTests
{
    private static readonly RuleEvaluationContext Today = new(new DateTime(2025, 6, 1), "TW");
    private static readonly Guid RunId = Guid.Parse("99999999-0000-0000-0000-000000000002");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    // --- 第3項不成立：仍是垂直區劃，照舊免面積檢討 ----------------------------------------------------

    [Fact]
    public void An_atrium_the_third_paragraph_does_not_exempt_keeps_the_use_exemption()
    {
        var finding = Review(Atrium(floor: 2, links: false, spanned: 5, connected: 4000));

        Assert.Equal(ReviewStatus.NotApplicable, finding.Status);
        Assert.Equal(RuleOutcomeReason.Exempt, finding.Outcome!.Reason);
        Assert.Equal("tw-bcr-79-area", finding.Result.RuleId);
        Assert.Contains(ZoneUses.VerticalCompartmentHandoff, finding.Result.Message, StringComparison.Ordinal);
    }

    // --- 第3項成立：連通區劃之合計面積回到第79條 --------------------------------------------------------

    /// <summary>
    /// 第二款 holds (三層、合計 1,200 ㎡), so the 挑空 is not 單獨區劃分隔 and its 連通區劃 is one 區劃
    /// under 第79條 — measured on the 合計, not on the 挑空's own 100 ㎡.
    /// </summary>
    [Fact]
    public void An_exempt_atrium_has_its_connected_total_reviewed_under_article_79()
    {
        var finding = Review(Atrium(floor: 2, links: false, spanned: 3, connected: 1200));

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal("tw-bcr-79-area-atrium", finding.Result.RuleId);
        Assert.Equal(ReviewValue.Quantity(1200, ReviewUnit.SquareMeter), finding.Result.ActualValue);
        Assert.Equal(ReviewValue.Quantity(1500, ReviewUnit.SquareMeter), finding.Result.RequiredValue);
        Assert.DoesNotContain("第83條", finding.Result.LegalReference, StringComparison.Ordinal);
        Assert.Equal(ReviewValue.OfBoolean(true), finding.Result.Evidence.Find("zone.atriumMerged"));
    }

    /// <summary>
    /// 第一款 has no area condition of its own, so an atrium exempt under it can connect more than
    /// 1,500 ㎡ — and that is exactly the 區劃 第79條 then catches. 有效自動滅火設備 doubles the limit as
    /// it does for any 區劃 (防火區劃與挑空規則 §4.3 的挑空正下方有效範圍 is not modelled, §9).
    /// </summary>
    [Theory]
    [InlineData(false, ReviewStatus.Fail)]
    [InlineData(true, ReviewStatus.Pass)]
    public void An_atrium_exempt_under_the_first_clause_can_still_fail_article_79(bool sprinklered, ReviewStatus expected)
    {
        var finding = Review(Atrium(floor: 1, links: true, finish: InteriorFinishGrades.ClassOne,
            spanned: 5, connected: 2000, sprinklered: sprinklered));

        Assert.Equal(expected, finding.Status);
        Assert.Equal("tw-bcr-79-area-atrium", finding.Result.RuleId);
    }

    /// <summary>第一款 holds without any 連通區劃面積, but 第79條 cannot be measured without one.</summary>
    [Fact]
    public void An_exempt_atrium_with_no_connected_area_is_insufficient_data()
    {
        var finding = Review(Atrium(floor: 1, links: true, finish: InteriorFinishGrades.ClassOne, spanned: 2, connected: null));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Equal("tw-bcr-79-area-atrium", finding.Result.RuleId);
        Assert.Contains(finding.Outcome!.Gaps, g => g.Field == "zone.atriumCompartmentArea");
    }

    // --- 所跨樓層含第十一層以上：第83條 ---------------------------------------------------------------

    /// <summary>
    /// 函第4點: 即使連跨三層以下，所跨樓層含第十一層以上者仍應依第83條. An atrium on 9F spanning three
    /// storeys reaches 11F, so its 合計 600 ㎡ is held to 第83條第一款's 100 ㎡ although it sits on 9F.
    /// </summary>
    [Fact]
    public void An_exempt_atrium_reaching_the_eleventh_storey_is_held_to_article_83()
    {
        var finding = Review(Atrium(floor: 9, links: false, spanned: 3, connected: 600, finish: InteriorFinishGrades.None));

        Assert.Equal(ReviewStatus.Fail, finding.Status);
        Assert.Equal("tw-bcr-83-area-atrium", finding.Result.RuleId);
        Assert.Contains("第83條", finding.Result.LegalReference, StringComparison.Ordinal);
        Assert.Equal(ReviewValue.Quantity(100, ReviewUnit.SquareMeter), finding.Result.RequiredValue);
        Assert.Equal(ReviewValue.Quantity(11, ReviewUnit.None), finding.Result.Evidence.Find("zone.atriumTopFloor"));
    }

    /// <summary>One storey lower it stops at 10F, and 第79條 decides.</summary>
    [Fact]
    public void An_exempt_atrium_that_stops_at_the_tenth_storey_stays_with_article_79()
    {
        var finding = Review(Atrium(floor: 8, links: false, spanned: 3, connected: 600, finish: InteriorFinishGrades.None));

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal("tw-bcr-79-area-atrium", finding.Result.RuleId);
    }

    /// <summary>
    /// On 12F the storey alone decides — 「所在樓層 ≥ 11」 is enough, so a 連跨樓層數 nobody stated does
    /// not hold 第83條 up; 耐燃一級 gives 第二款's 200 ㎡.
    /// </summary>
    [Fact]
    public void An_exempt_atrium_above_the_tenth_storey_needs_no_span_to_be_held_to_article_83()
    {
        var finding = Review(Atrium(floor: 12, links: true, finish: InteriorFinishGrades.ClassOne, spanned: null, connected: 150));

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal("tw-bcr-83-area-atrium", finding.Result.RuleId);
        Assert.Equal(ReviewValue.Quantity(200, ReviewUnit.SquareMeter), finding.Result.RequiredValue);
    }

    /// <summary>Below 11F, whether 第83條 reaches it turns on 連跨樓層數, so without one it is 資料不足.</summary>
    [Fact]
    public void An_exempt_atrium_below_the_eleventh_storey_with_no_span_is_insufficient_data()
    {
        var finding = Review(Atrium(floor: 9, links: true, finish: InteriorFinishGrades.ClassOne, spanned: null, connected: 150));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Contains(finding.Outcome!.Gaps, g => g.Field == "zone.atriumTopFloor");
    }

    // --- 逐層各放一個 Area（決議 34）-----------------------------------------------------------------

    /// <summary>
    /// The case the code review found: a 挑空 spanning 8F～10F drawn as one Area per storey. Counted
    /// from each Area's own storey, the 9F and 10F Areas would "reach" 11F and 12F and be held to
    /// 第83條's 200 ㎡; counted from the 挑空's lowest storey, every Area stops at 10F and 第79條's
    /// 1,500 ㎡ decides.
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void Every_storey_of_a_per_storey_atrium_counts_from_the_atriums_lowest_storey(int areaFloor)
    {
        var finding = Review(Atrium(floor: areaFloor, links: false, spanned: 3, connected: 1200,
            finish: InteriorFinishGrades.ClassOne, baseFloor: 8));

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal("tw-bcr-79-area-atrium", finding.Result.RuleId);
        Assert.Equal(ReviewValue.Quantity(10, ReviewUnit.None), finding.Result.Evidence.Find("zone.atriumTopFloor"));
    }

    /// <summary>An Area outside the storeys its own facts declare is a contradiction, said as one.</summary>
    [Fact]
    public void An_area_outside_the_declared_span_is_insufficient_data_that_says_why()
    {
        var finding = Review(Atrium(floor: 5, links: false, spanned: 2, connected: 600, baseFloor: 2));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        var gap = Assert.Single(finding.Outcome!.Gaps, g => g.Field == "zone.atriumTopFloor");
        Assert.Equal(RuleFactGapKind.Unreadable, gap.Kind);
        Assert.Contains("不在挑空起始樓層序 2 起連跨 2 層的範圍", gap.Reason, StringComparison.Ordinal);
    }

    /// <summary>Below 11F, without a 挑空起始樓層序 nobody can tell which storeys it spans.</summary>
    [Fact]
    public void An_exempt_atrium_below_the_eleventh_storey_with_no_base_floor_is_insufficient_data()
    {
        var finding = Review(Atrium(floor: 9, links: false, spanned: 3, connected: 600, baseFloor: null));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Contains(finding.Outcome!.Gaps, g => g.Field == "zone.atriumTopFloor");
    }

    /// <summary>
    /// 「同一區劃各 Area 填得不一致」 reaches the 區劃面積 result with its reason, rather than as a bare
    /// 「未設定」 that sends the user looking for a box that is filled in.
    /// </summary>
    [Fact]
    public void A_connected_area_the_areas_disagree_on_passes_its_reason_on()
    {
        var finding = Review(Atrium(floor: 1, links: true, finish: InteriorFinishGrades.ClassOne, spanned: 2, connected: null,
            extra: new[] { ReviewInput.Unreadable("zone.connectedArea", "此區劃的 2 個面積填寫不一致（800、900）") }));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        var gap = Assert.Single(finding.Outcome!.Gaps, g => g.Field == "zone.atriumCompartmentArea");
        Assert.Contains("填寫不一致", gap.Reason, StringComparison.Ordinal);
    }

    // --- 面積核對（code review 第 1 項）-------------------------------------------------------------

    /// <summary>
    /// A merged 挑空 is compared on the declared 合計, so whether its own Area agrees with its boundary
    /// cannot withhold the verdict: a 未符合 stays 未符合, with the disagreement noted beside it.
    /// </summary>
    [Fact]
    public void A_boundary_disagreement_does_not_withhold_a_merged_atriums_verdict()
    {
        var finding = Review(Atrium(floor: 2, links: true, finish: InteriorFinishGrades.ClassOne, spanned: 2, connected: 2000),
            revitSquareMeters: 150);

        Assert.Equal(AreaCrossCheck.Differs, finding.CrossCheck);
        Assert.Equal(ReviewStatus.Fail, finding.Status);
        Assert.Equal("tw-bcr-79-area-atrium", finding.Result.RuleId);
        Assert.Contains("另外", finding.Result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("不判定", finding.Result.Message, StringComparison.Ordinal);
    }

    /// <summary>Any other 區劃 still has its verdict withheld when the two areas disagree.</summary>
    [Fact]
    public void A_boundary_disagreement_still_withholds_an_ordinary_zones_verdict()
    {
        var finding = Review(Inputs("辦公", floor: 2, sprinklered: false), revitSquareMeters: 150);

        Assert.Equal(AreaCrossCheck.Differs, finding.CrossCheck);
        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
    }

    // --- 第83條歸類（code review 第 2 項）-----------------------------------------------------------

    /// <summary>
    /// A 2F 挑空 whose 第3項 is undecided is in doubt under <c>tw-bcr-83-area-atrium</c>, so its result
    /// carries that rule's 第83條 法源 — but the rule decided nothing, and the outcome says so. The
    /// runner reads that flag before it calls the 區劃 第83條's for the 帷幕牆 junctions.
    /// </summary>
    [Fact]
    public void An_undecided_atrium_names_article_83_but_is_marked_as_deciding_nothing()
    {
        var undecided = Review(Atrium(floor: 2, links: false, spanned: null, connected: 900));
        Assert.Contains("第83條", undecided.Result.LegalReference, StringComparison.Ordinal);
        Assert.True(undecided.Outcome!.IsApplicabilityUndecided);

        var decided = Review(Atrium(floor: 9, links: false, spanned: 3, connected: 600, finish: InteriorFinishGrades.None));
        Assert.Equal("tw-bcr-83-area-atrium", decided.Result.RuleId);
        Assert.False(decided.Outcome!.IsApplicabilityUndecided);

        var missingArea = Review(Atrium(floor: 1, links: true, finish: InteriorFinishGrades.ClassOne, spanned: 2, connected: null));
        Assert.Equal(ReviewStatus.InsufficientData, missingArea.Status);
        Assert.False(missingArea.Outcome!.IsApplicabilityUndecided);
    }

    // --- 第3項無法判定 ------------------------------------------------------------------------------

    /// <summary>
    /// Whether the use exemption still covers the 挑空 is exactly what 第3項 decides, so an undecided
    /// 第3項 is an undecided area result — the price 決議 26 refused and 決議 32 accepts.
    /// </summary>
    [Fact]
    public void An_atrium_whose_exemption_is_undecided_is_insufficient_data()
    {
        var finding = Review(Atrium(floor: 2, links: false, spanned: null, connected: 900));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Contains(finding.Outcome!.Gaps, g => g.Field == "zone.atriumMerged");
        Assert.Null(finding.Result.Evidence.Find("zone.atriumMerged"));
    }

    // --- 其他區劃不受影響 ---------------------------------------------------------------------------

    [Theory]
    [InlineData("辦公")]
    [InlineData(null)]
    [InlineData(ZoneUses.Stairwell)]
    public void Any_zone_that_is_not_an_atrium_is_left_to_the_two_ordinary_rules(string? use)
    {
        var finding = Review(Inputs(use, floor: 2, sprinklered: false));

        // A blank 用途 included: nobody said it is a 挑空, so zone.atriumMerged is false rather than
        // missing, and the atrium rules never make an ordinary 區劃 資料不足.
        Assert.Equal("tw-bcr-79-area", finding.Result.RuleId);
        Assert.NotEqual(ReviewStatus.InsufficientData, finding.Status);
    }

    // --- 欄位與輸入 ---------------------------------------------------------------------------------

    /// <summary>
    /// The derived facts are the check's, never an input's: supplying one would let a parameter
    /// overrule 第3項's judgement. They have no parameter to come from, so the two rules that read them
    /// never make 連跨樓層數 or 連通區劃面積 a 前置檢查 blocker for a project with no 挑空 (決議 27).
    /// </summary>
    [Theory]
    [InlineData("zone.atriumMerged")]
    [InlineData("zone.atriumTopFloor")]
    [InlineData("zone.atriumCompartmentArea")]
    public void The_derived_atrium_facts_cannot_be_supplied_and_have_no_parameter(string field)
    {
        Assert.Contains(field, CompartmentAreaInputs.ModelOwnedFields);
        Assert.Null(ReviewInputSources.For(field));

        var needed = ReviewInputSources.NeededBy(Shipped()).Select(s => s.Field).ToList();
        Assert.DoesNotContain("zone.spannedFloors", needed);
        Assert.DoesNotContain("zone.connectedArea", needed);
        Assert.DoesNotContain("zone.atriumBaseFloor", needed);
    }

    /// <summary>樓層序 skips 0: B2 spanning three storeys reaches 1F, not 0.</summary>
    [Theory]
    [InlineData(9, 3, 11)]
    [InlineData(1, 1, 1)]
    [InlineData(-2, 3, 1)]
    [InlineData(-3, 2, -2)]
    public void The_top_floor_counts_storeys_and_skips_zero(int floor, int spanned, int top)
    {
        Assert.Equal(top, CompartmentAreaCheck.TopFloor(floor, spanned));
    }

    // --- helpers ------------------------------------------------------------------------------------

    /// <summary>
    /// A 區 as one 挑空 Area on <paramref name="floor"/>. Unless a test says otherwise the Area sits on
    /// the 挑空's lowest storey, so 挑空起始樓層序 is the Area's own 所在樓層序.
    /// </summary>
    private static CompartmentAreaInputs Atrium(
        int floor,
        bool? links,
        int? spanned,
        double? connected,
        string? finish = null,
        bool sprinklered = false,
        int? baseFloor = -999,
        IEnumerable<ReviewInput>? extra = null)
    {
        var zone = new List<ReviewInput>
        {
            ReviewInput.Known("zone.use", ZoneUses.Atrium),
            ReviewInput.Known("zone.floorNumber", floor, ReviewUnit.None),
            ReviewInput.Known("zone.sprinklered", sprinklered)
        };
        if (links is bool l) zone.Add(ReviewInput.Known("zone.linksRefugeFloor", l));
        if (spanned is int s) zone.Add(ReviewInput.Known("zone.spannedFloors", s, ReviewUnit.None));
        if (connected is double c) zone.Add(ReviewInput.Known("zone.connectedArea", c, ReviewUnit.SquareMeter));
        if (finish is not null) zone.Add(ReviewInput.Known("zone.interiorFinish", finish));
        var lowest = baseFloor == -999 ? floor : baseFloor;
        if (lowest is int b) zone.Add(ReviewInput.Known("zone.atriumBaseFloor", b, ReviewUnit.None));
        if (extra is not null) zone.AddRange(extra);
        return Context(zone);
    }

    private static CompartmentAreaInputs Inputs(string? use, int floor, bool sprinklered)
    {
        var zone = new List<ReviewInput>
        {
            ReviewInput.Known("zone.floorNumber", floor, ReviewUnit.None),
            ReviewInput.Known("zone.sprinklered", sprinklered)
        };
        if (use is not null) zone.Add(ReviewInput.Known("zone.use", use));
        return Context(zone);
    }

    private static CompartmentAreaInputs Context(IEnumerable<ReviewInput> zone) =>
        new(new[]
            {
                ReviewInput.Known("building.fireResistiveConstruction", true),
                ReviewInput.Known("building.use", "B-2"),
                ReviewInput.Known("building.floorsAboveGround", 15, ReviewUnit.None)
            },
            new Dictionary<Guid, IEnumerable<ReviewInput>> { [ZoneA] = zone });

    /// <summary>A 區 alone, 10 m × 10 m — the 挑空's own 100 ㎡, which no atrium rule reads.</summary>
    private static ZoneAreaFinding Review(CompartmentAreaInputs inputs, double revitSquareMeters = 100)
    {
        var set = CandidateResolver.Resolve(new CandidateObservationSet(PackageId, "level-1F", "1F",
            new[]
            {
                new ZoneObservation(ZoneA, "A 區", new[]
                {
                    new ZonePartObservation("area-a", new[] { Rect(0, 0, 10, 10) },
                        PlanUnits.SquareMetersToSquareFeet(revitSquareMeters))
                })
            },
            Array.Empty<MemberObservation>(), Array.Empty<OpeningObservation>()));

        var review = CompartmentAreaCheck.Review(set, inputs, new RuleEngine(Shipped()), Today, RunId);
        Assert.True(review.IsSuccess, review.IsSuccess ? string.Empty : review.Error.ToString());
        return Assert.Single(review.Value.Findings);
    }

    private static CompiledRuleSet Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return loaded.Value;
    }
}
