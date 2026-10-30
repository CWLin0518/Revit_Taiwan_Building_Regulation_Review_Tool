using System;

namespace BuildingRegulationReview.Domain.Geometry;

// Revit stores lengths in decimal feet regardless of the project's display units, while the
// regulations and the review report are written in metres and square metres. Every conversion
// goes through here so a stray 3.28 never enters the pipeline.
public static class PlanUnits
{
    public const double MillimetersPerFoot = 304.8;
    public const double MetersPerFoot = 0.3048;
    public const double SquareMetersPerSquareFoot = MetersPerFoot * MetersPerFoot;

    public static double FeetToMillimeters(double feet) => Finite(feet, nameof(feet)) * MillimetersPerFoot;
    public static double MillimetersToFeet(double millimeters) => Finite(millimeters, nameof(millimeters)) / MillimetersPerFoot;
    public static double FeetToMeters(double feet) => Finite(feet, nameof(feet)) * MetersPerFoot;
    public static double MetersToFeet(double meters) => Finite(meters, nameof(meters)) / MetersPerFoot;

    public static double SquareFeetToSquareMeters(double squareFeet) =>
        Finite(squareFeet, nameof(squareFeet)) * SquareMetersPerSquareFoot;

    public static double SquareMetersToSquareFeet(double squareMeters) =>
        Finite(squareMeters, nameof(squareMeters)) / SquareMetersPerSquareFoot;

    private static double Finite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(name, "A unit conversion needs a finite value.");
        return value;
    }
}

// Rigid transform used to bring a linked model's plan geometry into host project coordinates
// (spec 10.1). Only rotation about Z and translation are supported: a link that is mirrored or
// non-uniformly scaled must be rejected by the adapter rather than silently flattened here.
public sealed class PlanTransform2D
{
    public static readonly PlanTransform2D Identity = new PlanTransform2D(new Point2D(0, 0), 0);

    public PlanTransform2D(Point2D translation, double rotationRadians)
    {
        if (double.IsNaN(rotationRadians) || double.IsInfinity(rotationRadians))
            throw new ArgumentOutOfRangeException(nameof(rotationRadians));

        Translation = translation;
        RotationRadians = rotationRadians;
        _cos = Math.Cos(rotationRadians);
        _sin = Math.Sin(rotationRadians);
    }

    private readonly double _cos;
    private readonly double _sin;

    public Point2D Translation { get; }
    public double RotationRadians { get; }
    public double RotationDegrees => RotationRadians * 180.0 / Math.PI;
    public bool IsIdentity => RotationRadians == 0 && Translation.X == 0 && Translation.Y == 0;

    public static PlanTransform2D FromDegrees(Point2D translation, double rotationDegrees) =>
        new PlanTransform2D(translation, rotationDegrees * Math.PI / 180.0);

    /// <summary>Rotates about the origin, then translates.</summary>
    public Point2D Apply(Point2D point) => new Point2D(
        (point.X * _cos) - (point.Y * _sin) + Translation.X,
        (point.X * _sin) + (point.Y * _cos) + Translation.Y);

    public Point2D ApplyInverse(Point2D point)
    {
        var dx = point.X - Translation.X;
        var dy = point.Y - Translation.Y;
        return new Point2D((dx * _cos) + (dy * _sin), (-dx * _sin) + (dy * _cos));
    }

    /// <summary>Transforms both endpoints and keeps the segment's provenance intact.</summary>
    public Segment2D Apply(Segment2D segment)
    {
        if (segment is null) throw new ArgumentNullException(nameof(segment));
        return new Segment2D(Apply(segment.Start), Apply(segment.End), segment.Source);
    }

    // Inverse of p' = R*p + t is p = R(-a)*p' - R(-a)*t.
    public PlanTransform2D Inverse() => new PlanTransform2D(
        new Point2D(
            (-Translation.X * _cos) - (Translation.Y * _sin),
            (Translation.X * _sin) - (Translation.Y * _cos)),
        -RotationRadians);
}

// Axis-aligned extent of the extraction scope: the Area Plan's crop region or scope box, already
// projected to the level plane. Used to discard geometry outside the view before repair runs.
public sealed class PlanExtent2D
{
    public PlanExtent2D(Point2D minimum, Point2D maximum)
    {
        if (maximum.X < minimum.X || maximum.Y < minimum.Y)
            throw new ArgumentException("Extent maximum must not be smaller than its minimum.", nameof(maximum));

        Minimum = minimum;
        Maximum = maximum;
    }

    public Point2D Minimum { get; }
    public Point2D Maximum { get; }
    public double WidthFeet => Maximum.X - Minimum.X;
    public double HeightFeet => Maximum.Y - Minimum.Y;

    public bool Contains(Point2D point, double toleranceFeet = 0) =>
        point.X >= Minimum.X - toleranceFeet && point.X <= Maximum.X + toleranceFeet &&
        point.Y >= Minimum.Y - toleranceFeet && point.Y <= Maximum.Y + toleranceFeet;

    public bool Contains(Segment2D segment, double toleranceFeet = 0)
    {
        if (segment is null) throw new ArgumentNullException(nameof(segment));
        return Contains(segment.Start, toleranceFeet) && Contains(segment.End, toleranceFeet);
    }
}
