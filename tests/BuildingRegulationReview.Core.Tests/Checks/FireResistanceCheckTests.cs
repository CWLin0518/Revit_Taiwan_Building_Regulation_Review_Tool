using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
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
}
