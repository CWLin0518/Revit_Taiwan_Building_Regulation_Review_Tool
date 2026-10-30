using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.RegionEditing;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.RegionEditing;

public class WrittenZoneRestorerTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ZoneA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ZoneB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ZoneColor Red = ZoneColor.FromHex("#CC3333");
    private static readonly ZoneColor Blue = ZoneColor.FromHex("#3355CC");

    [Fact]
    public void PutsBackAZoneWithItsIdNameColourAndTheFacesItsAreaCovers()
    {
        var map = ThreeRooms();

        var restored = WrittenZoneRestorer.Restore(map, new[] { Written(ZoneA, "A 區", Red, Box(0, 0, 20, 10)) });

        var zone = Assert.Single(restored.Zones.Zones);
        Assert.Equal(ZoneA, zone.Id);
        Assert.Equal("A 區", zone.Name);
        Assert.Equal(Red, zone.Color);
        Assert.Equal(new[] { FaceAt(map, 5, 5), FaceAt(map, 15, 5) }.OrderBy(x => x), zone.FaceIds);
        Assert.False(zone.AllowsDisjointParts);
        Assert.Empty(restored.Warnings);
    }

    [Fact]
    public void AZoneWrittenAsSeparatePartsComesBackAllowedToBeDisjoint()
    {
        var map = ThreeRooms();

        var restored = WrittenZoneRestorer.Restore(map, new[]
        {
            Written(ZoneA, "A 區", Red, Box(0, 0, 10, 10)),
            Written(ZoneA, "A 區", Red, Box(20, 0, 30, 10))
        });

        var zone = Assert.Single(restored.Zones.Zones);
        Assert.Equal(2, zone.FaceCount);
        Assert.True(zone.AllowsDisjointParts);
    }

    [Fact]
    public void AnUnenclosedAreaFallsBackToTheFaceUnderItsPlacement()
    {
        var map = ThreeRooms();

        var restored = WrittenZoneRestorer.Restore(map, new[]
        {
            new WrittenZoneArea(ZoneA, "A 區", Red, null, new Point2D(25, 5))
        });

        Assert.Equal(new[] { FaceAt(map, 25, 5) }, Assert.Single(restored.Zones.Zones).FaceIds);
    }

    [Fact]
    public void AZoneWhoseAreaNoLongerCoversAnyFaceIsReportedNotInvented()
    {
        var restored = WrittenZoneRestorer.Restore(ThreeRooms(), new[] { Written(ZoneA, "A 區", Red, Box(100, 100, 110, 110)) });

        Assert.True(restored.Zones.IsEmpty);
        Assert.Contains("A 區", Assert.Single(restored.Warnings));
    }

    [Fact]
    public void AFaceCoveredByTwoZonesStaysWithOnlyOne()
    {
        var map = ThreeRooms();

        var restored = WrittenZoneRestorer.Restore(map, new[]
        {
            Written(ZoneA, "A 區", Red, Box(0, 0, 20, 10)),
            Written(ZoneB, "B 區", Blue, Box(10, 0, 30, 10))
        });

        var middle = FaceAt(map, 15, 5);
        Assert.Equal(ZoneA, restored.Zones.ZoneOf(middle)!.Id);
        Assert.Equal(new[] { FaceAt(map, 25, 5) }, restored.Zones.Zone(ZoneB)!.FaceIds);
    }

    [Fact]
    public void TwoZonesWithTheSameNameAreKeptApart()
    {
        var restored = WrittenZoneRestorer.Restore(ThreeRooms(), new[]
        {
            Written(ZoneA, "區劃", Red, Box(0, 0, 10, 10)),
            Written(ZoneB, "區劃", Blue, Box(20, 0, 30, 10))
        });

        Assert.Equal(new[] { "區劃", "區劃 (2)" }, restored.Zones.Zones.Select(z => z.Name));
    }

    [Fact]
    public void ASessionOpenedOnRestoredZonesHasNothingUnapplied()
    {
        var map = ThreeRooms();
        var restored = WrittenZoneRestorer.Restore(map, new[] { Written(ZoneA, "A 區", Red, Box(0, 0, 20, 10)) });

        var session = new RegionEditorSession(map, new ScreenSize(800, 600), initialZones: restored.Zones);

        Assert.Single(session.Zones.Zones);
        Assert.False(session.HasUnappliedChanges);
        Assert.False(session.CanUndo);
    }

    private static WrittenZoneArea Written(Guid zoneId, string name, ZoneColor color, IReadOnlyList<Point2D> outline) =>
        new WrittenZoneArea(zoneId, name, color, new[] { outline }, null);

    private static IReadOnlyList<Point2D> Box(double x1, double y1, double x2, double y2) => new[]
    {
        new Point2D(x1, y1), new Point2D(x2, y1), new Point2D(x2, y2), new Point2D(x1, y2)
    };

    private static int FaceAt(PlanRegionMap map, double x, double y) => map.FaceAt(new Point2D(x, y))!.Id;

    // Three 10 x 10 rooms in a row.
    private static PlanRegionMap ThreeRooms()
    {
        var segments = new[]
        {
            Seg(0, 0, 30, 0, "S"),
            Seg(30, 0, 30, 10, "E"),
            Seg(30, 10, 0, 10, "N"),
            Seg(0, 10, 0, 0, "W"),
            Seg(10, 0, 10, 10, "D1"),
            Seg(20, 0, 20, 10, "D2")
        };

        var snapshot = new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, GeometryTolerance.Default);
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
