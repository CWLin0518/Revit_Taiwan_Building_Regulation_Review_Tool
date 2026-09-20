using System;

namespace BuildingRegulationReview.Domain.Geometry;

// The single tolerance contract for the whole network repair pipeline (spec 10.2). Values are
// decimal feet so they compare directly against model coordinates; the setup UI presents them in
// millimetres. Repair steps must read these values rather than hard-coding their own epsilons,
// otherwise a boundary can pass one step and fail the next.
public sealed class GeometryTolerance
{
    // Anything at or below this length is a degenerate curve, not a segment.
    public const double ZeroLengthFeet = 1.0e-6;

    public static readonly GeometryTolerance Default = FromMillimeters(
        duplicateMillimeters: 1.0,
        snapMillimeters: 10.0,
        gapExtensionMillimeters: 50.0,
        collinearDegrees: 0.5,
        closureMillimeters: 1.0);

    public GeometryTolerance(
        double duplicateFeet,
        double snapFeet,
        double gapExtensionFeet,
        double collinearRadians,
        double closureFeet)
    {
        Require(duplicateFeet, nameof(duplicateFeet));
        Require(snapFeet, nameof(snapFeet));
        Require(gapExtensionFeet, nameof(gapExtensionFeet));
        Require(collinearRadians, nameof(collinearRadians));
        Require(closureFeet, nameof(closureFeet));

        // Ordering matters: de-duplication runs before snapping, which runs before gap extension.
        // If a later step were tighter than an earlier one it would undo work already logged.
        if (duplicateFeet > snapFeet)
            throw new ArgumentException("Duplicate tolerance cannot exceed the snap tolerance.", nameof(duplicateFeet));
        if (snapFeet > gapExtensionFeet)
            throw new ArgumentException("Snap tolerance cannot exceed the gap extension tolerance.", nameof(snapFeet));
        if (closureFeet > snapFeet)
            throw new ArgumentException("Closure tolerance cannot exceed the snap tolerance.", nameof(closureFeet));
        if (collinearRadians >= Math.PI / 2.0)
            throw new ArgumentOutOfRangeException(nameof(collinearRadians), "Collinear tolerance must be smaller than 90 degrees.");

        DuplicateFeet = duplicateFeet;
        SnapFeet = snapFeet;
        GapExtensionFeet = gapExtensionFeet;
        CollinearRadians = collinearRadians;
        ClosureFeet = closureFeet;
    }

    /// <summary>Two segments closer than this everywhere are the same line.</summary>
    public double DuplicateFeet { get; }

    /// <summary>Endpoints within this distance are pulled onto one node.</summary>
    public double SnapFeet { get; }

    /// <summary>The furthest a dangling end may be extended to close a gap.</summary>
    public double GapExtensionFeet { get; }

    /// <summary>Direction difference under which two touching segments merge into one.</summary>
    public double CollinearRadians { get; }

    /// <summary>Residual distance still accepted when checking that a loop closes.</summary>
    public double ClosureFeet { get; }

    public double CollinearDegrees => CollinearRadians * 180.0 / Math.PI;

    public static GeometryTolerance FromMillimeters(
        double duplicateMillimeters,
        double snapMillimeters,
        double gapExtensionMillimeters,
        double collinearDegrees,
        double closureMillimeters) =>
        new GeometryTolerance(
            PlanUnits.MillimetersToFeet(duplicateMillimeters),
            PlanUnits.MillimetersToFeet(snapMillimeters),
            PlanUnits.MillimetersToFeet(gapExtensionMillimeters),
            collinearDegrees * Math.PI / 180.0,
            PlanUnits.MillimetersToFeet(closureMillimeters));

    public bool IsDuplicateDistance(double distanceFeet) => distanceFeet <= DuplicateFeet;
    public bool IsSnapDistance(double distanceFeet) => distanceFeet <= SnapFeet;
    public bool IsClosedDistance(double distanceFeet) => distanceFeet <= ClosureFeet;
    public bool CanExtendAcross(double gapFeet) => gapFeet <= GapExtensionFeet;

    /// <summary>Compares two segment directions already normalized to [0, pi).</summary>
    public bool AreCollinearDirections(double firstRadians, double secondRadians)
    {
        var delta = Math.Abs(firstRadians - secondRadians);
        if (delta > Math.PI / 2.0) delta = Math.PI - delta;
        return delta <= CollinearRadians;
    }

    private static void Require(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, "Tolerance must be a finite positive number.");
    }
}
