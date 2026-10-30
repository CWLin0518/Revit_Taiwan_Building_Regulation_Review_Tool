using System;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Geometry;

// The plane geometry the repair pipeline needs, in one place so every step measures the same way.
// All parameters are normalized to [0, 1] along the segment they belong to, and all distances are
// decimal feet, matching GeometryTolerance.
public static class SegmentGeometry
{
    /// <summary>Direction measured from +X and normalized to [0, pi), so a line and its reverse
    /// compare equal. Mirrors <see cref="Segment2D.DirectionRadians"/> for loose point pairs.</summary>
    public static double DirectionRadians(Point2D start, Point2D end)
    {
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        if (angle < 0) angle += Math.PI;
        if (angle >= Math.PI) angle -= Math.PI;
        return angle;
    }

    public static Point2D PointAt(Point2D start, Point2D end, double t) =>
        new Point2D(start.X + ((end.X - start.X) * t), start.Y + ((end.Y - start.Y) * t));

    /// <summary>Signed area of the triangle (a, b, p) times two. Sign tells which side p is on.</summary>
    public static double Cross(Point2D a, Point2D b, Point2D p) =>
        ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));

    /// <summary>
    /// Distance from <paramref name="point"/> to the segment, with the closest point and its
    /// parameter. A degenerate segment reports its start.
    /// </summary>
    public static double DistanceToSegment(Point2D point, Point2D start, Point2D end, out double t, out Point2D closest)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= GeometryTolerance.ZeroLengthFeet * GeometryTolerance.ZeroLengthFeet)
        {
            t = 0;
            closest = start;
            return point.DistanceTo(start);
        }

        t = (((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / lengthSquared;
        if (t < 0) t = 0;
        else if (t > 1) t = 1;

        closest = PointAt(start, end, t);
        return point.DistanceTo(closest);
    }

    /// <summary>Perpendicular distance to the infinite line through the two points.</summary>
    public static double DistanceToLine(Point2D point, Point2D start, Point2D end)
    {
        var length = start.DistanceTo(end);
        if (length <= GeometryTolerance.ZeroLengthFeet) return point.DistanceTo(start);
        return Math.Abs(Cross(start, end, point)) / length;
    }

    /// <summary>Position of a point along the direction start-&gt;end, in feet from start.</summary>
    public static double ProjectOntoAxis(Point2D point, Point2D start, Point2D end)
    {
        var length = start.DistanceTo(end);
        if (length <= GeometryTolerance.ZeroLengthFeet) return 0;
        return ((((point.X - start.X) * (end.X - start.X)) + ((point.Y - start.Y) * (end.Y - start.Y)))) / length;
    }

    /// <summary>
    /// Crossing point of two segments. <paramref name="toleranceFeet"/> lets an endpoint overshoot
    /// or fall short of the other segment and still count, which is how a T-junction drawn a few
    /// millimetres off still registers. Parallel and collinear pairs report no intersection: an
    /// overlap is a duplicate, and duplicate removal already handled it.
    /// </summary>
    public static bool TryIntersect(
        Point2D aStart,
        Point2D aEnd,
        Point2D bStart,
        Point2D bEnd,
        double toleranceFeet,
        out double tA,
        out double tB,
        out Point2D point)
    {
        tA = 0;
        tB = 0;
        point = aStart;

        var aDx = aEnd.X - aStart.X;
        var aDy = aEnd.Y - aStart.Y;
        var bDx = bEnd.X - bStart.X;
        var bDy = bEnd.Y - bStart.Y;

        var denominator = (aDx * bDy) - (aDy * bDx);
        var lengthA = Math.Sqrt((aDx * aDx) + (aDy * aDy));
        var lengthB = Math.Sqrt((bDx * bDx) + (bDy * bDy));
        if (lengthA <= GeometryTolerance.ZeroLengthFeet || lengthB <= GeometryTolerance.ZeroLengthFeet) return false;

        // The denominator is |a||b|sin(theta); comparing it against the tolerance scaled by both
        // lengths tests the angle rather than the raw number, so long lines are not misjudged.
        if (Math.Abs(denominator) <= GeometryTolerance.ZeroLengthFeet * lengthA * lengthB) return false;

        var ex = bStart.X - aStart.X;
        var ey = bStart.Y - aStart.Y;
        tA = ((ex * bDy) - (ey * bDx)) / denominator;
        tB = ((ex * aDy) - (ey * aDx)) / denominator;

        var slackA = toleranceFeet / lengthA;
        var slackB = toleranceFeet / lengthB;
        if (tA < -slackA || tA > 1 + slackA) return false;
        if (tB < -slackB || tB > 1 + slackB) return false;

        // Clamp before reporting so the point always sits on both segments, never just past an end.
        tA = Clamp01(tA);
        tB = Clamp01(tB);
        point = PointAt(aStart, aEnd, tA);
        return true;
    }

    /// <summary>
    /// First hit of a ray against a segment, used by the constrained gap extension: the dangling
    /// end may only travel along its own direction, never sideways.
    /// </summary>
    public static bool TryRayHitSegment(
        Point2D origin,
        double directionX,
        double directionY,
        double maxDistanceFeet,
        Point2D start,
        Point2D end,
        out double distanceFeet,
        out double tOnTarget,
        out Point2D hit)
    {
        distanceFeet = 0;
        tOnTarget = 0;
        hit = origin;

        var far = new Point2D(origin.X + (directionX * maxDistanceFeet), origin.Y + (directionY * maxDistanceFeet));
        if (origin.DistanceTo(far) <= GeometryTolerance.ZeroLengthFeet) return false;

        if (!TryIntersect(origin, far, start, end, 0, out var tRay, out var tTarget, out var point)) return false;

        distanceFeet = tRay * maxDistanceFeet;
        tOnTarget = tTarget;
        hit = point;
        return true;
    }

    /// <summary>
    /// Whether two segments are the same line drawn twice: parallel within the collinear tolerance,
    /// laterally apart by no more than <paramref name="lateralToleranceFeet"/>, and genuinely
    /// overlapping rather than merely touching end to end (that case belongs to collinear merging,
    /// which knows whether the shared point is also a junction).
    /// </summary>
    public static bool TryMeasureCollinearOverlap(
        Point2D aStart,
        Point2D aEnd,
        Point2D bStart,
        Point2D bEnd,
        GeometryTolerance tolerance,
        double lateralToleranceFeet,
        out double overlapFeet,
        out double lateralDistanceFeet)
    {
        if (tolerance is null) throw new ArgumentNullException(nameof(tolerance));

        overlapFeet = 0;
        lateralDistanceFeet = 0;

        if (!tolerance.AreCollinearDirections(DirectionRadians(aStart, aEnd), DirectionRadians(bStart, bEnd))) return false;

        var lateralStart = DistanceToLine(bStart, aStart, aEnd);
        var lateralEnd = DistanceToLine(bEnd, aStart, aEnd);
        lateralDistanceFeet = Math.Max(lateralStart, lateralEnd);
        if (lateralDistanceFeet > lateralToleranceFeet) return false;

        var aLength = aStart.DistanceTo(aEnd);
        var first = ProjectOntoAxis(bStart, aStart, aEnd);
        var second = ProjectOntoAxis(bEnd, aStart, aEnd);
        var low = Math.Min(first, second);
        var high = Math.Max(first, second);

        overlapFeet = Math.Min(aLength, high) - Math.Max(0, low);
        return overlapFeet > lateralToleranceFeet;
    }

    private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;
}
