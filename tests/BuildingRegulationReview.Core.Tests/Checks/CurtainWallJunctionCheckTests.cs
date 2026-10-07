using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Checks;

/// <summary>
/// 防火區劃與帷幕牆交接的 Check 層（docs/regulations/curtain-wall-fire-compartment.md §12 步驟 3）。
///
/// These run against the shipped rule set, not a hand-written one: the point of the check is that it
/// adds no judgement of its own. It turns what the geometry measured into facts, lets the rules
/// decide, and keeps the numbers — so the 20 cases of §10 that do not need real geometry can be
/// walked through here as fixtures.
/// </summary>
public sealed class CurtainWallJunctionCheckTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private static readonly RuleEvaluationContext Today = new(new DateTime(2026, 9, 25), "TW");
    private static readonly Guid RunId = Guid.Parse("99999999-0000-0000-0000-00000000000c");

    private static RuleEngine Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return new RuleEngine(loaded.Value);
    }

    private static ZoneObservation RectZone(Guid id, string name, string areaUid, double x0 = 0) =>
        new(id, name, new[]
        {
            new ZonePartObservation(areaUid, new[] { Rect(x0, 0, x0 + 30, 30) }, PlanUnits.SquareMetersToSquareFeet(900))
        });

    private static CandidateSet Set(params ZoneObservation[] zones) =>
        CandidateResolver.Resolve(new CandidateObservationSet(PackageId, "level-1F", "1F",
            zones.Length == 0 ? new[] { RectZone(ZoneA, "A 區", "area-a") } : zones,
            Array.Empty<MemberObservation>(), Array.Empty<OpeningObservation>()));

    private static CompartmentAreaInputs Context(bool? fireResistive = true) =>
        new(fireResistive is bool f
                ? new[] { ReviewInput.Known("building.fireResistiveConstruction", f, "專案設定") }
                : Array.Empty<ReviewInput>(),
            null);

    private static CurtainWallJunctionReview Review(
        IEnumerable<CurtainWallJunction> junctions,
        CandidateSet? set = null,
        bool? fireResistive = true,
        CompartmentAreaInputs? context = null)
    {
        var ids = Enumerable.Range(1, 100).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).GetEnumerator();
        var review = CurtainWallJunctionCheck.Review(set ?? Set(),
            new CurtainWallJunctionInputs(context ?? Context(fireResistive), junctions),
            Shipped(), Today, RunId, null, () => { ids.MoveNext(); return ids.Current; });
        Assert.True(review.IsSuccess, review.IsSuccess ? string.Empty : review.Error.ToString());
        return review.Value;
    }

    private static CurtainWallJunctionFinding Single(IEnumerable<CurtainWallJunction> junctions, bool? fireResistive = true) =>
        Assert.Single(Review(junctions, fireResistive: fireResistive).Findings);

    /// <summary>CW-H：一個 60 min 區劃牆與帷幕牆的交點，突出與連續長度由呼叫者給定。</summary>
    private static CurtainWallJunction Wall(
        double projectionMm,
        double? runMm = null,
        double? panelMinutes = 60,
        string reference = CurtainWallJunctionReferences.Article79,
        string id = "j-h1") =>
        CurtainWallJunction.WallJunction(id, ZoneA, "cw-1", "wall-1", projectionMm, runMm,
            hostRequiredFireRatingMinutes: 60,
            minFireRating: panelMinutes is double m ? ProvidedFireRating.Rated(m) : null,
            hostLegalReference: reference,
            panelUniqueIds: new[] { "panel-1", "panel-2" });

    /// <summary>CW-V：一個層間帶，樓地板要求時效由呼叫者給定（自頂層起算第 5 層以上為 120 min）。</summary>
    private static CurtainWallJunction Spandrel(
        double projectionMm,
        double? bandMm = null,
        double? panelMinutes = 60,
        double floorRequiredMinutes = 60,
        ProvidedFireRating? rating = null,
        string id = "j-v1") =>
        CurtainWallJunction.Spandrel(id, ZoneA, "cw-1", "floor-1", projectionMm, bandMm,
            hostRequiredFireRatingMinutes: floorRequiredMinutes,
            minFireRating: rating ?? (panelMinutes is double m ? ProvidedFireRating.Rated(m) : null),
            panelUniqueIds: new[] { "panel-3" });

    private static double Number(ReviewValue? value) => Assert.IsType<ReviewValue>(value).Number;

    // --- CW-H 區劃牆 × 帷幕牆（§10 案例 1–9） -------------------------------------------------------

    [Fact]
    public void Case1_a_compartment_wall_projecting_600mm_passes()
    {
        var finding = Single(new[] { Wall(600) });

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal("tw-bcr-79-curtain-wall-junction", finding.Result.RuleId);
        Assert.Equal(ReviewCheckTypes.CompartmentContinuity, finding.Result.CheckType);
        Assert.Equal(ZoneA.ToString("D"), finding.Result.ZoneId);
        Assert.Equal(PackageId, finding.Result.PackageId);
        Assert.Equal(RunId, finding.Result.RunId);
        Assert.Null(finding.ErrorCode);
        Assert.False(finding.IsExempt);

        // The measurement goes in as metres, the unit the rule field declares.
        Assert.Equal(0.6, Number(finding.Result.ActualValue), 6);
        Assert.Equal(ReviewUnit.Meter, finding.Result.ActualValue!.Unit);
        Assert.Equal(0.5, Number(finding.Result.RequiredValue), 6);
    }

    [Fact]
    public void Case2_a_wall_projecting_499mm_with_no_fire_rated_run_fails()
    {
        var finding = Single(new[] { Wall(499, runMm: 0) });

        Assert.Equal(ReviewStatus.Fail, finding.Status);
        Assert.Null(finding.ErrorCode);
    }

    [Theory]
    // 案例 3、4、7：但書採總和，900 mm 成立、899 mm 不成立。
    [InlineData(900, ReviewStatus.NotApplicable)]
    [InlineData(899, ReviewStatus.Fail)]
    public void Case3_and_4_the_900mm_run_exempts_a_wall_that_does_not_project(double runMm, ReviewStatus expected)
    {
        var finding = Single(new[] { Wall(0, runMm) });

        Assert.Equal(expected, finding.Status);
        Assert.Equal(expected == ReviewStatus.NotApplicable, finding.IsExempt);
    }

    [Fact]
    public void Case5_and_6_an_under_rated_panel_or_an_unprotected_opening_shortens_the_run()
    {
        // 案例 5：1200 mm 的帶裡有一片 30 min，不計入；案例 6：帶內有未受防護窗，累積在該處中斷。
        // 兩者到 Check 層都是「連續段不足 900 mm」——長度的定義本身已經把時效與開口算進去（§5.4）。
        var underRated = CurtainWallJunction.WallJunction("j-under", ZoneA, "cw-1", "wall-1", 0, 700,
            hostRequiredFireRatingMinutes: 60, minFireRating: ProvidedFireRating.Rated(30));
        var opening = CurtainWallJunction.WallJunction("j-opening", ZoneA, "cw-1", "wall-2", 0, 450,
            hostRequiredFireRatingMinutes: 60, minFireRating: ProvidedFireRating.Rated(60), hasUnprotectedOpening: true);

        var review = Review(new[] { underRated, opening });

        Assert.All(review.Findings, f => Assert.Equal(ReviewStatus.Fail, f.Status));
        Assert.Equal(ReviewValue.OfBoolean(true), review.For("j-opening")!.Result.Evidence.Find("junction.hasUnprotectedOpening"));
    }

    [Fact]
    public void Case7_900mm_on_one_side_alone_is_still_exempt()
    {
        // 決議 1：總和判定，不要求兩側各 450 mm。幾何層送進來的就是總和。
        Assert.Equal(ReviewStatus.NotApplicable, Single(new[] { Wall(0, 900) }).Status);
    }

    [Theory]
    // 決議 7：帶由上下帷幕牆之間的實體牆供給，這個交點上一片嵌板也沒有、也沒有嵌板時效可轉述。
    // 三態要跟嵌板供給的帶完全一樣走同一條規則，Check 層不必為它開特例。
    [InlineData(900.0, ReviewStatus.NotApplicable)]
    [InlineData(0.0, ReviewStatus.Fail)]
    [InlineData(null, ReviewStatus.InsufficientData)]
    public void A_band_supplied_by_a_solid_wall_answers_on_the_same_three_states(double? bandMm, ReviewStatus expected)
    {
        var junction = CurtainWallJunction.WallJunction(
            "j-band", ZoneA, "cw-1", "wall-band", 0, bandMm,
            hostRequiredFireRatingMinutes: 60);

        var finding = Single(new[] { junction });

        Assert.Equal(expected, finding.Status);
        Assert.Equal(0.0, Number(finding.Result.Evidence.Find("junction.panelCount")));
    }

    [Fact]
    public void Case8_a_grid_line_across_the_band_is_manual_review_and_names_it()
    {
        var doubt = new CurtainWallJunctionDoubt(CurtainWallJunctionDoubtKind.SplitByGridLine,
            "交接帶被 grid line（ElementId 123456）分割，請確認是否為真實構造斷點；若否，請刪除該 grid line 後重跑。",
            new[] { "grid-123456" });
        var finding = Single(new[]
        {
            CurtainWallJunction.Doubtful("j-h1", CurtainWallJunctionKind.WallToCurtainWall, ZoneA, "cw-1", doubt, "wall-1")
        });

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Null(finding.Outcome);
        Assert.Equal(ReviewErrorCode.CurtainWallJunctionSplitByGridLine, finding.ErrorCode);
        Assert.Contains("123456", finding.Result.Message);
        Assert.Contains("grid-123456", finding.Result.SubjectUniqueIds);
        Assert.Equal(ReviewValue.OfText("SplitByGridLine"), finding.Result.Evidence.Find("junction.doubt"));

        // No rule decided it, so the result is attributed to the rule set itself.
        Assert.Equal("tw-bcr-fire", finding.Result.RuleId);
        Assert.Null(finding.Result.ActualValue);
    }

    [Fact]
    public void Case8b_deleting_the_grid_line_makes_the_same_junction_exempt()
    {
        // 同一個交接處，建模端刪掉多餘 grid line 後嵌板連續，幾何層這次量得出 900 mm。
        Assert.Equal(ReviewStatus.NotApplicable, Single(new[] { Wall(0, 900) }).Status);
    }

    [Fact]
    public void Case8c_a_real_break_is_judged_on_the_length_it_leaves()
    {
        // grid line 一側 60 min 實板、另一側無時效玻璃：那是真的斷點，累積就停在那裡，不是人工覆核。
        var finding = Single(new[] { Wall(0, 520) });

        Assert.Equal(ReviewStatus.Fail, finding.Status);
        Assert.Null(finding.Doubt);
        Assert.Equal(0.52, Number(finding.Result.Evidence.Find("junction.continuousFireRatedLength")), 6);
    }

    [Fact]
    public void Case9_an_article83_compartment_is_judged_the_same_and_says_where_it_came_from()
    {
        var finding = Single(new[] { Wall(0, 900, reference: CurtainWallJunctionReferences.Article83) });

        Assert.Equal(ReviewStatus.NotApplicable, finding.Status);
        Assert.Equal(ReviewValue.OfText("第83條"), finding.Result.Evidence.Find("junction.hostLegalReference"));
        Assert.Contains("第83條", finding.Result.Message);
    }

    // --- CW-V 區劃樓地板 × 帷幕牆（§10 案例 10–14） -------------------------------------------------

    [Fact]
    public void Case10_a_900mm_spandrel_at_the_floors_own_rating_is_exempt()
    {
        var finding = Single(new[] { Spandrel(0, 900, panelMinutes: 60, floorRequiredMinutes: 60) });

        Assert.Equal(ReviewStatus.NotApplicable, finding.Status);
        Assert.True(finding.IsExempt);
        Assert.Equal("tw-bcr-79-3-curtain-wall-spandrel", finding.Result.RuleId);
        Assert.Equal(60, Number(finding.Result.Evidence.Find("junction.hostRequiredFireRating")), 6);
    }

    [Fact]
    public void Case11_a_60min_spandrel_under_a_120min_floor_does_not_count_towards_the_band()
    {
        // 自頂層起算第 5 層以上，樓地板要求 120 min：60 min 的嵌板不計入高度，帶就湊不到 900 mm。
        var finding = Single(new[] { Spandrel(0, bandMm: 0, panelMinutes: 60, floorRequiredMinutes: 120) });

        Assert.Equal(ReviewStatus.Fail, finding.Status);
        Assert.Equal(120, Number(finding.Result.Evidence.Find("junction.hostRequiredFireRating")), 6);
        Assert.Equal(60, Number(finding.Result.Evidence.Find("junction.minFireRating")), 6);
    }

    [Fact]
    public void Case12_a_900mm_spandrel_of_only_30min_fails()
    {
        Assert.Equal(ReviewStatus.Fail, Single(new[] { Spandrel(0, bandMm: 0, panelMinutes: 30) }).Status);
    }

    [Fact]
    public void Case13_a_projecting_floor_passes_even_with_no_panel_rating_at_all()
    {
        // 突出已成立，本文就過了；但書的資料缺口不影響（§5.4 的第二列）。
        var finding = Single(new[]
        {
            Spandrel(500, bandMm: null, rating: ProvidedFireRating.Missing("Curtain Panels 未綁定設計防火時效"))
        });

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Null(finding.ErrorCode);
    }

    [Fact]
    public void Case14_one_panel_without_a_rating_makes_the_whole_band_insufficient()
    {
        var finding = Single(new[]
        {
            Spandrel(0, bandMm: null, rating: ProvidedFireRating.Missing("層間帶內有嵌板型別未填設計防火時效"))
        });

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Contains("junction.continuousFireRatedHeight", finding.Outcome!.Gaps.Select(g => g.Field));
        Assert.Equal(ReviewErrorCode.ParameterMissing, finding.ErrorCode);
        Assert.Equal(ReviewValue.OfText("Missing"), finding.Result.Evidence.Find("provided.kind"));
        Assert.Equal(ReviewValue.OfText(FireRatingParameters.Provided), finding.Result.Evidence.Find("provided.parameter"));
    }

    [Fact]
    public void Case47_an_all_glass_spandrel_over_a_floor_that_does_not_project_fails()
    {
        // 幾何層對宣告為玻璃的層間帶供給高度 0、不供時效讀值（決議 16）：那是設計本身未設防火帶，
        // 不是資料缺口，所以是未符合而不是資料不足。
        var finding = Single(new[] { Spandrel(0, bandMm: 0, panelMinutes: null) });

        Assert.Equal(ReviewStatus.Fail, finding.Status);
        Assert.Equal("tw-bcr-79-3-curtain-wall-spandrel", finding.Result.RuleId);
        Assert.Null(finding.ErrorCode);
    }

    [Fact]
    public void A_floor_that_does_not_meet_the_curtain_wall_is_manual_review_with_its_own_code()
    {
        var junction = CurtainWallJunction.Doubtful("CW-V:cw-1", CurtainWallJunctionKind.FloorToCurtainWall, ZoneA, "cw-1",
            new CurtainWallJunctionDoubt(CurtainWallJunctionDoubtKind.FloorNotMeetingCurtainWall,
                "本層最近的區劃樓地板邊緣距帷幕牆定位線 400 mm，超過搜尋公差 300 mm。", new[] { "cw-1", "floor-1" }),
            hostUniqueId: "floor-1");

        var finding = Single(new[] { junction });

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.CurtainWallFloorNotMeeting, finding.ErrorCode);
        // 主詞不說「與區劃樓地板之層間交接」：沒有樓板與它交接正是這一列要講的事。
        Assert.StartsWith("帷幕牆（cw-1）於本層之層間交接處", finding.Result.Message);
        Assert.Equal(ReviewValue.OfText("FloorNotMeetingCurtainWall"), finding.Result.Evidence.Find("junction.doubt"));
        Assert.Null(finding.Result.Evidence.Find("junction.transferredTo"));
    }

    // --- CW-O 其餘帷幕牆面（第79條之4） -------------------------------------------------------------

    [Theory]
    [InlineData(30, ReviewStatus.Pass)]
    [InlineData(29, ReviewStatus.Fail)]
    public void Other_panels_need_half_an_hour(double minutes, ReviewStatus expected)
    {
        var finding = Single(new[]
        {
            CurtainWallJunction.OtherPanels("j-o1", ZoneA, "cw-1", ProvidedFireRating.Rated(minutes), new[] { "panel-9" })
        });

        Assert.Equal(expected, finding.Status);
        Assert.Equal("tw-bcr-79-4-curtain-wall-other", finding.Result.RuleId);
        Assert.Equal(ZoneA.ToString("D"), Assert.IsType<ReviewValue>(finding.Result.Evidence.Find("junction.zoneId")).Text);
    }

    /// <summary>案例 45：玻璃嵌板的三態，答的是防火設備而不是分鐘數（決議 16）。</summary>
    [Theory]
    [InlineData(ProvidedFireProtectionKind.Yes, ReviewStatus.Pass)]
    [InlineData(ProvidedFireProtectionKind.No, ReviewStatus.Fail)]
    [InlineData(ProvidedFireProtectionKind.Missing, ReviewStatus.InsufficientData)]
    public void Case45_glazed_other_panels_answer_with_their_protection(ProvidedFireProtectionKind kind, ReviewStatus expected)
    {
        var protection = kind switch
        {
            ProvidedFireProtectionKind.Yes => ProvidedFireProtection.Yes("是"),
            ProvidedFireProtectionKind.No => ProvidedFireProtection.No("否"),
            _ => ProvidedFireProtection.Missing("型別未提供防火保護")
        };

        var finding = Single(new[]
        {
            CurtainWallJunction.OtherGlazedPanels("j-o1", ZoneA, "cw-1", protection, new[] { "panel-9" })
        });

        Assert.Equal(expected, finding.Status);
        Assert.Equal("tw-bcr-79-4-curtain-wall-other-glazed", finding.Result.RuleId);
        Assert.Equal(ReviewValue.OfText(kind.ToString()), finding.Result.Evidence.Find("protection.kind"));
        Assert.Equal(ReviewValue.OfText(FireProtectionParameters.Provided), finding.Result.Evidence.Find("protection.parameter"));
        if (expected == ReviewStatus.InsufficientData)
            Assert.Equal(ReviewErrorCode.ParameterMissing, finding.ErrorCode);
    }

    /// <summary>
    /// 案例 46：沒宣告嵌板種類時判資料不足，而且訊息要指名那個參數——使用者補得起來，前提是知道
    /// 要補什麼（決議 16）。
    /// </summary>
    [Fact]
    public void Case46_other_panels_without_a_declared_kind_name_the_parameter_to_fill()
    {
        var finding = Single(new[]
        {
            CurtainWallJunction.OtherUndeclaredPanels("j-o1", ZoneA, "cw-1", new[] { "panel-9" })
        });

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Contains("junction.panelKind", finding.Outcome!.Gaps.Select(g => g.Field));
        Assert.Contains(CurtainPanelKindParameters.Provided, finding.Result.Message);
        Assert.Equal(ReviewErrorCode.ParameterMissing, finding.ErrorCode);
    }

    [Fact]
    public void Case15_panels_with_no_bound_rating_parameter_are_insufficient_data()
    {
        var review = Review(new[]
        {
            CurtainWallJunction.OtherPanels("j-o1", ZoneA, "cw-1", ProvidedFireRating.Missing("Curtain Panels 未綁定設計防火時效")),
            Wall(0, runMm: null, panelMinutes: null),
            Spandrel(0, bandMm: null, panelMinutes: null)
        });

        Assert.All(review.Findings, f => Assert.Equal(ReviewStatus.InsufficientData, f.Status));
        Assert.Equal(ReviewErrorCode.ParameterMissing, review.For("j-o1")!.ErrorCode);
    }

    [Fact]
    public void A_composite_panel_rating_is_manual_review_when_it_is_the_only_thing_missing()
    {
        var finding = Single(new[]
        {
            CurtainWallJunction.OtherPanels("j-o1", ZoneA, "cw-1",
                ProvidedFireRating.Undeterminable("1hr/2hr", "含 2 個防火時效，應為複合構造，無法判定單一時效"))
        });

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.FireRatingUndetermined, finding.ErrorCode);
        Assert.Contains("1hr/2hr", finding.Result.Message);
        Assert.Contains("30 min", finding.Result.Message);
    }

    // --- 適用性與幾何無法判定（§10 案例 16–18） -----------------------------------------------------

    [Fact]
    public void Case16_a_building_that_is_not_fire_resistive_is_not_reviewed_at_all()
    {
        var review = Review(new[]
        {
            Wall(0, 0),
            Spandrel(0, bandMm: 0),
            CurtainWallJunction.OtherPanels("j-o1", ZoneA, "cw-1", ProvidedFireRating.Rated(0))
        }, fireResistive: false);

        Assert.Equal(3, review.Count(ReviewStatus.NotApplicable));
        Assert.All(review.Findings, f => Assert.Equal(RuleOutcomeReason.NoRuleApplies, f.Outcome!.Reason));
    }

    [Fact]
    public void Case17_a_space_spanning_several_storeys_is_handed_to_article_79_2()
    {
        var doubt = new CurtainWallJunctionDoubt(CurtainWallJunctionDoubtKind.VerticalCompartmentSpace,
            "此處為連跨三個樓層之挑空帷幕牆，層間交接不適用，改依第79條之2垂直區劃檢討。");
        var finding = Single(new[]
        {
            CurtainWallJunction.Doubtful("j-v1", CurtainWallJunctionKind.FloorToCurtainWall, ZoneA, "cw-1", doubt, "floor-1")
        });

        // 不適用，不是人工覆核：這個位置有它自己的條文，不是量不出來。
        Assert.Equal(ReviewStatus.NotApplicable, finding.Status);
        Assert.Equal(ReviewErrorCode.CurtainWallVerticalSpace, finding.ErrorCode);
        Assert.Equal(ReviewValue.OfText(CurtainWallJunctionReferences.Article79_2),
            finding.Result.Evidence.Find("junction.transferredTo"));
    }

    [Fact]
    public void Case18_a_curved_curtain_wall_is_manual_review()
    {
        var doubt = new CurtainWallJunctionDoubt(CurtainWallJunctionDoubtKind.NonPlanarCurtainWall,
            "帷幕牆為曲面，本版只支援平面帷幕牆，需人工覆核。");
        var finding = Single(new[]
        {
            CurtainWallJunction.Doubtful("j-h1", CurtainWallJunctionKind.WallToCurtainWall, ZoneA, "cw-1", doubt, "wall-1")
        });

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.CurtainWallNotPlanar, finding.ErrorCode);
    }

    [Fact]
    public void An_intersection_that_did_not_resolve_is_manual_review()
    {
        var doubt = new CurtainWallJunctionDoubt(CurtainWallJunctionDoubtKind.UnresolvedIntersection,
            "區劃牆端點與帷幕牆的距離超過搜尋公差 300 mm，交接處無法唯一解析，需人工覆核。");
        var finding = Single(new[]
        {
            CurtainWallJunction.Doubtful("j-h1", CurtainWallJunctionKind.WallToCurtainWall, ZoneA, "cw-1", doubt, "wall-1")
        });

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.CurtainWallJunctionUnresolved, finding.ErrorCode);
    }

    [Fact]
    public void A_curtain_wall_whose_exposure_is_undecided_is_manual_review()
    {
        // 實機回歸（2026-10-07）：這一種疑義曾經沒有錯誤碼，ErrorCode 拋例外，整次檢討中止。
        var doubt = new CurtainWallJunctionDoubt(CurtainWallJunctionDoubtKind.ExposureUndecided,
            "無法判定帷幕牆（Id cw-1）是建築物外牆或室內帷幕牆：同一道弧形牆的各平面段判出不同結果，需人工覆核。",
            new[] { "cw-1" });
        var finding = Single(new[]
        {
            CurtainWallJunction.Doubtful("j-h1:exposure", CurtainWallJunctionKind.WallToCurtainWall, ZoneA, "cw-1", doubt)
        });

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.CurtainWallExposureUndecided, finding.ErrorCode);
        Assert.Contains("之室內外判定", finding.Result.Message);
    }

    [Fact]
    public void Every_kind_of_doubt_has_a_catalogued_error_code()
    {
        foreach (CurtainWallJunctionDoubtKind kind in Enum.GetValues(typeof(CurtainWallJunctionDoubtKind)))
        {
            var doubt = new CurtainWallJunctionDoubt(kind, "說明");
            Assert.True(ReviewErrorCode.IsKnown(doubt.ErrorCode), $"{kind} → {doubt.ErrorCode}");
        }
    }

    // --- 區劃範圍問題 -------------------------------------------------------------------------------

    [Fact]
    public void A_junction_of_a_zone_whose_extent_is_in_doubt_is_withheld()
    {
        var set = Set(RectZone(ZoneA, "A 區", "area-a"), RectZone(ZoneB, "B 區", "area-b", x0: 20));
        var review = CurtainWallJunctionCheck.Review(set,
            new CurtainWallJunctionInputs(Context(), new[] { Wall(0, 0) }), Shipped(), Today, RunId);

        Assert.True(review.IsSuccess, review.IsSuccess ? string.Empty : review.Error.ToString());
        var finding = Assert.Single(review.Value.Findings);

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Null(finding.Outcome);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, finding.ErrorCode);
        Assert.Equal(ReviewValue.OfText("ZonesOverlap"), finding.Result.Evidence.Find("zone.problems"));
    }

    [Fact]
    public void A_package_without_a_usable_zone_is_not_reviewed()
    {
        var empty = CandidateResolver.Resolve(new CandidateObservationSet(PackageId, "level-1F", "1F",
            Array.Empty<ZoneObservation>(), Array.Empty<MemberObservation>(), Array.Empty<OpeningObservation>()));

        var review = CurtainWallJunctionCheck.Review(empty, CurtainWallJunctionInputs.None, Shipped(), Today, RunId);

        Assert.True(review.IsFailure);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, review.Error.Code);
        Assert.Contains("帷幕牆區劃交接", review.Error.Message);
    }

    [Fact]
    public void A_junction_of_a_zone_outside_the_package_is_skipped_with_a_warning()
    {
        var review = Review(new[]
        {
            CurtainWallJunction.WallJunction("j-elsewhere", ZoneB, "cw-9", "wall-9", 0, 0),
            Wall(600)
        });

        Assert.Single(review.Findings);
        Assert.Equal("j-h1", review.Findings[0].JunctionId);
        Assert.Contains(review.Warnings, w => w.Contains("j-elsewhere") && w.Contains("已略過"));
    }

    // --- 結果本身 ----------------------------------------------------------------------------------

    [Fact]
    public void The_result_records_where_the_junction_is_so_the_review_view_can_be_marked_from_it_alone()
    {
        // docs §7.1：標示層是從已儲存的結果重建的，重新讀模型會把標示畫到「現在」的位置。
        var placement = CurtainWallJunctionPlacement.At(new Point2D(5000, 0), 0, 3600);
        var finding = Single(new[]
        {
            CurtainWallJunction.WallJunction("j-h1", ZoneA, "cw-1", "wall-1", 0, 900,
                hostRequiredFireRatingMinutes: 60, minFireRating: ProvidedFireRating.Rated(60),
                panelUniqueIds: new[] { "panel-1" }, placement: placement)
        });

        var text = Assert.IsType<ReviewValue>(finding.Result.Evidence.Find("junction.placement")).Text;
        Assert.True(CurtainWallJunctionPlacement.TryParseEvidence(text, out var read));
        Assert.Equal(5000, read!.StartMm.X, 3);
        Assert.Equal(3600, read.TopElevationMm, 3);
    }

    [Fact]
    public void A_junction_with_no_placement_simply_records_none()
    {
        Assert.False(Single(new[] { Wall(0, 900) }).Result.Evidence.Has("junction.placement"));
        Assert.False(Single(new[]
        {
            CurtainWallJunction.OtherPanels("j-o1", ZoneA, "cw-1", ProvidedFireRating.Rated(30), new[] { "panel-9" })
        }).Result.Evidence.Has("junction.placement"));
    }

    [Fact]
    public void The_result_keeps_the_measurement_the_clause_and_where_the_inputs_came_from()
    {
        var finding = Single(new[] { Wall(0, 900) });
        var evidence = finding.Result.Evidence;

        Assert.Equal(ReviewValue.OfText("WallToCurtainWall"), evidence.Find("junction.kind"));
        Assert.Equal(ReviewValue.OfText("j-h1"), evidence.Find("junction.id"));
        Assert.Equal(ReviewValue.OfText("A 區"), evidence.Find("zone.name"));
        Assert.Equal(ReviewValue.OfText("cw-1"), evidence.Find("junction.curtainWallUniqueId"));
        Assert.Equal(ReviewValue.OfText("wall-1"), evidence.Find("junction.hostUniqueId"));
        Assert.Equal(0.9, Number(evidence.Find("junction.continuousFireRatedLength")), 6);
        Assert.Equal(0, Number(evidence.Find("junction.projectionDepth")), 6);
        Assert.Equal(2, Number(evidence.Find("junction.panelCount")));
        Assert.Equal(ReviewValue.OfText("panel-1,panel-2"), evidence.Find("junction.panels"));
        Assert.Equal(0.3, Number(evidence.Find("option.junctionSearchTolerance")), 6);
        Assert.Equal(0.6, Number(evidence.Find("option.samplingInterval")), 6);
        Assert.Equal(ReviewValue.OfText("專案設定"), evidence.Find("source[building.fireResistiveConstruction]"));
        Assert.False(evidence.Has("rule.gaps"));

        Assert.Equal("建築技術規則建築設計施工編第79條第3項、第4項（區劃來源含第83條）", finding.Result.LegalReference);
        Assert.Equal(new[] { "cw-1", "panel-1", "panel-2", "wall-1" },
            finding.Result.SubjectUniqueIds.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void The_table_counts_the_three_rows_and_keeps_the_two_clauses_apart()
    {
        var review = Review(new[]
        {
            Wall(600, id: "j-h1"),
            Wall(0, 0, reference: CurtainWallJunctionReferences.Article83, id: "j-h2"),
            Spandrel(0, 900, id: "j-v1"),
            CurtainWallJunction.OtherPanels("j-o1", ZoneA, "cw-1", ProvidedFireRating.Rated(60))
        });

        Assert.Equal(3, review.Groups.Count);

        var horizontal = review.Group(CurtainWallJunctionKind.WallToCurtainWall);
        Assert.Equal(2, horizontal.JunctionCount);
        Assert.Equal(ReviewStatus.Fail, horizontal.Status);
        Assert.Equal(new[] { "第79條", "第83條" }, horizontal.LegalReferences);
        Assert.Equal(1, horizontal.CountOf(CurtainWallJunctionReferences.Article83));
        Assert.Equal("帷幕牆區劃交接（水平）", CurtainWallJunctionKinds.Label(CurtainWallJunctionKind.WallToCurtainWall));

        Assert.Equal(ReviewStatus.NotApplicable, review.Group(CurtainWallJunctionKind.FloorToCurtainWall).Status);
        Assert.Equal(ReviewStatus.Pass, review.Group(CurtainWallJunctionKind.CurtainPanelOther).Status);
    }

    [Fact]
    public void An_empty_row_is_not_run_rather_than_passing()
    {
        var review = Review(Array.Empty<CurtainWallJunction>());

        Assert.Empty(review.Findings);
        Assert.All(review.Groups, g => Assert.Equal(ReviewStatus.NotRun, g.Status));
    }

    [Fact]
    public void The_same_junctions_always_come_back_in_the_same_order()
    {
        var review = Review(new[]
        {
            CurtainWallJunction.OtherPanels("j-o1", ZoneA, "cw-1", ProvidedFireRating.Rated(60)),
            Spandrel(600, id: "j-v2"),
            Wall(600, id: "j-h2"),
            Wall(600, id: "j-h1")
        });

        Assert.Equal(new[] { "j-h1", "j-h2", "j-v2", "j-o1" }, review.Findings.Select(f => f.JunctionId));
    }

    // --- 輸入契約 ----------------------------------------------------------------------------------

    [Fact]
    public void The_junctions_are_checked_as_they_are_supplied()
    {
        Assert.Throws<ArgumentException>(() => new CurtainWallJunctionInputs(null, new[] { Wall(600), Wall(0, 900) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Wall(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Wall(0, -1));
        Assert.Throws<ArgumentException>(() =>
            CurtainWallJunction.WallJunction("j", ZoneA, "cw-1", " ", 0));
        Assert.Throws<ArgumentException>(() =>
            CurtainWallJunction.WallJunction("j", Guid.Empty, "cw-1", "wall-1", 0));
        Assert.Throws<ArgumentNullException>(() =>
            CurtainWallJunction.OtherPanels("j", ZoneA, "cw-1", null!));
    }

    [Fact]
    public void A_wall_junction_never_carries_a_spandrel_height_and_the_other_way_round()
    {
        // 幾何層若把高度送進水平交接，規則會讀不到它要的長度——欄位分開正是為了讓這種錯誤停在資料不足。
        Assert.Null(Wall(0, 900).ContinuousFireRatedHeightMm);
        Assert.Null(Spandrel(0, 900).ContinuousFireRatedLengthMm);
        Assert.Null(CurtainWallJunction.OtherPanels("j-o1", ZoneA, "cw-1", ProvidedFireRating.Rated(60)).ProjectionDepthMm);
    }

    [Fact]
    public void The_settings_are_the_ones_the_statute_states()
    {
        var options = CurtainWallJunctionOptions.Default;

        Assert.Equal(500, options.MinProjectionMm);
        Assert.Equal(900, options.MinFireRatedRunMm);
        Assert.Equal(30, options.OtherWallRequiredMinutes);
        Assert.Equal(300, options.JunctionSearchToleranceMm);
        Assert.Equal(600, options.SamplingIntervalMm);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CurtainWallJunctionOptions(minProjectionMm: 0));
    }

    [Fact]
    public void The_settings_agree_with_the_thresholds_the_shipped_rules_require()
    {
        // 常數表與規則集是兩份資料，數字必須一致，否則幾何層量的帶與規則判的門檻會對不上。
        var options = CurtainWallJunctionOptions.Default;

        Assert.Equal(options.MinProjectionMm / 1000.0, Number(Single(new[] { Wall(600) }).Result.RequiredValue), 6);
        Assert.Equal(options.OtherWallRequiredMinutes,
            Number(Single(new[] { CurtainWallJunction.OtherPanels("j-o1", ZoneA, "cw-1", ProvidedFireRating.Rated(60)) }).Result.RequiredValue), 6);

        // 但書的門檻沒有 RequiredValue 可讀，只能從兩側行為確認：剛好達標免突出，差 1 mm 就未符合。
        Assert.Equal(ReviewStatus.NotApplicable, Single(new[] { Wall(0, options.MinFireRatedRunMm) }).Status);
        Assert.Equal(ReviewStatus.Fail, Single(new[] { Wall(0, options.MinFireRatedRunMm - 1) }).Status);
        Assert.Equal(ReviewStatus.NotApplicable, Single(new[] { Spandrel(0, options.MinFireRatedRunMm) }).Status);
        Assert.Equal(ReviewStatus.Fail, Single(new[] { Spandrel(0, options.MinFireRatedRunMm - 1) }).Status);
    }
}
