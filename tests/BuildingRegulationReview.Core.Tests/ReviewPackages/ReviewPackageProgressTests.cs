using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;
using BuildingRegulationReview.Domain.ReviewPackages;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.ReviewPackages;

/// <summary>
/// Spec 10.6's third acceptance item — 面積與 Revit Area 的差異超過容許值時，禁止進入 Ready — and
/// spec 13's rule about what a write-back does to the package's own state.
/// </summary>
public class ReviewPackageProgressTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ZoneId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime At = new DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ARunThatWroteEverythingAndAgreesWithRevitReachesReady()
    {
        var outcome = ReviewPackageProgress.After(Package(), Run(agreeing: true), At);

        Assert.True(outcome.IsReady);
        Assert.Equal(ReviewPackageStatus.Ready, outcome.Status);
        Assert.Empty(outcome.Blockers);
        Assert.True(outcome.Changed);
    }

    [Fact]
    public void AnAreaThatDisagreesWithTheDraftKeepsThePackageOutOfReady()
    {
        var outcome = ReviewPackageProgress.After(Package(), Run(agreeing: false), At);

        Assert.False(outcome.IsReady);
        Assert.Equal(ReviewPackageStatus.BoundaryDraft, outcome.Status);
        Assert.Contains(outcome.Blockers, b => b.Contains("相差", StringComparison.Ordinal));
        Assert.Contains("尚不得進入", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAreaRevitMeasuredAsZeroIsJustAsMuchOfARefusal()
    {
        var plan = Plan();
        var builder = new ApplyResult.Builder(plan);
        foreach (var step in plan.Steps) builder.Created(step, "new-" + step.Key.ToToken());
        builder.Area(AreaAgreement.Compare(AreaKey(), "A", 92.9, 0.0));

        var outcome = ReviewPackageProgress.After(Package(), builder.Complete(), At);

        Assert.False(outcome.IsReady);
        Assert.Contains(outcome.Blockers, b => b.Contains("沒有落在封閉的邊界內", StringComparison.Ordinal));
    }

    [Fact]
    public void AnElementRevitRefusedAlsoKeepsThePackageOutOfReady()
    {
        var plan = Plan();
        var builder = new ApplyResult.Builder(plan);
        builder.Failed(plan.Steps[0], "邊界線太短。");
        foreach (var step in plan.Steps.Skip(1)) builder.Created(step, "new-" + step.Key.ToToken());
        builder.Area(AreaAgreement.Compare(AreaKey(), "A", 92.9, 92.9));

        var outcome = ReviewPackageProgress.After(Package(), builder.Complete(), At);

        Assert.Equal(ReviewPackageStatus.BoundaryDraft, outcome.Status);
        Assert.Contains(outcome.Blockers, b => b.Contains("沒有寫入模型", StringComparison.Ordinal));
    }

    [Fact]
    public void AColourTheUserHasToSetByHandIsNotAReasonToRefuseReady()
    {
        // A colour is cosmetic: the boundaries fence what they should and the Areas measure what
        // they should, which is everything spec 10.6 asks about.
        var plan = Plan();
        var builder = new ApplyResult.Builder(plan);
        foreach (var step in plan.Steps) builder.Created(step, "new-" + step.Key.ToToken());
        builder.Area(AreaAgreement.Compare(AreaKey(), "A", 92.9, 92.9));
        builder.Manual("面積色彩配置", "這個專案沒有可複製的色彩配置", "請先新增一個色彩配置");

        Assert.True(ReviewPackageProgress.After(Package(), builder.Complete(), At).IsReady);
    }

    [Fact]
    public void ARolledBackRunLeavesThePackageExactlyWhereItWas()
    {
        var package = Package(ReviewPackageStatus.Ready, boundaryRevision: 3);

        var outcome = ReviewPackageProgress.After(
            package,
            new ApplyResult.Builder(Plan()).RolledBack("Revit 無法提交。"),
            At);

        Assert.False(outcome.Changed);
        Assert.Equal(ReviewPackageStatus.Ready, outcome.Status);
        Assert.Equal(3, outcome.BoundaryRevision);
        Assert.Same(package, outcome.Package);
        Assert.Contains("維持", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunWithNothingToDoLearnsNothingAndSoDecidesNothing()
    {
        // The model already matched the drafts, so no Area was re-measured. Promoting the package on
        // the strength of a run that checked nothing would be a claim nobody made.
        var package = Package(ReviewPackageStatus.BoundaryDraft, boundaryRevision: 2);

        var outcome = ReviewPackageProgress.After(package, ApplyResult.Nothing(PackageId), At);

        Assert.False(outcome.Changed);
        Assert.Equal(ReviewPackageStatus.BoundaryDraft, outcome.Status);
        Assert.Equal(2, outcome.BoundaryRevision);
    }

    [Fact]
    public void ARunThatChangedTheModelAdvancesTheBoundaryRevision()
    {
        var outcome = ReviewPackageProgress.After(Package(boundaryRevision: 4), Run(agreeing: true), At);

        Assert.Equal(5, outcome.BoundaryRevision);
        Assert.Equal(4, outcome.PreviousBoundaryRevision);
    }

    [Fact]
    public void ABoundaryThatChangedInvalidatesWhateverWasReviewedOnTopOfIt()
    {
        // Spec 13.1: the review was about the old boundary, so the package cannot stay Reviewed.
        var outcome = ReviewPackageProgress.After(
            Package(ReviewPackageStatus.Reviewed, boundaryRevision: 1),
            Run(agreeing: true),
            At);

        Assert.Equal(ReviewPackageStatus.Ready, outcome.Status);
        Assert.Equal(ReviewPackageStatus.Reviewed, outcome.PreviousStatus);
        Assert.Equal(2, outcome.BoundaryRevision);
    }

    [Fact]
    public void ADocumentedPackageWhoseBoundaryWasRewrittenAndDisagreesFallsAllTheWayBack()
    {
        var outcome = ReviewPackageProgress.After(
            Package(ReviewPackageStatus.Documented, boundaryRevision: 7),
            Run(agreeing: false),
            At);

        Assert.Equal(ReviewPackageStatus.BoundaryDraft, outcome.Status);
        Assert.Equal(8, outcome.BoundaryRevision);
    }

    [Fact]
    public void RefusesAResultThatBelongsToAnotherPackage()
    {
        var other = new ReviewPackage(
            Guid.Parse("22222222-3333-4444-5555-666666666666"), "plan", "level", "scheme", "areaplan");

        Assert.Throws<ArgumentException>(() => ReviewPackageProgress.After(other, Run(agreeing: true), At));
    }

    [Fact]
    public void TheStateChangeIsOnTheLogWithTheOldAndNewStatusInTheTechnicalDetail()
    {
        var outcome = ReviewPackageProgress.After(Package(), Run(agreeing: false), At);

        var log = ReviewPackageProgress.Explain(outcome, At);
        var summary = log.Entries.Last();

        Assert.True(log.HasErrors);
        Assert.Equal(ReviewErrorCode.StatusBlocked, summary.Code);
        Assert.Equal(ReviewStage.Status, summary.Stage);
        Assert.Contains("Setup -> BoundaryDraft", summary.TechnicalDetail!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWholeRunReportCarriesTheResultTheStateAndTheLogTogether()
    {
        var report = ReviewRunReport.For(Package(), Run(agreeing: false), At);

        Assert.Equal(PackageId, report.PackageId);
        Assert.False(report.IsReady);
        Assert.True(report.PackageChanged);
        Assert.NotEmpty(report.Blockers);
        Assert.Contains(report.Log.Entries, e => e.Stage == ReviewStage.WriteBack);
        Assert.Contains(report.Log.Entries, e => e.Stage == ReviewStage.Status);
        Assert.Contains("尚不得進入", report.Summary, StringComparison.Ordinal);
        Assert.NotEmpty(report.UserProblems);
    }

    [Fact]
    public void EveryStatusHasAChineseNameForTheUser()
    {
        foreach (ReviewPackageStatus status in Enum.GetValues(typeof(ReviewPackageStatus)))
        {
            Assert.False(string.IsNullOrWhiteSpace(ReviewPackageProgress.Describe(status)));
        }
    }

    [Fact]
    public void ABoundaryRevisionOnlyEverMovesForward()
    {
        var package = Package(boundaryRevision: 5);

        Assert.Throws<ArgumentOutOfRangeException>(() => package.WithProgress(ReviewPackageStatus.Ready, 4));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static ReviewPackage Package(
        ReviewPackageStatus status = ReviewPackageStatus.Setup,
        int boundaryRevision = 0) =>
        new ReviewPackage(
            PackageId, "floor-plan", "level-1", "area-scheme", "area-plan",
            boundaryRevision: boundaryRevision, status: status);

    /// <summary>A run that wrote the whole plan, with an Area that either agrees or does not.</summary>
    private static ApplyResult Run(bool agreeing)
    {
        var plan = Plan();
        var builder = new ApplyResult.Builder(plan);
        foreach (var step in plan.Steps) builder.Created(step, "new-" + step.Key.ToToken());
        builder.Area(AreaAgreement.Compare(AreaKey(), "A", 92.9, agreeing ? 92.9 : 70.0));
        return builder.Complete();
    }

    private static ManagedElementKey AreaKey() =>
        new ManagedElementKey(PackageId, ZoneId, ManagedElementKind.Area, 0, 0);

    private static ApplyPlan Plan()
    {
        var map = Solve(Rectangle(0, 0, 10, 10, "OUTER"));
        var zones = Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            ZoneId, "A", ZoneColorPalette.At(0), new[] { map.FaceAt(new Point2D(5, 5))!.Id })));
        return ApplyPlan.Build(ApplyPreview.Build(PackageId, map, zones), ApplyPlan.AllKinds);
    }

    private static PlanRegionMap Solve(IEnumerable<Segment2D> segments)
    {
        var snapshot = new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance);
        var network = Succeeds(new LineNetworkRepairer().Repair(snapshot, SolvedAt));
        return Succeeds(new RegionSolver().Solve(network, SolvedAt));
    }

    private static Segment2D[] Rectangle(double x, double y, double width, double height, string prefix) => new[]
    {
        Seg(x, y, x + width, y, prefix + "-S"),
        Seg(x + width, y, x + width, y + height, prefix + "-E"),
        Seg(x + width, y + height, x, y + height, prefix + "-N"),
        Seg(x, y + height, x, y, prefix + "-W")
    };

    private static Segment2D Seg(double x1, double y1, double x2, double y2, string element) =>
        new Segment2D(new Point2D(x1, y1), new Point2D(x2, y2), new SourceRef("doc", element, GeometrySourceKind.WallCenterline));

    private static T Succeeds<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }
}
