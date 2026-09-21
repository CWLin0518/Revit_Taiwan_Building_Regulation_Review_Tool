using System;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Reviews;

public sealed class ReviewResultTests
{
    internal static readonly Guid RunId = Guid.Parse("11111111-2222-4333-8444-555555555555");
    internal static readonly Guid PackageId = Guid.Parse("7b6a1d2c-3e4f-4a5b-8c9d-0e1f2a3b4c5d");

    internal static ReviewResult Make(
        ReviewStatus status,
        ReviewValue? actual = null,
        ReviewValue? required = null,
        string? message = "原因",
        string[]? subjects = null,
        string? zoneId = "zone-1",
        string? reviewedBy = null,
        DateTime? reviewedAtUtc = null,
        Guid? resultId = null,
        Guid? runId = null,
        Guid? packageId = null) =>
        new ReviewResult(
            resultId ?? Guid.NewGuid(), runId ?? RunId, packageId ?? PackageId,
            "CompartmentArea", subjects ?? new[] { "area-1" }, zoneId, status,
            actual, required, "tw-bcr-79-area", "1", "第79條", message,
            new ReviewEvidence(new[] { new ReviewEvidenceItem("zone.area", ReviewValue.Quantity(1200, ReviewUnit.SquareMeter)) }),
            reviewedBy, reviewedAtUtc);

    private static readonly ReviewValue Actual = ReviewValue.Quantity(1600, ReviewUnit.SquareMeter);
    private static readonly ReviewValue Limit = ReviewValue.Quantity(1500, ReviewUnit.SquareMeter);

    [Fact]
    public void Six_states_each_have_their_own_label()
    {
        var statuses = Enum.GetValues(typeof(ReviewStatus)).Cast<ReviewStatus>().ToList();

        Assert.Equal(6, statuses.Count);
        Assert.Equal(
            new[] { "未檢討", "符合", "未符合", "資料不足", "不適用", "人工覆核" },
            statuses.Select(ReviewStatusText.Label));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReviewStatusText.Label((ReviewStatus)99));
    }

    [Theory]
    [InlineData(ReviewStatus.Pass)]
    [InlineData(ReviewStatus.Fail)]
    public void A_pass_or_a_fail_must_carry_both_values(ReviewStatus status)
    {
        Assert.Throws<ArgumentException>(() => Make(status, actual: null, required: Limit));
        Assert.Throws<ArgumentException>(() => Make(status, actual: Actual, required: null));

        var result = Make(status, Actual, Limit);
        Assert.Equal(status, result.Status);
        Assert.Equal(Actual, result.ActualValue);
        Assert.Equal(Limit, result.RequiredValue);
    }

    [Fact]
    public void Missing_data_is_its_own_state_not_a_failure()
    {
        // Spec 11.3 / 18.5: a zone whose area could not be read is 資料不足, and that is allowed to
        // exist without an actual value — a Fail in its place is refused above.
        var result = Make(ReviewStatus.InsufficientData, actual: null, required: Limit, message: "找不到 Revit Area");

        Assert.Equal(ReviewStatus.InsufficientData, result.Status);
        Assert.Null(result.ActualValue);
        Assert.Equal("資料不足", ReviewStatusText.Label(result.Status));
    }

    [Theory]
    [InlineData(ReviewStatus.Fail)]
    [InlineData(ReviewStatus.InsufficientData)]
    [InlineData(ReviewStatus.ManualReview)]
    public void Anything_short_of_a_pass_must_say_why(ReviewStatus status)
    {
        Assert.Throws<ArgumentException>(() => Make(status, Actual, Limit, message: " "));
        Assert.Throws<ArgumentException>(() => Make(status, Actual, Limit, message: null));
    }

    [Theory]
    [InlineData(ReviewStatus.Pass)]
    [InlineData(ReviewStatus.NotApplicable)]
    [InlineData(ReviewStatus.NotRun)]
    public void A_pass_not_applicable_or_not_run_may_go_without_a_message(ReviewStatus status)
    {
        var result = Make(status, Actual, Limit, message: null);

        Assert.Equal(string.Empty, result.Message);
    }

    [Fact]
    public void A_result_names_its_zone_or_its_elements_unless_it_never_ran()
    {
        Assert.Throws<ArgumentException>(() => Make(ReviewStatus.NotApplicable, subjects: new[] { " " }, zoneId: " "));

        Assert.Equal("zone-1", Make(ReviewStatus.NotApplicable, subjects: Array.Empty<string>()).ZoneId);
        Assert.Null(Make(ReviewStatus.NotApplicable, zoneId: null).ZoneId);
        Assert.Empty(Make(ReviewStatus.NotRun, subjects: Array.Empty<string>(), zoneId: null).SubjectUniqueIds);
    }

    [Fact]
    public void Subjects_are_trimmed_and_deduplicated()
    {
        var result = Make(ReviewStatus.NotApplicable, subjects: new[] { " wall-1 ", "wall-1", "", "door-2" });

        Assert.Equal(new[] { "wall-1", "door-2" }, result.SubjectUniqueIds);
    }

    [Fact]
    public void A_manual_review_records_who_and_when_together()
    {
        var when = new DateTime(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() => Make(ReviewStatus.ManualReview, reviewedBy: "覆核者"));
        Assert.Throws<ArgumentException>(() => Make(ReviewStatus.ManualReview, reviewedAtUtc: when));

        var result = Make(ReviewStatus.ManualReview, reviewedBy: " 覆核者 ", reviewedAtUtc: when);
        Assert.Equal("覆核者", result.ReviewedBy);
        Assert.Equal(when, result.ReviewedAtUtc);
    }

    [Fact]
    public void A_result_requires_its_ids_check_type_and_rule_trace()
    {
        Assert.Throws<ArgumentException>(() => Make(ReviewStatus.NotRun, resultId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Make(ReviewStatus.NotRun, runId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Make(ReviewStatus.NotRun, packageId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => new ReviewResult(Guid.NewGuid(), RunId, PackageId, " ", null, "z",
            ReviewStatus.NotRun, null, null, "r", "1", "第79條", null));
        Assert.Throws<ArgumentException>(() => new ReviewResult(Guid.NewGuid(), RunId, PackageId, "c", null, "z",
            ReviewStatus.NotRun, null, null, " ", "1", "第79條", null));
        Assert.Throws<ArgumentException>(() => new ReviewResult(Guid.NewGuid(), RunId, PackageId, "c", null, "z",
            ReviewStatus.NotRun, null, null, "r", " ", "第79條", null));
        Assert.Throws<ArgumentException>(() => new ReviewResult(Guid.NewGuid(), RunId, PackageId, "c", null, "z",
            ReviewStatus.NotRun, null, null, "r", "1", " ", null));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make((ReviewStatus)99));
    }

    [Fact]
    public void Values_are_finite_and_state_their_unit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReviewValue.Quantity(double.NaN, ReviewUnit.Minute));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReviewValue.Quantity(double.PositiveInfinity, ReviewUnit.Minute));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReviewValue.Quantity(1, (ReviewUnit)99));
        Assert.Throws<ArgumentNullException>(() => ReviewValue.OfText(null!));

        Assert.Equal(ReviewValue.Quantity(60, ReviewUnit.Minute), ReviewValue.Quantity(60, ReviewUnit.Minute));
        Assert.NotEqual(ReviewValue.Quantity(60, ReviewUnit.Minute), ReviewValue.Quantity(60, ReviewUnit.None));
        Assert.Equal(ReviewValue.OfText(" 是 "), ReviewValue.OfText("是"));
        Assert.NotEqual(ReviewValue.OfBoolean(true), ReviewValue.OfBoolean(false));
        Assert.Equal("1500.5 SquareMeter", ReviewValue.Quantity(1500.5, ReviewUnit.SquareMeter).ToString());
        Assert.Equal("3", ReviewValue.Quantity(3, ReviewUnit.None).ToString());
        Assert.Equal("是", ReviewValue.OfText("是").ToString());
        Assert.Equal("true", ReviewValue.OfBoolean(true).ToString());
    }

    [Fact]
    public void Evidence_keeps_order_and_refuses_a_field_twice()
    {
        var evidence = new ReviewEvidence(new[]
        {
            new ReviewEvidenceItem(" zone.area ", ReviewValue.Quantity(1200, ReviewUnit.SquareMeter)),
            new ReviewEvidenceItem("zone.sprinklered", ReviewValue.OfBoolean(false))
        });

        Assert.Equal(new[] { "zone.area", "zone.sprinklered" }, evidence.Items.Select(x => x.Field));
        Assert.Equal(ReviewValue.OfBoolean(false), evidence.Find(" zone.sprinklered "));
        Assert.True(evidence.Has("zone.area"));
        Assert.False(evidence.Has("zone.use"));
        Assert.Null(evidence.Find(null!));
        Assert.Equal(0, ReviewEvidence.Empty.Count);

        Assert.Throws<ArgumentException>(() => new ReviewEvidence(new[]
        {
            new ReviewEvidenceItem("zone.area", ReviewValue.OfBoolean(true)),
            new ReviewEvidenceItem("zone.area ", ReviewValue.OfBoolean(false))
        }));
        Assert.Throws<ArgumentException>(() => new ReviewEvidenceItem(" ", ReviewValue.OfBoolean(true)));
        Assert.Throws<ArgumentNullException>(() => new ReviewEvidenceItem("a", null!));
    }
}
