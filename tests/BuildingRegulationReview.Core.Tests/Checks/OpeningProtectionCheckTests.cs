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

public sealed class OpeningProtectionCheckTests
{
    private static readonly RuleEvaluationContext Today = new(new DateTime(2025, 6, 1), "TW");
    private static readonly Guid RunId = Guid.Parse("99999999-0000-0000-0000-000000000003");

    private const string DoorType = "type-fd";
    private const string WindowType = "type-fw";
    private const string Parameter = FireProtectionParameters.Provided;

    private static Rule ProtectionRule(string ruleId, string appliesWhen, int priority = 10) =>
        new(ruleId, "1", RuleCategory.OpeningProtection, $"測試條文 {ruleId}", new DateTime(2024, 1, 1), "TW", priority,
            new RuleExpression(appliesWhen), new RuleExpression("opening.providedFireProtection == \"是\""),
            evidenceFields: new[] { "opening.hostUniqueId", "opening.providedFireProtection" });

    /// <summary>Doors in a boundary wall must be protected; windows only when larger than 3 m².</summary>
    private static readonly Rule[] Rules =
    {
        ProtectionRule("door", "opening.kind == \"Door\" && opening.hostIsCompartmentBoundary == true"),
        ProtectionRule("window", "opening.kind == \"Window\" && opening.hostIsCompartmentBoundary == true && opening.area > 3 m2")
    };

    private static RuleEngine Engine(params Rule[] rules)
    {
        var compiled = RuleSetCompiler.Compile(new RuleSet("tw-bcr-fire", "2024.1", "防火門窗", rules.Length == 0 ? Rules : rules));
        Assert.True(compiled.IsSuccess, compiled.Error.TechnicalDetail);
        return new RuleEngine(compiled.Value);
    }

    // --- 固定幾何：A 區 x 0–10、B 區 x 10–20 ------------------------------------------------------

    private static readonly MemberObservation[] Walls =
    {
        Wall("W-left", 0, 0, 0, 10),
        Wall("W-shared", 10, 0, 10, 10),
        Wall("W-partition", 5, 0, 5, 10),
        Wall("W-bottom", 0, 0, 20, 0),
        Wall("CW-right", 20, 0, 20, 10, width: 0.1, curtain: true)
    };

    private static OpeningObservation Opening(
        string uid, string? host, double x, double y,
        CandidateCategory category = CandidateCategory.Door, double width = 1.0, double height = 2.1,
        string? type = null, string? link = null, CurtainPanelKind? panelKind = null) =>
        new(Source(uid, link), category, host, P(x, y), M(width), M(height),
            type ?? (category == CandidateCategory.Window ? WindowType : DoorType),
            category == CandidateCategory.Window ? "FW1" : "FD1",
            panelKind);

    private static OpeningObservation SharedDoor() => Opening("D-shared", "W-shared", 10, 3);
    private static OpeningObservation LeftDoor() => Opening("D-left", "W-left", 0, 5);
    private static OpeningObservation SmallWindow() => Opening("WN-small", "W-bottom", 5, 0, CandidateCategory.Window);
    private static OpeningObservation LargeWindow() => Opening("WN-large", "W-bottom", 15, 0, CandidateCategory.Window, 2.0, 2.0);

    private static IReadOnlyList<ZoneObservation> ZonesAB() => Zones().Take(2).ToList();

    private static CandidateSet Set(params OpeningObservation[] openings) => Set(ZonesAB(), null, openings);

    private static CandidateSet Set(IEnumerable<ZoneObservation> zones, CandidateResolutionOptions? options, params OpeningObservation[] openings) =>
        CandidateResolver.Resolve(Observations(zones, Walls, openings), options);

    private static OpeningFireProtection Instance(string uid, string text) =>
        OpeningFireProtection.ForInstance(uid, FireProtectionText.Parse(text), Parameter);

    private static OpeningProtectionInputs Inputs(params (string Uid, string Text)[] instances) =>
        Inputs(null, instances.Select(x => Instance(x.Uid, x.Text)).ToArray());

    private static OpeningProtectionInputs Inputs(CompartmentAreaInputs? context, params OpeningFireProtection[] values) => new(context, values);

    private static OpeningProtectionReview Review(CandidateSet set, OpeningProtectionInputs inputs, RuleEngine? engine = null)
    {
        var ids = Enumerable.Range(1, 200).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).GetEnumerator();
        var review = OpeningProtectionCheck.Review(set, inputs, engine ?? Engine(), Today, RunId, () => { ids.MoveNext(); return ids.Current; });
        Assert.True(review.IsSuccess, review.Error.ToString());
        return review.Value;
    }

    private static OpeningProtectionFinding Only(OpeningProtectionReview review) => Assert.Single(review.Findings);

    // --- 是／否／未設定 ------------------------------------------------------------------------------

    [Theory]
    [InlineData("是", ReviewStatus.Pass)]
    [InlineData("有", ReviewStatus.Pass)]
    [InlineData("Yes", ReviewStatus.Pass)]
    [InlineData("１", ReviewStatus.Pass)]
    [InlineData("否", ReviewStatus.Fail)]
    [InlineData("無", ReviewStatus.Fail)]
    [InlineData("false", ReviewStatus.Fail)]
    public void A_boundary_door_passes_when_protected_and_fails_when_not(string provided, ReviewStatus expected)
    {
        var finding = Only(Review(Set(LeftDoor()), Inputs(("D-left", provided))));
        var result = finding.Result;

        Assert.Equal(expected, result.Status);
        Assert.Equal(ReviewValue.OfText(expected == ReviewStatus.Pass ? "是" : "否"), result.ActualValue);
        Assert.Equal(ReviewValue.OfText("是"), result.RequiredValue);
        Assert.True(finding.RequiresProtection);
        Assert.Null(finding.ErrorCode);

        Assert.Equal(ReviewCheckTypes.OpeningProtection, result.CheckType);
        Assert.Equal(new[] { "D-left" }, result.SubjectUniqueIds);
        Assert.Equal(ZoneA.ToString("D"), result.ZoneId);
        Assert.Equal(PackageId, result.PackageId);
        Assert.Equal(RunId, result.RunId);
        Assert.Equal("door", result.RuleId);
        Assert.Equal(OpeningGroup.Door, finding.Group);
        Assert.Equal("W-left", finding.HostUniqueId);
        Assert.StartsWith($"門「FD1」（{Source("D-left")}）於區劃「A 區」：", result.Message);

        var evidence = result.Evidence;
        Assert.Equal(ReviewValue.OfText("Boundary"), evidence.Find("candidate.relation"));
        Assert.Equal(ReviewValue.OfText("W-left"), evidence.Find("opening.hostUniqueId"));
        Assert.Equal(ReviewValue.OfText(expected == ReviewStatus.Pass ? "Yes" : "No"), evidence.Find("provided.kind"));
        Assert.Equal(ReviewValue.OfText("Instance"), evidence.Find("provided.scope"));
        Assert.Equal(ReviewValue.OfText(Parameter), evidence.Find("provided.parameter"));
        Assert.Equal(ReviewValue.OfText(provided), evidence.Find("provided.raw"));
        Assert.Equal(ReviewValue.OfText(DoorType), evidence.Find("source.typeUniqueId"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void An_unset_protection_is_insufficient_data_that_still_states_the_requirement(string? provided)
    {
        var inputs = provided is null ? Inputs() : Inputs(("D-left", provided));
        var finding = Only(Review(Set(LeftDoor()), inputs));
        var result = finding.Result;

        Assert.Equal(ReviewStatus.InsufficientData, result.Status);
        Assert.Null(result.ActualValue);
        Assert.Equal(ReviewValue.OfText("是"), result.RequiredValue);
        Assert.Equal(ReviewErrorCode.ParameterMissing, finding.ErrorCode);
        Assert.Equal(ProvidedFireProtectionKind.Missing, finding.Provided!.Kind);
        Assert.True(finding.RequiresProtection);
        Assert.Contains("opening.providedFireProtection 未設定", result.Message);
        Assert.Equal(ReviewValue.OfText("opening.providedFireProtection 未設定"), result.Evidence.Find("rule.gaps"));
        Assert.Equal(ReviewValue.OfText(provided is null ? "開口與其 Type 都未提供防火保護" : "參數未填寫"),
            result.Evidence.Find("provided.reason"));
    }

    [Theory]
    [InlineData("甲種")]
    [InlineData("F60")]
    [InlineData("是/否")]
    public void An_unreadable_protection_is_insufficient_data_with_the_raw_value(string provided)
    {
        var finding = Only(Review(Set(LeftDoor()), Inputs(("D-left", provided))));
        var result = finding.Result;

        Assert.Equal(ReviewStatus.InsufficientData, result.Status);
        Assert.Equal(ReviewErrorCode.ParameterTypeMismatch, finding.ErrorCode);
        Assert.Equal(ProvidedFireProtectionKind.Unreadable, finding.Provided!.Kind);
        Assert.Contains("格式無法判讀", result.Message);
        Assert.Contains($"「{provided}」", result.Message);
        Assert.Equal(ReviewValue.OfText(provided), result.Evidence.Find("provided.raw"));
        Assert.Equal(ReviewValue.OfText("是"), result.RequiredValue);
    }

    // --- 先判斷是否需要防火保護 --------------------------------------------------------------------

    [Fact]
    public void Lying_on_a_boundary_does_not_by_itself_require_protection()
    {
        var review = Review(Set(SmallWindow(), LargeWindow()), Inputs(("WN-small", "否"), ("WN-large", "否")));

        var small = review.For("WN-small", ZoneA)!;
        Assert.Equal(ReviewStatus.NotApplicable, small.Status);
        Assert.False(small.RequiresProtection);
        Assert.Null(small.Result.RequiredValue);
        Assert.Equal(ReviewValue.OfText("Boundary"), small.Result.Evidence.Find("candidate.relation"));
        Assert.Equal(ReviewValue.OfText("Window"), small.Result.Evidence.Find("opening.kind"));
        Assert.Equal(ReviewValue.OfBoolean(true), small.Result.Evidence.Find("opening.hostIsCompartmentBoundary"));
        Assert.Equal(2.1, small.Result.Evidence.Find("opening.area")!.Number, 6);
        Assert.Equal(ReviewValue.OfText("No"), small.Result.Evidence.Find("provided.kind"));

        var large = review.For("WN-large", ZoneB)!;
        Assert.Equal(ReviewStatus.Fail, large.Status);
        Assert.True(large.RequiresProtection);
        Assert.Equal("window", large.Result.RuleId);
    }

    [Fact]
    public void An_interior_opening_is_not_a_candidate_by_default_and_not_applicable_with_its_evidence_when_included()
    {
        var partitionDoor = Opening("D-partition", "W-partition", 5, 4);
        var inputs = Inputs(("D-partition", "否"));

        Assert.Empty(Review(Set(partitionDoor), inputs).Findings);

        var finding = Only(Review(Set(ZonesAB(), new CandidateResolutionOptions(includeInteriorOpenings: true), partitionDoor), inputs));
        Assert.Equal(ReviewStatus.NotApplicable, finding.Status);
        Assert.False(finding.RequiresProtection);
        Assert.Equal(ReviewValue.OfText("Inside"), finding.Result.Evidence.Find("candidate.relation"));
        Assert.Equal(ReviewValue.OfBoolean(false), finding.Result.Evidence.Find("opening.hostIsCompartmentBoundary"));
        Assert.Equal(ReviewValue.OfText("Door"), finding.Result.Evidence.Find("opening.kind"));
    }

    [Theory]
    [InlineData(true, "否", ReviewStatus.Fail)]
    [InlineData(false, "否", ReviewStatus.NotApplicable)]
    [InlineData(null, "否", ReviewStatus.InsufficientData)]
    [InlineData(null, "是", ReviewStatus.InsufficientData)]
    public void Whether_protection_is_required_can_depend_on_building_inputs(bool? fireResistive, string provided, ReviewStatus expected)
    {
        var engine = Engine(ProtectionRule("fr-door", "building.fireResistiveConstruction == true && opening.hostIsCompartmentBoundary == true"));
        var context = new CompartmentAreaInputs(
            fireResistive is bool f ? new[] { ReviewInput.Known("building.fireResistiveConstruction", f, "專案設定") } : null, null);

        var finding = Only(Review(Set(LeftDoor()), Inputs(context, Instance("D-left", provided)), engine));

        Assert.Equal(expected, finding.Status);
        Assert.Equal(expected switch
        {
            ReviewStatus.Fail => true,
            ReviewStatus.NotApplicable => false,
            _ => (bool?)null
        }, finding.RequiresProtection);
        if (fireResistive.HasValue)
            Assert.Equal(ReviewValue.OfText("專案設定"), finding.Result.Evidence.Find("source[building.fireResistiveConstruction]"));
        else
            Assert.Contains("building.fireResistiveConstruction", finding.Result.Evidence.Find("rule.gaps")!.Text);
    }

    [Fact]
    public void A_door_in_a_shared_boundary_wall_is_reviewed_once_per_zone_and_counted_once()
    {
        var review = Review(Set(SharedDoor()), Inputs(("D-shared", "是")));

        Assert.Equal(new[] { ZoneA, ZoneB }, review.Findings.Select(f => f.ZoneId!.Value));
        Assert.All(review.Findings, f => Assert.Equal(ReviewStatus.Pass, f.Status));
        Assert.Equal(1, review.Group(OpeningGroup.Door).OpeningCount);
        Assert.Equal(2, review.Group(OpeningGroup.Door).Count(ReviewStatus.Pass));
    }

    // --- Instance／Type 來源 ------------------------------------------------------------------------

    [Fact]
    public void An_instance_value_overrides_its_Type_and_the_Type_fills_in_otherwise()
    {
        var other = Opening("D-other", "W-left", 0, 8);
        var inputs = Inputs(null,
            OpeningFireProtection.ForType(DoorType, ProvidedFireProtection.FromInteger(1), "防火門"),
            OpeningFireProtection.ForInstance("D-left", ProvidedFireProtection.FromBoolean(false), Parameter));
        var before = inputs.Protections.ToList();

        var review = Review(Set(LeftDoor(), other), inputs);

        var left = review.For("D-left", ZoneA)!;
        Assert.Equal(ReviewStatus.Fail, left.Status);
        Assert.Equal(FireProtectionScope.Instance, left.Source!.Scope);
        Assert.Equal(ReviewValue.OfText("Instance"), left.Result.Evidence.Find("provided.scope"));

        var typed = review.For("D-other", ZoneA)!;
        Assert.Equal(ReviewStatus.Pass, typed.Status);
        Assert.Equal(FireProtectionScope.Type, typed.Source!.Scope);
        Assert.Equal(ReviewValue.OfText("Type"), typed.Result.Evidence.Find("provided.scope"));
        Assert.Equal(ReviewValue.OfText("防火門"), typed.Result.Evidence.Find("provided.parameter"));
        Assert.Equal(ReviewValue.OfText("1"), typed.Result.Evidence.Find("provided.raw"));

        Assert.Equal(before, inputs.Protections);
        Assert.Same(before.Single(p => p.Scope == FireProtectionScope.Type).Protection, typed.Provided);
    }

    [Fact]
    public void Inputs_reject_two_values_for_one_subject_and_report_what_they_could_not_use()
    {
        Assert.Throws<ArgumentException>(() => Inputs(("D-left", "是"), ("D-left", "否")));
        Assert.Throws<ArgumentException>(() => new CompartmentAreaInputs(new[] { ReviewInput.Known("opening.kind", "Door") }, null));

        var context = new CompartmentAreaInputs(null, new Dictionary<Guid, IEnumerable<ReviewInput>>
        {
            [ZoneC] = new[] { ReviewInput.Known("zone.use", "辦公") }
        });
        var review = Review(Set(LeftDoor()), Inputs(context,
            Instance("D-left", "是"),
            OpeningFireProtection.ForType("D-left", ProvidedFireProtection.Yes(), Parameter),
            Instance("D-ghost", "是"),
            OpeningFireProtection.ForType("type-ghost", ProvidedFireProtection.No(), Parameter)));

        Assert.Equal(4, review.Warnings.Count);
        Assert.Contains(review.Warnings, w => w.Contains(ZoneC.ToString("D")));
        Assert.Contains(review.Warnings, w => w.Contains("開口 D-ghost"));
        Assert.Contains(review.Warnings, w => w.Contains("Type type-ghost"));
        Assert.Contains(review.Warnings, w => w.Contains("Type D-left"));
    }

    [Theory]
    [InlineData("是", ProvidedFireProtectionKind.Yes)]
    [InlineData(" ＹＥＳ ", ProvidedFireProtectionKind.Yes)]
    [InlineData("TRUE", ProvidedFireProtectionKind.Yes)]
    [InlineData("y", ProvidedFireProtectionKind.Yes)]
    [InlineData("否", ProvidedFireProtectionKind.No)]
    [InlineData("沒有", ProvidedFireProtectionKind.No)]
    [InlineData("0", ProvidedFireProtectionKind.No)]
    [InlineData("N", ProvidedFireProtectionKind.No)]
    [InlineData(null, ProvidedFireProtectionKind.Missing)]
    [InlineData("", ProvidedFireProtectionKind.Missing)]
    [InlineData("甲種防火門", ProvidedFireProtectionKind.Unreadable)]
    [InlineData("2", ProvidedFireProtectionKind.Unreadable)]
    [InlineData("是否", ProvidedFireProtectionKind.Unreadable)]
    public void Protection_text_is_read_only_as_an_unmistakable_yes_or_no(string? raw, ProvidedFireProtectionKind expected)
    {
        var value = FireProtectionText.Parse(raw);

        Assert.Equal(expected, value.Kind);
        Assert.Equal(expected switch
        {
            ProvidedFireProtectionKind.Yes => "是",
            ProvidedFireProtectionKind.No => "否",
            _ => null
        }, value.RuleText);
        if (expected == ProvidedFireProtectionKind.Unreadable) Assert.Equal("不是「是／否」的值", value.Reason);
        if (raw is not null && !string.IsNullOrWhiteSpace(raw)) Assert.Equal(raw.Trim(), value.RawText);
    }

    [Fact]
    public void Integer_and_boolean_parameters_map_to_yes_and_no()
    {
        Assert.Equal(ProvidedFireProtectionKind.Yes, ProvidedFireProtection.FromInteger(1).Kind);
        Assert.Equal(ProvidedFireProtectionKind.No, ProvidedFireProtection.FromInteger(0).Kind);
        Assert.Equal(ProvidedFireProtectionKind.Unreadable, ProvidedFireProtection.FromInteger(-1).Kind);
        Assert.Equal(ProvidedFireProtectionKind.Yes, ProvidedFireProtection.FromBoolean(true).Kind);
        Assert.Equal(ProvidedFireProtectionKind.No, ProvidedFireProtection.FromBoolean(false).Kind);
    }

    // --- 幕牆、非 Hosted、Host 未解析、link（MVP 政策） ------------------------------------------

    [Fact]
    public void Ambiguous_openings_follow_the_MVP_policy_as_manual_review()
    {
        var set = CandidateResolver.Resolve(Observations(ZonesAB(), Members(), Openings()));
        var review = Review(set, Inputs(("D1-shared", "是"), ("D4-on-boundary", "否"), ("WN1-bottom", "是"), ("P1-panel", "否")));

        Assert.Equal(
            new[]
            {
                ("D8-nothing", (Guid?)null), ("D1-shared", ZoneA), ("D4-on-boundary", ZoneA), ("D6-ghost-host", ZoneA),
                ("D7-no-location", ZoneA), ("N1-unhosted", ZoneA), ("D1-shared", ZoneB), ("D5-ambiguous-host", ZoneB),
                ("D7-no-location", ZoneB), ("WN1-bottom", ZoneB), ("P1-panel", ZoneB)
            },
            review.Findings.Select(f => (f.ElementUniqueId, f.ZoneId)));

        Assert.Equal(ReviewStatus.Pass, review.For("D1-shared", ZoneA)!.Status);
        Assert.Equal(ReviewStatus.Fail, review.For("D4-on-boundary", ZoneA)!.Status);
        Assert.Equal(ReviewStatus.NotApplicable, review.For("WN1-bottom", ZoneB)!.Status); // 2.1 m², below the window threshold

        var expected = new Dictionary<string, CandidateAmbiguityKind>
        {
            ["D8-nothing"] = CandidateAmbiguityKind.OpeningLocationUnknown,
            ["D6-ghost-host"] = CandidateAmbiguityKind.HostNotResolved,
            ["D7-no-location"] = CandidateAmbiguityKind.OpeningLocationUnknown,
            ["N1-unhosted"] = CandidateAmbiguityKind.NonHostedOpening,
            ["D5-ambiguous-host"] = CandidateAmbiguityKind.HostRelationAmbiguous
        };
        foreach (var finding in review.Findings.Where(f => f.Ambiguity is not null))
        {
            Assert.Equal(expected[finding.ElementUniqueId], finding.Ambiguity!.Kind);
            Assert.Equal(ReviewStatus.ManualReview, finding.Status);
            Assert.Equal(ReviewErrorCode.CandidateAmbiguous, finding.ErrorCode);
            Assert.Null(finding.Outcome);
            Assert.Null(finding.Provided);
            Assert.Null(finding.RequiresProtection);
            Assert.Equal("tw-bcr-fire", finding.Result.RuleId);
            Assert.Equal("2024.1", finding.Result.RuleVersion);
            Assert.Equal(ReviewCheckTypes.OpeningProtection, finding.Result.CheckType);
        }
        Assert.Equal(6, review.Findings.Count(f => f.Ambiguity is not null));

        // The panel is 不適用 even though its value says 否: it sits in the outer facade, which 第79條
        // does not divide with 防火門窗 — the facade is the curtain-wall junction review's.
        var panel = review.For("P1-panel", ZoneB)!;
        Assert.Equal(ReviewStatus.NotApplicable, panel.Status);
        Assert.Equal(CandidateCategory.CurtainPanel, panel.Category);
        Assert.Equal(OpeningGroup.CurtainWall, panel.Group);
        Assert.Equal(CandidateCategory.Door, review.For("D8-nothing").Single().Category);

        // Member ambiguities (offset wall, flush column, wall without geometry) belong to the rating check.
        Assert.DoesNotContain(review.Findings, f => f.ElementUniqueId == "C3-flush" || f.ElementUniqueId == "W9-nogeometry");
    }

    [Fact]
    public void A_door_in_the_outer_curtain_wall_is_not_applicable_and_a_linked_door_is_manual_review()
    {
        var curtainDoor = Opening("D-curtain", "CW-right", 20, 5);
        var linked = Opening("D-linked", "W-left", 0, 5, link: "link-instance");
        var review = Review(Set(curtainDoor, linked), Inputs(("D-curtain", "否"), ("D-linked", "是")));

        // Nothing lies beyond CW-right: it is the building's facade, not a 區劃分隔.
        var curtain = review.For("D-curtain", ZoneB)!;
        Assert.Equal(ReviewStatus.NotApplicable, curtain.Status);
        Assert.Null(curtain.Ambiguity);
        // An inference, so it is flagged for spot-checking: a log code and a tag the table search finds.
        Assert.Equal(ReviewErrorCode.CandidateFacadeInferred, curtain.ErrorCode);
        Assert.True(ReviewErrorCode.IsKnown(curtain.ErrorCode));
        Assert.StartsWith(OpeningProtectionCheck.FacadeInferredTag, curtain.Result.Message);
        Assert.False(curtain.RequiresProtection);
        Assert.Equal(CandidateCategory.Door, curtain.Category);
        Assert.Equal(OpeningGroup.CurtainWall, curtain.Group);
        Assert.Contains("第79條之4", curtain.Result.LegalReference);
        Assert.Contains("第110條", curtain.Result.Message);
        Assert.Equal(ReviewValue.OfText("Facade"), curtain.Result.Evidence.Find("candidate.relation"));

        // The linked door's host is the linked wall, which the review did not read.
        var link = Assert.Single(review.For("D-linked"));
        Assert.Equal(ReviewStatus.ManualReview, link.Status);
        Assert.Equal(CandidateAmbiguityKind.LinkedElement, link.Ambiguity!.Kind);
        Assert.Equal(OpeningGroup.Door, link.Group);
    }

    // --- 室內帷幕牆上的區劃邊緣開口（帷幕牆規格 §4.8）--------------------------------------------
    //
    // A curtain wall on the A｜B line is itself a 區劃分隔, not the building's 外牆: 第79條第1項 does
    // govern its openings. Which of them it can answer depends on 防火檢討_嵌板種類 — 門窗 and 玻璃嵌板
    // are 防火設備 and answer with 設計防火保護; a 實心嵌板 is construction and no opening rule reaches
    // it; an undeclared panel is 資料不足. The 外牆 curtain wall CW-right is untouched by all of this.

    private static MemberObservation[] WallsWithInteriorCurtainWall() =>
        Walls.Where(w => w.Source.ElementUniqueId != "W-shared")
            .Append(Wall("CW-shared", 10, 0, 10, 10, width: 0.1, curtain: true))
            .ToArray();

    private static OpeningProtectionReview ReviewOnInteriorCurtainWall(OpeningObservation opening, string protection)
    {
        var set = CandidateResolver.Resolve(Observations(ZonesAB(), WallsWithInteriorCurtainWall(), new[] { opening }));
        return Review(set, Inputs((opening.Source.ElementUniqueId, protection)));
    }

    [Fact]
    public void A_door_in_an_interior_curtain_wall_is_judged_as_a_boundary_opening()
    {
        var review = ReviewOnInteriorCurtainWall(Opening("D-cw", "CW-shared", 10, 3), "是");

        foreach (var zone in new[] { ZoneA, ZoneB })
        {
            var finding = review.For("D-cw", zone)!;
            Assert.Null(finding.Ambiguity);
            Assert.Equal(ReviewStatus.Pass, finding.Status);
            Assert.Equal(ReviewValue.OfText("Boundary"), finding.Result.Evidence.Find("candidate.relation"));
        }
    }

    [Fact]
    public void An_unprotected_door_in_an_interior_curtain_wall_fails_rather_than_asking_a_person()
    {
        var finding = ReviewOnInteriorCurtainWall(Opening("D-cw", "CW-shared", 10, 3), "否").For("D-cw", ZoneA)!;

        Assert.Null(finding.Ambiguity);
        Assert.Equal(ReviewStatus.Fail, finding.Status);
    }

    [Fact]
    public void A_glazed_panel_in_an_interior_curtain_wall_answers_with_its_fire_protection()
    {
        var panel = Opening("P-glazed", "CW-shared", 10, 7, CandidateCategory.CurtainPanel, panelKind: CurtainPanelKind.Glazed);
        var finding = ReviewOnInteriorCurtainWall(panel, "是").For("P-glazed", ZoneA)!;

        Assert.Null(finding.Ambiguity);
        Assert.Equal(ReviewValue.OfText("Boundary"), finding.Result.Evidence.Find("candidate.relation"));
    }

    [Fact]
    public void A_solid_panel_in_an_interior_curtain_wall_is_construction_and_no_opening_rule_answers_it()
    {
        var panel = Opening("P-solid", "CW-shared", 10, 7, CandidateCategory.CurtainPanel, panelKind: CurtainPanelKind.Solid);
        var finding = ReviewOnInteriorCurtainWall(panel, "是").For("P-solid", ZoneA)!;

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(CandidateAmbiguityKind.CurtainPanelIsConstruction, finding.Ambiguity!.Kind);
    }

    [Fact]
    public void A_panel_of_undeclared_kind_in_an_interior_curtain_wall_is_insufficient_data_not_a_guess()
    {
        var panel = Opening("P-unknown", "CW-shared", 10, 7, CandidateCategory.CurtainPanel);
        var finding = ReviewOnInteriorCurtainWall(panel, "是").For("P-unknown", ZoneA)!;

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(CandidateAmbiguityKind.CurtainPanelKindUndeclared, finding.Ambiguity!.Kind);
        Assert.Contains(CurtainPanelKindParameters.Provided, finding.Ambiguity.Message);
    }

    [Fact]
    public void Openings_of_a_zone_whose_extent_is_in_doubt_are_manual_review_without_a_rule()
    {
        var overlapping = new[]
        {
            Zone(ZoneA, "A 區", "area-a", Rect(0, 0, 10, 10)),
            Zone(ZoneB, "B 區", "area-b", Rect(8, 0, 18, 10))
        };
        var finding = Only(Review(Set(overlapping, null, LeftDoor()), Inputs(("D-left", "否"))));

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
        var none = OpeningProtectionCheck.Review(Set(Array.Empty<ZoneObservation>(), null), Inputs(), Engine(), Today, RunId);
        var open = OpeningProtectionCheck.Review(Set(Zones(), null, LeftDoor()), Inputs(), Engine(), Today, RunId);

        Assert.True(none.IsFailure);
        Assert.Equal(ReviewErrorCode.CandidateZoneUnusable, none.Error.Code);
        Assert.Contains("無法檢討防火門窗", none.Error.Message);
        Assert.True(open.IsFailure);
        Assert.Contains("「C 區」", open.Error.Message);
    }

    [Fact]
    public void Without_an_opening_rule_every_opening_is_manual_review()
    {
        var wallOnly = new Rule("wall", "1", RuleCategory.FireResistance, "第79條", new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("element.isCompartmentBoundary == true"), new RuleExpression("element.providedFireRating >= 60 min"));
        var finding = Only(Review(Set(LeftDoor()), Inputs(("D-left", "否")), Engine(wallOnly)));

        Assert.Equal(ReviewStatus.ManualReview, finding.Status);
        Assert.Equal(ReviewErrorCode.RuleMissing, finding.ErrorCode);
        Assert.Null(finding.RequiresProtection);
    }

    [Fact]
    public void Missing_or_doubtful_data_never_becomes_a_fail_or_a_pass()
    {
        var openings = new[] { LeftDoor(), SharedDoor(), LargeWindow() };
        foreach (var inputs in new[]
                 {
                     Inputs(),
                     Inputs(("D-left", ""), ("D-shared", "甲種"), ("WN-large", "F60"))
                 })
        {
            var review = Review(Set(openings), inputs);

            Assert.Equal(4, review.Findings.Count);
            Assert.All(review.Findings, f => Assert.Equal(ReviewStatus.InsufficientData, f.Status));
        }
    }

    // --- 依門／窗／幕牆統計、可重現、可保存 --------------------------------------------------------

    [Fact]
    public void Results_are_counted_by_door_window_and_curtain_wall()
    {
        var review = Review(Set(LeftDoor(), SharedDoor(), SmallWindow(), LargeWindow()),
            Inputs(("D-left", "是"), ("D-shared", "是"), ("WN-small", "否"), ("WN-large", "是")));

        Assert.Equal(OpeningGroups.All, review.Groups.Select(g => g.Group));
        Assert.Equal(new[] { "門", "窗", "幕牆" }, review.Groups.Select(g => OpeningGroups.Label(g.Group)));

        var doors = review.Group(OpeningGroup.Door);
        Assert.Equal(new[] { "D-left", "D-shared" }, doors.ElementUniqueIds);
        Assert.Equal(3, doors.Findings.Count);
        Assert.Equal(ReviewStatus.Pass, doors.Status);

        var windows = review.Group(OpeningGroup.Window);
        Assert.Equal(2, windows.OpeningCount);
        Assert.Equal(1, windows.Count(ReviewStatus.Pass));
        Assert.Equal(1, windows.Count(ReviewStatus.NotApplicable));
        Assert.Equal(ReviewStatus.Pass, windows.Status);

        var curtain = review.Group(OpeningGroup.CurtainWall);
        Assert.Equal(0, curtain.OpeningCount);
        Assert.Equal(ReviewStatus.NotRun, curtain.Status);
    }

    [Theory]
    [InlineData("否", ReviewStatus.Fail)]
    [InlineData("是", ReviewStatus.ManualReview)]
    [InlineData("", ReviewStatus.ManualReview)]
    public void A_group_passes_only_when_every_opening_does(string provided, ReviewStatus expected)
    {
        var review = Review(Set(LeftDoor(), Opening("N-unhosted", null, 10.1, 7)), Inputs(("D-left", provided)));

        var doors = review.Group(OpeningGroup.Door);
        Assert.Equal(expected, doors.Status);
        Assert.Equal(2, doors.Count(ReviewStatus.ManualReview)); // near the shared boundary: ambiguous in both zones
    }

    [Fact]
    public void Results_are_ordered_reproducible_and_fit_a_review_run_that_round_trips()
    {
        CandidateSet Build(bool reversed)
        {
            var openings = new[] { LargeWindow(), SharedDoor(), LeftDoor(), SmallWindow(), Opening("P-panel", "CW-right", 20, 5, CandidateCategory.CurtainPanel) };
            return Set(reversed ? openings.Reverse().ToArray() : openings);
        }

        var inputs = Inputs(("D-left", "是"), ("D-shared", "否"), ("WN-large", ""));
        var review = Review(Build(false), inputs);
        var again = Review(Build(true), inputs);

        Assert.Equal(
            new[] { "D-left", "D-shared", "WN-small", "D-shared", "WN-large", "P-panel" },
            review.Findings.Select(f => f.ElementUniqueId));
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
    public void The_valid_fixture_reviews_a_boundary_door_end_to_end()
    {
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "Fixtures", "valid-ruleset.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), json));
        Assert.True(loaded.IsSuccess, loaded.Error.TechnicalDetail);

        var review = Review(Set(LeftDoor(), SmallWindow()), Inputs(("D-left", "是"), ("WN-small", "否")), new RuleEngine(loaded.Value));

        var door = review.For("D-left", ZoneA)!;
        Assert.Equal(ReviewStatus.Pass, door.Status);
        Assert.Equal("tw-bcr-76-door", door.Result.RuleId);
        Assert.Equal("建築技術規則建築設計施工編第76條", door.Result.LegalReference);
        Assert.Equal(ReviewValue.OfText("W-left"), door.Result.Evidence.Find("opening.hostUniqueId"));
        Assert.Equal(ReviewStatus.Fail, review.For("WN-small", ZoneA)!.Status); // the fixture's rule covers every boundary opening
    }
}
