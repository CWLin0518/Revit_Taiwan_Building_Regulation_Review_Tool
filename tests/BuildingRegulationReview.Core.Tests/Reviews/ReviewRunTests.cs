using System;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Reviews;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Reviews;

public sealed class ReviewRunTests
{
    private static readonly Guid RunId = ReviewResultTests.RunId;
    private static readonly Guid PackageId = ReviewResultTests.PackageId;
    private static readonly DateTime Started = new DateTime(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ended = Started.AddSeconds(12);

    private static ReviewRun Running() => new ReviewRun(RunId, PackageId, " tw-bcr-fire ", " 2024.1 ", 3, Started);

    // One result in each of the six states, the kind of mix a real run produces.
    private static ReviewResult[] EverySixStates() => new[]
    {
        ReviewResultTests.Make(ReviewStatus.NotRun, message: null, subjects: Array.Empty<string>(), zoneId: null),
        ReviewResultTests.Make(ReviewStatus.Pass,
            ReviewValue.Quantity(1200.25, ReviewUnit.SquareMeter), ReviewValue.Quantity(1500, ReviewUnit.SquareMeter), message: null),
        ReviewResultTests.Make(ReviewStatus.Fail,
            ReviewValue.Quantity(30, ReviewUnit.Minute), ReviewValue.Quantity(60, ReviewUnit.Minute),
            message: "提供 30 分鐘，低於要求 60 分鐘", subjects: new[] { "wall-1", "wall-2" }, zoneId: null),
        ReviewResultTests.Make(ReviewStatus.InsufficientData,
            null, ReviewValue.OfText("是"), message: "ProvidedFireProtection 未設定", subjects: new[] { "door-1" }),
        ReviewResultTests.Make(ReviewStatus.NotApplicable, ReviewValue.OfBoolean(false), null, message: null),
        ReviewResultTests.Make(ReviewStatus.ManualReview, message: "複合構造無法判定",
            reviewedBy: "覆核者", reviewedAtUtc: Ended.AddMinutes(5))
    };

    [Fact]
    public void A_new_run_is_running_and_pins_rule_set_and_boundary_revision()
    {
        var run = Running();

        Assert.Equal(ReviewRunState.Running, run.State);
        Assert.Null(run.CompletedAtUtc);
        Assert.Empty(run.Results);
        Assert.Equal("tw-bcr-fire", run.RuleSetId);
        Assert.Equal("2024.1", run.RuleSetVersion);
        Assert.Equal(3, run.BoundaryRevision);
        Assert.Equal(ReviewRun.CurrentSchemaVersion, run.SchemaVersion);
    }

    [Fact]
    public void Completing_a_run_attaches_results_once()
    {
        var done = Running().Complete(EverySixStates(), Ended);

        Assert.Equal(ReviewRunState.Completed, done.State);
        Assert.Equal(Ended, done.CompletedAtUtc);
        Assert.Equal(6, done.Results.Count);
        Assert.Single(done.WithStatus(ReviewStatus.Fail));
        Assert.Throws<InvalidOperationException>(() => done.Complete(Array.Empty<ReviewResult>(), Ended));
        Assert.Throws<ArgumentOutOfRangeException>(() => Running().Complete(Array.Empty<ReviewResult>(), Ended, ReviewRunState.Running));

        var cancelled = Running().Complete(Array.Empty<ReviewResult>(), Ended, ReviewRunState.Cancelled);
        Assert.Equal(ReviewRunState.Cancelled, cancelled.State);
    }

    [Fact]
    public void Run_timing_must_agree_with_its_state()
    {
        Assert.Throws<ArgumentException>(() =>
            new ReviewRun(RunId, PackageId, "set", "1", 0, Started, ReviewRunState.Running, Ended));
        Assert.Throws<ArgumentException>(() =>
            new ReviewRun(RunId, PackageId, "set", "1", 0, Started, ReviewRunState.Completed));
        Assert.Throws<ArgumentException>(() =>
            new ReviewRun(RunId, PackageId, "set", "1", 0, Started, ReviewRunState.Failed, Started.AddSeconds(-1)));
    }

    [Fact]
    public void Run_requires_identity_rule_set_and_a_valid_revision()
    {
        Assert.Throws<ArgumentException>(() => new ReviewRun(Guid.Empty, PackageId, "set", "1", 0, Started));
        Assert.Throws<ArgumentException>(() => new ReviewRun(RunId, Guid.Empty, "set", "1", 0, Started));
        Assert.Throws<ArgumentException>(() => new ReviewRun(RunId, PackageId, " ", "1", 0, Started));
        Assert.Throws<ArgumentException>(() => new ReviewRun(RunId, PackageId, "set", " ", 0, Started));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReviewRun(RunId, PackageId, "set", "1", -1, Started));
    }

    [Fact]
    public void A_run_only_holds_its_own_unique_results()
    {
        var foreignRun = ReviewResultTests.Make(ReviewStatus.NotApplicable, runId: Guid.NewGuid());
        var foreignPackage = ReviewResultTests.Make(ReviewStatus.NotApplicable, packageId: Guid.NewGuid());
        var id = Guid.NewGuid();
        var twice = new[]
        {
            ReviewResultTests.Make(ReviewStatus.NotApplicable, resultId: id),
            ReviewResultTests.Make(ReviewStatus.NotApplicable, resultId: id)
        };

        Assert.Throws<ArgumentException>(() => Running().Complete(new[] { foreignRun }, Ended));
        Assert.Throws<ArgumentException>(() => Running().Complete(new[] { foreignPackage }, Ended));
        Assert.Throws<ArgumentException>(() => Running().Complete(twice, Ended));
    }

    [Fact]
    public void Stored_run_survives_a_json_round_trip_in_all_six_states()
    {
        var original = Running().Complete(EverySixStates(), Ended);

        var json = JsonSerializer.Serialize(ReviewRunStorageMapper.ToRecord(original));
        var restored = ReviewRunStorageMapper.FromRecord(JsonSerializer.Deserialize<ReviewRunStorageRecord>(json)!);

        Assert.Equal(original.RunId, restored.RunId);
        Assert.Equal(original.PackageId, restored.PackageId);
        Assert.Equal(original.RuleSetId, restored.RuleSetId);
        Assert.Equal(original.RuleSetVersion, restored.RuleSetVersion);
        Assert.Equal(original.BoundaryRevision, restored.BoundaryRevision);
        Assert.Equal(original.StartedAtUtc, restored.StartedAtUtc);
        Assert.Equal(DateTimeKind.Utc, restored.StartedAtUtc.Kind);
        Assert.Equal(original.State, restored.State);
        Assert.Equal(original.CompletedAtUtc, restored.CompletedAtUtc);
        Assert.Equal(original.Results.Count, restored.Results.Count);

        foreach (var (a, b) in original.Results.Zip(restored.Results, (a, b) => (a, b)))
        {
            Assert.Equal(a.ResultId, b.ResultId);
            Assert.Equal(a.CheckType, b.CheckType);
            Assert.Equal(a.SubjectUniqueIds, b.SubjectUniqueIds);
            Assert.Equal(a.ZoneId, b.ZoneId);
            Assert.Equal(a.Status, b.Status);
            Assert.Equal(a.ActualValue, b.ActualValue);
            Assert.Equal(a.RequiredValue, b.RequiredValue);
            Assert.Equal(a.RuleId, b.RuleId);
            Assert.Equal(a.RuleVersion, b.RuleVersion);
            Assert.Equal(a.LegalReference, b.LegalReference);
            Assert.Equal(a.Message, b.Message);
            Assert.Equal(a.Evidence.Items.Select(x => (x.Field, x.Value)), b.Evidence.Items.Select(x => (x.Field, x.Value)));
            Assert.Equal(a.ReviewedBy, b.ReviewedBy);
            Assert.Equal(a.ReviewedAtUtc, b.ReviewedAtUtc);
        }

        Assert.Contains("\"Status\":\"InsufficientData\"", json);
        Assert.Contains("\"State\":\"Completed\"", json);
    }

    [Fact]
    public void A_running_run_round_trips_without_a_completion_time()
    {
        var restored = ReviewRunStorageMapper.FromRecord(ReviewRunStorageMapper.ToRecord(Running()));

        Assert.Equal(ReviewRunState.Running, restored.State);
        Assert.Null(restored.CompletedAtUtc);
    }

    [Fact]
    public void An_unknown_schema_version_is_refused()
    {
        var record = ReviewRunStorageMapper.ToRecord(Running());
        record.SchemaVersion = "2.0";

        Assert.Throws<NotSupportedException>(() => ReviewRunStorageMapper.FromRecord(record));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("fail")]
    [InlineData("")]
    public void A_stored_status_must_be_one_of_the_six_names(string stored)
    {
        var record = ReviewRunStorageMapper.ToRecord(Running().Complete(EverySixStates(), Ended));
        record.Results[1].Status = stored;

        Assert.Throws<InvalidOperationException>(() => ReviewRunStorageMapper.FromRecord(record));
    }

    [Fact]
    public void A_stored_record_cannot_smuggle_in_a_fail_without_a_required_value()
    {
        var record = ReviewRunStorageMapper.ToRecord(Running().Complete(EverySixStates(), Ended));
        record.Results[2].RequiredValue = null;

        Assert.Throws<ArgumentException>(() => ReviewRunStorageMapper.FromRecord(record));
    }

    [Fact]
    public void Stored_ids_units_and_evidence_must_be_readable()
    {
        var badId = ReviewRunStorageMapper.ToRecord(Running());
        badId.RunId = "not-a-guid";
        Assert.Throws<InvalidOperationException>(() => ReviewRunStorageMapper.FromRecord(badId));

        var badUnit = ReviewRunStorageMapper.ToRecord(Running().Complete(EverySixStates(), Ended));
        badUnit.Results[1].ActualValue!.Unit = "m2";
        Assert.Throws<InvalidOperationException>(() => ReviewRunStorageMapper.FromRecord(badUnit));

        var badEvidence = ReviewRunStorageMapper.ToRecord(Running().Complete(EverySixStates(), Ended));
        badEvidence.Results[0].Evidence.Add(null!);
        Assert.Throws<InvalidOperationException>(() => ReviewRunStorageMapper.FromRecord(badEvidence));
    }
}
