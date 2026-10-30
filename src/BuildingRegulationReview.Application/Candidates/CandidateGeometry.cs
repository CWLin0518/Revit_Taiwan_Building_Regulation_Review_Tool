using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Candidates;

/// <summary>A closed parameter range [Low, High] along one segment, with 0 at its start and 1 at its end.</summary>
internal readonly struct ParameterRange
{
    public ParameterRange(double low, double high)
    {
        Low = low;
        High = high;
    }

    public double Low { get; }
    public double High { get; }
    public double Span => High - Low;
}

internal readonly struct PlanEdge
{
    public PlanEdge(Point2D start, Point2D end)
    {
        Start = start;
        End = end;
        MinX = Math.Min(start.X, end.X);
        MinY = Math.Min(start.Y, end.Y);
        MaxX = Math.Max(start.X, end.X);
        MaxY = Math.Max(start.Y, end.Y);
    }

    public Point2D Start { get; }
    public Point2D End { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public double Length => Start.DistanceTo(End);
    public double Direction => SegmentGeometry.DirectionRadians(Start, End);

    public bool IsNear(PlanEdge other, double distance) =>
        MinX - distance <= other.MaxX && other.MinX <= MaxX + distance &&
        MinY - distance <= other.MaxY && other.MinY <= MaxY + distance;
}

/// <summary>
/// A set of rings read even-odd — a zone's Areas, or a column or floor outline — with the few
/// questions the resolver asks of it: containment, distance to its edges, and an interior point.
/// </summary>
internal sealed class PlanShape
{
    private readonly List<IReadOnlyList<Point2D>> _rings;

    public PlanShape(IEnumerable<IReadOnlyList<Point2D>> rings)
    {
        _rings = rings.Where(r => r is not null && r.Count >= 3).ToList();
        Edges = _rings.SelectMany(RingEdges).Where(e => e.Length > GeometryTolerance.ZeroLengthFeet).ToList();
        if (Edges.Count > 0)
        {
            MinX = Edges.Min(e => e.MinX);
            MinY = Edges.Min(e => e.MinY);
            MaxX = Edges.Max(e => e.MaxX);
            MaxY = Edges.Max(e => e.MaxY);
        }
    }

    public IReadOnlyList<IReadOnlyList<Point2D>> Rings => _rings;
    public IReadOnlyList<PlanEdge> Edges { get; }
    public bool IsEmpty => Edges.Count == 0;
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }

    public bool Contains(Point2D point) => _rings.Count(r => RingGeometry.ContainsPoint(r, point)) % 2 == 1;

    public double DistanceToEdges(Point2D point)
    {
        var best = double.PositiveInfinity;
        foreach (var edge in Edges)
        {
            var distance = SegmentGeometry.DistanceToSegment(point, edge.Start, edge.End, out _, out _);
            if (distance < best) best = distance;
        }

        return best;
    }

    /// <summary>Strictly inside, and further than <paramref name="clearance"/> from every edge.</summary>
    public bool ContainsWithClearance(Point2D point, double clearance) =>
        Contains(point) && DistanceToEdges(point) > clearance;

    public bool Overlaps(double minX, double minY, double maxX, double maxY, double margin) =>
        !IsEmpty &&
        MinX - margin <= maxX && minX <= MaxX + margin &&
        MinY - margin <= maxY && minY <= MaxY + margin;

    /// <summary>
    /// Net area, taking the largest ring as the outline and the others as holes. The rings of one
    /// Area never overlap, so this equals the even-odd area.
    /// </summary>
    public double NetAreaSquareFeet()
    {
        if (_rings.Count == 0) return 0;
        var areas = _rings.Select(r => Math.Abs(RingGeometry.SignedArea(r))).OrderByDescending(a => a).ToList();
        return Math.Max(0, areas[0] - areas.Skip(1).Sum());
    }

    /// <summary>One point per ring that lies strictly inside the shape, for overlap tests.</summary>
    public IEnumerable<Point2D> InteriorPoints()
    {
        foreach (var ring in _rings)
        {
            var others = _rings.Where(r => !ReferenceEquals(r, ring));
            if (RingGeometry.TryFindInteriorPoint(ring, others, out var point) && Contains(point)) yield return point;
        }
    }

    private static IEnumerable<PlanEdge> RingEdges(IReadOnlyList<Point2D> ring)
    {
        for (var i = 0; i < ring.Count; i++) yield return new PlanEdge(ring[i], ring[(i + 1) % ring.Count]);
    }
}

/// <summary>How a path — a centreline or an outline perimeter — lies against a zone boundary.</summary>
internal sealed class PathMeasure
{
    public double TotalFeet { get; set; }

    /// <summary>Length running along a boundary edge within the boundary tolerance.</summary>
    public double OnBoundaryFeet { get; set; }

    /// <summary>Length running along a boundary edge within the member's half thickness plus tolerance.</summary>
    public double InBandFeet { get; set; }

    /// <summary>Length clear of the boundary and inside the zone.</summary>
    public double InsideFeet { get; set; }

    /// <summary>Length clear of the boundary and outside the zone.</summary>
    public double OutsideFeet { get; set; }
}

internal static class CandidateGeometry
{
    private const double ParameterEpsilon = 1.0e-12;

    /// <summary>
    /// The parameters of segment a→b whose points lie within <paramref name="distance"/> of segment
    /// p→q. The region is a capsule, which is convex, so the answer is one range: the union of the
    /// line's pieces inside the rectangle and the two end discs, computed exactly rather than sampled.
    /// </summary>
    public static bool TryCapsuleRange(Point2D a, Point2D b, Point2D p, Point2D q, double distance, out ParameterRange range)
    {
        range = default;
        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;

        var wx = b.X - a.X;
        var wy = b.Y - a.Y;
        var edgeLength = p.DistanceTo(q);

        if (edgeLength > GeometryTolerance.ZeroLengthFeet)
        {
            var ux = (q.X - p.X) / edgeLength;
            var uy = (q.Y - p.Y) / edgeLength;
            var ax = a.X - p.X;
            var ay = a.Y - p.Y;

            // In the edge's frame: u runs along it, v across it.
            var u0 = (ax * ux) + (ay * uy);
            var du = (wx * ux) + (wy * uy);
            var v0 = (ax * -uy) + (ay * ux);
            var dv = (wx * -uy) + (wy * ux);

            if (TrySolveLinear(u0, du, 0, edgeLength, out var uLow, out var uHigh) &&
                TrySolveLinear(v0, dv, -distance, distance, out var vLow, out var vHigh))
            {
                var rectLow = Math.Max(uLow, vLow);
                var rectHigh = Math.Min(uHigh, vHigh);
                if (rectLow <= rectHigh)
                {
                    low = Math.Min(low, rectLow);
                    high = Math.Max(high, rectHigh);
                }
            }
        }

        foreach (var centre in new[] { p, q })
        {
            if (!TrySolveDisc(a, wx, wy, centre, distance, out var discLow, out var discHigh)) continue;
            low = Math.Min(low, discLow);
            high = Math.Max(high, discHigh);
        }

        low = Math.Max(0, low);
        high = Math.Min(1, high);
        if (!(high - low > ParameterEpsilon)) return false;

        range = new ParameterRange(low, high);
        return true;
    }

    public static List<ParameterRange> Union(IEnumerable<ParameterRange> ranges)
    {
        var merged = new List<ParameterRange>();
        foreach (var range in ranges.OrderBy(r => r.Low).ThenBy(r => r.High))
        {
            if (merged.Count > 0 && range.Low <= merged[merged.Count - 1].High)
            {
                var last = merged[merged.Count - 1];
                merged[merged.Count - 1] = new ParameterRange(last.Low, Math.Max(last.High, range.High));
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }

    /// <summary>The parts of [0, 1] a sorted, disjoint union does not cover.</summary>
    public static IEnumerable<ParameterRange> Complement(IReadOnlyList<ParameterRange> union)
    {
        var cursor = 0.0;
        foreach (var range in union)
        {
            if (range.Low - cursor > ParameterEpsilon) yield return new ParameterRange(cursor, range.Low);
            cursor = Math.Max(cursor, range.High);
        }

        if (1 - cursor > ParameterEpsilon) yield return new ParameterRange(cursor, 1);
    }

    public static bool AreParallel(double firstRadians, double secondRadians, double toleranceRadians)
    {
        var delta = Math.Abs(firstRadians - secondRadians);
        if (delta > Math.PI / 2.0) delta = Math.PI - delta;
        return delta <= toleranceRadians;
    }

    /// <summary>
    /// Splits a path against a zone boundary into: along the boundary (on the line, or within the
    /// band), clear of it inside, and clear of it outside. A clear piece cannot cross the boundary —
    /// crossing would bring it within the tolerance — so its midpoint decides the whole piece.
    /// </summary>
    public static PathMeasure MeasurePath(
        IEnumerable<PlanEdge> path,
        PlanShape zone,
        double boundaryTolerance,
        double bandDistance,
        double parallelRadians)
    {
        var measure = new PathMeasure();
        var reach = Math.Max(boundaryTolerance, bandDistance);

        foreach (var segment in path)
        {
            var length = segment.Length;
            if (length <= GeometryTolerance.ZeroLengthFeet) continue;
            measure.TotalFeet += length;

            var direction = segment.Direction;
            var onLine = new List<ParameterRange>();
            var inBand = new List<ParameterRange>();
            var near = new List<ParameterRange>();

            foreach (var edge in zone.Edges)
            {
                if (!segment.IsNear(edge, reach)) continue;

                var parallel = AreParallel(direction, edge.Direction, parallelRadians);
                if (TryCapsuleRange(segment.Start, segment.End, edge.Start, edge.End, boundaryTolerance, out var close))
                {
                    near.Add(close);
                    if (parallel) onLine.Add(close);
                }

                if (parallel && TryCapsuleRange(segment.Start, segment.End, edge.Start, edge.End, bandDistance, out var band))
                {
                    inBand.Add(band);
                    near.Add(band);
                }
            }

            measure.OnBoundaryFeet += Covered(onLine) * length;
            measure.InBandFeet += Covered(inBand) * length;

            foreach (var gap in Complement(Union(near)))
            {
                var midpoint = SegmentGeometry.PointAt(segment.Start, segment.End, (gap.Low + gap.High) / 2.0);
                if (zone.Contains(midpoint)) measure.InsideFeet += gap.Span * length;
                else measure.OutsideFeet += gap.Span * length;
            }
        }

        return measure;
    }

    /// <summary>
    /// Length of <paramref name="lines"/> passing through the interior of <paramref name="outline"/>
    /// with more than <paramref name="clearance"/> to spare. A boundary drawn along a column face does
    /// not count; one drawn through the column does.
    /// </summary>
    public static double LengthThrough(IEnumerable<PlanEdge> lines, PlanShape outline, double clearance)
    {
        var total = 0.0;
        foreach (var line in lines)
        {
            if (!outline.Overlaps(line.MinX, line.MinY, line.MaxX, line.MaxY, 0)) continue;

            var cuts = new List<double> { 0, 1 };
            foreach (var edge in outline.Edges)
            {
                if (!line.IsNear(edge, 0)) continue;
                if (SegmentGeometry.TryIntersect(line.Start, line.End, edge.Start, edge.End, 0, out var t, out _, out _))
                    cuts.Add(t);
            }

            cuts.Sort();
            var length = line.Length;
            for (var i = 0; i + 1 < cuts.Count; i++)
            {
                var span = cuts[i + 1] - cuts[i];
                if (span * length <= GeometryTolerance.ZeroLengthFeet) continue;
                var midpoint = SegmentGeometry.PointAt(line.Start, line.End, (cuts[i] + cuts[i + 1]) / 2.0);
                if (outline.ContainsWithClearance(midpoint, clearance)) total += span * length;
            }
        }

        return total;
    }

    public static IEnumerable<PlanEdge> Polyline(IReadOnlyList<Point2D> points)
    {
        for (var i = 0; i + 1 < points.Count; i++) yield return new PlanEdge(points[i], points[i + 1]);
    }

    private static double Covered(List<ParameterRange> ranges) => Union(ranges).Sum(r => r.Span);

    // lower <= c0 + c1 t <= upper.
    private static bool TrySolveLinear(double c0, double c1, double lower, double upper, out double low, out double high)
    {
        if (Math.Abs(c1) <= ParameterEpsilon)
        {
            low = double.NegativeInfinity;
            high = double.PositiveInfinity;
            return c0 >= lower && c0 <= upper;
        }

        var first = (lower - c0) / c1;
        var second = (upper - c0) / c1;
        low = Math.Min(first, second);
        high = Math.Max(first, second);
        return true;
    }

    // |a + t w - centre| <= radius.
    private static bool TrySolveDisc(Point2D a, double wx, double wy, Point2D centre, double radius, out double low, out double high)
    {
        low = 0;
        high = 0;
        var fx = a.X - centre.X;
        var fy = a.Y - centre.Y;
        var qa = (wx * wx) + (wy * wy);
        if (qa <= ParameterEpsilon) return false;

        var qb = 2 * ((wx * fx) + (wy * fy));
        var qc = (fx * fx) + (fy * fy) - (radius * radius);
        var discriminant = (qb * qb) - (4 * qa * qc);
        if (discriminant < 0) return false;

        var root = Math.Sqrt(discriminant);
        low = (-qb - root) / (2 * qa);
        high = (-qb + root) / (2 * qa);
        return true;
    }
}
