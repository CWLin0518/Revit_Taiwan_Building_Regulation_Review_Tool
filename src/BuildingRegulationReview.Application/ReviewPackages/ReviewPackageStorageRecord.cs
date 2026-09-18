using System;
using System.Collections.Generic;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Application.ReviewPackages;

public sealed class ReviewPackageStorageRecord
{
    public string SchemaVersion { get; set; } = ReviewPackage.CurrentSchemaVersion;
    public string PackageId { get; set; } = string.Empty;
    public string SourceFloorPlanUniqueId { get; set; } = string.Empty;
    public string LevelUniqueId { get; set; } = string.Empty;
    public string AreaSchemeUniqueId { get; set; } = string.Empty;
    public string AreaPlanUniqueId { get; set; } = string.Empty;
    public string DraftingViewUniqueId { get; set; } = string.Empty;
    public IList<string> LegendViewUniqueIds { get; set; } = new List<string>();
    public string SheetUniqueId { get; set; } = string.Empty;
    public IList<string> GeneratedElementUniqueIds { get; set; } = new List<string>();
    public int BoundaryRevision { get; set; }
    public string RuleSetId { get; set; } = string.Empty;
    public string RuleSetVersion { get; set; } = string.Empty;
    public string LastReviewRunId { get; set; } = string.Empty;
    public int Status { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
}

public static class ReviewPackageStorageMapper
{
    public static ReviewPackageStorageRecord ToRecord(ReviewPackage package)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));
        return new ReviewPackageStorageRecord
        {
            SchemaVersion = ReviewPackage.CurrentSchemaVersion,
            PackageId = package.PackageId.ToString("D"),
            SourceFloorPlanUniqueId = package.SourceFloorPlanUniqueId,
            LevelUniqueId = package.LevelUniqueId,
            AreaSchemeUniqueId = package.AreaSchemeUniqueId,
            AreaPlanUniqueId = package.AreaPlanUniqueId ?? string.Empty,
            DraftingViewUniqueId = package.DraftingViewUniqueId ?? string.Empty,
            LegendViewUniqueIds = new List<string>(package.LegendViewUniqueIds),
            SheetUniqueId = package.SheetUniqueId ?? string.Empty,
            GeneratedElementUniqueIds = new List<string>(package.GeneratedElementUniqueIds),
            BoundaryRevision = package.BoundaryRevision,
            RuleSetId = package.RuleSetId ?? string.Empty,
            RuleSetVersion = package.RuleSetVersion ?? string.Empty,
            LastReviewRunId = package.LastReviewRunId ?? string.Empty,
            Status = (int)package.Status,
            UpdatedAtUtcTicks = package.UpdatedAtUtc.Ticks
        };
    }

    public static ReviewPackage FromRecord(ReviewPackageStorageRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        var migrated = ReviewPackageStorageMigrator.Migrate(record);
        if (!Guid.TryParse(migrated.PackageId, out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Stored ReviewPackage has an invalid PackageId.");
        if (!Enum.IsDefined(typeof(ReviewPackageStatus), migrated.Status))
            throw new InvalidOperationException("Stored ReviewPackage has an invalid status.");

        return new ReviewPackage(id, migrated.SourceFloorPlanUniqueId, migrated.LevelUniqueId,
            migrated.AreaSchemeUniqueId, migrated.AreaPlanUniqueId, migrated.DraftingViewUniqueId,
            migrated.LegendViewUniqueIds, migrated.SheetUniqueId, migrated.GeneratedElementUniqueIds,
            migrated.BoundaryRevision, migrated.RuleSetId, migrated.RuleSetVersion,
            migrated.LastReviewRunId, (ReviewPackageStatus)migrated.Status,
            new DateTime(migrated.UpdatedAtUtcTicks, DateTimeKind.Utc));
    }
}

public static class ReviewPackageStorageMigrator
{
    public static ReviewPackageStorageRecord Migrate(ReviewPackageStorageRecord record)
    {
        if (record.SchemaVersion == ReviewPackage.CurrentSchemaVersion) return record;
        if (record.SchemaVersion != "0.9")
            throw new NotSupportedException($"ReviewPackage schema version '{record.SchemaVersion}' is not supported.");

        record.LegendViewUniqueIds = record.LegendViewUniqueIds ?? new List<string>();
        record.GeneratedElementUniqueIds = record.GeneratedElementUniqueIds ?? new List<string>();
        record.SchemaVersion = ReviewPackage.CurrentSchemaVersion;
        return record;
    }
}
