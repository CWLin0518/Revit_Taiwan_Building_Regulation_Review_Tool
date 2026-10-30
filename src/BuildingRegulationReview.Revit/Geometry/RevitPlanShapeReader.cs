using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Revit.Geometry;

// Turns Revit curves and solids into plan polylines. This is the only place that knows about XYZ;
// past it everything is Point2D in host feet, which is what PlanGeometryBuilder expects.
internal static class RevitPlanShapeReader
{
    private static readonly Options GeometryOptions = new Options
    {
        DetailLevel = ViewDetailLevel.Medium,
        ComputeReferences = false,
        IncludeNonVisibleObjects = false
    };

    public static Point2D ToPlan(XYZ point) => new Point2D(point.X, point.Y);

    /// <summary>Straight curves stay two points; arcs and splines come back as their tessellation.</summary>
    public static IReadOnlyList<Point2D> Flatten(Curve curve)
    {
        if (curve is null) throw new ArgumentNullException(nameof(curve));

        if (curve is Line line)
            return new[] { ToPlan(line.GetEndPoint(0)), ToPlan(line.GetEndPoint(1)) };

        return curve.Tessellate().Select(ToPlan).ToArray();
    }

    /// <summary>
    /// The column's plan silhouette: the solid projected down the Z axis, which is what blocks a
    /// boundary regardless of where the view's cut plane sits inside the column.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Point2D>> ReadPlanOutlines(Element element, double planeElevationFeet)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));

        var outlines = new List<IReadOnlyList<Point2D>>();
        var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, planeElevationFeet));

        foreach (var solid in ReadSolids(element.get_Geometry(GeometryOptions)))
        {
            var analyzer = ExtrusionAnalyzer.Create(solid, plane, XYZ.BasisZ);
            foreach (CurveLoop loop in analyzer.GetExtrusionBase().GetEdgesAsCurveLoops())
            {
                var points = FlattenLoop(loop);
                if (points.Count >= 3) outlines.Add(points);
            }
        }

        return outlines;
    }

    /// <summary>
    /// Axis-aligned stand-in for a column whose solid cannot be analysed. It over-covers a rotated
    /// or round column, so the caller reports it instead of passing it off as the real outline.
    /// </summary>
    public static IReadOnlyList<Point2D>? ReadBoundingRectangle(Element element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));

        var box = element.get_BoundingBox(null);
        if (box is null) return null;

        var min = box.Transform.OfPoint(box.Min);
        var max = box.Transform.OfPoint(box.Max);
        var minX = Math.Min(min.X, max.X);
        var minY = Math.Min(min.Y, max.Y);
        var maxX = Math.Max(min.X, max.X);
        var maxY = Math.Max(min.Y, max.Y);

        return new[]
        {
            new Point2D(minX, minY),
            new Point2D(maxX, minY),
            new Point2D(maxX, maxY),
            new Point2D(minX, maxY)
        };
    }

    /// <summary>Plan extent of an element such as a scope box, in host coordinates.</summary>
    public static PlanExtent2D? ReadPlanExtent(BoundingBoxXYZ? box)
    {
        if (box is null) return null;

        var corners = Corners(box).ToArray();
        return new PlanExtent2D(
            new Point2D(corners.Min(p => p.X), corners.Min(p => p.Y)),
            new Point2D(corners.Max(p => p.X), corners.Max(p => p.Y)));
    }

    // A crop box carries its own transform, so the eight transformed corners - not Min and Max -
    // define the extent of a rotated crop region.
    private static IEnumerable<XYZ> Corners(BoundingBoxXYZ box)
    {
        var transform = box.Transform ?? Transform.Identity;
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            yield return transform.OfPoint(new XYZ(x, y, z));
    }

    private static IReadOnlyList<Point2D> FlattenLoop(CurveLoop loop)
    {
        var points = new List<Point2D>();
        foreach (Curve curve in loop)
        {
            var flattened = Flatten(curve);
            // The loop is continuous, so each curve contributes everything but its closing point.
            for (var i = 0; i < flattened.Count - 1; i++) points.Add(flattened[i]);
        }

        return points;
    }

    private static IEnumerable<Solid> ReadSolids(GeometryElement? geometry)
    {
        if (geometry is null) yield break;

        foreach (GeometryObject item in geometry)
        {
            if (item is Solid solid)
            {
                if (solid.Volume > 0 && solid.Faces.Size > 0) yield return solid;
            }
            else if (item is GeometryInstance instance)
            {
                foreach (var nested in ReadSolids(instance.GetInstanceGeometry())) yield return nested;
            }
        }
    }
}
