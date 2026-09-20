using System;
using System.Collections.Generic;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Domain.ReviewPackages;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.ReviewPackages;

public sealed class ReviewPackageTests
{
    [Fact]
    public void Constructor_rejects_missing_required_identity()
    {
        Assert.Throws<ArgumentException>(() => new ReviewPackage(Guid.Empty, "view", "level", "scheme"));
        Assert.Throws<ArgumentException>(() => new ReviewPackage(Guid.NewGuid(), " ", "level", "scheme"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReviewPackage(Guid.NewGuid(), "view", "level", "scheme", boundaryRevision: -1));
    }

    [Fact]
    public void Constructor_normalizes_optional_ids_and_collections()
    {
        var package = new ReviewPackage(Guid.NewGuid(), " view ", " level ", " scheme ",
            legendViewUniqueIds: new[] { " legend-1 ", "legend-1", "", "legend-2" });

        Assert.Equal("view", package.SourceFloorPlanUniqueId);
        Assert.Null(package.AreaPlanUniqueId);
        Assert.Equal(new[] { "legend-1", "legend-2" }, package.LegendViewUniqueIds);
        Assert.Equal(ReviewPackage.CurrentSchemaVersion, package.SchemaVersion);
    }

    [Fact]
    public void With_drafting_view_records_the_identity_and_keeps_everything_else()
    {
        // Spec 10.5 item 5: the UniqueId is the 唯一識別 that survives a rename, so the write-back
        // records it on the package rather than hoping to find the view by name next time.
        var original = new ReviewPackage(Guid.NewGuid(), "source", "level", "scheme", "area",
            legendViewUniqueIds: new[] { "legend-a" }, sheetUniqueId: "sheet", boundaryRevision: 3);

        var updated = original.WithDraftingView(" drafting-1 ");

        Assert.Equal("drafting-1", updated.DraftingViewUniqueId);
        Assert.Equal(original.PackageId, updated.PackageId);
        Assert.Equal(original.AreaPlanUniqueId, updated.AreaPlanUniqueId);
        Assert.Equal(original.LegendViewUniqueIds, updated.LegendViewUniqueIds);
        Assert.Equal(original.SheetUniqueId, updated.SheetUniqueId);
        Assert.Equal(original.BoundaryRevision, updated.BoundaryRevision);
        Assert.Equal(original.Status, updated.Status);
    }

    [Fact]
    public void With_drafting_view_rejects_an_identity_that_is_not_one()
    {
        var package = new ReviewPackage(Guid.NewGuid(), "source", "level", "scheme", "area");

        Assert.Throws<ArgumentException>(() => package.WithDraftingView("  "));
    }

    [Fact]
    public void Storage_mapper_round_trips_all_fields()
    {
        var id = Guid.NewGuid();
        var updated = new DateTime(638900000000000000, DateTimeKind.Utc);
        var original = new ReviewPackage(id, "source", "level", "scheme", "area", "draft",
            new[] { "legend-a", "legend-b" }, "sheet", new[] { "boundary", "area-element" }, 7,
            "rules", "2026.09", "run-42", ReviewPackageStatus.Reviewed, updated);

        var restored = ReviewPackageStorageMapper.FromRecord(ReviewPackageStorageMapper.ToRecord(original));

        Assert.Equal(original.PackageId, restored.PackageId);
        Assert.Equal(original.SourceFloorPlanUniqueId, restored.SourceFloorPlanUniqueId);
        Assert.Equal(original.LevelUniqueId, restored.LevelUniqueId);
        Assert.Equal(original.AreaSchemeUniqueId, restored.AreaSchemeUniqueId);
        Assert.Equal(original.AreaPlanUniqueId, restored.AreaPlanUniqueId);
        Assert.Equal(original.DraftingViewUniqueId, restored.DraftingViewUniqueId);
        Assert.Equal(original.LegendViewUniqueIds, restored.LegendViewUniqueIds);
        Assert.Equal(original.SheetUniqueId, restored.SheetUniqueId);
        Assert.Equal(original.GeneratedElementUniqueIds, restored.GeneratedElementUniqueIds);
        Assert.Equal(original.BoundaryRevision, restored.BoundaryRevision);
        Assert.Equal(original.RuleSetId, restored.RuleSetId);
        Assert.Equal(original.RuleSetVersion, restored.RuleSetVersion);
        Assert.Equal(original.LastReviewRunId, restored.LastReviewRunId);
        Assert.Equal(original.Status, restored.Status);
        Assert.Equal(original.UpdatedAtUtc, restored.UpdatedAtUtc);
    }

    [Fact]
    public void Storage_mapper_migrates_legacy_0_9_record()
    {
        var record = RequiredRecord("0.9");
        record.LegendViewUniqueIds = null!;
        record.GeneratedElementUniqueIds = null!;

        var restored = ReviewPackageStorageMapper.FromRecord(record);

        Assert.Empty(restored.LegendViewUniqueIds);
        Assert.Empty(restored.GeneratedElementUniqueIds);
        Assert.Equal(ReviewPackage.CurrentSchemaVersion, record.SchemaVersion);
    }

    [Fact]
    public void Storage_mapper_rejects_unknown_schema_version()
    {
        var error = Assert.Throws<NotSupportedException>(() =>
            ReviewPackageStorageMapper.FromRecord(RequiredRecord("2.0")));
        Assert.Contains("2.0", error.Message, StringComparison.Ordinal);
    }

    private static ReviewPackageStorageRecord RequiredRecord(string version) => new ReviewPackageStorageRecord
    {
        SchemaVersion = version,
        PackageId = Guid.NewGuid().ToString("D"),
        SourceFloorPlanUniqueId = "source",
        LevelUniqueId = "level",
        AreaSchemeUniqueId = "scheme",
        LegendViewUniqueIds = new List<string>(),
        GeneratedElementUniqueIds = new List<string>(),
        Status = (int)ReviewPackageStatus.Setup,
        UpdatedAtUtcTicks = DateTime.UtcNow.Ticks
    };
}
