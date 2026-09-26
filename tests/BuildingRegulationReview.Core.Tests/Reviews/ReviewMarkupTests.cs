using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;
using Model = BuildingRegulationReview.Core.Tests.Reviews.ReviewRunValidityTests.Model;

namespace BuildingRegulationReview.Core.Tests.Reviews;

/// <summary>
/// P3-T08: what the review view shows — red regions over failing 區劃 carrying Package／Run／Zone ID,
/// red overrides on failing elements with their original look recorded — and that marking only ever
/// touches the current package's marks and the elements the tool recorded (spec 13.2).
/// </summary>
public sealed class ReviewMarkupTests
{
    private static readonly Guid OtherPackage = Guid.Parse("99999999-2222-3333-4444-555555555555");
    private static readonly DateTime Later = ReviewRunValidityTests.Ended.AddHours(1);

    private static ReviewMarkupPlan Plan(ReviewRun run, Model model, ReviewRunFreshness? freshness = null)
    {
        var plan = ReviewMarkupPlan.Build(ReviewTable.Build(run, freshness), model.Set().Zones);
        Assert.True(plan.IsSuccess, plan.Error.ToString());
        return plan.Value;
    }

    /// <summary>
    /// The review view as the adapter would leave it: what the diff says, applied literally. Enough to
    /// show that re-running does not multiply anything and that foreign things are never touched.
    /// </summary>
    private sealed class FakeView
    {
        private int _next;
        public List<ExistingReviewMark> Marks { get; } = new();
        public Dictionary<string, string> Looks { get; } = new(StringComparer.Ordinal);
        public List<RecordedElementOverride> Records { get; } = new();

        public ReviewMarkupDiff Mark(ReviewMarkupPlan plan)
        {
            var diff = ReviewMarkupDiff.Compute(plan, Marks, Records);
            foreach (var change in diff.Regions)
            {
                if (change.Action == ReviewMarkAction.Unchanged) continue;
                if (change.Existing is not null) Marks.RemoveAll(m => m.ElementUniqueId == change.Existing.ElementUniqueId);
                if (change.Planned is not null)
                    Marks.Add(new ExistingReviewMark($"region-{++_next}", change.Planned.Key.ToToken(), change.Planned.Signature));
            }

            foreach (var change in diff.Notes)
                Redraw(change.Action, change.Existing, change.Planned?.Key, change.Planned?.Signature, "note");
            foreach (var change in diff.Bands)
                Redraw(change.Action, change.Existing, change.Planned?.Key, change.Planned?.Signature, "band");

            foreach (var change in diff.Overrides)
            {
                var current = Looks.TryGetValue(change.ElementUniqueId, out var look) ? look : "none";
                Records.RemoveAll(r => r.ElementUniqueId == change.ElementUniqueId);
                switch (change.Action)
                {
                    case ReviewMarkAction.Create:
                        Records.Add(new RecordedElementOverride(change.ElementUniqueId, plan.RunId, current, "red"));
                        Looks[change.ElementUniqueId] = "red";
                        break;
                    case ReviewMarkAction.Update:
                    case ReviewMarkAction.Unchanged:
                        Records.Add(change.Recorded!.ForRun(plan.RunId, "red"));
                        Looks[change.ElementUniqueId] = "red";
                        break;
                    case ReviewMarkAction.Remove:
                        if (ReviewOverrideRestore.Decide(change.Recorded!, current) == OverrideRestoreDecision.RestoreOriginal)
                            Looks[change.ElementUniqueId] = change.Recorded!.OriginalState;
                        break;
                }
            }
            return diff;
        }

        private void Redraw(ReviewMarkAction action, ExistingReviewMark? existing, ReviewMarkKey? key, string? signature, string prefix)
        {
            if (action == ReviewMarkAction.Unchanged) return;
            if (existing is not null) Marks.RemoveAll(m => m.ElementUniqueId == existing.ElementUniqueId);
            if (key is ReviewMarkKey drawn) Marks.Add(new ExistingReviewMark($"{prefix}-{++_next}", drawn.ToToken(), signature!));
        }
    }

    // --- 標示計畫 -----------------------------------------------------------------------------------

    [Fact]
    public void A_failing_zone_gets_one_red_region_per_enclosed_area_carrying_package_run_and_zone()
    {
        var model = new Model();
        var run = ReviewTableTests.ReviewAll(model, areaLimitM2: 50);
        var plan = Plan(run, model);

        Assert.Equal(2, plan.Regions.Count);
        var region = plan.Regions.Single(r => r.Key.ZoneId == ZoneA);
        Assert.Equal(PackageId, region.Key.PackageId);
        Assert.Equal(run.RunId, region.Key.RunId);
        Assert.Equal("A 區", region.ZoneName);
        Assert.Equal(4, Assert.Single(region.Loops).Count);
        Assert.Contains(ZoneA.ToString("D"), region.Key.ToLabel());
        Assert.Contains(run.RunId.ToString("D"), region.Key.ToLabel());

        Assert.True(ReviewMarkKey.TryParse(region.Key.ToToken(), out var parsed));
        Assert.Equal(region.Key, parsed);
        Assert.True(ManagedOwnership.BelongsTo(region.Key.ToToken(), PackageId));
        Assert.False(ManagedOwnership.BelongsTo(region.Key.ToToken(), OtherPackage));
        Assert.False(ReviewMarkKey.TryParse(new ManagedOutputKey(PackageId, ManagedOutputKind.ReviewView).ToToken(), out _));
        Assert.Empty(plan.Overrides);
    }

    [Fact]
    public void Failing_members_and_openings_are_painted_once_each_and_passes_are_not()
    {
        var model = new Model { DoorProtection = "否" };
        model.Ratings["type-a"] = 30;
        var plan = Plan(ReviewTableTests.ReviewAll(model), model);

        Assert.Equal(new[] { "D-left", "W-left" }, plan.Overrides.Select(o => o.ElementUniqueId));
        Assert.Empty(plan.Regions);
        Assert.Empty(plan.Skipped);
        Assert.Contains(ReviewCheckTypes.FireResistance, plan.Overrides.Single(o => o.ElementUniqueId == "W-left").CheckTypes);
    }

    [Fact]
    public void The_plan_follows_the_effective_status()
    {
        var model = new Model { DoorProtection = "否" };
        var run = ReviewTableTests.ReviewAll(model);
        var door = run.Results.Single(r => r.CheckType == ReviewCheckTypes.OpeningProtection && r.Status == ReviewStatus.Fail);
        var wall = run.Results.First(r => r.CheckType == ReviewCheckTypes.FireResistance && r.SubjectUniqueIds.Contains("W-right"));

        var overridden = ReviewOverrides.Apply(run, door.ResultId, ReviewStatus.Pass, "已更換防火門", "王建築師", Later).Value;
        overridden = ReviewOverrides.Apply(overridden, wall.ResultId, ReviewStatus.Fail, "現場為輕隔間", "王建築師", Later.AddMinutes(1)).Value;

        Assert.Equal(new[] { "W-right" }, Plan(overridden, model).Overrides.Select(o => o.ElementUniqueId));
    }

    [Fact]
    public void A_stale_failure_is_not_painted_but_listed_as_skipped()
    {
        var model = new Model();
        model.Ratings["type-a"] = 30;
        var run = ReviewTableTests.ReviewAll(model);
        var now = new Model();
        var freshness = ReviewRunValidity.Evaluate(run, now.Baseline(), ReviewRunValidityTests.RuleSetId, ReviewRunValidityTests.Version, 1);

        var plan = Plan(run, now, freshness);

        Assert.Empty(plan.Overrides);
        var skipped = Assert.Single(plan.Skipped);
        Assert.Contains("過期", skipped.Reason);
    }

    [Fact]
    public void A_linked_element_and_a_zone_without_geometry_are_skipped_with_a_reason()
    {
        var linked = new ReviewResult(Guid.NewGuid(), ReviewRunValidityTests.RunId, PackageId, ReviewCheckTypes.FireResistance,
            new[] { "L-1" }, ZoneA.ToString("D"), ReviewStatus.Fail, ReviewValue.OfText("30"), ReviewValue.OfText("60"),
            "wall", "1", "條文", "時效不足", new ReviewEvidence(new[]
            {
                new ReviewEvidenceItem("source.category", ReviewValue.OfText("Walls")),
                new ReviewEvidenceItem("source.linkInstanceUniqueId", ReviewValue.OfText("link-1"))
            }));
        var lostZone = new ReviewResult(Guid.NewGuid(), ReviewRunValidityTests.RunId, PackageId, ReviewCheckTypes.CompartmentArea,
            new[] { "area-x" }, Guid.NewGuid().ToString("D"), ReviewStatus.Fail, ReviewValue.OfText("200"), ReviewValue.OfText("150"),
            "79", "1", "條文", "超過上限");
        var run = new ReviewRun(ReviewRunValidityTests.RunId, PackageId, ReviewRunValidityTests.RuleSetId, ReviewRunValidityTests.Version, 1,
            ReviewRunValidityTests.Started).Complete(new[] { linked, lostZone }, ReviewRunValidityTests.Ended);

        var plan = Plan(run, new Model());

        Assert.Empty(plan.Overrides);
        Assert.Empty(plan.Regions);
        Assert.Equal(2, plan.Skipped.Count);
        Assert.Contains(plan.Skipped, s => s.ResultId == linked.ResultId && s.Reason.Contains("連結模型"));
        Assert.Contains(plan.Skipped, s => s.ResultId == lostZone.ResultId && s.Reason.Contains("範圍"));
    }

    [Fact]
    public void Only_a_completed_run_can_be_marked()
    {
        var running = new ReviewRun(ReviewRunValidityTests.RunId, PackageId, ReviewRunValidityTests.RuleSetId, ReviewRunValidityTests.Version, 1,
            ReviewRunValidityTests.Started);

        var plan = ReviewMarkupPlan.Build(ReviewTable.Build(running), new Model().Set().Zones);

        Assert.False(plan.IsSuccess);
        Assert.Equal(ReviewErrorCode.ReviewMarkRefused, plan.Error.Code);
        Assert.True(ReviewErrorCode.IsKnown(ReviewErrorCode.ReviewMarkSkipped));
        Assert.True(ReviewErrorCode.IsKnown(ReviewErrorCode.ReviewMarkUserChangeKept));
    }

    // --- 冪等更新（spec 13.2、16.3 第 10 項）-----------------------------------------------------------

    [Fact]
    public void Marking_the_same_run_three_times_does_not_add_anything()
    {
        var model = new Model { DoorProtection = "否" };
        var plan = Plan(ReviewTableTests.ReviewAll(model, areaLimitM2: 50), model);
        var view = new FakeView();

        var first = view.Mark(plan);
        // Two failing zones and the failing door.
        Assert.Equal(3, first.Count(ReviewMarkAction.Create));
        Assert.Contains("新增 3", first.Summary);

        for (var i = 0; i < 2; i++)
        {
            var again = view.Mark(plan);
            Assert.False(again.HasChanges);
            Assert.Equal(3, again.Count(ReviewMarkAction.Unchanged));
        }

        Assert.Equal(2, view.Marks.Count);
        Assert.Single(view.Records);
    }

    [Fact]
    public void A_new_run_takes_over_the_marks_of_the_last_one_instead_of_adding_its_own()
    {
        var model = new Model { DoorProtection = "否" };
        var view = new FakeView();
        view.Mark(Plan(ReviewTableTests.ReviewAll(model, areaLimitM2: 50), model));

        var next = view.Mark(Plan(ReviewTableTests.ReviewAll(model, areaLimitM2: 50, runId: ReviewRunValidityTests.NextRunId), model));

        Assert.Equal(0, next.Count(ReviewMarkAction.Create));
        Assert.Equal(3, next.Count(ReviewMarkAction.Update));
        Assert.Equal(2, view.Marks.Count);
        Assert.All(view.Marks, m => Assert.Equal(ReviewRunValidityTests.NextRunId, m.Key.RunId));
        Assert.All(view.Records, r => Assert.Equal(ReviewRunValidityTests.NextRunId, r.RunId));
        // The original look is the one from before the first run, not the red the last run left.
        Assert.Equal("none", view.Records.Single().OriginalState);
    }

    [Fact]
    public void What_no_longer_fails_is_removed_and_the_original_look_comes_back()
    {
        var failing = new Model { DoorProtection = "否" };
        var view = new FakeView();
        view.Looks["D-left"] = "halftone";
        view.Mark(Plan(ReviewTableTests.ReviewAll(failing, areaLimitM2: 50), failing));
        Assert.Equal("red", view.Looks["D-left"]);

        var fixedModel = new Model();
        var diff = view.Mark(Plan(ReviewTableTests.ReviewAll(fixedModel, runId: ReviewRunValidityTests.NextRunId), fixedModel));

        Assert.Equal(3, diff.Count(ReviewMarkAction.Remove));
        Assert.Empty(view.Marks);
        Assert.Empty(view.Records);
        Assert.Equal("halftone", view.Looks["D-left"]);
    }

    [Fact]
    public void Another_packages_marks_and_unreadable_marks_are_never_touched()
    {
        var model = new Model();
        var run = ReviewTableTests.ReviewAll(model, areaLimitM2: 50);
        var foreign = new ExistingReviewMark("foreign-1", new ReviewMarkKey(OtherPackage, run.RunId, ZoneA, 0).ToToken(), "x");
        var junk = new ExistingReviewMark("junk-1", "人工繪製", "");

        var diff = ReviewMarkupDiff.Compute(Plan(run, model), new[] { foreign, junk }, Array.Empty<RecordedElementOverride>());

        Assert.Equal(2, diff.ForeignMarks);
        Assert.DoesNotContain(diff.Regions, r => r.Existing is not null);
        Assert.Equal(2, diff.Count(ReviewMarkAction.Create));
    }

    [Fact]
    public void A_changed_zone_outline_updates_its_region_and_a_duplicate_is_removed()
    {
        var model = new Model();
        var run = ReviewTableTests.ReviewAll(model, areaLimitM2: 50);
        var plan = Plan(run, model);
        var a = plan.Regions.Single(r => r.Key.ZoneId == ZoneA);
        var b = plan.Regions.Single(r => r.Key.ZoneId == ZoneB);

        var diff = ReviewMarkupDiff.Compute(plan, new[]
        {
            new ExistingReviewMark("a-old", a.Key.ToToken(), "region|A 區|moved"),
            new ExistingReviewMark("b-1", b.Key.ToToken(), b.Signature),
            new ExistingReviewMark("b-2", b.Key.ToToken(), b.Signature)
        }, Array.Empty<RecordedElementOverride>());

        Assert.Equal(ReviewMarkAction.Update, diff.Regions.Single(r => r.Planned == a).Action);
        Assert.Equal(ReviewMarkAction.Unchanged, diff.Regions.Single(r => r.Planned == b).Action);
        Assert.Equal("b-2", diff.Regions.Single(r => r.Action == ReviewMarkAction.Remove).Existing!.ElementUniqueId);
    }

    [Fact]
    public void A_user_change_to_a_painted_element_is_kept_when_the_tool_lets_go()
    {
        var record = new RecordedElementOverride("W-left", ReviewRunValidityTests.RunId, "none", "red");

        Assert.Equal(OverrideRestoreDecision.RestoreOriginal, ReviewOverrideRestore.Decide(record, "red"));
        Assert.Equal(OverrideRestoreDecision.KeepUserChange, ReviewOverrideRestore.Decide(record, "blue"));
        Assert.Equal(OverrideRestoreDecision.ForgetMissing, ReviewOverrideRestore.Decide(record, null));
    }

    [Fact]
    public void The_marking_result_counts_every_outcome()
    {
        var result = new ReviewMarkupResult(PackageId, ReviewRunValidityTests.RunId, "view-1", new[]
        {
            new ReviewMarkupItem(ApplyOutcome.Created, "區劃"),
            new ReviewMarkupItem(ApplyOutcome.Updated, "牆", "W-left", "沿用前次"),
            new ReviewMarkupItem(ApplyOutcome.Deleted, "舊標示"),
            new ReviewMarkupItem(ApplyOutcome.Skipped, "連結牆", null, "連結模型")
        });

        Assert.Equal("檢討視圖標示完成：新增 1、更新 1、刪除 1、略過 1、失敗 0", result.Summary);
        Assert.False(result.IsRolledBack);
        Assert.Equal("已略過 連結牆：連結模型", result.Items[3].Text);
        Assert.True(new ReviewMarkupResult(PackageId, ReviewRunValidityTests.RunId, null, Array.Empty<ReviewMarkupItem>(), "視圖錯誤").IsRolledBack);
    }

    // --- 帷幕牆區劃交接的標示（帷幕牆規格 §7.1、§10 案例 19）-------------------------------------------

    private const string CurtainWall = "cw-1";

    private static readonly CurtainWallJunctionPlacement Crossing =
        CurtainWallJunctionPlacement.At(new Point2D(5000, 0), 0, 3600);

    private static readonly CurtainWallJunctionPlacement SpandrelBand =
        CurtainWallJunctionPlacement.Band(new Point2D(0, 0), new Point2D(12000, 0), 2700, 4500);

    /// <summary>A failing 帷幕牆區劃交接 result, with exactly the evidence the check writes for one.</summary>
    private static ReviewResult Junction(
        Guid runId,
        CurtainWallJunctionKind kind,
        string junctionId,
        IEnumerable<string>? panels = null,
        CurtainWallJunctionPlacement? placement = null,
        double? runMeters = null,
        double? projectionMeters = null,
        string? host = null,
        ReviewStatus status = ReviewStatus.Fail)
    {
        var panelList = (panels ?? Array.Empty<string>()).ToList();
        var evidence = new List<ReviewEvidenceItem>
        {
            new("junction.kind", ReviewValue.OfText(CurtainWallJunctionKinds.RuleText(kind))),
            new("junction.id", ReviewValue.OfText(junctionId)),
            new("junction.curtainWallUniqueId", ReviewValue.OfText(CurtainWall))
        };
        if (panelList.Count > 0) evidence.Add(new("junction.panels", ReviewValue.OfText(string.Join(",", panelList))));
        if (placement is not null) evidence.Add(new("junction.placement", ReviewValue.OfText(placement.ToEvidenceText())));
        if (runMeters is double measured)
            evidence.Add(new(kind == CurtainWallJunctionKind.FloorToCurtainWall
                ? "junction.continuousFireRatedHeight"
                : "junction.continuousFireRatedLength", ReviewValue.Quantity(measured, ReviewUnit.Meter)));
        if (projectionMeters is double projection)
            evidence.Add(new("junction.projectionDepth", ReviewValue.Quantity(projection, ReviewUnit.Meter)));

        var subjects = new[] { CurtainWall }.Concat(host is null ? Array.Empty<string>() : new[] { host }).Concat(panelList);
        return new ReviewResult(Guid.NewGuid(), runId, PackageId, ReviewCheckTypes.CompartmentContinuity,
            subjects, ZoneA.ToString("D"), status, ReviewValue.OfText("0"), ReviewValue.OfText("0.9"),
            "cw", "1", "條文", "交接處未維持區劃連續性", new ReviewEvidence(evidence));
    }

    private static ReviewRun JunctionRun(Guid runId, params ReviewResult[] results) =>
        new ReviewRun(runId, PackageId, ReviewRunValidityTests.RuleSetId, ReviewRunValidityTests.Version, 1,
            ReviewRunValidityTests.Started).Complete(results, ReviewRunValidityTests.Ended);

    private static ReviewResult HorizontalJunction(Guid runId, params string[] panels) =>
        Junction(runId, CurtainWallJunctionKind.WallToCurtainWall, "CW-H:cw-1:wall-1", panels, Crossing, 0.45, 0.12, "wall-1");

    private static ReviewResult SpandrelJunction(Guid runId) =>
        Junction(runId, CurtainWallJunctionKind.FloorToCurtainWall, "CW-V:cw-1:floor-1:0",
            new[] { "panel-3" }, SpandrelBand, 0.6, 0.0, "floor-1");

    [Fact]
    public void A_failing_CW_H_junction_paints_the_panels_it_covers_and_annotates_the_intersection()
    {
        var plan = Plan(JunctionRun(ReviewRunValidityTests.RunId,
            HorizontalJunction(ReviewRunValidityTests.RunId, "panel-1", "panel-2")), new Model());

        // Not the curtain wall and not the 區劃牆: §7.1 asks for the panels at the junction.
        Assert.Equal(new[] { "panel-1", "panel-2" }, plan.Overrides.Select(o => o.ElementUniqueId));

        var note = Assert.Single(plan.Notes);
        Assert.Equal(CurtainWallJunctionKind.WallToCurtainWall, note.JunctionKind);
        Assert.Equal("CW-H-01　交接帶連續具時效長度 450 mm／突出 120 mm", note.Text);
        Assert.Equal("CW-H-01", note.Number);
        Assert.Equal(ReviewMarkKind.JunctionNote, note.Key.Kind);
        Assert.Equal("CW-H:cw-1:wall-1", note.Key.Subject);
        Assert.Equal(ZoneA, note.Key.ZoneId);
        Assert.Equal("A 區", note.ZoneName);
        Assert.True(note.Placement.IsPoint);
        Assert.Empty(plan.Bands);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void A_failing_CW_V_junction_draws_the_spandrel_band_and_annotates_it_instead_of_painting_panels()
    {
        var plan = Plan(JunctionRun(ReviewRunValidityTests.RunId, SpandrelJunction(ReviewRunValidityTests.RunId)), new Model());

        Assert.Empty(plan.Overrides);
        var band = Assert.Single(plan.Bands);
        Assert.Equal(ReviewMarkKind.SpandrelBand, band.Key.Kind);
        Assert.Equal(CurtainWall, band.CurtainWallUniqueId);
        Assert.Equal(12000, band.Placement.LengthMm, 3);
        Assert.Equal(1800, band.Placement.HeightMm, 3);

        var note = Assert.Single(plan.Notes);
        Assert.Equal("CW-V-01　層間帶連續具時效高度 600 mm／突出 0 mm（詳見立面 CW-V-01）", note.Text);
        Assert.Equal("CW-V-01", note.Number);
        Assert.Equal("CW-V-01", band.Number);
        Assert.Equal(band.Key.Subject, note.Key.Subject);
        Assert.NotEqual(band.Key.Slot, note.Key.Slot);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void A_failing_CW_O_junction_paints_its_panels_and_has_nothing_to_annotate()
    {
        var plan = Plan(JunctionRun(ReviewRunValidityTests.RunId,
            Junction(ReviewRunValidityTests.RunId, CurtainWallJunctionKind.CurtainPanelOther, "CW-O:cw-1",
                new[] { "panel-9" })), new Model());

        Assert.Equal(new[] { "panel-9" }, plan.Overrides.Select(o => o.ElementUniqueId));
        Assert.Empty(plan.Notes);
        Assert.Empty(plan.Bands);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void A_measurement_the_run_never_took_is_annotated_as_未量得_rather_than_as_zero()
    {
        var plan = Plan(JunctionRun(ReviewRunValidityTests.RunId,
            Junction(ReviewRunValidityTests.RunId, CurtainWallJunctionKind.WallToCurtainWall, "CW-H:cw-1:wall-1",
                new[] { "panel-1" }, Crossing, runMeters: null, projectionMeters: 0.0, host: "wall-1")), new Model());

        Assert.Equal("CW-H-01　交接帶連續具時效長度 未量得／突出 0 mm", Assert.Single(plan.Notes).Text);
    }

    [Fact]
    public void A_junction_with_no_recorded_place_or_no_recorded_panels_is_skipped_with_a_reason()
    {
        var run = JunctionRun(ReviewRunValidityTests.RunId,
            Junction(ReviewRunValidityTests.RunId, CurtainWallJunctionKind.WallToCurtainWall, "CW-H:cw-1:wall-1",
                new[] { "panel-1" }, placement: null, host: "wall-1"),
            Junction(ReviewRunValidityTests.RunId, CurtainWallJunctionKind.CurtainPanelOther, "CW-O:cw-1"));

        var plan = Plan(run, new Model());

        // The panels of the junction that has them are still painted; only what is missing is skipped.
        Assert.Equal(new[] { "panel-1" }, plan.Overrides.Select(o => o.ElementUniqueId));
        Assert.Empty(plan.Notes);
        Assert.Equal(2, plan.Skipped.Count);
        Assert.Contains(plan.Skipped, s => s.Reason.Contains("位置"));
        Assert.Contains(plan.Skipped, s => s.Reason.Contains("帷幕嵌板"));
    }

    [Fact]
    public void Case19_re_running_the_same_package_takes_the_curtain_wall_marks_over_instead_of_drawing_more()
    {
        var model = new Model();
        var view = new FakeView();

        var first = view.Mark(Plan(JunctionRun(ReviewRunValidityTests.RunId,
            HorizontalJunction(ReviewRunValidityTests.RunId, "panel-1"),
            SpandrelJunction(ReviewRunValidityTests.RunId)), model));

        // One panel override, the CW-H note, the CW-V band and its note.
        Assert.Equal(4, first.Count(ReviewMarkAction.Create));
        Assert.Equal(3, view.Marks.Count);

        Assert.False(view.Mark(Plan(JunctionRun(ReviewRunValidityTests.RunId,
            HorizontalJunction(ReviewRunValidityTests.RunId, "panel-1"),
            SpandrelJunction(ReviewRunValidityTests.RunId)), model)).HasChanges);

        var next = view.Mark(Plan(JunctionRun(ReviewRunValidityTests.NextRunId,
            HorizontalJunction(ReviewRunValidityTests.NextRunId, "panel-1"),
            SpandrelJunction(ReviewRunValidityTests.NextRunId)), model));

        Assert.Equal(0, next.Count(ReviewMarkAction.Create));
        Assert.Equal(4, next.Count(ReviewMarkAction.Update));
        Assert.Equal(3, view.Marks.Count);
        Assert.All(view.Marks, m => Assert.Equal(ReviewRunValidityTests.NextRunId, m.Key.RunId));
    }

    [Fact]
    public void A_curtain_wall_mark_and_a_zone_region_never_take_each_other_over()
    {
        var model = new Model();
        var run = ReviewTableTests.ReviewAll(model, areaLimitM2: 50);
        var zones = Plan(run, model);
        var junctions = Plan(JunctionRun(ReviewRunValidityTests.RunId,
            SpandrelJunction(ReviewRunValidityTests.RunId)), model);

        // The zone run plans no band, so the band it finds is its own package's — and still not a
        // region it should have drawn. Each family is matched among its own.
        var region = zones.Regions.First();
        var band = junctions.Bands.Single();
        var diff = ReviewMarkupDiff.Compute(zones, new[]
        {
            new ExistingReviewMark("r-1", region.Key.ToToken(), region.Signature),
            new ExistingReviewMark("b-1", band.Key.ToToken(), band.Signature)
        }, Array.Empty<RecordedElementOverride>());

        Assert.Equal(ReviewMarkAction.Unchanged, diff.Regions.Single(r => r.Planned == region).Action);
        Assert.DoesNotContain(diff.Regions, r => r.Existing?.ElementUniqueId == "b-1");
        Assert.Equal("b-1", Assert.Single(diff.Bands).Existing!.ElementUniqueId);
        Assert.Equal(ReviewMarkAction.Remove, diff.Bands.Single().Action);
        Assert.Equal(0, diff.ForeignMarks);
    }

    [Fact]
    public void A_curtain_wall_mark_key_names_what_it_belongs_to_and_the_older_zone_form_still_reads()
    {
        var key = new ReviewMarkKey(PackageId, ReviewRunValidityTests.RunId, ZoneA, 0,
            ReviewMarkKind.SpandrelBand, "CW-V:cw-1:floor-1:0");

        Assert.True(ReviewMarkKey.TryParse(key.ToToken(), out var parsed));
        Assert.Equal(key, parsed);
        Assert.Equal(7, key.ToToken().Split('/').Length);
        Assert.Contains("SpandrelBand", key.ToLabel());
        Assert.True(ManagedOwnership.BelongsTo(key.ToToken(), PackageId));

        var zone = new ReviewMarkKey(PackageId, ReviewRunValidityTests.RunId, ZoneA, 0);
        Assert.Equal(5, zone.ToToken().Split('/').Length);
        Assert.Equal(ReviewMarkKind.Zone, zone.Kind);
        Assert.NotEqual(zone.Slot, key.Slot);
        Assert.True(ReviewMarkKey.TryParse(zone.ToToken(), out var legacy) && legacy.Kind == ReviewMarkKind.Zone);

        Assert.Throws<ArgumentException>(() =>
            new ReviewMarkKey(PackageId, ReviewRunValidityTests.RunId, ZoneA, 0, ReviewMarkKind.JunctionNote, "a/b"));
        Assert.Throws<ArgumentException>(() =>
            new ReviewMarkKey(PackageId, ReviewRunValidityTests.RunId, ZoneA, 0, ReviewMarkKind.JunctionNote, "  "));
        Assert.Throws<ArgumentException>(() =>
            new ReviewMarkKey(PackageId, ReviewRunValidityTests.RunId, ZoneA, 0, ReviewMarkKind.Zone, "x"));
    }

    // --- 未符合交接的檢討圖號（帷幕牆規格 §7.1）-------------------------------------------------------

    /// <summary>A run with two CW-H, two CW-V and one CW-O 未符合, deliberately out of junction order.</summary>
    private static ReviewRun NumberedRun(Guid runId) => JunctionRun(runId,
        Junction(runId, CurtainWallJunctionKind.FloorToCurtainWall, "CW-V:cw-1:floor-2:0",
            new[] { "panel-4" }, SpandrelBand, 0.6, 0.0, "floor-2"),
        Junction(runId, CurtainWallJunctionKind.WallToCurtainWall, "CW-H:cw-1:wall-2",
            new[] { "panel-2" }, Crossing, 0.45, 0.12, "wall-2"),
        Junction(runId, CurtainWallJunctionKind.FloorToCurtainWall, "CW-V:cw-1:floor-1:0",
            new[] { "panel-3" }, SpandrelBand, 0.6, 0.0, "floor-1"),
        Junction(runId, CurtainWallJunctionKind.WallToCurtainWall, "CW-H:cw-1:wall-1",
            new[] { "panel-1" }, Crossing, 0.45, 0.12, "wall-1"),
        Junction(runId, CurtainWallJunctionKind.CurtainPanelOther, "CW-O:cw-1", new[] { "panel-9" }));

    private static string? NumberOf(ReviewMarkupPlan plan, ReviewRun run, string junctionId) =>
        CurtainWallMarkNumbers.Of(plan.Numbers, run.Results
            .Single(r => r.Evidence.Find("junction.id")?.Text == junctionId).ResultId);

    [Fact]
    public void Each_failing_junction_gets_a_drawing_number_numbered_per_kind_and_in_junction_order()
    {
        var run = NumberedRun(ReviewRunValidityTests.RunId);
        var plan = Plan(run, new Model());

        // Listed out of order in the run; numbered by the junction's own ID, so the junctions of one
        // curtain wall stay together and the same run always numbers them the same way.
        Assert.Equal("CW-H-01", NumberOf(plan, run, "CW-H:cw-1:wall-1"));
        Assert.Equal("CW-H-02", NumberOf(plan, run, "CW-H:cw-1:wall-2"));
        Assert.Equal("CW-V-01", NumberOf(plan, run, "CW-V:cw-1:floor-1:0"));
        Assert.Equal("CW-V-02", NumberOf(plan, run, "CW-V:cw-1:floor-2:0"));

        // CW-O paints panels and writes no annotation, so it has nothing a number could appear on.
        Assert.Null(NumberOf(plan, run, "CW-O:cw-1"));

        Assert.Equal(new[] { "CW-H-01", "CW-H-02", "CW-V-01", "CW-V-02" },
            plan.Notes.Select(n => n.Number).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(new[] { "CW-V-01", "CW-V-02" },
            plan.Bands.Select(b => b.Number).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void The_number_the_table_shows_is_the_number_the_marks_carry()
    {
        var run = NumberedRun(ReviewRunValidityTests.RunId);
        var plan = Plan(run, new Model());

        // The window numbers straight from the table, without building a plan. Both have to agree, or
        // a 未符合 row would send the reader to a drawing that is not the one it was judged on.
        var fromTable = CurtainWallMarkNumbers.Assign(ReviewTable.Build(run, null));

        Assert.Equal(plan.Numbers.OrderBy(p => p.Key), fromTable.OrderBy(p => p.Key));
        foreach (var note in plan.Notes) Assert.Equal(note.Number, CurtainWallMarkNumbers.Of(fromTable, note.ResultId));
        foreach (var band in plan.Bands) Assert.Equal(band.Number, CurtainWallMarkNumbers.Of(fromTable, band.ResultId));

        // The number is what the note says and what the mark's description is filed under.
        foreach (var note in plan.Notes) Assert.StartsWith(note.Number, note.Text, StringComparison.Ordinal);
        foreach (var band in plan.Bands) Assert.StartsWith(band.Number, band.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_failing_junction_that_is_still_current_is_numbered()
    {
        var run = JunctionRun(ReviewRunValidityTests.RunId,
            HorizontalJunction(ReviewRunValidityTests.RunId, "panel-1"),
            Junction(ReviewRunValidityTests.RunId, CurtainWallJunctionKind.WallToCurtainWall, "CW-H:cw-1:wall-9",
                new[] { "panel-9" }, Crossing, 0.45, 0.12, "wall-9", ReviewStatus.Pass));

        var numbers = CurtainWallMarkNumbers.Assign(ReviewTable.Build(run, null));

        // A pass is not a 未符合, so it neither takes a number nor pushes the next one along.
        Assert.Equal("CW-H-01", Assert.Single(numbers).Value);
    }

    [Fact]
    public void Two_spandrels_of_one_wall_differ_by_their_number_alone_so_a_renumber_is_a_change()
    {
        var plan = Plan(NumberedRun(ReviewRunValidityTests.RunId), new Model());
        var bands = plan.Bands.OrderBy(b => b.Number, StringComparer.Ordinal).ToList();

        // Same wall, same 層間帶 rectangle: without the number in the signature a renumbered band would
        // read as unchanged, and the elevation's name would go on pointing at the old number.
        Assert.Equal(bands[0].CurtainWallUniqueId, bands[1].CurtainWallUniqueId);
        Assert.Equal(bands[0].Placement.LengthMm, bands[1].Placement.LengthMm, 3);
        Assert.NotEqual(bands[0].Signature, bands[1].Signature);
    }

    [Fact]
    public void A_generated_curtain_wall_elevation_is_named_after_the_numbers_it_shows()
    {
        Assert.Equal("防火_1F_防火檢討_CW-V-01_CW-A_帷幕牆立面",
            ReviewOutputNaming.CurtainWallElevation("防火_1F_防火檢討", "CW-A", "CW-V-01"));

        // No number to give (an older caller, or a wall whose bands were all skipped): the name it
        // always had, so nothing that exists in a model stops being recognisable.
        Assert.Equal("防火_1F_防火檢討_CW-A_帷幕牆立面",
            ReviewOutputNaming.CurtainWallElevation("防火_1F_防火檢討", "CW-A"));

        // Up to three numbers are named; past that the name says how many instead of listing them.
        Assert.Equal("CW-V-01", CurtainWallMarkNumbers.Join(new[] { "CW-V-01" }));
        Assert.Equal("CW-V-01、CW-V-02、CW-V-03",
            CurtainWallMarkNumbers.Join(new[] { "CW-V-03", "CW-V-01", "CW-V-02" }));
        Assert.Equal("CW-V-01等4處",
            CurtainWallMarkNumbers.Join(new[] { "CW-V-04", "CW-V-01", "CW-V-02", "CW-V-03" }));
        Assert.Equal(string.Empty, CurtainWallMarkNumbers.Join(Array.Empty<string>()));

        Assert.Equal("CW-V-01", CurtainWallMarkNumbers.Format(CurtainWallMarkNumbers.SpandrelPrefix, 1));
        Assert.Equal("CW-H-100", CurtainWallMarkNumbers.Format(CurtainWallMarkNumbers.HorizontalPrefix, 100));
        Assert.Null(CurtainWallMarkNumbers.Prefix(CurtainWallJunctionKind.CurtainPanelOther));
        Assert.Throws<ArgumentOutOfRangeException>(() => CurtainWallMarkNumbers.Format("CW-V", 0));
    }

    [Fact]
    public void The_review_view_has_its_own_default_name_and_container_mark()
    {
        Assert.Equal("防火_1F-東棟_防火檢討", ReviewOutputNaming.ReviewView("防火", "1F|東棟"));
        var token = new ManagedOutputKey(PackageId, ManagedOutputKind.ReviewView).ToToken();
        Assert.True(ManagedOutputKey.TryParse(token, out var key));
        Assert.Equal(ManagedOutputKind.ReviewView, key.Kind);
        Assert.Equal("防火檢討視圖", ManagedOutputKey.Describe(ManagedOutputKind.ReviewView));
        Assert.True(ManagedOwnership.BelongsTo(token, PackageId));
    }
}
