using System;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Domain.ReviewPackages;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.ProjectSetup;

public sealed class AreaPlanProvisioningTests
{
    [Fact]
    public void CanReuse_requires_a_resolvable_area_plan()
    {
        var package = Package("area-plan-1");
        Assert.True(AreaPlanProvisioning.CanReuse(package, id => id == "area-plan-1"));
        Assert.False(AreaPlanProvisioning.CanReuse(package, _ => false));
        Assert.False(AreaPlanProvisioning.CanReuse(Package(null), _ => true));
    }

    [Fact]
    public void Complete_preserves_identity_and_records_area_plan()
    {
        var package = Package(null);
        var timestamp = new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc);
        var updated = AreaPlanProvisioning.Complete(package, " area-plan-2 ", timestamp);
        Assert.Equal(package.PackageId, updated.PackageId);
        Assert.Equal("area-plan-2", updated.AreaPlanUniqueId);
        Assert.Equal(timestamp, updated.UpdatedAtUtc);
        Assert.Equal(ReviewPackageStatus.Setup, updated.Status);
    }

    private static ReviewPackage Package(string? areaPlanId) => new ReviewPackage(
        Guid.Parse("79f3370a-0f50-4471-b99b-1c824cad1641"), "floor", "level", "scheme",
        areaPlanUniqueId: areaPlanId, updatedAtUtc: DateTime.UnixEpoch);
}
