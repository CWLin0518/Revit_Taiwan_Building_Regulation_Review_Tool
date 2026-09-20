using System;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Geometry;

public sealed class PlanUnitsTests
{
    [Fact]
    public void Length_conversions_use_the_exact_international_foot()
    {
        Assert.Equal(304.8, PlanUnits.FeetToMillimeters(1.0), 9);
        Assert.Equal(0.3048, PlanUnits.FeetToMeters(1.0), 9);
        Assert.Equal(1.0, PlanUnits.MillimetersToFeet(304.8), 9);
        Assert.Equal(1.0, PlanUnits.MetersToFeet(0.3048), 9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1234.5)]
    [InlineData(-98.7)]
    public void Length_conversions_round_trip(double feet)
    {
        Assert.Equal(feet, PlanUnits.MillimetersToFeet(PlanUnits.FeetToMillimeters(feet)), 9);
        Assert.Equal(feet, PlanUnits.MetersToFeet(PlanUnits.FeetToMeters(feet)), 9);
    }

    [Fact]
    public void Area_conversions_square_the_length_factor()
    {
        Assert.Equal(0.09290304, PlanUnits.SquareFeetToSquareMeters(1.0), 9);
        Assert.Equal(1.0, PlanUnits.SquareMetersToSquareFeet(0.09290304), 9);

        // A 100 m2 fire compartment is the number the regulation text works in.
        var squareFeet = PlanUnits.SquareMetersToSquareFeet(100.0);
        Assert.Equal(100.0, PlanUnits.SquareFeetToSquareMeters(squareFeet), 9);
    }

    [Fact]
    public void Conversions_reject_non_finite_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanUnits.FeetToMeters(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanUnits.MillimetersToFeet(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanUnits.SquareFeetToSquareMeters(double.NegativeInfinity));
    }
}

public sealed class PlanTransform2DTests
{
    private static readonly SourceRef Source = new SourceRef("host", "wall-1", GeometrySourceKind.WallCenterline);

    [Fact]
    public void Identity_leaves_points_untouched()
    {
        var point = new Point2D(3.5, -7.25);

        Assert.True(PlanTransform2D.Identity.IsIdentity);
        Assert.Equal(point, PlanTransform2D.Identity.Apply(point));
    }

    [Fact]
    public void Rotation_happens_about_the_origin_before_translation()
    {
        var transform = PlanTransform2D.FromDegrees(new Point2D(10, 5), 90);

        var moved = transform.Apply(new Point2D(1, 0));

        Assert.Equal(10.0, moved.X, 9);
        Assert.Equal(6.0, moved.Y, 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(-135)]
    [InlineData(180)]
    public void Apply_inverse_returns_the_original_point(double degrees)
    {
        var transform = PlanTransform2D.FromDegrees(new Point2D(-12.5, 88.25), degrees);
        var point = new Point2D(4.75, -3.125);

        var roundTripped = transform.ApplyInverse(transform.Apply(point));

        Assert.Equal(point.X, roundTripped.X, 9);
        Assert.Equal(point.Y, roundTripped.Y, 9);
    }

    [Fact]
    public void Inverse_transform_undoes_the_forward_transform()
    {
        var transform = PlanTransform2D.FromDegrees(new Point2D(7, -2), 42);
        var point = new Point2D(15, 9);

        var roundTripped = transform.Inverse().Apply(transform.Apply(point));

        Assert.Equal(point.X, roundTripped.X, 9);
        Assert.Equal(point.Y, roundTripped.Y, 9);
    }

    [Fact]
    public void Transformed_segment_keeps_its_length_and_provenance()
    {
        var transform = PlanTransform2D.FromDegrees(new Point2D(100, 200), 37);
        var segment = new Segment2D(new Point2D(0, 0), new Point2D(3, 4), Source);

        var moved = transform.Apply(segment);

        Assert.Equal(5.0, moved.LengthFeet, 9);
        Assert.Same(segment.Source, moved.Source);
    }

    [Fact]
    public void Constructor_rejects_non_finite_rotation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanTransform2D(new Point2D(0, 0), double.NaN));
    }
}

public sealed class PlanExtent2DTests
{
    private static readonly SourceRef Source = new SourceRef("host", "wall-1", GeometrySourceKind.WallCenterline);

    [Fact]
    public void Extent_reports_its_size_and_membership()
    {
        var extent = new PlanExtent2D(new Point2D(0, 0), new Point2D(10, 20));

        Assert.Equal(10.0, extent.WidthFeet, 9);
        Assert.Equal(20.0, extent.HeightFeet, 9);
        Assert.True(extent.Contains(new Point2D(5, 5)));
        Assert.False(extent.Contains(new Point2D(11, 5)));
        Assert.True(extent.Contains(new Point2D(11, 5), toleranceFeet: 1.5));
    }

    [Fact]
    public void Segment_is_contained_only_when_both_ends_are_inside()
    {
        var extent = new PlanExtent2D(new Point2D(0, 0), new Point2D(10, 10));

        Assert.True(extent.Contains(new Segment2D(new Point2D(1, 1), new Point2D(9, 9), Source)));
        Assert.False(extent.Contains(new Segment2D(new Point2D(1, 1), new Point2D(11, 9), Source)));
    }

    [Fact]
    public void Constructor_rejects_an_inverted_extent()
    {
        Assert.Throws<ArgumentException>(() => new PlanExtent2D(new Point2D(10, 0), new Point2D(0, 10)));
    }
}
