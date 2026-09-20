using System;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Geometry;

public class LinkGeometryPolicyTests
{
    private static readonly BasisVector UnitZ = new BasisVector(0, 0, 1);
    private static readonly BasisVector Origin = new BasisVector(0, 0, 0);

    [Fact]
    public void AcceptsAnUnrotatedLinkAsAnIdentityPlanTransform()
    {
        var result = LinkGeometryPolicy.TryCreateTransform(
            new BasisVector(1, 0, 0), new BasisVector(0, 1, 0), UnitZ, Origin);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsIdentity);
    }

    [Fact]
    public void ReadsRotationAndTranslationFromTheBasis()
    {
        var angle = Math.PI / 6.0;
        var result = LinkGeometryPolicy.TryCreateTransform(
            new BasisVector(Math.Cos(angle), Math.Sin(angle), 0),
            new BasisVector(-Math.Sin(angle), Math.Cos(angle), 0),
            UnitZ,
            new BasisVector(12.5, -4.0, 9.0));

        Assert.True(result.IsSuccess);
        Assert.Equal(angle, result.Value.RotationRadians, 9);
        Assert.Equal(12.5, result.Value.Translation.X, 9);
        Assert.Equal(-4.0, result.Value.Translation.Y, 9);
    }

    [Fact]
    public void MovesAPointTheSameWayTheLinkTransformWould()
    {
        var result = LinkGeometryPolicy.TryCreateTransform(
            new BasisVector(0, 1, 0), new BasisVector(-1, 0, 0), UnitZ, new BasisVector(10, 20, 0));

        Assert.True(result.IsSuccess);
        var moved = result.Value.Apply(new Point2D(3, 0));
        Assert.Equal(10.0, moved.X, 9);
        Assert.Equal(23.0, moved.Y, 9);
    }

    [Fact]
    public void RejectsAMirroredLink()
    {
        var result = LinkGeometryPolicy.TryCreateTransform(
            new BasisVector(1, 0, 0), new BasisVector(0, -1, 0), UnitZ, Origin);

        Assert.True(result.IsFailure);
        Assert.Equal("geometry.link.mirrored", result.Error.Code);
    }

    [Fact]
    public void RejectsAScaledLink()
    {
        var result = LinkGeometryPolicy.TryCreateTransform(
            new BasisVector(2, 0, 0), new BasisVector(0, 2, 0), UnitZ, Origin);

        Assert.True(result.IsFailure);
        Assert.Equal("geometry.link.scaled", result.Error.Code);
    }

    [Fact]
    public void RejectsALinkTiltedOutOfThePlane()
    {
        var angle = Math.PI / 4.0;
        var result = LinkGeometryPolicy.TryCreateTransform(
            new BasisVector(1, 0, 0),
            new BasisVector(0, Math.Cos(angle), Math.Sin(angle)),
            new BasisVector(0, -Math.Sin(angle), Math.Cos(angle)),
            Origin);

        Assert.True(result.IsFailure);
        Assert.Equal("geometry.link.tilted", result.Error.Code);
    }

    [Fact]
    public void RejectsAnUpsideDownLink()
    {
        var result = LinkGeometryPolicy.TryCreateTransform(
            new BasisVector(1, 0, 0), new BasisVector(0, -1, 0), new BasisVector(0, 0, -1), Origin);

        Assert.True(result.IsFailure);
        Assert.Equal("geometry.link.tilted", result.Error.Code);
    }

    [Fact]
    public void RejectsANonOrthogonalBasis()
    {
        var result = LinkGeometryPolicy.TryCreateTransform(
            new BasisVector(1, 0, 0),
            new BasisVector(Math.Sqrt(0.5), Math.Sqrt(0.5), 0),
            UnitZ,
            Origin);

        Assert.True(result.IsFailure);
        Assert.Equal("geometry.link.skewed", result.Error.Code);
    }

    [Fact]
    public void ReportsTheOffendingBasisInTheTechnicalDetail()
    {
        var result = LinkGeometryPolicy.TryCreateTransform(
            new BasisVector(1, 0, 0), new BasisVector(0, -1, 0), UnitZ, Origin);

        Assert.Contains("basisY=(0, -1, 0)", result.Error.TechnicalDetail!);
    }
}
