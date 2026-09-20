using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.Geometry;

// One extraction run for one ReviewPackage: the raw line network before any repair, plus the
// tolerance and scope it was read under. It is stored so a later run can be compared against it
// and so a boundary can always be traced back to the model state it came from.
public sealed class PlanGeometrySnapshot
{
    public const string CurrentSchemaVersion = "1.0";

    public PlanGeometrySnapshot(
        Guid packageId,
        string hostDocumentUniqueId,
        string levelUniqueId,
        IEnumerable<Segment2D> segments,
        GeometryTolerance tolerance,
        PlanExtent2D? extent = null,
        IEnumerable<string>? warnings = null,
        DateTime? extractedAtUtc = null)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(hostDocumentUniqueId)) throw new ArgumentException("Host document identity is required.", nameof(hostDocumentUniqueId));
        if (string.IsNullOrWhiteSpace(levelUniqueId)) throw new ArgumentException("Level UniqueId is required.", nameof(levelUniqueId));
        if (segments is null) throw new ArgumentNullException(nameof(segments));

        var copy = segments.ToArray();
        if (copy.Any(x => x is null)) throw new ArgumentException("A snapshot cannot contain null segments.", nameof(segments));

        PackageId = packageId;
        HostDocumentUniqueId = hostDocumentUniqueId.Trim();
        LevelUniqueId = levelUniqueId.Trim();
        Segments = new ReadOnlyCollection<Segment2D>(copy);
        Tolerance = tolerance ?? throw new ArgumentNullException(nameof(tolerance));
        Extent = extent;
        Warnings = new ReadOnlyCollection<string>((warnings ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList());
        ExtractedAtUtc = (extractedAtUtc ?? DateTime.UtcNow).ToUniversalTime();
    }

    public Guid PackageId { get; }
    public string SchemaVersion => CurrentSchemaVersion;
    public string HostDocumentUniqueId { get; }
    public string LevelUniqueId { get; }
    public IReadOnlyList<Segment2D> Segments { get; }
    public GeometryTolerance Tolerance { get; }
    public PlanExtent2D? Extent { get; }
    public IReadOnlyList<string> Warnings { get; }
    public DateTime ExtractedAtUtc { get; }

    public bool IsEmpty => Segments.Count == 0;
    public double TotalLengthFeet => Segments.Sum(s => s.LengthFeet);

    public IEnumerable<Segment2D> OfKind(GeometrySourceKind kind) => Segments.Where(s => s.Source.Kind == kind);

    public IEnumerable<SourceRef> DistinctSources => Segments.Select(s => s.Source).Distinct();

    /// <summary>Extent measured from the geometry itself, for views without a crop or scope box.</summary>
    public PlanExtent2D? MeasuredExtent()
    {
        if (Segments.Count == 0) return null;
        var points = Segments.SelectMany(s => new[] { s.Start, s.End }).ToArray();
        return new PlanExtent2D(
            new Point2D(points.Min(p => p.X), points.Min(p => p.Y)),
            new Point2D(points.Max(p => p.X), points.Max(p => p.Y)));
    }
}
