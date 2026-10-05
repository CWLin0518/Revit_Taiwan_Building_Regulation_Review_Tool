using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Candidates;

/// <summary>
/// 弧形帷幕牆依直向 grid line 拆成平面段（docs/regulations/curtain-wall-fire-compartment.md §4.7）。
///
/// The model is the one that raised it: a quarter circle of radius 9 m round the origin, from due south
/// to due east, built as ten flat 9° facets — the outside of the arc is the outside of the building.
/// </summary>
public sealed class CurtainWallFacetTests
{
    private const double Radius = 9000;
    private const int FacetCount = 10;
    private const double FacetDegrees = 90.0 / FacetCount;
    private const double OffsetMm = 75;
    private const double StoreyMm = 3600;

    private static readonly Guid Package = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ZoneId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000f");

    // --- 讀入的段 ----------------------------------------------------------------------------------

    [Fact]
    public void A_facet_is_a_plane_by_construction()
    {
        Assert.Throws<ArgumentException>(() => new CurtainWallObservation(
            "ARC", new Point2D(0, 0), new Point2D(1000, 0), new Point2D(0, -1), OffsetMm, 0, StoreyMm,
            nonPlanarReason: "為曲面帷幕牆", facetIndex: 0));
    }

    [Fact]
    public void Only_a_facet_starts_part_way_along_the_wall()
    {
        Assert.Throws<ArgumentException>(() => new CurtainWallObservation(
            "CW", new Point2D(0, 0), new Point2D(1000, 0), new Point2D(0, -1), OffsetMm, 0, StoreyMm,
            facetOffsetMm: 500));
    }

    [Fact]
    public void A_wall_is_listed_whole_or_as_facets_never_both()
    {
        var facets = Arc(GlazingOnly, StoreyMm);
        var whole = new CurtainWallObservation("ARC", new Point2D(0, -Radius), new Point2D(Radius, 0),
            new Point2D(1, -1), OffsetMm, 0, StoreyMm);

        Assert.Throws<ArgumentException>(() => SetOf(facets.Append(whole), hosts: null, floors: null, StoreyLevels(0)));
        Assert.Throws<ArgumentException>(() => SetOf(facets.Append(facets[3]), hosts: null, floors: null, StoreyLevels(0)));
    }

    [Fact]
    public void Facets_are_kept_in_order_along_the_wall()
    {
        var set = SetOf(Arc(GlazingOnly, StoreyMm).Reverse(), hosts: null, floors: null, StoreyLevels(0));

        Assert.Equal(Enumerable.Range(0, FacetCount).Cast<int?>(), set.CurtainWalls.Select(w => w.FacetIndex));
    }

    // --- 整道牆的判定 ---------------------------------------------------------------------------------

    [Fact]
    public void An_arc_wall_read_as_facets_is_measured_rather_than_sent_to_manual_review()
    {
        var junctions = Resolve(Ground(Host(-49.5)));

        Assert.DoesNotContain(junctions, j => j.Doubt?.Kind == CurtainWallJunctionDoubtKind.NonPlanarCurtainWall);
        Assert.All(junctions, j => Assert.Equal("ARC", j.CurtainWallUniqueId));
    }

    // --- 室內外分流（docs §4.8）：同一道弧牆一半在室內、一半在室外 --------------------------------

    [Fact]
    public void An_arc_wall_part_inside_and_part_outside_is_undecided_rather_than_put_to_a_vote()
    {
        // 弧牆的前半段（−90°～−45°）外側也有區劃，後半段沒有：段號跟著 grid line 走，與每一段有多少
        // 立面無關，所以多數決會把真實存在的少數段套上錯誤的規則（審查 4-4）。整道牆交人工覆核。
        var outside = new List<Point2D>();
        for (var degrees = -90; degrees <= -45; degrees++) outside.Add(On(degrees, Radius + 10));
        for (var degrees = -45; degrees >= -90; degrees--) outside.Add(On(degrees, Radius + 2000));

        var set = new CurtainWallObservationSet(Package, "LVL", "1F", 0,
            new[] { Zone(), new CurtainWallZoneObservation(OutsideZoneId, "鄰室", new[] { outside }) },
            Arc(GlazingOnly, StoreyMm),
            new[] { Host(-49.5) },
            levelElevationsMm: StoreyLevels(0));

        var junction = Assert.Single(Resolve(set));

        Assert.Equal(CurtainWallJunctionDoubtKind.ExposureUndecided, junction.Doubt!.Kind);
        Assert.Equal(ReviewStatus.ManualReview, junction.Doubt.Status);
        Assert.Equal("ARC", junction.CurtainWallUniqueId);
        Assert.Contains("分成室內與室外兩道牆", junction.Doubt.Message);
    }

    private static readonly Guid OutsideZoneId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000001f");

    [Fact]
    public void A_compartment_wall_meeting_the_arc_inside_a_facet_is_one_junction_with_its_projection()
    {
        // 區劃牆沿 −49.5° 的徑向抵到第 5 段（−54°～−45°）的中點，外端在半徑 9600：
        // 段的弦在中點離圓心 R·cos 4.5°，所以突出 = 9600 − R·cos 4.5° − 外側面偏移。
        var junction = Assert.Single(Resolve(Ground(Host(-49.5))), j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall);

        Assert.False(junction.IsDoubtful);
        var expected = 9600 - (Radius * Math.Cos(FacetDegrees / 2 * Math.PI / 180)) - OffsetMm;
        Assert.Equal(expected, junction.ProjectionDepthMm!.Value, 1);
    }

    [Fact]
    public void A_compartment_wall_meeting_the_arc_at_a_vertical_grid_line_is_still_one_junction()
    {
        // −45° 正好是第 5、6 段的分界：兩段都求得到這個交點，但那是同一個地方。
        var junctions = Resolve(Ground(Host(-45))).Where(j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall).ToList();

        var junction = Assert.Single(junctions);
        Assert.False(junction.IsDoubtful);
    }

    [Fact]
    public void A_compartment_wall_meeting_a_facet_next_to_a_grid_line_is_not_a_near_miss_on_the_neighbour()
    {
        // −44° 落在第 6 段、離第 5 段的端點只有一度：第 5 段先被問到，不能因此留下一列人工覆核。
        var junction = Assert.Single(Resolve(Ground(Host(-44))), j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall);

        Assert.False(junction.IsDoubtful);
    }

    [Fact]
    public void A_compartment_wall_cutting_the_arc_twice_is_two_junctions()
    {
        // 一道直線區劃牆沿弦割過圓弧（圓形塔樓沿直徑一分為二的情形）：兩端各抵到圓弧一次，
        // 交點約在 −74.1°（第 2 段）與 −15.9°（第 9 段），兩處都要各自檢討，不能只留第一處。
        var chord = new CompartmentWallObservation("W1", On(-80, Radius + 600), On(-10, Radius + 600), 0, StoreyMm,
            CurtainWallJunctionReferences.Article79, 60, "RC 牆 15cm");

        var junctions = Resolve(Ground(chord)).Where(j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall).ToList();

        Assert.Equal(new[] { "CW-H:ARC:W1", "CW-H:ARC:W1:2" }, junctions.Select(j => j.JunctionId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(junctions, j => Assert.False(j.IsDoubtful));
        Assert.Contains(junctions, j => j.PanelUniqueIds.Contains("G1"));
        Assert.Contains(junctions, j => j.PanelUniqueIds.Contains("G8"));
    }

    [Fact]
    public void The_junction_band_reaches_across_a_grid_line_into_the_next_facets_panels()
    {
        // 交點在 −45°：交接帶左右各 900 mm，兩側段的嵌板中點都在帶內（段長約 1412 mm）。
        var junctions = Resolve(Ground(Host(-45)));
        var wallJunction = Assert.Single(junctions, j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(new[] { "G4", "G5" }, wallJunction.PanelUniqueIds);
        var other = Assert.Single(junctions, j => j.Kind == CurtainWallJunctionKind.CurtainPanelOther);
        Assert.DoesNotContain("G4", other.PanelUniqueIds);
        Assert.DoesNotContain("G5", other.PanelUniqueIds);
        Assert.Equal(FacetCount - 2, other.PanelUniqueIds.Count);
    }

    [Fact]
    public void Other_panels_are_answered_once_for_the_whole_wall_not_once_per_facet()
    {
        var junctions = Resolve(Ground(host: null));

        var other = Assert.Single(junctions, j => j.Kind == CurtainWallJunctionKind.CurtainPanelOther);
        Assert.Equal(FacetCount, other.PanelUniqueIds.Count);
        Assert.Equal(junctions.Count, junctions.Select(j => j.JunctionId).Distinct(StringComparer.Ordinal).Count());
    }

    // --- CW-V：層間帶跨過 grid line 仍是一條 ------------------------------------------------------

    [Fact]
    public void A_slab_edge_running_along_every_facet_is_one_spandrel_band()
    {
        var junction = Assert.Single(Resolve(Upper(spandrelMinutes: 60)), j => j.Kind == CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.False(junction.IsDoubtful);
        Assert.Equal(900, junction.ContinuousFireRatedHeightMm!.Value, 3);
        Assert.Equal("CW-V:ARC:F1:0", junction.JunctionId);
    }

    [Fact]
    public void The_spandrel_band_is_placed_on_the_chord_from_the_arcs_start_to_its_end()
    {
        var junction = Assert.Single(Resolve(Upper(spandrelMinutes: 60)), j => j.Kind == CurtainWallJunctionKind.FloorToCurtainWall);

        var placement = junction.Placement!;
        Assert.Equal(0, placement.StartMm.X, 3);
        Assert.Equal(-Radius, placement.StartMm.Y, 3);
        Assert.Equal(Radius, placement.EndMm.X, 3);
        Assert.Equal(0, placement.EndMm.Y, 3);
    }

    [Fact]
    public void The_spandrel_takes_the_worst_facet()
    {
        // 只有第 8 段的層間嵌板時效不足：整條層間帶以它判定，不是被其他九段平均掉。
        var junction = Assert.Single(Resolve(Upper(spandrelMinutes: 60, weakFacet: 7)),
            j => j.Kind == CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedHeightMm!.Value);
        Assert.Contains("S7", junction.PanelUniqueIds);
    }

    [Fact]
    public void Spandrel_panels_of_every_facet_are_left_out_of_the_other_panels()
    {
        var other = Assert.Single(Resolve(Upper(spandrelMinutes: 60)), j => j.Kind == CurtainWallJunctionKind.CurtainPanelOther);

        // 樓板以下的 L 嵌板屬於 1F，由 1F 的封包作答第79條之4；2F 只答自己樓層的 H 嵌板。
        Assert.DoesNotContain(other.PanelUniqueIds, id => id.StartsWith("S", StringComparison.Ordinal));
        Assert.DoesNotContain(other.PanelUniqueIds, id => id.StartsWith("L", StringComparison.Ordinal));
        Assert.Equal(FacetCount, other.PanelUniqueIds.Count);
    }

    // --- fixtures ---------------------------------------------------------------------------------

    private static IReadOnlyList<CurtainWallJunction> Resolve(CurtainWallObservationSet set) =>
        CurtainWallJunctionResolver.Resolve(set);

    private static Point2D On(double degrees, double radius = Radius) =>
        new(radius * Math.Cos(degrees * Math.PI / 180), radius * Math.Sin(degrees * Math.PI / 180));

    private static double DegreesAt(int facet) => -90 + (FacetDegrees * facet);

    /// <summary>The quarter circle as ten facets, each carrying whatever panels <paramref name="panels"/> lays on it.</summary>
    private static IReadOnlyList<CurtainWallObservation> Arc(
        Func<int, double, IEnumerable<CurtainPanelObservation>> panels,
        double top)
    {
        var facets = new List<CurtainWallObservation>();
        var offset = 0.0;
        for (var k = 0; k < FacetCount; k++)
        {
            var start = On(DegreesAt(k));
            var end = On(DegreesAt(k + 1));
            var length = start.DistanceTo(end);
            var dx = (end.X - start.X) / length;
            var dy = (end.Y - start.Y) / length;

            facets.Add(new CurtainWallObservation(
                "ARC", start, end, new Point2D(dy, -dx), OffsetMm, 0, top,
                panels(k, length), typeName: "帷幕牆-150x250cm", facetIndex: k, facetOffsetMm: offset));
            offset += length;
        }

        return facets;
    }

    private static IEnumerable<CurtainPanelObservation> GlazingOnly(int facet, double length) =>
        new[] { Panel("G" + facet, length, 0, StoreyMm, 0) };

    private static CurtainPanelObservation Panel(string uniqueId, double length, double bottom, double top, double? minutes) =>
        new(uniqueId, 0, length, bottom, top,
            minutes is double m ? ProvidedFireRating.Rated(m, m.ToString("0")) : ProvidedFireRating.Missing("參數值為空白"),
            false, null, kind: CurtainPanelKind.Solid);

    /// <summary>The quarter disc behind the arc, kept just inside it.</summary>
    private static CurtainWallZoneObservation Zone()
    {
        var loop = new List<Point2D> { new(0, 0) };
        for (var degrees = -90; degrees <= 0; degrees++) loop.Add(On(degrees, Radius - 10));
        return new CurtainWallZoneObservation(ZoneId, "挑空", new[] { loop });
    }

    /// <summary>A 區劃牆 along the radius at <paramref name="degrees"/>, from 3 m out to 600 mm past the arc.</summary>
    private static CompartmentWallObservation Host(double degrees) =>
        new("W1", On(degrees, 3000), On(degrees, Radius + 600), 0, StoreyMm,
            CurtainWallJunctionReferences.Article79, 60, "RC 牆 15cm");

    private static IReadOnlyList<double> StoreyLevels(double level) => new[] { level, level + StoreyMm };

    private static CurtainWallObservationSet SetOf(
        IEnumerable<CurtainWallObservation> walls,
        IEnumerable<CompartmentWallObservation>? hosts,
        IEnumerable<CompartmentFloorObservation>? floors,
        IReadOnlyList<double> levels,
        double levelElevation = 0) =>
        new(Package, "LVL", "1F", levelElevation, new[] { Zone() }, walls, hosts, floors,
            levelElevationsMm: levels);

    /// <summary>1F: a storey-high arc of glazing, optionally met by one 區劃牆.</summary>
    private static CurtainWallObservationSet Ground(CompartmentWallObservation? host) =>
        SetOf(Arc(GlazingOnly, StoreyMm), host is null ? null : new[] { host }, floors: null, StoreyLevels(0));

    /// <summary>
    /// 2F: the arc runs from 0 to two storeys, a 900 mm spandrel panel straddles the slab at 3600 on
    /// every facet, and the slab reaches just past the arc all the way round.
    /// </summary>
    private static CurtainWallObservationSet Upper(double spandrelMinutes, int? weakFacet = null)
    {
        var walls = Arc((k, length) => new[]
        {
            Panel("L" + k, length, 0, StoreyMm - 450, 0),
            Panel("S" + k, length, StoreyMm - 450, StoreyMm + 450, k == weakFacet ? 0 : spandrelMinutes),
            Panel("H" + k, length, StoreyMm + 450, StoreyMm * 2, 0)
        }, StoreyMm * 2);

        var outline = new List<Point2D> { new(-1000, 1000) };
        for (var degrees = -100; degrees <= 10; degrees++) outline.Add(On(degrees, Radius + 100));
        var floor = new CompartmentFloorObservation("F1", new[] { outline }, StoreyMm, 60);

        return new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm, new[] { Zone() }, walls,
            compartmentFloors: new[] { floor },
            levelElevationsMm: new[] { 0.0, StoreyMm, StoreyMm * 2 });
    }
}
