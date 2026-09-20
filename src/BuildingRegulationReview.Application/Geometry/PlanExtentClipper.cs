using System;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Geometry;

// Liang-Barsky clip of a plan segment against the extraction extent (spec 10.1: the scope is the
// Area Plan's crop region or scope box). A wall that crosses the boundary is cut at the boundary
// rather than dropped, so the part inside the view still takes part in loop detection.
public static class PlanExtentClipper
{
    public static bool TryClip(
        Point2D start,
        Point2D end,
        PlanExtent2D extent,
        double toleranceFeet,
        out Point2D clippedStart,
        out Point2D clippedEnd)
    {
        if (extent is null) throw new ArgumentNullException(nameof(extent));
        if (double.IsNaN(toleranceFeet) || toleranceFeet < 0) throw new ArgumentOutOfRangeException(nameof(toleranceFeet));

        var minX = extent.Minimum.X - toleranceFeet;
        var minY = extent.Minimum.Y - toleranceFeet;
        var maxX = extent.Maximum.X + toleranceFeet;
        var maxY = extent.Maximum.Y + toleranceFeet;

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var enter = 0.0;
        var exit = 1.0;

        if (Accept(-dx, start.X - minX, ref enter, ref exit) &&
            Accept(dx, maxX - start.X, ref enter, ref exit) &&
            Accept(-dy, start.Y - minY, ref enter, ref exit) &&
            Accept(dy, maxY - start.Y, ref enter, ref exit))
        {
            clippedStart = new Point2D(start.X + (enter * dx), start.Y + (enter * dy));
            clippedEnd = new Point2D(start.X + (exit * dx), start.Y + (exit * dy));
            return true;
        }

        clippedStart = start;
        clippedEnd = end;
        return false;
    }

    // p == 0 means the segment runs parallel to this edge: it is only outside when q says so,
    // otherwise the edge places no limit on the parameter range.
    private static bool Accept(double p, double q, ref double enter, ref double exit)
    {
        if (p == 0.0) return q >= 0.0;

        var t = q / p;
        if (p < 0.0)
        {
            if (t > exit) return false;
            if (t > enter) enter = t;
        }
        else
        {
            if (t < enter) return false;
            if (t < exit) exit = t;
        }

        return true;
    }
}
