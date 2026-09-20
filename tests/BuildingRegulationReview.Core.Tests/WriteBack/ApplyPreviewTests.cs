using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.WriteBack;

public class ApplyPreviewTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid OtherPackageId = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    // ---- the ownership token ------------------------------------------------------------------

    [Fact]
    public void AKeySurvivesBeingWrittenOutAndReadBack()
    {
        var key = new ManagedElementKey(PackageId, Guid.NewGuid(), ManagedElementKind.Area, 2, 7);

        Assert.True(ManagedElementKey.TryParse(key.ToToken(), out var parsed));
        Assert.Equal(key, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AreaBoundary")]
    [InlineData("OTHER/11111111222233334444555555555555/x/area/0/0")]
    [InlineData("BCR/not-a-guid/11111111222233334444555555555555/area/0/0")]
    [InlineData("BCR/11111111222233334444555555555555/11111111222233334444555555555555/sofa/0/0")]
    public void AForeignOrBrokenTokenIsNotAKey(string token)
    {
        Assert.False(ManagedElementKey.TryParse(token, out _));
    }

    // ---- what a draft plans ---------------------------------------------------------------------

    [Fact]
    public void PlansOneAreaOneTagAndTheFourWallsOfASingleRoom()
    {
        var (map, zones) = TwoRooms(left: true, right: false);

        var preview = ApplyPreview.Build(PackageId, map, zones);

        Assert.Equal(4, preview.CountOf(ManagedElementKind.AreaBoundaryLine, ApplyChangeKind.Add));
        Assert.Equal(4, preview.CountOf(ManagedElementKind.DetailCurve, ApplyChangeKind.Add));
        Assert.Equal(1, preview.CountOf(ManagedElementKind.Area, ApplyChangeKind.Add));
        Assert.Equal(1, preview.CountOf(ManagedElementKind.AreaTag, ApplyChangeKind.Add));
        Assert.All(preview.Items, item => Assert.Equal(ApplyChangeKind.Add, item.Change));
    }

    [Fact]
    public void DropsTheWallBetweenTwoRoomsOfTheSameZone()
    {
        var (map, zones) = TwoRooms(left: true, right: true);

        var preview = ApplyPreview.Build(PackageId, map, zones);

        // The 20x10 outline, split at the junctions the divider made, is six segments; the divider
        // itself is interior to the zone and is not drawn.
        Assert.Equal(6, preview.CountOf(ManagedElementKind.AreaBoundaryLine, ApplyChangeKind.Add));
        Assert.Equal(1, preview.CountOf(ManagedElementKind.Area, ApplyChangeKind.Add));
    }

    [Fact]
    public void KeepsAHoleThatIsNotPartOfTheZone()
    {
        var map = Solve(Rectangle(0, 0, 20, 20, "OUTER").Concat(Rectangle(5, 5, 5, 5, "ISLAND")));
        var zones = Assign(map, FaceAt(map, 1, 1));

        var preview = ApplyPreview.Build(PackageId, map, zones);

        Assert.Equal(8, preview.CountOf(ManagedElementKind.AreaBoundaryLine, ApplyChangeKind.Add));
    }

    [Fact]
    public void ClosesTheHoleWhenTheIslandJoinsTheSameZone()
    {
        var map = Solve(Rectangle(0, 0, 20, 20, "OUTER").Concat(Rectangle(5, 5, 5, 5, "ISLAND")));
        var zones = Assign(map, FaceAt(map, 1, 1), FaceAt(map, 7, 7));

        var preview = ApplyPreview.Build(PackageId, map, zones);

        Assert.Equal(4, preview.CountOf(ManagedElementKind.AreaBoundaryLine, ApplyChangeKind.Add));
    }

    [Fact]
    public void GivesEachBlockThatDoesNotTouchItsOwnArea()
    {
        var map = Solve(Rectangle(0, 0, 10, 10, "LEFT").Concat(Rectangle(50, 0, 10, 10, "RIGHT")));
        var zones = Assign(map, FaceAt(map, 5, 5), FaceAt(map, 55, 5));

        var preview = ApplyPreview.Build(PackageId, map, zones);

        Assert.Equal(2, preview.CountOf(ManagedElementKind.Area, ApplyChangeKind.Add));
        Assert.Equal(2, preview.CountOf(ManagedElementKind.AreaTag, ApplyChangeKind.Add));
        Assert.Equal(8, preview.CountOf(ManagedElementKind.AreaBoundaryLine, ApplyChangeKind.Add));
        Assert.Contains("2 塊不相連", string.Join("\n", preview.Warnings));
    }

    // ---- diffing against what the model already holds --------------------------------------------

    [Fact]
    public void ASecondRunOfTheSameDraftChangesNothing()
    {
        var (map, zones) = TwoRooms(left: true, right: false);

        var preview = ApplyPreview.Build(PackageId, map, zones, AsWritten(map, zones));

        Assert.True(preview.IsEmpty);
        Assert.Empty(preview.Added);
        Assert.Empty(preview.Updated);
        Assert.Empty(preview.Deleted);
        Assert.Equal(10, preview.Unchanged.Count);
        Assert.Contains("不會變更任何元素", preview.Summary);
    }

    [Fact]
    public void RenamingAZoneUpdatesItsAreaAndTagButLeavesTheLinesAlone()
    {
        var (map, zones) = TwoRooms(left: true, right: false);
        var written = AsWritten(map, zones);
        var renamed = Succeeds(zones.Rename(zones.Zones.Single().Id, "B"));

        var preview = ApplyPreview.Build(PackageId, map, renamed, written);

        Assert.Equal(2, preview.Updated.Count);
        Assert.Equal(
            new[] { ManagedElementKind.Area, ManagedElementKind.AreaTag },
            preview.Updated.Select(i => i.Kind).OrderBy(k => k.ToString()).ToArray());
        Assert.Equal(4, preview.CountOf(ManagedElementKind.AreaBoundaryLine, ApplyChangeKind.Unchanged));
        Assert.Empty(preview.Deleted);
    }

    [Fact]
    public void DeletesWhatThisPackageWroteAndNoLongerNeeds()
    {
        var (map, zones) = TwoRooms(left: true, right: true);
        var written = AsWritten(map, zones);
        var shrunk = Succeeds(Succeeds(zones.Unassign(FaceAt(map, 15, 5))).Zones.SetDisjointAllowed(zones.Zones.Single().Id, false));

        var preview = ApplyPreview.Build(PackageId, map, shrunk, written);

        Assert.NotEmpty(preview.Deleted);
        Assert.All(preview.Deleted, item => Assert.Equal(PackageId, item.Key.PackageId));
        Assert.All(preview.Deleted, item => Assert.NotNull(item.ElementUniqueId));
    }

    [Fact]
    public void NeverTouchesAnElementThisPackageDoesNotOwn()
    {
        var (map, zones) = TwoRooms(left: true, right: false);
        var foreign = new[]
        {
            new ExistingManagedElement(
                "other-package",
                new ManagedElementKey(OtherPackageId, Guid.NewGuid(), ManagedElementKind.Area, 0, 0).ToToken(),
                "whatever"),
            new ExistingManagedElement("hand-drawn", "使用者自己畫的", string.Empty)
        };

        var preview = ApplyPreview.Build(PackageId, map, zones, AsWritten(map, zones).Concat(foreign));

        Assert.Empty(preview.Deleted);
        Assert.Equal(2, preview.UntouchedElementCount);
        Assert.Contains("未受管理 2 個", preview.Summary);
    }

    [Fact]
    public void WarnsAboutZonesThatWouldWriteNothing()
    {
        var map = Solve(Rectangle(0, 0, 10, 10, "OUTER"));
        var zones = Succeeds(ZoneDraftSet.Empty.Add(
            new ZoneDraft(Guid.NewGuid(), "空的", ZoneColorPalette.At(0))));

        var preview = ApplyPreview.Build(PackageId, map, zones);

        Assert.Empty(preview.Items);
        Assert.Contains("區劃「空的」還沒有任何範圍", string.Join("\n", preview.Warnings));
    }

    [Fact]
    public void SumsUpEveryKindForTheDialog()
    {
        var (map, zones) = TwoRooms(left: true, right: false);

        var preview = ApplyPreview.Build(PackageId, map, zones);

        Assert.Equal(4, preview.KindSummaries.Count);
        Assert.Contains("面積邊界線：新增 4", preview.KindSummaries[0]);
        Assert.Contains("將新增 10 個", preview.Summary);
        Assert.Equal(10, preview.ChangeCount);
    }

    // ---- helpers ----------------------------------------------------------------------------

    /// <summary>Turns a plan into what the model would hold after it was written, for a re-run.</summary>
    private static IReadOnlyList<ExistingManagedElement> AsWritten(PlanRegionMap map, ZoneDraftSet zones) =>
        ZoneWritePlan.Build(PackageId, map, zones)
            .Select((element, index) => new ExistingManagedElement(
                "element-" + index,
                element.Key.ToToken(),
                element.Signature,
                element.Description))
            .ToList();

    private static (PlanRegionMap Map, ZoneDraftSet Zones) TwoRooms(bool left, bool right)
    {
        var map = Solve(Rectangle(0, 0, 20, 10, "OUTER").Concat(new[] { Seg(10, 0, 10, 10, "DIVIDER") }));
        var faces = new List<int>();
        if (left) faces.Add(FaceAt(map, 5, 5));
        if (right) faces.Add(FaceAt(map, 15, 5));
        return (map, Assign(map, faces.ToArray()));
    }

    private static ZoneDraftSet Assign(PlanRegionMap map, params int[] faceIds) =>
        Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            "A",
            ZoneColorPalette.At(0),
            faceIds)));

    private static PlanRegionMap Solve(IEnumerable<Segment2D> segments)
    {
        var snapshot = new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance);
        var network = Succeeds(new LineNetworkRepairer().Repair(snapshot, SolvedAt));
        return Succeeds(new RegionSolver().Solve(network, SolvedAt));
    }

    private static int FaceAt(PlanRegionMap map, double x, double y) => map.FaceAt(new Point2D(x, y))!.Id;

    private static Segment2D[] Rectangle(double x, double y, double width, double height, string prefix) => new[]
    {
        Seg(x, y, x + width, y, prefix + "-S"),
        Seg(x + width, y, x + width, y + height, prefix + "-E"),
        Seg(x + width, y + height, x, y + height, prefix + "-N"),
        Seg(x, y + height, x, y, prefix + "-W")
    };

    private static Segment2D Seg(double x1, double y1, double x2, double y2, string element) =>
        new Segment2D(new Point2D(x1, y1), new Point2D(x2, y2), new SourceRef("doc", element, GeometrySourceKind.WallCenterline));

    private static T Succeeds<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        return result.Value;
    }
}
