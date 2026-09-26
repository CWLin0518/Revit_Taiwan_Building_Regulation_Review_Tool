using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;
using Model = BuildingRegulationReview.Core.Tests.Reviews.ReviewRunValidityTests.Model;

namespace BuildingRegulationReview.Core.Tests.Reviews;

/// <summary>
/// P3-T08: the review table of spec 11.7 — three rows, six-state status per row, statistics per zone,
/// per category and Type, per opening kind, and the 總狀態 rule — all counted on the effective status.
/// </summary>
public sealed class ReviewTableTests
{
    private static readonly RuleEvaluationContext Today = new(new DateTime(2025, 6, 1), "TW");
    private static readonly DateTime Later = ReviewRunValidityTests.Ended.AddHours(1);

    private static Rule AreaRule(double limitM2) =>
        new("79", "1", RuleCategory.CompartmentArea, "建築技術規則建築設計施工編第79條", new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("zone.area >= 0 m2"),
            new RuleExpression($"zone.area <= {limitM2} m2"));

    private static Rule RatingRule() =>
        new("wall", "1", RuleCategory.FireResistance, "測試條文 wall", new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("element.category == \"Walls\" && element.isCompartmentBoundary == true"),
            new RuleExpression("element.providedFireRating >= 60 min"),
            evidenceFields: new[] { "element.providedFireRating" });

    private static Rule ProtectionRule() =>
        new("door", "1", RuleCategory.OpeningProtection, "測試條文 door", new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("opening.kind == \"Door\" && opening.hostIsCompartmentBoundary == true"),
            new RuleExpression("opening.providedFireProtection == \"是\""),
            evidenceFields: new[] { "opening.providedFireProtection" });

    /// <summary>All three checks over the fixed model of <see cref="ReviewRunValidityTests"/>; both zones measure 100 m².</summary>
    internal static ReviewRun ReviewAll(Model model, double areaLimitM2 = 150, Guid? runId = null)
    {
        var id = runId ?? ReviewRunValidityTests.RunId;
        var compiled = RuleSetCompiler.Compile(new RuleSet(ReviewRunValidityTests.RuleSetId, ReviewRunValidityTests.Version, "防火",
            new[] { AreaRule(areaLimitM2), RatingRule(), ProtectionRule() }));
        Assert.True(compiled.IsSuccess, compiled.Error.TechnicalDetail);
        var engine = new RuleEngine(compiled.Value);

        var prefix = id == ReviewRunValidityTests.RunId ? "3" : "4";
        var n = 0;
        Guid Next() => Guid.Parse($"{prefix}0000000-0000-0000-0000-{++n:D12}");

        var set = model.Set();
        var area = CompartmentAreaCheck.Review(set, CompartmentAreaInputs.None, engine, Today, id, null, Next);
        var ratings = FireResistanceCheck.Review(set, model.RatingInputs(), engine, Today, id, Next);
        var protection = OpeningProtectionCheck.Review(set, model.ProtectionInputs(), engine, Today, id, Next);
        Assert.True(area.IsSuccess, area.Error.ToString());
        Assert.True(ratings.IsSuccess, ratings.Error.ToString());
        Assert.True(protection.IsSuccess, protection.Error.ToString());

        return new ReviewRun(id, PackageId, ReviewRunValidityTests.RuleSetId, ReviewRunValidityTests.Version, 1, ReviewRunValidityTests.Started)
            .Complete(area.Value.Results.Concat(ratings.Value.Results).Concat(protection.Value.Results), ReviewRunValidityTests.Ended,
                baseline: model.Baseline());
    }

    private static ReviewRunFreshness Check(ReviewRun run, Model now) =>
        ReviewRunValidity.Evaluate(run, now.Baseline(), ReviewRunValidityTests.RuleSetId, ReviewRunValidityTests.Version, 1);

    private static ReviewResult Hand(ReviewStatus status, string checkType, string subject, params (string Field, ReviewValue Value)[] evidence) =>
        new(Guid.NewGuid(), ReviewRunValidityTests.RunId, PackageId, checkType, new[] { subject }, ZoneA.ToString("D"), status,
            ReviewStatusText.IsComparison(status) ? ReviewValue.OfText("x") : null,
            ReviewStatusText.IsComparison(status) ? ReviewValue.OfText("y") : null,
            "r", "1", "條文", status == ReviewStatus.Pass || status == ReviewStatus.NotApplicable || status == ReviewStatus.NotRun ? null : "原因",
            new ReviewEvidence(evidence.Select(e => new ReviewEvidenceItem(e.Field, e.Value))));

    private static ReviewRun HandRun(params ReviewResult[] results) =>
        new ReviewRun(ReviewRunValidityTests.RunId, PackageId, ReviewRunValidityTests.RuleSetId, ReviewRunValidityTests.Version, 1, ReviewRunValidityTests.Started)
            .Complete(results, ReviewRunValidityTests.Ended);

    // --- 六態彙總與總狀態規則（spec 11.7）-------------------------------------------------------------

    [Theory]
    [InlineData(new[] { ReviewStatus.Pass, ReviewStatus.Fail, ReviewStatus.InsufficientData }, ReviewStatus.Fail)]
    [InlineData(new[] { ReviewStatus.Pass, ReviewStatus.ManualReview, ReviewStatus.InsufficientData }, ReviewStatus.InsufficientData)]
    [InlineData(new[] { ReviewStatus.Pass, ReviewStatus.ManualReview }, ReviewStatus.ManualReview)]
    [InlineData(new[] { ReviewStatus.Pass, ReviewStatus.NotRun }, ReviewStatus.NotRun)]
    [InlineData(new[] { ReviewStatus.NotApplicable, ReviewStatus.Pass }, ReviewStatus.Pass)]
    [InlineData(new[] { ReviewStatus.NotApplicable }, ReviewStatus.NotApplicable)]
    [InlineData(new ReviewStatus[0], ReviewStatus.NotRun)]
    public void A_row_shows_its_worst_state(ReviewStatus[] statuses, ReviewStatus expected) =>
        Assert.Equal(expected, ReviewStatusAggregation.Row(statuses));

    [Theory]
    [InlineData(new[] { ReviewStatus.Pass, ReviewStatus.Fail, ReviewStatus.ManualReview }, ReviewVerdict.Fail)]
    [InlineData(new[] { ReviewStatus.Pass, ReviewStatus.InsufficientData }, ReviewVerdict.Pending)]
    [InlineData(new[] { ReviewStatus.Pass, ReviewStatus.ManualReview }, ReviewVerdict.Pending)]
    [InlineData(new[] { ReviewStatus.Pass, ReviewStatus.NotRun }, ReviewVerdict.Pending)]
    [InlineData(new[] { ReviewStatus.Pass, ReviewStatus.NotApplicable }, ReviewVerdict.Pass)]
    [InlineData(new[] { ReviewStatus.NotApplicable }, ReviewVerdict.Pass)]
    [InlineData(new ReviewStatus[0], ReviewVerdict.NotReviewed)]
    public void The_verdict_follows_the_spec_rule(ReviewStatus[] statuses, ReviewVerdict expected) =>
        Assert.Equal(expected, ReviewStatusAggregation.Verdict(statuses));

    [Fact]
    public void Missing_data_is_never_counted_as_a_fail_or_a_pass()
    {
        var counts = new ReviewStatusCounts(new[] { ReviewStatus.InsufficientData, ReviewStatus.ManualReview, ReviewStatus.NotRun, ReviewStatus.Pass });

        Assert.Equal(0, counts.Fail);
        Assert.Equal(1, counts.Pass);
        Assert.Equal(3, counts.Unknown);
        Assert.Equal(4, counts.Total);
        Assert.Equal("符合 1／未符合 0／待確認 3／不適用 0", counts.Text);
        Assert.Equal("待確認", ReviewVerdictText.Label(ReviewVerdict.Pending));
        Assert.Equal("需更新", ReviewVerdictText.Label(ReviewVerdict.NeedsUpdate));
    }

    // --- 表格內容 -----------------------------------------------------------------------------------

    [Fact]
    public void A_clean_model_reads_pass_on_all_three_rows()
    {
        var table = ReviewTable.Build(ReviewAll(new Model()));

        Assert.Equal(new[] { "防火區劃面積", "構件防火時效", "防火門窗", "帷幕牆區劃交接", "垂直區劃" },
            table.Sections.Select(s => s.Title));
        var empty = new[] { ReviewCheckTypes.CompartmentContinuity, ReviewCheckTypes.VerticalCompartment };
        Assert.All(table.Sections.Where(s => !empty.Contains(s.CheckType)),
            s => Assert.Equal(ReviewStatus.Pass, s.Status));

        // 帷幕牆區劃交接 and 垂直區劃 have no result in this model: an empty row is 未檢討 and adds nothing
        // to the verdict — the table counts results, not rows (spec 11.7).
        Assert.All(empty, type => Assert.Equal(ReviewStatus.NotRun, table.Section(type).Status));
        Assert.Equal(ReviewVerdict.Pass, table.Verdict);
        Assert.False(table.IsStale);
        Assert.Empty(table.OtherEntries);
        Assert.Equal(table.Run.Results.Count, table.Counts.Total);
    }

    [Fact]
    public void An_oversized_zone_fails_its_row_and_the_table()
    {
        var table = ReviewTable.Build(ReviewAll(new Model(), areaLimitM2: 50));
        var area = table.Section(ReviewCheckTypes.CompartmentArea);

        Assert.Equal(ReviewStatus.Fail, area.Status);
        Assert.Equal(ReviewVerdict.Fail, area.Verdict);
        Assert.Equal(2, area.Counts.Fail);
        Assert.Equal(ReviewVerdict.Fail, table.Verdict);
        Assert.Equal(ReviewStatus.Pass, table.Section(ReviewCheckTypes.FireResistance).Status);

        var zones = area.GroupsBy(ReviewTableGrouping.Zone).ToList();
        Assert.Equal(new[] { "A 區", "B 區" }, zones.Select(g => g.Label).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(zones, g => Assert.Equal(1, g.Counts.Fail));

        var entry = area.Entries.First();
        Assert.Equal("建築技術規則建築設計施工編第79條", entry.LegalReference);
        Assert.Equal("區劃", entry.CategoryLabel);
        Assert.NotEmpty(entry.LocateUniqueIds);
        Assert.NotNull(entry.ActualValue);
        Assert.NotNull(entry.RequiredValue);
    }

    [Fact]
    public void Fire_ratings_are_counted_by_category_and_by_type()
    {
        var model = new Model();
        model.Ratings["type-a"] = 30;
        var section = ReviewTable.Build(ReviewAll(model)).Section(ReviewCheckTypes.FireResistance);

        var category = Assert.Single(section.GroupsBy(ReviewTableGrouping.Category));
        Assert.Equal("牆", category.Label);
        Assert.Equal(1, category.Counts.Fail);

        var types = section.GroupsBy(ReviewTableGrouping.Type).ToDictionary(g => g.Key);
        Assert.Equal(ReviewStatus.Fail, types["牆/type-a"].Status);
        Assert.Equal(ReviewStatus.Pass, types["牆/type-b"].Status);
        Assert.Equal("牆：type-a", types["牆/type-a"].Label);
        Assert.Equal(ReviewStatus.Fail, section.Status);

        var failing = Assert.Single(section.Entries, e => e.EffectiveStatus == ReviewStatus.Fail);
        Assert.Equal(new[] { "W-left" }, failing.LocateUniqueIds);
        Assert.Equal("A 區", failing.ZoneName);
    }

    [Theory]
    [InlineData("否", ReviewStatus.Fail, ReviewVerdict.Fail)]
    [InlineData("未設定", ReviewStatus.InsufficientData, ReviewVerdict.Pending)]
    [InlineData("是", ReviewStatus.Pass, ReviewVerdict.Pass)]
    public void Openings_are_counted_by_kind(string protection, ReviewStatus row, ReviewVerdict verdict)
    {
        var model = new Model { DoorProtection = protection };
        var table = ReviewTable.Build(ReviewAll(model));
        var section = table.Section(ReviewCheckTypes.OpeningProtection);

        Assert.Equal(row, section.Status);
        Assert.Equal(verdict, table.Verdict);
        var door = Assert.Single(section.GroupsBy(ReviewTableGrouping.OpeningKind));
        Assert.Equal("門", door.Label);
        Assert.Equal(row, door.Status);
    }

    [Fact]
    public void Curtain_panels_and_doors_in_curtain_walls_count_as_curtain_wall()
    {
        var run = HandRun(
            Hand(ReviewStatus.Fail, ReviewCheckTypes.OpeningProtection, "cp-1", ("source.category", ReviewValue.OfText("CurtainPanel"))),
            Hand(ReviewStatus.Pass, ReviewCheckTypes.OpeningProtection, "cd-1", ("source.category", ReviewValue.OfText("Door")),
                ("source.hostIsCurtainWall", ReviewValue.OfBoolean(true))),
            Hand(ReviewStatus.Pass, ReviewCheckTypes.OpeningProtection, "w-1", ("source.category", ReviewValue.OfText("Window"))),
            Hand(ReviewStatus.ManualReview, ReviewCheckTypes.OpeningProtection, "x-1"));

        var groups = ReviewTable.Build(run).Section(ReviewCheckTypes.OpeningProtection)
            .GroupsBy(ReviewTableGrouping.OpeningKind).ToDictionary(g => g.Label);

        Assert.Equal(2, groups["幕牆"].Counts.Total);
        Assert.Equal(ReviewStatus.Fail, groups["幕牆"].Status);
        Assert.Equal(1, groups["窗"].Counts.Pass);
        Assert.Equal(ReviewStatus.ManualReview, groups["未分類"].Status);
    }

    /// <summary>帷幕牆規格 §7.2: the 帷幕牆 row breaks down into the three kinds, CW-H per 區劃來源條文.</summary>
    [Fact]
    public void Curtain_wall_junctions_are_counted_by_kind_and_CW_H_by_the_clause_its_compartment_came_from()
    {
        var run = HandRun(
            Junction(ReviewStatus.NotApplicable, CurtainWallJunctionKind.WallToCurtainWall, "cw-1",
                CurtainWallJunctionReferences.Article79),
            Junction(ReviewStatus.Fail, CurtainWallJunctionKind.WallToCurtainWall, "cw-2",
                CurtainWallJunctionReferences.Article83),
            Junction(ReviewStatus.InsufficientData, CurtainWallJunctionKind.FloorToCurtainWall, "cw-3",
                CurtainWallJunctionReferences.Article79_3),
            Junction(ReviewStatus.Pass, CurtainWallJunctionKind.CurtainPanelOther, "cw-4", reference: null));

        var section = ReviewTable.Build(run).Section(ReviewCheckTypes.CompartmentContinuity);
        Assert.Equal("帷幕牆區劃交接", section.Title);
        Assert.Equal(ReviewStatus.Fail, section.Status);

        var kinds = section.GroupsBy(ReviewTableGrouping.JunctionKind).ToDictionary(g => g.Label);
        Assert.Equal(new[] { "帷幕牆區劃交接（水平）", "帷幕牆區劃交接（層間）", "帷幕牆其他部分時效" }, kinds.Keys);
        Assert.Equal(2, kinds["帷幕牆區劃交接（水平）"].Counts.Total);
        Assert.Equal(ReviewStatus.InsufficientData, kinds["帷幕牆區劃交接（層間）"].Status);
        Assert.Equal(1, kinds["帷幕牆其他部分時效"].Counts.Pass);

        // 第79條 and 第83條 are counted apart (docs §2.5); 第79條之4's 其他部分 has no compartment of its
        // own, so it appears in its kind row only.
        var clauses = section.GroupsBy(ReviewTableGrouping.JunctionLegalReference).Select(g => g.Label).ToList();
        Assert.Equal(new[]
        {
            "帷幕牆區劃交接（水平）：第79條", "帷幕牆區劃交接（水平）：第83條", "帷幕牆區劃交接（層間）：第79條之3"
        }, clauses);
    }

    private static ReviewResult Junction(ReviewStatus status, CurtainWallJunctionKind kind, string subject, string? reference)
    {
        var evidence = new List<(string, ReviewValue)>
        {
            ("junction.kind", ReviewValue.OfText(CurtainWallJunctionKinds.RuleText(kind)))
        };
        if (reference is not null) evidence.Add(("junction.hostLegalReference", ReviewValue.OfText(reference)));
        return Hand(status, ReviewCheckTypes.CompartmentContinuity, subject, evidence.ToArray());
    }

    /// <summary>
    /// 垂直區劃規格 §7: the 垂直區劃 row breaks down into the three requirements of 第79條之2第1項, always
    /// in 條文 order however the run met them, and one 維修門 appears in two of those rows — which is
    /// the whole point of grouping by requirement rather than by element (§9 第8項).
    /// </summary>
    [Fact]
    public void Vertical_compartment_results_are_counted_by_requirement_in_clause_order()
    {
        var run = HandRun(
            Shaft(ReviewStatus.Pass, VerticalCompartmentRequirement.ShaftDoorSmokeSeal, "D-shaft"),
            Shaft(ReviewStatus.Fail, VerticalCompartmentRequirement.ShaftDoorRating, "D-shaft"),
            Shaft(ReviewStatus.InsufficientData, VerticalCompartmentRequirement.HoistwaySmokeSeal, "D-hoistway"));

        var section = ReviewTable.Build(run).Section(ReviewCheckTypes.VerticalCompartment);
        Assert.Equal("垂直區劃", section.Title);
        Assert.Equal(ReviewStatus.Fail, section.Status);

        var rows = section.GroupsBy(ReviewTableGrouping.ShaftRequirement).ToList();
        Assert.Equal(new[] { "昇降機道防火設備遮煙性能", "管道間維修門防火時效", "管道間維修門遮煙性能" },
            rows.Select(g => g.Label));
        Assert.All(rows, g => Assert.Equal(1, g.Counts.Total));
        Assert.Equal(new[] { "D-shaft" }, rows[1].ResultIds.Select(id => run.Result(id)!.SubjectUniqueIds.Single()));
        Assert.Equal(new[] { "D-shaft" }, rows[2].ResultIds.Select(id => run.Result(id)!.SubjectUniqueIds.Single()));

        var rating = section.Entries.Single(e => e.ShaftRequirement == VerticalCompartmentRequirement.ShaftDoorRating);
        Assert.Equal("管道間維修門防火時效", rating.ShaftRequirementLabel);
        Assert.Equal("門", rating.CategoryLabel);
    }

    /// <summary>
    /// The row name is the one the check stored with the result: the 檢討表 is rebuilt from the run
    /// alone, so a stored label is shown as it was written and never re-derived from the model.
    /// </summary>
    [Fact]
    public void A_stored_requirement_label_is_what_the_row_shows()
    {
        var run = HandRun(Hand(ReviewStatus.Pass, ReviewCheckTypes.VerticalCompartment, "D-shaft",
            (VerticalCompartmentRequirements.RequirementField,
                ReviewValue.OfText(VerticalCompartmentRequirements.RuleText(VerticalCompartmentRequirement.ShaftDoorRating))),
            ("shaft.requirementLabel", ReviewValue.OfText("管道間維修門防火時效（舊版用字）"))));

        var group = Assert.Single(ReviewTable.Build(run).Section(ReviewCheckTypes.VerticalCompartment)
            .GroupsBy(ReviewTableGrouping.ShaftRequirement));
        Assert.Equal("管道間維修門防火時效（舊版用字）", group.Label);
    }

    private static ReviewResult Shaft(ReviewStatus status, VerticalCompartmentRequirement requirement, string subject) =>
        Hand(status, ReviewCheckTypes.VerticalCompartment, subject,
            (VerticalCompartmentRequirements.RequirementField, ReviewValue.OfText(VerticalCompartmentRequirements.RuleText(requirement))),
            ("shaft.requirementLabel", ReviewValue.OfText(VerticalCompartmentRequirements.Label(requirement))),
            ("source.category", ReviewValue.OfText(CandidateCategories.RuleText(CandidateCategory.Door))));

    [Fact]
    public void A_result_of_an_unknown_check_is_kept_and_counted()
    {
        var table = ReviewTable.Build(HandRun(
            Hand(ReviewStatus.Pass, ReviewCheckTypes.FireResistance, "w"),
            Hand(ReviewStatus.Fail, "Future", "f")));

        var other = Assert.Single(table.OtherEntries);
        Assert.Equal("Future", other.CheckType);
        Assert.Equal(ReviewVerdict.Fail, table.Verdict);
        Assert.All(table.Sections, s => Assert.NotNull(s.Groups));
        Assert.Equal(ReviewStatus.NotRun, table.Section(ReviewCheckTypes.CompartmentArea).Status);
    }

    // --- 人工覆寫與失效 -------------------------------------------------------------------------------

    [Fact]
    public void An_active_override_is_what_the_table_counts()
    {
        var run = ReviewAll(new Model { DoorProtection = "否" });
        var door = run.Results.Single(r => r.CheckType == ReviewCheckTypes.OpeningProtection && r.Status == ReviewStatus.Fail);
        var overridden = ReviewOverrides.Apply(run, door.ResultId, ReviewStatus.Pass, "現場已更換防火門", "王建築師", Later);
        Assert.True(overridden.IsSuccess, overridden.Error.ToString());

        var table = ReviewTable.Build(overridden.Value);
        var entry = table.Entry(door.ResultId)!;

        Assert.Equal(ReviewStatus.Fail, entry.ComputedStatus);
        Assert.Equal(ReviewStatus.Pass, entry.EffectiveStatus);
        Assert.True(entry.IsOverridden);
        Assert.Equal("符合（人工覆寫，原為未符合）", entry.StatusText);
        Assert.Equal(ReviewVerdict.Pass, table.Verdict);
    }

    [Fact]
    public void An_override_awaiting_confirmation_does_not_count()
    {
        var model = new Model { DoorProtection = "否" };
        var run = ReviewAll(model);
        var door = run.Results.Single(r => r.CheckType == ReviewCheckTypes.OpeningProtection && r.Status == ReviewStatus.Fail);
        var overridden = ReviewOverrides.Apply(run, door.ResultId, ReviewStatus.Pass, "現場已更換防火門", "王建築師", Later).Value;

        model.DoorProtection = "未設定";
        var suspended = ReviewRunValidity.WithOverridesSuspended(Check(overridden, model));
        var table = ReviewTable.Build(suspended, Check(suspended, model));
        var entry = table.Entry(door.ResultId)!;

        Assert.True(entry.OverrideNeedsReconfirmation);
        Assert.Equal(ReviewStatus.Fail, entry.EffectiveStatus);
        Assert.Contains("人工覆寫需重新確認", entry.StatusText);
    }

    [Fact]
    public void A_stale_run_reads_needs_update_whatever_it_said()
    {
        var run = ReviewAll(new Model());
        var model = new Model();
        model.Ratings["type-a"] = 30;

        var table = ReviewTable.Build(run, Check(run, model));

        Assert.True(table.IsStale);
        Assert.NotEmpty(table.StaleReasons);
        Assert.Equal(ReviewVerdict.NeedsUpdate, table.Verdict);
        var section = table.Section(ReviewCheckTypes.FireResistance);
        Assert.Equal(ReviewVerdict.NeedsUpdate, section.Verdict);
        Assert.Equal(ReviewStatus.Pass, section.Status);
        Assert.Contains(section.Entries, e => e.IsStale && e.StatusText.EndsWith("〔需更新〕", StringComparison.Ordinal));
        Assert.Equal(ReviewVerdict.Pass, table.Section(ReviewCheckTypes.CompartmentArea).Verdict);
    }

    [Fact]
    public void A_rule_version_change_makes_every_row_need_an_update()
    {
        var run = ReviewAll(new Model());
        var freshness = ReviewRunValidity.Evaluate(run, new Model().Baseline(), ReviewRunValidityTests.RuleSetId, "2025.1", 1);

        var table = ReviewTable.Build(run, freshness);

        Assert.All(table.Sections.Where(s => s.Entries.Count > 0), s => Assert.Equal(ReviewVerdict.NeedsUpdate, s.Verdict));
        Assert.All(table.Entries, e => Assert.True(e.IsStale));
    }

    [Fact]
    public void An_unfinished_run_is_not_reviewed_and_freshness_must_match_the_run()
    {
        var cancelled = new ReviewRun(ReviewRunValidityTests.RunId, PackageId, ReviewRunValidityTests.RuleSetId, ReviewRunValidityTests.Version, 1,
            ReviewRunValidityTests.Started).Complete(Array.Empty<ReviewResult>(), ReviewRunValidityTests.Ended, ReviewRunState.Cancelled);
        Assert.Equal(ReviewVerdict.NotReviewed, ReviewTable.Build(cancelled).Verdict);
        Assert.Equal(ReviewVerdict.NotReviewed, ReviewTable.Build(HandRun()).Verdict);

        var other = ReviewAll(new Model(), runId: ReviewRunValidityTests.NextRunId);
        Assert.Throws<ArgumentException>(() => ReviewTable.Build(ReviewAll(new Model()), Check(other, new Model())));
    }
}
