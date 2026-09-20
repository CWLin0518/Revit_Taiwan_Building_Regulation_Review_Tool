using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Domain.Geometry;

// Coordinates are decimal feet on the package level's XY plane, expressed in the host document's
// project coordinates. Linked-model transforms and display-unit conversion happen before a point
// reaches this layer; see PlanTransform2D and PlanUnits.
public readonly struct Point2D : IEquatable<Point2D>
{
    public Point2D(double x, double y)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (double.IsNaN(y) || double.IsInfinity(y)) throw new ArgumentOutOfRangeException(nameof(y));
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }

    public double DistanceTo(Point2D other) => Math.Sqrt(SquaredDistanceTo(other));

    public double SquaredDistanceTo(Point2D other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return (dx * dx) + (dy * dy);
    }

    public Point2D Translate(double dx, double dy) => new Point2D(X + dx, Y + dy);

    public bool Equals(Point2D other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override bool Equals(object? obj) => obj is Point2D other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + X.GetHashCode();
            hash = (hash * 31) + Y.GetHashCode();
            return hash;
        }
    }

    public override string ToString() => $"({X:R}, {Y:R})";
}

// Why a segment exists in the network. The repair log and the review evidence both need this to
// explain a boundary back to the model, so it is captured at extraction time, not inferred later.
public enum GeometrySourceKind
{
    WallCenterline,
    ColumnOutline,
    AuxiliaryLine
}

public sealed class SourceRef : IEquatable<SourceRef>
{
    public SourceRef(string documentUniqueId, string elementUniqueId, GeometrySourceKind kind, string? linkInstanceUniqueId = null)
    {
        if (string.IsNullOrWhiteSpace(documentUniqueId)) throw new ArgumentException("Document identity is required.", nameof(documentUniqueId));
        if (string.IsNullOrWhiteSpace(elementUniqueId)) throw new ArgumentException("Element UniqueId is required.", nameof(elementUniqueId));
        if (!Enum.IsDefined(typeof(GeometrySourceKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));

        DocumentUniqueId = documentUniqueId.Trim();
        ElementUniqueId = elementUniqueId.Trim();
        Kind = kind;
        LinkInstanceUniqueId = string.IsNullOrWhiteSpace(linkInstanceUniqueId) ? null : linkInstanceUniqueId!.Trim();
    }

    public string DocumentUniqueId { get; }
    public string ElementUniqueId { get; }
    public GeometrySourceKind Kind { get; }
    public string? LinkInstanceUniqueId { get; }
    public bool IsFromLink => LinkInstanceUniqueId is not null;

    public bool Equals(SourceRef? other) =>
        other is not null &&
        string.Equals(DocumentUniqueId, other.DocumentUniqueId, StringComparison.Ordinal) &&
        string.Equals(ElementUniqueId, other.ElementUniqueId, StringComparison.Ordinal) &&
        Kind == other.Kind &&
        string.Equals(LinkInstanceUniqueId, other.LinkInstanceUniqueId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as SourceRef);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(DocumentUniqueId);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ElementUniqueId);
            hash = (hash * 31) + (int)Kind;
            hash = (hash * 31) + (LinkInstanceUniqueId is null ? 0 : StringComparer.Ordinal.GetHashCode(LinkInstanceUniqueId));
            return hash;
        }
    }

    public override string ToString() =>
        LinkInstanceUniqueId is null ? $"{Kind}:{ElementUniqueId}" : $"{Kind}:{ElementUniqueId}@{LinkInstanceUniqueId}";
}

public sealed class Segment2D
{
    public Segment2D(Point2D start, Point2D end, SourceRef source, double minimumLengthFeet = GeometryTolerance.ZeroLengthFeet)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (double.IsNaN(minimumLengthFeet) || minimumLengthFeet < 0) throw new ArgumentOutOfRangeException(nameof(minimumLengthFeet));
        if (start.DistanceTo(end) <= minimumLengthFeet)
            throw new ArgumentException("Segment must be longer than the zero-length tolerance.", nameof(end));

        Start = start;
        End = end;
    }

    public Point2D Start { get; }
    public Point2D End { get; }
    public SourceRef Source { get; }
    public double LengthFeet => Start.DistanceTo(End);

    // Measured from +X and normalized to [0, pi) so a segment and its reverse share one direction,
    // which is what collinear merging in P2-T03 compares.
    public double DirectionRadians
    {
        get
        {
            var angle = Math.Atan2(End.Y - Start.Y, End.X - Start.X);
            if (angle < 0) angle += Math.PI;
            if (angle >= Math.PI) angle -= Math.PI;
            return angle;
        }
    }

    public Segment2D Reversed() => new Segment2D(End, Start, Source);

    public Point2D Midpoint() => new Point2D((Start.X + End.X) / 2.0, (Start.Y + End.Y) / 2.0);

    public override string ToString() => $"{Start} -> {End} [{Source}]";
}

public sealed class Loop2D
{
    public Loop2D(IEnumerable<Segment2D> segments, GeometryTolerance tolerance)
    {
        if (segments is null) throw new ArgumentNullException(nameof(segments));
        if (tolerance is null) throw new ArgumentNullException(nameof(tolerance));

        var copy = segments.ToArray();
        if (copy.Length < 3) throw new ArgumentException("A loop needs at least three segments.", nameof(segments));
        if (copy.Any(x => x is null)) throw new ArgumentException("A loop cannot contain null segments.", nameof(segments));
        for (var i = 0; i < copy.Length; i++)
        {
            var next = copy[(i + 1) % copy.Length];
            if (copy[i].End.DistanceTo(next.Start) > tolerance.ClosureFeet)
                throw new ArgumentException("Segments must form an ordered closed loop within the closure tolerance.", nameof(segments));
        }

        Segments = new ReadOnlyCollection<Segment2D>(copy);
        Tolerance = tolerance;
    }

    public IReadOnlyList<Segment2D> Segments { get; }
    public GeometryTolerance Tolerance { get; }

    // Shoelace over the segment endpoints. Positive is counter-clockwise.
    public double SignedAreaSquareFeet =>
        0.5 * Segments.Sum(s => (s.Start.X * s.End.Y) - (s.End.X * s.Start.Y));

    public double AreaSquareFeet => Math.Abs(SignedAreaSquareFeet);
    public double AreaSquareMeters => PlanUnits.SquareFeetToSquareMeters(AreaSquareFeet);
    public bool IsClockwise => SignedAreaSquareFeet < 0;
    public double PerimeterFeet => Segments.Sum(s => s.LengthFeet);
    public IEnumerable<Point2D> Vertices => Segments.Select(s => s.Start);
    public IEnumerable<SourceRef> DistinctSources => Segments.Select(s => s.Source).Distinct();
}

public sealed class Region2D
{
    public Region2D(Loop2D outerBoundary, IEnumerable<Loop2D>? holes = null)
    {
        OuterBoundary = outerBoundary ?? throw new ArgumentNullException(nameof(outerBoundary));
        var copy = (holes ?? Array.Empty<Loop2D>()).ToArray();
        if (copy.Any(x => x is null)) throw new ArgumentException("Holes cannot contain null loops.", nameof(holes));
        Holes = new ReadOnlyCollection<Loop2D>(copy);
    }

    public Loop2D OuterBoundary { get; }
    public IReadOnlyList<Loop2D> Holes { get; }

    // Draft area only. The authoritative number is the Area element Revit reports after write-back;
    // spec 10.6 compares the two before a package may reach Ready.
    public double NetAreaSquareFeet => OuterBoundary.AreaSquareFeet - Holes.Sum(h => h.AreaSquareFeet);
    public double NetAreaSquareMeters => PlanUnits.SquareFeetToSquareMeters(NetAreaSquareFeet);

    public IEnumerable<Loop2D> AllLoops => new[] { OuterBoundary }.Concat(Holes);
}
