using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Geometry;

// Flat, serializer-agnostic shape of a snapshot, mirroring ReviewPackageStorageRecord. Plain
// properties only, so Extensible Storage, JSON and a debug dump all see the same data.
public sealed class PlanGeometrySnapshotRecord
{
    public string SchemaVersion { get; set; } = PlanGeometrySnapshot.CurrentSchemaVersion;
    public string PackageId { get; set; } = string.Empty;
    public string HostDocumentUniqueId { get; set; } = string.Empty;
    public string LevelUniqueId { get; set; } = string.Empty;
    public IList<SegmentRecord> Segments { get; set; } = new List<SegmentRecord>();
    public GeometryToleranceRecord Tolerance { get; set; } = new GeometryToleranceRecord();
    public bool HasExtent { get; set; }
    public double ExtentMinX { get; set; }
    public double ExtentMinY { get; set; }
    public double ExtentMaxX { get; set; }
    public double ExtentMaxY { get; set; }
    public IList<string> Warnings { get; set; } = new List<string>();
    public long ExtractedAtUtcTicks { get; set; }
}

public sealed class SegmentRecord
{
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public string DocumentUniqueId { get; set; } = string.Empty;
    public string ElementUniqueId { get; set; } = string.Empty;
    public string LinkInstanceUniqueId { get; set; } = string.Empty;
    public int SourceKind { get; set; }
}

public sealed class GeometryToleranceRecord
{
    public double DuplicateFeet { get; set; } = GeometryTolerance.Default.DuplicateFeet;
    public double SnapFeet { get; set; } = GeometryTolerance.Default.SnapFeet;
    public double GapExtensionFeet { get; set; } = GeometryTolerance.Default.GapExtensionFeet;
    public double CollinearRadians { get; set; } = GeometryTolerance.Default.CollinearRadians;
    public double ClosureFeet { get; set; } = GeometryTolerance.Default.ClosureFeet;
}

public static class PlanGeometryStorageMapper
{
    public static PlanGeometrySnapshotRecord ToRecord(PlanGeometrySnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

        return new PlanGeometrySnapshotRecord
        {
            SchemaVersion = PlanGeometrySnapshot.CurrentSchemaVersion,
            PackageId = snapshot.PackageId.ToString("D"),
            HostDocumentUniqueId = snapshot.HostDocumentUniqueId,
            LevelUniqueId = snapshot.LevelUniqueId,
            Segments = snapshot.Segments.Select(ToRecord).ToList(),
            Tolerance = ToRecord(snapshot.Tolerance),
            HasExtent = snapshot.Extent is not null,
            ExtentMinX = snapshot.Extent?.Minimum.X ?? 0,
            ExtentMinY = snapshot.Extent?.Minimum.Y ?? 0,
            ExtentMaxX = snapshot.Extent?.Maximum.X ?? 0,
            ExtentMaxY = snapshot.Extent?.Maximum.Y ?? 0,
            Warnings = new List<string>(snapshot.Warnings),
            ExtractedAtUtcTicks = snapshot.ExtractedAtUtc.Ticks
        };
    }

    public static PlanGeometrySnapshot FromRecord(PlanGeometrySnapshotRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        if (!string.Equals(record.SchemaVersion, PlanGeometrySnapshot.CurrentSchemaVersion, StringComparison.Ordinal))
            throw new NotSupportedException($"Plan geometry schema version '{record.SchemaVersion}' is not supported.");
        if (!Guid.TryParse(record.PackageId, out var packageId) || packageId == Guid.Empty)
            throw new InvalidOperationException("Stored plan geometry has an invalid PackageId.");

        var extent = record.HasExtent
            ? new PlanExtent2D(
                new Point2D(record.ExtentMinX, record.ExtentMinY),
                new Point2D(record.ExtentMaxX, record.ExtentMaxY))
            : null;

        return new PlanGeometrySnapshot(
            packageId,
            record.HostDocumentUniqueId,
            record.LevelUniqueId,
            (record.Segments ?? new List<SegmentRecord>()).Select(FromRecord),
            FromRecord(record.Tolerance ?? new GeometryToleranceRecord()),
            extent,
            record.Warnings ?? new List<string>(),
            new DateTime(record.ExtractedAtUtcTicks, DateTimeKind.Utc));
    }

    private static SegmentRecord ToRecord(Segment2D segment) => new SegmentRecord
    {
        StartX = segment.Start.X,
        StartY = segment.Start.Y,
        EndX = segment.End.X,
        EndY = segment.End.Y,
        DocumentUniqueId = segment.Source.DocumentUniqueId,
        ElementUniqueId = segment.Source.ElementUniqueId,
        LinkInstanceUniqueId = segment.Source.LinkInstanceUniqueId ?? string.Empty,
        SourceKind = (int)segment.Source.Kind
    };

    private static Segment2D FromRecord(SegmentRecord record)
    {
        if (record is null) throw new InvalidOperationException("Stored plan geometry has a missing segment.");
        if (!Enum.IsDefined(typeof(GeometrySourceKind), record.SourceKind))
            throw new InvalidOperationException("Stored plan geometry has an invalid source kind.");

        var source = new SourceRef(
            record.DocumentUniqueId,
            record.ElementUniqueId,
            (GeometrySourceKind)record.SourceKind,
            string.IsNullOrWhiteSpace(record.LinkInstanceUniqueId) ? null : record.LinkInstanceUniqueId);

        return new Segment2D(new Point2D(record.StartX, record.StartY), new Point2D(record.EndX, record.EndY), source);
    }

    private static GeometryToleranceRecord ToRecord(GeometryTolerance tolerance) => new GeometryToleranceRecord
    {
        DuplicateFeet = tolerance.DuplicateFeet,
        SnapFeet = tolerance.SnapFeet,
        GapExtensionFeet = tolerance.GapExtensionFeet,
        CollinearRadians = tolerance.CollinearRadians,
        ClosureFeet = tolerance.ClosureFeet
    };

    private static GeometryTolerance FromRecord(GeometryToleranceRecord record) => new GeometryTolerance(
        record.DuplicateFeet, record.SnapFeet, record.GapExtensionFeet, record.CollinearRadians, record.ClosureFeet);
}
