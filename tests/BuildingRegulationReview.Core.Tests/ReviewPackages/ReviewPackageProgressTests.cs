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

    /// <summary>
    /// The deadlock this guards against: a model that already matches the draft has nothing to
    /// write, so demanding a write before advancing made 區劃草稿 the one state such a package could
    /// never leave — 防火區劃檢討 then refused it with 「區劃範圍尚未完成寫入」 however correct the
    /// model was. The write-back measures the Areas it left alone too, so the run arrives with
    /// evidence and is judged on it.
    /// </summary>
    [Fact]
    public void AModelThatAlreadyMatchesTheDraftStillReachesReady()
    {
        var package = Package(ReviewPackageStatus.BoundaryDraft, boundaryRevision: 2);

        var outcome = ReviewPackageProgress.After(package, NothingToWrite(agreeing: true), At);

        Assert.True(outcome.IsReady);
        Assert.Equal(ReviewPackageStatus.Ready, outcome.Status);
        Assert.Empty(outcome.Blockers);
        // Nothing moved, so the boundaries are the same ones as before.
        Assert.Equal(2, outcome.BoundaryRevision);
    }

    [Fact]
    public void AModelThatWroteNothingButDisagreesOnAreaIsStillKeptOutOfReady()
    {
        var outcome = ReviewPackageProgress.After(
            Package(ReviewPackageStatus.BoundaryDraft), NothingToWrite(agreeing: false), At);

        Assert.False(outcome.IsReady);
        Assert.Equal(ReviewPackageStatus.BoundaryDraft, outcome.Status);
        Assert.NotEmpty(outcome.Blockers);
    }

    /// <summary>
    /// A run that moved nothing invalidated nothing, so it must not throw away a review or a set of
    /// drawings just because it re-measured the same Areas.
    /// </summary>
    [Theory]
    [InlineData(ReviewPackageStatus.Reviewed)]
    [InlineData(ReviewPackageStatus.Documented)]
    public void AVerifyOnlyRunNeverDemotesAPackageThatIsAlreadyPastReady(ReviewPackageStatus status)
    {
        var package = Package(status, boundaryRevision: 3);

        var outcome = ReviewPackageProgress.After(package, NothingToWrite(agreeing: true), At);

        Assert.Equal(status, outcome.Status);
        Assert.False(outcome.Changed);
        Assert.Equal(3, outcome.BoundaryRevision);
    }

    /// <summary>
    /// 已失效 is ReviewStaleness's verdict, given for things one area measurement says nothing about
    /// — a missing 單線圖 view, a swapped Area Scheme, a new rule version. Measuring must not clear it.
    /// </summary>
    [Fact]
    public void AVerifyOnlyRunDoesNotClearStaleness()
    {
        var outcome = ReviewPackageProgress.After(
            Package(ReviewPackageStatus.Stale), NothingToWrite(agreeing: true), At);

        Assert.Equal(ReviewPackageStatus.Stale, outcome.Status);
        Assert.False(outcome.Changed);
    }

    /// <summary>A boundary that really does disagree is worth dropping back for, written or not.</summary>
    [Fact]
    public void AVerifyOnlyRunThatFindsADisagreementStillDropsAReviewedPackageBack()
    {
        var outcome = ReviewPackageProgress.After(
            Package(ReviewPackageStatus.Reviewed), NothingToWrite(agreeing: false), At);

        Assert.Equal(ReviewPackageStatus.BoundaryDraft, outcome.Status);
        Assert.NotEmpty(outcome.Blockers);
    }

    /// <summary>
    /// An Area the plan expected but that is no longer in the model is a gap in the evidence, not
    /// silence: a package must not be confirmed on the Areas that happen to be left.
    /// </summary>
    [Fact]
    public void AnAreaThatCouldNotBeMeasuredKeepsThePackageOutOfReady()
    {
        var plan = AppliedPlan();
        var builder = new ApplyResult.Builder(plan);
        foreach (var area in plan.UnchangedAreas)
        {
            builder.Area(AreaAgreement.Missing(
                area.Planned!.Key, area.Planned.ZoneName, area.Planned.NetAreaSquareMeters, area.ElementUniqueId));
        }

        var outcome = ReviewPackageProgress.After(Package(ReviewPackageStatus.BoundaryDraft), builder.Complete(), At);

        Assert.False(outcome.IsReady);
        Assert.Equal(ReviewPackageStatus.BoundaryDraft, outcome.Status);
        Assert.Contains(outcome.Blockers, b => b.Contains("已不在模型中", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingAreaIsAVerdictThatBlocksReadyRatherThanAnAbsentQuestion()
    {
        var finding = AreaAgreement.Missing(AreaKey(), "A", 92.9, "gone");

        Assert.Equal(AreaAgreementKind.Missing, finding.Kind);
        Assert.True(finding.BlocksReady);
        Assert.NotNull(finding.Message);
    }

    /// <summary>A verify-only run must not report that it wrote something.</summary>
    [Fact]
    public void AVerifyOnlyRunSaysTheModelWasNotChanged()
    {
        var outcome = ReviewPackageProgress.After(
            Package(ReviewPackageStatus.BoundaryDraft), NothingToWrite(agreeing: true), At);

        Assert.Contains("模型未變更", outcome.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("已寫入", outcome.Message, StringComparison.Ordinal);
    }

    /// <summary>An already-applied plan writes nothing, but still names the Areas that can be measured.</summary>
    [Fact]
    public void AnAlreadyAppliedPlanCarriesTheAreasTheCrossCheckCanMeasure()
    {
        var plan = AppliedPlan();

        Assert.True(plan.IsEmpty);
        Assert.NotEmpty(plan.Unchanged);
        var area = Assert.Single(plan.UnchangedAreas);
        Assert.Equal(ManagedElementKind.Area, area.Kind);
        Assert.NotNull(area.Planned);
        Assert.NotNull(area.ElementUniqueId);
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

    /// <summary>
    /// What the write-back reports for a model that already matches the draft: no step ran, but the
    /// Areas it left alone were still measured (spec 10.6).
    /// </summary>
    private static ApplyResult NothingToWrite(bool agreeing)
    {
        var plan = AppliedPlan();
        Assert.True(plan.IsEmpty);

        var builder = new ApplyResult.Builder(plan);
        foreach (var area in plan.UnchangedAreas)
        {
            builder.Area(AreaAgreement.Compare(
                area.Planned!.Key,
                area.Planned.ZoneName,
                area.Planned.NetAreaSquareMeters,
                agreeing ? area.Planned.NetAreaSquareMeters : area.Planned.NetAreaSquareMeters / 2,
                elementUniqueId: area.ElementUniqueId));
        }

        return builder.Complete();
    }

    /// <summary>The same plan, but against a model that already holds every element it plans.</summary>
    private static ApplyPlan AppliedPlan()
    {
        var map = Solve(Rectangle(0, 0, 10, 10, "OUTER"));
        var zones = Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            ZoneId, "A", ZoneColorPalette.At(0), new[] { map.FaceAt(new Point2D(5, 5))!.Id })));

        var fresh = ApplyPreview.Build(PackageId, map, zones);
        var existing = fresh.Items
            .Where(item => item.Planned is not null)
            .Select(item => new ExistingManagedElement(
                "existing-" + item.Planned!.Key.ToToken(),
                item.Planned.Key.ToToken(),
                item.Planned.Signature))
            .ToList();

        return ApplyPlan.Build(ApplyPreview.Build(PackageId, map, zones, existing), ApplyPlan.AllKinds);
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
