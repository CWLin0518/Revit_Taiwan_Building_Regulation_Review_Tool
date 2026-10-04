using System;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Geometry;

public sealed class PlanGeometrySnapshotTests
{
    private static readonly Guid PackageId = Guid.Parse("7b6a1d2c-3e4f-4a5b-8c9d-0e1f2a3b4c5d");

    private static Segment2D Line(double x1, double y1, double x2, double y2, GeometrySourceKind kind, string id, string? link = null) =>
        new Segment2D(new Point2D(x1, y1), new Point2D(x2, y2), new SourceRef("host-doc", id, kind, link));

    private static PlanGeometrySnapshot Sample() => new PlanGeometrySnapshot(
        PackageId,
        " host-doc ",
        " level-2 ",
        new[]
        {
            Line(0, 0, 10, 0, GeometrySourceKind.WallCenterline, "wall-1"),
            Line(10, 0, 10, 20, GeometrySourceKind.WallCenterline, "wall-2"),
            Line(0, 0, 1, 1, GeometrySourceKind.ColumnOutline, "column-1"),
            Line(3, 3, 6, 3, GeometrySourceKind.AuxiliaryLine, "line-1", "link-9")
        },
        GeometryTolerance.FromMillimeters(1, 12, 48, 0.75, 1),
        new PlanExtent2D(new Point2D(-5, -5), new Point2D(50, 60)),
        new[] { " 1 link omitted ", "", "   " },
        new DateTime(638900000000000000, DateTimeKind.Utc));

    [Fact]
    public void Snapshot_normalizes_identity_and_warnings()
    {
        var snapshot = Sample();

        Assert.Equal("host-doc", snapshot.HostDocumentUniqueId);
        Assert.Equal("level-2", snapshot.LevelUniqueId);
        Assert.Equal(new[] { "1 link omitted" }, snapshot.Warnings);
        Assert.Equal(PlanGeometrySnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.False(snapshot.IsEmpty);
    }

    [Fact]
    public void Snapshot_requires_a_package_a_document_and_a_level()
    {
        var tolerance = GeometryTolerance.Default;
        var segments = Array.Empty<Segment2D>();

        Assert.Throws<ArgumentException>(() => new PlanGeometrySnapshot(Guid.Empty, "doc", "level", segments, tolerance));
        Assert.Throws<ArgumentException>(() => new PlanGeometrySnapshot(PackageId, " ", "level", segments, tolerance));
        Assert.Throws<ArgumentException>(() => new PlanGeometrySnapshot(PackageId, "doc", " ", segments, tolerance));
        Assert.Throws<ArgumentNullException>(() => new PlanGeometrySnapshot(PackageId, "doc", "level", segments, null!));
    }

    [Fact]
    public void Snapshot_groups_segments_by_source_kind()
    {
        var snapshot = Sample();

        Assert.Equal(2, snapshot.OfKind(GeometrySourceKind.WallCenterline).Count());
        Assert.Single(snapshot.OfKind(GeometrySourceKind.ColumnOutline));
        Assert.Single(snapshot.OfKind(GeometrySourceKind.AuxiliaryLine));
        Assert.Equal(4, snapshot.DistinctSources.Count());
        Assert.Equal(10 + 20 + Math.Sqrt(2) + 3, snapshot.TotalLengthFeet, 9);
    }

    [Fact]
    public void Measured_extent_bounds_every_endpoint()
    {
        var measured = Sample().MeasuredExtent();

        Assert.NotNull(measured);
        Assert.Equal(0.0, measured!.Minimum.X, 9);
        Assert.Equal(0.0, measured.Minimum.Y, 9);
        Assert.Equal(10.0, measured.Maximum.X, 9);
        Assert.Equal(20.0, measured.Maximum.Y, 9);
    }

    [Fact]
    public void Measured_extent_is_null_for_an_empty_snapshot()
    {
        var empty = new PlanGeometrySnapshot(PackageId, "doc", "level", Array.Empty<Segment2D>(), GeometryTolerance.Default);

        Assert.True(empty.IsEmpty);
        Assert.Null(empty.MeasuredExtent());
        Assert.Equal(0.0, empty.TotalLengthFeet, 9);
    }

    [Fact]
    public void Storage_mapper_round_trips_every_field()
    {
        var original = Sample();

        var restored = PlanGeometryStorageMapper.FromRecord(PlanGeometryStorageMapper.ToRecord(original));

        AssertSameSnapshot(original, restored);
    }

    [Fact]
    public void Snapshot_survives_a_json_round_trip()
    {
        var original = Sample();

        var json = JsonSerializer.Serialize(PlanGeometryStorageMapper.ToRecord(original));
        var restored = PlanGeometryStorageMapper.FromRecord(
            JsonSerializer.Deserialize<PlanGeometrySnapshotRecord>(json)!);

        AssertSameSnapshot(original, restored);
    }

    [Fact]
    public void Snapshot_without_an_extent_round_trips_as_null()
    {
        var original = new PlanGeometrySnapshot(PackageId, "doc", "level",
            new[] { Line(0, 0, 1, 0, GeometrySourceKind.WallCenterline, "wall-1") },
            GeometryTolerance.Default);

        var restored = PlanGeometryStorageMapper.FromRecord(PlanGeometryStorageMapper.ToRecord(original));

        Assert.Null(restored.Extent);
    }

    [Fact]
    public void Stored_geometry_with_bad_data_fails_loudly()
    {
        var record = PlanGeometryStorageMapper.ToRecord(Sample());

        record.PackageId = "not-a-guid";
        Assert.Throws<InvalidOperationException>(() => PlanGeometryStorageMapper.FromRecord(record));

        record.PackageId = PackageId.ToString("D");
        record.Segments[0].SourceKind = 99;
        Assert.Throws<InvalidOperationException>(() => PlanGeometryStorageMapper.FromRecord(record));
    }

    [Fact]
    public void Unknown_schema_version_is_rejected_rather_than_guessed()
    {
        var record = PlanGeometryStorageMapper.ToRecord(Sample());
        record.SchemaVersion = "9.9";

        Assert.Throws<NotSupportedException>(() => PlanGeometryStorageMapper.FromRecord(record));
    }

    private static void AssertSameSnapshot(PlanGeometrySnapshot expected, PlanGeometrySnapshot actual)
    {
        Assert.Equal(expected.PackageId, actual.PackageId);
        Assert.Equal(expected.HostDocumentUniqueId, actual.HostDocumentUniqueId);
        Assert.Equal(expected.LevelUniqueId, actual.LevelUniqueId);
        Assert.Equal(expected.ExtractedAtUtc, actual.ExtractedAtUtc);
        Assert.Equal(DateTimeKind.Utc, actual.ExtractedAtUtc.Kind);
        Assert.Equal(expected.Warnings, actual.Warnings);
        Assert.Equal(expected.Segments.Count, actual.Segments.Count);

        for (var i = 0; i < expected.Segments.Count; i++)
        {
            Assert.Equal(expected.Segments[i].Start, actual.Segments[i].Start);
            Assert.Equal(expected.Segments[i].End, actual.Segments[i].End);
            Assert.Equal(expected.Segments[i].Source, actual.Segments[i].Source);
        }

        Assert.Equal(expected.Tolerance.DuplicateFeet, actual.Tolerance.DuplicateFeet, 12);
        Assert.Equal(expected.Tolerance.SnapFeet, actual.Tolerance.SnapFeet, 12);
        Assert.Equal(expected.Tolerance.GapExtensionFeet, actual.Tolerance.GapExtensionFeet, 12);
        Assert.Equal(expected.Tolerance.CollinearRadians, actual.Tolerance.CollinearRadians, 12);
        Assert.Equal(expected.Tolerance.ClosureFeet, actual.Tolerance.ClosureFeet, 12);

        Assert.Equal(expected.Extent?.Minimum, actual.Extent?.Minimum);
        Assert.Equal(expected.Extent?.Maximum, actual.Extent?.Maximum);
    }
}

public sealed class PlanGeometryExtractionRequestTests
{
    [Fact]
    public void Default_options_read_host_walls_columns_aux_lines_and_room_separators_only()
    {
        var options = PlanGeometryExtractionOptions.Default;

        Assert.True(options.Includes(GeometrySourceKind.WallCenterline));
        Assert.True(options.Includes(GeometrySourceKind.ColumnOutline));
        Assert.True(options.Includes(GeometrySourceKind.AuxiliaryLine));
        Assert.True(options.Includes(GeometrySourceKind.RoomSeparationLine));
        Assert.False(options.IncludeLinkedModels);
        Assert.True(options.RestrictToViewExtent);
        Assert.Same(GeometryTolerance.Default, options.Tolerance);
    }

    [Fact]
    public void Options_reject_an_extraction_with_no_source_enabled()
    {
        Assert.Throws<ArgumentException>(() => new PlanGeometryExtractionOptions(false, false, false, false));
    }

    [Fact]
    public void Room_separators_alone_are_enough_to_extract()
    {
        // A 挑空 has no wall: an extraction of nothing but Room Separation lines is a real request.
        var options = new PlanGeometryExtractionOptions(false, false, false, includeRoomSeparationLines: true);

        Assert.True(options.Includes(GeometrySourceKind.RoomSeparationLine));
        Assert.False(options.Includes(GeometrySourceKind.WallCenterline));
    }

    [Fact]
    public void Disabled_source_is_reported_as_excluded()
    {
        var options = new PlanGeometryExtractionOptions(includeColumnOutlines: false);

        Assert.False(options.Includes(GeometrySourceKind.ColumnOutline));
        Assert.True(options.Includes(GeometrySourceKind.WallCenterline));
    }

    [Fact]
    public void Request_requires_a_package_and_an_area_plan()
    {
        Assert.Throws<ArgumentException>(() => new PlanGeometryExtractionRequest(Guid.Empty, "area-plan"));
        Assert.Throws<ArgumentException>(() => new PlanGeometryExtractionRequest(Guid.NewGuid(), " "));

        var request = new PlanGeometryExtractionRequest(Guid.NewGuid(), " area-plan ");

        Assert.Equal("area-plan", request.AreaPlanUniqueId);
        Assert.Same(PlanGeometryExtractionOptions.Default, request.Options);
    }
}
