using System;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.WriteBack;

/// <summary>
/// How a signature is spelled. Two callers depend on the spelling being one thing: the plan that
/// says what the model should contain, and the staleness probe that asks whether what is in the
/// model still matches what was written on it (spec 13.1).
/// </summary>
public class PlannedElementSignatureTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ZoneId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TwoPointsWithinTheClosureToleranceSpellTheSameSignature()
    {
        var quantum = Tolerance.ClosureFeet;

        // Float noise from a re-solve must not read as an edit, or every re-run would report updates.
        Assert.Equal(
            PlannedElementSignature.ForPoint(new Point2D(10.0, 4.0), quantum),
            PlannedElementSignature.ForPoint(new Point2D(10.0 + 1e-9, 4.0 - 1e-9), quantum));
    }

    [Fact]
    public void AMoveBiggerThanTheClosureToleranceChangesTheSignature()
    {
        var quantum = Tolerance.ClosureFeet;

        Assert.NotEqual(
            PlannedElementSignature.ForPoint(new Point2D(10.0, 4.0), quantum),
            PlannedElementSignature.ForPoint(new Point2D(10.02, 4.0), quantum));
    }

    [Fact]
    public void ASegmentReadBackTheOtherWayRoundStillMatchesWhatWasWritten()
    {
        // Revit stores a curve in whatever direction it was created; the probe has to normalize the
        // same way the plan orders its outline or every line would look changed.
        var a = new Point2D(0, 0);
        var b = new Point2D(10, 0);
        var quantum = Tolerance.ClosureFeet;

        Assert.Equal(
            PlannedElementSignature.ForSegment(a, b, quantum),
            PlannedElementSignature.ForCanonicalSegment(b, a, quantum));
    }

    [Fact]
    public void AVerticalSegmentIsPointedUpwardsSoItsOrderDoesNotDependOnWhoTracedIt()
    {
        var quantum = Tolerance.ClosureFeet;

        Assert.True(PlannedElementSignature.IsCanonical(new Point2D(5, 0), new Point2D(5, 10)));
        Assert.False(PlannedElementSignature.IsCanonical(new Point2D(5, 10), new Point2D(5, 0)));
        Assert.Equal(
            PlannedElementSignature.ForCanonicalSegment(new Point2D(5, 0), new Point2D(5, 10), quantum),
            PlannedElementSignature.ForCanonicalSegment(new Point2D(5, 10), new Point2D(5, 0), quantum));
    }

    [Fact]
    public void ATighterToleranceChangesEverySignature()
    {
        // Which is what makes spec 13.1's 幾何容差 change visible without storing the tolerance:
        // the elements in the model simply stop matching what the tool would write now.
        var point = new Point2D(10.0, 4.0);

        Assert.NotEqual(
            PlannedElementSignature.ForPoint(point, Tolerance.ClosureFeet),
            PlannedElementSignature.ForPoint(point, Tolerance.ClosureFeet / 4.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void RefusesAQuantumThatIsNotAPositiveNumber(double quantum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlannedElementSignature.ForPoint(new Point2D(1, 1), quantum));
    }

    [Fact]
    public void TheZoneNameCanBeReadBackOutOfAnAreaSignature()
    {
        var signature = PlannedElementSignature.ForArea("A 區", "#112233", new Point2D(5, 5), Tolerance.ClosureFeet);

        Assert.Equal("A 區", PlannedElementSignature.ZoneNameOf(signature));
    }

    [Fact]
    public void TheNameAndColourCanBeReadBackEvenWhenTheNameHoldsTheSeparator()
    {
        var signature = PlannedElementSignature.ForArea("A|B 區", "#112233", new Point2D(5, 5), Tolerance.ClosureFeet);

        Assert.True(PlannedElementSignature.TryReadArea(signature, out var name, out var hex));
        Assert.Equal("A|B 區", name);
        Assert.Equal("#112233", hex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0,0>10,0")]
    [InlineData("A|0,0")]
    public void ASignatureThatIsNotAnAreasDoesNotReadAsOne(string? signature)
    {
        Assert.False(PlannedElementSignature.TryReadArea(signature, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0,0>10,0")]
    public void ASignatureThatIsNotAnAreasHasNoZoneNameInIt(string? signature)
    {
        Assert.Null(PlannedElementSignature.ZoneNameOf(signature));
    }

    [Fact]
    public void ThePlanSpellsItsBoundarySignaturesExactlyThisWay()
    {
        // The probe compares against what the plan wrote, so the two spellings being one thing is
        // the property that matters, not the spelling itself.
        var map = Solve();
        var zones = Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            ZoneId, "A", ZoneColorPalette.At(0), new[] { map.FaceAt(new Point2D(5, 5))!.Id })));

        var boundary = ZoneWritePlan.Build(PackageId, map, zones, Tolerance)
            .First(e => e.Key.Kind == ManagedElementKind.AreaBoundaryLine);

        Assert.Equal(
            PlannedElementSignature.ForCanonicalSegment(boundary.Points[0], boundary.Points[1], Tolerance.ClosureFeet),
            boundary.Signature);
    }

    [Fact]
    public void ThePlanSpellsItsAreaSignaturesExactlyThisWayToo()
    {
        var map = Solve();
        var zones = Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            ZoneId, "A", ZoneColorPalette.At(0), new[] { map.FaceAt(new Point2D(5, 5))!.Id })));

        var area = ZoneWritePlan.Build(PackageId, map, zones, Tolerance)
            .First(e => e.Key.Kind == ManagedElementKind.Area);

        Assert.Equal(
            PlannedElementSignature.ForArea(
                "A", ZoneColorPalette.At(0).ToHex(), area.Placement!.Value, Tolerance.ClosureFeet),
            area.Signature);
        Assert.Equal("A", PlannedElementSignature.ZoneNameOf(area.Signature));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static PlanRegionMap Solve()
    {
        var segments = new[]
        {
            Seg(0, 0, 10, 0, "S"),
            Seg(10, 0, 10, 10, "E"),
            Seg(10, 10, 0, 10, "N"),
            Seg(0, 10, 0, 0, "W")
        };

        var snapshot = new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance);
        var network = Succeeds(new LineNetworkRepairer().Repair(snapshot, SolvedAt));
        return Succeeds(new RegionSolver().Solve(network, SolvedAt));
    }

    private static Segment2D Seg(double x1, double y1, double x2, double y2, string element) =>
        new Segment2D(new Point2D(x1, y1), new Point2D(x2, y2), new SourceRef("doc", element, GeometrySourceKind.WallCenterline));

    private static T Succeeds<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }
}
