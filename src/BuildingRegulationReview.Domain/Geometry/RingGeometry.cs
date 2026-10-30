using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingRegulationReview.Domain.Geometry;

// Ring-level plane geometry: everything that needs a closed sequence of points rather than a single
// segment. It lives in the Domain because a solved face answers "is this point inside me?" on its
// own — the Region Editor hit-tests faces without going back through the solver.
public static class RingGeometry
{
    /// <summary>Shoelace area of a closed ring. Positive is counter-clockwise.</summary>
    public static double SignedArea(IReadOnlyList<Point2D> ring)
    {
        if (ring is null) throw new ArgumentNullException(nameof(ring));
        if (ring.Count < 3) return 0;

        var sum = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum / 2.0;
    }

    public static bool IsCounterClockwise(IReadOnlyList<Point2D> ring) => SignedArea(ring) > 0;

    /// <summary>
    /// Crossing-number test with the half-open rule on Y, so a point never counts a vertex twice.
    /// Points exactly on the ring are undefined by design: the solver keeps its representative
    /// points away from boundaries rather than relying on a tie-break here.
    /// </summary>
    public static bool ContainsPoint(IReadOnlyList<Point2D> ring, Point2D point)
    {
        if (ring is null) throw new ArgumentNullException(nameof(ring));
        if (ring.Count < 3) return false;

        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Y > point.Y) == (b.Y > point.Y)) continue;

            var x = a.X + ((point.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
            if (point.X < x) inside = !inside;
        }

        return inside;
    }

    /// <summary>
    /// A point strictly inside <paramref name="outer"/> and outside every hole: what the Region
    /// Editor clicks on and what nesting tests are measured from.
    /// </summary>
    /// <remarks>
    /// The centroid is tried first because it is the obvious answer for the convex rooms that make
    /// up most plans. When the face is L-shaped or has a hole the centroid can fall outside, so the
    /// fallback scans horizontal bands: each band's Y sits strictly between two vertex heights, so
    /// the scan line never runs through a vertex and the crossings come in clean pairs. The first
    /// band midpoint that passes wins, which keeps the answer identical from run to run.
    /// </remarks>
    public static bool TryFindInteriorPoint(
        IReadOnlyList<Point2D> outer,
        IEnumerable<IReadOnlyList<Point2D>>? holes,
        out Point2D point)
    {
        if (outer is null) throw new ArgumentNullException(nameof(outer));

        var holeRings = (holes ?? Array.Empty<IReadOnlyList<Point2D>>()).Where(h => h is not null).ToList();
        point = outer.Count > 0 ? outer[0] : new Point2D(0, 0);
        if (outer.Count < 3) return false;

        var centroid = new Point2D(outer.Average(p => p.X), outer.Average(p => p.Y));
        if (IsInterior(outer, holeRings, centroid))
        {
            point = centroid;
            return true;
        }

        var levels = outer.Select(p => p.Y)
            .Concat(holeRings.SelectMany(h => h.Select(p => p.Y)))
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        for (var i = 0; i + 1 < levels.Count; i++)
        {
            var y = (levels[i] + levels[i + 1]) / 2.0;
            var crossings = CrossingsAt(outer, y).Concat(holeRings.SelectMany(h => CrossingsAt(h, y)))
                .OrderBy(x => x)
                .ToList();

            for (var k = 0; k + 1 < crossings.Count; k++)
            {
                var candidate = new Point2D((crossings[k] + crossings[k + 1]) / 2.0, y);
                if (!IsInterior(outer, holeRings, candidate)) continue;
                point = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool IsInterior(IReadOnlyList<Point2D> outer, List<IReadOnlyList<Point2D>> holes, Point2D candidate) =>
        ContainsPoint(outer, candidate) && !holes.Any(h => ContainsPoint(h, candidate));

    private static IEnumerable<double> CrossingsAt(IReadOnlyList<Point2D> ring, double y)
    {
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Y > y) == (b.Y > y)) continue;
            yield return a.X + ((y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
        }
    }
}
