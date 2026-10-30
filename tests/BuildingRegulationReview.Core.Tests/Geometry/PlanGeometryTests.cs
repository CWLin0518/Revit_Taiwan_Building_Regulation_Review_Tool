using System;
using System.Linq;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Geometry;

public sealed class PlanGeometryTests
{
    private static SourceRef Wall(string id) => new SourceRef("host-doc", id, GeometrySourceKind.WallCenterline);

    private static Segment2D Line(double x1, double y1, double x2, double y2, string id = "wall-1") =>
        new Segment2D(new Point2D(x1, y1), new Point2D(x2, y2), Wall(id));

    private static Loop2D Rectangle(double width, double height, GeometryTolerance? tolerance = null) =>
        new Loop2D(
            new[]
            {
                Line(0, 0, width, 0, "s-bottom"),
                Line(width, 0, width, height, "s-right"),
                Line(width, height, 0, height, "s-top"),
                Line(0, height, 0, 0, "s-left")
            },
            tolerance ?? GeometryTolerance.Default);

    [Fact]
    public void Point_rejects_non_finite_coordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Point2D(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Point2D(0, double.PositiveInfinity));
    }

    [Fact]
    public void Point_equality_is_by_value()
    {
        Assert.Equal(new Point2D(1.5, 2.5), new Point2D(1.5, 2.5));
        Assert.Equal(new Point2D(1.5, 2.5).GetHashCode(), new Point2D(1.5, 2.5).GetHashCode());
        Assert.NotEqual(new Point2D(1.5, 2.5), new Point2D(2.5, 1.5));
        Assert.Equal(5.0, new Point2D(0, 0).DistanceTo(new Point2D(3, 4)), 9);
    }

    [Fact]
    public void SourceRef_requires_identity_and_distinguishes_link_instances()
    {
        Assert.Throws<ArgumentException>(() => new SourceRef(" ", "wall-1", GeometrySourceKind.WallCenterline));
        Assert.Throws<ArgumentException>(() => new SourceRef("host", " ", GeometrySourceKind.WallCenterline));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceRef("host", "wall-1", (GeometrySourceKind)99));

        var host = new SourceRef(" host ", " wall-1 ", GeometrySourceKind.WallCenterline);
        var linked = new SourceRef("host", "wall-1", GeometrySourceKind.WallCenterline, "link-7");

        Assert.Equal("host", host.DocumentUniqueId);
        Assert.Equal("wall-1", host.ElementUniqueId);
        Assert.False(host.IsFromLink);
        Assert.True(linked.IsFromLink);
        Assert.NotEqual(host, linked);
        Assert.Equal(host, new SourceRef("host", "wall-1", GeometrySourceKind.WallCenterline));
    }

    [Fact]
    public void Segment_rejects_a_degenerate_length()
    {
        Assert.Throws<ArgumentException>(() => Line(1, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => new Segment2D(new Point2D(0, 0), new Point2D(0.5, 0), Wall("w"), minimumLengthFeet: 1.0));
        Assert.Throws<ArgumentNullException>(() => new Segment2D(new Point2D(0, 0), new Point2D(1, 0), null!));
    }

    [Fact]
    public void Segment_direction_is_independent_of_drawing_order()
    {
        var forward = Line(0, 0, 10, 0);
        var backward = forward.Reversed();

        Assert.Equal(forward.DirectionRadians, backward.DirectionRadians, 9);
        Assert.Equal(0.0, forward.DirectionRadians, 9);
        Assert.Equal(Math.PI / 4.0, Line(0, 0, 5, 5).DirectionRadians, 9);
        Assert.Equal(new Point2D(5, 0), forward.Midpoint());
    }

    [Fact]
    public void Loop_requires_ordered_segments_that_close()
    {
        Assert.Throws<ArgumentException>(() => new Loop2D(new[] { Line(0, 0, 1, 0), Line(1, 0, 1, 1) }, GeometryTolerance.Default));

        // An open corner wider than the closure tolerance is an error, not something to guess at.
        var open = new[] { Line(0, 0, 10, 0), Line(10, 0, 10, 10), Line(10, 10, 0, 10), Line(0, 10, 0, 1) };
        Assert.Throws<ArgumentException>(() => new Loop2D(open, GeometryTolerance.Default));
    }

    [Fact]
    public void Loop_accepts_a_gap_inside_the_closure_tolerance()
    {
        var nearlyClosed = new[]
        {
            Line(0, 0, 10, 0),
            Line(10, 0, 10, 10),
            Line(10, 10, 0, 10),
            Line(0, 10, 0, 0.001)
        };

        var loop = new Loop2D(nearlyClosed, GeometryTolerance.FromMillimeters(1, 10, 50, 0.5, 10));

        Assert.Equal(4, loop.Segments.Count);
    }

    [Fact]
    public void Loop_reports_area_orientation_and_perimeter()
    {
        var counterClockwise = Rectangle(10, 20);

        Assert.Equal(200.0, counterClockwise.AreaSquareFeet, 9);
        Assert.Equal(200.0, counterClockwise.SignedAreaSquareFeet, 9);
        Assert.False(counterClockwise.IsClockwise);
        Assert.Equal(60.0, counterClockwise.PerimeterFeet, 9);
        Assert.Equal(PlanUnits.SquareFeetToSquareMeters(200.0), counterClockwise.AreaSquareMeters, 9);
        Assert.Equal(4, counterClockwise.Vertices.Count());
    }

    [Fact]
    public void Loop_drawn_clockwise_keeps_a_positive_absolute_area()
    {
        var clockwise = new Loop2D(
            new[]
            {
                Line(0, 0, 0, 20, "s-left"),
                Line(0, 20, 10, 20, "s-top"),
                Line(10, 20, 10, 0, "s-right"),
                Line(10, 0, 0, 0, "s-bottom")
            },
            GeometryTolerance.Default);

        Assert.True(clockwise.IsClockwise);
        Assert.Equal(-200.0, clockwise.SignedAreaSquareFeet, 9);
        Assert.Equal(200.0, clockwise.AreaSquareFeet, 9);
    }

    [Fact]
    public void Loop_lists_its_distinct_sources_for_traceability()
    {
        var loop = new Loop2D(
            new[]
            {
                Line(0, 0, 10, 0, "wall-a"),
                Line(10, 0, 10, 10, "wall-b"),
                Line(10, 10, 0, 10, "wall-a"),
                Line(0, 10, 0, 0, "wall-c")
            },
            GeometryTolerance.Default);

        Assert.Equal(3, loop.DistinctSources.Count());
    }

    [Fact]
    public void Region_subtracts_holes_from_the_outer_boundary()
    {
        var hole = new Loop2D(
            new[]
            {
                Line(2, 2, 4, 2, "h-1"),
                Line(4, 2, 4, 4, "h-2"),
                Line(4, 4, 2, 4, "h-3"),
                Line(2, 4, 2, 2, "h-4")
            },
            GeometryTolerance.Default);

        var region = new Region2D(Rectangle(10, 10), new[] { hole });

        Assert.Equal(96.0, region.NetAreaSquareFeet, 9);
        Assert.Equal(PlanUnits.SquareFeetToSquareMeters(96.0), region.NetAreaSquareMeters, 9);
        Assert.Equal(2, region.AllLoops.Count());
    }

    [Fact]
    public void Region_requires_an_outer_boundary()
    {
        Assert.Throws<ArgumentNullException>(() => new Region2D(null!));
        Assert.Throws<ArgumentException>(() => new Region2D(Rectangle(5, 5), new Loop2D[] { null! }));
    }
}

public sealed class GeometryToleranceTests
{
    [Fact]
    public void Default_tolerances_match_the_documented_millimetre_values()
    {
        var tolerance = GeometryTolerance.Default;

        Assert.Equal(1.0, PlanUnits.FeetToMillimeters(tolerance.DuplicateFeet), 9);
        Assert.Equal(10.0, PlanUnits.FeetToMillimeters(tolerance.SnapFeet), 9);
        Assert.Equal(50.0, PlanUnits.FeetToMillimeters(tolerance.GapExtensionFeet), 9);
        Assert.Equal(1.0, PlanUnits.FeetToMillimeters(tolerance.ClosureFeet), 9);
        Assert.Equal(0.5, tolerance.CollinearDegrees, 9);
    }

    [Fact]
    public void Tolerances_must_be_finite_and_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryTolerance.FromMillimeters(0, 10, 50, 0.5, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryTolerance.FromMillimeters(1, -10, 50, 0.5, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryTolerance.FromMillimeters(1, 10, 50, double.NaN, 1));
    }

    [Fact]
    public void Repair_stages_must_stay_in_increasing_order()
    {
        Assert.Throws<ArgumentException>(() => GeometryTolerance.FromMillimeters(20, 10, 50, 0.5, 1));
        Assert.Throws<ArgumentException>(() => GeometryTolerance.FromMillimeters(1, 60, 50, 0.5, 1));
        Assert.Throws<ArgumentException>(() => GeometryTolerance.FromMillimeters(1, 10, 50, 0.5, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryTolerance.FromMillimeters(1, 10, 50, 90, 1));
    }

    [Fact]
    public void Distance_predicates_follow_the_configured_limits()
    {
        var tolerance = GeometryTolerance.FromMillimeters(1, 10, 50, 0.5, 1);

        Assert.True(tolerance.IsDuplicateDistance(PlanUnits.MillimetersToFeet(0.5)));
        Assert.False(tolerance.IsDuplicateDistance(PlanUnits.MillimetersToFeet(2)));
        Assert.True(tolerance.IsSnapDistance(PlanUnits.MillimetersToFeet(10)));
        Assert.False(tolerance.IsSnapDistance(PlanUnits.MillimetersToFeet(11)));
        Assert.True(tolerance.CanExtendAcross(PlanUnits.MillimetersToFeet(49)));
        Assert.False(tolerance.CanExtendAcross(PlanUnits.MillimetersToFeet(51)));
        Assert.True(tolerance.IsClosedDistance(PlanUnits.MillimetersToFeet(0.9)));
        Assert.False(tolerance.IsClosedDistance(PlanUnits.MillimetersToFeet(1.1)));
    }

    [Fact]
    public void Collinearity_wraps_around_the_direction_range()
    {
        var tolerance = GeometryTolerance.FromMillimeters(1, 10, 50, 1.0, 1);
        var almostZero = 0.2 * Math.PI / 180.0;
        var almostPi = Math.PI - almostZero;

        Assert.True(tolerance.AreCollinearDirections(0, almostZero));
        // 0 and just-under-pi are the same line drawn the other way, not a 180 degree difference.
        Assert.True(tolerance.AreCollinearDirections(0, almostPi));
        Assert.False(tolerance.AreCollinearDirections(0, Math.PI / 4.0));
    }
}
