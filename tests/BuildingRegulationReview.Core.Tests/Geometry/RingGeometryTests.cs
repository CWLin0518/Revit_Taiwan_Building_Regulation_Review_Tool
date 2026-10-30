using System;
using System.Collections.Generic;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Geometry;

public class RingGeometryTests
{
    [Fact]
    public void MeasuresACounterClockwiseRingAsPositive()
    {
        Assert.Equal(100.0, RingGeometry.SignedArea(Square(10)), 9);
        Assert.True(RingGeometry.IsCounterClockwise(Square(10)));
    }

    [Fact]
    public void MeasuresAClockwiseRingAsNegative()
    {
        var reversed = new List<Point2D>(Square(10));
        reversed.Reverse();

        Assert.Equal(-100.0, RingGeometry.SignedArea(reversed), 9);
        Assert.False(RingGeometry.IsCounterClockwise(reversed));
    }

    [Fact]
    public void IgnoresAnOutAndBackSpikeWhenMeasuringArea()
    {
        // A bridge walked in both directions inside the same face shows up as a spike in the ring.
        // It encloses nothing, so it must not change the area.
        var ring = new List<Point2D>
        {
            new Point2D(0, 0),
            new Point2D(10, 0),
            new Point2D(10, 10),
            new Point2D(15, 10),
            new Point2D(10, 10),
            new Point2D(0, 10)
        };

        Assert.Equal(100.0, RingGeometry.SignedArea(ring), 9);
    }

    [Fact]
    public void TellsInsideFromOutside()
    {
        Assert.True(RingGeometry.ContainsPoint(Square(10), new Point2D(5, 5)));
        Assert.False(RingGeometry.ContainsPoint(Square(10), new Point2D(15, 5)));
        Assert.False(RingGeometry.ContainsPoint(Square(10), new Point2D(5, -0.5)));
    }

    [Fact]
    public void CountsAVertexOnTheScanLineOnlyOnce()
    {
        // A ray through a vertex is the classic way to get the crossing count wrong. The half-open
        // rule on Y has to keep the diamond's own corner from flipping the answer.
        var diamond = new List<Point2D>
        {
            new Point2D(0, -5),
            new Point2D(5, 0),
            new Point2D(0, 5),
            new Point2D(-5, 0)
        };

        Assert.True(RingGeometry.ContainsPoint(diamond, new Point2D(0, 0)));
        Assert.False(RingGeometry.ContainsPoint(diamond, new Point2D(-10, 0)));
        Assert.False(RingGeometry.ContainsPoint(diamond, new Point2D(10, 0)));
    }

    [Fact]
    public void FindsTheCentroidOfAConvexRing()
    {
        Assert.True(RingGeometry.TryFindInteriorPoint(Square(10), null, out var point));
        Assert.Equal(5.0, point.X, 9);
        Assert.Equal(5.0, point.Y, 9);
    }

    [Fact]
    public void FindsAPointInsideAnLShapeWhoseCentroidFallsOutside()
    {
        var shape = LShape();
        var centroid = new Point2D(28.0 / 6.0, 28.0 / 6.0);
        Assert.False(RingGeometry.ContainsPoint(shape, centroid));

        Assert.True(RingGeometry.TryFindInteriorPoint(shape, null, out var point));
        Assert.True(RingGeometry.ContainsPoint(shape, point));
    }

    [Fact]
    public void KeepsTheInteriorPointOutOfAHole()
    {
        var outer = Square(20);
        var hole = new List<Point2D>
        {
            new Point2D(5, 5),
            new Point2D(5, 15),
            new Point2D(15, 15),
            new Point2D(15, 5)
        };

        Assert.True(RingGeometry.TryFindInteriorPoint(outer, new[] { (IReadOnlyList<Point2D>)hole }, out var point));
        Assert.True(RingGeometry.ContainsPoint(outer, point));
        Assert.False(RingGeometry.ContainsPoint(hole, point));
    }

    [Fact]
    public void RefusesARingThatIsNotAPolygon()
    {
        var line = new List<Point2D> { new Point2D(0, 0), new Point2D(10, 0) };

        Assert.Equal(0.0, RingGeometry.SignedArea(line));
        Assert.False(RingGeometry.ContainsPoint(line, new Point2D(5, 0)));
        Assert.False(RingGeometry.TryFindInteriorPoint(line, null, out _));
    }

    [Fact]
    public void RejectsANullRing()
    {
        Assert.Throws<ArgumentNullException>(() => RingGeometry.SignedArea(null!));
        Assert.Throws<ArgumentNullException>(() => RingGeometry.ContainsPoint(null!, new Point2D(0, 0)));
        Assert.Throws<ArgumentNullException>(() => RingGeometry.TryFindInteriorPoint(null!, null, out _));
    }

    private static IReadOnlyList<Point2D> Square(double side) => new[]
    {
        new Point2D(0, 0),
        new Point2D(side, 0),
        new Point2D(side, side),
        new Point2D(0, side)
    };

    // 10 x 4 along the bottom plus 4 x 6 up the left: 64 square feet, centroid outside.
    private static IReadOnlyList<Point2D> LShape() => new[]
    {
        new Point2D(0, 0),
        new Point2D(10, 0),
        new Point2D(10, 4),
        new Point2D(4, 4),
        new Point2D(4, 10),
        new Point2D(0, 10)
    };
}
