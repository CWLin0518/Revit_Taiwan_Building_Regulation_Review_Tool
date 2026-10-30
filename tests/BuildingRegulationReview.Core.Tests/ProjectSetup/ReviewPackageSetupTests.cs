using System;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Domain.ReviewPackages;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.ProjectSetup;

public sealed class ReviewPackageSetupTests
{
    [Fact]
    public void Create_builds_setup_package_without_creating_area_plan()
    {
        var id = Guid.Parse("6ca51928-dc0f-47a0-b3a5-ccfd371b6fca");
        var time = new DateTime(2026, 9, 19, 1, 0, 0, DateTimeKind.Utc);
        var selection = new ReviewPackageSetupSelection("floor", "level", "scheme", "template", true, "scope");
        var package = ReviewPackageSetup.Create(selection, id, time);
        Assert.Equal(id, package.PackageId);
        Assert.Equal("floor", package.SourceFloorPlanUniqueId);
        Assert.Equal("level", package.LevelUniqueId);
        Assert.Equal("scheme", package.AreaSchemeUniqueId);
        Assert.Null(package.AreaPlanUniqueId);
        Assert.Equal(ReviewPackageStatus.Setup, package.Status);
    }
}
