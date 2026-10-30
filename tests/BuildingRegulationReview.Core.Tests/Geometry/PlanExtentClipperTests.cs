using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Geometry;

public class PlanExtentClipperTests
{
    private static readonly PlanExtent2D Extent = new PlanExtent2D(new Point2D(0, 0), new Point2D(10, 10));

    [Fact]
    public void KeepsSegmentFullyInsideUnchanged()
    {
        var kept = PlanExtentClipper.TryClip(new Point2D(2, 2), new Point2D(8, 6), Extent, 0, out var start, out var end);

        Assert.True(kept);
        Assert.Equal(new Point2D(2, 2), start);
        Assert.Equal(new Point2D(8, 6), end);
    }

    [Fact]
    public void RejectsSegmentFullyOutside()
    {
        var kept = PlanExtentClipper.TryClip(new Point2D(12, 2), new Point2D(20, 6), Extent, 0, out _, out _);

        Assert.False(kept);
    }

    [Fact]
    public void RejectsSegmentThatStraddlesTheBoxWithoutCrossingIt()
    {
        // Passes diagonally past the top-right corner: every point is outside even though the
        // segment's own bounding box overlaps the extent.
        var kept = PlanExtentClipper.TryClip(new Point2D(6, 15), new Point2D(15, 6), Extent, 0, out _, out _);

        Assert.False(kept);
    }

    [Fact]
    public void CutsSegmentAtTheBoundaryItLeaves()
    {
        var kept = PlanExtentClipper.TryClip(new Point2D(5, 5), new Point2D(25, 5), Extent, 0, out var start, out var end);

        Assert.True(kept);
        Assert.Equal(new Point2D(5, 5), start);
        Assert.Equal(10.0, end.X, 9);
        Assert.Equal(5.0, end.Y, 9);
    }

    [Fact]
    public void CutsBothEndsOfASegmentCrossingTheWholeExtent()
    {
        var kept = PlanExtentClipper.TryClip(new Point2D(-5, 5), new Point2D(25, 5), Extent, 0, out var start, out var end);

        Assert.True(kept);
        Assert.Equal(0.0, start.X, 9);
        Assert.Equal(10.0, end.X, 9);
    }

    [Fact]
    public void KeepsSegmentRunningAlongTheBoundary()
    {
        var kept = PlanExtentClipper.TryClip(new Point2D(0, 0), new Point2D(10, 0), Extent, 0, out var start, out var end);

        Assert.True(kept);
        Assert.Equal(new Point2D(0, 0), start);
        Assert.Equal(new Point2D(10, 0), end);
    }

    [Fact]
    public void ToleranceKeepsASegmentJustOutsideTheBoundary()
    {
        var justOutside = PlanExtentClipper.TryClip(new Point2D(2, 10.05), new Point2D(8, 10.05), Extent, 0, out _, out _);
        var withTolerance = PlanExtentClipper.TryClip(new Point2D(2, 10.05), new Point2D(8, 10.05), Extent, 0.1, out _, out _);

        Assert.False(justOutside);
        Assert.True(withTolerance);
    }
}
