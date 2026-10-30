using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Domain.ReviewPackages;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.ReviewPackages;

/// <summary>
/// A package is a DataStorage (spec 8) and outlives the Area Plan it points at, so deleting the plan
/// leaves a package that no panel can open. These are the rules that keep it out of the pickers
/// without throwing it away — spec 附錄 9.5 asks for 刪除後修復, and re-running 防火區劃設定 is it.
/// </summary>
public class ReviewPackageAvailabilityTests
{
    private const string LivePlan = "live-area-plan";
    private const string DeletedPlan = "deleted-area-plan";

    private static bool OnlyLivePlanExists(string uniqueId) =>
        string.Equals(uniqueId, LivePlan, StringComparison.Ordinal);

    [Fact]
    public void APackageWhoseAreaPlanIsStillThereIsAvailable()
    {
        var state = ReviewPackageAvailability.Classify(Package(LivePlan), OnlyLivePlanExists);

        Assert.Equal(ReviewPackageAvailabilityState.Available, state);
    }

    [Fact]
    public void APackageWhoseAreaPlanWasDeletedIsNotAvailable()
    {
        var state = ReviewPackageAvailability.Classify(Package(DeletedPlan), OnlyLivePlanExists);

        Assert.Equal(ReviewPackageAvailabilityState.AreaPlanDeleted, state);
    }

    [Fact]
    public void APackageThatNeverRecordedAnAreaPlanIsAwaitingOne()
    {
        Assert.Equal(
            ReviewPackageAvailabilityState.AwaitingAreaPlan,
            ReviewPackageAvailability.Classify(Package(null), OnlyLivePlanExists));
    }

    [Fact]
    public void AnAreaPlanIdOfNothingButSpaceIsAwaitingOneRatherThanDeleted()
    {
        // ReviewPackage normalizes "   " to null, so this is the same case arriving by another door:
        // nothing was ever provisioned, and the predicate must not be asked about an empty id.
        var asked = false;
        var state = ReviewPackageAvailability.Classify(
            Package("   "),
            _ => { asked = true; return true; });

        Assert.Equal(ReviewPackageAvailabilityState.AwaitingAreaPlan, state);
        Assert.False(asked);
    }

    [Fact]
    public void PartitionListsOnlyTheAvailablePackagesAndKeepsTheirOrder()
    {
        var first = Package(LivePlan);
        var second = Package(DeletedPlan);
        var third = Package(LivePlan);

        var selection = ReviewPackageAvailability.Partition(
            new[] { first, second, third },
            OnlyLivePlanExists);

        Assert.Equal(new[] { first.PackageId, third.PackageId }, selection.Available.Select(x => x.PackageId));
        Assert.Equal(new[] { second.PackageId }, selection.AreaPlanDeleted.Select(x => x.PackageId));
        Assert.Empty(selection.AwaitingAreaPlan);
    }

    [Fact]
    public void PartitionAsksTheModelOncePerPackage()
    {
        // The predicate is a Revit element lookup; a picker that paid for it twice per package would
        // be doing it for every storey in the project.
        var calls = 0;
        ReviewPackageAvailability.Partition(
            new[] { Package(LivePlan), Package(DeletedPlan), Package(null) },
            _ => { calls++; return true; });

        Assert.Equal(2, calls);
    }

    [Fact]
    public void ADeletedAreaPlanIsWorthTellingTheUserAbout()
    {
        var selection = ReviewPackageAvailability.Partition(
            new[] { Package(LivePlan), Package(DeletedPlan), Package(DeletedPlan) },
            OnlyLivePlanExists);

        Assert.Contains("2 個檢討套件", selection.HiddenNotice, StringComparison.Ordinal);
        // The way back has to be in the message: the package is recoverable, not lost.
        Assert.Contains("防火區劃設定", selection.HiddenNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void APackageStillAwaitingItsAreaPlanIsNotWorthANotice()
    {
        // Mid-setup is not a problem to report; the picker's own empty-list message already says
        // what to run, and a package with no plan yet has nothing the user deleted.
        var selection = ReviewPackageAvailability.Partition(
            new[] { Package(LivePlan), Package(null) },
            OnlyLivePlanExists);

        Assert.Null(selection.HiddenNotice);
        Assert.Single(selection.AwaitingAreaPlan);
    }

    [Fact]
    public void NothingStoredIsAnEmptySelectionRatherThanAFailure()
    {
        var empty = ReviewPackageAvailability.Partition(Array.Empty<ReviewPackage>(), OnlyLivePlanExists);
        var missing = ReviewPackageAvailability.Partition(null, OnlyLivePlanExists);

        Assert.Empty(empty.Available);
        Assert.Null(empty.HiddenNotice);
        Assert.Empty(missing.Available);
        Assert.Empty(missing.AreaPlanDeleted);
        Assert.Empty(missing.AwaitingAreaPlan);
        Assert.Null(missing.HiddenNotice);
    }

    [Fact]
    public void TheCallerMustSupplyBothThePackageAndTheProbe()
    {
        Assert.Throws<ArgumentNullException>(() => ReviewPackageAvailability.Classify(null!, OnlyLivePlanExists));
        Assert.Throws<ArgumentNullException>(() => ReviewPackageAvailability.Classify(Package(LivePlan), null!));
        Assert.Throws<ArgumentNullException>(() => ReviewPackageAvailability.Partition(
            Array.Empty<ReviewPackage>(), null!));
    }

    private static ReviewPackage Package(string? areaPlanUniqueId) => new ReviewPackage(
        Guid.NewGuid(),
        "floor-plan",
        "level",
        "area-scheme",
        areaPlanUniqueId);
}
