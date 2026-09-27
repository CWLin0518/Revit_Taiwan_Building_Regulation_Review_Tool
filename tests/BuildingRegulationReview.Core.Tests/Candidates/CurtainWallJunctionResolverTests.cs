using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Candidates;

/// <summary>
/// 帷幕牆區劃交接的幾何層（docs/regulations/curtain-wall-fire-compartment.md §4、§12 步驟 4）。
///
/// The resolver is where §4 actually happens, so these are the tests of §4: the cases of §10 that do
/// need real geometry (8, 8b, 8c, 17, 18) are walked through here, and the ones the Check 層 already
/// proved as fixtures are re-checked from the other end — that the geometry hands over the numbers
/// those fixtures assumed.
///
/// The model is one straight curtain wall running east along y = 0, its outside face to the south:
/// a 區劃牆 reaching it from the north therefore projects by how far south of the line it ends.
/// </summary>
public sealed class CurtainWallJunctionResolverTests
{
    private const double WallLengthMm = 12000;
    private const double OffsetMm = 75;          // location line to outside face
    private const double StoreyMm = 3600;

    private static readonly Guid Package = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ZoneId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000f");

    // --- CW-H：交點、突出與連續段 ---------------------------------------------------------------

    [Fact]
    public void Wall_junction_measures_the_projection_past_the_outside_face()
    {
        var junction = Single(Resolve(Set(Wall(Glazing()), hosts: new[] { Host(5000, beyondLineMm: OffsetMm + 600) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(600, junction.ProjectionDepthMm!.Value, 3);
        Assert.Equal("W1", junction.HostUniqueId);
        Assert.Equal(CurtainWallJunctionReferences.Article79, junction.HostLegalReference);
    }

    [Fact]
    public void A_wall_that_does_not_project_supplies_zero_rather_than_nothing()
    {
        var junction = Single(Resolve(Set(Wall(Glazing()), hosts: new[] { Host(5000) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ProjectionDepthMm!.Value);
    }

    // --- CW-H：上下帷幕牆之間的實體牆防火帶（docs §4.2、決議 7）--------------------------------

    [Fact]
    public void A_rated_solid_wall_filling_the_gap_between_the_curtain_walls_is_the_cw_h_band()
    {
        var junction = Single(Resolve(Set(Wall(GapAt(1200, 2100)), hosts: new[] { Band(5000, 1200, 2100) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(900, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.Equal(60, junction.MinFireRating!.Minutes!.Value);
    }

    [Fact]
    public void A_wall_that_only_partly_fills_the_gap_is_no_band_at_all()
    {
        // 間隔 900 mm，填進去的牆只有 700 mm：上面還開著 200 mm，這不是「以實體牆填入的間隔」。
        // 供給 700 會讓「牆 900、旁邊還開著 900」也過關，所以這一步是守門而不是量測。
        var junction = Single(Resolve(Set(Wall(GapAt(1200, 2100)), hosts: new[] { Band(5000, 1200, 1900) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
    }

    [Fact]
    public void A_wall_floating_inside_a_tall_gap_is_no_band_either()
    {
        // 上下都有嵌板收邊，但牆兩邊都不貼：間隔 5000 mm 裡浮著一道 1000 mm 的牆。
        var panels = new[]
        {
            Panel("P-low", 0, WallLengthMm, 0, bottom: 0, top: 500),
            Panel("P-high", 0, WallLengthMm, 0, bottom: 5500, top: 6000)
        };
        var set = new CurtainWallObservationSet(
            Package, "LVL", "1F", 0, new[] { Zone() },
            new[] { Wall(panels, top: 6000) },
            new[] { Band(5000, 2000, 3000, ProvidedFireRating.Rated(120, "120")) },
            null, new[] { 0.0, 6000.0 });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
    }

    [Fact]
    public void A_gap_the_wall_fills_exactly_is_a_band_however_tall_the_gap_is()
    {
        // 帷幕牆通三層，中間那一層整柱沒有嵌板，而區劃牆的高程範圍恰好就是那一層。決議 7 認這一種：
        // 牆兩緣都貼著上下嵌板，模型能講的就是「這道牆填住了帷幕牆留下的那一段外牆面」，帶高多少
        // 不改變這個推論的性質——整層具時效的實體外牆比 90 cm 帶更充分，不是更不充分。
        //
        // 擋住誤放行的是「貼齊」而不是帶高上限：只填一半、浮在中間、與嵌板重疊的牆都拿不到帶長
        // （見上面三條）。這一條與那三條合起來才是完整的守門。
        var panels = new[]
        {
            Panel("P-low", 0, WallLengthMm, 0, bottom: 0, top: StoreyMm),
            Panel("P-high", 0, WallLengthMm, 0, bottom: StoreyMm * 2, top: StoreyMm * 3)
        };
        var host = new CompartmentWallObservation(
            "W-storey", new Point2D(5000, 3000), new Point2D(5000, -50),
            StoreyMm, StoreyMm * 2, CurtainWallJunctionReferences.Article79, 60, "RC 牆 15cm",
            ProvidedFireRating.Rated(120, "120"));
        var set = new CurtainWallObservationSet(
            Package, "LVL", "1F", 0, new[] { Zone() },
            new[] { Wall(panels, top: StoreyMm * 3) },
            new[] { host }, null, new[] { 0.0, StoreyMm, StoreyMm * 2, StoreyMm * 3 });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(StoreyMm, junction.ContinuousFireRatedLengthMm!.Value, 3);
    }

    [Fact]
    public void A_band_wall_carries_no_curtain_panel_in_the_evidence()
    {
        // 帶是牆供給的，這個交點上一片嵌板也沒有。把牆的 UniqueId 塞進 junction.panels 會讓
        // junction.panelCount 說謊，也會讓 ReviewMarkup 把區劃牆本身塗紅（那是它明文不做的事）。
        var junction = Single(Resolve(Set(Wall(GapAt(1200, 2100)), hosts: new[] { Band(5000, 1200, 2100) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Empty(junction.PanelUniqueIds);
        Assert.Equal("W-band", junction.HostUniqueId);
    }

    [Fact]
    public void A_solid_gap_wall_without_a_readable_rating_withholds_the_cw_h_band()
    {
        var host = Band(5000, 1200, 2100, ProvidedFireRating.Missing("參數值為空白"));

        var junction = Single(Resolve(Set(Wall(GapAt(1200, 2100)), hosts: new[] { host })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Null(junction.ContinuousFireRatedLengthMm);
    }

    [Fact]
    public void A_solid_gap_wall_below_the_required_rating_carries_no_band()
    {
        var host = Band(5000, 1200, 2100, ProvidedFireRating.Rated(30, "30"));

        var junction = Single(Resolve(Set(Wall(GapAt(1200, 2100)), hosts: new[] { host })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
    }

    [Fact]
    public void A_rated_wall_at_a_junction_the_curtain_wall_never_panelled_is_not_a_band()
    {
        // 這是守門測試：交點上沒有嵌板不等於「上下帷幕牆之間的間隔」。一道具時效的區劃牆若能單憑
        // 自己有時效就免突出，第79條第3項的但書就形同虛設——沒有上下兩段帷幕牆夾著，照舊算 0。
        var host = new CompartmentWallObservation(
            "W-storey", new Point2D(5000, 3000), new Point2D(5000, -50),
            0, StoreyMm, CurtainWallJunctionReferences.Article79, 60, "RC 牆 15cm",
            ProvidedFireRating.Rated(120, "120"));

        var junction = Single(Resolve(Set(Wall(Array.Empty<CurtainPanelObservation>()), hosts: new[] { host })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
    }

    [Fact]
    public void A_gap_open_at_the_top_is_not_a_band_either()
    {
        // 只有下面那一段帷幕牆：間隔沒有被上面的帷幕牆收邊，不是決議 7 講的那個間隔。
        var panels = new[] { Panel("P-low", 0, WallLengthMm, 0, bottom: 0, top: 1200) };

        var junction = Single(Resolve(Set(Wall(panels), hosts: new[] { Band(5000, 1200, 2100) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
    }

    [Fact]
    public void Two_separate_curtain_walls_stacked_around_the_band_produce_no_cw_h_junction_at_all()
    {
        // 決議 7 只在「同一片帷幕牆、中間那一柱沒有嵌板」時啟動。上下若是兩個獨立的帷幕牆元素，
        // 那道實體牆的高程範圍與任一片都不重疊，WallJunction 在求交點之前就 return null——CW-H
        // 交接處根本不存在，不是判 未符合 而是整列消失。這是本工具目前的限制，記在文件 §9。
        var band = Band(5000, 1200, 2100);
        var lower = new CurtainWallObservation(
            "CW-low", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, -1), OffsetMm, 0, 1200,
            new[] { Panel("P-low", 0, WallLengthMm, 0, bottom: 0, top: 1200) }, null, null, "帷幕牆 下段");
        var upper = new CurtainWallObservation(
            "CW-high", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, -1), OffsetMm, 2100, StoreyMm,
            new[] { Panel("P-high", 0, WallLengthMm, 0, bottom: 2100, top: StoreyMm) }, null, null, "帷幕牆 上段");

        var set = new CurtainWallObservationSet(
            Package, "LVL", "1F", 0, new[] { Zone() }, new[] { lower, upper }, new[] { band }, null,
            new[] { 0.0, StoreyMm });

        Assert.DoesNotContain(Resolve(set), j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall);
    }

    [Fact]
    public void A_junction_on_the_end_mullion_measures_the_panel_beside_it_rather_than_no_panel_at_all()
    {
        // 嵌板的沿牆範圍從豎框內緣起算，所以抵在帷幕牆端點的區劃牆，交點（0 mm）不被任何嵌板覆蓋。
        // 那是豎框佔著這個位置，不是「這一柱沒有嵌板」：查詢位置要移到旁邊那片嵌板上，量它的連續段，
        // 而不是讓決議 7 的守門在一片全玻璃的立面上啟動、回頭供給 0。區劃牆對齊豎框是常態做法。
        var wall = Wall(
            new[] { Panel("P-mullion", 30, 2970, 60) },
            new[] { Grid("G1", CurtainGridLineDirection.Vertical, 3000) });

        var junction = Single(Resolve(Set(wall, hosts: new[] { Host(0) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(2940.0, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.Contains("P-mullion", junction.PanelUniqueIds);
    }

    [Fact]
    public void A_column_the_curtain_wall_really_left_unpanelled_does_not_borrow_the_next_column_s_panel()
    {
        // 上一條的反面，也是它的守門：交點落在「整格都沒有嵌板」的那一柱時，不得吸附到鄰格的實板。
        // grid line 界定的那一格內確實沒有嵌板，這才是決議 7 所稱的間隔——照舊供給 0，證據裡沒有嵌板。
        var wall = Wall(
            new[] { Panel("P-left", 30, 2970, 60), Panel("P-right", 6030, 11970, 60) },
            new[]
            {
                Grid("G1", CurtainGridLineDirection.Vertical, 3000),
                Grid("G2", CurtainGridLineDirection.Vertical, 6000)
            });

        var junction = Single(Resolve(Set(wall, hosts: new[] { Host(4500) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
        Assert.Empty(junction.PanelUniqueIds);
    }

    [Fact]
    public void A_continuous_run_is_the_whole_panel_around_the_point_not_half_of_it_each_side()
    {
        // 案例 7：交點落在實板右緣，左側 900 mm、右側 0 mm — 採總和，仍然成立。
        var wall = Wall(new[]
        {
            Panel("P-solid", 4100, 5000, 60),
            Panel("P-glass", 5000, 9000, 0)
        });

        var junction = Single(Resolve(Set(wall, hosts: new[] { Host(5000) })), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(900, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.False(junction.IsDoubtful);
    }

    [Fact]
    public void A_panel_below_the_hosts_required_rating_does_not_count_towards_the_run()
    {
        // 案例 5：交接帶夠長，但交點所在的實板只有 30 min，達不到區劃牆要求的 60 min。
        var wall = Wall(new[] { Panel("P-30", 4000, 6000, 30) });

        var junction = Single(Resolve(Set(wall, hosts: new[] { Host(5000) })), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
        Assert.Equal(30, junction.MinFireRating!.Minutes!.Value);
    }

    [Fact]
    public void An_unprotected_opening_stops_the_run_where_it_sits()
    {
        // 案例 6：交點就在未受防護的可開啟嵌板上。
        var wall = Wall(new[]
        {
            Panel("P-left", 3000, 4550, 60),
            Panel("P-window", 4550, 5450, 60, isOpening: true, protection: ProvidedFireProtection.No("0")),
            Panel("P-right", 5450, 7000, 60)
        });

        var junction = Single(Resolve(Set(wall, hosts: new[] { Host(5000) })), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
        Assert.True(junction.HasUnprotectedOpening);
    }

    [Fact]
    public void A_protected_opening_in_the_band_is_not_a_break()
    {
        var wall = Wall(new[]
        {
            Panel("P-door", 4000, 6000, 60, isOpening: true, protection: ProvidedFireProtection.Yes("1"))
        });

        var junction = Single(Resolve(Set(wall, hosts: new[] { Host(5000) })), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(2000, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.False(junction.HasUnprotectedOpening);
    }

    [Fact]
    public void A_panel_the_measurement_looked_at_without_a_rating_withholds_the_run()
    {
        // 案例 14／15：不得以其他片推定，也不得填 0 — 不供給，讓引擎判資料不足。
        var wall = Wall(new[]
        {
            Panel("P-rated", 4550, 5000, 60),
            Panel("P-unset", 5000, 7000, null)
        });

        var junction = Single(Resolve(Set(wall, hosts: new[] { Host(5000) })), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Null(junction.ContinuousFireRatedLengthMm);
        Assert.Equal(ProvidedFireRatingKind.Missing, junction.MinFireRating!.Kind);
        Assert.Equal(0.0, junction.ProjectionDepthMm!.Value);
    }

    /// <summary>
    /// 案例 15：嵌板類別根本沒綁 防火檢討_設計防火時效。The adapter reads that as an absent parameter,
    /// which is the same 「不知道」 as a blank one — both 90 cm 但書 withhold their measurement rather
    /// than reading it as 0, so the engine lands on 資料不足 and not 未符合.
    /// </summary>
    [Fact]
    public void An_unbound_panel_category_withholds_both_the_length_and_the_height()
    {
        var unbound = ReviewInputAssembler.Rating(ParameterReading.Absent);
        Assert.Equal(ProvidedFireRatingKind.Missing, unbound.Kind);

        var horizontal = Single(
            Resolve(Set(Wall(new[] { Unrated("P-band", 3000, 7000, 0, StoreyMm, unbound) }), hosts: new[] { Host(5000) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Null(horizontal.ContinuousFireRatedLengthMm);
        Assert.Equal(0.0, horizontal.ProjectionDepthMm!.Value);

        var vertical = Single(
            Resolve(SpandrelSet(new[]
            {
                Unrated("S", 0, WallLengthMm, StoreyMm - 450, StoreyMm + 450, unbound),
                Panel("G-low", 0, WallLengthMm, 0, bottom: 0, top: StoreyMm - 450),
                Panel("G-high", 0, WallLengthMm, 0, bottom: StoreyMm + 450, top: StoreyMm * 2)
            })),
            CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Null(vertical.ContinuousFireRatedHeightMm);
        Assert.Equal(0.0, vertical.ProjectionDepthMm!.Value);
    }

    [Fact]
    public void An_unrated_neighbour_is_a_real_break_and_the_run_still_reports()
    {
        // 案例 8c：grid line 一側 60 min 實板、另一側明確無時效的玻璃 — 真實斷點，不是多餘的 grid line。
        var wall = Wall(
            new[]
            {
                Panel("P-solid", 4550, 5000, 60),
                Panel("P-glass", 5000, 9000, 0)
            },
            new[] { Grid("G-1", CurtainGridLineDirection.Vertical, 5000) });

        var junction = Single(Resolve(Set(wall, hosts: new[] { Host(5000) })), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.False(junction.IsDoubtful);
        Assert.Equal(450, junction.ContinuousFireRatedLengthMm!.Value, 3);
    }

    [Fact]
    public void A_band_split_by_a_grid_line_with_both_sides_rated_is_manual_review()
    {
        // 案例 8：兩側皆 60 min，合計已達 900 mm — 疑似多餘的 grid line，訊息要帶出它的 ElementId。
        var wall = Wall(
            new[]
            {
                Panel("P-a", 4550, 5000, 60),
                Panel("P-b", 5000, 5450, 60)
            },
            new[] { Grid("G-1", CurtainGridLineDirection.Vertical, 5000) });

        var junction = Single(Resolve(Set(wall, hosts: new[] { Host(5000) })), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.True(junction.IsDoubtful);
        Assert.Equal(CurtainWallJunctionDoubtKind.SplitByGridLine, junction.Doubt!.Kind);
        Assert.Equal(ReviewStatus.ManualReview, junction.Doubt.Status);
        Assert.Contains("G-1", junction.Doubt.Message);
        Assert.Contains("G-1", junction.Doubt.SubjectUniqueIds);
    }

    [Fact]
    public void Deleting_the_redundant_grid_line_makes_the_same_band_measure()
    {
        // 案例 8b：同一處，嵌板連續之後就是單純的 900 mm。
        var wall = Wall(new[] { Panel("P-one", 4550, 5450, 60) });

        var junction = Single(Resolve(Set(wall, hosts: new[] { Host(5000) })), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.False(junction.IsDoubtful);
        Assert.Equal(900, junction.ContinuousFireRatedLengthMm!.Value, 3);
    }

    [Fact]
    public void A_run_cut_short_by_the_curtain_walls_own_end_is_not_supplied()
    {
        // 立面在這裡接到另一片牆，工具沒讀到它 — 那是不知道，不是不足。
        var wall = Wall(new[] { Panel("P-end", 0, 600, 60) });

        var junction = Single(Resolve(Set(wall, hosts: new[] { Host(300) })), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Null(junction.ContinuousFireRatedLengthMm);
    }

    [Fact]
    public void The_clause_the_compartment_comes_from_is_carried_through()
    {
        // 案例 9：第83條所生的區劃牆，證據要分得出來（docs §2.5）。
        var junction = Single(
            Resolve(Set(Wall(new[] { Panel("P", 4100, 5900, 60) }),
                hosts: new[] { Host(5000, reference: CurtainWallJunctionReferences.Article83) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(CurtainWallJunctionReferences.Article83, junction.HostLegalReference);
        Assert.Equal(1800, junction.ContinuousFireRatedLengthMm!.Value, 3);
    }

    [Fact]
    public void A_host_with_no_required_rating_withholds_the_run_rather_than_counting_panels()
    {
        var junction = Single(
            Resolve(Set(Wall(new[] { Panel("P", 4100, 5900, 60) }), hosts: new[] { Host(5000, required: null) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Null(junction.ContinuousFireRatedLengthMm);
        Assert.Null(junction.HostRequiredFireRatingMinutes);
        Assert.Equal(0.0, junction.ProjectionDepthMm!.Value);
    }

    [Fact]
    public void A_compartment_wall_that_stops_near_the_curtain_wall_is_unresolved_not_ignored()
    {
        var host = new CompartmentWallObservation("W1", new Point2D(5000, 3000), new Point2D(5000, 400), 0, StoreyMm);

        var junction = Single(Resolve(Set(Wall(Glazing()), hosts: new[] { host })), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(CurtainWallJunctionDoubtKind.UnresolvedIntersection, junction.Doubt!.Kind);
        Assert.Contains("400", junction.Doubt.Message);
        Assert.Contains("W1", junction.Doubt.SubjectUniqueIds);
    }

    [Fact]
    public void A_compartment_wall_nowhere_near_the_curtain_wall_makes_no_junction()
    {
        var host = new CompartmentWallObservation("W1", new Point2D(5000, 8000), new Point2D(5000, 4000), 0, StoreyMm);

        Assert.DoesNotContain(
            Resolve(Set(Wall(Glazing()), hosts: new[] { host })),
            j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall);
    }

    // --- CW-V：層間帶 ----------------------------------------------------------------------------

    [Fact]
    public void A_spandrel_band_is_measured_up_and_down_from_the_slab()
    {
        // 案例 10：層間實板 900 mm、60 min，樓地板要求 60 min。
        var junction = Single(Resolve(Spandrel(spandrelMinutes: 60)), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(900, junction.ContinuousFireRatedHeightMm!.Value, 3);
        Assert.Equal(CurtainWallJunctionReferences.Article79_3, junction.HostLegalReference);
        Assert.Equal("F1", junction.HostUniqueId);
    }

    [Fact]
    public void A_spandrel_below_the_floors_required_rating_does_not_count()
    {
        // 案例 11：同樣 900 mm，但該樓層樓地板要求 120 min。
        var junction = Single(Resolve(Spandrel(spandrelMinutes: 60, required: 120)), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedHeightMm!.Value);
    }

    [Fact]
    public void A_slab_that_projects_is_measured_by_how_far_it_reaches_past_the_face()
    {
        // 案例 13：樓板外突，層間全玻璃且無時效值 — 突出量照樣供給，但書用不上也無妨。
        var junction = Single(Resolve(Spandrel(spandrelMinutes: null, slabEdgeMm: 600)), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(525, junction.ProjectionDepthMm!.Value, 3);
        Assert.Null(junction.ContinuousFireRatedHeightMm);
    }

    [Fact]
    public void The_most_unfavourable_sample_of_the_band_is_the_one_reported()
    {
        // 立面右半段的層間實板只有 300 mm 高：取樣要抓到它，不能被左半段的 900 mm 蓋過去。
        var panels = new List<CurtainPanelObservation>
        {
            Panel("S-left", 0, 6000, 60, bottom: StoreyMm - 450, top: StoreyMm + 450),
            Panel("S-right", 6000, WallLengthMm, 60, bottom: StoreyMm - 150, top: StoreyMm + 150),
            Panel("G-left-low", 0, 6000, 0, bottom: 0, top: StoreyMm - 450),
            Panel("G-right-low", 6000, WallLengthMm, 0, bottom: 0, top: StoreyMm - 150)
        };

        var junction = Single(Resolve(SpandrelSet(panels)), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(300, junction.ContinuousFireRatedHeightMm!.Value, 3);
    }

    [Fact]
    public void A_curtain_wall_running_past_a_storey_with_no_floor_goes_to_article_79_2()
    {
        // 案例 17：三層連跨挑空 — 不是本項的未符合，是改依第79條之2檢討。
        var set = new CurtainWallObservationSet(Package, "LVL", "1F", 0,
            new[] { Zone() },
            new[] { Wall(Glazing(top: StoreyMm * 3), top: StoreyMm * 3) },
            levelElevationsMm: new[] { 0.0, StoreyMm, StoreyMm * 2, StoreyMm * 3 });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(CurtainWallJunctionDoubtKind.VerticalCompartmentSpace, junction.Doubt!.Kind);
        Assert.Equal(ReviewStatus.NotApplicable, junction.Doubt.Status);
        Assert.Contains("連跨 3 個樓層", junction.Doubt.Message);
        Assert.Contains(CurtainWallJunctionReferences.Article79_2, junction.Doubt.Message);
    }

    [Fact]
    public void A_storey_whose_floor_reaches_the_curtain_wall_is_not_a_vertical_space()
    {
        var set = SpandrelSet(new[]
        {
            Panel("S", 0, WallLengthMm, 60, bottom: StoreyMm - 450, top: StoreyMm + 450),
            Panel("G", 0, WallLengthMm, 0, bottom: 0, top: StoreyMm - 450)
        });

        Assert.DoesNotContain(Resolve(set), j => j.Doubt?.Kind == CurtainWallJunctionDoubtKind.VerticalCompartmentSpace);
    }

    // --- CW-O、非平面、區劃歸屬 -------------------------------------------------------------------

    [Fact]
    public void Panels_outside_every_band_are_reviewed_as_other_exterior_wall()
    {
        var junction = Single(Resolve(Spandrel(spandrelMinutes: 60)), CurtainWallJunctionKind.CurtainPanelOther);

        Assert.Equal(ProvidedFireRatingKind.Rated, junction.MinFireRating!.Kind);
        Assert.Equal(0.0, junction.MinFireRating.Minutes!.Value);
        Assert.Null(junction.HostUniqueId);
        Assert.DoesNotContain("S", junction.PanelUniqueIds);
    }

    [Fact]
    public void A_curved_curtain_wall_is_manual_review_for_both_measured_clauses()
    {
        // 案例 18：曲面帷幕牆 — 平面量不出來的兩項轉人工覆核，第79條之4 仍可由嵌板時效回答。
        var wall = Wall(new[] { Panel("P", 0, WallLengthMm, 30) }, nonPlanarReason: "為曲面帷幕牆");
        var junctions = Resolve(Set(wall, hosts: new[] { Host(5000) }));

        Assert.All(
            junctions.Where(j => j.Kind != CurtainWallJunctionKind.CurtainPanelOther),
            j => Assert.Equal(CurtainWallJunctionDoubtKind.NonPlanarCurtainWall, j.Doubt!.Kind));
        Assert.Equal(2, junctions.Count(j => j.IsDoubtful));
        Assert.Contains(junctions, j => j.Kind == CurtainWallJunctionKind.CurtainPanelOther && !j.IsDoubtful);
    }

    [Fact]
    public void A_junction_that_falls_in_no_reviewed_zone_is_left_out()
    {
        var set = new CurtainWallObservationSet(Package, "LVL", "1F", 0,
            zones: Array.Empty<CurtainWallZoneObservation>(),
            curtainWalls: new[] { Wall(Glazing()) },
            compartmentWalls: new[] { Host(5000) },
            levelElevationsMm: new[] { 0.0, StoreyMm });

        Assert.Empty(Resolve(set));
    }

    [Fact]
    public void Every_junction_belongs_to_the_zone_behind_the_facade()
    {
        var junctions = Resolve(Spandrel(spandrelMinutes: 60));

        Assert.NotEmpty(junctions);
        Assert.All(junctions, j => Assert.Equal(ZoneId, j.ZoneId));
    }

    [Fact]
    public void The_same_model_resolves_to_the_same_junctions_in_the_same_order()
    {
        var set = Spandrel(spandrelMinutes: 60);

        Assert.Equal(
            Resolve(set).Select(j => j.JunctionId).ToList(),
            Resolve(set).Select(j => j.JunctionId).ToList());
    }

    [Fact]
    public void Resolved_junctions_feed_the_check_inputs_without_further_conversion()
    {
        var inputs = new CurtainWallJunctionInputs(null, Resolve(Spandrel(spandrelMinutes: 60)));

        Assert.NotEmpty(inputs.Junctions);
        Assert.All(inputs.Junctions, j => Assert.Equal(ZoneId, j.ZoneId));
    }

    // --- 標示位置（docs §7.1：標示層從結果重建，位置必須由幾何層交出）-------------------------------

    [Fact]
    public void A_wall_junction_records_the_intersection_as_the_point_its_measurements_are_annotated_at()
    {
        var junction = Single(Resolve(Set(Wall(Glazing()), hosts: new[] { Host(5000) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        var placement = junction.Placement!;
        Assert.True(placement.IsPoint);
        Assert.Equal(5000, placement.StartMm.X, 3);
        Assert.Equal(0, placement.StartMm.Y, 3);
        Assert.Equal(0, placement.BottomElevationMm, 3);
        Assert.Equal(StoreyMm, placement.TopElevationMm, 3);
    }

    [Fact]
    public void A_spandrel_records_the_band_the_floor_edge_spans_900_mm_above_and_below_the_slab()
    {
        var junction = Single(Resolve(Spandrel(spandrelMinutes: 30)), CurtainWallJunctionKind.FloorToCurtainWall);

        var placement = junction.Placement!;
        Assert.False(placement.IsPoint);
        Assert.Equal(0, placement.StartMm.X, 3);
        Assert.Equal(WallLengthMm, placement.EndMm.X, 3);
        Assert.Equal(StoreyMm - 900, placement.BottomElevationMm, 3);
        Assert.Equal(StoreyMm + 900, placement.TopElevationMm, 3);
    }

    [Fact]
    public void A_band_is_clipped_to_the_curtain_wall_rather_than_running_off_its_top()
    {
        // 帷幕牆只到 4000，樓板在 3600：層間帶的上緣停在牆頂，不是 4500。
        var panels = new[] { Panel("S", 0, WallLengthMm, 30, bottom: 0, top: 4000) };
        var floor = new CompartmentFloorObservation("F1",
            new[] { Rectangle(-500, -50, WallLengthMm + 500, 8000) }, StoreyMm, 60);
        var set = new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm,
            new[] { Zone() }, new[] { Wall(panels, top: 4000) }, compartmentFloors: new[] { floor },
            levelElevationsMm: new[] { 0.0, StoreyMm });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(4000, junction.Placement!.TopElevationMm, 3);
        Assert.Equal(StoreyMm - 900, junction.Placement!.BottomElevationMm, 3);
    }

    [Fact]
    public void A_placement_survives_the_trip_through_the_evidence_it_is_stored_in()
    {
        var placement = Single(Resolve(Spandrel(spandrelMinutes: 30)), CurtainWallJunctionKind.FloorToCurtainWall).Placement!;

        Assert.True(CurtainWallJunctionPlacement.TryParseEvidence(placement.ToEvidenceText(), out var read));
        Assert.Equal(placement.StartMm.X, read!.StartMm.X, 3);
        Assert.Equal(placement.EndMm.X, read.EndMm.X, 3);
        Assert.Equal(placement.BottomElevationMm, read.BottomElevationMm, 3);
        Assert.Equal(placement.TopElevationMm, read.TopElevationMm, 3);
        Assert.False(CurtainWallJunctionPlacement.TryParseEvidence("1,2,3", out _));
        Assert.False(CurtainWallJunctionPlacement.TryParseEvidence("1,2,3,4,5,5", out _));
    }

    [Fact]
    public void A_band_walks_its_rectangle_once_so_the_adapter_can_draw_it_without_sorting_anything()
    {
        // 步驟 6b-2: the Filled Region the elevation shows is built straight from these four corners,
        // so the order has to close the rectangle — bottom along the façade, then up and back.
        var placement = Single(Resolve(Spandrel(spandrelMinutes: 30)), CurtainWallJunctionKind.FloorToCurtainWall).Placement!;

        var corners = placement.Corners();

        Assert.Equal(4, corners.Count);
        Assert.Equal(new[]
        {
            new PlacementCorner(placement.StartMm, placement.BottomElevationMm),
            new PlacementCorner(placement.EndMm, placement.BottomElevationMm),
            new PlacementCorner(placement.EndMm, placement.TopElevationMm),
            new PlacementCorner(placement.StartMm, placement.TopElevationMm)
        }, corners);
    }

    [Fact]
    public void Other_panels_have_no_placement_because_they_are_a_set_of_panels_not_a_place()
    {
        var junction = Single(Resolve(Set(Wall(Glazing()), hosts: new[] { Host(5000) })),
            CurtainWallJunctionKind.CurtainPanelOther);

        Assert.Null(junction.Placement);
    }

    // --- fixtures ---------------------------------------------------------------------------------

    private static IReadOnlyList<CurtainWallJunction> Resolve(CurtainWallObservationSet set) =>
        CurtainWallJunctionResolver.Resolve(set);

    private static CurtainWallJunction Single(IEnumerable<CurtainWallJunction> junctions, CurtainWallJunctionKind kind) =>
        Assert.Single(junctions, j => j.Kind == kind);

    private static CurtainWallZoneObservation Zone() =>
        new(ZoneId, "A 區劃", new[] { Rectangle(-2000, 1, WallLengthMm + 2000, 20000) });

    private static IReadOnlyList<Point2D> Rectangle(double x0, double y0, double x1, double y1) =>
        new[] { new Point2D(x0, y0), new Point2D(x1, y0), new Point2D(x1, y1), new Point2D(x0, y1) };

    private static CurtainPanelObservation Panel(
        string uniqueId,
        double startMm,
        double endMm,
        double? minutes,
        double bottom = 0,
        double top = StoreyMm,
        bool isOpening = false,
        ProvidedFireProtection? protection = null) =>
        new(uniqueId, startMm, endMm, bottom, top,
            minutes is double m ? ProvidedFireRating.Rated(m, m.ToString("0")) : ProvidedFireRating.Missing("參數值為空白"),
            isOpening, protection);

    /// <summary>A panel whose rating is whatever the adapter made of the parameter — absent, blank or a value.</summary>
    private static CurtainPanelObservation Unrated(
        string uniqueId, double startMm, double endMm, double bottom, double top, ProvidedFireRating rating) =>
        new(uniqueId, startMm, endMm, bottom, top, rating, false, null);

    private static CurtainGridLineObservation Grid(string uniqueId, CurtainGridLineDirection direction, double positionMm) =>
        new(uniqueId, direction, positionMm);

    private static IReadOnlyList<CurtainPanelObservation> Glazing(double top = StoreyMm) =>
        new[] { Panel("P-glass", 0, WallLengthMm, 0, top: top) };

    /// <summary>
    /// 下面帷幕牆與上面帷幕牆，中間留出 <paramref name="bottomMm"/>–<paramref name="topMm"/> 的間隔：
    /// 決議 7 的實體牆防火帶就建在這一段裡。兩段都是不具時效的玻璃，帶長只能來自填入的牆。
    /// </summary>
    private static IReadOnlyList<CurtainPanelObservation> GapAt(double bottomMm, double topMm) =>
        new[]
        {
            Panel("P-low", 0, WallLengthMm, 0, bottom: 0, top: bottomMm),
            Panel("P-high", 0, WallLengthMm, 0, bottom: topMm, top: StoreyMm)
        };

    /// <summary>The real opaque wall built into that gap, reaching the curtain wall from inside.</summary>
    private static CompartmentWallObservation Band(
        double atMm,
        double bottomMm,
        double topMm,
        ProvidedFireRating? rating = null) =>
        new("W-band", new Point2D(atMm, 3000), new Point2D(atMm, -50), bottomMm, topMm,
            CurtainWallJunctionReferences.Article79, 60, "RC 牆 15cm",
            rating ?? ProvidedFireRating.Rated(60, "60"));

    private static CurtainWallObservation Wall(
        IEnumerable<CurtainPanelObservation> panels,
        IEnumerable<CurtainGridLineObservation>? gridLines = null,
        double top = StoreyMm,
        string? nonPlanarReason = null) =>
        new("CW1", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, -1), OffsetMm, 0, top,
            panels, gridLines, nonPlanarReason, "帷幕牆 1");

    /// <summary>A 區劃牆 reaching the curtain wall from inside; <paramref name="beyondLineMm"/> is how far south of the location line it ends.</summary>
    private static CompartmentWallObservation Host(
        double atMm,
        double beyondLineMm = 50,
        double? required = 60,
        string reference = CurtainWallJunctionReferences.Article79) =>
        new("W1", new Point2D(atMm, 3000), new Point2D(atMm, -beyondLineMm), 0, StoreyMm, reference, required);

    private static CurtainWallObservationSet Set(
        CurtainWallObservation wall,
        IEnumerable<CompartmentWallObservation>? hosts = null,
        IEnumerable<CompartmentFloorObservation>? floors = null) =>
        new(Package, "LVL", "1F", 0, new[] { Zone() }, new[] { wall }, hosts, floors,
            new[] { 0.0, StoreyMm });

    /// <summary>
    /// A storey whose slab sits at 3600: a spandrel band straddling it, glazing below, and the storey
    /// above still curtain-walled — so the band is bounded by panels, not by the wall's own edge.
    /// </summary>
    private static CurtainWallObservationSet Spandrel(double? spandrelMinutes, double? required = 60, double slabEdgeMm = 50)
    {
        var panels = new List<CurtainPanelObservation>
        {
            Panel("S", 0, WallLengthMm, spandrelMinutes, bottom: StoreyMm - 450, top: StoreyMm + 450),
            Panel("G-low", 0, WallLengthMm, 0, bottom: 0, top: StoreyMm - 450),
            Panel("G-high", 0, WallLengthMm, 0, bottom: StoreyMm + 450, top: StoreyMm * 2)
        };

        return SpandrelSet(panels, required, slabEdgeMm);
    }

    private static CurtainWallObservationSet SpandrelSet(
        IReadOnlyList<CurtainPanelObservation> panels,
        double? required = 60,
        double slabEdgeMm = 50)
    {
        var floor = new CompartmentFloorObservation("F1",
            new[] { Rectangle(-500, -slabEdgeMm, WallLengthMm + 500, 8000) }, StoreyMm, required);

        return new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm,
            new[] { Zone() },
            new[] { Wall(panels, top: StoreyMm * 2) },
            compartmentFloors: new[] { floor },
            levelElevationsMm: new[] { 0.0, StoreyMm, StoreyMm * 2 });
    }
}
