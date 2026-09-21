using System;
using System.Globalization;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>
/// How a <see cref="PlannedElement.Signature"/> is spelled. It lives on its own because two callers
/// need exactly the same spelling: <see cref="ZoneWritePlan"/> when it says what the model should
/// contain, and the staleness probe when it asks whether an element in the model still matches the
/// signature written on it (spec 13.1).
/// </summary>
/// <remarks>
/// The quantum is <see cref="GeometryTolerance.ClosureFeet"/>, never a new epsilon. Signatures have
/// to survive the float noise of a re-solve, or every re-run would report an update; and because the
/// quantum comes from the tolerance, a project that changes its tolerance changes every signature,
/// which is what makes spec 13.1's 幾何容差 change visible without storing the tolerance anywhere.
/// </remarks>
public static class PlannedElementSignature
{
    /// <summary>The signature of a boundary line or its 單線圖 copy.</summary>
    public static string ForSegment(Point2D start, Point2D end, double quantum) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}>{1}",
            ForPoint(start, quantum),
            ForPoint(end, quantum));

    /// <summary>
    /// The same segment whichever end it was traced from. The plan orders its outline canonically
    /// before writing, so a probe reading a curve out of Revit — where the direction is whatever
    /// Revit stored — has to normalize the same way or every line would look changed.
    /// </summary>
    public static string ForCanonicalSegment(Point2D start, Point2D end, double quantum) =>
        IsCanonical(start, end) ? ForSegment(start, end, quantum) : ForSegment(end, start, quantum);

    /// <summary>The signature of an Area: its name, its colour and where it sits.</summary>
    public static string ForArea(string? zoneName, string colorHex, Point2D placement, double quantum) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}|{1}|{2}",
            zoneName,
            colorHex,
            ForPoint(placement, quantum));

    /// <summary>The signature of an Area tag: the name it shows and where it sits.</summary>
    public static string ForTag(string? zoneName, Point2D placement, double quantum) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}|{1}",
            zoneName,
            ForPoint(placement, quantum));

    /// <summary>Rounds a point onto the closure tolerance grid.</summary>
    public static string ForPoint(Point2D point, double quantum)
    {
        if (double.IsNaN(quantum) || double.IsInfinity(quantum) || quantum <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantum), "The signature quantum must be a finite positive number.");

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1}",
            Math.Round(point.X / quantum),
            Math.Round(point.Y / quantum));
    }

    /// <summary>
    /// The part of an Area's signature that is its 區劃 name, for a caller that can read the name off
    /// the element but not the colour it was drawn in. Null when the signature is not an Area's.
    /// </summary>
    public static string? ZoneNameOf(string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return null;
        var index = signature!.IndexOf('|');
        return index < 0 ? null : signature.Substring(0, index);
    }

    /// <summary>Points a segment one way only, the way <see cref="ZoneWritePlan"/> orders its outline.</summary>
    public static bool IsCanonical(Point2D start, Point2D end) =>
        start.X < end.X || (start.X == end.X && start.Y <= end.Y);
}
