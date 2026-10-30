using System;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.Geometry;

/// <summary>One basis vector or origin of a link instance's transform, in host decimal feet.</summary>
public readonly struct BasisVector
{
    public BasisVector(double x, double y, double z)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (double.IsNaN(y) || double.IsInfinity(y)) throw new ArgumentOutOfRangeException(nameof(y));
        if (double.IsNaN(z) || double.IsInfinity(z)) throw new ArgumentOutOfRangeException(nameof(z));
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));
    public double Dot(BasisVector other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);
    public override string ToString() => $"({X:R}, {Y:R}, {Z:R})";
}

// A link only contributes reference geometry (spec 10.1), so its placement has to reduce to a Z
// rotation plus a translation without losing information. Mirrored, scaled or tilted links are
// rejected here and skipped with a warning rather than flattened into a plausible-looking wrong
// answer — a boundary drawn from a mirrored plan would pass every later check silently.
public static class LinkGeometryPolicy
{
    /// <summary>Unit-length and orthogonality slack; Revit's own transforms are exact to well within this.</summary>
    public const double BasisTolerance = 1.0e-9;

    public static Result<PlanTransform2D> TryCreateTransform(
        BasisVector basisX,
        BasisVector basisY,
        BasisVector basisZ,
        BasisVector origin)
    {
        if (Math.Abs(basisX.Length - 1.0) > BasisTolerance || Math.Abs(basisY.Length - 1.0) > BasisTolerance ||
            Math.Abs(basisZ.Length - 1.0) > BasisTolerance)
        {
            return Reject("geometry.link.scaled", "連結模型含縮放，平面座標無法直接對應主體專案。", basisX, basisY, basisZ);
        }

        if (Math.Abs(basisX.Dot(basisY)) > BasisTolerance)
            return Reject("geometry.link.skewed", "連結模型的座標軸不正交，無法轉換為平面剛體轉換。", basisX, basisY, basisZ);

        // A tilted link would project walls to the wrong plan length instead of failing outright.
        if (Math.Abs(basisZ.X) > BasisTolerance || Math.Abs(basisZ.Y) > BasisTolerance || basisZ.Z <= 0)
            return Reject("geometry.link.tilted", "連結模型有非水平的旋轉，平面投影會失真。", basisX, basisY, basisZ);

        // Right-handed placements keep the plan's handedness; a negative 2D determinant is a mirror.
        var determinant = (basisX.X * basisY.Y) - (basisX.Y * basisY.X);
        if (determinant <= 0)
            return Reject("geometry.link.mirrored", "連結模型為鏡射放置，平面方向與主體專案相反。", basisX, basisY, basisZ);

        var rotation = Math.Atan2(basisX.Y, basisX.X);
        return Result.Success(new PlanTransform2D(new Point2D(origin.X, origin.Y), rotation));
    }

    private static Result<PlanTransform2D> Reject(string code, string message, BasisVector x, BasisVector y, BasisVector z) =>
        Result.Failure<PlanTransform2D>(new Error(code, message, $"basisX={x}; basisY={y}; basisZ={z}"));
}
