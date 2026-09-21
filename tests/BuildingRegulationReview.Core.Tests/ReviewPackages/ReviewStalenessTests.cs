using System;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Domain.ReviewPackages;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.ReviewPackages;

/// <summary>
/// Spec 13.1: 來源視圖／Level、Area Boundary、Area、Area Scheme、規則版本 或幾何容差 的變更使相關
/// 結果成為 Stale。
/// </summary>
public class ReviewStalenessTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime At = new DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AModelThatStillMatchesLeavesTheStatusAlone()
    {
        var verdict = ReviewStaleness.Evaluate(Ready(), Intact(), At);

        Assert.False(verdict.IsStale);
        Assert.False(verdict.Changed);
        Assert.Empty(verdict.Reasons);
        Assert.Equal(ReviewPackageStatus.Ready, verdict.Status);
    }

    [Fact]
    public void ABoundaryThatWasDraggedMakesTheResultStale()
    {
        var verdict = ReviewStaleness.Evaluate(Ready(), Intact(changedManagedElementCount: 3), At);

        Assert.True(verdict.IsStale);
        Assert.True(verdict.Changed);
        Assert.Contains(verdict.Reasons, r => r.Contains("3 個面積邊界或面積已被修改", StringComparison.Ordinal));
        Assert.Equal(ReviewPackageStatus.Stale, verdict.Package.Status);
    }

    [Fact]
    public void AToleranceChangeArrivesAsTheSameChangedSignature()
    {
        // Signatures are quantized with GeometryTolerance.ClosureFeet, so a project that changed its
        // tolerance reports exactly what a dragged boundary reports — which is the point: both mean
        // the written result no longer describes what the tool would write now.
        var verdict = ReviewStaleness.Evaluate(Ready(), Intact(changedManagedElementCount: 12), At);

        Assert.True(verdict.IsStale);
        Assert.Single(verdict.Reasons);
    }

    [Fact]
    public void AMissingSourceFloorPlanMakesTheResultStale()
    {
        var verdict = ReviewStaleness.Evaluate(
            Ready(),
            new ReviewModelObservation(false, true, true, true, managedElementCount: 8),
            At);

        Assert.True(verdict.IsStale);
        Assert.Contains(verdict.Reasons, r => r.Contains("來源樓層平面", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingLevelOrAreaSchemeIsJustAsMuchOfAChange()
    {
        var verdict = ReviewStaleness.Evaluate(
            Ready(),
            new ReviewModelObservation(true, false, false, true, managedElementCount: 8),
            At);

        Assert.True(verdict.IsStale);
        Assert.Equal(2, verdict.Reasons.Count);
    }

    [Fact]
    public void AnAreaPlanRepointedAtAnotherLevelIsAChangeEvenThoughBothStillExist()
    {
        var verdict = ReviewStaleness.Evaluate(
            Ready(),
            new ReviewModelObservation(
                true, true, true, true,
                areaPlanLevelUniqueId: "level-9",
                areaPlanAreaSchemeUniqueId: "area-scheme",
                managedElementCount: 8),
            At);

        Assert.True(verdict.IsStale);
        Assert.Contains(verdict.Reasons, r => r.Contains("對應的樓層與這個套件記錄的不同", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAreaPlanRepointedAtAnotherAreaSchemeIsAChangeToo()
    {
        var verdict = ReviewStaleness.Evaluate(
            Ready(),
            new ReviewModelObservation(
                true, true, true, true,
                areaPlanLevelUniqueId: "level-1",
                areaPlanAreaSchemeUniqueId: "another-scheme",
                managedElementCount: 8),
            At);

        Assert.Contains(verdict.Reasons, r => r.Contains("Area Scheme", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingAreaPlanIsAnErrorRatherThanStalenessBecauseRerunningWouldNotHelp()
    {
        var verdict = ReviewStaleness.Evaluate(
            Ready(),
            new ReviewModelObservation(true, true, true, areaPlanFound: false),
            At);

        Assert.True(verdict.IsError);
        Assert.False(verdict.IsStale);
        Assert.Equal(ReviewPackageStatus.Error, verdict.Package.Status);
    }

    [Fact]
    public void ADraftingViewThePackageRecordedAndCannotFindIsAChange()
    {
        var verdict = ReviewStaleness.Evaluate(
            Ready(draftingViewUniqueId: "drafting-1"),
            Intact(draftingViewFound: false),
            At);

        Assert.True(verdict.IsStale);
        Assert.Contains(verdict.Reasons, r => r.Contains("單線圖視圖", StringComparison.Ordinal));
    }

    [Fact]
    public void APackageWithNoDraftingViewHasNoneToMiss()
    {
        var verdict = ReviewStaleness.Evaluate(Ready(), Intact(draftingViewFound: null), At);

        Assert.Empty(verdict.Reasons);
    }

    [Fact]
    public void EverythingTheToolWroteBeingGoneIsAChange()
    {
        var verdict = ReviewStaleness.Evaluate(Ready(), Intact(managedElementCount: 0), At);

        Assert.True(verdict.IsStale);
        Assert.Contains(verdict.Reasons, r => r.Contains("都已不在模型中", StringComparison.Ordinal));
    }

    [Fact]
    public void AnElementWhoseOwnershipMarkNoLongerParsesIsReportedBecauseItWillNeverBeUpdatedAgain()
    {
        var verdict = ReviewStaleness.Evaluate(Ready(), Intact(unreadableManagedElementCount: 2), At);

        Assert.True(verdict.IsStale);
        Assert.Contains(verdict.Reasons, r => r.Contains("讀不到擁有權標記", StringComparison.Ordinal));
    }

    [Fact]
    public void ANewerRuleSetVersionMakesTheResultStale()
    {
        var verdict = ReviewStaleness.Evaluate(
            Ready(ruleSetVersion: "2026.09"),
            Intact(ruleSetVersion: "2026.12"),
            At);

        Assert.True(verdict.IsStale);
        Assert.Contains(verdict.Reasons, r => r.Contains("規則版本已從 2026.09 更新為 2026.12", StringComparison.Ordinal));
    }

    [Fact]
    public void ARuleSetNobodyHasNamedSaysNothingEitherWay()
    {
        var verdict = ReviewStaleness.Evaluate(Ready(ruleSetVersion: "2026.09"), Intact(), At);

        Assert.Empty(verdict.Reasons);
    }

    [Fact]
    public void APackageThatHasNotPublishedAResultYetReportsTheChangeWithoutBeingCalledStale()
    {
        // 區劃草稿 already says the work is unfinished; replacing it with 已失效 would say less.
        var verdict = ReviewStaleness.Evaluate(
            Package(ReviewPackageStatus.BoundaryDraft, boundaryRevision: 1),
            Intact(changedManagedElementCount: 2),
            At);

        Assert.False(verdict.IsStale);
        Assert.False(verdict.Changed);
        Assert.NotEmpty(verdict.Reasons);
        Assert.Contains("尚未有可失效的結果", verdict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReviewedPackageGoesStaleJustLikeAReadyOne()
    {
        var verdict = ReviewStaleness.Evaluate(
            Package(ReviewPackageStatus.Reviewed, boundaryRevision: 2),
            Intact(changedManagedElementCount: 1),
            At);

        Assert.True(verdict.IsStale);
        Assert.Equal(ReviewPackageStatus.Reviewed, verdict.PreviousStatus);
    }

    [Fact]
    public void EveryReasonReachesTheLogWithItsOwnCodeAndASuggestion()
    {
        var verdict = ReviewStaleness.Evaluate(Ready(), Intact(changedManagedElementCount: 1), At);

        var log = ReviewStaleness.Explain(verdict, At);
        var reason = Assert.Single(log.Entries, e => e.Code == ReviewErrorCode.StatusStale && e.ElementUniqueId is null && e.Suggestion is not null);

        Assert.Equal(ReviewStage.Status, reason.Stage);
        Assert.Contains("重新套用", reason.Suggestion!, StringComparison.Ordinal);
        Assert.Contains(log.Entries, e => e.TechnicalDetail is not null && e.TechnicalDetail.Contains("Ready -> Stale", StringComparison.Ordinal));
    }

    [Fact]
    public void AnObservationCannotCountFewerThanNoElements()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ReviewModelObservation(true, true, true, true, managedElementCount: -1));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static ReviewPackage Ready(string? draftingViewUniqueId = null, string? ruleSetVersion = null) =>
        new ReviewPackage(
            PackageId, "floor-plan", "level-1", "area-scheme", "area-plan",
            draftingViewUniqueId: draftingViewUniqueId,
            boundaryRevision: 1,
            ruleSetVersion: ruleSetVersion,
            status: ReviewPackageStatus.Ready);

    private static ReviewPackage Package(ReviewPackageStatus status, int boundaryRevision) =>
        new ReviewPackage(
            PackageId, "floor-plan", "level-1", "area-scheme", "area-plan",
            boundaryRevision: boundaryRevision, status: status);

    private static ReviewModelObservation Intact(
        bool? draftingViewFound = null,
        int managedElementCount = 8,
        int changedManagedElementCount = 0,
        int unreadableManagedElementCount = 0,
        string? ruleSetVersion = null) =>
        new ReviewModelObservation(
            sourceFloorPlanFound: true,
            levelFound: true,
            areaSchemeFound: true,
            areaPlanFound: true,
            draftingViewFound: draftingViewFound,
            areaPlanLevelUniqueId: "level-1",
            areaPlanAreaSchemeUniqueId: "area-scheme",
            managedElementCount: managedElementCount,
            changedManagedElementCount: changedManagedElementCount,
            unreadableManagedElementCount: unreadableManagedElementCount,
            ruleSetVersion: ruleSetVersion);
}
