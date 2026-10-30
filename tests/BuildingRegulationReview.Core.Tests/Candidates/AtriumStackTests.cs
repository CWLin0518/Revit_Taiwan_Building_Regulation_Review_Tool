using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Candidates;

/// <summary>
/// 挑空 traced through the storeys (垂直區劃規格 §3.8、決議 35). Plans are in feet; each storey is
/// 0..60 × 0..20 unless a test says otherwise. Areas are given in square feet so the sums are exact.
/// </summary>
public sealed class AtriumStackTests
{
    private static readonly Guid Package = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private static int _next;

    // --- 一層開口 ------------------------------------------------------------------------------------

    /// <summary>
    /// The plain case: a 挑空 on 2F inside 區劃 X; 1F under it. 起始樓層 1F, 連跨 2 層, 連通區劃面積 =
    /// X + the 1F 區劃 under X. A 區劃 on 2F not touching the 挑空, and one on 1F not under X, are left
    /// out, and so is the 挑空 itself.
    /// </summary>
    [Fact]
    public void An_opening_in_the_second_floor_slab_spans_two_storeys_from_the_first()
    {
        var atrium = Atrium(2, Rect(10, 5, 20, 15));
        var x = Zone(2, "X", Hole(Rect(0, 0, 40, 20), Rect(10, 5, 20, 15)), 600);
        var elsewhere = Zone(2, "遠處", Rect(40, 0, 60, 20), 400);
        var under = Zone(1, "1F 大廳", Rect(0, 0, 40, 20), 800);
        var away = Zone(1, "1F 店舖", Rect(40, 0, 60, 20), 400);

        var stack = Resolve(atrium, atrium, x, elsewhere, under, away);

        Assert.Equal(2, stack.SpannedFloors);
        Assert.Equal(1, stack.BaseFloorNumber);
        Assert.Equal(2, stack.TopFloorNumber);
        Assert.Equal(new[] { "1F 大廳", "X" }, stack.Contributors.Select(c => c.Zone.Name));
        Assert.Equal(PlanUnits.SquareFeetToSquareMeters(600 + 800), stack.ConnectedAreaSquareMeters!.Value, 6);
        Assert.Null(stack.AreaProblem);
    }

    /// <summary>
    /// 起始樓層 counts the 區劃 under the 所在區劃 X, not only under the opening (使用者選 (2)): a 1F
    /// 區劃 under the far end of X, nowhere near the 挑空, is counted.
    /// </summary>
    [Fact]
    public void The_first_floor_counts_what_lies_under_the_surrounding_zone_not_only_under_the_opening()
    {
        var atrium = Atrium(2, Rect(2, 5, 8, 15));
        var x = Zone(2, "X", Rect(8, 0, 40, 20), 600);
        var underOpening = Zone(1, "1F 前段", Rect(0, 0, 10, 20), 200);
        var underFarEnd = Zone(1, "1F 後段", Rect(30, 0, 40, 20), 200);

        var stack = Resolve(atrium, atrium, x, underOpening, underFarEnd);

        Assert.Equal(new[] { "1F 前段", "1F 後段" }, stack.Contributors.Where(c => c.Role == AtriumContributorRole.Below).Select(c => c.Zone.Name));
    }

    // --- 多層：以區劃判斷連跨 -------------------------------------------------------------------------

    /// <summary>
    /// The case the user described: the 3F 挑空 is completely offset from the 2F one — the two openings
    /// do not overlap at all — but it lies in a 3F 區劃 Y over the 2F 區劃 X. One 挑空, 1F～3F.
    /// </summary>
    [Fact]
    public void An_offset_opening_upstairs_in_the_zone_over_this_one_continues_the_atrium()
    {
        var a2 = Atrium(2, Rect(2, 5, 8, 15));
        var x = Zone(2, "X", Rect(8, 0, 40, 20), 600);
        var a3 = Atrium(3, Rect(32, 5, 38, 15));
        var y = Zone(3, "Y", Rect(0, 0, 32, 20), 500);
        var under = Zone(1, "1F", Rect(0, 0, 40, 20), 800);

        var stack = Resolve(a2, a2, x, a3, y, under);

        Assert.Equal(new[] { a2, a3 }, stack.Atriums);
        Assert.Equal(3, stack.SpannedFloors);
        Assert.Equal(1, stack.BaseFloorNumber);
        Assert.Equal(3, stack.TopFloorNumber);
        Assert.Equal(new[] { "1F", "X", "Y" }, stack.Contributors.Select(c => c.Zone.Name));

        // Traced from the 3F opening, it is the same 挑空 with the same figures.
        var fromAbove = Resolve(a3, a2, x, a3, y, under);
        Assert.Equal(stack.SpannedFloors, fromAbove.SpannedFloors);
        Assert.Equal(stack.ConnectedAreaSquareMeters, fromAbove.ConnectedAreaSquareMeters);
    }

    /// <summary>A 3F 區劃 over X with no 挑空 in it closes the 挑空 at 2F.</summary>
    [Fact]
    public void A_zone_upstairs_with_no_opening_ends_the_atrium()
    {
        var a2 = Atrium(2, Rect(2, 5, 8, 15));
        var x = Zone(2, "X", Rect(8, 0, 40, 20), 600);
        var y = Zone(3, "Y", Rect(0, 0, 40, 20), 500);
        var under = Zone(1, "1F", Rect(0, 0, 40, 20), 800);

        var stack = Resolve(a2, a2, x, y, under);

        Assert.Equal(2, stack.SpannedFloors);
        Assert.DoesNotContain(stack.Contributors, c => c.Zone.Name == "Y");
    }

    /// <summary>A 挑空 upstairs in a 區劃 that is not over X is another 挑空.</summary>
    [Fact]
    public void An_opening_upstairs_in_a_zone_elsewhere_is_another_atrium()
    {
        var a2 = Atrium(2, Rect(2, 5, 8, 15));
        var x = Zone(2, "X", Rect(8, 0, 30, 20), 600);
        var a3 = Atrium(3, Rect(42, 5, 48, 15));
        var y = Zone(3, "Y", Rect(48, 0, 60, 20), 300);
        var under = Zone(1, "1F", Rect(0, 0, 30, 20), 800);

        var stack = Resolve(a2, a2, x, a3, y, under);

        Assert.Equal(new[] { a2 }, stack.Atriums);
        Assert.Equal(2, stack.SpannedFloors);
    }

    /// <summary>The 起始樓層 is the storey under the lowest opening, wherever the trace started.</summary>
    [Fact]
    public void A_trace_started_upstairs_finds_the_lowest_opening_downstairs()
    {
        var a3 = Atrium(3, Rect(2, 5, 8, 15));
        var y = Zone(3, "Y", Rect(8, 0, 40, 20), 500);
        var a4 = Atrium(4, Rect(2, 5, 8, 15));
        var z = Zone(4, "Z", Rect(8, 0, 40, 20), 500);
        var x = Zone(2, "X", Rect(0, 0, 40, 20), 900);

        var stack = Resolve(a4, a3, y, a4, z, x);

        Assert.Equal(3, stack.SpannedFloors);
        Assert.Equal(2, stack.BaseFloorNumber);
        Assert.Equal(4, stack.TopFloorNumber);
    }

    // --- 無法判定 ------------------------------------------------------------------------------------

    [Fact]
    public void An_opening_no_zone_surrounds_cannot_be_traced()
    {
        var atrium = Atrium(2, Rect(2, 5, 8, 15));
        var corner = Zone(2, "只碰到角", Rect(8, 15, 20, 20), 100);
        var under = Zone(1, "1F", Rect(0, 0, 40, 20), 800);

        var stack = Resolve(atrium, atrium, corner, under);

        Assert.Null(stack.SpannedFloors);
        Assert.Null(stack.ConnectedAreaSquareMeters);
        Assert.Contains("周圍沒有相接的區劃", stack.SpanProblem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_opening_on_the_lowest_storey_has_no_first_floor_under_it()
    {
        var atrium = Atrium(1, Rect(2, 5, 8, 15));
        var x = Zone(1, "X", Rect(8, 0, 40, 20), 600);

        var stack = Resolve(atrium, atrium, x);

        Assert.Null(stack.SpannedFloors);
        Assert.Contains("下方沒有起始樓層", stack.SpanProblem, StringComparison.Ordinal);
    }

    /// <summary>A counted 區劃 with no Revit area spoils the 合計 but not the span.</summary>
    [Fact]
    public void A_counted_zone_without_an_area_leaves_the_total_unknown_but_not_the_span()
    {
        var atrium = Atrium(2, Rect(2, 5, 8, 15));
        var x = Zone(2, "X", Rect(8, 0, 40, 20), null);
        var under = Zone(1, "1F", Rect(0, 0, 40, 20), 800);

        var stack = Resolve(atrium, atrium, x, under);

        Assert.Equal(2, stack.SpannedFloors);
        Assert.Null(stack.ConnectedAreaSquareMeters);
        Assert.Contains("沒有 Revit 面積", stack.AreaProblem, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_under_the_surrounding_zone_leaves_the_total_unknown()
    {
        var atrium = Atrium(2, Rect(2, 5, 8, 15));
        var x = Zone(2, "X", Rect(8, 0, 40, 20), 600);
        var away = Zone(1, "1F 別處", Rect(50, 0, 60, 20), 800);

        var stack = Resolve(atrium, atrium, x, away);

        Assert.Equal(2, stack.SpannedFloors);
        Assert.Contains("正下方的區劃", stack.AreaProblem, StringComparison.Ordinal);
    }

    // --- code review 修正（決議 36）----------------------------------------------------------------

    /// <summary>
    /// 3F has no 區劃 yet, so 2F and 4F are next to each other among the storeys that do. Counting
    /// around the gap would make a four-storey 挑空 three and let 「三層以下」 hold; with the 樓層序
    /// known the jump is reported instead.
    /// </summary>
    [Fact]
    public void A_storey_with_no_zones_yet_between_two_openings_is_reported_not_counted_around()
    {
        var a2 = Atrium(2, Rect(2, 5, 8, 15));
        var x = Zone(2, "X", Rect(8, 0, 40, 20), 600);
        var a4 = Atrium(4, Rect(2, 5, 8, 15));
        var z = Zone(4, "Z", Rect(8, 0, 40, 20), 600);
        var under = Zone(1, "1F", Rect(0, 0, 40, 20), 800);

        var stack = Resolve(a2, a2, x, a4, z, under);

        Assert.Null(stack.SpannedFloors);
        Assert.Null(stack.ConnectedAreaSquareMeters);
        Assert.Contains("之間的樓層沒有區劃", stack.SpanProblem, StringComparison.Ordinal);
    }

    /// <summary>Likewise a 起始樓層 that is not the storey right under the lowest opening.</summary>
    [Fact]
    public void A_first_floor_that_is_not_right_under_the_opening_is_reported()
    {
        var a2 = Atrium(2, Rect(2, 5, 8, 15));
        var x = Zone(2, "X", Rect(8, 0, 40, 20), 600);
        var b1 = Zone(-1, "B1", Rect(0, 0, 40, 20), 800);

        var stack = Resolve(a2, a2, x, b1);

        Assert.Null(stack.SpannedFloors);
        Assert.Contains("樓層序 -1", stack.SpanProblem, StringComparison.Ordinal);
    }

    /// <summary>
    /// A 1F 大廳 shaped exactly like the 2F opening lies in the 所在區劃's hole, not its interior; it
    /// is under the opening and counts.
    /// </summary>
    [Fact]
    public void A_zone_right_under_the_opening_counts_although_it_lies_in_the_surrounding_zones_hole()
    {
        var atrium = Atrium(2, Rect(10, 5, 20, 15));
        var x = Zone(2, "X", Hole(Rect(0, 0, 40, 20), Rect(10, 5, 20, 15)), 600);
        var hall = Zone(1, "1F 中庭大廳", Rect(10, 5, 20, 15), 100);
        var front = Zone(1, "1F 前段", Rect(20, 0, 40, 20), 400);

        var stack = Resolve(atrium, atrium, x, hall, front);

        Assert.Contains(stack.Contributors, c => c.Zone.Name == "1F 中庭大廳");
        Assert.Equal(PlanUnits.SquareFeetToSquareMeters(600 + 100 + 400), stack.ConnectedAreaSquareMeters!.Value, 6);
    }

    /// <summary>
    /// 決議 36: a 樓梯間 stacked through every storey touches both of two unrelated 挑空. It is not a
    /// 所在區劃 — it is 單獨區劃分隔 under 第1項 — so it neither stitches them into one nor adds its area.
    /// </summary>
    [Fact]
    public void A_stair_stacked_through_the_storeys_neither_joins_two_atriums_nor_counts()
    {
        var a2 = Atrium(2, Rect(20, 5, 26, 15));
        var stair2 = Make(2, "樓梯間2F", ZoneUses.Stairwell, new[] { Rect(26, 0, 30, 20) }, 80, 20);
        var x = Zone(2, "X", Rect(0, 0, 20, 20), 600);
        var a3 = Atrium(3, Rect(30, 5, 36, 15));
        var stair3 = Make(3, "樓梯間3F", ZoneUses.Stairwell, new[] { Rect(26, 0, 30, 20) }, 80, 30);
        var y = Zone(3, "Y", Rect(36, 0, 60, 20), 600);
        var under = Zone(1, "1F", Rect(0, 0, 30, 20), 800);

        var stack = Resolve(a2, a2, stair2, x, a3, stair3, y, under);

        Assert.Equal(new[] { a2 }, stack.Atriums);
        Assert.DoesNotContain(stack.Contributors, c => c.Zone.IsVerticalCompartment);
    }

    /// <summary>A 區劃 whose mark turns up on two levels — a copied Area — stops the trace with the reason.</summary>
    [Fact]
    public void A_zone_copied_to_another_storey_stops_the_trace_with_its_reason()
    {
        var atrium = Atrium(2, Rect(2, 5, 8, 15));
        var copied = new StoreyZone(Package, Guid.Parse("55555555-0000-0000-0000-000000000001"), "level@20", 20, "2F",
            "X", "辦公", 2, 600, new[] { Rect(8, 0, 40, 20) }, problem: "區劃「X」同時出現在 2F、3F，可能是複製到其他樓層的 Area");
        var under = Zone(1, "1F", Rect(0, 0, 40, 20), 800);

        var stack = Resolve(atrium, atrium, copied, under);

        Assert.Null(stack.SpannedFloors);
        Assert.Contains("可能是複製", stack.SpanProblem, StringComparison.Ordinal);
    }

    /// <summary>The same identity on two levels is told apart by the level the package is on.</summary>
    [Fact]
    public void A_zone_is_found_on_the_level_the_package_sits_on()
    {
        var id = Guid.Parse("55555555-0000-0000-0000-000000000002");
        var on2 = new StoreyZone(Package, id, "level-2", 20, "2F", "A", ZoneUses.Atrium, 2, 50, new[] { Rect(0, 0, 5, 5) });
        var on3 = new StoreyZone(Package, id, "level-3", 30, "3F", "A", ZoneUses.Atrium, 3, 50, new[] { Rect(0, 0, 5, 5) });
        var map = new StoreyZoneMap(new[] { on2, on3 });

        Assert.Same(on3, map.Find(Package, id, "level-3"));
        Assert.Null(map.Find(Package, id));
    }

    // --- 樓層序 ---------------------------------------------------------------------------------------

    /// <summary>When the 起始樓層's 區劃 carry no 樓層序, it is worked out from the opening's, skipping 0.</summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, -2)]
    public void The_first_floor_number_falls_back_to_the_storey_under_the_opening(int openingFloor, int expected)
    {
        var atrium = Atrium(openingFloor, Rect(2, 5, 8, 15), elevation: 10);
        var x = Zone(openingFloor, "X", Rect(8, 0, 40, 20), 600, elevation: 10);
        var under = Zone(null, "下層", Rect(0, 0, 40, 20), 800, elevation: 0);

        Assert.Equal(expected, Resolve(atrium, atrium, x, under).BaseFloorNumber);
    }

    [Fact]
    public void A_zone_that_is_not_an_atrium_is_not_traced()
    {
        var x = Zone(2, "X", Rect(8, 0, 40, 20), 600);
        var map = new StoreyZoneMap(new[] { x });

        Assert.Null(AtriumStackResolver.Resolve(map, x.PackageId, x.ZoneId));
    }

    // --- helpers ------------------------------------------------------------------------------------

    private static AtriumStack Resolve(StoreyZone start, params StoreyZone[] zones)
    {
        var stack = AtriumStackResolver.Resolve(new StoreyZoneMap(zones), start.PackageId, start.ZoneId);
        Assert.NotNull(stack);
        return stack!;
    }

    private static StoreyZone Atrium(int floor, IReadOnlyList<Point2D> rect, double? elevation = null) =>
        Make(floor, $"挑空{floor}F", ZoneUses.Atrium, new[] { rect }, 50, elevation ?? floor * 10.0);

    private static StoreyZone Zone(int? floor, string name, IReadOnlyList<Point2D> rect, double? squareFeet, double? elevation = null) =>
        Make(floor, name, "辦公", new[] { rect }, squareFeet, elevation ?? (floor ?? 0) * 10.0);

    private static StoreyZone Zone(int floor, string name, IReadOnlyList<IReadOnlyList<Point2D>> rings, double squareFeet) =>
        Make(floor, name, "辦公", rings, squareFeet, floor * 10.0);

    private static IReadOnlyList<IReadOnlyList<Point2D>> Hole(IReadOnlyList<Point2D> outer, IReadOnlyList<Point2D> inner) =>
        new[] { outer, inner };

    private static StoreyZone Make(int? floor, string name, string use, IEnumerable<IReadOnlyList<Point2D>> rings, double? squareFeet, double elevation)
    {
        var id = Guid.Parse($"44444444-0000-0000-0000-{++_next:D12}");
        return new StoreyZone(Package, id, $"level@{elevation}", elevation, floor is int f ? $"{f}F" : "下層",
            name, use, floor, squareFeet, rings);
    }
}
