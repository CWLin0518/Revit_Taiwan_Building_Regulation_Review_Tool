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
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Checks;

public sealed class CompartmentAreaCheckTests
{
    private static readonly RuleEvaluationContext Today = new(new DateTime(2025, 6, 1), "TW");
    private static readonly Guid RunId = Guid.Parse("99999999-0000-0000-0000-000000000001");

    private static Rule AreaRule(string ruleId = "79", string version = "1") =>
        new(ruleId, version, RuleCategory.CompartmentArea, "建築技術規則建築設計施工編第79條第1項", new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("building.fireResistiveConstruction == true"),
            new RuleExpression("zone.area <= (zone.sprinklered ? 3000 m2 : 1500 m2)"),
            new[] { new RuleExpression("zone.use == \"樓梯間\"") },
            new[] { "zone.sprinklered", "building.fireResistiveConstruction", "zone.use" });

    private static RuleEngine Engine(params Rule[] rules)
    {
        var compiled = RuleSetCompiler.Compile(new RuleSet("tw-bcr-fire", "2024.1", "防火區劃", rules));
        Assert.True(compiled.IsSuccess, compiled.Error.TechnicalDetail);
        return new RuleEngine(compiled.Value);
    }

    /// <summary>A zone of one rectangular Area, x 0..width, y at the given offset; Revit reports <paramref name="revitM2"/>.</summary>
    private static ZoneObservation RectZone(Guid id, string name, string areaUid, double width, double depth, double? revitM2, double x0 = 0) =>
        new(id, name, new[]
        {
            new ZonePartObservation(areaUid, new[] { Rect(x0, 0, x0 + width, depth) },
                revitM2 is double m2 ? PlanUnits.SquareMetersToSquareFeet(m2) : (double?)null)
        });

    private static CandidateSet Set(params ZoneObservation[] zones) =>
        CandidateResolver.Resolve(new CandidateObservationSet(PackageId, "level-1F", "1F", zones,
            Array.Empty<MemberObservation>(), Array.Empty<OpeningObservation>()));

    /// <summary>A single zone measuring width × 30 m, with Revit agreeing with the boundary unless told otherwise.</summary>
    private static CandidateSet OneZone(double areaM2, double? revitM2 = double.NaN) =>
        Set(RectZone(ZoneA, "A 區", "area-a", areaM2 / 30.0, 30, revitM2 is double r && double.IsNaN(r) ? areaM2 : revitM2));

    private static CompartmentAreaInputs Inputs(bool? fireResistive = true, bool? sprinklered = false, string? use = "辦公", Guid? zone = null)
    {
        var building = new List<ReviewInput>();
        if (fireResistive is bool f) building.Add(ReviewInput.Known("building.fireResistiveConstruction", f, "專案設定"));
        var zoneInputs = new List<ReviewInput>();
        if (sprinklered is bool s) zoneInputs.Add(ReviewInput.Known("zone.sprinklered", s, "防火檢討_自動滅火設備"));
        if (use is not null) zoneInputs.Add(ReviewInput.Known("zone.use", use, "防火檢討_區劃用途"));
        return new CompartmentAreaInputs(building, new Dictionary<Guid, IEnumerable<ReviewInput>> { [zone ?? ZoneA] = zoneInputs });
    }

    private static CompartmentAreaReview Review(CandidateSet set, CompartmentAreaInputs inputs, RuleEngine? engine = null, CompartmentAreaOptions? options = null)
    {
        var ids = Enumerable.Range(1, 100).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).GetEnumerator();
        var review = CompartmentAreaCheck.Review(set, inputs, engine ?? Engine(AreaRule()), Today, RunId, options,
            () => { ids.MoveNext(); return ids.Current; });
        Assert.True(review.IsSuccess, review.Error.ToString());
        return review.Value;
    }

    private static ZoneAreaFinding Single(CompartmentAreaReview review) => Assert.Single(review.Findings);

    /// <summary>A 垂直區劃 is not reported as a 區劃面積 result; its decision is only kept aside.</summary>
    private static ZoneAreaFinding SetAside(CompartmentAreaReview review)
    {
        Assert.Empty(review.Findings);
        Assert.Empty(review.Results);
        var finding = Assert.Single(review.VerticalCompartments);
        Assert.True(finding.IsVerticalCompartment);
        return finding;
    }

    private static double Number(ReviewValue? value) => Assert.IsType<ReviewValue>(value).Number;

    // --- 等於上限、超限 -----------------------------------------------------------------------------

    [Theory]
    [InlineData(1499.9, false, ReviewStatus.Pass)]
    [InlineData(1500, false, ReviewStatus.Pass)]
    [InlineData(1500.5, false, ReviewStatus.Fail)]
    [InlineData(3000, true, ReviewStatus.Pass)]
    [InlineData(3000.5, true, ReviewStatus.Fail)]
    public void Area_equal_to_the_limit_passes_and_above_it_fails(double area, bool sprinklered, ReviewStatus expected)
    {
        var finding = Single(Review(OneZone(area), Inputs(sprinklered: sprinklered)));
        var result = finding.Result;

        Assert.Equal(expected, result.Status);
        Assert.Equal(area, Number(result.ActualValue), 6);
        Assert.Equal(ReviewUnit.SquareMeter, result.ActualValue!.Unit);
        Assert.Equal(ReviewValue.Quantity(sprinklered ? 3000 : 1500, ReviewUnit.SquareMeter), result.RequiredValue);
        Assert.Equal(AreaCrossCheck.Agrees, finding.CrossCheck);
        Assert.Null(finding.ErrorCode);

        Assert.Equal(ReviewCheckTypes.CompartmentArea, result.CheckType);
        Assert.Equal(ZoneA.ToString("D"), result.ZoneId);
        Assert.Equal(new[] { "area-a" }, result.SubjectUniqueIds);
        Assert.Equal(PackageId, result.PackageId);
        Assert.Equal(RunId, result.RunId);
        Assert.Equal("79", result.RuleId);
        Assert.Equal("1", result.RuleVersion);
        Assert.Equal("建築技術規則建築設計施工編第79條第1項", result.LegalReference);
        Assert.StartsWith("區劃「A 區」：", result.Message);
    }

    [Fact]
    public void The_result_keeps_the_calculation_the_clause_and_where_inputs_came_from()
    {
        var result = Single(Review(OneZone(1500), Inputs())).Result;
        var evidence = result.Evidence;

        Assert.Equal(1500, Number(evidence.Find("zone.area")), 6);
        Assert.Equal(ReviewValue.OfBoolean(false), evidence.Find("zone.sprinklered"));
        Assert.Equal(ReviewValue.OfBoolean(true), evidence.Find("building.fireResistiveConstruction"));
        Assert.Equal(ReviewValue.OfText("辦公"), evidence.Find("zone.use"));
        Assert.Equal(ReviewValue.OfText("A 區"), evidence.Find("zone.name"));
        Assert.Equal(1, Number(evidence.Find("area.partCount")));
        Assert.Equal(1500, Number(evidence.Find("area.part[area-a].revit")), 6);
        Assert.Equal(1500, Number(evidence.Find("area.part[area-a].geometric")), 6);
        Assert.Equal(1500, Number(evidence.Find("area.revit")), 6);
        Assert.Equal(1500, Number(evidence.Find("area.geometric")), 6);
        Assert.Equal(ReviewValue.OfText("Agrees"), evidence.Find("area.crossCheck"));
        Assert.True(Number(evidence.Find("area.relativeDifference")) < 1e-9);
        Assert.Equal(0.01, Number(evidence.Find("area.crossCheckTolerance")));
        Assert.Equal(ReviewValue.OfText("專案設定"), evidence.Find("source[building.fireResistiveConstruction]"));
        Assert.Equal(ReviewValue.OfText("防火檢討_自動滅火設備"), evidence.Find("source[zone.sprinklered]"));
        Assert.False(evidence.Has("rule.gaps"));
    }

    [Fact]
    public void A_zone_of_several_Areas_is_reviewed_on_their_sum()
    {
        var zone = new ZoneObservation(ZoneA, "A 區", new[]
        {
            new ZonePartObservation("area-a1", new[] { Rect(0, 0, 30, 30) }, PlanUnits.SquareMetersToSquareFeet(900)),
            new ZonePartObservation("area-a2", new[] { Rect(40, 0, 60, 30) }, PlanUnits.SquareMetersToSquareFeet(600))
        });
        var finding = Single(Review(Set(zone), Inputs()));

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal(1500, Number(finding.Result.ActualValue), 6);
        Assert.Equal(new[] { "area-a1", "area-a2" }, finding.Result.SubjectUniqueIds);
        Assert.Equal(900, Number(finding.Result.Evidence.Find("area.part[area-a1].revit")), 6);
        Assert.Equal(600, Number(finding.Result.Evidence.Find("area.part[area-a2].geometric")), 6);
    }

    [Fact]
    public void A_hole_is_not_counted_in_the_measured_area()
    {
        // 40 × 40 with a 10 × 10 hole: 1500 m² net, the Revit Area excludes the hole too.
        var zone = new ZoneObservation(ZoneA, "A 區", new[]
        {
            new ZonePartObservation("area-a", new[] { Rect(0, 0, 40, 40), Rect(15, 15, 25, 25) }, PlanUnits.SquareMetersToSquareFeet(1500))
        });
        var finding = Single(Review(Set(zone), Inputs()));

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal(AreaCrossCheck.Agrees, finding.CrossCheck);
        Assert.Equal(1500, finding.GeometricAreaSquareMeters!.Value, 6);
    }

    // --- 缺輸入 ------------------------------------------------------------------------------------

    [Fact]
    public void Missing_sprinkler_input_is_insufficient_data_even_when_the_area_is_over_every_limit()
    {
        foreach (var area in new[] { 1000.0, 5000.0 })
        {
            var result = Single(Review(OneZone(area), Inputs(sprinklered: null))).Result;

            Assert.Equal(ReviewStatus.InsufficientData, result.Status);
            Assert.Equal(area, Number(result.ActualValue), 6);
            Assert.Null(result.RequiredValue);
            Assert.Contains("zone.sprinklered 未設定", result.Message);
            Assert.Equal(ReviewValue.OfText("zone.sprinklered 未設定"), result.Evidence.Find("rule.gaps"));
        }
    }

    [Fact]
    public void Missing_construction_input_leaves_applicability_undecided()
    {
        var finding = Single(Review(OneZone(5000), Inputs(fireResistive: null)));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Contains("building.fireResistiveConstruction 未設定", finding.Result.Message);
        Assert.Contains("是否適用", finding.Result.Message);
    }

    [Fact]
    public void An_unreadable_input_is_insufficient_data_with_the_reason()
    {
        var inputs = new CompartmentAreaInputs(
            new[] { ReviewInput.Known("building.fireResistiveConstruction", true) },
            new Dictionary<Guid, IEnumerable<ReviewInput>>
            {
                [ZoneA] = new[] { ReviewInput.Unreadable("zone.sprinklered", "參數值為「部分」", "防火檢討_自動滅火設備") }
            });
        var result = Single(Review(OneZone(5000), inputs)).Result;

        Assert.Equal(ReviewStatus.InsufficientData, result.Status);
        Assert.Contains("zone.sprinklered 格式無法判讀（參數值為「部分」）", result.Message);
        Assert.Equal(ReviewValue.OfText("防火檢討_自動滅火設備"), result.Evidence.Find("source[zone.sprinklered]"));
    }

    [Fact]
    public void An_Area_without_a_Revit_value_is_insufficient_data_not_a_verdict_on_the_measured_area()
    {
        var finding = Single(Review(OneZone(5000, revitM2: null), Inputs()));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Null(finding.Result.ActualValue);
        Assert.Equal(ReviewValue.Quantity(1500, ReviewUnit.SquareMeter), finding.Result.RequiredValue);
        Assert.Contains("zone.area 未設定", finding.Result.Message);
        Assert.Equal(AreaCrossCheck.NotComparable, finding.CrossCheck);
        Assert.Equal(5000, Number(finding.Result.Evidence.Find("area.geometric")), 6);
        Assert.False(finding.Result.Evidence.Has("area.revit"));
    }

    [Fact]
    public void An_Area_Revit_measures_as_zero_is_unreadable_not_a_pass()
    {
        var finding = Single(Review(OneZone(1000, revitM2: 0), Inputs()));

        Assert.Equal(ReviewStatus.InsufficientData, finding.Status);
        Assert.Contains("zone.area 格式無法判讀", finding.Result.Message);
        Assert.Contains("Revit 回報面積為 0", finding.Result.Message);
    }

    [Fact]
    public void Missing_data_never_becomes_a_fail()
    {
        var cases = new[]
        {
            Inputs(fireResistive: null),
            Inputs(sprinklered: null),
            Inputs(fireResistive: null, sprinklered: null, use: null),
            CompartmentAreaInputs.None
        };

        foreach (var inputs in cases)
        {
            var status = Single(Review(OneZone(99999), inputs)).Status;
            Assert.Equal(ReviewStatus.InsufficientData, status);
        }
    }

    // --- 豁免與不適用 -------------------------------------------------------------------------------

    [Fact]
    public void An_exempt_vertical_compartment_is_not_reported_whatever_its_area()
    {
        var finding = SetAside(Review(OneZone(9000), Inputs(use: "樓梯間")));

        Assert.Equal(ReviewStatus.NotApplicable, finding.Status);
        Assert.Equal(RuleOutcomeReason.Exempt, finding.Outcome!.Reason);
        Assert.Contains("符合豁免條件", finding.Result.Message);
        Assert.Equal(ReviewValue.OfText("樓梯間"), finding.Result.Evidence.Find("zone.use"));
        Assert.Equal("79", finding.Result.RuleId);
    }

    /// <summary>
    /// The engine's own message names the condition that held (<c>zone.use == "樓梯間"</c>), which on
    /// its own reads as if the 區劃 were simply not reviewed. A 垂直區劃 therefore also gets told who
    /// does review it — 第79條之2第1項 (docs/regulations/vertical-compartment.md §3).
    /// </summary>
    [Fact]
    public void An_exempt_vertical_compartment_is_told_which_article_takes_over()
    {
        var finding = SetAside(Review(OneZone(9000), Inputs(use: "樓梯間")));

        Assert.Contains("符合豁免條件", finding.Result.Message);
        Assert.Contains(ZoneUses.VerticalCompartmentHandoff, finding.Result.Message);
    }

    /// <summary>
    /// The note belongs to the 第79條之2 list, not to exemptions in general: a rule set that exempts
    /// something else must not have 第79條之2 put in its mouth.
    /// </summary>
    [Fact]
    public void An_exemption_that_is_not_a_vertical_compartment_gets_no_such_note()
    {
        var warehouse = new Rule("79", "1", RuleCategory.CompartmentArea, "建築技術規則建築設計施工編第79條第1項",
            new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("building.fireResistiveConstruction == true"),
            new RuleExpression("zone.area <= 1500 m2"),
            new[] { new RuleExpression("zone.use == \"倉庫\"") },
            new[] { "zone.use" });

        var finding = Single(Review(OneZone(9000), Inputs(use: "倉庫"), Engine(warehouse)));

        Assert.Equal(ReviewStatus.NotApplicable, finding.Status);
        Assert.Contains("符合豁免條件", finding.Result.Message);
        Assert.DoesNotContain("第79條之2", finding.Result.Message);
    }

    [Fact]
    public void An_undecidable_exemption_only_matters_when_the_limit_is_exceeded()
    {
        var under = Single(Review(OneZone(1500), Inputs(use: null)));
        var over = Single(Review(OneZone(1500.5), Inputs(use: null)));

        Assert.Equal(ReviewStatus.Pass, under.Status);
        Assert.Equal(ReviewStatus.InsufficientData, over.Status);
        Assert.Contains("無法確認是否符合豁免條件", over.Result.Message);
        Assert.Contains("zone.use 未設定", over.Result.Message);
    }

    [Fact]
    public void A_building_the_rule_does_not_cover_is_not_applicable_with_its_evidence()
    {
        var finding = Single(Review(OneZone(9000), Inputs(fireResistive: false)));

        Assert.Equal(ReviewStatus.NotApplicable, finding.Status);
        Assert.Equal(RuleOutcomeReason.NoRuleApplies, finding.Outcome!.Reason);
        Assert.Equal(ReviewValue.OfBoolean(false), finding.Result.Evidence.Find("building.fireResistiveConstruction"));
        Assert.Equal(9000, Number(finding.Result.Evidence.Find("area.revit")), 6);
    }

    [Fact]
    public void Without_any_area_rule_the_zone_is_manual_review_attributed_to_the_rule_set()
    {
        var wall = new Rule("wall", "1", RuleCategory.FireResistance, "第79條", new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("element.isCompartmentBoundary == true"), new RuleExpression("element.providedFireRating >= 60 min"));
        var finding = Single(Review(OneZone(1000), Inputs(), Engine(wall)));

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.RuleMissing, finding.ErrorCode);
        Assert.Equal("tw-bcr-fire", finding.Result.RuleId);
        Assert.Equal("2024.1", finding.Result.RuleVersion);
    }

    // --- Revit Area 與幾何交叉驗證 ------------------------------------------------------------------

    [Theory]
    [InlineData(1200, ReviewStatus.Pass)]
    [InlineData(1800, ReviewStatus.Fail)]
    public void A_Revit_Area_the_boundary_does_not_confirm_withholds_the_verdict(double revit, ReviewStatus wouldBe)
    {
        var finding = Single(Review(OneZone(1500, revitM2: revit), Inputs()));
        var result = finding.Result;

        Assert.Equal(ReviewStatus.ManualReview, result.Status);
        Assert.Equal(wouldBe, finding.Outcome!.Status);
        Assert.Equal(ReviewErrorCode.AreaDisagrees, finding.ErrorCode);
        Assert.Equal(AreaCrossCheck.Differs, finding.CrossCheck);
        Assert.Equal(0.2, finding.RelativeDifference!.Value, 9);
        Assert.Equal(revit, Number(result.ActualValue), 6);
        Assert.Equal(ReviewValue.Quantity(1500, ReviewUnit.SquareMeter), result.RequiredValue);
        Assert.Equal("79", result.RuleId);
        Assert.Contains("相差 20%", result.Message);
        Assert.Contains("需人工覆核", result.Message);
        Assert.Equal(ReviewValue.OfText("Differs"), result.Evidence.Find("area.crossCheck"));
    }

    [Theory]
    [InlineData(1500 * 1.0099, ReviewStatus.Fail)]
    [InlineData(1500 * 0.991, ReviewStatus.Pass)]
    public void Within_the_cross_check_tolerance_Revit_Area_decides(double revit, ReviewStatus expected)
    {
        // The measured boundary is exactly at the limit; Revit's own figure is the actual value.
        var finding = Single(Review(OneZone(1500, revitM2: revit), Inputs()));

        Assert.Equal(expected, finding.Status);
        Assert.Equal(AreaCrossCheck.Agrees, finding.CrossCheck);
        Assert.Equal(revit, Number(finding.Result.ActualValue), 6);
    }

    [Fact]
    public void The_cross_check_tolerance_is_configurable()
    {
        var finding = Single(Review(OneZone(1500, revitM2: 1450), Inputs(), options: new CompartmentAreaOptions(0.05)));

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal(AreaCrossCheck.Agrees, finding.CrossCheck);
        Assert.Equal(0.05, Number(finding.Result.Evidence.Find("area.crossCheckTolerance")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompartmentAreaOptions(-0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompartmentAreaOptions(double.NaN));
    }

    [Fact]
    public void A_disagreement_does_not_change_a_result_that_is_not_a_verdict()
    {
        var exempt = SetAside(Review(OneZone(1500, revitM2: 1000), Inputs(use: "樓梯間")));
        var missing = Single(Review(OneZone(1500, revitM2: 1000), Inputs(sprinklered: null)));

        Assert.Equal(ReviewStatus.NotApplicable, exempt.Status);
        Assert.Equal(ReviewStatus.InsufficientData, missing.Status);
        foreach (var finding in new[] { exempt, missing })
        {
            Assert.Equal(AreaCrossCheck.Differs, finding.CrossCheck);
            Assert.Equal(ReviewErrorCode.AreaDisagrees, finding.ErrorCode);
            Assert.Contains("面積來源無法確認", finding.Result.Message);
        }
    }

    // --- 區劃範圍問題 -------------------------------------------------------------------------------

    [Fact]
    public void Overlapping_zones_are_manual_review_and_no_rule_runs_on_them()
    {
        var set = Set(
            RectZone(ZoneA, "A 區", "area-a", 30, 30, 900),
            RectZone(ZoneB, "B 區", "area-b", 30, 30, 900, x0: 20));
        var review = Review(set, Inputs());

        Assert.Equal(2, review.Findings.Count);
        foreach (var finding in review.Findings)
        {
            Assert.Equal(ReviewStatus.ManualReview, finding.Status);
            Assert.Null(finding.Outcome);
            Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, finding.ErrorCode);
            Assert.Equal("tw-bcr-fire", finding.Result.RuleId);
            // 本列沒有個別條文，所以依據寫的是規則集的 id 與版本，不是規則集那段說明文（B-01）。
            Assert.Equal("依規則集 tw-bcr-fire 2024.1 判定（本列無個別條文）", finding.Result.LegalReference);
            Assert.DoesNotContain("防火區劃", finding.Result.LegalReference);
            Assert.Null(finding.Result.ActualValue);
            Assert.Contains("互相重疊", finding.Result.Message);
            Assert.Equal(ReviewValue.OfText("ZonesOverlap"), finding.Result.Evidence.Find("zone.problems"));
            Assert.Equal(new[] { "area-a", "area-b" }, finding.Result.SubjectUniqueIds.OrderBy(x => x, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void A_zone_with_one_unenclosed_Area_is_manual_review_but_the_others_are_still_reviewed()
    {
        var partly = new ZoneObservation(ZoneA, "A 區", new[]
        {
            new ZonePartObservation("area-a1", new[] { Rect(0, 0, 30, 30) }, PlanUnits.SquareMetersToSquareFeet(900)),
            new ZonePartObservation("area-a2", null, null)
        });
        var review = Review(Set(partly, RectZone(ZoneB, "B 區", "area-b", 30, 30, 900, x0: 50)), Inputs());

        var a = review.For(ZoneA)!;
        Assert.Equal(ReviewStatus.ManualReview, a.Status);
        Assert.Contains("未封閉", a.Result.Message);
        Assert.Contains("area-a2", a.Result.SubjectUniqueIds);
        Assert.Equal(900, Number(a.Result.Evidence.Find("area.part[area-a1].revit")), 6);

        // Zone B has no sprinkler input: its own facts decide it, independent of zone A.
        var b = review.For(ZoneB)!;
        Assert.Equal(ReviewStatus.InsufficientData, b.Status);
    }

    [Fact]
    public void A_package_without_zones_or_with_an_unmeasurable_zone_is_not_reviewed()
    {
        var engine = Engine(AreaRule());
        var none = CompartmentAreaCheck.Review(Set(), Inputs(), engine, Today, RunId);
        var open = CompartmentAreaCheck.Review(
            Set(new ZoneObservation(ZoneC, "C 區", new[] { new ZonePartObservation("area-c", null, null) })),
            Inputs(), engine, Today, RunId);

        Assert.True(none.IsFailure);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, none.Error.Code);
        Assert.True(open.IsFailure);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, open.Error.Code);
        Assert.Contains("「C 區」", open.Error.Message);
        Assert.Contains("建立區劃範圍", open.Error.Message);
    }

    // --- 輸入契約 -----------------------------------------------------------------------------------

    [Fact]
    public void Inputs_cannot_supply_what_the_model_owns_or_fields_of_another_scope()
    {
        Assert.Throws<ArgumentException>(() => new CompartmentAreaInputs(null,
            new Dictionary<Guid, IEnumerable<ReviewInput>> { [ZoneA] = new[] { ReviewInput.Known("zone.area", 10, ReviewUnit.SquareMeter) } }));
        Assert.Throws<ArgumentException>(() => new CompartmentAreaInputs(new[] { ReviewInput.Known("zone.use", "辦公") }, null));
        Assert.Throws<ArgumentException>(() => new CompartmentAreaInputs(null,
            new Dictionary<Guid, IEnumerable<ReviewInput>> { [ZoneA] = new[] { ReviewInput.Known("building.use", "辦公") } }));
        Assert.Throws<ArgumentException>(() => new CompartmentAreaInputs(
            new[] { ReviewInput.Known("building.use", "A"), ReviewInput.Known("building.use", "B") }, null));
        Assert.Throws<ArgumentException>(() => ReviewInput.Unreadable("zone.use", " "));
    }

    [Fact]
    public void An_input_of_the_wrong_type_or_unit_is_rejected_before_any_rule_runs()
    {
        var inputs = new CompartmentAreaInputs(new[] { ReviewInput.Known("building.fireResistiveConstruction", "是") }, null);

        Assert.Throws<ArgumentException>(() => Review(OneZone(1000), inputs));
    }

    [Fact]
    public void Inputs_for_a_zone_outside_the_package_are_reported_not_applied()
    {
        var stray = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        var review = Review(OneZone(1000), Inputs(zone: stray));

        Assert.Contains(review.Warnings, w => w.Contains(stray.ToString("D")));
        Assert.Equal(ReviewStatus.InsufficientData, Single(review).Status);
    }

    // --- 可重現與可保存 -----------------------------------------------------------------------------

    [Fact]
    public void Results_are_ordered_by_zone_and_fit_a_review_run_that_round_trips()
    {
        var set = Set(
            RectZone(ZoneB, "B 區", "area-b", 60, 30, 1800, x0: 100),
            RectZone(ZoneA, "A 區", "area-a", 40, 30, 1200));
        var inputs = new CompartmentAreaInputs(
            new[] { ReviewInput.Known("building.fireResistiveConstruction", true) },
            new Dictionary<Guid, IEnumerable<ReviewInput>>
            {
                [ZoneA] = new[] { ReviewInput.Known("zone.sprinklered", false), ReviewInput.Known("zone.use", "辦公") },
                [ZoneB] = new[] { ReviewInput.Known("zone.sprinklered", false), ReviewInput.Known("zone.use", "辦公") }
            });
        var review = Review(set, inputs);

        Assert.Equal(new[] { ZoneA, ZoneB }, review.Findings.Select(x => x.ZoneId));
        Assert.Equal(new[] { ReviewStatus.Pass, ReviewStatus.Fail }, review.Findings.Select(x => x.Status));
        Assert.Equal(1, review.Count(ReviewStatus.Fail));

        var started = new DateTime(2025, 6, 1, 1, 0, 0, DateTimeKind.Utc);
        var run = new ReviewRun(RunId, PackageId, "tw-bcr-fire", "2024.1", 3, started)
            .Complete(review.Results, started.AddMinutes(1));
        var back = ReviewRunStorageMapper.FromRecord(ReviewRunStorageMapper.ToRecord(run));

        Assert.Equal(run.Results.Select(x => (x.ZoneId, x.Status, x.Message)), back.Results.Select(x => (x.ZoneId, x.Status, x.Message)));
        Assert.Equal(run.Results[1].Evidence.Items.Select(x => x.Field), back.Results[1].Evidence.Items.Select(x => x.Field));
        Assert.Equal(run.Results[1].ActualValue, back.Results[1].ActualValue);

        var again = Review(set, inputs);
        Assert.Equal(review.Results.Select(x => (x.ResultId, x.Status, x.Message)), again.Results.Select(x => (x.ResultId, x.Status, x.Message)));
    }

    [Fact]
    public void The_valid_fixture_reviews_a_zone_end_to_end()
    {
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "Fixtures", "valid-ruleset.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), json));
        Assert.True(loaded.IsSuccess, loaded.Error.TechnicalDetail);

        var finding = Single(Review(OneZone(3000), Inputs(sprinklered: true), new RuleEngine(loaded.Value)));

        Assert.Equal(ReviewStatus.Pass, finding.Status);
        Assert.Equal("tw-bcr-79-area", finding.Result.RuleId);
        Assert.Equal("建築技術規則建築設計施工編第79條第1項", finding.Result.LegalReference);
    }
}
