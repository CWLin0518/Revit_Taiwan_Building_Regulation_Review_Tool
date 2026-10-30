using System;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Geometry;

public class PlanGeometryBuilderTests
{
    private const string HostDocument = "host-document-uid";
    private const string Level = "level-uid";
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static PlanGeometryBuilder NewBuilder(PlanGeometryExtractionOptions? options = null) =>
        new PlanGeometryBuilder(
            new PlanGeometryExtractionRequest(PackageId, "area-plan-uid", options),
            HostDocument,
            Level);

    private static SourceRef Wall(string id = "wall-1") =>
        new SourceRef(HostDocument, id, GeometrySourceKind.WallCenterline);

    [Fact]
    public void TurnsAPolylineIntoConsecutiveSegments()
    {
        var builder = NewBuilder();

        var added = builder.AddPolyline(
            new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 5) }, Wall());

        Assert.Equal(2, added);
        var snapshot = builder.Build().Value;
        Assert.Equal(new Point2D(0, 0), snapshot.Segments[0].Start);
        Assert.Equal(new Point2D(10, 0), snapshot.Segments[0].End);
        Assert.Equal(new Point2D(10, 5), snapshot.Segments[1].End);
    }

    [Fact]
    public void ClosesAPolylineOnlyWhenAsked()
    {
        var square = new[] { new Point2D(0, 0), new Point2D(4, 0), new Point2D(4, 4), new Point2D(0, 4) };

        Assert.Equal(3, NewBuilder().AddPolyline(square, Wall()));
        Assert.Equal(4, NewBuilder().AddPolyline(square, Wall(), closed: true));
    }

    [Fact]
    public void DropsRepeatedPointsInsteadOfFailing()
    {
        var builder = NewBuilder();

        var added = builder.AddPolyline(
            new[] { new Point2D(0, 0), new Point2D(0, 0), new Point2D(6, 0) }, Wall());

        Assert.Equal(1, added);
        Assert.Equal(1, builder.DegenerateCount);
        Assert.Contains(builder.Build().Value.Warnings, w => w.Contains("長度為零"));
    }

    [Fact]
    public void KeepsTheSourceElementOnEverySegment()
    {
        var builder = NewBuilder();
        builder.AddPolyline(new[] { new Point2D(0, 0), new Point2D(3, 0), new Point2D(3, 3) }, Wall("wall-42"));

        var snapshot = builder.Build().Value;

        Assert.All(snapshot.Segments, s => Assert.Equal("wall-42", s.Source.ElementUniqueId));
        Assert.All(snapshot.Segments, s => Assert.Equal(GeometrySourceKind.WallCenterline, s.Source.Kind));
    }

    [Fact]
    public void RefusesASourceKindTheOptionsExcluded()
    {
        var builder = NewBuilder(new PlanGeometryExtractionOptions(includeColumnOutlines: false));
        var column = new SourceRef(HostDocument, "column-1", GeometrySourceKind.ColumnOutline);

        Assert.Throws<ArgumentException>(() =>
            builder.AddPolyline(new[] { new Point2D(0, 0), new Point2D(1, 0) }, column));
    }

    [Fact]
    public void ClipsGeometryToTheViewExtentAndCountsIt()
    {
        var builder = NewBuilder();
        builder.UseExtent(new PlanExtent2D(new Point2D(0, 0), new Point2D(10, 10)));

        builder.AddSegment(new Point2D(-5, 5), new Point2D(25, 5), Wall());

        // The cut lands on the extent grown by the snap tolerance: geometry that sits marginally
        // outside the crop is still what the view was meant to show.
        var slack = GeometryTolerance.Default.SnapFeet;
        var snapshot = builder.Build().Value;
        Assert.Equal(1, builder.ClippedCount);
        Assert.Equal(-slack, snapshot.Segments[0].Start.X, 9);
        Assert.Equal(10.0 + slack, snapshot.Segments[0].End.X, 9);
        Assert.Contains(snapshot.Warnings, w => w.Contains("裁切"));
    }

    [Fact]
    public void DropsGeometryEntirelyOutsideTheViewExtent()
    {
        var builder = NewBuilder();
        builder.UseExtent(new PlanExtent2D(new Point2D(0, 0), new Point2D(10, 10)));

        var added = builder.AddSegment(new Point2D(50, 50), new Point2D(60, 50), Wall());

        Assert.Equal(0, added);
        Assert.Equal(1, builder.OutsideExtentCount);
    }

    [Fact]
    public void KeepsGeometryOutsideTheExtentWhenTheScopeIsTurnedOff()
    {
        var builder = NewBuilder(new PlanGeometryExtractionOptions(restrictToViewExtent: false));
        builder.UseExtent(new PlanExtent2D(new Point2D(0, 0), new Point2D(10, 10)));

        var added = builder.AddSegment(new Point2D(50, 50), new Point2D(60, 50), Wall());

        Assert.False(builder.ClipsToExtent);
        Assert.Equal(1, added);
        Assert.Equal(0, builder.OutsideExtentCount);
    }

    [Fact]
    public void ReportsTheSameWarningOnlyOnce()
    {
        var builder = NewBuilder();
        builder.AddSegment(new Point2D(0, 0), new Point2D(5, 0), Wall());
        builder.AddWarning("柱輪廓無法分析。");
        builder.AddWarning("柱輪廓無法分析。");

        Assert.Single(builder.Build().Value.Warnings);
    }

    [Fact]
    public void FailsWhenNothingWasExtracted()
    {
        var result = NewBuilder().Build();

        Assert.True(result.IsFailure);
        Assert.Equal("geometry.extraction.empty", result.Error.Code);
    }

    [Fact]
    public void FailureDetailExplainsWhatWasDiscarded()
    {
        var builder = NewBuilder();
        builder.UseExtent(new PlanExtent2D(new Point2D(0, 0), new Point2D(10, 10)));
        builder.AddSegment(new Point2D(50, 50), new Point2D(60, 50), Wall());

        var result = builder.Build();

        Assert.Contains("outsideExtent=1", result.Error.TechnicalDetail!);
    }

    [Fact]
    public void CarriesThePackageScopeAndToleranceIntoTheSnapshot()
    {
        var tolerance = GeometryTolerance.FromMillimeters(2, 20, 80, 1.0, 2);
        var extent = new PlanExtent2D(new Point2D(0, 0), new Point2D(10, 10));
        var builder = NewBuilder(new PlanGeometryExtractionOptions(tolerance: tolerance));
        builder.UseExtent(extent);
        builder.AddSegment(new Point2D(1, 1), new Point2D(9, 1), Wall());

        var snapshot = builder.Build(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc)).Value;

        Assert.Equal(PackageId, snapshot.PackageId);
        Assert.Equal(HostDocument, snapshot.HostDocumentUniqueId);
        Assert.Equal(Level, snapshot.LevelUniqueId);
        Assert.Same(tolerance, snapshot.Tolerance);
        Assert.Same(extent, snapshot.Extent);
        Assert.Equal(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc), snapshot.ExtractedAtUtc);
    }

    [Fact]
    public void OrdersSegmentsBySourceSoTwoRunsMatchWhateverOrderTheModelWasRead()
    {
        var forward = NewBuilder();
        forward.AddPolyline(new[] { new Point2D(0, 0), new Point2D(5, 0) }, Wall("wall-a"));
        forward.AddPolyline(
            new[] { new Point2D(0, 0), new Point2D(1, 0), new Point2D(1, 1) },
            new SourceRef(HostDocument, "column-b", GeometrySourceKind.ColumnOutline), closed: true);
        forward.AddPolyline(new[] { new Point2D(5, 0), new Point2D(5, 5) }, Wall("wall-b"));

        var reversed = NewBuilder();
        reversed.AddPolyline(
            new[] { new Point2D(0, 0), new Point2D(1, 0), new Point2D(1, 1) },
            new SourceRef(HostDocument, "column-b", GeometrySourceKind.ColumnOutline), closed: true);
        reversed.AddPolyline(new[] { new Point2D(5, 0), new Point2D(5, 5) }, Wall("wall-b"));
        reversed.AddPolyline(new[] { new Point2D(0, 0), new Point2D(5, 0) }, Wall("wall-a"));

        Assert.Equal(
            forward.Build().Value.Segments.Select(s => s.ToString()),
            reversed.Build().Value.Segments.Select(s => s.ToString()));
    }

    [Fact]
    public void KeepsAPolylineInTheOrderItWasTraced()
    {
        var builder = NewBuilder();
        builder.AddPolyline(
            new[] { new Point2D(0, 0), new Point2D(4, 0), new Point2D(4, 4) }, Wall("wall-1"));

        var segments = builder.Build().Value.Segments;

        Assert.Equal(new Point2D(0, 0), segments[0].Start);
        Assert.Equal(new Point2D(4, 0), segments[1].Start);
    }

    [Fact]
    public void SnapshotSurvivesTheStorageRoundTrip()
    {
        var builder = NewBuilder();
        builder.UseExtent(new PlanExtent2D(new Point2D(0, 0), new Point2D(10, 10)));
        builder.AddPolyline(new[] { new Point2D(1, 1), new Point2D(9, 1), new Point2D(9, 9) }, Wall("wall-7"));
        builder.AddPolyline(
            new[] { new Point2D(2, 2), new Point2D(3, 2), new Point2D(3, 3) },
            new SourceRef(HostDocument, "column-3", GeometrySourceKind.ColumnOutline, "link-1"),
            closed: true);

        var original = builder.Build().Value;
        var restored = PlanGeometryStorageMapper.FromRecord(PlanGeometryStorageMapper.ToRecord(original));

        Assert.Equal(original.Segments.Count, restored.Segments.Count);
        Assert.Equal(
            original.Segments.Select(s => s.Source.ToString()),
            restored.Segments.Select(s => s.Source.ToString()));
        Assert.Equal("link-1", restored.OfKind(GeometrySourceKind.ColumnOutline).First().Source.LinkInstanceUniqueId);
    }
}
