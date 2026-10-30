using System;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Geometry;

public class SegmentGeometryTests
{
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;

    [Fact]
    public void DirectionIsTheSameForASegmentAndItsReverse()
    {
        var forward = SegmentGeometry.DirectionRadians(new Point2D(0, 0), new Point2D(3, 3));
        var backward = SegmentGeometry.DirectionRadians(new Point2D(3, 3), new Point2D(0, 0));

        Assert.Equal(forward, backward, 12);
        Assert.InRange(forward, 0, Math.PI);
    }

    [Fact]
    public void FindsTheCrossingOfTwoSegments()
    {
        var hit = SegmentGeometry.TryIntersect(
            new Point2D(-5, 0), new Point2D(5, 0),
            new Point2D(0, -5), new Point2D(0, 5),
            0, out var tA, out var tB, out var point);

        Assert.True(hit);
        Assert.Equal(0.5, tA, 12);
        Assert.Equal(0.5, tB, 12);
        Assert.Equal(new Point2D(0, 0), point);
    }

    [Fact]
    public void ReportsNoCrossingForParallelSegments()
    {
        var hit = SegmentGeometry.TryIntersect(
            new Point2D(0, 0), new Point2D(10, 0),
            new Point2D(0, 1), new Point2D(10, 1),
            Tolerance.SnapFeet, out _, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void ReportsNoCrossingForCollinearSegments()
    {
        // Overlap is a duplicate, not an intersection; step 1 owns that case.
        var hit = SegmentGeometry.TryIntersect(
            new Point2D(0, 0), new Point2D(10, 0),
            new Point2D(5, 0), new Point2D(15, 0),
            Tolerance.SnapFeet, out _, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void AcceptsAnEndpointThatFallsShortWithinTolerance()
    {
        var shortfall = PlanUnits.MillimetersToFeet(5);

        var hit = SegmentGeometry.TryIntersect(
            new Point2D(0, 0), new Point2D(10, 0),
            new Point2D(5, shortfall), new Point2D(5, 10),
            Tolerance.SnapFeet, out var tA, out var tB, out _);

        Assert.True(hit);
        Assert.Equal(0.5, tA, 9);
        Assert.Equal(0, tB, 9);
    }

    [Fact]
    public void RejectsAnEndpointThatFallsShortBeyondTolerance()
    {
        var shortfall = PlanUnits.MillimetersToFeet(50);

        var hit = SegmentGeometry.TryIntersect(
            new Point2D(0, 0), new Point2D(10, 0),
            new Point2D(5, shortfall), new Point2D(5, 10),
            Tolerance.SnapFeet, out _, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void ClampsTheClosestPointToTheSegmentEnds()
    {
        var distance = SegmentGeometry.DistanceToSegment(
            new Point2D(15, 4), new Point2D(0, 0), new Point2D(10, 0), out var t, out var closest);

        Assert.Equal(1, t, 12);
        Assert.Equal(new Point2D(10, 0), closest);
        Assert.Equal(Math.Sqrt((5 * 5) + (4 * 4)), distance, 12);
    }

    [Fact]
    public void MeasuresDistanceToTheInfiniteLineSeparately()
    {
        Assert.Equal(4, SegmentGeometry.DistanceToLine(new Point2D(15, 4), new Point2D(0, 0), new Point2D(10, 0)), 12);
    }

    [Fact]
    public void ProjectsAPointOntoTheSegmentAxisInFeet()
    {
        Assert.Equal(15, SegmentGeometry.ProjectOntoAxis(new Point2D(15, 4), new Point2D(0, 0), new Point2D(10, 0)), 12);
        Assert.Equal(-3, SegmentGeometry.ProjectOntoAxis(new Point2D(-3, 1), new Point2D(0, 0), new Point2D(10, 0)), 12);
    }

    [Fact]
    public void RecognizesAnOverlappingCollinearPair()
    {
        var overlapping = SegmentGeometry.TryMeasureCollinearOverlap(
            new Point2D(0, 0), new Point2D(10, 0),
            new Point2D(5, 0), new Point2D(15, 0),
            Tolerance, Tolerance.DuplicateFeet, out var overlap, out var lateral);

        Assert.True(overlapping);
        Assert.Equal(5, overlap, 9);
        Assert.Equal(0, lateral, 12);
    }

    [Fact]
    public void RejectsACollinearPairThatOnlyTouchesEndToEnd()
    {
        // End to end is a collinear merge decision, and only the node degree can make it.
        var overlapping = SegmentGeometry.TryMeasureCollinearOverlap(
            new Point2D(0, 0), new Point2D(10, 0),
            new Point2D(10, 0), new Point2D(20, 0),
            Tolerance, Tolerance.DuplicateFeet, out _, out _);

        Assert.False(overlapping);
    }

    [Fact]
    public void RejectsAParallelPairThatIsTooFarApartSideways()
    {
        var overlapping = SegmentGeometry.TryMeasureCollinearOverlap(
            new Point2D(0, 0), new Point2D(10, 0),
            new Point2D(5, PlanUnits.MillimetersToFeet(5)), new Point2D(15, PlanUnits.MillimetersToFeet(5)),
            Tolerance, Tolerance.DuplicateFeet, out _, out var lateral);

        Assert.False(overlapping);
        Assert.Equal(5, PlanUnits.FeetToMillimeters(lateral), 6);
    }

    [Fact]
    public void FindsTheFirstSegmentARayHits()
    {
        var hit = SegmentGeometry.TryRayHitSegment(
            new Point2D(0, 0), 1, 0, 10,
            new Point2D(4, -5), new Point2D(4, 5),
            out var distance, out var tOnTarget, out var point);

        Assert.True(hit);
        Assert.Equal(4, distance, 9);
        Assert.Equal(0.5, tOnTarget, 9);
        Assert.Equal(new Point2D(4, 0), point);
    }

    [Fact]
    public void ReportsNoHitBeyondTheRayLength()
    {
        var hit = SegmentGeometry.TryRayHitSegment(
            new Point2D(0, 0), 1, 0, 3,
            new Point2D(4, -5), new Point2D(4, 5),
            out _, out _, out _);

        Assert.False(hit);
    }
}
