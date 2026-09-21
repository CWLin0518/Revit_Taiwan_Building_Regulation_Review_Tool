using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using BuildingRegulationReview.Core.Tests.Candidates;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Reviews;

/// <summary>
/// P3-T07: a run is stored with the evidence it was computed from, reads back whole, and goes stale
/// when the model or the rule set moves — only the results the change touched, or all of them when
/// something every result shares moved (spec 13.1). Overrides keep their audit trail and never
/// survive such a change unconfirmed (spec 11.8).
/// </summary>
public sealed class ReviewRunValidityTests
{
    internal const string RuleSetId = "tw-bcr-fire";
    internal const string Version = "2024.1";
    internal static readonly Guid RunId = Guid.Parse("77777777-0000-0000-0000-000000000001");
    internal static readonly Guid NextRunId = Guid.Parse("77777777-0000-0000-0000-000000000002");
    internal static readonly DateTime Started = new(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime Ended = Started.AddSeconds(20);
    private static readonly DateTime Later = Ended.AddHours(1);
    private static readonly RuleEvaluationContext Today = new(new DateTime(2025, 6, 1), "TW");

    private const string TypeA = "type-a";
    private const string TypeB = "type-b";

    // --- 固定模型：A 區 x 0–10、B 區 x 10–20；W-left 為 A 型，其餘 B 型；D-left 在 W-left 上 ------------

    internal sealed class Model
    {
        public List<ZoneObservation> Zones { get; set; } = CandidateModel.Zones().Take(2).ToList();
        public List<MemberObservation> Walls { get; set; } = new()
        {
            WallOf("W-left", 0, 0, 0, 10, TypeA),
            WallOf("W-shared", 10, 0, 10, 10, TypeB),
            WallOf("W-right", 20, 0, 20, 10, TypeB)
        };
        public List<OpeningObservation> Doors { get; set; } = new()
        {
            new OpeningObservation(Source("D-left"), CandidateCategory.Door, "W-left", P(0, 5), M(1), M(2.1), "type-fd", "FD1")
        };
        public Dictionary<string, double> Ratings { get; set; } = new() { [TypeA] = 60, [TypeB] = 60 };
        public string DoorProtection { get; set; } = "是";
        public string Phase { get; set; } = "新建";

        public CandidateSet Set() => CandidateResolver.Resolve(Observations(Zones, Walls, Doors));

        public FireResistanceInputs RatingInputs() => new(null, Ratings.Select(x =>
            new TypeFireRating(x.Key, ProvidedFireRating.Rated(x.Value), FireRatingParameters.Provided)));

        public OpeningProtectionInputs ProtectionInputs() => new(null, new[]
        {
            OpeningFireProtection.ForInstance("D-left", FireProtectionText.Parse(DoorProtection), FireProtectionParameters.Provided)
        });

        public ReviewEnvironment Environment() => ReviewEnvironment.Of(
            (ReviewEnvironment.AreaSchemeUniqueId, "scheme-fire"),
            (ReviewEnvironment.Phase, Phase),
            (ReviewEnvironment.DesignOption, null),
            (ReviewEnvironment.GeometryTolerance, "0.01"));

        public ReviewBaseline Baseline() =>
            ReviewBaselineBuilder.Build(Set(), Environment(), ratingInputs: RatingInputs(), protectionInputs: ProtectionInputs());
    }

    private static MemberObservation WallOf(string uid, double x0, double y0, double x1, double y1, string type) =>
        new(Source(uid), CandidateCategory.Wall, new[] { P(x0, y0), P(x1, y1) }, widthFeet: M(0.2),
            typeUniqueId: type, typeName: type, isStructural: true);

    private static Rule Rating(string version = "1") =>
        new("wall", version, RuleCategory.FireResistance, "測試條文 wall", new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("element.category == \"Walls\" && element.isCompartmentBoundary == true"),
            new RuleExpression("element.providedFireRating >= 60 min"),
            evidenceFields: new[] { "element.providedFireRating" });

    private static Rule Protection() =>
        new("door", "1", RuleCategory.OpeningProtection, "測試條文 door", new DateTime(2024, 1, 1), "TW", 10,
            new RuleExpression("opening.kind == \"Door\" && opening.hostIsCompartmentBoundary == true"),
            new RuleExpression("opening.providedFireProtection == \"是\""),
            evidenceFields: new[] { "opening.providedFireProtection" });

    /// <summary>Runs both element checks over the model, the way P3-T09 will, and stores the baseline with the run.</summary>
    internal static ReviewRun Review(Model model, Guid? runId = null, string version = Version, string ruleVersion = "1", int boundaryRevision = 1)
    {
        var id = runId ?? RunId;
        var compiled = RuleSetCompiler.Compile(new RuleSet(RuleSetId, version, "防火", new[] { Rating(ruleVersion), Protection() }));
        Assert.True(compiled.IsSuccess, compiled.Error.TechnicalDetail);
        var engine = new RuleEngine(compiled.Value);

        var prefix = id == RunId ? "1" : "2";
        var n = 0;
        Guid Next() => Guid.Parse($"{prefix}0000000-0000-0000-0000-{++n:D12}");

        var set = model.Set();
        var ratings = FireResistanceCheck.Review(set, model.RatingInputs(), engine, Today, id, Next);
        var protection = OpeningProtectionCheck.Review(set, model.ProtectionInputs(), engine, Today, id, Next);
        Assert.True(ratings.IsSuccess, ratings.Error.ToString());
        Assert.True(protection.IsSuccess, protection.Error.ToString());

        return new ReviewRun(id, PackageId, RuleSetId, version, boundaryRevision, Started)
            .Complete(ratings.Value.Results.Concat(protection.Value.Results), Ended, baseline: model.Baseline());
    }

    private static ReviewRunFreshness Check(ReviewRun run, Model now, string version = Version, int? boundaryRevision = 1) =>
        ReviewRunValidity.Evaluate(run, now.Baseline(), RuleSetId, version, boundaryRevision);

    private static ReviewResult ResultOf(ReviewRun run, string subject, Guid zone) =>
        run.Results.Single(r => r.SubjectUniqueIds.Contains(subject) && r.ZoneId == zone.ToString("D"));

    private static IEnumerable<string> SubjectsOf(ReviewRun run, IEnumerable<Guid> resultIds) =>
        resultIds.Select(id => run.Result(id)!).SelectMany(r => r.SubjectUniqueIds).Distinct().OrderBy(x => x, StringComparer.Ordinal);

    // --- 元素證據 ----------------------------------------------------------------------------------

    [Fact]
    public void The_fixture_reviews_walls_and_the_door_per_zone()
    {
        var run = Review(new Model());

        Assert.Equal(ReviewStatus.Pass, ResultOf(run, "W-left", ZoneA).Status);
        Assert.Equal(ReviewStatus.Pass, ResultOf(run, "W-shared", ZoneA).Status);
        Assert.Equal(ReviewStatus.Pass, ResultOf(run, "W-shared", ZoneB).Status);
        Assert.Equal(ReviewStatus.Pass, ResultOf(run, "D-left", ZoneA).Status);
        Assert.True(run.Baseline.IsRecorded);
        Assert.Contains(ReviewBaselineKeys.Zone(ZoneA), run.Baseline.SubjectIds);
        Assert.Contains("W-shared", run.Baseline.SubjectIds);
        Assert.Contains("area-a", run.Baseline.SubjectIds);
    }

    [Fact]
    public void Reading_the_same_model_again_gives_the_same_baseline_and_a_fresh_run()
    {
        var model = new Model();
        var run = Review(model);

        var again = model.Baseline();
        Assert.Equal(run.Baseline.ContextFingerprint, again.ContextFingerprint);
        Assert.Equal(run.Baseline.Subjects.OrderBy(x => x.Key), again.Subjects.OrderBy(x => x.Key));

        // Reading elements in a different order changes nothing.
        model.Walls.Reverse();
        var freshness = Check(run, model);
        Assert.False(freshness.IsStale);
        Assert.Empty(freshness.StaleResultIds);
        Assert.Equal("模型與規則版本與此次檢討一致，結果仍有效。", freshness.Message);
    }

    [Fact]
    public void A_changed_type_rating_invalidates_only_the_members_of_that_type()
    {
        var model = new Model();
        var run = Review(model);

        model.Ratings[TypeB] = 30;
        var freshness = Check(run, model);

        Assert.True(freshness.IsStale);
        Assert.False(freshness.InvalidatesAll);
        Assert.Equal(new[] { "W-right", "W-shared" }, freshness.ChangedSubjects);
        Assert.Equal(new[] { "W-right", "W-shared" }, SubjectsOf(run, freshness.StaleResultIds));
        Assert.False(freshness.IsResultStale(ResultOf(run, "W-left", ZoneA).ResultId));
        Assert.Contains("參與檢討的元素或區劃有 2 項已被修改", freshness.Reasons);
    }

    [Fact]
    public void A_moved_wall_invalidates_its_own_results()
    {
        var model = new Model();
        var run = Review(model);

        model.Walls[0] = WallOf("W-left", 0.3, 0, 0.3, 10, TypeA);
        var freshness = Check(run, model);

        Assert.Contains("W-left", freshness.ChangedSubjects);
        Assert.True(freshness.IsResultStale(ResultOf(run, "W-left", ZoneA).ResultId));
        Assert.False(freshness.IsResultStale(ResultOf(run, "W-shared", ZoneB).ResultId));
    }

    [Fact]
    public void A_changed_door_parameter_invalidates_the_door_result()
    {
        var model = new Model();
        var run = Review(model);

        model.DoorProtection = "否";
        var freshness = Check(run, model);

        Assert.Equal(new[] { "D-left" }, freshness.ChangedSubjects);
        Assert.Equal(new[] { ResultOf(run, "D-left", ZoneA).ResultId }, freshness.StaleResultIds);
    }

    [Fact]
    public void A_deleted_element_invalidates_its_results_and_a_new_one_makes_the_run_incomplete()
    {
        var model = new Model();
        var run = Review(model);

        model.Walls.RemoveAll(w => w.Source.ElementUniqueId == "W-right");
        var removed = Check(run, model);
        Assert.Equal(new[] { "W-right" }, removed.RemovedSubjects);
        Assert.Equal(new[] { "W-right" }, SubjectsOf(run, removed.StaleResultIds));
        Assert.Contains("參與檢討的元素或區劃有 1 項已不在模型中", removed.Reasons);

        model = new Model();
        model.Walls.Add(WallOf("W-new", 0, 10, 20, 10, TypeB));
        var added = Check(run, model);
        Assert.True(added.IsStale);
        Assert.Equal(new[] { "W-new" }, added.AddedSubjects);
        Assert.Empty(added.StaleResultIds);
        Assert.Contains("模型新增了 1 個此次檢討沒有涵蓋的元素或區劃", added.Reasons);
    }

    [Fact]
    public void A_changed_area_invalidates_every_result_in_that_zone_only()
    {
        var model = new Model();
        var run = Review(model);

        model.Zones[0] = new ZoneObservation(ZoneA, "A 區", new[]
        {
            new ZonePartObservation("area-a", new[] { Rect(0, 0, 10, 10) }, PlanUnits.SquareMetersToSquareFeet(99))
        });
        var freshness = Check(run, model);

        Assert.Contains(ReviewBaselineKeys.Zone(ZoneA), freshness.ChangedSubjects);
        Assert.Contains("area-a", freshness.ChangedSubjects);
        Assert.All(run.Results.Where(r => r.ZoneId == ZoneA.ToString("D")), r => Assert.True(freshness.IsResultStale(r.ResultId)));
        Assert.All(run.Results.Where(r => r.ZoneId == ZoneB.ToString("D")), r => Assert.False(freshness.IsResultStale(r.ResultId)));
    }

    // --- 全部失效 ----------------------------------------------------------------------------------

    [Fact]
    public void A_new_rule_set_version_invalidates_every_result()
    {
        var model = new Model();
        var run = Review(model);

        var freshness = Check(run, model, version: "2025.1");

        Assert.True(freshness.InvalidatesAll);
        Assert.Equal(run.Results.Select(r => r.ResultId), freshness.StaleResultIds);
        Assert.Contains("規則版本已從 2024.1 更新為 2025.1", freshness.Reasons);

        var otherSet = ReviewRunValidity.Evaluate(run, model.Baseline(), "tw-other", Version);
        Assert.Contains("規則集已從「tw-bcr-fire」改為「tw-other」", otherSet.Reasons);
    }

    [Fact]
    public void A_changed_phase_design_option_or_tolerance_invalidates_every_result()
    {
        var model = new Model();
        var run = Review(model);

        model.Phase = "既有";
        var freshness = Check(run, model);

        Assert.True(freshness.InvalidatesAll);
        Assert.Equal(run.Results.Count, freshness.StaleResultIds.Count);
        Assert.Contains(freshness.Reasons, r => r.Contains("Phase"));
    }

    [Fact]
    public void A_reapplied_boundary_invalidates_every_result()
    {
        var model = new Model();
        var run = Review(model);

        var freshness = Check(run, model, boundaryRevision: 2);

        Assert.True(freshness.InvalidatesAll);
        Assert.Contains("區劃邊界已重新套用（版次 1 → 2）", freshness.Reasons);
        Assert.False(Check(run, model, boundaryRevision: null).IsStale);
    }

    [Fact]
    public void A_run_without_evidence_or_not_completed_can_never_be_shown_to_hold()
    {
        var model = new Model();
        var run = Review(model);

        var withoutEvidence = new ReviewRun(run.RunId, run.PackageId, run.RuleSetId, run.RuleSetVersion,
            run.BoundaryRevision, run.StartedAtUtc, run.State, run.CompletedAtUtc, run.Results);
        var noEvidence = Check(withoutEvidence, model);
        Assert.True(noEvidence.InvalidatesAll);
        Assert.Contains("此次檢討沒有保存元素證據，無法確認結果仍符合模型", noEvidence.Reasons);

        var cancelled = new ReviewRun(RunId, PackageId, RuleSetId, Version, 1, Started)
            .Complete(Array.Empty<ReviewResult>(), Ended, ReviewRunState.Cancelled, model.Baseline());
        Assert.Contains("此次檢討沒有完成（Cancelled），結果不可沿用", Check(cancelled, model).Reasons);

        Assert.Throws<ArgumentException>(() => ReviewRunValidity.Evaluate(run, ReviewBaseline.None, RuleSetId, Version));
    }

    // --- 套件狀態與日誌 ------------------------------------------------------------------------------

    [Theory]
    [InlineData(ReviewPackageStatus.Reviewed, true, ReviewPackageStatus.Stale)]
    [InlineData(ReviewPackageStatus.Documented, true, ReviewPackageStatus.Stale)]
    [InlineData(ReviewPackageStatus.Ready, true, ReviewPackageStatus.Ready)]
    [InlineData(ReviewPackageStatus.Error, true, ReviewPackageStatus.Error)]
    [InlineData(ReviewPackageStatus.Reviewed, false, ReviewPackageStatus.Reviewed)]
    public void A_stale_run_marks_a_reviewed_package_stale(ReviewPackageStatus before, bool changeModel, ReviewPackageStatus expected)
    {
        var model = new Model();
        var run = Review(model);
        if (changeModel) model.DoorProtection = "否";
        var package = new ReviewPackage(PackageId, "plan", "level", "scheme", "area-plan", status: before, boundaryRevision: 1);

        var updated = ReviewRunValidity.ApplyTo(package, Check(run, model), Later);

        Assert.Equal(expected, updated.Status);
        Assert.Throws<ArgumentException>(() => ReviewRunValidity.ApplyTo(
            new ReviewPackage(Guid.NewGuid(), "plan", "level", "scheme"), Check(run, model)));
    }

    [Fact]
    public void The_verdict_is_logged_with_its_reasons_and_the_overrides_it_suspends()
    {
        var model = new Model();
        var run = Overridden(Review(model), "W-left");
        model.Ratings[TypeA] = 30;

        var log = ReviewRunValidity.Explain(Check(run, model), Later);

        Assert.All(log.Entries, e => Assert.Equal(ReviewStage.Review, e.Stage));
        Assert.Contains(log.Entries, e => e.Code == ReviewErrorCode.StatusStale && e.UserMessage.Contains("1 項已被修改"));
        Assert.Contains(log.Entries, e => e.Code == ReviewErrorCode.OverrideNeedsReconfirmation);
        Assert.Equal("開始檢討", ReviewLogEntry.StageText(ReviewStage.Review));
        Assert.True(ReviewErrorCode.IsKnown(ReviewErrorCode.OverrideRejected));
        Assert.True(ReviewErrorCode.IsKnown(ReviewErrorCode.OverrideNeedsReconfirmation));
    }

    // --- 人工覆寫 ----------------------------------------------------------------------------------

    internal static ReviewRun Overridden(ReviewRun run, string subject, ReviewStatus status = ReviewStatus.ManualReview)
    {
        var result = run.Results.First(r => r.SubjectUniqueIds.Contains(subject));
        var applied = ReviewOverrides.Apply(run, result.ResultId, status, "現場已確認為防火構造", "王建築師", Ended.AddMinutes(10),
            comment: "附現場照片");
        Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error.Message : null);
        return applied.Value;
    }

    [Fact]
    public void An_override_records_who_when_why_and_both_statuses()
    {
        var run = Review(new Model());
        var result = ResultOf(run, "W-left", ZoneA);

        var overridden = Overridden(run, "W-left");
        var entry = Assert.Single(overridden.Overrides);

        Assert.Equal(result.ResultId, entry.ResultId);
        Assert.Equal(ReviewStatus.Pass, entry.OriginalStatus);
        Assert.Equal(ReviewStatus.ManualReview, entry.OverriddenStatus);
        Assert.Equal("現場已確認為防火構造", entry.Reason);
        Assert.Equal("附現場照片", entry.Comment);
        Assert.Equal("王建築師", entry.OverriddenBy);
        Assert.Equal(Ended.AddMinutes(10), entry.OverriddenAtUtc);
        Assert.Equal(result.RuleVersion, entry.RuleVersion);
        Assert.Equal(ReviewBaselineBuilder.DependencyFingerprint(run.Baseline, result), entry.DependencyFingerprint);
        Assert.True(entry.IsActive);

        Assert.Equal(ReviewStatus.ManualReview, overridden.EffectiveStatus(result));
        Assert.Equal(ReviewStatus.Pass, overridden.Result(result.ResultId)!.Status);
    }

    [Fact]
    public void An_override_needs_a_reason_an_operator_and_a_real_change()
    {
        var run = Review(new Model());
        var result = ResultOf(run, "W-left", ZoneA);

        Result(ReviewOverrides.Apply(run, result.ResultId, ReviewStatus.Fail, " ", "王建築師", Later), "原因");
        Result(ReviewOverrides.Apply(run, result.ResultId, ReviewStatus.Fail, "原因", "", Later), "操作者");
        Result(ReviewOverrides.Apply(run, result.ResultId, ReviewStatus.Pass, "原因", "王建築師", Later), "不需要覆寫");
        Result(ReviewOverrides.Apply(run, result.ResultId, ReviewStatus.NotRun, "原因", "王建築師", Later), "必須選擇");
        Result(ReviewOverrides.Apply(run, Guid.NewGuid(), ReviewStatus.Fail, "原因", "王建築師", Later), "沒有這個結果");

        var running = new ReviewRun(RunId, PackageId, RuleSetId, Version, 1, Started);
        Result(ReviewOverrides.Apply(running, result.ResultId, ReviewStatus.Fail, "原因", "王建築師", Later), "已完成");

        static void Result(BuildingRegulationReview.Domain.Common.Result<ReviewRun> outcome, string message)
        {
            Assert.True(outcome.IsFailure);
            Assert.Equal(ReviewErrorCode.OverrideRejected, outcome.Error.Code);
            Assert.Contains(message, outcome.Error.Message);
        }
    }

    [Fact]
    public void A_stale_result_cannot_be_overridden()
    {
        var model = new Model();
        var run = Review(model);
        model.Ratings[TypeA] = 30;
        var freshness = Check(run, model);

        var outcome = ReviewOverrides.Apply(run, ResultOf(run, "W-left", ZoneA).ResultId, ReviewStatus.Fail,
            "原因", "王建築師", Later, freshness: freshness);

        Assert.True(outcome.IsFailure);
        Assert.Contains("已失效", outcome.Error.Message);
        Assert.True(ReviewOverrides.Apply(run, ResultOf(run, "W-shared", ZoneA).ResultId, ReviewStatus.Fail,
            "原因", "王建築師", Later, freshness: freshness).IsSuccess);
    }

    [Fact]
    public void Overriding_again_or_withdrawing_keeps_the_whole_audit_trail()
    {
        var run = Overridden(Review(new Model()), "W-left");
        var result = ResultOf(run, "W-left", ZoneA);
        var first = run.Overrides.Single();

        var again = ReviewOverrides.Apply(run, result.ResultId, ReviewStatus.Fail, "複核後認定不足", "李技師", Later).Value;
        Assert.Equal(2, again.Overrides.Count);
        Assert.Equal(ReviewOverrideStanding.Superseded, again.Overrides[0].Standing);
        Assert.Equal("已由 李技師 的新覆寫取代", again.Overrides[0].StandingReason);
        Assert.Equal(first.OverrideId, again.Overrides[1].PreviousOverrideId);
        Assert.Equal(ReviewStatus.Fail, again.EffectiveStatus(result));

        var withdrawn = ReviewOverrides.Withdraw(again, result.ResultId, "誤植", "李技師", Later.AddMinutes(1)).Value;
        Assert.Equal(2, withdrawn.Overrides.Count);
        Assert.All(withdrawn.Overrides, o => Assert.Equal(ReviewOverrideStanding.Superseded, o.Standing));
        Assert.StartsWith("已由 李技師 於", withdrawn.Overrides[1].StandingReason);
        Assert.Equal(ReviewStatus.Pass, withdrawn.EffectiveStatus(result));
        Assert.True(ReviewOverrides.Withdraw(withdrawn, result.ResultId, "誤植", "李技師", Later).IsFailure);
    }

    [Fact]
    public void A_model_change_suspends_the_override_until_it_is_reconfirmed()
    {
        var model = new Model();
        var run = Overridden(Review(model), "W-left");
        var result = ResultOf(run, "W-left", ZoneA);

        model.Walls[0] = WallOf("W-left", 0.05, 0, 0.05, 10, TypeA);
        var suspended = ReviewRunValidity.WithOverridesSuspended(Check(run, model));

        var entry = Assert.Single(suspended.Overrides);
        Assert.Equal(ReviewOverrideStanding.NeedsReconfirmation, entry.Standing);
        Assert.StartsWith("模型或規則版本在覆寫後已變更：", entry.StandingReason);
        Assert.Equal(ReviewStatus.Pass, suspended.EffectiveStatus(result));
        Assert.Same(run, ReviewRunValidity.WithOverridesSuspended(Check(run, new Model())));

        var confirmed = ReviewOverrides.Reconfirm(suspended, result.ResultId, "李技師", Later).Value;
        Assert.Equal(2, confirmed.Overrides.Count);
        Assert.Equal(ReviewOverrideStanding.Superseded, confirmed.Overrides[0].Standing);
        Assert.Equal("已由 李技師 重新確認", confirmed.Overrides[0].StandingReason);
        var renewed = confirmed.Overrides[1];
        Assert.True(renewed.IsActive);
        Assert.Equal("現場已確認為防火構造", renewed.Reason);
        Assert.Equal("李技師", renewed.OverriddenBy);
        Assert.Equal(ReviewStatus.ManualReview, confirmed.EffectiveStatus(result));

        Assert.True(ReviewOverrides.Reconfirm(confirmed, result.ResultId, "李技師", Later).IsFailure);
    }

    // --- 新的 Run 沿用覆寫 ---------------------------------------------------------------------------

    [Fact]
    public void An_unchanged_model_and_rule_keep_the_override_on_the_next_run()
    {
        var model = new Model();
        var previous = Overridden(Review(model), "W-left");
        var next = Review(model, NextRunId);

        var report = ReviewOverrides.CarryOver(previous, next);

        var carried = Assert.Single(report.Entries);
        Assert.Equal(OverrideCarryOverOutcome.Kept, carried.Outcome);
        var entry = Assert.Single(report.Run.Overrides);
        Assert.True(entry.IsActive);
        Assert.Equal(previous.Overrides[0].OverrideId, entry.PreviousOverrideId);
        Assert.Equal("王建築師", entry.OverriddenBy);
        Assert.Equal(ReviewStatus.ManualReview, report.Run.EffectiveStatus(ResultOf(report.Run, "W-left", ZoneA)));
    }

    [Fact]
    public void A_new_rule_version_brings_the_override_along_unconfirmed()
    {
        var model = new Model();
        var previous = Overridden(Review(model), "W-left");
        var next = Review(model, NextRunId, version: "2025.1", ruleVersion: "2");

        var report = ReviewOverrides.CarryOver(previous, next);

        var carried = Assert.Single(report.Entries);
        Assert.Equal(OverrideCarryOverOutcome.NeedsReconfirmation, carried.Outcome);
        Assert.Contains("規則 wall 已從 1 更新為 2", carried.Message);
        Assert.Equal(ReviewOverrideStanding.NeedsReconfirmation, report.Run.Overrides.Single().Standing);
        Assert.Equal(ReviewStatus.Pass, report.Run.EffectiveStatus(ResultOf(report.Run, "W-left", ZoneA)));
    }

    [Fact]
    public void Changed_evidence_or_a_changed_verdict_brings_the_override_along_unconfirmed()
    {
        var model = new Model();
        var previous = Overridden(Review(model), "W-left", ReviewStatus.NotApplicable);

        model.Ratings[TypeA] = 30;
        var report = ReviewOverrides.CarryOver(previous, Review(model, NextRunId));

        var carried = Assert.Single(report.Entries);
        Assert.Equal(OverrideCarryOverOutcome.NeedsReconfirmation, carried.Outcome);
        Assert.Contains("相關元素、參數或區劃在覆寫後已變更", carried.Message);
        Assert.Contains("計算結果已從「符合」變為「未符合」", carried.Message);
    }

    [Fact]
    public void An_override_is_dropped_when_its_result_is_gone_or_already_agrees()
    {
        var model = new Model();
        var previous = Overridden(Review(model), "W-right", ReviewStatus.Fail);

        model.Walls.RemoveAll(w => w.Source.ElementUniqueId == "W-right");
        var gone = ReviewOverrides.CarryOver(previous, Review(model, NextRunId));
        Assert.Equal(OverrideCarryOverOutcome.Dropped, Assert.Single(gone.Entries).Outcome);
        Assert.Empty(gone.Run.Overrides);

        model = new Model();
        model.Ratings[TypeB] = 30;
        var agrees = ReviewOverrides.CarryOver(previous, Review(model, NextRunId));
        Assert.Equal(OverrideCarryOverOutcome.Dropped, Assert.Single(agrees.Entries).Outcome);
        Assert.Contains("與覆寫相同", agrees.Entries[0].Message);
        Assert.Equal(1, agrees.Count(OverrideCarryOverOutcome.Dropped));
    }

    [Fact]
    public void Carrying_over_needs_two_runs_of_one_package()
    {
        var run = Review(new Model());

        Assert.Throws<ArgumentException>(() => ReviewOverrides.CarryOver(run, run));
        Assert.Throws<ArgumentException>(() => ReviewOverrides.CarryOver(run,
            new ReviewRun(NextRunId, PackageId, RuleSetId, Version, 1, Started)));
    }

    // --- 持久化：重開模型可讀 ------------------------------------------------------------------------

    [Fact]
    public void A_stored_run_reads_back_with_its_evidence_and_overrides_and_stays_fresh()
    {
        var model = new Model();
        var run = Overridden(Review(model), "W-left");
        run = ReviewOverrides.Apply(run, ResultOf(run, "W-left", ZoneA).ResultId, ReviewStatus.Fail, "複核", "李技師", Later).Value;

        var json = JsonSerializer.Serialize(ReviewRunStorageMapper.ToRecord(run));
        var restored = ReviewRunStorageMapper.FromRecord(JsonSerializer.Deserialize<ReviewRunStorageRecord>(json)!);

        Assert.Equal(ReviewRun.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal(run.Results.Select(r => (r.ResultId, r.Status)), restored.Results.Select(r => (r.ResultId, r.Status)));
        Assert.Equal(run.Baseline.ContextFingerprint, restored.Baseline.ContextFingerprint);
        Assert.Equal(run.Baseline.Subjects.OrderBy(x => x.Key), restored.Baseline.Subjects.OrderBy(x => x.Key));
        Assert.Equal(run.Overrides.Count, restored.Overrides.Count);
        foreach (var (a, b) in run.Overrides.Zip(restored.Overrides, (a, b) => (a, b)))
        {
            Assert.Equal(a.OverrideId, b.OverrideId);
            Assert.Equal(a.ResultId, b.ResultId);
            Assert.Equal(a.OriginalStatus, b.OriginalStatus);
            Assert.Equal(a.OverriddenStatus, b.OverriddenStatus);
            Assert.Equal(a.Reason, b.Reason);
            Assert.Equal(a.Comment, b.Comment);
            Assert.Equal(a.OverriddenBy, b.OverriddenBy);
            Assert.Equal(a.OverriddenAtUtc, b.OverriddenAtUtc);
            Assert.Equal(a.RuleVersion, b.RuleVersion);
            Assert.Equal(a.DependencyFingerprint, b.DependencyFingerprint);
            Assert.Equal(a.Standing, b.Standing);
            Assert.Equal(a.StandingReason, b.StandingReason);
            Assert.Equal(a.PreviousOverrideId, b.PreviousOverrideId);
        }

        Assert.False(Check(restored, model).IsStale);
        Assert.Equal(ReviewStatus.Fail, restored.EffectiveStatus(restored.Result(ResultOf(run, "W-left", ZoneA).ResultId)!));
        Assert.Contains("\"Standing\":\"Superseded\"", json);

        model.DoorProtection = "否";
        Assert.True(Check(restored, model).IsStale);
    }

    [Fact]
    public void A_first_release_record_reads_as_a_run_without_evidence()
    {
        var model = new Model();
        var record = ReviewRunStorageMapper.ToRecord(Overridden(Review(model), "W-left"));
        record.SchemaVersion = ReviewRunStorageMapper.LegacySchemaVersion;

        var legacy = ReviewRunStorageMapper.FromRecord(record);

        Assert.False(legacy.Baseline.IsRecorded);
        Assert.Empty(legacy.Overrides);
        Assert.True(Check(legacy, model).InvalidatesAll);
    }

    [Fact]
    public void A_stored_override_or_fingerprint_must_be_readable()
    {
        var run = Overridden(Review(new Model()), "W-left");

        var badStanding = ReviewRunStorageMapper.ToRecord(run);
        badStanding.Overrides[0].Standing = "1";
        Assert.Throws<InvalidOperationException>(() => ReviewRunStorageMapper.FromRecord(badStanding));

        var noReason = ReviewRunStorageMapper.ToRecord(run);
        noReason.Overrides[0].Reason = "";
        Assert.Throws<ArgumentException>(() => ReviewRunStorageMapper.FromRecord(noReason));

        var foreign = ReviewRunStorageMapper.ToRecord(run);
        foreign.Overrides[0].ResultId = Guid.NewGuid().ToString("D");
        Assert.Throws<ArgumentException>(() => ReviewRunStorageMapper.FromRecord(foreign));

        var twice = ReviewRunStorageMapper.ToRecord(run);
        twice.SubjectFingerprints.Add(new ReviewFingerprintRecord { SubjectId = twice.SubjectFingerprints[0].SubjectId, Fingerprint = "x" });
        Assert.Throws<ArgumentException>(() => ReviewRunStorageMapper.FromRecord(twice));

        var orphan = ReviewRunStorageMapper.ToRecord(run);
        orphan.ContextFingerprint = "";
        Assert.Throws<ArgumentException>(() => ReviewRunStorageMapper.FromRecord(orphan));
    }

    // --- Domain 不變式 ------------------------------------------------------------------------------

    [Fact]
    public void An_override_entry_guards_its_own_audit_fields()
    {
        ReviewOverride Make(ReviewStatus original = ReviewStatus.Pass, ReviewStatus overridden = ReviewStatus.Fail,
            string reason = "原因", string by = "王", ReviewOverrideStanding standing = ReviewOverrideStanding.Active,
            string? standingReason = null, Guid? previous = null) =>
            new(Guid.Parse("88888888-0000-0000-0000-000000000001"), Guid.NewGuid(), original, overridden, reason, null, by,
                Later, "1", null, standing, standingReason, previous);

        Assert.Throws<ArgumentException>(() => Make(overridden: ReviewStatus.Pass));
        Assert.Throws<ArgumentException>(() => Make(original: ReviewStatus.NotRun));
        Assert.Throws<ArgumentException>(() => Make(overridden: ReviewStatus.NotRun));
        Assert.Throws<ArgumentException>(() => Make(reason: " "));
        Assert.Throws<ArgumentException>(() => Make(by: ""));
        Assert.Throws<ArgumentException>(() => Make(standing: ReviewOverrideStanding.NeedsReconfirmation));
        Assert.Throws<ArgumentException>(() => Make(previous: Guid.Parse("88888888-0000-0000-0000-000000000001")));
        Assert.Throws<ArgumentException>(() => Make().WithStanding(ReviewOverrideStanding.Active, "x"));
        Assert.Null(Make(standingReason: "ignored").StandingReason);
    }

    [Fact]
    public void A_run_holds_overrides_only_for_its_own_completed_results_and_one_current_per_result()
    {
        var run = Overridden(Review(new Model()), "W-left");
        var entry = run.Overrides[0];

        Assert.Throws<ArgumentException>(() => run.WithOverrides(new[] { entry, entry }));
        var duplicate = new ReviewOverride(Guid.NewGuid(), entry.ResultId, entry.OriginalStatus, ReviewStatus.Fail, "r", null, "b",
            Later, "1", null);
        Assert.Throws<ArgumentException>(() => run.WithOverrides(new[] { entry, duplicate }));
        Assert.Throws<ArgumentException>(() => new ReviewRun(RunId, PackageId, RuleSetId, Version, 1, Started,
            ReviewRunState.Cancelled, Ended, run.Results, run.Baseline, run.Overrides));

        Assert.Throws<ArgumentException>(() => new ReviewBaseline(null, new[] { new KeyValuePair<string, string>("a", "b") }));
        Assert.Throws<ArgumentException>(() => new ReviewBaseline("ctx", new[] { new KeyValuePair<string, string>("a", " ") }));
        Assert.Throws<ArgumentException>(() => ReviewEnvironment.Of(("phase", "a"), ("phase", "b")));
        Assert.Equal("專案 Phase", ReviewEnvironment.Label(ReviewEnvironment.Phase));
    }
}
