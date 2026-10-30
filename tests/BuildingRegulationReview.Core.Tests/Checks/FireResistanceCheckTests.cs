using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Checks;

public sealed class FireResistanceCheckTests
{
    private static readonly RuleEvaluationContext Today = new(new DateTime(2025, 6, 1), "TW");
    private static readonly Guid RunId = Guid.Parse("99999999-0000-0000-0000-000000000002");

    private const string WallType = "type-wall";
    private const string ColumnType = "type-column";
    private const string BeamType = "type-beam";
    private const string FloorType = "type-floor";

    private static Rule RatingRule(string ruleId, string appliesWhen, string requiredValue) =>
        new(ruleId, "1", RuleCategory.FireResistance, $"測試條文 {ruleId}", new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression(appliesWhen), new RuleExpression(requiredValue),
            evidenceFields: new[] { "element.typeName", "element.providedFireRating" });

    /// <summary>Boundary walls 1 h; columns 2 h, or 3 h above ten storeys; beams 2 h; floors 1.5 h.</summary>
    private static readonly Rule[] Rules =
    {
        RatingRule("wall", "element.category == \"Walls\" && element.isCompartmentBoundary == true", "element.providedFireRating >= 60 min"),
        RatingRule("column", "element.category == \"Columns\"",
            "element.providedFireRating >= (building.floorsAboveGround > 10 ? 3 h : 2 h)"),
        RatingRule("beam", "element.category == \"StructuralFraming\"", "element.providedFireRating >= 120 min"),
        RatingRule("floor", "element.category == \"Floors\"", "element.providedFireRating >= 90 min")
    };

    private static RuleEngine Engine(params Rule[] rules)
    {
        var compiled = RuleSetCompiler.Compile(new RuleSet("tw-bcr-fire", "2024.1", "防火時效", rules.Length == 0 ? Rules : rules));
        Assert.True(compiled.IsSuccess, compiled.Error.TechnicalDetail);
        return new RuleEngine(compiled.Value);
    }

    // --- 固定幾何：A 區 x 0–10、B 區 x 10–20 ------------------------------------------------------

    private static MemberObservation WallAt(string uid, double x, string type = WallType, string typeName = "RC 200", string? link = null) =>
        new(Source(uid, link), CandidateCategory.Wall, new[] { P(x, 0), P(x, 10) }, widthFeet: M(0.2),
            typeUniqueId: type, typeName: typeName, isStructural: true);

    private static MemberObservation EdgeMember(CandidateCategory category, string? uid = null, string? type = null) => category switch
    {
        CandidateCategory.Wall => WallAt(uid ?? "W-edge", 0, type ?? WallType),
        CandidateCategory.Column => new MemberObservation(Source(uid ?? "C-edge"), CandidateCategory.Column,
            outlines: new[] { Rect(-0.2, 4.8, 0.2, 5.2) }, typeUniqueId: type ?? ColumnType, typeName: "C40x40", isStructural: true),
        CandidateCategory.StructuralFraming => new MemberObservation(Source(uid ?? "B-edge"), CandidateCategory.StructuralFraming,
            new[] { P(0, 0), P(10, 0) }, widthFeet: M(0.3), typeUniqueId: type ?? BeamType, typeName: "B30x60", isStructural: true),
        CandidateCategory.Floor => new MemberObservation(Source(uid ?? "F-slab"), CandidateCategory.Floor,
            outlines: new[] { Rect(0, 0, 10, 10) }, typeUniqueId: type ?? FloorType, typeName: "RC 150", isStructural: true),
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    private static IReadOnlyList<ZoneObservation> ZoneAOnly() => Zones().Take(1).ToList();
    private static IReadOnlyList<ZoneObservation> ZonesAB() => Zones().Take(2).ToList();

    private static CandidateSet Set(IEnumerable<ZoneObservation> zones, params MemberObservation[] members) =>
        CandidateResolver.Resolve(Observations(zones, members, Array.Empty<OpeningObservation>()));

    private static CompartmentAreaInputs Context(double? floors = 5) =>
        new(floors is double f ? new[] { ReviewInput.Known("building.floorsAboveGround", f, ReviewUnit.None, "專案設定") } : null, null);

    private static FireResistanceInputs Ratings(params (string Type, string Text)[] ratings) => Ratings(Context(), ratings);

    private static FireResistanceInputs Ratings(CompartmentAreaInputs context, params (string Type, string Text)[] ratings) =>
        new(context, ratings.Select(r => new TypeFireRating(r.Type, FireRatingText.Parse(r.Text), "Fire Rating")));

    private static FireResistanceReview Review(CandidateSet set, FireResistanceInputs inputs, RuleEngine? engine = null)
    {
        var ids = Enumerable.Range(1, 200).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).GetEnumerator();
        var review = FireResistanceCheck.Review(set, inputs, engine ?? Engine(), Today, RunId, () => { ids.MoveNext(); return ids.Current; });
        Assert.True(review.IsSuccess, review.Error.ToString());
        return review.Value;
    }

    private static MemberRatingFinding Only(FireResistanceReview review) => Assert.Single(review.Findings);

    private static double Number(ReviewValue? value) => Assert.IsType<ReviewValue>(value).Number;

    // --- 各 Category 與邊界值 -----------------------------------------------------------------------

    [Theory]
    [InlineData(CandidateCategory.Wall, "59", 60, ReviewStatus.Fail)]
    [InlineData(CandidateCategory.Wall, "60", 60, ReviewStatus.Pass)]
    [InlineData(CandidateCategory.Wall, "1 hr", 60, ReviewStatus.Pass)]
    [InlineData(CandidateCategory.Wall, "61", 60, ReviewStatus.Pass)]
    [InlineData(CandidateCategory.Column, "119", 120, ReviewStatus.Fail)]
    [InlineData(CandidateCategory.Column, "2小時", 120, ReviewStatus.Pass)]
    [InlineData(CandidateCategory.StructuralFraming, "119.5", 120, ReviewStatus.Fail)]
    [InlineData(CandidateCategory.StructuralFraming, "120 min", 120, ReviewStatus.Pass)]
    [InlineData(CandidateCategory.Floor, "89", 90, ReviewStatus.Fail)]
    [InlineData(CandidateCategory.Floor, "一小時半", 90, ReviewStatus.Pass)]
    [InlineData(CandidateCategory.Floor, "1 hr", 90, ReviewStatus.Fail)]
    public void Each_category_passes_at_its_required_rating_and_fails_below_it(CandidateCategory category, string provided, double required, ReviewStatus expected)
    {
        var member = EdgeMember(category);
        var finding = Only(Review(Set(ZoneAOnly(), member), Ratings((member.TypeUniqueId!, provided))));
        var result = finding.Result;

        Assert.Equal(expected, result.Status);
        Assert.Equal(ReviewValue.Quantity(FireRatingText.Parse(provided).Minutes!.Value, ReviewUnit.Minute), result.ActualValue);
        Assert.Equal(ReviewValue.Quantity(required, ReviewUnit.Minute), result.RequiredValue);
        Assert.Equal(required, finding.RequiredMinutes);
        Assert.Null(finding.ErrorCode);

        Assert.Equal(ReviewCheckTypes.FireResistance, result.CheckType);
        Assert.Equal(new[] { member.Source.ElementUniqueId }, result.SubjectUniqueIds);
        Assert.Equal(ZoneA.ToString("D"), result.ZoneId);
        Assert.Equal(PackageId, result.PackageId);
        Assert.Equal(RunId, result.RunId);
        Assert.Equal(category, finding.Category);
        Assert.Equal(member.TypeUniqueId, finding.TypeUniqueId);
        Assert.StartsWith($"{CandidateCategories.Label(category)}「{member.TypeName}」（{member.Source}）於區劃「A 區」：", result.Message);
    }

    [Theory]
    [InlineData(10, "2 hr", ReviewStatus.Pass)]
    [InlineData(11, "2 hr", ReviewStatus.Fail)]
    [InlineData(11, "3 hr", ReviewStatus.Pass)]
    public void The_required_rating_comes_from_the_rules_and_the_building_inputs(double floors, string provided, ReviewStatus expected)
    {
        var column = EdgeMember(CandidateCategory.Column);
        var result = Only(Review(Set(ZoneAOnly(), column), Ratings(Context(floors), (ColumnType, provided)))).Result;

        Assert.Equal(expected, result.Status);
        Assert.Equal(floors > 10 ? 180 : 120, Number(result.RequiredValue));
        Assert.Equal(ReviewValue.OfText("專案設定"), result.Evidence.Find("source[building.floorsAboveGround]"));
    }

    [Fact]
    public void A_wall_that_does_not_bound_the_zone_is_not_applicable_with_its_evidence()
    {
        var partition = WallAt("W-partition", 5);
        var result = Only(Review(Set(ZoneAOnly(), partition), Ratings((WallType, "30")))).Result;

        Assert.Equal(ReviewStatus.NotApplicable, result.Status);
        Assert.Equal(ReviewValue.OfBoolean(false), result.Evidence.Find("element.isCompartmentBoundary"));
        Assert.Equal(ReviewValue.OfText("Inside"), result.Evidence.Find("candidate.relation"));
    }

    [Fact]
    public void A_shared_boundary_wall_is_reviewed_once_per_zone()
    {
        var shared = WallAt("W-shared", 10);
        var review = Review(Set(ZonesAB(), shared), Ratings((WallType, "60")));

        Assert.Equal(new[] { ZoneA, ZoneB }, review.Findings.Select(f => f.ZoneId!.Value));
        Assert.All(review.Findings, f => Assert.Equal(ReviewStatus.Pass, f.Status));
        Assert.All(review.Findings, f => Assert.Equal(ReviewValue.OfText("Boundary"), f.Result.Evidence.Find("candidate.relation")));
    }

    // --- Required／Provided 分離，不覆寫 Provided --------------------------------------------------

    [Fact]
    public void A_failing_member_keeps_its_design_rating_and_the_requirement_stays_separate()
    {
        var wall = EdgeMember(CandidateCategory.Wall);
        var inputs = Ratings((WallType, "30 min"));
        var before = inputs.TypeRatings.Single().Rating;

        var finding = Only(Review(Set(ZoneAOnly(), wall), inputs));
        var result = finding.Result;

        Assert.Equal(ReviewStatus.Fail, result.Status);
        Assert.Equal(ReviewValue.Quantity(30, ReviewUnit.Minute), result.ActualValue);
        Assert.Equal(ReviewValue.Quantity(60, ReviewUnit.Minute), result.RequiredValue);
        Assert.Same(before, finding.Provided);
        Assert.Same(before, inputs.TypeRatings.Single().Rating);
        Assert.Equal(30, before.Minutes);
        Assert.Equal("30 min", before.RawText);

        var evidence = result.Evidence;
        Assert.Equal(ReviewValue.Quantity(30, ReviewUnit.Minute), evidence.Find("element.providedFireRating"));
        Assert.Equal(ReviewValue.OfText("Rated"), evidence.Find("provided.kind"));
        Assert.Equal(ReviewValue.OfText("Fire Rating"), evidence.Find("provided.parameter"));
        Assert.Equal(ReviewValue.OfText("30 min"), evidence.Find("provided.raw"));
        Assert.Equal(ReviewValue.OfText(WallType), evidence.Find("source.typeUniqueId"));
        Assert.DoesNotContain(evidence.Items, x => x.Field.Contains("Required", StringComparison.OrdinalIgnoreCase));

        var summary = Review(Set(ZoneAOnly(), wall), inputs).Type(WallType)!;
        Assert.Same(inputs.TypeRatings.Single(), summary.Provided);
        Assert.Equal(60, summary.HighestRequiredMinutes);
        Assert.Equal(30, summary.Provided!.Rating.Minutes);
    }

    [Fact]
    public void The_required_rating_parameter_cannot_feed_the_design_rating()
    {
        Assert.Throws<ArgumentException>(() => new FireResistanceInputs(null, new[]
        {
            new TypeFireRating(WallType, ProvidedFireRating.Rated(120), FireRatingParameters.Required)
        }));
    }

    // --- 缺值、格式錯誤、複合構造 --------------------------------------------------------------------

    [Theory]
    [InlineData(CandidateCategory.Wall, 60)]
    [InlineData(CandidateCategory.Column, 120)]
    [InlineData(CandidateCategory.StructuralFraming, 120)]
    [InlineData(CandidateCategory.Floor, 90)]
    public void A_missing_design_rating_is_insufficient_data_but_still_states_the_requirement(CandidateCategory category, double required)
    {
        var finding = Only(Review(Set(ZoneAOnly(), EdgeMember(category)), Ratings()));
        var result = finding.Result;

        Assert.Equal(ReviewStatus.InsufficientData, result.Status);
        Assert.Null(result.ActualValue);
        Assert.Equal(ReviewValue.Quantity(required, ReviewUnit.Minute), result.RequiredValue);
        Assert.Equal(ReviewErrorCode.ParameterMissing, finding.ErrorCode);
        Assert.Equal(ProvidedFireRatingKind.Missing, finding.Provided!.Kind);
        Assert.Contains("element.providedFireRating 未設定", result.Message);
        Assert.Equal(ReviewValue.OfText("Type 未提供防火時效"), result.Evidence.Find("provided.reason"));
    }

    [Fact]
    public void A_member_without_a_Type_cannot_have_a_design_rating()
    {
        var untyped = new MemberObservation(Source("W-untyped"), CandidateCategory.Wall, new[] { P(0, 0), P(0, 10) }, widthFeet: M(0.2));
        var finding = Only(Review(Set(ZoneAOnly(), untyped), Ratings((WallType, "60"))));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Equal(ReviewValue.OfText("構件沒有可讀取的 Type"), finding.Result.Evidence.Find("provided.reason"));
        Assert.Contains("無 Type 名稱", finding.Result.Message);
    }

    [Theory]
    [InlineData("耐燃")]
    [InlineData("-30")]
    [InlineData("25 hr")]
    public void An_unreadable_design_rating_is_insufficient_data_with_the_raw_value(string text)
    {
        var finding = Only(Review(Set(ZoneAOnly(), EdgeMember(CandidateCategory.Wall)), Ratings((WallType, text))));
        var result = finding.Result;

        Assert.Equal(ReviewStatus.InsufficientData, result.Status);
        Assert.Equal(ReviewErrorCode.ParameterTypeMismatch, finding.ErrorCode);
        Assert.Contains("格式無法判讀", result.Message);
        Assert.Contains($"「{text}」", result.Message);
        Assert.Equal(ReviewValue.OfText(text), result.Evidence.Find("provided.raw"));
        Assert.Equal(ReviewValue.Quantity(60, ReviewUnit.Minute), result.RequiredValue);
    }

    [Fact]
    public void A_composite_construction_is_manual_review_against_the_required_rating()
    {
        var finding = Only(Review(Set(ZoneAOnly(), EdgeMember(CandidateCategory.Wall)), Ratings((WallType, "1hr/2hr"))));
        var result = finding.Result;

        Assert.Equal(ReviewStatus.ManualReview, result.Status);
        Assert.Equal(ReviewErrorCode.FireRatingUndetermined, finding.ErrorCode);
        Assert.Null(result.ActualValue);
        Assert.Equal(ReviewValue.Quantity(60, ReviewUnit.Minute), result.RequiredValue);
        Assert.Equal("wall", result.RuleId);
        Assert.Contains("複合構造", result.Message);
        Assert.Contains("60 min（1 小時）", result.Message);
        Assert.Equal(ReviewValue.OfText("Undeterminable"), result.Evidence.Find("provided.kind"));
    }

    [Fact]
    public void A_composite_construction_only_matters_where_a_rule_applies_and_nothing_else_is_missing()
    {
        var partition = Only(Review(Set(ZoneAOnly(), WallAt("W-partition", 5)), Ratings((WallType, "1hr/2hr"))));
        Assert.Equal(ReviewStatus.NotApplicable, partition.Status);
        Assert.Null(partition.ErrorCode);

        // The column's requirement depends on the storey count, which is missing too: still 資料不足.
        var column = Only(Review(Set(ZoneAOnly(), EdgeMember(CandidateCategory.Column)),
            Ratings(Context(floors: null), (ColumnType, "2hr/3hr"))));
        Assert.Equal(ReviewStatus.InsufficientData, column.Status);
        Assert.Equal(ReviewErrorCode.FireRatingUndetermined, column.ErrorCode);
        Assert.Contains("building.floorsAboveGround 未設定", column.Result.Message);
    }

    [Fact]
    public void Missing_or_doubtful_data_never_becomes_a_fail()
    {
        var members = CandidateCategories.Members.Select(c => EdgeMember(c)).ToArray();
        foreach (var inputs in new[]
                 {
                     Ratings(),
                     Ratings(Context(floors: null)),
                     Ratings((WallType, "耐燃"), (ColumnType, ""), (BeamType, "1hr/2hr"), (FloorType, "F60"))
                 })
        {
            var review = Review(Set(ZoneAOnly(), members), inputs);

            Assert.Equal(4, review.Findings.Count);
            Assert.Equal(0, review.Count(ReviewStatus.Fail));
            Assert.Equal(0, review.Count(ReviewStatus.Pass));
        }
    }

    // --- 歧義與區劃問題 ----------------------------------------------------------------------------

    private static MemberObservation FlushColumn() =>
        new(Source("C-flush"), CandidateCategory.Column, outlines: new[] { Rect(0, 4, 0.4, 4.4) },
            typeUniqueId: ColumnType, typeName: "C40x40", isStructural: true);

    /// <summary>A wall whose centreline is 8 cm off the zone edge at x = 0: the edge lies in its thickness.</summary>
    private static MemberObservation OffsetWall() =>
        new(Source("W-offset"), CandidateCategory.Wall, new[] { P(0.08, 0), P(0.08, 10) }, widthFeet: M(0.2),
            typeUniqueId: WallType, typeName: "RC 200", isStructural: false);

    // --- 室內帷幕牆走 Settled 那一條路（帷幕牆規格 §4.8）-----------------------------------------
    //
    // `tw-bcr-79-wall-rating` 版本 3 的 appliesWhen 讀 `element.curtainWallExposure`，而 `Settled` 會
    // 為「構成邊界」與「不構成邊界」各評一次，兩次都用 `Assuming` 複製出來的 MemberCandidate。那份複本
    // 若沒把室內外判定帶過去，判定會讀成 Unknown、兩次都評成「不適用」，於是 `Settled` 以「不論是否
    // 構成邊界，結論都相同」逕行判定不適用——連人工覆核都不留。這是最糟的失敗型態：靜默，而且那片
    // 牆照樣有一列。

    /// <summary>版本 3 的 appliesWhen 逐字，用來測 `Assuming` 有沒有把室內外判定帶過去。</summary>
    private static Rule ExposureAwareWallRule() => RatingRule("wall-v3",
        "element.category == \"Walls\" && element.isCompartmentBoundary == true && " +
        "element.curtainWallExposure != \"Exterior\" && element.curtainWallExposure != \"Unknown\"",
        "element.providedFireRating >= 60 min");

    /// <summary>A｜B 分界（x = 10）上的室內帷幕牆，但中心線偏 80 mm：關係落 BoundaryOffCenterline。</summary>
    private static MemberObservation InteriorCurtainWallOffBoundary() =>
        new(Source("CW-off"), CandidateCategory.Wall, new[] { P(10.08, 0), P(10.08, 10) }, widthFeet: M(0.2),
            typeUniqueId: WallType, typeName: "帷幕牆", isStructural: false, isCurtainWall: true);

    [Theory]
    [InlineData("60", ReviewStatus.Pass)]
    [InlineData("30", ReviewStatus.ManualReview)]
    public void An_interior_curtain_wall_whose_boundary_relation_is_in_doubt_is_still_asked_for_its_rating(
        string provided, ReviewStatus expected)
    {
        var set = Set(ZonesAB(), InteriorCurtainWallOffBoundary());
        var member = set.Members.Single(m => m.Source.ElementUniqueId == "CW-off");

        Assert.Equal(CurtainWallExposure.Interior, member.CurtainWallExposure!.Exposure);
        Assert.Equal(CandidateAmbiguityKind.BoundaryOffCenterline, member.RelationTo(ZoneB)!.Ambiguity);

        var finding = Review(set, Ratings((WallType, provided)), Engine(ExposureAwareWallRule()))
            .Findings.Single(f => f.ElementUniqueId == "CW-off" && f.Result.ZoneId == ZoneB.ToString("D"));

        // 關鍵在於**不是** NotApplicable：那代表 Assuming 把判定丟成 Unknown、兩支都評不適用，
        // 於是這片室內區劃牆的第79條第1項時效被靜默免除。
        Assert.NotEqual(ReviewStatus.NotApplicable, finding.Status);
        Assert.Equal(expected, finding.Status);
    }

    // --- 室內外未定的帷幕牆仍是一列人工覆核（C-08、帷幕牆規格 §5.5 版本 3）-----------------------
    //
    // 規格的三分流：外牆帷幕牆不列入構件防火時效、室內帷幕牆照常列出、室內外未定的帷幕牆仍是一列
    // 人工覆核。既有測試只測到 CandidateResolver 產得出那個 ambiguity，沒有一條測它會不會出現在
    // FireResistanceReview.Findings 裡——而它原本不會：ambiguity 掛在 boundary 關係上，
    // AmbiguousMember 只認 Ambiguous 關係，比不到構件就 continue，那一列被靜默丟棄。

    /// <summary>
    /// B 區東緣（x = 20）上的帷幕牆：沿牆只有一側有區劃，幾何判外牆；型別 Function 卻宣告 Interior。
    /// 兩者相反 → 室內外 Unknown（DeclarationConflict），而它對 B 區是 boundary 關係。
    /// </summary>
    private static MemberObservation ExposureUndecidedCurtainWall(string uid = "CW-conflict", double x = 20) =>
        new(Source(uid), CandidateCategory.Wall, new[] { P(x, 0), P(x, 10) }, widthFeet: M(0.1),
            typeUniqueId: WallType, typeName: "帷幕牆", isStructural: false, isCurtainWall: true,
            curtainWallFunction: CurtainWallFunctionDeclaration.Interior);

    /// <summary>
    /// building 層輸入，含 <c>building.fireResistiveConstruction</c>。室內外未定那一列的護欄要求這個
    /// 事實**已知為 true** 才改走人工覆核，所以凡是測那條路的都得把它給齊；<c>null</c> 代表沒填。
    /// </summary>
    private static CompartmentAreaInputs FireResistive(bool? fireResistive = true, double? floors = 5)
    {
        var building = new List<ReviewInput>();
        if (floors is double f) building.Add(ReviewInput.Known("building.floorsAboveGround", f, ReviewUnit.None, "專案設定"));
        if (fireResistive is bool r) building.Add(ReviewInput.Known("building.fireResistiveConstruction", r, "專案設定"));
        return new CompartmentAreaInputs(building, null);
    }

    /// <summary>出貨規則集。護欄的對稱性只有在真的規則（六條都以防火構造開頭）上才測得出來。</summary>
    private static RuleEngine ShippedEngine()
    {
        var json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return new RuleEngine(loaded.Value);
    }

    [Fact]
    public void A_curtain_wall_of_undecided_exposure_is_reported_as_one_manual_review_row()
    {
        var set = Set(ZonesAB(), ExposureUndecidedCurtainWall());
        var member = set.Members.Single(m => m.Source.ElementUniqueId == "CW-conflict");

        Assert.Equal(CurtainWallExposure.Unknown, member.CurtainWallExposure!.Exposure);
        Assert.True(member.RelationTo(ZoneB)!.IsBoundary);

        var finding = Assert.Single(
            Review(set, Ratings(FireResistive(), (WallType, "60")), Engine(ExposureAwareWallRule())).Findings,
            f => f.ElementUniqueId == "CW-conflict");

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(CandidateAmbiguityKind.CurtainWallExposureUndecided, finding.Ambiguity!.Kind);

        // 訊息只有一個來源：CandidateResolver 寫的那一份。這裡逐字比對規格要的那句，
        // 以免有人在檢討端再抄一份複本，兩份日後各走各的。
        Assert.Contains("外牆不受第79條第1項區劃牆壁時效拘束", finding.Result.Message);
        Assert.Contains("無法判定它是建築物外牆或室內帷幕牆", finding.Result.Message);
    }

    /// <summary>
    /// 同一個構件×區劃只能出一列。主迴圈若不把這一輪跳掉，版本 3 的 appliesWhen 排除 Unknown
    /// 會再給一列 NoRuleApplies 的「不適用」，於是使用者看到自相矛盾的兩列。
    /// </summary>
    [Fact]
    public void An_undecided_curtain_wall_gets_no_second_not_applicable_row()
    {
        var set = Set(ZonesAB(), ExposureUndecidedCurtainWall());
        var review = Review(set, Ratings(FireResistive(), (WallType, "60")), Engine(ExposureAwareWallRule()));

        var rows = review.Findings.Concat(review.ExteriorCurtainWalls)
            .Where(f => f.ElementUniqueId == "CW-conflict").ToList();

        Assert.Single(rows);
        Assert.DoesNotContain(rows, f => f.Status == ReviewStatus.NotApplicable);
    }

    /// <summary>
    /// 一道牆同時是兩個區劃的邊界時，列數仍是 1:1：主迴圈跳掉兩輪、ambiguity 迴圈補回兩列，各一個區劃。
    /// </summary>
    /// <remarks>
    /// 兩個區劃上下疊在同一條東緣（x = 10）上，所以那道牆同時是兩者的邊界，而牆外沒有區劃 → 幾何
    /// 判外牆；型別 Function 宣告 Interior，兩者相反 → Unknown。不能改用「牆站在兩個區劃中間、
    /// Function 宣告 Exterior」：Exterior 是系統族預設值，依設計不足以否決幾何判出的室內
    /// （<c>CurtainWallExposureVerdict.Combine</c>），那樣造出來的是 Interior，不是 Unknown。
    /// </remarks>
    [Fact]
    public void An_undecided_curtain_wall_gets_one_row_per_zone_it_bounds()
    {
        var stacked = new[]
        {
            Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 5)),
            Zone(ZoneB, "B 區", "area-b", Rect(0, 5, 10, 10))
        };
        var set = Set(stacked, ExposureUndecidedCurtainWall("CW-both", 10));

        Assert.Equal(CurtainWallExposure.Unknown, set.Members.Single().CurtainWallExposure!.Exposure);

        var rows = Review(set, Ratings(FireResistive(), (WallType, "60")), Engine(ExposureAwareWallRule()))
            .Findings.Where(f => f.ElementUniqueId == "CW-both").ToList();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, f => Assert.Equal(ReviewStatus.ManualReview, f.Status));
        Assert.Equal(
            new[] { ZoneA.ToString("D"), ZoneB.ToString("D") }.OrderBy(x => x, StringComparer.Ordinal),
            rows.Select(f => f.Result.ZoneId).OrderBy(x => x, StringComparer.Ordinal));
    }

    // --- 室內外未定那一列的護欄：不能蓋掉本來不是「不適用」的列 ---------------------------------
    //
    // 主迴圈的 skip 與 ambiguity 迴圈的查找共用 IsExposureUndecidedBoundary，所以護欄放在述詞裡，
    // 跳掉幾列就一定補回幾列。護欄不成立時原本那一列講的是別的、而且更可執行的事：區劃範圍壞掉、
    // 這棟樓不是防火構造、防火構造還沒填、挑空內部線。以下四條各釘一種。

    [Fact]
    public void A_zone_whose_extent_is_in_doubt_still_withholds_instead_of_asking_about_exposure()
    {
        // 區劃重疊 → 兩個區劃都不 clear。室內外判定本來就是從區劃形狀量出來的，區劃壞掉時
        // 「先去修區劃邊界」才是病因，「判不出室內外」只是症狀。
        var overlapping = new[]
        {
            Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 10)),
            Zone(ZoneB, "B 區", "area-b", Rect(8, 0, 18, 10))
        };
        var set = Set(overlapping, ExposureUndecidedCurtainWall("CW-doubt", 18));

        Assert.Equal(CurtainWallExposure.Unknown, set.Members.Single().CurtainWallExposure!.Exposure);

        var finding = Only(Review(set, Ratings(FireResistive(), (WallType, "60")), Engine(ExposureAwareWallRule())));

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, finding.ErrorCode);
        Assert.Contains("範圍有問題", finding.Result.Message);
        Assert.DoesNotContain("無法判定它是建築物外牆或室內帷幕牆", finding.Result.Message);
    }

    /// <summary>
    /// 非防火構造建築物：出貨的六條構件規則 appliesWhen 全都以 <c>building.fireResistiveConstruction
    /// == true</c> 開頭，第79條對這棟樓根本不適用，每一列都該是不適用。這與
    /// <c>IsExteriorCurtainWallSetAside</c> 要求同一個事實為 true 是同一道護欄。
    /// </summary>
    [Fact]
    public void A_building_that_is_not_fire_resistive_keeps_its_not_applicable_row()
    {
        var set = Set(ZonesAB(), ExposureUndecidedCurtainWall());
        var finding = Only(Review(set, Ratings(FireResistive(fireResistive: false), (WallType, "60")), ShippedEngine()));

        Assert.Equal(ReviewStatus.NotApplicable, finding.Status);
        Assert.NotEqual(ReviewStatus.ManualReview, finding.Status);
        Assert.DoesNotContain("無法判定它是建築物外牆或室內帷幕牆", finding.Result.Message);
    }

    /// <summary>
    /// 防火構造還沒填：原本那一列講的是這棟樓本身的狀態，不能換成一個使用者無從下手的室內外問題。
    /// </summary>
    /// <remarks>
    /// 非結構的帷幕牆在這個狀態下出的是**不適用**而不是資料不足：出貨的六條構件規則除了防火構造
    /// 之外，每一條都還有另一個對這道牆明確為 false 的合項（Columns／StructuralFraming／Floors／
    /// <c>isStructural == true</c>／版本 3 排除 Unknown），三值邏輯下 <c>Unknown &amp;&amp; false</c>
    /// 仍是 false，所以每一條都確定不適用，引擎回 NoRuleApplies 而不是 UndecidedApplicability。
    /// 真正會停在資料不足的是**結構性**帷幕牆：第70條那條只被防火構造一個事實卡住。兩種都測，
    /// 因為護欄要守住的是「原本那一列是什麼就還是什麼」，不是某一個特定狀態。
    /// </remarks>
    [Fact]
    public void A_building_whose_fire_resistive_construction_is_unfilled_keeps_its_own_gap()
    {
        var nonStructural = Set(ZonesAB(), ExposureUndecidedCurtainWall());
        var plain = Only(Review(nonStructural, Ratings(FireResistive(fireResistive: null), (WallType, "60")), ShippedEngine()));

        Assert.Equal(ReviewStatus.NotApplicable, plain.Status);
        Assert.DoesNotContain("無法判定它是建築物外牆或室內帷幕牆", plain.Result.Message);

        var structural = Set(ZonesAB(), new MemberObservation(
            Source("CW-structural"), CandidateCategory.Wall, new[] { P(20, 0), P(20, 10) }, widthFeet: M(0.1),
            typeUniqueId: WallType, typeName: "帷幕牆", isStructural: true, isCurtainWall: true,
            curtainWallFunction: CurtainWallFunctionDeclaration.Interior));
        Assert.Equal(CurtainWallExposure.Unknown, structural.Members.Single().CurtainWallExposure!.Exposure);

        var gap = Only(Review(structural, Ratings(FireResistive(fireResistive: null), (WallType, "60")), ShippedEngine()));

        Assert.Equal(ReviewStatus.InsufficientData, gap.Status);
        Assert.Contains("building.fireResistiveConstruction", gap.Result.Message);
        Assert.DoesNotContain("無法判定它是建築物外牆或室內帷幕牆", gap.Result.Message);
    }

    // 第四種護欄（merged 挑空內部線）沒有測試，因為目前的分類器造不出那個組合：MergedAtriums
    // .IsInterior 要求沿線每一處都是「挑空｜連通區劃」，那正是幾何判 Interior 的條件，而
    // Combine() 在幾何判 Interior 時直接採用，不會回 Unknown（Function=Exterior 是系統族預設值，
    // 依設計不足以否決它）。那條 clause 因此是防禦性的：室內外判定與 MergedAtriums 是兩套各自
    // 取樣的實作，取樣深度與間距都不同，兩者在病態模型上答案分歧時它才會生效。

    /// <summary>
    /// 室內外未定 ＋ 邊界存疑（區劃邊線畫在牆面而非中心線）不能靜默收斂成不適用。版本 3 排除
    /// Unknown，所以 <c>Settled</c> 的「假設構成邊界」與「假設不構成」兩支都評不適用，那層
    /// 「不論是否構成邊界結論都相同」的推理會把一個真正的空缺讀成一致的答案。
    /// </summary>
    [Fact]
    public void An_undecided_curtain_wall_whose_boundary_is_also_in_doubt_is_not_silently_not_applicable()
    {
        // A 區東緣（x = 10）外面沒有區劃 → 幾何判外牆，Function 宣告 Interior → Unknown；
        // 中心線偏 80 mm，區劃邊線落在牆厚之內 → 關係是 BoundaryOffCenterline，走 Settled 那條路。
        var wall = new MemberObservation(Source("CW-off-unknown"), CandidateCategory.Wall,
            new[] { P(10.08, 0), P(10.08, 10) }, widthFeet: M(0.2),
            typeUniqueId: WallType, typeName: "帷幕牆", isStructural: false, isCurtainWall: true,
            curtainWallFunction: CurtainWallFunctionDeclaration.Interior);
        var set = Set(ZoneAOnly(), wall);

        Assert.Equal(CurtainWallExposure.Unknown, set.Members.Single().CurtainWallExposure!.Exposure);
        Assert.Equal(CandidateAmbiguityKind.BoundaryOffCenterline, set.Members.Single().RelationTo(ZoneA)!.Ambiguity);

        var rows = Review(set, Ratings(FireResistive(), (WallType, "60")), ShippedEngine())
            .Findings.Where(f => f.ElementUniqueId == "CW-off-unknown").ToList();

        Assert.NotEmpty(rows);
        Assert.All(rows, f => Assert.Equal(ReviewStatus.ManualReview, f.Status));
        Assert.DoesNotContain(rows, f => f.Status == ReviewStatus.NotApplicable);
    }

    /// <summary>
    /// 第三種分流不能把前兩種弄壞：A｜B 分界線上的室內帷幕牆照常列出，而且讀自己的時效。
    /// 兩個區劃各一列，因為它同時是兩個區劃的牆壁。
    /// </summary>
    [Fact]
    public void An_interior_curtain_wall_on_a_boundary_is_still_asked_for_its_own_rating()
    {
        var wall = new MemberObservation(Source("CW-interior"), CandidateCategory.Wall,
            new[] { P(10, 0), P(10, 10) }, widthFeet: M(0.1),
            typeUniqueId: WallType, typeName: "帷幕牆", isStructural: false, isCurtainWall: true);
        var set = Set(ZonesAB(), wall);

        Assert.Equal(CurtainWallExposure.Interior, set.Members.Single().CurtainWallExposure!.Exposure);

        var rows = Review(set, Ratings((WallType, "30")), Engine(ExposureAwareWallRule()))
            .Findings.Where(f => f.ElementUniqueId == "CW-interior").ToList();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, f => Assert.Equal(ReviewStatus.Fail, f.Status));
        Assert.All(rows, f => Assert.Equal(ReviewValue.Quantity(30, ReviewUnit.Minute), f.Result.ActualValue));
    }

    [Theory]
    [InlineData("30", ReviewStatus.Fail)]
    [InlineData("2 hr", ReviewStatus.Pass)]
    public void A_column_flush_with_the_boundary_is_decided_because_its_rating_does_not_depend_on_the_compartment(string provided, ReviewStatus expected)
    {
        // 第70條 asks the column for its rating whether or not it bounds a zone, and 第79條 asks only the
        // 牆壁: the geometric doubt cannot change the answer, so it is not handed to a person.
        var finding = Only(Review(Set(ZoneAOnly(), FlushColumn()), Ratings((ColumnType, provided))));
        var result = finding.Result;

        Assert.Equal(expected, result.Status);
        Assert.Equal(ReviewValue.Quantity(120, ReviewUnit.Minute), result.RequiredValue);
        Assert.Equal("column", result.RuleId);
        Assert.NotNull(finding.Outcome);
        Assert.Null(finding.ErrorCode);
        Assert.Equal(CandidateAmbiguityKind.BoundaryAlongOutline, finding.Ambiguity!.Kind);
        Assert.Contains("逕行判定", result.Message);
        Assert.Equal(ReviewValue.OfText("SameVerdictEitherWay"), result.Evidence.Find("candidate.settledBy"));
        Assert.Equal(ReviewValue.OfText("Ambiguous"), result.Evidence.Find("candidate.relation"));
        Assert.Equal(ReviewValue.OfText("BoundaryAlongOutline"), result.Evidence.Find("candidate.ambiguity"));
        Assert.Equal(ColumnType, finding.TypeUniqueId);
    }

    [Fact]
    public void A_wall_that_meets_the_boundary_requirement_passes_whether_or_not_it_bounds_the_zone()
    {
        // Bounding: 第79條 one hour, met. Not bounding: no requirement. Either way nothing fails.
        var finding = Only(Review(Set(ZoneAOnly(), OffsetWall()), Ratings((WallType, "60"))));

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal("wall", finding.Result.RuleId);
        Assert.Equal(ReviewValue.OfText(ReviewStatusText.Label(ReviewStatus.NotApplicable)), finding.Result.Evidence.Find("candidate.verdictIfNotBoundary"));
    }

    [Theory]
    [InlineData("30")] // bounding: Fail; not bounding: no requirement
    [InlineData("")]   // bounding: 資料不足; not bounding: no requirement
    public void An_ambiguous_member_whose_verdict_depends_on_the_boundary_stays_manual_review(string provided)
    {
        var finding = Only(Review(Set(ZoneAOnly(), OffsetWall()), Ratings((WallType, provided))));
        var result = finding.Result;

        Assert.Equal(ReviewStatus.ManualReview, result.Status);
        Assert.Equal(CandidateAmbiguityKind.BoundaryOffCenterline, finding.Ambiguity!.Kind);
        Assert.Equal(ReviewErrorCode.CandidateAmbiguous, finding.ErrorCode);
        Assert.Null(finding.Outcome);
        Assert.Null(result.RequiredValue);
        Assert.Equal("tw-bcr-fire", result.RuleId);
        Assert.Equal("2024.1", result.RuleVersion);
        Assert.Equal(ReviewCheckTypes.FireResistance, result.CheckType);
        Assert.Equal(ZoneA.ToString("D"), result.ZoneId);
    }

    [Theory]
    [InlineData("1hr/2hr")] // composite: 人工覆核 either way
    [InlineData("")]        // missing: 資料不足 either way
    public void Two_doubtful_answers_do_not_settle_a_boundary_doubt(string provided)
    {
        var finding = Only(Review(Set(ZoneAOnly(), FlushColumn()), Ratings((ColumnType, provided))));

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.CandidateAmbiguous, finding.ErrorCode);
        Assert.Null(finding.Outcome);
        Assert.DoesNotContain("逕行判定", finding.Result.Message);
        Assert.Null(finding.Result.Evidence.Find("candidate.settledBy"));
    }

    [Fact]
    public void A_boundary_doubt_in_a_zone_whose_extent_is_in_doubt_is_not_settled()
    {
        var overlapping = new[]
        {
            Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 10)),
            Zone(ZoneB, "B 區", "area-b", Rect(8, 0, 18, 10))
        };
        var review = Review(Set(overlapping, FlushColumn()), Ratings((ColumnType, "2 hr")));

        Assert.All(review.Findings, f => Assert.Equal(ReviewStatus.ManualReview, f.Status));
    }

    // --- 牆跨越區劃邊界（C-18）--------------------------------------------------------------------
    //
    // ZoneRelationKind.Crossing 原本不帶任何 CandidateAmbiguityKind，於是 IsBoundary 為 false、
    // element.isCompartmentBoundary 為 false、tw-bcr-79-wall-rating 的 appliesWhen 不成立，整列
    // 「所列規則均不適用」——沒有未符合、沒有待確認、連人工覆核都沒有，第79條第1項被靜默免除。
    // 同一個 resolver 對「開口的 host 牆跨越邊界」早就是 HostRelationAmbiguous 的人工覆核，牆自己
    // 卻不是。現在 Crossing 掛 BoundaryCrossed 並納入 IsBoundaryDoubt，走與 BoundaryOffCenterline
    // 同一條 Settled 兩支推理。

    /// <summary>A｜B 分界（x = 10）上橫跨過去的牆：區劃內外各 2 m，落在邊界上的長度為 0。</summary>
    private static MemberObservation CrossingWall(string uid = "W-crossing", bool curtainWall = false) =>
        new(Source(uid), CandidateCategory.Wall, new[] { P(8, 5), P(12, 5) }, widthFeet: M(0.2),
            typeUniqueId: WallType, typeName: curtainWall ? "帷幕牆" : "RC 200",
            isStructural: !curtainWall, isCurtainWall: curtainWall);

    /// <summary>A｜B 分界上橫跨過去的梁，用來測第70條那條不看區劃的路。</summary>
    private static MemberObservation CrossingBeam() =>
        new(Source("B-crossing"), CandidateCategory.StructuralFraming, new[] { P(8, 5), P(12, 5) },
            widthFeet: M(0.3), typeUniqueId: BeamType, typeName: "B30x60", isStructural: true);

    /// <summary>
    /// V-27 第 11 項的實機夾具：室內帷幕牆跨越區劃邊界、設計防火時效 2 h。原本整列「不適用」，
    /// 現在要落在「若構成區劃邊界則符合第79條區劃牆壁之要求，若不構成邊界則無此要求」那一支，
    /// 判定為**符合**。
    /// </summary>
    [Fact]
    public void An_interior_curtain_wall_crossing_the_boundary_is_judged_not_silently_set_aside()
    {
        var set = Set(ZonesAB(), CrossingWall("CW-crossing", curtainWall: true));
        var member = set.Members.Single();

        Assert.Equal(CurtainWallExposure.Interior, member.CurtainWallExposure!.Exposure);
        var relation = member.RelationTo(ZoneA)!;
        Assert.Equal(ZoneRelationKind.Crossing, relation.Kind);
        Assert.Equal(CandidateAmbiguityKind.BoundaryCrossed, relation.Ambiguity);
        Assert.True(relation.CarriesAmbiguity);
        Assert.False(relation.IsBoundary);

        var finding = Review(set, Ratings(FireResistive(), (WallType, "2 hr")), Engine(ExposureAwareWallRule()))
            .Findings.Single(f => f.ElementUniqueId == "CW-crossing" && f.Result.ZoneId == ZoneA.ToString("D"));
        var result = finding.Result;

        // 關鍵在於**不是** NotApplicable：那正是 C-18 的靜默免除。
        Assert.NotEqual(ReviewStatus.NotApplicable, result.Status);
        Assert.Equal(ReviewStatus.Pass, result.Status);
        Assert.Equal("wall-v3", result.RuleId);
        Assert.Equal(ReviewValue.Quantity(120, ReviewUnit.Minute), result.ActualValue);
        Assert.Equal(ReviewValue.Quantity(60, ReviewUnit.Minute), result.RequiredValue);
        Assert.Equal(CandidateAmbiguityKind.BoundaryCrossed, finding.Ambiguity!.Kind);

        // 兩支都評過，而且明細留得下來：這是「免人工覆核」的唯一正當理由。
        Assert.Equal(ReviewValue.OfText("SameVerdictEitherWay"), result.Evidence.Find("candidate.settledBy"));
        Assert.Equal(ReviewValue.OfText(ReviewStatusText.Label(ReviewStatus.Pass)),
            result.Evidence.Find("candidate.verdictIfBoundary"));
        Assert.Equal(ReviewValue.OfText(ReviewStatusText.Label(ReviewStatus.NotApplicable)),
            result.Evidence.Find("candidate.verdictIfNotBoundary"));
        Assert.Contains("若構成區劃邊界則符合第79條區劃牆壁之要求，若不構成邊界則無此要求", result.Message);

        // 明細仍要看得出關係本來是「跨越」，不是被改寫成邊界。
        Assert.Equal(ReviewValue.OfText("Crossing"), result.Evidence.Find("candidate.relation"));
        Assert.Equal(ReviewValue.OfText("BoundaryCrossed"), result.Evidence.Find("candidate.ambiguity"));
    }

    [Fact]
    public void A_plain_wall_crossing_the_boundary_and_meeting_article_79_passes()
    {
        var finding = Review(Set(ZoneAOnly(), CrossingWall()), Ratings((WallType, "60")))
            .Findings.Single(f => f.ElementUniqueId == "W-crossing");

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal("wall", finding.Result.RuleId);
        Assert.Equal(CandidateAmbiguityKind.BoundaryCrossed, finding.Ambiguity!.Kind);
        Assert.Contains("逕行判定", finding.Result.Message);
    }

    /// <summary>兩支結論衝突（構成邊界 → 未符合、不構成 → 不適用）時，回人工覆核而不是替使用者選一個。</summary>
    [Theory]
    [InlineData("30")] // 構成邊界：未符合；不構成：無要求
    [InlineData("")]   // 構成邊界：資料不足；不構成：無要求
    public void A_wall_crossing_the_boundary_whose_verdict_depends_on_it_stays_manual_review(string provided)
    {
        var finding = Only(Review(Set(ZoneAOnly(), CrossingWall()), Ratings((WallType, provided))));
        var result = finding.Result;

        Assert.Equal(ReviewStatus.ManualReview, result.Status);
        Assert.Equal(CandidateAmbiguityKind.BoundaryCrossed, finding.Ambiguity!.Kind);
        Assert.Equal(ReviewErrorCode.CandidateAmbiguous, finding.ErrorCode);
        Assert.Null(result.Evidence.Find("candidate.settledBy"));
        Assert.Contains("無法判定是否由此牆構成該區劃邊界", result.Message);
        Assert.Equal(ZoneA.ToString("D"), result.ZoneId);
    }

    /// <summary>
    /// 同一個構件×區劃只能出一列：主迴圈跳掉幾輪，ambiguity 迴圈就要補回幾列。跨越邊界的牆同時與
    /// 兩個區劃是 Crossing，所以兩個區劃各一列，而且沒有任何一列是靜默的「不適用」。
    /// </summary>
    [Fact]
    public void A_wall_crossing_the_boundary_gets_exactly_one_row_per_zone()
    {
        var review = Review(Set(ZonesAB(), CrossingWall()), Ratings((WallType, "60")));
        var rows = review.Findings.Concat(review.ExteriorCurtainWalls)
            .Where(f => f.ElementUniqueId == "W-crossing").ToList();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, f => Assert.Equal(ReviewStatus.Pass, f.Status));
        Assert.Equal(
            new[] { ZoneA.ToString("D"), ZoneB.ToString("D") }.OrderBy(x => x, StringComparer.Ordinal),
            rows.Select(f => f.Result.ZoneId).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>第70條不看構件是否構成區劃邊界，所以跨越邊界的梁兩支結論相同，逕行判定。</summary>
    [Theory]
    [InlineData("2 hr", ReviewStatus.Pass)]
    [InlineData("60", ReviewStatus.Fail)]
    public void A_beam_crossing_the_boundary_is_decided_because_article_70_does_not_depend_on_the_compartment(
        string provided, ReviewStatus expected)
    {
        var finding = Only(Review(Set(ZoneAOnly(), CrossingBeam()), Ratings((BeamType, provided))));

        Assert.Equal(expected, finding.Status);
        Assert.Equal("beam", finding.Result.RuleId);
        Assert.Equal(CandidateAmbiguityKind.BoundaryCrossed, finding.Ambiguity!.Kind);
        Assert.Contains("不論此構件是否構成區劃邊界，規則的結論都相同", finding.Result.Message);
    }

    /// <summary>
    /// 樓板的 Crossing 不是邊界疑義（一片樓板本來就跨多個區劃），所以它照舊走主迴圈的 Decide，
    /// 不多出一列人工覆核，也不帶 settledBy。這一條與上面的梁成對，守住 C-18 的修正範圍。
    /// </summary>
    [Fact]
    public void A_floor_crossing_a_zone_boundary_is_not_a_boundary_doubt()
    {
        var slab = new MemberObservation(Source("F-crossing"), CandidateCategory.Floor,
            outlines: new[] { Rect(5, 0, 15, 10) }, typeUniqueId: FloorType, typeName: "RC 150", isStructural: true);
        var set = Set(ZoneAOnly(), slab);

        var relation = set.Members.Single().RelationTo(ZoneA)!;
        Assert.Equal(ZoneRelationKind.Crossing, relation.Kind);
        Assert.False(relation.CarriesAmbiguity);
        Assert.DoesNotContain(set.Ambiguities, a => a.SubjectUniqueIds.Contains("F-crossing"));

        var finding = Only(Review(set, Ratings((FloorType, "90"))));
        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Null(finding.Ambiguity);
        Assert.Null(finding.Result.Evidence.Find("candidate.settledBy"));
    }

    [Fact]
    public void Linked_members_and_members_without_plan_geometry_are_manual_review()
    {
        var linked = WallAt("W-linked", 0, link: "link-instance");
        var noGeometry = new MemberObservation(Source("W-nogeometry"), CandidateCategory.Wall, widthFeet: M(0.2),
            typeUniqueId: WallType, typeName: "RC 200");
        var review = Review(Set(ZoneAOnly(), linked, noGeometry, WallAt("W-edge", 10)), Ratings((WallType, "60")));

        var link = review.For("W-linked", ZoneA)!;
        Assert.Equal(ReviewStatus.ManualReview, link.Status);
        Assert.Equal(CandidateAmbiguityKind.LinkedElement, link.Ambiguity!.Kind);

        var none = Assert.Single(review.For("W-nogeometry"));
        Assert.Equal(ReviewStatus.ManualReview, none.Status);
        Assert.Null(none.ZoneId);
        Assert.Equal(CandidateAmbiguityKind.NoPlanGeometry, none.Ambiguity!.Kind);
        Assert.Equal(CandidateCategory.Wall, none.Category);
        Assert.Equal(WallType, none.TypeUniqueId);

        Assert.Equal(ReviewStatus.Pass, review.For("W-edge", ZoneA)!.Status);
        Assert.Single(review.Types);
    }

    [Fact]
    public void Members_of_a_zone_whose_extent_is_in_doubt_are_manual_review_without_a_rule()
    {
        var overlapping = new[]
        {
            Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 10)),
            Zone(ZoneB, "B 區", "area-b", Rect(8, 0, 18, 10))
        };
        var finding = Only(Review(Set(overlapping, WallAt("W-edge", 0)), Ratings((WallType, "30"))));

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, finding.ErrorCode);
        Assert.Null(finding.Outcome);
        Assert.Equal("tw-bcr-fire", finding.Result.RuleId);
        Assert.Contains("ZonesOverlap", finding.Result.Message);
        Assert.Equal(ReviewValue.OfText("ZonesOverlap"), finding.Result.Evidence.Find("zone.problems"));
    }

    [Fact]
    public void A_package_without_zones_or_with_an_unmeasurable_zone_is_not_reviewed()
    {
        var none = FireResistanceCheck.Review(Set(Array.Empty<ZoneObservation>()), Ratings(), Engine(), Today, RunId);
        var open = FireResistanceCheck.Review(Set(new[] { Zone(ZoneC, "C 區", "area-c") }), Ratings(), Engine(), Today, RunId);

        Assert.True(none.IsFailure);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, none.Error.Code);
        Assert.Contains("無法檢討構件防火時效", none.Error.Message);
        Assert.True(open.IsFailure);
        Assert.Contains("「C 區」", open.Error.Message);
    }

    [Fact]
    public void Without_a_fire_resistance_rule_every_member_is_manual_review()
    {
        var areaOnly = new Rule("79", "1", RuleCategory.CompartmentArea, "第79條", new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("zone.area > 0 m2"), new RuleExpression("zone.area <= 1500 m2"));
        var finding = Only(Review(Set(ZoneAOnly(), EdgeMember(CandidateCategory.Wall)), Ratings((WallType, "60")), Engine(areaOnly)));

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.RuleMissing, finding.ErrorCode);
    }

    // --- Type 彙總 --------------------------------------------------------------------------------

    [Fact]
    public void Results_are_gathered_by_Type_with_each_instance_once()
    {
        var review = Review(
            Set(ZonesAB(),
                WallAt("W-left", 0), WallAt("W-shared", 10), WallAt("W-right", 20),
                WallAt("W-thin", 5, "type-thin", "LGS 100"),
                EdgeMember(CandidateCategory.Column)),
            Ratings((WallType, "60"), ("type-thin", "30")));

        Assert.Equal(new[] { "type-thin", WallType, ColumnType }, review.Types.Select(t => t.TypeUniqueId));
        Assert.Equal(new[] { "LGS 100", "RC 200", "C40x40" }, review.Types.Select(t => t.TypeName));

        var rc = review.Type(WallType)!;
        Assert.Equal(new[] { "W-left", "W-right", "W-shared" }, rc.ElementUniqueIds);
        Assert.Equal(3, rc.InstanceCount);
        Assert.Equal(4, rc.Findings.Count);
        Assert.Equal(4, rc.Count(ReviewStatus.Pass));
        Assert.Equal(ReviewStatus.Pass, rc.Status);
        Assert.Equal(60, rc.HighestRequiredMinutes);
        Assert.Equal(60, rc.Provided!.Rating.Minutes);

        var thin = review.Type("type-thin")!;
        Assert.Equal(ReviewStatus.NotApplicable, thin.Status); // a partition inside A: no rule applies

        var column = review.Type(ColumnType)!;
        Assert.Equal(ReviewStatus.InsufficientData, column.Status);
        Assert.Null(column.Provided);
        Assert.Equal(120, column.HighestRequiredMinutes);
    }

    [Theory]
    [InlineData("30", ReviewStatus.Fail)]
    [InlineData("60", ReviewStatus.ManualReview)]
    public void A_Type_passes_only_when_every_instance_does(string rating, ReviewStatus expected)
    {
        // Same Type: one wall reviewed normally, one in a linked model (ManualReview).
        var review = Review(Set(ZoneAOnly(), WallAt("W-edge", 0), WallAt("W-linked", 10, link: "link-instance")),
            Ratings((WallType, rating)));

        var type = Assert.Single(review.Types);
        Assert.Equal(expected, type.Status);
        Assert.Equal(1, type.Count(ReviewStatus.ManualReview));
    }

    // --- 輸入契約、可重現、可保存 --------------------------------------------------------------------

    [Fact]
    public void Inputs_reject_two_ratings_for_one_Type_and_report_what_they_could_not_use()
    {
        Assert.Throws<ArgumentException>(() => Ratings((WallType, "60"), (WallType, "120")));

        var context = new CompartmentAreaInputs(null, new Dictionary<Guid, IEnumerable<ReviewInput>>
        {
            [ZoneC] = new[] { ReviewInput.Known("zone.use", "辦公") }
        });
        var review = Review(Set(ZoneAOnly(), EdgeMember(CandidateCategory.Wall)), Ratings(context, (WallType, "60"), ("type-ghost", "60")));

        Assert.Equal(2, review.Warnings.Count);
        Assert.Contains(review.Warnings, w => w.Contains(ZoneC.ToString("D")));
        Assert.Contains(review.Warnings, w => w.Contains("type-ghost"));
    }

    [Fact]
    public void Results_are_ordered_reproducible_and_fit_a_review_run_that_round_trips()
    {
        CandidateSet Build(bool reversed)
        {
            var members = new[]
            {
                WallAt("W-shared", 10), WallAt("W-left", 0), EdgeMember(CandidateCategory.StructuralFraming),
                EdgeMember(CandidateCategory.Floor), EdgeMember(CandidateCategory.Column)
            };
            return Set(ZonesAB(), reversed ? members.Reverse().ToArray() : members);
        }

        var inputs = Ratings((WallType, "60"), (ColumnType, "90"), (BeamType, "2 hr"), (FloorType, "1hr/2hr"));
        var review = Review(Build(false), inputs);
        var again = Review(Build(true), inputs);

        Assert.Equal(
            new[] { "W-left", "W-shared", "C-edge", "B-edge", "F-slab", "W-shared", "B-edge" },
            review.Findings.Select(f => f.ElementUniqueId));
        // The beam's end sits on B's boundary within its width: ambiguous there, filed under zone B in order.
        Assert.Equal(CandidateAmbiguityKind.BoundaryOffCenterline, review.For("B-edge", ZoneB)!.Ambiguity!.Kind);
        Assert.Equal(review.Results.Select(r => (r.ResultId, r.ZoneId, r.Status, r.Message)),
            again.Results.Select(r => (r.ResultId, r.ZoneId, r.Status, r.Message)));

        var started = new DateTime(2025, 6, 1, 1, 0, 0, DateTimeKind.Utc);
        var run = new ReviewRun(RunId, PackageId, "tw-bcr-fire", "2024.1", 3, started).Complete(review.Results, started.AddMinutes(1));
        var back = ReviewRunStorageMapper.FromRecord(ReviewRunStorageMapper.ToRecord(run));

        Assert.Equal(run.Results.Select(x => (x.ZoneId, x.Status, x.Message, x.ActualValue, x.RequiredValue)),
            back.Results.Select(x => (x.ZoneId, x.Status, x.Message, x.ActualValue, x.RequiredValue)));
        Assert.Equal(run.Results[0].Evidence.Items.Select(x => x.Field), back.Results[0].Evidence.Items.Select(x => x.Field));
    }

    [Fact]
    public void The_valid_fixture_reviews_a_boundary_wall_end_to_end()
    {
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "Fixtures", "valid-ruleset.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), json));
        Assert.True(loaded.IsSuccess, loaded.Error.TechnicalDetail);

        var finding = Only(Review(Set(ZoneAOnly(), EdgeMember(CandidateCategory.Wall)), Ratings((WallType, "1 hr")), new RuleEngine(loaded.Value)));

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal("tw-bcr-79-wall-rating", finding.Result.RuleId);
        Assert.Equal(ReviewValue.OfText("RC 200"), finding.Result.Evidence.Find("element.typeName"));
    }

    /// <summary>
    /// 外牆帷幕牆不列入構件防火時效：檢討表、Type 彙總都沒有它。留存的判定指向帷幕牆的判定原則
    /// （第79條第3項、第4項、第79條之3、第79條之4），而不是候選規則清單的第一條。
    /// </summary>
    [Fact]
    public void An_exterior_curtain_wall_is_not_reported_and_is_left_to_the_curtain_wall_rules()
    {
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), json));
        Assert.True(loaded.IsSuccess, loaded.Error.TechnicalDetail);

        // A 區的西側邊界（x = 0）外面沒有區劃：沿牆只有一側有區劃，判為建築物外牆。
        var facade = new MemberObservation(Source("CW-facade"), CandidateCategory.Wall, new[] { P(0, 0), P(0, 10) }, widthFeet: M(0.025),
            typeUniqueId: WallType, typeName: "帷幕牆", isStructural: false, isCurtainWall: true);
        var set = Set(ZoneAOnly(), facade);
        Assert.Equal(CurtainWallExposure.Exterior, set.Members.Single().CurtainWallExposure!.Exposure);

        var building = new CompartmentAreaInputs(new[]
        {
            ReviewInput.Known("building.fireResistiveConstruction", true, "專案設定"),
            ReviewInput.Known("building.floorsAboveGround", 5, ReviewUnit.None, "專案設定")
        }, null);
        var review = Review(set, Ratings(building), new RuleEngine(loaded.Value));

        Assert.Empty(review.Findings);
        Assert.Empty(review.Results);
        Assert.Empty(review.Types);
        var finding = Assert.Single(review.ExteriorCurtainWalls);
        Assert.True(finding.IsExteriorCurtainWall);

        var result = finding.Result;
        Assert.True(result.Status == ReviewStatus.NotApplicable, result.Message);
        Assert.Equal(loaded.Value.RuleSet.RuleSetId, result.RuleId);
        Assert.Equal(FireResistanceCheck.ExteriorCurtainWallReference, result.LegalReference);
        Assert.DoesNotContain("第70條", result.LegalReference);
        Assert.Contains("帷幕牆區劃交接", result.Message);

        var reference = ReviewLegalReference.Parse(result.LegalReference);
        Assert.Equal(new[] { "第79條第3項、第4項", "第79條之3", "第79條之4" }, reference.Clauses);
    }
}
