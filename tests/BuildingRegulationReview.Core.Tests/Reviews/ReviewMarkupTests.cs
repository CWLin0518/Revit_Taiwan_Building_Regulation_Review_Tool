using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.WriteBack;
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
