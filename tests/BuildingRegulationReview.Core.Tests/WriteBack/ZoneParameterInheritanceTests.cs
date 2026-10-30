using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Geometry;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.WriteBack;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.WriteBack;

/// <summary>
/// 驗證清單 C-22：區劃編輯器把一個區劃拆成不相連的幾塊時會新建 Area，新 Area 要沿用同區劃既有 Area 的
/// 樓層序、自動滅火設備等參數；否則同一區劃各 Area 填得不一致，檢討讀成「格式無法判讀」而資料不足。
/// </summary>
public class ZoneParameterInheritanceTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ZoneId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly GeometryTolerance Tolerance = GeometryTolerance.Default;
    private static readonly DateTime SolvedAt = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);

    private static readonly PlanRegionMap TwoRooms =
        Solve(Rectangle(0, 0, 10, 10, "LEFT").Concat(Rectangle(40, 0, 10, 10, "RIGHT")));

    [Fact]
    public void A_new_part_takes_over_what_the_existing_area_of_its_zone_says()
    {
        var model = Written(Zone(Left), Values(floor: 2, sprinklered: 0));

        var preview = ApplyPreview.Build(PackageId, TwoRooms, Zone(Left, Right), model);

        var operation = Assert.Single(preview.ZoneInheritance);
        Assert.Equal(1, operation.Key.PartIndex);
        Assert.Equal(
            new[] { (ReviewInputSources.FloorNumber, 2), (ReviewInputSources.Sprinklered, 0) },
            operation.Values.Select(v => (v.Parameter, v.Value)));
        Assert.Empty(operation.Unresolved);
        Assert.Equal("「A」第 2 塊新建的面積沿用同區劃既有面積的 所在樓層序＝2、自動滅火設備＝否", operation.Text);

        // 計畫帶著它走，寫回才拿得到。
        Assert.Single(ApplyPlan.Build(preview, ApplyPlan.AllKinds).ZoneInheritance);
        // 「重建」只把不變列改成更新，新增列與它的沿用值照帶。
        Assert.Single(preview.ForRebuild().ZoneInheritance);
    }

    [Fact]
    public void A_brand_new_zone_has_nothing_to_take_over()
    {
        Assert.Empty(ApplyPreview.Build(PackageId, TwoRooms, Zone(Left, Right)).ZoneInheritance);
    }

    [Fact]
    public void An_area_that_already_exists_is_never_rewritten()
    {
        var model = Written(Zone(Left, Right), Values(floor: 2, sprinklered: 0));

        Assert.Empty(ApplyPreview.Build(PackageId, TwoRooms, Zone(Left, Right), model).ZoneInheritance);
    }

    [Fact]
    public void Existing_areas_that_disagree_are_not_settled_by_the_tool()
    {
        // 兩塊既有面積：樓層序一致、滅火設備一塊是一塊否、避難層通達只有一塊有填。
        var three = Solve(Rectangle(0, 0, 10, 10, "LEFT")
            .Concat(Rectangle(40, 0, 10, 10, "RIGHT"))
            .Concat(Rectangle(80, 0, 10, 10, "FAR")));
        var before = Zone(three, Left, Right);
        var model = ZoneWritePlan.Build(PackageId, three, before, Tolerance)
            .Select(planned => Existing(planned, planned.Key.PartIndex == 0
                ? Values(floor: 3, sprinklered: 1, refuge: 1)
                : Values(floor: 3, sprinklered: 0)))
            .ToList();

        var operation = Assert.Single(ApplyPreview.Build(PackageId, three, Zone(three, Left, Right, Far), model).ZoneInheritance);

        Assert.Equal(new[] { (ReviewInputSources.FloorNumber, 3) }, operation.Values.Select(v => (v.Parameter, v.Value)));
        Assert.Equal(new[] { ReviewInputSources.Sprinklered, ReviewInputSources.LinksRefugeFloor }, operation.Unresolved);
        Assert.Contains("填得不一致或有未填", operation.Text);
    }

    [Fact]
    public void A_zone_moved_to_another_room_keeps_its_area_and_so_its_values()
    {
        // 單塊區劃整個換到另一個房間：面積的鍵不變，寫回是搬動它而不是刪了重建，使用者填的值留在原面積上，
        // 沒有東西要沿用。
        var model = Written(Zone(Left), Values(floor: 2));

        var preview = ApplyPreview.Build(PackageId, TwoRooms, Zone(Right), model);

        Assert.DoesNotContain(preview.Deleted, d => d.Kind == ManagedElementKind.Area);
        Assert.Contains(preview.Updated, u => u.Kind == ManagedElementKind.Area);
        Assert.Empty(preview.ZoneInheritance);
    }

    [Fact]
    public void A_use_line_names_which_part_of_a_disjoint_zone_it_is_about()
    {
        // 2026-10-27 V-29：兩塊的預覽一字不差，看不出各指哪一塊。
        var zones = Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            ZoneId, "A", ZoneColorPalette.At(0), new[] { Face(TwoRooms, Left), Face(TwoRooms, Right) },
            allowsDisjointParts: true, use: "管道間")));

        var lines = ApplyPreview.Build(PackageId, TwoRooms, zones).ZoneUses.Select(op => op.Text).ToList();

        Assert.Equal(new[] { "將「A」的區劃用途設為「管道間」", "將「A」第 2 塊的區劃用途設為「管道間」" }, lines);
    }

    /// <summary>
    /// 用途類組 rides along with the integer parameters: a re-placed or split 區劃 deletes and recreates
    /// Areas, and the old Areas are the only record of the group the user filled in.
    /// </summary>
    [Fact]
    public void A_new_part_takes_over_the_use_group_its_zone_agrees_on()
    {
        var model = Written(Zone(Left), Values(floor: 2), useGroup: "H-2");

        var operation = Assert.Single(ApplyPreview.Build(PackageId, TwoRooms, Zone(Left, Right), model).ZoneInheritance);

        Assert.Equal("H-2", operation.UseGroup);
        Assert.Empty(operation.Unresolved);
        Assert.Equal("「A」第 2 塊新建的面積沿用同區劃既有面積的 所在樓層序＝2、用途類組＝H-2", operation.Text);
    }

    /// <summary>
    /// A 區劃 of several groups is legal, but which part of the redrawn 區劃 holds which group is the
    /// user's to say — the tool picks none and asks for it.
    /// </summary>
    [Fact]
    public void A_mixed_zone_leaves_the_new_parts_use_group_to_the_user()
    {
        var three = Solve(Rectangle(0, 0, 10, 10, "LEFT")
            .Concat(Rectangle(40, 0, 10, 10, "RIGHT"))
            .Concat(Rectangle(80, 0, 10, 10, "FAR")));
        var model = ZoneWritePlan.Build(PackageId, three, Zone(three, Left, Right), Tolerance)
            .Select(planned => Existing(planned, Values(floor: 3), planned.Key.PartIndex == 0 ? "H-2" : "B-2"))
            .ToList();

        var operation = Assert.Single(ApplyPreview.Build(PackageId, three, Zone(three, Left, Right, Far), model).ZoneInheritance);

        Assert.Null(operation.UseGroup);
        Assert.Equal(new[] { ReviewInputSources.UseGroup }, operation.Unresolved);
    }

    /// <summary>Two spellings of one group are one group: the new part takes it over in its canonical form.</summary>
    [Fact]
    public void Two_spellings_of_one_group_are_taken_over_as_that_group()
    {
        var three = Solve(Rectangle(0, 0, 10, 10, "LEFT")
            .Concat(Rectangle(40, 0, 10, 10, "RIGHT"))
            .Concat(Rectangle(80, 0, 10, 10, "FAR")));
        var model = ZoneWritePlan.Build(PackageId, three, Zone(three, Left, Right), Tolerance)
            .Select(planned => Existing(planned, Values(floor: 3), planned.Key.PartIndex == 0 ? "H-2" : "Ｈ－２組"))
            .ToList();

        var operation = Assert.Single(ApplyPreview.Build(PackageId, three, Zone(three, Left, Right, Far), model).ZoneInheritance);

        Assert.Equal("H-2", operation.UseGroup);
        Assert.Empty(operation.Unresolved);
    }

    /// <summary>Nobody filled the group in: nothing to take over, and nothing to complain about.</summary>
    [Fact]
    public void A_zone_with_no_use_group_hands_none_over()
    {
        var model = Written(Zone(Left), Values(floor: 2));

        var operation = Assert.Single(ApplyPreview.Build(PackageId, TwoRooms, Zone(Left, Right), model).ZoneInheritance);

        Assert.Null(operation.UseGroup);
        Assert.DoesNotContain(ReviewInputSources.UseGroup, operation.Unresolved);
    }

    // ---- fixtures ----------------------------------------------------------------------------

    private static readonly (double X, double Y) Left = (5, 5);
    private static readonly (double X, double Y) Right = (45, 5);
    private static readonly (double X, double Y) Far = (85, 5);

    private static ZoneDraftSet Zone(params (double X, double Y)[] rooms) => Zone(TwoRooms, rooms);

    private static ZoneDraftSet Zone(PlanRegionMap map, params (double X, double Y)[] rooms) =>
        Succeeds(ZoneDraftSet.Empty.Add(new ZoneDraft(
            ZoneId, "A", ZoneColorPalette.At(0), rooms.Select(r => Face(map, r)).ToArray(),
            allowsDisjointParts: rooms.Length > 1)));

    private static Dictionary<string, int?> Values(int? floor = null, int? sprinklered = null, int? refuge = null) => new()
    {
        [ReviewInputSources.FloorNumber] = floor,
        [ReviewInputSources.Sprinklered] = sprinklered,
        [ReviewInputSources.LinksRefugeFloor] = refuge,
        [ReviewInputSources.CannotBeSubdivided] = null
    };

    /// <summary>The model as an earlier run left it, every Area carrying <paramref name="values"/>.</summary>
    private static IReadOnlyList<ExistingManagedElement> Written(
        ZoneDraftSet zones, IReadOnlyDictionary<string, int?> values, string? useGroup = null) =>
        ZoneWritePlan.Build(PackageId, TwoRooms, zones, Tolerance).Select(p => Existing(p, values, useGroup)).ToList();

    private static ExistingManagedElement Existing(
        PlannedElement planned, IReadOnlyDictionary<string, int?> values, string? useGroup = null) =>
        new ExistingManagedElement(
            "uid-" + planned.Key.ToToken(),
            planned.Key.ToToken(),
            planned.Signature,
            planned.Description,
            planned.Key.Kind == ManagedElementKind.Area ? string.Empty : null,
            planned.Key.Kind == ManagedElementKind.Area ? values : null,
            planned.Key.Kind == ManagedElementKind.Area ? useGroup : null);

    private static PlanRegionMap Solve(IEnumerable<Segment2D> segments)
    {
        var snapshot = new PlanGeometrySnapshot(PackageId, "host-doc", "level-1", segments, Tolerance);
        var network = Succeeds(new LineNetworkRepairer().Repair(snapshot, SolvedAt));
        return Succeeds(new RegionSolver().Solve(network, SolvedAt));
    }

    private static int Face(PlanRegionMap map, (double X, double Y) at) => map.FaceAt(new Point2D(at.X, at.Y))!.Id;

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
