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

/// <summary>
/// 防火檢討_區劃用途 on the way back out of the model (任務 3). The three states exist to stop a
/// reopened Editor writing a blank over a use the 批次設定面板 set, so every one of them is pinned
/// here: a zone that agrees, a zone whose Areas disagree, and a model whose parameter is not bound.
/// </summary>
public class ZoneUseRestorationTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime SolvedAt = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ZoneA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly ZoneColor Red = ZoneColor.FromHex("#CC3333");

    [Fact]
    public void AZoneWhoseAreasAgreeComesBackCarryingThatUse()
    {
        var map = TwoRooms();

        var restored = WrittenZoneRestorer.Restore(map, new[]
        {
            Written(ZoneA, Box(0, 0, 10, 10), "管道間"),
            Written(ZoneA, Box(10, 0, 20, 10), "管道間")
        });

        Assert.Equal("管道間", Assert.Single(restored.Zones.Zones).Use);
        Assert.Empty(restored.Warnings);
    }

    [Fact]
    public void AZoneWhoseAreasAreAllBlankComesBackAsAnExplicitGeneralZone()
    {
        var map = TwoRooms();

        var restored = WrittenZoneRestorer.Restore(map, new[] { Written(ZoneA, Box(0, 0, 20, 10), string.Empty) });

        // Empty, not null: the model really does say 一般區劃, and 套用 writing that same blank back
        // is a no-op rather than a loss.
        Assert.Equal(string.Empty, Assert.Single(restored.Zones.Zones).Use);
        Assert.Empty(restored.Warnings);
    }

    [Fact]
    public void AZoneWhoseAreasDisagreeComesBackAsNoChangeAndSaysSo()
    {
        var map = TwoRooms();

        var restored = WrittenZoneRestorer.Restore(map, new[]
        {
            Written(ZoneA, Box(0, 0, 10, 10), "管道間"),
            Written(ZoneA, Box(10, 0, 20, 10), "挑空")
        });

        Assert.Null(Assert.Single(restored.Zones.Zones).Use);
        Assert.Contains(restored.Warnings, w => w.Contains("不一致") && w.Contains("保留"));
    }

    [Fact]
    public void AnAreaWhoseParameterCannotBeReadComesBackAsNoChangeWithoutAWarning()
    {
        var map = TwoRooms();

        var restored = WrittenZoneRestorer.Restore(map, new[] { Written(ZoneA, Box(0, 0, 20, 10), null) });

        // 不變更 all the same — but silent, because an unbound parameter is the whole model's state
        // and one line per zone would bury the write-back's single message about it.
        Assert.Null(Assert.Single(restored.Zones.Zones).Use);
        Assert.Empty(restored.Warnings);
    }

    [Fact]
    public void OneUnreadableAreaMakesTheWholeZoneNoChangeEvenWhenTheOthersAgree()
    {
        var map = TwoRooms();

        var restored = WrittenZoneRestorer.Restore(map, new[]
        {
            Written(ZoneA, Box(0, 0, 10, 10), "管道間"),
            Written(ZoneA, Box(10, 0, 20, 10), null)
        });

        Assert.Null(Assert.Single(restored.Zones.Zones).Use);
    }

    private static WrittenZoneArea Written(Guid zoneId, IReadOnlyList<Point2D> outline, string? use) =>
        new WrittenZoneArea(zoneId, "A 區", Red, new[] { outline }, null, use);

    private static IReadOnlyList<Point2D> Box(double x1, double y1, double x2, double y2) => new[]
    {
        new Point2D(x1, y1), new Point2D(x2, y1), new Point2D(x2, y2), new Point2D(x1, y2)
    };

    // Two 10 x 10 rooms side by side.
    private static PlanRegionMap TwoRooms()
    {
        var segments = new[]
        {
            Seg(0, 0, 20, 0, "S"),
            Seg(20, 0, 20, 10, "E"),
            Seg(20, 10, 0, 10, "N"),
            Seg(0, 10, 0, 0, "W"),
            Seg(10, 0, 10, 10, "D1")
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
